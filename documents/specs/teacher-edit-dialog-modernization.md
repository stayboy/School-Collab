# Spec: Teacher Create/Edit Dialog Modernization

> **Status:** Draft v4 — spec locked; implementation complete.
>
> **v4 — assignments-grid feature (feature change requested by owner):**
> 1. **Subjects go with grades — no standalone subjects.** A teacher's subject is no longer a standalone `(topic, role)` link; every subject is **grade-scoped**. The dialog's subjects + grades sections are replaced by a single **teaching-assignments grid**.
> 2. **Two assignment row types.** Each grid row is one teaching assignment:
>    - **Grade assignment** = `grade + optional subject + role` (a teacher may be assigned to a grade with a role, or to a subject within a grade with a role).
>    - **Activity assignment** = `activity + role + optional grades` (a teacher may be assigned to an activity with a role, optionally across multiple grades).
> 3. **Grid UI with inline row edit.** Rows render read-only by default; clicking **Edit** converts the row into editable controls (dropdowns); **Save** commits, **Cancel** reverts. Add / Remove rows.
> 4. **Default grade from context.** Opened from a grade detail page, the context grade is pre-set — new grade-assignment rows default to it (§3.5).
> 5. **Backend change (overturns the v3 “zero backend changes” decision for this feature).** Grade-scoped subjects and teacher-activity assignments need new entities + endpoints (§4). Profile fields + qualifications chips are unchanged.
>
> **v3.1 — implementation-introduced changes (noted here per request):**
> 1. **Chips render BELOW the add-picker combobox** (was above) for the qualifications and grade-level multi-value fields. The add-affordance (combobox) sits at the top of the row/section; the selected chips fall beneath it. §3.3 / §3.5.
> 2. **Double-select guard + `@key` re-init.** `FluentCombobox` no longer sets `OptionValue` (the GUID id) — it binds `SelectedOption` by object only (`OptionText` = name), so the change handler matches by name. Its web component can still fire a second (stale) selection for a single interaction (upstream quirk, see PR #2855), so each add-picker is **re-keyed after every add** (`@key="_qualKey"` / `_topicKey` / `_gradeKey`, incremented in the pick handler) to force a clean re-init that wipes the input/active-option state, and the handlers early-return if the item is already selected. §3.3 / §7.
> 3. **Regression tests** guard both add-pickers against the double-select (§5).
>
> **v3 changes (decisions locked):**
> 1. **Grade section hidden entirely in grade context.** When the dialog is opened from a grade detail page (create *or* edit), the grade section is **not rendered at all** — no locked chip, no picker, no checklist. The grade link is implicit on save. (Resolves §8 Q1 — simplest option.)
> 2. **Size stays `DialogSize.Large` (720px).** No downsize to `Medium`. (Resolves §8 Q2.)
> 3. **`TeacherSetupWizard` retired now.** `/students/teachers/create` route removed; wizard component deleted. (Resolves §8 Q3.)
> **Owner:** Students context — `TeacherEditDialog` + its call sites
> **Depends on:** `grade-detail-rich-grids-plan.md` §5 (cg/6, original dialog), `documents/solution/dialog-consolidation-plan.md` (`DialogShellBase`), `section-card-lessons-adoption.md`, `dialog-ui` skill
> **Branch:** `feat/teacher-dialog-modernization` (suggested)

> **One dialog, two modes.** There is **no separate `TeacherCreateDialog`** — create and edit are the same `TeacherEditDialog` component, with the mode driven by `TeacherId` nullability (`null` = create). Create flows: `Teachers.razor` landing "+ New" (`OpenTeacherDialogAsync(null)`) and the grade-detail Teachers card "Add" (`TeacherId = (Guid?)null`). Every change in this spec therefore applies to **both** create and edit automatically; the §3.5 matrix distinguishes behavior by mode and launch context.

**v2 changes (review feedback):**

1. ~~Combined "Full name" field~~ → **First/Last stay separate inputs, rendered inline side-by-side under one `FormRow Label="Name"`.** No split heuristic; model and backend keep `FirstName`/`LastName` separate.
2. ~~FluentSelect pickers~~ → **`FluentCombobox` with type-ahead** for the qualifications / subjects / grade pickers (verified against pinned FluentUI 4.14.2: `FluentCombobox<TOption>` supports `Items`, `OptionText`, `OptionValue`, `SelectedOption(Changed)`, `Placeholder`, and `Autocomplete="ComboboxAutocomplete.List"` — the popup list filters as you type).
3. **Grade-context subject scoping:** when the dialog is opened from a grade detail page (create *or* edit), the subjects picker lists **only subjects enrolled for that grade** (§3.6).

## 0. Decisions locked

1. **No inner scroll regions.** `.qual-list`, `.topic-role-list`, `.check-list` (180px scroll boxes in `TeacherEditDialog.razor.css:11-18`) are deleted. Multi-value fields = chips/selected-rows + a single combobox add-picker, so height grows only with what is actually *selected*, not with catalog size.
2. **Name: one row, two inline fields.** `FormRow Label="Name" Required` wraps two side-by-side `FluentTextField`s (First name / Last name placeholders). Model/backend keep `FirstName`/`LastName` separate — zero split logic, zero contract change.
3. **Qualifications: chips + combobox.** Selected quals render via shared `Chip.razor` (dismiss removes); adding via `FluentCombobox<CodedValueDto>` over unselected options.
4. **Assignments grid (v4).** Subjects are **grade-scoped** — no standalone subject links. The dialog's subjects + grades sections become one **teaching-assignments grid** of rows, each being a *grade assignment* (grade + optional subject + role) or an *activity assignment* (activity + role + optional grades). Rows render read-only; Edit converts a row to editable inline (§3.4).
5. **Grade-context linking (v4).** `ContextGradeLevelId` / `ContextGradeLevelName` pre-set the default grade for new grade-assignment rows. From a grade detail page the context grade is the default (and can be left implicit on save if no other grade is set); the separate chips/checklist grade picker is removed (§3.5).
6. **Assignments data source (v4).** The grid loads the teacher's grade assignments (grade + subject + role) and activity assignments; the subjects add-picker is scoped to a chosen grade's enrolled subjects (§3.6).
7. **Shell migration** to `DialogShellBase<TeacherFormModel, Guid>` + `DialogShellFooter` + `EditForm` validation (repo convention; this dialog is the only hand-rolled form dialog left — 13 siblings already use the shell).
8. **Size** stays `DialogSize.Large` (720px) — no downsize.
9. **Backend change for v4.** New entities + endpoints for grade-scoped subjects and teacher-activity assignments (§4). This overturns the v3 “zero backend changes” decision; it is required to support `grade+subject+role` and `activity+role+grades` rows.

## 1. Goal

Modern, compact, context-aware teacher create/edit dialog:

- Fewer, denser inputs; no scrollbars for typical data volumes.
- Qualifications show **what is selected** as chips; teaching assignments (`grade + optional subject + role`, or `activity + role + grades`) render as an **inline-editable grid**.
- Type-ahead combobox pickers for the qualifications add-affordance; `FluentSelect` dropdowns inside the assignments grid.
- Launched from a grade page, the dialog **defaults new grade-assignment rows to that grade**; a grade row's subject choices are scoped to that grade's enrolled subjects.

## 2. Current-state analysis

| # | Problem | Where (today) | Fix |
|---|---------|---------------|-----|
| P1 | 8 stacked `FormRow` inputs + 3 scroll lists → dialog always scrolls | `TeacherEditDialog.razor:32-153` | §3.1–3.5; D1/D2 |
| P2 | Qualifications = raw `<input type="checkbox">` list in a 180px scroll box | `TeacherEditDialog.razor:65-88` | Chips + combobox, §3.3 |
| P3 | First/Last name = two stacked rows | lines 42-47 | Inline pair under one label, §3.2 |
| P4 | Subjects = full topic catalog with checkboxes + inline role dropdowns in a 180px scroll box; from a grade page it also offers subjects irrelevant to that grade | lines 107-123 | Assignments grid with **grade-scoped subjects**, §3.4/§3.6 |
| P5 | Grade detail "Add teacher" pre-checks a grade checkbox but still shows the whole grade checklist | `GradeLevels/Detail.razor:846` passes `InitialGradeLevelIds`; dialog lines 127-153 | Grid row with default grade, §3.5 |
| P6 | Hand-rolled shell/footer — inconsistent with the repo's `DialogShellBase` convention | lines 11, 156-165 | §3.7 |
| P7 | `DialogSize.Large` everywhere — oversized for slimmed content | 4 call sites | §3.8 |

**Reused as-is (no new shared components, no new packages):** `Chip.razor` (`SchoolCollab.Admin.Shared/Components`), `CodedValueDropdown` (per-subject role dropdown only), `FormRow`, `DialogShellBase` / `DialogShellFooter` / `ShowShellDialogAsync`, `FluentCombobox` (FluentUI 4.14.2).

## 3. Design

### 3.1 Layout — before → after

```
BEFORE                                AFTER
────────────────────────────          ─────────────────────────────────────
Profile                               Profile
  Title           [dropdown]            Title          [dropdown]
  First name *    [text]                Name *         [First name] [Last name]   <- inline pair
  Last name  *    [text]                Display name   [text]
  Display name    [text]                Gender         [dropdown]
  Gender          [dropdown]            Date of birth  [date]
  Date of birth   [date]                Level of educ. [dropdown]
  Level of educ.  [dropdown]            Qualifications [chip][chip] + [combo v]
  Qualifications  [180px chk list]    Teaching assignments (N)   <- v4 grid
Subjects (N)      [180px chk+role]      Grade:   [Grade v] [Subject v] [Role v]      [Edit] [Remove]
Grade levels (N)  [180px chk list]      Activity:[Activity v] [Role v] [Grades v]   [Edit] [Remove]
[Cancel][Save] hand-rolled            [ + Add assignment ]
                                      [Cancel][Save] DialogShellFooter
```

Section headers (`Profile` / `Teaching assignments (N)`) with their `FluentIcon`s are kept. `FormRow` remains the row primitive for the profile + qualifications; the **assignments section is a grid** (§3.4).

### 3.2 Name — one label, two inline fields (v2)

```razor
<FormRow Label="Name" Required For="teacherFirstName">
    <div class="name-fields">
        <FluentTextField Id="teacherFirstName" @bind-Value="Model.FirstName" Placeholder="First name" />
        <FluentTextField Id="teacherLastName"  @bind-Value="Model.LastName"  Placeholder="Last name" />
    </div>
</FormRow>
```

- `.name-fields { display: flex; gap: 0.5rem; }` with each field `flex: 1` — new rule in `TeacherEditDialog.razor.css` (merged, never overwritten per dialog-ui skill §3). `FormRow` explicitly supports multi-input cells (its own docs show two date pickers in one row).
- Model keeps `[Required] string FirstName` / `[Required] string LastName`; one asterisk on the shared label; `EditForm` + `DataAnnotationsValidator` surfaces failures. **No split/join logic anywhere.**

### 3.3 Qualifications — chips + combobox (v2)

Replaces `TeacherEditDialog.razor:65-88`:

```razor
<FormRow Label="Qualifications" AlignTop>
    <FluentCombobox TOption="CodedValueDto"
                    Items="@UnselectedQualifications"
                    OptionText="@(q => q.Name)"
                    SelectedOption="@_qualToAdd"
                    SelectedOptionChanged="@OnQualificationPicked"
                    Autocomplete="ComboboxAutocomplete.List"
                    Placeholder="Add qualification…" Height="240px" />
    <div class="chip-list">
        @foreach (var q in SelectedQualifications)
        {
            <Chip Label="@q.Name" OnDismiss="…remove…" />
        }
    </div>
</FormRow>
```

- **Chips render below the combobox (v3.1):** the add-picker is at the top of the row; the selected chips fall beneath it. Empty states: no options → muted text; all selected → muted *"All qualifications selected."* + chips; otherwise combobox first, then the selected chips.
- **No `OptionValue` (v3.1):** bind `SelectedOption` by object only. Setting `OptionValue` to the id breaks FluentCombobox's change-handler name match and can double-select (see v3.1 header notes + §7).
- `UnselectedQualifications` = dialog-loaded `_allQualifications` minus `_selectedQualificationIds` (the dialog already loads the list for chip labels — no extra fetch; `CodedValueDropdown` is not used here because it self-loads and has no exclusion parameter).
- `OnQualificationPicked`: add to set, then **reset `_qualToAdd = null`** so the combobox returns to its placeholder for the next add.
- Save contract unchanged: `QualificationIds()` → `Guid[]?` on the create/update requests.

### 3.4 Teaching assignments grid (v4)

Replaces the standalone subjects (`.topic-role-list`, `TeacherSubjectRoleFormFields`) and the grades chips/checklist with a single **teaching-assignments grid**. Subjects are grade-scoped — there are **no standalone subject links**.

**Grid (v4):** one row per assignment. Rows render read-only by default; **Edit** converts a row to editable inline; **Save** commits, **Cancel** reverts; **Add assignment** appends a new (editable) row; **Remove** deletes a row.

**Two row types:**

*Grade assignment — `grade + optional subject + role`:*
```
[Grade v]   [Subject (optional) v]   [Role v]   [Edit] [Remove]
```

*Activity assignment — `activity + role + optional grades`:*
```
[Activity v]   [Role v]   [Grades (multi) v]   [Edit] [Remove]
```

- The grid is a `FluentDataGrid` (or equivalent table) over an `AssignmentRow` model. Read-only cells show resolved names; edit mode swaps them for `FluentSelect` / `FluentCombobox` dropdowns (grade / subject / activity / role / grades).
- **Subject scope:** a grade row's subject dropdown is filtered to the selected grade's enrolled subjects (§3.6).
- **Role:** `CodedValueParent.TeacherRoles` (`TCHROLES`), optional.
- **Dates:** each row may carry optional `StartDate` / `EndDate` (existing `TeacherTopic` semantics); hidden by default, revealed in edit mode.
- **Add:** a toolbar button (Grade / Activity type choice) appends an empty editable row.

### 3.5 Assignment row model, grades & context default

```diff
- [Parameter] public IEnumerable<Guid>? InitialGradeLevelIds { get; set; }
+ [Parameter] public Guid? ContextGradeLevelId { get; set; }      // default grade when launched from a grade detail page
+ [Parameter] public string? ContextGradeLevelName { get; set; }  // caller-supplied; fallback: resolve from loaded list
```

Row model (nested in `TeacherFormModel`):

```csharp
public sealed record AssignmentRow(Guid Id, AssignmentType Type, Guid? GradeLevelId,
    Guid? SubjectId, Guid? ActivityId, Guid? RoleCodedValueId, HashSet<Guid> GradeLevelIds);
public enum AssignmentType { Grade, Activity }
```

- **Grade row:** `GradeLevelId` required; `SubjectId` optional; `RoleCodedValueId` set via the role dropdown.
- **Activity row:** `ActivityId` required; `RoleCodedValueId`; `GradeLevelIds` optional multi (0..n).
- The old grade **chips / checklist picker is removed** — grades are captured inside the grid (the grade column of grade rows, and the multi-select on activity rows).

**Entity mapping (confirmed from repo):** a grid row maps to an existing or new backend link per row type:

| Row | Backing entity | Status |
|---|---|---|
| Grade row, no subject (`grade + role`) | `TeacherGradeLevel` (TeacherId, GradeLevelId, TeacherRoleCodedValueId) | **exists** — reuse |
| Grade row, with subject (`grade + subject + role`) | `TeacherGradeTopic` (TeacherId, GradeLevelId, TopicId, RoleCodedValueId, StartDate, EndDate) | **new** — grade-scoped subject; the current `TeacherTopic` is standalone (no `GradeLevelId`), so it cannot express grade-scoped subjects |
| Activity row (`activity + role + grades`) | `TeacherActivityAssignment` (TeacherId, ActivityGroupId, RoleCodedValueId) + `TeacherActivityAssignmentGrade` join (TeacherActivityAssignmentId, GradeLevelId) | **new** — no teacher→activity link exists |

**Backend implication (confirmed, option A):** grade+role rows are UI-only on the existing `TeacherGradeLevel`; grade+subject and activity rows need the two new entities + endpoints + a migration.

**Context default grade:**

| Launch point | `TeacherId` | `ContextGradeLevelId` | Assignments grid behavior |
|---|---|---|---|
| Teachers landing "+ New" | `null` | `null` | Empty grid; Add rows manually |
| **Grade detail › Teachers card › Add** | `null` | current grade | One **grade-assignment row pre-created** with the context grade; new grade rows default to it |
| Teacher detail "Edit" / landing kebab "Edit" | id | `null` | Existing rows preloaded |
| Grade detail kebab "Edit" | id | current grade | Existing rows preloaded; context grade is the default for new grade rows |

- *Create + context save:* the pre-created grade row (or any row whose grade equals the context grade) persists the context-grade link.
- *Grade section hidden entirely (v4):* the separate grade section is gone; grades live in the grid. In grade context the context grade is the default, but the grid still lets the user add the teacher to other grades too.

### 3.6 Assignments data source (v4)

- **Grade assignments:** `ListTeacherGradeAssignmentsAsync(teacherId)` (new) returns the combined grade rows — grade-only rows from `TeacherGradeLevel` (grade + role) and grade+subject rows from `TeacherGradeTopic` (grade + subject + role). Each row carries its resolved grade/subject/role names plus the entity ids.
- **Activity assignments:** `ListTeacherActivityAssignmentsAsync(teacherId)` (new) returns activity rows (activity + role + grades), grades resolved from the `TeacherActivityAssignmentGrade` join.
- **Subject dropdown scope:** a grade row's subject dropdown is filtered to `ListGradeTopicsByGradeAsync(gradeLevelId)` (the grade's enrolled subjects), names resolved from the loaded `_allTopics`. Rows without a chosen grade offer no subject until a grade is chosen.
- **Catalog loads:** `_allTopics`, `_allGradeLevels`, `_allActivities` (`ListActivityGroupsAsync`), qualifications — all loaded up front.
- **Empty states:** no assignments → empty grid + "Add assignment" button; a grade with no enrolled subjects → its subject dropdown is empty (muted *"No subjects are assigned to this grade yet."*).

### 3.7 Dialog shell migration

- `@inherits DialogShellBase<TeacherFormModel, Guid>`; nested `TeacherFormModel` (plain class, two-way bound): context fields (`TeacherId`, `ContextGradeLevelId/Name`) + profile fields + `List<AssignmentRow> Assignments`.
- `EditForm` + `DataAnnotationsValidator`; `[Required]` on `FirstName` / `LastName`.
- Footer → `<DialogShellFooter Saving Error OnCancel SubmitText …/>` (*"Create Teacher"* / *"Save Changes"* by mode). No `<FluentDivider>`.
- Opened via `ShowShellDialogAsync<TeacherEditDialog, TeacherFormModel, Guid>(…)`; returns `Guid?` (`null` = cancelled).
- **Save reconciliation (v4):** diff `Assignments` against the loaded originals per row type —
  - **Grade row, no subject:** upsert/delete `TeacherGradeLevel` (grade+role) via existing `LinkTeacherGradeLevelAsync` / `SetTeacherGradeLevelRoleAsync` / `UnlinkTeacherGradeLevelAsync`.
  - **Grade row, with subject:** upsert/delete `TeacherGradeTopic` (grade + subject + role) via new grade-scoped-subject endpoints.
  - **Activity row:** upsert/delete `TeacherActivityAssignment` (+ grades) via new activity-assignment endpoints.
  - Context grade is force-included on create (§3.5).
  - In grade context, a grade-only row with the context grade (no subject) is the implicit link; if no grade-only context row exists, one is created on save.

### 3.8 Call sites

| File | Change |
|---|---|
| `Components/Pages/Teachers/Teachers.razor:139-154` | `ShowShellDialogAsync`, size `Large`, `Guid?` result; post-save navigation unchanged |
| `Components/Pages/Teachers/TeacherDetail.razor:170-184` | Same swap; reload-on-save unchanged |
| `Components/Pages/Students/GradeLevels/Detail.razor:836-873` | Both handlers: shell + `Large` + context params (§3.5); `ReloadTeachersAsync` on non-null result unchanged |

## 4. Files touched

| File | Change |
|---|---|
| `src/Students/SchoolCollab.Students.Application/Components/Students/TeacherEditDialog.razor` | Rewritten per §3 (shell, model, inline name, chips/combobox qualifications, **assignments grid**) |
| `…/TeacherEditDialog.razor.css` | **Merge, never overwrite** (dialog-ui skill §3): remove `.topic-role-list` / `.check-list` scroll rules; add `.name-fields`, `.chip-list`, `.assignment-grid` / `.assignment-row` / `.assignment-row--editing` styles. (No `.grade-context` — grades now live in the grid, v4.) |
| `Components/Pages/Teachers/Teachers.razor`, `TeacherDetail.razor`, `GradeLevels/Detail.razor` | §3.8 (context params → default grade) |
| `Components/Pages/Teachers/TeacherSetupWizard.razor` + `/students/teachers/create` route | **Deleted** (v3) — wizard retired; landing already uses the dialog |
| `src/Students/SchoolCollab.Students.Core/Domain/TeacherGradeTopic.cs` (**new**) | grade-scoped subject link — `Id, TeacherId, GradeLevelId, TopicId, RoleCodedValueId?, StartDate, EndDate?` + tenant/audit/rowversion. So a subject is **tied to a specific grade** (the current `TeacherTopic` has no `GradeLevelId`). |
| `src/Students/SchoolCollab.Students.Core/Domain/TeacherActivityAssignment.cs` (**new**) | teacher-activity link — `Id, TeacherId, ActivityGroupId, RoleCodedValueId?` + tenant/audit/rowversion. Grades via a join table. |
| `src/Students/SchoolCollab.Students.Core/Domain/TeacherActivityAssignmentGrade.cs` (**new**) | join — `TeacherActivityAssignmentId, GradeLevelId` (0..n grades per activity row). |
| `…/Data/Configurations/TeacherGradeTopicConfiguration.cs`, `TeacherActivityAssignmentConfiguration.cs` (**new**) | EF configs for the new entities + join table. |
| `…/Students.Core/Migrations/<new>` (**new**) | add `TeacherGradeTopic`, `TeacherActivityAssignment`, `TeacherActivityAssignmentGrade` tables. |
| `…/Students.Api/Endpoints/TeacherRoutes.cs` + CQRS (**new**) | grade-scoped subjects + activity assignments (see contract below) |
| `…/Students.Application/Services/StudentsApiClient.cs` | add client methods for the new endpoints |
| `tests/SchoolCollab.Admin.Tests.Unit/TeacherEditDialogBunitTests.cs` | Rewritten (§5) |
| `tests/SchoolCollab.Admin.Tests.Unit/GradeLevelDetailPageTests.cs:426-460` | Wiring assertions updated (context params, shell open, `Large`) |

**Backend scope (v4):** new entities + endpoints above; a migration adds the `TeacherGradeTopic`, `TeacherActivityAssignment`, and `TeacherActivityAssignmentGrade` tables. This overturns the v3 “no backend files” note and is required for `grade+subject+role` and `activity+role+grades` rows.

**Endpoint contract (new):**

```
GET    /teachers/{id}/grade-assignments          → combined grade rows (grade-only from TeacherGradeLevel, grade+subject from TeacherGradeTopic)
POST   /teachers/{id}/grade-assignments          → upsert one grade row { gradeLevelId, subjectId?, roleCodedValueId?, startDate?, endDate? }
DELETE /teachers/{id}/grade-assignments/{rowId}  → delete the row (TeacherGradeLevel or TeacherGradeTopic)

GET    /teachers/{id}/activity-assignments        → activity rows (activity + role + gradeIds)
POST   /teachers/{id}/activity-assignments        → upsert one activity row { activityGroupId, roleCodedValueId?, gradeLevelIds[] }
DELETE /teachers/{id}/activity-assignments/{rowId} → delete the activity row + its grades
```

Client methods: `ListTeacherGradeAssignmentsAsync`, `UpsertTeacherGradeAssignmentAsync`, `DeleteTeacherGradeAssignmentAsync`, `ListTeacherActivityAssignmentsAsync`, `UpsertTeacherActivityAssignmentAsync`, `DeleteTeacherActivityAssignmentAsync`.

## 5. Tests

`TeacherEditDialogBunitTests` (Fluent inputs render in shadow DOM → assert structure + local-list state per the preflight rule *"every mutation handler must update the component's own list state"*):

- **Name row:** one `Name` label row containing two text fields; edit prefills both; saving with either blank → validation error, dialog stays open.
- **Qualifications:** chips render selected; combobox items exclude selected; pick adds a chip + resets the combobox; chip dismiss removes the chip **and** returns the option to the combobox. Chips render **below** the combobox.
- **Combobox double-select regression (v3.1):** simulating a single pick of *"Life Sciences Teaching"* adds **exactly one** chip — the sibling *"Languages Teaching"* is **not** pulled in.
- **Assignments grid (v4):** grade rows render read-only; **Edit** converts a row to editable (grade/subject/role dropdowns); **Save** commits, **Cancel** reverts; **Add assignment** appends a row; **Remove** deletes; `Assignments (N)` count tracks.
- **Two row types (v4):** grade row = grade + optional subject + role; activity row = activity + role + optional grades. A grade row's subject dropdown is scoped to the grade's enrolled subjects (mock `GET /students/topic-assignments/by-grade/{id}`).
- **Context default grade (v4):** with `ContextGradeLevelId` set, a grade-assignment row is pre-created with the context grade; new grade rows default to it. Context create save = `POST /teachers` + the grade / grade-scoped-subject / activity link calls for the grid rows.
- `GradeLevelDetailPageTests`: wiring assertions — `ContextGradeLevelId` / `ContextGradeLevelName` passed, shell open, `DialogSize.Large`.

HTTP contract coverage for the new grade-scoped-subject and teacher-activity endpoints stays in the new client + CQRS tests (§4).

## 6. Out of scope

- `TeacherSubjectsDialog` / `TeacherSubjectRoleFormFields` (per-topic dates editor) — **superseded** for the create/edit dialog by the v4 assignments grid; the kebab "Subjects" surface stays for the existing standalone (topic, role, dates) flow unless separately retired.
- `FluentAutocomplete` (multi-select token box) — rejected: zero repo usage; the assignments grid + `FluentSelect`/`FluentCombobox` are the house pattern.
- Backend detail beyond the new entities/endpoints in §4 (e.g. exact migration columns, CQRS handler internals) — left to the entity/endpoint design pass, not this UI spec.

## 7. FluentUI 4.14.2 usage rules (verified against the package)

- `FluentCombobox<TOption>`: `Autocomplete` accepts `ComboboxAutocomplete.Inline` / `.List` / `.Both` — we use **`List`** (filters the popup as you type, no inline completion). The combobox renders **no chips/tokens itself** — selected items stay our shared `Chip`.
- **Never set `OptionValue` on these add-pickers (v3.1).** `FluentCombobox.ChangeHandlerAsync` matches the typed/selected `value` against `GetOptionText(i)` (the **name**). If you set `OptionValue` to the id, that match fails, the combobox resets mid-change, and a single selection can pull in a second option. Bind `SelectedOption` by object (`OptionText` = display name) and reset the bound field to `null` in the change handler. `CodedValueDropdown` (per-subject role picker) is unaffected — it binds `SelectedId` (a `Guid?`) via `FluentSelect`, not a combobox.
- **Re-key each add-picker after every add (v3.1).** The FluentCombobox web component can fire a second, stale selection for one interaction (a known upstream quirk — FluentUI PR #2855 fixed a related double `ChangeHandlerAsync` call). Set `@key="_qualKey"` / `_topicKey` / `_gradeKey` on the pickers and increment the counter in the pick handler; re-keying forces a full re-init that clears the input/active-option state so no stale value survives to be re-added. Pair it with an idempotent handler that early-returns when the item is already selected.
- Combobox `Appearance` is a `FluentInputAppearance` value (input components: `Outline` default / `Filled`) — never `Appearance.Accent`.
- `FluentBadge` (inside `Chip`) only allows `Accent` / `Lightweight` / `Neutral` — `Chip.razor` already uses `Lightweight`.
- Buttons: `Accent` (submit) / `Outline` (cancel); never `Filled` / `Hypertext`.

## 8. Resolved decisions (v3)

1. **Grade-context edit** — **hide the grade section entirely** in grade context (simplest). Context grade link implicit on save; other linked grades managed from the teacher detail / landing edit surface.
2. **Size** — keep **`DialogSize.Large`** (720px); no downsize.
3. **Wizard route** — **retire `/students/teachers/create` now**; delete `TeacherSetupWizard` and its route.
