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
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the teacher create/edit dialog (v4 assignments grid,
/// teacher-edit-dialog-modernization.md §5). The dialog is a DialogShellBase form
/// dialog opened via ShowShellDialogAsync; Fluent inputs render in shadow DOM, so
/// this asserts structure + local-list state — the assignment rows / counts that
/// reflect the dialog's own list state, plus the context-grade default and the
/// save contract.
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

    /// <summary>Feature-flag stub that returns true for all flags so existing
    /// v4 teacher-dialog tests keep exercising the activity-group path.</summary>
    private sealed class AllEnabledFeatureFlagService : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => true;
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) => Task.FromResult(true);
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    }

    /// <summary>Feature-flag stub that disables only the activity-group feature.</summary>
    private sealed class ActivityGroupsOffFeatureFlagService : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => featureKey != FeatureFlagKeys.EnableActivityGroups;
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default)
            => Task.FromResult(featureKey != FeatureFlagKeys.EnableActivityGroups);
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
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

    private static Dictionary<string, object?> GradeJson(Guid id, string name, int level) => new()
    {
        ["id"] = id, ["codedValueId"] = Guid.NewGuid(), ["level"] = level, ["name"] = name,
        ["displayOrder"] = 1, ["topicCount"] = 0, ["studentCount"] = 0,
        ["createdAt"] = "2026-01-01T00:00:00Z", ["updatedAt"] = "2026-01-01T00:00:00Z",
    };

    private static Dictionary<string, object?> TopicJson(Guid id, string code, string name) => new()
    {
        ["id"] = id, ["codedValueId"] = (Guid?)Guid.NewGuid(), ["code"] = code, ["name"] = name,
        ["description"] = (string?)null, ["displayOrder"] = 1,
        ["createdAt"] = "2026-01-01T00:00:00Z", ["updatedAt"] = "2026-01-01T00:00:00Z",
    };

    private static Dictionary<string, object?> ActivityJson(Guid id, string name) => new()
    {
        ["id"] = id, ["name"] = name, ["description"] = (string?)null, ["category"] = (string?)null,
        ["periodId"] = (Guid?)null, ["capacity"] = (int?)null, ["status"] = "Active", ["activeMemberCount"] = 0,
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

    private static Dictionary<string, object?> GradeAssignmentJson(
        Guid rowId, Guid gradeId, string gradeName, int level, Guid? subjectId = null, string? subjectName = null, string? subjectCode = null, Guid? roleId = null) => new()
    {
        ["rowId"] = rowId, ["gradeLevelId"] = gradeId, ["gradeName"] = gradeName, ["gradeLevel"] = level,
        ["subjectId"] = subjectId, ["subjectName"] = subjectName, ["subjectCode"] = subjectCode, ["roleCodedValueId"] = roleId,
    };

    private static Dictionary<string, object?> ActivityAssignmentJson(Guid rowId, Guid activityId, string activityName, Guid? roleId, params Guid[] gradeIds) => new()
    {
        ["rowId"] = rowId, ["activityGroupId"] = activityId, ["activityName"] = activityName,
        ["roleCodedValueId"] = roleId, ["gradeLevelIds"] = gradeIds,
    };

    /// <summary>Registers the scripted HTTP backend the dialog talks to and returns the handler.</summary>
    private ScriptedHandler RegisterFor(
        IEnumerable<(Guid Id, string Name, int Level)>? grades = null,
        IEnumerable<(Guid Id, string Code, string Name)>? topics = null,
        IEnumerable<(Guid Id, string Name)>? activities = null,
        IEnumerable<(Guid Id, string Code, string Name)>? qualifications = null,
        Guid? teacherId = null,
        string? teacherJson = null,
        IEnumerable<Dictionary<string, object?>>? gradeAssignments = null,
        IEnumerable<Dictionary<string, object?>>? activityAssignments = null,
        Guid? contextGradeId = null,
        IEnumerable<Guid>? gradeEnrolledTopicIds = null)
    {
        var handler = new ScriptedHandler();

        handler.Map("/students/grade-levels", HttpStatusCode.OK,
            JsonArray((grades ?? []).Select(g => GradeJson(g.Id, g.Name, g.Level)).ToArray()));
        handler.Map("/students/topics", HttpStatusCode.OK,
            JsonArray((topics ?? []).Select(t => TopicJson(t.Id, t.Code, t.Name)).ToArray()));
        handler.Map("/activity-groups", HttpStatusCode.OK,
            JsonArray((activities ?? []).Select(a => ActivityJson(a.Id, a.Name)).ToArray()));
        handler.Map("/api/coded-values/by-parent?parentCode=QUALIF", HttpStatusCode.OK,
            JsonArray((qualifications ?? []).Select(q => QualifJson(q.Id, q.Code, q.Name)).ToArray()));
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
        else
        {
            // v4: load topic assignments for ALL grades (not just context grade) so each
            // grade row's subject picker is filtered correctly. Map empty assignments for
            // all registered grades when not in context mode.
            foreach (var g in grades ?? [])
            {
                handler.Map($"/students/topic-assignments/by-grade/{g.Id}", HttpStatusCode.OK, "[]");
            }
        }

        if (teacherId is { } tid)
        {
            handler.Map("GET", $"/teachers/{tid}", HttpStatusCode.OK, teacherJson ?? JsonSerializer.Serialize(TeacherJson(tid, "Jane", "Doe")));
            handler.Map($"/teachers/{tid}/grade-assignments", HttpStatusCode.OK, JsonArray((gradeAssignments ?? []).ToArray()));
            handler.Map($"/teachers/{tid}/activity-assignments", HttpStatusCode.OK, JsonArray((activityAssignments ?? []).ToArray()));
        }

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton<IFeatureFlagService>(new AllEnabledFeatureFlagService());
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
        // The dialog footer's Cancel is the last "Cancel" button (row-level Cancel
        // buttons in the grid render before the footer).
        => cut.FindAll("fluent-button").Last(b => b.TextContent.Contains("Cancel")).Click();

    [TestMethod]
    public async Task CreateMode_RendersProfileAndEmptyAssignmentsGrid()
    {
        var gradeId = Guid.NewGuid();
        RegisterFor(grades: new[] { (gradeId, "Grade 5", 5) });
        var cut = RenderProvider();

        var task = OpenAsync(cut, new TeacherEditDialog.TeacherFormModel { TeacherId = null });

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Markup.Should().Contain("Name");
        cut.Markup.Should().Contain("First name");
        cut.Markup.Should().Contain("Last name");
        cut.Markup.Should().Contain("Create Teacher");
        cut.Markup.Should().Contain("Teaching assignments (0)");
        cut.Markup.Should().Contain("+ Grade");
        cut.Markup.Should().Contain("+ Activity");

        Cancel(cut);
        (await task).Should().BeNull();
    }

    [TestMethod]
    public async Task CreateMode_ActivityGroupsFlagOff_HidesActivityButtonAndLoads()
    {
        var gradeId = Guid.NewGuid();
        RegisterFor(grades: new[] { (gradeId, "Grade 5", 5) });
        Services.AddSingleton<IFeatureFlagService>(new ActivityGroupsOffFeatureFlagService());
        var cut = RenderProvider();

        var task = OpenAsync(cut, new TeacherEditDialog.TeacherFormModel { TeacherId = null });

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Markup.Should().Contain("+ Grade");
        cut.Markup.Should().NotContain("+ Activity");
        cut.Markup.Should().NotContain("Could not load");

        Cancel(cut);
        (await task).Should().BeNull();
    }

    [TestMethod]
    public async Task EditMode_LoadsGradeAndActivityRows_ReadOnly()
    {
        var teacherId = Guid.NewGuid();
        var gradeId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var activityId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var gradeRowId = Guid.NewGuid();
        var activityRowId = Guid.NewGuid();

        RegisterFor(
            teacherId: teacherId,
            grades: new[] { (gradeId, "Grade 5", 5) },
            topics: new[] { (subjectId, "MATH", "Mathematics") },
            activities: new[] { (activityId, "Science Club") },
            gradeAssignments: new[]
            {
                GradeAssignmentJson(gradeRowId, gradeId, "Grade 5", 5, subjectId, "Mathematics", "MATH", roleId),
            },
            activityAssignments: new[]
            {
                ActivityAssignmentJson(activityRowId, activityId, "Science Club", roleId, gradeId),
            });
        var cut = RenderProvider();

        var task = OpenAsync(cut, new TeacherEditDialog.TeacherFormModel { TeacherId = teacherId }, "Edit Teacher");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Save Changes"));
        cut.Markup.Should().Contain("Teaching assignments (2)");
        cut.Markup.Should().Contain("Grade 5 · Mathematics");
        cut.Markup.Should().Contain("Science Club · Grade 5");

        Cancel(cut);
        (await task).Should().BeNull();
    }

    [TestMethod]
    public async Task ContextCreate_PrecreatesContextGradeRow()
    {
        var ctxGradeId = Guid.NewGuid();
        RegisterFor(
            contextGradeId: ctxGradeId,
            grades: new[] { (ctxGradeId, "Grade 5", 5) });
        var cut = RenderProvider();

        var task = OpenAsync(cut, new TeacherEditDialog.TeacherFormModel
        {
            TeacherId = null,
            ContextGradeLevelId = ctxGradeId,
            ContextGradeLevelName = "Grade 5",
        });

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Teaching assignments (1)"));

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
        cut.Markup.Should().Contain("B.Sc");
        var options = cut.FindAll("fluent-option");
        options.Select(o => o.TextContent).Should().Contain("M.Sc");
        options.Select(o => o.TextContent).Should().NotContain("B.Sc",
            "the selected qualification is excluded from the add-picker");

        Cancel(cut);
        (await task).Should().BeNull();
    }
}
