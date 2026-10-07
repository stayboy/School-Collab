using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit.Domain;

/// <summary>
/// Round A (<c>documents/solution/assignment-policy-fields.md</c> §4) — the per-grade assignment
/// policy widened from one nullable boolean to the shared field shape; round
/// <c>assignment-rules-policy-rework</c> (D3/D5/D7) adds the guardian-review and archive-window
/// fields to the same set. A null field means "inherit the tenant default"; a non-null field
/// overrides it.
/// </summary>
[TestClass]
public class GradeAssignmentPolicyTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GradeId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [TestMethod]
    public void Create_DefaultsToInheritingEveryField()
    {
        var p = GradeAssignmentPolicy.Create(TenantId, GradeId);

        p.TenantId.Should().Be(TenantId);
        p.GradeLevelId.Should().Be(GradeId);
        p.SignatureRequirement.Should().BeNull();
        p.RequiresApprovalBeforePublish.Should().BeNull();
        p.MaxPrimaryContacts.Should().BeNull();
        p.MaxCopyContacts.Should().BeNull();
        p.MandatoryReview.Should().BeNull();
        p.ArchiveGraceDays.Should().BeNull();
    }

    [TestMethod]
    public void Create_WithTheWholeFieldSet_StoresEveryOverride()
    {
        var p = GradeAssignmentPolicy.Create(
            TenantId,
            GradeId,
            signatureRequirement: SignatureRequirementMode.Mandatory,
            requiresApprovalBeforePublish: true,
            maxPrimaryContacts: 1,
            maxCopyContacts: 2,
            mandatoryReview: true,
            archiveGraceDays: 45);

        p.SignatureRequirement.Should().Be(SignatureRequirementMode.Mandatory);
        p.RequiresApprovalBeforePublish.Should().BeTrue();
        p.MaxPrimaryContacts.Should().Be(1);
        p.MaxCopyContacts.Should().Be(2);
        p.MandatoryReview.Should().BeTrue();
        p.ArchiveGraceDays.Should().Be(45);
    }

    [TestMethod]
    public void SetOverride_ToAllNulls_RestoresInheritForEveryField()
    {
        var p = GradeAssignmentPolicy.Create(
            TenantId, GradeId, SignatureRequirementMode.Mandatory, true, 1, 2,
            mandatoryReview: true, archiveGraceDays: 45);

        p.SetOverride(null, null, null, null, null, null);

        p.SignatureRequirement.Should().BeNull();
        p.RequiresApprovalBeforePublish.Should().BeNull();
        p.MaxPrimaryContacts.Should().BeNull();
        p.MaxCopyContacts.Should().BeNull();
        p.MandatoryReview.Should().BeNull();
        p.ArchiveGraceDays.Should().BeNull();
    }

    [TestMethod]
    public void SetOverride_StampsUpdatedAt_AndKeepsExplicitValues()
    {
        var p = GradeAssignmentPolicy.Create(TenantId, GradeId);
        var before = p.UpdatedAt;

        p.SetOverride(SignatureRequirementMode.Disabled, requiresApprovalBeforePublish: false, null, 3,
            mandatoryReview: false, archiveGraceDays: 14);

        p.SignatureRequirement.Should().Be(SignatureRequirementMode.Disabled);
        p.RequiresApprovalBeforePublish.Should().BeFalse("false overrides the tenant value, it does not inherit");
        p.MaxCopyContacts.Should().Be(3);
        p.MandatoryReview.Should().BeFalse("explicit false overrides the tenant value");
        p.ArchiveGraceDays.Should().Be(14);
        p.UpdatedAt.Should().BeOnOrAfter(before);
    }
}
