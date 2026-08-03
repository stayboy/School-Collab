using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class SplitGradeSubjectAssignmentIntoTopicAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TPH split of GradeSubjectAssignment into GradeTopicAssignment /
            // ActivityGroupTopicAssignment — ONE physical table via a
            // discriminator. Preserves all existing rows (data migration).

            // 1. Rename the table to the new TPH root name.
            migrationBuilder.RenameTable(
                name: "grade_subject_assignments",
                newName: "topic_assignments");

            // 2. Add the discriminator column (nullable so we can backfill).
            migrationBuilder.AddColumn<string>(
                name: "topic_assignment_type",
                table: "topic_assignments",
                type: "character varying(21)",
                maxLength: 21,
                nullable: true);

            // 3. Backfill the discriminator from the existing data: a row is a
            //    grade-level assignment when grade_level_id is set, otherwise it
            //    is an activity-group assignment.
            migrationBuilder.Sql(
                """
                UPDATE topic_assignments
                SET topic_assignment_type = 'grade'
                WHERE grade_level_id IS NOT NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE topic_assignments
                SET topic_assignment_type = 'activity_group'
                WHERE topic_assignment_type IS NULL;
                """);

            // 4. The discriminator is now required.
            migrationBuilder.AlterColumn<string>(
                name: "topic_assignment_type",
                table: "topic_assignments",
                type: "character varying(21)",
                maxLength: 21,
                nullable: false,
                defaultValue: "grade");

            // 5. Drop the old ineffective composite unique index (NULLs are
            //    distinct in Postgres, so it could not prevent duplicates).
            migrationBuilder.DropIndex(
                name: "ix_grade_subject_assignments_tenant_unique",
                table: "topic_assignments");

            // 6. Rename the shared indexes / PK / FKs to the new root name.
            migrationBuilder.RenameIndex(
                name: "ix_grade_subject_assignments_topic_lesson_id",
                table: "topic_assignments",
                newName: "ix_topic_assignments_topic_lesson_id");

            migrationBuilder.RenameIndex(
                name: "ix_grade_subject_assignments_topic_strand_id",
                table: "topic_assignments",
                newName: "ix_topic_assignments_topic_strand_id");

            migrationBuilder.RenameIndex(
                name: "ix_grade_subject_assignments_tenant_effective_dates",
                table: "topic_assignments",
                newName: "ix_topic_assignments_tenant_effective_dates");

            migrationBuilder.Sql(
                """ALTER TABLE "topic_assignments" RENAME CONSTRAINT "pk_grade_subject_assignments" TO "pk_topic_assignments";""");

            migrationBuilder.Sql(
                """ALTER TABLE "topic_assignments" RENAME CONSTRAINT "fk_grade_subject_assignments_topic_lessons_topic_lesson_id" TO "fk_topic_assignments_topic_lessons_topic_lesson_id";""");

            migrationBuilder.Sql(
                """ALTER TABLE "topic_assignments" RENAME CONSTRAINT "fk_grade_subject_assignments_topic_strands_topic_strand_id" TO "fk_topic_assignments_topic_strands_topic_strand_id";""");

            // 7. Add indexes + audience FKs (grade_levels / activity_groups)
            //    which were not previously constrained.
            migrationBuilder.CreateIndex(
                name: "ix_topic_assignments_activity_group_id",
                table: "topic_assignments",
                column: "activity_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_topic_assignments_grade_level_id",
                table: "topic_assignments",
                column: "grade_level_id");

            migrationBuilder.AddForeignKey(
                name: "fk_topic_assignments_activity_groups_activity_group_id",
                table: "topic_assignments",
                column: "activity_group_id",
                principalTable: "activity_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_topic_assignments_grade_levels_grade_level_id",
                table: "topic_assignments",
                column: "grade_level_id",
                principalTable: "grade_levels",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // 8. Per-subtype filtered unique indexes — the real duplicate
            //    prevention (the discriminator scopes each index to one
            //    audience, and NULLs in the non-audience column don't collide).
            migrationBuilder.CreateIndex(
                name: "ix_topic_assignments_tenant_grade_topic_unique",
                table: "topic_assignments",
                columns: new[] { "tenant_id", "grade_level_id", "topic_id" },
                unique: true,
                filter: "\"topic_assignment_type\" = 'grade'");

            migrationBuilder.CreateIndex(
                name: "ix_topic_assignments_tenant_group_topic_unique",
                table: "topic_assignments",
                columns: new[] { "tenant_id", "activity_group_id", "topic_id" },
                unique: true,
                filter: "\"topic_assignment_type\" = 'activity_group'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the filtered unique indexes + new audience FKs first.
            migrationBuilder.DropIndex(
                name: "ix_topic_assignments_tenant_group_topic_unique",
                table: "topic_assignments");

            migrationBuilder.DropIndex(
                name: "ix_topic_assignments_tenant_grade_topic_unique",
                table: "topic_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_topic_assignments_grade_levels_grade_level_id",
                table: "topic_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_topic_assignments_activity_groups_activity_group_id",
                table: "topic_assignments");

            migrationBuilder.DropIndex(
                name: "ix_topic_assignments_grade_level_id",
                table: "topic_assignments");

            migrationBuilder.DropIndex(
                name: "ix_topic_assignments_activity_group_id",
                table: "topic_assignments");

            // Recreate the legacy composite unique index.
            migrationBuilder.CreateIndex(
                name: "ix_grade_subject_assignments_tenant_unique",
                table: "topic_assignments",
                columns: new[] { "tenant_id", "grade_level_id", "activity_group_id", "topic_id" },
                unique: true);

            migrationBuilder.Sql(
                """ALTER TABLE "topic_assignments" RENAME CONSTRAINT "fk_topic_assignments_topic_strands_topic_strand_id" TO "fk_grade_subject_assignments_topic_strands_topic_strand_id";""");

            migrationBuilder.Sql(
                """ALTER TABLE "topic_assignments" RENAME CONSTRAINT "fk_topic_assignments_topic_lessons_topic_lesson_id" TO "fk_grade_subject_assignments_topic_lessons_topic_lesson_id";""");

            migrationBuilder.Sql(
                """ALTER TABLE "topic_assignments" RENAME CONSTRAINT "pk_topic_assignments" TO "pk_grade_subject_assignments";""");

            migrationBuilder.RenameIndex(
                name: "ix_topic_assignments_tenant_effective_dates",
                table: "topic_assignments",
                newName: "ix_grade_subject_assignments_tenant_effective_dates");

            migrationBuilder.RenameIndex(
                name: "ix_topic_assignments_topic_strand_id",
                table: "topic_assignments",
                newName: "ix_grade_subject_assignments_topic_strand_id");

            migrationBuilder.RenameIndex(
                name: "ix_topic_assignments_topic_lesson_id",
                table: "topic_assignments",
                newName: "ix_grade_subject_assignments_topic_lesson_id");

            migrationBuilder.DropColumn(
                name: "topic_assignment_type",
                table: "topic_assignments");

            migrationBuilder.RenameTable(
                name: "topic_assignments",
                newName: "grade_subject_assignments");
        }
    }
}
