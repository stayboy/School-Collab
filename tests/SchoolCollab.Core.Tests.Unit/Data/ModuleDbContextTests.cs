using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Tests.Unit.Data;

[TestClass]
public class ModuleDbContextTests
{
    [TestMethod]
    public void CurrentTenantId_ReturnsProviderValue()
    {
        var tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var provider = CreateProvider(tenantId);

        using var context = new TestModuleDbContext(
            new DbContextOptionsBuilder<TestModuleDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            provider);

        context.CurrentTenantId.Should().Be(tenantId);
    }

    [TestMethod]
    public async Task SaveChangesAsync_StampsTenantIdAndAuditTimestamps()
    {
        var tenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var provider = CreateProvider(tenantId);

        using var context = new TestModuleDbContext(
            new DbContextOptionsBuilder<TestModuleDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            provider);

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var entity = new TenantAuditableEntity { Id = Guid.NewGuid(), Name = "Test" };

        context.Entities.Add(entity);
        await context.SaveChangesAsync();

        entity.TenantId.Should().Be(tenantId);
        entity.CreatedAt.Should().BeOnOrAfter(before);
        entity.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [TestMethod]
    public async Task SaveChangesAsync_DoesNotOverwriteCreatedAtOnUpdate()
    {
        var provider = CreateProvider(Guid.NewGuid());

        using var context = new TestModuleDbContext(
            new DbContextOptionsBuilder<TestModuleDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            provider);

        var entity = new TenantAuditableEntity { Id = Guid.NewGuid(), Name = "Original" };
        context.Entities.Add(entity);
        await context.SaveChangesAsync();

        var createdAt = entity.CreatedAt;
        await Task.Delay(20);

        entity.Name = "Updated";
        await context.SaveChangesAsync();

        entity.CreatedAt.Should().Be(createdAt);
        entity.UpdatedAt.Should().BeAfter(createdAt);
    }

    [TestMethod]
    public async Task SaveChangesAsync_RespectsExplicitTenantId()
    {
        var currentTenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var explicitTenantId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var provider = CreateProvider(currentTenantId);

        using var context = new TestModuleDbContext(
            new DbContextOptionsBuilder<TestModuleDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            provider);

        var entity = new TenantAuditableEntity
        {
            Id = Guid.NewGuid(),
            TenantId = explicitTenantId,
            Name = "Explicit"
        };

        context.Entities.Add(entity);
        // The save-guard (FR-6) now rejects cross-tenant writes. Suppress to verify
        // the explicit tenant is still respected (not overwritten by the auto-default).
        using (new TenantContextAccessor(provider).SuppressTenantGuard())
        {
            await context.SaveChangesAsync();
        }

        entity.TenantId.Should().Be(explicitTenantId);
    }

    private static TenantProvider CreateProvider(Guid tenantId)
    {
        var provider = new TenantProvider();
        provider.SetTenant(new TenantContext(tenantId, "Test", TenantType.School));
        return provider;
    }

    private sealed class TestModuleDbContext : ModuleDbContext
    {
        public TestModuleDbContext(DbContextOptions<TestModuleDbContext> options, ITenantProvider tenantProvider)
            : base(options, tenantProvider)
        {
        }

        public DbSet<TenantAuditableEntity> Entities => Set<TenantAuditableEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<TenantAuditableEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.TenantId);
                e.Property(x => x.CreatedAt);
                e.Property(x => x.UpdatedAt);
                e.Property(x => x.Name).HasMaxLength(200);
            });
        }
    }

    private sealed class TenantAuditableEntity : IEntity, ITenantEntity, IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string Name { get; set; } = default!;
    }
}
