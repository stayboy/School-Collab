using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignActivityGroupTopic;

/// <summary>
/// Assigns a topic (subject) to an activity group on the bridge.
/// </summary>
/// <remarks>
/// <b>DEPRECATED (2026-09-26)</b> — subject-period-exception-model.md.
/// <paramref name="PeriodId"/> is kept on the contract for wire compatibility,
/// <b>accepted and ignored</b>: whitelist semantics are retired for group-owned
/// topics exactly as for grade-owned ones, and "not offered in period P" is
/// expressed by a <c>SubjectEnrollmentException</c> instead. The handler no longer
/// validates or persists it.
/// </remarks>
public sealed record AssignActivityGroupTopic(
    Guid ActivityGroupId,
    Guid TopicId,
    DateOnly StartDate,
    DateOnly? EndDate = null,
    Guid? TopicStrandId = null,
    Guid? PeriodId = null) : ICommand;
