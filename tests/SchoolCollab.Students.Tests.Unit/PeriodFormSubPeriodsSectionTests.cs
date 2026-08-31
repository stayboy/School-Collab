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
/// bUnit tests for the atomic-create Sub-periods section in <see cref="PeriodForm"/>
/// (period-activation-guard-atomic-create.md FR-C5 / AC-C4 / FR-C6). The section
/// renders only for a top-level Terms/Semesters create and the POST body carries the
/// serialized sub-period definitions.
/// </summary>
[TestClass]
public class PeriodFormSubPeriodsSectionTests : BunitContext
{
    public PeriodFormSubPeriodsSectionTests()
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
            {
                return new HttpResponseMessage(exact.Status)
                {
                    Content = new StringContent(exact.Body, Encoding.UTF8, "application/json"),
                };
            }
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

    private static PeriodDto Year(Guid id, string division, string status = "Active") =>
        new(
            id,
            "2026",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            status,
            ParentPeriodId: null,
            NextPeriodId: null,
            division,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

    private static string PeriodsJson(params PeriodDto[] periods) =>
        JsonSerializer.Serialize(periods, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string CreateIdJson(Guid id) =>
        $"{{\"id\":\"{id}\",\"subPeriodIds\":[]}}";

    /// <summary>AC-C4: a top-level Terms create renders the Sub-periods section.
    /// The form defaults to None division, so we switch it to Terms first.</summary>
    [TestMethod]
    public void TermsTopLevelCreate_RendersSubPeriodsSection()
    {
        var handler = new ScriptedHandler();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, PeriodsJson());
        Register(handler);

        var cut = Render<PeriodForm>(p => p
            .Add(x => x.CancelRoute, "/students/periods")
            .Add(x => x.PrefillAcademicYear, false));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Division *"));

        var select = cut.FindComponent<FluentSelect<string>>();
        cut.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync("Terms"));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Sub-periods",
            "the atomic-create section header is present for a top-level Terms create"));
        cut.Markup.Should().Contain("Auto-split into 3",
            "the Terms auto-split helper is present and splits into 3 equal spans (FR-C5)");
        cut.Markup.Should().Contain("+ Add sub-period", "an add-row affordance is present");
    }

    /// <summary>AC-C4: the section is hidden when the create is a None-division top-level year.</summary>
    [TestMethod]
    public void NoneDivisionCreate_HidesSubPeriodsSection()
    {
        var handler = new ScriptedHandler();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, PeriodsJson());
        Register(handler);

        var cut = Render<PeriodForm>(p => p
            .Add(x => x.CancelRoute, "/students/periods")
            .Add(x => x.PrefillAcademicYear, false));

        // Default division is None → no sub-period section.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Division *"));
        cut.Markup.Should().NotContain("Auto-split into", "None division shows no sub-period section (FR-C5/AC-C4)");
    }

    /// <summary>FR-C6: the section is hidden in edit mode (PeriodId set → PUT).</summary>
    [TestMethod]
    public void EditMode_HidesSubPeriodsSection()
    {
        var periodId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, PeriodsJson(Year(periodId, "Terms", "Draft")));
        handler.Map("GET", $"/students/periods/{periodId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(Year(periodId, "Terms", "Draft"), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Register(handler);

        var cut = Render<PeriodForm>(p => p
            .Add(x => x.PeriodId, periodId)
            .Add(x => x.CancelRoute, "/students/periods")
            .Add(x => x.PrefillAcademicYear, false));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Edit period"));
        cut.Markup.Should().NotContain("Auto-split into",
            "edit mode never shows the atomic-create sub-period section (FR-C6)");
    }

    /// <summary>FR-C6: sub-period create (?parent=) hides the section.</summary>
    [TestMethod]
    public void SubPeriodCreate_HidesSubPeriodsSection()
    {
        var parentId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, PeriodsJson(Year(parentId, "Terms")));
        Register(handler);

        var cut = Render<PeriodForm>(p => p
            .Add(x => x.InitialParentPeriodId, parentId)
            .Add(x => x.CancelRoute, "/students/periods")
            .Add(x => x.PrefillAcademicYear, false));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Parent academic year"));
        cut.Markup.Should().NotContain("Auto-split into",
            "a sub-period create has no atomic-create section (FR-C6)");
    }
}
