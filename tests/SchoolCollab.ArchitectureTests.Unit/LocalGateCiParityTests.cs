using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the local gate against drifting away from CI (optimisation round, grill Q103).
/// <para>
/// The failure this prevents is <b>false confidence</b>: a local script that loops over a
/// hand-picked subset of test projects and then prints "green". The repo's solution registers 13
/// <c>*.Tests.Unit</c> projects, and CI additionally gates an Integration leg, a project-scoped
/// <c>CrossModuleWiringTests</c> guard, and two Python portal suites. So <c>gates.sh</c> mirrors
/// CI's <b>filter legs</b>; this test fails if a leg is added to CI and not here, if the MAUI
/// serial flags disappear, if the "NOT checked here" honesty list is removed, or if
/// <c>preflight-pr.sh</c> grows a second, weaker definition of green.
/// </para>
/// </summary>
[TestClass]
public class LocalGateCiParityTests
{
    private static readonly Regex FilterPattern =
        new(@"--filter\s+""(?<f>[^""]+)""", RegexOptions.Compiled);

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "AppHost", "SchoolCollab.AppHost")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName;
    }

    private static (string Root, string Ci, string Gates, string Preflight) Read()
    {
        var root = FindRepoRoot();
        Assert.IsNotNull(root, "Could not locate the repository root from " + AppContext.BaseDirectory);

        var ci = Path.Combine(root, ".github", "workflows", "ci.yml");
        var gates = Path.Combine(root, "scripts", "round", "gates.sh");
        var preflight = Path.Combine(root, "scripts", "preflight-pr.sh");

        File.Exists(ci).Should().BeTrue($"{ci} must exist");
        File.Exists(gates).Should().BeTrue($"{gates} must exist");
        File.Exists(preflight).Should().BeTrue($"{preflight} must exist");

        return (root, File.ReadAllText(ci), File.ReadAllText(gates), File.ReadAllText(preflight));
    }

    [TestMethod]
    public void GatesScript_MirrorsEveryCiTestFilter()
    {
        var (_, ci, gates, _) = Read();

        var ciFilters = FilterPattern.Matches(ci).Select(m => m.Groups["f"].Value).Distinct().ToArray();
        var gateFilters = FilterPattern.Matches(gates).Select(m => m.Groups["f"].Value).Distinct().ToArray();

        // Non-vacuity twice: a regex that matched nothing would make the comparison meaningless.
        ciFilters.Should().NotBeEmpty("ci.yml must declare its test legs as --filter arguments");
        gateFilters.Should().NotBeEmpty("scripts/round/gates.sh must mirror those legs");

        var missing = ciFilters.Except(gateFilters).ToArray();
        missing.Should().BeEmpty(
            "every test leg CI runs must also run locally — a per-project subset drops the repo-wide " +
            "architecture scanner, the Integration projects and the CrossModuleWiring guard");

        var extra = gateFilters.Except(ciFilters).ToArray();
        extra.Should().BeEmpty(
            "gates.sh must not invent legs CI does not run (its green would then mean something different)");
    }

    [TestMethod]
    public void GatesScript_RunsBothPythonPortalSuites()
    {
        var (_, ci, gates, _) = Read();

        // The two portal suites contribute no .NET project, so `dotnet test` never sees them.
        foreach (var portal in new[] { "SchoolCollab.Portals", "SchoolCollab.AuthPortal" })
        {
            ci.Should().Contain(portal, $"ci.yml gates {portal}");
            gates.Should().Contain(portal,
                $"{portal} is invisible to `dotnet test`; a local gate that skips it reports " +
                "\"all green\" while two real CI jobs are unexamined");
        }
    }

    [TestMethod]
    public void GatesScript_CarriesTheMauiSerialFlags()
    {
        var (_, _, gates, _) = Read();

        // AGENTS.md: the default parallel build exhausts memory on the dev machine and kills the
        // agent process (~0xC0000409) instead of reporting a compile error. These flags are not optional.
        gates.Should().Contain("-p:BuildInParallel=false",
            "the MAUI head build must be serial or it can OOM-kill the gate it exists to speed up");
        gates.Should().Contain("-m:1", "the MAUI head build must cap MSBuild at one node");
        gates.Should().Contain("src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj",
            "the MAUI head is NOT in SchoolCollab.slnx — a shared-component break is invisible without it");
    }

    [TestMethod]
    public void GatesScript_StatesWhatItDidNotCheck()
    {
        var (_, _, gates, _) = Read();

        gates.Should().Contain("NOT checked here",
            "the gate must name what it skipped (integration, portals, or the MAUI head) so a green " +
            "local run is never mistaken for green CI — that is the whole reason this script exists");
    }

    [TestMethod]
    public void PreflightScript_ReusesTheGateInsteadOfDefiningASecondOne()
    {
        var (_, _, _, preflight) = Read();

        preflight.Should().Contain("scripts/round/gates.sh",
            "preflight-pr.sh must call the shared gate; a second, weaker build/test leg in the same " +
            "repo is a second definition of \"green\" and they will diverge");

        // The old inline leg must be gone: one `dotnet build` per definition is the smell.
        var inlineBuilds = Regex.Matches(preflight, @"^\s*dotnet build\b", RegexOptions.Multiline).Count;
        inlineBuilds.Should().Be(0,
            "preflight-pr.sh must not build inline any more — gates.sh owns the build/test legs");
    }
}
