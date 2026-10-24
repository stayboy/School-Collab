using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Naming spec D8 — the title backfill and the filtered unique index, asserted against <b>the migration
/// file that ships</b> (the <c>TenantNotificationTemplateUniquenessMigrationTests</c> extraction
/// precedent: the source is regex-read, never retyped, and a silent extraction failure fails rather than
/// passing vacuously).
///
/// <para>A fresh-database test run cannot see any of this: the tables are empty, so the backfill updates
/// nothing and the index accepts every row. What only the shipped source can prove:</para>
/// <list type="number">
///   <item><b>Existing rows get a title.</b> The column becomes required in the same migration, so a
///   backfill that ran after it — or ran twice, or ran not at all — would fail the upgrade on a database
///   that has rows.</item>
///   <item><b>The backfill derives the label the UI used to derive at display time</b> (subject, then the
///   body's plain text, then "{kind} · {channel}") and never leaves markup in it.</item>
///   <item><b>The unique index is filtered to live rows that carry a code</b> — a null code is a feature
///   (a pre-upgrade row renders a dash), not a value to keep unique.</item>
/// </list>
/// </summary>
[TestClass]
public class TenantNotificationTemplateTitleBackfillMigrationTests
{
    private const string ShippedMigrationSuffix =
        "_AddTenantNotificationTemplateTitleDescriptionAndCode.cs";

    private const string TemplateTable = "tenant_notification_templates";
    private const string IndexName = "ix_tenant_notification_templates_tenant_code";

    [TestMethod]
    public void Up_BackfillsTitle_InExactlyOneSqlBlock()
    {
        // Arrange
        var up = ReadBodies().Up;

        // Act
        var sqlBlocks = CountOccurrences(up, "migrationBuilder.Sql(");

        // Assert
        sqlBlocks.Should().Be(1,
            "the title backfill is the migration's only data change — a second Sql() block would mean "
            + "the round shipped something it does not document");
    }

    [TestMethod]
    public void Up_Backfill_DerivesTheTitleFromSubjectThenPlainBody_ThenTheKindAndChannelFallback()
    {
        // Arrange
        var sql = SqlBlock(ReadBodies().Up);

        // Act / Assert — the COALESCE chain, in the order the label used to be derived in.
        sql.Should().Contain("SET title = COALESCE(",
            "every row gets a title from the first non-empty source");
        sql.Should().Contain("NULLIF(btrim(subject), ''),",
            "the subject is the first choice — the label the grid showed when a template had one");
        sql.Should().Contain("regexp_replace(body, '<[^>]*>', ' ', 'g'),",
            "an email body's markup is stripped before a snippet of it becomes a name");
        sql.Should().Contain("left(",
            "the body snippet is truncated — a template cannot be named by a whole email");
        sql.Should().Contain("WHERE title IS NULL;",
            "the backfill only touches rows that have no title yet");
    }

    [TestMethod]
    public void Up_Backfill_HasBothEnumFallbacks_SoNoRowStaysNameless()
    {
        // Arrange
        var sql = SqlBlock(ReadBodies().Up);

        // Act / Assert — Kind and Channel are stored as ints, so the last-resort label names them here.
        sql.Should().Contain("CASE kind",
            "a row with neither subject nor body text is named by its kind");
        sql.Should().Contain("WHEN 1 THEN 'Reminder'",
            "the kind fallback spells the stored int in English");
        sql.Should().Contain("ELSE 'Template'",
            "an unmapped kind still produces a name rather than null");

        sql.Should().Contain("CASE channel",
            "…and by its contact mode");
        sql.Should().Contain("WHEN 1 THEN 'SMS'",
            "the channel fallback spells the stored int in English");
        sql.Should().Contain("ELSE 'Channel'",
            "an unmapped channel still produces a name rather than null");
    }

    [TestMethod]
    public void Up_MakesTitleRequired_OnlyAfterTheBackfillRan()
    {
        // Arrange
        var up = ReadBodies().Up;

        // Act
        var backfillIndex = up.IndexOf("migrationBuilder.Sql(", StringComparison.Ordinal);
        var alterIndex = up.IndexOf("migrationBuilder.AlterColumn<string>(", StringComparison.Ordinal);

        // Assert
        backfillIndex.Should().BeGreaterThan(-1);
        alterIndex.Should().BeGreaterThan(backfillIndex,
            "the column is added nullable, backfilled, and only then made required — in that order, or "
            + "the upgrade fails on any database that already has templates");

        var alterBlock = up[alterIndex..up.IndexOf("migrationBuilder.CreateIndex(", StringComparison.Ordinal)];
        alterBlock.Should().Contain("name: \"title\"");
        alterBlock.Should().Contain("nullable: false", "the title is required from the upgrade onward");
    }

    [TestMethod]
    public void Up_CreatesTheCodeIndex_Last_FilteredToLiveRowsThatCarryACode()
    {
        // Arrange
        var up = ReadBodies().Up;

        // Act
        var indexIndex = up.IndexOf("migrationBuilder.CreateIndex(", StringComparison.Ordinal);

        // Assert — naming spec D6/D7: unique per scope, over live rows, only where a code exists.
        indexIndex.Should().BeGreaterThan(
            up.IndexOf("migrationBuilder.AlterColumn<string>(", StringComparison.Ordinal),
            "the index is the migration's last step, after the schema is whole");

        var indexBlock = up[indexIndex..];
        indexBlock.Should().Contain($"name: \"{IndexName}\"");
        indexBlock.Should().Contain($"table: \"{TemplateTable}\"");
        indexBlock.Should().Contain("columns: new[] { \"tenant_id\", \"code\" }",
            "the code is unique per SCOPE, like a student number");
        indexBlock.Should().Contain("unique: true");
        indexBlock.Should().Contain("filter: \"is_deleted = false AND code IS NOT NULL\"",
            "a tombstoned row frees its code, and a pre-upgrade row's null code is not a value to keep "
            + "unique — two nulls in one scope must not collide");
    }

    [TestMethod]
    public void Down_DropsTheColumns_InTheReverseOfTheOrderUpAddedThem()
    {
        // Arrange
        var down = ReadBodies().Down;

        // Act
        var titleIndex = down.IndexOf("name: \"title\"", StringComparison.Ordinal);
        var descriptionIndex = down.IndexOf("name: \"description\"", StringComparison.Ordinal);
        var codeIndex = down.IndexOf("name: \"code\"", StringComparison.Ordinal);

        // Assert — Up() adds code → description → title, so Down() drops title → description → code.
        titleIndex.Should().BeGreaterThan(-1);
        descriptionIndex.Should().BeGreaterThan(titleIndex,
            "the reversal undoes the last column added first");
        codeIndex.Should().BeGreaterThan(descriptionIndex,
            "…and the first column added last");
        down.Should().Contain($"name: \"{IndexName}\"",
            "the index goes before the column it covers");
    }

    /// <summary>The single <c>migrationBuilder.Sql(…)</c> call's text, from its opening to its close.</summary>
    private static string SqlBlock(string up)
    {
        var start = up.IndexOf("migrationBuilder.Sql(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the shipped migration must carry the title backfill");

        var end = up.IndexOf("migrationBuilder.AlterColumn<string>(", StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "the backfill precedes the NOT NULL alter");

        return up[start..end];
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
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
