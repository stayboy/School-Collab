using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when a subject enrollment exception already exists for the same
/// <c>(owner, topic, division, span)</c> (subject-period-exception-model.md v3 §2.2).
/// Mirrors <see cref="DuplicateTopicAssignmentException"/> and maps to HTTP 409
/// Conflict. The COALESCE expression unique indexes the migration creates in raw SQL
/// forbid a second row (including two open-ended rows with the same start, which a
/// plain nullable-column index would let through), so this exception is the 409 the
/// caller gets instead of a raw <c>23505</c>.
/// </summary>
public sealed class DuplicateSubjectEnrollmentException : Exception
{
    public Guid? GradeLevelId { get; }
    public Guid? ActivityGroupId { get; }
    public Guid TopicId { get; }
    public AcademicYearDivision Division { get; }
    public DateOnly? StartDate { get; }
    public DateOnly? EndDate { get; }

    public DuplicateSubjectEnrollmentException(
        Guid? gradeLevelId,
        Guid? activityGroupId,
        Guid topicId,
        AcademicYearDivision division,
        DateOnly? startDate,
        DateOnly? endDate)
        : base($"A subject enrollment exception already exists for "
            + (gradeLevelId is { } g ? $"grade '{g}'" : $"activity group '{activityGroupId}'")
            + $", topic '{topicId}', {division} and span {DescribeSpan(startDate, endDate)}.")
    {
        GradeLevelId = gradeLevelId;
        ActivityGroupId = activityGroupId;
        TopicId = topicId;
        Division = division;
        StartDate = startDate;
        EndDate = endDate;
    }

    /// <summary>The span in words — an open bound is named as such, never printed as a date.</summary>
    private static string DescribeSpan(DateOnly? startDate, DateOnly? endDate) => (startDate, endDate) switch
    {
        ({ } s, { } e) => $"'{s:yyyy-MM-dd}' to '{e:yyyy-MM-dd}'",
        ({ } s, null) => $"from '{s:yyyy-MM-dd}'",
        (null, { } e) => $"up to '{e:yyyy-MM-dd}'",
        _ => "unbounded",
    };
}
