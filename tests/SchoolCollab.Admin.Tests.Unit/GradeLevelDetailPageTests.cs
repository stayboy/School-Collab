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
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the Grade-Level Detail page
/// (grade-level-detail-view-plan.md §5): Overview card, Topics &amp; Curriculum
/// tab, and Teachers tab wiring against the real StudentsApiClient /
/// CodedValuesApiClient via a scripted HTTP backend.
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
        string assignmentsJson = "[]")
    {
        var auth = new MutableAuthenticationStateProvider { User = CreateUser(realTenant: true) };
        var handler = new ScriptedHandler();
        handler.Map("GET", $"/students/grade-levels/{gradeId}", HttpStatusCode.OK, gradeJson);
        handler.Map("GET", "/students/topics", HttpStatusCode.OK, topicsCatalogJson);
        handler.Map("GET", $"/students/grade-levels/{gradeId}/teachers", HttpStatusCode.OK, teachersJson);
        handler.Map("GET", "/teachers", HttpStatusCode.OK, teachersJson);
        handler.Map("GET", $"/students/topic-assignments/by-grade/{gradeId}", HttpStatusCode.OK, assignmentsJson);
        // Role dropdown (TCHROLES) parent lookup.
        handler.Map("GET", RoleParentUrl, HttpStatusCode.OK, "[]");
        // Notification &amp; Delivery editor: no tenant default / no grade override.
        handler.Map("GET", "/api/settings/notification-policy", HttpStatusCode.NoContent, "");
        handler.Map("GET", $"/students/grade-levels/{gradeId}/notification-policy", HttpStatusCode.NoContent, "");
        handler.Map("PUT", $"/students/grade-levels/{gradeId}/notification-policy", HttpStatusCode.OK, "");

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton<AuthenticationStateProvider>(auth);
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));
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

    [TestMethod]
    public void Detail_Overview_ShowsGradeNameAndTabs()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId, "Grade 5"));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Grade 5"));

        cut.Markup.Should().Contain("Level");
        cut.Markup.Should().Contain("10–12", "age range renders as min–max");
        cut.Markup.Should().Contain("3 students");
        // Tab panel content renders server-side; the tab-header labels are
        // JS-composed by the FluentTabs web component and are not text in markup.
        cut.Markup.Should().Contain("Assigned Topics");
    }

    [TestMethod]
    public void Detail_TopicsTab_ShowsEmptyState_WhenNoAssignments()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId), assignmentsJson: "[]");

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Assigned Topics (0)"));
        cut.Markup.Should().Contain("No topics assigned to this grade yet");
    }

    [TestMethod]
    public void Detail_TopicsTab_ListsAssignedTopics_AndRemove_CallsRemoveAssignment()
    {
        var gradeId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var (handler, _) = Register(
            gradeId,
            GradeJson(gradeId),
            topicsCatalogJson: JsonSerializer.Serialize(new[] { new Dictionary<string, object?>
            {
                ["id"] = topicId, ["codedValueId"] = (Guid?)null, ["code"] = "MATH",
                ["name"] = "Mathematics", ["description"] = (string?)null,
                ["displayOrder"] = 0, ["createdAt"] = DateTimeOffset.UnixEpoch, ["updatedAt"] = DateTimeOffset.UnixEpoch,
            } }),
            assignmentsJson: JsonSerializer.Serialize(new[] { AssignmentJson(assignmentId, topicId, gradeId) }));
        handler.Map("DELETE", $"/students/topic-assignments/{assignmentId}", HttpStatusCode.NoContent, "");

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Assigned Topics (1)"));
        cut.Markup.Should().Contain("Mathematics");

        // Click the Remove button on the assigned-topic card.
        var remove = cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Remove"));
        remove.Click();
        cut.WaitForAssertion(() =>
            handler.Calls.Any(c => c.Method == "DELETE" && c.Url == $"/students/topic-assignments/{assignmentId}").Should().BeTrue());
    }

    [TestMethod]
    public void Detail_TeachersTab_ListsTeachers_WithRoleDropdown()
    {
        var gradeId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var (handler, _) = Register(
            gradeId,
            GradeJson(gradeId),
            teachersJson: JsonSerializer.Serialize(new[]
            {
                TeacherJson(teacherId, "Jane", "Doe", "jane@example.com", roleId: null,
                    (topicId, "Mathematics")),
            }));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Jane"));
        cut.Markup.Should().Contain("jane@example.com");
        cut.Markup.Should().Contain("Mathematics", "assigned-topic chip is rendered");
        cut.Markup.Should().Contain("Role", "role column header renders");

        // The TCHROLES dropdown fired one parent lookup.
        handler.Calls.Any(c => c.Url.Contains("parentCode=TCHROLES")).Should().BeTrue();
    }

    [TestMethod]
    public void Detail_UnlinkTeacherTopic_CallsUnlinkEndpoint()
    {
        var gradeId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var (handler, _) = Register(
            gradeId,
            GradeJson(gradeId),
            teachersJson: JsonSerializer.Serialize(new[]
            {
                TeacherJson(teacherId, "Jane", "Doe", "jane@example.com", null, (topicId, "Mathematics")),
            }));
        handler.Map("DELETE", $"/teachers/{teacherId}/topics/{topicId}", HttpStatusCode.NoContent, "");

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Jane"));

        var chip = cut.FindAll(".chip").First();
        chip.Click();
        cut.WaitForAssertion(() =>
            handler.Calls.Any(c => c.Method == "DELETE" && c.Url == $"/teachers/{teacherId}/topics/{topicId}").Should().BeTrue());
    }

    [TestMethod]
    public void Detail_AddTeacher_RevealsPicker()
    {
        var gradeId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var (handler, _) = Register(
            gradeId,
            GradeJson(gradeId),
            teachersJson: JsonSerializer.Serialize(Array.Empty<object>()),
            assignmentsJson: "[]");
        // All-teachers catalog feeds the add-teacher picker.
        handler.Map("GET", "/teachers", HttpStatusCode.OK, JsonSerializer.Serialize(new[] { TeacherJson(candidateId, "Bob", "Smith", "bob@example.com") }));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Add Teacher"));

        var addTeacher = cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Add Teacher"));
        addTeacher.Click();
        cut.Markup.Should().Contain("Choose a teacher…");
    }

    [TestMethod]
    public void Detail_EnrollmentToggle_ReflectsBlockedState()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId, blocked: false));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Allowed"));

        // The overview shows a read-only enrollment status (no inline switch).
        cut.Markup.Should().Contain("Enrollment");

        // Open the Actions popover to reveal the allow-enrollment switch.
        cut.Find("#grade-actions-trigger").Click();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Actions"));
        cut.Find("fluent-switch").Should().NotBeNull();
        cut.Markup.Should().Contain("Allow enrollment");
    }
}
