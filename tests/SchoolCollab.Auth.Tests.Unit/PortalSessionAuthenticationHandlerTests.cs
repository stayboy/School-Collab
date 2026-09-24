using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Auth.Auth;
using SchoolCollab.Auth.Providers;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// The portal-session authentication scheme (pass B5 / spec D12): the opaque session id the portal
/// presents becomes a principal carrying the SAME claim set the OIDC paths produce (D9) — including
/// <see cref="ClaimTypes.Role"/> entries, so <c>RequireRole</c> resolves — and every failure mode
/// (no header, unknown session, expired session, a token payload without the claim contract) fails
/// closed with <c>NoResult</c> and a bare <c>401</c> challenge, never a redirect and never a
/// partially-populated principal.
/// </summary>
[TestClass]
public class PortalSessionAuthenticationHandlerTests
{
    private const string AccessToken = "seeded_access_token";
    private const string RefreshToken = "seeded_refresh_token";
    private const string TeacherId = "00000000-0000-0000-0000-000000000003";

    private static readonly string ClaimBearingIdToken =
        SessionEndpointTests.JwtWithClaims("Dev School", ["user-admin", "platform-admin"]);

    [TestMethod]
    public async Task ValidSession_AuthenticatesWithTheFullClaimSet()
    {
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(
            new SessionEndpointTests.FakeTimeProvider(),
            new SessionEndpointTests.ScriptedHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway)));
        var sessionId = sessions.Create(AccessToken, RefreshToken, ClaimBearingIdToken, 3600);

        var (result, _) = await AuthenticateAsync(provider, sessionId);

        result.Succeeded.Should().BeTrue();
        var principal = result.Principal!;
        principal.Identity!.AuthenticationType.Should().Be(PortalSessionAuthenticationHandler.SchemeName);
        principal.Identity.IsAuthenticated.Should().BeTrue();
        principal.FindFirst(ClaimSetFactory.TenantIdClaim)!.Value
            .Should().Be("00000000-0000-0000-0000-000000000002");
        principal.FindFirst(ClaimSetFactory.TenantNameClaim)!.Value.Should().Be("Dev School");
        principal.FindFirst(ClaimSetFactory.TenantTypeClaim)!.Value.Should().Be("School");
        principal.FindFirst(ClaimSetFactory.TeacherIdClaim)!.Value.Should().Be(TeacherId);

        // D9 parity: RequireRole/IsInRole must resolve on this path exactly as on cookie/bearer.
        principal.IsInRole("user-admin").Should().BeTrue();
        principal.IsInRole("platform-admin").Should().BeTrue();
        principal.IsInRole("some-other-role").Should().BeFalse();
    }

    [TestMethod]
    public async Task NoSessionHeader_IsNoResult_AndChallengesWith401()
    {
        var (provider, _, _) = SessionEndpointTests.BuildProvider(
            new SessionEndpointTests.FakeTimeProvider(),
            new SessionEndpointTests.ScriptedHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway)));

        var (result, context) = await AuthenticateAsync(provider, sessionId: null);

        result.Succeeded.Should().BeFalse();
        result.None.Should().BeTrue();
        context.Response.StatusCode.Should().Be(401);
        context.Response.Headers.Location.Should().BeEmpty();
    }

    [TestMethod]
    public async Task UnknownSession_IsNoResult()
    {
        var (provider, _, _) = SessionEndpointTests.BuildProvider(
            new SessionEndpointTests.FakeTimeProvider(),
            new SessionEndpointTests.ScriptedHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway)));

        var (result, _) = await AuthenticateAsync(provider, "0000000000000000000000000000000000000000000000000000000000000000");

        result.Succeeded.Should().BeFalse();
        result.None.Should().BeTrue();
    }

    [TestMethod]
    public async Task ExpiredSession_IsNoResult()
    {
        var clock = new SessionEndpointTests.FakeTimeProvider();
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(
            clock, new SessionEndpointTests.ScriptedHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway)));
        var sessionId = sessions.Create(AccessToken, RefreshToken, ClaimBearingIdToken, 3600);

        clock.Now = clock.Now.AddMinutes(31);

        var (result, _) = await AuthenticateAsync(provider, sessionId);

        result.Succeeded.Should().BeFalse();
        result.None.Should().BeTrue();
    }

    [TestMethod]
    public async Task SessionWithoutTheClaimContract_IsNoResult()
    {
        // The session IS live, but its token payload lacks the tenant/teacher claims the realm
        // mappers contract (D9). Fail closed: a principal half-populated from an unreadable
        // claim set would authorize nothing useful and hide the drift.
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(
            new SessionEndpointTests.FakeTimeProvider(),
            new SessionEndpointTests.ScriptedHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway)));
        var sessionId = sessions.Create(
            AccessToken, RefreshToken, "header.eyJzdWIiOiJzZXNzaW9uLWZpeHR1cmUifQ.signature", 3600);

        var (result, _) = await AuthenticateAsync(provider, sessionId);

        result.Succeeded.Should().BeFalse();
        result.None.Should().BeTrue();
    }

    [TestMethod]
    public async Task AuthenticatingAPortalCall_TouchesNoNetwork()
    {
        // The D18 lifecycle (and its Keycloak round-trips) belongs to the session READ: the scheme
        // authenticates on every portal call and must stay a pure custody lookup.
        var handler = new SessionEndpointTests.ScriptedHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway));
        var clock = new SessionEndpointTests.FakeTimeProvider();
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(clock, handler);
        var sessionId = sessions.Create(AccessToken, RefreshToken, ClaimBearingIdToken, 300);

        clock.Now = clock.Now.AddSeconds(301); // the access token is long elapsed

        var (result, _) = await AuthenticateAsync(provider, sessionId);

        result.Succeeded.Should().BeTrue();
        handler.Requests.Should().Be(0);
    }

    [TestMethod]
    public async Task PortalSession_WithUserAdminRole_AuthorizesTheAdminSurface()
    {
        // THE POINT of the policy attachment (AuthEndpointGroup row): the admin UI lives in the
        // portal, which presents only its opaque session id — no token anywhere (AC11).
        await using var host = await AuthEndpointTestHost.StartAsync();
        StubAdmin(host);
        var sessionId = SeedHostSession(host, roles: ["user-admin"]);

        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/admin/users");
        request.Headers.Add(PortalSessionAuthenticationHandler.SessionHeaderName, sessionId);
        var response = await host.Client.SendAsync(request);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("alice");
    }

    [TestMethod]
    public async Task PortalSession_WithoutUserAdminRole_Is403()
    {
        // The gate is still a ROLE gate: a portal session that is merely authenticated must not
        // reach the admin surface. This half is what stops the pair passing vacuously.
        await using var host = await AuthEndpointTestHost.StartAsync();
        var sessionId = SeedHostSession(host, roles: ["teacher"]);

        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/admin/users");
        request.Headers.Add(PortalSessionAuthenticationHandler.SessionHeaderName, sessionId);
        var response = await host.Client.SendAsync(request);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task PortalSession_UnknownSession_Is401()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/admin/users");
        request.Headers.Add(PortalSessionAuthenticationHandler.SessionHeaderName, "not-a-live-session");
        var response = await host.Client.SendAsync(request);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull();
    }

    /// <summary>Seeds a live custody session bearing the D9 claim contract with the given realm
    /// roles, and returns its opaque id.</summary>
    private static string SeedHostSession(AuthEndpointTestHost host, string[] roles) =>
        host.Services.GetRequiredService<PortalSessionStore>()
            .Create(AccessToken, RefreshToken, SessionEndpointTests.JwtWithClaims("Dev School", roles), 3600);

    /// <summary>Routes the admin REST client's token fetch to a canned success and the users list
    /// to one canned user (any other call fails loudly rather than touching the network).</summary>
    private static void StubAdmin(AuthEndpointTestHost host) => host.AdminStub.OnSendAsync = (request, _) =>
        Task.FromResult(request.RequestUri!.AbsolutePath.Contains("/protocol/openid-connect/token", StringComparison.Ordinal)
            ? SessionEndpointTests.Json(
                System.Net.HttpStatusCode.OK, """{"access_token":"admin-bearer-token","expires_in":3600}""")
            : request.RequestUri.AbsolutePath.EndsWith("/users", StringComparison.Ordinal)
                ? SessionEndpointTests.Json(
                    System.Net.HttpStatusCode.OK, """[{"id":"u1","username":"alice","enabled":true}]""")
                : new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway));

    /// <summary>Drives the handler through the framework's own <see cref="IAuthenticationHandler"/>
    /// surface (initialize → authenticate → challenge), so neither the principal nor the 401 is
    /// asserted through a private path the framework does not use.</summary>
    private static async Task<(AuthenticateResult Result, DefaultHttpContext Context)> AuthenticateAsync(
        IAuthProvider provider,
        string? sessionId)
    {
        var context = new DefaultHttpContext();
        if (sessionId is not null)
        {
            context.Request.Headers[PortalSessionAuthenticationHandler.SessionHeaderName] = sessionId;
        }

        var handler = new PortalSessionAuthenticationHandler(
            OptionsMonitor(),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            provider);

        var scheme = new AuthenticationScheme(
            PortalSessionAuthenticationHandler.SchemeName,
            displayName: null,
            handlerType: typeof(PortalSessionAuthenticationHandler));

        var authenticationHandler = (IAuthenticationHandler)handler;
        await authenticationHandler.InitializeAsync(scheme, context);
        var result = await authenticationHandler.AuthenticateAsync();
        await authenticationHandler.ChallengeAsync(properties: null);

        return (result, context);
    }

    /// <summary>An <see cref="IOptionsMonitor{T}"/> over a fresh options instance — the scheme
    /// carries no settings, so nothing here is under test.</summary>
    private static IOptionsMonitor<PortalSessionAuthenticationOptions> OptionsMonitor() => new Monitor();

    private sealed class Monitor : IOptionsMonitor<PortalSessionAuthenticationOptions>
    {
        private readonly PortalSessionAuthenticationOptions _options = new();

        public PortalSessionAuthenticationOptions CurrentValue => _options;

        public PortalSessionAuthenticationOptions Get(string? name) => _options;

        public IDisposable? OnChange(Action<PortalSessionAuthenticationOptions, string?> listener) => null;
    }
}
