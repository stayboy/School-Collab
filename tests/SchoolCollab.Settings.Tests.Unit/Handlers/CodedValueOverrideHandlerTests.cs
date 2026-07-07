using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RemoveCodedValueOverride;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.UpsertCodedValueOverride;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValueById;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Settings.Core.DTOs;

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
        public UpsertCodedValueOverrideHandler Upsert { get; }
        public RemoveCodedValueOverrideHandler Remove { get; }

        public Scope(string dbName)
        {
            Db = BuildDb(dbName, Tenants);
            // No HybridCache in scope — GetCodedValueById reads directly from the DB.
            Resolver = new GetCodedValueByIdHandler(Db);
            Upsert = new UpsertCodedValueOverrideHandler(Db, Tenants, Resolver,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<UpsertCodedValueOverrideHandler>.Instance);
            Remove = new RemoveCodedValueOverrideHandler(Db, Tenants);
        }

        public void Dispose() => Db.Dispose();

        private static SettingsDbContext BuildDb(string name, MutableTenantProvider tenants)
        {
            var services = new ServiceCollection();
            // Register the test's MutableTenantProvider as the ITenantProvider so
            // the SettingsDbContext (and its CurrentTenantId property) sees the
            // same tenant as the handler constructors. Without this the DbContext
            // would use the default TenantProvider (Guid.Empty) and the
            // GetCodedValueById handler would read the global name instead of
            // the per-tenant override.
            services.AddSingleton<ITenantProvider>(tenants);
            services.AddDbContext<SettingsDbContext>(o => o.UseInMemoryDatabase(name));
            return services.BuildServiceProvider().GetRequiredService<SettingsDbContext>();
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

    // ── Default-tenant branch: rewrites the global blueprint ─────────────────

    [TestMethod]
    public async Task Upsert_ForDefaultTenant_UpdatesGlobalCodedValue()
    {
        // No real tenant → no override row; the "override" rewrites the
        // global CodedValue.Name so the wizard's "Override name" action still
        // has a visible effect.
        using var s = new Scope("override-default-update");
        var id = await SeedCodedValueAsync(s.Db, "GRADE_5", "Grade 5", displayOrder: 7);

        var dto = await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, "Standard 5", "Renamed for dev"));

        dto.Name.Should().Be("Standard 5");
        dto.Description.Should().Be("Renamed for dev");
        s.Db.TenantCodedValueOverrides.Should().BeEmpty("no real tenant → no override row");
        var stored = await s.Db.CodedValues.SingleAsync(x => x.Id == id);
        stored.Name.Should().Be("Standard 5");
        stored.Description.Should().Be("Renamed for dev");
        stored.DisplayOrder.Should().Be(7, "DisplayOrder is metadata, not part of the override");
    }

    [TestMethod]
    public async Task Remove_ForDefaultTenant_IsNoOp()
    {
        // The "override" was a direct update of the global blueprint, so there
        // is no override row to remove and nothing to revert automatically.
        using var s = new Scope("override-default-remove");
        var id = await SeedCodedValueAsync(s.Db, "GRADE_6", "Grade 6");

        await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, "Standard 6", null));

        var act = async () => await s.Remove.HandleAsync(new RemoveCodedValueOverride(id));
        await act.Should().NotThrowAsync();

        // The global rename persists; remove is a no-op for the default tenant.
        var stored = await s.Db.CodedValues.SingleAsync(x => x.Id == id);
        stored.Name.Should().Be("Standard 6");
    }

    [TestMethod]
    public async Task Upsert_ForDefaultTenant_UpdatesExistingGlobalName()
    {
        // A second override call updates the same global row (not a new row).
        using var s = new Scope("override-default-repeat");
        var id = await SeedCodedValueAsync(s.Db, "GRADE_7", "Grade 7");

        await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, "First", null));
        await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, "Second", null));

        s.Db.TenantCodedValueOverrides.Should().BeEmpty();
        var stored = await s.Db.CodedValues.SingleAsync(x => x.Id == id);
        stored.Name.Should().Be("Second");
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
