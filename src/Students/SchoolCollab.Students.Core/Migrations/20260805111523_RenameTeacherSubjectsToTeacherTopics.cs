using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class RenameTeacherSubjectsToTeacherTopics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FR-13: rename the TeacherSubject entity + table to TeacherTopic.
            // The columns are unchanged (topic_id was already the FK), so a
            // data-preserving RenameTable is all that is required. The unique
            // index travels with the table and is renamed to match.
            migrationBuilder.RenameTable(
                name: "teacher_subjects",
                newName: "teacher_topics");

            migrationBuilder.RenameIndex(
                name: "ix_teacher_subjects_tenant_teacher_subject",
                table: "teacher_topics",
                newName: "ix_teacher_topics_tenant_teacher_topic");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "ix_teacher_topics_tenant_teacher_topic",
                table: "teacher_topics",
                newName: "ix_teacher_subjects_tenant_teacher_subject");

            migrationBuilder.RenameTable(
                name: "teacher_topics",
                newName: "teacher_subjects");
        }
    }
}
