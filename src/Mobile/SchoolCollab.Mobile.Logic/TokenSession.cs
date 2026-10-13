using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace SchoolCollab.Mobile.Logic;

/// <summary>
/// A Keycloak token-endpoint response — the shape both grants the head performs return
/// (authorization-code exchange in <see cref="OidcLoginService"/>, refresh in
/// <see cref="TokenSession"/>), so the wire parsing lives here once.
/// </summary>
/// <param name="AccessToken">The bearer credential presented to the APIs.</param>
/// <param name="RefreshToken">The renewal credential; absent on responses that do not rotate it.</param>
/// <param name="ExpiresInSeconds">The access token's lifetime in seconds, as the endpoint reported it.</param>
public sealed record TokenResponse(string AccessToken, string? RefreshToken, int ExpiresInSeconds)
{
    private const string AccessTokenProperty = "access_token";
    private const string RefreshTokenProperty = "refresh_token";
    private const string ExpiresInProperty = "expires_in";
    private const string ErrorProperty = "error";

    /// <summary>Parses a successful token-endpoint body.</summary>
    /// <exception cref="FormatException">The body is not a token response carrying an access token.</exception>
    public static TokenResponse Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        var accessToken = ReadString(document.RootElement, AccessTokenProperty)
            ?? throw new FormatException($"The token response carries no '{AccessTokenProperty}'.");

        return new TokenResponse(
            accessToken,
            ReadString(document.RootElement, RefreshTokenProperty),
            ReadInt32(document.RootElement, ExpiresInProperty));
    }

    /// <summary>
    /// The <c>error</c> code of a rejected token-endpoint body (e.g. <c>invalid_grant</c>), or
    /// <see langword="null"/> when the body is not a JSON object carrying one. Only the error
    /// CODE is ever read — never a credential-shaped field — so it is safe to name in an
    /// exception message.
    /// </summary>
    public static string? TryReadErrorCode(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? ReadString(document.RootElement, ErrorProperty)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;

    private static int ReadInt32(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var value)
                ? value
                : 0;
}

/// <summary>
/// The session ended: the refresh token was rejected by the token endpoint, so the stored session
/// has been cleared and an interactive sign-in is required.
/// </summary>
/// <remarks>
/// This is the device-side parity of the spec's <c>session_ended</c>/410 note: one code path for
/// "the IdP no longer accepts this session". The message names *what* failed and never carries a
/// token, a refresh token, or <c>Authorization</c> header content.
/// </remarks>
public sealed class TokenSessionExpiredException(string message) : Exception(message);

/// <summary>
/// The device-side token session: the access token plus its usable-until instant, backed by
/// <see cref="ITokenStore"/> and refreshed on demand (the round's OD-1 decision — refresh where a
/// request needs it, never on a background timer).
/// </summary>
/// <remarks>
/// <para>The single signal to the UI is <see cref="SessionChanged"/>: it fires on sign-in
/// (<see cref="ApplyTokensAsync"/>), on an accepted refresh, and on session end
/// (<see cref="ClearAsync"/>, including the rejected-refresh path). The client-side
/// <see cref="MobileAuthenticationStateProvider"/> subscribes to it and raises
/// <c>NotifyAuthenticationStateChanged</c> — one direction only, so nothing loops.</para>
/// <para><b>A keystore failure never outlives the session.</b> Sign-out drops the in-memory
/// session <i>before</i> it touches the store, then sweeps the store best-effort; and a session
/// whose write the store refused is discarded rather than kept in memory. Either way the app
/// degrades to unauthenticated — it never holds, nor presents, a credential the device's keystore
/// refused (see <see cref="ClearAsync"/> and <see cref="ApplyTokensAsync"/>).</para>
/// <para>No member of this type logs or places token material in an exception message.</para>
/// </remarks>
/// <param name="tokenStore">The device's token custody.</param>
/// <param name="httpClientFactory">Supplies the <see cref="HttpClientName"/> client used for refreshes.</param>
/// <param name="options">The credential settings (authority, client id).</param>
/// <param name="timeProvider">The testable clock (the repo's <c>TimeProvider</c> convention).</param>
public sealed class TokenSession(
    ITokenStore tokenStore,
    IHttpClientFactory httpClientFactory,
    MobileAuthOptions options,
    TimeProvider timeProvider)
{
    /// <summary>
    /// The named <see cref="HttpClient"/> the token requests go through; its base address is the
    /// configured authority, bound at registration.
    /// </summary>
    public const string HttpClientName = "oidc-token";

    /// <summary>
    /// How much of the access token's lifetime is written off against clock skew: a token that
    /// "expires in 5 seconds" on the server may already be expired on a device whose clock is a
    /// little fast, so it is treated as expired 30 seconds early.
    /// </summary>
    private const int ExpiryClockSkewSeconds = 30;

    private const string RefreshGrantType = "refresh_token";
    private const string ClientIdFormKey = "client_id";
    private const string RefreshTokenFormKey = "refresh_token";
    private const string GrantTypeFormKey = "grant_type";

    /// <summary>
    /// Every key a session persists: session end sweeps exactly this set, so a key the session can
    /// write cannot silently survive the clear.
    /// </summary>
    private static readonly string[] StoredKeys = [TokenKeys.AccessToken, TokenKeys.RefreshToken, TokenKeys.ExpiresAt];

    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private string? _accessToken;
    private string? _refreshToken;
    private DateTimeOffset _usableUntil = DateTimeOffset.MinValue;
    private bool _initialized;

    /// <summary>
    /// Raised when the session starts, is renewed, or ends. The UI subscribes (see the type
    /// remarks); nothing here subscribes back.
    /// </summary>
    public event Action? SessionChanged;

    /// <summary>The current access token, or <see langword="null"/> when the session holds none.</summary>
    public string? AccessToken => _accessToken;

    /// <summary>
    /// Loads the persisted session into memory, once per instance. Safe to call from anywhere: the
    /// store is read at most once.
    /// </summary>
    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializeGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            _accessToken = await tokenStore.ReadAsync(TokenKeys.AccessToken, cancellationToken);
            _refreshToken = await tokenStore.ReadAsync(TokenKeys.RefreshToken, cancellationToken);
            _usableUntil = await ReadUsableUntilAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    /// <summary>
    /// The bearer token for one outgoing request: the cached access token while it is usable, a
    /// silently refreshed one when it is not, or <see langword="null"/> when there is nothing to
    /// send (no session, or no refresh token to renew with). At most one refresh attempt is made
    /// per call.
    /// </summary>
    /// <exception cref="TokenSessionExpiredException">The refresh token was rejected; the stored
    /// session has been cleared.</exception>
    /// <exception cref="TokenStoreException">The device's keystore refused a write while the token
    /// was being renewed. The session has been discarded, so nothing is sent; the typed signal is
    /// thrown for the caller, and an HTTP pipeline treats it as "no session"
    /// (<see cref="BearerTokenDelegatingHandler"/>).</exception>
    public async Task<string?> GetBearerTokenAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (IsAccessTokenUsable)
        {
            return _accessToken;
        }

        if (string.IsNullOrEmpty(_refreshToken))
        {
            return null;
        }

        await RefreshAsync(cancellationToken);
        return IsAccessTokenUsable ? _accessToken : null;
    }

    /// <summary>
    /// Renews the access token from the stored refresh token
    /// (<c>grant_type=refresh_token</c>). A no-op when the session holds no refresh token or when
    /// another caller has already renewed the token; concurrent callers collapse onto one request.
    /// </summary>
    /// <exception cref="TokenSessionExpiredException">The token endpoint rejected the refresh; the
    /// stored session has been cleared, which ends the session.</exception>
    /// <exception cref="TokenStoreException">The device's keystore refused a write of the renewed
    /// session; the in-memory session has been discarded (see <see cref="ApplyTokensAsync"/>).</exception>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            if (IsAccessTokenUsable || string.IsNullOrEmpty(_refreshToken))
            {
                return;
            }

            var form = new Dictionary<string, string>
            {
                [GrantTypeFormKey] = RefreshGrantType,
                [ClientIdFormKey] = options.ClientId ?? string.Empty,
                [RefreshTokenFormKey] = _refreshToken,
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, options.BuildTokenEndpointUri())
            {
                Content = new FormUrlEncodedContent(form),
            };

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // The session is over: clear the store so every later call is unauthenticated and
                // the UI's next auth-state read shows the signed-out state. A keystore that refuses
                // the removal is REPORTED by ClearAsync (never thrown), because the exception below
                // is the signal this path owes its caller. The message names the failure and the
                // endpoint's error CODE only — never a credential value.
                await ClearAsync(cancellationToken);
                throw new TokenSessionExpiredException(
                    "The refresh token was rejected by the token endpoint " +
                    $"(HTTP {(int)response.StatusCode}{DescribeErrorCode(TokenResponse.TryReadErrorCode(body))}).");
            }

            await ApplyTokensAsync(TokenResponse.Parse(body), cancellationToken);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Adopts a token response: caches the access token with its usable-until instant, persists the
    /// values (including a rotated refresh token — an absent one keeps the current token), and
    /// signals <see cref="SessionChanged"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="tokens"/> is <see langword="null"/>.</exception>
    /// <exception cref="TokenStoreException">The device's keystore refused one of the session's
    /// writes. The adopted session has already been discarded — in memory and, best-effort, in the
    /// store — and <see cref="SessionChanged"/> has signalled the end, so the caller sees a typed
    /// failure and never a live session the store refused to hold.</exception>
    public async Task ApplyTokensAsync(TokenResponse tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        _accessToken = tokens.AccessToken;
        if (!string.IsNullOrEmpty(tokens.RefreshToken))
        {
            _refreshToken = tokens.RefreshToken;
        }

        _usableUntil = timeProvider.GetUtcNow()
            .AddSeconds(tokens.ExpiresInSeconds - ExpiryClockSkewSeconds);
        _initialized = true;

        var values = new List<(string Key, string Value)>
        {
            (TokenKeys.AccessToken, tokens.AccessToken),
        };

        if (!string.IsNullOrEmpty(_refreshToken))
        {
            values.Add((TokenKeys.RefreshToken, _refreshToken));
        }

        values.Add((TokenKeys.ExpiresAt, _usableUntil.ToString("O", CultureInfo.InvariantCulture)));

        var failure = await WriteStoreBestEffortAsync(values, cancellationToken);

        if (failure is not null)
        {
            // POLICY: a session the store refused is DISCARDED, not kept in memory until process
            // exit. The store is the session's custody, so a memory-only session would be handed
            // to the APIs by a later request — the app would present a credential the device could
            // not keep. So both halves go: memory first (nothing can resurrect it), then the store
            // best-effort (a partial write must not come back as a session at the next start).
            // Then the typed failure is thrown, and what it means depends on the caller: the
            // SIGN-IN path shows it in the UI, while the refresh path runs inside an in-flight API
            // call, where it means "no session" — the request goes out unauthenticated and the
            // server's 401 drives the sign-in prompt.
            DiscardInMemorySession();
            SessionChanged?.Invoke();
            await ClearStoreBestEffortAsync(cancellationToken);

            throw failure;
        }

        SessionChanged?.Invoke();
    }

    /// <summary>
    /// Ends the session: forgets the in-memory tokens <b>first</b>, signals
    /// <see cref="SessionChanged"/>, then removes every stored key best-effort. Sign-out is local
    /// only — there is no Keycloak end-session round-trip this round.
    /// </summary>
    /// <returns><see langword="null"/> when every stored key was removed, otherwise the typed
    /// <see cref="TokenStoreException"/> naming the first key the platform keystore refused. A
    /// refused removal is reported, never thrown: sign-out is decided in memory, so a keystore that
    /// refuses the removal must neither abort the sign-out nor leave a usable session behind — it
    /// cannot resurrect what this call has already forgotten. Nothing acts on the returned signal;
    /// a caller that wants to tell the user the device could not forget the credential reads it.
    /// </returns>
    public async Task<TokenStoreException?> ClearAsync(CancellationToken cancellationToken = default)
    {
        // Memory FIRST — the ordering is the security property, not style. However the keystore
        // behaves below, this call has already left no usable in-memory session behind: no
        // in-flight request can still be handed the credential the caller asked to end, and a
        // refused (or throwing) removal cannot abort the sign-out half-way and resurrect one.
        DiscardInMemorySession();

        // The signal belongs to the in-memory change, so it fires before the store is touched: even
        // a sweep the platform cancels mid-way has already told the UI the session is over.
        SessionChanged?.Invoke();

        return await ClearStoreBestEffortAsync(cancellationToken);
    }

    /// <summary>
    /// The half of session end that never depends on the store: the in-memory session is dropped,
    /// and the instance is marked initialized so nothing re-reads the store and quietly restores a
    /// session this call has just discarded.
    /// </summary>
    private void DiscardInMemorySession()
    {
        _accessToken = null;
        _refreshToken = null;
        _usableUntil = DateTimeOffset.MinValue;
        _initialized = true;
    }

    /// <summary>
    /// Writes the adopted session's keys, best-effort: every entry is attempted, so one key the
    /// keystore rejects cannot leave the rest of the session unwritten. The first refusal is
    /// returned rather than thrown — <see cref="ApplyTokensAsync"/> decides what it means.
    /// </summary>
    /// <param name="values">The keys to write, in the order the session reads them back.</param>
    /// <param name="cancellationToken">The caller's token; cancellation is never swallowed.</param>
    private async Task<TokenStoreException?> WriteStoreBestEffortAsync(
        IReadOnlyList<(string Key, string Value)> values,
        CancellationToken cancellationToken)
    {
        TokenStoreException? failure = null;

        foreach (var (key, value) in values)
        {
            try
            {
                await tokenStore.WriteAsync(key, value, cancellationToken);
            }
            catch (TokenStoreException exception)
            {
                failure ??= exception;
            }
        }

        return failure;
    }

    /// <summary>
    /// Removes every stored key, best-effort: every key is attempted, so one key the keystore
    /// rejects cannot leave the others behind. The first refusal is returned rather than thrown —
    /// see <see cref="ClearAsync"/> for what the caller does with it.
    /// </summary>
    private async Task<TokenStoreException?> ClearStoreBestEffortAsync(CancellationToken cancellationToken)
    {
        TokenStoreException? failure = null;

        foreach (var key in StoredKeys)
        {
            try
            {
                await tokenStore.ClearAsync(key, cancellationToken);
            }
            catch (TokenStoreException exception)
            {
                failure ??= exception;
            }
        }

        return failure;
    }

    private bool IsAccessTokenUsable =>
        !string.IsNullOrEmpty(_accessToken) && _usableUntil > timeProvider.GetUtcNow();

    private async Task<DateTimeOffset> ReadUsableUntilAsync(CancellationToken cancellationToken)
    {
        var raw = await tokenStore.ReadAsync(TokenKeys.ExpiresAt, cancellationToken);
        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
                ? parsed
                : DateTimeOffset.MinValue;
    }

    private static string DescribeErrorCode(string? errorCode)
        => errorCode is null ? string.Empty : $" ({errorCode})";
}
