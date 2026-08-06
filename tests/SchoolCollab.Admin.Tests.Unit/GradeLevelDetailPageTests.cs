using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Pages.Students.GradeLevels;
using SchoolCollab.Students.Application.Services;
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

    private (ScriptedHandler Handler, Guid GradeId) Register(
        Guid gradeId,
        string gradeJson,
        string topicsCatalogJson = "[]",
        string teachersJson = "[]",
        string assignmentsJson = "[]",
        string studentsJson = "[]",
        string curriculumJson = "[]")
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

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton<AuthenticationStateProvider>(auth);
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));
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

        // Three equally-sized section cards render with titles + counts + anchors.
        cut.Markup.Should().Contain("class=\"section-card__title\">Topics</span>");
        cut.Markup.Should().Contain("class=\"section-card__title\">Teachers</span>");
        cut.Markup.Should().Contain("class=\"section-card__title\">Students</span>");
        cut.Markup.Should().Contain("View all topics (0)");
        cut.Markup.Should().Contain("View all teachers (0)");
        cut.Markup.Should().Contain("View all students (0)");
    }

    [TestMethod]
    public void Detail_TopicsCard_ListsPreview_AndCount()
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
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("View all topics (1)"));
        cut.Markup.Should().Contain("Mathematics", "top-15 preview lists the topic name");
        cut.Markup.Should().Contain("MATH", "topic code renders in the preview");
    }

    [TestMethod]
    public void Detail_TopicsCard_ShowsEmptyState_WhenNoAssignments()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId), assignmentsJson: "[]");

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("View all topics (0)"));
        cut.Markup.Should().Contain("No topics assigned to this grade yet");
    }

    [TestMethod]
    public void Detail_TeachersCard_ListsPreview_AndCount()
    {
        var gradeId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        Register(
            gradeId,
            GradeJson(gradeId),
            teachersJson: JsonSerializer.Serialize(new[]
            {
                TeacherJson(teacherId, "Jane", "Doe", "jane@example.com", roleId: null, (topicId, "Mathematics")),
            }));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("View all teachers (1)"));
        cut.Markup.Should().Contain("Jane Doe", "top-15 preview lists the teacher name");
    }

    [TestMethod]
    public void Detail_TeachersCard_ShowsEmptyState_WhenNone()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("View all teachers (0)"));
        cut.Markup.Should().Contain("No teachers linked to this grade yet");
    }

    [TestMethod]
    public void Detail_StudentsCard_ListsStudents_WithDemographics()
    {
        var gradeId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var genderId = Guid.NewGuid();
        var (handler, _) = Register(
            gradeId,
            GradeJson(gradeId),
            studentsJson: JsonSerializer.Serialize(new[]
            {
                StudentJson(studentId, "STU001", "Ada", "Lovelace", genderId, new DateOnly(2015, 3, 10)),
            }));
        handler.Map("/api/coded-values/by-ids", HttpStatusCode.OK,
            JsonSerializer.Serialize(new[] { new Dictionary<string, object?>
            {
                ["id"] = genderId, ["code"] = "GENDERS_FEMALE", ["name"] = "Female",
                ["parentId"] = (Guid?)null, ["parentCode"] = (string?)null,
                ["description"] = (string?)null, ["isDisabled"] = false,
                ["displayOrder"] = 0, ["createdAt"] = DateTimeOffset.UnixEpoch,
                ["updatedAt"] = DateTimeOffset.UnixEpoch, ["attributes"] = Array.Empty<object>(),
                ["childCount"] = 0,
            } }));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("View all students (1)"));
        cut.Markup.Should().Contain("Ada Lovelace", "top-15 preview lists the student name");
        cut.Markup.Should().Contain("Female", "demographics suffix carries the enriched gender");
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
            "the Topics card's View-all anchor opens GradeTopicsDialog via the read-only helper");
        source.Should().Contain("ShowReadonlyDialogAsync<GradeTeachersDialog>(",
            "the Teachers card's View-all anchor opens GradeTeachersDialog via the read-only helper");

        // GradeTopicsDialog gets the assigned topics + assignable catalog + action callbacks.
        source.Should().Contain("nameof(GradeTopicsDialog.Topics)");
        source.Should().Contain("nameof(GradeTopicsDialog.UnassignedTopics)");
        source.Should().Contain("nameof(GradeTopicsDialog.Remove)");
        source.Should().Contain("nameof(GradeTopicsDialog.Assign)");

        // GradeTeachersDialog gets the linked teachers + catalog + name maps + callbacks.
        source.Should().Contain("nameof(GradeTeachersDialog.Teachers)");
        source.Should().Contain("nameof(GradeTeachersDialog.UnlinkedTeachers)");
        source.Should().Contain("nameof(GradeTeachersDialog.RoleChanged)");
        source.Should().Contain("nameof(GradeTeachersDialog.UnlinkTopic)");

        // Students card's View-all navigates to the grade-filtered students landing.
        source.Should().Contain("View all students");
        source.Should().Contain("/students?gradeLevelId=");

        // The old segmented pill tab control is gone.
        source.Should().NotContain("grade-tabs__bar");
        source.Should().NotContain("SetActiveTab");
    }
}
