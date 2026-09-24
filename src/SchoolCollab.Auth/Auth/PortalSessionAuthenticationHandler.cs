using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolCollab.Auth.Providers;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Auth;

/// <summary>
/// Options for <see cref="PortalSessionAuthenticationHandler"/>. The scheme carries no settings:
/// the session id travels in a request header and the session itself is validated against
/// custody (spec D12), so there is nothing to configure.
/// </summary>
public sealed class PortalSessionAuthenticationOptions : AuthenticationSchemeOptions;

/// <summary>
/// The portal-session authentication scheme (spec D12 / D15): it turns the **opaque session id**
/// a portal presents into a principal, so portal-facing endpoints can authorize without the portal
/// holding a token of any kind (AC11).
/// </summary>
/// <remarks>
/// <para>
/// The portal never holds a Keycloak token: its cookie is a bare unguessable id (D12). It presents
/// that id in the <see cref="SessionHeaderName"/> request header on every identity- or
/// privilege-bearing call, and this handler asks the auth service's own custody — through the
/// D15 seam, never the store directly — whether the session is live. The claim set is the one
/// <see cref="ClaimSetFactory"/> produced for the OIDC paths (D9), including
/// <see cref="ClaimTypes.Role"/> entries for the realm roles, so <c>RequireRole</c> resolves
/// identically here and on the cookie/bearer paths.
/// </para>
/// <para>
/// <b>Fail-closed:</b> an absent header, an unknown session, an expired session and a token whose
/// payload does not carry the pinned claim contract all yield <see cref="AuthenticateResult.NoResult"/>
/// — never a partially-populated principal — and the challenge is a bare <c>401</c> (never a
/// redirect), which is what makes this scheme safe to list first in a portal-facing policy.
/// </para>
/// </remarks>
public sealed class PortalSessionAuthenticationHandler(
    IOptionsMonitor<PortalSessionAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAuthProvider provider)
    : AuthenticationHandler<PortalSessionAuthenticationOptions>(options, logger, encoder)
{
    /// <summary>The scheme name this handler is registered under.</summary>
    public const string SchemeName = "PortalSession";

    /// <summary>
    /// The policy-scheme name a portal-facing policy lists. Policies must name only schemes whose
    /// handler is registered in EVERY flag state (the authorization middleware challenges every
    /// listed scheme, and a missing handler there is a 500 — not a 401), so the portal-facing
    /// policy names this single gateway, which routes each request to either the portal-session
    /// scheme or the host's own default scheme (<see cref="SelectAuthenticationScheme"/>).
    /// </summary>
    public const string GatewaySchemeName = "PortalSessionGateway";

    /// <summary>The request header carrying the portal's opaque session id (spec D12).</summary>
    public const string SessionHeaderName = "X-Portal-Session";

    /// <summary>
    /// The gateway's per-request target: the portal-session scheme when the caller presents a
    /// session id, otherwise the scheme this app already authenticates with (the OIDC cookie, the
    /// bearer token, or dev TestAuth) — so a portal-facing policy behaves exactly as it did before
    /// the portal existed for every non-portal caller.
    /// </summary>
    public static string SelectAuthenticationScheme(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (PresentedSessionId(context.Request) is not null)
        {
            return SchemeName;
        }

        var options = context.RequestServices?.GetService<IOptions<AuthenticationOptions>>()?.Value;
        return options?.DefaultAuthenticateScheme
            ?? options?.DefaultScheme
            ?? CookieAuthenticationDefaults.AuthenticationScheme;
    }

    /// <summary>Builds the principal from a claim set: the pinned tenant/teacher/role names —
    /// the same claims, the same spelling, as the OIDC cookie and the bearer token (D9).</summary>
    public static ClaimsPrincipal BuildPrincipal(PortalClaims claims, string schemeName)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimSetFactory.TenantIdClaim, claims.TenantId),
                new Claim(ClaimSetFactory.TenantNameClaim, claims.TenantName),
                new Claim(ClaimSetFactory.TenantTypeClaim, claims.TenantType),
                new Claim(ClaimSetFactory.TeacherIdClaim, claims.TeacherId),
                .. claims.Roles.Select(role => new Claim(ClaimTypes.Role, role)),
            ],
            schemeName);

        return new ClaimsPrincipal(identity);
    }

    /// <summary>The session id a request presents, or <c>null</c> when it presents none.</summary>
    public static string? PresentedSessionId(HttpRequest request)
    {
        foreach (var value in request.Headers[SessionHeaderName])
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Validates the presented session id against custody (D12) and materializes its claim set.
    /// The read is the seam's cheap, I/O-free <see cref="IAuthProvider.ReadClaims"/>: authenticating
    /// a portal call must not trigger a Keycloak refresh — the D18 lifecycle belongs to the
    /// session read itself.
    /// </summary>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var sessionId = PresentedSessionId(Request);
        if (sessionId is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = provider.ReadClaims(sessionId);
        if (claims is null)
        {
            Logger.LogDebug("The presented portal session is unknown, expired or unusable; failing closed.");
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var principal = BuildPrincipal(claims, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }

    /// <summary>A bare <c>401</c>: a portal session cannot be "challenged" by redirecting (there is
    /// no interactive flow on this scheme) and a redirect would leak the caller onto an unrelated
    /// login UI.</summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
