using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace SchoolCollab.Students.Core.Services;

/// <summary>
/// Lightweight CodedValue DTO used by the Students module to call the Settings
/// REST API for strand validation. Mirrors the contract from
/// <c>SchoolCollab.Admin.Shared.Services.CodedValueDto</c>.
/// </summary>
public record StreamCodedValueDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    Guid? ParentId,
    string? ParentCode,
    bool IsDisabled,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<StreamAttributeDto> Attributes,
    bool IsOverridden = false,
    string? DefaultName = null,
    string? DefaultCode = null);

public record StreamAttributeDto(string Key, string Value);

/// <summary>
/// HTTP client for calling the Settings Coded Values REST API from Students
/// handlers. Used to validate grade-strand references (cross-module).
/// </summary>
public interface ICodedValuesApiClient
{
    /// <summary>
    /// Fetches a coded value by its ID. Returns <c>null</c> if not found.
    /// </summary>
    Task<StreamCodedValueDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Fetches the children of a parent coded value by the parent's <b>code</b>.
    /// Backed by the Settings
    /// <c>GET /api/coded-values/by-parent?parentCode=…</c> endpoint, which is
    /// <b>override-resolving</b> (tenant overrides applied, <c>IsOverridden</c> /
    /// <c>DefaultName</c> populated) and tenant-scoped in its cache key — the
    /// stream read path needs both, so this is deliberately not the
    /// tenant-independent <c>by-ids</c> endpoint.
    /// </summary>
    Task<StreamCodedValueDto[]> GetChildrenByParentCodeAsync(string parentCode, CancellationToken ct = default);
}

public sealed class CodedValuesApiClient(HttpClient http, ILogger<CodedValuesApiClient>? logger = null) : ICodedValuesApiClient
{
    public async Task<StreamCodedValueDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        // GET is idempotent → one immediate retry is safe. This heals the
        // IHttpClientFactory handler-lifetime rotation race where the cached
        // pipeline hands a request a pooled connection whose NetworkStream was
        // just disposed (ObjectDisposedException) — the mid-flight enroll
        // failure "Cannot access a disposed object … NetworkStream". Standard
        // resilience does NOT classify ObjectDisposedException as retryable,
        // so without this the whole enroll fails on a transient artifact.
        try
        {
            return await GetByIdCoreAsync(id, ct);
        }
        catch (ObjectDisposedException ex)
        {
            logger?.LogWarning(ex,
                "CodedValuesApiClient: GET /api/coded-values/{Id} hit a disposed pooled " +
                "connection (handler rotation race); retrying once on a fresh request", id);
            return await GetByIdCoreAsync(id, ct);
        }
    }

    private async Task<StreamCodedValueDto?> GetByIdCoreAsync(Guid id, CancellationToken ct)
    {
        var response = await http.GetAsync($"/api/coded-values/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // A 404 here is treated as 'coded value does not exist' (→ the caller
            // throws GradeLevelNotFoundException). But a 404 from a MISROUTED
            // pipeline — e.g. this typed client's InnerHandler overwritten by
            // another named client, or service discovery pointing at the wrong
            // host — is indistinguishable from a genuine miss. Log the hop so a
            // routing corruption is visible in the operator log instead of being
            // misdiagnosed as bad data.
            logger?.LogWarning(
                "CodedValuesApiClient: GET {BaseAddress}/api/coded-values/{Id} returned 404 (treated as not-found). " +
                "If this coded value exists in settings-api, suspect pipeline misrouting — verify the " +
                "ICodedValuesApiClient registration and its TenantForwardingDelegatingHandler wiring.",
                http.BaseAddress, id);
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<StreamCodedValueDto>(ct);
    }

    public async Task<StreamCodedValueDto[]> GetChildrenByParentCodeAsync(
        string parentCode, CancellationToken ct = default)
    {
        var result = await http.GetFromJsonAsync<StreamCodedValueDto[]>(
            $"/api/coded-values/by-parent?parentCode={Uri.EscapeDataString(parentCode)}", ct);
        return result ?? [];
    }
}