namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when a new enrollment targets an <see cref="ActivityGroup"/> whose
/// enrollment window has closed (spec activity-group-enrollment.md FR-52): the
/// group's <see cref="ActivityGroup.EnrollmentEndDate"/> (for a
/// <see cref="EnrollmentSpan.DateRange"/> span) has passed. New enrollments
/// attach to the current or next open window only. Maps to HTTP 422.
/// </summary>
public sealed class EnrollmentWindowClosedException : Exception
{
    public Guid ActivityGroupId { get; }
    public DateOnly? WindowEnd { get; }

    public EnrollmentWindowClosedException(Guid activityGroupId, DateOnly? windowEnd)
        : base($"Activity group '{activityGroupId}' enrollment window has closed (ended {windowEnd:O}); " +
               "no new enrollments until the next window opens.")
    {
        ActivityGroupId = activityGroupId;
        WindowEnd = windowEnd;
    }
}