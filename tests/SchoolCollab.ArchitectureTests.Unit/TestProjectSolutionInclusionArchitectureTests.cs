using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the discovery hole that let a whole test project rot unnoticed: a
/// <c>tests/**/*.csproj</c> that is absent from <c>SchoolCollab.slnx</c> is invisible to
/// <c>dotnet build</c> / <c>dotnet test</c> (CI uses no-arg single-solution discovery), so its
/// code is never compiled and its tests never run — while CI stays green.
///
/// <para>
/// Found live on 2026-09-25: <c>SchoolCollab.Students.Tests.Integration</c> (27 files, 15
/// endpoint suites) had been absent from every solution file since it was written, and in that
/// time it drifted into a hard compile error (<c>CS7036</c> — <c>PeriodDto</c> had gained a
/// constructor parameter) that nothing in the build could see.
/// </para>
///
/// <para>
/// The walk-up helper follows the <c>PortalsSolutionItemsArchitectureTests</c> precedent and
/// THROWS rather than passing vacuously when it cannot find the repository root.
/// </para>
/// </summary>
[TestClass]
public class TestProjectSolutionInclusionArchitectureTests
{
    private const string TestsFolderName = "tests";

    /// <summary>
    /// Anti-vacuity floor: a walk that found nothing (wrong root, renamed folder) must fail the
    /// guard rather than satisfy it. Deliberately a floor rather than an exact count, so adding a
    /// test project does not require editing this guard.
    /// </summary>
    private const int MinimumTestProjectCount = 10;

    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly string[] TestProjectPaths = EnumerateTestProjects();

    private static readonly string[] SolutionProjectPaths = ParseSolutionProjectPaths();

    [TestMethod]
    public void EveryTestProject_IsInTheSolution()
    {
        // Anti-vacuity: with an empty or wrongly-rooted walk the assertion below would pass
        // trivially for every project it never saw — the exact failure this guard exists to catch.
        TestProjectPaths.Should().NotBeEmpty(
            "the walk must find the test projects that exist on disk");
        TestProjectPaths.Length.Should().BeGreaterThanOrEqualTo(
            MinimumTestProjectCount,
            "a walk that finds almost nothing is broken, not satisfying the guard");
        TestProjectPaths.Should().Contain(
            path => path.Contains("SchoolCollab.ArchitectureTests.Unit", StringComparison.OrdinalIgnoreCase),
            "this guard's own project must be among the projects it walks");

        var listed = SolutionProjectPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unlisted = TestProjectPaths.Where(path => !listed.Contains(path)).ToArray();

        unlisted.Should().BeEmpty(
            "every test project belongs in SchoolCollab.slnx under /tests/ — an unlisted project is "
            + "never compiled or run, so it rots behind a green CI. Unlisted: "
            + string.Join(", ", unlisted));
    }

    private static string[] EnumerateTestProjects()
    {
        var testsDirectory = Path.Combine(RepoRoot, TestsFolderName);
        if (!Directory.Exists(testsDirectory))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(testsDirectory, "*.csproj", SearchOption.AllDirectories)
                .Where(path => !path
                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(segment => segment is "obj" or "bin"))
                .Select(path => Path.GetRelativePath(RepoRoot, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static string[] ParseSolutionProjectPaths() =>
        [.. Regex.Matches(
                File.ReadAllText(Path.Combine(RepoRoot, "SchoolCollab.slnx")),
                "<Project\\s+Path=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value.Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];

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
