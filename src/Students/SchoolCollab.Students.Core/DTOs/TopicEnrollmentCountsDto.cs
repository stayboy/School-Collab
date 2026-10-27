namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// The FR-ER-8 reconciliation triple returned by the <c>RequiresEnrollment</c> switch
/// (subject-enrollment-requirement-flag.md §4). The counts read the audience state
/// <b>at the moment of the switch, before the flip is applied</b> — that is the state the
/// switch's confirm copy describes:
/// <list type="bullet">
/// <item><see cref="AudienceCount"/> — the grade's roster for the active academic year.</item>
/// <item><see cref="EnrolledCount"/> — how many of them already held an enrolment row.</item>
/// <item><see cref="UnenrolledCount"/> — how many did not: the set a <c>false → true</c>
/// flip grandfathers (FR-ER-7/10), and the set left with no row on a <c>true → false</c>
/// flip (FR-ER-9).</item>
/// </list>
/// <para><see cref="RequiresEnrollment"/> is the flag's resulting value.</para>
/// </summary>
public sealed record TopicEnrollmentCountsDto(
    Guid AssignmentId,
    bool RequiresEnrollment,
    int AudienceCount,
    int EnrolledCount,
    int UnenrolledCount);
