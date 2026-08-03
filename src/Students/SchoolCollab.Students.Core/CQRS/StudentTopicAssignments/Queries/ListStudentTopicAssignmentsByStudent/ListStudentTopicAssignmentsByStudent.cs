using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Queries.ListStudentTopicAssignmentsByStudent;

public sealed record ListStudentTopicAssignmentsByStudent(Guid StudentId, Guid PeriodId) : IQuery<StudentTopicAssignmentDto[]>;