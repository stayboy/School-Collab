using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the AppHost wiring that no build or standalone-run recipe catches: a host whose
/// <c>Program.cs</c> calls a module <c>Add{Layer}()Core</c> extension that registers the shared
/// transactional outbox MUST also carry <c>.WithEnvironment("Outbox__ExchangeName", …)</c> in its
/// AppHost resource chain. <c>AddOutbox&lt;TContext&gt;</c> binds <c>OutboxOptions</c> from the
/// <c>Outbox</c> section and calls <c>ValidateOnStart()</c> on <c>ExchangeName</c>, so an omitted
/// value is a hard startup failure — not a degraded mode.
///
/// <para>
/// Found the hard way (2026-09-22): <c>assignments-worker</c> threw
/// <c>OptionsValidationException: ExchangeName must be set in the 'Outbox' configuration section</c>
/// at <c>host.Run()</c>, because the AppHost block set only <c>RabbitMq__Subscriber__ExchangeName</c>
/// while the AppHost's own comment above the outbox parameters claims the exchange names are fanned
/// out to the matching API/Worker. Aspire reported the dead worker as <c>Finished</c>, which reads
/// like a normal one-shot resource, so the failure was easy to misread as by-design.
/// </para>
///
/// <para>
/// The scan resolves "which hosts register the outbox" in two steps, both from real source under
/// <c>src/</c> — never from the AppHost's own text, which would make the assertion tautological:
/// find the <c>Add{Layer}()Core</c> extensions that call <c>services.AddOutbox&lt;</c>, then find the
/// <c>Program.cs</c> files that call those extensions. The helpers follow the
/// <c>AppHostSettingsDbWiringArchitectureTests</c> / <c>SeedCsvArchitectureTests</c> precedent and
/// THROW rather than passing vacuously when the repo or a scan target cannot be found.
/// </para>
/// </summary>
[TestClass]
public class AppHostOutboxExchangeWiringArchitectureTests
{
    /// <summary>The env-var injection every outbox-registering host must carry.</summary>
    private const string ExpectedInjection = "WithEnvironment(\"Outbox__ExchangeName\"";

    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly IReadOnlyList<ProjectResourceChain> ProjectResources = ParseProjectResourceChains();

    /// <summary>Repo-relative source path → the <c>Add{Layer}()Core</c> method declared in that file.</summary>
    private static readonly IReadOnlyDictionary<string, string> OutboxModuleCores = FindOutboxRegisteringModuleCores();

    private static readonly HashSet<string> ProjectsRegisteringOutbox =
        FindHostProjectsCallingCoreMethods(OutboxModuleCores.Values);

    private static readonly string[] OutboxResourceNames = [.. ProjectResources
        .Where(resource => ProjectsRegisteringOutbox.Contains(ProjectNameOf(resource.ProjectConstant)))
        .Select(resource => resource.ResourceName)
        .OrderBy(name => name, StringComparer.Ordinal)];

    [TestMethod]
    public void OutboxRegisteringHosts_GetOutboxExchangeName()
    {
        foreach (var resource in ProjectResources)
        {
            if (!ProjectsRegisteringOutbox.Contains(ProjectNameOf(resource.ProjectConstant)))
            {
                continue;
            }

            resource.Chain.Should().Contain(ExpectedInjection,
                $"{resource.ResourceName} calls a module Add{{Layer}}Core in its Program.cs, and that "
                + "extension registers the shared OutboxDispatcher — so the AppHost must inject "
                + "Outbox__ExchangeName, or OutboxOptions validation fails the host at startup with "
                + "\"ExchangeName must be set in the 'Outbox' configuration section\" (exactly how "
                + "assignments-worker was found dead in the 2026-09-22 cold start).");
        }
    }

    [TestMethod]
    public void OutboxRegisteringHosts_AreTheReviewedSet()
    {
        // A tripwire, not a tautology: the assertion above is skipped for any host the scan does not
        // recognise, so this pins the set it actually inspected. A new host registering the outbox
        // must update this list consciously (and get the exchange-name decision reviewed).
        OutboxResourceNames.Should().BeEquivalentTo(
            ["assignments-api", "assignments-worker", "settings-api", "students-api", "students-worker"],
            "these are the AppHost project resources whose Program.cs calls an Add{Layer}Core that registers the outbox.");
    }

    [TestMethod]
    public void Scan_ActuallyFoundTheAppHostAndTheOutboxHosts()
    {
        // Non-vacuity guard: if the AddProject regex or either src/ scan stops matching, the
        // assertions above would pass while inspecting nothing.
        ProjectResources.Should().HaveCountGreaterThanOrEqualTo(6,
            "the AppHost declares at least six AddProject resources; fewer means the parser found no real chains.");

        ProjectResources.Select(resource => resource.ResourceName)
            .Should().Contain(["assignments-api", "assignments-worker", "migrator"]);

        OutboxModuleCores.Values.Should().Contain(
            ["AddAssignmentsCore", "AddSettingsCore", "AddStudentsCore"],
            "these three module cores call services.AddOutbox<> and must be discovered by the src/ scan.");

        ProjectsRegisteringOutbox.Should().Contain(
            [
                "SchoolCollab.Assignments.Api", "SchoolCollab.Assignments.Worker",
                "SchoolCollab.Settings.Api", "SchoolCollab.Students.Api", "SchoolCollab.Students.Worker",
            ],
            "these projects call an outbox-registering Add{Layer}Core and must be discovered by the src/ Program.cs scan.");
    }

    [TestMethod]
    public void ModuleCoresRegisteringTheOutbox_AreTheReviewedSet()
    {
        // Pins the registration side of the derivation. A fourth module core that starts calling
        // services.AddOutbox<> makes its hosts subject to the Outbox__ExchangeName requirement too,
        // so it must trip this and force that decision to be reviewed rather than silently changing
        // the set of hosts the first test inspects.
        OutboxModuleCores.Select(core => $"{core.Key} => {core.Value}").Should().BeEquivalentTo(
            [
                "src/Assignments/SchoolCollab.Assignments.Core/Extensions.cs => AddAssignmentsCore",
                "src/Settings/SchoolCollab.Settings.Core/Extensions.cs => AddSettingsCore",
                "src/Students/SchoolCollab.Students.Core/Extensions.cs => AddStudentsCore",
            ],
            "these are the module cores that register the transactional outbox.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────

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

    /// <summary>
    /// Resolves every module core that registers the transactional outbox: the
    /// <c>Add{Layer}()Core</c> extension declared in a file that calls <c>services.AddOutbox&lt;</c>.
    /// THROWS when such a file declares no such method, so a new registrar cannot be skipped
    /// silently.
    /// </summary>
    private static IReadOnlyDictionary<string, string> FindOutboxRegisteringModuleCores()
    {
        var srcDir = Path.Combine(RepoRoot, "src");
        var cores = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var path in Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutputPath(path))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            if (!text.Contains("services.AddOutbox<", StringComparison.Ordinal))
            {
                continue;
            }

            var declaration = Regex.Match(text, @"public static IServiceCollection (Add[A-Za-z0-9_]*Core)\(");
            if (!declaration.Success)
            {
                throw new InvalidOperationException(
                    $"{path} calls services.AddOutbox<> but declares no Add{{Layer}}Core extension method, so its "
                    + "hosts cannot be discovered — give it one, or extend this guard deliberately.");
            }

            cores[RelativePathOf(path)] = declaration.Groups[1].Value;
        }

        if (cores.Count == 0)
        {
            throw new InvalidOperationException(
                $"No file under {srcDir} calls services.AddOutbox<> — the outbox-registration scan found nothing.");
        }

        return cores;
    }

    /// <summary>
    /// Resolves the projects whose <c>Program.cs</c> calls any of the supplied
    /// <c>Add{Layer}()Core</c> extension methods.
    /// </summary>
    private static HashSet<string> FindHostProjectsCallingCoreMethods(IEnumerable<string> coreMethodNames)
    {
        var srcDir = Path.Combine(RepoRoot, "src");
        var names = coreMethodNames.ToArray();

        return Directory
            .GetFiles(srcDir, "Program.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutputPath(path))
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return names.Any(name => text.Contains(name + "(", StringComparison.Ordinal));
            })
            .Select(path => Path.GetFileName(Path.GetDirectoryName(path)!))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string RelativePathOf(string path)
        => Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');

    private static bool IsBuildOutputPath(string path)
    {
        // bin/ and obj/ carry copied sources during a build.
        var segments = path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        return segments.Contains("bin", StringComparer.OrdinalIgnoreCase)
            || segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Removes trailing <c>//</c> comments before chain extraction. A comment inside a resource
    /// chain (e.g. one whose semicolon would otherwise look like the end of the statement) must not
    /// truncate the chain — that failure mode was hit while authoring the sibling guard.
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
