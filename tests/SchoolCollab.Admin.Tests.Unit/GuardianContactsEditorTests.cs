using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level regression tests for the in-memory
/// <c>GuardianContactsEditor.razor</c> component (spec §4.9 / goal #6).
///
/// The editor is a presentational component that operates on a
/// <c>ContactModel</c> list supplied by the parent. The tests assert:
///   - The component is wired into the expected files (parent
///     <c>GuardianFormDialog</c> in a follow-up).
///   - Move-up / Move-down / Remove / Mark-preferred are exposed in the
///     markup as FluentButton IconStart handlers so the parent can rely
///     on the surface.
///   - The CSS rules the markup depends on (preferred row highlight,
///     action alignment) are present.
/// </summary>
[TestClass]
public class GuardianContactsEditorTests
{
    private const string ComponentPath = "GuardianContactsEditor.razor";
    private const string CssPath = "GuardianContactsEditor.razor.css";

    [TestMethod]
    public void Component_Exists_And_Exposes_ContactModel_List()
    {
        var razor = ReadSource(ComponentPath);

        // The component takes a list of ContactModel entries that the
        // parent supplies and mutates in place.
        razor.Should().Contain("public sealed class ContactModel",
            "the in-memory contact shape must be public so the parent can construct a list");
        razor.Should().Contain("public IList<ContactModel> Contacts",
            "the parent hands in a list of ContactModel");
        razor.Should().Contain("public int Order",
            "Order drives the display order (lowest = preferred)");
    }

    [TestMethod]
    public void Component_Exposes_Move_Reorder_And_Preferred_Actions()
    {
        var razor = ReadSource(ComponentPath);

        // All four action surfaces must be present as buttons in the
        // markup so the parent's eventual wiring can rely on the surface.
        razor.Should().Contain("OnClick=\"@(() => MoveUp(c))\"",
            "move-up is wired to the MoveUp handler");
        razor.Should().Contain("OnClick=\"@(() => MoveDown(c))\"",
            "move-down is wired to the MoveDown handler");
        razor.Should().Contain("OnClick=\"@(() => MakePreferred(c))\"",
            "mark-preferred is wired to the MakePreferred handler");
        razor.Should().Contain("OnClick=\"@(() => Remove(c))\"",
            "remove is wired to the Remove handler");
    }

    [TestMethod]
    public void Component_Raises_Change_Notification()
    {
        var razor = ReadSource(ComponentPath);

        // The editor is purely in-memory; the parent listens via
        // OnContactsChanged after every mutation so it can persist.
        razor.Should().Contain("OnContactsChanged",
            "the editor must surface change notifications to the parent");
        razor.Should().Contain("await NotifyChangedAsync()",
            "every mutation flushes through NotifyChangedAsync");
    }

    [TestMethod]
    public void Component_Styles_Preferred_Row_And_Aligns_Actions()
    {
        var css = ReadSource(CssPath);

        // The CSS file must highlight the preferred row so the
        // "lowest Order" contact is the visual anchor.
        css.Should().Contain(".guardian-contact-row--preferred",
            "preferred row highlight class is defined in the CSS");
        // The actions cluster right-aligns so the row reads consistently.
        css.Should().Contain(".guardian-contact-actions",
            "actions cluster is defined in the CSS");
    }

    private static string ReadSource(string relativePath)
    {
        // Mirror the path resolution used by GuardianContactsListTests:
        // walk from the test bin directory up to the repo root, then
        // resolve the source file under src/Students.
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir,
            "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Admin",
            "Components", "Students", relativePath));
        File.Exists(srcPath).Should().BeTrue(
            $"{relativePath} should exist at '{srcPath}' — check the path resolution");
        return File.ReadAllText(srcPath);
    }
}