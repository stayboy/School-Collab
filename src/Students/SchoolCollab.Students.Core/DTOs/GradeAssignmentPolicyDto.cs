using System.Text.Json.Serialization;
using SchoolCollab.Core.AssignmentPolicies;

namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// Wire shape for a <see cref="Domain.GradeAssignmentPolicy"/> (the raw per-grade assignment-policy
/// override). Every field is nullable: null means "inherit the tenant default" (nothing stored for
/// this grade). The effective value is computed by the Assignments resolver fed with the tenant
/// default + this override (WS-C1 / spec §7 Q1;
/// <c>documents/solution/assignment-policy-fields.md</c> §4).
/// </summary>
public sealed record GradeAssignmentPolicyDto(
    Guid GradeLevelId,
    SignatureRequirementMode? SignatureRequirement,
    bool? RequiresApprovalBeforePublish,
    int? MaxPrimaryContacts,
    int? MaxCopyContacts,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// <b>Legacy-input compatibility (Round A only).</b> The pre-widening wire shape carried the
    /// boolean <c>requiresSignatureDefault</c>, and the shipped Admin editor
    /// (<c>GradeSignaturePolicyEditor</c>) still reads it. Derived from
    /// <see cref="SignatureRequirement"/> (<c>Disabled → false</c>, <c>Optional</c>/<c>Mandatory
    /// → true</c>, null → null) and never serialized — the wire carries the new field set only.
    /// Round B deletes this member together with the retired editor.
    /// </summary>
    [JsonIgnore]
    public bool? RequiresSignatureDefault => SignatureRequirement switch
    {
        null => null,
        SignatureRequirementMode.Disabled => false,
        _ => true,
    };
}
