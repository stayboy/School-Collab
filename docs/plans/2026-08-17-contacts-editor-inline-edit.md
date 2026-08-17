# Plan: Inline Edit for ContactsEditor Rows

**Date:** 2026-08-17  
**Status:** SPEC — no implementation yet  
**Scope:** `src/SchoolCollab.Admin.Shared/Components/ContactsEditor.razor` + `.razor.css` + unit tests  
**Related work:** This plan is a follow-up to the guardian-section extraction; it does NOT change `StudentFormFields` / `GuardianSection`.

---

## 1. Goal

Add an inline **Edit** action to every contact row in the shared `ContactsEditor`. Today the component supports add / remove / reorder / verify / subscribe, but the only way to change a channel, value, label, or country code is to delete the contact and re-add it. That loses order, verified state, and persisted id, and it is poor UX for long lists.

The edit must work in **both** `ContactsEditor` modes:

- **Live** (owner already exists): call `IContactsClient.UpdateContactAsync` immediately.
- **Buffered** (owner not yet persisted, e.g. guardian creation in `GuardianPickerDialog`): mutate the in-memory `ContactModel` in place and raise `ContactsChanged`.

---

## 2. Why edit was originally left out

The first `ContactsEditor` implementation (plan `2026-07-17-country-codes-for-contacts.md`) intentionally scoped contact mutation to add / remove / reorder / verify / subscribe. The rationale was that a contact is "just channel + value + label", so delete-and-re-add was acceptable. That assumption does not hold once:

- order / verified status matter (re-add appends at the tail and clears verified),
- SMS/WhatsApp country codes are involved (re-typing is error-prone),
- lists become long (common for guardians),
- the row carries a persisted id that should survive (Live mode).

Backend support already exists (`UpdateContactAsync` + `UpdateContactHandler`) — the UI was the missing piece.

---

## 3. Current component behavior (baseline)

- `ContactRow` (private render-only record): `Key`, `Channel`, `Value`, `Label`, `CountryCode`, `IsPreferred`, `IsVerified`, `Order`.
- Row actions today:
  - Live only: Verify, Subscribe, Mark preferred (implicit via reorder).
  - Both modes: Move up, Move down, Remove.
- Add row: channel dropdown + conditional country-code dropdown + value + label + Add.
- `ContactModel` (Buffered) and `ContactDto` (Live) project into the shared `ContactRow` for rendering.

---

## 4. Design

### 4.1 Per-row edit affordance

Add a lightweight **Edit** icon button to `.contact-actions`, left of the existing action buttons:

```razor
<FluentButton Appearance="Appearance.Lightweight"
              Title="Edit contact"
              IconStart="@FluentIcons.Edit"
              OnClick="@(() => StartEdit(c.Key))" />
```

Clicking it puts **only that row** into edit mode. All other rows stay read-only.

### 4.2 Inline edit form

When a row is being edited, replace the static row markup with a compact inline form inside the same `<li class="contact-item ...">`:

- **Channel dropdown** (`DropdownForEnum<ContactChannel>`) bound to the draft channel.
- **Country-code dropdown** (`CodedValueDropdown Parent="CountryCallingCodes"`) shown only when the draft channel is `SMS` or `WhatsApp`.
- **Value field** (`FluentTextField`) with placeholder driven by channel.
- **Label field** (`FluentTextField`) optional.
- **Save** button (Accent, small) — disabled while the value is blank or an API call is in flight.
- **Cancel** button (Outline, small) — reverts to the original values and exits edit mode.

The inline form should visually sit inside the list item so the layout does not jump dramatically. Use a second inner flex row (or stack on very narrow widths) for the edit fields.

### 4.3 State machine

Add private state:

```csharp
private Guid? _editingContactKey;          // null = no row editing
private ContactChannel _editChannel;
private Guid? _editCountryCodeId;
private string _editValue = string.Empty;
private string _editLabel = string.Empty;
private string? _editCountryCode;          // resolved string, or null for email
private bool _savingEdit;
```

On `StartEdit(Guid key)`:

1. Look up the row in `RowSource`.
2. Snapshot current values into the edit fields.
3. If the channel is SMS/WhatsApp, load country-code options and pre-select the row's `CountryCode` (matching `_newCountryCodeId` resolution logic).
4. Set `_editingContactKey = key`.

On `SaveEditAsync()`:

- **Live mode:**
  1. Resolve `_editCountryCodeId` to the dial-code string if applicable.
  2. Call `Api.UpdateContactAsync(key, new UpdateContactRequest(...))`.
  3. On success, call `LoadAsync()` to refresh the list (this resets `_editingContactKey`).
  4. On failure, surface error in `_error` and keep edit mode open.
- **Buffered mode:**
  1. Find the `ContactModel` by `TempId`.
  2. Mutate `Channel`, `Value`, `Label`, `CountryCode` in place.
  3. If channel changed to Email, clear `CountryCode`.
  4. Call `NotifyChangedAsync()`.
  5. Clear `_editingContactKey`.

On `CancelEdit()`:

1. Clear `_editingContactKey`.
2. Discard draft values (no mutation).

### 4.4 Country-code handling during edit

Reuse the same helper pattern as the add row:

- `_editCountryCodeId` is bound to the dropdown.
- When channel changes to SMS/WhatsApp, call `LoadCountryCodesAsync()` (or a variant scoped to the edit row) and default to the existing `CountryCode` if known, else Ghana (`+233`).
- When channel changes to Email, clear `_editCountryCodeId` and `_editCountryCode`.
- On save, resolve `_editCountryCodeId` from `_countryCodeOptions` to a string.

A single shared `_countryCodeOptions` cache is sufficient (same options for add and edit), but the edit row needs its own selected-id field.

### 4.5 Validation

Lightweight, client-side only:

- Value must be non-whitespace. Disable Save when `string.IsNullOrWhiteSpace(_editValue)`.
- For SMS/WhatsApp, a missing country code is allowed (backward-compatible), but the UI pre-selects one so it is unlikely.
- Server-side validation errors from `UpdateContactAsync` are surfaced in `_error` and the row stays in edit mode.

### 4.6 Read-only row during someone else's edit

Only one row can be edited at a time. Other rows render normally; the row under edit expands to show the inline form. The list remains sortable/reorderable, but the editing row should probably disable Move up/down while editing (not critical; can keep enabled and let Save use the current order).

---

## 5. UI/UX details

### 5.1 Button placement

The Edit button should be the **first** action in `.contact-actions` (leftmost), so the common read → edit flow is natural. Existing order becomes:

```
[Edit] [Verify] [Up] [Down] [Remove]
```

(Verify is Live-only and only shown when unverified.)

### 5.2 Visual treatment of editing row

- Keep the `.contact-item` container.
- The static content is replaced by the inline edit form.
- Slightly stronger border / background can mark the editing row, but the existing `.contact-item--preferred` style already tints the preferred row — avoid collision. A simple `.contact-item--editing` modifier with a subtle neutral border change is enough.
- Save/Cancel buttons sit at the right end of the inline form row.

### 5.3 Responsive behavior

- Wide row: channel + country code + value + label + Save/Cancel all on one line.
- Medium/narrow: channel/country code group on one line, value/label/Save/Cancel on the next line — mirroring the add-row grouping fix just applied.

---

## 6. Files to change

- `src/SchoolCollab.Admin.Shared/Components/ContactsEditor.razor`
  - Add Edit button per row.
  - Add inline edit markup branch.
  - Add `StartEdit`, `SaveEditAsync`, `CancelEdit`, plus edit-specific country-code helpers.
- `src/SchoolCollab.Admin.Shared/Components/ContactsEditor.razor.css`
  - `.contact-item--editing` modifier.
  - Inline edit row layout classes.
- `tests/SchoolCollab.Admin.Tests.Unit/ContactsEditorTests.cs`
  - Live-mode edit test: click Edit, change value, click Save, assert `UpdateContactAsync` called with new value.
  - Buffered-mode edit test: edit mutates the in-memory list and raises `ContactsChanged`.
  - Cancel test: changes are discarded.
  - Channel-swap test: changing SMS to Email clears country code; changing Email to SMS shows dropdown.

No server-side changes are required (`UpdateContact` command/handler/API already exist).

---

## 7. Test plan

### 7.1 Unit / bUnit tests

1. **Live mode edit happy path**
   - Render with one contact.
   - Click Edit button.
   - Change value.
   - Click Save.
   - Assert `IContactsClient.UpdateContactAsync` was called with the correct id and new value.
   - Assert `LoadAsync` refreshes the list and edit mode closes.

2. **Live mode edit failure keeps edit open**
   - Fake `UpdateContactAsync` throws.
   - Assert error bar appears.
   - Assert inline edit form is still rendered.

3. **Buffered mode edit mutates list**
   - Render Buffered with one contact.
   - Click Edit, change label + value.
   - Click Save.
   - Assert the in-memory `ContactModel` has new values.
   - Assert `ContactsChanged` was invoked.
   - Assert no API call happened.

4. **Cancel discards edits**
   - Click Edit, change value.
   - Click Cancel.
   - Assert the read-only row shows the original value.
   - Assert `UpdateContactAsync` was NOT called (Live) and `ContactsChanged` was NOT raised (Buffered).

5. **Channel swap clears / loads country code**
   - Edit an SMS contact; country-code dropdown is present and pre-selected.
   - Change channel to Email; dropdown disappears, country code is null on save.
   - Change back to WhatsApp; dropdown reappears with default selection.

6. **Edit disabled states**
   - Save disabled when value is blank.
   - Save disabled while `_savingEdit` is true.

### 7.2 Build / smoke

- `dotnet build src/SchoolCollab.Admin.Shared/SchoolCollab.Admin.Shared.csproj`
- `dotnet test tests/SchoolCollab.Admin.Tests.Unit/SchoolCollab.Admin.Tests.Unit.csproj --filter "FullyQualifiedName~ContactsEditorTests"`
- Manual smoke in:
  - `StudentEditDialog` (Buffered, create-time)
  - `Edit.razor` → Direct contact (Live)
  - `GuardianDetail.razor` (Live, guardian contacts)

---

## 8. Implementation order

1. Add private edit state and `StartEdit` / `CancelEdit` to `ContactsEditor.razor`.
2. Add inline edit markup branch inside the `@foreach` (reusing add-row helpers).
3. Implement `SaveEditAsync` for Live and Buffered modes.
4. Add country-code resolution for the edit row.
5. Add CSS for the editing row and inline form layout.
6. Add unit tests.
7. Build + test pass.

---

## 9. Open questions / decisions

1. **Should the preferred / verified badges be editable?**
   - Preferred is controlled by order (move up/down), so no separate edit field.
   - Verified is toggled by the Verify button; no edit field needed.
   - **Decision:** edit only changes channel, value, label, and country code.

2. **Should multiple rows be editable at once?**
   - **Decision:** no. Single-row edit keeps the state machine simple and matches common list-edit UX. Cancel/Save apply to one row at a time.

3. **What happens if the user reorders a row that is being edited?**
   - The edit form holds the row by key, so reordering does not lose the edit context. The displayed row moves to its new position while still in edit mode. Acceptable.

4. **Should we reuse the add-row markup for editing?**
   - The add row and edit row are visually similar but edit needs Save/Cancel and must bind to existing values. Extracting a small private render fragment (`RenderContactEditorFields`) for channel/country/value/label could reduce duplication. **Decision:** extract a shared fragment if it saves >20 lines; otherwise duplicate the small amount of markup to keep the change reviewable.

5. **Icon for edit button:**
   - Use `FluentIcons.Edit` (established repo constant). Verify it exists in the legacy shorthand set or use the fully-qualified `Icons.Regular.Size16.Edit` if needed (see `fluentui-icons-in-school-collab` project skill).
