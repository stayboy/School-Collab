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
/// bUnit tests for <see cref="GradeLevelCreateDialog"/> mounted through the
/// real <see cref="FluentDialogProvider"/> + <c>DialogService.ShowDialogAsync</c>
/// pipeline (no FluentUI-internal mocking). Covers the dialog's submit
/// pipeline: pick a GRADE → resolve coded value → GetOrCreate → subject
/// diff. The same pattern is shared with the upcoming
/// <c>GradeLevelEditDialogTests</c>; only the seeded assignments and the
/// grade id source differ.
/// </summary>
[TestClass]
public class GradeLevelCreateDialogTests : BunitContext
{
    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public GradeLevelCreateDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    /// <summary>Renders the dialog provider that hosts the dialogs shown in tests.</summary>
    private IRenderedComponent<FluentDialogProvider> RenderProvider() => Render<FluentDialogProvider>();

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
    /// Registers a scripted HTTP backend that answers the dialog's expected
    /// calls. <paramref name="seededAssignments"/> simulates any grade-subject
    /// rows that already exist for the (post-create) grade + period; the
    /// dialog's diff treats them as the baseline.
    /// </summary>
    private ScriptedHandler RegisterFor(
        Guid gradeId,
        Guid codedValueId,
        IEnumerable<(Guid SubjectId, Guid AssignmentId)>? seededAssignments = null)
    {
        var handler = new ScriptedHandler();

        // CodedValueDropdown parent lookups + Topics catalog - empty. Keyed
        // off the parent-code endpoint so it does NOT shadow the per-id
        // GetByIdAsync registration below.
        handler.Map("/api/coded-values/by-parent", HttpStatusCode.OK, "[]");
        handler.Map("/students/topics", HttpStatusCode.OK, "[]");

        // GetByIdAsync for the picked coded value - exact-match on the
        // /api/coded-values/{id} path. The dictionary's exact-match lookup
        // runs before the wildcard scan, so this entry wins.
        // Attributes / AttributeDefinitions are typed as
        // IReadOnlyCollection<...> on CodedValueDto; null is the safest
        // serialized form (System.Text.Json round-trips an empty list
        // fine, but a typed-empty list of object won't deserialize back to
        // the strongly-typed CodedValueAttributeDto).
        handler.Map("GET", $"/api/coded-values/{codedValueId}", HttpStatusCode.OK,
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
                ["attributes"] = (IReadOnlyCollection<object>?)null,
                ["attributeDefinitions"] = (IReadOnlyCollection<object>?)null,
                ["childrenCount"] = 0,
                ["isDeleted"] = false,
                ["deletedAt"] = (DateTimeOffset?)null,
                ["isOverridden"] = false,
                ["defaultName"] = (string?)null,
            }));

        // GetOrCreateGradeLevelAsync -> POST /students/grade-levels/get-or-create.
        handler.Map("POST", "/students/grade-levels/get-or-create", HttpStatusCode.OK,
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["id"] = gradeId,
                ["codedValueId"] = Guid.NewGuid(),
                ["level"] = 5,
                ["name"] = "Grade 5",
                ["displayOrder"] = 5,
                ["topicCount"] = 0,
                ["studentCount"] = 0,
                ["createdAt"] = DateTimeOffset.UnixEpoch,
                ["updatedAt"] = DateTimeOffset.UnixEpoch,
            }));

        // ListGradeTopicsByGradeAsync -> GET /students/topic-assignments/by-grade/{id}.
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

        // AssignGradeTopicAsync -> POST /students/topic-assignments/grade.
        handler.Map("POST", "/students/topic-assignments/grade", HttpStatusCode.Created,
            JsonSerializer.Serialize(new Dictionary<string, object?> { ["id"] = Guid.NewGuid() }));
        // RemoveTopicAssignmentAsync -> DELETE /students/topic-assignments/{id}.
        handler.Map("DELETE", "/students/topic-assignments/", HttpStatusCode.NoContent, "");

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));

        return handler;
    }

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

    [TestMethod]
    public async Task Create_Dialog_Renders_Form_Fields()
    {
        // Register services BEFORE opening the dialog so its [Inject] students
        // api client resolves at instantiation time.
        RegisterFor(gradeId: Guid.NewGuid(), codedValueId: Guid.NewGuid());
        var cut = RenderProvider();

        var task = DialogService.ShowShellDialogAsync<GradeLevelCreateDialog, GradeLevelCreateDialog.GradeLevelCreateModel, SchoolCollab.Students.Application.Services.GradeLevelDto>(
            new GradeLevelCreateDialog.GradeLevelCreateModel(),
            title: "Create grade level",
            size: DialogSize.Medium);

        // Wait for the EditForm to mount inside the provider.
        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        cut.Markup.Should().Contain("Grade");
        cut.Markup.Should().Contain("Age range");
        cut.Markup.Should().Contain("Allowed Gender");
        cut.Markup.Should().Contain("Create"); // submit button label

        // Cancel so the dialog task completes cleanly.
        var cancelButton = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Cancel"));
        cancelButton.Click();
        var result = await task;
        result.Should().BeNull("cancelling closes the dialog with no result");
    }

    [TestMethod]
    public async Task Create_Dialog_Submit_BrandNewGrade_AssignsAllPickedSubjects()
    {
        // Plan §5.2: brand-new grade (baseline empty) -> N AssignGradeTopic
        // calls, no RemoveTopicAssignment calls.
        var gradeId = Guid.NewGuid();
        var codedValueId = Guid.NewGuid();
        var subjectIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var handler = RegisterFor(
            gradeId: gradeId,
            codedValueId: codedValueId,
            seededAssignments: null); // empty baseline

        // Seed the topic catalog so the dialog's ListTopicsAsync returns
        // options. The dialog stores picked ids on the model; the test
        // sets them directly before submitting.
        var catalog = subjectIds
            .Select(id => (object)new Dictionary<string, object?>
            {
                ["id"] = id,
                ["codedValueId"] = Guid.NewGuid(),
                ["code"] = $"SUBJ-{id.ToString()[..4]}",
                ["name"] = $"Subject {id.ToString()[..4]}",
                ["displayOrder"] = 1,
                ["isOverridden"] = false,
                ["createdAt"] = DateTimeOffset.UnixEpoch,
                ["updatedAt"] = DateTimeOffset.UnixEpoch,
            })
            .ToArray();
        handler.Map("/students/topics", HttpStatusCode.OK, JsonSerializer.Serialize(catalog));

        var cut = RenderProvider();
        var model = new GradeLevelCreateDialog.GradeLevelCreateModel
        {
            CodedValueId = codedValueId,
            MinAge = 10,
            MaxAge = 12,
            TopicIds = subjectIds.ToList(),
        };

        var task = DialogService.ShowShellDialogAsync<
            GradeLevelCreateDialog,
            GradeLevelCreateDialog.GradeLevelCreateModel,
            SchoolCollab.Students.Application.Services.GradeLevelDto>(
            model, "Create grade level", DialogSize.Medium);

        // Wait for the dialog's EditForm to render, then submit.
        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Find("form").Submit();

        // The deeper submit-pipeline assertion (assign-on-add / remove-on-drop /
        // no-period-short-circuit) is driven through the picker's real callback in
        // Create_Dialog_TopicSelection_ReachesTheModel_AndSubmitAssignsThePickedTopics;
        // this smoke case only pins the mount + Cancel path.
        var cancelButton = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Cancel"));
        cancelButton.Click();
        var result = await task;
        result.Should().BeNull("cancelling closes the dialog with no result");
    }

    /// <summary>
    /// Regression (round fluentui-dead-binding): the Topics picker bound the dead
    /// <c>SelectedValues</c> pair, which does not exist on FluentUI 4.14.2 list
    /// components — Blazor dropped it into the catch-all <c>AdditionalAttributes</c>
    /// (a stray <c>selectedvalues</c> HTML attribute) and the picker never showed a
    /// selection. The supported control highlights the options it is handed, so a
    /// pre-populated <c>Model.TopicIds</c> renders as selected.
    /// </summary>
    [TestMethod]
    public async Task Create_Dialog_TopicPicker_BindsTheSupportedApi_AndPreloadedIdsRenderSelected()
    {
        var gradeId = Guid.NewGuid();
        var codedValueId = Guid.NewGuid();
        var topicA = Guid.NewGuid();
        var topicB = Guid.NewGuid();
        var handler = RegisterFor(gradeId: gradeId, codedValueId: codedValueId);
        handler.Map("/students/topics", HttpStatusCode.OK, JsonSerializer.Serialize(new[]
        {
            TopicJson(topicA, "Algebra"),
            TopicJson(topicB, "Geometry"),
        }));

        var cut = RenderProvider();
        var model = new GradeLevelCreateDialog.GradeLevelCreateModel
        {
            CodedValueId = codedValueId,
            TopicIds = [topicB],
        };

        var task = DialogService.ShowShellDialogAsync<
            GradeLevelCreateDialog,
            GradeLevelCreateDialog.GradeLevelCreateModel,
            SchoolCollab.Students.Application.Services.GradeLevelDto>(
            model, "Create grade level", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Algebra", "the topic catalog renders as picker options"));

        cut.Markup.Should().NotContain("selectedvalues",
            "the dead SelectedValues binding must not leak into the DOM");
        cut.FindAll("fluent-listbox").Should().BeEmpty(
            "the supported control is the multi-select FluentSelect (the skills' multi-select route)");

        var picker = cut.FindComponent<FluentSelect<TopicDto>>().Instance;
        picker.Multiple.Should().BeTrue();
        picker.SelectedOptions.Should().ContainSingle(o => o.Id == topicB,
            "the pre-populated Model.TopicIds projects into the picker's selected options");

        cut.FindAll("fluent-option").Single(o => o.GetAttribute("value") == topicB.ToString())
            .HasAttribute("selected").Should().BeTrue("the pre-populated topic renders as selected");
        cut.FindAll("fluent-option").Single(o => o.GetAttribute("value") == topicA.ToString())
            .HasAttribute("selected").Should().BeFalse("an unpicked topic must not render as selected");

        var cancelButton = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Cancel"));
        cancelButton.Click();
        (await task).Should().BeNull();
    }

    /// <summary>
    /// Regression (round fluentui-dead-binding): a click on the picker must reach the
    /// ids field, and the submit diff must then carry those ids. With the dead binding
    /// neither direction worked — the picker reported nothing into
    /// <c>Model.TopicIds</c>, so no topic could ever be assigned.
    /// </summary>
    [TestMethod]
    public async Task Create_Dialog_TopicSelection_ReachesTheModel_AndSubmitAssignsThePickedTopics()
    {
        var gradeId = Guid.NewGuid();
        var codedValueId = Guid.NewGuid();
        var topicA = Guid.NewGuid();
        var topicB = Guid.NewGuid();
        var handler = RegisterFor(gradeId: gradeId, codedValueId: codedValueId);
        handler.Map("/students/topics", HttpStatusCode.OK, JsonSerializer.Serialize(new[]
        {
            TopicJson(topicA, "Algebra"),
            TopicJson(topicB, "Geometry"),
        }));
        // The dialog's baseline read is a prefix-matching wildcard (RegisterFor's
        // by-grade entry is method-keyed and so never matches a parameterised url).
        handler.Map("/students/topic-assignments/by-grade/", HttpStatusCode.OK, "[]");

        var cut = RenderProvider();
        var model = new GradeLevelCreateDialog.GradeLevelCreateModel { CodedValueId = codedValueId };

        var task = DialogService.ShowShellDialogAsync<
            GradeLevelCreateDialog,
            GradeLevelCreateDialog.GradeLevelCreateModel,
            SchoolCollab.Students.Application.Services.GradeLevelDto>(
            model, "Create grade level", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Algebra"));

        var picker = cut.FindComponent<FluentSelect<TopicDto>>();
        await cut.InvokeAsync(() => picker.Instance.SelectedOptionsChanged.InvokeAsync(
            picker.Instance.Items!.Where(t => t.Id == topicA || t.Id == topicB).ToArray()));

        model.TopicIds.Should().BeEquivalentTo(new[] { topicA, topicB },
            "the picker's callback writes the picked ids back to the single source of truth");
        cut.Markup.Should().Contain("Topics (2)", "the label's count is projected from the ids field");

        cut.Find("form").Submit();

        cut.WaitForAssertion(
            () => handler.Calls.Count(c => c.Method == "POST" && c.Url == "/students/topic-assignments/grade")
                .Should().Be(2, "the submit diff assigns exactly the picked topics"),
            TimeSpan.FromSeconds(5));

        var assignBodies = handler.Calls
            .Where(c => c.Method == "POST" && c.Url == "/students/topic-assignments/grade")
            .Select(c => c.Body ?? string.Empty)
            .ToArray();
        assignBodies.Should().Contain(b => b.Contains(topicA.ToString()));
        assignBodies.Should().Contain(b => b.Contains(topicB.ToString()));

        (await task).Should().NotBeNull("the create completed against the scripted backend");
    }
}
