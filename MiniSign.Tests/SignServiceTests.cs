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
}
