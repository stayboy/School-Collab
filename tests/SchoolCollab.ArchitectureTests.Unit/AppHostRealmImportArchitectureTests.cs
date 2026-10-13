using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        var claimNames = ClaimNamesOf(client);

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
    public void RealmImportFile_SchoolCollabClient_RegistersThePostLogoutRedirectUrisAttribute_IncludingThePinnedPortalLanding()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RealmFile));
        var client = FindClient(doc.RootElement, "school-collab-client");

        // Keycloak reads the allowlist from the client ATTRIBUTE `post.logout.redirect.uris`, with the
        // URIs `##`-separated. A top-level `postLogoutRedirectUris` array — this file's first shape —
        // is not among ClientRepresentation's known properties, so `start-dev --import-realm` aborts
        // with `Unrecognized field "postLogoutRedirectUris"`, the container exits 1, and everything
        // gated on WaitFor(keycloak) silently never starts. The negative half of this guard below is
        // therefore load-bearing: it pins the shape that actually imports.
        client.TryGetProperty("attributes", out var attributes).Should().BeTrue(
            "the post-logout allowlist must live in the client's `attributes` map — the only shape "
            + "Keycloak 26.4.7's realm importer accepts.");
        attributes.TryGetProperty("post.logout.redirect.uris", out var urisAttribute).Should().BeTrue(
            "D13 option (ii) puts `post_logout_redirect_uri` in the end-session URL the auth service builds; "
            + "Keycloak matches it against `attributes[\"post.logout.redirect.uris\"]`, so without this "
            + "entry every logout is rejected after the portal has already cleared its cookie.");
        urisAttribute.ValueKind.Should().Be(JsonValueKind.String);
        var list = (urisAttribute.GetString() ?? string.Empty)
            .Split("##", StringSplitOptions.RemoveEmptyEntries)
            .Select(u => u.Trim())
            .Where(u => u.Length > 0)
            .ToArray();

        client.TryGetProperty("postLogoutRedirectUris", out _).Should().BeFalse(
            "a top-level `postLogoutRedirectUris` array is an UNKNOWN field to Keycloak 26.4.7's "
            + "ClientRepresentation: `--import-realm` fails the WHOLE realm with `Unrecognized field "
            + "\"postLogoutRedirectUris\"`, Keycloak exits 1, and every resource gated on "
            + "WaitFor(keycloak) never starts. The allowlist belongs in `attributes` (asserted above).");

        // The four per-app /signout-callback-oidc landings — the same four literals redirectUris
        // already carries for the admin (5300/7300) and families (5400/7400) dev profiles.
        list.Should().Contain(new[]
            {
                "http://localhost:5300/signout-callback-oidc",
                "https://localhost:7300/signout-callback-oidc",
                "http://localhost:5400/signout-callback-oidc",
                "https://localhost:7400/signout-callback-oidc",
            },
            "the four Blazor hosts land on their own /signout-callback-oidc page after a Keycloak sign-out, "
            + "so each must be registered as a post-logout target.");

        // Plan-review P1-1: the portal landing literal and the AppHost's pinned portal port are the
        // same fact stated twice — the realm is a committed static file, the fan-out is an endpoint
        // expression — so the guard reads the port out of Program.cs and holds the literal to it.
        // An AddUvicornApp resource has no launchSettings.json (unlike the Blazor ports above), which
        // is why the pin exists at all; Keycloak matches post_logout_redirect_uri EXACTLY, so a
        // wildcard is not an option and a drifted literal would break logout at runtime only.
        var pinnedPortalPort = PinnedAuthPortalHostPort();
        list.Should().Contain($"http://localhost:{pinnedPortalPort}/",
            "the auth service receives this very portal landing URI as Auth__PostLogoutRedirectUri, derived "
            + "from the portal's pinned Aspire endpoint — trailing slash included.");

        // Why a PORTAL URL is legitimate here although D16 — asserted just below by
        // RealmImportFile_DeclaresNoPortalOidcClient_AndNoPortalRedirectUri — forbids a portal entry in
        // redirectUris: a post-logout URI is handed NOTHING. The browser is sent there after Keycloak has
        // already ended the session, so no authorization code, token or other code-bearing value ever
        // reaches it, while a redirect URI is exactly where the code/token-bearing response is delivered.
        // This entry therefore does not narrow D16's rationale (the portal holds no token and redeems no
        // authorization code) and the negative assertion must stay green — it scans redirectUris only.
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
        //
        // DELIBERATE, OWNER-APPROVED WIDENING (round mobile-credential / L3, owner pin 2026-10-13):
        // `school-collab-mobile-client` joins the set. A device cannot hold a client secret, so
        // the public-client credential decision (spec §2 D5) makes a THIRD, public client
        // mandatory; this guard pins the client set exactly, so the mobile client is named here
        // rather than absorbed silently. The widening removes nothing: the two negative
        // assertions below still scan EVERY client's redirectUris.
        clientIds.Should().BeEquivalentTo(
            new[] { "school-collab-client", "school-collab-mobile-client", "school-collab-auth-admin" },
            "the realm must declare exactly the app/Blazor client, the device public client introduced by D5, and the admin service-account client — D16 forbids a portal OIDC client (the portal holds no token and performs no code flow).");

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

    [TestMethod]
    public void RealmImportFile_MobileClient_IsAPublicClientWithTheAuthorizationCodeFlowOnly()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RealmFile));
        var client = FindClient(doc.RootElement, "school-collab-mobile-client");

        // D5: a device cannot hold a client secret, so the device credential is a PUBLIC client
        // performing Authorization Code + PKCE through the system browser. The confidential
        // `school-collab-client` is unusable off-server for exactly this reason.
        client.GetProperty("publicClient").ValueKind.Should().Be(JsonValueKind.True,
            "D5: the mobile credential is a public client (no client secret can be embedded on a device).");
        client.GetProperty("standardFlowEnabled").ValueKind.Should().Be(JsonValueKind.True,
            "the authorization-code flow must be enabled — that is the only flow the PKCE redirect acquires tokens with.");
        client.GetProperty("directAccessGrantsEnabled").ValueKind.Should().Be(JsonValueKind.False,
            "the device must never be able to send user credentials directly: the direct grant is disabled (contrast school-collab-client).");
        client.TryGetProperty("secret", out _).Should().BeFalse(
            "a public client carries NO secret — embedding one on the device is the vulnerability D5 exists to avoid.");
    }

    [TestMethod]
    public void RealmImportFile_MobileClient_MappersEmitExactlyTheClaimSetFactoryClaimNames()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RealmFile));
        var client = FindClient(doc.RootElement, "school-collab-mobile-client");

        // Same parity contract the school-collab-client guard pins: the five claim names ARE the
        // ClaimSetFactory contract (D9), and the gates read the raw realm spellings
        // (VisibleTenantService reads `tenant_id` verbatim). Exact-set, so a rename/droppage of a
        // mapper in the mobile client cannot silently break tenant/role gating on the device.
        var claimNames = ClaimNamesOf(client);
        claimNames.Should().BeEquivalentTo(
            new[] { "tenant_id", "tenant_name", "tenant_type", "teacher_id", "roles" },
            "school-collab-mobile-client's protocol mappers must emit EXACTLY the claim names ClaimSetFactory reads "
            + "(tenant_id/tenant_name/tenant_type/teacher_id/roles).");
    }

    [TestMethod]
    public void RealmImportFile_MobileClient_HasTheFourAttributeMappersOnBothTokenPaths()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RealmFile));
        var client = FindClient(doc.RootElement, "school-collab-mobile-client");

        // The verbatim copy of school-collab-client's attribute mappers — user.attribute == claim.name
        // per pair, on both token paths (the bearer APIs read the ACCESS token, the ID token carries
        // the same values for the client-side provider's parity).
        foreach (var claimName in new[] { "tenant_id", "tenant_name", "tenant_type", "teacher_id" })
        {
            var mapper = FindMapper(client, "oidc-usermodel-attribute-mapper", claimName);
            mapper.GetProperty("config").GetProperty("user.attribute").GetString().Should().Be(claimName,
                $"the '{claimName}' mapper must read the user attribute of the same name.");
            AssertBothTokenPathsAreEnabled(mapper, claimName);
            mapper.GetProperty("config").TryGetProperty("userinfo.token.claim", out var uinfo).Should().BeTrue();
            uinfo.ValueKind.Should().Be(JsonValueKind.True,
                $"the '{claimName}' mapper is copied verbatim from school-collab-client, so userinfo.token.claim stays true.");
        }
    }

    [TestMethod]
    public void RealmImportFile_MobileClient_HasRolesMapperOnBothTokenPaths()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RealmFile));
        var client = FindClient(doc.RootElement, "school-collab-mobile-client");

        var rolesMapper = FindMapper(client, "oidc-usermodel-realm-role-mapper", "roles");
        AssertBothTokenPathsAreEnabled(rolesMapper, "roles");
        rolesMapper.GetProperty("config").GetProperty("multivalued").ValueKind.Should().Be(JsonValueKind.True,
            "a user with several realm roles carries one `roles` claim per role (the client-side provider flattens the array).");
    }

    [TestMethod]
    public void RealmImportFile_MobileClient_AudienceMapperEmitsTheApiValidatedSchoolCollabClientAudience()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RealmFile));
        var client = FindClient(doc.RootElement, "school-collab-mobile-client");

        // The APIs' AddJwtBearer validates options.Audience = Auth:Keycloak:ClientId, whose
        // AppHost-fanned value is `school-collab-client` (Program.cs parameter). The audience the
        // mobile token must therefore CARRY is the resource audience — NOT the mobile client's own
        // clientId, which is only the OIDC authorization `client_id` in the request.
        var audienceMapper = FindMapper(client, "oidc-audience-mapper", "audience-school-collab-client");
        audienceMapper.GetProperty("config").GetProperty("included.client.audience").GetString()
            .Should().Be("school-collab-client",
                "the APIs validate aud against Auth:Keycloak:ClientId = school-collab-client; emitting any other audience makes every bearer call 401.");
        AssertBothTokenPathsAreEnabled(audienceMapper, "audience-school-collab-client");
    }

    [TestMethod]
    public void RealmImportFile_MobileClient_RegistersTheCustomSchemeRedirectUri()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RealmFile));
        var client = FindClient(doc.RootElement, "school-collab-mobile-client");

        client.TryGetProperty("redirectUris", out var uris).Should().BeTrue();
        var list = uris.EnumerateArray()
            .Where(u => u.ValueKind == JsonValueKind.String)
            .Select(u => u.GetString())
            .Where(u => u is not null)
            .Cast<string>()
            .ToArray();

        list.Should().Contain("schoolcollab://signin-oidc",
            "Keycloak matches redirect URIs EXACTLY (the ##-separator lesson below), and this literal is the "
            + "CallbackUrl the head hands to WebAuthenticator — a drifted spelling breaks sign-in at runtime only.");
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

    /// <summary>The <c>claim.name</c> values a client's protocol mappers emit — the ClaimSetFactory
    /// contract extraction shared by the school-collab-client and mobile-client guards. Mappers
    /// without a <c>claim.name</c> (the audience mapper) are not claims and are skipped.</summary>
    private static IReadOnlyList<string> ClaimNamesOf(JsonElement client)
    {
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

        return claimNames;
    }

    /// <summary>Resolves a mapper by <c>protocolMapper</c> type and (for claim mappers) its
    /// <c>claim.name</c>; the audience mapper is keyed by its mapper name instead. THROWS when the
    /// mapper is missing, so a dropped mapper cannot pass the assertions vacuously.</summary>
    private static JsonElement FindMapper(JsonElement client, string protocolMapper, string key)
    {
        if (!client.TryGetProperty("protocolMappers", out var mappers) || mappers.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Client has no 'protocolMappers' array.");
        }

        foreach (var mapper in mappers.EnumerateArray())
        {
            if (mapper.ValueKind != JsonValueKind.Object
                || !mapper.TryGetProperty("protocolMapper", out var pm)
                || pm.ValueKind != JsonValueKind.String
                || pm.GetString() != protocolMapper)
            {
                continue;
            }

            var config = mapper.GetProperty("config");
            var matchesClaim = config.TryGetProperty("claim.name", out var claimName)
                && claimName.ValueKind == JsonValueKind.String
                && claimName.GetString() == key;
            var matchesName = mapper.TryGetProperty("name", out var mapperName)
                && mapperName.ValueKind == JsonValueKind.String
                && mapperName.GetString() == key;
            if (matchesClaim || matchesName)
            {
                return mapper;
            }
        }

        throw new InvalidOperationException(
            $"Client has no '{protocolMapper}' mapper identified by '{key}'.");
    }

    private static void AssertBothTokenPathsAreEnabled(JsonElement mapper, string label)
    {
        var config = mapper.GetProperty("config");
        config.TryGetProperty("access.token.claim", out var access).Should().BeTrue(
            $"the '{label}' mapper must state its access-token participation explicitly.");
        access.ValueKind.Should().Be(JsonValueKind.True,
            $"the bearer path reads '{label}' from the access token, so access.token.claim must be true.");
        config.TryGetProperty("id.token.claim", out var id).Should().BeTrue(
            $"the '{label}' mapper must state its ID-token participation explicitly.");
        id.ValueKind.Should().Be(JsonValueKind.True,
            $"the client-side provider reads '{label}' from the ID token, so id.token.claim must be true.");
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

    /// <summary>The host port pinned on the AppHost's <c>auth-portal</c> resource
    /// (<c>.WithHttpEndpoint(port: …, name: "http")</c>), which is what makes
    /// <c>GetEndpoint("http")</c> — and therefore the fanned <c>Auth__PostLogoutRedirectUri</c> —
    /// deterministically <c>http://localhost:{port}/</c>. THROWS when the pin is absent, so the
    /// realm-vs-AppHost agreement assertion can never pass vacuously (the file's other helpers
    /// follow the same fail-loud rule).</summary>
    private static int PinnedAuthPortalHostPort()
    {
        // Line comments are stripped FIRST: the pin is located inside the slice from the resource
        // creation to that statement's terminating `;`, so a `;` occurring inside a comment between
        // the two (e.g. "(`##`-separated; a top-level …)") would end the slice early and make this
        // fail-loud helper fail for the WRONG reason — reporting "pins no host port" for a pin that
        // is present. Same rule as AppHostEndpointPortingArchitectureTests.StripLineComments.
        var program = StripLineComments(ReadAppHostFile("Program.cs"));
        var portalResource = program.IndexOf("""AddUvicornApp("auth-portal""", StringComparison.Ordinal);
        if (portalResource < 0)
        {
            throw new InvalidOperationException(
                "Program.cs declares no AddUvicornApp(\"auth-portal\", …) resource, so the portal's pinned host port cannot be read.");
        }

        // Scoped to the portal's OWN creation statement — from `AddUvicornApp("auth-portal"` to that
        // statement's terminating `;` — not merely to everything after it. Program.cs carries other
        // pinned `name: "http"` host ports (mailpit's 8025), and a resource created later on another
        // chain could otherwise be matched instead. It fails loud below when the statement ends without a
        // pin, so the pin must stay on this chain and moving it becomes a visible guard failure rather
        // than a silent mis-read.
        var statementEnd = program.IndexOf(';', portalResource);
        var pin = Regex.Match(
            statementEnd < 0 ? program[portalResource..] : program[portalResource..statementEnd],
            """\.WithHttpEndpoint\(port: (?<port>\d+)[^)]*name: "http"\)""");
        if (!pin.Success)
        {
            throw new InvalidOperationException(
                "The auth-portal resource pins no host port (no `.WithHttpEndpoint(port: …, name: \"http\")` on its chain), "
                + "so its landing URI cannot AGREE with the realm's postLogoutRedirectUris literal.");
        }

        return int.Parse(pin.Groups["port"].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>Drops <c>//</c> line comments — mirrors the strip the other Program.cs-scanning
    /// guards use, so punctuation inside a comment can never influence a source-scanning
    /// assertion.</summary>
    private static string StripLineComments(string source)
        => string.Join('\n', source.Split('\n').Select(line =>
        {
            var commentAt = line.IndexOf("//", StringComparison.Ordinal);
            return commentAt < 0 ? line : line[..commentAt];
        }));

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
