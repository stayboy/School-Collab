using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Auth.Endpoints;
using SchoolCollab.Auth.Options;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// The D16 passkey relying-party flow (round B pass B6): the bootstrap code's single-use / TTL /
/// URI-binding / allowlist rules, the D6 continuation, and — the round's AC12 axis — that neither
/// a token nor an authorization code ever reaches the portal.
/// <para>
/// The two ceremony endpoints are driven <b>directly</b> (their delegates take the
/// <see cref="HttpContext"/> and their dependencies explicitly), because the interesting states —
/// a ceremony cookie whose <c>app_redirect_uri</c> is missing, withdrawn from the allowlist, or
/// present — cannot be produced by a hermetic HTTP request (the real flow needs a live Keycloak
/// WebAuthn ceremony). The cookie is therefore a genuine <see cref="AuthenticationTicket"/> with
/// crafted properties, and an <see cref="IAuthenticationService"/> mock stands in for the handler
/// that would have deserialized it. The HTTP surface (route mapping, status codes, raw response
/// bodies) is covered through the REAL Kestrel pipeline of <see cref="AuthEndpointTestHost"/>.
/// </para>
/// </summary>
[TestClass]
public class PasskeyBootstrapTests
{
    /// <summary>The Blazor callback the test host's <c>Auth:AppCallbackPrefixes</c> allows — the
    /// exact shape B1's challenge handler and B4's <c>BuildCallbackUri</c> mint.</summary>
    private const string AppCallback = "http://localhost:5300/signin-handshake";

    /// <summary>A second allowlisted callback, used to prove matching is per-entry, not
    /// per-non-empty-list.</summary>
    private const string OtherAppCallback = "https://localhost:7400/signin-handshake";

    /// <summary>The portal's bootstrap redemption URI (a distinct origin from every app callback,
    /// so a sign-in targeting it is the portal's own — D16 issues no continuation for it).</summary>
    private const string PortalBootstrapUrl = "http://localhost:9000/bootstrap";

    /// <summary>A redirect target that is NOT on the allowlist — the attacker URI
    /// (<c>portal/login?return_uri=…</c>) the B5b allowlist exists to defeat.</summary>
    private const string AttackerCallback = "http://evil.example/signin-handshake";

    private const string CustodySessionId = "9f2c8a1e5b34d607ef82a1c39d45b670";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static AppCallbackAllowlist Allowlist()
        => new($"{AppCallback};{OtherAppCallback};{PortalBootstrapUrl}");

    private static AuthServiceOptions OptionsFixture() => new()
    {
        Keycloak = new AuthServiceOptions.KeycloakOptions
        {
            Authority = "http://keycloak:8080/realms/school-collab",
            ClientId = "school-collab-client",
            ClientSecret = "a-client-secret",
            ServiceAccountClientSecret = "a-service-account-secret",
        },
        AppCallbackPrefixes = $"{AppCallback};{OtherAppCallback};{PortalBootstrapUrl}",
    };

    private static IConfiguration ConfigurationFixture(string? bootstrapRedirectUrl = PortalBootstrapUrl)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Portal:BootstrapRedirectUrl"] = bootstrapRedirectUrl,
            })
            .Build();

    private static OneTimeCodeStore HandshakeCodes(FakeTimeProvider clock)
        => new(clock, Microsoft.Extensions.Options.Options.Create(OptionsFixture()));

    private static BootstrapCodeStore BootstrapCodes(FakeTimeProvider clock)
        => new(clock, Microsoft.Extensions.Options.Options.Create(OptionsFixture()));

    /// <summary>A three-segment JWT carrying <paramref name="payloadJson"/> as its payload — the
    /// shape the endpoint's <c>exp</c> read (and round A's claim reads) expect. The signature is
    /// irrelevant here: the token never leaves the process and is never validated.</summary>
    private static string FakeJwt(string payloadJson)
        => "header." + Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".signature";

    private static string AccessToken(FakeTimeProvider clock, int lifetimeSeconds = 1800)
    {
        var exp = clock.Now.AddSeconds(lifetimeSeconds).ToUnixTimeSeconds();
        return FakeJwt($$"""{"exp":{{exp}},"tenant_id":"tenant","teacher_id":"teacher"}""");
    }

    /// <summary>The properties the OIDC handler's <c>SaveTokens</c> channel writes onto the
    /// signed-in cookie, with the ceremony's persisted <paramref name="persistedAppRedirectUri"/>
    /// item (absent when the challenge never set one — the state a tampered callback produces).</summary>
    private static AuthenticationProperties CeremonyProperties(
        FakeTimeProvider clock,
        string? persistedAppRedirectUri,
        bool withTokens = true)
    {
        var properties = new AuthenticationProperties();

        if (persistedAppRedirectUri is not null)
        {
            properties.Items[PasskeyEndpoints.AppRedirectUriItemName] = persistedAppRedirectUri;
        }

        if (withTokens)
        {
            properties.Items[".Token.access_token"] = AccessToken(clock);
            properties.Items[".Token.refresh_token"] = "seeded_refresh_token";
            properties.Items[".Token.id_token"] = FakeJwt("""{"tenant_id":"tenant"}""");
        }

        return properties;
    }

    private static (HttpContext Context, Mock<IAuthenticationService> Authentication) Context(
        AuthenticationProperties? ceremonyProperties,
        string? query = null)
    {
        var authentication = new Mock<IAuthenticationService>();
        authentication
            .Setup(s => s.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string?>()))
            .ReturnsAsync(ceremonyProperties is null
                ? AuthenticateResult.NoResult()
                : AuthenticateResult.Success(new AuthenticationTicket(
                    new System.Security.Claims.ClaimsPrincipal(
                        new System.Security.Claims.ClaimsIdentity("Cookies")),
                    ceremonyProperties,
                    CookieAuthenticationDefaults.AuthenticationScheme)));

        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection()
            .AddOptions()
            .AddLogging()
            .AddSingleton(authentication.Object)
            .BuildServiceProvider();

        if (query is not null)
        {
            context.Request.QueryString = new QueryString(query);
        }

        return (context, authentication);
    }

    // ── GET /auth/passkey/login ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task Login_BlankAppRedirectUri_Is400_AndNeverChallenges()
    {
        var (context, authentication) = Context(ceremonyProperties: null);

        var result = await PasskeyEndpoints.Login(context, Allowlist());
        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        authentication.Verify(
            s => s.ChallengeAsync(It.IsAny<HttpContext>(), It.IsAny<string?>(), It.IsAny<AuthenticationProperties?>()),
            Times.Never,
            "a blank app_redirect_uri must be refused before any ceremony starts.");
    }

    [TestMethod]
    public async Task Login_NonAllowlistedAppRedirectUri_Is400_AndNeverChallenges()
    {
        var (context, authentication) = Context(ceremonyProperties: null,
            query: $"?app_redirect_uri={Uri.EscapeDataString(AttackerCallback)}");

        var result = await PasskeyEndpoints.Login(context, Allowlist());
        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        context.Response.Headers.Location.Should().BeEmpty(
            "the attack the allowlist defeats is exactly this: an outbound redirect carrying a code to an attacker URI.");
        authentication.Verify(
            s => s.ChallengeAsync(It.IsAny<HttpContext>(), It.IsAny<string?>(), It.IsAny<AuthenticationProperties?>()),
            Times.Never,
            "no ceremony may be started for a target that could never legally receive a code.");
    }

    [TestMethod]
    public async Task Login_AllowlistedAppRedirectUri_ChallengesTheOidcSchemeWithThePersistedPendingState()
    {
        var (context, authentication) = Context(ceremonyProperties: null,
            query: $"?app_redirect_uri={Uri.EscapeDataString(AppCallback)}");

        AuthenticationProperties? challenged = null;
        string? challengedScheme = null;
        authentication
            .Setup(s => s.ChallengeAsync(It.IsAny<HttpContext>(), It.IsAny<string?>(), It.IsAny<AuthenticationProperties?>()))
            .Callback<HttpContext, string?, AuthenticationProperties?>((_, scheme, properties) =>
            {
                challengedScheme = scheme;
                challenged = properties;
            })
            .Returns(Task.CompletedTask);

        await PasskeyEndpoints.Login(context, Allowlist());

        // The OIDC scheme is named EXPLICITLY: the host's default challenge scheme is B1's
        // login-UI policy scheme, and the auth service is the relying party on this path in both
        // flag states — routing its own passkey challenge to the portal's login page would loop.
        challengedScheme.Should().Be(OpenIdConnectDefaults.AuthenticationScheme);

        challenged.Should().NotBeNull();
        challenged!.Items.Should().ContainKey(PasskeyEndpoints.AppRedirectUriItemName);
        challenged.Items[PasskeyEndpoints.AppRedirectUriItemName].Should().Be(AppCallback,
            "the validated target must travel with the ceremony (the correlation cookie's protected state) — not be re-supplied at the callback.");
        challenged.RedirectUri.Should().Be(PasskeyEndpoints.CompletePath,
            "the handler must come back to /complete after /signin-oidc, never re-enter /login (which would re-challenge).");
    }

    // ── GET /auth/passkey/complete ───────────────────────────────────────────────────

    [TestMethod]
    public async Task Complete_WithoutACeremonyCookie_Is401_AndRedirectsNowhere()
    {
        var clock = new FakeTimeProvider();
        var (context, _) = Context(ceremonyProperties: null, query: $"?app_redirect_uri={Uri.EscapeDataString(AppCallback)}");

        var result = await PasskeyEndpoints.Complete(
            context, Allowlist(), SessionStore(clock), HandshakeCodes(clock), BootstrapCodes(clock),
            ConfigurationFixture(), clock);
        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized,
            "no cookie means no ceremony was completed — a query parameter alone never signs anyone in.");
        context.Response.Headers.Location.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Complete_FreshQueryParameterIsNeverTrusted_Is400()
    {
        var clock = new FakeTimeProvider();
        // A COMPLETE ceremony (valid token set) that never persisted an app_redirect_uri, plus an
        // allowlisted callback supplied as a fresh query parameter. If /complete read the query
        // parameter this request would succeed and mint a code — it must not.
        var (context, _) = Context(
            CeremonyProperties(clock, persistedAppRedirectUri: null),
            query: $"?app_redirect_uri={Uri.EscapeDataString(AppCallback)}");

        var result = await PasskeyEndpoints.Complete(
            context, Allowlist(), SessionStore(clock), HandshakeCodes(clock), BootstrapCodes(clock),
            ConfigurationFixture(), clock);
        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        context.Response.Headers.Location.Should().BeEmpty(
            "the persisted value decides where a code points; a parameter a caller can rewrite at the callback must never be consulted.");
    }

    [TestMethod]
    public async Task Complete_PersistedTargetWithdrawnFromTheAllowlist_Is400_AndIssuesNoCode()
    {
        var clock = new FakeTimeProvider();
        var (context, _) = Context(CeremonyProperties(clock, persistedAppRedirectUri: AttackerCallback));

        var result = await PasskeyEndpoints.Complete(
            context, Allowlist(), SessionStore(clock), HandshakeCodes(clock), BootstrapCodes(clock),
            ConfigurationFixture(), clock);
        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest,
            "the persisted value is RE-VALIDATED at completion: a target that was legal at /login but is not now must not receive a code.");
        context.Response.Headers.Location.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Complete_WithoutAUsableTokenSet_Is502_AndIssuesNoCode()
    {
        var clock = new FakeTimeProvider();
        var (context, _) = Context(CeremonyProperties(clock, AppCallback, withTokens: false));

        var result = await PasskeyEndpoints.Complete(
            context, Allowlist(), SessionStore(clock), HandshakeCodes(clock), BootstrapCodes(clock),
            ConfigurationFixture(), clock);
        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status502BadGateway,
            "a session without a refresh token could never be refreshed or revoked, so it must not be created at all.");
        context.Response.Headers.Location.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Complete_PersistedAllowlistedTarget_RedirectsWithABootstrapCodeCarryingTheD6Continuation()
    {
        var clock = new FakeTimeProvider();
        var sessions = SessionStore(clock);
        var codes = HandshakeCodes(clock);
        var bootstrapCodes = BootstrapCodes(clock);
        var (context, _) = Context(CeremonyProperties(clock, AppCallback));

        var result = await PasskeyEndpoints.Complete(
            context, Allowlist(), sessions, codes, bootstrapCodes, ConfigurationFixture(), clock);
        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
        var location = context.Response.Headers.Location.ToString();
        location.Should().StartWith(PortalBootstrapUrl + "?code=",
            "the browser is handed the bootstrap code only — never a token, never an authorization code (AC12).");

        var bootstrapCode = location[(location.IndexOf("code=", StringComparison.Ordinal) + "code=".Length)..];

        var redemption = bootstrapCodes.Redeem(bootstrapCode, PortalBootstrapUrl);
        redemption.Status.Should().Be(RedeemStatus.Success);
        redemption.SessionId.Should().NotBeNullOrWhiteSpace();
        sessions.Get(redemption.SessionId!).Should().NotBeNull("the ceremony's token set must be in custody under that id.");
        redemption.Continuation.Should().NotBeNull(
            "a sign-in that began at a Blazor page carries the D16 continuation — it is never suppressed.");
        redemption.Continuation!.AppRedirectUri.Should().Be(AppCallback);

        // The continuation code is a REAL D6 handshake code: the app redeems it at /auth/redeem
        // with its own callback for the claim set.
        codes.Redeem(redemption.Continuation.AppCode, AppCallback).Status.Should().Be(RedeemStatus.Success);
    }

    [TestMethod]
    public async Task Complete_PortalOwnSignIn_OmitsTheD6Continuation()
    {
        var clock = new FakeTimeProvider();
        var bootstrapCodes = BootstrapCodes(clock);
        var (context, _) = Context(CeremonyProperties(clock, PortalBootstrapUrl));

        var result = await PasskeyEndpoints.Complete(
            context, Allowlist(), SessionStore(clock), HandshakeCodes(clock), bootstrapCodes,
            ConfigurationFixture(), clock);
        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
        var location = context.Response.Headers.Location.ToString();
        var bootstrapCode = location[(location.IndexOf("code=", StringComparison.Ordinal) + "code=".Length)..];

        var redemption = bootstrapCodes.Redeem(bootstrapCode, PortalBootstrapUrl);
        redemption.Status.Should().Be(RedeemStatus.Success);
        redemption.Continuation.Should().BeNull(
            "a sign-in that began at the portal itself needs only its session id (D16: the app_redirect_uri/app_code pair is absent otherwise).");
    }

    [TestMethod]
    public async Task Complete_WithoutAConfiguredBootstrapUrl_FailsClosed_AndIssuesNoCode()
    {
        var clock = new FakeTimeProvider();
        var (context, _) = Context(CeremonyProperties(clock, AppCallback));

        var result = await PasskeyEndpoints.Complete(
            context, Allowlist(), SessionStore(clock), HandshakeCodes(clock), BootstrapCodes(clock),
            ConfigurationFixture(bootstrapRedirectUrl: null), clock);
        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError,
            "an unconfigured portal redirect target must fail loudly rather than emit a blank or relative redirect.");
        context.Response.Headers.Location.Should().BeEmpty();
    }

    // ── POST /auth/bootstrap/redeem (direct) ─────────────────────────────────────────

    [TestMethod]
    public async Task BootstrapRedeem_BlankRedirectUri_Is400_AndNonAllowlistedUri_Is400()
    {
        var clock = new FakeTimeProvider();
        var store = BootstrapCodes(clock);

        var blank = await ExecuteStatusAsync(PasskeyEndpoints.BootstrapRedeem(
            new PasskeyEndpoints.BootstrapRedeemRequest("a-code", "  "), Allowlist(), store));
        blank.Should().Be(StatusCodes.Status400BadRequest);

        var attacker = await ExecuteStatusAsync(PasskeyEndpoints.BootstrapRedeem(
            new PasskeyEndpoints.BootstrapRedeemRequest("a-code", AttackerCallback), Allowlist(), store));
        attacker.Should().Be(StatusCodes.Status400BadRequest,
            "the redemption is allowlist-bound as well as URI-bound.");
    }

    [TestMethod]
    public async Task BootstrapRedeem_UnknownCode_Is404_AndReplayOrMismatchIsRejected()
    {
        var clock = new FakeTimeProvider();
        var store = BootstrapCodes(clock);
        var sessionId = CustodySessionId;

        var unknown = await ExecuteStatusAsync(PasskeyEndpoints.BootstrapRedeem(
            new PasskeyEndpoints.BootstrapRedeemRequest("never-issued", PortalBootstrapUrl), Allowlist(), store));
        unknown.Should().Be(StatusCodes.Status404NotFound);

        var code = store.Create(PortalBootstrapUrl, sessionId);

        var mismatch = await ExecuteStatusAsync(PasskeyEndpoints.BootstrapRedeem(
            new PasskeyEndpoints.BootstrapRedeemRequest(code, AppCallback), Allowlist(), store));
        mismatch.Should().Be(StatusCodes.Status400BadRequest, "a code may only be redeemed at the URI it was bound to.");

        var replay = await ExecuteStatusAsync(PasskeyEndpoints.BootstrapRedeem(
            new PasskeyEndpoints.BootstrapRedeemRequest(code, PortalBootstrapUrl), Allowlist(), store));
        replay.Should().Be(StatusCodes.Status404NotFound,
            "a mismatched attempt consumes the code (round A's store semantics), so nothing is left to redeem.");
    }

    // ── BootstrapCodeStore: single-use / TTL / binding / continuation ─────────────────

    [TestMethod]
    public void BootstrapCodeStore_IsSingleUse_BoundToItsUri_AndTtlLimited()
    {
        var clock = new FakeTimeProvider();
        var store = BootstrapCodes(clock);

        var code = store.Create(PortalBootstrapUrl, CustodySessionId);

        store.Redeem(code, AppCallback).Status.Should().Be(RedeemStatus.RedirectUriMismatch);
        store.Redeem(code, PortalBootstrapUrl).Status.Should().Be(RedeemStatus.InvalidCode,
            "the mismatch consumed the code.");

        var live = store.Create(PortalBootstrapUrl, CustodySessionId);
        store.Redeem(live, PortalBootstrapUrl).Status.Should().Be(RedeemStatus.Success);
        store.Redeem(live, PortalBootstrapUrl).Status.Should().Be(RedeemStatus.InvalidCode,
            "a bootstrap code is single-use.");

        var expiring = store.Create(PortalBootstrapUrl, CustodySessionId);
        clock.Now = clock.Now.Add(OptionsFixture().OneTimeCodeTtl).AddSeconds(1);
        store.Redeem(expiring, PortalBootstrapUrl).Status.Should().Be(RedeemStatus.Expired,
            "the bootstrap code inherits round A's short TTL.");
    }

    [TestMethod]
    public void BootstrapCodeStore_HandsOutTheContinuationOnlyOnce()
    {
        var clock = new FakeTimeProvider();
        var store = BootstrapCodes(clock);
        var continuation = new BootstrapContinuation(AppCallback, "d6-continuation-code");

        var code = store.Create(PortalBootstrapUrl, CustodySessionId, continuation);

        var redeemed = store.Redeem(code, PortalBootstrapUrl);
        redeemed.SessionId.Should().Be(CustodySessionId);
        redeemed.Continuation.Should().Be(continuation);

        var relayed = store.Create(PortalBootstrapUrl, CustodySessionId, continuation);
        store.Redeem(relayed, AppCallback);
        store.Redeem(relayed, PortalBootstrapUrl).Continuation.Should().BeNull(
            "a rejected attempt drops the continuation with the code — it can never be handed out twice.");
    }

    // ── HTTP surface through the REAL pipeline ───────────────────────────────────────

    [TestMethod]
    public async Task PasskeyRoutes_AreMappedAndFailClosed()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();

        (await host.Client.GetAsync($"{PasskeyEndpoints.LoginPath}"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "a blank app_redirect_uri is refused at the route.");
        (await host.Client.GetAsync($"{PasskeyEndpoints.LoginPath}?app_redirect_uri={Uri.EscapeDataString(AttackerCallback)}"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.Client.GetAsync(PasskeyEndpoints.CompletePath))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                "no ceremony cookie: there is no bootstrap code to hand out.");

        // The redemption's taxonomy, over the wire (the shape B2's portal client maps).
        var blank = await host.Client.PostAsync(
            "/auth/bootstrap/redeem",
            JsonContent.Create(new PasskeyEndpoints.BootstrapRedeemRequest("a-code", ""), options: WebJson));
        blank.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await blank.Content.ReadAsStringAsync()).Should().Contain("redirect_uri_required");

        var unknown = await host.Client.PostAsync(
            "/auth/bootstrap/redeem",
            JsonContent.Create(
                new PasskeyEndpoints.BootstrapRedeemRequest("never-issued", AppCallback), options: WebJson));
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await unknown.Content.ReadAsStringAsync()).Should().Contain("invalid_code");
    }

    [TestMethod]
    public async Task BootstrapRedeem_ResponseCarriesTheSessionIdAndContinuation_AndNoTokenOfAnyKind()
    {
        await using var host = await AuthEndpointTestHost.StartAsync();

        // The test host's allowlist has exactly one entry (the admin handshake callback), so the
        // bootstrap redemption URI this test uses IS that entry — the code is bound to it exactly
        // as the real one is bound to the portal's `/bootstrap` route.
        const string portalBootstrapUri = AppCallback;
        var store = host.Services.GetRequiredService<BootstrapCodeStore>();
        var code = store.Create(
            portalBootstrapUri,
            CustodySessionId,
            new BootstrapContinuation(OtherAppCallback, "seeded_d6_handshake_code"));

        var response = await host.Client.PostAsync(
            "/auth/bootstrap/redeem",
            JsonContent.Create(new PasskeyEndpoints.BootstrapRedeemRequest(code, portalBootstrapUri), options: WebJson));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();

        // AC12: the portal-bound body is scanned as RAW TEXT, so a token field added to the payload
        // later fails here instead of being ignored by deserialization.
        raw.Should().NotContain("seeded_access_token");
        raw.Should().NotContain("seeded_refresh_token");
        raw.Should().NotContain("seeded_id_token");
        raw.Should().NotContainAny("access_token", "refresh_token", "id_token");

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        root.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(new[] { "sessionId", "appRedirectUri", "appCode" },
                "the redemption body is exactly the D6 continuation payload the portal client pins — no field may be silently added.");
        root.GetProperty("sessionId").GetString().Should().Be(CustodySessionId);
        root.GetProperty("appRedirectUri").GetString().Should().Be(OtherAppCallback);
        root.GetProperty("appCode").GetString().Should().Be("seeded_d6_handshake_code",
            "the continuation code is the auth service's own D6 one-time code, minted for the app's callback.");
    }

    /// <summary>Executes an endpoint result against a bare context and reports its status — the
    /// shape the direct-invocation tests assert (the HTTP surface is covered separately through the
    /// real pipeline).</summary>
    private static async Task<int> ExecuteStatusAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection().AddOptions().AddLogging().BuildServiceProvider();
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        return context.Response.StatusCode;
    }

    private static PortalSessionStore SessionStore(FakeTimeProvider clock)
        => new(clock, Microsoft.Extensions.Options.Options.Create(OptionsFixture()));
}
