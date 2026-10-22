using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Settings.Core.Migrations
{
    /// <summary>
    /// Naming spec (D1/D6/D8): a notification template gains an operator-visible <c>title</c>, an
    /// optional <c>description</c>, and a generated <c>code</c>.
    /// <para>
    /// <c>title</c> is backfilled <b>inside this migration</b> from the label the codebase derives at
    /// display time today — the subject when present, else the first ~80 characters of the body's
    /// plain text, else <c>"{kind} · {channel}"</c> — and only then made <c>NOT NULL</c>. HTML tags are
    /// stripped before the body snippet is taken, so an email template's markup never lands in the
    /// name; entities and other markup remnants are left alone, because a trimmed snippet of legacy
    /// content is a better name than none.
    /// </para>
    /// <para>
    /// <c>code</c> stays NULL for existing rows on purpose (D7): a null code renders as a dash, which
    /// is how an operator tells a pre-upgrade row from a minted one. The unique index is therefore
    /// filtered to live rows <b>and</b> to rows that carry a code.
    /// </para>
    /// </summary>
    public partial class AddTenantNotificationTemplateTitleDescriptionAndCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "code",
                table: "tenant_notification_templates",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "tenant_notification_templates",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // Added nullable so this migration can backfill first; altered to NOT NULL below.
            migrationBuilder.AddColumn<string>(
                name: "title",
                table: "tenant_notification_templates",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            // Backfill in the same migration, before the column becomes required: subject → first ~80
            // chars of the body's plain text → "{Kind} · {Channel}". Kind/Channel are stored as ints
            // (HasConversion<int>()), so the fallback names them in English here.
            migrationBuilder.Sql(
                """
                UPDATE tenant_notification_templates
                SET title = COALESCE(
                    NULLIF(btrim(subject), ''),
                    NULLIF(
                        btrim(
                            left(
                                regexp_replace(
                                    regexp_replace(body, '<[^>]*>', ' ', 'g'),
                                    '\s+', ' ', 'g'),
                                80)),
                        ''),
                    (CASE kind
                        WHEN 0 THEN 'Publish'
                        WHEN 1 THEN 'Reminder'
                        WHEN 2 THEN 'Completion'
                        WHEN 3 THEN 'Overdue'
                        ELSE 'Template'
                     END)
                    || ' · '
                    || (CASE channel
                        WHEN 0 THEN 'Email'
                        WHEN 1 THEN 'SMS'
                        WHEN 2 THEN 'WhatsApp'
                        ELSE 'Channel'
                     END))
                WHERE title IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "title",
                table: "tenant_notification_templates",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_notification_templates_tenant_code",
                table: "tenant_notification_templates",
                columns: new[] { "tenant_id", "code" },
                unique: true,
                filter: "is_deleted = false AND code IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverting drops all three columns, so every backfilled title and every minted code goes
            // with them — there is nowhere to preserve the values, and the pre-migration schema had no
            // such data at all.
            migrationBuilder.DropIndex(
                name: "ix_tenant_notification_templates_tenant_code",
                table: "tenant_notification_templates");

            migrationBuilder.DropColumn(
                name: "code",
                table: "tenant_notification_templates");

            migrationBuilder.DropColumn(
                name: "description",
                table: "tenant_notification_templates");

            migrationBuilder.DropColumn(
                name: "title",
                table: "tenant_notification_templates");
        }
    }
}
