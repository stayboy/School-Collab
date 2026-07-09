using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Tests.Unit.Data;

/// <summary>
/// Tests for the <see cref="ModuleDbContext"/> save-guards (FR-5/FR-6/FR-8).
/// Covers AC-2 (empty-tenant throw), AC-3 (mismatch throw), AC-4 (explicit-tenant
/// save), and the hybrid null/Guid.Empty rules. See global-tenant-filter.md §4/§6.1.
/// </summary>
[TestClass]
public class TenantSaveGuardTests
{
    [TestInitialize]
    public void Reset()
    {
        TenantContextAccessor.GuardSuppressed.Value = false;
    }

    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // AC-2: no tenant context → TenantContextRequiredException before any write.
    [TestMethod]
    public async Task SaveChanges_ThrowsTenantContextRequired_WhenNoTenantContext()
    {
        var provider = new TenantProvider();
        provider.Clear(); // no tenant → Guid.Empty

        using var db = CreateDb(provider);
        db.StrictEntities.Add(new StrictEntity { Id = Guid.NewGuid(), Name = "X" });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<TenantContextRequiredException>();
    }

    // AC-3: entity tenant differs from context → TenantMismatchException.
    [TestMethod]
    public async Task SaveChanges_ThrowsTenantMismatch_WhenEntityTenantDiffersFromContext()
    {
        var provider = new TenantProvider();
        provider.SetTenant(new TenantContext(TenantA, "A", TenantType.School));

        using var db = CreateDb(provider);
        db.StrictEntities.Add(new StrictEntity { Id = Guid.NewGuid(), TenantId = TenantB, Name = "B" });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<TenantMismatchException>();
    }

    // AC-4: RunWithExplicitTenant → saves with the explicit tenant; context restored.
    [TestMethod]
    public async Task SaveChanges_Succeeds_WhenRunWithExplicitTenantMatches()
    {
        var provider = new TenantProvider();
        provider.SetTenant(new TenantContext(TenantA, "A", TenantType.School));
        var accessor = new TenantContextAccessor(provider);

        using var db = CreateDb(provider);

        await accessor.RunWithExplicitTenantAsync(TenantB, async ct =>
        {
            db.StrictEntities.Add(new StrictEntity { Id = Guid.NewGuid(), TenantId = TenantB, Name = "B" });
            await db.SaveChangesAsync();
            return true;
        }, default);

        provider.GetTenantContext().TenantId.Should().Be(TenantA, "context restored after the scope");
    }

    // Suppression allows a cross-tenant write (admin / migration / seed).
    [TestMethod]
    public async Task SaveChanges_Succeeds_WhenGuardSuppressed_AndTenantMismatch()
    {
        var provider = new TenantProvider();
        provider.SetTenant(new TenantContext(TenantA, "A", TenantType.School));
        var accessor = new TenantContextAccessor(provider);

        using var db = CreateDb(provider);
        db.StrictEntities.Add(new StrictEntity { Id = Guid.NewGuid(), TenantId = TenantB, Name = "B" });

        using (accessor.SuppressTenantGuard())
        {
            await db.SaveChangesAsync();
        }
    }

    // FR-8: hybrid entity with null TenantId (shared blueprint) is allowed.
    [TestMethod]
    public async Task SaveChanges_Succeeds_WhenHybridEntityHasNullTenant()
    {
        var provider = new TenantProvider();
        provider.SetTenant(new TenantContext(TenantA, "A", TenantType.School));

        using var db = CreateDb(provider);
        db.HybridEntities.Add(new HybridEntity { Id = Guid.NewGuid(), TenantId = null, Name = "Blueprint" });

        await db.SaveChangesAsync(); // null is the blueprint sentinel — no throw
    }

    // FR-8: hybrid entity with Guid.Empty is never valid.
    [TestMethod]
    public async Task SaveChanges_ThrowsTenantContextRequired_WhenHybridEntityHasGuidEmpty()
    {
        var provider = new TenantProvider();
        provider.SetTenant(new TenantContext(TenantA, "A", TenantType.School));

        using var db = CreateDb(provider);
        db.HybridEntities.Add(new HybridEntity { Id = Guid.NewGuid(), TenantId = Guid.Empty, Name = "Bad" });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<TenantContextRequiredException>();
    }

    // FR-8: hybrid entity whose tenant matches the context is allowed.
    [TestMethod]
    public async Task SaveChanges_Succeeds_WhenHybridEntityTenantMatchesContext()
    {
        var provider = new TenantProvider();
        provider.SetTenant(new TenantContext(TenantA, "A", TenantType.School));

        using var db = CreateDb(provider);
        db.HybridEntities.Add(new HybridEntity { Id = Guid.NewGuid(), TenantId = TenantA, Name = "Owned" });

        await db.SaveChangesAsync();
    }

    // FR-8: hybrid entity whose tenant differs from the context → mismatch.
    [TestMethod]
    public async Task SaveChanges_ThrowsTenantMismatch_WhenHybridEntityTenantDiffersFromContext()
    {
        var provider = new TenantProvider();
        provider.SetTenant(new TenantContext(TenantA, "A", TenantType.School));

        using var db = CreateDb(provider);
        db.HybridEntities.Add(new HybridEntity { Id = Guid.NewGuid(), TenantId = TenantB, Name = "B-owned" });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<TenantMismatchException>();
    }

    private static int _counter;
    private static GuardTestDbContext CreateDb(ITenantProvider provider)
    {
        var n = Interlocked.Increment(ref _counter);
        return new GuardTestDbContext(
            new DbContextOptionsBuilder<GuardTestDbContext>()
                .UseInMemoryDatabase($"GuardTest_{n}")
                .Options,
            provider);
    }

    private sealed class GuardTestDbContext : ModuleDbContext
    {
        public GuardTestDbContext(DbContextOptions<GuardTestDbContext> options, ITenantProvider tenantProvider)
            : base(options, tenantProvider) { }

        public DbSet<StrictEntity> StrictEntities => Set<StrictEntity>();
        public DbSet<HybridEntity> HybridEntities => Set<HybridEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<StrictEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.TenantId);
                e.Property(x => x.Name).HasMaxLength(200);
                e.ConfigureTenantQueryFilter(() => CurrentTenantId);
            });

            modelBuilder.Entity<HybridEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e.ConfigureTenantOrGlobalProperties();
                e.ConfigureTenantOrGlobalQueryFilter(() => CurrentTenantId);
                e.Property(x => x.Name).HasMaxLength(200);
            });
        }
    }

    private sealed class StrictEntity : IEntity, ITenantEntity
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = default!;
    }

    private sealed class HybridEntity : IEntity, IHybridTenantEntity
    {
        public Guid Id { get; set; }
        public Guid? TenantId { get; set; }
        public string Name { get; set; } = default!;
    }
}
