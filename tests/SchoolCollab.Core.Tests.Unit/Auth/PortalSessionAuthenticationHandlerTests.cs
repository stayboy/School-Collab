using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Auth;

namespace SchoolCollab.Core.Tests.Unit.Auth;

/// <summary>
/// The portal-session authentication scheme's IN-PROCESS tests (spec D12/D15; round
/// <c>portal-session-adoption</c> D19/D1 — moved here with the handler from
/// <c>SchoolCollab.Auth</c>, where the real-custody host-level half stays): the opaque session id
/// the portal presents becomes a principal carrying the SAME claim set the OIDC paths produce (D9)
/// — including <see cref="ClaimTypes.Role"/> entries, so <c>RequireRole</c> resolves — and every
/// failure mode (no header, an unknown/expired session, a claim set the port refuses) fails closed
/// with <c>NoResult</c> and a bare <c>401</c> challenge, never a redirect and never a
/// partially-populated principal. The handler consults ONLY the claims port, so the tests drive it
/// through a stub port — the same contract the auth service's in-process adapter and the
/// Assignments API's remote reader both implement.
/// <para>
/// These helpers are deliberately LOCAL to this assembly (no cross-assembly reuse of
/// <c>Auth.Tests.Unit</c>'s <c>BuildProvider</c>/<c>JwtWithClaims</c>/<c>FakeTimeProvider</c>/
/// <c>ScriptedHandler</c>): Core.Tests.Unit must not depend on the auth service's test fixtures.
/// </para>
/// </summary>
[TestClass]
public class PortalSessionAuthenticationHandlerTests
{
    private static readonly string TenantId = "00000000-0000-0000-0000-000000000002";
    private static readonly string TeacherId = "00000000-0000-0000-0000-000000000003";

    private static readonly PortalClaims FullClaims =
        new(TenantId, "Dev School", "School", TeacherId, ["user-admin", "platform-admin"]);

    // ── The handler against a stubbed claims port ─────────────────────────────────────────────

    [TestMethod]
    public async Task ValidSession_AuthenticatesWithTheFullClaimSet()
    {
        var port = new FakeClaimsReader { NextClaims = FullClaims };

        var (result, _) = await AuthenticateAsync(port, sessionId: "session-id-1");

        result.Succeeded.Should().BeTrue();
        port.Calls.Should().Be(1, "the handler consults the claims port exactly once per request");
        port.LastSessionId.Should().Be("session-id-1");
        var principal = result.Principal!;
        principal.Identity!.AuthenticationType.Should().Be(PortalSessionAuthenticationHandler.SchemeName);
        principal.Identity.IsAuthenticated.Should().BeTrue();
        principal.FindFirst(PortalClaims.TenantIdClaim)!.Value.Should().Be(TenantId);
        principal.FindFirst(PortalClaims.TenantNameClaim)!.Value.Should().Be("Dev School");
        principal.FindFirst(PortalClaims.TenantTypeClaim)!.Value.Should().Be("School");
        principal.FindFirst(PortalClaims.TeacherIdClaim)!.Value.Should().Be(TeacherId);

        // D9 parity: RequireRole/IsInRole must resolve on this path exactly as on cookie/bearer.
        principal.IsInRole("user-admin").Should().BeTrue();
        principal.IsInRole("platform-admin").Should().BeTrue();
        principal.IsInRole("some-other-role").Should().BeFalse();
    }

    [TestMethod]
    public async Task NoSessionHeader_IsNoResult_AndChallengesWith401()
    {
        var port = new FakeClaimsReader();

        var (result, context) = await AuthenticateAsync(port, sessionId: null);

        result.Succeeded.Should().BeFalse();
        result.None.Should().BeTrue();
        port.Calls.Should().Be(0, "an absent header never reaches the claims port");
        context.Response.StatusCode.Should().Be(401);
        context.Response.Headers.Location.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CustodyNull_IsNoResult()
    {
        // The port answers null for an unknown session, an expired one and a token payload without
        // the pinned claim contract alike — the three custody-refusal states share one fail-closed
        // answer (the auth-service adapter and the remote reader both produce null for them).
        var (result, _) = await AuthenticateAsync(new FakeClaimsReader(), sessionId: "unknown-or-dead");

        result.Succeeded.Should().BeFalse();
        result.None.Should().BeTrue();
    }

    [TestMethod]
    public async Task Challenge_IsABare401_EvenForASuccessfulScheme()
    {
        var port = new FakeClaimsReader { NextClaims = FullClaims };

        var (result, context) = await AuthenticateAsync(port, sessionId: "session-id-1");

        // The challenge pin is unconditional: a portal session cannot be redirected to a login UI,
        // so the gateway's ForwardChallenge stays this scheme's bare 401 in every state.
        result.Succeeded.Should().BeTrue();
        context.Response.StatusCode.Should().Be(401);
        context.Response.Headers.Location.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AuthenticatingAPortalCall_TouchesNothingButThePort()
    {
        // The D18 lifecycle (and its Keycloak round-trips) belongs to the session READ: the scheme
        // authenticates on every portal call and must stay a pure claims-port lookup. The stub port
        // models the auth service's in-process adapter over IAuthProvider.ReadClaims — genuinely
        // I/O-free — so a live session's principal comes back with zero transport work.
        var port = new FakeClaimsReader { NextClaims = FullClaims };

        var (result, _) = await AuthenticateAsync(port, sessionId: "session-id-1");

        result.Succeeded.Should().BeTrue();
        port.Calls.Should().Be(1);
        port.TouchedTransport.Should().BeFalse();
    }

    [TestMethod]
    public void TheClaimNameConstants_AreTheRealmMapperContract()
    {
        // The handler materializes the principal from these very names; a drift between the
        // shared-kernel constants and the realm mapper contract is what D9 forbids. Asserted in the
        // Core spelling, where the constants now live (D19/D1).
        PortalClaims.TenantIdClaim.Should().Be("tenant_id");
        PortalClaims.TenantNameClaim.Should().Be("tenant_name");
        PortalClaims.TenantTypeClaim.Should().Be("tenant_type");
        PortalClaims.TeacherIdClaim.Should().Be("teacher_id");
        PortalClaims.RolesClaim.Should().Be("roles");
    }

    // ── The gateway selector ───────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Selector_WithASessionHeader_PortalSessionWins_WhateverTheFallback()
    {
        var (context, _) = HttpContexts(sessionId: "session-id-1", disableOidcAuth: false);

        PortalSessionAuthenticationHandler.SelectAuthenticationScheme(context, fallbackScheme: "Bearer")
            .Should().Be(PortalSessionAuthenticationHandler.SchemeName);
    }

    [TestMethod]
    public void Selector_RealAuth_WithAPinnedFallback_ResolvesToTheFallback()
    {
        // THE AC2 half: a bearer-presenting (or otherwise non-portal) caller keeps its own scheme —
        // the pin restores today's authentication union for callers whose host default would
        // otherwise substitute the OIDC cookie.
        var (context, _) = HttpContexts(sessionId: null, disableOidcAuth: false);

        PortalSessionAuthenticationHandler.SelectAuthenticationScheme(context, fallbackScheme: "Bearer")
            .Should().Be("Bearer");
    }

    [TestMethod]
    public void Selector_DevFlagOn_TheHostsOwnSchemeWins_OverThePin()
    {
        // FEATURE:DisableOIDCAuth ON keeps today's dev posture (TestAuth auto-authenticates) even
        // where a fallback is pinned — the flag resolves per request from the request's config.
        var (context, _) = HttpContexts(sessionId: null, disableOidcAuth: true, defaultScheme: "TestAuth");

        PortalSessionAuthenticationHandler.SelectAuthenticationScheme(context, fallbackScheme: "Bearer")
            .Should().Be("TestAuth");
    }

    [TestMethod]
    public void Selector_WithoutAPin_TheHostDefaultResolves_AsBefore()
    {
        var (context, _) = HttpContexts(sessionId: null, disableOidcAuth: false, defaultAuthenticateScheme: "Cookies");

        PortalSessionAuthenticationHandler.SelectAuthenticationScheme(context)
            .Should().Be("Cookies", "a null fallback preserves the pre-adoption host-default resolution");
    }

    [TestMethod]
    public void Selector_WithAnUnparseableDevFlag_FailsClosedToThePinnedFallback()
    {
        // Absent/invalid ⇒ fail-closed real auth ⇒ the pin — an unparseable flag value can never
        // switch a principal onto a scheme the host did not register.
        var (context, _) = HttpContexts(sessionId: null, disableOidcAuth: false, flagValue: "not-a-bool");

        PortalSessionAuthenticationHandler.SelectAuthenticationScheme(context, fallbackScheme: "Bearer")
            .Should().Be("Bearer");
    }

    // ── Fixtures ───────────────────────────────────────────────────────────────────────────────

    /// <summary>A claims port whose answers are canned per test: <see cref="NextClaims"/> is
    /// returned once and then the port reverts to <c>null</c> (so a replay-looking second call is
    /// distinguishable), and every call is counted.</summary>
    private sealed class FakeClaimsReader : IPortalSessionClaimsReader
    {
        public PortalClaims? NextClaims { get; set; }

        public int Calls { get; private set; }

        public string? LastSessionId { get; private set; }

        public bool TouchedTransport { get; private set; }

        public ValueTask<PortalClaims?> ReadClaimsAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastSessionId = sessionId;
            var claims = NextClaims;
            NextClaims = null;
            return ValueTask.FromResult(claims);
        }
    }

    /// <summary>Drives the handler through the framework's own <see cref="IAuthenticationHandler"/>
    /// surface (initialize → authenticate → challenge), so neither the principal nor the 401 is
    /// asserted through a private path the framework does not use.</summary>
    private static async Task<(AuthenticateResult Result, DefaultHttpContext Context)> AuthenticateAsync(
        IPortalSessionClaimsReader claimsReader,
        string? sessionId)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = ServicesWith(claimsReader);
        if (sessionId is not null)
        {
            context.Request.Headers[PortalSessionAuthenticationHandler.SessionHeaderName] = sessionId;
        }

        var handler = new PortalSessionAuthenticationHandler(
            OptionsMonitor(),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            claimsReader);

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

    private static IServiceProvider ServicesWith(IPortalSessionClaimsReader claimsReader)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPortalSessionClaimsReader>(claimsReader);
        services.AddOptions();
        return services.BuildServiceProvider();
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

    /// <summary>A request context whose config and authentication options are set per test: the
    /// selector resolves the dev flag from the request's configuration and the host default from
    /// <see cref="AuthenticationOptions"/>, exactly as a real pipeline would supply them.</summary>
    private static (DefaultHttpContext Context, ServiceProvider Provider) HttpContexts(
        string? sessionId,
        bool disableOidcAuth,
        string? flagValue = null,
        string? defaultScheme = null,
        string? defaultAuthenticateScheme = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:FEATURE:DisableOIDCAuth"] = flagValue ?? (disableOidcAuth ? "true" : "false"),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions();
        services.Configure<AuthenticationOptions>(options =>
        {
            options.DefaultScheme = defaultScheme ?? "Bearer";
            options.DefaultAuthenticateScheme = defaultAuthenticateScheme ?? (defaultScheme ?? "Bearer");
        });
        var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = provider };
        if (sessionId is not null)
        {
            context.Request.Headers[PortalSessionAuthenticationHandler.SessionHeaderName] = sessionId;
        }

        return (context, provider);
    }
}
