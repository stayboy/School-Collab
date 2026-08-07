using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonDemographicBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "date_of_birth",
                table: "teachers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "gender_coded_value_id",
                table: "teachers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "level_of_education_coded_value_id",
                table: "teachers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "title_coded_value_id",
                table: "students",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "date_of_birth",
                table: "guardians",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "gender_coded_value_id",
                table: "guardians",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "teacher_qualifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    coded_value_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teacher_qualifications", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_teacher_qualifications_tenant_teacher_qualification",
                table: "teacher_qualifications",
                columns: new[] { "tenant_id", "teacher_id", "coded_value_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "teacher_qualifications");

            migrationBuilder.DropColumn(
                name: "date_of_birth",
                table: "teachers");

            migrationBuilder.DropColumn(
                name: "gender_coded_value_id",
                table: "teachers");

            migrationBuilder.DropColumn(
                name: "level_of_education_coded_value_id",
                table: "teachers");

            migrationBuilder.DropColumn(
                name: "title_coded_value_id",
                table: "students");

            migrationBuilder.DropColumn(
                name: "date_of_birth",
                table: "guardians");

            migrationBuilder.DropColumn(
                name: "gender_coded_value_id",
                table: "guardians");
        }
    }
}
