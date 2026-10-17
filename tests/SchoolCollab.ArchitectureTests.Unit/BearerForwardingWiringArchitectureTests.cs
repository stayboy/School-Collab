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
///   <item>the Bearer opt-in lives in exactly ONE place — <c>EndpointGroupAuthExtensions.RequireAuthIfOidc</c>
///        (<c>src/SchoolCollab.Core/Auth/</c>) — and every group the hops target reaches it with a
///        counted call: five in <c>StudentEndpoints.cs</c>, five in <c>AssignmentEndpoints.cs</c> and one
///        per Settings.Api <c>*Endpoints.cs</c> file (the Settings flag-write and tenant-override routes
///        carry no gate of their own — the <c>/api/config</c> group policy covers them);</item>
///   <item>no hand-rolled <c>if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))</c> guard
///        survives in the migrated endpoint groups — a re-introduced block fails immediately;</item>
///   <item><c>Endpoints/ConfigResolveRoutes.cs</c> is the pinned exemption: it takes a <c>bool</c>, not the
///        flag service, so it keeps its inline gate and its own Bearer opt-in;</item>
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
    private const string Gate = "RequireAuthIfOidc(";
    private const string HandRolledGuard = "if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))";

    /// <summary>The nine Settings.Api orchestrators — one Bearer-gated group each.</summary>
    private static readonly string[] SettingsEndpointFiles =
    [
        "AssignmentAiPromptEndpoints.cs",
        "AssignmentPolicyEndpoints.cs",
        "CodedValueEndpoints.cs",
        "ConfigEndpoints.cs",
        "EntityCodeRuleEndpoints.cs",
        "NotificationPolicyEndpoints.cs",
        "NotificationTemplateEndpoints.cs",
        "SignatureConsentTextEndpoints.cs",
        "TenantEndpoints.cs",
    ];

    /// <summary>The 12 files migrated onto <c>RequireAuthIfOidc</c> (spec minimal-api-composition §WP2).
    /// The 13th guard site, <c>Endpoints/ConfigResolveRoutes.cs</c>, is the documented exemption — it takes a
    /// <c>bool</c>, not the flag service, and is pinned by <c>ConfigResolveRoutes_KeepsItsInlineOidcGate</c>.</summary>
    private static readonly string[] MigratedEndpointFiles =
    [
        "src/Students/SchoolCollab.Students.Api/StudentEndpoints.cs",
        "src/Assignments/SchoolCollab.Assignments.Api/AssignmentEndpoints.cs",
        .. SettingsEndpointFiles.Select(file => $"src/Settings/SchoolCollab.Settings.Api/{file}"),
        "src/SchoolCollab.Auth/Endpoints/AuthEndpointGroup.cs",
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
    public void Helper_IsTheSingleBearerOptInWritePoint()
    {
        // The opt-in moved into the helper, so the per-file assertions below could only ever say
        // "zero" — which asserts nothing. Pinning the helper instead keeps the guard non-vacuous:
        // deleting the opt-in from EndpointGroupAuthExtensions.cs fails here.
        Count(Read("src", "SchoolCollab.Core", "Auth", "EndpointGroupAuthExtensions.cs"), OptIn).Should().Be(1,
            "the helper is the single write-point for the Bearer opt-in; deleting it must fail this guard.");
    }

    [TestMethod]
    public void RequireAuthIfOidc_PerGroup_StudentsAssignmentsAndSettings()
    {
        Count(Read("src", "Students", "SchoolCollab.Students.Api", "StudentEndpoints.cs"), Gate).Should().Be(5,
            "StudentEndpoints.cs has five conditional groups (students, activityGroups, guardians, contacts, teachers) each gated through RequireAuthIfOidc.");

        Count(Read("src", "Assignments", "SchoolCollab.Assignments.Api", "AssignmentEndpoints.cs"), Gate).Should().Be(5,
            "AssignmentEndpoints.cs gates five groups (assignments, reader, writer, ward, activityGroups) through RequireAuthIfOidc — "
            + "the reader and writer sub-groups pass their gateway-scheme policy through the callback overload.");

        foreach (var settingsFile in SettingsEndpointFiles)
        {
            Count(Read("src", "Settings", "SchoolCollab.Settings.Api", settingsFile), Gate).Should().Be(1,
                $"{settingsFile} must gate its {settingsFile.Replace(".cs", "")} group through RequireAuthIfOidc.");
        }

        // The Auth host's admin group keeps its "missing service means gate on" posture: the helper's
        // policy overload treats a null IFeatureFlagService as gate-on, so no hand-rolled null check remains.
        Count(Read("src", "SchoolCollab.Auth", "Endpoints", "AuthEndpointGroup.cs"), Gate).Should().Be(1,
            "AuthEndpointGroup.cs must gate /auth/admin through RequireAuthIfOidc with its portal-facing admin policy.");
    }

    [TestMethod]
    public void NoHandRolledOidcFlagGuard_InMigratedEndpoints()
    {
        // Non-vacuity floor: an empty or wrongly-rooted enumeration must fail rather than pass silently.
        // 12 files carry the 20 migrated gate calls; the 13th guard site (Endpoints/ConfigResolveRoutes.cs)
        // is the documented exemption and is pinned by its own test below.
        MigratedEndpointFiles.Should().HaveCount(12, "the spec migrates 20 guard blocks across 12 files, with ConfigResolveRoutes exempt.");

        foreach (var relativePath in MigratedEndpointFiles)
        {
            Count(Read(relativePath), HandRolledGuard).Should().Be(0,
                $"{relativePath} must gate through RequireAuthIfOidc — a hand-rolled OIDC flag guard is the duplication this guard exists to stop.");

            // A redundant second opt-in in a migrated file (a new sub-group pinning Bearer by hand, with no
            // flag guard) would otherwise pass every other guard here — the pre-refactor per-file count caught it.
            Count(Read(relativePath), OptIn).Should().Be(0,
                $"{relativePath} must not carry its own Bearer opt-in — the helper owns the only one, and a hand-rolled addition would diverge from it silently.");
        }
    }

    [TestMethod]
    public void ConfigResolveRoutes_KeepsItsInlineOidcGate()
    {
        // Exemption (spec §WP2): ConfigResolveRoutes takes a bool, not the flag service, so it cannot use
        // the helper — it keeps its own inline `if (oidcEnabled)` + Bearer opt-in and must not silently move.
        var configResolveRoutes = Read("src", "Settings", "SchoolCollab.Settings.Api", "Endpoints", "ConfigResolveRoutes.cs");

        Count(configResolveRoutes, OptIn).Should().Be(1,
            "the tenant-scoped /api/features/{tenantId} read must keep its own Bearer opt-in — it is the documented exemption.");
        Count(configResolveRoutes, Gate).Should().Be(0,
            "ConfigResolveRoutes takes a bool, not IFeatureFlagService — adopting the helper there would need a signature change.");
    }

    [TestMethod]
    public void ConfigRoutes_NoRouteLevelAdminPolicy()
    {
        // The Settings flag-write and tenant-override routes carry NO route-level gate: the
        // unsatisfiable "flag_admin" role policy is gone, and the /api/config group policy is what
        // makes those routes authenticated + bearer-eligible. These absence counts are the
        // non-vacuity guard — re-adding a route-level gate must fail this test.
        var flagRoutes = Read("src", "Settings", "SchoolCollab.Settings.Api", "Endpoints", "ConfigFlagRoutes.cs");
        var overrideRoutes = Read("src", "Settings", "SchoolCollab.Settings.Api", "Endpoints", "ConfigTenantFlagOverrideRoutes.cs");

        Read("src", "Settings", "SchoolCollab.Settings.Api", "Program.cs")
            .Should().NotContain("flag_admin",
                "the Settings host must register no flag_admin policy — the role policy was removed (owner ruling 2026-09-24).");

        Count(flagRoutes, "ApplyAdminPolicy").Should().Be(0,
            "the flag write routes must not re-acquire a route-level admin policy; the /api/config group policy carries them.");
        Count(overrideRoutes, "ApplyAdminPolicy").Should().Be(0,
            "the tenant-override write routes must not re-acquire a route-level admin policy; the /api/config group policy carries them.");
        Count(flagRoutes, "RequireAuthorization").Should().Be(0,
            "a route-level authorization call on a flag route would bypass the /api/config group policy and its Bearer scheme choice.");
        Count(overrideRoutes, "RequireAuthorization").Should().Be(0,
            "a route-level authorization call on a tenant-override route would bypass the /api/config group policy and its Bearer scheme choice.");
    }

    [TestMethod]
    public void AssignmentsApiClients_DisableAutoRedirect()
    {
        var assignmentsApi = Read("src", "Assignments", "SchoolCollab.Assignments.Api", "Program.cs");

        assignmentsApi.Should().Contain("ConfigurePrimaryHttpMessageHandler",
            "row 7: every Assignments.Api cross-module client must disable auto-redirect so a real-auth 302-challenge surfaces as non-2xx.");
        Count(assignmentsApi, "AllowAutoRedirect = false").Should().Be(3,
            "every Assignments.Api named client (students-api, settings-api, and the portal-session "
            + "claims reader's auth client — round portal-session-adoption D4, plan amendment "
            + "2026-10-05) must set AllowAutoRedirect = false.");
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

    /// <summary>Reads a repo-root-relative path (forward-slash form, as listed in the migrated-file array).</summary>
    private static string Read(string repoRelativePath)
        => Read(repoRelativePath.Split('/'));

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
