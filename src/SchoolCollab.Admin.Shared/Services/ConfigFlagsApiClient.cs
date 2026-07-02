using System.Net.Http.Json;

namespace SchoolCollab.Admin.Shared.Services;

public record FeatureFlagDto(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    string Kind,
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
public record UpsertOverrideRequest(bool? IsEnabled, string Reason, DateTimeOffset? EffectiveFrom, DateTimeOffset? EffectiveTo);

/// <summary>
/// HTTP client for the central Config service, used by the unified admin host's
/// Config Flags pages. Mirrors <see cref="CodedValuesApiClient"/>: record-based
/// DTOs declared here (independent of the Config bounded-context Core) so
/// <see cref="SchoolCollab.Admin.Shared"/> does not need a Config.Core reference.
/// </summary>
public sealed class ConfigFlagsApiClient(HttpClient http)
{
    public Task<FeatureFlagDto[]> ListAsync(string? search, bool includeArchived, CancellationToken ct = default)
    {
        var url = "/api/config/flags";
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search)}");
        if (includeArchived) query.Add("includeArchived=true");
        if (query.Count > 0) url += "?" + string.Join("&", query);
        return http.GetFromJsonAsync<FeatureFlagDto[]>(url, ct).ContinueWith(t => t.Result ?? Array.Empty<FeatureFlagDto>(), ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    public Task<FeatureFlagDto?> GetAsync(string key, CancellationToken ct = default) =>
        http.GetFromJsonAsync<FeatureFlagDto>($"/api/config/flags/{Uri.EscapeDataString(key)}", ct);

    public async Task<Guid> CreateAsync(CreateFlagRequest req, CancellationToken ct = default)
    {
        var resp = await http.PostAsJsonAsync("/api/config/flags", req, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<CreateFlagResponse>(ct);
        return body?.Id ?? Guid.Empty;
    }

    public Task UpdateAsync(string key, UpdateFlagRequest req, CancellationToken ct = default) =>
        http.PutAsJsonAsync($"/api/config/flags/{Uri.EscapeDataString(key)}", req, ct).ContinueWith(t => t.Result.EnsureSuccessStatusCode(), ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);

    public Task SetEnabledAsync(string key, SetEnabledRequest req, CancellationToken ct = default) =>
        http.PutAsJsonAsync($"/api/config/flags/{Uri.EscapeDataString(key)}/enabled", req, ct).ContinueWith(t => t.Result.EnsureSuccessStatusCode(), ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);

    public Task ArchiveAsync(string key, ReasonRequest req, CancellationToken ct = default) =>
        http.PostAsJsonAsync($"/api/config/flags/{Uri.EscapeDataString(key)}/archive", req, ct).ContinueWith(t => t.Result.EnsureSuccessStatusCode(), ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);

    public Task UnarchiveAsync(string key, ReasonRequest req, CancellationToken ct = default) =>
        http.PostAsJsonAsync($"/api/config/flags/{Uri.EscapeDataString(key)}/unarchive", req, ct).ContinueWith(t => t.Result.EnsureSuccessStatusCode(), ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);

    public async Task DeleteAsync(string key, string reason, CancellationToken ct = default)
    {
        var resp = await http.DeleteAsync($"/api/config/flags/{Uri.EscapeDataString(key)}?reason={Uri.EscapeDataString(reason)}", ct);
        resp.EnsureSuccessStatusCode();
    }

    public Task RecoverAsync(string key, ReasonRequest req, CancellationToken ct = default) =>
        http.PostAsJsonAsync($"/api/config/flags/{Uri.EscapeDataString(key)}/recover", req, ct).ContinueWith(t => t.Result.EnsureSuccessStatusCode(), ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);

    public Task<TenantFlagOverrideDto[]> ListOverridesAsync(string key, CancellationToken ct = default) =>
        http.GetFromJsonAsync<TenantFlagOverrideDto[]>($"/api/config/flags/{Uri.EscapeDataString(key)}/overrides", ct).ContinueWith(t => t.Result ?? Array.Empty<TenantFlagOverrideDto>(), ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);

    public async Task<TenantFlagOverrideDto?> UpsertOverrideAsync(string key, Guid tenantId, UpsertOverrideRequest req, CancellationToken ct = default)
    {
        var resp = await http.PutAsJsonAsync($"/api/config/flags/{Uri.EscapeDataString(key)}/overrides/{tenantId}", req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<TenantFlagOverrideDto>(ct);
    }

    public async Task DeleteOverrideAsync(string key, Guid tenantId, string reason, CancellationToken ct = default)
    {
        var resp = await http.DeleteAsync($"/api/config/flags/{Uri.EscapeDataString(key)}/overrides/{tenantId}?reason={Uri.EscapeDataString(reason)}", ct);
        resp.EnsureSuccessStatusCode();
    }

    public Task<FlagAuditEntryDto[]> ListAuditAsync(string? key, Guid? tenantId, DateTimeOffset? from, DateTimeOffset? to, int skip, int take, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(key)) query.Add($"key={Uri.EscapeDataString(key)}");
        if (tenantId.HasValue) query.Add($"tenantId={tenantId.Value}");
        if (from.HasValue) query.Add($"from={Uri.EscapeDataString(from.Value.ToString("O"))}");
        if (to.HasValue) query.Add($"to={Uri.EscapeDataString(to.Value.ToString("O"))}");
        query.Add($"skip={skip}");
        query.Add($"take={take}");
        var url = "/api/config/audit?" + string.Join("&", query);
        return http.GetFromJsonAsync<FlagAuditEntryDto[]>(url, ct).ContinueWith(t => t.Result ?? Array.Empty<FlagAuditEntryDto>(), ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    public record CreateFlagResponse(Guid Id);
}