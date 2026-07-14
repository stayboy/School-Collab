using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddGuardiansContactsTeachers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<int>(type: "integer", nullable: false),
                    scope_ref_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_subscriptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_type = table.Column<int>(type: "integer", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    label = table.Column<string>(type: "text", nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contacts", x => x.id);
                });

            // Unique index MUST exist before the backfill below, otherwise its
            // ON CONFLICT (tenant_id, owner_type, owner_id, channel, value) fails
            // with "no unique or exclusion constraint matching the ON CONFLICT".
            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_owner_channel_value",
                table: "contacts",
                columns: new[] { "tenant_id", "owner_type", "owner_id", "channel", "value" },
                unique: true);

            // ── Legacy-contact backfill (M1) ───────────────────────────────────
            // Migrate existing student email/phone into the new multi-channel
            // contacts table. Email -> channel 0 (Email); Phone -> channel 1 (SMS).
            // Contacts default to unverified; subscriptions default to opted-out
            // (no contact_subscriptions row is created here). Runs before the
            // student.contact_email / contact_phone columns are dropped below.
            migrationBuilder.Sql(
                "INSERT INTO contacts (id, owner_type, owner_id, channel, value, label, is_primary, is_verified, tenant_id, is_deleted, created_at, updated_at) " +
                "SELECT gen_random_uuid(), 0, id, 0, contact_email, NULL, TRUE, FALSE, tenant_id, FALSE, now(), now() " +
                "FROM students WHERE contact_email IS NOT NULL AND contact_email <> '' AND NOT is_deleted " +
                "ON CONFLICT (tenant_id, owner_type, owner_id, channel, value) DO NOTHING;");
            migrationBuilder.Sql(
                "INSERT INTO contacts (id, owner_type, owner_id, channel, value, label, is_primary, is_verified, tenant_id, is_deleted, created_at, updated_at) " +
                "SELECT gen_random_uuid(), 0, id, 1, contact_phone, NULL, TRUE, FALSE, tenant_id, FALSE, now(), now() " +
                "FROM students WHERE contact_phone IS NOT NULL AND contact_phone <> '' AND NOT is_deleted " +
                "ON CONFLICT (tenant_id, owner_type, owner_id, channel, value) DO NOTHING;");

            migrationBuilder.CreateTable(
                name: "guardian_name_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    guardian_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "text", nullable: false),
                    last_name = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guardian_name_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "guardians",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title_coded_value_id = table.Column<Guid>(type: "uuid", nullable: true),
                    first_name = table.Column<string>(type: "text", nullable: false),
                    last_name = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    community_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guardians", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "student_guardians",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guardian_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relationship_coded_value_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role = table.Column<int>(type: "integer", nullable: false),
                    is_emergency_contact = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_by_guardian_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_guardians", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "teacher_grade_levels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grade_level_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teacher_grade_levels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "teacher_subjects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teacher_subjects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "teachers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title_coded_value_id = table.Column<Guid>(type: "uuid", nullable: true),
                    first_name = table.Column<string>(type: "text", nullable: false),
                    last_name = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: false),
                    contact_phone = table.Column<string>(type: "text", nullable: true),
                    staff_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teachers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contact_subscriptions_tenant_contact_scope",
                table: "contact_subscriptions",
                columns: new[] { "tenant_id", "contact_id", "scope", "scope_ref_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_owner",
                table: "contacts",
                columns: new[] { "tenant_id", "owner_type", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_guardian_name_history_tenant_guardian",
                table: "guardian_name_history",
                columns: new[] { "tenant_id", "guardian_id" });

            migrationBuilder.CreateIndex(
                name: "ix_guardians_tenant_last_name",
                table: "guardians",
                columns: new[] { "tenant_id", "last_name" });

            migrationBuilder.CreateIndex(
                name: "ix_student_guardians_tenant_guardian",
                table: "student_guardians",
                columns: new[] { "tenant_id", "guardian_id" });

            migrationBuilder.CreateIndex(
                name: "ix_student_guardians_tenant_student_guardian",
                table: "student_guardians",
                columns: new[] { "tenant_id", "student_id", "guardian_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_teacher_grade_levels_tenant_teacher_grade_level",
                table: "teacher_grade_levels",
                columns: new[] { "tenant_id", "teacher_id", "grade_level_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_teacher_subjects_tenant_teacher_subject",
                table: "teacher_subjects",
                columns: new[] { "tenant_id", "teacher_id", "subject_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_teachers_tenant_last_name",
                table: "teachers",
                columns: new[] { "tenant_id", "last_name" });

            migrationBuilder.DropColumn(
                name: "contact_email",
                table: "students");

            migrationBuilder.DropColumn(
                name: "contact_phone",
                table: "students");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_subscriptions");

            migrationBuilder.DropTable(
                name: "contacts");

            migrationBuilder.DropTable(
                name: "guardian_name_history");

            migrationBuilder.DropTable(
                name: "guardians");

            migrationBuilder.DropTable(
                name: "student_guardians");

            migrationBuilder.DropTable(
                name: "teacher_grade_levels");

            migrationBuilder.DropTable(
                name: "teacher_subjects");

            migrationBuilder.DropTable(
                name: "teachers");

            migrationBuilder.AddColumn<string>(
                name: "contact_email",
                table: "students",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "contact_phone",
                table: "students",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }
    }
}
