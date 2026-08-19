using System;
using System.IO;
using System.Reflection;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level tests for the <see cref="SchoolCollab.Admin.Shared.Components.FormRow"/>
/// Orientation parameter (plan 2026-08-18-formrow-orientation-parameter.md).
///
/// <para>
/// FormRow used to auto-stack its label above its input cell via a
/// <c>@media (max-width: 720px)</c> media query in <c>FormRow.razor.css</c>
/// silently flipping any row hosted inside a narrow viewport, including a
/// 420px side drawer hosted inside a wide desktop browser. The Orientation
/// parameter (Horizontal | Vertical) makes the layout explicit at the call
/// site instead of a viewport side-effect.
/// </para>
///
/// <para>
/// These tests assert at the SOURCE level that:
/// <list type="bullet">
///   <item><see cref="SchoolCollab.Admin.Shared.Components.RowOrientation"/> enum exists with
///         <c>Horizontal</c> + <c>Vertical</c> members.</item>
///   <item>FormRow declares an <c>Orientation</c> parameter defaulting to
///         <c>RowOrientation.Horizontal</c>.</item>
///   <item>The markup renders BOTH <c>form-row--horizontal</c> /
///         <c>form-row--vertical</c> modifier classes so callers can pick.</item>
///   <item><c>FormRow.razor.css</c> no longer has the auto-stack
///         <c>@media (max-width: 720px)</c> media query that used to override
///         explicit Horizontal callers below 720px.</item>
///   <item><c>ContactsEditor.razor</c> Edit view passes
///         <c>Orientation="RowOrientation.Vertical"</c> on every FormRow in
///         the drawer.</item>
/// </list>
/// </para>
/// </summary>
[TestClass]
public class FormRowOrientationTests
{
    private static string ReadSource(string relative)
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir, "..", "..", "..", "..", "..", "src", relative));
        File.Exists(srcPath).Should().BeTrue(
            $"source should exist at '{srcPath}' check the path resolution");
        return File.ReadAllText(srcPath);
    }

    private static string ReadFormRowSource() => ReadSource(
        "SchoolCollab.Admin.Shared/Components/FormRow.razor");

    private static string ReadFormRowCssSource() => ReadSource(
        "SchoolCollab.Admin.Shared/Components/FormRow.razor.css");

    private static string ReadRowOrientationSource() => ReadSource(
        "SchoolCollab.Admin.Shared/Components/RowOrientation.cs");

    private static string ReadContactsEditorSource() => ReadSource(
        "SchoolCollab.Admin.Shared/Components/ContactsEditor.razor");

    private static string ReadContactFormFieldsSource() => ReadSource(
        "SchoolCollab.Admin.Shared/Components/ContactFormFields.razor");

    // ---- Enum exists with Horizontal + Vertical ----

    [TestMethod]
    public void RowOrientation_EnumExistsWithHorizontalAndVerticalValues()
    {
        var source = ReadRowOrientationSource();

        // The enum must live in the Shared components namespace alongside
        // FormRow and FieldWidth (same naming-convention family).
        source.Should().Contain("namespace SchoolCollab.Admin.Shared.Components",
            "the enum lives in the same namespace as FormRow (consumers can fully-qualify or @using)");
        source.Should().Contain("public enum RowOrientation",
            "the enum is public so Blazor components can reference it by name");

        source.Should().Contain("Horizontal,",
            "Horizontal is one of the orientation values preserves the canonical label-left + input-right layout");
        source.Should().Contain("Vertical,",
            "Vertical is the other orientation explicit label-top + input-bottom for narrow surfaces");
    }

    // ---- Orientation parameter on FormRow ----

    [TestMethod]
    public void FormRow_DeclaresOrientationParameterDefaultingToHorizontal()
    {
        var source = ReadFormRowSource();

        source.Should().Contain("[Parameter] public RowOrientation Orientation { get; set; } = RowOrientation.Horizontal;",
            "FormRow exposes Orientation as a Parameter that defaults to Horizontal so every existing caller renders identically");

        // Documentation: the parameter should be documented in the
        // header comment so future contributors see why it exists and
        // when to use Vertical.
        source.Should().Contain("Row orientation. <see cref=\"RowOrientation.Horizontal\"/> (default)",
            "the Orientation parameter has a doc comment explaining the default and when to override");
    }

    [TestMethod]
    public void FormRow_MarkupRendersOrientationModifierClassOnRoot()
    {
        var source = ReadFormRowSource();

        // The root <div> must include a modifier class derived from the
        // Orientation parameter so the CSS can route Horizontal vs Vertical
        // rules to the correct selector.
        source.Should().Contain("form-row--@(Orientation.ToString().ToLowerInvariant())",
            "the root .form-row element renders a form-row--{orientation} modifier class");

        // The classic AlignTop modifier must still be additive (composes
        // with the orientation modifier on the same element).
        source.Should().Contain("form-row--align-top",
            "the align-top modifier still composes with the orientation modifier on the root element");
    }

    // ---- CSS: modifier classes present, old media query gone ----

    [TestMethod]
    public void FormRowCss_DefinesHorizontalAndVerticalModifierClasses()
    {
        var source = ReadFormRowCssSource();

        // Horizontal: the canonical label-left + input-right row.
        source.Should().Contain(".form-row--horizontal",
            "the CSS defines a .form-row--horizontal modifier class for Horizontal orientation");
        source.Should().Contain("flex-direction: row",
            "Horizontal orientation lays out as a flex row");

        // Vertical: the explicit label-top + input-bottom row.
        source.Should().Contain(".form-row--vertical",
            "the CSS defines a .form-row--vertical modifier class for Vertical orientation");
        source.Should().Contain("flex-direction: column",
            "Vertical orientation lays out as a flex column");

        // The 180px label column is HORIZONTAL-only. Vertical uses 100%.
        source.Should().Contain(".form-row--horizontal .form-row-label",
            "the 180px fixed label column is scoped to Horizontal (Vertical is full-width)");
        source.Should().Contain(".form-row--vertical .form-row-label",
            "the Vertical label cell is full-width and matches the row");
    }

    [TestMethod]
    public void FormRowCss_DropsViewportBasedAutoStackMediaQuery()
    {
        var source = ReadFormRowCssSource();

        // Pre-2026-08-18: a @media (max-width: 720px) block silently flipped
        // every row to a column on phones AND on a 420px drawer inside a
        // desktop browser. The whole reason this refactor exists is to
        // remove that side-effect Orientation is the only way to stack.
        // The historical "@media (max-width: 720px)" string may appear in
        // the file's HEADER COMMENT as historical context — that's fine.
        // What MUST NOT survive is a LIVE @media rule on its own line.
        System.Text.RegularExpressions.Regex.IsMatch(
            source, @"^\s*@media\s*\(\s*max-width\s*:\s*720px\s*\)",
            System.Text.RegularExpressions.RegexOptions.Multiline
        ).Should().BeFalse(
            "the auto-stack viewport media query is removed; callers must use Orientation=Vertical explicitly");

        // The file should now be media-query-free altogether Orientation
        // is the only path to a vertical row. Note the file MAY mention
        // "@media" in comments for historical context — a live RULE is
        // what would silently override callers.
        System.Text.RegularExpressions.Regex.IsMatch(
            source, @"(?m)^\s*@media\b",
            System.Text.RegularExpressions.RegexOptions.Singleline
        ).Should().BeFalse(
            "FormRow.razor.css has no live @media rules Orientation is the only path to a vertical row");
    }

    // ---- Caller consistency: the shared Edit-view field group is Vertical ----

    [TestMethod]
    public void ContactsEditor_EditViewPassesVerticalOrientationToEveryFormRow()
    {
        // The Add and Edit forms in the focused Edit view share ONE field
        // group (ContactFormFields) — Channel (+country code, combined under
        // the same label) / Value / Label. The Vertical orientation for the
        // narrow 420px drawer lives on those FormRows in ContactFormFields.razor
        // rather than being duplicated inline in the Add/Edit branches (so the
        // two can never drift apart).
        var fields = ReadContactFormFieldsSource();

        var formRowMatches = System.Text.RegularExpressions.Regex.Matches(
            fields, @"<FormRow\s+[^>]*>", System.Text.RegularExpressions.RegexOptions.Singleline);
        formRowMatches.Count.Should().BeGreaterThanOrEqualTo(3,
            "ContactFormFields renders the 3 field rows (Channel (+country code) + Value + Label)");

        foreach (System.Text.RegularExpressions.Match m in formRowMatches)
        {
            m.Value.Should().Contain("Orientation=\"RowOrientation.Vertical\"",
                $"every <FormRow> in the shared field group opts into Vertical (saw: {m.Value})");
        }

        // The Edit view wires BOTH branches through that shared component
        // rather than duplicating the rows inline.
        var editor = ReadContactsEditorSource();
        editor.Should().Contain("<ContactFormFields",
            "the Edit view renders the shared ContactFormFields field group in the Add and Edit branches");
    }

    // ---- Caller non-regression: no other source silently flips to Vertical ----

    [TestMethod]
    public void FormRow_OtherCallersAreNotSilentlyForcedToVerticalByCss()
    {
        // After this refactor the auto-stack media query is gone, so every
        // <FormRow> in the codebase that does NOT explicitly set
        // Orientation renders Horizontal (label left + input right). This
        // test is a guard against a future regression that re-introduces
        // a viewport-based auto-stack.
        //
        // We dont enumerate every caller that would be brittle but we
        // DO confirm the CSS no longer hides a column-flip rule, and we
        // cross-check that ContactsEditor.razor's Full-view branch (used
        // by GuardianSection / GuardianPickerDialog) still doesnt pass
        // Orientation (so it stays Horizontal).
        var css = ReadFormRowCssSource();
        System.Text.RegularExpressions.Regex.IsMatch(
            css, @"(?m)^\s*@media\b",
            System.Text.RegularExpressions.RegexOptions.Singleline
        ).Should().BeFalse(
            "FormRow.razor.css has no live @media rules Orientation is the only path to a vertical row");

        var editor = ReadContactsEditorSource();

        // Slice the Full-view branch (everything after the Edit-views
        // closing brace). It must NOT declare Orientation on its FormRows
        // GuardianSection / GuardianPickerDialog rely on the canonical
        // Horizontal layout for the Full views Buffered inline edit.
        var fullStart = editor.IndexOf("\n    else\n    {\n", StringComparison.Ordinal);
        // Fallback: skip past the Edit view entirely and grab the rest.
        if (fullStart < 0)
        {
            fullStart = editor.IndexOf("Mode == EditorMode.Live && _loading", StringComparison.Ordinal);
        }
        fullStart.Should().BeGreaterThan(-1, "the Full view branch exists in ContactsEditor.razor");
        var fullBody = editor.Substring(fullStart);

        fullBody.Should().NotContain("Orientation=\"RowOrientation.Vertical\"",
            "the Full view (Buffered inline-edit used by GuardianSection / GuardianPickerDialog) stays Horizontal those callers rely on the canonical layout");
    }

    // ---- Caller widths: narrow fields stay narrow in the vertical drawer ----

    [TestMethod]
    public void ContactsEditor_EditViewUsesCanonicalFieldWidthsNotFill()
    {
        // Channel and Country code are SHORT values (an enum choice and a
        // 2-4 digit calling code). Both the Add and Edit forms share the
        // ContactFormFields group, which uses W3 (Channel, 160px) and W5
        // (Country code, 240px) — deliberately wider than the tiniest ladder
        // step so the fields read as real content space. The Fill width (W9 /
        // w-9) stays reserved for free-text Value / Label that genuinely need
        // the full input cell; giving Channel / Country code W9 would stretch
        // them to fill the 420px drawer cell, which is visually wrong and
        // misleading about the data type.
        var raw = ReadContactFormFieldsSource();
        // Normalize line endings + collapse runs of whitespace so the
        // assertion is robust against CRLF vs LF, indent changes, and
        // trailing-space drift in the source file.
        var source = System.Text.RegularExpressions.Regex.Replace(raw, @"\s+", " ");

        // Channel dropdown binds to the Model (no inline Width — the
        // vertical FormRow stacks label-on-top and the dropdown fills the
        // input cell at its intrinsic width).
        source.Should().Contain(
            "<DropdownForEnum TEnum=\"ContactChannel\" @bind-SelectedValue=\"Model.Channel\" @bind-SelectedValue:after=\"NotifyChannelChanged\" />",
            "the shared field group's Channel dropdown binds to Model.Channel");

        // Country code -> Width="FieldWidth.W5".
        source.Should().Contain(
            "Width=\"FieldWidth.W5\" OptionText=\"@OptionText\"",
            "the shared field group's Country code dropdown uses FieldWidth.W5");

        // Value + Label rows still fill (w-9 CSS class), so users get a
        // roomy free-text input on the Value and the optional Label.
        source.Should().Contain("Placeholder=\"@ValuePlaceholder\" Required class=\"w-9\"",
            "the Value field fills its cell via the w-9 CSS class");
        source.Should().Contain("Placeholder=\"Label (optional)\" class=\"w-9\"",
            "the Label field fills its cell via the w-9 CSS class");

        // Defensive: neither narrow field should get W9 in the shared group.
        raw.Should().NotContain("Width=\"FieldWidth.W9\"\n",
            "ContactFormFields never declares FieldWidth.W9 for Channel / Country code — free-text fields use the w-9 CSS class");
    }
}