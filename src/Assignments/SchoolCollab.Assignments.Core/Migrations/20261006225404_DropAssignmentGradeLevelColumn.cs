using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <summary>
    /// Round <c>drop-primary-grade</c>: drops the assignment's authored primary grade
    /// (<c>assignments.grade_level_id</c>) and its index. The assignment's grades are its authored
    /// <c>assignment_targets</c> rows (<c>Kind = 'GradeLevel'</c>); the policy-scope grade is derived
    /// from them.
    /// <para><b>No backfill, and the down is schema-only — lossy by design.</b> The authoritative copy
    /// of every persisted grade already became a <c>GradeLevel</c> target row in the shipped
    /// <c>AddAssignmentTargets</c> backfill (TGT-14). That backfill is not lossless for every row: a
    /// legacy <c>AllStudents</c> row carrying a primary grade got only the <c>AllStudents</c> target, so
    /// its grade is gone with no target echo — consistent with the owner's settled decision that
    /// recipients come from the targets. <see cref="Down"/> re-adds the nullable column and the index
    /// but restores NO values (they are not recoverable from the target rows).</para>
    /// </summary>
    public partial class DropAssignmentGradeLevelColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_assignments_grade_level_id",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "grade_level_id",
                table: "assignments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "grade_level_id",
                table: "assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_assignments_grade_level_id",
                table: "assignments",
                column: "grade_level_id");
        }
    }
}
