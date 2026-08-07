using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTopicStrandParentAndDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "end_date",
                table: "subject_strands",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_strand_id",
                table: "subject_strands",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "start_date",
                table: "subject_strands",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_subject_strands_parent_strand_id",
                table: "subject_strands",
                column: "parent_strand_id");

            migrationBuilder.CreateIndex(
                name: "ix_subject_strands_tenant_parent",
                table: "subject_strands",
                columns: new[] { "tenant_id", "parent_strand_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_subject_strands_subject_strands_parent_strand_id",
                table: "subject_strands",
                column: "parent_strand_id",
                principalTable: "subject_strands",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_subject_strands_subject_strands_parent_strand_id",
                table: "subject_strands");

            migrationBuilder.DropIndex(
                name: "ix_subject_strands_parent_strand_id",
                table: "subject_strands");

            migrationBuilder.DropIndex(
                name: "ix_subject_strands_tenant_parent",
                table: "subject_strands");

            migrationBuilder.DropColumn(
                name: "end_date",
                table: "subject_strands");

            migrationBuilder.DropColumn(
                name: "parent_strand_id",
                table: "subject_strands");

            migrationBuilder.DropColumn(
                name: "start_date",
                table: "subject_strands");
        }
    }
}
