using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>
/// HTTP-backed <see cref="IAssignmentPolicyResolver"/>
/// (<c>documents/solution/assignment-policy-fields.md</c> §5; WS-C1 / spec §7 Q1). Reads the
/// tenant-global default from the Settings API and the optional per-grade override from the
/// Students API, then merges them with the shared
/// <see cref="EffectiveAssignmentPolicyResolver"/>. Named clients <c>settings-api</c> +
/// <c>students-api</c> resolve through Aspire service discovery.
///
/// <para>Any fetch failure degrades gracefully to "nothing configured" (unset tenant ⇒
/// <c>null</c>; failed / absent grade row ⇒ inherit), so the built-in defaults
/// (<see cref="SignatureRequirementMode.Disabled"/>, no approval requirement, uncapped) apply and
/// neither the create wizard nor a publish is blocked by policy — the fail-open posture of the
/// resolver it replaces. For the two resolved write-seam fields that posture is explicit: a
/// degraded fetch leaves <c>MandatoryReview</c> <see langword="null"/> (the write seam keeps today's
/// mandatory-review default) and <c>ArchiveGraceDays</c> <see langword="null"/> (the write seam keeps
/// the built-in 30-day retention floor) — degradation never turns a stricter policy ON and never
/// silently zeroes the archive window.</para>
///
/// <para><b>Q49 hardening (round notification-templates-b2a).</b> Both fetches carry an explicit
/// 5-second wall clock and widen to the template resolver's fail-open contract (timeout, non-JSON
/// 200, 5xx): the narrow <c>HttpRequestException</c>-only catch this resolver started with let a
/// timeout, a gateway HTML page or an unset base address escape as a thrown exception and abort the
/// caller. Genuine cancellation still propagates — a cancelled request must never be reported as
/// "nothing configured".</para>
/// </summary>
public sealed class AssignmentPolicyResolver(
    IHttpClientFactory httpClientFactory,
    ILogger<AssignmentPolicyResolver> logger) : IAssignmentPolicyResolver
{
    private static readonly EffectiveAssignmentPolicyResolver _merger = new();

    /// <summary>The cross-context read wall clock — a hung Settings/Students call must not stall the caller.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task<EffectiveAssignmentPolicy> ResolveAsync(
        Guid? gradeLevelId, CancellationToken ct = default)
    {
        var tenantDefault = await FetchTenantDefaultAsync(ct);

        AssignmentPolicyFields? gradeOverride = null;
        if (gradeLevelId.HasValue)
            gradeOverride = await FetchGradeOverrideAsync(gradeLevelId.Value, ct);

        return _merger.Resolve(tenantDefault, gradeOverride);
    }

    private async Task<AssignmentPolicyFields?> FetchTenantDefaultAsync(CancellationToken ct)
    {
        var settings = httpClientFactory.CreateClient("settings-api");
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(Timeout);

            var response = await settings.GetAsync("/api/settings/assignment-policy", timeoutCts.Token);
            // 204 = no policy row configured yet ⇒ nothing set (built-in defaults apply).
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<TenantAssignmentPolicyDto>(timeoutCts.Token);
            return dto is null ? null : ToFields(dto);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Timed out resolving the tenant assignment policy; using built-in defaults");
            return null;
        }
        catch (OperationCanceledException)
        {
            throw; // genuine caller cancellation propagates
        }
        catch (Exception ex) when (IsDegradable(ex))
        {
            logger.LogWarning(ex, "Failed to resolve tenant assignment policy; using built-in defaults");
            return null;
        }
    }

    private async Task<AssignmentPolicyFields?> FetchGradeOverrideAsync(Guid gradeLevelId, CancellationToken ct)
    {
        var students = httpClientFactory.CreateClient("students-api");
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(Timeout);

            var response = await students.GetAsync(
                $"students/grade-levels/{gradeLevelId}/assignment-policy", timeoutCts.Token);
            // 204 (no override) ⇒ null ⇒ inherit the tenant default.
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<GradeAssignmentPolicyDto>(timeoutCts.Token);
            return dto is null ? null : ToFields(dto);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Timed out resolving grade assignment policy for {GradeLevelId}", gradeLevelId);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw; // genuine caller cancellation propagates
        }
        catch (Exception ex) when (IsDegradable(ex))
        {
            logger.LogWarning(ex, "Failed to resolve grade assignment policy for {GradeLevelId}", gradeLevelId);
            return null;
        }
    }

    /// <summary>
    /// The fail-open exception set (Q49, the template resolver's contract): a transport failure, a
    /// 200 carrying a non-JSON body ("garbage" — <see cref="System.Text.Json.JsonException"/>, or
    /// <see cref="NotSupportedException"/> from a content-type mismatch) and an unset base address
    /// (<see cref="InvalidOperationException"/>) all degrade to "nothing configured" rather than
    /// escaping. Genuine cancellation is handled before this filter, never here.
    /// </summary>
    private static bool IsDegradable(Exception ex) => ex is HttpRequestException
        or InvalidOperationException
        or System.Text.Json.JsonException
        or NotSupportedException;

    private static AssignmentPolicyFields ToFields(TenantAssignmentPolicyDto dto) => new()
    {
        SignatureRequirement = dto.SignatureRequirement,
        RequiresApprovalBeforePublish = dto.RequiresApprovalBeforePublish,
        MaxPrimaryContacts = dto.MaxPrimaryContacts,
        MaxCopyContacts = dto.MaxCopyContacts,
        MandatoryReview = dto.MandatoryReview,
        ArchiveGraceDays = dto.ArchiveGraceDays,
    };

    private static AssignmentPolicyFields ToFields(GradeAssignmentPolicyDto dto) => new()
    {
        SignatureRequirement = dto.SignatureRequirement,
        RequiresApprovalBeforePublish = dto.RequiresApprovalBeforePublish,
        MaxPrimaryContacts = dto.MaxPrimaryContacts,
        MaxCopyContacts = dto.MaxCopyContacts,
        MandatoryReview = dto.MandatoryReview,
        ArchiveGraceDays = dto.ArchiveGraceDays,
    };
}
