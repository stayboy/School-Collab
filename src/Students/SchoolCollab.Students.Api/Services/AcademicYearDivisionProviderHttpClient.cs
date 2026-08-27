using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Api.Services;

/// <summary>
/// HTTP-backed <see cref="IAcademicYearDivisionProvider"/> (period-hierarchy
/// period-hierarchy-terms-semesters.md FR-H7). Calls the Settings API
/// <c>GET /api/config/flags/academic_year_division</c> via Aspire service
/// discovery (named client <c>settings-api</c>). The tenant is forwarded by the
/// <c>TenantForwardingDelegatingHandler</c> so the Settings endpoint resolves the
/// same tenant's division. Fail-open to <c>"None"</c> if the Settings API is
/// unreachable (conservative: no sub-periods allowed without knowing the framework).
/// </summary>
public sealed class AcademicYearDivisionProviderHttpClient(
    IHttpClientFactory httpClientFactory,
    ILogger<AcademicYearDivisionProviderHttpClient> logger) : IAcademicYearDivisionProvider
{
    public async Task<string> GetDivisionAsync(CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("settings-api");

        try
        {
            var dto = await client.GetFromJsonAsync<DivisionResponse>(
                "api/config/flags/academic_year_division", cancellationToken);
            return dto?.Value ?? "None";
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex,
                "Settings API unreachable resolving academic-year division; defaulting to None");
            return "None";
        }
    }

    private sealed record DivisionResponse(string Value, string Source);
}
