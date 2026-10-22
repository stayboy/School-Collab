using System.Net;
using System.Text;
using FluentAssertions;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Wire contract of <see cref="NotificationTemplateApiClient"/> (round notification-templates-b3a).
/// Pins, per method, the path and HTTP method the Settings routes actually expose — including the
/// <b>by-id</b> mutation paths (<c>PUT</c>/<c>DELETE …/{id}</c>) that round notification-templates-b2c
/// introduced — the documented <c>204 → null</c> of the default-row read, and the admin surface's
/// throw-on-non-success posture.
/// </summary>
[TestClass]
public class NotificationTemplateApiClientTests
{
    private const string BasePath = "/api/settings/notification-templates";

    private static readonly Guid TemplateId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid GradeLevelId = Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789");

    private const string TemplateJson = """
        {
          "id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
          "kind": 1,
          "channel": 1,
          "title": "Grade 8 Science reminder",
          "description": "Sent the day before the lab.",
          "code": "TPL07",
          "subject": null,
          "body": "Due {{DueDate}}",
          "constraints": [{ "id": "cccccccc-cccc-cccc-cccc-cccccccccccc", "dimension": 0, "value": "abcdef01-2345-6789-abcd-ef0123456789" }],
          "createdAt": "2026-10-19T00:00:00+00:00",
          "updatedAt": "2026-10-19T01:00:00+00:00"
        }
        """;

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            var response = new HttpResponseMessage(status);
            if (status != HttpStatusCode.NoContent)
            {
                response.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            return response;
        }
    }

    private static (NotificationTemplateApiClient Client, RecordingHandler Handler) Build(
        HttpStatusCode status = HttpStatusCode.OK, string body = "{}")
    {
        var handler = new RecordingHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        return (new NotificationTemplateApiClient(http), handler);
    }

    // ── Reads ───────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ListAsync_HitsTheListPathAndReturnsTheRows()
    {
        var (client, handler) = Build(body: $"[{TemplateJson}]");

        var rows = await client.ListAsync();

        handler.Requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be(BasePath);
        rows.Should().ContainSingle();
        rows[0].Body.Should().Be("Due {{DueDate}}");
        rows[0].Title.Should().Be("Grade 8 Science reminder",
            "the row's identity crosses the wire (naming spec D3)");
        rows[0].Description.Should().Be("Sent the day before the lab.");
        rows[0].Code.Should().Be("TPL07", "the generated reference is read-only data (D6)");
        rows[0].Constraints.Should().ContainSingle()
            .Which.Dimension.Should().Be(TemplateConstraintDimension.GradeLevel);
    }

    [TestMethod]
    public async Task ListAsync_ToleratesARowWithoutATitleOrCode()
    {
        // D7: a row that predates the naming columns carries a null title and a null code, and a client
        // must not treat that as a malformed payload — the editor and the landing render the fallback.
        var (client, _) = Build(body: """
            [{
              "id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
              "kind": 1,
              "channel": 1,
              "title": null,
              "description": null,
              "code": null,
              "subject": "Legacy subject",
              "body": "Due {{DueDate}}",
              "constraints": [],
              "createdAt": "2026-10-19T00:00:00+00:00",
              "updatedAt": "2026-10-19T01:00:00+00:00"
            }]
            """);

        var rows = await client.ListAsync();

        rows.Should().ContainSingle();
        rows[0].Code.Should().BeNull();
        rows[0].Description.Should().BeNull();
    }

    [TestMethod]
    public async Task GetDefaultAsync_ReturnsNullOn204()
    {
        // Spec §5: 204 = unset — the built-in default applies. The only non-throwing non-success.
        var (client, handler) = Build(HttpStatusCode.NoContent);

        var row = await client.GetDefaultAsync(
            NotificationTemplateKind.Reminder, NotificationChannel.SMS);

        row.Should().BeNull();
        handler.Requests[0].RequestUri!.AbsolutePath.Should()
            .Be($"{BasePath}/Reminder/SMS", "route values bind by NAME on the Settings host");
    }

    [TestMethod]
    public async Task GetDefaultAsync_ReturnsTheRowOn200()
    {
        var (client, handler) = Build(body: TemplateJson);

        var row = await client.GetDefaultAsync(
            NotificationTemplateKind.Reminder, NotificationChannel.SMS);

        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        row.Should().NotBeNull();
        row!.Id.Should().Be(TemplateId);
        row.Kind.Should().Be(NotificationTemplateKind.Reminder);
        row.Channel.Should().Be(NotificationChannel.SMS);
    }

    [TestMethod]
    public async Task GetAuditAsync_HitsTheKeyedAuditPath()
    {
        var (client, handler) = Build(body: """
            [{
              "id": "dddddddd-dddd-dddd-dddd-dddddddddddd",
              "changeKind": 1,
              "previousSubject": "old",
              "newSubject": "new",
              "previousBody": null,
              "newBody": "body",
              "previousConstraints": null,
              "newConstraints": "[]",
              "reason": "tweak",
              "actorId": "test:actor",
              "actorDisplayName": "Test Actor",
              "occurredAt": "2026-10-19T00:00:00+00:00"
            }]
            """);

        var entries = await client.GetAuditAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email);

        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be($"{BasePath}/Publish/Email/audit");
        entries.Should().ContainSingle();
        entries[0].ChangeKind.Should().Be(TemplateChangeKind.Updated);
        entries[0].Reason.Should().Be("tweak");
    }

    [TestMethod]
    public async Task GetApplicableAsync_HitsTheApplicabilityPathAndReturnsTheSlots()
    {
        var (client, handler) = Build(body: """
            {
              "slots": [{
                "kind": 0,
                "channel": 0,
                "templates": [{
                  "id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                  "subject": "Subject",
                  "isDefault": false,
                  "constraints": [{ "dimension": 0, "value": "abcdef01-2345-6789-abcd-ef0123456789", "display": "Grade 3" }]
                }]
              }]
            }
            """);

        var result = await client.GetApplicableAsync(GradeLevelId);

        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be($"{BasePath}/applicable/{GradeLevelId:D}");
        result.Slots.Should().ContainSingle();
        result.Slots[0].Templates.Should().ContainSingle()
            .Which.Constraints[0].Display.Should().Be("Grade 3");
    }

    // ── Writes ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CreateAsync_PostsTheRequestToTheListPath()
    {
        var (client, handler) = Build(body: TemplateJson);

        var created = await client.CreateAsync(new CreateNotificationTemplateRequest(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            "Grade 8 Science reminder", "Sent the day before the lab.",
            "Subject for {{Title}}", "Due {{DueDate}}",
            [new TemplateConstraintRequest(TemplateConstraintDimension.GradeLevel, GradeLevelId.ToString("D"))],
            "initial"));

        handler.Requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be(BasePath);
        handler.Bodies[0].Should().Contain("\"body\":\"Due {{DueDate}}\"");
        handler.Bodies[0].Should().Contain("\"title\":\"Grade 8 Science reminder\"",
            "the naming fields are part of the create body (naming spec D1)");
        handler.Bodies[0].Should().Contain("\"description\":\"Sent the day before the lab.\"");
        handler.Bodies[0].Should().Contain("\"kind\":0",
            "the Settings host registers no JsonStringEnumConverter, so a body carries the numeric enum value");
        created.Id.Should().Be(TemplateId);
    }

    [TestMethod]
    public async Task UpdateAsync_PutsToTheByIdPath()
    {
        var (client, handler) = Build(body: TemplateJson);

        await client.UpdateAsync(TemplateId, new UpdateNotificationTemplateRequest(
            "Renamed", null, null, "Due {{DueDate}}", [], "tweak"));

        handler.Requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Put);
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be($"{BasePath}/{TemplateId:D}");
        handler.Bodies[0].Should().Contain("\"body\":\"Due {{DueDate}}\"");
        handler.Bodies[0].Should().Contain("\"title\":\"Renamed\"",
            "a rename travels on the replace body (naming spec D1)");
        handler.Bodies[0].Should().NotContain("\"code\"",
            "the generated code is never writable, so it is not part of any request (D6)");
    }

    [TestMethod]
    public async Task DeleteAsync_SendsTheIdAndTheReason()
    {
        var (client, handler) = Build(HttpStatusCode.NoContent);

        await client.DeleteAsync(TemplateId, "reset after review");

        handler.Requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Delete);
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be($"{BasePath}/{TemplateId:D}");
        handler.Requests[0].RequestUri!.Query.Should().Be("?reason=reset%20after%20review",
            "a reset is audited, so the operator's reason must reach the endpoint");
    }

    // ── Failure posture ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task EveryMethod_ThrowsOnANonSuccessResponse()
    {
        // The admin surface must show the error; fail-open is the render path's posture, not the
        // editor's. Only the documented 204 of GetDefaultAsync is mapped to a value.
        var (client, _) = Build(HttpStatusCode.InternalServerError, "boom");

        await FluentActions.Awaiting(() => client.ListAsync()).Should().ThrowAsync<HttpRequestException>();
        await FluentActions.Awaiting(() => client.GetDefaultAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email)).Should().ThrowAsync<HttpRequestException>();
        await FluentActions.Awaiting(() => client.CreateAsync(new CreateNotificationTemplateRequest(
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            "Title", null, null, "Body", [])))
            .Should().ThrowAsync<HttpRequestException>();
        await FluentActions.Awaiting(() => client.UpdateAsync(
            TemplateId, new UpdateNotificationTemplateRequest("Title", null, null, "Body", [])))
            .Should().ThrowAsync<HttpRequestException>();
        await FluentActions.Awaiting(() => client.DeleteAsync(TemplateId, "reset"))
            .Should().ThrowAsync<HttpRequestException>();
        await FluentActions.Awaiting(() => client.GetAuditAsync(
            NotificationTemplateKind.Publish, NotificationChannel.Email)).Should().ThrowAsync<HttpRequestException>();
        await FluentActions.Awaiting(() => client.GetApplicableAsync(GradeLevelId))
            .Should().ThrowAsync<HttpRequestException>();
    }

    // ── Spec divergence guard ───────────────────────────────────────────────

    [TestMethod]
    public async Task MutationsAddressTheRowById_NotTheKindAndChannel()
    {
        // Spec §8.5's table still documents PUT/DELETE …/{kind}/{channel}; round b2c replaced both with
        // by-id routes because the keyed shape could not tell two candidates of one slot apart. This
        // guard keeps the stale shape from creeping back in through the client.
        var (client, handler) = Build(body: TemplateJson);

        await client.UpdateAsync(TemplateId, new UpdateNotificationTemplateRequest("Title", null, null, "Body", []));
        await client.DeleteAsync(TemplateId, "reset");

        var paths = handler.Requests.Select(r => r.RequestUri!.AbsolutePath).ToList();

        paths.Should().HaveCount(2);
        paths.Should().OnlyContain(path => path == $"{BasePath}/{TemplateId:D}",
            "mutations are addressed by id, not by kind × channel");
        paths.Should().NotContain(path => path.Contains("/Publish/Email", StringComparison.Ordinal),
            "the retired (kind, channel)-keyed writer must not reappear");
    }
}
