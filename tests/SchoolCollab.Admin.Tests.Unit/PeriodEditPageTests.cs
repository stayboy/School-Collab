using System.Net;
using System.Text;
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
/// bUnit tests for <see cref="Edit"/> (the period edit page). Locks the
/// sub-period section placement + the guard that combines PeriodType with
/// the tenant's academic-year division. The placement is verified by the
/// section's position relative to the form's Period-type selector: the
/// section is rendered BEFORE the form, and the form's "Period type"
/// selector is the first FluentSelect&lt;string&gt; in the markup below it.
/// </summary>
[TestClass]
public class PeriodEditPageTests : BunitContext
{
    public PeriodEditPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly Dictionary<(string Method, string Url), (HttpStatusCode Status, string Body)> Responses = new();

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            Responses[(method.ToUpperInvariant(), url)] = (status, body);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.PathAndQuery;
            if (Responses.TryGetValue((request.Method.Method.ToUpperInvariant(), url), out var exact))
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
        Services.AddSingleton(new ConfigFlagsApiClient(http));
        return handler;
    }

    private static string AcademicYearJson(string? division = null) =>
        $"{{\"id\":\"{YearId}\",\"name\":\"2026\",\"startDate\":\"2026-01-01\",\"endDate\":\"2026-12-31\",\"status\":\"Active\",\"periodType\":\"AcademicYear\",\"parentPeriodId\":null,\"nextPeriodId\":null,\"division\":{(division is null ? "null" : $"\"{division}\"")},\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}";

    private static string TermJson() =>
        $"{{\"id\":\"{YearId}\",\"name\":\"Term 1\",\"startDate\":\"2026-01-01\",\"endDate\":\"2026-06-30\",\"status\":\"Active\",\"periodType\":\"Term\",\"parentPeriodId\":\"11111111-1111-1111-1111-111111111111\",\"nextPeriodId\":null,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}";

    /// <summary>
    /// G1: editing an AcademicYear under a "Terms" division renders the
    /// sub-period section AND the period form below it. Section is found by
    /// its "Sub-periods" header; form is found by its "Edit period" header.
    /// </summary>
    [TestMethod]
    public void Edit_AcademicYear_TermsDivision_ShowsSubPeriodsSection_AndForm()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.OK, AcademicYearJson("Terms"));
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK, "[]");

        var cut = Render<Edit>(p => p.Add(x => x.Id, YearId));

        cut.WaitForState(() => cut.Markup.Contains("Sub-periods"), TimeSpan.FromSeconds(5));
        cut.WaitForState(() => cut.Markup.Contains("Edit period"), TimeSpan.FromSeconds(5));

        // Section header precedes the form header in the markup.
        var sectionPos = cut.Markup.IndexOf("Sub-periods", StringComparison.Ordinal);
        var formPos = cut.Markup.IndexOf("Edit period", StringComparison.Ordinal);
        sectionPos.Should().BeGreaterThanOrEqualTo(0, "the sub-periods section is rendered");
        formPos.Should().BeGreaterThan(sectionPos,
            "the sub-periods section sits ABOVE the edit form on the page");
    }

    /// <summary>
    /// G2: editing a Term/Semester (not an AcademicYear) does NOT render the
    /// sub-period section, regardless of the division flag. Sub-periods are
    /// owned by years, never by other sub-periods.
    /// </summary>
    [TestMethod]
    public void Edit_TermPeriod_HidesSubPeriodsSection()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.OK, TermJson());
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");

        var cut = Render<Edit>(p => p.Add(x => x.Id, YearId));

        cut.WaitForState(() => cut.Markup.Contains("Edit period"), TimeSpan.FromSeconds(5));
        cut.Markup.Should().NotContain("Sub-periods",
            "sub-periods of a non-AcademicYear period are not meaningful");
    }

    /// <summary>
    /// G3: editing an AcademicYear under a "None" division does NOT render the
    /// sub-period section — sub-periods are server-rejected under "None", so
    /// rendering the section would surface a form that always fails. Mirrors
    /// server-side PeriodFrameworkMismatchException gate.
    /// </summary>
    [TestMethod]
    public void Edit_AcademicYear_NoneDivision_HidesSubPeriodsSection()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.OK, AcademicYearJson("None"));
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");

        var cut = Render<Edit>(p => p.Add(x => x.Id, YearId));

        cut.WaitForState(() => cut.Markup.Contains("Edit period"), TimeSpan.FromSeconds(5));
        cut.Markup.Should().NotContain("Sub-periods",
            "a None division forbids sub-periods at the server; the section must be hidden");
    }

    /// <summary>
    /// G4: editing an AcademicYear under a "Semesters" division DOES render
    /// the sub-period section (Semesters is a sub-period-allowing division).
    /// Symmetric with G1's Terms case.
    /// </summary>
    [TestMethod]
    public void Edit_AcademicYear_SemestersDivision_ShowsSubPeriodsSection()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.OK, AcademicYearJson("Semesters"));
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK, "[]");

        var cut = Render<Edit>(p => p.Add(x => x.Id, YearId));

        cut.WaitForState(() => cut.Markup.Contains("Sub-periods"), TimeSpan.FromSeconds(5));
        cut.Markup.Should().Contain("Sub-periods",
            "a Semesters division allows sub-periods; the section must be rendered");
    }

    /// <summary>
    /// G5: when the division flag is unreadable (404 → null), the guard
    /// treats it as "unknown" and still shows the section — SubPeriodsSection
    /// falls back to an explicit type selector so the user is never blocked
    /// by a failed flag read.
    /// </summary>
    [TestMethod]
    public void Edit_AcademicYear_UnknownDivision_StillShowsSubPeriodsSection()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.OK, AcademicYearJson());
        // No division on the year → null → "unknown".
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK, "[]");

        var cut = Render<Edit>(p => p.Add(x => x.Id, YearId));

        cut.WaitForState(() => cut.Markup.Contains("Sub-periods"), TimeSpan.FromSeconds(5));
        cut.Markup.Should().Contain("Sub-periods",
            "an unknown division falls back to the explicit selector; the section must still render");
    }

    /// <summary>
    /// F3: when the initial period load fails (non-404), the page renders a
    /// page-level error bar instead of silently showing an empty form. The
    /// distinctive "Couldn't load this period" wording distinguishes the page
    /// bar from the embedded PeriodForm's own raw ex.Message bar.
    /// </summary>
    [TestMethod]
    public void Edit_LoadFailure_ShowsPageLevelErrorBar()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.InternalServerError, "{\"message\":\"boom\"}");

        var cut = Render<Edit>(p => p.Add(x => x.Id, YearId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Couldn't load this period"));
    }

    /// <summary>
    /// F4: the SubPeriodsSection's always-visible inline Add button carries an
    /// accessible Title ("Add sub-period") matching its state-dependent text.
    /// </summary>
    [TestMethod]
    public void SubPeriodsSection_InlineAddButton_HasTitle()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.OK, AcademicYearJson("Terms"));
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK, "[]");

        var cut = Render<Edit>(p => p.Add(x => x.Id, YearId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("title=\"Add sub-period\""));
    }
}
