using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.ListStudentSubjectAssignmentsByPeriod;

public sealed record ListStudentSubjectAssignmentsByPeriod(Guid PeriodId) : IQuery<StudentSubjectAssignmentDto[]>;