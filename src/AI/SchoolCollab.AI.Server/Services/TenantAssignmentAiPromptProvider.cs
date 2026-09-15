using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SchoolCollab.AI.Services;

/// <summary>
/// WS-B2 — local re-declaration of the tenant organization AI prompt wire shape
/// (mirrors <c>SchoolCollab.Settings.Core.DTOs.TenantAssignmentAiPromptDto</c>;
/// AI.Abstractions/AI.Server must not reference Settings.Core).
/// </summary>
public sealed record TenantAssignmentAiPromptInfo(string? SystemPrompt, bool IsLocked);

/// <summary>
/// Resolves the caller's tenant-level organization AI prompt for assignment
/// question generation from the Settings API (WS-B2 / spec §3.4 line 91).
/// Fail-open: 204/404 ⇒ <see langword="null"/> (embedded default applies), and any
/// transport/API failure logs a warning and returns <see langword="null"/> — it
/// never throws to the caller, so a Settings outage cannot block question
/// generation. Mirrors the ar-8 <c>ISignatureDefaultResolver</c> fail-open posture.
/// </summary>
public sealed class TenantAssignmentAiPromptProvider(
    HttpClient http,
    ILogger<TenantAssignmentAiPromptProvider> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TenantAssignmentAiPromptInfo?> FetchAsync(CancellationToken ct = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync("/api/settings/assignment-ai-prompt", ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex,
                "Failed to fetch the tenant assignment AI prompt (network) — falling back to the embedded default");
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient timeout — the caller did not request cancellation, so
            // this is a transport failure: fail open to the embedded default.
            logger.LogWarning(
                "Timed out fetching the tenant assignment AI prompt — falling back to the embedded default");
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }

        if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Failed to fetch the tenant assignment AI prompt (HTTP {Status}) — falling back to the embedded default",
                (int)response.StatusCode);
            return null;
        }

        try
        {
            var dto = await response.Content.ReadFromJsonAsync<AssignmentAiPromptWireDto>(JsonOptions, ct);
            return dto is null ? null : new TenantAssignmentAiPromptInfo(dto.SystemPrompt, dto.IsLocked);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Malformed assignment AI prompt response — falling back to the embedded default");
            return null;
        }
    }

    private sealed record AssignmentAiPromptWireDto(string? SystemPrompt, bool IsLocked);
}
