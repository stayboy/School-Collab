# Implementation Steps: Existing-guardian Selection from the "New guardian" Drawer

Plan: `2026-08-20-guardian-drawer-existing-guardian-selection.md`
Branch: `feature/contact-guardian-form-fields-consolidation`

## Overview
Add a screen switch inside the drawer's Add branch to allow linking existing guardians (instead of only creating new ones). The drawer currently only offers a blank "New guardian" form; this plan adds the ability to search and link existing guardians.

## 1. Problem
- The student edit dialog's shared drawer hosts `GuardianSection` in `GuardianView.Edit`
- When operator picks "Add guardian", the drawer shows only the **Add** branch with a blank form
- There's no way to link an existing guardian from the drawer
- The existing-guardian typeahead exists in Full-mode inline add row, but not in the drawer

## 2. Goal
Add a lightweight screen switch inside the drawer's Add branch:
- Title row with left label and far-right `FluentAnchor` "Select existing guardian"
- Clicking anchor switches body to existing-guardian selection screen
- Screen reuses proven search machinery: `FluentAutocomplete` over `GuardianSearchRow`
- Contact | Student radio, relationship dropdown, commit action adds link to `GuardianLinks`
- Same anchor toggles back to "Add new guardian"

## 3. Design & Implementation Steps

### 3.1 Mode Flag + Title Row with Far-Right Anchor
- **Add enum** on `GuardianSection`: `private enum DrawerAddMode { NewGuardian, ExistingGuardian }`
- **Add field**: `private DrawerAddMode _drawerAddMode = DrawerAddMode.NewGuardian;`
- **In Add branch** (`GuardianView.Edit && IsAdd`): wrap `.guardian-edit-identity` in a title row:
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
- **Implement `ToggleDrawerAddModeAsync`**: flips `_drawerAddMode`, clears selection state, calls `StateHasChanged`
- **Anchor role (confirmed)**: the anchor toggles the drawer Add body between the new-guardian form and the existing-guardian selection screen. Native `Hypertext` appearance; positioned **to the right of the body title** (right-aligned `margin-left:auto` in the same title row as the identity/name label that brackets the `<GuardianEditFields>` add form — not a separate chrome row above it); shown in the dialog side drawer
- CSS: `.guardian-drawer-add-title` (flex row, justify-between, align-center), `.guardian-drawer-add-toggle` (`margin-left: auto`)

### 3.2 Existing-guardian Selection Sub-screen
- While `_drawerAddMode == ExistingGuardian`, render:
  ```razor
  <div class="guardian-drawer-existing">
      <FluentAutocomplete TOption="GuardianSearchRow"
                          ref="_typeahead"
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
      @* role capture: relationship + role (confirmed §8) — CC checkbox
         mirrors the new/edit Relationship + IsCC row *@
      <div class="guardian-role-checkbox">
          <FluentCheckbox @bind-Value="_pickedIsCC"
                          Label="CC"
                          HelpText="carbon-copy / not the primary" />
      </div>
  </div>
  ```
- **Reused helpers** (already implemented in Full-mode):
  - `OnTypeaheadSearchAsync` / search methods
  - `OnTypeaheadSelectedAsync` – sets `_existingGuardianId`, `_pickedTitleId`, `_pickedRelationshipId`
  - `RowTemplate`, `GuardianSearchRowComparer`, `FormatGuardianName`
- **Must NOT reset** `@ref="_typeahead"`, `_searchMode`, `_searchCts` on Full↔drawer split

### 3.3 Footer Save Integration
- **Branch `SaveAddGuardianAsync()`** on `_drawerAddMode`:
  - **ExistingGuardian mode**: validate `_existingGuardianId` is not null; build `GuardianAssignment(ExistingGuardianId: _existingGuardianId, ..., Role: _pickedIsCC ? GuardianRole.CC : GuardianRole.Primary)`; call `AddDraftAsync(assignment)` (already guards duplicates); **stay** on the selection surface — do NOT reset `_drawerAddMode`, call `ClearExistingSelectionState()` and `return false` (drawer stays open to link more)
  - **NewGuardian mode**: existing path (creates new guardian, returns `true` → closes)

### 3.4 State & Cleanup
- `_drawerAddMode` resets to `NewGuardian` when a drawer-add starts: `InitializeEditViewAsync` (from `OnParametersSetAsync`) resets it in its `IsAdd` branch; toggling via `ToggleDrawerAddModeAsync` clears it on the switch away from the selection surface. (The field is private to `GuardianSection`, so no host `StudentEditDialog` change is required — the component instance is torn down when the drawer closes anyway.)
- **Implement `ClearExistingSelectionState()`** (coded as `ClearDrawerExistingSelectionState`): nulls `_existingGuardianId`, `_pickedRelationshipId`, `_pickedTitleId`, resets `_pickedIsCC` to false, clears the current draft first/last names, cancels `_searchCts` via `ClearTypeaheadState()`
- Existing-guardian sub-screen is mutually exclusive with contact sub-screen (`_contactEditTarget` must be null)
- **Confirmed: "stay" post-link navigation** — after a successful link the selection surface stays open (`_drawerAddMode` NOT reset; `SaveAddGuardianAsync` returns `false` via `SaveExistingGuardianLinkAsync`) so the operator can link more guardians

### 3.5 CSS (`GuardianSection.razor.css`)
Append:
- `.guardian-drawer-add-title` — flex row, `justify-content: space-between`, `align-items: center`
- `.guardian-drawer-add-toggle { margin-left: auto; }` — pushes anchor far right
- `.guardian-drawer-existing` — column stack (`gap`), optional divider under title row; reuses `.guardian-typeahead`, `.guardian-search-mode`, `.guardian-search-*` classes

### 3.6 Host Modification
- **None.** The `_drawerAddMode` reset lives in `GuardianSection.InitializeEditViewAsync`'s `IsAdd` branch (private field; component disposed on drawer close).

### 3.7 Files to Change
- `GuardianSection.razor` — add-branch title row + anchor, existing-guardian sub-screen markup, `_drawerAddMode` branch in `SaveAddGuardianAsync` (via `SaveExistingGuardianLinkAsync`), `ToggleDrawerAddModeAsync`, `ClearDrawerExistingSelectionState`
- `GuardianSection.razor.css` — `.guardian-drawer-add-title`, `.guardian-drawer-add-toggle`, `.guardian-drawer-existing`
- `GuardianEditFields.razor` — Title + Name FormRows switched to `Orientation="RowOrientation.Vertical"` (combined First|Last Name row, plan §3.1.2)
- No `StudentEditDialog.razor` change (the reset is component-side; see §3.4/§3.6)

## 4. Tests (Source-assert + bUnit)
- **Source-assert**: Add branch renders far-right `FluentAnchor` with `class="guardian-drawer-add-toggle"` and "Select existing guardian" label; `SaveAddGuardianAsync` branches on `_drawerAddMode == DrawerAddMode.ExistingGuardian`
- **Source-assert**: Existing-guardian body reuses `<FluentAutocomplete TOption="GuardianSearchRow">`, Contact|Student `FluentRadioGroup`, and the relationship dropdown **+ CC checkbox (role capture)**
- **bUnit behavior**: Open drawer Add; click toggle → existing surface renders; pick `GuardianSearchRow` + relationship + CC; commit via Footer Save → `GuardianLinks` gains draft with picked `ExistingGuardianId` and `Role` (CC), the selection surface **stays open** (drawer not closed, mode not reset) to link more; toggle back → new-guardian surface returns

## 5. Acceptance Criteria
- [ ] Drawer Add branch shows "New guardian" on left and far-right "Select existing guardian" FluentAnchor
- [ ] Clicking anchor switches body to existing-guardian selection screen (typeahead + Contact|Student radio + relationship dropdown + CC/role); back anchor returns to new-guardian screen
- [ ] Footer Save on selection screen links chosen existing guardian with relationship + role (dedup by `AddDraftAsync`), **stays on the selection screen** (no close, no reset to new-guardian), clearing only per-link selection; on new-guardian screen still creates new guardian and closes
- [ ] Drawer title stays "Add guardian" (unchanged); only body content switches
- [ ] No nested dialog/drawer; no change to Full-mode inline add row
- [ ] Admin + Students unit suites pass (existing tests kept green)

## 6. Out of Scope
- Editing existing guardian's identity / title / contacts from this drawer
- The page-side ("Add Existing Guardian" sentinel) flow
- `ContactsEditor`, `DialogDrawer` component changes

## 7. Open Decisions (confirmed)
- **Anchor copy/placement**: **confirmed** — the anchor toggles the drawer body between the new-guardian form and the existing-guardian selection screen; native `Hypertext` appearance; positioned to the right of the body title (right-aligned `margin-left:auto` in the same title row, not a separate row above it); shown in the dialog side drawer
- **Post-link navigation**: **stay** on the existing-guardian surface to link more (confirmed) — selection surface stays open; `SaveAddGuardianAsync` returns `false` for the existing branch
- **Role capture**: **relationship + role (CC checkbox)** (confirmed) — drafted with `Role: _pickedIsCC ? GuardianRole.CC : GuardianRole.Primary`