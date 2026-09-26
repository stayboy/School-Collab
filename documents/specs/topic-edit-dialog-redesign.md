# Spec: Grade-Detail Edit Topic Dialog Redesign

> **Status:** Draft v1 — decisions locked 2026-09-26. **§3 (the coded value) and
> §5 (layout) stand. §4 is superseded by two later specs**:
> `subject-period-exception-model.md` (the delivery-period whitelist is replaced by
> a block/exception model) and `subject-enrollment-requirement-flag.md` (a
> `RequiresEnrollment` boolean also lands on this dialog). Nothing implemented.
> **Owner:** Students context — `TopicEditDialog` + its call site
> (`GradeLevels/Detail.razor` `OpenTopicEditAsync`)
> **Depends on:** `subject-to-topic-polymorphism.md` (the `Topic` ↔
> `GradeTopicAssignment` bridge, decision 2a), `activity-group-enrollment.md`
> **Rev. 6** (FR-55…58 — the nullable bridge `PeriodId`),
> `topic-codedvalue-override-plan.md` (the override/provisional-coded-value
> mechanism), `period-hierarchy-terms-semesters.md`,
> `strand-lesson-unification-plan.md` (lessons are parented strands)
> **Relates to:** `documents/solution/subject-topic-delivery-periods.md` — this
> spec **changes that doc's §5 "the period editor is topic-scoped" decision** from
> *a separate dialog* to *a section of the edit dialog*. See §8 Q1.
> **Branch:** `feat/topic-edit-dialog-redesign` (suggested)
> **Skill:** `dialog-ui`, `fluentui-dialog-shell`, `blazor-css-isolation`

> **One dialog, one Save.** Everything a user can change about a subject *on this
> grade* — its identity, its override, and its delivery periods — is edited in a
> single dialog with a single primary action. The period set is not a separate
> dialog reachable from a kebab; it is a section of the edit dialog, because the
> grade detail page is the only place where "which periods is this subject
> delivered in **for this grade**" is even a well-formed question.

---

## 0. Decisions locked in this revision

1. **The period set is a section of `TopicEditDialog`, not a second dialog.**
   The dialog gains a **"Delivery periods"** section. `TopicPeriodsEditDialog`
   remains as a *thin wrapper over the same shared editor component* so the two
   existing "Edit periods" kebab entry points keep working (see §8 Q1 for the
   alternative — deleting the standalone dialog outright).
2. **A period set is a SET, one row per bridge row, never a single value.** A
   subject can run in several terms, several semesters, one year, or
   year-spanning. This is unchanged from
   `subject-topic-delivery-periods.md` §5 and from FR-55: the period lives on
   `GradeTopicAssignment`, so N periods ⇒ N bridge rows for the same
   `(grade, topic)` pair.
3. **The coded-value dropdown is no longer a picker.** It is replaced by a
   read-only display of the currently resolved value plus two explicit
   affordances — **Edit override** and **Create override**. A topic may no
   longer be repointed at a *different existing* subject coded value from this
   dialog.
4. **Selectable periods exclude `Archived` and `Deactivated`.** `Draft`,
   `Active` and `Completed` remain selectable. See §8 Q2 — a strict
   `Status == Active` reading is available but has a real cost.
5. **Strands and lessons are untouched.** The existing `<StrandsEditor>` block
   stays exactly as it is. It is explicitly out of scope (§9).
6. **Periods are applied before fields.** Period writes are the ones the server
   can reject (FR-57/FR-58 `422`); field writes are local and effectively cannot
   fail. Saving periods first means a rejected period cannot leave a
   half-renamed subject. See §6.3 for the accepted inverse case.

---

## 1. Context and the problem

`TopicEditDialog` is opened from the grade-detail Subjects card — the topic name
is the primary affordance (`ItemOnClick="t => OpenTopicEditAsync(t)"`) and the
kebab reaches it too. It is the most natural place a user goes to answer *"how do
I change this subject?"*

It currently answers only half of that:

| A user on the grade detail page wants to… | Today |
|---|---|
| Rename the subject | ✅ inline "Edit fields" → display mode |
| Change the subject's code | ✅ (routes through the coded-value override) |
| See **which terms the subject runs in** | ❌ nowhere in this dialog |
| Change **which terms it runs in** | ❌ one kebab click away, in a *different* dialog |
| Repoint the subject at a different coded value | ✅ **allowed** — and that is the bug |

The last row is the problem the owner flagged. The dropdown is enabled and
searchable, so a user can point a subject at *another* subject's coded value. On
save `TopicCodedValueSaver` / `TopicEditRouter` writes an override or a new
provisional value, and `UpdateTopicAsync` re-points `Topic.CodedValueId`. The
result is a subject that silently becomes a different subject, or two subjects
sharing one coded value — a data-integrity footgun with no confirmation and no
reversal path. **Editing an existing subject's identity must not be a
dropdown selection; it must be a deliberate override.**

Meanwhile the thing the user actually came to check on a *grade* page — the
delivery periods — is in a separate dialog, one kebab over, and invisible from
the dialog whose whole reason for existing is "edit this subject on this grade".

---

## 2. Scope

**In scope**

- The `Subject` row of `TopicEditDialog`: read-only resolved value + **Edit
  override** + **Create override**. No new selection.
- A new **Delivery periods** section: the multi-period set editor, scoped to the
  active academic year, excluding retired periods.
- The save sequence across both (§6).
- The grade context the period section needs, and the hide-when-absent rule.
- Extraction of the period set editor into a component shared with
  `TopicPeriodsEditDialog`.

**Out of scope**

- **Strands and lessons.** The `<StrandsEditor>` block is untouched — same
  component, same parameters, same position (§9).
- `TopicCreateDialog`. It legitimately *does* need a coded-value picker, because
  creating a subject is exactly when you choose one. This spec does not change
  it, which leaves the create/edit asymmetry in place for the picker — but §3
  removes it for *editing*, which is where it was harmful.
- `SubjectEditDialog` (the rename-only dialog on the Topics landing) — a
  different dialog, different problem. See §8 Q4.
- Any change to the period *model*, endpoints, or FR-55…58.

---

## 3. Functional requirements — the coded value

**FR-ED-1 — The coded value is display-only.**
The `CodedValueDropdown` in the `Subject` form row is removed. In its place a
`FieldDisplay` shows the currently resolved value: the tenant-effective name,
code and description (the same value `CodedValues.GetByIdAsync` returns today,
which already applies tenant overrides). When the topic has **no** coded value
(`Topic.CodedValueId is null`), the row renders an explicit empty state
(*"No coded value — this subject can only be renamed."*) and only **Create
override** is offered.

**FR-ED-2 — No new selection is possible from this dialog.**
There is no code path in the redesigned dialog that changes
`Model.CodedValueId` to a *different pre-existing* coded value. The
`@bind-SelectedId:after="OnCodedValuePicked"` hook, the `_codedValueDropdown`
`@ref`, and `UpdateCodedValueFromDropdownAsync` are deleted. A test asserts
that the dialog renders no select/combobox bound to a coded value (§10).

**FR-ED-3 — "Edit override" is always available and is idempotent.**
Where a coded value backs the topic, the row shows an **Edit override**
secondary button. It opens the existing override form bound to the *current*
coded value id and pre-filled with the current effective name/code/description.
Saving issues `PUT /api/coded-values/{id}/override`
(`CodedValuesApiClient.UpsertOverrideAsync`).

The dialog must **not** probe whether an override row already exists: `PUT` is
create-or-update, so "Edit override" is correct and idempotent in both cases. Do
not add a read endpoint for this.

**FR-ED-4 — "Create override" creates a tenant-scoped provisional value.**
Available whenever the user has changed **both** code and description — the one
case in which an override is impossible because the code is the identity of a
different value. It issues
`CodedValuesApiClient.CreateProvisionalCodedValueAsync(...)` and re-points
`Topic.CodedValueId` at the new provisional id via `UpdateTopicAsync`.

This is the *only* way `Topic.CodedValueId` changes in this dialog, and it always
goes to a **newly created** value, never an existing one.

**FR-ED-5 — The save router is unchanged.**
`TopicCodedValueSaver.SaveAsync` / `TopicEditRouter` continue to decide between
`DirectNameOnly`, `EditInPlace`, `Override` and new-provisional. This spec
changes what the *user can express*, not which write the router picks. The
dialog's own copy must stop claiming the user can "pick the subject's coded
value".

**FR-ED-6 — Copy must state the tenancy effect.**
The hint under the row changes from *"Pick the subject's coded value, or switch
to
edit mode…"* to something that says: these changes are saved as **tenant
overrides** and do not alter the shared subject catalog.

---

## 4. Functional requirements — delivery periods

> **⚠️ SUPERSEDED (2026-09-26) — do not implement this section.**
> `specs/subject-period-exception-model.md` replaces the delivery-period
> whitelist with a **block/exceptions** model, because a subject pinned to a
> *period instance* silently disappears when the academic year rolls over and
> the pinned term is archived — and cannot be repaired through the picker, which
> excludes retired periods. Periods are no longer attached to the subject↔grade
> link at all.
>
> What survives from this section: **FR-ED-15** (period names, never GUID
> prefixes), **FR-ED-16** (grade-scoped section, hides without a grade) and
> **FR-ED-11** in inverted form (the *block* picker excludes `Archived`/
> `Deactivated`, but existing blocks on retired periods stay visible and
> deletable). Everything else — the one-row-per-bridge-row set, FR-ED-10,
> FR-ED-13, FR-ED-14, FR-ED-9 — is replaced or becomes moot; see that spec §5
> for the specific deletions and the one new rule.
>
> §0 decision 2 ("a period set, never a single value") and §8 Q1 (the shared
> `TopicPeriodsEditor` extraction) are **withdrawn**. §3 (the coded value) is
> unaffected and stands.
>
> **Superseded in part by `subject-enrollment-requirement-flag.md` (2026-09-26).**
> That spec adds an **"Enrollment"** section to this dialog for the
> `RequiresEnrollment` boolean, with a warning + bulk-enrol flow on toggle
> (FR-ER-25/FR-ER-27). It is a **different concern** from the blocks below: blocks
> decide *when* a subject is offered; `RequiresEnrollment` decides *who* may
> receive work for it. The dialog hosts both, plus the §3 coded-value row.

**FR-ED-7 — A period set, one row per bridge row.** *(withdrawn — see above)*
The section renders one row per existing `GradeTopicAssignment` for
`(GradeLevelId, TopicId)`. Each row is a period select plus a remove control.
"Add term" appends a row that becomes a **new** assignment on save. Removed
saved rows are tombstoned (`Removed`) so the edit is undoable before submitting;
a removed *unsaved* row is dropped outright.

> **Never key this collection by `TopicId`.** One subject legitimately has N
> rows for the same grade; `ToDictionary(a => a.TopicId, …)` throws
> `ArgumentException` on the duplicate key. This exact bug shipped once already
> and crashed the only period-editable surface
> (`subject-topic-delivery-periods.md` Gap 1). Group into
> `Dictionary<Guid, List<…>>`.

**FR-ED-8 — Multiple terms, multiple semesters, and a single year are all valid selections.**
The set may contain any combination of live sub-periods (terms, semesters) and
at most one whole-year entry. "Mathematics in Term 1 **and** Term 2" is the
normal case, not an edge case.

**FR-ED-9 — "A single year" has two distinct representations, and both are offered.**

| Option | `PeriodId` | Meaning |
|---|---|---|
| *Whole academic year (not tied to a year)* | `null` | Year-spanning per FR-55 — active whenever the assignment's date window is. Not bound to any particular academic year. |
| *\<Active year name\> (e.g. "2026 Academic Year")* | the academic-year period id | Bound to **this** academic year specifically (FR-57 permits an `AcademicYear`). |

These are genuinely different and must not be collapsed. They must be labelled so
the difference is legible, and the `null` option's label must not read like the
other one.

**FR-ED-10 — A year-spanning row cannot coexist with period-specific rows.**
Selecting *Whole academic year (not tied to a year)* alongside any term/semester
is self-contradictory: the subject is either always on or only during named
terms. The client rejects the combination with a per-row error and **no write is
attempted**. This is a new validation rule — today's `ValidateRows` only checks
duplicates and emptiness.

**FR-ED-11 — Selectable periods exclude `Archived` and `Deactivated`.**
`PeriodStatus` is `Draft | Active | Completed | Archived | Deactivated`
(`SchoolCollab.Students.Core/Domain/PeriodStatus.cs`). `Archived` and
`Deactivated` are filtered out of the picker — a subject cannot be delivered into
a period that is no longer live. `Draft`, `Active` and `Completed` stay
selectable so an upcoming term can be planned and a just-closed term can still
be corrected. See §8 Q2.

**FR-ED-12 — Scope to the active academic year.**
The picker offers the top-level academic years plus the sub-periods whose
`ParentPeriodId` is the tenant's active academic year (via
`GetActiveAcademicYearAsync`), matching FR-57 and the existing
`TopicPeriodsEditDialog.LoadPeriodsAsync`. A failed active-year lookup degrades
to any matching sub-period so the user still sees options — the server remains
the final gate.

**FR-ED-13 — Duplicate periods are rejected without a write.**
Two rows resolving to the same `PeriodId` is a client-side validation error on
both rows, raised before any request. (`null` appearing twice counts as a
duplicate.)

**FR-ED-14 — The set may not be emptied.**
A subject must be delivered in at least one period. Removing every row is
rejected with a clear message. (This is how a user un-assigns a subject from a
grade — via **Remove** on the card, not by emptying the set.)

**FR-ED-15 — Period names, never GUID prefixes.**
Every option label resolves through a `PeriodDto.Id → Name` map. A missing name
degrades to the 8-char prefix, never to a raw GUID in the common path.

**FR-ED-16 — The section is grade-scoped and hides itself without a grade.**
`TopicEditModel` gains `Guid? GradeLevelId`. When it is `null` the section is not
rendered at all — there is no bridge row to hang a period on, so offering the
editor would be a false affordance. The only current caller
(`GradeLevels/Detail.razor`) passes the grade; any future caller must pass it
too or gets no period section. This is the "gate the control, not just the
call" rule from
`documents/solution/activity-groups-flag-client-isolation.md` §5.

---

## 5. Layout and interaction

Dialog size stays `DialogSize.Large` (720px) — it already hosts the field
section, the strands editor, and now the period rows. Sections are separated by
a CSS `border-top` per the `dialog-ui` skill §2, with scoped CSS in
`TopicEditDialog.razor.css`; no inline `<style>`.

```
┌─ Edit subject · Mathematics ───────────────────────┐
│ Subject                                             │
│   [ Mathematics        ]  MATH      (read-only)    │
│   [ Edit override ]  [ Create override ]           │
│ ─────────────────────────────────────────────────── │
│ Name / Code / Description        (display ⇄ edit)   │
│ ─────────────────────────────────────────────────── │
│ Delivery periods                       (this grade) │
│   A subject can run in several terms, or all year.  │
│   [ Term 1        ▾]  [Remove]                      │
│   [ Term 2        ▾]  [Remove]                      │
│   [ Whole academic year (not tied to a year) ▾] [+]  │
│ ─────────────────────────────────────────────────── │
│ Strands                                             │
│   <StrandsEditor … />                    UNCHANGED   │
└─────────────────────────────────────────────────────┘
│                              [Cancel]  [Save]        │
```

The **Save periods**-style split of the existing period dialog collapses into
the single **Save** footer action. The dialog's existing two-mode footer
behaviour (display mode = server save; edit mode = local "Update Fields") is
preserved — `TopicFormFields` and the "Edit fields" body button are unchanged.

---

## 6. Data and save sequence

### 6.1 Load

`LoadAsync` (already `OnAfterRenderAsync(firstRender)`) additionally loads:

1. `ListGradeTopicsByGradeAsync(GradeLevelId)` → grouped into
   `Dictionary<Guid, List<TopicAssignmentDto>>`, filtered to
   `GradeLevelId == Model.GradeLevelId`. One row per element, carrying
   `AssignmentId` and `PeriodId`.
2. `ListPeriodsAsync()` + `GetActiveAcademicYearAsync()` → the selectable set
   and the name map.

Both are best-effort. A failure degrades the section (FR-ED-16's hide rule) and
sets the dialog's `Error`; it must not fail the dialog load. Use
`Error="@Error"` — the literal-string trap
(`subject-topic-delivery-periods.md` §8 pitfall 5) is a real bug that has shipped.

### 6.2 Model additions

```csharp
public sealed class TopicEditModel : TopicFormFields.ITopicFormModel
{
    // … existing: Id, Name, Code, Description, DisplayOrder, Original, CodedValue …

    /// <summary>Grade context. Null ⇒ the Delivery periods section is not rendered.</summary>
    public Guid? GradeLevelId { get; set; }

    /// <summary>Existing bridge rows, one per delivery period.</summary>
    public List<TopicPeriodRow> Rows { get; set; } = new();
}
```

`TopicPeriodRow` moves into the shared editor component unchanged
(`AssignmentId`, `PeriodId`, `OriginalPeriodId`, `Removed`, `IsNew`, `Choice`).

### 6.3 Save order

```
1. Client validation  ── duplicate period / year+term mix / empty set
      └─ fail ⇒ per-row errors, dialog stays open, ZERO requests
2. Apply period rows   ── per row: removed ⇒ DELETE
      │                    changed ⇒ PUT  /topic-assignments/{id}/period
      │                    new     ⇒ POST /topic-assignments/grade
      └─ any failure ⇒ collect + abort, dialog stays open, fields UNTOUCHED
                        (server 422 message shown verbatim, never swallowed)
3. TopicCodedValueSaver.SaveAsync(...)   ── override / in-place / new provisional
4. UpdateTopicAsync(...)
5. return TopicEditResult(topic, periodIds)
```

**Why periods first.** FR-57/FR-58 validation lives on the server and can
reject; a field rename cannot. Periods-first means the common failure leaves the
subject's identity untouched. The inverse — periods applied, then a field write
fails — is possible but requires the coded-value API to be down while the
Students API is up; it is accepted and surfaces as a dialog `Error` with the
periods already saved. The dialog copy says so.

**Result DTO.** Extend to carry the applied period set so the caller can avoid a
blind refetch. The caller (`OpenTopicEditAsync`) already calls
`ReloadAssignedTopicsAsync()` on a non-null result, so this is an optimisation,
not a requirement.

---

## 7. Acceptance criteria

- **AC-1** Opening the dialog on a subject with no coded value shows the empty
  state and only **Create override**.
- **AC-2** The dialog renders **no** coded-value select or combobox.
- **AC-3** Editing only the name issues the in-place/override write and changes
  no period rows.
- **AC-4** Adding a second term produces **two** `POST /topic-assignments/grade`
  calls on first save and **one** `PUT …/period` on a later edit of that term.
- **AC-5** A subject already in Term 1 and Term 2 opens the dialog with **two**
  rows, each pre-selected to the right named period.
- **AC-6** `Archived` and `Deactivated` periods appear nowhere in the picker.
- **AC-7** Selecting *Whole academic year* **and** *Term 1* shows per-row errors
  and issues **no** requests.
- **AC-8** Removing every row shows "keep at least one delivery period" and
  issues **no** requests.
- **AC-9** Two rows on the same period shows an error on both and issues **no**
  requests.
- **AC-10** A server `422` on one period row surfaces that server's message, the
  dialog stays open, and **no** coded-value or topic write was issued.
- **AC-11** The strands editor renders and behaves exactly as before the change.
- **AC-12** Opening the dialog without a `GradeLevelId` renders no Delivery
  periods section and no period requests fire.

---

## 8. Open questions — decisions needed before implementation

**Q1 — What happens to `TopicPeriodsEditDialog` and the two "Edit periods" kebabs?**

| Option | Effect |
|---|---|
| **(A) Shared component, keep both entry points** *(recommended)* | Extract the period set editor into `TopicPeriodsEditor.razor`. `TopicEditDialog` uses it as a section; `TopicPeriodsEditDialog` becomes a thin wrapper. One implementation, both existing entry points keep working, the 7 existing `TopicPeriodsEditDialogTests` keep guarding the editor. Cost: a small wrapper component survives. |
| (B) Fold entirely, delete the dialog | One entry point, no wrapper. Cost: removes a working kebab from the Subjects card and `GradeTopicsDialog`, and their tests. Broader blast radius. |

Default to **(A)** unless the owner prefers a single entry point.

**Q2 — Does "active periods" mean "not archived", or strictly `Status == Active`?**

| Option | Selectable | Consequence |
|---|---|---|
| **(A) Not archived** *(recommended)* | `Draft`, `Active`, `Completed` | A future `Draft` term can be planned; a just-closed `Completed` term can still be corrected. This is what the existing editor does and what `subject-topic-delivery-periods.md` §3 settled. |
| (B) Strictly active | `Active` only | You cannot pre-assign a subject to an upcoming term, and **you cannot correct a subject that ran in a term that just closed** — the correction is impossible precisely when it is most needed. |

The owner asked for "active periods, not archived", which reads as (A). Flagging
because (B) is the stricter literal reading and has a real cost.

**Q3 — Should the Topics landing's `SubjectEditDialog` get the same treatment?**
It is a rename-only `Code` + `Name` dialog with no period path. Leaving it
means renaming a subject is possible in two places with two different sets of
affordances. Out of scope here; flagging for a follow-up.

**Q4 — Is the "not tied to a year" (`PeriodId = null`) option actually wanted
next to the specific-year option?** FR-ED-9 offers both because they differ, but
two year-shaped options in one picker may be one too many. If the owner prefers
one, the specific academic year is the more precise of the two and the `null`
option can be dropped (it would then require an explicit API change to express
year-spanning).

---

## 9. Explicitly unchanged

`<StrandsEditor TopicId="Model.Id" TopicName="Model.Name" />` is **not touched**
by this spec — same component, same parameters, same rendered position. Lessons
are parented strands (`strand-lesson-unification-plan.md`); nothing about that
model changes. The strands block is listed here only so a reviewer can confirm
it was considered and deliberately excluded, not overlooked.

Also unchanged: `TopicFormFields` (Name / Code / Description / DisplayOrder and
the display ⇄ edit toggle), the dialog's `DialogSize.Large`, the
`DialogShellBase` footer behaviour, `TopicCodedValueSaver` / `TopicEditRouter`,
every period endpoint, and the `GradeTopicAssignment` model.

---

## 10. Testing requirements

Per `.github/copilot/rules/testing.md` — MSTest + Moq + FluentAssertions, bUnit
for components, MTP. Each FR-ED with behaviour gets a test.

- `TopicEditDialogTests` (new): AC-1, AC-2, AC-3, AC-11, AC-12.
- `TopicPeriodsEditorTests` (extracted from the existing
  `TopicPeriodsEditDialogTests`): AC-4, AC-5, AC-6, AC-7, AC-8, AC-9, AC-10.
- **AC-5 must be mutation-verified**: seed two bridge rows for one topic and
  assert the editor opens with two rows. Reverting the seed to `.Take(1)` — or
  keying the map by `TopicId` — must fail the test. (Keying by `TopicId` does
  not compile against `Dictionary<Guid, List<…>>`, which is itself the guard.)
- **AC-7/8/9 must assert "no request issued"**, not just the error text — the
  handler's `Calls` list must be empty for the write endpoints. A test that only
  checks the message passes even if the write went out.
- The 7 existing `TopicPeriodsEditDialogTests` must keep passing unchanged under
  option (A) §8 Q1.

Gotchas to expect (all recorded in
`documents/solution/subject-topic-delivery-periods.md` §8):

- `FluentSelect` binds `Value`/`ValueChanged` over `string`; inline lambdas are
  not coerced to `EventCallback<string>` — use
  `EventCallback.Factory.Create<string>(this, …)`, one per row.
- `FluentSelect` and `FluentMenu` do **not** materialise their children while
  closed. Assert on bound `Items` / the action list, never on markup.
- `Error="@Error"`, never `Error="Error"`.
- Invoke a row action through `cut.InvokeAsync(...)`; a direct call throws
  *"The current thread is not associated with the Dispatcher"*.

---

## 11. Supersession

On implementation, `documents/solution/subject-topic-delivery-periods.md` §5
("Decision: the period editor is *topic-scoped*") must be amended: the reasoning
for a *set* stands unchanged, but the *entry point* is now a section of the edit
dialog. Gap 4 (the create/edit asymmetry) is resolved by this spec for the
period half. `documents/configuration.md` needs no change — no feature flag is
involved.
