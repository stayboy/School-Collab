using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Settings.Core.Migrations
{
    /// <inheritdoc />
    public partial class RelaxTenantNotificationTemplateUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tenant_notification_templates_tenant_kind_channel",
                table: "tenant_notification_templates");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_notification_templates_tenant_kind_channel",
                table: "tenant_notification_templates",
                columns: new[] { "tenant_id", "kind", "channel" },
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tenant_notification_templates_tenant_kind_channel",
                table: "tenant_notification_templates");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_notification_templates_tenant_kind_channel",
                table: "tenant_notification_templates",
                columns: new[] { "tenant_id", "kind", "channel" },
                unique: true,
                filter: "is_deleted = false");
        }
    }
}
