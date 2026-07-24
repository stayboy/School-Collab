using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level regression tests for the <c>FEATURE:EnableGradeLevelSetupOnEnrollDialog</c>
/// feature flag and its integration with <c>EnrollStudentDialog</c>.
///
/// The "+" inline-create-grade button on the Enroll Student dialog has a
/// global side-effect: it creates a new GRADE coded value + a matching
/// <c>GradeLevel</c> row, both of which are shared across tenants. The
/// flag is therefore <b>opt-in</b> (default false) and only flips on
/// after an explicit ConfigFlags toggle. These tests pin the
/// registration + gating contract so a future regression (e.g.
/// accidentally re-introducing the button as always-on, or renaming
/// the flag and breaking the ConfigFlags landing page link) is caught
/// by source-level scan.
///
/// The button is also gated by the <b>new-enrollment check</b>
/// (<c>IsNewEnrollment</c>) — a derived boolean driven by
/// <c>Model.SuggestedGradeLevelId is null</c>. The Detail.razor /
/// Edit.razor call sites pass the student's current active-enrollment
/// grade as that field, so null means "the student has no active
/// enrollment" (i.e. this is a new-enrollment scenario). The
/// new-enrollment check is the validation that "setting up a grade
/// level inside the enrollment dialog" is appropriate for the
/// current scenario: for a re-enrollment the button is hidden
/// because the right action is "pick a different existing grade"
/// rather than "stand a brand-new grade up mid-flow". The two gates
/// are ANDed because they cover different concerns: the
/// new-enrollment check covers "is this the right time to offer
/// grade setup" (UX correctness); the feature flag covers "is the
/// tenant authorized to use inline grade setup at all" (governance).
/// Either gate being false hides the button.
///
/// The dialog intentionally does NOT special-case the empty-grade
/// list — there is no blocking warning, no setup CTA inside the form,
/// and no Disabled / Placeholder on the grade dropdown. The server's
/// enrollment API is the source of truth for whether the chosen
/// grade is valid; bouncing the user out of the dialog to fix a
/// config gap was higher friction than the warning was worth. The
/// setup-grade flow is the user's responsibility — they either flip
/// the flag on to reveal the + button inline (for the new-enrollment
/// case) or use the wizard / grade-levels landing page directly.
///
/// The period is the tenant's CURRENT GLOBAL period (Status == "Active"),
/// not a user-pickable dropdown. The server's <c>EnrollStudentHandler</c>
/// enforces this via <c>IActivePeriodProvider.GetActivePeriodAsync()</c>
/// and rejects any enrollment whose PeriodId does not match. The
/// dialog surfaces the active period as a read-only "Period" row
/// (FormRow + FluentTextField ReadOnly) so the user can see what
/// the enrollment is going against, and submits with that period's
/// id. There is no period dropdown because there is no choice to
/// make.
///
/// The grade is picked via the shared <c>CodedValueDropdown</c>
/// component (Parent = Grades), which loads the tenant-resolved
/// CodedValue list from the Settings service and shows the
/// per-tenant override name. The selected CodedValueId is then
/// resolved to a <c>GradeLevelDto.Id</c> via the loaded
/// <c>_gradeLevels</c> list (matched on CodedValueId) for the
/// actual EnrollStudentAsync call. This replaces the previous
/// <c>FluentSelect Items="_gradeLevels" OptionText="g => g.Name"</c>
/// which displayed the stale mirrored <c>GradeLevelDto.Name</c> and
/// ignored any tenant override.
///
/// The form follows the canonical school-collab FormRow layout
/// (180px label cell + flex:1 input cell + gap 12px; see
/// <c>FormRow.razor</c> for the full pattern).
///
/// What these tests guard against:
///   - The <c>+</c> button being visible by default (it must be hidden
///     behind BOTH the new-enrollment check AND the feature flag)
///   - The dialog adding a blocking "no grades available" warning
///     back into the markup (regression of the empty-list check)
///   - The dialog adding a "Set up first grade" CTA inside the form
///     (regression of the empty-list check)
///   - The grade dropdown falling back to <c>FluentSelect</c> with
///     the stale mirrored name (regression of the "load coded
///     values" fix)
///   - The period being a user-pickable dropdown (regression of
///     the "always pick the current global period" fix)
///   - The form fields losing the FormRow pattern (regression of
///     the layout fix)
///   - The inline-create-grade flow losing the per-tenant Name
///     override (i.e. the <c>cvResult.Name</c> → <c>GetOrCreateGradeLevelAsync</c>
///     passthrough)
///   - The flag name drifting away from the
///     <c>FEATURE:EnableGradeLevelSetupOnEnrollDialog</c> key the
///     ConfigFlags landing page references
///   - The flag being added to one place (UI) but not the others
///     (appsettings.json + migration service) — all three must be in
///     lock-step for the flag to be discoverable and seedable
/// </summary>
[TestClass]
public class EnrollStudentDialogFeatureFlagTests
{
    private const string DialogPath = "src/Students/SchoolCollab.Students.Admin/Components/Students/EnrollStudentDialog.razor";
    private const string DialogCssPath = "src/Students/SchoolCollab.Students.Admin/Components/Students/EnrollStudentDialog.razor.css";
    private const string AppSettingsPath = "src/SchoolCollab.Admin/appsettings.json";
    private const string MigrationServicePath = "src/SchoolCollab.MigrationService/Program.cs";
    private const string ApiClientPath = "src/Students/SchoolCollab.Students.Admin/Services/StudentsApiClient.cs";
    private const string EnrollmentRoutesPath = "src/Students/SchoolCollab.Students.Api/Endpoints/EnrollmentRoutes.cs";
    private const string ExpectedFlagKey = FeatureFlagKeys.EnableGradeLevelSetupOnEnrollDialog;

    /// <summary>
    /// Reads a source file from the repo root. The path constants above
    /// are repo-relative, so we walk up 5 levels from the test assembly
    /// output directory (bin/Debug/net10.0) to land on the repo root.
    /// </summary>
    private static string Load(string repoRelativePath)
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir, "..", "..", "..", "..", "..", repoRelativePath));
        File.Exists(srcPath).Should().BeTrue(
            $"{repoRelativePath} should exist at '{srcPath}' — check the path resolution");
        return File.ReadAllText(srcPath);
    }

    [TestMethod]
    public void EnrollDialog_Plus_Button_Is_Gated_By_FeatureFlag()
    {
        // The "+" inline-create-grade button must be wrapped in a
        // <FeatureFlagGate Key="FEATURE:EnableGradeLevelSetupOnEnrollDialog">.
        // If the gate is missing the button is always-on and a future
        // ConfigFlags toggle would have no effect on it.
        var src = Load(DialogPath);
        src.Should().Contain("<FeatureFlagGate",
            "the inline-create-grade UI on EnrollStudentDialog MUST be wrapped in <FeatureFlagGate> so the ConfigFlags landing page can toggle it");
        src.Should().Contain($"Key=\"@FeatureFlagKeys.EnableGradeLevelSetupOnEnrollDialog\"",
            $"the <FeatureFlagGate> on EnrollStudentDialog MUST key off '{ExpectedFlagKey}' so it ties to the runtime flag seeded by the migration service");
        src.Should().Contain("OnAddNewGradeAsync",
            "the OnAddNewGradeAsync handler (which opens the CodedValueDialog + materializes a GradeLevel) must be wired up — confirms the gated button is the actual create-grade entry point, not an orphan");
    }

    [TestMethod]
    public void EnrollDialog_Plus_Button_Is_Gated_By_IsNewEnrollment_And_FeatureFlag()
    {
        // The "+" inline-create-grade button is gated by BOTH the
        // new-enrollment check (UX correctness) AND the feature flag
        // (governance), ANDed together. The markup must wrap the
        // <FeatureFlagGate> in an `@if (IsNewEnrollment)` block so the
        // button is hidden for re-enrollments even if the tenant has
        // opted in to the flag.
        //
        // The new-enrollment check is the validation that "setting up
        // a grade level inside the enrollment dialog" is appropriate
        // for the current scenario — for a re-enrollment (the student
        // is already enrolled in a grade) the button is hidden because
        // standing up a brand-new grade mid-flow is the wrong action
        // (the user should pick a different existing grade instead).
        var src = Load(DialogPath);
        // The @if (IsNewEnrollment) must wrap the FeatureFlagGate
        // containing the + button. We assert both via substring
        // presence + a positional check: the @if opens before the
        // gate, and the closing brace of the @if comes after the
        // gate's closing tag.
        var ifIdx = src.IndexOf("@if (IsNewEnrollment)", StringComparison.Ordinal);
        var gateIdx = src.IndexOf($"<FeatureFlagGate Key=\"@FeatureFlagKeys.EnableGradeLevelSetupOnEnrollDialog\"", StringComparison.Ordinal);
        ifIdx.Should().BeGreaterThan(0,
            "the dialog MUST wrap the + button in `@if (IsNewEnrollment)` so the inline grade-setup path is only offered for the new-enrollment case");
        gateIdx.Should().BeGreaterThan(ifIdx,
            "the @if (IsNewEnrollment) MUST appear BEFORE the <FeatureFlagGate> so the new-enrollment check is the outer gate (the FeatureFlagGate is the inner one)");
        var gateCloseIdx = src.IndexOf("</FeatureFlagGate>", gateIdx, StringComparison.Ordinal);
        // After the gate's closing tag, the @if block must eventually
        // close. Walk forward looking for the next `}` at column 0
        // (start of line) — a loose but adequate signal that the @if
        // body has ended.
        var afterGate = src.Substring(gateCloseIdx);
        var nextCloseBrace = afterGate.IndexOf("\n}", StringComparison.Ordinal);
        nextCloseBrace.Should().BeGreaterThan(0,
            "the @if (IsNewEnrollment) block must close AFTER the </FeatureFlagGate> so the new-enrollment check wraps the feature flag");
    }

    [TestMethod]
    public void EnrollDialog_Plus_Button_Is_The_Only_Child_Of_The_Gate()
    {
        // Pin the markup shape: the gate wraps exactly the + button, not
        // the whole grade-level row. A regression that accidentally gates
        // the dropdown too (or the entire dialog) would break the common
        // case (picking an existing grade).
        var src = Load(DialogPath);
        // Look for the gate opening tag, then the immediate next element
        // (a <FluentButton>), then the gate closing tag. This is a loose
        // shape check — tolerant of whitespace/blank lines.
        var gateOpenIdx = src.IndexOf($"<FeatureFlagGate Key=\"@FeatureFlagKeys.EnableGradeLevelSetupOnEnrollDialog\"", StringComparison.Ordinal);
        gateOpenIdx.Should().BeGreaterThan(0, "the FeatureFlagGate opening tag must exist on the dialog");
        var buttonIdx = src.IndexOf("<FluentButton", gateOpenIdx, StringComparison.Ordinal);
        buttonIdx.Should().BeGreaterThan(gateOpenIdx, "the next <FluentButton> after the gate opening must be the + button");
        var gateCloseIdx = src.IndexOf("</FeatureFlagGate>", buttonIdx, StringComparison.Ordinal);
        gateCloseIdx.Should().BeGreaterThan(buttonIdx, "the </FeatureFlagGate> closing tag must come after the + button");
    }

    [TestMethod]
    public void FeatureFlag_Is_Registered_In_AppSettings_With_Default_Off()
    {
        // The flag must be in appsettings.json with a default of "false"
        // (opt-in). If a future change flips the default to "true" by
        // accident, every tenant would see the + button immediately on
        // startup without an explicit ConfigFlags toggle — a noisy
        // regression for an inline-create-grade action that creates
        // global state.
        var settings = Load(AppSettingsPath);
        settings.Should().Contain("\"EnableGradeLevelSetupOnEnrollDialog\"",
            "the feature flag MUST be registered in appsettings.json so the Admin host can resolve it via IConfiguration (the ConfigFeatureFlagService reads it from here on cold-start before the cached Settings client warms up)");
        // The default must be "false" — the flag is opt-in. A "true"
        // default would surprise tenants by surfacing inline-grade-
        // creation on first run.
        var flagLine = settings
            .Split('\n')
            .FirstOrDefault(line => line.Contains("EnableGradeLevelSetupOnEnrollDialog"));
        flagLine.Should().NotBeNull("the flag must be present in appsettings.json");
        flagLine!.Should().Contain("\"false\"",
            "the flag default MUST be \"false\" — inline-grade-create is opt-in to prevent surprise global side-effects on tenants that have not configured grade levels yet");
    }

    [TestMethod]
    public void FeatureFlag_Is_Seeded_By_MigrationService_As_Default_Off()
    {
        // The migration service must seed the flag with IsEnabled=false
        // so a fresh deployment starts with the button hidden. The seed
        // must be idempotent (skip-if-exists) so re-running the
        // migrator does not violate ix_feature_flags_key_unique.
        var src = Load(MigrationServicePath);
        src.Should().Contain("SeedEnableGradeLevelSetupOnEnrollDialogAsync",
            "the migration service MUST define a SeedEnableGradeLevelSetupOnEnrollDialogAsync method to idempotently seed the flag on first deploy");
        src.Should().Contain("await SeedEnableGradeLevelSetupOnEnrollDialogAsync(settingsDb, logger);",
            "the seed call MUST be invoked from the Settings migration block alongside the other runtime flag seeds so a fresh deploy actually persists the row");
        // The seed body must create the flag with IsEnabled=false (opt-in).
        var seedBodyIdx = src.IndexOf("SeedEnableGradeLevelSetupOnEnrollDialogAsync", StringComparison.Ordinal);
        seedBodyIdx.Should().BeGreaterThan(0);
        var afterSeedHeader = src.Substring(seedBodyIdx);
        // Look for the IsEnabled: false argument on the FeatureFlag.Create call
        // in the seed body. Tolerant of line wrapping / whitespace.
        afterSeedHeader.Should().Contain("isEnabled: false",
            "the seed MUST create the flag with isEnabled: false (opt-in) — flipping the default would surprise tenants on first deploy");
    }

    [TestMethod]
    public void FeatureFlag_Key_Matches_Across_All_Three_Registration_Sites()
    {
        // The flag key must be the EXACT same string in:
        //   1. <FeatureFlagGate Key="..."> in the dialog
        //   2. appsettings.json (under FeatureFlags.FEATURE.<key>)
        //   3. FeatureFlag.NormalizeKey call in the migration seed
        // If any one of them drifts, the ConfigFlags landing page would
        // flip a flag that has no UI consumer, or the UI would be gated
        // by a key that no seed ever persisted.
        var dialog = Load(DialogPath);
        var settings = Load(AppSettingsPath);
        var migration = Load(MigrationServicePath);

        // Strip the FEATURE: prefix for the appsettings check (the JSON
        // nests under "FEATURE": { "<Area>": { "...": "true" } }).
        var area = ExpectedFlagKey["FEATURE:".Length..];

        dialog.Should().Contain("@FeatureFlagKeys.EnableGradeLevelSetupOnEnrollDialog", "the <FeatureFlagGate> in the dialog must bind to the FeatureFlagKeys constant");
        settings.Should().Contain($"\"{area}\"",
            $"appsettings.json must register the flag under FeatureFlags.FEATURE.{area}");
        migration.Should().Contain("FeatureFlag.NormalizeKey(FeatureFlagKeys.EnableGradeLevelSetupOnEnrollDialog)",
            "the migration seed must call FeatureFlag.NormalizeKey with the FeatureFlagKeys constant so the canonical key matches what the UI + ConfigFlags landing page resolve");
    }

    // ── No empty-list check logic (the "we don't need the check" fix) ──────
    //
    // The dialog intentionally does not special-case the empty-grade
    // list. There is no blocking warning, no setup CTA inside the form,
    // and no Disabled / Placeholder on the grade FluentSelect. The
    // dialog's job is to collect a well-formed enrollment; whether
    // the tenant has any grade levels at all is a configuration
    // concern that lives outside this dialog, and the server-side
    // validation is the final authority on whether the chosen grade
    // is valid. These tests pin the "no empty-list check" contract
    // so a regression that re-introduces the blocking warning or the
    // setup CTA is caught by source-level scan.

    [TestMethod]
    public void EnrollDialog_Does_Not_Have_An_Empty_Grade_Warning_Branch()
    {
        // The dialog must not contain a `else if (_gradeLevels.Length == 0
        // && ...)` blocking warning branch. Pin that no such branch
        // exists in the markup. The dialog renders the form directly
        // for any grade list state (populated or empty) — the server's
        // enrollment API is the source of truth for grade validity.
        var src = Load(DialogPath);
        // The re-enrollment empty-grade branch (the "data-integrity"
        // warning) must not exist.
        src.Should().NotContain("else if (_gradeLevels.Length == 0 && !IsNewEnrollment)",
            "the dialog MUST NOT have a re-enrollment empty-grade blocking warning branch — the missing-grade situation for an active enrollment is a data-integrity concern, not a dialog-level UX concern; bouncing the user out of the dialog to fix it is more friction than the warning is worth");
        // The flag-off empty-grade branch (the original behavior) must
        // not exist either.
        src.Should().NotContain("else if (_gradeLevels.Length == 0 && !_gradeSetupEnabled)",
            "the dialog MUST NOT have a flag-off empty-grade blocking warning branch — the dialog intentionally does not special-case the empty-grade list, regardless of the feature-flag state");
        // The "No grade levels are configured yet" warning copy itself
        // must not be in the dialog (was the user-facing text for both
        // empty-grade warning branches).
        src.Should().NotContain("No grade levels are configured yet — create one before enrolling.",
            "the dialog MUST NOT have a 'No grade levels are configured yet' warning copy — that is the empty-list warning wording that was removed");
        // The dialog DOES inject IFeatureFlagService and keep a _gradeSetupEnabled
        // field — but ONLY to drive the SubmitAsync auto-materialize decision
        // (when the flag is ON and the picked coded value has no GradeLevel row,
        // the dialog sets the grade level up by itself via GetOrCreateGradeLevelAsync
        // instead of erroring). The FeatureFlagGate still owns the reactive
        // +/Override button rendering; this code-path resolution is the supported
        // way to branch a submit-time path on the flag (the gate can't).
        src.Should().Contain("_gradeSetupEnabled",
            "the dialog MUST keep a _gradeSetupEnabled field resolved in OnInitializedAsync — SubmitAsync's auto-materialize-vs-error branch is driven by the flag state in code");
        src.Should().Contain("IFeatureFlagService",
            "the dialog MUST inject IFeatureFlagService to resolve the flag for the SubmitAsync auto-materialize decision (the FeatureFlagGate can't branch a submit-time path)");
        src.Should().Contain("FeatureFlags.IsEnabledAsync",
            "the dialog MUST resolve the flag via IFeatureFlagService.IsEnabledAsync in OnInitializedAsync so SubmitAsync can branch on _gradeSetupEnabled");
    }

    [TestMethod]
    public void EnrollDialog_AutoMaterializesGradeLevel_WhenFlagOn_Errors_WhenFlagOff()
    {
        // When the picked coded value has no matching GradeLevel row, SubmitAsync
        // MUST branch on the flag:
        //   - Flag ON (_gradeSetupEnabled): auto-materialize the GradeLevel via
        //     GetOrCreateGradeLevelAsync (the dialog sets the grade level up BY
        //     ITSELF) and proceed — NO error.
        //   - Flag OFF (!_gradeSetupEnabled): surface an actionable error telling
        //     the user to enable the feature flag (so the dialog can set the grade
        //     up inline) or set it up on the Grade Levels page.
        // The "not set up as a grade level" error MUST be inside the
        // !_gradeSetupEnabled branch (shows ONLY when the flag blocks inline
        // setup); the GetOrCreateGradeLevelAsync call MUST be inside the
        // _gradeSetupEnabled branch.
        var src = Load(DialogPath);

        // The flag-off error is gated by !_gradeSetupEnabled.
        src.Should().Contain("if (!_gradeSetupEnabled)",
            "the 'not set up as a grade level' error MUST be gated by !_gradeSetupEnabled — it shows ONLY when the flag blocks inline grade-level setup");
        src.Should().Contain("Enable the grade-level setup feature flag",
            "the flag-off error MUST tell the user to enable the feature flag (so the dialog can set the grade up inline) or use the Grade Levels page");

        // The flag-on auto-materialize path calls GetOrCreateGradeLevelAsync.
        src.Should().Contain("GetOrCreateGradeLevelAsync",
            "the flag-on branch MUST auto-materialize the GradeLevel via GetOrCreateGradeLevelAsync so the dialog sets the grade level up by itself instead of erroring");
    }

    [TestMethod]
    public void EnrollDialog_Has_OverrideNameButton_GatedByFlagAndSelectedGrade()
    {
        // The "Override name" button (renames the selected grade per-tenant,
        // mirroring the GradeLevelWizard's Override Name action) MUST render when
        // a grade is selected AND the flag is on (FeatureFlagGate). It opens
        // CodedValueDialog in Override mode and refreshes the dropdown on success.
        // It is NOT gated by IsNewEnrollment (renaming an existing grade is valid
        // for both new + re-enrollment, unlike the + create button).
        var src = Load(DialogPath);

        src.Should().Contain("OnOverrideGradeNameAsync",
            "the dialog MUST have an OnOverrideGradeNameAsync handler that opens CodedValueDialog in Override mode for the selected grade");
        src.Should().Contain("CodedValueFormModel.ForOverride",
            "the override handler MUST open CodedValueDialog via CodedValueFormModel.ForOverride (Override mode)");
        src.Should().Contain("enroll-grade-override",
            "the Override name button MUST carry the .enroll-grade-override class so it can be styled + tested");
        src.Should().Contain("_selectedGradeCodedValueId is not null",
            "the Override button MUST be gated by a selected grade (_selectedGradeCodedValueId is not null) — it renames the SELECTED grade, so it only shows once one is picked");
        // The override button uses the same Edit icon the GradeLevelWizard uses
        // for its "Override the default name" action, for cross-page consistency.
        src.Should().Contain("FluentIcons.Edit",
            "the Override button MUST use FluentIcons.Edit (same icon as the GradeLevelWizard's override-name action)");
    }

    [TestMethod]
    public void EnrollDialog_Does_Not_Have_A_Setup_First_Grade_CTA()
    {
        // The dialog must not have the "Set up first grade" CTA inside
        // the form (the Info MessageBar that used to render when grades
        // is empty AND the flag is on). The setup flow is exclusively
        // via the + button next to the grade dropdown.
        var src = Load(DialogPath);
        src.Should().NotContain("enroll-grade-setup-banner",
            "the dialog MUST NOT have a .enroll-grade-setup-banner (Info MessageBar) — that was the empty-list setup CTA, which is removed; the setup flow is exclusively via the + button");
        // The "Set up first grade" label must not be on an actual
        // <FluentButton> — i.e. the button rendered to the user must
        // not have that text. The string can still appear in
        // historical doc comments referencing what was removed.
        // Pin by counting <FluentButton>...</FluentButton> blocks
        // that contain the label: there must be zero.
        var buttonBlocks = System.Text.RegularExpressions.Regex.Matches(
            src, @"<FluentButton\b[^>]*>.*?</FluentButton>", System.Text.RegularExpressions.RegexOptions.Singleline);
        var anySetupButton = buttonBlocks.Cast<System.Text.RegularExpressions.Match>()
            .Any(m => m.Value.Contains("Set up first grade"));
        anySetupButton.Should().BeFalse(
            "no <FluentButton> in the dialog may render the 'Set up first grade' label — that was the empty-list setup CTA button, which is removed; the + button (with the Add icon, not text) is the only inline-create-grade entry point");
    }

    [TestMethod]
    public void EnrollDialog_Grade_Select_Has_No_Disabled_Or_Placeholder_When_Empty()
    {
        // The grade <CodedValueDropdown> must not be disabled or show a
        // "No grades yet" placeholder when the list is empty — the
        // dialog intentionally does not special-case the empty-grade
        // list. A disabled-with-placeholder state would be a regression
        // of the empty-list check.
        var src = Load(DialogPath);
        src.Should().NotContain("Disabled=\"@(_gradeLevels.Length == 0)\"",
            "the grade dropdown MUST NOT be disabled when the list is empty — the dialog does not special-case the empty-grade list; a disabled dropdown is a regression of the empty-list check");
        src.Should().NotContain("Placeholder=\"@(_gradeLevels.Length == 0",
            "the grade dropdown MUST NOT have an empty-list placeholder — the dialog does not special-case the empty-grade list; the placeholder would be a regression of the empty-list check");
    }

    // ── New-enrollment check (the validation for "set up gradelevel in
    // the enrollment dialog") ──────
    //
    // The IsNewEnrollment computed property is the validation that
    // "setting up a grade level inside the enrollment dialog" is
    // appropriate for the current scenario. The + button is gated by
    // IsNewEnrollment (and the feature flag), so the property must
    // exist and must be driven by Model.SuggestedGradeLevelId.

    [TestMethod]
    public void EnrollDialog_Has_IsNewEnrollment_Computed_Property()
    {
        // The dialog must compute IsNewEnrollment from the model so
        // the + button can be gated on the new-enrollment check. The
        // check is driven by Model.SuggestedGradeLevelId is null (the
        // call sites pass the student's current active-enrollment
        // grade; null means the student has no active enrollment).
        var src = Load(DialogPath);
        src.Should().Contain("IsNewEnrollment",
            "the dialog MUST compute IsNewEnrollment from Model.SuggestedGradeLevelId so the + button can be gated on the new-enrollment check (the validation for 'set up a grade level inside the enrollment dialog')");
        src.Should().Contain("Model.SuggestedGradeLevelId is null",
            "IsNewEnrollment MUST be true when Model.SuggestedGradeLevelId is null — that is the signal the Detail.razor / Edit.razor call sites use to mean 'student has no active enrollment'");
    }

    [TestMethod]
    public void EnrollDialog_Inline_Grade_Setup_Allows_Per_Tenant_Name_Override()
    {
        // The inline-create-grade flow (OnAddNewGradeAsync) must pass
        // the user-entered Name through to GetOrCreateGradeLevelAsync
        // so the tenant can override the default name. The CodedValueDialog
        // in Create mode collects Code, Name, DisplayOrder, Description
        // — the Name field is the per-tenant override name. The
        // collected Name must be threaded through to the
        // GradeLevelDto materialization so the tenant's choice is
        // persisted and reflected in the dropdown refresh.
        var src = Load(DialogPath);
        // Look for the GetOrCreateGradeLevelAsync call site in the
        // OnAddNewGradeAsync handler. It must pass cvResult.Name as
        // one of the positional args (the per-tenant name).
        src.Should().Contain("GetOrCreateGradeLevelAsync",
            "the OnAddNewGradeAsync handler MUST call GetOrCreateGradeLevelAsync to materialize a GradeLevelDto from the freshly created CodedValueDto");
        src.Should().Contain("cvResult.Name",
            "the OnAddNewGradeAsync handler MUST pass cvResult.Name to GetOrCreateGradeLevelAsync so the per-tenant override name is persisted on the new GradeLevel row");
        // The handler must also refresh the dropdown list from the
        // server (rather than appending in place) so the resolved
        // tenant name is reflected immediately.
        src.Should().Contain("ListGradeLevelsAsync()",
            "the OnAddNewGradeAsync handler MUST refresh the grade list from the server (ListGradeLevelsAsync) so the resolved per-tenant name is reflected in the dropdown immediately");
        // The handler must refresh the CodedValueDropdown via its
        // RefreshAsync method (rather than reloading the page) so the
        // newly created GRADE coded value appears in the dropdown.
        src.Should().Contain("RefreshAsync()",
            "the OnAddNewGradeAsync handler MUST call CodedValueDropdown.RefreshAsync() to reload the dropdown after a successful create so the new entry appears without a page reload");
    }

    // ── Period is always the current global period (no user-pickable
    // dropdown) ──────
    //
    // The period is derived client-side from ListPeriodsAsync filtered
    // by Status == "Active" and submitted as the active period's id.
    // The server's EnrollStudentHandler also enforces this via
    // IActivePeriodProvider.GetActivePeriodAsync() and rejects any
    // enrollment whose PeriodId does not match. There is no
    // period <FluentSelect> because there is no choice to make.
    // A regression that re-introduces the period dropdown would
    // let the user pick a value the server is going to reject.

    [TestMethod]
    public void EnrollDialog_Does_Not_Have_A_Period_FluentSelect()
    {
        // The dialog must not have a <FluentSelect TOption="PeriodDto">
        // (the old user-pickable period dropdown). The period is now
        // a read-only display row showing the active period's name
        // and date range. A regression that re-introduces the period
        // dropdown would let the user pick a value the server's
        // EnrollStudentHandler is going to reject.
        var src = Load(DialogPath);
        src.Should().NotContain("TOption=\"PeriodDto\"",
            "the dialog MUST NOT have a <FluentSelect TOption=\"PeriodDto\"> — the period is the tenant's current global period (Status == Active), not a user-pickable value; the server's EnrollStudentHandler rejects any enrollment whose PeriodId does not match the active one");
        // Also pin the absence of the old `Items="@_periods"` shape
        // (was the binding source for the old dropdown).
        src.Should().NotContain("Items=\"@_periods\"",
            "the dialog MUST NOT bind a period FluentSelect to _periods — the period is a single derived value (_activePeriod), not a list");
    }

    [TestMethod]
    public void EnrollDialog_Derives_ActivePeriod_From_ListPeriodsAsync()
    {
        // The dialog must derive the active period from
        // ListPeriodsAsync (filtered by Status == "Active") and use
        // that as the submitted period. The same approach
        // ActiveTermToolbar uses for the toolbar's "current period"
        // link. The server's IActivePeriodProvider.GetActivePeriodAsync()
        // is the authoritative implementation; the dialog just mirrors
        // it client-side so it can show the period name + dates in
        // the read-only "Period" row.
        var src = Load(DialogPath);
        src.Should().Contain("ListPeriodsAsync",
            "the dialog MUST call ListPeriodsAsync to derive the current global period (Status == Active)");
        src.Should().Contain("Status",
            "the dialog MUST filter periods by Status when deriving the active one (Status == \"Active\")");
        src.Should().Contain("_activePeriod",
            "the dialog MUST hold the derived active period in a _activePeriod field that the submit + read-only display consume");
        // The submit must use _activePeriod.Id (NOT a user-picked
        // _selectedPeriod.Id) because there is no picker.
        src.Should().Contain("_activePeriod.Id",
            "the EnrollStudentAsync submit MUST use _activePeriod.Id (not a _selectedPeriod.Id) because the period is server-derived, not user-picked");
        // The _activePeriod field is null when there is no active
        // period for the tenant. The dialog must surface that as a
        // warning (the form is not rendered in that case).
        src.Should().Contain("_activePeriod is null",
            "the dialog MUST have a guard that handles the no-active-period case (a hard block — the user has nothing valid to enroll against)");
        src.Should().Contain("No active academic period",
            "the no-active-period warning copy MUST explain the user must open a period first (linking to the periods page)");
    }

    [TestMethod]
    public void EnrollDialog_Shows_Active_Period_As_ReadOnly_FormRow()
    {
        // The "Period" FormRow must be read-only — the user cannot
        // pick a different period because the server is going to
        // reject anything but the active one. The row follows the
        // FormRow pattern (Label=\"Period\") so its label lines up
        // with the other fields' labels down the form. The input
        // is a read-only FluentTextField showing the active period
        // in the canonical academic-year format ("StartDate.Year/EndDate.Year",
        // e.g. "2025/2026") and exposes the full name + date range
        // as a native title= tooltip for hover context.
        var src = Load(DialogPath);
        // The FormRow for "Period" must exist.
        src.Should().Contain("<FormRow Label=\"Period\"",
            "the dialog MUST render the active period as a <FormRow Label=\"Period\"> so its label lines up with the other fields' labels down the form");
        // The input must be read-only.
        var periodFormRowIdx = src.IndexOf("<FormRow Label=\"Period\"", StringComparison.Ordinal);
        periodFormRowIdx.Should().BeGreaterThan(0);
        var afterPeriodRow = src.Substring(periodFormRowIdx);
        afterPeriodRow.Should().Contain("ReadOnly=\"true\"",
            "the period FormRow's input MUST be ReadOnly=true (the user cannot pick a different period)");
        afterPeriodRow.Should().Contain("ActivePeriodText",
            "the period FormRow's input MUST show the derived ActivePeriodText (the academic-year token, e.g. \"2025/2026\") so the user can see what the enrollment is going against");
        afterPeriodRow.Should().Contain("ActivePeriodTooltip",
            "the period FormRow's input MUST expose the full period name + date range as a native title= tooltip (ActivePeriodTooltip) so the user can hover for the complete context without the row overflowing");
    }

    // ── Grade dropdown loads the CodedValue (not the stale mirror) ──────
    //
    // The grade is picked via the shared <CodedValueDropdown
    // Parent="CodedValueParent.Grades"> component, which loads the
    // tenant-resolved CodedValue list from the Settings service. This
    // replaces the previous <FluentSelect Items="_gradeLevels"
    // OptionText="g => g.Name"> which displayed the stale
    // GradeLevelDto.Name mirror and ignored any tenant override.

    [TestMethod]
    public void EnrollDialog_Grade_Uses_CodedValueDropdown_Not_FluentSelect()
    {
        // The grade picker MUST be a <CodedValueDropdown
        // Parent="CodedValueParent.Grades">, NOT a <FluentSelect> with
        // GradeLevelDto items. The CodedValueDropdown loads the
        // tenant-resolved CodedValue list (with per-tenant override
        // name) from the Settings service; the FluentSelect + GradeLevelDto
        // approach displayed a stale mirror and ignored overrides.
        var src = Load(DialogPath);
        src.Should().Contain("CodedValueDropdown",
            "the grade picker MUST be a <CodedValueDropdown> — the FluentSelect + GradeLevelDto approach displayed the stale mirrored name and ignored tenant overrides");
        src.Should().Contain("CodedValueParent.Grades",
            "the <CodedValueDropdown> MUST key off CodedValueParent.Grades so it loads the GRADE coded values, not some other category");
        // The old FluentSelect with GradeLevelDto must be gone.
        src.Should().NotContain("TOption=\"GradeLevelDto\"",
            "the dialog MUST NOT have a <FluentSelect TOption=\"GradeLevelDto\"> — the grade is now picked from the CodedValue list, not the GradeLevelDto list; a FluentSelect with GradeLevelDto would display the stale mirrored name");
        // The CodedValueDropdown must be refreshable from the + button
        // (it has a @ref we can call RefreshAsync on).
        src.Should().Contain("_gradeCodedValueDropdown",
            "the dialog MUST hold a reference to the grade <CodedValueDropdown> (via @ref) so the + button can call RefreshAsync() after creating a new grade");
    }

    [TestMethod]
    public void EnrollDialog_Resolves_CodedValueId_To_GradeLevelDto_For_Submit()
    {
        // The dialog's submit uses a <GradeLevelDto.Id> (not a
        // CodedValueId) because the server's EnrollStudentAsync
        // takes a GradeLevelId. The CodedValueId the user picks in
        // the dropdown must be translated to a GradeLevelDto via
        // the loaded _gradeLevels list (matched on CodedValueId).
        // A regression that submits the CodedValueId directly would
        // hit a server-side validation error.
        var src = Load(DialogPath);
        src.Should().Contain("_selectedGradeCodedValueId",
            "the dialog MUST hold the picked CodedValueId in a _selectedGradeCodedValueId field bound two-way to <CodedValueDropdown>");
        src.Should().Contain("OnGradeCodedValueChanged",
            "the dialog MUST have an OnGradeCodedValueChanged handler (the <CodedValueDropdown>'s @bind-SelectedId:after target) that resolves the CodedValueId to a GradeLevelDto");
        // The :after binding must wire the handler to the dropdown.
        src.Should().Contain("@bind-SelectedId:after=\"OnGradeCodedValueChanged\"",
            "the <CodedValueDropdown>'s @bind-SelectedId:after MUST be OnGradeCodedValueChanged so the binder updates the field AND the resolver runs");
        // The resolver walks _gradeLevels matching on CodedValueId.
        src.Should().Contain("g.CodedValueId == _selectedGradeCodedValueId",
            "the resolver MUST look up the GradeLevelDto by CodedValueId match against the loaded _gradeLevels list");
    }

    // ── Form follows the canonical FormRow pattern (180px labels) ──────
    //
    // Each form field is wrapped in the shared <FormRow> primitive so
    // the labels are equal-width (180px) and every input's left edge
    // lands on the same vertical axis. The form uses the canonical
    // school-collab FormRow pattern, NOT a free <FluentStack>.

    [TestMethod]
    public void EnrollDialog_Uses_FormRow_For_All_Form_Fields()
    {
        // Every form field MUST be wrapped in a <FormRow> with a
        // label. A regression that drops the FormRow wrapper (and
        // falls back to raw <FluentSelect>/<FluentDatePicker> with
        // their own built-in labels) would break the canonical
        // 180px-label layout.
        var src = Load(DialogPath);
        // Each expected field has its own FormRow. Note: there is
        // NO "Student" FormRow — the dialog intentionally does NOT
        // re-state the student context (the caller already shows
        // the student on the page; the user just clicked "Enroll"
        // on a specific student's page). A regression that adds
        // a "Student" FormRow would be redundant.
        src.Should().NotContain("<FormRow Label=\"Student\"",
            "the dialog MUST NOT have a <FormRow Label=\"Student\"> — the caller already shows the student context on the page; re-stating it inside the dialog is redundant and was deliberately removed");
        src.Should().Contain("<FormRow Label=\"Period\"",
            "the Period field MUST be wrapped in a <FormRow Label=\"Period\"> (read-only — see EnrollDialog_Shows_Active_Period_As_ReadOnly_FormRow)");
        src.Should().Contain("<FormRow Label=\"Grade level\"",
            "the Grade level field MUST be wrapped in a <FormRow Label=\"Grade level\"> so its label lines up with the other fields");
        src.Should().Contain("<FormRow Label=\"Enrolled on\"",
            "the Enrolled on field MUST be wrapped in a <FormRow Label=\"Enrolled on\"> so its label lines up with the other fields");
        // The form must use the canonical FormRow primitive from
        // SchoolCollab.Admin.Shared.Components (already in scope via
        // the @using directive).
        src.Should().Contain("FormRow",
            "the dialog MUST use the shared <FormRow> primitive from SchoolCollab.Admin.Shared.Components — the repo-wide 180px-label form layout convention");
    }

    [TestMethod]
    public void EnrollDialog_Css_Aligns_Form_With_FormRow_Primitive()
    {
        // The dialog's own CSS must NOT redefine the 180px label
        // column — that lives in the shared FormRow.razor.css. The
        // dialog's CSS only carries dialog-specific bits (form
        // max-width, grade row layout, per-input widths).
        var src = Load(DialogCssPath);
        // The dialog must not redefine the .form-row-label width
        // (that would be a regression of the FormRow contract).
        src.Should().NotContain(".form-row-label",
            "the dialog's own CSS MUST NOT redefine .form-row-label (the 180px column lives in the shared FormRow.razor.css; redefining it here would break the FormRow contract across other consumers)");
        // The dialog must have a max-width on the form so the input
        // cells don't stretch uncomfortably wide on big screens.
        src.Should().Contain(".enroll-dialog",
            "the dialog's own CSS MUST carry the dialog-level .enroll-dialog rules (max-width, gap, etc.)");
        // The 720px cap (wider than StudentFormFields' 600px) gives
        // the read-only Period row enough room to display the full
        // period text on one line even when the period's name is
        // long (e.g. "Fall 2025 / Spring 2026"). A regression that
        // narrows the cap back to 600px would re-introduce the
        // text-truncation issue the cap was widened to fix.
        src.Should().Contain("max-width: 720px",
            "the .enroll-dialog form MUST be capped at 720px max-width (wider than the 600px StudentFormFields cap) so the read-only Period row can display the full period text on one line without truncation");
    }

    // ── No "Student" row (the "remove student name" fix) ──────
    //
    // The dialog intentionally does NOT render a "Student" row. The
    // caller (Detail.razor / Edit.razor) already shows the student
    // context on the page; re-stating it inside the dialog is
    // redundant (the user just clicked "Enroll" on a specific
    // student's page; they know which student they're enrolling).
    // These tests pin the "no student name in the dialog" contract
    // so a regression that re-adds the Student row (or its
    // supporting state) is caught by source-level scan.

    [TestMethod]
    public void EnrollDialog_Does_Not_Show_A_Student_Row_Or_Load_Student()
    {
        // The dialog must not render a "Student" FormRow. It also
        // must not hold a _studentName state field nor call
        // GetStudentByIdAsync in OnInitializedAsync (both were
        // removed when the Student row was deleted; re-introducing
        // either is a regression of the "remove student name"
        // fix and would also add a redundant API round-trip).
        var src = Load(DialogPath);
        src.Should().NotContain("<FormRow Label=\"Student\"",
            "the dialog MUST NOT have a <FormRow Label=\"Student\"> — the caller already shows the student context on the page; re-stating it inside the dialog is redundant");
        src.Should().NotContain("_studentName",
            "the dialog MUST NOT have a _studentName state field — the Student row is gone, so the field is dead state; keeping it would mean dead state to maintain alongside the live fields");
        src.Should().NotContain("GetStudentByIdAsync",
            "the dialog MUST NOT call GetStudentByIdAsync in OnInitializedAsync — the Student row is gone, so the call is a redundant API round-trip; the dialog only needs periods + grade levels to render the form");
    }

    // ── Active period display is the academic-year format (the
    // "show period as 2025/2026" fix) ──────
    //
    // The active period is rendered in the canonical academic-year
    // format "{StartDate.Year}/{EndDate.Year}" (e.g. "2025/2026")
    // so the read-only input cell always fits the full text on one
    // line regardless of the period's name. The full name + date
    // range is exposed as a native title= tooltip via
    // ActivePeriodTooltip so the user can hover for the complete
    // context without the row overflowing.

    [TestMethod]
    public void EnrollDialog_Active_Period_Display_Is_Academic_Year_Format()
    {
        // ActivePeriodText must return the academic-year token
        // "{StartDate.Year}/{EndDate.Year}" (e.g. "2025/2026"),
        // NOT the old "Name (start – end)" formatting. A regression
        // that re-introduces the long format would overflow the
        // read-only input cell when the period's name is long.
        var src = Load(DialogPath);
        src.Should().Contain("ActivePeriodText",
            "the dialog MUST compute ActivePeriodText for the Period FormRow's input");
        // The new format is the short academic-year token. The
        // string is built from p.StartDate.Year + "/" + p.EndDate.Year
        // (the canonical C# expression). We assert both halves
        // appear in the expression so a regression that drops the
        // format change is caught.
        var activePeriodTextDefIdx = src.IndexOf("ActivePeriodText =>", StringComparison.Ordinal);
        activePeriodTextDefIdx.Should().BeGreaterThan(0, "ActivePeriodText must be defined as a computed property");
        var activePeriodTextBody = src.Substring(activePeriodTextDefIdx);
        activePeriodTextBody.Should().Contain("StartDate.Year",
            "ActivePeriodText MUST be built from StartDate.Year (the academic-year start year) so the format is the canonical \"YYYY/YYYY\" token");
        activePeriodTextBody.Should().Contain("EndDate.Year",
            "ActivePeriodText MUST be built from EndDate.Year (the academic-year end year) so the format is the canonical \"YYYY/YYYY\" token");
    }

    [TestMethod]
    public void EnrollDialog_Active_Period_Has_Full_Context_Tooltip()
    {
        // The active period must expose the full name + date range
        // as a native title= tooltip via ActivePeriodTooltip so
        // a user hovering the field sees the complete context
        // (not just the short academic-year token). A regression
        // that drops the tooltip leaves the user with only the
        // short token and no way to see the period's full name
        // or its start/end dates.
        var src = Load(DialogPath);
        src.Should().Contain("ActivePeriodTooltip",
            "the dialog MUST compute an ActivePeriodTooltip property that exposes the full period name + date range for hover context");
        // The period FormRow's input must bind its Title attribute
        // to ActivePeriodTooltip so the tooltip actually surfaces
        // to the user.
        var periodRowIdx = src.IndexOf("<FormRow Label=\"Period\"", StringComparison.Ordinal);
        periodRowIdx.Should().BeGreaterThan(0);
        var afterPeriodRow = src.Substring(periodRowIdx);
        afterPeriodRow.Should().Contain("Title=\"@ActivePeriodTooltip\"",
            "the period FormRow's input MUST bind its Title attribute to ActivePeriodTooltip so the hover-tooltip actually surfaces to the user");
    }

    // ── Per-field error display below the Enrolled on date picker
    // (the "error display message below enrolledOn date" fix) ──────
    //
    // The dialog's submit failure error (set on the Error property
    // by SubmitAsync on the null-return or throw path) is surfaced
    // directly below the Enrolled on date picker via a
    // <FluentMessageBar Intent="Error"> inside the Enrolled on
    // FormRow's input cell. This pins the failure visually to the
    // field that triggered it. The shared DialogShellFooter still
    // shows its own error bar (kept for symmetry with every other
    // DialogShellBase dialog); the in-context display is the
    // primary user-facing message.

    [TestMethod]
    public void EnrollDialog_Shows_Per_Field_Error_Below_EnrolledOn()
    {
        // The "Enrolled on" FormRow must contain a per-field
        // <FluentMessageBar Intent="Error"> that mirrors the
        // dialog's Error property, rendered DIRECTLY BELOW the
        // <FluentDatePicker>. A regression that drops the per-
        // field error (or moves it elsewhere) leaves the user
        // with only the footer's error bar — far from the field
        // that caused the failure.
        var src = Load(DialogPath);
        var enrolledOnRowIdx = src.IndexOf("<FormRow Label=\"Enrolled on\"", StringComparison.Ordinal);
        enrolledOnRowIdx.Should().BeGreaterThan(0, "the dialog MUST have a <FormRow Label=\"Enrolled on\"> (sanity)");
        // The FluentDatePicker must come before the per-field
        // error bar (the error renders below the picker, in the
        // same input cell).
        var afterEnrolledOnRow = src.Substring(enrolledOnRowIdx);
        var datePickerIdx = afterEnrolledOnRow.IndexOf("<FluentDatePicker", StringComparison.Ordinal);
        var errorBarIdx = afterEnrolledOnRow.IndexOf("enroll-form-field-error", StringComparison.Ordinal);
        datePickerIdx.Should().BeGreaterThan(0, "the Enrolled on FormRow MUST contain a <FluentDatePicker>");
        errorBarIdx.Should().BeGreaterThan(0,
            "the Enrolled on FormRow MUST contain a per-field error MessageBar (class='enroll-form-field-error') directly below the date picker");
        errorBarIdx.Should().BeGreaterThan(datePickerIdx,
            "the per-field error MessageBar MUST come AFTER the <FluentDatePicker> in the Enrolled on FormRow (the error renders below the picker, in the same input cell)");
        // The error bar must be gated on the Error property (so it
        // does not render an empty bar on the happy path). The
        // markup uses the Razor form `@Error` (Razor treats it as
        // a variable output) — NOT `@(Error)` — so we assert the
        // unparenthesized form.
        afterEnrolledOnRow.Should().Contain("@Error",
            "the per-field error MessageBar MUST render the dialog's @Error property (so the same text surfaces in the footer's error bar and the per-field bar)");
        afterEnrolledOnRow.Should().Contain("MessageIntent.Error",
            "the per-field error MessageBar MUST use MessageIntent.Error to match the dialog's error semantics");
    }

    [TestMethod]
    public void EnrollDialog_Css_Accommodates_Full_Period_Text()
    {
        // The dialog's CSS must widen the form max-width to 720px
        // (vs the previous 600px) so the read-only Period row can
        // display the full period text on one line. The 720px cap
        // gives the input cell enough room even for long period
        // names like "Fall 2025 / Spring 2026". A regression that
        // narrows the cap back to 600px re-introduces the
        // truncation issue the cap was widened to fix.
        var src = Load(DialogCssPath);
        src.Should().NotContain("max-width: 600px",
            "the dialog CSS MUST NOT cap the form at 600px (the previous cap that was too narrow to fit long period text — the cap was deliberately widened to 720px to accommodate the full period text)");
        src.Should().Contain("max-width: 720px",
            "the dialog CSS MUST cap the form at 720px max-width (wider than StudentFormFields' 600px) so the read-only Period row can fit the full period text on one line");
        // The dialog CSS must also style the per-field error bar
        // (sized to the input cell, with a top margin to sit just
        // below the date picker without disrupting form rhythm).
        src.Should().Contain(".enroll-form-field-error",
            "the dialog CSS MUST style the .enroll-form-field-error per-field error MessageBar (the bar that sits below the Enrolled on date picker)");
        src.Should().Contain("margin-top: 8px",
            "the .enroll-form-field-error CSS MUST add a margin-top so the error bar sits a few px below the date picker (visual separation without disrupting the form rhythm)");
    }

    // ── Error tracing detail (the "only Error with no detail" fix) ──────
    //
    // The user observed the dialog's error MessageBar showing only
    // the word "Error" (the FluentMessageBar's built-in heading for
    // MessageIntent.Error) with no body detail. Tracing the chain:
    //   1. The user clicks Enroll.
    //   2. <SubmitAsync> calls <EnrollStudentAsync> on the API client.
    //   3. The server returns a non-2xx (e.g. 400 for
    //      <PeriodNotOpenException>, 409 for <ConcurrencyException>).
    //   4. <StudentsApiClient.EnrollStudentAsync> previously called
    //      <HttpResponseMessage.EnsureSuccessStatusCode()>, which
    //      throws <HttpRequestException> with Message = "Response
    //      status code does not indicate success: 400 (Bad Request)."
    //      — the response BODY is dropped, so the actual server-side
    //      reason ("Cannot enrol students: no active period is open
    //      for this tenant...") is lost.
    //   5. <SubmitAsync> catches the exception and sets
    //      <Error = ex.Message>, so the user sees only the generic
    //      status-code text — useless for tracing WHAT went wrong.
    //   6. The server endpoint also did NOT catch
    //      <PeriodNotOpenException>, so the exception bubbled up as
    //      a 500 with no body — making step (4) even less useful
    //      (the body was literally empty).
    //
    // The fix has three parts that must stay in lock-step:
    //   1. The server endpoint catches <PeriodNotOpenException> and
    //      returns a 400 with the exception's Message in the body
    //      (so the body is non-empty and carries the real reason).
    //   2. <StudentsApiClient.EnrollStudentAsync> reads the response
    //      body on failure and includes it in the thrown
    //      <HttpRequestException.Message> (so the full detail
    //      survives the wire into the client's catch block).
    //   3. <EnrollStudentDialog.SubmitAsync> sets <Error = ex.Message>
    //      (already the case; the richer Message now flows through
    //      automatically) and logs the full context (StudentId,
    //      PeriodId, GradeLevelId) for operator-side tracing.
    //   4. The per-field error MessageBar renders the full @Error
    //      string (already the case; the markup is unchanged).
    // These tests pin each link in the chain so a regression that
    // drops any of them (e.g. a future refactor re-introducing
    // <EnsureSuccessStatusCode> on the enroll path) is caught.

    [TestMethod]
    public void EnrollDialog_Api_Client_Reads_Response_Body_On_Failure()
    {
        // The <StudentsApiClient.EnrollStudentAsync> method must NOT
        // call <HttpResponseMessage.EnsureSuccessStatusCode()> — the
        // default <HttpRequestException> it throws only carries the
        // status code text and DROPS the response body. The server's
        // body is where the actual tracing detail lives (e.g.
        // "PeriodNotOpenException" messages). Pin that the enroll
        // path uses the manual status-check + body-read pattern so
        // the body survives the wire into the client catch.
        var src = Load(ApiClientPath);
        // The enroll method is the only one we care about for the
        // dialog (other methods can be migrated separately if
        // needed). Pin the presence of the manual pattern AND the
        // absence of the bad pattern. Scope the search to just this
        // method's body (start: the signature, end: the next method
        // boundary or 1200 chars — the enroll method body is well
        // under that). The next method in the file starts with
        // "public async Task TransferStudentAsync", which is the
        // cleanest boundary to use.
        var enrollMethodIdx = src.IndexOf("EnrollStudentAsync(EnrollStudentRequest req", StringComparison.Ordinal);
        enrollMethodIdx.Should().BeGreaterThan(0, "the API client MUST define an EnrollStudentAsync(EnrollStudentRequest) method (sanity)");
        var nextMethodIdx = src.IndexOf("public async Task TransferStudentAsync", enrollMethodIdx, StringComparison.Ordinal);
        nextMethodIdx.Should().BeGreaterThan(enrollMethodIdx, "TransferStudentAsync must come after EnrollStudentAsync (sanity)");
        var enrollMethodBody = src.Substring(enrollMethodIdx, nextMethodIdx - enrollMethodIdx);
        enrollMethodBody.Should().Contain("IsSuccessStatusCode",
            "the enroll method MUST check the status manually (IsSuccessStatusCode) so the failure path can read the response body before throwing");
        enrollMethodBody.Should().Contain("ReadAsStringAsync",
            "the enroll method MUST read the response body as a string on failure so the body is included in the thrown exception's Message (the default EnsureSuccessStatusCode path drops the body)");
        enrollMethodBody.Should().Contain("EnrollStudent failed",
            "the enroll method's thrown exception MUST be tagged with the failing method name (\"EnrollStudent failed\") so the user-facing error in the dialog identifies which API call failed");
        enrollMethodBody.Should().Contain("response.StatusCode",
            "the enroll method's thrown exception MUST include the HTTP status code (response.StatusCode) so the user can distinguish 4xx (client error — fix the request) from 5xx (server error — escalate)");
        enrollMethodBody.Should().Contain("HttpRequestException",
            "the enroll method MUST throw an HttpRequestException (not a custom exception type) so the dialog's existing `catch (Exception ex)` block surfaces the rich message without needing a separate handler");
        // The BAD pattern: calling EnsureSuccessStatusCode on the
        // enroll path drops the body. A regression that re-introduces
        // the call would silently re-break the tracing flow. Scoped
        // to the enroll method body (not the whole file) so it
        // doesn't false-positive on other methods that still use it.
        // We check for the actual CALL shape (e.g.
        // "response.EnsureSuccessStatusCode()" or just
        // "EnsureSuccessStatusCode()" on a line) rather than the
        // bare word, so the doc comment "do NOT use
        // EnsureSuccessStatusCode" inside the method body doesn't
        // false-positive this check.
        var ensureCallPattern = new Regex(@"EnsureSuccessStatusCode\s*\(", RegexOptions.Compiled);
        var linesWithEnsureCall = enrollMethodBody.Split('\n')
            .Where(line => ensureCallPattern.IsMatch(line))
            .ToArray();
        linesWithEnsureCall.Should().BeEmpty(
            "the enroll method MUST NOT call EnsureSuccessStatusCode (it throws an HttpRequestException with only the status code text and drops the response body — the body is where the server's tracing detail lives). The doc comment mentioning EnsureSuccessStatusCode by name is fine; only an actual CALL is a regression.");
    }

    [TestMethod]
    public void EnrollDialog_Api_Endpoint_Catches_PeriodNotOpen_Exception()
    {
        // The <MapPost("/enrollments", ...)> endpoint must catch
        // <PeriodNotOpenException> and return a 400 (Bad Request)
        // with the exception's Message in the body. Without this
        // catch the exception bubbles up as a 500 with no body,
        // and the client's <EnrollStudentAsync> only sees the
        // generic "Response status code does not indicate success:
        // 500" text — useless for tracing WHAT went wrong. The
        // 400 is the correct semantic (the request targets a
        // period that is not the active one) and carries the
        // server's reason in the body for the client to surface.
        var src = Load(EnrollmentRoutesPath);
        // Scope to the enroll MapPost handler body (starts with
        // the signature, ends with the next MapPost — the transfer
        // endpoint). This avoids false positives from other
        // endpoints that may catch the same exception in the
        // future.
        var enrollPostIdx = src.IndexOf("MapPost(\"/enrollments\", async", StringComparison.Ordinal);
        enrollPostIdx.Should().BeGreaterThan(0, "the API MUST define a MapPost(\"/enrollments\", ...) endpoint (sanity)");
        var nextPostIdx = src.IndexOf("MapPost(\"/enrollments/", enrollPostIdx + 1, StringComparison.Ordinal);
        var enrollPostBody = nextPostIdx > enrollPostIdx
            ? src.Substring(enrollPostIdx, nextPostIdx - enrollPostIdx)
            : src.Substring(enrollPostIdx, Math.Min(src.Length - enrollPostIdx, 4000));
        enrollPostBody.Should().Contain("PeriodNotOpenException",
            "the enroll endpoint MUST catch <PeriodNotOpenException> (thrown by the handler when the tenant has no active period or the request targets a non-active one) and return a 4xx with the message in the body so the client can surface the real reason in the dialog's error bar");
        enrollPostBody.Should().Contain("Results.BadRequest",
            "the enroll endpoint MUST return Results.BadRequest(...) for the PeriodNotOpenException case (400 is the correct semantic — the request is well-formed but targets a non-active period, not a server error)");
        enrollPostBody.Should().Contain("ex.Message",
            "the enroll endpoint MUST include ex.Message in the BadRequest body so the client receives the real server-side reason (not just the status code)");
    }

    [TestMethod]
    public void EnrollDialog_Submit_Logs_Full_Context_For_Tracing()
    {
        // The dialog's <SubmitAsync> catch block must log the full
        // context (StudentId + PeriodId + GradeLevelId) alongside
        // the exception so an operator can correlate the user's
        // error with a server-side log entry. Without these fields
        // in the log message, the only way to trace the failure is
        // to grep for the timestamp + student id — doable but slow.
        // Including them in the structured log message turns
        // log-queries into one-liners.
        var src = Load(DialogPath);
        var submitAsyncIdx = src.IndexOf("protected override async Task<EnrollStudentResult?> SubmitAsync", StringComparison.Ordinal);
        submitAsyncIdx.Should().BeGreaterThan(0, "the dialog MUST define a SubmitAsync method (sanity)");
        // Scope to the SubmitAsync body (start: the signature,
        // end: the next method — the OnAddNewGradeAsync summary
        // doc-comment marker is the cleanest boundary).
        var nextMethodIdx = src.IndexOf("private async Task OnAddNewGradeAsync", submitAsyncIdx, StringComparison.Ordinal);
        nextMethodIdx.Should().BeGreaterThan(submitAsyncIdx, "OnAddNewGradeAsync must come after SubmitAsync (sanity)");
        var submitAsyncBody = src.Substring(submitAsyncIdx, nextMethodIdx - submitAsyncIdx);
        // The existing single-message log call is too sparse —
        // upgrade it to include all three ids.
        submitAsyncBody.Should().Contain("Logger.LogError(ex,",
            "the SubmitAsync catch block MUST log the exception (so the operator has the stack trace)");
        submitAsyncBody.Should().Contain("model.StudentId",
            "the SubmitAsync catch block MUST include model.StudentId in the log message (so the operator can correlate the user's error with the student they were enrolling)");
        submitAsyncBody.Should().Contain("_activePeriod.Id",
            "the SubmitAsync catch block MUST include the active period id in the log message (so the operator can check the period state at the time of the failure)");
        submitAsyncBody.Should().Contain("_selectedGrade.Id",
            "the SubmitAsync catch block MUST include the selected grade id in the log message (so the operator can check the grade state at the time of the failure)");
        // The Error property must still be set to ex.Message so the
        // per-field error MessageBar AND the shared DialogShellFooter
        // both display the rich message (status code + body). The
        // message is set to ex.Message on the @Error property, which
        // flows through to both the per-field bar and the footer.
        submitAsyncBody.Should().Contain("Error = ex.Message",
            "the SubmitAsync catch block MUST set Error = ex.Message so the rich message (status code + body) flows through to BOTH the per-field error MessageBar AND the shared DialogShellFooter");
    }

    [TestMethod]
    public void EnrollDialog_Does_Not_Shadow_Base_Error_Field()
    {
        // The dialog MUST use the shared <Error> property from
        // <DialogShellBase> (which backs the <DialogShellFooter>
        // error bar). Declaring a derived <private string? _error;>
        // creates a SECOND, independent error state: the top-level
        // bar checks one field while the footer / per-field bar
        // checks another. That caused the dialog to render a stray
        // error MessageBar immediately on popup (the derived field
        // was set during initialization while the footer saw a
        // different value). Use the base property everywhere.
        var src = Load(DialogPath);
        src.Should().NotContain("private string? _error;",
            "the dialog MUST NOT shadow DialogShellBase's _error field with its own declaration — it should use the protected Error property so all error surfaces (top-level bar, per-field bar, footer) share one state");
        // The top-level load-failure bar must consume the same
        // <Error> property so initialization errors flow to the
        // footer too.
        var topLevelBarIdx = src.IndexOf("else if", StringComparison.Ordinal);
        var topLevelBarBody = src.Substring(topLevelBarIdx, Math.Min(src.Length - topLevelBarIdx, 200));
        topLevelBarBody.Should().Contain("Error",
            "the top-level error bar MUST check the shared Error property, not a shadow _error field");
    }

    [TestMethod]
    public void EnrollDialog_Api_Data_Load_Methods_Read_Body_On_Failure()
    {
        // The dialog's initial render calls <ListPeriodsAsync> and
        // <ListGradeLevelsAsync>. If either of those endpoints fails,
        // the resulting <HttpRequestException> must include the
        // response body — otherwise the dialog's <Error = ex.Message>
        // in OnInitializedAsync's catch block shows only the generic
        // "Response status code does not indicate success" text,
        // and the operator/user can't tell WHICH API call failed or
        // WHY. Pin the manual status-check + body-read pattern for
        // both data-load methods (same pattern already used by
        // <EnrollStudentAsync>).
        var src = Load(ApiClientPath);

        foreach (var methodName in new[] { "ListPeriodsAsync", "ListGradeLevelsAsync" })
        {
            var methodIdx = src.IndexOf($"public async Task<PeriodDto[]?> {methodName}", StringComparison.Ordinal);
            if (methodIdx < 0)
            {
                methodIdx = src.IndexOf($"public async Task<GradeLevelDto[]?> {methodName}", StringComparison.Ordinal);
            }
            methodIdx.Should().BeGreaterThan(0, $"the API client MUST define a {methodName} method (sanity)");

            var nextMethodIdx = src.IndexOf("public async Task", methodIdx + 1, StringComparison.Ordinal);
            var methodBody = nextMethodIdx > methodIdx
                ? src.Substring(methodIdx, nextMethodIdx - methodIdx)
                : src.Substring(methodIdx, Math.Min(src.Length - methodIdx, 1200));

            methodBody.Should().Contain("IsSuccessStatusCode",
                $"{methodName} MUST check the status manually so the failure path can read the response body");
            methodBody.Should().Contain("ReadAsStringAsync",
                $"{methodName} MUST read the response body as a string on failure so the body is included in the thrown exception's Message");
            methodBody.Should().Contain($"{methodName.Replace("Async", "")} failed",
                $"{methodName}'s thrown exception MUST identify the failing method name so the dialog's error bar shows which data load failed");
            methodBody.Should().Contain("HttpRequestException",
                $"{methodName} MUST throw an HttpRequestException so the dialog's existing catch block surfaces the rich message");
        }
    }
}

