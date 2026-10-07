using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;
using SchoolCollab.Students.Core.DTOs;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for <see cref="AssignmentPolicyFieldEditDialog"/> — the dialog opened from the
/// grade-detail "Assignment Policy" grid's Edit action (round B1, Plan (f) AC2). Rendered through
/// the real <see cref="FluentDialogProvider"/> + <c>DialogService.ShowShellDialogAsync</c> pipeline.
///
/// <para>Discriminating cases: (a) a Global-only change writes exactly one
/// <c>PUT /api/settings/assignment-policy</c> and preserves the other fields; (b) a grade-only
/// change writes exactly one <c>PUT /grade-levels/{id}/assignment-policy</c>; (c) both scopes
/// changed ⇒ both PUTs; (d) open + save with NO edits ⇒ <b>zero</b> PUTs.</para>
///
/// <para><b>Flake discipline</b> (Plan risk R-B1-5 / the
/// <c>fix-flaky-bunit-fluentui-after-cascade</c> skill): each interaction is driven through the
/// component callback and proven applied — the editor re-renders the option/value the dialog
/// stored — before the next interaction runs; never a Change/Change/Submit chain.</para>
/// </summary>
[TestClass]
public class AssignmentPolicyFieldEditDialogTests : BunitContext
{
    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public AssignmentPolicyFieldEditDialogTests()
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

    private ScriptedHandler Register(Guid gradeId)
    {
        var handler = new ScriptedHandler();
        handler.Map("PUT", "/api/settings/assignment-policy", HttpStatusCode.OK, "");
        handler.Map("PUT", $"/students/grade-levels/{gradeId}/assignment-policy", HttpStatusCode.OK, "");

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton(new AssignmentPolicyApiClient(http));
        var codedValues = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValues);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValues));
        return handler;
    }

    private static TenantAssignmentPolicyDto Tenant(
        SignatureRequirementMode? signature = null,
        bool? requiresApproval = null,
        int? maxPrimary = null,
        int? maxCopy = null,
        bool? mandatoryReview = null,
        int? archiveGraceDays = null) =>
        new(signature, requiresApproval, maxPrimary, maxCopy, mandatoryReview, archiveGraceDays);

    private static GradeAssignmentPolicyDto Grade(
        Guid gradeId,
        SignatureRequirementMode? signature = null,
        bool? requiresApproval = null,
        int? maxPrimary = null,
        int? maxCopy = null,
        bool? mandatoryReview = null,
        int? archiveGraceDays = null) =>
        new(gradeId, signature, requiresApproval, maxPrimary, maxCopy, mandatoryReview, archiveGraceDays,
            DateTimeOffset.UnixEpoch);

    private sealed record OpenDialog(
        IRenderedComponent<FluentDialogProvider> Cut,
        Task<AssignmentPolicyFieldEditDialog.EditResult?> Task);

    private OpenDialog Open(AssignmentPolicyFieldEditDialog.EditModel model)
    {
        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<AssignmentPolicyFieldEditDialog,
            AssignmentPolicyFieldEditDialog.EditModel,
            AssignmentPolicyFieldEditDialog.EditResult>(model, $"Edit {model.Label}", DialogSize.Large);

        cut.WaitForAssertion(() => cut.Find("#apd-global").Should().NotBeNull(),
            TimeSpan.FromSeconds(5));

        return new OpenDialog(cut, task);
    }

    /// <summary>
    /// Drives one select editor (identified by its DOM id) to <paramref name="key"/> and does not
    /// return until the selection is observable through the editor — the R-B1-5 discipline that
    /// closes the interleaving window between two interactions.
    /// </summary>
    private static void Select(IRenderedComponent<FluentDialogProvider> cut, string inputId, string key)
    {
        cut.InvokeAsync(() => { }).GetAwaiter().GetResult();

        var select = cut
            .FindComponents<FluentSelect<AssignmentPolicyFieldValueEditor.ValueOption>>()
            .Single(s => s.Instance.Id == inputId);

        cut.InvokeAsync(() => select.Instance.SelectedOptionChanged.InvokeAsync(
            new AssignmentPolicyFieldValueEditor.ValueOption(key, key))).GetAwaiter().GetResult();

        cut.WaitForAssertion(() => cut
            .FindComponents<FluentSelect<AssignmentPolicyFieldValueEditor.ValueOption>>()
            .Single(s => s.Instance.Id == inputId)
            .Instance.SelectedOption!.Value.Should().Be(key,
                $"the {inputId} editor must render the option the dialog stored, which proves the change was applied"));
    }

    /// <summary>Drives the number-field editor to <paramref name="value"/> and proves it applied.
    /// The editor binds its number field straight to the nullable value, so the callback is the
    /// same path the DOM input takes; the rendered field is asserted to exist by <c>Open</c>.</summary>
    private static void SetNumber(IRenderedComponent<FluentDialogProvider> cut, string inputId, int value)
    {
        cut.InvokeAsync(() => { }).GetAwaiter().GetResult();

        var editor = cut.FindComponents<AssignmentPolicyFieldValueEditor>()
            .Single(e => e.Instance.InputId == inputId);
        cut.InvokeAsync(() => editor.Instance.IntValueChanged.InvokeAsync(value)).GetAwaiter().GetResult();

        cut.WaitForAssertion(() => cut.FindComponents<AssignmentPolicyFieldValueEditor>()
            .Single(e => e.Instance.InputId == inputId)
            .Instance.IntValue.Should().Be(value, $"the {inputId} number field must render the value the dialog stored"));
    }

    [TestMethod]
    public async Task Dialog_Shows_Both_Scope_Panels_SideBySide()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId);
        var model = new AssignmentPolicyFieldEditDialog.EditModel(
            "SignatureRequirement", "Guardian signature", AssignmentPolicyFieldEditDialog.FieldKind.Mode,
            gradeId, Tenant(SignatureRequirementMode.Optional), Grade(gradeId, SignatureRequirementMode.Mandatory));

        var dialog = Open(model);

        dialog.Cut.WaitForAssertion(() => dialog.Cut.FindAll("fluent-select").Should().HaveCount(2));
        dialog.Cut.Markup.Should().Contain("Global settings", "the global-settings panel header renders");
        dialog.Cut.Markup.Should().Contain("This grade", "the per-grade panel header renders");
        dialog.Cut.Markup.Should().Contain("Not set", "the tenant default's sentinel clears the value");
        dialog.Cut.Markup.Should().Contain("Inherit global", "the grade override's sentinel restores inheritance");

        dialog.Cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Cancel").Click();
        var result = await dialog.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("cancelling closes the dialog after inspecting the panels");
    }

    // ── D8/AC7 (round assignment-rules-policy-rework): the two new policy fields ──

    /// <summary>
    /// AC7: the dialog sets the guardian-review flag (Bool kind) in BOTH scopes, and both scopes'
    /// wires carry it — a field the editor cannot write would be dead configuration.
    /// </summary>
    [TestMethod]
    public async Task Dialog_SetsGuardianReview_BoolKind_InBothScopes()
    {
        var gradeId = Guid.NewGuid();
        var handler = Register(gradeId);
        var model = new AssignmentPolicyFieldEditDialog.EditModel(
            "MandatoryReview", "Guardian review before submit", AssignmentPolicyFieldEditDialog.FieldKind.Bool,
            gradeId,
            Tenant(SignatureRequirementMode.Disabled, mandatoryReview: null),
            Grade(gradeId, mandatoryReview: null));

        var dialog = Open(model);
        Select(dialog.Cut, "apd-global", "true");
        Select(dialog.Cut, "apd-grade", "false");
        dialog.Cut.Find("form").Submit();

        var result = await dialog.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull();

        handler.Calls.Should().HaveCount(2);
        handler.Calls.Single(c => c.Url == "/api/settings/assignment-policy").Body
            .Should().Contain("\"mandatoryReview\":true", "the tenant default carries the new field");
        handler.Calls.Single(c => c.Url.Contains("grade-levels")).Body
            .Should().Contain("\"mandatoryReview\":false", "the grade override carries it too");
    }

    /// <summary>AC7: the archive window (Int kind) round-trips through the same dialog and both wires.</summary>
    [TestMethod]
    public async Task Dialog_SetsArchiveGraceDays_IntKind_InBothScopes()
    {
        var gradeId = Guid.NewGuid();
        var handler = Register(gradeId);
        var model = new AssignmentPolicyFieldEditDialog.EditModel(
            "ArchiveGraceDays", "Archive grace window (days)", AssignmentPolicyFieldEditDialog.FieldKind.Int,
            gradeId,
            Tenant(archiveGraceDays: 30),
            Grade(gradeId, archiveGraceDays: null));

        var dialog = Open(model);
        SetNumber(dialog.Cut, "apd-global", 45);
        SetNumber(dialog.Cut, "apd-grade", 14);
        dialog.Cut.Find("form").Submit();

        var result = await dialog.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull();

        handler.Calls.Should().HaveCount(2);
        handler.Calls.Single(c => c.Url == "/api/settings/assignment-policy").Body
            .Should().Contain("\"archiveGraceDays\":45", "the tenant default carries the new field");
        handler.Calls.Single(c => c.Url.Contains("grade-levels")).Body
            .Should().Contain("\"archiveGraceDays\":14", "the grade override carries it too");
    }

    [TestMethod]
    public async Task Dialog_ChangesOnlyGlobalScope_WritesSettingsEndpoint_Only()
    {
        var gradeId = Guid.NewGuid();
        var handler = Register(gradeId);
        var model = new AssignmentPolicyFieldEditDialog.EditModel(
            "SignatureRequirement", "Guardian signature", AssignmentPolicyFieldEditDialog.FieldKind.Mode,
            gradeId,
            Tenant(SignatureRequirementMode.Disabled, requiresApproval: true, maxPrimary: 2, maxCopy: 4),
            Grade(gradeId));

        var dialog = Open(model);
        Select(dialog.Cut, "apd-global", "Optional");
        dialog.Cut.Find("form").Submit();

        var result = await dialog.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull();

        handler.Calls.Should().ContainSingle();
        var put = handler.Calls.Single();
        put.Method.Should().Be("PUT");
        put.Url.Should().Be("/api/settings/assignment-policy", "only the changed global scope is written");
        put.Body.Should().Contain("\"signatureRequirement\":\"Optional\"");
        put.Body.Should().Contain("\"requiresApprovalBeforePublish\":true", "the other fields are preserved");
        put.Body.Should().Contain("\"maxPrimaryContacts\":2");
        put.Body.Should().Contain("\"maxCopyContacts\":4");
        put.Body.Should().Contain("\"mandatoryReview\":null",
            "the untouched new field rides the payload as unset");
        put.Body.Should().Contain("\"archiveGraceDays\":null");
    }

    [TestMethod]
    public async Task Dialog_ChangesOnlyGradeScope_WritesGradeEndpoint_Only()
    {
        var gradeId = Guid.NewGuid();
        var handler = Register(gradeId);
        var model = new AssignmentPolicyFieldEditDialog.EditModel(
            "RequiresApprovalBeforePublish", "Approval before publish", AssignmentPolicyFieldEditDialog.FieldKind.Bool,
            gradeId,
            Tenant(SignatureRequirementMode.Optional, requiresApproval: false, maxPrimary: 3, maxCopy: null),
            Grade(gradeId, maxPrimary: 7));

        var dialog = Open(model);
        Select(dialog.Cut, "apd-grade", "true");
        dialog.Cut.Find("form").Submit();

        var result = await dialog.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull();

        handler.Calls.Should().ContainSingle();
        var put = handler.Calls.Single();
        put.Url.Should().Be($"/students/grade-levels/{gradeId}/assignment-policy", "only the changed grade scope is written");
        put.Body.Should().Contain("\"requiresApprovalBeforePublish\":true");
        put.Body.Should().Contain("\"maxPrimaryContacts\":7", "the grade's other override is preserved");
        put.Body.Should().Contain("\"maxCopyContacts\":null");
        put.Body.Should().Contain("\"signatureRequirement\":null", "the untouched grade field stays inherit");
    }

    [TestMethod]
    public async Task Dialog_ChangesBothScopes_WritesBothEndpoints()
    {
        var gradeId = Guid.NewGuid();
        var handler = Register(gradeId);
        var model = new AssignmentPolicyFieldEditDialog.EditModel(
            "MaxPrimaryContacts", "Max primary guardian contacts per sendout",
            AssignmentPolicyFieldEditDialog.FieldKind.Int,
            gradeId,
            Tenant(maxPrimary: 2, maxCopy: 5),
            Grade(gradeId, maxPrimary: 1));

        var dialog = Open(model);
        SetNumber(dialog.Cut, "apd-global", 9);
        SetNumber(dialog.Cut, "apd-grade", 3);
        dialog.Cut.Find("form").Submit();

        var result = await dialog.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull();

        handler.Calls.Should().HaveCount(2);
        var settingsPut = handler.Calls.Single(c => c.Url == "/api/settings/assignment-policy");
        settingsPut.Body.Should().Contain("\"maxPrimaryContacts\":9");
        settingsPut.Body.Should().Contain("\"maxCopyContacts\":5", "the untouched global field is preserved");

        var gradePut = handler.Calls.Single(c => c.Url.Contains("grade-levels"));
        gradePut.Body.Should().Contain("\"maxPrimaryContacts\":3");
    }

    [TestMethod]
    public async Task Dialog_ClearingTheGradeOverride_WritesNullForThatFieldOnly()
    {
        var gradeId = Guid.NewGuid();
        var handler = Register(gradeId);
        var model = new AssignmentPolicyFieldEditDialog.EditModel(
            "SignatureRequirement", "Guardian signature", AssignmentPolicyFieldEditDialog.FieldKind.Mode,
            gradeId,
            Tenant(),
            Grade(gradeId, SignatureRequirementMode.Mandatory, maxPrimary: 2));

        var dialog = Open(model);
        // The empty-key sentinel = "Inherit global" on the grade side ⇒ null on the wire.
        Select(dialog.Cut, "apd-grade", string.Empty);
        dialog.Cut.Find("form").Submit();

        var result = await dialog.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull();

        handler.Calls.Should().ContainSingle();
        var put = handler.Calls.Single();
        put.Url.Should().Be($"/students/grade-levels/{gradeId}/assignment-policy");
        put.Body.Should().Contain("\"signatureRequirement\":null");
        put.Body.Should().Contain("\"maxPrimaryContacts\":2", "the other grade overrides are preserved");
    }

    [TestMethod]
    public async Task Dialog_CleanOpen_RendersNoErrorBar()
    {
        // Regression guard (defect found 2026-10-01, post-B1). DialogShellFooter renders a danger
        // FluentMessageBar whenever its string? Error parameter is non-empty. Because a bare value on
        // a STRING parameter is a string literal, `Error="Error"` passed the text "Error" and painted
        // a permanent red bar reading "Error" on every open — success or failure. The fix binds the
        // base property (`Error="@Error"`), so a clean open must render NO error-intent bar.
        var gradeId = Guid.NewGuid();
        Register(gradeId);
        var model = new AssignmentPolicyFieldEditDialog.EditModel(
            "SignatureRequirement", "Guardian signature",
            AssignmentPolicyFieldEditDialog.FieldKind.Mode,
            gradeId,
            Tenant(signature: SignatureRequirementMode.Optional),
            Grade(gradeId));

        var dialog = Open(model);

        dialog.Cut.FindComponents<FluentMessageBar>()
            .Where(b => b.Instance.Intent == MessageIntent.Error)
            .Should().BeEmpty(
                "a clean open must render no error bar — one reading 'Error' means the footer's Error " +
                "parameter was passed as a string literal instead of '@Error'");

        // The dialog must still be fully usable.
        dialog.Cut.Find("form").Submit();
        var result = await dialog.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Dialog_NoChanges_WritesNothing()
    {
        var gradeId = Guid.NewGuid();
        var handler = Register(gradeId);
        var model = new AssignmentPolicyFieldEditDialog.EditModel(
            "MaxCopyContacts", "Max copy guardian contacts per sendout",
            AssignmentPolicyFieldEditDialog.FieldKind.Int,
            gradeId,
            Tenant(maxCopy: 4),
            Grade(gradeId, maxCopy: 2));

        var dialog = Open(model);
        dialog.Cut.Find("form").Submit();

        var result = await dialog.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull();
        handler.Calls.Should().BeEmpty(
            "opening the dialog and saving without edits must not create or mutate a policy");
    }

    [TestMethod]
    public async Task Dialog_SavingAValueEqualToOneAlreadyStored_WritesNothing()
    {
        var gradeId = Guid.NewGuid();
        var handler = Register(gradeId);
        var model = new AssignmentPolicyFieldEditDialog.EditModel(
            "RequiresApprovalBeforePublish", "Approval before publish", AssignmentPolicyFieldEditDialog.FieldKind.Bool,
            gradeId,
            Tenant(requiresApproval: true),
            Grade(gradeId));

        var dialog = Open(model);
        // Re-select the value the global scope already holds: the dialog compares against the
        // initial value, so this is a no-change save rather than a redundant write.
        Select(dialog.Cut, "apd-global", "true");
        dialog.Cut.Find("form").Submit();

        var result = await dialog.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull();
        handler.Calls.Should().BeEmpty("re-selecting the stored value is not a change");
    }
}
