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
/// bUnit tests for the TopicStrandsDialog / TopicLessonsDialog that host the
/// shared StrandsEditor / LessonsEditor (grade-detail-rich-grids-plan.md §5).
/// Rendered directly (no FluentDialog host): the Close button guards a null
/// cascade, and the hosted editor loads its rows via the scripted backend.
/// </summary>
[TestClass]
public class TopicDialogsBunitTests : BunitContext
{
    public TopicDialogsBunitTests()
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

    private static string StrandJson(Guid strandId, Guid topicId, string name) =>
        JsonSerializer.Serialize(new[] { new Dictionary<string, object?>
        {
            ["id"] = strandId, ["topicId"] = topicId, ["name"] = name,
            ["description"] = (string?)null, ["displayOrder"] = 0,
            ["createdAt"] = System.DateTimeOffset.UnixEpoch, ["updatedAt"] = System.DateTimeOffset.UnixEpoch,
        } });

    private static string LessonJson(Guid lessonId, Guid topicId, string name) =>
        JsonSerializer.Serialize(new[] { new Dictionary<string, object?>
        {
            ["id"] = lessonId, ["topicId"] = topicId, ["strandId"] = (Guid?)null,
            ["name"] = name, ["description"] = (string?)null,
            ["startDate"] = (string?)null, ["endDate"] = (string?)null,
            ["displayOrder"] = 0,
            ["createdAt"] = System.DateTimeOffset.UnixEpoch, ["updatedAt"] = System.DateTimeOffset.UnixEpoch,
        } });

    [TestMethod]
    public void TopicStrandsDialog_HostsStrandsEditor_AndListsStrands()
    {
        var topicId = Guid.NewGuid();
        var strandId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map($"/students/topics/{topicId}/strands", HttpStatusCode.OK, StrandJson(strandId, topicId, "Numbers"));
        Register(handler);

        var cut = Render<TopicStrandsDialog>(p =>
        {
            p.Add(x => x.TopicId, topicId);
            p.Add(x => x.TopicName, "Mathematics");
        });

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Numbers"));
        cut.Markup.Should().Contain("Strands (1)");
        cut.Markup.Should().Contain("Close");
    }

    [TestMethod]
    public void TopicLessonsDialog_HostsLessonsEditor_AndListsLessons()
    {
        var topicId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map($"/students/topics/{topicId}/lessons", HttpStatusCode.OK, LessonJson(lessonId, topicId, "Add"));
        handler.Map($"/students/topics/{topicId}/strands", HttpStatusCode.OK, "[]");
        Register(handler);

        var cut = Render<TopicLessonsDialog>(p =>
        {
            p.Add(x => x.TopicId, topicId);
            p.Add(x => x.TopicName, "Mathematics");
        });

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Add"));
        cut.Markup.Should().Contain("Close");
    }
}
