using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.CodedValues.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAttributeDefinitionsAndSimplifyAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove DataType/SourceCode from instance-level attributes
            migrationBuilder.DropColumn(
                name: "data_type",
                table: "coded_value_attributes");

            migrationBuilder.DropColumn(
                name: "source_code",
                table: "coded_value_attributes");

            // Create the new parent-level attribute definitions table
            migrationBuilder.CreateTable(
                name: "coded_value_attribute_definitions",
                columns: table => new
                {
                    coded_value_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    data_type = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    source_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coded_value_attribute_definitions", x => new { x.coded_value_id, x.key });
                    table.ForeignKey(
                        name: "fk_coded_value_attribute_definitions_coded_values_coded_value_id",
                        column: x => x.coded_value_id,
                        principalTable: "coded_values",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_coded_value_attribute_definitions_key",
                table: "coded_value_attribute_definitions",
                column: "key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "coded_value_attribute_definitions");

            migrationBuilder.AddColumn<int>(
                name: "data_type",
                table: "coded_value_attributes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "source_code",
                table: "coded_value_attributes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}
