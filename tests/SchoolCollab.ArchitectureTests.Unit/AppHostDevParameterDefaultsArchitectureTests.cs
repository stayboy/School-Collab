using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the AppHost's committed DEV-ONLY parameter defaults (keycloak-dev-defaults round).
/// Five DEV-ONLY parameters — <c>keycloak-admin-password</c>, <c>keycloak-client-secret</c>,
/// <c>keycloak-auth-admin-secret</c>, <c>smtp-user</c> and <c>smtp-password</c> — had no value in
/// any Development config source, so Aspire stopped and prompted on a plain <c>aspire run</c>. Their
/// defaults now live in <c>appsettings.Development.json</c>, which is NOT loaded outside
/// Development, so the "no committed production secret" posture is preserved. That posture is
/// asserted here by requiring the same keys to be ABSENT from the non-Development
/// <c>appsettings.json</c> — i.e. they are DEV-ONLY, never merely defaulted.
/// <para>
/// The client-secret default must equal the realm import file's <c>school-collab-client</c>
/// secret: a mismatch is not caught by any build or unit test, and surfaces only when
/// Keycloak rejects <c>Auth:Keycloak:ClientSecret</c> at a manual sign-in.
/// </para>
/// <para>
/// Discovery THROWS rather than passing vacuously when the AppHost directory or either JSON
/// file is missing (the <c>AppHostRealmImportArchitectureTests</c> precedent).
/// </para>
/// <para>
/// A committed dev default is not evidence that the parameter EXISTS: one round shipped a dev
/// default and documentation for <c>keycloak-auth-admin-secret</c> while the <c>AddParameter(...)</c>
/// declaration was never written, and the JSON-only guards stayed green.
/// <c>EveryDevParameterDefault_IsDeclaredAsAnAppHostParameter</c> ties each dev default back to a
/// real <c>AddParameter("&lt;key&gt;")</c> call in the AppHost source.
/// </para>
/// <para>
/// The <c>dev-teacher-id</c> pair (round <c>dev-teacher-identity-wiring</c>) is policed against the
/// identity's real source of truth, exactly as the two secret defaults above are policed against
/// the realm import: the committed dev value must equal <c>DevIdentitySeeder.DevTeacherId</c>, and
/// the committed base value must stay EMPTY. The AppHost must not reference
/// <c>MigrationService</c> (it is the orchestration project), so the seeder's value is scanned
/// from its source — the <see cref="DevIdentitySeederStaffNumberArchitectureTests"/> precedent
/// and spec §5's "hermetic source scans".
/// </para>
/// </summary>
[TestClass]
public class AppHostDevParameterDefaultsArchitectureTests
{
    private const string KeycloakAdminPasswordKey = "keycloak-admin-password";
    private const string KeycloakClientSecretKey = "keycloak-client-secret";
    private const string KeycloakAuthAdminSecretKey = "keycloak-auth-admin-secret";
    private const string SmtpUserKey = "smtp-user";
    private const string SmtpPasswordKey = "smtp-password";
    private const string DevTeacherIdKey = "dev-teacher-id";

    /// <summary>The dev bypass's teacher id, declared in the seeder as a fixed well-known Guid.</summary>
    private static readonly Regex SeededDevTeacherId = new(
        """public\s+static\s+readonly\s+Guid\s+DevTeacherId\s*=\s*Guid\.Parse\("(?<id>[0-9A-Fa-f-]{36})"\)""",
        RegexOptions.Compiled);

    private static readonly string DevSettingsPath = FindAppHostFile("appsettings.Development.json");
    private static readonly string SettingsPath = FindAppHostFile("appsettings.json");
    private static readonly string RealmPath = FindRealmFile();
    private static readonly string DevIdentitySeederPath = FindRepoFile(
        "src", "SchoolCollab.MigrationService", "Seeding", "DevIdentitySeeder.cs");

    [TestMethod]
    public void DevClientSecret_MatchesRealmFileClientSecret_ForSchoolCollabClient()
    {
        var realmSecret = ReadRealmClientSecret(RealmPath, "school-collab-client");
        var devSecret = ReadParameters(DevSettingsPath)[KeycloakClientSecretKey];

        devSecret.Should().Be(realmSecret,
            "the committed dev `keycloak-client-secret` must equal the realm import file's "
            + "`school-collab-client` secret — a mismatch makes Keycloak reject "
            + "Auth:Keycloak:ClientSecret, and it surfaces only at a manual sign-in.");
    }

    [TestMethod]
    public void SecretParameters_HaveNonEmptyDevDefaults()
    {
        var parameters = ReadParameters(DevSettingsPath);

        foreach (var key in PromptingSecretParameterKeys())
        {
            parameters.Should().ContainKey(key,
                $"`{key}` has no other Development value source, so Aspire prompts for it "
                + "unless appsettings.Development.json supplies a dev default.");
            parameters[key].Should().NotBeNullOrWhiteSpace(
                $"the dev default for `{key}` must be a non-empty literal.");
        }
    }

    [TestMethod]
    public void PromptingSecretParameters_AreDevOnly_NotInNonDevelopmentAppSettings()
    {
        var devParameters = ReadParameters(DevSettingsPath);
        var nonDevParameters = ReadParameters(SettingsPath);

        foreach (var key in PromptingSecretParameterKeys())
        {
            // The dev-only INVARIANT is the pairing: supplied by the Development file and
            // never by the non-Development one. Asserting only the absence half would hold
            // even when the default is missing entirely, so it could not fail against a
            // tree without the dev defaults.
            devParameters.Should().ContainKey(key,
                $"`{key}` must be supplied ONLY as a dev default in appsettings.Development.json.");
            nonDevParameters.Should().NotContainKey(key,
                $"`{key}` may only carry the DEV-ONLY default in appsettings.Development.json — "
                + "committing it to the non-Development appsettings.json would ship a production secret.");
        }
    }

    [TestMethod]
    public void DevAuthAdminSecret_MatchesRealmFileServiceAccountClientSecret()
    {
        var realmSecret = ReadRealmClientSecret(RealmPath, "school-collab-auth-admin");
        var devSecret = ReadParameters(DevSettingsPath)[KeycloakAuthAdminSecretKey];

        devSecret.Should().Be(realmSecret,
            "the committed dev `keycloak-auth-admin-secret` must equal the realm import file's "
            + "`school-collab-auth-admin` secret — a mismatch makes Keycloak reject the Admin REST "
            + "client_credentials grant on the first admin call, and it surfaces only at runtime.");
    }

    [TestMethod]
    public void DevTeacherIdDefault_MatchesDevIdentitySeederDevTeacherId()
    {
        var devValue = ReadParameters(DevSettingsPath)[DevTeacherIdKey];
        var seededTeacherId = ReadSeededDevTeacherId();

        devValue.Should().Be(seededTeacherId,
            $"the committed dev `{DevTeacherIdKey}` must equal DevIdentitySeeder.DevTeacherId — "
            + "that is the row the dev seed inserts inside the 'Dev School' tenant, and the value "
            + "the committed realm file's `teacher_id` mapper emits. A drifted value names a teacher "
            + "the dev database does not have, so the teacher-scoped read answers empty and the "
            + "ward portal's queue looks like a successful 'no submissions' result. Nothing else "
            + "compares the two, and it surfaces only in a live `aspire run`.");
    }

    [TestMethod]
    public void DevTeacherIdDefault_BaseValueIsEmpty_FailClosed()
    {
        ReadParameters(SettingsPath).Should().ContainKey(DevTeacherIdKey)
            .WhoseValue.Should().BeEmpty(
                "the committed base default is FAIL-CLOSED — an empty value keeps the dev bypass "
                + "claim-less (TestAuthHandlerOptions.TeacherId stays Guid.Empty) and a publish "
                + "must never bake a dev identity into a manifest. The real Guid lives only in "
                + "appsettings.Development.json, beside the Keycloak dev secrets.");
    }

    [TestMethod]
    public void EveryDevParameterDefault_IsDeclaredAsAnAppHostParameter()
    {
        var devParameters = ReadParameters(DevSettingsPath);
        devParameters.Should().NotBeEmpty(
            "the dev-default guard must not pass vacuously over an empty parameter set.");

        // Direction matters: JSON -> code. Reading only the JSON is exactly how a missing
        // `AddParameter` declaration went unnoticed while every guard stayed green.
        var appHostSource = File.ReadAllText(FindAppHostFile("Program.cs"));
        var missing = devParameters.Keys
            .Where(key => !appHostSource.Contains($"AddParameter(\"{key}\"", StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            "every committed dev default (appsettings.Development.json `Parameters:`) must be backed "
            + "by a real Aspire parameter declaration (`AddParameter(\"<key>\"` in the AppHost "
            + "Program.cs). Precedent: `keycloak-auth-admin-secret` carried a dev default and "
            + "documentation while `builder.AddParameter(\"keycloak-auth-admin-secret\", secret: true)` "
            + "was never written — every other guard stayed green because it read only the JSON. "
            + "Missing declarations: " + string.Join(", ", missing) + ".");
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static string[] PromptingSecretParameterKeys()
        => new[] { KeycloakAdminPasswordKey, KeycloakClientSecretKey, KeycloakAuthAdminSecretKey, SmtpUserKey, SmtpPasswordKey };

    /// <summary>Reads a file's <c>Parameters</c> section as a key → value map. THROWS when
    /// the section is absent, so a missing block cannot make the assertions above pass
    /// vacuously.</summary>
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

    /// <summary>Resolves the named client's <c>secret</c> from the realm import file's
    /// <c>clients</c> array. THROWS when the client or its secret is missing.</summary>
    private static string ReadRealmClientSecret(string path, string clientId)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("clients", out var clients) || clients.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Realm file {path} has no 'clients' array.");
        }

        foreach (var client in clients.EnumerateArray())
        {
            if (client.ValueKind == JsonValueKind.Object
                && client.TryGetProperty("clientId", out var id)
                && id.ValueKind == JsonValueKind.String
                && id.GetString() == clientId)
            {
                if (!client.TryGetProperty("secret", out var secret) || secret.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException($"Realm client '{clientId}' in {path} has no string 'secret'.");
                }

                return secret.GetString()!;
            }
        }

        throw new InvalidOperationException($"Realm file {path} has no client with clientId '{clientId}'.");
    }

    /// <summary>Reads the fixed dev teacher id from the seeder's own source — the AppHost must not
    /// reference <c>MigrationService</c>, and this test project does not either, so the literal is
    /// scanned hermetically. THROWS rather than passing vacuously when the field or its
    /// <c>Guid.Parse</c> literal cannot be found.</summary>
    private static string ReadSeededDevTeacherId()
    {
        var source = File.ReadAllText(DevIdentitySeederPath);
        source.Should().Contain("DevTeacherId",
            "the seeder must still declare the fixed dev teacher id this guard compares against.");

        var match = SeededDevTeacherId.Match(source);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Could not find the `public static readonly Guid DevTeacherId = Guid.Parse(\"…\")` "
                + $"declaration in {DevIdentitySeederPath}.");
        }

        return match.Groups["id"].Value;
    }

    /// <summary>Resolves the single realm candidate in the AppHost dir. THROWS on zero or
    /// multiple candidates so a renamed realm cannot pass by silently finding nothing.</summary>
    private static string FindRealmFile()
    {
        var appHostDir = FindAppHostDir();
        var candidates = Directory.EnumerateFiles(appHostDir, "*-realm.json")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one *-realm.json under {appHostDir}, found {candidates.Count}.");
        }

        return candidates[0];
    }

    private static string FindAppHostFile(string name)
    {
        var path = Path.Combine(FindAppHostDir(), name);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Expected AppHost file {path}.");
        }

        return path;
    }

    private static string FindAppHostDir()
        => Path.Combine(FindRepoRoot(), "src", "AppHost", "SchoolCollab.AppHost");

    private static string FindRepoFile(params string[] segments)
    {
        var path = Path.Combine([FindRepoRoot(), .. segments]);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Expected repo file {path}.");
        }

        return path;
    }

    private static string FindRepoRoot()
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

        return dir.FullName;
    }
}
