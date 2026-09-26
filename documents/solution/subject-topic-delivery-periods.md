# Subject / Topic delivery periods — feature reference & grade-surface review

> **Status (2026-09-26):** the grade surface is now period-aware, and the
> **Topics landing can both show and edit periods**. Items 1–4, 6 and 7 below are
> **landed and covered by bUnit tests**; item 5 remains open (and is a product
> decision, not a defect — see §6 Gap 5). Item 8 is a product question.
> Read this doc before touching the Subjects card or the Topics landing.
>
> **Folder:** `solution/` — this is technical memory (findings + decision +
> implementation record), not the source of truth for the feature. The governing
> requirements live in `specs/`; see [§2 Requirements provenance](#2-requirements-provenance).

---

## 1. What this feature is

A **subject** is displayed to users but is stored as a **Topic**. The rename
(`Subject` → `Topic`) was a domain-layer decision; the UI kept the word
"Subject" because that is what a school calls it. The topic catalog is
tenant-global; a subject only becomes real *for a school* when it is **assigned**
to a grade level (or, when that feature is on, to an activity group).

The assignment is the **bridge** — a `GradeTopicAssignment` row. This is the part
people get wrong: **the period lives on the bridge row, not on the topic.**

```
Topic  (catalog)            GradeTopicAssignment (bridge)          GradeLevel
  Id                         Id                                      Id
  CodedValueId ──1:n──▶      TopicId  ──n:1──▶  Topic                 Name
  Code                       GradeLevelId ──n:1──▶ GradeLevelId
  Name                       PeriodId  ◀── THE PERIOD LIVES HERE
  Description                [StartDate, EndDate?]   (date-based, decision 2a)
  DisplayOrder
```

**Consequence:** one topic can be assigned to the same grade **N times** — once
per term it runs in. "Mathematics in Term 1" and "Mathematics in Term 2" are two
bridge rows pointing at the same `Topic`. Any UI that keys by `TopicId` and
assumes one row is wrong.

### Vocabulary map

| Spec / code | UI label | Code symbol |
|---|---|---|
| `Topic` | Subject | `Topic`, `TopicDto` |
| `GradeSubjectAssignment` | the subject on this grade | `GradeTopicAssignment`, `TopicAssignmentDto` |
| `Period` | Delivery period | `Period`, `PeriodDto` |
| `ActivityGroup` | Activity Group | `ActivityGroup`, `ActivityGroupDto` |

Two route surfaces exist for the same handlers: `/students/topics` (canonical)
and `/students/subjects` (deprecated alias, `TopicRoutes.MapTopicRoutes`). The
client still calls the alias.

---

## 2. Requirements provenance

| Doc | Role |
|---|---|
| `specs/subject-to-topic-polymorphism.md` | Core spec. The Subject→Topic rename; **decision 2a: the grade↔topic bridge is date-based, not period-bound**. FR-21 requires the by-grade list endpoint to accept an optional `periodId` / `effectiveDate`. |
| `specs/activity-group-enrollment.md` **Rev. 6** | **FR-55…58** — adds the optional nullable `PeriodId` to the bridge. *This is the "period or whole academic year" feature.* Tracked in `subject-to-topic-polymorphism-impl.md` as "Rev. 6 bridge `PeriodId` (FR-55..58)", marked done. |
| `specs/ui-implementation-backlog.md` **Sprint 4 §4.2** | The UI work item, marked `[x]`. **The checkmark is inaccurate** — see [§6 Gap 4](#6-gap-4--createedit-asymmetry). |
| `specs/topic-codedvalue-override-plan.md` | Why topic name/code edits route through the coded-value override mechanism rather than editing the topic row. |

---

## 3. The period rules (canonical)

These are the invariants. Quote the FR id in code comments and PRs.

- **FR-55** — `GradeTopicAssignment.PeriodId` is **nullable**.
  `null` = year-spanning (the original date-based behaviour).
  Non-null = the subject is delivered in that specific term / semester /
  academic year.
- **FR-56** — **Group-owned** (`ActivityGroupId` set): `PeriodId`, when set,
  MUST match the group's `EnrollmentSpan` — `Termly`→`Term`,
  `Semester`→`Semester`, `WholeAcademicYear`→`AcademicYear`;
  `OpenEnded`/`DateRange`→**`PeriodId` must be `null`**.
- **FR-57** — **Grade-owned** (`GradeLevelId` set): `PeriodId`, when set, MUST
  be an `AcademicYear` or a `Term`/`Semester` **within the tenant's active
  academic year**. `null` = delivered year-spanning to all grade-enrolled
  students. Grade enrollment stays AcademicYear-level — a term-scoped subject
  gates **when** it is active, **not who** is enrolled.
- **FR-58** — **Assignment/subject consistency.** A `SelectedGrades` assignment's
  subject MUST be assigned for a period covering the assignment's effective date
  (a null-`PeriodId` year-spanning row active on that date, *or* a period-aligned
  row whose period contains it); otherwise the assignment is rejected. Mirrored
  for `SelectedGroups`. This refines recipient resolution (FR-20); it does not
  change it.

### Refinement agreed 2026-09-26 (product owner)

> A subject **can run in multiple terms**, or across an academic year, or be
> year-spanning (`PeriodId = null`) — **unless the period is archived or
> soft-deleted.**

Two consequences that the original FRs leave implicit:

1. **Multi-term is the normal case, not an edge case.** A single-value period
   editor is structurally incapable of representing it.
2. **Retired periods are not selectable.** `PeriodStatus` has
   `Draft | Active | Completed | Archived | Deactivated`; `Archived` and
   `Deactivated` must be filtered out of every period picker. `Draft`,
   `Active`, `Completed` stay selectable so an upcoming or just-closed term can
   still be corrected.

---

## 4. Surface map — who can see and edit a period

| Surface | Shows period? | Edits period? |
|---|---|---|
| Grade detail → **Subjects SectionCard** (`GradeLevels/Detail.razor`) | ✅ meta line: period name(s) + strand/lesson counts | ✅ kebab → **"Edit periods"** |
| Grade detail → **"View all subjects"** → `GradeTopicsDialog` | ✅ all period names, `·`-joined | ✅ "Edit periods" (same editor) |
| **Topics landing** (`/students/subjects`), grade owner | ✅ **"Delivery periods"** column | ✅ kebab → **"Edit periods"** |
| **Topics landing**, activity-group owner | ❌ | ❌ — see Gap 7, blocked on a missing API endpoint |
| Topics **create** dialog (`TopicCreateDialog`) | — | ✅ optional period, FR-56/57-filtered |
| Topics **edit** dialog (`SubjectEditDialog`) | ❌ (correct — the topic has no period) | ❌ |

The superseded single-assignment `TopicAssignmentPeriodEditDialog` was **deleted**;
`TopicPeriodsEditDialog` replaces it everywhere.

---

## 5. Decision: the period editor is *topic-scoped*

> **Amended by `specs/topic-edit-dialog-redesign.md` (2026-09-26, draft).** The
> reasoning below — a period is a SET of bridge rows, never a single value —
> **stands unchanged**. The *entry point* changes: the period editor is to become
> a **section of `TopicEditDialog`** (the grade-detail dialog), not a separate
> dialog one kebab away. That spec also resolves the coded-value dropdown problem
> (it currently allows repointing a subject at another subject's coded value) and
> states the period-status scoping rule. Read it before implementing anything
> here; this section is superseded on placement, not on shape.

**Decision:** period editing is a **set** (one row per delivery period for a
given grade+topic), not a single value.

**Why not add a period field to `TopicEditDialog`:** the period is on the bridge
row, and one topic may have several. A single-value picker there would be a false
affordance — it would have to silently drop terms. This also resolves the
create/edit asymmetry: the *edit* surface for a period is a dedicated
**assignment-level** editor, reachable from the same card that displays it,
rather than a field on the topic dialog.

**Shape** (`TopicPeriodsEditDialog`, landed 2026-09-26):

- One row per existing `GradeTopicAssignment` for this (grade, topic).
- Each row: a period select (`Whole academic year (no period)` + the active
  year's terms/semesters), and a remove control.
- "Add term" appends a row that becomes a **new** assignment on save.
- Removed saved rows are tombstoned (`Removed`) so the edit is undoable before
  submitting; a removed *unsaved* row is dropped outright.
- Client-side guard: the same period cannot be selected twice.
- Save applies, per row: remove → `DELETE /topic-assignments/{id}`;
  changed → `PUT /topic-assignments/{id}/period`; new →
  `POST /topic-assignments/grade` with `PeriodId`.
- Per-row failures are collected and surfaced; the server's own FR-57/FR-58 422
  message is shown rather than swallowed.

---

## 6. Review findings (2026-09-26, grade detail surface)

### Gap 1 — CRASH: a subject in two periods broke "View all subjects" ✅ **fixed**

`GradeLevels/Detail.razor` → `OpenTopicsDialogAsync`:

```csharp
_assignedTopics.ToDictionary(a => a.TopicId, a => new GradeTopicsDialog.AssignedTopicRef(...))
```

`ToDictionary` throws `ArgumentException` on duplicate keys. Two bridge rows for
the same topic — i.e. the *normal* multi-term case — **crashed the only surface
where periods are editable**. Not theoretical: it is the central scenario.

**Fix:** grouped by `TopicId` into `Dictionary<Guid, List<AssignedTopicRef>>`.
Guarded by `Detail_TopicsCard_SubjectRunningInTwoTerms_…_ViewAllDoesNotCrash`,
mutation-verified (reverting the fix fails the test).

### Gap 2 — the curriculum query ignores the period, so the card double-counted ✅ **fixed client-side**

`ListGradeTopicCurriculumByGradeHandler` filters **by date only**
(`StartDate <= effectiveDate && (EndDate == null || EndDate >= effectiveDate)`) —
no period predicate. A subject in two terms yielded **two identical rows**, and
`Count="@(_curriculum?.Length ?? 0)"` reported 2 for one subject.

**Fix:** `Detail.razor` dedupes `_curriculum` by `TopicId` on both load paths.
The **query** is still period-blind — a server-side `periodId` predicate is
still worth adding if the landing ever needs per-term filtering (item 6).

### Gap 3 — the SectionCard could not show or edit the period ✅ **fixed**

- `GradeTopicCurriculumDto` has **no `PeriodId` field** — the card literally
  cannot represent it.
- `ItemMetaSelector` shows only `"{n} strands"` / `"{n} lessons"`.
- `BuildTopicRowActions` is `Strands` + `Remove` — **no "Edit periods"**, even
  though `EditAssignmentPeriodAsync` is wired one dialog over.

So on the primary surface, delivery period was invisible and uneditable.

**Fix:** the card resolves periods from `_assignedTopics` (which already carries
`PeriodId` + `AssignmentId`) joined against a `_periodNames` map loaded from
`ListPeriodsAsync`. `ItemMetaSelector="SubjectMeta"` leads with the period
name(s); the kebab gained **"Edit periods"** (replacing the deleted
`EditAssignmentPeriodAsync`).

### Gap 4 — create/edit asymmetry

| | `TopicCreateDialog` | `TopicEditDialog` |
|---|---|---|
| Owner type / id | ✅ | ❌ |
| Period | ✅ optional, FR-56/57-filtered | ❌ |
| Coded value, Name/Code/Description/DisplayOrder | ✅ | ✅ |
| Strands | card kebab | ✅ inline |

Structurally correct (the period is not a topic field) but UX-inconsistent:
"Add subject" offers a period, "Edit subject" does not, and the only place to
change it is behind "View all subjects" → kebab. `ui-implementation-backlog.md`
Sprint 4 §4.2 is checked `[x]` for **both** Create and Edit — inaccurate for Edit.

### Gap 5 — the quick-assign path defaults to year-spanning (reframed: a product choice, not a defect)

`AssignTopicAsync` (add an *existing* subject to a grade) calls
`new AssignGradeTopicRequest(Id, topicId, DateOnly.FromDateTime(DateTime.UtcNow))`
— `PeriodId` defaults to `null`, i.e. **year-spanning**.

This is a legitimate state under FR-55, not a lost value: `null` means "runs all
year". So the only real objection is that it is **silent**, and that adding a
*new* subject offers a period picker while adding an *existing* one does not.
Now that "Edit periods" is one kebab click away on both the grade card and the
Topics landing, the follow-up path exists, which makes the default defensible.
**Decision needed:** keep the year-spanning default, or add a period picker to
quick-assign. Five call sites share this pattern — `GradeLevels/Detail.razor`,
`GradeLevels/Create.razor`, `GradeLevels/Edit.razor`, `GradeLevelCreateDialog`,
`GradeLevelEditDialog`.

### Gap 6 — `PeriodLabel` showed a raw GUID prefix ✅ **fixed**

`GradeTopicsDialog.PeriodLabel` returned `pid.ToString()[..8]` — `a3f9c1d2`, not
"Term 1". Unusable as a label. Now resolves through the page's `PeriodNames` map
and joins multiple terms with `·`.

### Gap 7 — the Topics landing had no period column, filter UI, **or any way to edit a period** ✅ **fixed (grade owner); group owner blocked**

The original framing of this gap was display-only ("no period column"), which
understated it: the landing is **owner-scoped** (the user picks a GradeLevel or
Activity Group), so each row *is* a (grade, topic) pair — exactly the bridge
scope — yet it offered **no route to period editing at all**. The subject edit
dialog there is `SubjectEditDialog`, a 131-line rename-only surface
(`Code` + `Name` → `CodedValueDto`) with no owner, no grade and no period. Adding
a period field there would be wrong (§5); the correct answer was a row action.

**Fixed:** a **"Delivery periods"** column and an **"Edit periods"** kebab action
that reuses `TopicPeriodsEditDialog` verbatim — no dialog changes were needed. The
landing now loads the owner-scoped bridge rows
(`ListGradeTopicsByGradeAsync` → `TopicAssignmentDto`, which carries `AssignmentId`
+ `PeriodId`) and a `_periodNames` map, because `_items` are `SubjectDto` and carry
neither (pitfalls 1 and 3).

The `?periodId=` query param still has no filter **UI**; it remains available for
deep links. That part of the original gap is still open.

**Group owner remains blocked**, and deliberately shows nothing rather than a dead
control: there is a **write** (`AssignActivityGroupTopicAsync`) but **no list
endpoint** for a group's topic assignments, so a group's existing periods cannot
be read at all. Closing this needs `GET /activity-groups/{id}/topic-assignments`
in `SchoolCollab.Students.Api` (flag-gated like the rest of the group routes).
Note FR-56's group period filter differs from FR-57's grade filter
(`TopicCreateDialog.FilterPeriodsForGroup` matches the group's `EnrollmentSpan`,
vs `FilterPeriodsForGrade`'s active-year rule) — so `TopicPeriodsEditDialog`, which
hardcodes the grade filter and `AssignGradeTopicAsync`, would need generalizing
too, not just a new endpoint.

### Non-gap — period scope is active-year-only (by design)

`FilterPeriodsForGrade()` and the period editor both restrict to the active
academic year, which is exactly what FR-57 requires. Consequence: next year's
terms cannot be planned in advance. This is spec-compliant but a **product
limitation worth raising**, not a bug.

---

## 7. Work log

Items 1–4 and 7 are landed; each is locked in by a bUnit test that was
mutation-verified (the test fails when the fix is reverted).

| # | Item | Touches | Status |
|---|---|---|---|
| 1 | **Fixed the `ToDictionary` crash** (Gap 1) — grouped by `TopicId` into `Dictionary<Guid, List<AssignedTopicRef>>`. | `Detail.razor`, `GradeTopicsDialog.razor` | ✅ done |
| 2 | **Card period awareness** (Gaps 2/3) — `_curriculum` deduped by `TopicId`; period chip in the meta line (names, not GUIDs); **"Edit periods"** row action; `_periodNames` map loaded on the page. | `Detail.razor` | ✅ done |
| 3 | **`GradeTopicsDialog`** — all period names `·`-joined; delegates to the topic-scoped editor (Gap 6). | `GradeTopicsDialog.razor` | ✅ done |
| 4 | **Retired `TopicAssignmentPeriodEditDialog`** (also carried the `Error="Error"` literal-string bug). | — | ✅ done |
| 5 | Quick-assign period support (Gap 5) — `AssignTopicAsync` still sends `PeriodId = null`. Reframed: a **product decision**, not a defect. | `GradeLevels/*` | ⬜ open — decision |
| 6 | **Topics landing: "Delivery periods" column + "Edit periods" action** (Gap 7), grade owner. Reuses `TopicPeriodsEditDialog` unchanged. Group owner blocked on a missing list endpoint. | `Subjects.razor` | ✅ done (grade) / ⬜ open (group) |
| 7 | bUnit tests. | `tests/SchoolCollab.Admin.Tests.Unit/` | ✅ done |
| 8 | Product decision: is active-year-only period scope acceptable (see §3)? | — | ⬜ open — decision |
| 9 | `?periodId=` filter **UI** on the Topics landing (the param works for deep links; nothing sets it). | `Subjects.razor` | ⬜ open |

### Tests added

| Test | Guards |
|---|---|
| `GradeLevelDetailPageTests.Detail_TopicsCard_SubjectRunningInTwoTerms_ShowsBothPeriods_AndViewAllDoesNotCrash` | The crash (Gap 1), the dedupe, the period-name chip, the "Edit periods" action |
| `TopicPeriodsEditDialogTests.Dialog_OffersActiveYearAndTerms_ButNeverArchivedPeriods` | Retired periods are not selectable |
| `TopicPeriodsEditDialogTests.Dialog_SubjectRunningInTwoTerms_RendersAndKeepsBothPeriods` | Multi-term round-trip; no-op save writes nothing |
| `TopicPeriodsEditDialogTests.Dialog_YearSpanningSubject_ShowsTheEmptyPeriodSelection` | `null` = whole academic year |
| `TopicPeriodsEditDialogTests.Dialog_AllRowsRemoved_IsRejected` | Cannot remove a subject from a grade by accident |
| `TopicPeriodsEditDialogTests.Dialog_DuplicatePeriod_IsRejectedWithoutCallingTheApi` | Client-side guard, no write attempted |
| `TopicPeriodsEditDialogTests.Dialog_UsesExpressionBoundErrorSoValidationMessagesSurface` | The `Error="Error"` literal-string trap (pitfall 5) |
| `SubjectsLandingPageTests.GradeOwner_SubjectInTwoTerms_ShowsBothPeriodNamesInTheColumn` | Both period **names** render, not GUID prefixes |
| `SubjectsLandingPageTests.GradeOwner_RowOffersEditPeriodsAction` | Period editing is reachable from the landing |
| `SubjectsLandingPageTests.NoBridgeRows_RowOmitsEditPeriodsRatherThanOfferingADeadControl` | Gate the control, not just the call (pitfall 9) |
| `SubjectsLandingPageTests.EditPeriods_OpensEditorWithOneRowPerBridgeRow` | **One row per bridge row** — mutation-verified: reverting to `Take(1)` fails it |
| `SubjectsLandingPageTests.DeliveryPeriodLoadFails_TopicsStillLoadAndColumnFallsBack` | The period load is best-effort and never fails the list |

---

## 8. Pitfalls for anyone touching this next

1. **The period is on the bridge, not the topic.** Never add `PeriodId` to
   `Topic` or to a "subject" DTO that describes the catalog entity.
2. **Never key a grade's subject collection by `TopicId`.** It is not unique —
   `ToDictionary` will throw. Key by `AssignmentId`, or group by `TopicId` into
   a list.
3. **`GradeTopicCurriculumDto` has no `PeriodId`.** The card's data source is
   period-blind; the period data for a card row must come from
   `ListGradeTopicsByGradeAsync` → `TopicAssignmentDto.PeriodId`.
4. **`ListGradeTopicCurriculumByGrade*` does not filter by period** and will
   return duplicate rows per topic. Dedupe client-side or add a period predicate.
5. **Razor gotcha — `Error="Error"` is a literal string.** `DialogShellFooter`'s
   `Error` is a `string` parameter, so `Error="Error"` passes the *word* "Error"
   and the real message never renders. Always `Error="@Error"`. Fixed in
   `JoinGroupsDialog`; fixed by deletion in `TopicAssignmentPeriodEditDialog`.
   Still unfixed in `ActivityGroupCreateDialog`, `ActivityGroupEditDialog` and
   `NotificationPolicyFieldEditDialog` — audit with
   `grep -rn -A4 '<DialogShellFooter' --include=*.razor src | grep -E 'Error='`.
6. **`FluentSelect` in v4.14.2 binds `Value`/`ValueChanged` over `string`**, not
   over `TOption`, and Razor will not coerce an inline lambda to
   `EventCallback<string>`. Bind list elements through
   `EventCallback.Factory.Create<string>(this, …)`. It also does **not**
   materialise its option children while closed — assert on the bound `Items`,
   not on markup.
7. **A period picker must exclude `Archived` / `Deactivated`.** A subject cannot
   be delivered into a period that is no longer live.
8. **The create dialog's period options are owner-dependent** —
   `FilterPeriodsForGrade()` (active year + its sub-periods) vs
   `FilterPeriodsForGroup()` (matched to the group's `EnrollmentSpan`). Do not
   reuse one for the other. `TopicPeriodsEditDialog` hardcodes the grade rule and
   `AssignGradeTopicAsync`, so generalizing it for a group owner is real work, not
   a one-line change.
9. **`FluentMenu` does not materialise its items while closed** — the same trap
   as pitfall 6 for `FluentSelect`. A `RowActionsMenu` kebab renders as a bare
   `<fluent-button>`; the labels are absent from the markup until it is opened. To
   test a row action, read the page's own delegate instead:
   `cut.FindComponent<LandingPage<T>>().Instance.RowActions!(item)`. `RowAction.OnClick`
   is public, so invoking it exercises the real handler.
10. **Invoke an action through `cut.InvokeAsync(...)`, not directly.** A row-action
    handler usually ends in `StateHasChanged`, which asserts dispatcher access; a
   direct call from the test thread throws *"The current thread is not associated
   with the Dispatcher"*.
11. **A `Dictionary<Guid, List<T>>` shape is a structural guard.** Because
    `_assignmentsByTopic` is a list-valued map, keying it wrongly does not compile
    (it fails the "silently drop a term" test too). Prefer that shape over a
    dictionary plus a separate count.
12. **Gate the control, not just the call.** With no bridge rows loaded there is no
    period set to edit, so "Edit periods" is not rendered at all. Adding the
   action unconditionally would have replaced "no affordance" with a dead control.
