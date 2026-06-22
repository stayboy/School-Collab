using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using FluentAssertions;
using SchoolCollab.Core.Auth;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Core.Tests.Unit.Auth;

[TestClass]
public class AuthTenancyTests
{
    [TestMethod]
    public void AddAuthAndTenancy_WhenFlagEnabled_ShouldUseTestAuth()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:FEATURE:DisableOIDCAuth"] = "true"
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddAuthAndTenancy(configuration);
        var sp = services.BuildServiceProvider();
        var schemeProvider = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var defaultScheme = schemeProvider.GetDefaultAuthenticateSchemeAsync().GetAwaiter().GetResult();

        // Assert
        defaultScheme.Should().NotBeNull();
        defaultScheme!.Name.Should().Be(TestAuthExtensions.TestAuthScheme);
    }

    [TestMethod]
    public void AddAuthAndTenancy_WhenFlagDisabled_ShouldUseCookieAuth()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:FEATURE:DisableOIDCAuth"] = "false"
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddAuthAndTenancy(configuration);
        var sp = services.BuildServiceProvider();
        var schemeProvider = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var defaultScheme = schemeProvider.GetDefaultAuthenticateSchemeAsync().GetAwaiter().GetResult();

        // Assert
        defaultScheme.Should().NotBeNull();
        defaultScheme!.Name.Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    [TestMethod]
    public void AddAuthAndTenancy_WhenFlagNotSet_ShouldUseCookieAuth()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddAuthAndTenancy(configuration);
        var sp = services.BuildServiceProvider();
        var schemeProvider = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var defaultScheme = schemeProvider.GetDefaultAuthenticateSchemeAsync().GetAwaiter().GetResult();

        // Assert
        defaultScheme.Should().NotBeNull();
        defaultScheme!.Name.Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
