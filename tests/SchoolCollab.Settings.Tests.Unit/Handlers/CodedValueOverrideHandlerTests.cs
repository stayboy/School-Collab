using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RemoveCodedValueOverride;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.UpsertCodedValueOverride;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValueById;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValuesByParent;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Tests.Unit.Handlers;

[TestClass]
public class CodedValueOverrideHandlerTests
{
    /// <summary>
    /// Mutable tenant provider so individual tests can opt into either the
    /// "default" tenant (no real tenant, overrides rewrite the global blueprint)
    /// or a real tenant (overrides create per-tenant rows).
    /// </summary>
    private sealed class MutableTenantProvider : ITenantProvider
    {
        public TenantContext Current { get; set; } = new(Guid.Empty, "System", TenantType.Organization);
        public TenantContext GetTenantContext() => Current;
    }

    private sealed class Scope : IDisposable
    {
        public SettingsDbContext Db { get; }
        public MutableTenantProvider Tenants { get; } = new();
        public GetCodedValueByIdHandler Resolver { get; }
        public IDbContextFactory<SettingsDbContext> Factory { get; }
        public GetCodedValuesByParentHandler ByParent { get; }
        public UpsertCodedValueOverrideHandler Upsert { get; }
        public RemoveCodedValueOverrideHandler Remove { get; }
        public HybridCache Cache { get; }

        public Scope(string dbName)
        {
            var services = BuildServices(dbName, Tenants);
            var provider = services.BuildServiceProvider();

            Db = provider.GetRequiredService<SettingsDbContext>();
            Cache = provider.GetRequiredService<HybridCache>();

            // The handlers only use SuppressTenantGuard() (a static AsyncLocal flag),
            // so a TenantContextAccessor backed by a throwaway TenantProvider is fine —
            // the flag is shared across all accessor instances.
            var accessor = new TenantContextAccessor(new TenantProvider());
            Resolver = new GetCodedValueByIdHandler(Db);
            // GetCodedValuesByParentHandler creates short-lived contexts via
            // IDbContextFactory (HybridCache body may outlive the request scope);
            // the factory shares the SAME InMemory database name as Db, so seeds
            // written through Db are visible to handler-created contexts.
            Factory = provider.GetRequiredService<IDbContextFactory<SettingsDbContext>>();
            ByParent = new GetCodedValuesByParentHandler(Factory, Cache, Tenants);
            Upsert = new UpsertCodedValueOverrideHandler(Db, Tenants, accessor, Resolver, Cache,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<UpsertCodedValueOverrideHandler>.Instance);
            Remove = new RemoveCodedValueOverrideHandler(Db, Tenants, accessor, Cache);
        }

        public void Dispose() => Db.Dispose();

        private static IServiceCollection BuildServices(string name, MutableTenantProvider tenants)
        {
            var services = new ServiceCollection();
            // Register the test's MutableTenantProvider as the ITenantProvider so
            // the SettingsDbContext (and its CurrentTenantId property) sees the
            // same tenant as the handler constructors. Without this the DbContext
            // would use the default TenantProvider (Guid.Empty) and the
            // GetCodedValueById handler would read the global name instead of
            // the per-tenant override.
            services.AddSingleton<ITenantProvider>(tenants);
            // AddDbContextFactory ALSO registers SettingsDbContext as a scoped
            // service, so `provider.GetRequiredService<SettingsDbContext>()`
            // (used for seeding) keeps working while handlers get the factory.
            services.AddDbContextFactory<SettingsDbContext>(o => o.UseInMemoryDatabase(name));
            services.AddScoped<ICodedValueRepository, CodedValueRepository>();
            // In-memory HybridCache so cache-invalidation calls in the handlers
            // do not fail in unit tests.
            services.AddDistributedMemoryCache();
            services.AddHybridCache();
            return services;
        }
    }

    private static async Task<Guid> SeedCodedValueAsync(SettingsDbContext db, string code, string name, int displayOrder = 0)
    {
        var cv = CodedValue.Create(code, name, null, null, displayOrder);
        db.CodedValues.Add(cv);
        await db.SaveChangesAsync();
        return cv.Id;
    }

    // ── Real-tenant branch: per-tenant override rows ─────────────────────────

    [TestMethod]
    public async Task Upsert_ForRealTenant_CreatesOverrideRow()
    {
        using var s = new Scope("override-create");
        s.Tenants.Current = new TenantContext(Guid.NewGuid(), "Hydeson", TenantType.School);
        var id = await SeedCodedValueAsync(s.Db, "GRADE_1", "Grade 1");

        var dto = await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, "Standard 1", null));

        dto.Name.Should().Be("Standard 1");
        s.Db.TenantCodedValueOverrides.Should().ContainSingle(o => o.GlobalCodedValueId == id);
    }

    [TestMethod]
    public async Task Upsert_ForRealTenant_UpdatesExistingOverride()
    {
        using var s = new Scope("override-update");
        s.Tenants.Current = new TenantContext(Guid.NewGuid(), "Hydeson", TenantType.School);
        var id = await SeedCodedValueAsync(s.Db, "GRADE_2", "Grade 2");

        await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, "First", null));
        var dto = await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, "Second", null));

        dto.Name.Should().Be("Second");
        s.Db.TenantCodedValueOverrides.Should().ContainSingle(o => o.GlobalCodedValueId == id);
    }

    [TestMethod]
    public async Task Upsert_InvalidatesCodedValuesCache()
    {
        // Warm the by-parent cache, apply an override, then re-query by parent.
        // If cache invalidation is missing the second query will return the stale
        // blueprint name from the cached entry.
        using var s = new Scope("override-cache-invalidate");
        s.Tenants.Current = new TenantContext(Guid.NewGuid(), "Hydeson", TenantType.School);
        var id = await SeedCodedValueAsync(s.Db, "GRADE_2_5", "Grade 2.5");

        var before = await s.ByParent.HandleAsync(new GetCodedValuesByParent(null, "", null, false));
        before.Should().ContainSingle(x => x.Id == id)
            .Which.Name.Should().Be("Grade 2.5");

        await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, "Standard 2.5", null));

        var after = await s.ByParent.HandleAsync(new GetCodedValuesByParent(null, "", null, false));
        after.Should().ContainSingle(x => x.Id == id)
            .Which.Name.Should().Be("Standard 2.5");
    }

    [TestMethod]
    public async Task Remove_ForRealTenant_FallsBackToBlueprintName()
    {
        using var s = new Scope("override-remove");
        s.Tenants.Current = new TenantContext(Guid.NewGuid(), "Hydeson", TenantType.School);
        var id = await SeedCodedValueAsync(s.Db, "GRADE_3", "Grade 3");

        await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, "Standard 3", null));
        await s.Remove.HandleAsync(new RemoveCodedValueOverride(id));

        s.Db.TenantCodedValueOverrides.Should().BeEmpty();

        var resolved = await s.Resolver.HandleAsync(new GetCodedValueById(id));
        resolved.Should().NotBeNull();
        resolved!.Name.Should().Be("Grade 3"); // back to the global blueprint name
    }

    [TestMethod]
    public async Task Remove_ForRealTenant_WhenNoOverride_IsIdempotent()
    {
        using var s = new Scope("override-remove-nop");
        s.Tenants.Current = new TenantContext(Guid.NewGuid(), "Hydeson", TenantType.School);
        var id = await SeedCodedValueAsync(s.Db, "GRADE_4", "Grade 4");

        // Removing a non-existent override must not throw.
        var act = async () => await s.Remove.HandleAsync(new RemoveCodedValueOverride(id));
        await act.Should().NotThrowAsync();
    }

    // ── Default-tenant branch: dedicated override row keyed by Guid.Empty ─────

    [TestMethod]
    public async Task Upsert_ForDefaultTenant_CreatesOverrideRowWithEmptyTenantId()
    {
        // The default tenant gets its own override row keyed by Guid.Empty.
        // The global CodedValue blueprint is never rewritten, so real tenants
        // can never see this override.
        using var s = new Scope("override-default-create");
        var id = await SeedCodedValueAsync(s.Db, "GRADE_5", "Grade 5", displayOrder: 7);

        var dto = await s.Upsert.HandleAsync(
            new UpsertCodedValueOverride(id, "Standard 5", "Renamed for dev"));

        dto.Name.Should().Be("Standard 5");
        dto.Description.Should().Be("Renamed for dev");
        s.Db.TenantCodedValueOverrides.Should().ContainSingle(o =>
            o.GlobalCodedValueId == id && o.TenantId == Guid.Empty);
        var stored = await s.Db.CodedValues.SingleAsync(x => x.Id == id);
        stored.Name.Should().Be("Grade 5", "global blueprint must not be rewritten");
        stored.DisplayOrder.Should().Be(7);
    }

    [TestMethod]
    public async Task Remove_ForDefaultTenant_RemovesDefaultTenantOverride()
    {
        // The default tenant's override is a real row keyed by Guid.Empty, so
        // remove targets it and leaves the global blueprint untouched.
        using var s = new Scope("override-default-remove");
        var id = await SeedCodedValueAsync(s.Db, "GRADE_6", "Grade 6");

        await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, "Standard 6", null));
        await s.Remove.HandleAsync(new RemoveCodedValueOverride(id));

        s.Db.TenantCodedValueOverrides.Should().BeEmpty();
        var stored = await s.Db.CodedValues.SingleAsync(x => x.Id == id);
        stored.Name.Should().Be("Grade 6", "global blueprint must remain untouched");
    }

    [TestMethod]
    public async Task Upsert_ForDefaultTenant_UpdatesExistingDefaultOverride()
    {
        // A second override call updates the same Guid.Empty row (not a new row).
        using var s = new Scope("override-default-repeat");
        var id = await SeedCodedValueAsync(s.Db, "GRADE_7", "Grade 7");

        await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, "First", null));
        await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, "Second", null));

        s.Db.TenantCodedValueOverrides.Should().ContainSingle(o =>
            o.GlobalCodedValueId == id && o.TenantId == Guid.Empty);
        var row = s.Db.TenantCodedValueOverrides.Single();
        row.OverriddenName.Should().Be("Second");
    }

    [TestMethod]
    public async Task DefaultAndRealTenant_OverridesAreIsolated()
    {
        // The default tenant's override (Guid.Empty) must never be visible to
        // a real tenant, and vice versa. This is the core tenancy-isolation
        // guarantee the per-tenant row model provides.
        using var s = new Scope("override-isolation");
        var realTenantId = Guid.NewGuid();
        var id = await SeedCodedValueAsync(s.Db, "GRADE_8", "Grade 8");

        // Default tenant sets an override.
        await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, "Default-Name", null));

        // Real tenant sets its own override.
        s.Tenants.Current = new TenantContext(realTenantId, "Hydeson", TenantType.School);
        await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, "Real-Name", null));

        // Two separate rows, one per tenant. Read with the "Tenant" filter bypassed
        // so both rows are visible for this raw-storage assertion (the filter scopes
        // enumeration to the current real tenant, which would hide the Guid.Empty row).
        var allOverrides = await s.Db.TenantCodedValueOverrides.IgnoreQueryFilters(["Tenant"]).ToListAsync();
        allOverrides.Should().HaveCount(2);
        allOverrides.Should().Contain(o =>
            o.TenantId == Guid.Empty && o.OverriddenName == "Default-Name");
        allOverrides.Should().Contain(o =>
            o.TenantId == realTenantId && o.OverriddenName == "Real-Name");

        // The real tenant resolves its own name; the default-tenant override is
        // invisible because the resolver filters by the current tenant id.
        var resolved = await s.Resolver.HandleAsync(new GetCodedValueById(id));
        resolved!.Name.Should().Be("Real-Name");
    }

    [TestMethod]
    public async Task Upsert_ForRealTenant_OverridesCode()
    {
        using var s = new Scope("override-code-create");
        s.Tenants.Current = new TenantContext(Guid.NewGuid(), "Hydeson", TenantType.School);
        var id = await SeedCodedValueAsync(s.Db, "GRADE_A", "Grade A");

        var dto = await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, null, null, "GRADE_A1"));

        dto.Code.Should().Be("GRADE_A1", "the resolved code reflects the tenant override");
        dto.DefaultCode.Should().Be("GRADE_A", "the blueprint code is preserved for the UI");
        var row = s.Db.TenantCodedValueOverrides.Single(o => o.GlobalCodedValueId == id);
        row.OverriddenCode.Should().Be("GRADE_A1");
    }

    [TestMethod]
    public async Task Upsert_ForRealTenant_UpdatesExistingCodeOverride()
    {
        using var s = new Scope("override-code-update");
        s.Tenants.Current = new TenantContext(Guid.NewGuid(), "Hydeson", TenantType.School);
        var id = await SeedCodedValueAsync(s.Db, "GRADE_B", "Grade B");

        await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, null, null, "GRADE_B1"));
        var dto = await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, null, null, "GRADE_B2"));

        dto.Code.Should().Be("GRADE_B2");
        s.Db.TenantCodedValueOverrides.Should().ContainSingle(o => o.GlobalCodedValueId == id);
    }

    [TestMethod]
    public async Task Upsert_RejectsOverridingCodeAndDescriptionTogether()
    {
        // A tenant may override Name, Description, or Code — but not both Code
        // AND Description at once (that is a new tenant-scoped coded value, tcv/3).
        using var s = new Scope("override-code-desc-reject");
        s.Tenants.Current = new TenantContext(Guid.NewGuid(), "Hydeson", TenantType.School);
        var id = await SeedCodedValueAsync(s.Db, "GRADE_C", "Grade C");

        var act = async () => await s.Upsert.HandleAsync(
            new UpsertCodedValueOverride(id, null, "Renamed", "GRADE_C1"));

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Cannot override both Code and Description*");
        s.Db.TenantCodedValueOverrides.Should().BeEmpty("no partial override may be persisted");
    }

    // ── Common error path ────────────────────────────────────────────────────

    [TestMethod]
    public async Task Upsert_ForMissingCodedValue_ThrowsNotFound()
    {
        using var s = new Scope("override-missing");
        var act = async () => await s.Upsert.HandleAsync(
            new UpsertCodedValueOverride(Guid.NewGuid(), "X", null));
        await act.Should().ThrowAsync<CodedValueNotFoundException>();
    }
}
