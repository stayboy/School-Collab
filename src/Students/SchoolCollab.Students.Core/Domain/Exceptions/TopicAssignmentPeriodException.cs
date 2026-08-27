namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when a topic assignment's <see cref="TopicAssignment.PeriodId"/>
/// violates the Rev. 6 period rules (spec activity-group-enrollment.md FR-56/57,
/// EC-23/24): a grade-owned topic's period must be an AcademicYear or a
/// Term/Semester within the active academic year; an activity-group-owned
/// topic's period must match the group's enrollment span (Termly→Term,
/// Semester→Semester, WholeAcademicYear→AcademicYear; OpenEnded/DateRange→null).
/// Maps to HTTP 422.
/// </summary>
public sealed class TopicAssignmentPeriodException : Exception
{
    public Guid? PeriodId { get; }

    public TopicAssignmentPeriodException(string message, Guid? periodId = null)
        : base(message)
        => PeriodId = periodId;
}