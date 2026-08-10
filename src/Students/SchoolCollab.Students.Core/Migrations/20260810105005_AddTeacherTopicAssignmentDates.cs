using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTeacherTopicAssignmentDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "end_date",
                table: "teacher_topics",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "start_date",
                table: "teacher_topics",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // Backfill existing rows: set start_date to the assignment's created
            // date (the point the teacher began teaching the topic). A blank end
            // date means open-ended. Only rows that received the default (year 1)
            // are touched, so this is idempotent.
            migrationBuilder.Sql(
                "UPDATE \"teacher_topics\" SET \"start_date\" = (\"created_at\")::date " +
                "WHERE \"start_date\" = DATE '0001-01-01';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "end_date",
                table: "teacher_topics");

            migrationBuilder.DropColumn(
                name: "start_date",
                table: "teacher_topics");
        }
    }
}
