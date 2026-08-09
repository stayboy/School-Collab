using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class RenameGradeStrandToStream : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "grade_strand_coded_value_id",
                table: "student_enrollments",
                newName: "stream_coded_value_id");

            migrationBuilder.RenameIndex(
                name: "ix_student_enrollments_tenant_grade_strand",
                table: "student_enrollments",
                newName: "ix_student_enrollments_tenant_stream");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "stream_coded_value_id",
                table: "student_enrollments",
                newName: "grade_strand_coded_value_id");

            migrationBuilder.RenameIndex(
                name: "ix_student_enrollments_tenant_stream",
                table: "student_enrollments",
                newName: "ix_student_enrollments_tenant_grade_strand");
        }
    }
}
