# Plan: Guardian edit drawer refinements

**Date:** 2026-08-19
**Branch (target):** `feature/contact-guardian-form-fields-consolidation` (follow-up to `docs/plans/2026-08-18-dialog-min-height-and-guardian-contact-compact.md`)
**Status:** Partial implementation — R1 (identity spacing), R2 (dynamic
relationship binding), R3 (Delete inline / Edit in toolbar), R4 (Add contact
for all guardians), and R6 (role as CC checkbox) are **implemented** and
tested. R5 (contact sub-screens hiding relationship+role) and the D1–D4
sub-screen / inline-reason / drawer-title wiring remain **spec only**
(see §9/§10).

This spec refines the `GuardianSection` **Edit view** hosted in the shared
`DialogDrawer` inside `StudentEditDialog`. It captures five requested UX
changes and answers one question about dynamic title behavior.

---

## 1. Current state (grounded in code)

The drawer Edit view (`GuardianSection.razor`, `GuardianView.Edit`) renders:

```
.guardian-edit-identity          ── identity header (name + optional relationship)
.guardian-edit-form              ── gray field region (background neutral-layer-2)
  <GuardianEditFields>           ── Relationship/Role (+ Title/name for drafts)
  .guardian-edit-contacts        ── compact contact manager
    list surface | add/edit sub-panel
  .guardian-edit-actions         ── inline Save only
```

**Compact contact manager** (`RenderCompactContactManager`, `_contactEditTarget`):

- **List surface** (`_contactEditTarget == null`):
  - Single-line selectable `<li>` rows: `glyph | value | label | actions`.
  - Draft mode: each row shows **inline Edit ✎ + Delete 🗑**.
  - Live (`LiveReadOnly`) mode: **no inline icons** (read-only).
  - Outside reorder toolbar: **↑ / ↓** only.
  - "Add contact" anchor: **Draft only** (`showAddAnchor: true`); Live passes
    `showAddAnchor: false` → **no add affordance for existing guardians**.
- **Sub-panel** (`_contactEditTarget != null`): inline `<ContactFormFields>` +
  Cancel/Commit, replacing the list. The `GuardianEditFields` (relationship +
  role) remain visible **above** it.

**Contact modes** (`ContactManagerMode`):
- `Draft` → buffered `_editContacts`; full add/edit/remove/reorder.
- `LiveReadOnly` → `_liveContacts` loaded via `IContactsClient`; reorder only.

**Identity header relationship binding:** the edit-existing branch renders
`ResolveRelName(editedGuardian.RelationshipCodedValueId)`, where
`editedGuardian` is the **original** assignment (`GuardianLinks[_editingIndex]`),
**not** the working copy `_editModel`. See §3 for why that matters.

**API contract** (`IContactsClient`):
- `AddContactAsync(req)` — **no reason** required.
- `UpdateContactAsync(id, req)` — `req.Reason` **required**.
- `DeleteContactAsync(id, reason)` — **reason required**.

---

## 2. Requirements

| # | Requirement | Type |
|---|-------------|------|
| R1 | Tighten the top guardian identity label — too much top-to-bottom white space. | Layout |
| R2 | Relationship title should reflect the selected relationship; answer: does it update dynamically today? | Dynamic binding |
| R3 | Contact selection enables **remove** and **edit**. Keep the **Delete icon on the contact row**; move the **Edit button out to the toolbar** with the reorder ↑/↓ buttons. | Contact manager layout/actions |
| R4 | Provide a button to **add a new contact** to a guardian (currently absent for existing guardians). | Contact manager |
| R5 | When **adding / editing / removing a guardian's contact**, switch the drawer to a dedicated **contact sub-screen** that **hides the relationship + role form fields** to recover vertical space (room for the contact form and any inline reason). | Drawer body-mode |
| R6 | Guardian **role** has exactly two states — **Primary** / **CC** — rendered as a **checkbox**, not a dropdown. | Role field |

---

## 3. Answer to R2 — does the relationship update the title dynamically?

**No, not today.**

The edit-existing identity header renders:

```razor
@if (ResolveRelName(editedGuardian.RelationshipCodedValueId) is { } relName)
{
    <span class="guardian-edit-identity-rel">(@relName)</span>
}
```

`ResolveRelName(...)` is called with the **original** assignment's
`RelationshipCodedValueId` (`editedGuardian = GuardianLinks[_editingIndex]`),
not the working copy `_editModel.RelationshipCodedValueId` that the
`CodedValueDropdown` two-way-binds to. So changing the dropdown does **not**
re-render the header relationship.

### Design (R2 resolution)
Bind the identity header to the **working copy** so it updates live:

- Edit-existing header: use `ResolveRelName(_editModel.RelationshipCodedValueId)`.
- Add (IsAdd) header: when a relationship is picked, show it in the header too
  (mirrors the name live-typing that already exists).
- The relationship name lookup already caches into `_relNames` and has an
  `EnsureRelNameAsync(relId)` loader. Add a **parameterless** `RelationshipChanged`
  `EventCallback` on `GuardianEditFields` (mirrors the existing
  `ContactFormFields.ChannelChanged`, wired via `@bind-SelectedId:after="..."`)
  so the host can call `EnsureRelNameAsync(_editModel.RelationshipCodedValueId)`
  + `StateHasChanged` when the dropdown changes — the host reads the new id from
  the two-way-bound model, same pattern already used for contact channel.
- Note: this only needs the **relationship name**, not the full assignment. Keep
  the name derived from `_editModel` for both the live-typing **and** the
  selected relationship.

**Open (minor):** the header currently omits the relationship when it is not yet
loaded in the cache. On a freshly-picked relationship the lookup may be missing
until `EnsureRelNameAsync` resolves — accept the brief blank (same
behavior as the card list today).

---

## 4. R1 — Tighten the identity header vertical space

Current `.guardian-edit-identity`:

```
padding: 0.5rem 0.25rem;   /* top+bottom 0.5rem */
margin-bottom: 0.5rem;
gap: 0.5rem;
name font-size: 1rem; font-weight: 600
```

Combined with `.guardian-edit-form { gap: 0.75rem; padding: 0.75rem; }` this
reads as a tall banner.

**Design**
- Reduce the identity block's vertical footprint:
  - `padding: 0.15rem 0.25rem` (top/bottom 0.15rem).
  - `margin-bottom: 0.4rem`.
  - Keep `gap: 0.4rem`, keep the name `font-weight: 600` but cap `font-size` at
    `0.95rem` and `line-height: 1.2`.
- Reduce the first-field gap inside `.guardian-edit-form` (the relationship/role
  `FormRow` sits immediately under the identity; the `0.75rem` gap + identity
  margin is the bulk of the "too much top-bottom white space").
  - Target: `.guardian-edit-form { gap: 0.5rem; padding: 0.5rem 0.75rem; }`
- Acceptable visual result: the header reads as a **compact title line**, not a
  banner, and the field region starts sooner.

**Verification:** the identity block height is roughly halved; the relationship
row appears without a large empty band above it.

---

## 5. R3 — Contact row: Delete inline, Edit in toolbar

**Today (Draft):** each row has inline Edit ✎ **and** Delete 🗑.

**Target:** 

```
Row:  glyph | value | (label) | [🗑 Delete inline]
Toolbar (below list):  [✎ Edit] [↑] [↓]
```

- **Delete** stays **inline on the contact row** (each row keeps its own Delete).
- **Edit** moves **out of the row** into the toolbar, alongside ↑ / ↓.
- The toolbar **Edit** button is **disabled** unless a row is selected
  (`_selectedContactKey != null`) — same disabled-gating already used for ↑ / ↓.

**Mechanics**
- Remove the inline `Edit` `FluentButton` from each `<li>`; keep only the Delete
  button inline.
- Add an `Edit` `FluentButton` to `.guardian-contact-reorder-bar` (before the
  ↑ / ↓ buttons), `Disabled` when no row is selected, `OnClick`
  `StartContactEditAsync(selectedRow)`. The handler already exists but must
  additionally flip `GuardianBodyMode` to `EditContact` (§6.2) so the drawer
  switches to the contact sub-screen; look up the selected contact by
  `_selectedContactKey`. Likewise `StartContactAddAsync` flips to `AddContact`.
- Selection still drives which row Edit targets; Delete remains per-row
  (no selection needed).

**Applies to:** both `Draft` and `Live` modes (R4/R5 scope, §6).

---

## 6. R4 + R5 — Add contact for a guardian + dedicated add-contact screen

### 6.1 There must be an "Add contact" affordance for all guardians

- Show the "Add contact" anchor (`Appearance.Hypertext`) for **both** `Draft`
  and `LiveReadOnly` modes (currently `showAddAnchor: false` for Live). Make it
  unconditional (remove the `showAddAnchor` parameter, or default it `true`).
- The anchor already renders in the empty-contact branch today (it sits after
  the `contactList.Count == 0` check, not inside it), so no extra change is
  needed for the empty case beyond enabling it for Live.

### 6.2 Contact add/edit/delete switches the drawer body to a dedicated sub-screen

**Today:** `StartContactAddAsync` / `StartContactEditAsync` swap the list
surface for an inline `<ContactFormFields>` but the relationship + role rows
stay visible above it, which is cramped. Delete is a direct per-row action
(no sub-screen).

**Target** (`GuardianBodyMode` on `GuardianSection`):

```
GuardianBodyMode.Edit           (default) — identity + relationship/role + contacts list surface
GuardianBodyMode.AddContact     — dedicated sub-screen: only "Add contact" content
GuardianBodyMode.EditContact    — dedicated sub-screen: "Edit contact" / "Remove contact" content
```

- **Add** → `AddContact`: render **only** the sub-screen (compact heading "Add
  contact", `<ContactFormFields>`, Cancel + Add). Hide the
  `.guardian-edit-identity` block **and** the `GuardianEditFields` relationship
  + role rows.
- **Edit** (R3) → `EditContact`: hide the same top fields; for **existing**
  guardians the sub-screen also renders an **inline reason** field (D1, §7).
- **Delete** (R3) → same sub-screen in **remove-confirm** form: show the contact
  being removed + an **inline reason** field (**existing** guardians only) +
  Cancel + Confirm. For **draft** guardians, delete stays a direct buffered
  remove (no reason, no sub-screen).
  *(Open: whether draft delete should also confirm — default: no, keep direct.)*
- Returning (Cancel / committed add-or-edit-or-remove) switches back to `Edit`.

### 6.3 Drawer title follows the body mode

The drawer chrome title is owned by `StudentEditDialog.GetDrawerTitle()`
(which currently returns "Add guardian" / "Edit · {name}"). When the section
enters a contact sub-screen, the drawer title should read "Add contact" /
"Edit contact" instead.

- Add a host-facing callback from `GuardianSection` to `StudentEditDialog`
  (e.g. `OnContactSubScreenChanged(bool isAdd, bool active)`), or a small
  mode enum surfaced as `EventCallback<GuardianContactSubMode>`.
- `GetDrawerTitle()` returns the contact sub-screen title when the section
  reports a contact sub-screen, otherwise the guardian title.
- The sub-screen header inside the drawer body can echo the same title for
  clarity (drawer title bar is a single line; an in-body heading reads better).

---

### 6.4 (R6) Role as a two-state checkbox

`GuardianRole` already has exactly two values — `Primary = 0`, `CC = 1` — so a
binary control is a natural fit. Replace the `DropdownForEnum` role dropdown in
`GuardianEditFields` with a `FluentCheckbox`:

- **Checked** → `GuardianRole.CC`.
- **Unchecked** → `GuardianRole.Primary` (the default; the cards already
  highlight Primary with the accent-tinted `guardian-card--primary` style).
- Label it **"CC"** (helper: "carbon-copy / not the primary"). Because Primary
  is the default, the box is almost always unchecked, keeping the role row
  quiet.
- Keep it in the same "Relationship/Role" `FormRow` (vertical), below the
  relationship `CodedValueDropdown`, preserving the row width.

> Polarity is reversible (D5): the inverse — a checked-by-default "Primary"
> checkbox — is acceptable. Default in this spec is the CC checkbox.

---

## 7. Persistence & the reason/audit constraint (D1: reason stays inline)

**Decision (D1, confirmed):** reason is captured **inline** in the contact
sub-screen — **not** in a nested modal and **not** deferred to the page.
Edit/remove operate on the **guardian's contacts** and, for existing (Live)
guardians, require the inline reason.

`IContactsClient` requires a **reason** for `UpdateContactAsync` and
`DeleteContactAsync`, but **not** for `AddContactAsync`. The drawer pattern doc
(`docs/patterns/dialog-side-drawer.md`) forbids nested reason modals inside the
drawer — which is why add/edit/delete for **existing** guardians were previously
deferred to the page. The inline-reason sub-screen (§6.2) makes them safe to
offer in the drawer.

Reconciliation for **existing (Live) guardians**:

| Action | Reason needed? | Feasible in drawer? |
|--------|---------------|---------------------|
| Add | No | ✅ Inline — persist via `AddContactAsync` (or stage+buffer). |
| Edit (Update) | Yes | ✅ Inline reason field in the `EditContact` sub-screen (§6.2). |
| Delete | Yes | ✅ Inline reason field in the remove-confirm sub-screen (§6.2). |

- **Draft guardians** (`ExistingGuardianId == null`) keep the buffered path
  (no reason — contacts are staged into the student save; delete stays a direct
  per-row delete).
- **Existing (Live) guardians**: add = plain (no reason); edit/delete = inline
  reason required.

---

## 8. Drawer body-mode wiring (constraint)

The whole section currently renders a single scroll surface inside the drawer
body. Adding a sub-screen mode means the drawer body switches between:

1. `Edit` mode: identity + relationship/role + contact list + toolbar + add button
2. `AddContact` / `EditContact` mode: contact sub-screen (hide identity + relationship/role); `EditContact` / remove-confirm adds the inline **reason** field for Live guardians

This is a pure `GuardianSection` render branch (`@if/else` on the mode),
composed with the host title callback. No `DialogDrawer` change is required.
Keep the drawer `width: 420px` and rely on the existing vertical scroll; the
sub-screen removes relationship/role to give the contact form more room.

---

## 9. Open decisions / open questions (confirm before implementing)

- [x] **D1** Reason **inline** in the contact sub-screen — **confirmed** (§7).
- [x] **D2** Live guardian add: **buffered** (staged in `_editContacts`, written at student save). Matches draft behavior.
- [x] **D3** Drawer title in a contact sub-screen: **body-only** (not chrome). §6.3.
- [x] **D4** Chrome title: **name only, no salutation** (current). Body identity header: **salutation + name + relationship**.
- [ ] **D5** Role checkbox polarity: **"CC" checked / Primary unchecked** (default) vs the inverse ("Primary" checkbox) — §6.4.

---

## 10. Acceptance criteria

- [ ] The drawer edit view renders a **compact** top identity label (R1) —
  vertical padding/margin reduced, no oversized empty band.
- [ ] Changing the **Relationship** dropdown re-renders the identity header
  relationship text (R2), including on the add screen once a relationship is
  picked.
- [ ] Each contact row keeps its **inline Delete**; there is **no inline Edit**
  on the row (R3).
- [ ] The **toolbar** shows **Edit, ↑, ↓**; **Edit** is disabled until a row is
  selected and operates on the selected row (R3).
- [ ] **Add contact** is available for all guardians (R4), including when the
  list is empty.
- [ ] **Add / Edit / remove** contact switches the drawer body to a dedicated
  **"Add contact" / "Edit contact" / "Remove contact"** section that **hides**
  the relationship + role rows (R5).
- [ ] **Edit/remove for existing guardians shows an inline reason** field and
  blocks commit until it is filled (R3, D1); **add** needs no reason.
- [ ] The **role** field is a **checkbox** with exactly two states — Primary /
  CC — default **CC checked / Primary unchecked**, no longer a dropdown (R6, D5).
- [ ] Admin (413) + Students (221) unit suites pass; new source-assertion tests
  added for the drawer-mode switch, the toolbar Edit button, the absent
  relationship row in add-contact mode, and the live relationship binding.

---

## 11. Out of scope

- **Guardian "verify contact"** — not requested; remains on the guardian detail
  page.
- **Changing the `DialogDrawer` component itself** — the drawer chrome is reused
  as-is (body-mode switch is internal to `GuardianSection`).
- **Nested reason modals** in the drawer — still forbidden by the pattern doc;
  any reason capture must be inline.
- **Reordering behavior** — unchanged beyond moving Edit into the same toolbar.
