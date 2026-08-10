# Spec: SectionCard Lessons Adoption + Shared Component Tests

> Status: **Draft — under review**
> Owner: Students + Admin contexts
> Depends on: `grade-level-detail-view-plan.md` (Phase A), `grade-detail-rich-grids-plan.md`
> (cg/dm), `grade-level-landing-topics-strands-lessons.md`, `ui-visible-tenancy-guard.md`
> Branch: `stack/N-section-card-adoption` (gh-stack, one PR per phase)

## 0. Why this spec exists

The generic `SectionCard` component
(`src/Students/.../Components/Students/SectionCard.razor`) drives four cards on the
grade-level Detail page (`GradeLevels/Detail.razor`): **Subjects (topic)**, **Teachers**,
**Students**, and **Streams**. During the section-card redesign (`b087208`) and the topic
create/edit + destructive-confirm work (`b239c19`, `b54c2c6`), the **Subjects card became the
richest usage** and accumulated several lessons the other three cards do not yet follow.

Those lessons and the repeated test patterns that grew around them were **never captured in a
spec** — the two parent specs are marked complete and pre-date the redesign. This spec:

1. Locks the lessons learned from the Subjects (topic) card.
2. Diffs the other three cards against it and lists the gaps.
3. Centralizes the repeating SectionCard test logic into a single shared component test file.
4. Rolls the lessons into the repo rules so they apply to future cards everywhere.

## 1. Goal

- **Adopt** the Subjects-card lessons (L1–L5 below) consistently across all four grade-detail
  section cards, closing the gaps in §3 and giving every card a full `ItemActions` kebab with its
  domain-appropriate actions.
- **De-duplicate** the 4×-repeated SectionCard rendering tests by introducing a single
  `SectionCardTests.cs` that verifies the component's contract once, against every usage.
- **Enforce** the lessons at the repo-rule level so new cards inherit them without re-review.

## 2. Current state — capability matrix

All four cards use `<SectionCard TItem="...">` with the following parameters:

| Capability | **Subjects (topic)** | Teachers | Students | Streams |
|---|---|---|---|---|
| `OnAddClick` (create/add dialog) | ✅ `OpenTopicCreateAsync` | ✅ `OpenTeachersDialogAsync` | ✅ `OpenAddStudentsAsync` | ✅ `OpenStreamCreateAsync` |
| `ItemTextSelector` | ✅ name | ✅ display name | ✅ full name | ✅ name |
| `ItemMetaSelector` (secondary line) | ✅ `[strands, lessons]` | ✅ `[role]` | ✅ `["gender, age"]` (one combined string) | ❌ |
| Primary affordance | `ItemOnClick` → **edit dialog** | `ItemHrefSelector` → navigate | `ItemHrefSelector` → navigate | `ItemOnClick` → **edit dialog** |
| `ItemNameTitle` tooltip | ✅ "Edit topic" | ❌ | ✅ "View student" | ✅ "Edit stream" |
| `ItemActions` kebab (destructive+confirm) | ✅ Strands/Teachers/**Remove** | ✅ (planned) | ✅ (planned) | ✅ (planned) |
| View-all | `OnViewAllClick` (dialog) | `OnViewAllClick` (dialog) | `ViewAllNavigationUrl` (navigate) | `ViewAllNavigationUrl` (navigate) |
| Per-card error state | page msg-bar `_topicsError` | ❌ `_teachersError` set, **never rendered** | ✅ `ErrorMessage` `_studentsError` | ❌ (silently `[]`) |

## 3. Lessons learned from the Subjects (topic) card

The following are the durable lessons embodied by the Subjects card. They are the target
state every card should reach.

### L1 — Destructive confirmation is enforced at the component (kebab) level, not per-page
`RowActionsMenu.InvokeActionAsync` gates any action marked `destructive: true` through
`DialogServiceExtensions.ShowConfirmDialogAsync` (modal, dark overlay, click-outside/ESC
dismiss). The Subjects card's `ItemActions` kebab uses this for **Remove** with a custom
`confirmMessage`. Enforced everywhere the kebab is used — no page-specific confirmation, and
no double-prompt. Codified in `.github/copilot/rules/blazor-components.md` §Destroy. Currently
the Subjects card is the **only** card that reaches this machinery.

### L2 — `ItemNameTitle` tooltip advertises the primary affordance
e.g. `ItemNameTitle="Edit topic"`. Cheap, aids discoverability. Teachers card omits it; the
tooltip falls back to the item text.

### L3 — Post-mutation `StateHasChanged()` when a child component triggers the reload
`ReloadAssignedTopicsAsync()` explicitly calls `StateHasChanged()` because it is invoked from
child components (the row kebab, `GradeTopicsDialog`) whose events re-render **themselves**, not
the page. Without it the SectionCard never receives the refreshed `Items` after an add/remove.
This is the `testing.md` rule 4 mutation-handler case.

### L4 — Name is the primary affordance; secondary + destructive actions move to the kebab
Keeps the row from being crowded. The Subjects card's name opens the edit dialog; the kebab
holds Strands/Teachers/Remove.

**Conscious deviation (locked, §9):** Teachers + Students keep **Edit** in the kebab even though
the exemplar puts edit on the name. Reason: their name is a *view-profile* navigation, not an edit,
so Edit needs an explicit home. Edit opens a **shared-form-fields edit dialog** (mirroring
`TopicEditDialog`); **Create** is a card-level affordance via a **shared-form-fields create dialog**
(mirroring `TopicCreateDialog`). See §9 + §11/§12.

### L5 — Per-card error isolation
Each card should surface its own load/mutation failure so a failure in one card does not fail
the page. **Only the Students card actually wires `ErrorMessage`** on SectionCard. The Subjects
card renders `_topicsError` via a page-level `<FluentMessageBar>` above the card (works, but not
via the param). The **Teachers** card sets `_teachersError` but **never renders it** (silent — G1).
The **Streams** card silently empties to `[]` (G3). Target: every card wires `ErrorMessage`.

## 4. Gaps / misses on the other cards

### G1 — Teachers card (biggest inconsistency)
- **No `ItemActions` kebab** — no inline role add/change, view profile, edit, or Remove. Must open
  the full `GradeTeachersDialog` for everything.
- **No `ErrorMessage` wired** — `_teachersError` is set by `ReloadTeachersAsync` /
  `RemoveTeacherAsync` / `OnRoleChangedAsync` but is **never rendered**; failures are silent.
  Violates L5 (same bug class as G3).
- **No shared `TeacherFormFields` / `TeacherFormModel`** — Edit/Create dialogs need a reusable
  form-fields component; none exists (teachers use a setup-wizard page today). Net-new UI.
- **No `ItemNameTitle`** tooltip (L2 not applied).

### G2 — Students card
- **No `ItemActions` kebab** — zero inline actions. The card supports several domain actions that
  are currently only reachable via the full students landing page.
- **Card data lacks the enrollment ID** — the card renders `StudentDto[]` from
  `ListStudentsByGradeAsync`, but `StudentDto` has **no `EnrollmentId`**. Transfer + Withdraw
  operate on an enrollment id (`POST /enrollments/{id}/transfer|withdraw`). The landing pages
  resolve it via `ListEnrollmentsByStudent(studentId)`; the card must add this resolution
  (net-new wiring — see §11.2).
- **No student create/edit dialog** — `StudentFormFields` + `StudentFormModel` exist, but there is
  no dialog wrapper (only `StudentPickerDialog` / `StudentTransferDialog` / `WithdrawEnrollmentDialog`).
- Keeps `ViewAllNavigationUrl` (navigation) — appropriate for a large collection.

### G3 — Streams card
- **No `ItemActions` kebab** — no inline edit or Remove from the preview (edit is only via the
  primary `ItemOnClick`; there is no Remove at all).
- **No `ErrorMessage` wired** — `ReloadStreamsAsync` catches and sets `[]` silently, so a load
  failure renders as the misleading "No streams defined" empty state. Violates L5 and is a real
  bug (a failure looks like an empty state).

## 5. Test centralization — shared `SectionCardTests.cs`

### 5.1 Problem
`tests/SchoolCollab.Admin.Tests.Unit/GradeLevelDetailPageTests.cs` re-tests the same SectionCard
*rendering* behaviors once per card:
- `Detail_TopicsCard_ListsPreview_AndCount`
- `Detail_TeachersCard_ListsPreview_AndCount`
- `Detail_StudentsCard_ListsStudents_WithDemographics`
- `Detail_StreamsCard_ListsStreams`

Each re-implements "card renders title + count + list preview + empty state + add button." That
is 4× duplicated logic that is really testing **SectionCard** plus the page wiring.

### 5.2 Why it centralizes cleanly
`SectionCard` is self-contained: it only injects `NavigationManager` and takes all data via
parameters (`Items`, selectors, callbacks). No HTTP, no FluentUI service dependency beyond the
standard render. Therefore its rendering contract can be verified **once** against fake data and
apply to every usage.

### 5.3 Target split

**A. New `tests/SchoolCollab.Admin.Tests.Unit/SectionCardTests.cs`** — render `<SectionCard>`
directly with fake `Items` + callbacks and assert the full component contract once. This is the
shared home for all kebab + action assertions so each card's wiring test asserts only that the
elements are wired, not the rendering mechanics:
- title + count render
- empty state (`EmptyMessage`)
- loading ring (`Loading`)
- error message (`ErrorMessage`)
- top-N preview (`MaxPreviewItems`)
- `ItemTextSelector` renders
- `ItemMetaSelector` renders the `|`-separated meta line
- `ItemHrefSelector` renders a hypertext anchor
- `ItemOnClick` fires
- `ItemNameTitle` tooltip
- `ItemActions` kebab renders
- add button (`ShowAddButton` / `OnAddClick`) fires
- view-all (nav url + click callback)
- `ItemTemplate` custom rendering

**B. `GradeLevelDetailPageTests.cs` keeps only wiring tests** — which handler is bound to
`OnAddClick` / `ItemOnClick` / `ItemActions` / `ViewAll`, per-card selectors, and the
mutation-handler local-state assertions (the `testing.md` rule 4 requirement). Most of these are
already source-inspection today.

### 5.4 Honest caveats
- Page-level wiring still needs its own tests — SectionCard cannot know about HTTP or the page's
  handlers. You cannot eliminate page tests entirely, only the duplicated rendering assertions.
- The `SectionCardTests.cs` lives in `Admin.Tests.Unit` (already references
  `Students.Application` + has bUnit). The source-inspection helpers (`ReadSectionCardSource`,
  `ReadDetailSource`) stay in the page test file.

## 6. Repo-rule enforcement

### 6.1 `.github/copilot/rules/blazor-components.md`
Add a SectionCard rule block:
- Destructive actions in a card's `ItemActions` kebab must be `RowAction.Callback(..., destructive: true)`
  with a `confirmMessage` — never a hand-rolled page-level confirm (double-prompt).
- Every card must wire `ErrorMessage` so a load failure renders an error, not a misleading empty state.
- Every card should set `ItemNameTitle` when the primary affordance is an edit or a named view.
- Child-component-triggered reloads must call `StateHasChanged()` after refetching `Items`.
- Card-level create + per-row edit must use **shared-form-fields dialogs** (`XxxFormFields` bound
  to `IXxxFormModel`, wrapped in `DialogShellBase` via `ShowShellDialogAsync`) — mirroring
  `TopicCreateDialog`/`TopicEditDialog` — not landing-page forms.

### 6.2 `.github/copilot/rules/testing.md`
- New SectionCard capabilities must be covered in `SectionCardTests.cs` (the shared component test).
- Page tests stay focused on wiring + per-card selectors + mutation-handler local-state assertions.

## 7. Rollout (one PR per phase, each shippable)

| Phase | Branch | Scope | Risk |
|-------|--------|-------|------|
| **1** | `stack/1-sectioncard-shared-tests` | Add `SectionCardTests.cs`; strip the 4× duplicated rendering assertions out of `GradeLevelDetailPageTests.cs` (keep wiring). | Pure test refactor — no prod change. |
| **2a** | `stack/2-streams-card` | Streams card: surface load errors via `ErrorMessage` (fix G3) **and** add `ItemActions` kebab with **Edit** + destructive **Remove** (confirm). Test. | Prod behavior change. |
| **2b** | `stack/3-teachers-card` | Teachers card: add `ItemActions` kebab with **Role** (grade-level), **Subjects** (focused per-teacher subject+role dialog), **View profile**, **Edit**, destructive **Remove** (confirm=unlink) + `ItemNameTitle` (fix G1). **Author `TeacherFormFields` + `TeacherFormModel`** (none exist) and wrap them in Edit/Create dialogs mirroring `TopicEditDialog`/`TopicCreateDialog`. **Author `TeacherSubjectRoleFormFields`** (shared) and use it in **both** the focused Subjects dialog **and** `GradeTeachersDialog`. **Wire `ErrorMessage`** (fix silent `_teachersError`). Test. | Prod behavior change + new shared form-fields (teacher + subject-role). |
| **2c** | `stack/4-students-card` | Students card: add `ItemActions` kebab with **Transfer**, **Withdraw** (end-date + reason), destructive **Remove** (soft-delete the whole student, not an enrollment-withdraw), **Edit**, **View profile**; card Add → **Create** dialog. Wrap existing `StudentFormFields`/`StudentFormModel` in Edit/Create dialogs mirroring `TopicEditDialog`/`TopicCreateDialog`. **Resolve the enrollment id** for Transfer/Withdraw (§11.2). Add the **Withdraw-reason backend** (§11.1) if not already landed. **Preserve the name as the default link to the view page.** Test. | Prod behavior change + dialog wrappers + withdraw-reason backend. |
| **3** | `stack/5-rule-enforcement` | Update `blazor-components.md` + `testing.md` rules. | Doc only. |

Ordering rationale: Phase 1 first (pure win, no risk); then the three prod fixes smallest-first
(2a streams, 2b teachers, 2c students — the largest kebab). All depend on the destructive-confirm
machinery already present in `RowActionsMenu`/`ShowConfirmDialogAsync` — no new component needed.
Existing dialogs are reused where they exist (`StudentTransferDialog`, `WithdrawEnrollmentDialog`,
`CodedValueDialog`); **net-new dialog UI** is the Edit/Create wrappers around `StudentFormFields`
(students), the new `TeacherFormFields` + model + dialogs (teachers), and the shared
`TeacherSubjectRoleFormFields` used by both the focused Subjects dialog and `GradeTeachersDialog`
— all mirror `TopicEditDialog`/`TopicCreateDialog` shared-form-fields pattern. Net-new backend is
the withdraw reason only (§11.1); enrollment-id resolution is wiring (§11.2). See §9 + §11/§12.

## 8. Test plan

- `SectionCardTests.cs` (new): full rendering contract — happy path, empty, loading, error,
  top-N, selectors, href/onClick, tooltip, kebab, add, view-all, ItemTemplate.
- `GradeLevelDetailPageTests.cs`: wiring tests unchanged; remove the 4× duplicated
  preview/empty-state rendering assertions (now covered by §5.3A).
- New/updated for G1/G2/G3:
  - Teachers card renders a kebab offering Role / Subjects / View profile / Edit / Remove; Remove
    triggers `ShowConfirmDialogAsync`; confirm **unlinks from this grade** (teacher stays in catalog,
    active in other grades). Role = grade-level role change; Subjects = focused per-teacher subject+role
    dialog sharing `TeacherSubjectRoleFormFields` with `GradeTeachersDialog`. Edit/Create open the new
    `TeacherFormFields` dialogs. **Teachers card renders `ErrorMessage`** on load/mutation failure
    (fixes silent `_teachersError`).
  - Students card renders a kebab offering Transfer / Withdraw / Remove / Edit / View profile; card
    Add → Create. Each maps to the right action or navigation. The name remains the default link to
    the view page; Remove soft-deletes the whole student; Withdraw end-dates with a reason;
    Transfer/Withdraw resolve the enrollment id (§11.2).
  - Streams card renders a kebab offering Edit / Remove; Remove triggers `ShowConfirmDialogAsync`
    (disable); and the card renders `ErrorMessage` text on load failure instead of "No streams defined".
- Full suite green at each stack tip (Admin, Students, Assignments, Settings) per the repo's
  stack convention.

## 9. Decisions (locked) & open questions

### Locked decisions (from review)
1. **Teachers kebab scope (G1):** inline **Role add/change**, **Subjects**, **View profile**,
   **Edit**, and destructive **Remove** (confirm). **Edit** opens a shared-form-fields edit dialog
   (mirroring `TopicEditDialog`) — **net-new UI**: no `TeacherFormFields` / `TeacherFormModel` exists
   yet, so those must be created. **Create** is the card Add affordance via a shared-form-fields
   create dialog (mirroring `TopicCreateDialog`). **Backend audit:** Role (`SetTeacherGradeLevelRoleAsync`),
   subject+role (`LinkTeacherTopicAsync`/`UnlinkTeacherTopicAsync`), and Remove
   (`UnlinkTeacherGradeLevelAsync`) all already exist; `CreateTeacherAsync` / `UpdateTeacherAsync`
   exist. **Teachers Remove = unlink from this grade** (locked): removes only the grade assignment;
   the teacher stays in the catalog and remains active in other grades (matches the existing
   `RemoveTeacherAsync` → `UnlinkTeacherGradeLevelAsync`). **Teacher-grade assignment model** (locked):
   a teacher's link to a grade is **role-only** (`SetTeacherGradeLevelRoleAsync`) **or
   subject+role** (per-topic links via `LinkTeacherTopicAsync(teacherId, topicId, roleCodedValueId)`);
   both facets are managed in `GradeTeachersDialog` today, so the kebab's **Role** does the quick
   grade-level role change and **Subjects** opens subject+role management for that teacher.
2. **Students kebab scope (G2):** inline **Transfer**, **Withdraw**, destructive **Remove**,
   **Edit**, **Create**, and **View profile**. **Edit** opens a shared-form-fields edit dialog
   (mirroring `TopicEditDialog`) — `StudentFormFields` + `StudentFormModel` already exist, so only
   the **dialog wrapper is net-new**. **Create** is the card Add affordance via a shared-form-fields
   create dialog (mirroring `TopicCreateDialog`) — coexists with the existing enroll-existing flow
   (`StudentPickerDialog`); the exact Add-button UX (enroll vs create vs chooser) is a Phase 2c
   detail. **Transfer, View profile already have backend/action surface.** **Remove is a soft delete
   of the whole student record** (`Student.Delete()` → `DELETE /students/{id}`) — it is **NOT** an
   enrollment-withdraw, so it does **not** require a reason. **Withdraw is a separate, distinct
   action**: an end-date operation on the enrollment with a required **reason** — the end-date
   exists, but **capturing the reason is the only net-new backend** (see §11.1). **Transfer +
   Withdraw need the enrollment id**, which the card's `StudentDto[]` does not carry — net-new
   wiring (see §11.2). Map each to the existing student-domain actions (see §11).
3. **Streams kebab scope (G3):** inline **Edit** and destructive **Remove** (confirm). **Remove
   means disable** (set `isDisabled`, keep the row) — matching coded-value lifecycle, not a hard
   delete. **Backend audit:** `CodedValuesApiClient.DisableAsync` → `POST /coded-values/{id}/disable`
   + `CodedValueDto.IsDisabled` already exist — **no net-new backend**.
4. **Student primary affordance is preserved:** the student item's default link stays
   `ItemHrefSelector` → `/students/{id}` (view page). The kebab is secondary; it does **not** take
   over the primary click. Create/edit for students uses **dialogs** (consistent with the other
   cards), not the landing page forms.
5. **Teachers role UX:** inline role add/change via a small role-selection dialog reusing
   `GradeTeachersDialog`'s `CodedValueDropdown Parent="CodedValueParent.TeacherRoles"` binding.
6. **`SectionCardTests.cs` placement:** `Admin.Tests.Unit` (references `Students.Application` +
   bUnit). No dedicated component-test project.
7. **Teachers "Subjects" kebab action:** opens a **focused per-teacher subject+role dialog**
   (kebab-scale), distinct from the full `GradeTeachersDialog` (View all). Both surfaces share a
   **single `TeacherSubjectRoleFormFields` component** bound to a model (the subject+role
   add/edit form) so the focused dialog and `GradeTeachersDialog`'s subject+role flow stay in sync
   — same shared-form-fields pattern as `StudentFormFields`/`TopicCreateDialog`.

### Resolved open questions (from prior review)
- **§9 (old) Q2 — Student action mapping:** Transfer / Edit / View exist; Remove = soft delete;
   Withdraw = end-date + reason. See §11 for the full mapping.
- **§9 (old) Q3 — Streams Remove = disable** (not hard delete).
- **§9 (old) Q4 — Teachers role UX = small role dialog** (not a menu submenu).

### Open questions
_(none — all resolved; see locked decisions §9.)_

## 10. Out of scope
- No changes to SectionCard's public parameter surface (unless a gap requires one — deferred).
- No changes to strands/lessons/topics CRUD semantics.
- No notification / delivery changes.
- A full enrollment-lifecycle redesign is out of scope. Only the kebab-required backend is in
  scope (withdraw reason, §11.1); Transfer / View are reused as-is, and Edit / Create gain new
  shared-form-fields dialog wrappers (§11/§12).

## 11. Resolved student-kebab action mapping

| Kebab action | Type | Behaviour | Backend art | Dialog/nav |
|---|---|---|---|---|
| **View profile** | non-destructive | Navigate to the student detail page | ✅ exists | `ItemHrefSelector` → `/students/{id}` |
| **Edit** | non-destructive | Open the student edit **dialog** | ⚠️ backend + `StudentFormFields`/`StudentFormModel` exist; **dialog wrapper is net-new** (mirror `TopicEditDialog`) | `StudentFormModel`-based shell dialog |
| **Create** | non-destructive (card Add) | Open the student create **dialog** | ⚠️ backend + `StudentFormModel` exist; **dialog wrapper is net-new** (mirror `TopicCreateDialog`); coexists with enroll-existing `StudentPickerDialog` | `StudentFormModel`-based shell dialog |
| **Transfer** | mutating | Change the student's grade | ✅ backend (`TransferStudent` cmd + `Reason`, `POST /enrollments/{id}/transfer`); ⚠️ **needs enrollment id** (see §11.2) | `StudentTransferDialog` + `StudentTransferModel` |
| **Withdraw** | mutating (L3) | **End-date** the enrollment with a required **reason** | ⚠️ end-date exists (`WithdrawStudent` + `POST /enrollments/{id}/withdraw`); **reason is net-new** (§11.1); ⚠️ **needs enrollment id** (see §11.2) | `WithdrawEnrollmentDialog` + reason field |
| **Remove** | destructive (soft delete) | Soft-delete the **whole student record** (`Student.Delete()` sets `isDeleted` + `DeletedAt`, `DELETE /students/{id}`). **Not** an enrollment-withdraw (no reason required). | ✅ exists (`Student.Delete()` + `DELETE /students/{id}` + `DeleteStudentAsync`) | confirm via `ShowConfirmDialogAsync` |

- **Primary affordance preserved:** the student name remains the default link to the view page
  via `ItemHrefSelector`; the kebab is secondary and does not capture the primary click.
- **Remove = soft delete of the whole student** (`Student.Delete()`, `isDeleted`), **not** an
  enrollment-withdraw and not a hard unenroll; destructive confirm applies. It does **not** require
  a reason.
- **Withdraw** is a distinct action: it end-dates the enrollment (with a reason) rather than
  soft-deleting the student.
- **Backend audit result:** Transfer, Edit, Create, View, and Remove all have existing backend.
  **Two net-new items for students:** (1) capturing the **withdraw reason** (§11.1, backend);
  (2) resolving the **enrollment id** the card data does not carry (§11.2, wiring). Edit/Create
  need **net-new dialog wrappers** around the existing `StudentFormFields`. No changes to
  `EnrollStudentRequest` are needed.

### 11.1 Net-new backend — Withdraw reason (small, additive)

The withdraw stack currently has **no reason field** (contrast: transfer already has `Reason`):
- `StudentEnrollment.Withdraw(DateOnly? exitDate)` — no `reason` param (transfer has one + `TransferReason`).
- `WithdrawStudent(EnrollmentId, ExitDate)` command — no reason.
- `WithdrawStudentRequest(ExitDate)` route body — no reason.
- `WithdrawEnrollmentModel` — no reason field.

Mirror the existing transfer-reason pattern:
1. Add nullable `WithdrawReason` to `StudentEnrollment` + `reason` param on `Withdraw(...)`.
2. Add `reason` to `WithdrawStudent` command + `WithdrawStudentRequest`.
3. Add a reason field to the withdraw dialog model/content.

This keeps the withdraw change purely additive and consistent with how transfer records its
reason. Excluded from Phase 1 (test refactor) and Phase 2c (kebab wiring) until this lands.

### 11.2 Net-new wiring — Enrollment id for Transfer / Withdraw

The card renders `StudentDto[]` from `ListStudentsByGradeAsync`, and `StudentDto` carries **no
`EnrollmentId`** (fields: Id, StudentNumber, names, DOB, gender, IsDeleted, Age, GenderName,
CurrentGrade). But Transfer (`POST /enrollments/{id}/transfer`) and Withdraw
(`POST /enrollments/{id}/withdraw`) key off the **enrollment id**, not the student id.

The landing pages resolve it via `ListEnrollmentsByStudent(studentId)` → `StudentEnrollmentDto`
(active enrollment's `Id`). Those queries already exist (`ListEnrollmentsByStudent`, plus the batch
`ListEnrollmentsByStudents`). Options for the card:
1. **Extra fetch per kebab open** — call `ListEnrollmentsByStudentAsync(studentId)` when the user
   picks Transfer/Withdraw, then feed the active enrollment id into the existing dialog. Simplest;
   no card-data change.
2. **Switch the card data source** to `StudentEnrollmentDto[]` (via `ListEnrollmentsByStudents`)
   so each row already carries its enrollment id; enrich with the demographics the card shows today.
   More upfront work, no per-action fetch.

Pick per Phase 2c; both reuse existing queries. **No net-new backend.**

## 12. Resolved teacher-kebab action mapping

| Kebab action | Type | Behaviour | Backend art | Dialog/nav |
|---|---|---|---|---|
| **View profile** | non-destructive | Navigate to the teacher detail page | ✅ exists | `ItemHrefSelector` → `/students/teachers/{id}` |
| **Edit** | non-destructive | Open the teacher edit **dialog** | ⚠️ backend `UpdateTeacherAsync` exists; **`TeacherFormFields` + `TeacherFormModel` + dialog are all net-new** (mirror `TopicEditDialog`) | new `TeacherFormFields`-based shell dialog |
| **Create** | non-destructive (card Add) | Open the teacher create **dialog** | ⚠️ backend `CreateTeacherAsync` exists; **form-fields + model + dialog net-new** (mirror `TopicCreateDialog`) | new `TeacherFormFields`-based shell dialog |
| **Role add/change** | mutating | Change the teacher's **grade-level** role | ✅ exists (`SetTeacherGradeLevelRoleAsync`, `PATCH /teachers/{id}/grade-levels/{gid}/role`) | small role dialog reusing `CodedValueDropdown Parent="CodedValueParent.TeacherRoles"` |
| **Subjects** | mutating | Manage **subject+role** links (assign/unlink topics taught on this grade, each with a per-topic role) | ✅ exists (`LinkTeacherTopicAsync(teacherId, topicId, roleCodedValueId)`, `UnlinkTeacherTopicAsync(teacherId, topicId)`) | **focused per-teacher subject+role dialog** (§9 dec 7) sharing `TeacherSubjectRoleFormFields` with `GradeTeachersDialog` |
| **Remove** | destructive | **Unlink from this grade** (`UnlinkTeacherGradeLevelAsync`); removes only the grade assignment — **teacher stays in the catalog, active in other grades** | ✅ exists (`UnlinkTeacherGradeLevelAsync`) | confirm via `ShowConfirmDialogAsync` |

- **Teachers have more net-new UI than students:** there is no `TeacherFormFields` / `TeacherFormModel`
  today (teachers use a setup-wizard page), so Edit/Create require authoring the shared form-fields
  component + model + dialog — not just a dialog wrapper.
- **Net-new shared form-fields for subject+role:** a `TeacherSubjectRoleFormFields` component +
  model (topic selector + `CodedValueDropdown Parent="CodedValueParent.TeacherRoles"`) is authored
  **once** and used in **both** the focused per-teacher Subjects kebab dialog **and** the existing
  `GradeTeachersDialog`'s add-subject flow — keeping the two surfaces in sync (§9 dec 7).
- **Teacher-grade assignment model (locked):** a teacher's link to a grade is **role-only**
  (grade-level role via `SetTeacherGradeLevelRoleAsync`) **or subject+role** (per-topic links via
  `LinkTeacherTopicAsync(teacherId, topicId, roleCodedValueId)`). The kebab **Role** action does
  the quick grade-level role change; **Subjects** manages the per-topic subject+role links. Both
  facets already exist in `GradeTeachersDialog`; the kebab surfaces them per row.
- **Remove** reuses the existing confirm wording: "Remove teacher '…' from this grade? Only the
  grade assignment is removed — the teacher stays in the catalog."