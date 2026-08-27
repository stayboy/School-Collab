using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Queries.ListTopicsByGrade;

/// <summary>
/// Returns all subjects assigned to a grade level. If <c>effectiveDate</c> is
/// omitted, today is used — the grade's currently-effective topics.
/// </summary>
public sealed record ListTopicsByGrade(
    Guid GradeLevelId,
    DateOnly? EffectiveDate = null,
    Guid? PeriodId = null) : IQuery<TopicDto[]>;