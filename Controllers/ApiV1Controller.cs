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
public class ApiV1Controller(ISignService svc, ICache cache, ITenantContext tenant) : ControllerBase
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
            c.Id, c.Subject, c.Serial, c.Algorithm, c.NotBefore, c.NotAfter,
            status = (int)c.Status, statusText = Ui.Cert(c).text, statusCss = Ui.Cert(c).css, usable = c.IsUsable
        }));

    [HttpPost("certs")]
    public async Task<IActionResult> CreateCert([FromBody] CertReq r)
    {
        if (string.IsNullOrWhiteSpace(r.Subject)) return BadRequest(new { error = "Cần chủ thể (CN)." });
        var (ok, msg, id) = await svc.CreateCertAsync(r.Subject.Trim(), r.Years <= 0 ? 3 : r.Years);
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

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyReq r)
    {
        var res = await svc.VerifyAsync(r.Serial ?? "", r.Content ?? "", r.Signature ?? "");
        return Ok(new { res.valid, res.msg, res.subject, res.serial, res.signedAt });
    }

    [HttpGet("signlogs")]
    public async Task<IActionResult> SignLogs([FromQuery] int? certId)
        => Ok((await svc.SignLogsAsync(certId)).Select(l => new { l.Id, cert = l.Certificate?.Subject, serial = l.Certificate?.Serial, l.DocName, l.Hash, l.ContentLength, l.CreatedAt }));
}

public record DashDto(int Certs, int Active, int Signs);

public class CertReq { public string Subject { get; set; } = ""; public int Years { get; set; } }
public class SignReq { public int CertId { get; set; } public string? DocName { get; set; } public string? Content { get; set; } }
public class VerifyReq { public string? Serial { get; set; } public string? Content { get; set; } public string? Signature { get; set; } }
