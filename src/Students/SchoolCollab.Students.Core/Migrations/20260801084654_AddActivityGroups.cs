using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "activity_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    period_id = table.Column<Guid>(type: "uuid", nullable: true),
                    capacity = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_groups", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_groups_periods_period_id",
                        column: x => x.period_id,
                        principalTable: "periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "activity_group_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_on = table.Column<DateOnly>(type: "date", nullable: false),
                    exited_on = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_group_memberships", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_group_memberships_activity_groups_activity_group_id",
                        column: x => x.activity_group_id,
                        principalTable: "activity_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_activity_group_memberships_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_group_memberships_activity_group_id",
                table: "activity_group_memberships",
                column: "activity_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_group_memberships_student_id",
                table: "activity_group_memberships",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_agm_tenant_group_status",
                table: "activity_group_memberships",
                columns: new[] { "tenant_id", "activity_group_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_agm_tenant_student",
                table: "activity_group_memberships",
                columns: new[] { "tenant_id", "student_id" });

            migrationBuilder.CreateIndex(
                name: "ix_agm_tenant_student_group_active",
                table: "activity_group_memberships",
                columns: new[] { "tenant_id", "student_id", "activity_group_id" },
                unique: true,
                filter: "status = 0");

            migrationBuilder.CreateIndex(
                name: "ix_activity_groups_period_id",
                table: "activity_groups",
                column: "period_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_groups_tenant_period",
                table: "activity_groups",
                columns: new[] { "tenant_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_groups_tenant_status",
                table: "activity_groups",
                columns: new[] { "tenant_id", "status" });

            // FR-1 / AC-3: case-insensitive unique group name per tenant. EF Core
            // cannot express the lower() expression, so the unique index is created
            // via raw SQL (mirrors the CodedValue COALESCE pattern). The non-unique
            // EF-tracked ix_activity_groups_tenant_name is intentionally NOT created
            // — this raw SQL index supersedes it.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ix_activity_groups_tenant_name " +
                "ON activity_groups (tenant_id, lower(name))");

            // FR-1: CHECK (capacity >= 1) — defense-in-depth alongside the entity
            // level validation. Only applies when capacity is not NULL.
            migrationBuilder.Sql(
                "ALTER TABLE activity_groups " +
                "ADD CONSTRAINT ck_activity_groups_capacity_min CHECK (capacity IS NULL OR capacity >= 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the raw-SQL unique index and check constraint (not tracked by EF).
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_activity_groups_tenant_name");
            migrationBuilder.Sql("ALTER TABLE activity_groups DROP CONSTRAINT IF EXISTS ck_activity_groups_capacity_min");

            migrationBuilder.DropTable(
                name: "activity_group_memberships");

            migrationBuilder.DropTable(
                name: "activity_groups");
        }
    }
}
