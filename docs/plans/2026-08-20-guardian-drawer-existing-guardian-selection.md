# Plan: Existing-guardian selection from the "New guardian" drawer (Request A)

**Date:** 2026-08-20
**Branch (target):** `feature/contact-guardian-form-fields-consolidation`
**Status:** **Implementation — confirmed open decisions (anchor = native Hypertext body-mode toggle, §3.1; post-link navigation = stay, §8; role capture = relationship + role/CC, §8) are coded (2026-08-20).**

## 1. Problem

The student edit dialog's shared drawer (`DialogDrawer` in `StudentEditDialog`)
hosts `GuardianSection` in `GuardianView.Edit`. When the operator picks "Add
guardian" the drawer shows the **Add** branch (`IsAdd == true`), which currently
offers only a **new** (blank) guardian form:

```
.guardian-edit-identity       ── "New guardian" (editedGuardianDisplayName)
.guardian-edit-form
  <GuardianEditFields>        ── Title / Name / Relationship+Role (+ compact contacts)
  .guardian-edit-contacts     ── compact contact manager (Draft)
```

There is **no way to instead link an existing guardian** from this drawer. The
existing-guardian typeahead ("Add Existing Guardian" sentinel → Contact|Student
search) already exists in the **Full-mode** inline add row
(`GuardianView.Full`, the page-side student form), but that surface is not
present inside the drawer, and the drawer's Add flow never reaches it. So a
user who wants to link a guardian that already exists on the school must
abandon the drawer workflow.

## 2. Goal

Add a lightweight **screen switch** inside the drawer's Add branch:

- The body title row reads **"New guardian"** with a **`FluentAnchor` on the
  far right — "Select existing guardian"** — that switches the drawer body to
  an **existing-guardian selection screen**.
- The existing-guardian screen reuses the proven search machinery: a
  `FluentAutocomplete` over `GuardianSearchRow` with the Contact | Student
  radio, a relationship dropdown for the link, and a commit action that adds
  the existing-guardian link to `GuardianLinks` (the same drafted
  `GuardianAssignment` list the rest of the dialog flushes atomically on
  Save).
- The same title-row anchor toggles back to "Add new guardian" while on the
  existing-guardian screen.
- The **new-guardian** form's identity fields are laid out as **vertical
  FormRows** (label on top) to fit the 420px drawer, with First name + Last
  name **combined** under a single "Name" row (the two inputs sit side-by-side
  below the label). This is the required layout for the Add form and is
  currently **not in place** (the fields render with default horizontal
  FormRows).

The Footer Save (added in the previous commit) must commit whichever surface
is active — a **new** guardian on the new-guardian screen, or an **existing**
guardian link on the selection screen.

**Non-goals**
- Do not change the Full-mode inline add row (`GuardianView.Full`) or its
  existing typeahead.
- No nested `DialogDrawer` / `FluentDialog` (the pattern doc forbids nested
  modals in the drawer); the switch is an in-body surface toggle like the
  contact sub-screens (R5).
- Guardian metadata that does not persist through the student save (title /
  name / contacts for existing guardians) is not captured here — only the
  link's relationship / role, matching the existing `ShowIdentityFields=false`
  reasoning.

## 3. Design

### 3.1 Mode flag + title row with far-right anchor

Add a small enum on `GuardianSection` (or a private nested enum in `@code`) and
a field, derived only when `IsAdd`:

```csharp
private enum DrawerAddMode { NewGuardian, ExistingGuardian }
private DrawerAddMode _drawerAddMode = DrawerAddMode.NewGuardian;
```

In the Add branch (`GuardianView.Edit && IsAdd`), wrap the bare
`.guardian-edit-identity` in a **title row** that carries the mode label on the
left and the toggle `FluentAnchor` pushed far right:

```razor
<div class="guardian-drawer-add-title">
    <span class="guardian-edit-identity-name">
        @(_drawerAddMode == DrawerAddMode.ExistingGuardian ? "Select existing guardian" : editedGuardianDisplayName)
    </span>
    <FluentAnchor Href="#"
                  Appearance="Appearance.Hypertext"
                  OnClick="ToggleDrawerAddModeAsync"
                  class="guardian-drawer-add-toggle">
        @(_drawerAddMode == DrawerAddMode.NewGuardian ? "Select existing guardian" : "Add New guardian")
    </FluentAnchor>
</div>
```

`ToggleDrawerAddModeAsync` flips `_drawerAddMode`, clears the selection state
(`_existingGuardianId`, `_pickedRelationshipId`, typeahead) when leaving the
selection screen, and calls `StateHasChanged`.

**Anchor role (confirmed).** The `FluentAnchor` is the mode switch for the
drawer Add body: it toggles between the new-guardian form and the
existing-guardian selection screen — the label reads "Select existing
guardian" on the new-guardian surface and "Add New guardian" on the selection
surface. It uses the native hypertext appearance
(`Appearance="Appearance.Hypertext"`), is positioned **to the right of the
body title** (the identity/name label that brackets the `<GuardianEditFields>`
add form), and is shown in the dialog's side drawer (the Add branch of
`GuardianView.Edit`). This resolves the placement decision: keep the name left
and push the anchor far right (`.guardian-drawer-add-toggle { margin-left:auto }`),
in the same row as the body title — no separate chrome row above the form.

### 3.1.2 Add-form identity fields: vertical FormRow + combined Name row

The Add form's identity fields are rendered by the shared `<GuardianEditFields>`
hosted inside the 420px drawer. The spec requires these fields to use the
**vertical** `FormRow` orientation (label on top, input below) — the horizontal
180px-label row is too wide for the drawer — and the name fields to be
**combined** into a single "Name" row:

- **Title** — `<FormRow Orientation="Vertical">`, label "Title" above the
  salutation dropdown.
- **Name** — a single `<FormRow Orientation="Vertical" Label="Name">` carrying
  First name + Last name side-by-side in the input cell below the label (each
  `flex: 1 1 0` via `.guardian-name-field`). Required.
- **Relationship/Role** — already `<FormRow Orientation="Vertical">` (unchanged).

This is the required layout for the Add form and is **not yet in place**: the
current `GuardianEditFields` renders Title / Name with default (horizontal)
FormRows. Implementation must switch the Title and Name rows to
`Orientation="Vertical"` in `GuardianEditFields.razor` (the combined Name row
already exists).

### 3.2 Existing-guardian selection sub-screen

While `_drawerAddMode == ExistingGuardian`, render an **existing-guardian
sub-screen** in place of the new-guardian identity + contact manager:

```razor
@if (_drawerAddMode == DrawerAddMode.ExistingGuardian)
{
    <div class="guardian-drawer-existing">
        <FluentAutocomplete TOption="GuardianSearchRow"
                            @ref="_typeahead"
                            Multiple="false"
                            MaximumOptionsSearch="30"
                            ImmediateDelay="300"
                            ShowProgressIndicator="true"
                            AutoComplete="off"
                            Placeholder="Search by guardian or student name…"
                            OptionText="(r => r.FullName)"
                            OptionComparer="GuardianSearchRowComparer.Instance"
                            OptionTemplate="RowTemplate"
                            OnOptionsSearch="OnTypeaheadSearchAsync"
                            SelectedOptionChanged="OnTypeaheadSelectedAsync"
                            class="guardian-typeahead" />
        <FluentRadioGroup @bind-Value="_searchMode" class="guardian-search-mode">
            <FluentRadio Value="SearchMode.Contact">Contact</FluentRadio>
            <FluentRadio Value="SearchMode.Student">Student</FluentRadio>
        </FluentRadioGroup>
        <CodedValueDropdown Parent="CodedValueParent.Relationships"
                            @bind-SelectedId="_pickedRelationshipId"
                            Placeholder="Relationship" />
        @* Role capture: relationship + role (confirmed §8). Mirrors the new /
           edit form's Relationship + CC row so the link records whether the
           guardian is the primary (CC unchecked) or a carbon-copy. *@
        <div class="guardian-role-checkbox">
            <FluentCheckbox @bind-Value="_pickedIsCC"
                            Label="CC"
                            HelpText="carbon-copy / not the primary" />
        </div>
    </div>
}
else
{
    @* existing new-guardian identity + <GuardianEditFields> + compact contacts *@
}
```

Reused (already implemented, Full-mode) helpers — no new search code:
- `OnTypeaheadSearchAsync` / `SearchContactRowsAsync` / `SearchGuardiansByStudentNameRowsAsync` — set `e.Items` from `Api.ListGuardiansAsync(ct, search, excludeStudentId: StudentId)` / student→guardian flattening; already capped and exclude already-drafted guardian ids.
- `OnTypeaheadSelectedAsync(GuardianSearchRow?)` — sets `_existingGuardianId`, `_pickedTitleId`, `_pickedRelationshipId`.
- `RowTemplate` / `GuardianSearchRowComparer` / `FormatGuardianName` — unchanged.

`@ref="_typeahead"`, `_searchMode`, and the typeahead debounce `_searchCts` are
already declared and must NOT be reset on a Full↔drawer split (the section is a
single instance; only one surface is mounted at a time).

### 3.3 Footer Save integration

`SaveAddGuardianAsync()` (already `public Task<bool>` from the previous commit)
currently always builds a **new** `GuardianAssignment(ExistingGuardianId: null,
...)`. Branch on the active add mode:

```csharp
if (_drawerAddMode == DrawerAddMode.ExistingGuardian)
{
    if (_existingGuardianId is null) { _guardianError = "Pick an existing guardian."; return false; }
    var assignment = new GuardianAssignment(
        ExistingGuardianId: _existingGuardianId,
        FirstName: _pickedFirstName ?? string.Empty,
        LastName: _pickedLastName ?? string.Empty,
        RelationshipCodedValueId: _pickedRelationshipId,
        ContactChannel: null, ContactValue: null,
        TitleCodedValueId: _pickedTitleId,
        CountryCode: null,
        Role: _pickedIsCC ? GuardianRole.CC : GuardianRole.Primary);
    await AddDraftAsync(assignment);      // guards duplicates (ExistingGuardianId)
    // Post-link navigation = STAY (confirmed §8): keep the selection surface
    // open so the operator can link more guardians, and clear only the
    // per-link selection state (NOT _drawerAddMode, which stays
    // ExistingGuardian). Returning false keeps the drawer open (the footer
    // closes only on true); the operator closes manually when done.
    ClearExistingSelectionState();
    return false;
}
// else: existing NewGuardian path
```

`AddDraftAsync` already skips a duplicate `ExistingGuardianId`, so re-linking a
guardian already in `GuardianLinks` is a no-op. Because the confirmed
post-link navigation is **stay** (not return-and-close), the existing-guardian
branch does **not** reset `_drawerAddMode` and returns `false` (drawer stays
open); the new-guardian branch keeps its existing `true`-closes behavior.

### 3.4 State & cleanup

- `_drawerAddMode` resets to `NewGuardian` whenever the drawer-add starts. The
  component's `InitializeEditViewAsync` (invoked from `OnParametersSetAsync`
  when `View == Edit`) resets it in its `IsAdd` branch, so a fresh Add begins on
  the new-guardian surface; the far-right anchor then drives the switch. (No host
  `StudentEditDialog` change is needed — the field is private to `GuardianSection`
  and the component is torn down on drawer close anyway.)
- `ClearExistingSelectionState()` nulls `_existingGuardianId` (+ `_drawerAddMode`
  reset on the toggle path), `_pickedRelationshipId`, `_pickedTitleId`, clears
  `_pickedIsCC` to false, cancels `_searchCts`, and resets the current
  draft-first/last names.
- Interaction with R5 (contact sub-screen): the existing-guardian sub-screen is
  mutually exclusive with a contact sub-screen (`_drawerAddMode` only has
  meaning on the Add surface, and only while `_contactEditTarget` is null). The
  footer Save stays visible (it is a guardian surface, not a contact
  sub-screen).

### 3.5 CSS (`GuardianSection.razor.css`)

Append:

- `.guardian-drawer-add-title` — flex row, `justify-content: space-between`,
  `align-items: center`.
- `.guardian-drawer-add-toggle { margin-left: auto; }` — pushes the anchor to
  the far right.
- `.guardian-drawer-existing` — column stack (`gap`), optional top border /
  divider under the title row; reuse the existing `.guardian-typeahead`,
  `.guardian-search-mode`, and `.guardian-search-*` classes.

## 4. Files to change

- `src/Students/SchoolCollab.Students.Application/Components/Students/GuardianSection.razor`
  — Add-branch title row + anchor, existing-guardian sub-screen markup,
  `_drawerAddMode` branch in `SaveAddGuardianAsync`, `ToggleDrawerAddModeAsync`,
  `ClearExistingSelectionState`.
- `src/Students/SchoolCollab.Students.Application/Components/Students/GuardianSection.razor.css`
  — `.guardian-drawer-add-title`, `.guardian-drawer-add-toggle`,
  `.guardian-drawer-existing`.
- `src/Students/SchoolCollab.Students.Application/Components/Students/StudentEditDialog.razor`
  — reset `_drawerAddMode` on drawer close (host-side, one line).
- `src/SchoolCollab.Admin.Shared/Components/GuardianEditFields.razor`
  — switch the Title and Name FormRows to `Orientation="Vertical"` (the combined
  "Name" row with First + Last side-by-side is already present via
  `GuardianEditFields.razor.css` `.guardian-name-field`). See §3.1.2.

No `ContactsEditor`, `DialogDrawer`, or API changes. (`GuardianEditFields` is now
in scope for the vertical FormRow / combined-Name layout, §3.1.2.)

## 5. Tests (source-assertion + bUnit, `StudentFormFieldsSectionEditTests.cs`)

- Source-assert: the Add branch renders a far-right `FluentAnchor` with
  `class="guardian-drawer-add-toggle"` and a "Select existing guardian" label,
  and `SaveAddGuardianAsync` branches on `_drawerAddMode ==
  DrawerAddMode.ExistingGuardian`.
- Source-assert: the existing-guardian body reuses `<FluentAutocomplete
  TOption="GuardianSearchRow"` and the Contact | Student `FluentRadioGroup`.
- bUnit behavior: open the drawer Add; click the toggle anchor → existing
  surface renders; pick a `GuardianSearchRow`; commit via Footer Save →
  `GuardianLinks` gains a draft with the picked `ExistingGuardianId` and the
  read-only summary reflects it; toggle back → the new-guardian surface
  returns.
- Source-assert (`GuardianEditFields`): the Title and Name FormRows use
  `Orientation="Vertical"`, and the Name row is a single `FormRow Label="Name"`
  carrying both `Model.FirstName` and `Model.LastName` inputs (§3.1.2).

## 6. Acceptance criteria

- [ ] The drawer Add branch shows **"New guardian"** on the left and a
  **far-right "Select existing guardian" FluentAnchor**.
- [ ] Clicking the anchor switches the body to the **existing-guardian
  selection screen** (typeahead + Contact|Student radio + relationship
  dropdown **+ CC/role capture**); a back anchor ("Add New guardian") returns
  to the new-guardian screen.
- [ ] Footer Save on the selection screen links the chosen **existing**
  guardian with its **relationship + role (CC)** (dedup handled by
  `AddDraftAsync`), **stays on the selection screen** to link more (does not
  close the drawer / does not reset to new-guardian), and clears only the
  per-link selection; on the new-guardian screen it still creates a **new**
  guardian and closes.
- [ ] The drawer title stays "Add guardian" (unchanged); only the body content
  switches.
- [ ] The Add form identity fields use **vertical FormRows**, with the name
  fields **combined** into a single "Name" row (First | Last below the label).
- [ ] No nested dialog / drawer; no change to the Full-mode inline add row.
- [ ] Admin + Students unit suites pass (existing tests kept green).

## 7. Out of scope

- Editing an existing guardian's identity / title / contacts from this drawer
  (guardian-surface operations) — unchanged.
- The page-side ("Add Existing Guardian" sentinel) flow — unchanged.
- `ContactsEditor` / `DialogDrawer` component changes (`GuardianEditFields`
  layout — vertical FormRow + combined Name — is in scope, §3.1.2).

## 8. Open decisions (confirm before implementing)

- **Anchor copy / placement:** **confirmed** — the anchor toggles the drawer
  body between the new-guardian form and the existing-guardian selection screen;
  native `Hypertext` appearance; positioned **to the right of the body title**
  in the Add form (right-aligned `margin-left:auto` in the same title row, not
  a separate row above it); shown in the dialog side drawer.
- **Post-link navigation:** after linking an existing guardian, **stay** on the
  existing-guardian surface to link more (confirmed). Do not return to the
  new-guardian surface and do not auto-close; clear only the per-link selection
  state and keep the drawer open (Footer Save returns `false` for the
  existing-guardian branch so it does not close; the operator closes manually).
- **Role capture:** **relationship + role** (confirmed). The selection screen
  records the link's role (Primary / CC) via a CC checkbox alongside the
  Relationship dropdown, mirroring the new/edit form's Relationship + `IsCC`
  row; the assignment is drafted with `Role` (confirmed).