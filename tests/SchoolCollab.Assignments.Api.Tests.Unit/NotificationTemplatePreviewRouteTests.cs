using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Api;
using SchoolCollab.Assignments.Api.Endpoints;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// Wire tests for <c>POST /assignments/notification-templates/preview</c> (spec §3.10, round
/// notification-templates-b3b): the Settings editor's live preview must run the SAME render path as
/// a send, against the documented sample data, with a channel-appropriate <c>{{Link}}</c>. The host
/// here registers the enum-name converters the REAL Program.cs block carries — the
/// <see cref="ProgramJsonEnumConverterTests"/> source scan is what keeps the two from diverging.
/// </summary>
[TestClass]
public class NotificationTemplatePreviewRouteTests
{
    private sealed class StubFeatureFlags : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => featureKey == FeatureFlagKeys.DisableOIDCAuth;
        public IDictionary<string, bool> GetAllFlags() =>
            new Dictionary<string, bool> { [FeatureFlagKeys.DisableOIDCAuth] = true };
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(IsEnabled(featureKey));
    }

    private static async Task<(WebApplication App, HttpClient Client)> StartHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IFeatureFlagService>(new StubFeatureFlags());
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<NotificationKind>());
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<NotificationChannel>());
        });

        var app = builder.Build();
        app.MapAssignmentEndpoints(app.Services.GetRequiredService<IFeatureFlagService>());
        await app.StartAsync();

        var client = ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();
        return (app, client);
    }

    private static Task<HttpResponseMessage> PostPreviewAsync(
        HttpClient client,
        NotificationKind kind,
        NotificationChannel channel,
        string? subject,
        string body) =>
        client.PostAsJsonAsync(
            "/assignments/notification-templates/preview",
            new { kind = kind.ToString(), channel = channel.ToString(), subject, body });

    [TestMethod]
    public async Task Preview_RendersTheAuthoredBody_NotTheBuiltInDefault()
    {
        var (app, client) = await StartHostAsync();
        await using (app)
        {
            var response = await PostPreviewAsync(
                client,
                NotificationKind.Publish,
                NotificationChannel.SMS,
                subject: null,
                body: "AUTHORED ONLY: {{Title}}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var rendered = await response.Content.ReadFromJsonAsync<NotificationTemplatePreviewResponse>();
            rendered.Should().NotBeNull();
            rendered!.Body.Should().StartWith("AUTHORED ONLY:",
                "a null resolved template would render the built-in default and silently ignore the author's text");
            rendered.Body.Should().Contain("Grade 8 Science",
                "the documented sample assignment title substitutes");
        }
    }

    [TestMethod]
    public async Task Preview_LeavesAnUnknownTagLiteral_AndStripsScriptsFromTheEmailBody()
    {
        var (app, client) = await StartHostAsync();
        await using (app)
        {
            var response = await PostPreviewAsync(
                client,
                NotificationKind.Publish,
                NotificationChannel.Email,
                subject: "New: {{Title}}",
                body: "<p>{{Title}} — {{NoSuchTag}}</p><script>alert(1)</script>");

            var rendered = await response.Content.ReadFromJsonAsync<NotificationTemplatePreviewResponse>();

            rendered!.Subject.Should().Be("New: Grade 8 Science: the water cycle investigation",
                "the email subject runs the plain-text substitution");
            rendered.Body.Should().Contain("{{NoSuchTag}}",
                "§3.2: an unknown tag stays literal so the preview shows the author the mistake");
            rendered.Body.Should().NotContain("<script>",
                "the preview runs the ONE sanitizer, exactly like a send");
        }
    }

    [TestMethod]
    public async Task Preview_UsesTheLongLinkForEmail_AndTheShortLinkForSms()
    {
        var (app, client) = await StartHostAsync();
        await using (app)
        {
            var email = await (await PostPreviewAsync(
                    client, NotificationKind.Reminder, NotificationChannel.Email, null, "Open {{Link}}"))
                .Content.ReadFromJsonAsync<NotificationTemplatePreviewResponse>();

            var sms = await (await PostPreviewAsync(
                    client, NotificationKind.Reminder, NotificationChannel.SMS, null, "Open {{Link}}"))
                .Content.ReadFromJsonAsync<NotificationTemplatePreviewResponse>();

            email!.Body.Should().Contain("/deeplink/",
                "the email sample link is the long token URL (§3.2)");
            sms!.Body.Should().Contain("/l/SAMPLE01",
                "the SMS sample link is the short code form (§3.2) — the one sample bit a preview can get visibly wrong");
            sms.Body.Should().NotContain("/deeplink/");
        }
    }
}
