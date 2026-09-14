using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>
/// HTTP-backed <see cref="IStudentDirectory"/> (WS-C1 / spec §5 + §7 Q5).
/// Calls the Students API via Aspire service discovery (named client
/// <c>students-api</c>). Best-effort reads: <see cref="GetStudentNameAsync"/>
/// and <see cref="GetGuardiansAsync"/> degrade to null/empty on 404 or network
/// failure; <see cref="IsGuardianOfAsync"/> is fail-CLOSED (any failure ⇒
/// false) because the sign/reassign handlers treat an unverifiable guardian as
/// unauthorized. Mirrors <see cref="ActivityGroupLookupHttpClient"/>.
/// </summary>
public sealed class StudentDirectoryHttpClient(
    IHttpClientFactory httpClientFactory,
    ILogger<StudentDirectoryHttpClient> logger) : IStudentDirectory
{
    /// <summary>Path of the students guardian-list route
    /// (<c>GET /students/{studentId}/guardians</c> — StudentGuardianRoutes.cs).</summary>
    internal const string GuardiansPathTemplate = "students/{0}/guardians";

    public async Task<StudentNameInfo?> GetStudentNameAsync(
        Guid studentId, CancellationToken cancellationToken = default)
    {
        var students = httpClientFactory.CreateClient("students-api");
        try
        {
            var response = await students.GetAsync($"students/{studentId}", cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<StudentDto>(cancellationToken);
            if (dto is null)
            {
                return null;
            }
            return new StudentNameInfo(dto.Id, $"{dto.FirstName} {dto.LastName}");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to resolve student name for {StudentId}", studentId);
            return null;
        }
    }

    public async Task<IReadOnlyList<WardGuardianInfo>> GetGuardiansAsync(
        Guid studentId, CancellationToken cancellationToken = default)
    {
        var students = httpClientFactory.CreateClient("students-api");
        try
        {
            var response = await students.GetAsync(
                string.Format(GuardiansPathTemplate, studentId), cancellationToken);
            response.EnsureSuccessStatusCode();
            var dtos = await response.Content.ReadFromJsonAsync<StudentGuardianViewDto[]>(cancellationToken);
            if (dtos is null)
            {
                return [];
            }

            return dtos
                .Where(g => g.GuardianId != Guid.Empty)
                .Select(g => new WardGuardianInfo(
                    g.GuardianId,
                    !string.IsNullOrWhiteSpace(g.DisplayName) ? g.DisplayName : TrimFullName($"{g.FirstName} {g.LastName}"),
                    g.Role == GuardianRole.Primary))
                .ToList();
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to resolve guardians for student {StudentId}", studentId);
            return [];
        }
    }

    public async Task<bool> IsGuardianOfAsync(
        Guid studentId, Guid guardianId, CancellationToken cancellationToken = default)
    {
        if (guardianId == Guid.Empty)
        {
            return false;
        }

        // Fail-closed on authorization: reuse the guardians fetch and require a
        // matching linked row. An empty/errored result must NOT authorize.
        var guardians = await GetGuardiansAsync(studentId, cancellationToken);
        return guardians.Any(g => g.GuardianId == guardianId);
    }

    private static string TrimFullName(string name) => name.Trim();
}