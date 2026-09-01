---
name: dropdown-ui
description: |
  SchoolCollab dropdown & select conventions. Use when creating or editing any
  Blazor dropdown, select, combo box, or enum picker in a form or dialog.
  Triggers: "dropdown", "FluentSelect", "select", "combo box", "DropdownForEnum",
  "DropdownComponent", "CodedValueDropdown", "enum picker", "bind-SelectedValue",
  "OptionText", "OptionValue", "FieldWidth", "W9", "placeholder", "form field
  dropdown", "division selector", "status filter".
---

# Dropdown & select conventions (SchoolCollab)

This is the goto guidance for any dropdown-style form control in this repo.
It distills the in-source contracts of the three shared dropdown wrappers in
`src/SchoolCollab.Admin.Shared/Components/` — read those files' doc comments
for the full rationale (async-load pitfalls, double-fire diagnostics).

---

## 1. Pick the right wrapper — don't hand-roll a raw `FluentSelect`

| Situation | Use | Notes |
| --- | --- | --- |
| Options are the members of an enum | `DropdownForEnum` | Zero boilerplate: `TEnum` + `@bind-SelectedValue`. Option text is the enum member name. |
| Options are Coded Values (gender, relationship, salutation, …) | `CodedValueDropdown` | Loads its own items from the Settings service via `CodedValueParent`; handles async load, error states, and the FluentUI #1533 pre-selection workaround. |
| Parent already holds a list of domain DTOs | `DropdownComponent` | Generic key-value dropdown: parent passes `Items` + `OptionText`/`OptionValue`, binds to the primitive key (`Guid?`, `string`, …). |
| None of the above (e.g. `Multiple` selection, heavily templated options) | raw `FluentSelect` | Document why in a comment next to the usage. |

A raw `FluentSelect` with inline `<FluentOption>` children is a smell: it
repeats the `TOption`/`OptionText`/`OptionValue` boilerplate the wrappers
remove and misses the shared `FieldWidth` sizing contract.

## 2. Binding conventions

```razor
@* Enum — DropdownForEnum *@
<DropdownForEnum TEnum="AcademicYearDivision"
                 @bind-SelectedValue="_division"
                 @bind-SelectedValue:after="OnDivisionChanged"
                 Width="FieldWidth.W9" />

@* Domain DTO list — DropdownComponent (bind the KEY, not the object) *@
<DropdownComponent TItem="PeriodDto" TValue="string"
                   Items="@AcademicYears"
                   @bind-SelectedValue="Model.ParentPeriodIdText"
                   OptionText="@(ay => ay.Name)"
                   OptionValue="@(ay => ay.Id.ToString())"
                   Placeholder="— Select academic year —"
                   Width="FieldWidth.W9" />
```

- **Bind to the primitive key** (`Guid?`, `string`), never the full option
  object. The parent's domain code reads the key directly; no mirrored
  `_selectedXxx` field.
- **Side-effects go on `:after`** (`@bind-SelectedValue:after="Handler"`).
  Never pass `SelectedValueChanged="…"` directly — that overrides the binder
  and the bound field stops updating (documented on `DropdownComponent`).
- **`:after` handlers are sync `Action`s.** Fire-and-forget async work with
  `_ = SomeEventCallback.InvokeAsync(…)` unless the wrapper's contract
  provides an async callback.
- **String-keyed models:** if a form model stores the selection as a string
  (e.g. `DivisionSelect`), adapt to the enum in a component-local field —
  re-sync in `OnParametersSet` (parent-driven changes must reach the
  dropdown) and write the model in the `:after` handler. See
  `PeriodFormFields.razor` (`_divisionValue`) for the worked example.

## 3. Width — use the `FieldWidth` ladder, not ad-hoc styles

Set the strongly-typed `Width` parameter (`FieldWidth.W1`–`W9`) on the
wrappers instead of inline `Style="width: …"`. `FieldWidth.W9` is the
"fill the FormRow input cell" value (`width:100%; min-width:0; flex:1 1 0`)
and is the default choice for fields inside a `<FormRow>`. The wrappers emit
an inline style on the underlying `FluentSelect` so the width wins over their
scoped CSS — see `FieldWidth.cs` for why this is inline-style, not a CSS
class. Keep pixel values in sync with the `w-1`…`w-9` classes in
`src/SchoolCollab.Admin/wwwroot/css/app.css`.

## 4. Refresh & lookups

- Reassign the `Items` field (`.ToArray()` after mutation) and Blazor's
  parameter-change detection updates the dropdown. For in-place `List<T>`
  mutations call `DropdownComponent.Refresh()`.
- Need the full DTO for a known key? Use `DropdownComponent.TryFindItem(key,
  out var item)` instead of a LINQ lookup in the parent.

## 5. Known pitfalls (why the wrappers exist)

- **FluentUI #1533:** async-loaded options need a pre-selection workaround —
  do not bypass `CodedValueDropdown`'s load-key contract with a hand-rolled
  async `FluentSelect`.
- **Double-fire on async reload:** a cleared/missing option can fire a spurious
  `SelectedOptionChanged(null)`. `CodedValueDropdown` guards this with
  `ComputeLoadKey`/`ResolveSelection`; `DropdownComponent` settles after one
  clear because it has no async load window. Don't add async loading to
  `DropdownComponent` without porting those guards (see
  `StreamDropdownDoubleFireDiagnosticTests`).
