using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddActivePeriodUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
