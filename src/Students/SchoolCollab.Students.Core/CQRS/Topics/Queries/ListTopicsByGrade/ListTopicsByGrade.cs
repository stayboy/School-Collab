using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Queries.ListTopicsByGrade;

/// <summary>
/// Returns all subjects assigned to a grade level. If <c>effectiveDate</c> is
/// omitted, today is used — the grade's currently-effective topics.
///
/// <para>There is deliberately no period filter (subject-period-exception-model.md v3
/// §8 Q5): the bridge row carries no period meaning, so a filter would be dead by
/// construction. This was the last period-aware read path.</para>
/// </summary>
public sealed record ListTopicsByGrade(
    Guid GradeLevelId,
    DateOnly? EffectiveDate = null) : IQuery<TopicDto[]>;
