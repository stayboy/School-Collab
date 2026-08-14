# Dialog Parameter Binding (Lessons Learned)

**Concern: how a dialog opened via `ShowReadonlyDialogAsync` receives its inputs.**

Enforced by `DotNetBestPracticesArchitectureTests.Dialog_ContentParameter_Only_For_ReadonlyDialogs`
— see the companion doc [`dto-form-model-mapping.md`](./dto-form-model-mapping.md) for the
separate concern of projecting DTOs into form models.

## FluentUI 4.14.x does NOT bind `DialogParameters` indexer entries to `[Parameter]`s

### The bug

`DialogServiceExtensions.ShowReadonlyDialogAsync<TComponent>` puts a component's inputs
into the `DialogParameters` **indexer**:

```csharp
var dialogParams = BuildShellParameters(title, size);
dialogParams["StudentId"] = student.Id;          // indexer entry
ShowDialogAsync<StudentEditDialog, DialogParameters>(dialogParams, dialogParams);
```

The intent (per the old doc comment) was that FluentUI binds each indexer entry to the
content component's `[Parameter]` properties. **It does not.** In FluentUI 4.14.2 the
dialog content is rendered via Blazor's `DynamicComponent` with:

```csharp
Parameters = DialogInstance.GetParameterDictionary()   // == { "Content": <DialogParameters> }
```

`GetParameterDictionary()` returns **only** `{ "Content": ... }` — the indexer entries
(`StudentId`, `CurrentRoleId`, `Topics`, …) are **never** spread onto the component's
`[Parameter]` properties. So a `[Parameter] Guid StudentId` silently defaults to
`Guid.Empty`, a `[Parameter] Guid? CurrentRoleId` to `null`, a `[Parameter] T[] Topics`
to `[]`, etc.

### The symptom

The edit dialog opened but the profile fields were blank — the dialog called
`GET /students/00000000-…` (empty guid), which 404'd, so nothing loaded.

### The rule

**A `ShowReadonlyDialogAsync` dialog must read its inputs from `Content` (the
`DialogParameters` indexer), NOT from separate `[Parameter]` properties.**

- Declare a `public const string XxxKey = nameof(Xxx);` for each input so callers set the
  same key the dialog reads.
- Read each input via `Content.TryGet<T>(XxxKey)` (the indexer throws
  `KeyNotFoundException` for a missing key, so use `TryGet`, not `[]`).
- Keep `[Parameter] public DialogParameters Content` (required by
  `IDialogContentComponent<DialogParameters>`).

```csharp
// StudentEditDialog.razor
public const string StudentIdKey = nameof(StudentId);
[Parameter] public DialogParameters Content { get; set; } = default!;
private Guid StudentId => Content is { } c ? c.TryGet<Guid>(StudentIdKey) : Guid.Empty;
```

Callers pass the key, not `nameof`:

```csharp
ShowReadonlyDialogAsync<StudentEditDialog>(title,
    new Dictionary<string, object?> { { StudentEditDialog.StudentIdKey, student.Id } },
    DialogSize.ExtraLarge);
```

### Why `ShowShellDialogAsync` / `DialogShellBase` are unaffected

`ShowShellDialogAsync<TComponent, TModel, TResult>` passes a strongly-typed payload
(`DialogShellData<TModel>`) as the `Content`, and `DialogShellBase<TModel,TResult>` reads
`Model` from `Content.Model`. That works because the data travels inside `Content`, which
FluentUI *does* set. The `ShowReadonlyDialogAsync` components broke because they assumed
the indexer entries would be spread — they aren't.

### Affected dialogs (all fixed)

`StudentEditDialog`, `StudentCreateDialog`, `TeacherRoleDialog`, `GradeTopicsDialog`,
`TopicStrandsDialog`, `GuardianContactsDialog`.

## Testing lesson: assert the bound value, not a string that happens to appear

### The false positive

A test asserted `cut.Markup.Should().Contain("Ada")` after opening the edit dialog through
the real `FluentDialogProvider` flow. It passed — but only because the dialog **title**
is `"Edit Student · Ada Lovelace"`, which contains `"Ada"`. The profile was never loaded;
the assertion matched the title.

### The rule

When verifying a dialog binds its input, assert the **actual bound value** (e.g. the
`<input>` `value` attribute), not a substring that could come from the title, a label, or
an always-rendered element:

```csharp
cut.WaitForAssertion(() => cut.Find("#studentFormFirstName").GetAttribute("value")
    .Should().Be("Ada", "the FirstName input binds the loaded student"));
```

Also: when a dialog reads its inputs from `Content`, tests that render the dialog directly
must pass `Content` (a `DialogParameters` with the keys), not `.Add(x => x.SomeParam, …)`.

## Adoption checklist for a new `ShowReadonlyDialogAsync` dialog

1. Read every input from `Content.TryGet<T>(XxxKey)`; expose `public const string XxxKey`.
2. Do **not** declare `[Parameter]` for the inputs (only `Content`).
3. Callers pass the key constants, not `nameof`.
4. Test the real flow and assert the bound value, not a title substring.
5. A NetArchTest guard fails CI if you declare a non-`Content` `[Parameter]` in a
   `IDialogContentComponent<DialogParameters>` component — so the above is enforced, not
   just recommended.
