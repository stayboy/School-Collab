using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.GradeAssignmentPolicies.Commands.UpsertGradeAssignmentPolicy;

/// <summary>
/// Creates or replaces the guardian-signature override row for a grade. A null
/// <see cref="RequiresSignatureDefault"/> restores "inherit the tenant default"
/// (WS-C1 / spec §7 Q1).
/// </summary>
public sealed record UpsertGradeAssignmentPolicy(
    Guid GradeLevelId,
    bool? RequiresSignatureDefault) : ICommand;
