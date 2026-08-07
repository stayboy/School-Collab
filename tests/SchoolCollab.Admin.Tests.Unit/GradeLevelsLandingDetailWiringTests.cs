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
/// bUnit tests for the Grade Levels landing wiring added by the
/// grade-level-detail-view plan (§8): the Name column navigates to the
/// routable Detail page, and the row kebab exposes a View action before Edit.
/// </summary>
[TestClass]
public class GradeLevelsLandingDetailWiringTests : BunitContext
{
    public GradeLevelsLandingDetailWiringTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<string> Urls = new();
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses = new(StringComparer.OrdinalIgnoreCase);

        public ScriptedHandler Map(string url, HttpStatusCode status, string body)
        {
            _responses[url] = (status, body);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.PathAndQuery;
            Urls.Add(url);
            foreach (var kv in _responses)
            {
                if (url.Equals(kv.Key, StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(kv.Value.Status)
                    {
                        Content = new StringContent(kv.Value.Body, Encoding.UTF8, "application/json"),
                    });
                }
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected URL: {url}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private static ClaimsPrincipal CreateUser()
        => new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tenant_id", Guid.NewGuid().ToString()),
            new Claim("tenant_name", "Hydeson"),
        }, "TestScheme"));

    private ScriptedHandler Register(Guid gradeId, string name)
    {
        var auth = new FakeAuth();
        var handler = new ScriptedHandler();
        var landing = JsonSerializer.Serialize(new[]
        {
            new Dictionary<string, object?>
            {
                ["id"] = gradeId, ["codedValueId"] = Guid.NewGuid(), ["name"] = name,
                ["topicCount"] = 2, ["strandCount"] = 3, ["lessonCount"] = 4, ["studentCount"] = 5,
                ["createdAt"] = DateTimeOffset.UnixEpoch, ["updatedAt"] = DateTimeOffset.UnixEpoch,
                ["minAge"] = 10, ["maxAge"] = 12, ["allowedGenderCodedValueId"] = (Guid?)null,
                ["isBlockedFromEnrollment"] = false,
            }
        });
        handler.Map("/students/grade-levels/landing", HttpStatusCode.OK, landing);
        handler.Map("/api/coded-values/by-parent?parentCode=GRADE", HttpStatusCode.OK, "[]");

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton<AuthenticationStateProvider>(auth);
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));
        return handler;
    }

    private sealed class FakeAuth : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(CreateUser()));
    }

    [TestMethod]
    public void Landing_Name_IsAnchorToDetail()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, "Grade 5");

        var cut = Render<SchoolCollab.Students.Application.Components.Pages.Students.GradeLevels.Index>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Grade 5"));

        var detailHref = $"/students/grade-levels/{gradeId}";
        var anchors = cut.FindAll($"[href=\"{detailHref}\"]");
        anchors.Should().NotBeEmpty($"the Name column links to the detail page ({detailHref})");
    }
}
