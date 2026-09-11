using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>
/// HTTP-backed <see cref="ISignatureDefaultResolver"/> (WS-C1 / spec §7 Q1).
/// Reads the tenant-global default from the Settings API and the optional per-grade
/// override from the Students API, then resolves the effective value (grade override
/// wins; null override inherits the tenant default; an unset tenant default is
/// <see langword="false"/>). Named clients <c>settings-api</c> + <c>students-api</c>
/// resolve through Aspire service discovery. Any fetch failure degrades gracefully
/// (tenant fetch ⇒ <see langword="false"/>, grade fetch ⇒ inherit), matching the
/// fail-open best-effort posture of
/// <see cref="NotificationPolicyResolver"/> — the create wizard is never blocked.
/// </summary>
public sealed class SignatureDefaultResolver(
    IHttpClientFactory httpClientFactory,
    ILogger<SignatureDefaultResolver> logger) : ISignatureDefaultResolver
{
    public async Task<bool> ResolveRequiresSignatureDefaultAsync(
        Guid? gradeLevelId, CancellationToken ct = default)
    {
        var tenantDefault = await FetchTenantDefaultAsync(ct);

        bool? gradeOverride = null;
        if (gradeLevelId.HasValue)
            gradeOverride = await FetchGradeOverrideAsync(gradeLevelId.Value, ct);

        // Effective = grade override ?? tenant default (fail-open resolution):
        // a null grade override inherits the tenant default.
        return gradeOverride ?? tenantDefault;
    }

    private async Task<bool> FetchTenantDefaultAsync(CancellationToken ct)
    {
        var settings = httpClientFactory.CreateClient("settings-api");
        try
        {
            var response = await settings.GetAsync("/api/settings/assignment-policy", ct);
            // 204 = no row configured yet ⇒ caller falls back to false.
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return false;
            }
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<TenantAssignmentPolicyDto>(ct);
            return dto?.RequiresSignatureDefault ?? false;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to resolve tenant assignment policy; defaulting to false");
            return false;
        }
    }

    private async Task<bool?> FetchGradeOverrideAsync(Guid gradeLevelId, CancellationToken ct)
    {
        var students = httpClientFactory.CreateClient("students-api");
        try
        {
            var response = await students.GetAsync(
                $"students/grade-levels/{gradeLevelId}/assignment-policy", ct);
            // 204 (no override) ⇒ null ⇒ inherit the tenant default.
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return null;
            }
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<GradeAssignmentPolicyDto>(ct);
            return dto?.RequiresSignatureDefault;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to resolve grade assignment policy for {GradeLevelId}", gradeLevelId);
            return null;
        }
    }
}