using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Core.Tests.Unit.Features;

[TestClass]
public class ConfigFeatureFlagConfigurationProviderTests
{
    private static HttpClient CreateHttpClient(HttpStatusCode statusCode, string? responseBody = null)
    {
        var handler = new TestHttpMessageHandler(statusCode, responseBody);
        return new HttpClient(handler) { BaseAddress = new Uri("http://config") };
    }

    private static IConfigurationRoot BuildRoot(ConfigFeatureFlagConfigurationProvider provider)
    {
        return new ConfigurationRoot(new List<IConfigurationProvider> { provider });
    }

    [TestMethod]
    public void Load_WhenEndpointReturnsFlags_LoadsThemUnderFeatureFlagsPrefix()
    {
        // Arrange
        var flags = new Dictionary<string, bool>
        {
            { "FEATURE:DisableOIDCAuth", true },
            { "FEATURE:NewDashboard", false }
        };
        var json = JsonSerializer.Serialize(flags);
        var provider = new ConfigFeatureFlagConfigurationProvider(CreateHttpClient(HttpStatusCode.OK, json));

        // Act
        provider.Load();

        // Assert
        var config = BuildRoot(provider);
        Assert.AreEqual("True", config["FeatureFlags:FEATURE:DisableOIDCAuth"]);
        Assert.AreEqual("False", config["FeatureFlags:FEATURE:NewDashboard"]);
    }

    [TestMethod]
    public void Load_WhenEndpointFails_LeavesConfigurationEmpty()
    {
        // Arrange
        var provider = new ConfigFeatureFlagConfigurationProvider(CreateHttpClient(HttpStatusCode.InternalServerError));

        // Act
        provider.Load();

        // Assert
        var config = BuildRoot(provider);
        Assert.IsNull(config["FeatureFlags:FEATURE:DisableOIDCAuth"]);
    }

    [TestMethod]
    public void Load_WhenEndpointThrows_LeavesConfigurationEmpty()
    {
        // Arrange
        var handler = new TestHttpMessageHandler(throws: true);
        var provider = new ConfigFeatureFlagConfigurationProvider(new HttpClient(handler));

        // Act
        provider.Load();

        // Assert
        var config = BuildRoot(provider);
        Assert.IsNull(config["FeatureFlags:FEATURE:DisableOIDCAuth"]);
    }

    [TestMethod]
    public void Load_WhenBodyIsMalformedJson_LeavesConfigurationEmpty()
    {
        // Arrange
        var provider = new ConfigFeatureFlagConfigurationProvider(CreateHttpClient(HttpStatusCode.OK, "not-json"));

        // Act
        provider.Load();

        // Assert
        var config = BuildRoot(provider);
        Assert.IsNull(config["FeatureFlags:FEATURE:DisableOIDCAuth"]);
    }

    [TestMethod]
    public void AddRemoteFeatureFlags_IntegratesWithConfigurationBuilder()
    {
        // Arrange
        var flags = new Dictionary<string, bool>
        {
            { "FEATURE:DisableOIDCAuth", true }
        };
        var handler = new TestHttpMessageHandler(HttpStatusCode.OK, JsonSerializer.Serialize(flags));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://config") };

        var configuration = new ConfigurationBuilder()
            .Add(new ConfigFeatureFlagConfigurationSource(httpClient))
            .Build();

        // Act
        var value = configuration["FeatureFlags:FEATURE:DisableOIDCAuth"];

        // Assert
        Assert.AreEqual("True", value);
    }

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string? _responseBody;
        private readonly bool _throws;

        public TestHttpMessageHandler(HttpStatusCode statusCode = HttpStatusCode.OK, string? responseBody = null, bool throws = false)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
            _throws = throws;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_throws)
            {
                throw new HttpRequestException("Simulated failure");
            }

            var response = new HttpResponseMessage(_statusCode)
            {
                Content = _responseBody is not null
                    ? new StringContent(_responseBody, Encoding.UTF8, "application/json")
                    : null
            };

            return Task.FromResult(response);
        }
    }
}
