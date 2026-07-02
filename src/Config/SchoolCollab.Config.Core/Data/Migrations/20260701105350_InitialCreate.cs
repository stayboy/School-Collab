using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Config.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feature_flags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Boolean"),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_flags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "flag_audit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    feature_flag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_flag_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    change_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    previous_is_enabled = table.Column<bool>(type: "boolean", nullable: true),
                    new_is_enabled = table.Column<bool>(type: "boolean", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    actor_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    actor_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_flag_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_flag_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_flag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_flag_overrides", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_flag_overrides_feature_flags_feature_flag_id",
                        column: x => x.feature_flag_id,
                        principalTable: "feature_flags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_feature_flags_key_unique",
                table: "feature_flags",
                column: "key",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_flag_audit_entries_flag_occurred",
                table: "flag_audit_entries",
                columns: new[] { "feature_flag_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_flag_audit_entries_tenant_occurred",
                table: "flag_audit_entries",
                columns: new[] { "tenant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "dispatched_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_flag_overrides_flag",
                table: "tenant_flag_overrides",
                column: "feature_flag_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_flag_overrides_unique",
                table: "tenant_flag_overrides",
                columns: new[] { "tenant_id", "feature_flag_id" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flag_audit_entries");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "tenant_flag_overrides");

            migrationBuilder.DropTable(
                name: "feature_flags");
        }
    }
}
