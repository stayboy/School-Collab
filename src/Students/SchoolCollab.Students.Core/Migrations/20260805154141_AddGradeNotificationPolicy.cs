using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddGradeNotificationPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "grade_notification_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    grade_level_id = table.Column<Guid>(type: "uuid", nullable: false),
                    preferred_channel_order = table.Column<string>(type: "jsonb", nullable: true),
                    blocked_channels = table.Column<string>(type: "jsonb", nullable: true),
                    max_notifications = table.Column<int>(type: "integer", nullable: true),
                    max_reminders = table.Column<int>(type: "integer", nullable: true),
                    reminder_interval_hours = table.Column<int>(type: "integer", nullable: true),
                    link_validity_days = table.Column<int>(type: "integer", nullable: true),
                    sendout_time_of_day = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    sendout_interval_minutes = table.Column<int>(type: "integer", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grade_notification_policies", x => x.id);
                    table.ForeignKey(
                        name: "fk_grade_notification_policies_grade_levels_grade_level_id",
                        column: x => x.grade_level_id,
                        principalTable: "grade_levels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_grade_notification_policies_grade_level_id",
                table: "grade_notification_policies",
                column: "grade_level_id");

            migrationBuilder.CreateIndex(
                name: "ix_grade_notification_policies_tenant_grade",
                table: "grade_notification_policies",
                columns: new[] { "tenant_id", "grade_level_id" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "grade_notification_policies");
        }
    }
}
