using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Settings.Api.Endpoints;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Round A (<c>documents/solution/assignment-policy-fields.md</c> §4/§5, plan IN item 8) — the
/// tenant assignment-policy PUT body's field mapping. The new field set is stored as given (null =
/// unset), and the pre-widening input-only boolean the shipped Admin editor still sends is mapped
/// per D8 (<c>true → Optional</c>, <c>false → Disabled</c>, null → unset), with a supplied
/// <c>SignatureRequirement</c> always winning. Round B deletes the legacy member.
/// </summary>
[TestClass]
public class UpsertAssignmentPolicyRequestTests
{
    private static SignatureRequirementMode? Resolve(
        SignatureRequirementMode? signatureRequirement, bool? legacyRequiresSignatureDefault) =>
        new AssignmentPolicyRoutes.UpsertAssignmentPolicyRequest(
            SignatureRequirement: signatureRequirement,
            RequiresApprovalBeforePublish: null,
            MaxPrimaryContacts: null,
            MaxCopyContacts: null,
            MandatoryReview: null,
            ArchiveGraceDays: null,
            RequiresSignatureDefault: legacyRequiresSignatureDefault)
            .ResolveSignatureRequirement();

    [TestMethod]
    public void NewFieldSet_IsPassedThroughUnchanged()
    {
        Resolve(SignatureRequirementMode.Mandatory, null).Should().Be(SignatureRequirementMode.Mandatory);
        Resolve(SignatureRequirementMode.Optional, null).Should().Be(SignatureRequirementMode.Optional);
        Resolve(SignatureRequirementMode.Disabled, null).Should().Be(SignatureRequirementMode.Disabled);
        Resolve(null, null).Should().BeNull("null is 'unset' — the built-in default then applies");
    }

    [TestMethod]
    public void LegacyTrue_MapsToOptional()
    {
        Resolve(null, true).Should().Be(SignatureRequirementMode.Optional,
            "D8: the old 'required by default' becomes the pre-filled-but-changeable default");
    }

    [TestMethod]
    public void LegacyFalse_MapsToDisabled()
    {
        Resolve(null, false).Should().Be(SignatureRequirementMode.Disabled,
            "D8: the old default-false is 'never required'");
    }

    [TestMethod]
    public void NewFieldWins_OverTheLegacyBoolean()
    {
        Resolve(SignatureRequirementMode.Mandatory, false).Should().Be(SignatureRequirementMode.Mandatory,
            "the legacy member is input-only compatibility; a supplied new field always wins");
        Resolve(SignatureRequirementMode.Disabled, true).Should().Be(SignatureRequirementMode.Disabled);
    }
}
