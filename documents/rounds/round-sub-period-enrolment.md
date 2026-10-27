# Round — sub-period enrolment resolves to nothing (#427)

Mode: attended · Tier 2 · branch `fix/sub-period-enrolment-union`, cut from `main` after #429 merged

**Amended after a `P0` plan gate** (two blocking findings + three notes). The amendments are marked
**[F1]**, **[F2]** and **[N]** inline; the gate's frontier echo was `accepted: 3 of 3` for Q89/Q90/Q91.

## What is actually broken (verified, and not where the issue says)

`ListStudentSubjectsHandler` never reaches the subject query for these students:

```csharp
var enrollment = await enrollmentRepository.GetActiveEnrollmentByStudentAndPeriodAsync(
    query.StudentId, activeYear.Id, ct);                                      // ← scoped to the YEAR
if (enrollment is null) return [];                                            // ← a term-level row misses here
```

The union query and the `StudentTopicAssignment.PeriodId` match are **already correct** — assignments are
stamped with the academic year, and `SubPeriodId` carries the finer grain when present. Nothing about the
subject half changes.

**[N — the cause is bad data, not an unsupported shape.]** An earlier draft of this plan claimed
`StudentEnrollment.PeriodId` may "legitimately be a sub-period via the create path". **That is false.** Both
write paths enforce year-level:

- `EnrollStudentHandler.cs:47-51` — guards `active.ParentPeriodId is not null` and rejects (FR-H9 / AC-H8)
- `CreateStudentWithLinkedDataHandler.cs:245,252-255` — rejects `pid != active.Id`

So a sub-period enrolment is **legacy data that violates the current invariant**. The read-path fallback is
still the right thing to ship (it is defensive, and the observed STUA01 row proves such rows exist), but it
is hardening against bad data — not support for a supported shape. The alternative root-cause fix is a data
migration re-pointing those rows at their parent year; **that is a separate decision, not this round's**
(see "Deferred").

**Observed live (2026-10-27):** student `01234282-…` (STUA01), enrolment period `d6ef639c-…` (Grade 4
*Term 2*) → `GET /students/01234282-…/subjects` → `[]`, so the page renders no subject bands at all, which
reads as "this student has no subjects".

## Scope

### P1 — resolve the enrolment's period to the academic year (the fix)

When no enrolment is found for the active year, look for one whose period is a **child of** that year, and
use its `GradeLevelId`.

- One hop only. **[N — verified safe]:** `CreatePeriodHandler` and `UpdatePeriodHandler` both reject a
  sub-period parent (`parent.ParentPeriodId is not null`), so the model genuinely allows two levels and
  `ParentPeriodId == activeYear.Id` is the only relationship needed. A recursive walker would invent a third.
- **Never guess a grade.** No "most recent enrolment" fallback — a wrong grade renders confident, wrong
  bands, which is worse than an empty page.
- Extract the lookup into one private method so the year-direct and child-period paths are visibly the same
  concept, and **the year-direct path runs first** so the 99% case is unchanged.

### P2 — the handler/endpoint contract for "we cannot answer" **[F1]**

The plan previously said "return `TypedResults`" and named no mechanism. Pinned:

1. **The handler signals through a typed exception**, matching this file's own idiom — it already maps
   `PeriodNotOpenException → 409` at `StudentTopicAssignmentRoutes.cs:62`. New
   `SubjectPeriodUnresolvedException` (or the existing conflict exception if one fits), thrown by the handler.
   A discriminated return type would change the `IQueryHandler<ListStudentSubjects, StudentSubjectEnrollmentDto[]>`
   signature and every caller, for one edge case.
2. **The route becomes a typed union:** `Results<Ok<StudentSubjectEnrollmentDto[]>, ProblemHttpResult>` and
   returns `TypedResults.Problem(...)` with 409 for that exception — per AGENTS.md's rule that new/edited
   endpoints return `TypedResults`, so OpenAPI emits the schema.
3. **Three conditions, named** — the handler must call the existing
   `GetActiveEnrollmentsByStudentAsync` (`StudentEnrollmentRepository.cs:50-52`) to tell them apart:
   - **(a)** no year-direct and no child-period enrolment, **and** no active enrolment rows at all → `200 []`
     (legitimately: this student is not enrolled).
   - **(b)** no year-direct and no child-period enrolment, **but** ≥1 active enrolment neither in the year
     nor a child of it → **409** + Warning log (student id, enrolment period id, active year id).
   - **(c)** a resolved enrolment whose grade has **0** effective subjects → `200 []` (legitimately empty).

### P3 — the client and the page must carry the 409 to the rail **[F2]**

Without this the handler can be perfect and the rail still shows a silent empty state — the plan's
criterion 3 would fail with correct server code.

- `StudentsApiClient.ListStudentSubjectsAsync` (`:1964-1965`) currently `GetFromJsonAsync<…[]>` → throws
  `HttpRequestException` on 409, and its own doc (`:1957-1963`) promises "empty array, never a 404". Change
  it to read the raw response and surface a **typed** result the caller can branch on (409 = unresolved,
  everything else as today). Update that doc comment in the same edit — it will otherwise be a lie.
- `Edit.razor.LoadSubjectsAsync` (`:552-570`) currently catches → `LogWarning` + `_subjects = []` and
  **never sets `_subjectError`**. It must set
  `_subjectError = FriendlyError.Describe(ex, "This student's enrolment is in a period that could not be
  matched to the current academic year.", Logger)` for the unresolved case only.
- **The message needs a home:** the bar at `Edit.razor:235` renders only inside the has-enrolment branch, so
  the unresolved sentence would have nowhere to appear. Render it in the subject band's own place — the
  point is that this student *does* have an enrolment, so the "no enrolment" branch is the wrong shape.

### P4 — tranche 0 of the friendly-error ledger (#424, riding along per Q91)

`LandingPage.razor:89` renders `@ex.Message` in the shared wrapper's own `<ErrorContent>` — widest blast
radius in the 298-entry ledger, inherited by all 15 consumers.

- Route it through `FriendlyError.Describe(ex, "This page could not be displayed. Reload to try again.", Logger)`.
- Delete its ledger entry (`FriendlyErrorAllowList.cs:116`) and lower `RecordedEntryCount` **298 → 297** in
  the same edit. The entry's recorded line (`:80`) is already stale against the real `:89`; the entry
  disappears, so there is nothing left to correct.

### P5 — tests

- Sub-period enrolment returns the grade's subjects — the case the suite has never covered.
- Sub-period and year-direct enrolments return the **same** rows for equivalent data.
- Condition **(b)** → `409` with the typed problem asserted; condition **(a)** → `200 []`; condition **(c)**
  → `200 []`. All three, so P2 cannot over-fire.
- **The 409 → sentence path:** `Edit.razor` renders the plain sentence and `_subjects` stays empty
  (**[F2]** — the test the plan was missing).
- Tranche 0: the fallback renders the plain sentence, `NotContain` the exception text, **scoped to the
  message bar** with a positive assertion first (per `testing.md` § "Scope every negative assertion").

## Explicitly NOT in this round

| Deferred | Why |
|---|---|
| **Data migration re-pointing legacy sub-period enrolments** | A migration that rewrites enrolment periods needs its own review and a count of affected rows — see Q93. This round tolerates the rows on read. |
| Rollover screen (§7, FR-ER-18…23) | Its own two-round feature, per Q89 |
| #413 student number | Settled shape (Q92: peek-without-consume + post-save reconciliation), its own round |
| The rest of the ledger (#424 tranches 1–4) | Tranche 0 only, per Q91 |

## Acceptance criteria

1. A student with a sub-period enrolment sees their grade's subject bands; the rows are identical to those a
   year-direct enrolment produces.
2. Only one level of `ParentPeriodId` is consulted; no "most recent enrolment" fallback exists anywhere in
   the path.
3. Condition (b) returns **409** with a typed problem, **and** the rail renders the plain sentence via
   `FriendlyError.Describe` — not an empty state, not exception text.
4. Conditions (a) and (c) still return `200 []` and render the legitimate empty state.
5. The unresolved case logs at Warning with student id, enrolment period id and active year id.
6. The route is a `Results<Ok<…>, ProblemHttpResult>` union returning `TypedResults`, so OpenAPI carries both.
7. `StudentsApiClient.ListStudentSubjectsAsync` surfaces the 409 instead of throwing, and its doc comment
   says so.
8. `LandingPage`'s fallback renders the plain sentence; no `@ex.Message` remains in the shared wrapper.
9. `RecordedEntryCount` is **297**, its entry deleted, guard green.
10. `dotnet build` 0 errors; affected suites green; `scripts/round/gates.sh --serial` exit 0 with every
    skipped leg named (`--serial` is mandatory here: the parallel build fail-fast crashed twice today, and
    this diff touches `SchoolCollab.Admin.Shared`, i.e. shared chrome → full gate, no skips).

## Not run

_(filled at acceptance)_
