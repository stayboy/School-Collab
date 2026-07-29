using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Settings.Core.Migrations
{
    /// <summary>
    /// Data migration: renumbers <c>display_order</c> on GRADE-coded values from the
    /// old 1-13 scheme to 0-12 so that DisplayOrder IS the grade level (no separate
    /// Level column). See spec §4.1: GRADE_R=0, GRADE_1=1, ..., GRADE_12=12.
    /// </summary>
    public partial class RenumberGradeDisplayOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Shift each GRADE child's display_order down by 1.
            // GRADE_R: 1 -> 0, GRADE_1: 2 -> 1, ..., GRADE_12: 13 -> 12.
            // The grade_levels table mirrors this column, so we update it too.
            migrationBuilder.Sql("""
                UPDATE coded_values
                SET display_order = display_order - 1
                WHERE parent_id = (SELECT id FROM coded_values WHERE code = 'GRADE' AND parent_id IS NULL)
                  AND code IN ('GRADE_R', 'GRADE_1', 'GRADE_2', 'GRADE_3', 'GRADE_4',
                               'GRADE_5', 'GRADE_6', 'GRADE_7', 'GRADE_8', 'GRADE_9',
                               'GRADE_10', 'GRADE_11', 'GRADE_12');
            """);

            migrationBuilder.Sql("""
                UPDATE grade_levels
                SET "level" = "level" - 1
                WHERE coded_value_id IN (
                    SELECT id FROM coded_values
                    WHERE code IN ('GRADE_R', 'GRADE_1', 'GRADE_2', 'GRADE_3', 'GRADE_4',
                                   'GRADE_5', 'GRADE_6', 'GRADE_7', 'GRADE_8', 'GRADE_9',
                                   'GRADE_10', 'GRADE_11', 'GRADE_12')
                );
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE coded_values
                SET display_order = display_order + 1
                WHERE parent_id = (SELECT id FROM coded_values WHERE code = 'GRADE' AND parent_id IS NULL)
                  AND code IN ('GRADE_R', 'GRADE_1', 'GRADE_2', 'GRADE_3', 'GRADE_4',
                               'GRADE_5', 'GRADE_6', 'GRADE_7', 'GRADE_8', 'GRADE_9',
                               'GRADE_10', 'GRADE_11', 'GRADE_12');
            """);

            migrationBuilder.Sql("""
                UPDATE grade_levels
                SET "level" = "level" + 1
                WHERE coded_value_id IN (
                    SELECT id FROM coded_values
                    WHERE code IN ('GRADE_R', 'GRADE_1', 'GRADE_2', 'GRADE_3', 'GRADE_4',
                                   'GRADE_5', 'GRADE_6', 'GRADE_7', 'GRADE_8', 'GRADE_9',
                                   'GRADE_10', 'GRADE_11', 'GRADE_12')
                );
            """);
        }
    }
}