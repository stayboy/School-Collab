using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>
/// HTTP-backed <see cref="ISignatureConsentTextResolver"/> (WS-C1/C2 / spec
/// §3.2 line 53). Reads the tenant's consent-language override from the
/// Settings API (<c>GET /api/settings/signature-consent-text</c>, named client
/// <c>settings-api</c>) and falls back to
/// <see cref="SignatureConsentDefaults.EmbeddedConsentText"/> on 204 or any
/// failure — fail-open, signing is never blocked by a Settings outage (the
/// <see cref="SignatureDefaultResolver"/> posture). Mirrors it line-for-line.
/// </summary>
public sealed class SignatureConsentTextResolver(
    IHttpClientFactory httpClientFactory,
    ILogger<SignatureConsentTextResolver> logger) : ISignatureConsentTextResolver
{
    public async Task<string> ResolveConsentTextAsync(CancellationToken cancellationToken = default)
    {
        var settings = httpClientFactory.CreateClient("settings-api");
        try
        {
            var response = await settings.GetAsync(
                "/api/settings/signature-consent-text", cancellationToken);
            // 204 = no tenant row ⇒ embedded default.
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return SignatureConsentDefaults.EmbeddedConsentText;
            }
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<TenantSignatureConsentTextDto>(cancellationToken);
            if (dto is not null && !string.IsNullOrWhiteSpace(dto.ConsentText))
            {
                return dto.ConsentText.Trim();
            }
            return SignatureConsentDefaults.EmbeddedConsentText;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to resolve tenant signature consent text; using the embedded default");
            return SignatureConsentDefaults.EmbeddedConsentText;
        }
    }
}