using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Application.Components.Pages.Students.GradeLevels;
using SchoolCollab.Students.Application.Services;
using SchoolCollab.Students.Core.Contracts;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the Grade-Level Detail page (grade-level-detail-view-plan.md §5):
/// Overview card + the three equal section cards (Topics / Teachers / Students)
/// with top-15 preview lists, count chips, and "View all" anchors. The full
/// management grids moved into <c>GradeTopicsDialog</c> / <c>GradeTeachersDialog</c>,
/// which are covered separately in <c>GradeDialogsBunitTests</c>.
/// </summary>
[TestClass]
public class GradeLevelDetailPageTests : BunitContext
{
    private const string RoleParentUrl = "/api/coded-values/by-parent?parentCode=TCHROLES";

    public GradeLevelDetailPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private static string ReadDetailSource()
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir, "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Application",
            "Components", "Pages", "Students", "GradeLevels", "Detail.razor"));
        File.Exists(srcPath).Should().BeTrue($"Detail.razor should exist at '{srcPath}'");
        return File.ReadAllText(srcPath);
    }

    private static string ReadSectionCardSource()
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir, "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Application",
            "Components", "Students", "SectionCard.razor"));
        File.Exists(srcPath).Should().BeTrue($"SectionCard.razor should exist at '{srcPath}'");
        return File.ReadAllText(srcPath);
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
        public ScriptedHandler Map(string url, HttpStatusCode status, string body) => Map("ANY", url, status, body);

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
                return new HttpResponseMessage(hit.Status) { Content = new StringContent(hit.Body, Encoding.UTF8, "application/json") };
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected URL: {request.Method.Method} {url}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private static ClaimsPrincipal CreateUser(bool realTenant)
    {
        var tenantId = realTenant ? Guid.NewGuid().ToString() : Guid.Empty.ToString();
        var claims = new[] { new Claim("tenant_id", tenantId), new Claim("tenant_name", realTenant ? "Hydeson" : "System") };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestScheme"));
    }

    private sealed class MutableAuthenticationStateProvider : AuthenticationStateProvider
    {
        private ClaimsPrincipal _user = new();
        public ClaimsPrincipal User { set { _user = value; NotifyAuthenticationStateChanged(GetAuthenticationStateAsync()); } }
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(_user));
    }

    /// <summary>
    /// Hosts a <see cref="FluentDialogProvider"/> alongside a child component so
    /// destructive row actions (which show a confirmation prompt via
    /// <c>IDialogService</c>) can render their dialog in the provider.
    /// </summary>
    private sealed class DialogHost : ComponentBase
    {
        [Parameter] public RenderFragment? ChildContent { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<FluentDialogProvider>(0);
            builder.CloseComponent();
            builder.AddContent(1, ChildContent);
        }
    }

    private static Dictionary<string, object?> StreamJson(Guid id, string name, string code) =>
        new()
        {
            ["id"] = id, ["code"] = code, ["name"] = name,
            ["description"] = (string?)null, ["parentId"] = (Guid?)null, ["parentCode"] = "GRSTREAMS",
            ["isDisabled"] = false, ["displayOrder"] = 0,
            ["createdAt"] = DateTimeOffset.UnixEpoch, ["updatedAt"] = DateTimeOffset.UnixEpoch,
            ["attributes"] = Array.Empty<object>(), ["attributeDefinitions"] = Array.Empty<object>(),
        };

    private (ScriptedHandler Handler, Guid GradeId) Register(
        Guid gradeId,
        string gradeJson,
        string topicsCatalogJson = "[]",
        string teachersJson = "[]",
        string assignmentsJson = "[]",
        string studentsJson = "[]",
        string curriculumJson = "[]",
        string streamsJson = "[]")
    {
        var auth = new MutableAuthenticationStateProvider { User = CreateUser(realTenant: true) };
        var handler = new ScriptedHandler();
        handler.Map("GET", $"/students/grade-levels/{gradeId}", HttpStatusCode.OK, gradeJson);
        handler.Map("GET", "/students/topics", HttpStatusCode.OK, topicsCatalogJson);
        handler.Map("GET", $"/students/grade-levels/{gradeId}/teachers", HttpStatusCode.OK, teachersJson);
        handler.Map("GET", "/teachers", HttpStatusCode.OK, teachersJson);
        handler.Map("GET", $"/students/topic-assignments/by-grade/{gradeId}", HttpStatusCode.OK, assignmentsJson);
        handler.Map("GET", $"/students/by-grade/{gradeId}", HttpStatusCode.OK, studentsJson);
        handler.Map("GET", $"/students/grade-levels/{gradeId}/curriculum", HttpStatusCode.OK, curriculumJson);
        // Role dropdown (TCHROLES) parent lookup.
        handler.Map("GET", RoleParentUrl, HttpStatusCode.OK, "[]");
        // Grade streams (GRSTREAMS) for the Streams card.
        handler.Map("/api/coded-values/by-parent?parentCode=GRSTREAMS", HttpStatusCode.OK, streamsJson);
        // Notification &amp; Delivery editor: no tenant default / no grade override.
        handler.Map("GET", "/api/settings/notification-policy", HttpStatusCode.NoContent, "");
        handler.Map("GET", $"/students/grade-levels/{gradeId}/notification-policy", HttpStatusCode.NoContent, "");
        handler.Map("PUT", $"/students/grade-levels/{gradeId}/notification-policy", HttpStatusCode.OK, "");

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton<AuthenticationStateProvider>(auth);
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        var api = new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient);
        Services.AddSingleton(api);
        // The ContactsEditor (rendered by the student create/edit dialogs)
        // injects IContactsClient, which the app maps to StudentsApiClient.
        Services.AddSingleton<IContactsClient>(api);
        Services.AddSingleton(new NotificationPolicyApiClient(http));
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));

        return (handler, gradeId);
    }

    private static string GradeJson(Guid gradeId, string name = "Grade 5", bool blocked = false) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = gradeId,
            ["codedValueId"] = Guid.NewGuid(),
            ["level"] = 5,
            ["name"] = name,
            ["displayOrder"] = 5,
            ["topicCount"] = 1,
            ["studentCount"] = 3,
            ["createdAt"] = DateTimeOffset.UnixEpoch,
            ["updatedAt"] = DateTimeOffset.UnixEpoch,
            ["minAge"] = 10,
            ["maxAge"] = 12,
            ["allowedGenderCodedValueId"] = (Guid?)null,
            ["isBlockedFromEnrollment"] = blocked,
        });

    private static Dictionary<string, object?> AssignmentJson(Guid assignmentId, Guid topicId, Guid gradeId) =>
        new()
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

    private static Dictionary<string, object?> TeacherJson(
        Guid teacherId,
        string firstName,
        string lastName,
        string email,
        Guid? roleId = null,
        params (Guid TopicId, string TopicName)[] topics) =>
        new()
        {
            ["id"] = teacherId,
            ["titleCodedValueId"] = (Guid?)null,
            ["firstName"] = firstName,
            ["lastName"] = lastName,
            ["displayName"] = (string?)null,
            ["email"] = email,
            ["contactPhone"] = (string?)null,
            ["isDeleted"] = false,
            ["teacherRoleCodedValueId"] = roleId,
            ["assignedTopics"] = topics.Select(t => new Dictionary<string, object?>
            {
                ["id"] = t.TopicId,
                ["codedValueId"] = (Guid?)null,
                ["code"] = (string?)null,
                ["name"] = t.TopicName,
                ["description"] = (string?)null,
                ["displayOrder"] = 0,
                ["createdAt"] = DateTimeOffset.UnixEpoch,
                ["updatedAt"] = DateTimeOffset.UnixEpoch,
            }).ToArray(),
            ["createdAt"] = DateTimeOffset.UnixEpoch,
            ["updatedAt"] = DateTimeOffset.UnixEpoch,
        };

    private static Dictionary<string, object?> StudentJson(
        Guid studentId, string studentNumber, string first, string last,
        Guid? genderId, DateOnly? dob) => new()
    {
        ["id"] = studentId,
        ["studentNumber"] = studentNumber,
        ["titleCodedValueId"] = (Guid?)null,
        ["firstName"] = first,
        ["lastName"] = last,
        ["dateOfBirth"] = dob,
        ["genderCodedValueId"] = genderId,
        ["isDeleted"] = false,
        ["createdAt"] = DateTimeOffset.UnixEpoch,
        ["updatedAt"] = DateTimeOffset.UnixEpoch,
    };

    [TestMethod]
    public void Detail_Overview_ShowsGradeNameAndSectionCards()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId, "Grade 5"));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Grade 5"));

        cut.Markup.Should().Contain("Level");
        cut.Markup.Should().Contain("10–12", "age range renders as min–max");
        cut.Markup.Should().Contain("3 students");

        // Three equally-sized section cards render with titles + counts.
        cut.Markup.Should().Contain("Subjects", "Subjects card title renders");
        cut.Markup.Should().Contain("Teachers", "Teachers card title renders");
        cut.Markup.Should().Contain("Students", "Students card title renders");
    }

    [TestMethod]
    public void Detail_TopicsCard_Row_HasKebab_WithSecondaryActions()
    {
        var gradeId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        Register(
            gradeId,
            GradeJson(gradeId),
            topicsCatalogJson: JsonSerializer.Serialize(new[] { new Dictionary<string, object?>
            {
                ["id"] = topicId, ["codedValueId"] = (Guid?)null, ["code"] = "MATH",
                ["name"] = "Mathematics", ["description"] = (string?)null,
                ["displayOrder"] = 0, ["createdAt"] = DateTimeOffset.UnixEpoch, ["updatedAt"] = DateTimeOffset.UnixEpoch,
            } }),
            assignmentsJson: JsonSerializer.Serialize(new[] { AssignmentJson(Guid.NewGuid(), topicId, gradeId) }),
            curriculumJson: JsonSerializer.Serialize(new[] { new Dictionary<string, object?>
            {
                ["topicId"] = topicId, ["name"] = "Mathematics", ["code"] = "MATH",
                ["strandCount"] = 2, ["lessonCount"] = 3,
            } }));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("View all subjects (1)"));

        // Counts are informational (not links) now.
        cut.Markup.Should().Contain("2 strands", "strand count renders as plain text");
        cut.Markup.Should().Contain("3 lessons", "lesson count renders as plain text");

        // Open the topic row kebab to surface its inline menu items. The topic
        // name itself is the primary affordance (opens the topic edit dialog),
        // so the kebab hosts the remaining secondary actions.
        cut.Find("fluent-button[title=\"Actions for Mathematics\"]").Click();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Strands", "kebab offers strands"));
        cut.Markup.Should().Contain("Teachers", "kebab offers teachers");
        cut.Markup.Should().Contain("Remove", "kebab offers remove");
    }

    [TestMethod]
    public void Detail_TopicsCard_ShowsEmptyState_WhenNoAssignments()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId), assignmentsJson: "[]");

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("View all subjects (0)"));
        cut.Markup.Should().Contain("No subjects assigned to this curriculum yet");
    }

    [TestMethod]
    public void Detail_TeachersCard_ShowsEmptyState_WhenNone()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Teachers"));
        cut.Markup.Should().Contain("No teachers linked to this grade yet");
    }

    [TestMethod]
    public void Detail_TeachersCard_Row_HasKebab_WithActions()
    {
        var gradeId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId), teachersJson: JsonSerializer.Serialize(new[]
        {
            TeacherJson(teacherId, "Jane", "Doe", "jane@example.com"),
        }));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Jane Doe"));

        // The teacher name is the primary affordance (navigates to the teacher
        // detail page); the kebab hosts the secondary actions + destructive Remove.
        cut.Find("fluent-button[title=\"Actions for Jane Doe\"]").Click();
        var items = cut.FindAll("fluent-menu-item").Select(i => i.TextContent.Trim()).ToArray();
        items.Should().Contain("Role", "teachers kebab offers role");
        items.Should().Contain("View profile", "teachers kebab offers view profile");
        items.Should().Contain("Edit", "teachers kebab offers edit");
        items.Should().Contain("Remove", "teachers kebab offers remove");
    }

    [TestMethod]
    public void Detail_TeachersCard_Remove_Confirms_AndUnlinks()
    {
        var gradeId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var (handler, _) = Register(gradeId, GradeJson(gradeId), teachersJson: JsonSerializer.Serialize(new[]
        {
            TeacherJson(teacherId, "Jane", "Doe", "jane@example.com"),
        }));
        handler.Map("DELETE", $"/teachers/{teacherId}/grade-levels/{gradeId}", HttpStatusCode.OK, "");

        // Host the page under a FluentDialogProvider so the destructive Remove
        // confirmation prompt can render.
        var cut = Render<DialogHost>(p => p
            .AddChildContent<Detail>(child => child.Add(x => x.Id, gradeId)));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Jane Doe"));

        // Open the teacher kebab and click Remove.
        cut.Find("fluent-button[title=\"Actions for Jane Doe\"]").Click();
        var removeItem = cut.FindAll("fluent-menu-item").First(i => i.TextContent.Contains("Remove"));
        removeItem.Click();

        // The destructive Remove opens a MODAL confirmation dialog.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Remove teacher 'Jane Doe' from this grade"));
        cut.WaitForAssertion(() => cut.FindAll(".confirm-dialog fluent-button[appearance='accent']").Any());

        // Confirm → the teacher is unlinked from this grade (stays in the catalog).
        cut.Find(".confirm-dialog fluent-button[appearance='accent']").Click();
        cut.WaitForAssertion(() => handler.Calls.Should().Contain(c =>
            c.Method == "DELETE" && c.Url == $"/teachers/{teacherId}/grade-levels/{gradeId}"));
    }

    [TestMethod]
    public void Detail_TeachersCard_Wires_PageErrorAlert()
    {
        // The Teachers card surfaces _teachersError (set by the mutation handlers /
        // ReloadTeachersAsync) via a PAGE-LEVEL message alert above the card —
        // the same pattern the Subjects card uses for _topicsError. Not the
        // SectionCard ErrorMessage param.
        var source = ReadDetailSource();
        source.Should().Contain("_teachersError",
            "the Teachers card surfaces _teachersError");
        source.Should().Contain("FluentMessageBar", "the error renders as a page message bar");
        source.Should().NotContain("ErrorMessage=\"@_teachersError\"",
            "the Teachers card does NOT use the SectionCard ErrorMessage param — page alert instead");
    }

    [TestMethod]
    public void Detail_TeachersCard_Add_OpensTeacherCreateDialog()
    {
        var source = ReadDetailSource();

        // The Teachers card Add button must open the shared TeacherEditDialog in
        // Create mode (new teacher), not just the link-existing GradeTeachersDialog.
        source.Should().Contain("OnAddClick=\"OpenTeacherCreateAsync\"",
            "the Teachers card Add button opens the create dialog");
        source.Should().Contain("ShowShellDialogAsync<TeacherEditDialog, TeacherEditDialog.TeacherFormModel, TeacherDto>",
            "the create handler opens TeacherEditDialog via the dialog shell");
        source.Should().Contain("ContextGradeLevelId = Id",
            "the create handler passes the current grade as the context grade");
        source.Should().Contain("ContextGradeLevelName = _grade.Name",
            "the create handler passes the context grade name");
    }

    [TestMethod]
    public void Detail_TeachersCard_Add_Click_OpensTeacherCreateDialog()
    {
        // BEHAVIORAL check (vs the source-only test above): clicking the Teachers
        // card Add button must actually open the shared TeacherEditDialog in Create
        // mode. This is the flow the source-inspection test never executes.
        var gradeId = Guid.NewGuid();
        var (handler, _) = Register(gradeId, GradeJson(gradeId));

        // Register IFeatureFlagService (required by TeacherEditDialog)
        Services.AddSingleton<IFeatureFlagService>(new StubFlagService { Enabled = true });
        Services.AddSingleton<IFeatureFlagChangeNotifier>(new StubFlagNotifier());

        // Endpoints the TeacherEditDialog hits on init (create mode).
        handler.Map("GET", "/students/grade-levels", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/api/coded-values/by-parent?parentCode=QUALIF", HttpStatusCode.OK, "[]");

        var cut = Render<DialogHost>(p => p
            .AddChildContent<Detail>(child => child.Add(x => x.Id, gradeId)));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Teachers"));

        // The Subjects card (1st) and Teachers card (2nd) both use the SectionCard
        // default AddTitle="Add"; Students/Streams use distinct titles. So the
        // Teachers card's Add button is the 2nd "Add" button inside a section card.
        cut.FindAll("fluent-card.section-card-wrapper fluent-button[title=\"Add\"]")[1].Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain(
            "New Teacher",
            "clicking Add on the Teachers card opens the shared TeacherEditDialog in create mode"));
    }

    [TestMethod]
    public void Detail_StudentsCard_Edit_Click_OpensStudentEditDialog()
    {
        // BEHAVIORAL check: clicking the Students card kebab Edit must open the
        // shared StudentEditDialog pre-loaded with the row's student.
        var gradeId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var (handler, _) = Register(gradeId, GradeJson(gradeId), studentsJson: JsonSerializer.Serialize(new[]
        {
            StudentJson(studentId, "STU001", "Ada", "Lovelace", null, new DateOnly(2015, 3, 10)),
        }));
        handler.Map("GET", $"/students/{studentId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(StudentJson(studentId, "STU001", "Ada", "Lovelace", null, new DateOnly(2015, 3, 10))));
        // The all-inclusive edit dialog also loads the student's guardians + contacts.
        handler.Map("GET", $"/students/{studentId}/guardians", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/contacts?ownerType=Student&ownerId={studentId}", HttpStatusCode.OK, "[]");

        var cut = Render<DialogHost>(p => p
            .AddChildContent<Detail>(child => child.Add(x => x.Id, gradeId)));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Ada Lovelace"));

        cut.Find("fluent-button[title=\"Actions for Ada Lovelace\"]").Click();
        var editItem = cut.FindAll("fluent-menu-item").First(i => i.TextContent.Contains("Edit"));
        editItem.Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain(
            "Save Changes",
            "clicking Edit on the Students card opens the shared StudentEditDialog"));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain(
            "Ada", "the edit dialog is pre-loaded with the selected student"));
    }

    [TestMethod]
    public void Detail_StudentsCard_Subitem_Click_OpensStudentEditDialog_WithStudentId()
    {
        // Regression: clicking a student's name in the Students section card
        // must open StudentEditDialog with that student's real Id (not an
        // empty guid). If an empty guid were passed, the dialog's
        // GET /students/{id} load would miss and show the error instead of
        // the pre-loaded student.
        var gradeId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var (handler, _) = Register(gradeId, GradeJson(gradeId), studentsJson: JsonSerializer.Serialize(new[]
        {
            StudentJson(studentId, "STU001", "Ada", "Lovelace", null, new DateOnly(2015, 3, 10)),
        }));
        handler.Map("GET", $"/students/{studentId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(StudentJson(studentId, "STU001", "Ada", "Lovelace", null, new DateOnly(2015, 3, 10))));
        // The all-inclusive edit dialog also loads the student's guardians + contacts.
        handler.Map("GET", $"/students/{studentId}/guardians", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/contacts?ownerType=Student&ownerId={studentId}", HttpStatusCode.OK, "[]");

        var cut = Render<DialogHost>(p => p
            .AddChildContent<Detail>(child => child.Add(x => x.Id, gradeId)));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Ada Lovelace"));

        // Click the student's name (the section-card subitem anchor).
        cut.Find(".item-name").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain(
            "Save Changes",
            "clicking the student subitem opens the shared StudentEditDialog"));
        // Definitive check: the FirstName input must be bound to the loaded
        // student profile. A mere "Ada" match on the markup is a false positive
        // (the dialog title is "Edit Student · Ada Lovelace"). If StudentId were
        // an empty guid (never bound via ShowReadonlyDialogAsync), the dialog's
        // GET /students/{id} would 404 and the field would render blank.
        cut.WaitForAssertion(() => cut.Find("#studentFormFirstName").GetAttribute("value")
            .Should().Be("Ada",
                "the edit dialog's FirstName input binds the loaded student (StudentId must be a real guid)"));
    }

    [TestMethod]
    public void Detail_StudentsCard_Subitem_Click_PassesCorrectStudent_WhenMultiple()
    {
        // Regression: with multiple students, clicking a specific subitem must
        // pass THAT student's Id to the edit dialog — not the last student, not
        // an empty guid (a classic @foreach closure-capture mistake).
        var gradeId = Guid.NewGuid();
        var adaId = Guid.NewGuid();
        var graceId = Guid.NewGuid();
        var (handler, _) = Register(gradeId, GradeJson(gradeId), studentsJson: JsonSerializer.Serialize(new[]
        {
            StudentJson(adaId, "STU001", "Ada", "Lovelace", null, new DateOnly(2015, 3, 10)),
            StudentJson(graceId, "STU002", "Grace", "Hopper", null, new DateOnly(2016, 4, 20)),
        }));
        handler.Map("GET", $"/students/{adaId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(StudentJson(adaId, "STU001", "Ada", "Lovelace", null, new DateOnly(2015, 3, 10))));
        handler.Map("GET", $"/students/{graceId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(StudentJson(graceId, "STU002", "Grace", "Hopper", null, new DateOnly(2016, 4, 20))));
        // The all-inclusive edit dialog also loads each student's guardians + contacts.
        handler.Map("GET", $"/students/{adaId}/guardians", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/contacts?ownerType=Student&ownerId={adaId}", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/students/{graceId}/guardians", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/contacts?ownerType=Student&ownerId={graceId}", HttpStatusCode.OK, "[]");

        var cut = Render<DialogHost>(p => p
            .AddChildContent<Detail>(child => child.Add(x => x.Id, gradeId)));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Ada Lovelace"));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Grace Hopper"));

        // Click the SECOND student's name (Grace Hopper).
        cut.FindAll(".item-name")[1].Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain(
            "Save Changes",
            "clicking a student subitem opens the shared StudentEditDialog"));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain(
            "Grace", "the edit dialog pre-loads the clicked student (not the last/empty one)"));
        cut.Markup.Should().NotContain("Could not load",
            "the dialog must receive a real Id, not an empty guid that 404s");
    }

    [TestMethod]
    public void Detail_StudentsCard_ShowsEmptyState_WhenNoStudents()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId), studentsJson: "[]");

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("View all students (0)"));
        cut.Markup.Should().Contain("No active students in this grade for the current period.");
    }

    [TestMethod]
    public void Detail_StudentsCard_Row_HasKebab_WithActions()
    {
        var gradeId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId), studentsJson: JsonSerializer.Serialize(new[]
        {
            StudentJson(studentId, "STU001", "Ada", "Lovelace", null, new DateOnly(2015, 3, 10)),
        }));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Ada Lovelace"));

        // The student name is the primary affordance (navigates to the student
        // view page); the kebab hosts the secondary actions + destructive Remove.
        cut.Find("fluent-button[title=\"Actions for Ada Lovelace\"]").Click();
        var items = cut.FindAll("fluent-menu-item").Select(i => i.TextContent.Trim()).ToArray();
        items.Should().Contain("Transfer", "students kebab offers transfer");
        items.Should().Contain("Withdraw", "students kebab offers withdraw");
        items.Should().Contain("View profile", "students kebab offers view profile");
        items.Should().Contain("Edit", "students kebab offers edit");
        items.Should().Contain("Remove", "students kebab offers remove");
    }

    [TestMethod]
    public void Detail_StudentsCard_Remove_Confirms_AndSoftDeletes()
    {
        var gradeId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var (handler, _) = Register(gradeId, GradeJson(gradeId), studentsJson: JsonSerializer.Serialize(new[]
        {
            StudentJson(studentId, "STU001", "Ada", "Lovelace", null, new DateOnly(2015, 3, 10)),
        }));
        handler.Map("DELETE", $"/students/{studentId}", HttpStatusCode.OK, "");

        // Host the page under a FluentDialogProvider so the destructive Remove
        // confirmation prompt can render.
        var cut = Render<DialogHost>(p => p
            .AddChildContent<Detail>(child => child.Add(x => x.Id, gradeId)));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Ada Lovelace"));

        // Open the student kebab and click Remove.
        cut.Find("fluent-button[title=\"Actions for Ada Lovelace\"]").Click();
        var removeItem = cut.FindAll("fluent-menu-item").First(i => i.TextContent.Contains("Remove"));
        removeItem.Click();

        // The destructive Remove opens a MODAL confirmation dialog.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Remove student 'Ada Lovelace'?"));
        cut.WaitForAssertion(() => cut.FindAll(".confirm-dialog fluent-button[appearance='accent']").Any());

        // Confirm → the whole student record is soft-deleted (not an enrollment-withdraw).
        cut.Find(".confirm-dialog fluent-button[appearance='accent']").Click();
        cut.WaitForAssertion(() => handler.Calls.Should().Contain(c =>
            c.Method == "DELETE" && c.Url == $"/students/{studentId}"));
    }

    [TestMethod]
    public void Detail_StudentsCard_Withdraw_ResolvesEnrollment_AndOpensDialog()
    {
        var gradeId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();
        var (handler, _) = Register(gradeId, GradeJson(gradeId), studentsJson: JsonSerializer.Serialize(new[]
        {
            StudentJson(studentId, "STU001", "Ada", "Lovelace", null, new DateOnly(2015, 3, 10)),
        }));
        handler.Map("GET", $"/students/enrollments/by-student/{studentId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(new[]
            {
                new Dictionary<string, object?>
                {
                    ["id"] = enrollmentId, ["studentId"] = studentId, ["periodId"] = Guid.NewGuid(),
                    ["gradeLevelId"] = gradeId, ["streamCodedValueId"] = (Guid?)null,
                    ["enrolledOn"] = "2025-01-01", ["exitDate"] = (string?)null,
                    ["status"] = "Active",
                    ["createdAt"] = DateTimeOffset.UnixEpoch, ["updatedAt"] = DateTimeOffset.UnixEpoch,
                },
            }));

        // Host the page under a FluentDialogProvider so the WithdrawEnrollmentDialog
        // (opened via ShowShellDialogAsync) can render.
        var cut = Render<DialogHost>(p => p
            .AddChildContent<Detail>(child => child.Add(x => x.Id, gradeId)));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Ada Lovelace"));

        // Open the student kebab and click Withdraw.
        cut.Find("fluent-button[title=\"Actions for Ada Lovelace\"]").Click();
        var withdrawItem = cut.FindAll("fluent-menu-item").First(i => i.TextContent.Contains("Withdraw"));
        withdrawItem.Click();

        // The WithdrawEnrollmentDialog opens with the resolved active enrollment.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("This will set the exit date on the current enrollment."));
    }

    [TestMethod]
    public void Detail_StreamsCard_ShowsEmptyState_WhenNone()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No streams defined for this grade yet."));
    }

    [TestMethod]
    public void Detail_StreamsCard_Row_HasKebab_WithEditAndRemove()
    {
        var gradeId = Guid.NewGuid();
        var streamId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId), streamsJson: JsonSerializer.Serialize(new[]
        {
            StreamJson(streamId, "Grade 5A", "GR5A"),
        }));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Grade 5A"));

        // The stream name is the primary affordance (opens the edit dialog); the
        // kebab hosts the secondary actions (Edit + destructive Remove).
        cut.Find("fluent-button[title=\"Actions for Grade 5A\"]").Click();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Edit", "streams kebab offers edit"));
        cut.Markup.Should().Contain("Remove", "streams kebab offers remove");
    }

    [TestMethod]
    public void Detail_StreamsCard_Remove_Confirms_AndDisables()
    {
        var gradeId = Guid.NewGuid();
        var streamId = Guid.NewGuid();
        var (handler, _) = Register(gradeId, GradeJson(gradeId), streamsJson: JsonSerializer.Serialize(new[]
        {
            StreamJson(streamId, "Grade 5A", "GR5A"),
        }));
        handler.Map("POST", $"/api/coded-values/{streamId}/disable", HttpStatusCode.OK, "");

        // Host the page under a FluentDialogProvider so the destructive Remove
        // confirmation prompt can render.
        var cut = Render<DialogHost>(p => p
            .AddChildContent<Detail>(child => child.Add(x => x.Id, gradeId)));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Grade 5A"));

        // Open the stream kebab and click Remove.
        cut.Find("fluent-button[title=\"Actions for Grade 5A\"]").Click();
        var removeItem = cut.FindAll("fluent-menu-item").First(i => i.TextContent.Contains("Remove"));
        removeItem.Click();

        // The destructive Remove opens a MODAL confirmation dialog.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Remove stream 'Grade 5A'?"));
        cut.WaitForAssertion(() => cut.FindAll(".confirm-dialog fluent-button[appearance='accent']").Any());

        // Confirm → the stream is disabled (coded-value lifecycle, not a hard delete).
        cut.Find(".confirm-dialog fluent-button[appearance='accent']").Click();
        cut.WaitForAssertion(() => handler.Calls.Should().Contain(c =>
            c.Method == "POST" && c.Url == $"/api/coded-values/{streamId}/disable"));
    }

    [TestMethod]
    public void Detail_StreamsCard_RendersErrorMessage_OnLoadFailure()
    {
        var gradeId = Guid.NewGuid();
        // Invalid JSON makes the streams fetch throw, so the card must surface a
        // page-level error alert (the Subjects/Topic card pattern) instead of
        // failing silently. The card's empty state may still render below the
        // alert — the alert itself is the contract.
        Register(gradeId, GradeJson(gradeId), streamsJson: "not-json");

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Could not load streams",
            "a load failure renders a page-level error alert, not a silent empty state"));
    }

    [TestMethod]
    public void Detail_StreamsAdd_OpensCodedValueDialog_CreateMode()
    {
        var source = ReadDetailSource();

        // The Streams card "Add" affordance must open the shared CodedValueDialog
        // in Create mode (not navigate away to the GRSTREAMS children page).
        source.Should().Contain("OnAddClick=\"OpenStreamCreateAsync\"",
            "the Streams card Add button opens the create dialog, not a navigation");
        source.Should().Contain("CodedValueFormModel.ForCreate",
            "the create handler opens CodedValueDialog in Create mode");
        source.Should().Contain("CodedValueParent.Streams.ToCode()",
            "the create handler resolves the GRSTREAMS parent to create under");
        source.Should().Contain("SetAttributeAsync",
            "the create handler tags the new stream with the grade's gradeLevel attribute");
        source.Should().Contain("ReloadStreamsAsync",
            "the create handler reloads the streams card after creating");

        // The old navigation-away behavior is gone.
        source.Should().NotContain("OpenStreamsAsync", "the navigation-away handler is removed");
    }

    [TestMethod]
    public void Detail_EnrollmentToggle_ReflectsBlockedState()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId, blocked: false));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Allowed"));

        // The overview reflects the not-blocked state as a status badge, and the
        // enrollment toggle is grouped in the header "Actions" menu (not a switch).
        cut.Markup.Should().Contain("Enrollment");
        cut.Find("fluent-button[title='Actions']").Should().NotBeNull();

        // Open the header Actions menu → Edit + the enrollment toggle render inline.
        cut.Find("fluent-button[title='Actions']").Click();
        cut.Markup.Should().Contain("Edit");
        cut.Markup.Should().Contain("Block enrollment");
    }

    [TestMethod]
    public void Detail_ViewAll_Wires_TopicsAndTeachers_Dialogs()
    {
        var source = ReadDetailSource();

        source.Should().Contain("ShowReadonlyDialogAsync<GradeTopicsDialog>(",
            "the Subjects card's View-all opens GradeTopicsDialog via the read-only helper");

        // GradeTopicsDialog gets the assigned topics + assignable catalog + action callbacks
        // (passed via the Content DialogParameters indexer keys).
        source.Should().Contain("GradeTopicsDialog.TopicsKey");
        source.Should().Contain("GradeTopicsDialog.UnassignedTopicsKey");
        source.Should().Contain("GradeTopicsDialog.RemoveKey");
        source.Should().Contain("GradeTopicsDialog.AssignKey");

        // Students card's View-all navigates to the grade-filtered students landing.
        source.Should().Contain("View all students");
        source.Should().Contain("/students?gradeLevelId=");

        // Subjects card's Add button opens the shared TopicCreateDialog (add-new
        // subject/topic wired to the grade), and its View-all opens GradeTopicsDialog
        // to assign existing topics / manage the assigned list.
        source.Should().Contain("OnAddClick=\"OpenTopicCreateAsync\"",
            "the Subjects card's Add button opens the topic create dialog");
        source.Should().Contain("OnViewAllClick=\"OpenTopicsDialogAsync\"",
            "the Subjects card's View-all opens GradeTopicsDialog");

        // The old segmented pill tab control is gone.
        source.Should().NotContain("grade-tabs__bar");
        source.Should().NotContain("SetActiveTab");
    }

    [TestMethod]
    public void Detail_SubjectsCard_Add_OpensTopicCreateDialog()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("View all subjects (0)"));

        // Bug fix: the Subjects card's Add button must open the shared
        // TopicCreateDialog (add-new subject/topic wired to the grade), following
        // the same DialogShellBase pattern as the topic edit dialog — not just the
        // assign-existing-topics GradeTopicsDialog. The subject is display-only;
        // the underlying entity is a Topic, and creating one never renames an
        // existing topic.
        var source = ReadDetailSource();
        source.Should().Contain("OnAddClick=\"OpenTopicCreateAsync\"",
            "the Subjects card's Add button opens the topic create dialog");
        source.Should().Contain("ShowShellDialogAsync<", "the add handler uses the shell dialog service");
        source.Should().Contain("TopicCreateDialog", "the add handler opens TopicCreateDialog");
        source.Should().Contain("GradeLevelId = _grade.Id",
            "the add handler passes the grade context so the subject is wired to the grade");
    }

    [TestMethod]
    public void Detail_TopicLine_OpensTopicEditDialog()
    {
        var source = ReadDetailSource();

        // The topic name is the primary affordance in the Subjects card and opens
        // the topic edit dialog (rename / code / description), not the strands dialog.
        source.Should().Contain("ItemOnClick=\"t => OpenTopicEditAsync(t)\"",
            "the topic name click must invoke the edit-dialog handler");
        source.Should().Contain("ItemNameTitle=\"Edit topic\"",
            "the topic anchor advertises the edit affordance");

        // The edit handler opens TopicEditDialog through the shell dialog helper.
        source.Should().Contain("ShowShellDialogAsync<", "the topic edit handler uses the shell dialog service");
        source.Should().Contain("TopicEditDialog", "the topic edit handler opens TopicEditDialog");
        source.Should().Contain("size: DialogSize.Large",
            "the topic edit dialog opens large to fit the inline strands editor");

        // Strands stay reachable (moved to the row kebab, not removed).
        source.Should().Contain("OpenStrandsAsync", "strands remain available via the row kebab");
        source.Should().Contain("ShowReadonlyDialogAsync<TopicStrandsDialog>",
            "strands are opened via the read-only helper");
    }

    [TestMethod]
    public void Detail_SectionCards_HaveAddIconButtons()
    {
        var source = ReadDetailSource();

        // Each card uses the SectionCard component with add button parameters.
        source.Should().Contain("<SectionCard", "SectionCard component is used for all three cards");
        source.Should().Contain("OnAddClick=\"OpenTopicCreateAsync\"", "Subjects card has add callback");
        source.Should().Contain("OnAddClick=\"OpenTeacherCreateAsync\"", "Teachers card has add callback");
        source.Should().Contain("OnAddClick=\"OpenStudentCreateAsync\"", "Students card has add callback");
        source.Should().Contain("AddTitle=\"Add student\"", "Students card has add title");
        source.Should().Contain("AddAriaLabel=\"Add student\"", "Students card has add aria-label");
    }

    [TestMethod]
    public void Detail_SectionCards_Wire_ItemSelectors_And_PrimaryAffordances()
    {
        // The SectionCard rendering mechanics (text/meta/href/click/tooltip) are
        // covered once in SectionCardTests.cs. These source-inspection assertions
        // verify the per-card WIRING — which selector / primary affordance each
        // card binds — so a card can't silently regress to a different selector.
        var source = ReadDetailSource();

        // Subjects card: topic name + strand/lesson counts; name opens the edit dialog.
        source.Should().Contain("ItemTextSelector=\"t => t.Name\"", "Subjects card binds the topic name");
        source.Should().Contain("ItemMetaSelector=\"@(t => [ $", "Subjects card binds strand/lesson counts");
        source.Should().Contain("ItemOnClick=\"t => OpenTopicEditAsync(t)\"", "Subjects card name opens the topic edit dialog");
        source.Should().Contain("ItemKeySelector=\"t => t.TopicId\"", "Subjects card opts into the central edit-key guard (TopicId)");
        source.Should().Contain("OnItemActionBlocked=\"OnTopicEditBlocked\"", "Subjects card surfaces the guard block");
        source.Should().Contain("ItemNameTitle=\"Edit topic\"", "Subjects card advertises the edit affordance");

        // Teachers card: display name + role; name navigates to the teacher detail page.
        source.Should().Contain("ItemTextSelector=\"t => GetTeacherDisplayName(t)\"", "Teachers card binds the teacher display name");
        source.Should().Contain("ItemMetaSelector=\"@(t => [ GetTeacherRole(t) ])\"", "Teachers card binds the role meta");
        source.Should().Contain("ItemHrefSelector=\"@(t => $\"/students/teachers/{t.Id}\")\"", "Teachers card name navigates to the teacher detail page");

        // Students card: full name + demographics; name opens the edit dialog.
        source.Should().Contain("ItemTextSelector=\"@(st => $", "Students card binds the student full name");
        source.Should().Contain("ItemMetaSelector=\"@(st => [ GetStudentDemographics(st) ])\"", "Students card binds demographics meta");
        source.Should().Contain("ItemOnClick=\"st => OpenStudentEditAsync(st)\"", "Students card name opens the student edit dialog");
        source.Should().Contain("ItemKeySelector=\"st => st.Id\"", "Students card opts into the central edit-key guard (Id)");
        source.Should().Contain("OnItemActionBlocked=\"OnStudentEditBlocked\"", "Students card surfaces the guard block");
        source.Should().Contain("ItemNameTitle=\"Edit student\"", "Students card advertises the edit affordance");

        // Streams card: stream name; name opens the stream edit dialog.
        source.Should().Contain("ItemTextSelector=\"s => s.Name\"", "Streams card binds the stream name");
        source.Should().Contain("ItemOnClick=\"s => OpenStreamEditAsync(s)\"", "Streams card name opens the stream edit dialog");
        source.Should().Contain("ItemKeySelector=\"s => s.Id\"", "Streams card opts into the central edit-key guard (Id)");
        source.Should().Contain("OnItemActionBlocked=\"OnStreamEditBlocked\"", "Streams card surfaces the guard block");
        source.Should().Contain("ItemNameTitle=\"Edit stream\"", "Streams card advertises the edit affordance");
    }

    [TestMethod]
    public void Detail_StudentAdd_OpensStudentCreateDialog()
    {
        var source = ReadDetailSource();

        // The Students card Add button must open the shared StudentCreateDialog
        // (new student), not the enroll-existing StudentPickerDialog.
        source.Should().Contain("OnAddClick=\"OpenStudentCreateAsync\"",
            "the Students card Add button opens the create dialog");
        source.Should().Contain("ShowReadonlyDialogAsync<StudentCreateDialog>",
            "the create handler opens StudentCreateDialog");
        source.Should().NotContain("OpenAddStudentsAsync",
            "the enroll-existing handler is removed from the card Add");
    }

    [TestMethod]
    public void Detail_NotificationEditor_IsWired()
    {
        var source = ReadDetailSource();

        source.Should().Contain("GradeNotificationPolicyEditor",
            "the merged nd/4 feature hosts the per-grade notification & delivery editor on the grade detail page");
        source.Should().Contain("notification-card",
            "the editor is wrapped in a Notification & Delivery card below the section cards");
        source.Should().Contain("Notification &amp; Delivery",
            "the card is titled 'Notification & Delivery'");
    }

    private sealed class StubFlagService : IFeatureFlagService
    {
        public bool Enabled { get; set; }
        public bool IsEnabled(string featureKey) => Enabled;
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) => Task.FromResult(Enabled);
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    }

    private sealed class StubFlagNotifier : IFeatureFlagChangeNotifier
    {
        public event Action? FeatureFlagsChanged;
        public void Raise() => FeatureFlagsChanged?.Invoke();
    }
}
