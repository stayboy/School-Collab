using System.Net.Http.Json;
using System.Text.Json;

namespace SchoolCollab.Admin.Shared.Services;

// Mirrors SchoolCollab.Settings.Core.DTOs.TenantAssignmentAiPromptDto + the PUT
// request shape from the settings assignment-ai-prompt endpoint. Re-declared here
// (rather than referencing Settings.Core) to keep Admin.Shared free of a Settings
// reference, matching the CodedValues/Tenants/NotificationPolicy/AssignmentPolicy/
// SignatureConsentText clients.

/// <summary>
/// Tenant-level organization AI prompt for assignment question generation
/// (WS-B2 / spec §3.4 line 91). A <see langword="null"/>
/// <see cref="SystemPrompt"/> = the embedded default prompt applies.
/// </summary>
public sealed record TenantAssignmentAiPromptDto(string? SystemPrompt, bool IsLocked);

/// <summary>Upsert request for the tenant-level organization AI prompt.</summary>
public sealed record UpsertAssignmentAiPromptRequest(string? SystemPrompt, bool IsLocked);

/// <summary>
/// Typed rejection surfaced when the settings endpoint answers non-success (the
/// 4000-character cap answers 400 with a typed error body) — the message carries
/// the server's error text so the admin page can display it verbatim.
/// </summary>
public sealed class AssignmentAiPromptRejectedException(string error) : Exception(error);

/// <summary>
/// Admin client for the per-tenant organization AI prompt (WS-B2 / spec §3.4) at
/// <c>/api/settings/assignment-ai-prompt</c>. Base address
/// <c>https+http://settings-api</c> is configured in DI. The resolved entity is a
/// STRICT tenant entity — the registered <c>TenantPropagationDelegatingHandler</c>
/// carries the selected tenant or the read/write hits the wrong tenant (the
/// historical symptom class documented in ModuleServices.cs).
/// </summary>
public sealed class AssignmentAiPromptApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TenantAssignmentAiPromptDto?> GetAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("/api/settings/assignment-ai-prompt", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TenantAssignmentAiPromptDto>(cancellationToken: ct);
    }

    public async Task<TenantAssignmentAiPromptDto> UpsertAsync(
        string? systemPrompt, bool isLocked, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync(
            "/api/settings/assignment-ai-prompt",
            new UpsertAssignmentAiPromptRequest(systemPrompt, isLocked), ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            string error = body;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var element))
                    error = element.GetString() ?? body;
            }
            catch (JsonException)
            {
                // Non-JSON error body — surface it verbatim.
            }
            throw new AssignmentAiPromptRejectedException(error);
        }
        return await response.Content.ReadFromJsonAsync<TenantAssignmentAiPromptDto>(
            JsonOptions, cancellationToken: ct);
    }
}
