using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SchoolCollab.Admin.Shared.Services;

/// <summary>
/// Mirror of <see cref="SchoolCollab.Config.Core.DTOs.FlagKindDto"/> so the
/// shared admin client can deserialize the flag kind without taking a reference
/// on Config.Core.
/// </summary>
public enum FlagKindDto
{
    Boolean = 0,
    String = 1,
}

public record FeatureFlagDto(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    FlagKindDto Kind,
    string? Value,
    bool IsEnabled,
    bool IsArchived,
    bool IsDeleted,
    int OverrideCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record TenantFlagOverrideDto(
    Guid Id,
    Guid TenantId,
    Guid FeatureFlagId,
    bool? IsEnabled,
    string? Value,
    string Reason,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record FlagAuditEntryDto(
    Guid Id,
    Guid? TenantId,
    Guid FeatureFlagId,
    string FeatureFlagKey,
    string ChangeKind,
    bool? PreviousIsEnabled,
    bool? NewIsEnabled,
    string? Reason,
    string ActorId,
    string ActorDisplayName,
    DateTimeOffset OccurredAt);

public record CreateFlagRequest(string Key, string Name, string? Description, bool IsEnabled, string Reason);
public record UpdateFlagRequest(string Name, string? Description, string Reason);
public record SetEnabledRequest(bool IsEnabled, string Reason);
public record ReasonRequest(string Reason);
public record UpsertOverrideRequest(bool? IsEnabled, string? Value, string Reason, DateTimeOffset? EffectiveFrom, DateTimeOffset? EffectiveTo);

/// <summary>
/// HTTP client for the central Config service, used by the unified admin host's
/// Config Flags pages. Mirrors <see cref="CodedValuesApiClient"/>: record-based
/// DTOs declared here (independent of the Config bounded-context Core) so
/// <see cref="SchoolCollab.Admin.Shared"/> does not need a Config.Core reference.
/// </summary>
public sealed class ConfigFlagsApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<FlagKindDto>() }
    };

    public async Task<FeatureFlagDto[]> ListAsync(string? search, bool includeArchived, CancellationToken ct = default)
    {
        var url = "/api/config/flags";
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search)}");
        if (includeArchived) query.Add("includeArchived=true");
        if (query.Count > 0) url += "?" + string.Join("&", query);

        var result = await http.GetFromJsonAsync<FeatureFlagDto[]>(url, JsonOptions, ct);
        return result ?? [];
    }

    public async Task<FeatureFlagDto?> GetAsync(string key, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/api/config/flags/{Uri.EscapeDataString(key)}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FeatureFlagDto>(JsonOptions, ct);
    }

    public async Task<Guid> CreateAsync(CreateFlagRequest req, CancellationToken ct = default)
    {
        var resp = await http.PostAsJsonAsync("/api/config/flags", req, JsonOptions, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<CreateFlagResponse>(JsonOptions, ct);
        return body?.Id ?? Guid.Empty;
    }

    public async Task UpdateAsync(string key, UpdateFlagRequest req, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"/api/config/flags/{Uri.EscapeDataString(key)}", req, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetEnabledAsync(string key, SetEnabledRequest req, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"/api/config/flags/{Uri.EscapeDataString(key)}/enabled", req, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task ArchiveAsync(string key, ReasonRequest req, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync($"/api/config/flags/{Uri.EscapeDataString(key)}/archive", req, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task UnarchiveAsync(string key, ReasonRequest req, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync($"/api/config/flags/{Uri.EscapeDataString(key)}/unarchive", req, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(string key, string reason, CancellationToken ct = default)
    {
        var resp = await http.DeleteAsync($"/api/config/flags/{Uri.EscapeDataString(key)}?reason={Uri.EscapeDataString(reason)}", ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task RecoverAsync(string key, ReasonRequest req, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync($"/api/config/flags/{Uri.EscapeDataString(key)}/recover", req, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<TenantFlagOverrideDto[]> ListOverridesAsync(string key, CancellationToken ct = default)
    {
        var result = await http.GetFromJsonAsync<TenantFlagOverrideDto[]>($"/api/config/flags/{Uri.EscapeDataString(key)}/overrides", JsonOptions, ct);
        return result ?? [];
    }

    public async Task<TenantFlagOverrideDto?> UpsertOverrideAsync(string key, Guid tenantId, UpsertOverrideRequest req, CancellationToken ct = default)
    {
        var resp = await http.PutAsJsonAsync($"/api/config/flags/{Uri.EscapeDataString(key)}/overrides/{tenantId}", req, JsonOptions, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<TenantFlagOverrideDto>(JsonOptions, ct);
    }

    public async Task DeleteOverrideAsync(string key, Guid tenantId, string reason, CancellationToken ct = default)
    {
        var resp = await http.DeleteAsync($"/api/config/flags/{Uri.EscapeDataString(key)}/overrides/{tenantId}?reason={Uri.EscapeDataString(reason)}", ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<FlagAuditEntryDto[]> ListAuditAsync(string? key, Guid? tenantId, DateTimeOffset? from, DateTimeOffset? to, int skip, int take, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(key)) query.Add($"key={Uri.EscapeDataString(key)}");
        if (tenantId.HasValue) query.Add($"tenantId={tenantId.Value}");
        if (from.HasValue) query.Add($"from={Uri.EscapeDataString(from.Value.ToString("O"))}");
        if (to.HasValue) query.Add($"to={Uri.EscapeDataString(to.Value.ToString("O"))}");
        query.Add($"skip={skip}");
        query.Add($"take={take}");
        var url = "/api/config/audit?" + string.Join("&", query);

        var result = await http.GetFromJsonAsync<FlagAuditEntryDto[]>(url, JsonOptions, ct);
        return result ?? [];
    }

    public record CreateFlagResponse(Guid Id);
}
