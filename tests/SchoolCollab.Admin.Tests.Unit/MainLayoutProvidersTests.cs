using System.Reflection;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Theme;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level regression tests for the main app layout
/// (<c>SchoolCollab.Admin/Components/Layout/MainLayout.razor</c>).
///
/// These tests read the .razor source at test time and assert that the layout
/// continues to register every FluentUI provider the app relies on. Adding a
/// new component that requires a provider will silently throw at runtime if the
/// matching provider is missing — these checks make that a build-time failure
/// instead of a runtime red banner.
///
/// FluentUI v5 removed <c>FluentMenuProvider</c> (menus need no provider any
/// more), so the assertion that pinned it was retired rather than left passing
/// vacuously.
///
/// <para>Theming moved off the layout in v5 as well: <c>FluentDesignTheme</c> — and with it
/// <c>DesignThemeModes</c>/<c>OfficeColor</c> — no longer exists
/// (<c>MigrationFluentDesignTheme.md</c>), so the layout's whole theme wiring is now the
/// <see cref="ThemeSwitcher"/> — hosted by the sidebar's utility row since 2026-10-24. The assertion that counted
/// <c>&lt;FluentDesignTheme</c> literals therefore stopped discriminating anything (the
/// literal count is 0 in v5) and is replaced here by three checks that do: the layout still
/// hosts the switcher, it has not regained the removed v4 root, and the switcher itself
/// renders its mode + accent controls against the v5 <c>IThemeService</c>.</para>
/// </summary>
[TestClass]
public class MainLayoutProvidersTests : BunitContext
{
    public MainLayoutProvidersTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

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
    public void Layout_Registers_All_FluentUI_Providers()
    {
        // Belt-and-braces check for every provider the app's grids, dialogs,
        // toasts, tooltips, and shortcuts depend on. If any of these is
        // removed, the layout will fail at the first interaction with the
        // matching feature — a build-time test is much friendlier.
        // (FluentUI v5: the FluentMenuProvider half of this check was retired —
        // the component no longer exists and menus need no provider.)
        //
        // v5 also collapsed the toast/dialog/tooltip/key-code providers into the single
        // <FluentProviders> host, which must sit at the layout root as a SELF-CLOSING sibling:
        // it declares no ChildContent, so any markup given to it is discarded. The message-bar
        // provider is NOT part of that host and still has to be declared here — which is why
        // this test pins the pair rather than counting provider tags. The shell-level proof that
        // the placement actually renders lives in RoutesShellRenderTests.
        var source = ReadLayoutSource();
        source.Should().Contain("<FluentProviders /");
        source.Should().Contain("<FluentMessageBarProvider");
    }

    [TestMethod]
    public void Sidebar_Hosts_TheThemeSwitcher_And_TheHeader_Does_Not()
    {
        // v5's whole theme wiring is the switcher: it owns the mode and the accent against
        // IThemeService, so dropping it would leave the shell with no way to change the theme
        // at all — and no compile error would say so. Since 2026-10-24 it lives in the
        // sidebar's utility row, not the app bar, so the assertion follows it there and pins
        // the bar's new composition at the same time (the bar carries context only).
        var layout = ReadLayoutSource();
        layout.Should().NotContain("<ThemeSwitcher",
            "the theme control moved to the sidebar — a copy back in the header re-grows the " +
            "bar's fixed width, which is what truncated the page title (#402)");

        var nav = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "SchoolCollab.Admin", "Components", "Layout", "NavMenu.razor"));
        nav.Should().Contain("<ThemeSwitcher", "the sidebar's utility row hosts the theme menu");
        nav.Should().Contain("nav-footer", "the utility row is the footer, pinned at the bottom");
        layout.Should().Contain("<NavMenu", "the layout must still render the sidebar it moved into");
    }

    [TestMethod]
    public void Layout_Does_Not_Reintroduce_The_Removed_DesignTheme_Root()
    {
        // FluentDesignTheme does not exist in 5.0.1, so this pins the removal rather than
        // describing live wiring: a v4 root copied back into the header is a compile error
        // today, and that is the point — the switcher above is the replacement.
        var source = ReadLayoutSource();
        source.Should().NotContain("<FluentDesignTheme");
    }

    [TestMethod]
    public void Switcher_Offers_TheThreeModes_And_TheSixAccentChoices()
    {
        // The real v5 mechanism, exercised instead of grepped: the trigger reports the active
        // mode in its accessible name, and the menu carries the three ThemeModes plus the six
        // Office-colour accents. A menu, not a cycle (owner, 2026-10-24).
        var cut = Render<ThemeSwitcher>();

        cut.Find("fluent-button").GetAttribute("title").Should().Be(
            "Theme: System — open theme menu",
            "a fresh session follows the system theme until the user picks another");

        var labels = cut.FindAll("fluent-menu-item")
            .Select(item => item.TextContent.Trim().TrimStart('\u2713').Trim())
            .ToList();
        labels.Should().Contain(["System", "Light", "Dark"]);
        labels.Should().Contain(
            ["Teams", "Word", "Excel", "PowerPoint", "Outlook", "OneNote"]);
    }

}
