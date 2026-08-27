using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Settings.Core.Services;

namespace SchoolCollab.Settings.Api.Services;

/// <summary>
/// HTTP-backed <see cref="ISubPeriodCountProvider"/> (period-hierarchy-terms-
/// semesters.md FR-H7). Calls the Students API
/// <c>GET /students/periods/sub-period-count</c> via Aspire service discovery
/// (named client <c>students-api</c>). The tenant is forwarded by the
/// <c>TenantForwardingDelegatingHandler</c> so the Students endpoint resolves the
/// same tenant's sub-periods. Fail-closed: if Students is unreachable it throws
/// (the division route treats an indeterminate count as "has sub-periods" and
/// rejects the switch rather than risk an unsafe framework change). The default
/// <see cref="DefaultSubPeriodCountProvider"/> (returns 0) covers a Settings-only
/// deployment that has no Students service at all.
/// </summary>
public sealed class SubPeriodCountProviderHttpClient(
    IHttpClientFactory httpClientFactory,
    ILogger<SubPeriodCountProviderHttpClient> logger) : ISubPeriodCountProvider
{
    public async Task<int> GetSubPeriodCountAsync(CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("students-api");

        // Fail-closed: an indeterminate count must not allow a framework switch
        // that FR-H7 forbids while sub-periods exist. The route maps this to a 422.
        try
        {
            var response = await client.GetFromJsonAsync<SubPeriodCountResponse>(
                "students/periods/sub-period-count", cancellationToken);
            return response?.Count ?? 0;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex,
                "Students API unreachable resolving sub-period count; failing closed on the division switch");
            throw new InvalidOperationException(
                "Cannot verify the sub-period count with the Students API; the division switch is refused.", ex);
        }
    }

    private sealed record SubPeriodCountResponse(int Count);
}