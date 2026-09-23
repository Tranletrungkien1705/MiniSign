using Microsoft.EntityFrameworkCore;
using MiniSign.Data;
using MiniSign.Models;
using MiniSign.Services;
using Serilog;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
FleetObs.ConfigureLogger("minisign");

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.WebHost.UseUrls($"http://0.0.0.0:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}");

var conn = Environment.GetEnvironmentVariable("CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=minisign.db";
builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (DbUtil.IsPostgres(conn)) o.UseNpgsql(DbUtil.ToNpgsql(conn));
    else o.UseSqlite(conn);
});
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ISignService, SignService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<ILicenseService, LicenseService>();
builder.Services.AddFleetObs();
builder.Services.AddControllersWithViews();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await Seeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());

app.UseFleetObs();
FleetObs.ReportLicense(Environment.GetEnvironmentVariable("SSO_AUTHORITY") ?? "https://minisso.onrender.com", "minisign");

app.Use(async (ctx, next) =>
{
    var key = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(key)) ctx.Request.Cookies.TryGetValue(TenantContext.CookieName, out key);
    if (!string.IsNullOrWhiteSpace(key))
    {
        using var lookup = app.Services.CreateScope();
        var ldb = lookup.ServiceProvider.GetRequiredService<AppDbContext>();
        var org = await ldb.Orgs.FirstOrDefaultAsync(o => o.ApiKey == key);
        if (org != null) ctx.RequestServices.GetRequiredService<ITenantContext>().OrgId = org.Id;
    }
    await next();
});

app.UseStaticFiles();
app.MapGet("/healthz", () => "ok");
app.MapGet("/api/summary", async (ISignService svc) =>
{
    var d = await svc.DashboardAsync();
    return Results.Ok(new { certs = d.Certs, active = d.Active, signs = d.Signs });
});

// API ký: cần X-Api-Key (tenant) — chọn chứng thư theo id.
app.MapPost("/api/sign", async (SignDto dto, ISignService svc) =>
{
    var r = await svc.SignAsync(dto.CertId, dto.DocName ?? "document", dto.Content ?? "");
    return r.ok ? Results.Ok(new { hash = r.hash, signature = r.signature, serial = r.serial, algo = r.algo })
                : Results.BadRequest(new { error = r.msg });
});

// API verify: công khai (theo serial, xuyên tenant).
app.MapPost("/api/verify", async (VerifyDto dto, ISignService svc) =>
{
    var r = await svc.VerifyAsync(dto.Serial ?? "", dto.Content ?? "", dto.Signature ?? "");
    return Results.Ok(new { valid = r.valid, msg = r.msg, subject = r.subject, serial = r.serial, signedAt = r.signedAt });
});

// API ký SHA1withRSA — thuật toán hóa đơn điện tử (port từ InBrand signTTHD).
app.MapPost("/api/sign/sha1", async (SignDto dto, ISignService svc) =>
{
    var r = await svc.SignAsync(dto.CertId, dto.DocName ?? "document", dto.Content ?? "", SignAlgorithm.SHA1withRSA);
    return r.ok ? Results.Ok(new { hash = r.hash, signature = r.signature, serial = r.serial, algo = r.algo })
                : Results.BadRequest(new { error = r.msg });
});

// API verify SHA1withRSA (port từ InBrand SignatureVerify).
app.MapPost("/api/verify/sha1", async (VerifyDto dto, ISignService svc) =>
{
    var r = await svc.VerifyAsync(dto.Serial ?? "", dto.Content ?? "", dto.Signature ?? "", SignAlgorithm.SHA1withRSA);
    return Results.Ok(new { valid = r.valid, msg = r.msg, subject = r.subject, serial = r.serial, signedAt = r.signedAt });
});

// Tra cứu thông tin chứng thư công khai theo serial (port từ GetCertificateInfo).
app.MapGet("/api/certinfo/{serial}", async (string serial, ISignService svc) =>
{
    var info = await svc.CertInfoAsync(serial);
    return info == null ? Results.NotFound(new { error = "Không tìm thấy chứng thư." }) : Results.Ok(info);
});

// Hạn mức cấp số hóa đơn theo MST (port từ InBrand Invoice_license).
app.MapGet("/api/licenses", async (ILicenseService lic) =>
    Results.Ok((await lic.ListAsync()).Select(l => new
    {
        l.Id, l.MST, l.NetworkID, l.TotalQty, l.TotalQtyIssued, l.TotalQtyUsed, l.QtyRemain,
        l.FlagActive, l.LogLUBy, l.LogLUDTimeUTC
    })));

// Tăng hạn mức (port từ Invoice_license_IncreaseQtyX). Qty phải >= 0.
app.MapPost("/api/licenses/{mst}/increase", async (string mst, LicenseIncreaseDto dto, ILicenseService lic) =>
{
    var r = await lic.IncreaseQtyAsync(mst, dto.Qty, dto.By ?? "");
    return r.ok ? Results.Ok(new { r.license!.MST, r.license.TotalQty, r.license.TotalQtyIssued, r.license.TotalQtyUsed, r.license.QtyRemain })
                : Results.BadRequest(new { error = r.msg });
});

// Tính lại hạn mức đã cấp/đã dùng từ các mẫu số (port từ Invoice_license_TotalQtyIssued/Used).
app.MapPost("/api/licenses/{mst}/recompute", async (string mst, LicenseRecomputeDto dto, ILicenseService lic) =>
{
    var r = await lic.RecomputeAsync(mst, dto.By ?? "");
    return r.ok ? Results.Ok(new { r.license!.MST, r.license.TotalQty, r.license.TotalQtyIssued, r.license.TotalQtyUsed, r.license.QtyRemain })
                : Results.BadRequest(new { error = r.msg });
});

app.MapPost("/api/orgs/register", async (RegisterOrgDto dto, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(dto.Name)) return Results.BadRequest(new { error = "Cần Name." });
    var org = new Org { Name = dto.Name.Trim(), ApiKey = "sign_" + Guid.NewGuid().ToString("N") };
    db.Orgs.Add(org); await db.SaveChangesAsync();
    return Results.Ok(new { orgId = org.Id, apiKey = org.ApiKey });
});

// Import chứng thư số đại diện cho nhiều đối tác (subject = tên đại lý, mỗi cert sinh RSA keypair thật)
app.MapPost("/api/import/certs", async (List<ImportCertDto> rows, ISignService svc, AppDbContext db, ITenantContext tc) =>
{
    if (rows == null || rows.Count == 0) return Results.BadRequest(new { error = "Không có dữ liệu." });
    int added = 0, skipped = 0;
    var orgId = tc.OrgId;
    var existSubjects = db.Certificates.Where(c => c.OrgId == orgId).Select(c => c.Subject).ToHashSet();
    foreach (var row in rows)
    {
        if (string.IsNullOrWhiteSpace(row.Subject)) { skipped++; continue; }
        var subj = row.Subject.Trim();
        if (existSubjects.Contains(subj)) { skipped++; continue; }
        var (ok, _, _) = await svc.CreateCertAsync(subj, row.Years > 0 ? row.Years : 3);
        if (ok) { existSubjects.Add(subj); added++; } else skipped++;
    }
    return Results.Ok(new { added, skipped, total = added + skipped });
});

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();

record SignDto(int CertId, string? DocName, string? Content);
record VerifyDto(string? Serial, string? Content, string? Signature);
record RegisterOrgDto(string Name);
record ImportCertDto(string? Subject, int Years);
record LicenseIncreaseDto(long Qty, string? By);
record LicenseRecomputeDto(string? By);
