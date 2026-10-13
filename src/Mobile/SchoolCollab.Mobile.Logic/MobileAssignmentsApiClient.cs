using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Mobile.Logic;

/// <summary>
/// The assignments slice's typed API client — the <b>twelve</b> methods the mobile assignments list
/// and detail screens call, mirrored method-for-method from the server-side
/// <c>Assignments.Application.Services.AssignmentsApiClient</c> (same route, verb, body and log
/// semantics) and touching only <c>Assignments.Contracts</c> DTOs plus the BCL.
/// </summary>
/// <remarks>
/// <para>
/// A slice-scoped client exists instead of the 44-method server client because the head must not
/// reference <c>Assignments.Application</c>: that project's closure carries a
/// <c>Microsoft.AspNetCore.App</c> FrameworkReference through <c>Students.Application</c> — the very
/// carrier that broke the head's <c>net10.0-android</c> build before L2 removed it. Two method families
/// are deliberately absent and recorded in the L4 round doc: <c>GetEffectivePolicyAsync</c> (its return
/// type, <c>SchoolCollab.Core.AssignmentPolicies.EffectiveAssignmentPolicy</c>, is pinned to the
/// framework-carrying <c>Core</c> and has no caller in this slice) and the authoring / review / grade /
/// duplicate methods (OD-2, OD-3).
/// </para>
/// <para>
/// Base addresses are configuration, never C# literals (spec D8): every path below is relative and is
/// appended to the configured <see cref="MobileApiOptions.AssignmentsApi"/> address by
/// <see cref="MobileAssignmentsClientRegistration.AddMobileAssignmentsApiClient"/>.
/// </para>
/// </remarks>
public sealed class MobileAssignmentsApiClient(HttpClient http, ILogger<MobileAssignmentsApiClient> logger)
{
    /// <summary>
    /// The string-enum converter set for exactly the enums this slice's payloads carry — the slice's
    /// mirror of the server client's set. A missing converter is not cosmetic: the API host registers
    /// these enums as names (<c>src/Assignments/SchoolCollab.Assignments.Api/Program.cs:24-66</c>), so a
    /// value that arrives as <c>"published"</c> or <c>"awaitingSignature"</c> deserializes only when its
    /// converter is registered here.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter<AssignmentTypeDto>(),
            new JsonStringEnumConverter<AssignmentStatusDto>(),
            new JsonStringEnumConverter<GradingFormatDto>(),
            new JsonStringEnumConverter<TargetAudienceTypeDto>(),
            new JsonStringEnumConverter<ApprovalStatusDto>(),
            new JsonStringEnumConverter<ReviewStateDto>(),
            new JsonStringEnumConverter<SubmissionSourceDto>(),
            new JsonStringEnumConverter<GuardianRoleDto>(),
            // Read-side tolerance, mirroring the server client: the delivery pair is registered even
            // though no slice method returns a delivery DTO today, so a later read cannot be the place
            // the wire form silently breaks.
            new JsonStringEnumConverter<NotificationKindDto>(),
            new JsonStringEnumConverter<ContactOwnerTypeDto>(),
            new JsonStringEnumConverter<ContactChannelDto>(),
            // SubmissionDetailDto.SignOffState is non-nullable and always present on the submission
            // wire, so without this converter every GetSubmissionAsync call would throw JsonException.
            new JsonStringEnumConverter<SignOffStateDto>(),
        },
    };

    /// <summary>
    /// Lists assignments, optionally filtered by status — <c>GET /assignments[?status=&lt;name&gt;]</c>.
    /// The status travels as its enum name (the server client's shape); <see langword="null"/> lists
    /// every status.
    /// </summary>
    public async Task<AssignmentSummaryDto[]?> ListAsync(
        AssignmentStatusDto? status = null, CancellationToken ct = default)
    {
        var url = "/assignments";
        if (status.HasValue)
        {
            url += $"?status={status.Value}";
        }

        logger.LogDebug("Listing assignments with status filter {Status}", status);
        var result = await http.GetFromJsonAsync<AssignmentSummaryDto[]>(url, JsonOptions, ct);
        logger.LogInformation("Listed {Count} assignments", result?.Length ?? 0);
        return result;
    }

    /// <summary>
    /// Reads one assignment — <c>GET /assignments/{id}</c>. A 404 is an expected answer (deleted or
    /// out-of-scope assignment) and returns <see langword="null"/> rather than throwing.
    /// </summary>
    public async Task<AssignmentSummaryDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        logger.LogDebug("Getting assignment {AssignmentId}", id);
        var response = await http.GetAsync($"/assignments/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning("Assignment {AssignmentId} not found", id);
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AssignmentSummaryDto>(JsonOptions, ct);
    }

    /// <summary>Publishes a draft or scheduled assignment — <c>POST /assignments/{id}/publish</c>.</summary>
    public async Task PublishAsync(Guid id, CancellationToken ct = default)
    {
        logger.LogInformation("Publishing assignment {AssignmentId}", id);
        (await http.PostAsJsonAsync($"/assignments/{id}/publish", (PublishAssignmentRequest?)null, JsonOptions, ct))
            .EnsureSuccessStatusCode();
    }

    /// <summary>Returns a published assignment to draft — <c>POST /assignments/{id}/unpublish</c>.</summary>
    public async Task UnpublishAsync(Guid id, CancellationToken ct = default)
    {
        logger.LogInformation("Unpublishing assignment {AssignmentId}", id);
        (await http.PostAsync($"/assignments/{id}/unpublish", null, ct)).EnsureSuccessStatusCode();
    }

    /// <summary>Closes an assignment — <c>POST /assignments/{id}/close</c>.</summary>
    public async Task CloseAsync(Guid id, CancellationToken ct = default)
    {
        logger.LogInformation("Closing assignment {AssignmentId}", id);
        (await http.PostAsync($"/assignments/{id}/close", null, ct)).EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Submits a draft assignment for approval — <c>POST /assignments/{id}/submit-for-approval</c>.
    /// </summary>
    public async Task SubmitForApprovalAsync(Guid id, CancellationToken ct = default)
    {
        logger.LogInformation("Submitting assignment {AssignmentId} for approval", id);
        (await http.PostAsync($"/assignments/{id}/submit-for-approval", null, ct)).EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Approves a pending assignment — <c>POST /assignments/{id}/approve</c> with
    /// <see cref="ApproveAssignmentRequest"/>. <paramref name="approverId"/> is server-ignored when the
    /// principal carries a <c>teacher_id</c> claim (the principal wins).
    /// </summary>
    public async Task ApproveAsync(Guid id, Guid approverId, CancellationToken ct = default)
    {
        logger.LogInformation("Approving assignment {AssignmentId}", id);
        (await http.PostAsJsonAsync($"/assignments/{id}/approve", new ApproveAssignmentRequest(approverId), JsonOptions, ct))
            .EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Rejects a pending assignment — <c>POST /assignments/{id}/reject</c> with
    /// <see cref="RejectAssignmentRequest"/>. <paramref name="approverId"/> has the same
    /// principal-wins posture as <see cref="ApproveAsync"/>.
    /// </summary>
    public async Task RejectAsync(Guid id, Guid approverId, CancellationToken ct = default)
    {
        logger.LogInformation("Rejecting assignment {AssignmentId}", id);
        (await http.PostAsJsonAsync($"/assignments/{id}/reject", new RejectAssignmentRequest(approverId), JsonOptions, ct))
            .EnsureSuccessStatusCode();
    }

    /// <summary>Deletes an assignment — <c>DELETE /assignments/{id}</c>.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        logger.LogInformation("Deleting assignment {AssignmentId}", id);
        (await http.DeleteAsync($"/assignments/{id}", ct)).EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Reads an assignment's publish recipients — <c>GET /assignments/{id}/recipients</c>. A 404
    /// returns <see langword="null"/>.
    /// </summary>
    public async Task<AssignmentRecipientDto[]?> GetRecipientsAsync(Guid id, CancellationToken ct = default)
    {
        logger.LogDebug("Getting recipients for assignment {AssignmentId}", id);
        var response = await http.GetAsync($"/assignments/{id}/recipients", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AssignmentRecipientDto[]>(JsonOptions, ct);
    }

    /// <summary>
    /// Lists an assignment's submissions — <c>GET /assignments/{id}/submissions</c>.
    /// </summary>
    public async Task<SubmissionForReviewDto[]?> ListSubmissionsAsync(Guid id, CancellationToken ct = default)
    {
        logger.LogDebug("Listing submissions for assignment {AssignmentId}", id);
        var result = await http.GetFromJsonAsync<SubmissionForReviewDto[]>(
            $"/assignments/{id}/submissions", JsonOptions, ct);
        logger.LogInformation("Listed {Count} submissions", result?.Length ?? 0);
        return result;
    }

    /// <summary>
    /// Reads one student's submission with its version history — <c>GET
    /// /assignments/{id}/students/{studentId}/submission</c>. A 404 returns <see langword="null"/>.
    /// </summary>
    public async Task<SubmissionDetailDto?> GetSubmissionAsync(
        Guid id, Guid studentId, CancellationToken ct = default)
    {
        logger.LogDebug("Getting submission for assignment {AssignmentId} / student {StudentId}", id, studentId);
        var response = await http.GetAsync($"/assignments/{id}/students/{studentId}/submission", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SubmissionDetailDto>(JsonOptions, ct);
    }
}
