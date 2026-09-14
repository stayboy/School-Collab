using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Settings.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSignatureConsentText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_signature_consent_texts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    consent_text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_signature_consent_texts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_signature_consent_texts_tenant",
                table: "tenant_signature_consent_texts",
                column: "tenant_id",
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_signature_consent_texts");
        }
    }
}
