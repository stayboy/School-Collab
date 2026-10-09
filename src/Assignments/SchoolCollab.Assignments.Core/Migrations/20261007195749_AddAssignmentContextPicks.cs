using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentContextPicks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "context_lesson_ids",
                table: "assignments",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<Guid[]>(
                name: "context_strand_ids",
                table: "assignments",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // R4 (P5): the reversal is by-design lossy of nothing beyond the picks themselves —
            // both columns did not exist before this migration, and nothing else ever read or
            // derived from them, so dropping them restores the pre-R4 schema exactly (no data
            // backfill ran in Up()).
            migrationBuilder.DropColumn(
                name: "context_lesson_ids",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "context_strand_ids",
                table: "assignments");
        }
    }
}
