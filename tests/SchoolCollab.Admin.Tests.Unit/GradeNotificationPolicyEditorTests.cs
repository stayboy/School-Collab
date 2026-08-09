using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the reworked per-grade Notification &amp; Delivery editor
/// (notification-delivery-plan.md §5): a single grid with per-setting
/// "Global settings" / "Grade override" columns and Edit / Reset row actions.
/// The edit dialog's save logic is covered by
/// <see cref="NotificationPolicyFieldEditDialogTests"/>.
/// </summary>
[TestClass]
public class GradeNotificationPolicyEditorTests : BunitContext
{
    public GradeNotificationPolicyEditorTests()
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
            var key = (request.Method.Method.ToUpperInvariant(), request.RequestUri.PathAndQuery);
            if (_responses.TryGetValue(key, out var hit))
                return new HttpResponseMessage(hit.Status) { Content = new StringContent(hit.Body, Encoding.UTF8, "application/json") };
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected URL: {request.Method.Method} {request.RequestUri.PathAndQuery}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private (ScriptedHandler Handler, Guid GradeId) Register(string tenantBody, string gradeBody, HttpStatusCode gradeStatus = HttpStatusCode.OK)
    {
        var gradeId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map("GET", "/api/settings/notification-policy",
            string.IsNullOrEmpty(tenantBody) ? HttpStatusCode.NoContent : HttpStatusCode.OK, tenantBody);
        handler.Map("GET", $"/students/grade-levels/{gradeId}/notification-policy", gradeStatus, gradeBody);
        handler.Map("PUT", $"/students/grade-levels/{gradeId}/notification-policy", HttpStatusCode.OK, gradeBody);

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton(new NotificationPolicyApiClient(http));
        var codedValues = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValues);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValues));

        return (handler, gradeId);
    }

    private const string NoContent = "";

    private static string TenantJson(int? maxNotifications, int? linkValidityDays, int[] preferredChannels) =>
        Json(new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid(),
            ["preferredChannelOrder"] = preferredChannels,
            ["blockedChannels"] = Array.Empty<int>(),
            ["maxNotifications"] = maxNotifications,
            ["maxReminders"] = null,
            ["reminderIntervalHours"] = null,
            ["linkValidityDays"] = linkValidityDays,
            ["sendoutTimeOfDay"] = null,
            ["sendoutIntervalMinutes"] = null,
            ["updatedAt"] = System.DateTimeOffset.UnixEpoch,
        });

    private static string GradeJson(int? maxNotifications, int? linkValidityDays, int[]? preferredChannels = null) =>
        Json(new Dictionary<string, object?>
        {
            ["gradeLevelId"] = Guid.NewGuid(),
            ["preferredChannelOrder"] = preferredChannels,
            ["blockedChannels"] = Array.Empty<int>(),
            ["maxNotifications"] = maxNotifications,
            ["maxReminders"] = null,
            ["reminderIntervalHours"] = null,
            ["linkValidityDays"] = linkValidityDays,
            ["sendoutTimeOfDay"] = null,
            ["sendoutIntervalMinutes"] = null,
            ["updatedAt"] = System.DateTimeOffset.UnixEpoch,
        });

    private static string Json(Dictionary<string, object?> dict) =>
        System.Text.Json.JsonSerializer.Serialize(dict);

    [TestMethod]
    public void Grid_shows_setting_global_and_grade_override_columns_with_values()
    {
        var (_, gradeId) = Register(
            tenantBody: TenantJson(maxNotifications: 50, linkValidityDays: 7, preferredChannels: [0]),
            gradeBody: GradeJson(maxNotifications: 10, linkValidityDays: null));

        var cut = Render<GradeNotificationPolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Global settings"));
        cut.Markup.Should().Contain("Grade override");
        cut.Markup.Should().Contain("Actions");

        // Global column shows the tenant default (50); grade override column shows 10.
        cut.Markup.Should().Contain("50");
        cut.Markup.Should().Contain("10");
        // Preferred modes of contact resolved from the tenant default (Email = value 0).
        cut.Markup.Should().Contain("Email");
        // 'channels' is labelled 'Mode of Contact'.
        cut.Markup.Should().Contain("Preferred Mode of Contact");

        // The Actions column renders a kebab (⋮) overflow menu per row.
        var triggers = cut.FindAll("fluent-button[title^='Actions for']");
        triggers.Count.Should().BeGreaterThan(0, "each row exposes an overflow kebab menu");
        triggers.First().Click();
        cut.FindAll("fluent-menu-item").Should().Contain(i => i.TextContent.Trim() == "Edit");
        cut.FindAll("fluent-menu-item").Should().Contain(i => i.TextContent.Trim() == "Reset");
    }

    [TestMethod]
    public void Grid_shows_inherit_global_badge_when_grade_has_no_override()
    {
        var (_, gradeId) = Register(
            tenantBody: TenantJson(maxNotifications: 50, linkValidityDays: 7, preferredChannels: []),
            gradeBody: NoContent,
            gradeStatus: HttpStatusCode.NoContent);

        var cut = Render<GradeNotificationPolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Inherit global"));
    }

    [TestMethod]
    public void Reset_clears_grade_override_field_and_preserves_others()
    {
        // Grade override row with maxNotifications=10 AND linkValidityDays=14.
        var (handler, gradeId) = Register(
            tenantBody: TenantJson(maxNotifications: 50, linkValidityDays: 7, preferredChannels: []),
            gradeBody: GradeJson(maxNotifications: 10, linkValidityDays: 14));

        var cut = Render<GradeNotificationPolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));
        cut.WaitForAssertion(() => cut.FindAll("tr.fluent-data-grid-row").Should().NotBeEmpty());

        var row = cut.FindAll("tr.fluent-data-grid-row")
            .Single(r => r.TextContent.Contains("Max notifications per sendout"));
        row.QuerySelector("fluent-button")!.Click();   // open the row's overflow kebab menu

        // Re-query the menu items globally after the click: the row reference
        // captured above is stale once the menu-open re-render runs.
        cut.FindAll("fluent-menu-item")
            .Single(i => i.TextContent.Trim().Equals("Reset", System.StringComparison.OrdinalIgnoreCase))
            .Click();

        var put = handler.Calls.Should().Contain(c => c.Method == "PUT" && c.Url.Contains("notification-policy")).Which;
        put.Body.Should().Contain("\"maxNotifications\":null", "the reset clears the per-grade override for that field");
        put.Body.Should().Contain("\"linkValidityDays\":14", "unrelated grade overrides are preserved");
    }

    [TestMethod]
    public void Edit_opens_the_field_edit_dialog_for_that_setting()
    {
        var (_, gradeId) = Register(
            tenantBody: TenantJson(maxNotifications: 50, linkValidityDays: 7, preferredChannels: []),
            gradeBody: NoContent,
            gradeStatus: HttpStatusCode.NoContent);

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(DialogResult.Cancel()));

        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowDialogAsync<NotificationPolicyFieldEditDialog, DialogShellData<NotificationPolicyFieldEditDialog.EditModel>>(
                It.IsAny<DialogShellData<NotificationPolicyFieldEditDialog.EditModel>>(), It.IsAny<DialogParameters>()))
            .ReturnsAsync(dialogRef.Object);
        Services.AddSingleton(dialogMock.Object);

        var cut = Render<GradeNotificationPolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Max notifications per sendout"));

        // The setting name is a hyperlink label that opens the edit dialog.
        var row = cut.FindAll("tr.fluent-data-grid-row")
            .Single(r => r.TextContent.Contains("Max notifications per sendout"));
        row.QuerySelector("fluent-anchor")!.Click();

        dialogMock.Verify(d => d.ShowDialogAsync<NotificationPolicyFieldEditDialog, DialogShellData<NotificationPolicyFieldEditDialog.EditModel>>(
            It.IsAny<DialogShellData<NotificationPolicyFieldEditDialog.EditModel>>(), It.IsAny<DialogParameters>()), Times.Once);
    }
}
