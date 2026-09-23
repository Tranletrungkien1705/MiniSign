namespace MiniSign.Models;

public interface IOrgOwned { Guid OrgId { get; set; } }

public enum CertStatus { Active = 0, Revoked = 1, Expired = 2 }

// Thuật toán ký số. Port từ InBrand: hóa đơn điện tử dùng SHA1WithRSA (signTTHD/SignatureVerify).
public enum SignAlgorithm { SHA256withRSA = 0, SHA1withRSA = 1 }

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
    public string TaxCode { get; set; } = "";           // MST — mã số thuế đơn vị (port từ InBrand CertificateInfo.MST)
    public string Serial { get; set; } = "";            // Số serial (GLOBAL unique — verify công khai)
    public string PublicKeyPem { get; set; } = "";
    public string PrivateKeyPem { get; set; } = "";     // (lab: lưu thẳng; thực tế nằm trong HSM/USB token)
    public string Algorithm { get; set; } = "SHA256withRSA";   // thuật toán mặc định khi ký
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
    public string Hash { get; set; } = "";              // hex của nội dung (theo thuật toán)
    public string Signature { get; set; } = "";         // base64
    public string Algorithm { get; set; } = "SHA256withRSA";  // thuật toán đã dùng để ký
    public int ContentLength { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Kết quả tra cứu/xác thực chứng thư theo serial + MST (port từ InBrand CertificateInfo:
// "Check SerialNumber và MST có trong hệ thống" + GetCertificateInfo của Signature Server).
public record CertValidation(
    bool found,          // có chứng thư với serial này trong hệ thống
    bool taxCodeMatch,   // MST nhập khớp MST đã đăng ký của chứng thư
    bool valid,          // chứng thư còn hiệu lực (Active + trong khoảng NotBefore..NotAfter)
    string status,       // nhãn trạng thái hiển thị (Hiệu lực / Hết hạn / Đã thu hồi / Chưa hiệu lực)
    string message,      // thông điệp kết luận
    string? subject,     // CN chủ thể
    string? taxCode,     // MST đã đăng ký
    string? serial,
    string? algorithm,
    DateTime? notBefore,
    DateTime? notAfter);
