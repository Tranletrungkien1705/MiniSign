using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSign.Data;
using MiniSign.Models;
using MiniSign.Services;
using Xunit;

namespace MiniSign.Tests;

/// <summary>
/// Test nghiệp vụ ký hóa đơn theo vòng đời trạng thái (port từ InBrand OS_Invoice_InvoiceTemp
/// + OS_Invoice_InvoiceTemp_UpdMultiSignStatus).
/// </summary>
public class InvoiceServiceTests
{
    private static (AppDbContext db, IInvoiceService inv, ISignService sign, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        var sign = new SignService(db);
        return (db, new InvoiceService(db, sign), sign, conn);
    }

    [Fact]
    public async Task Create_StartsPending()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var r = await inv.CreateAsync("HD-001", "0101234567", "Cty ABC", "Nội dung hóa đơn", 1000m);
            Assert.True(r.ok);
            Assert.Equal(SignStatus.Pending, r.invoice!.SignStatus);
        }
    }

    [Fact]
    public async Task Create_DuplicateCode_Rejected()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            await inv.CreateAsync("HD-001", "", "", "abc", 0m);
            var r = await inv.CreateAsync("HD-001", "", "", "xyz", 0m);
            Assert.False(r.ok);
        }
    }

    [Fact]
    public async Task Sign_WithValidCert_BecomesSigned()
    {
        var (_, inv, sign, conn) = NewSvc(); using (conn)
        {
            var (_, _, certId) = await sign.CreateCertAsync("Cty ABC", 3, "0101234567");
            var c = await inv.CreateAsync("HD-001", "0101234567", "Cty ABC", "Nội dung hóa đơn", 1000m);
            var r = await inv.SignAsync(c.invoice!.Id, certId, "ketoan01");
            Assert.True(r.ok);
            Assert.Equal(SignStatus.Signed, r.invoice!.SignStatus);
            Assert.Equal("ketoan01", r.invoice.SignBy);
            Assert.NotNull(r.invoice.SignDTimeUTC);
            Assert.False(string.IsNullOrEmpty(r.invoice.SignSerial));
        }
    }

    [Fact]
    public async Task Sign_RevokedCert_BecomesFailed()
    {
        var (_, inv, sign, conn) = NewSvc(); using (conn)
        {
            var (_, _, certId) = await sign.CreateCertAsync("Cty ABC", 3);
            await sign.RevokeAsync(certId);
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            var r = await inv.SignAsync(c.invoice!.Id, certId, "ketoan01");
            Assert.False(r.ok);
            Assert.Equal(SignStatus.Failed, r.invoice!.SignStatus);
            Assert.False(string.IsNullOrEmpty(r.invoice.SignError));
        }
    }

    [Fact]
    public async Task Sign_AlreadySigned_Rejected()
    {
        var (_, inv, sign, conn) = NewSvc(); using (conn)
        {
            var (_, _, certId) = await sign.CreateCertAsync("Cty ABC", 3);
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            await inv.SignAsync(c.invoice!.Id, certId, "ketoan01");
            var r = await inv.SignAsync(c.invoice.Id, certId, "ketoan01");
            Assert.False(r.ok);   // đã ký → không ký lại
        }
    }

    // Quy tắc InBrand: chỉ phát hành tiếp khi lần call trước ở FAILED/PROCESSING.
    [Fact]
    public async Task SetStatus_Processing_OnlyFromFailedOrProcessing()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            // PENDING → không được chuyển sang PROCESSING
            var r1 = await inv.SetStatusAsync(c.invoice!.Id, SignStatus.Processing, null);
            Assert.False(r1.ok);

            // FAILED → được phép chuyển sang PROCESSING
            await inv.SetStatusAsync(c.invoice.Id, SignStatus.Failed, "lỗi mạng");
            var r2 = await inv.SetStatusAsync(c.invoice.Id, SignStatus.Processing, null);
            Assert.True(r2.ok);
            Assert.Equal(SignStatus.Processing, r2.invoice!.SignStatus);
        }
    }

    [Fact]
    public async Task Dashboard_CountsByStatus()
    {
        var (_, inv, sign, conn) = NewSvc(); using (conn)
        {
            var (_, _, certId) = await sign.CreateCertAsync("Cty ABC", 3);
            var a = await inv.CreateAsync("HD-001", "", "", "a", 0m);
            await inv.CreateAsync("HD-002", "", "", "b", 0m);
            await inv.SignAsync(a.invoice!.Id, certId, "u1");
            var d = await inv.DashboardAsync();
            Assert.Equal(2, d.Total);
            Assert.Equal(1, d.Signed);
            Assert.Equal(1, d.Pending);
        }
    }

    // Hủy hóa đơn (port từ InBrand Invoice_Invoice_Cancel): PENDING/APPROVED mới được hủy.
    [Fact]
    public async Task Cancel_Pending_BecomesCanceled()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var c = await inv.CreateAsync("HD-001", "0101234567", "Cty ABC", "Nội dung", 1000m);
            var r = await inv.CancelAsync(c.invoice!.Id, "sai thông tin khách hàng", "ketoan01");
            Assert.True(r.ok);
            Assert.Equal(InvoiceStatus.Canceled, r.invoice!.Status);
            Assert.Equal("ketoan01", r.invoice.CancelBy);
            Assert.Equal("sai thông tin khách hàng", r.invoice.CancelReason);
            Assert.NotNull(r.invoice.CancelDTimeUTC);
        }
    }

    [Fact]
    public async Task Cancel_AlreadyCanceled_Rejected()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            await inv.CancelAsync(c.invoice!.Id, "lần 1", "u1");
            var r = await inv.CancelAsync(c.invoice.Id, "lần 2", "u1");
            Assert.False(r.ok);   // đã hủy → không hủy lại
        }
    }

    [Fact]
    public async Task Cancel_Issued_Rejected()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            // Giả lập hóa đơn đã phát hành (ISSUED) — không nằm trong PENDING/APPROVED.
            c.invoice!.Status = InvoiceStatus.Issued;
            await inv.SetStatusAsync(c.invoice.Id, SignStatus.Signed, null);
            var r = await inv.CancelAsync(c.invoice.Id, "lý do", "u1");
            Assert.False(r.ok);
        }
    }

    // Duyệt hóa đơn (port từ InBrand Invoice_Invoice_ApprovedMultiX): PENDING + có InvoiceNo mới duyệt được.
    [Fact]
    public async Task Approve_PendingWithInvoiceNo_BecomesApproved()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var c = await inv.CreateAsync("HD-001", "0101234567", "Cty ABC", "Nội dung", 1000m);
            var r = await inv.ApproveAsync(c.invoice!.Id, "00000001", "ketoan01");
            Assert.True(r.ok);
            Assert.Equal(InvoiceStatus.Approved, r.invoice!.Status);
            Assert.Equal("00000001", r.invoice.InvoiceNo);
            Assert.Equal("ketoan01", r.invoice.ApprBy);
            Assert.NotNull(r.invoice.ApprDTimeUTC);
        }
    }

    [Fact]
    public async Task Approve_WithoutInvoiceNo_Rejected()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            var r = await inv.ApproveAsync(c.invoice!.Id, "  ", "u1");
            Assert.False(r.ok);   // thiếu số hóa đơn
            Assert.Equal(InvoiceStatus.Pending, r.invoice!.Status);
        }
    }

    [Fact]
    public async Task Approve_AlreadyApproved_Rejected()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            await inv.ApproveAsync(c.invoice!.Id, "00000001", "u1");
            var r = await inv.ApproveAsync(c.invoice.Id, "00000002", "u1");
            Assert.False(r.ok);   // đã duyệt → không duyệt lại
        }
    }

    // Phát hành hóa đơn (port từ InBrand Invoice_Invoice_IssuedXMulti): chỉ APPROVED mới phát hành được.
    [Fact]
    public async Task Issue_Approved_BecomesIssued()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            await inv.ApproveAsync(c.invoice!.Id, "00000001", "u1");
            var r = await inv.IssueAsync(c.invoice.Id, "ketoan01");
            Assert.True(r.ok);
            Assert.Equal(InvoiceStatus.Issued, r.invoice!.Status);
            Assert.Equal("ketoan01", r.invoice.IssuedBy);
            Assert.NotNull(r.invoice.IssuedDTimeUTC);
        }
    }

    [Fact]
    public async Task Issue_Pending_Rejected()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            var r = await inv.IssueAsync(c.invoice!.Id, "u1");
            Assert.False(r.ok);   // chưa duyệt → không phát hành được
        }
    }

    [Fact]
    public async Task LifecycleDashboard_CountsByStatus()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var a = await inv.CreateAsync("HD-001", "", "", "a", 0m);
            await inv.CreateAsync("HD-002", "", "", "b", 0m);
            await inv.CancelAsync(a.invoice!.Id, "lý do", "u1");
            var d = await inv.LifecycleDashboardAsync();
            Assert.Equal(2, d.Total);
            Assert.Equal(1, d.Pending);
            Assert.Equal(1, d.Canceled);
        }
    }

    // Xóa hóa đơn (port từ InBrand Invoice_Invoice_Deleted): chỉ ISSUED mới được xóa.
    [Fact]
    public async Task Delete_Issued_BecomesDeleted()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var c = await inv.CreateAsync("HD-001", "0101234567", "Cty ABC", "Nội dung", 1000m);
            await inv.ApproveAsync(c.invoice!.Id, "00000001", "u1");
            await inv.IssueAsync(c.invoice.Id, "u1");
            var r = await inv.DeleteAsync(c.invoice.Id, "hóa đơn sai", "ketoan01", "/UploadedFiles/Inv_IVID/x.pdf");
            Assert.True(r.ok);
            Assert.Equal(InvoiceStatus.Deleted, r.invoice!.Status);
            Assert.Equal("ketoan01", r.invoice.DeleteBy);
            Assert.Equal("hóa đơn sai", r.invoice.DeleteReason);
            Assert.Equal("/UploadedFiles/Inv_IVID/x.pdf", r.invoice.AttachedDelFilePath);
            Assert.NotNull(r.invoice.DeleteDTimeUTC);
        }
    }

    [Fact]
    public async Task Delete_Pending_Rejected()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            var r = await inv.DeleteAsync(c.invoice!.Id, "lý do", "u1", null);
            Assert.False(r.ok);   // chưa phát hành → không xóa được
            Assert.Equal(InvoiceStatus.Pending, r.invoice!.Status);
        }
    }

    [Fact]
    public async Task Delete_AlreadyDeleted_Idempotent()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            await inv.ApproveAsync(c.invoice!.Id, "00000001", "u1");
            await inv.IssueAsync(c.invoice.Id, "u1");
            await inv.DeleteAsync(c.invoice.Id, "lần 1", "u1", null);
            var r = await inv.DeleteAsync(c.invoice.Id, "lần 2", "u1", null);
            Assert.True(r.ok);   // đã DELETED → bỏ qua, không báo lỗi
            Assert.Equal(InvoiceStatus.Deleted, r.invoice!.Status);
        }
    }

    // ===== Cấp số hóa đơn (port từ InBrand Invoice_Invoice_AllocatedInvX_New20190917) =====

    private static async Task<InvoiceTemplate> SeedTemplate(AppDbContext db, long start = 1, long end = 1000)
    {
        var t = new InvoiceTemplate
        {
            TInvoiceCode = "1C26TAA",
            TaxCode = "0101234567",
            InvoiceSerial = "C26TAA",
            StartInvoiceNo = start,
            EndInvoiceNo = end,
            QtyUsed = 0,
            EffDateStart = DateTime.Today.AddMonths(-1),
            FlagActive = true
        };
        db.InvoiceTemplates.Add(t); await db.SaveChangesAsync();
        return t;
    }

    [Fact]
    public async Task Allocate_Pending_AssignsNextInvoiceNo()
    {
        var (db, inv, _, conn) = NewSvc(); using (conn)
        {
            await SeedTemplate(db);
            var c = await inv.CreateAsync("HD-001", "0101234567", "Cty ABC", "Nội dung", 1000m);
            var r = await inv.AllocateAsync(c.invoice!.Id, "1C26TAA", DateTime.Today, "ketoan01");
            Assert.True(r.ok);
            Assert.Equal("0000001", r.invoice!.InvoiceNo);   // StartInvoiceNo(1) + QtyUsed(0)
            Assert.Equal("ketoan01", r.invoice.InvoiceNoBy);
            Assert.NotNull(r.invoice.InvoiceNoDTimeUTC);
            Assert.Equal(DateTime.Today, r.invoice.InvoiceDateUTC!.Value.Date);
            // Mẫu đã tăng QtyUsed và ghi LastInvoiceNo.
            var tpl = await db.InvoiceTemplates.FirstAsync();
            Assert.Equal(1, tpl.QtyUsed);
            Assert.Equal("0000001", tpl.LastInvoiceNo);
        }
    }

    [Fact]
    public async Task Allocate_SecondInvoice_IncrementsNumber()
    {
        var (db, inv, _, conn) = NewSvc(); using (conn)
        {
            await SeedTemplate(db);
            var a = await inv.CreateAsync("HD-001", "", "", "a", 0m);
            var b = await inv.CreateAsync("HD-002", "", "", "b", 0m);
            await inv.AllocateAsync(a.invoice!.Id, "1C26TAA", DateTime.Today, "u1");
            var r = await inv.AllocateAsync(b.invoice!.Id, "1C26TAA", DateTime.Today, "u1");
            Assert.True(r.ok);
            Assert.Equal("0000002", r.invoice!.InvoiceNo);
        }
    }

    [Fact]
    public async Task Allocate_AlreadyHasInvoiceNo_Rejected()
    {
        var (db, inv, _, conn) = NewSvc(); using (conn)
        {
            await SeedTemplate(db);
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            await inv.AllocateAsync(c.invoice!.Id, "1C26TAA", DateTime.Today, "u1");
            var r = await inv.AllocateAsync(c.invoice.Id, "1C26TAA", DateTime.Today, "u1");
            Assert.False(r.ok);   // đã có số → không cấp lại
        }
    }

    [Fact]
    public async Task Allocate_FutureDate_Rejected()
    {
        var (db, inv, _, conn) = NewSvc(); using (conn)
        {
            await SeedTemplate(db);
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            var r = await inv.AllocateAsync(c.invoice!.Id, "1C26TAA", DateTime.Today.AddDays(5), "u1");
            Assert.False(r.ok);   // ngày hóa đơn ở tương lai
            Assert.Null(r.invoice!.InvoiceNo);
        }
    }

    [Fact]
    public async Task Allocate_BeforeLastInvoiceDate_Rejected()
    {
        var (db, inv, _, conn) = NewSvc(); using (conn)
        {
            var tpl = await SeedTemplate(db);
            tpl.LastInvoiceDateUTC = DateTime.Today;   // đã cấp số hôm nay
            await db.SaveChangesAsync();
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            var r = await inv.AllocateAsync(c.invoice!.Id, "1C26TAA", DateTime.Today.AddDays(-1), "u1");
            Assert.False(r.ok);   // ngày HĐ < ngày cấp gần nhất
        }
    }

    [Fact]
    public async Task Allocate_RangeExhausted_Rejected()
    {
        var (db, inv, _, conn) = NewSvc(); using (conn)
        {
            var tpl = await SeedTemplate(db, start: 1, end: 1);
            tpl.QtyUsed = 1;   // dải chỉ có 1 số, đã dùng hết
            await db.SaveChangesAsync();
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            var r = await inv.AllocateAsync(c.invoice!.Id, "1C26TAA", DateTime.Today, "u1");
            Assert.False(r.ok);   // hết dải số
        }
    }

    [Fact]
    public async Task Allocate_UnknownTemplate_Rejected()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            var r = await inv.AllocateAsync(c.invoice!.Id, "KHONG-CO", DateTime.Today, "u1");
            Assert.False(r.ok);
        }
    }
}
