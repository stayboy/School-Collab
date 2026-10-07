using SchoolCollab.Core.AssignmentPolicies;

namespace SchoolCollab.Settings.Core.DTOs;

/// <summary>
/// Wire shape for a <see cref="Domain.TenantAssignmentPolicy" /> (the global assignment-policy
/// default for a tenant — assignment-request-implementation-details.md WS-C1 /
/// <c>documents/solution/assignment-policy-fields.md</c> §4). Every field is nullable: null means
/// the tenant has not set that field (the built-in default applies). A 204/absent result means no
/// policy row exists yet — callers fall back to the built-in defaults.
/// </summary>
public sealed record TenantAssignmentPolicyDto(
    SignatureRequirementMode? SignatureRequirement,
    bool? RequiresApprovalBeforePublish,
    int? MaxPrimaryContacts,
    int? MaxCopyContacts,
    bool? MandatoryReview = null,
    int? ArchiveGraceDays = null);
