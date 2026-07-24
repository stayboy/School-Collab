using System.Reflection;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level regression tests for the main app layout
/// (<c>SchoolCollab.Admin/Components/Layout/MainLayout.razor</c>).
///
/// These tests read the .razor source at test time and assert that the layout
/// continues to register every FluentUI provider the app relies on. Adding a
/// new component that requires a provider (e.g. a sortable, menu-driven
/// <c>FluentDataGrid</c>) will silently throw at runtime if the matching
/// provider is missing — these checks make that a build-time failure instead
/// of a runtime red banner.
/// </summary>
[TestClass]
public class MainLayoutProvidersTests
{
    private static readonly string _layoutPath = Path.Combine(
        AppContext.BaseDirectory,
        // The Admin.Tests.Unit project is copied next to SchoolCollab.Admin in
        // bin/Debug/.../SchoolCollab.Admin/Components/Layout, but at test
        // design time we resolve from the repo source path so the test
        // doesn't depend on build output layout.
        "..", "..", "..", "..", "..",
        "src", "SchoolCollab.Admin", "Components", "Layout", "MainLayout.razor");

    private static string ReadLayoutSource()
    {
        // Resolve from the executing assembly's location so the test works no
        // matter which test runner hosts it.
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir,
            "..", "..", "..", "..", "..",
            "src", "SchoolCollab.Admin", "Components", "Layout", "MainLayout.razor"));
        File.Exists(srcPath).Should().BeTrue(
            $"MainLayout.razor should exist at '{srcPath}' — check the path resolution");
        return File.ReadAllText(srcPath);
    }

    [TestMethod]
    public void Layout_Registers_FluentMenuProvider()
    {
        // The FluentDataGrid renders its column-header sort/options menu via
        // the IMenuService, which requires <FluentMenuProvider /> in the layout.
        // Without it, every sortable grid in the app throws at runtime with
        // "<FluentMenuProvider /> needs to be added to the main layout" — the
        // exact error surfaced in #88.
        var source = ReadLayoutSource();
        source.Should().Contain("<FluentMenuProvider", 
            "FluentDataGrid's column-header menu requires FluentMenuProvider at the layout root");
    }

    [TestMethod]
    public void Layout_Registers_All_FluentUI_Providers()
    {
        // Belt-and-braces check for every provider the app's grids, dialogs,
        // toasts, tooltips, and shortcuts depend on. If any of these is
        // removed, the layout will fail at the first interaction with the
        // matching feature — a build-time test is much friendlier.
        var source = ReadLayoutSource();
        source.Should().Contain("<FluentDesignTheme");
        source.Should().Contain("<FluentToastProvider");
        source.Should().Contain("<FluentDialogProvider");
        source.Should().Contain("<FluentMessageBarProvider");
        source.Should().Contain("<FluentTooltipProvider");
        source.Should().Contain("<FluentKeyCodeProvider");
        source.Should().Contain("<FluentMenuProvider");
    }
}
