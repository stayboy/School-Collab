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
/// <see cref="ThemeSwitcher"/> in the header. The assertion that counted
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
        var source = ReadLayoutSource();
        source.Should().Contain("<FluentToastProvider");
        source.Should().Contain("<FluentDialogProvider");
        source.Should().Contain("<FluentMessageBarProvider");
        source.Should().Contain("<FluentTooltipProvider");
        source.Should().Contain("<FluentKeyCodeProvider");
    }

    [TestMethod]
    public void Layout_Hosts_TheThemeSwitcher()
    {
        // The layout's theme wiring in v5 is the switcher itself: the switcher owns the
        // mode cycle and the accent picker against IThemeService, so dropping it from the
        // header would leave the shell with no way to change the theme at all — and no
        // compile error would say so.
        var source = ReadLayoutSource();
        source.Should().Contain("<ThemeSwitcher");
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
    public void Switcher_Renders_TheModeCycle_And_TheSixAccentChoices()
    {
        // The real v5 mechanism, exercised instead of grepped: the switcher renders a mode
        // button whose accessible name reports the current ThemeMode, plus the accent
        // picker carrying the six Office-colour accents as hex choices.
        var cut = Render<ThemeSwitcher>();

        var modeButton = cut.Find("fluent-button");
        modeButton.GetAttribute("title").Should().Be("Mode: System — click to switch",
            "a fresh session follows the system theme until the user cycles it");

        var accentNames = cut.FindAll("fluent-option")
            .Select(option => option.TextContent.Trim())
            .ToList();
        accentNames.Should().Contain(
            ["Teams", "Word", "Excel", "PowerPoint", "Outlook", "OneNote"]);
    }

    [TestMethod]
    public void Switcher_Cycles_LightDarkSystem()
    {
        var cut = Render<ThemeSwitcher>();

        cut.Find("fluent-button").Click();
        cut.Find("fluent-button").GetAttribute("title").Should().Be("Mode: Light — click to switch");

        cut.Find("fluent-button").Click();
        cut.Find("fluent-button").GetAttribute("title").Should().Be("Mode: Dark — click to switch");

        cut.Find("fluent-button").Click();
        cut.Find("fluent-button").GetAttribute("title").Should().Be("Mode: System — click to switch");
    }
}
