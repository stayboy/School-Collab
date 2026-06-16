using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <inheritdoc />
    public partial class CreateAssignmentTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    assignment_type = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    subject_coded_value_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grade_coded_value_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    max_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_by_teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assignment_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_assignment_attachments_assignments_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assignment_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    question_type = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    correct_option_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_questions", x => x.id);
                    table.ForeignKey(
                        name: "fk_assignment_questions_assignments_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assignment_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    comments = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    review_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_reviews", x => x.id);
                    table.ForeignKey(
                        name: "fk_assignment_reviews_assignments_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "question_options",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    option_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_correct = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_question_options", x => x.id);
                    table.ForeignKey(
                        name: "fk_question_options_assignment_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "assignment_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_attachments_assignment_id",
                table: "assignment_attachments",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_questions_assignment_id",
                table: "assignment_questions",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_reviews_assignment_id",
                table: "assignment_reviews",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_status",
                table: "assignments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_subject_cv_id",
                table: "assignments",
                column: "subject_coded_value_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_teacher_id",
                table: "assignments",
                column: "created_by_teacher_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at",
                table: "outbox_messages",
                column: "processed_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_question_options_question_id",
                table: "question_options",
                column: "question_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignment_attachments");

            migrationBuilder.DropTable(
                name: "assignment_reviews");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "question_options");

            migrationBuilder.DropTable(
                name: "assignment_questions");

            migrationBuilder.DropTable(
                name: "assignments");
        }
    }
}
