using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentTransferAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "promotion_outcome",
                table: "student_enrollments");

            migrationBuilder.AddColumn<string>(
                name: "transfer_reason",
                table: "student_enrollments",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "student_transfer_audit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_grade_level_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_grade_level_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    actor_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    actor_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_transfer_audit_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_student_transfer_audit_tenant_period",
                table: "student_transfer_audit_entries",
                columns: new[] { "tenant_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "ix_student_transfer_audit_tenant_student",
                table: "student_transfer_audit_entries",
                columns: new[] { "tenant_id", "student_id" });

            migrationBuilder.CreateIndex(
                name: "ix_student_transfer_audit_tenant_to_grade",
                table: "student_transfer_audit_entries",
                columns: new[] { "tenant_id", "to_grade_level_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "student_transfer_audit_entries");

            migrationBuilder.DropColumn(
                name: "transfer_reason",
                table: "student_enrollments");

            migrationBuilder.AddColumn<int>(
                name: "promotion_outcome",
                table: "student_enrollments",
                type: "integer",
                nullable: true);
        }
    }
}
