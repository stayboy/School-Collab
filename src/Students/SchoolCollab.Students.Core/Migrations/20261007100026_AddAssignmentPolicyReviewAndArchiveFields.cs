using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <summary>
    /// Round <c>assignment-rules-policy-rework</c> (spec decisions D3/D5/D7): widens the per-grade
    /// assignment-policy override with the guardian-review flag and the archive grace window.
    ///
    /// <para><b>Additive only (ef-migrations rule 8).</b> Two nullable columns, no backfill: a null
    /// value means "inherit the tenant default" (and, when the tenant row is unset too, the write
    /// seam falls back to the built-in defaults — review <see langword="true"/>, grace 30).</para>
    /// </summary>
    public partial class AddAssignmentPolicyReviewAndArchiveFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "archive_grace_days",
                table: "grade_assignment_policies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "mandatory_review",
                table: "grade_assignment_policies",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "archive_grace_days",
                table: "grade_assignment_policies");

            migrationBuilder.DropColumn(
                name: "mandatory_review",
                table: "grade_assignment_policies");
        }
    }
}
