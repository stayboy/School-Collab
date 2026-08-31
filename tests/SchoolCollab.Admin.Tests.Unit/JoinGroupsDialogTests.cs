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
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the span-aware <see cref="JoinGroupsDialog"/> (Sprint 6
/// Round 3, AC-35/36). Rendered through the real <see cref="FluentDialogProvider"/>
/// + <c>DialogService.ShowShellDialogAsync</c> pipeline. Verifies the active
/// period-type resolution and that period-aligned spans are filtered to the
/// currently-open period while OpenEnded groups are always joinable.
/// </summary>
[TestClass]
public class JoinGroupsDialogTests : BunitContext
{
    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public JoinGroupsDialogTests()
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

    private static readonly Guid StudentId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid OpenGroupId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TermGroupId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SemGroupId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid YearId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid TermId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private void Register(ScriptedHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var cv = new CodedValuesApiClient(http);
        Services.AddSingleton(cv);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, cv));
    }

    private static string GroupJson(Guid id, string name, string span) =>
        $"{{\"id\":\"{id}\",\"name\":\"{name}\",\"description\":null,\"category\":null,\"capacity\":null,\"isActive\":true,\"span\":\"{span}\",\"enrollmentStartDate\":null,\"enrollmentEndDate\":null,\"autoRenewDefault\":true,\"eligibleGradeIds\":[],\"activeMemberCount\":0,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}";

    private static string PeriodJson(Guid id, string name, string periodType) =>
        periodType == "Term"
            ? $"{{\"id\":\"{id}\",\"name\":\"{name}\",\"startDate\":\"2026-01-01\",\"endDate\":\"2026-12-31\",\"status\":\"Active\",\"parentPeriodId\":\"{YearId}\",\"nextPeriodId\":null,\"division\":\"Terms\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}"
            : $"{{\"id\":\"{id}\",\"name\":\"{name}\",\"startDate\":\"2026-01-01\",\"endDate\":\"2026-12-31\",\"status\":\"Active\",\"parentPeriodId\":null,\"nextPeriodId\":null,\"division\":\"None\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}";

    /// <summary>
    /// A7 (AC-36): with no active period (both active-period GETs 404), the
    /// active period type resolves to AcademicYear and an OpenEnded group is
    /// listed as joinable.
    /// </summary>
    [TestMethod]
    public async Task JoinDialog_OpenEnded_Listed_WhenNoActivePeriod()
    {
        var handler = new ScriptedHandler();
        handler.Map("GET", "/activity-groups", HttpStatusCode.OK,
            $"[{GroupJson(OpenGroupId, "Chess Club", "OpenEnded")}]");
        handler.Map("GET", $"/students/{StudentId}/activity-groups", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/students/periods/active-sub-period", HttpStatusCode.NotFound, "{}");
        handler.Map("GET", "/students/periods/active-academic-year", HttpStatusCode.NotFound, "{}");
        Register(handler);

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<JoinGroupsDialog, JoinGroupsDialog.JoinGroupsModel, JoinGroupsDialog.JoinGroupsResult>(
            new JoinGroupsDialog.JoinGroupsModel { StudentId = StudentId }, "Join groups", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Chess Club"));
        cut.Markup.Should().Contain("OpenEnded", "the group option text shows the span");

        // Cleanup: close the dialog.
        cut.Find("fluent-button[aria-label='Close']").Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("closing the dialog yields no result");
    }

    /// <summary>
    /// A8 (AC-35): with an active Term, a Termly group is listed but a Semester
    /// group is filtered out (period-aligned spans only join the matching period).
    /// </summary>
    [TestMethod]
    public async Task JoinDialog_Termly_Listed_SemesterFiltered_WhenActiveTerm()
    {
        var handler = new ScriptedHandler();
        handler.Map("GET", "/activity-groups", HttpStatusCode.OK,
            $"[{GroupJson(TermGroupId, "Chess Club", "Termly")},{GroupJson(SemGroupId, "Semester Band", "Semester")}]");
        handler.Map("GET", $"/students/{StudentId}/activity-groups", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/students/periods/active-sub-period", HttpStatusCode.OK, PeriodJson(TermId, "Term 1", "Term"));
        handler.Map("GET", "/students/periods/active-academic-year", HttpStatusCode.OK, PeriodJson(YearId, "2026", "AcademicYear"));
        Register(handler);

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<JoinGroupsDialog, JoinGroupsDialog.JoinGroupsModel, JoinGroupsDialog.JoinGroupsResult>(
            new JoinGroupsDialog.JoinGroupsModel { StudentId = StudentId }, "Join groups", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Chess Club"));
        cut.Markup.Should().NotContain("Semester Band", "a Semester group is not joinable while a Term is active (AC-35)");

        // Cleanup: close the dialog.
        cut.Find("fluent-button[aria-label='Close']").Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("closing the dialog yields no result");
    }
}
