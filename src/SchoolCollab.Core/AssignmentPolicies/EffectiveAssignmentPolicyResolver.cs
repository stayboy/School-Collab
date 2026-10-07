namespace SchoolCollab.Core.AssignmentPolicies;

/// <summary>
/// Default merge implementation: a non-null grade override field wins; otherwise the tenant
/// default value is used. The <c>*FromOverride</c> flag reports per field whether the resolved
/// value came from the grade override (true) or was inherited / unset (false). Mirrors
/// <see cref="Notifications.EffectiveNotificationPolicyResolver"/>.
///
/// <para><b>D4 implication.</b> A resolved signature requirement that is not
/// <see cref="SignatureRequirementMode.Disabled"/> pins <see cref="EffectiveAssignmentPolicy.MandatoryReview"/>
/// to <see langword="true"/>, whatever the policy levels say (including "unset"); otherwise the
/// merged nullable review value flows through, so a policy that leaves review unset resolves to
/// <see langword="null"/> (the author chooses).</para>
/// </summary>
public sealed class EffectiveAssignmentPolicyResolver : IEffectiveAssignmentPolicyResolver
{
    public EffectiveAssignmentPolicy Resolve(
        AssignmentPolicyFields? tenantDefault,
        AssignmentPolicyFields? gradeOverride)
    {
        var tenant = tenantDefault ?? AssignmentPolicyFields.Empty;

        var signatureRequirement = gradeOverride?.SignatureRequirement
            ?? tenant.SignatureRequirement
            ?? SignatureRequirementMode.Disabled;

        // D4: Mandatory AND Optional both require a signature (OD5), so both imply guardian review.
        var requiresSignature = signatureRequirement != SignatureRequirementMode.Disabled;
        var policyReview = gradeOverride?.MandatoryReview ?? tenant.MandatoryReview;

        return new EffectiveAssignmentPolicy(
            SignatureRequirement: signatureRequirement,
            RequiresApprovalBeforePublish: gradeOverride?.RequiresApprovalBeforePublish
                ?? tenant.RequiresApprovalBeforePublish
                ?? false,
            MaxPrimaryContacts: gradeOverride?.MaxPrimaryContacts ?? tenant.MaxPrimaryContacts,
            MaxCopyContacts: gradeOverride?.MaxCopyContacts ?? tenant.MaxCopyContacts,
            MandatoryReview: requiresSignature ? true : policyReview,
            ArchiveGraceDays: gradeOverride?.ArchiveGraceDays ?? tenant.ArchiveGraceDays,
            SignatureRequirementFromOverride: gradeOverride?.SignatureRequirement is not null,
            RequiresApprovalBeforePublishFromOverride: gradeOverride?.RequiresApprovalBeforePublish is not null,
            MaxPrimaryContactsFromOverride: gradeOverride?.MaxPrimaryContacts is not null,
            MaxCopyContactsFromOverride: gradeOverride?.MaxCopyContacts is not null,
            MandatoryReviewFromOverride: gradeOverride?.MandatoryReview is not null,
            ArchiveGraceDaysFromOverride: gradeOverride?.ArchiveGraceDays is not null);
    }
}
