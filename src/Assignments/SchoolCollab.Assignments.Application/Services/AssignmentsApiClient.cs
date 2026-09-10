using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Application.Services;

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
                new JsonStringEnumConverter<TargetAudienceTypeDto>(),
                new JsonStringEnumConverter<ReviewStateDto>(),
                new JsonStringEnumConverter<ContactOwnerTypeDto>(),
                new JsonStringEnumConverter<ContactChannelDto>(),
                new JsonStringEnumConverter<GuardianRoleDto>(),
                new JsonStringEnumConverter<SubmissionSourceDto>(),
                new JsonStringEnumConverter<QuestionTypeDto>(),
                // WS-A1 / FR-210-212: content module + AI-generation resource
                // enums round-trip as strings on the staging endpoint +
                // create payload.
                new JsonStringEnumConverter<ModuleTypeDto>(),
                new JsonStringEnumConverter<ResourceKindDto>(),
                // WS-A2 / spec §7 Q2: approval status is nullable on the wire
                // (null = not yet submitted). The string converter serializes
                // Pending / Approved / Rejected; null stays null.
                new JsonStringEnumConverter<ApprovalStatusDto>()
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

    public async Task LinkAssignmentGroupsAsync(Guid assignmentId, IReadOnlyList<Guid> groupIds, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/assignments/{assignmentId}/groups", new { ActivityGroupIds = groupIds }, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();
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
        (await _http.PostAsJsonAsync($"/assignments/{id}/publish", (PublishAssignmentRequest?)null, _jsonOptions, ct)).EnsureSuccessStatusCode();
    }

    public async Task PublishAsync(Guid id, IReadOnlyList<Guid>? contactIds, CancellationToken ct = default)
    {
        _logger.LogInformation("Publishing assignment {AssignmentId} to {Count} selected contacts", id, contactIds?.Count ?? 0);
        (await _http.PostAsJsonAsync($"/assignments/{id}/publish", new PublishAssignmentRequest(contactIds), _jsonOptions, ct)).EnsureSuccessStatusCode();
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

    // ── WS-A2 / spec §3.5 step 2 + §7 Q2 lifecycle ───────────────────────────

    /// <summary>Schedule an assignment to auto-publish at the given
    /// UTC moment. The scheduled-publish sweep dispatches the
    /// existing publish command when the moment arrives.</summary>
    public async Task ScheduleAsync(Guid id, DateTimeOffset availableFromUtc, CancellationToken ct = default)
    {
        _logger.LogInformation("Scheduling assignment {AssignmentId} for {AvailableFromUtc}", id, availableFromUtc);
        (await _http.PostAsJsonAsync($"/assignments/{id}/schedule", new ScheduleAssignmentRequest(availableFromUtc), _jsonOptions, ct)).EnsureSuccessStatusCode();
    }

    /// <summary>Submit a draft assignment for approval (spec §7 Q2).</summary>
    public async Task SubmitForApprovalAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogInformation("Submitting assignment {AssignmentId} for approval", id);
        (await _http.PostAsync($"/assignments/{id}/submit-for-approval", null, ct)).EnsureSuccessStatusCode();
    }

    /// <summary>Approve a pending assignment. <paramref name="approverId"/>
    /// is the identity placeholder — wired to auth in a later phase.</summary>
    public async Task ApproveAsync(Guid id, Guid approverId, CancellationToken ct = default)
    {
        _logger.LogInformation("Approving assignment {AssignmentId}", id);
        (await _http.PostAsJsonAsync($"/assignments/{id}/approve", new ApproveAssignmentRequest(approverId), _jsonOptions, ct)).EnsureSuccessStatusCode();
    }

    /// <summary>Reject a pending assignment. <paramref name="approverId"/>
    /// is the identity placeholder — wired to auth in a later phase.</summary>
    public async Task RejectAsync(Guid id, Guid approverId, CancellationToken ct = default)
    {
        _logger.LogInformation("Rejecting assignment {AssignmentId}", id);
        (await _http.PostAsJsonAsync($"/assignments/{id}/reject", new RejectAssignmentRequest(approverId), _jsonOptions, ct)).EnsureSuccessStatusCode();
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

    // ── Phase 7: recipients + submissions + review/gate (spec §8/§9/§12) ─────

    public async Task<AssignmentRecipientDto[]?> GetRecipientsAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogDebug("Getting recipients for assignment {AssignmentId}", id);
        var response = await _http.GetAsync($"/assignments/{id}/recipients", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AssignmentRecipientDto[]>(_jsonOptions, ct);
    }

    public async Task<SubmissionForReviewDto[]?> ListSubmissionsAsync(Guid assignmentId, CancellationToken ct = default)
    {
        _logger.LogDebug("Listing submissions for assignment {AssignmentId}", assignmentId);
        var result = await _http.GetFromJsonAsync<SubmissionForReviewDto[]>($"/assignments/{assignmentId}/submissions", _jsonOptions, ct);
        _logger.LogInformation("Listed {Count} submissions", result?.Length ?? 0);
        return result;
    }

    public async Task<SubmissionDetailDto?> GetSubmissionAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default)
    {
        _logger.LogDebug("Getting submission for assignment {AssignmentId} / student {StudentId}", assignmentId, studentId);
        var response = await _http.GetAsync($"/assignments/{assignmentId}/students/{studentId}/submission", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SubmissionDetailDto>(_jsonOptions, ct);
    }

    public async Task ReviewSubmissionAsync(Guid assignmentId, Guid studentId, ReviewSubmissionRequest req, CancellationToken ct = default)
    {
        _logger.LogInformation("Reviewing submission for assignment {AssignmentId} / student {StudentId}", assignmentId, studentId);
        (await _http.PostAsJsonAsync($"/assignments/{assignmentId}/students/{studentId}/submission/review", req, _jsonOptions, ct)).EnsureSuccessStatusCode();
    }

    public async Task ReviewGateAsync(Guid assignmentId, Guid studentId, ReviewSubmissionGateRequest req, CancellationToken ct = default)
    {
        _logger.LogInformation("Reviewing gate for assignment {AssignmentId} / student {StudentId}", assignmentId, studentId);
        (await _http.PostAsJsonAsync($"/assignments/{assignmentId}/students/{studentId}/guardian-review", req, _jsonOptions, ct)).EnsureSuccessStatusCode();
    }

    public async Task EnableSubmissionAsync(Guid assignmentId, Guid studentId, EnableStudentSubmissionRequest req, CancellationToken ct = default)
    {
        _logger.LogInformation("Enabling submission for assignment {AssignmentId} / student {StudentId}", assignmentId, studentId);
        (await _http.PostAsJsonAsync($"/assignments/{assignmentId}/students/{studentId}/enable-submission", req, _jsonOptions, ct)).EnsureSuccessStatusCode();
    }

    // ── WS-A1 / FR-210-212: stage one resource file (EC-4 stage-at-selection) ──

    /// <summary>Stages one uploaded file via the multipart staging
    /// endpoint (WS-A1 / FR-210). On 200 returns the server-issued
    /// <see cref="StagedAttachmentDto"/> (carrying the opaque
    /// <c>StoragePath</c> the wizard then rides on the create payload).
    /// On 400 / 413 reads the <c>{"message": ...}</c> body and throws
    /// a typed <see cref="AttachmentStagingFailed"/> carrying the server
    /// message (the round-2 <c>QuestionGenerationFailed</c> pattern).
    /// Any other non-success status propagates as an
    /// <see cref="HttpRequestException"/> via
    /// <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/>.</summary>
    public async Task<StagedAttachmentDto> StageAttachmentAsync(
        Stream content,
        string fileName,
        string contentType,
        long fileSize,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name is required.", nameof(fileName));
        if (string.IsNullOrWhiteSpace(contentType))
            throw new ArgumentException("Content type is required.", nameof(contentType));

        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);

        _logger.LogInformation(
            "Staging attachment {FileName} ({FileSize} bytes)", fileName, fileSize);
        var response = await _http.PostAsync("/assignments/attachments/stage", form, ct);

        if (response.IsSuccessStatusCode)
        {
            return (await response.Content.ReadFromJsonAsync<StagedAttachmentDto>(_jsonOptions, ct))!;
        }

        if (response.StatusCode is System.Net.HttpStatusCode.BadRequest
            or System.Net.HttpStatusCode.RequestEntityTooLarge)
        {
            string message;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    message = m.GetString() ?? "The server rejected the staged file.";
                }
                else
                {
                    message = "The server rejected the staged file.";
                }
            }
            catch (System.Text.Json.JsonException)
            {
                message = "The server rejected the staged file.";
            }
            _logger.LogWarning("Stage attachment rejected by server: {Message}", message);
            throw new AttachmentStagingFailed(message);
        }

        response.EnsureSuccessStatusCode();
        throw new InvalidOperationException("Unreachable: EnsureSuccessStatusCode returned without throwing.");
    }
}