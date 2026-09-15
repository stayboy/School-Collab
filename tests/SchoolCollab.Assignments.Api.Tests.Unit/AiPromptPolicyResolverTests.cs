using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;
using SchoolCollab.Assignments.Api.Services;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// WS-B2 (spec §3.4 line 70) — the HTTP-backed <see cref="AiPromptPolicyResolver"/>:
/// 204 (no org prompt) ⇒ false, 200 ⇒ the dto's <c>IsLocked</c>, and any fetch
/// failure fails open to false so a create/edit wizard is never blocked.
/// </summary>
[TestClass]
public class AiPromptPolicyResolverTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static AiPromptPolicyResolver NewResolver(MockHttpMessageHandler settingsApi) =>
        new(new StubSettingsClientFactory(settingsApi), NullLogger<AiPromptPolicyResolver>.Instance);

    [TestMethod]
    public async Task Resolve_NoOrgPrompt_ReturnsFalse()
    {
        var settings = new MockHttpMessageHandler();
        settings.When("http://settings-api/api/settings/assignment-ai-prompt")
            .Respond(HttpStatusCode.NoContent);
        var resolver = NewResolver(settings);

        var result = await resolver.ResolveAiPromptLockedAsync();

        result.Should().BeFalse();
    }

    [TestMethod]
    public async Task Resolve_IsLocked_ReturnsTrue()
    {
        var settings = new MockHttpMessageHandler();
        settings.When("http://settings-api/api/settings/assignment-ai-prompt")
            .Respond("application/json", JsonSerializer.Serialize(
                new TenantAssignmentAiPromptDto(SystemPrompt: "locked prompt", IsLocked: true), JsonOptions));
        var resolver = NewResolver(settings);

        var result = await resolver.ResolveAiPromptLockedAsync();

        result.Should().BeTrue();
    }

    [TestMethod]
    public async Task Resolve_NotLocked_ReturnsFalse()
    {
        var settings = new MockHttpMessageHandler();
        settings.When("http://settings-api/api/settings/assignment-ai-prompt")
            .Respond("application/json", JsonSerializer.Serialize(
                new TenantAssignmentAiPromptDto(SystemPrompt: "open prompt", IsLocked: false), JsonOptions));
        var resolver = NewResolver(settings);

        var result = await resolver.ResolveAiPromptLockedAsync();

        result.Should().BeFalse();
    }

    [TestMethod]
    public async Task Resolve_SettingsUnreachable_DegradesToFalse()
    {
        var settings = new MockHttpMessageHandler();
        settings.When("http://settings-api/api/settings/assignment-ai-prompt")
            .Throw(new HttpRequestException("settings-api unreachable"));
        var resolver = NewResolver(settings);

        var result = await resolver.ResolveAiPromptLockedAsync();

        result.Should().BeFalse();
    }

    private sealed class StubSettingsClientFactory(MockHttpMessageHandler settingsApi) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = settingsApi.ToHttpClient();
            client.BaseAddress = new Uri("http://settings-api");
            return client;
        }
    }
}
