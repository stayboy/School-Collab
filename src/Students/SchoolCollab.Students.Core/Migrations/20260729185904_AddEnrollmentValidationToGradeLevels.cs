using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrollmentValidationToGradeLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "allowed_gender_coded_value_id",
                table: "grade_levels",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_age",
                table: "grade_levels",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "min_age",
                table: "grade_levels",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allowed_gender_coded_value_id",
                table: "grade_levels");

            migrationBuilder.DropColumn(
                name: "max_age",
                table: "grade_levels");

            migrationBuilder.DropColumn(
                name: "min_age",
                table: "grade_levels");
        }
    }
}
