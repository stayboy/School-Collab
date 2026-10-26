using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level regression tests for the student edit page
/// (<c>Edit.razor</c>) on the shared create / edit skeleton
/// (<c>documents/solution/panel-layout-pattern.md</c>).
///
/// The class keeps the name it was created under: this page USED to be a
/// page-level 2-column grid (<c>.student-edit-grid</c> with a sticky
/// right-column Save / Cancel sidebar). Owner, 2026-10-24 (§4/§5 of the
/// pattern): the pair moved into the page action header
/// (<c>AuthoringShell</c>), and that column became the sticky RELATIONSHIP
/// RAIL. The tests below were retargeted from the sidebar seams to the
/// panel-layout seams rather than deleted — the behaviours they were written
/// for (the save pair exists, saving drives the form through
/// <c>SubmitAsync</c>, the blocks land in the right region) still hold, only
/// their homes moved.
///
/// What these tests guard against:
///   - Re-introducing a wrapper inside <c>StudentFormFields</c>
///     (which would force dialogs to inherit a sidebar they don't
///     want, breaking the dialog-ui convention)
///   - Adding a FluentTabs (the user explicitly forbade tabs on the
///     student view page; one scrolling column is the design)
///   - Re-introducing the obsolete ActionsPlacement enum / parameter
///   - Forgetting to drive the form's submit from the header's primary
///     (the form would silently not submit)
///   - A block drifting back into the wrong region of the grid
/// </summary>
[TestClass]
public class EditSidebarLayoutTests
{
    private const string EditRazorPath = "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Edit.razor";
    private const string EditCssPath = "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Edit.razor.css";
    private const string FormRazorPath = "src/Students/SchoolCollab.Students.Application/Components/Students/StudentFormFields.razor";
    private const string FormCssPath = "src/Students/SchoolCollab.Students.Application/Components/Students/StudentFormFields.razor.css";

    /// <summary>The shared layout primitives — the region grid and its sticky rail — live
    /// here, not in the page stylesheet (panel-layout-pattern §2).</summary>
    private const string AppCssPath = "src/SchoolCollab.Admin.Shared/wwwroot/app.css";

    /// <summary>
    /// Reads a source file from the repo root. The path constants above
    /// are repo-relative (e.g. "src/Students/.../Edit.razor"), so we walk
    /// up 5 levels from the test assembly output directory
    /// (bin/Debug/net10.0) to land on the repo root.
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
    public void Edit_Uses_The_Shared_Region_Grid_With_Main_And_Rail()
    {
        var src = Load(EditRazorPath);
        // The layout is the shared pair from app.css, not a page-local grid: the host
        // carries the container query, the grid the two tracks, the main column the
        // student's own blocks and `#student-edit-rail` the relationships.
        src.Should().Contain("class=\"region-grid-host\"",
            "Edit.razor wraps the grid in the shared .region-grid-host (the container-query host)");
        src.Should().Contain("class=\"region-grid\"",
            "the two-column layout is the shared .region-grid, not a page-local one");
        src.Should().Contain("class=\"region-grid__main\"",
            "column 1 holds the student's own blocks (Details, Contacts)");
        src.Should().Contain("class=\"region-grid__rail\" id=\"student-edit-rail\"",
            "column 2 is the relationship rail, addressed by the pinned #student-edit-rail seam");
        src.Should().NotContain("student-edit-grid",
            "the retired page-local grid must not come back — it would fork the shared layout");
    }

    [TestMethod]
    public void Edit_Does_Not_Use_FluentTabs()
    {
        var src = Load(EditRazorPath);
        // The user explicitly forbade tabs on the student view/edit
        // pages: a single scrollable sectioned layout is the design.
        // Edit.razor lives in the same module and must follow the
        // same rule.
        src.Should().NotContain("<FluentTabs",
            "the student edit page is single-page sectioned (no tabs) — matches the student view page convention");
    }

    [TestMethod]
    public void Edit_Save_Pair_Lives_In_The_Page_Action_Header()
    {
        var src = Load(EditRazorPath);
        // Owner, 2026-10-24 (§4): the page action header owns the save pair. The primary is
        // pinned to #authoring-primary-action and drives the form (see the SubmitAsync test);
        // Cancel rides in the LeadingActions slot, i.e. left of the primary.
        src.Should().Contain("PrimaryId=\"authoring-primary-action\"",
            "the header's primary button carries the pinned #authoring-primary-action seam");
        src.Should().Contain("OnPrimaryAction=\"OnSidebarSaveAsync\"",
            "the header's primary action must run the page's save handler");
        src.Should().Contain("LeadingActions=\"@PageCancel\"",
            "Cancel rides in the header's leading-actions slot, left of the primary");
        src.Should().Contain("Id=\"authoring-cancel\"",
            "the Cancel button carries the pinned #authoring-cancel seam");
        src.Should().NotContain("student-edit-action-stack",
            "the retired sidebar action stack must not come back");
    }

    [TestMethod]
    public void Edit_Primary_Action_Is_SaveChanges_And_Cancel_Leaves_For_The_Detail_Page()
    {
        var src = Load(EditRazorPath);
        // The primary's label is the saving ternary, so the button cannot read "Save" while
        // the POST is in flight. "Save Changes" — not "Save" — matches the other authoring
        // surfaces. Cancel leaves without saving, back to the student's view page.
        src.Should().Contain("@(_saving ? \"Saving…\" : \"Save Changes\")",
            "the header's primary reads Save Changes, and Saving… while the save is in flight");
        src.Should().Contain("Nav.NavigateTo($\"/students/{Id}\")",
            "Cancel leaves for the student's view page (the destination the sidebar's Cancel used)");
    }

    [TestMethod]
    public void Edit_Save_Button_Calls_SubmitAsync_Through_Form_Ref()
    {
        var src = Load(EditRazorPath);
        // The save button's @onclick calls OnSidebarSaveAsync,
        // which delegates to _form!.SubmitAsync(). The standard
        // Blazor pattern for driving an EditForm submit from a
        // button that sits outside the form.
        src.Should().Contain("OnPrimaryAction=\"OnSidebarSaveAsync\"",
            "the header's primary action must call the page-level OnSidebarSaveAsync (which delegates to _form.SubmitAsync())");
        src.Should().Contain("\"OnSidebarSaveAsync\"",
            "the handler is wired by name — a rename on one side must break the build, not the submit");
        src.Should().Contain("await _form.SubmitAsync()",
            "OnSidebarSaveAsync must call _form.SubmitAsync() to drive the EditForm submit through the @ref");
    }

    [TestMethod]
    public void Edit_StudentFormFields_Uses_RenderActions_False()
    {
        var src = Load(EditRazorPath);
        // The page action header IS the action row on the Edit
        // page. StudentFormFields' built-in bottom-horizontal row
        // would create a duplicate pair of buttons (one in the
        // header, one below the form). RenderActions="false"
        // tells the component to skip its built-in row.
        src.Should().Contain("RenderActions=\"false\"",
            "Edit.razor MUST pass RenderActions=\"false\" to <StudentFormFields> so the component skips its built-in bottom-horizontal row (the page action header is the only action row)");
    }

    [TestMethod]
    public void Edit_FormFields_And_Contacts_Are_Both_In_The_Main_Region()
    {
        var src = Load(EditRazorPath);
        // Contacts stay in the MAIN column (owner Q81: the student's own reachability
        // detail, and a full inline form that does not fit a rail). The form fields are
        // of course main too. If either escaped into the rail, the rail would stop being
        // the list of RELATIONSHIPS and become a second content column.
        var mainMatch = Regex.Match(src, @"<div class=""region-grid__main"">");
        var railMatch = Regex.Match(src, @"<div class=""region-grid__rail""");
        mainMatch.Success.Should().BeTrue("the main region exists");
        railMatch.Success.Should().BeTrue("the rail region exists");
        var mainStart = mainMatch.Index + mainMatch.Length;
        var railStart = railMatch.Index;
        mainStart.Should().BeLessThan(railStart, "the main region precedes the rail");

        var mainSlice = src.Substring(mainStart, railStart - mainStart);
        mainSlice.Should().Contain("<StudentFormFields",
            "<StudentFormFields> MUST be inside the main region");
        mainSlice.Should().Contain("<ContactsEditor",
            "the <ContactsEditor> MUST be inside the main region — the contacts editor is the student's own detail, not a relationship");
    }

    [TestMethod]
    public void StudentFormFields_RenderActions_Parameter_Defaults_To_True()
    {
        // The default MUST be true so dialog callers (Create.razor,
        // GradeLevelWizard.razor, StudentPickerDialog.razor) keep
        // the dialog-ui convention of bottom-horizontal Cancel/Save
        // without having to opt in. Only Edit.razor opts out.
        var src = Load(FormRazorPath);
        var paramMatch = Regex.Match(src,
            @"\[Parameter\]\s*public\s+bool\s+RenderActions\s*\{\s*get;\s*set;\s*\}\s*=\s*true");
        paramMatch.Success.Should().BeTrue(
            "StudentFormFields.RenderActions MUST default to true (dialog callers rely on the built-in bottom-horizontal row)");
    }

    [TestMethod]
    public void StudentFormFields_Exposes_Public_SubmitAsync_Method()
    {
        // The page action header's primary calls _form.SubmitAsync() to drive
        // the EditForm submit. The method must be public so the
        // page can call it across the component boundary, and it
        // must (1) read the EditForm's EditContext, (2) call
        // EditContext.Validate(), (3) invoke OnValidSubmit on
        // success — the canonical Blazor pattern.
        var src = Load(FormRazorPath);
        src.Should().Contain("public async Task<bool> SubmitAsync()",
            "StudentFormFields must expose a public SubmitAsync() method so the page action header can drive the EditForm submit");
        src.Should().Contain("_editForm?.EditContext",
            "SubmitAsync() must read the EditForm's EditContext via _editForm?.EditContext (the canonical Blazor pattern for outside-form submits)");
        src.Should().Contain("ctx.Validate()",
            "SubmitAsync() must call EditContext.Validate() and short-circuit on validation failure");
        src.Should().Contain("await OnValidSubmit.InvokeAsync()",
            "SubmitAsync() must fire OnValidSubmit on successful validation (mirroring the EditForm's private HandleSubmitAsync)");
    }

    [TestMethod]
    public void StudentFormFields_Has_No_Internal_Sidebar_Or_Layout_Wrapper()
    {
        // The whole point of the redesign: the 2-column layout is in
        // the parent (Edit.razor / the shared region grid), not inside
        // the form component. If anyone re-introduces a layout wrapper /
        // sidebar slot inside StudentFormFields, dialogs would be forced
        // to inherit a sidebar they don't want.
        var src = Load(FormRazorPath);
        src.Should().NotContain("student-form-fields__layout",
            "StudentFormFields must NOT have an internal layout wrapper — the 2-column layout lives in the parent (Edit.razor)");
        src.Should().NotContain("student-form-fields__sidebar",
            "StudentFormFields must NOT have an internal sidebar slot — the rail lives in the parent (Edit.razor)");
        src.Should().NotContain("student-form-fields--sidebar",
            "StudentFormFields must NOT have an internal sidebar modifier class — the rail lives in the parent (Edit.razor)");
    }

    [TestMethod]
    public void StudentFormFields_Has_No_Obsolete_ActionsPlacement_Enum()
    {
        // The ActionsPlacement enum is dead weight now that the
        // layout is in the parent. If it reappears, dialogs would
        // either (a) be forced to opt out (breaking the
        // dialog-friendly default) or (b) silently render a
        // duplicate sidebar alongside the page action header.
        var src = Load(FormRazorPath);
        src.Should().NotContain("StudentFormActionsPlacement",
            "the obsolete StudentFormActionsPlacement enum MUST NOT reappear in StudentFormFields — the layout is now in the parent");
        src.Should().NotContain("ActionsPlacement=",
            "the obsolete ActionsPlacement parameter MUST NOT reappear on <StudentFormFields> — the layout is now in the parent");
    }

    [TestMethod]
    public void StudentFormFields_CSS_Has_No_Sidebar_Or_Layout_Wrapper_Rules()
    {
        // The .student-form-fields__layout / .student-form-fields--sidebar /
        // .form-actions--sidebar / @media (max-width: 900px) collapse
        // rules were all internal-sidebar machinery. Now that the
        // sidebar lives in the parent, the form's stylesheet must
        // only carry the canonical action-row rule
        // (.form-actions + .form-actions--right).
        var css = Load(FormCssPath);
        css.Should().NotContain(".student-form-fields__layout",
            "the layout wrapper rule must be removed from the form's stylesheet (the grid lives in the parent)");
        css.Should().NotContain(".student-form-fields--sidebar",
            "the --sidebar modifier rule must be removed from the form's stylesheet");
        css.Should().NotContain(".form-actions--sidebar",
            "the .form-actions--sidebar vertical-stack rule must be removed (the bottom-horizontal row is the only default)");
    }

    [TestMethod]
    public void Edit_CSS_Keeps_The_Enrollment_Rules_And_the_Rail_Internals_Are_Shared()
    {
        // The page stylesheet carries only what this page's own panel needs. The layout — including
        // the sticky rail — is the shared grid's job, and the rail-panel internals (.panel-count,
        // .panel-kebab, .rail-list*, .rail-empty) moved into the shared app.css on 2026-10-24 because
        // /students/create renders the same Guardians panel under the same Enrollment panel.
        var css = System.Text.RegularExpressions.Regex.Replace(
            Load(EditCssPath), @"/\*.*?\*/", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);
        css.Should().Contain(".enrollment-display",
            "the read-only enrollment fact set rule must exist");
        css.Should().NotContain(".rail-list",
            "the rail-list rules are shared chrome now — a page-local copy would fork them");
        css.Should().NotContain(".panel-count",
            "the count chip belongs to PanelSection now, not to a page or to the shared rail rules");
        css.Should().NotContain(".student-edit-grid",
            "the page no longer declares its own grid — the shared .region-grid owns the columns");

        var shared = Load(AppCssPath);
        foreach (var rule in new[] { ".rail-list", ".rail-list__row", ".rail-empty" })
        {
            shared.Should().Contain(rule, $"the shared app.css must carry {rule} now that two pages use it");
        }
    }

    [TestMethod]
    public void Contacts_Add_Row_Is_One_Line_With_Value_And_AddGrouped()
    {
        // The add-contact row used to be two stacked rows (identity row + value row), which put the
        // email input on a line of its own beside the Add button and read as a wrapping defect
        // (owner, 2026-10-24). One line now: channel · label · [value + ＋].
        var razor = Load("src/Students/SchoolCollab.Students.Application/Components/Contacts/ContactsEditor.razor");
        razor.Should().NotContain("contacts-editor-add__row--bottom",
            "the second add row is gone — value and Add live inside the one row");
        razor.Should().Contain("contacts-editor-add__value",
            "value + Add travel together as one nowrap group");

        var css = Load("src/Students/SchoolCollab.Students.Application/Components/Contacts/ContactsEditor.razor.css");
        css.Should().NotContain("row--bottom",
            "the dead second-row rules must go with the markup");
        css.Should().Contain(".contacts-editor-add__value",
            "the group rule is what keeps the value input and the Add button on one line");

        // The row must stretch. As a flex item of the wrapping add container it sized to its content,
        // so the row — and with it the value input — stopped short of the block's right edge, leaving
        // a dead gutter beside the Add button (owner report, 2026-10-24).
        css.Should().Contain("width: 100%;",
            "the add row has to fill the add container, or its flexible child has nothing to grow into");

        // These classes land on the `fluent-field` host of a CHILD FluentTextField, which carries the
        // child's CSS-isolation scope attribute — a plain selector here never matched, which is why the
        // value input sat at its intrinsic 185px inside a 586px row. `::deep` is the fix, so pin it.
        css.Should().Contain("::deep .contacts-value",
            "the value-input width rule must pierce CSS isolation — a bare selector silently matches nothing");
        css.Should().Contain("::deep .contacts-label",
            "the label-input width rule must pierce CSS isolation for the same reason");

        // REQUIRED before OPTIONAL (owner, 2026-10-25): the optional label used to sit before the
        // required value, giving the optional input the stronger position in the row.
        var valueIdx = razor.IndexOf("@bind-Value=\"_newContactModel.Value\"", StringComparison.Ordinal);
        var labelIdx = razor.IndexOf("@bind-Value=\"_newContactModel.Label\"", StringComparison.Ordinal);
        valueIdx.Should().BeGreaterThan(-1, "the add row binds the required contact value");
        labelIdx.Should().BeGreaterThan(-1, "the add row binds the optional label");
        valueIdx.Should().BeLessThan(labelIdx,
            "the required value input renders BEFORE the optional label input");
    }

    [TestMethod]
    public void The_Rail_Sticks_And_Collapses_Under_The_Shared_Container_Query()
    {
        // Both halves of the rail's responsive contract moved into the shared stylesheet:
        // it sticks to the scroll port (§6) and collapses to one column under the
        // container query the .region-grid-host declares (§2).
        var css = Load(AppCssPath);
        css.Should().Contain(".region-grid__rail",
            "the sticky rail rule lives in the shared app.css");
        css.Should().Contain("position: sticky",
            "the rail must stick so the relationships stay reachable while the form scrolls");
        css.Should().Contain("@container region-grid-host (max-width: 899px)",
            "the collapse is a container query on the host, so a narrow RAIL column is not mistaken for a narrow page");
        css.Should().Contain("grid-template-columns: minmax(0, 1fr)",
            "the collapse rule drops the grid to a single column");
    }

    // ── Active enrollment panel (read-only display + kebab actions) ────────
    //
    // The data model is Student ─< Enrollment >─ GradeLevel. A student
    // does not have a direct GradeLevelId; the active grade is read off
    // the student's primary active enrollment (= the most recent
    // StudentEnrollment with Status == "Active" and ExitDate null).
    //
    // The spec says: existing primary active enrollment CANNOT be edited
    // on the student edit page — it is only displayed. The user can act
    // on it via the Enroll / Transfer / Withdraw items in the panel's kebab
    // (which create a new enrollment row and/or mark the current as
    // Transferred / Withdrawn). On the Edit page:
    //   - The primary active enrollment is rendered read-only (no inputs,
    //     no two-way bindings to the row's fields).
    //   - The panel sits in the RAIL, below Guardians (owner, 2026-10-24).
    //   - The actions open the same dialogs the Detail page uses; no new
    //     dialogs are added.

    [TestMethod]
    public void Edit_Enrollment_Panel_Sits_In_The_Rail()
    {
        var src = Load(EditRazorPath);
        // The enrollment panel is Edit-only and NOT collapsible (owner, 2026-10-24): one
        // read-only fact set with its actions in a kebab. It is a relationship record about
        // the student, so it belongs with Guardians in the rail, not with the fields.
        var railMatch = Regex.Match(src, @"<div class=""region-grid__rail""[^>]*>");
        var enrollmentMatch = Regex.Match(src, @"Id=""student-edit-enrollment""");
        railMatch.Success.Should().BeTrue("the rail region exists");
        enrollmentMatch.Success.Should().BeTrue("the enrollment panel exists on the edit page");
        enrollmentMatch.Index.Should().BeGreaterThan(railMatch.Index,
            "the enrollment panel MUST sit inside the rail, below Guardians");
        src.Should().MatchRegex(@"Id=""student-edit-enrollment""\s+Title=""Enrollment""",
            "the enrollment panel is a titled PanelSection (the block keeps its title — it is one of two rail panels)");
    }

    [TestMethod]
    public void Edit_Primary_Active_Enrollment_Is_Displayed_As_ReadOnly_Bands()
    {
        // The primary active enrollment renders as read-only BANDS of chips (owner, 2026-10-25, mock v4):
        // placement first, then the activity-group memberships. Both are <RailBand> now (component-first
        // policy Q22a), so this asserts the CALL SITES — the band's own markup and ARIA are pinned by
        // RailBandComponentTests. A regression that re-introduces an editable control for grade/period on
        // this panel is caught by the negative assertion below.
        var src = Load(EditRazorPath);
        src.Should().Contain("<RailBand Id=\"student-edit-enrollment-facts\" Label=\"Placement\"",
            "the placement band keeps the pinned seam the enrollment display has always had, now as a RailBand call site");
        src.Should().Contain("Variant=\"RailBandVariant.Groups\"",
            "the membership band is the Groups variant, not a hand-tinted div");
        src.Should().Contain("<RailChip",
            "the facts are RailChips — one shape for facts, removable names and blocked options");
        src.Should().NotContain("class=\"rail-band",
            "hand-written band markup on the page is what the component replaced: one band shape, one owner");

        var placementBlock = src[src.IndexOf("student-edit-enrollment-facts", StringComparison.Ordinal)..];
        placementBlock = placementBlock[..placementBlock.IndexOf("student-edit-activity-groups", StringComparison.Ordinal)];
        placementBlock.Should().NotContain("FluentTextInput",
            "placement is displayed, never edited — an input here is the regression this guards");
        placementBlock.Should().NotContain("FluentSelect",
            "the same: grade/period are read-only facts on this panel");
    }

    [TestMethod]
    public void Edit_Enrollment_Panel_Menu_Opens_The_Existing_Dialogs()
    {
        // The Enroll / Transfer / Withdraw items on the Edit page open the same dialogs the
        // Detail page uses. The source must call ShowShellDialogAsync<EnrollStudentDialog,
        // ...> and the other two; no new dialog types are added. The items are the panel's
        // kebab (EnrollmentPanelActions), not a row of buttons inside the block: the block
        // header carries only its own controls (§4).
        var src = Load(EditRazorPath);
        src.Should().Contain("OnEnrollAsync",
            "the Edit page MUST have an OnEnrollAsync handler that opens the EnrollStudentDialog (matches the Detail page's flow)");
        src.Should().Contain("OnTransferAsync",
            "the Edit page MUST have an OnTransferAsync handler that opens the StudentTransferDialog");
        src.Should().Contain("OnWithdrawAsync",
            "the Edit page MUST have an OnWithdrawAsync handler that opens the WithdrawEnrollmentDialog");
        src.Should().Contain("EnrollmentPanelActions",
            "all three are items of EnrollmentPanelActions — the enrollment panel's kebab");
        src.Should().Contain("EnrollStudentDialog",
            "the Enroll item must open the existing EnrollStudentDialog (no new dialog type)");
        src.Should().Contain("StudentTransferDialog",
            "the Transfer item must open the existing StudentTransferDialog (no new dialog type)");
        src.Should().Contain("WithdrawEnrollmentDialog",
            "the Withdraw item must open the existing WithdrawEnrollmentDialog (no new dialog type)");
    }

    [TestMethod]
    public void Edit_Does_Not_Add_GradeLevelId_To_Student_Form_Model()
    {
        // The data model is Student ─< Enrollment >─ GradeLevel. A
        // student does NOT have a direct GradeLevelId on the Student
        // record or on StudentFormModel. If someone tries to "fix" the
        // spec by adding a GradeLevelId to StudentFormModel, the
        // wizard's grade-level binding would silently break (the
        // wizard enrolls via EnrollStudentAsync, not via a direct
        // GradeLevelId field). This test pins the data-model contract.
        var src = Load(EditRazorPath);
        src.Should().NotContain("Student.GradeLevelId",
            "Student has no direct GradeLevelId — the grade lives on a StudentEnrollment row. The Edit page must read it via ListEnrollmentsByStudentAsync, not via a Student.GradeLevelId property");
    }

    [TestMethod]
    public void Edit_Loads_Enrollments_For_Primary_Active_Display()
    {
        // The Edit page must load the student's enrollments on init so
        // the primary active enrollment can be displayed. Without this
        // load the panel would be permanently empty. The lookup is
        // ListEnrollmentsByStudentAsync (the same one Detail.razor uses),
        // and we also build the per-grade and per-period name lookups
        // so the panel can show "Grade: GradeName" + "Period: PeriodName"
        // instead of GUIDs.
        var src = Load(EditRazorPath);
        src.Should().Contain("ListEnrollmentsByStudentAsync",
            "OnInitializedAsync MUST call ListEnrollmentsByStudentAsync so the primary active enrollment is available to display");
        src.Should().Contain("PrimaryActiveEnrollment",
            "the Edit page must compute PrimaryActiveEnrollment (most recent Status==Active, ExitDate==null) and use it to drive the display + enable the Transfer/Withdraw items");
        src.Should().Contain("ListGradeLevelsAsync",
            "OnInitializedAsync MUST call ListGradeLevelsAsync to build the grade-name lookup for the read-only display");
        src.Should().Contain("ListPeriodsAsync",
            "OnInitializedAsync MUST call ListPeriodsAsync to build the period-name lookup for the read-only display");
    }

    [TestMethod]
    public void GradeLevelWizard_Step2_Shows_Read_Only_Enrolment_Target_Card()
    {
        // REMOVED: The GradeLevelWizard was replaced by a single-page
        // GradeLevelFormFields component (spec §3). Enrolment target card
        // is no longer part of the grade-level management UX.
    }
}
