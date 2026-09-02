using System.Net;
using System.Text;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Pages.Periods;
using SchoolCollab.Students.Application.Services;
using System.Security.Claims;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the PeriodUpsert page (create mode) hosted against the
/// StudentsApiClient HTTP integration boundary
/// (documents/specs/period-create-edit-single-page.md). Covers the blocked-parent
/// panel: when ?parent= points at a None-division year, the page shows a warning +
/// back affordance and renders NO editable form fields (review P2-2).
/// </summary>
[TestClass]
public class PeriodCreatePageTests : BunitContext
{
    public PeriodCreatePageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Dictionary<(string Method, string Url), (HttpStatusCode Status, string Body)> _responses = new();

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            _responses[(method.ToUpperInvariant(), url)] = (status, body);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.PathAndQuery;
            if (_responses.TryGetValue((request.Method.Method.ToUpperInvariant(), url), out var exact))
                return Task.FromResult(new HttpResponseMessage(exact.Status)
                {
                    Content = new StringContent(exact.Body, Encoding.UTF8, "application/json"),
                });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected {url}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class FakeAuth : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("tenant_id", Guid.NewGuid().ToString()) }, "Test"))));
    }

    private ScriptedHandler RegisterClient()
    {
        var auth = new FakeAuth();
        var handler = new ScriptedHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton<AuthenticationStateProvider>(auth);
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));
        return handler;
    }

    private static string YearJson(Guid id, string name, string division, string status) =>
        $"{{\"id\":\"{id}\",\"name\":\"{name}\",\"startDate\":\"2024-01-01\",\"endDate\":\"2024-12-31\",\"status\":\"{status}\",\"division\":\"{division}\",\"parentPeriodId\":null,\"nextPeriodId\":null,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}";

    private static readonly Guid BlockedParentId = Guid.Parse("99999999-9999-9999-9999-999999999996");

    /// <summary>P2-2 review fix: rendering PeriodUpsert (create mode) with ?parent=
    /// pointing at a None-division year surfaces the blocked-parent warning + back
    /// affordance and renders NO editable form fields (no Division selector, no Submit).</summary>
    [TestMethod]
    public void BlockedParentPanel_Shows_WhenParentDivisionNone()
    {
        var handler = RegisterClient();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK,
            "[" + YearJson(BlockedParentId, "ParentYear", "None", "Active") + "]");

        Services.GetRequiredService<NavigationManager>().NavigateTo($"/students/periods/create?parent={BlockedParentId}", forceLoad: true);
        var cut = Render<PeriodUpsert>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("sub-periods are not allowed",
            "the blocked-parent warning is shown when ?parent= points at a None-division year"));
        cut.Markup.Should().Contain("Back to periods",
            "the back-to-list affordance is shown");
        cut.FindAll("fluent-button")
            .Any(b => b.TextContent.Contains("Create period")).Should().BeFalse(
            "no Create submit button renders — the editable form is gated on !_parentBlocked");
        cut.FindAll("fluent-select").Any(s => s.TextContent.Contains("Terms")).Should().BeFalse(
            "no Division selector renders — the form fields block is gated on !_parentBlocked");
    }
}
