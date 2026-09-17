using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    delivery_status = table.Column<int>(type: "integer", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    next_retry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    to_address = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    subject = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    body_html = table.Column<string>(type: "text", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notification_logs_assignment_status",
                table: "notification_logs",
                columns: new[] { "assignment_id", "delivery_status" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_logs_tenant_status_next_retry",
                table: "notification_logs",
                columns: new[] { "tenant_id", "delivery_status", "next_retry_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_logs");
        }
    }
}
