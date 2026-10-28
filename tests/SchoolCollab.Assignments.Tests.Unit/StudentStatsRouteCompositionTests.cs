using FluentAssertions;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Issue #444 (layer L2a) — how the stats route is composed: chained from the <c>/assignments</c>
/// orchestrator group, so it inherits that group's prefix and its <c>RequireAuthIfOidc</c> policy, and
/// declared over the <c>GetStudentAssignmentStats</c> query with a typed result.
/// <para>
/// Asserted against the two source files rather than a started host: this test project references
/// <c>Assignments.Core</c>, <c>Contracts</c>, <c>Application</c> and <c>Worker</c> but NOT
/// <c>Assignments.Api</c>, and has no <c>TestServer</c> package, so the orchestrator cannot be mapped
/// in-process here — the host-level guard for this route is
/// <c>ArchitectureTests.Unit.MinimalApiCompositionArchitectureTests</c>, and the wire check lands in L2b
/// where the client exists. The source is the contract either way: a route that drifts out of the group,
/// onto an absolute path, or back onto the <c>Results</c> factory fails here.
/// </para>
/// </summary>
[TestClass]
public class StudentStatsRouteCompositionTests
{
    private const string RouteFile = "src/Assignments/SchoolCollab.Assignments.Api/Endpoints/StudentStatsRoutes.cs";
    private const string OrchestratorFile = "src/Assignments/SchoolCollab.Assignments.Api/AssignmentEndpoints.cs";
    private const string RelativeRoute = "\"/{studentId:guid}/assignment-stats\"";

    private static readonly string RepoRoot = FindRepoRoot();

    private static string Read(string relativePath)
    {
        var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue($"{relativePath} is the route's single declaration site");
        return File.ReadAllText(path);
    }

    /// <summary>The file without its comment lines: the prose may name a symbol freely, the code is what is
    /// asserted on.</summary>
    private static string Code(string source) => string.Join('\n', source
        .Split('\n')
        .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [TestMethod]
    public void StatsRoute_IsMappedRelative_WithATypedResult_AndNoAuthorizationOfItsOwn()
    {
        var source = Code(Read(RouteFile));

        source.Should().Contain("MapStudentStatsRoutes(this RouteGroupBuilder group)",
            "a specialty mapper takes the orchestrator's group, so it cannot escape the group's prefix or policy");
        source.Should().Contain("group.MapGet(" + RelativeRoute,
            "the path is RELATIVE — an absolute '/assignments/…' here would bypass the group and could drift "
            + "from its authorization policy");
        source.Should().Contain("IQueryHandler<GetStudentAssignmentStats, StudentAssignmentStatsDto>",
            "the route serves the stats query, not a bespoke read");
        source.Should().Contain("TypedResults",
            "typed results give the generated document a real response schema");
        source.Should().NotContain("return Results.",
            "the Results factory loses the response schema — TypedResults is the route-level rule: the typed "
            + "return makes the schema inferable without a hand-written Produces<T>()");
        source.Should().NotContain("RequireAuthorization",
            "the policy belongs to the orchestrator group; a route-level gate silently diverges from it");
        source.Should().NotContain("RequireAuthIfOidc",
            "same rule, the group's helper form");
        source.Should().NotContain("FeatureFlagKeys",
            "no hand-rolled feature-flag branch — the group's RequireAuthIfOidc already owns that decision");
    }

    [TestMethod]
    public void StatsRoute_IsChainedFromTheAssignmentsGroup_NotFromAppOrTheWardGroup()
    {
        var source = Code(Read(OrchestratorFile));

        var groupDeclaration = source.IndexOf("app.MapGroup(\"/assignments\")", StringComparison.Ordinal);
        var chained = source.IndexOf("group.MapStudentStatsRoutes();", StringComparison.Ordinal);
        var wardGroup = source.IndexOf("app.MapGroup(\"/students\")", StringComparison.Ordinal);

        groupDeclaration.Should().BeGreaterThan(-1, "the orchestrator must still declare the /assignments group");
        chained.Should().BeGreaterThan(-1,
            "AssignmentEndpoints must chain the stats route — a mapper nobody calls leaves the route unmapped");
        wardGroup.Should().BeGreaterThan(-1,
            "the /students group is the neighbouring ward surface and must still exist for this ordering pin");
        chained.Should().BeGreaterThan(groupDeclaration,
            "the stats route is chained AFTER the /assignments group exists");
        chained.Should().BeLessThan(wardGroup,
            "…and from `group.` (the /assignments group) — chaining it after the /students group means it was "
            + "mounted on the ward group instead, which is a different prefix and a different audience");

        source.Should().Contain("group.MapStudentStatsRoutes();",
            "the call site is spelled on the /assignments group variable, so the route inherits its prefix "
            + "and RequireAuthIfOidc policy");
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
