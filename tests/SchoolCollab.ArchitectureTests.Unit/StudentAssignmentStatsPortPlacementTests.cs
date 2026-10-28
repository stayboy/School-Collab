using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Issue #444 (layer L2b) — where the student assignment-stats port lives, and what its placement buys.
/// The students view needs six counts the Assignments context owns; owner Q152 mirrored them into
/// <c>Students.Core</c> rather than referencing an Assignments assembly, so the hop stays HTTP and
/// <c>Students.Application</c> never grows a cross-context project reference.
/// <list type="number">
///   <item>the port interface is in <c>Students.Core/Services</c> and the HTTP implementation in
///       <c>Students.Api/Services</c> — the <c>IActivityGroupAssignmentQuery</c> precedent;</item>
///   <item><b>neither</b> <c>Students.Core</c> <b>nor</b> <c>Students.Application</c> carries an Assignments
///       assembly reference. This is the guarantee the mirror rests on: the moment either project references
///       an Assignments assembly, the mirror is redundant and the bounded-context boundary is gone.</item>
/// </list>
/// <para>
/// Scope, stated because it is easy to mistake for the neighbouring guard:
/// <c>TeacherScopeWiringArchitectureTests.CrossContextProjectReferences_DoNotGrow</c> counts
/// Assignments→Students edges — a different direction. This test is the only pin on
/// Students→Assignments, so it must not be deleted as "already covered".
/// </para>
/// <para>
/// Follows the <c>PortalSessionClaimsPortWiringArchitectureTests</c> idiom: read-only source scans, a
/// <c>FindRepoRoot()</c> walk-up that THROWS rather than passing vacuously, and count-based assertions with
/// named reasons.
/// </para>
/// </summary>
[TestClass]
public class StudentAssignmentStatsPortPlacementTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private const string PortInterfaceFile =
        "src/Students/SchoolCollab.Students.Core/Services/IStudentAssignmentStatsReader.cs";

    private const string PortImplementationFile =
        "src/Students/SchoolCollab.Students.Api/Services/StudentAssignmentStatsReaderHttpClient.cs";

    /// <summary>The two projects that must stay free of the Assignments context.</summary>
    private static readonly string[] AssignmentsFreeProjects =
    [
        "src/Students/SchoolCollab.Students.Core/SchoolCollab.Students.Core.csproj",
        "src/Students/SchoolCollab.Students.Application/SchoolCollab.Students.Application.csproj",
    ];

    [TestMethod]
    public void PortInterface_LivesInStudentsCore_AndTheImplementationInStudentsApi()
    {
        var port = Read(PortInterfaceFile);
        port.Should().Contain("interface IStudentAssignmentStatsReader",
            $"the cross-context port must be declared in {PortInterfaceFile}, where the consuming handlers can "
            + "see it without an API-side dependency");

        var implementation = Read(PortImplementationFile);
        implementation.Should().Contain(": IStudentAssignmentStatsReader",
            $"the HTTP implementation must live in {PortImplementationFile} — the port itself stays free of "
            + "HttpClient and of service discovery");
        implementation.Should().Contain("CreateClient(\"assignments-api\")",
            "the client rides the EXISTING named client (owner Q151), whose handler chain already supplies the "
            + "tenant and the forwarded bearer token; a second registration would silently drop both");
    }

    [TestMethod]
    public void StudentsCore_AndApplication_CarryNoAssignmentsAssemblyReference()
    {
        var references = AssignmentsFreeProjects
            .Select(project => new
            {
                Project = project,
                Exists = File.Exists(Path.Combine(RepoRoot, project)),
                Source = File.Exists(Path.Combine(RepoRoot, project)) ? Read(project) : string.Empty,
            })
            .ToArray();

        // Anti-vacuity: both project files must be found, and each must actually carry references — a scan
        // that read an empty or missing file would satisfy the negative assertion below while proving nothing.
        references.Should().HaveCount(AssignmentsFreeProjects.Length);
        foreach (var project in references)
        {
            project.Exists.Should().BeTrue($"'{project.Project}' must exist — this guard reads its references");
            project.Source.Should().Contain("<ProjectReference",
                $"'{project.Project}' must declare at least one project reference; otherwise the scan below "
                + "reads nothing and the rule is ceremony");
        }

        foreach (var project in references)
        {
            project.Source.Should().NotContain("Assignments",
                $"'{project.Project}' must not reference an Assignments assembly: the six counts are MIRRORED "
                + "into Students.Core (owner Q152), and the hop is HTTP. A project reference here would delete "
                + "the reason the mirror exists — and with it the bounded-context boundary AGENTS.md § "
                + "'Cross-context access' requires");
        }
    }

    private static string Read(string relativePath)
    {
        var path = Path.Combine(RepoRoot, relativePath);
        return File.Exists(path)
            ? File.ReadAllText(path)
            : throw new FileNotFoundException($"Expected source file not found under the repo: {relativePath}");
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
