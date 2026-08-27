namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when creating or updating a <c>Term</c>/<c>Semester</c> period while
/// the tenant's academic-year division does not permit it
/// (period-hierarchy-terms-semesters.md FR-H7). A <c>Term</c> requires
/// <c>AcademicYearDivision = Terms</c>; a <c>Semester</c> requires
/// <c>Semesters</c>. <c>AcademicYear</c> creation is always framework-agnostic.
/// The API maps this to <c>422 Unprocessable Entity</c>.
/// </summary>
public sealed class PeriodFrameworkMismatchException : Exception
{
    public string PeriodType { get; }
    public string Division { get; }

    public PeriodFrameworkMismatchException(string periodType, string division)
        : base($"Cannot create a {periodType} period: the tenant's academic-year division is '{division}' " +
               $"(requires {(periodType == "Term" ? "Terms" : "Semesters")}).")
    {
        PeriodType = periodType;
        Division = division;
    }
}