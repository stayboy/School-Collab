using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the ar-24 bearer-forwarding + receiving-side opt-in wiring (the same failure
/// class the plan calls "attach a handler and forget the receiver"). Read-only source scans
/// against the real files under <c>src/</c>:
/// <list type="number">
///   <item><c>BEARERHANDLER</c> must exist in <c>src/SchoolCollab.Core/Auth/</c>;</item>
///   <item>the API-to-API hop clients (<c>Assignments.Api/Program.cs</c>, <c>Students.Api/Program.cs</c>)
///        must attach <c>AddHttpMessageHandler&lt;BearerForwardingDelegatingHandler&gt;</c>, and the
///        <c>Assignments.Worker</c> client must NOT (the carved-out residual is pinned);</item>
///   <item>every group the hops target must opt into the Bearer scheme — exactly five blocks in
///        <c>StudentEndpoints.cs</c> and one per Settings.Api <c>*Endpoints.cs</c> file (the Settings
///        flag-write and tenant-override routes carry no gate of their own — the <c>/api/config</c>
///        group opt-in covers them);</item>
///   <item>both <c>Assignments.Api</c> clients must carry <c>AllowAutoRedirect = false</c>
///        (row-7's discriminating source assertion — a scripted handler test cannot prove it).</item>
/// </list>
/// Follows the <c>AppHostSettingsDbWiringArchitectureTests</c> / <c>SeedCsvArchitectureTests</c>
/// walk-up precedent and throws when the repo root cannot be located.
/// </summary>
[TestClass]
public class BearerForwardingWiringArchitectureTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private const string HandlerFileName = "BearerForwardingDelegatingHandler.cs";
    private const string HandlerName = "BearerForwardingDelegatingHandler";
    private const string OptIn = "AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme)";

    private static readonly string[] SettingsEndpointFiles =
    [
        "AssignmentAiPromptEndpoints.cs",
        "AssignmentPolicyEndpoints.cs",
        "CodedValueEndpoints.cs",
        "ConfigEndpoints.cs",
        "EntityCodeRuleEndpoints.cs",
        "NotificationPolicyEndpoints.cs",
        "SignatureConsentTextEndpoints.cs",
        "TenantEndpoints.cs",
        "Endpoints/ConfigResolveRoutes.cs", // ar-24 item 8: tenant-resolve read also receives Bearer
    ];

    [TestMethod]
    public void Handler_Exists_InCoreAuth()
    {
        File.Exists(Path.Combine(RepoRoot, "src", "SchoolCollab.Core", "Auth", HandlerFileName))
            .Should().BeTrue($"the ar-24 {HandlerName} class must live in src/SchoolCollab.Core/Auth/.");
    }

    [TestMethod]
    public void AssignedAttachments_Present_WorkerAbsent()
    {
        var assignmentsApi = Read("src", "Assignments", "SchoolCollab.Assignments.Api", "Program.cs");
        var studentsApi = Read("src", "Students", "SchoolCollab.Students.Api", "Program.cs");
        var worker = Read("src", "Assignments", "SchoolCollab.Assignments.Worker", "Program.cs");

        // Count-based (diff-review P2-1): a presence check cannot fail when ONE attachment of
        // several is removed — which is exactly the plan's execution-order step 4(b) revert probe.
        Count(assignmentsApi, $"AddHttpMessageHandler<{HandlerName}>()").Should().Be(2,
            "both Assignments.Api hops (students-api, settings-api) must forward the bearer — removing either must fail this guard.");
        Count(studentsApi, $"AddHttpMessageHandler<{HandlerName}>()").Should().Be(3,
            "all three Students.Api cross-context hops must forward the bearer — removing any one must fail this guard.");
        worker.Should().NotContain($"AddHttpMessageHandler<{HandlerName}>()",
            "the Assignments.Worker has no caller token to forward (pinned carve-out residual).");
    }

    [TestMethod]
    public void BearerOptIn_PerGroup_StudentsAndSettings_NoRouteLevelFlagGate()
    {
        var studentsEndpoints = Read("src", "Students", "SchoolCollab.Students.Api", "StudentEndpoints.cs");

        OptInCount(studentsEndpoints).Should().Be(5,
            "StudentEndpoints.cs has five conditional groups (students, activityGroups, guardians, contacts, teachers) each opting into Bearer.");

        foreach (var settingsFile in SettingsEndpointFiles)
        {
            OptInCount(Read("src", "Settings", "SchoolCollab.Settings.Api", settingsFile))
                .Should().Be(1, $"{settingsFile} must opt its {settingsFile.Replace(".cs", "")} group into the Bearer scheme.");
        }

        // The Settings flag-write and tenant-override routes carry NO route-level gate: the
        // unsatisfiable "flag_admin" role policy is gone, and the /api/config group opt-in above
        // is what makes those routes authenticated + bearer-eligible. These absence counts are the
        // non-vacuity guard — re-adding a route-level gate must fail this test.
        var flagRoutes = Read("src", "Settings", "SchoolCollab.Settings.Api", "Endpoints", "ConfigFlagRoutes.cs");
        var overrideRoutes = Read("src", "Settings", "SchoolCollab.Settings.Api", "Endpoints", "ConfigTenantFlagOverrideRoutes.cs");

        Read("src", "Settings", "SchoolCollab.Settings.Api", "Program.cs")
            .Should().NotContain("flag_admin",
                "the Settings host must register no flag_admin policy — the role policy was removed (owner ruling 2026-09-24).");

        Count(flagRoutes, "ApplyAdminPolicy").Should().Be(0,
            "the flag write routes must not re-acquire a route-level admin policy; the /api/config group opt-in carries them.");
        Count(overrideRoutes, "ApplyAdminPolicy").Should().Be(0,
            "the tenant-override write routes must not re-acquire a route-level admin policy; the /api/config group opt-in carries them.");
        Count(flagRoutes, "RequireAuthorization").Should().Be(0,
            "a route-level authorization call on a flag route would bypass the /api/config group opt-in and its Bearer scheme choice.");
        Count(overrideRoutes, "RequireAuthorization").Should().Be(0,
            "a route-level authorization call on a tenant-override route would bypass the /api/config group opt-in and its Bearer scheme choice.");

        // ConfigEndpoints.cs's single bearer opt-in — the /api/config group policy that now carries the
        // flag-write and tenant-override routes — is already asserted by the SettingsEndpointFiles loop
        // above (ConfigEndpoints.cs is in that array), so it is deliberately not asserted a second time.
    }

    [TestMethod]
    public void AssignmentsApiClients_DisableAutoRedirect()
    {
        var assignmentsApi = Read("src", "Assignments", "SchoolCollab.Assignments.Api", "Program.cs");

        assignmentsApi.Should().Contain("ConfigurePrimaryHttpMessageHandler",
            "row 7: both Assignments.Api clients must disable auto-redirect so a real-auth 302-challenge surfaces as non-2xx.");
        Count(assignmentsApi, "AllowAutoRedirect = false").Should().Be(2,
            "both Assignments.Api named clients (students-api, settings-api) must set AllowAutoRedirect = false.");
    }

    private static int OptInCount(string source)
        => Count(source, OptIn);

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
