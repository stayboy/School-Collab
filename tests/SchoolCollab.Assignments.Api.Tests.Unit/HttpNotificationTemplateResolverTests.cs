using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RichardSzalay.MockHttp;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// Rounds notification-templates-b1/b2c, spec §3.1.1/§3.3 — wire-level coverage for
/// <see cref="HttpNotificationTemplateResolver"/> (which lives in Assignments.Core so both hosts
/// register the one implementation). Round b2c turned the single-slot read into a <b>candidate</b> read
/// off the existing list route: the wire shape (including <c>createdAt</c>), the client-side (kind,
/// channel) filter, and the fail-open posture — 204/500/timeout/malformed/non-JSON each read as
/// <see cref="NotificationTemplateSlotState.Unreadable"/> (or <see cref="NotificationTemplateSlotState.NotSet"/>
/// for the 204) so a Settings outage never blocks a publish, while genuine cancellation propagates.
/// </summary>
[TestClass]
public class HttpNotificationTemplateResolverTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string ListUrl = "http://settings-api/api/settings/notification-templates";

    private static HttpNotificationTemplateResolver NewResolver(HttpMessageHandler settingsApi) =>
        new(new StubSettingsHttpClientFactory(settingsApi),
            NullLogger<HttpNotificationTemplateResolver>.Instance);

    private static Task<NotificationTemplateCandidatesRead> Read(HttpNotificationTemplateResolver resolver) =>
        resolver.ReadCandidatesAsync(NotificationKind.Publish, NotificationChannel.Email, TenantId);

    private static string Serialize(params TenantNotificationTemplateDto[] rows) =>
        JsonSerializer.Serialize(rows, JsonOptions);

    private static TenantNotificationTemplateDto Row(
        NotificationTemplateKind kind = NotificationTemplateKind.Publish,
        NotificationChannel channel = NotificationChannel.Email,
        string body = "<p>Body</p>",
        string? subject = "Subject",
        DateTimeOffset? createdAt = null,
        params TemplateConstraintDto[] constraints) =>
        new(Guid.NewGuid(), kind, channel, subject, body, constraints,
            createdAt ?? new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero));

    [TestMethod]
    public async Task NoContent_ReadsNotSet_SoTheBuiltInDefaultRenders()
    {
        // 204 = the tenant has no template rows at all.
        var settings = new MockHttpMessageHandler();
        settings.When(ListUrl).Respond(HttpStatusCode.NoContent);

        var read = await Read(NewResolver(settings));

        read.State.Should().Be(NotificationTemplateSlotState.NotSet);
        read.Candidates.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ServerError_ReadsUnreadable_SoThePublishProceedsOnDefaults()
    {
        var settings = new MockHttpMessageHandler();
        settings.When(ListUrl).Respond(HttpStatusCode.InternalServerError);

        var read = await Read(NewResolver(settings));

        read.State.Should().Be(NotificationTemplateSlotState.Unreadable,
            "a Settings outage must never surface as a failed publish");
        read.Candidates.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Timeout_ReadsUnreadable_OnTheResolversOwnWallClock()
    {
        // The handler hangs until the resolver's internal timeout cancels the request — the
        // caller's token is NOT cancelled, so the OperationCanceledException branch must degrade.
        var read = await Read(NewResolver(new HangingHandler()));

        read.State.Should().Be(NotificationTemplateSlotState.Unreadable);
    }

    [TestMethod]
    public async Task Timeout_DoesNotSwallowGenuineCallerCancellation()
    {
        // A real cancellation (client disconnect / shutdown) must propagate, never be read as a
        // template outage — otherwise a cancelled publish would silently continue.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => NewResolver(new HangingHandler())
            .ReadCandidatesAsync(NotificationKind.Publish, NotificationChannel.Email, TenantId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task MalformedBody_ReadsUnreadable()
    {
        // 200 with an unparseable payload: §3.3's fail-open covers "garbage", not just outages —
        // a JSON parse failure must not escape as a thrown exception that aborts the publish.
        var settings = new MockHttpMessageHandler();
        settings.When(ListUrl).Respond("application/json", "{ this is not json");

        var read = await Read(NewResolver(settings));

        read.State.Should().Be(NotificationTemplateSlotState.Unreadable);
    }

    [TestMethod]
    public async Task NonJsonPayload_ReadsUnreadable()
    {
        // A 200 carrying text/html (a gateway error page, a proxy interstitial) makes
        // ReadFromJsonAsync throw NotSupportedException — not the HttpRequestException /
        // JsonException family. It must degrade like the rest of §3.3's fail-open contract
        // rather than escape and abort the publish.
        var settings = new MockHttpMessageHandler();
        settings.When(ListUrl).Respond("text/html", "<html><body>Service unavailable</body></html>");

        var read = await Read(NewResolver(settings));

        read.State.Should().Be(NotificationTemplateSlotState.Unreadable, "a non-JSON 200 is garbage, not a template");
    }

    [TestMethod]
    public async Task CandidateRows_MapIdSubjectBodyConstraintsAndCreatedAt()
    {
        // The non-degrade twin: the fail-open tests above are only meaningful if the happy path
        // actually maps the payload — including createdAt, the Q67 tie-break input.
        var id = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        var settings = new MockHttpMessageHandler();
        settings.When(ListUrl).Respond("application/json", Serialize(
            new TenantNotificationTemplateDto(
                id, NotificationTemplateKind.Publish, NotificationChannel.Email,
                "New task: {{Title}}", "<p>{{Description}}</p>",
                [new TemplateConstraintDto(Guid.NewGuid(), TemplateConstraintDimension.GradeLevel, "Grade 3")],
                createdAt, createdAt.AddDays(1))));

        var read = await Read(NewResolver(settings));

        read.State.Should().Be(NotificationTemplateSlotState.Found);
        var candidate = read.Candidates.Should().ContainSingle().Subject;
        candidate.TemplateId.Should().Be(id);
        candidate.Subject.Should().Be("New task: {{Title}}");
        candidate.Body.Should().Be("<p>{{Description}}</p>");
        candidate.CreatedAt.Should().Be(createdAt);
        candidate.Constraints.Should().ContainSingle()
            .Which.Should().Be(new NotificationTemplateConstraint("GradeLevel", "Grade 3"));
    }

    [TestMethod]
    public async Task RowsOfAnotherSlot_AreFilteredOutClientSide()
    {
        // The read consumes the EXISTING list route (no candidates route exists), so the (kind,
        // channel) narrowing happens in memory. Discriminating: the same payload yields one
        // candidate for Publish/Email, none for Publish/SMS.
        var settings = new MockHttpMessageHandler();
        settings.When(ListUrl).Respond("application/json", Serialize(
            Row(kind: NotificationTemplateKind.Publish, channel: NotificationChannel.Email),
            Row(kind: NotificationTemplateKind.Reminder, channel: NotificationChannel.Email),
            Row(kind: NotificationTemplateKind.Publish, channel: NotificationChannel.SMS)));

        var resolver = NewResolver(settings);
        var email = await resolver.ReadCandidatesAsync(
            NotificationKind.Publish, NotificationChannel.Email, TenantId);

        email.State.Should().Be(NotificationTemplateSlotState.Found);
        email.Candidates.Should().ContainSingle("only the Publish/Email row belongs to this slot");
    }

    [TestMethod]
    public async Task NoRowForTheSlot_ReadsNotSet()
    {
        var settings = new MockHttpMessageHandler();
        settings.When(ListUrl).Respond("application/json", Serialize(
            Row(kind: NotificationTemplateKind.Publish, channel: NotificationChannel.Email)));

        var read = await NewResolver(settings).ReadCandidatesAsync(
            NotificationKind.Publish, NotificationChannel.SMS, TenantId);

        read.State.Should().Be(NotificationTemplateSlotState.NotSet);
        read.Candidates.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SeveralCandidates_AreReturnedUnordered()
    {
        // The read has no assignment context, so it must not imply an order the evaluator owns:
        // the payload order is preserved and nothing more is promised.
        var first = Row(createdAt: new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero));
        var second = Row(createdAt: new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        var settings = new MockHttpMessageHandler();
        settings.When(ListUrl).Respond("application/json", Serialize(first, second));

        var read = await Read(NewResolver(settings));

        read.State.Should().Be(NotificationTemplateSlotState.Found);
        read.Candidates.Select(c => c.TemplateId).Should().ContainInOrder(first.Id, second.Id);
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
