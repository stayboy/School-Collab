using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <inheritdoc />
    public partial class RenameAssignmentSubjectToTopic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "subject_id",
                table: "assignments",
                newName: "topic_id");

            migrationBuilder.RenameIndex(
                name: "ix_assignments_subject_id",
                table: "assignments",
                newName: "ix_assignments_topic_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "topic_id",
                table: "assignments",
                newName: "subject_id");

            migrationBuilder.RenameIndex(
                name: "ix_assignments_topic_id",
                table: "assignments",
                newName: "ix_assignments_subject_id");
        }
    }
}
