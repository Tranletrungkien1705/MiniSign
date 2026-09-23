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
                TaxCode = "0101234567",
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

        if (!await db.InvoiceTemplates.AnyAsync())
        {
            db.InvoiceTemplates.Add(new InvoiceTemplate
            {
                TInvoiceCode = "1C26TAA",
                TaxCode = "0101234567",
                InvoiceSerial = "C26TAA",
                StartInvoiceNo = 1,
                EndInvoiceNo = 1000,
                QtyUsed = 0,
                EffDateStart = DateTime.Today.AddMonths(-1),
                FlagActive = true
            });
            await db.SaveChangesAsync();
        }

        if (!await db.Invoices.AnyAsync())
        {
            db.Invoices.Add(new Invoice
            {
                InvoiceCode = "1C26TAA-00000001",
                TaxCode = "0101234567",
                CustomerName = "Công ty TNHH Khách Hàng Demo",
                Content = "Hóa đơn 1C26TAA-00000001 | Tổng: 550,000,000đ",
                TotalValPmt = 550_000_000m,
                SignStatus = SignStatus.Pending
            });
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
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Orgs_ApiKey\" ON minisign.\"Orgs\" (\"ApiKey\")",
            "CREATE TABLE IF NOT EXISTS minisign.\"Invoices\" (\"Id\" serial PRIMARY KEY, \"OrgId\" uuid NOT NULL, \"InvoiceCode\" text NOT NULL DEFAULT '', \"TaxCode\" text NOT NULL DEFAULT '', \"CustomerName\" text NOT NULL DEFAULT '', \"Content\" text NOT NULL DEFAULT '', \"TotalValPmt\" numeric NOT NULL DEFAULT 0, \"SignStatus\" integer NOT NULL DEFAULT 0, \"SignBy\" text NULL, \"SignDTimeUTC\" timestamp NULL, \"SignSerial\" text NULL, \"Signature\" text NULL, \"SignError\" text NULL, \"Status\" integer NOT NULL DEFAULT 0, \"CancelBy\" text NULL, \"CancelDTimeUTC\" timestamp NULL, \"CancelReason\" text NULL, \"CreatedAt\" timestamp NOT NULL DEFAULT now(), \"UpdatedAt\" timestamp NOT NULL DEFAULT now())",
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Invoices_OrgId_InvoiceCode\" ON minisign.\"Invoices\" (\"OrgId\", \"InvoiceCode\")" };
        foreach (var t in tables) sql.Add($"ALTER TABLE minisign.\"{t}\" ADD COLUMN IF NOT EXISTS \"OrgId\" uuid NOT NULL DEFAULT '{def}'");
        sql.Add("ALTER TABLE minisign.\"Certificates\" ADD COLUMN IF NOT EXISTS \"TaxCode\" text NOT NULL DEFAULT ''");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"Status\" integer NOT NULL DEFAULT 0");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"CancelBy\" text NULL");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"CancelDTimeUTC\" timestamp NULL");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"CancelReason\" text NULL");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"InvoiceNo\" text NULL");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"ApprBy\" text NULL");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"ApprDTimeUTC\" timestamp NULL");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"IssuedBy\" text NULL");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"IssuedDTimeUTC\" timestamp NULL");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"DeleteBy\" text NULL");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"DeleteDTimeUTC\" timestamp NULL");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"DeleteReason\" text NULL");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"AttachedDelFilePath\" text NULL");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"TInvoiceCode\" text NULL");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"InvoiceDateUTC\" timestamp NULL");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"InvoiceNoBy\" text NULL");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"InvoiceNoDTimeUTC\" timestamp NULL");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"FlagChange\" boolean NOT NULL DEFAULT true");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"ChangeBy\" text NULL");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"ChangeDTimeUTC\" timestamp NULL");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"ChangeReason\" text NULL");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"SourceInvoiceCode\" integer NOT NULL DEFAULT 0");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"InvoiceAdjType\" integer NOT NULL DEFAULT 0");
        sql.Add("ALTER TABLE minisign.\"Invoices\" ADD COLUMN IF NOT EXISTS \"RefNo\" text NULL");
        sql.Add("CREATE TABLE IF NOT EXISTS minisign.\"InvoiceTemplates\" (\"Id\" serial PRIMARY KEY, \"OrgId\" uuid NOT NULL, \"TInvoiceCode\" text NOT NULL DEFAULT '', \"TaxCode\" text NOT NULL DEFAULT '', \"InvoiceSerial\" text NOT NULL DEFAULT '', \"StartInvoiceNo\" bigint NOT NULL DEFAULT 1, \"EndInvoiceNo\" bigint NOT NULL DEFAULT 0, \"QtyUsed\" bigint NOT NULL DEFAULT 0, \"LastInvoiceNo\" text NULL, \"LastInvoiceDateUTC\" timestamp NULL, \"EffDateStart\" timestamp NOT NULL DEFAULT now(), \"FlagActive\" boolean NOT NULL DEFAULT true, \"CreatedAt\" timestamp NOT NULL DEFAULT now())");
        sql.Add("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_InvoiceTemplates_OrgId_TInvoiceCode\" ON minisign.\"InvoiceTemplates\" (\"OrgId\", \"TInvoiceCode\")");
        foreach (var s in sql) try { await db.Database.ExecuteSqlRawAsync(s); } catch { }
    }
}
