using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Assignments.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentRecipientShortLinkAndPublishTemplateStamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "publish_email_template_id",
                table: "assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "publish_sms_template_id",
                table: "assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "short_link_code",
                table: "assignment_recipients",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_assignment_recipients_short_link_code",
                table: "assignment_recipients",
                column: "short_link_code",
                unique: true,
                filter: "\"short_link_code\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Exact inverse of Up(): the index goes first, then the columns in reverse order.
            migrationBuilder.DropIndex(
                name: "ux_assignment_recipients_short_link_code",
                table: "assignment_recipients");

            migrationBuilder.DropColumn(
                name: "short_link_code",
                table: "assignment_recipients");

            migrationBuilder.DropColumn(
                name: "publish_sms_template_id",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "publish_email_template_id",
                table: "assignments");
        }
    }
}
