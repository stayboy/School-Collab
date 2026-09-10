using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmissionAnswersAndScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_attempts",
                table: "assignments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "pass_score",
                table: "assignments",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "attempt_limit_overridden_at",
                table: "assignment_submissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "attempt_limit_overridden_by",
                table: "assignment_submissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "passed",
                table: "assignment_submission_versions",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "score",
                table: "assignment_submission_versions",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "assignment_submission_answers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selected_option_id = table.Column<Guid>(type: "uuid", nullable: true),
                    text_answer = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_submission_answers", x => x.id);
                    table.ForeignKey(
                        name: "fk_assignment_submission_answers_assignment_submission_version",
                        column: x => x.submission_version_id,
                        principalTable: "assignment_submission_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_submission_answers_submission_version_id",
                table: "assignment_submission_answers",
                column: "submission_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_submission_answers_tenant_version",
                table: "assignment_submission_answers",
                columns: new[] { "tenant_id", "submission_version_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignment_submission_answers");

            migrationBuilder.DropColumn(
                name: "max_attempts",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "pass_score",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "attempt_limit_overridden_at",
                table: "assignment_submissions");

            migrationBuilder.DropColumn(
                name: "attempt_limit_overridden_by",
                table: "assignment_submissions");

            migrationBuilder.DropColumn(
                name: "passed",
                table: "assignment_submission_versions");

            migrationBuilder.DropColumn(
                name: "score",
                table: "assignment_submission_versions");
        }
    }
}
