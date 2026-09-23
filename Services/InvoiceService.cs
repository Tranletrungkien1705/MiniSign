using Microsoft.EntityFrameworkCore;
using MiniSign.Data;
using MiniSign.Models;

namespace MiniSign.Services;

// Kết quả một thao tác trên hóa đơn (tạo/ký/đổi trạng thái).
public record InvoiceResult(bool ok, string msg, Invoice? invoice);

// Thống kê hóa đơn theo trạng thái ký.
public record InvoiceDash(int Total, int Pending, int Processing, int Signed, int Failed);

// Thống kê hóa đơn theo trạng thái vòng đời (port từ InBrand InvoiceStatus).
public record InvoiceLifecycleDash(int Total, int Pending, int Approved, int Issued, int Canceled, int Deleted);

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
    Task<InvoiceResult> ApproveAsync(int id, string invoiceNo, string apprBy);
    Task<InvoiceResult> AllocateAsync(int id, string tInvoiceCode, DateTime invoiceDateUtc, string allocateBy);
    Task<List<InvoiceTemplate>> TemplatesAsync();
    Task<InvoiceResult> IssueAsync(int id, string issuedBy);
    Task<InvoiceResult> CancelAsync(int id, string reason, string cancelBy);
    Task<InvoiceResult> DeleteAsync(int id, string reason, string deleteBy, string? attachedDelFilePath);
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

    // Duyệt hóa đơn (port từ InBrand Invoice_Invoice_ApprovedMultiX / WAS_Invoice_Invoice_Approved).
    // Quy tắc InBrand:
    //  - Hóa đơn phải tồn tại và đang ở trạng thái PENDING (Invoice_Invoice_CheckDB với InvoiceStatus.Pending).
    //  - Số hóa đơn (InvoiceNo) không được rỗng (lỗi Invoice_Invoice_ApprovedMultiX_InvoiceNoIsNotNull).
    //  - Khi duyệt ghi InvoiceStatus = APPROVED + người duyệt (ApprBy) + thời gian duyệt (ApprDTimeUTC).
    public async Task<InvoiceResult> ApproveAsync(int id, string invoiceNo, string apprBy)
    {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null) return new(false, "Không tìm thấy hóa đơn.", null);
        if (inv.Status != InvoiceStatus.Pending)
            return new(false, "Chỉ được duyệt hóa đơn ở trạng thái PENDING.", inv);

        var no = (invoiceNo ?? "").Trim();
        if (no.Length == 0)
            return new(false, "Cần số hóa đơn (InvoiceNo) trước khi duyệt.", inv);

        inv.InvoiceNo = no;
        inv.Status = InvoiceStatus.Approved;
        inv.ApprBy = string.IsNullOrWhiteSpace(apprBy) ? "system" : apprBy.Trim();
        inv.ApprDTimeUTC = DateTime.UtcNow;
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new(true, "Đã duyệt hóa đơn.", inv);
    }

    // Cấp số hóa đơn (port từ InBrand Invoice_Invoice_AllocatedInvX_New20190917).
    // Quy tắc InBrand:
    //  - Hóa đơn phải tồn tại và đang ở trạng thái PENDING (Invoice_Invoice_CheckDB với InvoiceStatus.Pending).
    //  - Hóa đơn chưa được cấp số (InvoiceNo phải rỗng) — lỗi Invoice_Invoice_AllocatedInv_NotAllowAllocatedInv.
    //  - Ngày hóa đơn bắt buộc (Invoice_Invoice_AllocatedInv_InvoiceDateUTCIsNotNull).
    //  - Ngày hóa đơn >= LastInvoiceDateUTC của mẫu (InvalidInvoiceDateUTCBeforeLastInvoiceDateUTC).
    //  - Ngày hóa đơn >= EffDateStart của mẫu (InvalidInvoiceDateUTCBeforeEffDateStart).
    //  - Ngày hóa đơn không được ở tương lai (InvaliInvoiceDateUTCAfterSysDate).
    //  - Số HĐ kế tiếp = StartInvoiceNo + QtyUsed; phải nằm trong [StartInvoiceNo, EndInvoiceNo]
    //    (myCheck_Invoice_TempInvoice_InvoiceNo); sau khi cấp thì QtyUsed++ và LastInvoiceNo = số vừa cấp.
    public async Task<InvoiceResult> AllocateAsync(int id, string tInvoiceCode, DateTime invoiceDateUtc, string allocateBy)
    {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null) return new(false, "Không tìm thấy hóa đơn.", null);
        if (inv.Status != InvoiceStatus.Pending)
            return new(false, "Chỉ được cấp số hóa đơn ở trạng thái PENDING.", inv);
        if (!string.IsNullOrEmpty(inv.InvoiceNo))
            return new(false, "Hóa đơn đã có số — không cấp lại.", inv);

        var code = (tInvoiceCode ?? "").Trim();
        if (code.Length == 0) return new(false, "Cần chọn mẫu số hóa đơn (TInvoiceCode).", inv);

        var tpl = await db.InvoiceTemplates.FirstOrDefaultAsync(t => t.TInvoiceCode == code);
        if (tpl == null) return new(false, $"Không tìm thấy mẫu số hóa đơn {code}.", inv);
        if (!tpl.FlagActive) return new(false, "Mẫu số hóa đơn đã ngừng hiệu lực.", inv);

        var date = invoiceDateUtc.Date;
        if (date == default) return new(false, "Cần ngày hóa đơn (InvoiceDateUTC).", inv);
        if (tpl.LastInvoiceDateUTC.HasValue && date < tpl.LastInvoiceDateUTC.Value.Date)
            return new(false, $"Ngày hóa đơn phải >= ngày cấp số gần nhất ({tpl.LastInvoiceDateUTC.Value:dd/MM/yyyy}).", inv);
        if (date < tpl.EffDateStart.Date)
            return new(false, $"Ngày hóa đơn phải >= ngày hiệu lực mẫu ({tpl.EffDateStart:dd/MM/yyyy}).", inv);
        if (date > DateTime.Now.Date)
            return new(false, "Ngày hóa đơn không được ở tương lai.", inv);

        // Số HĐ kế tiếp = StartInvoiceNo + QtyUsed (port từ AllocatedInvX).
        var nextNo = tpl.StartInvoiceNo + tpl.QtyUsed;
        if (nextNo < tpl.StartInvoiceNo || nextNo > tpl.EndInvoiceNo)
            return new(false, $"Dải số đã hết (Start={tpl.StartInvoiceNo}, End={tpl.EndInvoiceNo}, đã dùng={tpl.QtyUsed}).", inv);

        var invoiceNo = nextNo.ToString("D7");
        tpl.QtyUsed += 1;
        tpl.LastInvoiceNo = invoiceNo;
        tpl.LastInvoiceDateUTC = date;

        inv.TInvoiceCode = code;
        inv.InvoiceNo = invoiceNo;
        inv.InvoiceDateUTC = date;
        inv.InvoiceNoBy = string.IsNullOrWhiteSpace(allocateBy) ? "system" : allocateBy.Trim();
        inv.InvoiceNoDTimeUTC = DateTime.UtcNow;
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new(true, $"Đã cấp số hóa đơn {invoiceNo}.", inv);
    }

    public Task<List<InvoiceTemplate>> TemplatesAsync() =>
        db.InvoiceTemplates.OrderBy(t => t.TInvoiceCode).ToListAsync();

    // Phát hành hóa đơn (port từ InBrand Invoice_Invoice_IssuedXMulti / WAS_Invoice_Invoice_Issued).
    // Quy tắc InBrand:
    //  - Hóa đơn phải tồn tại và đang ở trạng thái APPROVED (Invoice_Invoice_CheckDB với InvoiceStatus.Approved).
    //  - Khi phát hành ghi InvoiceStatus = ISSUED + người phát hành (IssuedBy) + thời gian phát hành (IssuedDTimeUTC).
    public async Task<InvoiceResult> IssueAsync(int id, string issuedBy)
    {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null) return new(false, "Không tìm thấy hóa đơn.", null);
        if (inv.Status != InvoiceStatus.Approved)
            return new(false, "Chỉ được phát hành hóa đơn ở trạng thái APPROVED.", inv);

        inv.Status = InvoiceStatus.Issued;
        inv.IssuedBy = string.IsNullOrWhiteSpace(issuedBy) ? "system" : issuedBy.Trim();
        inv.IssuedDTimeUTC = DateTime.UtcNow;
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new(true, "Đã phát hành hóa đơn.", inv);
    }

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

    // Xóa hóa đơn (port từ InBrand Invoice_Invoice_Deleted / Invoice_Invoice_DeletedX_New20190715).
    // Quy tắc InBrand:
    //  - Hóa đơn phải tồn tại và đang ở trạng thái ISSUED (đã phát hành) mới được xóa.
    //  - Nếu hóa đơn đã ở trạng thái DELETED thì bỏ qua (idempotent — không báo lỗi).
    //  - Khi xóa ghi InvoiceStatus = DELETED + người xóa (DeleteBy) + thời gian xóa (DeleteDTimeUTC)
    //    + lý do xóa (DeleteReason) + file đính kèm (AttachedDelFilePath).
    public async Task<InvoiceResult> DeleteAsync(int id, string reason, string deleteBy, string? attachedDelFilePath)
    {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null) return new(false, "Không tìm thấy hóa đơn.", null);

        // Đã xóa rồi → bỏ qua (idempotent, port từ nhánh "if InvoiceStatus != DELETED" của InBrand).
        if (inv.Status == InvoiceStatus.Deleted)
            return new(true, "Hóa đơn đã ở trạng thái DELETED.", inv);

        if (inv.Status != InvoiceStatus.Issued)
            return new(false, "Chỉ được xóa hóa đơn ở trạng thái ISSUED (đã phát hành).", inv);

        inv.Status = InvoiceStatus.Deleted;
        inv.DeleteReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        inv.DeleteBy = string.IsNullOrWhiteSpace(deleteBy) ? "system" : deleteBy.Trim();
        inv.DeleteDTimeUTC = DateTime.UtcNow;
        inv.AttachedDelFilePath = string.IsNullOrWhiteSpace(attachedDelFilePath) ? null : attachedDelFilePath.Trim();
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new(true, "Đã xóa hóa đơn.", inv);
    }

    public async Task<InvoiceLifecycleDash> LifecycleDashboardAsync() => new(
        await db.Invoices.CountAsync(),
        await db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Pending),
        await db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Approved),
        await db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Issued),
        await db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Canceled),
        await db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Deleted));
}
