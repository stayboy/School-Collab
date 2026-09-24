using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SchoolCollab.Auth.Options;
using SchoolCollab.Auth.Providers;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Endpoints;

/// <summary>
/// The D16 passkey relying-party flow (round B pass B6). On this path the **auth service is the
/// OIDC relying party** — the realm has no portal client — so the WebAuthn ceremony happens
/// between the browser and Keycloak and comes back to this service's own <c>/signin-oidc</c>. The
/// portal never sees a token or an authorization code: it receives a single-use, short-TTL
/// bootstrap code whose redemption yields its opaque session id (AC11/AC12).
/// <para>
/// <b>Three routes.</b> <see cref="Login"/> starts the ceremony, <see cref="Complete"/> finishes
/// it, and <see cref="BootstrapRedeem"/> is the portal's server-to-server redemption of the
/// bootstrap code the 302 carried.
/// </para>
/// <para>
/// <b>The open-redirect control is the B5b allowlist, enforced at issuance.</b> The
/// <c>app_redirect_uri</c> the caller supplies is checked against
/// <see cref="AppCallbackAllowlist"/> <b>before</b> the challenge, then persisted in the OIDC
/// pending state (the correlation cookie's authentication properties) and <b>re-validated from
/// that persisted value</b> at <see cref="Complete"/>. <see cref="Complete"/> never reads a fresh
/// query parameter: the value that decides where the D6 continuation code points is the one the
/// ceremony carried, so a modified URL at the callback cannot redirect a minted code.
/// </para>
/// </summary>
public static class PasskeyEndpoints
{
    /// <summary>
    /// The <see cref="AuthenticationProperties"/> item the validated <c>app_redirect_uri</c>
    /// travels in. The OIDC handler protects the challenge's properties into the correlation
    /// cookie's state parameter and returns them on the callback ticket (the same channel that
    /// carries <c>SaveTokens</c>' <c>.Token.*</c> entries), so the value survives the ceremony
    /// without this service storing per-request state of its own.
    /// </summary>
    public const string AppRedirectUriItemName = "app_redirect_uri";

    /// <summary>The ceremony's start path (what the portal's passkey button links to).</summary>
    public const string LoginPath = "/auth/passkey/login";

    /// <summary>
    /// The ceremony's completion path — the <see cref="AuthenticationProperties.RedirectUri"/> the
    /// challenge pins, so the OIDC handler redirects the browser here after <c>/signin-oidc</c>
    /// signs the cookie instead of back into <see cref="Login"/> (which would re-challenge).
    /// </summary>
    public const string CompletePath = "/auth/passkey/complete";

    /// <summary>
    /// Configuration key for the portal route the browser is 302d to with the bootstrap code
    /// (B3's pin: fanned to the auth service only, derived from the portal's Aspire endpoint — no
    /// hardcoded port). Read from <see cref="IConfiguration"/> rather than
    /// <see cref="AuthServiceOptions"/> because it belongs to the passkey flow alone and has no
    /// startup validation contract; a blank value fails closed per request (see
    /// <see cref="Complete"/>).
    /// </summary>
    internal const string BootstrapRedirectUrlKey = "Auth:Portal:BootstrapRedirectUrl";

    /// <summary>Query parameter carrying the single-use bootstrap code on the 302.</summary>
    internal const string BootstrapCodeQueryName = "code";

    /// <summary>
    /// An access-token lifetime used only when the token's own <c>exp</c> claim cannot be read: a
    /// deliberately SHORT fallback, so the session's refresh happens early (an unnecessary
    /// refresh) rather than late (a token the data APIs reject).
    /// </summary>
    private const int FallbackAccessTokenLifetimeSeconds = 300;

    /// <summary>
    /// Starts the passkey ceremony (D16). The supplied <c>app_redirect_uri</c> — the callback a
    /// Blazor app's sign-in must eventually continue to, or the portal's own route when the portal
    /// itself initiated the sign-in — is validated against <see cref="AppCallbackAllowlist"/>
    /// <b>before</b> the challenge, so no ceremony is ever started for a target that could not
    /// receive a code. A blank value (400 <c>redirect_uri_required</c>) and a non-allowlisted value
    /// (400 <c>redirect_uri_not_allowed</c>) are refused without a single redirect; neither body
    /// echoes the rejected URI.
    /// <para>
    /// The challenge names the <see cref="OpenIdConnectDefaults.AuthenticationScheme"/> explicitly
    /// on purpose: the host's default challenge scheme is the login-UI policy scheme (B1), and the
    /// auth service must NOT let that route its own browser-facing passkey challenge to the portal's
    /// login page — the auth service is the relying party here, in both flag states.
    /// </para>
    /// </summary>
    public static async Task<IResult> Login(
        HttpContext httpContext,
        AppCallbackAllowlist callbackAllowlist)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(callbackAllowlist);

        var appRedirectUri = httpContext.Request.Query[AppRedirectUriItemName].ToString();

        if (string.IsNullOrWhiteSpace(appRedirectUri))
        {
            return Results.Json(
                new { error = "redirect_uri_required" },
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!callbackAllowlist.IsAllowed(appRedirectUri))
        {
            return Results.Json(
                new { error = "redirect_uri_not_allowed" },
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Persisted, not stored: the OIDC handler protects these properties into the correlation
        // cookie's state and returns them on the callback ticket, so Complete can re-read the
        // value the ceremony started with (never a query parameter supplied at the callback).
        var properties = new AuthenticationProperties { RedirectUri = CompletePath };
        properties.Items[AppRedirectUriItemName] = appRedirectUri.Trim();

        await httpContext.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);

        // The challenge has already written the 302; nothing may be appended.
        return Results.Empty;
    }

    /// <summary>
    /// Completes the passkey ceremony (D16): the OIDC callback signed this browser in (the cookie
    /// is the ceremony's result), so the service now
    /// <list type="number">
    /// <item>reads the <c>app_redirect_uri</c> from the <b>persisted</b> authentication properties
    /// and re-validates it against the allowlist — a fresh query parameter is never consulted;</item>
    /// <item>takes the token set out of the cookie's properties (the OIDC handler's
    /// <c>SaveTokens</c> channel) into custody, returning only the opaque session id;</item>
    /// <item>mints the single-use, TTL-bound, URI-bound bootstrap code for the portal, plus — when
    /// a Blazor app originated the sign-in — the D6 one-time handshake code for that app's
    /// callback;</item>
    /// <item>302s the browser to <c>Auth:Portal:BootstrapRedirectUrl</c> with only the bootstrap
    /// code. No token and no authorization code ever leaves this service on this response
    /// (AC12).</item>
    /// </list>
    /// Fail-closed throughout: an unauthenticated caller (401), a missing/withdrawn/non-allowlisted
    /// persisted target (400), an unusable token set (502) and an unconfigured portal bootstrap URL
    /// (500) all end without a code being minted.
    /// </summary>
    public static async Task<IResult> Complete(
        HttpContext httpContext,
        AppCallbackAllowlist callbackAllowlist,
        PortalSessionStore sessions,
        OneTimeCodeStore codes,
        BootstrapCodeStore bootstrapCodes,
        IConfiguration configuration,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(callbackAllowlist);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(codes);
        ArgumentNullException.ThrowIfNull(bootstrapCodes);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(timeProvider);

        // The ceremony's own result is the cookie the OIDC handler signed on /signin-oidc.
        var ceremony = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!ceremony.Succeeded || ceremony.Properties is null)
        {
            return Results.Json(
                new { error = "passkey_ceremony_incomplete" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // The PERSISTED value decides where the continuation code may point. A query parameter on
        // this request is ignored by construction — it is never read here.
        if (!ceremony.Properties.Items.TryGetValue(AppRedirectUriItemName, out var persistedAppRedirectUri)
            || string.IsNullOrWhiteSpace(persistedAppRedirectUri)
            || !callbackAllowlist.IsAllowed(persistedAppRedirectUri))
        {
            return Results.Json(
                new { error = "redirect_uri_not_allowed" },
                statusCode: StatusCodes.Status400BadRequest);
        }

        var appRedirectUri = persistedAppRedirectUri.Trim();

        var tokens = ReadTokenSet(ceremony.Properties, timeProvider);
        if (tokens is null)
        {
            return Results.Json(
                new { error = "keycloak_unreachable", detail = "the ceremony returned no usable token set" },
                statusCode: StatusCodes.Status502BadGateway);
        }

        if (!TryResolveBootstrapTarget(configuration, out var bootstrapRedirectUrl, out var bootstrapBinding))
        {
            return Results.Json(
                new { error = "bootstrap_redirect_unconfigured" },
                statusCode: StatusCodes.Status500InternalServerError);
        }

        // Custody: the token set lives only here; the session id is the only value a caller holds.
        var sessionId = sessions.Create(
            tokens.AccessToken, tokens.RefreshToken, tokens.IdToken, tokens.ExpiresInSeconds);

        // D16 continuation: a sign-in that began at a gated Blazor page gets the D6 one-time code
        // for that app's callback, which the portal hands onward after the redemption. A sign-in
        // that began at the portal itself shares the portal's origin and needs no continuation —
        // the portal only needs its session id.
        BootstrapContinuation? continuation = null;
        if (!IsPortalOwnSignIn(appRedirectUri, bootstrapBinding))
        {
            continuation = new BootstrapContinuation(
                appRedirectUri,
                codes.Create(appRedirectUri, sessionId));
        }

        var bootstrapCode = bootstrapCodes.Create(bootstrapBinding, sessionId, continuation);

        var location = $"{bootstrapRedirectUrl}"
            + (bootstrapRedirectUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?")
            + $"{BootstrapCodeQueryName}={Uri.EscapeDataString(bootstrapCode)}";

        return Results.Redirect(location, permanent: false);
    }

    /// <summary>Request body for <c>POST /auth/bootstrap/redeem</c> — the shape B2's portal client
    /// pins: <c>{code, redirectUri}</c>, where <c>redirectUri</c> is the portal's own bootstrap
    /// redemption URI (the URI the code was bound to when this service issued it).</summary>
    public sealed record BootstrapRedeemRequest(string Code, string RedirectUri);

    /// <summary>
    /// Success body for the portal (AC12): its opaque session id plus — for a Blazor-originated
    /// sign-in — the D6 continuation (that app's callback and the one-time code the portal redirects
    /// onward with). <b>No token and no authorization code of the portal's own is ever present.</b>
    /// </summary>
    public sealed record BootstrapRedeemResponse(string SessionId, string? AppRedirectUri = null, string? AppCode = null);

    /// <summary>
    /// Redeems the bootstrap code server-to-server for the portal (D16). The supplied
    /// <c>redirectUri</c> is checked against the allowlist <b>and</b> against the code's own binding,
    /// so a code observed in one call site cannot be redeemed from another. Single-use, TTL-bound,
    /// and fail-closed on every rejection; the response carries only non-token data (AC12).
    /// </summary>
    public static IResult BootstrapRedeem(
        BootstrapRedeemRequest request,
        AppCallbackAllowlist callbackAllowlist,
        BootstrapCodeStore bootstrapCodes)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(callbackAllowlist);
        ArgumentNullException.ThrowIfNull(bootstrapCodes);

        if (string.IsNullOrWhiteSpace(request.RedirectUri))
        {
            return Results.Json(
                new { error = "redirect_uri_required" },
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!callbackAllowlist.IsAllowed(request.RedirectUri))
        {
            return Results.Json(
                new { error = "redirect_uri_not_allowed" },
                statusCode: StatusCodes.Status400BadRequest);
        }

        var redemption = bootstrapCodes.Redeem(request.Code, request.RedirectUri);
        return redemption.Status switch
        {
            RedeemStatus.Success => Results.Ok(new BootstrapRedeemResponse(
                redemption.SessionId!,
                redemption.Continuation?.AppRedirectUri,
                redemption.Continuation?.AppCode)),
            RedeemStatus.InvalidCode => Results.Json(
                new { error = "invalid_code" },
                statusCode: StatusCodes.Status404NotFound),
            RedeemStatus.Expired => Results.Json(
                new { error = "code_expired" },
                statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Json(
                new { error = "redirect_uri_mismatch" },
                statusCode: StatusCodes.Status400BadRequest),
        };
    }

    /// <summary>
    /// Resolves the configured portal bootstrap target: the URL the browser is redirected to (as
    /// configured) and the normalized <c>scheme://host[:port]/path</c> the bootstrap code is bound
    /// to (query and fragment stripped, so the code's binding is stable while the redirect may carry
    /// its own parameters). <c>false</c> — never a blank redirect — when the key is absent, blank or
    /// not an absolute http(s) URI.
    /// </summary>
    private static bool TryResolveBootstrapTarget(
        IConfiguration configuration,
        out string redirectUrl,
        out string binding)
    {
        redirectUrl = string.Empty;
        binding = string.Empty;

        var configured = configuration[BootstrapRedirectUrlKey];
        if (string.IsNullOrWhiteSpace(configured)
            || !Uri.TryCreate(configured.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        redirectUrl = configured.Trim();
        binding = parsed.GetLeftPart(UriPartial.Path);
        return true;
    }

    /// <summary>
    /// Whether the sign-in began at the portal itself rather than at a Blazor app: the persisted
    /// target shares the portal bootstrap URL's origin. Such a sign-in needs no D6 continuation
    /// (the portal only needs its session id); every other origin is an app and gets one.
    /// </summary>
    private static bool IsPortalOwnSignIn(string appRedirectUri, string portalBootstrapBinding)
    {
        return Uri.TryCreate(appRedirectUri.Trim(), UriKind.Absolute, out var app)
            && Uri.TryCreate(portalBootstrapBinding, UriKind.Absolute, out var portal)
            && string.Equals(
                app.GetLeftPart(UriPartial.Authority),
                portal.GetLeftPart(UriPartial.Authority),
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The ceremony's token set, read from the cookie properties the OIDC handler wrote
    /// (<c>SaveTokens</c>). <c>null</c> when any of the three tokens is missing: a session without
    /// a refresh token could never be refreshed or revoked, so it must not be created at all — the
    /// same posture round A's exchange takes for a token-less "success".
    /// <para>
    /// <see cref="ProviderTokenSet"/> travels only into <see cref="PortalSessionStore"/>; it is never
    /// serialized, so this type is not a response shape (D12/AC11).
    /// </para>
    /// </summary>
    private static ProviderTokenSet? ReadTokenSet(AuthenticationProperties properties, TimeProvider timeProvider)
    {
        var accessToken = properties.GetTokenValue("access_token");
        var refreshToken = properties.GetTokenValue("refresh_token");
        var idToken = properties.GetTokenValue("id_token");

        if (string.IsNullOrEmpty(accessToken)
            || string.IsNullOrEmpty(refreshToken)
            || string.IsNullOrEmpty(idToken))
        {
            return null;
        }

        return new ProviderTokenSet(
            accessToken,
            refreshToken,
            idToken,
            AccessTokenLifetimeSeconds(accessToken, timeProvider));
    }

    /// <summary>
    /// The access token's remaining lifetime, read from its own <c>exp</c> claim so custody's
    /// refresh timing matches what Keycloak issued. Falls back to
    /// <see cref="FallbackAccessTokenLifetimeSeconds"/> when the payload cannot be read — refreshing
    /// early is harmless, refreshing late is not.
    /// </summary>
    private static int AccessTokenLifetimeSeconds(string accessToken, TimeProvider timeProvider)
    {
        try
        {
            var payload = DecodePayloadSegment(accessToken);
            if (payload.TryGetProperty("exp", out var exp)
                && exp.ValueKind == JsonValueKind.Number
                && exp.TryGetInt64(out var unixSeconds))
            {
                var remaining = DateTimeOffset.FromUnixTimeSeconds(unixSeconds) - timeProvider.GetUtcNow();
                if (remaining > TimeSpan.Zero)
                {
                    return (int)Math.Min(remaining.TotalSeconds, int.MaxValue);
                }
            }
        }
        catch (InvalidDataException)
        {
            // Not a readable JWT payload: fall through to the conservative lifetime below.
        }

        return FallbackAccessTokenLifetimeSeconds;
    }

    /// <summary>
    /// Decodes the <c>payload</c> segment of a JWT as JSON **without cryptographic verification**,
    /// exactly as round A's redemption path and <c>KeycloakAuthProvider</c> do: the token was minted
    /// by Keycloak and handed to this service by its own OIDC handler moments earlier, so only the
    /// claim value is needed. A three-segment JWT with a base64url JSON payload is assumed; anything
    /// else throws.
    /// </summary>
    private static JsonElement DecodePayloadSegment(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            throw new InvalidDataException("the token is not a three-segment JWT.");
        }

        var segment = (parts[1].Length % 4) switch
        {
            0 => parts[1],
            2 => parts[1] + "==",
            3 => parts[1] + "=",
            _ => throw new InvalidDataException("the token payload segment has an invalid length."),
        };

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(segment.Replace('-', '+').Replace('_', '/'));
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("the token payload segment is not valid base64url.", ex);
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("the token payload segment is not valid JSON.", ex);
        }
    }
}

/// <summary>
/// The D16 continuation a bootstrap code carries: the originating app's callback and the D6
/// one-time handshake code minted for it. Present only when a Blazor app — not the portal —
/// originated the sign-in.
/// </summary>
public sealed record BootstrapContinuation(string AppRedirectUri, string AppCode);

/// <summary>Outcome of a bootstrap-code redemption: the round-A <see cref="RedeemStatus"/>, the
/// custody session id when the code was consumed, and the continuation carried by the code.</summary>
public sealed record BootstrapRedemptionResult(
    RedeemStatus Status,
    string? SessionId = null,
    BootstrapContinuation? Continuation = null);

/// <summary>
/// Custody for the D16 bootstrap codes (round B pass B6). It reuses round A's
/// <see cref="OneTimeCodeStore"/> primitive for the code itself — single-use, TTL-bound, and bound
/// to the portal's bootstrap redemption URI — in a **dedicated instance**, so the bootstrap
/// namespace and the D6 handshake namespace can never cross: a bootstrap code can never be
/// redeemed at <c>/auth/redeem</c> (which returns a claim set) and a handshake code can never be
/// redeemed at <c>/auth/bootstrap/redeem</c>.
/// <para>
/// What it adds to the primitive is the optional <see cref="BootstrapContinuation"/> the bootstrap
/// code must carry to redemption, so the portal learns the app callback and the D6 code from the
/// redemption response (D16) instead of from a browser-visible parameter. The continuation is
/// removed with any redemption attempt of its code and is swept on the same TTL as the code itself,
/// so custody stays bounded — the standard the round-A store review set.
/// </para>
/// </summary>
public sealed class BootstrapCodeStore
{
    private const int MaxLiveContinuations = 1024;
    private const int SweepEveryNCreates = 256;

    private readonly OneTimeCodeStore _codes;
    private readonly ConcurrentDictionary<string, (BootstrapContinuation Continuation, DateTimeOffset ExpiresAtUtc)> _continuations
        = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ttl;

    private long _createdCount;

    public BootstrapCodeStore(TimeProvider timeProvider, IOptions<AuthServiceOptions> options)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        _timeProvider = timeProvider;
        _ttl = options.Value.OneTimeCodeTtl;
        _codes = new OneTimeCodeStore(timeProvider, options);
    }

    /// <summary>
    /// Issues a bootstrap code bound to <paramref name="redirectUri"/> (the portal's bootstrap
    /// redemption URI) for <paramref name="sessionId"/>, carrying
    /// <paramref name="continuation"/> when the sign-in began at a Blazor app.
    /// </summary>
    public string Create(string redirectUri, string sessionId, BootstrapContinuation? continuation = null)
    {
        var code = _codes.Create(redirectUri, sessionId);

        if (continuation is not null)
        {
            _continuations[code] = (continuation, _timeProvider.GetUtcNow() + _ttl);

            var created = Interlocked.Increment(ref _createdCount);
            if (created % SweepEveryNCreates == 0 || _continuations.Count > MaxLiveContinuations)
            {
                SweepExpiredContinuations();
            }
        }

        return code;
    }

    /// <summary>
    /// Redeems a bootstrap code exactly once. Any attempt — success, replay, expiry or a
    /// redirect-URI mismatch — also drops the code's continuation: after the code is gone the
    /// continuation can never be handed out again.
    /// </summary>
    public BootstrapRedemptionResult Redeem(string code, string redirectUri)
    {
        var redemption = _codes.Redeem(code, redirectUri);
        _continuations.TryRemove(code, out var continuation);

        return redemption.Status == RedeemStatus.Success
            ? new BootstrapRedemptionResult(RedeemStatus.Success, redemption.Entry!.SessionId, continuation.Continuation)
            : new BootstrapRedemptionResult(redemption.Status);
    }

    /// <summary>
    /// Reclaims continuations whose code has outlived its own TTL — the ones a sweep in the code
    /// store already dropped, and any code that was simply never redeemed.
    /// </summary>
    private void SweepExpiredContinuations()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var (key, entry) in _continuations)
        {
            if (entry.ExpiresAtUtc <= now)
            {
                _continuations.TryRemove(new KeyValuePair<string, (BootstrapContinuation, DateTimeOffset)>(key, entry));
            }
        }
    }
}
