using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MiniSign.Data;
using MiniSign.Models;

namespace MiniSign.Services;

public record SignResult(bool ok, string msg, string? hash, string? signature, string? serial, string? algo);
public record VerifyResult(bool valid, string msg, string? subject, string? serial, DateTime? signedAt);
public record SignDash(int Certs, int Active, int Signs, List<Certificate> RecentCerts, List<SignLog> RecentSigns);

public interface ISignService
{
    Task<List<Certificate>> CertsAsync();
    Task<Certificate?> GetCertAsync(int id);
    Task<Certificate?> GetBySerialAsync(string serial);
    Task<(bool ok, string msg, int id)> CreateCertAsync(string subject, int years);
    Task<(bool ok, string msg)> RevokeAsync(int id);
    Task<SignResult> SignAsync(int certId, string docName, string content);
    Task<VerifyResult> VerifyAsync(string serial, string content, string signatureB64);
    Task<List<SignLog>> SignLogsAsync(int? certId);
    Task<SignDash> DashboardAsync();

    string Sha256Hex(string content);
}

public class SignService(AppDbContext db) : ISignService
{
    public Task<List<Certificate>> CertsAsync() => db.Certificates.OrderByDescending(c => c.Id).ToListAsync();
    public Task<Certificate?> GetCertAsync(int id) => db.Certificates.FirstOrDefaultAsync(c => c.Id == id);
    public Task<Certificate?> GetBySerialAsync(string serial) =>
        db.Certificates.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Serial == serial);

    public async Task<(bool ok, string msg, int id)> CreateCertAsync(string subject, int years)
    {
        if (string.IsNullOrWhiteSpace(subject)) return (false, "Cần tên chủ thể (CN).", 0);
        using var rsa = RSA.Create(2048);
        var cert = new Certificate
        {
            Subject = subject.Trim(),
            Serial = await GenSerialAsync(),
            PublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem(),
            PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem(),
            NotBefore = DateTime.Today,
            NotAfter = DateTime.Today.AddYears(years <= 0 ? 3 : years)
        };
        db.Certificates.Add(cert); await db.SaveChangesAsync();
        return (true, "Đã tạo chứng thư số (RSA 2048).", cert.Id);
    }

    public async Task<(bool ok, string msg)> RevokeAsync(int id)
    {
        var c = await db.Certificates.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return (false, "Không tìm thấy.");
        c.Status = CertStatus.Revoked; await db.SaveChangesAsync();
        return (true, "Đã thu hồi chứng thư.");
    }

    public async Task<SignResult> SignAsync(int certId, string docName, string content)
    {
        var c = await db.Certificates.FirstOrDefaultAsync(x => x.Id == certId);
        if (c == null) return new(false, "Không tìm thấy chứng thư.", null, null, null, null);
        if (!c.IsUsable) return new(false, "Chứng thư không hợp lệ (đã thu hồi/hết hạn).", null, null, null, null);
        if (string.IsNullOrEmpty(content)) return new(false, "Nội dung rỗng.", null, null, null, null);

        var bytes = Encoding.UTF8.GetBytes(content);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(c.PrivateKeyPem);
        var sig = rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var sigB64 = Convert.ToBase64String(sig);
        var hashHex = Sha256Hex(content);

        db.SignLogs.Add(new SignLog { CertificateId = c.Id, DocName = string.IsNullOrWhiteSpace(docName) ? "document" : docName.Trim(), Hash = hashHex, Signature = sigB64, ContentLength = bytes.Length });
        await db.SaveChangesAsync();
        return new(true, "Đã ký thành công.", hashHex, sigB64, c.Serial, c.Algorithm);
    }

    public async Task<VerifyResult> VerifyAsync(string serial, string content, string signatureB64)
    {
        var c = await GetBySerialAsync((serial ?? "").Trim());
        if (c == null) return new(false, "Không tìm thấy chứng thư theo serial.", null, null, null);
        byte[] sig;
        try { sig = Convert.FromBase64String(signatureB64.Trim()); }
        catch { return new(false, "Chữ ký không đúng định dạng base64.", c.Subject, c.Serial, null); }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(c.PublicKeyPem);
        var ok = rsa.VerifyData(Encoding.UTF8.GetBytes(content ?? ""), sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        DateTime? signedAt = ok ? (await db.SignLogs.IgnoreQueryFilters()
            .Where(l => l.CertificateId == c.Id && l.Signature == signatureB64.Trim())
            .OrderByDescending(l => l.Id).Select(l => (DateTime?)l.CreatedAt).FirstOrDefaultAsync()) : null;
        return new(ok, ok ? "Chữ ký HỢP LỆ — nội dung toàn vẹn." : "Chữ ký KHÔNG hợp lệ — nội dung đã bị thay đổi hoặc sai chữ ký.", c.Subject, c.Serial, signedAt);
    }

    public Task<List<SignLog>> SignLogsAsync(int? certId)
    {
        var q = db.SignLogs.Include(l => l.Certificate).AsQueryable();
        if (certId.HasValue) q = q.Where(l => l.CertificateId == certId.Value);
        return q.OrderByDescending(l => l.Id).Take(300).ToListAsync();
    }

    public async Task<SignDash> DashboardAsync() => new(
        await db.Certificates.CountAsync(),
        await db.Certificates.CountAsync(c => c.Status == CertStatus.Active),
        await db.SignLogs.CountAsync(),
        await db.Certificates.OrderByDescending(c => c.Id).Take(5).ToListAsync(),
        await db.SignLogs.Include(l => l.Certificate).OrderByDescending(l => l.Id).Take(6).ToListAsync());

    public string Sha256Hex(string content)
    {
        var h = SHA256.HashData(Encoding.UTF8.GetBytes(content ?? ""));
        return Convert.ToHexString(h).ToLowerInvariant();
    }

    private async Task<string> GenSerialAsync()
    {
        for (int i = 0; i < 12; i++)
        {
            var s = Convert.ToHexString(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();
            if (!await db.Certificates.IgnoreQueryFilters().AnyAsync(x => x.Serial == s)) return s;
        }
        return Guid.NewGuid().ToString("N")[..20];
    }
}
