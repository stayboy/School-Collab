using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentInstructions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "instructions",
                table: "assignments",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "instructions",
                table: "assignments");
        }
    }
}
