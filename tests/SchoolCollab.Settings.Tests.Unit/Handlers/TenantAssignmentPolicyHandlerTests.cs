using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.AssignmentPolicies.Commands.UpsertTenantAssignmentPolicy;
using SchoolCollab.Settings.Core.CQRS.AssignmentPolicies.Queries.GetTenantAssignmentPolicy;
using SchoolCollab.Settings.Core.Data;

namespace SchoolCollab.Settings.Tests.Unit.Handlers;

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
        result.Should().BeNull();
    }

    [TestMethod]
    public async Task Upsert_CreatesRow_GetReturnsDto()
    {
        AsTenant(TenantA);
        await _upsertHandler.HandleAsync(new UpsertTenantAssignmentPolicy(RequiresSignatureDefault: true));

        var result = await _getHandler.HandleAsync(GetTenantAssignmentPolicy.Instance);
        result.Should().NotBeNull();
        result!.RequiresSignatureDefault.Should().BeTrue();
    }

    [TestMethod]
    public async Task Upsert_Twice_ReplacesValue()
    {
        AsTenant(TenantA);
        await _upsertHandler.HandleAsync(new UpsertTenantAssignmentPolicy(RequiresSignatureDefault: true));
        await _upsertHandler.HandleAsync(new UpsertTenantAssignmentPolicy(RequiresSignatureDefault: false));

        var result = await _getHandler.HandleAsync(GetTenantAssignmentPolicy.Instance);
        result!.RequiresSignatureDefault.Should().BeFalse();
        _db.TenantAssignmentPolicies.Count().Should().Be(1);
    }
}