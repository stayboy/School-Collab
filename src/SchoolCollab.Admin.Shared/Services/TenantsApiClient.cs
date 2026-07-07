using System.Net.Http.Json;

namespace SchoolCollab.Admin.Shared.Services;

/// <summary>
/// Client-side tenant record. Mirrors the server-side
/// <c>SchoolCollab.Settings.Core.DTOs.TenantDto</c> JSON shape so the admin
/// host can list tenants for the dev tenant switcher without referencing
/// Settings.Core directly. <see cref="Type"/> is the string form of
/// <see cref="SchoolCollab.Core.Tenancy.TenantType"/>.
/// </summary>
public record TenantDto(Guid Id, string Name, string Type);

/// <summary>
/// HTTP client for the read-only tenant registry endpoint
/// (<c>GET /api/tenants</c>). Used by the dev tenant switcher to populate its
/// dropdown. Registered in <c>AddSettingsModule</c> against <c>settings-api</c>.
/// </summary>
public sealed class TenantsApiClient(HttpClient http)
{
    public async Task<TenantDto[]> ListTenantsAsync(CancellationToken ct = default)
    {
        var result = await http.GetFromJsonAsync<TenantDto[]>("/api/tenants", ct);
        return result ?? [];
    }
}