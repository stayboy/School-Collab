using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Tests.Unit.Domain;

/// <summary>
/// Round A (<c>documents/solution/assignment-policy-fields.md</c> §4) — the tenant-global
/// assignment policy widened from one boolean to the shared four-field shape. Every field is
/// nullable and null means "unset" (the built-in default applies at resolution time).
/// </summary>
[TestClass]
public class TenantAssignmentPolicyTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [TestMethod]
    public void Create_DefaultsToEveryFieldUnset()
    {
        var p = TenantAssignmentPolicy.Create(TenantId);

        p.TenantId.Should().Be(TenantId);
        p.SignatureRequirement.Should().BeNull();
        p.RequiresApprovalBeforePublish.Should().BeNull();
        p.MaxPrimaryContacts.Should().BeNull();
        p.MaxCopyContacts.Should().BeNull();
    }

    [TestMethod]
    public void Create_WithTheWholeFieldSet_StoresEveryField()
    {
        var p = TenantAssignmentPolicy.Create(
            TenantId,
            signatureRequirement: SignatureRequirementMode.Mandatory,
            requiresApprovalBeforePublish: true,
            maxPrimaryContacts: 2,
            maxCopyContacts: 4);

        p.SignatureRequirement.Should().Be(SignatureRequirementMode.Mandatory);
        p.RequiresApprovalBeforePublish.Should().BeTrue();
        p.MaxPrimaryContacts.Should().Be(2);
        p.MaxCopyContacts.Should().Be(4);
    }

    [TestMethod]
    public void Create_AcceptsTheDisabledTriState_NotJustTrue()
    {
        var p = TenantAssignmentPolicy.Create(TenantId, signatureRequirement: SignatureRequirementMode.Disabled);

        p.SignatureRequirement.Should().Be(SignatureRequirementMode.Disabled,
            "an explicit Disabled is a stored value, distinct from unset (null)");
    }

    [TestMethod]
    public void SetPolicy_ReplacesEveryFieldAndStampsUpdatedAt()
    {
        var p = TenantAssignmentPolicy.Create(TenantId);
        var before = p.UpdatedAt;

        p.SetPolicy(
            signatureRequirement: SignatureRequirementMode.Optional,
            requiresApprovalBeforePublish: true,
            maxPrimaryContacts: 1,
            maxCopyContacts: 3);

        p.SignatureRequirement.Should().Be(SignatureRequirementMode.Optional);
        p.RequiresApprovalBeforePublish.Should().BeTrue();
        p.MaxPrimaryContacts.Should().Be(1);
        p.MaxCopyContacts.Should().Be(3);
        p.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [TestMethod]
    public void SetPolicy_WithAllNulls_ClearsEveryFieldBackToUnset()
    {
        var p = TenantAssignmentPolicy.Create(
            TenantId, SignatureRequirementMode.Mandatory, true, 2, 4);

        p.SetPolicy(null, null, null, null);

        p.SignatureRequirement.Should().BeNull();
        p.RequiresApprovalBeforePublish.Should().BeNull();
        p.MaxPrimaryContacts.Should().BeNull();
        p.MaxCopyContacts.Should().BeNull();
    }

    [TestMethod]
    public void SetPolicy_StoresExplicitFalse_NotAsUnset()
    {
        var p = TenantAssignmentPolicy.Create(TenantId, requiresApprovalBeforePublish: true);

        p.SetPolicy(null, requiresApprovalBeforePublish: false, null, null);

        p.RequiresApprovalBeforePublish.Should().BeFalse("false is a configured value, not 'unset'");
    }
}
