# Parked decisions — revisit backlog

One file for decisions that are **ambiguous or parked** rather than settled. When a question ends
in "save this for later revisit" instead of an answer, it lands here — never left in a round doc
(`documents/rounds/` is bulk-trashed when rounds close) and never buried in an issue comment.

**Entry shape** (follow it for every future entry):

- **What** is undecided.
- **Why** it is parked.
- **What's known** — the verified facts, so nobody re-derives them (including wrong turns already
  ruled out).
- **What unblocks** it — the concrete inputs or confirmations needed before acting.

Revisit deliberately. When an entry is settled, move the decision to its proper home (ADR, spec,
rule, or an issue that will actually be worked) and **delete the entry here**.

---

## Q93 — migrate the legacy sub-period enrolment rows now, or later?

**Parked:** 2026-10-27 (owner: "save this for future revisit").

**What.** `StudentEnrollment.PeriodId` must reference an `AcademicYear` — FR-H9
(`documents/specs/period-hierarchy-terms-semesters.md:161`), restated in
`documents/specs/backend-implementation-backlog.md` § 1.8 "Grade enrollment stays year-level".
A legacy row exists whose `PeriodId` is a Term/Semester (observed: STUA01, Grade 4 · Term 2).
It makes `ListStudentSubjectsHandler` return `[]` (no subject bands) and `AddMembershipHandler`
throw `GradeNotEligibleException`. Decide: re-point those rows to their parent year now, or ship
only read-path tolerance and migrate later.

**Why parked.** The owner flagged it for later revisit rather than settling it in the round.

**What's known (do not re-derive):**

- Both write paths already enforce FR-H9 (`EnrollStudentHandler.cs:47-51` FR-H9/AC-H8;
  `CreateStudentWithLinkedDataHandler.ValidateEnrollmentTargetAsync` rejects `pid != active.Id`).
  So the legacy row predates the guard or is manual data — **no current path can produce it**, and
  no seeder produces enrolments. `GetActivePeriodAsync()` delegates to
  `GetActiveAcademicYearAsync()` (top-level year only).
- `StudentEnrollment` has **no `SubPeriodId` by design** — the term was never supposed to live on an
  enrolment. `SubPeriodId` belongs on `StudentTopicAssignment` (term-delivered subjects). Therefore
  re-pointing `period_id → parent_period_id` **loses nothing**; there is no term data to preserve.
- The migration is one statement (no schema change):
  ```sql
  UPDATE student_enrollments
  SET period_id = p.parent_period_id
  FROM periods p
  WHERE p.id = student_enrollments.period_id AND p.parent_period_id IS NOT NULL;
  ```
- Blast radius is **two** handlers, not one: `ListStudentSubjectsHandler.cs:44` (silent `[]`) and
  `AddMembershipHandler.cs:100` `EnsureGradeEligible` (throws `GradeNotEligibleException`).
- An earlier draft wrongly claimed the migration needed a new `SubPeriodId` column on the enrolment;
  that was retracted — it conflated the enrolment (year-level, FR-H9) with the assignment
  (sub-period-carrying).

**What unblocks it:**

- A count of affected rows per tenant, so the migration diff is reviewable.
- Confirmation that re-pointing **ended** enrolments is safe (expected: yes — they are history and
  their `PeriodId` was wrong the same way).
- A decision on whether the read-path tolerance stays as a **defensive guard that logs** (rather than
  a silent fallback) even after the data is fixed.

**Related:** issue #427 (the symptom); `documents/rounds/round-sub-period-enrolment.md` (the plan,
paused at the Q93 gate).
