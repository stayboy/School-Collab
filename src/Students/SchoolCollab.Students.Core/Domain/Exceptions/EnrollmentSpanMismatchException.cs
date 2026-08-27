namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when an <see cref="ActivityGroup"/>'s enrollment span is incompatible
/// with the membership being added (spec activity-group-enrollment.md FR-43/47):
/// e.g. a DateRange group requires a null PeriodId, or a period-aligned span
/// requires a matching PeriodId. Maps to HTTP 422.
/// </summary>
public sealed class EnrollmentSpanMismatchException : Exception
{
    public Guid ActivityGroupId { get; }
    public string Span { get; }

    public EnrollmentSpanMismatchException(Guid activityGroupId, string span, string message)
        : base(message)
    {
        ActivityGroupId = activityGroupId;
        Span = span;
    }
}