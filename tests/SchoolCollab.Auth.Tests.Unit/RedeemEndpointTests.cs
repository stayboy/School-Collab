using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Auth.Endpoints;
using SchoolCollab.Auth.Options;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// <c>POST /auth/redeem</c> coverage (pass 3c step 4): the happy path returns the CLAIM SET
/// ONLY (AC9 — the guarantee pass 3b handed forward), the request contract carries no session id
/// (the defect the parent corrected in 3c), a replayed code is rejected, TTL expiry is honoured,
/// a redirect-URI mismatch is consumed as misuse, and unknown codes are 404. The happy and
/// pipeline cases run through the real Kestrel host with a synthetically-minted ID token; the
/// clock-dependent case (TTL expiry) invokes the endpoint directly under a fake TimeProvider,
/// because the host's code TTL is 20 seconds of real time.
/// ALSO HOSTS the shared <see cref="FakeTimeProvider"/> and <see cref="EndpointTestSupport"/>
/// helpers used by <see cref="SessionEndpointTests"/>'s clock-dependent case.
/// </summary>
[TestClass]
public class RedeemEndpointTests
{
    private const string RedirectUri = "http://localhost:5300/signin-oidc";

    /// <summary>The realm mappers' claim names (school-collab-realm.json <c>claim.name</c>
    /// values), carried by a SYNTHETIC UNSIGNED ID token. Round A's redeem path decodes only the
    /// JWT payload (documented in <see cref="RedeemEndpoints"/>), so a fixture matching the
    /// realm-mapper contract is valid here: it is a claim-SHAPE fixture (D9), NOT a claim of
    /// live parity — full signature/issuer/audience validation and live parity remain the
    /// round's cold-start/round-B checks.
    /// </summary>
    private static readonly string SyntheticIdToken = "header."
        + Convert.ToBase64String(Encoding.UTF8.GetBytes(
            """{"tenant_id":"00000000-0000-0000-0000-000000000002","tenant_name":"Dev School","tenant_type":"School","teacher_id":"00000000-0000-0000-0000-000000000003","roles":["user-admin"]}""")).TrimEnd('=')
        + ".signature";

    private static JsonContent Request(string code, string redirectUri = RedirectUri) =>
        JsonContent.Create(new RedeemEndpoints.RedeemRequest(code, redirectUri),
            options: new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>Seeds a live session plus a bound single-use code through the host's REAL
    /// stores, and returns the values needed for the no-token-in-body scan.</summary>
    private static async Task<(string Code, string AccessToken, string RefreshToken, string IdToken)> SeedRedemptionAsync(
        AuthEndpointTestHost host)
    {
        var sessions = host.Services.GetRequiredService<PortalSessionStore>();
        var codes = host.Services.GetRequiredService<OneTimeCodeStore>();
        const string access = "seeded_access_token";
        const string refresh = "seeded_refresh_token";
        var sessionId = sessions.Create(access, refresh, SyntheticIdToken, 3600);
        var code = codes.Create(RedirectUri, sessionId);
        return (code, access, refresh, SyntheticIdToken);
    }

    [TestMethod]
    public async Task Redeem_HappyPath_ReturnsTheClaimSetOnly_NoTokenInBody()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        var (code, access, refresh, idToken) = await SeedRedemptionAsync(host);

        // The calling app holds ONLY the code and its own redirect URI — no session id anywhere
        // (D6 / spec §5.2 step 5). Success therefore proves the code alone resolves the claims.
        var response = await host.Client.PostAsync("/auth/redeem", Request(code));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("tenantId").GetString().Should().Be("00000000-0000-0000-0000-000000000002");
        doc.RootElement.GetProperty("tenantName").GetString().Should().Be("Dev School");
        doc.RootElement.GetProperty("tenantType").GetString().Should().Be("School");
        doc.RootElement.GetProperty("teacherId").GetString().Should().Be("00000000-0000-0000-0000-000000000003");
        var roles = doc.RootElement.GetProperty("roles");
        roles.GetArrayLength().Should().Be(1);
        roles[0].GetString().Should().Be("user-admin");

        // AC9: no secret or token in ANY response body. Scan the RAW serialized JSON for every
        // seeded token value AND for token-shaped property names — typed deserialization alone
        // could never catch a token field the DTO does not model.
        json.Should().NotContain(access);
        json.Should().NotContain(refresh);
        json.Should().NotContain(idToken);
        json.Should().NotContain("access_token");
        json.Should().NotContain("refresh_token");
        json.Should().NotContain("id_token");
        // The redeem response is the CLAIM SET; the session id travels with the portal's own
        // signed cookie and must never appear in this body (P2-3). Same raw-scan protection as
        // the token names, so a SessionId field added to the response record fails this test.
        json.Should().NotContain("sessionId");
    }

    [TestMethod]
    public void RedeemRequest_ContractHasNoSessionIdField()
    {
        // The pass-3c contract defect the parent corrected: the redeemer is the calling Blazor
        // app, which receives ONLY the one-time code (D6, spec §5.2 steps 4-5), so the request
        // type must not expose a session field — the code entry carries the reference. Pinned
        // by reflection so a future SessionId field cannot creep back in.
        typeof(RedeemEndpoints.RedeemRequest).GetProperties().Should().HaveCount(2);
        typeof(RedeemEndpoints.RedeemRequest).GetProperty("SessionId").Should().BeNull();
    }

    [TestMethod]
    public async Task Redeem_ReplayOfAConsumedCode_Is404()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        var (code, _, _, _) = await SeedRedemptionAsync(host);

        var first = await host.Client.PostAsync("/auth/redeem", Request(code));
        var second = await host.Client.PostAsync("/auth/redeem", Request(code));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        // DISCRIMINATION: the second call can only see InvalidCode if the first redemption
        // REALLY removed the entry atomically — this fails if redemption leaves the entry or
        // returns success twice.
        second.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await second.Content.ReadAsStringAsync()).Should().Contain("invalid_code");
    }

    [TestMethod]
    public async Task Redeem_RedirectUriMismatch_ConsumesTheCodeAsMisuse()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        var (code, _, _, _) = await SeedRedemptionAsync(host);

        var mismatch = await host.Client.PostAsync("/auth/redeem", Request(code, "http://evil.example/callback"));
        // The TRUE holder then retries with the correct URI.
        var retry = await host.Client.PostAsync("/auth/redeem", Request(code));

        mismatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await mismatch.Content.ReadAsStringAsync()).Should().Contain("redirect_uri_mismatch");
        // The mismatched attempt was CONSUMED as misuse (D6 anti-probe posture), so even the
        // legitimate holder's retry is now rejected. Both statuses together prove
        // reject-AND-consume — the first alone would not.
        retry.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task Redeem_UnknownCode_Is404()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();
        var response = await host.Client.PostAsync("/auth/redeem", Request("deadbeefdeadbeef"));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_code");
    }

    [TestMethod]
    public async Task Redeem_TtlExpiredCode_Is400()
    {
        // Direct invocation under a controlling clock: through the host the code TTL is 20 real
        // seconds, which no test should sleep for. The store is built under the SAME fake clock
        // it uses internally, so expiry (code TTL 20 s) is deterministic and instant. The
        // session TTL defaults to 30 min, so the code really is the thing that expired.
        var clock = new FakeTimeProvider();
        var options = EndpointTestSupport.OptionsFor(codeTtl: TimeSpan.FromSeconds(20));
        var codes = new OneTimeCodeStore(clock, options);
        var sessions = new PortalSessionStore(clock, options);
        var sessionId = sessions.Create("seeded_access_token", "seeded_refresh_token", SyntheticIdToken, 3600);
        var code = codes.Create(RedirectUri, sessionId);

        clock.Now = clock.Now.AddSeconds(21);

        var (status, body) = await EndpointTestSupport.ExecuteAsync(
            RedeemEndpoints.Redeem(
                new RedeemEndpoints.RedeemRequest(code, RedirectUri), codes, sessions, new ClaimSetFactory()));

        status.Should().Be(400);
        body.Should().Contain("code_expired");
    }
}

/// <summary>Controllable clock for the TTL cases the host cannot reach without sleeping. The
/// stores use <c>TimeProvider.GetUtcNow()</c> exclusively, so overriding that one method fully
/// controls expiry.</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Shared construction/execution helpers for the direct-invocation endpoint tests.</summary>
internal static class EndpointTestSupport
{
    public static IOptions<AuthServiceOptions> OptionsFor(TimeSpan? codeTtl = null, TimeSpan? sessionTtl = null) =>
        new OptionsWrapper<AuthServiceOptions>(new AuthServiceOptions
        {
            OneTimeCodeTtl = codeTtl ?? TimeSpan.FromSeconds(60),
            PortalSessionTtl = sessionTtl ?? TimeSpan.FromMinutes(30),
        });

    /// <summary>Executes an <see cref="IResult"/> against a bare <see cref="DefaultHttpContext"/>
    /// and returns the status plus the serialized body — used to run the endpoint methods
    /// directly (no HTTP host) when a controllable clock is required. A minimal provider is
    /// wired so the Json result serializers can resolve their options (a bare context has none).</summary>
    public static async Task<(int Status, string Body)> ExecuteAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>(
            Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Http.Json.JsonOptions()));
        services.AddSingleton<ILoggerFactory>((ILoggerFactory)Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        context.RequestServices = services.BuildServiceProvider();
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, body);
    }
}
