using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTeacherGradeSubjectAndActivityAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "teacher_topics");

            migrationBuilder.DropIndex(
                name: "ix_teacher_grade_levels_tenant_teacher_grade_level",
                table: "teacher_grade_levels");

            migrationBuilder.AddColumn<Guid>(
                name: "topic_id",
                table: "teacher_grade_levels",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "teacher_activity_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_coded_value_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teacher_activity_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_teacher_activity_assignments_activity_groups_activity_group",
                        column: x => x.activity_group_id,
                        principalTable: "activity_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_teacher_activity_assignments_teachers_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "teachers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "teacher_activity_assignment_grades",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_activity_assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grade_level_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teacher_activity_assignment_grades", x => x.id);
                    table.ForeignKey(
                        name: "fk_teacher_activity_assignment_grades_teacher_activity_assignm",
                        column: x => x.teacher_activity_assignment_id,
                        principalTable: "teacher_activity_assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_teacher_grade_levels_tenant_teacher_grade_unique_no_topic",
                table: "teacher_grade_levels",
                columns: new[] { "tenant_id", "teacher_id", "grade_level_id" },
                unique: true,
                filter: "\"topic_id\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_teac_act_grade_tenant_assignment",
                table: "teacher_activity_assignment_grades",
                columns: new[] { "tenant_id", "teacher_activity_assignment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_teac_act_grade_tenant_grade",
                table: "teacher_activity_assignment_grades",
                columns: new[] { "tenant_id", "grade_level_id" });

            migrationBuilder.CreateIndex(
                name: "ix_teacher_activity_assignment_grades_teacher_activity_assignm",
                table: "teacher_activity_assignment_grades",
                column: "teacher_activity_assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_teacher_activity_assignments_activity_group_id",
                table: "teacher_activity_assignments",
                column: "activity_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_teacher_activity_assignments_teacher_id",
                table: "teacher_activity_assignments",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "ix_teacher_activity_assignments_tenant_activity",
                table: "teacher_activity_assignments",
                columns: new[] { "tenant_id", "activity_group_id" });

            migrationBuilder.CreateIndex(
                name: "ix_teacher_activity_assignments_tenant_teacher",
                table: "teacher_activity_assignments",
                columns: new[] { "tenant_id", "teacher_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "teacher_activity_assignment_grades");

            migrationBuilder.DropTable(
                name: "teacher_activity_assignments");

            migrationBuilder.DropIndex(
                name: "ix_teacher_grade_levels_tenant_teacher_grade_unique_no_topic",
                table: "teacher_grade_levels");

            migrationBuilder.DropColumn(
                name: "topic_id",
                table: "teacher_grade_levels");

            migrationBuilder.CreateTable(
                name: "teacher_topics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    role_coded_value_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teacher_topics", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_teacher_grade_levels_tenant_teacher_grade_level",
                table: "teacher_grade_levels",
                columns: new[] { "tenant_id", "teacher_id", "grade_level_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_teacher_topics_tenant_teacher_topic",
                table: "teacher_topics",
                columns: new[] { "tenant_id", "teacher_id", "topic_id" },
                unique: true);
        }
    }
}
