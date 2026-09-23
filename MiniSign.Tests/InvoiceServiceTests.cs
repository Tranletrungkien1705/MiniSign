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
}
