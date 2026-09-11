using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeAssignmentPolicies.Queries.GetGradeAssignmentPolicy;

/// <summary>
/// Returns the grade's guardian-signature override row, or <see langword="null"/>
/// when the grade inherits the tenant default (WS-C1 / spec §7 Q1).
/// </summary>
public sealed record GetGradeAssignmentPolicy(Guid GradeLevelId) : IQuery<GradeAssignmentPolicyDto?>;
