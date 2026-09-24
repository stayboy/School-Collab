using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Auth.Options;
using SchoolCollab.Auth.Providers;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// The D15 provider seam (pass B5): every portal-facing operation routes through the ONE
/// <see cref="IAuthProvider"/> implementation, with round A's failure taxonomy preserved
/// (authenticate), custody kept opaque (session create/read/revoke), the D18 refresh lifecycle
/// distinguished from an outage, and a token-free D13 logout. No response record of the seam can
/// carry a token — the shapes are asserted structurally as well as behaviourally (AC11).
/// </summary>
[TestClass]
public class KeycloakAuthProviderTests
{
    private const string AccessToken = "seeded_access_token";
    private const string RefreshToken = "seeded_refresh_token";
    private const string FreshAccessToken = "fresh_access_token";
    private const string FreshRefreshToken = "fresh_refresh_token";

    private const string IdTokenFixture = "header.eyJzdWIiOiJzZXNzaW9uLWZpeHR1cmUifQ.signature";

    private static readonly string ClaimBearingIdToken = SessionEndpointTests.JwtWithClaims("Dev School", ["user-admin"]);

    // ── authenticate ────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Authenticate_Success_ReturnsTheTokenSetAndNothingElse()
    {
        var handler = new SessionEndpointTests.ScriptedHandler(_ => SessionEndpointTests.Json(
            HttpStatusCode.OK,
            """{"access_token":"kc_access","refresh_token":"kc_refresh","id_token":"a.b.c","expires_in":300}"""));
        var (provider, _, _) = SessionEndpointTests.BuildProvider(new SessionEndpointTests.FakeTimeProvider(), handler);

        var result = await provider.AuthenticateAsync("alice", "correct-horse");

        result.Status.Should().Be(ProviderAuthenticationStatus.Success);
        result.IsSuccess.Should().BeTrue();
        result.Tokens!.AccessToken.Should().Be("kc_access");
        result.Tokens.RefreshToken.Should().Be("kc_refresh");
        result.Tokens.ExpiresInSeconds.Should().Be(300);
        handler.LastBody.Should().Contain("grant_type=password");
        handler.LastBody.Should().Contain("client_secret=test-client-secret");
    }

    [TestMethod]
    public async Task Authenticate_RejectedCredentials_PreserveRoundAsTaxonomy()
    {
        var handler = new SessionEndpointTests.ScriptedHandler(_ => SessionEndpointTests.Json(
            HttpStatusCode.Unauthorized,
            """{"error":"invalid_grant","error_description":"Invalid user credentials"}"""));
        var (provider, _, _) = SessionEndpointTests.BuildProvider(new SessionEndpointTests.FakeTimeProvider(), handler);

        var result = await provider.AuthenticateAsync("alice", "wrong");

        result.Status.Should().Be(ProviderAuthenticationStatus.InvalidCredentials);
        result.Tokens.Should().BeNull();
    }

    [TestMethod]
    public async Task Authenticate_DisabledAccount_IsDisabledUser()
    {
        var handler = new SessionEndpointTests.ScriptedHandler(_ => SessionEndpointTests.Json(
            HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Account is disabled, contact your administrator."}"""));
        var (provider, _, _) = SessionEndpointTests.BuildProvider(new SessionEndpointTests.FakeTimeProvider(), handler);

        var result = await provider.AuthenticateAsync("alice", "correct-horse");

        result.Status.Should().Be(ProviderAuthenticationStatus.DisabledUser);
    }

    [TestMethod]
    public async Task Authenticate_UnreachableEndpoint_IsUpstreamUnreachable_NotInvalidCredentials()
    {
        var handler = new SessionEndpointTests.ScriptedHandler(
            _ => throw new HttpRequestException("connection refused"));
        var (provider, _, _) = SessionEndpointTests.BuildProvider(new SessionEndpointTests.FakeTimeProvider(), handler);

        var result = await provider.AuthenticateAsync("alice", "correct-horse");

        result.Status.Should().Be(ProviderAuthenticationStatus.UpstreamUnreachable);
    }

    // ── session create / claims / read ──────────────────────────────────────────────────────

    [TestMethod]
    public void CreateSession_ReturnsAnOpaqueId_AndStoresTheTokenSetInCustody()
    {
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(
            new SessionEndpointTests.FakeTimeProvider(),
            new SessionEndpointTests.ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)));

        var sessionId = provider.CreateSession(
            new ProviderTokenSet(AccessToken, RefreshToken, ClaimBearingIdToken, 300));

        // The id is not derived from the tokens and is long enough to be unguessable (D12).
        sessionId.Should().HaveLength(64);
        sessionId.Should().NotContain(AccessToken);
        sessions.Get(sessionId)!.RefreshToken.Should().Be(RefreshToken);
    }

    [TestMethod]
    public void ReadClaims_LiveSession_ReturnsThePinnedClaimSet()
    {
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(
            new SessionEndpointTests.FakeTimeProvider(),
            new SessionEndpointTests.ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)));
        var sessionId = sessions.Create(AccessToken, RefreshToken, ClaimBearingIdToken, 3600);

        var claims = provider.ReadClaims(sessionId);

        claims.Should().NotBeNull();
        claims!.TenantName.Should().Be("Dev School");
        claims.TenantType.Should().Be("School");
        claims.Roles.Should().Contain("user-admin");
    }

    [TestMethod]
    public void ReadClaims_UnknownOrContractlessSession_IsNull()
    {
        var clock = new SessionEndpointTests.FakeTimeProvider();
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(
            clock, new SessionEndpointTests.ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)));

        // Unknown id, and a session whose token payload does not carry the D9 contract: both fail
        // closed to the caller (the handler turns this into "no principal").
        provider.ReadClaims("unknown-session-id").Should().BeNull();
        var contractless = sessions.Create(AccessToken, RefreshToken, IdTokenFixture, 3600);
        provider.ReadClaims(contractless).Should().BeNull();
    }

    [TestMethod]
    public async Task ReadSession_LiveSession_IsActive_AndTouchesNoNetwork()
    {
        var handler = new SessionEndpointTests.ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(new SessionEndpointTests.FakeTimeProvider(), handler);
        var sessionId = sessions.Create(AccessToken, RefreshToken, ClaimBearingIdToken, 3600);

        var read = await provider.ReadSessionAsync(sessionId);

        read.Status.Should().Be(ProviderSessionStatus.Active);
        read.Claims!.TeacherId.Should().Be("00000000-0000-0000-0000-000000000003");
        read.ExpiresInSeconds.Should().BeInRange(1740, 1800);
        handler.Requests.Should().Be(0);
    }

    [TestMethod]
    public async Task ReadSession_UnknownSession_IsNotFound()
    {
        var (provider, _, _) = SessionEndpointTests.BuildProvider(
            new SessionEndpointTests.FakeTimeProvider(),
            new SessionEndpointTests.ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)));

        var read = await provider.ReadSessionAsync("unknown-session-id");

        read.Status.Should().Be(ProviderSessionStatus.NotFound);
        read.Claims.Should().BeNull();
    }

    // ── refresh (D18) ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Refresh_Success_TracksTheRotatedRefreshTokenForTheNextRefresh()
    {
        var handler = new SessionEndpointTests.ScriptedHandler(request => request.RequestUri!.AbsolutePath
                .EndsWith("/revoke", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK)
            : SessionEndpointTests.Json(HttpStatusCode.OK, $$"""
                {"access_token":"{{FreshAccessToken}}","refresh_token":"{{FreshRefreshToken}}",
                 "id_token":"{{ClaimBearingIdToken}}","expires_in":300}
                """));
        var clock = new SessionEndpointTests.FakeTimeProvider();
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(clock, handler);
        var sessionId = sessions.Create(AccessToken, RefreshToken, ClaimBearingIdToken, 300);

        var first = await provider.RefreshAsync(sessionId);
        handler.LastBody.Should().Contain($"refresh_token={RefreshToken}");

        clock.Now = clock.Now.AddSeconds(301);
        var second = await provider.RefreshAsync(sessionId);
        var secondRequestBody = handler.LastBody;

        first.Status.Should().Be(ProviderRefreshStatus.Refreshed);
        second.Status.Should().Be(ProviderRefreshStatus.Refreshed);
        first.ExpiresInSeconds.Should().Be(300);
        // ONE source of truth: the rotated set is IN the custody store, so any other reader (e.g.
        // pass B7's mediated reads) sees exactly the tokens the next refresh will present.
        sessions.Get(sessionId)!.RefreshToken.Should().Be(FreshRefreshToken);
        sessions.Get(sessionId)!.AccessToken.Should().Be(FreshAccessToken);
        // The rotated refresh token is what the NEXT refresh presents — otherwise rotation (when a
        // realm enables it) would kill the session after the first refresh.
        secondRequestBody.Should().Contain($"refresh_token={FreshRefreshToken}");
    }

    [TestMethod]
    public async Task Refresh_RejectedRefreshToken_EndsTheSession_AndDropsCustody()
    {
        var handler = new SessionEndpointTests.ScriptedHandler(_ => SessionEndpointTests.Json(
            HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""));
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(new SessionEndpointTests.FakeTimeProvider(), handler);
        var sessionId = sessions.Create(AccessToken, RefreshToken, ClaimBearingIdToken, 300);

        var refresh = await provider.RefreshAsync(sessionId);

        refresh.Status.Should().Be(ProviderRefreshStatus.SessionEnded);
        sessions.Get(sessionId).Should().BeNull();
    }

    [TestMethod]
    public async Task Refresh_SessionExpiresWhileKeycloakAnswers_EndsTheSession_InsteadOfThrowing()
    {
        // The custody entry can disappear mid-refresh (TTL elapsed, or a concurrent revoke). The
        // refreshed tokens are then unusable — there is no live session to attach them to — and the
        // caller gets the D18 session-over state, not an exception.
        var clock = new SessionEndpointTests.FakeTimeProvider();
        var handler = new SessionEndpointTests.ScriptedHandler(_ =>
        {
            clock.Now = clock.Now.AddMinutes(31);
            return SessionEndpointTests.Json(HttpStatusCode.OK, $$"""
                {"access_token":"{{FreshAccessToken}}","refresh_token":"{{FreshRefreshToken}}",
                 "id_token":"{{ClaimBearingIdToken}}","expires_in":300}
                """);
        });
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(clock, handler);
        var sessionId = sessions.Create(AccessToken, RefreshToken, ClaimBearingIdToken, 300);

        var refresh = await provider.RefreshAsync(sessionId);

        refresh.Status.Should().Be(ProviderRefreshStatus.SessionEnded);
        sessions.Get(sessionId).Should().BeNull("the expired session must not be resurrected by a refresh.");
    }

    [TestMethod]
    public async Task Refresh_KeycloakOutage_IsUpstreamUnreachable_AndKeepsCustody()
    {
        var handler = new SessionEndpointTests.ScriptedHandler(
            _ => throw new HttpRequestException("connection refused"));
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(new SessionEndpointTests.FakeTimeProvider(), handler);
        var sessionId = sessions.Create(AccessToken, RefreshToken, ClaimBearingIdToken, 300);

        var refresh = await provider.RefreshAsync(sessionId);

        refresh.Status.Should().Be(ProviderRefreshStatus.UpstreamUnreachable);
        sessions.Get(sessionId).Should().NotBeNull();
    }

    [TestMethod]
    public async Task Refresh_UnknownSession_IsNotFound()
    {
        var (provider, _, _) = SessionEndpointTests.BuildProvider(
            new SessionEndpointTests.FakeTimeProvider(),
            new SessionEndpointTests.ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)));

        (await provider.RefreshAsync("unknown-session-id")).Status.Should().Be(ProviderRefreshStatus.NotFound);
    }

    // ── revoke / logout (D13) ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void RevokeSession_DropsCustody_WithoutHandingOutTheRefreshToken()
    {
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(
            new SessionEndpointTests.FakeTimeProvider(),
            new SessionEndpointTests.ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)));
        var sessionId = sessions.Create(AccessToken, RefreshToken, ClaimBearingIdToken, 300);

        var revocation = provider.RevokeSession(sessionId);

        revocation.Found.Should().BeTrue();
        sessions.Get(sessionId).Should().BeNull();
        provider.RevokeSession(sessionId).Found.Should().BeFalse();
    }

    [TestMethod]
    public async Task Logout_RevokesServerSide_AndReturnsATokenFreeEndSessionUrl()
    {
        var handler = new SessionEndpointTests.ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(new SessionEndpointTests.FakeTimeProvider(), handler);
        var sessionId = sessions.Create(AccessToken, RefreshToken, ClaimBearingIdToken, 300);

        var logout = await provider.LogoutAsync(sessionId);

        logout.Found.Should().BeTrue();
        logout.EndSessionUrl.Should().StartWith("http://localhost:1/realms/school-collab/protocol/openid-connect/logout?");
        logout.EndSessionUrl.Should().Contain("client_id=school-collab-client");

        // D13 read against AC11: the URL is built in custody and carries NO credential at all —
        // neither the id token (which is why id_token_hint is deliberately absent) nor a refresh
        // token (the round-A exemption is closed: the SERVICE revokes, the portal never sees one).
        logout.EndSessionUrl.Should().NotContain(AccessToken);
        logout.EndSessionUrl.Should().NotContain(RefreshToken);
        logout.EndSessionUrl.Should().NotContain(ClaimBearingIdToken);

        handler.LastRequestUri!.AbsolutePath.Should().Be("/realms/school-collab/protocol/openid-connect/revoke");
        sessions.Get(sessionId).Should().BeNull();
    }

    [TestMethod]
    public async Task Logout_KeycloakUnreachable_StillEndsTheLocalSession()
    {
        var handler = new SessionEndpointTests.ScriptedHandler(
            _ => throw new HttpRequestException("connection refused"));
        var (provider, sessions, _) = SessionEndpointTests.BuildProvider(new SessionEndpointTests.FakeTimeProvider(), handler);
        var sessionId = sessions.Create(AccessToken, RefreshToken, ClaimBearingIdToken, 300);

        var logout = await provider.LogoutAsync(sessionId);

        // Revocation at Keycloak is defense-in-depth (D13): an outage must not make logout fail.
        logout.Found.Should().BeTrue();
        logout.EndSessionUrl.Should().NotBeNull();
        sessions.Get(sessionId).Should().BeNull();
    }

    [TestMethod]
    public async Task Logout_UnknownSession_ReportsNotFound_AndBuildsNothing()
    {
        var (provider, _, _) = SessionEndpointTests.BuildProvider(
            new SessionEndpointTests.FakeTimeProvider(),
            new SessionEndpointTests.ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)));

        var logout = await provider.LogoutAsync("unknown-session-id");

        logout.Found.Should().BeFalse();
        logout.EndSessionUrl.Should().BeNull();
    }

    // ── the seam's shapes (AC11, structural) ────────────────────────────────────────────────

    [TestMethod]
    public void PortalFacingResultShapes_CannotCarryAToken()
    {
        // Structural, not behavioural: a token field added to a result the endpoints serialize
        // would be a leak the endpoint tests might not reach. Every portal-facing record's
        // property surface is inspected here.
        foreach (var type in new[]
        {
            typeof(ProviderSessionRead),
            typeof(ProviderLogout),
            typeof(ProviderRevocation),
            typeof(SchoolCollab.Auth.Endpoints.SessionEndpoints.SessionResponse),
            typeof(SchoolCollab.Auth.Endpoints.SessionEndpoints.DeleteSessionResponse),
        })
        {
            var names = type.GetProperties().Select(p => p.Name).ToArray();
            names.Should().NotContain(
                name => name.Contains("token", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("secret", StringComparison.OrdinalIgnoreCase),
                $"{type.Name} is serialized to the portal and must stay token-free (AC11)");
        }

        // TokenSet/ProviderAuthentication are internal-only: they exist to move a token set INTO
        // custody, and no endpoint parameter or response uses them.
        typeof(ProviderTokenSet).GetProperties().Select(p => p.Name)
            .Should().Contain(["AccessToken", "RefreshToken", "IdToken"]);
    }

    [TestMethod]
    public void ClaimSetFactoryAndTheSeamAgreeOnTheClaimNames()
    {
        // The handler materializes the principal from these very names; a drift between the
        // factory's constants and the realm mapper contract is what D9 forbids.
        ClaimSetFactory.TenantIdClaim.Should().Be("tenant_id");
        ClaimSetFactory.TeacherIdClaim.Should().Be("teacher_id");
        ClaimSetFactory.RolesClaim.Should().Be("roles");
    }
}
