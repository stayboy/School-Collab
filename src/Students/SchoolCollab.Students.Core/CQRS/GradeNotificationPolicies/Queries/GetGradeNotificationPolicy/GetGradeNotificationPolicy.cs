using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeNotificationPolicies.Queries.GetGradeNotificationPolicy;

/// <summary>
/// Returns the current tenant's override policy for a grade, or <see langword="null"/>
/// when the grade has no override (all fields inherit the tenant default).
/// </summary>
public sealed record GetGradeNotificationPolicy(Guid GradeLevelId)
    : IQuery<GradeNotificationPolicyDto?>;
