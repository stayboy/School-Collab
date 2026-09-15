namespace SchoolCollab.Assignments.Api.Services;

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Settings.Core.DTOs;

/// <summary>
/// HTTP-backed <see cref="IAiPromptPolicyResolver"/> (WS-B2 / spec §3.4). Reads
/// the tenant org-level AI prompt's lock flag from the Settings API via the named
/// <c>settings-api</c> client (already registered — no new <c>AddHttpClient</c>).
/// 204 or <see langword="false"/>-locked resolves; any
/// <see cref="HttpRequestException"/> fails open to <see langword="false"/>
/// (warning logged), so the create wizard is never blocked.
/// </summary>
public sealed class AiPromptPolicyResolver(
    IHttpClientFactory httpClientFactory,
    ILogger<AiPromptPolicyResolver> logger) : IAiPromptPolicyResolver
{
    public async Task<bool> ResolveAiPromptLockedAsync(CancellationToken ct = default)
    {
        var settings = httpClientFactory.CreateClient("settings-api");
        try
        {
            var response = await settings.GetAsync("/api/settings/assignment-ai-prompt", ct);
            // 204 = no org prompt configured yet ⇒ caller falls back to false.
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return false;
            }
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<TenantAssignmentAiPromptDto>(ct);
            return dto?.IsLocked ?? false;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to resolve the tenant AI-prompt lock; defaulting to false");
            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient timeout — the caller did not request cancellation, so this
            // is a transport failure: fail open to false (never block the wizard).
            logger.LogWarning("Timed out resolving the tenant AI-prompt lock; defaulting to false");
            return false;
        }
    }
}
