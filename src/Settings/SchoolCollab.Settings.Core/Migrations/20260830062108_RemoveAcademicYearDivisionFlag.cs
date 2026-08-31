using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Settings.Core.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAcademicYearDivisionFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "value",
                table: "tenant_flag_overrides");

            migrationBuilder.DropColumn(
                name: "value",
                table: "feature_flags");

            // Rev. 2 §8.2: the academic-year division moved onto the Students Period
            // entity. Delete the seeded FEATURE:AcademicYearDivision flag row (and its
            // tenant overrides) — division values are NOT migrated to any period
            // (existing years get the back-filled None; operators re-pick per year).
            // flag_audit_entries carries no FK to feature_flags, so its rows remain as
            // history.
            migrationBuilder.Sql(
                "DELETE FROM tenant_flag_overrides WHERE feature_flag_id IN " +
                "(SELECT id FROM feature_flags WHERE key = 'FEATURE:AcademicYearDivision');");
            migrationBuilder.Sql(
                "DELETE FROM feature_flags WHERE key = 'FEATURE:AcademicYearDivision';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "value",
                table: "tenant_flag_overrides",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "value",
                table: "feature_flags",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}
