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
7. **Preferred-contact indicator** in the contact columns — a subtle marker on the single highest-priority contact. Contacts have **no Primary/CC role**; that role belongs to the guardian *link* (`StudentGuardian.Role`), not the contact, so there is no per-contact role tick.
8. **Title included in guardian displayed name** — the canonical single-line guardian display is `Title GuardianCombinedName (Relationship, FirstPrimaryContact)` (e.g. `"Mr. John Smith (Father, +233 0241234567)"`) used for pills/chips and tooltips; the grid Name cell shows `Title Name` with relationship/contacts in dedicated columns.
9. **Contact ordering** — contacts carry a display-order / relevance field used to prioritise which contacts show first in the 3-column grid and to identify the preferred ("first primary") contact.

---

## 2. Non-Goals

- The `Contact` schema change is limited to adding a `DisplayOrder` ordering column and removing the redundant `IsPrimary` role flag (see §4.9); no other entity/schema changes.
- Do **not** change the current page-level create/link flow on `Edit.razor` / `Detail.razor` / `GradeLevelWizard.razor` unless required by the new component contract.
- Do **not** support more than three contacts in the compact per-row display; additional contacts remain reachable via the full `ContactsEditor` after the guardian exists.

---

## 3. Design Overview

The work reshapes three public surfaces:

| Surface | Current | Proposed |
|---------|---------|----------|
| `GuardianPickerDialog` | 3-column `EntityGrid<GuardianDto>` + inline single-contact `GuardianFormFields` | Wider dialog; single reusable `GuardianGrid` in picker mode; inline **in-memory** multi-contact editor for new guardians; title shown in name display |
| `StudentGuardiansList` | 6-column grid with one primary contact | grid (Name incl. title, Relationship, Contacts ≤3, **Primary tick**, Actions) using same `GuardianGrid` in display mode |
| `GuardiansTab` / legacy grids | Deprecated / separate | Replaced by `GuardianGrid` |

A new `GuardianGrid.razor` component is the central primitive. It consumes a normalized row model (`GuardianGridRow`) and exposes two modes:

- **`Picker`** — multi-select checkbox, search (still owned by parent), Name column, up to 3 Contacts columns, optional "New guardian" button.
- **`Display`** — Name (+ title + emergency badge), Relationship, up to 3 Contacts columns, **Primary tick column** (checkmark when the guardian link's `Role == GuardianRole.Primary`; header "Primary"), per-row Actions.

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

- **Picker:** Name | Contact 1 | Contact 2 | Contact 3 (or fewer) — see §4.6. The old separate "Preferred contact" + "Contact value" columns are **removed**; the primary contact now appears as the first contact column (ordered IsPrimary-first), which also resolves the original "Preferred contact" clipping complaint (req 3) because the column is wider and the channel label wraps.
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
    /// <summary>Display/priority order (0 = highest priority / preferred).
    /// Assigned by add sequence; the editor exposes move-up/move-down to
    /// reorder. Replaces the binary IsPrimary role on contacts — the
    /// Primary/CC role belongs to the guardian link, not the contact.
    /// </summary>
    public int Order { get; set; }
}
```

Behavior:
- Same add row as `ContactsEditor`: Channel dropdown, optional country-code dropdown for SMS/WhatsApp, Value text field, optional Label, Add button. **No "Primary" checkbox** — contacts have no Primary/CC role (that is a guardian-link concept). Priority is expressed by `Order` (add sequence); the editor exposes move-up / move-down buttons to reorder.
- List rendered with channel glyph, formatted value, Verified badge, move-up / move-down, and Remove actions. The first row (lowest `Order`) is the "preferred" contact.
- No API calls. `OnChannelChanged` still loads country-code options via `CodedValuesApiClient`.
- Country-code default Ghana (`+233`), copied from `ContactsEditor`.

#### Picker integration

In `GuardianPickerDialog` New mode:
- Keep a `GuardianAssignmentModel` for identity fields (First/Last/Title/Relationship/Role).
- Add `List<ContactModel> _newContacts` bound to `GuardianContactsEditor`.
- On "Add to list":
  - Validate at least one contact exists **or** keep name-only fallback? Decision needed; recommend requiring at least one contact because the user explicitly asked for a contact editor.
  - Pick the first contact by `Order` (lowest = preferred) to populate the synthetic `GuardianDto.PrimaryContact*` fields so the picker grid shows the preferred contact immediately.
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
- If `Contacts` is non-empty, call `AddContactAsync` for each contact with its `Order` (mirrors current Phase 4 wizard logic but iterated). The first (lowest `Order`) is the preferred contact — no separate "set primary" call needed.

### 4.5 Student guardian list: up to 3 contacts per row

#### DTO enrichment

`StudentGuardianViewDto` currently carries only the primary contact. Extend it to carry the top N contacts:
```csharp
public sealed record GuardianContactView(
    ContactChannel Channel,
    string Value,
    string? CountryCode,
    int Order);

public sealed record StudentGuardianViewDto(
    ...existing fields...,
    GuardianContactView[] Contacts = null!);
```

Use `GuardianContactView[]` instead of `IReadOnlyList<ContactDto>` to avoid leaking unneeded fields (`IsVerified`, `Label`, subscription state) into the read-only list.

#### Handler change

`ListGuardiansByStudentHandler` already batch-loads contacts. Modify the projection to keep up to 3 non-deleted contacts per guardian, ordered by `DisplayOrder` ascending then by `CreatedAt` ascending (so the preferred contact — lowest `DisplayOrder` — is first). The existing single-contact fields can be deprecated/removed or kept for backward compatibility; recommend removing them once `GuardianGrid` is updated.

#### Component change

`StudentGuardiansList` becomes a thin wrapper around `GuardianGrid` in `Display` mode:
```razor
<GuardianGrid Mode="GuardianGridMode.Display"
              Items="@_rows"
              RelationshipNameLookup="RelationshipNameLookup"
              OnEdit="OnEdit"
              OnRemove="OnRemove" />
```

The Contacts column renders each `GuardianContactView` as a stacked cell (channel on top, value below). **No per-contact role tick** (contacts have no Primary/CC role). A subtle "preferred" star marks the single first-by-`Order` contact:
```
<div class="contact-stack">
    <span class="contact-channel">📱 WhatsApp</span>
    <span class="contact-value">⭐ +233 0241234567</span>
</div>
```
The star (`FluentIcons.Star`, `title="Preferred contact"`) appears only on the first (lowest `Order`) contact. The country code is part of the value line, not a separate tick.

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
- **Display:** Name (with emergency badge), Relationship, Contact 1, Contact 2, Contact 3, **Primary tick** (checkmark when `Role == GuardianRole.Primary`; header "Primary"; muted — for CC), per-row Actions.

Internally uses `EntityGrid<GuardianGridRow>` so it inherits search, empty state, and selection behavior.

#### Migration path

1. Build `GuardianGrid`.
2. Replace the grid in `GuardianPickerDialog` with `GuardianGrid` in `Picker` mode.
3. Replace the grid in `StudentGuardiansList` with `GuardianGrid` in `Display` mode.
4. Remove or deprecate `GuardiansTab` (already marked deprecated).
5. Update `GuardianAssignmentList` (wizard) to use `GuardianGrid` if it also lists guardians; otherwise leave it.

### 4.7 Preferred-contact indicator (no per-contact role tick)

**Contacts have no Primary/CC role.** The Primary/CC role belongs to the guardian *link* (`StudentGuardian.Role`), surfaced via the **Primary tick column** in Display mode (a `FluentIcons.CheckmarkCircle` checkmark when `Role == GuardianRole.Primary`; header "Primary"; a muted — for CC) — never as a per-contact tick. Therefore the original "tick for primary or cc" on each contact column is **removed**.

The only per-contact indicator is a subtle **preferred** marker on the single highest-priority contact (the first by `DisplayOrder`) — rendered as a small `FluentIcons.Star` (or `Checkmark`) beside the contact value, `title="Preferred contact"`. The country code is shown as text within the value line (e.g. `+233 0241234567`), not as a tick.

```
📱 WhatsApp
⭐ +233 0241234567
```

CSS class `.contact-preferred` hosts the marker. The guardian link's Primary/CC *role* is shown by the dedicated **Primary tick column** (Display mode) — a checkmark when the link is the Primary guardian.

### 4.8 Title included in guardian displayed name

Guardians have an optional `TitleCodedValueId` (e.g., Mr., Mrs., Dr.). Today it is stored but not surfaced in lists or pills. This requirement adds the resolved title to every guardian name display.

#### Title resolution

- `GuardianGrid` loads the title coded-value dictionary once via `CodedValuesApiClient.GetChildrenByParentCodeAsync(CodedValueParent.Salutations.ToCode(), ...)` — the guardian title parent is **`Salutations`** (code `"SALUTS"`), NOT a hypothetical `Titles` member (verified in `CodedValueConstants.cs`).
- Store in `_titleNames: Dictionary<Guid, string>`.
- A helper formats the **combined guardian display string** the prompt specifies: `Title GuardianCombinedName (Relationship, FirstPrimaryContact)` — e.g. `"Mr. John Smith (Father, +233 0241234567)"`. This is the canonical single-line guardian display used by pills/chips and any tooltip/badge that shows a guardian in one line. It composes three parts:
  1. **Title** — resolved from `TitleCodedValueId` via `_titleNames` (e.g. `"Mr."`); omitted when null.
  2. **GuardianCombinedName** — `DisplayName` when present, else `$"{FirstName} {LastName}"`.
  3. **(Relationship, FirstPrimaryContact)** — subtitle part: the relationship display name (resolved from `RelationshipCodedValueId`) and the **FirstPrimaryContact** (the contact with the lowest `DisplayOrder` — i.e. the preferred contact — formatted as `[+CC] value`). Omit the relationship part when unknown (existing-picker picks); omit the contact part when the guardian has no contacts; omit the whole parenthetical when both are absent.
  ```csharp
  private string FormatGuardianName(GuardianGridRow g)
  {
      var title = g.TitleCodedValueId is { } id && _titleNames.TryGetValue(id, out var t)
          ? t : null;
      var name = string.IsNullOrWhiteSpace(g.DisplayName)
          ? $"{g.FirstName} {g.LastName}" : g.DisplayName;
      var label = string.IsNullOrWhiteSpace(title) ? name : $"{title} {name}";
      return label;
  }

  private string FormatGuardianDisplay(GuardianGridRow g)
  {
      var label = FormatGuardianName(g);
      var rel = g.RelationshipCodedValueId is { } rid
          && RelationshipNameLookup?.Invoke(rid) is { } rn && !string.IsNullOrWhiteSpace(rn)
          ? rn : null;
      var contact = FormatFirstPrimaryContact(g);
      var parts = new[] { rel, contact }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
      return parts.Length == 0 ? label : $"{label} ({string.Join(", ", parts)})";
  }
  ```
- **FirstPrimaryContact** = the contact with the lowest `DisplayOrder` (the preferred contact), formatted as `$"{CountryCode} {Value}"` when a country code is present, else `Value`. Returns null when the guardian has no contacts.
- If the title already appears inside `DisplayName`, do not prepend it again (some tenants may store the full display name). Detect by checking whether `DisplayName` starts with the resolved title, case-insensitive.

#### Surfaces updated (combined vs split display)

The combined `Title Name (Relationship, FirstPrimaryContact)` form is used wherever a guardian is shown on **one line** — picker pills/chips, tooltips, and any badge-style display. The **grid Name cell** shows only `Title Name` (title + combined name) because the grid has dedicated Relationship and Contact columns; rendering the full combined string in the Name cell would duplicate those columns. Concretely:

- **Grid Name cell (Picker + Display):** `FormatGuardianName(g)` → `"Mr. John Smith"`.
- **Picker pill label:** `FormatGuardianName(g)` → `"Mr. John Smith"`; **subtitle:** the `(Relationship, FirstPrimaryContact)` part from `FormatGuardianDisplay(g)`.
  - *New* guardians (relationship captured in the create form): subtitle = `(Father, +233 0241234567)`.
  - *Existing* picker picks (relationship not yet known): subtitle = `(+233 0241234567)` (FirstPrimaryContact only — relationship is set per-link after the picker returns).
- **Display-mode rows** (StudentGuardiansList, where relationship is known per link): the Name cell still shows `Title Name`; relationship and contacts live in their own columns. The combined form is available for tooltips on the Name cell.
- Anywhere else `GuardianDto` or `StudentGuardianViewDto` names are displayed should route through `GuardianGrid` after the migration.

#### DTO / handler changes (optional)

Alternative to client-side resolution: compute the formatted name server-side in `ListGuardiansHandler` and `ListGuardiansByStudentHandler` by batch-loading title coded values. This keeps the UI simpler but duplicates formatting logic. Recommendation: resolve client-side in `GuardianGrid` so the component is self-contained and reusable across contexts where only the `TitleCodedValueId` is available.

### 4.9 Contact ordering (domain change)

**Background:** The `Primary/CC` role is a guardian-*link* property (`StudentGuardian.Role`), not a contact property. Today `Contact` has a binary `IsPrimary` flag plus a `SetPrimaryContactAsync` endpoint, which conflates "preferred contact" with a "primary role". The user has clarified contacts should not carry a role; instead they need an **order of display / relevance** for prioritisation.

**Change:** Replace `Contact.IsPrimary` (bool) with `Contact.DisplayOrder` (int, non-nullable, default 0). Lower = higher priority; the contact with the smallest `DisplayOrder` is the "preferred" contact.

- `Contact` entity: replace `IsPrimary` with `int DisplayOrder`; `Create(...)` takes `displayOrder` instead of `isPrimary`; `SetPrimary(bool)` becomes `SetOrder(int)` (or `MoveTo(int)`).
- `ContactDto`: replace `IsPrimary` with `int DisplayOrder`.
- `AddContactRequest`: replace `IsPrimary` with `int DisplayOrder` (default 0; new contacts appended at the end get the next available order).
- `IContactsClient.SetPrimaryContactAsync` → rename/repurpose to `SetContactOrderAsync(Guid id, int order)` (or keep `SetPrimaryContactAsync` as a thin convenience that sets `DisplayOrder = 0` and shifts others). A full reorder endpoint (`ReorderContactsAsync(Guid ownerType, Guid ownerId, Guid[] orderedIds)`) is the cleanest for move-up/move-down UX.
- DB migration: add `DisplayOrder int NOT NULL DEFAULT 0`; backfill from `IsPrimary` (primary → 0, others → `ROW_NUMBER` over `CreatedAt`); drop the `IsPrimary` column.
- Handlers (`ListContactsHandler`, `ListGuardiansByStudentHandler`, `ListGuardiansHandler`): order by `DisplayOrder` ascending then `CreatedAt` ascending; the "primary contact" projection becomes the row with `MIN(DisplayOrder)`.

**Editor impact:** `ContactsEditor` loses the "Primary" checkbox and "Set as primary" button; gains move-up / move-down buttons (or drag-and-drop) that call the reorder endpoint. The first row is the preferred contact. `GuardianContactsEditor` (in-memory) mirrors this with `ContactModel.Order`.

**Migration risk:** `IsPrimary` is referenced across Core, API, Admin, tests, and the DB. This is the largest single change in the plan — sequence it first (see §11) so downstream grid/editor work depends on the new field.

---

## 5. Data Model / DTO Changes

| File | Change |
|------|--------|
| `Contact.cs` (Domain) | Replace `IsPrimary` with `int DisplayOrder`; `Create`/`SetPrimary` → `SetOrder`; add migration |
| `ContactDto.cs` | Replace `IsPrimary` with `int DisplayOrder` |
| `AddContactRequest` / `UpdateContactRequest` (Contracts) | Add `int DisplayOrder`; reorder support |
| `GuardianAssignment.cs` | Add optional `IReadOnlyList<ContactRequest>? Contacts` parameter |
| `GuardianDto.cs` | Keep existing primary-contact fields; no change required if `GuardianGrid` maps from DTO |
| `StudentGuardianViewDto.cs` | Replace primary-contact scalar fields with `GuardianContactView[] Contacts` (default empty array) |
| New `GuardianGridRow.cs` / `GuardianContactView.cs` | Add in the Admin component namespace or Core DTOs |

---

## 6. API / Client Changes

- **New/replaced endpoint:** `SetContactOrderAsync(Guid id, int order)` (and/or `ReorderContactsAsync(ContactOwnerType ownerType, Guid ownerId, Guid[] orderedIds)`) replaces `SetPrimaryContactAsync`. The old endpoint is removed or kept as a thin shim that sets `DisplayOrder = 0`.
- `AddContactAsync` accepts `DisplayOrder` on `AddContactRequest` (new contacts default to the next available order).
- Parent pages iterate `GuardianAssignment.Contacts` and call `AddContactAsync` for each with its `Order`.

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
| `ContactsEditor.razor` | Drop "Primary" checkbox + "Set as primary"; add move-up/move-down (or drag) calling the reorder endpoint; first row = preferred |
| `GuardiansTab.razor` | Remove or keep deprecated comment; do not extend |
| `GuardianFormFields.razor` | Keep for inline mode in long forms if still needed, but no longer used by picker |
| `EntityGrid.razor` / `.css` | Add header-wrap CSS and verify `MultiLine` propagates to headers |

---

## 8. CSS Changes

- `EntityGrid.razor.css`: header wrapping rules.
- `GuardianGrid.razor.css`: column templates for Picker/Display, contact-stack layout, preferred-star styling, emergency badge inline layout.
- `GuardianContactsEditor.razor.css`: reuse the layout of `ContactsEditor.razor.css` (or import/shared classes) without the page-card styling.
- `GuardianPickerDialog.razor.css`: widen `.picker-dialog` max-width if needed; the dialog width is primarily controlled by `DialogSize` but body CSS can add padding/max-width guards.

---

## 9. Test Plan

- Unit tests for `GuardianGrid` rendering (markup contains expected columns, no clipping CSS classes applied).
- Unit tests for `GuardianContactsEditor`:
  - Add contact updates `ContactsChanged`.
  - SMS/WhatsApp shows country-code dropdown.
  - Move-up/move-down reorders `ContactModel.Order`; first row is preferred.
- Unit tests for `ContactsEditor` reorder: move-up calls `SetContactOrderAsync` (or `ReorderContactsAsync`) with the right order sequence.
- Contact-ordering domain tests: `Contact.Create` assigns `DisplayOrder`; `SetOrder` shifts; handlers order by `DisplayOrder` then `CreatedAt`.
- Update `StudentDetailSectionsTests` if it still asserts the old 6-column layout.
- Update CQRS handler tests for `ListGuardiansByStudentHandler` to assert up to 3 contacts are projected.
- Run the full suite with `-p:BuildProjectReferences=false` to avoid file-lock issues.

---

## 10. Open Questions / Decisions Needed

1. **Dialog size:** `Large` (640 px typical) or `Panel` (full-height side panel)? Recommendation: `Large` for the picker; `Panel` only if the in-memory contact editor feels cramped.
2. **New-guardian contact requirement:** Require at least one contact, or allow name-only and default to no contacts? Recommendation: require at least one contact because the user explicitly replaced the single-contact form with a contact editor.
3. **Tick meaning (resolved):** Contacts have **no Primary/CC role** — that role belongs to the guardian *link* (`StudentGuardian.Role`), shown via the **Primary tick column** (checkmark when `Role == GuardianRole.Primary`; header "Primary"). There is therefore **no per-contact role tick**. The only per-contact marker is a "preferred" star on the single highest-priority contact (first by `DisplayOrder`). See §4.7 and §4.9.
4. **Relationship/Role in picker create panel:** Keep Relationship dropdown (it is per-link) and add Role (Primary/CC)? Current `GuardianAssignmentModel` already has these; ensure the new editor still captures them.
5. **Backward compatibility of `StudentGuardianViewDto`:** The DTO is a positional record. Adding a new array parameter at the end with a default empty array keeps existing callers compiling, but verify all construction sites.
6. **Title source (resolved):** The guardian title parent is `CodedValueParent.Salutations` (code `"SALUTS"`) — verified in `src/SchoolCollab.Admin.Shared/Constants/CodedValueConstants.cs`. There is no `Titles` member. Remaining sub-question: whether the title text already includes a trailing period (`"Mr."` vs `"Mr"`). The formatter must not double the period if the coded value already contains it.
7. **`IsPrimary` vs `DisplayOrder` (decision):** Replace `Contact.IsPrimary` (bool) with `Contact.DisplayOrder` (int) — recommended, matches "contacts have no role" + "order of relevance". Alternative: keep `IsPrimary` and add `DisplayOrder` alongside (less migration, but keeps the redundant binary role on contacts). Recommendation: **replace** (see §4.9). This is the largest change (Core + DB + API + tests + `ContactsEditor`) — confirm before migration.

---

## 11. Suggested Implementation Order

1. **Contact ordering domain change (§4.9)** — add `DisplayOrder` to `Contact`/DTOs/contracts, DB migration, reorder endpoint, update `ContactsEditor`. Sequence first; downstream grid/editor depend on it.
2. **Header wrapping CSS** — low risk, global improvement.
3. **`GuardianGrid` shell** — build the component with Picker and Display modes using mock data; include title-resolution helper and formatted name display.
4. **StudentGuardiansList migration** — switch to `GuardianGrid` Display mode; verify 3-contact (ordered) rendering.
5. **DTO/handler enrichment** — add `GuardianContactView[]` to `StudentGuardianViewDto`; update `ListGuardiansByStudentHandler` (order by `DisplayOrder`).
6. **`GuardianContactsEditor` in-memory component** — build and unit-test independently.
7. **Picker migration** — swap grid to `GuardianGrid`, swap New panel to `GuardianContactsEditor`, extend `GuardianAssignment`, wire parent pages to create multiple contacts with order.
8. **Dialog sizing + polish** — bump sizes, tune column widths, add preferred-contact star, verify title appears in names and pills.
9. **Full test pass + cleanup** — remove `GuardiansTab`, consolidate CSS.

---

## 12. Risks

- **Scope creep:** items 4–7 together are large. Consider splitting into two deliverables: (A) grid unification + 3-contact display, (B) in-memory contact editor + picker migration.
- **`IsPrimary` → `DisplayOrder` migration (highest risk):** Replacing the binary `Contact.IsPrimary` with an `int DisplayOrder` touches the domain entity, DTOs, contracts, API endpoint, DB migration, `ContactsEditor`, handlers, and all contact tests. A bug here breaks contact display across students AND guardians. Mitigate by sequencing it first (§11.1), writing a reversible EF migration with `IsPrimary`→`DisplayOrder` backfill, and keeping `SetPrimaryContactAsync` as a thin shim during transition. Confirm the decision (§10.7) before starting.
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

**Domain / contracts / API (§4.9 ordering change):**
- `src/Students/SchoolCollab.Students.Core/Domain/Contact.cs` — replace `IsPrimary` with `int DisplayOrder`; `Create`/`SetOrder`
- `src/Students/SchoolCollab.Students.Core/DTOs/ContactDto.cs` — `DisplayOrder`
- `src/Students/SchoolCollab.Students.Core/Contracts/IContactsClient.cs` — `SetContactOrderAsync` / `ReorderContactsAsync`; `AddContactRequest`/`UpdateContactRequest` `DisplayOrder`
- `src/Students/SchoolCollab.Students.Api/Endpoints/` — contact routes (rename/repurpose set-primary → order; add reorder)
- `src/Students/SchoolCollab.Students.Core/CQRS/Contacts/` — handlers (order by `DisplayOrder`)
- EF migration (add `DisplayOrder`, backfill from `IsPrimary`, drop `IsPrimary`)

**Admin UI:**
- `src/SchoolCollab.Admin.Shared/Components/ContactsEditor.razor` — drop Primary checkbox/Set-primary; add move-up/move-down
- `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardianPickerDialog.razor` + `.css`
- `src/Students/SchoolCollab.Students.Admin/Components/Students/StudentGuardiansList.razor` + `.css`
- `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/Edit.razor`
- `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/Detail.razor`
- `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/GradeLevels/GuardianAssignment.cs`
- `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/GradeLevels/GradeLevelWizard.razor`
- `src/Students/SchoolCollab.Students.Core/DTOs/StudentGuardianViewDto.cs`
- `src/Students/SchoolCollab.Students.Core/CQRS/Guardians/Queries/ListGuardiansByStudent/ListGuardiansByStudentHandler.cs`
- `src/Students/SchoolCollab.Students.Core/CQRS/Guardians/Queries/ListGuardians/ListGuardiansHandler.cs`
- `src/SchoolCollab.Admin.Shared/Components/EntityGrid.razor.css`

**Tests:**
- `tests/SchoolCollab.Admin.Tests.Unit/StudentDetailSectionsTests.cs`
- `tests/SchoolCollab.Students.Tests.Unit/` — contact CQRS / ordering tests (`GuardianContactsCqrsTests.cs` and related)
- `tests/SchoolCollab.Students.Tests.Integration/` — contact reorder endpoint tests
