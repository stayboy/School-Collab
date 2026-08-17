# Plan: Extract Guardian Section from StudentFormFields

## Status
Approved single-component design. Supersedes any prior multi-component proposals.

## Goal
Extract the guardian display and add-fields UI from `StudentFormFields.razor` into **one** dedicated component, `GuardianSection`, that is designed to be hosted **inside** the existing shared `FormRow` component.

`GuardianSection` owns all guardian state. `StudentFormFields` becomes a pure student profile-form shell that wraps `GuardianSection` in a `FormRow`.

## Component Tree

```
StudentFormFields (owns: Model, Mode, StudentId, ShowGuardians, form actions)
└── FormRow (Label="Guardians", only rendered when ShowGuardians is true)
    └── GuardianSection (NEW — owns ALL guardian state)
        └── StudentGuardiansList (existing, Linked mode only)
```

## Component Responsibilities

### `StudentFormFields`
- Keeps `Model`, `Mode`, `StudentId`, `ShowGuardians`, `LinkedItems`, `OnManageGuardians`, `OnRemoveGuardian`.
- Renders the `FormRow` for Guardians only when `ShowGuardians` is true.
- Passes `Model.GuardianLinks` by reference to `GuardianSection`.
- No longer owns any guardian state, code-behind, or CSS for guardians.

### `FormRow`
- Unchanged shared component.
- Provides the left-side "Guardians" label and right-side content area.
- `GuardianSection` is supplied as `ChildContent`.

### `GuardianSection` (NEW)

**File:** `src/Students/SchoolCollab.Students.Application/Components/Students/GuardianSection.razor`  
**Styles:** `src/Students/SchoolCollab.Students.Application/Components/Students/GuardianSection.razor.css`

**Parameters**

| Parameter | Type | Purpose |
|-----------|------|---------|
| `GuardianLinks` | `List<GuardianAssignment>` | Same reference as `Model.GuardianLinks`; mutated in place. |
| `Mode` | `StudentFormFieldsMode` | `Inline` (create/edit dialog) or `Linked` (page-side). |
| `StudentId` | `Guid?` | Excludes current student from typeahead results. |
| `LinkedItems` | `StudentGuardianViewDto[]?` | Existing linked guardians for Linked mode. |
| `OnManageGuardians` | `EventCallback` | Linked mode Manage button. |
| `OnRemoveGuardian` | `EventCallback<StudentGuardianViewDto>` | Linked mode per-row remove. |

**Injections**
- `StudentsApiClient`
- `CodedValuesApiClient`
- `ILogger<GuardianSection>`

**State Owned** (all moved from `StudentFormFields`)
- Loading/error: `_loadingGuardians`, `_guardianError`.
- Lookup caches: `_relNames`, `_salutations`.
- Panel/edit: `_panelMode`, `_editingIndex`, `_editRelationshipId`, `_editRole`, `_editTitleId`, `_editFirstName`, `_editLastName`, `_editContacts`.
- Add-row/typeahead: `_newRelId`, `_newLastName`, `_newFirstName`, `_adding`, `_relationshipOptions`, `_inExistingMode`, `_searchMode`, `_existingGuardianId`, `_pickedRelationshipId`, `_pickedTitleId`, `_searchCts`, `_typeahead`, `_selectedRelOption`.
- Internal types moved with the component: `RelationshipOptionKind`, `RelationshipOption`, `GuardianSearchRow`, `GuardianSearchRowComparer`, `ExistingGuardianSentinel`, `WardLabel`.

**Lifecycle**
- `OnInitializedAsync`: load relationship options + rel/salutation name lookups.
- `DisposeAsync`: cancel the typeahead search CTS.

**Rendering**
- `Linked` mode: count header + Manage button + `StudentGuardiansList` + error text.
- `Inline` mode: add row (relationship dropdown + name/typeahead + Add), card list, edit panel.

## Data Flow

```
StudentFormFields
  ├─ [Parameter] GuardianLinks (same ref as Model.GuardianLinks) ──→ GuardianSection
  ├─ [Parameter] Mode ──→ GuardianSection
  ├─ [Parameter] StudentId ──→ GuardianSection
  ├─ [Parameter] LinkedItems ──→ GuardianSection
  ├─ [Parameter] OnManageGuardians ──→ GuardianSection
  └─ [Parameter] OnRemoveGuardian ──→ GuardianSection
```

`GuardianSection` mutates `GuardianLinks` in place (Add/RemoveAt/index assignment). Because it is the same `List<GuardianAssignment>` reference held by `Model.GuardianLinks`, the parent's save flow sees every add/edit/remove without additional plumbing.

## Hosting in `FormRow`

In `StudentFormFields.razor`, the guardian block becomes:

```razor
@if (ShowGuardians)
{
    <FormRow Label="Guardians" LabelWidth="120px" Gap="16px">
        <GuardianSection GuardianLinks="Model.GuardianLinks"
                         Mode="Mode"
                         StudentId="StudentId"
                         LinkedItems="LinkedItems"
                         OnManageGuardians="OnManageGuardians"
                         OnRemoveGuardian="OnRemoveGuardian" />
    </FormRow>
}
```

`GuardianSection` does **not** render its own `FormRow`. It is the content of the `FormRow`.

## Implementation Order

1. **Move `StudentFormFieldsMode` enum** to `StudentFormFieldsMode.cs` so both `StudentFormFields` and `GuardianSection` can reference it without nesting. Update `StudentFormFields.razor` and `Edit.razor`.
2. **Create `GuardianSection.razor` + `.razor.css`** — move all guardian markup, code-behind, internal types, and CSS from `StudentFormFields` in one pass.
3. **Slim `StudentFormFields.razor`** — replace the existing `@if (ShowGuardians)` guardian block with a `FormRow` containing `<GuardianSection ... />`; delete all moved guardian code-behind.
4. **Clean up `StudentFormFields.razor.css`** — remove all guardian CSS classes; keep only profile-form styles (e.g., `.muted`).
5. **Update tests** — retarget `StudentFormFieldsGuardianTypeaheadTests.cs` from `StudentFormFields.*` to `GuardianSection.*`.
6. **Build and test** — verify build, run retargeted tests, and smoke-test Inline + Linked modes.

## What Stays in `StudentFormFields`

- Student profile fields (name, DOB/Gender, student number, status, contacts).
- Validation and `SubmitAsync` / `ValidateAsync`.
- `ShowGuardians` gating of the `FormRow`.
- Pass-through parameters (`Mode`, `StudentId`, `LinkedItems`, `OnManageGuardians`, `OnRemoveGuardian`).
- Student's own `ContactsEditor` / `OnDraftContactsChanged` logic.
- DOB bridge code.

## Risks / Notes

- **`StudentFormFieldsMode` relocation** touches `Edit.razor` (one external reference); `StudentFormFields.razor` internal references also update.
- **Internal types move** to `GuardianSection`; they remain `internal` so `InternalsVisibleTo` keeps tests compiling.
- **`DisposeAsync`** (search CTS cancellation) moves entirely to `GuardianSection`. `StudentFormFields` keeps `IAsyncDisposable` only if needed for other resources; otherwise remove it.
- **CSS split**: all guardian classes move to `GuardianSection.razor.css`. Verify `.muted` and any other shared class is not removed from `StudentFormFields.razor.css` if still used by non-guardian markup.
- **No behavior change**: this is a pure structural extraction. Logic, ordering, and user-visible behavior remain identical.

## Verification Checklist

- [ ] `dotnet build` on `SchoolCollab.Students.Application` → 0 errors.
- [ ] Retargeted `StudentFormFieldsGuardianTypeaheadTests` → 11/11 pass.
- [ ] `StudentEditDialog` (Inline mode): add row, relationship dropdown divider, typeahead, card list, edit panel, Save/Cancel all render and function identically.
- [ ] `Edit.razor` (Linked mode): count header, Manage button, `StudentGuardiansList`, per-row Remove all work.
- [ ] `StudentFormFields.razor` line count drops significantly (target ~1000 lines or fewer).
- [ ] No duplicated CSS classes between `StudentFormFields.razor.css` and `GuardianSection.razor.css`.

## Decision

Implement the single `GuardianSection` component hosted inside `FormRow`. Decomposition into smaller leaves (add row, card list, edit panel) is deferred until after this extraction is merged and the boundary is proven stable.
