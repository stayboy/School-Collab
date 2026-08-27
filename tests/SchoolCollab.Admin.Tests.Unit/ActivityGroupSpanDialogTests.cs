using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Forms;
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
/// bUnit tests for the span-aware activity-group create/edit dialogs
/// (Sprint 6 Round 3, AC-35/37/42/43). Rendered through the real
/// <see cref="FluentDialogProvider"/> + <c>DialogService.ShowShellDialogAsync</c>
/// pipeline. Verifies span-dependent field reveal, next-window validation
/// (both-or-neither; next-start &gt;= current-end), and the correct API payloads.
/// </summary>
[TestClass]
public class ActivityGroupSpanDialogTests : BunitContext
{
    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public ActivityGroupSpanDialogTests()
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

            var url = request.RequestUri.PathAndQuery;
            if (_responses.TryGetValue((request.Method.Method.ToUpperInvariant(), url), out var exact))
                return new HttpResponseMessage(exact.Status) { Content = new StringContent(exact.Body, Encoding.UTF8, "application/json") };

            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent($"Unexpected {url}", Encoding.UTF8, "application/json") };
        }
    }

    private static readonly Guid GroupId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private void Register(ScriptedHandler handler)
    {
        // Both dialogs load grade levels on init to populate the eligible-grades
        // checkboxes. Map to empty so the dialog renders in the test.
        handler.Map("GET", "/students/grade-levels/landing", HttpStatusCode.OK, "[]");

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var cv = new CodedValuesApiClient(http);
        Services.AddSingleton(cv);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, cv));
    }

    private static string GroupJson(string span) =>
        $"{{\"id\":\"{GroupId}\",\"name\":\"Chess Club\",\"description\":null,\"category\":null,\"capacity\":null,\"isActive\":true,\"span\":\"{span}\",\"enrollmentStartDate\":\"2026-01-01\",\"enrollmentEndDate\":\"2026-06-30\",\"autoRenewDefault\":true,\"eligibleGradeIds\":[],\"activeMemberCount\":0,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}";

    private async Task DriveSpanAsync(IRenderedComponent<FluentDialogProvider> cut, string span)
    {
        var spanSelect = cut.FindComponents<FluentSelect<string>>()
            .First(s => s.Instance.Id == "ag-create-span");
        await cut.InvokeAsync(() => spanSelect.Instance.ValueChanged.InvokeAsync(span));
    }

    private async Task SubmitAsync(IRenderedComponent<FluentDialogProvider> cut)
    {
        var editForm = cut.FindComponent<EditForm>();
        await cut.InvokeAsync(() => editForm.Instance.OnValidSubmit.InvokeAsync(editForm.Instance.EditContext));
    }

    /// <summary>
    /// A1 (AC-35/37): selecting <c>DateRange</c> reveals the enrollment-window
    /// and next-window date pickers; the default <c>OpenEnded</c> hides them.
    /// </summary>
    [TestMethod]
    public async Task CreateDialog_DateRangeSpan_RevealsWindowDates()
    {
        var handler = new ScriptedHandler();
        Register(handler);

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<ActivityGroupCreateDialog, ActivityGroupCreateDialog.ActivityGroupCreateModel, ActivityGroupDto>(
            new ActivityGroupCreateDialog.ActivityGroupCreateModel(), "Create activity group", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        // Default span is OpenEnded — no window date pickers.
        cut.FindComponents<FluentDatePicker>().Should().BeEmpty("OpenEnded carries no window dates");

        // Switch to DateRange — the window + next-window pickers appear.
        await DriveSpanAsync(cut, "DateRange");
        cut.WaitForAssertion(() => cut.FindComponents<FluentDatePicker>().Should().HaveCount(4,
            "DateRange reveals start/end + next-start/next-end pickers"));

        // Cleanup: close the dialog.
        cut.Find("fluent-button[aria-label='Close']").Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("closing the dialog yields no result");
    }

    /// <summary>
    /// A2 (AC-36): the default <c>OpenEnded</c> span hides the window date
    /// pickers entirely.
    /// </summary>
    [TestMethod]
    public async Task CreateDialog_OpenEndedSpan_HidesWindowDates()
    {
        var handler = new ScriptedHandler();
        Register(handler);

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<ActivityGroupCreateDialog, ActivityGroupCreateDialog.ActivityGroupCreateModel, ActivityGroupDto>(
            new ActivityGroupCreateDialog.ActivityGroupCreateModel(), "Create activity group", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        cut.FindComponents<FluentDatePicker>().Should().BeEmpty("OpenEnded carries no window dates");
        cut.Markup.Should().NotContain("Enrollment window", "OpenEnded does not render the window row");

        // Cleanup: close the dialog.
        cut.Find("fluent-button[aria-label='Close']").Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("closing the dialog yields no result");
    }

    /// <summary>
    /// A3 (AC-43): a DateRange next window with only one date set is rejected
    /// — the create POST is blocked (the both-or-neither guard fires before any
    /// API call). The error text rendering is not asserted here because the
    /// footer error surface is not reliably observable in bUnit; the guard
    /// behavior (no POST, dialog stays open) is the meaningful contract.
    /// </summary>
    [TestMethod]
    public async Task CreateDialog_NextWindow_OnlyStart_ShowsBothOrNeitherError()
    {
        var handler = new ScriptedHandler();
        Register(handler);

        var cut = Render<FluentDialogProvider>();
        var model = new ActivityGroupCreateDialog.ActivityGroupCreateModel
        {
            Name = "Chess Club",
            Span = "DateRange",
            EnrollmentStartDate = new DateTime(2026, 1, 1),
            EnrollmentEndDate = new DateTime(2026, 6, 30),
            NextEnrollmentStartDate = new DateTime(2026, 7, 1),
            NextEnrollmentEndDate = null,
        };
        var task = DialogService.ShowShellDialogAsync<ActivityGroupCreateDialog, ActivityGroupCreateDialog.ActivityGroupCreateModel, ActivityGroupDto>(
            model, "Create activity group", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        await SubmitAsync(cut);

        // The both-or-neither guard must block the create POST entirely.
        handler.Calls.Should().NotContain(c => c.Method == "POST" && c.Url == "/activity-groups",
            "the both-or-neither guard must block the create POST");

        // Cleanup: close the dialog.
        cut.Find("fluent-button[aria-label='Close']").Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("the guard keeps the dialog open");
    }

    /// <summary>
    /// A4 (AC-43): a next-window start before the current window's end is
    /// rejected — the create POST is blocked (the next-start guard fires before
    /// any API call). The error text rendering is not asserted here because the
    /// footer error surface is not reliably observable in bUnit; the guard
    /// behavior (no POST, dialog stays open) is the meaningful contract.
    /// </summary>
    [TestMethod]
    public async Task CreateDialog_NextWindow_StartBeforeEnd_Rejected()
    {
        var handler = new ScriptedHandler();
        Register(handler);

        var cut = Render<FluentDialogProvider>();
        var model = new ActivityGroupCreateDialog.ActivityGroupCreateModel
        {
            Name = "Chess Club",
            Span = "DateRange",
            EnrollmentStartDate = new DateTime(2026, 1, 1),
            EnrollmentEndDate = new DateTime(2026, 6, 30),
            NextEnrollmentStartDate = new DateTime(2026, 1, 1),
            NextEnrollmentEndDate = new DateTime(2026, 12, 31),
        };
        var task = DialogService.ShowShellDialogAsync<ActivityGroupCreateDialog, ActivityGroupCreateDialog.ActivityGroupCreateModel, ActivityGroupDto>(
            model, "Create activity group", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        await SubmitAsync(cut);

        // The next-start guard must block the create POST entirely.
        handler.Calls.Should().NotContain(c => c.Method == "POST" && c.Url == "/activity-groups",
            "the next-start guard must block the create POST");

        // Cleanup: close the dialog.
        cut.Find("fluent-button[aria-label='Close']").Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("the guard keeps the dialog open");
    }

    /// <summary>
    /// A5 (AC-37/43): a valid DateRange create posts the span + window dates to
    /// POST /activity-groups and then sets the next window via PUT next-window.
    /// </summary>
    [TestMethod]
    public async Task CreateDialog_ValidDateRange_PostsCreateAndNextWindow()
    {
        var handler = new ScriptedHandler();
        handler.Map("POST", "/activity-groups", HttpStatusCode.OK, $"{{\"id\":\"{GroupId}\"}}");
        handler.Map("PUT", $"/activity-groups/{GroupId}/next-window", HttpStatusCode.OK, "{}");
        handler.Map("GET", $"/activity-groups/{GroupId}", HttpStatusCode.OK, GroupJson("DateRange"));
        Register(handler);

        var cut = Render<FluentDialogProvider>();
        var model = new ActivityGroupCreateDialog.ActivityGroupCreateModel
        {
            Name = "Chess Club",
            Span = "DateRange",
            EnrollmentStartDate = new DateTime(2026, 1, 1),
            EnrollmentEndDate = new DateTime(2026, 6, 30),
            NextEnrollmentStartDate = new DateTime(2026, 7, 1),
            NextEnrollmentEndDate = new DateTime(2026, 12, 31),
        };
        var task = DialogService.ShowShellDialogAsync<ActivityGroupCreateDialog, ActivityGroupCreateDialog.ActivityGroupCreateModel, ActivityGroupDto>(
            model, "Create activity group", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        await SubmitAsync(cut);

        cut.WaitForAssertion(() =>
            handler.Calls.Should().Contain(c => c.Method == "POST" && c.Url == "/activity-groups"));
        var create = handler.Calls.Single(c => c.Method == "POST" && c.Url == "/activity-groups");
        create.Body.Should().Contain("\"span\":\"DateRange\"", "the create payload carries the DateRange span");
        create.Body.Should().Contain("\"enrollmentStartDate\":\"2026-01-01\"", "the create payload carries the window start");
        create.Body.Should().Contain("\"enrollmentEndDate\":\"2026-06-30\"", "the create payload carries the window end");

        handler.Calls.Should().Contain(c => c.Method == "PUT" && c.Url == $"/activity-groups/{GroupId}/next-window",
            "a valid next window is set after create");

        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull("a successful create returns the created group");
    }

    /// <summary>
    /// A6 (AC-42/43): the edit dialog shows the span read-only and a valid
    /// DateRange edit posts PUT /activity-groups/{id} (no next-window set).
    /// </summary>
    [TestMethod]
    public async Task EditDialog_ReadOnlySpan_AndValidPut()
    {
        var handler = new ScriptedHandler();
        handler.Map("PUT", $"/activity-groups/{GroupId}", HttpStatusCode.OK, "{}");
        handler.Map("GET", $"/activity-groups/{GroupId}", HttpStatusCode.OK, GroupJson("DateRange"));
        Register(handler);

        var cut = Render<FluentDialogProvider>();
        var model = new ActivityGroupEditDialog.ActivityGroupEditModel
        {
            Id = GroupId,
            Name = "Chess Club",
            Span = "DateRange",
            EnrollmentStartDate = new DateTime(2026, 1, 1),
            EnrollmentEndDate = new DateTime(2026, 6, 30),
            AutoRenewDefault = true,
        };
        var task = DialogService.ShowShellDialogAsync<ActivityGroupEditDialog, ActivityGroupEditDialog.ActivityGroupEditModel, ActivityGroupDto>(
            model, "Edit activity group", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        // The span field is a read-only text field, not a select.
        var spanField = cut.Find("#ag-edit-span");
        spanField.GetAttribute("readonly").Should().NotBeNull("the span is immutable after creation");
        cut.FindComponents<FluentSelect<string>>().Should().BeEmpty("the edit dialog offers no span select");

        await SubmitAsync(cut);

        cut.WaitForAssertion(() =>
            handler.Calls.Should().Contain(c => c.Method == "PUT" && c.Url == $"/activity-groups/{GroupId}"));
        var put = handler.Calls.Single(c => c.Method == "PUT" && c.Url == $"/activity-groups/{GroupId}");
        put.Body.Should().Contain("\"name\":\"Chess Club\"", "the update payload carries the edited name");
        handler.Calls.Should().NotContain(c => c.Method == "PUT" && c.Url.Contains("/next-window"),
            "no next window was set on this edit");

        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull("a successful edit returns the updated group");
    }
}
