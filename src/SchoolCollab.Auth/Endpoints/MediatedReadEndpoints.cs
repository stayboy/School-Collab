using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Auth.Auth;
using SchoolCollab.Auth.Providers;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Endpoints;

/// <summary>
/// The portal's mediated picker reads (spec D17): <c>GET /auth/pickers/tenants</c> and
/// <c>GET /auth/pickers/teachers</c>.
/// <para>
/// The portal holds no credential of any kind (AC11/D12) and makes no direct call to settings-api
/// or students-api, so the auth service performs those reads on its behalf — **as the session's
/// user**: it resolves the presented portal session through the D15 provider seam, forwards that
/// session's custody access token server-side, and returns the upstream data only. The data API's
/// own tenant middleware (<c>TenantClaimsTransformation</c>) then scopes the result from the
/// forwarded token's <c>tenant_id</c> claim, exactly as it does for a direct authenticated call.
/// </para>
/// <para>
/// Responses are DATA ONLY: no token, session id, secret or upstream error body is ever echoed
/// (AC9/AC11). Every failure is fail-closed and typed — the D18 <c>session_not_found</c> /
/// <c>session_ended</c> states, and one degraded <c>upstream_unreachable</c> status the portal
/// renders as an AC10 card. A mediated read never returns 500.
/// </para>
/// </summary>
public static class MediatedReadEndpoints
{
    /// <summary>
    /// Maps the <c>/auth/pickers</c> group (called from <c>AuthEndpointGroup</c>, so the routes
    /// live under the service's single <c>/auth</c> group like every other route — endpoint groups
    /// are always grouped extension methods, never inline maps).
    /// </summary>
    public static RouteGroupBuilder MapMediatedReadEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/pickers");
        group.RequireAuthorization(ConfigurePortalSessionPolicy);
        group.MapGet("/tenants", Tenants);
        group.MapGet("/teachers", Teachers);

        return group;
    }

    /// <summary>The tenant-picker rows (settings-api's tenant registry, read as the session's user).</summary>
    public static Task<IResult> Tenants(
        HttpContext httpContext,
        ISessionTokenAccessor sessionTokens,
        TenantDirectoryReader tenants,
        CancellationToken cancellationToken = default)
        => MediateAsync(httpContext, sessionTokens, tenants.ReadAsync, cancellationToken);

    /// <summary>The teacher-picker rows (students-api's teacher list, read as the session's user).</summary>
    public static Task<IResult> Teachers(
        HttpContext httpContext,
        ISessionTokenAccessor sessionTokens,
        TeacherDirectoryReader teachers,
        CancellationToken cancellationToken = default)
        => MediateAsync(httpContext, sessionTokens, teachers.ReadAsync, cancellationToken);

    /// <summary>
    /// The one mediation shape both pickers share: session → custody access token → upstream read.
    /// </summary>
    /// <remarks>
    /// The token comes from <see cref="ISessionTokenAccessor"/> — never from the custody store
    /// directly — because that read refreshes an elapsed token in place and returns the set the
    /// store then holds: a direct store read could hand this method a stale access token, and the
    /// data API would reject the forward.
    /// </remarks>
    private static async Task<IResult> MediateAsync<TItem>(
        HttpContext httpContext,
        ISessionTokenAccessor sessionTokens,
        Func<string, CancellationToken, Task<MediatedRead<IReadOnlyList<TItem>>>> read,
        CancellationToken cancellationToken)
    {
        var sessionId = PortalSessionAuthenticationHandler.PresentedSessionId(httpContext.Request);
        if (sessionId is null)
        {
            // Unreachable through this group's policy (PortalSession returns no result without a
            // session id, so the request is challenged first) — kept as the fail-closed default so
            // the mediator never mediates on behalf of an unidentified caller if the policy is ever
            // loosened or the method is invoked directly.
            return SessionNotFound(StatusCodes.Status401Unauthorized);
        }

        var accessToken = await sessionTokens.GetAccessTokenAsync(sessionId, cancellationToken);

        if (accessToken.Status != ProviderAccessTokenStatus.Active)
        {
            return accessToken.Status switch
            {
                ProviderAccessTokenStatus.SessionEnded => Results.Json(
                    new { error = "session_ended" },
                    statusCode: StatusCodes.Status410Gone),
                ProviderAccessTokenStatus.NotFound => SessionNotFound(StatusCodes.Status404NotFound),
                _ => UpstreamUnavailable(),
            };
        }

        var result = await read(accessToken.AccessToken!, cancellationToken);

        return result.Status == MediatedReadStatus.Ok
            ? Results.Ok(result.Data)
            : UpstreamUnavailable();
    }

    /// <summary>
    /// Authorization for the picker group, stated explicitly: a valid **portal session**, and
    /// nothing else. The policy lists one scheme whose handler is registered in BOTH flag states
    /// (<c>AddAuthServices</c>), so the challenge is the portal-session handler's bare <c>401</c> —
    /// never a redirect, and never a 500 from a policy naming a scheme this host does not register.
    /// Bearer and cookie callers are deliberately NOT accepted: the pickers exist for the portal,
    /// which presents its opaque session id in <c>X-Portal-Session</c> (D12).
    /// </summary>
    private static void ConfigurePortalSessionPolicy(AuthorizationPolicyBuilder policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        policy
            .AddAuthenticationSchemes(PortalSessionAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser();
    }

    /// <summary>No usable session: the same body code at 401 (nothing presented) and 404 (unknown),
    /// so the portal has one vocabulary for "this session is not usable" (D18).</summary>
    private static IResult SessionNotFound(int statusCode) =>
        Results.Json(new { error = "session_not_found" }, statusCode: statusCode);

    /// <summary>One degraded status for every upstream failure class: the portal cannot act on the
    /// difference, and the upstream status or body must not travel (AC11).</summary>
    private static IResult UpstreamUnavailable() =>
        Results.Json(new { error = "upstream_unreachable" }, statusCode: StatusCodes.Status502BadGateway);
}
