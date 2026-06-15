using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.CodedValues.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class ScopeUniqueIndexToParentCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the old non-unique index on code (EF created it as part of the model)
            migrationBuilder.DropIndex(
                name: "ix_coded_values_code",
                table: "coded_values");

            // Create a non-unique index on code for lookup performance
            migrationBuilder.CreateIndex(
                name: "ix_coded_values_code",
                table: "coded_values",
                column: "code");

            // Create a unique partial index on (COALESCE(parent_id, sentinel), code)
            // so that codes are unique within their parent scope (roots share a sentinel).
            // PostgreSQL treats NULL != NULL, so a plain composite unique index on (parent_id, code)
            // would allow duplicate root codes. COALESCE maps NULL parent_id to a sentinel UUID.
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX ix_coded_values_parent_code
    ON coded_values (COALESCE(parent_id, '00000000-0000-0000-0000-000000000000'), code)
    WHERE is_deleted = false;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_coded_values_parent_code;");

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
    }
}
