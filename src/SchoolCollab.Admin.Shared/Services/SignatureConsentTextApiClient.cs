using System.Net.Http.Json;
using System.Text.Json;

namespace SchoolCollab.Admin.Shared.Services;

// Mirrors SchoolCollab.Settings.Core.DTOs.TenantSignatureConsentTextDto + the PUT
// request shape from the settings signature-consent-text endpoint. Re-declared here
// (rather than referencing Settings.Core) to keep Admin.Shared free of a Settings
// reference, matching the CodedValues/Tenants/NotificationPolicy/AssignmentPolicy
// clients.

/// <summary>Tenant-level guardian sign-off consent text (WS-C2 / spec §3.2 line 53).</summary>
public sealed record TenantSignatureConsentTextDto(string? ConsentText);

/// <summary>Upsert request for the tenant-level sign-off consent text.</summary>
public sealed record UpsertSignatureConsentTextRequest(string? ConsentText);

/// <summary>
/// Typed rejection surfaced when the settings endpoint answers non-success (the
/// 4000-character cap answers 400 with a typed error body) — the message carries
/// the server's error text so the admin page can display it verbatim.
/// </summary>
public sealed class SignatureConsentTextRejectedException(string error) : Exception(error);

/// <summary>
/// Admin client for the per-tenant guardian sign-off consent text (WS-C2 / spec §3.2)
/// at <c>/api/settings/signature-consent-text</c>. Base address
/// <c>https+http://settings-api</c> is configured in DI. The resolved entity is a
/// STRICT tenant entity — the registered <c>TenantPropagationDelegatingHandler</c>
/// carries the selected tenant or the read/write hits the wrong tenant (the
/// historical symptom class documented in ModuleServices.cs).
/// </summary>
public sealed class SignatureConsentTextApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TenantSignatureConsentTextDto?> GetAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("/api/settings/signature-consent-text", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TenantSignatureConsentTextDto>(cancellationToken: ct);
    }

    public async Task<TenantSignatureConsentTextDto> UpsertAsync(string? consentText, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync(
            "/api/settings/signature-consent-text",
            new UpsertSignatureConsentTextRequest(consentText), ct);
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
            throw new SignatureConsentTextRejectedException(error);
        }
        return await response.Content.ReadFromJsonAsync<TenantSignatureConsentTextDto>(
            JsonOptions, cancellationToken: ct);
    }
}
