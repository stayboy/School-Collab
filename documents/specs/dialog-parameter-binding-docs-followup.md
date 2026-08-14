# Spec: Dialog Parameter Binding — Documentation Follow-up

> **Status:** Draft
> **Owner:** Students context (dialog infrastructure)
> **Depends on:** `documents/solution/dialog-parameter-binding.md`, `DialogServiceExtensions.cs`
> **Branch:** `feature/dialog-binding-and-form-model-mapping` (current)

## 1. Background

A review of `documents/solution/dialog-parameter-binding.md` (the lessons-learned doc for
`ShowReadonlyDialogAsync` parameter binding) found several documentation gaps and one
**stale code comment** that directly contradicts the spec. The spec itself is technically
accurate and the architecture-test enforcement is solid — but the supporting material
needs tightening to prevent future confusion.

## 2. Findings

### F-1: Stale code comment in `ShowReadonlyDialogAsync` (critical)

**File:** `src/SchoolCollab.Admin.Shared/Components/Dialogs/DialogServiceExtensions.cs`,
lines 113–120.

The inline comment reads:

```csharp
// FluentUI binds indexer entries to the content's [Parameter]s the same way
// it binds the typed Title/Width/PrimaryAction/etc. properties.
// …
// the dialog ignores Content and reads everything via [Parameter].
```

Both statements are the **exact opposite** of the actual behavior documented in
`dialog-parameter-binding.md`:

- FluentUI does **NOT** bind indexer entries to `[Parameter]` properties.
- The dialog **must** read from `Content.TryGet<T>(…)`, not from `[Parameter]`.

The comment was written before the bug was discovered and was never updated after the
fix. Any developer reading this comment will be misled into thinking the old (broken)
pattern works.

### F-2: `TryGet<T>` return-value semantics not documented

The spec says "Read each input via `Content.TryGet<T>(XxxKey)`" but doesn't clarify that
`TryGet<T>` returns `T` (not `T?`). For **value types** like `Guid`, a missing key
returns `Guid.Empty` — not `null`. The code example handles this correctly, but the spec
should state the behavior explicitly so callers don't assume `null` for missing
value-type keys.

### F-3: Caller key-constant pattern not explained

The spec says "Callers pass the key constants, not `nameof`" but doesn't explain **why**.
The reason: `nameof(StudentId)` in a caller resolves to the string `"StudentId"`, which
happens to match `StudentIdKey` today — but if the dialog's internal property name
changes, `nameof` in the caller silently breaks (the key string no longer matches), while
referencing `StudentEditDialog.StudentIdKey` keeps the contract explicit and
refactor-safe.


### F-4: Dual-purpose `DialogParameters` instance not explained

The spec shows `ShowDialogAsync<TComponent, DialogParameters>(dialogParams, dialogParams)`
passing the same instance as both the `TData` content and the `DialogParameters` argument,
but doesn't explain why. The first parameter is the content payload (becomes
`IDialogContentComponent.Content`); the second is the dialog chrome (Title/Width/etc.).
Because `ShowReadonlyDialogAsync` merges shell chrome and content entries into a single
`DialogParameters`, the same instance serves both roles.

### F-5: `WaitForAssertion` usage not explained

The testing lesson uses `cut.WaitForAssertion(…)` without explaining why it's needed.
The dialog renders asynchronously (the `FluentDialogProvider` flow involves an async
show → render → data-fetch cycle), so a synchronous assertion can fire before the dialog
has populated its inputs. `WaitForAssertion` retries until the assertion passes or the
bUnit timeout expires.

### F-6: Affected-dialogs list is complete (no action needed)

The spec lists six dialogs: `StudentEditDialog`, `StudentCreateDialog`,
`TeacherRoleDialog`, `GradeTopicsDialog`, `TopicStrandsDialog`, `GuardianContactsDialog`.

A scan of all `.razor` files implementing `IDialogContentComponent<DialogParameters>`
confirms exactly these six. `TeacherEditDialog` is a **shell dialog**
(`@inherits DialogShellBase<TeacherFormModel, TeacherDto>`) — it uses
`ShowShellDialogAsync`, not `ShowReadonlyDialogAsync`, so it is correctly excluded.

## 3. Changes

### 3.1 Fix the stale comments in `DialogServiceExtensions.cs`

Rewrite the `<typeparam>`, the `<param name="parameters">`, and the inline comment so
they reflect the actual behavior: FluentUI renders the content via `DynamicComponent` with
only `{ "Content": <DialogParameters> }`; indexer entries are NOT spread to `[Parameter]`s;
the dialog reads inputs from `Content.TryGet<T>(key)`; and callers pass the dialog's key
constants (not `nameof`). Point at `dialog-parameter-binding.md`.

```csharp
// Build a single DialogParameters carrying both the shell chrome
// (Title/Width/etc.) and the content parameter entries. The same instance
// is passed as both the TData content (IDialogContentComponent.Content) and
// the DialogParameters argument — FluentUI only sets Content from the TData;
// indexer entries are NOT spread to [Parameter] properties. The dialog must
// read its inputs from Content.TryGet<T>(key), not from separate [Parameter]s.
// See dialog-parameter-binding.md for the full explanation.

### 3.2 Update `dialog-parameter-binding.md` with clarifications

Add the following clarifications to the existing doc (non-breaking additions):

#### 3.2.1 `TryGet<T>` semantics (add after "The rule" bullets)

After the bullet "Read each input via `Content.TryGet<T>(XxxKey)`…", add:

> `TryGet<T>` returns `T` (not `T?`). For reference types, a missing key returns `null`.
> For value types (e.g. `Guid`, `int`), a missing key returns `default(T)` — `Guid.Empty`,
> `0`, etc. — **not** `null`. Guard value-type reads with a null check on `Content` and
> document the default in the property's summary.

#### 3.2.2 Why key constants, not `nameof` (add after caller code example)

After the caller code example (`ShowReadonlyDialogAsync<StudentEditDialog>(…)`), add:

> **Why `StudentEditDialog.StudentIdKey` and not `nameof(StudentId)`?** The key constant
> is declared on the dialog, so if the internal property name changes, the constant
> updates automatically and all callers follow. A bare `nameof(StudentId)` in the caller
> resolves to the string `"StudentId"` at compile time — if the dialog renames its
> property, the caller's string silently stops matching and the input defaults.

#### 3.2.3 Dual-purpose `DialogParameters` (add after "The bug" code example)

After the code example showing `ShowDialogAsync<StudentEditDialog, DialogParameters>(dialogParams, dialogParams)`, add:

> The same `dialogParams` instance is passed as both the `TData` content (which becomes
> `IDialogContentComponent<DialogParameters>.Content`) and the `DialogParameters` argument
> (which FluentUI reads for shell chrome: Title, Width, etc.). This works because
> `DialogParameters` stores shell properties as settable fields and content entries in
> its dictionary indexer — the two namespaces don't collide.

#### 3.2.4 `WaitForAssertion` (add after testing-lesson code example)

After the `WaitForAssertion` code example, add:

> `WaitForAssertion` is required because the `FluentDialogProvider` flow is asynchronous:
> the dialog is shown → rendered → the content component fetches data → re-renders with
> populated inputs. A synchronous assertion fires before the data returns, so it sees
> empty inputs and fails. `WaitForAssertion` retries until the assertion passes or the
> bUnit timeout expires (default 1 second).

## 4. Verification

1. **Build:** `dotnet build` — no compile errors from the comment change.
2. **Architecture test:** `dotnet test --filter Dialog_ContentParameter_Only_For_ReadonlyDialogs` — still passes (the six dialogs are unchanged).
3. **Doc review:** Read the updated `dialog-parameter-binding.md` end-to-end and confirm
   no internal contradictions remain.
4. **Code comment review:** Read `DialogServiceExtensions.cs` lines 113–120 and confirm
   the comment now matches the spec.

## 5. Out of scope

- **New dialogs.** This spec doesn't add or modify any dialog components — it only fixes
  documentation and one code comment.
- **Shell dialogs.** `DialogShellBase` / `ShowShellDialogAsync` are already correctly
  documented and unaffected.
- **Architecture test changes.** The existing guard is sufficient; no new tests needed.

