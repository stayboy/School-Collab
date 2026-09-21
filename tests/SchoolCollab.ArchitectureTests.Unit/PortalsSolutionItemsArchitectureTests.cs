using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the one piece of the portal that the .NET build cannot see: its entries in
/// <c>SchoolCollab.slnx</c>.
///
/// <para>
/// The portal (<c>src/SchoolCollab.Portals/</c>) is Python, launched by the AppHost through
/// <c>AddUvicornApp</c>, so it contributes no project to the solution. It is surfaced in the
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

    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly string[] SolutionFileItems = ParseSolutionFileItems();

    private static readonly string[] PortalSolutionFolderNodes = ParsePortalSolutionFolderNodes();

    private static readonly string[] PortalSourceFiles = EnumeratePortalSourceFiles();

    [TestMethod]
    public void EveryPortalFile_IsListedInTheSolution()
    {
        // Anti-vacuity: with an empty walk the assertion below would pass trivially.
        PortalSourceFiles.Should().NotBeEmpty(
            "src/SchoolCollab.Portals should exist and hold the portal's Python sources");

        var listed = SolutionFileItems.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unlisted = PortalSourceFiles.Where(file => !listed.Contains(file)).ToArray();

        unlisted.Should().BeEmpty(
            "every portal file belongs in the solution view, and .slnx has no glob support — "
            + "add each one as a <File> under /src/SchoolCollab.Portals/<package>/. Unlisted: "
            + string.Join(", ", unlisted));
    }

    [TestMethod]
    public void EveryPortalSolutionItem_ExistsOnDisk()
    {
        var missing = SolutionFileItems
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
        SolutionFileItems.Should().BeEquivalentTo(
            [
                "src/SchoolCollab.Portals/app.py",
                "src/SchoolCollab.Portals/api/__init__.py",
                "src/SchoolCollab.Portals/api/assignments_api_client.py",
                "src/SchoolCollab.Portals/api/dto.py",
                "src/SchoolCollab.Portals/api/errors.py",
                "src/SchoolCollab.Portals/api/service_discovery.py",
                "src/SchoolCollab.Portals/pyproject.toml",
                "src/SchoolCollab.Portals/tests/test_app_routes.py",
                "src/SchoolCollab.Portals/tests/test_assignments_api_client.py",
                "src/SchoolCollab.Portals/tests/test_service_discovery.py",
                "src/SchoolCollab.Portals/uv.lock",
                "src/SchoolCollab.Portals/views/__init__.py",
                "src/SchoolCollab.Portals/views/ward.py",
            ],
            "these are the reviewed portal solution items.");
    }

    [TestMethod]
    public void PortalSolutionFolderNodes_MirrorThePackageLayout()
    {
        // The solution view nests by path prefix, so a single flat folder node would hide the
        // api/views/tests split the refactor introduced.
        PortalSolutionFolderNodes.Should().BeEquivalentTo(
            [
                "/src/SchoolCollab.Portals/",
                "/src/SchoolCollab.Portals/api/",
                "/src/SchoolCollab.Portals/tests/",
                "/src/SchoolCollab.Portals/views/",
            ],
            "the portal's solution folder nodes should mirror its package layout.");
    }

    private static string ReadSolutionFile() => File.ReadAllText(Path.Combine(RepoRoot, "SchoolCollab.slnx"));

    private static string[] ParseSolutionFileItems() =>
        [.. Regex.Matches(ReadSolutionFile(), "<File\\s+Path=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value.Replace('\\', '/'))
            .Where(path => path.StartsWith(PortalFolderPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];

    private static string[] ParsePortalSolutionFolderNodes() =>
        [.. Regex.Matches(ReadSolutionFile(), "<Folder\\s+Name=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value.Replace('\\', '/'))
            .Where(name => name.StartsWith("/src/SchoolCollab.Portals/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];

    private static string[] EnumeratePortalSourceFiles()
    {
        var portalDirectory = Path.Combine(RepoRoot, "src", "SchoolCollab.Portals");
        if (!Directory.Exists(portalDirectory))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(portalDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(RepoRoot, path).Replace('\\', '/'))
            .Where(IsPortalSourceFile)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];
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
