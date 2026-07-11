using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotionOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "promotion_outcome",
                table: "student_enrollments",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "promotion_outcome",
                table: "student_enrollments");
        }
    }
}
