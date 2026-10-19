using Microsoft.Extensions.Logging;
using SchoolCollab.Core.DeepLinks;
using SchoolCollab.Families.Services;

namespace SchoolCollab.Families.DeepLinks;

/// <summary>The public paths the short-link landing hands over to (round b2a, spec §3.6).</summary>
public static class ShortLinkLanding
{
    /// <summary>The friendly "link expired" page path — the same 302 target the <c>/deeplink</c>
    /// landing uses for an expired/tampered token. Never a raw 404/410 to the visitor (§3.6).</summary>
    public const string ExpiredPath = "/deeplink/expired";

    /// <summary>The existing deep-link landing this host serves
    /// (<see cref="DeepLinkEndpoints"/>' <c>/deeplink/{token}</c> group): the short link only
    /// resolves the code — validation, sign-in and the <c>OpenedAt</c> stamp stay there.</summary>
    public static string DeepLinkPath(string token) => $"/deeplink/{token}";
}

/// <summary>
/// Round notification-templates-b2a (spec §3.6, Q61) — the public <c>/l/{code}</c> resolution the
/// Families host serves for SMS/WhatsApp short links. The code is resolved to its contact-scoped
/// deep-link token through the tenant-less <see cref="ShortLinkResolverClient"/> and the visitor is
/// handed to the <b>existing</b> <c>/deeplink/{token}</c> landing — one landing, one validation, no
/// second auth path.
/// <para>Unknown and expired codes are <b>not</b> errors: both answer the friendly expired page
/// (§3.6), never 404/410. A malformed code short-circuits before any HTTP call, so a garbage URL
/// costs no cross-service round trip.</para>
/// </summary>
public sealed class ShortLinkLandingService(
    ShortLinkResolverClient resolver,
    ILogger<ShortLinkLandingService> logger)
{
    /// <summary>Whether <paramref name="code"/> is shaped like a minted code
    /// (<see cref="DeepLinkConstants.ShortLinkCodeLength"/> URL-safe characters).</summary>
    public static bool IsWellFormed(string? code) =>
        code is { Length: DeepLinkConstants.ShortLinkCodeLength }
        && code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>
    /// Resolves <paramref name="code"/> to the redirect the public route answers with: the existing
    /// deep-link landing for a live code, the friendly expired page otherwise.
    /// </summary>
    public async Task<string> ResolveRedirectAsync(string? code, CancellationToken ct = default)
    {
        if (!IsWellFormed(code))
        {
            logger.LogInformation("Short link landing rejected: malformed code");
            return ShortLinkLanding.ExpiredPath;
        }

        var token = await resolver.ResolveTokenAsync(code!, ct);
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogInformation("Short link landing: code did not resolve to a live link");
            return ShortLinkLanding.ExpiredPath;
        }

        return ShortLinkLanding.DeepLinkPath(token);
    }
}
