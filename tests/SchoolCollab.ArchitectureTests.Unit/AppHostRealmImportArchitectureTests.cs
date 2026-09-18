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
