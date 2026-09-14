using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSignOffAndSignatureEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "expected_signer_guardian_id",
                table: "assignment_submissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "finalized_at",
                table: "assignment_submissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sign_off_state",
                table: "assignment_submissions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "signed_at",
                table: "assignment_submissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "signature_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signer_guardian_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signature_type = table.Column<int>(type: "integer", nullable: false),
                    typed_signature = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    consent_text_shown = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    certificate_storage_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_signature_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_signature_events_assignment_student",
                table: "signature_events",
                columns: new[] { "assignment_id", "student_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "signature_events");

            migrationBuilder.DropColumn(
                name: "expected_signer_guardian_id",
                table: "assignment_submissions");

            migrationBuilder.DropColumn(
                name: "finalized_at",
                table: "assignment_submissions");

            migrationBuilder.DropColumn(
                name: "sign_off_state",
                table: "assignment_submissions");

            migrationBuilder.DropColumn(
                name: "signed_at",
                table: "assignment_submissions");
        }
    }
}
