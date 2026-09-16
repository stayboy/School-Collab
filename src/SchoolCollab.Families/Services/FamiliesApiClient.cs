using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.DeepLinks;

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
/// WS-F3 (ar-15-signoff-relocation, decision (b)) — the guardian identity the Families
/// host re-mints into the <c>x-deeplink-token</c> header for the guardian call family.
/// Both values come from the ar-14 landing's cookie principal
/// (<c>tenant_id</c>/<c>contact_id</c>); the guardian page resolves the pair once per
/// circuit and passes it to <see cref="FamiliesApiClient"/> per call. The body of a
/// guardian sign POST never carries a GuardianId — the API resolves the acting guardian
/// from this token.
/// </summary>
public sealed record GuardianCallerContext(Guid TenantId, Guid ContactId);

/// <summary>
/// F1 (slice 2b) — the Families surface's thin typed client over the Assignments
/// API's ward-facing endpoints (the ar-12 seams). Option B: the Families host
/// references only <c>Assignments.Contracts</c> (the cross-context boundary), so
/// this client carries the exact six calls the ward pages bind to. No admin
/// <c>Assignments.Application</c> reference, so no admin route leakage.
/// <para>WS-F3 adds the three guardian-scoped calls (the <c>/guardian</c> route group),
/// each attaching a re-minted short-TTL <c>x-deeplink-token</c> header protected with the
/// shared <see cref="DeepLinkProtector"/> + purpose (decision (a)) — no new keyring, no
/// new purpose, no OIDC dependency.</para>
/// </summary>
public sealed class FamiliesApiClient(
    HttpClient http,
    DeepLinkProtector protector,
    ILogger<FamiliesApiClient> logger)
{
    /// <summary>WS-F3 — the re-mint TTL for the guardian header token (owner decision
    /// 2026-09-16: 15 minutes). The re-mint cannot extend link validity: the API
    /// cross-checks the authoritative stored <c>AssignmentRecipient.DeepLinkExpiresAt</c>.</summary>
    public static readonly TimeSpan GuardianTokenReMintTtl = TimeSpan.FromMinutes(15);

    private readonly string _assignments = "assignments";
    private readonly string _guardian = "guardian";

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

    /// <summary>WS-E1 (ar-14-deep-links) — idempotent first-visit <c>OpenedAt</c> stamp, invoked
    /// server-side by the public deep-link landing. The Assignments API marks the recipient
    /// at most once (Sent → Viewed chain) and returns 404 for an unknown (assignment, contact)
    /// row; treated as best-effort here so a stamp failure never breaks the landing redirect.
    /// <paramref name="tenantId"/> is the token's validated payload tenant, sent explicitly as
    /// the <c>x-tenant-id</c> header on this one call so the tenant-scoped lookup resolves even
    /// when no dev tenant is selected (see <see cref="SchoolCollab.Core.Auth.TenantPropagationDelegatingHandler"/>).
    /// Bound to a short per-call deadline (~3s, P1 / ar-14) so a slow-but-reachable API cannot
    /// stall the public landing's user-visible redirect.</summary>
    public async Task MarkRecipientOpenedAsync(Guid assignmentId, Guid contactId, Guid tenantId, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/{_assignments}/{assignmentId}/recipients/{contactId}/opened");
        // Carry the payload tenant explicitly: the dev-selection propagation handler only
        // overwrites x-tenant-id when a dev tenant is selected, so an explicit header here
        // keeps the stamp tenant-correct in both dev and production.
        request.Headers.TryAddWithoutValidation("x-tenant-id", tenantId.ToString());

        // P1 (ar-14): bound the stamp to a short per-call deadline via a linked CTS, NOT a
        // global HttpClient.Timeout. The ward pages share this typed client and their data
        // calls legitimately need the default 100s window, so a global ~3s timeout would be
        // wrong for them. When the deadline elapses, SendAsync raises
        // OperationCanceledException with the CALLER's ct NOT cancelled, which the landing
        // guard classifies as a best-effort stamp failure (log-and-continue) rather than a
        // caller cancellation (which must still rethrow). The linked CTS is scoped to THIS
        // call only and disposed here — no shared/typed-client state is touched.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(3));

        // A non-success response (404 unknown recipient row, 401 in prod) is surfaced via
        // EnsureSuccessStatusCode so the landing's guard logs it as a warning instead of
        // silently dropping the OpenedAt stamp.
        var response = await http.SendAsync(request, linked.Token);
        response.EnsureSuccessStatusCode();
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

    // ── WS-F3 (ar-15-signoff-relocation): the token-gated guardian call family ──────

    /// <summary>WS-F3 — the sign-off context the guardian sign page renders (ONE call).
    /// Returns <see langword="null"/> for a 404 (unknown assignment/student); a 401/403
    /// (expired, tampered, or out-of-scope link) surfaces as
    /// <see cref="HttpRequestException"/> carrying the status for the page to render.</summary>
    public async Task<SignOffContextDto?> GetGuardianSignOffContextAsync(
        Guid assignmentId, Guid studentId, GuardianCallerContext caller, CancellationToken ct = default)
    {
        using var request = GuardianRequest(
            HttpMethod.Get,
            $"/{_guardian}/{_assignments}/{assignmentId}/students/{studentId}/sign-off",
            assignmentId, studentId, caller);

        var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForStatusAsync(response, ct);
        }
        return await response.Content.ReadFromJsonAsync<SignOffContextDto>(FamiliesJson.Options, ct);
    }

    /// <summary>WS-F3 — the guardian e-signs the ward's submission. The request body
    /// carries NO GuardianId (decision (b)): the acting guardian is resolved server-side
    /// from the header token. A 409 (already signed / wrong state / locked / not
    /// authorized) surfaces as <see cref="HttpRequestException"/> whose message is the
    /// API's problem detail, so the page can render the read-only re-entry posture.</summary>
    public async Task<SignOffStatusDto?> SubmitGuardianSignOffAsync(
        Guid assignmentId, Guid studentId, GuardianSignOffSubmissionRequest body,
        GuardianCallerContext caller, CancellationToken ct = default)
    {
        using var request = GuardianRequest(
            HttpMethod.Post,
            $"/{_guardian}/{_assignments}/{assignmentId}/students/{studentId}/sign-off",
            assignmentId, studentId, caller);
        request.Content = JsonContent.Create(body, options: FamiliesJson.Options);

        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForStatusAsync(response, ct);
        }
        return await response.Content.ReadFromJsonAsync<SignOffStatusDto>(FamiliesJson.Options, ct);
    }

    /// <summary>WS-F3 — the finalized certificate PDF (decision (e)/step 5). A plain
    /// <c>&lt;a href&gt;</c> cannot attach the header token, so the page fetches the bytes
    /// through this call and hands them to the browser-side download module (the C3
    /// orchestration, mirrored in <c>GuardianCertificateDownloadService</c>). A 404 (no
    /// signature event / no certificate) surfaces as <see cref="HttpRequestException"/>.</summary>
    public async Task<byte[]> GetGuardianCertificateAsync(
        Guid assignmentId, Guid studentId, GuardianCallerContext caller, CancellationToken ct = default)
    {
        using var request = GuardianRequest(
            HttpMethod.Get,
            $"/{_guardian}/{_assignments}/{assignmentId}/students/{studentId}/certificate",
            assignmentId, studentId, caller);

        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForStatusAsync(response, ct);
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        logger.LogInformation(
            "Guardian downloaded {Bytes} certificate bytes for assignment {AssignmentId} / student {StudentId}",
            bytes.Length, assignmentId, studentId);
        return bytes;
    }

    /// <summary>
    /// Builds a guardian-group request carrying a fresh short-TTL
    /// <c>x-deeplink-token</c> (to-verify 1): the payload is re-protected HERE, per call,
    /// with the registered <see cref="DeepLinkProtector"/> — the same keyring and
    /// <see cref="DeepLinkConstants.Purpose"/> as the ar-14 mint path, so the API can
    /// unprotect it. Assignment + ward come from the call arguments; tenant + contact from
    /// the caller's cookie principal.
    /// </summary>
    private HttpRequestMessage GuardianRequest(
        HttpMethod method, string relativeUrl, Guid assignmentId, Guid studentId, GuardianCallerContext caller)
    {
        var payload = new DeepLinkTokenPayload(
            TenantId: caller.TenantId,
            AssignmentId: assignmentId,
            ContactId: caller.ContactId,
            OwnerType: (int)ContactOwnerTypeDto.Guardian,
            Role: null,
            WardStudentId: studentId,
            ExpiresAt: DateTimeOffset.UtcNow + GuardianTokenReMintTtl);

        var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.TryAddWithoutValidation("x-deeplink-token", protector.Protect(payload));
        return request;
    }

    /// <summary>Surfaces a non-success guardian response as a typed
    /// <see cref="HttpRequestException"/> carrying the API's problem detail as the
    /// message (the page matches on it for the already-signed re-entry posture).</summary>
    private static async Task ThrowForStatusAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var detail = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(detail)
                ? $"The Assignments API returned {(int)response.StatusCode} ({response.StatusCode})."
                : detail,
            inner: null,
            statusCode: response.StatusCode);
    }
}
