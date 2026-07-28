using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Settings.Core.Migrations
{
    /// <summary>
    /// Data migration: converts CodedValue-typed attribute values from their
    /// human-readable code (e.g. "GRADE_R", "CITIES_ACCRA") to the GUID of the
    /// referenced coded value. The seeder was originally storing the raw code
    /// string, but the attribute definition's DataType is CodedValue, so APIs
    /// and filters expect the stored value to be the referenced CodedValueId.
    /// </summary>
    public partial class FixCodedValueAttributeGuids : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE coded_value_attributes AS attr
                SET value = ref.id::text
                FROM coded_values AS cv
                JOIN coded_values AS parent ON cv.parent_id = parent.id
                JOIN coded_value_attribute_definitions AS def
                    ON def.coded_value_id = parent.id
                    AND def.key = attr.key
                JOIN coded_values AS ref ON ref.code = attr.value
                WHERE attr.coded_value_id = cv.id
                  AND def.data_type = 7
                  AND attr.value !~ '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$';
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: we cannot know which original code string a GUID
            // came from without re-querying the coded_values table. A down
            // migration would require joining back to coded_values by id, but
            // that would overwrite any manually-set GUID values as well. Leave
            // no-op; this is a corrective data migration.
        }
    }
}
