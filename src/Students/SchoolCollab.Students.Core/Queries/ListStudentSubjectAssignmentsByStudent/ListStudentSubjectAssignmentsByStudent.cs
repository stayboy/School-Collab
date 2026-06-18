using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.ListStudentSubjectAssignmentsByStudent;

public sealed record ListStudentSubjectAssignmentsByStudent(Guid StudentId, Guid PeriodId) : IQuery<StudentSubjectAssignmentDto[]>;