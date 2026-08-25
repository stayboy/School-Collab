using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace SchoolCollab.Core.Tests.Unit.Architecture;

/// <summary>
/// Architectural guard: every cross-module HTTP client base address in
/// <c>src/**</c> that targets an Aspire service by name (e.g.
/// <c>"https+http://settings-api"</c>) must have a matching
/// <c>.WithReference(&lt;resourceVar&gt;)</c> on the CALLING project in the
/// AppHost (<c>src/AppHost/SchoolCollab.AppHost/Program.cs</c>).
///
/// <para>WHY THIS TEST EXISTS: a missing WithReference is invisible at build
/// time — service discovery simply has no endpoint to rewrite the URL, the
/// call falls back to literal DNS, and the failure only surfaces at runtime as
/// <c>No such host is known (settings-api:80)</c>. This exact bug shipped in
/// Students.Api (enroll → CodedValues hop) and was misdiagnosed for days as an
/// HttpClient handler-lifetime problem because nothing mechanically compared
/// client registrations against the AppHost topology. This test turns that
/// mistake into a red build.</para>
///
/// <para>ATTRIBUTION RULES:</para>
/// <list type="bullet">
/// <item>Registration file lives under a project that the AppHost hosts via
///       <c>AddProject&lt;Projects.X&gt;("resource-name")</c> → STRICT: that
///       project's own statement must contain <c>.WithReference</c> of a var
///       bound to the target resource name.</item>
/// <item>Registration file lives under a class library (Blazor module, shared
///       kernel — e.g. SchoolCollab.Students.Application) that runs inside one
///       or more hosted hosts → WEAK: at least ONE hosted project must
///       reference the target. LIMITATION: this cannot tell WHICH host actually
///       executes the library's registration; when adding a client to a
///       library consumed by several hosts, verify each consuming host's
///       wiring manually.</item>
/// </list>
///
/// <para>The test also fails on UNKNOWN service names (e.g. a typo like
/// <c>https+http://studentsapi</c>) — every target must correspond to a
/// resource declared anywhere in the AppHost.</para>
/// </summary>
[TestClass]
public class CrossModuleWiringTests
{
    // ── Scan result records ─────────────────────────────────────────────

    private sealed record ClientRegistration(
        string File,
        string TargetService);

    private sealed record HostedProject(
        string ResourceName,
        string CsprojNameDots,
        IReadOnlySet<string> ReferencedResources);

    // ── Test ────────────────────────────────────────────────────────────

    [TestMethod]
    public void CrossModule_BaseAddresses_HaveMatchingAppHostReference()
    {
        var repoRoot = FindRepoRoot();
        var appHostSource = ReadCommentStripped(Path.Combine(repoRoot.FullName, "src", "AppHost", "SchoolCollab.AppHost", "Program.cs"));

        var hosted = ParseHostedProjects(appHostSource);
        var declaredResources = ParseDeclaredResourceNames(appHostSource);
        var registrations = ScanClientRegistrations(repoRoot);
        var csprojGraph = BuildProjectReferenceGraph(repoRoot);

        var violations = new List<string>();

        foreach (var reg in registrations)
        {
            // Unknown resource name → typo'd service name. Fail regardless of attribution.
            if (!declaredResources.Contains(reg.TargetService))
            {
                violations.Add(
                    $"{Relative(repoRoot, reg.File)}: base address targets '{reg.TargetService}' " +
                    $"but NO resource with that name is declared in AppHost/Program.cs (typo?)");
                continue;
            }

            var ownerCsproj = FindNearestCsprojName(repoRoot, reg.File);
            var hostedOwner = hosted.FirstOrDefault(h => h.CsprojNameDots == ownerCsproj);

            if (hostedOwner is { } owner)
            {
                // STRICT rule for directly-hosted projects.
                if (!owner.ReferencedResources.Contains(reg.TargetService))
                {
                    violations.Add(
                        $"{Relative(repoRoot, reg.File)}: registers a cross-module client → '{reg.TargetService}' " +
                        $"but AppHost project '{owner.ResourceName}' has no .WithReference for it. " +
                        $"Add .WithReference(<{reg.TargetService}Var>) to '{owner.ResourceName}' or the call fails " +
                        $"at runtime with \"No such host is known ({reg.TargetService}:80)\".");
                }
            }
            else
            {
                // STRICT rule for class libraries: EVERY hosted project that
                // DIRECTLY consumes the library must reference the target.
                // We can't trace which extension methods each consumer calls,
                // so the conservative rule is "all direct consumers wired".
                var consumers = csprojGraph
                    .Where(kvp => kvp.Value.Contains(ownerCsproj))
                    .Select(kvp => kvp.Key)
                    .ToHashSet();
                var hostedConsumers = hosted
                    .Where(h => consumers.Contains(h.CsprojNameDots))
                    .ToList();

                if (hostedConsumers.Count == 0)
                {
                    violations.Add(
                        $"{Relative(repoRoot, reg.File)}: library '{ownerCsproj}' registers a cross-module client " +
                        $"=> '{reg.TargetService}' but no AppHost-hosted project references '{ownerCsproj}'. " +
                        $"Is the library dead code, or is its host missing from the AppHost?");
                }
                else
                {
                    var missingHosts = hostedConsumers
                        .Where(c => !c.ReferencedResources.Contains(reg.TargetService))
                        // A hosted project cannot .WithReference itself; Aspire
                        // would reject the circular wiring. Exclude the case
                        // where the consumer IS the target service.
                        .Where(c => c.ResourceName != reg.TargetService)
                        .Select(c => c.ResourceName)
                        .ToList();

                    if (missingHosts.Count > 0)
                    {
                        violations.Add(
                            $"{Relative(repoRoot, reg.File)}: library '{ownerCsproj}' registers a cross-module client " +
                            $"=> '{reg.TargetService}' but direct hosting projects [{string.Join(", ", missingHosts)}] " +
                            $"do not .WithReference it. Add .WithReference for '{reg.TargetService}' on each, " +
                            $"or the call fails at runtime with \"No such host is known ({reg.TargetService}:80)\".");
                    }
                }
            }
        }

        violations.Should().BeEmpty(
            "every cross-module HTTP base address needs Aspire service discovery wiring; " +
            "a missing .WithReference surfaces only at runtime as \"No such host is known\"");
    }

    // ── Parsing: AppHost ────────────────────────────────────────────────

    /// <summary>Parses AddProject declarations and their full statement chain
    /// (up to the terminating ';') to extract each project's WithReference set.
    /// Statement bodies never contain ';' before the terminator, so slicing
    /// from declaration start to the next ';' is safe.</summary>
    private static List<HostedProject> ParseHostedProjects(string appHostSource)
    {
        // NOTE: the leading "var x =" assignment is OPTIONAL — some projects
        // (e.g. admin) are added directly without binding the builder.
        var declRegex = new Regex(
            @"(?:var\s+(?<var>\w+)\s*=\s*)?builder\.AddProject<Projects\.(?<csproj>[\w.]+)>\(\s*""(?<resource>[^""]+)""",
            RegexOptions.Compiled);
        var withRefRegex = new Regex(@"\.WithReference\((?<var>\w+)\)", RegexOptions.Compiled);
        // Post-declaration re-assignments: `foo = foo.WithReference(bar);` when
        // the target var is declared later than the consumer's first statement.
        var reassignRefRegex = new Regex(
            @"^\s*(?<lhs>\w+)\s*=\s*\w+\.WithReference\((?<rhs>\w+)\)",
            RegexOptions.Compiled | RegexOptions.Multiline);

        var projects = new List<HostedProject>();
        var byVar = new Dictionary<string, HostedProject>();
        foreach (Match m in declRegex.Matches(appHostSource))
        {
            var end = appHostSource.IndexOf(';', m.Index);
            var body = end > m.Index ? appHostSource[m.Index..end] : string.Empty;
            var referencedVars = withRefRegex.Matches(body)
                .Select(x => x.Groups["var"].Value)
                .ToHashSet();

            var hp = new HostedProject(
                ResourceName: m.Groups["resource"].Value,
                CsprojNameDots: m.Groups["csproj"].Value.Replace('_', '.'),
                ReferencedResources: ResolveReferences(referencedVars));
            projects.Add(hp);
            if (m.Groups["var"].Success) byVar[m.Groups["var"].Value] = hp;
        }

        // Fold in any post-declaration WithReference re-assignments.
        foreach (Match m in reassignRefRegex.Matches(appHostSource))
        {
            var lhs = m.Groups["lhs"].Value;
            if (!byVar.TryGetValue(lhs, out var hp)) continue;
            var extra = FindVarResource(appHostSource, m.Groups["rhs"].Value);
            if (extra is not null && !hp.ReferencedResources.Contains(extra))
            {
                projects.Remove(hp);
                var merged = new HostedProject(
                    hp.ResourceName,
                    hp.CsprojNameDots,
                    new HashSet<string>(hp.ReferencedResources) { extra });
                projects.Add(merged);
                byVar[lhs] = merged;
            }
        }
        return projects;

        // Map WithReference(varName) → resource name using ALL var declarations.
        IReadOnlySet<string> ResolveReferences(HashSet<string> vars)
        {
            var resolved = new HashSet<string>();
            foreach (var v in vars)
            {
                var resource = FindVarResource(appHostSource, v);
                if (resource is not null) resolved.Add(resource);
            }
            return resolved;
        }
    }

    /// <summary>Finds the first quoted string argument of the builder/Add call
    /// assigned to <paramref name="varName"/> — covers AddProject,
    /// AddPostgres/AddDatabase chains, containers (rabbit/redis), etc.</summary>
    private static string? FindVarResource(string source, string varName)
    {
        var regex = new Regex(
            $@"var\s+{Regex.Escape(varName)}\s*=\s*\w+\.Add\w+(?:<[^;]*?>)?\(\s*""(?<resource>[^""]+)""",
            RegexOptions.Compiled | RegexOptions.Singleline);
        // Non-greedy across the generic; take first match.
        foreach (Match m in regex.Matches(source))
            return m.Groups["resource"].Value;
        return null;
    }

    /// <summary>Every resource name declared anywhere in the AppHost (projects,
    /// databases, containers) — used to reject typo'd service names.</summary>
    private static HashSet<string> ParseDeclaredResourceNames(string appHostSource)
    {
        var regex = new Regex(@"\.Add\w+(?:<[^;]*?>)?\(\s*""(?<resource>[^""]+)""", RegexOptions.Compiled);
        return regex.Matches(appHostSource)
            .Select(m => m.Groups["resource"].Value)
            .ToHashSet();
    }

    // ── Scanning: src/**/*.cs base-address literals ─────────────────────

    private static List<ClientRegistration> ScanClientRegistrations(DirectoryInfo repoRoot)
    {
        var urlRegex = new Regex(@"""(?:(?:https?\+http)|http)://(?<host>[a-z][a-z0-9-]*)""", RegexOptions.Compiled);
        var excludedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "localhost", "host.docker.internal", "0.0.0.0", "example.com", "example.org"
        };

        var srcDir = Path.Combine(repoRoot.FullName, "src");
        var appHostDir = Path.Combine(srcDir, "AppHost");

        var results = new List<ClientRegistration>();
        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(file);
            if (full.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || full.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || full.StartsWith(appHostDir, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var cleaned = ReadCommentStripped(full);
            foreach (Match m in urlRegex.Matches(cleaned))
            {
                var host = m.Groups["host"].Value;
                if (!excludedHosts.Contains(host))
                {
                    results.Add(new ClientRegistration(full, host));
                }
            }
        }
        return results;
    }

    // ── csproj ProjectReference graph (for library attribution) ─────────

    /// <summary>Maps csproj-name-without-extension → set of directly
    /// referenced csproj names (same format), parsed from src/**.csproj.</summary>
    private static Dictionary<string, HashSet<string>> BuildProjectReferenceGraph(DirectoryInfo repoRoot)
    {
        var graph = new Dictionary<string, HashSet<string>>();
        // Capture the FULL Include attribute value, then derive the project
        // name via Path.GetFileNameWithoutExtension. A naive "([\w.]+)\.csproj"
        // against a greedy [^"]* prefix backtracks down to a SINGLE-character
        // capture (the letter right before .csproj) — classic greedy-backtrack
        // trap; the full-path capture sidesteps it entirely.
        var refRegex = new Regex(@"<ProjectReference\s+Include=""(?<include>[^""]+\.csproj)""", RegexOptions.Compiled);

        foreach (var csproj in Directory.EnumerateFiles(Path.Combine(repoRoot.FullName, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(csproj);
            if (full.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || full.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(csproj);
            var content = File.ReadAllText(csproj);
            graph[name] = refRegex.Matches(content)
                .Select(m => Path.GetFileNameWithoutExtension(m.Groups["include"].Value.Replace('\\', '/')))
                .ToHashSet();
        }
        return graph;
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SchoolCollab.sln")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("SchoolCollab.sln should exist above the test output directory");
        return dir!;
    }

    private static string Relative(DirectoryInfo root, string fullPath)
        => Path.GetRelativePath(root.FullName, fullPath);

    /// <summary>Returns the name (without extension) of the nearest ancestor
    /// .csproj for a source file.</summary>
    private static string? FindNearestCsprojName(DirectoryInfo repoRoot, string filePath)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(filePath)!);
        while (dir is not null && dir.FullName.StartsWith(repoRoot.FullName, StringComparison.OrdinalIgnoreCase))
        {
            var csproj = dir.EnumerateFiles("*.csproj").FirstOrDefault();
            if (csproj is not null) return Path.GetFileNameWithoutExtension(csproj.Name);
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>Reads a source file with // and /* */ comments removed while
    /// respecting double-quoted string literals (so URLs inside strings — the
    /// very things we scan for — survive, and URLs inside comments do not).</summary>
    private static string ReadCommentStripped(string path)
    {
        var text = File.ReadAllText(path);
        var sb = new StringBuilder(text.Length);
        var i = 0;
        var inString = false;
        var inLineComment = false;
        var inBlockComment = false;

        while (i < text.Length)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n') { inLineComment = false; sb.Append(c); }
                i++;
                continue;
            }
            if (inBlockComment)
            {
                if (c == '*' && next == '/') { inBlockComment = false; i += 2; continue; }
                i++;
                continue;
            }
            if (inString)
            {
                sb.Append(c);
                if (c == '\\' && next != '\0') { sb.Append(next); i += 2; continue; }
                if (c == '"') inString = false;
                i++;
                continue;
            }
            if (c == '/' && next == '/') { inLineComment = true; i += 2; continue; }
            if (c == '/' && next == '*') { inBlockComment = true; i += 2; continue; }
            if (c == '"') { inString = true; sb.Append(c); i++; continue; }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }
}

