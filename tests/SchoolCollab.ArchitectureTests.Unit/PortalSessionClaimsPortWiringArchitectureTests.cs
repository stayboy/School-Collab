using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the defect class round <c>portal-claims-port-binding</c> closed
/// (<c>documents/rounds/round-portal-claims-port-binding.md</c>): a host adopts the portal-session
/// scheme, whose handler is built by <b>DI</b> (<c>AddScheme&lt;TOptions, THandler&gt;</c> in
/// <c>AddPortalSessionAuthentication</c>), but nothing in that host binds the
/// <c>IPortalSessionClaimsReader</c> its constructor requires. The compiler accepts that shape, the
/// build is green, and the failure only appears <b>at request time</b> as
/// <c>InvalidOperationException: Unable to resolve service for type '…IPortalSessionClaimsReader'
/// while attempting to activate 'PortalSessionAuthenticationHandler'</c> — an HTTP <b>500</b> where
/// the challenge owes a bare <b>401</b>. That is exactly what happened to the Assignments API: it
/// registered the typed client (<c>AddCrossModuleHttpClient&lt;PortalSessionClaimsReader&gt;</c>,
/// which binds the concrete class alone) and never forwarded the port.
/// <list type="number">
///   <item><b>Every host that adopts the scheme binds the port.</b> For each source under
///       <c>src/</c> calling <c>&lt;anything&gt;.AddPortalSessionAuthentication(</c>, walk up to that
///       file's nearest ancestor directory holding a <c>*.csproj</c>, and require a
///       <c>Add{Singleton|Transient|Scoped}&lt;IPortalSessionClaimsReader&gt;</c> registration
///       <b>somewhere beneath that project directory</b> — a host may lawfully keep the call in one
///       file (the auth service's <c>AuthServiceExtensions.cs</c>) and the binding in another (the
///       Assignments API's <c>Program.cs</c>).</item>
///   <item><b>The adopting-host set is pinned to the two that exist.</b> Exactly two hosts adopt the
///       scheme today; a third must extend the pin — and bring its own port binding — in the same
///       change, so the guard grows with its class.</item>
///   <item><b>The guard cannot go vacuous.</b> It also reads the handler and asserts its constructor
///       still takes the port: if the ctor ever stops naming
///       <c>IPortalSessionClaimsReader</c>, the two rules above are ceremony rather than protection,
///       and this test says so.</item>
/// </list>
/// <para>
/// The scan root is <c>src/</c>: a host is a project this repository deploys, whereas the test hosts
/// under <c>tests/</c> compose their own pipeline and stub the port by hand (the round
/// <c>portal-session-adoption</c> D7 doctrine), so they are deliberately outside this guard's class.
/// </para>
/// <para>
/// Follows the <c>BearerForwardingWiringArchitectureTests</c> /
/// <c>AppHostEndpointPortingArchitectureTests</c> idiom: read-only source scans, a
/// <c>FindRepoRoot()</c> walk-up that THROWS rather than passing vacuously, count-based assertions
/// whose failure messages give a named reason, and <b>line comments stripped before matching</b> so a
/// commented-out call — or a doc sentence naming the port — can neither satisfy nor break a rule.
/// No string literal in the scanned files carries <c>//</c> ahead of the matched tokens.
/// </para>
/// </summary>
[TestClass]
public class PortalSessionClaimsPortWiringArchitectureTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    /// <summary>The extension-method call a host makes to adopt the scheme. The leading dot is what
    /// distinguishes a CALL from the extension's own declaration in
    /// <c>PortalSessionAuthenticationHandler.cs</c> (which it names without a dot).</summary>
    private const string AdoptionCall = ".AddPortalSessionAuthentication(";

    /// <summary>Every DI lifetime a host may legally use to forward the port. A registration is a
    /// generic type argument naming the port — never the port's mere mention in prose or in the
    /// reader's own <c>: IPortalSessionClaimsReader</c> declaration, which is why the generic
    /// argument (and not the bare type name) is what gets counted.</summary>
    private static readonly string[] PortRegistrationForms =
    [
        "AddSingleton<IPortalSessionClaimsReader>",
        "AddTransient<IPortalSessionClaimsReader>",
        "AddScoped<IPortalSessionClaimsReader>",
    ];

    /// <summary>The complete set of hosts that adopt the portal-session scheme, and the file each one
    /// adopts it from. Bumping this is a deliberate act.</summary>
    private static readonly (string ProjectDirectory, string AdoptionFile)[] PinnedAdoptingHosts =
    [
        ("src/SchoolCollab.Auth", "AuthServiceExtensions.cs"),
        ("src/Assignments/SchoolCollab.Assignments.Api", "Program.cs"),
    ];

    private static readonly IReadOnlyList<AdoptingHost> AdoptingHosts = FindAdoptingHosts();

    [TestMethod]
    public void EveryAdoptingHost_AlsoBindsTheClaimsPort()
    {
        AdoptingHosts.Should().NotBeEmpty(
            $"the scan must find the hosts calling '{AdoptionCall}…' under src/ — an empty scan would "
            + "make this rule vacuous, and the pinned-host rule below would pass while inspecting "
            + "nothing.");

        foreach (var host in AdoptingHosts)
        {
            var registrations = PortRegistrationForms.Sum(form => Count(host.PortSources, form));

            registrations.Should().BeGreaterThan(0,
                $"'{host.ProjectDirectory}' adopts the portal-session scheme "
                + $"({string.Join(" and ", host.AdoptionFiles)} call "
                + $"'{AdoptionCall}…'), so its handler is activated by DI and its constructor's "
                + "IPortalSessionClaimsReader dependency must resolve. Nothing under that project "
                + "binds the port, and an unregistered ctor dependency is a DI failure at request "
                + "time — an HTTP 500 where the challenge owes a bare 401 — not a build error. Bind it "
                + "in the host project, forwarding the typed client: "
                + "AddTransient<IPortalSessionClaimsReader>(sp => sp.GetRequiredService<…>()).");
        }
    }

    [TestMethod]
    public void AdoptingHosts_ArePinnedToTheTwoThatExist()
    {
        AdoptingHosts.Should().HaveCount(PinnedAdoptingHosts.Length,
            "exactly two hosts adopt the portal-session scheme today: the auth service (over its "
            + "in-process custody adapter) and the Assignments API (over the remote claims port). The "
            + "guard grows with its class — a third adopting host must extend this pin, and add its "
            + "own IPortalSessionClaimsReader binding, in the same change.");

        foreach (var (projectDirectory, adoptionFile) in PinnedAdoptingHosts)
        {
            var host = AdoptingHosts.SingleOrDefault(candidate => candidate.ProjectDirectory == projectDirectory);

            host.Should().NotBeNull(
                $"'{projectDirectory}' must still adopt the scheme by calling '{AdoptionCall}…': the "
                + "count pin above is only meaningful while both pinned hosts exist, and dropping the "
                + "call would silently drop this host — and any host that would have followed it — "
                + "out of the port-binding rule.");

            host!.AdoptionFiles.Should().Contain(adoptionFile,
                $"'{projectDirectory}' adopts the scheme from {adoptionFile}. Moving the call to "
                + "another file inside the same project is legal, but it is a deliberate act: update "
                + "this pin in the same change rather than leaving the guard pointing at a file that "
                + "no longer carries the adoption.");
        }
    }

    [TestMethod]
    public void HandlerConstructor_StillRequiresTheClaimsPort()
    {
        var handler = StripLineComments(
            Read("src", "SchoolCollab.Core", "Auth", "PortalSessionAuthenticationHandler.cs"));

        const string ctorDeclaration = "public sealed class PortalSessionAuthenticationHandler(";
        var declarationAt = handler.IndexOf(ctorDeclaration, StringComparison.Ordinal);

        declarationAt.Should().BeGreaterThanOrEqualTo(0,
            $"the portal-session handler must still be declared as '{ctorDeclaration}…' with its "
            + "constructor parameter list on the same statement — this guard locates the ctor "
            + "dependency there.");

        // The primary-constructor parameter list ends where the derived-type separator begins — the first
        // line after the declaration whose first non-whitespace character is ':'. Delimiting there, rather
        // than at the first ')', keeps this read correct if a parameter type ever gains a nested
        // parenthesis (a generic constraint, a tuple or a delegate): the first-')' parse would have
        // truncated early and failed this guard against a file that was in fact correct.
        // (Diff-review P2, applied verbatim pre-PR — the reviewer's own smallest fix; no third review pass.)
        var separator = System.Text.RegularExpressions.Regex.Match(
            handler[declarationAt..],
            @"^[ \t]*:",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        separator.Success.Should().BeTrue(
            "the handler's primary constructor must be followed by its ':' base-type separator: that "
            + "separator is what delimits the parameter list this guard reads, and without it the ctor "
            + "dependency below would be read out of the wrong text.");

        handler[declarationAt..(declarationAt + separator.Index)].Should().Contain("IPortalSessionClaimsReader",
            "the handler constructor must still name IPortalSessionClaimsReader. That ctor dependency "
            + "is what makes an unbound port a request-time DI failure instead of a 401, and therefore "
            + "what makes the two rules above protective rather than empty ceremony — if the port ever "
            + "stops being a required dependency, this guard must be re-derived, not silently kept.");
    }

    /// <summary>A host that adopts the portal-session scheme: its project directory (the nearest
    /// ancestor of the adoption call holding a <c>*.csproj</c>), the file(s) making the call, and its
    /// whole comment-stripped source tree — the scope a port binding may live in.</summary>
    private sealed record AdoptingHost(
        string ProjectDirectory,
        IReadOnlyList<string> AdoptionFiles,
        string PortSources);

    private static IReadOnlyList<AdoptingHost> FindAdoptingHosts()
        => [.. SourceFilesUnder(Path.Combine(RepoRoot, "src"))
            .Where(file => StripLineComments(File.ReadAllText(file))
                .Contains(AdoptionCall, StringComparison.Ordinal))
            .GroupBy(ProjectDirectoryOf)
            .Select(group => new AdoptingHost(
                ToRepoRelative(group.Key),
                [.. group.Select(file => ToRepoRelative(group.Key, file)).OrderBy(name => name, StringComparer.Ordinal)],
                string.Join('\n', SourceFilesUnder(group.Key).Select(file => StripLineComments(File.ReadAllText(file))))))
            .OrderBy(host => host.ProjectDirectory, StringComparer.Ordinal)];

    /// <summary>Every C# source under a directory, build output excluded — a compiled artifact is not
    /// source the compiler of this change checked.</summary>
    private static IEnumerable<string> SourceFilesUnder(string directory)
        => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file));

    private static bool IsBuildOutput(string path)
    {
        var segments = path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);

        return segments.Contains("bin", StringComparer.OrdinalIgnoreCase)
            || segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The project a source file belongs to: its nearest ancestor directory holding a
    /// <c>*.csproj</c>. Throws rather than silently grouping the file nowhere.</summary>
    private static string ProjectDirectoryOf(string sourceFile)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)
            ?? throw new InvalidOperationException($"Source file has no directory: {sourceFile}"));

        while (directory is not null && directory.EnumerateFiles("*.csproj").FirstOrDefault() is null)
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"No ancestor of {sourceFile} holds a *.csproj, so the host it belongs to cannot be determined.");
    }

    private static string ToRepoRelative(string absolute)
        => Path.GetRelativePath(RepoRoot, absolute).Replace('\\', '/');

    private static string ToRepoRelative(string projectDirectory, string file)
        => Path.GetRelativePath(projectDirectory, file).Replace('\\', '/');

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

    /// <summary>Removes trailing <c>//</c> line comments before matching (the
    /// <c>AppHostEndpointPortingArchitectureTests</c> precedent): these files document themselves, and
    /// a commented-out adoption call or a doc sentence naming the claims port would otherwise satisfy
    /// (or break) a rule. No string literal ahead of the matched tokens contains <c>//</c>.</summary>
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
                "Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
