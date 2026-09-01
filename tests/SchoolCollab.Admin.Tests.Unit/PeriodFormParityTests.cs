using System.Net;
using Bunit;
using FluentAssertions;
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
/// Edit ↔ create parity + Auto-split-on-edit (documents/specs/period-edit-parity-deactivate.md
/// FR-E2/E3/E4/E5, AC-E2/E3). The edit form renders the same sub-periods section with the
/// Auto-split button (FR-E4); Division is disabled on edit (FR-E2); and Auto-split is
/// enabled only when no non-Draft sub-period exists (FR-E5).
/// </summary>
[TestClass]
public class PeriodFormParityTests : BunitContext
{
    private static readonly Guid YearId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");

    public PeriodFormParityTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class Handler : HttpMessageHandler
    {
        public (HttpStatusCode Status, string Body) SubPeriods { get; set; } = (HttpStatusCode.OK, "[]");
        public string YearDivision { get; set; } = "Terms";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.PathAndQuery;
            var (status, body) = url switch
            {
                _ when url == $"/students/periods/{YearId}/sub-periods" => SubPeriods,
                _ when url == $"/students/periods/{YearId}" => (HttpStatusCode.OK, YearJson(YearId, YearDivision, "Draft")),
                _ when url == "/students/periods" => (HttpStatusCode.OK, "[]"),
                _ => (HttpStatusCode.NotFound, "{}"),
            };
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class Auth : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("tenant_id", Guid.NewGuid().ToString()) }, "Test"))));
    }

    private Handler Registered()
    {
        var handler = new Handler();
        var auth = new Auth();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton<AuthenticationStateProvider>(auth);
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));
        var cv = new CodedValuesApiClient(http);
        Services.AddSingleton(cv);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, cv));
        return handler;
    }

    private static string YearJson(Guid id, string division, string status) =>
        $"{{\"id\":\"{id}\",\"name\":\"AY2026\",\"startDate\":\"2026-09-01\",\"endDate\":\"2027-08-31\",\"status\":\"{status}\",\"division\":\"{division}\",\"parentPeriodId\":null,\"nextPeriodId\":null,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}";

    private static string SubJson(string status) =>
        $"{{\"id\":\"{Guid.NewGuid()}\",\"name\":\"T1\",\"startDate\":\"2026-09-01\",\"endDate\":\"2026-12-31\",\"status\":\"{status}\",\"division\":\"Terms\",\"parentPeriodId\":\"{YearId}\",\"nextPeriodId\":null,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}";

    // FR-E2: editing a Terms year disables the Division selector (immutable).
    [TestMethod]
    public void Edit_AcademicYear_DivisionSelectIsDisabled()
    {
        Registered();
        var cut = Render<Edit>(p => p.Add(x => x.Id, YearId));

        cut.WaitForState(() => cut.Markup.Contains("Edit period"));
        var divisionSelect = cut.FindAll("fluent-select").FirstOrDefault(s => s.TextContent.Contains("Terms"));
        divisionSelect.Should().NotBeNull("the Division selector is present on edit");
        divisionSelect!.GetAttribute("disabled").Should().NotBeNull("Division is disabled on edit (FR-E2)");
    }

    // FR-E4: the edit form renders the sub-periods section WITH the Auto-split button.
    [TestMethod]
    public void Edit_AcademicYear_TermsDivision_ShowsAutoSplitButton()
    {
        var handler = Registered();
        handler.SubPeriods = (HttpStatusCode.OK, "[]");

        var cut = Render<Edit>(p => p.Add(x => x.Id, YearId));

        cut.WaitForState(() => cut.Markup.Contains("Sub-periods"));
        cut.WaitForAssertion(() => cut.FindAll("fluent-button")
            .Any(b => b.TextContent.Contains("Auto-split")).Should().BeTrue(
                "Auto-split is visible on edit (FR-E4)"));
    }

    // FR-E5: with zero sub-periods Auto-split is enabled.
    [TestMethod]
    public void Edit_NoSubPeriods_AutoSplitEnabled()
    {
        var handler = Registered();
        handler.SubPeriods = (HttpStatusCode.OK, "[]");

        var cut = Render<Edit>(p => p.Add(x => x.Id, YearId));
        cut.WaitForState(() => cut.Markup.Contains("Sub-periods"));

        var auto = cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Auto-split"));
        auto.GetAttribute("disabled").Should().BeNull("Auto-split is enabled when there are no sub-periods (FR-E5)");
    }

    // FR-E5: with a non-Draft (Active) sub-period Auto-split is disabled + tooltip.
    [TestMethod]
    public void Edit_ActiveSubPeriod_AutoSplitDisabled_WithTooltip()
    {
        var handler = Registered();
        handler.SubPeriods = (HttpStatusCode.OK, $"[{SubJson("Active")}]");

        var cut = Render<Edit>(p => p.Add(x => x.Id, YearId));
        cut.WaitForState(() => cut.Markup.Contains("Sub-periods"));

        var auto = cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Auto-split"));
        auto.GetAttribute("disabled").Should().NotBeNull(
            "Auto-split is disabled when a non-Draft sub-period exists (FR-E5)");
        auto.GetAttribute("title").Should().Contain("non-Draft",
            "the disabled Auto-split explains why");
    }
}
