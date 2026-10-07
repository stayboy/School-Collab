using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Settings.Core.Migrations
{
    /// <summary>
    /// Round <c>assignment-rules-policy-rework</c> (spec decisions D3/D5/D7): widens the tenant-global
    /// assignment policy with the guardian-review flag and the archive grace window, so both flow
    /// through the existing tenant-default + per-grade-override merge.
    ///
    /// <para><b>Additive only (ef-migrations rule 8).</b> Two nullable columns, no backfill: a null
    /// value means "unset" — the write seam then keeps today's mandatory-review default
    /// (<see langword="true"/>) and the built-in 30-day archive retention floor.</para>
    /// </summary>
    public partial class AddAssignmentPolicyReviewAndArchiveFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "archive_grace_days",
                table: "tenant_assignment_policies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "mandatory_review",
                table: "tenant_assignment_policies",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "archive_grace_days",
                table: "tenant_assignment_policies");

            migrationBuilder.DropColumn(
                name: "mandatory_review",
                table: "tenant_assignment_policies");
        }
    }
}
