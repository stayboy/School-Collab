using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Integration;

/// <summary>
/// R2 (documents/specs/assignment-authoring-compartments.md §7.4 TGT-14; round decision D-3) — the
/// <c>AddAssignmentTargets</c> migration's backfill. Discriminating by construction: the table and
/// the SQL do not exist on the pre-R2 base (<c>565e48f8</c>). The rules are an EXCLUSIVE precedence
/// (the legacy model enforces no exclusivity), the AllStudents guard is NULL-safe, and a re-run
/// inserts nothing.
/// </summary>
[TestClass]
public sealed class AssignmentTargetBackfillMigrationTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid GradeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid GroupA = Guid.Parse("55555555-5555-5555-5555-55555555555a");
    private static readonly Guid GroupB = Guid.Parse("55555555-5555-5555-5555-55555555555b");

    /// <summary>The migration immediately BEFORE <c>AddAssignmentTargets</c> — the legacy schema.</summary>
    private const string PreTargetsMigration = "20261001180607_AddAssignmentInstructions";

    /// <summary>The migration under test. The forward leg is pinned to it (round
    /// <c>drop-primary-grade</c>): the later <c>DropAssignmentGradeLevelColumn</c> removes
    /// <c>assignments.grade_level_id</c>, which the raw legacy seeding above and the verbatim
    /// backfill re-run below both address — a migrate-to-latest would break them, and this test is
    /// about the backfill that shipped with THIS migration.</summary>
    private const string AddTargetsMigration = "20261002065949_AddAssignmentTargets";

    private sealed record LegacyRow(
        Guid Id, int Audience, Guid? GradeLevelId, Guid[] GroupIds, string Title);

    [TestMethod]
    public async Task Backfill_AppliesTheExclusivePrecedence_AndIsIdempotent()
    {
        var connectionString = await AssignmentsDbFactory.CreateDatabaseAsync(Guid.NewGuid().ToString("N"));

        // ── Arrange: the legacy schema, then the legacy rows it can hold. ──
        await using (var pre = AssignmentsDbFactory.CreateContext(connectionString))
        {
            await pre.Database.MigrateAsync(PreTargetsMigration);
        }

        await using (var relax = new NpgsqlConnection(connectionString))
        {
            await relax.OpenAsync();
            await using var cmd = relax.CreateCommand();
            cmd.CommandText = "ALTER TABLE assignments ALTER COLUMN subject_coded_value_id DROP NOT NULL";
            await cmd.ExecuteNonQueryAsync();
        }

        var everyone = new LegacyRow(Guid.NewGuid(), 0, GradeId, [GroupA], "Everyone");
        var gradesOnly = new LegacyRow(Guid.NewGuid(), 1, GradeId, [], "Grades");
        var groupsOnly = new LegacyRow(Guid.NewGuid(), 2, null, [GroupA, GroupB], "Groups");
        // D-3 zero-backfill class: a Draft SelectedGroups row with no links and no grade.
        var zeroBackfill = new LegacyRow(Guid.NewGuid(), 2, null, [], "Zero");
        // An AllStudents row that ALSO carries a grade + links (rule 1 wins outright).
        var legacyMixed = new LegacyRow(Guid.NewGuid(), 0, null, [GroupA], "Mixed legacy");

        await using (var seed = new NpgsqlConnection(connectionString))
        {
            await seed.OpenAsync();
            foreach (var row in new[] { everyone, gradesOnly, groupsOnly, zeroBackfill, legacyMixed })
            {
                await InsertLegacyAssignmentAsync(seed, row);
                foreach (var groupId in row.GroupIds)
                {
                    await InsertLegacyLinkAsync(seed, row.Id, groupId);
                }
            }
        }

        // ── Act: apply the additive migration (table + indexes + the NOT EXISTS-guarded backfill). ──
        await using (var apply = AssignmentsDbFactory.CreateContext(connectionString))
        {
            await apply.Database.MigrateAsync(AddTargetsMigration);
        }

        // ── Assert: exactly the backfilled rows the precedence prescribes. ──
        await using (var assert = AssignmentsDbFactory.CreateContext(connectionString))
        {
            var rows = await assert.AssignmentTargets
                .IgnoreQueryFilters(["Tenant"])
                .AsNoTracking()
                .ToListAsync();

            Rows(rows, everyone.Id).Should().ContainSingle()
                .Which.Kind.Should().Be(TargetKind.AllStudents,
                    "rule 1 short-circuits — no grade, stream, student or group rows for an AllStudents row");
            Rows(rows, everyone.Id).Single().RefId.Should().BeNull();

            Rows(rows, legacyMixed.Id).Should().ContainSingle()
                .Which.Kind.Should().Be(TargetKind.AllStudents,
                    "rule 1 precedes the grade/group rules even when the legacy row also carried both");

            var gradeRows = Rows(rows, gradesOnly.Id);
            gradeRows.Should().ContainSingle()
                .Which.Kind.Should().Be(TargetKind.GradeLevel, "rule 2: the grade id becomes the grade target");
            gradeRows.Single().RefId.Should().Be(GradeId);

            var groupRows = Rows(rows, groupsOnly.Id).OrderBy(t => t.DisplayOrder).ToList();
            groupRows.Should().HaveCount(2, "rule 3: one ActivityGroup row per link");
            groupRows.Select(t => t.Kind).Should().AllBeEquivalentTo(TargetKind.ActivityGroup);
            groupRows.Select(t => t.DisplayOrder).Should().Equal(new[] { 0, 1 },
                "DisplayOrder 0..n-1 in created order");
            groupRows.Select(t => t.RefId).Should().BeEquivalentTo(new Guid?[] { GroupA, GroupB });

            Rows(rows, zeroBackfill.Id).Should().BeEmpty(
                "D-3: a Draft SelectedGroups row with no links and no grade id backfills to ZERO targets " +
                "(publish stays refused until it is re-authored)");
        }

        // ── Act 2: re-run the backfill block (the MigrationService retry path). ──
        var before = await CountTargetsAsync(connectionString);
        await RunBackfillAsync(connectionString);
        (await CountTargetsAsync(connectionString)).Should().Be(before,
            "every rule is NOT EXISTS-guarded — including the NULL-ref_id AllStudents row — so a retry is a no-op");
    }

    private static List<AssignmentTarget> Rows(List<AssignmentTarget> all, Guid assignmentId) =>
        all.Where(t => t.AssignmentId == assignmentId).ToList();

    private static async Task<int> CountTargetsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM assignment_targets";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    /// <summary>Re-executes the three backfill statements the migration ships (verbatim).</summary>
    private static async Task RunBackfillAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var sql in BackfillStatements)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static readonly string[] BackfillStatements =
    [
        """
        INSERT INTO assignment_targets
            (id, tenant_id, assignment_id, kind, ref_id, display_order, created_at, updated_at)
        SELECT gen_random_uuid(), a.tenant_id, a.id, 'AllStudents', NULL, 0, now(), now()
        FROM assignments a
        WHERE a.target_audience_type = 0
          AND NOT EXISTS (
              SELECT 1 FROM assignment_targets t
              WHERE t.assignment_id = a.id AND t.kind = 'AllStudents' AND t.ref_id IS NULL)
        """,
        """
        INSERT INTO assignment_targets
            (id, tenant_id, assignment_id, kind, ref_id, display_order, created_at, updated_at)
        SELECT gen_random_uuid(), a.tenant_id, a.id, 'GradeLevel', a.grade_level_id, 0, now(), now()
        FROM assignments a
        WHERE a.target_audience_type <> 0
          AND a.grade_level_id IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM assignment_targets t
              WHERE t.assignment_id = a.id AND t.kind = 'GradeLevel' AND t.ref_id = a.grade_level_id)
        """,
        """
        INSERT INTO assignment_targets
            (id, tenant_id, assignment_id, kind, ref_id, display_order, created_at, updated_at)
        SELECT gen_random_uuid(), a.tenant_id, l.assignment_id, 'ActivityGroup', l.activity_group_id,
               ROW_NUMBER() OVER (PARTITION BY l.assignment_id ORDER BY l.created_at, l.id) - 1,
               now(), now()
        FROM assignment_activity_groups l
        JOIN assignments a ON a.id = l.assignment_id
        WHERE a.target_audience_type = 2
          AND a.grade_level_id IS NULL
          AND NOT EXISTS (
              SELECT 1 FROM assignment_targets t
              WHERE t.assignment_id = l.assignment_id
                AND t.kind = 'ActivityGroup'
                AND t.ref_id = l.activity_group_id)
        """
    ];

    private static async Task InsertLegacyAssignmentAsync(NpgsqlConnection connection, LegacyRow row)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO assignments
                (id, title, description, assignment_type, grading_format, target_audience_type,
                 topic_id, grade_level_id, status, created_by_teacher_id, mandatory_review,
                 archive_grace_days, created_at, updated_at)
            VALUES
                (@id, @title, NULL, 0, 0, @audience, @topic, @grade, 0, @teacher, true, 30, now(), now())
            """;
        cmd.Parameters.AddWithValue("id", row.Id);
        cmd.Parameters.AddWithValue("title", row.Title);
        cmd.Parameters.AddWithValue("audience", row.Audience);
        cmd.Parameters.AddWithValue("topic", TopicId);
        cmd.Parameters.AddWithValue("grade", (object?)row.GradeLevelId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("teacher", TeacherId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertLegacyLinkAsync(NpgsqlConnection connection, Guid assignmentId, Guid groupId)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO assignment_activity_groups
                (id, tenant_id, assignment_id, activity_group_id, created_at, updated_at)
            VALUES (@id, @tenant, @assignment, @group, now(), now())
            """;
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("tenant", TenantId);
        cmd.Parameters.AddWithValue("assignment", assignmentId);
        cmd.Parameters.AddWithValue("group", groupId);
        await cmd.ExecuteNonQueryAsync();
    }
}
