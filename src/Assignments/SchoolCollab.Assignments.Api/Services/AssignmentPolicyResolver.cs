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
/// </summary>
public sealed class AssignmentPolicyResolver(
    IHttpClientFactory httpClientFactory,
    ILogger<AssignmentPolicyResolver> logger) : IAssignmentPolicyResolver
{
    private static readonly EffectiveAssignmentPolicyResolver _merger = new();

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
            var response = await settings.GetAsync("/api/settings/assignment-policy", ct);
            // 204 = no policy row configured yet ⇒ nothing set (built-in defaults apply).
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<TenantAssignmentPolicyDto>(ct);
            return dto is null ? null : ToFields(dto);
        }
        catch (HttpRequestException ex)
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
            var response = await students.GetAsync(
                $"students/grade-levels/{gradeLevelId}/assignment-policy", ct);
            // 204 (no override) ⇒ null ⇒ inherit the tenant default.
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<GradeAssignmentPolicyDto>(ct);
            return dto is null ? null : ToFields(dto);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to resolve grade assignment policy for {GradeLevelId}", gradeLevelId);
            return null;
        }
    }

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
