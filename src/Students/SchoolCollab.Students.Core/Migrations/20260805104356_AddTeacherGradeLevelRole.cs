using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTeacherGradeLevelRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "teacher_role_coded_value_id",
                table: "teacher_grade_levels",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_teacher_grade_levels_tenant_grade_level",
                table: "teacher_grade_levels",
                columns: new[] { "tenant_id", "grade_level_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_teacher_grade_levels_tenant_grade_level",
                table: "teacher_grade_levels");

            migrationBuilder.DropColumn(
                name: "teacher_role_coded_value_id",
                table: "teacher_grade_levels");
        }
    }
}
