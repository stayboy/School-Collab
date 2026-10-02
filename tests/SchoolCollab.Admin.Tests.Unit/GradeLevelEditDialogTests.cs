using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;
using TopicDto = SchoolCollab.Students.Core.DTOs.TopicDto;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit smoke tests for <see cref="GradeLevelEditDialog"/>. Mirrors the
/// Create-dialog suite (phase 3) so the Edit dialog's mount + cancel
/// path is exercised through the real
/// <see cref="FluentDialogProvider"/> + <c>DialogService.ShowDialogAsync</c>
/// pipeline. The full Edit submit + subject-diff suite lives in
/// follow-up phase 4.5 tests.
/// </summary>
[TestClass]
public class GradeLevelEditDialogTests : BunitContext
{
    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public GradeLevelEditDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string? Body)> Calls = new();
        private readonly Dictionary<(string Method, string Url), (HttpStatusCode Status, string Body)> _responses = new();

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            _responses[(method.ToUpperInvariant(), url)] = (status, body);
            return this;
        }

        public ScriptedHandler Map(string url, HttpStatusCode status, string body)
            => Map("ANY", url, status, body);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery, body));

            var url = request.RequestUri.PathAndQuery;
            (HttpStatusCode Status, string Body)? found = null;
            if (_responses.TryGetValue((request.Method.Method.ToUpperInvariant(), url), out var exact))
                found = exact;
            else
            {
                foreach (var kv in _responses)
                {
                    if (kv.Key.Method != "ANY") continue;
                    if (url.Equals(kv.Key.Url, StringComparison.OrdinalIgnoreCase) ||
                        url.StartsWith(kv.Key.Url, StringComparison.OrdinalIgnoreCase))
                    {
                        found = kv.Value;
                        break;
                    }
                }
            }
            if (found is { } hit)
            {
                return new HttpResponseMessage(hit.Status)
                {
                    Content = new StringContent(hit.Body, Encoding.UTF8, "application/json"),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected URL: {request.Method.Method} {url}", Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>
    /// Registers the scripted HTTP backend the Edit dialog talks to.
    /// The dialog calls ListTopicsAsync (topic catalog),
    /// CodedValuesApi.GetByIdAsync (the grade coded value), and
    /// ListGradeTopicsByGradeAsync (existing subject assignments).
    /// </summary>
    private ScriptedHandler RegisterFor(
        Guid gradeId,
        Guid codedValueId,
        IEnumerable<(Guid SubjectId, Guid AssignmentId)>? seededAssignments = null)
    {
        var handler = new ScriptedHandler();

        // Topic catalog - empty (no topics in this fixture).
        handler.Map("/students/topics", HttpStatusCode.OK, "[]");

        // CodedValueDropdown parent lookup (no-op when not used).
        handler.Map("/api/coded-values/", HttpStatusCode.OK, "[]");

        // CodedValuesApi.GetByIdAsync for the picked grade coded value -
        // the dialog uses this to capture Level / DisplayOrder.
        handler.Map("GET", "/api/coded-values/", HttpStatusCode.OK,
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["id"] = codedValueId,
                ["code"] = "GRADE5",
                ["name"] = "Grade 5",
                ["description"] = (string?)null,
                ["parentId"] = (Guid?)null,
                ["parentCode"] = (string?)null,
                ["isDisabled"] = false,
                ["displayOrder"] = 5,
                ["createdAt"] = DateTimeOffset.UnixEpoch,
                ["updatedAt"] = DateTimeOffset.UnixEpoch,
                ["attributes"] = Array.Empty<object>(),
                ["attributeDefinitions"] = Array.Empty<object>(),
                ["childrenCount"] = 0,
                ["isDeleted"] = false,
                ["deletedAt"] = (DateTimeOffset?)null,
                ["isOverridden"] = false,
                ["defaultName"] = (string?)null,
            }));

        // ListGradeTopicsByGradeAsync - baseline assignments for the diff.
        var seeded = (seededAssignments ?? []).Select(a => new Dictionary<string, object?>
        {
            ["id"] = a.AssignmentId,
            ["audience"] = "grade",
            ["gradeLevelId"] = gradeId,
            ["activityGroupId"] = (Guid?)null,
            ["topicId"] = a.SubjectId,
            ["startDate"] = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            ["endDate"] = (string?)null,
            ["topicStrandId"] = (Guid?)null,
            ["topicLessonId"] = (Guid?)null,
            ["createdAt"] = DateTimeOffset.UnixEpoch,
            ["updatedAt"] = DateTimeOffset.UnixEpoch,
        }).ToArray();
        handler.Map("GET", "/students/topic-assignments/by-grade/", HttpStatusCode.OK,
            JsonSerializer.Serialize(seeded));

        // Assign / Remove - wired up so deeper phase-4.5 tests can reuse.
        handler.Map("POST", "/students/topic-assignments/grade", HttpStatusCode.Created,
            JsonSerializer.Serialize(new Dictionary<string, object?> { ["id"] = Guid.NewGuid() }));
        handler.Map("DELETE", "/students/topic-assignments/", HttpStatusCode.NoContent, "");
        // PUT grade-level validation fields.
        handler.Map("PUT", "/students/grade-levels/", HttpStatusCode.NoContent, "");

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));

        return handler;
    }

    private IRenderedComponent<FluentDialogProvider> RenderProvider() => Render<FluentDialogProvider>();

    /// <summary>Serializes a topic for the <c>GET /students/topics</c> catalog.</summary>
    private static Dictionary<string, object?> TopicJson(Guid id, string name) => new()
    {
        ["id"] = id,
        ["codedValueId"] = (Guid?)null,
        ["code"] = $"T-{id.ToString()[..4]}",
        ["name"] = name,
        ["description"] = (string?)null,
        ["displayOrder"] = 1,
        ["createdAt"] = DateTimeOffset.UnixEpoch,
        ["updatedAt"] = DateTimeOffset.UnixEpoch,
    };

    /// <summary>Serializes a grade-topic assignment for the by-grade baseline.</summary>
    private static Dictionary<string, object?> AssignmentJson(Guid assignmentId, Guid gradeId, Guid topicId) => new()
    {
        ["id"] = assignmentId,
        ["audience"] = "grade",
        ["gradeLevelId"] = gradeId,
        ["activityGroupId"] = (Guid?)null,
        ["topicId"] = topicId,
        ["startDate"] = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
        ["endDate"] = (string?)null,
        ["topicStrandId"] = (Guid?)null,
        ["topicLessonId"] = (Guid?)null,
        ["createdAt"] = DateTimeOffset.UnixEpoch,
        ["updatedAt"] = DateTimeOffset.UnixEpoch,
    };

    /// <summary>
    /// Serializes the picked grade's coded value for the dialog's
    /// <c>GET /api/coded-values/{id}</c> lookup. RegisterFor keys that route by its bare
    /// prefix, so a test that needs the load to COMPLETE (not just to render the topic
    /// catalog) must register the id-qualified route itself.
    /// </summary>
    private static Dictionary<string, object?> CodedValueJson(Guid id) => new()
    {
        ["id"] = id,
        ["code"] = "GRADE5",
        ["name"] = "Grade 5",
        ["description"] = (string?)null,
        ["parentId"] = (Guid?)null,
        ["parentCode"] = (string?)null,
        ["isDisabled"] = false,
        ["displayOrder"] = 5,
        ["createdAt"] = DateTimeOffset.UnixEpoch,
        ["updatedAt"] = DateTimeOffset.UnixEpoch,
        ["attributes"] = (IReadOnlyCollection<object>?)null,
        ["attributeDefinitions"] = (IReadOnlyCollection<object>?)null,
        ["childrenCount"] = 0,
        ["isDeleted"] = false,
        ["deletedAt"] = (DateTimeOffset?)null,
        ["isOverridden"] = false,
        ["defaultName"] = (string?)null,
    };

    [TestMethod]
    public async Task Edit_Dialog_Renders_GradeName_Pencil_And_ValidationFields()
    {
        var gradeId = Guid.NewGuid();
        var codedValueId = Guid.NewGuid();
        RegisterFor(gradeId, codedValueId);
        var cut = RenderProvider();

        var model = new GradeLevelEditDialog.GradeLevelEditModel
        {
            Id = gradeId,
            CodedValueId = codedValueId,
            CurrentName = "Grade 5",
            MinAge = 10,
            MaxAge = 12,
        };

        var task = DialogService.ShowShellDialogAsync<
            GradeLevelEditDialog,
            GradeLevelEditDialog.GradeLevelEditModel,
            SchoolCollab.Students.Application.Services.GradeLevelDto>(
            model, "Edit grade - Grade 5", DialogSize.Medium);

        // Wait for the EditForm to mount inside the provider.
        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        cut.Markup.Should().Contain("Grade 5", "the dialog shows the resolved grade name");
        cut.Markup.Should().Contain("Age range");
        cut.Markup.Should().Contain("Allowed Gender");
        cut.Markup.Should().Contain("Save"); // submit button label
        cut.Markup.Should().Contain("Override the tenant display name",
            "the pencil icon button is rendered with a tooltip");

        // Cancel so the dialog task completes cleanly.
        var cancelButton = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Cancel"));
        cancelButton.Click();
        var result = await task;
        result.Should().BeNull("cancelling closes the dialog with no result");
    }

    [TestMethod]
    public async Task Edit_Dialog_Renders_Topics_Without_Current_Period()
    {
        // Assignments are date-based, not period-bound, so the Topics section
        // is NOT gated on a current period existing. With an empty topic
        // catalog, the info bar shows.
        var gradeId = Guid.NewGuid();
        var codedValueId = Guid.NewGuid();
        RegisterFor(gradeId, codedValueId);
        var cut = RenderProvider();

        var model = new GradeLevelEditDialog.GradeLevelEditModel
        {
            Id = gradeId,
            CodedValueId = codedValueId,
            CurrentName = "Grade 5",
        };

        var task = DialogService.ShowShellDialogAsync<
            GradeLevelEditDialog,
            GradeLevelEditDialog.GradeLevelEditModel,
            SchoolCollab.Students.Application.Services.GradeLevelDto>(
            model, "Edit grade", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        cut.Markup.Should().Contain("No topics exist yet.",
            "with an empty catalog the Topics info bar shows even without a period");

        var cancelButton = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Cancel"));
        cancelButton.Click();
        var result = await task;
        result.Should().BeNull();
    }

    /// <summary>
    /// Regression (round fluentui-dead-binding): the Topics picker bound the dead
    /// <c>SelectedValues</c> pair — absent from FluentUI 4.14.2 list components — so
    /// Blazor dropped it into the catch-all <c>AdditionalAttributes</c> (a stray
    /// <c>selectedvalues</c> HTML attribute) and the loaded assignments never rendered
    /// as selected. The supported control highlights the options it is handed.
    /// </summary>
    [TestMethod]
    public async Task EditDialog_TopicPicker_BindsTheSupportedApi_AndLoadedAssignmentsRenderSelected()
    {
        var gradeId = Guid.NewGuid();
        var codedValueId = Guid.NewGuid();
        var topicA = Guid.NewGuid();
        var topicB = Guid.NewGuid();
        var assignmentA = Guid.NewGuid();

        var handler = RegisterFor(gradeId, codedValueId);
        handler.Map("GET", $"/api/coded-values/{codedValueId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(CodedValueJson(codedValueId)));
        handler.Map("/students/topics", HttpStatusCode.OK, JsonSerializer.Serialize(new[]
        {
            TopicJson(topicA, "Algebra"),
            TopicJson(topicB, "Geometry"),
        }));
        // The dialog's baseline read is a prefix-matching wildcard (the seeded
        // by-grade entry in RegisterFor is method-keyed and never matches a
        // parameterised url).
        handler.Map("/students/topic-assignments/by-grade/", HttpStatusCode.OK,
            JsonSerializer.Serialize(new[] { AssignmentJson(assignmentA, gradeId, topicA) }));

        var cut = RenderProvider();
        var model = new GradeLevelEditDialog.GradeLevelEditModel
        {
            Id = gradeId,
            CodedValueId = codedValueId,
            CurrentName = "Grade 5",
        };

        var task = DialogService.ShowShellDialogAsync<
            GradeLevelEditDialog,
            GradeLevelEditDialog.GradeLevelEditModel,
            SchoolCollab.Students.Application.Services.GradeLevelDto>(
            model, "Edit grade", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Algebra"));

        cut.Markup.Should().NotContain("selectedvalues",
            "the dead SelectedValues binding must not leak into the DOM");
        cut.FindAll("fluent-listbox").Should().BeEmpty(
            "the supported control is the multi-select FluentSelect (the skills' multi-select route)");

        model.TopicIds.Should().BeEquivalentTo(new[] { topicA },
            "the loaded assignments are the picker's single source of truth");
        cut.Markup.Should().Contain("Topics (1)");

        var picker = cut.FindComponent<FluentSelect<TopicDto>>().Instance;
        picker.Multiple.Should().BeTrue();
        picker.SelectedOptions.Should().ContainSingle(o => o.Id == topicA);
        cut.FindAll("fluent-option").Single(o => o.GetAttribute("value") == topicA.ToString())
            .HasAttribute("selected").Should().BeTrue("the loaded assignment renders as selected");
        cut.FindAll("fluent-option").Single(o => o.GetAttribute("value") == topicB.ToString())
            .HasAttribute("selected").Should().BeFalse("an unassigned topic must not render as selected");

        var cancelButton = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Cancel"));
        cancelButton.Click();
        (await task).Should().BeNull();
    }

    /// <summary>
    /// Regression (round fluentui-dead-binding): changing the picker must reach the
    /// ids field and the submit diff must apply the new set — assign the picked topic
    /// and drop the one the picker no longer reports. With the dead binding the loaded
    /// baseline could never change, so neither call was ever issued.
    /// </summary>
    [TestMethod]
    public async Task EditDialog_TopicSelection_ReachesTheModel_AndSubmitAppliesThePickedSet()
    {
        var gradeId = Guid.NewGuid();
        var codedValueId = Guid.NewGuid();
        var topicA = Guid.NewGuid();
        var topicB = Guid.NewGuid();
        var assignmentA = Guid.NewGuid();

        var handler = RegisterFor(gradeId, codedValueId);
        handler.Map("GET", $"/api/coded-values/{codedValueId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(CodedValueJson(codedValueId)));
        handler.Map("/students/topics", HttpStatusCode.OK, JsonSerializer.Serialize(new[]
        {
            TopicJson(topicA, "Algebra"),
            TopicJson(topicB, "Geometry"),
        }));
        handler.Map("/students/topic-assignments/by-grade/", HttpStatusCode.OK,
            JsonSerializer.Serialize(new[] { AssignmentJson(assignmentA, gradeId, topicA) }));
        handler.Map("PUT", $"/students/grade-levels/{gradeId}", HttpStatusCode.NoContent, "");
        handler.Map("DELETE", $"/students/topic-assignments/{assignmentA}", HttpStatusCode.NoContent, "");

        var cut = RenderProvider();
        var model = new GradeLevelEditDialog.GradeLevelEditModel
        {
            Id = gradeId,
            CodedValueId = codedValueId,
            CurrentName = "Grade 5",
        };

        var task = DialogService.ShowShellDialogAsync<
            GradeLevelEditDialog,
            GradeLevelEditDialog.GradeLevelEditModel,
            SchoolCollab.Students.Application.Services.GradeLevelDto>(
            model, "Edit grade", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Algebra"));

        var picker = cut.FindComponent<FluentSelect<TopicDto>>();
        await cut.InvokeAsync(() => picker.Instance.SelectedOptionsChanged.InvokeAsync(
            picker.Instance.Items!.Where(t => t.Id == topicB).ToArray()));

        model.TopicIds.Should().BeEquivalentTo(new[] { topicB },
            "the picker's callback writes the picked ids back to the single source of truth");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            handler.Calls.Should().Contain(c => c.Method == "POST" && c.Url == "/students/topic-assignments/grade",
                "the submitted set assigns the picked topic");
            handler.Calls.Should().Contain(c => c.Method == "DELETE" && c.Url == $"/students/topic-assignments/{assignmentA}",
                "the dropped topic is unassigned");
        }, TimeSpan.FromSeconds(5));

        handler.Calls.Single(c => c.Method == "POST" && c.Url == "/students/topic-assignments/grade")
            .Body.Should().Contain(topicB.ToString()).And.NotContain(topicA.ToString());

        (await task).Should().NotBeNull("the edit completed against the scripted backend");
    }
}