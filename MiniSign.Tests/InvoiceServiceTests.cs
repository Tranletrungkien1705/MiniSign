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

    // ===== Đánh dấu thay thế hóa đơn (port từ InBrand Invoice_Invoice_ChangeX) =====

    private static async Task<Invoice> SeedIssued(IInvoiceService inv)
    {
        var c = await inv.CreateAsync("HD-001", "0101234567", "Cty ABC", "Nội dung", 1000m);
        await inv.ApproveAsync(c.invoice!.Id, "00000001", "u1");
        await inv.IssueAsync(c.invoice.Id, "u1");
        return c.invoice;
    }

    [Fact]
    public async Task Change_Issued_BecomesInactive()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var issued = await SeedIssued(inv);
            var r = await inv.ChangeAsync(issued.Id, "điều chỉnh sai sót", "ketoan01");
            Assert.True(r.ok);
            Assert.False(r.invoice!.FlagChange);
            Assert.Equal("ketoan01", r.invoice.ChangeBy);
            Assert.Equal("điều chỉnh sai sót", r.invoice.ChangeReason);
            Assert.NotNull(r.invoice.ChangeDTimeUTC);
        }
    }

    [Fact]
    public async Task Change_Pending_Rejected()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            var r = await inv.ChangeAsync(c.invoice!.Id, "lý do", "u1");
            Assert.False(r.ok);   // chưa phát hành → không thay thế được
            Assert.True(r.invoice!.FlagChange);
        }
    }

    [Fact]
    public async Task Change_AlreadyChanged_Rejected()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var issued = await SeedIssued(inv);
            await inv.ChangeAsync(issued.Id, "lần 1", "u1");
            var r = await inv.ChangeAsync(issued.Id, "lần 2", "u1");
            Assert.False(r.ok);   // đã thay thế → FlagChange không còn Active
        }
    }

    // ===== Đánh dấu hóa đơn ĐIỀU CHỈNH/THAY THẾ (port từ InBrand Invoice_Invoice_Save_Adj/Save_Replace → Invoice_Invoice_SaveX) =====

    [Fact]
    public async Task Adjust_AdjIncrease_RequiresRefNo()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var c = await inv.CreateAsync("HD-002", "", "", "Nội dung", 0m);
            var r = await inv.AdjustAsync(c.invoice!.Id, "", SourceInvoiceCode.Adj, InvoiceAdjType.AdjIncrease, "tăng", "u1");
            Assert.False(r.ok);   // điều chỉnh Tăng bắt buộc có RefNo
        }
    }

    [Fact]
    public async Task Adjust_Adj_RefNoMustBeIssued()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            // Hóa đơn gốc mới ở PENDING → chưa phát hành.
            await inv.CreateAsync("HD-001", "", "", "gốc", 0m);
            var c = await inv.CreateAsync("HD-002", "", "", "điều chỉnh", 0m);
            var r = await inv.AdjustAsync(c.invoice!.Id, "HD-001", SourceInvoiceCode.Adj, InvoiceAdjType.AdjIncrease, "tăng", "u1");
            Assert.False(r.ok);   // gốc chưa ISSUED → không điều chỉnh được
        }
    }

    [Fact]
    public async Task Adjust_Adj_IssuedRefNo_Succeeds()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var issued = await SeedIssued(inv);   // HD-001 đã ISSUED
            var c = await inv.CreateAsync("HD-002", "", "", "điều chỉnh", 0m);
            var r = await inv.AdjustAsync(c.invoice!.Id, "HD-001", SourceInvoiceCode.Adj, InvoiceAdjType.AdjIncrease, "tăng giá", "ketoan01");
            Assert.True(r.ok);
            Assert.Equal(SourceInvoiceCode.Adj, r.invoice!.SourceInvoiceCode);
            Assert.Equal(InvoiceAdjType.AdjIncrease, r.invoice.InvoiceAdjType);
            Assert.Equal("HD-001", r.invoice.RefNo);
            Assert.Equal("ketoan01", r.invoice.ChangeBy);
            Assert.NotNull(r.invoice.ChangeDTimeUTC);
        }
    }

    [Fact]
    public async Task Adjust_Replace_RefNoMustBeDeleted()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var issued = await SeedIssued(inv);   // HD-001 đang ISSUED (chưa xóa)
            var c = await inv.CreateAsync("HD-002", "", "", "thay thế", 0m);
            var r = await inv.AdjustAsync(c.invoice!.Id, "HD-001", SourceInvoiceCode.Replace, InvoiceAdjType.Normal, "thay thế", "u1");
            Assert.False(r.ok);   // gốc chưa DELETED → không thay thế được
        }
    }

    [Fact]
    public async Task Adjust_Replace_DeletedRefNo_Succeeds()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var issued = await SeedIssued(inv);
            await inv.DeleteAsync(issued.Id, "sai", "u1", null);   // HD-001 → DELETED
            var c = await inv.CreateAsync("HD-002", "", "", "thay thế", 0m);
            var r = await inv.AdjustAsync(c.invoice!.Id, "HD-001", SourceInvoiceCode.Replace, InvoiceAdjType.Normal, "thay thế", "ketoan01");
            Assert.True(r.ok);
            Assert.Equal(SourceInvoiceCode.Replace, r.invoice!.SourceInvoiceCode);
            Assert.Equal("HD-001", r.invoice.RefNo);
        }
    }

    [Fact]
    public async Task Adjust_RefNoAlreadyAdjusted_Rejected()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var issued = await SeedIssued(inv);
            await inv.DeleteAsync(issued.Id, "sai", "u1", null);   // HD-001 → DELETED
            var a = await inv.CreateAsync("HD-002", "", "", "thay thế 1", 0m);
            await inv.AdjustAsync(a.invoice!.Id, "HD-001", SourceInvoiceCode.Replace, InvoiceAdjType.Normal, "lần 1", "u1");
            var b = await inv.CreateAsync("HD-003", "", "", "thay thế 2", 0m);
            var r = await inv.AdjustAsync(b.invoice!.Id, "HD-001", SourceInvoiceCode.Replace, InvoiceAdjType.Normal, "lần 2", "u1");
            Assert.False(r.ok);   // gốc đã được thay thế một lần → không thay thế tiếp
        }
    }

    [Fact]
    public async Task Adjust_UnknownRefNo_Rejected()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var c = await inv.CreateAsync("HD-002", "", "", "điều chỉnh", 0m);
            var r = await inv.AdjustAsync(c.invoice!.Id, "KHONG-CO", SourceInvoiceCode.Adj, InvoiceAdjType.AdjIncrease, "tăng", "u1");
            Assert.False(r.ok);   // không tìm thấy hóa đơn gốc
        }
    }

    // ===== Cập nhật hóa đơn SAU KHI CẤP SỐ (port từ InBrand Invoice_Invoice_UpdAfterAllocatedX) =====

    private static InvoiceUpdateReq UpdReq(decimal totalPmt = 1000m, DateTime? date = null) => new(
        "TM", "KH01", "Cty Khách", "Hà Nội", "0900000000", "VCB", "kh@abc.vn", "123456", "Nguyễn Văn A",
        "0109999999", date ?? DateTime.Today, 1000m, 100m, totalPmt, 0m, 0m, 500m, 25m, 500m, 50m);

    [Fact]
    public async Task UpdateAfterAllocated_PendingWithInvoiceNo_Succeeds()
    {
        var (db, inv, _, conn) = NewSvc(); using (conn)
        {
            await SeedTemplate(db);
            var c = await inv.CreateAsync("HD-001", "0101234567", "Cty ABC", "Nội dung", 0m);
            await inv.AllocateAsync(c.invoice!.Id, "1C26TAA", DateTime.Today, "u1");
            var r = await inv.UpdateAfterAllocatedAsync(c.invoice.Id, UpdReq());
            Assert.True(r.ok);
            Assert.Equal("TM", r.invoice!.PaymentMethodCode);
            Assert.Equal("KH01", r.invoice.CustomerNNTCode);
            Assert.Equal("0109999999", r.invoice.CustomerMST);
            Assert.Equal(1000m, r.invoice.TotalValInvoice);
            Assert.Equal(100m, r.invoice.TotalValVAT);
            Assert.Equal(500m, r.invoice.ValGoodsVAT5);
            Assert.Equal(25m, r.invoice.ValVAT5);
            Assert.Equal(500m, r.invoice.ValGoodsVAT10);
            Assert.Equal(50m, r.invoice.ValVAT10);
            Assert.NotNull(r.invoice.UpdAfterAllocatedDTimeUTC);
        }
    }

    [Fact]
    public async Task UpdateAfterAllocated_WithoutInvoiceNo_Rejected()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            var r = await inv.UpdateAfterAllocatedAsync(c.invoice!.Id, UpdReq());
            Assert.False(r.ok);   // chưa cấp số → không cập nhật được
        }
    }

    [Fact]
    public async Task UpdateAfterAllocated_NotPending_Rejected()
    {
        var (db, inv, _, conn) = NewSvc(); using (conn)
        {
            await SeedTemplate(db);
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            await inv.AllocateAsync(c.invoice!.Id, "1C26TAA", DateTime.Today, "u1");
            await inv.ApproveAsync(c.invoice.Id, "0000001", "u1");   // → APPROVED
            var r = await inv.UpdateAfterAllocatedAsync(c.invoice.Id, UpdReq());
            Assert.False(r.ok);   // không còn PENDING
        }
    }

    [Fact]
    public async Task UpdateAfterAllocated_FutureDate_Rejected()
    {
        var (db, inv, _, conn) = NewSvc(); using (conn)
        {
            await SeedTemplate(db);
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            await inv.AllocateAsync(c.invoice!.Id, "1C26TAA", DateTime.Today, "u1");
            var r = await inv.UpdateAfterAllocatedAsync(c.invoice.Id, UpdReq(date: DateTime.Today.AddDays(5)));
            Assert.False(r.ok);   // ngày hóa đơn ở tương lai
        }
    }

    [Fact]
    public async Task UpdateAfterAllocated_BeforePreviousInvoiceDate_Rejected()
    {
        var (db, inv, _, conn) = NewSvc(); using (conn)
        {
            await SeedTemplate(db);
            // Hóa đơn liền trước (số 0000001) có ngày hôm nay.
            var a = await inv.CreateAsync("HD-001", "", "", "a", 0m);
            await inv.AllocateAsync(a.invoice!.Id, "1C26TAA", DateTime.Today, "u1");
            // Hóa đơn liền sau (số 0000002) — cập nhật với ngày trước hóa đơn liền trước.
            var b = await inv.CreateAsync("HD-002", "", "", "b", 0m);
            await inv.AllocateAsync(b.invoice!.Id, "1C26TAA", DateTime.Today, "u1");
            var r = await inv.UpdateAfterAllocatedAsync(b.invoice.Id, UpdReq(date: DateTime.Today.AddDays(-1)));
            Assert.False(r.ok);   // ngày HĐ < ngày hóa đơn liền trước
        }
    }

    // ===== Đánh dấu hóa đơn ĐÃ GỬI EMAIL (port từ InBrand Invoice_Invoice_UpdMailSentDTimeUTCX) =====

    [Fact]
    public async Task MarkMailSent_Issued_Succeeds()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var issued = await SeedIssued(inv);   // HD-001 đã ISSUED
            var r = await inv.MarkMailSentAsync(issued.Id, "ketoan01");
            Assert.True(r.ok);
            Assert.NotNull(r.invoice!.MailSentDTimeUTC);
            Assert.NotNull(r.invoice.SendEmailDTimeUTC);
            Assert.Equal("ketoan01", r.invoice.SendEmailBy);
            Assert.Equal("ketoan01", r.invoice.MailLogLUBy);
            Assert.NotNull(r.invoice.MailLogLUDTimeUTC);
        }
    }

    [Fact]
    public async Task MarkMailSent_Pending_Rejected()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            var r = await inv.MarkMailSentAsync(c.invoice!.Id, "u1");
            Assert.False(r.ok);   // chưa phát hành → không đánh dấu gửi email được
            Assert.Null(r.invoice!.MailSentDTimeUTC);
        }
    }

    [Fact]
    public async Task MarkMailSent_AlreadySent_Rejected()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var issued = await SeedIssued(inv);
            await inv.MarkMailSentAsync(issued.Id, "u1");
            var r = await inv.MarkMailSentAsync(issued.Id, "u1");
            Assert.False(r.ok);   // đã gửi email → không đánh dấu lại
        }
    }

    // ===== Đẩy hóa đơn lên cổng thông tin điện tử (port từ InBrand Invoice_Invoice_Issued_UpdFlagPushOutSiteX) =====

    [Fact]
    public async Task PushOutSite_Issued_Succeeds()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var issued = await SeedIssued(inv);   // HD-001 đã ISSUED
            var r = await inv.PushOutSiteAsync(issued.Id, "ketoan01");
            Assert.True(r.ok);
            Assert.NotNull(r.invoice!.FlagPushOutSite);
            Assert.Equal("ketoan01", r.invoice.PushOutSiteBy);
            Assert.NotNull(r.invoice.PushOutSiteDTimeUTC);
        }
    }

    [Fact]
    public async Task PushOutSite_Deleted_Succeeds()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var issued = await SeedIssued(inv);
            await inv.DeleteAsync(issued.Id, "sai", "u1", null);   // HD-001 → DELETED
            var r = await inv.PushOutSiteAsync(issued.Id, "ketoan01");
            Assert.True(r.ok);   // DELETED cũng được đẩy cổng
            Assert.NotNull(r.invoice!.FlagPushOutSite);
        }
    }

    [Fact]
    public async Task PushOutSite_Pending_Rejected()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var c = await inv.CreateAsync("HD-001", "", "", "Nội dung", 0m);
            var r = await inv.PushOutSiteAsync(c.invoice!.Id, "u1");
            Assert.False(r.ok);   // chưa phát hành → không đẩy cổng được
            Assert.Null(r.invoice!.FlagPushOutSite);
        }
    }

    [Fact]
    public async Task PushOutSite_AlreadyPushed_Rejected()
    {
        var (_, inv, _, conn) = NewSvc(); using (conn)
        {
            var issued = await SeedIssued(inv);
            await inv.PushOutSiteAsync(issued.Id, "u1");
            var r = await inv.PushOutSiteAsync(issued.Id, "u1");
            Assert.False(r.ok);   // đã đẩy cổng → không đẩy lại (ExistFlagPushOutSite)
        }
    }
}
