using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Families.Services;

/// <summary>Json serialization options shared by the Families client — web defaults
/// plus string-encoded enums, mirroring the Assignments API's
/// <c>ConfigureHttpJsonOptions</c> (round-trip the ward DTO enums as names, not
/// numbers).</summary>
internal static class FamiliesJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };
}

/// <summary>
/// F1 (slice 2b) — the Families surface's thin typed client over the Assignments
/// API's ward-facing endpoints (the ar-12 seams). Option B: the Families host
/// references only <c>Assignments.Contracts</c> (the cross-context boundary), so
/// this client carries the exact six calls the ward pages bind to. No admin
/// <c>Assignments.Application</c> reference, so no admin route leakage.
/// </summary>
public sealed class FamiliesApiClient(
    HttpClient http,
    ILogger<FamiliesApiClient> logger)
{
    private readonly string _assignments = "assignments";

    /// <summary>WS-A5 — the ward's assignment list.</summary>
    public async Task<WardAssignmentListItemDto[]?> ListWardAssignmentsAsync(Guid studentId, CancellationToken ct = default)
    {
        try
        {
            var response = await http.GetAsync($"/students/{studentId}/assignments", ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return [];
            }
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<WardAssignmentListItemDto[]>(FamiliesJson.Options, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Ward assignment-list fetch failed for student {StudentId}", studentId);
            throw;
        }
    }

    /// <summary>WS-D1/WS-A5 — the ward's per-assignment view (modules + gate flag).</summary>
    public async Task<WardAssignmentViewDto?> GetWardAssignmentViewAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/{_assignments}/{assignmentId}/students/{studentId}/modules", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WardAssignmentViewDto>(FamiliesJson.Options, ct);
    }

    /// <summary>WS-D1 — report per-module progress (monotonic/idempotent on the API).</summary>
    public async Task<bool> ReportModuleProgressAsync(Guid assignmentId, Guid studentId, Guid moduleId, int percent, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            $"/{_assignments}/{assignmentId}/students/{studentId}/modules/{moduleId}/progress",
            new RecordModuleProgressRequest(percent), FamiliesJson.Options, ct);
        return response.StatusCode == System.Net.HttpStatusCode.NoContent;
    }

    /// <summary>WS-A3 — submit the ward's answers (free-text content + structured answers).</summary>
    public async Task<SubmissionFeedbackDto?> SubmitAsync(Guid assignmentId, Guid studentId, string? content, IReadOnlyList<SubmissionAnswerDto>? answers, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            $"/{_assignments}/{assignmentId}/students/{studentId}/submission",
            new CreateStudentSubmissionRequest(content, answers), FamiliesJson.Options, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return null;
        }
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict ||
            response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Submission rejected {Status} for assignment {Id}/student {StudentId}: {Body}",
                (int)response.StatusCode, assignmentId, studentId, error);
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SubmissionFeedbackDto>(FamiliesJson.Options, ct);
    }

    /// <summary>WS-A3 — the submission detail (result score/pass/retry state).</summary>
    public async Task<SubmissionDetailDto?> GetSubmissionAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/{_assignments}/{assignmentId}/students/{studentId}/submission", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SubmissionDetailDto>(FamiliesJson.Options, ct);
    }
}
