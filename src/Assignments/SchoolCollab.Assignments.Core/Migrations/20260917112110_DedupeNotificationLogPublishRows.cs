using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <inheritdoc />
    public partial class DedupeNotificationLogPublishRows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // E3 (ar-19) decision 3: dedupe FIRST, then enforce the index. Collapse the
            // known F1 duplicate class — multiple `Publish` rows for the same
            // (tenant_id, assignment_id, recipient_id) — keeping the row with the
            // **latest activity** per key: greatest updated_at, tie-broken by highest
            // attempt, then id (the republish re-broadcast row carries the re-minted
            // deep-link token, so latest wins). The legitimate multiple `Reminder` /
            // `Overdue` rows a recipient accrues over a cadence are untouched.
            // One-way transform: deleted rows are NOT restorable (Down() drops only the
            // index) — accepted, documented risk.
            migrationBuilder.Sql(
                """
                WITH publish_rows AS (
                    SELECT
                        id,
                        ROW_NUMBER() OVER (
                            PARTITION BY tenant_id, assignment_id, recipient_id
                            ORDER BY updated_at DESC, attempt DESC, id
                        ) AS rn
                    FROM notification_logs
                    WHERE kind = 0
                )
                DELETE FROM notification_logs
                WHERE id IN (SELECT id FROM publish_rows WHERE rn > 1)
                """);

            migrationBuilder.CreateIndex(
                name: "ux_notification_logs_publish_uniqueness",
                table: "notification_logs",
                columns: new[] { "tenant_id", "assignment_id", "recipient_id" },
                unique: true,
                filter: "\"kind\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_notification_logs_publish_uniqueness",
                table: "notification_logs");
        }
    }
}
