using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.AssignmentPolicies;

namespace SchoolCollab.Assignments.Application.Services;

/// <summary>
/// The Audience &amp; Targets live-preview request (assignment-authoring-compartments §7.5
/// TGT-16 / D-4): the current constraint set, exactly as the create/update payload would carry it.
/// Publish derives both its teacher cohort and its policy-scope grade from the grade-target ids in
/// <see cref="GradeLevelIds"/>; the preview reproduces that derivation from the same posted set
/// (round <c>drop-primary-grade</c> removed the separately-authored primary grade the preview used
/// to carry).
/// </summary>
public sealed record RecipientPreviewRequest(
    bool AllStudents,
    IReadOnlyList<Guid> GradeLevelIds,
    IReadOnlyList<Guid> StreamCodedValueIds,
    IReadOnlyList<Guid> StudentIds,
    IReadOnlyList<Guid> ActivityGroupIds);

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
                // R2 (TGT-1): the targeting-kind discriminator round-trips as a name on the
                // create/update payload and the authoring child read.
                new JsonStringEnumConverter<TargetKindDto>(),
                new JsonStringEnumConverter<ReviewStateDto>(),
                new JsonStringEnumConverter<ContactOwnerTypeDto>(),
                new JsonStringEnumConverter<ContactChannelDto>(),
                // WS-E2 / ar-17: tolerant read-side converter for the delivery enums.
                // NOTE (review F3): the Assignments API registers ten sibling enums as
                // strings but does NOT register NotificationKindDto / ContactChannelDto,
                // so the live wire form is currently NUMERIC and would deserialize
                // without this converter. It is kept as read-side tolerance (correct
                // either way) pending the API being aligned with its siblings — recorded
                // as a residual; the API-side registration is the real inconsistency.
                new JsonStringEnumConverter<NotificationKindDto>(),
                new JsonStringEnumConverter<GuardianRoleDto>(),
                new JsonStringEnumConverter<SubmissionSourceDto>(),
                new JsonStringEnumConverter<QuestionTypeDto>(),
                // WS-A1 / FR-210-212: content module + AI-generation resource
                // enums round-trip as strings on the staging endpoint +
                // create payload.
                new JsonStringEnumConverter<ModuleTypeDto>(),
                new JsonStringEnumConverter<ResourceKindDto>(),
                // R3 (D4/P1-3): the attachment extraction status round-trips as its name on the
                // stage response, the create/update payload and the authoring child read —
                // registered here AND on the API host so neither side hides the other's gap.
                new JsonStringEnumConverter<AttachmentExtractionStatusDto>(),
                // WS-A2 / spec §7 Q2: approval status is nullable on the wire
                // (null = not yet submitted). The string converter serializes
                // Pending / Approved / Rejected; null stays null.
                new JsonStringEnumConverter<ApprovalStatusDto>(),
                // WS-C1/C2: sign-off + signature-type enums round-trip as
                // strings on the sign-off routes.
                new JsonStringEnumConverter<SignOffStateDto>(),
                new JsonStringEnumConverter<SignatureTypeDto>()
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

    /// <summary>
    /// Resolves the <b>effective assignment policy</b> for the authoring page's Rules readouts
    /// (round <c>assignment-rules-policy-rework</c> D2/OD4). <paramref name="gradeLevelId"/> null
    /// resolves the tenant-global default; a grade id resolves the grade override falling back to
    /// the tenant default. The response is the whole resolved policy with the D4 implication
    /// (<c>RequiresSignature ⇒ MandatoryReview</c>) already applied server-side, so no caller
    /// recomputes it. Always succeeds against a reachable API (the route is fail-open); an
    /// unreachable endpoint surfaces as <see cref="HttpRequestException"/> for the caller to log
    /// and ignore.
    /// </summary>
    public async Task<EffectiveAssignmentPolicy?> GetEffectivePolicyAsync(
        Guid? gradeLevelId, CancellationToken ct = default)
    {
        _logger.LogDebug("Resolving the effective assignment policy for grade {GradeLevelId}", gradeLevelId);
        var url = gradeLevelId.HasValue
            ? $"/assignments/effective-policy?gradeLevelId={gradeLevelId}"
            : "/assignments/effective-policy";
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var policy = await response.Content.ReadFromJsonAsync<EffectiveAssignmentPolicy>(_jsonOptions, ct);
        _logger.LogInformation(
            "Resolved the effective assignment policy for grade {GradeLevelId}: signature {Signature}, review {Review}, archive grace {ArchiveGraceDays}",
            gradeLevelId, policy?.SignatureRequirement, policy?.MandatoryReview, policy?.ArchiveGraceDays);
        return policy;
    }

    /// <summary>
    /// Reads one assignment's persisted child collections — questions (with options),
    /// attachments and AI-generation resources — so the Edit surface can populate its
    /// editors before an author edits them (assignment-authoring P1 rework). Null on
    /// 404 (no such assignment in the caller's tenant).
    /// </summary>
    public async Task<AssignmentAuthoringChildrenDto?> GetAuthoringChildrenAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogDebug("Getting authoring children for assignment {AssignmentId}", id);
        var response = await _http.GetAsync($"/assignments/{id}/authoring", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Assignment {AssignmentId} not found for the authoring child read", id);
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AssignmentAuthoringChildrenDto>(_jsonOptions, ct);
    }

    /// <summary>
    /// Resolves the live recipient preview for a constraint set (assignment-authoring-compartments
    /// §7.5 TGT-16 / D-4). The counts are advisory — a failed read returns null and the caller
    /// renders the degraded note; save is never blocked.
    /// <para>Publish derives its teacher-recipient cohort and its notification/assignment policy
    /// from the assignment's grade TARGETS, so the preview carries the posted grade-target ids in
    /// <see cref="RecipientPreviewRequest.GradeLevelIds"/> and the server reproduces that same
    /// derivation — otherwise the "contacts reachable" count under-reports against what publish
    /// actually sends.</para>
    /// </summary>
    public async Task<RecipientPreviewDto?> GetRecipientPreviewAsync(
        RecipientPreviewRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = new List<string> { $"allStudents={request.AllStudents.ToString().ToLowerInvariant()}" };
        query.AddRange(request.GradeLevelIds.Select(id => $"gradeLevelIds={id}"));
        query.AddRange(request.StreamCodedValueIds.Select(id => $"streamCodedValueIds={id}"));
        query.AddRange(request.StudentIds.Select(id => $"studentIds={id}"));
        query.AddRange(request.ActivityGroupIds.Select(id => $"activityGroupIds={id}"));

        _logger.LogDebug("Resolving recipient preview for {Constraints} target(s)",
            request.GradeLevelIds.Count + request.StreamCodedValueIds.Count
            + request.StudentIds.Count + request.ActivityGroupIds.Count);
        var response = await _http.GetAsync($"/assignments/recipient-preview?{string.Join("&", query)}", ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Recipient preview read failed with {Status}", response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<RecipientPreviewDto>(_jsonOptions, ct);
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

    /// <summary>
    /// Reads the activity groups already linked to an assignment (spec §7.3
    /// <c>GET /assignments/{id}/groups</c>) so the Edit surface's picker starts from the
    /// persisted links rather than an empty set. Returns <c>null</c> when the route is not
    /// mapped for this host (404 — <c>FEATURE:EnableActivityGroups</c> off) or the read
    /// fails, which the caller treats as "links unknown": it then never persists a
    /// replace-set (that would clear the persisted links).
    /// </summary>
    public async Task<Guid[]?> GetLinkedActivityGroupIdsAsync(Guid assignmentId, CancellationToken ct = default)
    {
        _logger.LogDebug("Getting linked activity groups for assignment {AssignmentId}", assignmentId);
        var response = await _http.GetAsync($"/assignments/{assignmentId}/groups", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        var refs = await response.Content.ReadFromJsonAsync<LinkedActivityGroupResponse[]>(_jsonOptions, ct);
        return refs?.Select(r => r.Id).ToArray();
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

    /// <summary>Archive a closed (or published) assignment — read-only retention
    /// (assignment-authoring-compartments §11: the Closed row's primary action).
    /// The archive sweep performs the same transition automatically once the grace
    /// window elapses.</summary>
    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogInformation("Archiving assignment {AssignmentId}", id);
        (await _http.PostAsync($"/assignments/{id}/archive", null, ct)).EnsureSuccessStatusCode();
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

    /// <summary>Duplicate an assignment as a fresh Draft template copy
    /// (WS-A4 / spec §3.1). The no-body POST mirrors the Unpublish/Close
    /// precedent; the Created response's <c>{"id": ...}</c> body is parsed
    /// via the private <see cref="IdResponse"/> record (NOT
    /// <c>ReadFromJsonAsync&lt;Guid&gt;</c>, which throws on the object body).</summary>
    public async Task<Guid> DuplicateAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogInformation("Duplicating assignment {AssignmentId}", id);
        var response = await _http.PostAsync($"/assignments/{id}/duplicate", null, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(_jsonOptions, ct);
        _logger.LogInformation("Assignment {AssignmentId} duplicated to {NewAssignmentId}", id, result!.Id);
        return result!.Id;
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

    /// <summary>WS-A3 (spec §7 Q4) — clear the <c>MaxAttempts</c> cap
    /// on a single submission. <paramref name="teacherId"/> is the
    /// identity placeholder (D-6).</summary>
    public async Task OverrideStudentSubmissionAttemptsAsync(Guid assignmentId, Guid studentId, Guid teacherId, CancellationToken ct = default)
    {
        _logger.LogInformation("Overriding attempt cap for assignment {AssignmentId} / student {StudentId}", assignmentId, studentId);
        (await _http.PostAsJsonAsync(
            $"/assignments/{assignmentId}/students/{studentId}/override-attempts",
            new OverrideStudentSubmissionAttemptsRequest(teacherId),
            _jsonOptions,
            ct)).EnsureSuccessStatusCode();
    }

    // ── WS-C1/C2: guardian sign-off (spec §3.2 / §5 / §6) ────────────────────

    /// <summary>Resolves the consent language the sign page presents (WS-C2).
    /// The API always resolves (fail-open) to a usable string — the tenant
    /// override or the embedded default.</summary>
    public async Task<string> GetSignatureConsentTextAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Resolving signature consent text");
        var response = await _http.GetAsync("/assignments/signature-consent-text", ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SignatureConsentTextResponse>(_jsonOptions, ct);
        return result?.ConsentText ?? "";
    }

    /// <summary>Per-ward sign-off status rows for the teacher surface (WS-C1).
    /// 404 on a missing assignment.</summary>
    public async Task<IReadOnlyList<SignOffStatusDto>?> ListSignOffStatusesAsync(Guid assignmentId, CancellationToken ct = default)
    {
        _logger.LogDebug("Listing sign-off statuses for assignment {AssignmentId}", assignmentId);
        var response = await _http.GetAsync($"/assignments/{assignmentId}/sign-off-statuses", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<SignOffStatusDto>>(_jsonOptions, ct);
    }

    /// <summary>Reads the failed-delivery rows for an assignment (WS-E2 / ar-17).
    /// The tenant-scoped endpoint always returns 200 with a (possibly empty)
    /// <see cref="NotificationFailureDto"/> array; a null response is mapped to an
    /// empty array so the caller can treat it as "no failures". The rows are read-
    /// only — no retry / requeue affordance exists here (those are E3's concern).</summary>
    public async Task<NotificationFailureDto[]> GetNotificationFailuresAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogDebug("Getting notification failures for assignment {AssignmentId}", id);
        var result = await _http.GetFromJsonAsync<NotificationFailureDto[]>(
            $"/assignments/{id}/notification-failures", _jsonOptions, ct);
        _logger.LogInformation(
            "Loaded {Count} notification failures for assignment {AssignmentId}", result?.Length ?? 0, id);
        return result ?? [];
    }

    /// <summary>The aggregate the guardian sign page consumes (WS-C2) — one call.</summary>
    public async Task<SignOffContextDto?> GetSignOffContextAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default)
    {
        _logger.LogDebug("Getting sign-off context for assignment {AssignmentId} / student {StudentId}", assignmentId, studentId);
        var response = await _http.GetAsync($"/assignments/{assignmentId}/students/{studentId}/sign-off", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SignOffContextDto>(_jsonOptions, ct);
    }

    /// <summary>Guardian e-signs a ward's submission (WS-C2). Returns the refreshed status row.</summary>
    public async Task<SignOffStatusDto> SignOffAsync(Guid assignmentId, Guid studentId, SignOffSubmissionRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Signing off student {StudentId} for assignment {AssignmentId}", studentId, assignmentId);
        var response = await _http.PostAsJsonAsync(
            $"/assignments/{assignmentId}/students/{studentId}/sign-off", request, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SignOffStatusDto>(_jsonOptions, ct))!;
    }

    /// <summary>Teacher reassigns the expected signer (WS-C1).</summary>
    public async Task ReassignSignerAsync(Guid assignmentId, Guid studentId, Guid newGuardianId, CancellationToken ct = default)
    {
        _logger.LogInformation("Reassigning signer for student {StudentId} / assignment {AssignmentId}", studentId, assignmentId);
        (await _http.PostAsJsonAsync(
            $"/assignments/{assignmentId}/students/{studentId}/sign-off/reassign",
            new ReassignSignOffRequest(newGuardianId),
            _jsonOptions,
            ct)).EnsureSuccessStatusCode();
    }

    /// <summary>Teacher finalizes a signed sign-off (WS-C1/C4).</summary>
    public async Task FinalizeSignOffAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default)
    {
        _logger.LogInformation("Finalizing sign-off for student {StudentId} / assignment {AssignmentId}", studentId, assignmentId);
        (await _http.PostAsync(
            $"/assignments/{assignmentId}/students/{studentId}/sign-off/finalize", null, ct)).EnsureSuccessStatusCode();
    }

    /// <summary>C3 — downloads the finalized sign-off certificate PDF for a
    /// (assignment, ward) pair. A 404 (no event / no certificate generated /
    /// backing file missing) surfaces as <see cref="HttpRequestException"/>
    /// via <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/> for the
    /// UI to show an error bar rather than crash.</summary>
    public async Task<byte[]> GetCertificateAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default)
    {
        _logger.LogDebug("Getting certificate for assignment {AssignmentId} / student {StudentId}", assignmentId, studentId);
        var response = await _http.GetAsync(
            $"/assignments/{assignmentId}/students/{studentId}/certificate", ct);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        _logger.LogInformation("Downloaded {Bytes} certificate bytes for assignment {AssignmentId} / student {StudentId}",
            bytes.Length, assignmentId, studentId);
        return bytes;
    }

    /// <summary>The AI-prompt lock state for the create/edit wizard (WS-B2
    /// spec §3.4 line 70). Always-200 fail-open resolution from the API.</summary>
    public async Task<bool> GetAiPromptPolicyAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Resolving AI-prompt lock state");
        var response = await _http.GetAsync("/assignments/ai-prompt-policy", ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AiPromptPolicyResponse>(_jsonOptions, ct);
        return result?.AiPromptLocked ?? false;
    }

    /// <summary>The staged AI questions draft (WS-B2 spec §3.4 line 73); null
    /// when none is staged (the API returns 204) — survives page reloads.</summary>
    public async Task<IReadOnlyList<NewQuestionDto>?> GetQuestionsDraftAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/assignments/{id}/questions-draft", ct);
        if (response.StatusCode is System.Net.HttpStatusCode.NoContent or System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<QuestionsDraftResponse>(_jsonOptions, ct);
        return result?.Questions;
    }

    /// <summary>Stages a generated questions draft server-side (WS-B2).</summary>
    public async Task StageQuestionsDraftAsync(Guid id, IReadOnlyList<NewQuestionDto> questions, CancellationToken ct = default)
    {
        _logger.LogDebug("Staging questions draft for assignment {AssignmentId}", id);
        var response = await _http.PutAsJsonAsync($"/assignments/{id}/questions-draft", new StageQuestionsDraftRequest(questions), _jsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>REPLACES all existing questions with the staged draft (WS-B2);
    /// returns the updated summary.</summary>
    public async Task<AssignmentSummaryDto?> ConfirmQuestionsDraftAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogDebug("Confirming questions draft for assignment {AssignmentId}", id);
        var response = await _http.PostAsJsonAsync($"/assignments/{id}/questions-draft/confirm", (ConfirmQuestionsDraftRequest?)null, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AssignmentSummaryDto>(_jsonOptions, ct);
    }

    /// <summary>Drops the staged questions draft (WS-B2).</summary>
    public async Task DiscardQuestionsDraftAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogDebug("Discarding questions draft for assignment {AssignmentId}", id);
        var response = await _http.DeleteAsync($"/assignments/{id}/questions-draft", ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>WS-B2 — the always-200 <c>{"aiPromptLocked": ...}</c> body of the
    /// /assignments/ai-prompt-policy route (round-7 <see cref="IdResponse"/> precedent).</summary>
    private sealed record AiPromptPolicyResponse(bool AiPromptLocked);

    // ── R3 (D3/D4): generation headers + attachment extraction regeneration ──

    /// <summary>
    /// R3 (D4/P1-2) — records one AI question generation and returns the new header's id, which the
    /// caller stamps onto every produced question. <paramref name="provider"/>/<paramref name="model"/> are
    /// the AI host's own resolution relayed from its response (P1-1): the author never supplies them
    /// and this client never invents them.
    /// </summary>
    public async Task<Guid> RecordQuestionGenerationAsync(
        Guid assignmentId,
        RecordQuestionGenerationRequest request,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Recording question generation for assignment {AssignmentId}", assignmentId);
        var response = await _http.PostAsJsonAsync(
            $"/assignments/{assignmentId}/question-generations", request, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<RecordQuestionGenerationResponse>(_jsonOptions, ct);
        return body!.GenerationId;
    }

    /// <summary>
    /// R3 (D3, criterion 5) — re-runs extraction for one persisted attachment and returns the row with
    /// its new status. The only path that rewrites a stored extraction.
    /// </summary>
    public async Task<AssignmentAttachmentReadDto> RegenerateAttachmentExtractionAsync(
        Guid assignmentId,
        Guid attachmentId,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Regenerating extraction for attachment {AttachmentId} of assignment {AssignmentId}",
            attachmentId, assignmentId);
        var response = await _http.PostAsync(
            $"/assignments/{assignmentId}/attachments/{attachmentId}/extract", null, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AssignmentAttachmentReadDto>(_jsonOptions, ct))!;
    }

    /// <summary>Route body for staging a draft (PUT /assignments/{id}/questions-draft).</summary>
    private sealed record StageQuestionsDraftRequest(IReadOnlyList<NewQuestionDto> Questions);

    /// <summary>GET /assignments/{id}/questions-draft body envelope.</summary>
    private sealed record QuestionsDraftResponse(IReadOnlyList<NewQuestionDto> Questions);

    /// <summary>Marker body for the POST /…/questions-draft/confirm route (no payload).</summary>
    private sealed record ConfirmQuestionsDraftRequest;

    /// <summary>WS-C2 — the wire response of the always-200 consent-text route.</summary>
    private sealed record SignatureConsentTextResponse(string ConsentText);

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

    /// <summary>Private envelope for <c>GET /assignments/{id}/groups</c> — the Assignments
    /// API's <c>ActivityGroupRefDto</c> is declared in the Core project, which this client
    /// does not reference, so the read binds its own shape (id + name only).</summary>
    private sealed record LinkedActivityGroupResponse(Guid Id, string Name);

    /// <summary>Private envelope for the duplicate route's Created body
    /// (<c>{"id": ...}</c>) — the StudentsApiClient precedent. The create
    /// path's literal <c>ReadFromJsonAsync&lt;Guid&gt;</c> throws a
    /// <see cref="System.Text.Json.JsonException"/> on this object body; the
    /// duplicate uses this working record instead.</summary>
    private sealed record IdResponse(Guid Id);
}