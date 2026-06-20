using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.CodedValues.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_coded_value_attribute_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    coded_value_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    custom_value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_coded_value_attribute_overrides", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_coded_value_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    coded_value_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    is_disabled = table.Column<bool>(type: "boolean", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_coded_value_overrides", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_coded_value_attribute_overrides_tenant_id_coded_valu",
                table: "tenant_coded_value_attribute_overrides",
                columns: new[] { "tenant_id", "coded_value_id", "attribute_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_coded_value_overrides_tenant_id_coded_value_id",
                table: "tenant_coded_value_overrides",
                columns: new[] { "tenant_id", "coded_value_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_coded_value_attribute_overrides");

            migrationBuilder.DropTable(
                name: "tenant_coded_value_overrides");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
