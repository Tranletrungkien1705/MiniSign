using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MiniSign.Models;
namespace MiniSign.Data;

public static class Seeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        await MigratePostgresAsync(db);
        if (!await db.Orgs.AnyAsync(o => o.Id == TenantContext.DefaultOrgId))
        { db.Orgs.Add(new Org { Id = TenantContext.DefaultOrgId, Name = "Demo Ký số", ApiKey = TenantContext.DefaultApiKey }); await db.SaveChangesAsync(); }

        if (!await db.Certificates.AnyAsync())
        {
            using var rsa = RSA.Create(2048);
            var cert = new Certificate
            {
                Subject = "Công ty CP Ô tô Đông Đô",
                Serial = "54c0ffee1234abcd5678",
                PublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem(),
                PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem(),
                NotBefore = DateTime.Today.AddMonths(-2),
                NotAfter = DateTime.Today.AddYears(3)
            };
            db.Certificates.Add(cert); await db.SaveChangesAsync();

            var content = "Hóa đơn 1C26TAA-00000001 | Tổng: 550,000,000đ";
            var sig = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(content), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
            db.SignLogs.Add(new SignLog { CertificateId = cert.Id, DocName = "HoaDon_1C26TAA_00000001.xml", Hash = hash, Signature = sig, ContentLength = Encoding.UTF8.GetByteCount(content) });
            await db.SaveChangesAsync();
        }
    }

    private static async Task MigratePostgresAsync(AppDbContext db)
    {
        if (!db.Database.IsNpgsql()) return;
        var def = TenantContext.DefaultOrgId;
        var tables = new[] { "Certificates", "SignLogs" };
        var sql = new List<string> {
            "CREATE TABLE IF NOT EXISTS minisign.\"Orgs\" (\"Id\" uuid PRIMARY KEY, \"Name\" text NOT NULL DEFAULT '', \"ApiKey\" text NOT NULL DEFAULT '', \"CreatedAt\" timestamp NOT NULL DEFAULT now())",
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Orgs_ApiKey\" ON minisign.\"Orgs\" (\"ApiKey\")" };
        foreach (var t in tables) sql.Add($"ALTER TABLE minisign.\"{t}\" ADD COLUMN IF NOT EXISTS \"OrgId\" uuid NOT NULL DEFAULT '{def}'");
        foreach (var s in sql) try { await db.Database.ExecuteSqlRawAsync(s); } catch { }
    }
}
