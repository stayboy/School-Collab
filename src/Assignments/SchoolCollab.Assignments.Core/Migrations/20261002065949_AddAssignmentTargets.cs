using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assignment_targets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ref_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_targets", x => x.id);
                    table.ForeignKey(
                        name: "fk_assignment_targets_assignments_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_targets_tenant_kind_ref",
                table: "assignment_targets",
                columns: new[] { "tenant_id", "kind", "ref_id" });

            migrationBuilder.CreateIndex(
                name: "uq_assignment_targets_assignment_kind_ref",
                table: "assignment_targets",
                columns: new[] { "assignment_id", "kind", "ref_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_assignment_targets_all_students",
                table: "assignment_targets",
                columns: new[] { "assignment_id", "kind", "ref_id" },
                unique: true,
                filter: "\"kind\" = 'AllStudents' AND \"ref_id\" IS NULL");

            // D-3 (round assignment-targeting-r2): backfill the legacy single-choice model
            // into target rows. The rules are an EXCLUSIVE PRECEDENCE — first matching rule
            // wins and only its rows are inserted — because the legacy model never enforced
            // exclusivity (a row may carry AllStudents + a grade id + group links at once).
            // The guards make a re-run (MigrationService retry after a mid-migration
            // failure) a no-op; the AllStudents guard is NULL-safe, since a plain
            // (assignment_id, kind, ref_id) equality never matches a NULL ref_id.
            //
            // Rule 1 — TargetAudienceType = AllStudents (0) ⇒ exactly one AllStudents row.
            migrationBuilder.Sql(
                """
                INSERT INTO assignment_targets
                    (id, tenant_id, assignment_id, kind, ref_id, display_order, created_at, updated_at)
                SELECT gen_random_uuid(), a.tenant_id, a.id, 'AllStudents', NULL, 0, now(), now()
                FROM assignments a
                WHERE a.target_audience_type = 0
                  AND NOT EXISTS (
                      SELECT 1 FROM assignment_targets t
                      WHERE t.assignment_id = a.id AND t.kind = 'AllStudents' AND t.ref_id IS NULL)
                """);

            // Rule 2 — SelectedGrades (1), or a non-AllStudents row carrying a grade id ⇒
            // exactly one GradeLevel row. Grade precedes groups, so a legacy
            // grade + group-links row backfills only the grade row and stays valid under
            // SetTargets. Rule 4 (a SelectedGrades row with no grade id) inserts nothing —
            // the create/update guard makes it impossible anyway.
            migrationBuilder.Sql(
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
                """);

            // Rule 3 — SelectedGroups (2) with no grade id ⇒ one ActivityGroup row per link,
            // ordered by created_at then id and inserted in DisplayOrder 0..n-1. A Draft
            // SelectedGroups row with zero links and no grade id backfills to ZERO targets —
            // accepted (D-3): publish stays refused until it is re-authored (TGT-13).
            migrationBuilder.Sql(
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignment_targets");
        }
    }
}
