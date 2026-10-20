using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Application.Components.Pages;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the notification-template editor (route <c>/notification-templates</c>, spec §3.10,
/// round notification-templates-b3b). What is pinned here is the CONTRACT the plan called out:
/// the master–detail slot shape (row inventory + one editing pane), the three presentational states
/// (default row selected on tab entry / empty slot / all-constrained slot), create-vs-update-by-id,
/// reset-by-id with the operator's reason, the §3.7 conflict warning with both negatives, the SMS
/// length guidance including its degraded fallback, tag chips, the exact sanitize-on-write, the live
/// preview's unknown-tag and degradation behaviour, the audit list, and the <c>?grade=</c> receiving
/// end.
/// <para>
/// The tab seam is FluentTabs' own two-way binder, invoked on <see cref="FluentTabStrip"/> (the
/// chrome forwards it): a web component's tab click is browser JS, so this is the component-level
/// equivalent — the same seam the suite already uses for <c>ValueChanged</c> on inputs.
/// </para>
/// </summary>
[TestClass]
public class NotificationTemplatesPageTests : BunitContext
{
    private const string BasePath = "/api/settings/notification-templates";

    private static readonly Guid GradeA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid GradeB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid DefaultRowId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid ConstrainedRowId = Guid.Parse("22222222-0000-0000-0000-000000000002");

    public NotificationTemplatesPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class Row
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        public int Kind { get; init; }

        public int Channel { get; init; }

        public string? Subject { get; set; }

        public string Body { get; set; } = string.Empty;

        public List<(int Dimension, string Value)> Constraints { get; init; } = [];

        public DateTimeOffset CreatedAt { get; init; } = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UpdatedAt { get; set; } = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class TemplateStoreHandler : HttpMessageHandler
    {
        public List<Row> Rows { get; } = [];

        public List<(string Method, string Url, string? Body)> Calls { get; } = [];

        public string AuditJson { get; set; } = "[]";

        public bool PreviewFails { get; set; }

        public string PreviewJson { get; set; } =
            """{ "subject": "Sample subject: Grade 8 Science", "body": "<p>Sample body — {{Title}}</p>" }""";

        public bool LookupsFail { get; set; }

        private static readonly string GradeLevelsJson = $$"""
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
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((method, url, body));

            if (path.StartsWith(BasePath, StringComparison.Ordinal))
                return HandleTemplates(method, path, body);

            if (path is "/students/grade-levels" or "/students/topics" or "/activity-groups"
                or "/api/coded-values/by-parent")
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
            if (method == "GET" && path.EndsWith("/audit", StringComparison.Ordinal))
                return Json(AuditJson);

            if (method == "GET" && path == BasePath)
            {
                return Json(JsonSerializer.Serialize(
                    Rows.Select(ToWire).ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            }

            if (method == "POST")
            {
                using var doc = JsonDocument.Parse(body!);
                var root = doc.RootElement;
                var row = new Row
                {
                    Kind = root.GetProperty("kind").GetInt32(),
                    Channel = root.GetProperty("channel").GetInt32(),
                    Subject = root.TryGetProperty("subject", out var subject) && subject.ValueKind != JsonValueKind.Null
                        ? subject.GetString()
                        : null,
                    Body = root.GetProperty("body").GetString() ?? string.Empty,
                    Constraints = root.GetProperty("constraints").EnumerateArray()
                        .Select(c => (c.GetProperty("dimension").GetInt32(), c.GetProperty("value").GetString() ?? string.Empty))
                        .ToList(),
                };

                Rows.Add(row);
                return Json(JsonSerializer.Serialize(
                    ToWire(row), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            }

            if (method == "PUT")
            {
                var id = Guid.Parse(path[(path.LastIndexOf('/') + 1)..]);
                var row = Rows.Single(r => r.Id == id);

                using var doc = JsonDocument.Parse(body!);
                var root = doc.RootElement;
                if (root.TryGetProperty("subject", out var subject) && subject.ValueKind != JsonValueKind.Null)
                    row.Subject = subject.GetString();
                row.Body = root.GetProperty("body").GetString() ?? row.Body;
                row.UpdatedAt = row.UpdatedAt.AddMinutes(1);

                return Json(JsonSerializer.Serialize(
                    ToWire(row), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            }

            if (method == "DELETE")
            {
                var id = Guid.Parse(path[(path.LastIndexOf('/') + 1)..]);
                Rows.RemoveAll(r => r.Id == id);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return Status(HttpStatusCode.MethodNotAllowed, $"Unexpected {method} {path}");
        }

        private static object ToWire(Row row) => new
        {
            id = row.Id,
            kind = row.Kind,
            channel = row.Channel,
            subject = row.Subject,
            body = row.Body,
            constraints = row.Constraints.Select(c => new
            {
                id = Guid.NewGuid(),
                dimension = c.Dimension,
                value = c.Value,
            }),
            createdAt = row.CreatedAt,
            updatedAt = row.UpdatedAt,
        };

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Status(HttpStatusCode status, string message = "") =>
            new(status) { Content = new StringContent(message, Encoding.UTF8, "text/plain") };
    }

    private TemplateStoreHandler Register()
    {
        var handler = new TemplateStoreHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton(new NotificationTemplateApiClient(http));
        Services.AddSingleton(new NotificationTemplatePreviewClient(http));
        Services.AddSingleton(new StudentsLookupClient(http));
        Services.AddSingleton(new CodedValuesApiClient(http));
        Services.AddSingleton<ILogger<NotificationTemplates>>(NullLogger<NotificationTemplates>.Instance);
        return handler;
    }

    private IRenderedComponent<NotificationTemplates> RenderPage(string? grade = null)
    {
        if (grade is not null)
        {
            // A [SupplyParameterFromQuery] value cannot be seeded as a component parameter (bUnit
            // rejects it deliberately): it arrives from the URL, so the test navigates first.
            Services.GetRequiredService<NavigationManager>()
                .NavigateTo($"/notification-templates?grade={grade}");
        }

        return Render<NotificationTemplates>();
    }

    private static async Task SwitchChannelAsync(
        IRenderedComponent<NotificationTemplates> cut, NotificationChannel channel)
    {
        var inner = cut.FindComponents<FluentTabStrip>()[1];
        await cut.InvokeAsync(() =>
            inner.Instance.ActiveTabIdChanged.InvokeAsync($"nt-channel-{channel}"));
    }

    private static async Task SwitchKindAsync(
        IRenderedComponent<NotificationTemplates> cut, NotificationTemplateKind kind)
    {
        var outer = cut.FindComponents<FluentTabStrip>()[0];
        await cut.InvokeAsync(() =>
            outer.Instance.ActiveTabIdChanged.InvokeAsync($"nt-kind-{kind}"));
    }

    private static void ClickButton(IRenderedComponent<NotificationTemplates> cut, string text) =>
        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == text).Click();

    private static string? RequestBody(string? requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson!);
        return doc.RootElement.TryGetProperty("body", out var body) ? body.GetString() : null;
    }

    private static IRenderedComponent<FluentTextArea> SmsBody(IRenderedComponent<NotificationTemplates> cut) =>
        cut.FindComponents<FluentTextArea>().Single(t => t.Instance.Label == "Message");

    private static IRenderedComponent<FluentTextInput> Input(IRenderedComponent<NotificationTemplates> cut, string label) =>
        cut.FindComponents<FluentTextInput>().Single(t => t.Instance.Label == label);

    // ── Presentational states (selection contract) ─────────────────────────────

    [TestMethod]
    public void TabEntry_SelectsTheDefaultRow_WhenTheSlotHasOne()
    {
        var handler = Register();
        handler.Rows.Add(new Row { Id = DefaultRowId, Kind = 0, Channel = 0, Body = "<p>DEFAULT-BODY</p>" });
        handler.Rows.Add(new Row
        {
            Id = ConstrainedRowId, Kind = 0, Channel = 0, Subject = "Grade 3 candidate",
            Constraints = [(0, GradeB.ToString("D"))],
        });

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            cut.Find($"[data-row='{DefaultRowId}']").TextContent.Should().Contain("Editing",
                "on entering a tab the tenant default is the selected row");
            cut.Find($"[data-row='{ConstrainedRowId}']").TextContent.Should().NotContain("Editing");
            cut.Markup.Should().Contain("Edit template");
            cut.Find($"[data-row='{DefaultRowId}']").TextContent.Should().Contain("Default");
        });
    }

    [TestMethod]
    public void EmptySlot_SaysTheBuiltInDefaultApplies_AndOffersTheAddAffordance()
    {
        Register();

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("the built-in default applies");
            cut.FindAll("fluent-button").Select(b => b.TextContent.Trim()).Should().Contain("Add template",
                "an empty slot offers the add affordance");
            cut.Markup.Should().Contain("New template", "…and starts in the add state");
        });
    }

    [TestMethod]
    public void AllConstrainedSlot_SaysThereIsNoTenantDefault()
    {
        var handler = Register();
        handler.Rows.Add(new Row
        {
            Id = ConstrainedRowId, Kind = 0, Channel = 0, Subject = "Grade 3 only",
            Constraints = [(0, GradeB.ToString("D"))],
        });

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("No tenant default",
                "a slot with only constrained rows has no tenant default; the built-in default covers unconstrained sends"));
    }

    // ── Save: create vs update-by-id ────────────────────────────────────────────

    [TestMethod]
    public async Task Save_CreatesWhenNothingIsSelected()
    {
        var handler = Register();
        var cut = RenderPage();
        await SwitchChannelAsync(cut, NotificationChannel.SMS);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("New template"));

        await cut.InvokeAsync(() => SmsBody(cut).Instance.ValueChanged.InvokeAsync("New SMS body: {{Title}}"));
        ClickButton(cut, "Save");

        cut.WaitForAssertion(() =>
        {
            var post = handler.Calls.Single(c => c.Method == "POST");
            post.Url.Should().Be(BasePath, "create posts to the list path — the row's identity is its id");
            post.Body.Should().Contain("\"body\":\"New SMS body: {{Title}}\"");
            post.Body.Should().Contain("\"kind\":0");
            post.Body.Should().Contain("\"channel\":1", "the SMS channel travels numerically on the Settings client");
            handler.Calls.Should().NotContain(c => c.Method == "PUT");
        });
    }

    [TestMethod]
    public async Task Save_UpdatesTheSelectedRowById()
    {
        var handler = Register();
        handler.Rows.Add(new Row { Id = DefaultRowId, Kind = 0, Channel = 1, Body = "OLD" });
        var cut = RenderPage();
        await SwitchChannelAsync(cut, NotificationChannel.SMS);
        cut.WaitForAssertion(() => SmsBody(cut).Instance.Value.Should().Be("OLD"));

        await cut.InvokeAsync(() => SmsBody(cut).Instance.ValueChanged.InvokeAsync("NEW"));
        ClickButton(cut, "Save");

        cut.WaitForAssertion(() =>
        {
            var put = handler.Calls.Single(c => c.Method == "PUT");
            put.Url.Should().Be($"{BasePath}/{DefaultRowId:D}", "an existing template updates by id");
            put.Body.Should().Contain("\"body\":\"NEW\"");
            handler.Calls.Should().NotContain(c => c.Method == "POST");
        });
    }

    // ── Reset: delete by id with the reason ─────────────────────────────────────

    [TestMethod]
    public async Task Reset_DeletesTheRowById_WithTheOperatorsReason()
    {
        var handler = Register();
        handler.Rows.Add(new Row
        {
            Id = ConstrainedRowId, Kind = 0, Channel = 1, Subject = "SMS candidate", Body = "text",
            Constraints = [(0, GradeB.ToString("D"))],
        });

        var cut = RenderPage();
        await SwitchChannelAsync(cut, NotificationChannel.SMS);
        cut.WaitForAssertion(() => cut.Find($"[data-row='{ConstrainedRowId}']").Should().NotBeNull());

        await cut.InvokeAsync(() =>
            Input(cut, "Reason (recorded in the audit trail)").Instance.ValueChanged.InvokeAsync("reset after review"));
        cut.Find($"[data-row='{ConstrainedRowId}']").QuerySelectorAll("fluent-button")
            .Single(b => b.TextContent.Trim() == "Reset").Click();

        cut.WaitForAssertion(() =>
        {
            var delete = handler.Calls.Single(c => c.Method == "DELETE");
            delete.Url.Should().Be($"{BasePath}/{ConstrainedRowId:D}?reason=reset%20after%20review",
                "a reset is audited, so the operator's reason must reach the endpoint");
        });
    }

    // ── §3.7 conflict warning: one positive, both negatives ────────────────────

    [TestMethod]
    public void ConflictWarning_AppearsForAnIdenticalConstraintSetInTheSameSlot()
    {
        var handler = Register();
        handler.Rows.Add(new Row
        {
            Id = DefaultRowId, Kind = 0, Channel = 0, Subject = "Row A", Constraints = [(0, GradeB.ToString("D"))],
        });
        handler.Rows.Add(new Row
        {
            Id = ConstrainedRowId, Kind = 0, Channel = 0, Subject = "Row B", Constraints = [(0, GradeB.ToString("D"))],
        });

        var cut = RenderPage();
        cut.WaitForAssertion(() => ClickButton(cut, "Row A"));

        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("identical constraint set",
                "two same-slot templates with the same constraints cannot be told apart at selection"));
    }

    [TestMethod]
    public void ConflictWarning_IsSilentForADifferentConstraintSet()
    {
        var handler = Register();
        handler.Rows.Add(new Row
        {
            Id = DefaultRowId, Kind = 0, Channel = 0, Subject = "Row A", Constraints = [(0, GradeB.ToString("D"))],
        });
        handler.Rows.Add(new Row
        {
            Id = ConstrainedRowId, Kind = 0, Channel = 0, Subject = "Row B",
            Constraints = [(2, "dddddddd-0000-0000-0000-00000000000d")],
        });

        var cut = RenderPage();
        cut.WaitForAssertion(() => ClickButton(cut, "Row A"));

        cut.WaitForAssertion(() =>
            cut.Markup.Should().NotContain("identical constraint set",
                "different constraint sets are the intended way to hold several candidates"));
    }

    [TestMethod]
    public void ConflictWarning_IsSilentForAnIdenticalSetInAnotherSlot()
    {
        var handler = Register();
        handler.Rows.Add(new Row
        {
            Id = DefaultRowId, Kind = 0, Channel = 0, Subject = "Row A", Constraints = [(0, GradeB.ToString("D"))],
        });
        handler.Rows.Add(new Row
        {
            Id = ConstrainedRowId, Kind = 1, Channel = 0, Subject = "Row B", Constraints = [(0, GradeB.ToString("D"))],
        });

        var cut = RenderPage();
        cut.WaitForAssertion(() => ClickButton(cut, "Row A"));

        cut.WaitForAssertion(() =>
            cut.Markup.Should().NotContain("identical constraint set",
                "the ambiguity is per kind × channel — the same set in another slot is two different messages"));
    }

    // ── SMS length guidance: warn, never block; degrade to the raw length ──────

    [TestMethod]
    public async Task SmsHint_WarnsOverTheLimit_AndNeverBlocksSave()
    {
        var handler = Register();
        handler.Rows.Add(new Row { Id = DefaultRowId, Kind = 0, Channel = 1, Body = new string('x', 200) });
        handler.PreviewJson = $$"""{ "subject": "s", "body": "{{new string('y', 200)}}" }""";

        var cut = RenderPage();
        await SwitchChannelAsync(cut, NotificationChannel.SMS);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Over 160 characters", "the rendered length crosses the single-segment limit");
            cut.Markup.Should().Contain("rendered length", "the hint is based on the rendered text");
        }, TimeSpan.FromSeconds(5));

        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Save")
            .HasAttribute("disabled").Should().BeFalse("the length hint is guidance, never a hard block");
    }

    [TestMethod]
    public async Task SmsHint_FallsBackToTheAuthoredLength_WhenThePreviewIsUnavailable()
    {
        var handler = Register();
        handler.Rows.Add(new Row { Id = DefaultRowId, Kind = 0, Channel = 1, Body = "A short body" });
        handler.PreviewFails = true;

        var cut = RenderPage();
        await SwitchChannelAsync(cut, NotificationChannel.SMS);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("authored length — rendered length unavailable",
                "the hint must not disappear when the preview is degraded");
            cut.Markup.Should().Contain("12 characters", "the raw authored length is still useful guidance");
        }, TimeSpan.FromSeconds(5));
    }

    // ── Tag chips ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task TagChips_RenderTheSpecSet_AndAppendIntoTheSubjectAndSmsBody()
    {
        var handler = Register();
        handler.Rows.Add(new Row { Id = DefaultRowId, Kind = 0, Channel = 0, Body = "<p>body</p>" });

        var cut = RenderPage();
        cut.WaitForAssertion(() =>
        {
            var chips = cut.FindAll("fluent-button").Select(b => b.TextContent.Trim()).ToHashSet();
            chips.Should().Contain(["{{Title}}", "{{Description}}", "{{DueDate}}", "{{Instructions}}",
                "{{StudentName}}", "{{GuardianName}}", "{{GuardianRole}}", "{{SchoolName}}", "{{Link}}",
                "{{LinkExpiry}}"]);
        });

        ClickButton(cut, "{{Title}}");
        cut.WaitForAssertion(() =>
            Input(cut, "Subject").Instance.Value.Should().Be("{{Title}}",
                "the email chips append to the subject (the body is the rich editor's own surface)"));

        await SwitchChannelAsync(cut, NotificationChannel.SMS);
        cut.WaitForAssertion(() => SmsBody(cut).Should().NotBeNull());
        ClickButton(cut, "{{DueDate}}");
        cut.WaitForAssertion(() =>
            SmsBody(cut).Instance.Value.Should().Be("{{DueDate}}",
                "the SMS chips append into the message body"));
    }

    // ── Email body: read at submit through the editor's interop, sanitized once ─

    [TestMethod]
    public void Save_EmailBody_ReadsTheEditorOverInterop_AndSanitizesTheStoredHtml()
    {
        var handler = Register();
        handler.Rows.Add(new Row
        {
            Id = DefaultRowId, Kind = 0, Channel = 0, Body = "<p>Hi<script>alert(1)</script></p>",
        });

        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Edit template"));
        ClickButton(cut, "Save");

        cut.WaitForAssertion(() =>
        {
            JSInterop.Invocations.Should().Contain(i => i.Identifier == "import",
                "the email body is read from the shared rich editor at submit — the wrapper's own interop, "
                + "not a second editor or a second sanitizer");
            var put = handler.Calls.Single(c => c.Method == "PUT");
            RequestBody(put.Body).Should().Be("<p>Hi</p>",
                "the authored markup survives the ONE instruction sanitizer, and the script element does not");
        });
    }

    // ── Live preview: rendered output, unknown tag literal, degradation ────────

    [TestMethod]
    public void Preview_ShowsTheRenderedPair_IncludingAnUnknownTagLiteral()
    {
        var handler = Register();
        handler.Rows.Add(new Row { Id = DefaultRowId, Kind = 0, Channel = 0, Body = "<p>{{Title}}</p>" });
        handler.PreviewJson = """{ "subject": "Rendered subject", "body": "<p>Hello {{NoSuchTag}}</p>" }""";

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Rendered subject");
            cut.Markup.Should().Contain("{{NoSuchTag}}",
                "an unknown tag stays literal in the preview (spec §3.2)");
        }, TimeSpan.FromSeconds(5));
        handler.Calls.Should().Contain(c =>
            c.Method == "POST" && c.Url == "/assignments/notification-templates/preview");
    }

    [TestMethod]
    public async Task PreviewFailure_ShowsTheNote_AndSaveStillWorks()
    {
        var handler = Register();
        handler.Rows.Add(new Row { Id = DefaultRowId, Kind = 0, Channel = 1, Body = "OLD" });
        handler.PreviewFails = true;

        var cut = RenderPage();
        await SwitchChannelAsync(cut, NotificationChannel.SMS);

        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("Preview unavailable", "a dead preview degrades to an inline note"),
            TimeSpan.FromSeconds(5));

        await cut.InvokeAsync(() => SmsBody(cut).Instance.ValueChanged.InvokeAsync("STILL SAVES"));
        ClickButton(cut, "Save");

        cut.WaitForAssertion(() =>
            handler.Calls.Should().Contain(c =>
                c.Method == "PUT" && c.Body!.Contains("STILL SAVES"),
                "the preview is an enrichment read — it can never block the save path"));
    }

    // ── Audit panel ────────────────────────────────────────────────────────────

    [TestMethod]
    public void AuditPanel_ListsTheSelectedSlotsEntries()
    {
        var handler = Register();
        handler.Rows.Add(new Row { Id = DefaultRowId, Kind = 0, Channel = 0, Body = "<p>b</p>" });
        handler.AuditJson = """
            [{
              "id": "99999999-0000-0000-0000-000000000009",
              "changeKind": 1,
              "previousSubject": "old",
              "newSubject": "new",
              "previousBody": null,
              "newBody": "body",
              "previousConstraints": null,
              "newConstraints": "[]",
              "reason": "initial seed",
              "actorId": "test:actor",
              "actorDisplayName": "Test Operator",
              "occurredAt": "2026-10-19T00:00:00+00:00"
            }]
            """;

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("initial seed");
            cut.Markup.Should().Contain("Test Operator");
        });
    }

    // ── Tab switching loads that slot's inventory (the master–detail shape) ────

    [TestMethod]
    public async Task TabSwitch_RendersThatSlotsRowInventory_AndEveryKindAndChannelStaysReachable()
    {
        var handler = Register();
        handler.Rows.Add(new Row { Id = DefaultRowId, Kind = 0, Channel = 0, Body = "<p>PUBLISH-BODY</p>" });
        handler.Rows.Add(new Row { Id = ConstrainedRowId, Kind = 1, Channel = 0, Body = "<p>REMINDER-BODY</p>" });

        var cut = RenderPage();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("PUBLISH-BODY");
            cut.Markup.Should().NotContain("REMINDER-BODY", "only the active slot's pane renders");
        });

        await SwitchKindAsync(cut, NotificationTemplateKind.Reminder);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("REMINDER-BODY");
            cut.Markup.Should().NotContain("PUBLISH-BODY");
        });

        foreach (var header in new[] { "Publish", "Reminder", "Completion", "Overdue", "Email", "SMS" })
            cut.Markup.Should().Contain(header, "the 4×2 tab shell never narrows");
    }

    // ── ?grade= receiving end (§3.11) ──────────────────────────────────────────

    [TestMethod]
    public async Task GradeFilter_HidesAnotherGradesRow_KeepsEveryTab_AndPrefillsTheConstraint()
    {
        var handler = Register();
        handler.Rows.Add(new Row
        {
            Id = DefaultRowId, Kind = 0, Channel = 0,
            Body = "<p>OTHER-GRADE-ROW</p>", Constraints = [(0, GradeA.ToString("D"))],
        });
        handler.Rows.Add(new Row
        {
            Id = ConstrainedRowId, Kind = 0, Channel = 0,
            Body = "<p>MY-GRADE-ROW</p>", Constraints = [(0, GradeB.ToString("D"))],
        });

        var cut = RenderPage(grade: GradeB.ToString("D"));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("MY-GRADE-ROW");
            cut.Markup.Should().NotContain("OTHER-GRADE-ROW",
                "the filter narrows each tab's row inventory to the templates that can apply to this grade");
            cut.Markup.Should().Contain("Grade level: Grade 3",
                "the GradeLevel constraint is pre-filled so a grade-specific template is one click away");
        });

        await SwitchChannelAsync(cut, NotificationChannel.SMS);
        cut.WaitForAssertion(() =>
            cut.FindAll("fluent-tab").Select(t => t.TextContent.Trim())
                .Should().Contain(["Publish", "Reminder", "Completion", "Overdue", "Email", "SMS"],
                    "the filter never removes a tab"));
    }
}
