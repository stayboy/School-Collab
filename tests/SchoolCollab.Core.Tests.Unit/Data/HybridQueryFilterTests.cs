using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Tests.Unit.Data;

/// <summary>
/// Tests for the hybrid (tenant-or-global) query filter on <see cref="IHybridTenantEntity"/>.
/// Covers AC-5 (shared NULL blueprint visible to all tenants) and AC-6 (tenant-owned
/// rows isolated from other tenants). See global-tenant-filter.md §3.3 / §6.2.
/// </summary>
[TestClass]
public class HybridQueryFilterTests
{
    [TestInitialize]
    public void Reset()
    {
        TenantContextAccessor.GuardSuppressed.Value = false;
    }

    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // AC-5: shared NULL-blueprint row is visible to ALL tenants; tenant-owned rows
    // are visible only to the owning tenant.
    [TestMethod]
    public async Task HybridFilter_SurfacesSharedBlueprint_ToAllTenants_AndIsolatesOwned()
    {
        var provider = new TenantProvider();
        var accessor = new TenantContextAccessor(provider);

        using var db = CreateDb(provider);

        var sharedId = Guid.NewGuid();
        var aOwnedId = Guid.NewGuid();
        var bOwnedId = Guid.NewGuid();

        // Under tenant A: insert a shared (null) row + an A-owned row.
        provider.SetTenant(new TenantContext(TenantA, "A", TenantType.School));
        db.HybridEntities.Add(new HybridEntity { Id = sharedId, TenantId = null, Name = "Shared" });
        db.HybridEntities.Add(new HybridEntity { Id = aOwnedId, TenantId = TenantA, Name = "A-owned" });
        await db.SaveChangesAsync();

        // Under tenant B: insert a B-owned row.
        await accessor.RunWithExplicitTenantAsync(TenantB, async ct =>
        {
            db.HybridEntities.Add(new HybridEntity { Id = bOwnedId, TenantId = TenantB, Name = "B-owned" });
            await db.SaveChangesAsync();
            return true;
        }, default);

        // Tenant A sees: shared + A-owned (NOT B-owned).
        provider.SetTenant(new TenantContext(TenantA, "A", TenantType.School));
        (await db.HybridEntities.Select(r => r.Id).ToListAsync())
            .Should().BeEquivalentTo(new[] { sharedId, aOwnedId });

        // Tenant B sees: shared + B-owned (NOT A-owned).
        provider.SetTenant(new TenantContext(TenantB, "B", TenantType.School));
        (await db.HybridEntities.Select(r => r.Id).ToListAsync())
            .Should().BeEquivalentTo(new[] { sharedId, bOwnedId });
    }

    // AC-6: a tenant-owned row is hidden from other tenants.
    [TestMethod]
    public async Task HybridFilter_HidesTenantOwnedRows_FromOtherTenants()
    {
        var provider = new TenantProvider();
        var accessor = new TenantContextAccessor(provider);

        using var db = CreateDb(provider);

        var aOwnedId = Guid.NewGuid();

        await accessor.RunWithExplicitTenantAsync(TenantA, async ct =>
        {
            db.HybridEntities.Add(new HybridEntity { Id = aOwnedId, TenantId = TenantA, Name = "A-only" });
            await db.SaveChangesAsync();
            return true;
        }, default);

        // Tenant B sees nothing (no shared rows, no B-owned rows).
        provider.SetTenant(new TenantContext(TenantB, "B", TenantType.School));
        (await db.HybridEntities.ToListAsync()).Should().BeEmpty();
    }

    // Default-tenant (Guid.Empty) sees only shared (null) rows — not tenant-owned.
    [TestMethod]
    public async Task HybridFilter_DefaultTenantSeesOnlySharedRows()
    {
        var provider = new TenantProvider();
        var accessor = new TenantContextAccessor(provider);

        using var db = CreateDb(provider);

        var sharedId = Guid.NewGuid();
        var aOwnedId = Guid.NewGuid();

        await accessor.RunWithExplicitTenantAsync(TenantA, async ct =>
        {
            db.HybridEntities.Add(new HybridEntity { Id = sharedId, TenantId = null, Name = "Shared" });
            db.HybridEntities.Add(new HybridEntity { Id = aOwnedId, TenantId = TenantA, Name = "A-owned" });
            await db.SaveChangesAsync();
            return true;
        }, default);

        // Default tenant (Guid.Empty) sees only the shared blueprint row.
        provider.Clear();
        (await db.HybridEntities.Select(r => r.Id).ToListAsync())
            .Should().BeEquivalentTo(new[] { sharedId });
    }

    private static int _counter;
    private static HybridTestDbContext CreateDb(ITenantProvider provider)
    {
        var n = Interlocked.Increment(ref _counter);
        return new HybridTestDbContext(
            new DbContextOptionsBuilder<HybridTestDbContext>()
                .UseInMemoryDatabase($"HybridFilter_{n}")
                .Options,
            provider);
    }

    private sealed class HybridTestDbContext : ModuleDbContext
    {
        public HybridTestDbContext(DbContextOptions<HybridTestDbContext> options, ITenantProvider tenantProvider)
            : base(options, tenantProvider) { }

        public DbSet<HybridEntity> HybridEntities => Set<HybridEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<HybridEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e.ConfigureTenantOrGlobalProperties();
                e.ConfigureTenantOrGlobalQueryFilter(() => CurrentTenantId);
                e.Property(x => x.Name).HasMaxLength(200);
            });
        }
    }

    private sealed class HybridEntity : IEntity, IHybridTenantEntity
    {
        public Guid Id { get; set; }
        public Guid? TenantId { get; set; }
        public string Name { get; set; } = default!;
    }
}
