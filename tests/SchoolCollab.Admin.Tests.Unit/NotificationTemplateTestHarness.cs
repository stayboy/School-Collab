using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
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

    /// <summary>
    /// The owning tenant, or null for a system-blueprint row (redesign spec §3.7). Defaults to the
    /// session's own tenant, because a fixture row is normally one the page's operator can edit; a
    /// blueprint row is always explicit (<c>TenantId = null</c>).
    /// </summary>
    public Guid? TenantId { get; init; } = TemplateTestServices.RealTenant;

    /// <summary>The owner label the API projects ("System" or the tenant's name).</summary>
    public string OwnerName { get; init; } = "Northfield";
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
    public List<TemplateAuditRow> AuditEntries { get; } = [];

    /// <summary>The tenant an adopt copies into (the fake session's own tenant).</summary>
    public Guid SessionTenantId { get; set; } = TemplateTestServices.RealTenant;

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
            return HandleTemplates(method, path, url, body);

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

    private HttpResponseMessage HandleTemplates(string method, string path, string url, string? body)
    {
        if (method == "GET" && path == BasePath)
            return Json(JsonSerializer.Serialize(Rows.Select(ToWire).ToArray(), Web));

        // Adopt / promote (redesign spec §3.8): a copy with the same content, in the target scope.
        if (method == "POST" && TryCopyAction(path, out var copyId, out var action))
        {
            var source = Rows.FirstOrDefault(r => r.Id == copyId);
            if (source is null)
                return Status(HttpStatusCode.NotFound, "not found");

            var intoTenant = action == "adopt";
            var copy = new TemplateRow
            {
                Kind = source.Kind,
                Channel = source.Channel,
                Subject = source.Subject,
                Body = source.Body,
                Constraints = source.Constraints.ToList(),
                TenantId = intoTenant ? SessionTenantId : null,
                OwnerName = intoTenant ? "Northfield" : "System",
            };
            Rows.Add(copy);
            return Json(JsonSerializer.Serialize(ToWire(copy), Web));
        }

        if (method == "GET" && path.Length > BasePath.Length + 1
            && Guid.TryParse(path[(BasePath.Length + 1)..], out var id))
        {
            var row = Rows.FirstOrDefault(r => r.Id == id);
            return row is null
                ? Status(HttpStatusCode.NotFound, "not found")
                : Json(JsonSerializer.Serialize(ToWire(row), Web));
        }

        if (method == "GET" && path.EndsWith("/audit", StringComparison.Ordinal))
        {
            var templateId = QueryGuid(url, "templateId");
            var rows = AuditEntries
                .Where(a => templateId is null || a.TemplateId == templateId)
                .Select(a => new
                {
                    id = Guid.NewGuid(),
                    templateId = a.TemplateId,
                    changeKind = a.ChangeKind,
                    previousSubject = (string?)null,
                    newSubject = (string?)null,
                    previousBody = (string?)null,
                    newBody = (string?)null,
                    previousConstraints = (string?)null,
                    newConstraints = (string?)null,
                    reason = a.Reason,
                    actorId = "test:actor",
                    actorDisplayName = a.Actor,
                    occurredAt = a.OccurredAt,
                });
            return Json(JsonSerializer.Serialize(rows, Web));
        }

        if (method == "PUT" && path.Length > BasePath.Length + 1
            && Guid.TryParse(path[(BasePath.Length + 1)..], out var putId))
        {
            var index = Rows.FindIndex(r => r.Id == putId);
            if (index < 0)
                return Status(HttpStatusCode.NotFound, "not found");

            using var doc = JsonDocument.Parse(body!);
            var root = doc.RootElement;
            var existing = Rows[index];
            var updated = new TemplateRow
            {
                Id = existing.Id,
                Kind = existing.Kind,
                Channel = existing.Channel,
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
            Rows[index] = updated;
            return Json(JsonSerializer.Serialize(ToWire(updated), Web));
        }

        if (method == "DELETE" && path.Length > BasePath.Length + 1
            && Guid.TryParse(path[(BasePath.Length + 1)..], out var deleteId))
        {
            Rows.RemoveAll(r => r.Id == deleteId);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
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


    /// <summary>
    /// Recognises <c>/notification-templates/{id}/adopt</c> and <c>.../promote</c> (redesign spec §3.8).
    /// </summary>
    private static bool TryCopyAction(string path, out Guid id, out string action)
    {
        id = Guid.Empty;
        action = string.Empty;

        if (path.Length <= BasePath.Length + 2)
            return false;

        var segments = path[(BasePath.Length + 1)..].Split('/');
        if (segments.Length != 2 || !Guid.TryParse(segments[0], out id))
            return false;

        action = segments[1];
        return action is "adopt" or "promote";
    }

    private static Guid? QueryGuid(string url, string name)
    {
        var queryIndex = url.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex < 0)
            return null;

        foreach (var pair in url[(queryIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == name && Guid.TryParse(parts[1], out var value))
                return value;
        }

        return null;
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
        tenantId = row.TenantId,
        ownerName = row.OwnerName,
    };

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Status(HttpStatusCode status, string message = "") =>
        new(status) { Content = new StringContent(message, Encoding.UTF8, "text/plain") };
}

/// <summary>
/// The tenant scope the fake session reports (redesign spec §3.7): the landing and the editor read it to
/// decide which row actions are offered. A null tenant means the system (blueprint) session.
/// </summary>
internal sealed class TemplateTestAuth(Guid? tenantId) : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var identity = new ClaimsIdentity(authenticationType: "test");
        if (tenantId is { } id)
        {
            identity.AddClaim(new Claim("tenant_id", id.ToString("D")));
            identity.AddClaim(new Claim("tenant_name", "Northfield"));
        }

        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }
}

/// <summary>Registers the four admin clients, the tenant scope and the console logger every page injects.</summary>
internal static class TemplateTestServices
{
    /// <summary>The tenant a real (non-system) test session operates in.</summary>
    public static readonly Guid RealTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>
    /// Registers the page's dependencies. <paramref name="realTenant"/> selects the session scope: a real
    /// tenant (the ordinary admin session, which may adopt) or the system blueprint session.
    /// </summary>
    public static TemplateApiHandler Register<TComponent>(BunitContext ctx, bool realTenant = true)
    {
        var sessionTenant = realTenant ? RealTenant : (Guid?)null;
        var handler = new TemplateApiHandler { SessionTenantId = sessionTenant ?? RealTenant };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        ctx.Services.AddSingleton(new NotificationTemplateApiClient(http));
        ctx.Services.AddSingleton(new NotificationTemplatePreviewClient(http));
        ctx.Services.AddSingleton(new StudentsLookupClient(http));
        ctx.Services.AddSingleton(new CodedValuesApiClient(http));
        ctx.Services.AddSingleton<Microsoft.Extensions.Logging.ILogger<TComponent>>(
            NullLogger<TComponent>.Instance);

        var auth = new TemplateTestAuth(sessionTenant);
        ctx.Services.AddSingleton<AuthenticationStateProvider>(auth);
        ctx.Services.AddSingleton(new VisibleTenantService(
            auth, NullLogger<VisibleTenantService>.Instance));

        return handler;
    }
}

/// <summary>One audit row the fake Settings API serves for the per-template trail.</summary>
internal sealed record TemplateAuditRow(
    Guid TemplateId, int ChangeKind, string? Reason, string Actor, DateTimeOffset OccurredAt);
