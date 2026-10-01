using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.AssignmentPolicies;

namespace SchoolCollab.Assignments.Tests.Unit.AssignmentPolicies;

/// <summary>
/// Round A (<c>documents/solution/assignment-policy-fields.md</c> D1, §5) coverage for the pure
/// <see cref="EffectiveAssignmentPolicyResolver"/>: a non-null grade override wins per field, a null
/// grade field inherits the tenant value, an unset tenant value falls to the exact built-in
/// defaults (Disabled / false / null / null), and each <c>*FromOverride</c> flag is true iff the
/// grade field was non-null. Mirrors <c>EffectiveNotificationPolicy</c>'s merge contract.
/// </summary>
[TestClass]
public class EffectiveAssignmentPolicyResolverTests
{
    private static readonly EffectiveAssignmentPolicyResolver Resolver = new();

    private static AssignmentPolicyFields TenantDefault() => new()
    {
        SignatureRequirement = SignatureRequirementMode.Disabled,
        RequiresApprovalBeforePublish = false,
        MaxPrimaryContacts = 5,
        MaxCopyContacts = 10,
    };

    [TestMethod]
    public void Resolve_NoTenantValueNoOverride_YieldsTheExactBuiltInDefaults()
    {
        // Arrange / Act
        var resolved = Resolver.Resolve(tenantDefault: null, gradeOverride: null);

        // Assert
        resolved.SignatureRequirement.Should().Be(SignatureRequirementMode.Disabled,
            "the old RequiresSignatureDefault = false maps to Disabled");
        resolved.RequiresApprovalBeforePublish.Should().BeFalse();
        resolved.MaxPrimaryContacts.Should().BeNull("no cap configured means uncapped");
        resolved.MaxCopyContacts.Should().BeNull();
    }

    [TestMethod]
    public void Resolve_EmptyTenantDefault_YieldsTheSameBuiltInDefaultsAsNull()
    {
        // Arrange / Act
        var resolved = Resolver.Resolve(AssignmentPolicyFields.Empty, gradeOverride: null);

        // Assert
        resolved.Should().Be(Resolver.Resolve(tenantDefault: null, gradeOverride: null),
            "an all-null tenant row is semantically identical to no row");
    }

    [TestMethod]
    public void Resolve_TenantValuesOnly_AreUsedWithNoOverrideFlags()
    {
        // Arrange / Act
        var resolved = Resolver.Resolve(TenantDefault(), gradeOverride: null);

        // Assert
        resolved.SignatureRequirement.Should().Be(SignatureRequirementMode.Disabled);
        resolved.RequiresApprovalBeforePublish.Should().BeFalse();
        resolved.MaxPrimaryContacts.Should().Be(5);
        resolved.MaxCopyContacts.Should().Be(10);
        resolved.SignatureRequirementFromOverride.Should().BeFalse();
        resolved.RequiresApprovalBeforePublishFromOverride.Should().BeFalse();
        resolved.MaxPrimaryContactsFromOverride.Should().BeFalse();
        resolved.MaxCopyContactsFromOverride.Should().BeFalse();
    }

    [TestMethod]
    public void Resolve_EveryGradeFieldSet_GradeWinsPerField_WithEveryOverrideFlagTrue()
    {
        // Arrange
        var grade = new AssignmentPolicyFields
        {
            SignatureRequirement = SignatureRequirementMode.Mandatory,
            RequiresApprovalBeforePublish = true,
            MaxPrimaryContacts = 2,
            MaxCopyContacts = 3,
        };

        // Act
        var resolved = Resolver.Resolve(TenantDefault(), grade);

        // Assert
        resolved.SignatureRequirement.Should().Be(SignatureRequirementMode.Mandatory,
            "a non-null grade field overrides the tenant value");
        resolved.RequiresApprovalBeforePublish.Should().BeTrue();
        resolved.MaxPrimaryContacts.Should().Be(2);
        resolved.MaxCopyContacts.Should().Be(3);
        resolved.SignatureRequirementFromOverride.Should().BeTrue();
        resolved.RequiresApprovalBeforePublishFromOverride.Should().BeTrue();
        resolved.MaxPrimaryContactsFromOverride.Should().BeTrue();
        resolved.MaxCopyContactsFromOverride.Should().BeTrue();
    }

    [TestMethod]
    public void Resolve_PartiallySetGradeFields_InheritPerFieldAndFlagOnlyTheSetOnes()
    {
        // Arrange — only the copy cap is overridden; the rest must inherit the tenant row.
        var grade = new AssignmentPolicyFields { MaxCopyContacts = 1 };

        // Act
        var resolved = Resolver.Resolve(TenantDefault(), grade);

        // Assert
        resolved.MaxCopyContacts.Should().Be(1);
        resolved.MaxCopyContactsFromOverride.Should().BeTrue();

        resolved.SignatureRequirement.Should().Be(SignatureRequirementMode.Disabled,
            "an unset grade field inherits the tenant default");
        resolved.RequiresApprovalBeforePublish.Should().BeFalse();
        resolved.MaxPrimaryContacts.Should().Be(5);
        resolved.SignatureRequirementFromOverride.Should().BeFalse();
        resolved.RequiresApprovalBeforePublishFromOverride.Should().BeFalse();
        resolved.MaxPrimaryContactsFromOverride.Should().BeFalse();
    }

    [TestMethod]
    public void Resolve_ExplicitFalseAndZero_OverrideTheTenantValues_NotJustNonNullValues()
    {
        // Arrange — false/0 are real values, not "unset": the tenant must not leak through.
        var tenant = new AssignmentPolicyFields
        {
            RequiresApprovalBeforePublish = true,
            MaxPrimaryContacts = 7,
        };
        var grade = new AssignmentPolicyFields
        {
            RequiresApprovalBeforePublish = false,
            MaxPrimaryContacts = 0,
        };

        // Act
        var resolved = Resolver.Resolve(tenant, grade);

        // Assert
        resolved.RequiresApprovalBeforePublish.Should().BeFalse();
        resolved.RequiresApprovalBeforePublishFromOverride.Should().BeTrue();
        resolved.MaxPrimaryContacts.Should().Be(0);
        resolved.MaxPrimaryContactsFromOverride.Should().BeTrue();
    }

    [TestMethod]
    public void Resolve_OverrideOnlyNoTenantDefault_ReplacesTheBuiltIns()
    {
        // Arrange
        var grade = new AssignmentPolicyFields
        {
            SignatureRequirement = SignatureRequirementMode.Optional,
            RequiresApprovalBeforePublish = true,
        };

        // Act
        var resolved = Resolver.Resolve(tenantDefault: null, gradeOverride: grade);

        // Assert
        resolved.SignatureRequirement.Should().Be(SignatureRequirementMode.Optional);
        resolved.RequiresApprovalBeforePublish.Should().BeTrue();
        resolved.MaxPrimaryContacts.Should().BeNull();
        resolved.SignatureRequirementFromOverride.Should().BeTrue();
    }

    [TestMethod]
    public void Resolve_EveryFromOverrideFlag_IsTrueIffTheGradeFieldWasNonNull()
    {
        // Arrange — one field set at a time, so each flag is pinned to its own field.
        var tenant = TenantDefault();

        // Act / Assert
        Resolver.Resolve(tenant, new AssignmentPolicyFields { SignatureRequirement = SignatureRequirementMode.Optional })
            .Should().Match<EffectiveAssignmentPolicy>(p =>
                p.SignatureRequirementFromOverride
                && !p.RequiresApprovalBeforePublishFromOverride
                && !p.MaxPrimaryContactsFromOverride
                && !p.MaxCopyContactsFromOverride);

        Resolver.Resolve(tenant, new AssignmentPolicyFields { RequiresApprovalBeforePublish = false })
            .Should().Match<EffectiveAssignmentPolicy>(p =>
                !p.SignatureRequirementFromOverride
                && p.RequiresApprovalBeforePublishFromOverride
                && !p.MaxPrimaryContactsFromOverride
                && !p.MaxCopyContactsFromOverride);

        Resolver.Resolve(tenant, new AssignmentPolicyFields { MaxPrimaryContacts = 1 })
            .Should().Match<EffectiveAssignmentPolicy>(p =>
                !p.SignatureRequirementFromOverride
                && !p.RequiresApprovalBeforePublishFromOverride
                && p.MaxPrimaryContactsFromOverride
                && !p.MaxCopyContactsFromOverride);

        Resolver.Resolve(tenant, new AssignmentPolicyFields { MaxCopyContacts = 1 })
            .Should().Match<EffectiveAssignmentPolicy>(p =>
                !p.SignatureRequirementFromOverride
                && !p.RequiresApprovalBeforePublishFromOverride
                && !p.MaxPrimaryContactsFromOverride
                && p.MaxCopyContactsFromOverride);
    }
}
