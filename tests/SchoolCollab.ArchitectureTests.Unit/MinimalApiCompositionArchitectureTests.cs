using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the repo's two-layer minimal-API composition (spec <c>minimal-api-composition</c> §WP4,
/// pattern and rationale: <c>documents/solution/endpoint-organization-pattern.md</c>):
/// <list type="number">
///   <item>every <c>Map&lt;Specialty&gt;Routes(...)</c> call made from a <c>&lt;Domain&gt;Endpoints.cs</c>
///        orchestrator resolves to a specialty mapper declared <c>(this RouteGroupBuilder group)</c> —
///        so a specialty can only ever inherit the orchestrator group's prefix and policy;</item>
///   <item>an orchestrator maps NO routes itself — routes belong to the specialty files;</item>
///   <item>a specialty <c>*Routes.cs</c> file calls no <c>RequireAuthorization</c> — the authorization
///        policy is the orchestrator's (and the <c>RequireAuthIfOidc</c> helper's) single concern.</item>
/// </list>
/// Deliberately NOT asserted: <c>TypedResults</c> usage (the 558 legacy <c>Results.*</c> call sites the
/// round's D2 declined to sweep) and <c>.http</c> artifacts (declined, D6).
/// <para>
/// Every assertion message names the offending file. Follows the
/// <c>BearerForwardingWiringArchitectureTests</c> <c>FindRepoRoot()</c> walk-up precedent and throws
/// when the repo root cannot be located.
/// </para>
/// </summary>
[TestClass]
public class MinimalApiCompositionArchitectureTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    /// <summary>
    /// Mappers that deliberately do NOT take a <c>RouteGroupBuilder</c>. Each entry is the exemption the
    /// spec §WP2 exemption table records; the assertion still requires the declaration to exist.
    /// </summary>
    private static readonly Dictionary<string, string> ReceiverExemptions = new(StringComparer.Ordinal)
    {
        ["MapConfigResolveRoutes"] =
            "Endpoints/ConfigResolveRoutes.cs takes (this WebApplication app, bool oidcEnabled) — it cannot "
            + "see the group or the flag service (spec §WP2 exemption).",
    };

    /// <summary>
    /// Leaf endpoint groups: their <c>Map&lt;Domain&gt;Endpoints</c> method maps its routes itself because
    /// there is no specialty to delegate to. Converting them is new-and-touched work (D5), not this round's.
    /// </summary>
    private static readonly Dictionary<string, string> LeafGroupExemptions = new(StringComparer.Ordinal)
    {
        ["src/SchoolCollab.Auth/Endpoints/AdminRoleEndpoints.cs"] =
            "leaf group: the three /auth/admin/roles routes are mapped on an IEndpointRouteBuilder receiver.",
        ["src/SchoolCollab.Auth/Endpoints/AdminUserEndpoints.cs"] =
            "leaf group: the five /auth/admin/users routes are mapped on an IEndpointRouteBuilder receiver.",
        ["src/SchoolCollab.Auth/Endpoints/MediatedReadEndpoints.cs"] =
            "leaf group: the two portal picker reads are mapped on an IEndpointRouteBuilder receiver.",
        ["src/SchoolCollab.Families/DeepLinks/DeepLinkEndpoints.cs"] =
            "leaf group in the Families host: the single deeplink GET is mapped on a WebApplication receiver.",
        ["src/AI/SchoolCollab.AI.Server/Endpoints/AssignmentQuestionGenerationEndpoints.cs"] =
            "leaf group in the AI server: the single generation POST is mapped on a WebApplication receiver.",
    };

    /// <summary>
    /// A specialty file that may call <c>RequireAuthorization</c>: its only gated route is the tenant read,
    /// gated by a <c>bool oidcEnabled</c> parameter rather than the flag service (spec §WP2 exemption).
    /// </summary>
    private const string RouteAuthorizationExemption =
        "src/Settings/SchoolCollab.Settings.Api/Endpoints/ConfigResolveRoutes.cs";

    /// <summary>An anti-vacuity floor: the walk must see the files the assertions below describe.</summary>
    private const int MinimumRouteFileCount = 25;

    private const int MinimumDelegatingCallCount = 25;

    /// <summary>A <c>.Map&lt;Specialty&gt;Routes(</c> call site.</summary>
    private static readonly Regex SpecialtyCall = new(@"\.(Map\w+Routes)\s*\(", RegexOptions.Compiled);

    /// <summary>A <c>static &lt;return&gt; Map&lt;Specialty&gt;Routes(this &lt;Receiver&gt; …)</c> declaration.</summary>
    private static readonly Regex SpecialtyDeclaration = new(
        @"static\s+\S+\s+(Map\w+Routes)\s*\(\s*this\s+(\w+)",
        RegexOptions.Compiled);

    private static readonly string[] InlineMapCalls =
    [
        ".MapGet(", ".MapPost(", ".MapPut(", ".MapDelete(", ".MapPatch(",
    ];

    private static readonly string[] OrchestratorFiles = [.. EnumerateSourceFiles("Endpoints.cs")];

    [TestMethod]
    public void OrchestratorCalls_ResolveToRouteGroupBuilderSpecialtyMappers()
    {
        OrchestratorFiles.Should().NotBeEmpty(
            "the walk must find the <Domain>Endpoints.cs orchestrators that exist under src/");

        var declarations = SpecialtyMappers();

        var checkedCalls = 0;
        foreach (var orchestrator in OrchestratorFiles)
        {
            var source = File.ReadAllText(orchestrator);

            foreach (Match call in SpecialtyCall.Matches(source))
            {
                var mapper = call.Groups[1].Value;

                declarations.Should().ContainKey(mapper,
                    Relative(orchestrator) + $" calls {mapper}(...), so some specialty file under src/ must "
                    + "declare it — a dangling specialty mapper would leave the group's routes unmapped.");

                var (declaringFile, receiver) = declarations[mapper];
                if (ReceiverExemptions.TryGetValue(mapper, out var exemption))
                {
                    continue;
                }

                receiver.Should().Be("RouteGroupBuilder",
                    Relative(orchestrator) + $" calls {mapper}(...) declared in {declaringFile} with a "
                    + $"'{receiver}' receiver — a specialty mapper must take (this RouteGroupBuilder group) "
                    + "so it inherits the orchestrator's prefix and authorization policy.");

                checkedCalls++;
            }
        }

        checkedCalls.Should().BeGreaterThanOrEqualTo(MinimumDelegatingCallCount,
            "the orchestrators delegate every route to a specialty mapper — a low count means the scan "
            + "missed them and the assertion above passed vacuously");
    }

    [TestMethod]
    public void Orchestrators_MapNoRoutesInline()
    {
        OrchestratorFiles.Should().NotBeEmpty(
            "the walk must find the <Domain>Endpoints.cs orchestrators that exist under src/");

        var checkedFiles = 0;
        foreach (var orchestrator in OrchestratorFiles)
        {
            var relative = Relative(orchestrator);
            if (LeafGroupExemptions.ContainsKey(relative))
            {
                continue;
            }

            var source = File.ReadAllText(orchestrator);
            foreach (var inlineCall in InlineMapCalls)
            {
                Count(source, inlineCall).Should().Be(0,
                    $"{relative} must not call {inlineCall} — the orchestrator owns MapGroup, the "
                    + "authorization policy and the Map<Specialty>Routes() chain only; the route belongs in "
                    + "a specialty <Specialty>Routes.cs file");
            }

            checkedFiles++;
        }

        checkedFiles.Should().BeGreaterThanOrEqualTo(11,
            "eleven delegating orchestrators exist today (StudentEndpoints, AssignmentEndpoints and the nine "
            + "Settings.Api *Endpoints.cs files) — a lower count means the scan skipped them");
    }

    [TestMethod]
    public void SpecialtyRouteFiles_DeclareNoAuthorization()
    {
        var routeFiles = EnumerateSourceFiles("Routes.cs");

        // Anti-vacuity both ways: the route files must be found, and the one exemption must still be one.
        routeFiles.Should().HaveCountGreaterThanOrEqualTo(MinimumRouteFileCount,
            "the walk must find the specialty *Routes.cs files that exist under src/");
        File.Exists(Path.Combine(RepoRoot, RouteAuthorizationExemption)).Should().BeTrue(
            $"{RouteAuthorizationExemption} is the pinned exemption — if it moves, this guard must follow it");

        foreach (var routeFile in routeFiles)
        {
            var relative = Relative(routeFile);
            if (relative == RouteAuthorizationExemption)
            {
                continue;
            }

            Count(File.ReadAllText(routeFile), "RequireAuthorization(").Should().Be(0,
                $"{relative} must not carry a route-level authorization call — the policy belongs to the "
                + "orchestrator's group (RequireAuthIfOidc); a route-level gate silently diverges from it.");
        }
    }

    /// <summary>Every specialty mapper declared under <c>src/</c>: name → (declaring file, receiver type).</summary>
    private static Dictionary<string, (string File, string Receiver)> SpecialtyMappers()
    {
        var declarations = new Dictionary<string, (string File, string Receiver)>(StringComparer.Ordinal);
        foreach (var file in EnumerateSourceFiles(".cs"))
        {
            var source = File.ReadAllText(file);
            foreach (Match match in SpecialtyDeclaration.Matches(source))
            {
                declarations.TryAdd(match.Groups[1].Value, (Relative(file), match.Groups[2].Value));
            }
        }

        return declarations;
    }

    private static IEnumerable<string> EnumerateSourceFiles(string fileNameSuffix)
    {
        var srcRoot = Path.Combine(RepoRoot, "src");
        Directory.Exists(srcRoot).Should().BeTrue($"the repo root walk must resolve {srcRoot}");

        return Directory.EnumerateFiles(srcRoot, "*" + fileNameSuffix, SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal);
    }

    private static string Relative(string absolutePath)
        => Path.GetRelativePath(RepoRoot, absolutePath).Replace('\\', '/');

    private static int Count(string source, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(needle, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += needle.Length;
        }
        return count;
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
