using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.AssignmentPolicies.Commands.UpsertTenantAssignmentPolicy;

/// <summary>
/// Creates or replaces the current tenant's default assignment policy
/// (<c>documents/solution/assignment-policy-fields.md</c> §4). Every field is nullable — null
/// stores "unset" (the built-in default then applies at resolution time). The returned
/// <see cref="DTOs.TenantAssignmentPolicyDto"/> is the persisted policy.
/// </summary>
public sealed record UpsertTenantAssignmentPolicy(
    SignatureRequirementMode? SignatureRequirement,
    bool? RequiresApprovalBeforePublish,
    int? MaxPrimaryContacts,
    int? MaxCopyContacts,
    bool? MandatoryReview = null,
    int? ArchiveGraceDays = null) : ICommand;
