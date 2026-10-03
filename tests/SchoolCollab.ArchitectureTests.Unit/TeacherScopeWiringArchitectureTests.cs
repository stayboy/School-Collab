using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Constants;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Round <c>teacher-scope-auth</c> — the source-shape guards for D2's port and its wiring, which no
/// behavioural test can pin:
///
/// <list type="number">
/// <item><b>[P1-6] one registration, the documented form.</b> <c>Assignments.Api/Program.cs</c> must
/// carry the migrated <c>AddCrossModuleHttpClient("students-api", …)</c> form and NO bare
/// <c>AddHttpClient("students-api")</c>: that one named client also feeds
/// <c>IContactResolver</c>, <c>ITeacherDirectory</c>, <c>IStudentDirectory</c>,
/// <c>IActivityGroupLookup</c>, <c>IAssignmentTargetResolver</c>, <c>ITopicAssignmentLookup</c> and
/// <c>ITeacherScopeProvider</c>, so a second registration of the same name would silently drop the
/// ar-24 handler chain. The <c>Assignments.Worker</c>'s own client is a separate host and is pinned
/// as untouched.</item>
/// <item><b>[Q7] the port's file placement.</b> The interface lives in Assignments.Core, the HTTP
/// implementation + the local DTO mirror in Assignments.Api — and none of the three names a
/// <c>Students.Core</c>/<c>Students.Application</c> type.</item>
/// <item><b>[Q7] no new cross-context project reference.</b> The recorded
/// <c>Assignments.Core → Students.Core</c> violation
/// (<c>cross-context-rule-followups.md</c> §1) is counted, not repaired: the count must not grow.</item>
/// </list>
///
/// Follows the <c>BearerForwardingWiringArchitectureTests</c> walk-up precedent.
/// </summary>
[TestClass]
public class TeacherScopeWiringArchitectureTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private const string CrossModuleRegistration =
        "AddCrossModuleHttpClient(\"students-api\", \"https+http://students-api\", propagateTenant: false)";

    /// <summary>
    /// D1 ([P2-4]) — the realm file declares every role <see cref="RealmRoleNames"/> names, and
    /// the C# constants carry no value the realm does not define. The realm file stays literal
    /// (it is Keycloak configuration); this guard is what keeps the single C# spelling honest.
    ///
    /// <para><b>Weakly discriminating</b> (the plan says so): it fails only if a role is ABSENT —
    /// it cannot prove Keycloak assigns it, which stays the D11/admin-UI step.</para>
    /// </summary>
    [TestMethod]
    public void RealmFile_DeclaresEveryRoleNameTheCodeGatesOn()
    {
        var realm = JsonDocument.Parse(File.ReadAllText(RealmFile()));
        var declared = realm.RootElement.GetProperty("roles").GetProperty("realm")
            .EnumerateArray()
            .Select(r => r.GetProperty("name").GetString()!)
            .ToList();

        var constants = typeof(RealmRoleNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false })
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        constants.Should().NotBeEmpty("the shared role-name constants are what the policies read");
        constants.Should().OnlyHaveUniqueItems();
        declared.Should().Contain(constants,
            "every RealmRoleNames constant must exist as a realm role — otherwise role assignment in "
            + "Keycloak cannot satisfy the policies that gate on it");
        declared.Should().Contain(new[] { "user-admin", "platform-admin", "teacher", "staff" },
            "the four roles this solution recognises (D1) are declared in the realm import");
    }

    [TestMethod]
    public void AssignmentsApi_StudentsApiClient_IsTheSingleCrossModuleRegistration()
    {
        var program = Read("src", "Assignments", "SchoolCollab.Assignments.Api", "Program.cs");

        program.Should().Contain(CrossModuleRegistration,
            "the students-api client must use the documented cross-module registration (30-minute "
            + "handler lifetime + CrossModuleRetryDelegatingHandler)");

        Count(program, "AddHttpClient(\"students-api\"")
            .Should().Be(0,
                "[P1-6] the bare registration must be MIGRATED, not duplicated: a second registration "
                + "of the same name silently re-registers the named client and drops the bearer/"
                + "strict-2xx chain every cross-context consumer shares");

        Count(program, "(\"students-api\"").Should().Be(1,
            "exactly one registration of the students-api named client exists in this host");

        // The ar-24 posture that the pattern doc omits is kept on the migrated client.
        program.Should().Contain("AddHttpMessageHandler<BearerForwardingDelegatingHandler>()");
        program.Should().Contain("AllowAutoRedirect = false");
    }

    [TestMethod]
    public void AssignmentsWorker_StudentsApiClient_IsUntouched()
    {
        var worker = Read("src", "Assignments", "SchoolCollab.Assignments.Worker", "Program.cs");

        worker.Should().Contain("AddHttpClient(\"students-api\")",
            "the Worker's own named client is a separate host with no caller token to forward — the "
            + "round deliberately leaves it alone");
    }

    [TestMethod]
    public void TeacherScopePort_InterfaceInCore_ClientAndMirrorInApi_NoStudentsTypeInAnySignature()
    {
        var interfaceFile = Read("src", "Assignments", "SchoolCollab.Assignments.Core", "Services", "ITeacherScopeProvider.cs");
        Read("src", "Assignments", "SchoolCollab.Assignments.Core", "Services", "TeacherScope.cs");
        var clientFile = Read("src", "Assignments", "SchoolCollab.Assignments.Api", "Services", "TeacherScopeHttpClient.cs");
        var mirrorFile = Read("src", "Assignments", "SchoolCollab.Assignments.Api", "Services", "TeacherGradeAssignmentResponse.cs");

        interfaceFile.Should().Contain("interface ITeacherScopeProvider");
        clientFile.Should().Contain("ITeacherScopeProvider");
        interfaceFile.Should().Contain("GetScopeAsync",
            "the port's shape is what the endpoint resolves through");

        foreach (var (name, text) in new[]
                 {
                     ("ITeacherScopeProvider.cs", interfaceFile),
                     ("TeacherScope.cs", Read("src", "Assignments", "SchoolCollab.Assignments.Core", "Services", "TeacherScope.cs")),
                     ("TeacherScopeHttpClient.cs", clientFile),
                     ("TeacherGradeAssignmentResponse.cs", mirrorFile),
                 })
        {
            // The comment above each file NAMES the forbidden namespaces on purpose (that is how a
            // reader learns why the mirror exists); what must not appear is a real reference to a
            // Students type — an import or a fully-qualified use.
            text.Should().NotContain("using SchoolCollab.Students.",
                $"{name} must not import a Students.Core/Students.Application type — the port is an "
                + "HTTP hop and the DTO is mirrored locally");
            text.Should().NotContain("SchoolCollab.Students.",
                $"{name} must not name a fully-qualified Students type either");
        }
    }

    [TestMethod]
    public void CrossContextProjectReferences_DoNotGrow()
    {
        // The pre-existing Assignments.Core → Students.Core edge is the OPEN violation recorded in
        // cross-context-rule-followups.md §1 (a separate docs/cleanup task). This round must not
        // deepen it: the counts below are the recorded ones, and a new reference fails here.
        CountStudentsProjectReferences("SchoolCollab.Assignments.Core").Should().Be(1,
            "the recorded Core → Students.Core violation is not repaired here, but it must not grow");
        CountStudentsProjectReferences("SchoolCollab.Assignments.Api").Should().Be(1,
            "Assignments.Api already referenced Students.Core before this round; no new edge may be added");
        CountStudentsProjectReferences("SchoolCollab.Assignments.Contracts").Should().Be(0,
            "the contracts project carries no cross-context reference at all");
    }

    private static int CountStudentsProjectReferences(string projectName)
    {
        var csproj = Read("src", "Assignments", projectName, $"{projectName}.csproj");
        return csproj
            .Split('\n')
            .Count(line => line.Contains("ProjectReference", StringComparison.Ordinal)
                        && line.Contains("Students", StringComparison.Ordinal));
    }

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

    private static string Read(params string[] relative)
    {
        var path = Path.Combine([RepoRoot, .. relative]);
        return File.Exists(path)
            ? File.ReadAllText(path)
            : throw new FileNotFoundException($"Expected source file not found under the repo: {string.Join('/', relative)}");
    }

    /// <summary>The realm import file, discovered by the <c>&lt;realm&gt;-realm.json</c> naming
    /// rule that <c>AppHostRealmImportArchitectureTests</c> already guards.</summary>
    private static string RealmFile()
    {
        var files = Directory.EnumerateFiles(
                Path.Combine(RepoRoot, "src", "AppHost", "SchoolCollab.AppHost"), "*-realm.json")
            .ToList();
        files.Should().ContainSingle("the AppHost carries exactly one realm import file");
        return files[0];
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
