using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Settings.Core.Migrations
{
    /// <inheritdoc />
    public partial class RenameStrandVersionAttributeToStreamVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename the grade-stream version attribute key from strandVersion to
            // streamVersion to match the renamed concept (grade strands → streams).
            migrationBuilder.Sql("UPDATE coded_value_attributes SET key = 'streamVersion' WHERE key = 'strandVersion';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE coded_value_attributes SET key = 'strandVersion' WHERE key = 'streamVersion';");
        }
    }
}
