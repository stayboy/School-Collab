using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.ListGradeSubjectAssignmentsByGradeLevel;

public sealed record ListGradeSubjectAssignmentsByGradeLevel(Guid GradeLevelId, Guid PeriodId) : IQuery<GradeSubjectAssignmentDto[]>;