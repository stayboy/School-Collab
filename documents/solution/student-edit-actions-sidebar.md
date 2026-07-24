# Student edit — move Save/Cancel to a right-side sidebar

## Goal

The student edit form (`/students/{id}/edit`) currently renders the
Save / Cancel buttons in a horizontal row **below** the form fields,
separated by a CSS `border-top` on `.form-actions`. The user wants
the action buttons moved to a **vertical sidebar on the right** of
the form fields, so they:

- stack vertically (one per row)
- sit to the right of the form, not below it
- stay visible (sticky) as the user scrolls the form

The default behavior stays unchanged for every other consumer of
`StudentFormFields` (the Create form, the inline GradeLevelWizard
"new student" form) — only the Edit page opts in to the sidebar.

## Why

- **Long forms are awkward with a bottom action bar.** The student
  edit form has identity + DOB + gender + guardians list + Direct
  contact editor. By the time the user reaches the bottom, the
  Cancel/Save buttons may be off-screen on a laptop viewport. A
  right-side sticky sidebar keeps them always reachable.
- **Vertical Save/Cancel is conventional for right-side action
  panels.** Microsoft 365 admin panels, Aspire Dashboard, GitHub
  PRs, Figma, Linear — all use a vertical action stack in a
  right rail. Horizontal-at-bottom is for short forms only.
- **Sidebar is opt-in, not a breaking change.** A new
  `ActionsPlacement` parameter defaults to `Bottom` so the Create
  form and the inline GradeLevelWizard form keep their existing
  layout. Only the Edit page opts in.

## Design

### New parameter: `ActionsPlacement`

Added to `StudentFormFields.razor`:

```csharp
public enum StudentFormActionsPlacement
{
    /// <summary>Save/Cancel render in a horizontal row below the
    /// form fields, separated by a border-top on .form-actions.
    /// The original behavior. Use for short forms where a
    /// bottom-anchored action bar makes sense.</summary>
    Bottom,

    /// <summary>Save/Cancel render in a vertical column to the
    /// right of the form fields, inside a CSS Grid two-column
    /// layout. The column is sticky so the buttons stay visible
    /// as the user scrolls. The .form-actions--sidebar wrapper
    /// draws the visual separator. Use for long forms where a
    /// bottom action bar can scroll off-screen.</summary>
    Right,
}

[Parameter] public StudentFormActionsPlacement ActionsPlacement { get; set; } = StudentFormActionsPlacement.Bottom;
```

### Markup change (StudentFormFields.razor)

The existing `<div class="form-actions ...">` is rendered
**unconditionally** at the end of the `<EditForm>`. The new
behavior wraps the **whole form** in a CSS Grid container when
`ActionsPlacement == Right`, and moves the `.form-actions`
into the second grid column.

To keep the change minimal and avoid touching every existing
caller:

1. The `<EditForm>` gets a wrapping `<div
   class="student-form-fields__layout @(ActionsPlacement ==
   StudentFormActionsPlacement.Right ?
   "student-form-fields__layout--sidebar" : null)">` when
   `ActionsPlacement == Right`.
2. Inside that wrapper, the form fields go in column 1
   (`<div class="student-form-fields__fields">`) and the
   `.form-actions` block goes in column 2 (`<div
   class="student-form-fields__sidebar">`).
3. The `.form-actions` block now uses `.form-actions--sidebar`
   (a new modifier) when in sidebar mode, which restyles the
   buttons to stack vertically and align to the right edge.

For the `Bottom` (default) case, the wrapper is not rendered
and the markup is byte-identical to before.

### CSS (StudentFormFields.razor.css)

```css
/* Sidebar layout: a 2-column CSS Grid with form fields in column 1
   and the action buttons in column 2. The action column is
   position:sticky so the buttons stay visible as the user scrolls.
   Collapses to a single column (actions go back to the bottom) on
   viewports < 900px. The form max-width (600px) now applies to
   the FIELDS column, not the whole component, so the form doesn't
   stretch uncomfortably wide on big screens. */
.student-form-fields__layout--sidebar {
    display: grid;
    grid-template-columns: minmax(0, 1fr) 200px;
    gap: 24px;
    align-items: start;
}

.student-form-fields__layout--sidebar .student-form-fields__fields {
    min-width: 0;  /* allow the grid track to shrink below the
                      content's intrinsic width */
    max-width: 600px;  /* keep fields narrow on big screens */
}

.student-form-fields__layout--sidebar .student-form-fields__sidebar {
    position: sticky;
    top: 16px;  /* sits below the page header on a typical viewport */
    display: flex;
    flex-direction: column;
    align-items: stretch;
    gap: 8px;
}

/* Sidebar buttons: full-width, stacked vertically. The Save
   button is Accent (primary), Cancel is Outline (secondary).
   Both fill the sidebar width so they have a clear hit target
   on touch devices. */
.form-actions--sidebar {
    display: flex;
    flex-direction: column;
    align-items: stretch;
    gap: 8px;
    margin-top: 0;          /* no top border/margin needed — the grid gap separates */
    padding-top: 0;
    border-top: none;       /* no separator in the sidebar */
}

.form-actions--sidebar .form-actions__button {
    width: 100%;
    justify-content: center;
}

/* Narrow-viewport collapse: drop the sidebar column and let the
   .form-actions fall back to a horizontal row at the bottom of
   the form. The CSS Grid 1fr template collapses naturally if we
   switch to a single column. */
@media (max-width: 900px) {
    .student-form-fields__layout--sidebar {
        grid-template-columns: minmax(0, 1fr);
    }
    .student-form-fields__layout--sidebar .student-form-fields__sidebar {
        position: static;  /* sticky doesn't make sense at the bottom */
    }
    .form-actions--sidebar {
        flex-direction: row;
        justify-content: flex-end;
    }
}
```

### Edit.razor change

```razor
<StudentFormFields Model="_model"
                   ShowGuardians="true"
                   StudentId="Id"
                   ReadOnlyStudentNumber="true"
                   ErrorMessage="@_error"
                   OnValidSubmit="OnSaveAsync"
                   SubmitLabel="Save"
                   SubmittingLabel="Saving…"
                   Submitting="@_saving"
                   OnCancel='@(() => Nav.NavigateTo($"/students/{Id}"))'
                   ActionsPlacement="StudentFormFields.StudentFormActionsPlacement.Right" />
```

### Other consumers (UNCHANGED)

- `Create.razor` — uses the default `Bottom` placement. Short form
  with only identity + DOB + gender, so a bottom action bar is
  fine.
- `GradeLevelWizard.razor` (inline "new student" form) — uses the
  default `Bottom` placement. It's a side-by-side wizard step,
  not a full-page form, and the wizard has its own Back/Next
  footer.

## File map

| File | Change |
|------|--------|
| `src/Students/SchoolCollab.Students.Admin/Components/Students/StudentFormFields.razor` | Add `StudentFormActionsPlacement` enum + `ActionsPlacement` parameter. Wrap `<EditForm>` in a sidebar-mode grid container. Move `.form-actions` into a `.form-fields__sidebar` slot when `Right`. Add `form-actions__button` class on the buttons so the CSS can target them. |
| `src/Students/SchoolCollab.Students.Admin/Components/Students/StudentFormFields.razor.css` | Add `.student-form-fields__layout--sidebar` (grid), `.student-form-fields__fields`, `.student-form-fields__sidebar`, `.form-actions--sidebar` (vertical column), `.form-actions__button` (full-width), `@media (max-width: 900px)` collapse rule. |
| `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/Edit.razor` | Pass `ActionsPlacement="...Right"` to `<StudentFormFields>`. |
| `tests/SchoolCollab.Admin.Tests.Unit/StudentFormFieldsActionsPlacementTests.cs` (new) | Source-level tests: assert the enum, parameter, default, Edit.razor opts in, Create.razor + GradeLevelWizard keep default. CSS cross-check: every new class has a rule. |
| `tests/SchoolCollab.Admin.Tests.Unit/EditContactEditorTests.cs` (update) | The "Contacts editor sits outside StudentFormFields" test must still pass — the wrapper change doesn't move the editor. |

## Verification

1. `dotnet build` — 0 errors.
2. `dotnet test` (Admin unit) — all pass, including the new
   `StudentFormFieldsActionsPlacementTests` (~6 tests).
3. Manual smoke (with AppHost running):
   - `/students/{id}/edit` — Save/Cancel in a right-side vertical
     stack, sticky on scroll, collapses to bottom row on narrow
     viewport (<900px).
   - `/students/create` — Save/Cancel still horizontal at the
     bottom (unchanged).
   - The GradeLevelWizard inline "new student" form still has its
     bottom action bar (unchanged).
   - Form fields stay max 600px wide on the edit page (they were
     before; the wrapper doesn't widen the form).

## Decision log

- **Bottom vs. Right.** Considered a third option "Right and
  Bottom (both visible)" — rejected. Two action affordances for
  the same Save action is confusing. One placement per form.
- **Sticky vs. not.** Sticky on the sidebar makes the action bar
  always reachable. Sticky on a bottom-anchored bar is impossible
  (it'd float over the form). So sticky only applies in the
  Right placement, where it makes sense.
- **Sidebar width.** 200px is wide enough for a full-width "Save"
  button with a normal touch target (~36px tall) and just narrow
  enough to keep the form fields at their natural 600px max-width
  on a typical 1366px laptop screen (1366 - 200 - 48 gap - 48
  page-padding - 32 sidebar = ~1038px available for fields, so
  they hit the 600px cap and the sidebar fills the rest).
- **No separate component.** Considered extracting a
  `<StudentFormActions>` component. Rejected — the action row is
  tightly coupled to the `<EditForm>` (the Submit button needs
  `Type="ButtonType.Submit"`), and there's no second consumer.
- **No custom renderfragment for the action placement.** A boolean
  flag like `ActionsOnRight` would be too narrow. An enum lets
  the form grow into more placements (e.g. `Top` for an action
  bar above the fields) without breaking the API.
- **CSS Grid vs. Flex.** CSS Grid is the right tool for a
  two-column layout where one column is the natural document flow
  and the other is a fixed-width sidebar. Flex would require
  magic `margin-left: -200px` tricks to get the same effect.
- **No JavaScript.** The sticky positioning is pure CSS
  (`position: sticky`). No `IntersectionObserver` or scroll
  listener needed.
- **Reuse the existing `form-actions` block.** The action row's
  internals (the Submit button + Cancel button + the optional
  `Actions` RenderFragment override) are unchanged. The wrapper
  around them gets a new modifier class when in sidebar mode.
  This keeps the diff small and the test surface stable.
