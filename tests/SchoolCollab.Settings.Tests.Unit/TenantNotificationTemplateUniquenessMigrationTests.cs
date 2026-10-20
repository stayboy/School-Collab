using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Round notification-templates-b2c, WS1 — the index swap's contract, asserted against <b>the
/// migration file that ships</b> (the <c>AssignmentPolicyFieldsMigrationTests</c> extraction
/// precedent: the source is regex-read, never retyped, and a silent extraction failure fails rather
/// than passing vacuously).
///
/// <para>Two things a fresh-database test run cannot see:</para>
/// <list type="number">
///   <item><b>The swap must not lose or rewrite data.</b> Relaxing the unique index to non-unique is
///   a pure index replacement on the same columns with the same <c>is_deleted = false</c> filter — any
///   <c>migrationBuilder.Sql(…)</c> in <c>Up()</c> would mean the round smuggled a data migration it
///   explicitly does not have.</item>
///   <item><b>The replacement index must stay non-unique.</b> If a later migration (or a hand edit)
///   re-adds <c>unique: true</c>, every tenant with a constrained candidate plus a default fails to
///   write — the exact failure this round removes.</item>
/// </list>
/// </summary>
[TestClass]
public class TenantNotificationTemplateUniquenessMigrationTests
{
    private const string ShippedMigrationSuffix = "_RelaxTenantNotificationTemplateUniqueness.cs";
    private const string IndexName = "ix_tenant_notification_templates_tenant_kind_channel";
    private const string TemplateTable = "tenant_notification_templates";

    [TestMethod]
    public void Up_ReplacesTheIndexWithANonUniqueOne_OnTheSameColumnsAndFilter()
    {
        var up = ReadBodies().Up;

        up.Should().Contain($"name: \"{IndexName}\"", "the swap must name the shipped index");
        up.Should().Contain($"table: \"{TemplateTable}\"");
        up.Should().Contain("columns: new[] { \"tenant_id\", \"kind\", \"channel\" }",
            "the column set is unchanged — this is an index swap, not a re-key");
        up.Should().Contain("filter: \"is_deleted = false\"",
            "the soft-delete filter is what lets a deleted row be recreated");
        up.Should().NotContain("unique: true",
            "Up() must create the index NON-unique: candidates coexist with the tenant default");
    }

    [TestMethod]
    public void Up_CarriesNoDataMigration()
    {
        // "No data migration" (round plan WS1): the swap touches no row, so no SQL block may appear.
        ReadBodies().Up.Should().NotContain("migrationBuilder.Sql(",
            "the round explicitly ships no data migration — a Sql() block here would be out of scope");
    }

    [TestMethod]
    public void Down_RestoresTheUniqueIndex()
    {
        // Reversal must be the inverse of Up(): the unique index comes back.
        var down = ReadBodies().Down;

        down.Should().Contain($"name: \"{IndexName}\"");
        down.Should().Contain("unique: true", "Down() restores the pre-round invariant");
    }

    private static (string Up, string Down) ReadBodies()
    {
        var path = Path.Combine(MigrationsDirectory(), Directory
            .GetFiles(MigrationsDirectory(), $"*{ShippedMigrationSuffix}")
            .Single());

        var source = File.ReadAllText(path);
        var upIndex = source.IndexOf("protected override void Up", StringComparison.Ordinal);
        var downIndex = source.IndexOf("protected override void Down", StringComparison.Ordinal);

        upIndex.Should().BeGreaterThan(-1, "the shipped migration must implement Up()");
        downIndex.Should().BeGreaterThan(upIndex, "the shipped migration must implement Down() after Up()");

        return (source[upIndex..downIndex], source[downIndex..]);
    }

    private static string MigrationsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SchoolCollab.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must run from inside the repository");

        var migrationsDir = Path.Combine(
            directory!.FullName, "src", "Settings", "SchoolCollab.Settings.Core", "Migrations");

        Directory.GetFiles(migrationsDir, $"*{ShippedMigrationSuffix}").Should().ContainSingle(
            $"exactly one '*{ShippedMigrationSuffix}' must exist — this test asserts the file that ships");

        return migrationsDir;
    }
}
