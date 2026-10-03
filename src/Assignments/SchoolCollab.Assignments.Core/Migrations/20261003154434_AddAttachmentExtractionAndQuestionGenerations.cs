using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachmentExtractionAndQuestionGenerations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "generation_id",
                table: "assignment_questions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "extracted_at",
                table: "assignment_attachments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "extracted_text",
                table: "assignment_attachments",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "extraction_error",
                table: "assignment_attachments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "extraction_status",
                table: "assignment_attachments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "assignment_question_generations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_count = table.Column<int>(type: "integer", nullable: false),
                    types = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    difficulty_easy_count = table.Column<int>(type: "integer", nullable: true),
                    difficulty_medium_count = table.Column<int>(type: "integer", nullable: true),
                    difficulty_hard_count = table.Column<int>(type: "integer", nullable: true),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_question_generations", x => x.id);
                    table.ForeignKey(
                        name: "fk_assignment_question_generations_assignments_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_question_generations_assignment_id",
                table: "assignment_question_generations",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_question_generations_tenant_assignment",
                table: "assignment_question_generations",
                columns: new[] { "tenant_id", "assignment_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignment_question_generations");

            migrationBuilder.DropColumn(
                name: "generation_id",
                table: "assignment_questions");

            migrationBuilder.DropColumn(
                name: "extracted_at",
                table: "assignment_attachments");

            migrationBuilder.DropColumn(
                name: "extracted_text",
                table: "assignment_attachments");

            migrationBuilder.DropColumn(
                name: "extraction_error",
                table: "assignment_attachments");

            migrationBuilder.DropColumn(
                name: "extraction_status",
                table: "assignment_attachments");
        }
    }
}
