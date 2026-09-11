using FluentAssertions;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Tests.Unit.Domain;

[TestClass]
public class TenantAssignmentPolicyTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [TestMethod]
    public void Create_DefaultsToRequiresSignatureFalse()
    {
        var p = TenantAssignmentPolicy.Create(TenantId);
        p.TenantId.Should().Be(TenantId);
        p.RequiresSignatureDefault.Should().BeFalse();
    }

    [TestMethod]
    public void Create_WithTrue_StoresFlag()
    {
        var p = TenantAssignmentPolicy.Create(TenantId, requiresSignatureDefault: true);
        p.RequiresSignatureDefault.Should().BeTrue();
    }

    [TestMethod]
    public void SetPolicy_UpdatesFlagAndStampsUpdatedAt()
    {
        var p = TenantAssignmentPolicy.Create(TenantId);
        var before = p.UpdatedAt;

        p.SetPolicy(requiresSignatureDefault: true);

        p.RequiresSignatureDefault.Should().BeTrue();
        p.UpdatedAt.Should().BeOnOrAfter(before);
    }
}