using Microsoft.AspNetCore.Mvc;
using MiniSign.Data;
using MiniSign.Models;
using MiniSign.Services;

namespace MiniSign.Controllers;

/// <summary>
/// API JSON cho SPA React. DTO phẳng. Dashboard cache Redis 30s theo tenant (X-Cache).
/// Cổng ký số: chứng thư RSA (Active/Revoked/Expired) → ký tài liệu (SHA256withRSA) → xác thực chữ ký công khai theo serial.
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class ApiV1Controller(ISignService svc, IInvoiceService invoices, ILicenseService licenses, ICache cache, ITenantContext tenant) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var key = $"sign:dash:{tenant.OrgId}";
        var hit = await cache.GetAsync<DashDto>(key);
        if (hit != null) { Response.Headers["X-Cache"] = "HIT"; return Ok(hit); }
        var d = await svc.DashboardAsync();
        var dto = new DashDto(d.Certs, d.Active, d.Signs);
        await cache.SetAsync(key, dto, TimeSpan.FromSeconds(30));
        Response.Headers["X-Cache"] = "MISS";
        return Ok(dto);
    }

    [HttpGet("certs")]
    public async Task<IActionResult> Certs()
        => Ok((await svc.CertsAsync()).Select(c => new
        {
            c.Id, c.Subject, c.TaxCode, c.Serial, c.Algorithm, c.NotBefore, c.NotAfter,
            status = (int)c.Status, statusText = Ui.Cert(c).text, statusCss = Ui.Cert(c).css, usable = c.IsUsable
        }));

    [HttpPost("certs")]
    public async Task<IActionResult> CreateCert([FromBody] CertReq r)
    {
        if (string.IsNullOrWhiteSpace(r.Subject)) return BadRequest(new { error = "Cần chủ thể (CN)." });
        var (ok, msg, id) = await svc.CreateCertAsync(r.Subject.Trim(), r.Years <= 0 ? 3 : r.Years, r.TaxCode ?? "");
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("certs/{id:int}/revoke")]
    public async Task<IActionResult> Revoke(int id)
    {
        var (ok, msg) = await svc.RevokeAsync(id);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    [HttpPost("sign")]
    public async Task<IActionResult> Sign([FromBody] SignReq r)
    {
        var res = await svc.SignAsync(r.CertId, r.DocName ?? "document", r.Content ?? "");
        return res.ok ? Ok(new { ok = true, res.hash, res.signature, res.serial, res.algo }) : BadRequest(new { ok = false, error = res.msg });
    }

    // Ký SHA1withRSA — thuật toán hóa đơn điện tử (port từ InBrand signTTHD).
    [HttpPost("sign/sha1")]
    public async Task<IActionResult> SignSha1([FromBody] SignReq r)
    {
        var res = await svc.SignAsync(r.CertId, r.DocName ?? "document", r.Content ?? "", SignAlgorithm.SHA1withRSA);
        return res.ok ? Ok(new { ok = true, res.hash, res.signature, res.serial, res.algo }) : BadRequest(new { ok = false, error = res.msg });
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyReq r)
    {
        var res = await svc.VerifyAsync(r.Serial ?? "", r.Content ?? "", r.Signature ?? "");
        return Ok(new { res.valid, res.msg, res.subject, res.serial, res.signedAt });
    }

    // Xác thực SHA1withRSA (port từ InBrand SignatureVerify).
    [HttpPost("verify/sha1")]
    public async Task<IActionResult> VerifySha1([FromBody] VerifyReq r)
    {
        var res = await svc.VerifyAsync(r.Serial ?? "", r.Content ?? "", r.Signature ?? "", SignAlgorithm.SHA1withRSA);
        return Ok(new { res.valid, res.msg, res.subject, res.serial, res.signedAt });
    }

    // Tra cứu thông tin chứng thư công khai theo serial (port từ GetCertificateInfo).
    [HttpGet("certinfo/{serial}")]
    public async Task<IActionResult> CertInfo(string serial)
    {
        var info = await svc.CertInfoAsync(serial);
        return info == null ? NotFound(new { error = "Không tìm thấy chứng thư." }) : Ok(info);
    }

    // Tra cứu & xác thực chứng thư theo serial + MST (port từ InBrand CertificateInfo:
    // "Check SerialNumber và MST có trong hệ thống").
    [HttpPost("certs/validate")]
    public async Task<IActionResult> ValidateCert([FromBody] ValidateReq r)
        => Ok(await svc.ValidateAsync(r.Serial ?? "", r.TaxCode ?? ""));

    [HttpGet("signlogs")]
    public async Task<IActionResult> SignLogs([FromQuery] int? certId)
        => Ok((await svc.SignLogsAsync(certId)).Select(l => new { l.Id, cert = l.Certificate?.Subject, serial = l.Certificate?.Serial, l.DocName, l.Hash, l.ContentLength, l.CreatedAt }));

    // ===== Hóa đơn ký số theo vòng đời trạng thái (port từ InBrand OS_Invoice_InvoiceTemp) =====

    [HttpGet("invoices")]
    public async Task<IActionResult> Invoices([FromQuery] int? status)
        => Ok((await invoices.ListAsync(status.HasValue ? (SignStatus)status.Value : null)).Select(InvDto));

    [HttpGet("invoices/dashboard")]
    public async Task<IActionResult> InvoiceDashboard()
    {
        var d = await invoices.DashboardAsync();
        return Ok(new { d.Total, d.Pending, d.Processing, d.Signed, d.Failed });
    }

    [HttpPost("invoices")]
    public async Task<IActionResult> CreateInvoice([FromBody] InvoiceReq r)
    {
        var res = await invoices.CreateAsync(r.InvoiceCode ?? "", r.TaxCode ?? "", r.CustomerName ?? "", r.Content ?? "", r.TotalValPmt);
        return res.ok ? Ok(InvDto(res.invoice!)) : BadRequest(new { error = res.msg });
    }

    // Ký hóa đơn bằng chứng thư (SHA1withRSA — thuật toán hóa đơn điện tử).
    [HttpPost("invoices/{id:int}/sign")]
    public async Task<IActionResult> SignInvoice(int id, [FromBody] InvoiceSignReq r)
    {
        var res = await invoices.SignAsync(id, r.CertId, r.SignBy ?? "");
        return res.ok ? Ok(InvDto(res.invoice!)) : BadRequest(new { error = res.msg, invoice = res.invoice == null ? null : InvDto(res.invoice) });
    }

    // Đổi trạng thái ký (port từ UpdMultiSignStatus). Chỉ phát hành tiếp khi trước đó FAILED/PROCESSING.
    [HttpPost("invoices/{id:int}/status")]
    public async Task<IActionResult> SetInvoiceStatus(int id, [FromBody] InvoiceStatusReq r)
    {
        var res = await invoices.SetStatusAsync(id, (SignStatus)r.Status, r.Error);
        return res.ok ? Ok(InvDto(res.invoice!)) : BadRequest(new { error = res.msg });
    }

    // Cấp số hóa đơn (port từ InBrand Invoice_Invoice_AllocatedInvX_New20190917).
    // Chỉ cấp khi PENDING + chưa có InvoiceNo; ngày HĐ >= LastInvoiceDateUTC/EffDateStart và không ở tương lai.
    [HttpPost("invoices/{id:int}/allocate")]
    public async Task<IActionResult> AllocateInvoice(int id, [FromBody] InvoiceAllocateReq r)
    {
        var res = await invoices.AllocateAsync(id, r.TInvoiceCode ?? "", r.InvoiceDateUTC ?? default, r.AllocateBy ?? "");
        return res.ok ? Ok(InvDto(res.invoice!)) : BadRequest(new { error = res.msg, invoice = res.invoice == null ? null : InvDto(res.invoice) });
    }

    // Danh sách mẫu số hóa đơn (dải số được cấp phát).
    [HttpGet("invoices/templates")]
    public async Task<IActionResult> InvoiceTemplates()
        => Ok((await invoices.TemplatesAsync()).Select(t => new
        {
            t.Id, t.TInvoiceCode, t.TaxCode, t.InvoiceSerial, t.StartInvoiceNo, t.EndInvoiceNo,
            t.QtyUsed, t.QtyRemain, t.LastInvoiceNo, t.LastInvoiceDateUTC, t.EffDateStart, t.FlagActive
        }));

    // Duyệt hóa đơn (port từ InBrand Invoice_Invoice_ApprovedMultiX). Chỉ duyệt khi PENDING + có InvoiceNo.
    [HttpPost("invoices/{id:int}/approve")]
    public async Task<IActionResult> ApproveInvoice(int id, [FromBody] InvoiceApproveReq r)
    {
        var res = await invoices.ApproveAsync(id, r.InvoiceNo ?? "", r.ApprBy ?? "");
        return res.ok ? Ok(InvDto(res.invoice!)) : BadRequest(new { error = res.msg, invoice = res.invoice == null ? null : InvDto(res.invoice) });
    }

    // Phát hành hóa đơn (port từ InBrand Invoice_Invoice_IssuedXMulti). Chỉ phát hành khi APPROVED.
    [HttpPost("invoices/{id:int}/issue")]
    public async Task<IActionResult> IssueInvoice(int id, [FromBody] InvoiceIssueReq r)
    {
        var res = await invoices.IssueAsync(id, r.IssuedBy ?? "");
        return res.ok ? Ok(InvDto(res.invoice!)) : BadRequest(new { error = res.msg, invoice = res.invoice == null ? null : InvDto(res.invoice) });
    }

    // Hủy hóa đơn (port từ InBrand Invoice_Invoice_Cancel). Chỉ hủy khi PENDING/APPROVED.
    [HttpPost("invoices/{id:int}/cancel")]
    public async Task<IActionResult> CancelInvoice(int id, [FromBody] InvoiceCancelReq r)
    {
        var res = await invoices.CancelAsync(id, r.Reason ?? "", r.CancelBy ?? "");
        return res.ok ? Ok(InvDto(res.invoice!)) : BadRequest(new { error = res.msg, invoice = res.invoice == null ? null : InvDto(res.invoice) });
    }

    // Xóa hóa đơn (port từ InBrand Invoice_Invoice_Deleted). Chỉ xóa khi ISSUED; đã DELETED thì bỏ qua.
    [HttpPost("invoices/{id:int}/delete")]
    public async Task<IActionResult> DeleteInvoice(int id, [FromBody] InvoiceDeleteReq r)
    {
        var res = await invoices.DeleteAsync(id, r.Reason ?? "", r.DeleteBy ?? "", r.AttachedDelFilePath);
        return res.ok ? Ok(InvDto(res.invoice!)) : BadRequest(new { error = res.msg, invoice = res.invoice == null ? null : InvDto(res.invoice) });
    }

    // Đánh dấu hóa đơn đã bị thay thế/điều chỉnh (port từ InBrand Invoice_Invoice_ChangeX).
    // Chỉ khi ISSUED + FlagChange đang Active; ghi FlagChange=Inactive + người/thời gian/lý do.
    [HttpPost("invoices/{id:int}/change")]
    public async Task<IActionResult> ChangeInvoice(int id, [FromBody] InvoiceChangeReq r)
    {
        var res = await invoices.ChangeAsync(id, r.Reason ?? "", r.ChangeBy ?? "");
        return res.ok ? Ok(InvDto(res.invoice!)) : BadRequest(new { error = res.msg, invoice = res.invoice == null ? null : InvDto(res.invoice) });
    }

    // Đánh dấu hóa đơn là ĐIỀU CHỈNH/THAY THẾ một hóa đơn khác (port từ InBrand
    // Invoice_Invoice_Save_Adj / Invoice_Invoice_Save_Replace → Invoice_Invoice_SaveX).
    // Quy tắc: AdjIncrease/AdjDecrease bắt buộc có RefNo; Adj → RefNo phải ISSUED;
    // Replace → RefNo phải DELETED; một hóa đơn gốc chỉ được điều chỉnh/thay thế một lần.
    [HttpPost("invoices/{id:int}/adjust")]
    public async Task<IActionResult> AdjustInvoice(int id, [FromBody] InvoiceAdjustReq r)
    {
        var res = await invoices.AdjustAsync(id, r.RefNo ?? "", (SourceInvoiceCode)r.SourceInvoiceCode, (InvoiceAdjType)r.InvoiceAdjType, r.Reason ?? "", r.AdjustBy ?? "");
        return res.ok ? Ok(InvDto(res.invoice!)) : BadRequest(new { error = res.msg, invoice = res.invoice == null ? null : InvDto(res.invoice) });
    }

    // Cập nhật hóa đơn SAU KHI CẤP SỐ (port từ InBrand Invoice_Invoice_UpdAfterAllocatedX).
    // Chỉ khi PENDING + đã có InvoiceNo + SourceInvoiceCode = Root; ngày HĐ không tương lai và
    // nằm giữa ngày hóa đơn liền trước/liền sau cùng mẫu số. Cập nhật phương thức thanh toán,
    // thông tin khách hàng và bảng kê giá trị theo thuế suất (VAT 5%/10%, không chịu thuế).
    [HttpPost("invoices/{id:int}/update-after-allocated")]
    public async Task<IActionResult> UpdateAfterAllocated(int id, [FromBody] InvoiceUpdateBody r)
    {
        var req = new InvoiceUpdateReq(r.PaymentMethodCode, r.CustomerNNTCode, r.CustomerNNTName, r.CustomerNNTAddress,
            r.CustomerNNTPhone, r.CustomerNNTBankName, r.CustomerNNTEmail, r.CustomerNNTAccNo, r.CustomerNNTBuyerName,
            r.CustomerMST, r.InvoiceDateUTC, r.TotalValInvoice, r.TotalValVAT, r.TotalValPmt, r.ValGoodsNotTaxable,
            r.ValGoodsNotChargeTax, r.ValGoodsVAT5, r.ValVAT5, r.ValGoodsVAT10, r.ValVAT10);
        var res = await invoices.UpdateAfterAllocatedAsync(id, req);
        return res.ok ? Ok(InvDto(res.invoice!)) : BadRequest(new { error = res.msg, invoice = res.invoice == null ? null : InvDto(res.invoice) });
    }

    // Đánh dấu hóa đơn ĐÃ GỬI EMAIL (port từ InBrand Invoice_Invoice_UpdMailSentDTimeUTCX).
    // Chỉ khi ISSUED + chưa gửi email; ghi MailSentDTimeUTC + SendEmailDTimeUTC + SendEmailBy.
    [HttpPost("invoices/{id:int}/mail-sent")]
    public async Task<IActionResult> MarkMailSent(int id, [FromBody] InvoiceMailSentReq r)
    {
        var res = await invoices.MarkMailSentAsync(id, r.SendBy ?? "");
        return res.ok ? Ok(InvDto(res.invoice!)) : BadRequest(new { error = res.msg, invoice = res.invoice == null ? null : InvDto(res.invoice) });
    }

    // Đẩy hóa đơn LÊN CỔNG THÔNG TIN ĐIỆN TỬ (port từ InBrand Invoice_Invoice_Issued_UpdFlagPushOutSiteX).
    // Chỉ khi ISSUED/DELETED + FlagPushOutSite còn rỗng; ghi FlagPushOutSite + PushOutSiteBy.
    [HttpPost("invoices/{id:int}/push-out-site")]
    public async Task<IActionResult> PushOutSite(int id, [FromBody] InvoicePushOutSiteReq r)
    {
        var res = await invoices.PushOutSiteAsync(id, r.PushBy ?? "");
        return res.ok ? Ok(InvDto(res.invoice!)) : BadRequest(new { error = res.msg, invoice = res.invoice == null ? null : InvDto(res.invoice) });
    }

    // Thống kê hóa đơn theo trạng thái vòng đời (port từ InBrand InvoiceStatus).
    [HttpGet("invoices/lifecycle")]
    public async Task<IActionResult> InvoiceLifecycle()
    {
        var d = await invoices.LifecycleDashboardAsync();
        return Ok(new { d.Total, d.Pending, d.Approved, d.Issued, d.Canceled, d.Deleted });
    }

    // ===== Hạn mức cấp số hóa đơn theo MST (port từ InBrand Invoice_license) =====

    [HttpGet("licenses")]
    public async Task<IActionResult> Licenses()
        => Ok((await licenses.ListAsync()).Select(LicDto));

    [HttpGet("licenses/dashboard")]
    public async Task<IActionResult> LicenseDashboard()
    {
        var d = await licenses.DashboardAsync();
        return Ok(new { d.Total, d.TotalQty, d.TotalQtyIssued, d.TotalQtyUsed, d.QtyRemain });
    }

    // Tăng hạn mức (port từ Invoice_license_IncreaseQtyX). Qty phải >= 0; MST chưa có thì tự tạo.
    [HttpPost("licenses/{mst}/increase")]
    public async Task<IActionResult> IncreaseLicense(string mst, [FromBody] LicenseIncreaseReq r)
    {
        var res = await licenses.IncreaseQtyAsync(mst, r.Qty, r.By ?? "");
        return res.ok ? Ok(LicDto(res.license!)) : BadRequest(new { error = res.msg });
    }

    // Tính lại hạn mức đã cấp/đã dùng từ các mẫu số (port từ Invoice_license_TotalQtyIssued/Used).
    [HttpPost("licenses/{mst}/recompute")]
    public async Task<IActionResult> RecomputeLicense(string mst, [FromBody] LicenseRecomputeReq r)
    {
        var res = await licenses.RecomputeAsync(mst, r.By ?? "");
        return res.ok ? Ok(LicDto(res.license!)) : BadRequest(new { error = res.msg, license = res.license == null ? null : LicDto(res.license) });
    }

    private static object LicDto(InvoiceLicense l) => new
    {
        l.Id, l.MST, l.NetworkID, l.TotalQty, l.TotalQtyIssued, l.TotalQtyUsed, l.QtyRemain,
        l.FlagActive, l.LogLUBy, l.LogLUDTimeUTC, l.CreatedAt
    };

    private static object InvDto(Invoice i) => new
    {
        i.Id, i.InvoiceCode, i.TaxCode, i.CustomerName, i.TotalValPmt,
        status = (int)i.SignStatus, statusText = i.SignStatus.ToString(),
        lifecycle = (int)i.Status, lifecycleText = i.Status.ToString(),
        i.SignBy, i.SignDTimeUTC, i.SignSerial, i.SignError,
        i.InvoiceNo, i.TInvoiceCode, i.InvoiceDateUTC, i.InvoiceNoBy, i.InvoiceNoDTimeUTC,
        i.ApprBy, i.ApprDTimeUTC, i.IssuedBy, i.IssuedDTimeUTC,
        i.CancelBy, i.CancelDTimeUTC, i.CancelReason,
        i.DeleteBy, i.DeleteDTimeUTC, i.DeleteReason, i.AttachedDelFilePath,
        i.FlagChange, i.ChangeBy, i.ChangeDTimeUTC, i.ChangeReason, i.CreatedAt, i.UpdatedAt,
        sourceInvoiceCode = (int)i.SourceInvoiceCode, sourceInvoiceCodeText = i.SourceInvoiceCode.ToString(),
        invoiceAdjType = (int)i.InvoiceAdjType, invoiceAdjTypeText = i.InvoiceAdjType.ToString(), i.RefNo,
        i.PaymentMethodCode, i.CustomerNNTCode, i.CustomerNNTName, i.CustomerNNTAddress, i.CustomerNNTPhone,
        i.CustomerNNTBankName, i.CustomerNNTEmail, i.CustomerNNTAccNo, i.CustomerNNTBuyerName, i.CustomerMST,
        i.TotalValInvoice, i.TotalValVAT, i.ValGoodsNotTaxable, i.ValGoodsNotChargeTax,
        i.ValGoodsVAT5, i.ValVAT5, i.ValGoodsVAT10, i.ValVAT10,
        i.UpdAfterAllocatedBy, i.UpdAfterAllocatedDTimeUTC,
        i.MailSentDTimeUTC, i.SendEmailDTimeUTC, i.SendEmailBy, i.MailLogLUBy, i.MailLogLUDTimeUTC,
        i.FlagPushOutSite, i.PushOutSiteBy, i.PushOutSiteDTimeUTC
    };
}

public record DashDto(int Certs, int Active, int Signs);

public class CertReq { public string Subject { get; set; } = ""; public int Years { get; set; } public string? TaxCode { get; set; } }
public class SignReq { public int CertId { get; set; } public string? DocName { get; set; } public string? Content { get; set; } }
public class VerifyReq { public string? Serial { get; set; } public string? Content { get; set; } public string? Signature { get; set; } }
public class ValidateReq { public string? Serial { get; set; } public string? TaxCode { get; set; } }
public class InvoiceReq { public string? InvoiceCode { get; set; } public string? TaxCode { get; set; } public string? CustomerName { get; set; } public string? Content { get; set; } public decimal TotalValPmt { get; set; } }
public class InvoiceSignReq { public int CertId { get; set; } public string? SignBy { get; set; } }
public class InvoiceStatusReq { public int Status { get; set; } public string? Error { get; set; } }
public class InvoiceCancelReq { public string? Reason { get; set; } public string? CancelBy { get; set; } }
public class InvoiceDeleteReq { public string? Reason { get; set; } public string? DeleteBy { get; set; } public string? AttachedDelFilePath { get; set; } }
public class InvoiceChangeReq { public string? Reason { get; set; } public string? ChangeBy { get; set; } }
public class InvoiceAdjustReq { public string? RefNo { get; set; } public int SourceInvoiceCode { get; set; } public int InvoiceAdjType { get; set; } public string? Reason { get; set; } public string? AdjustBy { get; set; } }
public class InvoiceApproveReq { public string? InvoiceNo { get; set; } public string? ApprBy { get; set; } }
public class InvoiceAllocateReq { public string? TInvoiceCode { get; set; } public DateTime? InvoiceDateUTC { get; set; } public string? AllocateBy { get; set; } }
public class InvoiceIssueReq { public string? IssuedBy { get; set; } }
public class InvoiceMailSentReq { public string? SendBy { get; set; } }
public class InvoicePushOutSiteReq { public string? PushBy { get; set; } }
public class InvoiceUpdateBody
{
    public string? PaymentMethodCode { get; set; }
    public string? CustomerNNTCode { get; set; }
    public string? CustomerNNTName { get; set; }
    public string? CustomerNNTAddress { get; set; }
    public string? CustomerNNTPhone { get; set; }
    public string? CustomerNNTBankName { get; set; }
    public string? CustomerNNTEmail { get; set; }
    public string? CustomerNNTAccNo { get; set; }
    public string? CustomerNNTBuyerName { get; set; }
    public string? CustomerMST { get; set; }
    public DateTime? InvoiceDateUTC { get; set; }
    public decimal TotalValInvoice { get; set; }
    public decimal TotalValVAT { get; set; }
    public decimal TotalValPmt { get; set; }
    public decimal ValGoodsNotTaxable { get; set; }
    public decimal ValGoodsNotChargeTax { get; set; }
    public decimal ValGoodsVAT5 { get; set; }
    public decimal ValVAT5 { get; set; }
    public decimal ValGoodsVAT10 { get; set; }
    public decimal ValVAT10 { get; set; }
}

public class LicenseIncreaseReq { public long Qty { get; set; } public string? By { get; set; } }
public class LicenseRecomputeReq { public string? By { get; set; } }
