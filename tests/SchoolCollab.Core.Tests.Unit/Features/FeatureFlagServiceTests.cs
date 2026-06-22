using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Core.Tests.Unit.Features;

[TestClass]
public class FeatureFlagServiceTests
{
    private IConfiguration CreateConfiguration(Dictionary<string, string?> flags)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(flags)
            .Build();
    }

    [TestMethod]
    public void IsEnabled_WhenFlagIsTrue_ReturnsTrue()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?> 
        { 
            { "FeatureFlags:FEATURE:DisableOIDCAuth", "true" } 
        });
        var service = new FeatureFlagService(config);

        // Act
        var result = service.IsEnabled("FEATURE:DisableOIDCAuth");

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsEnabled_WhenFlagIsFalse_ReturnsFalse()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?> 
        { 
            { "FeatureFlags:FEATURE:DisableOIDCAuth", "false" } 
        });
        var service = new FeatureFlagService(config);

        // Act
        var result = service.IsEnabled("FEATURE:DisableOIDCAuth");

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsEnabled_WhenFlagIsMissing_ReturnsFalse()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?>());
        var service = new FeatureFlagService(config);

        // Act
        var result = service.IsEnabled("FEATURE:DisableOIDCAuth");

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void GetAllFlags_ReturnsCorrectFlags()
    {
        // Arrange
        var config = CreateConfiguration(new Dictionary<string, string?> 
        { 
            { "FeatureFlags:Flag1", "true" },
            { "FeatureFlags:Flag2", "false" },
            { "FeatureFlags:NotABool", "hello" }
        });
        var service = new FeatureFlagService(config);

        // Act
        var result = service.GetAllFlags();

        // Assert
        Assert.IsTrue(result.ContainsKey("Flag1"));
        Assert.IsTrue(result["Flag1"]);
        Assert.IsTrue(result.ContainsKey("Flag2"));
        Assert.IsFalse(result["Flag2"]);
        Assert.IsFalse(result.ContainsKey("NotABool"));
    }
}
