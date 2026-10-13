using System.Net.Http;

namespace SchoolCollab.Mobile.Logic;

/// <summary>
/// Attaches the session's access token to every outgoing API call as
/// <c>Authorization: Bearer &lt;token&gt;</c> — the device's replacement for the server-side
/// cookie/bearer estate. A request that already carries the header is left alone.
/// </summary>
/// <remarks>
/// <para><b>Refresh policy (OD-1).</b> The token is fetched on demand: <see cref="TokenSession"/>
/// returns the cached one while it is usable and silently refreshes it when it is not. A failed
/// refresh sends the request <i>unauthenticated</i> and lets the server answer 401 — the server's
/// 401 is the truth, and the UI's interactive sign-in is the fallback. There is deliberately
/// <b>no automatic resend of a 401'd request</b> this round: a resend loop on a rejected credential
/// is how a 401 storm starts.</para>
/// <para><b>A store failure is "no session".</b> The device's keystore can refuse a write on the
/// same path (see <see cref="TokenSession.ApplyTokensAsync"/>), which discards the session and
/// signals the typed <see cref="TokenStoreException"/> instead of letting it escape.</para>
/// <para><b>An unreachable token endpoint is "no session" too.</b> A refresh can also die in
/// transport — <see cref="HttpRequestException"/>, <see cref="TimeoutException"/>,
/// <see cref="IOException"/>, or a <see cref="TaskCanceledException"/> raised by the token
/// client's own <see cref="HttpClient.Timeout"/> — and that produced no credential either, so it
/// degrades the same way: unauthenticated send, server 401. Neither a storage hiccup nor a network
/// hiccup can become a transport failure for the caller's in-flight request. The transport cases
/// deliberately leave the session intact — an unreachable endpoint says nothing about the refresh
/// token's validity, so a later request may retry the refresh; the two typed cases cannot retry,
/// because both have already discarded the session.</para>
/// <para><b>Cancellation is not a session outcome.</b> An <see cref="HttpClient"/> timeout does
/// <i>not</i> surface as a <see cref="TimeoutException"/> on this stack — it surfaces as a
/// <see cref="TaskCanceledException"/> whose inner exception is the timeout — so that clause is
/// guarded on the caller's token: only the token client's own deadline degrades, while a
/// <i>caller</i>-cancelled request still sees its cancellation and is never quietly re-sent
/// unauthenticated behind the caller's back.</para>
/// <para>Registered <b>transient</b>, like every delegating handler in this repo:
/// <c>IHttpClientFactory</c> builds a fresh handler instance per outgoing request through the
/// client's handler pipeline, so a handler must never capture per-request state.</para>
/// <para>Nothing on this path logs, and no token material is ever placed in an exception message.</para>
/// </remarks>
/// <param name="session">The token session the bearer value comes from.</param>
public sealed class BearerTokenDelegatingHandler(TokenSession session) : DelegatingHandler
{
    private const string AuthorizationHeaderName = "Authorization";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Headers.Contains(AuthorizationHeaderName))
        {
            string? token = null;
            try
            {
                token = await session.GetBearerTokenAsync(cancellationToken);
            }
            catch (TokenSessionExpiredException)
            {
                // The refresh token was rejected, so the session has already been cleared. Send the
                // request unauthenticated: the API's 401 is the authoritative answer and the UI
                // turns it into a sign-in prompt. Nothing to log, and no token to name.
            }
            catch (TokenStoreException)
            {
                // The device's keystore refused a write while the session was being renewed, so
                // TokenSession has discarded the session and this is its typed report. A store
                // failure is "no session", never a transport failure: the request goes out
                // unauthenticated, and the API's 401 is the answer. Nothing to log — the message
                // names a key and a platform exception type, never a stored value.
            }
            catch (Exception exception)
                when (exception is HttpRequestException or TimeoutException or IOException)
            {
                // The token endpoint was unreachable — DNS, socket, TLS, a timeout, or a read that
                // failed mid-response — so the refresh produced no credential. That is a transport
                // failure of the REFRESH, not a verdict on the session, and it must not become a
                // transport failure for the caller's in-flight API request: the request goes out
                // unauthenticated and the API's 401 is the authoritative answer, exactly as for the
                // two typed failures above. The session stays intact on purpose — an unreachable
                // endpoint says nothing about the refresh token's validity, so a later request may
                // retry the refresh. Nothing to log — no value on this path is ever placed in a
                // message; only the exception type would be named.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The token client's OWN timeout ended the attempt, which is the shape an
                // HttpClient deadline actually takes (a TaskCanceledException carrying an inner
                // TimeoutException — measured, never a bare TimeoutException). Same outcome as the
                // transport failure above: no credential was produced, so the request goes out
                // unauthenticated and the API's 401 is the answer. The guard is what keeps this a
                // session outcome instead of swallowing the caller's cancellation, which must keep
                // propagating. Nothing to log.
            }

            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.TryAddWithoutValidation(AuthorizationHeaderName, $"Bearer {token}");
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
