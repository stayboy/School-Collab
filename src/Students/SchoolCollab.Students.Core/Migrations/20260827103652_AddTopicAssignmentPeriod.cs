using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTopicAssignmentPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "period_id",
                table: "topic_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_topic_assignments_period_id",
                table: "topic_assignments",
                column: "period_id");

            migrationBuilder.AddForeignKey(
                name: "fk_topic_assignments_periods_period_id",
                table: "topic_assignments",
                column: "period_id",
                principalTable: "periods",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_topic_assignments_periods_period_id",
                table: "topic_assignments");

            migrationBuilder.DropIndex(
                name: "ix_topic_assignments_period_id",
                table: "topic_assignments");

            migrationBuilder.DropColumn(
                name: "period_id",
                table: "topic_assignments");
        }
    }
}
