using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Core.DTOs;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Api.Services;

/// <summary>
/// HTTP-backed <see cref="IStudentAssignmentStatsReader"/> (issue #444, layer L2b). Resolves the
/// <b>existing</b> named client <c>assignments-api</c>, whose handler chain already supplies the tenant and
/// the forwarded bearer token (owner Q151), and calls the route layer L2a shipped:
/// <c>GET /assignments/{studentId}/assignment-stats</c>.
/// <para>
/// The <c>assignments/</c> segment is <b>required</b>: that named client's base address is the host root
/// (<c>https+http://assignments-api</c>), and the Assignments route lives on its <c>/assignments</c> group.
/// A path without it would 404 into a convincing "unavailable" against a healthy endpoint.
/// </para>
/// <para>
/// Failures are loud here and degraded one layer up: an unreachable host is logged and rethrown, a
/// non-success response throws through <c>EnsureSuccessStatusCode</c>, and an empty body throws rather than
/// masquerading as six zeros. <c>StudentStatsRoutes</c> turns that into a logged problem.
/// </para>
/// </summary>
public sealed class StudentAssignmentStatsReaderHttpClient(
    IHttpClientFactory httpClientFactory,
    ILogger<StudentAssignmentStatsReaderHttpClient> logger) : IStudentAssignmentStatsReader
{
    public async Task<StudentAssignmentStats> GetAsync(
        Guid studentId, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("assignments-api");

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(
                $"assignments/{studentId}/assignment-stats", cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex,
                "Assignments API unreachable when reading assignment stats for student {StudentId}",
                studentId);
            throw;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<StudentAssignmentStats>(cancellationToken)
            // An empty body is a failed read, not six zero counts: the panel's "unavailable" state is the
            // truthful answer, and zeros are a claim.
            ?? throw new HttpRequestException(
                $"The Assignments API answered an empty assignment-stats body for student {studentId}.");
    }
}
