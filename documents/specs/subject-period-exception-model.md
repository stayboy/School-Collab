# Spec: Subject Enrollment Exceptions (decouple subject availability from the period lifecycle)

> **Status:** **v3 — redesigned 2026-09-26.** v1 (period-only "blocks", grade-only UI) was
> implemented through phase 1 and reviewed CLOSED. v2 widened it to date windows and
> activity groups; **v3 removes the `PeriodId` dependency entirely** — an exception is
> expressed as a **period part** (term / semester) plus a **date span**, so it never
> references a period instance. Superseded text is kept inline with strikethroughs so
> the reasoning trail survives.
> **Owner:** Students context (`SchoolCollab.Students.Core` / `.Api` / `.Application`)
> **Cross-context impact:** `SchoolCollab.Assignments.Core` — FR-58 and `ITopicAssignmentLookup`
> **Supersedes:** the `GradeTopicAssignment.PeriodId` whitelist semantics from
> `activity-group-enrollment.md` **Rev. 6 FR-55…57** (FR-58 is *refined*, see §4.3)
> **Supersedes:** §4 of `topic-edit-dialog-redesign.md`
> **Supersedes:** `documents/solution/subject-topic-delivery-periods.md` §3
> **Relates to:** `period-hierarchy-terms-semesters.md`, `active-period-per-tenancy.md`,
> `activity-group-enrollment.md` (`EnrollmentSpan`, `DateRange`/`OpenEnded` windows)
> **Sibling spec (independent — do not conflate):**
> `subject-enrollment-requirement-flag.md` decides *who* may receive work; this spec
> decides *when* a subject is offered. Orthogonal: this spec removes `PeriodId` from the
> bridge, the sibling adds `RequiresEnrollment` to it.

---

## 0. Decisions locked

**From v1 (2026-09-26):**

1. **Exceptions are for genuine exceptions only** — staffing, curriculum clash, a
   teacher on leave. No scheduling whitelist, no hybrid.
2. "Subject offered only in Term 1" is not the use case; the use case is
   *"not offered in Term 3"*. A missing exception fails **silently** (the subject
   appears where it should not), which is accepted.

**From v2 (2026-09-26):**

3. **Exceptions cover both owners** — grade-owned subjects **and** activity-group
   subjects. `DateRange`/`OpenEnded` groups are no longer unexceptable.
4. **Management is one dedicated page.** The v1 `SubjectBlocksDialog` is retired.
5. **Entry points are thin** — a **count badge** next to the subject plus a kebab
   that **navigates**. No inline list, no inline editor on any card.
6. **Vocabulary:** the user-facing noun is **"enrollment exceptions"**.

**Locked in v3 (2026-09-26) — the pivotal change:**

7. **No `PeriodId`. Anywhere.** An exception is expressed as a **period part**
   (`AcademicYearDivision`: `Terms` | `Semesters`, or `None` for a plain window)
   plus a **date span**. It never references a period *instance*.

   *Why:* a period instance id is year-specific, so an exception pinned to
   `2026 Term 3` silently fails to cover `2027 Term 3` — and `Period` carries **no
   ordinal field** (`Id, Name, StartDate, EndDate, Status, Division,
   ParentPeriodId, …`), so "Term 3" is only identifiable by its dates or its id.
   Storing the part + span removes the dependency and the limitation together.

8. **`StartDate` is nullable.** A span may be open at the start, open at the end,
   or both — but **not neither** (an unbounded exception is meaningless; "never
   offered" is a different concept). `EndDate >= StartDate` when both are set.

9. **The whole predicate becomes date-based.** With no period reference anywhere,
   v1/v2's "active period id set" — and its RISK-2 two-id ruling (§8 Q6) — is
   **dissolved**, not carried forward. Availability is a single date test.

10. **Rename.** `SubjectEnrollmentBlock` → `SubjectEnrollmentException`,
    `subject_enrollment_blocks` → `subject_enrollment_exceptions`,
    `/students/subject-blocks` → `/students/enrollment-exceptions`. Free now (v1's
    migration is uncommitted); a table-rename migration after release.

---

## 1. The problem being solved

> **⚠️ CORRECTED 2026-09-26 (v1, after plan review).** The original v1 text claimed the
> pinned subject *"silently disappears from the grade's curriculum"* on rollover. **That
> is false and was never verified** — it conflated student→grade *enrollment* with
> *subject availability*. Every shipped availability read path is period-blind; **no
> availability handler reads `PeriodStatus`**. A subject pinned to an archived term stays
> **visible**. v1 was justified by the two defects below instead.

The bridge `GradeTopicAssignment.PeriodId` is a **whitelist**: non-null means
*"delivered only in that period"*.

**Defect 1 — half-wired, and the wired half is wrong.** `PeriodId` is read by exactly
one surface (`ListTopicsByGrade` under an explicit `?periodId=`), matched **exactly**;
every other reader ignores it. It does nothing in most of the system, and where it
applies it excludes the year-spanning (`null`) row the design treats as primary.

**Defect 2 — the whitelist cannot express what it was invented for.**
`ix_topic_assignments_tenant_grade_topic_unique` on `(tenant_id, grade_level_id,
topic_id)` — **UNIQUE**, filtered `topic_assignment_type = 'grade'` — permits at most
**one** bridge row per (tenant, grade, topic). Rev. 6 FR-55's "N rows for N periods"
was never representable; `TopicAssignment.UpdatePeriod(Guid?)` is the sole mutation path.

**Defect 3 (v2/v3) — a period *instance* is the wrong key.** Availability is about a
*span of time*, and the calendar is authoritative: a term is defined by `StartDate`/
`EndDate`. Keying an exception to a period instance makes it year-specific (so it must
be re-added every year) and couples it to period lifecycle status that availability
does not consult. **The part + span model removes that coupling.**

---

## 2. The model — default-on, explicit exceptions

| | Whitelist (today) | v3 exceptions |
|---|---|---|
| Subject on Grade 5 | one bridge row, `PeriodId = <a period>` or `null` | one bridge row, **no period meaning** |
| "Not offered in Term 3" | pin to Term 3, re-pin every year | one row: `Division = Terms` + Term 3's dates |
| "Closed 1–14 March" | **not expressible** | one row: `Division = None` + that span |
| "Not offered from Term 3 on" | **not expressible** | one row: open **end** |
| "Not offered up to Term 3" | **not expressible** | one row: open **start** |
| Activity group with a `DateRange` window | **cannot be excepted at all** | a window inside the group's window |
| Year rollover | stale row → subject vanishes | nothing to do; dates still match |
| Availability | two-way date/period resolution | `bridge EXISTS AND NOT exceptionContains(date)` |

### 2.1 The entity

```csharp
/// One exception: a topic is NOT offered for a span of time, expressed as a period
/// part (Term/Semester) or a plain window. The absence of an exception is the
/// normal, expected state.
/// NOTE: there is deliberately NO PeriodId — see §0 decision 7.
public sealed class SubjectEnrollmentException : BaseTenantEntityWithAudit, IHasRowVersion
{
    public Guid Id { get; private set; }

    /// <summary>Grade-owned. Mutually exclusive with <see cref="ActivityGroupId"/>.</summary>
    public Guid? GradeLevelId { get; private set; }

    /// <summary>Group-owned. Mutually exclusive with <see cref="GradeLevelId"/>.</summary>
    public Guid? ActivityGroupId { get; private set; }

    public Guid TopicId { get; private set; }

    /// <summary>
    /// The period PART this exception is expressed in: Terms or Semesters. None means
    /// the academic year itself, or a free window not expressed as a term/semester.
    /// This is a kind, never a period instance.
    /// </summary>
    public AcademicYearDivision Division { get; private set; }

    /// <summary>First day excepted (inclusive). Null = open start.</summary>
    public DateOnly? StartDate { get; private set; }

    /// <summary>Last day excepted (inclusive). Null = open end.</summary>
    public DateOnly? EndDate { get; private set; }

    /// <summary>Optional free text, e.g. "teacher on leave". Never load-bearing.</summary>
    public string? Reason { get; private set; }
}
```

### 2.2 Invariants

- **Exactly one owner:** `GradeLevelId` XOR `ActivityGroupId`.
- **At least one bound:** `StartDate` or `EndDate` must be set. Both null is rejected —
  an unbounded exception would mean "never offered", which is not this concept.
- **Ordering:** when both are set, `EndDate >= StartDate`.
- **No period reference.** No FK, no `PeriodId`, no period-status rule. *(v1's
  "`ON DELETE RESTRICT` keeps a period exception from being orphaned" and its
  "retired periods cannot be excepted" rule **both dissolve** — there is no period to
  orphan or retire. The v1 archived-marker trap rule goes with them: an exception on a
  period that later archives is simply unaffected, because it never named one.)*
- **Uniqueness** — duplicates are a **409**, not a silent second row. Because both
  bounds are nullable and Postgres treats NULLs as *distinct*, the unique index is an
  **expression index** created in raw SQL by the migration
  (§7), keyed on
  `(tenant_id, owner_id, topic_id, division, COALESCE(start_date,'-infinity'), COALESCE(end_date,'infinity'))`
  filtered `owner_id IS NOT NULL AND is_deleted = false`, once per owner column. A
  plain `HasIndex` on nullable columns would let the same open-ended exception be
  inserted twice.
- **No status/lifecycle column.** Immutable history; removal soft-deletes (audit base +
  `IHasRowVersion` for `xmin`), and the soft-delete filter in the index is
  **required** — otherwise remove-then-re-add collides.

### 2.3 Availability semantics (the single source of truth)

```
SubjectAvailable(owner, topicId, effectiveDate) =
      EXISTS     bridge(owner, topicId) effective on effectiveDate
  AND NOT EXISTS exception(owner, topicId) where
          (start_date IS NULL OR start_date <= effectiveDate)
      AND (end_date   IS NULL OR end_date   >= effectiveDate)
```

**One test, one date.** No active-period lookup, no id set, no range-vs-id duality:
`Division` is descriptive (it tells the UI and FR-56 what *kind* of span this is), and
does **not** participate in matching. That is what makes §0 decision 9 possible — and
it retires v1/v2's RISK-2 "active period id set" entirely.

All of it lives in **one** shared helper so the predicate and the cache key cannot
drift. The exception lookup is tenant-scoped inside the cache factory (the ambient
tenant is not available there), and exception writes invalidate the `students` tag —
both already true in v1.

### 2.4 What this deliberately gives up

- Availability is a **date** test, so a period that is `Draft` or `Archived` has no
  special meaning for exceptions. Recording *"not offered in the upcoming term"* is
  therefore done by naming its dates, and nothing warns you if those dates later move.
  *(v1 got that warning for free by pointing at the period instance. Accepted: the
  instance pin was the limitation that motivated v3.)*
- `Division` is descriptive only; a `Terms` exception whose span crosses a semester
  boundary is accepted. Guarding that would need a hierarchy walk on every write for a
  case with no failure mode (the date test is what decides).
- A gapped selection like *"Terms 1 and 3, not 2"* is **two rows** (one span each), not
  one. The UI presents it as a single selection; storage stays honest.

---

## 3. What this fixes

1. **Year rollover is a no-op** — no period instance is attached to anything.
2. **Exceptions survive the calendar** — a span expressed in dates keeps matching.
3. **Date windows are expressible** — including open-start and open-end spans.
4. **Activity groups finally reachable** — including the `DateRange`/`OpenEnded` spans
   v1 FR-56 declared unexceptable.
5. **One predicate** — the date test replaces a period-id set, a range-versus-id
   duality, and the retired-period rules that v1 needed.

---

## 4. Impact on existing FRs

### 4.1 FR-55 — superseded

> ~~`GradeTopicAssignment.PeriodId` is nullable; null = year-spanning, non-null =
> delivered in that period.~~

`PeriodId` carries no meaning on the bridge. **Keep and ignore** (§8 Q2).

### 4.2 FR-56 / FR-57 — superseded by the exception's own shape validation

**FR-56 (group-owned) — REWRITTEN.** A group's exception `Division` must match the
group's `EnrollmentSpan`:

| `EnrollmentSpan` | Required `Division` | Extra rule |
|---|---|---|
| `WholeAcademicYear` | `None` | — |
| `Termly` | `Terms` | — |
| `Semester` | `Semesters` | — |
| `DateRange` | `None` | the span must fall **inside** `[EnrollmentStartDate, EnrollmentEndDate]` (null bound = unbounded, §Q10) |
| `OpenEnded` | `None` | any span, open or bounded |

> **v1 said** `OpenEnded`/`DateRange` "cannot be blocked at all (they have no period to
> block)". True only while a period was the sole shape; v3 removes the clause.

**FR-57 (grade-owned).** No shape constraint at all — a grade has **no window**
(`GradeLevel` carries `Id, CodedValueId, Level, Name, DisplayOrder, MinAge, MaxAge,
AllowedGenderCodedValueId, IsBlockedFromEnrollment`, and timestamps) and no period
reference to bound against. A grade exception may use any `Division` and any span.
*(v1's "period must be within the active academic year" rule dissolves with the
`PeriodId`.)*

**Retired-period exclusion** — **dissolved** (§2.2). It was a rule about periods.

### 4.3 FR-58 — refined (now purely date-based)

```
IsTopicAssignedAsync(gradeLevelId | activityGroupIds, topicId, effectiveDate)
  → return bridgeExists(owner, topic) effective on effectiveDate
     && !exceptionContains(owner, topic, effectiveDate)
```

The **contract signature is unchanged**; `Assignments.Core` does not change at all.
The Students-side implementation becomes *simpler* than v1's — no active-period
resolution, no id set.

---

## 5. Surfaces — one management page, thin entry points

### 5.1 The page (primary, and the only place exceptions are edited)

`EnrollmentExceptions` page under Students, owner-aware:

- **Owner selection** mirroring the Topics landing's owner toggle — a grade level **or**
  an activity group (the group side gated by `FEATURE:EnableActivityGroups`).
- **List**: topic, the **period part** (`Term` / `Semester` / `—`), the span rendered
  human-readably (`Term 3`, or `1–14 Mar 2027`, `from 1 Mar 2027`, `to 14 Mar 2027`),
  the optional reason, and a delete control.
- **Add**: pick a topic; pick the part (**Term** or **Semester**, or *"not term-scoped"*);
  pick the span — a **term/semester picker** that resolves to that part's dates, **or**
  free start/end dates, each independently clearable to mean open. Term/semester choices
  come from the tenant's period hierarchy filtered by the owner's rule (§4.2).
- **Empty state is normal** — zero exceptions is the expected state, not a warning.
- **Immediate write.** No Save button, no form model — a submit-shaped page here would
  either lie or silently persist something else.

### 5.2 Entry points (thin — badge + navigation only)

| Surface | What it shows |
|---|---|
| **Topics landing** (`/students/subjects`) | per-row **count badge**; the kebab's **"Enrollment exceptions"** action navigates to the page with owner + topic pre-selected. Owner gate **widened to include activity groups** (owner-confirmed 2026-09-26 — the v1 grade-only rationale is void because exceptions now have group owners, and the landing already resolves `ActivityGroupId` per row). |
| **Activity groups landing / details** | ⏸ **DEFERRED (owner sign-off 2026-09-26) — not in this round.** These two pages render **no subject list** (verified: every `topic\|subject` hit is a `catch (Exception ex)`), so this row presupposes a surface that does not exist. The group side remains fully reachable via the management page's owner toggle (§5.1). Revisit only if a per-subject surface is added there. |
| **Grade-detail Subjects card** (`GradeLevels/Detail.razor`) | a **count badge** next to the subject (`2 exceptions`) plus a kebab that **navigates**. **No list, no inline editor.** |
| **`GradeTopicsDialog`** (View all subjects) | the same count badge and navigating action. |

> **Why a badge rather than v1's descriptive label** (owner decision): listing
> exceptions on the card makes a complex UI; a count badge is scannable and the kebab
> takes you to the detail. `Offered in every period` therefore **disappears** from the
> card — the absence of a badge is the normal state.

### 5.3 Retired in v3

- **`SubjectBlocksDialog`** (v1) — replaced by the page; its body becomes the page's
  list section.
- **`TopicEditDialog`'s exceptions section** — already stripped in v1; stays out.
- **`TopicPeriodsEditDialog`** — deleted in v1.
- **The archived-marker rendering and its trap rule** — dissolved with the `PeriodId`
  (§2.2). `SubjectBlockLabels.IsRetiredPeriod` and the retired-marker tests go with it.

### 5.4 Rules that disappear (they existed only because a whitelist needs them)

- **FR-ED-10** (a year-spanning row cannot coexist with period rows) — no such row.
- **FR-ED-13** (duplicate periods rejected) — the unique index forbids it; the UI omits
  already-excepted spans from the picker and the server answers 409 for a stale list.
- **FR-ED-14** (the set may not be emptied) — an empty list is the normal state.
- **FR-ED-9** (two meanings of "a year") — an exception is one concrete span.
- **v1's retired-period rules** — nothing references a period.

### 5.5 The sibling spec

`RequiresEnrollment` (`subject-enrollment-requirement-flag.md`) decides *who* may
receive work, on the same Subjects card and topic edit dialog. It does **not** touch
this page; see that spec §8 (FR-ER-25/FR-ER-27).

---

## 6. API surface

Students API, grouped per `endpoint-organization-pattern.md` — its **own extension
method on the students group chain**, never inline in `Program.cs`.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/students/enrollment-exceptions?gradeLevelId={id}` | list a grade's exceptions (or `?activityGroupId={id}`), optional `&topicId={id}` |
| `GET` | `/students/enrollment-exceptions/check?gradeLevelId={id}&topicId={id}&onDate={date}` — **or** `?activityGroupId={id}&topicId={id}&onDate={date}` | is this topic excepted on this date (pickers) — accepts **either** owner form, mirroring the list route, so both sides of the owner toggle (§5.1) have a check |
| `POST` | `/students/enrollment-exceptions` | create — `division` + `startDate`/`endDate` (at least one bound) |
| `DELETE` | `/students/enrollment-exceptions/{id}` | remove (idempotent soft delete) |

The body carries **no period id** — that is the point (§0 decision 7). A body with
neither bound is **422**, mirroring the entity invariant.

**No feature flag** — this touches enrollment correctness; a flag would mean two
divergent availability rules at runtime.

`PUT /topic-assignments/{id}/period` and `PeriodId` on `AssignGradeTopicRequest` stay
**no-ops, deprecated not removed** (v1), guarded by
`DeprecatedPeriodWritePathArchitectureTests`.

---

## 7. Migration

v1's table is **unreleased**, so v3 edits that migration rather than adding another —
the rename, the dropped column and the new shape are all free now.

**Step 0 — measure first (unchanged):**

```sql
SELECT topic_assignment_type, grade_level_id, activity_group_id, count(*)
FROM topic_assignments
WHERE period_id IS NOT NULL
GROUP BY 1, 2, 3;
```

- **Zero rows** → purely additive: create `subject_enrollment_exceptions` (no
  `period_id` column at all) plus the two **expression** unique indexes below.
- **Non-zero rows** → v1's rule stands: ship the **additive table only**; the
  whitelist→exception **conversion** is not written into the migration and is deferred
  to a separately reviewed script under owner sign-off. *(v1 measured non-zero — one
  pinned dev row. Converting it would encode "offered only in X" semantics that §0
  decision 2 rules out, so leaving it inert remains the consistent action.)*

**Raw-SQL index the EF model cannot express** (§2.2):

```sql
CREATE UNIQUE INDEX ix_enrollment_exceptions_tenant_grade_topic_span
  ON subject_enrollment_exceptions
     (tenant_id, grade_level_id, topic_id, division,
      COALESCE(start_date, '-infinity'), COALESCE(end_date, 'infinity'))
  WHERE grade_level_id IS NOT NULL AND is_deleted = false;
-- plus the activity_group_id twin with the same filter on activity_group_id
```

`MigrationGuardTests.NoUncommittedModelChanges` compares the model snapshot to the
DbContext model; a raw index created only in SQL is not part of that model, so the
guard is unaffected. The index must be dropped in `Down`.

---

## 8. Open questions

**Q1 — scheduling vs exceptions?** ✅ **ANSWERED** — exception model adopted, no hybrid.

**Q2 — Keep or drop `GradeTopicAssignment.PeriodId`?** ✅ **SETTLED (spec default)** —
**keep and ignore** (§4.1). Dropping it is a second migration for no functional gain,
and the deprecated `PUT` write path still accepts it.

**Q3 — Soft or hard delete?** ✅ **SETTLED (spec default)** — **soft**, via
`BaseTenantEntityWithAudit` + `IHasRowVersion` (audit trail). The soft-delete filter in
the unique index is **required**, otherwise remove-then-re-add collides on the index.

**Q4 — Is a `Reason` field wanted?** ✅ **SETTLED at the specified default** — optional
free text (`string?`), never load-bearing. Structured reasons would need a coded-value
parent and a larger change; the owner was invited to ask for them and did not, so the
default holds. Not a blocker.

**Q5 — What happens to the `?periodId=` *listing* filter?** ✅ **ANSWERED (owner,
2026-09-26) — REMOVE IT.** `ListSubjectsByGradeAsync` takes one; it is the only
period-aware reader that matches exactly, so it returns nothing once rows carry
`PeriodId = null` — dead by construction. Remove:
`[SupplyParameterFromQuery] Guid? PeriodId` from `Subjects.razor:95`, its `:386`
call-site argument, the client parameter, and the handler's filter. The alternative
(keep it as a deprecated no-op, mirroring the `PUT`) was **rejected**: a filter that
silently returns an empty list is worse than one that no longer exists.
*Implementation note:* resolve by grep whether this is the **same** endpoint as
Defect 1's `ListTopicsByGrade` `?periodId=` reader; if it is, its removal retires the
last period-aware read path and §4.1 needs no further work.

**Q6 — "the active period" as a two-id set?** ✅ **DISSOLVED by v3** (§0 decision 9).
With no period reference in the predicate there is no active-period lookup at all. v1's
RISK-2 ruling is retained only as history.

**Q7 — Open-start spans?** ✅ **ANSWERED** — `StartDate` is nullable (§0 decision 8).
The uniqueness consequence is handled by the expression index (§2.2).

**Q9 — uniqueness with nullable bounds?** ✅ **ANSWERED** — `COALESCE` expression index
(§2.2/§7). A plain index would allow duplicate open-ended rows.

**Q10 — must a `DateRange` group's span fall inside the group's window?** ✅ **ANSWERED**
— yes, containment, with a null bound meaning unbounded (§4.2).

**Q11 — what does the picker offer for the *part* when the span is not term-shaped?**
✅ **ANSWERED (owner, 2026-09-26) — option (a), "recommended as-is".** The picker offers
**only the divisions the tenant actually has** (derived from its period hierarchy), plus
an explicit ***"not term-scoped"*** option that stores `Division = None`. A tenant with
plain `Division = None` years therefore never sees an empty Term/Semester list, and
`None` gets an honest label instead of reading as a magic value.

---

## 9. Testing requirements

Per `.github/copilot/rules/testing.md` (MSTest + Moq + FluentAssertions, MTP).
`dotnet-best-practices.md` and `ef-migrations.md` apply to the entity + migration.

**Domain / handler unit tests**

- Availability: bridge + no exception ⇒ available; bridge + exception whose span
  contains `effectiveDate` ⇒ **not**; bridge + exception whose span does **not** contain
  it ⇒ still available; no bridge ⇒ not available regardless.
- **Open bounds:** open start matches any earlier date; open end matches any later date.
- **The v1 regression guard:** an assignment with no `PeriodId` and no exception stays
  available after the year rolls over and the prior terms archive.
- **Entity invariants:** owner XOR; at least one bound; `EndDate >= StartDate`; both-null
  rejected.
- **Duplicates per shape ⇒ 409**, and specifically that **two open-ended exceptions with
  the same start are rejected** — the case a plain unique index would let through
  (Postgres NULL-distinct), which is the expression index's whole purpose. Real Postgres.
- **FR-56 division mapping:** `Termly` requires `Terms`; `Semester` requires `Semesters`;
  `WholeAcademicYear`/`OpenEnded` require `None`; `DateRange` requires `None` **and**
  containment inside the group's window, including the null-bound cases.
- **FR-57:** a grade exception is accepted with any `Division` and any span — the v1
  "outside the active academic year" rejection is **gone**, and its test is replaced,
  not deleted silently.

**FR-58 (`Assignments.Core`)** — port signature unchanged; assert behaviour only: a
subject excepted for the effective date **is absent from the feed** the unchanged client
consumes (this is the v1 `ByGradeFeed_…` integration test, re-expressed against a span).

**bUnit (page)** — empty state is normal, no warning; a term-shaped and a plain-window
row both render with their part and span; open-start and open-end rows render
`from …` / `to …`; removing the last exception succeeds; add-with-nothing-selected
writes nothing; **no Save control exists**; a server 409 surfaces the server's message.

**bUnit (badges)** — card and View-all show the **count badge** for a subject with
exceptions and **no badge** without, and the kebab **navigates** rather than opening an
editor.

**Retired by v3:** the v1/v2 archived-period marker tests, the retired-period exclusion
tests, and the "active period id set" cache-key tests all go with the `PeriodId`.

---

## 10. Out of scope

- A **whitelist/scheduling** mode ("offered only in Term 1") — §0 decision 2.
- **Structured** exception reasons (§8 Q4) unless the owner asks.
- The `SubjectEditDialog` on the Topics landing (`topic-edit-dialog-redesign.md` §8 Q3).
- Strands and lessons — unchanged.
- `documents/configuration.md` — **no** flag is added; the migration path may warrant
  one only under §7's non-zero branch, which ships an operator-run script.
- A **gapped single-row** selection ("Terms 1 and 3, not 2") — two spans, §2.4.
