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
/// bUnit tests for the TopicStrandsDialog that hosts the unified StrandsEditor
/// (root strands + their lessons — strand-lesson-unification-plan.md). Rendered
/// directly (no FluentDialog host): the Close button guards a null cascade, and
/// the hosted editor loads its rows via the scripted backend.
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

    private static Dictionary<string, object?> StrandRow(Guid strandId, Guid topicId, string name, Guid? parentStrandId) => new()
    {
        ["id"] = strandId, ["topicId"] = topicId, ["parentStrandId"] = parentStrandId,
        ["name"] = name, ["description"] = (string?)null,
        ["startDate"] = (string?)null, ["endDate"] = (string?)null,
        ["isLesson"] = parentStrandId.HasValue, ["displayOrder"] = 0,
        ["createdAt"] = DateTimeOffset.UnixEpoch, ["updatedAt"] = DateTimeOffset.UnixEpoch,
    };

    private static string StrandJson(params Dictionary<string, object?>[] rows) =>
        JsonSerializer.Serialize(rows);

    [TestMethod]
    public void TopicStrandsDialog_HostsStrandsEditor_AndListsStrandsAndLessons()
    {
        var topicId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map($"/students/topics/{topicId}/strands", HttpStatusCode.OK,
            StrandJson(StrandRow(rootId, topicId, "Numbers", null), StrandRow(lessonId, topicId, "Add", rootId)));
        Register(handler);

        var cut = Render<TopicStrandsDialog>(p => p.Add(x => x.Content, new DialogParameters
        {
            [TopicStrandsDialog.TopicIdKey] = topicId,
            [TopicStrandsDialog.TopicNameKey] = "Mathematics",
        }));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Numbers"));
        cut.Markup.Should().Contain("Strands (1)", "one root strand");
        cut.Markup.Should().Contain("Add", "a lesson renders under its root strand");
        cut.Markup.Should().Contain("New Lesson");
        cut.Markup.Should().Contain("Close");
    }
}
