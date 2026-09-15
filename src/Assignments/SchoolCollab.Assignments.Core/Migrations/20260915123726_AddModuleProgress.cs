using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assignment_module_progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    percent_complete = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_module_progress", x => x.id);
                    table.ForeignKey(
                        name: "fk_assignment_module_progress_assignment_content_modules_conte",
                        column: x => x.content_module_id,
                        principalTable: "assignment_content_modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_module_progress_content_module_id",
                table: "assignment_module_progress",
                column: "content_module_id");

            migrationBuilder.CreateIndex(
                name: "uq_assignment_module_progress_tenant_assignment_student_module",
                table: "assignment_module_progress",
                columns: new[] { "tenant_id", "assignment_id", "student_id", "content_module_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignment_module_progress");
        }
    }
}
