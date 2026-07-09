using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.CreateCodedValue;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Tests.Unit.Tenancy;

/// <summary>
/// Acceptance criteria AC-5..AC-10 (global-tenant-filter.md §6.2): hybrid CodedValue
/// tenancy — shared-blueprint visibility, tenant-owned isolation, default-tenant NULL
/// blueprint creation, the retained override pattern, and the duplicate-code guard.
/// Uses a real in-memory <see cref="SettingsDbContext"/> with a controllable
/// <see cref="TenantProvider"/> so the hybrid query filter and save-guard exercise
/// end-to-end (the partial unique indexes are a Postgres backstop, tested separately).
/// </summary>
[TestClass]
public class CodedValueHybridTenancyTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private SettingsDbContext _db = default!;
    private TenantProvider _tenants = default!;
    private TenantContextAccessor _accessor = default!;
    private HybridCache _cache = default!;
    private CodedValueRepository _repo = default!;
    private CodedValueResolver _resolver = default!;
    private CreateCodedValueHandler _handler = default!;
    private Mock<IIntegrationEventPublisher> _publisher = default!;

    [TestInitialize]
    public void Setup()
    {
        _tenants = new TenantProvider();
        _accessor = new TenantContextAccessor(_tenants);

        var options = new DbContextOptionsBuilder<SettingsDbContext>()
            .UseInMemoryDatabase($"HybridCodedValue_{Guid.NewGuid()}")
            .Options;
        _db = new SettingsDbContext(options, _tenants);

        var services = new ServiceCollection();
        services.AddHybridCache();
        _cache = services.BuildServiceProvider().GetRequiredService<HybridCache>();

        _repo = new CodedValueRepository(_db);
        _resolver = new CodedValueResolver(_repo);
        _publisher = new Mock<IIntegrationEventPublisher>();
        _handler = new CreateCodedValueHandler(
            _repo, _publisher.Object, _cache, _tenants, _accessor,
            new Mock<ILogger<CreateCodedValueHandler>>().Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _db.Dispose();
    }

    private void AsTenant(Guid tenantId) =>
        _tenants.SetTenant(new TenantContext(tenantId, tenantId.ToString(), TenantType.School));

    private void AsDefault() => _tenants.Clear();

    /// <summary>Seeds a shared-blueprint (NULL tenant) coded value directly.</summary>
    private async Task<CodedValue> SeedSharedAsync(string code, string name, Guid? parentId = null, int order = 0)
    {
        var cv = CodedValue.Create(code, name, null, parentId, order);
        // NULL rows are allowed by the hybrid guard under any tenant context.
        _db.CodedValues.Add(cv);
        await _db.SaveChangesAsync();
        return cv;
    }

    [TestMethod]
    public async Task AC5_SharedBlueprint_IsVisibleToAllTenants()
    {
        AsTenant(TenantA);
        await SeedSharedAsync("GRADE_1", "Grade 1");

        // Tenant A sees the shared NULL row.
        AsTenant(TenantA);
        (await _db.CodedValues.CountAsync(x => x.Code == "GRADE_1")).Should().Be(1);

        // Tenant B also sees the shared NULL row (hybrid filter surfaces NULL to all).
        AsTenant(TenantB);
        (await _db.CodedValues.CountAsync(x => x.Code == "GRADE_1")).Should().Be(1,
            "the hybrid filter surfaces shared NULL-blueprint rows to every tenant");
    }

    [TestMethod]
    public async Task AC6_TenantOwnedRow_IsolatedFromOtherTenants()
    {
        AsTenant(TenantA);
        await _handler.HandleAsync(new CreateCodedValue("MATH_A", "Math A", null, null, 0));

        // The created row is tenant-owned (TenantId == A).
        AsTenant(TenantA);
        var owned = await _db.CodedValues.SingleAsync(x => x.Code == "MATH_A");
        owned.TenantId.Should().Be(TenantA, "real-tenant creation stamps a tenant-owned row (FR-5)");

        // Tenant B does NOT see tenant A's owned row (only shared NULL rows).
        AsTenant(TenantB);
        (await _db.CodedValues.AnyAsync(x => x.Code == "MATH_A")).Should().BeFalse(
            "tenant-owned rows are isolated from other tenants by the hybrid filter");
    }

    [TestMethod]
    public async Task AC7_DefaultTenant_Create_WritesNullBlueprintRow()
    {
        AsDefault();
        await _handler.HandleAsync(new CreateCodedValue("GRADES", "Grades", null, null, 0));

        // No Guid.Empty is ever stored; the row is a NULL shared blueprint.
        var row = await _db.CodedValues.IgnoreQueryFilters(["Tenant"])
            .SingleAsync(x => x.Code == "GRADES");
        row.TenantId.Should().BeNull("the default/dev path writes a NULL shared-blueprint row (FR-5)");
    }

    [TestMethod]
    public async Task AC8_Resolver_RetainsPerTenantOverrideForSharedRow()
    {
        AsTenant(TenantA);
        var shared = await SeedSharedAsync("GRADE_1", "Grade 1");

        // Hydeson-style override on the shared NULL row (override pattern retained — AC-8).
        _db.TenantCodedValueOverrides.Add(
            TenantCodedValueOverride.Create(TenantA, shared.Id, "Standard 1", null));
        await _db.SaveChangesAsync();

        // Resolver returns the overridden name; CodedValueResolver code is unchanged.
        AsTenant(TenantA);
        var cv = await _db.CodedValues.SingleAsync(x => x.Code == "GRADE_1");
        var resolved = await _resolver.ResolveAsync(cv, TenantA);
        resolved.Name.Should().Be("Standard 1", "the retained override pattern overlays the shared row's name");
        resolved.IsOverridden.Should().BeTrue();
        resolved.DefaultName.Should().Be("Grade 1");
    }

    [TestMethod]
    public async Task AC9_DuplicateGuard_RejectsTenantDuplicateOfSharedRow()
    {
        AsTenant(TenantA);
        await SeedSharedAsync("GRADE_1", "Grade 1");

        AsTenant(TenantA);
        var act = async () => await _handler.HandleAsync(
            new CreateCodedValue("grade_1", "My Grade 1", null, null, 1));

        var ex = await act.Should().ThrowAsync<CodedValueCodeConflictException>();
        ex.Which.ExistingIsSharedBlueprint.Should().BeTrue(
            "the guard directs the tenant to override the shared row's name, not create a duplicate (FR-6)");

        // No duplicate was created.
        AsTenant(TenantA);
        (await _db.CodedValues.IgnoreQueryFilters(["Tenant"]).CountAsync(x => x.Code == "GRADE_1"))
            .Should().Be(1, "no duplicate row is created when the guard rejects");
    }

    [TestMethod]
    public async Task AC10_TwoTenants_CanEachOwnSameCode_DuplicateWithinTenantFails()
    {
        var subjectId = Guid.NewGuid();
        AsTenant(TenantA);
        await _handler.HandleAsync(new CreateCodedValue("MATH", "Math A", null, subjectId, 0));

        AsTenant(TenantB);
        await _handler.HandleAsync(new CreateCodedValue("MATH", "Math B", null, subjectId, 0));

        // Both owned rows exist (one per tenant) — the partial unique index allows one
        // owned row per (tenant, parent, code).
        AsTenant(TenantA);
        (await _db.CodedValues.CountAsync(x => x.Code == "MATH")).Should().Be(1);
        AsTenant(TenantB);
        (await _db.CodedValues.CountAsync(x => x.Code == "MATH")).Should().Be(1);

        // Tenant A creating another MATH under the same parent is rejected by the guard
        // (the tenant's own owned row already exists).
        AsTenant(TenantA);
        var act = async () => await _handler.HandleAsync(
            new CreateCodedValue("math", "Math A2", null, subjectId, 1));
        var ex = await act.Should().ThrowAsync<CodedValueCodeConflictException>();
        ex.Which.ExistingIsSharedBlueprint.Should().BeFalse(
            "the conflict is the tenant's own owned row, not a shared blueprint");
    }
}
