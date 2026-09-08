using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentLifecycleAndApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "approval_status",
                table: "assignments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                table: "assignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by",
                table: "assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "archive_grace_days",
                table: "assignments",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "available_from_utc",
                table: "assignments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "approval_status",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "approved_at",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "approved_by",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "archive_grace_days",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "available_from_utc",
                table: "assignments");
        }
    }
}
