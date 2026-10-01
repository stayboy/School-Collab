namespace SchoolCollab.Core.AssignmentPolicies;

/// <summary>
/// Default merge implementation: a non-null grade override field wins; otherwise the tenant
/// default value is used. The <c>*FromOverride</c> flag reports per field whether the resolved
/// value came from the grade override (true) or was inherited / unset (false). Mirrors
/// <see cref="Notifications.EffectiveNotificationPolicyResolver"/>.
/// </summary>
public sealed class EffectiveAssignmentPolicyResolver : IEffectiveAssignmentPolicyResolver
{
    public EffectiveAssignmentPolicy Resolve(
        AssignmentPolicyFields? tenantDefault,
        AssignmentPolicyFields? gradeOverride)
    {
        var tenant = tenantDefault ?? AssignmentPolicyFields.Empty;

        return new EffectiveAssignmentPolicy(
            SignatureRequirement: gradeOverride?.SignatureRequirement
                ?? tenant.SignatureRequirement
                ?? SignatureRequirementMode.Disabled,
            RequiresApprovalBeforePublish: gradeOverride?.RequiresApprovalBeforePublish
                ?? tenant.RequiresApprovalBeforePublish
                ?? false,
            MaxPrimaryContacts: gradeOverride?.MaxPrimaryContacts ?? tenant.MaxPrimaryContacts,
            MaxCopyContacts: gradeOverride?.MaxCopyContacts ?? tenant.MaxCopyContacts,
            SignatureRequirementFromOverride: gradeOverride?.SignatureRequirement is not null,
            RequiresApprovalBeforePublishFromOverride: gradeOverride?.RequiresApprovalBeforePublish is not null,
            MaxPrimaryContactsFromOverride: gradeOverride?.MaxPrimaryContacts is not null,
            MaxCopyContactsFromOverride: gradeOverride?.MaxCopyContacts is not null);
    }
}
