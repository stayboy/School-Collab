using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace SchoolCollab.Assignments.Tests.Integration;

/// <summary>
/// Round <c>drop-primary-grade</c> — the <c>DropAssignmentGradeLevelColumn</c> migration's schema
/// round-trip on REAL Postgres/Npgsql (AC-3). The in-memory provider has no catalog to inspect, and
/// the migration's whole point is a catalogue change plus a data-survival claim, so this test MUST run
/// against Postgres.
///
/// <para><b>Discriminating:</b> the pre-drop schema carries <c>assignments.grade_level_id</c> and
/// <c>ix_assignments_grade_level_id</c> — both asserted PRESENT before the migration runs — and the
/// seeded assignment + its <c>GradeLevel</c> target row are asserted directly through SQL afterwards, so
/// the drop cannot pass by having deleted the rows too (or by having been satisfied by the EF model
/// alone).</para>
/// </summary>
[TestClass]
public sealed class DropAssignmentGradeLevelColumnMigrationTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid GradeId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>The migration immediately BEFORE the drop — the last schema that still carries the
    /// legacy <c>grade_level_id</c> column, so the round-trip can start from real legacy data.</summary>
    private const string PreDropMigration = "20261002065949_AddAssignmentTargets";

    [TestMethod]
    public async Task Drop_RemovesTheGradeLevelColumnAndIndex_AndKeepsRowsAndTargets()
    {
        var connectionString = await AssignmentsDbFactory.CreateDatabaseAsync(Guid.NewGuid().ToString("N"));

        // ── Arrange: the pre-drop schema + the legacy rows it can hold. ──
        await using (var pre = AssignmentsDbFactory.CreateContext(connectionString))
        {
            await pre.Database.MigrateAsync(PreDropMigration);
        }

        await RelaxLegacyNotNullAsync(connectionString);

        var assignmentId = Guid.NewGuid();
        await SeedLegacyAssignmentAsync(connectionString, assignmentId);

        (await ColumnExistsAsync(connectionString)).Should().BeTrue(
            "premise: the pre-drop schema carries assignments.grade_level_id");
        (await IndexExistsAsync(connectionString)).Should().BeTrue(
            "premise: …and its index — otherwise the drop assertions below are vacuous");

        // ── Act: apply the drop migration (migrate to latest). ──
        await using (var apply = AssignmentsDbFactory.CreateContext(connectionString))
        {
            await apply.Database.MigrateAsync();
        }

        // ── Assert: the catalogue change, and the rows that must survive it. ──
        (await ColumnExistsAsync(connectionString)).Should().BeFalse(
            "AC-3: the drop migration removes the assignment's authored primary grade column");
        (await IndexExistsAsync(connectionString)).Should().BeFalse(
            "AC-3: …and its index, or the drop leaves a dangling index over a missing column");

        (await ScalarAsync(connectionString,
                "SELECT count(*) FROM assignments WHERE id = @id", ("id", assignmentId))).Should().Be(1,
            "dropping a column must not delete the assignment rows");
        (await ScalarAsync(connectionString,
                "SELECT count(*) FROM assignment_targets WHERE assignment_id = @id", ("id", assignmentId))).Should().Be(1,
            "…nor their authored targets — the target rows ARE the assignment's grade scope now");
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>The pre-drop schema's <c>assignments.subject_coded_value_id</c> is NOT NULL while the
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

    /// <summary>One legacy assignment carrying a grade, plus the <c>GradeLevel</c> target row the
    /// <c>AddAssignmentTargets</c> backfill would have given it.</summary>
    private static async Task SeedLegacyAssignmentAsync(string connectionString, Guid assignmentId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using (var assignment = connection.CreateCommand())
        {
            assignment.CommandText =
                """
                INSERT INTO assignments
                    (id, title, description, assignment_type, grading_format, target_audience_type,
                     topic_id, grade_level_id, status, created_by_teacher_id, mandatory_review,
                     archive_grace_days, created_at, updated_at)
                VALUES
                    (@id, 'Drop round-trip', NULL, 0, 0, 1, @topic, @grade, 0, @teacher, true, 30, now(), now())
                """;
            assignment.Parameters.AddWithValue("id", assignmentId);
            assignment.Parameters.AddWithValue("topic", TopicId);
            assignment.Parameters.AddWithValue("grade", GradeId);
            assignment.Parameters.AddWithValue("teacher", TeacherId);
            await assignment.ExecuteNonQueryAsync();
        }

        await using var target = connection.CreateCommand();
        target.CommandText =
            """
            INSERT INTO assignment_targets
                (id, tenant_id, assignment_id, kind, ref_id, display_order, created_at, updated_at)
            VALUES (@id, @tenant, @assignment, 'GradeLevel', @grade, 0, now(), now())
            """;
        target.Parameters.AddWithValue("id", Guid.NewGuid());
        target.Parameters.AddWithValue("tenant", TenantId);
        target.Parameters.AddWithValue("assignment", assignmentId);
        target.Parameters.AddWithValue("grade", GradeId);
        await target.ExecuteNonQueryAsync();
    }

    private static async Task<bool> ColumnExistsAsync(string connectionString) =>
        await ScalarAsync(connectionString,
            """
            SELECT count(*) FROM information_schema.columns
            WHERE table_name = 'assignments' AND column_name = 'grade_level_id'
            """) > 0;

    private static async Task<bool> IndexExistsAsync(string connectionString) =>
        await ScalarAsync(connectionString,
            """
            SELECT count(*) FROM pg_catalog.pg_indexes
            WHERE tablename = 'assignments' AND indexname = 'ix_assignments_grade_level_id'
            """) > 0;

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
