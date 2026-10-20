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
/// <see langword="null"/> (uncapped). <see cref="MandatoryReview"/>,
/// <see cref="ArchiveGraceDays"/> and <see cref="ApprovalWindowDays"/> stay <see langword="null"/>
/// when nothing is configured — an unset guardian review leaves the choice to the author (the write
/// seam falls back to <see langword="true"/>), an unset archive window leaves the built-in 30-day
/// retention floor to the write seam, and an unset approval window means "no deadline".</para>
///
/// <para><b>D4 implication.</b> <see cref="MandatoryReview"/> is not a plain merge: a
/// <see cref="SignatureRequirement"/> of <see cref="SignatureRequirementMode.Optional"/> or
/// <see cref="SignatureRequirementMode.Mandatory"/> implies it, so the resolver reports
/// <see langword="true"/> whenever a signature is required and the policy levels leave review
/// unset.</para>
/// </summary>
public sealed record EffectiveAssignmentPolicy(
    SignatureRequirementMode SignatureRequirement,
    bool RequiresApprovalBeforePublish,
    int? MaxPrimaryContacts,
    int? MaxCopyContacts,
    bool? MandatoryReview,
    int? ArchiveGraceDays,
    /// <summary>Spec §3.3, Q11 — the approval window in days; null = no deadline (the built-in
    /// default, so a fail-open resolve never invents one).</summary>
    int? ApprovalWindowDays,
    bool SignatureRequirementFromOverride,
    bool RequiresApprovalBeforePublishFromOverride,
    bool MaxPrimaryContactsFromOverride,
    bool MaxCopyContactsFromOverride,
    bool MandatoryReviewFromOverride,
    bool ArchiveGraceDaysFromOverride,
    bool ApprovalWindowDaysFromOverride);
