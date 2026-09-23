using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniSign.Data;
using MiniSign.Models;
using MiniSign.Services;

namespace MiniSign.Controllers;

public class HomeController : Controller
{
    // SPA React ở "/". Trang xác thực công khai /Verify (Razor) giữ nguyên.
    public IActionResult Index() => Redirect("/index.html");
}

public class LegacyController(ISignService svc) : Controller
{
    public async Task<IActionResult> Index() { ViewBag.Dash = await svc.DashboardAsync(); return View("~/Views/Home/Index.cshtml"); }
}

public class CertController(ISignService svc) : Controller
{
    public async Task<IActionResult> Index() => View(await svc.CertsAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string subject, int years, string? taxCode)
    {
        var (ok, msg, _) = await svc.CreateCertAsync(subject, years, taxCode ?? "");
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(int id)
    {
        var (ok, msg) = await svc.RevokeAsync(id);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Detail(int id)
    {
        var c = await svc.GetCertAsync(id);
        if (c == null) return NotFound();
        ViewBag.Logs = await svc.SignLogsAsync(id);
        return View(c);
    }
}

public class SignController(ISignService svc) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.Certs = await svc.CertsAsync();
        ViewBag.Logs = await svc.SignLogsAsync(null);
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Do(int certId, string? docName, string content)
    {
        var r = await svc.SignAsync(certId, docName ?? "", content ?? "");
        if (r.ok) { ViewBag.Result = r; }
        else TempData["Error"] = r.msg;
        ViewBag.Certs = await svc.CertsAsync();
        ViewBag.Logs = await svc.SignLogsAsync(null);
        ViewBag.Content = content; ViewBag.DocName = docName; ViewBag.CertId = certId;
        return View("Index");
    }
}

public class VerifyController(ISignService svc) : Controller
{
    public IActionResult Index() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Do(string serial, string content, string signature)
    {
        ViewBag.Result = await svc.VerifyAsync(serial ?? "", content ?? "", signature ?? "");
        ViewBag.Serial = serial; ViewBag.Content = content; ViewBag.Signature = signature;
        return View("Index");
    }
}

// Tra cứu & xác thực chứng thư theo serial + MST (port từ InBrand CertificateInfo).
public class RegistryController(ISignService svc) : Controller
{
    public IActionResult Index() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Do(string serial, string taxCode)
    {
        ViewBag.Result = await svc.ValidateAsync(serial ?? "", taxCode ?? "");
        ViewBag.Serial = serial; ViewBag.TaxCode = taxCode;
        return View("Index");
    }
}

// Ký hóa đơn/tài liệu theo vòng đời trạng thái (port từ InBrand OS_Invoice_InvoiceTemp).
public class InvoiceController(IInvoiceService invoices, ISignService svc) : Controller
{
    public async Task<IActionResult> Index(int? status)
    {
        ViewBag.Certs = await svc.CertsAsync();
        ViewBag.Dash = await invoices.DashboardAsync();
        ViewBag.Lifecycle = await invoices.LifecycleDashboardAsync();
        ViewBag.Status = status;
        return View(await invoices.ListAsync(status.HasValue ? (SignStatus)status.Value : null));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string invoiceCode, string? taxCode, string? customerName, string content, decimal totalValPmt)
    {
        var r = await invoices.CreateAsync(invoiceCode, taxCode ?? "", customerName ?? "", content ?? "", totalValPmt);
        TempData[r.ok ? "Success" : "Error"] = r.msg;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Sign(int id, int certId, string? signBy)
    {
        var r = await invoices.SignAsync(id, certId, signBy ?? "");
        TempData[r.ok ? "Success" : "Error"] = r.msg;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, int status, string? error)
    {
        var r = await invoices.SetStatusAsync(id, (SignStatus)status, error);
        TempData[r.ok ? "Success" : "Error"] = r.msg;
        return RedirectToAction(nameof(Index));
    }

    // Hủy hóa đơn (port từ InBrand Invoice_Invoice_Cancel).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? reason, string? cancelBy)
    {
        var r = await invoices.CancelAsync(id, reason ?? "", cancelBy ?? "");
        TempData[r.ok ? "Success" : "Error"] = r.msg;
        return RedirectToAction(nameof(Index));
    }
}

public class OrgController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        Request.Cookies.TryGetValue(TenantContext.CookieName, out var curKey);
        ViewBag.CurrentKey = curKey ?? TenantContext.DefaultApiKey;
        return View(await db.Orgs.IgnoreQueryFilters().OrderBy(o => o.CreatedAt).ToListAsync());
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { TempData["Error"] = "Cần tên tổ chức."; return RedirectToAction(nameof(Index)); }
        var org = new Org { Name = name.Trim(), ApiKey = "sign_" + Guid.NewGuid().ToString("N") };
        db.Orgs.Add(org); await db.SaveChangesAsync();
        SetCookies(org.ApiKey, org.Name);
        TempData["Success"] = $"Đã tạo & chuyển sang \"{org.Name}\"."; return RedirectToAction("Index", "Home");
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Switch(string apiKey)
    {
        var org = await db.Orgs.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.ApiKey == apiKey);
        if (org == null) { TempData["Error"] = "Không tìm thấy."; return RedirectToAction(nameof(Index)); }
        SetCookies(org.ApiKey, org.Name); return RedirectToAction("Index", "Home");
    }
    private void SetCookies(string k, string n)
    {
        var o = new CookieOptions { IsEssential = true, Expires = DateTimeOffset.UtcNow.AddDays(30) };
        Response.Cookies.Append(TenantContext.CookieName, k, o); Response.Cookies.Append("org_name", n, o);
    }
}
