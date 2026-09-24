using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SchoolCollab.Core.Auth;

/// <summary>
/// Options for <see cref="PortalRedirectChallengeHandler"/>.
/// </summary>
public sealed class PortalRedirectChallengeOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// Absolute URL of the auth portal's login page — configuration key
    /// <c>Auth:Portal:LoginUrl</c> (env-var <c>Auth__Portal__LoginUrl</c>), supplied by the
    /// AppHost to the browser-facing Blazor hosts only. When null or blank the handler
    /// fails closed with <c>401</c> instead of redirecting (see
    /// <see cref="PortalRedirectChallengeHandler"/>).
    /// </summary>
    public string? LoginUrl { get; set; }
}

/// <summary>
/// The custom-login-UI challenge handler (spec D4/D5, round-B design P1-2). Registered as an
/// authentication scheme named <see cref="SchemeName"/> and selected as the forward-challenge
/// target of the <see cref="AuthTenancyExtensions.LoginUiChallengeScheme"/> policy scheme when
/// <c>FEATURE:DisableKeycloakLoginUi</c> is ON.
/// </summary>
/// <remarks>
/// <para>
/// A gated page's challenge becomes a <c>302</c> to
/// <c>{Auth:Portal:LoginUrl}?return_uri=&lt;app handshake callback&gt;</c>, where the callback is
/// derived from the request (<c>{scheme}://{host}{pathBase}/signin-handshake</c>) so it matches
/// the origin the browser actually used, and the blocked in-app path travels with it as the
/// nested <c>ReturnUrl</c> parameter for post-handshake continuation (D6). The portal validates
/// the callback against its app-callback allowlist before rendering, and the auth service
/// enforces the same allowlist at code issuance.
/// </para>
/// <para>
/// <b>Fail-closed:</b> when the portal login URL is not configured the handler answers
/// <c>401</c> — it must never emit a blank redirect and must never silently fall back to
/// Keycloak's hosted page (which is the UI the flag turned off). Keycloak itself is unaffected:
/// the auth service receives the flag but not the browser-facing login URL.
/// </para>
/// </remarks>
public sealed class PortalRedirectChallengeHandler : AuthenticationHandler<PortalRedirectChallengeOptions>
{
    /// <summary>The scheme name this handler is registered under.</summary>
    public const string SchemeName = "PortalRedirect";

    /// <summary>The query parameter carrying the app callback the portal must return to (spec D6).</summary>
    public const string ReturnUriParameter = "return_uri";

    /// <summary>The query parameter carrying the blocked in-app path on the handshake callback.</summary>
    public const string ReturnUrlParameter = "ReturnUrl";

    /// <summary>The handshake callback path each Blazor host exposes for D6 code redemption.</summary>
    public const string HandshakeCallbackPath = "/signin-handshake";

    public PortalRedirectChallengeHandler(
        IOptionsMonitor<PortalRedirectChallengeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    /// <summary>
    /// This scheme only routes challenges; it never authenticates a caller, so it is
    /// explicitly a non-participant in the authenticate step (<c>NoResult</c>) rather than a
    /// silent failure.
    /// </summary>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        => Task.FromResult(AuthenticateResult.NoResult());

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var loginUrl = Options.LoginUrl;

        if (string.IsNullOrWhiteSpace(loginUrl))
        {
            Logger.LogWarning(
                "Custom login UI is enabled but Auth:Portal:LoginUrl is not configured; "
                + "failing the challenge closed with 401 instead of redirecting to Keycloak.");

            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        var request = Request;
        var pathBase = request.PathBase.Value ?? string.Empty;
        var returnUrl = $"{pathBase}{request.Path.Value}{request.QueryString.Value}";

        var handshakeCallback = $"{request.Scheme}://{request.Host}{pathBase}{HandshakeCallbackPath}";
        var returnUri = $"{handshakeCallback}?{ReturnUrlParameter}={Uri.EscapeDataString(returnUrl)}";

        var separator = loginUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        Response.Redirect(
            $"{loginUrl}{separator}{ReturnUriParameter}={Uri.EscapeDataString(returnUri)}");

        return Task.CompletedTask;
    }
}
