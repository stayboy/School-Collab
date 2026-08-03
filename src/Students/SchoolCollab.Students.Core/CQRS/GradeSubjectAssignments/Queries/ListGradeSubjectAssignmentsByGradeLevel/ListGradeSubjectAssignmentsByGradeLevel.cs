using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeSubjectAssignments.Queries.ListGradeSubjectAssignmentsByGradeLevel;

public sealed record ListGradeSubjectAssignmentsByGradeLevel(Guid GradeLevelId, DateOnly EffectiveDate) : IQuery<GradeSubjectAssignmentDto[]>;