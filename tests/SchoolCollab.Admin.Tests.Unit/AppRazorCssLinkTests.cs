using System.Reflection;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level regression tests for the host's <c>App.razor</c>
/// (<c>src/SchoolCollab.Admin/Components/App.razor</c>).
///
/// The host previously had three <c>&lt;link rel="stylesheet"&gt;</c>
/// tags pointing at flat-name scoped-CSS bundles:
///
///   <c>SchoolCollab.Admin.styles.css</c>
///   <c>SchoolCollab.Settings.Admin.styles.css</c>
///   <c>SchoolCollab.Assignments.Admin.styles.css</c>
///
/// All three 404'd at runtime. The flat-name convention was the
/// pre-.NET 8 "first global CSS" path produced by the legacy
/// Razor SDK; modern Blazor (8+) emits scoped CSS bundles as
/// <c>{Project}.bundle.scp.css</c> under
/// <c>_content/{Project}/</c> and the framework injects them
/// per-component — the host no longer needs to <c>&lt;link&gt;</c>
/// them explicitly. These tests guard against the dead links
/// being reintroduced.
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
    public void AppRazor_Does_Not_Reference_Flat_Name_Styles_Css()
    {
        // Regression guard: the three flat-name scoped-CSS bundle links
        // were a pre-.NET 8 convention that no longer resolves. They
        // produced browser 404s on every page. They must not return.
        // We assert against the actual <link> tags, not the whole file
        // (the explanatory comment in App.razor intentionally names
        // the legacy routes for historical context).
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
        linkText.Should().NotContain("SchoolCollab.Admin.styles.css",
            "the host's own flat-name scoped-CSS bundle is no longer produced by the SDK");
        linkText.Should().NotContain("SchoolCollab.Settings.Admin.styles.css",
            "the Settings RCL's flat-name scoped-CSS bundle is no longer produced by the SDK");
        linkText.Should().NotContain("SchoolCollab.Assignments.Admin.styles.css",
            "the Assignments RCL's flat-name scoped-CSS bundle is no longer produced by the SDK");
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
    public void AppRazor_Comment_Explains_Why_Scoped_Css_Bundles_Are_Not_Linked()
    {
        // The fix included an explanatory comment so the next reader
        // understands why the three flat-name links are absent.
        // Asserting on the comment guards against someone "tidying"
        // it away and then re-adding the dead links.
        var razor = ReadAppRazor();
        razor.Should().Contain("Scoped CSS bundles",
            "the explanatory comment must remain so future readers understand the intent");
        razor.Should().Contain("per-component",
            "the comment must point to the modern per-component auto-loading mechanism");
        razor.Should().Contain("404",
            "the comment must call out the 404 risk that motivated the change");
    }
}
