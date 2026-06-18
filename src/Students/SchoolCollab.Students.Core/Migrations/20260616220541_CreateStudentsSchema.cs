using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class CreateStudentsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "grade_levels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    coded_value_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grade_levels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "grade_subject_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    grade_level_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grade_subject_assignments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    allow_subject_overrides = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    next_period_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_periods", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "student_enrollments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grade_level_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrolled_on = table.Column<DateOnly>(type: "date", nullable: false),
                    exit_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_enrollments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "student_subject_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_override = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    source_type = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_subject_assignments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "students",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    first_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    last_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    gender_coded_value_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_email = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    contact_phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_students", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subjects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    coded_value_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subjects", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_grade_levels_coded_value_id",
                table: "grade_levels",
                column: "coded_value_id");

            migrationBuilder.CreateIndex(
                name: "ix_grade_levels_level",
                table: "grade_levels",
                column: "level");

            migrationBuilder.CreateIndex(
                name: "ix_grade_subject_assignments_period",
                table: "grade_subject_assignments",
                column: "period_id");

            migrationBuilder.CreateIndex(
                name: "ix_grade_subject_assignments_unique",
                table: "grade_subject_assignments",
                columns: new[] { "grade_level_id", "subject_id", "period_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dispatched_at",
                table: "outbox_messages",
                column: "dispatched_at");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                table: "outbox_messages",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_periods_start_date",
                table: "periods",
                column: "start_date");

            migrationBuilder.CreateIndex(
                name: "ix_periods_status",
                table: "periods",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_student_enrollments_grade_level",
                table: "student_enrollments",
                column: "grade_level_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_enrollments_period",
                table: "student_enrollments",
                column: "period_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_enrollments_status",
                table: "student_enrollments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_student_enrollments_student_period",
                table: "student_enrollments",
                columns: new[] { "student_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "ix_student_subject_assignments_period",
                table: "student_subject_assignments",
                column: "period_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_subject_assignments_unique",
                table: "student_subject_assignments",
                columns: new[] { "student_id", "subject_id", "period_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_students_gender_cv_id",
                table: "students",
                column: "gender_coded_value_id");

            migrationBuilder.CreateIndex(
                name: "ix_students_is_deleted",
                table: "students",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_students_student_number",
                table: "students",
                column: "student_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subjects_code",
                table: "subjects",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subjects_coded_value_id",
                table: "subjects",
                column: "coded_value_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "grade_levels");

            migrationBuilder.DropTable(
                name: "grade_subject_assignments");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "periods");

            migrationBuilder.DropTable(
                name: "student_enrollments");

            migrationBuilder.DropTable(
                name: "student_subject_assignments");

            migrationBuilder.DropTable(
                name: "students");

            migrationBuilder.DropTable(
                name: "subjects");
        }
    }
}
