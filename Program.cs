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
builder.Services.AddFleetObs();
builder.Services.AddControllersWithViews();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await Seeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());

app.UseFleetObs();

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

app.MapPost("/api/orgs/register", async (RegisterOrgDto dto, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(dto.Name)) return Results.BadRequest(new { error = "Cần Name." });
    var org = new Org { Name = dto.Name.Trim(), ApiKey = "sign_" + Guid.NewGuid().ToString("N") };
    db.Orgs.Add(org); await db.SaveChangesAsync();
    return Results.Ok(new { orgId = org.Id, apiKey = org.ApiKey });
});

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();

record SignDto(int CertId, string? DocName, string? Content);
record VerifyDto(string? Serial, string? Content, string? Signature);
record RegisterOrgDto(string Name);
