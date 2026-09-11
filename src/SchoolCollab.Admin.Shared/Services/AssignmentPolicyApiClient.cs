using System.Net.Http.Json;

namespace SchoolCollab.Admin.Shared.Services;

// Mirrors SchoolCollab.Settings.Core.DTOs.TenantAssignmentPolicyDto + the PUT
// request shape from the settings assignment-policy endpoint. Re-declared here
// (rather than referencing Settings.Core) to keep Admin.Shared free of a Settings
// reference, matching the CodedValues/Tenants/NotificationPolicy clients.

/// <summary>Tenant-global default guardian-signature policy (WS-C1 / spec §7 Q1).</summary>
public sealed record TenantAssignmentPolicyDto(bool RequiresSignatureDefault);

/// <summary>Upsert request for the tenant-global guardian-signature default.</summary>
public sealed record UpsertAssignmentPolicyRequest(bool RequiresSignatureDefault);

/// <summary>
/// Admin client for the per-tenant global-default guardian-signature policy
/// (WS-C1 / spec §7 Q1) at <c>/api/settings/assignment-policy</c>.
/// Base address <c>https+http://settings-api</c> is configured in DI. The
/// resolved entity is a STRICT tenant entity — the registered
/// <c>TenantPropagationDelegatingHandler</c> carries the selected tenant or the
/// read/write hits the wrong tenant (the historical symptom class documented in
/// ModuleServices.cs).
/// </summary>
public sealed class AssignmentPolicyApiClient(HttpClient http)
{
    public async Task<TenantAssignmentPolicyDto?> GetAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("/api/settings/assignment-policy", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TenantAssignmentPolicyDto>(cancellationToken: ct);
    }

    public async Task UpsertAsync(bool requiresSignatureDefault, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync(
            "/api/settings/assignment-policy",
            new UpsertAssignmentPolicyRequest(requiresSignatureDefault), ct);
        response.EnsureSuccessStatusCode();
    }
}