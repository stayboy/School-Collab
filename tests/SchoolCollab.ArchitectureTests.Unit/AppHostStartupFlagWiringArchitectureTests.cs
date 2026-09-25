using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Features;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the startup-switch governance adopted 2026-09-25
/// (<c>documents/specs/startup-flag-governance.md</c>, grill Q1–Q6). Two invariants that no
/// build or unit test can see, because they live in configuration rather than in code:
/// <list type="number">
///   <item><b>Guard A</b> — no per-host <c>appsettings*.json</c> (base or environment-specific)
///        carries either startup switch, in any shape. A per-host copy would be <b>silently
///        masked</b> by the AppHost's fanned env var (env vars outrank appsettings) while
///        silently governing every standalone run — the failure mode that made
///        <c>FEATURE:DisableOIDCAuth</c> live in six files with no single flip point;</item>
///   <item><b>Guard B</b> — <c>FEATURE:DisableOIDCAuth</c> is declared as an AppHost parameter
///        with a <b>fail-closed</b> committed base default (<c>"false"</c>, so a publish can
///        never bake <c>TestAuth</c> into a manifest), a Development-file dev posture
///        (<c>"true"</c>, beside the Keycloak dev secrets — the repo's proven home for
///        dev-only values), and is fanned out to <b>exactly</b> the six hosts that call
///        <c>AddAuthAndTenancy</c>.</item>
/// </list>
/// <para>
/// The read itself is untouched (<c>AuthTenancyExtensions.IsFlagEnabled</c>, registration time)
/// — this round governs <i>where the value is written</i>, never when or how it is read. The
/// resource-block parser, <c>ReadParameters</c> helper and non-vacuity pattern are mirrored
/// from <see cref="AppHostLoginUiFlagWiringArchitectureTests"/> (the login-UI flag's guard),
/// so the two switches are held to the same standard by the same means.
/// </para>
/// </summary>
[TestClass]
public class AppHostStartupFlagWiringArchitectureTests
{
    private const string OidcFlagParameterName = "feature-flag-disable-oidc-auth";

    /// <summary>The fan-out literal — pins the env-var spelling AND the parameter variable.</summary>
    private const string OidcFlagFanOut =
        """WithEnvironment("FeatureFlags__FEATURE__DisableOIDCAuth", disableOidcAuth)""";

    /// <summary>The six hosts that call <c>AddAuthAndTenancy</c> — and no others. auth-portal,
    /// the two workers and the migrator are verified non-consumers (the portal holds no OIDC
    /// client; the workers/migrator never register auth).</summary>
    private static readonly string[] OidcFlagConsumers =
        ["admin", "assignments-api", "auth", "families", "settings-api", "students-api"];

    /// <summary>The two deployment-time startup switches. Runtime <c>Enable*</c> flags are
    /// deliberately OUT of scope: their per-host cold-start fallbacks are a different,
    /// documented mechanism (the Config service is authoritative at runtime).</summary>
    private static readonly string[] StartupSwitchKeys =
        [FeatureFlagKeys.DisableOIDCAuth, FeatureFlagKeys.DisableKeycloakLoginUi];

    /// <summary>Families' appsettings carries <c>//</c> comments, and the JSON configuration
    /// provider accepts them — so the parser must skip them too, or Guard A would throw on a
    /// legitimate file.</summary>
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Regex CreatedResource = new(
        """^\s*(?:var\s+)?(?:(?<variable>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*)?builder\.(?:AddProject<Projects\.[A-Za-z0-9_]+>|AddUvicornApp)\("(?<resource>[^"]+)"[,)]""",
        RegexOptions.Compiled);

    private static readonly Regex ReassignedResource = new(
        @"^\s*(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<target>[A-Za-z_][A-Za-z0-9_]*)\.", RegexOptions.Compiled);

    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly string AppHostDir =
        Path.Combine(RepoRoot, "src", "AppHost", "SchoolCollab.AppHost");

    private static readonly string AppHostProgramPath = Path.Combine(AppHostDir, "Program.cs");

    private static readonly string AppHostSettingsPath = Path.Combine(AppHostDir, "appsettings.json");

    private static readonly string AppHostDevelopmentSettingsPath =
        Path.Combine(AppHostDir, "appsettings.Development.json");

    /// <summary>The whole comment-stripped <c>Program.cs</c> — used for the exact-count
    /// assertion that pins the fan-out to a total number of call sites.</summary>
    private static readonly string Program = StripLineComments(File.ReadAllText(AppHostProgramPath));

    private static readonly IReadOnlyList<ResourceBlock> Blocks = ParseResourceBlocks(Program);

    /// <summary>Every <c>src/**/appsettings*.json</c> — base AND environment-specific —
    /// excluding build output. Enumerated once; Guard A asserts it is non-empty.</summary>
    private static readonly IReadOnlyList<string> PerHostAppSettings = FindPerHostAppSettings();

    [TestMethod]
    public void StartupSwitches_AreAbsentFromEveryPerHostAppsettings()
    {
        var violations = new List<string>();

        foreach (var path in PerHostAppSettings)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), DocumentOptions);

            foreach (var key in StartupSwitchKeys)
            {
                var occurrence = FindStartupSwitchOccurrence(doc.RootElement, key);
                if (occurrence is not null)
                {
                    violations.Add($"{Path.GetRelativePath(RepoRoot, path)} → {occurrence}");
                }
            }
        }

        violations.Should().BeEmpty(
            "a startup switch lives in the AppHost parameter and reaches hosts through "
            + "WithEnvironment (documents/specs/startup-flag-governance.md). A per-host "
            + "appsettings copy is SILENTLY MASKED by the fanned env var inside the AppHost "
            + "(env vars outrank appsettings) while silently governing every standalone run — "
            + "that is exactly how FEATURE:DisableOIDCAuth ended up in six files with no single "
            + "flip point. Set the value in <AppHost>/appsettings.json (fail-closed default) or "
            + "<AppHost>/appsettings.Development.json (dev posture) instead.");
    }

    [TestMethod]
    public void StartupFlagParameter_IsDeclaredWithFailClosedDefault_AndFannedOutToExactlyTheSixConsumers()
    {
        Program.Should().Contain($"""AddParameter("{OidcFlagParameterName}")""",
            "a Parameters: entry is not evidence the parameter exists — the round-A lesson "
            + "(keycloak-auth-admin-secret shipped a default and docs with no declaration).");

        ReadParameters(AppHostSettingsPath).Should()
            .ContainKey(OidcFlagParameterName)
            .WhoseValue.Should().Be("false",
                "the committed default is FAIL-CLOSED: a publish must never bake TestAuth into a "
                + "manifest, and a missing env var must mean OIDC rather than a fake identity.");

        ReadParameters(AppHostDevelopmentSettingsPath).Should()
            .ContainKey(OidcFlagParameterName)
            .WhoseValue.Should().Be("true",
                "the dev posture is unchanged: `aspire run` is a Development run, so it must "
                + "resolve TestAuth exactly as before adoption (the Playwright suites document "
                + "that assumption). The value lives beside the Keycloak dev secrets.");

        var consumers = Blocks
            .Where(block => block.Source.Contains(OidcFlagFanOut, StringComparison.Ordinal))
            .Select(block => block.ResourceName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        consumers.Should().BeEquivalentTo(OidcFlagConsumers,
            "the startup auth-mode switch is read at registration time by exactly the six hosts "
            + "that call AddAuthAndTenancy — a new consumer must be a conscious decision, and a "
            + "silently dropped fan-out makes that host fall back to OIDC in dev.");

        // A whole-file count, so a fan-out written OUTSIDE any resource block (or into one the
        // block parser did not recognise) cannot slip past the per-block assertion above.
        Count(Program, OidcFlagFanOut).Should().Be(OidcFlagConsumers.Length,
            "each of the six consumers must receive the flag via the AppHost parameter — not a "
            + "literal and not a seventh call site.");
    }

    [TestMethod]
    public void Scan_ActuallyFoundTheAppHostResources_AndThePerHostSettingsFiles()
    {
        // Non-vacuity, twice over: every assertion above is per-resource or per-file, so a
        // parser that matched nothing would pass while inspecting empty collections.
        PerHostAppSettings.Should().HaveCountGreaterThanOrEqualTo(10,
            "the repo declares appsettings for every host plus several environment-specific "
            + "files; fewer means the scan found no real files.");

        PerHostAppSettings.Should().Contain(
            Path.Combine(RepoRoot, "src", "SchoolCollab.Auth", "appsettings.json"),
            "the auth host's base settings must be among the scanned files — it is the file that "
            + "carried the scattered copy this round removed.");

        Blocks.Select(block => block.ResourceName)
            .Should().Contain(OidcFlagConsumers)
            .And.Contain(["auth-portal", "migrator"]);

        foreach (var name in OidcFlagConsumers)
        {
            Block(name).Source.Should().Contain("WithEnvironment(",
                $"the '{name}' block must have absorbed its fluent continuation lines.");
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private sealed record ResourceBlock(string ResourceName, string Source);

    /// <summary>
    /// Finds a startup switch in <paramref name="root"/>, in any shape the configuration
    /// provider resolves to <c>FeatureFlags:FEATURE:&lt;Name&gt;</c>: the nested object
    /// (<c>FeatureFlags.FEATURE.&lt;Name&gt;</c>), the flat colon key
    /// (<c>FeatureFlags."FEATURE:&lt;Name&gt;"</c> — the shape Auth and Settings.Api used),
    /// the root-level joined key, or the bare flag key (<c>IsFlagEnabled</c> falls back to it).
    /// Returns the offending path for the failure message, or <see langword="null"/>.
    /// </summary>
    private static string? FindStartupSwitchOccurrence(JsonElement root, string featureKey)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var shortName = featureKey["FEATURE:".Length..];

        if (root.TryGetProperty($"FeatureFlags:{featureKey}", out _))
        {
            return $"FeatureFlags:{featureKey}";
        }

        if (root.TryGetProperty(featureKey, out _))
        {
            return featureKey;
        }

        if (!root.TryGetProperty("FeatureFlags", out var flags) || flags.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (flags.TryGetProperty(featureKey, out _))
        {
            return $"FeatureFlags:\"{featureKey}\"";
        }

        if (flags.TryGetProperty("FEATURE", out var feature)
            && feature.ValueKind == JsonValueKind.Object
            && feature.TryGetProperty(shortName, out _))
        {
            return $"FeatureFlags:FEATURE:{shortName}";
        }

        return null;
    }

    private static IReadOnlyList<string> FindPerHostAppSettings()
        => [.. Directory
            .EnumerateFiles(Path.Combine(RepoRoot, "src"), "appsettings*.json", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains(
                    $"{Path.DirectorySeparatorChar}.venv{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains(
                    $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)];

    /// <summary>
    /// Splits the AppHost source into per-resource blocks: the block opens on the resource's
    /// creation call, absorbs the fluent lines that follow, and absorbs later
    /// <c>name = name.…</c> reassignments of the same alias. A non-fluent statement closes the
    /// block. Mirrors <c>AppHostLoginUiFlagWiringArchitectureTests.ParseResourceBlocks</c>.
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

            if (line.TrimStart().StartsWith('.'))
            {
                if (open is not null)
                {
                    sources[open].Append('\n').Append(line);
                }

                continue;
            }

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
        using var doc = JsonDocument.Parse(File.ReadAllText(path), DocumentOptions);
        if (!doc.RootElement.TryGetProperty("Parameters", out var parameters)
            || parameters.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{path} has no object 'Parameters' section.");
        }

        return parameters.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
    }

    /// <summary>
    /// Removes trailing <c>//</c> comments before parsing, so a comment naming a config key can
    /// neither satisfy nor break a substring assertion. No string literal in the file contains
    /// <c>//</c> (URLs are built from endpoint expressions).
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
                "Could not locate the repo root (src/AppHost/SchoolCollab.AppHost) from " + AppContext.BaseDirectory);
    }
}
