# Guardian Dialog & Grid UX Refinement Plan

**Date:** 2026-07-25  
**Branch target:** `feature/guardian-grid-ux-refinement` (to be created from `main` after the current `feature/guardian-link-from-student-edit-gradelevel-wizard` branch is merged or its work is complete)  
**Status:** Planned — implementation deferred  
**Related plan:** [2026-07-25-guardian-link-from-student-edit-gradelevel-wizard.md](./2026-07-25-guardian-link-from-student-edit-gradelevel-wizard.md)  

---

## 1. Goals

This plan captures seven related UX requests for guardian selection and display surfaces:

1. **Larger modal dialog** for the guardian picker / add flow.
2. **Grid header titles never clip** — wrap or truncate gracefully with full text available to the user.
3. **Preferred contact column no longer clips** in the 3-column picker grid.
4. **New-guardian setup uses a multi-contact editor** (like `ContactsEditor`) instead of the current single-contact `GuardianFormFields`.
5. **Student guardian list shows up to 3 contacts per row**, formatted as channel on top and contact value below.
6. **One reusable grid component** for guardian selection/display everywhere.
7. **Tick/check indicator** in each contact column to show whether the contact is primary and/or carries a country code.

---

## 2. Non-Goals

- Do **not** change the underlying `Contact` entity or database schema beyond possibly extending DTOs.
- Do **not** change the current page-level create/link flow on `Edit.razor` / `Detail.razor` / `GradeLevelWizard.razor` unless required by the new component contract.
- Do **not** support more than three contacts in the compact per-row display; additional contacts remain reachable via the full `ContactsEditor` after the guardian exists.

---

## 3. Design Overview

The work reshapes three public surfaces:

| Surface | Current | Proposed |
|---------|---------|----------|
| `GuardianPickerDialog` | 3-column `EntityGrid<GuardianDto>` + inline single-contact `GuardianFormFields` | Wider dialog; single reusable `GuardianGrid` in picker mode; inline **in-memory** multi-contact editor for new guardians |
| `StudentGuardiansList` | 6-column grid with one primary contact | 4-column grid (Name, Relationship, Contacts ≤3, Role, Actions) using same `GuardianGrid` in display mode |
| `GuardiansTab` / legacy grids | Deprecated / separate | Replaced by `GuardianGrid` |

A new `GuardianGrid.razor` component is the central primitive. It consumes a normalized row model (`GuardianGridRow`) and exposes two modes:

- **`Picker`** — multi-select checkbox, search (still owned by parent), Name column, up to 3 Contacts columns, optional "New guardian" button.
- **`Display`** — Name (+ emergency badge), Relationship, up to 3 Contacts columns, Role badge, per-row Actions.

A new `GuardianContactsEditor.razor` component provides the `ContactsEditor`-style UI but operates **in-memory**, so the picker can capture multiple contacts for a guardian that has not yet been persisted. On confirm the parent receives the contacts and creates them together with the guardian.

---

## 4. Detailed Design

### 4.1 Larger modal dialog

- Change the call sites that open `GuardianPickerDialog` (`Edit.razor`, `Detail.razor`, and any wizard usage) to pass `DialogSize.Large` (or `DialogSize.Panel` if the in-memory contact editor needs even more width).
- Verify `DialogServiceExtensions.ShowShellDialogAsync` supports the size parameter — it already accepts `DialogSize size = DialogSize.Small`.
- Also consider increasing `GuardianFormDialog` to at least `DialogSize.Medium` if it still used anywhere; the new `GuardianContactsEditor` should be sized through the picker, not a separate dialog.

**Files to change:** `Edit.razor`, `Detail.razor`, wizard caller(s).

### 4.2 Grid header titles never clip

`FluentDataGrid` headers use the supplied `Title` text and can clip when the column is narrower than the label, even with `MultiLine="true"`. Two complementary fixes:

1. **CSS:** add a global rule in `EntityGrid.razor.css` (and inherited by `GuardianGrid`):
   ```css
   .entity-grid-scroll fluent-data-grid thead th .col-title {
       white-space: normal;
       overflow: visible;
       text-overflow: clip;
       line-height: 1.3;
   }
   ```
   (The exact selector depends on FluentUI 4.14.x markup; verify in browser DevTools.)
2. **Fallback / accessibility:** if the header cell structure is not styleable, replace plain `Title="..."` with `TitleTemplate` on every `TemplateColumn` so the header renders a `<span title="...">` with `white-space: normal`. This also gives the user a tooltip on hover.

`GuardianGrid` should expose a helper method or CSS class that guarantees wrapped headers for all its built-in columns.

**Files to change:** `EntityGrid.razor.css`, `GuardianGrid.razor`.

### 4.3 Preferred contact column no longer clips

Current picker template:
```csharp
GridTemplateColumns = "minmax(180px,2fr) minmax(130px,1fr) minmax(180px,2fr)"
```
Proposed templates after header/content audit:

- **Picker:** Name | Contact 1 | Contact 2 | Contact 3 (or fewer) — see §4.6.
  Template example: `"minmax(200px,1.5fr) minmax(170px,1fr) minmax(170px,1fr) minmax(170px,1fr)"`.
- Each contact column has a fixed minimum wide enough for "WhatsApp" + value + tick.
- Add CSS to the contact-cell so long channel names wrap (`white-space: normal`) and the value line truncates with ellipsis but exposes a `title` tooltip.

### 4.4 In-memory multi-contact editor for new guardians

The current picker New panel uses `GuardianFormFields`, which only captures one contact. Replace it with a new component that reuses the visual design of `ContactsEditor` but stores contacts locally.

#### New component: `GuardianContactsEditor.razor`

Parameters:
```csharp
[Parameter] public List<ContactModel> Contacts { get; set; } = new();
[Parameter] public EventCallback<List<ContactModel>> ContactsChanged { get; set; }
```

Where `ContactModel` is a lightweight mutable model (not a DTO) owned by the picker:
```csharp
public sealed class ContactModel
{
    public Guid TempId { get; set; } = Guid.NewGuid();
    public ContactChannel Channel { get; set; }
    public string? CountryCode { get; set; }
    public string Value { get; set; } = string.Empty;
    public string? Label { get; set; }
    public bool IsPrimary { get; set; }
}
```

Behavior:
- Same add row as `ContactsEditor`: Channel dropdown, optional country-code dropdown for SMS/WhatsApp, Value text field, optional Label, Primary checkbox, Add button.
- List rendered with channel glyph, formatted value, Primary/Verified-style badges, Set-primary and Remove actions.
- No API calls. `OnChannelChanged` still loads country-code options via `CodedValuesApiClient`.
- Country-code default Ghana (`+233`), copied from `ContactsEditor`.

#### Picker integration

In `GuardianPickerDialog` New mode:
- Keep a `GuardianAssignmentModel` for identity fields (First/Last/Title/Relationship/Role).
- Add `List<ContactModel> _newContacts` bound to `GuardianContactsEditor`.
- On "Add to list":
  - Validate at least one contact exists **or** keep name-only fallback? Decision needed; recommend requiring at least one contact because the user explicitly asked for a contact editor.
  - Pick the first `IsPrimary` contact (or the first contact if none marked primary) to populate the synthetic `GuardianDto.PrimaryContact*` fields so the picker grid shows a primary contact immediately.
  - Store the full `ContactModel[]` inside `GuardianAssignment`.

#### Data model change: `GuardianAssignment`

Extend the record with a contacts collection:
```csharp
public sealed record GuardianAssignment(
    Guid? ExistingGuardianId,
    string FirstName,
    string LastName,
    Guid? RelationshipCodedValueId,
    ContactChannel? ContactChannel,
    string? ContactValue,
    Guid? TitleCodedValueId,
    string? CountryCode = null,
    GuardianRole Role = GuardianRole.Primary,
    IReadOnlyList<ContactRequest>? Contacts = null);
```

`ContactRequest` already exists in the Core contracts and is what the parent uses to call `AddContactAsync`. Mapping from `ContactModel` → `ContactRequest` is trivial.

This extension is backward-compatible because of the optional default.

#### Parent-page change

When a new `GuardianAssignment` (with `ExistingGuardianId == null`) is returned:
- Create guardian.
- Link guardian.
- If `Contacts` is non-empty, call `AddContactAsync` for each contact (mirrors current Phase 4 wizard logic but iterated).
- Set the first contact as primary server-side if `IsPrimary` is true (the existing `SetPrimaryContactAsync` endpoint does this).

### 4.5 Student guardian list: up to 3 contacts per row

#### DTO enrichment

`StudentGuardianViewDto` currently carries only the primary contact. Extend it to carry the top N contacts:
```csharp
public sealed record GuardianContactView(
    ContactChannel Channel,
    string Value,
    string? CountryCode,
    bool IsPrimary);

public sealed record StudentGuardianViewDto(
    ...existing fields...,
    GuardianContactView[] Contacts = null!);
```

Use `GuardianContactView[]` instead of `IReadOnlyList<ContactDto>` to avoid leaking unneeded fields (`IsVerified`, `Label`, subscription state) into the read-only list.

#### Handler change

`ListGuardiansByStudentHandler` already batch-loads contacts. Modify the projection to keep up to 3 non-deleted contacts per guardian, ordered by `IsPrimary` descending then by creation order. The existing single-contact fields can be deprecated/removed or kept for backward compatibility; recommend removing them once `GuardianGrid` is updated.

#### Component change

`StudentGuardiansList` becomes a thin wrapper around `GuardianGrid` in `Display` mode:
```razor
<GuardianGrid Mode="GuardianGridMode.Display"
              Items="@_rows"
              RelationshipNameLookup="RelationshipNameLookup"
              OnEdit="OnEdit"
              OnRemove="OnRemove" />
```

The Contacts column renders each `GuardianContactView` as:
```
<div class="contact-stack">
    <span class="contact-channel">📱 WhatsApp</span>
    <span class="contact-value">+233 0241234567</span>
    <span class="contact-meta">✓ Primary · +233</span>
</div>
```

### 4.6 Single reusable `GuardianGrid` component

#### New file: `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardianGrid.razor`

A normalized row model:
```csharp
public sealed record GuardianGridRow(
    Guid Id,
    string FirstName,
    string LastName,
    string? DisplayName,
    Guid? TitleCodedValueId,
    Guid? RelationshipCodedValueId,
    GuardianRole Role,
    bool IsEmergencyContact,
    IReadOnlyList<GuardianContactView> Contacts);
```

Parameters:
```csharp
[Parameter, EditorRequired] public GuardianGridRow[] Items { get; set; } = [];
[Parameter, EditorRequired] public GuardianGridMode Mode { get; set; }
[Parameter] public Dictionary<object, GuardianGridRow>? Selected { get; set; }
[Parameter] public EventCallback<Dictionary<object, GuardianGridRow>> SelectedChanged { get; set; }
[Parameter] public Func<Guid?, string?>? RelationshipNameLookup { get; set; }
[Parameter] public EventCallback<GuardianGridRow> OnEdit { get; set; }
[Parameter] public EventCallback<GuardianGridRow> OnRemove { get; set; }
[Parameter] public EventCallback OnAddNew { get; set; }
```

Modes:
```csharp
public enum GuardianGridMode { Picker, Display }
```

Built-in column layout:
- **Picker:** Checkbox (if `Selected` bound), Name, Contact 1, Contact 2, Contact 3.
- **Display:** Name (with emergency badge), Relationship, Contact 1, Contact 2, Contact 3, Role badge, Actions.

Internally uses `EntityGrid<GuardianGridRow>` so it inherits search, empty state, and selection behavior.

#### Migration path

1. Build `GuardianGrid`.
2. Replace the grid in `GuardianPickerDialog` with `GuardianGrid` in `Picker` mode.
3. Replace the grid in `StudentGuardiansList` with `GuardianGrid` in `Display` mode.
4. Remove or deprecate `GuardiansTab` (already marked deprecated).
5. Update `GuardianAssignmentList` (wizard) to use `GuardianGrid` if it also lists guardians; otherwise leave it.

### 4.7 Tick/check sign for primary / country code

Use FluentUI icons for consistency:
- **Primary:** `FluentIcons.Checkmark` or `FluentIcons.CheckmarkCircle` with `Appearance.Accent`, title="Primary contact".
- **Country code:** do not use a tick; instead display the dial code as text (e.g., `+233`). The "tick sign" requirement is interpreted as: a visible indicator sits beside each contact value showing whether it is the primary contact. The country code is already shown as part of the formatted value.

Alternative: if the user literally wants a tick to mean "has country code", show `FluentIcons.Checkmark` next to the country-code text. Combined render:
```
📱 WhatsApp
+233 0241234567
✓ Primary   CC: +233
```

CSS class `.contact-meta` will host the indicator row.

---

## 5. Data Model / DTO Changes

| File | Change |
|------|--------|
| `GuardianAssignment.cs` | Add optional `IReadOnlyList<ContactRequest>? Contacts` parameter |
| `GuardianDto.cs` | Keep existing primary-contact fields; no change required if `GuardianGrid` maps from DTO |
| `StudentGuardianViewDto.cs` | Replace primary-contact scalar fields with `GuardianContactView[] Contacts` (default empty array) |
| New `GuardianGridRow.cs` / `GuardianContactView.cs` | Add in the Admin component namespace or Core DTOs |

---

## 6. API / Client Changes

- No new endpoints required.
- Parent pages will iterate over `GuardianAssignment.Contacts` and call the existing `AddContactAsync` for each.
- `IContactsClient.AddContactAsync` already accepts `AddContactRequest` with `CountryCode`.

---

## 7. Component Changes

| File | Action |
|------|--------|
| New `GuardianGrid.razor` + `.css` | Create reusable grid |
| New `GuardianContactsEditor.razor` + `.css` | Create in-memory multi-contact editor |
| `GuardianPickerDialog.razor` | Use `GuardianGrid`; replace New-panel `GuardianFormFields` with `GuardianContactsEditor`; enlarge dialog |
| `GuardianPickerDialog.razor.css` | Adjust for larger dialog and contact columns |
| `StudentGuardiansList.razor` | Replace grid with `GuardianGrid` Display mode |
| `StudentGuardiansList.razor.css` | Merge useful styles into `GuardianGrid.razor.css`; otherwise delete |
| `GuardiansTab.razor` | Remove or keep deprecated comment; do not extend |
| `GuardianFormFields.razor` | Keep for inline mode in long forms if still needed, but no longer used by picker |
| `EntityGrid.razor` / `.css` | Add header-wrap CSS and verify `MultiLine` propagates to headers |

---

## 8. CSS Changes

- `EntityGrid.razor.css`: header wrapping rules.
- `GuardianGrid.razor.css`: column templates for Picker/Display, contact-stack layout, tick/meta styling, emergency badge inline layout.
- `GuardianContactsEditor.razor.css`: reuse the layout of `ContactsEditor.razor.css` (or import/shared classes) without the page-card styling.
- `GuardianPickerDialog.razor.css`: widen `.picker-dialog` max-width if needed; the dialog width is primarily controlled by `DialogSize` but body CSS can add padding/max-width guards.

---

## 9. Test Plan

- Unit tests for `GuardianGrid` rendering (markup contains expected columns, no clipping CSS classes applied).
- Unit tests for `GuardianContactsEditor`:
  - Add contact updates `ContactsChanged`.
  - SMS/WhatsApp shows country-code dropdown.
  - Primary flag surfaces on the right contact.
- Update `StudentDetailSectionsTests` if it still asserts the old 6-column layout.
- Update CQRS handler tests for `ListGuardiansByStudentHandler` to assert up to 3 contacts are projected.
- Run the full suite with `-p:BuildProjectReferences=false` to avoid file-lock issues.

---

## 10. Open Questions / Decisions Needed

1. **Dialog size:** `Large` (640 px typical) or `Panel` (full-height side panel)? Recommendation: `Large` for the picker; `Panel` only if the in-memory contact editor feels cramped.
2. **New-guardian contact requirement:** Require at least one contact, or allow name-only and default to no contacts? Recommendation: require at least one contact because the user explicitly replaced the single-contact form with a contact editor.
3. **Tick meaning:** Does "tick for primary or cc" mean one tick for either condition, or separate indicators? Recommendation: separate — checkmark for primary, country-code text for CC.
4. **Relationship/Role in picker create panel:** Keep Relationship dropdown (it is per-link) and add Role (Primary/CC)? Current `GuardianAssignmentModel` already has these; ensure the new editor still captures them.
5. **Backward compatibility of `StudentGuardianViewDto`:** The DTO is a positional record. Adding a new array parameter at the end with a default empty array keeps existing callers compiling, but verify all construction sites.

---

## 11. Suggested Implementation Order

1. **Header wrapping CSS** — low risk, global improvement.
2. **`GuardianGrid` shell** — build the component with Picker and Display modes using mock data.
3. **StudentGuardiansList migration** — switch to `GuardianGrid` Display mode; verify 3-contact rendering.
4. **DTO/handler enrichment** — add `GuardianContactView[]` to `StudentGuardianViewDto`; update `ListGuardiansByStudentHandler`.
5. **`GuardianContactsEditor` in-memory component** — build and unit-test independently.
6. **Picker migration** — swap grid to `GuardianGrid`, swap New panel to `GuardianContactsEditor`, extend `GuardianAssignment`, wire parent pages to create multiple contacts.
7. **Dialog sizing + polish** — bump sizes, tune column widths, add tick indicators.
8. **Full test pass + cleanup** — remove `GuardiansTab`, consolidate CSS.

---

## 12. Risks

- **Scope creep:** items 4–7 together are large. Consider splitting into two deliverables: (A) grid unification + 3-contact display, (B) in-memory contact editor + picker migration.
- **`ContactsEditor` API dependency:** The in-memory editor must not accidentally call `IContactsClient`; careful parameter design is required.
- **Grid column width on small screens:** More columns (up to 3 contacts + Name + checkbox) may overflow on narrow viewports; ensure `minmax()` and horizontal scroll in `EntityGrid` handle it.
- **Record positional constructor breaks:** `StudentGuardianViewDto` is widely used; adding the contacts array must not break handlers or tests.
- **File-lock build issues:** Continue using individual project builds and `-p:BuildProjectReferences=false` for tests while VS / API / Worker processes are running.

---

## 13. Files to Create

- `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardianGrid.razor`
- `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardianGrid.razor.css`
- `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardianContactsEditor.razor`
- `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardianContactsEditor.razor.css`
- `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardianGridRow.cs` (or place in a shared models folder)

## 14. Files to Modify

- `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardianPickerDialog.razor` + `.css`
- `src/Students/SchoolCollab.Students.Admin/Components/Students/StudentGuardiansList.razor` + `.css`
- `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/Edit.razor`
- `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/Detail.razor`
- `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/GradeLevels/GuardianAssignment.cs`
- `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/GradeLevels/GradeLevelWizard.razor`
- `src/Students/SchoolCollab.Students.Core/DTOs/StudentGuardianViewDto.cs`
- `src/Students/SchoolCollab.Students.Core/CQRS/Guardians/Queries/ListGuardiansByStudent/ListGuardiansByStudentHandler.cs`
- `src/SchoolCollab.Admin.Shared/Components/EntityGrid.razor.css`
- `tests/SchoolCollab.Admin.Tests.Unit/StudentDetailSectionsTests.cs`
