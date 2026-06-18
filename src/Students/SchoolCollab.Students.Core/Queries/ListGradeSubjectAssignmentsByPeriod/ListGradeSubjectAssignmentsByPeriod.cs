using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.ListGradeSubjectAssignmentsByPeriod;

public sealed record ListGradeSubjectAssignmentsByPeriod(Guid PeriodId) : IQuery<GradeSubjectAssignmentDto[]>;