---
name: dialog-ui
description: |
  SchoolCollab dialog & inline-form UI conventions. Use when creating or
  editing a Blazor dialog, the shared dialog footer, an inline form, the
  action-button row, or the horizontal separator line between form content
  and the Cancel/Save buttons. Triggers: "dialog", "DialogShellBase",
  "DialogShellFooter", "ShowShellDialogAsync", "form actions", "button row",
  "horizontal line", "separator", "border-top separator", "scoped CSS",
  "razor.css overwrite", "action buttons", "Cancel Save buttons", "inline
  form", "StudentFormFields", "PeriodForm", "CodedValueDialog",
  "AttributeDefinitionDialog", "FluentDialog".
---

# Dialog & inline-form UI conventions (SchoolCollab)

This is the goto guidance for dialog UI in this repo. Use it whenever you
create or edit a Blazor dialog, the shared dialog footer, an inline form, or
the action-button row + its separator line.

Canonical source docs:
- `documents/solution/dialog-consolidation-plan.md` — the spec sheet for the
  `DialogShellBase` / `DialogShellFooter` consolidation (read for full
  rationale, phase history, and acceptance criteria).
- This skill is the durable, quick-reference distillation of that plan plus
  the standing "horizontal line" convention.

---

## 1. Build dialogs on `DialogShellBase` — don't hand-roll dialog markup

Every "form" dialog in this repo derives from the shared shell in
`src/SchoolCollab.Admin.Shared/Components/Dialogs/`. Do NOT write a fresh
`<FluentDialog>` + Cancel/Save buttons + saving/error state per dialog.

### The shell components

| File | Role |
| --- | --- |
| `Dialogs/DialogShellData.cs` | `DialogShellData<TModel>(TModel Model)` (input payload) and `DialogShellResult<TResult>(TResult Value)` (output wrapper). |
| `Dialogs/DialogShellBase.cs` | Abstract base: `ComponentBase` + `IDialogContentComponent<DialogShellData<TModel>>`. Owns `[Parameter] Content`, `[CascadingParameter] FluentDialog Dialog`, `Model` (falls back to `new TModel()`), `Saving`/`Error` state, `SubmitAsync` hook, `HandleSubmitAsync`/`HandleCancelAsync`. **No markup.** |
| `Dialogs/DialogShellFooter.razor` | Shared presentational footer: error `FluentMessageBar` + Cancel/Save `.button-row`. Derived dialog places `<DialogShellFooter/>` at the END of its markup, INSIDE its `<EditForm>`. |
| `Dialogs/DialogServiceExtensions.cs` | `ShowShellDialogAsync(model, title, width, …)` — opens the dialog with the four constant `DialogParameters` (below) and returns the unwrapped `TResult` (or `null` on cancel). |

### Derived dialog shape

```razor
@inherits DialogShellBase<CodedValueFormModel, CodedValueResult>
@* ... @code: override SubmitAsync ... *@

<EditForm Model="Model" OnValidSubmit="HandleSubmitAsync">
    <DataAnnotationsValidator />
    @* form fields bound to Model *@
    <DialogShellFooter Saving="Saving" Error="Error"
                       OnCancel="HandleCancelAsync" SubmitText="Save" />
</EditForm>

@code {
    protected override async Task<CodedValueResult?> SubmitAsync(
        CodedValueFormModel model)
    {
        // side effect; return non-null to close, null to stay open (set Error),
        // throw to surface ex.Message in the footer and stay open.
    }
}
```

### Rules that catch people out

- The footer's **Submit** button is `Type="ButtonType.Submit"` with **no
  `@onclick`** — clicking it submits the enclosing `<EditForm>` and fires
  `OnValidSubmit` → `HandleSubmitAsync` → your `SubmitAsync`. The **Cancel**
  button uses `OnClick` (NOT submit) so it does **not** trigger validation.
- `HandleSubmitAsync` guards double-submit (`if (_saving) return;`), clears
  `Error`, calls `SubmitAsync`, closes with `DialogShellResult<T>` on non-null,
  surfaces `ex.Message` on throw, and always resets `_saving`.
- Open the dialog via `ShowShellDialogAsync`, which always sets:
  `PrimaryAction = null`, `SecondaryAction = null`,
  `PreventDismissOnOverlayClick = true`, plus the caller's `Title` + `Width`.
  Do NOT pass custom primary/secondary actions.

---

## 2. The action-button separator is a CSS `border-top`, NOT a divider

Every dialog/inline form draws the horizontal line between its content and the
action buttons with a **CSS `border-top` on the action-row block** — never a
`<FluentDivider>` and never an `<hr>`.

Why:
- No extra DOM node → the line aligns exactly with the label column (`x = 0`),
  not shifted by a web component's implicit margins.
- Uses the FluentUI design variable `var(--neutral-stroke-rest, #e0e0e0)` →
  adapts to light / dark / high-contrast themes.

Where the rule lives today (do NOT "tidy" a form by swapping it for a
`<FluentDivider>`):

| Component | Class | File |
| --- | --- | --- |
| `DialogShellFooter` (shared; every `DialogShellBase` dialog) | `.button-row` (`border-top`) | `src/SchoolCollab.Admin.Shared/Components/Dialogs/DialogShellFooter.razor` (inline `<style>`) |
| `StudentFormFields` (shared Student create / edit / inline wizard) | `.form-actions` (`border-top`) + `.form-actions--right` | `src/Students/SchoolCollab.Students.Application/Components/Students/StudentFormFields.razor.css` |
| `PeriodForm` (sibling, same pattern) | `.form-actions` | `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor.css` |

The `StudentFormFields` and `DialogShellFooter` comments cross-reference each
other — if you change one, change the other.

Canonical `.form-actions` rule (mirror this if you add a new inline form):

```css
.form-actions {
    margin-top: 1.25rem;
    padding-top: 1rem;
    border-top: 1px solid var(--neutral-stroke-rest, #e0e0e0);
    display: flex;
    align-items: center;
}
.form-actions--right { justify-content: flex-end; }
```

---

## 3. Scoped-CSS hazard — never blanket-overwrite a `*.razor.css`

Blazor **scoped** stylesheets (`Component.razor.css`) are tied to the component
by markup class names. The build does NOT warn when a CSS rule has no matching
markup, and does NOT warn when markup references a class the CSS no longer
defines. The symptom is a **silent visual regression** — the build still passes.

### Real incident
A full-file `write` of `StudentFormFields.razor.css` replaced the file with
only the guardian classes, deleting `.form-actions { border-top }`. The
`<div class="form-actions">` element stayed in `StudentFormFields.razor`, so
the build was green — but the horizontal separator silently vanished from the
student picker dialog.

### Do this instead
- **Edit/append individual rules**; keep existing rules intact. Prefer a
  precise `edit` over a whole-file rewrite.
- If you must restructure the file: first read it, diff against
  `git show HEAD:<path>`, and copy forward every rule whose class is still
  referenced by the markup. Then merge your changes.
- After any `*.razor.css` change, grep the markup for the classes you touched
  and confirm they still resolve to a CSS definition:

  ```bash
  grep -n "form-actions\|button-row\|student-form-" <Component>.razor
  ```

- A CSS-only change is NOT caught by a build error — verify the separator is
  present by visual / browser check (the build still reports success).

---

## 4. Inline forms (no dialog), and the "no nested dialog" rule

For an inline form inside an existing page/dialog (e.g. the guardian section
inside `StudentFormFields`), use a plain `<div>` panel switch — do NOT open a
dialog from within a dialog ("invoking a dialog from a dialog isn't the best")
and do NOT nest an `<EditForm>` inside the parent `<EditForm>` (nested `<form>`
HTML is invalid). A reusable presentational component like `GuardianFormFields`
(no `<EditForm>` of its own) bound to the parent's model is the pattern.

The inline form's action row still uses the `.form-actions` + `border-top`
separator from §2.

---

## 5. Label column is a shared primitive

The 180px label column, required markers, and `FormRow` layout live in
`src/SchoolCollab.Admin.Shared/Components/FormRow.razor(.css)`. Form components
(`StudentFormFields`, `PeriodForm`) carry only their own field-specific bits;
do NOT re-declare the label grid locally.

---

## 6. Checklist for a new dialog / inline form

- [ ] Dialog derives from `DialogShellBase<TModel, TResult>` (no hand-rolled
      `<FluentDialog>`/Cancel/Save/saving state).
- [ ] `<DialogShellFooter/>` placed at the end of the markup, INSIDE the
      `<EditForm OnValidSubmit="HandleSubmitAsync">`.
- [ ] `SubmitAsync` overridden: returns `TResult?`; null = stay open (set
      `Error`); throw = surface message, stay open.
- [ ] Opened via `ShowShellDialogAsync` (four constant `DialogParameters`).
- [ ] Action buttons in a `.form-actions` / `.button-row` block with
      `border-top` — NOT a `<FluentDivider>`.
- [ ] Label column uses the shared `FormRow` primitive.
- [ ] Any `*.razor.css` edit was a **merge**, not a full overwrite; markup
      classes still resolve to CSS rules.
- [ ] Build passes AND the separator line is confirmed present in the UI.
- [ ] Any dialog **height** is set on the dialog's **content root** (definite
      height + a `flex: 1 1 auto; min-height: 0` scrolling region + the action
      row last with `margin-top: auto`) — NOT via `DialogParameters.Height`:
      the actions must sit on the dialog's bottom, never mid-dialog. Use the
      shared `ReadOnlyDialogShell` instead of re-declaring any of it (§7).
- [ ] A scrolling region holding a `FluentDataGrid` scrolls the **wrapper**, not
      the grid (the grid is `width: 100%` only — `flex: 1 1 auto` stretches its
      rows) and the grid uses `GenerateHeader="GenerateHeaderOption.Sticky"` so
      only the body scrolls under a fixed header (§7).

---

## 7. Dialog height — the content area is the height authority, not the box

To make a dialog taller, put the height on the dialog's **content root** — do
**not** grow the dialog box with `DialogParameters.Height` (the `height:`
argument on `ShowShellDialogAsync` / `ShowReadonlyDialogAsync`).

Why (verified against FluentUI 4.14.2):

- The dialog box (`::part(control)`) is a **fixed** height:
  `calc(var(--dialog-height) - 2 * padding)`, with `--dialog-height` defaulting
  to `480px`. `DialogParameters.Height` only re-pins that box.
- The body (`.fluent-dialog-body`) is `height: auto`, so a taller box does NOT
  grow the body. The content sits at the top and the action row (the shell
  footer, or a read-only dialog's `.dialog-footer` — the content's LAST child)
  is left **stranded mid-dialog**, with dead space underneath it.
- A definite height on the content root gives the body something to grow to,
  and the flex column hands the slack to the scrolling region, so the action
  row lands on the dialog's bottom.

Do this:

```css
.my-dialog {                      /* content root = the height authority */
    display: flex;
    flex-direction: column;
    height: max(72vh, 480px);     /* or min-height/max-height for content-fill */
    overflow: hidden;
}
.my-dialog__scroll {              /* the ONE scrolling region (grid/list/body) */
    flex: 1 1 auto;
    min-height: 0;                /* REQUIRED: a flex child will not shrink without it */
    overflow-y: auto;
}
.dialog-footer {                  /* actions: last child, pinned to the bottom */
    margin-top: auto;
    flex: none;
    /* + the §2 border-top separator */
}
```

Open the dialog with the width preset you need and **no** `height:` argument:
`ShowReadonlyDialogAsync<T>(title, params, DialogSize.ExtraLarge)`.

- Prefer `min-height` + `max-height` (content-fill) when a short dialog should
  stay short: `StudentEditDialog.razor.css` (`.student-edit-dialog-root`) is the
  shipped precedent — `max-height: 72vh; min-height: 320px`.
- Use a fixed `height` when the surface should always be that tall, e.g. the
  Subjects View-all grid (`GradeTopicsDialog.razor.css`, `height: max(72vh, 480px)`).

### A grid inside the scroll region keeps its natural height

When the scrolling region holds a `FluentDataGrid`, the grid must keep its
**natural height**: never `flex: 1 1 auto` on the grid (it stretches the rows to
fill the box) and never `overflow`/`max-height` on the grid either — a bounded or
overflowing grid becomes the sticky context itself and scrolls its header out of
view (`LandingPage.razor.css` records that rationale). Let the wrapper scroll and
switch on FluentUI's sticky header row:

```razor
<div class="my-dialog__scroll">
    <FluentDataGrid … GenerateHeader="GenerateHeaderOption.Sticky" Class="my-dialog__grid" />
</div>
```

```css
.my-dialog__scroll { flex: 1 1 auto; min-height: 0; overflow-y: auto; } /* the scroll region */
.my-dialog__grid   { width: 100%; }                                     /* natural height */
```

`GenerateHeaderOption.Sticky` marks the header row `row-type="sticky-header"`, and
FluentUI's own grid CSS supplies `position: sticky; top: 0; background-color:
var(--neutral-fill-stealth-rest); z-index: 2` — so only the body scrolls.

### Implementation: use `ReadOnlyDialogShell` — never hand-roll any of this

The three pieces above are a **shared component**:
`src/SchoolCollab.Admin.Shared/Components/Dialogs/ReadOnlyDialogShell.razor(.css)`
(the read-only sibling of `DialogShellFooter`). It renders the root (the height
authority, content-fill: `min-height: 220px; max-height: 72vh; overflow: hidden`),
one `.readonly-dialog__scroll` region and the bottom-pinned
`.readonly-dialog__footer`. It takes:

- `Class` — the caller's own root class, appended to `readonly-dialog`, so the
  caller's scoped CSS and its tests keep working;
- `Toolbar` — optional fixed rows ABOVE the region (assign picker, error bar);
- `ChildContent` — the scroll region's content;
- `Footer` — the action row.

```razor
<ReadOnlyDialogShell Class="my-dialog">
    <Toolbar>…picker row / error bar…</Toolbar>
    <ChildContent>…the scrollable list or grid…</ChildContent>
    <Footer><FluentButton OnClick="CloseAsync">Close</FluentButton></Footer>
</ReadOnlyDialogShell>
```

Two things to know:

- With named slots present, Razor requires the body in an explicit
  `<ChildContent>` (`RZ9996` otherwise) — unnamed content is rejected.
- The dialog using the shell declares **no** height/scroll/footer CSS of its own.

Consumers today: `GradeTopicsDialog` (Subjects View-all) and `SectionListDialog`
(the Streams View-all a `SectionCard` opens).

---

## 8. Key file references

- `src/SchoolCollab.Admin.Shared/Components/Dialogs/DialogShellBase.cs`
- `src/SchoolCollab.Admin.Shared/Components/Dialogs/DialogShellFooter.razor`
- `src/SchoolCollab.Admin.Shared/Components/Dialogs/DialogServiceExtensions.cs`
- `src/SchoolCollab.Admin.Shared/Components/FormRow.razor(.css)`
- `src/Students/SchoolCollab.Students.Application/Components/Students/StudentFormFields.razor(.css)`
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor.css`
- `documents/solution/dialog-consolidation-plan.md` (full spec)