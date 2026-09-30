using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddBridgeDisplayOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "display_order",
                table: "topic_assignments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "display_order",
                table: "grade_stream_assignments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill: preserve the order the UI rendered BEFORE this column existed.
            //
            // Streams — the visible order was today's listing order, i.e. the
            // repository's `ORDER BY created_at` (the coded values live in the
            // Settings database, so this migration cannot join them, and nothing in
            // the UI ever sorted by the coded value's DisplayOrder anyway). The
            // seeder batch-inserts its rows in one AddRange, so created_at ties are
            // expected: `id` is the tiebreak that makes the backfill deterministic
            // and matches the reorder normaliser's (CreatedAt, Id) ordering.
            migrationBuilder.Sql("""
                UPDATE grade_stream_assignments AS gsa
                SET display_order = ranked.position
                FROM (
                    SELECT id,
                           (row_number() OVER (
                               PARTITION BY grade_level_id
                               ORDER BY created_at, id) - 1)::int AS position
                    FROM grade_stream_assignments
                ) AS ranked
                WHERE gsa.id = ranked.id;
                """);

            // Subjects — the visible order was the curriculum query's
            // `ORDER BY topic.DisplayOrder, topic.Name`, so the backfill replicates
            // it per grade (over that grade's rows) and closes the gap with start_date
            // / id so every grade ends contiguous 0..n-1. Activity-group rows are left
            // NULL: they have no order surface.
            migrationBuilder.Sql("""
                UPDATE topic_assignments AS ta
                SET display_order = ranked.position
                FROM (
                    SELECT ta2.id,
                           (row_number() OVER (
                               PARTITION BY ta2.grade_level_id
                               ORDER BY s.display_order, s.name, ta2.start_date, ta2.id) - 1)::int AS position
                    FROM topic_assignments AS ta2
                    INNER JOIN subjects AS s ON s.id = ta2.topic_id
                    WHERE ta2.topic_assignment_type = 'grade'
                ) AS ranked
                WHERE ta.id = ranked.id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "display_order",
                table: "grade_stream_assignments");

            migrationBuilder.DropColumn(
                name: "display_order",
                table: "topic_assignments");
        }
    }
}
