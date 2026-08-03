using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListActivityGroupTopicAssignments;

public sealed record ListActivityGroupTopicAssignments(Guid ActivityGroupId, DateOnly EffectiveDate) : IQuery<TopicAssignmentDto[]>;
