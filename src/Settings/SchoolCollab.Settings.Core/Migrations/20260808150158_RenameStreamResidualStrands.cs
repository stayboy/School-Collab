using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Settings.Core.Migrations
{
    /// <inheritdoc />
    public partial class RenameStreamResidualStrands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Corrective: the earlier RenameGradeStrandParentCodeToStream migration
            // was applied to existing databases in its initial (parent-only) form, so
            // stream children kept their GRSTRNDS_* codes. Rename any residuals so the
            // updated seed data (GRSTREAMS_*) stays idempotent instead of inserting
            // duplicates. No-op on fresh databases (no GRSTRNDS_* exist yet).
            migrationBuilder.Sql(
                "UPDATE coded_values SET code = REPLACE(code, 'GRSTRNDS_', 'GRSTREAMS_') " +
                "WHERE code LIKE 'GRSTRNDS\\_%';");

            // Corrective: RenameStrandVersionAttributeToStreamVersion renamed the
            // attribute VALUE key (coded_value_attributes) but missed the attribute
            // DEFINITION key (coded_value_attribute_definitions). Rename residuals so
            // the seeder's streamVersion definition is idempotent. No-op on fresh DBs.
            migrationBuilder.Sql(
                "UPDATE coded_value_attribute_definitions SET key = 'streamVersion' " +
                "WHERE key = 'strandVersion';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE coded_values SET code = REPLACE(code, 'GRSTREAMS_', 'GRSTRNDS_') " +
                "WHERE code LIKE 'GRSTREAMS\\_%';");
            migrationBuilder.Sql(
                "UPDATE coded_value_attribute_definitions SET key = 'strandVersion' " +
                "WHERE key = 'streamVersion';");
        }
    }
}