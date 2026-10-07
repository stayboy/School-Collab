using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Round A (<c>documents/solution/assignment-policy-fields.md</c> D8, plan P1-2) — the shipped
/// Settings migration's back-compat contract, asserted against <b>the file that ships</b> (the
/// <c>PeriodSequenceBackfillMigrationTests</c> extraction precedent: the SQL is regex-read from the
/// migration source, never retyped, and the count is pinned so a silent extraction failure cannot
/// make the test vacuously green).
///
/// <para>Two things here are invisible to a fresh-database test run — both were plan-review
/// findings:</para>
/// <list type="number">
///   <item><b>The legacy column must not be dropped</b> (R1 / ef-migrations rule 8): EF scaffolds a
///   <c>DropColumn</c> for <c>requires_signature_default</c> because the entity no longer maps it.
///   Removing it is the whole point of the round's additive-only posture.</item>
///   <item><b>It must stay INSERT-safe</b> (P1-2): the column is <c>NOT NULL</c> with no database
///   default, so once unmapped every first insert of a tenant policy row fails with Postgres 23502.
///   <c>Up()</c> therefore re-adds a <c>false</c> default.</item>
/// </list>
/// </summary>
[TestClass]
public class AssignmentPolicyFieldsMigrationTests
{
    private const string ShippedMigrationSuffix = "_AddAssignmentPolicyFields.cs";

    private static readonly Regex SqlBlockPattern = new(
        "migrationBuilder\\.Sql\\(\\s*\"\"\"(?<sql>.*?)\"\"\"",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly string[] NewColumns =
        ["signature_requirement", "requires_approval_before_publish", "max_primary_contacts", "max_copy_contacts"];

    private static string ShippedMigrationPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SchoolCollab.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must run from inside the repository");

        var migrationsDir = Path.Combine(
            directory!.FullName, "src", "Settings", "SchoolCollab.Settings.Core", "Migrations");

        var file = Directory.GetFiles(migrationsDir, $"*{ShippedMigrationSuffix}").SingleOrDefault();
        file.Should().NotBeNull(
            $"the migration '*{ShippedMigrationSuffix}' must exist — this test asserts the SQL that ships");
        return file!;
    }

    private static string MigrationPath(string suffix)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SchoolCollab.slnx")))
        {
            directory = directory.Parent;
        }

        var migrationsDir = Path.Combine(
            directory!.FullName, "src", "Settings", "SchoolCollab.Settings.Core", "Migrations");

        var file = Directory.GetFiles(migrationsDir, $"*{suffix}").SingleOrDefault();
        file.Should().NotBeNull($"the migration '*{suffix}' must exist — this test asserts the file that ships");
        return file!;
    }

    /// <summary>The <c>Up()</c>/<c>Down()</c> bodies of one named migration.</summary>
    private static (string Up, string Down) ReadBodies(string suffix)
    {
        var source = File.ReadAllText(MigrationPath(suffix));
        var upIndex = source.IndexOf("protected override void Up", StringComparison.Ordinal);
        var downIndex = source.IndexOf("protected override void Down", StringComparison.Ordinal);
        upIndex.Should().BeGreaterThan(-1, "the migration must implement Up()");
        downIndex.Should().BeGreaterThan(upIndex, "the migration must implement Down() after Up()");
        return (source[upIndex..downIndex], source[downIndex..]);
    }

    /// <summary>
    /// The two method <b>bodies</b> — not the whole file: the class-level XML doc names the very
    /// members these tests assert the absence of ("the EF-scaffolded DropColumn has to be deleted
    /// by hand"), so a source-wide scan would fail on its own explanation.
    /// </summary>
    private static (string Up, string Down) ReadUpAndDown()
    {
        var source = File.ReadAllText(ShippedMigrationPath());
        var upIndex = source.IndexOf("protected override void Up", StringComparison.Ordinal);
        var downIndex = source.IndexOf("protected override void Down", StringComparison.Ordinal);
        upIndex.Should().BeGreaterThan(-1, "the migration must implement Up()");
        downIndex.Should().BeGreaterThan(upIndex, "the migration must implement Down() after Up()");
        return (source[upIndex..downIndex], source[downIndex..]);
    }

    private static MatchCollection ReadSqlBlocks(string section) => SqlBlockPattern.Matches(section);

    [TestMethod]
    public void ShippedMigration_BackfillsTheLegacyBoolean_ToTheD8Mapping()
    {
        var (up, _) = ReadUpAndDown();
        var blocks = ReadSqlBlocks(up);

        blocks.Count.Should().Be(2,
            "the settings Up() ships exactly two Sql blocks (the D8 backfill, then the legacy column's "
            + "INSERT-safety default); a different count means the extraction below is reading "
            + "something other than what ships");

        var backfill = blocks[0].Groups["sql"].Value;
        backfill.Should().Contain("SET signature_requirement");
        backfill.Should().Contain("WHEN requires_signature_default THEN 'Optional'",
            "D8: true → Optional (the old 'required by default' becomes the pre-filled default)");
        backfill.Should().Contain("ELSE 'Disabled'",
            "D8: false → Disabled (this table's column is NOT NULL, so every row has a value)");
        backfill.Should().NotContain("RequiresSignatureDefault",
            "the stored values are the enum names the entity's HasConversion<string>() writes");

        var insertSafety = blocks[1].Groups["sql"].Value;
        insertSafety.Should().Contain("SET DEFAULT false", "block 2 is the legacy column's default");
    }

    [TestMethod]
    public void ShippedMigration_KeepsTheLegacyColumn_AndMakesItInsertSafe()
    {
        var (up, down) = ReadUpAndDown();

        up.Should().NotContain("DropColumn",
            "additive-only (R1): the legacy column must survive the deploy window, and the "
            + "EF-scaffolded DropColumn for requires_signature_default has to be deleted by hand");
        down.Should().NotContain("RenameColumn",
            "a rename would move signature booleans into a different column");

        up.Should().Contain("ALTER COLUMN requires_signature_default SET DEFAULT false",
            "P1-2: the column is NOT NULL with no default and is no longer mapped, so the create "
            + "path would omit it and every first insert would fail with Postgres 23502");
        down.Should().Contain("ALTER COLUMN requires_signature_default DROP DEFAULT",
            "Down() restores the pre-migration schema exactly (NOT NULL, no default)");
    }

    [TestMethod]
    public void ShippedMigration_IsAdditive_AndDownIsItsInverse()
    {
        var (up, down) = ReadUpAndDown();

        foreach (var column in NewColumns)
        {
            up.Should().Contain($"name: \"{column}\"",
                "every new field is a real column on the policy table (no JSON/owned type)");
            down.Should().Contain($"name: \"{column}\"", "Down() drops what Up() added");
        }

        // Order matters: Up() adds, backfills, then relaxes the legacy column's NOT NULL; the
        // inverse ordering in Down() is enforced by ef-migrations rule 4 and asserted here.
        up.IndexOf("AddColumn", StringComparison.Ordinal)
            .Should().BeLessThan(up.IndexOf("ALTER COLUMN requires_signature_default SET DEFAULT", StringComparison.Ordinal),
                "the new column must exist before the backfill writes to it");
        up.IndexOf("SET signature_requirement", StringComparison.Ordinal)
            .Should().BeLessThan(up.IndexOf("ALTER COLUMN requires_signature_default SET DEFAULT", StringComparison.Ordinal),
                "backfill first, then relax the constraint");
        down.IndexOf("ALTER COLUMN requires_signature_default DROP DEFAULT", StringComparison.Ordinal)
            .Should().BeLessThan(down.IndexOf("DropColumn", StringComparison.Ordinal),
                "the inverse of Up() runs last-added first");
    }

    // ── Round assignment-rules-policy-rework (D7/AC6): the guardian-review + archive-window pair ──

    private const string ReviewAndArchiveMigrationSuffix = "_AddAssignmentPolicyReviewAndArchiveFields.cs";

    /// <summary>
    /// AC6: the Settings half of the migration pair. Two additive nullable columns, no backfill (a
    /// null value means "unset" — the write seam then keeps the built-in defaults), and a
    /// <c>Down()</c> that is the exact inverse.
    /// </summary>
    [TestMethod]
    public void ReviewAndArchiveMigration_AddsTwoNullableColumns_AndHasNoBackfill()
    {
        var (up, down) = ReadBodies(ReviewAndArchiveMigrationSuffix);

        up.Should().Contain("name: \"mandatory_review\"",
            "the guardian-review flag is a real column on the policy table (no JSON/owned type)");
        up.Should().Contain("name: \"archive_grace_days\"",
            "the archive window joins the same field set (D5) rather than a second resolution mechanism");
        up.Should().NotContain("DropColumn", "additive-only (ef-migrations rule 8)");
        up.Should().NotContain("RenameColumn");
        ReadSqlBlocks(up).Should().BeEmpty(
            "there is nothing to backfill: an existing row simply stays 'unset' on both fields");

        down.Should().Contain("name: \"mandatory_review\"", "Down() drops what Up() added");
        down.Should().Contain("name: \"archive_grace_days\"");
        down.IndexOf("name: \"archive_grace_days\"", StringComparison.Ordinal)
            .Should().BeLessThan(down.IndexOf("name: \"mandatory_review\"", StringComparison.Ordinal),
                "ef-migrations rule 4: Down() runs last-added first");
    }
}
