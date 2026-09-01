# Dropdown wrapper adoption (DropdownForEnum / DropdownComponent / CodedValueDropdown)

**Date:** 2026-09-01 · **Area:** Blazor form dropdowns (Periods first, then repo-wide)

## Finding

The shared dropdown wrappers in `SchoolCollab.Admin.Shared/Components/`
(`DropdownForEnum`, `DropdownComponent`, `CodedValueDropdown`) are thoroughly
documented in-source but were not recorded as adoption rules anywhere in
`.github/` — so forms kept hand-rolling raw `FluentSelect`s with inline
`<FluentOption>` children, repeating `TOption`/`OptionText`/`OptionValue`
boilerplate (~25+ sites repo-wide) and bypassing the `FieldWidth` width ladder.

## Decision

- **Division selector** (`PeriodFormFields.razor`): raw `FluentSelect` over the
  string `DivisionSelect` replaced with `DropdownForEnum` bound directly to
  `AcademicYearDivision`. The consuming pages' string-based contract
  (`IPeriodFormModel.DivisionSelect`) is preserved via a component-local
  `_divisionValue` adapter: re-synced from the model in `OnParametersSet`
  (parent-driven changes, e.g. Edit's locked division), written back in a sync
  `:after` handler (`:after` binders take `Action`, not `Func<Task>`; async
  callbacks are fire-and-forget).
- **Parent academic year picker** (`PeriodFormFields.razor`): raw
  `FluentSelect` + `FluentOption` loop replaced with `DropdownComponent`
  (`TItem="PeriodDto"`, string key, `Width="FieldWidth.W9"`).
- **Sub-period type selects** (`PeriodSubPeriodsEditor.razor`, cell + inline
  add): raw `FluentSelect` replaced with `DropdownComponent` over a static
  `["Term", "Semester"]` option surface.
- **Pattern recorded** for future adoption: new skill
  `.github/skills/dropdown-ui/SKILL.md` (decision table, binding conventions,
  `FieldWidth` width rule, refresh/`TryFindItem`, FluentUI #1533 and
  double-fire pitfalls), wired into the `AGENTS.md` specialty table, with a
  one-paragraph pointer added to `.github/copilot/rules/blazor-components.md`.

## Why wrappers (not raw `FluentSelect`)

- Uniform key-value binding: parents store the primitive key, no mirrored
  selected-object fields.
- Centralized FluentUI pitfalls (#1533 async pre-selection, spurious
  `SelectedOptionChanged(null)` on reload).
- Strongly-typed `FieldWidth` sizing consistent with the W1–W9 ladder.

## Implementation steps

1. Converted the three Periods sites listed above (0 build errors;
   `SchoolCollab.Students.Application` builds clean).
2. Authored `.github/skills/dropdown-ui/SKILL.md`; added the AGENTS.md table
   row and the `blazor-components.md` rule bullet.
3. Remaining raw `FluentSelect` sites (Assignments forms, DevTenantSwitcher,
   ThemeSwitcher, `GuardiansTab`, `TeacherEditDialog`, `GradeTopicsDialog`,
   `TopicCreateDialog`, etc.) are candidates for follow-up conversion; the
   skill's decision table covers each case.
