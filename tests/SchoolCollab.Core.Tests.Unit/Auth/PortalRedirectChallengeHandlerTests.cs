using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Auth;

namespace SchoolCollab.Core.Tests.Unit.Auth;

/// <summary>
/// Round-B pass B1 challenge-path tests (design P1-2, spec D4/D5/D6). They drive the real
/// registration — <see cref="AuthTenancyExtensions.AddAuthAndTenancy"/> plus the policy scheme —
/// and issue the challenge through <see cref="IAuthenticationService"/>, so they pin both the
/// interception (the policy scheme really does reach the portal handler) and the redirect shape
/// the portal consumes.
/// </summary>
[TestClass]
public class PortalRedirectChallengeHandlerTests
{
    private const string PortalLoginUrl = "https://localhost:5600/login";

    private static ServiceProvider Build(bool disableKeycloakLoginUi, string? portalLoginUrl)
    {
        var settings = new Dictionary<string, string?>
        {
            ["FeatureFlags:FEATURE:DisableOIDCAuth"] = "false",
            ["FeatureFlags:FEATURE:DisableKeycloakLoginUi"] = disableKeycloakLoginUi ? "true" : "false",
            ["Auth:Keycloak:Authority"] = "http://localhost:8080/realms/school-collab",
            ["Auth:Keycloak:ClientId"] = "school-collab-client",
            ["Auth:Keycloak:ClientSecret"] = "dev-only-school-collab-client-secret",
        };

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

    /// <summary>
    /// Issues an unauthenticated-gated-page challenge (no explicit scheme ⇒ the default
    /// challenge scheme, i.e. the policy scheme under test) on a request that looks like the
    /// admin host on port 7300.
    /// </summary>
    private static async Task<HttpContext> ChallengeAsync(
        ServiceProvider provider,
        string path = "/admin/students",
        string query = "?page=2")
    {
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost", 7300);
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);

        await provider
            .GetRequiredService<IAuthenticationService>()
            .ChallengeAsync(context, scheme: null, properties: null);

        return context;
    }

    private static string ExpectedHandshakeCallback(string returnUrl)
        => "https://localhost:7300"
            + PortalRedirectChallengeHandler.HandshakeCallbackPath
            + $"?{PortalRedirectChallengeHandler.ReturnUrlParameter}={Uri.EscapeDataString(returnUrl)}";

    [TestMethod]
    public async Task Challenge_WhenFlagOn_RedirectsToPortalLogin_CarryingHandshakeCallbackAndBlockedPath()
    {
        await using var provider = Build(disableKeycloakLoginUi: true, portalLoginUrl: PortalLoginUrl);

        var context = await ChallengeAsync(provider);

        context.Response.StatusCode.Should().Be(
            StatusCodes.Status302Found,
            "flag ON must redirect the browser to the prefab login form instead of Keycloak (D4/AC7)");

        context.Response.Headers.Location.ToString().Should().Be(
            PortalLoginUrl
            + $"?{PortalRedirectChallengeHandler.ReturnUriParameter}="
            + Uri.EscapeDataString(ExpectedHandshakeCallback("/admin/students?page=2")),
            "the portal needs the request-derived handshake callback (scheme+host+port so the cookie lands on the origin the browser used) and the blocked in-app path for post-handshake continuation (D6)");
    }

    [TestMethod]
    public async Task Challenge_AppendsReturnUriWithAmpersand_WhenPortalLoginUrlAlreadyCarriesAQuery()
    {
        await using var provider = Build(
            disableKeycloakLoginUi: true,
            portalLoginUrl: PortalLoginUrl + "?tenant=acme");

        var context = await ChallengeAsync(provider);

        context.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
        context.Response.Headers.Location.ToString().Should().Be(
            PortalLoginUrl
            + "?tenant=acme&"
            + $"{PortalRedirectChallengeHandler.ReturnUriParameter}="
            + Uri.EscapeDataString(ExpectedHandshakeCallback("/admin/students?page=2")),
            "an already-parameterised login URL must be extended, not corrupted with a second '?'");
    }

    [TestMethod]
    public async Task Challenge_WhenFlagOnButPortalLoginUrlMissing_FailsClosedWith401()
    {
        await using var provider = Build(disableKeycloakLoginUi: true, portalLoginUrl: null);

        var context = await ChallengeAsync(provider);

        context.Response.StatusCode.Should().Be(
            StatusCodes.Status401Unauthorized,
            "fail-closed: with no portal login URL configured the challenge must never emit a blank redirect and must never fall back to Keycloak's hosted page — the auth service receives the flag without the browser-facing login URL, so a Keycloak fallback would silently re-enable the UI the flag turned off (B1 hard requirement / criterion B-A)");

        context.Response.Headers.Location.ToString().Should().BeEmpty(
            "a 401 must not carry a redirect target");
    }

    [TestMethod]
    public async Task Challenge_WhenPortalLoginUrlBlank_FailsClosedWith401()
    {
        await using var provider = Build(disableKeycloakLoginUi: true, portalLoginUrl: "   ");

        var context = await ChallengeAsync(provider);

        context.Response.StatusCode.Should().Be(
            StatusCodes.Status401Unauthorized,
            "a whitespace-only login URL is a configuration error, not a valid redirect target");
    }
}
