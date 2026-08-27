using System.Net;
using System.Text;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Components.Landing;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Pages.Periods;
using SchoolCollab.Students.Application.Services;
using System.Security.Claims;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the <see cref="SubPeriods"/> page (Sprint 6 §6.3a):
/// loading / empty / error states and the per-status row actions.
/// Locks the Round 1 Item 6 states per plan-ui-sprint6-r2.md §5.3 (T5a/T5b
/// required, T5c optional render-level row-action assertions).
/// </summary>
[TestClass]
public class SubPeriodsPageTests : BunitContext
{
    public SubPeriodsPageTests()
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
                new[] { new Claim("tenant_id", Guid.NewGuid().ToString()), new Claim("tenant_name", "Hydeson") }, "TestScheme"))));
    }

    private static readonly Guid YearId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Term1Id = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Term2Id = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private ScriptedHandler Register()
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

    private static string YearJson() =>
        $"{{\"id\":\"{YearId}\",\"name\":\"2026\",\"startDate\":\"2026-01-01\",\"endDate\":\"2026-12-31\",\"status\":\"Active\",\"periodType\":\"AcademicYear\",\"parentPeriodId\":null,\"nextPeriodId\":null,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}";

    private static string SubPeriodsJson() =>
        $"[{{\"id\":\"{Term1Id}\",\"name\":\"Term 1\",\"startDate\":\"2026-01-01\",\"endDate\":\"2026-06-30\",\"status\":\"Draft\",\"periodType\":\"Term\",\"parentPeriodId\":\"{YearId}\",\"nextPeriodId\":null,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}," +
        $"{{\"id\":\"{Term2Id}\",\"name\":\"Term 2\",\"startDate\":\"2026-07-01\",\"endDate\":\"2026-12-31\",\"status\":\"Active\",\"periodType\":\"Term\",\"parentPeriodId\":\"{YearId}\",\"nextPeriodId\":null,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}]";

    /// <summary>
    /// T5a: an empty sub-period list shows the page's EmptyMessage and the
    /// Create action is enabled for a real tenant (6.3a empty state).
    /// </summary>
    [TestMethod]
    public void SubPeriods_EmptyList_ShowsEmptyMessage()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.OK, YearJson());
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK, "[]");

        var cut = Render<SubPeriods>(p => p.Add(x => x.AcademicYearId, YearId));

        cut.WaitForState(() => cut.Markup.Contains("No sub-periods for this academic year yet."),
            TimeSpan.FromSeconds(5));
        cut.Markup.Should().Contain("2026", "the page title shows the academic year name");
        // The Create action lives in LandingPage's page-toolbar SectionContent,
        // which does not render in a headless bUnit tree — assert the parameter
        // the page passes instead (CreateEnabled = real tenant via FakeAuth).
        cut.FindComponent<LandingPage<PeriodDto>>().Instance.CreateEnabled.Should()
            .BeTrue("a real tenant can create sub-periods");
    }

    /// <summary>
    /// T5b: a failing sub-periods load shows the error message bar and the
    /// Back button instead of the grid (6.3a error state).
    /// </summary>
    [TestMethod]
    public void SubPeriods_LoadError_ShowsErrorBarAndBackButton()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.OK, YearJson());
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.InternalServerError,
            "{\"message\":\"boom\"}");

        var cut = Render<SubPeriods>(p => p.Add(x => x.AcademicYearId, YearId));

        cut.WaitForState(() => cut.Markup.Contains("Back"), TimeSpan.FromSeconds(5));
        cut.Markup.Should().Contain("500", "the load failure surfaces the error message");
        cut.Markup.Should().Contain("Back", "the error state renders a Back button");
        cut.Markup.Should().NotContain("No sub-periods", "the grid is not rendered in the error state");
    }

    /// <summary>
    /// T5c (optional, plan §5.3): row actions render per status — the Draft row
    /// offers Activate and a disabled Complete; the Active row the reverse.
    /// Rendering assertions only (no confirm-dialog / POST driving).
    /// </summary>
    [TestMethod]
    public void SubPeriods_RowActions_RenderPerStatus()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.OK, YearJson());
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK, SubPeriodsJson());

        var cut = Render<SubPeriods>(p => p.Add(x => x.AcademicYearId, YearId));

        cut.WaitForState(() => cut.Markup.Contains("Term 1"), TimeSpan.FromSeconds(5));

        var menus = cut.FindComponents<RowActionsMenu>();
        menus.Should().HaveCount(2, "one actions menu per rendered sub-period row");

        // FluentMenu items only render once the kebab is open — open each row's
        // kebab trigger before asserting its items.
        menus[0].Find("fluent-button").Click();

        // First row = Draft ("Term 1"): Activate enabled, Complete disabled.
        var draftItems = menus[0].FindComponents<FluentMenuItem>()
            .ToDictionary(i => i.Instance.Label!, i => i);
        draftItems.Keys.Should().Contain(new[] { "Edit", "Activate", "Complete" });
        draftItems["Activate"].Instance.Disabled.Should().BeFalse("a Draft sub-period can be activated");
        draftItems["Complete"].Instance.Disabled.Should().BeTrue("a Draft sub-period cannot be completed");

        // Second row = Active ("Term 2"): Activate disabled, Complete enabled.
        menus[1].Find("fluent-button").Click();
        var activeItems = menus[1].FindComponents<FluentMenuItem>()
            .ToDictionary(i => i.Instance.Label!, i => i);
        activeItems["Activate"].Instance.Disabled.Should().BeTrue("an Active sub-period cannot be re-activated");
        activeItems["Complete"].Instance.Disabled.Should().BeFalse("an Active sub-period can be completed");
    }
}