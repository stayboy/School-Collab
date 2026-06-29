using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.StudentSubjectAssignments.Queries.ListStudentSubjectAssignmentsByStudent;

public sealed record ListStudentSubjectAssignmentsByStudent(Guid StudentId, Guid PeriodId) : IQuery<StudentSubjectAssignmentDto[]>;