using System.Globalization;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Mobile.Logic;

namespace SchoolCollab.Mobile.Tests.Unit;

/// <summary>
/// Covers <see cref="MobileAuthenticationStateProvider"/> — the client-side auth bridge the shared
/// gates require. The claim-spelling contract asserted here is the SAME read the shared UI performs:
/// <see cref="VisibleTenantService"/> is exercised against the provider, exactly as
/// <c>TenantGate</c>'s condition does (spec §8 L3 criterion 5's ubuntu-mechanical half).
/// </summary>
[TestClass]
public class MobileAuthenticationStateProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A fixture access token carrying the raw realm claim spellings the mappers emit.
    /// Built here rather than fetched: no live Keycloak participates in this suite.</summary>
    private static readonly string ClaimsToken = Jwt(
        """
        {
          "tenant_id": "00000000-0000-0000-0000-000000000002",
          "tenant_name": "Dev School",
          "teacher_id": "00000000-0000-0000-0000-000000000003",
          "roles": ["teacher", "staff"]
        }
        """);

    [TestMethod]
    public async Task GetAuthenticationStateAsync_IsAnonymous_WhenNoSessionIsStored()
    {
        var fixture = new Fixture();

        var state = await fixture.Provider.GetAuthenticationStateAsync();

        state.User.Identity!.IsAuthenticated.Should().BeFalse(
            "an empty session must show the signed-out state, so the gates render their fallback.");
        state.User.Claims.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetAuthenticationStateAsync_MintsAFreshAnonymousState_SoNoTwoConsumersShareOne()
    {
        var fixture = new Fixture();

        var first = await fixture.Provider.GetAuthenticationStateAsync();
        var second = await fixture.Provider.GetAuthenticationStateAsync();

        first.User.Should().NotBeSameAs(
            second.User,
            "unrelated consumers must never be handed the same ClaimsPrincipal instance — a cached anonymous principal is shared auth state.");
        first.User.Identity!.IsAuthenticated.Should().BeFalse();
        second.User.Identity!.IsAuthenticated.Should().BeFalse();
    }

    [TestMethod]
    public async Task GetAuthenticationStateAsync_YieldsAnAuthenticatedPrincipal_WithTheRawRealmClaimSpellings()
    {
        var fixture = new Fixture();
        await fixture.SignInAsync();

        var state = await fixture.Provider.GetAuthenticationStateAsync();

        state.User.Identity!.IsAuthenticated.Should().BeTrue();
        state.User.FindFirst("tenant_id")!.Value.Should().Be("00000000-0000-0000-0000-000000000002");
        state.User.FindFirst("tenant_name")!.Value.Should().Be("Dev School");
        state.User.FindFirst("teacher_id")!.Value.Should().Be("00000000-0000-0000-0000-000000000003");
        state.User.FindAll("roles").Select(claim => claim.Value).Should().Equal(
            new[] { "teacher", "staff" },
            "a multivalued claim reads as one claim per array element.");
    }

    [TestMethod]
    public async Task GetAuthenticationStateAsync_BacksTheGatesOwnTenantRead()
    {
        var fixture = new Fixture();
        await fixture.SignInAsync();

        // The real consumer, not a re-implementation of it: this is the exact service TenantGate's
        // TenantSelectedCondition calls, and it reads the raw `tenant_id` claim (VisibleTenantService).
        var visibleTenant = new VisibleTenantService(
            fixture.Provider,
            NullLogger<VisibleTenantService>.Instance);

        var scope = await visibleTenant.GetScopeAsync();

        scope.IsRealTenant.Should().BeTrue();
        scope.TenantId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        scope.TenantName.Should().Be("Dev School");
    }

    [TestMethod]
    public async Task NotifyAuthenticationStateChanged_FiresOnSessionStartAndSessionEnd()
    {
        var fixture = new Fixture();
        var notified = new List<Task<AuthenticationState>>();
        fixture.Provider.AuthenticationStateChanged += task => notified.Add(task);

        await fixture.SignInAsync();
        (await notified.Single()).User.Identity!.IsAuthenticated.Should().BeTrue();

        notified.Clear();
        await fixture.Session.ClearAsync();
        (await notified.Single()).User.Identity!.IsAuthenticated.Should().BeFalse(
            "sign-out must re-notify the gates, which is what flips TenantGate back to its fallback branch.");
    }

    [TestMethod]
    public async Task GetAuthenticationStateAsync_IgnoresATokenWhosePayloadCannotBeRead()
    {
        var fixture = new Fixture();
        await fixture.Store.WriteAsync(TokenKeys.AccessToken, "not-a-jwt");
        await fixture.Store.WriteAsync(TokenKeys.ExpiresAt, UsableUntil(TimeSpan.FromMinutes(5)));

        var state = await fixture.Provider.GetAuthenticationStateAsync();

        state.User.Identity!.IsAuthenticated.Should().BeFalse(
            "a token nobody can read cannot authenticate anybody — and the APIs would refuse it too.");
    }

    [TestMethod]
    public async Task Dispose_DetachesFromTheSession()
    {
        var fixture = new Fixture();
        var notified = 0;
        fixture.Provider.AuthenticationStateChanged += _ => notified++;

        fixture.Provider.Dispose();
        await fixture.Session.ClearAsync();

        notified.Should().Be(0);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Store = new FakeTokenStore();
            Session = new TokenSession(Store, new StubHttpClientFactory(), AuthOptions(), new MutableTimeProvider(Now));
            Provider = new MobileAuthenticationStateProvider(Session);
        }

        public FakeTokenStore Store { get; }

        public TokenSession Session { get; }

        public MobileAuthenticationStateProvider Provider { get; }

        /// <summary>Simulates what <see cref="OidcLoginService"/> does after a successful exchange.</summary>
        public async Task SignInAsync() =>
            await Session.ApplyTokensAsync(new TokenResponse(ClaimsToken, "a-refresh-token", 300));
    }

    private static MobileAuthOptions AuthOptions() => new()
    {
        Authority = new UriBuilder(Uri.UriSchemeHttps, "idp.example", 8080, "realms/school-collab").Uri.ToString(),
        ClientId = "school-collab-mobile-client",
        RedirectScheme = "schoolcollab",
        RedirectHost = "signin-oidc",
    };

    private static string UsableUntil(TimeSpan offset) =>
        Now.Add(offset).ToString("O", CultureInfo.InvariantCulture);

    private static string Jwt(string payloadJson) =>
        $"{ToBase64Url("{{\"alg\":\"RS256\",\"typ\":\"JWT\"}}")}.{ToBase64Url(payloadJson)}.signature-not-verified";

    private static string ToBase64Url(string value) => Convert
        .ToBase64String(Encoding.UTF8.GetBytes(value))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private sealed class FakeTokenStore : ITokenStore
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

        public Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);

        public Task WriteAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task ClearAsync(string key, CancellationToken cancellationToken = default)
        {
            _values[key] = null;
            return Task.CompletedTask;
        }
    }

    /// <summary>No HTTP is expected on this path; the factory exists only to satisfy the session's
    /// constructor.</summary>
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new UnreachableHandler(), disposeHandler: false);
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The auth-state provider must never make an HTTP call.");
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
