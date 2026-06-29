using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.StudentSubjectAssignments.Queries.ListStudentSubjectAssignmentsByPeriod;

public sealed record ListStudentSubjectAssignmentsByPeriod(Guid PeriodId) : IQuery<StudentSubjectAssignmentDto[]>;