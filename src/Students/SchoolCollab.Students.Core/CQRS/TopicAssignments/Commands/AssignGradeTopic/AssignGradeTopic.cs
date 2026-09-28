using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignGradeTopic;

/// <summary>
/// Assigns a topic (subject) to a grade level on the bridge.
/// </summary>
/// <remarks>
/// <b>DEPRECATED (2026-09-26)</b> — subject-period-exception-model.md.
/// <paramref name="PeriodId"/> is kept on the contract for wire compatibility,
/// <b>accepted and ignored</b>: the bridge row carries no period meaning any more
/// (one row per (tenant, grade, topic) — see
/// <c>ix_topic_assignments_tenant_grade_topic_unique</c>), and "not offered in
/// period P" is expressed by a <c>SubjectEnrollmentException</c> instead. The handler
/// no longer validates or persists it.
/// </remarks>
public sealed record AssignGradeTopic(
    Guid GradeLevelId,
    Guid TopicId,
    DateOnly StartDate,
    DateOnly? EndDate = null,
    Guid? TopicStrandId = null,
    Guid? PeriodId = null) : ICommand;
