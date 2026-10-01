using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Settings.Core.Migrations
{
    /// <summary>
    /// Round A of <c>documents/solution/assignment-policy-fields.md</c> (owner decisions D1/D8):
    /// widens the tenant-global assignment policy from the single boolean
    /// <c>requires_signature_default</c> to the shared four-field
    /// <c>AssignmentPolicyFields</c> shape (real nullable columns — no JSON/owned type).
    ///
    /// <para><b>Additive only (ef-migrations rule 8).</b> The legacy
    /// <c>requires_signature_default</c> column is deliberately left in place — an old app binary
    /// keeps working through the deploy window — and the EF-generated <c>DropColumn</c> for it was
    /// removed from this migration by hand.</para>
    ///
    /// <para><b>Why it must stay INSERT-safe (plan P1-2).</b> The column is <c>NOT NULL</c> with no
    /// database default (see <c>20260910124751_AddTenantAssignmentPolicy</c>), and it is no longer
    /// mapped by the entity, so the create path would omit it and every first insert of a tenant
    /// policy row would fail with Postgres 23502. <c>Up()</c> therefore sets a database default of
    /// <c>false</c> — the exact behaviour the previous app version produced — and <c>Down()</c>
    /// removes it again. The column is dropped in Round B, once no deployed binary reads it.</para>
    ///
    /// <para><b>Backfill</b> maps the legacy boolean onto the new field exactly as D8 specifies:
    /// <c>false → Disabled</c>, <c>true → Optional</c> (this table's column is non-null, so the
    /// grade table's third case — <c>null → null</c> — cannot occur here).</para>
    /// </summary>
    public partial class AddAssignmentPolicyFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_copy_contacts",
                table: "tenant_assignment_policies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_primary_contacts",
                table: "tenant_assignment_policies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_approval_before_publish",
                table: "tenant_assignment_policies",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signature_requirement",
                table: "tenant_assignment_policies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Back-compat backfill (D8): false → Disabled, true → Optional. A tenant that had no
            // value had `false` (the column is NOT NULL), which is exactly Disabled.
            migrationBuilder.Sql("""
                UPDATE tenant_assignment_policies
                SET signature_requirement = CASE
                    WHEN requires_signature_default THEN 'Optional'
                    ELSE 'Disabled'
                END
                WHERE signature_requirement IS NULL;
                """);

            // Keep the now-unmapped legacy column INSERT-safe for the rest of the deploy window
            // (NOT NULL, no default ⇒ 23502 on every first insert otherwise).
            migrationBuilder.Sql("""
                ALTER TABLE tenant_assignment_policies
                    ALTER COLUMN requires_signature_default SET DEFAULT false;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse order of Up(): the legacy column's default goes back to "none" first, so the
            // pre-migration schema shape is restored exactly. The four new columns are dropped
            // (their values are lost — the reversal is a developer/rollback action only).
            migrationBuilder.Sql("""
                ALTER TABLE tenant_assignment_policies
                    ALTER COLUMN requires_signature_default DROP DEFAULT;
                """);

            migrationBuilder.DropColumn(
                name: "max_copy_contacts",
                table: "tenant_assignment_policies");

            migrationBuilder.DropColumn(
                name: "max_primary_contacts",
                table: "tenant_assignment_policies");

            migrationBuilder.DropColumn(
                name: "requires_approval_before_publish",
                table: "tenant_assignment_policies");

            migrationBuilder.DropColumn(
                name: "signature_requirement",
                table: "tenant_assignment_policies");
        }
    }
}
