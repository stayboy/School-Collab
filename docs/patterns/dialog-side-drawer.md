# Pattern: Dialog Side Drawer

Use a `<DialogDrawer>` inside a `<FluentDialog>` when a focused, per-item edit
form needs to slide in over the dialog body — for example editing a contact,
guardian, or any sub-entity of the larger form without leaving the dialog.

---

## When to use

- The user is already inside a `<FluentDialog>`.
- They need to add/edit one item of a collection that lives on the dialog's
  main form.
- You want to avoid nested modal dialogs (the anti-pattern the dialog-ui skill
  warns against).

## When NOT to use

- For page-level flows (use a full page or a standalone wizard instead).
- When the edit surface is large or complex enough to need the full viewport
  (open a separate dialog or page).
- For operations that require a reason/audit modal inside the drawer — the drawer
  should not host nested modals; defer those actions to the entity's detail page.

---

## Components

| Component | Responsibility |
|-----------|--------------|
| `DialogDrawer` | Renders the slide-in panel, backdrop, header (title + ×), body, and footer (Close/Submit). |
| Host component | Provides a `position: relative` wrapper with a **definite height** inside `.fluent-dialog-body`. |
| Body component | Renders only the fields and the primary action (Save/Add); the drawer owns Close/Cancel. |

---

## Layout contract

The host dialog body has the following DOM structure:

```html
<div class="fluent-dialog-body">
  <!-- .fluent-dialog-body is a 1fr grid row, so it has a definite height -->
  <div class="my-dialog-root">
    <StudentFormFields ... />
    <DialogDrawer ...>
      <ContactsEditor View="ContactsView.Edit" ... />
    </DialogDrawer>
  </div>
</div>
```

The host wrapper **must** be the drawer's containing block:

```css
.my-dialog-root {
  position: relative;
  /* Give the wrapper a definite height so the absolutely-positioned drawer
     (inset: 0) is constrained to the dialog body instead of sizing to its
     content and overshooting. */
  max-height: 72vh;
  min-height: 320px;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
```

`DialogDrawer` positions itself `absolute` with `top: 0; bottom: 0;` inside this
wrapper, so it fills the dialog body vertically without overlapping the title bar
or the action row.

---

## Action contract

The drawer **owns the Cancel/Close affordance** via `ShowCancel="true"`:

- × button in the header.
- Clicking the backdrop.
- Pressing Escape while focus is inside the drawer.
- The footer Close button.

The body component **must NOT** render its own Cancel button. It should render
only the primary action (Save/Add) inline, matching `ContactsEditor.Edit`:

```razor
@* Inside the body component's Edit view *@
<div class="edit-form">
    <ContactFormFields Model="..." ... />
    <div class="edit-form__actions">
        <FluentButton Appearance="Appearance.Accent" OnClick="SaveAsync">
            Save
        </FluentButton>
    </div>
</div>
```

If a Submit button in the drawer footer is ever needed, wire `ShowSubmit="true"`
and `OnSubmitAsync` on the drawer, but that is not the default repo pattern.

---

## Minimal example

```razor
@* MyDialog.razor *@
<div class="my-dialog-root">
    <StudentFormFields ...
        OnEditContact="OpenEditContactAsync"
        OnAddContact="OpenAddContactFormAsync" />

    <DialogDrawer Open="@(_editor != ActiveEditor.None)"
                  OpenChanged="OnDrawerOpenChangedAsync"
                  Title="Edit contact"
                  Side="DialogDrawerSide.Right"
                  Width="420px"
                  ShowCancel="true"
                  CancelText="Close">
        <ContactsEditor View="ContactsEditor.ContactsView.Edit"
                        Mode="ContactsEditor.EditorMode.Buffered"
                        OwnerType="ContactOwnerType.Student"
                        Contacts="_model.Contacts"
                        IsAdd="@_isAdd"
                        InitialEditKey="@_editingKey"
                        ContactsChanged="OnDraftContactsChanged" />
    </DialogDrawer>
</div>
```

```css
/* MyDialog.razor.css */
.my-dialog-root {
    position: relative;
    display: flex;
    flex-direction: column;
    max-height: 72vh;
    min-height: 320px;
    overflow: hidden;
}
```

```csharp
// MyDialog.razor.cs (or @code block)
private enum ActiveEditor { None, Contacts }
private ActiveEditor _editor = ActiveEditor.None;
private bool _isAdd;
private Guid? _editingKey;

private void OpenEditContactAsync(Guid key)
{
    _isAdd = false;
    _editingKey = key;
    _editor = ActiveEditor.Contacts;
}

private void OpenAddContactFormAsync()
{
    _isAdd = true;
    _editingKey = null;
    _editor = ActiveEditor.Contacts;
}

private Task OnDrawerOpenChangedAsync(bool open)
{
    if (!open)
    {
        _editor = ActiveEditor.None;
        _isAdd = false;
        _editingKey = null;
    }
    return InvokeAsync(StateHasChanged);
}
```

---

## Do's and don'ts

### Do

- ✅ Wrap the dialog content in a `position: relative` host with a definite
  height (usually `max-height: 72vh; min-height: 320px;`).
- ✅ Use `DialogDrawer` for focused per-item add/edit inside a dialog.
- ✅ Put only the primary action (Save/Add) inside the body component.
- ✅ Let the drawer provide Close/Cancel via `ShowCancel="true"`.
- ✅ Disable the main form's action row and other section triggers while the
  drawer is open so only one item is edited at a time.
- ✅ Reset transient state when the drawer closes.

### Don't

- ❌ Render an inline Cancel button inside the drawer body — it duplicates the
  drawer's own Close/Cancel affordance.
- ❌ Open a second modal dialog inside the drawer — defer reason/audit modals to
  the entity's detail page.
- ❌ Host a drawer without a positioned ancestor — the panel will size to its
  content and overshoot the dialog.
- ❌ Use the drawer for page-level flows; it is scoped to the dialog body.

---

## Styling

`DialogDrawer.razor.css` already provides:

- A dim backdrop over the main form.
- A white panel with a strong cast shadow and an inside-facing border for clear
  visual separation.
- Slide-in animation from the right (default) or left.

Hosts only need to supply the wrapper styles above. Body components style their
own field layout, but should keep the narrow 420px drawer width in mind (use
`FormRow Orientation="RowOrientation.Vertical"` for stacked fields).

---

## Examples in the repo

- `src/SchoolCollab.Admin.Shared/Components/ContactsEditor.razor` — the
  reference implementation of the pattern (Edit view, no inline Cancel, inline
  Save only).
- `src/Students/SchoolCollab.Students.Application/Components/Students/GuardianSection.razor` —
  per-guardian edit inside the student edit dialog.
- `src/Students/SchoolCollab.Students.Application/Components/Students/StudentEditDialog.razor` —
  the host that owns the shared `DialogDrawer` and orchestrates contacts vs
  guardians.
