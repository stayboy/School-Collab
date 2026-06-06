using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.CodedValues.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueIndexFilterForSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_coded_values_code",
                table: "coded_values");

            migrationBuilder.CreateIndex(
                name: "ix_coded_values_code",
                table: "coded_values",
                column: "code",
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_coded_values_code",
                table: "coded_values");

            migrationBuilder.CreateIndex(
                name: "ix_coded_values_code",
                table: "coded_values",
                column: "code",
                unique: true);
        }
    }
}
