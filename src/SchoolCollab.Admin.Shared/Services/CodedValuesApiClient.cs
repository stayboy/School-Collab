using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace SchoolCollab.Admin.Shared.Services;

public record CodedValueAttributeDto(string Key, string Value);

public enum AttributeDataType
{
    Text = 0,
    Integer = 1,
    Decimal = 2,
    Boolean = 3,
    Date = 4,
    DateTime = 5,
    Time = 6,
    CodedValue = 7
}

public record CodedValueAttributeDefinitionDto(
    string Key,
    string? DisplayName,
    AttributeDataType DataType,
    string? SourceCode,
    bool IsRequired,
    bool AllowMultiple,
    int? MinLength,
    int? MaxLength,
    string? RegexPattern);

public record CodedValueDto(
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
    IReadOnlyCollection<CodedValueAttributeDto> Attributes,
    IReadOnlyCollection<CodedValueAttributeDefinitionDto> AttributeDefinitions,
    int ChildrenCount = 0,
    bool IsDeleted = false,
    DateTimeOffset? DeletedAt = null);

public record CreateCodedValueRequest(
    string Code,
    string Name,
    string? Description,
    Guid? ParentId,
    int DisplayOrder = 0);

public record UpdateCodedValueRequest(string Name, string? Description, int DisplayOrder);

public sealed class CodedValuesApiClient(HttpClient http)
{
    public async Task<CodedValueDto[]> SearchAsync(string text, Guid? parentId = null, bool includeDisabled = false, CancellationToken ct = default)
    {
        var url = $"/api/coded-values/search?text={Uri.EscapeDataString(text)}";
        if (parentId.HasValue)
            url += $"&parentId={parentId.Value}";
        if (includeDisabled)
            url += "&includeDisabled=true";
        var result = await http.GetFromJsonAsync<CodedValueDto[]>(url, ct);
        return result ?? [];
    }

    public Task<CodedValueDto[]?> GetRootValuesAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<CodedValueDto[]>("/api/coded-values", ct);

    public Task<CodedValueDto[]?> GetChildrenAsync(Guid parentId, CancellationToken ct = default) =>
        http.GetFromJsonAsync<CodedValueDto[]>($"/api/coded-values/by-parent?parentId={parentId}&includeDisabled=true", ct);

    public Task<CodedValueDto[]?> GetChildrenByParentCodeAsync(string parentCode, CancellationToken ct = default) =>
        http.GetFromJsonAsync<CodedValueDto[]>($"/api/coded-values/by-parent?parentCode={Uri.EscapeDataString(parentCode)}", ct);

    public async Task<CodedValueDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/api/coded-values/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CodedValueDto>(ct);
    }

    public async Task<CodedValueDto?> GetByCodeAsync(string code, Guid? parentId = null, CancellationToken ct = default)
    {
        var url = parentId.HasValue
            ? $"/api/coded-values/by-code/{Uri.EscapeDataString(code)}?parentId={parentId.Value}"
            : $"/api/coded-values/by-code/{Uri.EscapeDataString(code)}";
        var response = await http.GetAsync(url, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CodedValueDto>(ct);
    }

    public async Task CreateAsync(CreateCodedValueRequest req, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("/api/coded-values", req, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateAsync(Guid id, UpdateCodedValueRequest req, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"/api/coded-values/{id}", req, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DisableAsync(Guid id, CancellationToken ct = default) =>
        (await http.PostAsync($"/api/coded-values/{id}/disable", null, ct)).EnsureSuccessStatusCode();

    public async Task EnableAsync(Guid id, CancellationToken ct = default) =>
        (await http.PostAsync($"/api/coded-values/{id}/enable", null, ct)).EnsureSuccessStatusCode();

    public async Task SetAttributeAsync(Guid id, string key, string value, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"/api/coded-values/{id}/attributes/{key}", new { Value = value }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveAttributeAsync(Guid id, string key, CancellationToken ct = default) =>
        (await http.DeleteAsync($"/api/coded-values/{id}/attributes/{key}", ct)).EnsureSuccessStatusCode();

    public async Task SetAttributeDefinitionAsync(Guid id, string key, AttributeDefinitionRequest req, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"/api/coded-values/{id}/attribute-definitions/{key}", req, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveAttributeDefinitionAsync(Guid id, string key, CancellationToken ct = default) =>
        (await http.DeleteAsync($"/api/coded-values/{id}/attribute-definitions/{key}", ct)).EnsureSuccessStatusCode();

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"/api/coded-values/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(error);
        }
        response.EnsureSuccessStatusCode();
    }

    public async Task RecoverAsync(Guid id, CancellationToken ct = default) =>
        (await http.PostAsync($"/api/coded-values/{id}/recover", null, ct)).EnsureSuccessStatusCode();

    public Task<CodedValueDto[]?> GetDeletedAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<CodedValueDto[]>("/api/coded-values/deleted", ct);

    // ── Tenant coded-value overrides (current tenant resolved server-side) ──────
    // Consumed by the wizard's override dialog (§6.2). PUT upserts the current
    // tenant's display-name override and returns the resolved CodedValueDto;
    // DELETE removes it (falls back to the global blueprint name). See spec §5.1.
    public async Task<CodedValueDto> UpsertOverrideAsync(
        Guid codedValueId, string? name, string? description, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync(
            $"/api/coded-values/{codedValueId}/override", new { name, description }, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new KeyNotFoundException($"Coded value {codedValueId} not found.");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CodedValueDto>(ct))!;
    }

    public async Task RemoveOverrideAsync(Guid codedValueId, CancellationToken ct = default) =>
        (await http.DeleteAsync($"/api/coded-values/{codedValueId}/override", ct)).EnsureSuccessStatusCode();
}

public record AttributeDefinitionRequest(
    string? DisplayName,
    AttributeDataType DataType,
    string? SourceCode,
    bool IsRequired,
    bool AllowMultiple = false,
    int? MinLength = null,
    int? MaxLength = null,
    string? RegexPattern = null);
