using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Settings.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantNotificationPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_notification_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    preferred_channel_order = table.Column<string>(type: "jsonb", nullable: false),
                    blocked_channels = table.Column<string>(type: "jsonb", nullable: false),
                    max_notifications = table.Column<int>(type: "integer", nullable: true),
                    max_reminders = table.Column<int>(type: "integer", nullable: true),
                    reminder_interval_hours = table.Column<int>(type: "integer", nullable: true),
                    link_validity_days = table.Column<int>(type: "integer", nullable: true),
                    sendout_time_of_day = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    sendout_interval_minutes = table.Column<int>(type: "integer", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_notification_policies", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_notification_policies_tenant",
                table: "tenant_notification_policies",
                column: "tenant_id",
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_notification_policies");
        }
    }
}
