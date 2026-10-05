using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Auth.Endpoints;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// <c>GET /auth/session/{id}/claims</c> coverage (round <c>portal-session-adoption</c> D2): the
/// cheap claims read a REMOTE host's portal-session authentication performs. It answers the D9
/// claim set as data (the <see cref="SessionEndpoints.SessionResponse"/> field spelling minus the
/// session and lifetime fields), 404 <c>session_not_found</c> for an unknown/expired/claim-less
/// session, and — the invariant the remote path exists to preserve — it uses the seam's
/// <see cref="IAuthProvider.ReadClaims"/> projection only: no refresh, no I/O, no Keycloak round
/// trip. Token-free by construction (AC11).
/// </summary>
[TestClass]
public class SessionClaimsEndpointTests
{
    private const string AccessToken = "seeded_access_token";
    private const string RefreshToken = "seeded_refresh_token";
    private const string TenantId = "00000000-0000-0000-0000-000000000002";
    private const string TeacherId = "00000000-0000-0000-0000-000000000003";

    private const string UnknownSessionId =
        "0000000000000000000000000000000000000000000000000000000000000000";

    private static string SeedSession(AuthEndpointTestHost host) =>
        host.Services.GetRequiredService<PortalSessionStore>()
            .Create(AccessToken, RefreshToken, SessionEndpointTests.JwtWithClaims("Dev School", ["user-admin"]), 3600);

    // ── Hosted read path ────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetClaims_ReturnsTheClaimSetAsData_NoTokenInBody()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        var sessionId = SeedSession(host);

        var response = await host.Client.GetAsync($"/auth/session/{sessionId}/claims");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("tenantId").GetString().Should().Be(TenantId);
        root.GetProperty("tenantName").GetString().Should().Be("Dev School");
        root.GetProperty("tenantType").GetString().Should().Be("School");
        root.GetProperty("teacherId").GetString().Should().Be(TeacherId);
        root.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Contain("user-admin");

        // The SessionResponse-only fields are deliberately absent: this read carries no session or
        // lifetime state — the D18 lifecycle belongs to the session read itself.
        root.TryGetProperty("sessionId", out _).Should().BeFalse();
        root.TryGetProperty("expiresInSeconds", out _).Should().BeFalse();

        // AC11, VALUE-based: the fixture's token strings never appear.
        json.Should().NotContain(AccessToken);
        json.Should().NotContain(RefreshToken);
        json.Should().NotContain("id_token");
    }

    [TestMethod]
    public async Task GetClaims_UnknownSession_Is404()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();

        var response = await host.Client.GetAsync($"/auth/session/{UnknownSessionId}/claims");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("session_not_found");
    }

    [TestMethod]
    public async Task GetClaims_TokenWithoutTheClaimContract_Is404()
    {
        // The session is live, but ReadClaims refuses a payload without the pinned claim contract —
        // the same fail-closed null the remote reader maps to NoResult (a bare 401), never a 500 or
        // a partial principal.
        await using var host = await AuthEndpointTestHost.StartAsync();
        var sessionId = host.Services.GetRequiredService<PortalSessionStore>().Create(
            AccessToken, RefreshToken, "header.eyJzdWIiOiJzZXNzaW9uLWZpeHR1cmUifQ.signature", 3600);

        var response = await host.Client.GetAsync($"/auth/session/{sessionId}/claims");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── The cheap-read invariant (direct invocation: fake clock + scripted transport) ───────

    [TestMethod]
    public async Task GetClaims_ElapsedAccessToken_TouchesNoTransport()
    {
        // THE invariant D2 exists for: the claims read projects custody as-is. Even with the access
        // token long elapsed (a read that refreshed would have hit Keycloak), zero requests leave.
        var clock = new SessionEndpointTests.FakeTimeProvider();
        var handler = new SessionEndpointTests.ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(clock, handler);
        var sessionId = sessions.Create(AccessToken, RefreshToken, SessionEndpointTests.JwtWithClaims("Dev School", ["user-admin"]), 300);

        clock.Now = clock.Now.AddSeconds(301); // the access token is long elapsed

        var (status, body) = await EndpointTestSupport.ExecuteAsync(
            SessionEndpoints.GetClaims(sessionId, provider));

        status.Should().Be(200);
        body.Should().Contain("Dev School");
        handler.Requests.Should().Be(0, "authenticating a portal call must not trigger a Keycloak refresh");
    }

    [TestMethod]
    public async Task GetClaims_DelegatesToReadClaims_AndNothingElse()
    {
        // A session whose custody entry exists (so ReadSessionAsync-style lifecycle work is
        // reachable in principle) still answers from the pure projection: the read is the D15
        // seam's ReadClaims member and nothing else.
        var clock = new SessionEndpointTests.FakeTimeProvider();
        var handler = new SessionEndpointTests.ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(clock, handler);
        var sessionId = sessions.Create(AccessToken, RefreshToken, SessionEndpointTests.JwtWithClaims("Dev School", ["teacher"]), 3600);

        var (status, body) = await EndpointTestSupport.ExecuteAsync(
            SessionEndpoints.GetClaims(sessionId, provider));

        status.Should().Be(200);
        body.Should().Contain(TeacherId);
        handler.Requests.Should().Be(0);
    }
}
