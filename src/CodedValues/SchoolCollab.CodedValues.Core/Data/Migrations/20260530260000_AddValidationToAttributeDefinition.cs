using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.CodedValues.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddValidationToAttributeDefinition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "min_length",
                table: "coded_value_attribute_definitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_length",
                table: "coded_value_attribute_definitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "regex_pattern",
                table: "coded_value_attribute_definitions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "min_length",
                table: "coded_value_attribute_definitions");

            migrationBuilder.DropColumn(
                name: "max_length",
                table: "coded_value_attribute_definitions");

            migrationBuilder.DropColumn(
                name: "regex_pattern",
                table: "coded_value_attribute_definitions");
        }
    }
}
