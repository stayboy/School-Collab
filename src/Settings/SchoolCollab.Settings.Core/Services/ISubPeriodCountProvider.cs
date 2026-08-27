namespace SchoolCollab.Settings.Core.Services;

/// <summary>
/// Cross-context port (period-hierarchy-terms-semesters.md FR-H7) that lets the
/// Settings context ask the Students context how many non-completed
/// <c>Term</c>/<c>Semester</c> sub-periods the current tenant has. The Settings
/// PUT for <c>academic_year_division</c> rejects a framework switch while
/// sub-periods exist (the tenant must complete/remove them first). Implemented in
/// Settings.Api as an HTTP client calling the Students API; the default (no
/// Students service deployed) returns <c>0</c> — a Settings-only deployment has
/// no Students sub-periods, so a switch is safe.
/// </summary>
public interface ISubPeriodCountProvider
{
    /// <summary>
    /// The number of non-completed (Draft/Active) Term/Semester sub-periods for
    /// the current tenant.
    /// </summary>
    Task<int> GetSubPeriodCountAsync(CancellationToken cancellationToken = default);
}