using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.Tenants.Commands.SetDefaultTenant;
using SchoolCollab.Settings.Core.CQRS.Tenants.Commands.SetUserDefaultTenant;
using SchoolCollab.Settings.Core.CQRS.Tenants.Queries.GetUserDefaultTenant;
using SchoolCollab.Settings.Core.CQRS.Tenants.Queries.ListTenants;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Tests.Unit.Tenants;

/// <summary>
/// The admin portal's default-tenant designation: the platform flag on <see cref="Tenant"/>
/// and the per-operator preference row. The two are deliberately separate — the platform
/// default is one row the whole portal shares, the preference is keyed by OIDC subject —
/// so they are tested separately, and their composition is tested on the resolver in
/// <c>SchoolCollab.Admin.Tests.Unit</c>.
/// </summary>
[TestClass]
public class TenantDefaultDesignationTests
{
    private SettingsDbContext _db = default!;

    [TestInitialize]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<SettingsDbContext>()
            .UseInMemoryDatabase($"TenantDefaults_{Guid.NewGuid()}")
            // SetDefaultTenantHandler wraps its two saves in an explicit transaction so the
            // partial unique index is never transiently violated. The in-memory provider has no
            // real transactions and warns instead; the invariant itself is covered against real
            // PostgreSQL by the Settings API integration tests.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new SettingsDbContext(options, new TenantProvider());
    }

    [TestCleanup]
    public void Cleanup() => _db.Dispose();

    private Tenant SeedTenant(string name, bool isDefault = false)
    {
        var tenant = Tenant.Create(name, TenantType.School, isDefault);
        _db.Tenants.Add(tenant);
        return tenant;
    }

    // ── Platform default (Tenant.IsDefault) ─ ───────────────────────────────

    [TestMethod]
    public async Task Tenant_Create_IsNotDefaultUnlessAsked()
    {
        SeedTenant("Alpha");
        await _db.SaveChangesAsync();

        (await _db.Tenants.SingleAsync()).IsDefault.Should().BeFalse(
            "a fresh tenant must never silently become the portal default");
    }

    [TestMethod]
    public async Task SetDefaultTenant_MovesTheDesignation_AndClearsThePrevious()
    {
        var first = SeedTenant("Alpha", isDefault: true);
        var second = SeedTenant("Beta");
        await _db.SaveChangesAsync();

        await new SetDefaultTenantHandler(_db).HandleAsync(new SetDefaultTenant(second.Id));

        var rows = await _db.Tenants.AsNoTracking().ToListAsync();
        rows.Single(t => t.Id == second.Id).IsDefault.Should().BeTrue();
        rows.Single(t => t.Id == first.Id).IsDefault.Should().BeFalse();
        rows.Count(t => t.IsDefault).Should().Be(1,
            "the partial unique index permits exactly one designated tenant");
    }

    [TestMethod]
    public async Task SetDefaultTenant_WhenNoneWasDesignated_DesignatesIt()
    {
        var tenant = SeedTenant("Alpha");
        await _db.SaveChangesAsync();

        await new SetDefaultTenantHandler(_db).HandleAsync(new SetDefaultTenant(tenant.Id));

        (await _db.Tenants.AsNoTracking().SingleAsync()).IsDefault.Should().BeTrue();
    }

    [TestMethod]
    public async Task SetDefaultTenant_WhenAlreadyTheDefault_IsIdempotent()
    {
        var tenant = SeedTenant("Alpha", isDefault: true);
        await _db.SaveChangesAsync();
        var before = await _db.Tenants.AsNoTracking().SingleAsync();

        await new SetDefaultTenantHandler(_db).HandleAsync(new SetDefaultTenant(tenant.Id));

        var after = await _db.Tenants.AsNoTracking().SingleAsync();
        after.IsDefault.Should().BeTrue();
        after.UpdatedAt.Should().Be(before.UpdatedAt,
            "re-designating the current default is a no-op, so it must not churn the audit timestamp");
    }

    [TestMethod]
    public async Task SetDefaultTenant_ForAnUnknownTenant_Throws()
    {
        SeedTenant("Alpha");
        await _db.SaveChangesAsync();
        var missing = Guid.NewGuid();

        var act = () => new SetDefaultTenantHandler(_db).HandleAsync(new SetDefaultTenant(missing));

        (await act.Should().ThrowAsync<TenantNotFoundException>())
            .Which.TenantId.Should().Be(missing);
    }

    [TestMethod]
    public async Task ListTenants_FlagsTheDesignatedDefault()
    {
        SeedTenant("Alpha", isDefault: true);
        SeedTenant("Beta");
        await _db.SaveChangesAsync();

        var result = await new ListTenantsHandler(_db).HandleAsync(new ListTenants());

        result.Should().HaveCount(2);
        result.Single(t => t.Name == "Alpha").IsDefault.Should().BeTrue();
        result.Single(t => t.Name == "Beta").IsDefault.Should().BeFalse();
    }

    // ── Per-operator preference (AdminUserPreference) ────────────────────────

    [TestMethod]
    public async Task GetUserDefaultTenant_WithNoRow_ReportsNoPreference()
    {
        var result = await new GetUserDefaultTenantHandler(_db)
            .HandleAsync(new GetUserDefaultTenant("user-with-no-preference"));

        result.DefaultTenantId.Should().BeNull(
            "a missing preference is the documented 'none recorded' state, not a 404");
    }

    [TestMethod]
    public async Task SetUserDefaultTenant_CreatesThenUpdatesTheSameRow()
    {
        var first = SeedTenant("Alpha");
        var second = SeedTenant("Beta");
        await _db.SaveChangesAsync();

        var handler = new SetUserDefaultTenantHandler(_db);
        await handler.HandleAsync(new SetUserDefaultTenant("subject-1", first.Id));
        await handler.HandleAsync(new SetUserDefaultTenant("subject-1", second.Id));

        var rows = await _db.AdminUserPreferences.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(1, "one row per operator — the unique index on subject_id is the invariant");
        rows[0].DefaultTenantId.Should().Be(second.Id);
    }

    [TestMethod]
    public async Task SetUserDefaultTenant_IsScopedToItsOwnSubject()
    {
        var tenant = SeedTenant("Alpha");
        await _db.SaveChangesAsync();

        var handler = new SetUserDefaultTenantHandler(_db);
        await handler.HandleAsync(new SetUserDefaultTenant("subject-1", tenant.Id));

        var other = await new GetUserDefaultTenantHandler(_db)
            .HandleAsync(new GetUserDefaultTenant("subject-2"));

        other.DefaultTenantId.Should().BeNull("one operator's preference must not leak into another's");
    }

    [TestMethod]
    public async Task SetUserDefaultTenant_WithNull_ClearsThePreference()
    {
        var tenant = SeedTenant("Alpha");
        await _db.SaveChangesAsync();

        var handler = new SetUserDefaultTenantHandler(_db);
        await handler.HandleAsync(new SetUserDefaultTenant("subject-1", tenant.Id));
        await handler.HandleAsync(new SetUserDefaultTenant("subject-1", null));

        var result = await new GetUserDefaultTenantHandler(_db)
            .HandleAsync(new GetUserDefaultTenant("subject-1"));

        result.DefaultTenantId.Should().BeNull(
            "clearing must fall back to the platform default, which is a different state from Guid.Empty");
    }

    [TestMethod]
    public async Task SetUserDefaultTenant_AcceptsTheSystemContextSentinel()
    {
        var handler = new SetUserDefaultTenantHandler(_db);

        // Guid.Empty is the system / no-tenant context — legitimately absent from the registry,
        // and meaningfully different from null (which means "no preference recorded").
        await handler.HandleAsync(new SetUserDefaultTenant("system-preferrer", Guid.Empty));

        var result = await new GetUserDefaultTenantHandler(_db)
            .HandleAsync(new GetUserDefaultTenant("system-preferrer"));

        result.DefaultTenantId.Should().Be(Guid.Empty,
            "preferring the shared-blueprint context is a recorded preference, not the absence of one");
    }

    [TestMethod]
    public async Task SetUserDefaultTenant_ForAnUnknownTenant_Throws()
    {
        var missing = Guid.NewGuid();

        var act = () => new SetUserDefaultTenantHandler(_db)
            .HandleAsync(new SetUserDefaultTenant("subject-1", missing));

        (await act.Should().ThrowAsync<TenantNotFoundException>())
            .Which.TenantId.Should().Be(missing);
    }

    [TestMethod]
    public async Task SetUserDefaultTenant_WithABlankSubject_Throws()
    {
        var act = () => new SetUserDefaultTenantHandler(_db)
            .HandleAsync(new SetUserDefaultTenant("   ", null));

        await act.Should().ThrowAsync<ArgumentException>(
            "the row is keyed by subject; a blank one would collide across every anonymous caller");
    }

    [TestMethod]
    public async Task GetUserDefaultTenant_WithABlankSubject_ReportsNoPreference()
    {
        var result = await new GetUserDefaultTenantHandler(_db)
            .HandleAsync(new GetUserDefaultTenant("   "));

        result.DefaultTenantId.Should().BeNull("a claim-less caller has no preference to report");
    }

    [TestMethod]
    public async Task AdminUserPreference_SetDefaultTenant_IsIdempotent()
    {
        var tenant = Guid.NewGuid();
        var preference = AdminUserPreference.Create("subject-1", tenant);
        var before = preference.UpdatedAt;

        preference.SetDefaultTenant(tenant);

        preference.UpdatedAt.Should().Be(before, "re-recording the same preference must not churn the timestamp");
    }
}
