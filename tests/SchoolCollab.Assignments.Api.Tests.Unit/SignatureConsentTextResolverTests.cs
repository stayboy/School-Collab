using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;
using SchoolCollab.Assignments.Api.Services;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// WS-C1/C2 (spec §3.2 line 53) coverage for the HTTP-backed
/// <see cref="SignatureConsentTextResolver"/>: tenant override when configured,
/// embedded default when unset, and the fail-open degradation posture (any
/// settings-api failure falls back to the embedded default — signing is never
/// blocked by a Settings outage).
/// </summary>
[TestClass]
public class SignatureConsentTextResolverTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static SignatureConsentTextResolver NewResolver(MockHttpMessageHandler settingsApi)
    {
        return new SignatureConsentTextResolver(
            new StubSettingsHttpClientFactory(settingsApi),
            NullLogger<SignatureConsentTextResolver>.Instance);
    }

    [TestMethod]
    public async Task Resolve_WithTenantRow_ReturnsTenantText()
    {
        var settings = new MockHttpMessageHandler();
        settings.When("http://settings-api/api/settings/signature-consent-text")
            .Respond("application/json", JsonSerializer.Serialize(
                new TenantSignatureConsentTextDto("District consent language"), JsonOptions));
        var resolver = NewResolver(settings);

        var result = await resolver.ResolveConsentTextAsync();

        result.Should().Be("District consent language");
    }

    [TestMethod]
    public async Task Resolve_NoTenantRow_ReturnsEmbeddedDefault()
    {
        var settings = new MockHttpMessageHandler();
        settings.When("http://settings-api/api/settings/signature-consent-text")
            .Respond(HttpStatusCode.NoContent);
        var resolver = NewResolver(settings);

        var result = await resolver.ResolveConsentTextAsync();

        result.Should().Be(SignatureConsentDefaults.EmbeddedConsentText);
    }

    [TestMethod]
    public async Task Resolve_NetworkError_FallsBackToEmbedded()
    {
        var settings = new MockHttpMessageHandler();
        settings.When("http://settings-api/api/settings/signature-consent-text")
            .Respond(HttpStatusCode.BadGateway);
        var resolver = NewResolver(settings);

        var result = await resolver.ResolveConsentTextAsync();

        result.Should().Be(SignatureConsentDefaults.EmbeddedConsentText);
    }

    private sealed class StubSettingsHttpClientFactory(MockHttpMessageHandler settingsApi) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = settingsApi.ToHttpClient();
            client.BaseAddress = new Uri("http://settings-api");
            return client;
        }
    }
}