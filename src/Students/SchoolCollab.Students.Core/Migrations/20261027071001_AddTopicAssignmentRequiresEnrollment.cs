using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTopicAssignmentRequiresEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // R1 (subject-enrollment-requirement-flag.md §2.2): the schema default exists
            // ONLY so existing rows can take a NOT NULL column. It must not survive — both
            // audiences share this table (TPH), so a surviving `DEFAULT false` would silently
            // contradict the activity-group default of true for every later insert.
            migrationBuilder.AddColumn<bool>(
                name: "requires_enrollment",
                table: "topic_assignments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill: an activity-group subject enrols explicitly (FR-ER-2). Expected to
            // touch ~0 rows — the group↔subject bridge is built but unexercised (§11) — so
            // this is correctness, not data volume.
            migrationBuilder.Sql("""
                UPDATE topic_assignments
                SET requires_enrollment = true
                WHERE topic_assignment_type = 'activity_group';
                """);

            // Drop the transient default so the column's value only ever comes from the two
            // factories (GradeTopicAssignment.Create => false, ActivityGroupTopicAssignment.Create
            // => true) and from the switch command. No HasDefaultValue on the model either.
            migrationBuilder.Sql("""
                ALTER TABLE topic_assignments ALTER COLUMN requires_enrollment DROP DEFAULT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "requires_enrollment",
                table: "topic_assignments");
        }
    }
}
