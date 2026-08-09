using System.Net.Http.Json;

namespace SchoolCollab.Admin.Shared.Services;

// ── Wire-format DTOs ────────────────────────────────────────────────────────
//
// These mirror the server-side read DTOs in
// SchoolCollab.Settings.Core/DTOs/EntityCodeRuleDto.cs. They are
// re-declared here (rather than referencing Settings.Core) to keep
// Admin.Shared free of bounded-context dependencies — the same pattern as
// CodedValueDto in CodedValuesApiClient. Property names match the JSON wire
// format the server emits; `Type` and `ResetPeriod` are integer-valued enums
// that round-trip with the same numeric codes as their server counterparts.

/// <summary>
/// Wire-format <c>EntityCodeRuleDto</c>. Includes the ordered
/// <see cref="Segments"/> collection.
/// </summary>
public sealed record EntityCodeRuleDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    Guid? TenantId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<EntityCodeSegmentDto> Segments);

/// <summary>
/// Wire-format <c>EntityCodeSegmentDto</c>. <see cref="Type"/> and
/// <see cref="ResetPeriod"/> use the same integer codes as
/// <c>SchoolCollab.Settings.Core.Domain.SegmentType</c> /
/// <c>ResetPeriod</c> (Fixed=0, NumericSequence=1, AlphabeticSequence=2,
/// AlphanumericSequence=3; None=0, Yearly=1, Monthly=2, Quarterly=3).
/// </summary>
public sealed record EntityCodeSegmentDto(
    Guid Id,
    int Index,
    string? Role,
    int Type,
    string FixedText,
    string Prefix,
    string Suffix,
    int ResetPeriod,
    int MinWidth,
    string? UpperLimit,
    int LastSequence,
    string? LastPrefix,
    string? LastPeriodBucket);

// ── Request DTOs ────────────────────────────────────────────────────────────
//
// These mirror the server-side CQRS command records (SchoolCollab.Settings.Core
// /CQRS/EntityCodes/Commands/*). Kept in the Admin.Shared client file to
// match the CodedValuesApiClient pattern (the request records live next to
// the client, not in a separate Contracts package). Property names use the
// camelCase JSON wire format the server expects.

/// <summary>
/// Wire-format body for <c>POST /api/entity-code-rules</c>.
/// Mirrors <c>CreateEntityCodeRule</c>.
/// </summary>
public sealed record CreateEntityCodeRuleRequest(
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<EntityCodeSegmentInputDto> Segments);

/// <summary>
/// Wire-format body for <c>PUT /api/entity-code-rules/{id}</c>.
/// Mirrors <c>UpdateEntityCodeRuleRequest</c> (segments replace-all).
/// </summary>
public sealed record UpdateEntityCodeRuleRequest(
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<EntityCodeSegmentInputDto> Segments);

/// <summary>
/// One segment of a rule's template as posted by the admin UI.
/// Mirrors <c>EntityCodeSegmentInput</c>.
/// </summary>
public sealed record EntityCodeSegmentInputDto(
    int Index,
    string? Role,
    int Type,
    string? FixedText,
    string? Prefix,
    string? Suffix,
    int ResetPeriod,
    int MinWidth,
    string? UpperLimit);

/// <summary>
/// Wire-format response for <c>POST /api/entity-code-rules</c> — the created
/// rule's id (server returns <c>Results.Created(...)</c> with a body of
/// <c>{ id }</c>).
/// </summary>
public sealed record CreateEntityCodeRuleResponse(Guid Id);

/// <summary>
/// Wire-format response for <c>POST /api/entity-code-rules/generate</c> — the
/// generated code (server returns <c>{ code }</c>).
/// </summary>
public sealed record GenerateCodeResponse(string Code);

// ── Typed client ────────────────────────────────────────────────────────────

/// <summary>
/// Typed HTTP client for the <c>/api/entity-code-rules</c> endpoints (spec §4.7).
/// Registered in <c>SchoolCollab.Settings.Application.ModuleServices</c> with
/// base address <c>https+http://settings-api</c>.
/// </summary>
public sealed class EntityCodeRulesApiClient(HttpClient http)
{
    /// <summary>List all rules (with segments), regardless of active/deleted state.</summary>
    public async Task<EntityCodeRuleDto[]> ListAsync(CancellationToken ct = default)
    {
        var result = await http.GetFromJsonAsync<EntityCodeRuleDto[]>("/api/entity-code-rules", ct);
        return result ?? [];
    }

    /// <summary>Get a rule by id (with segments). Returns null if not found.</summary>
    public async Task<EntityCodeRuleDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/api/entity-code-rules/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EntityCodeRuleDto>(ct);
    }

    /// <summary>
    /// Generates a code from a rule + name hint via
    /// <c>POST /api/entity-code-rules/generate</c>. Used by the topic-create
    /// dialog's "regenerate template code" button. Advances the rule's sequence
    /// state, so the caller should use the returned code directly (not
    /// regenerate again on create).
    /// </summary>
    public async Task<string> GenerateAsync(string ruleCode, string? nameHint, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            "/api/entity-code-rules/generate",
            new { ruleCode, nameHint },
            ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GenerateCodeResponse>(ct);
        return result?.Code ?? string.Empty;
    }

    /// <summary>
    /// Create a new rule with its initial segments.
    /// Throws <see cref="EntityCodeRuleCodeConflictException"/> on duplicate code
    /// (server returns 409 with <c>{ message }</c>).
    /// </summary>
    public async Task<Guid> CreateAsync(CreateEntityCodeRuleRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await http.PostAsJsonAsync("/api/entity-code-rules", request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ServerErrorBody>(ct);
            throw new EntityCodeRuleCodeConflictException(
                request.Code,
                body?.Message ?? $"A rule with code '{request.Code}' already exists.");
        }
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadFromJsonAsync<ServerErrorBody>(ct);
            throw new ArgumentException(body?.Message ?? "Invalid rule payload.");
        }
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<CreateEntityCodeRuleResponse>(ct);
        return created?.Id ?? throw new InvalidOperationException("Server returned no id.");
    }

    /// <summary>
    /// Update an existing rule's metadata + segments (replace-all).
    /// Throws on 404 (not found), 409 (concurrency), or 400 (validation).
    /// </summary>
    public async Task UpdateAsync(Guid id, UpdateEntityCodeRuleRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await http.PutAsJsonAsync($"/api/entity-code-rules/{id}", request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new KeyNotFoundException($"Rule {id} not found.");
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ServerErrorBody>(ct);
            throw new InvalidOperationException(body?.Message ?? "Rule was changed by someone else. Reload and retry.");
        }
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadFromJsonAsync<ServerErrorBody>(ct);
            throw new ArgumentException(body?.Message ?? "Invalid rule payload.");
        }
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Soft-delete a rule. Throws <see cref="KeyNotFoundException"/> on 404.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"/api/entity-code-rules/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new KeyNotFoundException($"Rule {id} not found.");
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Activate a rule (deactivates other active rules with the same Code).
    /// Throws <see cref="KeyNotFoundException"/> on 404.
    /// </summary>
    public async Task ActivateAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"/api/entity-code-rules/{id}/activate", content: null, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new KeyNotFoundException($"Rule {id} not found.");
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Loads the current tenant's override rows for the rule (spec §4.12).
    /// Returns an empty array if the tenant has no overrides.
    /// </summary>
    public async Task<TenantEntityCodeRuleOverrideDto[]> GetOverridesAsync(Guid ruleId, CancellationToken ct = default)
    {
        var result = await http.GetFromJsonAsync<TenantEntityCodeRuleOverrideDto[]>(
            $"/api/entity-code-rules/{ruleId}/overrides", ct);
        return result ?? [];
    }

    /// <summary>
    /// Replaces the current tenant's full override set on the rule (atomic,
    /// full overwrite). Throws <see cref="KeyNotFoundException"/> on 404,
    /// <see cref="ArgumentException"/> on validation failure.
    /// </summary>
    public async Task ReplaceOverridesAsync(
        Guid ruleId,
        IReadOnlyList<EntityCodeRuleOverrideInputDto> overrides,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var response = await http.PutAsJsonAsync(
            $"/api/entity-code-rules/{ruleId}/overrides",
            new ReplaceEntityCodeRuleOverridesRequest(overrides), ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new KeyNotFoundException($"Rule {ruleId} not found.");
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadFromJsonAsync<ServerErrorBody>(ct);
            throw new ArgumentException(body?.Message ?? "Invalid override payload.");
        }
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Body shape returned by the server for 4xx error responses — both the
    /// generic ProblemDetails and the <c>Results.BadRequest(new { ex.Message })</c>
    /// / <c>Results.Conflict(new { ex.Message })</c> shapes include a
    /// <c>message</c> field (the server uses anonymous <c>new { ex.Message }</c>).
    /// </summary>
    private sealed record ServerErrorBody(string? Message);
}

// ── Per-tenant overrides wire DTOs (spec §4.12) ────────────────────────────

/// <summary>
/// Wire-format override row returned by <c>GET /api/entity-code-rules/{id}/overrides</c>.
/// </summary>
public sealed record TenantEntityCodeRuleOverrideDto(
    Guid Id,
    Guid GenerationRuleId,
    Guid EntityCodeSegmentId,
    int SegmentIndex,
    int Field,
    string Value);

/// <summary>
/// One override row posted to <c>PUT /api/entity-code-rules/{id}/overrides</c>.
/// <c>Id</c> is <c>Guid.Empty</c> for a new row.
/// </summary>
public sealed record EntityCodeRuleOverrideInputDto(
    Guid Id,
    Guid EntityCodeSegmentId,
    int Field,
    string Value);

/// <summary>
/// Wire-format body for <c>PUT /api/entity-code-rules/{id}/overrides</c>.
/// </summary>
public sealed record ReplaceEntityCodeRuleOverridesRequest(
    IReadOnlyList<EntityCodeRuleOverrideInputDto> Overrides);

/// <summary>
/// Local copy of the server <c>OverrideField</c> enum so the admin UI can
/// render the field dropdown without referencing Settings.Core. Integer
/// values must match SchoolCollab.Settings.Core.Domain.OverrideField.
/// </summary>
public enum OverrideFieldDto
{
    FixedText = 0,
    Prefix = 1,
    Suffix = 2,
    ResetPeriod = 3,
    MinWidth = 4,
    UpperLimit = 5
}

/// <summary>
/// Thrown by <see cref="EntityCodeRulesApiClient.CreateAsync"/> when the server
/// returns 409 (rule with that code already exists). The admin UI maps this
/// to a friendly message under the Code input.
/// </summary>
public sealed class EntityCodeRuleCodeConflictException : Exception
{
    public string RuleCode { get; }
    public EntityCodeRuleCodeConflictException(string ruleCode, string message) : base(message)
    {
        RuleCode = ruleCode;
    }
}