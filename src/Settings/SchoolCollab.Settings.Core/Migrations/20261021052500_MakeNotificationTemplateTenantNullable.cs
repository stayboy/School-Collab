using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Settings.Core.Migrations
{
    /// <inheritdoc />
    public partial class MakeNotificationTemplateTenantNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "tenant_id",
                table: "tenant_notification_templates",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "tenant_id",
                table: "template_constraints",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverting this migration is lossy: a system-blueprint row (tenant_id IS NULL) has no
            // tenant to fall back to. Fail loudly rather than silently stamping Guid.Empty, which the
            // domain rejects as a scope (redesign spec §4).
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM tenant_notification_templates WHERE tenant_id IS NULL)
                       OR EXISTS (SELECT 1 FROM template_constraints WHERE tenant_id IS NULL) THEN
                        RAISE EXCEPTION 'Cannot revert MakeNotificationTemplateTenantNullable: system-blueprint notification template rows exist (tenant_id IS NULL). Delete or adopt them before downgrading.';
                    END IF;
                END $$;
                """);


            migrationBuilder.AlterColumn<Guid>(
                name: "tenant_id",
                table: "tenant_notification_templates",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "tenant_id",
                table: "template_constraints",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
