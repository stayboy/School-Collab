using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.GetTopicEnrollment;

/// <summary>
/// Resolves the enrolment audience/eligible pair for one (grade, subject) pair — the
/// publish filter's single hop (subject-enrollment-requirement-flag.md FR-ER-14, round 13
/// P4). There is deliberately <b>no period parameter</b>: the academic year is resolved
/// server-side from the active period (FR-ER-3), so a caller can never choose the year
/// that eligibility is evaluated against.
/// </summary>
public sealed record GetTopicEnrollment(Guid GradeLevelId, Guid TopicId) : IQuery<TopicEnrollmentDto>;
