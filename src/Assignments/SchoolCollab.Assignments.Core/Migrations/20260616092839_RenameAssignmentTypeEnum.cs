using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <inheritdoc />
    public partial class RenameAssignmentTypeEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No schema change — enum values were renamed (Digital→Online, SemiManual→Hybrid,
            // Manual→Offline) but underlying integer column values (0, 1, 2) are unchanged.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: reverse of a no-op migration.
        }
    }
}
