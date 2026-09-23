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

// Dữ liệu cập nhật hóa đơn SAU KHI CẤP SỐ (port từ InBrand Invoice_Invoice_UpdAfterAllocatedX).
// Gồm phương thức thanh toán, thông tin khách hàng và bảng kê giá trị theo thuế suất.
public record InvoiceUpdateReq(
    string? PaymentMethodCode,
    string? CustomerNNTCode,
    string? CustomerNNTName,
    string? CustomerNNTAddress,
    string? CustomerNNTPhone,
    string? CustomerNNTBankName,
    string? CustomerNNTEmail,
    string? CustomerNNTAccNo,
    string? CustomerNNTBuyerName,
    string? CustomerMST,
    DateTime? InvoiceDateUTC,
    decimal TotalValInvoice,
    decimal TotalValVAT,
    decimal TotalValPmt,
    decimal ValGoodsNotTaxable,
    decimal ValGoodsNotChargeTax,
    decimal ValGoodsVAT5,
    decimal ValVAT5,
    decimal ValGoodsVAT10,
    decimal ValVAT10);

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
    Task<InvoiceResult> ChangeAsync(int id, string reason, string changeBy);
    Task<InvoiceResult> AdjustAsync(int id, string refNo, SourceInvoiceCode source, InvoiceAdjType adjType, string reason, string adjustBy);
    Task<InvoiceResult> UpdateAfterAllocatedAsync(int id, InvoiceUpdateReq req);
    Task<InvoiceResult> MarkMailSentAsync(int id, string sendBy);
    Task<InvoiceResult> PushOutSiteAsync(int id, string pushBy);
    Task<List<InvoiceLine>> LinesAsync(int invoiceId);
    Task<InvoiceResult> AddLineAsync(int invoiceId, InvoiceLineReq req);
    Task<InvoiceTotalCheck> CheckTotalAsync(int invoiceId, VatType vatType);
    Task<InvoiceDash> DashboardAsync();
    Task<InvoiceLifecycleDash> LifecycleDashboardAsync();
}

// Dữ liệu một dòng chi tiết hóa đơn (port từ InBrand Invoice_InvoiceDtl).
public record InvoiceLineReq(
    int Idx,
    string? SpecCode,
    string? SpecName,
    decimal Qty,
    decimal UnitPrice,
    decimal VATRate,
    decimal ValInvoice,
    decimal ValTax);

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

    // Đánh dấu hóa đơn đã bị THAY THẾ/ĐIỀU CHỈNH (port từ InBrand Invoice_Invoice_ChangeX).
    // Quy tắc InBrand:
    //  - Hóa đơn phải tồn tại và đang ở trạng thái ISSUED (Invoice_Invoice_CheckDB với InvoiceStatus.Issued).
    //  - FlagChange phải đang Active (chưa bị thay thế) — lỗi Invoice_Invoice_Change_InvalidFlagChange.
    //  - Khi thay thế ghi FlagChange = Inactive + người thay thế (ChangeBy) + thời gian (ChangeDTimeUTC) + lý do (Remark).
    public async Task<InvoiceResult> ChangeAsync(int id, string reason, string changeBy)
    {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null) return new(false, "Không tìm thấy hóa đơn.", null);
        if (inv.Status != InvoiceStatus.Issued)
            return new(false, "Chỉ được đánh dấu thay thế hóa đơn ở trạng thái ISSUED (đã phát hành).", inv);
        if (!inv.FlagChange)
            return new(false, "Hóa đơn đã bị thay thế trước đó (FlagChange không còn Active).", inv);

        inv.FlagChange = false;
        inv.ChangeReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        inv.ChangeBy = string.IsNullOrWhiteSpace(changeBy) ? "system" : changeBy.Trim();
        inv.ChangeDTimeUTC = DateTime.UtcNow;
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new(true, "Đã đánh dấu hóa đơn bị thay thế.", inv);
    }

    // Đánh dấu hóa đơn là ĐIỀU CHỈNH / THAY THẾ một hóa đơn khác (port từ InBrand
    // Invoice_Invoice_Save_Adj / Invoice_Invoice_Save_Replace → Invoice_Invoice_SaveX).
    // Quy tắc InBrand:
    //  - InvoiceAdjType = AdjIncrease/AdjDecrease → bắt buộc có RefNo (Invoice_Invoice_SaveX_InvoiceAdjTypeIsNotNull).
    //  - SourceInvoiceCode = Adj → hóa đơn RefNo phải tồn tại và ở trạng thái ISSUED.
    //  - SourceInvoiceCode = Replace → hóa đơn RefNo phải tồn tại và ở trạng thái DELETED.
    //  - Một hóa đơn chỉ được điều chỉnh/thay thế MỘT lần (myCheck_Invoice_Invoice_RefNo:
    //    "1 hóa đơn đã điều chỉnh không được phép điều chỉnh tiếp").
    public async Task<InvoiceResult> AdjustAsync(int id, string refNo, SourceInvoiceCode source, InvoiceAdjType adjType, string reason, string adjustBy)
    {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null) return new(false, "Không tìm thấy hóa đơn.", null);

        var refCode = (refNo ?? "").Trim();

        // Loại điều chỉnh Tăng/Giảm bắt buộc phải có hóa đơn gốc (RefNo).
        if ((adjType == InvoiceAdjType.AdjIncrease || adjType == InvoiceAdjType.AdjDecrease) && refCode.Length == 0)
            return new(false, "Điều chỉnh Tăng/Giảm bắt buộc phải có số tra cứu hóa đơn gốc (RefNo).", inv);

        if (refCode.Length > 0)
        {
            var refInv = await db.Invoices.FirstOrDefaultAsync(i => i.InvoiceCode == refCode);
            if (refInv == null) return new(false, $"Không tìm thấy hóa đơn gốc {refCode}.", inv);

            // Hóa đơn điều chỉnh: hóa đơn gốc phải đã PHÁT HÀNH (ISSUED).
            if (source == SourceInvoiceCode.Adj && refInv.Status != InvoiceStatus.Issued)
                return new(false, "Hóa đơn điều chỉnh chỉ áp dụng cho hóa đơn gốc đã phát hành (ISSUED).", inv);

            // Hóa đơn thay thế: hóa đơn gốc phải đã bị XÓA (DELETED).
            if (source == SourceInvoiceCode.Replace && refInv.Status != InvoiceStatus.Deleted)
                return new(false, "Hóa đơn thay thế chỉ áp dụng cho hóa đơn gốc đã bị xóa (DELETED).", inv);

            // Một hóa đơn gốc chỉ được điều chỉnh/thay thế một lần (trừ hóa đơn điều chỉnh).
            var already = await db.Invoices.AnyAsync(i => i.RefNo == refCode && i.SourceInvoiceCode != SourceInvoiceCode.Adj);
            if (already)
                return new(false, $"Hóa đơn gốc {refCode} đã được điều chỉnh/thay thế trước đó.", inv);
        }

        inv.RefNo = refCode.Length == 0 ? null : refCode;
        inv.SourceInvoiceCode = source;
        inv.InvoiceAdjType = adjType;
        inv.ChangeReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        inv.ChangeBy = string.IsNullOrWhiteSpace(adjustBy) ? "system" : adjustBy.Trim();
        inv.ChangeDTimeUTC = DateTime.UtcNow;
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new(true, "Đã đánh dấu hóa đơn điều chỉnh/thay thế.", inv);
    }

    // Cập nhật hóa đơn SAU KHI CẤP SỐ (port từ InBrand Invoice_Invoice_UpdAfterAllocatedX).
    // Quy tắc InBrand:
    //  - Hóa đơn phải tồn tại và đang ở trạng thái PENDING (Invoice_Invoice_CheckDB với InvoiceStatus.Pending).
    //  - SourceInvoiceCode phải là INVOICEROOT (lỗi Invoice_Invoice_UpdAfterAllocatedX_SourceInvoiceCode).
    //  - Hóa đơn phải đã có số (InvoiceNo not null) — lỗi Invoice_Invoice_UpdAfterAllocatedX_InvalidInvoiceNo.
    //  - Ngày hóa đơn không được ở tương lai (InvaliInvoiceDateUTCAfterSysDate).
    //  - Ngày hóa đơn phải >= ngày của hóa đơn liền trước (InvoiceNo-1) — Invalid_Before_InvoiceDateUTC
    //    và <= ngày của hóa đơn liền sau (InvoiceNo+1) — Invalid_Last_InvoiceDateUTC (cùng mẫu TInvoiceCode).
    //  - Cập nhật phương thức thanh toán, thông tin khách hàng và bảng kê giá trị theo thuế suất.
    public async Task<InvoiceResult> UpdateAfterAllocatedAsync(int id, InvoiceUpdateReq req)
    {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null) return new(false, "Không tìm thấy hóa đơn.", null);
        if (inv.Status != InvoiceStatus.Pending)
            return new(false, "Chỉ cập nhật được hóa đơn ở trạng thái PENDING.", inv);
        if (inv.SourceInvoiceCode != SourceInvoiceCode.Root)
            return new(false, "Chỉ cập nhật được hóa đơn gốc (SourceInvoiceCode = INVOICEROOT).", inv);
        if (string.IsNullOrWhiteSpace(inv.InvoiceNo))
            return new(false, "Hóa đơn chưa được cấp số (InvoiceNo) — không cập nhật được.", inv);

        var date = (req.InvoiceDateUTC ?? inv.InvoiceDateUTC ?? DateTime.Today).Date;
        if (date > DateTime.Now.Date)
            return new(false, "Ngày hóa đơn không được ở tương lai.", inv);

        // Kiểm tra tính nhất quán ngày với hóa đơn liền trước/liền sau trong cùng mẫu số.
        if (!string.IsNullOrWhiteSpace(inv.TInvoiceCode) && long.TryParse(inv.InvoiceNo, out var no))
        {
            var siblings = await db.Invoices
                .Where(i => i.TInvoiceCode == inv.TInvoiceCode && i.Id != inv.Id && i.InvoiceNo != null)
                .Select(i => new { i.InvoiceNo, i.InvoiceDateUTC })
                .ToListAsync();

            DateTime? before = null, last = null;
            foreach (var s in siblings)
            {
                if (!long.TryParse(s.InvoiceNo, out var sn) || !s.InvoiceDateUTC.HasValue) continue;
                if (sn == no - 1) before = s.InvoiceDateUTC.Value.Date;
                if (sn == no + 1) last = s.InvoiceDateUTC.Value.Date;
            }
            if (before.HasValue && before.Value > date)
                return new(false, $"Ngày hóa đơn phải >= ngày hóa đơn liền trước ({before.Value:dd/MM/yyyy}).", inv);
            if (last.HasValue && last.Value < date)
                return new(false, $"Ngày hóa đơn phải <= ngày hóa đơn liền sau ({last.Value:dd/MM/yyyy}).", inv);
        }

        inv.PaymentMethodCode = Trim(req.PaymentMethodCode);
        inv.CustomerNNTCode = Trim(req.CustomerNNTCode);
        inv.CustomerNNTName = Trim(req.CustomerNNTName);
        inv.CustomerNNTAddress = Trim(req.CustomerNNTAddress);
        inv.CustomerNNTPhone = Trim(req.CustomerNNTPhone);
        inv.CustomerNNTBankName = Trim(req.CustomerNNTBankName);
        inv.CustomerNNTEmail = Trim(req.CustomerNNTEmail);
        inv.CustomerNNTAccNo = Trim(req.CustomerNNTAccNo);
        inv.CustomerNNTBuyerName = Trim(req.CustomerNNTBuyerName);
        inv.CustomerMST = Trim(req.CustomerMST);
        inv.InvoiceDateUTC = date;
        inv.TotalValInvoice = req.TotalValInvoice;
        inv.TotalValVAT = req.TotalValVAT;
        inv.TotalValPmt = req.TotalValPmt;
        inv.ValGoodsNotTaxable = req.ValGoodsNotTaxable;
        inv.ValGoodsNotChargeTax = req.ValGoodsNotChargeTax;
        inv.ValGoodsVAT5 = req.ValGoodsVAT5;
        inv.ValVAT5 = req.ValVAT5;
        inv.ValGoodsVAT10 = req.ValGoodsVAT10;
        inv.ValVAT10 = req.ValVAT10;
        inv.UpdAfterAllocatedBy = "system";
        inv.UpdAfterAllocatedDTimeUTC = DateTime.UtcNow;
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new(true, "Đã cập nhật hóa đơn sau khi cấp số.", inv);
    }

    // Đánh dấu hóa đơn ĐÃ GỬI EMAIL (port từ InBrand Invoice_Invoice_UpdMailSentDTimeUTCX).
    // Quy tắc InBrand:
    //  - Hóa đơn phải tồn tại và đang ở trạng thái ISSUED (Invoice_Invoice_CheckDB với InvoiceStatus.Issued).
    //  - Hóa đơn CHƯA gửi email (MailSentDTimeUTC còn rỗng) — lỗi Invoice_Invoice_UpdMailSentDTimeUTCX_Invalid.
    //  - Khi gửi ghi MailSentDTimeUTC + SendEmailDTimeUTC + SendEmailBy + LogLUBy + LogLUDTimeUTC.
    public async Task<InvoiceResult> MarkMailSentAsync(int id, string sendBy)
    {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null) return new(false, "Không tìm thấy hóa đơn.", null);
        if (inv.Status != InvoiceStatus.Issued)
            return new(false, "Chỉ đánh dấu gửi email được khi hóa đơn ở trạng thái ISSUED (đã phát hành).", inv);
        if (inv.MailSentDTimeUTC.HasValue)
            return new(false, "Hóa đơn đã được đánh dấu gửi email trước đó.", inv);

        var now = DateTime.UtcNow;
        var by = string.IsNullOrWhiteSpace(sendBy) ? "system" : sendBy.Trim();
        inv.MailSentDTimeUTC = now;
        inv.SendEmailDTimeUTC = now;
        inv.SendEmailBy = by;
        inv.MailLogLUBy = by;
        inv.MailLogLUDTimeUTC = now;
        inv.UpdatedAt = now;
        await db.SaveChangesAsync();
        return new(true, "Đã đánh dấu hóa đơn đã gửi email.", inv);
    }

    // Đẩy hóa đơn LÊN CỔNG THÔNG TIN ĐIỆN TỬ (port từ InBrand Invoice_Invoice_Issued_UpdFlagPushOutSiteX).
    // Quy tắc InBrand:
    //  - Hóa đơn phải tồn tại và đang ở trạng thái ISSUED hoặc DELETED
    //    (Invoice_Invoice_CheckDB với strInvoiceStatusListToCheck = "ISSUED, DELETED").
    //  - FlagPushOutSite phải còn rỗng (chưa đẩy) — lỗi Invoice_Invoice_Issued_UpdFlagPushOutSiteX_ExistFlagPushOutSite.
    //  - Khi đẩy ghi FlagPushOutSite = thời điểm đẩy + LogLUBy + LogLUDTimeUTC.
    public async Task<InvoiceResult> PushOutSiteAsync(int id, string pushBy)
    {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null) return new(false, "Không tìm thấy hóa đơn.", null);
        if (inv.Status != InvoiceStatus.Issued && inv.Status != InvoiceStatus.Deleted)
            return new(false, "Chỉ đẩy lên cổng được khi hóa đơn ở trạng thái ISSUED hoặc DELETED.", inv);
        if (inv.FlagPushOutSite.HasValue)
            return new(false, "Hóa đơn đã được đẩy lên cổng thông tin điện tử trước đó.", inv);

        var now = DateTime.UtcNow;
        var by = string.IsNullOrWhiteSpace(pushBy) ? "system" : pushBy.Trim();
        inv.FlagPushOutSite = now;
        inv.PushOutSiteBy = by;
        inv.PushOutSiteDTimeUTC = now;
        inv.UpdatedAt = now;
        await db.SaveChangesAsync();
        return new(true, "Đã đẩy hóa đơn lên cổng thông tin điện tử.", inv);
    }

    // Danh sách dòng chi tiết của một hóa đơn (port từ InBrand Invoice_InvoiceDtl).
    public Task<List<InvoiceLine>> LinesAsync(int invoiceId) =>
        db.InvoiceLines.Where(l => l.InvoiceId == invoiceId).OrderBy(l => l.Idx).ToListAsync();

    // Thêm một dòng chi tiết hóa đơn (port từ InBrand Invoice_InvoiceDtl).
    public async Task<InvoiceResult> AddLineAsync(int invoiceId, InvoiceLineReq req)
    {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId);
        if (inv == null) return new(false, "Không tìm thấy hóa đơn.", null);
        if (req.Qty < 0) return new(false, "Số lượng (Qty) phải >= 0.", inv);
        if (req.UnitPrice < 0) return new(false, "Đơn giá (UnitPrice) phải >= 0.", inv);
        if (req.VATRate < 0) return new(false, "Thuế suất (VATRate) phải >= 0.", inv);

        var line = new InvoiceLine
        {
            InvoiceId = inv.Id,
            Idx = req.Idx,
            SpecCode = (req.SpecCode ?? "").Trim(),
            SpecName = (req.SpecName ?? "").Trim(),
            Qty = req.Qty,
            UnitPrice = req.UnitPrice,
            VATRate = req.VATRate,
            ValInvoice = req.ValInvoice,
            ValTax = req.ValTax
        };
        db.InvoiceLines.Add(line);
        await db.SaveChangesAsync();
        return new(true, "Đã thêm dòng chi tiết hóa đơn.", inv);
    }

    // KIỂM TRA/ĐỐI CHIẾU TỔNG TIỀN HÓA ĐƠN (port từ InBrand
    // myCheck_Invoice_Invoice_Total_New20190905 + myCheck_Invoice_InvoiceDtl_Total_New20190905).
    // Quy tắc InBrand:
    //  - Tính lại từ dòng chi tiết: TotalValInvoice = Σ(Qty × UnitPrice);
    //    TotalValVAT = Σ(Qty × UnitPrice × VATRate/100); TotalValPmt = TotalValInvoice + TotalValVAT.
    //  - VatType = NoVat ("NVAT"): LÀM TRÒN từng dòng rồi mới cộng; SingleVat ("1VAT"): cộng rồi mới làm tròn.
    //  - Dung sai Delta = max(TổngThanhToán / 1.000.000, 10).
    //  - ok = true khi mọi chênh lệch |khai báo − tính lại| <= Delta
    //    (lỗi myCheck_Invoice_Invoice_Total_InvalidValue khi vượt dung sai).
    public async Task<InvoiceTotalCheck> CheckTotalAsync(int invoiceId, VatType vatType)
    {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId);
        if (inv == null)
            return new(false, "Không tìm thấy hóa đơn.", vatType, 0, 0, 0, 0);

        var lines = await db.InvoiceLines.Where(l => l.InvoiceId == invoiceId).ToListAsync();

        decimal calcInvoice, calcVat;
        if (vatType == VatType.NoVat)
        {
            // "NVAT": làm tròn TỪNG dòng rồi mới cộng.
            calcInvoice = lines.Sum(l => Math.Round(l.Qty * l.UnitPrice, 0, MidpointRounding.AwayFromZero));
            calcVat = lines.Sum(l => Math.Round(l.Qty * l.UnitPrice * l.VATRate / 100m, 0, MidpointRounding.AwayFromZero));
        }
        else
        {
            // "1VAT": cộng trước rồi mới làm tròn theo tổng.
            calcInvoice = Math.Round(lines.Sum(l => l.Qty * l.UnitPrice), 0, MidpointRounding.AwayFromZero);
            calcVat = Math.Round(lines.Sum(l => l.Qty * l.UnitPrice * l.VATRate / 100m), 0, MidpointRounding.AwayFromZero);
        }
        var calcPmt = calcInvoice + calcVat;

        var inputInvoice = inv.TotalValInvoice;
        var inputVat = inv.TotalValVAT;
        var inputPmt = inv.TotalValPmt;

        // Dung sai: max(TổngThanhToán / 1.000.000, 10) — port từ InBrand.
        var delta = Math.Max(inputPmt / 1_000_000m, 10m);

        var ok = Math.Abs(inputInvoice - calcInvoice) <= delta
              && Math.Abs(inputVat - calcVat) <= delta
              && Math.Abs(inputPmt - calcPmt) <= delta;

        var msg = ok
            ? $"Tổng tiền khớp (dung sai {delta:N0})."
            : $"Tổng tiền KHÔNG khớp: khai báo {inputPmt:N0} vs tính lại {calcPmt:N0} (dung sai {delta:N0}).";

        return new(ok, msg, vatType, calcInvoice, calcVat, calcPmt, inputInvoice, inputVat, inputPmt, delta, lines.Count);
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public async Task<InvoiceLifecycleDash> LifecycleDashboardAsync() => new(
        await db.Invoices.CountAsync(),
        await db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Pending),
        await db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Approved),
        await db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Issued),
        await db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Canceled),
        await db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Deleted));
}
