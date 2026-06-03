namespace SchoolCollab.CodedValues.Admin.Services;

public record CodedValueAttributeDto(string Key, string Value);

public record CodedValueDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    Guid? ParentId,
    bool IsDisabled,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<CodedValueAttributeDto> Attributes);

public record CreateCodedValueRequest(
    string Code,
    string Name,
    string? Description,
    Guid? ParentId,
    int DisplayOrder = 0);

public record UpdateCodedValueRequest(string Name, string? Description, int DisplayOrder);

public sealed class CodedValuesApiClient(HttpClient http)
{
    public Task<CodedValueDto[]?> GetRootValuesAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<CodedValueDto[]>("/coded-values", ct);

    public Task<CodedValueDto[]?> GetChildrenAsync(Guid parentId, CancellationToken ct = default) =>
        http.GetFromJsonAsync<CodedValueDto[]>($"/coded-values/by-parent?parentId={parentId}&includeDisabled=true", ct);

    public Task<CodedValueDto?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        http.GetFromJsonAsync<CodedValueDto>($"/coded-values/{id}", ct);

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
}
