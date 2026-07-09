using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Tests.Unit.Data;

[TestClass]
public class GlobalQueryFilterTests
{
    // Production registers ITenantProvider as a singleton. The cached EF model captures the
    // provider instance in the filter expression, so tests must use a shared provider too.
    private static readonly TenantProvider SharedProvider = new();

    [TestInitialize]
    public void ResetTenant()
    {
        SharedProvider.SetTenant(new TenantContext(Guid.NewGuid(), "Test", TenantType.School));
        // Reset the save-guard suppression flag between tests (FR-8).
        TenantContextAccessor.GuardSuppressed.Value = false;
    }

    [TestMethod]
    public async Task TenantQueryFilter_RestrictsResultsToCurrentTenant()
    {
        var tenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var tenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

        SharedProvider.SetTenant(new TenantContext(tenantA, "Test", TenantType.School));
        using var db = CreateDb(SharedProvider);

        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        db.Entities.Add(new TenantFilteredEntity { Id = idA, TenantId = tenantA, Name = "A" });
        db.Entities.Add(new TenantFilteredEntity { Id = idB, TenantId = tenantB, Name = "B" });
        // The save-guard (FR-6) rejects cross-tenant writes. Suppress to insert
        // tenant B's row under tenant A's context — test setup, not a real write.
        using (new TenantContextAccessor(SharedProvider).SuppressTenantGuard())
        {
            await db.SaveChangesAsync();
        }

        var results = await db.Entities.ToListAsync();

        results.Should().ContainSingle().Which.Id.Should().Be(idA);
    }

    [TestMethod]
    public async Task SoftDeleteQueryFilter_HidesDeletedRows()
    {
        using var db = CreateDb(SharedProvider);

        var active = new SoftDeleteEntity { Id = Guid.NewGuid(), Name = "Active" };
        var deleted = new SoftDeleteEntity { Id = Guid.NewGuid(), Name = "Deleted", IsDeleted = true };

        db.SoftDeleteEntities.AddRange(active, deleted);
        await db.SaveChangesAsync();

        var results = await db.SoftDeleteEntities.ToListAsync();

        results.Should().ContainSingle().Which.Id.Should().Be(active.Id);
    }

    [TestMethod]
    public async Task IgnoreQueryFilters_SoftDelete_DisablesOnlySoftDeleteFilter()
    {
        var tenantA = Guid.Parse("33333333-3333-3333-3333-333333333333");
        SharedProvider.SetTenant(new TenantContext(tenantA, "Test", TenantType.School));
        using var db = CreateDb(SharedProvider);

        var active = new TenantSoftDeleteEntity { Id = Guid.NewGuid(), TenantId = tenantA, Name = "Active" };
        var softDeleted = new TenantSoftDeleteEntity { Id = Guid.NewGuid(), TenantId = tenantA, Name = "Deleted", IsDeleted = true };

        db.TenantSoftDeleteEntities.AddRange(active, softDeleted);
        await db.SaveChangesAsync();

        // Bypass only the soft-delete filter; the tenant filter must remain active.
        var results = await db.TenantSoftDeleteEntities
            .IgnoreQueryFilters(["SoftDelete"])
            .ToListAsync();

        results.Should().Contain(x => x.Id == softDeleted.Id);
        results.Should().Contain(x => x.Id == active.Id);
    }

    [TestMethod]
    public async Task IgnoreQueryFilters_NamedFilter_KeepsTenantIsolation()
    {
        var tenantA = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var tenantB = Guid.Parse("66666666-6666-6666-6666-666666666666");

        SharedProvider.SetTenant(new TenantContext(tenantA, "Test", TenantType.School));
        using var db = CreateDb(SharedProvider);

        var softDeletedA = new TenantSoftDeleteEntity { Id = Guid.NewGuid(), TenantId = tenantA, Name = "Deleted A", IsDeleted = true };
        var softDeletedB = new TenantSoftDeleteEntity { Id = Guid.NewGuid(), TenantId = tenantB, Name = "Deleted B", IsDeleted = true };

        db.TenantSoftDeleteEntities.AddRange(softDeletedA, softDeletedB);
        // The save-guard (FR-6) rejects cross-tenant writes. Suppress for test setup.
        using (new TenantContextAccessor(SharedProvider).SuppressTenantGuard())
        {
            await db.SaveChangesAsync();
        }

        var resultsForA = await db.TenantSoftDeleteEntities
            .IgnoreQueryFilters(["SoftDelete"])
            .ToListAsync();

        resultsForA.Should().ContainSingle().Which.Id.Should().Be(softDeletedA.Id);

        SharedProvider.SetTenant(new TenantContext(tenantB, "Test", TenantType.School));

        var resultsForB = await db.TenantSoftDeleteEntities
            .IgnoreQueryFilters(["SoftDelete"])
            .ToListAsync();

        resultsForB.Should().ContainSingle().Which.Id.Should().Be(softDeletedB.Id);
    }

    private static int _dbCounter = 0;

    private static TestFilterDbContext CreateDb(ITenantProvider tenantProvider)
    {
        var number = Interlocked.Increment(ref _dbCounter);
        return new TestFilterDbContext(
            new DbContextOptionsBuilder<TestFilterDbContext>()
                .UseInMemoryDatabase($"FilterTest_{number}")
                .Options,
            tenantProvider);
    }

    private sealed class TestFilterDbContext : ModuleDbContext
    {
        public TestFilterDbContext(DbContextOptions<TestFilterDbContext> options, ITenantProvider tenantProvider)
            : base(options, tenantProvider)
        {
        }

        public DbSet<TenantFilteredEntity> Entities => Set<TenantFilteredEntity>();
        public DbSet<SoftDeleteEntity> SoftDeleteEntities => Set<SoftDeleteEntity>();
        public DbSet<TenantSoftDeleteEntity> TenantSoftDeleteEntities => Set<TenantSoftDeleteEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<TenantFilteredEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.TenantId);
                e.Property(x => x.Name).HasMaxLength(200);
                e.ConfigureTenantQueryFilter(() => CurrentTenantId);
            });

            modelBuilder.Entity<SoftDeleteEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.IsDeleted);
                e.Property(x => x.Name).HasMaxLength(200);
                e.ConfigureSoftDeleteProperties();
                e.ConfigureSoftDeleteQueryFilter();
            });

            modelBuilder.Entity<TenantSoftDeleteEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.TenantId);
                e.Property(x => x.IsDeleted);
                e.Property(x => x.Name).HasMaxLength(200);
                e.ConfigureTenantProperties();
                e.ConfigureTenantQueryFilter(() => CurrentTenantId);
                e.ConfigureSoftDeleteProperties();
                e.ConfigureSoftDeleteQueryFilter();
            });
        }
    }

    private sealed class TenantFilteredEntity : IEntity, ITenantEntity
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = default!;
    }

    private sealed class SoftDeleteEntity : IEntity, ISoftDeletableEntity
    {
        public Guid Id { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset? DeletedAt { get; set; }
        public string Name { get; set; } = default!;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class TenantSoftDeleteEntity : IEntity, ITenantEntity, ISoftDeletableEntity
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset? DeletedAt { get; set; }
        public string Name { get; set; } = default!;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
