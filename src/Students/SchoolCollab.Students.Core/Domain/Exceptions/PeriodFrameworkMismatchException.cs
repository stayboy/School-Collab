namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when creating or updating a <c>Term</c>/<c>Semester</c> sub-period whose
/// division does not match its parent academic year's division
/// (plan-drop-periodtype.md — a sub-period must share its parent's division). The
/// API maps this to <c>422 Unprocessable Entity</c>.
/// </summary>
public sealed class PeriodFrameworkMismatchException : Exception
{
    public string Division { get; }
    public string ParentDivision { get; }

    public PeriodFrameworkMismatchException(string division, string parentDivision)
        : base($"Cannot create a {division} sub-period: the parent academic year's division is '{parentDivision}' " +
               $"(a sub-period must share its parent's division).")
    {
        Division = division;
        ParentDivision = parentDivision;
    }

    /// <summary>
    /// Message-only overload for the division-change rejection (plan-drop-periodtype.md):
    /// changing a top-level year's division while non-completed sub-periods exist.
    /// </summary>
    public PeriodFrameworkMismatchException(string message)
        : base(message)
    {
        Division = "AcademicYear";
        ParentDivision = "";
    }
}