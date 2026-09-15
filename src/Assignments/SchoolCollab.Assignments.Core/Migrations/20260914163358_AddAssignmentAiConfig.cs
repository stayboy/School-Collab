using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentAiConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "difficulty_easy_count",
                table: "assignments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "difficulty_hard_count",
                table: "assignments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "difficulty_medium_count",
                table: "assignments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "questions_draft_json",
                table: "assignments",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "difficulty_easy_count",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "difficulty_hard_count",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "difficulty_medium_count",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "questions_draft_json",
                table: "assignments");
        }
    }
}
