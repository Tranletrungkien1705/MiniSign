using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSign.Data;
using MiniSign.Models;
using MiniSign.Services;
using Xunit;

namespace MiniSign.Tests;

/// <summary>
/// Test nghiệp vụ HẠN MỨC CẤP SỐ HÓA ĐƠN theo MST (port từ InBrand Invoice_license
/// + Invoice_license_IncreaseQtyX + Invoice_license_TotalQtyIssued/Used).
/// </summary>
public class LicenseServiceTests
{
    private static (AppDbContext db, ILicenseService lic, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new LicenseService(db), conn);
    }

    // Tăng hạn mức cho MST chưa có license → tự tạo bản ghi (TotalQty=0) rồi cộng thêm Qty.
    [Fact]
    public async Task IncreaseQty_NewMst_CreatesLicense()
    {
        var (_, lic, conn) = NewSvc(); using (conn)
        {
            var r = await lic.IncreaseQtyAsync("0101234567", 100, "ketoan01");
            Assert.True(r.ok);
            Assert.Equal("0101234567", r.license!.MST);
            Assert.Equal(100, r.license.TotalQty);
            Assert.Equal("ketoan01", r.license.LogLUBy);
            Assert.NotNull(r.license.LogLUDTimeUTC);
        }
    }

    // Tăng hạn mức lần hai → cộng dồn TotalQty.
    [Fact]
    public async Task IncreaseQty_ExistingMst_Accumulates()
    {
        var (_, lic, conn) = NewSvc(); using (conn)
        {
            await lic.IncreaseQtyAsync("0101234567", 100, "u1");
            var r = await lic.IncreaseQtyAsync("0101234567", 50, "u1");
            Assert.True(r.ok);
            Assert.Equal(150, r.license!.TotalQty);
        }
    }

    // Qty âm → bị từ chối (lỗi Invoice_license_IncreaseQtyX_InvalidQty).
    [Fact]
    public async Task IncreaseQty_Negative_Rejected()
    {
        var (_, lic, conn) = NewSvc(); using (conn)
        {
            var r = await lic.IncreaseQtyAsync("0101234567", -5, "u1");
            Assert.False(r.ok);
        }
    }

    // MST rỗng → bị từ chối.
    [Fact]
    public async Task IncreaseQty_EmptyMst_Rejected()
    {
        var (_, lic, conn) = NewSvc(); using (conn)
        {
            var r = await lic.IncreaseQtyAsync("  ", 10, "u1");
            Assert.False(r.ok);
        }
    }

    // Tính lại: TotalQtyIssued = sum(End-Start+1) của mẫu Active; TotalQtyUsed = sum(QtyUsed).
    [Fact]
    public async Task Recompute_SumsTemplates()
    {
        var (db, lic, conn) = NewSvc(); using (conn)
        {
            await lic.IncreaseQtyAsync("0101234567", 1000, "u1");
            db.InvoiceTemplates.Add(new InvoiceTemplate
            {
                TInvoiceCode = "1C26TAA", TaxCode = "0101234567", InvoiceSerial = "C26TAA",
                StartInvoiceNo = 1, EndInvoiceNo = 100, QtyUsed = 10, FlagActive = true,
                EffDateStart = DateTime.Today.AddMonths(-1)
            });
            await db.SaveChangesAsync();

            var r = await lic.RecomputeAsync("0101234567", "u1");
            Assert.True(r.ok);
            Assert.Equal(100, r.license!.TotalQtyIssued);   // 100 - 1 + 1
            Assert.Equal(10, r.license.TotalQtyUsed);
            Assert.Equal(900, r.license.QtyRemain);         // 1000 - 100
        }
    }

    // Bất biến: TotalQty < TotalQtyIssued → báo lỗi (Invoice_license_TotalQtyIssued_InvalidValue).
    [Fact]
    public async Task Recompute_IssuedExceedsQuota_Rejected()
    {
        var (db, lic, conn) = NewSvc(); using (conn)
        {
            await lic.IncreaseQtyAsync("0101234567", 50, "u1");   // hạn mức 50
            db.InvoiceTemplates.Add(new InvoiceTemplate
            {
                TInvoiceCode = "1C26TAA", TaxCode = "0101234567", InvoiceSerial = "C26TAA",
                StartInvoiceNo = 1, EndInvoiceNo = 100, QtyUsed = 0, FlagActive = true,
                EffDateStart = DateTime.Today.AddMonths(-1)
            });
            await db.SaveChangesAsync();

            var r = await lic.RecomputeAsync("0101234567", "u1");
            Assert.False(r.ok);   // đã cấp 100 > hạn mức 50
        }
    }

    // Bất biến: TotalQtyUsed > TotalQtyIssued → báo lỗi (Invoice_license_TotalQtyUsed_InvalidValue).
    [Fact]
    public async Task Recompute_UsedExceedsIssued_Rejected()
    {
        var (db, lic, conn) = NewSvc(); using (conn)
        {
            await lic.IncreaseQtyAsync("0101234567", 1000, "u1");
            db.InvoiceTemplates.Add(new InvoiceTemplate
            {
                TInvoiceCode = "1C26TAA", TaxCode = "0101234567", InvoiceSerial = "C26TAA",
                StartInvoiceNo = 1, EndInvoiceNo = 10, QtyUsed = 20, FlagActive = true,   // dùng 20 > cấp 10
                EffDateStart = DateTime.Today.AddMonths(-1)
            });
            await db.SaveChangesAsync();

            var r = await lic.RecomputeAsync("0101234567", "u1");
            Assert.False(r.ok);
        }
    }

    // Recompute cho MST chưa có license → báo lỗi.
    [Fact]
    public async Task Recompute_UnknownMst_Rejected()
    {
        var (_, lic, conn) = NewSvc(); using (conn)
        {
            var r = await lic.RecomputeAsync("9999999999", "u1");
            Assert.False(r.ok);
        }
    }

    // Dashboard tổng hợp hạn mức.
    [Fact]
    public async Task Dashboard_Aggregates()
    {
        var (_, lic, conn) = NewSvc(); using (conn)
        {
            await lic.IncreaseQtyAsync("0101234567", 100, "u1");
            await lic.IncreaseQtyAsync("0109999999", 200, "u1");
            var d = await lic.DashboardAsync();
            Assert.Equal(2, d.Total);
            Assert.Equal(300, d.TotalQty);
            Assert.Equal(300, d.QtyRemain);
        }
    }
}
