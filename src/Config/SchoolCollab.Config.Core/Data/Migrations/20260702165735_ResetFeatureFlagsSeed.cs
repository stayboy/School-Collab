using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Config.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class ResetFeatureFlagsSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM feature_flags;");

            migrationBuilder.Sql(
                "INSERT INTO feature_flags (id, key, name, description, kind, is_enabled, is_archived, is_deleted, deleted_at, created_at, updated_at) " +
                "VALUES ('a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11', 'FEATURE:ENABLECODEDVALUESAICHAT', 'Enable AI chat on Coded Values landing page', NULL, 'Boolean', true, false, false, NULL, NOW(), NOW());");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM feature_flags WHERE key = 'FEATURE:ENABLECODEDVALUESAICHAT';");
        }
    }
}
