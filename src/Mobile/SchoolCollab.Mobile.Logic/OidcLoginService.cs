using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace SchoolCollab.Mobile.Logic;

/// <summary>Where an interactive sign-in currently stands, for the sign-in control's own state.</summary>
public enum SignInState
{
    /// <summary>No session. The UI shows the sign-in affordance and the gates their fallback.</summary>
    SignedOut,

    /// <summary>The system browser is open; the app is waiting for the redirect.</summary>
    SigningIn,

    /// <summary>A session is held; the gates can read the token's claims.</summary>
    SignedIn,
}

/// <summary>
/// The sign-in could not complete: the platform could not carry out the browser hand-off, the
/// authorization response failed validation (its <c>state</c> did not match, or it carried no
/// code), the code exchange failed at the token endpoint, or that endpoint could not be reached at
/// all. No token was persisted either way.
/// </summary>
/// <remarks>The message names what failed — never a token, a refresh token, or header content. This
/// is the ONE type every failed sign-in leaves the flow as, on purpose: the page's message bar owns
/// it, so no sign-in outcome can reach the page's <c>&lt;ErrorBoundary&gt;</c> and replace the whole
/// page — sign-in control included.</remarks>
public sealed class SignInFailedException(string message) : Exception(message);

/// <summary>
/// The device's interactive OIDC sign-in: an Authorization Code + PKCE flow in the system browser.
/// </summary>
/// <remarks>
/// <para><b>The security core.</b> The <c>code_verifier</c> is minted here, sent only to the token
/// endpoint, and never persisted; the <c>code_challenge</c> is its SHA-256 (PKCE <c>S256</c>, plain
/// — no <c>plain</c> method). Together with the random <c>state</c> this is the deliberate
/// mitigation for the custom redirect scheme's hijack exposure (any app may register the same
/// filter): a captured redirect yields a code that cannot be redeemed without the in-memory
/// verifier, and a forged callback fails the state match before any token is stored. See
/// <see cref="MobileAuthOptions"/>' remarks.</para>
/// <para><b>No <c>nonce</c>, deliberately.</b> The client-side provider does not verify the
/// id_token's signature (see <see cref="JwtPayloadReader"/>), so a nonce check would be theatre.
/// PKCE + <c>state</c> are the replay/interception defences that matter for a native code flow.</para>
/// <para>Sign-out is local only (clear the store); the Keycloak end-session round-trip is out of
/// this round's scope.</para>
/// </remarks>
/// <param name="options">The credential settings (authority, client id, scheme/host, scope).</param>
/// <param name="authenticator">The system-browser acquirer (platform-implemented).</param>
/// <param name="session">The session the acquired tokens are applied to.</param>
/// <param name="httpClientFactory">Supplies the <see cref="TokenSession.HttpClientName"/> client used for the code exchange.</param>
public sealed class OidcLoginService(
    MobileAuthOptions options,
    ISystemBrowserAuthenticator authenticator,
    TokenSession session,
    IHttpClientFactory httpClientFactory)
{
    /// <summary>RFC 7636 requires a verifier of 43–128 characters; 32 random bytes encode to 43.</summary>
    private const int CodeVerifierBytes = 32;

    private const int StateBytes = 16;
    private const string AuthorizationCodeGrantType = "authorization_code";
    private const string S256ChallengeMethod = "S256";

    private const string GrantTypeFormKey = "grant_type";
    private const string ClientIdFormKey = "client_id";
    private const string CodeFormKey = "code";
    private const string RedirectUriFormKey = "redirect_uri";
    private const string CodeVerifierFormKey = "code_verifier";

    private const string StateResponseKey = "state";
    private const string CodeResponseKey = "code";

    /// <summary>
    /// The one message both transport failures of the code exchange carry: what failed, and the
    /// reassurance that no session came out of it. It names no endpoint, no value, and no header.
    /// </summary>
    private const string UnreachableTokenEndpointMessage =
        "The token endpoint could not be reached (the connection failed or timed out), so the " +
        "sign-in did not complete — no token was persisted.";

    /// <summary>Where the current interactive sign-in stands.</summary>
    public SignInState State { get; private set; } = SignInState.SignedOut;

    /// <summary>
    /// Runs one interactive sign-in: build the authorize request, open the system browser, verify
    /// the returned <c>state</c> and <c>code</c>, exchange the code for tokens, and apply them to
    /// the session (which signals the auth-state change).
    /// </summary>
    /// <exception cref="SignInCancelledException">The user dismissed the browser.</exception>
    /// <exception cref="SignInFailedException">The authorization response was rejected, or the code
    /// exchange failed. Nothing was persisted.</exception>
    public async Task SignInAsync(CancellationToken cancellationToken = default)
    {
        State = SignInState.SigningIn;
        try
        {
            var codeVerifier = CreateCodeVerifier();
            var codeChallenge = CreateCodeChallenge(codeVerifier);
            var state = CreateState();

            // The realm-registered literal, NOT a normalized Uri: Keycloak matches redirect URIs
            // exactly, and a pathless custom-scheme Uri normalizes to `.../signin-oidc/`.
            var redirectUri = options.BuildRedirectUriString();

            var response = await authenticator.AuthenticateAsync(
                BuildAuthorizeUrl(codeChallenge, state, redirectUri),
                options.BuildRedirectUri(),
                cancellationToken);

            if (!response.TryGetValue(StateResponseKey, out var returnedState)
                || !string.Equals(returnedState, state, StringComparison.Ordinal))
            {
                throw new SignInFailedException(
                    "The authorization response's state did not match the request's — no token was persisted.");
            }

            if (!response.TryGetValue(CodeResponseKey, out var code) || string.IsNullOrWhiteSpace(code))
            {
                throw new SignInFailedException(
                    "The authorization response carried no authorization code — no token was persisted.");
            }

            await session.ApplyTokensAsync(
                await ExchangeCodeAsync(code, codeVerifier, redirectUri, cancellationToken),
                cancellationToken);

            State = SignInState.SignedIn;
        }
        catch
        {
            // Cancellation, a rejected authorization response, or a rejected code exchange: all of
            // them leave the session exactly as it was. Nothing is persisted unless the exchange
            // succeeded, and the original exception (typed) reaches the UI unchanged.
            State = SignInState.SignedOut;
            throw;
        }
    }

    /// <summary>
    /// Ends the session locally: the store is cleared and the auth state is re-notified. There is
    /// no Keycloak end-session redirect this round.
    /// </summary>
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await session.ClearAsync(cancellationToken);
        State = SignInState.SignedOut;
    }

    private Uri BuildAuthorizeUrl(string codeChallenge, string state, string redirectUri)
    {
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            [ClientIdFormKey] = options.ClientId ?? string.Empty,
            [RedirectUriFormKey] = redirectUri,
            ["scope"] = options.ResolveScope(),
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = S256ChallengeMethod,
            [StateResponseKey] = state,
        };

        var builder = new UriBuilder(options.BuildAuthorizeEndpointUri())
        {
            Query = string.Join('&', query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}")),
        };

        return builder.Uri;
    }

    private async Task<TokenResponse> ExchangeCodeAsync(
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            [GrantTypeFormKey] = AuthorizationCodeGrantType,
            [ClientIdFormKey] = options.ClientId ?? string.Empty,
            [CodeFormKey] = code,
            [RedirectUriFormKey] = redirectUri,
            // The verifier's only journey: from memory into this request body. It is never written
            // to the token store, so a stolen redirect (or a stolen store) cannot redeem a code.
            [CodeVerifierFormKey] = codeVerifier,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, options.BuildTokenEndpointUri())
        {
            Content = new FormUrlEncodedContent(form),
        };

        var client = httpClientFactory.CreateClient(TokenSession.HttpClientName);

        HttpResponseMessage? response = null;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Names the status and the endpoint's error CODE only — never a credential value.
                var errorCode = TokenResponse.TryReadErrorCode(body);
                throw new SignInFailedException(
                    "The token endpoint rejected the code exchange " +
                    $"(HTTP {(int)response.StatusCode}{(errorCode is null ? string.Empty : $" ({errorCode})")}).");
            }

            return TokenResponse.Parse(body);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TimeoutException or IOException)
        {
            // The token endpoint was unreachable — DNS, socket, TLS, a timeout, or a read that
            // failed mid-response — so the exchange produced no credential. This is the same
            // normalisation the refresh path applies (BearerTokenDelegatingHandler): the transport
            // failure becomes the typed sign-in failure the page already surfaces in its message
            // bar, so it can never escape into the page's <ErrorBoundary> and replace the whole
            // page — sign-in button included. Nothing is logged and no value is named.
            throw new SignInFailedException(UnreachableTokenEndpointMessage);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The token client's OWN deadline ended the attempt. Measured on this stack that is the
            // shape an HttpClient timeout actually takes (a TaskCanceledException, never a bare
            // TimeoutException — see BearerTokenDelegatingHandlerTests), and it is the same sign-in
            // outcome as the unreachable endpoint above. The guard is what keeps this a sign-in
            // outcome instead of swallowing the CALLER's cancellation, which must keep propagating.
            throw new SignInFailedException(UnreachableTokenEndpointMessage);
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static string CreateCodeVerifier() => Base64Url(RandomNumberGenerator.GetBytes(CodeVerifierBytes));

    private static string CreateCodeChallenge(string codeVerifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

    private static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(StateBytes));

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
