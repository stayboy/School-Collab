using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the AppHost's endpoint-porting rule — the defect a dev hit as a hard startup crash:
/// <c>The endpoint 'http' for resource 'auth-portal' ... Non-container resources cannot be proxied
/// when both TargetPort and Port are specified with the same value.</c>, thrown from
/// <c>DistributedApplication.Run()</c> — i.e. <b>after</b> <c>dotnet build</c> and every unit test
/// were already green, which is precisely why it needs a source-level guard.
/// <para>
/// Every assertion parses the real <c>Program.cs</c> with <b>line comments stripped first</b>, so
/// the comment prose that discusses <c>targetPort</c> and <c>AddUvicornApp</c> (the AppHost
/// documents its chains inline) can neither satisfy nor break an assertion — the same reasoning as
/// <c>StripLineComments</c> in <c>AppHostLoginUiFlagWiringArchitectureTests</c>.
/// </para>
/// <list type="number">
///   <item>an equal <c>port</c>/<c>targetPort</c> pin is legal only on a <b>container</b> resource
///       (never on an executable/project, and never when the call opts out of proxying with
///       <c>isProxied: false</c>);</item>
///   <item>the <c>auth-portal</c> — an <c>AddUvicornApp</c>, i.e. an executable — pins the
///       <b>host</b> port alone: <c>port: 5700</c>, with no <c>targetPort</c> argument;</item>
///   <item>a whole-file inventory of endpoint declarations: adding one must be a deliberate act
///       (the exact-count tripwire precedent from <c>AppHostLoginUiFlagWiringArchitectureTests</c>).</item>
/// </list>
/// <para>
/// The walk-up helper follows the <c>AppHostRealmImportArchitectureTests</c> precedent and THROWS
/// rather than passing vacuously when the AppHost cannot be located.
/// </para>
/// </summary>
[TestClass]
public class AppHostEndpointPortingArchitectureTests
{
    /// <summary>Every endpoint declaration the AppHost may declare. Bumping this is a deliberate act.</summary>
    private const int ExpectedEndpointDeclarationCount = 5;

    private const int PinnedPortalHostPort = 5700;

    /// <summary>Resource kinds Aspire runs as containers — equal port/targetPort pins are legal here.</summary>
    private static readonly string[] ContainerKinds =
        ["AddContainer", "AddMailpit", "AddKeycloak", "AddRedis", "AddPostgres", "AddRabbitMQ"];

    /// <summary>Resource kinds Aspire starts as a local executable — never a same-valued pin.</summary>
    private static readonly string[] NonContainerKinds =
        ["AddProject", "AddUvicornApp", "AddExecutable", "AddNpmApp", "AddJavaScriptApp", "AddPythonApp"];

    /// <summary>Token count, matched over the WHOLE file so a multi-line call cannot hide.</summary>
    private static readonly Regex EndpointToken = new(@"\bWith(?:Https?)?Endpoint\s*\(", RegexOptions.Compiled);

    /// <summary>Arguments of a single-line call — parsed per line so the parse count can disagree
    /// with the token count above (which is how an unparseable call fails loudly).</summary>
    private static readonly Regex EndpointArgs = new(@"\bWith(?:Https?)?Endpoint\s*\((?<args>[^)]*)\)", RegexOptions.Compiled);

    private static readonly Regex PortArg = new(@"(?<![A-Za-z])port:\s*(?<v>\d+)", RegexOptions.Compiled);

    private static readonly Regex TargetPortArg = new(@"(?<![A-Za-z])targetPort:\s*(?<v>\d+)", RegexOptions.Compiled);

    /// <summary>The digit-shaped form only: a comment saying “no <c>targetPort</c>” must not trip it
    /// (comments are stripped anyway — this is the belt to that braces).</summary>
    private static readonly Regex TargetPortArgument = new(@"\btargetPort:\s*\d+", RegexOptions.Compiled);

    private static readonly Regex IsProxiedFalse = new(@"\bisProxied:\s*false\b", RegexOptions.Compiled);

    private static readonly Regex ResourceDeclaration = new(
        """builder\.(?<kind>Add[A-Za-z0-9_]+)(?:<[^>]+>)?\s*\(\s*"(?<resource>[^"]+)""",
        RegexOptions.Compiled);

    [TestMethod]
    public void EndpointInventory_MatchesTheExpectedDeclarations()
    {
        var program = StrippedProgram();
        var tokens = EndpointToken.Matches(program).Count;
        var sites = ParseEndpoints(program);

        sites.Count.Should().Be(tokens,
            "every endpoint declaration must parse — a mismatch means a call spans lines (or nested "
            + "parens) and the parser silently skipped it, so the rules below would no longer see it.");

        sites.Select(site => site.Resource).Should().BeEquivalentTo(
            ["mailpit", "mailpit", "keycloak", "keycloak", "auth-portal"],
            "each endpoint must still be attributed to the resource that owns it — a changed set "
            + "means a declaration moved or was added, and the equal-port rule below must be "
            + "re-judged against the new owner.");

        sites.Count.Should().Be(ExpectedEndpointDeclarationCount,
            "the AppHost declares exactly this many endpoints; adding or removing one must update "
            + "this tripwire deliberately (and re-check the equal-port rule for it).");
    }

    [TestMethod]
    public void EqualPortAndTargetPort_Pins_AreContainerOnly()
    {
        var sites = ParseEndpoints(StrippedProgram());

        foreach (var site in sites)
        {
            if (site.Port is null || site.TargetPort is null || site.Port != site.TargetPort)
            {
                continue;
            }

            if (site.IsProxiedFalse)
            {
                continue;
            }

            if (ContainerKinds.Contains(site.Kind))
            {
                continue;
            }

            NonContainerKinds.Should().Contain(site.Kind,
                $"L{site.Line}: resource '{site.Resource}' is declared with an unrecognised kind "
                + $"'{site.Kind}' — classify it (container or not) before this rule can judge it. "
                + $"Failing loudly rather than guessing.");

            ContainerKinds.Should().Contain(site.Kind,
                $"L{site.Line}: {site.Kind} '{site.Resource}' pins port:{site.Port} == "
                + $"targetPort:{site.TargetPort}. Aspire throws at Run(): “Non-container resources "
                + "cannot be proxied when both TargetPort and Port are specified with the same value” "
                + "— pin the HOST port only and let Aspire allocate the target port. (Equal pins on a "
                + "container are legal — Mailpit's are the precedent; executables are not.)");
        }
    }

    [TestMethod]
    public void AuthPortal_PinsTheHostPortOnly()
    {
        var program = StrippedProgram();

        var creation = program.IndexOf("""AddUvicornApp("auth-portal""", StringComparison.Ordinal);
        creation.Should().BeGreaterThanOrEqualTo(0,
            "Program.cs must declare the auth-portal resource — its host pin is what makes the "
            + "realm's committed postLogoutRedirectUris literal and the fanned "
            + "Auth__PostLogoutRedirectUri agree.");

        var statementEnd = program.IndexOf(';', creation);
        statementEnd.Should().BeGreaterThan(creation,
            "the auth-portal creation statement must terminate with ';' — without it the pin cannot "
            + "be scoped, and this assertion would otherwise read the rest of the file.");

        var statement = program[creation..statementEnd];

        statement.Should().Contain($"WithHttpEndpoint(port: {PinnedPortalHostPort}",
            $"the auth-portal must pin its HOST port to {PinnedPortalHostPort} so "
            + "GetEndpoint(\"http\") resolves to http://localhost:5700 and matches the realm literal.");

        foreach (Match call in EndpointArgs.Matches(statement))
        {
            TargetPortArgument.IsMatch(call.Value).Should().BeFalse(
                $"the auth-portal must pin the host port only, but its call carries a targetPort "
                + $"argument: {call.Value.Trim()} — equal port/targetPort on this non-container "
                + "resource is rejected at Run(); a distinct targetPort is pointless because Aspire "
                + "allocates one.");
        }
    }

    private sealed record EndpointSite(int Line, string Kind, string Resource, int? Port, int? TargetPort, bool IsProxiedFalse);

    /// <summary>Comment-stripped <c>Program.cs</c>, parsed into the endpoint sites it declares.</summary>
    private static List<EndpointSite> ParseEndpoints(string program)
    {
        var sites = new List<EndpointSite>();
        var kind = string.Empty;
        var resource = string.Empty;
        var lines = program.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var declaration = ResourceDeclaration.Match(lines[i]);
            if (declaration.Success)
            {
                kind = declaration.Groups["kind"].Value;
                resource = declaration.Groups["resource"].Value;
            }

            foreach (Match call in EndpointArgs.Matches(lines[i]))
            {
                sites.Add(new(
                    Line: i + 1,
                    Kind: kind,
                    Resource: resource,
                    Port: ParseInt(call.Value, PortArg),
                    TargetPort: ParseInt(call.Value, TargetPortArg),
                    IsProxiedFalse: IsProxiedFalse.IsMatch(call.Value)));
            }
        }

        return sites;

        static int? ParseInt(string args, Regex pattern)
        {
            var match = pattern.Match(args);
            return match.Success && int.TryParse(match.Groups["v"].Value, out var value) ? value : null;
        }
    }

    private static string StrippedProgram()
        => StripLineComments(File.ReadAllText(AppHostProgramPath));

    private static readonly string AppHostProgramPath =
        Path.Combine(FindAppHostDir(), "Program.cs");

    /// <summary>Removes trailing <c>//</c> comments before parsing (the
    /// <c>AppHostLoginUiFlagWiringArchitectureTests</c> precedent): the AppHost documents its chains
    /// with comments between continuation lines, and comment prose naming <c>targetPort</c> would
    /// otherwise satisfy (or break) an argument assertion. No string literal in the file contains
    /// <c>//</c>.</summary>
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

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"Could not locate src/AppHost/SchoolCollab.AppHost from {AppContext.BaseDirectory}");
        }

        return Path.Combine(dir.FullName, "src", "AppHost", "SchoolCollab.AppHost");
    }
}
