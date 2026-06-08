namespace SchoolCollab.CodedValues.AI.Services;

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

public record AttributeDefinitionRequest(
    string? DisplayName,
    AttributeDataType DataType,
    string? SourceCode,
    bool IsRequired,
    bool AllowMultiple = false,
    int? MinLength = null,
    int? MaxLength = null,
    string? RegexPattern = null);

/// <summary>
/// HTTP client for calling the Coded Values REST API.
/// Uses Aspire service discovery for the base address.
/// </summary>
public sealed class CodedValuesApiClient(HttpClient http)
{
    public Task<CodedValueDto[]?> GetRootValuesAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<CodedValueDto[]>("/coded-values", ct);

    public Task<CodedValueDto[]?> GetChildrenAsync(Guid parentId, CancellationToken ct = default) =>
        http.GetFromJsonAsync<CodedValueDto[]>($"/coded-values/by-parent?parentId={parentId}&includeDisabled=true", ct);

    public Task<CodedValueDto?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        http.GetFromJsonAsync<CodedValueDto>($"/coded-values/{id}", ct);

    public Task<CodedValueDto?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        http.GetFromJsonAsync<CodedValueDto>($"/coded-values/by-code/{Uri.EscapeDataString(code)}", ct);

    public async Task CreateAsync(CreateCodedValueRequest req, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("/coded-values", req, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateAsync(Guid id, UpdateCodedValueRequest req, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"/coded-values/{id}", req, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DisableAsync(Guid id, CancellationToken ct = default) =>
        (await http.PostAsync($"/coded-values/{id}/disable", null, ct)).EnsureSuccessStatusCode();

    public async Task EnableAsync(Guid id, CancellationToken ct = default) =>
        (await http.PostAsync($"/coded-values/{id}/enable", null, ct)).EnsureSuccessStatusCode();

    public async Task SetAttributeAsync(Guid id, string key, string value, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"/coded-values/{id}/attributes/{key}", new { Value = value }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveAttributeAsync(Guid id, string key, CancellationToken ct = default) =>
        (await http.DeleteAsync($"/coded-values/{id}/attributes/{key}", ct)).EnsureSuccessStatusCode();

    public async Task SetAttributeDefinitionAsync(Guid id, string key, AttributeDefinitionRequest req, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"/coded-values/{id}/attribute-definitions/{key}", req, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveAttributeDefinitionAsync(Guid id, string key, CancellationToken ct = default) =>
        (await http.DeleteAsync($"/coded-values/{id}/attribute-definitions/{key}", ct)).EnsureSuccessStatusCode();
}