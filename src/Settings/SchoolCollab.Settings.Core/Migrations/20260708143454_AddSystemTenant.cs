using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Settings.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // §9.2 #5 / Q-1: seed the System tenant idempotently — a backfill sink for
            // unattributable strict rows (students/grade_levels/subjects/periods with a
            // legacy Guid.Empty tenant). Fixed well-known id so cross-context backfill
            // migrations (Students) can reference it. No end-users authenticate as System.
            // Idempotent by the ix_tenants_name_unique natural key.
            migrationBuilder.Sql(@"
INSERT INTO tenants (id, name, type, created_at, updated_at)
VALUES ('00000000-0000-0000-0000-000000000001', 'System', 'Organization', now(), now())
ON CONFLICT (name) DO NOTHING");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM tenants WHERE id = '00000000-0000-0000-0000-000000000001'");
        }
    }
}
