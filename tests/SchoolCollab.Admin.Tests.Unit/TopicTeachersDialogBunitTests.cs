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
/// bUnit tests for the <see cref="TopicTeachersDialog"/> (grade-detail-rich-grids-plan.md §5 —
/// "topic dialog assigns teachers + roles"). The dialog loads the topic's teachers via
/// ListTopicTeachersAsync and lists each with a role dropdown. The Fluent role dropdown and
/// teacher picker render in shadow DOM, so this asserts the loaded rows + affordances render;
/// the underlying assign/set-role/unlink HTTP contracts are covered by
/// <see cref="TopicTeacherClientRoutesTests"/> and the CQRS handler tests.
/// </summary>
[TestClass]
public class TopicTeachersDialogBunitTests : BunitContext
{
    public TopicTeachersDialogBunitTests()
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

    private static TeacherWithRoleDto Teacher(Guid id, string first, string last) => new(
        Id: id, TitleCodedValueId: null, FirstName: first, LastName: last, DisplayName: null,
        GenderCodedValueId: null, DateOfBirth: null, LevelOfEducationCodedValueId: null,
        QualificationCodedValueIds: [], IsDeleted: false, TeacherRoleCodedValueId: null,
        AssignedTopics: [], CreatedAt: DateTimeOffset.UnixEpoch, UpdatedAt: DateTimeOffset.UnixEpoch);

    [TestMethod]
    public void Dialog_ListsTopicTeachers_AndShowsPicker()
    {
        var topicId = Guid.NewGuid();
        var janeId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map($"/students/topics/{topicId}/teachers", HttpStatusCode.OK,
            JsonSerializer.Serialize(new[]
            {
                new Dictionary<string, object?>
                {
                    ["teacherId"] = janeId, ["titleCodedValueId"] = (Guid?)null,
                    ["firstName"] = "Jane", ["lastName"] = "Doe",
                    ["displayName"] = (string?)null, ["roleCodedValueId"] = roleId,
                },
            }));
        handler.Map("/api/coded-values/by-parent?parentCode=TCHROLES", HttpStatusCode.OK, "[]");
        Register(handler);

        // The grade's teacher pool (Jane already on the topic, Bob available to add).
        var jane = Teacher(janeId, "Jane", "Doe");
        var bob = Teacher(Guid.NewGuid(), "Bob", "Smith");

        var cut = Render<TopicTeachersDialog>(p =>
        {
            p.Add(x => x.TopicId, topicId);
            p.Add(x => x.TopicName, "Mathematics");
            p.Add(x => x.AvailableTeachers, new[] { jane, bob });
        });

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Jane Doe"));
        cut.Markup.Should().Contain("Teachers — Mathematics");
        cut.Markup.Should().Contain("Add a teacher");
        cut.Markup.Should().Contain("Bob Smith"); // available-pool picker option
        cut.Markup.Should().Contain("Close");
    }

    [TestMethod]
    public void Dialog_EmptyState_WhenNoTeachersAssigned()
    {
        var topicId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map($"/students/topics/{topicId}/teachers", HttpStatusCode.OK, "[]");
        handler.Map("/api/coded-values/by-parent?parentCode=TCHROLES", HttpStatusCode.OK, "[]");
        Register(handler);

        var cut = Render<TopicTeachersDialog>(p =>
        {
            p.Add(x => x.TopicId, topicId);
            p.Add(x => x.TopicName, "Mathematics");
            p.Add(x => x.AvailableTeachers, new[] { Teacher(Guid.NewGuid(), "Bob", "Smith") });
        });

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No teachers assigned to this topic yet"));
        cut.Markup.Should().Contain("Close");
    }
}
