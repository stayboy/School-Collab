using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityGroupEnrollmentSpans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_renew_default",
                table: "activity_groups",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "enrollment_end_date",
                table: "activity_groups",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "enrollment_start_date",
                table: "activity_groups",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "span",
                table: "activity_groups",
                type: "integer",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<bool>(
                name: "auto_renew",
                table: "activity_group_memberships",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "window_end_date",
                table: "activity_group_memberships",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "window_start_date",
                table: "activity_group_memberships",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auto_renew_default",
                table: "activity_groups");

            migrationBuilder.DropColumn(
                name: "enrollment_end_date",
                table: "activity_groups");

            migrationBuilder.DropColumn(
                name: "enrollment_start_date",
                table: "activity_groups");

            migrationBuilder.DropColumn(
                name: "span",
                table: "activity_groups");

            migrationBuilder.DropColumn(
                name: "auto_renew",
                table: "activity_group_memberships");

            migrationBuilder.DropColumn(
                name: "window_end_date",
                table: "activity_group_memberships");

            migrationBuilder.DropColumn(
                name: "window_start_date",
                table: "activity_group_memberships");
        }
    }
}
