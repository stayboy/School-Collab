using System.Xml.Linq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards against orphaned projects — the <c>SchoolCollab.Students.Tests.Integration</c>
/// lesson (an integration suite that has been out of every solution file for a long
/// time, so its breakage never surfaced in CI). The solution is the XML
/// <c>SchoolCollab.slnx</c> and has NO globbing: every <c>src/**/*.csproj</c> and
/// <c>tests/**/*.csproj</c> present on disk must appear there explicitly, or the
/// project silently drops out of solution build/test runs. New projects (e.g.
/// <c>src/SchoolCollab.Auth</c>) must therefore be registered in the same change
/// set — this guard is what forces it. The on-disk set is derived by walking the
/// repo (skipping <c>bin</c>/<c>obj</c>), and both sets are asserted non-empty so
/// the guard cannot pass vacuously. The discovery helper follows the
/// <c>AppHostRealmImportArchitectureTests</c> precedent (walk-up from
/// <c>AppContext.BaseDirectory</c>) and THROWS when the solution cannot be found.
/// </summary>
[TestClass]
public class NewProjectsSolutionRegistrationArchitectureTests
{
    /// <summary>
    /// Pre-existing orphans that are deliberately NOT in the solution. Currently the
    /// old Students integration suite, which does not compile against the current
    /// core DTOs (CS7036 vs <c>PeriodDto</c>) — registering it would break the
    /// solution build — and is superseded by <c>SchoolCollab.Students.Api.Tests.Unit</c>.
    /// The live existence check below forces this entry to be removed when the folder
    /// is finally deleted, so the exclusion cannot silently rot.
    /// </summary>
    private static readonly string[] KnownOrphanedProjects =
    [
        "tests/SchoolCollab.Students.Tests.Integration/SchoolCollab.Students.Tests.Integration.csproj",
    ];

    [TestMethod]
    public void EveryProjectOnDisk_IsRegisteredInTheSolution()
    {
        var repoRoot = FindRepoRoot();
        var solutionFile = Path.Combine(repoRoot, "SchoolCollab.slnx");

        var onDisk = EnumerateProjects(repoRoot).ToList();
        onDisk.Should().NotBeEmpty("the on-disk project walk must never pass vacuously by finding nothing.");

        var registered = ReadRegisteredProjectPaths(solutionFile);
        registered.Should().NotBeEmpty("SchoolCollab.slnx must list its projects explicitly (it has no globbing).");

        // The known orphans must still exist so the exclusion is live — deleting the
        // folder (the intended end state) fails this check and forces cleanup here.
        foreach (var orphan in KnownOrphanedProjects)
        {
            File.Exists(Path.Combine(repoRoot, orphan)).Should().BeTrue(
                $"known orphan '{orphan}' no longer exists on disk — remove it from {nameof(KnownOrphanedProjects)}.");
        }

        var orphans = KnownOrphanedProjects.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unregistered = onDisk
            .Where(p => !registered.Contains(p) && !orphans.Contains(p))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        unregistered.Should().BeEmpty(
            "every src/** and tests/** project on disk must be registered in SchoolCollab.slnx — unregistered: " +
            string.Join(", ", unregistered));
    }

    /// <summary>Walks <c>src/</c> and <c>tests/</c> for <c>*.csproj</c>, skipping any
    /// build-output tree, and yields repo-root-relative forward-slash paths.</summary>
    private static IEnumerable<string> EnumerateProjects(string repoRoot)
    {
        foreach (var subdir in new[] { "src", "tests" })
        {
            var root = Path.Combine(repoRoot, subdir);
            foreach (var file in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            {
                var segments = file.Split(Path.DirectorySeparatorChar);
                if (segments.Contains("bin") || segments.Contains("obj"))
                {
                    continue;
                }

                yield return Normalize(Path.GetRelativePath(repoRoot, file));
            }
        }
    }

    /// <summary>Reads every <c>Project Path=</c> attribute from the .slnx document,
    /// normalized to forward slashes.</summary>
    private static HashSet<string> ReadRegisteredProjectPaths(string solutionFile)
    {
        var doc = XDocument.Load(solutionFile);
        return doc.Descendants("Project")
            .Select(e => e.Attribute("Path")?.Value)
            .Where(p => p is not null)
            .Select(p => Normalize(p!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    /// <summary>Walk up from the test bin dir to the repository root. THROWS when the
    /// solution cannot be found so a moved/renamed file cannot pass by silently
    /// finding nothing.</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SchoolCollab.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                "Could not locate SchoolCollab.slnx from " + AppContext.BaseDirectory);
        }

        return dir.FullName;
    }
}
