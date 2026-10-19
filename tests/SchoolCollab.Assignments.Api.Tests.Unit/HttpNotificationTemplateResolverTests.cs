using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RichardSzalay.MockHttp;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// Round notification-templates-b1, spec §3.3 — wire-level fail-open coverage for
/// <see cref="HttpNotificationTemplateResolver"/> (which lives in Assignments.Core so both
/// hosts register the one implementation): 204 (nothing set), a 500, a timeout, a malformed
/// body and a non-JSON 200 each resolve <see langword="null"/>, so the renderer falls back to
/// the built-in defaults and a Settings outage never blocks a publish.
/// </summary>
[TestClass]
public class HttpNotificationTemplateResolverTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string TemplateUrl =
        "http://settings-api/api/settings/notification-templates/Publish/Email";

    private static HttpNotificationTemplateResolver NewResolver(HttpMessageHandler settingsApi) =>
        new(new StubSettingsHttpClientFactory(settingsApi),
            NullLogger<HttpNotificationTemplateResolver>.Instance);

    private static Task<ResolvedNotificationTemplate?> Resolve(HttpNotificationTemplateResolver resolver) =>
        resolver.ResolveAsync(NotificationKind.Publish, NotificationChannel.Email, TenantId);

    [TestMethod]
    public async Task NoContent_ResolvesNull_SoTheBuiltInDefaultRenders()
    {
        // 204 = the tenant has no template row (the assignment-policy endpoint posture).
        var settings = new MockHttpMessageHandler();
        settings.When(TemplateUrl).Respond(HttpStatusCode.NoContent);

        var template = await Resolve(NewResolver(settings));

        template.Should().BeNull();
    }

    [TestMethod]
    public async Task ServerError_ResolvesNull_SoThePublishProceedsOnDefaults()
    {
        var settings = new MockHttpMessageHandler();
        settings.When(TemplateUrl).Respond(HttpStatusCode.InternalServerError);

        var template = await Resolve(NewResolver(settings));

        template.Should().BeNull("a Settings outage must never surface as a failed publish");
    }

    [TestMethod]
    public async Task Timeout_ResolvesNull_OnTheResolversOwnWallClock()
    {
        // The handler hangs until the resolver's internal timeout cancels the request — the
        // caller's token is NOT cancelled, so the OperationCanceledException branch must degrade.
        var template = await Resolve(NewResolver(new HangingHandler()));

        template.Should().BeNull();
    }

    [TestMethod]
    public async Task Timeout_DoesNotSwallowGenuineCallerCancellation()
    {
        // A real cancellation (client disconnect / shutdown) must propagate, never be read as a
        // template outage — otherwise a cancelled publish would silently continue.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => NewResolver(new HangingHandler())
            .ResolveAsync(NotificationKind.Publish, NotificationChannel.Email, TenantId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task MalformedBody_ResolvesNull()
    {
        // 200 with an unparseable payload: §3.3's fail-open covers "garbage", not just outages —
        // a JSON parse failure must not escape as a thrown exception that aborts the publish.
        var settings = new MockHttpMessageHandler();
        settings.When(TemplateUrl).Respond("application/json", "{ this is not json");

        var template = await Resolve(NewResolver(settings));

        template.Should().BeNull();
    }

    [TestMethod]
    public async Task NonJsonPayload_ResolvesNull()
    {
        // A 200 carrying text/html (a gateway error page, a proxy interstitial) makes
        // ReadFromJsonAsync throw NotSupportedException — not the HttpRequestException /
        // JsonException family. It must degrade like the rest of §3.3's fail-open contract
        // rather than escape and abort the publish.
        var settings = new MockHttpMessageHandler();
        settings.When(TemplateUrl).Respond("text/html", "<html><body>Service unavailable</body></html>");

        var template = await Resolve(NewResolver(settings));

        template.Should().BeNull("a non-JSON 200 is garbage, not a template");
    }

    [TestMethod]
    public async Task ConfiguredTemplate_MapsIdSubjectAndBody()
    {
        // The non-degrade twin: the fail-open tests above are only meaningful if the happy path
        // actually maps the payload.
        var id = Guid.NewGuid();
        var settings = new MockHttpMessageHandler();
        settings.When(TemplateUrl).Respond(
            "application/json",
            JsonSerializer.Serialize(
                new TenantNotificationTemplateDto(
                    id, NotificationTemplateKind.Publish, NotificationChannel.Email, "New task: {{Title}}",
                    "<p>{{Description}}</p>", [], DateTimeOffset.UtcNow),
                JsonOptions));

        var template = await Resolve(NewResolver(settings));

        template.Should().NotBeNull();
        template!.TemplateId.Should().Be(id);
        template.Subject.Should().Be("New task: {{Title}}");
        template.Body.Should().Be("<p>{{Description}}</p>");
    }

    private sealed class StubSettingsHttpClientFactory(HttpMessageHandler settingsApi) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = settingsApi is MockHttpMessageHandler mock
                ? mock.ToHttpClient()
                : new HttpClient(settingsApi, disposeHandler: false);
            client.BaseAddress = new Uri("http://settings-api");
            return client;
        }
    }

    /// <summary>A Settings service that never answers: the request hangs on the caller's token,
    /// which is exactly the state the resolver's 5s wall clock exists for.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
