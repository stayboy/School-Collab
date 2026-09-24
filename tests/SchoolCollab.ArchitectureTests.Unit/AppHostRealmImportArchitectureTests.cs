using System.Text.Json;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the Keycloak dev-IdP realm import artifact (ar-21): the realm file
/// must be strict JSON (the parser rejects comments — no substring scan), named
/// <c>&lt;realm&gt;-realm.json</c> where <c>realm</c> is read from the file's
/// OWN <c>realm</c> property (not a hardcoded literal, so a renamed realm
/// cannot pass vacuously), and every filename-agreement site must agree with
/// that derived name — the AppHost <c>keycloakRealmPath</c>, the csproj copy
/// item, and the container bind-mount target (which must also live under
/// <c>/opt/keycloak/data/import/</c>). A stale site otherwise surfaces only at
/// the manual AC#4 keycloak run. The discovery helper follows the
/// <c>SeedCsvArchitectureTests</c> precedent (walk-up from
/// <c>AppContext.BaseDirectory</c>) and THROWS on zero or multiple realm
/// candidates so a renamed file cannot pass by silently finding nothing.
/// </summary>
[TestClass]
public class AppHostRealmImportArchitectureTests
{
    private static readonly string RealmFile = FindRealmFile();

    [TestMethod]
    public void RealmImportFile_IsStrictJson_NoComments()
    {
        var raw = File.ReadAllText(RealmFile);

        // Parse with comments DISALLOWED — the same rule Keycloak applies
        // (ALLOW_COMMENTS is not enabled). A `//`-substring scan was removed in
        // review: it false-fails on any legitimate URL value while not actually
        // proving the file parses.
        Action strictParse = () => JsonDocument.Parse(raw, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Disallow,
        }).Dispose();

        strictParse.Should().NotThrow(
            "the realm import file must be strict JSON — Keycloak rejects non-standard comments, so the dev-IdP import must not depend on them (ar-21).");
    }

    [TestMethod]
    public void RealmImportFile_FilenameIsDerivedFromItsOwnRealmProperty()
    {
        var realm = ReadRealmProperty(RealmFile);
        var expected = $"{realm}-realm.json";
        Path.GetFileName(RealmFile).Should().Be(expected,
            "Keycloak requires realm import files to be named <realm>-realm.json; the name must derive from the file's own `realm` property, not a hardcoded literal.");
    }

    [TestMethod]
    public void RealmImportFile_KeycloakRealmPathVariableAgreesWithDerivedName()
    {
        var derived = DerivedRealmFilename();
        var program = ReadAppHostFile("Program.cs");
        program.Should().Contain($"keycloakRealmPath = Path.Combine(AppContext.BaseDirectory, \"{derived}\")",
            "the keycloakRealmPath variable must point at the derived realm filename.");
    }

    [TestMethod]
    public void RealmImportFile_CsprojCopyItemAgreesWithDerivedName()
    {
        var derived = DerivedRealmFilename();
        var csproj = ReadAppHostFile("SchoolCollab.AppHost.csproj");
        csproj.Should().Contain($"<None Include=\"{derived}\"",
            "the csproj copy item must ship the derived realm filename.");
    }

    [TestMethod]
    public void RealmImportFile_BindMountTargetAgreesWithDerivedName_UnderImportDir()
    {
        var derived = DerivedRealmFilename();
        var target = $"/opt/keycloak/data/import/{derived}";
        var program = ReadAppHostFile("Program.cs");
        // The import-dir prefix is carried by `target` itself, so THIS Contain
        // assertion is what enforces it. The extra StartWith probe on a
        // locally-built literal was a tautology and was removed in review.
        program.Should().Contain($"WithBindMount(keycloakRealmPath, \"{target}\")",
            "the bind-mount target must point at the derived realm filename under /opt/keycloak/data/import/ (the only dir --import-realm scans).");
    }

    [TestMethod]
    public void RealmImportFile_DeclaresTheTwoRealmRoles()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RealmFile));
        doc.RootElement.TryGetProperty("roles", out var roles).Should().BeTrue(
            "the realm import must declare a top-level `roles` object (spec §11.2).");
        roles.TryGetProperty("realm", out var realmRoles).Should().BeTrue(
            "realm roles must live under `roles.realm`.");

        var names = realmRoles.EnumerateArray()
            .Where(r => r.ValueKind == JsonValueKind.Object && r.TryGetProperty("name", out _))
            .Select(r => r.GetProperty("name").GetString())
            .ToArray();

        names.Should().Contain(new[] { "user-admin", "platform-admin" },
            "the realm must declare the two realm roles the auth service gates on (spec §11.2).");
    }

    [TestMethod]
    public void RealmImportFile_DeclaresTheServiceAccountClient()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RealmFile));
        var realmRoot = doc.RootElement;
        var client = FindClient(realmRoot, "school-collab-auth-admin");

        client.TryGetProperty("serviceAccountsEnabled", out var sa).Should().BeTrue(
            "the admin service-account client must enable service accounts.");
        sa.ValueKind.Should().Be(JsonValueKind.True,
            "serviceAccountsEnabled must be true for the client_credentials grant (spec §11.3).");
        client.TryGetProperty("secret", out var secret).Should().BeTrue();
        secret.ValueKind.Should().Be(JsonValueKind.String, "the service-account client must carry a secret.");

        // The realm-management grant lives as clientRoles on the service-account USER entry
        // (username `service-account-<clientId>`), not on the client representation:
        // ClientRepresentation has no clientRoles property, and Keycloak's realm import silently
        // drops unknown properties — a client-side block would leave the first Admin REST call
        // with 403 (diff-review P1, round keycloak-ui-auth).
        var saUser = FindServiceAccountUser(realmRoot, "school-collab-auth-admin");
        saUser.TryGetProperty("clientRoles", out var clientRoles).Should().BeTrue(
            "the service-account user entry must carry the realm-management grant (diff-review P1).");
        clientRoles.TryGetProperty("realm-management", out var rm).Should().BeTrue(
            "the grant must target the realm-management client roles.");
        rm.ValueKind.Should().Be(JsonValueKind.Array, "realm-management must be an array of role names.");
        var granted = rm.EnumerateArray()
            .Where(r => r.ValueKind == JsonValueKind.String)
            .Select(r => r.GetString())
            .Where(r => r is not null)
            .Cast<string>()
            .ToArray();
        granted.Should().BeEquivalentTo(new[] { "view-users", "manage-users", "view-realm", "manage-realm" },
            "the service account must be granted EXACTLY the least-privilege realm-management quartet (spec §16.2) — an exact-set assertion so silent privilege creep or a dropped role cannot pass the guard.");
    }

    [TestMethod]
    public void RealmImportFile_SchoolCollabClient_HasRolesMapperOnBothTokenPaths()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RealmFile));
        var client = FindClient(doc.RootElement, "school-collab-client");

        client.TryGetProperty("protocolMappers", out var mappers).Should().BeTrue();
        var rolesMapper = mappers.EnumerateArray().FirstOrDefault(m =>
            m.ValueKind == JsonValueKind.Object
            && m.TryGetProperty("protocolMapper", out var pm)
            && pm.ValueKind == JsonValueKind.String
            && pm.GetString() == "oidc-usermodel-realm-role-mapper");

        rolesMapper.ValueKind.Should().Be(JsonValueKind.Object,
            "school-collab-client must carry a realm-role mapper emitting the `roles` claim.");
        rolesMapper.GetProperty("config").TryGetProperty("access.token.claim", out var atc).Should().BeTrue();
        atc.ValueKind.Should().Be(JsonValueKind.True,
            "the bearer path reads `roles` from the access token, so access.token.claim must be true.");
        rolesMapper.GetProperty("config").TryGetProperty("id.token.claim", out var itc).Should().BeTrue();
        itc.ValueKind.Should().Be(JsonValueKind.True,
            "the cookie path reads `roles` from the ID token, so id.token.claim must be true.");
    }

    [TestMethod]
    public void RealmImportFile_SchoolCollabClient_MappersEmitExactlyTheClaimSetFactoryClaimNames()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RealmFile));
        var client = FindClient(doc.RootElement, "school-collab-client");

        client.TryGetProperty("protocolMappers", out var mappers).Should().BeTrue(
            "the guard must not pass vacuously over a missing protocolMappers block.");
        var claimNames = new List<string>();
        foreach (var mapper in mappers.EnumerateArray())
        {
            if (mapper.ValueKind != JsonValueKind.Object
                || !mapper.TryGetProperty("config", out var config)
                || config.ValueKind != JsonValueKind.Object
                || !config.TryGetProperty("claim.name", out var name)
                || name.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            claimNames.Add(name.GetString()!);
        }

        // The five claim names ARE the ClaimSetFactory contract (D9) — the factory lives in the
        // auth service and cannot be referenced from this project, so the values are pinned here.
        // Exact-set assertion: renaming a mapper's claim.name (a silent tenant-binding break),
        // dropping one, or adding a new claim mapper without updating this guard ALL fail.
        claimNames.Should().BeEquivalentTo(
            new[] { "tenant_id", "tenant_name", "tenant_type", "teacher_id", "roles" },
            "school-collab-client's protocol mappers must emit EXACTLY the claim names "
            + "ClaimSetFactory reads (tenant_id/tenant_name/tenant_type/teacher_id/roles): "
            + "a mapper rename in school-collab-realm.json must fail this guard.");
    }

    [TestMethod]
    public void RealmImportFile_SchoolCollabClient_HasRedirectUrisAndWebOrigins()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RealmFile));
        var client = FindClient(doc.RootElement, "school-collab-client");

        client.TryGetProperty("redirectUris", out var uris).Should().BeTrue();
        uris.ValueKind.Should().Be(JsonValueKind.Array);
        var list = uris.EnumerateArray()
            .Where(u => u.ValueKind == JsonValueKind.String)
            .Select(u => u.GetString())
            .Where(u => u is not null)
            .Cast<string>()
            .ToArray();
        list.Should().HaveCountGreaterThan(0, "the OIDC code flow cannot complete without registered redirect URIs.");
        list.Should().Contain(u => u.Contains("localhost:5300") && u.Contains("/signin-oidc"));
        list.Should().Contain(u => u.Contains("localhost:5400") && u.Contains("/signin-oidc"));

        client.TryGetProperty("webOrigins", out var origins).Should().BeTrue();
        origins.ValueKind.Should().Be(JsonValueKind.Array);
        origins.GetArrayLength().Should().BeGreaterThan(0);
    }

    [TestMethod]
    public void RealmImportFile_SchoolCollabClient_RegistersTheAuthServiceRelyingPartyRedirectUris_BothDevSchemes()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RealmFile));
        var client = FindClient(doc.RootElement, "school-collab-client");

        client.TryGetProperty("redirectUris", out var uris).Should().BeTrue();
        var list = uris.EnumerateArray()
            .Where(u => u.ValueKind == JsonValueKind.String)
            .Select(u => u.GetString())
            .Where(u => u is not null)
            .Cast<string>()
            .ToArray();

        // Round B pass B6 (D16): the passkey path makes the AUTH SERVICE the OIDC relying party,
        // so its own launch-profile URLs must be registered or the authorization response has
        // nowhere legal to return to. Both dev schemes are registered, exactly as round A does
        // per host (the dev HTTPS profile plus the HTTP one, `Properties/launchSettings.json`:
        // `https://localhost:55458;http://localhost:55459`) — a single-scheme registration would
        // let the ceremony break silently depending on which profile is active.
        list.Should().Contain("https://localhost:55458/signin-oidc",
            "the auth service is the D16 passkey relying party and must have its HTTPS dev /signin-oidc callback registered.");
        list.Should().Contain("http://localhost:55459/signin-oidc",
            "the auth service's HTTP dev launch profile must also be able to complete the ceremony.");
    }

    [TestMethod]
    public void RealmImportFile_DeclaresNoPortalOidcClient_AndNoPortalRedirectUri()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RealmFile));
        var realmRoot = doc.RootElement;
        realmRoot.TryGetProperty("clients", out var clients).Should().BeTrue();
        clients.ValueKind.Should().Be(JsonValueKind.Array);

        var clientIds = clients.EnumerateArray()
            .Where(c => c.ValueKind == JsonValueKind.Object && c.TryGetProperty("clientId", out _))
            .Select(c => c.GetProperty("clientId").GetString())
            .Where(id => id is not null)
            .Cast<string>()
            .ToArray();

        // D16's NEGATIVE invariant: the portal is NOT an OIDC client. It performs no code flow —
        // its own login is the custom form + /auth/exchange, and its passkey login is the
        // bootstrap handoff with the auth service as the relying party — so a `clients` entry
        // for it (which would come with a redirect URI the portal could be redirected to) is
        // exactly the design D16 removed. Asserting the EXACT client-id set makes the guard
        // discriminating: any added client fails, whatever it is named (a name-based check would
        // pass for a portal client spelled differently).
        clientIds.Should().BeEquivalentTo(
            new[] { "school-collab-client", "school-collab-auth-admin" },
            "the realm must declare exactly the app/Blazor client and the admin service-account client — D16 forbids a portal OIDC client (the portal holds no token and performs no code flow).");

        var redirectUris = clients.EnumerateArray()
            .Where(c => c.ValueKind == JsonValueKind.Object
                && c.TryGetProperty("redirectUris", out var uris)
                && uris.ValueKind == JsonValueKind.Array)
            .SelectMany(c => c.GetProperty("redirectUris").EnumerateArray())
            .Where(u => u.ValueKind == JsonValueKind.String)
            .Select(u => u.GetString()!)
            .ToArray();

        redirectUris.Should().NotBeEmpty(
            "the guard must not pass vacuously when no client carries a redirectUris array.");
        redirectUris.Should().NotContain(u => u.Contains("portal", StringComparison.OrdinalIgnoreCase),
            "no portal URL may be registered as a redirect URI anywhere in the realm (D16).");
        redirectUris.Should().NotContain(u => u.EndsWith("/bootstrap", StringComparison.OrdinalIgnoreCase),
            "the portal's bootstrap redemption route is a code-receiving URI but it is NOT an OIDC redirect URI: on the passkey path the code reaches the portal as a one-time bootstrap code, never as an authorization code (D16/AC12).");
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>Resolves the single realm candidate anywhere in the AppHost dir
    /// (any <c>*-realm.json</c> file). THROWS on zero or multiple candidates so
    /// a renamed realm cannot pass by silently finding nothing.</summary>
    private static string FindRealmFile()
    {
        var appHostDir = FindAppHostDir();
        var candidates = Directory.EnumerateFiles(appHostDir, "*-realm.json")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"No keycloak realm candidate found under {appHostDir} (expected exactly one *-realm.json).");
        }

        if (candidates.Count > 1)
        {
            throw new InvalidOperationException(
                $"Multiple keycloak realm candidates found under {appHostDir}: {string.Join(", ", candidates.Select(Path.GetFileName))}");
        }

        return candidates[0];
    }

    private static string DerivedRealmFilename()
        => $"{ReadRealmProperty(RealmFile)}-realm.json";

    private static string ReadRealmProperty(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("realm", out var realm) || realm.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Realm file {path} has no string 'realm' property.");
        }

        return realm.GetString()!;
    }

    /// <summary>Resolves the named client from an already-parsed realm document root. THROWS when the
    /// client is missing, so a renamed/removed client cannot pass the assertions vacuously. The caller
    /// owns the <see cref="JsonDocument"/> lifetime (a returned <see cref="JsonElement"/> is invalid
    /// after its document is disposed).</summary>
    private static JsonElement FindClient(JsonElement realmRoot, string clientId)
    {
        if (!realmRoot.TryGetProperty("clients", out var clients) || clients.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Realm file has no 'clients' array.");
        }

        foreach (var client in clients.EnumerateArray())
        {
            if (client.ValueKind == JsonValueKind.Object
                && client.TryGetProperty("clientId", out var id)
                && id.ValueKind == JsonValueKind.String
                && id.GetString() == clientId)
            {
                return client;
            }
        }

        throw new InvalidOperationException($"Realm file has no client with clientId '{clientId}'.");
    }

    /// <summary>Resolves the service-account USER entry for the named client (username convention
    /// `service-account-{clientId}`, identified by its `serviceAccountClientId` property — the shape
    /// an admin-console realm export produces for a client with a role grant). THROWS when missing,
    /// so a dropped grant entry cannot pass the assertions vacuously.</summary>
    private static JsonElement FindServiceAccountUser(JsonElement realmRoot, string clientId)
    {
        if (!realmRoot.TryGetProperty("users", out var users) || users.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Realm file has no 'users' array.");
        }

        foreach (var user in users.EnumerateArray())
        {
            if (user.ValueKind == JsonValueKind.Object
                && user.TryGetProperty("serviceAccountClientId", out var sac)
                && sac.ValueKind == JsonValueKind.String
                && sac.GetString() == clientId)
            {
                return user;
            }
        }

        throw new InvalidOperationException($"Realm file has no service-account user with serviceAccountClientId '{clientId}'.");
    }

    private static string ReadAppHostFile(string name)
        => File.ReadAllText(Path.Combine(FindAppHostDir(), name));

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
                "Could not locate src/AppHost/SchoolCollab.AppHost from " + AppContext.BaseDirectory);
        }

        return Path.Combine(dir.FullName, "src", "AppHost", "SchoolCollab.AppHost");
    }
}
