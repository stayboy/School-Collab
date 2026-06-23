using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSubjectStrandsAndLessons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "subject_lesson_id",
                table: "grade_subject_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "subject_strand_id",
                table: "grade_subject_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "subject_strands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subject_strands", x => x.id);
                    table.ForeignKey(
                        name: "fk_subject_strands_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subject_lessons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    strand_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subject_lessons", x => x.id);
                    table.ForeignKey(
                        name: "fk_subject_lessons_subject_strands_strand_id",
                        column: x => x.strand_id,
                        principalTable: "subject_strands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_subject_lessons_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_grade_subject_assignments_subject_lesson_id",
                table: "grade_subject_assignments",
                column: "subject_lesson_id");

            migrationBuilder.CreateIndex(
                name: "ix_grade_subject_assignments_subject_strand_id",
                table: "grade_subject_assignments",
                column: "subject_strand_id");

            migrationBuilder.CreateIndex(
                name: "ix_subject_lessons_strand",
                table: "subject_lessons",
                column: "strand_id");

            migrationBuilder.CreateIndex(
                name: "ix_subject_lessons_subject",
                table: "subject_lessons",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_subject_strands_subject",
                table: "subject_strands",
                column: "subject_id");

            migrationBuilder.AddForeignKey(
                name: "fk_grade_subject_assignments_subject_lessons_subject_lesson_id",
                table: "grade_subject_assignments",
                column: "subject_lesson_id",
                principalTable: "subject_lessons",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_grade_subject_assignments_subject_strands_subject_strand_id",
                table: "grade_subject_assignments",
                column: "subject_strand_id",
                principalTable: "subject_strands",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_grade_subject_assignments_subject_lessons_subject_lesson_id",
                table: "grade_subject_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_grade_subject_assignments_subject_strands_subject_strand_id",
                table: "grade_subject_assignments");

            migrationBuilder.DropTable(
                name: "subject_lessons");

            migrationBuilder.DropTable(
                name: "subject_strands");

            migrationBuilder.DropIndex(
                name: "ix_grade_subject_assignments_subject_lesson_id",
                table: "grade_subject_assignments");

            migrationBuilder.DropIndex(
                name: "ix_grade_subject_assignments_subject_strand_id",
                table: "grade_subject_assignments");

            migrationBuilder.DropColumn(
                name: "subject_lesson_id",
                table: "grade_subject_assignments");

            migrationBuilder.DropColumn(
                name: "subject_strand_id",
                table: "grade_subject_assignments");
        }
    }
}
