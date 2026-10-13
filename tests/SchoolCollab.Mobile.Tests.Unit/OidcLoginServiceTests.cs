using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Mobile.Logic;

namespace SchoolCollab.Mobile.Tests.Unit;

/// <summary>
/// Covers <see cref="OidcLoginService"/> — the PKCE acquisition: the authorize request it builds,
/// the <c>state</c> validation it enforces before anything is persisted, and the code exchange it
/// runs. The browser is a fake and the token endpoint a scripted handler, so no live Keycloak
/// participates (spec §8 L3 criterion 2 asserts the decoded claims, not a live IdP).
/// </summary>
[TestClass]
public class OidcLoginServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly Uri AuthorizeEndpoint =
        new UriBuilder(Uri.UriSchemeHttps, "idp.example", 8080, "realms/school-collab/protocol/openid-connect/auth").Uri;

    private static readonly Uri TokenEndpoint =
        new UriBuilder(Uri.UriSchemeHttps, "idp.example", 8080, "realms/school-collab/protocol/openid-connect/token").Uri;

    [TestMethod]
    public async Task SignInAsync_BuildsTheAuthorizeRequest_WithS256PkceAndState()
    {
        var fixture = new Fixture();

        await fixture.Service.SignInAsync();

        var authorize = fixture.Authenticator.AuthorizeUrl!;
        authorize.GetLeftPart(UriPartial.Path).Should().Be(AuthorizeEndpoint.ToString());
        fixture.Authenticator.CallbackUri.Should().Be(new Uri("schoolcollab://signin-oidc"),
            "the callback handed to the browser must be exactly what the realm registers and the platform filter/plist declare.");
        var query = ParseQuery(authorize);
        query["response_type"].Should().Be("code");
        query["client_id"].Should().Be("school-collab-mobile-client");
        query["redirect_uri"].Should().Be("schoolcollab://signin-oidc");
        query["scope"].Should().Be("openid");
        query["code_challenge_method"].Should().Be("S256");
        query["state"].Should().NotBeNullOrWhiteSpace("a random state is the replay defence for the unverified custom scheme.");

        // The challenge must be the SHA-256 of the verifier the code exchange later sends — and the
        // verifier must come from memory, never from the token store.
        var verifier = FormValues(fixture.Handler.Bodies[0])["code_verifier"];
        verifier.Length.Should().BeInRange(43, 128, "RFC 7636 requires 43–128 characters.");
        query["code_challenge"].Should().Be(ToBase64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
        fixture.Store.Values.Should().OnlyContain(value => value == null || value != verifier,
            "the verifier is the one secret a hijacked redirect must not be able to reach: it never leaves memory.");
    }

    [TestMethod]
    public async Task SignInAsync_ExchangesTheCodeAndAppliesTheSession()
    {
        var fixture = new Fixture();
        var sessionChanges = 0;
        fixture.Session.SessionChanged += () => sessionChanges++;

        await fixture.Service.SignInAsync();

        fixture.Handler.Requests.Should().HaveCount(1);
        fixture.Handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        fixture.Handler.Requests[0].RequestUri.Should().Be(TokenEndpoint);

        var form = FormValues(fixture.Handler.Bodies[0]);
        form["grant_type"].Should().Be("authorization_code");
        form["code"].Should().Be("authorization-code");
        form["client_id"].Should().Be("school-collab-mobile-client");
        form["redirect_uri"].Should().Be("schoolcollab://signin-oidc");

        fixture.Store.Written[TokenKeys.AccessToken].Should().Be(fixture.ClaimsToken);
        fixture.Store.Written[TokenKeys.RefreshToken].Should().Be("a-refresh-token");
        fixture.Service.State.Should().Be(SignInState.SignedIn);
        sessionChanges.Should().Be(1, "the session signals the UI once, and the provider turns that into NotifyAuthenticationStateChanged.");
    }

    [TestMethod]
    public async Task SignInAsync_RejectsAStateMismatch_BeforeAnythingIsPersisted()
    {
        var fixture = new Fixture();
        fixture.Authenticator.State = "a-forged-state";

        Func<Task> signIn = () => fixture.Service.SignInAsync();

        var exception = await signIn.Should().ThrowAsync<SignInFailedException>();

        exception.Which.Should().NotBeNull();
        fixture.Handler.Requests.Should().BeEmpty("a forged callback must not reach the token endpoint.");
        fixture.Store.Written.Should().BeEmpty("nothing may be persisted by a failed validation.");
        fixture.Service.State.Should().Be(SignInState.SignedOut);
    }

    [TestMethod]
    public async Task SignInAsync_RejectsAResponseWithoutACode()
    {
        var fixture = new Fixture();
        fixture.Authenticator.WithCode = false;

        Func<Task> signIn = () => fixture.Service.SignInAsync();

        await signIn.Should().ThrowAsync<SignInFailedException>();

        fixture.Handler.Requests.Should().BeEmpty();
        fixture.Store.Written.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SignInAsync_SurfacesACancellation_AsTheTypedCancelledException()
    {
        var fixture = new Fixture();
        fixture.Authenticator.Cancels = true;

        Func<Task> signIn = () => fixture.Service.SignInAsync();

        await signIn.Should().ThrowAsync<SignInCancelledException>();

        fixture.Service.State.Should().Be(SignInState.SignedOut);
        fixture.Store.Written.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SignInAsync_FailsTheSignIn_WhenTheTokenEndpointRejectsTheCodeExchange()
    {
        var fixture = new Fixture();
        fixture.Handler.Status = HttpStatusCode.BadRequest;
        fixture.Handler.Body = """{"error":"invalid_grant","error_description":"Code not valid"}""";

        Func<Task> signIn = () => fixture.Service.SignInAsync();

        var exception = await signIn.Should().ThrowAsync<SignInFailedException>();

        exception.Which.Message.Should()
            .Contain("invalid_grant", "the message names the endpoint's error code so the failure is actionable.")
            .And.NotContain("authorization-code", "the authorization code is credential material and must never reach a message.");
        fixture.Store.Written.Should().BeEmpty();
        fixture.Service.State.Should().Be(SignInState.SignedOut);
    }

    /// <summary>
    /// R11: a code exchange that dies in transport — the token endpoint unreachable — must leave the
    /// flow as the typed sign-in failure the page already shows in its message bar. A transport
    /// exception escaping here would reach <c>Spike.razor</c>'s <c>&lt;ErrorBoundary&gt;</c> and
    /// replace the WHOLE page, sign-in button included. Same normalisation the refresh path applies
    /// (<see cref="BearerTokenDelegatingHandler"/>).
    /// </summary>
    [TestMethod]
    [DataRow(SignInTransportFailure.RequestFailed)]
    [DataRow(SignInTransportFailure.TimedOut)]
    [DataRow(SignInTransportFailure.ReadFailed)]
    public async Task SignInAsync_SurfacesATransportFailureOfTheCodeExchange_AsTheTypedSignInFailure(
        SignInTransportFailure failure)
    {
        var fixture = new Fixture(tokenEndpointFailure: TransportException(failure));

        Func<Task> signIn = () => fixture.Service.SignInAsync();

        await signIn.Should().ThrowAsync<SignInFailedException>(
            "the transport failure belongs in the sign-in message bar, never in the page's ErrorBoundary.");
        fixture.Service.State.Should().Be(SignInState.SignedOut);
        fixture.Store.Written.Should().BeEmpty("the exchange produced no credential, so nothing may be persisted.");
    }

    /// <summary>
    /// R11's own-deadline leg, which the three thrown shapes above cannot reach: the token endpoint
    /// does not refuse, it HANGS, and the token client's own <see cref="HttpClient.Timeout"/> ends the
    /// attempt. Measured on this stack that surfaces as a <see cref="TaskCanceledException"/> — never
    /// a bare <see cref="TimeoutException"/> — so the guarded clause is the only thing standing
    /// between a dead network and the page's ErrorBoundary.
    /// </summary>
    [TestMethod]
    public async Task SignInAsync_SurfacesAClientTimeoutOfTheCodeExchange_AsTheTypedSignInFailure()
    {
        var fixture = new Fixture(hangs: true, tokenEndpointTimeout: TimeSpan.FromMilliseconds(50));

        Func<Task> signIn = () => fixture.Service.SignInAsync();

        await signIn.Should().ThrowAsync<SignInFailedException>(
            "the token client's own deadline is a sign-in outcome, not a transport failure for the page.");
        fixture.Service.State.Should().Be(SignInState.SignedOut);
        fixture.Store.Written.Should().BeEmpty();
    }

    /// <summary>
    /// The guard's other half, without which the timeout clause above would swallow every
    /// cancellation: a <see cref="TaskCanceledException"/> raised by the CALLER's token is not a
    /// sign-in outcome and must keep propagating. The token endpoint hangs long enough to cancel an
    /// exchange that is genuinely in flight.
    /// </summary>
    [TestMethod]
    public async Task SignInAsync_Rethrows_WhenTheCallerCancelsTheCodeExchange()
    {
        var fixture = new Fixture(hangs: true);
        using var cancellation = new CancellationTokenSource();

        var signIn = fixture.Service.SignInAsync(cancellation.Token);
        await fixture.Handler.Entered;
        await cancellation.CancelAsync();

        var act = async () =>
        {
            await signIn;
        };

        await act.Should().ThrowAsync<OperationCanceledException>(
            "the caller's cancellation is the caller's verdict — the guard must let it through.");
        fixture.Service.State.Should().Be(SignInState.SignedOut);
        fixture.Store.Written.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SignOutAsync_ClearsTheSession()
    {
        var fixture = new Fixture();
        await fixture.Service.SignInAsync();

        await fixture.Service.SignOutAsync();

        fixture.Store.Values.Should().OnlyContain(value => value == null);
        fixture.Service.State.Should().Be(SignInState.SignedOut);
    }

    /// <summary>One scripted sign-in: the fake browser, the scripted token endpoint, the in-memory
    /// store and the flow under test.</summary>
    private sealed class Fixture
    {
        /// <param name="tokenEndpointFailure">When set, the code exchange dies in transport instead of
        /// being answered — the unreachable token endpoint.</param>
        /// <param name="hangs">The token endpoint never answers — the exchange runs until the token
        /// client's own timeout, or the caller's cancellation, ends it.</param>
        /// <param name="tokenEndpointTimeout">The token client's own <see cref="HttpClient.Timeout"/>,
        /// left at its default when absent.</param>
        public Fixture(
            Exception? tokenEndpointFailure = null,
            bool hangs = false,
            TimeSpan? tokenEndpointTimeout = null)
        {
            Store = new FakeTokenStore();
            Handler = new ScriptedHandler(HttpStatusCode.OK, BuildTokenBody(ClaimsToken))
            {
                Failure = tokenEndpointFailure,
                Hangs = hangs,
            };
            Session = new TokenSession(Store, new StubHttpClientFactory(Handler), AuthOptions(), new MutableTimeProvider(Now));
            Authenticator = new FakeAuthenticator();
            Service = new OidcLoginService(
                AuthOptions(),
                Authenticator,
                Session,
                new StubHttpClientFactory(Handler) { Timeout = tokenEndpointTimeout });
        }

        /// <summary>A fixture access token whose payload carries the realm's raw claim spellings.</summary>
        public string ClaimsToken { get; } = Jwt(
            """
            {
              "tenant_id": "00000000-0000-0000-0000-000000000002",
              "tenant_name": "Dev School",
              "teacher_id": "00000000-0000-0000-0000-000000000003",
              "roles": ["teacher"]
            }
            """);

        public FakeTokenStore Store { get; }

        public ScriptedHandler Handler { get; }

        public TokenSession Session { get; }

        public FakeAuthenticator Authenticator { get; }

        public OidcLoginService Service { get; }

        private string BuildTokenBody(string accessToken) =>
            $$"""{"access_token":"{{accessToken}}","refresh_token":"a-refresh-token","expires_in":300,"token_type":"Bearer"}""";
    }

    private static MobileAuthOptions AuthOptions() => new()
    {
        Authority = new UriBuilder(Uri.UriSchemeHttps, "idp.example", 8080, "realms/school-collab").Uri.ToString(),
        ClientId = "school-collab-mobile-client",
        RedirectScheme = "schoolcollab",
        RedirectHost = "signin-oidc",
    };

    /// <summary>The system browser: hands back the authorization response the test asks for, echoing
    /// the <c>state</c> it was sent unless the test forges one.</summary>
    private sealed class FakeAuthenticator : ISystemBrowserAuthenticator
    {
        /// <summary>When set, the response carries this state instead of the requested one.</summary>
        public string? State { get; set; }

        public bool WithCode { get; set; } = true;

        public bool Cancels { get; set; }

        public Uri? AuthorizeUrl { get; private set; }

        public Uri? CallbackUri { get; private set; }

        public Task<IDictionary<string, string>> AuthenticateAsync(
            Uri authorizeUrl,
            Uri callbackUri,
            CancellationToken cancellationToken = default)
        {
            if (Cancels)
            {
                throw new SignInCancelledException("The sign-in was cancelled before the browser returned a callback.");
            }

            AuthorizeUrl = authorizeUrl;
            CallbackUri = callbackUri;

            var response = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["state"] = State ?? ParseQuery(authorizeUrl)["state"],
            };

            if (WithCode)
            {
                response["code"] = "authorization-code";
            }

            return Task.FromResult<IDictionary<string, string>>(response);
        }
    }

    private sealed class FakeTokenStore : ITokenStore
    {
        private static readonly string[] Keys = [TokenKeys.AccessToken, TokenKeys.RefreshToken, TokenKeys.ExpiresAt];

        private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

        public Dictionary<string, string?> Written { get; } = new(StringComparer.Ordinal);

        public IEnumerable<string?> Values => Keys.Select(key => _values.TryGetValue(key, out var value) ? value : null);

        public Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);

        public Task WriteAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            Written[key] = value;
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
        /// turns a hung token endpoint into a client-timed-out exchange. Absent, the client keeps its
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
    /// The token endpoint: records the code exchange POST and answers with a canned response, or —
    /// when <see cref="Failure"/> is set — dies in transport without answering, or — when
    /// <see cref="Hangs"/> is set — never answers at all.
    /// </summary>
    private sealed class ScriptedHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HttpStatusCode Status { get; set; } = status;

        public string Body { get; set; } = body;

        /// <summary>When set, the exchange dies in transport instead of being answered.</summary>
        public Exception? Failure { get; set; }

        /// <summary>When set, the exchange never answers — the token endpoint that hangs until the
        /// client's own deadline, or the caller's cancellation, ends it.</summary>
        public bool Hangs { get; set; }

        /// <summary>Completes once a request has reached this far end, so a test can act on an
        /// exchange that is still in flight instead of racing it.</summary>
        public Task Entered => _entered.Task;

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
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

    /// <summary>
    /// The three transport shapes an unreachable token endpoint surfaces as — the same three the
    /// refresh path degrades on (<see cref="BearerTokenDelegatingHandler"/>).
    /// Public only because an MSTest method is public and a parameter type may not be less
    /// accessible than the method that takes it — no other type outside this class uses it.
    /// </summary>
    public enum SignInTransportFailure
    {
        RequestFailed,
        TimedOut,
        ReadFailed,
    }

    private static Exception TransportException(SignInTransportFailure failure) => failure switch
    {
        SignInTransportFailure.RequestFailed =>
            new HttpRequestException("The token endpoint was unreachable."),
        SignInTransportFailure.TimedOut =>
            new TimeoutException("The token endpoint did not answer in time."),
        _ => new IOException("The token endpoint's response could not be read."),
    };

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static Dictionary<string, string> ParseQuery(Uri uri) => uri.Query
        .TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(pair => pair.Split('=', 2))
        .ToDictionary(
            parts => Uri.UnescapeDataString(parts[0]),
            parts => parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty,
            StringComparer.Ordinal);

    private static Dictionary<string, string> FormValues(string body) => body
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(pair => pair.Split('=', 2))
        .ToDictionary(
            parts => Uri.UnescapeDataString(parts[0]),
            parts => parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty,
            StringComparer.Ordinal);

    private static string Jwt(string payloadJson) =>
        $"{ToBase64Url("{\"alg\":\"RS256\",\"typ\":\"JWT\"}")}.{ToBase64Url(payloadJson)}.signature-not-verified";

    private static string ToBase64Url(ReadOnlySpan<byte> bytes) => Convert
        .ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static string ToBase64Url(string value) => ToBase64Url(Encoding.UTF8.GetBytes(value));
}
