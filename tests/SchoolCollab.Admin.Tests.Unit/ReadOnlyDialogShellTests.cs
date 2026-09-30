using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using System.Reflection;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The shared read-only dialog shell — the height authority + single scroll region +
/// bottom-pinned action row that every read-only "View all" dialog used to re-declare
/// (§7 of .github/skills/dialog-ui/SKILL.md). Owner decision 2026-09-30: content-fill
/// with a 220px floor and a 72vh cap, so a short dialog stays short and a long one
/// scrolls instead of growing the box.
/// </summary>
[TestClass]
public class ReadOnlyDialogShellTests : BunitContext
{
    [TestMethod]
    public void RendersTheCallersRootClass_Toolbar_ScrollRegion_AndPinnedFooter()
    {
        var cut = Render<ReadOnlyDialogShell>(p => p
            .Add(x => x.Class, "my-dialog")
            .Add(x => x.Toolbar, Fragment("<span class=\"tb\">T</span>"))
            .Add(x => x.ChildContent, Fragment("<span class=\"body\">B</span>"))
            .Add(x => x.Footer, Fragment("<span class=\"ft\">F</span>")));

        cut.Find(".readonly-dialog").ClassList.Should().Contain("my-dialog",
            "the caller's own root class rides along, so its scoped CSS and tests keep working");
        cut.Find(".readonly-dialog > .tb").Should().NotBeNull(
            "the toolbar is a DIRECT child of the root, so omitting it costs no gap");
        cut.Find(".readonly-dialog__scroll > .body").Should().NotBeNull(
            "the content is wrapped in the shell's single scroll region");
        cut.Find(".readonly-dialog__footer > .ft").Should().NotBeNull(
            "the actions are the shell's last, bottom-pinned row");
    }

    [TestMethod]
    public void OmittingTheToolbar_LeavesNoEmptyRow()
    {
        var cut = Render<ReadOnlyDialogShell>(p => p
            .Add(x => x.ChildContent, Fragment("<span class=\"body\">B</span>"))
            .Add(x => x.Footer, Fragment("<span>F</span>")));

        cut.FindAll(".readonly-dialog > *").Should().HaveCount(2,
            "only the scroll region and the footer render — no phantom toolbar node");
        cut.FindAll(".readonly-dialog__scroll > *").Should().ContainSingle(
            "the scroll region wraps exactly the content it was given");
    }

    [TestMethod]
    public void ShellOwnsTheHeightAuthority_TheScrollRegion_AndThePinnedFooter()
    {
        var css = ReadSource(
            "src/SchoolCollab.Admin.Shared/Components/Dialogs/ReadOnlyDialogShell.razor.css");

        css.Should().Contain(".readonly-dialog {", "the shell's root rule exists");
        css.Should().Contain("display: flex;",
            "the root is the flex column that hands the slack to the scroll region");
        css.Should().Contain("min-height: 220px;",
            "content-fill floor: a dialog with two rows stays short");
        css.Should().Contain("max-height: 72vh;",
            "the cap — beyond it the scroll region scrolls instead of growing the dialog");
        css.Should().Contain("overflow: hidden;", "the root clips its single scroll region");

        css.Should().Contain(".readonly-dialog__scroll {", "the ONE scroll region");
        css.Should().Contain("min-height: 0;",
            "required: a flex child will not shrink below its content height without it");
        css.Should().Contain("overflow-y: auto;", "the region scrolls — content inside it never does");

        css.Should().Contain(".readonly-dialog__footer {", "the action row is the shell's");
        css.Should().Contain("margin-top: auto;", "…pinned to the dialog's bottom");
        css.Should().Contain("border-top: 1px solid", "the §2 separator is a border, not a divider");

        // The regression guards: the shell must not cap or scroll via the dialog box,
        // and nothing may re-introduce a scroll context on the content itself.
        css.Should().NotContain("height: max(", "the box must stay content-fill, not fixed-tall");
    }

    private static RenderFragment Fragment(string markup) =>
        builder => builder.AddMarkupContent(0, markup);

    private static string ReadSource(string relative)
    {
        // tests/<project>/bin/<config>/<tfm> → repo root
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var path = Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", "..", relative));
        File.Exists(path).Should().BeTrue($"source should exist at '{path}' — check path resolution");
        return File.ReadAllText(path);
    }
}
