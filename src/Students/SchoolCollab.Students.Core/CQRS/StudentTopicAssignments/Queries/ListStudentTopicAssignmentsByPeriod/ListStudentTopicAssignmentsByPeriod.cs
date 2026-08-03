using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Queries.ListStudentTopicAssignmentsByPeriod;

public sealed record ListStudentTopicAssignmentsByPeriod(Guid PeriodId) : IQuery<StudentTopicAssignmentDto[]>;