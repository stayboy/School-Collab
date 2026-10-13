using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Mobile.Logic;

namespace SchoolCollab.Mobile.Tests.Unit;

/// <summary>
/// Covers <see cref="TokenSession"/> — the device's token custody and its refresh-on-demand policy
/// (OD-1): a usable token is reused with no traffic, an expired one is refreshed exactly once, and a
/// rejected refresh ends the session (store cleared, typed exception). HTTP is driven by a scripted
/// handler, never a mocked framework type.
/// </summary>
[TestClass]
public class TokenSessionTests
{
    private static readonly Uri TokenEndpoint = new UriBuilder(Uri.UriSchemeHttps, "idp.example", 8080, "realms/school-collab/protocol/openid-connect/token").Uri;

    /// <summary>A fixed instant, so a "usable until" assertion is exact rather than approximate.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task GetBearerTokenAsync_ReusesAStoredToken_WithoutAnyTraffic()
    {
        var store = new FakeTokenStore();
        await store.SeedAsync(TokenKeys.AccessToken, "stored-access-token");
        await store.SeedAsync(TokenKeys.RefreshToken, "stored-refresh-token");
        await store.SeedAsync(TokenKeys.ExpiresAt, UsableUntil(TimeSpan.FromMinutes(5)));
        var handler = new ScriptedHandler(HttpStatusCode.OK, TokenBody("refreshed", "rotated", 300));
        var session = CreateSession(store, handler);

        var token = await session.GetBearerTokenAsync();

        token.Should().Be("stored-access-token");
        handler.Requests.Should().BeEmpty(
            "a usable token must be reused as-is: refreshing on every call is the refresh storm the policy forbids.");
    }

    [TestMethod]
    public async Task GetBearerTokenAsync_RefreshesAnExpiredToken_ExactlyOnceForRepeatedCalls()
    {
        var store = new FakeTokenStore();
        await store.SeedAsync(TokenKeys.AccessToken, "expired-access-token");
        await store.SeedAsync(TokenKeys.RefreshToken, "stored-refresh-token");
        await store.SeedAsync(TokenKeys.ExpiresAt, UsableUntil(TimeSpan.FromMinutes(-5)));
        var handler = new ScriptedHandler(HttpStatusCode.OK, TokenBody("refreshed-access-token", "rotated-refresh-token", 300));
        var session = CreateSession(store, handler);

        var first = await session.GetBearerTokenAsync();
        var second = await session.GetBearerTokenAsync();

        first.Should().Be("refreshed-access-token");
        second.Should().Be("refreshed-access-token");
        handler.Requests.Should().HaveCount(1, "the second call finds a usable token and must not refresh again.");

        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri.Should().Be(TokenEndpoint,
            "the refresh POSTs to the authority's OIDC token endpoint, built from the configured authority.");

        var body = handler.Bodies[0];
        body.Should().Contain("grant_type=refresh_token");
        body.Should().Contain("client_id=school-collab-mobile-client");
        body.Should().Contain("refresh_token=stored-refresh-token");
        store.Written[TokenKeys.AccessToken].Should().Be("refreshed-access-token");
        store.Written[TokenKeys.RefreshToken].Should().Be("rotated-refresh-token");
    }

    [TestMethod]
    public async Task GetBearerTokenAsync_ReturnsNullWithoutTraffic_WhenTheSessionHoldsNoRefreshToken()
    {
        var store = new FakeTokenStore();
        var handler = new ScriptedHandler(HttpStatusCode.OK, TokenBody("unused", "unused", 300));
        var session = CreateSession(store, handler);

        var token = await session.GetBearerTokenAsync();

        token.Should().BeNull("there is no credential to attach, so the request goes out unauthenticated and the server answers 401.");
        handler.Requests.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetBearerTokenAsync_ReturnsNullWithoutTraffic_WhenTheTokenIsExpiredAndUnrefreshable()
    {
        var store = new FakeTokenStore();
        await store.SeedAsync(TokenKeys.AccessToken, "expired-access-token");
        await store.SeedAsync(TokenKeys.ExpiresAt, UsableUntil(TimeSpan.FromMinutes(-5)));
        var handler = new ScriptedHandler(HttpStatusCode.OK, TokenBody("unused", "unused", 300));
        var session = CreateSession(store, handler);

        var token = await session.GetBearerTokenAsync();

        token.Should().BeNull();
        handler.Requests.Should().BeEmpty("with no refresh token there is nothing to renew — and no attempt to make.");
    }

    [TestMethod]
    public async Task RefreshAsync_ClearsTheStoreAndThrows_TheTypedSessionExpiredException()
    {
        var store = new FakeTokenStore();
        await store.SeedAsync(TokenKeys.AccessToken, "expired-access-token");
        await store.SeedAsync(TokenKeys.RefreshToken, "stored-refresh-token");
        await store.SeedAsync(TokenKeys.ExpiresAt, UsableUntil(TimeSpan.FromMinutes(-5)));
        var handler = new ScriptedHandler(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Token is not active"}""");
        var session = CreateSession(store, handler);
        var sessionChanges = 0;
        session.SessionChanged += () => sessionChanges++;

        Func<Task> refresh = () => session.GetBearerTokenAsync();

        var exception = await refresh.Should().ThrowAsync<TokenSessionExpiredException>();

        exception.Which.Message.Should()
            .Contain("invalid_grant", "the message names the endpoint's error CODE so an operator can act on it.")
            .And.NotContain("expired-access-token", "a token value must never reach an exception message.")
            .And.NotContain("stored-refresh-token", "a refresh-token value must never reach an exception message.");

        // Session end: every stored key is gone, so the next auth-state read is the signed-out state.
        store.Values.Should().OnlyContain(value => value == null);
        session.AccessToken.Should().BeNull();
        sessionChanges.Should().Be(1, "session end must signal the UI exactly once.");
    }

    [TestMethod]
    public async Task ApplyTokensAsync_PersistsTheSessionAndSignalsTheChange()
    {
        var store = new FakeTokenStore();
        var handler = new ScriptedHandler(HttpStatusCode.OK, TokenBody("unused", "unused", 300));
        var session = CreateSession(store, handler);
        var sessionChanges = 0;
        session.SessionChanged += () => sessionChanges++;

        await session.ApplyTokensAsync(new TokenResponse("new-access-token", "new-refresh-token", 300));

        store.Written[TokenKeys.AccessToken].Should().Be("new-access-token");
        store.Written[TokenKeys.RefreshToken].Should().Be("new-refresh-token");
        store.Written[TokenKeys.ExpiresAt].Should().Be(
            UsableUntil(TimeSpan.FromSeconds(300 - 30)),
            "the persisted usable-until instant is the token's expiry minus the 30s clock-skew margin.");
        sessionChanges.Should().Be(1);
    }

    [TestMethod]
    public async Task ApplyTokensAsync_KeepsTheCurrentRefreshToken_WhenTheResponseRotatesNone()
    {
        var store = new FakeTokenStore();
        var handler = new ScriptedHandler(HttpStatusCode.OK, TokenBody("unused", "unused", 300));
        var session = CreateSession(store, handler);
        await session.ApplyTokensAsync(new TokenResponse("first-access-token", "first-refresh-token", 300));

        await session.ApplyTokensAsync(new TokenResponse("second-access-token", null, 300));

        store.Written[TokenKeys.RefreshToken].Should().Be("first-refresh-token",
            "a grant that does not rotate the refresh token must not clear the one we hold.");
    }

    [TestMethod]
    public async Task EnsureInitializedAsync_RestoresThePersistedSession_Once()
    {
        var store = new FakeTokenStore();
        await store.SeedAsync(TokenKeys.AccessToken, "persisted-access-token");
        await store.SeedAsync(TokenKeys.ExpiresAt, UsableUntil(TimeSpan.FromMinutes(5)));
        var handler = new ScriptedHandler(HttpStatusCode.OK, TokenBody("unused", "unused", 300));
        var session = CreateSession(store, handler);

        await session.EnsureInitializedAsync();
        await session.EnsureInitializedAsync();

        session.AccessToken.Should().Be("persisted-access-token");
        store.Reads.Should().Be(3, "the store is read once per key, not once per caller.");
        (await session.GetBearerTokenAsync()).Should().Be("persisted-access-token");
    }

    [TestMethod]
    public async Task EnsureInitializedAsync_TreatsAnUnreadableExpiryAsExpired()
    {
        var store = new FakeTokenStore();
        await store.SeedAsync(TokenKeys.AccessToken, "access-token-without-a-readable-expiry");
        await store.SeedAsync(TokenKeys.RefreshToken, "stored-refresh-token");
        await store.SeedAsync(TokenKeys.ExpiresAt, "not-a-timestamp");
        var handler = new ScriptedHandler(HttpStatusCode.OK, TokenBody("refreshed", "rotated", 300));
        var session = CreateSession(store, handler);

        var token = await session.GetBearerTokenAsync();

        token.Should().Be("refreshed", "an unreadable expiry must fail towards refreshing, never towards trusting the token.");
        handler.Requests.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task ClearAsync_RemovesEveryStoredKey_AndSignalsTheChange()
    {
        var store = new FakeTokenStore();
        var handler = new ScriptedHandler(HttpStatusCode.OK, TokenBody("unused", "unused", 300));
        var session = CreateSession(store, handler);
        await session.ApplyTokensAsync(new TokenResponse("access", "refresh", 300));
        var sessionChanges = 0;
        session.SessionChanged += () => sessionChanges++;

        var failure = await session.ClearAsync();

        failure.Should().BeNull("every stored key was removed, so there is nothing to report.");
        store.Values.Should().OnlyContain(value => value == null);
        session.AccessToken.Should().BeNull();
        sessionChanges.Should().Be(1);
    }

    /// <summary>
    /// R7 regression. The keystore refuses the removal of one key — the worst case for sign-out.
    /// The session must still end in memory, with nothing usable left and the UI told; the refusal
    /// is reported as the typed signal instead of thrown, so it can neither abort the sign-out
    /// (which the pre-fix ordering did, leaving a live in-memory session behind) nor stop the sweep
    /// of the keys the keystore could still forget.
    /// </summary>
    [TestMethod]
    public async Task ClearAsync_LeavesNoUsableSession_WhenTheStoreRefusesTheRemoval()
    {
        var store = new FakeTokenStore();
        await store.SeedAsync(TokenKeys.AccessToken, "stored-access-token");
        await store.SeedAsync(TokenKeys.RefreshToken, "stored-refresh-token");
        await store.SeedAsync(TokenKeys.ExpiresAt, UsableUntil(TimeSpan.FromMinutes(5)));
        var handler = new ScriptedHandler(HttpStatusCode.OK, TokenBody("refreshed", "rotated", 300));
        var session = CreateSession(store, handler);
        var sessionChanges = 0;
        session.SessionChanged += () => sessionChanges++;

        // The session is live in memory — and a usable bearer token — before the store turns on us.
        (await session.GetBearerTokenAsync()).Should().Be("stored-access-token");
        store.RefusedRemovalKey = TokenKeys.AccessToken;

        var failure = await session.ClearAsync();

        failure.Should().BeOfType<TokenStoreException>(
            "the refused removal is REPORTED as the typed signal — a sign-out must never fail into the caller.");
        failure!.Message.Should()
            .Contain(TokenKeys.AccessToken, "the signal names the key the device could not forget.")
            .And.NotContain("stored-access-token", "and never the value stored under it.");
        session.AccessToken.Should().BeNull(
            "the in-memory token is dropped BEFORE the store is touched, so a refused (or throwing) clear cannot leave it live.");
        sessionChanges.Should().Be(1, "sign-out tells the UI exactly once, whatever the store then does.");
        (await session.GetBearerTokenAsync()).Should().BeNull(
            "nothing may hand out a bearer token after a sign-out the store refused to complete.");
        handler.Requests.Should().BeEmpty("with the session dropped there is nothing to refresh — and no traffic to make.");

        store.Stored(TokenKeys.AccessToken).Should().Be("stored-access-token",
            "that one entry survived, which is exactly what the reported signal is for.");
        store.Stored(TokenKeys.RefreshToken).Should().BeNull("best-effort: one refused key does not stop the sweep of the rest.");
        store.Stored(TokenKeys.ExpiresAt).Should().BeNull("best-effort: one refused key does not stop the sweep of the rest.");
    }

    /// <summary>
    /// R7's write-path policy. A store that refuses the write ends the session instead of leaving a
    /// memory-only session whose credential the device cannot hold: memory and store are both dropped
    /// and the typed failure is thrown for the caller — the sign-in page shows it, while the refresh
    /// path inside an in-flight API call reads it as "no session"
    /// (<see cref="BearerTokenDelegatingHandler"/>).
    /// </summary>
    [TestMethod]
    public async Task ApplyTokensAsync_DiscardsTheSessionAndReportsTheRefusal_WhenTheStoreRefusesTheWrite()
    {
        var store = new FakeTokenStore();
        await store.SeedAsync(TokenKeys.AccessToken, "previous-access-token");
        await store.SeedAsync(TokenKeys.RefreshToken, "previous-refresh-token");
        var handler = new ScriptedHandler(HttpStatusCode.OK, TokenBody("unused", "unused", 300));
        var session = CreateSession(store, handler);
        var sessionChanges = 0;
        session.SessionChanged += () => sessionChanges++;
        store.RefusesWrite = true;

        var apply = async () => await session.ApplyTokensAsync(new TokenResponse("new-access-token", "new-refresh-token", 300));

        var exception = await apply.Should().ThrowAsync<TokenStoreException>();

        exception.Which.Message.Should()
            .NotContain("new-access-token", "a token value must never reach an exception message.")
            .And.NotContain("new-refresh-token", "a refresh-token value must never reach an exception message.");
        session.AccessToken.Should().BeNull(
            "a session the store refused is discarded, never kept in memory for a later request to present.");
        sessionChanges.Should().Be(1, "discarding the adopted session is a session change the UI must see.");
        store.Values.Should().OnlyContain(value => value == null,
            "the store is swept too, so the previously stored session cannot be restored at the next start.");
        (await session.GetBearerTokenAsync()).Should().BeNull();
        handler.Requests.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ApplyTokensAsync_RejectsANullResponse()
    {
        var session = CreateSession(new FakeTokenStore(), new ScriptedHandler(HttpStatusCode.OK, "{}"));

        var apply = async () => await session.ApplyTokensAsync(null!);

        await apply.Should().ThrowAsync<ArgumentNullException>();
    }

    private static TokenSession CreateSession(FakeTokenStore store, ScriptedHandler handler) =>
        new(store, new StubHttpClientFactory(handler), CreateOptions(), new MutableTimeProvider(Now));

    private static string UsableUntil(TimeSpan offset) =>
        Now.Add(offset).ToString("O", CultureInfo.InvariantCulture);

    private static MobileAuthOptions CreateOptions() => new()
    {
        Authority = new UriBuilder(Uri.UriSchemeHttps, "idp.example", 8080, "realms/school-collab").Uri.ToString(),
        ClientId = "school-collab-mobile-client",
        RedirectScheme = "schoolcollab",
        RedirectHost = "signin-oidc",
    };

    private static string TokenBody(string accessToken, string refreshToken, int expiresInSeconds) =>
        $$"""{"access_token":"{{accessToken}}","refresh_token":"{{refreshToken}}","expires_in":{{expiresInSeconds}},"token_type":"Bearer"}""";

    /// <summary>An in-memory <see cref="ITokenStore"/>: what is written is what an app restart reads.
    /// Two switches simulate the keystore refusals the store contract permits so the auth path's
    /// degradation can be asserted: a write the keystore would not take, and a removal it would not
    /// carry out.</summary>
    private sealed class FakeTokenStore : ITokenStore
    {
        private static readonly string[] Keys = [TokenKeys.AccessToken, TokenKeys.RefreshToken, TokenKeys.ExpiresAt];

        private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

        public Dictionary<string, string?> Written { get; } = new(StringComparer.Ordinal);

        public int Reads { get; private set; }

        /// <summary>When set, every write is refused — the keystore that cannot keep a session.</summary>
        public bool RefusesWrite { get; set; }

        /// <summary>When set, the removal of exactly this key is refused — the keystore that cannot
        /// forget one entry while still forgetting the others.</summary>
        public string? RefusedRemovalKey { get; set; }

        public IEnumerable<string?> Values => Keys.Select(Stored);

        /// <summary>The value stored under <paramref name="key"/>, or <see langword="null"/> when absent.</summary>
        public string? Stored(string key) => _values.TryGetValue(key, out var value) ? value : null;

        public Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(Stored(key));
        }

        public Task WriteAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            if (RefusesWrite)
            {
                throw Refusal("write of", key);
            }

            _values[key] = value;
            Written[key] = value;
            return Task.CompletedTask;
        }

        public Task ClearAsync(string key, CancellationToken cancellationToken = default)
        {
            if (string.Equals(key, RefusedRemovalKey, StringComparison.Ordinal))
            {
                throw Refusal("removal of", key);
            }

            _values[key] = null;
            return Task.CompletedTask;
        }

        public Task SeedAsync(string key, string value) => WriteAsync(key, value);

        /// <summary>The refusal shape the platform implementation raises: the key NAME and the
        /// platform exception TYPE, never a stored value.</summary>
        private static TokenStoreException Refusal(string operation, string key) =>
            new($"The device's token store refused the {operation} its '{key}' entry (PlatformException).");
    }

    /// <summary>A hand-written <see cref="IHttpClientFactory"/> — no framework type is mocked, and no
    /// named-client resolution machinery is needed to drive one scripted handler.</summary>
    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Records every request (and its body) and answers with one canned response.</summary>
    private sealed class ScriptedHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
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

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>The testable clock the repo's convention demands (<c>TimeProvider</c>).</summary>
    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
