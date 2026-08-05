using System.Reflection;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level regression tests for the host's <c>App.razor</c>
/// (<c>src/SchoolCollab.Admin/Components/App.razor</c>).
///
/// The host originally had three <c>&lt;link rel="stylesheet"&gt;</c>
/// tags pointing at flat-name scoped-CSS bundles:
///
///   <c>SchoolCollab.Admin.styles.css</c>
///   <c>SchoolCollab.Settings.Application.styles.css</c>
///   <c>SchoolCollab.Assignments.Application.styles.css</c>
///
/// Of those, the two RCL ones (<c>Settings</c> and <c>Assignments</c>)
/// 404'd at runtime — the flat-name convention was the pre-.NET 8
/// "first global CSS" path produced by the legacy Razor SDK; modern
/// Blazor (8+) emits scoped-CSS bundles as
/// <c>{Project}.bundle.scp.css</c> under <c>_content/{Project}/</c>
/// and the framework injects them per-component.
///
/// The host's own <c>SchoolCollab.Admin.styles.css</c> is DIFFERENT —
/// unlike the RCLs, the host project's scoped-CSS bundle is still
/// produced at the flat-name path and is the ONLY way to load the
/// host's <c>*.razor.css</c> files (e.g. <c>MainLayout.razor.css</c>,
/// <c>DevTenantSwitcher.razor.css</c>). Removing it silently unstyles
/// every page — that's the bug behind the "card view and page is off"
/// report.
///
/// These tests guard against both failure modes: dead RCL links
/// returning, AND the host link being removed by an over-zealous
/// refactor that confuses the two cases.
/// </summary>
[TestClass]
public class AppRazorCssLinkTests
{
    private static string ReadAppRazor()
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir,
            "..", "..", "..", "..", "..",
            "src", "SchoolCollab.Admin", "Components", "App.razor"));
        File.Exists(srcPath).Should().BeTrue(
            $"App.razor should exist at '{srcPath}'");
        return File.ReadAllText(srcPath);
    }

    [TestMethod]
    public void AppRazor_Does_Not_Reference_Dead_Rcl_Styles_Css()
    {
        // Regression guard: the two RCL flat-name scoped-CSS bundle links
        // were a pre-.NET 8 convention that no longer resolves. They
        // produced browser 404s on every page. They must not return.
        //
        // NOTE: the host's own SchoolCollab.Admin.styles.css is NOT in
        // this list — unlike the RCLs, the host project's scoped-CSS
        // bundle is still produced at the flat-name path and is required
        // to load MainLayout.razor.css and DevTenantSwitcher.razor.css.
        // The RCLs use the modern {Project}.bundle.scp.css path under
        // _content/{Project}/ instead.
        var razor = ReadAppRazor();

        // Extract every <link rel="stylesheet" href="..." /> line so
        // we don't false-positive on the explanatory comment.
        var linkLines = System.Text.RegularExpressions.Regex.Matches(
            razor,
            @"<link\s+rel=""stylesheet""[^>]*>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        linkLines.Should().NotBeEmpty(
            "App.razor must still link at least the global app.css");

        var linkText = string.Join("\n", linkLines.Select(m => m.Value));
        linkText.Should().NotContain("SchoolCollab.Settings.Application.styles.css",
            "the Settings RCL's flat-name scoped-CSS bundle is no longer produced by the SDK");
        linkText.Should().NotContain("SchoolCollab.Assignments.Application.styles.css",
            "the Assignments RCL's flat-name scoped-CSS bundle is no longer produced by the SDK");
    }

    [TestMethod]
    public void AppRazor_Links_Host_Scoped_Css_Bundle()
    {
        // The host project's own scoped-CSS bundle
        // (SchoolCollab.Admin.styles.css) is required — it carries
        // MainLayout.razor.css (the page layout chain) and
        // DevTenantSwitcher.razor.css. Unlike the RCLs, the host project
        // does NOT emit a _content/{Project}/.../bundle.scp.css path; it
        // still uses the flat-name route. This is the only way to load
        // the host's *.razor.css files — removing it silently unstyles
        // every page.
        var razor = ReadAppRazor();
        var linkLines = System.Text.RegularExpressions.Regex.Matches(
            razor,
            @"<link\s+rel=""stylesheet""[^>]*>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var linkText = string.Join("\n", linkLines.Select(m => m.Value));
        linkText.Should().Contain("SchoolCollab.Admin.styles.css",
            "the host project's scoped-CSS bundle must be linked so MainLayout.razor.css / DevTenantSwitcher.razor.css are loaded");
    }

    [TestMethod]
    public void AppRazor_Still_Links_The_Known_Static_Assets()
    {
        // The remaining <link> tags are correct and must stay: the
        // host's wwwroot/css/app.css (global app styles), and the two
        // RCL-served plain-CSS files from SchoolCollab.Admin.Shared.
        // Those routes ARE produced (the manifest has them as
        // _content/SchoolCollab.Admin.Shared/{app,nav}.css).
        var razor = ReadAppRazor();
        razor.Should().Contain("css/app.css",
            "the host's global app stylesheet must stay linked");
        razor.Should().Contain("_content/SchoolCollab.Admin.Shared/app.css",
            "the Shared RCL's app.css is a real static asset");
        razor.Should().Contain("_content/SchoolCollab.Admin.Shared/nav.css",
            "the Shared RCL's nav.css is a real static asset");
    }

    [TestMethod]
    public void AppRazor_Comment_Explains_Why_Scoped_Css_Bundles_Are_Linked()
    {
        // The fix includes an explanatory comment in App.razor so the
        // next reader understands the asymmetry between the host and
        // the RCLs: the host MUST be linked (it's the only way to load
        // the host's *.razor.css files), the RCLs MUST NOT be linked
        // (their flat-name routes 404). Asserting on the comment
        // guards against someone "tidying" it away and then either
        // removing the host link (unstyles every page) or re-adding
        // the dead RCL links (browser 404 on every page).
        var razor = ReadAppRazor();
        razor.Should().Contain("Host (SchoolCollab.Admin) scoped-CSS bundle",
            "the comment must call out that this is the host's bundle, distinct from the RCLs");
        razor.Should().Contain("no longer produced",
            "the comment must explain why the RCL flat-name links are gone");
        razor.Should().Contain("auto-injected by the Blazor framework",
            "the comment must explain the RCL mechanism so the next reader doesn't re-add RCL <link> tags");
    }
}
