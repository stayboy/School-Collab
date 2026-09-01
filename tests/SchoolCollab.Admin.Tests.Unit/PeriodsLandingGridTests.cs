using System.Net;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
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
/// bUnit tests for the Periods landing-grid beautify round
/// (documents/specs/periods-landing-grid-beautify.md). Covers the grid
/// (FR-1/2/3/5) and the new <see cref="SubPeriodsListDialog"/> (FR-4), per
/// NFR-2: name-as-edit-link, per-year sub-period counts (0 non-interactive,
/// sub-period rows show —), count link opens the dialog, and the dialog's
/// list / empty / error / activate / complete behavior.
/// </summary>
[TestClass]
public class PeriodsLandingGridTests : BunitContext
{
    public PeriodsLandingGridTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    // ── Test doubles (mirror SubPeriodsPageTests / ContactsEditorTests) ─────

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Dictionary<(string Method, string Url), (HttpStatusCode Status, string Body)> _responses = new();

        /// <summary>Count of mutation (POST) requests actually served — lets a
        /// test assert that a cancelled confirmation never fired the API call.</summary>
        public int PostCount { get; private set; }

        /// <summary>Count of DELETE requests served (period delete).</summary>
        public int DeleteCount { get; private set; }

        /// <summary>Count of GET requests served (list reloads).</summary>
        public int GetCount { get; private set; }

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            _responses[(method.ToUpperInvariant(), url)] = (status, body);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post) PostCount++;
            if (request.Method == HttpMethod.Delete) DeleteCount++;
            if (request.Method == HttpMethod.Get) GetCount++;
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
    private static readonly Guid EmptyYearId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid Term1Id = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Term2Id = Guid.Parse("44444444-4444-4444-4444-444444444444");

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

    private static string PeriodJson(Guid id, string name, string type, string status, Guid? parent, string start = "2026-01-01", string end = "2026-12-31") =>
        $"{{\"id\":\"{id}\",\"name\":\"{name}\",\"startDate\":\"{start}\",\"endDate\":\"{end}\",\"status\":\"{status}\",\"periodType\":\"{type}\",\"parentPeriodId\":{(parent is null ? "null" : $"\"{parent}\"")},\"nextPeriodId\":null,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}";

    /// <summary>Variants that set the DTO's <c>division</c> field (a real PeriodDto
    /// property) — used by the FR-G6 activation-guard row-action tests, which key
    /// off <c>division</c> to decide whether a Draft year's Activate is guarded.</summary>
    private static string PeriodJsonWithDivision(Guid id, string name, string division, string status, Guid? parent, string start = "2026-01-01", string end = "2026-12-31") =>
        $"{{\"id\":\"{id}\",\"name\":\"{name}\",\"startDate\":\"{start}\",\"endDate\":\"{end}\",\"status\":\"{status}\",\"division\":\"{division}\",\"parentPeriodId\":{(parent is null ? "null" : $"\"{parent}\"")},\"nextPeriodId\":null,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}";

    private static string PeriodsJson() =>
        $"[{PeriodJson(YearId, "2026", "AcademicYear", "Active", null)}," +
        $"{PeriodJson(EmptyYearId, "2025", "AcademicYear", "Completed", null)}," +
        $"{PeriodJson(Term1Id, "Term 1", "Term", "Draft", YearId, "2026-01-01", "2026-06-30")}," +
        $"{PeriodJson(Term2Id, "Term 2", "Semester", "Active", YearId, "2026-07-01", "2026-12-31")}]";

    // ── FR-2 / FR-3 / FR-5 grid assertions ──────────────────────────────────

    [TestMethod]
    public void Periods_Name_RendersAsEditLink()
    {
        var handler = RegisterClient();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, PeriodsJson());

        var cut = Render<Periods>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain($"href=\"/students/periods/{YearId}/edit\""));
        cut.Markup.Should().Contain("Edit period",
            "the name anchor carries an accessible title for the edit navigation");
    }

    [TestMethod]
    public void Periods_CountColumn_ShowsPerYearCounts()
    {
        var handler = RegisterClient();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, PeriodsJson());

        var cut = Render<Periods>();

        // Year 2026 has 2 sub-periods (Term 1 Draft + Term 2 Semester).
        cut.WaitForAssertion(() => cut.FindAll("fluent-anchor").Any(a => a.GetAttribute("title") == "View sub-periods"));
        var countLink = cut.FindAll("fluent-anchor").First(a => a.GetAttribute("title") == "View sub-periods");
        countLink.TextContent.Trim().Should().Be("2", "2026 has two sub-periods");
    }

    [TestMethod]
    public void Periods_ZeroCount_RendersMutedNonInteractive()
    {
        var handler = RegisterClient();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, PeriodsJson());

        var cut = Render<Periods>();

        // Only ONE View-sub-periods link exists (the 2-count for 2026); the
        // empty year (2025) renders a non-interactive muted "0", not a link.
        cut.WaitForAssertion(() => cut.FindAll("fluent-anchor").Any(a => a.GetAttribute("title") == "View sub-periods"));
        cut.FindAll("fluent-anchor").Count(a => a.GetAttribute("title") == "View sub-periods").Should().Be(1);
        cut.FindAll(".muted").Any(m => m.TextContent.Trim() == "0").Should().BeTrue(
            "a year with no sub-periods renders a muted, non-interactive 0");
    }

    [TestMethod]
    public void Periods_SubPeriodRows_RenderEmDash()
    {
        var handler = RegisterClient();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, PeriodsJson());

        var cut = Render<Periods>();

        // Term / Semester rows are not AcademicYear → they render an em dash
        // in the count column, non-interactive.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Term 1"));
        cut.Markup.Should().Contain("—", "sub-period rows show an em dash in the count column");
    }

    [TestMethod]
    public void Periods_CountLink_OpensSubPeriodsDialog()
    {
        var handler = RegisterClient();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, PeriodsJson());
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK,
            $"[{PeriodJson(Term1Id, "Term 1", "Term", "Draft", YearId, "2026-01-01", "2026-06-30")}]");

        // Mock IDialogService so the read-only dialog open is assertable (the
        // grid page opens it via ShowReadonlyDialogAsync → ShowDialogAsync).
        var dialogRef = new Mock<IDialogReference>();
        dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(DialogResult.Cancel()));
        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowDialogAsync<SubPeriodsListDialog, DialogParameters>(
                It.IsAny<DialogParameters>(), It.IsAny<DialogParameters>()))
            .ReturnsAsync(dialogRef.Object);
        Services.AddSingleton(dialogMock.Object);

        var cut = Render<Periods>();
        cut.WaitForAssertion(() => cut.FindAll("fluent-anchor").Any(a => a.GetAttribute("title") == "View sub-periods"));

        cut.FindAll("fluent-anchor").First(a => a.GetAttribute("title") == "View sub-periods").Click();

        dialogMock.Verify(d => d.ShowDialogAsync<SubPeriodsListDialog, DialogParameters>(
            It.IsAny<DialogParameters>(), It.IsAny<DialogParameters>()), Times.Once);
    }

    [TestMethod]
    public void Periods_RowActions_CollapseToSingle()
    {
        var handler = RegisterClient();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, PeriodsJson());

        var cut = Render<Periods>();

        // r2 (period-edit-parity-deactivate.md): every action-bearing row now carries
        // 2 actions (Draft = Activate + Delete; Active = Complete + Deactivate), so
        // they ALL render as a kebab + FluentMenu. Collect the union of action labels
        // from the menu items and assert it contains Activate + Complete — never Edit
        // or Sub-periods.
        cut.WaitForAssertion(() => cut.FindAll("fluent-button[title='Period actions']").Any().Should().BeTrue());

        var labels = cut.FindAll("fluent-button")
            .Select(b => b.GetAttribute("title"))
            .Where(t => !string.IsNullOrEmpty(t))
            .Select(t => t!)
            .ToList();

        // Open any kebab menus (rows with 2+ actions) and collect their items.
        foreach (var kebab in cut.FindAll("fluent-button[title='Period actions']"))
            kebab.Click();
        labels.AddRange(cut.FindAll("fluent-menu-item")
            .Select(m => m.GetAttribute("label") ?? m.TextContent.Trim())
            .Where(l => l.Length > 0));

        labels = labels.Distinct().ToList();
        labels.Should().Contain(new[] { "Activate", "Complete" });
        labels.Should().NotContain("Edit", "the Edit row action is removed (FR-2)");
        labels.Should().NotContain("Sub-periods", "the Sub-periods navigate action is removed (FR-5)");
    }

    [TestMethod]
    public void Periods_RowActions_Guard_DisablesActivateForTermsYearNoDraftSub()
    {
        // A Terms year (parent null) with Draft status but ZERO Draft subs → the
        // Activate action is present but disabled with an explanatory label (FR-G6).
        // Draft rows now carry Activate + Delete, so they render as a kebab menu.
        var yearId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var handler = RegisterClient();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK,
            "[" + PeriodJsonWithDivision(yearId, "2027", "Terms", "Draft", null, "2027-01-01", "2027-12-31") + "]");

        var cut = Render<Periods>();

        cut.WaitForAssertion(() => cut.FindAll("fluent-button[title='Period actions']").Any().Should().BeTrue());
        cut.FindAll("fluent-button[title='Period actions']").First().Click();
        cut.WaitForAssertion(() => cut.FindAll("fluent-menu-item")
            .Any(m => m.TextContent.Contains("Add a draft Term/Semester to activate")).Should().BeTrue(
                "a guarded Terms year shows a disabled Activate with an explanatory label"));
        var item = cut.FindAll("fluent-menu-item")
            .First(m => m.TextContent.Contains("Add a draft Term/Semester to activate"));
        item.GetAttribute("disabled").Should().NotBeNull("the guarded Activate is disabled");
    }

    [TestMethod]
    public void Periods_RowActions_TermsYear_WithDraftSub_ActivateEnabled()
    {
        // A Terms year with at least one Draft sub → Activate enabled (regression).
        // Draft rows now carry Activate + Delete, so they render as a kebab menu.
        var yearId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var draftSub = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var handler = RegisterClient();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK,
            "[" + PeriodJsonWithDivision(yearId, "2027", "Terms", "Draft", null, "2027-01-01", "2027-12-31") + "," +
            PeriodJsonWithDivision(draftSub, "Term 1", "Terms", "Draft", yearId, "2027-01-01", "2027-06-30") + "]");

        var cut = Render<Periods>();

        cut.WaitForAssertion(() => cut.FindAll("fluent-button[title='Period actions']").Any().Should().BeTrue());
        cut.FindAll("fluent-button[title='Period actions']").First().Click();
        cut.WaitForAssertion(() => cut.FindAll("fluent-menu-item")
            .Any(m => m.TextContent.Trim() == "Activate").Should().BeTrue(
                "a Terms year with a Draft sub offers an enabled Activate"));
        var item = cut.FindAll("fluent-menu-item").First(m => m.TextContent.Trim() == "Activate");
        item.GetAttribute("disabled").Should().BeNull("Activate is enabled when a Draft sub exists");
    }

    [TestMethod]
    public void Periods_RowActions_NoneDivisionYear_ActivateEnabled()
    {
        // A None-division year needs no sub to activate → Activate enabled (FR-G3).
        // Draft rows now carry Activate + Delete, so they render as a kebab menu.
        var yearId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var handler = RegisterClient();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK,
            "[" + PeriodJsonWithDivision(yearId, "2027", "None", "Draft", null, "2027-01-01", "2027-12-31") + "]");

        var cut = Render<Periods>();

        cut.WaitForAssertion(() => cut.FindAll("fluent-button[title='Period actions']").Any().Should().BeTrue());
        cut.FindAll("fluent-button[title='Period actions']").First().Click();
        cut.WaitForAssertion(() => cut.FindAll("fluent-menu-item")
            .Any(m => m.TextContent.Trim() == "Activate").Should().BeTrue(
                "a None-division Draft year offers an enabled Activate"));
    }

    // ── FR-4 dialog assertions ──────────────────────────────────────────────

    private static DialogParameters SubPeriodsContent(Guid yearId, string yearName = "2026", Func<Task>? onChanged = null)
    {
        var c = new DialogParameters
        {
            [SubPeriodsListDialog.YearIdKey] = yearId,
            [SubPeriodsListDialog.YearNameKey] = yearName,
        };
        if (onChanged is not null) c[SubPeriodsListDialog.OnChangedKey] = onChanged;
        return c;
    }

    [TestMethod]
    public void Dialog_List_RendersRows()
    {
        var handler = RegisterClient();
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK,
            $"[{PeriodJson(Term1Id, "Term 1", "Term", "Draft", YearId, "2026-01-01", "2026-06-30")}," +
            $"{PeriodJson(Term2Id, "Semester 1", "Semester", "Active", YearId, "2026-07-01", "2026-12-31")}]");

        var cut = Render<SubPeriodsListDialog>(p => p.Add(x => x.Content, SubPeriodsContent(YearId)));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Term 1"));
        cut.Markup.Should().Contain("Semester 1", "both sub-period rows render");
        cut.Markup.Should().Contain("Close", "the footer renders a single Close button");
        cut.Markup.Should().Contain("Activate", "the Draft row offers Activate");
        cut.Markup.Should().Contain("Complete", "the Active row offers Complete");
    }

    [TestMethod]
    public void Dialog_EmptyState_ShowsNewSubPeriodLink()
    {
        var handler = RegisterClient();
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK, "[]");

        var cut = Render<SubPeriodsListDialog>(p => p.Add(x => x.Content, SubPeriodsContent(YearId)));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No sub-periods for this academic year yet."));
        cut.Markup.Should().Contain("+ New sub-period");
    }

    [TestMethod]
    public void Dialog_LoadError_ShowsErrorBar()
    {
        var handler = RegisterClient();
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.InternalServerError, "{\"message\":\"boom\"}");

        var cut = Render<SubPeriodsListDialog>(p => p.Add(x => x.Content, SubPeriodsContent(YearId)));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Couldn't load sub-periods"));
        cut.Markup.Should().Contain("boom", "the underlying failure message is surfaced");
    }

    [TestMethod]
    public void Dialog_Activate_SyncsBackToGrid()
    {
        var handler = RegisterClient();
        // Initial load: one Draft row.
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK,
            $"[{PeriodJson(Term1Id, "Term 1", "Term", "Draft", YearId, "2026-01-01", "2026-06-30")}]");
        // Activate POST succeeds; the reload returns the row as Active.
        handler.Map("POST", $"/students/periods/{Term1Id}/activate", HttpStatusCode.OK, "");
        var gridRefresh = 0;

        var cut = Render<SubPeriodsListDialog>(p => p.Add(x => x.Content,
            SubPeriodsContent(YearId, onChanged: () => { gridRefresh++; return Task.CompletedTask; })));

        cut.WaitForAssertion(() => cut.FindAll("fluent-button").Any(b => b.TextContent.Contains("Activate")));
        cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Activate")).Click();

        // The parent grid reload callback is invoked after a dialog mutation so
        // the landing grid stays in sync (the core sync-back contract).
        cut.WaitForAssertion(() => gridRefresh.Should().Be(1));
    }

    [TestMethod]
    public void Dialog_Complete_SyncsBackToGrid()
    {
        var handler = RegisterClient();
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK,
            $"[{PeriodJson(Term2Id, "Semester 1", "Semester", "Active", YearId, "2026-07-01", "2026-12-31")}]");
        handler.Map("POST", $"/students/periods/{Term2Id}/complete", HttpStatusCode.OK, "");
        var gridRefresh = 0;

        // Complete is gated by a confirmation prompt (FR-4); mock the message box
        // so the primary (Confirm) action resolves as accepted.
        var dialogRef = new Mock<IDialogReference>();
        dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(DialogResult.Ok<object>(null!)));
        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(dialogRef.Object);
        Services.AddSingleton(dialogMock.Object);

        var cut = Render<SubPeriodsListDialog>(p => p.Add(x => x.Content,
            SubPeriodsContent(YearId, onChanged: () => { gridRefresh++; return Task.CompletedTask; })));

        cut.WaitForAssertion(() => cut.FindAll("fluent-button").Any(b => b.TextContent.Contains("Complete")));
        cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Complete")).Click();

        // Confirmation was accepted, so the parent grid reload callback fires after
        // the dialog mutation (the core sync-back contract).
        cut.WaitForAssertion(() => gridRefresh.Should().Be(1));
        dialogMock.Verify(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Once, "Complete must prompt for confirmation before acting");
    }

    [TestMethod]
    public void Dialog_Complete_Cancelled_DoesNotCallApiOrReload()
    {
        var handler = RegisterClient();
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK,
            $"[{PeriodJson(Term2Id, "Semester 1", "Semester", "Active", YearId, "2026-07-01", "2026-12-31")}]");
        var gridRefresh = 0;

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(DialogResult.Cancel()));
        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(dialogRef.Object);
        Services.AddSingleton(dialogMock.Object);

        var cut = Render<SubPeriodsListDialog>(p => p.Add(x => x.Content,
            SubPeriodsContent(YearId, onChanged: () => { gridRefresh++; return Task.CompletedTask; })));

        cut.WaitForAssertion(() => cut.FindAll("fluent-button").Any(b => b.TextContent.Contains("Complete")));
        cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Complete")).Click();

        // Cancelling the prompt must short-circuit: no API call, no reload,
        // no parent-grid refresh.
        handler.PostCount.Should().Be(0, "cancellation must not fire the complete API call");
        gridRefresh.Should().Be(0, "cancellation must not reload the parent grid");
    }

    /// <summary>
    /// F4: the dialog footer Close button carries an accessible Title matching
    /// its visible text.
    /// </summary>
    [TestMethod]
    public void SubPeriodsListDialog_FooterCloseButton_HasTitle()
    {
        var handler = RegisterClient();
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK,
            $"[{PeriodJson(Term1Id, "Term 1", "Term", "Draft", YearId, "2026-01-01", "2026-06-30")}]");

        var cut = Render<SubPeriodsListDialog>(p => p.Add(x => x.Content, SubPeriodsContent(YearId)));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("title=\"Close\""));
    }

    /// <summary>
    /// F4: the empty-state "+ New sub-period" anchor carries an accessible
    /// Title matching its intent (visible label minus the + glyph).
    /// </summary>
    [TestMethod]
    public void SubPeriodsListDialog_EmptyStateAnchor_HasTitle()
    {
        var handler = RegisterClient();
        handler.Map("GET", $"/students/periods/{YearId}/sub-periods", HttpStatusCode.OK, "[]");

        var cut = Render<SubPeriodsListDialog>(p => p.Add(x => x.Content, SubPeriodsContent(YearId)));

        cut.WaitForAssertion(() => cut.FindAll("fluent-anchor")
            .Any(a => a.GetAttribute("title") == "New sub-period").Should().BeTrue());
    }

    // ── Draft-period delete (period-draft-delete.md FR-D9 / NFR-D3 / AC-D8/D9) ──

    private static readonly Guid DraftYearId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DraftSub1Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid DraftSub2Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

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

    /// <summary>AC-D8 + NFR-D3: a Draft row offers an enabled Delete action in the
    /// kebab (a real tab stop, not a disabled tooltip).</summary>
    [TestMethod]
    public void Periods_RowActions_DraftRow_OffersDelete()
    {
        var handler = RegisterClient();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK,
            "[" + PeriodJsonWithDivision(DraftYearId, "2026", "None", "Draft", null) + "]");

        var cut = Render<Periods>();

        cut.WaitForAssertion(() => cut.FindAll("fluent-button[title='Period actions']").Any().Should().BeTrue());
        cut.FindAll("fluent-button[title='Period actions']").First().Click();
        cut.WaitForAssertion(() => cut.FindAll("fluent-menu-item")
            .Any(m => m.TextContent.Contains("Delete")).Should().BeTrue(
                "a Draft row offers a Delete action (AC-D8)"));
        var item = cut.FindAll("fluent-menu-item").First(m => m.TextContent.Contains("Delete"));
        item.GetAttribute("disabled").Should().BeNull(
            "the Delete action is enabled and keyboard-reachable (NFR-D3)");
    }

    /// <summary>AC-D8: non-Draft rows expose no Delete action.</summary>
    [TestMethod]
    public void Periods_RowActions_NonDraft_NoDelete()
    {
        var handler = RegisterClient();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, PeriodsJson());

        var cut = Render<Periods>();

        // r2: action-bearing rows render as kebab menus (2 actions each), never a
        // single labeled action button.
        cut.WaitForAssertion(() => cut.FindAll("fluent-button[title='Period actions']").Any().Should().BeTrue());
        cut.FindAll("fluent-button").Any(b => b.GetAttribute("title") == "Delete").Should().BeFalse(
            "Active/Completed rows offer no labeled Delete (AC-D8)");
        cut.FindAll("fluent-menu-item").Any(m => m.TextContent.Contains("Delete")).Should().BeFalse(
            "Active/Completed rows offer no Delete menu item (AC-D8)");
    }

    /// <summary>FR-D9: confirming the delete fires the DELETE call and reloads the list.</summary>
    [TestMethod]
    public void Periods_Delete_Confirm_CallsApiAndReloads()
    {
        var handler = RegisterClient();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK,
            "[" + PeriodJsonWithDivision(DraftYearId, "2026", "None", "Draft", null) + "]");
        handler.Map("DELETE", $"/students/periods/{DraftYearId}", HttpStatusCode.NoContent, "");
        Services.AddSingleton(ConfirmationDialog().Object);

        var cut = Render<Periods>();
        cut.WaitForAssertion(() => cut.FindAll("fluent-button[title='Period actions']").Any().Should().BeTrue());
        cut.FindAll("fluent-button[title='Period actions']").First().Click();
        cut.WaitForAssertion(() => cut.FindAll("fluent-menu-item")
            .Any(m => m.TextContent.Contains("Delete")).Should().BeTrue());
        var beforeGets = handler.GetCount;

        cut.FindAll("fluent-menu-item").First(m => m.TextContent.Contains("Delete")).Click();

        cut.WaitForAssertion(() => handler.DeleteCount.Should().Be(1, "the confirmed delete fires the DELETE call"));
        handler.GetCount.Should().BeGreaterThan(beforeGets, "the list is re-fetched after a successful delete (FR-D9)");
    }

    /// <summary>FR-D9: cancelling the confirmation never fires the API call.</summary>
    [TestMethod]
    public void Periods_Delete_Cancelled_DoesNotCallApi()
    {
        var handler = RegisterClient();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK,
            "[" + PeriodJsonWithDivision(DraftYearId, "2026", "None", "Draft", null) + "]");
        var dialogRef = new Mock<IDialogReference>();
        dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(DialogResult.Cancel()));
        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(dialogRef.Object);
        Services.AddSingleton(dialogMock.Object);

        var cut = Render<Periods>();
        cut.WaitForAssertion(() => cut.FindAll("fluent-button[title='Period actions']").Any().Should().BeTrue());
        cut.FindAll("fluent-button[title='Period actions']").First().Click();
        cut.WaitForAssertion(() => cut.FindAll("fluent-menu-item")
            .Any(m => m.TextContent.Contains("Delete")).Should().BeTrue());

        cut.FindAll("fluent-menu-item").First(m => m.TextContent.Contains("Delete")).Click();

        handler.DeleteCount.Should().Be(0, "cancelling the confirmation must not fire the DELETE call");
    }

    /// <summary>FR-D9: the year confirmation names the period and the memoized Draft sub count.</summary>
    [TestMethod]
    public void Periods_Delete_YearConfirmation_NamesPeriodAndDraftSubCount()
    {
        var handler = RegisterClient();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK,
            "[" + PeriodJsonWithDivision(DraftYearId, "2026", "Terms", "Draft", null) + "," +
            PeriodJsonWithDivision(DraftSub1Id, "Term 1", "Terms", "Draft", DraftYearId) + "," +
            PeriodJsonWithDivision(DraftSub2Id, "Term 2", "Terms", "Draft", DraftYearId) + "]");
        var captured = new StringBuilder();
        Services.AddSingleton(ConfirmationDialog(captured).Object);

        var cut = Render<Periods>();
        cut.WaitForAssertion(() => cut.FindAll("fluent-button[title='Period actions']").Any().Should().BeTrue());
        cut.FindAll("fluent-button[title='Period actions']").First().Click();
        cut.WaitForAssertion(() => cut.FindAll("fluent-menu-item")
            .Any(m => m.TextContent.Contains("Delete")).Should().BeTrue());

        cut.FindAll("fluent-menu-item").First(m => m.TextContent.Contains("Delete")).Click();

        cut.WaitForAssertion(() => captured.ToString().Should().Contain("2026"));
        captured.ToString().Should().Contain("2 draft sub-periods",
            "the year confirmation names the memoized Draft sub count (FR-D9)");
    }

    // ── FR-X5/X9 (period-edit-parity-deactivate.md AC-E7) ────────────────────

    private static readonly Guid DeactivatedYearId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    /// <summary>AC-E7 + FR-X9: a Deactivated row exposes a single Archive action.
    /// Single-action rows render as a labeled button (not a kebab menu).</summary>
    [TestMethod]
    public void Periods_RowActions_DeactivatedRow_OffersArchive()
    {
        var handler = RegisterClient();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK,
            "[" + PeriodJson(DeactivatedYearId, "2025-archived", "AcademicYear", "Deactivated", null) + "]");

        var cut = Render<Periods>();

        cut.WaitForAssertion(() => cut.FindAll("fluent-button")
            .Any(b => b.TextContent.Contains("Archive")).Should().BeTrue(
                "a Deactivated row offers an Archive action (FR-X5/X9)"));
        cut.FindAll("fluent-button[title='Period actions']").Should().BeEmpty(
            "a Deactivated row has a single action so it never renders as a kebab");
    }

    /// <summary>AC-E7: a Deactivated row offers NO Delete, NO Activate, NO Complete,
    /// and NO Deactivate action — only the cleanup Archive path (FR-X5/X9).</summary>
    [TestMethod]
    public void Periods_RowActions_DeactivatedRow_NoLifecycleActions()
    {
        var handler = RegisterClient();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK,
            "[" + PeriodJson(DeactivatedYearId, "2025-archived", "AcademicYear", "Deactivated", null) + "]");

        var cut = Render<Periods>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Deactivated"));
        cut.FindAll("fluent-button").Any(b => b.TextContent.Contains("Delete")).Should().BeFalse(
            "AC-E7: Delete is not exposed on Deactivated rows");
        cut.FindAll("fluent-button").Any(b => b.TextContent.Contains("Activate")).Should().BeFalse(
            "AC-E7: Activate is not exposed on Deactivated rows (re-activation out of scope)");
        cut.FindAll("fluent-button").Any(b => b.TextContent.Contains("Complete")).Should().BeFalse(
            "AC-E7: Complete is not exposed on Deactivated rows");
        cut.FindAll("fluent-button").Any(b => b.TextContent.Contains("Deactivate")).Should().BeFalse(
            "AC-E7: Deactivate is not exposed on a row that is already Deactivated");
        cut.FindAll("fluent-menu-item").Any(m => m.TextContent.Contains("Delete")).Should().BeFalse(
            "AC-E7: no Delete menu item on Deactivated rows");
    }
}
