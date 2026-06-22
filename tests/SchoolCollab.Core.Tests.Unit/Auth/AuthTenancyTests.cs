using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Moq;
using FluentAssertions;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Core.Tests.Unit.Auth;

[TestClass]
public class AuthTenancyTests
{
    private Mock<IFeatureFlagService> _mockFeatureService = null!;
    private IConfiguration _configuration = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockFeatureService = new Mock<IFeatureFlagService>();

        var settings = new Dictionary<string, string?> {
            {"Environment", "Development"},
            {"Auth:Keycloak:Authority", "https://keycloak.local/realms/school-collab"}
        };
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    [TestMethod]
    public void AddAuthAndTenancy_WhenFlagEnabled_ShouldUseTestAuth()
    {
        // Arrange
        _mockFeatureService.Setup(s => s.IsEnabled("FEATURE:DisableOIDCAuth")).Returns(true);

        var services = new ServiceCollection();
        services.AddSingleton<IFeatureFlagService>(_mockFeatureService.Object);

        // Act
        services.AddAuthAndTenancy(_configuration);
        var sp = services.BuildServiceProvider();
        var schemeProvider = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var defaultScheme = schemeProvider.GetDefaultAuthenticateSchemeAsync().GetAwaiter().GetResult();

        // Assert
        _mockFeatureService.Verify(s => s.IsEnabled("FEATURE:DisableOIDCAuth"), Times.AtLeastOnce);
        defaultScheme.Should().NotBeNull();
        defaultScheme!.Name.Should().Be(TestAuthExtensions.TestAuthScheme);
    }

    [TestMethod]
    public void AddAuthAndTenancy_WhenFlagDisabled_ShouldUseCookieAuth()
    {
        // Arrange
        _mockFeatureService.Setup(s => s.IsEnabled("FEATURE:DisableOIDCAuth")).Returns(false);

        var services = new ServiceCollection();
        services.AddSingleton<IFeatureFlagService>(_mockFeatureService.Object);

        // Act
        services.AddAuthAndTenancy(_configuration);
        var sp = services.BuildServiceProvider();
        var schemeProvider = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var defaultScheme = schemeProvider.GetDefaultAuthenticateSchemeAsync().GetAwaiter().GetResult();

        // Assert
        _mockFeatureService.Verify(s => s.IsEnabled("FEATURE:DisableOIDCAuth"), Times.AtLeastOnce);
        defaultScheme.Should().NotBeNull();
        defaultScheme!.Name.Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
