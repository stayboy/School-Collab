using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the round-<c>portal-session-adoption</c> wiring (D19/D9) that no build and no unit test
/// can see, because it lives in source the compiler accepts in several wrong shapes:
/// <list type="number">
///   <item>the Assignments API's <c>RequireAssignmentReader</c> policy names the <b>portal-session
///        gateway</b> scheme — and no longer Bearer. The gateway is registered in every flag state,
///        so a listed scheme always has a handler (a policy-named scheme without one is a 500, not a
///        401), and it routes each request to the portal session, to Bearer, or to the dev
///        TestAuth scheme. The round-<c>portal-submission-grade</c> writer policy
///        (<c>RequireAssignmentWriter</c>) is guarded the same way, together with the route it
///        holds and the general map it left;</item>
///   <item>the AppHost's <b>both</b> app-callback allowlist copies carry the teacher portal's
///        <c>/auth/callback</c> append — the auth service's (which enforces the list at code
///        issuance) and the auth portal's own copy (which re-validates the value it redirects to).
///        Without either, the teacher portal's handoff code cannot be minted for its callback;</item>
///   <item>the <c>portals</c> resource carries <c>WithReference(auth)</c> and both
///        endpoint-derived env values — <c>PORTALS_PUBLIC_BASE_URL</c> (the callback URI's origin)
///        and <c>PORTALS_LOGIN_URL</c> (the browser-facing sign-in link D19's rule 3 keys on);
///        without the reference the portal cannot resolve <c>services__auth__http__0</c> at all;</item>
///   <item>the <c>assignments-api</c> resource carries <c>WithReference(auth)</c> — without it the
///        <c>https+http://auth</c> claims-read client dies at runtime with "No such host is
///        known", which is exactly what <c>CrossModuleWiringTests</c> protects.</item>
/// </list>
/// <para>
/// The resource blocks are parsed by line from the real <c>Program.cs</c>: a block opens at its
/// <c>AddProject&lt;…&gt;("name")</c> / <c>AddUvicornApp("name", …)</c> call, absorbs the fluent
/// continuation lines that follow, and also absorbs later <c>name = name.…</c> reassignments —
/// the shape a cross-referencing pair of resources forces (the AppHostLoginUiFlagWiringArchitecture
/// Tests precedent). Line comments are stripped first: these chains document themselves, and a
/// comment naming a config key must neither satisfy nor break an assertion.
/// </para>
/// <para>
/// The walk-up follows the <c>AppHostRealmImportArchitectureTests</c> /
/// <c>PortalsSolutionItemsArchitectureTests</c> precedent and THROWS rather than passing vacuously
/// when the repository root or a named resource block cannot be found.
/// </para>
/// </summary>
[TestClass]
public class PortalSessionAdoptionArchitectureTests
{
    /// <summary>The reader policy's one listed scheme (D4).</summary>
    private const string GatewaySchemeOptIn =
        "AddAuthenticationSchemes(PortalSessionAuthenticationHandler.GatewaySchemeName)";

    private const string BearerSchemeOptIn = "AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme)";
    private static readonly string RepoRoot = FindRepoRoot();

    /// <summary>A resource-creation chain: <c>var portals = builder.AddUvicornApp("portals", …)</c>.
    /// Declared before <see cref="Blocks"/> — static initializers run in declaration order.</summary>
    private static readonly Regex CreatedResource = new(
        """^\s*(?:var\s+)?(?:(?<variable>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*)?builder\.(?:AddProject<Projects\.[A-Za-z0-9_]+>|AddUvicornApp)\("(?<resource>[^"]+)"[,)]""",
        RegexOptions.Compiled);

    /// <summary>A later <c>name = name.…</c> reassignment of an existing resource (the shape a
    /// cross-referencing pair of resources forces).</summary>
    private static readonly Regex ReassignedResource = new(
        @"^\s*(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<target>[A-Za-z_][A-Za-z0-9_]*)\.(\w)",
        RegexOptions.Compiled);

    private static readonly string Program = StripLineComments(
        File.ReadAllText(Path.Combine(RepoRoot, "src", "AppHost", "SchoolCollab.AppHost", "Program.cs")));

    private static readonly IReadOnlyList<ResourceBlock> Blocks = ParseResourceBlocks(Program);

    [TestMethod]
    public void ReaderPolicy_NamesTheGateway_AndNoLongerBearer()
    {
        var endpoints = Read("src", "Assignments", "SchoolCollab.Assignments.Api", "AssignmentEndpoints.cs");
        var policy = ExtractMethod(endpoints, "void RequireAssignmentReader(");

        policy.Should().Contain(GatewaySchemeOptIn,
            "the reader policy must list the portal-session gateway (D4): it is the ONE scheme with "
            + "a handler in every flag state, and it routes a portal session to the portal-session "
            + "scheme while bearer and dev callers keep their own.");

        policy.Should().NotContain("AuthTenancyExtensions.BearerScheme",
            "the policy must list exactly one scheme: authorization challenges every listed scheme, "
            + "and Bearer has no handler registered under FEATURE:DisableOIDCAuth — a second listed "
            + "scheme would 500 the dev posture instead of authenticating it.");

        policy.Should().Contain("RequireAuthenticatedUser()")
            .And.Contain("RequireRole(",
                "the policy stays self-sufficient for the routes it decorates (the disjunctive "
                + "teacher ∨ staff ∨ user-admin ∨ platform-admin role set is its own requirement).");

        var groupOptIns = Regex.Matches(endpoints, Regex.Escape(BearerSchemeOptIn)).Count;
        groupOptIns.Should().Be(3,
            "the three group-level Bearer opt-ins (the /assignments group and its two nested "
            + "sub-groups) are untouched — only the reader policy moved to the gateway.");
    }

    [TestMethod]
    public void WriterPolicy_NamesTheGateway_AndHoldsExactlyTheGradeRoute()
    {
        // The round-portal-submission-grade writer policy (D1) is the pair of facts no build and
        // no unit test can see: which scheme it lists, and which route it decorates. Both are
        // non-vacuous — removing the grade route from the writer sub-group, or dropping the
        // gateway scheme from the policy, reddens this test.
        //
        // NOTE on the sub-group's inherited policy: the writer sub-group ALSO inherits the
        // parent /assignments group's Bearer opt-in (as the reader sub-group does). That is
        // correct and load-bearing — the authorization middleware COMBINES an endpoint's
        // policies, so the union of {Bearer, gateway} authenticates a portal session through the
        // gateway and an existing Bearer caller through the fallback. The route-level tests
        // (AssignmentWriterPolicyRouteTests) prove that union over the real HTTP pipeline; this
        // guard pins the SOURCE shape it depends on.
        var endpoints = Read("src", "Assignments", "SchoolCollab.Assignments.Api", "AssignmentEndpoints.cs");
        var policy = ExtractMethod(endpoints, "void RequireAssignmentWriter(");

        policy.Should().Contain(GatewaySchemeOptIn,
            "the writer policy must list the portal-session gateway: it is the ONE scheme with a "
            + "handler in every flag state, and it routes a portal session to the portal-session "
            + "scheme while bearer and dev callers keep their own.");

        policy.Should().NotContain("AuthTenancyExtensions.BearerScheme",
            "the writer policy must list exactly one scheme (the reader's reason): authorization "
            + "challenges every listed scheme, and Bearer has no handler under "
            + "FEATURE:DisableOIDCAuth.");

        policy.Should().Contain("RequireAuthenticatedUser()")
            .And.Contain("RequireRole(",
                "the writer policy stays self-sufficient for the route it decorates.");

        // ONE role disjunction in the module: the writer's must be the reader's, exactly.
        var readerPolicy = ExtractMethod(endpoints, "void RequireAssignmentReader(");
        RoleNamesIn(policy).Should().Equal(RoleNamesIn(readerPolicy),
            "the writer policy restates the reader's four-role disjunction verbatim — two "
            + "disjunctions that can drift apart are exactly what the reader's P1-1 rework forbade.");

        endpoints.Should().Contain("writerGroup.RequireAuthorization(RequireAssignmentWriter)",
            "the writer sub-group is where the policy lands (never the whole /assignments group, "
            + "whose widening would re-open every create/edit/publish write to the reader roles).");

        endpoints.Should().Contain("writerGroup.MapAssignmentGradeRoutes()",
            "the writer sub-group maps the grade route — the policy and the route it decorates "
            + "must move together.");

        Regex.Matches(endpoints, Regex.Escape(BearerSchemeOptIn)).Count.Should().Be(3,
            "the writer sub-group must add NO group-level Bearer opt-in: the three the reader "
            + "guard pins stay unchanged.");

        // The route itself: mounted by the grade map, and gone from the general map.
        var routes = Read("src", "Assignments", "SchoolCollab.Assignments.Api", "Endpoints", "AssignmentRoutes.cs");
        ExtractMethod(routes, "RouteGroupBuilder MapAssignmentGradeRoutes(this RouteGroupBuilder")
            .Should().Contain("/{id:guid}/students/{studentId:guid}/submission/review",
                "the grade POST is the writer sub-group's one route.");
        ExtractMethod(routes, "RouteGroupBuilder MapAssignmentRoutes(this RouteGroupBuilder")
            .Should().NotContain("submission/review",
                "the grade POST must no longer be mounted by MapAssignmentRoutes — that is the "
                + "route move the writer policy depends on.");
    }

    /// <summary>The realm role names a policy body lists, in source order.</summary>
    private static IReadOnlyList<string> RoleNamesIn(string policyBody)
        => [.. Regex.Matches(policyBody, @"RealmRoleNames\.\w+").Select(match => match.Value)];

    [TestMethod]
    public void BothAppCallbackAllowlistCopies_CarryTheTeacherPortalCallback()
    {
        var authSource = Normalize(Block("auth").Source);
        var allowlistAt = authSource.IndexOf("Auth__AppCallbackPrefixes", StringComparison.Ordinal);
        allowlistAt.Should().BeGreaterThanOrEqualTo(0,
            "the auth service must still receive Auth:AppCallbackPrefixes — it enforces the list at "
            + "code issuance.");

        var authFanOut = authSource[allowlistAt..];
        authFanOut.Should().Contain("portals.GetEndpoint(\"http\")}/auth/callback",
            "the teacher portal's callback must be appended to the ENFORCED copy: without it the "
            + "one-time handshake code can never be minted for the portal's redemption, and every "
            + "sign-in fails closed at issuance.");
        authFanOut.Should().Contain("authPortal.GetEndpoint(\"http\")}/bootstrap",
            "the auth portal's bootstrap URI must survive — this is an append, not a replacement.");

        var authPortalSource = Normalize(Block("auth-portal").Source);
        var portalAllowlistAt = authPortalSource.IndexOf("AuthPortal__AppCallbackPrefixes", StringComparison.Ordinal);
        portalAllowlistAt.Should().BeGreaterThanOrEqualTo(0,
            "the auth portal re-validates the value it redirects a browser to against its own copy "
            + "(defense-in-depth, spec §14).");

        authPortalSource[portalAllowlistAt..].Should().Contain("portals.GetEndpoint(\"http\")}/auth/callback",
            "the second copy needs the same append: without it the passkey continuation and the "
            + "login page's return_uri are refused for the teacher portal.");
    }

    [TestMethod]
    public void PortalsResource_ReferencesAuth_AndCarriesBothSessionKeys()
    {
        var portals = Block("portals");

        portals.Source.Should().Contain("WithReference(auth)",
            "the teacher portal's session client resolves the auth service through the "
            + "AppHost-injected services__auth__http__0 discovery variable.");

        portals.Source.Should().Contain(
            "WithEnvironment(\"PORTALS_PUBLIC_BASE_URL\", portals.GetEndpoint(\"http\"))",
            "the callback URI's origin comes from the resource's own Aspire endpoint (the "
            + "AuthPortal__PublicBaseUrl pattern) — never a hardcoded port.");

        portals.Source.Should().Contain(
            "WithEnvironment(\"PORTALS_LOGIN_URL\", $\"{authPortal.GetEndpoint(\"http\")}/login\")",
            "the browser-facing sign-in link is derived from the auth portal's endpoint. It is a "
            + "DISTINCT key from Auth__Portal__LoginUrl on purpose: that key's guard pins it to the "
            + "two challenge surfaces, and this link is not one.");
    }

    [TestMethod]
    public void AssignmentsApiResource_ReferencesAuth()
    {
        Block("assignments-api").Source.Should().Contain("WithReference(auth)",
            "the portal-session claims reader is registered over the literal https+http://auth base "
            + "address (D4), so the AppHost must carry the matching reference — CrossModuleWiring "
            + "Tests demands it, and without it the hop dies at runtime.");
    }

    [TestMethod]
    public void Scan_ActuallyFoundTheAppHostResources()
    {
        // Non-vacuity: every assertion above is per-resource or per-method, so a parser that
        // silently matched nothing would pass while inspecting empty strings.
        Blocks.Should().HaveCountGreaterThanOrEqualTo(10,
            "the AppHost declares more than ten project/uvicorn resources; fewer means the parser "
            + "found no real blocks.");

        Blocks.Select(block => block.ResourceName)
            .Should().Contain(["assignments-api", "auth", "auth-portal", "portals"]);

        foreach (var name in new[] { "assignments-api", "auth", "auth-portal", "portals" })
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
    /// <c>name = name.…</c> reassignments of the same alias. A statement that spans several lines
    /// (a <c>WithEnvironment(key,</c> whose argument continues on the next line) stays one
    /// statement — tracked by the running parenthesis balance — so the whole fan-out expression is
    /// inside the block it belongs to. Any other statement closes the block.
    /// </summary>
    private static IReadOnlyList<ResourceBlock> ParseResourceBlocks(string program)
    {
        var sources = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        var aliasToResource = new Dictionary<string, string>(StringComparer.Ordinal);
        string? open = null;
        var openParentheses = 0;

        foreach (var rawLine in program.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (openParentheses > 0)
            {
                // Inside a multi-line statement: the line belongs to whatever opened it.
                openParentheses += ParenthesisBalance(line);
                if (open is not null)
                {
                    sources[open].Append('\n').Append(line);
                }

                continue;
            }

            var created = CreatedResource.Match(line);
            if (created.Success)
            {
                var resource = created.Groups["resource"].Value;
                if (created.Groups["variable"].Success)
                {
                    aliasToResource[created.Groups["variable"].Value] = resource;
                }

                sources[resource] = new StringBuilder(line);
                open = resource;
                openParentheses = ParenthesisBalance(line);
                continue;
            }

            var reassigned = ReassignedResource.Match(line);
            if (reassigned.Success)
            {
                var alias = reassigned.Groups["alias"].Value;
                if (alias == reassigned.Groups["target"].Value
                    && aliasToResource.TryGetValue(alias, out var resource))
                {
                    sources[resource].Append('\n').Append(line);
                    open = resource;
                    openParentheses = ParenthesisBalance(line);
                }
                else
                {
                    open = null;
                }

                continue;
            }

            // A continuation line is indented and starts with a fluent dot.
            if (line.TrimStart().StartsWith('.'))
            {
                if (open is not null)
                {
                    sources[open].Append('\n').Append(line);
                }

                openParentheses = ParenthesisBalance(line);
                continue;
            }

            open = null;
        }

        return [.. sources.Select(entry => new ResourceBlock(entry.Key, entry.Value.ToString()))];
    }

    /// <summary>Whether a line leaves a parenthesis open (a fluent statement continuing below).</summary>
    private static int ParenthesisBalance(string line)
        => line.Count(character => character == '(') - line.Count(character => character == ')');

    private static ResourceBlock Block(string resourceName)
        => Blocks.SingleOrDefault(block => block.ResourceName == resourceName)
            ?? throw new InvalidOperationException(
                $"The AppHost Program.cs has no resource block named '{resourceName}'. Known blocks: "
                + string.Join(", ", Blocks.Select(block => block.ResourceName).OrderBy(name => name, StringComparer.Ordinal)));

    /// <summary>One C# method body, from its signature line to its closing brace at member indent.</summary>
    private static string ExtractMethod(string source, string signature)
    {
        var lines = source.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var start = Array.FindIndex(lines, line => line.Contains(signature, StringComparison.Ordinal));

        start.Should().BeGreaterThanOrEqualTo(0,
            $"the method '{signature}' must exist — an absent policy would make this guard vacuous.");

        // The method's closing brace is the first line that closes a member (four spaces + `}`):
        // the body's own braces are indented further.
        var end = Array.FindIndex(lines, start + 1, line => line == "    }");
        end.Should().BeGreaterThan(start, $"the body of '{signature}' must terminate at member indent.");

        return string.Join('\n', lines[start..(end + 1)]);
    }

    /// <summary>Whitespace-insensitive form — lets a multi-line fluent expression be pinned exactly.</summary>
    private static string Normalize(string source) => Regex.Replace(source, @"\s+", string.Empty);

    private static string Read(params string[] relative)
    {
        var path = Path.Combine([RepoRoot, .. relative]);
        return File.Exists(path)
            ? File.ReadAllText(path)
            : throw new FileNotFoundException($"Expected source file not found under the repo: {string.Join('/', relative)}");
    }

    /// <summary>
    /// Removes trailing <c>//</c> comments before parsing: these chains document themselves, and a
    /// comment naming a config key would otherwise satisfy (or break) a substring assertion. No
    /// string literal in the AppHost file contains <c>//</c> (URLs are built from endpoint
    /// expressions).
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
                "Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
