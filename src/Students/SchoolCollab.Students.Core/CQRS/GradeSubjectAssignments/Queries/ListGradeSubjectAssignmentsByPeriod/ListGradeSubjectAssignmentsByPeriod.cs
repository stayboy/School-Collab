using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeSubjectAssignments.Queries.ListGradeSubjectAssignmentsByPeriod;

public sealed record ListGradeSubjectAssignmentsByPeriod(Guid PeriodId) : IQuery<GradeSubjectAssignmentDto[]>;