using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Wire contract of <see cref="NotificationTemplatePreviewClient"/> (round notification-templates-b3b):
/// the path/method of the Assignments host's preview route, and — the point of this client — that both
/// enums travel as NAMES. The host registers matching converters in its real
/// <c>ConfigureHttpJsonOptions</c> block; a client that emitted the numeric value would 400 there,
/// and the editor would degrade to "preview unavailable" forever.
/// </summary>
[TestClass]
public class NotificationTemplatePreviewClientTests
{
    private const string PreviewPath = "/assignments/notification-templates/preview";

    private static readonly string RenderedJson = """
        { "subject": "Reminder: Water cycle", "body": "Open https://families.example/l/SAMPLE01" }
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

    private static (NotificationTemplatePreviewClient Client, RecordingHandler Handler) Build(
        HttpStatusCode status = HttpStatusCode.OK, string body = "{}")
    {
        var handler = new RecordingHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        return (new NotificationTemplatePreviewClient(http), handler);
    }

    [TestMethod]
    public async Task PreviewAsync_PostsTheAuthoredPair_ToTheAssignmentsPreviewRoute()
    {
        var (client, handler) = Build(body: RenderedJson);

        var rendered = await client.PreviewAsync(
            NotificationTemplateKind.Reminder,
            NotificationChannel.SMS,
            subject: null,
            body: "Reminder: {{Title}} — {{Link}}");

        handler.Requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be(PreviewPath);

        // Parsed, not substring-matched: the default encoder escapes non-ASCII (an em dash becomes
        // \u2014), which says nothing about what the host will deserialize.
        using var request = JsonDocument.Parse(handler.Bodies[0]);
        request.RootElement.GetProperty("body").GetString()
            .Should().Be("Reminder: {{Title}} — {{Link}}");
        rendered.Subject.Should().Be("Reminder: Water cycle");
        rendered.Body.Should().Contain("/l/SAMPLE01");
    }

    [TestMethod]
    public async Task PreviewAsync_TravelsBothEnumsByName()
    {
        var (client, handler) = Build(body: RenderedJson);

        await client.PreviewAsync(
            NotificationTemplateKind.Completion,
            NotificationChannel.Email,
            "Done: {{Title}}",
            "<p>{{Title}} is complete.</p>");

        handler.Bodies[0].Should().Contain("\"kind\":\"Completion\"",
            "the Assignments host binds the preview request's enums by NAME (its Program.cs registers "
            + "the matching converters); a numeric value is a live 400");
        handler.Bodies[0].Should().Contain("\"channel\":\"Email\"");
        handler.Bodies[0].Should().Contain("\"subject\":\"Done: {{Title}}\"");
    }

    [TestMethod]
    public async Task PreviewAsync_ThrowsOnANonSuccessResponse()
    {
        // The page owns the degradation ("preview unavailable", save unaffected) — the client must
        // surface the failure rather than silently returning a fabricated render.
        var (client, _) = Build(HttpStatusCode.InternalServerError, "boom");

        await FluentActions.Awaiting(() => client.PreviewAsync(
                NotificationTemplateKind.Publish, NotificationChannel.Email, null, "body"))
            .Should().ThrowAsync<HttpRequestException>();
    }
}
