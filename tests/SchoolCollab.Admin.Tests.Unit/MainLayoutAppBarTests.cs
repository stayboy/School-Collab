using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Components.Layout;
using SchoolCollab.Admin.Services;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Application.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The app bar's title contract (#402): the bar names the route it is showing, the brand appears
/// exactly once, an unmapped route shows nothing rather than a second "School Collab", and the
/// page title is not width-capped in any stylesheet.
/// </summary>
/// <remarks>
/// <para>
/// bUnit cannot see CSS clipping, so "in full" here means the title's <em>text node</em> is the
/// whole route title — the rendered-frame evidence for the visual claim lives with the round
/// (W4), not in this fixture.
/// </para>
/// <para>
/// The route table itself is pinned source-level: the conformance test scans the <c>@page</c>
/// templates of exactly the four assemblies the Admin <c>Router</c> routes
/// (<c>Components/Routes.razor</c>) and fails when one resolves to no title. Scanning
/// <c>src/**</c> instead would demand titles for Families / Mobile pages that never render under
/// this layout, and their templates collide with Admin's (<c>/</c>, <c>/assignments</c>,
/// <c>/Error</c>).
/// </para>
/// </remarks>
[TestClass]
public class MainLayoutAppBarTests : BunitContext
{
    private static string? _repoRoot;

    /// <summary>The repo root, found by walking up to the directory holding <c>SchoolCollab.slnx</c>.</summary>
    /// <remarks>
    /// A fixed <c>..</c> hop from the test assembly (what most source-scanning tests here use) only
    /// lands on the repo root for the default output layout, and the scans below need a directory
    /// tree rather than one file — so they resolve by marker and fail loudly if it is gone.
    /// </remarks>
    private static string RepoRoot => _repoRoot ??= FindRepoRoot();

    /// <summary>The four assemblies <c>Components/Routes.razor</c> actually routes.</summary>
    private static string[] RoutedSourceRoots =>
    [
        Path.Combine(RepoRoot, "src", "SchoolCollab.Admin"),
        Path.Combine(RepoRoot, "src", "Settings", "SchoolCollab.Settings.Application"),
        Path.Combine(RepoRoot, "src", "Assignments", "SchoolCollab.Assignments.Application"),
        Path.Combine(RepoRoot, "src", "Students", "SchoolCollab.Students.Application"),
    ];

    /// <summary>An <c>@page "…"</c> directive, including one preceded by a byte-order mark.</summary>
    private static readonly Regex PageDirective = new(
        "^\uFEFF?[ \t]*@page[ \t]+\"(?<template>[^\"]+)\"", RegexOptions.Multiline);

    /// <summary>
    /// Extracts every declaration block that follows <paramref name="selector"/> in <paramref name="css"/>,
    /// at any nesting depth.
    /// </summary>
    /// <remarks>
    /// A flat regex cannot do this: an <c>@media</c>-scoped rule sits inside a block that itself contains
    /// braces, so a "no braces in the body" pattern silently skips <em>every</em> media-scoped rule — and
    /// the sheet this test guards (app.css) already carries a media block for <c>.brand-page</c>. Brace
    /// counting from the opening <c>{</c> reaches the nested rule regardless of how many wrappers it sits in.
    /// </remarks>
    private static IEnumerable<string> CssBlocksFor(string css, string selector)
    {
        var index = 0;
        while ((index = css.IndexOf(selector, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var open = css.IndexOf('{', index);
            if (open < 0)
            {
                yield break;
            }

            var depth = 0;
            var end = -1;
            for (var i = open; i < css.Length; i++)
            {
                if (css[i] == '{')
                {
                    depth++;
                }
                else if (css[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        end = i;
                        break;
                    }
                }
            }

            if (end < 0)
            {
                yield break;
            }

            yield return $"{css[index..open].Trim()} {{ {css[(open + 1)..end].Trim()} }}";
            index = end;
        }
    }

    public MainLayoutAppBarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddLogging();

        // The layout's own dependencies, with the shape RoutesShellRenderTests uses: one inert
        // HTTP pipeline for the three typed clients. Anonymous auth means the term chip renders
        // "No tenant" and an empty configuration keeps the dev tenant switcher hidden, so
        // neither reaches its API.
        var http = new HttpClient(new NotFoundHandler()) { BaseAddress = new Uri("https://localhost/") };
        var codedValues = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValues);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValues));
        Services.AddSingleton(new TenantsApiClient(http));
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        var auth = new AnonymousAuthStateProvider();
        Services.AddSingleton<AuthenticationStateProvider>(auth);
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));
        Services.AddSingleton<IDevTenantSelection>(new StubDevTenantSelection());
        Services.AddSingleton<AdminDefaultTenantResolver>();
        Services.AddSingleton<IFeatureFlagService>(new StubFlagService());
    }

    [TestMethod]
    public void AppBar_Renders_TheRouteTitle_InFull_And_TheBrand_Once()
    {
        var cut = RenderLayoutAt("/notification-templates");

        cut.FindAll(".brand-page").Should().ContainSingle(
            "the bar shows the route's own title — the reported defect was the brand rendered twice");
        cut.Find(".brand-page").TextContent.Should().Be("Notification Templates",
            "the title text node must be the whole route title; a clipped title is a CSS question " +
            "the rendered frames prove, not something bUnit can assert");

        cut.FindAll(".brand").Should().ContainSingle();
        cut.Find(".brand").TextContent.Should().Be("School Collab");

        Regex.Matches(cut.Find(".layout-header-stack").TextContent, "School Collab").Count
            .Should().Be(1, "'School Collab · School Collab' is exactly what #402 reported");
    }

    [TestMethod]
    public void AppBar_Renders_NoTitle_And_NoSeparator_For_An_Unmapped_Route()
    {
        var cut = RenderLayoutAt("/no-such-route");

        cut.FindAll(".brand-page").Should().BeEmpty(
            "an unmapped route must not fall back to the brand string as its title");
        cut.FindAll(".brand-sep").Should().BeEmpty(
            "the separator only introduces a title — without one it would render a dangling bullet");
        cut.FindAll(".brand").Should().ContainSingle("the brand is still the bar's identity");
    }

    [TestMethod]
    public void Every_Routed_Page_Resolves_AStaticTitle()
    {
        var templates = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var root in RoutedSourceRoots)
        {
            Directory.Exists(root).Should().BeTrue(
                $"'{root}' must exist — the route scan resolves from the repo root");
            foreach (var file in Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories))
            {
                if (IsBuildOutput(file))
                {
                    continue;
                }

                foreach (Match directive in PageDirective.Matches(File.ReadAllText(file)))
                {
                    templates.Add(directive.Groups["template"].Value);
                }
            }
        }

        templates.Count.Should().BeGreaterThan(40,
            "the scan must actually find the routed pages — a mis-resolved path would otherwise " +
            "make this test pass vacuously");

        var unresolved = templates
            .Where(template => string.IsNullOrWhiteSpace(MainLayout.ResolveTitle(ToShape(template))))
            .ToList();

        unresolved.Should().BeEmpty(
            "every @page template the Admin shell routes must resolve through MainLayout.ResolveTitle " +
            "(literal route, or an arm whose StartsWith/EndsWith predicate matches its segment shape). " +
            "Unresolved: " + string.Join(", ", unresolved));
    }

    [TestMethod]
    public void No_Stylesheet_Caps_The_PageTitle_Width()
    {
        var stylesheets = Directory
            .EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.css", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Where(file => file.EndsWith(".razor.css", StringComparison.OrdinalIgnoreCase)
                           || file.EndsWith("app.css", StringComparison.OrdinalIgnoreCase))
            .ToList();
        stylesheets.Should().NotBeEmpty();

        var titleRules = new List<string>();
        foreach (var sheet in stylesheets)
        {
            foreach (var rule in CssBlocksFor(File.ReadAllText(sheet), ".brand-page"))
            {
                titleRules.Add($"{Path.GetFileName(sheet)} {rule}");
                rule.Should().NotContain("max-width",
                    "a width cap on the page title clips it before the bar's other controls have " +
                    $"yielded — that is the truncation #402 reported ({Path.GetFileName(sheet)}: {rule})");
            }
        }

        titleRules.Should().NotBeEmpty(
            ".brand-page must still have a real rule (its ellipsis / nowrap), otherwise this " +
            "check proves nothing about the bar");
    }

    [TestMethod]
    public void ResolveTitle_Names_A_Detail_Route_And_Its_Edit_Route_Distinctly()
    {
        // The conformance test above proves every route resolves to *something*; this one proves the
        // label is not merely non-null. A detail page labelled "Edit …" is the bar lying about where
        // you are — the same class of defect as #402 itself, and exactly how the grade-level detail
        // route was mislabelled until the diff review caught it (2026-10-24).
        const string gradeLevel = "/students/grade-levels/0f9c2c8e-1d5b-4c3a-9f1e-2b7c6a5d4e3f";
        MainLayout.ResolveTitle(gradeLevel).Should().Be("Grade Level");
        MainLayout.ResolveTitle(gradeLevel + "/edit").Should().Be("Edit Grade Level");

        MainLayout.ResolveTitle("/notification-templates/0f9c2c8e-1d5b-4c3a-9f1e-2b7c6a5d4e3f")
            .Should().Be("Notification Template");
    }

    [TestMethod]
    public void AppBar_Height_Matches_TheAppBarHeightToken()
    {
        var layout = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SchoolCollab.Admin", "Components", "Layout", "MainLayout.razor"));
        var header = Regex.Match(layout, "Area=\"LayoutArea\\.Header\"[\\s\\S]{0,40}?Height=\"(?<height>\\d+)\"");
        header.Success.Should().BeTrue("the layout must still pin its header region's height");

        var appCss = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SchoolCollab.Admin.Shared", "wwwroot", "app.css"));
        var token = Regex.Match(appCss, "--app-bar-height:\\s*(?<height>\\d+)px");
        token.Success.Should().BeTrue("--app-bar-height is the token every sticky offset reads");

        token.Groups["height"].Value.Should().Be(header.Groups["height"].Value,
            "a header height that differs from --app-bar-height silently breaks every sticky block " +
            "that reads the token (panel-layout-pattern.md §6)");
    }

    /// <summary>
    /// Renders the real <see cref="MainLayout"/> as the shell renders it, on <paramref name="path"/>.
    /// </summary>
    private IRenderedComponent<MainLayout> RenderLayoutAt(string path)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(path);
        return Render<MainLayout>();
    }

    /// <summary>Reduces every <c>{…}</c> route segment to a wildcard, so shapes compare, not names.</summary>
    private static string ToShape(string template) =>
        string.Join('/', template.Split('/').Select(segment => segment.Contains('{') ? "*" : segment));

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SchoolCollab.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find SchoolCollab.slnx above '{AppContext.BaseDirectory}' — the route scan needs the repo root");
    }

    private static bool IsBuildOutput(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class AnonymousAuthStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal()));
    }

    private sealed class StubDevTenantSelection : IDevTenantSelection
    {
        public Task<Guid?> GetSelectedTenantIdAsync(CancellationToken ct = default) => Task.FromResult<Guid?>(null);
        public Task SetSelectedTenantIdAsync(Guid? tenantId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Guid?> GetAppliedDefaultTenantIdAsync(CancellationToken ct = default) => Task.FromResult<Guid?>(null);
        public Task SetAppliedDefaultTenantIdAsync(Guid? tenantId, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>
    /// Every flag off. The shell's <c>NavMenu</c> is constructed for any route below the root, so it
    /// needs the service; nothing in this fixture renders a gated item.
    /// </summary>
    private sealed class StubFlagService : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => false;

        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) => Task.FromResult(false);

        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();

        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    }

    [TestMethod]
    public void AppBar_Text_Takes_TheOnBrandForeground_InBothThemes()
    {
        // The bar paints a BRAND surface (light #0f6cbd, dark #115ea3 — measured 2026-10-24), so
        // its text must resolve the on-brand foreground explicitly. Declaring no colour at all
        // let the brand and page title inherit --colorNeutralForeground1 (#242424 in light mode),
        // which measured 2.88:1 and 2.55:1 on the blue bar — both below WCAG AA.
        var appCss = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SchoolCollab.Admin.Shared", "wwwroot", "app.css"));

        var brandRules = CssBlocksFor(appCss, ".brand").ToList();
        brandRules.Should().NotBeEmpty(".brand must still be styled");
        brandRules.Should().Contain(
            rule => rule.Contains("--colorNeutralForegroundOnBrand", StringComparison.Ordinal),
            "the bar's text colour must come from the on-brand token, not the inherited page colour");

        var titleRules = CssBlocksFor(appCss, ".brand-page").ToList();
        titleRules.Should().NotBeEmpty(".brand-page must still be styled");
        titleRules.Should().OnlyContain(
            rule => !rule.Contains("opacity", StringComparison.Ordinal),
            "any opacity below 1 on the title drops white-on-#0f6cbd under AA (0.85 composites to 4.32:1)");

        var layoutCss = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SchoolCollab.Admin", "Components", "Layout", "MainLayout.razor.css"));
        CssBlocksFor(layoutCss, ".layout-header-stack")
            .Should().Contain(
                rule => rule.Contains("--colorNeutralForegroundOnBrand", StringComparison.Ordinal),
                "the bar's own container sets the on-brand colour so no child inherits the page's");
    }
}
