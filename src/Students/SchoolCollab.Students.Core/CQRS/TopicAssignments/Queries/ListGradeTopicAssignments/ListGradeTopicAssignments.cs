using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListGradeTopicAssignments;

public sealed record ListGradeTopicAssignments(Guid GradeLevelId, DateOnly EffectiveDate) : IQuery<TopicAssignmentDto[]>;
