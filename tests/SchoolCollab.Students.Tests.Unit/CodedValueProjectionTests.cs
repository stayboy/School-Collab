using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Contracts.Events;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Projections;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// adr-cross-module-calls.md Phase 1: local coded-value read model.
/// Covers the repository's effective-resolution rules and every projection
/// consumer rule recorded in the ADR's Phase 0 section.
/// </summary>
[TestClass]
public class CodedValueProjectionTests
{
    // ── Repository resolution (pure function) ──

    private static LocalCodedValue Row(Guid id, Guid? tenantId, string code = "GRADE7", string name = "Year 7",
        string? description = null, bool isDeleted = false, int displayOrder = 7,
        Guid? parentId = null, string? parentCode = "GRADE") => new()
    {
        Id = id, TenantId = tenantId, Code = code, Name = name, Description = description,
        IsDeleted = isDeleted, DisplayOrder = displayOrder, ParentId = parentId, ParentCode = parentCode,
        Attributes = [new LocalCodedValueAttribute("gradeLevel", "abc")],
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    [TestMethod]
    public void Resolve_GlobalRowOnly_ReturnsGlobalValues()
    {
        var id = Guid.NewGuid();
        var dto = LocalCodedValueRepository.Resolve([Row(id, null, name: "Global Name")]);

        dto.Should().NotBeNull();
        dto!.Name.Should().Be("Global Name");
        dto.DisplayOrder.Should().Be(7);
        dto.Attributes.Should().ContainSingle(a => a.Key == "gradeLevel");
    }

    [TestMethod]
    public void Resolve_GlobalPlusOverride_NonNullOverlayWins_NullKeepsGlobal()
    {
        var id = Guid.NewGuid();
        var global = Row(id, null, name: "Global Name", description: "Global desc", displayOrder: 3);
        var overlay = Row(id, Guid.NewGuid(), name: "Tenant Name");
        overlay.Description = null; // "keep global"

        var dto = LocalCodedValueRepository.Resolve([global, overlay]);

        dto!.Name.Should().Be("Tenant Name");      // overridden
        dto.Description.Should().Be("Global desc"); // null overlay keeps global
        dto.DisplayOrder.Should().Be(3);            // order always comes from global source
    }

    [TestMethod]
    public void Resolve_TenantOwnedStandalone_NoGlobalRow_UsesTenantRow()
    {
        var id = Guid.NewGuid();
        var dto = LocalCodedValueRepository.Resolve([Row(id, Guid.NewGuid(), name: "Owned")]);

        dto!.Name.Should().Be("Owned");
    }

    [TestMethod]
    public void Resolve_DeletedRows_ReturnNull()
    {
        var id = Guid.NewGuid();
        LocalCodedValueRepository.Resolve([Row(id, null, isDeleted: true)]).Should().BeNull();
        LocalCodedValueRepository.Resolve([]).Should().BeNull();
    }

    // ── Projection consumer rules ──

    private sealed class TestDbFactory(IServiceProvider sp) : IDbContextFactory<StudentsDbContext>
    {
        public StudentsDbContext CreateDbContext() => sp.CreateScope().ServiceProvider.GetRequiredService<StudentsDbContext>();
        public Task<StudentsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }

    private sealed class Harness(string dbName)
    {
        public IServiceProvider Provider { get; } = Build(dbName);
        public IDbContextFactory<StudentsDbContext> Factory { get; private set; } = null!;

        public Harness() : this("cv-proj-default") { }

        private static IServiceProvider Build(string name)
        {
            var services = new ServiceCollection();
            services.AddTenancy();
            services.AddDbContext<StudentsDbContext>(o => o.UseInMemoryDatabase(name));
            services.AddDistributedMemoryCache();
            services.AddHybridCache();
            return services.BuildServiceProvider();
        }

        public Harness Init()
        {
            Factory = new TestDbFactory(Provider);
            ((TenantProvider)Provider.GetRequiredService<ITenantProvider>()).SetTenant(
                new TenantContext(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "T", TenantType.School));
            return this;
        }

        public async Task<LocalCodedValue?> FindAsync(Guid id)
        {
            await using var db = await Factory.CreateDbContextAsync();
            return await db.LocalCodedValues.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.TenantId == null);
        }
    }

    [TestMethod]
    public async Task CreatedEvent_UpsertsGlobalRow_AndRepositoryResolvesIt()
    {
        var h = new Harness("cv-proj-created").Init();
        var id = Guid.NewGuid();

        await new CodedValueCreatedProjectionHandler(h.Factory, h.Provider.GetRequiredService<HybridCache>())
            .HandleAsync(new CodedValueCreated(id, "GRADE7", "Year 7", null, null, 7, DateTimeOffset.UtcNow,
                ParentCode: "GRADE", IsDisabled: false, Attributes: null, TenantId: null));

        var row = await h.FindAsync(id);
        row.Should().NotBeNull();
        row!.Name.Should().Be("Year 7");
    }

    [TestMethod]
    public async Task ApprovalUpdated_DropsStaleTenantRow()
    {
        var h = new Harness("cv-proj-approved").Init();
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        // Provisional tenant-owned row first.
        await new CodedValueCreatedProjectionHandler(h.Factory, h.Provider.GetRequiredService<HybridCache>())
            .HandleAsync(new CodedValueCreated(id, "GRADE8", "Y8", null, null, 8, DateTimeOffset.UtcNow,
                ParentCode: "GRADE", IsDisabled: false, Attributes: null, TenantId: tenantId));

        // Approval arrives as Updated with TenantId=null.
        await new CodedValueUpdatedProjectionHandler(h.Factory, h.Provider.GetRequiredService<HybridCache>())
            .HandleAsync(new CodedValueUpdated(id, "GRADE8", "Y8", null, DateTimeOffset.UtcNow,
                ParentId: null, ParentCode: "GRADE", DisplayOrder: 8, IsDisabled: false,
                Attributes: null, TenantId: null));

        await using var db = await h.Factory.CreateDbContextAsync();
        (await db.LocalCodedValues.CountAsync(x => x.Id == id)).Should().Be(1);
        (await db.LocalCodedValues.SingleAsync(x => x.Id == id)).TenantId.Should().BeNull();
    }

    [TestMethod]
    public async Task DeletedEvent_RemovesAllRowsForId()
    {
        var h = new Harness("cv-proj-deleted").Init();
        var id = Guid.NewGuid();

        await new CodedValueCreatedProjectionHandler(h.Factory, h.Provider.GetRequiredService<HybridCache>())
            .HandleAsync(new CodedValueCreated(id, "X", "X", null, null, 0, DateTimeOffset.UtcNow,
                ParentCode: null, IsDisabled: false, Attributes: null, TenantId: null));

        await new CodedValueDeletedProjectionHandler(h.Factory, h.Provider.GetRequiredService<HybridCache>())
            .HandleAsync(new CodedValueDeleted(id, "X", DateTimeOffset.UtcNow));

        (await h.FindAsync(id)).Should().BeNull();
    }

    [TestMethod]
    public async Task OverrideUpsertThenRemove_MergesAndFallsBack()
    {
        var h = new Harness("cv-proj-override").Init();
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var cache = h.Provider.GetRequiredService<HybridCache>();

        await new CodedValueCreatedProjectionHandler(h.Factory, cache)
            .HandleAsync(new CodedValueCreated(id, "GRADE9", "Y9", "desc", null, 9, DateTimeOffset.UtcNow,
                ParentCode: "GRADE", IsDisabled: false, Attributes: null, TenantId: null));
        await new CodedValueOverrideUpsertedProjectionHandler(h.Factory, cache)
            .HandleAsync(new CodedValueOverrideUpserted(tenantId, id, "Tenant Y9", null, null, DateTimeOffset.UtcNow));

        await using var db = await h.Factory.CreateDbContextAsync();
        var overlay = await db.LocalCodedValues.SingleAsync(x => x.Id == id && x.TenantId == tenantId);
        overlay.Name.Should().Be("Tenant Y9");
        overlay.Description.Should().BeNull(); // keeps global at read time

        await new CodedValueOverrideRemovedProjectionHandler(h.Factory, cache)
            .HandleAsync(new CodedValueOverrideRemoved(tenantId, id, DateTimeOffset.UtcNow));
        (await db.LocalCodedValues.CountAsync(x => x.TenantId == tenantId)).Should().Be(0);
    }

    [TestMethod]
    public async Task OverrideBeforeGlobalCreated_KeepsGlobalCode_AfterCreatedArrives()
    {
        var h = new Harness("cv-proj-override-first").Init();
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var cache = h.Provider.GetRequiredService<HybridCache>();

        // Override arrives first, overriding ONLY Name (Code/Description null).
        await new CodedValueOverrideUpsertedProjectionHandler(h.Factory, cache)
            .HandleAsync(new CodedValueOverrideUpserted(tenantId, id, "Tenant Name", null, null, DateTimeOffset.UtcNow));

        // Global Created arrives after with full state.
        await new CodedValueCreatedProjectionHandler(h.Factory, cache)
            .HandleAsync(new CodedValueCreated(id, "GRADE7", "Year 7", "desc", null, 7, DateTimeOffset.UtcNow,
                ParentCode: "GRADE", IsDisabled: false, Attributes: null, TenantId: null));

        // Resolve under the overriding tenant: Name overridden, Code from global.
        ((TenantProvider)h.Provider.GetRequiredService<ITenantProvider>()).SetTenant(
            new TenantContext(tenantId, "T", TenantType.School));
        var repo = new LocalCodedValueRepository(h.Factory, h.Provider.GetRequiredService<ITenantProvider>(), cache);
        var dto = await repo.GetByIdAsync(id);

        dto.Should().NotBeNull();
        dto!.Name.Should().Be("Tenant Name");
        dto.Code.Should().Be("GRADE7"); // NOT empty string from the placeholder
        dto.Description.Should().Be("desc");
    }
}
