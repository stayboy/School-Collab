using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Auth.Auth;
using SchoolCollab.Auth.Endpoints;
using SchoolCollab.Auth.Providers;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// The D17 mediated picker reads (pass B7): <c>GET /auth/pickers/tenants</c> and
/// <c>GET /auth/pickers/teachers</c>.
/// <para>
/// What this file pins, in the round's terms: the routes exist AND are authorization-gated on the
/// portal-session scheme (an anonymous caller gets a bare 401 — a 404 would mean the route is not
/// mapped, a 2xx/500 would mean the policy is not attached); every mediated call runs as the
/// session's user by forwarding the **custody access token** as a bearer credential, and that token
/// is the CURRENT one (an elapsed token is refreshed first — the whole point of reading through
/// <see cref="ISessionTokenAccessor"/> rather than the custody store); the upstream failure classes
/// all collapse to one fail-closed degraded status and never to a 500; and no response body carries
/// a token, a secret or an upstream body (AC9/AC11) — including the upstream's extra fields, which
/// the typed projection drops.
/// </para>
/// <para>
/// Split of technique, mirroring <c>SessionEndpointTests</c>: the authorization/mapping axes run
/// through the real Kestrel pipeline (<see cref="AuthEndpointTestHost"/>), while the token, session
/// and upstream axes are invoked directly under a scripted transport and a fake clock — the hosted
/// path cannot script settings-api/students-api nor expire an access token without waiting.
/// </para>
/// </summary>
[TestClass]
public class MediatedReadEndpointTests
{
    private const string TenantsRoute = "/auth/pickers/tenants";
    private const string TeachersRoute = "/auth/pickers/teachers";

    private const string StaleAccessToken = "seeded_access_token";
    private const string StaleRefreshToken = "seeded_refresh_token";
    private const string RotatedAccessToken = "rotated_access_token";
    private const string RotatedRefreshToken = "rotated_refresh_token";
    private const string UnknownSessionId =
        "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>Distinctive strings only an upstream body could carry: their ABSENCE from every
    /// mediated response is the projection/token-scan assertion.</summary>
    private const string UpstreamTokenMarker = "upstream_access_token_marker";
    private const string UpstreamSecretMarker = "upstream_secret_marker";

    private static readonly string CustodyIdToken =
        SessionEndpointTests.JwtWithClaims(tenantName: "Dev School", roles: ["user-admin"]);

    private static readonly string TenantDirectoryJson =
        "[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"Dev School\","
        + "\"type\":\"School\",\"accessToken\":\"" + UpstreamTokenMarker + "\","
        + "\"secret\":\"" + UpstreamSecretMarker + "\"}]";

    // ── Routes and authorization (real pipeline) ─────────────────────────────────────────────

    [TestMethod]
    public async Task Pickers_WithoutAPortalSession_Are401_Not404()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();

        foreach (var route in new[] { TenantsRoute, TeachersRoute })
        {
            var response = await host.Client.GetAsync(route);
            var body = await response.Content.ReadAsStringAsync();

            // 401 (not 404) proves the route is MAPPED and that an anonymous caller is refused. The
            // policy half is pinned by Pickers_WithAnUnknownSession_Are401 below (a session-shaped
            // id that the scheme cannot resolve answers 404 when the policy is absent, 401 when it
            // is present) — this test alone would also pass off the mediator's own fail-closed
            // guard, so the two together are what make the authorization explicit.
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{route} must require a portal session");
            response.Headers.Location.Should().BeNull("the portal-session challenge is a bare 401, never a redirect");
            body.Should().NotContain(StaleAccessToken);
            body.Should().NotContain(UnknownSessionId);
        }
    }

    [TestMethod]
    public async Task Pickers_WithAnUnknownSession_Are401_AndDiscloseNothing()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();

        foreach (var route in new[] { TenantsRoute, TeachersRoute })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.Add(PortalSessionAuthenticationHandler.SessionHeaderName, UnknownSessionId);

            var response = await host.Client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            // Unknown, expired and unusable sessions all fail closed at the scheme's challenge —
            // and this is the assertion that discriminates the POLICY: with the picker group
            // unauthorized, the same request reaches the mediator and answers 404 instead.
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            body.Should().NotContain("upstream_unreachable");
            body.Should().NotContain(UnknownSessionId);
        }
    }

    // ── Registration (the pass's DI row) ────────────────────────────────────────────────────

    [TestMethod]
    public async Task MediatedReaders_AndTheCustodyAccessor_AreRegistered()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();

        host.Services.GetRequiredService<TenantDirectoryReader>().Should().NotBeNull();
        host.Services.GetRequiredService<TeacherDirectoryReader>().Should().NotBeNull();

        // ONE custody implementation: the accessor a mediated read uses must be the very instance
        // behind the D15 seam — a second instance would be a second view of a session's tokens.
        host.Services.GetRequiredService<ISessionTokenAccessor>()
            .Should().BeSameAs(host.Services.GetRequiredService<IAuthProvider>());
    }

    // ── Forwarding, tenant scoping and data-only responses (direct invocation) ──────────────

    [TestMethod]
    public async Task Tenants_WithALiveSession_ForwardsTheCustodyToken_AndReturnsDataOnly()
    {
        var clock = new SessionEndpointTests.FakeTimeProvider();
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(
            clock,
            new SessionEndpointTests.ScriptedHandler(_ => throw new InvalidOperationException("Keycloak must not be called")));
        var sessionId = sessions.Create(StaleAccessToken, StaleRefreshToken, CustodyIdToken, 3600);

        var upstream = new RecordingHandler(_ =>
            Json(HttpStatusCode.OK, TenantDirectoryJson));

        var (status, body) = await EndpointTestSupport.ExecuteAsync(
            await MediatedReadEndpoints.Tenants(ContextPresenting(sessionId), provider, TenantReader(upstream)));

        status.Should().Be(200);
        body.Should().Contain("Dev School");

        // Runs AS THE SESSION'S USER: the custody access token is the bearer credential the data
        // API sees, on settings-api's real route. The data API's own tenant middleware resolves the
        // tenant from that token, which is what scopes the result exactly as a direct call.
        upstream.Authorization.Should().Be($"Bearer {StaleAccessToken}");
        upstream.LastRequestUri!.AbsolutePath.Should().Be("/api/tenants");

        // DATA ONLY: the picker projection drops the upstream's extra fields, and no token of any
        // kind (seeded, stored or upstream-supplied) reaches the body (AC9/AC11).
        var root = JsonDocument.Parse(body).RootElement;
        root.ValueKind.Should().Be(JsonValueKind.Array);
        body.Should().NotContain(UpstreamTokenMarker);
        body.Should().NotContain(UpstreamSecretMarker);
        body.Should().NotContain(StaleAccessToken);
        body.Should().NotContain(StaleRefreshToken);
        body.Should().NotContain(CustodyIdToken);
    }

    [TestMethod]
    public async Task Teachers_WithALiveSession_ForwardsTheCustodyToken_ToTheStudentsApiRoute()
    {
        var clock = new SessionEndpointTests.FakeTimeProvider();
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(
            clock,
            new SessionEndpointTests.ScriptedHandler(_ => throw new InvalidOperationException("Keycloak must not be called")));
        var sessionId = sessions.Create(StaleAccessToken, StaleRefreshToken, CustodyIdToken, 3600);

        var upstream = new RecordingHandler(_ => Json(
            HttpStatusCode.OK,
            "[{\"id\":\"22222222-2222-2222-2222-222222222222\",\"firstName\":\"Ada\","
            + "\"lastName\":\"Lovelace\",\"displayName\":\"A. Lovelace\",\"isDeleted\":false}]"));

        var (status, body) = await EndpointTestSupport.ExecuteAsync(
            await MediatedReadEndpoints.Teachers(ContextPresenting(sessionId), provider, TeacherReader(upstream)));

        status.Should().Be(200);
        body.Should().Contain("A. Lovelace");
        upstream.Authorization.Should().Be($"Bearer {StaleAccessToken}");
        upstream.LastRequestUri!.AbsolutePath.Should().Be("/teachers");
        body.Should().NotContain(StaleAccessToken);
        body.Should().NotContain(CustodyIdToken);
    }

    [TestMethod]
    public async Task Tenants_WhenTheAccessTokenHasElapsed_ForwardsTheRefreshedToken_NotTheStaleOne()
    {
        var clock = new SessionEndpointTests.FakeTimeProvider();
        var keycloak = new SessionEndpointTests.ScriptedHandler(_ => Json(HttpStatusCode.OK,
            "{\"access_token\":\"" + RotatedAccessToken + "\",\"refresh_token\":\"" + RotatedRefreshToken
            + "\",\"expires_in\":300}"));
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(clock, keycloak);
        var sessionId = sessions.Create(StaleAccessToken, StaleRefreshToken, CustodyIdToken, 300);

        clock.Now = clock.Now.AddSeconds(301);

        var upstream = new RecordingHandler(_ => Json(HttpStatusCode.OK, TenantDirectoryJson));

        var (status, body) = await EndpointTestSupport.ExecuteAsync(
            await MediatedReadEndpoints.Tenants(ContextPresenting(sessionId), provider, TenantReader(upstream)));

        status.Should().Be(200);

        // THE stale-token axis: reading the token through the seam (not the custody store) is what
        // makes this hold — the elapsed token is refreshed first, and the credential the data API
        // receives is the rotated one. A direct store read would forward the stale token and the
        // data API would reject the call.
        keycloak.Requests.Should().Be(1);
        upstream.Authorization.Should().Be($"Bearer {RotatedAccessToken}");
        upstream.Authorization.Should().NotContain(StaleAccessToken);
        upstream.Authorization.Should().NotContain(StaleRefreshToken);

        // Neither the refreshed token nor the refresh token reaches the caller (AC11).
        body.Should().NotContain(RotatedAccessToken);
        body.Should().NotContain(RotatedRefreshToken);
        body.Should().NotContain(StaleAccessToken);
    }

    // ── Fail-closed taxonomy (never a 500, never an upstream body) ───────────────────────────

    [TestMethod]
    public async Task Tenants_WhenKeycloakRejectsTheRefresh_Is410SessionEnded_AndNeverReadsUpstream()
    {
        var clock = new SessionEndpointTests.FakeTimeProvider();
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(
            clock,
            new SessionEndpointTests.ScriptedHandler(_ => Json(
                HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Refresh token expired"}""")));
        var sessionId = sessions.Create(StaleAccessToken, StaleRefreshToken, CustodyIdToken, 300);

        clock.Now = clock.Now.AddSeconds(301);

        var upstream = new RecordingHandler(_ => Json(HttpStatusCode.OK, TenantDirectoryJson));

        var (status, body) = await EndpointTestSupport.ExecuteAsync(
            await MediatedReadEndpoints.Tenants(ContextPresenting(sessionId), provider, TenantReader(upstream)));

        // The D18 session state, distinct from a stale cookie (404) and from an outage (502).
        status.Should().Be(410);
        body.Should().Contain("session_ended");
        body.Should().NotContain("invalid_grant");
        upstream.Requests.Should().Be(0, "an ended session must not produce an upstream call");
    }

    [TestMethod]
    public async Task Tenants_WhenKeycloakIsDown_Is502_AndKeepsTheSession()
    {
        var clock = new SessionEndpointTests.FakeTimeProvider();
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(
            clock,
            new SessionEndpointTests.ScriptedHandler(_ => throw new HttpRequestException("connection refused")));
        var sessionId = sessions.Create(StaleAccessToken, StaleRefreshToken, CustodyIdToken, 300);

        clock.Now = clock.Now.AddSeconds(301);

        var upstream = new RecordingHandler(_ => Json(HttpStatusCode.OK, TenantDirectoryJson));

        var (status, body) = await EndpointTestSupport.ExecuteAsync(
            await MediatedReadEndpoints.Tenants(ContextPresenting(sessionId), provider, TenantReader(upstream)));

        // An outage is NOT the end of a session (nothing was revoked) — the degraded status only.
        status.Should().Be(502);
        body.Should().Contain("upstream_unreachable");
        upstream.Requests.Should().Be(0);
        sessions.Get(sessionId).Should().NotBeNull("an unreachable IdP must not tear the session down");
    }

    [TestMethod]
    public async Task Tenants_WithoutAPresentedSession_Is401_AndNeverReadsUpstream()
    {
        var clock = new SessionEndpointTests.FakeTimeProvider();
        var (provider, _, _) = SessionEndpointTests.BuildProvider(
            clock,
            new SessionEndpointTests.ScriptedHandler(_ => throw new InvalidOperationException("Keycloak must not be called")));

        var upstream = new RecordingHandler(_ => Json(HttpStatusCode.OK, TenantDirectoryJson));

        var (status, body) = await EndpointTestSupport.ExecuteAsync(
            await MediatedReadEndpoints.Tenants(ContextPresenting(sessionId: null), provider, TenantReader(upstream)));

        // Unreachable through the group's policy (the challenge runs first); asserted so the
        // mediator can never mediate for an unidentified caller if the policy is loosened.
        status.Should().Be(401);
        body.Should().Contain("session_not_found");
        upstream.Requests.Should().Be(0);
    }

    [TestMethod]
    public async Task Tenants_WhenTheSessionVanishesBeforeTheMediation_Is404_AndNeverReadsUpstream()
    {
        var clock = new SessionEndpointTests.FakeTimeProvider();
        var (provider, _, _) = SessionEndpointTests.BuildProvider(
            clock,
            new SessionEndpointTests.ScriptedHandler(_ => throw new InvalidOperationException("Keycloak must not be called")));

        var upstream = new RecordingHandler(_ => Json(HttpStatusCode.OK, TenantDirectoryJson));

        // A session that authenticated a moment ago and was revoked/expired in between.
        var (status, body) = await EndpointTestSupport.ExecuteAsync(
            await MediatedReadEndpoints.Tenants(ContextPresenting(UnknownSessionId), provider, TenantReader(upstream)));

        status.Should().Be(404);
        body.Should().Contain("session_not_found");
        body.Should().NotContain(UnknownSessionId);
        upstream.Requests.Should().Be(0);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.InternalServerError, "{\"error\":\"boom\",\"refresh_token\":\"" + UpstreamTokenMarker + "\"}", DisplayName = "upstream 500")]
    [DataRow(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_token\"}", DisplayName = "upstream rejected the forward")]
    [DataRow(HttpStatusCode.OK, "not json at all", DisplayName = "upstream body is not the expected shape")]
    public async Task Teachers_WhenTheUpstreamFails_Is502Degraded_NeverA500_AndNeverEchoesTheUpstream(
        HttpStatusCode upstreamStatus,
        string upstreamBody)
    {
        var clock = new SessionEndpointTests.FakeTimeProvider();
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(
            clock,
            new SessionEndpointTests.ScriptedHandler(_ => throw new InvalidOperationException("Keycloak must not be called")));
        var sessionId = sessions.Create(StaleAccessToken, StaleRefreshToken, CustodyIdToken, 3600);

        var upstream = new RecordingHandler(_ => Json(upstreamStatus, upstreamBody));

        var (status, body) = await EndpointTestSupport.ExecuteAsync(
            await MediatedReadEndpoints.Teachers(ContextPresenting(sessionId), provider, TeacherReader(upstream)));

        upstream.Requests.Should().Be(1);
        status.Should().Be(502);
        status.Should().NotBe(500);
        body.Should().Contain("upstream_unreachable");

        // One clean degraded status: no upstream status code, error code or body text travels.
        body.Should().NotContain("boom");
        body.Should().NotContain("invalid_token");
        body.Should().NotContain("not json at all");
        body.Should().NotContain(UpstreamTokenMarker);
        body.Should().NotContain(StaleAccessToken);
    }

    // ── Fixtures ───────────────────────────────────────────────────────────────────────────

    private static DefaultHttpContext ContextPresenting(string? sessionId)
    {
        var context = new DefaultHttpContext();
        if (sessionId is not null)
        {
            context.Request.Headers[PortalSessionAuthenticationHandler.SessionHeaderName] = sessionId;
        }

        return context;
    }

    private static TenantDirectoryReader TenantReader(RecordingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https+http://settings-api") },
            NullLogger<MediatedReader<PickerTenant>>.Instance);

    private static TeacherDirectoryReader TeacherReader(RecordingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https+http://students-api") },
            NullLogger<MediatedReader<PickerTeacher>>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>Scripted upstream transport that records what a mediated read actually sent — the
    /// bearer credential especially, which is the whole D17 assertion (scripted
    /// <see cref="HttpMessageHandler"/>, per the repo's HTTP-testing rule).</summary>
    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            LastRequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(respond(request));
        }
    }
}
