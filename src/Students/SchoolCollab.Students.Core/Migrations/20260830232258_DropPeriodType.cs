using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class DropPeriodType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_periods_one_active_sub_period",
                table: "periods");

            migrationBuilder.DropIndex(
                name: "ix_periods_one_active_year",
                table: "periods");

            migrationBuilder.DropIndex(
                name: "ix_periods_tenant_type_status",
                table: "periods");

            migrationBuilder.DropColumn(
                name: "period_type",
                table: "periods");

            migrationBuilder.AlterColumn<int>(
                name: "division",
                table: "periods",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_periods_one_active_sub_period",
                table: "periods",
                columns: new[] { "tenant_id", "parent_period_id" },
                unique: true,
                filter: "parent_period_id IS NOT NULL AND status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_periods_one_active_year",
                table: "periods",
                column: "tenant_id",
                unique: true,
                filter: "parent_period_id IS NULL AND status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_periods_tenant_division_status",
                table: "periods",
                columns: new[] { "tenant_id", "division", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_periods_one_active_sub_period",
                table: "periods");

            migrationBuilder.DropIndex(
                name: "ix_periods_one_active_year",
                table: "periods");

            migrationBuilder.DropIndex(
                name: "ix_periods_tenant_division_status",
                table: "periods");

            migrationBuilder.AlterColumn<int>(
                name: "division",
                table: "periods",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "period_type",
                table: "periods",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_periods_one_active_sub_period",
                table: "periods",
                columns: new[] { "tenant_id", "parent_period_id", "period_type" },
                unique: true,
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_periods_one_active_year",
                table: "periods",
                column: "tenant_id",
                unique: true,
                filter: "period_type = 0 AND status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_periods_tenant_type_status",
                table: "periods",
                columns: new[] { "tenant_id", "period_type", "status" });
        }
    }
}
