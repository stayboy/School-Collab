using System.Net.Http.Json;
using System.Text.Json;

namespace SchoolCollab.Admin.Shared.Services;

/// <summary>
/// Client-side tenant record. Mirrors the server-side
/// <c>SchoolCollab.Settings.Core.DTOs.TenantDto</c> JSON shape so the admin
/// host can list tenants for the dev tenant switcher without referencing
/// Settings.Core directly. <see cref="Type"/> is the string form of
/// <see cref="SchoolCollab.Core.Tenancy.TenantType"/>.
/// </summary>
/// <param name="IsDefault">
/// Whether this tenant is the platform's designated default for the admin portal. Additive
/// with a default so existing construction sites keep compiling.
/// </param>
public record TenantDto(Guid Id, string Name, string Type, bool IsDefault = false);

/// <summary>
/// Client-side mirror of <c>SchoolCollab.Settings.Core.DTOs.UserTenantPreferenceDto</c> —
/// the signed-in operator's recorded default-tenant preference.
/// </summary>
/// <remarks>
/// Three states, matching the server: <see langword="null"/> = no preference recorded
/// (fall through to the platform default); <see cref="Guid.Empty"/> = an explicit
/// preference for the system / no-tenant context; any other value = a real tenant.
/// </remarks>
public sealed record UserTenantPreferenceDto(Guid? DefaultTenantId);

/// <summary>
/// HTTP client for the tenant registry endpoints (<c>/api/tenants</c>): the read-only list
/// used by the dev tenant switcher, the platform-default designation, and the signed-in
/// operator's own preference. Registered in <c>AddSettingsModule</c> against
/// <c>settings-api</c>.
/// </summary>
/// <remarks>
/// Deliberately registered <b>without</b> <c>TenantPropagationDelegatingHandler</c>: the
/// registry is global and cross-tenant, so stamping the selected tenant onto these calls
/// would be meaningless. The preference routes key off the caller's <c>sub</c> claim, not
/// off a tenant header.
/// </remarks>
public sealed class TenantsApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TenantDto[]> ListTenantsAsync(CancellationToken ct = default)
    {
        var result = await http.GetFromJsonAsync<TenantDto[]>("/api/tenants", JsonOptions, ct);
        return result ?? [];
    }

    /// <summary>
    /// Designates <paramref name="tenantId"/> as the platform default for the admin portal,
    /// clearing the previous holder. Throws on a non-success status (404 for an unknown tenant).
    /// </summary>
    public async Task SetDefaultTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var response = await http.PutAsync($"/api/tenants/{tenantId}/default", content: null, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Reads the signed-in operator's own preference. Never 404s — a missing preference is
    /// reported as a null <see cref="UserTenantPreferenceDto.DefaultTenantId"/>.
    /// </summary>
    public async Task<UserTenantPreferenceDto> GetMyDefaultTenantAsync(CancellationToken ct = default)
    {
        var result = await http.GetFromJsonAsync<UserTenantPreferenceDto>("/api/tenants/me/default", JsonOptions, ct);
        return result ?? new UserTenantPreferenceDto(null);
    }

    /// <summary>
    /// Records the signed-in operator's own preference. <see langword="null"/> clears it;
    /// <see cref="Guid.Empty"/> selects the system / no-tenant context. Throws on a non-success
    /// status (404 for an unknown tenant).
    /// </summary>
    public async Task SetMyDefaultTenantAsync(Guid? tenantId, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync(
            "/api/tenants/me/default",
            new SetUserDefaultTenantRequest(tenantId),
            JsonOptions,
            ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Body of <c>PUT /api/tenants/me/default</c>; mirrors the server's request record.</summary>
    private sealed record SetUserDefaultTenantRequest(Guid? TenantId);
}
