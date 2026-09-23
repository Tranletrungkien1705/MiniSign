using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniSign.Data;
using MiniSign.Models;
using MiniSign.Services;
using Xunit;

namespace MiniSign.Tests;

/// <summary>Test ký số RSA: cấp chứng thư, ký→verify hợp lệ, sửa nội dung→verify fail, thu hồi chặn ký.</summary>
public class SignServiceTests
{
    private static (AppDbContext db, ISignService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new SignService(db), conn);
    }

    [Fact]
    public async Task CreateCert_GeneratesSerial()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (ok, _, id) = await svc.CreateCertAsync("Cty ABC", 3);
            Assert.True(ok);
            var c = await svc.GetCertAsync(id);
            Assert.False(string.IsNullOrEmpty(c!.Serial));
            Assert.Contains("ABC", c.Subject);
        }
    }

    [Fact]
    public async Task Sign_ThenVerify_Valid()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateCertAsync("Cty ABC", 3);
            var c = await svc.GetCertAsync(id);
            var sign = await svc.SignAsync(id, "doc.txt", "Nội dung hợp đồng ABC");
            Assert.True(sign.ok);
            var v = await svc.VerifyAsync(c!.Serial, "Nội dung hợp đồng ABC", sign.signature!);
            Assert.True(v.valid);
        }
    }

    [Fact]
    public async Task Verify_TamperedContent_Invalid()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateCertAsync("Cty ABC", 3);
            var c = await svc.GetCertAsync(id);
            var sign = await svc.SignAsync(id, "doc.txt", "Nội dung gốc");
            var v = await svc.VerifyAsync(c!.Serial, "Nội dung ĐÃ SỬA", sign.signature!);
            Assert.False(v.valid);   // nội dung khác → chữ ký không khớp
        }
    }

    [Fact]
    public async Task Revoke_BlocksSigning()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateCertAsync("Cty ABC", 3);
            await svc.RevokeAsync(id);
            var sign = await svc.SignAsync(id, "doc.txt", "abc");
            Assert.False(sign.ok);   // chứng thư thu hồi không ký được
        }
    }

    [Fact]
    public async Task Verify_UnknownSerial_Invalid()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var v = await svc.VerifyAsync("KHONGCO", "abc", "xxx");
            Assert.False(v.valid);
        }
    }

    [Fact]
    public async Task Sign_LogsRecorded()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateCertAsync("Cty ABC", 3);
            await svc.SignAsync(id, "doc.txt", "abc");
            var logs = await svc.SignLogsAsync(id);
            Assert.Single(logs);
        }
    }

    // Port từ InBrand: ký/xác thực hóa đơn điện tử bằng SHA1withRSA (signTTHD/SignatureVerify).
    [Fact]
    public async Task Sign_Sha1_ThenVerify_Valid()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateCertAsync("Cty ABC", 3);
            var c = await svc.GetCertAsync(id);
            var sign = await svc.SignAsync(id, "hoadon.xml", "Hóa đơn 1C26TAA-00000001", SignAlgorithm.SHA1withRSA);
            Assert.True(sign.ok);
            Assert.Equal("SHA1withRSA", sign.algo);
            var v = await svc.VerifyAsync(c!.Serial, "Hóa đơn 1C26TAA-00000001", sign.signature!, SignAlgorithm.SHA1withRSA);
            Assert.True(v.valid);
        }
    }

    [Fact]
    public async Task Verify_Sha1_WrongAlgorithm_Invalid()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateCertAsync("Cty ABC", 3);
            var c = await svc.GetCertAsync(id);
            var sign = await svc.SignAsync(id, "hoadon.xml", "Hóa đơn ABC", SignAlgorithm.SHA1withRSA);
            // Xác thực bằng SHA256 → không khớp thuật toán → không hợp lệ
            var v = await svc.VerifyAsync(c!.Serial, "Hóa đơn ABC", sign.signature!, SignAlgorithm.SHA256withRSA);
            Assert.False(v.valid);
        }
    }

    [Fact]
    public async Task CertInfo_ReturnsMetadata()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateCertAsync("Cty ABC", 3);
            var c = await svc.GetCertAsync(id);
            var info = await svc.CertInfoAsync(c!.Serial);
            Assert.NotNull(info);
            Assert.Equal(c.Serial, info!.serial);
            Assert.True(info.usable);
        }
    }

    // Port từ InBrand CertificateInfo: "Check SerialNumber và MST có trong hệ thống".
    [Fact]
    public async Task Validate_SerialAndTaxCode_Match_Valid()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateCertAsync("Cty ABC", 3, "0101234567");
            var c = await svc.GetCertAsync(id);
            var v = await svc.ValidateAsync(c!.Serial, "0101234567");
            Assert.True(v.found);
            Assert.True(v.taxCodeMatch);
            Assert.True(v.valid);
        }
    }

    [Fact]
    public async Task Validate_WrongTaxCode_Invalid()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateCertAsync("Cty ABC", 3, "0101234567");
            var c = await svc.GetCertAsync(id);
            var v = await svc.ValidateAsync(c!.Serial, "9999999999");
            Assert.True(v.found);
            Assert.False(v.taxCodeMatch);
            Assert.False(v.valid);
        }
    }

    [Fact]
    public async Task Validate_UnknownSerial_NotFound()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var v = await svc.ValidateAsync("KHONGCO", "0101234567");
            Assert.False(v.found);
            Assert.False(v.valid);
        }
    }

    [Fact]
    public async Task Validate_RevokedCert_Invalid()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateCertAsync("Cty ABC", 3, "0101234567");
            var c = await svc.GetCertAsync(id);
            await svc.RevokeAsync(id);
            var v = await svc.ValidateAsync(c!.Serial, "0101234567");
            Assert.True(v.found);
            Assert.True(v.taxCodeMatch);
            Assert.False(v.valid);   // đã thu hồi → không hợp lệ
        }
    }
}
