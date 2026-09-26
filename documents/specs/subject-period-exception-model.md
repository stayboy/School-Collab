# Spec: Subject Period Exceptions (decouple subject availability from the period lifecycle)

> **Status:** Draft v1 — **§8 Q1 answered 2026-09-26: blocks are for real
> exceptions only, no hybrid (see §0).** Nothing implemented.
> **Owner:** Students context (`SchoolCollab.Students.Core` / `.Api` / `.Application`)
> **Cross-context impact:** `SchoolCollab.Assignments.Core` — FR-58 and
> `ITopicAssignmentLookup`
> **Supersedes:** the `GradeTopicAssignment.PeriodId` whitelist semantics from
> `activity-group-enrollment.md` **Rev. 6 FR-55…57** (FR-58 is *refined*, see §4.3)
> **Supersedes:** §4 of `topic-edit-dialog-redesign.md` (the dialog's period section)
> **Supersedes:** `documents/solution/subject-topic-delivery-periods.md` §3
> **Relates to:** `period-hierarchy-terms-semesters.md`, `active-period-per-tenancy.md`
> **Sibling spec (independent — do not conflate):**
> `subject-enrollment-requirement-flag.md`. That spec's `RequiresEnrollment`
> boolean decides *who* may receive work for a subject; this spec decides *when* a
> subject is offered. The two share the `TopicAssignment` bridge but are
> orthogonal: this spec removes `PeriodId` from it, the sibling adds
> `RequiresEnrollment` to it.

---

## 0. Decision locked in this revision

1. **Blocks are for genuine exceptions only** — staffing, curriculum clash, a
   teacher on leave. There is **no hybrid** and no scheduling whitelist. A subject
   is year-spanning on a grade by default; a `SubjectEnrollmentBlock` row blocks
   it for one concrete period.
2. The accepted cost: **"subject offered only in Term 1" is not expressible
   cheaply.** It would need `N-1` blocks per year, and a missing block fails
   *silently* (the subject appears where it should not, compounding each year)
   rather than loudly (the subject disappears, which is visible). This case is
   explicitly out of scope for v1. §8 Q1 below is retained as the reasoning trail;
   the open question is closed.

## 1. The problem being solved

Today a subject's availability on a grade is expressed as a **whitelist**: the
bridge row `GradeTopicAssignment` carries a nullable `PeriodId`, and a non-null
value means *"this subject is delivered only in that period"*.

That ties a durable relationship — *this subject belongs to this grade* — to the
lifecycle of a **specific period instance**. Periods have a natural end
(`Completed` → `Archived`), and academic years roll over. So the whitelist
produces exactly the failure the owner reported:

> *"closing and opening periods can disable subjects."*

The mechanism, concretely:

1. Mathematics is assigned to Grade 5 with `PeriodId = 2026 Term 1`.
2. The tenant rolls to the 2027 academic year. The row still points at **2026
   Term 1** — a period that is now `Completed`, then `Archived`.
3. Availability is resolved against the **active** period. `EnrollStudentHandler`
   hard-requires `command.PeriodId == activePeriod.Id`
   (`EnrollStudentHandler.cs:55`), and `ITopicAssignmentLookup` resolves the
   whitelist as *"a null-`PeriodId` year-spanning assignment effective on the
   date, **or** a period-aligned assignment whose period contains the date"*.
   2026 Term 1 does not contain a 2027 date, and it is not null — so the
   subject resolves as **not available**.
4. The subject silently disappears from the grade's curriculum and from
   assignment subject pickers.

**And it is not repairable through the UI.** The period picker excludes
`Archived` and `Deactivated` (a subject cannot be delivered into a dead period),
so once 2026 Term 1 is archived the row's period cannot be re-selected — the
assignment is stranded on a value the UI can no longer offer. The user must
remove and re-add the subject every year.

This is not an edge case: it is the *normal* consequence of using a
period-instance whitelist for a relationship that is meant to be permanent.

---

## 2. The proposed model — default-on, explicit blocks

Invert the default.

| | Today (whitelist) | Proposed (exceptions) |
|---|---|---|
| Subject on Grade 5 | one bridge row, `PeriodId = <a period>` or `null` | one bridge row, **no period at all** |
| "Not offered in Term 3" | pin to Term 3, then re-pin every year | one **block** row for Term 3 |
| Year rollover | row goes stale → subject vanishes | nothing to do; blocks are historical |
| Period archived | subject pinned to it becomes unselectable and stranded | inert — the block is just history |
| Availability query | two-way: "a null row active on the date **or** a period-aligned row whose period contains it" | two-step: "assignment exists **and** no block covers the period" |

The new entity is a **block**, not a schedule:

```csharp
/// A subject is blocked from enrollment/assignment for ONE specific period.
/// The absence of a block is the normal, expected state.
public sealed class SubjectEnrollmentBlock : BaseTenantEntity
{
    public Guid Id { get; private set; }

    /// <summary>Grade-owned block. Mutually exclusive with <see cref="ActivityGroupId"/>.</summary>
    public Guid? GradeLevelId { get; private set; }

    /// <summary>Group-owned block. Mutually exclusive with <see cref="GradeLevelId"/>.</summary>
    public Guid? ActivityGroupId { get; private set; }

    public Guid TopicId { get; private set; }

    /// <summary>
    /// REQUIRED. A block with no period is meaningless — "no period" *is* the
    /// default (available). This non-nullability is the whole point of the
    /// inversion: the nullable whitelist case has no analogue here.
    /// </summary>
    public Guid PeriodId { get; private set; }

    /// <summary>Optional free text, e.g. "teacher on leave". Never load-bearing.</summary>
    public string? Reason { get; private set; }
}
```

### Invariants

- **Exactly one** of `GradeLevelId` / `ActivityGroupId` is set (mirrors the
  existing bridge rule).
- **`PeriodId` is non-nullable** and must be a term, semester, or academic year
  belonging to the tenant's period hierarchy.
- Unique index on `(GradeLevelId, TopicId, PeriodId)` and on
  `(ActivityGroupId, TopicId, PeriodId)`; filtered so a null owner does not
  collide. Duplicate blocks are a 409, not a silent second row.
- A block references a **submittable** period (`Active` / `Completed` /
  `Draft` — never `Archived`/`Deactivated`, and never `Draft`-deletable). Since
  period deletion is Draft-only and a block can never point at a Draft-only-
  deletable period without a status change, **`ON DELETE RESTRICT` is
  effectively guaranteed** — a block cannot be orphaned by deleting its period.
- **No status/lifecycle column.** A block is immutable history. Un-blocking
  deletes the row (audit via the soft-delete/`xmin` convention already in the
  codebase). Adding a status would recreate the coupling this spec exists to
  remove.
- A block whose period is later archived is **inert, never invalid** — it stays
  visible for reporting and remains deletable.

### Availability semantics (the single source of truth)

```
SubjectAvailable(gradeLevelId, topicId, periodId) =
      EXISTS  bridge(gradeLevelId, topicId)  effective on the period's date range
  AND NOT EXISTS  block(gradeLevelId, topicId, periodId)
```

`periodId` is always the **active** period, which `EnrollStudent` already
enforces. So the date-vs-period fuzziness in today's FR-58 resolution
disappears: the check is an exact id match, not a range-containment test.

---

## 3. What this fixes

1. **Year rollover is a no-op.** No period is attached to the subject↔grade
   link, so nothing goes stale.
2. **Period lifecycle becomes inert.** Archiving or deactivating a term does not
   change any subject's availability. The subject simply stops being *offered
   for that term* because that term is over — which is what closing a term is
   supposed to mean.
3. **The stranded-row bug is gone.** A block can always be deleted, and the
   bridge row has no period to re-point.
4. **FR-58 gets strictly simpler** (§4.3).
5. **The dialog section gets much simpler** — see §5. Three validation rules from
   `topic-edit-dialog-redesign.md` disappear entirely.

---

## 4. Impact on existing FRs

### 4.1 FR-55 — superseded

> ~~`GradeTopicAssignment.PeriodId` is nullable; null = year-spanning, non-null
> = delivered in that period.~~

`PeriodId` no longer carries meaning. **Decision needed:** keep the column
(readable, zero-risk) or drop it in a follow-up migration once §8 Q2 is settled.
Recommend **keep and ignore** — dropping a column is not worth the migration risk
and the column is still a useful "what this was pinned to" forensic record.

### 4.2 FR-56 / FR-57 — superseded by the block's own validation

- **FR-56 (group-owned):** a group's block's period must match the group's
  `EnrollmentSpan` — `Termly`→`Term`, `Semester`→`Semester`,
  `WholeAcademicYear`→`AcademicYear`; `OpenEnded`/`DateRange` cannot be blocked
  at all (they have no period to block). Reuses
  `TopicCreateDialog.FilterPeriodsForGroup`.
- **FR-57 (grade-owned):** a grade block's period must be an academic year or a
  term/semester **within the tenant's active academic year**. Reuses
  `FilterPeriodsForGrade`.
- **Retired-period exclusion** now lives on the *block* rather than on the
  assignment: `Archived` and `Deactivated` periods cannot be blocked. `Draft`,
  `Active`, `Completed` can — so an upcoming term can be blocked in advance and a
  just-closed term can still have its block corrected.

### 4.3 FR-58 — refined, and it gets simpler

Today, `ITopicAssignmentLookup.IsTopicAssignedAsync` resolves a two-way
condition (null-row-active-on-date **or** period-aligned-contains-date). Under
this model it becomes:

```
IsTopicAssignedAsync(gradeLevelId | activityGroupIds, topicId, effectiveDate)
  → resolve effectiveDate to the active period
  → return bridgeExists(grade, topic)
     && !blockExists(grade, topic, activePeriodId)
```

The **contract signature is unchanged** — no cross-context breakage, only a
simpler implementation on the Students side. That is a deliberate design goal:
`Assignments.Core` does not change at all.

---

## 5. Effect on the dialog

> **Note (2026-09-26):** a second, independent concern joins this surface. The
> `RequiresEnrollment` boolean (`subject-enrollment-requirement-flag.md`) also
> lands on the same Subjects card and the same edit dialog, and its toggle
> carries a warning + bulk-enrol flow. See that spec §8 (FR-ER-25/FR-ER-27) for
> how the two coexist on one surface.

`TopicEditDialog`'s **"Delivery periods"** section (`topic-edit-dialog-redesign.md`
§4) becomes **"Enrollment exceptions"**: a list of blocked periods for this
grade, each deletable, with an add-affordance.

Rules that **disappear** (they existed only because a whitelist needs them):

- FR-ED-10 (a year-spanning row cannot coexist with period rows) — there is no
  year-spanning row any more.
- FR-ED-13 (duplicate periods rejected) — a unique index forbids it, and the UI
  greys out already-blocked periods instead.
- FR-ED-14 (the set may not be emptied) — an **empty list is the normal state**.
  You can have zero exceptions. This is the single biggest UX simplification.
- FR-ED-9 (two meanings of "a year") — a block is always one concrete period.
  Blocking a whole academic year is a block on the academic-year period id.

One new rule, and it is a real trap:

- **Blocks on retired periods stay visible and deletable.** The *picker* excludes
  `Archived`/`Deactivated` (you cannot block a dead period), but an existing
  block whose period has since been archived must still render — read-only, with
  a "this period is archived" marker and a delete control. Otherwise the tenant
  accumulates uncleanable blocks and the section becomes a dead end.

The shared-editor extraction (`TopicPeriodsEditor`) proposed in
`topic-edit-dialog-redesign.md` §8 Q1 is **withdrawn** — there is no longer a
standalone period dialog to share with, because a block is small enough to edit
inline.

---

## 6. API surface

New, in `TopicAssignmentRoutes` (Students API, grouped per
`endpoint-organization-pattern.md`):

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/students/subject-blocks?gradeLevelId={id}` | list blocks for a grade (or `?activityGroupId=`) |
| `GET` | `/students/subject-blocks?gradeLevelId={id}&periodId={id}` | is this subject blocked in this period (for pickers) |
| `POST` | `/students/subject-blocks` | create a block |
| `DELETE` | `/students/subject-blocks/{id}` | remove a block (idempotent) |

**No feature flag.** This replaces shipped behaviour rather than adding an
opt-in surface, and it touches enrollment correctness — a flag would mean two
divergent availability rules. (A flag *is* warranted for the §7 migration if a
tenant has live whitelist rows; see §8 Q2.)

`PUT /topic-assignments/{id}/period` and the `PeriodId` parameter on
`AssignGradeTopicRequest` become **no-ops** and should be deprecated, not
removed, in the same PR.

---

## 7. Migration

**Step 0 — measure first (cheap, and it sizes the whole job):**

```sql
SELECT grade_level_id, count(*) FROM grade_topic_assignments
WHERE period_id IS NOT NULL GROUP BY grade_level_id;
```

**If this returns zero rows**, the whitelist was never used: there is no data to
convert, no flag is needed, and the migration is purely additive (new table).

**If it returns rows**, each whitelist row `P` must be converted:

1. Set that assignment's `PeriodId = NULL` → the subject becomes year-spanning.
2. For every **sibling** period in the same academic year (i.e. every live
   term/semester that is not `P`, plus the academic year itself if the whitelist
   previously permitted it), insert a `SubjectEnrollmentBlock` row.

The expansion in step 2 is mechanical but **must be reviewed row-by-row before
applying** — it is the difference between "only Term 1" and "everything except
Term 1", and getting it wrong silently widens or narrows what a subject is
offered for. Generate the script, review it, then apply behind
`FEATURE:UseSubjectPeriodExceptions` so it can be rolled back per tenant.

---

## 8. Open questions — decisions needed before implementation

**Q1 — Does the school need *scheduling* semantics, or only *exceptions*?**

This is the pivotal question and it decides whether this model is a clean win or
a trap.

| Need | Under blocks |
|---|---|
| "Mathematics is **blocked** in Term 3 because the teacher is on leave" | ✅ one row, correct forever |
| "Mathematics is **not offered at all** in Term 3" | ⚠️ also one row — reads as a temporary exception but means "never" |
| "Mathematics is offered **only in Term 1**" | ❌ **requires a block for every other term, every year** |

The last row is the cost. With a whitelist it is a single row; with blocks it is
`N-1` rows **per academic year**, and a block for 2026 Term 2 does not cover
2027 Term 2. The failure mode is also worse in kind:

- A stale **whitelist** row makes a subject **disappear** — loud, visible, and
  safe (one re-add per year).
- A missing **block** makes a subject **appear where it should not be** — silent,
  invisible, and it compounds every year. "Math suddenly shows up in Term 2 of
  2027" is a data-integrity problem, not a cosmetic one.

**Recommendation:** adopt the block model **if and only if** subjects are
year-spanning by default and blocking is genuinely exceptional — which is the
common case, and is exactly the case the owner described. If the school needs
"Term 1 only" as a *standing schedule*, the answer is a **hybrid**: keep a
positive delivery-schedule concept *and* add blocks for exceptions. That keeps
the loud failure mode but is two mechanisms instead of one, and this spec would
need §2 rewritten.

> **✅ ANSWERED (2026-09-26) — block model adopted, no hybrid.** The owner
> confirmed blocking is for real exceptions spanning a period, term or semester,
> and accepted the "Term 1 only" limitation above. Locked as §0 decision 1. The
> text above is retained as the reasoning trail; the question is closed.

**Q2 — Keep or drop `GradeTopicAssignment.PeriodId`?** Recommend keep-and-ignore
(§4.1). Dropping it is a second migration for no functional gain.

**Q3 — Should blocks be soft-deleted or hard-deleted?** Recommend soft-delete for
audit ("who blocked Math in Term 3, and when?") consistent with
`BaseTenantEntity`. A hard delete loses the reason a subject was unavailable.

**Q4 — Is a `Reason` field wanted?** `SubjectEnrollmentBlock.Reason` is specified
as optional free text ("teacher on leave"). If the school wants structured
reasons (staffing, curriculum, timetable clash) that is a coded-value parent and
a bigger change — say so now.

---

## 9. Testing requirements

Per `.github/copilot/rules/testing.md` (MSTest + Moq + FluentAssertions, MTP).
`dotnet-best-practices.md` and `ef-migrations.md` apply to the entity + migration.

**Domain / handler unit tests**

- Availability: bridge exists + no block ⇒ available. Bridge exists + block on
  the active period ⇒ **not** available. No bridge ⇒ not available regardless of
  blocks.
- **The regression that motivates this spec:** an assignment with **no**
  `PeriodId` and **no** block stays available after the academic year rolls over
  and the prior year's terms are archived. This must be a test, or the original
  bug returns.
- Archiving a period that has a block on it leaves the block intact and does not
  change availability of anything else.
- FR-56 group span validation: a `Termly` group accepts a `Term` block and
  rejects a `Semester` one; `OpenEnded`/`DateRange` groups accept none.
- FR-57 grade validation: a block outside the active academic year is rejected.
- Retired periods cannot be blocked; `Draft`/`Active`/`Completed` can.
- Duplicate `(grade, topic, period)` ⇒ conflict, not a second row.

**FR-58 (`Assignments.Core`)** — the port signature is unchanged, so assert the
behaviour only: an assignment whose subject is blocked for the active period is
**rejected at publish** with the server's own message, not swallowed.

**bUnit (dialog)** — the "Enrollment exceptions" section: empty state is the
normal state and must not show a warning; adding a block for an already-blocked
period is impossible; removing the last block succeeds (no "keep at least one"
rule); a block on an archived period renders read-only and remains deletable.

**Integration** — the §7 conversion script against a fixture containing a
whitelist row in two terms, asserting the subject is available in neither
*other* term and *is* available in the original two.

---

## 10. Out of scope

- The `SubjectEditDialog` on the Topics landing (see
  `topic-edit-dialog-redesign.md` §8 Q3).
- Strands and lessons — unchanged, per that spec §9.
- `documents/configuration.md` — **no** flag is added under the recommendation in
  §7; only the migration path in §7 Q2 may warrant one.
- Activity-group period rules beyond what FR-56 already states.
