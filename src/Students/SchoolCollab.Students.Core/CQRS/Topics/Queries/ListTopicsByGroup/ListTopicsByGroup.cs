using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Queries.ListTopicsByGroup;

/// <summary>
/// Returns all subjects assigned to an activity group. If <c>effectiveDate</c> is
/// omitted, today is used — the group's currently-effective topics.
/// </summary>
public sealed record ListTopicsByGroup(
    Guid ActivityGroupId,
    DateOnly? EffectiveDate = null) : IQuery<TopicDto[]>;
