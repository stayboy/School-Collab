using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Admin.Services;

public sealed class AssignmentsApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<AssignmentsApiClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public AssignmentsApiClient(HttpClient http, ILogger<AssignmentsApiClient> logger)
    {
        _http = http;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters =
            {
                new JsonStringEnumConverter<AssignmentTypeDto>(),
                new JsonStringEnumConverter<AssignmentStatusDto>(),
                new JsonStringEnumConverter<GradingFormatDto>(),
                new JsonStringEnumConverter<TargetAudienceTypeDto>()
            }
        };
    }

    public async Task<AssignmentSummaryDto[]?> ListAsync(AssignmentStatusDto? status = null, CancellationToken ct = default)
    {
        var url = "/assignments";
        if (status.HasValue)
            url += $"?status={status.Value}";

        _logger.LogDebug("Listing assignments with status filter {Status}", status);
        var result = await _http.GetFromJsonAsync<AssignmentSummaryDto[]>(url, _jsonOptions, ct);
        _logger.LogInformation("Listed {Count} assignments", result?.Length ?? 0);
        return result;
    }

    public async Task<AssignmentSummaryDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogDebug("Getting assignment {AssignmentId}", id);
        var response = await _http.GetAsync($"/assignments/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Assignment {AssignmentId} not found", id);
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AssignmentSummaryDto>(_jsonOptions, ct);
    }

    public async Task<Guid> CreateAsync(CreateAssignmentRequest req, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating assignment with title {Title}", req.Title);
        var response = await _http.PostAsJsonAsync("/assignments", req, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();
        var id = await response.Content.ReadFromJsonAsync<Guid>(_jsonOptions, ct);
        _logger.LogInformation("Assignment created with id {AssignmentId}", id);
        return id;
    }

    public async Task UpdateAsync(Guid id, UpdateAssignmentRequest req, CancellationToken ct = default)
    {
        _logger.LogInformation("Updating assignment {AssignmentId}", id);
        var response = await _http.PutAsJsonAsync($"/assignments/{id}", req, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task PublishAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogInformation("Publishing assignment {AssignmentId}", id);
        (await _http.PostAsync($"/assignments/{id}/publish", null, ct)).EnsureSuccessStatusCode();
    }

    public async Task UnpublishAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogInformation("Unpublishing assignment {AssignmentId}", id);
        (await _http.PostAsync($"/assignments/{id}/unpublish", null, ct)).EnsureSuccessStatusCode();
    }

    public async Task CloseAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogInformation("Closing assignment {AssignmentId}", id);
        (await _http.PostAsync($"/assignments/{id}/close", null, ct)).EnsureSuccessStatusCode();
    }

    public async Task ReviewAsync(Guid id, ReviewAssignmentRequest req, CancellationToken ct = default)
    {
        _logger.LogInformation("Reviewing assignment {AssignmentId}", id);
        (await _http.PostAsJsonAsync($"/assignments/{id}/review", req, _jsonOptions, ct)).EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogInformation("Deleting assignment {AssignmentId}", id);
        (await _http.DeleteAsync($"/assignments/{id}", ct)).EnsureSuccessStatusCode();
    }
}