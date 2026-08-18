# Plan: Embedded SideDrawer for Contacts & Guardians inside StudentEditDialog

**Date:** 2026-08-17
**Status:** IMPLEMENTED
**Scope:** `StudentEditDialog.razor`, `StudentFormFields.razor`, `ContactsEditor.razor`, `GuardianSection.razor`, shared `SideDrawer.razor`.

---

## 1. Goal

Use the shared `SideDrawer` component for editing **contacts** and **guardians** inside the `StudentEditDialog`. The drawer must slide in **inside the dialog content area** (not as a full-viewport overlay), giving the operator a focused, consistent editing surface for both child-entities without leaving the student edit context.

Key principle:
> The `StudentEditDialog` is a dialog (modal). The SideDrawer is **not a second dialog**; it is an slide-in panel **contained within** the dialog's content box. We avoid nested modal dialogs entirely.

---

## 2. Feasibility: Is a SideDrawer inside a dialog a good idea?

### 2.1 Why it works

- The shared `SideDrawer` now has an `Embedded` parameter. When `Embedded="true"`, the panel uses `position: absolute` and fills its nearest positioned ancestor (an element with `position: relative`).
- The dialog content box is a natural containing block. We wrap the relevant section in `position: relative`, and the SideDrawer will fill that box.
- The dialog content is already a constrained, scrollable region with a clear boundary. The embedded drawer will slide within that boundary, preserving context.

### 2.2 Why not a full-viewport drawer?

- A full-viewport drawer over a dialog creates a "dialog over a dialog" problem: it blocks the entire screen, hides the dialog, and breaks the user's mental model of editing a sub-entity within the student form.
- It also traps focus in a way that screen readers may announce as leaving the original dialog, which is confusing.
- The embedded pattern keeps the user in the same dialog and the same task: "edit this student's contact/guardian."

### 2.3 Key risks and mitigations

| Risk | Mitigation |
|------|-----------|
| **Backdrop click conflicts with dialog close** | The embedded drawer stops propagation on its own backdrop; the dialog's close button is disabled while the drawer is open. |
| **Z-index stacking** | Embedded drawer uses `z-index: 50` (panel) / `z-index: 49` (backdrop), which is high enough to float above dialog content but stays inside the dialog's stacking context. |
| **Two sources of truth for edit state** | The drawer is a visual shell only; the child section component (`ContactsEditor` / `GuardianSection`) owns the editing state. The drawer open/close is purely a visual effect controlled by the section. |
| **Narrow dialog width** | Set the embedded drawer width to a fixed value (e.g., 420px) and ensure the dialog is wide enough. `StudentEditDialog` already uses `Wide="true"` on `StudentFormFields`. |
| **Focus management** | The SideDrawer focuses its panel when it opens; focus returns to the triggering element when it closes (handled by the section component). |
| **Escape key conflicts** | The SideDrawer handles Escape and stops propagation. The dialog no longer closes while the drawer is open because its own close buttons are disabled. |

---

## 3. Target UX

### 3.1 Normal state

```
┌─────────────────────────────────────┐
│  Edit Student                       │
│  ─────────────────────────────────  │
│  Student number  [readonly]         │
│  Name            [enabled]          │
│  Title           [enabled]          │
│  DOB / Gender    [enabled]          │
├─────────────────────────────────────┤
│  Contacts (list + add row)          │
├─────────────────────────────────────┤
│  Guardians (list + add row)         │
├─────────────────────────────────────┤
│           [Cancel] [Save Changes]   │
└─────────────────────────────────────┘
```

### 3.2 Contact edit state (embedded SideDrawer slides in from right)

```
┌─────────────────────────────────────┐
│  Edit Student                       │
│  ─────────────────────────────────  │
│  Student number  [dimmed]           │
│  Name            [dimmed]             │
│  Title           [dimmed]           │
│  DOB / Gender    [dimmed]           │
├─────────────────────────────────────┤
│  Contacts (dimmed)        ┌─────────┤
│  Guardians (hidden)       │ Edit    │
│                           │ contact │
│                           │ [Form]  │
│                           │         │
│                           │ Cancel  │
│                           │ Save    │
│                           └─────────┘
│  [Cancel] [Save Changes]  (disabled)│
└─────────────────────────────────────┘
```

### 3.3 Guardian edit state (embedded SideDrawer slides in from right)

```
┌─────────────────────────────────────┐
│  Edit Student                       │
│  ─────────────────────────────────  │
│  Student number  [dimmed]           │
│  Name            [dimmed]           │
│  Title           [dimmed]           │
│  DOB / Gender    [dimmed]           │
├─────────────────────────────────────┤
│  Contacts (hidden)        ┌─────────┤
│  Guardians (dimmed)       │ Update  │
│                           │ guardian│
│                           │ [Form]  │
│                           │         │
│                           │ Cancel  │
│                           │ Save    │
│                           └─────────┘
│  [Cancel] [Save Changes]  (disabled)│
└─────────────────────────────────────┘
```

---

## 4. Component design

### 4.1 High-level architecture

```
StudentEditDialog (FluentDialog)
└── StudentFormFields
    └── FluentStack (dialog content, position: relative)
        ├── Profile FormRows
        ├── Contacts section wrapper
        │   ├── ContactsEditor
        │   │   └── <SideDrawer Embedded="true" />   // contact edit
        │   └── ...
        ├── Guardians section wrapper
        │   ├── GuardianSection
        │   │   └── <SideDrawer Embedded="true" />   // guardian edit
        │   └── ...
        └── Form actions
```

### 4.2 Responsibilities

| Component | Responsibility |
|-----------|---------------|
| `StudentEditDialog` | Owns the dialog; hosts `StudentFormFields`; disables its own Save/Cancel when a section is editing. |
| `StudentFormFields` | Owns the edit state machine; dims profile fields and disables the dialog action row while a drawer is open; renders the form content `FluentStack` with `position: relative` so the drawer fills the full content area. Both Contacts and Guardians sections stay visible while a drawer is open. |
| `ContactsEditor` | Renders the contact list; on Edit, loads a working copy into the embedded `SideDrawer`; Save/Cancel mutate the in-memory list. |
| `GuardianSection` | Renders the guardian list; on Edit, loads a working copy into the embedded `SideDrawer`; Save/Cancel mutate `Model.GuardianLinks`. |
| `SideDrawer` | Reusable slide-in shell. In `Embedded` mode, fills nearest positioned ancestor. |

### 4.3 State machine

```
[None] --Edit contact-->  [Contacts drawer open]
[None] --Edit guardian--> [Guardians drawer open]

[Contacts drawer open] --Cancel/Save--> [None]
[Guardians drawer open] --Cancel/Save--> [None]
```

While any drawer is open:
- Profile fields are disabled (`AreProfileFieldsDisabled = true`).
- Both Contacts and Guardians sections remain visible (the drawer backdrop blocks interaction with the underlying content).
- The dialog's own Cancel/Save buttons are disabled.

---

## 5. Detailed implementation

### 5.1 SideDrawer (already supports Embedded mode)

Current API:

```razor
<SideDrawer Embedded="true"
            Open="@_open"
            OpenChanged="@OnOpenChanged"
            Title="Edit contact"
            Width="420px"
            ShowCancel="true"
            CancelText="Cancel"
            ShowSubmit="true"
            SubmitText="Save"
            OnSubmitAsync="@SaveAsync">
    @ChildContent
</SideDrawer>
```

Required for this plan: **No changes** to `SideDrawer` are needed. The `Embedded` parameter and the `--embedded` CSS classes already provide the positioning behavior.

#### Embedded CSS review

```css
.side-drawer-panel--embedded {
    position: absolute;
    top: 0;
    right: 0;
    bottom: 0;
    height: auto;
    max-width: 100%;
    z-index: 50;
}

.side-drawer-backdrop--embedded {
    position: absolute;
    inset: 0;
    z-index: 49;
}
```

Key points:
- `position: absolute` positions the drawer relative to the nearest positioned ancestor.
- `top: 0; right: 0; bottom: 0` makes it fill the right side of the ancestor.
- The ancestor must have `position: relative`.

### 5.2 StudentFormFields — add positioned ancestor to the content stack

The form content is wrapped in a `<FluentStack>`. Add `position: relative` to that stack so the embedded SideDrawer fills the **full form content area** in the dialog, not a single section.

```razor
<FluentStack Orientation="Orientation.Vertical" Spacing="3" Class="student-form-fields__content-stack">
    ...
</FluentStack>
```

```css
.student-form-fields__content-stack {
    position: relative; /* establishes containing block for embedded SideDrawers */
}
```

The individual section wrappers do **not** need `position: relative` — the drawer must fill the full content stack, not one section.

Remove any focused section-edit CSS that hides a sibling section or restyles the active section (no `student-form-fields__section-row--hidden`, `--active`, or `__edit-title` rules). Both sections remain visible at all times; the drawer overlays the full content.

### 5.3 ContactsEditor — already converted to embedded SideDrawer

The recent change moved the Buffered-mode inline edit into an `<SideDrawer Embedded="true">`. This serves as the reference implementation. The drawer now fills the parent content stack (the `FluentStack` in `StudentFormFields`) because the component root is no longer positioned.

### 5.4 GuardianSection — convert inline edit panel to embedded SideDrawer

Currently `GuardianSection` uses an panel switch (`_panelMode == GuardianPanelMode.Edit`) that replaces the card list with an inline edit form. Convert this to a SideDrawer:

1. Keep the card list always rendered (do not skip cards while editing).
2. On Edit, open the SideDrawer with a working copy of the guardian being edited.
3. On Save/Cancel, close the drawer and apply/discard changes.
4. Fire `IsEditingChanged(true/false)` so the parent dims profile fields and disables the dialog action row.

```razor
@* Inside GuardianSection, Inline mode *@
<div class="student-guardians">
    @* ... add row ... *@
    @* ... card list (always rendered) ... *@

    <SideDrawer Embedded="true"
                Open="@(_panelMode == GuardianPanelMode.Edit)"
                OpenChanged="OnEditDrawerOpenChangedAsync"
                Title="Update guardian"
                Width="420px"
                ShowCancel="true"
                CancelText="Cancel"
                ShowSubmit="true"
                SubmitText="Save"
                OnSubmitAsync="SaveEditGuardianAsync">
        @if (editedGuardian is { } eg)
        {
            @* Title / name (draft-only) ... *@
            @* Relationship / Role row ... *@
            @* ContactsEditor (Live or Buffered) ... *@
        }
    </SideDrawer>
</div>
```

`GuardianSection` must **not** set `position: relative` on `.student-guardians`; the drawer must fill the parent content stack.

### 5.5 Consistent containing blocks

For both sections, the drawer must fill the **full form content area**, not a single section or component.

- Apply `position: relative` to the **content `FluentStack`** in `StudentFormFields`.
- Do **not** apply `position: relative` to individual section wrappers or to the `ContactsEditor` / `GuardianSection` component roots — those would make the drawer fill only that smaller box.

The nearest positioned ancestor wins. If the content stack is the only positioned element, the drawer fills the full form content inside the dialog.

### 5.6 Width and sizing

- Drawer width: `420px` (default) works for the dialog. The dialog's `StudentFormFields` uses `Wide="true"` so the content area is at least ~600–800px.
- If the dialog content is narrower than 420px on small screens, the drawer fills 100% of the ancestor (`max-width: 100%`).
- The form body inside the drawer should be scrollable if the content overflows. `SideDrawer`'s `.side-drawer-body` has `overflow: hidden` and `flex: 1 1 auto`. The drawer's body content must manage its own overflow or be short enough.

### 5.7 Focus and keyboard

- On open, `SideDrawer` focuses the panel (`_panel.FocusAsync()`).
- On close, the triggering Edit button should regain focus. The component that owns the drawer should call `FocusAsync()` on the trigger button. This requires adding `@ref` to the Edit buttons.
- Escape key is handled by the drawer and does not bubble to the dialog.

### 5.8 Sibling section management

When a drawer is open, the **sibling section is not hidden**. Both Contacts and Guardians remain visible; the drawer's backdrop blocks interaction with the underlying content. The parent still disables the profile fields and the dialog action row while a section is editing.

---

## 6. UI tricks and best practices

### 6.1 Contain the drawer to the full form content, not a single section

Without a positioned ancestor, an absolutely positioned drawer would fill the nearest `position: relative` ancestor up the tree. We set `position: relative` on the parent `FluentStack` in `StudentFormFields` so the drawer fills the **entire form content area** inside the dialog, overlaying both Contacts and Guardians sections while a contact or guardian is being edited.

### 6.2 Use the drawer's own Cancel/Save buttons

Both drawers should use the SideDrawer's footer buttons (`ShowCancel="true"` / `ShowSubmit="true"`) rather than custom buttons in the body. This gives:
- Consistent positioning at the bottom of the drawer.
- Consistent styling (Cancel outline, Save accent).
- Busy state handling (both buttons disable while `OnSubmitAsync` runs).
- Automatic close on successful save.

### 6.3 Keep the dialog action row visible but disabled

Do not hide the dialog's Cancel/Save buttons. Keeping them visible but disabled maintains the user's sense of place and avoids layout shifts.

### 6.4 Dim the profile fields while the drawer is open

The profile fields are disabled during section edit. The underlying Contacts and Guardians sections remain visible; the drawer's own backdrop dims them slightly and blocks pointer events so the drawer content pops. Do not add an active-section background/border — both sections stay in their normal state.

### 6.5 Use the same drawer width for both sections

Consistent width (`420px`) makes the transition between contact and guardian edit feel uniform.

### 6.6 Drawer title matches the focused section

- Contacts: "Edit contact" or "Update contact".
- Guardians: "Update guardian".

### 6.7 Prevent dialog close while drawer is open

The dialog's close button and Save/Cancel are disabled during section edit. The dialog's backdrop click should also be ignored. `StudentEditDialog` already disables the action buttons; the dialog backdrop may need an additional guard if `FluentDialog` supports it.

### 6.8 Mobile considerations

On narrow viewports, the drawer will fill the dialog width. The dialog itself may need a max-width or the content may need to scroll. Since `StudentEditDialog` is primarily a desktop/admin UI, this is acceptable, but the drawer should not break the layout.

### 6.9 Avoid drawer animation conflicts

If two drawers could open in rapid succession (e.g., user clicks Edit contact, then Edit guardian), ensure the first is fully closed before the second opens. The state machine in `StudentFormFields` only allows one active section at a time, so this is naturally prevented.

---

## 7. Implementation steps

1. **Verify SideDrawer Embedded mode** — already added; ensure it has the right CSS and ARIA.
2. **Add positioned ancestor in StudentFormFields** — apply `position: relative` to the section row wrappers.
3. **ContactsEditor** — already uses embedded SideDrawer (reference implementation).
4. **GuardianSection** — replace inline edit panel with embedded SideDrawer.
   - Add `position: relative` to `.student-guardians`.
   - Move the edit form into `<SideDrawer Embedded="true" ...>`.
   - Wire `Open` / `OpenChanged` to `_panelMode` and `NotifyIsEditingAsync`.
   - Use `OnSubmitAsync="SaveEditGuardianAsync"` and the drawer's Cancel for `CancelPanel`.
5. **StudentFormFields CSS** — ensure active section wrapper is the containing block and sibling sections are hidden.
6. **StudentEditDialog** — no changes likely needed; it already listens to `ActiveEditSectionChanged`.
7. **Tests** — update source-level and bUnit tests to assert drawer markup instead of inline forms.
8. **Manual verification** — open the student edit dialog, edit a contact, then a guardian, confirm the drawer slides in inside the dialog content.

---

## 8. Acceptance criteria

- [ ] Clicking Edit on a contact opens the `SideDrawer` embedded inside the **full form content area** of the student edit dialog.
- [ ] Clicking Edit on a guardian opens the `SideDrawer` embedded inside the **full form content area** of the student edit dialog.
- [ ] Both drawers use `Embedded="true"` so they fill the parent content `FluentStack`.
- [ ] The drawer does **not** cover the whole viewport; it stays inside the `StudentEditDialog` content area.
- [ ] Profile fields are dimmed/disabled while either drawer is open.
- [ ] The sibling section (Contacts or Guardians) is **not hidden** while a drawer is open; it remains visible underneath the drawer.
- [ ] The dialog's own Cancel/Save buttons are disabled while a drawer is open.
- [ ] Both drawers use the SideDrawer's footer Cancel/Save buttons, with Cancel first and Save second.
- [ ] On Save, the working copy is applied to the in-memory model (Buffered mode) and the drawer closes.
- [ ] On Cancel, the working copy is discarded and the drawer closes.
- [ ] Escape key closes the drawer but does not close the dialog.
- [ ] Focus moves into the drawer when it opens and returns to the trigger when it closes.
- [ ] All existing unit tests pass; new source-level/bUnit tests assert the embedded drawer markup and the content-stack containing block.
- [ ] No nested modal dialogs are introduced.

---

## 9. Open questions

1. **Guardian add flow:** Adding a new guardian currently auto-opens the inline edit panel. With the drawer, it should auto-open the drawer. Does this fire `IsEditingChanged(true)` and hide the Contacts section? **Yes** — the add path should behave identically to editing a guardian.
2. **Guardian contacts inside the drawer:** The guardian edit drawer already hosts a `ContactsEditor`. Should that nested `ContactsEditor` also use a drawer if the user edits a guardian's contact? **No** — that would be a drawer inside a drawer inside a dialog. The nested `ContactsEditor` should keep its existing inline edit or use a small inline form within the drawer body. A future plan can evaluate this further.
3. **Drawer width on small screens:** Should we reduce the width on narrow viewports? The drawer already uses `max-width: 100%`, so it will fill the section row. This is acceptable for now.
4. **Dialog close on backdrop click:** Does `FluentDialog` allow disabling backdrop close? If not, we may need to intercept the close event and prevent it while a drawer is open. This can be a follow-up if needed.

---

## 10. Conclusion

Using an embedded `SideDrawer` inside the `StudentEditDialog` is **feasible and desirable**. It gives a consistent, focused editing experience for contacts and guardians while avoiding the anti-pattern of nested modal dialogs. The existing `Embedded` parameter on `SideDrawer` provides the necessary positioning, and the current section-edit state machine in `StudentFormFields` handles dimming, hiding, and disabling the rest of the dialog.

The next step is to convert the guardian inline edit panel to use the same embedded `SideDrawer` pattern that contacts now use, then verify both drawers behave correctly inside the dialog.