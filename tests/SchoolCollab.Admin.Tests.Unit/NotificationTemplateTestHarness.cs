using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>One stored template row, as the fake Settings API serves it.</summary>
internal sealed class TemplateRow
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int Kind { get; init; }
    public int Channel { get; init; }
    public string? Subject { get; init; }
    public string Body { get; init; } = "<p>Body</p>";
    public List<(int Dimension, string Value)> Constraints { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; } = new(2026, 10, 1, 9, 30, 0, TimeSpan.Zero);
}

/// <summary>
/// The W1 fake backend: the template store routes, the four lookup reads the constraint vocabulary
/// needs, and the Assignments preview seam — the same surface the deleted page suite faked, so the new
/// suites assert behaviour through the same external seam rather than a new one.
/// </summary>
internal sealed class TemplateApiHandler : HttpMessageHandler
{
    public const string BasePath = "/api/settings/notification-templates";
    public const string GradeA = "aaaaaaaa-0000-0000-0000-00000000000a";
    public const string GradeB = "bbbbbbbb-0000-0000-0000-00000000000b";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public List<TemplateRow> Rows { get; } = [];
    public List<(string Method, string Url, string? Body)> Calls { get; } = [];
    public bool PreviewFails { get; set; }
    public bool LookupsFail { get; set; }
    public string PreviewJson { get; set; } =
        """{ "subject": "Sample subject: Grade 8 Science", "body": "<p>Rendered body</p>" }""";

    private static readonly string GradeLevelsJson =
        $$"""
        [
          { "id": "{{GradeA}}", "name": "Grade 2" },
          { "id": "{{GradeB}}", "name": "Grade 3" }
        ]
        """;

    private const string TopicsJson = """
        [{ "id": "cccccccc-0000-0000-0000-00000000000c", "name": "Mathematics" }]
        """;

    private const string ActivityGroupsJson = """
        [{ "id": "dddddddd-0000-0000-0000-00000000000d", "name": "Chess club", "isActive": true }]
        """;

    private const string StreamsJson = """
        [{
          "id": "eeeeeeee-0000-0000-0000-00000000000e", "code": "NORTH", "name": "North",
          "description": null, "parentId": null, "parentCode": "GRSTREAMS", "isDisabled": false,
          "displayOrder": 0, "createdAt": "2026-10-01T00:00:00+00:00", "updatedAt": "2026-10-01T00:00:00+00:00",
          "attributes": [], "attributeDefinitions": []
        }]
        """;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var url = request.RequestUri.PathAndQuery;
        var method = request.Method.Method;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Add((method, url, body));

        if (path.StartsWith(BasePath, StringComparison.Ordinal))
            return HandleTemplates(method, path, body);

        if (path is "/students/grade-levels" or "/students/topics" or "/activity-groups" or "/api/coded-values/by-parent")
        {
            if (LookupsFail)
                return Status(HttpStatusCode.InternalServerError, "lookup down");

            return Json(path switch
            {
                "/students/grade-levels" => GradeLevelsJson,
                "/students/topics" => TopicsJson,
                "/activity-groups" => ActivityGroupsJson,
                _ => StreamsJson,
            });
        }

        if (path == "/assignments/notification-templates/preview")
        {
            return PreviewFails
                ? Status(HttpStatusCode.InternalServerError, "preview down")
                : Json(PreviewJson);
        }

        return Status(HttpStatusCode.NotFound, $"Unexpected {method} {url}");
    }

    private HttpResponseMessage HandleTemplates(string method, string path, string? body)
    {
        if (method == "GET" && path == BasePath)
            return Json(JsonSerializer.Serialize(Rows.Select(ToWire).ToArray(), Web));

        if (method == "GET" && path.Length > BasePath.Length + 1
            && Guid.TryParse(path[(BasePath.Length + 1)..], out var id))
        {
            var row = Rows.FirstOrDefault(r => r.Id == id);
            return row is null
                ? Status(HttpStatusCode.NotFound, "not found")
                : Json(JsonSerializer.Serialize(ToWire(row), Web));
        }

        if (method == "POST")
        {
            using var doc = JsonDocument.Parse(body!);
            var root = doc.RootElement;
            var created = new TemplateRow
            {
                Kind = root.GetProperty("kind").GetInt32(),
                Channel = root.GetProperty("channel").GetInt32(),
                Subject = root.TryGetProperty("subject", out var subject) && subject.ValueKind != JsonValueKind.Null
                    ? subject.GetString()
                    : null,
                Body = root.GetProperty("body").GetString() ?? string.Empty,
                Constraints = root.TryGetProperty("constraints", out var constraints)
                    ? constraints.EnumerateArray()
                        .Select(c => (c.GetProperty("dimension").GetInt32(), c.GetProperty("value").GetString() ?? string.Empty))
                        .ToList()
                    : [],
            };
            Rows.Add(created);
            return Json(JsonSerializer.Serialize(ToWire(created), Web));
        }

        return Status(HttpStatusCode.NotFound, $"Unexpected {method} {path}");
    }

    private static object ToWire(TemplateRow row) => new
    {
        id = row.Id,
        kind = row.Kind,
        channel = row.Channel,
        subject = row.Subject,
        body = row.Body,
        constraints = row.Constraints.Select(c => new { id = Guid.NewGuid(), dimension = c.Dimension, value = c.Value }),
        createdAt = row.UpdatedAt,
        updatedAt = row.UpdatedAt,
    };

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Status(HttpStatusCode status, string message = "") =>
        new(status) { Content = new StringContent(message, Encoding.UTF8, "text/plain") };
}

/// <summary>Registers the four admin clients and the console logger every page under test injects.</summary>
internal static class TemplateTestServices
{
    public static TemplateApiHandler Register<TComponent>(BunitContext ctx)
    {
        var handler = new TemplateApiHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        ctx.Services.AddSingleton(new NotificationTemplateApiClient(http));
        ctx.Services.AddSingleton(new NotificationTemplatePreviewClient(http));
        ctx.Services.AddSingleton(new StudentsLookupClient(http));
        ctx.Services.AddSingleton(new CodedValuesApiClient(http));
        ctx.Services.AddSingleton<Microsoft.Extensions.Logging.ILogger<TComponent>>(
            NullLogger<TComponent>.Instance);
        return handler;
    }
}