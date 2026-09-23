namespace MiniSign.Models;

public interface IOrgOwned { Guid OrgId { get; set; } }

public enum CertStatus { Active = 0, Revoked = 1, Expired = 2 }

// Thuật toán ký số. Port từ InBrand: hóa đơn điện tử dùng SHA1WithRSA (signTTHD/SignatureVerify).
public enum SignAlgorithm { SHA256withRSA = 0, SHA1withRSA = 1 }

// Vòng đời trạng thái ký của một hóa đơn/tài liệu.
// Port từ InBrand OS_Invoice_InvoiceTemp.SignStatus (TConst: PENDING/PROCESSING/SIGNED/FAILED).
// Quy tắc nghiệp vụ: chỉ được phát hành/ký lại khi lần call trước ở trạng thái FAILED hoặc PROCESSING.
public enum SignStatus { Pending = 0, Processing = 1, Signed = 2, Failed = 3 }

// Vòng đời trạng thái HÓA ĐƠN (khác với trạng thái ký).
// Port từ InBrand TConst.InvoiceStatus (PENDING/APPROVED/ISSUED/CANCELED/DELETED).
// Quy tắc nghiệp vụ (Invoice_Invoice_CancelX): chỉ được HỦY khi hóa đơn đang ở PENDING hoặc APPROVED.
public enum InvoiceStatus { Pending = 0, Approved = 1, Issued = 2, Canceled = 3 }

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

// Hóa đơn/tài liệu cần ký — mang trạng thái ký (SignStatus) theo vòng đời.
// Port từ InBrand OS_Invoice_InvoiceTemp (InvoiceCode, MST, SignStatus, SignBy, SignDTimeUTC...).
public class Invoice : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string InvoiceCode { get; set; } = "";        // Số tra cứu / mã hóa đơn (unique trong tenant)
    public string TaxCode { get; set; } = "";            // MST người nộp thuế
    public string CustomerName { get; set; } = "";       // Tên khách hàng
    public string Content { get; set; } = "";            // Nội dung hóa đơn (bản rõ để ký)
    public decimal TotalValPmt { get; set; }              // Tiền thanh toán
    public SignStatus SignStatus { get; set; } = SignStatus.Pending;
    public string? SignBy { get; set; }                   // Người ký
    public DateTime? SignDTimeUTC { get; set; }           // Thời gian ký
    public string? SignSerial { get; set; }               // Serial chứng thư đã dùng để ký
    public string? Signature { get; set; }                // Chữ ký base64 (khi đã ký)
    public string? SignError { get; set; }                // Lý do thất bại (khi SignStatus = Failed)

    // Trạng thái vòng đời hóa đơn (port từ InBrand InvoiceStatus). Mặc định PENDING khi tạo.
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Pending;
    public string? CancelBy { get; set; }                 // Người hủy (port từ Invoice_Invoice.CancelBy)
    public DateTime? CancelDTimeUTC { get; set; }         // Thời gian hủy (port từ Invoice_Invoice.CancelDTimeUTC)
    public string? CancelReason { get; set; }             // Lý do hủy (port từ Invoice_Invoice.Remark)

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
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
