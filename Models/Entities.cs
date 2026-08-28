namespace MiniSign.Models;

public interface IOrgOwned { Guid OrgId { get; set; } }

public enum CertStatus { Active = 0, Revoked = 1, Expired = 2 }

public class Org
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Chứng thư số (RSA keypair) — mô phỏng CKS dùng ký hóa đơn/tài liệu
public class Certificate : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Subject { get; set; } = "";           // CN — chủ thể (VD "Công ty ABC")
    public string Serial { get; set; } = "";            // Số serial (GLOBAL unique — verify công khai)
    public string PublicKeyPem { get; set; } = "";
    public string PrivateKeyPem { get; set; } = "";     // (lab: lưu thẳng; thực tế nằm trong HSM/USB token)
    public string Algorithm { get; set; } = "SHA256withRSA";
    public DateTime NotBefore { get; set; } = DateTime.Today;
    public DateTime NotAfter { get; set; } = DateTime.Today.AddYears(3);
    public CertStatus Status { get; set; } = CertStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsUsable => Status == CertStatus.Active && DateTime.Today >= NotBefore && DateTime.Today <= NotAfter;
}

// Nhật ký một lần ký
public class SignLog : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int CertificateId { get; set; }
    public Certificate? Certificate { get; set; }
    public string DocName { get; set; } = "";
    public string Hash { get; set; } = "";              // SHA256 hex của nội dung
    public string Signature { get; set; } = "";         // base64
    public int ContentLength { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
