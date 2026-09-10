using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddContentModulesAndResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assignment_content_modules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_type = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    min_completion_threshold_percent = table.Column<int>(type: "integer", nullable: false, defaultValue: 100),
                    is_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_content_modules", x => x.id);
                    table.ForeignKey(
                        name: "fk_assignment_content_modules_assignments_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assignment_resources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_kind = table.Column<int>(type: "integer", nullable: false),
                    url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    storage_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    included_in_generation = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_resources", x => x.id);
                    table.ForeignKey(
                        name: "fk_assignment_resources_assignments_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_content_modules_assignment_id",
                table: "assignment_content_modules",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_content_modules_tenant_assignment",
                table: "assignment_content_modules",
                columns: new[] { "tenant_id", "assignment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_resources_assignment_id",
                table: "assignment_resources",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_resources_tenant_assignment",
                table: "assignment_resources",
                columns: new[] { "tenant_id", "assignment_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignment_content_modules");

            migrationBuilder.DropTable(
                name: "assignment_resources");
        }
    }
}
