using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Http;

namespace SchoolCollab.Core.Auth;

/// <summary>
/// The claim set the auth service returns from the D6 redemption (<c>POST /auth/redeem</c>) —
/// claims **as data**, never a token (spec §5.2 step 5 / D6). The names are the auth service's
/// <c>PortalClaims</c> / <c>ClaimSetFactory</c> shape (D9): ONE shape shared with the OIDC path,
/// so the handshake cannot drift from the claims the realm mappers produce.
/// </summary>
public sealed record PortalClaimSet(
    string TenantId,
    string TenantName,
    string TenantType,
    string TeacherId,
    IReadOnlyList<string> Roles);

/// <summary>
/// Outcome of redeeming a one-time handshake code. <see cref="Error"/> carries the auth service's
/// bounded failure code (<c>invalid_code</c>, <c>code_expired</c>, <c>redirect_uri_mismatch</c>,
/// …) for the log line only — it is never returned to the browser.
/// </summary>
public sealed record PortalRedemption(bool IsSuccess, PortalClaimSet? Claims = null, string? Error = null)
{
    /// <summary>The code was valid, unexpired, bound to this host's callback URI, and consumed.</summary>
    public static PortalRedemption Redeemed(PortalClaimSet claims) => new(true, claims);

    /// <summary>The auth service refused the redemption (replay, TTL expiry, URI mismatch, …).</summary>
    public static PortalRedemption Rejected(string error) => new(false, null, error);
}

/// <summary>
/// The typed redemption client of the flag-ON handshake (spec §5.2 / D6): the calling Blazor host
/// posts the portal-supplied one-time code to the auth service **server-to-server** and receives
/// the claim set only. The response carries no token and no secret (AC9/AC11) — this client has no
/// token-shaped member anywhere, so a token cannot leak into the host's HTTP response.
/// </summary>
/// <remarks>
/// Registered by <see cref="PortalHandshakeExtensions.AddPortalHandshake"/>, which passes the
/// caller's base address to <c>AddCrossModuleHttpClient</c> — the retry handler, the tenant
/// propagation and the long handler lifetime of every other cross-module call site, plus the
/// literal-at-call-site wiring <c>CrossModuleWiringTests</c> attributes to the host.
/// </remarks>
public sealed class PortalHandshakeClient(HttpClient httpClient)
{
    /// <summary>The auth service's redemption route.</summary>
    public const string RedeemPath = "/auth/redeem";

    /// <summary>The failure code reported when the auth service answered with a body this client
    /// cannot read — never a caller-visible detail.</summary>
    public const string UnreadableResponseError = "redeem_failed";

    /// <summary>
    /// Both directions of the redemption call use <see cref="JsonSerializerDefaults.Web"/> — the
    /// shape the auth service's minimal-API serializer emits and reads (camelCase: <c>tenantId</c>,
    /// <c>tenantName</c>, <c>teacherId</c>, <c>roles</c>; <c>code</c>, <c>redirectUri</c>), the same
    /// options every other cross-service client in this repo uses. Passing them explicitly keeps the
    /// contract independent of ambient/default serializer behaviour: a reader that does not match the
    /// service's casing would bind every claim to null and reject a perfectly valid code.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Redeems <paramref name="code"/> bound to <paramref name="redirectUri"/> — the EXACT URI the
    /// code was minted for (<c>redirect_uri_mismatch</c> otherwise, D6). Never throws on a
    /// non-success status: the failure taxonomy is returned as data so the caller can fail closed.
    /// </summary>
    public async Task<PortalRedemption> RedeemAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            RedeemPath,
            new RedeemRequest(code, redirectUri),
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return PortalRedemption.Rejected(await ReadErrorCodeAsync(response, cancellationToken));
        }

        var claims = await response.Content.ReadFromJsonAsync<PortalClaimSet>(JsonOptions, cancellationToken);
        return claims is null
            ? PortalRedemption.Rejected(UnreadableResponseError)
            : PortalRedemption.Redeemed(claims);
    }

    /// <summary>Reads the bounded <c>error</c> code from a failure body; anything unparseable
    /// degrades to <see cref="UnreadableResponseError"/> rather than surfacing upstream text.</summary>
    private static async Task<string> ReadErrorCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));

            return document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String
                && error.GetString() is { Length: > 0 } code
                    ? code
                    : UnreadableResponseError;
        }
        catch (JsonException)
        {
            return UnreadableResponseError;
        }
    }

    /// <summary>Request body for <c>POST /auth/redeem</c> — mirrors the auth service's
    /// <c>RedeemRequest</c> (code + the caller's own callback URI).</summary>
    private sealed record RedeemRequest(string Code, string RedirectUri);
}

/// <summary>
/// The calling Blazor hosts' half of the flag-ON handshake (spec §5.2 / D6, design P1-3), shared by
/// Admin and Families: register the typed redemption client, then map the three host-level routes
/// the portal and the auth service hand the browser to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Base address is a parameter, never a literal in this file (plan-review P1-1).</b>
/// <c>CrossModuleWiringTests</c> attributes a cross-module base-address literal to the project that
/// contains it and demands a matching <c>.WithReference(auth)</c> on that project. Core is consumed
/// by students-api / settings-api / settings-ai / assignments-api, which do not reference the auth
/// service, so a literal here would red-light the suite for four unrelated hosts; a config-bound
/// value would be invisible to the guard and fail at runtime with "No such host is known". Each
/// host therefore passes its own literal (<c>"https+http://auth"</c>) at its call site — the
/// <c>FamiliesModuleServices</c> pattern — and B3 pins the matching references.
/// </para>
/// <para>
/// <b>The routes.</b> <c>GET /signin-handshake?code=…&amp;ReturnUrl=…</c> redeems the code and signs
/// the caller into the host's own cookie (D6); <c>GET /login</c> and <c>GET /logout</c> are the
/// explicit endpoints the hosts lacked (spec §13): login defers to the startup-selected challenge
/// — so with the flag ON it lands on the portal's login page through
/// <see cref="PortalRedirectChallengeHandler"/>, and with it OFF on Keycloak exactly as today —
/// and logout signs out through the OIDC handler, which ends the Keycloak SSO session (D13).
/// </para>
/// </remarks>
public static class PortalHandshakeExtensions
{
    /// <summary>The host's explicit login route (spec §13).</summary>
    public const string LoginPath = "/login";

    /// <summary>The host's explicit logout route (spec §13).</summary>
    public const string LogoutPath = "/logout";

    /// <summary>The site root — the continuation used when a caller supplies no local return URL.</summary>
    public const string HomePath = "/";

    /// <summary>
    /// Registers the handshake transport. <paramref name="baseAddress"/> is the caller's own
    /// cross-module literal for the auth service so the wiring guard can attribute it to this host.
    /// </summary>
    public static IServiceCollection AddPortalHandshake(this IServiceCollection services, string baseAddress)
    {
        services.AddCrossModuleHttpClient<PortalHandshakeClient>(baseAddress, propagateTenant: true);
        return services;
    }

    /// <summary>
    /// Maps the handshake group. Routed at the host root (not inline in <c>Program.cs</c>), and
    /// deliberately WITHOUT <c>RequireAuthorization</c>: it is the surface an anonymous caller
    /// reaches to become authenticated.
    /// </summary>
    public static IEndpointRouteBuilder MapPortalHandshakeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(string.Empty);

        group.MapGet(PortalRedirectChallengeHandler.HandshakeCallbackPath, HandshakeAsync);
        group.MapGet(LoginPath, Login);
        group.MapGet(LogoutPath, Logout);

        return app;
    }

    /// <summary>
    /// The D6 redemption: verify the one-time code against the auth service, then sign the caller
    /// into this host's cookie with the SAME claim set the OIDC path produces, and continue to the
    /// page the challenge originally blocked.
    /// </summary>
    /// <remarks>
    /// Fail-closed on every failure path: no cookie, no redirect, and a single bounded status —
    /// the failure class stays in the log (never in the response), so the endpoint is no oracle for
    /// code probing. The redirect URI sent to the auth service is REBUILT from this request
    /// (scheme + host + path base + the fixed callback path + the escaped <c>ReturnUrl</c>) in the
    /// exact shape <see cref="PortalRedirectChallengeHandler"/> minted as the code's binding — the
    /// constants are shared, so the two spellings cannot drift.
    /// </remarks>
    private static async Task<IResult> HandshakeAsync(
        HttpContext context,
        PortalHandshakeClient client,
        ILogger<PortalHandshakeClient> logger,
        string? code = null,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            logger.LogWarning("Portal handshake was called without a one-time code; refusing to sign in.");
            return HandshakeRejected();
        }

        var redirectUri = BuildCallbackUri(context.Request, returnUrl ?? string.Empty);
        var redemption = await client.RedeemAsync(code, redirectUri, cancellationToken);

        if (!redemption.IsSuccess)
        {
            logger.LogWarning(
                "Portal handshake redemption was rejected ({Error}); refusing to sign in.",
                redemption.Error);
            return HandshakeRejected();
        }

        var claims = redemption.Claims!;
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            CreatePrincipal(claims));

        logger.LogInformation(
            "Portal handshake signed the caller in for tenant {TenantId}.",
            claims.TenantId);

        return Results.Redirect(IsLocalPath(returnUrl) ? returnUrl! : HomePath);
    }

    /// <summary>
    /// The hosts' explicit login route. It defers to the startup-selected challenge scheme, so the
    /// login UI choice (portal form when the flag is ON, Keycloak when it is OFF) has exactly ONE
    /// spelling — the policy scheme B1 registers — and this endpoint can never disagree with the
    /// challenge a gated page issues (D5/AC7).
    /// </summary>
    private static IResult Login(HttpContext context, string? returnUrl = null)
    {
        var target = IsLocalPath(returnUrl) ? returnUrl! : HomePath;

        // Post-handshake continuation lands here first; an authenticated caller must be sent on
        // rather than challenged again (a re-challenge here would loop through the portal).
        if (context.User.Identity?.IsAuthenticated == true)
        {
            return Results.LocalRedirect(target);
        }

        return Results.Challenge(new AuthenticationProperties { RedirectUri = target });
    }

    /// <summary>
    /// The hosts' explicit logout route (D13): the OIDC handler signs out this host's cookie and
    /// redirects to Keycloak's <c>end_session</c> endpoint, ending the central SSO session. Both
    /// hosts use this under BOTH flag states — the flag only selects which login UI is presented.
    /// No <c>RedirectUri</c> is supplied: the handler builds <c>post_logout_redirect_uri</c> from
    /// its own <c>SignedOutCallbackPath</c> (<c>/signout-callback-oidc</c>), which is exactly the
    /// per-host URI the realm registers — the one value Keycloak accepts as a post-logout target.
    /// </summary>
    private static IResult Logout() => Results.SignOut(
        authenticationSchemes:
        [
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme,
        ]);

    /// <summary>
    /// The callback URI the one-time code is bound to — rebuilt in
    /// <see cref="PortalRedirectChallengeHandler"/>'s exact shape
    /// (<c>{scheme}://{host}{pathBase}/signin-handshake?ReturnUrl={escaped}</c>).
    /// </summary>
    private static string BuildCallbackUri(HttpRequest request, string returnUrl)
    {
        var pathBase = request.PathBase.Value ?? string.Empty;

        return $"{request.Scheme}://{request.Host}{pathBase}"
            + PortalRedirectChallengeHandler.HandshakeCallbackPath
            + $"?{PortalRedirectChallengeHandler.ReturnUrlParameter}={Uri.EscapeDataString(returnUrl)}";
    }

    /// <summary>
    /// Builds the cookie principal from the redeemed claim set. Claim names match the OIDC cookie
    /// identity exactly — the realm mappers' <c>tenant_id</c>/<c>tenant_name</c>/<c>tenant_type</c>/
    /// <c>teacher_id</c> (consumed by <see cref="CurrentUser"/> and
    /// <see cref="TenantClaimsTransformation"/>) and roles emitted under
    /// <see cref="ClaimTypes.Role"/>, the type the OIDC handler's default inbound mapping renames the
    /// realm's <c>roles</c> claim to (the D9 pin) — so <c>RequireRole</c> resolves identically on
    /// both paths.
    /// </summary>
    private static ClaimsPrincipal CreatePrincipal(PortalClaimSet claims)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim("tenant_id", claims.TenantId),
                new Claim("tenant_name", claims.TenantName),
                new Claim("tenant_type", claims.TenantType),
                new Claim("teacher_id", claims.TeacherId),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        foreach (var role in claims.Roles ?? [])
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(identity);
    }

    /// <summary>Fail-closed answer for every rejected handshake: <c>401</c>, no cookie, no
    /// <c>Location</c> header.</summary>
    private static IResult HandshakeRejected() => Results.StatusCode(StatusCodes.Status401Unauthorized);

    /// <summary>
    /// Whether <paramref name="url"/> is a site-local path — the open-redirect guard for the
    /// challenge <c>ReturnUrl</c> and the login <c>returnUrl</c> (a protocol-relative
    /// <c>//host</c> or an absolute URL is not local).
    /// </summary>
    private static bool IsLocalPath(string? url)
        => !string.IsNullOrEmpty(url)
        && url[0] == '/'
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}
