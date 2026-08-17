using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddContactAuditEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_audit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_type = table.Column<int>(type: "integer", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    previous_channel = table.Column<int>(type: "integer", nullable: false),
                    previous_value = table.Column<string>(type: "text", nullable: false),
                    previous_label = table.Column<string>(type: "text", nullable: true),
                    previous_country_code = table.Column<string>(type: "text", nullable: true),
                    new_channel = table.Column<int>(type: "integer", nullable: true),
                    new_value = table.Column<string>(type: "text", nullable: true),
                    new_label = table.Column<string>(type: "text", nullable: true),
                    new_country_code = table.Column<string>(type: "text", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    actor_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_audit_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contact_audit_entries_tenant_contact",
                table: "contact_audit_entries",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contact_audit_entries_tenant_owner_occurred",
                table: "contact_audit_entries",
                columns: new[] { "tenant_id", "owner_type", "owner_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_audit_entries");
        }
    }
}
