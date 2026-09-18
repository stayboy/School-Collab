using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>
/// HTTP-backed <see cref="ITeacherDirectory"/> (ar-20). Calls the existing
/// <c>GET /teachers/{id:guid}</c> Students route via the <c>students-api</c> named client
/// (Aspire service discovery). Fail-closed: any 404 or <see cref="HttpRequestException"/> /
/// non-success status returns <see langword="false"/> so an unknown or unreachable teacher
/// never passes the create-attribution gate. Mirrors <see cref="StudentDirectoryHttpClient"/>.
/// </summary>
public sealed class TeacherDirectoryHttpClient(
    IHttpClientFactory httpClientFactory,
    ILogger<TeacherDirectoryHttpClient> logger) : ITeacherDirectory
{
    public async Task<bool> ExistsAsync(Guid teacherId, CancellationToken cancellationToken = default)
    {
        if (teacherId == Guid.Empty)
        {
            return false;
        }

        var students = httpClientFactory.CreateClient("students-api");
        try
        {
            var response = await students.GetAsync($"teachers/{teacherId}", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to verify teacher {TeacherId} existence via students-api", teacherId);
            return false;
        }
    }
}
