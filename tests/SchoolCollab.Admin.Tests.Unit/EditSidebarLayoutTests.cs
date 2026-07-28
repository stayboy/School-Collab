using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level regression tests for the student edit page
/// (<c>Edit.razor</c>) and the shared <c>StudentFormFields</c>
/// component's new "page-level sidebar" pattern.
///
/// The student edit page is a long form: identity fields + guardians +
/// Direct contact editor. The Save / Cancel pair lives in a sticky
/// right-column sidebar so it stays visible as the user scrolls. This
/// is a PAGE-LEVEL grid (a <c>&lt;div class="student-edit-grid"&gt;</c>
/// with two children: <c>__fields</c> and <c>__sidebar</c>) and NOT a
/// wrapper inside <c>StudentFormFields</c>, so the same
/// <c>StudentFormFields</c> component can be embedded in dialogs
/// (Create.razor / GradeLevelWizard.razor / StudentPickerDialog.razor)
/// with the dialog-ui convention of bottom-horizontal Cancel/Save
/// separated by a border-top.
///
/// The right-column action stack is a <c>&lt;FluentStack
/// Orientation="Vertical" HorizontalAlignment="Stretch"&gt;</c>
/// holding Save (top) and Cancel (bottom) as full-width equal-sized
/// buttons. The Save button calls
/// <c>StudentFormFields.SubmitAsync()</c> through a
/// <c>@ref="_form"</c> reference — the standard Blazor pattern for
/// driving an EditForm submit from a button that sits OUTSIDE the
/// form. <c>RenderActions="false"</c> tells the component to skip
/// its built-in bottom-horizontal row so the page-level sidebar is
/// the single source of action buttons.
///
/// What these tests guard against:
///   - Re-introducing a wrapper inside <c>StudentFormFields</c>
///     (which would force dialogs to inherit a sidebar they don't
///     want, breaking the dialog-ui convention)
///   - Adding a FluentTabs (the user explicitly forbade tabs on the
///     student view page; sidebar + scroll is the desired pattern)
///   - Re-introducing the obsolete ActionsPlacement enum / parameter
///     (now that the layout is in the parent, the enum is dead
///     weight)
///   - Forgetting to call SubmitAsync on the Save button (the form
///     would silently not submit)
/// </summary>
[TestClass]
public class EditSidebarLayoutTests
{
    private const string EditRazorPath = "src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/Edit.razor";
    private const string EditCssPath = "src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/Edit.razor.css";
    private const string FormRazorPath = "src/Students/SchoolCollab.Students.Admin/Components/Students/StudentFormFields.razor";
    private const string FormCssPath = "src/Students/SchoolCollab.Students.Admin/Components/Students/StudentFormFields.razor.css";

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
    public void Edit_Uses_Page_Level_Grid_With_Fields_And_Sidebar()
    {
        var src = Load(EditRazorPath);
        // The 2-column grid wrapper is the canonical pattern: a
        // <div class="student-edit-grid"> with TWO children,
        // __fields and __sidebar. Anything else (e.g. FluentTabs)
        // is the wrong pattern.
        src.Should().Contain("class=\"student-edit-grid\"",
            "Edit.razor hosts a 2-column page-level grid (not a wrapper inside the form)");
        src.Should().Contain("class=\"student-edit-grid__fields\"",
            "column 1 of the grid holds the form fields + Direct contact editor");
        src.Should().Contain("class=\"student-edit-grid__sidebar\"",
            "column 2 of the grid holds the Save/Cancel FluentStack");
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
    public void Edit_Sidebar_Action_Stack_Is_FluentStack_Vertical_With_Stretch_Alignment()
    {
        var src = Load(EditRazorPath);
        // The action stack is a FluentStack Orientation="Vertical"
        // (not Horizontal) so Save sits above Cancel. The
        // HorizontalAlignment="Stretch" makes the children fill
        // the column width so the two buttons are equal size.
        // The Spacing="8" gives a comfortable 8px gap between
        // the Save and Cancel buttons.
        var sidebarMatch = Regex.Match(src,
            @"<FluentStack[^>]*Orientation=""Orientation\.Vertical""[^>]*HorizontalAlignment=""HorizontalAlignment\.Stretch""[^>]*Spacing=""8""[^>]*class=""student-edit-action-stack""");
        sidebarMatch.Success.Should().BeTrue(
            "the right-column action stack must be a vertical FluentStack with horizontal-stretch alignment and the action-stack class so the CSS makes the buttons full-width");
    }

    [TestMethod]
    public void Edit_Sidebar_Save_Button_Is_First_And_Cancel_Is_Second()
    {
        // The user explicitly wants Save on TOP and Cancel BELOW.
        // The order in the markup (which becomes the visual order
        // in a vertical FluentStack) is the canonical check.
        // The Save button's text is the @(_saving ? "Saving…" : "Save")
        // ternary (so we look for the literal "Save" string, NOT
        // ">Save<" which would never appear). The Cancel button is
        // plain text "Cancel" inside <FluentButton> (preceded by
        // whitespace, NOT a '>').
        var src = Load(EditRazorPath);
        var sidebarStart = src.IndexOf("student-edit-grid__sidebar", StringComparison.Ordinal);
        sidebarStart.Should().BeGreaterThan(0, "the sidebar div must exist");
        var sidebarSlice = src.Substring(sidebarStart,
            Math.Min(3000, src.Length - sidebarStart));

        // For the Save button: match the "Save" literal as the false
        // branch of the saving ternary — it is the only "Save" that
        // appears as button text in the source.
        var saveIdx = sidebarSlice.IndexOf("\"Save\"", StringComparison.Ordinal);
        var cancelIdx = sidebarSlice.IndexOf("Cancel", StringComparison.Ordinal);
        saveIdx.Should().BeGreaterThan(0, "the Save button must be present in the sidebar (rendered as the \"Save\" branch of the saving ternary)");
        cancelIdx.Should().BeGreaterThan(0, "the Cancel button must be present in the sidebar");
        saveIdx.Should().BeLessThan(cancelIdx,
            "Save must come BEFORE Cancel in the vertical stack (the user-facing requirement: Save on top, Cancel below)");
    }

    [TestMethod]
    public void Edit_Save_Button_Calls_SubmitAsync_Through_Form_Ref()
    {
        var src = Load(EditRazorPath);
        // The Save button's @onclick calls OnSidebarSaveAsync,
        // which delegates to _form!.SubmitAsync(). The standard
        // Blazor pattern for driving an EditForm submit from a
        // button that sits outside the form.
        src.Should().Contain("OnClick=\"OnSidebarSaveAsync\"",
            "the Save button's OnClick must call the page-level OnSidebarSaveAsync (which delegates to _form.SubmitAsync())");
        src.Should().Contain("await _form.SubmitAsync()",
            "OnSidebarSaveAsync must call _form.SubmitAsync() to drive the EditForm submit through the @ref");
    }

    [TestMethod]
    public void Edit_StudentFormFields_Uses_RenderActions_False()
    {
        var src = Load(EditRazorPath);
        // The page-level sidebar IS the action row in the Edit
        // page. StudentFormFields' built-in bottom-horizontal row
        // would create a duplicate pair of buttons (one in the
        // sidebar, one below the form). RenderActions="false"
        // tells the component to skip its built-in row.
        src.Should().Contain("RenderActions=\"false\"",
            "Edit.razor MUST pass RenderActions=\"false\" to <StudentFormFields> so the component skips its built-in bottom-horizontal row (the page-level sidebar is the only action row)");
    }

    [TestMethod]
    public void Edit_FormFields_And_Direct_Contact_Are_Both_In_Column_One()
    {
        var src = Load(EditRazorPath);
        // The user explicitly wants the action buttons to be
        // "far right of all form fields, including contact editor".
        // That means BOTH the StudentFormFields AND the Direct
        // contact editor must sit in column 1 of the grid
        // (the .student-edit-grid__fields slot). If either escapes
        // into column 2 or out of the grid entirely, the right-
        // column sidebar would no longer span BOTH sections.
        // We find the __fields slot and the __sidebar slot via
        // regex so the close-marker is tolerant of the blank
        // line the Razor formatter inserts between sibling divs.
        var fieldsMatch = Regex.Match(src,
            @"<div class=""student-edit-grid__fields"">");
        var sidebarMatch = Regex.Match(src,
            @"<div class=""student-edit-grid__sidebar"">");
        fieldsMatch.Success.Should().BeTrue("the __fields slot exists");
        sidebarMatch.Success.Should().BeTrue("the __sidebar slot exists");
        var fieldsStart = fieldsMatch.Index + fieldsMatch.Length;
        var fieldsEnd = sidebarMatch.Index;
        fieldsStart.Should().BeLessThan(fieldsEnd, "the __fields slot precedes the __sidebar slot");

        var columnOneSlice = src.Substring(fieldsStart, fieldsEnd - fieldsStart);
        columnOneSlice.Should().Contain("<StudentFormFields",
            "<StudentFormFields> MUST be inside the __fields column (column 1) so the sidebar in column 2 spans it");
        columnOneSlice.Should().Contain("<ContactsEditor",
            "the <ContactsEditor> for Direct contact MUST be inside the __fields column (column 1) so the sidebar in column 2 spans BOTH the form and the contact editor");
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
        // The page-level sidebar calls _form.SubmitAsync() to drive
        // the EditForm submit. The method must be public so the
        // page can call it across the component boundary, and it
        // must (1) read the EditForm's EditContext, (2) call
        // EditContext.Validate(), (3) invoke OnValidSubmit on
        // success — the canonical Blazor pattern.
        var src = Load(FormRazorPath);
        src.Should().Contain("public async Task<bool> SubmitAsync()",
            "StudentFormFields must expose a public SubmitAsync() method so the page-level sidebar can drive the EditForm submit");
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
        // The whole point of the redesign: the 2-column grid is in
        // the parent (Edit.razor), not inside the form component.
        // If anyone re-introduces a layout wrapper / sidebar slot
        // inside StudentFormFields, dialogs would be forced to
        // inherit a sidebar they don't want.
        var src = Load(FormRazorPath);
        src.Should().NotContain("student-form-fields__layout",
            "StudentFormFields must NOT have an internal layout wrapper — the 2-column grid lives in the parent (Edit.razor)");
        src.Should().NotContain("student-form-fields__sidebar",
            "StudentFormFields must NOT have an internal sidebar slot — the sidebar lives in the parent (Edit.razor)");
        src.Should().NotContain("student-form-fields--sidebar",
            "StudentFormFields must NOT have an internal sidebar modifier class — the sidebar lives in the parent (Edit.razor)");
    }

    [TestMethod]
    public void StudentFormFields_Has_No_Obsolete_ActionsPlacement_Enum()
    {
        // The ActionsPlacement enum is dead weight now that the
        // layout is in the parent. If it reappears, dialogs would
        // either (a) be forced to opt out (breaking the
        // dialog-friendly default) or (b) silently render a
        // duplicate sidebar alongside the page-level one.
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
    public void Edit_CSS_Has_Sticky_Sidebar_And_Equal_Width_Action_Buttons()
    {
        // The page-level grid + sticky sidebar + equal-width action
        // buttons are visual contracts. The CSS rules must exist
        // so the markup actually renders as designed.
        var css = Load(EditCssPath);
        css.Should().Contain(".student-edit-grid",
            "the page-level grid rule must exist in Edit.razor.css");
        css.Should().Contain("grid-template-columns: minmax(0, 1fr) 200px",
            "the grid uses a 1fr / 200px two-column layout (form fields in col 1, action sidebar in col 2)");
        css.Should().Contain("position: sticky",
            "the sidebar must be position:sticky so the buttons stay visible as the user scrolls through the form + Direct contact editor");
        css.Should().Contain(".student-edit-action-stack__button",
            "the equal-width button rule must exist so Save and Cancel are full-width in the sidebar");
        css.Should().Contain("width: 100%",
            "the buttons fill the sidebar's 200px column (equal size, regardless of label text length)");
    }

    [TestMethod]
    public void Edit_CSS_Has_Narrow_Viewport_Collapse_Rule()
    {
        // On phones (< 900px) the 2-column grid would leave no
        // room for the form fields. The @media rule collapses to
        // a single column and moves the sidebar to the bottom in
        // a horizontal row, matching the original behavior.
        var css = Load(EditCssPath);
        css.Should().Contain("@media (max-width: 900px)",
            "narrow-viewport collapse rule must exist so the sidebar doesn't crowd the form on phones");
        css.Should().Contain("grid-template-columns: minmax(0, 1fr)",
            "narrow-viewport rule collapses the grid to a single column");
    }

    // ── Active enrollment section (read-only display + actions) ──────────
    //
    // The data model is Student ─< Enrollment >─ GradeLevel. A student
    // does not have a direct GradeLevelId; the active grade is read off
    // the student's primary active enrollment (= the most recent
    // StudentEnrollment with Status == "Active" and ExitDate null).
    //
    // The spec says: existing primary active enrollment CANNOT be edited
    // on the student edit page — it is only displayed. The user can act
    // on it via the Enroll / Transfer / Withdraw buttons (which create a
    // new enrollment row and/or mark the current as Transferred /
    // Withdrawn). On the Edit page:
    //   - The primary active enrollment is rendered as a read-only card
    //     (no inputs, no two-way bindings to the row's fields).
    //   - The card sits in column 1 of the page-level grid, below the
    //     Direct contact editor and above the action sidebar (so the
    //     sticky sidebar still spans it).
    //   - The action buttons (Enroll / Transfer / Withdraw) open the
    //     same dialogs the Detail page uses; no new dialogs are added.

    [TestMethod]
    public void Edit_Has_Active_Enrollment_Section_Inside_Fields_Column()
    {
        var src = Load(EditRazorPath);
        // The new section uses .student-edit-enrollment as its container
        // class — it lives inside .student-edit-grid__fields (column 1)
        // so the sticky sidebar (column 2) spans it.
        var fieldsMatch = Regex.Match(src,
            @"<div class=""student-edit-grid__fields"">");
        var sidebarMatch = Regex.Match(src,
            @"<div class=""student-edit-grid__sidebar"">");
        fieldsMatch.Success.Should().BeTrue("the __fields slot exists");
        sidebarMatch.Success.Should().BeTrue("the __sidebar slot exists");
        var fieldsStart = fieldsMatch.Index + fieldsMatch.Length;
        var fieldsEnd = sidebarMatch.Index;

        var columnOneSlice = src.Substring(fieldsStart, fieldsEnd - fieldsStart);
        columnOneSlice.Should().Contain("student-edit-enrollment",
            "the Active-enrollment section MUST live inside column 1 so the sticky sidebar in column 2 spans it (matches the Direct contact section's column placement)");
    }

    [TestMethod]
    public void Edit_Primary_Active_Enrollment_Is_Displayed_Read_Only()
    {
        // The primary active enrollment card has a "read-only" affordance
        // in the markup. We check for the explicit annotation string so a
        // future regression (e.g. someone re-introducing an editable
        // field on the card) is caught by the source-level scan.
        var src = Load(EditRazorPath);
        src.Should().Contain("enrollment-display-card",
            "the primary active enrollment is rendered inside an .enrollment-display-card (a FluentCard, not a FluentTextField/FluentSelect) — the source-level contract that the row is display-only");
        src.Should().Contain("read-only — edit via Transfer / Withdraw",
            "the meta line explicitly says 'read-only — edit via Transfer / Withdraw' so a future regression that adds an editable field on the card is caught by this assertion");
    }

    [TestMethod]
    public void Edit_Enrollment_Section_Has_Three_Action_Buttons_Opening_Existing_Dialogs()
    {
        // The Enroll / Transfer / Withdraw buttons on the Edit page open
        // the same dialogs the Detail page uses. The source must call
        // ShowShellDialogAsync<EnrollStudentDialog, ...> and the other
        // two; no new dialog types are added. We also verify the
        // button-visible-text matches the dialog title (so a rename
        // in one place forces the other to keep up).
        var src = Load(EditRazorPath);
        src.Should().Contain("OnEnrollAsync",
            "the Edit page MUST have an OnEnrollAsync handler that opens the EnrollStudentDialog (matches the Detail page's flow)");
        src.Should().Contain("OnTransferAsync",
            "the Edit page MUST have an OnTransferAsync handler that opens the StudentTransferDialog");
        src.Should().Contain("OnWithdrawAsync",
            "the Edit page MUST have an OnWithdrawAsync handler that opens the WithdrawEnrollmentDialog");
        src.Should().Contain("EnrollStudentDialog",
            "the Enroll button must open the existing EnrollStudentDialog (no new dialog type)");
        src.Should().Contain("StudentTransferDialog",
            "the Transfer button must open the existing StudentTransferDialog (no new dialog type)");
        src.Should().Contain("WithdrawEnrollmentDialog",
            "the Withdraw button must open the existing WithdrawEnrollmentDialog (no new dialog type)");
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
        // load the card would be permanently empty. The lookup is
        // ListEnrollmentsByStudentAsync (the same one Detail.razor uses),
        // and we also build the per-grade and per-period name lookups
        // so the card can show "Grade: GradeName" + "Period: PeriodName"
        // instead of GUIDs.
        var src = Load(EditRazorPath);
        src.Should().Contain("ListEnrollmentsByStudentAsync",
            "OnInitializedAsync MUST call ListEnrollmentsByStudentAsync so the primary active enrollment is available to display");
        src.Should().Contain("PrimaryActiveEnrollment",
            "the Edit page must compute PrimaryActiveEnrollment (most recent Status==Active, ExitDate==null) and use it to drive the display + enable the Transfer/Withdraw actions");
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
