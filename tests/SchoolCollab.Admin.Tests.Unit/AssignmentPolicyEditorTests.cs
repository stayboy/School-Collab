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
/// bUnit tests for the per-grade <see cref="AssignmentPolicyEditor"/> grid (round B1, Plan (f)
/// AC1/AC2(e)): one row per policy field with a Global-settings value, a Grade-override value (or
/// the "Inherit global" badge) and Edit / Reset row actions; a load failure surfaces a
/// <see cref="FluentMessageBar"/>. The edit dialog's save logic is covered by
/// <see cref="AssignmentPolicyFieldEditDialogTests"/>.
/// </summary>
[TestClass]
public class AssignmentPolicyEditorTests : BunitContext
{
    public AssignmentPolicyEditorTests()
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

    private (ScriptedHandler Handler, Guid GradeId) Register(
        string tenantBody, string gradeBody, HttpStatusCode gradeStatus = HttpStatusCode.OK,
        HttpStatusCode tenantStatus = HttpStatusCode.OK)
    {
        var gradeId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map("GET", "/api/settings/assignment-policy", tenantStatus, tenantBody);
        handler.Map("GET", $"/students/grade-levels/{gradeId}/assignment-policy", gradeStatus, gradeBody);
        handler.Map("PUT", $"/students/grade-levels/{gradeId}/assignment-policy", HttpStatusCode.OK, "");

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton(new AssignmentPolicyApiClient(http));
        var codedValues = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValues);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValues));

        return (handler, gradeId);
    }

    private static string TenantJson(
        string? signature = null, bool? requiresApproval = null, int? maxPrimary = null, int? maxCopy = null,
        bool? mandatoryReview = null, int? archiveGraceDays = null) =>
        Json(new Dictionary<string, object?>
        {
            ["signatureRequirement"] = signature,
            ["requiresApprovalBeforePublish"] = requiresApproval,
            ["maxPrimaryContacts"] = maxPrimary,
            ["maxCopyContacts"] = maxCopy,
            ["mandatoryReview"] = mandatoryReview,
            ["archiveGraceDays"] = archiveGraceDays,
        });

    private static string GradeJson(
        string? signature = null, bool? requiresApproval = null, int? maxPrimary = null, int? maxCopy = null,
        bool? mandatoryReview = null, int? archiveGraceDays = null) =>
        Json(new Dictionary<string, object?>
        {
            ["gradeLevelId"] = Guid.NewGuid(),
            ["signatureRequirement"] = signature,
            ["requiresApprovalBeforePublish"] = requiresApproval,
            ["maxPrimaryContacts"] = maxPrimary,
            ["maxCopyContacts"] = maxCopy,
            ["mandatoryReview"] = mandatoryReview,
            ["archiveGraceDays"] = archiveGraceDays,
            ["updatedAt"] = DateTimeOffset.UnixEpoch,
        });

    private static string Json(Dictionary<string, object?> dict) =>
        System.Text.Json.JsonSerializer.Serialize(dict);

    [TestMethod]
    public void Grid_shows_every_field_with_its_global_and_grade_value()
    {
        var (_, gradeId) = Register(
            tenantBody: TenantJson(signature: "Optional", requiresApproval: true, maxPrimary: 2, maxCopy: 4),
            gradeBody: GradeJson(signature: "Mandatory", maxPrimary: 1));

        var cut = Render<AssignmentPolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Global settings"));
        cut.Markup.Should().Contain("Grade override");
        cut.Markup.Should().Contain("Actions");

        // Every one of the SEVEN policy fields is a row (the seventh is the approval window,
        // round assignment-approval-workflow §3.3).
        cut.Markup.Should().Contain("Guardian signature");
        cut.Markup.Should().Contain("Approval before publish");
        cut.Markup.Should().Contain("Max primary guardian contacts per sendout");
        cut.Markup.Should().Contain("Max copy guardian contacts per sendout");
        cut.Markup.Should().Contain("Guardian review before submit");
        cut.Markup.Should().Contain("Archive grace window (days)");
        cut.Markup.Should().Contain("Approval window (days)");

        // Global column = the tenant default; grade column = the override where one exists.
        cut.Markup.Should().Contain("Optional", "the tenant signature requirement is rendered");
        cut.Markup.Should().Contain("Mandatory (locked)", "the grade override is rendered");
        cut.Markup.Should().Contain("Required", "the tenant approval flag renders as Required");
        cut.Markup.Should().Contain("4", "the tenant copy cap renders");

        // Each row exposes the kebab with Edit + Reset.
        var triggers = cut.FindAll("fluent-button[title^='Actions for']");
        triggers.Count.Should().Be(7, "one kebab per policy field");
        triggers.First().Click();
        cut.FindAll("fluent-menu-item").Should().Contain(i => i.TextContent.Trim() == "Edit");
        cut.FindAll("fluent-menu-item").Should().Contain(i => i.TextContent.Trim() == "Reset");
    }

    [TestMethod]
    public void Grid_shows_inherit_global_badge_per_unset_field()
    {
        var (_, gradeId) = Register(
            tenantBody: TenantJson(signature: "Optional", maxPrimary: 2),
            gradeBody: GradeJson(maxPrimary: 1),
            gradeStatus: HttpStatusCode.OK);

        var cut = Render<AssignmentPolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Inherit global"));

        // Six of the SEVEN fields inherit (only MaxPrimaryContacts is overridden) ⇒ six badges.
        cut.FindAll(".inherit-badge").Count.Should().Be(6,
            "every field without a grade override shows the Inherit global badge");
    }

    [TestMethod]
    public void Grid_with_no_grade_override_row_shows_inherit_badge_for_every_field()
    {
        var (_, gradeId) = Register(
            tenantBody: TenantJson(signature: "Mandatory", requiresApproval: true, maxPrimary: 2, maxCopy: 4),
            gradeBody: string.Empty,
            gradeStatus: HttpStatusCode.NoContent);

        var cut = Render<AssignmentPolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Mandatory (locked)"));
        cut.FindAll(".inherit-badge").Count.Should().Be(7, "a 204 grade read means every field inherits");
    }

    /// <summary>
    /// D8/AC7 (round <c>assignment-rules-policy-rework</c>): the two fields added to the shared set are
    /// exposed by the SAME editor grid — a field the resolver can read but no editor can write would be
    /// dead configuration.
    /// </summary>
    [TestMethod]
    public void Grid_shows_the_guardian_review_and_archive_window_fields() 
    {
        var (_, gradeId) = Register(
            tenantBody: TenantJson(mandatoryReview: true, archiveGraceDays: 30),
            gradeBody: GradeJson(mandatoryReview: false, archiveGraceDays: 45));

        var cut = Render<AssignmentPolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));

        cut.WaitForAssertion(() =>
        {
            var reviewRow = cut.FindAll("tr.fluent-data-grid-row")
                .Single(r => r.TextContent.Contains("Guardian review before submit"));
            reviewRow.TextContent.Should().Contain("Required", "the tenant default renders in the global column");
            reviewRow.TextContent.Should().Contain("Not required", "the grade override renders in its column");

            var archiveRow = cut.FindAll("tr.fluent-data-grid-row")
                .Single(r => r.TextContent.Contains("Archive grace window (days)"));
            archiveRow.TextContent.Should().Contain("30");
            archiveRow.TextContent.Should().Contain("45");
        });
    }

    [TestMethod]
    public void Reset_clears_grade_override_field_and_preserves_others()
    {
        // The grade overrides signature + primary cap; resetting the primary cap must clear only it.
        var (handler, gradeId) = Register(
            tenantBody: TenantJson(signature: "Optional", maxPrimary: 2),
            gradeBody: GradeJson(signature: "Mandatory", maxPrimary: 1));

        var cut = Render<AssignmentPolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));
        cut.WaitForAssertion(() => cut.FindAll("tr.fluent-data-grid-row").Should().NotBeEmpty());

        var row = cut.FindAll("tr.fluent-data-grid-row")
            .Single(r => r.TextContent.Contains("Max primary guardian contacts per sendout"));
        row.QuerySelector("fluent-button")!.Click();   // open the row's overflow kebab menu

        // v5 renders EVERY row's kebab menu items in the DOM (the menu slot is no longer
        // materialised on open), so re-query the ROW after the click and scope the search to
        // that row's own menu — a global "Reset" match now hits one item per row.
        cut.FindAll("tr.fluent-data-grid-row")
            .Single(r => r.TextContent.Contains("Max primary guardian contacts per sendout"))
            .QuerySelectorAll("fluent-menu-item")
            .Single(i => i.TextContent.Trim().Equals("Reset", System.StringComparison.OrdinalIgnoreCase))
            .MenuItemClick();

        var put = handler.Calls.Should().Contain(c => c.Method == "PUT" && c.Url.Contains("assignment-policy")).Which;
        put.Body.Should().Contain("\"maxPrimaryContacts\":null",
            "the reset clears the per-grade override for that field");
        put.Body.Should().Contain("\"signatureRequirement\":\"Mandatory\"",
            "the grade's other override is preserved");
    }

    [TestMethod]
    public void Edit_opens_the_field_edit_dialog_for_that_setting()
    {
        var (_, gradeId) = Register(
            tenantBody: TenantJson(signature: "Optional"),
            gradeBody: string.Empty,
            gradeStatus: HttpStatusCode.NoContent);

        var dialogMock = this.RegisterDialogService();
        dialogMock
            .Setup(d => d.ShowDialogAsync(typeof(AssignmentPolicyFieldEditDialog), It.IsAny<DialogOptions>()))
            .ReturnsAsync(DialogResult.Cancel());

        var cut = Render<AssignmentPolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Guardian signature"));

        // The setting name is a hyperlink label that opens the edit dialog.
        var row = cut.FindAll("tr.fluent-data-grid-row")
            .Single(r => r.TextContent.Contains("Guardian signature"));
        var link = row.QuerySelector("fluent-link.setting-label");
        link.Should().NotBeNull("the setting name renders as the hypertext edit affordance");

        link!.Click();
        dialogMock.Verify(
            d => d.ShowDialogAsync(typeof(AssignmentPolicyFieldEditDialog), It.IsAny<DialogOptions>()),
            Times.Once,
            "clicking the setting name opens the field edit dialog");
    }

    [TestMethod]
    public void Load_failure_shows_an_error_message_bar()
    {
        var (_, gradeId) = Register(
            tenantBody: string.Empty,
            gradeBody: string.Empty,
            gradeStatus: HttpStatusCode.InternalServerError);

        var cut = Render<AssignmentPolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));

        cut.WaitForAssertion(() =>
            cut.FindComponents<FluentMessageBar>()
                .Should().Contain(mb => mb.Instance.Intent == MessageBarIntent.Error,
                    "a failed policy load must surface an error rather than looking like an empty policy"));
    }

    /// <summary>
    /// Round <c>assignment-approval-workflow</c> (spec §3.3/§5): the approval window is the SEVENTH
    /// policy field and it must be reachable from this editor — a field the resolver reads but no
    /// editor can write is dead configuration. Global value + grade override both render, and the
    /// row opens the shared edit dialog like every other field.
    /// </summary>
    [TestMethod]
    public void Grid_shows_the_approval_window_field_WithBothScopes()
    {
        var (_, gradeId) = Register(
            tenantBody: RawTenantJson(approvalWindowDays: 7),
            gradeBody: RawGradeJson(approvalWindowDays: 3));

        var dialogMock = this.RegisterDialogService();
        dialogMock
            .Setup(d => d.ShowDialogAsync(typeof(AssignmentPolicyFieldEditDialog), It.IsAny<DialogOptions>()))
            .ReturnsAsync(DialogResult.Cancel());

        var cut = Render<AssignmentPolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Approval window (days)"));

        var row = cut.FindAll("tr.fluent-data-grid-row")
            .Single(r => r.TextContent.Contains("Approval window (days)"));
        row.TextContent.Should().Contain("7", "the tenant default window renders in the Global column");
        row.TextContent.Should().Contain("3", "the grade override window renders in the Grade column");

        row.QuerySelector("fluent-link.setting-label")!.Click();
        dialogMock.Verify(
            d => d.ShowDialogAsync(typeof(AssignmentPolicyFieldEditDialog), It.IsAny<DialogOptions>()),
            Times.Once,
            "the new row uses the same editor-dialog pattern as every other field");
    }

    private static string RawTenantJson(int? approvalWindowDays) =>
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["requiresApprovalBeforePublish"] = true,
            ["approvalWindowDays"] = approvalWindowDays,
        });

    private static string RawGradeJson(int? approvalWindowDays) =>
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["gradeLevelId"] = Guid.NewGuid(),
            ["approvalWindowDays"] = approvalWindowDays,
            ["updatedAt"] = DateTimeOffset.UnixEpoch,
        });
}
