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
/// bUnit tests for the teacher create/edit dialog (teacher-edit-dialog-modernization.md).
/// The dialog is a DialogShellBase form dialog opened via ShowShellDialogAsync, so these
/// tests render it through the real FluentDialogProvider + IDialogService pipeline (the
/// same pattern as GradeLevelEditDialogTests). The Fluent inputs (text fields, comboboxes,
/// dropdowns) render in shadow DOM, so this asserts structure + local-list state — the
/// chips / selected-only rows / counts that reflect the dialog's own list state — plus
/// the grade-context subjects scoping and the context-grade save contract.
/// </summary>
[TestClass]
public class TeacherEditDialogBunitTests : BunitContext
{
    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public TeacherEditDialogBunitTests()
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

    private static string JsonArray(params object[] items) => JsonSerializer.Serialize(items);

    private static Dictionary<string, object?> TopicJson(Guid id, string code, string name) => new()
    {
        ["id"] = id, ["codedValueId"] = (Guid?)Guid.NewGuid(), ["code"] = code, ["name"] = name,
        ["description"] = (string?)null, ["displayOrder"] = 1,
        ["createdAt"] = "2026-01-01T00:00:00Z", ["updatedAt"] = "2026-01-01T00:00:00Z",
    };

    private static Dictionary<string, object?> GradeJson(Guid id, string name, int level) => new()
    {
        ["id"] = id, ["codedValueId"] = Guid.NewGuid(), ["level"] = level, ["name"] = name,
        ["displayOrder"] = 1, ["topicCount"] = 0, ["studentCount"] = 0,
        ["createdAt"] = "2026-01-01T00:00:00Z", ["updatedAt"] = "2026-01-01T00:00:00Z",
    };

    private static Dictionary<string, object?> QualifJson(Guid id, string code, string name) => new()
    {
        ["id"] = id, ["code"] = code, ["name"] = name, ["description"] = (string?)null,
        ["parentId"] = (Guid?)null, ["parentCode"] = "QUALIF", ["isDisabled"] = false, ["displayOrder"] = 1,
    };

    private static Dictionary<string, object?> TeacherJson(Guid id, string first, string last) => new()
    {
        ["id"] = id, ["titleCodedValueId"] = (Guid?)null, ["firstName"] = first, ["lastName"] = last,
        ["displayName"] = (string?)null, ["genderCodedValueId"] = (Guid?)null, ["dateOfBirth"] = (DateOnly?)null,
        ["levelOfEducationCodedValueId"] = (Guid?)null, ["qualificationCodedValueIds"] = Array.Empty<Guid>(),
        ["isDeleted"] = false, ["createdAt"] = "2026-01-01T00:00:00Z", ["updatedAt"] = "2026-01-01T00:00:00Z",
    };

    /// <summary>
    /// Registers the scripted HTTP backend the dialog talks to and returns the handler
    /// (so tests can add save-path responses and inspect recorded calls).
    /// </summary>
    private ScriptedHandler RegisterFor(
        IEnumerable<(Guid Id, string Code, string Name)>? topics = null,
        IEnumerable<(Guid Id, string Name, int Level)>? grades = null,
        IEnumerable<(Guid Id, string Code, string Name)>? qualifications = null,
        Guid? teacherId = null,
        string? teacherJson = null,
        IEnumerable<(Guid TopicId, Guid? RoleId)>? seededTopicRoles = null,
        IEnumerable<Guid>? seededGradeIds = null,
        Guid? contextGradeId = null,
        IEnumerable<Guid>? gradeEnrolledTopicIds = null)
    {
        var handler = new ScriptedHandler();

        handler.Map("/students/topics", HttpStatusCode.OK,
            JsonArray((topics ?? []).Select(t => TopicJson(t.Id, t.Code, t.Name)).ToArray()));
        handler.Map("/students/grade-levels", HttpStatusCode.OK,
            JsonArray((grades ?? []).Select(g => GradeJson(g.Id, g.Name, g.Level)).ToArray()));
        handler.Map("/api/coded-values/by-parent?parentCode=QUALIF", HttpStatusCode.OK,
            JsonArray((qualifications ?? []).Select(q => QualifJson(q.Id, q.Code, q.Name)).ToArray()));

        // Profile/topic role dropdown option sources (render in shadow DOM; empty is fine).
        handler.Map("/api/coded-values/by-parent?parentCode=SALUTS", HttpStatusCode.OK, "[]");
        handler.Map("/api/coded-values/by-parent?parentCode=GENDER", HttpStatusCode.OK, "[]");
        handler.Map("/api/coded-values/by-parent?parentCode=EDUCLEVEL", HttpStatusCode.OK, "[]");
        handler.Map("/api/coded-values/by-parent?parentCode=TCHROLES", HttpStatusCode.OK, "[]");

        if (contextGradeId is { } ctxId)
        {
            handler.Map($"/students/topic-assignments/by-grade/{ctxId}", HttpStatusCode.OK,
                JsonArray((gradeEnrolledTopicIds ?? []).Select(topicId => new Dictionary<string, object?>
                {
                    ["id"] = Guid.NewGuid(), ["audience"] = "grade", ["gradeLevelId"] = ctxId,
                    ["activityGroupId"] = (Guid?)null, ["topicId"] = topicId,
                    ["startDate"] = "2026-01-01", ["endDate"] = (string?)null,
                    ["topicStrandId"] = (Guid?)null, ["topicLessonId"] = (Guid?)null,
                    ["createdAt"] = "2026-01-01T00:00:00Z", ["updatedAt"] = "2026-01-01T00:00:00Z",
                }).ToArray()));
        }

        if (teacherId is { } tid)
        {
            // Exact GET match (not the ANY prefix fallback) so it does not shadow
            // the more specific /teachers/{id}/topics/roles and /teachers/{id}/grade-levels.
            handler.Map("GET", $"/teachers/{tid}", HttpStatusCode.OK, teacherJson ?? JsonSerializer.Serialize(TeacherJson(tid, "Jane", "Doe")));
            handler.Map($"/teachers/{tid}/topics/roles", HttpStatusCode.OK,
                JsonArray((seededTopicRoles ?? []).Select(r => new Dictionary<string, object?>
                {
                    ["topicId"] = r.TopicId, ["roleCodedValueId"] = r.RoleId,
                    ["startDate"] = (string?)null, ["endDate"] = (string?)null,
                }).ToArray()));
            handler.Map($"/teachers/{tid}/grade-levels", HttpStatusCode.OK,
                JsonArray((seededGradeIds ?? []).Select(gid => GradeJson(gid, "Grade 5", 5)).ToArray()));
        }

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));

        return handler;
    }

    private IRenderedComponent<FluentDialogProvider> RenderProvider() => Render<FluentDialogProvider>();

    private Task<TeacherDto?> OpenAsync(
        IRenderedComponent<FluentDialogProvider> cut,
        TeacherEditDialog.TeacherFormModel model,
        string title = "New Teacher")
        => DialogService.ShowShellDialogAsync<TeacherEditDialog, TeacherEditDialog.TeacherFormModel, TeacherDto>(
            model, title, DialogSize.Large);

    private static void Cancel(IRenderedComponent<FluentDialogProvider> cut)
        => cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Cancel")).Click();

    [TestMethod]
    public async Task CreateMode_RendersNameRowAndAllSections()
    {
        var topicId = Guid.NewGuid();
        var gradeId = Guid.NewGuid();
        var qualifId = Guid.NewGuid();
        RegisterFor(
            topics: new[] { (topicId, "MATH", "Mathematics") },
            grades: new[] { (gradeId, "Grade 5", 5) },
            qualifications: new[] { (qualifId, "BSC", "B.Sc") });
        var cut = RenderProvider();

        var task = OpenAsync(cut, new TeacherEditDialog.TeacherFormModel { TeacherId = null });

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        // Name = one label row with two inline fields.
        cut.Markup.Should().Contain("Name");
        cut.Markup.Should().Contain("First name");
        cut.Markup.Should().Contain("Last name");
        cut.Markup.Should().Contain("Create Teacher");
        cut.Markup.Should().Contain("Subjects (0)");
        cut.Markup.Should().Contain("Grade levels (0)");
        cut.Markup.Should().Contain("Mathematics");
        cut.Markup.Should().Contain("Grade 5");
        cut.Markup.Should().Contain("B.Sc");

        Cancel(cut);
        (await task).Should().BeNull("cancelling closes the dialog with no result");
    }

    [TestMethod]
    public async Task EditMode_PrefillsNameSubjectsAndGrades()
    {
        var teacherId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var gradeId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        RegisterFor(
            teacherId: teacherId,
            teacherJson: JsonSerializer.Serialize(TeacherJson(teacherId, "Jane", "Doe")),
            topics: new[] { (topicId, "MATH", "Mathematics") },
            grades: new[] { (gradeId, "Grade 5", 5) },
            seededTopicRoles: new[] { (topicId, (Guid?)roleId) },
            seededGradeIds: new[] { gradeId });
        var cut = RenderProvider();

        var task = OpenAsync(cut, new TeacherEditDialog.TeacherFormModel { TeacherId = teacherId }, "Edit Teacher");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Save Changes"));
        cut.Markup.Should().Contain("Jane");
        cut.Markup.Should().Contain("Doe");
        cut.Markup.Should().Contain("Subjects (1)");
        cut.Markup.Should().Contain("Grade levels (1)");
        cut.Markup.Should().Contain("Mathematics");
        cut.Markup.Should().Contain("Grade 5");

        Cancel(cut);
        (await task).Should().BeNull();
    }

    [TestMethod]
    public async Task Qualifications_SelectedRenderAsChips_ComboboxExcludesSelected()
    {
        var teacherId = Guid.NewGuid();
        var selectedQual = Guid.NewGuid();
        var unselectedQual = Guid.NewGuid();
        RegisterFor(
            teacherId: teacherId,
            teacherJson: JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["id"] = teacherId, ["titleCodedValueId"] = (Guid?)null, ["firstName"] = "Jane", ["lastName"] = "Doe",
                ["displayName"] = (string?)null, ["genderCodedValueId"] = (Guid?)null, ["dateOfBirth"] = (DateOnly?)null,
                ["levelOfEducationCodedValueId"] = (Guid?)null,
                ["qualificationCodedValueIds"] = new[] { selectedQual },
                ["isDeleted"] = false, ["createdAt"] = "2026-01-01T00:00:00Z", ["updatedAt"] = "2026-01-01T00:00:00Z",
            }),
            qualifications: new[]
            {
                (selectedQual, "BSC", "B.Sc"),
                (unselectedQual, "MSC", "M.Sc"),
            });
        var cut = RenderProvider();

        var task = OpenAsync(cut, new TeacherEditDialog.TeacherFormModel { TeacherId = teacherId }, "Edit Teacher");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Save Changes"));
        // The selected qualification renders as a chip.
        cut.Markup.Should().Contain("B.Sc");
        // The combobox offers only the unselected qualification — the selected one
        // is excluded from the add-picker options.
        var options = cut.FindAll("fluent-option");
        options.Select(o => o.TextContent).Should().Contain("M.Sc");
        options.Select(o => o.TextContent).Should().NotContain("B.Sc",
            "the selected qualification is excluded from the add-picker");

        Cancel(cut);
        (await task).Should().BeNull();
    }

    [TestMethod]
    public async Task Qualifications_SelectingOneOption_DoesNotPullInASecond()
    {
        // Regression guard for the FluentCombobox double-select: with
        // Autocomplete="List" the combobox can report a selection that pulls in an
        // extra, unintended option. Selecting "Life Sciences Teaching" must add
        // exactly that one chip — "Languages Teaching" must stay unselected.
        var lifeSciences = Guid.NewGuid();
        var languages = Guid.NewGuid();
        RegisterFor(
            qualifications: new[]
            {
                (lifeSciences, "LIFESCI", "Life Sciences Teaching"),
                (languages, "LANG", "Languages Teaching"),
            });
        var cut = RenderProvider();

        var task = OpenAsync(cut, new TeacherEditDialog.TeacherFormModel { TeacherId = null });

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Add qualification…"));

        // The only CodedValueDto combobox is the qualifications add-picker.
        var qualCombobox = cut.FindComponents<FluentCombobox<CodedValueDto>>().Single();
        var lifeSciencesOption = qualCombobox.Instance.Items!.Single(i => i.Name == "Life Sciences Teaching");

        // Simulate the combobox reporting a single selection of "Life Sciences Teaching".
        await cut.InvokeAsync(() =>
            qualCombobox.Instance.SelectedOptionChanged.InvokeAsync(lifeSciencesOption));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Life Sciences Teaching"));

        // Exactly one qualification chip; the second option is NOT pulled in.
        var chipLabels = cut.FindAll(".chip-label").Select(c => c.TextContent.Trim()).ToArray();
        chipLabels.Should().HaveCount(1, "selecting one qualification adds exactly one chip");
        chipLabels.Should().Contain("Life Sciences Teaching");
        chipLabels.Should().NotContain("Languages Teaching",
            "selecting one qualification must not also select a second (FluentCombobox double-select)");

        Cancel(cut);
        (await task).Should().BeNull();
    }

    [TestMethod]
    public async Task Qualifications_ClickingOption_AddsExactlyOneChip()
    {
        // Faithful reproduction of the real browser event path: clicking a rendered
        // <fluent-option> drives FluentCombobox.OnSelectedItemChangedHandlerAsync,
        // which fires SelectedOptionChanged AND re-enters ChangeHandlerAsync — the
        // path that historically double-added. Exactly one chip must result.
        var lifeSciences = Guid.NewGuid();
        var languages = Guid.NewGuid();
        RegisterFor(
            qualifications: new[]
            {
                (lifeSciences, "LIFESCI", "Life Sciences Teaching"),
                (languages, "LANG", "Languages Teaching"),
            });
        var cut = RenderProvider();

        var task = OpenAsync(cut, new TeacherEditDialog.TeacherFormModel { TeacherId = null });

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Add qualification…"));

        var option = cut.FindAll("fluent-option").Single(o => o.TextContent.Contains("Life Sciences Teaching"));
        option.Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Life Sciences Teaching"));

        var chipLabels = cut.FindAll(".chip-label").Select(c => c.TextContent.Trim()).ToArray();
        chipLabels.Should().HaveCount(1, "clicking one option adds exactly one chip (not two)");
        chipLabels.Should().Contain("Life Sciences Teaching");
        chipLabels.Should().NotContain("Languages Teaching",
            "clicking one option must not pull in a second qualification");

        Cancel(cut);
        (await task).Should().BeNull();
    }

    [TestMethod]
    public async Task Qualifications_EachPickAddsExactlyOneChip()
    {
        // Confirms the chip count grows by exactly one per selection across
        // multiple picks — a single FluentCombobox selection must never add two chips.
        var lifeSciences = Guid.NewGuid();
        var languages = Guid.NewGuid();
        var chemistry = Guid.NewGuid();
        RegisterFor(
            qualifications: new[]
            {
                (lifeSciences, "LIFESCI", "Life Sciences Teaching"),
                (languages, "LANG", "Languages Teaching"),
                (chemistry, "CHEM", "Chemistry Teaching"),
            });
        var cut = RenderProvider();

        var task = OpenAsync(cut, new TeacherEditDialog.TeacherFormModel { TeacherId = null });

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Add qualification…"));

        string[] ChipLabels() => cut.FindAll(".chip-label").Select(c => c.TextContent.Trim()).ToArray();

        ChipLabels().Should().BeEmpty("nothing selected yet");

        var expectedCount = 0;
        foreach (var name in new[] { "Life Sciences Teaching", "Languages Teaching", "Chemistry Teaching" })
        {
            cut.WaitForAssertion(() =>
                cut.FindAll("fluent-option").Any(o => o.TextContent.Contains(name)).Should().BeTrue());
            cut.FindAll("fluent-option").Single(o => o.TextContent.Contains(name)).Click();

            cut.WaitForAssertion(() => cut.Markup.Should().Contain(name));

            expectedCount++;
            var labels = ChipLabels();
            labels.Should().HaveCount(expectedCount, $"picking \"{name}\" adds exactly one chip (total {expectedCount})");
            labels.Should().Contain(name);
        }

        Cancel(cut);
        (await task).Should().BeNull();
    }

    [TestMethod]
    public async Task Subjects_EachPickAddsExactlyOneRow()
    {
        // Same per-pick count guarantee for the subjects add-picker (rows, not chips).
        var lifeSciences = Guid.NewGuid();
        var languages = Guid.NewGuid();
        var chemistry = Guid.NewGuid();
        RegisterFor(
            topics: new[]
            {
                (lifeSciences, "LIFESCI", "Life Sciences Teaching"),
                (languages, "LANG", "Languages Teaching"),
                (chemistry, "CHEM", "Chemistry Teaching"),
            });
        var cut = RenderProvider();

        var task = OpenAsync(cut, new TeacherEditDialog.TeacherFormModel { TeacherId = null });

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Add subject…"));

        var expectedCount = 0;
        foreach (var name in new[] { "Life Sciences Teaching", "Languages Teaching", "Chemistry Teaching" })
        {
            cut.WaitForAssertion(() =>
                cut.FindAll("fluent-option").Any(o => o.TextContent.Contains(name)).Should().BeTrue());
            cut.FindAll("fluent-option").Single(o => o.TextContent.Contains(name)).Click();

            cut.WaitForAssertion(() => cut.Markup.Should().Contain($"Subjects ({expectedCount + 1})"));

            expectedCount++;
            var rows = cut.FindAll(".subject-row");
            rows.Should().HaveCount(expectedCount, $"picking \"{name}\" adds exactly one row (total {expectedCount})");
        }

        Cancel(cut);
        (await task).Should().BeNull();
    }

    [TestMethod]
    public async Task Subjects_SelectingOneTopic_DoesNotPullInASecond()
    {
        // Same regression guard for the subjects add-picker.
        var lifeSciences = Guid.NewGuid();
        var languages = Guid.NewGuid();
        RegisterFor(
            topics: new[]
            {
                (lifeSciences, "LIFESCI", "Life Sciences Teaching"),
                (languages, "LANG", "Languages Teaching"),
            });
        var cut = RenderProvider();

        var task = OpenAsync(cut, new TeacherEditDialog.TeacherFormModel { TeacherId = null });

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Add subject…"));

        var subjectCombobox = cut.FindComponents<FluentCombobox<TopicDto>>().Single();
        var lifeSciencesOption = subjectCombobox.Instance.Items!.Single(t => t.Name == "Life Sciences Teaching");

        await cut.InvokeAsync(() =>
            subjectCombobox.Instance.SelectedOptionChanged.InvokeAsync(lifeSciencesOption));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Subjects (1)"));

        // Only one subject row; the second topic is NOT pulled in.
        var rows = cut.FindAll(".subject-row");
        rows.Should().HaveCount(1, "selecting one subject must not also select a second (FluentCombobox double-select)");
        rows[0].TextContent.Should().Contain("Life Sciences Teaching");
        rows[0].TextContent.Should().NotContain("Languages Teaching");

        Cancel(cut);
        (await task).Should().BeNull();
    }

    [TestMethod]
    public async Task Subjects_OnlyAssignedRenderRows_CountTracks()
    {
        var teacherId = Guid.NewGuid();
        var assignedTopic = Guid.NewGuid();
        var unassignedTopic = Guid.NewGuid();
        RegisterFor(
            teacherId: teacherId,
            topics: new[]
            {
                (assignedTopic, "MATH", "Mathematics"),
                (unassignedTopic, "SCI", "Science"),
            },
            seededTopicRoles: new[] { (assignedTopic, (Guid?)null) });
        var cut = RenderProvider();

        var task = OpenAsync(cut, new TeacherEditDialog.TeacherFormModel { TeacherId = teacherId }, "Edit Teacher");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Subjects (1)"));
        // Only the assigned topic renders as a ROW (zero vertical space for
        // unassigned topics). The unassigned topic is still offered by the
        // add-picker combobox, so we assert on the row count, not absence of text.
        var rows = cut.FindAll(".subject-row");
        rows.Should().HaveCount(1, "only assigned topics render rows");
        rows[0].TextContent.Should().Contain("Mathematics");

        Cancel(cut);
        (await task).Should().BeNull();
    }

    [TestMethod]
    public async Task ContextSubjects_ScopesPickerToGradeEnrolled_ExistingOutsideStillRenders()
    {
        var teacherId = Guid.NewGuid();
        var ctxGradeId = Guid.NewGuid();
        var enrolledTopic = Guid.NewGuid();
        var outsideTopic = Guid.NewGuid();
        RegisterFor(
            teacherId: teacherId,
            contextGradeId: ctxGradeId,
            gradeEnrolledTopicIds: new[] { enrolledTopic },
            topics: new[]
            {
                (enrolledTopic, "MATH", "Mathematics"),
                (outsideTopic, "SCI", "Science"),
            },
            // The teacher already teaches Science, which is NOT enrolled in this grade.
            seededTopicRoles: new[] { (outsideTopic, (Guid?)null) });
        var cut = RenderProvider();

        var task = OpenAsync(cut, new TeacherEditDialog.TeacherFormModel
        {
            TeacherId = teacherId,
            ContextGradeLevelId = ctxGradeId,
            ContextGradeLevelName = "Grade 5",
        }, "Edit Teacher");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Save Changes"));
        // The existing outside-grade assignment still renders as a row (removable).
        cut.Markup.Should().Contain("Science");
        // The add-picker offers only the grade-enrolled topic.
        var options = cut.FindAll("fluent-option");
        options.Select(o => o.TextContent).Should().Contain("Mathematics");
        options.Select(o => o.TextContent).Should().NotContain("Science",
            "the picker is scoped to grade-enrolled subjects only");

        Cancel(cut);
        (await task).Should().BeNull();
    }

    [TestMethod]
    public async Task ContextSubjects_ZeroEnrolled_ShowsInfoAndNoCombobox()
    {
        var ctxGradeId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        RegisterFor(
            contextGradeId: ctxGradeId,
            gradeEnrolledTopicIds: Array.Empty<Guid>(),
            topics: new[] { (topicId, "MATH", "Mathematics") });
        var cut = RenderProvider();

        var task = OpenAsync(cut, new TeacherEditDialog.TeacherFormModel
        {
            TeacherId = null,
            ContextGradeLevelId = ctxGradeId,
            ContextGradeLevelName = "Grade 5",
        });

        cut.WaitForAssertion(() => cut.Markup.Should().Contain(
            "No subjects are assigned to this grade yet"));
        // No add-picker when the grade has no enrolled subjects.
        cut.Markup.Should().NotContain("Add subject…");

        Cancel(cut);
        (await task).Should().BeNull();
    }

    [TestMethod]
    public async Task ContextGrade_SectionHidden_NoGradePicker()
    {
        var ctxGradeId = Guid.NewGuid();
        var gradeId = Guid.NewGuid();
        RegisterFor(
            contextGradeId: ctxGradeId,
            grades: new[] { (gradeId, "Grade 5", 5) });
        var cut = RenderProvider();

        var task = OpenAsync(cut, new TeacherEditDialog.TeacherFormModel
        {
            TeacherId = null,
            ContextGradeLevelId = ctxGradeId,
            ContextGradeLevelName = "Grade 5",
        });

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        // Grade section is hidden entirely in grade context — no picker, no checklist.
        cut.Markup.Should().NotContain("Grade levels (");
        cut.Markup.Should().NotContain("Add grade…");
        cut.Markup.Should().Contain("Linked to this grade on save.");

        Cancel(cut);
        (await task).Should().BeNull();
    }

    [TestMethod]
    public async Task LandingCreate_ShowsGradeSection_WithChipsAndPicker()
    {
        var gradeId = Guid.NewGuid();
        var otherGradeId = Guid.NewGuid();
        RegisterFor(
            grades: new[]
            {
                (gradeId, "Grade 5", 5),
                (otherGradeId, "Grade 6", 6),
            });
        var cut = RenderProvider();

        var task = OpenAsync(cut, new TeacherEditDialog.TeacherFormModel { TeacherId = null });

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        // Non-context create shows the grade section with an add-picker.
        cut.Markup.Should().Contain("Grade levels (0)");
        cut.Markup.Should().Contain("Add grade…");
        cut.Markup.Should().NotContain("Linked to this grade on save.");

        Cancel(cut);
        (await task).Should().BeNull();
    }

    [TestMethod]
    public async Task ContextCreate_Save_LinksContextGrade()
    {
        var ctxGradeId = Guid.NewGuid();
        var newTeacherId = Guid.NewGuid();
        var handler = RegisterFor(
            contextGradeId: ctxGradeId,
            grades: new[] { (ctxGradeId, "Grade 5", 5) });
        // POST /teachers returns the new id; GET /teachers/{id} returns the DTO
        // (the dialog re-fetches the teacher to return as its result).
        handler.Map("POST", "/teachers", HttpStatusCode.OK,
            JsonSerializer.Serialize(new Dictionary<string, object?> { ["id"] = newTeacherId }));
        handler.Map("GET", $"/teachers/{newTeacherId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(TeacherJson(newTeacherId, "Jane", "Doe")));
        handler.Map("POST", $"/teachers/{newTeacherId}/grade-levels", HttpStatusCode.NoContent, "");
        var cut = RenderProvider();

        var task = OpenAsync(cut, new TeacherEditDialog.TeacherFormModel
        {
            TeacherId = null,
            ContextGradeLevelId = ctxGradeId,
            ContextGradeLevelName = "Grade 5",
        });

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Find("#teacherFirstName").Change("Jane");
        cut.Find("#teacherLastName").Change("Doe");
        cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Create Teacher")).Click();

        var result = await task;
        result.Should().NotBeNull();
        result!.Id.Should().Be(newTeacherId);

        handler.Calls.Should().Contain(c => c.Method == "POST" && c.Url == "/teachers");
        // Exactly one grade-link call — the context grade, linked implicitly on save.
        handler.Calls.Count(c => c.Method == "POST" && c.Url == $"/teachers/{newTeacherId}/grade-levels")
            .Should().Be(1, "the context grade is linked implicitly on save");
    }
}
