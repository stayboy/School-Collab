using System.IO;
using System.Reflection;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level tests for the shared <c>DialogDrawer</c> component (plan
/// 2026-08-18-dialog-content-drawer.md). The drawer is designed to live
/// INSIDE a FluentDialog body — it fills the full dialog content area
/// (between the title bar and the actions bar) and never overlaps either.
/// Asserted invariants:
/// <list type="bullet">
///   <item>The component exposes <c>Open</c>, <c>OpenChanged</c>, <c>Title</c>,
///         <c>Side</c> (Right/Left), <c>Width</c>, <c>ChildContent</c>,
///         <c>ShowSubmit</c>, <c>SubmitText</c>, <c>OnSubmitAsync</c>,
///         <c>ShowCancel</c>, <c>CancelText</c>, <c>ShowBackdrop</c>,
///         <c>Busy</c> parameters.</item>
///   <item>The panel markup renders <c>role="region"</c> + <c>aria-labelledby</c>
///         (non-modal inside the dialog), a header (title + dismiss), a
///         scrollable body, and an optional footer with Cancel / Submit buttons.</item>
///   <item>The CSS positions the panel + backdrop absolutely inside the
///         dialog content area (no viewport-fixed behaviour).</item>
///   <item>Escape closes the drawer; <c>OpenChanged(false)</c> is fired.</item>
///   <item><c>OnSubmitAsync</c> returning <c>true</c> auto-closes the
///         drawer; returning <c>false</c> keeps it open.</item>
/// </list>
/// </summary>
[TestClass]
public class DialogDrawerTests
{
    private static string ReadSource(string relative)
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir, "..", "..", "..", "..", "..", "src", relative));
        File.Exists(srcPath).Should().BeTrue(
            $"source should exist at '{srcPath}'");
        return File.ReadAllText(srcPath);
    }

    [TestMethod]
    public void DialogDrawer_ExposesExpectedParameters()
    {
        var source = ReadSource(
            "SchoolCollab.Admin.Shared/Components/DialogDrawer.razor");

        source.Should().Contain("public bool Open { get; set; }",
            "Open is two-way bindable");
        source.Should().Contain("public EventCallback<bool> OpenChanged { get; set; }",
            "OpenChanged fires when the drawer closes");
        source.Should().Contain("EditorRequired",
            "Title is required (header text)");
        source.Should().Contain("public string Title",
            "Title parameter exists");
        source.Should().Contain("public DialogDrawerSide Side { get; set; }",
            "Side anchors the panel to Right (default) or Left");
        source.Should().Contain("public string Width { get; set; }",
            "Width controls the panel width");
        source.Should().Contain("public RenderFragment? ChildContent { get; set; }",
            "ChildContent is the edit form body");
        source.Should().Contain("public bool ShowSubmit { get; set; }",
            "ShowSubmit toggles the Submit button in the footer");
        source.Should().Contain("public Func<Task<bool>>? OnSubmitAsync",
            "OnSubmitAsync returns true to auto-close, false to keep open");
        source.Should().Contain("public bool ShowCancel { get; set; }",
            "ShowCancel toggles the Cancel button in the footer");
        source.Should().Contain("public bool ShowBackdrop { get; set; }",
            "ShowBackdrop dims the main form behind the drawer");
        source.Should().Contain("public bool Busy { get; set; }",
            "Busy is an optional external busy flag (disables the footer)");
    }

    [TestMethod]
    public void DialogDrawer_Panel_IsANonModalRegionInsideTheDialog()
    {
        var source = ReadSource(
            "SchoolCollab.Admin.Shared/Components/DialogDrawer.razor");

        // The drawer is a region INSIDE the already-modal FluentDialog, not
        // a second modal surface. Nested aria-modal declarations confuse
        // screen readers, so the panel is role="region" (no aria-modal) and
        // labelled by its own title element.
        source.Should().Contain("role=\"region\"",
            "the panel is a region inside the dialog, not a second modal");
        source.Should().NotContain("aria-modal=\"true\"",
            "nested aria-modal would double-trap screen readers inside the dialog");
        source.Should().Contain("aria-labelledby=\"@_titleId\"",
            "the panel is labelled by its own title element");
        source.Should().Contain("id=\"@_titleId\"",
            "the title element carries a stable per-instance id for aria-labelledby");
    }

    [TestMethod]
    public void DialogDrawer_Panel_AnchorsToRightByDefault_LeftWhenSideIsLeft()
    {
        var source = ReadSource(
            "SchoolCollab.Admin.Shared/Components/DialogDrawer.razor");

        source.Should().Contain("DialogDrawerSide.Right",
            "the default side is Right");
        source.Should().Contain("dialog-drawer-panel--right",
            "the right-anchored panel has a right-anchored class");
        source.Should().Contain("dialog-drawer-panel--left",
            "the left-anchored panel has a left-anchored class");
        source.Should().Contain("right: 0;",
            "the right-anchored panel pins to right:0 via inline style");
        source.Should().Contain("left: 0;",
            "the left-anchored panel pins to left:0 via inline style");
    }

    [TestMethod]
    public void DialogDrawer_HasHeaderBodyAndFooter()
    {
        var source = ReadSource(
            "SchoolCollab.Admin.Shared/Components/DialogDrawer.razor");

        source.Should().Contain("dialog-drawer-header",
            "the panel renders a header (title + dismiss ✕)");
        source.Should().Contain("dialog-drawer-title",
            "the panel renders a title element");
        source.Should().Contain("dialog-drawer-dismiss",
            "the panel renders a dismiss ✕ button");
        source.Should().Contain("dialog-drawer-body",
            "the panel renders a scrollable body for ChildContent");
        source.Should().Contain("dialog-drawer-footer",
            "the panel renders an optional footer with Cancel / Submit");
        source.Should().Contain("dialog-drawer-btn-cancel",
            "the Cancel button has a stable class for styling");
        source.Should().Contain("dialog-drawer-btn-submit",
            "the Submit button has a stable class for styling");
    }

    [TestMethod]
    public void DialogDrawer_Css_PanelsFillContainingBlock_NotViewport()
    {
        var css = ReadSource(
            "SchoolCollab.Admin.Shared/Components/DialogDrawer.razor.css");

        // The drawer is position: absolute (NOT fixed) so it fills the
        // containing block rather than the viewport. This is the key
        // contract: positioned inside the dialog-content root, the panel
        // spans the dialog body content area (between header and footer)
        // without overlapping either.
        css.Should().Contain(".dialog-drawer-panel {",
            "the panel rule exists");
        css.Should().Contain("position: absolute;",
            "the panel is positioned absolutely (fills containing block, not viewport)");
        css.Should().Contain(".dialog-drawer-backdrop {",
            "the backdrop rule exists");
        css.Should().Contain("position: absolute;",
            "the backdrop is positioned absolutely too");
        css.Should().NotContain("position: fixed;",
            "the drawer must NOT use fixed positioning — it must stay inside the dialog body");
    }

    [TestMethod]
    public void DialogDrawer_Escape_ClosesDrawer()
    {
        var source = ReadSource(
            "SchoolCollab.Admin.Shared/Components/DialogDrawer.razor");

        source.Should().Contain("if (e.Key == \"Escape\" && !IsBusy)",
            "Escape closes the drawer when not busy");
        source.Should().Contain("@onkeydown:stopPropagation",
            "Escape propagation is stopped so the parent FluentDialog does not also close");
        source.Should().Contain("await OpenChanged.InvokeAsync(false)",
            "the drawer fires OpenChanged(false) on close (Cancel / ✕ / Escape / backdrop)");
    }

    [TestMethod]
    public void DialogDrawer_Submit_AutoClosesOnTrue_StaysOpenOnFalse()
    {
        var source = ReadSource(
            "SchoolCollab.Admin.Shared/Components/DialogDrawer.razor");

        source.Should().Contain("var ok = await OnSubmitAsync.Invoke()",
            "the drawer invokes OnSubmitAsync when Submit is clicked");
        source.Should().Contain("if (ok)",
            "the drawer checks the submit result");
        source.Should().Contain("await OpenChanged.InvokeAsync(false)",
            "the drawer auto-closes when OnSubmitAsync returns true");
    }

    [TestMethod]
    public void DialogDrawer_BackdropClick_ClosesDrawer()
    {
        var source = ReadSource(
            "SchoolCollab.Admin.Shared/Components/DialogDrawer.razor");

        source.Should().Contain("<div class=\"@BackdropClass\" @onclick=\"RequestClose\"",
            "clicking the backdrop fires RequestClose");
    }
}