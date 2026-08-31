using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Pages.Periods;
using SchoolCollab.Students.Application.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the <see cref="PeriodForm"/> Division + parent selector
/// (plan-drop-periodtype.md). A top-level year (no sub-period intent) shows the
/// Division selector and no parent dropdown; a sub-period intent (?parent=…)
/// locks the division to the parent's division and shows the parent dropdown.
/// </summary>
[TestClass]
public class PeriodFormTests : BunitContext
{
    public PeriodFormTests()
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
        Services.AddSingleton(new ConfigFlagsApiClient(http));
        Services.AddSingleton(NullLogger<PeriodForm>.Instance);
    }

    private static string YearJson(Guid id, string division) =>
        $"{{\"id\":\"{id}\",\"name\":\"2026\",\"startDate\":\"2026-01-01\",\"endDate\":\"2026-12-31\",\"status\":\"Active\",\"parentPeriodId\":null,\"nextPeriodId\":null,\"division\":\"{division}\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}";

    /// <summary>
    /// A top-level year create (no sub-period intent) shows the Division selector
    /// and no parent dropdown.
    /// </summary>
    [TestMethod]
    public void PeriodForm_TopLevelYear_NoParentSelector()
    {
        var handler = new ScriptedHandler();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");
        Register(handler);

        var cut = Render<PeriodForm>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Division"));

        cut.Markup.Should().NotContain("Parent academic year", "a top-level year has no parent");
    }

    /// <summary>
    /// A sub-period intent (?parent=…) shows the parent dropdown and locks the
    /// division to the parent's division.
    /// </summary>
    [TestMethod]
    public void PeriodForm_SubPeriodIntent_ShowsParentSelector()
    {
        var parentId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, $"[{YearJson(parentId, "Terms")}]");
        Register(handler);

        var cut = Render<PeriodForm>(p => p.Add(x => x.InitialParentPeriodId, parentId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Parent academic year"));

        var divisionSelect = cut.FindComponents<FluentSelect<string>>().First();
        divisionSelect.Instance.Value.Should().Be("Terms", "division is locked to the parent's division");
        divisionSelect.Instance.Disabled.Should().BeTrue("the division is locked while the sub-period intent is set");
    }

    /// <summary>
    /// A sub-period intent on a None-division year surfaces an explicit
    /// framework-mismatch error instead of silently rewriting the form.
    /// </summary>
    [TestMethod]
    public void PeriodForm_SubPeriodIntent_NoneDivision_ShowsError()
    {
        var parentId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, $"[{YearJson(parentId, "None")}]");
        Register(handler);

        var cut = Render<PeriodForm>(p => p.Add(x => x.InitialParentPeriodId, parentId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("sub-periods are not allowed",
            "None division forbids sub-periods; the user must see an explicit error"));

        handler.Calls.Should().NotContain(c => c.Method == "POST" && c.Url == "/students/periods",
            "the framework-mismatch must block the create POST");
    }

    /// <summary>
    /// A sub-period intent on a Semesters-division year locks the division to
    /// Semesters (not a hard-coded Term).
    /// </summary>
    [TestMethod]
    public void PeriodForm_SubPeriodIntent_SemestersDivision_LocksToSemesters()
    {
        var parentId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, $"[{YearJson(parentId, "Semesters")}]");
        Register(handler);

        var cut = Render<PeriodForm>(p => p.Add(x => x.InitialParentPeriodId, parentId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Parent academic year"));

        var divisionSelect = cut.FindComponents<FluentSelect<string>>().First();
        divisionSelect.Instance.Value.Should().Be("Semesters",
            "Semesters division + ?parent=... locks the division to Semesters");
        divisionSelect.Instance.Disabled.Should().BeTrue("the division is locked while the sub-period intent is set");

        cut.Markup.Should().NotContain("sub-periods are not allowed",
            "Semesters division allows Semesters — no error should surface");
    }
}
