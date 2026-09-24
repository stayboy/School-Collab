using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Auth.Auth;
using SchoolCollab.Auth.Services;
using SchoolCollab.Core.Features;
using System.Security.Claims;

namespace SchoolCollab.Auth.Endpoints;

/// <summary>
/// Maps the auth service's endpoint groups. Endpoint groups are always grouped extension
/// methods (AGENTS.md) — never inline route maps in Program.cs. Round A pass 2 carries the
/// grouping plus a placeholder <c>GET /auth/ping</c>; pass 3c wires the exchange/redeem/session
/// groups and attaches the fixed-window rate-limit policy to the credential exchange (spec §14);
/// pass 3d adds the admin groups and the <c>user-admin</c> role gate.
/// </summary>
public static class AuthEndpointGroup
{
    /// <summary>Name of the fixed-window rate-limit policy applied to <c>POST /auth/exchange</c>.
    /// Registered by Program.cs (before <c>builder.Build()</c>) and required here.</summary>
    public const string ExchangeRateLimitPolicyName = "exchange-fixed-window";

    /// <summary>The realm role that authorizes the <c>/auth/admin/*</c> groups
    /// (<c>school-collab-realm.json</c> roles, spec §11.2).</summary>
    public const string UserAdminRoleName = "user-admin";

    /// <summary>Maps the <c>/auth</c> group under the app's route builder.</summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");

        group.MapGet("/ping", () => Results.Ok(new { status = "ok" }));

        // Trust boundary: the exchange is called server-to-server by the portal inside the
        // AppHost network, never from a browser — the rate limit is the service-side half of
        // spec §14 (the portal form's own antiforgery is round B's: the form does not exist
        // in round A, so there is nothing to antiforgery yet).
        group.MapPost("/exchange", ExchangeEndpoints.Exchange)
            .RequireRateLimiting(ExchangeRateLimitPolicyName);
        group.MapPost("/redeem", RedeemEndpoints.Redeem);

        // Round B pass B6 — the D16 passkey relying-party flow. /passkey/login is the portal's
        // passkey handoff (the auth service holds the OIDC session with Keycloak), /passkey/complete
        // finishes the ceremony in custody, and /bootstrap/redeem is the portal's server-to-server
        // redemption of the single-use bootstrap code. None of the three is browser-authenticated
        // when it starts: login/completed are the ceremony itself, and the redemption's authorization
        // is the code's single-use + TTL + URI binding against the app-callback allowlist.
        group.MapGet("/passkey/login", PasskeyEndpoints.Login);
        group.MapGet("/passkey/complete", PasskeyEndpoints.Complete);
        group.MapPost("/bootstrap/redeem", PasskeyEndpoints.BootstrapRedeem);
        group.MapGet("/session/{sessionId}", SessionEndpoints.Get);
        group.MapDelete("/session/{sessionId}", SessionEndpoints.Delete);

        // Round B pass B7 — D17's mediated picker reads (/auth/pickers/tenants, /auth/pickers/teachers).
        // The portal holds no credential, so these are its only way to fill a tenant/teacher picker:
        // each requires a portal session and runs as that session's user (see MediatedReadEndpoints).
        group.MapMediatedReadEndpoints();

        // Pass 3d — the admin groups (spec §13). The /auth/admin/* gate mirrors
        // AssignmentEndpoints' conditional-group shape: it applies while OIDC auth is on and
        // opens when FEATURE:DisableOIDCAuth is set. The role requirement uses RequireRole:
        // the realm's `User Realm Role` mapper emits a flat `roles` claim, the handlers'
        // DEFAULT inbound mapping (MapInboundClaims=true — nothing overrides it) renames it to
        // ClaimTypes.Role, and AuthTenancyExtensions pins each handler's RoleClaimType to that
        // same default type, so IsInRole resolves against exactly the claim the mapping
        // produced — identically on the bearer and cookie paths (D9). EMPIRICAL (pass 3d-ii +
        // the corrective pass): the previous RequireClaim("roles", ...) form failed the gate's
        // OWN tests (12 failures under the default mapping, including a caller holding
        // user-admin being rejected), because the literal claim name cannot match after the
        // rename; a bare RequireRole keeps them green.
        var featureFlags = app.ServiceProvider.GetService<IFeatureFlagService>();
        var adminGroup = app.MapGroup("/auth/admin");
        // GetService (not GetRequiredService) on purpose: the endpoint-test host composes the
        // pipeline from public extensions and does not register the flag service (AddAuthAndTenancy
        // is part ii's addition); a MISSING service therefore means "gate on" — the production
        // posture — never "gate open".
        if (featureFlags is null || !featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
        {
            adminGroup.RequireAuthorization(ConfigurePortalFacingAdminPolicy);
        }

        adminGroup.MapAdminUserRoutes();
        adminGroup.MapAdminRoleRoutes();

        return app;
    }

    /// <summary>
    /// The portal-facing admin policy (spec §13 gate, widened by round B's D12/D15): the SAME
    /// authenticated-user + <c>user-admin</c> requirement round A pinned — now satisfiable by the
    /// portal, which holds no token, only its opaque session id
    /// (<see cref="PortalSessionAuthenticationHandler"/> / AC11).
    /// <para>
    /// The policy names ONE scheme, the portal-session gateway, and that is deliberate: the
    /// authorization middleware CHALLENGES every scheme a policy lists, and <c>Bearer</c> /
    /// <c>Cookies</c> / <c>TestAuth</c> are not all registered in both flag states — a listed scheme
    /// without a handler fails the challenge with a 500 instead of a 401. The gateway always
    /// exists, routes authentication to whichever scheme this app already uses (so round A's
    /// cookie/bearer/TestAuth callers are unchanged), and its challenge is a bare 401.
    /// </para>
    /// </summary>
    internal static void ConfigurePortalFacingAdminPolicy(AuthorizationPolicyBuilder policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        policy
            .AddAuthenticationSchemes(PortalSessionAuthenticationHandler.GatewaySchemeName)
            .RequireAuthenticatedUser()
            .RequireRole(UserAdminRoleName);
    }

    /// <summary>Maps an <see cref="AdminStatus"/> from the Keycloak Admin client onto a typed HTTP
    /// result. <c>Forbidden</c> stays a distinct 403 (the loud cold-start failure an insufficient
    /// imported role set must surface as). <c>Detail</c> is Keycloak's own error text — never a
    /// token or a secret.</summary>
    internal static IResult AdminFailure(AdminStatus status, string? detail) => status switch
    {
        AdminStatus.NotFound => Results.Json(new { error = "not_found", detail }, statusCode: StatusCodes.Status404NotFound),
        AdminStatus.Conflict => Results.Json(new { error = "conflict", detail }, statusCode: StatusCodes.Status409Conflict),
        AdminStatus.Forbidden => Results.Json(new { error = "forbidden", detail }, statusCode: StatusCodes.Status403Forbidden),
        AdminStatus.Unauthorized => Results.Json(new { error = "unauthorized", detail }, statusCode: StatusCodes.Status401Unauthorized),
        _ => Results.Json(new { error = "upstream_unreachable", detail }, statusCode: StatusCodes.Status502BadGateway),
    };

    /// <summary>
    /// The audit actor identifier: the teacher id from the authenticated principal's
    /// <c>teacher_id</c> claim — the SAME claim and the SAME read <see cref="CurrentUser"/> uses
    /// ("the same way for OIDC cookie, bearer JWT, and TestAuth principals") — or "unknown".
    /// </summary>
    /// <remarks>
    /// The mutation routes resolve the actor/tenant from the principal directly (via the always
    /// inferable <see cref="HttpContext"/> parameter) rather than injecting <see cref="ICurrentUser"/>:
    /// a route parameter whose type is not a registered service fails parameter inference GLOBALLY
    /// (every endpoint's metadata is inferred when the router initializes), and the endpoint-test
    /// host bootstraps without <c>AddAuthAndTenancy</c> (which registers <c>ICurrentUser</c>) until
    /// part ii adds the auth pipeline. The claims read here are identical to what
    /// <see cref="CurrentUser"/> and <see cref="TenantClaimsTransformation"/> consume, so the audit
    /// values do not change when part ii registers the real service.
    /// </remarks>
    internal static string AuditActor(ClaimsPrincipal user)
        => user.FindFirst(TeacherIdClaim)?.Value ?? "unknown";

    internal static string AuditTenant(ClaimsPrincipal user)
        => user.FindFirst(TenantIdClaim)?.Value ?? SystemTenantId;

    /// <summary>Claim names consumed by <see cref="CurrentUser"/> and
    /// <see cref="TenantClaimsTransformation"/> in <c>SchoolCollab.Core.Auth</c>.</summary>
    private const string TeacherIdClaim = "teacher_id";

    private const string TenantIdClaim = "tenant_id";

    /// <summary>The seeded System tenant (<c>TenantSeeder</c>). Used when the principal carries
    /// no <c>tenant_id</c> claim — the same fallback posture <c>ICurrentUser.CurrentTenant</c>
    /// documents ("the default System context when no tenant is in scope").</summary>
    private const string SystemTenantId = "00000000-0000-0000-0000-000000000001";
}
