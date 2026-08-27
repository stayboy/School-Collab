namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when an <see cref="ActivityGroup"/>'s <see cref="EnrollmentSpan"/> is
/// incompatible with the tenant's academic-year division (spec
/// activity-group-enrollment.md FR-45): a <c>Termly</c> span requires a terms
/// framework; a <c>Semester</c> span requires a semesters framework.
/// <c>WholeAcademicYear</c>/<c>OpenEnded</c>/<c>DateRange</c> are framework-agnostic.
/// Maps to HTTP 422.
/// </summary>
public sealed class EnrollmentSpanIncompatibleException : Exception
{
    public Guid ActivityGroupId { get; }
    public string Span { get; }
    public string Division { get; }

    public EnrollmentSpanIncompatibleException(string span, string division)
        : base($"A {span} activity group requires the academic-year division '{division}'.")
    {
        Span = span;
        Division = division;
    }
}