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
public class ApiV1Controller(ISignService svc, IInvoiceService invoices, ICache cache, ITenantContext tenant) : ControllerBase
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

    // Hủy hóa đơn (port từ InBrand Invoice_Invoice_Cancel). Chỉ hủy khi PENDING/APPROVED.
    [HttpPost("invoices/{id:int}/cancel")]
    public async Task<IActionResult> CancelInvoice(int id, [FromBody] InvoiceCancelReq r)
    {
        var res = await invoices.CancelAsync(id, r.Reason ?? "", r.CancelBy ?? "");
        return res.ok ? Ok(InvDto(res.invoice!)) : BadRequest(new { error = res.msg, invoice = res.invoice == null ? null : InvDto(res.invoice) });
    }

    // Thống kê hóa đơn theo trạng thái vòng đời (port từ InBrand InvoiceStatus).
    [HttpGet("invoices/lifecycle")]
    public async Task<IActionResult> InvoiceLifecycle()
    {
        var d = await invoices.LifecycleDashboardAsync();
        return Ok(new { d.Total, d.Pending, d.Approved, d.Issued, d.Canceled });
    }

    private static object InvDto(Invoice i) => new
    {
        i.Id, i.InvoiceCode, i.TaxCode, i.CustomerName, i.TotalValPmt,
        status = (int)i.SignStatus, statusText = i.SignStatus.ToString(),
        lifecycle = (int)i.Status, lifecycleText = i.Status.ToString(),
        i.SignBy, i.SignDTimeUTC, i.SignSerial, i.SignError,
        i.CancelBy, i.CancelDTimeUTC, i.CancelReason, i.CreatedAt, i.UpdatedAt
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
