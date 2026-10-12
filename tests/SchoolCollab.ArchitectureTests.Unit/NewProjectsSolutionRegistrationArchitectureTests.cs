using System.Xml.Linq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards against orphaned projects — the <c>SchoolCollab.Students.Tests.Integration</c>
/// lesson (an integration suite that has been out of every solution file for a long
/// time, so its breakage never surfaced in CI). The solutions are the XML
/// <c>*.slnx</c> files and they have NO globbing: every <c>src/**/*.csproj</c> and
/// <c>tests/**/*.csproj</c> present on disk must appear in a solution explicitly, or the
/// project silently drops out of solution build/test runs. New projects (e.g.
/// <c>src/SchoolCollab.Auth</c>) must therefore be registered in the same change
/// set — this guard is what forces it.
/// <para>
/// <b>Registration is "in ANY checked-in <c>*.slnx</c>", not "in <c>SchoolCollab.slnx</c>".</b>
/// The mobile train added a second solution (<c>src/Mobile/SchoolCollab.Mobile.slnx</c>)
/// whose MAUI head cannot build on the ubuntu runners and so must stay out of the root
/// solution — but it is not orphaned, and listing it in
/// <see cref="KnownOrphanedProjects"/> would be a lie. The guard's purpose is preserved
/// by unioning every solution: a project in <i>no</i> solution still fails, which is the
/// failure mode this test exists for. Each solution's <c>Project Path</c> values resolve
/// against <i>that solution's own directory</i> (the mobile solution's paths are relative
/// to <c>src/Mobile/</c>) and are then normalized to the repo-root-relative form the
/// on-disk walk produces.
/// </para>
/// The on-disk set is derived by walking the repo (skipping <c>bin</c>/<c>obj</c>), and both
/// sets are asserted non-empty so the guard cannot pass vacuously. The discovery helper
/// follows the <c>AppHostRealmImportArchitectureTests</c> precedent (walk-up from
/// <c>AppContext.BaseDirectory</c>) and THROWS when the root solution cannot be found.
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

    /// <summary>
    /// Directories the <c>*.slnx</c> walk never descends into: build output, VCS metadata, and
    /// package caches. None of them can hold a checked-in solution.
    /// </summary>
    private static readonly string[] SkippedDirectories =
    [
        "bin", "obj", ".git", ".vs", "node_modules", ".venv", "TestResults", "artifacts",
    ];

    [TestMethod]
    public void EveryProjectOnDisk_IsRegisteredInASolution()
    {
        var repoRoot = FindRepoRoot();

        var onDisk = EnumerateProjects(repoRoot).ToList();
        onDisk.Should().NotBeEmpty("the on-disk project walk must never pass vacuously by finding nothing.");

        var solutionFiles = EnumerateSolutionFiles(repoRoot).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        solutionFiles.Should().NotBeEmpty("the repository must contain at least the root SchoolCollab.slnx.");

        var registered = ReadRegisteredProjectPaths(solutionFiles, repoRoot);
        registered.Should().NotBeEmpty("a .slnx lists its projects explicitly (it has no globbing).");

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
            "every src/** and tests/** project on disk must be registered in any checked-in .slnx — unregistered: " +
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

    /// <summary>
    /// Every checked-in <c>*.slnx</c> under the repository root, skipping
    /// <see cref="SkippedDirectories"/>. There is more than one solution since the mobile
    /// train added <c>src/Mobile/SchoolCollab.Mobile.slnx</c>.
    /// </summary>
    private static IEnumerable<string> EnumerateSolutionFiles(string repoRoot)
    {
        var pending = new Stack<string>();
        pending.Push(repoRoot);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            foreach (var solutionFile in Directory.EnumerateFiles(directory, "*.slnx"))
            {
                yield return solutionFile;
            }

            foreach (var subdirectory in Directory.EnumerateDirectories(directory))
            {
                if (SkippedDirectories.Contains(Path.GetFileName(subdirectory), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                pending.Push(subdirectory);
            }
        }
    }

    /// <summary>
    /// Reads every <c>Project Path=</c> attribute from every solution file, resolving each
    /// value against <i>that solution's own directory</i> (a .slnx lists its projects
    /// relative to itself — the mobile solution's paths are relative to <c>src/Mobile/</c>)
    /// and normalizing the result to a repo-root-relative forward-slash path so it can be
    /// compared with the on-disk walk.
    /// </summary>
    private static HashSet<string> ReadRegisteredProjectPaths(
        IEnumerable<string> solutionFiles,
        string repoRoot)
    {
        var registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var solutionFile in solutionFiles)
        {
            var solutionDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionFile))!;
            var doc = XDocument.Load(solutionFile);
            var paths = doc.Descendants("Project")
                .Select(e => e.Attribute("Path")?.Value)
                .Where(p => p is not null);

            foreach (var path in paths)
            {
                var absolute = Path.GetFullPath(Path.Combine(solutionDirectory, path!));
                registered.Add(Normalize(Path.GetRelativePath(repoRoot, absolute)));
            }
        }

        return registered;
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
