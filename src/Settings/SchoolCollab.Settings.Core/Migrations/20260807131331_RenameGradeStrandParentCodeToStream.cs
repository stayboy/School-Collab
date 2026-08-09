using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Settings.Core.Migrations
{
    /// <inheritdoc />
    public partial class RenameGradeStrandParentCodeToStream : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename the grade-stream parent coded value's code from GRSTRNDS to
            // GRSTREAMS to match the renamed concept (grade strands → streams).
            migrationBuilder.Sql("UPDATE coded_values SET code = 'GRSTREAMS' WHERE code = 'GRSTRNDS';");

            // Rename the stream children too (GRSTRNDS_0A → GRSTREAMS_0A, …) so the
            // updated seed data (which now uses GRSTREAMS_*) does not insert duplicates
            // on an existing database.
            migrationBuilder.Sql(
                "UPDATE coded_values SET code = REPLACE(code, 'GRSTRNDS_', 'GRSTREAMS_') " +
                "WHERE code LIKE 'GRSTRNDS\\_%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE coded_values SET code = 'GRSTRNDS' WHERE code = 'GRSTREAMS';");

            migrationBuilder.Sql(
                "UPDATE coded_values SET code = REPLACE(code, 'GRSTREAMS_', 'GRSTRNDS_') " +
                "WHERE code LIKE 'GRSTREAMS\\_%';");
        }
    }
}
