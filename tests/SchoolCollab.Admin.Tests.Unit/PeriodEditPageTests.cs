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
using Moq;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Pages.Periods;
using SchoolCollab.Students.Application.Services;
using System.Security.Claims;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for <see cref="PeriodUpsert"/> (the period edit page). Locks the
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
        public readonly List<(string Method, string Url)> Calls = new();

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            Responses[(method.ToUpperInvariant(), url)] = (status, body);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.PathAndQuery;
            Calls.Add((request.Method.Method.ToUpperInvariant(), url));
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
    private static readonly Guid DraftSub1Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid DraftSub2Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ActiveSubId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static string PeriodJson(Guid id, string name, string type, string status, Guid? parent, string start = "2026-01-01", string end = "2026-12-31") =>
        $"{{\"id\":\"{id}\",\"name\":\"{name}\",\"startDate\":\"{start}\",\"endDate\":\"{end}\",\"status\":\"{status}\",\"periodType\":\"{type}\",\"parentPeriodId\":{(parent is null ? "null" : $"\"{parent}\"")},\"nextPeriodId\":null,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}";

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

        var cut = Render<PeriodUpsert>(p => p.Add(x => x.Id, YearId));

        cut.WaitForState(() => cut.Markup.Contains("Sub-periods"), TimeSpan.FromSeconds(5));
        cut.WaitForState(() => cut.Markup.Contains("Edit period"), TimeSpan.FromSeconds(5));

        // r2 (period-edit-parity-deactivate.md FR-E3): the sub-periods section now
        // lives INSIDE the unified edit form (matching create), so its header follows
        // the form header in the markup.
        var sectionPos = cut.Markup.IndexOf("Sub-periods", StringComparison.Ordinal);
        var formPos = cut.Markup.IndexOf("Edit period", StringComparison.Ordinal);
        sectionPos.Should().BeGreaterThanOrEqualTo(0, "the sub-periods section is rendered");
        sectionPos.Should().BeGreaterThan(formPos,
            "the sub-periods section sits within the edit form (after the form header)");
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

        var cut = Render<PeriodUpsert>(p => p.Add(x => x.Id, YearId));

        cut.WaitForState(() => cut.Markup.Contains("Edit period"), TimeSpan.FromSeconds(5));
        cut.Markup.Should().NotContain("Sub-periods",
            "sub-periods of a non-AcademicYear period are not meaningful");
    }

    /// <summary>
    /// G3: editing an AcademicYear under a "None" division renders the sub-period
    /// section but DISABLED (period-create-edit-single-page.md FR-5/FR-7): the
    /// section is always visible for a top-level period, and a None division
    /// disables its controls with a hint. Sub-periods are still server-rejected
    /// under "None", so the create submit sends no definitions.
    /// </summary>
    [TestMethod]
    public void Edit_AcademicYear_NoneDivision_ShowsDisabledSubPeriodsSection()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.OK, AcademicYearJson("None"));
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK, "[]");

        var cut = Render<PeriodUpsert>(p => p.Add(x => x.Id, YearId));

        cut.WaitForState(() => cut.Markup.Contains("Sub-periods"), TimeSpan.FromSeconds(5));
        cut.Markup.Should().Contain("Sub-periods",
            "the section renders for any top-level period regardless of division (FR-5)");
        cut.Markup.Should().Contain("Switch division to Terms or Semesters to add sub-periods",
            "a None division disables the section with a hint (FR-7)");
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

        var cut = Render<PeriodUpsert>(p => p.Add(x => x.Id, YearId));

        cut.WaitForState(() => cut.Markup.Contains("Sub-periods"), TimeSpan.FromSeconds(5));
        cut.Markup.Should().Contain("Sub-periods",
            "a Semesters division allows sub-periods; the section must be rendered");
    }

    /// <summary>
    /// G5: when the division is unreadable (null), the section still renders for
    /// a top-level period (FR-5); the editor disables its controls because the
    /// division is not Terms/Semesters.
    /// </summary>
    [TestMethod]
    public void Edit_AcademicYear_UnknownDivision_StillShowsSubPeriodsSection()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.OK, AcademicYearJson());
        // No division on the year → null → "unknown".
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK, "[]");

        var cut = Render<PeriodUpsert>(p => p.Add(x => x.Id, YearId));

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

        var cut = Render<PeriodUpsert>(p => p.Add(x => x.Id, YearId));

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

        var cut = Render<PeriodUpsert>(p => p.Add(x => x.Id, YearId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("title=\"Add sub-period\""));
    }

    /// <summary>
    /// In-cell editing (round subperiods-incell-grid): opening the sub-period row's
    /// kebab (⋮) and clicking Edit (repo-standard RowActionsMenu) switches that row
    /// to in-cell inputs (Save/Cancel appear), and Save persists via
    /// PUT /students/periods/{id}.
    /// </summary>
    [TestMethod]
    public void SubPeriodsSection_InCellEdit_SaveCallsUpdate()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.OK, AcademicYearJson("Terms"));
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK,
            $"[{PeriodJson(DraftSub1Id, "Term 1", "Term", "Draft", YearId)}]");
        handler.Map("PUT", $"/students/periods/{DraftSub1Id}", HttpStatusCode.NoContent, "");

        var cut = Render<PeriodUpsert>(p => p.Add(x => x.Id, YearId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Sub-periods"));

        // Open the sub-period row's kebab (⋮, RowActionsMenu) and click Edit.
        cut.Find("fluent-button[title='Sub-period actions']").Click();
        var editItem = cut.FindAll("fluent-menu-item").First(i => i.TextContent.Contains("Edit"));
        editItem.Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("title=\"Save sub-period\""));
        cut.Markup.Should().Contain("title=\"Cancel editing\"", "in-cell editing shows a Cancel affordance");

        cut.FindAll("fluent-button").First(b => b.GetAttribute("title") == "Save sub-period").Click();

        cut.WaitForAssertion(() => handler.Calls.Should().Contain(("PUT", $"/students/periods/{DraftSub1Id}")));
    }
    /// <summary>
    /// Repo-standard kebab: a Draft sub-period row renders the shared RowActionsMenu
    /// with Edit + Delete (destructive). Non-Draft rows drop the Delete item and
    /// render a single-action Edit button instead.
    /// </summary>
    [TestMethod]
    public void SubPeriodsSection_DraftRow_RendersKebabWithEditAndDelete()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.OK, AcademicYearJson("Terms"));
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK,
            $"[{PeriodJson(DraftSub1Id, "Term 1", "Term", "Draft", YearId)}]");

        var cut = Render<PeriodUpsert>(p => p.Add(x => x.Id, YearId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Sub-periods"));

        cut.Find("fluent-button[title='Sub-period actions']").Click();
        var labels = cut.FindAll("fluent-menu-item").Select(i => i.TextContent.Trim()).ToArray();
        labels.Should().Contain(new[] { "Edit", "Delete" },
            "a Draft sub-period row offers Edit and a destructive Delete in the kebab");
    }

    /// <summary>
    /// Repo-standard kebab consistency: when ANY sub-period row is Draft (2
    /// actions → qualifies for the kebab), the kebab is forced on EVERY row —
    /// including non-Draft rows that would otherwise render a lone labeled Edit
    /// button. This keeps the actions column visually consistent.
    /// </summary>
    [TestMethod]
    public void SubPeriodsSection_AnyDraftRow_ForcesKebabOnEveryRow()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.OK, AcademicYearJson("Terms"));
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK,
            $"[{PeriodJson(DraftSub1Id, "Term 1", "Term", "Draft", YearId)},{PeriodJson(ActiveSubId, "Term 2", "Term", "Active", YearId)}]");

        var cut = Render<PeriodUpsert>(p => p.Add(x => x.Id, YearId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Sub-periods"));

        // Both rows render the kebab trigger (⋮) — the Draft row qualifies, so the
        // non-Draft row is forced to the kebab too instead of a lone Edit button.
        cut.FindAll("fluent-button[title='Sub-period actions']").Should().HaveCount(2,
            "every sub-period row renders the kebab trigger when any row qualifies");
    }



    // ── Draft-period delete danger zone (period-draft-delete.md FR-D10) ──

    private static string DraftYearJson(string? division = null) =>
        $"{{\"id\":\"{YearId}\",\"name\":\"2026\",\"startDate\":\"2026-01-01\",\"endDate\":\"2026-12-31\",\"status\":\"Draft\",\"periodType\":\"AcademicYear\",\"parentPeriodId\":null,\"nextPeriodId\":null,\"division\":{(division is null ? "null" : $"\"{division}\"")},\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}";

    private static Mock<IDialogService> ConfirmationDialog(StringBuilder? capturedMessage = null)
    {
        var dialogRef = new Mock<IDialogReference>();
        dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(DialogResult.Ok<object>(null!)));
        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string>((msg, _, __, ___) => capturedMessage?.Append(msg))
            .ReturnsAsync(dialogRef.Object);
        return dialogMock;
    }

    /// <summary>FR-D10: a Draft period renders the danger-zone Delete affordance.</summary>
    [TestMethod]
    public void Edit_DraftPeriod_RendersDangerZoneDelete()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.OK, DraftYearJson("None"));
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");

        var cut = Render<PeriodUpsert>(p => p.Add(x => x.Id, YearId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Danger zone"));
        cut.Markup.Should().Contain("title=\"Delete period\"",
            "the danger zone offers a Delete affordance (FR-D10)");
    }

    /// <summary>FR-D10: a non-Draft period renders no danger zone.</summary>
    [TestMethod]
    public void Edit_NonDraftPeriod_HasNoDangerZone()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.OK, AcademicYearJson("None"));
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");

        var cut = Render<PeriodUpsert>(p => p.Add(x => x.Id, YearId));

        cut.WaitForState(() => cut.Markup.Contains("Edit period"), TimeSpan.FromSeconds(5));
        cut.Markup.Should().NotContain("Danger zone",
            "a non-Draft period has no delete danger zone (FR-D10)");
    }

    /// <summary>FR-D10: confirming the delete fires the DELETE call and navigates to the list.</summary>
    [TestMethod]
    public void Edit_Delete_Confirm_NavigatesToPeriods()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.OK, DraftYearJson("None"));
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK, "[]");
        handler.Map("DELETE", $"/students/periods/{YearId}", HttpStatusCode.NoContent, "");
        Services.AddSingleton(ConfirmationDialog().Object);

        var cut = Render<PeriodUpsert>(p => p.Add(x => x.Id, YearId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("title=\"Delete period\""));

        cut.FindAll("fluent-button").First(b => b.GetAttribute("title") == "Delete period").Click();

        cut.WaitForAssertion(() => Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/students/periods"));
    }

    /// <summary>FR-D10/D9: the edit-page year confirmation shares the grid's wording
    /// (names the period and the Draft sub count).</summary>
    [TestMethod]
    public void Edit_Delete_YearConfirmation_Wording_MatchesGrid()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.OK, DraftYearJson("Terms"));
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK,
            $"[{PeriodJson(DraftSub1Id, "Term 1", "Term", "Draft", YearId)},{PeriodJson(DraftSub2Id, "Term 2", "Term", "Draft", YearId)}]");
        var captured = new StringBuilder();
        Services.AddSingleton(ConfirmationDialog(captured).Object);

        var cut = Render<PeriodUpsert>(p => p.Add(x => x.Id, YearId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("title=\"Delete period\""));

        cut.FindAll("fluent-button").First(b => b.GetAttribute("title") == "Delete period").Click();

        cut.WaitForAssertion(() => captured.ToString().Should().Contain("2026"));
        captured.ToString().Should().Contain("2 draft sub-periods",
            "the edit-page year confirmation matches the grid wording (FR-D9/D10)");
    }
    // ── Auto-split count (period-create-edit-single-page.md FR-8) ──

    /// <summary>FR-8: the auto-split count is prefilled from the division
    /// convention (Terms = 3) until the user overrides it.</summary>
    [TestMethod]
    public void SubPeriodsSection_SplitCount_TermsPrefillsThree()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.OK, AcademicYearJson("Terms"));
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK, "[]");

        var cut = Render<PeriodUpsert>(p => p.Add(x => x.Id, YearId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Auto-split into 3"));
    }

    /// <summary>FR-8: the auto-split count is prefilled from the division
    /// convention (Semesters = 2) until the user overrides it.</summary>
    [TestMethod]
    public void SubPeriodsSection_SplitCount_SemestersPrefillsTwo()
    {
        var handler = Register();
        handler.Map("GET", $"/students/periods/{YearId}", HttpStatusCode.OK, AcademicYearJson("Semesters"));
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK, "[]");

        var cut = Render<PeriodUpsert>(p => p.Add(x => x.Id, YearId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Auto-split into 2"));
    }

}

