using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Families.Services;

/// <summary>
/// Round notification-templates-b2a (spec §3.6, Q61) — the tenant-<b>less</b> client the public
/// <c>/l/{code}</c> landing reads a short-link code through.
///
/// <para>Deliberately NOT <see cref="FamiliesApiClient"/>: that one propagates the caller's dev
/// tenant as an <c>x-tenant-id</c> header, and an unauthenticated visitor arriving from an SMS has
/// no tenant to propagate — the resolution is cross-tenant by design (the code is the credential).
/// Its own <c>AddCrossModuleHttpClient</c> registration therefore sets
/// <c>propagateTenant: false</c>, keeping the retry/lifetime chain and dropping the tenant header.
/// </para>
/// </summary>
public sealed class ShortLinkResolverClient(
    HttpClient http,
    ILogger<ShortLinkResolverClient> logger)
{
    /// <summary>
    /// The deep-link token <paramref name="code"/> stands for, or <see langword="null"/> when the
    /// code is unknown or its link has expired (the API answers 404 for both).
    /// </summary>
    public async Task<string?> ResolveTokenAsync(string code, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/short-links/{Uri.EscapeDataString(code)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var resolution = await response.Content.ReadFromJsonAsync<ShortLinkResolutionDto>(ct);
        logger.LogInformation("Short link resolved to a contact-scoped deep-link token");
        return resolution?.DeepLinkToken;
    }
}
