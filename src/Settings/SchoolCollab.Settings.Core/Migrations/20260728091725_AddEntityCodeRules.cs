using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Settings.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityCodeRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entity_code_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_code_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "entity_code_segments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_code_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    index = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    fixed_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    prefix = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    suffix = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reset_period = table.Column<int>(type: "integer", nullable: false),
                    min_width = table.Column<int>(type: "integer", nullable: false),
                    upper_limit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    last_sequence = table.Column<int>(type: "integer", nullable: false),
                    last_prefix = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    last_period_bucket = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_code_segments", x => x.id);
                    table.ForeignKey(
                        name: "fk_entity_code_segments_entity_code_rules_entity_code_rule_id",
                        column: x => x.entity_code_rule_id,
                        principalTable: "entity_code_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entity_code_rules_code_unique",
                table: "entity_code_rules",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entity_code_segments_rule_index_unique",
                table: "entity_code_segments",
                columns: new[] { "entity_code_rule_id", "index" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_code_segments");

            migrationBuilder.DropTable(
                name: "entity_code_rules");
        }
    }
}
