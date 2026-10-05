using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the portal's .NET-visible seams: its explicit entries in <c>SchoolCollab.slnx</c>
/// (the .NET build cannot see Python files otherwise) and the durable facts recorded in
/// <c>documents/specs/teachers-ward-portal-prefab-plan.md</c> that are only enforceable by
/// source inspection.
///
/// <para>
/// The portal projects (<c>src/SchoolCollab.Portals/</c> and <c>src/SchoolCollab.AuthPortal/</c>)
/// are Python, launched by the AppHost through
/// <c>AddUvicornApp</c>, so they contribute no project to the solution. They are surfaced in the
/// Visual Studio solution view as explicit solution items instead — and <b>.slnx has no
/// globbing support</b> (Visual Studio's FAQ: "globbing is not currently supported"; the PR
/// that would have added it was closed), so every file is a hand-maintained
/// <c>&lt;File&gt;</c> path. Without this guard the list rots: it was already stale one
/// refactor after it was introduced — the <c>api/</c>, <c>views/</c> and <c>tests/</c>
/// packages were missing from the solution view.
/// </para>
///
/// <para>
/// The walk-up helper follows the <c>AppHostRealmImportArchitectureTests</c> /
/// <c>AppHostSettingsDbWiringArchitectureTests</c> precedent and THROWS rather than passing
/// vacuously when it cannot find the repository root.
/// </para>
/// </summary>
[TestClass]
public class PortalsSolutionItemsArchitectureTests
{
    private const string PortalFolderPrefix = "src/SchoolCollab.Portals/";

    private const string AuthPortalFolderPrefix = "src/SchoolCollab.AuthPortal/";

    /// <summary>
    /// Every portal project whose Python files must appear as explicit solution items. The walk
    /// and both solution-item assertions follow from this list — adding a portal project means
    /// adding its prefix here once, not re-deriving the guard.
    /// </summary>
    private static readonly string[] PortalFolderPrefixes = [PortalFolderPrefix, AuthPortalFolderPrefix];

    /// <summary>
    /// The same projects spelled the way solution folder nodes are: a node's name is the folder
    /// path with a leading slash ("src/SchoolCollab.Portals/" on disk is
    /// "/src/SchoolCollab.Portals/" in the solution view).
    /// </summary>
    private static readonly string[] PortalFolderNodePrefixes =
        [.. PortalFolderPrefixes.Select(prefix => "/" + prefix)];

    private static readonly string RepoRoot = FindRepoRoot();

    /// <summary>
    /// The ward portal's solution items. Its reviewed-set tripwire pins exactly this array, so the
    /// pin stays scoped to <c>src/SchoolCollab.Portals/</c> while the assertions above cover every
    /// portal project.
    /// </summary>
    private static readonly string[] SolutionFileItems = ParseSolutionFileItems(PortalFolderPrefix);

    private static readonly string[] AllPortalSolutionFileItems = ParseSolutionFileItems(PortalFolderPrefixes);

    /// <summary>
    /// The solution folder nodes of <b>every</b> portal project. B8's amendment: this used to walk
    /// <c>src/SchoolCollab.Portals/</c> alone, so the auth portal's nodes — which only exist once
    /// its <c>views/</c> package does — were pinned nowhere and a missing/mistyped node would ship
    /// unguarded. Both prefixes now feed one list, mirroring <see cref="AllPortalSolutionFileItems"/>.
    /// </summary>
    private static readonly string[] AllPortalSolutionFolderNodes = ParsePortalSolutionFolderNodes();

    private static readonly string[] PortalSourceFiles = EnumeratePortalSourceFiles();

    private static readonly string SpecPath = Path.Combine(RepoRoot, "documents", "specs", "teachers-ward-portal-prefab-plan.md");

    private static readonly string Spec = File.ReadAllText(SpecPath);

    /// <summary>The T1 decision row in the teacher-portal grill table. Scoped so an assertion
    /// about the flat-module fact cannot be satisfied by the Date line or a different section.</summary>
    private static readonly string TeacherPortalT1Row = ExtractTeacherPortalT1Row(Spec);

    [TestMethod]
    public void EveryPortalFile_IsListedInTheSolution()
    {
        // Anti-vacuity: with an empty walk — or one that reaches only one of the portal projects —
        // the assertion below would pass trivially for whichever project it missed.
        PortalSourceFiles.Should().NotBeEmpty(
            "every portal project should exist and hold its Python sources");

        foreach (var prefix in PortalFolderPrefixes)
        {
            PortalSourceFiles.Should().Contain(
                file => file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase),
                $"the on-disk walk must reach {prefix}");
        }

        var listed = AllPortalSolutionFileItems.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unlisted = PortalSourceFiles.Where(file => !listed.Contains(file)).ToArray();

        unlisted.Should().BeEmpty(
            "every portal file belongs in the solution view, and .slnx has no glob support — "
            + "add each one as a <File> under /src/SchoolCollab.<Portal>/<package>/. Unlisted: "
            + string.Join(", ", unlisted));
    }

    [TestMethod]
    public void EveryPortalSolutionItem_ExistsOnDisk()
    {
        var missing = AllPortalSolutionFileItems
            .Where(item => !File.Exists(Path.Combine(RepoRoot, item.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();

        missing.Should().BeEmpty(
            "a stale <File> path shows up as a broken node in the Visual Studio solution view: "
            + string.Join(", ", missing));
    }

    [TestMethod]
    public void PortalSolutionItems_AreTheReviewedSet()
    {
        // A tripwire, not a tautology: the first test only proves disk ⊆ slnx, so this pins the
        // slnx set itself. Adding or removing a portal file must update this list consciously —
        // and with it the solution items.
        //
        // Round `portal-session-adoption` added two files here: the auth-service client and the
        // session-path test module. Both land under existing `api/` and `tests/` package nodes, so
        // PortalSolutionFolderNodes_MirrorThePackageLayout is deliberately unchanged — a new folder
        // node would be a package-layout change, which this pair of files is not.
        SolutionFileItems.Should().BeEquivalentTo(
            [
                "src/SchoolCollab.Portals/app.py",
                "src/SchoolCollab.Portals/api/__init__.py",
                "src/SchoolCollab.Portals/api/assignments_api_client.py",
                "src/SchoolCollab.Portals/api/auth_api_client.py",
                "src/SchoolCollab.Portals/api/dto.py",
                "src/SchoolCollab.Portals/api/errors.py",
                "src/SchoolCollab.Portals/api/service_discovery.py",
                "src/SchoolCollab.Portals/pyproject.toml",
                "src/SchoolCollab.Portals/tests/test_app_routes.py",
                "src/SchoolCollab.Portals/tests/test_assignments_api_client.py",
                "src/SchoolCollab.Portals/tests/test_service_discovery.py",
                "src/SchoolCollab.Portals/tests/test_teacher_grade.py",
                "src/SchoolCollab.Portals/tests/test_teacher_session.py",
                "src/SchoolCollab.Portals/tests/test_teacher_views.py",
                "src/SchoolCollab.Portals/uv.lock",
                "src/SchoolCollab.Portals/views/__init__.py",
                "src/SchoolCollab.Portals/views/teacher.py",
                "src/SchoolCollab.Portals/views/ward.py",
            ],
            "these are the reviewed portal solution items.");
    }

    [TestMethod]
    public void PortalSolutionFolderNodes_MirrorThePackageLayout()
    {
        // The solution view nests by path prefix, so a single flat folder node would hide the
        // api/views/tests split — and a node the .slnx forgot to rename would leave the portal's
        // files ungrouped. Both portal projects are pinned, because a node that is missing is
        // exactly as invisible as a node that is wrong.
        AllPortalSolutionFolderNodes.Should().BeEquivalentTo(
            [
                "/src/SchoolCollab.AuthPortal/",
                "/src/SchoolCollab.AuthPortal/api/",
                "/src/SchoolCollab.AuthPortal/tests/",
                "/src/SchoolCollab.AuthPortal/views/",
                "/src/SchoolCollab.Portals/",
                "/src/SchoolCollab.Portals/api/",
                "/src/SchoolCollab.Portals/tests/",
                "/src/SchoolCollab.Portals/views/",
            ],
            "each portal's solution folder nodes should mirror its package layout.");
    }

    /// <summary>The open-questions section where the landed CI job is recorded. Scoped so the
    /// Date line cannot satisfy the job assertion.</summary>
    private static readonly string OpenQuestionsSection = ExtractOpenQuestionsSection(Spec);

    [TestMethod]
    public void TeacherPortalSpec_RecordsFlatModule()
    {
        // Scoped to the T1 row so the Date line (which mentions the same phrase) cannot satisfy it.
        TeacherPortalT1Row.Should().Contain("views/teacher.py",
            "the T1 row must record the flat teacher module path in {0}", SpecPath);

        // Independently checkable disk facts: the flat module exists and the package form does not.
        File.Exists(Path.Combine(RepoRoot, "src/SchoolCollab.Portals/views/teacher.py"))
            .Should().BeTrue("the flat module src/SchoolCollab.Portals/views/teacher.py must exist on disk");

        Directory.Exists(Path.Combine(RepoRoot, "src/SchoolCollab.Portals/views/teacher"))
            .Should().BeFalse("the teacher view must not be a package at src/SchoolCollab.Portals/views/teacher/");
    }

    [TestMethod]
    public void TeacherPortalSpec_RecordsCiJobLanded()
    {
        // Assert the durable job identifier inside §7, not the surrounding prose, so edits like
        // inserting "CI" do not flip the guard while the fact remains true. The Date line is
        // deliberately excluded so the assertion cannot be satisfied by a changelog entry.
        OpenQuestionsSection.Should().Contain("portals-python",
            "the open-questions section must record the landed portal CI job id in {0}", SpecPath);

        // Hermetic cross-check against the workflow file that declares the job.
        var workflowPath = Path.Combine(RepoRoot, ".github/workflows/ci.yml");
        File.ReadAllText(workflowPath)
            .Should().Contain("portals-python:",
                "the portals-python job must still be declared in {0}", workflowPath);
    }

    private static string ExtractTeacherPortalT1Row(string spec)
    {
        // Match the grill-me table row whose Branch is T1; capture everything from | T1 | to the
        // end of that line (the row may contain `views/teacher/` inside a negation).
        var match = Regex.Match(spec, @"\|\s*T1\s*\|.*?(\r?\n)", RegexOptions.Singleline);

        match.Success.Should().BeTrue(
            "the teacher-portal T1 decision row must exist in {0}", SpecPath);

        return match.Value;
    }

    private static string ExtractOpenQuestionsSection(string spec)
    {
        // Capture the "Open questions → resolved / remaining" section to the end of the file,
        // so the CI-job assertion is scoped to the durable record and not the Date line.
        // Deliberately `\d+` for the section number (the AssignmentAuthoringSpecGapsTests precedent):
        // a renumber must not redden six tests for a docs-only edit.
        var match = Regex.Match(spec, @"^## \d+\.\s+Open questions.*$", RegexOptions.Multiline);

        match.Success.Should().BeTrue(
            "the open-questions section (§7) must exist in {0}", SpecPath);

        return spec[match.Index..];
    }

    private static string ReadSolutionFile() => File.ReadAllText(Path.Combine(RepoRoot, "SchoolCollab.slnx"));

    private static string[] ParseSolutionFileItems(params string[] prefixes) =>
        [.. Regex.Matches(ReadSolutionFile(), "<File\\s+Path=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value.Replace('\\', '/'))
            .Where(path => prefixes.Any(prefix =>
                path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];

    private static string[] ParsePortalSolutionFolderNodes() =>
        [.. Regex.Matches(ReadSolutionFile(), "<Folder\\s+Name=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value.Replace('\\', '/'))
            .Where(name => PortalFolderNodePrefixes.Any(prefix =>
                name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];

    private static string[] EnumeratePortalSourceFiles()
    {
        var files = new List<string>();
        foreach (var prefix in PortalFolderPrefixes)
        {
            var portalDirectory = Path.Combine(RepoRoot, prefix.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(portalDirectory))
            {
                continue;
            }

            files.AddRange(Directory.EnumerateFiles(portalDirectory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(RepoRoot, path).Replace('\\', '/'))
                .Where(IsPortalSourceFile));
        }

        return [.. files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];
    }

    private static bool IsPortalSourceFile(string relativePath)
    {
        // Skip tooling output: .venv/, .pytest_cache/, __pycache__/, obj/, bin/.
        if (relativePath.Split('/').Any(segment =>
            segment.StartsWith('.') || segment is "__pycache__" or "obj" or "bin"))
        {
            return false;
        }

        return Path.GetExtension(relativePath) is ".py" or ".toml" or ".lock";
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "AppHost", "SchoolCollab.AppHost")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException(
                "Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
