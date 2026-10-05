using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Auth.Endpoints;
using SchoolCollab.Auth.Options;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// <c>POST /auth/handshake/session</c> coverage (round <c>portal-session-adoption</c> D3): a D6
/// handshake code plus its bound redirect URI redeems for the caller's opaque session id — the
/// minimal generalization that lets a second portal establish its own cookie. The failure taxonomy
/// is the <c>BootstrapRedeem</c> one over the handshake store (replay → <c>invalid_code</c> 404,
/// TTL → <c>code_expired</c> 400, URI mismatch → <c>redirect_uri_mismatch</c> 400, blank URI →
/// <c>redirect_uri_required</c> 400, omitted code → <c>invalid_code</c> 404, never a 500), there is
/// deliberately NO allowlist re-check (the allowlist is enforced at issuance,
/// <c>ExchangeEndpoints.Exchange</c>), and a bootstrap code is NOT redeemable
/// here — the two code namespaces are disjoint exactly as built (AC4). The response carries only
/// the opaque session id — no token (AC11).
/// </summary>
[TestClass]
public class HandshakeSessionEndpointTests
{
    private const string AccessToken = "seeded_access_token";
    private const string RefreshToken = "seeded_refresh_token";
    private const string RedirectUri = "http://localhost:5310/auth/callback";

    /// <summary>Mints a D6 code bound to <paramref name="redirectUri"/> for a live custody session,
    /// exactly as the exchange does, and returns the code.</summary>
    private static string MintCode(AuthEndpointTestHost host, string redirectUri = RedirectUri) =>
        host.Services.GetRequiredService<OneTimeCodeStore>().Create(
            redirectUri,
            host.Services.GetRequiredService<PortalSessionStore>().Create(
                AccessToken, RefreshToken, SessionEndpointTests.JwtWithClaims("Dev School", ["teacher"]), 3600));

    private static async Task<HttpResponseMessage> RedeemAsync(AuthEndpointTestHost host, string code, string redirectUri) =>
        await host.Client.PostAsJsonAsync("/auth/handshake/session",
            new HandshakeSessionEndpoints.HandshakeSessionRequest(code, redirectUri));

    [TestMethod]
    public async Task AValidCode_YieldsTheBoundSessionId()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        var code = MintCode(host);

        var response = await RedeemAsync(host, code, RedirectUri);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessionId = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("sessionId").GetString();
        sessionId.Should().NotBeNullOrEmpty();

        // The redeemed id is a LIVE custody session (the same one the exchange seeded) — the
        // portal's cookie is not an orphan.
        host.Services.GetRequiredService<PortalSessionStore>().Get(sessionId!).Should().NotBeNull();
    }

    [TestMethod]
    public async Task AReplay_Is404_InvalidCode()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        var code = MintCode(host);

        (await RedeemAsync(host, code, RedirectUri)).StatusCode.Should().Be(HttpStatusCode.OK);

        var replay = await RedeemAsync(host, code, RedirectUri);

        replay.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await replay.Content.ReadAsStringAsync()).Should().Contain("invalid_code");
    }

    [TestMethod]
    public async Task AnOmittedCode_Is404_InvalidCode_Not500()
    {
        // The guard, not the store's null check: an omitted/null code must answer the taxonomy's
        // unknown-code shape instead of surfacing as a 500 (AC4's taxonomy is a 4xx everywhere).
        await using var host = await AuthEndpointTestHost.StartAsync();

        var response = await host.Client.PostAsJsonAsync(
            "/auth/handshake/session",
            new { redirectUri = RedirectUri });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_code");
    }

    [TestMethod]
    public async Task AUriMismatch_Is400()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        var code = MintCode(host);

        var response = await RedeemAsync(host, code, "http://attacker.test/auth/callback");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("redirect_uri_mismatch");
    }

    [TestMethod]
    public async Task ABlankRedirectUri_Is400()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        var code = MintCode(host);

        var response = await RedeemAsync(host, code, string.Empty);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("redirect_uri_required");
    }

    [TestMethod]
    public async Task AnExpiredCode_Is400_CodeExpired()
    {
        // The TTL case the hosted path cannot reach without sleeping: the store and the endpoint
        // run directly over a controllable clock (the SessionEndpointTests/RedeemEndpointTests
        // direct-invocation pattern).
        var clock = new FakeTimeProvider();
        var options = EndpointTestSupport.OptionsFor(codeTtl: TimeSpan.FromSeconds(60));
        var sessions = new PortalSessionStore(clock, options);
        var codes = new OneTimeCodeStore(clock, options);
        var sessionId = sessions.Create(AccessToken, RefreshToken, SessionEndpointTests.JwtWithClaims("Dev School", ["teacher"]), 3600);
        var code = codes.Create(RedirectUri, sessionId);

        clock.Now = clock.Now.AddSeconds(61);

        var (status, body) = await EndpointTestSupport.ExecuteAsync(
            HandshakeSessionEndpoints.Redeem(new HandshakeSessionEndpoints.HandshakeSessionRequest(code, RedirectUri), codes));

        status.Should().Be(400);
        body.Should().Contain("code_expired");
    }

    [TestMethod]
    public async Task ABootstrapCode_IsNotRedeemableThere_NamespaceDisjoint()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        var sessionId = host.Services.GetRequiredService<PortalSessionStore>().Create(
            AccessToken, RefreshToken, SessionEndpointTests.JwtWithClaims("Dev School", ["teacher"]), 3600);
        var bootstrapCode = new BootstrapCodeStore(
            host.Services.GetRequiredService<TimeProvider>(),
            Microsoft.Extensions.Options.Options.Create(new AuthServiceOptions { OneTimeCodeTtl = TimeSpan.FromSeconds(60) }))
            .Create("http://localhost:5700/bootstrap", sessionId);

        var response = await RedeemAsync(host, bootstrapCode, "http://localhost:5700/bootstrap");

        // A bootstrap code simply does not exist in the handshake store's namespace — the same
        // 404 a replay or a forged code gets. The inverse holds too: /auth/bootstrap/redeem never
        // sees a handshake code (the store instances are separate by construction).
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_code");
    }

    [TestMethod]
    public async Task TheResponse_CarriesNoToken()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        var code = MintCode(host);

        var body = await (await RedeemAsync(host, code, RedirectUri)).Content.ReadAsStringAsync();

        // AC11, VALUE-based: the fixture's token strings must never appear in the response.
        body.Should().NotContain(AccessToken);
        body.Should().NotContain(RefreshToken);
    }
}
