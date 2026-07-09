using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Tests.Unit.Data;

/// <summary>
/// Tests for the build-time model audit (<see cref="ModuleDbContext.ValidateTenantFilters"/>).
/// Covers AC-17: a non-allow-listed entity lacking a "Tenant" filter throws
/// <see cref="TenantFilterMissingException"/>. See global-tenant-filter.md FR-14.
/// </summary>
[TestClass]
public class TenantFilterAuditTests
{
    [TestInitialize]
    public void Reset()
    {
        TenantContextAccessor.GuardSuppressed.Value = false;
    }

    // AC-17: audit throws when a non-allow-listed entity lacks a "Tenant" filter.
    [TestMethod]
    public void ValidateTenantFilters_Throws_WhenEntityMissingTenantFilter()
    {
        var provider = new TenantProvider();
        provider.SetTenant(new TenantContext(Guid.NewGuid(), "Test", TenantType.School));

        var act = () =>
        {
            using var db = new AuditFailDbContext(
                new DbContextOptionsBuilder<AuditFailDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options,
                provider);
            // Trigger OnModelCreating (lazy) — the audit throws here.
            _ = db.UnfilteredEntities.FirstOrDefault();
        };

        act.Should().Throw<TenantFilterMissingException>()
            .Which.EntityType.Should().Be(typeof(AuditFailDbContext.UnfilteredEntity));
    }

    // Audit succeeds when every entity has a "Tenant" filter.
    [TestMethod]
    public void ValidateTenantFilters_Succeeds_WhenAllEntitiesHaveFilter()
    {
        var provider = new TenantProvider();
        provider.SetTenant(new TenantContext(Guid.NewGuid(), "Test", TenantType.School));

        var act = () =>
        {
            using var db = new AllFilteredDbContext(
                new DbContextOptionsBuilder<AllFilteredDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options,
                provider);
            _ = db.FilteredEntities.FirstOrDefault();
        };

        act.Should().NotThrow();
    }

    // Audit succeeds when the unfiltered entity is on the context's allow-list.
    [TestMethod]
    public void ValidateTenantFilters_Succeeds_WhenEntityOnAllowList()
    {
        var provider = new TenantProvider();
        provider.SetTenant(new TenantContext(Guid.NewGuid(), "Test", TenantType.School));

        var act = () =>
        {
            using var db = new AllowListDbContext(
                new DbContextOptionsBuilder<AllowListDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options,
                provider);
            _ = db.GlobalEntities.FirstOrDefault();
        };

        act.Should().NotThrow();
    }

    // Audit skips owned types — they inherit the owner's filter via Include.
    [TestMethod]
    public void ValidateTenantFilters_SkipsOwnedTypes()
    {
        var provider = new TenantProvider();
        provider.SetTenant(new TenantContext(Guid.NewGuid(), "Test", TenantType.School));

        var act = () =>
        {
            using var db = new OwnedTypeDbContext(
                new DbContextOptionsBuilder<OwnedTypeDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options,
                provider);
            _ = db.Owners.FirstOrDefault();
        };

        act.Should().NotThrow();
    }

    // ── Test contexts ──

    private sealed class AuditFailDbContext : ModuleDbContext
    {
        public AuditFailDbContext(DbContextOptions<AuditFailDbContext> options, ITenantProvider provider)
            : base(options, provider) { }

        public DbSet<UnfilteredEntity> UnfilteredEntities => Set<UnfilteredEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<UnfilteredEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).HasMaxLength(200);
                // Intentionally no "Tenant" filter — the audit must catch this.
            });
            ValidateTenantFilters(modelBuilder);
        }

        internal sealed class UnfilteredEntity : IEntity
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = default!;
        }
    }

    private sealed class AllFilteredDbContext : ModuleDbContext
    {
        public AllFilteredDbContext(DbContextOptions<AllFilteredDbContext> options, ITenantProvider provider)
            : base(options, provider) { }

        public DbSet<FilteredEntity> FilteredEntities => Set<FilteredEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<FilteredEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.TenantId);
                e.Property(x => x.Name).HasMaxLength(200);
                e.ConfigureTenantQueryFilter(() => CurrentTenantId);
            });
            ValidateTenantFilters(modelBuilder);
        }
    }

    private sealed class AllowListDbContext : ModuleDbContext
    {
        public AllowListDbContext(DbContextOptions<AllowListDbContext> options, ITenantProvider provider)
            : base(options, provider) { }

        public DbSet<GlobalEntity> GlobalEntities => Set<GlobalEntity>();

        protected override Type[] GlobalEntityAllowList => [typeof(GlobalEntity)];

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<GlobalEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).HasMaxLength(200);
                // No "Tenant" filter — but on the allow-list, so the audit passes.
            });
            ValidateTenantFilters(modelBuilder);
        }
    }

    private sealed class OwnedTypeDbContext : ModuleDbContext
    {
        public OwnedTypeDbContext(DbContextOptions<OwnedTypeDbContext> options, ITenantProvider provider)
            : base(options, provider) { }

        public DbSet<OwnerEntity> Owners => Set<OwnerEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<OwnerEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.TenantId);
                e.Property(x => x.Name).HasMaxLength(200);
                e.ConfigureTenantQueryFilter(() => CurrentTenantId);
                e.OwnsOne(x => x.Detail, d => d.Property(p => p.Value).HasMaxLength(200));
            });
            ValidateTenantFilters(modelBuilder);
        }
    }

    // ── Shared test entities ──

    private sealed class FilteredEntity : IEntity, ITenantEntity
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = default!;
    }

    private sealed class GlobalEntity : IEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
    }

    private sealed class OwnerEntity : IEntity, ITenantEntity
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = default!;
        public OwnedDetail Detail { get; set; } = new();
    }

    private sealed class OwnedDetail
    {
        public string Value { get; set; } = default!;
    }
}
