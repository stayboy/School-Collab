using System.Text.Json;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the AppHost's committed DEV-ONLY parameter defaults (keycloak-dev-defaults round).
/// Four parameters — <c>keycloak-admin-password</c>, <c>keycloak-client-secret</c>,
/// <c>smtp-user</c> and <c>smtp-password</c> — had no value in any Development config
/// source, so Aspire stopped and prompted on a plain <c>aspire run</c>. Their defaults now
/// live in <c>appsettings.Development.json</c>, which is NOT loaded outside Development, so
/// the "no committed production secret" posture is preserved. That posture is asserted here
/// by requiring the same four keys to be ABSENT from the non-Development
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
/// </summary>
[TestClass]
public class AppHostDevParameterDefaultsArchitectureTests
{
    private const string KeycloakAdminPasswordKey = "keycloak-admin-password";
    private const string KeycloakClientSecretKey = "keycloak-client-secret";
    private const string SmtpUserKey = "smtp-user";
    private const string SmtpPasswordKey = "smtp-password";

    private static readonly string DevSettingsPath = FindAppHostFile("appsettings.Development.json");
    private static readonly string SettingsPath = FindAppHostFile("appsettings.json");
    private static readonly string RealmPath = FindRealmFile();

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

    // ── Helpers ─────────────────────────────────────────────────────────

    private static string[] PromptingSecretParameterKeys()
        => new[] { KeycloakAdminPasswordKey, KeycloakClientSecretKey, SmtpUserKey, SmtpPasswordKey };

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
