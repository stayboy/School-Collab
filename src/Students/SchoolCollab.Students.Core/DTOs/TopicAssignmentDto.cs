namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// A topic assignment row targets exactly one audience — a grade level or an
/// activity group. <c>Audience</c> is one of <c>"grade"</c> / <c>"activity_group"</c>
/// (mirroring the DB discriminator) and matches the populated
/// <c>GradeLevelId</c>/<c>ActivityGroupId</c>; the other accessor is null.
/// <para><c>RequiresEnrollment</c> is the bridge row's own flag
/// (subject-enrollment-requirement-flag.md §2): <c>false</c> enrols every member of the
/// grade by implication, <c>true</c> enrols only students holding a
/// <c>StudentTopicAssignment</c> for the active academic year.</para>
/// </summary>
public sealed record TopicAssignmentDto(
    Guid Id,
    string Audience,
    Guid? GradeLevelId,
    Guid? ActivityGroupId,
    Guid TopicId,
    DateOnly StartDate,
    DateOnly? EndDate,
    Guid? TopicStrandId,
    Guid? PeriodId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool RequiresEnrollment);

