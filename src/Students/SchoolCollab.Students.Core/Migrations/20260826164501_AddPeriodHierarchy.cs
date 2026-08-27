using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "parent_period_id",
                table: "periods",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "period_type",
                table: "periods",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_periods_parent_period_id",
                table: "periods",
                column: "parent_period_id");

            migrationBuilder.CreateIndex(
                name: "ix_periods_tenant_parent_status",
                table: "periods",
                columns: new[] { "tenant_id", "parent_period_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_periods_tenant_type_status",
                table: "periods",
                columns: new[] { "tenant_id", "period_type", "status" });

            migrationBuilder.AddForeignKey(
                name: "fk_periods_periods_parent_period_id",
                table: "periods",
                column: "parent_period_id",
                principalTable: "periods",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_periods_periods_parent_period_id",
                table: "periods");

            migrationBuilder.DropIndex(
                name: "ix_periods_parent_period_id",
                table: "periods");

            migrationBuilder.DropIndex(
                name: "ix_periods_tenant_parent_status",
                table: "periods");

            migrationBuilder.DropIndex(
                name: "ix_periods_tenant_type_status",
                table: "periods");

            migrationBuilder.DropColumn(
                name: "parent_period_id",
                table: "periods");

            migrationBuilder.DropColumn(
                name: "period_type",
                table: "periods");
        }
    }
}
