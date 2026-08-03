using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class RenameStudentSubjectAssignmentsToStudentTopicAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FR-13: rename the StudentSubjectAssignment entity + table to
            // StudentTopicAssignment. The columns are unchanged (topic_id was
            // already introduced by the subject_id -> topic_id rename), so a
            // data-preserving RenameTable is all that is required. The indexes
            // travel with the table and are renamed to match.
            migrationBuilder.RenameTable(
                name: "student_subject_assignments",
                newName: "student_topic_assignments");

            migrationBuilder.RenameIndex(
                name: "ix_student_subject_assignments_tenant_unique",
                table: "student_topic_assignments",
                newName: "ix_student_topic_assignments_tenant_unique");

            migrationBuilder.RenameIndex(
                name: "ix_student_subject_assignments_tenant_period",
                table: "student_topic_assignments",
                newName: "ix_student_topic_assignments_tenant_period");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "ix_student_topic_assignments_tenant_period",
                table: "student_topic_assignments",
                newName: "ix_student_subject_assignments_tenant_period");

            migrationBuilder.RenameIndex(
                name: "ix_student_topic_assignments_tenant_unique",
                table: "student_topic_assignments",
                newName: "ix_student_subject_assignments_tenant_unique");

            migrationBuilder.RenameTable(
                name: "student_topic_assignments",
                newName: "student_subject_assignments");
        }
    }
}
