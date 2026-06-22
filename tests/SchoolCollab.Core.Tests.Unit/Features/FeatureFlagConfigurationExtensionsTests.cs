using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Core.Tests.Unit.Features;

[TestClass]
public class FeatureFlagConfigurationExtensionsTests
{
    [TestMethod]
    public void ResolveAspireServiceAddress_ReturnsOriginal_WhenAddressIsNotServiceDiscovery()
    {
        // Arrange
        const string address = "http://localhost:5000";

        // Act
        var resolved = FeatureFlagConfigurationExtensions.ResolveAspireServiceAddress(address);

        // Assert
        Assert.AreEqual(address, resolved);
    }

    [TestMethod]
    public void ResolveAspireServiceAddress_ReturnsResolvedUrl_WhenAspireEnvVarExists()
    {
        // Arrange
        const string aspireAddress = "https+http://config";
        const string expected = "http://localhost:61431";
        const string envVarName = "services__config__http__0";

        var original = Environment.GetEnvironmentVariable(envVarName);
        Environment.SetEnvironmentVariable(envVarName, expected);

        try
        {
            // Act
            var resolved = FeatureFlagConfigurationExtensions.ResolveAspireServiceAddress(aspireAddress);

            // Assert
            Assert.AreEqual(expected, resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, original);
        }
    }

    [TestMethod]
    public void ResolveAspireServiceAddress_PrefersHttps_WhenBothEnvVarsExist()
    {
        // Arrange
        const string aspireAddress = "https+http://config";
        const string httpUrl = "http://localhost:61431";
        const string httpsUrl = "https://localhost:61430";
        const string httpEnvVar = "services__config__http__0";
        const string httpsEnvVar = "services__config__https__0";

        var originalHttp = Environment.GetEnvironmentVariable(httpEnvVar);
        var originalHttps = Environment.GetEnvironmentVariable(httpsEnvVar);
        Environment.SetEnvironmentVariable(httpEnvVar, httpUrl);
        Environment.SetEnvironmentVariable(httpsEnvVar, httpsUrl);

        try
        {
            // Act
            var resolved = FeatureFlagConfigurationExtensions.ResolveAspireServiceAddress(aspireAddress);

            // Assert
            Assert.AreEqual(httpsUrl, resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(httpEnvVar, originalHttp);
            Environment.SetEnvironmentVariable(httpsEnvVar, originalHttps);
        }
    }

    [TestMethod]
    public void ResolveAspireServiceAddress_ReturnsOriginal_WhenNoEnvVarExists()
    {
        // Arrange
        const string aspireAddress = "https+http://missing";
        const string httpEnvVar = "services__missing__http__0";
        const string httpsEnvVar = "services__missing__https__0";

        var originalHttp = Environment.GetEnvironmentVariable(httpEnvVar);
        var originalHttps = Environment.GetEnvironmentVariable(httpsEnvVar);
        Environment.SetEnvironmentVariable(httpEnvVar, null);
        Environment.SetEnvironmentVariable(httpsEnvVar, null);

        try
        {
            // Act
            var resolved = FeatureFlagConfigurationExtensions.ResolveAspireServiceAddress(aspireAddress);

            // Assert
            Assert.AreEqual(aspireAddress, resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(httpEnvVar, originalHttp);
            Environment.SetEnvironmentVariable(httpsEnvVar, originalHttps);
        }
    }
}
