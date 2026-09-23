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
// Quy tắc nghiệp vụ (Invoice_Invoice_DeletedX): chỉ được XÓA khi hóa đơn đang ở ISSUED.
public enum InvoiceStatus { Pending = 0, Approved = 1, Issued = 2, Canceled = 3, Deleted = 4 }

// Nguồn gốc hóa đơn (port từ InBrand TConst.SourceInvoiceCode).
//  - Root: hóa đơn gốc (INVOICEROOT).
//  - Replace: hóa đơn thay thế (INVOICEREPLACE) — thay cho một hóa đơn đã bị XÓA (DELETED).
//  - Adj: hóa đơn điều chỉnh (INVOICEADJ) — điều chỉnh một hóa đơn đã PHÁT HÀNH (ISSUED).
public enum SourceInvoiceCode { Root = 0, Replace = 1, Adj = 2 }

// Loại điều chỉnh (port từ InBrand TConst.InvoiceAdjType).
//  - Normal: bình thường (không điều chỉnh).
//  - AdjIncrease: điều chỉnh TĂNG (ADJINCREASE) — bắt buộc có RefNo.
//  - AdjDecrease: điều chỉnh GIẢM (ADJDESCREASE) — bắt buộc có RefNo.
public enum InvoiceAdjType { Normal = 0, AdjIncrease = 1, AdjDecrease = 2 }

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

    // Số hóa đơn (InvoiceNo) — bắt buộc phải có trước khi DUYỆT.
    // Port từ InBrand Invoice_Invoice_ApprovedMultiX: "InvoiceNoIsNotNull" (không được rỗng khi duyệt).
    public string? InvoiceNo { get; set; }

    // Cấp số hóa đơn (port từ InBrand Invoice_Invoice_AllocatedInvX_New20190917).
    // Mẫu số hóa đơn dùng để cấp số (TInvoiceCode) + ngày hóa đơn + người/thời điểm cấp số.
    public string? TInvoiceCode { get; set; }             // Mẫu số HĐ (port từ Invoice_Invoice.TInvoiceCode)
    public DateTime? InvoiceDateUTC { get; set; }         // Ngày hóa đơn (port từ Invoice_Invoice.InvoiceDateUTC)
    public string? InvoiceNoBy { get; set; }              // Người cấp số (port từ Invoice_Invoice.InvoiceNoBy)
    public DateTime? InvoiceNoDTimeUTC { get; set; }      // Thời điểm cấp số (port từ Invoice_Invoice.InvoiceNoDTimeUTC)
    public string? ApprBy { get; set; }                   // Người duyệt (port từ Invoice_Invoice.ApprBy)
    public DateTime? ApprDTimeUTC { get; set; }           // Thời gian duyệt (port từ Invoice_Invoice.ApprDTimeUTC)
    public string? IssuedBy { get; set; }                 // Người phát hành (port từ Invoice_Invoice.IssuedBy)
    public DateTime? IssuedDTimeUTC { get; set; }         // Thời gian phát hành (port từ Invoice_Invoice.IssuedDTimeUTC)

    // Xóa hóa đơn (port từ InBrand Invoice_Invoice_DeletedX_New20190715).
    // Quy tắc: chỉ xóa được hóa đơn đang ở trạng thái ISSUED; khi xóa ghi DELETED + người xóa + thời gian + lý do.
    public string? DeleteBy { get; set; }                 // Người xóa (port từ Invoice_Invoice.DeleteBy)
    public DateTime? DeleteDTimeUTC { get; set; }         // Thời gian xóa (port từ Invoice_Invoice.DeleteDTimeUTC)
    public string? DeleteReason { get; set; }             // Lý do xóa (port từ Invoice_Invoice.DeleteReason)
    public string? AttachedDelFilePath { get; set; }      // File đính kèm khi xóa (port từ Invoice_Invoice.AttachedDelFilePath)

    // Đánh dấu hóa đơn đã bị THAY THẾ/ĐIỀU CHỈNH (port từ InBrand Invoice_Invoice_ChangeX).
    // FlagChange: true = còn hiệu lực (Active "1"), false = đã bị thay thế (Inactive "0").
    // Quy tắc: chỉ đánh dấu được khi hóa đơn ở trạng thái ISSUED và FlagChange đang Active.
    public bool FlagChange { get; set; } = true;          // Còn hiệu lực (port từ Invoice_Invoice.FlagChange)
    public string? ChangeBy { get; set; }                 // Người thay thế (port từ Invoice_Invoice.ChangeBy)
    public DateTime? ChangeDTimeUTC { get; set; }         // Thời gian thay thế (port từ Invoice_Invoice.ChangeDTimeUTC)
    public string? ChangeReason { get; set; }             // Lý do thay thế (port từ Invoice_Invoice.Remark)

    // Nguồn gốc + loại điều chỉnh (port từ InBrand Invoice_Invoice.SourceInvoiceCode / InvoiceAdjType).
    // Quy tắc (Invoice_Invoice_SaveX):
    //  - SourceInvoiceCode = Adj → hóa đơn được tham chiếu (RefNo) phải tồn tại và ở trạng thái ISSUED.
    //  - SourceInvoiceCode = Replace → hóa đơn được tham chiếu (RefNo) phải tồn tại và ở trạng thái DELETED.
    //  - InvoiceAdjType = AdjIncrease/AdjDecrease → bắt buộc có RefNo (Invoice_Invoice_SaveX_InvoiceAdjTypeIsNotNull).
    //  - Một hóa đơn chỉ được điều chỉnh/thay thế MỘT lần (myCheck_Invoice_Invoice_RefNo).
    public SourceInvoiceCode SourceInvoiceCode { get; set; } = SourceInvoiceCode.Root;  // Nguồn gốc HĐ
    public InvoiceAdjType InvoiceAdjType { get; set; } = InvoiceAdjType.Normal;         // Loại điều chỉnh
    public string? RefNo { get; set; }                    // Số tra cứu HĐ gốc bị điều chỉnh/thay thế (port từ Invoice_Invoice.RefNo)

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// Mẫu số hóa đơn (Invoice_TempInvoice) — dải số được cấp phát cho một MST.
// Port từ InBrand Invoice_TempInvoice: StartInvoiceNo/EndInvoiceNo/QtyUsed/LastInvoiceNo/LastInvoiceDateUTC/EffDateStart.
// Quy tắc cấp số (Invoice_Invoice_AllocatedInvX_New20190917):
//  - Số HĐ kế tiếp = StartInvoiceNo + QtyUsed; sau khi cấp thì QtyUsed++ và LastInvoiceNo = số vừa cấp.
//  - Số HĐ phải nằm trong [StartInvoiceNo, EndInvoiceNo] (myCheck_Invoice_TempInvoice_InvoiceNo).
//  - Ngày hóa đơn phải >= LastInvoiceDateUTC (ngày cấp số trước đó) và >= EffDateStart (ngày hiệu lực mẫu).
public class InvoiceTemplate : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string TInvoiceCode { get; set; } = "";        // Mã mẫu số HĐ (unique trong tenant)
    public string TaxCode { get; set; } = "";             // MST được cấp dải số
    public string InvoiceSerial { get; set; } = "";       // Ký hiệu hóa đơn (port từ Invoice_TempInvoice.InvoiceSerial)
    public long StartInvoiceNo { get; set; } = 1;         // Số bắt đầu của dải
    public long EndInvoiceNo { get; set; } = 0;           // Số kết thúc của dải
    public long QtyUsed { get; set; } = 0;                // Số lượng đã dùng
    public string? LastInvoiceNo { get; set; }            // Số HĐ cấp gần nhất
    public DateTime? LastInvoiceDateUTC { get; set; }     // Ngày hóa đơn cấp gần nhất
    public DateTime EffDateStart { get; set; } = DateTime.Today;  // Ngày hiệu lực mẫu
    public bool FlagActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public long QtyRemain => EndInvoiceNo - StartInvoiceNo + 1 - QtyUsed;
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
