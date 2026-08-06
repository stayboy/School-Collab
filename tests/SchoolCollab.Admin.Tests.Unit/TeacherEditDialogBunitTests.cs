using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Students;
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
/// bUnit tests for the teacher create/edit dialog (grade-detail-rich-grids-plan.md §5 /
/// cg/6). The dialog loads profile options + topics (with per-topic roles) + grade levels
/// and prefills them when editing. The Fluent inputs (dropdowns, checkboxes, date picker)
/// render in shadow DOM, so this asserts load/render + prefill state (the "Subjects (N)" /
/// "Grade levels (N)" counts reflect the loaded topic-role and grade-level selections);
/// the save HTTP contract is covered by the client + CQRS tests.
/// </summary>
[TestClass]
public class TeacherEditDialogBunitTests : BunitContext
{
    public TeacherEditDialogBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Dictionary<(string Method, string Url), (HttpStatusCode Status, string Body)> _responses = new();
        public ScriptedHandler Map(string url, HttpStatusCode status, string body)
        {
            _responses[("ANY", url)] = (status, body);
            return this;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.PathAndQuery;
            foreach (var kv in _responses)
            {
                if (kv.Key.Method != "ANY") continue;
                if (url.Equals(kv.Key.Url, System.StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith(kv.Key.Url, System.StringComparison.OrdinalIgnoreCase))
                    return new HttpResponseMessage(kv.Value.Status) { Content = new StringContent(kv.Value.Body, Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent($"Unexpected {url}", Encoding.UTF8, "application/json") };
        }
    }

    private void Register(ScriptedHandler handler)
    {
        var auth = new MutableAuthenticationStateProvider
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("tenant_id", Guid.NewGuid().ToString()),
                new Claim("tenant_name", "Hydeson"),
            }, "t"))
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton<AuthenticationStateProvider>(auth);
        var cv = new CodedValuesApiClient(http);
        Services.AddSingleton(cv);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, cv));
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));
    }

    private sealed class MutableAuthenticationStateProvider : AuthenticationStateProvider
    {
        private ClaimsPrincipal _u = new();
        public ClaimsPrincipal User { set { _u = value; NotifyAuthenticationStateChanged(GetAuthenticationStateAsync()); } }
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(_u));
    }

    private static string JsonArray(params object[] items) => JsonSerializer.Serialize(items);

    private static Dictionary<string, object?> TeacherJson(Guid id, string first, string last) => new()
    {
        ["id"] = id, ["titleCodedValueId"] = (Guid?)null,
        ["firstName"] = first, ["lastName"] = last, ["displayName"] = (string?)null,
        ["genderCodedValueId"] = (Guid?)null, ["dateOfBirth"] = (DateOnly?)null,
        ["levelOfEducationCodedValueId"] = (Guid?)null,
        ["qualificationCodedValueIds"] = Array.Empty<Guid>(),
        ["isDeleted"] = false,
        ["createdAt"] = "2026-01-01T00:00:00Z", ["updatedAt"] = "2026-01-01T00:00:00Z",
    };

    private static void ScriptCommon(ScriptedHandler h, Guid topicId, Guid gradeId, Guid qualifId)
    {
        h.Map("/students/topics", HttpStatusCode.OK, JsonArray(
            new Dictionary<string, object?> { ["id"] = topicId, ["codedValueId"] = (Guid?)Guid.NewGuid(), ["code"] = "MATH", ["name"] = "Mathematics", ["description"] = (string?)null, ["displayOrder"] = 1, ["createdAt"] = "2026-01-01T00:00:00Z", ["updatedAt"] = "2026-01-01T00:00:00Z" }));
        h.Map("/students/grade-levels", HttpStatusCode.OK, JsonArray(
            new Dictionary<string, object?> { ["id"] = gradeId, ["codedValueId"] = Guid.NewGuid(), ["level"] = 5, ["name"] = "Grade 5", ["displayOrder"] = 1, ["topicCount"] = 0, ["studentCount"] = 0, ["createdAt"] = "2026-01-01T00:00:00Z", ["updatedAt"] = "2026-01-01T00:00:00Z" }));
        h.Map("/api/coded-values/by-parent?parentCode=QUALIF", HttpStatusCode.OK, JsonArray(
            new Dictionary<string, object?> { ["id"] = qualifId, ["code"] = "BSC", ["name"] = "B.Sc", ["description"] = (string?)null, ["parentId"] = (Guid?)null, ["parentCode"] = "QUALIF", ["isDisabled"] = false, ["displayOrder"] = 1 }));
        // Profile/topic role dropdown option sources (render in shadow DOM; empty is fine).
        h.Map("/api/coded-values/by-parent?parentCode=SALUTS", HttpStatusCode.OK, "[]");
        h.Map("/api/coded-values/by-parent?parentCode=GENDER", HttpStatusCode.OK, "[]");
        h.Map("/api/coded-values/by-parent?parentCode=EDUCLEVEL", HttpStatusCode.OK, "[]");
        h.Map("/api/coded-values/by-parent?parentCode=TCHROLES", HttpStatusCode.OK, "[]");
    }

    [TestMethod]
    public void CreateMode_RendersProfileTopicsAndGradeLevels()
    {
        var topicId = Guid.NewGuid();
        var gradeId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        ScriptCommon(handler, topicId, gradeId, Guid.NewGuid());
        Register(handler);

        var cut = Render<TeacherEditDialog>(p => p.Add(x => x.TeacherId, (Guid?)null));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("First name"));
        cut.Markup.Should().Contain("Create Teacher");
        cut.Markup.Should().Contain("Subjects (0)");
        cut.Markup.Should().Contain("Grade levels (0)");
        cut.Markup.Should().Contain("Mathematics");
        cut.Markup.Should().Contain("Grade 5");
    }

    [TestMethod]
    public void EditMode_PrefillsTopicsWithRolesAndGradeLevels()
    {
        var teacherId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var gradeId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        ScriptCommon(handler, topicId, gradeId, Guid.NewGuid());

        // Existing teacher: teaches Mathematics with role, and Grade 5.
        handler.Map($"/teachers/{teacherId}", HttpStatusCode.OK, JsonSerializer.Serialize(TeacherJson(teacherId, "Jane", "Doe")));
        handler.Map($"/teachers/{teacherId}/topics/roles", HttpStatusCode.OK, JsonArray(
            new Dictionary<string, object?> { ["topicId"] = topicId, ["roleCodedValueId"] = roleId }));
        handler.Map($"/teachers/{teacherId}/grade-levels", HttpStatusCode.OK, JsonArray(
            new Dictionary<string, object?> { ["id"] = gradeId, ["codedValueId"] = Guid.NewGuid(), ["level"] = 5, ["name"] = "Grade 5", ["displayOrder"] = 1, ["topicCount"] = 0, ["studentCount"] = 0, ["createdAt"] = "2026-01-01T00:00:00Z", ["updatedAt"] = "2026-01-01T00:00:00Z" }));
        Register(handler);

        var cut = Render<TeacherEditDialog>(p => p.Add(x => x.TeacherId, teacherId));

        // Prefill: one topic selected (with a role) and one grade level selected.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Save Changes"));
        cut.Markup.Should().Contain("Subjects (1)");
        cut.Markup.Should().Contain("Grade levels (1)");
        cut.Markup.Should().Contain("Mathematics");
        cut.Markup.Should().Contain("Grade 5");
    }
}
