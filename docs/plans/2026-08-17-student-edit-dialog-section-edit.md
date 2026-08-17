# Plan: Student Edit Dialog — Focused Section Editing for Contacts & Guardians

**Date:** 2026-08-17  
**Status:** SPEC + IMPLEMENTATION (corrected)  
**Scope:** `StudentEditDialog.razor` and its shared form shell `StudentFormFields.razor`, with minimal changes to `GuardianSection.razor` and `ContactsEditor.razor`.

---

## 1. Goal

Improve the student-edit dialog experience so the operator edits **one contact** or **one guardian** in a focused, section-level mode rather than scrolling through an always-editable form.

When the operator starts editing one of those entities (via the existing Edit/Save affordances inside `ContactsEditor` or `GuardianSection`):

1. **Both** display row sections (the Contacts row and the Guardians row) are hidden.
2. A dedicated edit-view section (**"Update contact"** / **"Update guardian"**) renders the full editor for the chosen entity.
3. All student profile fields (student number, name, title, DOB, gender) become disabled.
4. The dialog-level submit actions (**Cancel** / **Save**) are **disabled** (shown but greyed out) — NOT removed.
5. The chosen entity's own **Cancel** / **Save** buttons remain usable and use the same order as the dialog actions: **Cancel first, Save second**.
6. On Cancel or Save inside the inline editor, the dialog returns to the normal state: both display rows visible, profile fields enabled, dialog submit actions re-enabled.

The enhancement applies primarily to the **student edit dialog** (`StudentEditDialog.razor`) because that is the dialog-hosted, full-student editing surface. The page form (`/students/{id}/edit`) can opt into the same `StudentFormFields` behaviour later, but its out-of-form "Direct contact" section requires separate page-level coordination and is not in this plan's primary scope.

---

## 2. Current state and why change is needed

`StudentEditDialog` hosts `StudentFormFields` with both `ShowContacts="true"` and `ShowGuardians="true"`.

- Contacts are rendered by `ContactsEditor` in **Buffered** mode, collecting into `StudentFormModel.Contacts`.
- Guardians are rendered by `GuardianSection` in **Inline** mode, collecting into `StudentFormModel.GuardianLinks`.

Both sections are always visible side-by-side. That creates a long, busy dialog where the operator's attention is split. The new UX narrows focus: when a section's own edit affordance is used, the sibling section disappears and the rest of the form is temporarily disabled.

---

## 3. UX states

### 3.1 Normal state (default)

```
┌─────────────────────────────┐
│  Student number  [readonly] │
│  Name            [enabled]  │
│  Title           [enabled]  │
│  DOB / Gender    [enabled]  │
├─────────────────────────────┤
│  Contacts (N)               │  ← normal always-editable ContactsEditor
├─────────────────────────────┤
│  Guardians (N)              │  ← normal always-editable GuardianSection
├─────────────────────────────┤
│        [Cancel] [Save]      │  ← dialog actions visible and enabled
└─────────────────────────────┘
```

### 3.2 Contacts-edit state

```
┌─────────────────────────────┐
│  Student number  [disabled] │
│  Name            [disabled] │
│  Title           [disabled] │
│  DOB / Gender    [disabled] │
├─────────────────────────────┤
│  Contacts row    (hidden)   │
│  Guardians row   (hidden)   │
├─────────────────────────────┤
│  Update contact             │  ← edit-view section: full ContactsEditor
│  [Channel] [Value] [Label]  │     with inline Cancel/Save for the edited row
├─────────────────────────────┤
│  dialog actions  (disabled) │  ← Cancel/Save greyed out
└─────────────────────────────┘
```

In **Buffered** mode the contact edit is inline (Cancel first, Save second). In **Live** mode the `ContactChangeDialog` still opens because per-edit audit requires a reason. Delete keeps the shared dialog in both modes (it already collects a required reason).

### 3.3 Guardians-edit state

```
┌─────────────────────────────┐
│  Student number  [disabled] │
│  Name            [disabled] │
│  Title           [disabled] │
│  DOB / Gender    [disabled] │
├─────────────────────────────┤
│  Contacts row    (hidden)   │
│  Guardians row   (hidden)   │
├─────────────────────────────┤
│  Update guardian            │  ← edit-view section: full GuardianSection inline editor
│  [Cancel] [Save]            │  ← GuardianSection's own inline panel buttons
├─────────────────────────────┤
│  dialog actions  (disabled) │  ← Cancel/Save greyed out
└─────────────────────────────┘
```

---

## 4. Component design

### 4.1 `StudentFormFields.razor` — central state machine

Introduce a new `StudentEditSection` enum and an `ActiveEditSection` parameter.

```csharp
public enum StudentEditSection { None, Contacts, Guardians }
```

New parameters on `StudentFormFields`:

| Parameter | Type | Purpose |
|-----------|------|---------|
| `EnableSectionEdit` | `bool` | Opt-in flag so existing callers keep the always-editable form. |
| `ActiveEditSection` | `StudentEditSection` | Which section, if any, is in focused edit mode. |
| `ActiveEditSectionChanged` | `EventCallback<StudentEditSection>` | Two-way binding callback so the parent (`StudentEditDialog`) can react. |

Behaviour derived from `ActiveEditSection`:

- `None`:
  - All profile fields enabled (respecting existing `ReadOnlyStudentNumber`).
  - Contacts and guardians sections visible side-by-side in their normal, always-editable form.
  - `RenderActions` honoured normally.
- `Contacts`:
  - Profile fields disabled via a computed flag `AreProfileFieldsDisabled = true`.
  - **Both** the Contacts display row and the Guardians display row are hidden.
  - A dedicated edit-view section ("Update contact") renders the full `ContactsEditor`.
  - The built-in form action row stays visible but its Submit/Cancel buttons are **disabled** (greyed out) — not removed.
- `Guardians`:
  - Profile fields disabled.
  - **Both** the Contacts display row and the Guardians display row are hidden.
  - A dedicated edit-view section ("Update guardian") renders the full `GuardianSection` Inline editor.
  - The built-in form action row stays visible but disabled.

The form wires the child components' new `IsEditingChanged` callbacks:

```razor
<ContactsEditor ... IsEditingChanged="OnContactsEditingChanged" />
<GuardianSection ... IsEditingChanged="OnGuardiansEditingChanged" />
```

```csharp
private async Task OnContactsEditingChanged(bool isEditing)
    => await ActiveEditSectionChanged.InvokeAsync(isEditing ? StudentEditSection.Contacts : StudentEditSection.None);

private async Task OnGuardiansEditingChanged(bool isEditing)
    => await ActiveEditSectionChanged.InvokeAsync(isEditing ? StudentEditSection.Guardians : StudentEditSection.None);
```

No section-header Edit buttons are added, and no dedicated Cancel/Done buttons are added to the edit-view section. The child components own those interactions.

### 4.2 `ContactsEditor.razor` — inline edit in Buffered mode, report state changes

Add a single new parameter:

```csharp
[Parameter] public EventCallback<bool> IsEditingChanged { get; set; }
```

**Buffered mode:** Clicking a contact's Edit button switches that row to an inline edit form (channel dropdown, optional country-code dropdown, value, label, **Cancel**, **Save**). The editor fires `IsEditingChanged(true)` when the inline form opens and `IsEditingChanged(false)` when Save or Cancel closes it.

**Live mode:** Keeps the existing `ContactChangeDialog` because per-edit contact audit requires a reason.

Remove/delete keeps the shared dialog in both modes (it already collects a required reason).

### 4.3 `GuardianSection.razor` — inline edit panel, report state changes

Add a single new parameter:

```csharp
[Parameter] public EventCallback<bool> IsEditingChanged { get; set; }
```

Fire `true` when the inline edit panel opens (either from a card's Edit button or from the add-row path that auto-opens the panel for a newly-added guardian). Fire `false` when the panel closes via Save or Cancel.

The inline edit panel title is **"Update guardian"** and its action buttons are **Cancel** then **Save**.

When the panel is open, **all** guardian cards are hidden; only the edit panel is visible.

### 4.4 `StudentEditDialog.razor` — mirror the section state

Add local state:

```csharp
private StudentFormFields.StudentEditSection _activeEditSection = StudentFormFields.StudentEditSection.None;
```

Bind it to `StudentFormFields`:

```razor
<StudentFormFields Model="_model"
                   ShowGuardians="true"
                   ShowContacts="true"
                   Wide="true"
                   ReadOnlyStudentNumber="true"
                   StudentId="@StudentId"
                   ErrorMessage="@_error"
                   OnValidSubmit="OnSaveAsync"
                   SubmitLabel="Save Changes"
                   SubmittingLabel="Saving…"
                   Submitting="@_saving"
                   OnCancel="CancelAsync"
                   EnableSectionEdit="true"
                   ActiveEditSection="@_activeEditSection"
                   ActiveEditSectionChanged="OnActiveEditSectionChanged" />
```

The dialog no longer snapshots or restores section data. The child components own their own Cancel behaviour:

```csharp
private Task OnActiveEditSectionChanged(StudentFormFields.StudentEditSection section)
{
    _activeEditSection = section;
    return Task.CompletedTask;
}
```

The dialog's main `OnSaveAsync` guards against persisting while a section edit is active:

```csharp
if (_activeEditSection != StudentFormFields.StudentEditSection.None) return;
```

This is a safety net; the Save/Cancel buttons are already disabled during a section edit.

---

## 5. CSS

Add scoped CSS classes to `StudentFormFields.razor.css`:

- `.student-form-fields__profile-row` / `.student-form-fields__profile-row--disabled` — each profile `FormRow` is wrapped in a div so it can be dimmed and made non-interactive during a section edit without breaking the `FluentStack Spacing="3"` gaps.
- `.student-form-fields__section-row` / `--hidden` / `--active` — each Contacts/Guardians `FormRow` is wrapped in a div so the section switch is done via CSS. This keeps the same component instance mounted. The active state hides the `FormRow` label via `::deep`, shows the `"Update ..."` title, and draws a highlighted box around the section. The active section uses `padding-left: 0` and indents the title and `FormRow` content by the canonical **180px** so the editor lines up with the normal profile input cells above.
- `.student-form-fields__edit-title` — the "Update contact" / "Update guardian" title.
- `.student-form-fields__section-divider` — the horizontal rule between the profile block and each section (and between sections). Given `margin: 0.75rem 0` so it no longer touches the content below it.

No read-only styling is added to `ContactsEditor` or `GuardianSection`; their normal rendering is used in both display and edit-view states. The inline edit form in `ContactsEditor` uses a new `.contact-item--editing` class and `.contact-edit-actions` row for Cancel/Save styling.

---

## 6. Tests

### 6.1 Source-level tests in `SchoolCollab.Admin.Tests.Unit`

Add `StudentFormFieldsSectionEditTests.cs` (source-level assertions against the .razor files):

1. `ContactsEdit_HidesSiblingSectionAndDisablesProfileAndSubmit`
   - Assert the contacts edit-view section is gated on `ActiveEditSection == Contacts` and `EnableSectionEdit`.
   - Assert the title is **"Update contact"**.
   - Assert profile inputs use `Disabled="@AreProfileFieldsDisabled"`.
   - Assert the form action buttons use `Disabled="@(Submitting || AreProfileFieldsDisabled)"`.

2. `GuardiansEdit_HidesSiblingSectionAndDisablesProfileAndSubmit`
   - Assert the guardians edit-view section is gated on `ActiveEditSection == Guardians` and `EnableSectionEdit`.
   - Assert the title is **"Update guardian"**.
   - Assert profile fields and action buttons are disabled during the edit.

3. `NormalState_ShowsBothSections`
   - Assert both sections render side-by-side in the normal state.
   - Assert no section-header Edit buttons (`StartContactsEditAsync` / `StartGuardiansEditAsync`) are added.

4. `SectionEdit_IsOptIn_SoExistingCallersUnchanged`
   - Assert `EnableSectionEdit` gates the edit-view sections and defaults to false.

5. `ChildComponents_ReportEditingStateChanges`
   - Assert `ContactsEditor` declares `IsEditingChanged` and invokes it with `true`/`false`.
   - Assert `GuardianSection` declares `IsEditingChanged` and invokes it with `true`/`false`.

6. `ContactsEditor_UsesInlineEditWithSaveCancelInBufferedMode`
   - Assert `ContactsEditor` branches Live mode to the `ContactChangeDialog` and Buffered mode to inline editing with Cancel/Save.

7. `InlineEditActions_UseConsistentCancelSaveOrder`
   - Assert both `ContactsEditor` and `GuardianSection` inline edit action rows render **Cancel before Save**.

8. `GuardianEdit_HidesDisplayViewOfEditedGuardian`
   - Assert `GuardianSection` skips **all** guardian cards when the inline edit panel is open — the edit panel is the only visible view of the guardian being edited.

9. `StudentEditDialog_WiresSectionEditParameters`
   - Assert the dialog passes `EnableSectionEdit="true"`, `ActiveEditSection`, and `ActiveEditSectionChanged`.

10. `StudentEditDialog_DoesNotSnapshotOrAddSectionButtons`
    - Assert the dialog does not keep `_contactsSnapshot` / `_guardianLinksSnapshot`.
    - Assert the dialog does not wire `OnSectionEditSave` / `OnSectionEditCancel`.

### 6.2 Playwright smoke test (optional, future)

A focused E2E smoke can wait until the new behaviour is stable. The existing `ContactAuditSmokeTests.cs` project already has Playwright wiring; a future test can navigate to the student card, open the edit dialog, and verify the section focus UX.

---

## 7. Acceptance criteria

- [x] `StudentEditDialog` opens in normal state: both sections visible, profile editable, Cancel/Save visible **and enabled**.
- [x] Activating a contact edit (via `ContactsEditor`'s existing Edit button) hides **both** sections, disables profile, **disables** the dialog Cancel/Save (greyed out, still present), and renders an "Update contact" edit-view section with the full `ContactsEditor` and an inline Cancel/Save form for the edited contact.
- [x] Activating a guardian edit (via `GuardianSection`'s existing Edit affordance) hides **both** sections, disables profile, disables the dialog Cancel/Save, and renders an "Update guardian" edit-view section with the full `GuardianSection` inline editor (its own Cancel/Save).
- [x] Inline edit action buttons in both `ContactsEditor` and `GuardianSection` use the same order as the dialog actions: **Cancel first, Save second**.
- [x] The section's own Cancel/Save buttons return the form to the normal state and re-enable the dialog Cancel/Save.
- [x] The dialog-level Save still writes the entire student (profile + contacts + guardians) atomically, and guards against persisting while a section edit is active.
- [x] Row spacing (gaps) in `StudentFormFields` are preserved: profile `FormRow`s and section `FormRow`s are wrapped in divs that remain direct children of the `FluentStack Spacing="3"`.
- [x] The focused edit-view section is offset to the right by the canonical 180px label-column width, so the "Update ..." title and the editor line up with the input cells in the normal profile rows above.
- [x] The section divider (`FluentDivider`) has vertical breathing room (`margin: 0.75rem 0`) so it no longer touches the content below it.
- [x] When editing a guardian, the guardian card display view is hidden entirely; only the inline edit panel is shown.
- [x] All existing callers of `StudentFormFields` continue to work unchanged when `ActiveEditSection` is not set (defaults to `None`).
- [x] `ContactsEditor` and `GuardianSection` are not redesigned as read-only display components; they only gain an `IsEditingChanged` callback.
- [x] All new and existing unit tests pass.

---

## 8. Open questions

1. **Guardian add-row:** Adding a new guardian inline opens the edit panel automatically. Does that count as "editing guardians" and therefore hide the Contacts section? Yes — the add path transitions to the Edit panel and fires `IsEditingChanged(true)`.
2. **Page scope:** Should `/students/{id}/edit` adopt the same focus UX in this plan? The recommendation is no — keep the plan scoped to the dialog and add page support in a follow-up plan once the component changes are proven.
