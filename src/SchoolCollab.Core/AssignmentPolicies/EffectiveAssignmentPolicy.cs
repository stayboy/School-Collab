namespace SchoolCollab.Core.AssignmentPolicies;

/// <summary>
/// The <b>resolved</b> (merged) assignment policy for a grade: each field's effective value
/// plus a per-field flag reporting whether that value came from the grade override (true) or
/// was inherited from the tenant default / left unset (false) — the flag drives the Round B
/// "Grade override" vs "Inherit global" badge. Computed by
/// <see cref="IEffectiveAssignmentPolicyResolver"/>. Mirrors
/// <see cref="Notifications.EffectiveNotificationPolicy"/>.
///
/// <para>Built-in defaults (no tenant value, no override): <see cref="SignatureRequirement"/>
/// <see cref="SignatureRequirementMode.Disabled"/> (the old <c>false</c>),
/// <see cref="RequiresApprovalBeforePublish"/> <see langword="false"/>, and both contact caps
/// <see langword="null"/> (uncapped).</para>
/// </summary>
public sealed record EffectiveAssignmentPolicy(
    SignatureRequirementMode SignatureRequirement,
    bool RequiresApprovalBeforePublish,
    int? MaxPrimaryContacts,
    int? MaxCopyContacts,
    bool SignatureRequirementFromOverride,
    bool RequiresApprovalBeforePublishFromOverride,
    bool MaxPrimaryContactsFromOverride,
    bool MaxCopyContactsFromOverride);
