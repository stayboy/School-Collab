# Plan: ContactsEditor & GuardianSection — Readonly / Edit Modes (drop the fragment tree)

**Date:** 2026-08-18
**Status:** IMPLEMENTED (option B — per-card Edit/Delete + "Add" anchor; focused per-item form in side drawer)
**Scope:** `ContactsEditor.razor`, `GuardianSection.razor`, `StudentFormFields.razor`, `StudentEditDialog.razor`, `SectionEditContext.cs`, and the section-edit unit tests.

> **Implementation summary** (option B, as built):
>
> - `ContactsEditor.View` and `GuardianSection.View` are `{ Full, Readonly, Edit }` (default `Full`).
> - `Readonly` view (student-edit-dialog summary): per-card Edit + Delete buttons; "Add contact"/"Add guardian" `FluentAnchor` below the list; no section-level "Manage" button. Edit/Delete/Add raise `OnEditContact` / `OnAddContact` / `OnEditGuardian` / `OnAddGuardian` host callbacks.
> - `Edit` view (focused per-item form hosted in the side drawer): plain Razor markup. Hosts pass `InitialEditKey` / `InitialEditIndex` + `IsAdd` to drive the focused form.
> - `EditDisabled` is wired to per-card triggers in `Readonly` mode and to the focused form's submit in `Edit` mode, so the host can dim the section while a drawer is open.
> - `StudentFormFields` removed the "Manage contacts" / "Manage guardians" buttons; it now forwards per-card / Add callbacks into the Readonly `ContactsEditor` / `GuardianSection` and to the dialog.
> - `StudentEditDialog` is now the host: it owns `_editingContactKey` / `_editingGuardianIndex` / `_isAdd`, and the single `DialogDrawer` hosts the focused `View="Edit"` editor with the right parameters. `OnDrawerOpenChangedAsync` clears the focused state on close.
> - `SectionEditContext`, `BuildEditFragment`, `PublishEditContextAsync`, and the section-swap guard are gone.
> - All 387 Admin + 221 Students + 1 Students API tests green. 0 build errors across the whole solution.

---

## 1. Goal

Replace the current "publish a live edit `RenderFragment` up the tree into a shared drawer" pattern with a two-surface design where each editor owns its views as plain Razor markup, and the host renders whichever it needs.

In the student edit dialog, keep **both** surfaces displayed simultaneously:

- **Readonly summary** on the dialog main content — the contacts / guardians list, where **each card carries Edit + Delete** and the section has a separate **"Add contact" / "Add guardian" `FluentAnchor` below the list**. Edit/Delete/Add raise events to the host; the summary never opens a form inline.
- **Edit form** in the side drawer — a **focused edit form for the one selected item** (an existing item's edit form, or a blank form for add). Not the full editor.

Both surfaces bind the same in-memory lists, so a Save in the drawer is reflected in the summary behind it.

Trigger UX note: there is **no section-level "Manage" button** — entry to the drawer is per-card Edit (or the "Add contact" / "Add guardian" anchor), as requested.

Primary outcome: **avoid the complex Blazor trees** used to express the current focused section-edit feature (`BuildEditFragment` RenderTreeBuilder code, the frozen-fragment re-publish workaround, the `SectionEditContext` publish-up chain, and the section-swap cancellation guard).

---

## 2. Current architecture (why the fragment tree exists)

Today a child editor threads a live edit form up through two hosts and into a drawer:

```
ContactsEditor / GuardianSection ──PublishEditContextAsync()──▶ SectionEditContext(Content = RenderFragment)
     │  BuildEditFragment() = raw RenderTreeBuilder (builder.OpenComponent / sequence numbers)
     ▼
StudentFormFields (SectionEditContent param) ──▶ StudentEditDialog (_sectionEditContent)
                                                     ▼
                                             DialogDrawer   @ctx.Content
```

Complexities this introduces:

1. **`BuildEditFragment()`** (`ContactsEditor.razor:539`, `GuardianSection.razor:475`) builds the form by hand with `RenderTreeBuilder` sequences instead of normal Razor markup.
2. **Frozen-fragment problem** — a published fragment only re-executes when re-published, so a channel change must re-call `PublishEditContextAsync()` to reveal/hide the gated country-code field.
3. **Publish-up chain** — `SectionEditContextChanged` → `StudentFormFields.SectionEditContent(Changed)` → `StudentEditDialog._sectionEditContent` → `DialogDrawer @ctx.Content`.
4. **Section-swap guard** — `OnSectionEditContentChanged` must cancel the previous section's context on a genuine key change (`previous.SectionKey != ctx.SectionKey`) to avoid silent data loss, while not cancelling same-key re-publishes.

---

## 3. Proposed design: two modes, host renders both

### 3.1 Presentation dimension (`View`) on both editors

Introduce a presentation enum used by both components, keeping the existing *data* dimensions orthogonal. Three values: the pre-existing full editor (default, unchanged callers) plus the two surfaces used in the student edit dialog. The `Readonly` surface (per-card Edit/Delete + the "Add contact" / "Add guardian" `FluentAnchor`) is rendered **only in the student edit dialog**; `Full` (default) is unchanged and has no such anchor.

- **`Full`** (default) — the existing inline editor: add-row + list with per-row Edit/Remove/Reorder/Verify/Subscribe + inline per-row edit form. Unchanged behavior for all current callers (`ContactsTab`, `GuardianDetail`, `GuardianSetupWizard`, `TeacherDetail`, the picker/form dialogs, etc.).
- **`Readonly`** — the dialog main-content summary. A list of cards where **each card has Edit + Delete** and the section has a separate **"Add contact" / "Add guardian" `FluentAnchor`** below the list.
  - `ContactsEditor`: contact rows (channel/value/label/preferred/verified badges) + per-row Edit/Delete, and an "Add contact" `FluentAnchor` below the list.
  - `GuardianSection`: guardian cards (name/relationship/role badges/contact chips) + per-card Edit/Delete, and an "Add guardian" `FluentAnchor` below the cards.
  - Edit/Add do **not** mutate inline — they raise host callbacks (`OnEditContact`/`OnAddContact`, `OnEditGuardian`/`OnAddGuardian`) so the host opens the drawer. Delete removes directly in Buffered mode (see §5).
- **`Edit`** — the focused per-item edit form for the side drawer. Renders **only** that one item's edit form (channel/value/label/country for contacts; title/names/relationship/role/nested-contacts for guardians) + Save/Cancel — plain Razor markup, no `RenderTreeBuilder`, no publish-up. Initialized from `InitialEditKey`/`InitialEditIndex` (edit existing) or `IsAdd` (blank add).

### 3.2 Naming (avoid the `Mode` collision)

- `ContactsEditor` already has `EditorMode { Live, Buffered }`. **Keep it as-is** — that enum name is referenced by ~10 call sites via `Mode="EditorMode.Live/Buffered"`, so renaming it would turn an additive change into a large, breaking sweep.
- Add **`View { Full, Readonly, Edit }`** as the presentation dimension on both components (`[Parameter] public ContactsView View` / `GuardianView View`), **defaulting to `Full`** so existing callers render the full editor unchanged. `Readonly` + `Edit` are the two surfaces used in the student dialog ("Readonly vs EditMode" per the task brief); `Full` is the pre-existing default.
- `GuardianSection.Mode: StudentFormFieldsMode { Inline, Linked }` stays orthogonal to `View` (data/UX flavor).

### 3.3 Student edit dialog renders both (one shared drawer)

The dialog owns a single `DialogDrawer` and a light `_editor` switch (None / Contacts / Guardians). The **Readonly** summary lives in `StudentFormFields`; its per-card Edit/Add raise callbacks the dialog wires to "open the drawer for this item". The drawer hosts the **focused `Edit`** form for the selected item.

```razor
<StudentFormFields ... ProfileFieldsDisabled="@(_editor != ActiveEditor.None)"
                   OnEditContact="OpenContactEditorAsync"   OnAddContact="OpenContactAdderAsync"
                   OnEditGuardian="OpenGuardianEditorAsync" OnAddGuardian="OpenGuardianAdderAsync" />

<DialogDrawer Open="@(_editor != ActiveEditor.None)"
              Title="@(_editor == ActiveEditor.Contacts ? "Edit contact" : "Edit guardians")"
              OnSubmitAsync="SubmitActiveEditorAsync" OnCancel="CancelActiveEditorAsync">
   @if (_editor == ActiveEditor.Contacts)
   {
       <ContactsEditor View="ContactsView.Edit" @ref="_contactsEditor"
                       Mode="ContactsEditor.EditorMode.Buffered" OwnerType="ContactOwnerType.Student"
                       Contacts="Model.Contacts" ContactsChanged="OnDraftContactsChanged"
                       InitialEditKey="@_editContactKey" IsAdd="@_isAdd" />
   }
   else if (_editor == ActiveEditor.Guardians)
   {
       <GuardianSection View="GuardianView.Edit" @ref="_guardianSection"
                       GuardianLinks="Model.GuardianLinks" Mode="StudentFormFieldsMode.Inline"
                       InitialEditIndex="@_editGuardianIndex" IsAdd="@_isAdd" />
   }
</DialogDrawer>
```

- Main content (student edit dialog) = **Readonly** summaries (per-card Edit/Delete + an "Add contact"/"Add guardian" `FluentAnchor` below the list); the side drawer = the focused **Edit** form for the selected item.
- Both surfaces bind the same in-memory lists. A drawer Save mutates `Model.Contacts` / `Model.GuardianLinks` and the summary re-renders.
- The drawer `Title` reflects `_editor` + the action (e.g. "Edit contact" / "Add contact").
- Drawer Save/Cancel call the active `Edit` instance's `SubmitEditFormAsync()` / `CancelEditFormAsync()` via `@ref` (null-safe — only one `@if` branch renders).
- The "edit active" state is driven by `_editor != None`. The dialog passes that to `StudentFormFields` via **`ProfileFieldsDisabled`** (replacing the old `EnableSectionEdit`/`ActiveEditSection` path); existing `Disabled="..."` bindings on profile fields and the action row stay.
- **No "Manage" buttons** — entry is per-card Edit or the "Add contact"/"Add guardian" `FluentAnchor` below the list, both on the student-edit-dialog Readonly summary.

### 3.4 Removing the complex tree (both components)

| Current complexity | After |
|---|---|
| `BuildEditFragment()` RenderTreeBuilder code | Deleted — the focused `Edit` form is normal Razor markup in the drawer |
| Frozen-fragment re-publish on channel change | Gone — reactive re-render inside the drawer |
| `SectionEditContext` record + publish-up chain | Removed — drawer gets plain Razor `ChildContent` |
| Section-swap cancellation guard | Replaced by the dialog's simple `_editor` switch (previous section is just not rendered) |
| `IsEditingChanged` propagation | Unneeded — the dialog drives a new `ProfileFieldsDisabled` param from `_editor != None`; `StudentFormFields` no longer needs `EnableSectionEdit`/`ActiveEditSection` |

---

## 4. Concrete file changes

1. **`ContactsEditor.razor`** — change `View` to `{ Full, Readonly, Edit }` (default `Full`); `Readonly` = student-edit-dialog summary list with per-card **Edit + Delete** (Edit raises `OnEditContact(key)`; Delete removes — Buffered direct, Live via `ContactChangeDialog`) plus an "Add contact" `FluentAnchor` below the list (raises `OnAddContact`); `Edit` = focused per-item edit form (plain Razor) initialized from `InitialEditKey`/`IsAdd`, exposing `SubmitEditFormAsync()` / `CancelEditFormAsync()`; `Full` = today's full editor for existing callers. Drop `BuildEditFragment`, the publish plumbing, and `SectionEditContextChanged`. Keep `EditorMode` (no rename).
2. **`GuardianSection.razor`** — same shape: `View { Full, Readonly, Edit }`; `Readonly` = student-edit-dialog cards with per-card Edit/Delete (Edit raises `OnEditGuardian(index)`; Delete removes) plus an "Add guardian" `FluentAnchor` below the cards (raises `OnAddGuardian`); `Edit` = focused per-guardian edit form (title/names/relationship/role/nested `ContactsEditor`) from `InitialEditIndex`/`IsAdd`; `Full` = existing inline editor. Drop `BuildEditFragment` / `SectionEditContextChanged` / `IsEditingChanged`.
3. **`StudentFormFields.razor`** — `ShowContacts` / `ShowGuardians` render **Readonly** summaries; forward the per-card Edit/Add/Delete callbacks (`OnEditContact` / `OnAddContact`, `OnEditGuardian` / `OnAddGuardian`) to the host; remove `SectionEditContent` / `SectionEditContentChanged` and `EnableSectionEdit` / `ActiveEditSection(Changed)`; add **`ProfileFieldsDisabled`** (dialog sets from `_editor != None`).
4. **`StudentEditDialog.razor`** — single `DialogDrawer` hosting the focused `Edit` form for the selected item (`InitialEditKey` / `InitialEditIndex` / `IsAdd`), switched by `_editor`; Save/Cancel via `@ref`. Remove `_sectionEditContent` / `OnSectionEditContentChanged` and the swap guard. While a drawer is open, **disable all other per-card Edit/Delete/Add triggers** (both sections) so only one item is edited at a time (see §5).
5. **`SectionEditContext.cs`** — delete (no longer referenced).
6. **CSS** — `.contacts-editor--readonly` / `.guardian-cards--readonly` (summary) + focused-form styling; keep the drawer layout.
7. **Tests** — update per §6.

---

## 5. Edge cases & risks

- **One drawer, one item at a time — swap data loss** — the drawer's `Edit` form holds a working copy for one item; opening a different item (or Add) mid-edit would discard it. **Decision: while a drawer is open, disable all other per-card Edit/Delete/Add triggers** (both sections) so only one item is ever edited. The `@ref` must be null-safe (guard before `SubmitEditFormAsync`).
- **Delete on a summary card** — Buffered (student dialog): remove the item directly from `Model.Contacts` / `Model.GuardianLinks` and re-render (no audit). Live (contacts tab etc.): keep `ContactChangeDialog` for the audit reason. The Readonly summary's Delete reuses the per-row remove logic the full editor already had.
- **Add** — the "Add contact" / "Add guardian" `FluentAnchor` below the list opens the drawer with `IsAdd=true` (blank focused form showing the full field set); on Save the new item is appended to the shared list.
- **UX behaviour change (not a pure refactor)** — today the student dialog shows the full inline `ContactsEditor` (add-row always visible) in main content and the drawer holds a per-row edit form published up as a fragment. After this change the main content is a **student-edit-dialog summary with per-card Edit/Delete + the "Add contact"/"Add guardian" anchor** and the drawer holds the **focused per-item edit form** (plain Razor, no publish-up). This is the intended design; do not expect pixel-parity.
- **Nested contact editor in guardian edit** — the guardian `Edit` (focused) form still hosts a Buffered `ContactsEditor` (`Full`) for that guardian's contacts; it stays inline within the drawer (no drawer-in-drawer).
- **Live-mode audit reason** — keep `ContactChangeDialog` for Live contexts; the student dialog (Buffered) edits inline / removes directly.
- **List ownership** — neither surface owns the lists; they read/mutate `Model.Contacts` / `Model.GuardianLinks`, so parent save is unchanged and atomic.
- **Existing callers** — `ContactsEditor` is used directly in ~10 places (`ContactChangeDialog`, `GuardianDetail`, `GuardianSetupWizard`, `Edit.razor`, `TeacherDetail`, `GuardianContactsDialog`, `GuardianFormDialog`, `GuardianPickerDialog`, nested in `GuardianSection`, and `StudentFormFields`). All keep working because `View` defaults to `Full` and `EditorMode` is unchanged. `GuardianSection` is only consumed by `StudentFormFields`.

---

## 6. Testing

Update `StudentFormFieldsSectionEditTests.cs` (and any `GuardianSection` source tests) to assert:

- The dialog renders **Readonly** `ContactsEditor` / `GuardianSection` in main content, and the **focused `View="Edit"`** instance (with `InitialEditKey` / `InitialEditIndex` / `IsAdd`) inside the single `DialogDrawer`.
- The student-edit-dialog Readonly summary renders **per-card Edit + Delete** and an **"Add contact" / "Add guardian" `FluentAnchor` below the list** — and **no section-level "Manage" button**.
- `BuildEditFragment`, `SectionEditContext(Changed)`, and the swap-guard logic are gone.
- The drawer content and `Title` switch by `_editor` (Contacts vs Guardians) and action (Edit vs Add).
- Delete `StudentEditDialog_CancelsPreviousContextOnSectionSwap`; replace with an assertion that the other per-card triggers are disabled while a drawer is open.
- bUnit `ContactsEditorTests`: the focused `Edit` form Save mutates the in-memory list (edit-existing and add); Cancel discards.

---

## 7. Open questions

1. **Drawer trigger UX** — resolved: **no "Manage" button**. Each summary card has **Edit + Delete**, and a separate **"Add contact" / "Add guardian" `FluentAnchor` below the list** opens the drawer with a blank focused form. Edit on a card opens the drawer with that item's focused edit form. (Student edit dialog only.)
2. **Drawer sizing** — keep the existing fixed **420px** with internal scroll; revisit if the guardian focused form feels cramped.
3. **Guardian `Mode` (Inline/Linked)** — kept orthogonal to `View`; the drawer renders `GuardianSection View="Edit" Mode="Inline"`.

All three are resolved by the implementation.

---

## 8. Acceptance criteria

- [x] `ContactsEditor` and `GuardianSection` drop `BuildEditFragment` / `SectionEditContext` (no publish-up, no `RenderTreeBuilder`).
- [x] `View` is `{ Full, Readonly, Edit }` (default `Full`); `Readonly` = student-edit-dialog summary with per-card Edit/Delete + an "Add contact"/"Add guardian" `FluentAnchor` below the list; `Edit` = focused per-item edit form.
- [x] No section-level "Manage" button — entry to the drawer is per-card Edit or the "Add contact"/"Add guardian" `FluentAnchor`.
- [x] Each contact row / guardian card in the student-edit-dialog Readonly summary has Edit + Delete.
- [x] Edit on a card opens the drawer with a **focused edit form for that one item**; the "Add contact"/"Add guardian" `FluentAnchor` opens a blank focused form (full field set).
- [x] Drawer Save mutates the shared list (edit-existing / add-new) and the summary re-renders; Cancel discards.
- [x] Profile fields and the dialog action row are disabled while a drawer is open (`ProfileFieldsDisabled` from `_editor != None`).
- [x] While a drawer is open, the other per-card Edit/Delete/Add triggers are disabled (one item at a time) — `EditDisabled` is forwarded from `AreProfileFieldsDisabled`.
- [x] Existing callers default to `Full` (unchanged); `EditorMode` is not renamed.
- [x] All new and existing unit tests pass — 387 Admin tests, 221 Students tests, 1 Students API test, all green. 0 build errors across the whole solution.

