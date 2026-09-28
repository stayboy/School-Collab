using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSubjectEnrollmentExceptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subject_enrollment_exceptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    grade_level_id = table.Column<Guid>(type: "uuid", nullable: true),
                    activity_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    topic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    division = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subject_enrollment_exceptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_subject_enrollment_exceptions_activity_groups_activity_grou",
                        column: x => x.activity_group_id,
                        principalTable: "activity_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_subject_enrollment_exceptions_grade_levels_grade_level_id",
                        column: x => x.grade_level_id,
                        principalTable: "grade_levels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_subject_enrollment_exceptions_subjects_topic_id",
                        column: x => x.topic_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subject_enrollment_exceptions_activity_group_id",
                table: "subject_enrollment_exceptions",
                column: "activity_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_subject_enrollment_exceptions_grade_level_id",
                table: "subject_enrollment_exceptions",
                column: "grade_level_id");

            migrationBuilder.CreateIndex(
                name: "ix_subject_enrollment_exceptions_tenant_grade_topic_division",
                table: "subject_enrollment_exceptions",
                columns: new[] { "tenant_id", "grade_level_id", "topic_id", "division" });

            migrationBuilder.CreateIndex(
                name: "ix_subject_enrollment_exceptions_tenant_group_topic_division",
                table: "subject_enrollment_exceptions",
                columns: new[] { "tenant_id", "activity_group_id", "topic_id", "division" });

            migrationBuilder.CreateIndex(
                name: "ix_subject_enrollment_exceptions_topic_id",
                table: "subject_enrollment_exceptions",
                column: "topic_id");

            // ── The unique key the EF model cannot express (spec §2.2/§7) ─────────
            // Both span bounds are nullable, and Postgres treats NULLs as DISTINCT, so a
            // plain unique index on (…, start_date, end_date) would let the same
            // open-ended exception be inserted twice. COALESCE collapses "open" onto the
            // -infinity / infinity sentinels, and the is_deleted = false filter keeps
            // remove-then-re-add legal. One index per owner column, because each owner
            // column is NULL for the other owner form.
            //
            // Deliberately raw SQL: these indexes are invisible to the EF model, so
            // MigrationGuardTests.NoUncommittedModelChanges stays green, and Down() must
            // drop them BY NAME before DropTable.
            //
            // The shorter ix_enrollment_exceptions_… prefix on these two indexes (rather
            // than the model-derived ix_subject_enrollment_exceptions_…) is DELIBERATE, not
            // an oversight: Postgres silently truncates identifiers at 63 bytes, and the
            // full expression key is long, so these names stay safely inside that limit.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ix_enrollment_exceptions_tenant_grade_topic_span
                  ON subject_enrollment_exceptions
                     (tenant_id, grade_level_id, topic_id, division,
                      COALESCE(start_date, '-infinity'), COALESCE(end_date, 'infinity'))
                  WHERE grade_level_id IS NOT NULL AND is_deleted = false;
                """);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ix_enrollment_exceptions_tenant_group_topic_span
                  ON subject_enrollment_exceptions
                     (tenant_id, activity_group_id, topic_id, division,
                      COALESCE(start_date, '-infinity'), COALESCE(end_date, 'infinity'))
                  WHERE activity_group_id IS NOT NULL AND is_deleted = false;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The two raw-SQL indexes are not part of the EF model, so DropTable knows
            // nothing about them — drop them by name first (spec §7).
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_enrollment_exceptions_tenant_group_topic_span;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_enrollment_exceptions_tenant_grade_topic_span;");

            migrationBuilder.DropTable(
                name: "subject_enrollment_exceptions");
        }
    }
}
