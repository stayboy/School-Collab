using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.GradeAssignmentPolicies.Commands.UpsertGradeAssignmentPolicy;

/// <summary>
/// Creates or replaces the assignment-policy override row for a grade. A null field restores
/// "inherit the tenant default" (WS-C1 / spec §7 Q1;
/// <c>documents/solution/assignment-policy-fields.md</c> §4).
/// </summary>
public sealed record UpsertGradeAssignmentPolicy(
    Guid GradeLevelId,
    SignatureRequirementMode? SignatureRequirement,
    bool? RequiresApprovalBeforePublish,
    int? MaxPrimaryContacts,
    int? MaxCopyContacts,
    bool? MandatoryReview = null,
    int? ArchiveGraceDays = null) : ICommand;
