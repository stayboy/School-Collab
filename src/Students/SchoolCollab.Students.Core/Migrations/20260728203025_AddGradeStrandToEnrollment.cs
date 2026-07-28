using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddGradeStrandToEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "grade_strand_coded_value_id",
                table: "student_enrollments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_student_enrollments_tenant_grade_strand",
                table: "student_enrollments",
                columns: new[] { "tenant_id", "grade_strand_coded_value_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_student_enrollments_tenant_grade_strand",
                table: "student_enrollments");

            migrationBuilder.DropColumn(
                name: "grade_strand_coded_value_id",
                table: "student_enrollments");
        }
    }
}
