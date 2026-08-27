namespace SchoolCollab.Students.Core.Services;

/// <summary>
/// Cross-context port (period-hierarchy-terms-semesters.md FR-H7) used by
/// <c>CreatePeriodHandler</c> to gate <c>Term</c>/<c>Semester</c> creation on
/// the tenant's academic-year division. The HTTP client implementation lives in
/// <c>SchoolCollab.Students.Api</c> and calls
/// <c>GET /api/config/flags/academic_year_division</c> on the settings-api via
/// Aspire service discovery. Returns one of <c>"None"</c> | <c>"Terms"</c> |
/// <c>"Semesters"</c>.
/// </summary>
public interface IAcademicYearDivisionProvider
{
    /// <summary>
    /// The effective academic-year division for the current tenant. Fail-open to
    /// <c>"None"</c> if the Settings API is unreachable (conservative: no
    /// sub-periods are allowed without knowing the framework).
    /// </summary>
    Task<string> GetDivisionAsync(CancellationToken cancellationToken = default);
}
