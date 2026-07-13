using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentSubmissionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "mandatory_review",
                table: "assignments",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "assignment_recipients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_type = table.Column<int>(type: "integer", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ward_student_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: true),
                    notify_on_broadcast = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    subscription_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_recipients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assignment_submission_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    submitted_by_guardian_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    content = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_submission_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assignment_submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_version_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    current_source = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    submitted_by_guardian_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submission_gate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_state = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_submissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "guardian_submission_gates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by_guardian_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    submission_enabled_for_student = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    submitted_by_guardian_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_by_guardian_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guardian_submission_gates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "submission_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    grade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    comments = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submission_reviews", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_recipients_assignment_owner",
                table: "assignment_recipients",
                columns: new[] { "assignment_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "uq_assignment_recipients_tenant_assignment_contact",
                table: "assignment_recipients",
                columns: new[] { "tenant_id", "assignment_id", "contact_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_assignment_submission_versions_tenant_submission_version",
                table: "assignment_submission_versions",
                columns: new[] { "tenant_id", "submission_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_assignment_submissions_tenant_assignment_student",
                table: "assignment_submissions",
                columns: new[] { "tenant_id", "assignment_id", "student_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_guardian_submission_gates_tenant_assignment_student",
                table: "guardian_submission_gates",
                columns: new[] { "tenant_id", "assignment_id", "student_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_submission_reviews_tenant_submission",
                table: "submission_reviews",
                columns: new[] { "tenant_id", "submission_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignment_recipients");

            migrationBuilder.DropTable(
                name: "assignment_submission_versions");

            migrationBuilder.DropTable(
                name: "assignment_submissions");

            migrationBuilder.DropTable(
                name: "guardian_submission_gates");

            migrationBuilder.DropTable(
                name: "submission_reviews");

            migrationBuilder.DropColumn(
                name: "mandatory_review",
                table: "assignments");
        }
    }
}
