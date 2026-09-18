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

    private static ServiceProvider Build(bool disableOidc)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Canonical flag key shape (configuration.md §5): FeatureFlags:FEATURE:<key>.
                // A bare "FeatureFlags:DisableOIDCAuth" is NOT what the hosts set — getting this
                // wrong silently exercises the OIDC branch in the TestAuth test (caught here).
                ["FeatureFlags:FEATURE:DisableOIDCAuth"] = disableOidc ? "true" : "false",
                ["Auth:Keycloak:Authority"] = Authority,
                ["Auth:Keycloak:ClientId"] = ClientId,
                ["Auth:Keycloak:ClientSecret"] = ClientSecret,
            })
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
}
