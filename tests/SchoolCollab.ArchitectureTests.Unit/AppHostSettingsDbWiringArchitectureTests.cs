using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the AppHost wiring that no standalone-run recipe can catch: a host whose
/// <c>Program.cs</c> registers the Settings bounded context
/// (<c>AddSettingsCore</c> — needed for <c>IEntityCodeGenerator</c>, which reads
/// <c>EntityCodeRule</c> rows from <c>settings-db</c>) MUST also carry
/// <c>.WithReference(settingsDb)</c> in its AppHost resource chain. Without it,
/// <c>SchoolCollab.Settings.Core/Extensions.cs</c> silently falls back to
/// <c>Host=localhost;Port=5432;Database=schoolcollab_settings</c>, so the Settings
/// outbox dispatcher registered by <c>AddSettingsCore</c> logs a connection error
/// on every retry (measured: ~1000 lines within minutes from a single cold start)
/// and every entity-code generation attempt fails — while the host still reports
/// Healthy, because Aspire only probes the endpoint.
///
/// <para>
/// Discovered during the ar-21 orchestrated cold start (the first time the AppHost
/// started on this machine at all): <c>assignments-api</c> logged 1029 such errors
/// and <c>students-api</c> 1030, while <c>settings-api</c> — which does reference
/// <c>settingsDb</c> — logged 0.
/// </para>
///
/// <para>
/// The scan resolves "which projects register Settings.Core" by looking at real
/// <c>Program.cs</c> files under <c>src/</c> (skipping build output) rather than by
/// mangling the project constant into a directory name: a stale
/// <c>src/SchoolCollab.AI.Server/{bin,obj}</c> leftover from the AI project's move to
/// <c>src/AI/</c> otherwise makes the directory lookup ambiguous. The walk-up helper
/// follows the <c>AppHostRealmImportArchitectureTests</c> / <c>SeedCsvArchitectureTests</c>
/// precedent and THROWS rather than passing vacuously when it cannot find the repo.
/// </para>
/// </summary>
[TestClass]
public class AppHostSettingsDbWiringArchitectureTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly IReadOnlyList<ProjectResourceChain> ProjectResources = ParseProjectResourceChains();

    private static readonly HashSet<string> ProjectsRegisteringSettingsCore = FindProjectsRegisteringSettingsCore();

    private static readonly string[] SettingsCoreResourceNames = [.. ProjectResources
        .Where(resource => ProjectsRegisteringSettingsCore.Contains(ProjectNameOf(resource.ProjectConstant)))
        .Select(resource => resource.ResourceName)
        .OrderBy(name => name, StringComparer.Ordinal)];

    [TestMethod]
    public void EveryHostRegisteringSettingsCore_ReferencesSettingsDb()
    {
        foreach (var resource in ProjectResources)
        {
            if (!ProjectsRegisteringSettingsCore.Contains(ProjectNameOf(resource.ProjectConstant)))
            {
                continue;
            }

            resource.Chain.Should().Contain("WithReference(settingsDb)",
                $"{resource.ResourceName} calls AddSettingsCore in its Program.cs, so the AppHost must inject "
                + "ConnectionStrings:settings-db — otherwise Settings.Core falls back to localhost:5432 and the "
                + "Settings outbox dispatcher fails on every retry (seen in the ar-21 cold start).");
        }
    }

    [TestMethod]
    public void SettingsCoreHosts_AreTheReviewedSet()
    {
        // A tripwire, not a tautology: the assertion above is skipped for any host the
        // scan does not recognise, so this pins the set it actually inspected. A new
        // host registering Settings.Core must update this list consciously (and get the
        // settings-db reference decision reviewed).
        SettingsCoreResourceNames.Should().BeEquivalentTo(
            ["assignments-api", "settings-api", "students-api"],
            "these are the AppHost project resources whose Program.cs calls AddSettingsCore.");
    }

    [TestMethod]
    public void Scan_ActuallyFoundTheAppHostAndTheSettingsCoreProjects()
    {
        // Non-vacuity guard: if the AddProject regex or the src/ scan stops matching, the
        // assertions above would pass while inspecting nothing.
        ProjectResources.Should().HaveCountGreaterThanOrEqualTo(6,
            "the AppHost declares at least six AddProject resources; fewer means the parser found no real chains.");

        ProjectResources.Select(resource => resource.ResourceName)
            .Should().Contain(["assignments-api", "settings-api", "students-api", "migrator"]);

        ProjectsRegisteringSettingsCore.Should().Contain(
            ["SchoolCollab.Assignments.Api", "SchoolCollab.Settings.Api", "SchoolCollab.Students.Api"],
            "these projects call AddSettingsCore and must be discovered by the src/ Program.cs scan.");
    }

    private sealed record ProjectResourceChain(string ProjectConstant, string ResourceName, string Chain);

    /// <summary>Projects.SchoolCollab_Assignments_Api → SchoolCollab.Assignments.Api</summary>
    private static string ProjectNameOf(string projectConstant)
        => projectConstant.Replace('_', '.');

    private static IReadOnlyList<ProjectResourceChain> ParseProjectResourceChains()
    {
        var program = StripLineComments(
            File.ReadAllText(Path.Combine(RepoRoot, "src", "AppHost", "SchoolCollab.AppHost", "Program.cs")));

        // e.g. builder.AddProject<Projects.SchoolCollab_Assignments_Api>("assignments-api")
        var matches = Regex.Matches(program, """AddProject<Projects\.([A-Za-z0-9_]+)>\("([^"]+)"\)""");

        var chains = new List<ProjectResourceChain>();
        foreach (Match match in matches)
        {
            // A fluent AddProject chain contains no statement terminator, so the next
            // ';' ends it (WireKeycloakAuth(...) calls follow as separate statements).
            var end = program.IndexOf(';', match.Index);
            if (end < 0)
            {
                throw new InvalidOperationException(
                    $"Unterminated AddProject chain for '{match.Groups[2].Value}' in the AppHost Program.cs.");
            }

            chains.Add(new ProjectResourceChain(
                match.Groups[1].Value,
                match.Groups[2].Value,
                program[match.Index..end]));
        }

        return chains;
    }

    private static HashSet<string> FindProjectsRegisteringSettingsCore()
    {
        var srcDir = Path.Combine(RepoRoot, "src");

        return Directory
            .GetFiles(srcDir, "Program.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutputPath(path))
            .Where(path => File.ReadAllText(path).Contains("AddSettingsCore(", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(Path.GetDirectoryName(path)!))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsBuildOutputPath(string path)
    {
        // bin/ and obj/ carry copied Program.cs files during a build.
        var segments = path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        return segments.Contains("bin", StringComparer.OrdinalIgnoreCase)
            || segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Removes trailing <c>//</c> comments before chain extraction. A comment inside a
    /// resource chain (e.g. one documenting the <c>localhost:5432</c> fallback, whose
    /// semicolon would otherwise look like the end of the statement) must not truncate
    /// the chain — that failure mode was hit while authoring this guard.
    /// </summary>
    private static string StripLineComments(string source)
        => string.Join('\n', source.Split('\n').Select(line =>
        {
            var commentAt = line.IndexOf("//", StringComparison.Ordinal);
            return commentAt >= 0 ? line[..commentAt] : line;
        }));

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
