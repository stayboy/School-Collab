using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class MakeGradeSubjectAssignmentDateBased : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_grade_subject_assignments_tenant_period",
                table: "grade_subject_assignments");

            migrationBuilder.DropIndex(
                name: "ix_grade_subject_assignments_tenant_unique",
                table: "grade_subject_assignments");

            // Convert the period-bound bridge to a date-based one. We first add the
            // new date columns (nullable) so we can backfill each existing row's
            // effective window from its linked period, then drop period_id.
            migrationBuilder.AddColumn<DateOnly>(
                name: "end_date",
                table: "grade_subject_assignments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "start_date",
                table: "grade_subject_assignments",
                type: "date",
                nullable: true);

            // Backfill the effective window from the linked period, so no assignment
            // history is lost. Rows without a matching period default to open-ended
            // (null end) starting from today.
            migrationBuilder.Sql(
                """
                UPDATE grade_subject_assignments AS gsa
                SET start_date = p.start_date,
                    end_date   = p.end_date
                FROM periods AS p
                WHERE p.id = gsa.period_id;
                """);

            migrationBuilder.Sql(
                """
                UPDATE grade_subject_assignments
                SET start_date = CURRENT_DATE
                WHERE start_date IS NULL;
                """);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "start_date",
                table: "grade_subject_assignments",
                type: "date",
                nullable: false);

            migrationBuilder.DropColumn(
                name: "period_id",
                table: "grade_subject_assignments");

            migrationBuilder.CreateIndex(
                name: "ix_grade_subject_assignments_tenant_effective_dates",
                table: "grade_subject_assignments",
                columns: new[] { "tenant_id", "start_date", "end_date" });

            migrationBuilder.CreateIndex(
                name: "ix_grade_subject_assignments_tenant_unique",
                table: "grade_subject_assignments",
                columns: new[] { "tenant_id", "grade_level_id", "activity_group_id", "topic_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_grade_subject_assignments_tenant_effective_dates",
                table: "grade_subject_assignments");

            migrationBuilder.DropIndex(
                name: "ix_grade_subject_assignments_tenant_unique",
                table: "grade_subject_assignments");

            migrationBuilder.DropColumn(
                name: "end_date",
                table: "grade_subject_assignments");

            migrationBuilder.DropColumn(
                name: "start_date",
                table: "grade_subject_assignments");

            migrationBuilder.AddColumn<Guid>(
                name: "period_id",
                table: "grade_subject_assignments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_grade_subject_assignments_tenant_period",
                table: "grade_subject_assignments",
                columns: new[] { "tenant_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "ix_grade_subject_assignments_tenant_unique",
                table: "grade_subject_assignments",
                columns: new[] { "tenant_id", "grade_level_id", "activity_group_id", "topic_id", "period_id" },
                unique: true);
        }
    }
}
