using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentActivityLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assignment_activity_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_activity_groups", x => x.id);
                    table.ForeignKey(
                        name: "fk_assignment_activity_groups_assignments_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_activity_groups_assignment_id",
                table: "assignment_activity_groups",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_activity_groups_tenant_group",
                table: "assignment_activity_groups",
                columns: new[] { "tenant_id", "activity_group_id" });

            migrationBuilder.CreateIndex(
                name: "uq_assignment_activity_groups_tenant_assignment_group",
                table: "assignment_activity_groups",
                columns: new[] { "tenant_id", "assignment_id", "activity_group_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignment_activity_groups");
        }
    }
}
