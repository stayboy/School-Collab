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
using TopicDto = SchoolCollab.Students.Core.DTOs.TopicDto;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the routable Grade-Level Create / Edit pages
/// (grade-level-detail-view-plan.md §7). Verifies each page mounts without
/// errors against a scripted HTTP backend and renders its expected form
/// controls / preloaded state.
/// </summary>
[TestClass]
public class GradeLevelCreateEditPageTests : BunitContext
{
    public GradeLevelCreateEditPageTests()
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
            if (_responses.TryGetValue((request.Method.Method.ToUpperInvariant(), url), out var exact)) found = exact;
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

    private static ClaimsPrincipal CreateUser()
    {
        var claims = new[] { new Claim("tenant_id", Guid.NewGuid().ToString()), new Claim("tenant_name", "Hydeson") };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestScheme"));
    }

    private sealed class MutableAuth : AuthenticationStateProvider
    {
        private ClaimsPrincipal _user = new();
        public ClaimsPrincipal User { set { _user = value; NotifyAuthenticationStateChanged(GetAuthenticationStateAsync()); } }
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(_user));
    }

    private ScriptedHandler RegisterBase()
    {
        var auth = new MutableAuth { User = CreateUser() };
        var handler = new ScriptedHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton<AuthenticationStateProvider>(auth);
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));
        return handler;
    }

    private static string GradeJson(Guid gradeId, string name = "Grade 5") =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = gradeId, ["codedValueId"] = Guid.NewGuid(), ["level"] = 5, ["name"] = name,
            ["displayOrder"] = 5, ["topicCount"] = 0, ["studentCount"] = 0,
            ["createdAt"] = DateTimeOffset.UnixEpoch, ["updatedAt"] = DateTimeOffset.UnixEpoch,
            ["minAge"] = 10, ["maxAge"] = 12, ["allowedGenderCodedValueId"] = (Guid?)null,
            ["isBlockedFromEnrollment"] = false,
        });

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

    [TestMethod]
    public void Create_Page_RendersFormAndLoadsTopics()
    {
        var handler = RegisterBase();
        handler.Map("GET", "/students/topics", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/api/coded-values/by-parent?parentCode=GRADE", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/api/coded-values/by-parent?parentCode=GENDER", HttpStatusCode.OK, "[]");

        var cut = Render<Create>();

        cut.Markup.Should().Contain("New Grade Level");
        cut.Markup.Should().Contain("No topics exist yet.", "empty catalog shows the info bar in the topics section");
        cut.Markup.Should().Contain(">Create</", "the submit button label is Create");
    }

    [TestMethod]
    public void Edit_Page_LoadsGradeAndRendersSave()
    {
        var gradeId = Guid.NewGuid();
        var handler = RegisterBase();
        handler.Map("GET", $"/students/grade-levels/{gradeId}", HttpStatusCode.OK, GradeJson(gradeId, "Grade 5"));
        handler.Map("GET", "/students/topics", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/students/topic-assignments/by-grade/{gradeId}", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/api/coded-values/by-parent?parentCode=GRADE", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/api/coded-values/by-parent?parentCode=GENDER", HttpStatusCode.OK, "[]");

        var cut = Render<Edit>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Edit — Grade 5"));
        cut.Markup.Should().Contain(">Save</", "the submit button label is Save");
    }

    [TestMethod]
    public void Edit_Page_NotFound_ShowsMessage()
    {
        var gradeId = Guid.NewGuid();
        var handler = RegisterBase();
        handler.Map("GET", $"/students/grade-levels/{gradeId}", HttpStatusCode.NotFound, "");
        handler.Map("GET", "/students/topics", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/api/coded-values/by-parent?parentCode=GRADE", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/api/coded-values/by-parent?parentCode=GENDER", HttpStatusCode.OK, "[]");

        var cut = Render<Edit>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Grade level not found."));
    }

    /// <summary>
    /// Regression (round fluentui-dead-binding): the page's Topics picker bound the dead
    /// <c>SelectedValues</c> pair — absent from FluentUI 4.14.2 list components — so Blazor
    /// dropped it into the catch-all <c>AdditionalAttributes</c> (a stray
    /// <c>selectedvalues</c> HTML attribute) and a pick never reached
    /// <c>_selectedTopicIds</c>. The label's count is projected from that field, so it is
    /// the visible proof the selection landed.
    /// </summary>
    [TestMethod]
    public async Task Create_Page_TopicPicker_BindsTheSupportedApi_AndTheSelectionReachesTheField()
    {
        var topicA = Guid.NewGuid();
        var topicB = Guid.NewGuid();
        var handler = RegisterBase();
        handler.Map("GET", "/students/topics", HttpStatusCode.OK, JsonSerializer.Serialize(new[]
        {
            TopicJson(topicA, "Algebra"),
            TopicJson(topicB, "Geometry"),
        }));
        handler.Map("GET", "/api/coded-values/by-parent?parentCode=GRADE", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/api/coded-values/by-parent?parentCode=GENDER", HttpStatusCode.OK, "[]");

        var cut = Render<Create>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Algebra", "the topic catalog renders as picker options"));
        cut.Markup.Should().Contain("Topics (0)");

        cut.Markup.Should().NotContain("selectedvalues",
            "the dead SelectedValues binding must not leak into the DOM");
        cut.FindAll("fluent-listbox").Should().BeEmpty(
            "the supported control is the multi-select FluentSelect (the skills' multi-select route)");

        var picker = cut.FindComponent<FluentSelect<TopicDto>>();
        picker.Instance.Multiple.Should().BeTrue();
        await cut.InvokeAsync(() => picker.Instance.SelectedOptionsChanged.InvokeAsync(
            picker.Instance.Items!.Where(t => t.Id == topicB).ToArray()));

        // The label's count is projected from _selectedTopicIds — the page's single
        // source of truth — so it is the visible proof the picker's callback landed.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Topics (1)"));
    }

    /// <summary>
    /// Regression (round fluentui-dead-binding): the Edit page's picker must render the
    /// grade's loaded assignments as selected, and a changed selection must reach the
    /// submit diff — assigning the picked topic and unassigning the dropped one. With the
    /// dead binding the loaded baseline could never change, so neither call was issued.
    /// </summary>
    [TestMethod]
    public async Task Edit_Page_TopicPicker_ShowsLoadedAssignments_AndSubmitsThePickedSet()
    {
        var gradeId = Guid.NewGuid();
        var topicA = Guid.NewGuid();
        var topicB = Guid.NewGuid();
        var assignmentA = Guid.NewGuid();

        var handler = RegisterBase();
        handler.Map("GET", $"/students/grade-levels/{gradeId}", HttpStatusCode.OK, GradeJson(gradeId, "Grade 5"));
        handler.Map("GET", "/students/topics", HttpStatusCode.OK, JsonSerializer.Serialize(new[]
        {
            TopicJson(topicA, "Algebra"),
            TopicJson(topicB, "Geometry"),
        }));
        handler.Map("GET", $"/students/topic-assignments/by-grade/{gradeId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(new[] { AssignmentJson(assignmentA, gradeId, topicA) }));
        handler.Map("GET", "/api/coded-values/by-parent?parentCode=GRADE", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/api/coded-values/by-parent?parentCode=GENDER", HttpStatusCode.OK, "[]");
        handler.Map("PUT", $"/students/grade-levels/{gradeId}", HttpStatusCode.NoContent, "");
        handler.Map("POST", "/students/topic-assignments/grade", HttpStatusCode.Created,
            JsonSerializer.Serialize(new Dictionary<string, object?> { ["id"] = Guid.NewGuid() }));
        handler.Map("DELETE", $"/students/topic-assignments/{assignmentA}", HttpStatusCode.NoContent, "");

        var cut = Render<Edit>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Algebra"));

        cut.Markup.Should().NotContain("selectedvalues",
            "the dead SelectedValues binding must not leak into the DOM");
        cut.FindAll("fluent-listbox").Should().BeEmpty();
        cut.Markup.Should().Contain("Topics (1)", "the loaded assignment is the field's single source of truth");
        cut.FindAll("fluent-option").Single(o => o.GetAttribute("value") == topicA.ToString())
            .HasAttribute("selected").Should().BeTrue("the loaded assignment renders as selected");
        cut.FindAll("fluent-option").Single(o => o.GetAttribute("value") == topicB.ToString())
            .HasAttribute("selected").Should().BeFalse("an unassigned topic must not render as selected");

        var picker = cut.FindComponent<FluentSelect<TopicDto>>();
        await cut.InvokeAsync(() => picker.Instance.SelectedOptionsChanged.InvokeAsync(
            picker.Instance.Items!.Where(t => t.Id == topicB).ToArray()));

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
    }
}
