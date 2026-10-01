using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <summary>
    /// Round A of <c>documents/solution/assignment-policy-fields.md</c> (owner decisions D1/D8):
    /// widens the per-grade assignment policy from the single nullable boolean
    /// <c>requires_signature_default</c> to the shared four-field
    /// <c>AssignmentPolicyFields</c> shape (real nullable columns — no JSON/owned type).
    ///
    /// <para><b>Additive only (ef-migrations rule 8).</b> EF initially scaffolded this change as a
    /// <c>RenameColumn</c> of <c>requires_signature_default</c> → <c>requires_approval_before_publish</c>;
    /// that is wrong twice over — it would carry signature booleans into the approval column as
    /// data, and it would delete a column a still-deployed binary reads. It was replaced by hand
    /// with an <c>AddColumn</c> plus the D8 backfill below, leaving the legacy column in place.
    /// Round B drops it once no deployed binary reads it.</para>
    ///
    /// <para><b>Backfill</b> maps the legacy tri-state onto the new field exactly as D8 specifies:
    /// <c>false → Disabled</c>, <c>true → Optional</c>, <c>null → null</c> (still "inherit the
    /// tenant default").</para>
    /// </summary>
    public partial class AddAssignmentPolicyFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "requires_approval_before_publish",
                table: "grade_assignment_policies",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_copy_contacts",
                table: "grade_assignment_policies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_primary_contacts",
                table: "grade_assignment_policies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signature_requirement",
                table: "grade_assignment_policies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Back-compat backfill (D8): false → Disabled, true → Optional, null → null (the
            // row keeps inheriting the tenant default). Explicitly three-valued so the mapping
            // cannot silently fall back to a default for a NULL legacy value.
            migrationBuilder.Sql("""
                UPDATE grade_assignment_policies
                SET signature_requirement = CASE
                    WHEN requires_signature_default IS TRUE THEN 'Optional'
                    WHEN requires_signature_default IS FALSE THEN 'Disabled'
                    ELSE NULL
                END
                WHERE signature_requirement IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The inverse of Up(): drop the four new columns (their values are lost — the
            // reversal is a developer/rollback action only). The legacy
            // requires_signature_default column is untouched: it was never removed.
            migrationBuilder.DropColumn(
                name: "max_copy_contacts",
                table: "grade_assignment_policies");

            migrationBuilder.DropColumn(
                name: "max_primary_contacts",
                table: "grade_assignment_policies");

            migrationBuilder.DropColumn(
                name: "requires_approval_before_publish",
                table: "grade_assignment_policies");

            migrationBuilder.DropColumn(
                name: "signature_requirement",
                table: "grade_assignment_policies");
        }
    }
}
