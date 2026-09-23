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

// Kiểu thuế GTGT của mẫu số hóa đơn (port từ InBrand Invoice_TempGroup.VATType).
// Quyết định cách LÀM TRÒN khi tính tổng tiền hóa đơn (myCheck_Invoice_Invoice_Total_New20190905):
//  - SingleVat ("1VAT"): một thuế suất — CỘNG trước rồi MỚI làm tròn theo tổng.
//  - NoVat ("NVAT"): nhiều thuế suất — LÀM TRÒN từng dòng rồi mới cộng.
public enum VatType { SingleVat = 0, NoVat = 1 }

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

    // Cập nhật hóa đơn SAU KHI CẤP SỐ (port từ InBrand Invoice_Invoice_UpdAfterAllocatedX).
    // Sau khi hóa đơn đã có số (InvoiceNo), kế toán bổ sung phương thức thanh toán, thông tin khách hàng
    // và BẢNG KÊ GIÁ TRỊ THEO THUẾ SUẤT (hàng không chịu thuế / không chịu thuế GTGT / chịu VAT 5% / 10%).
    // Quy tắc: chỉ cập nhật khi hóa đơn ở PENDING, đã có InvoiceNo, SourceInvoiceCode = Root,
    // ngày hóa đơn không ở tương lai và phải nằm giữa ngày của hóa đơn liền trước (InvoiceNo-1)
    // và liền sau (InvoiceNo+1) trong cùng mẫu số (TInvoiceCode).
    public string? PaymentMethodCode { get; set; }        // Phương thức thanh toán (port từ Invoice_Invoice.PaymentMethodCode)
    public string? CustomerNNTCode { get; set; }          // Mã khách hàng NNT (port từ Invoice_Invoice.CustomerNNTCode)
    public string? CustomerNNTName { get; set; }          // Tên khách hàng NNT (port từ Invoice_Invoice.CustomerNNTName)
    public string? CustomerNNTAddress { get; set; }       // Địa chỉ khách hàng (port từ Invoice_Invoice.CustomerNNTAddress)
    public string? CustomerNNTPhone { get; set; }         // Điện thoại khách hàng (port từ Invoice_Invoice.CustomerNNTPhone)
    public string? CustomerNNTBankName { get; set; }      // Ngân hàng khách hàng (port từ Invoice_Invoice.CustomerNNTBankName)
    public string? CustomerNNTEmail { get; set; }         // Email khách hàng (port từ Invoice_Invoice.CustomerNNTEmail)
    public string? CustomerNNTAccNo { get; set; }         // Số tài khoản khách hàng (port từ Invoice_Invoice.CustomerNNTAccNo)
    public string? CustomerNNTBuyerName { get; set; }     // Người mua hàng (port từ Invoice_Invoice.CustomerNNTBuyerName)
    public string? CustomerMST { get; set; }              // MST khách hàng (port từ Invoice_Invoice.CustomerMST)
    public decimal TotalValInvoice { get; set; }          // Tổng giá trị hàng hóa (port từ Invoice_Invoice.TotalValInvoice)
    public decimal TotalValVAT { get; set; }              // Tổng tiền thuế GTGT (port từ Invoice_Invoice.TotalValVAT)
    public decimal ValGoodsNotTaxable { get; set; }       // Giá trị hàng không chịu thuế (port từ Invoice_Invoice.ValGoodsNotTaxable)
    public decimal ValGoodsNotChargeTax { get; set; }     // Giá trị hàng không chịu thuế GTGT (port từ Invoice_Invoice.ValGoodsNotChargeTax)
    public decimal ValGoodsVAT5 { get; set; }             // Giá trị hàng chịu VAT 5% (port từ Invoice_Invoice.ValGoodsVAT5)
    public decimal ValVAT5 { get; set; }                  // Tiền thuế VAT 5% (port từ Invoice_Invoice.ValVAT5)
    public decimal ValGoodsVAT10 { get; set; }            // Giá trị hàng chịu VAT 10% (port từ Invoice_Invoice.ValGoodsVAT10)
    public decimal ValVAT10 { get; set; }                 // Tiền thuế VAT 10% (port từ Invoice_Invoice.ValVAT10)
    public string? UpdAfterAllocatedBy { get; set; }      // Người cập nhật sau cấp số (port từ Invoice_Invoice.LogLUBy)
    public DateTime? UpdAfterAllocatedDTimeUTC { get; set; }  // Thời điểm cập nhật sau cấp số (port từ Invoice_Invoice.LogLUDTimeUTC)

    // Đánh dấu hóa đơn ĐÃ GỬI EMAIL cho khách hàng (port từ InBrand Invoice_Invoice_UpdMailSentDTimeUTCX).
    // Quy tắc: chỉ đánh dấu được khi hóa đơn ở trạng thái ISSUED (đã phát hành) và CHƯA gửi email
    // (MailSentDTimeUTC còn rỗng — lỗi Invoice_Invoice_UpdMailSentDTimeUTCX_Invalid).
    // Khi gửi ghi MailSentDTimeUTC + SendEmailDTimeUTC + SendEmailBy + LogLUBy + LogLUDTimeUTC.
    public DateTime? MailSentDTimeUTC { get; set; }       // Thời điểm gửi email (port từ Invoice_Invoice.MailSentDTimeUTC)
    public DateTime? SendEmailDTimeUTC { get; set; }      // Thời điểm gửi email (port từ Invoice_Invoice.SendEmailDTimeUTC)
    public string? SendEmailBy { get; set; }              // Người gửi email (port từ Invoice_Invoice.SendEmailBy)
    public string? MailLogLUBy { get; set; }              // Người cập nhật gần nhất (port từ Invoice_Invoice.LogLUBy)
    public DateTime? MailLogLUDTimeUTC { get; set; }      // Thời điểm cập nhật gần nhất (port từ Invoice_Invoice.LogLUDTimeUTC)

    // Đẩy hóa đơn LÊN CỔNG THÔNG TIN ĐIỆN TỬ (push out site) — port từ InBrand
    // Invoice_Invoice_Issued_UpdFlagPushOutSiteX.
    // Quy tắc: hóa đơn phải tồn tại và đang ở trạng thái ISSUED hoặc DELETED;
    // FlagPushOutSite phải còn rỗng (chưa đẩy) — lỗi Invoice_Invoice_Issued_UpdFlagPushOutSiteX_ExistFlagPushOutSite.
    // Khi đẩy ghi FlagPushOutSite = thời điểm đẩy + LogLUBy + LogLUDTimeUTC.
    public DateTime? FlagPushOutSite { get; set; }        // Thời điểm đẩy lên cổng (port từ Invoice_Invoice.FlagPushOutSite)
    public string? PushOutSiteBy { get; set; }            // Người đẩy lên cổng (port từ Invoice_Invoice.LogLUBy)
    public DateTime? PushOutSiteDTimeUTC { get; set; }    // Thời điểm cập nhật gần nhất (port từ Invoice_Invoice.LogLUDTimeUTC)

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// Dòng chi tiết hóa đơn (port từ InBrand Invoice_InvoiceDtl).
// Mỗi dòng có số lượng (Qty), đơn giá (UnitPrice), thuế suất (VATRate %) và
// giá trị/thuế do người dùng khai báo (ValInvoice/ValTax).
// Dùng để TÍNH LẠI tổng tiền hóa đơn và ĐỐI CHIẾU với tổng đã khai báo
// (myCheck_Invoice_InvoiceDtl_Total_New20190905).
public class InvoiceLine : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public int Idx { get; set; }                          // Thứ tự dòng (port từ Invoice_InvoiceDtl.Idx)
    public string? ProductID { get; set; }                // Mã sản phẩm (port từ Invoice_InvoiceDtl.ProductID) — dùng để kiểm tra trùng
    public string SpecCode { get; set; } = "";            // Mã hàng hóa/dịch vụ (port từ Invoice_InvoiceDtl.SpecCode)
    public string SpecName { get; set; } = "";            // Tên hàng hóa/dịch vụ (port từ Invoice_InvoiceDtl.SpecName)
    public decimal Qty { get; set; }                      // Số lượng (port từ Invoice_InvoiceDtl.Qty)
    public decimal UnitPrice { get; set; }                // Đơn giá (port từ Invoice_InvoiceDtl.UnitPrice)
    public decimal VATRate { get; set; }                  // Thuế suất % (port từ Invoice_InvoiceDtl.VATRate)
    public decimal ValInvoice { get; set; }               // Giá trị khai báo (port từ Invoice_InvoiceDtl.ValInvoice)
    public decimal ValTax { get; set; }                   // Tiền thuế khai báo (port từ Invoice_InvoiceDtl.ValTax)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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

// Hạn mức (license) cấp số hóa đơn theo MST — port từ InBrand Invoice_license.
// Mỗi MST (mã số thuế) có một hạn mức gồm:
//  - TotalQty: tổng số hóa đơn được phép phát hành (hạn mức).
//  - TotalQtyIssued: tổng số hóa đơn đã được CẤP dải số (sum EndInvoiceNo-StartInvoiceNo+1 của các mẫu ISSUED).
//  - TotalQtyUsed: tổng số hóa đơn đã DÙNG (sum QtyUsed của các mẫu số).
// Quy tắc (Invoice_license_TotalQtyIssued / Invoice_license_TotalQtyUsed):
//  bất biến TotalQty >= TotalQtyIssued và TotalQtyUsed <= TotalQtyIssued.
// Quy tắc (Invoice_license_IncreaseQtyX): tăng hạn mức TotalQty += Qty; Qty phải >= 0
//  (lỗi Invoice_license_IncreaseQtyX_InvalidQty); MST phải tồn tại và license đang Active.
public class InvoiceLicense : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string MST { get; set; } = "";              // MST người nộp thuế (unique trong tenant)
    public string NetworkID { get; set; } = "";        // Mã mạng (port từ Invoice_license.NetworkID)
    public long TotalQty { get; set; } = 0;             // Hạn mức tổng (port từ Invoice_license.TotalQty)
    public long TotalQtyIssued { get; set; } = 0;       // Đã cấp dải số (port từ Invoice_license.TotalQtyIssued)
    public long TotalQtyUsed { get; set; } = 0;         // Đã dùng (port từ Invoice_license.TotalQtyUsed)
    public bool FlagActive { get; set; } = true;        // Còn hiệu lực (port từ Invoice_license.FlagActive)
    public string? LogLUBy { get; set; }                // Người cập nhật gần nhất (port từ Invoice_license.LogLUBy)
    public DateTime? LogLUDTimeUTC { get; set; }        // Thời điểm cập nhật gần nhất (port từ Invoice_license.LogLUDTimeUTC)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public long QtyRemain => TotalQty - TotalQtyIssued;  // Hạn mức còn lại có thể cấp
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

// Kết quả KIỂM TRA/ĐỐI CHIẾU TỔNG TIỀN HÓA ĐƠN (port từ InBrand
// myCheck_Invoice_Invoice_Total_New20190905 + myCheck_Invoice_InvoiceDtl_Total_New20190905).
// Tính lại tổng từ các dòng chi tiết rồi so với tổng đã khai báo trên hóa đơn.
//  - TotalValInvoice = Σ(Qty × UnitPrice); TotalValVAT = Σ(Qty × UnitPrice × VATRate/100).
//  - TotalValPmt = TotalValInvoice + TotalValVAT.
//  - VatType = NoVat ("NVAT"): làm tròn TỪNG dòng rồi mới cộng; SingleVat ("1VAT"): cộng rồi mới làm tròn.
//  - Delta: dung sai cho phép = max(TổngThanhToán / 1.000.000, 10).
//  - ok = true khi mọi chênh lệch (|khai báo − tính lại|) đều <= Delta.
public record InvoiceTotalCheck(
    bool ok,                 // tổng khai báo khớp tổng tính lại (trong dung sai)
    string message,          // thông điệp kết luận
    VatType vatType,         // kiểu thuế của mẫu số (quyết định cách làm tròn)
    decimal calcTotalValInvoice = 0,   // tổng giá trị hàng hóa tính lại từ dòng chi tiết
    decimal calcTotalValVAT = 0,       // tổng tiền thuế GTGT tính lại
    decimal calcTotalValPmt = 0,       // tổng thanh toán tính lại = giá trị + thuế
    decimal inputTotalValInvoice = 0,  // tổng giá trị hàng hóa đã khai báo trên hóa đơn
    decimal inputTotalValVAT = 0,      // tổng tiền thuế GTGT đã khai báo
    decimal inputTotalValPmt = 0,      // tổng thanh toán đã khai báo
    decimal delta = 0,                 // dung sai cho phép
    int lineCount = 0);                // số dòng chi tiết đã dùng để tính
// Kết quả KIỂM TRA HÓA ĐƠN TRƯỚC KHI LƯU (port từ InBrand Invoice_Invoice_Calc).
// Tập hợp các quy tắc kiểm tra hợp lệ của hóa đơn trước khi ghi nhận:
//  - InvoiceCode không được rỗng (Invoice_Invoice_Calc_InvalidInvoiceCode).
//  - Hóa đơn phải đang ở trạng thái PENDING (Invoice_Invoice_Calc_StatusNotMatched).
//  - Nếu là thao tác XÓA: hóa đơn CHƯA được cấp số (InvoiceNo còn rỗng)
//    (Invoice_Invoice_Calc_ExistInvoiceNo).
//  - Phải có ít nhất 1 dòng chi tiết (Invoice_Invoice_Calc_Input_InvoiceDtlTblNotFound/Invalid).
//  - ProductID không được trùng trong cùng hóa đơn
//    (Invoice_Invoice_Calc_Input_InvoiceDtl_ProductIDDuplicate).
//  - SpecCode không được trùng trong cùng hóa đơn khi dòng không có ProductID
//    (Invoice_Invoice_Calc_Input_InvoiceDtl_SpecCodeDuplicate).
// ok = true khi mọi quy tắc đều đạt; errors liệt kê mã lỗi vi phạm.
public record InvoiceCalcResult(
    bool ok,                 // hóa đơn hợp lệ để lưu
    string message,          // thông điệp kết luận
    List<string> errors,     // danh sách mã lỗi vi phạm (rỗng khi ok)
    int lineCount = 0);      // số dòng chi tiết đã kiểm tra
