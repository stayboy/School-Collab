using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class MergeLessonsIntoParentedStrands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A lesson is a strand that has a parent (strand-lesson-unification-plan.md).
            // Move each subject_lessons row into subject_strands as a parented strand
            // (parent_strand_id = old strand_id), keeping a lesson_id -> strand_id map
            // so topic_assignments.topic_lesson_id can be backfilled onto topic_strand_id.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE _lesson_strand_map(lesson_id uuid PRIMARY KEY, strand_id uuid NOT NULL);

                INSERT INTO _lesson_strand_map(lesson_id, strand_id)
                SELECT l.id, gen_random_uuid() FROM subject_lessons l;

                INSERT INTO subject_strands(id, tenant_id, topic_id, parent_strand_id, name, description, start_date, end_date, display_order, created_at, updated_at)
                SELECT m.strand_id, l.tenant_id, l.topic_id, l.strand_id, l.name, l.description, l.start_date, l.end_date, l.display_order, l.created_at, l.updated_at
                FROM subject_lessons l
                JOIN _lesson_strand_map m ON m.lesson_id = l.id;

                UPDATE topic_assignments a
                SET topic_strand_id = m.strand_id
                FROM _lesson_strand_map m
                WHERE a.topic_lesson_id = m.lesson_id;

                DROP TABLE _lesson_strand_map;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_topic_assignments_topic_lessons_topic_lesson_id",
                table: "topic_assignments");

            migrationBuilder.DropTable(
                name: "subject_lessons");

            migrationBuilder.DropIndex(
                name: "ix_topic_assignments_topic_lesson_id",
                table: "topic_assignments");

            migrationBuilder.DropColumn(
                name: "topic_lesson_id",
                table: "topic_assignments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "topic_lesson_id",
                table: "topic_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "subject_lessons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    strand_id = table.Column<Guid>(type: "uuid", nullable: true),
                    topic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subject_lessons", x => x.id);
                    table.ForeignKey(
                        name: "fk_subject_lessons_subject_strands_strand_id",
                        column: x => x.strand_id,
                        principalTable: "subject_strands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_subject_lessons_subjects_topic_id",
                        column: x => x.topic_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_topic_assignments_topic_lesson_id",
                table: "topic_assignments",
                column: "topic_lesson_id");

            migrationBuilder.CreateIndex(
                name: "ix_subject_lessons_strand",
                table: "subject_lessons",
                column: "strand_id");

            migrationBuilder.CreateIndex(
                name: "ix_subject_lessons_tenant_subject",
                table: "subject_lessons",
                columns: new[] { "tenant_id", "topic_id" });

            migrationBuilder.CreateIndex(
                name: "ix_subject_lessons_topic_id",
                table: "subject_lessons",
                column: "topic_id");

            migrationBuilder.AddForeignKey(
                name: "fk_topic_assignments_topic_lessons_topic_lesson_id",
                table: "topic_assignments",
                column: "topic_lesson_id",
                principalTable: "subject_lessons",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // Best-effort reversal: rebuild subject_lessons from parented strands and
            // restore topic_lesson_id on assignments that pointed at a lesson.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE _lesson_strand_map(lesson_id uuid PRIMARY KEY, strand_id uuid NOT NULL);
                INSERT INTO _lesson_strand_map(lesson_id, strand_id)
                SELECT id, id FROM subject_strands WHERE parent_strand_id IS NOT NULL;

                INSERT INTO subject_lessons(id, tenant_id, topic_id, strand_id, name, description, start_date, end_date, display_order, created_at, updated_at)
                SELECT s.id, s.tenant_id, s.topic_id, s.parent_strand_id, s.name, s.description, s.start_date, s.end_date, s.display_order, s.created_at, s.updated_at
                FROM subject_strands s
                JOIN _lesson_strand_map m ON m.strand_id = s.id;

                UPDATE topic_assignments a
                SET topic_lesson_id = m.lesson_id, topic_strand_id = NULL
                FROM _lesson_strand_map m
                WHERE a.topic_strand_id = m.strand_id;

                DROP TABLE _lesson_strand_map;
                """);
        }
    }
}
