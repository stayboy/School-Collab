using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Round A (<c>documents/solution/assignment-policy-fields.md</c> D8, R1) — the shipped Students
/// migration's back-compat contract, asserted against <b>the file that ships</b> (the
/// <c>PeriodSequenceBackfillMigrationTests</c> extraction precedent: the SQL is regex-read from the
/// migration source, never retyped).
///
/// <para>This one is sharper than its Settings twin because EF scaffolded the change as a
/// <c>RenameColumn</c> of <c>requires_signature_default</c> →
/// <c>requires_approval_before_publish</c>: left in place, every legacy signature boolean would
/// have become an <i>approval</i> flag (and the legacy column would have vanished). The shipped
/// file must be an <c>AddColumn</c> plus the three-valued D8 backfill, with the legacy column
/// untouched.</para>
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
            directory!.FullName, "src", "Students", "SchoolCollab.Students.Core", "Migrations");

        var file = Directory.GetFiles(migrationsDir, $"*{ShippedMigrationSuffix}").SingleOrDefault();
        file.Should().NotBeNull(
            $"the migration '*{ShippedMigrationSuffix}' must exist — this test asserts the SQL that ships");
        return file!;
    }

    /// <summary>The <c>Up()</c>/<c>Down()</c> bodies of one named migration.</summary>
    private static (string Up, string Down) ReadBodies(string suffix)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SchoolCollab.slnx")))
        {
            directory = directory.Parent;
        }

        var migrationsDir = Path.Combine(
            directory!.FullName, "src", "Students", "SchoolCollab.Students.Core", "Migrations");
        var file = Directory.GetFiles(migrationsDir, $"*{suffix}").SingleOrDefault();
        file.Should().NotBeNull($"the migration '*{suffix}' must exist — this test asserts the file that ships");

        var source = File.ReadAllText(file!);
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

    [TestMethod]
    public void ShippedMigration_BackfillsTheLegacyTriState_ToTheD8Mapping()
    {
        var (up, _) = ReadUpAndDown();
        var blocks = SqlBlockPattern.Matches(up);

        blocks.Count.Should().Be(1,
            "the grade backfill ships as exactly one Sql block; a different count means the "
            + "extraction is reading something other than what ships");

        var backfill = blocks[0].Groups["sql"].Value;
        backfill.Should().Contain("SET signature_requirement");
        backfill.Should().Contain("WHEN requires_signature_default IS TRUE THEN 'Optional'");
        backfill.Should().Contain("WHEN requires_signature_default IS FALSE THEN 'Disabled'");
        backfill.Should().Contain("ELSE NULL",
            "D8: null → null — a grade with no override keeps inheriting the tenant default, so the "
            + "mapping must be explicitly three-valued rather than defaulting a NULL legacy value");
    }

    [TestMethod]
    public void ShippedMigration_AddsColumns_NeverRenamesOrDropsTheLegacyColumn()
    {
        var (up, _) = ReadUpAndDown();

        up.Should().NotContain("RenameColumn",
            "EF's scaffolded rename would reinterpret every legacy signature boolean as an approval flag");
        up.Should().NotContain("DropColumn",
            "additive-only (R1): the legacy column survives the deploy window");
        up.Should().Contain("name: \"requires_approval_before_publish\"",
            "the approval field is a NEW nullable column, not the renamed legacy one");
        up.Should().NotContain("name: \"requires_signature_default\"",
            "the legacy column is neither re-added nor touched — it is simply left as it was");
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

        down.Should().NotContain("RenameColumn", "Down() must not manufacture the scaffolded rename either");

        up.IndexOf("AddColumn", StringComparison.Ordinal)
            .Should().BeLessThan(up.IndexOf("SET signature_requirement", StringComparison.Ordinal),
                "the new column must exist before the backfill writes to it");
    }

    // ── Round assignment-rules-policy-rework (D7/AC6): the guardian-review + archive-window pair ──

    private const string ReviewAndArchiveMigrationSuffix = "_AddAssignmentPolicyReviewAndArchiveFields.cs";

    /// <summary>
    /// AC6: the Students half of the migration pair — the same two additive nullable columns as the
    /// Settings migration, shipped in the same change (D7), with a <c>Down()</c> that is the exact
    /// inverse and nothing to backfill.
    /// </summary>
    [TestMethod]
    public void ReviewAndArchiveMigration_AddsTwoNullableColumns_AndHasNoBackfill()
    {
        var (up, down) = ReadBodies(ReviewAndArchiveMigrationSuffix);

        up.Should().Contain("name: \"mandatory_review\"",
            "the grade override gets the guardian-review flag as a real column (no JSON/owned type)");
        up.Should().Contain("name: \"archive_grace_days\"");
        up.Should().NotContain("DropColumn", "additive-only (ef-migrations rule 8)");
        up.Should().NotContain("RenameColumn");
        SqlBlockPattern.Matches(up).Should().BeEmpty(
            "there is nothing to backfill: an existing override simply stays 'inherit' on both fields");

        down.Should().Contain("name: \"mandatory_review\"", "Down() drops what Up() added");
        down.Should().Contain("name: \"archive_grace_days\"");
        down.IndexOf("name: \"archive_grace_days\"", StringComparison.Ordinal)
            .Should().BeLessThan(down.IndexOf("name: \"mandatory_review\"", StringComparison.Ordinal),
                "ef-migrations rule 4: Down() runs last-added first");
    }
}
