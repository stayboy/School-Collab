namespace SchoolCollab.Students.Core.Services;

/// <summary>
/// Default <see cref="IAcademicYearDivisionProvider"/> returning <c>"None"</c>
/// (no sub-periods). Used by hosts that don't override with the Settings-API
/// HTTP client (workers, tests). Students.Api overrides this with
/// <see cref="SchoolCollab.Students.Api.Services.AcademicYearDivisionProviderHttpClient"/>.
/// </summary>
public sealed class DefaultAcademicYearDivisionProvider : IAcademicYearDivisionProvider
{
    public Task<string> GetDivisionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult("None");
}
