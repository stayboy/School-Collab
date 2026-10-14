using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace SchoolCollab.Assignments.Tests.Integration;

/// <summary>
/// Round <c>media-pill-listing</c> (Q7/Slice A) — the <c>AddAssignmentInstructionDurationSeconds</c>
/// migration on REAL Postgres/Npgsql. The in-memory provider has no catalogue to inspect and no notion
/// of a column default, and this migration's whole claim is a catalogue SHAPE plus a data claim ("an
/// existing instruction row simply has no duration"), so the test MUST run against Postgres — the
/// <c>AssignmentInstructionsResponseKindsMigrationTests</c> precedent.
///
/// <para><b>Discriminating:</b> the pre-migration catalogue lacks the column (asserted ABSENT first,
/// so the assertions below cannot pass vacuously), and the seeded pre-migration row is asserted
/// through SQL afterwards, so the migration cannot pass by having rebuilt the row. The script is
/// asserted additive-only against this test's own migration id.</para>
/// </summary>
[TestClass]
public sealed class AddAssignmentInstructionDurationSecondsMigrationTests
{
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>The migration immediately BEFORE this one — the last schema whose instruction table
    /// carries no duration column.</summary>
    private const string PreMigration = "20261010114247_AddAssignmentInstructionTitle";

    /// <summary>This test's OWN migration — pinned rather than the tip, because
    /// <c>toMigration: null</c> silently absorbs every later migration.</summary>
    private const string OwnMigration = "20261014100415_AddAssignmentInstructionDurationSeconds";

    [TestMethod]
    public async Task AddDurationSeconds_AddsNullableColumn_AndKeepsLegacyRowsNull()
    {
        var connectionString = await AssignmentsDbFactory.CreateDatabaseAsync(Guid.NewGuid().ToString("N"));

        // ── Arrange: the pre-migration schema + one real instruction row. ──
        await using (var pre = AssignmentsDbFactory.CreateContext(connectionString))
        {
            await pre.Database.MigrateAsync(PreMigration);
        }

        await RelaxLegacyNotNullAsync(connectionString);
        var assignmentId = Guid.NewGuid();
        await SeedAssignmentAsync(connectionString, assignmentId);
        var questionId = Guid.NewGuid();
        await SeedQuestionAsync(connectionString, assignmentId, questionId, kinds: [3]);
        await SeedInstructionAsync(connectionString, assignmentId, questionId);

        (await ColumnExistsAsync(connectionString, "assignment_instructions", "duration_seconds"))
            .Should().BeFalse(
                "premise: the pre-migration schema has no duration_seconds column — otherwise the "
                + "assertions below are vacuous");

        // ── Act: migrate to the tip. ──
        await using (var apply = AssignmentsDbFactory.CreateContext(connectionString))
        {
            await apply.Database.MigrateAsync();
        }

        // ── Assert: additive, nullable, unbounded — and NO backfill. ──
        (await ColumnExistsAsync(connectionString, "assignment_instructions", "duration_seconds"))
            .Should().BeTrue("Q7: the migration adds assignment_instructions.duration_seconds");
        var (isNullable, columnDefault) = await ColumnShapeAsync(
            connectionString, "assignment_instructions", "duration_seconds");
        isNullable.Should().Be("YES",
            "Q7: nullable — rows written before the column carry none, and nothing invents one for them");
        columnDefault.Should().BeEmpty(
            "no column default and no backfill: a server-side probe would need a media parser the repo "
            + "does not have, so old rows must read NULL (the UI then falls back to —:—)");
        (await UdtNameAsync(connectionString, "assignment_instructions", "duration_seconds"))
            .Should().Be("int4", "whole seconds — the readout renders m:ss from an int");
        (await ScalarAsync(
                connectionString,
                "SELECT count(*) FROM assignment_instructions WHERE duration_seconds IS NULL"))
            .Should().Be(1, "the pre-existing row keeps NULL — no backfill, no invented duration");

        // ── Assert: the duration round-trips. ──
        await ExecuteAsync(
            connectionString,
            "UPDATE assignment_instructions SET duration_seconds = 154 WHERE assignment_id = @id",
            ("id", assignmentId));
        (await ScalarAsync(
                connectionString,
                "SELECT duration_seconds FROM assignment_instructions WHERE assignment_id = @id",
                ("id", assignmentId)))
            .Should().Be(154, "the column stores the media's whole-second duration");

        // ── Assert: THIS migration's own script is additive-only. ──
        await using var context = AssignmentsDbFactory.CreateContext(connectionString);
        var script = context.GetService<IMigrator>().GenerateScript(
            fromMigration: PreMigration,
            toMigration: OwnMigration,
            MigrationsSqlGenerationOptions.Idempotent);
        script.Should().Contain("ADD duration_seconds", "the duration column is added")
            .And.NotContain("DROP TABLE", "additive only")
            .And.NotContain("DROP COLUMN", "…and nothing is dropped");
    }

    // ── helpers (the AssignmentInstructionsResponseKindsMigrationTests precedent) ──

    /// <summary>The pre-migration schema's <c>assignments.subject_coded_value_id</c> is NOT NULL while
    /// the EF model no longer maps it, so a raw insert has to relax it.</summary>
    private static Task RelaxLegacyNotNullAsync(string connectionString) =>
        ExecuteAsync(connectionString, "ALTER TABLE assignments ALTER COLUMN subject_coded_value_id DROP NOT NULL");

    private static Task SeedAssignmentAsync(string connectionString, Guid assignmentId) =>
        ExecuteAsync(
            connectionString,
            """
            INSERT INTO assignments
                (id, title, description, assignment_type, grading_format, target_audience_type,
                 topic_id, status, created_by_teacher_id, mandatory_review,
                 archive_grace_days, created_at, updated_at)
            VALUES
                (@id, 'Duration round-trip', NULL, 0, 0, 1, @topic, 0, @teacher, true, 30, now(), now())
            """,
            ("id", assignmentId), ("topic", TopicId), ("teacher", TeacherId));

    private static Task SeedQuestionAsync(
        string connectionString, Guid assignmentId, Guid questionId, int[] kinds) =>
        ExecuteAsync(
            connectionString,
            """
            INSERT INTO assignment_questions
                (id, assignment_id, question_text, question_type, display_order, response_kinds)
            VALUES
                (@id, @assignment, 'Explain the working.', 2, 0, @kinds)
            """,
            ("id", questionId), ("assignment", assignmentId), ("kinds", kinds));

    private static Task SeedInstructionAsync(string connectionString, Guid assignmentId, Guid questionId) =>
        ExecuteAsync(
            connectionString,
            """
            INSERT INTO assignment_instructions
                (id, assignment_id, question_id, kind, text, url, file_name, content_type,
                 file_size, storage_path, display_order)
            VALUES
                (@id, @assignment, @question, 1, NULL, NULL, 'how-to.mp3', 'audio/mpeg',
                 2048, 'tenants/t/staging/g/how-to.mp3', 0)
            """,
            ("id", Guid.NewGuid()), ("assignment", assignmentId), ("question", questionId));

    private static async Task ExecuteAsync(
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

        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<bool> ColumnExistsAsync(
        string connectionString, string tableName, string columnName) =>
        await ScalarAsync(connectionString,
            """
            SELECT count(*) FROM information_schema.columns
            WHERE table_name = @table AND column_name = @column
            """, ("table", tableName), ("column", columnName)) > 0;

    private static async Task<string> UdtNameAsync(
        string connectionString, string tableName, string columnName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT udt_name FROM information_schema.columns
            WHERE table_name = @table AND column_name = @column
            """;
        cmd.Parameters.AddWithValue("table", tableName);
        cmd.Parameters.AddWithValue("column", columnName);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<(string IsNullable, string ColumnDefault)> ColumnShapeAsync(
        string connectionString, string tableName, string columnName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT is_nullable, coalesce(column_default, '') FROM information_schema.columns
            WHERE table_name = @table AND column_name = @column
            """;
        cmd.Parameters.AddWithValue("table", tableName);
        cmd.Parameters.AddWithValue("column", columnName);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.GetString(1));
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
