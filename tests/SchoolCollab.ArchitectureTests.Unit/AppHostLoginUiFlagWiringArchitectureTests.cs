using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the round-B login-UI flag wiring in the AppHost
/// (<c>keycloak-ui-auth</c>, spec D4/D5/D16, plan-review P2-5). Four invariants that no
/// build or unit test can see, because they live in the AppHost's parameter fan-out:
/// <list type="number">
///   <item>the <c>FEATURE:DisableKeycloakLoginUi</c> startup flag is fanned out to
///        <b>exactly</b> its four consumers — admin, families, auth and auth-portal;</item>
///   <item><c>Auth:Portal:LoginUrl</c> reaches the two browser-facing Blazor hosts and
///        <b>not</b> the auth service — the flag-ON challenge handler fails closed with
///        <c>401</c> exactly because the auth service never receives the login URL;</item>
///   <item><c>Auth:Portal:BootstrapRedirectUrl</c> reaches the auth service only (it is the
///        server-side D16 redirect target of the passkey ceremony, which the auth service
///        alone performs);</item>
///   <item>the auth portal is registered as a <b>Uvicorn</b> app (<c>AddUvicornApp</c> +
///        <c>WithUv</c>, never the typed-<c>ProjectResource</c> <c>WireKeycloakAuth</c> helper),
///        and the two Blazor hosts carry the <c>.WithReference(auth)</c> that
///        <c>CrossModuleWiringTests</c> demands for the literal <c>https+http://auth</c>
///        handshake base address.</item>
/// </list>
/// <para>
/// Resource blocks are parsed from the real <c>Program.cs</c> by line: a block opens at its
/// <c>AddProject&lt;…&gt;("name")</c> / <c>AddUvicornApp("name", …)</c> call, absorbs the fluent
/// continuation lines that follow, and also absorbs later <c>name = name.…</c> reassignment
/// statements — the shape a cross-referencing pair of resources forces (the auth service needs
/// the portal's endpoint, the portal needs the auth service's service-discovery env var). A
/// plain substring scan over the whole file would not distinguish "fanned to the auth service"
/// from "fanned to a Blazor host", which is exactly the distinction P2-5 turns on.
/// </para>
/// <para>
/// The walk-up helper follows the <c>AppHostRealmImportArchitectureTests</c> /
/// <c>AppHostSettingsDbWiringArchitectureTests</c> precedent and THROWS rather than passing
/// vacuously when the AppHost or a named resource block cannot be found.
/// </para>
/// </summary>
[TestClass]
public class AppHostLoginUiFlagWiringArchitectureTests
{
    private const string FlagFanOut =
        """WithEnvironment("FeatureFlags__FEATURE__DisableKeycloakLoginUi", disableKeycloakLoginUi)""";

    private const string LoginUrlFanOut =
        """WithEnvironment("Auth__Portal__LoginUrl", $"{authPortal.GetEndpoint("http")}/login")""";

    private const string BootstrapRedirectUrlFanOut =
        """WithEnvironment("Auth__Portal__BootstrapRedirectUrl", $"{authPortal.GetEndpoint("http")}/bootstrap")""";

    private const string PublicBaseUrlFanOut =
        """WithEnvironment("AuthPortal__PublicBaseUrl", authPortal.GetEndpoint("http"))""";

    /// <summary>The four consumers the flag must reach (spec §10 / D5) — and no others.</summary>
    private static readonly string[] FlagConsumers = ["admin", "auth", "auth-portal", "families"];

    private static readonly string[] BlazorHosts = ["admin", "families"];

    private static readonly Regex CreatedResource = new(
        """^\s*(?:var\s+)?(?:(?<variable>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*)?builder\.(?:AddProject<Projects\.[A-Za-z0-9_]+>|AddUvicornApp)\("(?<resource>[^"]+)"[,)]""",
        RegexOptions.Compiled);

    private static readonly Regex ReassignedResource = new(
        @"^\s*(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<target>[A-Za-z_][A-Za-z0-9_]*)\.",
        RegexOptions.Compiled);

    private static readonly string AppHostProgramPath =
        Path.Combine(FindAppHostDir(), "Program.cs");

    private static readonly string AppHostSettingsPath =
        Path.Combine(FindAppHostDir(), "appsettings.json");

    /// <summary>The whole comment-stripped <c>Program.cs</c> — used for the exact-count assertions
    /// that pin a fan-out to a total number of call sites.</summary>
    private static readonly string Program = StripLineComments(File.ReadAllText(AppHostProgramPath));

    private static readonly IReadOnlyList<ResourceBlock> Blocks = ParseResourceBlocks(Program);

    [TestMethod]
    public void FlagFanOut_ReachesExactlyTheFourLoginUiConsumers()
    {
        var consumers = Blocks
            .Where(block => block.Source.Contains(FlagFanOut, StringComparison.Ordinal))
            .Select(block => block.ResourceName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        consumers.Should().BeEquivalentTo(FlagConsumers,
            "the login-UI flag is read at startup by exactly these four consumers (spec §10 / D5) — "
            + "a new consumer must be a conscious decision, and a silently dropped fan-out makes "
            + "that host render the wrong login UI.");

        // A whole-file count, so a fan-out written OUTSIDE any resource block (or into one the
        // block parser did not recognise) cannot slip past the per-block assertion above.
        Count(Program, FlagFanOut).Should().Be(FlagConsumers.Length,
            "each of the four consumers must receive the flag via the AppHost parameter — not a "
            + "literal and not a fifth call site.");
    }

    [TestMethod]
    public void PortalLoginUrl_ReachesTheBlazorHosts_AndIsAbsentFromTheAuthService()
    {
        foreach (var host in BlazorHosts)
        {
            Block(host).Source.Should().Contain(LoginUrlFanOut,
                $"{host} presents the challenge that the flag-ON PortalRedirect handler redirects to "
                + "the portal, so it needs the login URL — derived from the portal's own Aspire "
                + "endpoint, never a hardcoded port.");
        }

        // The absent half is the load-bearing one (plan-review P2-5): the auth service receives
        // the flag but NOT the login URL, so its own flag-ON challenge fails closed with 401
        // instead of redirecting a browser to a UI it does not host.
        Block("auth").Source.Should().NotContain("Auth__Portal__LoginUrl",
            "the auth service must NOT receive Auth:Portal:LoginUrl — the fail-closed 401 branch of "
            + "PortalRedirectChallengeHandler depends on it staying unconfigured there.");

        Count(Program, "Auth__Portal__LoginUrl").Should().Be(2,
            "the login URL reaches the two browser-facing Blazor hosts only; a third fan-out is a "
            + "widening of the browser-facing surface.");
    }

    [TestMethod]
    public void PortalBootstrapRedirectUrl_ReachesTheAuthServiceOnly()
    {
        Block("auth").Source.Should().Contain(BootstrapRedirectUrlFanOut,
            "on the passkey path the auth service is the OIDC relying party and is what redirects "
            + "the browser back to the portal's bootstrap route (D16) — so it, and only it, needs "
            + "the portal's bootstrap URL.");

        foreach (var other in FlagConsumers.Where(name => name != "auth"))
        {
            Block(other).Source.Should().NotContain("Auth__Portal__BootstrapRedirectUrl",
                $"{other} does not perform the passkey ceremony, so the bootstrap redirect target "
                + "must not leak onto it.");
        }

        Count(Program, "Auth__Portal__BootstrapRedirectUrl").Should().Be(1,
            "exactly one consumer receives the server-side D16 redirect target.");
    }

    [TestMethod]
    public void AuthPortal_IsRegisteredAsAUvicornApp_WithUv_AndReferencesTheAuthService()
    {
        var portal = Block("auth-portal");

        portal.Source.Should().Contain(
                """AddUvicornApp("auth-portal", "..\\..\\SchoolCollab.AuthPortal", "app:app")""",
                "the Python portal is launched through Aspire's Uvicorn integration, mirroring the "
                + "`portals` resource.")
            .And.Contain("WithUv()", "the project is a uv project — the resource must run `uv sync` before start.")
            .And.Contain("WithReference(auth)",
                "the portal's typed client resolves the auth service through the AppHost-injected "
                + "service-discovery env var (B2's AuthApiClient reads services__auth__http__0).")
            .And.Contain(PublicBaseUrlFanOut,
                "the portal's own browser-facing base URL comes from its Aspire endpoint "
                + "(owner-adjudicated pattern), not a hardcoded port.");

        Program.Should().NotContain("WireKeycloakAuth(authPortal",
            "WireKeycloakAuth is typed to IResourceBuilder<ProjectResource>; the Python portal cannot "
            + "use it and must take explicit WithEnvironment calls instead.");
    }

    [TestMethod]
    public void HandshakeTransport_ReferencesSurviveOnBothSides()
    {
        foreach (var host in BlazorHosts)
        {
            Block(host).Source.Should().Contain("WithReference(auth)",
                $"{host} registers the typed redeem client with the literal `https+http://auth` base "
                + "address, so CrossModuleWiringTests requires this matching AppHost reference — "
                + "without it the handshake dies at runtime with \"No such host is known\".");
        }

        Block("auth").Source.Should().Contain("WithReference(settingsApi)")
            .And.Contain("WithReference(studentsApi)",
                "D17: the mediated picker reads (tenants / teachers) are HTTP calls from the auth "
                + "service, so it must reference both data APIs.");
    }

    [TestMethod]
    public void FlagParameter_IsDeclaredInProgram_AndDefaultsOffInAppSettings()
    {
        Program.Should().Contain("""AddParameter("feature-flag-disable-keycloak-login-ui")""",
            "a Parameters: entry is not evidence the parameter exists — the round-A lesson "
            + "(keycloak-auth-admin-secret shipped a default and docs with no declaration).");

        ReadParameters(AppHostSettingsPath).Should()
            .ContainKey("feature-flag-disable-keycloak-login-ui")
            .WhoseValue.Should().Be("false",
                "spec D5: the custom login UI is opt-in, so the committed default stays OFF.");
    }

    [TestMethod]
    public void Scan_ActuallyFoundTheAppHostResources()
    {
        // Non-vacuity: every assertion above is per-resource, so a parser that silently matched
        // nothing (or that lost the continuation lines) would pass while inspecting empty blocks.
        Blocks.Should().HaveCountGreaterThanOrEqualTo(10,
            "the AppHost declares more than ten project/uvicorn resources; fewer means the parser "
            + "found no real blocks.");

        Blocks.Select(block => block.ResourceName)
            .Should().Contain(["migrator", "settings-api", "students-api", "assignments-api",
                "admin", "families", "auth", "auth-portal"]);

        foreach (var name in FlagConsumers)
        {
            Block(name).Source.Should().Contain("WithEnvironment(",
                $"the '{name}' block must have absorbed its fluent continuation lines.");
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private sealed record ResourceBlock(string ResourceName, string Source);

    /// <summary>
    /// Splits the AppHost source into per-resource blocks: the block opens on the resource's
    /// creation call, absorbs the fluent lines that follow, and absorbs later
    /// <c>name = name.…</c> reassignments of the same alias. A non-fluent statement closes the
    /// block; blank/comment lines are transparent (they are stripped beforehand, and the AppHost
    /// carries documented comments between chain lines).
    /// </summary>
    private static IReadOnlyList<ResourceBlock> ParseResourceBlocks(string program)
    {
        var sources = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        var aliasToResource = new Dictionary<string, string>(StringComparer.Ordinal);
        string? open = null;

        foreach (var rawLine in program.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var created = CreatedResource.Match(line);
            if (created.Success)
            {
                var resource = created.Groups["resource"].Value;
                var variable = created.Groups["variable"].Success
                    ? created.Groups["variable"].Value
                    : null;

                if (variable is not null)
                {
                    aliasToResource[variable] = resource;
                }

                sources[resource] = new StringBuilder(line);
                open = resource;
                continue;
            }

            var reassigned = ReassignedResource.Match(line);
            if (reassigned.Success)
            {
                var alias = reassigned.Groups["alias"].Value;
                var isSelfReassignment = alias == reassigned.Groups["target"].Value;

                if (isSelfReassignment && aliasToResource.TryGetValue(alias, out var resource))
                {
                    sources[resource].Append('\n').Append(line);
                    open = resource;
                }
                else
                {
                    open = null;
                }

                continue;
            }

            // Continuation lines are indented, so the marker is checked after trimming the
            // leading whitespace only (a `.` mid-line is not a fluent continuation).
            if (line.TrimStart().StartsWith('.'))
            {
                if (open is not null)
                {
                    sources[open].Append('\n').Append(line);
                }

                continue;
            }

            // Any other statement (helper definition, parameter declaration, `Build().Run()`,
            // `WireKeycloakAuth(...)`) ends the open chain.
            open = null;
        }

        return [.. sources.Select(entry => new ResourceBlock(entry.Key, entry.Value.ToString()))];
    }

    private static ResourceBlock Block(string resourceName)
        => Blocks.SingleOrDefault(block => block.ResourceName == resourceName)
            ?? throw new InvalidOperationException(
                $"The AppHost Program.cs has no resource block named '{resourceName}'. Known blocks: "
                + string.Join(", ", Blocks.Select(block => block.ResourceName).OrderBy(name => name, StringComparer.Ordinal)));

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

    /// <summary>Reads a file's <c>Parameters</c> section as a key → value map. THROWS when the
    /// section is absent, so a missing block cannot make the assertions pass vacuously.</summary>
    private static Dictionary<string, string> ReadParameters(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("Parameters", out var parameters)
            || parameters.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{path} has no object 'Parameters' section.");
        }

        return parameters.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
    }

    /// <summary>
    /// Removes trailing <c>//</c> comments before parsing. The AppHost documents its chains with
    /// comments between continuation lines, and a <c>//</c>-comment naming a config key would
    /// otherwise satisfy (or break) a substring assertion. No string literal in the file contains
    /// <c>//</c> (urls in the file are built from endpoint expressions, e.g.
    /// <c>$"{keycloak.GetEndpoint("http")}/realms/school-collab"</c>).
    /// </summary>
    private static string StripLineComments(string source)
        => string.Join('\n', source.Split('\n').Select(line =>
        {
            var commentAt = line.IndexOf("//", StringComparison.Ordinal);
            return commentAt >= 0 ? line[..commentAt] : line;
        }));

    private static string FindAppHostDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "AppHost", "SchoolCollab.AppHost")))
        {
            dir = dir.Parent;
        }

        return dir is null
            ? throw new InvalidOperationException(
                "Could not locate src/AppHost/SchoolCollab.AppHost from " + AppContext.BaseDirectory)
            : Path.Combine(dir.FullName, "src", "AppHost", "SchoolCollab.AppHost");
    }
}
