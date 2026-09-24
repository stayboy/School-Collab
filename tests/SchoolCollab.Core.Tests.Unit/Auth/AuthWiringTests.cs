using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
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
        string? portalLoginUrl = null)
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
        services.AddAuthAndTenancy(configuration);
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
