using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.CodedValues.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAttributeDefinitionsAndValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bus_name",
                table: "outbox_state",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "coded_value_attribute_definitions",
                columns: table => new
                {
                    coded_value_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    data_type = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    source_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    allow_multiple = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    min_length = table.Column<int>(type: "integer", nullable: true),
                    max_length = table.Column<int>(type: "integer", nullable: true),
                    regex_pattern = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coded_value_attribute_definitions", x => new { x.coded_value_id, x.key });
                    table.ForeignKey(
                        name: "fk_coded_value_attribute_definitions_coded_values_coded_value_",
                        column: x => x.coded_value_id,
                        principalTable: "coded_values",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_state_bus_name_created",
                table: "outbox_state",
                columns: new[] { "bus_name", "created" });

            migrationBuilder.CreateIndex(
                name: "ix_coded_value_attribute_definitions_key",
                table: "coded_value_attribute_definitions",
                column: "key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coded_value_attribute_definitions");

            migrationBuilder.DropIndex(
                name: "ix_outbox_state_bus_name_created",
                table: "outbox_state");

            migrationBuilder.DropColumn(
                name: "bus_name",
                table: "outbox_state");
        }
    }
}
