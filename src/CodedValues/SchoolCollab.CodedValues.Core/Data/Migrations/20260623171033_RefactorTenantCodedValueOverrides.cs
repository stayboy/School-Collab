using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.CodedValues.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class RefactorTenantCodedValueOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "code",
                table: "tenant_coded_value_overrides");

            migrationBuilder.DropColumn(
                name: "is_disabled",
                table: "tenant_coded_value_overrides");

            migrationBuilder.DropColumn(
                name: "name",
                table: "tenant_coded_value_overrides");

            migrationBuilder.RenameColumn(
                name: "coded_value_id",
                table: "tenant_coded_value_overrides",
                newName: "global_coded_value_id");

            migrationBuilder.RenameIndex(
                name: "ix_tenant_coded_value_overrides_tenant_id_coded_value_id",
                table: "tenant_coded_value_overrides",
                newName: "ix_tenant_coded_value_overrides_unique");

            migrationBuilder.RenameColumn(
                name: "coded_value_id",
                table: "tenant_coded_value_attribute_overrides",
                newName: "global_coded_value_id");

            migrationBuilder.RenameIndex(
                name: "ix_tenant_coded_value_attribute_overrides_tenant_id_coded_valu",
                table: "tenant_coded_value_attribute_overrides",
                newName: "ix_tenant_coded_value_attribute_overrides_tenant_id_global_cod");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                table: "tenant_coded_value_overrides",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "overridden_description",
                table: "tenant_coded_value_overrides",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "overridden_name",
                table: "tenant_coded_value_overrides",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "tenant_coded_value_overrides",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateIndex(
                name: "ix_tenant_coded_value_overrides_tenant",
                table: "tenant_coded_value_overrides",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tenant_coded_value_overrides_tenant",
                table: "tenant_coded_value_overrides");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "tenant_coded_value_overrides");

            migrationBuilder.DropColumn(
                name: "overridden_description",
                table: "tenant_coded_value_overrides");

            migrationBuilder.DropColumn(
                name: "overridden_name",
                table: "tenant_coded_value_overrides");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "tenant_coded_value_overrides");

            migrationBuilder.RenameColumn(
                name: "global_coded_value_id",
                table: "tenant_coded_value_overrides",
                newName: "coded_value_id");

            migrationBuilder.RenameIndex(
                name: "ix_tenant_coded_value_overrides_unique",
                table: "tenant_coded_value_overrides",
                newName: "ix_tenant_coded_value_overrides_tenant_id_coded_value_id");

            migrationBuilder.RenameColumn(
                name: "global_coded_value_id",
                table: "tenant_coded_value_attribute_overrides",
                newName: "coded_value_id");

            migrationBuilder.RenameIndex(
                name: "ix_tenant_coded_value_attribute_overrides_tenant_id_global_cod",
                table: "tenant_coded_value_attribute_overrides",
                newName: "ix_tenant_coded_value_attribute_overrides_tenant_id_coded_valu");

            migrationBuilder.AddColumn<string>(
                name: "code",
                table: "tenant_coded_value_overrides",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_disabled",
                table: "tenant_coded_value_overrides",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "tenant_coded_value_overrides",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }
    }
}
