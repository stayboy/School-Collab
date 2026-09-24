using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Auth;

namespace SchoolCollab.Core.Tests.Unit.Auth;

/// <summary>
/// ar-20 wiring tests: exercises the round's OWN registration
/// (<see cref="AuthTenancyExtensions.AddAuthAndTenancy"/>) instead of a test-local scheme,
/// so a renamed/dropped <c>Bearer</c> scheme, an unpinned <c>Audience</c>/<c>Authority</c>,
/// or an accidental HTTPS-metadata change fails here. The sibling
/// <c>AuthTenancyClaimMappingTests</c> registers its own <c>AddJwtBearer</c> and therefore
/// cannot observe any of this wiring.
/// </summary>
[TestClass]
public class AuthWiringTests
{
    private const string Authority = "http://localhost:8080/realms/school-collab";
    private const string ClientId = "school-collab-client";
    private const string ClientSecret = "dev-only-school-collab-client-secret";
    private const string PortalLoginUrl = "https://localhost:5600/login";

    private static ServiceProvider Build(
        bool disableOidc,
        bool disableKeycloakLoginUi = false,
        string? portalLoginUrl = null,
        bool requireOidcRelyingParty = false)
    {
        var settings = new Dictionary<string, string?>
        {
            // Canonical flag key shape (configuration.md §5): FeatureFlags:FEATURE:<key>.
            // A bare "FeatureFlags:DisableOIDCAuth" is NOT what the hosts set — getting this
            // wrong silently exercises the OIDC branch in the TestAuth test (caught here).
            ["FeatureFlags:FEATURE:DisableOIDCAuth"] = disableOidc ? "true" : "false",
            ["FeatureFlags:FEATURE:DisableKeycloakLoginUi"] = disableKeycloakLoginUi ? "true" : "false",
            ["Auth:Keycloak:Authority"] = Authority,
            ["Auth:Keycloak:ClientId"] = ClientId,
            ["Auth:Keycloak:ClientSecret"] = ClientSecret,
        };

        // B3 fans Auth:Portal:LoginUrl to the browser-facing hosts only; its absence is the
        // fail-closed case the challenge-handler tests pin.
        if (portalLoginUrl is not null)
        {
            settings["Auth:Portal:LoginUrl"] = portalLoginUrl;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDistributedMemoryCache();
        services.AddAuthAndTenancy(configuration, requireOidcRelyingParty);
        return services.BuildServiceProvider();
    }

    [TestMethod]
    public async Task RealAuthMode_RegistersBearer_WithPinnedAudienceAuthority_AndHttpMetadataOff()
    {
        await using var provider = Build(disableOidc: false);
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        (await schemes.GetSchemeAsync(AuthTenancyExtensions.BearerScheme))
            .Should().NotBeNull("real-auth mode must register the round's Bearer scheme name");

        var jwt = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(AuthTenancyExtensions.BearerScheme);

        jwt.Audience.Should().Be(
            ClientId,
            "Keycloak's realm audience mapper emits aud=clientId, so the handler must validate that audience (IDX10214 otherwise)");
        jwt.Authority.Should().Be(
            Authority,
            "the authority must come from Auth:Keycloak:Authority, not a hardcoded URL");
        jwt.RequireHttpsMetadata.Should().BeFalse(
            "the dev container is http-only; production must point Authority at an https URL instead");

        (await schemes.GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme))
            .Should().NotBeNull("the Blazor hosts keep the OIDC code flow");

        // D9 parity: both handlers must resolve roles from the flat `roles` claim the
        // realm's `User Realm Role` mapper emits (on the ID token and on the access
        // token), so [Authorize(Roles=...)] behaves identically over the cookie and
        // bearer paths. This asserts the WIRING and claim shape, not live gate
        // behaviour (the round's honesty note: live checks need a running Keycloak).
        jwt.TokenValidationParameters.RoleClaimType.Should().Be(
            ClaimTypes.Role,
            "the bearer path must pin RoleClaimType to the type the default inbound mapping produces — "
            + "the realm's flat `roles` claim is renamed to ClaimTypes.Role, so the literal name could never match (D9)");

        var oidc = provider
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);

        oidc.TokenValidationParameters.RoleClaimType.Should().Be(
            ClaimTypes.Role,
            "the OIDC cookie path must pin the same RoleClaimType as the bearer path (D9)");
    }

    [TestMethod]
    public async Task TestAuthMode_RegistersTestAuth_AndNoBearerScheme()
    {
        await using var provider = Build(disableOidc: true);
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        var registered = (await schemes.GetAllSchemesAsync()).Select(s => s.Name).ToArray();

        registered.Should().Contain(
            TestAuthExtensions.TestAuthScheme,
            $"the dev/CI posture must stay TestAuth so tests remain container-free (registered: {string.Join(", ", registered)})");

        registered.Should().NotContain(
            AuthTenancyExtensions.BearerScheme,
            "bearer is registered only in the OIDC branch");

        (await schemes.GetDefaultAuthenticateSchemeAsync())?.Name.Should().Be(
            TestAuthExtensions.TestAuthScheme,
            "TestAuth must be the default authenticate scheme in this mode");
    }

    [TestMethod]
    public async Task RealAuthMode_ChallengesThroughLoginUiPolicyScheme_ForwardingToOidc_WhenFlagOff()
    {
        await using var provider = Build(disableOidc: false);
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        (await schemes.GetDefaultChallengeSchemeAsync())?.Name.Should().Be(
            AuthTenancyExtensions.LoginUiChallengeScheme,
            "the OIDC branch must no longer challenge Keycloak directly — the policy scheme is the single challenge entry point that the flag re-targets (D5, design P1-2)");

        var policy = provider
            .GetRequiredService<IOptionsMonitor<PolicySchemeOptions>>()
            .Get(AuthTenancyExtensions.LoginUiChallengeScheme);

        policy.ForwardChallenge.Should().Be(
            OpenIdConnectDefaults.AuthenticationScheme,
            "with the flag OFF an unauthenticated gated page must still 302 to Keycloak's hosted login exactly as before the flag existed (AC7 / round-B criterion B-A)");

        (await schemes.GetSchemeAsync(PortalRedirectChallengeHandler.SchemeName))
            .Should().BeNull(
                "the portal-redirect handler is registered only with the flag ON, so the OFF pipeline stays the pre-flag pipeline");
    }

    [TestMethod]
    public async Task RealAuthMode_PolicyScheme_ForwardsToPortalRedirect_WhenFlagOn()
    {
        await using var provider = Build(
            disableOidc: false,
            disableKeycloakLoginUi: true,
            portalLoginUrl: PortalLoginUrl);

        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        (await schemes.GetDefaultChallengeSchemeAsync())?.Name.Should().Be(
            AuthTenancyExtensions.LoginUiChallengeScheme,
            "the challenge still enters through the policy scheme; only its target changes");

        var policy = provider
            .GetRequiredService<IOptionsMonitor<PolicySchemeOptions>>()
            .Get(AuthTenancyExtensions.LoginUiChallengeScheme);

        policy.ForwardChallenge.Should().Be(
            PortalRedirectChallengeHandler.SchemeName,
            "with the flag ON the challenge must land on the portal-redirect handler instead of Keycloak");

        (await schemes.GetSchemeAsync(PortalRedirectChallengeHandler.SchemeName))
            .Should().NotBeNull("the flag-ON challenge target must be a registered scheme");

        (await schemes.GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme))
            .Should().NotBeNull(
                "the OIDC handler stays registered with the flag ON — the passkey/bootstrap path (D16) and the auth service's own flow still use it");
    }

    [TestMethod]
    public async Task TestAuthMode_WithOidcRelyingPartyOptIn_RegistersCookieAndOidc_AndKeepsTestAuthDefault()
    {
        await using var provider = Build(disableOidc: true, requireOidcRelyingParty: true);
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        (await schemes.GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme))
            .Should().NotBeNull(
                "D16: the auth service IS the relying party — its /signin-oidc callback and its passkey bootstrap need the OIDC handler even with the dev bypass ON");

        // The load-bearing composition assertion: /complete reads the ticket BY SCHEME NAME
        // (PasskeyEndpoints.cs:164, CookieAuthenticationDefaults.AuthenticationScheme), which is also
        // AddOpenIdConnect's default SignInScheme. A misnamed .AddCookie("Other") leaves /complete
        // failing closed and must NOT satisfy this guard.
        (await schemes.GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme))
            .Should().NotBeNull(
                "the RP pipeline must register the DEFAULT cookie scheme by name — the scheme the OIDC handler signs into and /complete reads");

        // The interface projection of AuthenticationOptions.DefaultScheme
        // (DefaultAuthenticateScheme ?? DefaultScheme) — the same pin the sibling
        // TestAuthMode test uses.
        (await schemes.GetDefaultAuthenticateSchemeAsync())?.Name.Should().Be(
            TestAuthExtensions.TestAuthScheme,
            "the carve-out is additive: requests still authenticate as the test user by default (TestAuth stays the default scheme)");
    }

    [TestMethod]
    public async Task NonOptInHosts_DevPipelineIsSchemeForSchemeUnchanged()
    {
        await using var provider = Build(disableOidc: true);
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        var registered = (await schemes.GetAllSchemesAsync()).Select(s => s.Name).ToArray();

        (await schemes.GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme))
            .Should().BeNull(
                $"the RP pipeline is opt-in — hoisting its registration out of the parameter would give every consumer host an OIDC scheme in dev (registered: {string.Join(", ", registered)})");

        (await schemes.GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme))
            .Should().BeNull(
                $"no cookie scheme may appear in a non-opted-in consumer's dev pipeline (registered: {string.Join(", ", registered)})");

        // The interface projection of AuthenticationOptions.DefaultScheme
        // (DefaultAuthenticateScheme ?? DefaultScheme).
        (await schemes.GetDefaultAuthenticateSchemeAsync())?.Name.Should().Be(
            TestAuthExtensions.TestAuthScheme,
            "the five unedited call sites keep TestAuth as their default scheme — this is the direction that pins them");
    }

    [TestMethod]
    public async Task TestAuthMode_RegistersNoLoginUiPolicyScheme()
    {
        await using var provider = Build(
            disableOidc: true,
            disableKeycloakLoginUi: true,
            portalLoginUrl: PortalLoginUrl);

        var registered = (await provider
            .GetRequiredService<IAuthenticationSchemeProvider>()
            .GetAllSchemesAsync()).Select(s => s.Name).ToArray();

        registered.Should().NotContain(
            AuthTenancyExtensions.LoginUiChallengeScheme,
            $"the flag is a login-UI toggle for the OIDC branch only; DisableOIDCAuth must keep its own pipeline untouched (registered: {string.Join(", ", registered)})");

        registered.Should().NotContain(
            PortalRedirectChallengeHandler.SchemeName,
            "no portal-redirect challenge can exist when OIDC itself is disabled");
    }
}
