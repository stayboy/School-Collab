# Plan: Dialog-content drawer/sidebar for the student edit dialog

Status: **IMPLEMENTED** (DialogDrawer.razor + .razor.css in Admin.Shared, wired into StudentEditDialog; ContactsEditor and GuardianSection publish their edit context up via SectionEditContextChanged; the shared drawer fills the full dialog body content area between the title bar and the actions bar).

## 1. Problem

The student edit dialog (`StudentEditDialog`) edits a student's profile,
contacts, and guardians. Editing a contact or a guardian opens a shared
`SideDrawer` with `Embedded="true"`. That drawer is `position: absolute`
and fills its nearest positioned ancestor — currently the
`.student-form-fields__content-stack` `FluentStack` that holds the profile
rows + the Contacts and Guardians sections.

This produces a poor experience inside the modal:

- The drawer only spans the **form-rows stack**, not the full dialog body.
  It is either too short (when the form content is short) or it fills the
  scrollable content height (when tall), so its bottom edge lands at an
  arbitrary spot above the dialog's own Cancel/Save row rather than at the
  bottom of the dialog content area.
- The drawer's backdrop dims the **entire** form (profile + both sections),
  so the operator loses all context about the student being edited while a
  single contact or guardian is being refined.
- The drawer is an overlay, not a layout region, so the form has to be
  carefully positioned (`position: relative` on the content stack, z-index
  juggling with the dialog) and it still does not look like a first-class
  part of the dialog.

The root cause: the shared `SideDrawer` was built for a **full-viewport**
slide-over (the AI chat panel). Its `Embedded` mode is a bolt-on that
anchors to "the nearest positioned ancestor," which is the wrong anchor
inside a `FluentDialog`. We want a drawer that is **a first-class region of
the dialog body**, occupying the dialog content area only — between the
title bar and the actions bar — and never overlapping either.

## 2. Goal

Build a dedicated **dialog-content drawer/sidebar** component (working
name: `DialogDrawer`) that:

1. Lives **inside the dialog body** (`.fluent-dialog-body`, the `1fr` grid
   cell between `fluent-dialog-header` and `fluent-dialog-footer`).
2. Fills the **full dialog content area** — top edge under the title bar,
   bottom edge above the actions bar — and never overlaps the title bar or
   the actions bar.
3. Anchors to the **right by default** (parameterized: `Right` (default) /
   `Left`).
4. Hosts the **edit forms** for a contact (`ContactsEditor` Buffered edit)
   and a guardian (`GuardianSection` edit), replacing the embedded
   `SideDrawer` usage in both.
5. Keeps the main form (profile + section summaries) visible and dimmed
   (not hidden) while a drawer is open, and disables the dialog's own
   Cancel/Save row while editing.
6. Reuses the existing `SideDrawer` visual language (header title + ✕,
   scrollable body, footer Cancel/Save) so the edit UX stays consistent
   with the rest of the app.

**Non-goals**

- Do not change the shared `SideDrawer` component (still used by the AI
  chat panel and other full-viewport slide-overs).
- Do not change `ContactsEditor` Live mode (contact edit/delete with reason
  stays in `ContactChangeDialog`).
- Do not change the create flow (`StudentCreateDialog`) — it has no
  per-edit drawer today; the create dialog keeps its inline section editors.

## 3. The FluentDialog DOM (grounding)

`FluentUI` renders a dialog as a CSS grid (`Microsoft.FluentUI…bundle.scp.css`):

```
fluent-dialog  (the <fluent-dialog> web component)
└── ::part(control)  →  display: grid
    grid-template-rows: auto 1fr auto
    grid-template-areas:
        'dialog-header'
        'dialog-body'
        'dialog-footer'
    ├── .fluent-dialog-header   (grid-area: dialog-header)  ← title bar
    ├── .fluent-dialog-body     (grid-area: dialog-body)    ← 1fr, our content lives here
    └── .fluent-dialog-footer   (grid-area: dialog-footer)  ← actions bar (null for our dialog)
```

`StudentEditDialog` is opened via `ShowReadonlyDialogAsync` with
`PrimaryAction = null` / `SecondaryAction = null`, so `FluentUI` does **not**
render `.fluent-dialog-footer`. The dialog's own Cancel/Save buttons are the
`.form-actions` row that `StudentFormFields` renders **inside** the body.
So for this dialog the "actions bar" the drawer must not cover is the
`.form-actions` row, and the "title bar" is `.fluent-dialog-header`.

Because the body row is `1fr`, `.fluent-dialog-body` has a **definite
height** (dialog height minus header/footer). A child with `height: 100%`
therefore fills the body exactly — this is the key fact that lets a drawer
fill "the dialog content area only" without touching header or footer.

## 4. Design

### 4.1 New component: `DialogDrawer`

Location: `src/SchoolCollab.Admin.Shared/Components/DialogDrawer.razor`
(+ `.razor.css`). Lives in `Admin.Shared` next to `SideDrawer` so both the
Students app and any other admin app can reuse it.

Responsibilities:
- Render an optional **backdrop** (dim the main form) and a **panel**
  (right- or left-anchored) that fills the dialog body content area.
- Provide a **header** (title + dismiss ✕), a **scrollable body**
  (`ChildContent`), and an optional **footer** (Cancel / Submit) — same
  shape as `SideDrawer` so the two feel identical to the operator.
- Close on ✕, backdrop click, or Escape (but **not** on a `FluentDialog`
  backdrop click — the dialog's own overlay is a separate element).
- Disable its footer buttons while `OnSubmitAsync` is in flight, show a
  spinner on Submit, auto-close when `OnSubmitAsync` returns `true`, stay
  open when it returns `false` (mirrors `SideDrawer`).

Parameters (parity with `SideDrawer` where it makes sense):

| Parameter | Type | Default | Notes |
|-----------|------|---------|-------|
| `Open` | `bool` | `false` | Two-way bindable via `OpenChanged`. |
| `OpenChanged` | `EventCallback<bool>` | — | Mirrors the drawer's open state back. |
| `Title` | `string` | required | Header text. |
| `Side` | `DialogDrawerSide` | `Right` | `Right` / `Left`. Anchors the panel. |
| `Width` | `string` | `"420px"` | Panel width. |
| `ShowBackdrop` | `bool` | `true` | Dim the main form behind the drawer. |
| `ChildContent` | `RenderFragment?` | — | The edit form body. |
| `ShowSubmit` | `bool` | `false` | Footer Submit button. |
| `SubmitText` | `string` | `"Save"` | |
| `OnSubmitAsync` | `Func<Task<bool>>?` | — | `true` ⇒ auto-close. |
| `ShowCancel` | `bool` | `false` | Footer Cancel button. |
| `CancelText` | `string` | `"Cancel"` | |
| `Busy` | `bool` | `false` | Optional external busy flag (disables footer). |

(No `Embedded` parameter — this component is always dialog-content-scoped.)

### 4.2 Positioning strategy

The drawer must fill `.fluent-dialog-body`, not the form-rows stack. Two
layers cooperate:

**Layer 1 — the dialog body becomes the containing block.**
`StudentEditDialog`'s root element gets `height: 100%` so it fills the
`1fr` body cell, and `position: relative` so it is the positioned ancestor.
Because the body row is `1fr`, the dialog-content root has a definite
height; `height: 100%` resolves against it. The drawer is then
`position: absolute; inset: 0` **inside that root**, so it fills the body
exactly — top under the title bar, bottom above the actions bar.

```
.fluent-dialog-body            (1fr grid cell, definite height)
└── .student-edit-dialog-root  (position: relative; height: 100%)
    ├── StudentFormFields       (main form: profile + sections + .form-actions)
    └── DialogDrawer           (position: absolute; inset: 0)  ← fills the body
        ├── backdrop           (position: absolute; inset: 0; dims the form)
        └── panel              (position: absolute; top/right/bottom: 0; width: Width)
```

This replaces the current `position: relative` on
`.student-form-fields__content-stack`. The content stack is no longer the
anchor; the dialog-content root is.

**Layer 2 — the panel anchors right (default).**
`.dialog-drawer-panel` is `position: absolute; top: 0; bottom: 0; right: 0`
(for `Side = Right`) or `left: 0` (for `Side = Left`), `width: Width`,
`max-width: 100%`. The backdrop is `position: absolute; inset: 0`. Both
are scoped under `.student-edit-dialog-root` so they never escape the
dialog body.

**Why this fixes the original bug:** the drawer's containing block is now
the whole dialog body (definite height from the grid `1fr` row), not the
form-rows stack (which is only as tall as its content). The drawer's top
edge is the top of the body (under the title bar) and its bottom edge is
the bottom of the body (above the actions bar), for every form height.

### 4.3 Where the drawer lives in the tree

`StudentEditDialog` becomes the host. It wraps `StudentFormFields` and
renders the `DialogDrawer` as a sibling **inside** the positioned root:

```razor
@* StudentEditDialog.razor (sketch — not implemented) *@
<div class="student-edit-dialog-root">
    <StudentFormFields Model="_model"
                       ...
                       EnableSectionEdit="true"
                       ActiveEditSection="@_activeEditSection"
                       ActiveEditSectionChanged="OnActiveEditSectionChanged"
                       SectionEditContent="@_sectionEditContent" />
    @if (_activeEditSection != StudentEditSection.None)
    {
        <DialogDrawer Open="@(_activeEditSection != StudentEditSection.None)"
                      OpenChanged="OnDrawerOpenChangedAsync"
                      Title="@_drawerTitle"
                      Side="DialogDrawerSide.Right"
                      Width="420px"
                      ShowCancel="true" CancelText="Cancel"
                      ShowSubmit="true"  SubmitText="Save"
                      OnSubmitAsync="OnDrawerSubmitAsync">
            @(_sectionEditContent)
        </DialogDrawer>
    }
</div>
```

The drawer is **owned by the dialog**, not by `ContactsEditor` or
`GuardianSection`. The child sections stop rendering their own drawers;
instead they **publish their edit form** (the inputs they currently put
inside their `<SideDrawer>`) up to the dialog, and the dialog hosts that
content in the single shared `DialogDrawer`.

### 4.4 How the edit content reaches the drawer

Two viable approaches — pick one (recommendation: **A**).

**A. RenderFragment publish-up (recommended).**
`StudentFormFields` gains an optional `[Parameter] RenderFragment? SectionEditContent`
plus an `EventCallback<RenderFragment?> SectionEditContentChanged`. When a
child section starts editing, it builds its edit form as a `RenderFragment`
and pushes it up; the dialog renders that fragment inside the `DialogDrawer`.
This keeps `ContactsEditor` / `GuardianSection` as the form authors (they own
their fields and validation) while the dialog owns the chrome.

```
ContactsEditor / GuardianSection  → build edit RenderFragment
  → StudentFormFields.SectionEditContentChanged → StudentEditDialog._sectionEditContent
  → <DialogDrawer>@_sectionEditContent</DialogDrawer>
```

The child also pushes a submit callback (e.g. `Func<Task<bool>>`) and a title
up via the same mechanism (a small `SectionEditContext` record carries
`Title`, `Content`, `OnSubmitAsync`).

**B. Fixed slot per section.**
`StudentFormFields` exposes two `RenderFragment` parameters
(`ContactsEditSlot`, `GuardiansEditSlot`) that the dialog fills with
`ContactsEditor`/`GuardianSection` edit markup. More boilerplate, less
flexible — only worth it if the edit forms are fully static.

### 4.5 State machine

```
[None]
  ├── Edit contact  → [Contacts drawer open]
  └── Edit guardian → [Guardians drawer open]

[Contacts drawer open]  --Cancel/✕/Escape/backdrop--> [None]
[Contacts drawer open]  --Save (OnSubmitAsync true)--> [None]
[Guardians drawer open] --Cancel/✕/Escape/backdrop--> [None]
[Guardians drawer open] --Save (OnSubmitAsync true)--> [None]
```

Only **one drawer is ever open** (you edit a contact or a guardian, never
both at once). The `StudentEditSection` enum (`None | Contacts | Guardians`)
already exists on `StudentFormFields` and is already two-way bound to
`StudentEditDialog._activeEditSection` — reuse it as the single source of
truth. The `DialogDrawer.Open` is derived: `Open = _activeEditSection != None`.

While a drawer is open:
- Profile fields are disabled (`AreProfileFieldsDisabled` already exists).
- The dialog's own `.form-actions` Cancel/Save are disabled (already wired
  via `Submitting || AreProfileFieldsDisabled`).
- Both Contacts and Guardians **section summaries stay visible** (dimmed by
  the drawer backdrop); neither is hidden.

### 4.6 Submit handling

Each child supplies its save handler as `Func<Task<bool>>`:
- `ContactsEditor` Buffered edit save → returns `true` on success, `false`
  on validation error (keeps the drawer open).
- `GuardianSection` edit save → `SaveEditGuardianAsync` already returns
  `Task<bool>` (returns `true` to close, `false` on error). Reuse as-is.

The dialog's `OnDrawerSubmitAsync` dispatches to whichever child owns the
active section. Because the child publishes its submit callback in the
`SectionEditContext`, the dialog just invokes it.

Cancel / ✕ / Escape / backdrop all flow through `OpenChanged(false)` →
the dialog resets `_activeEditSection = None` and the child clears its
in-memory working copy (the existing `CancelPanel` / `EndInlineEditAsync`
paths).

### 4.7 Migration of the two sections

**`ContactsEditor.razor`**
- Remove the embedded `<SideDrawer Embedded="true">` and the
  `_editDrawerOpen` / `OnEditDrawerOpenChangedAsync` / `SaveEditFromDrawerAsync`
  plumbing that was added for the embedded drawer.
- Keep the edit **fields** (channel + country code + value + label) and the
  `SaveEditAsync` mutation handler.
- Expose a method/fragment that builds the edit form for the drawer. The
  editor still owns its working-copy state (`_editChannel`, `_editValue`,
  etc.) and its validation.
- Live mode is untouched (still opens `ContactChangeDialog`).

**`GuardianSection.razor`**
- Remove the embedded `<SideDrawer Embedded="true">` and the
  `OnEditDrawerOpenChangedAsync` plumbing.
- Keep the edit **fields** (title/first/last for drafts, relationship/role,
  nested `ContactsEditor`) and the `SaveEditGuardianAsync` handler.
- Expose the edit form for the drawer.

**`StudentFormFields.razor`**
- Remove `position: relative` from `.student-form-fields__content-stack`
  (the dialog root is now the anchor, not the stack).
- Add the `SectionEditContent` / `SectionEditContext` publish-up plumbing.
- Keep the dimming of profile rows and disabling of `.form-actions`.

**`StudentEditDialog.razor`**
- Add the `.student-edit-dialog-root` wrapper (`position: relative;
  height: 100%`).
- Own the single `DialogDrawer` and the active-section state (already does).

### 4.8 CSS

New `DialogDrawer.razor.css` (scoped), mirroring `SideDrawer.razor.css`
naming where the visual is shared:

```css
.dialog-drawer-backdrop {
    position: absolute;
    inset: 0;
    background: rgba(0, 0, 0, 0.32);
    z-index: 40;            /* above the form, below the panel */
    animation: dialog-drawer-fade-in 120ms ease-out;
}
.dialog-drawer-panel {
    position: absolute;
    top: 0; bottom: 0; right: 0;   /* left:0 when Side=Left */
    width: var(--dialog-drawer-width, 420px);
    max-width: 100%;
    background: var(--neutral-layer-1, #fff);
    color: var(--neutral-foreground-1, #1f1f1f);
    box-shadow: -8px 0 24px rgba(0,0,0,0.18);
    z-index: 41;
    display: flex; flex-direction: column;
    animation: dialog-drawer-slide-in 180ms ease-out;
    outline: none;
}
/* header / body / footer / buttons: same as SideDrawer */
```

`StudentEditDialog` root (scoped `.razor.css` or a shared class):

```css
.student-edit-dialog-root {
    position: relative;
    height: 100%;
}
```

Open question: confirm `.fluent-dialog-body` resolves `height: 100%` on a
child. The body row is `1fr` and the body element is `grid-area: dialog-body`
with `min-height: 80px`, so it should stretch; verify with a quick
browser check before locking the CSS (see §9 risks).

### 4.9 Accessibility

- Panel: `role="dialog"`, `aria-modal="true"`, `aria-label="@Title"`.
  (Unlike the full-viewport `SideDrawer` Embedded mode, this drawer **is**
  the active dialog surface, so `aria-modal="true"` is correct.)
- On open, focus the panel (reuse the `SideDrawer` `_panel.FocusAsync()`
  pattern).
- Escape closes the drawer only; `stopPropagation` so the parent
  `FluentDialog` does not also close on Escape.
- On close, return focus to the triggering Edit button (add `@ref` to the
  contact/guardian Edit buttons).
- Backdrop click closes (cancel). The main form behind is disabled
  (`pointer-events: none` on profile rows already), so the only interactive
  surface is the drawer.
- Keyboard: the drawer content is normal tab order; the footer Cancel/Save
  are real buttons.

## 5. Files (planned changes — not yet made)

New:
- `src/SchoolCollab.Admin.Shared/Components/DialogDrawer.razor`
- `src/SchoolCollab.Admin.Shared/Components/DialogDrawer.razor.css`
- `src/SchoolCollab.Admin.Shared/Components/DialogDrawerSide.cs` (enum:
  `Right`, `Left`)
- `docs/plans/2026-08-18-dialog-content-drawer.md` (this file)

Modified:
- `src/Students/SchoolCollab.Students.Application/Components/Students/StudentEditDialog.razor`
  — host the `DialogDrawer`; add the positioned root; own the active-section
  state (already does) and the section-edit context.
- `src/Students/SchoolCollab.Students.Application/Components/Students/StudentEditDialog.razor.css`
  (new if not present) — `.student-edit-dialog-root { position: relative;
  height: 100% }`.
- `src/Students/SchoolCollab.Students.Application/Components/Students/StudentFormFields.razor`
  — remove `position: relative` from the content stack; add the
  section-edit-content publish-up; keep profile dimming + actions disabling.
- `src/Students/SchoolCollab.Students.Application/Components/Students/StudentFormFields.razor.css`
  — remove `.student-form-fields__content-stack { position: relative }`.
- `src/SchoolCollab.Admin.Shared/Components/ContactsEditor.razor`
  — remove its embedded `SideDrawer`; expose the edit form for the drawer.
- `src/Students/SchoolCollab.Students.Application/Components/Students/GuardianSection.razor`
  — remove its embedded `SideDrawer`; expose the edit form for the drawer.

Tests:
- `tests/SchoolCollab.Admin.Tests.Unit/StudentFormFieldsSectionEditTests.cs`
  — assert the `DialogDrawer` is hosted by `StudentEditDialog`, fills the
  dialog-content root (not the content stack), and that neither section is
  hidden while a drawer is open.
- `tests/SchoolCollab.Admin.Tests.Unit/ContactsEditorTests.cs` — update
  drawer-host expectations.
- New `tests/SchoolCollab.Admin.Tests.Unit/DialogDrawerTests.cs` —
  source-level assertions for the `DialogDrawer` API (Open/Title/Side/
  ShowCancel/ShowSubmit/OnSubmitAsync) and the absolute-fill CSS class.

## 6. Acceptance criteria

- [ ] Opening a contact edit shows the `DialogDrawer` anchored right inside
      the dialog body; its top edge sits **under** the dialog title bar and
      its bottom edge sits **above** the dialog Cancel/Save row — for both a
      short form and a tall (scrollable) form.
- [ ] Opening a guardian edit shows the same `DialogDrawer` with the
      guardian edit form (relationship/role + nested contacts editor).
- [ ] The drawer never overlaps the title bar or the actions bar at any
      dialog height.
- [ ] The drawer fills the full dialog content area height (no gap below
      the drawer, no overflow into the title bar).
- [ ] The main form (profile + both section summaries) stays visible and
      dimmed behind the backdrop; neither section is hidden.
- [ ] The dialog's own Cancel/Save buttons are disabled while a drawer is
      open; the drawer's own Cancel/Save are the active controls.
- [ ] Save closes the drawer on success and keeps it open on validation
      error (handler returns `false`).
- [ ] Cancel / ✕ / Escape / backdrop click all close the drawer and discard
      the working copy, without closing the parent `FluentDialog`.
- [ ] Focus moves into the drawer on open and returns to the triggering
      Edit button on close.
- [ ] `Side = Left` anchors the panel on the left.
- [ ] All existing Admin unit tests pass; new `DialogDrawer` tests pass;
      Students unit tests pass.
- [ ] The shared `SideDrawer` (AI chat panel etc.) is unchanged.

## 7. Open questions

1. **Body height resolution.** Confirm `.fluent-dialog-body` gives a child
   `height: 100%` a definite height (the grid `1fr` row should). If not,
   fall back to `position: absolute; inset: 0` on the drawer with the body
   itself as the positioned ancestor via a scoped global rule
   (`.fluent-dialog-body:has(.student-edit-dialog-root) { position: relative }`)
   — confirm before locking CSS.
2. **Publish-up vs slot.** Confirm approach A (RenderFragment publish-up)
   is acceptable; it is the least boilerplate but means the child sections
   build `RenderFragment`s at runtime.
3. **Nested contacts editor on a draft guardian.** Editing a contact on a
   **draft** guardian currently opens a second embedded `SideDrawer`
   (Buffered `ContactsEditor`). With the single-drawer model, only one
   `DialogDrawer` exists — so the draft-guardian contact add/edit must run
   **inline inside the guardian drawer body** (not in a second drawer).
   Decide: (a) inline within the drawer body (recommended), or (b) a second
   `DialogDrawer` instance for the nested case (re-introduces the
   drawer-in-drawer). The single-drawer model favours (a).
4. **`DialogSize.ExtraLarge` width.** The edit dialog opens at
   `DialogSize.ExtraLarge`. Confirm the 420px drawer + main form fit
   comfortably at that width; if not, widen the drawer or the dialog.
5. **Backdrop vs no-backdrop sidebar mode.** Do we want an option where the
   main form is **not** dimmed and remains fully visible (a true sidebar /
   split-pane) instead of a dimming overlay? Add a `Mode` parameter
   (`Overlay` (default, with backdrop) vs `Sidebar` (no backdrop, main form
   stays interactive)) if that UX is wanted. Out of scope for v1 unless
   requested.

## 8. Risks

- **Dialog body height.** If the `1fr` grid row does not give the body a
  definite height, `height: 100%` collapses and the drawer vanishes. Mitigation:
  verify in the browser first; fallback to scoping `position: relative` on
  `.fluent-dialog-body` via a `:has()` rule.
- **Escape propagation.** `FluentDialog` may close on Escape. The drawer's
  `keydown` handler must `preventDefault`/`stopPropagation` for Escape so
  only the drawer closes. Verify `FluentUI`'s dialog Escape behavior.
- **Focus trap.** A `role="dialog"` panel inside an already-open
  `FluentDialog` can confuse screen readers. Keep `aria-modal="true"` on
  the drawer and ensure focus stays within the drawer while open; consider
  a focus-trap utility if the tab order escapes.
- **Z-index.** The dialog header/footer are siblings of the body in the
  same stacking context. The drawer at `z-index: 41` inside the body should
  not paint over them (they are outside the body), but verify the dialog
  control's stacking context does not let the drawer escape.
- **Two-way `RenderFragment` publishing.** Building `RenderFragment`s in
  C# and passing them up is unusual but supported; ensure the fragment is
  rebuilt when the edited item changes (key the drawer body by the edited
  item id).

## 8.5 Reworks after code review (post-implementation)

A code review of the first implementation surfaced five findings; this
section records the reworks applied to address them and how they map
back to the original plan.

### Finding A — Nested `ContactsEditor` (draft guardian) no-op on Edit

**Issue:** the nested `ContactsEditor` rendered inside the draft-guardian
edit form (`BuildEditFragment`) was wired without `SectionEditContextChanged`
or `IsEditingChanged`. The user could click "Edit contact" but nothing
rendered — a silent no-op.

**Rework (as implemented):** add an `[Parameter] bool EditDisabled` to
`ContactsEditor` that suppresses **only** the per-row Edit button.
`GuardianSection.BuildEditFragment` passes `EditDisabled="true"` for the
nested `ContactsEditor`, so the silent no-op trigger is removed while a
guardian edit is in flight. Add and Remove stay functional — they are not
no-ops (Add mutates the draft guardian's `_editContacts` directly; Remove
opens the reason dialog and mutates the list), so the operator can still
manage the guardian's contacts while editing the guardian, and Save/Cancel
the guardian as a unit. (The earlier draft of this rework text said to also
disable the add-row and render the list read-only; that was over-specified —
Add/Remove are legitimately useful and were left enabled.) This satisfies
the plan's "no nested drawer" rule (§4.4 / §7) by removing the trigger,
not by re-introducing drawer-in-drawer.

### Finding B — Section-swap can silently lose guardian edits

**Issue:** clicking Edit on a guardian (drawer opens with `Update
guardian`) and then clicking Edit on a contact in the outer Contacts
section would replace `_sectionEditContent` with the contact context
without invoking the guardian context's `Cancel`. The guardian section's
`_editingIndex` / `_edit*` state remained populated. If the user then
closed the contact drawer and clicked the dialog's Save button
(`AreProfileFieldsDisabled` already `false` because the contact edit's
end fired `IsEditingChanged(false)`), the dialog would save the model
without the guardian's pending edits — silent data loss.

**Rework:** in `StudentEditDialog.OnSectionEditContentChanged`, when a
new context arrives and a previous context is set, call the previous
context's `Cancel()` before swapping. This guarantees the previously-
editing section's `CancelPanel`/`CancelInlineEditAsync` runs, which
clears its internal state and fires `IsEditingChanged(false)` to reset
`_activeEditSection` to `None`. Mid-flight section swaps can no longer
leave a section with half-finished edits invisible to the dialog.

### Finding C — `EditFormContent` parameter is dead code

**Issue:** both `ContactsEditor` and `GuardianSection` declared
`[Parameter] RenderFragment? EditFormContent` and rendered `@if
(EditFormContent is not null) { @EditFormContent }`, but no host ever
wired it (the publish-up mechanism via `SectionEditContextChanged` is
the sole host-driven flow). Dead code that misled readers.

**Rework:** remove `[Parameter] RenderFragment? EditFormContent` and the
`@if (EditFormContent is not null)` branch from both components. Keep
`SectionEditContextChanged` as the only publish-up channel.

### Finding D — Focus not restored to triggering button after close

**Issue:** the plan called for focus restore on close; the
implementation focused the panel on open but did not restore focus
afterwards, leaving the operator at `<body>` after Cancel/Save.

**Rework (as implemented):** on open, capture a CSS selector for the
currently-focused element via `IJSRuntime` interop (best-effort, wrapped
in try/catch). On close, restore focus to that selector via interop. Use
a stable selector string rather than `ElementReference` because
`ElementReference` IDs are scoped to the capturing component and cannot
be focused from another instance. Falls back silently if the captured
element no longer exists (or was never uniquely identifiable). The JS
module lives at `wwwroot/js/dialogDrawer.js` and exposes two named
exports — `captureActiveElementSelector()` and `focusBySelector(selector)`
— which `DialogDrawer.OnAfterRenderAsync` imports once via the standard
`JS.InvokeAsync<IJSObjectReference>("import",
"./_content/SchoolCollab.Admin.Shared/js/dialogDrawer.js")` pattern and
then invokes through the `IJSObjectReference`. (The earlier draft of
this rework text specified a `wwwroot/lib/schoolcollab/dialog-drawer.js`
path, a global `SchoolCollab.dialogDrawer.*` namespace, and
`.lib.module.js` auto-discovery; the implemented explicit-import /
named-export approach is simpler and is what shipped — no global is
registered because nothing calls it.)

`captureActiveElementSelector` walks up from `document.activeElement` to
the first ancestor with an `id` that still resolves to itself and returns
`#id`; when no ancestor has an `id` it returns `null` (rather than a
`tagName` fallback, which `querySelector(tagName)` would resolve to the
*first* element of that tag on the page — almost never the trigger).

### Finding E — Nested `aria-modal="true"` accessibility violation

**Issue:** the drawer panel set `aria-modal="true"`, but it renders
inside an already-modal `FluentDialog`. Nested modal declarations
confuse screen readers and double-trap focus.

**Rework:** change the panel from `role="dialog" aria-modal="true"` to
`role="region"`. The drawer is a region inside the existing dialog,
not a separate modal surface. Add `aria-labelledby` pointing at the
drawer's `<h3>` title (with a per-instance id so multiple drawers on
the same page each get a unique target). The outer FluentDialog keeps
its modal semantics; the drawer is now correctly a non-modal region
inside it.

## 8.6 Post-implementation bug: contact edit drawer threw on channel render

**Reported:** "side drawer breaks on contact edit in student edit dialog."

**Root cause (primary):** `ContactsEditor.BuildEditFragment` built the
channel picker as `DropdownForEnum<ContactChannel>` with parameters named
`Value` / `ValueChanged`. `DropdownForEnum<TEnum>` exposes
`SelectedValue` / `SelectedValueChanged`, not `Value` / `ValueChanged`.
`ComponentBase.SetParametersAsync` throws `InvalidOperationException` for an
unmatched `[Parameter]`, so rendering the contact edit fragment threw the
instant the drawer opened — the drawer showed an unhandled error instead of
the edit form. (The guardian edit fragment used the correct names, which is
why only the contact edit broke.) This was latent because no test rendered
the published fragment — the publish-up tests capture the `SectionEditContext`
and drive `Submit` / `Cancel` via reflection, so the render path was
unexercised.

**Root cause (secondary — frozen fragment):** even after the param-name
fix, the published `RenderFragment` is rendered by the host `DialogDrawer`
and then held frozen: it only re-executes when the host re-renders, which
only happens when `StudentEditDialog` re-renders, which only happens when
`_sectionEditContent` / `_activeEditSection` change. Editing the channel
updates `ContactsEditor._editChannel` but does not change the dialog state,
so the channel-gated country-code `CodedValueDropdown` (`if (_editChannel is
SMS or WhatsApp)`) never appeared / disappeared. (`CodedValueDropdown` and
`FluentTextField` self-render their own selection / typed text, so only the
conditional field was frozen.)

**Fix:**

1. Correct the channel picker parameter names to `SelectedValue` /
   `SelectedValueChanged` so the fragment renders.
2. `ContactsEditor.OnEditChannelChanged(ContactChannel)` re-publishes the
   `SectionEditContext` on channel change so the host re-renders the
   fragment with the new channel (revealing / hiding the country-code
   field). It also loads country calling codes on demand when entering
   SMS / WhatsApp and clears a stale country-code selection when leaving
   them. The value / label / country-code pickers are left as plain
   `ValueChanged` lambdas (no re-publish) — `FluentTextField` holds typed
   text natively and `CodedValueDropdown` self-renders its selection, so
   they need no host re-render; re-publishing on every keystroke would risk
   the known FluentUI cursor-reset quirk.
3. The swap-cancel in `StudentEditDialog.OnSectionEditContentChanged` is now
   `SectionKey`-aware: a same-section re-publish (equal `SectionKey`) just
   adopts the new fragment (does NOT cancel the in-flight edit); only a
   genuine cross-section swap (different `SectionKey`) cancels the previous.
   `SectionEditContext` gained a `SectionKey` field
   (`"Contacts"` / `"Guardians"`) for this. Without it, re-publishing on
   channel change would have torn down the edit (the previous `ReferenceEquals`
   check treated every new context instance as a swap).

**Regression test:** `BufferedEdit_ChannelChange_RePublishesAndRevealsCountryCodeField`
renders the published fragment for an Email contact (1 `fluent-select` —
channel only), switches the channel to SMS via `OnEditChannelChanged`, and
asserts the re-published fragment has 2 `fluent-select` (channel +
country-code). This is the first test that actually renders the published
fragment, so it guards both the param-name render bug and the reactivity
regression.

## 9. Out of scope

- Changing the shared `SideDrawer`.
- `ContactsEditor` Live mode / `ContactChangeDialog`.
- `StudentCreateDialog`.
- The page-side `/students/{id}/edit` (`Edit.razor`) — it uses
  `StudentFormFields` in `Linked` mode without a drawer; unaffected.
- Mobile responsive behavior (dialog is desktop-first; drawer `max-width:
  100%` handles narrow bodies but is not a designed mobile layout).