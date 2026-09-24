using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Auth.Endpoints;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// <c>POST /auth/exchange</c> coverage (pass 3c step 4): the success shape
/// (code + sessionId + expiry, token-free), the Direct-Grant failure taxonomy surfaced as
/// endpoint statuses (401 / 403 / 502), the defensive 502 for a "success" payload that omits a
/// token, the blank-redirect-URI guard, and the 429 rate-limit proof. Every request goes through
/// the REAL Kestrel pipeline of <see cref="AuthEndpointTestHost"/>; only the Direct-Grant
/// exchanger's network hop is stubbed (<see cref="StubHttpHandler"/>), so
/// <c>ExchangeStub.ReceivedRequests</c> also proves nothing ever touched the network.
/// Round B pass B5b adds the per-app allowlist: a redirect URI outside
/// <c>Auth:AppCallbackPrefixes</c> is rejected before a code exists, which is also why
/// <see cref="RedirectUri"/> is the allowlisted handshake callback the test host configures.
/// </summary>
[TestClass]
public class ExchangeEndpointTests
{
    /// <summary>The admin host's handshake callback — the exact shape B1's challenge handler and
    /// B4's <c>BuildCallbackUri</c> mint (query with the nested <c>ReturnUrl</c> ignored by the
    /// matcher), and the one the test host's <c>Auth:AppCallbackPrefixes</c> allows.</summary>
    private const string RedirectUri = "http://localhost:5300/signin-handshake";

    /// <summary>Well-formed Direct-Grant success. The token values are DISTINCTIVE so the
    /// no-token-in-body scan can assert their absence in the raw response.</summary>
    private const string SuccessTokenBody =
        """{"access_token":"seeded_access_token","refresh_token":"seeded_refresh_token","id_token":"seeded_id_token","expires_in":3600}""";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static void StubSuccess(AuthEndpointTestHost host) =>
        host.ExchangeStub.OnSendAsync = static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SuccessTokenBody, Encoding.UTF8, "application/json"),
        });

    private static void StubFailure(AuthEndpointTestHost host, HttpStatusCode status, string body) =>
        host.ExchangeStub.OnSendAsync = (_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });

    private static JsonContent Request(string? redirectUri = RedirectUri) =>
        JsonContent.Create(
            new ExchangeEndpoints.ExchangeRequest("dev-teacher", "dev-only-password", redirectUri!),
            options: WebJson);

    [TestMethod]
    public async Task Exchange_Success_ReturnsCodeAndSessionId_AndNoTokenInBody()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        StubSuccess(host);

        var response = await host.Client.PostAsync("/auth/exchange", Request());

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var code = doc.RootElement.GetProperty("code").GetString()!;
        var sessionId = doc.RootElement.GetProperty("sessionId").GetString()!;
        doc.RootElement.GetProperty("expiresInSeconds").GetInt32().Should().Be(3600);

        code.Should().NotBeNullOrWhiteSpace();
        sessionId.Should().NotBeNullOrWhiteSpace();
        sessionId.Should().HaveLength(64); // 256-bit opaque custody id (D12) — not a token

        // AC9: the success body is exactly { code, sessionId, expiresInSeconds }. Scanning the
        // RAW serialized JSON — rather than the typed DTO — means a token field added to the
        // payload later fails here instead of being silently ignored by deserialization.
        json.Should().NotContain("seeded_access_token");
        json.Should().NotContain("seeded_refresh_token");
        json.Should().NotContain("seeded_id_token");

        host.ExchangeStub.ReceivedRequests.Should().Be(1, "the stub — not the network — answered the exchange");
    }

    [TestMethod]
    public async Task Exchange_InvalidCredentials_Is401()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        StubFailure(host, HttpStatusCode.Unauthorized,
            """{"error":"invalid_grant","error_description":"Invalid user credentials"}""");

        var response = await host.Client.PostAsync("/auth/exchange", Request());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_credentials");
    }

    [TestMethod]
    public async Task Exchange_DisabledUser_Is403()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        // Keycloak's canonical accountDisabledMessage (verified against the 26.4.7 message
        // sources in pass 3a's fix): a disabled user must be surfaced as 403, not as a generic
        // credential failure — the whole point of the pass-3a P1 fix.
        StubFailure(host, HttpStatusCode.Unauthorized,
            """{"error":"invalid_grant","error_description":"Account is disabled, contact your administrator."}""");

        var response = await host.Client.PostAsync("/auth/exchange", Request());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("disabled_user");
    }

    [TestMethod]
    public async Task Exchange_Unreachable_Is502()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        StubFailure(host, HttpStatusCode.BadGateway, "gateway exploded");

        var response = await host.Client.PostAsync("/auth/exchange", Request());

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [TestMethod]
    public async Task Exchange_SuccessOmittingIdToken_Is502()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        host.ExchangeStub.OnSendAsync = static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"access_token":"seeded_access_token"}""", Encoding.UTF8, "application/json"),
        });

        var response = await host.Client.PostAsync("/auth/exchange", Request());

        // A 200 that omits id_token/refresh_token is a malformed endpoint (comment in
        // ExchangeEndpoints): it must not seed a custody entry that redemption or logout could
        // never use, so it surfaces as unreachable rather than success.
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [TestMethod]
    public async Task Exchange_BlankRedirectUri_Is400_BeforeTouchingKeycloak()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        // No stub configured here: the default stub answers 502. The redirect-URI guard must
        // win BEFORE any exchange, so ReceivedRequests stays at zero even though the stub would
        // fail if it were called — proving the guard is first in the handler.
        var response = await host.Client.PostAsync("/auth/exchange", Request("   "));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.ExchangeStub.ReceivedRequests.Should().Be(0, "the redirect-URI guard runs before the exchange");
    }

    [TestMethod]
    public async Task Exchange_NonAllowlistedRedirectUri_IsRejected_WithNoCodeAndNoUpstreamCall()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        // The stub WOULD answer a code-minting success. The allowlist guard must therefore win
        // before the exchange: otherwise the crafted `portal/login?return_uri=attacker` link
        // mints an attacker-redeemable code carrying the victim's claim set (spec §14 / P1-3).
        StubSuccess(host);

        var response = await host.Client.PostAsync(
            "/auth/exchange",
            Request("http://attacker.example/signin-handshake"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("redirect_uri_not_allowed");

        // No code in the rejection body — asserted on the raw JSON (a typed-DTO assertion would
        // silently ignore a code field added later).
        using (var doc = JsonDocument.Parse(json))
        {
            doc.RootElement.TryGetProperty("code", out _).Should().BeFalse(
                "the rejection body carries no one-time code — rejecting is pointless if a code leaks first.");
        }

        json.Should().NotContain("seeded_access_token");
        json.Should().NotContain("seeded_refresh_token");
        json.Should().NotContain("seeded_id_token");

        host.ExchangeStub.ReceivedRequests.Should().Be(0,
            "the allowlist is enforced before the credential exchange: a disallowed target must not even cost a Keycloak round trip.");
    }

    [TestMethod]
    public async Task Exchange_AllowlistedRedirectUri_WithNestedReturnUrlQuery_IssuesTheCode()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        StubSuccess(host);

        // The exact spelling the Blazor hosts mint: `…/signin-handshake?ReturnUrl=<escaped>`. The
        // nested query must not break the allowlist comparison, or the flag-ON handshake could
        // never complete (B1/B4's callback shape — cross-pass wire pin).
        var response = await host.Client.PostAsync(
            "/auth/exchange",
            Request($"{RedirectUri}?ReturnUrl=%2Fstudents%3Fpage%3D2"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("code").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public async Task Exchange_FourthCallWithinWindow_Is429()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        StubSuccess(host);

        var first = await host.Client.PostAsync("/auth/exchange", Request());
        var second = await host.Client.PostAsync("/auth/exchange", Request());
        var third = await host.Client.PostAsync("/auth/exchange", Request());
        var fourth = await host.Client.PostAsync("/auth/exchange", Request());

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        third.StatusCode.Should().Be(HttpStatusCode.OK);
        fourth.StatusCode.Should().Be((HttpStatusCode)429);

        // DISCRIMINATION (why this cannot pass without the limiter): the host config permits
        // exactly THREE requests per fixed window per partition (the connection's loopback
        // address) with a 429 rejection status. The limiter middleware counts requests BEFORE
        // the endpoint handler runs, so without `AddAuthRateLimiting` + `RequireRateLimiting`
        // on this route the fourth call would reach the handler and return 200 like its
        // predecessors — only the limiter's own rejection path can emit 429.
        host.ExchangeStub.ReceivedRequests.Should().BeGreaterThan(0, "requests really reached the pipeline");
    }
}
