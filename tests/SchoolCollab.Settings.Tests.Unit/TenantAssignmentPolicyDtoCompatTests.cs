using System.Text.Json;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.AssignmentPolicies;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Round A (plan R2) — the Admin.Shared wire mirror's <b>derived</b>
/// <see cref="TenantAssignmentPolicyDto.RequiresSignatureDefault"/>. The shipped
/// <c>GradeSignaturePolicyEditor</c> reads that boolean, and Round A may not touch a UI file, so the
/// mirror must keep producing the pre-widening answer from the widened payload the Settings API now
/// sends (<c>signatureRequirement</c> is on the wire; the compat member is
/// <c>[JsonIgnore]</c>-marked and recomputed on read).
///
/// <para>The derivation is deliberately not "SignatureRequirement != Disabled": an existing row
/// whose field is unset (null) is the pre-widening <c>false</c> — "no signature required" — not
/// <c>true</c>.</para>
/// </summary>
[TestClass]
public class TenantAssignmentPolicyDtoCompatTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static TenantAssignmentPolicyDto Read(string json) =>
        JsonSerializer.Deserialize<TenantAssignmentPolicyDto>(json, JsonOptions)!;

    [TestMethod]
    public void OptionalOrMandatory_DerivesTrue()
    {
        Read("""{"signatureRequirement":"Optional","maxPrimaryContacts":2}""")
            .RequiresSignatureDefault.Should().BeTrue();
        Read("""{"signatureRequirement":"Mandatory"}""")
            .RequiresSignatureDefault.Should().BeTrue();
    }

    [TestMethod]
    public void Disabled_DerivesFalse()
    {
        Read("""{"signatureRequirement":"Disabled","requiresApprovalBeforePublish":true}""")
            .RequiresSignatureDefault.Should().BeFalse();
    }

    [TestMethod]
    public void UnsetSignatureRequirement_DerivesFalse_LikeThePreWideningDefault()
    {
        Read("""{"maxCopyContacts":4}""")
            .RequiresSignatureDefault.Should().BeFalse(
                "an unset field is the old default-false; reading it as true would flip the editor's switch on");
    }

    [TestMethod]
    public void WidenedPayload_BindsEveryNewField()
    {
        var dto = Read("""
            {"signatureRequirement":"Mandatory","requiresApprovalBeforePublish":true,
             "maxPrimaryContacts":1,"maxCopyContacts":2}
            """);

        dto.SignatureRequirement.Should().Be(SignatureRequirementMode.Mandatory);
        dto.RequiresApprovalBeforePublish.Should().BeTrue();
        dto.MaxPrimaryContacts.Should().Be(1);
        dto.MaxCopyContacts.Should().Be(2);
    }
}
