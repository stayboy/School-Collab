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
}
