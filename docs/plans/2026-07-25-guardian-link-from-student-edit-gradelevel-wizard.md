# Plan: Guardian Link on Student Edit + GradeLevel Wizard Enhancement

**Date:** 2026-07-25
**Status:** PLAN (not yet implemented)
**Branch target:** feature branch off `main`, squash-merge via PR
(repo convention: push with `SCHOOLCOLLAB_ALLOW_PUSH=1`, wait for
Build & Test CI, then squash-merge).

---

## 1. Goal

Define a unified rule for **how guardian linking and editing are surfaced in
the admin UI** that the rest of the app can follow without further debate:

> **From inside a dialog** (e.g. `GuardianPickerDialog` opened from the
> `GradeLevelWizard`), guardian linking keeps the existing
> **panel-switch / same-dialog-screen UX** (e.g. "New guardian" inside
> `GuardianPickerDialog` reveals a sub-form within the same dialog; "By
> contact" / "By student" tabs inside the picker switch the same dialog
> to a different search mode).
>
> **From inside a page** (e.g. `/students/{id}/edit`, `/students/{id}` view,
> future `/students/{id}/guardians` or `/guardians` landing), guardian
> linking and per-guardian editing is done in a **modal dialog** opened
> from the page.

The two contexts have different ergonomics: inside a wizard/dialog, the
user is already modal and committed to a multi-step flow, so an
in-place panel switch keeps them oriented; on a page, the user expects
to dive into a record, edit, and come back without re-navigating the
wizard — a modal dialog is the right primitive.

The page-side work unifies three currently-different guardian experiences
on `/students/{id}/edit`:

- the `StudentFormFields` guardian section (already a panel switch)
- `Detail.razor` → `GuardiansTab` (a collapsible link form with no
  per-guardian edit / no relationship / no emergency-contact support on
  the link row)
- the upcoming `/students/{id}/view` and any guardian landing page

after this plan they all open the same `GuardianPickerDialog` /
`GuardianFormDialog` (or a leaner page-side equivalent) for the add /
edit / link / unlink actions.

---

## 2. Key codebase facts (research)

### 2.1 Current guardian-UI surface

| Component | File | Pattern | Edit? | Add new? | Add existing? |
| --- | --- | --- | --- | --- | --- |
| `GuardianAssignmentList` | `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/GradeLevels/GuardianAssignmentList.razor` | Wizard step; emits `OnRequestAdd` / `OnRequestEdit` events | Calls `GuardianFormDialog` (dialog) | Calls `GuardianPickerDialog` (dialog) | Inside `GuardianPickerDialog` (panel switch) |
| `StudentFormFields` (guardian section) | `src/Students/SchoolCollab.Students.Admin/Components/Students/StudentFormFields.razor` | Inline panel switch inside the long student form | none (drafts only) | Inline `<GuardianFormFields>` panel | Inline `EntityGrid` "By contact" / "By student" panels |
| `GuardiansTab` | `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardiansTab.razor` | Collapsible single-row "Link a guardian" form, grouped-by-role list with delete-only | none | none (links existing tenant guardian only) | Single `FluentSelect` of all `ListGuardiansAsync` not already linked |
| `GuardianDetail.razor` | `src/Students/SchoolCollab.Students.Admin/Components/Pages/Guardians/GuardianDetail.razor` | Page with inline edit form; no link-from-guardian UI for new students | Inline `SaveEdit` form | "Add a ward" form (single, not multi) | n/a (link only) |
| `GradeLevelWizard.razor` (Step "Guardians") | `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/GradeLevels/GradeLevelWizard.razor` | Wizard step rendering `GuardianAssignmentList` | opens `GuardianFormDialog` | opens `GuardianPickerDialog` | inside `GuardianPickerDialog` |

### 2.2 Dialog primitives (already in place)

| File | Role |
| --- | --- |
| `src/SchoolCollab.Admin.Shared/Components/Dialogs/DialogShellBase.cs` | `ComponentBase` + `IDialogContentComponent<DialogShellData<TModel>>`; owns saving/error state, `SubmitAsync`, `HandleSubmitAsync`/`HandleCancelAsync`. **No markup.** |
| `src/SchoolCollab.Admin.Shared/Components/Dialogs/DialogShellFooter.razor` | Shared footer: error `MessageBar` + Cancel/Save `.button-row` with `border-top` separator. |
| `src/SchoolCollab.Admin.Shared/Components/Dialogs/DialogServiceExtensions.cs` | `ShowShellDialogAsync<TComponent, TModel, TResult>(model, title, size)` — opens with the four constant `DialogParameters` (`PrimaryAction = null`, `SecondaryAction = null`, `PreventDismissOnOverlayClick = true`, `Width = size.ToCssWidth()`). |
| `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardianFormDialog.razor` | Existing shell-derived dialog. ForAdd / ForEdit; uses `GuardianFormFields` (presentational). |
| `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardianPickerDialog.razor` | Existing shell-derived dialog. Search + multi-select + chip list + "New guardian" button that itself opens `GuardianFormDialog` (nested). |

Both guardian dialogs already follow the dialog-shell conventions, so
**any new page-side dialog reuses them as-is** — the only new work is
the page-side caller wiring.

### 2.3 Page-side conventions

- `/students/{id}/edit` (`Edit.razor`) uses `StudentFormFields` with
  `ShowGuardians="true"`, `RenderActions="false"`, the right-column
  Save / Cancel sidebar, and `OnValidSubmit="OnSaveAsync"`. The
  guardian section inside `StudentFormFields` is an inline
  panel-switch (NOT a dialog) — this is the inconsistency the user is
  calling out.
- `/students/{id}` (`Detail.razor`) is a single-page view with
  embedded `GuardiansTab` and `GuardianContactsList` components.
  `GuardiansTab` is a collapsible "Add a guardian link" form that
  only links existing tenant guardians and has no per-row edit /
  no relationship / no emergency-contact UI; the rest of the list
  is just name + role + emergency badge + delete.
- `EnrollStudentDialog`, `StudentTransferDialog`, `WithdrawEnrollmentDialog`
  are the page-side dialog pattern reference: opened via
  `ShowShellDialogAsync<TComponent, TModel, TResult>(...)`, returning
  a typed result, refreshing the page state on non-null. The
  guardian dialogs should be invoked the same way from page-side
  callers.

### 2.4 Domain & API (already in place)

- `StudentsApiClient` exposes
  `CreateGuardianAsync(CreateGuardianRequest)`,
  `LinkGuardianAsync(LinkGuardianRequest)`,
  `UnlinkGuardianAsync(studentId, guardianId)`,
  `ListGuardiansByStudentAsync(studentId, ct, search)`,
  `ListGuardiansAsync(ct, search)`. All needed for the page-side
  actions — no backend change.
- `GuardianAssignment` (record, in
  `Components/Pages/Students/GradeLevels/GuardianAssignment.cs`) is
  the wizard's per-link draft payload (carries `ExistingGuardianId`,
  name, relationship id, role, contact channel/value, title id). The
  page-side dialog will return a similar payload so the caller can
  drive the same create-then-link flow as the wizard's Phase 4.
- `StudentGuardianViewDto` is the read shape returned by
  `ListGuardiansByStudentAsync` — already used by
  `GuardiansTab.razor` and `StudentFormFields.razor`.

---

## 3. Design decisions

### 3.1 From dialogs (wizard / picker / nested), keep the panel-switch UX

Inside `GuardianPickerDialog` and inside the wizard's "Guardians" step,
the user is already inside a wizard / multi-step flow. Switching to a
nested dialog for "New guardian" is the right move ONLY when the user
needs to be in a separate visual context to do it (which is why
`GuardianPickerDialog` already does this — see
`OnCreateNew` calling `ShowShellDialogAsync<GuardianFormDialog,...>`).

But the **guardian-list editing** in the wizard stays inline (a
panel switch inside the wizard step, like the student edit already
does at `GradeLevelWizard.razor:362-381`): no change to the
`GuardianAssignmentList` "Add guardian" / "Edit" buttons. They keep
opening `GuardianPickerDialog` and `GuardianFormDialog` respectively,
which is already a dialog-from-wizard flow, not a panel switch — the
exception, not the rule, and the rule still holds.

The user is clarifying the rule, not requesting wizard changes:
> "From dialogs, it should be as it is — switching screens on same
> page/dialog."

Concretely, the only place inside a dialog that currently uses a panel
switch for "New guardian" is `StudentFormFields` (the
`GuardianPanelMode.New` / `.Existing` enum). When this surface is
called from **a dialog** (e.g. the wizard's inline "new student"
form, the `StudentPickerDialog` create flow), the existing
panel-switch behaviour stays. When called from a **page**, the
panel switch is replaced by the dialog UX in §3.2 below.

### 3.2 From pages, open a modal dialog for guardian linking / editing

Every page that currently shows a guardian section switches to:

- **Add existing guardian** → opens `GuardianPickerDialog` in
  `DialogSize.Large` (matches the wizard), returns
  `GuardianAssignment[]?` (null = cancel). On a non-null result the
  page links each returned `GuardianAssignment` to the student
  immediately (`LinkGuardianAsync`). For an existing guardian picked
  from the grid, `ExistingGuardianId` is the GuardianId; for a
  guardian created inside the picker via "New guardian", the returned
  `GuardianAssignment` has `ExistingGuardianId` = the newly-created
  GuardianId (the picker resolves it — see
  `GuardianPickerDialog.OnCreateNew` / `SubmitAsync`).
- **Add new guardian inline on the page** → opens `GuardianFormDialog`
  in `DialogSize.Medium`, returns `GuardianAssignment?`. On a
  non-null result the page creates the guardian then links it
  (`CreateGuardianAsync` → `LinkGuardianAsync`).
- **Edit an existing linked guardian** (e.g. change relationship,
  set emergency contact, edit contact value) → opens
  `GuardianFormDialog` in `DialogSize.Medium` with
  `GuardianAssignmentModel.ForEdit(existing)`, returns
  `GuardianAssignment?`. On non-null the page either updates the
  link metadata (relationship / emergency contact — backend route
  decision below) or, if the user changed the name/title, calls
  `UpdateGuardianAsync` + the link-update endpoint.
- **Unlink a guardian** → stays as the existing per-row delete button
  (calls `UnlinkGuardianAsync` and reloads). No dialog needed — the
  action is a single API call and is reversible by re-adding.

`Detail.razor` and `Edit.razor` both already have a working
`DialogService` injection (used for `EnrollStudentDialog` /
`StudentTransferDialog` / `WithdrawEnrollmentDialog`) — no DI change.

### 3.3 Reuse `GuardianPickerDialog` and `GuardianFormDialog` as-is

The two dialogs are already shell-derived, follow the dialog-ui
conventions (`.button-row` with `border-top`, `ShowShellDialogAsync`
open, `DialogShellFooter`), and already accept a per-context
`GuardianAssignment` model. **No dialog markup change is needed** for
the page-side callers. The wizard stays on the same dialogs too —
this is a pure caller-side unification.

The two dialogs' existing behaviour:

- `GuardianFormDialog` collects Title / First / Last / Relationship /
  Contact channel / Contact value via the shared
  `GuardianFormFields`; returns a `GuardianAssignment` whose
  `ExistingGuardianId` is null on Add or the picked guardian's id on
  Edit.
- `GuardianPickerDialog` is a server-filtered multi-select grid of
  tenant guardians + a "New guardian" inline button that itself
  opens `GuardianFormDialog`. The submit result combines picked +
  created-in-dialog into a single `GuardianAssignment[]` where each
  entry carries the (possibly newly created) `ExistingGuardianId`.

This already matches what the page-side flow needs; the only change is
where they're opened from.

### 3.4 Replace the inline `StudentFormFields` guardian section when used standalone on a page

The `StudentFormFields` guardian section is the panel-switch UX
(dialogs-style, but it's inside the long form). On a page, this
pattern fights the user:

- A second "Add existing" grid (By contact / By student) inside a
  page-level form duplicates the picker dialog.
- A second "New guardian" panel inside a page-level form duplicates
  the form dialog.
- The page-level student edit's "Save" button also commits the
  guardian list — but a linked guardian created on the page should
  persist immediately, not wait for the student save (the user might
  navigate away from the edit page with the student not yet saved,
  losing the links).

`StudentFormFields` therefore gets a new **`ShowGuardians`** mode for
the **edit-page** call site: instead of an inline section, it renders
**"Guardians (N) — Manage…"** as a small section header + a button
that opens `GuardianPickerDialog`. The section header surfaces the
current count + a "View all" link to a per-student guardians
sub-page (out of scope for this plan; tracked in §5).

When used inside a dialog (e.g. the `StudentPickerDialog` "create
new student" inline form, the wizard's Step 2 inline edit), the
existing `ShowGuardians` behaviour (inline panel switch) is
preserved — the user said the dialog UX should be unchanged. This is
gated by a new optional `[Parameter] Mode` (default `Inline`):
`Inline` = current behaviour (panel switch), `Linked` = page
behaviour (manage button + count + view-all link).

The Mode parameter is intentionally simple (a string enum, not a
strategy chain) because there are only two callers, and the
inline-mode code path is the historical default that the wizard
already depends on.

### 3.5 `Detail.razor` (`/students/{id}` view) guardian section is also dialog-driven

`Detail.razor` today embeds `GuardiansTab`, which has a collapsible
"Add a guardian link" form. That tab is also replaced by a
"Manage guardians" button that opens `GuardianPickerDialog`, plus a
read-only list rendered as the existing `EntityGrid<TItem=...>` of
links (with an "Edit" button per row that opens `GuardianFormDialog`
in `ForEdit` mode against the existing link's data).

`GuardiansTab.razor` is deprecated: it stays in the codebase (for
back-compat with any external embed) but is no longer rendered by
`Detail.razor`. The component file gets a top-of-file comment marking
it as "deprecated; use GuardianPickerDialog + a page-side link list
instead."

### 3.6 `GuardianDetail.razor` (guardian page) already has an inline edit form — no change required

`GuardianDetail.razor` is the per-guardian page (not a
student-centric surface). It already has an inline edit form for
the guardian's own profile and a "link a new ward" form. The
"link a new ward" form is a single-ward link (pick one student +
role) — it stays inline because it's a single-action surface and
the user is on the guardian's page, not the student's.

The only consistency improvement: replace the "link a new ward" form
with the same `GuardianPickerDialog`-from-the-other-side flow? **No.**
The user is on a guardian page; the picker model assumes the
**caller is the student** (it returns a `GuardianAssignment[]` of
guardians-to-link). The reverse direction (pick a student to link a
guardian to) is a different shape (one student + role + relationship
+ emergency flag) and the inline form is already correct. This plan
does NOT touch `GuardianDetail.razor`.

### 3.7 Save semantics on `Edit.razor` (page-side)

The page-side "Add / Edit / Unlink" actions are **immediate**: each
one calls the relevant API on confirm and reloads the guardian list.
The right-column page-level "Save" still saves identity fields only
(first/last/DOB/gender), not the guardian list — guardians are
already persisted by the time the user clicks page-level Save. This
matches `Detail.razor`'s current semantics (`UnlinkGuardianAsync` is
immediate, not deferred to a page-level Save).

`Create.razor` (create mode) is unchanged: drafts on
`StudentFormModel.GuardianLinks` are linked after the student is
created by `Create.razor.OnSaveAsync`. The wizard's Phase 4 also
stays unchanged.

---

## 4. Implementation steps

### Part A — `StudentFormFields` mode split

**Files:**
`src/Students/SchoolCollab.Students.Admin/Components/Students/StudentFormFields.razor`,
`StudentFormFields.razor.css`

1. Add a `StudentFormFieldsMode` enum (or `string`) parameter:
   `Mode` defaulting to `Inline` (current behaviour).
2. In `Mode == Linked`, replace the inline section body with:
   - A section header "Guardians (<count>)" with a "Manage" button
     that fires `EventCallback OnManageGuardians` (new parameter).
   - A read-only `EntityGrid` of current links (mirrors the
     `GuardiansTab` view: First/Last/Relationship/Emergency/Delete
     button) — presentational only, no inline create.
   - The count is `_links.Count` in edit mode or
     `Model.GuardianLinks.Count` in create mode.
3. Delete the panel-switch state machine
   (`_panelMode`, `_existingTab`, `_pickerRows`, `_selectedRows`,
   `_studentResults`, `_selectedStudentId`, `_newGuardian`,
   `OnContactSearchAsync`, `OnStudentSearchAsync`,
   `SelectStudentAsync`, `SaveNewGuardianAsync`,
   `AddSelectedGuardiansAsync`, `BackToStudentSearch`,
   `SwitchExistingTab`, `StartNewGuardian`, `StartAddExisting`,
   `CancelPanel`) **only in `Mode == Linked`**. In `Mode == Inline`
   (dialogs) the code is preserved exactly as it is today.
4. Keep the existing `OnRemoveGuardian` event callback so the page
   can call `UnlinkGuardianAsync` + reload without the field owning
   the API call.
5. Add `OnManageGuardians` parameter (EventCallback). When invoked,
   the page opens `GuardianPickerDialog` (and on non-null result
   performs the link + reload — see Part B).
6. CSS: add a `.student-form-guardians--linked` class for the
   compact "Guardians (N) — Manage" layout (smaller section than
   the dialog mode; see `StudentFormFields.razor.css` for the
   existing `.student-guardians` block).

**Why Mode-gate rather than split the component:** the inline-mode
code path has 11+ private members that are referenced by the panel
flow and the create-mode flow; splitting would require
duplicating ~200 lines. A single component with a Mode parameter
keeps the diff small and the wizard's call site (which uses
`Mode = Inline`, the default) unchanged.

### Part B — `Edit.razor` (`/students/{id}/edit`) page-side wiring

**File:** `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/Edit.razor`

1. In `OnInitializedAsync`, after the student + enrollments load,
   pre-load the link list (currently inside
   `StudentFormFields`; the page now owns it because the form is
   mode-gated and the page is the one calling
   `LinkGuardianAsync` / `UnlinkGuardianAsync` in `Linked` mode).
   Replace the existing `LoadGuardiansAsync` inside
   `StudentFormFields` with a `LoadGuardiansAsync` on `Edit.razor`
   that calls the same API and stores the result on `_links` /
   `_relNames` on the page.
2. Pass `Mode="Linked"` + `ShowGuardians="true"` to
   `StudentFormFields` and drop the page's reliance on
   `StudentFormFields` for guardian state.
3. Add `OnManageGuardians` handler on the page:
   - Calls `DialogService.ShowShellDialogAsync<GuardianPickerDialog,
     GuardianPickerModel, GuardianAssignment[]>(new
     GuardianPickerModel(), "Manage guardians",
     DialogSize.Large)`.
   - On non-null result, for each `GuardianAssignment` in the
     result: if `ExistingGuardianId` is set, call
     `LinkGuardianAsync(new LinkGuardianRequest(StudentId,
     ExistingGuardianId, RelationshipCodedValueId, Role,
     IsEmergencyContact, null))`. If null, call
     `CreateGuardianAsync(...)` then `LinkGuardianAsync(...)`.
   - On completion, call `LoadGuardiansAsync` to refresh the page.
4. Add an `OnRemoveGuardianFromPage` handler on the page:
   - Calls `UnlinkGuardianAsync(StudentId, guardianId)`.
   - Calls `LoadGuardiansAsync` to refresh.
5. Move `_links`, `_relNames`, `_guardianError`, the load / refresh
   helpers, and the create+link / unlink logic from
   `StudentFormFields` to `Edit.razor`. The form component in
   `Mode == Linked` is now a thin presenter (count + Manage button
   + read-only grid + delete-per-row).
6. Inject `DialogService` and `StudentsApiClient` (already
   injected) into `Edit.razor`. No new using directives
   (`SchoolCollab.Students.Admin.Components.Pages.Students.GradeLevels`
   for `GuardianAssignment` is already imported via the namespace
   on the existing model).
7. On the page-level "Save" button, the behaviour is unchanged
   (saves identity fields only). The guardian section is
   self-persisting and independent of the page-level save.

### Part C — `Detail.razor` (`/students/{id}` view) page-side wiring

**File:**
`src/Users/skwar/source/repos/School-Collab/src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/Detail.razor`

1. Replace `<GuardiansTab StudentId="Id" />` with an inline
   `EntityGrid` of `_links` (or a new presentational component
   `StudentGuardiansList` that the wizard also uses — see Part E).
2. Add a "Manage" button in the section header (next to the
   existing `<h3>Guardians</h3>`).
3. Wire `OnManageGuardians` / `OnEditGuardian` / `OnRemoveGuardian`
   exactly as `Edit.razor` does (Parts B.3 / B.4). The dialog
   service and `StudentsApiClient` are already injected.
4. Add `OnEditGuardian` handler: opens
   `GuardianFormDialog` with
   `GuardianAssignmentModel.ForEdit(assignment)`; on non-null result,
   re-link / re-update. The link-metadata update (relationship,
   emergency contact, role) goes via a single new endpoint
   `PUT /students/api/students/{studentId}/guardians/{guardianId}`
   (see Part D).
5. Add a per-row "Edit" `FluentButton` (Lightweight,
   `IconStart="@FluentIcons.Edit"`) in the `EntityGrid` row's
   trailing cell. The row's `Edit` click invokes
   `OnEditGuardian(link)`.

### Part D — Backend: link metadata update endpoint (small)

**Files:**
`src/Students/SchoolCollab.Students.Core/CQRS/Guardians/Commands/UpdateStudentGuardian/`,
`src/Students/SchoolCollab.Students.Api/Endpoints/StudentGuardianRoutes.cs`

The current `LinkGuardianAsync` is a POST that creates the link.
Per-page edit (relationship / role / emergency contact) needs an
update path. Two options:

1. **Unlink + re-link** (no new endpoint). On Edit, the page
   calls `UnlinkGuardianAsync` then `LinkGuardianAsync` with the
   new values. Simple, works today, but emits two events / two
   rows in the audit log instead of one.
2. **New `UpdateStudentGuardianCommand` + PUT endpoint** (preferred).
   Updates `RelationshipCodedValueId`, `Role`,
   `IsEmergencyContact` in place; emits a single
   `StudentGuardianUpdatedEvent`; preserves `CreatedAt` /
   `CreatedByGuardianId` (audit history). One row, one event.

**Decision: option 2.** The audit / event-sourcing story is
important enough that the doubled event in option 1 is worth
avoiding. The new command + handler follows the
`UpdateStudentCommand` pattern (CQRS handler in
`Commands/UpdateStudentGuardian/`, with `UpdateStudentGuardianRequest`
in `Contracts/`, DTO unchanged). Estimated new files: 5 (request,
command, handler, validator, event) + 1 endpoint line. **Backend
scope is intentionally small** — only the fields the page-side
edit dialog actually edits (relationship / role / emergency
flag). Title / first / last / contact edits go through the
existing `UpdateGuardianAsync` (already exists) and the
existing `AddContactAsync` / `UpdateContactAsync` / contact
remove flows.

### Part E — Optional: extract a shared `StudentGuardiansList` presentational component

**File (new):**
`src/Students/SchoolCollab.Students.Admin/Components/Students/StudentGuardiansList.razor`

A thin presentational component (no API calls, no dialog opens)
that renders the read-only `EntityGrid` of links with an "Edit" and
"Delete" button per row. The Edit/Delete buttons fire
`EventCallback<Guid>` (`OnEdit`, `OnRemove`) — the parent page
opens the dialog. This component is used by both `Edit.razor`
(Linked mode) and `Detail.razor`. Estimated size: 80 lines +
20 lines CSS.

**Why a new component, not reusing `GuardiansTab.razor`:**
`GuardiansTab` owns its own state, loads its own links, and has
the deprecated "Add a guardian link" collapsible form baked in.
A new thin component is cheaper than unwinding the
back-compat. The new component's name (`StudentGuardiansList`)
makes the page-side vs dialog-side intent explicit.

### Part F — Defer / out of scope

- "View all" link to a per-student guardians sub-page
  (`/students/{id}/guardians`) is **deferred**. The current
  `Detail.razor` shows all links on the student view itself; a
  sub-page would only add value once links can have more
  attributes (custody / pickup rights) than fit inline. Track in
  §5 followups.
- The wizard's `GuardianAssignmentList` is unchanged. The wizard
  is the only place that defers guardian link creation to its
  own Save phase (Phase 4) — that's correct wizard semantics and
  the user explicitly said dialog UX should stay as-is.
- `GuardiansTab.razor` is deprecated but kept in the codebase
  (no deletion) for any external embed. It gets a one-line
  deprecation comment at the top of the file.

---

## 5. Acceptance criteria

### Dialogs (unchanged)
- [ ] `GuardianPickerDialog` and `GuardianFormDialog` markup and
      behaviour are unchanged.
- [ ] The wizard's Guardian step (Step "Guardians") still works
      end-to-end (add via picker, edit via form, save in Phase 4).

### Pages (new)
- [ ] `Edit.razor` (`/students/{id}/edit`) renders a "Guardians (N)
      — Manage" section that opens `GuardianPickerDialog` and links
      on confirm; renders a read-only grid of current links with
      a per-row Delete button.
- [ ] `Detail.razor` (`/students/{id}`) renders the same "Manage"
      section + grid, with an additional per-row Edit button that
      opens `GuardianFormDialog` in `ForEdit` mode.
- [ ] `Create.razor` (`/students/create`) is unchanged: drafts
      link after student creation.
- [ ] `StudentFormFields` `Mode="Inline"` (default) keeps the
      current panel-switch behaviour for dialog callers (wizard
      inline-edit, `StudentPickerDialog` create form).
- [ ] `StudentFormFields` `Mode="Linked"` strips the panel-switch
      state and renders the page-side compact section.

### Backend (new)
- [ ] New `UpdateStudentGuardianCommand` updates
      `RelationshipCodedValueId`, `Role`, `IsEmergencyContact`
      in place.
- [ ] New `PUT /students/api/students/{studentId}/guardians/{guardianId}`
      endpoint invokes the command; returns the updated
      `StudentGuardianViewDto`.
- [ ] Single `StudentGuardianUpdatedEvent` emitted (no audit
      duplication).

### Quality gates
- [ ] `dotnet build` clean.
- [ ] `dotnet test` (bUnit dialog tests + Admin.Tests) green.
- [ ] No new full-page `.razor.css` overwrites (preserve the
      `.form-actions` border-top rule per the
      `dialog-ui` skill).
- [ ] No new "nested dialog" anti-patterns: any dialog opened
      from a page calls `ShowShellDialogAsync` and the caller
      reloads on non-null.

---

## 6. Affected files

### Modified
- `src/Students/SchoolCollab.Students.Admin/Components/Students/StudentFormFields.razor` — add `Mode` parameter; branch on `Inline` / `Linked`.
- `src/Students/SchoolCollab.Students.Admin/Components/Students/StudentFormFields.razor.css` — add `.student-form-guardians--linked` class.
- `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/Edit.razor` — own guardian state, add Manage / Edit / Remove handlers, pass `Mode="Linked"`.
- `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/Detail.razor` — replace `<GuardiansTab>` with new list + Manage button.
- `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardiansTab.razor` — deprecation comment (no behaviour change).
- `src/Students/SchoolCollab.Students.Core/CQRS/Guardians/Commands/UpdateStudentGuardian/` — new command + handler + validator.
- `src/Students/SchoolCollab.Students.Core/Contracts/IStudentsClient.cs` (or equivalent) — add `UpdateStudentGuardianAsync`.
- `src/Students/SchoolCollab.Students.Api/Endpoints/StudentGuardianRoutes.cs` — new PUT route.
- `src/Students/SchoolCollab.Students.Core/Domain/StudentGuardian.cs` — no schema change (handler updates existing columns).
- `tests/...` — bUnit tests for the new `StudentGuardiansList`, `Edit.razor` Manage flow, `UpdateStudentGuardian` handler.

### New
- `src/Students/SchoolCollab.Students.Admin/Components/Students/StudentGuardiansList.razor` (+ `.razor.css`) — presentational list used by `Edit.razor` and `Detail.razor`.
- `src/Students/SchoolCollab.Students.Core/CQRS/Guardians/Commands/UpdateStudentGuardian/UpdateStudentGuardianCommand.cs`
- `src/Students/SchoolCollab.Students.Core/CQRS/Guardians/Commands/UpdateStudentGuardian/UpdateStudentGuardianHandler.cs`
- `src/Students/SchoolCollab.Students.Core/CQRS/Guardians/Commands/UpdateStudentGuardian/UpdateStudentGuardianValidator.cs`
- `src/Students/SchoolCollab.Students.Core/CQRS/Guardians/Events/StudentGuardianUpdatedEvent.cs`

### Untouched (intentionally)
- `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardianFormDialog.razor`
- `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardianPickerDialog.razor`
- `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/GradeLevels/GradeLevelWizard.razor` (and its `.razor.css`)
- `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/GradeLevels/GuardianAssignmentList.razor`
- `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/Create.razor`
- `src/Students/SchoolCollab.Students.Admin/Components/Pages/Guardians/GuardianDetail.razor`
- `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardiansTab.razor` (deprecation comment only)

---

## 7. Key file references (quick links)

### Frontend
- `src/SchoolCollab.Admin.Shared/Components/Dialogs/DialogShellBase.cs`
- `src/SchoolCollab.Admin.Shared/Components/Dialogs/DialogShellFooter.razor`
- `src/SchoolCollab.Admin.Shared/Components/Dialogs/DialogServiceExtensions.cs`
- `src/Students/SchoolCollab.Students.Admin/Components/Students/StudentFormFields.razor` (+ `.razor.css`)
- `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardianFormDialog.razor`
- `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardianPickerDialog.razor`
- `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardianFormFields.razor` (presentational, reused)
- `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/Edit.razor`
- `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/Detail.razor`
- `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/GradeLevels/GradeLevelWizard.razor`
- `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/GradeLevels/GuardianAssignmentList.razor`

### Backend
- `src/Students/SchoolCollab.Students.Core/CQRS/Guardians/Commands/LinkGuardian/`
- `src/Students/SchoolCollab.Students.Core/CQRS/Guardians/Commands/UnlinkGuardian/`
- `src/Students/SchoolCollab.Students.Core/Domain/StudentGuardian.cs`
- `src/Students/SchoolCollab.Students.Api/Endpoints/StudentGuardianRoutes.cs`
- `src/Students/SchoolCollab.Students.Admin/Services/StudentsApiClient.cs`

### Spec / plan
- `documents/solution/dialog-consolidation-plan.md`
- `documents/solution/student-guardian-plan.md`
- `documents/solution/student-edit-actions-sidebar.md`
- `.github/skills/dialog-ui/SKILL.md` (in-repo canonical)

---

## 8. Followups (out of scope, tracked)

1. **Per-student guardians sub-page** (`/students/{id}/guardians`).
   Today's `Detail.razor` shows the full link list inline; a
   sub-page is worth adding once links grow attributes
   (custody, pickup rights, FERPA flags) that don't fit on a
   single student view.

2. **Guardian-page reverse picker.** When on
   `GuardianDetail.razor`, "add a ward" today is an inline
   single-row form. A symmetric `StudentPickerDialog` opener
   (with relationship / role / emergency flag in the dialog
   body) would be nice but is a different shape than the
   student-side `GuardianPickerDialog` — needs its own spec.

3. **Bulk-link wizard shortcut.** From a student's view page,
   "Link to another grade's guardians" (e.g. siblings share
   guardians) is a multi-student picker that the page-side
   `GuardianPickerDialog` does not support today. Out of scope
   here; would need a `BulkLinkGuardians` API.

4. **Test coverage for `Mode="Linked"`.** The bUnit suite covers
   the dialog flow; a small set of bUnit tests on
   `StudentFormFields` covering both `Mode` branches is
   desirable.

5. **Migration of `GuardiansTab.razor` consumers.** Currently
   only `Detail.razor` renders it; after this plan, none do.
   Consider deleting the file in a follow-up once any
   embed-surface is confirmed (no external embedders).
