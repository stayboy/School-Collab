using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueIndexesGradeLevelSubjectCodedValueId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_subjects_coded_value_id",
                table: "subjects");

            migrationBuilder.DropIndex(
                name: "ix_grade_levels_coded_value_id",
                table: "grade_levels");

            migrationBuilder.CreateIndex(
                name: "ix_subjects_coded_value_id",
                table: "subjects",
                column: "coded_value_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_grade_levels_coded_value_id",
                table: "grade_levels",
                column: "coded_value_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_subjects_coded_value_id",
                table: "subjects");

            migrationBuilder.DropIndex(
                name: "ix_grade_levels_coded_value_id",
                table: "grade_levels");

            migrationBuilder.CreateIndex(
                name: "ix_subjects_coded_value_id",
                table: "subjects",
                column: "coded_value_id");

            migrationBuilder.CreateIndex(
                name: "ix_grade_levels_coded_value_id",
                table: "grade_levels",
                column: "coded_value_id");
        }
    }
}
