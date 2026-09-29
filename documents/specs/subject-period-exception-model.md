# Spec: Subject Enrollment Exceptions (decouple subject availability from the period lifecycle)

> **Status:** **v6 — bulk sequence selection + the span HIDDEN when it is derived, 2026-09-29 — decisions 17–19 LOCKED and IMPLEMENTED.** **v5 — the part is CHOSEN, and the position is persisted 2026-09-28.** v1
> (period-only "blocks", grade-only UI) was implemented through phase 1 and reviewed CLOSED.
> v2 widened it to date windows and activity groups; **v3 removed the `PeriodId` dependency
> entirely** — an exception is expressed as a **period part** (term / semester) plus a
> **date span**, so it never references a period instance, and was implemented and reviewed
> CLOSED (commits `a50dff95`, `49d3ba5b`). **v4 redesigned the page's interaction model**
> (§5.1, decisions 11–14); **v5 keeps v4's model and reverses ONE of its decisions**: the
> part is no longer *derived from a picked period instance* but **chosen as an academic
> division** (decision 16, reversing 13), the period-instance picker is gone, and a
> **position** — 1st, 2nd, 3rd … — is chosen with it and **persisted on the exception** as
> its `ordinal` (decision 15). That last change is the reason v5 is not presentation-only:
> it adds `periods.sequence` and `subject_enrollment_exceptions.ordinal`, so it ships a
> migration (§7). The availability predicate, FR-56/57/58 and the entry-point contract are
> **unchanged by v5**. Superseded text is kept inline with strikethroughs so the reasoning
> trail survives.
> **v6 (2026-09-29) is specified, LOCKED and IMPLEMENTED**: it makes
> the add form's Position control a **multi-select** (several sequences written in one action) and
> **hides the date span whenever it is derived** from a period. Decisions **17–19** (§0) amend §5.1,
> §6 and §10 — see **§11**. Route **A** needs **no migration**.
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

**Locked in v4 (2026-09-26) — the page's interaction model, after the owner reviewed the
shipped page and called it clumsy and duplicative:**

11. **The filter row is `Grade Level` + `Subject`, on one line, and it is the ONLY subject
    control on the page.** The filter and the add form are the same subject — the add form
    is **defaulted to the filter** rather than carrying its own subject picker, so the two
    can never disagree. *This supersedes v3's separate "list filter + `Show all`" and
    "add-panel subject dropdown", which held two states (`_filterTopicId` and
    `_selectedTopicId`) derived from one query value and could drift apart.*
12. **A date span has ONE range input**, not four. Open ends are expressed *on the range*
    as `(any start)` / `(any end)`, not by a separate "Clear" button beside each bound.
    The term/semester picker becomes a **shortcut on that range** ("use Term 3's dates"),
    not a labelled control in its own right.
13. ~~**The period part is DERIVED, never chosen.** It is a function of the span: picking
    "Term 3" *means* `Terms`; free dates mean `None`. It is displayed as read-only text.~~ ⚠️
    **REVERSED in v5 by decision 16.** The reasoning that produced it was sound about the
    *symptom* (v3's part was simultaneously a control, a value and a label) and wrong about
    the *cure*: deriving the part from a picked period instance re-introduces the very thing
    decision 7 removed — a year-specific period instance the reader must pick — and it makes
    the part unreadable as an independent choice ("I want no Term-3 offering" is about the
    part, not about a particular 2027 row). Read-only text also cannot carry the position.
14. **One spelling per concept, shared by the list and the picker** — `Term` / `Semester` /
    `Any date`. v3 spelled the same concept three ways (the picker's "Not term-scoped", the
    list's "—", and a dropdown whose own `Label` was *derived* from the picked part, so a
    row could read "Period part [Term] Term [Term 3]"). **Kept, and now load-bearing in two
    directions**: the add section's "Fill from" control renders its options through the SAME
    label helper the list rows use, so the words on the control and the words on the row are
    one string, not two that happen to agree.

**Locked in v5 (2026-09-28) — the position, and the part as a choice:**

15. **A sub-period has a POSITION in its year's run of its division, and an exception
    persists the position it was written as.** `Period.Sequence` is a nullable, 1-based
    integer — `1` for the first term/semester of an academic year, `2` for the second, … —
    and **only a sub-period may carry one** (a top-level year is not the "1st" of anything).
    It is **unique per `(tenant, parent_period_id, division)` where non-NULL**, enforced by a
    filtered unique index, and readable at the API boundary: the create/update handlers
    pre-check it and answer **422 naming the sibling that holds the position**, because the
    index alone would surface the collision as an unhandled `DbUpdateException` (a 500).

    The exception side is `SubjectEnrollmentException.Ordinal` — the **same number**, chosen
    in the add section as the position (1st, 2nd, 3rd, …) beside the part.

    **Two rulings are part of the decision, and both are load-bearing:**
    - **The ordinal is descriptive; the dates are the truth.** Availability never reads it
      (§2.3), exactly as it never reads `Division`.
    - **The ordinal and the span are ALLOWED to disagree.** A tenant that has not periodised
      its years can still record "not offered in the 3rd term" against typed dates, and a
      subset of one term is a legitimate span for a whole-term ordinal.

    **The price, stated honestly:** a row can therefore read "3rd term" over dates that are
    not a 3rd term's, and nothing warns anyone. That is the cost of a position that is
    **structural rather than looked-up** — the position may be named even when no period of
    that position exists (round owner decision **Q1**, 2026-09-28), which is the whole point: an exception is recorded before
    the calendar is built, not after.
16. **The period part is CHOSEN as an academic division, and offered only where it can be
    written.** The add section's "Fill from" control offers `Term` / `Semester` / `Any date`:
    - For a **grade** owner (FR-57 is unconstrained) it offers the divisions the tenant
      actually has periods for, plus `Any date`. A part with no periods behind it is not a
      free choice — it is a part nobody can fill from, and offering it only invites a span
      that no calendar supports.
    - For a **group** owner, FR-56 leaves exactly **one** legal division
      (`Termly`→`Terms`, `Semester`→`Semesters`, everything else→`None`), so exactly one is
      offered. **FR-56 remains enforced server-side**; the control cannot offer an invalid
      part, which is a stronger guarantee than v4's "derive the only valid one".
    - **`Any date`** (division `None`) offers **no position**, because a free window has no
      position in a run of terms or semesters — and the server rejects an ordinal on one.

    Choosing a **position whose period exists** fills the range from that period's dates;
    choosing one with **no** period leaves the range empty, and `Add exception` stays
    disabled until at least one bound is typed. That is the intended flow, not an error.
    The **period-instance picker is gone** — there is no period selection anywhere on the
    page any more, only a division and a position.

**Decisions 11–12 and 14 touch presentation only. Decisions 15–16 do not**: 15 adds
`periods.sequence` and `subject_enrollment_exceptions.ordinal` and therefore a migration
(§7). FR-56/57/58 and the availability predicate are untouched, and so is the entry-point
contract in §5.2 — the four surfaces still navigate here with the owner and the subject in
the query string, which is the filter row's initial state rather than a separate
pre-selection. **Q4 (owner decision, 2026-09-28):** the page preselects the owner **kind**
only — no grade level and no subject — so it lands with no filter applied rather than
silently scoping to the first grade level.

**LOCKED in v6 (2026-09-29) by the owner — route A and the SILENT span confirmed explicitly, and
IMPLEMENTED in the same round.** (The owner was shown the one cost of silence in §11.2 and chose it
anyway; that acceptance is recorded there so it cannot later be mistaken for an oversight.)

- **Decision 17 — the span is hidden when it is derived.** With a real part chosen and a period of
  that part actually holding the chosen position, the two date pickers are **removed entirely** — no
  derived-span text beside them (owner decision, 2026-09-29: *"keep span fully silent"*). The pickers
  stay whenever no period holds the position, because v5's documented "type the bounds yourself"
  flow must remain reachable — and `CanAdd` requires a bound, so hiding them there would **dead-end
  the form** (button disabled forever, with no control able to satisfy it). `Any date` keeps the
  editable range in every case. (§11.2)
- **Decision 18 — a multi-sequence selection writes ONE ROW PER SEQUENCE**, not one row holding a
  set. A bulk insert is **one request**, which is what the owner wants, and that is a *command*
  concern rather than a *row* concern. Storing several sequences in one `jsonb`/string column would
  contradict §2.4 ("two spans"), make the existing duplicate key `(owner, topic, division, span)`
  ambiguous, turn the `/check` equality test into a containment query, need an edit path to remove
  a single sequence, and add a migration — all to save **list rows**, which is a *presentation*
  concern that a compact rendering of contiguous ordinals ("1st–4th") solves without touching the
  model. A delimited string is the worst of the three: no containment query, no integrity. (§11.3)
- **Decision 19 — the two are coupled.** With several sequences chosen, whether the span is derived
  is decided **per sequence**, so the span inputs are shown whenever **any** chosen sequence has no
  period behind it. (§11.4)

**LOCKED in v7 (2026-09-30) by the owner — the span is hidden for EVERY real part.**

- **Decision 20 — the Position row and the range are mutually exclusive.** The range (the "Not
  offered from / to" pickers) belongs to **`Any date` alone**. The moment a real part (`Term` /
  `Semester`) is chosen, the range is **removed entirely** — v6 decision 17 hid it only when every
  chosen sequence resolved to a period; v7 hides it **unconditionally** for a real part. `Any date`
  still shows it, because a free window has no period to derive from.
- **Decision 21 — an unbacked position is DISABLED, not hidden.** The ladder keeps its structural
  SHAPE (Q5: 1st–4th, extended by any higher declared position) but a position the tenant has **no
  period for** is rendered **disabled**, so it cannot be chosen. This replaces v6's dead-end guard
  (§11.2's third table row) without dead-ending the form: every selectable position is backed by a
  period, so `CanAdd` is satisfied on the choice alone and no typed bound is ever needed for a real
  part. **This retires the Q1 "record a 3rd term before the calendar is built" write flow** — an
  owner-accepted reversal, recorded rather than absorbed (§11.7).
- Consequence: a real part with no position ticked is a **pre-write state**, not a write. The add
  section says *"Choose a position to add an exception."*

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

    /// <summary>
    /// The POSITION this exception was written as — 1 for "the 1st term", 2 for "the
    /// 2nd", … (v5 §0 decision 15). Null when the division names no real part (a free
    /// window has no position) or when no position was chosen.
    ///
    /// DESCRIPTIVE, like Division: availability never reads it, it is not part of the
    /// duplicate key or of the COALESCE unique index, and it and the span are deliberately
    /// allowed to disagree — a tenant that has not periodised its years can still record
    /// "not offered in the 3rd term" against typed dates.
    /// </summary>
    public int? Ordinal { get; private set; }
}
```

*Invariants added in v5:* the ordinal is 1 or greater, and it may only be supplied when
`Division` names a real part (`ValidateExceptionOrdinal`, a 422 at the boundary). It is
deliberately **not** cross-checked against the span — that absence is decision 15's second
ruling, not an oversight.

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

`EnrollmentExceptions` page under Students, owner-aware. **Redesigned in v4** (owner review of
the shipped v3 page, which was reported clumsy and duplicative). The thesis: **one subject
per view, and one control owns it.**

- **The filter row — `Grade Level` + `Subject`, on one line (decision 11).** This row is
  the page's only subject control, and it is the page's scope: the list, the count badge and
  the add form all describe exactly the subject it names.
  - `Grade Level` mirrors the Topics landing's owner toggle — a grade level **or** an
    activity group (the group side gated by `FEATURE:EnableActivityGroups`), rendered in
    the same slot.
  - `Subject` lists the owner's subjects plus an **"All subjects"** option. Choosing
    "All subjects" *is* the reset — there is no separate "Show all" button, because a filter
    is reset by changing the filter. It is the owner's **whole set**, so it lists every row
    (see the empty states below).
  - With no subject in scope the add section is **closed**, not merely disarmed: the add
    section's subject IS the filter's, so with "All subjects" there is nothing to write for.
  - Arriving from an entry point (§5.2) with `?gradeLevelId=` / `?activityGroupId=` /
    `?topicId=` pre-selects the row; the subject is only applied once the owner's subject list
    has loaded, so a subject the owner does not list leaves the row unscoped rather than
    filtering to nothing.
- **List**: subject, the **period part** (`Term` / `Semester` / `Any date` — decision 14), the
  span in words (`Term 3`, `1–14 Mar 2027`, `1 Mar 2027 – 30 Jun 2027`, `from 1 Mar 2027`,
  `to 14 Mar 2027`), the optional reason, and a delete control. When a subject is in scope
  the subject column is redundant with the filter and is **omitted**; the row shows part,
  span, reason, delete. Open-start spans lead (the widest span first).
- **Add**: the subject comes from the filter (**defaulted**, decision 11) and is shown in the
  add section's heading ("Add exception for *Mathematics*") — **not** a second dropdown.
  With "All subjects" in force the add section renders a prompt instead of a form: *"Choose a
  subject above to add an exception for."*
  - **"Fill from" — the part is CHOSEN (decision 16, reversing 13).** A dropdown of
    `Term` / `Semester` / `Any date`: the divisions the tenant has periods for plus the free
    window for a grade owner, and exactly the one division FR-56 permits for a group owner.
    Its labels come from the same helper the list rows use, so the control and the row spell
    the part identically (decision 14).
  - **Position — a MULTI-SELECT row of `FluentCheckbox`es: `1st` / `2nd` / `3rd` / `4th`, plus
    any higher position the tenant has declared** on a period of that division (decision 15,
    offered only when the division names a real part). Ticking several writes **one exception per
    sequence, in ONE transaction** (v6 §11.3, decision 18), which is what makes a gapped selection
    ("1st and 3rd") a single action; a checkbox is also the one control here that can be un-picked.
    The ladder is **structural** (Q5): 1st–4th are always rendered and a declared higher position
    extends it. **v7 (decision 21): a position the tenant has no period for renders DISABLED** — it
    is not hidden, so the year's shape stays visible, but it cannot be chosen, because the range is
    silent for every real part and such an item could never be written.
    Native control, chosen by the repo's own rule (FluentUI's parameters before custom CSS):
    FluentUI 4.14.2 has no `FluentListBox` and no `FluentField`, and `FluentRadioGroup` is
    single-value, so the multi-select is a row of `FluentCheckbox` in a native `FluentStack`,
    bound through the **inherited** `Value`/`ValueChanged` — **not** `CheckState`, which is the
    three-state parameter and throws unless `ThreeState` is true.
  - **One range input, shown ONLY on `Any date`** (decision 12, plus v6 §11.2 decision 17 and v7
    §11.7 decision 20): a single labelled **"Not offered"** range whose ends are independently open.
    An open end renders as **`(any start)`** / **`(any end)`** — the openness is part of the range,
    not a separate Clear button beside it. It is **removed entirely** the moment a real part is
    chosen: each span is then DERIVED from its own position's period and kept **silent** (v6 §11.2 —
    no read-only text either). It stays removed even when no position is ticked yet; that state is a
    pre-write state, named by the add section's note, not a reason to bring the pickers back.
    The **period instance picker is gone entirely** — the page has no period selection any more,
    only a division and a set of sequences.
  - The layout is the house form primitive (`FormRow`) with the label **beneath** its input
    (round owner decisions **Q3/Q7**, 2026-09-28 — an optional parameter on the shared
    `FormRow`), which replaces v4's floating read-only part badge and the
    `margin-top` nudge that kept it on the row's label line.
  - Optional **reason**, then **Add exception** — which posts ONE request carrying one item per
    ticked sequence, each with its own span and ordinal.
- **Empty states are normal prose, never a warning**, and there are exactly **two**, because
  there is only one distinction that matters — *is a subject in scope?* "All subjects" is not
  a third case: it is the owner's **whole set**, so it lists everything. (An earlier draft of
  this section specified a third state that PROMPTED instead of listing when "All subjects"
  was chosen and the owner had rows — which would have made the option hide the very set it
  promises to show. Corrected here and in the implementation.)
  1. *No subject in scope, owner has none* — "No exceptions — every subject is offered on
     every date."
  2. *A subject is in scope and has none* — "No exceptions for **Mathematics** — this subject
     is offered on every date." The owner's default sentence would be **false** here (the owner
     may well have an exception, just not this subject's), which is why the two cannot be one.
- **Immediate write.** No Save button, no form model — a submit-shaped page here would either
  lie or silently persist something else. A successful write re-reads the list; a 409/422
  surfaces the server's sentence verbatim.
- **The count badge counts the OWNER's total**, not the filtered rows, and is shown only
  when that total is non-zero. Under a subject filter it is what tells the reader the rows
  below are a subset.

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
| `POST` | `/students/enrollment-exceptions` | create — `division` + `startDate`/`endDate` (at least one bound) + optional `ordinal` (v5: the chosen position; **422** on a free window, and never consulted by availability or by the duplicate check) |
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

### 7.1 v5 — the position and the ordinal (migration `20260928174930_AddPeriodSequenceAndExceptionOrdinal`)

Two nullable columns, one filtered unique index, and two backfills. **Order inside `Up()`
is load-bearing:**

1. `AddColumn subject_enrollment_exceptions.ordinal` (`integer null`)
2. `AddColumn periods.sequence` (`integer null`)
3. **Backfill 1 — `periods.sequence`.** For **sub-period rows only** (`division <> 0`): take
   the trailing integer from `name` **anchored at the end** (`([0-9]+)[[:space:]]*$`, so
   `"2027 Term"` does not parse as 2027), else fall back to the 1-based position by
   `start_date` within the same `(tenant_id, parent_period_id, division)`. Top-level years
   stay **NULL** — that NULL is what makes the filtered index correct, since Postgres treats
   NULLs as DISTINCT and a plain unique index would then allow many unpositioned rows (which
   the atomic year-create and the grid's auto-split legitimately produce).
4. **Backfill 2 — `subject_enrollment_exceptions.ordinal`.** Set **only** where an exception's
   span **exactly equals** a positioned sub-period's span in the same tenant and division.
   Conservative by construction: everything else (unmatched span, shifted span, an open
   bound, a tenant that never periodised) stays NULL. `DISTINCT ON … ORDER BY … sequence`
   makes the match deterministic when two years happen to share a span.
5. `CreateIndex ix_periods_tenant_parent_division_sequence` **last**, so the
   one-position-per-run rule is asserted *against the data backfill 1 just produced*: a
   collision aborts the migration loudly with the offending key rather than silently
   shipping a half-positioned column.

`Down()` drops the index first, then the two columns in the reverse of the order they were
added; the backfills need no reversal, because dropping the columns discards what they wrote.

The index name is **42 bytes** — inside Postgres's 63-byte identifier limit, so nothing is
truncated and the model snapshot cannot desynchronise over a silent rename. The name matches
the `HasDatabaseName(...)` in `PeriodConfiguration` exactly, which is what keeps
`has-pending-model-changes` green.

**The handler pre-check.** `PeriodSequenceGuard` (shared by `CreatePeriodHandler` and
`UpdatePeriodHandler`) rejects a position a sibling already holds with a
`PeriodSequenceTakenException` → **422 naming the sibling**, excluding the period being
updated so an edit can keep its own position. Without it the collision would reach the index
and surface as an unhandled `DbUpdateException`.

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

**bUnit (page) — REWRITTEN by v4.** v3's page shipped with **29 bUnit tests**
(`EnrollmentExceptionsPageTests.cs`) that encode the *old* shape — a separate list filter with
its own `Show all`, an add-panel `Subject` dropdown, a `Period part` picker, and two Clear
buttons beside `From`/`To`. v4's interaction model (decisions 11–14) changes what several of
them assert, so the v4 round must **map each to a replacement, never delete one silently**
(§(j)'s rule, inherited):

| v3 test | v4 fate |
|---|---|
| `QueryString_FiltersTheListToThePreSelectedSubject` | **kept**, re-expressed against the filter row's `Subject` dropdown |
| `ShowAll_ClearsTheFilter_AndRestoresTheOwnersWholeSet` | **replaced** — "All subjects" in the dropdown *is* the reset; there is no button |
| `FilteredEmpty_SaysTheSubjectHasNone_NeverThePageDefaultEmptyState` | **kept** — §5.1 empty state 2 |
| `NoTopicInTheQuery_ListsEverySubjectsExceptions` | **kept** — no `?topicId=` ⇒ the row defaults to "All subjects" |
| `QueryString_TopicTheOwnerDoesNotList_LeavesTheListUnfiltered` | **kept** — an unresolvable subject leaves the row unscoped, not blank |
| `AddForAnotherSubject_ClearsTheFilter_SoTheWrittenRowIsVisible` | **retired** — with one subject control there is no "another subject" case; the add form is scoped by definition |
| `FailedAdd_LeavesTheFilterAlone` | **replaced** — a 409 leaves the scope untouched (still meaningful) |
| `PartPicker_GroupOwned_*` (2) | **retired as control tests** — the UI can no longer offer an invalid part; the FR-56 rejection is asserted at the **handler** level, where it already is |
| `PartPicker_OffersTheDivisionsTheTenantHas…`, `…TenantWithoutDivisions…` | **retired** — no part picker exists |
| `ClearDate_UnbindsItsOwnDate_AndRecomputesTheWriteAffordance` | **replaced** — openness is now `(any start)`/`(any end)` on the range |
| `AddWithNothingSelected_WritesNothing` | **replaced** — under "All subjects" the add section is **disabled with a prompt**, so the "nothing selected" state no longer exists |
| `FilterLine_EmptyState_AndSpanGroup_CarryTheirAccessibleNames` | **kept**, re-pointed at the filter row's status region |
| `ChangingOwner_ResetsTheAddPanel`, `PageOffersNoSaveControl`, `DuplicateRejectedByTheServer…`, `PickersCheck_ShowsTheHint…`, the term/window/owner-toggle/pre-selection tests | **kept** |

**New v4 coverage with no v3 analogue:** the **derived part** flipping when a period supplies
the span vs when the ends are typed; the **`(any start)` / `(any end)`** rendering and its
effect on `CanAdd`; and **"All subjects"** restoring the owner's whole set.

**bUnit (badges)** — card and View-all show the **count badge** for a subject with
exceptions and **no badge** without, and the kebab **navigates** rather than opening an
editor.

**Retired by v3:** the v1/v2 archived-period marker tests, the retired-period exclusion
tests, and the "active period id set" cache-key tests all go with the `PeriodId`.
**Retired by v4:** the part-picker tests (4) and the Clear-date test (1) — v4 has no such
controls (see the map above).

**bUnit (page) — REWRITTEN AGAIN by v5.** v4's `DerivedPart_*` tests encode the *derived*
part, which decision 16 removes, so each was replaced by its chosen-part equivalent and the
predecessor is named in the replacement's doc comment. The subject/scope/filter/empty-state
half of the file is untouched: v5 keeps v4's interaction model, and only the add section's
shape changed.

| v4 test | v5 fate |
|---|---|
| `DerivedPart_FlipsBetweenTermAndAnyDate_WithTheRangeSource` | **replaced** → `ChosenPart_IsAControl_OfferedOnlyWhereTheTenantHasPeriods` — the part is a control, it is offered only where it can be written, the read-only badge is gone, and every add-section row puts its label beneath its input |
| `AllDateTenants_ReadAnyDate_BecauseNoPeriodDerivesAPart` | **replaced** → `ChosenPart_TenantWithoutDivisions_OffersAnyDateOnly_AndNoPosition` — same tenant, same promise ("no part this tenant cannot write"), asserted against the control |
| `DerivedPart_GroupOwned_TermlyGroup_CanOnlyEverBeTerm` | **replaced** → `ChosenPart_GroupOwned_TermlyGroup_OffersTermOnly` — FR-56 enforced at the control (exactly one option) *and* the group-side ordinal path asserted through the write |
| `DerivedPart_GroupOwned_UnAlignedSpan_OffersNoShortcut_AndReadsAnyDate` | **replaced** → `ChosenPart_GroupOwned_UnAlignedSpan_OffersAnyDateOnly_AndNoPosition` |
| every other test in `EnrollmentExceptionsPageTests.cs` | **kept**, with one mechanical change: Q4 removed the first-grade auto-selection, so a test that needs a scope now **names an owner in the query string** instead of relying on the page to pick one |

**New v5 coverage with no v4 analogue:** the position options are **1st–4th plus any higher
position the tenant has declared, and are NOT limited to the periods that exist** (round owner
decisions Q5/Q1); a position **whose period exists** fills the range; a position with **no** such period **clears**
it and keeps `CanAdd` false until a bound is typed; the written body carries the **chosen
`division` and the chosen `ordinal`**; the **ordinal is not part of the duplicate key**; the
position pick is deterministic when several years hold the same position; and the page lands
with **no owner, no grade and no subject** (round owner decision Q4).

**Domain / handler / component coverage owed by v5:** `Period.Sequence` invariants (≥ 1,
sub-period-only) on create **and** update; `ValidateExceptionOrdinal`'s two rules (≥ 1, and
only on a real part) plus the deliberate **absence** of a span↔ordinal cross-check; the
period form's Sequence row offering **only positions no sibling holds** (and keeping the row's
own); and `PeriodSequenceGuard`'s pre-check on both handlers, including that the row being
updated is excluded from its own check.

**Real-Postgres integration coverage owed by v5** (`PeriodSequenceAndExceptionOrdinalEndpointTests`):
a posted `sequence` and a posted `ordinal` each **persist and read back** through the API; a
taken position is a **422 naming the sibling** on create and on update; the **filtered unique
index** rejects two sibling rows inserted directly (SQLSTATE `23505` — the only test that can
observe the index, since the guard means the endpoint never reaches Postgres with a collision);
sibling rows **without** positions do not collide (the index is filtered on `sequence IS NOT
NULL`); and an exception with a **different ordinal for the same span** is still a **409**.

---

## 10. Out of scope

- A **whitelist/scheduling** mode ("offered only in Term 1") — §0 decision 2.
- **Structured** exception reasons (§8 Q4) unless the owner asks.
- The `SubjectEditDialog` on the Topics landing (`topic-edit-dialog-redesign.md` §8 Q3).
- Strands and lessons — unchanged.
- `documents/configuration.md` — **no** flag is added; the migration path may warrant
  one only under §7's non-zero branch, which ships an operator-run script.
- ~~A **gapped single-row** selection ("Terms 1 and 3, not 2") — two spans, §2.4.~~
  **REVERSED in v6 (decision 18):** a gapped selection ("Terms 1 and 3") is now **supported** — as
  **two rows**, which is exactly what §2.4 always implied. What remains out of scope is the
  *single-row* spelling of it, rejected for the reasons in §11.3 — and a **single row holding two
  different spans**, which §2.4 rules out on the model's own terms.

---

## 11. v6 — bulk sequence selection, and a span that is hidden when it is derived

> **Status: LOCKED and IMPLEMENTED (2026-09-29).** The owner asked for two
> changes to the add form. Both are specified here because the second is backend work **and because
> the two are coupled** — the span section's visibility is decided *per selected sequence*, so it
> cannot be settled before the selection model is. **Route A** and the **silent** span were both
> confirmed by the owner; §11.2 records the one cost that acceptance carries.
>
> **Amended by v7 (§11.7, 2026-09-30):** the per-sequence guard below is **retired**. The span now
> belongs to `Any date` alone; a real part always hides it, and a position with no period is
> disabled rather than given typed bounds.

### 11.1 What was asked for

1. *"when [the] date range is not selected, [the] date span section should be hidden"* — with a real
   part chosen (`Term` / `Semester` + a position), the two date pickers should not be shown.
2. *"allow multiple sequence selection, for bulk insertion"* — several positions (e.g. 1st **and**
   3rd) chosen at once and written in one action.

### 11.2 Decision 17 — the span is hidden when it is derived; the pickers stay when it is not

Hiding the pickers outright is not safe. Two concrete reasons, both visible in the shipped code:

- **"A position with no period behind it" is a supported flow, not an error.** v5's own words: *the
  reader "types at least one bound and the write becomes possible again"*. `CanAdd` requires one —
  `(_startDate is not null || _endDate is not null)`. Hide the pickers for every real part and that
  flow becomes a **dead end**: choose 3rd term, the tenant has no 3rd-term period, and no control
  anywhere can satisfy `CanAdd`. The button is disabled forever.
- **The span is the data.** The ordinal is explicitly *descriptive* (decision 15) and the **span is
  the truth** (§2.3). Silencing the span while still writing one derived from the period means the
  reader cannot see the dates they are recording — and v5 explicitly allows the ordinal and the span
  to disagree, which is exactly when that matters.

**Owner decision (2026-09-29): the span is FULLY SILENT** — no derived-span text either. The table
below is therefore the locked behaviour, and the second argument above is a **cost the owner has
accepted explicitly**, recorded here so it is not later mistaken for an oversight:

| Situation | Span section |
|---|---|
| `Any date` (division `None`) | **Shown, editable** — the free window is the whole point |
| Real part + position, and a period of that part **holds** that position | **Removed entirely** — no pickers and no derived-span text |
| Real part + position, but **no** period holds it | **Shown, editable** — unchanged v5 flow; `CanAdd` still needs a bound |
| Real part chosen, no position yet | Shown, editable — nothing is derivable yet |
| Any of the above, with several sequences chosen | Per §11.4 — shown if **any** chosen sequence has no period |

The guard in the last three rows is **not optional** and is not part of the silent-vs-read-only
question: it is what keeps the form reachable at all. Silence applies only where a span is genuinely
derivable, which is the first two rows.

> ⚠️ **SUPERSEDED by v7 (§11.7, decision 20/21).** The three guard rows above no longer describe the
> page. v7 removes the range for **every** real part and disables an unbacked position instead, so
> the guard's dead-end risk is designed out rather than handled. The table is kept as the v6 record.

### 11.3 Decision 18 — multi-sequence selection writes **one row per sequence**

Owner's question: *"keep multiple rows, or single row? My choice is single row, using json field or
string concatenation."* **Recommendation: multiple rows.** The instinct about the *write* is right — a
bulk insert should be one action — but "one row" is not what delivers that. A **bulk command** is.
Rows and requests are independent axes.

| | **A — one row per sequence** *(recommended)* | **B1 — single row, `jsonb` set** | **B2 — single row, delimited string** |
|---|---|---|---|
| Schema change | **none** | new column + canonical unique index | new column (+ index) |
| Fit with §2.4 | **exact** — a gapped selection *is* two spans | contradicts: two spans, one row | contradicts |
| Duplicate key `(owner, topic, division, span)` | **unchanged** — each sequence derives its own span | ambiguous: is `{1,3}` a duplicate of `{1}`? Needs a sorted canonical expression index | same, plus ordering bugs |
| `/check` containment | unchanged equality test | containment (`@>`) + GIN index | `LIKE`/regex — fragile, unindexable |
| Remove one sequence | delete that row (exists already) | **new edit path** (new command + concurrency surface) | same |
| Availability predicate (§2.3) | unchanged | must iterate the set | must parse |
| Backend cost | bulk command + endpoint + tests | above **+ migration + index + query + update path** | above, with a worse query |

- **B2 should be dropped on its own merits**: a delimited string has no containment query and no
  array integrity, and cannot be indexed for "does this row cover 3?". It is a JSON set with none
  of the benefits.
- **B1 is workable but pays a lot to save list rows.** Its one genuine advantage would be a shorter
  list ("Terms 1, 2, 3" instead of three rows) — and that is **presentation**, not storage. Route A
  was originally justified here by "rendering a contiguous run of ordinals as a range", on the
  assumption the list showed ordinals. **It never did** — the list row is part + span + reason
  (`SpanLabel`), and the ordinal has no display at all — so that justification is **withdrawn as
  untrue**. Route A stands on the reasons above it (schema, key, `/check`, delete granularity,
  atomicity), not on a list rendering that does not exist. If a compact ordinal display is ever
  wanted it is a read-model change to `SubjectEnrollmentExceptionDto`'s consumers, and it is
  **not** required by, nor an argument for, the row strategy.

**The bulk write (what the owner actually wants):**

- One `POST /students/enrollment-exceptions` whose body carries the chosen sequences **as a set**
  (e.g. `ordinals: [1, 3]`); the server loops in **one transaction** — N rows created or none.
  Partial success is not offered. The response returns the created ids.
- A duplicate inside the batch (e.g. `{1,3}` where 1 already exists) fails the **whole** batch with
  the same **409** the single-sequence path returns, naming the sequence — the batch's atomicity
  matches the error's granularity.
- No new cross-context port, no `PeriodId`; §2.3 still reads one row at a time.

### 11.4 Decision 19 — the coupling (why these are one spec, not two)

With several positions selected, whether the span is derived is decided **per sequence**: 1st may
resolve to a period and 3rd may not. So:

> **The span inputs are shown whenever `Any date` is chosen, OR any selected sequence has no period
> to derive from. If every selected sequence resolves to a period, the inputs are removed entirely
> and no span text is shown (decision 17 — silent).**

With more than one sequence in play the write carries each sequence's **own** derived span, and none
of them is displayed — which is precisely why route **A** is the model-shaped answer and B is not:
the spans must be separable **in the data** even though the reader never sees them.

### 11.5 Surface, migration and DTO impact

- **API (§6 amended):** a **NEW** `POST /enrollment-exceptions/bulk` carries the batch — owner,
  subject, division and reason once, plus a list of **items** each holding its **own** span and
  ordinal — and answers **200** with the created `ids`. The single-item route, command and handler
  are **kept unchanged**.
  *Why additive, rather than making the existing create set-shaped (the original wording here):* a
  set-shaped body forces all **45 existing constructor sites** in
  `CreateSubjectEnrollmentExceptionHandlerTests` (42) and `ListGradeTopicAssignmentsCacheKeyTests`
  (3) to become one-item lists — each site's arguments must be **split** between the outer record and
  a new per-item record, which is a large mechanical diff over a carefully-reasoned test file that
  buys no behaviour. A separate bulk route is also the repo's **own** idiom
  (`POST /coded-values/bulk` → `BulkCreateCodedValues` → 200 + a result object), and it keeps 45
  reviewed assertions untouched. The UI still has **one** write path: the page posts to `/bulk` for
  every add, one sequence or several.
  The `/check` route is **unchanged** under route A — it still answers for one anchor date.
- **Migration: NONE under route A.** `subject_enrollment_exceptions.ordinal` is already an `int?`
  with the existing duplicate key over the span; N sequences are N rows. **Route B1 would need one**
  (new `jsonb` column + canonical unique index) — a further reason to prefer A.
- **DTO:** `SubjectEnrollmentExceptionDto` is unchanged; the list simply returns N rows.
- **Page (§5.1 amended):** Position becomes a **multi-select**; the span section follows 11.2/11.4;
  `CanAdd` becomes "every selected sequence is either derivable or has a typed bound".

### 11.6 Testing owed by v6 — **IMPLEMENTED** (map every touched test; delete none)

The table below is what was owed; the "Where it landed" lines record the reality, including two
places the outcome differs from the plan.

| Area | Coverage |
|---|---|
| Handler / domain | a bulk create writes N rows in one transaction; a duplicate anywhere in the batch writes **none** and returns 409 naming the sequence; `ValidateExceptionOrdinal` runs per sequence |
| Integration (real Postgres) | posting `[1,3]` persists **two** rows with their own spans and reads back; re-posting the same batch is a **409** |
| Page (bUnit) | `Any date` → inputs shown; real part + period → inputs **absent** and **no** derived-span text (the silent rule, §11.2); real part **without** a period → inputs shown (the dead-end guard, §11.2); a multi-selection sends every chosen sequence |
| Regression | the v5 single-sequence path stays covered — a one-sequence batch is the same write |

**Where it landed:** handler — the new `CreateSubjectEnrollmentExceptionsHandlerTests` covers the
four bullets above (a valid first item does **not** survive a bad second; the ordinal is not part of
the duplicate key). Integration — three tests added to
`EnrollmentExceptionAvailabilityEndpointTests`, which already had the seed and POST helpers.
Page — the existing `EnrollmentExceptionsPageTests` suite was migrated rather than replaced
(14 assertions re-targeted, none deleted), and the multi-select assertion lives in
`PositionWithNoMatchingPeriod_LeavesTheRangeEmpty_AndTakesTheTypedDates` (**renamed in v7 to
`PositionsWithoutAPeriod_AreDisabled_AndTheRangeStaysHidden`**, §11.7).

**Two plan corrections, both recorded rather than quietly absorbed:**

1. **The intra-batch duplicate is a 409, never a `23505`.** The plan expected a same-span batch to
   collide on the duplicate key. It does not: the handler's in-memory check catches it first,
   because the database pre-check structurally cannot see rows the transaction has not saved. That
   is the better outcome — the same error vocabulary as every other duplicate — and it is asserted
   on real Postgres (`Bulk_IntraBatchDuplicateSpan_IsRejected409_WithNoPartialWrite`), including
   that the valid first item leaves no row behind.
2. **The list renders no ordinals** (§11.3), so the "contiguous run as a range" item in the plan
   above was dropped — there was nothing to render.

---

### 11.7 v7 — the span belongs to the free window; an unbacked position is disabled

> **Status: LOCKED and IMPLEMENTED (2026-09-30) — owner decision, Solo round.** v7 completes the v6
> direction. The owner's words: *"Position is showing as spec intended, but date range must hide
> when position shows."* The range therefore no longer depends on whether a period happens to hold
> the chosen position — it belongs to `Any date` alone.

**Decision 20 — the Position row and the range are mutually exclusive.** With a real part
(`Term` / `Semester`) chosen, the span section is removed ENTIRELY (still no derived-span text — v6
decision 17's silence, which v7 keeps). `Any date` keeps the editable range in every case.

| Situation | Span section |
|---|---|
| `Any date` (division `None`) | **Shown, editable** — the free window is the whole point |
| Any real part (`Term` / `Semester`), with or without a position ticked | **Removed entirely** — no pickers and no derived-span text |

**Decision 21 — an unbacked position is DISABLED, not hidden.** The ladder keeps its structural
SHAPE (Q5: 1st–4th, extended by any higher declared position) but a position the tenant has **no
period for** is rendered **disabled**. This is what makes decision 20 safe: every *selectable*
position is backed by a period, so every item in the batch has a derived span and `CanAdd` is
satisfied on the choice alone. v6's dead-end guard (§11.2's third row) is therefore **removed**, and
with it the Q1 write flow it protected — "record a 3rd term before the calendar is built" is no
longer reachable, an owner-accepted cost rather than an oversight.

**Consequences for the page (implemented):**

- `SpanIsDerived` is now simply `Division != AcademicYearDivision.None`.
- `PositionIsDerivable(position)` decides each checkbox's `Disabled` state.
- A real part with **no** position ticked is a **pre-write** state, not a write: the add section
  renders the note *"Choose a position to add an exception."* (a polite live region) rather than
  leaving a disabled button unexplained.
- `BuildItems()` is unchanged in shape; a real part with no position simply produces an unbounded
  item, which `CanAdd` rejects.

**Tests.** `PositionsWithoutAPeriod_AreDisabled_AndTheRangeStaysHidden` replaces v6's
`PositionWithNoMatchingPeriod_LeavesTheRangeEmpty_AndTakesTheTypedDates` (the reversed flow) and
keeps the multi-select coverage via the fixture's two backed positions; `OpenEnds_…`,
`ChosenPart_IsAControl_…` and `Positions_AreOneToFourPlusAnyHigherDeclared_…` were re-targeted to
the disabled/v7 shape.
