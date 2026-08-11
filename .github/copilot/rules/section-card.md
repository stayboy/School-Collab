# SectionCard usage (grade-detail section cards)

This file holds the topic-specific rules for the shared `SectionCard` component
(`src/Students/SchoolCollab.Students.Application/Components/Students/SectionCard.razor`)
used by the Subjects / Teachers / Students / Streams preview cards on the grade-detail
page (`GradeLevels/Detail.razor`). Split out of `blazor-components.md` to keep that
file from overflowing — follow this split-file pattern for other component-specific
rule sets.

## Kebab actions (RowActionsMenu)

- **Destructive kebab actions** must be `RowAction.Callback("Remove", ..., destructive: true)` with a
  `confirmMessage` — never a hand-rolled page-level confirm (that would double-prompt). The confirm is
  enforced at the component level by `RowActionsMenu` via `ShowConfirmDialogAsync`.
- The item **name is the primary affordance**; the kebab holds the secondary + destructive actions. Set
  `ItemNameTitle` when the primary affordance is an edit or a named view (e.g. `ItemNameTitle="Edit topic"`,
  `ItemNameTitle="View student"`) so the item anchor advertises its action.

## Error state — page message alerts, NOT the SectionCard `ErrorMessage` param

- A card that catches a load/mutation error must surface it via a **page-level
  `<FluentMessageBar Intent="MessageIntent.Error">` placed ABOVE the card** (inside the
  `FluentGridItem`), exactly like the Subjects card surfaces `_topicsError`.
- **Do NOT use the `SectionCard` `ErrorMessage` parameter** for card error state. That param
  was a misunderstanding of the Subjects pattern — the Subjects card uses a page message
  alert, not the in-component param. Keep the page-alert pattern for every card.
- A card that catches a load error and silently sets `Items = []` (no page alert) is a bug —
  the failure looks like "no items".

## Reload after child-triggered mutations

- **Child-component-triggered reloads must call `StateHasChanged()`** after refetching `Items`. The kebab
  menu and dialogs re-render THEMSELVES, not the page — without `StateHasChanged()` the card never
  receives the refreshed `Items` after a mutation.

## Create / Edit dialogs — shared form-fields; never nest a dialog inside a dialog

- **Card-level create + per-row edit use shared-form-fields dialogs** (`XxxFormFields` bound to a model,
  wrapped in a `DialogShellBase`/`IDialogContentComponent` dialog) — mirroring
  `TopicCreateDialog`/`TopicEditDialog` — not landing-page forms. The card Add button opens the create
  dialog; the row kebab Edit opens the edit dialog.
- **Never open a dialog from another dialog instance.** When a dialog needs to expose a sub-editor
  (e.g. `GradeTeachersDialog`'s per-teacher subject+role management), use an **in-page section (`<div>`)
  toggle** that expands/collapses the editor inline within the same dialog — not a nested dialog. The
  shared `TeacherSubjectRoleFormFields` is reused both in a standalone dialog (`TeacherSubjectsDialog`,
  from the kebab) and inline-toggled within `GradeTeachersDialog`.

## Read-only dialog plumbing (`IDialogContentComponent<DialogParameters>`)

- **Every dialog opened via `ShowReadonlyDialogAsync<TComponent>` must mark its `Content`
  property with `[Parameter]`** (it implements `IDialogContentComponent<DialogParameters>`).
  FluentUI's dialog host sets `Content` as a regular parameter on open — if the attribute is
  missing, Blazor throws `ThrowForUnknownIncomingParameterName` and the dialog **never renders**
  (silently "does nothing" from the user's POV). This is exactly what broke the section-card
  create/edit dialogs. Do NOT use `[CascadingParameter]` here — FluentUI passes it as a direct
  parameter, so `[CascadingParameter]` fails with "cannot be set explicitly".

## Await the dialog result before reloading the card

- **Create/Edit handlers must `await` the dialog's result and reload the card only on a
  non-cancelled / successful save.** `ShowReadonlyDialogAsync` returns the `IDialogReference`
  as soon as the dialog is shown; reloading immediately after it returns pulls **stale** data
  (before the user saved). The correct pattern:
  ```
  var dialog = await DialogService.ShowReadonlyDialogAsync<XxxDialog>(...);
  if (_disposed) return;
  var result = await dialog.Result;
  if (_disposed || result.Cancelled) return;
  await ReloadXxxAsync();
  ```
- **When a dialog mutates data internally (no OK/Cancel result), raise an `OnChanged`
  `Func<Task>` callback** so the page reloads the card at the moment the change is persisted,
  and have the handler pass `OnChanged = ReloadXxxAsync` instead of reloading after open.

## Topic+role assignment dates (open-ended)

- A teacher↔topic assignment (`TeacherTopic`) carries `StartDate` (required) and `EndDate` (nullable =
  open-ended). The shared `TeacherSubjectRoleFormFields` renders a start + open-ended-end date picker per
  assigned topic. Persist via `LinkTeacherTopicAsync` / `SetTeacherTopicRoleAsync` (which carry the dates).

## Reference implementation

- The **Subjects card** on `GradeLevels/Detail.razor` (page message alert for `_topicsError`,
  `ItemNameTitle`, `StateHasChanged()` after reload, topic create/edit dialogs).
- `GradeTeachersDialog` renders its teacher list in a **`FluentDataGrid`** (compact rows) and toggles the
  shared `TeacherSubjectRoleFormFields` in-page for subject+role management.