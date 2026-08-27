namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Academic-calendar hierarchy kind (period-hierarchy-terms-semesters.md FR-H1).
/// An <see cref="AcademicYear"/> is the root; a <see cref="Term"/> or
/// <see cref="Semester"/> is a sub-period whose <c>ParentPeriodId</c> points at
/// its AcademicYear.
/// </summary>
public enum PeriodType
{
    AcademicYear = 0,
    Term = 1,
    Semester = 2
}
