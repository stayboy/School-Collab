using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level regression tests for the
/// <see cref="StudentFormFieldsActionsPlacement"/> feature.
///
/// The shared <c>&lt;StudentFormFields&gt;</c> component used to render
/// the Save / Cancel buttons in a horizontal row BELOW the form fields,
/// separated by a CSS <c>border-top</c> on <c>.form-actions</c>. The
/// student edit page (<c>Edit.razor</c>) is a long form (identity + DOB
/// + gender + guardians + direct contact), and a bottom action bar
/// scrolls off-screen on a laptop viewport.
///
/// A new <c>ActionsPlacement</c> parameter (with values
/// <c>Bottom</c> and <c>Right</c>) was added. The Edit page opts in to
/// <c>Right</c>, which renders the action buttons in a sticky vertical
/// sidebar to the right of the form fields. All other consumers
/// (Create.razor, the inline GradeLevelWizard "new student" form) keep
/// the default <c>Bottom</c> placement and the original byte-equivalent
/// markup.
///
/// These tests guard the feature:
///   - the enum + parameter are defined and the default is <c>Bottom</c>
///   - the layout wrapper markup is always present (so the bottom
///     placement is byte-equivalent in CSS-only behavior, not markup)
///   - the <c>.form-actions</c> action row is always present
///   - <c>Edit.razor</c> opts in to <c>Right</c>
///   - <c>Create.razor</c> and <c>GradeLevelWizard.razor</c> keep the
///     default <c>Bottom</c> placement
///   - the new CSS rules are all present
///   - every new class used in the markup has a matching CSS rule
/// </summary>
[TestClass]
public class StudentFormFieldsActionsPlacementTests
{
    private static string ReadStudentFormFieldsSource()
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir,
            "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Admin",
            "Components", "Students", "StudentFormFields.razor"));
        File.Exists(srcPath).Should().BeTrue(
            $"StudentFormFields.razor should exist at '{srcPath}'");
        return File.ReadAllText(srcPath);
    }

    private static string ReadStudentFormFieldsCss()
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir,
            "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Admin",
            "Components", "Students", "StudentFormFields.razor.css"));
        File.Exists(srcPath).Should().BeTrue(
            $"StudentFormFields.razor.css should exist at '{srcPath}'");
        return File.ReadAllText(srcPath);
    }

    private static string ReadConsumerSource(string relativePath)
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir,
            "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Admin",
            relativePath));
        File.Exists(srcPath).Should().BeTrue(
            $"{relativePath} should exist at '{srcPath}'");
        return File.ReadAllText(srcPath);
    }

    [TestMethod]
    public void StudentFormFields_Declares_ActionsPlacement_Enum_With_Bottom_And_Right()
    {
        var source = ReadStudentFormFieldsSource();
        source.Should().Contain("public enum StudentFormActionsPlacement",
            "the enum is the type of the ActionsPlacement parameter");
        source.Should().Contain("StudentFormActionsPlacement.Bottom",
            "the enum must have a Bottom member (the default placement)");
        source.Should().Contain("StudentFormActionsPlacement.Right",
            "the enum must have a Right member (the sidebar placement)");
    }

    [TestMethod]
    public void StudentFormFields_ActionsPlacement_Parameter_Defaults_To_Bottom()
    {
        var source = ReadStudentFormFieldsSource();
        // Match the parameter declaration with its default initializer.
        var match = Regex.Match(
            source,
            @"public\s+StudentFormActionsPlacement\s+ActionsPlacement\s*\{\s*get;\s*set;\s*\}\s*=\s*StudentFormActionsPlacement\.(\w+);",
            RegexOptions.Singleline);
        match.Success.Should().BeTrue(
            "the ActionsPlacement parameter must have an explicit default initializer so existing callers are unaffected");
        match.Groups[1].Value.Should().Be("Bottom",
            "the default ActionsPlacement must be Bottom (the original behavior) so existing callers don't silently opt in to the sidebar");
    }

    [TestMethod]
    public void StudentFormFields_Markup_Always_Renders_Layout_Wrapper_And_Sidebar_Slot()
    {
        // The wrapper (.student-form-fields__layout > .student-form-fields__fields
        // + .student-form-fields__sidebar) is ALWAYS rendered in the markup.
        // The Bottom vs. Right difference is purely a CSS-class toggle on the
        // <EditForm> (.student-form-fields vs .student-form-fields--sidebar),
        // not a markup conditional. This keeps the Razor markup a single
        // shape and avoids the @if-with-raw-tag-closing hazard.
        var source = ReadStudentFormFieldsSource();
        source.Should().Contain("class=\"student-form-fields__layout\"",
            "the layout wrapper is always rendered; the Bottom vs Right toggle is a CSS class on the EditForm");
        source.Should().Contain("class=\"student-form-fields__fields\"",
            "the fields slot is always rendered inside the wrapper");
        source.Should().Contain("class=\"student-form-fields__sidebar\"",
            "the sidebar slot is always rendered inside the wrapper");
    }

    [TestMethod]
    public void StudentFormFields_FormClass_Helper_Composes_Sidebar_Class()
    {
        // The class attribute on the rendered <EditForm> is computed in C#
        // via the FormClass helper because the EditForm component's
        // tag-helper parser doesn't tolerate the
        // class="... @(cond ? ... : null)" pattern (it emits RZ9986).
        // The helper must add the --sidebar modifier when ActionsPlacement
        // is Right, and omit it when Bottom.
        var source = ReadStudentFormFieldsSource();
        source.Should().Contain("private string FormClass =>",
            "the class attribute on the rendered <EditForm> is composed by a private helper (not by a Razor expression in the attribute)");
        source.Should().Contain("student-form-fields--sidebar",
            "FormClass must include the --sidebar modifier when ActionsPlacement is Right");
    }

    [TestMethod]
    public void StudentFormFields_ActionRow_Class_Includes_Sidebar_Modifier_When_Right()
    {
        // The class on the .form-actions div is also composed in C#
        // (FormActionsClass helper) for the same tag-helper parsing
        // reason. It must add the .form-actions--sidebar modifier when
        // ActionsPlacement is Right.
        var source = ReadStudentFormFieldsSource();
        source.Should().Contain("private string FormActionsClass",
            "the .form-actions class attribute is composed by a private helper (not by a Razor expression)");
        source.Should().Contain("parts.Add(\"form-actions--sidebar\")",
            "FormActionsClass must append the --sidebar modifier when ActionsPlacement is Right");
        source.Should().Contain("parts.Add(\"form-actions--right\")",
            "FormActionsClass must also respect the ActionsAlignment parameter (Left/Right alignment) for backward compatibility");
    }

    [TestMethod]
    public void StudentFormFields_ActionRow_Markup_Is_Preserved_With_Sidebar_Class()
    {
        // The action row markup is unchanged from the original: a single
        // <div class="form-actions ..."> wrapping the existing FluentStack
        // with Submit + Cancel buttons. The new modifier is added via
        // the FormActionsClass helper.
        var source = ReadStudentFormFieldsSource();
        source.Should().Contain("class=\"form-actions @FormActionsClass\"",
            "the .form-actions div now uses the FormActionsClass helper to compose its class list (no inline @(cond ? ... : null) in the attribute)");
        source.Should().Contain("FluentStack Orientation=\"Orientation.Horizontal\" Spacing=\"8\"",
            "the built-in Submit + Cancel buttons still render in a FluentStack (the FluentStack itself is restyled by the sidebar CSS to stack vertically)");
        source.Should().Contain("class=\"form-actions__button\"",
            "the buttons carry a form-actions__button class so the sidebar CSS can target them for full-width styling");
    }

    [TestMethod]
    public void Edit_Page_Opts_In_To_Right_ActionsPlacement()
    {
        // Edit.razor is the only consumer that opts in to the sidebar
        // placement. Create.razor and the inline GradeLevelWizard form
        // keep the default Bottom placement (see the other tests).
        var source = ReadConsumerSource("Components/Pages/Students/Edit.razor");
        source.Should().Contain("ActionsPlacement=\"StudentFormFields.StudentFormActionsPlacement.Right\"",
            "Edit.razor must opt in to the Right ActionsPlacement so Save/Cancel render in a sticky right-side sidebar");
    }

    [TestMethod]
    public void Create_Page_Keeps_Default_Bottom_ActionsPlacement()
    {
        // Create.razor is a short form (only identity + DOB + gender);
        // a bottom action bar is appropriate. Assert it does NOT opt in
        // to Right — the default Bottom placement is correct here.
        var source = ReadConsumerSource("Components/Pages/Students/Create.razor");
        // The student-number is editable in Create mode (ReadOnlyStudentNumber
        // defaults to false), so we anchor on ActionsPlacement rather than
        // the student-number.
        var hasActionsPlacement = source.Contains("ActionsPlacement=\"", StringComparison.Ordinal);
        hasActionsPlacement.Should().BeFalse(
            "Create.razor must keep the default Bottom ActionsPlacement — the form is short and a bottom action bar fits well");
    }

    [TestMethod]
    public void GradeLevelWizard_Keeps_Default_Bottom_ActionsPlacement()
    {
        // The inline "new student" form inside the GradeLevelWizard is a
        // side-by-side wizard step, not a full-page form. A sidebar
        // action bar would compete with the wizard's own Back/Next
        // footer. Keep Bottom.
        var source = ReadConsumerSource("Components/Pages/Students/GradeLevels/GradeLevelWizard.razor");
        var hasActionsPlacement = source.Contains("ActionsPlacement=\"", StringComparison.Ordinal);
        hasActionsPlacement.Should().BeFalse(
            "GradeLevelWizard.razor must keep the default Bottom ActionsPlacement — the wizard has its own Back/Next footer that a sidebar would compete with");
    }

    [TestMethod]
    public void StudentFormFields_CSS_Defines_Layout_And_Sidebar_Rules()
    {
        // The CSS adds four new rules for the sidebar placement. Each
        // must be present.
        var css = ReadStudentFormFieldsCss();
        css.Should().Contain(".student-form-fields__layout",
            "the .student-form-fields__layout rule is required (display:contents by default)");
        css.Should().Contain(".student-form-fields__fields",
            "the .student-form-fields__fields rule is required (min-width:0 + flex column)");
        css.Should().Contain(".student-form-fields__sidebar",
            "the .student-form-fields__sidebar rule is required (display:contents by default, sticky in sidebar mode)");
        css.Should().Contain(".form-actions--sidebar",
            "the .form-actions--sidebar rule is required (vertical stack + no separator)");
        css.Should().Contain(".form-actions__button",
            "the .form-actions__button rule is required (full-width buttons in the sidebar)");
        css.Should().Contain(".student-form-fields--sidebar",
            "the .student-form-fields--sidebar grid layout is required (the .student-form-fields--sidebar modifier flips the wrapper to display:grid)");
    }

    [TestMethod]
    public void StudentFormFields_CSS_Has_Narrow_Viewport_Collapse()
    {
        // On viewports < 900px, the sidebar should collapse back to the
        // bottom-bar treatment. The original behavior must hold on
        // phones / narrow windows.
        var css = ReadStudentFormFieldsCss();
        css.Should().Contain("@media (max-width: 900px)",
            "the @media query that collapses the sidebar on narrow viewports is required so the form stays usable on phones / narrow windows");
        css.Should().MatchRegex(@"@media\s*\(max-width:\s*900px\)\s*\{[^}]*\.student-form-fields--sidebar[^}]*grid-template-columns:\s*minmax",
            "the narrow-viewport media query must collapse the grid-template-columns to a single track");
    }

    [TestMethod]
    public void StudentFormFields_CSS_Has_Sticky_Behavior()
    {
        // The sidebar must be position:sticky so the Save/Cancel buttons
        // stay visible as the user scrolls the form on a laptop viewport.
        var css = ReadStudentFormFieldsCss();
        css.Should().Contain("position: sticky",
            "the sidebar slot must be position:sticky so the Save/Cancel buttons stay visible while scrolling");
        css.Should().Contain("top: 16px",
            "the sticky offset (16px) places the sidebar just below the page header on a typical viewport");
    }
}
