using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <inheritdoc />
    public partial class UseSharedOutboxMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_processed_at",
                table: "outbox_messages");

            migrationBuilder.RenameColumn(
                name: "processed_at",
                table: "outbox_messages",
                newName: "dispatched_at");

            migrationBuilder.RenameColumn(
                name: "error",
                table: "outbox_messages",
                newName: "last_error");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "outbox_messages",
                newName: "occurred_at");

            migrationBuilder.AddColumn<int>(
                name: "attempts",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "dispatched_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_pending",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "attempts",
                table: "outbox_messages");

            migrationBuilder.RenameColumn(
                name: "occurred_at",
                table: "outbox_messages",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "last_error",
                table: "outbox_messages",
                newName: "error");

            migrationBuilder.RenameColumn(
                name: "dispatched_at",
                table: "outbox_messages",
                newName: "processed_at");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at",
                table: "outbox_messages",
                column: "processed_at",
                filter: "processed_at IS NULL");
        }
    }
}
