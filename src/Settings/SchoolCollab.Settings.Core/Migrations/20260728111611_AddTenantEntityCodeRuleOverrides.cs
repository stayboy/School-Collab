using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Settings.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantEntityCodeRuleOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_entity_code_rule_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_code_segment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_entity_code_rule_overrides", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_entity_code_rule_overrides_rule",
                table: "tenant_entity_code_rule_overrides",
                columns: new[] { "tenant_id", "generation_rule_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_entity_code_rule_overrides_unique",
                table: "tenant_entity_code_rule_overrides",
                columns: new[] { "tenant_id", "generation_rule_id", "entity_code_segment_id", "field" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_entity_code_rule_overrides");
        }
    }
}
