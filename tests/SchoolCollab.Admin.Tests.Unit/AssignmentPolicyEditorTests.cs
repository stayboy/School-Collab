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
        string? signature = null, bool? requiresApproval = null, int? maxPrimary = null, int? maxCopy = null) =>
        Json(new Dictionary<string, object?>
        {
            ["signatureRequirement"] = signature,
            ["requiresApprovalBeforePublish"] = requiresApproval,
            ["maxPrimaryContacts"] = maxPrimary,
            ["maxCopyContacts"] = maxCopy,
        });

    private static string GradeJson(
        string? signature = null, bool? requiresApproval = null, int? maxPrimary = null, int? maxCopy = null) =>
        Json(new Dictionary<string, object?>
        {
            ["gradeLevelId"] = Guid.NewGuid(),
            ["signatureRequirement"] = signature,
            ["requiresApprovalBeforePublish"] = requiresApproval,
            ["maxPrimaryContacts"] = maxPrimary,
            ["maxCopyContacts"] = maxCopy,
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

        // Every one of the four policy fields is a row.
        cut.Markup.Should().Contain("Guardian signature");
        cut.Markup.Should().Contain("Approval before publish");
        cut.Markup.Should().Contain("Max primary guardian contacts per sendout");
        cut.Markup.Should().Contain("Max copy guardian contacts per sendout");

        // Global column = the tenant default; grade column = the override where one exists.
        cut.Markup.Should().Contain("Optional", "the tenant signature requirement is rendered");
        cut.Markup.Should().Contain("Mandatory (locked)", "the grade override is rendered");
        cut.Markup.Should().Contain("Required", "the tenant approval flag renders as Required");
        cut.Markup.Should().Contain("4", "the tenant copy cap renders");

        // Each row exposes the kebab with Edit + Reset.
        var triggers = cut.FindAll("fluent-button[title^='Actions for']");
        triggers.Count.Should().Be(4, "one kebab per policy field");
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

        // Three of the four fields inherit (only MaxPrimaryContacts is overridden) ⇒ three badges.
        cut.FindAll(".inherit-badge").Count.Should().Be(3,
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
        cut.FindAll(".inherit-badge").Count.Should().Be(4, "a 204 grade read means every field inherits");
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

        // Re-query the menu items globally after the click: the row reference is stale once the
        // menu-open re-render runs.
        cut.FindAll("fluent-menu-item")
            .Single(i => i.TextContent.Trim().Equals("Reset", System.StringComparison.OrdinalIgnoreCase))
            .Click();

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

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(DialogResult.Cancel()));

        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowDialogAsync<AssignmentPolicyFieldEditDialog, DialogShellData<AssignmentPolicyFieldEditDialog.EditModel>>(
                It.IsAny<DialogShellData<AssignmentPolicyFieldEditDialog.EditModel>>(), It.IsAny<DialogParameters>()))
            .ReturnsAsync(dialogRef.Object);
        Services.AddSingleton(dialogMock.Object);

        var cut = Render<AssignmentPolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Guardian signature"));

        // The setting name is a hyperlink label that opens the edit dialog.
        var row = cut.FindAll("tr.fluent-data-grid-row")
            .Single(r => r.TextContent.Contains("Guardian signature"));
        row.QuerySelector("fluent-anchor")!.Click();

        dialogMock.Verify(d => d.ShowDialogAsync<AssignmentPolicyFieldEditDialog, DialogShellData<AssignmentPolicyFieldEditDialog.EditModel>>(
            It.IsAny<DialogShellData<AssignmentPolicyFieldEditDialog.EditModel>>(), It.IsAny<DialogParameters>()), Times.Once);
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
                .Should().Contain(mb => mb.Instance.Intent == MessageIntent.Error,
                    "a failed policy load must surface an error rather than looking like an empty policy"));
    }
}
