using Microsoft.EntityFrameworkCore;
using MiniSign.Data;
using MiniSign.Models;

namespace MiniSign.Services;

// Kết quả một thao tác trên hạn mức cấp số (license).
public record LicenseResult(bool ok, string msg, InvoiceLicense? license);

// Thống kê hạn mức theo MST.
public record LicenseDash(int Total, long TotalQty, long TotalQtyIssued, long TotalQtyUsed, long QtyRemain);

// Nghiệp vụ HẠN MỨC CẤP SỐ HÓA ĐƠN theo MST (license/quota).
// Port từ InBrand Invoice_license + Invoice_license_IncreaseQtyX + Invoice_license_TotalQtyIssued/Used:
//  - Mỗi MST có một hạn mức: TotalQty (tổng được phép), TotalQtyIssued (đã cấp dải số),
//    TotalQtyUsed (đã dùng). Bất biến: TotalQty >= TotalQtyIssued và TotalQtyUsed <= TotalQtyIssued.
//  - Tăng hạn mức: TotalQty += Qty; Qty phải >= 0 (lỗi Invoice_license_IncreaseQtyX_InvalidQty);
//    nếu MST chưa có license thì tự tạo (TotalQty=0) rồi cộng thêm.
//  - Recompute: tính lại TotalQtyIssued/TotalQtyUsed từ các mẫu số (Invoice_TempInvoice) của MST.
public interface ILicenseService
{
    Task<List<InvoiceLicense>> ListAsync();
    Task<InvoiceLicense?> GetAsync(string mst);
    Task<LicenseResult> IncreaseQtyAsync(string mst, long qty, string by);
    Task<LicenseResult> RecomputeAsync(string mst, string by);
    Task<LicenseDash> DashboardAsync();
}

public class LicenseService(AppDbContext db) : ILicenseService
{
    public Task<List<InvoiceLicense>> ListAsync() =>
        db.InvoiceLicenses.OrderBy(l => l.MST).ToListAsync();

    public Task<InvoiceLicense?> GetAsync(string mst) =>
        db.InvoiceLicenses.FirstOrDefaultAsync(l => l.MST == (mst ?? "").Trim());

    // Tăng hạn mức (port từ InBrand Invoice_license_IncreaseQtyX).
    // Quy tắc InBrand:
    //  - MST bắt buộc (không rỗng).
    //  - Qty phải >= 0 (lỗi Invoice_license_IncreaseQtyX_InvalidQty).
    //  - Nếu MST chưa có license → tự tạo bản ghi (TotalQty=0, FlagActive) rồi cộng thêm Qty.
    //  - Ghi người cập nhật (LogLUBy) + thời điểm (LogLUDTimeUTC).
    public async Task<LicenseResult> IncreaseQtyAsync(string mst, long qty, string by)
    {
        var code = (mst ?? "").Trim();
        if (code.Length == 0) return new(false, "Cần MST người nộp thuế.", null);
        if (qty < 0) return new(false, "Số lượng tăng hạn mức (Qty) phải >= 0.", null);

        var lic = await db.InvoiceLicenses.FirstOrDefaultAsync(l => l.MST == code);
        if (lic == null)
        {
            // MST chưa có license → tạo mới với hạn mức 0 (port từ nhánh insert của IncreaseQtyX).
            lic = new InvoiceLicense { MST = code, TotalQty = 0, FlagActive = true };
            db.InvoiceLicenses.Add(lic);
        }
        if (!lic.FlagActive)
            return new(false, "Hạn mức của MST đã ngừng hiệu lực (FlagActive không còn Active).", lic);

        lic.TotalQty += qty;
        lic.LogLUBy = string.IsNullOrWhiteSpace(by) ? "system" : by.Trim();
        lic.LogLUDTimeUTC = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new(true, $"Đã tăng hạn mức MST {code} thêm {qty} (tổng {lic.TotalQty}).", lic);
    }

    // Tính lại TotalQtyIssued/TotalQtyUsed từ các mẫu số của MST (port từ
    // Invoice_license_TotalQtyIssued / Invoice_license_TotalQtyUsed).
    //  - TotalQtyIssued = sum(EndInvoiceNo - StartInvoiceNo + 1) của các mẫu đã ISSUED.
    //  - TotalQtyUsed = sum(QtyUsed) của tất cả mẫu số.
    //  - Sau khi tính, kiểm tra bất biến: TotalQty >= TotalQtyIssued và TotalQtyUsed <= TotalQtyIssued
    //    (lỗi Invoice_license_TotalQtyIssued_InvalidValue / Invoice_license_TotalQtyUsed_InvalidValue).
    public async Task<LicenseResult> RecomputeAsync(string mst, string by)
    {
        var code = (mst ?? "").Trim();
        if (code.Length == 0) return new(false, "Cần MST người nộp thuế.", null);

        var lic = await db.InvoiceLicenses.FirstOrDefaultAsync(l => l.MST == code);
        if (lic == null) return new(false, $"Không tìm thấy hạn mức cho MST {code}.", null);

        var templates = await db.InvoiceTemplates.Where(t => t.TaxCode == code).ToListAsync();
        var issued = templates.Where(t => t.FlagActive).Sum(t => t.EndInvoiceNo - t.StartInvoiceNo + 1);
        var used = templates.Sum(t => t.QtyUsed);

        lic.TotalQtyIssued = issued;
        lic.TotalQtyUsed = used;
        lic.LogLUBy = string.IsNullOrWhiteSpace(by) ? "system" : by.Trim();
        lic.LogLUDTimeUTC = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Bất biến (port từ điều kiện Check của InBrand).
        if (lic.TotalQty < lic.TotalQtyIssued)
            return new(false, $"Hạn mức ({lic.TotalQty}) nhỏ hơn số đã cấp dải ({lic.TotalQtyIssued}).", lic);
        if (lic.TotalQtyUsed > lic.TotalQtyIssued)
            return new(false, $"Số đã dùng ({lic.TotalQtyUsed}) lớn hơn số đã cấp dải ({lic.TotalQtyIssued}).", lic);

        return new(true, $"Đã tính lại hạn mức MST {code}: cấp {lic.TotalQtyIssued}, dùng {lic.TotalQtyUsed}.", lic);
    }

    public async Task<LicenseDash> DashboardAsync()
    {
        var all = await db.InvoiceLicenses.ToListAsync();
        return new(all.Count, all.Sum(l => l.TotalQty), all.Sum(l => l.TotalQtyIssued),
            all.Sum(l => l.TotalQtyUsed), all.Sum(l => l.QtyRemain));
    }
}
