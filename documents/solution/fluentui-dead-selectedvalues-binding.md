# FluentUI dead `@bind-SelectedValues` binding — a repo-wide bug class

> **Status:** finding filed 2026-10-01 from round `assignment-authoring-r1` (P1-b).
> One site is **fixed** (Assignments authoring); **five remain** in the Students
> admin and need their own round. This doc is the durable record — the round doc
> that found it is ephemeral (`documents/rounds/README.md`).

## Problem

Blazor components in this repo bind a **parameter that does not exist** on the
FluentUI list components:

```razor
<FluentListbox Items="@_allTopics"
               @bind-SelectedValues="_selectedTopicIds"   @* ← no such parameter *@
               OptionText="@(t => t.Name)"
               OptionValue="@(t => t.Id.ToString())"
               Multiple="true" />
```

## Evidence

- `Microsoft.FluentUI.AspNetCore.Components` **4.14.2** (`Directory.Packages.props`):
  `FluentListbox<T>` exposes `SelectedOptions` / `SelectedOptionsChanged`. Assembly
  reflection over the whole FluentUI assembly: `anySelectedValuesProperty=False`.
- Because the name is unknown, Blazor routes both the generated `SelectedValues`
  and `SelectedValuesChanged` parameters into the component's catch-all
  `AdditionalAttributes`. The rendered element therefore emits a stray HTML
  attribute whose value is the raw collection:
  `selectedvalues="System.Collections.Generic.HashSet\`1[System.Guid]"`.
- The bUnit probe on the real component confirmed the stray attribute, and that
  no `fluent-listbox` remained after the Assignments fix.

## Impact

The binding is **write-only decoration**: a click never reaches the bound field,
and the bound field never highlights anything. Concretely, at the fixed site it
meant `Create`-mode By-Group could never pass `ValidateForSave`
("Select at least one activity group.") — i.e. an author could not create a
group-targeted assignment at all, a regression introduced when the create wizard
was replaced. The same mechanism makes the remaining sites silently inert.

## Fix pattern (verified)

Use the supported pair, and keep a **single source of truth** for the selection:

```razor
<FluentSelect TOption="PickerOption" Multiple
              Items="@_groupOptions"
              OptionText="@(o => o.Label)"
              OptionValue="@(o => o.Value)"
              SelectedOptions="@SelectedGroupOptions"
              SelectedOptionsChanged="@OnSelectedGroupsChangedAsync" />

@code {
    // computed projection of the page's single source of truth (ids)
    private IEnumerable<PickerOption> SelectedGroupOptions =>
        _groupOptions.Where(o => _selectedGroupIds.Contains(Guid.Parse(o.Value)));
}
```

Notes:

- `.github/skills/dropdown-ui/SKILL.md` routes **multi-select** to a raw
  `FluentSelect` (not the single-select wrapper) — follow it; the repo precedent
  is the `TeacherEditDialog` multi-grade picker.
- Prefer `SelectedOptions` + `SelectedOptionsChanged` with a **computed**
  projection over `@bind-SelectedOptions`, which would introduce a second,
  mirrored field that can drift from the ids.
- Driving the callback in bUnit: `.Instance.SelectedOptionsChanged.InvokeAsync(...)`
  wrapped in `cut.InvokeAsync(...)` — never raw `TriggerEvent(...)`.

## Sites

| Site | Status |
|---|---|
| `Assignments/…/AssignmentAuthoring.razor` | **fixed** (R1-5, 2026-10-01) |
| `Students/…/Students/GradeLevels/Create.razor` | **open** |
| `Students/…/Students/GradeLevels/Edit.razor` | **open** |
| `Students/…/GradeLevelCreateDialog.razor` | **open** |
| `Students/…/GradeLevelEditDialog.razor` | **open** |
| `Students/…/JoinGroupsDialog.razor` | **open** |

The five open sites are all grade-level **topic** pickers or the **join-groups**
picker, all `FluentListbox` + `@bind-SelectedValues`.

## Detection (cheap audit)

```bash
grep -rn 'bind-SelectedValues' src/ tests/ --include=*.razor | grep -v obj
```

Any hit on a FluentUI list component (or any component that does not itself
declare `SelectedValues`) is this defect. The Assignments bUnit test
`GroupPicker_BindsTheSupportedApi_AndRendersNoStrayAttribute` is the regression
pin for the fixed site and the template for the others.

## Follow-up

One bounded round in the **Students** context: fix the five sites, add the same
stray-attribute + selection-round-trip assertions, and re-verify the grade-level
topic pickers and the join-groups dialog end to end. Until then, treat those
multi-selects as non-functional in both directions.
