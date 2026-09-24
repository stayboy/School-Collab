using Microsoft.AspNetCore.Http;
using SchoolCollab.Auth.Providers;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Endpoints;

/// <summary>
/// <c>GET</c> / <c>DELETE /auth/session/{id}</c> — the portal's session read and revocation
/// (spec §5.4 / D12, §9 / D13, D18), served through the D15 provider seam.
/// <para>
/// The GET returns the **claim set as data** — tenant, teacher and roles — plus the session's
/// remaining lifetime: no token of any kind, because the portal holds no credential (AC11) and
/// renders identity from data. Tokens are refreshed transparently in custody, and a refresh
/// Keycloak rejects surfaces as the distinct <c>session_ended</c> state on
/// <c>410 Gone</c> — deliberately NOT <c>404</c> (unknown session) and never a 2xx, so the portal
/// recognizes it from the body code and clears its cookie (D18).
/// </para>
/// <para>
/// The DELETE revokes **server-side** (Keycloak refresh-token revocation, then the custody entry is
/// gone) and returns only the built <c>end_session</c> URL for the portal to redirect the browser
/// to: <c>id_token_hint</c> + <c>post_logout_redirect_uri</c>, both built in custody. The portal
/// treats that URL as **opaque** — it 302s the browser to it and never parses, logs or persists it
/// — and its hint is a logout hint, not a credential (AC11's carve-out). This <b>supersedes round
/// A's refresh-token-return contract</b> (owner-adjudicated under AC11): the portal must never
/// receive a token, so it cannot perform the revocation itself — the auth service does.
/// </para>
/// </summary>
public static class SessionEndpoints
{
    /// <summary>Session-read body: the claim set as data (D9 claim names) plus the session's
    /// remaining lifetime. Token-free by construction (AC11).</summary>
    public sealed record SessionResponse(
        string SessionId,
        string TenantId,
        string TenantName,
        string TenantType,
        string TeacherId,
        IReadOnlyList<string> Roles,
        int ExpiresInSeconds);

    /// <summary>Logout body (D13 option ii): the fully-built <c>end_session</c> URL —
    /// <c>id_token_hint</c> + <c>post_logout_redirect_uri</c> — built in custody because the portal
    /// holds neither the id token nor the registered landing URI. It is OPAQUE to the portal: the
    /// portal 302s the browser to it and never parses, logs or persists it.</summary>
    public sealed record DeleteSessionResponse(string EndSessionUrl);

    /// <summary>
    /// Reads a live session as data. <c>session_not_found</c> (404) and <c>session_ended</c> (410)
    /// are distinct states on purpose: the first tells the portal its cookie is stale, the second
    /// that the identity provider ended the session — the portal renders an AC10 error card and
    /// clears the cookie for both, but only the second is a revocation (D18).
    /// </summary>
    public static async Task<IResult> Get(
        string sessionId,
        IAuthProvider provider,
        CancellationToken cancellationToken = default)
    {
        var read = await provider.ReadSessionAsync(sessionId, cancellationToken);

        return read.Status switch
        {
            ProviderSessionStatus.Active => Results.Ok(new SessionResponse(
                sessionId,
                read.Claims!.TenantId,
                read.Claims.TenantName,
                read.Claims.TenantType,
                read.Claims.TeacherId,
                read.Claims.Roles,
                read.ExpiresInSeconds)),
            ProviderSessionStatus.NotFound => Results.Json(
                new { error = "session_not_found" },
                statusCode: StatusCodes.Status404NotFound),
            ProviderSessionStatus.SessionEnded => Results.Json(
                new { error = "session_ended" },
                statusCode: StatusCodes.Status410Gone),
            ProviderSessionStatus.ClaimSetIncomplete => Results.Json(
                new { error = "claim_set_incomplete" },
                statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Json(
                new { error = "upstream_unreachable" },
                statusCode: StatusCodes.Status502BadGateway),
        };
    }

    /// <summary>
    /// Logs the session out (D13): custody drops the session, the refresh token is revoked at
    /// Keycloak server-side, and the caller receives the built <c>end_session</c> URL
    /// (<c>id_token_hint</c> + <c>post_logout_redirect_uri</c>) — never a token of any other kind.
    /// An unknown session is <c>session_not_found</c> (the portal treats it as already logged out).
    /// </summary>
    public static async Task<IResult> Delete(
        string sessionId,
        IAuthProvider provider,
        CancellationToken cancellationToken = default)
    {
        var logout = await provider.LogoutAsync(sessionId, cancellationToken);

        return logout.Found
            ? Results.Ok(new DeleteSessionResponse(logout.EndSessionUrl!))
            : Results.Json(
                new { error = "session_not_found" },
                statusCode: StatusCodes.Status404NotFound);
    }
}
