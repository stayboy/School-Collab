using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityGroupRev2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_activity_groups_periods_period_id",
                table: "activity_groups");

            migrationBuilder.DropIndex(
                name: "ix_activity_groups_period_id",
                table: "activity_groups");

            migrationBuilder.DropIndex(
                name: "ix_activity_groups_tenant_period",
                table: "activity_groups");

            migrationBuilder.DropIndex(
                name: "ix_activity_groups_tenant_status",
                table: "activity_groups");

            migrationBuilder.DropIndex(
                name: "ix_agm_tenant_student_group_active",
                table: "activity_group_memberships");

            migrationBuilder.DropColumn(
                name: "period_id",
                table: "activity_groups");

            migrationBuilder.DropColumn(
                name: "status",
                table: "activity_groups");

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "activity_groups",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "period_id",
                table: "activity_group_memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "activity_group_grade_levels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grade_level_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_group_grade_levels", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_group_grade_levels_activity_groups_activity_group_",
                        column: x => x.activity_group_id,
                        principalTable: "activity_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_activity_group_grade_levels_grade_levels_grade_level_id",
                        column: x => x.grade_level_id,
                        principalTable: "grade_levels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_groups_tenant_active",
                table: "activity_groups",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_group_memberships_period_id",
                table: "activity_group_memberships",
                column: "period_id");

            migrationBuilder.CreateIndex(
                name: "ix_agm_tenant_student_group_active",
                table: "activity_group_memberships",
                columns: new[] { "tenant_id", "student_id", "activity_group_id" },
                unique: true,
                filter: "status = 0 AND period_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_agm_tenant_student_group_period_active",
                table: "activity_group_memberships",
                columns: new[] { "tenant_id", "student_id", "activity_group_id", "period_id" },
                unique: true,
                filter: "status = 0 AND period_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_activity_group_grade_levels_activity_group_id",
                table: "activity_group_grade_levels",
                column: "activity_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_group_grade_levels_grade_level_id",
                table: "activity_group_grade_levels",
                column: "grade_level_id");

            migrationBuilder.CreateIndex(
                name: "ix_agg_tenant_grade",
                table: "activity_group_grade_levels",
                columns: new[] { "tenant_id", "grade_level_id" });

            migrationBuilder.CreateIndex(
                name: "ix_agg_tenant_group_grade_unique",
                table: "activity_group_grade_levels",
                columns: new[] { "tenant_id", "activity_group_id", "grade_level_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_activity_group_memberships_periods_period_id",
                table: "activity_group_memberships",
                column: "period_id",
                principalTable: "periods",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_activity_group_memberships_periods_period_id",
                table: "activity_group_memberships");

            migrationBuilder.DropTable(
                name: "activity_group_grade_levels");

            migrationBuilder.DropIndex(
                name: "ix_activity_groups_tenant_active",
                table: "activity_groups");

            migrationBuilder.DropIndex(
                name: "ix_activity_group_memberships_period_id",
                table: "activity_group_memberships");

            migrationBuilder.DropIndex(
                name: "ix_agm_tenant_student_group_active",
                table: "activity_group_memberships");

            migrationBuilder.DropIndex(
                name: "ix_agm_tenant_student_group_period_active",
                table: "activity_group_memberships");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "activity_groups");

            migrationBuilder.DropColumn(
                name: "period_id",
                table: "activity_group_memberships");

            migrationBuilder.AddColumn<Guid>(
                name: "period_id",
                table: "activity_groups",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "status",
                table: "activity_groups",
                type: "integer",
                nullable: false,
                defaultValue: 0);

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

            migrationBuilder.CreateIndex(
                name: "ix_agm_tenant_student_group_active",
                table: "activity_group_memberships",
                columns: new[] { "tenant_id", "student_id", "activity_group_id" },
                unique: true,
                filter: "status = 0");

            migrationBuilder.AddForeignKey(
                name: "fk_activity_groups_periods_period_id",
                table: "activity_groups",
                column: "period_id",
                principalTable: "periods",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
