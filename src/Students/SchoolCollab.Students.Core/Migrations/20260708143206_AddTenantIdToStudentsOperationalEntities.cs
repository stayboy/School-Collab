using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantIdToStudentsOperationalEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_subjects_code",
                table: "subjects");

            migrationBuilder.DropIndex(
                name: "ix_subjects_coded_value_id",
                table: "subjects");

            migrationBuilder.DropIndex(
                name: "ix_students_student_number",
                table: "students");

            migrationBuilder.DropIndex(
                name: "ix_student_subject_assignments_period",
                table: "student_subject_assignments");

            migrationBuilder.DropIndex(
                name: "ix_student_subject_assignments_unique",
                table: "student_subject_assignments");

            migrationBuilder.DropIndex(
                name: "ix_student_enrollments_grade_level",
                table: "student_enrollments");

            migrationBuilder.DropIndex(
                name: "ix_student_enrollments_period",
                table: "student_enrollments");

            migrationBuilder.DropIndex(
                name: "ix_student_enrollments_student_period",
                table: "student_enrollments");

            migrationBuilder.DropIndex(
                name: "ix_periods_status",
                table: "periods");

            migrationBuilder.DropIndex(
                name: "ix_grade_subject_assignments_period",
                table: "grade_subject_assignments");

            migrationBuilder.DropIndex(
                name: "ix_grade_subject_assignments_unique",
                table: "grade_subject_assignments");

            migrationBuilder.DropIndex(
                name: "ix_grade_levels_coded_value_id",
                table: "grade_levels");

            migrationBuilder.RenameIndex(
                name: "ix_subject_strands_subject",
                table: "subject_strands",
                newName: "ix_subject_strands_subject_id");

            migrationBuilder.RenameIndex(
                name: "ix_subject_lessons_subject",
                table: "subject_lessons",
                newName: "ix_subject_lessons_subject_id");

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "subjects",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "subject_strands",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "subject_lessons",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "student_subject_assignments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "student_enrollments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "periods",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "grade_subject_assignments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "grade_levels",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // ── §9.3 backfill: attribute existing rows to a tenant BEFORE the unique
            //    composite indexes are created (otherwise the sentinel Guid.Empty
            //    default would violate (tenant_id, …) uniqueness). Orphan rows
            //    (Guid.Empty) → the well-known System tenant sink (Q-1). Child rows
            //    inherit their parent's tenant. ──
            migrationBuilder.Sql(@"
UPDATE students SET tenant_id = '00000000-0000-0000-0000-000000000001' WHERE tenant_id = '00000000-0000-0000-0000-000000000000';
UPDATE grade_levels SET tenant_id = '00000000-0000-0000-0000-000000000001' WHERE tenant_id = '00000000-0000-0000-0000-000000000000';
UPDATE subjects SET tenant_id = '00000000-0000-0000-0000-000000000001' WHERE tenant_id = '00000000-0000-0000-0000-000000000000';
UPDATE periods SET tenant_id = '00000000-0000-0000-0000-000000000001' WHERE tenant_id = '00000000-0000-0000-0000-000000000000';
UPDATE student_enrollments se SET tenant_id = s.tenant_id FROM students s WHERE se.student_id = s.id AND se.tenant_id = '00000000-0000-0000-0000-000000000000';
UPDATE student_subject_assignments ssa SET tenant_id = s.tenant_id FROM students s WHERE ssa.student_id = s.id AND ssa.tenant_id = '00000000-0000-0000-0000-000000000000';
UPDATE grade_subject_assignments gsa SET tenant_id = gl.tenant_id FROM grade_levels gl WHERE gsa.grade_level_id = gl.id AND gsa.tenant_id = '00000000-0000-0000-0000-000000000000';
UPDATE subject_strands ss SET tenant_id = sub.tenant_id FROM subjects sub WHERE ss.subject_id = sub.id AND ss.tenant_id = '00000000-0000-0000-0000-000000000000';
UPDATE subject_lessons sl SET tenant_id = sub.tenant_id FROM subjects sub WHERE sl.subject_id = sub.id AND sl.tenant_id = '00000000-0000-0000-0000-000000000000';
-- Drop the sentinel defaults so future inserts MUST supply a tenant (enforced by
-- the ModuleDbContext save-guard / auto-stamp).
ALTER TABLE students ALTER COLUMN tenant_id DROP DEFAULT;
ALTER TABLE grade_levels ALTER COLUMN tenant_id DROP DEFAULT;
ALTER TABLE subjects ALTER COLUMN tenant_id DROP DEFAULT;
ALTER TABLE periods ALTER COLUMN tenant_id DROP DEFAULT;
ALTER TABLE student_enrollments ALTER COLUMN tenant_id DROP DEFAULT;
ALTER TABLE grade_subject_assignments ALTER COLUMN tenant_id DROP DEFAULT;
ALTER TABLE student_subject_assignments ALTER COLUMN tenant_id DROP DEFAULT;
ALTER TABLE subject_strands ALTER COLUMN tenant_id DROP DEFAULT;
ALTER TABLE subject_lessons ALTER COLUMN tenant_id DROP DEFAULT;
");

            migrationBuilder.CreateIndex(
                name: "ix_subjects_tenant_code",
                table: "subjects",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subjects_tenant_coded_value_id",
                table: "subjects",
                columns: new[] { "tenant_id", "coded_value_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subject_strands_tenant_subject",
                table: "subject_strands",
                columns: new[] { "tenant_id", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_subject_lessons_tenant_subject",
                table: "subject_lessons",
                columns: new[] { "tenant_id", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_students_tenant_student_number",
                table: "students",
                columns: new[] { "tenant_id", "student_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_student_subject_assignments_tenant_period",
                table: "student_subject_assignments",
                columns: new[] { "tenant_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "ix_student_subject_assignments_tenant_unique",
                table: "student_subject_assignments",
                columns: new[] { "tenant_id", "student_id", "subject_id", "period_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_student_enrollments_tenant_grade_level",
                table: "student_enrollments",
                columns: new[] { "tenant_id", "grade_level_id" });

            migrationBuilder.CreateIndex(
                name: "ix_student_enrollments_tenant_period",
                table: "student_enrollments",
                columns: new[] { "tenant_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "ix_student_enrollments_tenant_student_period",
                table: "student_enrollments",
                columns: new[] { "tenant_id", "student_id", "period_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_periods_tenant_status",
                table: "periods",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_grade_subject_assignments_tenant_period",
                table: "grade_subject_assignments",
                columns: new[] { "tenant_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "ix_grade_subject_assignments_tenant_unique",
                table: "grade_subject_assignments",
                columns: new[] { "tenant_id", "grade_level_id", "subject_id", "period_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_grade_levels_tenant_coded_value_id",
                table: "grade_levels",
                columns: new[] { "tenant_id", "coded_value_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_subjects_tenant_code",
                table: "subjects");

            migrationBuilder.DropIndex(
                name: "ix_subjects_tenant_coded_value_id",
                table: "subjects");

            migrationBuilder.DropIndex(
                name: "ix_subject_strands_tenant_subject",
                table: "subject_strands");

            migrationBuilder.DropIndex(
                name: "ix_subject_lessons_tenant_subject",
                table: "subject_lessons");

            migrationBuilder.DropIndex(
                name: "ix_students_tenant_student_number",
                table: "students");

            migrationBuilder.DropIndex(
                name: "ix_student_subject_assignments_tenant_period",
                table: "student_subject_assignments");

            migrationBuilder.DropIndex(
                name: "ix_student_subject_assignments_tenant_unique",
                table: "student_subject_assignments");

            migrationBuilder.DropIndex(
                name: "ix_student_enrollments_tenant_grade_level",
                table: "student_enrollments");

            migrationBuilder.DropIndex(
                name: "ix_student_enrollments_tenant_period",
                table: "student_enrollments");

            migrationBuilder.DropIndex(
                name: "ix_student_enrollments_tenant_student_period",
                table: "student_enrollments");

            migrationBuilder.DropIndex(
                name: "ix_periods_tenant_status",
                table: "periods");

            migrationBuilder.DropIndex(
                name: "ix_grade_subject_assignments_tenant_period",
                table: "grade_subject_assignments");

            migrationBuilder.DropIndex(
                name: "ix_grade_subject_assignments_tenant_unique",
                table: "grade_subject_assignments");

            migrationBuilder.DropIndex(
                name: "ix_grade_levels_tenant_coded_value_id",
                table: "grade_levels");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "subjects");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "subject_strands");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "subject_lessons");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "student_subject_assignments");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "student_enrollments");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "periods");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "grade_subject_assignments");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "grade_levels");

            migrationBuilder.RenameIndex(
                name: "ix_subject_strands_subject_id",
                table: "subject_strands",
                newName: "ix_subject_strands_subject");

            migrationBuilder.RenameIndex(
                name: "ix_subject_lessons_subject_id",
                table: "subject_lessons",
                newName: "ix_subject_lessons_subject");

            migrationBuilder.CreateIndex(
                name: "ix_subjects_code",
                table: "subjects",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subjects_coded_value_id",
                table: "subjects",
                column: "coded_value_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_students_student_number",
                table: "students",
                column: "student_number",
                unique: true);

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
                name: "ix_student_enrollments_grade_level",
                table: "student_enrollments",
                column: "grade_level_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_enrollments_period",
                table: "student_enrollments",
                column: "period_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_enrollments_student_period",
                table: "student_enrollments",
                columns: new[] { "student_id", "period_id" });

            migrationBuilder.CreateIndex(
                name: "ix_periods_status",
                table: "periods",
                column: "status");

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
                name: "ix_grade_levels_coded_value_id",
                table: "grade_levels",
                column: "coded_value_id",
                unique: true);
        }
    }
}
