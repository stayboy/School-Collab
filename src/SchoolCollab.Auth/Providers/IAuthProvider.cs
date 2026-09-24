using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Providers;

/// <summary>
/// Outcome of <see cref="IAuthProvider.AuthenticateAsync"/> (spec §5.2 / D6). The taxonomy is
/// round A's Direct-Grant taxonomy, re-stated at the seam so a caller never sees
/// Keycloak-specific types.
/// </summary>
public enum ProviderAuthenticationStatus
{
    /// <summary>Credentials accepted; the token set is on the result and must go straight to custody.</summary>
    Success,

    /// <summary>The credential pair was rejected.</summary>
    InvalidCredentials,

    /// <summary>The account exists but is disabled / not fully provisioned.</summary>
    DisabledUser,

    /// <summary>The token endpoint did not answer, or answered with something unusable.</summary>
    UpstreamUnreachable,
}

/// <summary>
/// A session's token set as it travels **inside** the auth service: exchange result → custody
/// (<see cref="PortalSessionStore"/>). It is never serialized, never returned to the portal and
/// never logged (D7 / D12 / AC11) — the portal-facing response records carry claims as data only.
/// </summary>
public sealed record ProviderTokenSet(
    string AccessToken,
    string RefreshToken,
    string IdToken,
    int ExpiresInSeconds);

/// <summary>Result of an authenticate operation. <see cref="Tokens"/> is present only on
/// <see cref="ProviderAuthenticationStatus.Success"/>; <see cref="Detail"/> is bounded
/// diagnostic text (never a token, never the raw upstream body).</summary>
public sealed record ProviderAuthentication(
    ProviderAuthenticationStatus Status,
    ProviderTokenSet? Tokens = null,
    string? Detail = null)
{
    public bool IsSuccess => Status == ProviderAuthenticationStatus.Success;
}

/// <summary>Outcome of a token refresh (spec D18).</summary>
public enum ProviderRefreshStatus
{
    /// <summary>Keycloak issued a fresh token set; it now lives in custody.</summary>
    Refreshed,

    /// <summary>No live session exists under that id.</summary>
    NotFound,

    /// <summary>Keycloak rejected the refresh token — revoked or expired. The session is over
    /// (D18's distinct <c>session ended</c> state): the custody entry is dropped.</summary>
    SessionEnded,

    /// <summary>Keycloak did not answer (or answered unusably). The session is NOT ended —
    /// nothing is revoked — so the caller must not tear the portal's session down.</summary>
    UpstreamUnreachable,
}

/// <summary>Result of a refresh. <see cref="ExpiresInSeconds"/> is the fresh access token's
/// lifetime when the refresh succeeded.</summary>
public sealed record ProviderRefresh(ProviderRefreshStatus Status, int ExpiresInSeconds = 0);

/// <summary>State of a session read (spec D18).</summary>
public enum ProviderSessionStatus
{
    /// <summary>The session is live and the claim set is on the result.</summary>
    Active,

    /// <summary>No live session exists under that id.</summary>
    NotFound,

    /// <summary>The session's refresh token was rejected — the session ended (D18).</summary>
    SessionEnded,

    /// <summary>The identity provider could not be reached; the session state is unknown.</summary>
    UpstreamUnreachable,

    /// <summary>The token payload did not carry the pinned claim contract (D9) — the realm
    /// mappers and <see cref="ClaimSetFactory"/> have drifted.</summary>
    ClaimSetIncomplete,
}

/// <summary>
/// A session read (spec D18): the claim set **as data** plus the session's remaining lifetime.
/// Deliberately token-free — this record's data is what a portal-facing response may serialize
/// (AC11); a token never enters it.
/// </summary>
public sealed record ProviderSessionRead(
    ProviderSessionStatus Status,
    PortalClaims? Claims = null,
    int ExpiresInSeconds = 0);

/// <summary>Result of a session revocation (local custody removal).</summary>
public sealed record ProviderRevocation(bool Found);

/// <summary>
/// Result of a logout (spec §9 / D13): whether a live session was found, and — when one was —
/// the fully-built Keycloak <c>end_session</c> URL the portal redirects the browser to.
/// <see cref="EndSessionUrl"/> carries <c>id_token_hint</c> + <c>post_logout_redirect_uri</c>
/// (D13 option ii) and is <b>opaque to the portal</b>: it 302s the browser to it and never parses,
/// logs or persists it. The hint is the ONE token-shaped value in it — a logout hint, not a
/// bearer credential (see <see cref="KeycloakAuthProvider.BuildEndSessionUrl"/>); no other token
/// travels.
/// </summary>
public sealed record ProviderLogout(bool Found, string? EndSessionUrl = null);

/// <summary>
/// The ONE portal-facing provider seam (spec D15). Keycloak is the only implementation today
/// (<see cref="KeycloakAuthProvider"/>); another IdP would be another implementation selected by
/// configuration, so the portal never learns which provider is behind the seam. The Keycloak
/// **Admin REST** surface deliberately stays Keycloak-specific (<c>KeycloakAdminClient</c>): it
/// *is* a Keycloak admin API, not a portable identity operation.
/// </summary>
/// <remarks>
/// D15's operations, one member each: <see cref="AuthenticateAsync"/> (authenticate),
/// <see cref="CreateSession"/> (session create), <see cref="ReadSessionAsync"/> (session read),
/// <see cref="ReadClaims"/> (claims), <see cref="RefreshAsync"/> (refresh),
/// <see cref="RevokeSession"/> (session revoke), <see cref="LogoutAsync"/> (logout).
/// Every member keeps the token set inside the auth service: no member returns a token to a
/// caller that serializes it, and the portal-facing shapes (<see cref="ProviderSessionRead"/>,
/// <see cref="ProviderLogout"/>) are token-free by construction (D12 / AC11).
/// </remarks>
public interface IAuthProvider
{
    /// <summary>
    /// Verifies a credential pair against the identity provider and returns the resulting token
    /// set (or the typed failure). The caller decides what to do with it — the auth service
    /// stores it via <see cref="CreateSession"/>.
    /// </summary>
    Task<ProviderAuthentication> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a token set as a portal session and returns the opaque session id — the only value
    /// the portal (or a Blazor app) ever holds (D12).
    /// </summary>
    string CreateSession(ProviderTokenSet tokens);

    /// <summary>
    /// Reads a session as **data** (spec D18): the claim set plus the remaining session lifetime.
    /// Tokens are refreshed transparently in custody when the access token has elapsed, so the
    /// caller never has to know about token lifetimes; a refresh Keycloak rejects yields
    /// <see cref="ProviderSessionStatus.SessionEnded"/>.
    /// </summary>
    Task<ProviderSessionRead> ReadSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The claim set for a live session as data, with **no** I/O and no refresh — the cheap
    /// projection the portal-session authentication scheme uses on every portal call.
    /// <c>null</c> when the session is unknown, expired, or its token payload does not carry the
    /// pinned claim contract (the caller fails closed).
    /// </summary>
    PortalClaims? ReadClaims(string sessionId);

    /// <summary>
    /// Refreshes a session's token set in custody (spec D18). <see
    /// cref="ProviderRefreshStatus.SessionEnded"/> means Keycloak revoked/rejected the session —
    /// the custody entry is dropped and the caller must treat the session as over.
    /// </summary>
    Task<ProviderRefresh> RefreshAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops a session from custody. The refresh token is NOT handed to the caller (round-A
    /// contract superseded per AC11): revocation at Keycloak belongs to <see cref="LogoutAsync"/>,
    /// which performs it server-side.
    /// </summary>
    ProviderRevocation RevokeSession(string sessionId);

    /// <summary>
    /// Logout (spec §9 / D13): drops the custody entry, revokes the refresh token at Keycloak
    /// **server-side**, and returns the fully-built <c>end_session</c> URL (<c>id_token_hint</c> +
    /// <c>post_logout_redirect_uri</c>, D13 option ii) for the portal to redirect the browser to —
    /// built in custody, because the portal holds neither the id token nor the registered landing
    /// URI. Revocation at Keycloak is defense-in-depth and best-effort — the local session is gone
    /// either way — and no token is ever returned as data.
    /// </summary>
    Task<ProviderLogout> LogoutAsync(
        string sessionId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of the custody access-token read (D17's mediated reads). The four states are the D18
/// session taxonomy, so a mediator maps them onto the SAME fail-closed responses the session read
/// uses.
/// </summary>
public enum ProviderAccessTokenStatus
{
    /// <summary>A live session exists and its access token is current (refreshed if it had elapsed).</summary>
    Active,

    /// <summary>No live session exists under that id.</summary>
    NotFound,

    /// <summary>Keycloak rejected the session's refresh token — the session is over (D18).</summary>
    SessionEnded,

    /// <summary>Keycloak did not answer, so no current token could be produced. The session is
    /// NOT ended — nothing is revoked.</summary>
    UpstreamUnreachable,
}

/// <summary>
/// A live session's custody access token — the narrow projection the auth service forwards
/// server-side when it calls another module as the session's user (D17). It carries ONLY the
/// access token (never the refresh or id token) and is internal to the service: it is never
/// serialized into a portal-facing response and never logged (D7 / D12 / AC11).
/// </summary>
public sealed record ProviderAccessToken(ProviderAccessTokenStatus Status, string? AccessToken = null);

/// <summary>
/// The auth service's OWN custody read (spec D17): the access token of a live session, refreshed
/// transparently when it has elapsed, so a mediated call can act as the session's user without the
/// portal ever holding a credential (AC11).
/// </summary>
/// <remarks>
/// Deliberately a SEPARATE interface rather than an eighth member of <see cref="IAuthProvider"/>:
/// D15 pins that seam at its seven portal-facing operations, and this read is not one of them —
/// nothing portals call goes through it. <see cref="KeycloakAuthProvider"/> implements both, so
/// there is still exactly ONE custody implementation and ONE source of truth for a session's
/// tokens (the store, via <see cref="Services.PortalSessionStore.ReplaceTokens"/>).
/// </remarks>
public interface ISessionTokenAccessor
{
    /// <summary>
    /// The access token of a live session, refreshed in custody when it has elapsed (minus the
    /// provider's safety margin) so the caller never forwards a stale credential; the value handed
    /// back is the one the custody store now holds. Failure taxonomy: <c>NotFound</c> and
    /// <c>SessionEnded</c> are the D18 session states, <c>UpstreamUnreachable</c> the degraded one.
    /// </summary>
    Task<ProviderAccessToken> GetAccessTokenAsync(
        string sessionId,
        CancellationToken cancellationToken = default);
}
