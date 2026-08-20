# Plan: Dialog height control on drawer open + compact guardian contact manager in the edit drawer

Status: **IMPLEMENTED** — Features A/B/C/D are landed on
`feature/contact-guardian-form-fields-consolidation` (uncommitted). Feature A
uses the content-fill host wrapper (`.student-edit-dialog-root { max-height: 72vh;
min-height: 320px; overflow: hidden }`) and the `height` call-site argument is
dropped. Build green; Admin + Students unit tests pass. The only remaining item
is an in-browser confirmation that the drawer no longer overshoots (§7.2), which
cannot be settled by code/tests alone. See §10 for the full progress + residual
risks.

## 1. Problem

The student edit dialog (`StudentEditDialog`) opens at `DialogSize.ExtraLarge`.
When the shared `DialogDrawer` slides in to edit a contact or a guardian, it
fills the dialog's **body** (`1fr` grid row) top-to-bottom. That height is the
dialog height minus title bar minus actions — a fixed quantity the drawer
cannot grow. Three UX frictions follow:

1. **The drawer overshoots the dialog's bottom.** The operator gets
   whatever body height the ExtraLarge preset gave them, and the shared
   `DialogDrawer` (`position: absolute; inset: 0`) is supposed to fill that
   body. But FluentUI's `.fluent-dialog-body` is `height: auto`, so the host
   wrapper's `height: 100%` resolves to `auto` — the drawer then has no
   containing-block height to clamp it and sizes to its own content, which
   can exceed the dialog box (the drawer spills past the bottom). Pinning the
   dialog box taller via `DialogParameters.Height` does not fix this: the box
   grows but the body stays `height: auto`, so the drawer still overshoots.
   We want the dialog body to be the height authority (content-filled, capped,
   scrollable) so the drawer is clamped to it and the bottom actions stay
   pinned (see §4.1).

2. **Guardian edit shows a full contacts editor, not a compact manager.**
   Inside the guardian edit drawer, the contacts area renders a **full**
   `ContactsEditor` (the same add-row + per-row action list used on the
   page). That is heavy chrome for a 420px-wide drawer: an always-visible add
   row plus a tall list. Space is limited. We want each contact on a single
   line with an **Edit** + **Remove** icon, a focused single-contact form that
   appears only when asked for (Edit a row, or click an "Add contact"
   anchor), and reorder done by **selecting a line and using outside
   up/down buttons** — not by a cluster of buttons inside every row. Today
   adding a contact and editing one are two different visual patterns (inline
   add-row vs. nothing compact), which is confusing inside a drawer.

3. **Drawer title lacks the guardian identity.** When editing a guardian the
   drawer header just says "Edit guardian". It does not show *which* guardian
   — the operator has to recall it from the dimmed card behind. The drawer
   should announce the guardian's name.

## 2. Goal

Deliver three related behaviors in the student edit dialog:

**A. Dialog body fills to content height; drawer is clamped to the body.**
Give the student edit dialog a content-filled, capped, scrollable body so the
shared drawer — which overlays the body — has a usable vertical area and
can no longer overshoot the dialog's bottom. The chosen path (§4.1) makes the
host wrapper (`.student-edit-dialog-root`) the height authority
(`max-height: 72vh; min-height: 320px; display: flex; flex-direction: column;
overflow: hidden`) and drops the earlier fixed `DialogParameters.Height`
approach (it pinned the box but not the `height: auto` body, so the drawer
overshot). The dialog grows to its content up to `72vh`; beyond that the form
scrolls inside the body. Bottom actions stay pinned by FluentUI's dialog grid.

**B. Compact single-line contacts inside the guardian edit drawer.**
Replace the nested full `ContactsEditor` in `GuardianSection`'s Edit view with
a compact manager:

- Each contact renders as a **single line**: channel glyph + formatted value
  (+ optional label) + an **Edit** icon + a **Remove** icon. The line is
  **selectable** (click to highlight).
- **Reorder is done from outside the list**, not inside a row: a small
  up/down toolbar acts on the selected contact. No reorder buttons live
  inside a contact line.
- Clicking a row's Edit icon **switches an inner `<div>`** inside the drawer
  body (not a second `DialogDrawer`) to a single-contact edit form
  (`ContactFormFields`) bound to that contact's working copy.
- An **"Add contact" `FluentAnchor`** switches the same inner `<div>` to a
  blank single-contact add form. Only one inner surface is ever shown at a
  time (list ⇄ add ⇄ edit).

**C. Guardian identity in the drawer.**
When editing a guardian, the drawer header shows the guardian's display name
(e.g. **"Edit · Jane Smith"**) and the drawer body shows the name as a
visible header, not just a generic title. The host resolves the name from the
guardian being edited.

**Non-goals**
- Do not change the shared `SideDrawer` (AI chat panel etc.).
- Do not change `ContactsEditor` **Live** mode (`ContactChangeDialog`,
  reason-and-audit) — existing guardians editing a contact outside the drawer
  keep that flow.
- Do not change the page-side `/students/{id}/edit` (`Linked` mode) — no
  drawer there.
- Do not change `StudentCreateDialog`.
- Do not add a second nested `DialogDrawer` (the drawer-in-drawer anti-pattern
  stays out). The existing `ContactChangeDialog` reason modal is a
  `FluentDialog`, not a `DialogDrawer`; it is **not** used inside the guardian
  drawer in this spec (see §4.2 — existing-guardian add/edit/remove are
  deferred to the page).
- **Mark-verified** on a contact is a page-level Live operation; it is not
  rendered in the compact drawer manager.

## 3. Grounding (current architecture)

### 3.1 Dialog sizing + drawer filling

- `StudentEditDialog` is opened by `GradeLevels/Detail.razor` via
  `DialogService.ShowReadonlyDialogAsync<StudentEditDialog>("Edit Student · …",
  { StudentIdKey }, DialogSize.ExtraLarge)`.
- `ShowReadonlyDialogAsync` signature is
  `(IDialogService, string title, IDictionary<string,object?> parameters,
  DialogSize size = Medium)` — it takes **only a `DialogSize` preset**, not an
  explicit height. It builds the shell via `BuildShellParameters(title, size)`,
  which sets `Width = size.ToCssWidth()` and **no height**. There is **no
  "taller" `DialogSize`** (presets only vary width). The earlier revision added
  an optional `height` parameter forwarded to `DialogParameters.Height`; the
  content-fill revision (§4.1) **keeps the parameter** (harmless, may serve
  other callers) but `StudentEditDialog`'s open call **no longer passes it**.
- **Dialog box height is FIXED by `--dialog-height`; the BODY is `height: auto`
  (verified against FluentUI 4.14.2).** The `fluent-dialog` host defaults
  `--dialog-height: 480px`; the dialog box `::part(control)` is
  `height: calc(var(--dialog-height) - 2 * padding)` (a fixed height, no
  `min/max/auto`). **The body (`.fluent-dialog-body`) is the `1fr` grid row but
  has `height: auto; overflow-y:auto`** — it is NOT a definite height. This is
  the root cause of the drawer overshoot (§4.1): the host wrapper's
  `height: 100%` resolves to `auto` against an `auto`-height body, so
  `DialogDrawer` (`position: absolute; inset: 0`) has no containing-block
  height to clamp it and sizes to its own content.
- **`DialogParameters.Height` pins the box, not the body.** It is a real
  property ("a valid CSS height value like '600px' or '3em'") that FluentUI
  applies as `--dialog-height`, overriding the 480px default. Setting it grows
  the fixed **box** → but the body stays `height: auto`, so the wrapper's
  `height: 100%` still resolves to `auto` and the drawer still overshoots.
  That is why the explicit-height path was revised (§4.1). `DialogParameters`
  has `Height`, `Width`, and `DialogBodyStyle` but **no `MinHeight`/`MaxHeight`**.
- **The content-fill lever is the host wrapper, not `--dialog-height`.** Giving
  `.student-edit-dialog-root` a definite height (`max-height: 72vh;
  min-height: 320px`) makes the wrapper the height authority: the drawer's
  `inset: 0` is then clamped to that definite height and can no longer
  overshoot. This is independent of FluentUI's `height: auto` body sizing.
- `StudentEditDialog`'s root is `.student-edit-dialog-root`
  (`position: relative; height: 100%`, in `StudentEditDialog.razor.css`) —
  the content-fill revision (§4.1) changes this to `position: relative;
  display: flex; flex-direction: column; max-height: 72vh; min-height: 320px;
  overflow: hidden`.
- `DialogDrawer` renders `position: absolute; inset: 0` inside that root and
  fills the body exactly; its panel is `top/bottom: 0; right: 0; width: 420px`
  (`DialogDrawer.razor`).
- `DialogDrawer` parameters today: `Open/OpenChanged/Title/Side/Width/
  ShowBackdrop/ShowCancel/CancelText/ShowSubmit/SubmitText/OnSubmitAsync/Busy`.
  No height signal exists, and none is added by this spec.
- Drawer body scrolls independently (`.dialog-drawer-body`). The panel is
  `role="region"` inside the already-modal `FluentDialog` (from the
  post-implementation rework in `2026-08-18-dialog-content-drawer.md`).

### 3.2 Guardian edit view + nested contacts

`StudentEditDialog` hosts a single `DialogDrawer`; for `_editor ==
ActiveEditor.Guardians` it renders:

```
<GuardianSection View="GuardianSection.GuardianView.Edit"
                 GuardianLinks="_model.GuardianLinks"
                 Mode="StudentFormFieldsMode.Inline"
                 StudentId="StudentId"
                 IsAdd="@_isAdd"
                 InitialEditIndex="@_editingGuardianIndex" />
```

Inside `GuardianSection`'s Edit view, the contacts area is:

```
<div class="guardian-edit-contacts">
    @if (editedGuardian.ExistingGuardianId is { } gid)
        <ContactsEditor OwnerType="Guardian" OwnerId="@gid" EditDisabled="true" />
    else
        <ContactsEditor Mode="Buffered" OwnerType="Guardian"
                        Contacts="_editContacts" ContactsChanged="OnEditContactsChanged"
                        EditDisabled="true" />
</div>
```

So a draft guardian's contacts are a Buffered `ContactsEditor` (`_editContacts`),
and an existing guardian's contacts are a Live `ContactsEditor` loaded from
`OwnerId`. `EditDisabled="true"` currently suppresses the per-row **Edit**
button and the **Add** button (both are gated by `EditDisabled`; the Remove,
move-up/down, and mark-verified buttons are not). So today, inside the
guardian drawer the operator **cannot add or edit** a contact value, but can
**remove**, **reorder**, and (Live) **mark verified**. This spec closes the
add/edit gap for **drafts** and preserves reorder (via the new select+outside
model); it defers existing-guardian add/edit/remove/verify to the page (see
§4.2 — a noted behavior change).

`ContactsEditor` exposes `ContactsView { Full, Readonly, Edit }` and
`EditorMode { Live, Buffered }`. The Readonly view already renders a
single-line contact item (`.contact-item`: channel + value + optional label +
`Edit contact` + `Remove contact` icon buttons) plus an "Add contact"
`FluentAnchor` — this is the visual pattern to mirror for the guardian
compact manager. The Edit view renders the single `ContactFormFields` group
(channel + country code + value + label, `Model`-bound) + a commit button.

`GuardianSection` currently injects `StudentsApiClient`, `CodedValuesApiClient`,
and `ILogger<GuardianSection>` — **no `IContactsClient`** and no
`IDialogService`. The Live nested `ContactsEditor` does its own contact
load/mutate/reorder against `IContactsClient` internally. This spec moves
existing-guardian **reorder** into `GuardianSection` (Live reorder API), so
`GuardianSection` gains `[Inject] IContactsClient` (see §5).

### 3.3 Drawer title

`StudentEditDialog.GetDrawerTitle()` returns a static string:

```
Contacts  → "Add contact" / "Edit contact"
Guardians → "Add guardian" / "Edit guardian"
```

No name is included. `GuardianAssignment` carries `FirstName`/`LastName`. The
host passes `GuardianLinks="_model.GuardianLinks"`, so the edited guardian is
`_model.GuardianLinks[_editingGuardianIndex]`.

## 4. Design

### 4.1 Feature A — Content-fill dialog body + constrained drawer

**Decision: let the dialog body fill to its content height and constrain the
drawer to that body — do NOT pin a fixed dialog height via `DialogParameters.Height`.**
The earlier "explicit-height open path" (pass `height: "max(72vh, 480px)"` to
`DialogParameters.Height`) was **revised** after an in-browser check showed the
side drawer overshooting the dialog's bottom. Root cause: FluentUI's
`.fluent-dialog-body` is `height: auto` (§3.1), so the host wrapper's
`height: 100%` resolves to `auto` — the `DialogDrawer` (`position: absolute;
inset: 0`) then has no containing-block height to clamp it and sizes to its own
content, which can exceed the fixed `--dialog-height` box. Pinning the box
taller does not fix this: the drawer is still unconstrained by the body.

The revised approach makes the **body** the height authority (content-fill,
capped, scrollable) and lets the dialog box grow to that body. The bottom
actions stay pinned by FluentUI's grid (title / body / actions); the drawer is
clamped to the body because the body now has a definite height.

**Mechanism.**

1. **Drop the `height` argument on `StudentEditDialog`'s open call.**
   `GradeLevels/Detail.razor` no longer passes `height: "max(72vh, 480px)"`.
   The `height` parameter stays on `DialogServiceExtensions` (it is harmless
   and may serve other callers) but `StudentEditDialog` omits it, so the dialog
   box is not pinned to a fixed `--dialog-height`.
2. **Make the dialog body a definite-height flex column.** In
   `StudentEditDialog.razor.css`, the root wrapper
   (`.student-edit-dialog-root`) becomes the height authority:
   ```css
   .student-edit-dialog-root {
       position: relative;        /* stays the drawer's containing block */
       display: flex;
       flex-direction: column;
       max-height: 72vh;          /* cap so very tall content scrolls, not grows the dialog off-screen */
       min-height: 320px;         /* floor so a bare profile form is not cramped */
       overflow: hidden;          /* the drawer/body scroll internally, not the root */
   }
   ```
   The wrapper now has a **definite** height (`max-height: 72vh` clamps it;
   content fills up to that cap), so `DialogDrawer`'s `position: absolute;
   inset: 0` is clamped to the wrapper and can no longer overshoot.
3. **Form content scrolls inside the body; actions stay pinned.** The form
   region inside the root (the `StudentFormFields` host area) gets
   `flex: 1 1 auto; min-height: 0; overflow: auto` so a tall form scrolls
   within the capped body. FluentUI's dialog grid keeps the actions row as a
   fixed `auto`-height row below the body, so Cancel/Save stay anchored at the
   dialog's bottom. No `position: sticky` is needed — the grid does it.
4. **No `DialogDrawer` change, no per-toggle class, no global CSS rule.** The
   drawer's existing `position: absolute; inset: 0` + `display: flex;
   flex-direction: column` + `.dialog-drawer-body { flex: 1 1 auto; min-height: 0;
   overflow: auto }` already scroll the drawer's own content; the only missing
   piece was a definite containing-block height, which step 2 now provides.

**Height policy.** The dialog grows to its content up to `72vh` (the root's
`max-height`); on short viewports the `320px` floor keeps the bare form usable.
Beyond `72vh`, the form scrolls inside the body — the dialog never grows past
the viewport. The dialog does **not** grow on drawer open or shrink on close;
the drawer simply overlays the (already content-filled) body. Because the body
is now the height authority, there is no "wasted space when no drawer is open"
trade — the body is exactly the form's height (capped).

**Why not the earlier explicit-height open path.** Setting
`DialogParameters.Height` pins `--dialog-height` on the **box**, but the box's
`height: calc(var(--dialog-height) - 2*padding)` does not propagate a definite
height to `.fluent-dialog-body` (which is `height: auto`). The wrapper's
`height: 100%` then resolves to `auto`, and the drawer overshoots. The
content-fill path fixes the root cause by giving the wrapper itself a definite
height (`max-height` + `min-height`), independent of FluentUI's body sizing.

**Why not the per-toggle CSS approach.** It required a global
`fluent-dialog:has(.student-edit-dialog-root--drawer-open)::part(control)`
rule reaching into FluentUI's shadow DOM, which is not reliably supported
across the dialog's shadow boundary, and a scoped `.razor.css` cannot reach
the `<fluent-dialog>` element at all (it is rendered by FluentUI's
`IDialogService` host, not by `StudentEditDialog`). The content-fill path
sidesteps both issues by sizing the **host wrapper** (which `StudentEditDialog`
owns) instead of the FluentUI box.

### 4.2 Feature B — Compact single-line guardian contacts with selection reorder + inner edit toggle

**Recommendation.** Build the compact manager inside `GuardianSection`'s Edit
view, reusing `ContactFormFields` for the focused sub-form. Do **not** render
a nested `ContactsEditor` Full view there anymore.

The Edit view's contacts area becomes (sketch):

```
<div class="guardian-contact-manager">
    @if (_contactEditTarget is null)
    {
        @* LIST surface *@
        @if (ContactList.Count == 0)
        {
            <span class="guardian-contacts-empty">— no contacts —</span>
        }
        else
        {
            <ul class="guardian-contact-single-lines">
                @foreach (var c in ContactList)
                {
                    var selected = _selectedContactKey == c.Key;
                    <li class="guardian-contact-line @(selected ? "guardian-contact-line--selected" : null)"
                        @onclick="SelectContact(c.Key)">
                        <span class="guardian-contact-glyph">@ChannelGlyph(c.Channel)</span>
                        <span class="guardian-contact-value">@FormatContactValue(c)</span>
                        @if (!string.IsNullOrWhiteSpace(c.Label))
                        { <span class="guardian-contact-label">(@c.Label)</span> }
                        @* Edit + Remove icons only — NO reorder buttons here. *@
                        <span class="guardian-contact-actions">
                            @if (CanEditContact(c))
                            {
                                <FluentButton Appearance="Lightweight" Title="Edit contact"
                                              IconStart="@FluentIcons.Edit"
                                              OnClick="StartContactEditAsync(c)" />
                            }
                            @if (CanRemoveContact(c))
                            {
                                <FluentButton Appearance="Lightweight" Title="Remove contact"
                                              IconStart="@FluentIcons.Delete"
                                              OnClick="RemoveContactAsync(c)" />
                            }
                        </span>
                    </li>
                }
            </ul>
        }

        @* Reorder toolbar — OUTSIDE the list, acts on the selected line. *@
        <div class="guardian-contact-reorder-bar">
            <FluentButton Appearance="Lightweight" Title="Move up (higher priority)"
                          IconStart="@FluentIcons.ChevronUp"
                          Disabled="@(_selectedContactKey is null || IsContactFirst(_selectedContactKey))"
                          OnClick="MoveContactUpAsync" />
            <FluentButton Appearance="Lightweight" Title="Move down (lower priority)"
                          IconStart="@FluentIcons.ChevronDown"
                          Disabled="@(_selectedContactKey is null || IsContactLast(_selectedContactKey))"
                          OnClick="MoveContactDownAsync" />
        </div>

        @* Add affordance — anchor, not a button (space is limited). *@
        @if (CanAddContact())
        {
            <FluentAnchor Href="#" Appearance="Appearance.Hypertext"
                          OnClick="StartContactAddAsync" class="guardian-contact-add-anchor">
                Add contact
            </FluentAnchor>
        }
    }
    else
    {
        @* Focused single-contact add / edit sub-panel (inline, not a second drawer) *@
        <ContactFormFields Model="@_contactEditModel"
                           ChannelChanged="OnGuardianContactChannelChanged"
                           ValueLabel="@ContactValueLabel"
                           ValuePlaceholder="@ContactValuePlaceholder"
                           OptionText="@FormatCountryCodeOption" />
        <div class="guardian-contact-actions">
            <FluentButton Appearance="Lightweight" OnClick="CancelContactEditAsync">Cancel</FluentButton>
            <FluentButton Appearance="Accent" OnClick="CommitContactAsync">
                @(_contactEditTarget.IsAdd ? "Add" : "Save")
            </FluentButton>
        </div>
    }
</div>
```

**Reorder model (selection + outside up/down).**
- The compact list is **selectable**: clicking a contact line sets
  `_selectedContactKey` and the line gets a highlight class
  (`.guardian-contact-line--selected`). Only one contact is selected at a time.
- A small reorder toolbar **outside** the list holds two icon buttons:
  **Move up** (↑, higher priority) and **Move down** (↓, lower priority). They
  act on the selected contact. Each is disabled when nothing is selected or
  the selected is already first/last.
- **Move up** swaps the selected contact with the one above (lower `Order`
  index → higher priority; `Order 0` is the preferred/top contact). **Move
  down** swaps with the one below. After a move the selection **stays with the
  moved item**, so repeated taps keep moving it in the same direction.
- **No reorder buttons inside a contact line.** A line only carries Edit +
  Remove (when those actions are available for that contact).
- Preferred is expressed by order (move-to-top = preferred), exactly as the
  current Full view already does — no separate "make preferred" control.
- **Mark-verified** is not rendered here (page-level Live only).

**Inner switch state.** `GuardianSection` owns a tiny state machine inside
the Edit view:

```
_contactEditTarget: null          → list (default)
                    { IsAdd }     → blank add form
                    { Contact }    → single-contact edit form
```

Only one inner surface renders at a time. Clicking a row's **Edit** sets
`_contactEditTarget = Edit(c)` and copies `c` into `_contactEditModel`
(a `ContactFormFieldsModel`). Clicking **Add contact** sets
`_contactEditTarget = Add` and a fresh blank `_contactEditModel`. Cancel /
Save return to the list and re-sync the working list.

**`ContactList` (the symbol in the sketch).** This is a single read-only view
over the working set, not a third list:
- **Draft guardian** (`ExistingGuardianId is null`): the existing Buffered
  `_editContacts` (already owned by `GuardianSection`).
- **Existing guardian** (`ExistingGuardianId is { } gid`): a Live list loaded
  by `GuardianSection` via the new `[Inject] IContactsClient` —
  `IContactsClient.ListContactsAsync(Guardian, gid)` — the same source the
  nested Live `ContactsEditor` used. Loaded once when the Edit view initializes
  for that guardian; refreshed after each Live mutation.

**Per-contact action availability (draft vs. existing).** The compact manager
is primarily the **inline Buffered (draft) editor** the user described. To keep
the drawer free of nested audited reason-modals (and avoid adding
`IDialogService` to `GuardianSection`), the action set is scoped per case:

| Action | Draft (Buffered) | Existing (Live) |
|--------|------------------|-----------------|
| Add (anchor) | inline blank form | deferred to page (no anchor) |
| Edit (icon) | inline `ContactFormFields` | deferred to page (no icon) |
| Remove (icon) | inline (in-memory) | deferred to page (no icon) |
| Reorder (select + ↑↓) | inline (in-memory swap) | inline (Live reorder API) |
| Mark-verified | n/a (drafts) | deferred to page |

**Behavior change (flagged):** today an existing guardian's contacts can be
**removed** and **marked verified** inline in the drawer (the nested Live
`ContactsEditor` with `EditDisabled` leaves Remove + verify enabled). This
spec defers existing-guardian **remove** and **verify** to the guardian detail
page, keeping only **reorder** inline (the user's explicit "preserve the
reorder feature" ask). This is a deliberate, visible scope reduction — not a
silent regression — so the drawer stays free of nested reason modals and
`GuardianSection` needs only `IContactsClient` (not `IDialogService`).
This is a locked decision (§7). If existing-guardian inline remove must be
revisited later, that would re-add the nested `ContactChangeDialog` and
`IDialogService` (the nested-surface tension this spec deliberately avoids).

**`EditDisabled`.** The `EditDisabled` gate that previously suppressed the
nested editor's per-row Edit + Add is no longer needed (the inner toggle
replaces the trigger it was disabling). Keep `EditDisabled` for the student
EditDialog Readonly summaries; drop it from the guardian drawer.

**Channel-change re-publish.** The focused `ContactFormFields` sub-panel is a
real child of `GuardianSection` (rendered inline, not published-up as a
`RenderFragment`), so it re-renders with `_contactEditModel`/`_contactEditTarget`
naturally — the earlier published-fragment freeze bug (a frozen
`RenderFragment` only re-executing on host re-render) does not apply. The
channel-change side-effect (loading country codes / clearing a stale code)
stays in `GuardianSection` via the `ChannelChanged` callback, exactly as
`ContactsEditor` does today.

### 4.3 Feature C — "Add contact" uses a FluentAnchor (same switch)

The add affordance is the same `FluentAnchor` ("Add contact") that toggles
`_contactEditTarget` to `Add`. Rationale for anchor over button: space is
limited in the drawer and it matches the student-dialog contact summary
pattern; the anchor reads as a secondary action below the compact list.
Clicking it opens the blank `ContactFormFields` sub-panel in the same inner
div — identical to the Edit switch, so the operator only has one mental model
("tap to open a contact editor here"). Shown only for draft guardians (the
inline add case); existing-guardian add is deferred to the page.

### 4.4 Feature D — Guardian identity in the drawer

1. **Drawer header (title).** `StudentEditDialog.GetDrawerTitle()` for the
   Guardians branch resolves the display name from
   `_model.GuardianLinks[_editingGuardianIndex]`:
   - Add: `"Add guardian"` (no identity to show).
   - Edit: `"Edit · {FirstName} {LastName}"` (fall back to `"Edit guardian"`
     if the name is blank).
   So the header reads **"Edit · Jane Smith"**.
2. **Editor body header.** `GuardianSection` Edit view already has the
   `editedGuardian` in scope; render a small identity header line above the
   fields (e.g. `.guardian-edit-identity` with the name + relationship) so
   the name is visible even if the drawer header is truncated. This is cheap
   and self-contained in `GuardianSection`.
3. Reuse the existing name-building helper already used by the guardian cards
   (`salutation + First + Last`, relationship optional) so the spelling is
   consistent.

## 5. Files

Status markers: ✅ done (uncommitted on the feature branch).

**New:**
- `docs/plans/2026-08-18-dialog-min-height-and-guardian-contact-compact.md`
  (this file). ✅

**Modified:**
- `src/SchoolCollab.Admin.Shared/Components/Dialogs/DialogServiceExtensions.cs`
  — the `height` parameter on `BuildShellParameters` / `ShowReadonlyDialogAsync`
  (forwarded to `DialogParameters.Height`) **stays** in the helper (harmless,
  may serve other callers) but `StudentEditDialog`'s open call no longer passes
  it (§4.1 revision). ✅ (helper done; `StudentEditDialog` call-site done — see
  `Detail.razor` below)
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor`
  — **dropped** the `height: "max(72vh, 480px)"` argument on the
  `StudentEditDialog` open call (§4.1 revision). The dialog box is no longer
  pinned to a fixed `--dialog-height`; the host wrapper caps the body instead. ✅
- `src/Students/SchoolCollab.Students.Application/Components/Students/StudentEditDialog.razor`
  — resolve the guardian name in `GetDrawerTitle()` via
  `_model.GuardianLinks[_editingGuardianIndex]` (`BuildGuardianTitle` →
  `"Edit · {First} {Last}"`, fallback `"Edit guardian"`; Add → `"Add guardian"`).
  **No** `--drawer-open` class toggle; **no** `DialogDrawer` change. ✅ (name
  omits salutation — see §7.4 / §10.3)
- `src/Students/SchoolCollab.Students.Application/Components/Students/StudentEditDialog.razor.css`
  — **MODIFIED** (was "unchanged" under the explicit-height path). The root
  wrapper `.student-edit-dialog-root` becomes the height authority:
  `position: relative; display: flex; flex-direction: column; max-height: 72vh;
  min-height: 320px; overflow: hidden` (§4.1 step 2). The form region inside
  the root gets `flex: 1 1 auto; min-height: 0; overflow: auto` so a tall form
  scrolls within the capped body; the actions row stays pinned by FluentUI's
  grid. ✅
- `src/Students/SchoolCollab.Students.Application/Components/Students/GuardianSection.razor`
  (+ `.razor.css`) — replace the nested `ContactsEditor` in the Edit view with
  the compact manager: selectable single-line list + outside reorder toolbar
  + inner `_contactEditTarget`/`_contactEditModel` switch + `ContactFormFields`
  sub-panel; add the `guardian-edit-identity` header; add
  `[Inject] IContactsClient` and load/refresh the Live list for existing
  guardians; implement Live reorder against `IContactsClient` (reusing the
  exact reorder contract `ContactsEditor.MoveUpAsync`/`MoveDownAsync` use —
  locked decision, §7). Reuse the existing `ChannelGlyph` / `FormatContactValue` helpers.
  Existing-guardian add/edit/remove/verify are **not** rendered (deferred to the
  page — §4.2).
  `.razor` markup + `@code` logic: ✅. `.razor.css` styles for the new
  `.guardian-contact-manager` / `.guardian-contact-line` (+`--selected`) /
  `.guardian-contact-reorder-bar` / `.guardian-contact-add-anchor` /
  `.guardian-edit-identity` (+ `-name`/`-rel`) / `.guardian-contacts-empty`
  classes: ✅ (closed gap §7.3 / §10.2). The body header now also folds
  the salutation via `ResolveSalutation` (closed gap §7.4 / §10.2 — body
  header only; drawer title still omits salutation by design).
- `src/SchoolCollab.Admin.Shared/Components/DialogDrawer.razor` — **no change.** ✅

**Tests:**
- `StudentFormFieldsSectionEditTests.cs::GuardianSection_EditView_NestedContactsEditorIsDisabled`
  — rewritten to assert the new contract (no nested `<ContactsEditor>` in the
  Edit view; both branches route through `RenderCompactContactManager`; Draft
  with `showAddAnchor: true`, LiveReadOnly with `showAddAnchor: false`). ✅
- `StudentEditDialog_GetDrawerTitle_IncludesGuardianNameAndFallsBack` —
  asserts `BuildGuardianTitle` emits `"Edit · {name}"` and falls back to
  `"Edit guardian"`; `GetDrawerTitle` routes the Guardians branch through it;
  the four title strings (`Add/Edit contact`, `Add/Edit guardian`) are present. ✅
- `DialogServiceExtensions_BuildShellParameters_ForwardsHeightToDialogParameters`
  — asserts `BuildShellParameters` accepts `string? height = null` and sets
  `Height = height` on `DialogParameters`; `ShowReadonlyDialogAsync` accepts
  the same argument and threads it through to `BuildShellParameters(title, size, height)`;
  the XML doc names `--dialog-height`. ✅ (the helper keeps the param; `StudentEditDialog`
  just no longer uses it)
- `GradeLevelsDetail_OpensStudentEditDialog_WithoutPinnedHeight` — **replaces**
  the earlier `..._AtTallerExplicitHeight` test. Asserts the `StudentEditDialog`
  open call does **NOT** pass `height:` (the body cap lives in CSS now, §4.1) and
  that the call still uses `DialogSize.ExtraLarge` + the `StudentIdKey` parameter.
  Also asserts **no** `ShowReadonlyDialogAsync<...>` call on the page passes a
  `height:` argument (the box is never pinned). ✅
- `StudentEditDialog_RootWrapper_IsContentFillFlexColumn` — asserts
  `StudentEditDialog.razor.css` gives `.student-edit-dialog-root`
  `max-height: 72vh`, `min-height: 320px`, `display: flex`, `flex-direction: column`,
  `overflow: hidden`, and `position: relative` (the drawer's containing block).
  Also asserts the OLD `height: 100%` rule is gone (it was the root-cause of the
  overshoot). ✅
- `GuardianSection_CompactContactManager_RendersSingleLineSelectableList` —
  asserts the list, `<li>`, `--selected`, glyph/value/label, `aria-selected`,
  and defensively asserts no `MoveContactUpAsync` / `MoveContactDownAsync` /
  `ChevronUp` / `ChevronDown` *inside* the `<li>` block. ✅
- `GuardianSection_CompactContactManager_OutsideReorderToolbar` — asserts the
  toolbar's class, the two `ChevronUp`/`ChevronDown` icon buttons, the
  disabled-state conditions (`IsContactFirst` / `IsContactLast`), and that
  the toolbar appears *after* the list's `</ul>` (visually outside). ✅
- `GuardianSection_CompactContactManager_EditAndAddAnchorsFlipInnerSwitch` —
  asserts the inner switch renders `<ContactFormFields Model="_contactEditModel">`
  when `_contactEditTarget` is set, that **no `<DialogDrawer>` is opened
  inside the Edit view** (comment-stripped), that the Edit/Add/Cancel/Commit
  handlers fire, that the Add anchor uses `Appearance.Hypertext` and the
  `guardian-contact-add-anchor` class, and that opening the Edit switch
  pins the selection to that row's key. ✅
- `GuardianSection_CompactContactManager_IdentityHeader` — asserts the
  `.guardian-edit-identity` header (name + relationship spans), the
  `editedGuardianDisplayName` binding, both `ResolveSalutation` calls in the
  getter (draft + existing), and the three CSS rules. ✅
- `GuardianSection_CompactContactManager_CssStylesCoverNewClasses` — asserts
  one rule per new class (compact manager, list, line + `--selected`, glyph,
  value, label, actions, reorder bar, add anchor, empty state). ✅

## 6. Acceptance criteria

Code-complete items are checked `[x]` (logic lands on the feature branch); items
needing CSS, browser confirmation, or tests remain `[ ]`. See §10 for the gap
breakdown.

- [x] `StudentEditDialog`'s root wrapper is a content-fill flex column
      (`max-height: 72vh; min-height: 320px; overflow: hidden`) so the dialog
      grows to its content up to `72vh` and the drawer is clamped to the body
      (no overshoot). The earlier fixed `DialogParameters.Height` is no longer
      passed. *(CSS + call-site revert + tests ✅; browser-confirm §7.2)*
- [ ] When a drawer is open, the drawer body fills the dialog body (usable
      vertical area) and **does not overshoot the dialog's bottom**. *(the core
      bug this revision fixes; code/tests ✅, browser-confirm §7.2 still valuable)*
- [ ] If a width adjustment is needed, the drawer still leaves the main form
      legible (no overflow / overlap). *(browser-confirm §7.1)*
- [x] In `GuardianSection` Edit view, each contact renders on a single line
      (glyph + value + optional label + Edit icon + Remove icon, where
      available). No reorder buttons exist inside a contact line. *(markup + CSS + tests ✅)*
- [x] Clicking a contact line selects it (highlighted); the outside ↑/↓
      toolbar acts on the selected contact and is disabled when nothing is
      selected or the selected is first/last; the selection follows the moved
      item across repeated taps. *(logic + CSS + tests ✅)*
- [x] Reorder works for draft guardians (in-memory swap + `ContactsChanged`)
      and for existing guardians (Live reorder API via `IContactsClient`). *(logic + tests ✅)*
- [x] Clicking a contact's Edit icon switches the inner div to a
      `ContactFormFields` form pre-loaded with that contact; Save commits and
      returns to the list; Cancel discards and returns. *(logic + tests ✅)*
- [x] Clicking the **"Add contact"** FluentAnchor switches the same inner div
      to a blank add form; Add commits to the draft guardian's contact list
      and returns to the list. *(logic + tests ✅)*
- [x] The compact manager renders the full inline set (add/edit/remove/
      reorder) for **draft** guardians; for **existing** guardians it renders
      the list + reorder only (add/edit/remove/verify deferred to the page).
- [x] The drawer header for an existing guardian reads "Edit · {name}"; the
      guardian body shows a visible identity header with the name. *(body header now includes salutation; drawer title omits salutation by design — §7.4 / §10.3)*
- [x] No second `DialogDrawer` is added; the inner switch is a `<div>` toggle.
- [x] `StudentEditDialog` Save/Cancel remain disabled while the drawer is
      open; the drawer Cancel/backdrop/Escape/× all close and discard the
      working copy without closing the parent `FluentDialog`. *(pre-existing behavior, unchanged)*
- [x] All relevant unit tests pass; new tests above pass. *(Admin + Students
      unit tests ✅; 4 unrelated failures in full `dotnet test` run — 1
      pre-existing ArchitectureTests substring match, 3 Settings integration
      environment/timing; browser-confirm §7.1/§7.2 still valuable)*

## 7. Open questions

**Resolved decisions (locked in):**

- **Dialog resize lever → content-fill host wrapper (revised).** The earlier
  "explicit-height open path" (pass `height: "max(72vh, 480px)"` to
  `DialogParameters.Height`) was **replaced** after an in-browser check showed
  the drawer overshooting. Root cause: `.fluent-dialog-body` is `height: auto`,
  so `height: 100%` on the host wrapper resolved to `auto` and the absolute
  drawer had no containing-block height. The revised locked decision: make the
  host wrapper (`.student-edit-dialog-root`) the height authority —
  `max-height: 72vh; min-height: 320px; display: flex; flex-direction: column;
  overflow: hidden` — and drop the `height` argument on `StudentEditDialog`'s
  open call. The `height` parameter stays on `DialogServiceExtensions` (may
  serve other callers). The per-toggle CSS (`:has()`/`::part()`) approach
  remains rejected (§4.1).
- **Existing-guardian inline remove → deferred to the page.** The drawer
  stays free of nested reason modals; `GuardianSection` does not gain
  `IDialogService`. Existing-guardian add/edit/remove/verify run on the
  guardian detail page (§4.2 table).
- **Live reorder API shape → reuse `ContactsEditor`'s contract.**
  `GuardianSection` calls the same reorder/move endpoint
  `ContactsEditor.MoveUpAsync` / `MoveDownAsync` use (swap + persist); no new
  contract.

**Still open:**

1. **Width policy at ExtraLarge with a 420px drawer.** Does the drawer width
   leave the main form legible, or is a width reduction needed? Decide after a
   browser check now that the body is content-filled (not fixed-height).
2. **Browser-confirm the content-fill fix.** Code/tests are complete; confirm in
   the browser that (a) the dialog grows to its content up to `72vh`, (b) a
   tall form scrolls inside the body (not the dialog box), (c) the bottom
   actions stay pinned, and (d) the drawer no longer overshoots the dialog's
   bottom.
3. ~~Compact-manager CSS (implementation gap).~~ **Closed.** The new classes
   (`.guardian-contact-manager`, `.guardian-contact-single-lines`,
   `.guardian-contact-line` + `--selected`, `.guardian-contact-glyph`/
   `-value`/`-label`/`-actions`, `.guardian-contact-reorder-bar`,
   `.guardian-contact-add-anchor`, `.guardian-edit-identity` + `-name`/`-rel`,
   `.guardian-contacts-empty`) are now styled in `GuardianSection.razor.css`
   (§5). Accent-tinted `--selected` highlight, hover/focus-visible affordances,
   flex-row layout with `text-overflow: ellipsis` on the value, and an
   identity header with a divider and muted relationship span.
4. ~~Salutation in the guardian identity (§4.4 item 3).~~ **Closed for the body
   header only.** `editedGuardianDisplayName` now folds the salutation using
   the existing `ResolveSalutation` lookup — matching the guardian cards'
   `salutation + First + Last` spelling. The drawer title (in
   `StudentEditDialog.BuildGuardianTitle`) still omits salutation by design:
   `StudentEditDialog` has no salutation lookup loaded, and adding one is out
   of scope (judgment call recorded in §10.3).
5. ~~Test coverage (implementation gap).~~ **Closed.** New tests in
   `StudentFormFieldsSectionEditTests.cs`:
   - `StudentEditDialog_GetDrawerTitle_IncludesGuardianNameAndFallsBack`
   - `DialogServiceExtensions_BuildShellParameters_ForwardsHeightToDialogParameters`
   - `GradeLevelsDetail_OpensStudentEditDialog_AtTallerExplicitHeight`
   - `GuardianSection_CompactContactManager_RendersSingleLineSelectableList`
   - `GuardianSection_CompactContactManager_OutsideReorderToolbar`
   - `GuardianSection_CompactContactManager_EditAndAddAnchorsFlipInnerSwitch`
   - `GuardianSection_CompactContactManager_IdentityHeader`
   - `GuardianSection_CompactContactManager_CssStylesCoverNewClasses`
   Total Admin unit tests: 16 (was 11). 7/8 test projects pass; the one
   failure (`ArchitectureTests::StudentEditDialog_Saves_Atomically_Not_Live`)
   is pre-existing and unrelated.

## 8. Risks

- **No wasted space when no drawer is open (content-fill benefit).** The
  content-fill wrapper sizes the dialog to its content (capped at `72vh`), so
  the bare profile form sits in a box exactly its own height — there is no
  "fixed-tall box" trade. The earlier explicit-height path had that trade;
  the revision removes it.
- **`max-height` on the wrapper, not `--dialog-height` on the box.** The fix
  depends on `.student-edit-dialog-root { max-height: 72vh; min-height: 320px }`
  giving the wrapper a definite height so the absolute drawer is clamped. If a
  future FluentUI version changes `.fluent-dialog-body` from `height: auto` to
  a definite height, the wrapper's `max-height` still caps it correctly (it is
  a max, not a fixed height). The `height: 100%` rule that caused the overshoot
  is removed (§4.1).
- **Form-scroll vs. drawer-scroll.** Both the form region (`flex: 1 1 auto;
  overflow: auto`) and the drawer body (`flex: 1 1 auto; overflow: auto`)
  scroll independently inside the capped body. Confirm in the browser that a
  tall form scrolls without pushing the actions row off-screen (§7.2).
- **Live reorder in `GuardianSection`.** Moving existing-guardian reorder into
  `GuardianSection` adds a Live API call path and an `IContactsClient`
  dependency to a component that was previously Buffered-only for contacts.
  Reuse the exact reorder contract `ContactsEditor` uses to avoid drift.
- **Accessibility.** The selectable list must be keyboard-reachable: the line
  is a `<li>` with a click handler; add `tabindex="0"` + `aria-selected` and
  handle Enter/Space to select, and arrow keys to move (or keep the outside
  ↑/↓ buttons as the reorder affordance, which are already focusable). The
  drawer stays `role="region"`; the identity header is the section heading. No
  new nested modal.
- **Scope creep.** Keep page-level Live `ContactsEditor` (the guardian detail
  page) untouched; only the guardian drawer presentation changes. Existing
  `ContactChangeDialog` Live mode is unchanged.

## 9. Out of scope

- Changing the shared `SideDrawer` (AI chat).
- `StudentCreateDialog`, the page-side `/students/{id}/edit` Linked mode.
- The page-level Live `ContactsEditor` / `ContactChangeDialog` (guardian
  detail page owns audited add/edit/remove/verify for existing guardians).
- **Mark-verified** in the drawer (page-level Live only).
- Mobile-first responsive redesign of the dialog/drawer (desktop-first today).
- Any change to the contact add/edit **fields** (`ContactFormFields` is
  reused, not changed).
- A nested `DialogDrawer` (drawer-in-drawer stays out). The existing
  `ContactChangeDialog` reason modal is not used inside the guardian drawer in
  this spec (existing-guardian add/edit/remove are deferred to the page).

## 10. Implementation progress & review

Branch: `feature/contact-guardian-form-fields-consolidation` (uncommitted on top
of `6fb5008`). Build green; 7/8 test projects pass (the one failure,
`ArchitectureTests.Unit::StudentEditDialog_Saves_Atomically_Not_Live`, is
pre-existing and unrelated — substring `AddContactAsync` matches the dialog's
local `OpenAddContactAsync` handler).

### 10.1 Conformance (logic)

| Spec | Requirement | Status |
|------|-------------|--------|
| §4.1 | `BuildShellParameters`/`ShowReadonlyDialogAsync` keep the `height` parameter (harmless; other callers may use it) | ✅ |
| §4.1 | `Detail.razor` **drops** `height: "max(72vh, 480px)"` on `StudentEditDialog` (no pinned box) | ✅ |
| §4.1 | `.student-edit-dialog-root` → content-fill flex column (`max-height: 72vh; min-height: 320px; overflow: hidden`); `height: 100%` removed | ✅ |
| §4.1 | No per-toggle class / no `DialogDrawer` change | ✅ |
| §4.2 | Compact manager replaces nested `ContactsEditor` in both Edit branches (`RenderCompactContactManager`) | ✅ |
| §4.2 | Single-line glyph+value+label+Edit/Remove; no in-row reorder buttons | ✅ |
| §4.2 | Selectable line (`--selected` + `_selectedContactKey`); outside ↑/↓ disabled at ends | ✅ |
| §4.2 | Move up = higher priority (lower `Order`); selection follows moved item; restamp `Order` | ✅ |
| §4.2 | Inner switch `_contactEditTarget`/`_contactEditModel` + `ContactFormFields` sub-panel | ✅ |
| §4.2 | Draft = full inline; Existing = list+reorder only (`ContactManagerMode.Draft`/`LiveReadOnly`) | ✅ |
| §4.2 | `[Inject] IContactsClient`; `ListContactsAsync` load; `ReorderContactsAsync` (same contract as `ContactsEditor`) | ✅ |
| §4.2 | Reuse `ChannelGlyph`/`FormatContactValue`; channel-change side-effect in host | ✅ |
| §4.3 | "Add contact" `FluentAnchor` `Appearance.Hypertext`; drafts only | ✅ |
| §4.4 | `GetDrawerTitle()` → `"Edit · {First} {Last}"` (fallback `"Edit guardian"`); Add → `"Add guardian"` | ✅ (no salutation) |
| §4.4 | `.guardian-edit-identity` body header (name + relationship, salutation folded in) | ✅ |
| §7   | Locked decisions honored (content-fill wrapper; existing remove deferred; reuse reorder contract) | ✅ |

### 10.2 Gaps → all closed

1. ~~Compact-manager CSS~~ — **closed.** New rules in `GuardianSection.razor.css`
   cover the manager container, single-line list, selectable + `--selected`
   highlight, hover/focus-visible affordances, glyph/value/label/actions
   layout, outside reorder toolbar, add anchor, identity header (name +
   relationship), and empty state.
2. ~~Salutation in identity (§4.4 item 3)~~ — **partially closed.** Body
   header (`editedGuardianDisplayName`) now folds the salutation via the
   existing `ResolveSalutation` lookup — matching the guardian cards'
   `salutation + First + Last` spelling. Drawer title
   (`StudentEditDialog.BuildGuardianTitle`) still omits salutation by design
   (no salutation lookup loaded in `StudentEditDialog`; judgment call).
3. ~~Test coverage~~ — **closed.** Ten source-level tests in `StudentFormFieldsSectionEditTests.cs`
   cover `GetDrawerTitle` name/fallback, `BuildShellParameters` height
   forwarding, `Detail.razor` height call-site (now asserts NO pinned height),
   the content-fill CSS root-wrapper test, and the compact-manager behavior
   suite (single-line selectable list, outside reorder toolbar, Edit/Add inner
   switch, identity header, CSS coverage). Total tests in this file: 17 (was 11).
   Relevant unit-test projects (Admin, Students) pass. The unrelated failures
   (`ArchitectureTests.Unit::StudentEditDialog_Saves_Atomically_Not_Live` and
   three Settings integration tests) are pre-existing/out of scope.
4. ~~Revert the explicit-height call-site + add the content-fill CSS~~ —
   **closed.** `Detail.razor` no longer passes `height:`; `StudentEditDialog.razor.css`
   now uses the content-fill flex-column root wrapper and scrollable form
   content stack; the height test was flipped to
   `GradeLevelsDetail_OpensStudentEditDialog_WithoutPinnedHeight` and the new
   `StudentEditDialog_RootWrapper_IsContentFillFlexColumn` CSS test was added.

### 10.3 Recommendation → followed

- **Did:** completed the §4.1 revision work: removed the `height:` argument from
  the `StudentEditDialog` open call in `Detail.razor`; rewrote
  `StudentEditDialog.razor.css` with the content-fill flex-column root wrapper,
  scrollable form content stack, and pinned action row; flipped the height
  call-site test and added the CSS root-wrapper test.
- **Build/test status:** full solution builds (0 errors, 25 warnings); relevant
  unit-test projects pass — Admin (413), Students (221). The full `dotnet test`
  run shows 4 unrelated failures: the pre-existing
  `ArchitectureTests.Unit::StudentEditDialog_Saves_Atomically_Not_Live` (loose
  substring match) and 3 Settings integration tests (environment/timing, out of
  scope for this change).
- **Still need browser-confirm before close:** §7.1 (width policy) and §7.2
  (that the dialog grows to content up to `72vh`, tall forms scroll inside the
  body, actions stay pinned, and the drawer no longer overshoots). These are
  the only items that cannot be settled by code/tests alone.
- **Deferred (judgment call):** drawer title salutation — `StudentEditDialog`
  has no salutation lookup loaded, and adding one is out of scope.
- **Unrelated, noted:** `ArchitectureTests.Unit::StudentEditDialog_Saves_Atomically_Not_Live`
  fails on a loose substring (`AddContactAsync` ⊂ `OpenAddContactAsync`). Not
  introduced here; tighten the match separately.

---

## 11. Post-implementation UI refinements

After the content-fill work landed, three additional UX items were addressed:

### 11.1 Clear-cut drawer edge

`DialogDrawer.razor.css` now gives the panel a stronger cast shadow and an
inside-facing border so the drawer is visually separated from the dimmed main
form:

- `.dialog-drawer-panel--right`: `box-shadow: -12px 0 32px rgba(0, 0, 0, 0.22)`
  and `border-left: 1px solid var(--neutral-stroke-divider-rest, #e0e0e0)`.
- `.dialog-drawer-panel--left`: `box-shadow: 12px 0 32px rgba(0, 0, 0, 0.22)`
  and `border-right: 1px solid var(--neutral-stroke-divider-rest, #e0e0e0)`.

A new source-level test `DialogDrawer_Css_PanelHasClearCutShadowAndBorder`
asserts both variants carry a `box-shadow` and the corresponding border rule.

### 11.2 Guardian identity header outside the field region

In `GuardianSection.razor`'s `GuardianView.Edit` branches, the
`.guardian-edit-identity` block (name + optional relationship) was moved
**above** `.guardian-edit-form` so it sits in the white drawer-body area,
before the gray field container (`background: var(--neutral-layer-2, #fafafa)`).
The CSS for the header was updated to remove the bottom border, add a small
bottom margin, and slightly increase the name font size. A source-assertion in
`GuardianSection_CompactContactManager_IdentityHeader` now confirms the
identity block opens before the `.guardian-edit-form` container.

### 11.3 No inline Cancel in the drawer Edit view

The `GuardianSection.razor` `GuardianView.Edit` branches previously rendered a
`Cancel` + `Save` action row. The Cancel button duplicated the drawer's own
Close/Cancel affordance (`DialogDrawer` `ShowCancel="true"`, `CancelText="Close"`,
× button, backdrop click, Escape). Following the existing `ContactsEditor`
Edit-view contract, the inline `Cancel` was removed; only the inline `Save`
remains. The unused `CancelEditFormAsync` helper was removed. A new
source-level test `GuardianSection_EditView_DropsInlineCancel` asserts the
absence of an inline Cancel and the continued presence of the inline Save
buttons.

### 11.4 Repo pattern documentation

The dialog side-drawer pattern was documented as a reusable repo convention:

- **Pattern doc:** `docs/patterns/dialog-side-drawer.md` — covers when to use
  the drawer, the host-wrapper contract, the action contract (drawer owns
  Close/Cancel; body component owns Save/Add), a minimal example, and do's/
  don'ts.
- **Skill:** `project:School-Collab:dialog-side-drawer` — a project-scoped
  skill for discovering and applying the pattern.

### 11.5 Verification after refinements

- `dotnet build` of the affected projects succeeds.
- `SchoolCollab.Admin.Tests.Unit` passes (413 tests) with the new
  `DialogDrawer_Css_PanelHasClearCutShadowAndBorder` and
  `GuardianSection_EditView_DropsInlineCancel` tests.
- Full `dotnet test` run still shows the same 4 unrelated failures noted in
  §10.3.
- Browser confirmation still needed for §7.1/§7.2.