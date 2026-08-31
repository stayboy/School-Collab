using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Pages.Periods;
using SchoolCollab.Students.Application.Services;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// bUnit tests for the P2-5/P2-6 fix in <see cref="PeriodForm"/>: a
/// create-from-?parent= intent against a None-division parent year must render
/// an inline cannot-host warning with a working "Back to periods" affordance
/// instead of the dead-end form, and the academic-year prefill must be skipped
/// (P2-6) rather than silently gated on the error surface.
/// </summary>
[TestClass]
public class PeriodFormBlockedParentTests : BunitContext
{
    public PeriodFormBlockedParentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string? Body)> Calls = new();
        private readonly Dictionary<(string Method, string Url), (HttpStatusCode Status, string Body)> _responses = new();

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            _responses[(method.ToUpperInvariant(), url)] = (status, body);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery, body));

            var url = request.RequestUri!.PathAndQuery;
            if (_responses.TryGetValue((request.Method.Method.ToUpperInvariant(), url), out var exact))
                return new HttpResponseMessage(exact.Status)
                {
                    Content = new StringContent(exact.Body, Encoding.UTF8, "application/json"),
                };

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected {url}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private void Register(ScriptedHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var cv = new CodedValuesApiClient(http);
        Services.AddSingleton(cv);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, cv));
        Services.AddSingleton(NullLogger<PeriodForm>.Instance);
    }

    private static PeriodDto Year(Guid id, string division) =>
        new(
            id,
            "2026",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            "Active",
            ParentPeriodId: null,
            NextPeriodId: null,
            division,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

    /// <summary>
    /// Serialize a real <see cref="PeriodDto"/> with the same web JSON options
    /// <see cref="StudentsApiClient.ListPeriodsAsync"/> uses (camelCase) — never
    /// hand-write the casing.
    /// </summary>
    private static string PeriodsJson(params PeriodDto[] periods) =>
        JsonSerializer.Serialize(periods, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>
    /// P2-5: a create-from-?parent= intent against a None-division year renders
    /// the cannot-host warning, hides the editable form (no Division select), and
    /// shows a "Back to periods" affordance.
    /// </summary>
    [TestMethod]
    public void BlockedParent_NoneDivision_RendersWarning_NoForm_BackButton()
    {
        var parentId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, PeriodsJson(Year(parentId, "None")));
        Register(handler);

        var cut = Render<PeriodForm>(p => p
            .Add(x => x.InitialParentPeriodId, parentId)
            .Add(x => x.CancelRoute, "/students/periods"));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("sub-periods are not allowed",
            "a None-division parent cannot host sub-periods; the user must see the cannot-host warning"));

        // The editable form (Division select, name, dates, submit) is replaced by
        // the blocked panel — no FluentSelect is rendered.
        cut.FindComponents<FluentSelect<string>>().Should().BeEmpty(
            "the blocked panel replaces the editable form, so no Division select is rendered");

        // A working back affordance is present.
        cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Back to periods"))
            .Should().NotBeNull("the blocked panel must offer a Back to periods affordance");

        // P1: the create-hint paragraph is suppressed in the blocked render (it
        // would be factually false under the cannot-host panel), while the
        // "New period" header title is kept.
        cut.Markup.Should().NotContain("Use the buttons to suggest or backfill",
            "the block-rendered create hint is false under the cannot-host panel, so it must be suppressed");
        cut.Markup.Should().Contain("New period",
            "the 'New period' header title stays accurate in blocked create mode");

        // P2-1: the Back button sits inside a mt-3 spacing wrapper (utopia pattern).
        cut.Find("div.mt-3 fluent-button")
            .TextContent.Should().Contain("Back to periods", "the back affordance is wrapped for spacing");

        // P2-2: focus management on the Back button.
        cut.Markup.Should().Contain("autofocus", "the back affordance autofocuses for keyboard users");
    }

    /// <summary>
    /// P2-5 affordance: clicking "Back to periods" navigates to the CancelRoute
    /// (honouring the component's CancelAsync, not a hardcoded route).
    /// </summary>
    [TestMethod]
    public void BlockedParent_BackButton_NavigatesToCancelRoute()
    {
        var parentId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, PeriodsJson(Year(parentId, "None")));
        Register(handler);

        var cut = Render<PeriodForm>(p => p
            .Add(x => x.InitialParentPeriodId, parentId)
            .Add(x => x.CancelRoute, "/students/periods"));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Back to periods"));

        cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Back to periods")).Click();

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.Uri.Should().EndWith("/students/periods",
            "the Back affordance must navigate to the CancelRoute");
    }

    /// <summary>
    /// P2-6: in the blocked render the name input is not shown (the form is
    /// replaced by the panel), so no academic-year prefill value is applied or
    /// left behind the panel.
    /// </summary>
    [TestMethod]
    public void BlockedParent_NoneDivision_NoNameInput_NoPrefill()
    {
        var parentId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, PeriodsJson(Year(parentId, "None")));
        Register(handler);

        var cut = Render<PeriodForm>(p => p
            .Add(x => x.InitialParentPeriodId, parentId)
            .Add(x => x.CancelRoute, "/students/periods"));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("sub-periods are not allowed"));

        // The name field is not rendered in the blocked panel, so no prefill value
        // is visible (P2-6: the prefill skip is an explicit consequence of the
        // blocked state, not a stale value behind the panel).
        cut.FindAll("#periodName").Should().BeEmpty(
            "the blocked panel replaces the form, so the name input (and any prefill) is not rendered");
    }

    /// <summary>
    /// Positive control: a Terms-division parent renders the normal pre-locked
    /// sub-period form (Division locked to Terms, parent dropdown shown) with no
    /// blocked warning.
    /// </summary>
    [TestMethod]
    public void BlockedParent_TermsDivision_RendersNormalForm_NoWarning()
    {
        var parentId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, PeriodsJson(Year(parentId, "Terms")));
        Register(handler);

        var cut = Render<PeriodForm>(p => p
            .Add(x => x.InitialParentPeriodId, parentId)
            .Add(x => x.CancelRoute, "/students/periods"));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Parent academic year",
            "a Terms-division parent is a valid sub-period intent, so the parent dropdown is shown"));

        var divisionSelect = cut.FindComponents<FluentSelect<string>>().First();
        divisionSelect.Instance.Value.Should().Be("Terms", "division is locked to the parent's division");
        divisionSelect.Instance.Disabled.Should().BeTrue("the division is locked while the sub-period intent is set");

        cut.Markup.Should().NotContain("sub-periods are not allowed",
            "Terms division allows sub-periods — no blocked warning should surface");
    }
}
