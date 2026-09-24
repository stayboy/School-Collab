using System.IO;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolCollab.Auth.Options;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Providers;

/// <summary>
/// The Keycloak implementation of the portal-facing provider seam (spec D15). It performs no
/// identity work of its own: authenticate and the token JSON shape are round A's
/// <see cref="DirectGrantExchanger"/> / <c>DirectGrantResult</c>, custody is round A's
/// <see cref="PortalSessionStore"/>, and the claim shape is round A's
/// <see cref="ClaimSetFactory"/>. What this class adds is the **refresh** and **logout** halves of
/// the session lifecycle (D13/D18), which are Keycloak HTTP calls with no round-A home.
/// </summary>
/// <remarks>
/// <para>
/// <b>Custody — ONE store.</b> The token set lives in <see cref="PortalSessionStore"/> and nowhere
/// else: nothing here is serialized, logged or returned to the portal, and no portal-facing
/// response record can carry a token by construction (D7 / D12 / AC11). A refresh swaps the token
/// set **through that store** (<see cref="PortalSessionStore.ReplaceTokens"/>), so there is a single
/// source of truth — any reader sees the rotated (`refresh_token`) set rather than the exchanged
/// one, and the swapped-in set is as durable as the session itself.
/// </para>
/// <para>
/// <b>Refresh policy.</b> The custody record carries both the session's absolute expiry
/// (<see cref="PortalSessionEntry.ExpiresAtUtc"/>) and the current token set's own expiry
/// (<see cref="PortalSessionEntry.AccessTokenExpiresAtUtc"/>), so the provider refreshes once the
/// access token has elapsed (minus a small safety margin) — refresh-on-demand, so the caller asks
/// for claims and always gets live ones.
/// </para>
/// </remarks>
public sealed class KeycloakAuthProvider : IAuthProvider, ISessionTokenAccessor
{
    /// <summary>Keycloak's OIDC token endpoint, relative to <c>Auth:Keycloak:Authority</c>.</summary>
    private const string TokenPath = "/protocol/openid-connect/token";

    /// <summary>Keycloak's OIDC token revocation endpoint (spec §9 / D13).</summary>
    private const string RevocationPath = "/protocol/openid-connect/revoke";

    /// <summary>Keycloak's RP-initiated logout endpoint (the <c>end_session</c> URL's path).</summary>
    private const string LogoutPath = "/protocol/openid-connect/logout";

    /// <summary>Refresh this long before the access token's real expiry, so a slow round-trip
    /// cannot race it. Mirrors <c>KeycloakAdminClient</c>'s safety margin.</summary>
    private static readonly TimeSpan RefreshSafetyMargin = TimeSpan.FromSeconds(30);

    private readonly DirectGrantExchanger _exchanger;
    private readonly PortalSessionStore _sessions;
    private readonly ClaimSetFactory _claimSetFactory;
    private readonly HttpClient _httpClient;
    private readonly AuthServiceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<KeycloakAuthProvider> _logger;

    public KeycloakAuthProvider(
        DirectGrantExchanger exchanger,
        PortalSessionStore sessions,
        ClaimSetFactory claimSetFactory,
        HttpClient httpClient,
        IOptions<AuthServiceOptions> options,
        TimeProvider timeProvider,
        ILogger<KeycloakAuthProvider> logger)
    {
        _exchanger = exchanger;
        _sessions = sessions;
        _claimSetFactory = claimSetFactory;
        _httpClient = httpClient;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProviderAuthentication> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var result = await _exchanger.ExchangeAsync(username, password, cancellationToken);

        return result.Status switch
        {
            DirectGrantStatus.Success => new ProviderAuthentication(
                ProviderAuthenticationStatus.Success,
                new ProviderTokenSet(
                    result.AccessToken!,
                    result.RefreshToken!,
                    result.IdToken!,
                    result.ExpiresInSeconds)),
            DirectGrantStatus.InvalidCredentials => new ProviderAuthentication(
                ProviderAuthenticationStatus.InvalidCredentials,
                Detail: result.Detail),
            DirectGrantStatus.DisabledUser => new ProviderAuthentication(
                ProviderAuthenticationStatus.DisabledUser,
                Detail: result.Detail),
            _ => new ProviderAuthentication(
                ProviderAuthenticationStatus.UpstreamUnreachable,
                Detail: result.Detail),
        };
    }

    /// <inheritdoc />
    public string CreateSession(ProviderTokenSet tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        return _sessions.Create(tokens.AccessToken, tokens.RefreshToken, tokens.IdToken, tokens.ExpiresInSeconds);
    }

    /// <inheritdoc />
    public async Task<ProviderSessionRead> ReadSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var (tokens, tokenExpiresAtUtc) = CurrentTokens(sessionId);
        if (tokens is null)
        {
            return new ProviderSessionRead(ProviderSessionStatus.NotFound);
        }

        if (_timeProvider.GetUtcNow() >= tokenExpiresAtUtc - RefreshSafetyMargin)
        {
            // The refresh swaps the custody entry atomically and hands back what it wrote, so the
            // claims below come from the authoritative set: the session's TTL can elapse (or a
            // concurrent logout revoke it) while Keycloak answers — that lands as a rejected
            // replacement, i.e. the D18 session-over state, never an exception and never a second
            // racy read of custody.
            var refresh = await RefreshTokensAsync(sessionId, cancellationToken);
            if (refresh.Tokens is null)
            {
                return new ProviderSessionRead(refresh.Status switch
                {
                    ProviderRefreshStatus.SessionEnded => ProviderSessionStatus.SessionEnded,
                    ProviderRefreshStatus.NotFound => ProviderSessionStatus.NotFound,
                    _ => ProviderSessionStatus.UpstreamUnreachable,
                });
            }

            tokens = refresh.Tokens;
        }

        var claims = BuildClaims(tokens!.IdToken);
        if (claims is null)
        {
            return new ProviderSessionRead(ProviderSessionStatus.ClaimSetIncomplete);
        }

        return new ProviderSessionRead(
            ProviderSessionStatus.Active,
            claims,
            RemainingSessionSeconds(sessionId));
    }

    /// <inheritdoc />
    public PortalClaims? ReadClaims(string sessionId)
    {
        var (tokens, _) = CurrentTokens(sessionId);
        return tokens is null ? null : BuildClaims(tokens.IdToken);
    }

    /// <inheritdoc />
    public async Task<ProviderRefresh> RefreshAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var refresh = await RefreshTokensAsync(sessionId, cancellationToken);
        return new ProviderRefresh(refresh.Status, refresh.Tokens?.ExpiresInSeconds ?? 0);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The mediated read's credential (D17). It refreshes through the SAME
    /// <see cref="RefreshTokensAsync"/> the session read uses, so the token handed to the caller is
    /// the authoritative set the custody store now holds: the decision to refresh and the write
    /// happen in one place, and there is no second read of custody that could observe a different
    /// state. <see cref="ProviderAccessTokenStatus.Active"/> therefore always means "current",
    /// never "possibly stale" — which is exactly what makes it safe to forward.
    /// </remarks>
    public async Task<ProviderAccessToken> GetAccessTokenAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var (tokens, tokenExpiresAtUtc) = CurrentTokens(sessionId);
        if (tokens is null)
        {
            return new ProviderAccessToken(ProviderAccessTokenStatus.NotFound);
        }

        if (_timeProvider.GetUtcNow() >= tokenExpiresAtUtc - RefreshSafetyMargin)
        {
            var refresh = await RefreshTokensAsync(sessionId, cancellationToken);
            if (refresh.Tokens is null)
            {
                return new ProviderAccessToken(refresh.Status switch
                {
                    ProviderRefreshStatus.SessionEnded => ProviderAccessTokenStatus.SessionEnded,
                    ProviderRefreshStatus.NotFound => ProviderAccessTokenStatus.NotFound,
                    _ => ProviderAccessTokenStatus.UpstreamUnreachable,
                });
            }

            tokens = refresh.Tokens;
        }

        return new ProviderAccessToken(ProviderAccessTokenStatus.Active, tokens.AccessToken);
    }

    /// <summary>
    /// The refresh itself, returning the token set that now lives in custody — the private half of
    /// <see cref="RefreshAsync"/>, so the read path can serve claims from the SAME set the store
    /// wrote instead of re-reading custody (that re-read is the race the store's atomic swap closes).
    /// The token set never leaves this class except into the custody store.
    /// </summary>
    private async Task<(ProviderRefreshStatus Status, ProviderTokenSet? Tokens)> RefreshTokensAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var (tokens, _) = CurrentTokens(sessionId);
        if (tokens is null)
        {
            return (ProviderRefreshStatus.NotFound, null);
        }

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _options.Keycloak.ClientId,
            ["client_secret"] = _options.Keycloak.ClientSecret,
            ["refresh_token"] = tokens.RefreshToken,
        });

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(TokenEndpoint(), form, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return (ProviderRefreshStatus.UpstreamUnreachable, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return (ProviderRefreshStatus.UpstreamUnreachable, null);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Keycloak answers a revoked/expired/rotated-away refresh token with the 400/401
                // family. Anything else (5xx, gateway errors) is an outage: the session may well
                // still be alive, so it must NOT be reported as ended (D18's state must mean
                // "revoked/expired", never "Keycloak was down").
                if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                    or HttpStatusCode.Forbidden)
                {
                    EndSession(sessionId);
                    return (ProviderRefreshStatus.SessionEnded, null);
                }

                return (ProviderRefreshStatus.UpstreamUnreachable, null);
            }

            // Round A's parser is the ONE token-JSON shape reader (access_token present,
            // refresh_token/id_token optional in the wild but required for a usable session).
            var parsed = DirectGrantResult.SuccessFromTokenJson(body);
            if (!parsed.IsSuccess || parsed.AccessToken is null || parsed.RefreshToken is null)
            {
                return (ProviderRefreshStatus.UpstreamUnreachable, null);
            }

            // The refreshed set goes into the ONE custody store, under the SAME session id and the
            // SAME session expiry (D18): the rotated refresh token is what the next refresh must
            // present, and a direct store reader must see it too. A null replacement means the
            // session expired or was revoked while Keycloak was answering, so the refreshed tokens
            // are unusable and the session is over — the D18 state, reported without throwing.
            var replaced = _sessions.ReplaceTokens(
                sessionId,
                parsed.AccessToken,
                parsed.RefreshToken,
                parsed.IdToken ?? tokens.IdToken,
                parsed.ExpiresInSeconds);

            if (replaced is null)
            {
                _logger.LogInformation(
                    "The portal session was no longer live when its refreshed tokens came back; the refresh ends the session.");
                return (ProviderRefreshStatus.SessionEnded, null);
            }

            // The authoritative set the store now holds — what the claims (and the next refresh)
            // are built from.
            return (ProviderRefreshStatus.Refreshed, new ProviderTokenSet(
                replaced.AccessToken, replaced.RefreshToken, replaced.IdToken, replaced.ExpiresInSeconds));
        }
    }

    /// <inheritdoc />
    public ProviderRevocation RevokeSession(string sessionId)
    {
        return new ProviderRevocation(_sessions.Revoke(sessionId).Found);
    }

    /// <inheritdoc />
    public async Task<ProviderLogout> LogoutAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var revocation = _sessions.Revoke(sessionId);

        if (!revocation.Found)
        {
            return new ProviderLogout(Found: false);
        }

        if (revocation.RefreshToken is not null)
        {
            await RevokeAtKeycloakAsync(revocation.RefreshToken, cancellationToken);
        }

        return new ProviderLogout(Found: true, BuildEndSessionUrl());
    }

    /// <summary>
    /// The fully-built <c>end_session</c> URL the portal redirects the browser to (D13).
    /// </summary>
    /// <remarks>
    /// Token-free by construction — and the reason is now an owner adjudication, not a reading of
    /// AC11 over D13: the round restores D13 by having the **auth service perform the browser-facing
    /// logout redirect itself**, carrying <c>id_token_hint</c> + <c>post_logout_redirect_uri</c> in a
    /// <c>Location</c> header. The id token then never enters a response body (AC11 holds) and never
    /// reaches Python state (the portal only follows a redirect). That endpoint and the realm's
    /// post-logout redirect URI are pass **B6**'s work — this pass returns the token-free URL only.
    /// Keycloak accepts <c>client_id</c> when <c>id_token_hint</c> is absent, so this URL still
    /// terminates the session; it is the fallback shape, not the round's final logout flow.
    /// </remarks>
    public string BuildEndSessionUrl()
    {
        var authority = _options.Keycloak.Authority.TrimEnd('/');
        var clientId = Uri.EscapeDataString(_options.Keycloak.ClientId);
        return $"{authority}{LogoutPath}?client_id={clientId}";
    }

    /// <summary>
    /// Revokes the refresh token at Keycloak (spec §9 / D13). Best-effort by design: D13 calls
    /// revocation defense-in-depth — the custody entry is already gone and the browser is about to
    /// be sent to <c>end_session</c> — so an outage must not turn a logout into a failure. The
    /// token is never logged.
    /// </summary>
    private async Task RevokeAtKeycloakAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = _options.Keycloak.ClientId,
            ["client_secret"] = _options.Keycloak.ClientSecret,
            ["token"] = refreshToken,
            ["token_type_hint"] = "refresh_token",
        });

        try
        {
            using var response = await _httpClient.PostAsync(RevocationEndpoint(), form, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Keycloak rejected the refresh-token revocation during logout with status {StatusCode}; "
                    + "the local session is already revoked.",
                    (int)response.StatusCode);
            }
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning(
                "Keycloak was unreachable for the refresh-token revocation during logout; "
                + "the local session is already revoked.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Keycloak timed out for the refresh-token revocation during logout; "
                + "the local session is already revoked.");
        }
    }

    private void EndSession(string sessionId) => _sessions.Revoke(sessionId);

    /// <summary>The token set currently in custody for the session plus that set's access-token
    /// expiry. Both are <c>null</c>/default when the session is unknown or expired.</summary>
    private (ProviderTokenSet? Tokens, DateTimeOffset TokenExpiresAtUtc) CurrentTokens(string sessionId)
    {
        var entry = _sessions.Get(sessionId);
        if (entry is null)
        {
            return (null, default);
        }

        return (new ProviderTokenSet(
            entry.AccessToken, entry.RefreshToken, entry.IdToken, entry.ExpiresInSeconds),
            entry.AccessTokenExpiresAtUtc);
    }

    private int RemainingSessionSeconds(string sessionId)
    {
        var entry = _sessions.Get(sessionId);
        if (entry is null)
        {
            return 0;
        }

        var remaining = entry.ExpiresAtUtc - _timeProvider.GetUtcNow();
        return remaining <= TimeSpan.Zero ? 0 : (int)remaining.TotalSeconds;
    }

    private PortalClaims? BuildClaims(string idToken)
    {
        try
        {
            return _claimSetFactory.Build(DecodePayloadSegment(idToken));
        }
        catch (InvalidDataException ex)
        {
            // The token came from Keycloak through this service's own exchange: a payload that is
            // not a well-formed JWT with the pinned claim contract (D9) is an internal
            // inconsistency, so the caller fails closed rather than rendering half a page.
            _logger.LogWarning(ex, "The session's ID token payload did not yield a claim set.");
            return null;
        }
    }

    /// <summary>
    /// Decodes the <c>payload</c> segment of a JWT as JSON **without cryptographic verification**,
    /// exactly as round A's redemption path does: the token was minted by Keycloak and stored by
    /// this service moments earlier, so the read path only needs the claim SHAPE (D9). The helper
    /// is private to round A's <c>RedeemEndpoints</c>, which this pass's expected-files rows do not
    /// cover, so it is restated here rather than shared.
    /// </summary>
    private static JsonElement DecodePayloadSegment(string idToken)
    {
        var parts = idToken.Split('.');
        if (parts.Length != 3)
        {
            throw new InvalidDataException("the ID token is not a three-segment JWT.");
        }

        var segment = (parts[1].Length % 4) switch
        {
            0 => parts[1],
            2 => parts[1] + "==",
            3 => parts[1] + "=",
            _ => throw new InvalidDataException("the ID token payload segment has an invalid length."),
        };

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(segment.Replace('-', '+').Replace('_', '/'));
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("the ID token payload segment is not valid base64url.", ex);
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("the ID token payload segment is not valid JSON.", ex);
        }
    }

    private Uri TokenEndpoint() => new($"{_options.Keycloak.Authority.TrimEnd('/')}{TokenPath}");

    private Uri RevocationEndpoint() => new($"{_options.Keycloak.Authority.TrimEnd('/')}{RevocationPath}");
}
