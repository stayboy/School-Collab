using Microsoft.Extensions.Configuration;

namespace SchoolCollab.Mobile.Logic;

/// <summary>
/// The mobile head's OIDC credential settings, bound from the <see cref="SectionName"/> section of
/// the head's <c>appsettings*.json</c>.
/// </summary>
/// <remarks>
/// <para>Every address is configuration, never a C# literal (spec D8): the authority differs per
/// environment (a Windows dev loopback, an Android-emulator host alias, a deployed IdP), so it is
/// bound and normalized here and the OIDC endpoint paths are appended by
/// <see cref="BuildAuthorizeEndpointUri"/> / <see cref="BuildTokenEndpointUri"/>.</para>
/// <para><b>Custom-scheme hijack posture.</b> <see cref="RedirectScheme"/> is an unverified,
/// non-owned scheme: any Android app can register the same intent filter and receive the
/// redirect. The deliberate mitigations are <b>PKCE + <c>state</c></b> — a hijacker that captures
/// the redirect holds a short-lived authorization <c>code</c> that is useless without the
/// in-memory <c>code_verifier</c> (which is never persisted), and a forged callback fails the
/// <c>state</c> match before any token is persisted (see <see cref="OidcLoginService"/>). The
/// loopback alternative — a local listener on <c>127.0.0.1</c> — owns no scheme at all and is the
/// preferred shape where practical, but it needs a per-run listener plus platform loopback
/// permissions; this round ships the custom scheme with the mitigations explicit in code.</para>
/// </remarks>
public sealed class MobileAuthOptions
{
    /// <summary>The configuration section this class is bound from.</summary>
    public const string SectionName = "MobileAuth";

    /// <summary>The Keycloak OIDC authorization endpoint path, relative to <see cref="Authority"/>.</summary>
    private const string AuthorizePath = "protocol/openid-connect/auth";

    /// <summary>The Keycloak OIDC token endpoint path, relative to <see cref="Authority"/>.</summary>
    private const string TokenPath = "protocol/openid-connect/token";

    /// <summary>The realm URL, e.g. <c>&lt;scheme&gt;://&lt;host&gt;/realms/&lt;realm&gt;</c> — from configuration only.</summary>
    public string? Authority { get; set; }

    /// <summary>The public Keycloak client the device signs in with (the realm's mobile client).</summary>
    public string? ClientId { get; set; }

    /// <summary>The custom redirect scheme the device registers (the app's callback scheme).</summary>
    public string? RedirectScheme { get; set; }

    /// <summary>
    /// The redirect URI's authority component — named <c>RedirectHost</c>, not <c>RedirectPath</c>,
    /// because a custom-scheme callback URI carries <b>no path</b>: in
    /// <c>schoolcollab://signin-oidc</c> the trailing token is the URI authority. The Android
    /// <c>[IntentFilter]</c>'s <c>DataHost</c> and the realm's <c>redirectUris</c> entry must both
    /// equal this value (Keycloak matches redirect URIs exactly).
    /// </summary>
    public string? RedirectHost { get; set; }

    /// <summary>The scopes the authorize request asks for. Defaults to <c>openid</c> (the ID token requires it).</summary>
    public string Scope { get; set; } = "openid";

    /// <summary>Additional scopes the app requires; requested alongside <see cref="Scope"/>, de-duplicated.</summary>
    public string[] RequiredScopes { get; set; } = [];

    /// <summary>
    /// Binds a new instance from the <see cref="SectionName"/> section of
    /// <paramref name="configuration"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    public static MobileAuthOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new MobileAuthOptions();
        configuration.GetSection(SectionName).Bind(options);
        return options;
    }

    /// <summary>
    /// The configured realm URL normalized to an absolute <see cref="Uri"/> with a trailing slash
    /// (the shape relative Keycloak paths are appended to) — or <see langword="null"/> when the
    /// value is absent or blank, so the head can start without a credential configuration and fail
    /// only at the first sign-in attempt.
    /// </summary>
    /// <exception cref="InvalidOperationException">The configured value is not an absolute URI.</exception>
    public Uri? AuthorityUri => string.IsNullOrWhiteSpace(Authority) ? null : ToAbsolute(Authority);

    /// <summary>The realm's OIDC authorization endpoint.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Authority"/> is not configured.</exception>
    public Uri BuildAuthorizeEndpointUri() => BuildEndpoint(AuthorizePath);

    /// <summary>The realm's OIDC token endpoint (the authorization-code exchange and refresh both POST here).</summary>
    /// <exception cref="InvalidOperationException"><see cref="Authority"/> is not configured.</exception>
    public Uri BuildTokenEndpointUri() => BuildEndpoint(TokenPath);

    /// <summary>
    /// The custom-scheme callback URI as the exact string the realm registers in
    /// <c>redirectUris</c> and the authorize request carries as <c>redirect_uri</c>.
    /// </summary>
    /// <remarks>
    /// This is a STRING, not the <see cref="Uri"/>, on purpose: <see cref="Uri.ToString"/> normalizes a
    /// pathless custom-scheme URI to a trailing slash (<c>schoolcollab://signin-oidc/</c>), and Keycloak
    /// matches redirect URIs exactly — the slash is a hard <c>invalid_redirect_uri</c> at the first real
    /// sign-in attempt.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The scheme or host is not configured.</exception>
    public string BuildRedirectUriString()
    {
        if (string.IsNullOrWhiteSpace(RedirectScheme) || string.IsNullOrWhiteSpace(RedirectHost))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(RedirectScheme)} and {nameof(RedirectHost)} must both be configured.");
        }

        return $"{RedirectScheme.Trim()}://{RedirectHost.Trim()}";
    }

    /// <summary>
    /// The custom-scheme callback URI built as <c>{RedirectScheme}://{RedirectHost}</c> — the exact
    /// literal the realm registers in <c>redirectUris</c> and the head hands to
    /// <c>WebAuthenticator</c> as the callback URL (the platform matches scheme + authority, so the
    /// normalized form is what it needs).
    /// </summary>
    /// <exception cref="InvalidOperationException">The scheme or host is not configured.</exception>
    public Uri BuildRedirectUri() => new(BuildRedirectUriString());

    /// <summary>
    /// The space-separated scope string the authorize request sends: <see cref="Scope"/> plus
    /// <see cref="RequiredScopes"/>, de-duplicated in request order.
    /// </summary>
    public string ResolveScope() => string.Join(
        ' ',
        new[] { Scope }.Concat(RequiredScopes)
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.Ordinal));

    private Uri BuildEndpoint(string path)
    {
        var authority = AuthorityUri
            ?? throw new InvalidOperationException(
                $"{SectionName}:{nameof(Authority)} must be configured before an OIDC endpoint can be built.");

        return new Uri(authority, path);
    }

    private static Uri ToAbsolute(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(Authority)} must be an absolute URL; got '{value}'.");
        }

        // A trailing slash is what makes `new Uri(authority, path)` APPEND the path instead of
        // replacing the realm segment: `.../realms/school-collab` + `protocol/…` would otherwise
        // resolve to `.../realms/protocol/…`.
        var builder = new UriBuilder(uri);
        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }

        return builder.Uri;
    }
}
