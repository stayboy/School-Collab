using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Auth.Endpoints;
using SchoolCollab.Auth.Options;
using SchoolCollab.Auth.Providers;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// <c>GET</c> / <c>DELETE /auth/session/{id}</c> coverage (pass B5 / spec §5.4 / D12, §9 / D13,
/// D18): the read carries the claim set **as data** (AC11); a refresh Keycloak rejects is the
/// distinct <c>session_ended</c> state on <c>410</c> — deliberately NOT the unknown-session
/// <c>404</c> and never a 2xx — while an unreachable Keycloak is <c>upstream_unreachable</c>,
/// because D18's state must mean "revoked/expired", never "the IdP was down"; and DELETE revokes
/// server-side and returns only the built <c>end_session</c> URL (round A's refresh-token-return
/// contract is superseded under AC11). Every response body is scanned RAW for tokens.
/// <para>
/// The refresh/refresh-rejection cases run the endpoint directly under a fake clock with a
/// scripted <see cref="HttpMessageHandler"/>: the hosted path cannot be made to reach Keycloak
/// rejection (the host's authority is a closed port) nor to expire a token without waiting.
/// </para>
/// </summary>
[TestClass]
public class SessionEndpointTests
{
    private const string AccessToken = "seeded_access_token";
    private const string RefreshToken = "seeded_refresh_token";
    private const string TenantId = "00000000-0000-0000-0000-000000000002";
    private const string TeacherId = "00000000-0000-0000-0000-000000000003";

    /// <summary>The custody ID token's payload IS the claim source (D9), so the fixture is a real
    /// three-segment JWT whose payload carries the pinned contract.</summary>
    private static readonly string IdToken = JwtWithClaims(tenantName: "Dev School", roles: ["user-admin"]);

    private const string UnknownSessionId =
        "0000000000000000000000000000000000000000000000000000000000000000";

    private static string SeedSession(AuthEndpointTestHost host) =>
        host.Services.GetRequiredService<PortalSessionStore>()
            .Create(AccessToken, RefreshToken, IdToken, 3600);

    // ── Hosted read path ────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Get_ReturnsTheClaimSetAsData_NoTokenInBody()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        var sessionId = SeedSession(host);

        var response = await host.Client.GetAsync($"/auth/session/{sessionId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("sessionId").GetString().Should().Be(sessionId);
        root.GetProperty("tenantId").GetString().Should().Be(TenantId);
        root.GetProperty("tenantName").GetString().Should().Be("Dev School");
        root.GetProperty("tenantType").GetString().Should().Be("School");
        root.GetProperty("teacherId").GetString().Should().Be(TeacherId);
        root.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Contain("user-admin");

        // The session's own remaining lifetime (30 minutes in this host), NOT the access token's.
        root.GetProperty("expiresInSeconds").GetInt32().Should().BeInRange(1740, 1800);

        // AC11, scanned VALUE-based: the fixture's actual token strings must never appear in a
        // response body. (Asserting on key NAMES such as `id_token` would over-reach: the raw
        // string is also a URL parameter name — see the logout flow — where no token value exists.)
        json.Should().NotContain(AccessToken);
        json.Should().NotContain(RefreshToken);
        json.Should().NotContain(IdToken);
    }

    [TestMethod]
    public async Task Get_UnknownSession_Is404()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        var response = await host.Client.GetAsync($"/auth/session/{UnknownSessionId}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── D18 lifecycle (direct invocation: fake clock + scripted Keycloak responses) ──────────

    [TestMethod]
    public async Task Get_ElapsedAccessToken_RefreshesTransparentlyInCustody()
    {
        var clock = new FakeTimeProvider();
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, TokenJson(
            accessToken: "fresh_access_token",
            refreshToken: "fresh_refresh_token",
            idToken: JwtWithClaims(tenantName: "Refreshed School", roles: ["user-admin", "platform-admin"]),
            expiresIn: 300)));

        var (provider, sessions, _) = BuildProvider(clock, handler);
        var sessionId = sessions.Create(AccessToken, RefreshToken, IdToken, 300);

        clock.Now = clock.Now.AddSeconds(301);

        var (status, body) = await EndpointTestSupport.ExecuteAsync(
            await SessionEndpoints.Get(sessionId, provider));

        status.Should().Be(200);
        body.Should().Contain("Refreshed School");
        body.Should().Contain("platform-admin");
        handler.Requests.Should().Be(1);
        handler.LastBody.Should().Contain("grant_type=refresh_token");
        handler.LastBody.Should().Contain("refresh_token=seeded_refresh_token");
        handler.LastBody.Should().NotContain("password");

        // The freshly issued tokens never reach the body either (AC11).
        body.Should().NotContain("fresh_access_token");
        body.Should().NotContain("fresh_refresh_token");
    }

    [TestMethod]
    public async Task Get_RefreshRejectedByKeycloak_IsSessionEnded_Not404()
    {
        var clock = new FakeTimeProvider();
        var handler = new ScriptedHandler(_ => Json(
            HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Refresh token expired"}"""));

        var (provider, sessions, _) = BuildProvider(clock, handler);
        var sessionId = sessions.Create(AccessToken, RefreshToken, IdToken, 300);

        clock.Now = clock.Now.AddSeconds(301);

        var (status, body) = await EndpointTestSupport.ExecuteAsync(
            await SessionEndpoints.Get(sessionId, provider));

        // Distinct from the unknown-session state AND non-2xx: the portal keys its D18 handling on
        // the body code, and this must be a state it can never confuse with "session not found".
        status.Should().Be(410);
        status.Should().NotBe(404);
        body.Should().Contain("session_ended");
        body.Should().NotContain("invalid_grant");

        // A revoked session is gone from custody — a second read is the not-found state.
        sessions.Get(sessionId).Should().BeNull();
    }

    [TestMethod]
    public async Task Get_SessionExpiresWhileTheRefreshIsInFlight_IsSessionEnded_NotA500()
    {
        // The race the reviewer pinned: the session's TTL can elapse WHILE Keycloak answers the
        // refresh. The custody entry is then gone when the fresh tokens come back, so the read
        // must report the D18 session-over state — never dereference a vanished entry.
        var clock = new FakeTimeProvider();
        var handler = new ScriptedHandler(_ =>
        {
            clock.Now = clock.Now.AddMinutes(31); // the session expires mid-call
            return Json(HttpStatusCode.OK, TokenJson(
                accessToken: "fresh_access_token",
                refreshToken: "fresh_refresh_token",
                idToken: JwtWithClaims(tenantName: "Refreshed School", roles: ["user-admin"]),
                expiresIn: 300));
        });

        var (provider, sessions, _) = BuildProvider(clock, handler);
        var sessionId = sessions.Create(AccessToken, RefreshToken, IdToken, 300);

        clock.Now = clock.Now.AddSeconds(301); // the access token has elapsed -> refresh is due

        var (status, body) = await EndpointTestSupport.ExecuteAsync(
            await SessionEndpoints.Get(sessionId, provider));

        status.Should().Be(410);
        body.Should().Contain("session_ended");
        sessions.Get(sessionId).Should().BeNull();
    }

    [TestMethod]
    public async Task Get_KeycloakOutage_IsUpstreamUnreachable_AndKeepsTheSession()
    {
        var clock = new FakeTimeProvider();
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var (provider, sessions, _) = BuildProvider(clock, handler);
        var sessionId = sessions.Create(AccessToken, RefreshToken, IdToken, 300);

        clock.Now = clock.Now.AddSeconds(301);

        var (status, body) = await EndpointTestSupport.ExecuteAsync(
            await SessionEndpoints.Get(sessionId, provider));

        // An outage is NOT a revocation: tearing down the portal's cookie here would log users out
        // for a Keycloak restart. The D18 `session ended` state stays reserved for revoked/expired.
        status.Should().Be(502);
        body.Should().Contain("upstream_unreachable");
        sessions.Get(sessionId).Should().NotBeNull();
    }

    [TestMethod]
    public async Task Get_ExpiredSession_Is404()
    {
        // The host's PortalSessionTtl is 30 minutes of real time — the fake clock (which the
        // custody store and the provider share) keeps this instant and deterministic.
        var clock = new FakeTimeProvider();
        var (provider, sessions, _) = BuildProvider(clock, new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)));
        var sessionId = sessions.Create(AccessToken, RefreshToken, IdToken, 3600);

        clock.Now = clock.Now.AddMinutes(31);

        var (status, body) = await EndpointTestSupport.ExecuteAsync(
            await SessionEndpoints.Get(sessionId, provider));

        status.Should().Be(404);
        body.Should().Contain("session_not_found");
    }

    [TestMethod]
    public async Task Get_TokenWithoutTheClaimContract_IsClaimSetIncomplete()
    {
        // Round A's fixture payload carries no tenant/teacher claims at all: the D9 contract is
        // broken, so the read fails loudly instead of rendering a half-populated page.
        var clock = new FakeTimeProvider();
        var (provider, sessions, _) = BuildProvider(clock, new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)));
        var sessionId = sessions.Create(
            AccessToken, RefreshToken, "header.eyJzdWIiOiJzZXNzaW9uLWZpeHR1cmUifQ.signature", 3600);

        var (status, body) = await EndpointTestSupport.ExecuteAsync(
            await SessionEndpoints.Get(sessionId, provider));

        status.Should().Be(500);
        body.Should().Contain("claim_set_incomplete");
    }

    // ── Logout (D13) ────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Delete_RevokesServerSide_AndReturnsTheEndSessionUrl_WithNoTokenInBody()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        var sessionId = SeedSession(host);

        var delete = await host.Client.DeleteAsync($"/auth/session/{sessionId}");
        var getAfter = await host.Client.GetAsync($"/auth/session/{sessionId}");

        delete.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await delete.Content.ReadAsStringAsync();
        var endSessionUrl = JsonDocument.Parse(body).RootElement.GetProperty("endSessionUrl").GetString();

        endSessionUrl.Should().Contain("/protocol/openid-connect/logout");
        endSessionUrl.Should().Contain("client_id=school-collab-client");

        // AC11 / B-E: NO token in ANY response body — including DELETE. Round A handed the refresh
        // token to the caller so the PORTAL could revoke it at Keycloak; this pass revokes it in
        // custody instead, and the id token never leaves the service (D13). This is the assertion
        // change that supersedes round A's contract (owner-adjudicated).
        // Value-based again (the reviewer's precision point): the three seeded token VALUES are
        // what must never travel. The body legitimately carries the end_session URL, whose
        // parameter NAMES are the OIDC vocabulary, not tokens.
        body.Should().NotContain(AccessToken);
        body.Should().NotContain(RefreshToken);
        body.Should().NotContain(IdToken);

        // The local session is gone either way — even though this host's Keycloak is unreachable
        // (its authority is a closed loopback port), so the revocation call failed best-effort.
        getAfter.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task Delete_RevokesTheRefreshTokenAtKeycloak_BeforeAnswering()
    {
        var clock = new FakeTimeProvider();
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var (provider, sessions, _) = BuildProvider(clock, handler);
        var sessionId = sessions.Create(AccessToken, RefreshToken, IdToken, 300);

        var logout = await provider.LogoutAsync(sessionId);

        handler.Requests.Should().Be(1);
        handler.LastRequestUri!.AbsolutePath.Should().Be("/realms/school-collab/protocol/openid-connect/revoke");
        handler.LastBody.Should().Contain("token_type_hint=refresh_token");
        handler.LastBody.Should().Contain($"token={RefreshToken}");
        logout.EndSessionUrl.Should().NotContain(RefreshToken);
    }

    [TestMethod]
    public async Task Delete_UnknownSession_Is404()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        var response = await host.Client.DeleteAsync($"/auth/session/{UnknownSessionId}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────────────────

    /// <summary>Builds a three-segment JWT whose payload is the pinned claim contract (D9) —
    /// the custody read decodes exactly this segment, and no signature is involved on the read
    /// path (the token came from Keycloak through this service's own exchange).</summary>
    internal static string JwtWithClaims(string tenantName, string[] roles, string? tenantId = null) =>
        "header."
        + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            tenant_id = tenantId ?? TenantId,
            tenant_name = tenantName,
            tenant_type = "School",
            teacher_id = TeacherId,
            roles,
        }))).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        + ".signature";

    internal static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string TokenJson(string accessToken, string refreshToken, string idToken, int expiresIn) =>
        JsonSerializer.Serialize(new
        {
            access_token = accessToken,
            refresh_token = refreshToken,
            id_token = idToken,
            expires_in = expiresIn,
        });

    /// <summary>Builds the D15 implementation over a shared fake clock, a scripted transport and
    /// the pass's option shape — the same wiring <c>AddAuthServices</c> performs in production.</summary>
    internal static (KeycloakAuthProvider Provider, PortalSessionStore Sessions, FakeTimeProvider Clock)
        BuildProvider(FakeTimeProvider clock, ScriptedHandler handler)
    {
        var options = new OptionsWrapper<AuthServiceOptions>(new AuthServiceOptions
        {
            Keycloak = new AuthServiceOptions.KeycloakOptions
            {
                Authority = "http://localhost:1/realms/school-collab",
                ClientId = "school-collab-client",
                ClientSecret = "test-client-secret",
                ServiceAccountClientSecret = "test-service-account-secret",
            },
            OneTimeCodeTtl = TimeSpan.FromSeconds(60),
            PortalSessionTtl = TimeSpan.FromMinutes(30),
        });

        var httpClient = new HttpClient(handler);
        var sessions = new PortalSessionStore(clock, options);
        var provider = new KeycloakAuthProvider(
            new DirectGrantExchanger(httpClient, options),
            sessions,
            new ClaimSetFactory(),
            httpClient,
            options,
            clock,
            NullLogger<KeycloakAuthProvider>.Instance);

        return (provider, sessions, clock);
    }

    /// <summary>Scripted transport (the repo's test-HTTP pattern): records what the provider sent
    /// and answers with a canned response.</summary>
    internal sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            LastRequestUri = request.RequestUri;
            LastBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    internal sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
