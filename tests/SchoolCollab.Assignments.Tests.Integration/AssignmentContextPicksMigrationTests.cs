using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace SchoolCollab.Assignments.Tests.Integration;

/// <summary>
/// R4 (spec §6A CP-5; round <c>assignment-context-strands-lessons</c>) — the
/// <c>AddAssignmentContextPicks</c> migration's schema round-trip on REAL Postgres/Npgsql (AC-2).
/// The in-memory provider has no catalogue to inspect and has no notion of a column default, and
/// this migration's whole claim is a catalogue shape plus a data-survival claim ("every existing
/// row simply has no picks"), so the test MUST run against Postgres.
///
/// <para><b>Discriminating:</b> the schema immediately before this migration carries neither
/// <c>context_strand_ids</c> nor <c>context_lesson_ids</c> — both asserted ABSENT before it runs —
/// and the seeded assignment is asserted directly through SQL afterwards, so the migration cannot
/// pass by having deleted or rebuilt the row. The migration's script is also asserted to be
/// additive-only (two ADD COLUMNs, no CREATE TABLE): the plan's P5 contract.</para>
/// </summary>
[TestClass]
public sealed class AssignmentContextPicksMigrationTests
{
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>The migration immediately BEFORE the picks migration — the last schema with no
    /// context pick columns, so the round-trip can start from a real pre-R4 database.</summary>
    private const string PrePicksMigration = "20261006225404_DropAssignmentGradeLevelColumn";

    /// <summary>This test's OWN migration. The script below is pinned to it because
    /// <c>toMigration: null</c> means "to the tip" — it silently absorbs every migration that lands
    /// afterwards, which is how a later sibling migration (one that legitimately creates a table)
    /// turned this file's <c>NotContain("CREATE TABLE")</c> red from a distance.</summary>
    private const string PicksMigration = "20261007195749_AddAssignmentContextPicks";

    [TestMethod]
    public async Task AddPicks_AddsTwoNotNullUuidArraysDefaultingToEmpty_AndKeepsRows()
    {
        var connectionString = await AssignmentsDbFactory.CreateDatabaseAsync(Guid.NewGuid().ToString("N"));

        // ── Arrange: the pre-R4 schema + one real assignment row. ──
        await using (var pre = AssignmentsDbFactory.CreateContext(connectionString))
        {
            await pre.Database.MigrateAsync(PrePicksMigration);
        }

        await RelaxLegacyNotNullAsync(connectionString);

        var assignmentId = Guid.NewGuid();
        await SeedAssignmentAsync(connectionString, assignmentId);

        (await ColumnExistsAsync(connectionString, "context_strand_ids")).Should().BeFalse(
            "premise: the pre-R4 schema carries no context pick columns — otherwise the assertions below are vacuous");
        (await ColumnExistsAsync(connectionString, "context_lesson_ids")).Should().BeFalse();

        // ── Act: apply the picks migration (migrate to latest). ──
        await using (var apply = AssignmentsDbFactory.CreateContext(connectionString))
        {
            await apply.Database.MigrateAsync();
        }

        // ── Assert: the catalogue shape, then the data that must survive it. ──
        foreach (var column in new[] { "context_strand_ids", "context_lesson_ids" })
        {
            (await ColumnExistsAsync(connectionString, column)).Should().BeTrue(
                $"AC-2: the picks migration adds assignments.{column}");
            (await UdtNameAsync(connectionString, column)).Should().Be("_uuid",
                $"{column} is a Npgsql first-class uuid[] — no owned table, no converter");

            var (isNullable, columnDefault) = await ColumnShapeAsync(connectionString, column);
            isNullable.Should().Be("NO",
                "the CLR property is non-nullable, so the column is NOT NULL — \"no picks\" has exactly one " +
                "representation, the empty array");
            // Postgres normalizes the empty-array literal to `ARRAY[]::uuid[]` (what EF's
            // `defaultValue: new Guid[0]` renders as) — either spelling is the empty uuid array.
            columnDefault.Should().Contain("ARRAY[]::uuid[]",
                $"…and defaulting to the empty array, which backfills every existing row in the one ADD COLUMN " +
                $"({column})");
        }

        (await ScalarAsync(connectionString,
                "SELECT count(*) FROM assignments WHERE id = @id", ("id", assignmentId))).Should().Be(1,
            "adding a column must not delete the assignment rows");

        var (strands, lessons) = await PicksAsync(connectionString, assignmentId);
        strands.Should().BeEmpty("a pre-migration row survives with NO picks (nothing authored them before R4)");
        lessons.Should().BeEmpty();

        // ── Additive-only by construction (P5): the migration's own SQL adds two columns and
        // touches nothing else — no new table, no rewritten/backfilled table. ──
        var script = await MigrationScriptAsync(connectionString);
        script.Should().Contain(
                "ALTER TABLE assignments ADD context_strand_ids uuid[] NOT NULL DEFAULT ARRAY[]::uuid[]",
            "the strand column is an additive NOT NULL uuid[] defaulting to the empty array")
            .And.Contain(
                "ALTER TABLE assignments ADD context_lesson_ids uuid[] NOT NULL DEFAULT ARRAY[]::uuid[]",
                "…and so is the lesson column");
        script.Split("ALTER TABLE assignments").Should().HaveCount(3,
            "exactly the two pick columns are added by THIS migration's script");
        script.Should().NotContain("CREATE TABLE", "no new table is introduced (the picks are columns, not a join table)")
            .And.NotContain("DROP COLUMN", "…and nothing is dropped");
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>The pre-R4 schema's <c>assignments.subject_coded_value_id</c> is NOT NULL while the
    /// EF model no longer maps it, so a raw insert has to relax it (the <c>AssignmentsDbFactory</c>
    /// precedent).</summary>
    private static async Task RelaxLegacyNotNullAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "ALTER TABLE assignments ALTER COLUMN subject_coded_value_id DROP NOT NULL";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedAssignmentAsync(string connectionString, Guid assignmentId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO assignments
                (id, title, description, assignment_type, grading_format, target_audience_type,
                 topic_id, status, created_by_teacher_id, mandatory_review,
                 archive_grace_days, created_at, updated_at)
            VALUES
                (@id, 'Picks round-trip', NULL, 0, 0, 1, @topic, 0, @teacher, true, 30, now(), now())
            """;
        cmd.Parameters.AddWithValue("id", assignmentId);
        cmd.Parameters.AddWithValue("topic", TopicId);
        cmd.Parameters.AddWithValue("teacher", TeacherId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>The picks migration's OWN SQL — <see cref="PrePicksMigration"/> to
    /// <see cref="PicksMigration"/>, never the tip — the observable for "this migration adds the two
    /// columns and nothing else".</summary>
    private static async Task<string> MigrationScriptAsync(string connectionString)
    {
        await using var context = AssignmentsDbFactory.CreateContext(connectionString);
        var migrator = context.GetService<IMigrator>();
        return migrator.GenerateScript(
            fromMigration: PrePicksMigration,
            toMigration: PicksMigration,
            MigrationsSqlGenerationOptions.Idempotent);
    }

    private static async Task<bool> ColumnExistsAsync(string connectionString, string columnName) =>
        await ScalarAsync(connectionString,
            """
            SELECT count(*) FROM information_schema.columns
            WHERE table_name = 'assignments' AND column_name = @column
            """, ("column", columnName)) > 0;

    private static async Task<string> UdtNameAsync(string connectionString, string columnName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT udt_name FROM information_schema.columns
            WHERE table_name = 'assignments' AND column_name = @column
            """;
        cmd.Parameters.AddWithValue("column", columnName);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<(string IsNullable, string ColumnDefault)> ColumnShapeAsync(
        string connectionString, string columnName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT is_nullable, coalesce(column_default, '') FROM information_schema.columns
            WHERE table_name = 'assignments' AND column_name = @column
            """;
        cmd.Parameters.AddWithValue("column", columnName);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.GetString(1));
    }

    private static async Task<(Guid[] Strands, Guid[] Lessons)> PicksAsync(
        string connectionString, Guid assignmentId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT context_strand_ids, context_lesson_ids FROM assignments WHERE id = @id";
        cmd.Parameters.AddWithValue("id", assignmentId);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return ((Guid[])reader.GetValue(0), (Guid[])reader.GetValue(1));
    }

    private static async Task<int> ScalarAsync(
        string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
