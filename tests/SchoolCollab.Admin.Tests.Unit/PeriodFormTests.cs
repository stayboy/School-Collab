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
/// bUnit tests for the <see cref="PeriodForm"/> PeriodType + parent selector
/// validation (Sprint 6 Round 3, C1-C3). The parent academic-year dropdown
/// appears only for Term/Semester, and a Term/Semester without a parent is
/// rejected on submit.
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
        Services.AddSingleton(NullLogger<PeriodForm>.Instance);
    }

    private async Task DrivePeriodTypeAsync(IRenderedComponent<PeriodForm> cut, string periodType)
    {
        // The period-type select is the first FluentSelect<string> in the form
        // (it has no Id attribute). In create mode only the type select exists
        // until a Term/Semester is chosen, so First() is the type selector.
        var typeSelect = cut.FindComponents<FluentSelect<string>>().First();
        await cut.InvokeAsync(() => typeSelect.Instance.ValueChanged.InvokeAsync(periodType));
    }

    /// <summary>
    /// C1: selecting Term reveals the parent academic-year dropdown.
    /// </summary>
    [TestMethod]
    public async Task PeriodForm_Term_ShowsParentSelector()
    {
        var handler = new ScriptedHandler();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");
        Register(handler);

        var cut = Render<PeriodForm>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Period type"));

        // Default is AcademicYear — no parent dropdown.
        cut.Markup.Should().NotContain("Parent academic year", "AcademicYear has no parent");

        await DrivePeriodTypeAsync(cut, "Term");
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Parent academic year",
            "a Term period requires a parent academic year"));
    }

    /// <summary>
    /// C2: the default AcademicYear type hides the parent dropdown.
    /// </summary>
    [TestMethod]
    public void PeriodForm_AcademicYear_HidesParentSelector()
    {
        var handler = new ScriptedHandler();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");
        Register(handler);

        var cut = Render<PeriodForm>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Period type"));

        cut.Markup.Should().NotContain("Parent academic year", "AcademicYear has no parent");
    }

    /// <summary>
    /// C3: submitting a Term without a parent shows the validation error.
    /// </summary>
    [TestMethod]
    public async Task PeriodForm_Term_NoParent_ShowsError()
    {
        var handler = new ScriptedHandler();
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");
        Register(handler);

        var cut = Render<PeriodForm>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Period type"));

        await DrivePeriodTypeAsync(cut, "Term");
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Parent academic year"));

        // The submit button is a direct-OnClick FluentButton (not an EditForm
        // submit), so clicking it is the correct driving approach here.
        var submit = cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Create period"));
        submit.Click();

        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("Select a parent academic year for this period."));
        handler.Calls.Should().NotContain(c => c.Method == "POST" && c.Url == "/students/periods",
            "the parent guard must block the create POST");
    }
}
