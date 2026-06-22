using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Admin.Tests.Unit;

[TestClass]
public class ProgramAuthFeatureFlagTests
{
    [TestMethod]
    public void AddAuthAndTenancy_registers_test_auth_handler_when_disable_oidc_is_enabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:FEATURE:DisableOIDCAuth"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddAuthAndTenancy(configuration);

        var provider = services.BuildServiceProvider();
        var featureFlags = provider.GetRequiredService<IFeatureFlagService>();
        var schemeProvider = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        Assert.IsTrue(featureFlags.IsEnabled("FEATURE:DisableOIDCAuth"));

        var scheme = schemeProvider.GetSchemeAsync(TestAuthExtensions.TestAuthScheme).GetAwaiter().GetResult();
        Assert.IsNotNull(scheme);
        Assert.AreEqual(TestAuthExtensions.TestAuthScheme, scheme.Name);
    }

    [TestMethod]
    public void AddAuthAndTenancy_registers_oidc_and_cookie_schemes_when_disable_oidc_is_disabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:FEATURE:DisableOIDCAuth"] = "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddAuthAndTenancy(configuration);

        var provider = services.BuildServiceProvider();
        var featureFlags = provider.GetRequiredService<IFeatureFlagService>();
        var schemeProvider = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        Assert.IsFalse(featureFlags.IsEnabled("FEATURE:DisableOIDCAuth"));

        var cookieScheme = schemeProvider.GetSchemeAsync("Cookies").GetAwaiter().GetResult();
        var oidcScheme = schemeProvider.GetSchemeAsync("OpenIdConnect").GetAwaiter().GetResult();

        Assert.IsNotNull(cookieScheme);
        Assert.IsNotNull(oidcScheme);
    }
}
