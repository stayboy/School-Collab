namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when activating a top-level academic year whose
/// <see cref="AcademicYearDivision"/> is <c>Terms</c>/<c>Semesters</c> but which
/// has no <see cref="PeriodStatus.Draft"/> sub-period to attach Termly/Semester
/// activity-group memberships to (period-activation-guard-atomic-create.md FR-G1).
/// The guard is a hard, always-on invariant: the year must contain at least one
/// Draft sub-period before it can be activated. The API maps this to
/// <c>422 Unprocessable Entity</c> (FR-G5).
/// </summary>
public sealed class PeriodGuardException : Exception
{
    public PeriodGuardException(string message) : base(message) { }
}
