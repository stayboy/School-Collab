using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Settings.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantIdToCodedValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FR-5 / §9.1: add the hybrid tenant_id column. Existing rows are the
            // shared CSV-seeded blueprint — they are NULL by default (no backfill
            // needed; the column is nullable). Existing TenantCodedValueOverride
            // rows keep targeting these now-NULL rows via GlobalCodedValueId.
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "coded_values",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_coded_values_owned_tenant_parent",
                table: "coded_values",
                columns: new[] { "tenant_id", "parent_id" },
                filter: "tenant_id IS NOT NULL");

            // FR-7 / §3.4: two partial UNIQUE indexes that backstop the duplicate-code
            // guard. COALESCE(parent_id, sentinel) gives root values (parent_id IS
            // NULL) a synthetic sentinel so their codes stay unique among roots, while
            // child codes are scoped to their parent. EF Core cannot express COALESCE
            // in index columns, so these are raw SQL (the established precedent).
            const string sharedUnique = @"
CREATE UNIQUE INDEX uq_coded_values_shared_parent_code
    ON coded_values (COALESCE(parent_id, '00000000-0000-0000-0000-000000000000'::uuid), code)
    WHERE tenant_id IS NULL";
            const string ownedUnique = @"
CREATE UNIQUE INDEX uq_coded_values_owned_tenant_parent_code
    ON coded_values (tenant_id, COALESCE(parent_id, '00000000-0000-0000-0000-000000000000'::uuid), code)
    WHERE tenant_id IS NOT NULL";
            migrationBuilder.Sql(sharedUnique);
            migrationBuilder.Sql(ownedUnique);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS uq_coded_values_owned_tenant_parent_code");
            migrationBuilder.Sql("DROP INDEX IF EXISTS uq_coded_values_shared_parent_code");

            migrationBuilder.DropIndex(
                name: "ix_coded_values_owned_tenant_parent",
                table: "coded_values");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "coded_values");
        }
    }
}
