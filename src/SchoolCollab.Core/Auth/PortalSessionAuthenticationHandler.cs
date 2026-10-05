using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Core.Auth;

/// <summary>
/// The portal-facing claim set (spec §5.4 / D9). ONE shape, ONE spelling: the auth service's
/// <c>ClaimSetFactory</c> builds it, the portal-session handler materializes it, and the realm
/// mappers emit it — the claim names here ARE the mapper contract pinned by the realm import
/// (school-collab-realm.json).
/// </summary>
/// <remarks>
/// Moved to the shared kernel with <see cref="PortalSessionAuthenticationHandler"/> (round
/// <c>portal-session-adoption</c>, D19/D1): the Assignments API resolves the same session header
/// into the SAME claim set the cookie/bearer paths carry, so the D9 contract keeps exactly one
/// spelling across every host. The claim-name constants are the realm mapper contract — changing
/// the realm mappers or these constants without the other is exactly the drift D9 forbids
/// (school-collab-realm.json mappers: tenant-id → tenant_id, tenant-name → tenant_name,
/// tenant-type → tenant_type, teacher-id → teacher_id, roles → roles).
/// </remarks>
public sealed record PortalClaims(
    string TenantId,
    string TenantName,
    string TenantType,
    string TeacherId,
    IReadOnlyList<string> Roles)
{
    public const string TenantIdClaim = "tenant_id";
    public const string TenantNameClaim = "tenant_name";
    public const string TenantTypeClaim = "tenant_type";
    public const string TeacherIdClaim = "teacher_id";
    public const string RolesClaim = "roles";
}

/// <summary>
/// The cheap, I/O-free claims read the portal-session scheme uses to turn the presented session id
/// into a claim set. The auth service registers it as a trivial adapter over its own D15 custody
/// seam (<c>IAuthProvider.ReadClaims</c>, genuinely I/O-free there); every OTHER host that adopts
/// the scheme — today the Assignments API — implements it as a remote HTTP read against the auth
/// service's <c>GET /auth/session/{id}/claims</c> route. Custody is process-local in the auth
/// service, so no remote host can ever resolve the D15 seam in-process: the port is what makes the
/// scheme shareable without sharing custody.
/// </summary>
/// <remarks>
/// The contract is the D15 read's own: <c>null</c> when the session is unknown, expired, or its
/// token payload does not carry the pinned claim contract — the caller fails closed. The read must
/// stay cheap: authenticating a portal call must not trigger a Keycloak refresh (the D18 lifecycle
/// belongs to the session read itself, <c>GET /auth/session/{id}</c>).
/// </remarks>
public interface IPortalSessionClaimsReader
{
    /// <summary>The claim set for a live session, or <c>null</c> to fail the authentication closed.</summary>
    ValueTask<PortalClaims?> ReadClaimsAsync(string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for <see cref="PortalSessionAuthenticationHandler"/>. The scheme carries no settings:
/// the session id travels in a request header and the session itself is validated against the
/// claims port (<see cref="IPortalSessionClaimsReader"/>, custody per D12), so there is nothing to
/// configure.
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
/// privilege-bearing call, and this handler asks custody — through the
/// <see cref="IPortalSessionClaimsReader"/> port, never the auth service's store directly — whether
/// the session is live. The claim set is the one <see cref="PortalClaims"/> pins (D9), including
/// <see cref="ClaimTypes.Role"/> entries for the realm roles, so <c>RequireRole</c> resolves
/// identically here and on the cookie/bearer paths.
/// </para>
/// <para>
/// <b>Fail-closed:</b> an absent header, an unknown session, an expired session and a token whose
/// payload does not carry the pinned claim contract all yield <see cref="AuthenticateResult.NoResult"/>
/// — never a partially-populated principal — and the challenge is a bare <c>401</c> (never a
/// redirect), which is what makes this scheme safe to list first in a portal-facing policy.
/// </para>
/// <para>
/// <b>Shared-kernel home (D19/D1).</b> The scheme lives in Core so more than one host can adopt it
/// (the <see cref="TestAuthHandler"/> precedent): the auth service registers it over in-process
/// custody, and every other adopting host registers it over the remote claims port — the
/// <see cref="AddPortalSessionAuthentication"/> extension and its fallback-aware gateway selector
/// are the shared registration both use.
/// </para>
/// </remarks>
public sealed class PortalSessionAuthenticationHandler(
    IOptionsMonitor<PortalSessionAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IPortalSessionClaimsReader claimsReader)
    : AuthenticationHandler<PortalSessionAuthenticationOptions>(options, logger, encoder)
{
    /// <summary>The scheme name this handler is registered under.</summary>
    public const string SchemeName = "PortalSession";

    /// <summary>
    /// The policy-scheme name a portal-facing policy lists. Policies must name only schemes whose
    /// handler is registered in EVERY flag state (the authorization middleware challenges every
    /// listed scheme, and a missing handler there is a 500 — not a 401), so the portal-facing
    /// policy names this single gateway, which routes each request to either the portal-session
    /// scheme or the host's fallback scheme (<see cref="SelectAuthenticationScheme"/>).
    /// </summary>
    public const string GatewaySchemeName = "PortalSessionGateway";

    /// <summary>The request header carrying the portal's opaque session id (spec D12).</summary>
    public const string SessionHeaderName = "X-Portal-Session";

    /// <summary>
    /// The gateway's per-request target: the portal-session scheme when the caller presents a
    /// session id, otherwise the caller's own scheme. With a pinned <paramref name="fallbackScheme"/>
    /// (a host whose default scheme is semantically wrong for its API surface — e.g. an API host
    /// whose default authenticates browsers via the OIDC cookie), the pinned scheme wins under real
    /// auth; under <c>FEATURE:DisableOIDCAuth</c> the request-time flag resolution keeps the host's
    /// own dev scheme, because the dev posture is today's (the flag resolves per request from
    /// <see cref="IConfiguration"/> via <c>RequestServices</c>, and an absent or invalid value fails
    /// closed to real auth). Without a pin, the host default resolves exactly as before.
    /// </summary>
    public static string SelectAuthenticationScheme(HttpContext context, string? fallbackScheme = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (PresentedSessionId(context.Request) is not null)
        {
            return SchemeName;
        }

        // The pin governs authentication ROUTING only under real auth — the challenge is always
        // this scheme's bare 401, never the fallback. Under the dev flag the host default IS the
        // dev scheme (TestAuth), which is the posture to keep.
        if (!IsOidcDisabled(context) && !string.IsNullOrWhiteSpace(fallbackScheme))
        {
            return fallbackScheme;
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
                new Claim(PortalClaims.TenantIdClaim, claims.TenantId),
                new Claim(PortalClaims.TenantNameClaim, claims.TenantName),
                new Claim(PortalClaims.TenantTypeClaim, claims.TenantType),
                new Claim(PortalClaims.TeacherIdClaim, claims.TeacherId),
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
    /// Validates the presented session id against the claims port (D12) and materializes its claim
    /// set. The read is the port's cheap, I/O-free projection: authenticating a portal call must not
    /// trigger a Keycloak refresh — the D18 lifecycle belongs to the session read itself.
    /// </summary>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var sessionId = PresentedSessionId(Request);
        if (sessionId is null)
        {
            return AuthenticateResult.NoResult();
        }

        var claims = await claimsReader.ReadClaimsAsync(sessionId, Context.RequestAborted);
        if (claims is null)
        {
            Logger.LogDebug("The presented portal session is unknown, expired or unusable; failing closed.");
            return AuthenticateResult.NoResult();
        }

        var principal = BuildPrincipal(claims, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    /// <summary>A bare <c>401</c>: a portal session cannot be "challenged" by redirecting (there is
    /// no interactive flow on this scheme) and a redirect would leak the caller onto an unrelated
    /// login UI.</summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Whether <c>FEATURE:DisableOIDCAuth</c> is enabled in the current request's configuration —
    /// the same resolution <c>AuthTenancyExtensions.IsFlagEnabled</c> performs at registration, read
    /// per request here so a pinned fallback never overrides the dev posture. Absent or invalid ⇒
    /// <c>false</c> ⇒ real auth ⇒ the pinned fallback (fail-closed: an unparseable flag value can
    /// never switch a principal onto a scheme the host did not register).
    /// </summary>
    private static bool IsOidcDisabled(HttpContext context)
    {
        var configuration = context.RequestServices?.GetService<IConfiguration>();
        if (configuration is null)
        {
            return false;
        }

        var value = configuration[$"FeatureFlags:{FeatureFlagKeys.DisableOIDCAuth}"]
                 ?? configuration[FeatureFlagKeys.DisableOIDCAuth];

        return bool.TryParse(value, out var enabled) && enabled;
    }
}

/// <summary>
/// Registers the portal-session scheme (D12/D19) — the portal-session scheme plus the
/// <see cref="PortalSessionAuthenticationHandler.GatewaySchemeName"/> policy scheme — in the
/// shared-kernel shape every adopting host calls.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gateway is registered in EVERY flag state, deliberately:</b> a policy that names a scheme
/// without a handler in some flag state fails the challenge with a 500 instead of a 401, so the
/// portal-facing policies list only the gateway. Authentication follows the request (portal session
/// vs the host's fallback); the challenge is ALWAYS the portal-session scheme's bare 401, never a
/// redirect and never the fallback's.
/// </para>
/// <para>
/// <paramref name="fallbackScheme"/> pins the scheme non-portal callers authenticate with — a
/// hardening for API hosts whose host default is semantically wrong (the OIDC cookie). When it is
/// <c>null</c>, the host default resolves exactly as before the adoption. The reading host that
/// wants a flag-aware pin passes its real-auth scheme; the dev flag resolves per request inside the
/// selector, so the caller never adds flag knowledge.
/// </para>
/// </remarks>
public static class PortalSessionAuthenticationExtensions
{
    /// <summary>Registers the portal-session scheme and its gateway (D12/D19).</summary>
    public static AuthenticationBuilder AddPortalSessionAuthentication(
        this AuthenticationBuilder authentication,
        string? fallbackScheme = null)
    {
        ArgumentNullException.ThrowIfNull(authentication);

        return authentication
            .AddScheme<PortalSessionAuthenticationOptions, PortalSessionAuthenticationHandler>(
                PortalSessionAuthenticationHandler.SchemeName,
                static _ => { })
            .AddPolicyScheme(
                PortalSessionAuthenticationHandler.GatewaySchemeName,
                "Portal-session or host-scheme routing",
                policy =>
                {
                    // Authentication follows the request (portal session vs the pinned fallback or
                    // the host's own scheme); the challenge is ALWAYS the portal-session scheme's
                    // bare 401, never a redirect.
                    policy.ForwardDefaultSelector = context =>
                        PortalSessionAuthenticationHandler.SelectAuthenticationScheme(context, fallbackScheme);
                    policy.ForwardChallenge = PortalSessionAuthenticationHandler.SchemeName;
                });
    }
}
