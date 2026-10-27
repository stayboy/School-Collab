namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// The eligibility answer for one (grade, subject) pair — the payload of
/// <c>GET /students/topic-enrollment</c> (subject-enrollment-requirement-flag.md FR-ER-14,
/// shape amended by round 13 P4/P7).
/// <para><see cref="Audience"/> is the grade's roster for the active academic year: the
/// students the subject's grade contains. <see cref="Eligible"/> is the students who are
/// actually enrolled per §3 — equal to <see cref="Audience"/> for a <c>false</c> subject
/// (membership is the enrolment), the row-backed subset for a <c>true</c> subject.</para>
/// <para>Both arrays are ids only: no Students domain or DTO type crosses the boundary
/// (FR-ER-14a). The publish filter needs <see cref="Audience"/> as well as
/// <see cref="Eligible"/> because it must exclude exactly <c>Audience \ Eligible</c> while
/// leaving students who arrived on a non-grade targeting leg untouched — one flat id array
/// cannot express that difference.</para>
/// </summary>
public sealed record TopicEnrollmentDto(Guid[] Audience, Guid[] Eligible);
