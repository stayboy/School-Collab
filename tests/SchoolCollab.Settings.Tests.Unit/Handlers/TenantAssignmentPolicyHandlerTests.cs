using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.AssignmentPolicies.Commands.UpsertTenantAssignmentPolicy;
using SchoolCollab.Settings.Core.CQRS.AssignmentPolicies.Queries.GetTenantAssignmentPolicy;
using SchoolCollab.Settings.Core.Data;

namespace SchoolCollab.Settings.Tests.Unit.Handlers;

/// <summary>
/// Round A (<c>documents/solution/assignment-policy-fields.md</c> §4) — the Settings CQRS pair
/// round-trips the whole four-field assignment-policy shape through upsert → get, and the
/// pre-existing 204-when-unset behaviour is unchanged.
/// </summary>
[TestClass]
public class TenantAssignmentPolicyHandlerTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private SettingsDbContext _db = default!;
    private TenantProvider _tenants = default!;
    private GetTenantAssignmentPolicyHandler _getHandler = default!;
    private UpsertTenantAssignmentPolicyHandler _upsertHandler = default!;

    [TestInitialize]
    public void Setup()
    {
        _tenants = new TenantProvider();
        var options = new DbContextOptionsBuilder<SettingsDbContext>()
            .UseInMemoryDatabase($"AssignmentPolicy_{Guid.NewGuid()}")
            .Options;
        _db = new SettingsDbContext(options, _tenants);
        _getHandler = new GetTenantAssignmentPolicyHandler(_db);
        _upsertHandler = new UpsertTenantAssignmentPolicyHandler(_db, _tenants);
    }

    [TestCleanup]
    public void Cleanup() => _db.Dispose();

    private void AsTenant(Guid tenantId) =>
        _tenants.SetTenant(new TenantContext(tenantId, tenantId.ToString(), TenantType.School));

    [TestMethod]
    public async Task Get_WhenUnset_ReturnsNull()
    {
        AsTenant(TenantA);
        var result = await _getHandler.HandleAsync(GetTenantAssignmentPolicy.Instance);
        result.Should().BeNull("no row means 204 on the wire — the caller applies built-in defaults");
    }

    [TestMethod]
    public async Task Upsert_CreatesRow_GetRoundTripsEveryField()
    {
        AsTenant(TenantA);
        await _upsertHandler.HandleAsync(new UpsertTenantAssignmentPolicy(
            SignatureRequirementMode.Optional,
            RequiresApprovalBeforePublish: true,
            MaxPrimaryContacts: 2,
            MaxCopyContacts: 4));

        var result = await _getHandler.HandleAsync(GetTenantAssignmentPolicy.Instance);

        result.Should().NotBeNull();
        result!.SignatureRequirement.Should().Be(SignatureRequirementMode.Optional);
        result.RequiresApprovalBeforePublish.Should().BeTrue();
        result.MaxPrimaryContacts.Should().Be(2);
        result.MaxCopyContacts.Should().Be(4);
    }

    [TestMethod]
    public async Task Upsert_Twice_ReplacesEveryField()
    {
        AsTenant(TenantA);
        await _upsertHandler.HandleAsync(new UpsertTenantAssignmentPolicy(
            SignatureRequirementMode.Mandatory, true, 1, 1));
        await _upsertHandler.HandleAsync(new UpsertTenantAssignmentPolicy(
            SignatureRequirementMode.Disabled, false, 7, 9));

        var result = await _getHandler.HandleAsync(GetTenantAssignmentPolicy.Instance);

        result!.SignatureRequirement.Should().Be(SignatureRequirementMode.Disabled);
        result.RequiresApprovalBeforePublish.Should().BeFalse();
        result.MaxPrimaryContacts.Should().Be(7);
        result.MaxCopyContacts.Should().Be(9);
        _db.TenantAssignmentPolicies.Count().Should().Be(1, "one policy row per tenant");
    }

    [TestMethod]
    public async Task Upsert_WithAllNulls_StoresUnsetAndGetStillReturnsARow()
    {
        AsTenant(TenantA);
        await _upsertHandler.HandleAsync(new UpsertTenantAssignmentPolicy(null, null, null, null));

        var result = await _getHandler.HandleAsync(GetTenantAssignmentPolicy.Instance);

        result.Should().NotBeNull("the row exists; only its fields are unset");
        result!.SignatureRequirement.Should().BeNull();
        result.RequiresApprovalBeforePublish.Should().BeNull();
        result.MaxPrimaryContacts.Should().BeNull();
        result.MaxCopyContacts.Should().BeNull();
    }

    [TestMethod]
    public async Task Upsert_ReturnsThePersistedFieldSet()
    {
        AsTenant(TenantA);

        var result = await _upsertHandler.HandleAsync(new UpsertTenantAssignmentPolicy(
            SignatureRequirementMode.Mandatory, null, 3, null));

        result.SignatureRequirement.Should().Be(SignatureRequirementMode.Mandatory);
        result.RequiresApprovalBeforePublish.Should().BeNull();
        result.MaxPrimaryContacts.Should().Be(3);
        result.MaxCopyContacts.Should().BeNull();
    }

    // ── Q7 — non-positive contact caps are rejected on the write path ─────

    [TestMethod]
    public async Task Upsert_WithZeroPrimaryCap_ThrowsAndPersistsNothing()
    {
        AsTenant(TenantA);

        var act = () => _upsertHandler.HandleAsync(new UpsertTenantAssignmentPolicy(
            SignatureRequirementMode.Optional, null, MaxPrimaryContacts: 0, MaxCopyContacts: 3));

        var ex = await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        ex.Which.ParamName.Should().Be("MaxPrimaryContacts", "the guard names the offending field");
        (await _db.TenantAssignmentPolicies.CountAsync()).Should().Be(0,
            "a rejected cap never creates a policy row");
    }

    [TestMethod]
    public async Task Upsert_WithNegativeCopyCap_ThrowsAndPersistsNothing()
    {
        AsTenant(TenantA);

        var act = () => _upsertHandler.HandleAsync(new UpsertTenantAssignmentPolicy(
            SignatureRequirementMode.Optional, null, MaxPrimaryContacts: 3, MaxCopyContacts: -1));

        var ex = await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        ex.Which.ParamName.Should().Be("MaxCopyContacts", "the guard names the offending field");
        (await _db.TenantAssignmentPolicies.CountAsync()).Should().Be(0,
            "the guard runs before the row is created");
    }

    [TestMethod]
    public async Task Upsert_WithNullCaps_IsAccepted()
    {
        AsTenant(TenantA);

        var result = await _upsertHandler.HandleAsync(new UpsertTenantAssignmentPolicy(
            SignatureRequirementMode.Optional, true, MaxPrimaryContacts: null, MaxCopyContacts: null));

        result.MaxPrimaryContacts.Should().BeNull("null stays unset — it is not a non-positive cap");
        result.MaxCopyContacts.Should().BeNull();
        result.SignatureRequirement.Should().Be(SignatureRequirementMode.Optional);
    }
}
