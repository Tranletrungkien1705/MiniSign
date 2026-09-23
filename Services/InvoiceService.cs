using Microsoft.EntityFrameworkCore;
using MiniSign.Data;
using MiniSign.Models;

namespace MiniSign.Services;

// Kết quả một thao tác trên hóa đơn (tạo/ký/đổi trạng thái).
public record InvoiceResult(bool ok, string msg, Invoice? invoice);

// Thống kê hóa đơn theo trạng thái ký.
public record InvoiceDash(int Total, int Pending, int Processing, int Signed, int Failed);

// Thống kê hóa đơn theo trạng thái vòng đời (port từ InBrand InvoiceStatus).
public record InvoiceLifecycleDash(int Total, int Pending, int Approved, int Issued, int Canceled);

// Nghiệp vụ ký hóa đơn/tài liệu theo vòng đời trạng thái.
// Port từ InBrand OS_Invoice_InvoiceTemp + OS_Invoice_InvoiceTemp_UpdMultiSignStatus:
//  - Hóa đơn có SignStatus (PENDING/PROCESSING/SIGNED/FAILED).
//  - Chỉ được phát hành/ký lại khi lần call trước ở trạng thái FAILED hoặc PROCESSING
//    (InBrand: "Check chỉ được phát hành tiếp khi lời call trước ở trạng thái Failed,Processing").
public interface IInvoiceService
{
    Task<List<Invoice>> ListAsync(SignStatus? status);
    Task<Invoice?> GetAsync(int id);
    Task<Invoice?> GetByCodeAsync(string invoiceCode);
    Task<InvoiceResult> CreateAsync(string invoiceCode, string taxCode, string customerName, string content, decimal totalValPmt);
    Task<InvoiceResult> SignAsync(int id, int certId, string signBy);
    Task<InvoiceResult> SetStatusAsync(int id, SignStatus status, string? error);
    Task<InvoiceResult> CancelAsync(int id, string reason, string cancelBy);
    Task<InvoiceDash> DashboardAsync();
    Task<InvoiceLifecycleDash> LifecycleDashboardAsync();
}

public class InvoiceService(AppDbContext db, ISignService sign) : IInvoiceService
{
    public Task<List<Invoice>> ListAsync(SignStatus? status)
    {
        var q = db.Invoices.AsQueryable();
        if (status.HasValue) q = q.Where(i => i.SignStatus == status.Value);
        return q.OrderByDescending(i => i.Id).Take(300).ToListAsync();
    }

    public Task<Invoice?> GetAsync(int id) => db.Invoices.FirstOrDefaultAsync(i => i.Id == id);

    public Task<Invoice?> GetByCodeAsync(string invoiceCode) =>
        db.Invoices.FirstOrDefaultAsync(i => i.InvoiceCode == (invoiceCode ?? "").Trim());

    public async Task<InvoiceResult> CreateAsync(string invoiceCode, string taxCode, string customerName, string content, decimal totalValPmt)
    {
        var code = (invoiceCode ?? "").Trim();
        if (code.Length == 0) return new(false, "Cần số tra cứu (InvoiceCode).", null);
        if (string.IsNullOrWhiteSpace(content)) return new(false, "Cần nội dung hóa đơn.", null);
        if (await db.Invoices.AnyAsync(i => i.InvoiceCode == code))
            return new(false, $"Đã tồn tại hóa đơn {code}.", null);

        var inv = new Invoice
        {
            InvoiceCode = code,
            TaxCode = (taxCode ?? "").Trim(),
            CustomerName = (customerName ?? "").Trim(),
            Content = content,
            TotalValPmt = totalValPmt,
            SignStatus = SignStatus.Pending
        };
        db.Invoices.Add(inv); await db.SaveChangesAsync();
        return new(true, "Đã tạo hóa đơn (chờ ký).", inv);
    }

    // Ký hóa đơn: chỉ cho phép khi trạng thái hiện tại là PENDING/FAILED/PROCESSING
    // (tức chưa ký thành công). Sau khi ký thành công → SIGNED, ghi người ký + thời gian + serial.
    public async Task<InvoiceResult> SignAsync(int id, int certId, string signBy)
    {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null) return new(false, "Không tìm thấy hóa đơn.", null);
        if (inv.SignStatus == SignStatus.Signed)
            return new(false, "Hóa đơn đã ký — không ký lại.", inv);

        // Đánh dấu đang xử lý (PROCESSING) trước khi ký.
        inv.SignStatus = SignStatus.Processing;
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var r = await sign.SignAsync(certId, inv.InvoiceCode, inv.Content, SignAlgorithm.SHA1withRSA);
        if (!r.ok)
        {
            inv.SignStatus = SignStatus.Failed;
            inv.SignError = r.msg;
            inv.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return new(false, r.msg, inv);
        }

        inv.SignStatus = SignStatus.Signed;
        inv.SignBy = string.IsNullOrWhiteSpace(signBy) ? "system" : signBy.Trim();
        inv.SignDTimeUTC = DateTime.UtcNow;
        inv.SignSerial = r.serial;
        inv.Signature = r.signature;
        inv.SignError = null;
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new(true, "Đã ký hóa đơn thành công.", inv);
    }

    // Đổi trạng thái ký thủ công (port từ UpdMultiSignStatus).
    // Quy tắc InBrand: chỉ được phát hành/ký tiếp khi lần call trước ở FAILED hoặc PROCESSING.
    public async Task<InvoiceResult> SetStatusAsync(int id, SignStatus status, string? error)
    {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null) return new(false, "Không tìm thấy hóa đơn.", null);

        if (status == SignStatus.Processing && inv.SignStatus != SignStatus.Failed && inv.SignStatus != SignStatus.Processing)
            return new(false, "Chỉ được phát hành tiếp khi lần call trước ở trạng thái FAILED hoặc PROCESSING.", inv);

        inv.SignStatus = status;
        inv.SignError = status == SignStatus.Failed ? (error ?? "Lỗi không xác định.") : null;
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new(true, $"Đã cập nhật trạng thái ký: {status}.", inv);
    }

    public async Task<InvoiceDash> DashboardAsync() => new(
        await db.Invoices.CountAsync(),
        await db.Invoices.CountAsync(i => i.SignStatus == SignStatus.Pending),
        await db.Invoices.CountAsync(i => i.SignStatus == SignStatus.Processing),
        await db.Invoices.CountAsync(i => i.SignStatus == SignStatus.Signed),
        await db.Invoices.CountAsync(i => i.SignStatus == SignStatus.Failed));

    // Hủy hóa đơn (port từ InBrand Invoice_Invoice_Cancel / Invoice_Invoice_CancelX_New20190705).
    // Quy tắc InBrand: hóa đơn phải tồn tại và đang ở trạng thái PENDING hoặc APPROVED mới được hủy;
    // khi hủy ghi InvoiceStatus = CANCELED + người hủy + thời gian hủy + lý do (Remark).
    public async Task<InvoiceResult> CancelAsync(int id, string reason, string cancelBy)
    {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null) return new(false, "Không tìm thấy hóa đơn.", null);
        if (inv.Status != InvoiceStatus.Pending && inv.Status != InvoiceStatus.Approved)
            return new(false, "Chỉ được hủy hóa đơn ở trạng thái PENDING hoặc APPROVED.", inv);

        inv.Status = InvoiceStatus.Canceled;
        inv.CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        inv.CancelBy = string.IsNullOrWhiteSpace(cancelBy) ? "system" : cancelBy.Trim();
        inv.CancelDTimeUTC = DateTime.UtcNow;
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new(true, "Đã hủy hóa đơn.", inv);
    }

    public async Task<InvoiceLifecycleDash> LifecycleDashboardAsync() => new(
        await db.Invoices.CountAsync(),
        await db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Pending),
        await db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Approved),
        await db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Issued),
        await db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Canceled));
}
