using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityGroupNextWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "next_enrollment_end_date",
                table: "activity_groups",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "next_enrollment_start_date",
                table: "activity_groups",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "next_enrollment_end_date",
                table: "activity_groups");

            migrationBuilder.DropColumn(
                name: "next_enrollment_start_date",
                table: "activity_groups");
        }
    }
}
