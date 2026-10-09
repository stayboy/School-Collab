using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace SchoolCollab.Assignments.Tests.Integration;

/// <summary>
/// QR-2/QR-5 (spec <c>question-response-types</c> §5.7) — the
/// <c>AddAssignmentInstructionsAndResponseKinds</c> migration on REAL Postgres/Npgsql (AC-3). The
/// in-memory provider has no catalogue to inspect and no notion of a column default, and this
/// migration's claims are catalogue shapes plus two data claims ("every existing question simply has
/// no kinds" and "an assignment's instructions go with it"), so the test MUST run against Postgres —
/// the <c>AssignmentContextPicksMigrationTests</c> precedent.
///
/// <para><b>Discriminating:</b> the schema immediately before this migration carries neither the
/// instruction table nor the response-kind column — both asserted ABSENT first — and the seeded
/// pre-migration assignment is asserted through SQL afterwards, so the migration cannot pass by
/// having rebuilt the row. The migration's own script is also asserted additive-only.</para>
/// </summary>
[TestClass]
public sealed class AssignmentInstructionsResponseKindsMigrationTests
{
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>The migration immediately BEFORE this one — the last schema with no instruction table
    /// and no response-kind column.</summary>
    private const string PreMigration = "20261007195749_AddAssignmentContextPicks";

    [TestMethod]
    public async Task AddInstructionsAndKinds_AddsTheColumnAndTable_KeepsRows_AndCascades()
    {
        var connectionString = await AssignmentsDbFactory.CreateDatabaseAsync(Guid.NewGuid().ToString("N"));

        // ── Arrange: the pre-migration schema + one real assignment row. ──
        await using (var pre = AssignmentsDbFactory.CreateContext(connectionString))
        {
            await pre.Database.MigrateAsync(PreMigration);
        }

        await RelaxLegacyNotNullAsync(connectionString);
        var assignmentId = Guid.NewGuid();
        await SeedAssignmentAsync(connectionString, assignmentId);

        (await TableExistsAsync(connectionString, "assignment_instructions")).Should().BeFalse(
            "premise: the pre-migration schema has no instruction table — otherwise the assertions below are vacuous");
        (await ColumnExistsAsync(connectionString, "assignment_questions", "response_kinds")).Should().BeFalse(
            "premise: …and no response-kind column");

        // ── Act: migrate to the tip. ──
        await using (var apply = AssignmentsDbFactory.CreateContext(connectionString))
        {
            await apply.Database.MigrateAsync();
        }

        // ── Assert: the column — additive, NOT NULL, defaulting to the empty array (the backfill). ──
        (await ColumnExistsAsync(connectionString, "assignment_questions", "response_kinds")).Should().BeTrue(
            "AC-3: the migration adds assignment_questions.response_kinds");
        (await UdtNameAsync(connectionString, "assignment_questions", "response_kinds")).Should().Be("_int4",
            "the kinds are a first-class integer[] — no join table, no converter (round Q1a)");

        var (kindsNullable, kindsDefault) = await ColumnShapeAsync(
            connectionString, "assignment_questions", "response_kinds");
        kindsNullable.Should().Be("NO",
            "the CLR property is non-nullable, so \"no kinds recorded\" has exactly one representation");
        // Postgres normalizes the empty-array literal to `ARRAY[]::integer[]` (what EF's
        // `defaultValue: new int[0]` renders as) — either spelling is the empty int array.
        kindsDefault.Should().Contain("ARRAY[]::integer[]",
            "…and defaulting to the empty array, which is what backfills every existing question in " +
            "the one ADD COLUMN");

        // ── Assert: the instruction table, its nullable-owner discriminator, its indexes and FK. ──
        (await TableExistsAsync(connectionString, "assignment_instructions")).Should().BeTrue();
        foreach (var column in new[]
                 {
                     "id", "assignment_id", "question_id", "kind", "text", "url",
                     "file_name", "content_type", "file_size", "storage_path", "display_order",
                 })
        {
            (await ColumnExistsAsync(connectionString, "assignment_instructions", column)).Should().BeTrue(
                $"AC-3: an instruction row carries {column}");
        }

        (await ColumnNullableAsync(connectionString, "assignment_instructions", "question_id")).Should().Be("YES",
            "null means the instruction belongs to the ASSIGNMENT — the owner discriminator (§5.6)");
        (await ColumnNullableAsync(connectionString, "assignment_instructions", "assignment_id")).Should().Be("NO");
        (await IndexExistsAsync(connectionString, "ix_assignment_instructions_assignment_id")).Should().BeTrue();
        (await IndexExistsAsync(connectionString, "ix_assignment_instructions_question_id")).Should().BeTrue(
            "the soft question link is indexed — the read groups by it");
        (await ForeignKeyDeleteRuleAsync(connectionString, "fk_assignment_instructions_assignments_assignment_id"))
            .Should().Be("CASCADE", "deleting an assignment takes its instruction rows with it");

        // ── Assert: the pre-migration row survived with no kinds (nothing authored them before). ──
        (await ScalarAsync(connectionString, "SELECT count(*) FROM assignments WHERE id = @id", ("id", assignmentId)))
            .Should().Be(1, "adding a column and a table must not delete assignment rows");

        // ── Assert: the integer[] round-trips the enum values the domain stores. ──
        var questionId = Guid.NewGuid();
        await SeedQuestionAsync(connectionString, assignmentId, questionId, kinds: new[] { 3, 4 });
        (await KindArrayAsync(connectionString, questionId)).Should().Equal([3, 4],
            "the enum values round-trip through the array column");

        // ── Assert: the cascade, through a real row. ──
        await SeedInstructionAsync(connectionString, assignmentId, questionId);
        (await ScalarAsync(connectionString, "SELECT count(*) FROM assignment_instructions")).Should().Be(1);
        await ExecuteAsync(connectionString, "DELETE FROM assignments WHERE id = @id", ("id", assignmentId));
        (await ScalarAsync(connectionString, "SELECT count(*) FROM assignment_instructions")).Should().Be(0,
            "the assignment FK cascades");
    }

    [TestMethod]
    public async Task MigrationScript_IsAdditiveOnly()
    {
        var connectionString = await AssignmentsDbFactory.CreateDatabaseAsync(Guid.NewGuid().ToString("N"));
        await using (var pre = AssignmentsDbFactory.CreateContext(connectionString))
        {
            await pre.Database.MigrateAsync(PreMigration);
        }

        await using var context = AssignmentsDbFactory.CreateContext(connectionString);
        var script = context.GetService<IMigrator>().GenerateScript(
            fromMigration: PreMigration,
            toMigration: null,
            MigrationsSqlGenerationOptions.Idempotent);

        script.Should().Contain(
                "ALTER TABLE assignment_questions ADD response_kinds integer[] NOT NULL DEFAULT ARRAY[]::integer[]",
                "the kinds column is an additive NOT NULL integer[] defaulting to the empty array")
            .And.Contain("CREATE TABLE assignment_instructions", "the instruction table is new")
            .And.NotContain("DROP TABLE", "additive only")
            .And.NotContain("DROP COLUMN", "…and nothing is dropped");
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>The pre-migration schema's <c>assignments.subject_coded_value_id</c> is NOT NULL while
    /// the EF model no longer maps it, so a raw insert has to relax it (the picks-test precedent).</summary>
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
                (@id, 'Instructions round-trip', NULL, 0, 0, 1, @topic, 0, @teacher, true, 30, now(), now())
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
                (@id, @assignment, @question, 2, NULL, NULL, 'how-to.mp3', 'audio/mpeg',
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

    private static async Task<bool> TableExistsAsync(string connectionString, string tableName) =>
        await ScalarAsync(connectionString,
            "SELECT count(*) FROM information_schema.tables WHERE table_name = @table",
            ("table", tableName)) > 0;

    private static async Task<bool> ColumnExistsAsync(
        string connectionString, string tableName, string columnName) =>
        await ScalarAsync(connectionString,
            """
            SELECT count(*) FROM information_schema.columns
            WHERE table_name = @table AND column_name = @column
            """, ("table", tableName), ("column", columnName)) > 0;

    private static async Task<bool> IndexExistsAsync(string connectionString, string indexName) =>
        await ScalarAsync(connectionString,
            "SELECT count(*) FROM pg_indexes WHERE indexname = @index",
            ("index", indexName)) > 0;

    private static async Task<string> ColumnNullableAsync(
        string connectionString, string tableName, string columnName)
    {
        var (isNullable, _) = await ColumnShapeAsync(connectionString, tableName, columnName);
        return isNullable;
    }

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

    private static async Task<string> ForeignKeyDeleteRuleAsync(string connectionString, string constraintName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT delete_rule FROM information_schema.referential_constraints
            WHERE constraint_name = @name
            """;
        cmd.Parameters.AddWithValue("name", constraintName);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<int[]> KindArrayAsync(string connectionString, Guid questionId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT response_kinds FROM assignment_questions WHERE id = @id";
        cmd.Parameters.AddWithValue("id", questionId);
        return (int[])(await cmd.ExecuteScalarAsync())!;
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
