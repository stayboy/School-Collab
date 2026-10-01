using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SchoolCollab.Core.AssignmentPolicies;

namespace SchoolCollab.Admin.Shared.Services;

// Mirrors SchoolCollab.Settings.Core.DTOs.TenantAssignmentPolicyDto + the PUT
// request shape from the settings assignment-policy endpoint. Re-declared here
// (rather than referencing Settings.Core) to keep Admin.Shared free of a Settings
// reference, matching the CodedValues/Tenants/NotificationPolicy clients.

/// <summary>
/// Tenant-global default assignment policy (WS-C1 / spec §7 Q1;
/// <c>documents/solution/assignment-policy-fields.md</c> §4). Every field is nullable: null means
/// the tenant has not set that field (the built-in default applies).
/// </summary>
public sealed record TenantAssignmentPolicyDto(
    SignatureRequirementMode? SignatureRequirement,
    bool? RequiresApprovalBeforePublish,
    int? MaxPrimaryContacts,
    int? MaxCopyContacts)
{
    /// <summary>
    /// <b>Legacy-input compatibility (Round A only).</b> Derived from
    /// <see cref="SignatureRequirement"/> (<c>Optional</c>/<c>Mandatory → true</c>,
    /// <c>Disabled</c>/unset → false) so the shipped <c>GradeSignaturePolicyEditor</c> switch keeps
    /// compiling and showing the resolved state; never serialized — the wire carries the field set
    /// only. Round B deletes this member together with the retired editor.
    /// </summary>
    [JsonIgnore]
    public bool RequiresSignatureDefault =>
        SignatureRequirement is not null and not SignatureRequirementMode.Disabled;
}

/// <summary>Upsert request for the tenant-global assignment-policy default (all-nullable: a null
/// field clears it to "unset").</summary>
public sealed record UpsertAssignmentPolicyRequest(
    SignatureRequirementMode? SignatureRequirement,
    bool? RequiresApprovalBeforePublish,
    int? MaxPrimaryContacts,
    int? MaxCopyContacts);

/// <summary>
/// <b>Legacy request shape (Round A only).</b> The pre-widening PUT body was the single
/// input-only boolean <c>requiresSignatureDefault</c>; the shipped Admin editor still sends it and
/// the settings endpoint maps it (<c>true → Optional</c>, <c>false → Disabled</c>). Round B deletes
/// this record with the retired editor.
/// </summary>
public sealed record LegacyUpsertAssignmentPolicyRequest(bool RequiresSignatureDefault);

/// <summary>
/// Admin client for the per-tenant global-default assignment policy
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

    /// <summary>Replaces the tenant default with the supplied field set (null = unset).</summary>
    public async Task UpsertAsync(UpsertAssignmentPolicyRequest req, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync(
            "/api/settings/assignment-policy", req, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// <b>Legacy overload (Round A only)</b> — sends the pre-widening boolean body the shipped
    /// editor still uses. Round B deletes it with the editor.
    /// </summary>
    public async Task UpsertAsync(bool requiresSignatureDefault, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync(
            "/api/settings/assignment-policy",
            new LegacyUpsertAssignmentPolicyRequest(requiresSignatureDefault), ct);
        response.EnsureSuccessStatusCode();
    }
}
