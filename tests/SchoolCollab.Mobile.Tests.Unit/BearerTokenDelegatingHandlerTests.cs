using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Mobile.Logic;

namespace SchoolCollab.Mobile.Tests.Unit;

/// <summary>
/// Covers <see cref="BearerTokenDelegatingHandler"/> — the device's presentation of the session's
/// access token, and the OD-1 refresh policy seen from the request side: refresh on demand before the
/// send, and never a second attempt after the server said 401.
/// </summary>
[TestClass]
public class BearerTokenDelegatingHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly Uri RequestUri =
        new UriBuilder(Uri.UriSchemeHttps, "settings.example", 7460, "api/coded-values").Uri;

    [TestMethod]
    public async Task SendAsync_AttachesTheStoredTokenAsABearerHeader()
    {
        var fixture = await Fixture.CreateAsync(accessToken: "stored-access-token", expiresIn: TimeSpan.FromMinutes(5));

        using var response = await fixture.Client.GetAsync(RequestUri);

        fixture.PrimaryHandler.AuthorizationHeader.Should().Be("Bearer stored-access-token");
        fixture.PrimaryHandler.RequestCount.Should().Be(1);
    }

    [TestMethod]
    public async Task SendAsync_SendsWithoutTheHeader_WhenTheSessionHoldsNoToken()
    {
        var fixture = await Fixture.CreateAsync();

        using var response = await fixture.Client.GetAsync(RequestUri);

        fixture.PrimaryHandler.AuthorizationHeader.Should().BeNull(
            "there is no credential to attach; the API's 401 is the authoritative answer.");
    }

    [TestMethod]
    public async Task SendAsync_RefreshesAnExpiredToken_AndSendsWithTheRefreshedOne()
    {
        var fixture = await Fixture.CreateAsync(
            accessToken: "expired-access-token",
            expiresIn: TimeSpan.FromMinutes(-5),
            refreshToken: "stored-refresh-token");

        using var response = await fixture.Client.GetAsync(RequestUri);

        fixture.TokenHandler.Requests.Should().HaveCount(1, "one refresh, then the send.");
        fixture.PrimaryHandler.AuthorizationHeader.Should().Be("Bearer refreshed-access-token");
        fixture.PrimaryHandler.RequestCount.Should().Be(1, "a successful refresh sends the request once — never twice.");
    }

    [TestMethod]
    public async Task SendAsync_SendsWithoutTheHeader_WhenTheRefreshIsRejected()
    {
        var fixture = await Fixture.CreateAsync(
            accessToken: "expired-access-token",
            expiresIn: TimeSpan.FromMinutes(-5),
            refreshToken: "rejected-refresh-token");
        fixture.TokenHandler.Status = HttpStatusCode.BadRequest;
        fixture.TokenHandler.Body = """{"error":"invalid_grant"}""";

        using var response = await fixture.Client.GetAsync(RequestUri);

        fixture.PrimaryHandler.AuthorizationHeader.Should().BeNull(
            "the session ended, so the request goes unauthenticated and the server's 401 drives the UI's sign-in prompt.");
        fixture.PrimaryHandler.RequestCount.Should().Be(1);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the handler must not turn a dead session into a transport failure.");
        fixture.Store.Values.Should().OnlyContain(value => value == null, "an expired session is cleared, not kept.");
    }

    /// <summary>
    /// R8's store-failure leg on the request path: the refresh succeeds at the IdP but the device's
    /// keystore refuses to keep the renewed session. The typed refusal must not escape into the
    /// caller's in-flight API call — the request still goes out, unauthenticated, and the API's 401
    /// is the answer — and the session is discarded rather than held in memory, so the app never
    /// presents a token the store refused.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_SendsWithoutTheHeader_WhenTheStoreRefusesTheRenewedSession()
    {
        var fixture = await Fixture.CreateAsync(
            accessToken: "expired-access-token",
            expiresIn: TimeSpan.FromMinutes(-5),
            refreshToken: "stored-refresh-token");
        fixture.TokenHandler.Status = HttpStatusCode.OK;
        fixture.Store.RefusesWrite = true;

        using var response = await fixture.Client.GetAsync(RequestUri);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "a store failure is a session outcome, never a transport failure for the caller's request.");
        fixture.PrimaryHandler.RequestCount.Should().Be(1, "the request is still sent — unauthenticated.");
        fixture.PrimaryHandler.AuthorizationHeader.Should().BeNull(
            "the session was discarded instead of kept in memory, so no bearer header can be built from it.");
        fixture.TokenHandler.Requests.Should().HaveCount(1, "one refresh attempt; a store failure is not retried.");
        fixture.Store.Values.Should().OnlyContain(value => value == null,
            "the refused session is swept from the store too, so it cannot come back at the next start.");
    }

    /// <summary>
    /// The transport leg of the refresh policy, on the request path: the refresh dies in transport —
    /// the token endpoint is unreachable — instead of being rejected. The handler must degrade
    /// exactly as it does for the two typed failures: one unauthenticated send, the API's 401 as the
    /// answer, and no exception escaping into the caller's in-flight API request. The session is
    /// deliberately NOT ended on this path (unlike both typed cases): an unreachable endpoint is no
    /// verdict on the refresh token, so the session survives for a later retry.
    /// </summary>
    [TestMethod]
    [DataRow(TransportFailure.RequestFailed)]
    [DataRow(TransportFailure.TimedOut)]
    [DataRow(TransportFailure.ReadFailed)]
    public async Task SendAsync_SendsWithoutTheHeader_WhenTheRefreshDiesInTransport(
        TransportFailure failure)
    {
        var fixture = await Fixture.CreateAsync(
            accessToken: "expired-access-token",
            expiresIn: TimeSpan.FromMinutes(-5),
            refreshToken: "stored-refresh-token",
            tokenEndpointFailure: TransportException(failure));

        using var response = await fixture.Client.GetAsync(RequestUri);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "a refresh the network could not complete is a session outcome, never a transport failure.");
        fixture.PrimaryHandler.RequestCount.Should().Be(1, "the request is still sent — unauthenticated, once.");
        fixture.PrimaryHandler.AuthorizationHeader.Should().BeNull(
            "the refresh produced no credential, so there is no bearer header to attach.");
        fixture.TokenHandler.Requests.Should().HaveCount(
            1,
            "one refresh attempt; a transport failure is not retried, and no 401 resend follows.");
        (await fixture.Store.ReadAsync(TokenKeys.RefreshToken)).Should().Be(
            "stored-refresh-token",
            "an unreachable endpoint is no verdict on the session, so the token is kept for a later attempt.");
    }

    /// <summary>
    /// The leg the three thrown shapes above cannot reach: the token endpoint does not refuse, it
    /// HANGS, and the token client's own <see cref="HttpClient.Timeout"/> ends the attempt. Measured
    /// on this stack, that surfaces as a <see cref="TaskCanceledException"/> whose inner exception
    /// is the timeout — never as a bare <see cref="TimeoutException"/> — so the guarded clause is
    /// the only thing standing between a dead network and a transport failure in the caller's
    /// in-flight request.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_SendsWithoutTheHeader_WhenTheRefreshTimesOutOnTheClient()
    {
        var fixture = await Fixture.CreateAsync(
            accessToken: "expired-access-token",
            expiresIn: TimeSpan.FromMinutes(-5),
            refreshToken: "stored-refresh-token",
            hangs: true,
            tokenEndpointTimeout: TimeSpan.FromMilliseconds(50));

        using var response = await fixture.Client.GetAsync(RequestUri);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "a refresh the token client's own deadline abandoned is a session outcome, never a transport failure.");
        fixture.PrimaryHandler.RequestCount.Should().Be(1, "the request is still sent — unauthenticated, once.");
        fixture.PrimaryHandler.AuthorizationHeader.Should().BeNull(
            "the refresh produced no credential, so there is no bearer header to attach.");
        (await fixture.Store.ReadAsync(TokenKeys.RefreshToken)).Should().Be(
            "stored-refresh-token",
            "a timeout is no verdict on the session, so the token is kept for a later attempt.");
    }

    /// <summary>
    /// The guard's other half, without which the clause above would swallow every cancellation: a
    /// <see cref="TaskCanceledException"/> that is the CALLER's cancellation must still propagate. The
    /// token endpoint hangs long enough for the caller to cancel, and the in-flight API request must
    /// die with that cancellation — never be quietly sent unauthenticated behind the caller's back.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_Rethrows_WhenTheCallerCancelsTheRefresh()
    {
        var fixture = await Fixture.CreateAsync(
            accessToken: "expired-access-token",
            expiresIn: TimeSpan.FromMinutes(-5),
            refreshToken: "stored-refresh-token",
            hangs: true);
        using var cancellation = new CancellationTokenSource();

        var send = fixture.Client.GetAsync(RequestUri, cancellation.Token);
        await fixture.TokenHandler.Entered;
        await cancellation.CancelAsync();

        var act = async () =>
        {
            await send;
        };

        await act.Should().ThrowAsync<OperationCanceledException>(
            "the caller's cancellation is the caller's verdict — the guard must let it through.");
        fixture.PrimaryHandler.RequestCount.Should().Be(
            0,
            "a cancelled request is never re-sent unauthenticated behind the caller's back.");
    }

    [TestMethod]
    public async Task SendAsync_LeavesAnAlreadyPresentAuthorizationHeaderAlone()
    {
        var fixture = await Fixture.CreateAsync(accessToken: "stored-access-token", expiresIn: TimeSpan.FromMinutes(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, RequestUri);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer caller-supplied-token");

        using var response = await fixture.Client.SendAsync(request);

        fixture.PrimaryHandler.AuthorizationHeader.Should().Be("Bearer caller-supplied-token");
    }

    /// <summary>
    /// The converse of the header assertions above, for the NAMED client
    /// (<see cref="TokenSession.HttpClientName"/>) that <see cref="TokenSession"/> and
    /// <see cref="OidcLoginService"/> send their token requests through: it is registered bare, so
    /// the token endpoint never receives the session's own bearer credential. The session holds a
    /// LIVE token here on purpose — were <see cref="BearerTokenDelegatingHandler"/> ever added to
    /// that client, this request would carry the header and this test would fail.
    /// </summary>
    [TestMethod]
    public async Task TokenClient_SendsNoAuthorizationHeader_EvenWhileTheSessionHoldsALiveToken()
    {
        var store = new FakeTokenStore();
        await store.WriteAsync(TokenKeys.AccessToken, "stored-access-token");
        await store.WriteAsync(
            TokenKeys.ExpiresAt,
            Now.Add(TimeSpan.FromMinutes(5)).ToString("O", CultureInfo.InvariantCulture));

        var authOptions = AuthOptions();
        var recording = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<ITokenStore>(store);
        services.AddMobileAuth(authOptions);

        // Registered exactly as the head's AddMobileAdminClients registers it, so that a handler
        // added to THIS client would resolve and attach the header — the assertion below is then
        // about the header, not about a container that could not build the pipeline at all.
        services.AddTransient<BearerTokenDelegatingHandler>();
        services.AddHttpClient(TokenSession.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => recording);

        using var provider = services.BuildServiceProvider();
        var tokenClient = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(TokenSession.HttpClientName);

        // The token endpoint's own request shape, relative to the authority the named client binds.
        using var response = await tokenClient.PostAsync(
            "protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token" }));

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "the recording handler is the far end of the named client's real pipeline.");
        recording.RequestCount.Should().Be(1);
        recording.RequestUri.Should().Be(
            authOptions.BuildTokenEndpointUri(),
            "this is the endpoint TokenSession refreshes at: the named client's base address is the configured authority.");
        recording.AuthorizationHeader.Should().BeNull(
            "the token endpoint is the one call the bearer handler must never reach — the request that renews the credential must not present it.");
    }

    private sealed class Fixture
    {
        private Fixture(
            FakeTokenStore store,
            ScriptedHandler tokenHandler,
            RecordingHandler primaryHandler,
            TimeSpan? tokenEndpointTimeout)
        {
            Store = store;
            TokenHandler = tokenHandler;
            PrimaryHandler = primaryHandler;

            var factory = new StubHttpClientFactory(tokenHandler) { Timeout = tokenEndpointTimeout };
            var session = new TokenSession(store, factory, AuthOptions(), new MutableTimeProvider(Now));
            var handler = new BearerTokenDelegatingHandler(session) { InnerHandler = primaryHandler };
            Client = new HttpClient(handler);
        }

        public FakeTokenStore Store { get; }

        public ScriptedHandler TokenHandler { get; }

        public RecordingHandler PrimaryHandler { get; }

        public HttpClient Client { get; }

        /// <summary>A session with the given state, and the handler pipeline under test.</summary>
        /// <param name="hangs">The token endpoint never answers — the refresh runs until the client's
        /// own timeout, or the caller's cancellation, ends it.</param>
        /// <param name="tokenEndpointTimeout">The token client's own <see cref="HttpClient.Timeout"/>,
        /// left at its default when absent.</param>
        public static async Task<Fixture> CreateAsync(
            string? accessToken = null,
            TimeSpan expiresIn = default,
            string? refreshToken = null,
            Exception? tokenEndpointFailure = null,
            bool hangs = false,
            TimeSpan? tokenEndpointTimeout = null)
        {
            var store = new FakeTokenStore();
            if (accessToken is not null)
            {
                await store.WriteAsync(TokenKeys.AccessToken, accessToken);
                await store.WriteAsync(
                    TokenKeys.ExpiresAt,
                    Now.Add(expiresIn).ToString("O", CultureInfo.InvariantCulture));
            }

            if (refreshToken is not null)
            {
                await store.WriteAsync(TokenKeys.RefreshToken, refreshToken);
            }

            var tokenHandler = new ScriptedHandler(
                HttpStatusCode.OK,
                TokenBody("refreshed-access-token", "rotated-refresh-token"))
            {
                Failure = tokenEndpointFailure,
                Hangs = hangs,
            };

            return new Fixture(store, tokenHandler, new RecordingHandler(), tokenEndpointTimeout);
        }
    }

    /// <summary>
    /// The three transport shapes an unreachable token endpoint surfaces as — the ones the bearer
    /// handler degrades on (see <see cref="BearerTokenDelegatingHandler"/>).
    /// <see cref="TransportFailure.RequestFailed"/> is the realistic one on a device:
    /// Android's handler wraps DNS, socket and TLS failures in <see cref="HttpRequestException"/>.
    /// Public only because an MSTest method is public and a parameter type may not be less
    /// accessible than the method that takes it — no other type outside this class uses it.
    /// </summary>
    public enum TransportFailure
    {
        RequestFailed,
        TimedOut,
        ReadFailed,
    }

    private static Exception TransportException(TransportFailure failure) => failure switch
    {
        TransportFailure.RequestFailed =>
            new HttpRequestException("The token endpoint was unreachable."),
        TransportFailure.TimedOut =>
            new TimeoutException("The token endpoint did not answer in time."),
        _ => new IOException("The token endpoint's response could not be read."),
    };

    private static string TokenBody(string accessToken, string refreshToken) =>
        $$"""{"access_token":"{{accessToken}}","refresh_token":"{{refreshToken}}","expires_in":300,"token_type":"Bearer"}""";

    private static MobileAuthOptions AuthOptions() => new()
    {
        Authority = new UriBuilder(Uri.UriSchemeHttps, "idp.example", 8080, "realms/school-collab").Uri.ToString(),
        ClientId = "school-collab-mobile-client",
        RedirectScheme = "schoolcollab",
        RedirectHost = "signin-oidc",
    };

    /// <summary>The far end of the pipeline: records what actually arrived.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? AuthorizationHeader { get; private set; }

        public Uri? RequestUri { get; private set; }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestUri = request.RequestUri;
            AuthorizationHeader = request.Headers.TryGetValues("Authorization", out var values)
                ? string.Join(",", values)
                : null;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class FakeTokenStore : ITokenStore
    {
        private static readonly string[] Keys = [TokenKeys.AccessToken, TokenKeys.RefreshToken, TokenKeys.ExpiresAt];

        private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

        public IEnumerable<string?> Values => Keys.Select(key => _values.TryGetValue(key, out var value) ? value : null);

        /// <summary>When set, every write is refused — the keystore that cannot keep the renewed session.</summary>
        public bool RefusesWrite { get; set; }

        public Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);

        public Task WriteAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            if (RefusesWrite)
            {
                throw new TokenStoreException(
                    $"The device's token store refused the write of its '{key}' entry (PlatformException).");
            }

            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task ClearAsync(string key, CancellationToken cancellationToken = default)
        {
            _values[key] = null;
            return Task.CompletedTask;
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        /// <summary>
        /// When set, the token client's own <see cref="HttpClient.Timeout"/> — the real clock that
        /// turns a hung token endpoint into a client-timed-out refresh. Absent, the client keeps its
        /// default deadline, which no test here waits out.
        /// </summary>
        public TimeSpan? Timeout { get; set; }

        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(handler, disposeHandler: false);

            if (Timeout is not null)
            {
                client.Timeout = Timeout.Value;
            }

            return client;
        }
    }

    /// <summary>
    /// The token endpoint: records the refresh POST and answers with a canned response, or — when
    /// <see cref="Failure"/> is set — dies in transport without answering, or — when
    /// <see cref="Hangs"/> is set — never answers at all.
    /// </summary>
    private sealed class ScriptedHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HttpStatusCode Status { get; set; } = status;

        public string Body { get; set; } = body;

        /// <summary>When set, the attempt dies in transport instead of answering.</summary>
        public Exception? Failure { get; set; }

        /// <summary>When set, the attempt never answers — the token endpoint that hangs.</summary>
        public bool Hangs { get; set; }

        /// <summary>Completes once a request has reached this far end, so a test can act on a refresh
        /// that is still in flight instead of racing it.</summary>
        public Task Entered => _entered.Task;

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            _entered.TrySetResult();

            if (Failure is not null)
            {
                throw Failure;
            }

            if (Hangs)
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(Body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
