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
///
/// <para>Round <c>assignment-rules-policy-rework</c> (AC1/AC2) extends the same suite with the
/// guardian-review and archive-window fields added to the shared set (D3/D5) and the D4 implication
/// the resolver derives from the signature requirement.</para>
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
        resolved.MandatoryReview.Should().BeNull(
            "nothing configured leaves the guardian review to the author (the write seam falls back to true)");
        resolved.ArchiveGraceDays.Should().BeNull(
            "nothing configured leaves the built-in 30-day archive window to the write seam");
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
        resolved.MandatoryReviewFromOverride.Should().BeFalse();
        resolved.ArchiveGraceDaysFromOverride.Should().BeFalse();
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

    /// <summary>
    /// AC1 (round <c>assignment-rules-policy-rework</c>, D5/D7): the two fields added to the shared set
    /// merge exactly like the pre-existing four — a null grade field inherits the tenant default, a
    /// non-null one overrides it, and each <c>*FromOverride</c> flag follows its own field.
    /// </summary>
    [TestMethod]
    public void Resolve_ReviewAndArchiveFields_MergePerFieldWithTheirOwnOverrideFlags()
    {
        // Arrange — the tenant sets the archive window, the grade sets the review flag.
        var tenant = new AssignmentPolicyFields { ArchiveGraceDays = 42 };
        var grade = new AssignmentPolicyFields { MandatoryReview = true };

        // Act
        var resolved = Resolver.Resolve(tenant, grade);

        // Assert
        resolved.ArchiveGraceDays.Should().Be(42, "no grade override, so the tenant default applies");
        resolved.ArchiveGraceDaysFromOverride.Should().BeFalse();
        resolved.MandatoryReview.Should().BeTrue("the grade override sets it");
        resolved.MandatoryReviewFromOverride.Should().BeTrue();
    }

    /// <summary>
    /// AC2 (D4 + OD2/OD5): the implication matrix. A signature requirement that is Optional or
    /// Mandatory pins guardian review ON; Disabled leaves the merged nullable policy value alone —
    /// including the unset case, where the author (and the write seam's <c>true</c> fallback) decides.
    /// </summary>
    [TestMethod]
    public void Resolve_SignatureRequirement_DerivesMandatoryReview_AcrossTheWholeMatrix()
    {
        // Mandatory signature, no policy review value ⇒ review is implied ON.
        Resolver.Resolve(new AssignmentPolicyFields { SignatureRequirement = SignatureRequirementMode.Mandatory }, null)
            .MandatoryReview.Should().BeTrue();

        // Disabled signature, policy review explicitly true ⇒ the policy value stands.
        Resolver.Resolve(new AssignmentPolicyFields { MandatoryReview = true }, null)
            .MandatoryReview.Should().BeTrue();

        // OD5: Optional also requires a signature, so it feeds the same implication.
        Resolver.Resolve(new AssignmentPolicyFields { SignatureRequirement = SignatureRequirementMode.Optional }, null)
            .MandatoryReview.Should().BeTrue();

        // Disabled signature, nothing set ⇒ unset (OD2): the author chooses, the seam falls back to true.
        Resolver.Resolve(new AssignmentPolicyFields { SignatureRequirement = SignatureRequirementMode.Disabled }, null)
            .MandatoryReview.Should().BeNull();

        // D4 beats an explicit false policy review too: a signature-required assignment always
        // requires review, so the domain backstop (Assignment.Create) can never be tripped by
        // the resolved pair.
        Resolver.Resolve(new AssignmentPolicyFields { MandatoryReview = false }, null)
            .MandatoryReview.Should().BeFalse("with no signature requirement the policy's false stands");
        Resolver.Resolve(
                new AssignmentPolicyFields { SignatureRequirement = SignatureRequirementMode.Mandatory, MandatoryReview = false },
                null)
            .MandatoryReview.Should().BeTrue("a signature requirement overrides an explicit false review policy");
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
                && !p.MaxCopyContactsFromOverride
                && !p.MandatoryReviewFromOverride
                && !p.ArchiveGraceDaysFromOverride);

        Resolver.Resolve(tenant, new AssignmentPolicyFields { RequiresApprovalBeforePublish = false })
            .Should().Match<EffectiveAssignmentPolicy>(p =>
                !p.SignatureRequirementFromOverride
                && p.RequiresApprovalBeforePublishFromOverride
                && !p.MaxPrimaryContactsFromOverride
                && !p.MaxCopyContactsFromOverride
                && !p.MandatoryReviewFromOverride
                && !p.ArchiveGraceDaysFromOverride);

        Resolver.Resolve(tenant, new AssignmentPolicyFields { MaxPrimaryContacts = 1 })
            .Should().Match<EffectiveAssignmentPolicy>(p =>
                !p.SignatureRequirementFromOverride
                && !p.RequiresApprovalBeforePublishFromOverride
                && p.MaxPrimaryContactsFromOverride
                && !p.MaxCopyContactsFromOverride
                && !p.MandatoryReviewFromOverride
                && !p.ArchiveGraceDaysFromOverride);

        Resolver.Resolve(tenant, new AssignmentPolicyFields { MaxCopyContacts = 1 })
            .Should().Match<EffectiveAssignmentPolicy>(p =>
                !p.SignatureRequirementFromOverride
                && !p.RequiresApprovalBeforePublishFromOverride
                && !p.MaxPrimaryContactsFromOverride
                && p.MaxCopyContactsFromOverride
                && !p.MandatoryReviewFromOverride
                && !p.ArchiveGraceDaysFromOverride);

        Resolver.Resolve(tenant, new AssignmentPolicyFields { MandatoryReview = true })
            .Should().Match<EffectiveAssignmentPolicy>(p =>
                !p.SignatureRequirementFromOverride
                && !p.RequiresApprovalBeforePublishFromOverride
                && !p.MaxPrimaryContactsFromOverride
                && !p.MaxCopyContactsFromOverride
                && p.MandatoryReviewFromOverride
                && !p.ArchiveGraceDaysFromOverride);

        Resolver.Resolve(tenant, new AssignmentPolicyFields { ArchiveGraceDays = 7 })
            .Should().Match<EffectiveAssignmentPolicy>(p =>
                !p.SignatureRequirementFromOverride
                && !p.RequiresApprovalBeforePublishFromOverride
                && !p.MaxPrimaryContactsFromOverride
                && !p.MaxCopyContactsFromOverride
                && !p.MandatoryReviewFromOverride
                && p.ArchiveGraceDaysFromOverride);
    }
}
