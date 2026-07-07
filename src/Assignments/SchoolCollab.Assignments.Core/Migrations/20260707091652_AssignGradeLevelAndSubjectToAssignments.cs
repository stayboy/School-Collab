using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <summary>
    /// Adds the operational <c>subject_id</c> and <c>grade_level_id</c> columns to
    /// <c>assignments</c>, replacing the former coded-value id references. The old
    /// <c>subject_coded_value_id</c> / <c>grade_coded_value_id</c> columns (and the
    /// <c>ix_assignments_subject_cv_id</c> index) are INTENTIONALLY KEPT here so the
    /// MigrationService backfill step (<c>AssignmentBackfillService</c>) can read them
    /// and populate the new columns with the correct Subject/GradeLevel entity ids
    /// (find-or-create across the Students bounded context). The old columns become
    /// orphaned DB debt once every environment is backfilled; a future cleanup
    /// migration may drop them. See documents/specs/grade-level-setup.md §5.7.
    /// </summary>
    public partial class AssignGradeLevelAndSubjectToAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // New operational columns (nullable so existing rows don't violate the
            // constraint before the backfill populates them; the domain enforces
            // SubjectId required on new creates).
            migrationBuilder.AddColumn<Guid>(
                name: "subject_id",
                table: "assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "grade_level_id",
                table: "assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_assignments_subject_id",
                table: "assignments",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_grade_level_id",
                table: "assignments",
                column: "grade_level_id");

            // NOTE: subject_coded_value_id, grade_coded_value_id, and
            // ix_assignments_subject_cv_id are deliberately retained for the
            // MigrationService backfill (see class-level remarks).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_assignments_grade_level_id",
                table: "assignments");

            migrationBuilder.DropIndex(
                name: "ix_assignments_subject_id",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "grade_level_id",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "subject_id",
                table: "assignments");
        }
    }
}