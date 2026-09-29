# Run B (second attempt) — slices 4b/5/6/7 verification + P2-3 + the backfill regression test

**Agent:** `worker` (implementation run). **Branch** `feat/subject-period-blocks`, base `49d3ba5b`.
**Nothing committed, pushed, staged, stashed or reverted.** Working tree only.

This run's job was **not** to start over: the first Run B (`ollama-cloud/deepseek-v4.1-pro`)
died at its 30-minute deadline with slices 4b–6 on disk and its round-doc sections written but
**no report and no test evidence of its own**. So the first half of this run was *verification*;
the second half was the work the brief says was still missing — **P2-3** and **the backfill
regression test** — plus the mutation checks and the honest acceptance record.

## Status at a glance

| Brief item | State | Evidence |
|---|---|---|
| Slice 5 — the page | **verified as already correct/complete** (2 fixes made) | read line by line; `EnrollmentExceptionsPageTests` 33/33 green; mutations M1–M3 below |
| Slice 6 — tests | **verified green** (33 page tests, 5 new unit suites, 1 integration suite) | `Students.Tests.Unit` 522/0, `Students.Tests.Integration` 77/0 |
| 3 — P2-3 (illegal `Period.Sequence` was a 400) | **DONE** | new boundary exception + guard call in both handlers + 422 on both routes; 4 new unit + 3 new endpoint tests |
| 4 — backfill regression test | **DONE — the top open item is closed** | `PeriodSequenceBackfillMigrationTests` (3 tests, real Postgres 16, SQL extracted from the shipped migration) |
| 5 — docs (spec v5 + acceptance) | spec v5 present and verified by reading; acceptance in the round doc **re-derived from my own runs** (see `## Acceptance` correction note) | this file + round doc |
| Migration guards | **PASS, each selecting exactly 1 test** | below |
| Playwright | **NOT RUN** — out of scope, no browser pass exists | stated, not implied |

## Files I changed in this run (one line each)

| File | Why |
|---|---|
| `src/Students/SchoolCollab.Students.Core/Domain/Exceptions/PeriodSequenceInvalidException.cs` | **NEW** — the boundary rejection type for an illegal position; the routes map it to 422. |
| `src/Students/SchoolCollab.Students.Core/CQRS/Periods/PeriodSequenceGuard.cs` | added `EnsureDeclarable(sequence, parentPeriodId)` — the boundary half of the two position invariants (mirrors `Period.ValidateSequence`, but as a 422 type). |
| `.../Commands/CreatePeriod/CreatePeriodHandler.cs` | calls `EnsureDeclarable` before any repository lookup, so an illegal position never costs a query. |
| `.../Commands/UpdatePeriod/UpdatePeriodHandler.cs` | same call on the edit path. |
| `src/Students/SchoolCollab.Students.Api/Endpoints/PeriodRoutes.cs` | `catch (PeriodSequenceInvalidException) → 422` on `POST /periods` and `PUT /periods/{id}`, next to `PeriodSequenceTakenException` (P2-3). |
| `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/EnrollmentExceptions.razor` | `HighestDeclaredPosition` now `DefaultIfEmpty(0).Max()` — see the finding below; this is the only page edit I made. |
| `tests/SchoolCollab.Students.Tests.Unit/PeriodSequenceBoundaryTests.cs` | **NEW** — 4 tests: position < 1 on create/update, position on a top-level year, and the control (an unpositioned sub-period still creates). |
| `tests/SchoolCollab.Students.Tests.Integration/PeriodSequenceAndExceptionOrdinalEndpointTests.cs` | +3 endpoint tests: the three illegal-position shapes are **422, not 400**, and write nothing. |
| `tests/SchoolCollab.Students.Tests.Integration/PeriodSequenceBackfillMigrationTests.cs` | **NEW** — the backfill regression test (3 tests). |

## Finding NOT in the brief — a latent render crash I fixed (slice 5)

`HighestDeclaredPosition` was `_periods.Where(p => IsDivision(p, Division)).Max(p => p.Sequence ?? 0)`.
`Enumerable.Max` over an **empty** sequence **throws**; it does not return 0. The sequence is
legitimately empty for a **group** owner: `AvailableDivisions` offers the one division FR-56
permits (Termly → `Terms`) whether or not the tenant has any Term period, and `_periods` is also
emptied wholesale when the best-effort period load fails. So a Termly group in a tenant with no
Term periods threw `InvalidOperationException` during render — reachable, not theoretical, and not
covered by any test (the fixtures all have Term periods). Fixed with `DefaultIfEmpty(0)`.

## Verification

### Build

| Command | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **Build succeeded. 0 Errors.** (84 warnings on a full rebuild, all pre-existing: NU1902/NU1903 advisories, Aspire ASPIRE010, MSTEST0042 duplicates) |

### Migration guards (re-run by real name, each filter selecting exactly 1 test)

| Filter | Project | Result |
|---|---|---|
| `MigrationGuardTests.NoUncommittedModelChanges` | `SchoolCollab.Students.Tests.Unit` | **total: 1, failed: 0** |
| `MigrationGuardTests.NoDomainDbContext_HasPendingModelChanges` | `SchoolCollab.ArchitectureTests.Unit` | **total: 1, failed: 0** |

### Per-project runs (`dotnet test <csproj> --no-build`; `--nologo` deliberately NOT used)

Measured by me, on a **clean rebuild after every mutation was reverted**:

| Project | total | failed | vs the run-A baseline for that project |
|---|---|---|---|
| `SchoolCollab.Admin.Tests.Unit` | **595** | **0** | = (slice-6 page suite, unchanged by my edits) |
| `SchoolCollab.Students.Tests.Unit` | **526** | **0** | **+4** (my `PeriodSequenceBoundaryTests`) |
| `SchoolCollab.Students.Tests.Integration` | **83** | **0** | **+6** (my 3 endpoint + 3 backfill tests) |

Verified earlier in this run, before the P2-3/backfill additions landed, on the inherited tree:
Admin 595/0, Students.Unit **522**/0, Students.Integration **77**/0, and the two guards below.
The **untouched** projects (Assignments, Auth, Core, Families, Settings, Architecture, the two
`*.Api.Tests.Unit`) were **not re-run by me**; the predecessor's numbers for those are quoted in
the round doc and are *not* my measurement.

> **A false red I produced, and the lesson.** After reverting mutation M3 the *source* was
> byte-identical but `bin` still held the mutated page component, so a `--no-build` run reported
> 2 failures in `Admin.Tests.Unit` for a tree that was correct. A mutation is only reverted when
> the project has been **rebuilt**; otherwise the next measurement is of the mutant. Re-running
> after `dotnet build` gave 595/0.

## Mutation checks — the risky logic, each on a verified-green baseline

Baseline before every one: `EnrollmentExceptionsPageTests` **33/33 green** (595/0 for the whole
project). M1–M3 mutate the page; M9 mutates the *shipped migration's SQL text*, which is what makes
the new backfill test's verdict meaningful.

| # | Mutation | Result |
|---|---|---|
| M1 | the position ⇒ dates fill is severed (`OnPositionChangedAsync` clears the range instead of reading the period) | **CAUGHT — 11 of 33 page tests fail** (`total: 33, failed: 11`) |
| M2 | the division ⇄ position coupling dropped (the ladder is offered on `Any date` too) | **CAUGHT — 5 of 33 fail** |
| M3 | `CanAdd` drops the span-bound requirement | **CAUGHT — 2 of 33 fail** (`OpenEnds_ReadAsAnyStartAndAnyDate_AndGateTheWrite`, `PositionWithNoMatchingPeriod_LeavesTheRangeEmpty_AndTakesTheTypedDates`) |
| M9 | the migration's backfill guard reverted to P1's `WHERE division <> 0` (no `parent_period_id IS NOT NULL`) | **CAUGHT — `Backfill_PositionsSubPeriodRuns_…` fails on the DATA assertion: "a PERIODISED top-level year … must stay NULL too … but found 2"** (the P1 defect reproduced by the test, not by a text check) |

Every mutation was reverted and **verified byte-identical** by `sha1sum -c` against a copy taken
before the mutation (`/tmp/page.bak`, `/tmp/mig.bak`), and a repository-wide grep confirms **zero**
`MUTATION M` residue. The M9 mutation was re-run once: my first attempt injected the comment inside
the SQL body with `//` (a Postgres syntax error), which made the test fail for the wrong reason —
the verdict above is the second attempt, where the SQL still parses and the failure is the value
assertion.

## The backfill regression test (the gap that let P1 hide), and how it extracts the SQL

`tests/SchoolCollab.Students.Tests.Integration/PeriodSequenceBackfillMigrationTests.cs` — **3
tests**, added by this run:

1. `Backfill_PositionsSubPeriodRuns_ByParsedNameThenDate_AndLeavesEveryTopLevelYearNull`
2. `Backfill_GivesAnOrdinalOnlyToAnExceptionWhoseSpanExactlyMatchesAPositionedSubPeriod`
3. `ShippedMigration_DeclaresThePartialIndexTheBackfillsRelyOn_AndExactlyTwoSqlBlocks`

**How the SQL is obtained (the part that stops the test drifting from what ships).** The test walks
up from `AppContext.BaseDirectory` to the directory holding **`SchoolCollab.slnx`** (the repo root —
no hard-coded drive path), takes
`src/Students/SchoolCollab.Students.Core/Migrations/*_AddPeriodSequenceAndExceptionOrdinal.cs`, and
regex-matches `migrationBuilder\.Sql\(\s*"""(?<sql>.*?)"""`. It asserts there are **exactly two**
blocks, that both are non-empty, and that block 1 contains `SET sequence` while block 2 contains
`SET ordinal` — so an extraction that silently matched nothing fails instead of passing vacuously.
The two blocks are then executed **in the migration's own order** (positions first, because the
ordinal backfill reads the `sequence` values it writes).

**The database is real, and the schema is the shipped one.** The class reuses `ApiFactory`
(Testcontainers **postgres:16-alpine**), whose `MigrateAsync()` runs the whole migration chain — so
the columns, types and constraints the backfill writes through are the deployment's. The rows are
seeded **after** migrating, in the state a real upgrade starts from: every `sequence`/`ordinal` NULL,
because the migration has just added the columns. The backfill is then executed by the test rather
than by EF, which is the only way to get rows in front of a backfill that a fresh-container suite
can never have.

**Seeded shapes (brief item 4, all present):**

| Seed | Assertion |
|---|---|
| Plain top-level year (`division` None, `parent_period_id` NULL) | stays **NULL** |
| **Top-level PERIODISED year** (`AY2027`, division Terms, parent NULL) — the P1 shape | stays **NULL** |
| A run of three sub-periods: `"Term 1"`, `"Winter"` (no trailing digits), `"Semester 2 2027"` (**name ends in a year**, P2-4) | **1, 2, 3** — the year name must NOT become position 2027, so it falls through to date order |
| A second run: `"Term 1"` and `"Term 01"` (equal parse) | **1, 2** — distinct; the single `row_number()` is unique by construction, the old COALESCE version gave both 1 and aborted `CreateIndex` |
| Exception whose span **exactly** equals a positioned sub-period's span (same division) | ordinal **1** |
| Exception whose span is a **subset** of the same sub-period | stays **NULL** |
| Exception on a free window (division None) | stays **NULL** |
| Exception whose span matches a sub-period of the **other** division | stays **NULL** |
| Open-ended exception (no end date) | stays **NULL** |

The test additionally asserts **one position per (year, division)** across the backfilled data.

**Stricter than the migration itself, deliberately.** Because the migration has already run, the
partial unique index exists *while the backfill executes*, so a collision is rejected by the
backfill's own `UPDATE`; the migration instead creates the index last, which is exactly why P1's
wrong guard got as far as `CreateIndex`. Test 3 also reads `pg_indexes.indexdef` out of the running
database and asserts the index is `UNIQUE` with the `sequence IS NOT NULL` predicate — the
nullability rules above are only correct because that index is *partial* (Postgres treats NULLs as
DISTINCT, so without the filter every top-level year would collide).

**Would it have caught P1? Yes — measured, not argued.** Mutation **M9** rewrote the shipped
migration's guard back to `WHERE division <> 0` (and nothing else) and the data assertion failed
with `but found 2`; the file was then restored and hash-verified. That is the coverage the round
did not have before.

## Deviations from the brief, and why

1. **I edited the page's `HighestDeclaredPosition`** — not in the brief's slice-5 list, but it is a
   reachable crash inside the slice-5 method set (above). Reported rather than silently patched.
2. **The round doc's own acceptance section was written by the dead run, claiming COMPLETE and green.**
   I did **not** delete or rewrite its prose (it is the record of what that run believed); I added a
   verification addendum with my own measurements and corrected the counts, because several of its
   load-bearing claims had no evidence behind them when I started.
3. **I did not re-run the untested-here projects** (Assignments, Auth, Core, Families, Settings,
   Architecture, the two `*.Api.Tests.Unit`). The brief's baseline (2896 / 3 environmental
   failures) is quoted, not re-measured by me.
4. **Playwright / browser pass: not run.** No browser suite is in scope and no browser is
   configured; every layout claim in the round doc is a **static** claim about markup and bound state.
5. The predecessor's other four mutation results (M4, M5, M7, M8) are **its** claims; I re-derived
   only the three the brief named (M1–M3) plus M9. I have no evidence for or against M4/M5/M7/M8.

## What I could not verify

- The per-project numbers for the projects this round did not touch are the predecessor's.
- `RowLabelPosition.Below` CSS has never been seen at a real width (no browser pass).
- `ChosenPart_*` test *quality* (as opposed to their greenness and their mutation sensitivity): I
  read the page and the tests, but a full line-by-line review of all 33 page tests was not in the
  time budget.

