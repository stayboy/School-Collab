using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace SchoolCollab.Assignments.Admin.Services;

public enum AssignmentStatusDto { Draft, Published, Closed }
public enum AssignmentTypeDto { Online, Hybrid, Offline }

public record AssignmentSummaryDto(
    Guid Id,
    string Title,
    string? Description,
    AssignmentTypeDto AssignmentType,
    Guid SubjectCodedValueId,
    string? SubjectName,
    Guid? GradeCodedValueId,
    string? GradeName,
    AssignmentStatusDto Status,
    DateTimeOffset? DueDate,
    decimal? MaxScore,
    Guid CreatedByTeacherId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateAssignmentRequest(
    string Title,
    string? Description,
    AssignmentTypeDto AssignmentType,
    Guid SubjectCodedValueId,
    Guid? GradeCodedValueId,
    DateTimeOffset? DueDate,
    decimal? MaxScore);

public record UpdateAssignmentRequest(
    string Title,
    string? Description,
    AssignmentTypeDto AssignmentType,
    Guid SubjectCodedValueId,
    Guid? GradeCodedValueId,
    DateTimeOffset? DueDate,
    decimal? MaxScore);

public record ReviewAssignmentRequest(
    Guid TeacherId,
    decimal? Score,
    string? Comments);

public sealed class AssignmentsApiClient(HttpClient http, ILogger<AssignmentsApiClient> logger)
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<AssignmentSummaryDto[]?> ListAsync(AssignmentStatusDto? status = null, CancellationToken ct = default)
    {
        var url = "/assignments";
        if (status.HasValue)
            url += $"?status={status.Value}";

        logger.LogDebug("Listing assignments with status filter {Status}", status);
        var result = await http.GetFromJsonAsync<AssignmentSummaryDto[]>(url, _jsonOptions, ct);
        logger.LogInformation("Listed {Count} assignments", result?.Length ?? 0);
        return result;
    }

    public async Task<AssignmentSummaryDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        logger.LogDebug("Getting assignment {AssignmentId}", id);
        var response = await http.GetAsync($"/assignments/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogWarning("Assignment {AssignmentId} not found", id);
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AssignmentSummaryDto>(_jsonOptions, ct);
    }

    public async Task<Guid> CreateAsync(CreateAssignmentRequest req, CancellationToken ct = default)
    {
        logger.LogInformation("Creating assignment with title {Title}", req.Title);
        var response = await http.PostAsJsonAsync("/assignments", req, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();
        var id = await response.Content.ReadFromJsonAsync<Guid>(ct);
        logger.LogInformation("Assignment created with id {AssignmentId}", id);
        return id;
    }

    public async Task UpdateAsync(Guid id, UpdateAssignmentRequest req, CancellationToken ct = default)
    {
        logger.LogInformation("Updating assignment {AssignmentId}", id);
        var response = await http.PutAsJsonAsync($"/assignments/{id}", req, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task PublishAsync(Guid id, CancellationToken ct = default)
    {
        logger.LogInformation("Publishing assignment {AssignmentId}", id);
        (await http.PostAsync($"/assignments/{id}/publish", null, ct)).EnsureSuccessStatusCode();
    }

    public async Task UnpublishAsync(Guid id, CancellationToken ct = default)
    {
        logger.LogInformation("Unpublishing assignment {AssignmentId}", id);
        (await http.PostAsync($"/assignments/{id}/unpublish", null, ct)).EnsureSuccessStatusCode();
    }

    public async Task CloseAsync(Guid id, CancellationToken ct = default)
    {
        logger.LogInformation("Closing assignment {AssignmentId}", id);
        (await http.PostAsync($"/assignments/{id}/close", null, ct)).EnsureSuccessStatusCode();
    }

    public async Task ReviewAsync(Guid id, ReviewAssignmentRequest req, CancellationToken ct = default)
    {
        logger.LogInformation("Reviewing assignment {AssignmentId}", id);
        (await http.PostAsJsonAsync($"/assignments/{id}/review", req, ct)).EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        logger.LogInformation("Deleting assignment {AssignmentId}", id);
        (await http.DeleteAsync($"/assignments/{id}", ct)).EnsureSuccessStatusCode();
    }
}