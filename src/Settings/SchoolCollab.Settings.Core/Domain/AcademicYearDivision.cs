namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// A tenant's academic-calendar subdivision (period-hierarchy-terms-semesters.md
/// FR-H6). <see cref="None"/> = single AcademicYear periods only (pre-hierarchy);
/// <see cref="Terms"/>/<see cref="Semesters"/> enables the matching sub-period
/// type. Stored as a string-valued feature flag (<c>academic_year_division</c>).
/// </summary>
public enum AcademicYearDivision
{
    None = 0,
    Terms = 1,
    Semesters = 2
}
