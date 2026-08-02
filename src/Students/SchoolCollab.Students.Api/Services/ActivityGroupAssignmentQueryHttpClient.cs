using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Core.DTOs;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Api.Services;

/// <summary>
/// HTTP-backed <see cref="IActivityGroupAssignmentQuery"/> (spec
/// activity-group-enrollment.md FR-6 / EC-1). Calls the Assignments API
/// <c>GET /api/activity-groups/{id}/assignments</c> via Aspire service
/// discovery (named client <c>assignments-api</c>). The check is
/// <b>fail-closed</b>: if the Assignments API returns a server error or is
/// unreachable, the method throws so the delete handler blocks the delete.
/// A 404 is treated as "no references" (the endpoint may not be deployed yet
/// in earlier phases — Phase 3 adds the Assignments API side).
/// </summary>
public sealed class ActivityGroupAssignmentQueryHttpClient(
    IHttpClientFactory httpClientFactory,
    ILogger<ActivityGroupAssignmentQueryHttpClient> logger) : IActivityGroupAssignmentQuery
{
    public async Task<AssignmentReferenceDto[]> GetReferencingAssignmentsAsync(
        Guid activityGroupId, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("assignments-api");

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(
                $"api/activity-groups/{activityGroupId}/assignments", cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex,
                "Assignments API unreachable when checking references for group {Id}; failing closed",
                activityGroupId);
            throw;
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Endpoint not deployed yet (Phase 2) or no references — treat as empty.
            return [];
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AssignmentReferenceDto[]>(cancellationToken)
            ?? [];
    }
}
