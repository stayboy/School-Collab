namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// An AcademicYear period's academic-calendar subdivision
/// (period-hierarchy-terms-semesters.md FR-H6). <see cref="None"/> = single
/// academic-year period only (no sub-periods); <see cref="Terms"/> /
/// <see cref="Semesters"/> enables the matching sub-period type under that year.
/// Chosen during period setup on the AcademicYear period itself (Rev. 2 — moved
/// off the Settings feature flag). Null on Term/Semester rows; non-null on
/// AcademicYear rows (back-filled <see cref="None"/>).
/// </summary>
public enum AcademicYearDivision
{
    None = 0,
    Terms = 1,
    Semesters = 2
}
