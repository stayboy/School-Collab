using System.Net;
using System.Text;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;
using TopicDto = SchoolCollab.Students.Core.DTOs.TopicDto;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for <see cref="TopicPeriodsEditDialog"/> — the topic-scoped
/// delivery-period editor (Rev. 6 FR-55..58).
///
/// <para><b>Why a set and not a single value.</b> The period lives on the
/// <c>GradeTopicAssignment</c> bridge row, not on the topic, so one subject can be
/// delivered to a grade in several terms at once — one bridge row per period. A
/// single-value editor (the superseded <c>TopicAssignmentPeriodEditDialog</c>)
/// could not represent that, and the Subjects card could not even show it.</para>
///
/// <para>See <c>documents/solution/subject-topic-delivery-periods.md</c>.</para>
/// </summary>
[TestClass]
public class TopicPeriodsEditDialogTests : BunitContext
{
    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public TopicPeriodsEditDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private static readonly Guid GradeId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TopicId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid YearId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid Term1Id = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid Term2Id = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<(string Method, string Url)> Calls = new();
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses = new();

        public ScriptedHandler Map(string url, HttpStatusCode status, string body)
        {
            _responses[url] = (status, body);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.PathAndQuery;
            Calls.Add((request.Method.Method, url));

            // Exact match wins over prefix, so "/students/periods" does not
            // swallow "/students/periods/active-academic-year".
            if (_responses.TryGetValue(url, out var exact))
            {
                return Task.FromResult(new HttpResponseMessage(exact.Status)
                {
                    Content = new StringContent(exact.Body, Encoding.UTF8, "application/json"),
                });
            }

            foreach (var (key, value) in _responses)
            {
                if (url.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(value.Status)
                    {
                        Content = new StringContent(value.Body, Encoding.UTF8, "application/json"),
                    });
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>
    /// The active academic year plus two sub-terms, and one ARCHIVED year that
    /// must never be offered.
    /// </summary>
    private static string PeriodsJson(Guid archivedYearId) =>
        $$"""
        [
          { "id": "{{YearId}}", "name": "2026 Academic Year", "startDate": "2026-01-01", "endDate": "2026-12-31",
            "status": "Active", "parentPeriodId": null, "nextPeriodId": null, "division": "None",
            "activationToleranceDays": null, "createdAt": "2026-01-01T00:00:00Z", "updatedAt": "2026-01-01T00:00:00Z" },
          { "id": "{{Term1Id}}", "name": "Term 1", "startDate": "2026-02-01", "endDate": "2026-06-30",
            "status": "Active", "parentPeriodId": "{{YearId}}", "nextPeriodId": null, "division": "Terms",
            "activationToleranceDays": null, "createdAt": "2026-01-01T00:00:00Z", "updatedAt": "2026-01-01T00:00:00Z" },
          { "id": "{{Term2Id}}", "name": "Term 2", "startDate": "2026-07-01", "endDate": "2026-12-20",
            "status": "Active", "parentPeriodId": "{{YearId}}", "nextPeriodId": null, "division": "Terms",
            "activationToleranceDays": null, "createdAt": "2026-01-01T00:00:00Z", "updatedAt": "2026-01-01T00:00:00Z" },
          { "id": "{{archivedYearId}}", "name": "2019 Archived Year", "startDate": "2019-01-01", "endDate": "2019-12-31",
            "status": "Archived", "parentPeriodId": null, "nextPeriodId": null, "division": "None",
            "activationToleranceDays": null, "createdAt": "2019-01-01T00:00:00Z", "updatedAt": "2019-01-01T00:00:00Z" }
        ]
        """;

    private ScriptedHandler Register(Guid? archivedYearId = null)
    {
        var handler = new ScriptedHandler()
            .Map("/students/periods", HttpStatusCode.OK, PeriodsJson(archivedYearId ?? Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")))
            .Map("/students/periods/active-academic-year", HttpStatusCode.OK,
                $$"""{"id":"{{YearId}}","name":"2026 Academic Year","startDate":"2026-01-01","endDate":"2026-12-31","status":"Active","parentPeriodId":null,"nextPeriodId":null,"division":"None","createdAt":"2026-01-01T00:00:00Z","updatedAt":"2026-01-01T00:00:00Z"}""")
            .Map("/students/topic-assignments/", HttpStatusCode.OK, "{}");

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var cv = new CodedValuesApiClient(http);
        Services.AddSingleton(cv);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, cv));
        return handler;
    }

    private static TopicPeriodsEditDialog.TopicPeriodsEditModel Model(
        params (Guid? AssignmentId, Guid? PeriodId)[] rows) =>
        new()
        {
            GradeLevelId = GradeId,
            TopicId = TopicId,
            TopicName = "Mathematics",
            Rows = rows
                .Select(r => new TopicPeriodsEditDialog.TopicPeriodRow
                {
                    AssignmentId = r.AssignmentId,
                    PeriodId = r.PeriodId,
                })
                .ToList(),
        };

    private (IRenderedComponent<FluentDialogProvider> Cut, Task<TopicPeriodsEditDialog.TopicPeriodsEditResult?> Task) Show(
        TopicPeriodsEditDialog.TopicPeriodsEditModel model)
    {
        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<
            TopicPeriodsEditDialog,
            TopicPeriodsEditDialog.TopicPeriodsEditModel,
            TopicPeriodsEditDialog.TopicPeriodsEditResult>(
            model, "Delivery periods", DialogSize.Medium);
        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        return (cut, task);
    }

    private IRenderedComponent<FluentDialogProvider> ShowDialog(
        TopicPeriodsEditDialog.TopicPeriodsEditModel model) =>
        Show(model).Cut;

    // ── Period option filtering ────────────────────────────────────────────

    /// <summary>
    /// A subject can be delivered in several terms or across the academic year,
    /// but NEVER into a period that is archived (or deactivated) — the product
    /// refinement of 2026-09-26.
    /// </summary>
    [TestMethod]
    public void Dialog_OffersActiveYearAndTerms_ButNeverArchivedPeriods()
    {
        var archivedId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Register(archivedId);
        var cut = ShowDialog(Model((Guid.NewGuid(), Term1Id)));

        // FluentSelect does not materialise its option children while closed, so
        // assert the bound Items (what the user actually picks from). The empty
        // string is the whole-academic-year entry.
        var items = cut.FindComponent<FluentSelect<string>>().Instance.Items!.ToArray();

        items[0].Should().BeEmpty("the whole-academic-year option comes first");
        items.Should().Contain(YearId.ToString());
        items.Should().Contain(Term1Id.ToString());
        items.Should().Contain(Term2Id.ToString());
        items.Should().NotContain(archivedId.ToString(),
            "an archived period is no longer live and must not be selectable");
    }

    /// <summary>
    /// The multi-term case is the NORM: a subject with two bridge rows must render
    /// one editable row per period, and the result must carry both.
    /// </summary>
    [TestMethod]
    public async Task Dialog_SubjectRunningInTwoTerms_RendersAndKeepsBothPeriods()
    {
        var handler = Register();
        var a1 = Guid.NewGuid();
        var a2 = Guid.NewGuid();

        var (cut, task) = Show(Model((a1, Term1Id), (a2, Term2Id)));

        var selects = cut.FindComponents<FluentSelect<string>>();
        selects.Should().HaveCount(2, "one row per bridge row / delivery period");
        selects[0].Instance.Value.Should().Be(Term1Id.ToString());
        selects[1].Instance.Value.Should().Be(Term2Id.ToString());

        // Saving with no change must NOT rewrite unchanged rows, and the result must
        // still carry both periods.
        cut.Find("form").Submit();

        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull();
        result!.PeriodIds.Should().BeEquivalentTo(new Guid?[] { Term1Id, Term2Id },
            "both delivery periods must survive a no-op save");

        handler.Calls.Should().NotContain(c => c.Url.Contains("/topic-assignments/") && c.Method == "PUT",
            "an unchanged row must not be rewritten");
    }

    /// <summary>
    /// A year-spanning subject (PeriodId = null) must be representable and must not
    /// be confused with "no period options loaded".
    /// </summary>
    [TestMethod]
    public void Dialog_YearSpanningSubject_ShowsTheEmptyPeriodSelection()
    {
        Register();
        var cut = ShowDialog(Model((Guid.NewGuid(), null)));

        var select = cut.FindComponent<FluentSelect<string>>();
        select.Instance.Value.Should().BeEmpty(
            "a null PeriodId means the whole academic year");
    }

    /// <summary>
    /// With every row removed there is nothing to deliver, which must be rejected
    /// rather than silently unassigning the subject from the grade.
    /// </summary>
    [TestMethod]
    public async Task Dialog_AllRowsRemoved_IsRejected()
    {
        Register();
        var cut = ShowDialog(Model((Guid.NewGuid(), Term1Id)));

        cut.Find("fluent-button[aria-label='Remove period row 1']").Click();

        cut.Find("form").Submit();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Keep at least one delivery period"),
            TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// The same period twice would create two identical bridge rows for one
    /// subject — rejected client-side before any request is made.
    /// </summary>
    [TestMethod]
    public async Task Dialog_DuplicatePeriod_IsRejectedWithoutCallingTheApi()
    {
        var handler = Register();
        var cut = ShowDialog(Model((Guid.NewGuid(), Term1Id), (Guid.NewGuid(), Term1Id)));

        cut.Find("form").Submit();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("selected twice"),
            TimeSpan.FromSeconds(5));

        handler.Calls.Should().NotContain(c => c.Url.Contains("/topic-assignments/"),
            "validation must fail before any write");
    }

    /// <summary>
    /// Regression: <c>DialogShellFooter Error="Error"</c> passes the LITERAL string
    /// "Error" (it is a string parameter), so the real message never rendered. The
    /// editor must use <c>Error="@Error"</c> or the server's FR-57/FR-58 422 is
    /// swallowed and the user sees nothing.
    /// </summary>
    [TestMethod]
    public void Dialog_UsesExpressionBoundErrorSoValidationMessagesSurface()
    {
        var source = ReadDialogSource();
        source.Should().Contain("Error=\"@Error\"",
            "Error=\"Error\" is a literal string and swallows every error message");
        source.Should().NotContain("Error=\"Error\"");
    }

    private static string ReadDialogSource()
    {
        var asmDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir, "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Application",
            "Components", "Students", "TopicPeriodsEditDialog.razor"));
        File.Exists(srcPath).Should().BeTrue($"TopicPeriodsEditDialog.razor should exist at '{srcPath}'");
        return File.ReadAllText(srcPath);
    }
}
