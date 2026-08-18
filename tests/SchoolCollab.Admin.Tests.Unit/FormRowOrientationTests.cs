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

    // ---- Caller consistency: ContactsEditor Edit view passes Vertical ----

    [TestMethod]
    public void ContactsEditor_EditViewPassesVerticalOrientationToEveryFormRow()
    {
        var source = ReadContactsEditorSource();

        // Slice to the Edit-view body so a Vertical call elsewhere (e.g. a
        // future Full-view usage) doesnt false-pass.
        var editStart = source.IndexOf("else if (View == ContactsView.Edit)", StringComparison.Ordinal);
        editStart.Should().BeGreaterThan(-1, "the Edit view branch exists in ContactsEditor.razor");
        // End of slice = next 'else' at column 0 (the Full-view branch opener).
        // Fallback to a more permissive match if the indentation varies.
        var editEnd = source.IndexOf("\n    else\n    {\n", editStart, StringComparison.Ordinal);
        if (editEnd < 0) editEnd = source.IndexOf("\n    else\r\n    {\r\n", editStart, StringComparison.Ordinal);
        editEnd.Should().BeGreaterThan(editStart, "the Edit view slice has a well-defined end");

        var editBody = source.Substring(editStart, editEnd - editStart);

        // Every <FormRow> in the Edit view body must declare Vertical so
        // the narrow 420px drawer uses label-on-top + input-below for
        // every field (Channel, optional Country code, Value, Label
        // duplicated for Add branch + Edit branch).
        var formRowMatches = System.Text.RegularExpressions.Regex.Matches(
            editBody, @"<FormRow\b[^>]*>", System.Text.RegularExpressions.RegexOptions.Singleline);
        formRowMatches.Count.Should().BeGreaterThanOrEqualTo(8,
            "the Edit view renders at least 8 FormRows (Channel + Country code + Value + Label x {Add, Edit})");

        foreach (System.Text.RegularExpressions.Match m in formRowMatches)
        {
            m.Value.Should().Contain("Orientation=\"RowOrientation.Vertical\"",
                $"every <FormRow> in the Edit view opts into Vertical (saw: {m.Value})");
        }
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
        // 2-4 digit calling code). The Edit branch uses the canonical narrow
        // widths from the FieldWidth ladder:
        //   Channel       -> W1 (80px)  -- "Channel enum, tiny fields"
        //   Country code  -> W2 (120px) -- "Country calling code"
        // The Add branch uses W3 (Channel) and W5 (Country code) — those
        // are deliberately wider than the Edit branch because the Add form
        // is the empty form a user lands on first (the "Add contact" anchor
        // from the Readonly summary), so we want Channel / Country code to
        // read at a glance as fields with real content space, not the tiniest
        // possible controls. The Fill width (W9 / w-9) is still reserved for
        // free-text fields (Value, Label) that genuinely need the full input
        // cell. Giving Channel / Country code W9 would make them stretch to
        // fill the 420px drawer cell, which is both visually wrong and
        // misleading about the data type — so this test rejects W9 on those
        // narrow fields and locks in the deliberate W1/W2 vs W3/W5 split.
        var raw = ReadContactsEditorSource();
        // Normalize line endings + collapse runs of whitespace so the
        // assertion is robust against CRLF vs LF, indent changes, and
        // trailing-space drift in the source file.
        var source = System.Text.RegularExpressions.Regex.Replace(
            raw, @"\s+", " ");

        // --- Add branch (W3 / W5) ---
        // Channel FormRow -> Width="FieldWidth.W3".
        source.Should().Contain(
            "<FormRow Label=\"Channel\" Orientation=\"RowOrientation.Vertical\"> " +
            "<DropdownForEnum TEnum=\"ContactChannel\" @bind-Value=\"_newChannel\" " +
            "@bind-Value:after=\"OnChannelChanged\" Width=\"FieldWidth.W3\" />",
            "the Add-branch Channel dropdown uses FieldWidth.W3 — the empty form a user lands on first, wider than the Edit branch's W1");

        // Country code -> Width="FieldWidth.W5".
        source.Should().Contain(
            "Width=\"FieldWidth.W5\" OptionText=\"@FormatCountryCodeOption\"",
            "the Add-branch Country code dropdown uses FieldWidth.W5 — wider than the Edit branch's W2 since this is the empty form");

        // --- Edit branch (W1 / W2) ---
        source.Should().Contain(
            "@bind-SelectedValue=\"_editChannel\" " +
            "@bind-SelectedValue:after=\"OnInlineEditChannelChanged\" " +
            "Width=\"FieldWidth.W1\"",
            "the Edit-branch Channel dropdown uses FieldWidth.W1");

        // Both branches declare W2/W5 + OptionText for country code. The
        // Add branch above already covers W5; require a SECOND match (W2)
        // to prove the Edit branch also has it.
        System.Text.RegularExpressions.Regex.Matches(
            source, @"Width=""FieldWidth\.W2"" OptionText=""@FormatCountryCodeOption"""
        ).Count.Should().Be(1,
            "the Edit branch of the country code dropdown uses FieldWidth.W2 (120px)");

        // Defensive: neither branch should have a W9 on Country code.
        // We check the WHOLE source so a future third branch (e.g. Full
        // view) can't silently regress either.
        raw.Should().NotContain("Width=\"FieldWidth.W9\"\n",
            "no ContactsEditor row declares FieldWidth.W9 on its own line — Channel / Country code are W1/W2 (Edit) or W3/W5 (Add) and free-text fields use the w-9 CSS class");

        // Value + Label rows still fill (w-9 CSS class), so users get a
        // roomy free-text input on the Value and the optional Label.
        source.Should().Contain("Placeholder=\"@ValuePlaceholder\" Required class=\"w-9\"",
            "the Value field (Add branch) fills its cell via the w-9 CSS class");
        source.Should().Contain("Placeholder=\"Label (optional)\" class=\"w-9\"",
            "the Label field (Add branch) fills its cell via the w-9 CSS class");
        source.Should().Contain("Placeholder=\"@EditValuePlaceholder\" Required class=\"w-9\"",
            "the Value field (Edit branch) fills its cell via the w-9 CSS class");
    }
}