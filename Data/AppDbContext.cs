using Microsoft.EntityFrameworkCore;
using MiniSign.Models;

namespace MiniSign.Data;

public class AppDbContext : DbContext
{
    private readonly Guid _orgId;
    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant) : base(options) => _orgId = tenant.OrgId;

    public DbSet<Org> Orgs => Set<Org>();
    public DbSet<Certificate> Certificates => Set<Certificate>();
    public DbSet<SignLog> SignLogs => Set<SignLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        if (Database.IsNpgsql()) b.HasDefaultSchema("minisign");
        b.Entity<Org>().HasIndex(x => x.ApiKey).IsUnique();
        b.Entity<Certificate>(e =>
        {
            e.HasIndex(x => x.Serial).IsUnique();           // verify công khai xuyên tenant theo serial
            e.Ignore(x => x.IsUsable);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<SignLog>(e =>
        {
            e.HasIndex(x => new { x.OrgId, x.CertificateId });
            e.HasOne(x => x.Certificate).WithMany().HasForeignKey(x => x.CertificateId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
    }

    public override int SaveChanges() { StampOrg(); return base.SaveChanges(); }
    public override Task<int> SaveChangesAsync(CancellationToken ct = default) { StampOrg(); return base.SaveChangesAsync(ct); }
    private void StampOrg()
    {
        foreach (var e in ChangeTracker.Entries<IOrgOwned>())
            if (e.State == EntityState.Added && e.Entity.OrgId == Guid.Empty) e.Entity.OrgId = _orgId;
    }
}
