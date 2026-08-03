using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class TopicSharedGlobalBridge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_grade_subject_assignments_subject_lessons_subject_lesson_id",
                table: "grade_subject_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_grade_subject_assignments_subject_strands_subject_strand_id",
                table: "grade_subject_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_subject_lessons_subjects_subject_id",
                table: "subject_lessons");

            migrationBuilder.DropForeignKey(
                name: "fk_subject_strands_subjects_subject_id",
                table: "subject_strands");

            migrationBuilder.DropIndex(
                name: "ix_grade_subject_assignments_tenant_unique",
                table: "grade_subject_assignments");

            migrationBuilder.RenameColumn(
                name: "subject_id",
                table: "teacher_subjects",
                newName: "topic_id");

            migrationBuilder.RenameColumn(
                name: "subject_id",
                table: "subject_strands",
                newName: "topic_id");

            migrationBuilder.RenameIndex(
                name: "ix_subject_strands_subject_id",
                table: "subject_strands",
                newName: "ix_subject_strands_topic_id");

            migrationBuilder.RenameColumn(
                name: "subject_id",
                table: "subject_lessons",
                newName: "topic_id");

            migrationBuilder.RenameIndex(
                name: "ix_subject_lessons_subject_id",
                table: "subject_lessons",
                newName: "ix_subject_lessons_topic_id");

            migrationBuilder.RenameColumn(
                name: "subject_id",
                table: "student_subject_assignments",
                newName: "topic_id");

            migrationBuilder.RenameColumn(
                name: "subject_strand_id",
                table: "grade_subject_assignments",
                newName: "topic_strand_id");

            migrationBuilder.RenameColumn(
                name: "subject_lesson_id",
                table: "grade_subject_assignments",
                newName: "topic_lesson_id");

            migrationBuilder.RenameColumn(
                name: "subject_id",
                table: "grade_subject_assignments",
                newName: "topic_id");

            migrationBuilder.RenameIndex(
                name: "ix_grade_subject_assignments_subject_strand_id",
                table: "grade_subject_assignments",
                newName: "ix_grade_subject_assignments_topic_strand_id");

            migrationBuilder.RenameIndex(
                name: "ix_grade_subject_assignments_subject_lesson_id",
                table: "grade_subject_assignments",
                newName: "ix_grade_subject_assignments_topic_lesson_id");

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "subjects",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "grade_level_id",
                table: "grade_subject_assignments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "activity_group_id",
                table: "grade_subject_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_grade_subject_assignments_tenant_unique",
                table: "grade_subject_assignments",
                columns: new[] { "tenant_id", "grade_level_id", "activity_group_id", "topic_id", "period_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_grade_subject_assignments_topic_lessons_topic_lesson_id",
                table: "grade_subject_assignments",
                column: "topic_lesson_id",
                principalTable: "subject_lessons",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_grade_subject_assignments_topic_strands_topic_strand_id",
                table: "grade_subject_assignments",
                column: "topic_strand_id",
                principalTable: "subject_strands",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_subject_lessons_subjects_topic_id",
                table: "subject_lessons",
                column: "topic_id",
                principalTable: "subjects",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_subject_strands_subjects_topic_id",
                table: "subject_strands",
                column: "topic_id",
                principalTable: "subjects",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_grade_subject_assignments_topic_lessons_topic_lesson_id",
                table: "grade_subject_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_grade_subject_assignments_topic_strands_topic_strand_id",
                table: "grade_subject_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_subject_lessons_subjects_topic_id",
                table: "subject_lessons");

            migrationBuilder.DropForeignKey(
                name: "fk_subject_strands_subjects_topic_id",
                table: "subject_strands");

            migrationBuilder.DropIndex(
                name: "ix_grade_subject_assignments_tenant_unique",
                table: "grade_subject_assignments");

            migrationBuilder.DropColumn(
                name: "description",
                table: "subjects");

            migrationBuilder.DropColumn(
                name: "activity_group_id",
                table: "grade_subject_assignments");

            migrationBuilder.RenameColumn(
                name: "topic_id",
                table: "teacher_subjects",
                newName: "subject_id");

            migrationBuilder.RenameColumn(
                name: "topic_id",
                table: "subject_strands",
                newName: "subject_id");

            migrationBuilder.RenameIndex(
                name: "ix_subject_strands_topic_id",
                table: "subject_strands",
                newName: "ix_subject_strands_subject_id");

            migrationBuilder.RenameColumn(
                name: "topic_id",
                table: "subject_lessons",
                newName: "subject_id");

            migrationBuilder.RenameIndex(
                name: "ix_subject_lessons_topic_id",
                table: "subject_lessons",
                newName: "ix_subject_lessons_subject_id");

            migrationBuilder.RenameColumn(
                name: "topic_id",
                table: "student_subject_assignments",
                newName: "subject_id");

            migrationBuilder.RenameColumn(
                name: "topic_strand_id",
                table: "grade_subject_assignments",
                newName: "subject_strand_id");

            migrationBuilder.RenameColumn(
                name: "topic_lesson_id",
                table: "grade_subject_assignments",
                newName: "subject_lesson_id");

            migrationBuilder.RenameColumn(
                name: "topic_id",
                table: "grade_subject_assignments",
                newName: "subject_id");

            migrationBuilder.RenameIndex(
                name: "ix_grade_subject_assignments_topic_strand_id",
                table: "grade_subject_assignments",
                newName: "ix_grade_subject_assignments_subject_strand_id");

            migrationBuilder.RenameIndex(
                name: "ix_grade_subject_assignments_topic_lesson_id",
                table: "grade_subject_assignments",
                newName: "ix_grade_subject_assignments_subject_lesson_id");

            migrationBuilder.AlterColumn<Guid>(
                name: "grade_level_id",
                table: "grade_subject_assignments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_grade_subject_assignments_tenant_unique",
                table: "grade_subject_assignments",
                columns: new[] { "tenant_id", "grade_level_id", "subject_id", "period_id" },
                unique: true);

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

            migrationBuilder.AddForeignKey(
                name: "fk_subject_lessons_subjects_subject_id",
                table: "subject_lessons",
                column: "subject_id",
                principalTable: "subjects",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_subject_strands_subjects_subject_id",
                table: "subject_strands",
                column: "subject_id",
                principalTable: "subjects",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
