using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolCollab.Students.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodSequenceAndExceptionOrdinal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ordinal",
                table: "subject_enrollment_exceptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sequence",
                table: "periods",
                type: "integer",
                nullable: true);

            // ── Backfill 1 — periods.sequence (subject-period-exception-model.md v5 §0
            // decision 15, Q8a) ─────────────────────────────────────────────────────
            // Existing sub-periods need a position, but nothing recorded one: the column
            // is new, so every row starts NULL. Two rules, in this order:
            //
            //   1. the trailing integer of the NAME, when there is one ("Term 2" → 2).
            //      Anchored at the END of the string, so "2027 Term" does NOT parse as
            //      2027 — a leading year is the most likely integer in a real period
            //      name and parsing it would hand out absurd ordinals.
            //   2. otherwise the 1-based position by start_date among siblings sharing
            //      (tenant_id, parent_period_id, division) — i.e. the run order.
            //
            // Both the cast and its guard are load-bearing: a name with no trailing
            // integer yields NULL from substring(), and the CASE both rejects the empty
            // match and refuses to cast a digit run longer than 9 characters (which
            // would raise "value out of range" and abort the migration rather than
            // backfill it). CASE branches are only evaluated when taken, so the cast
            // never runs for a row it would fail on.
            //
            // Scoped to SUB-PERIODS ONLY — `parent_period_id IS NOT NULL AND division <> 0`.
            //
            // Both conditions are load-bearing, and `parent_period_id IS NOT NULL` is the
            // one this originally got WRONG (caught in static review, 2026-09-28). A
            // top-level periodised year — `parent_period_id IS NULL` with a Terms/Semesters
            // division — is a documented, first-class shape in this model: `Period.Division`'s
            // own comment reads "Terms/Semesters on a top-level year (ParentPeriodId == null)
            // means the year may contain only that sub-period kind", and `ValidateHierarchy`
            // rejects only the opposite case (sub-period + None). Gating on `division <> 0`
            // alone therefore backfilled a position onto rows that `ValidateSequence` forbids
            // ("Only a sub-period can carry a sequence") — a state no application path can
            // recreate, an edit-path hazard, and, because such rows are INSIDE the partial
            // index, a way for two periodised years with names ending in the same integer to
            // abort this migration at CreateIndex. `division <> 0` is kept as well because a
            // sub-period implies a non-None division by ValidateHierarchy, but raw data need
            // not honour that.
            //
            // Top-level years of every kind keep sequence = NULL, and that NULL is what makes
            // the partial unique index below correct: Postgres treats NULLs as DISTINCT, so
            // without the filter every top-level year would collide with every other.
            migrationBuilder.Sql(
                """
                WITH siblings AS (
                    SELECT id,
                           tenant_id,
                           parent_period_id,
                           division,
                           start_date,
                           substring(name from '([0-9]+)[[:space:]]*$') AS trailing_digits,
                           count(*) OVER (
                               PARTITION BY tenant_id, parent_period_id, division
                           ) AS run_size
                    FROM periods
                    WHERE parent_period_id IS NOT NULL
                      AND division <> 0
                ),
                positioned AS (
                    SELECT id,
                           -- ONE ordering, not two. An earlier version COALESCE'd a
                           -- name-parsed position with an INDEPENDENT `row_number()` fallback,
                           -- which was wrong: the two sources are computed separately over the
                           -- same run and can claim the same slot. A run holding "Term 1" and
                           -- an unnamed "Winter" gave BOTH position 1, and the partial index
                           -- created below then ABORTED the migration. (Found by running the
                           -- backfill against real Postgres, not by reading it — every suite
                           -- migrates a fresh container, so these backfills have always run
                           -- over zero rows.)
                           --
                           -- A single `row_number()` ordered "plausible parsed position first,
                           -- then date" is unique BY CONSTRUCTION, and still honours names:
                           --   * "Term 1" sorts to the front and takes 1 even if dated last;
                           --   * NULLS LAST puts unparseable rows behind the named ones, and
                           --     they take the remaining positions in date order;
                           --   * a parse is only BELIEVABLE if it could exist in a run this
                           --     long, so "Semester 2 2027" → 2027 and "Term 0" → 0 both sort
                           --     as unparsed rather than writing a nonsense position;
                           --   * equal parses tie-break on start_date then id, which also
                           --     dissolves the "Term 1" / "Term 01" collision the earlier
                           --     design listed as an accepted residual.
                           (row_number() OVER (
                                PARTITION BY siblings.tenant_id, siblings.parent_period_id, siblings.division
                                ORDER BY
                                    CASE
                                        WHEN length(siblings.trailing_digits) BETWEEN 1 AND 9
                                             AND siblings.trailing_digits::int BETWEEN 1 AND siblings.run_size
                                            THEN siblings.trailing_digits::int
                                    END NULLS LAST,
                                    siblings.start_date,
                                    siblings.id
                            ))::int AS sequence
                    FROM siblings
                )
                UPDATE periods
                SET sequence = positioned.sequence
                FROM positioned
                WHERE periods.id = positioned.id;
                """);

            // ── Backfill 2 — subject_enrollment_exceptions.ordinal (Q6b) ─────────────
            // Deliberately CONSERVATIVE: an exception receives an ordinal only when its
            // span is EXACTLY equal to a positioned sub-period's span in the same tenant
            // and division. Anything else stays NULL — a partial or shifted span, a
            // tenant that never periodised its years, a free window (division None).
            // Guessing "this is probably term 2" is not something a data migration may
            // do, because the ordinal is what the row will READ BACK as (decision 15).
            //
            // A positioned SUB-PERIOD only — same guard as backfill 1, for the same reason.
            // `parent_period_id IS NOT NULL` is required here too: this CTE reads `sequence`,
            // which backfill 1 now leaves NULL on every top-level year, so the guard is
            // belt-and-braces rather than load-bearing. The `division <> 0` below IS load-
            // bearing: because the exception's division must equal the matched period's, no
            // exception on a free window (None) can be given an ordinal here.
            //
            // The DISTINCT ON is determinism, not decoration: two academic years of one
            // tenant could in principle carry identically-dated terms of the same
            // division, and an UPDATE … FROM with more than one match would then set an
            // arbitrary one of them. Lowest sequence wins, and the statement is the same
            // on every re-run.
            //
            // This runs AFTER backfill 1 — it reads the sequence values that one wrote.
            migrationBuilder.Sql(
                """
                WITH positioned_sub_periods AS (
                    SELECT DISTINCT ON (tenant_id, division, start_date, end_date)
                           tenant_id,
                           division,
                           start_date,
                           end_date,
                           sequence
                    FROM periods
                    WHERE parent_period_id IS NOT NULL
                      AND division <> 0
                      AND sequence IS NOT NULL
                    ORDER BY tenant_id, division, start_date, end_date, sequence
                )
                UPDATE subject_enrollment_exceptions
                SET ordinal = positioned_sub_periods.sequence
                FROM positioned_sub_periods
                WHERE subject_enrollment_exceptions.tenant_id = positioned_sub_periods.tenant_id
                  AND subject_enrollment_exceptions.division = positioned_sub_periods.division
                  AND subject_enrollment_exceptions.start_date = positioned_sub_periods.start_date
                  AND subject_enrollment_exceptions.end_date = positioned_sub_periods.end_date;
                """);

            // Created LAST, after the backfill, so the one-position-per-run rule is
            // asserted against the data that rule just produced: a collision fails the
            // migration loudly here instead of leaving two periods quietly claiming the
            // same position.
            //
            // The name is 42 bytes — inside Postgres's 63-byte identifier limit, so it is
            // stored whole and the model snapshot cannot desynchronise over a silent
            // truncation.
            migrationBuilder.CreateIndex(
                name: "ix_periods_tenant_parent_division_sequence",
                table: "periods",
                columns: new[] { "tenant_id", "parent_period_id", "division", "sequence" },
                unique: true,
                filter: "sequence IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The exact inverse of Up(), i.e. the reverse of its
            // AddColumn(ordinal) → AddColumn(sequence) → [backfills] → CreateIndex order:
            // index first, THEN the columns in the reverse of the order they were added.
            // The two backfills need no reversal of their own — dropping the columns
            // discards everything they wrote, and nothing else in the database was read
            // or written. The index MUST go first: it is the only operation here with a
            // dependency on the columns.
            migrationBuilder.DropIndex(
                name: "ix_periods_tenant_parent_division_sequence",
                table: "periods");

            migrationBuilder.DropColumn(
                name: "sequence",
                table: "periods");

            migrationBuilder.DropColumn(
                name: "ordinal",
                table: "subject_enrollment_exceptions");
        }
    }
}
