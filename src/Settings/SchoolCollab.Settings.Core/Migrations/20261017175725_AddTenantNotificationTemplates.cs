using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Settings.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantNotificationTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "template_audit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    change_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    previous_subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    new_subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    previous_body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    new_body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    previous_constraints = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    new_constraints = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    actor_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    actor_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_notification_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_notification_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "template_constraints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dimension = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_constraints", x => x.id);
                    table.ForeignKey(
                        name: "fk_template_constraints_tenant_notification_templates_template",
                        column: x => x.template_id,
                        principalTable: "tenant_notification_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_template_audit_entries_template_occurred",
                table: "template_audit_entries",
                columns: new[] { "template_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_template_audit_entries_tenant_occurred",
                table: "template_audit_entries",
                columns: new[] { "tenant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_template_constraints_template",
                table: "template_constraints",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_notification_templates_tenant_kind_channel",
                table: "tenant_notification_templates",
                columns: new[] { "tenant_id", "kind", "channel" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "template_audit_entries");

            migrationBuilder.DropTable(
                name: "template_constraints");

            migrationBuilder.DropTable(
                name: "tenant_notification_templates");
        }
    }
}
