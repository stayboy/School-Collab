# Dialog Container Consolidation Plan

Status: **implemented** — Phase 1 (shell infrastructure + 8 bUnit tests) and Phase 2 (both dialog containers migrated, 6 call sites updated) are complete. Full solution builds; all 33 unit tests pass. Deviations from the original draft are recorded inline below (notably: the `new()` constraint on `TModel` was dropped in favour of a fail-fast `InvalidOperationException`, since the real form models are records with primary constructors; and an optional `SavingText` virtual/parameter was added to preserve the Create-mode "Creating..." in-flight label).

Scope: **Pattern A only** — the typed form-dialog *containers*
(`IDialogContentComponent<TData>` dialogs). The confirmation-prompt
sweep and the wizard-helper dedup are adjacent ergonomics improvements
that do not share the "opening / closing / passing results" structure
this plan targets; they are recorded as out-of-scope in §1 and may be
pursued separately.

This plan is the frontend counterpart of the
`messaging-consolidation-plan` and
`outbox-message-configuration-consolidation-plan` efforts: a shared
reusable component that absorbs the boilerplate duplicated across the
existing dialog containers.

## 1. Background and Goals

The admin apps have two custom dialog containers, both implementing
`IDialogContentComponent<TData>`:

- `src/SchoolCollab.Admin.Shared/Components/CodedValueDialog.razor`
  (Create / Override / RemoveOverride for the Coded Values wizard)
- `src/Settings/SchoolCollab.Settings.Admin/Components/Pages/CodedValues/AttributeDefinitionDialog.razor`
  (Add / Edit an attribute definition on a coded value)

Consumer call sites:

| Dialog | Call sites |
|---|---|
| `CodedValueDialog` | `GradeLevelWizard.razor` — `OpenOverrideDialogAsync`, `OpenSubjectOverrideDialogCoreAsync`, `OpenCreateDialogAsync`, `OpenCreateSubjectDialogAsync` (4 sites) |
| `AttributeDefinitionDialog` | `Edit.razor` — `OpenAddDefinitionDialog`, `EditDefinitionDialog` (2 sites) |

Every call site looks like this (the **opening** + **passing results**
boilerplate, ~10 lines):

```csharp
var data = new CodedValueDialogData(Mode: "Override", ParentId: ..., CodedValue: ...);
var dialog = await DialogService.ShowDialogAsync<CodedValueDialog, CodedValueDialogData>(
    data,
    new DialogParameters
    {
        Title = "Override Grade Name",
        PrimaryAction = null,
        SecondaryAction = null,
        Width = "420px",
        PreventDismissOnOverlayClick = true,
    });
var result = await dialog.Result;
if (!result.Cancelled && result.Data is CodedValueDialogResult cvResult) { ... }
```

And inside each dialog body, the **closing** boilerplate:

```csharp
// In every dialog:
private bool _saving;
private string? _error;
private async Task CancelAsync() => await Dialog.CancelAsync();
// ... and at each success path:
await Dialog.CloseAsync(new CodedValueDialogResult(created));
```

### What is duplicated

| Concern | Where | Count |
|---|---|---|
| `Dialog.CloseAsync(new XxxDialogResult(...))` inside the dialog body | `CodedValueDialog` (×3: Create / Override / RemoveOverride), `AttributeDefinitionDialog` (×1) | **4** |
| `private async Task CancelAsync() => await Dialog.CancelAsync();` | both dialogs | **2** |
| `_saving` + `_error` + `FluentMessageBar Intent="MessageIntent.Error"` inside the dialog body | both dialogs | **2** |
| `.button-row` flex CSS + `.dialog-container` padding CSS | both dialogs | **2** |
| `new DialogParameters { Title, PrimaryAction = null, SecondaryAction = null, Width, PreventDismissOnOverlayClick = true }` | `GradeLevelWizard.razor` (×4), `Edit.razor` (×2) | **6** |
| `if (!result.Cancelled && result.Data is XxxDialogResult ...)` | same 6 call sites | **6** |
| Per-dialog `XxxDialogData` / `XxxDialogResult` record pairs | `CodedValueDialogData.cs`, `AttributeDefinitionDialogTypes.cs` | **2 pairs** |

### Why this is duplication, not "intentional per-dialog code"

- The two dialogs are byte-identical in their *outer shell*: the
  `<div class="dialog-container">` wrapper, the error `FluentMessageBar`,
  the Cancel/Save `.button-row`, the `.dialog-container` / `.button-row`
  / `.mb-3` CSS, the `CancelAsync` helper, and the close-vs-cancel
  plumbing. The *form fields* are the only thing that differs.
- The six `DialogParameters` blocks differ only in `Title` and `Width`.
  The other four fields (`PrimaryAction = null`, `SecondaryAction = null`,
  `PreventDismissOnOverlayClick = true`) are constant across every site.
- The `if (!result.Cancelled && result.Data is XxxDialogResult ...)`
  triplet is a verbatim copy modulo the result type name.

### Goals

1. **One reusable dialog-shell component** lives in
   `SchoolCollab.Admin.Shared` and absorbs the **closing** boilerplate
   (`CancelAsync`, `CloseAsync` with a typed result, `_saving`/`_error`,
   the error `FluentMessageBar`, the Cancel/Save footer, the CSS).
   Individual dialogs contribute only the *form fields* and a
   `SubmitAsync` hook.
2. **One `IDialogService` extension** (`ShowShellDialogAsync`) absorbs
   the **opening** boilerplate (the constant `DialogParameters` block,
   `await dialog.Result`, the `result.Cancelled` check, the
   `result.Data is XxxDialogResult` type-test) and returns the typed
   result or `null` on cancel.
3. **One generic result-payload pair** (`DialogShellData<TModel>` /
   `DialogShellResult<TResult>`) replaces the per-dialog
   `XxxDialogData` / `XxxDialogResult` records so the **passing
   results** path is typed once, not per dialog.
4. No call site has to know about `DialogParameters`,
   `result.Cancelled`, or `result.Data is XxxDialogResult`.
5. No new server endpoint, no new package, no FluentUI version bump —
   purely a frontend shared-library consolidation.

### Non-goals

- **Confirmation-prompt sweep.** The 9 `ShowConfirmationAsync` sites
  (delete / recover / activate / complete prompts across `Index.razor`,
  `Children.razor`, `GradeLevels.razor`, `Periods.razor`, `Subjects.razor`)
  have no custom opening code, no custom closing code, and no typed
  result passing — they already use FluentUI's built-in reusable
  helper. A `ConfirmAsync` one-liner wrapper is a real but marginal
  ergonomics gain and is **out of scope** for this plan. Filed for a
  future small PR if desired.
- **Wizard-helper dedup.** `OpenCreateDialogAsync` and
  `OpenCreateSubjectDialogAsync` in `GradeLevelWizard.razor` are
  near-duplicate *caller* methods, not dialog-container shared
  features. **Out of scope.**
- **Splitting `CodedValueDialog` into separate `Create`/`Override`
  components.** The two modes have different form fields, so a future
  split is cleaner than the current `string Mode` discrimination — but
  it is a behavior-shape change, not a boilerplate extraction. **Out of
  scope.** This plan keeps the existing `string Mode` field verbatim.
- Changing the dialog UX (header, footer, widths, button text).
  Individual dialogs still own the labels and copy.
- Replacing `IDialogService` with a custom overlay. The consolidation
  is *on top of* the existing FluentUI dialog provider, not a
  replacement for it.
- Generic form-rendering support (a `RenderTreeBuilder`-based form
  factory). The shell is for the modal-form shape the two existing
  dialogs share, not for free-form content dialogs.

## 2. Target Final Layout

```
src/SchoolCollab.Admin.Shared/Components/Dialogs/        NEW
├── DialogShellData.cs                                   — DialogShellData<TModel>, DialogShellResult<TResult> records
├── DialogShellBase.cs                                   — abstract base: logic only (Content, Dialog cascade, saving/error, SubmitAsync hook, HandleSubmit/HandleCancel)
├── DialogShellFooter.razor                              — shared markup: error message bar + Cancel/Save footer + CSS
└── DialogServiceExtensions.cs                           — ShowShellDialogAsync

src/SchoolCollab.Admin.Shared/Components/
├── CodedValueDialog.razor                               — refactored: @inherits DialogShellBase<...>, renders fields + <DialogShellFooter/>
└── CodedValueDialogData.cs                              — DELETED (replaced by CodedValueFormModel + DialogShellData<>)

src/Settings/SchoolCollab.Settings.Admin/Components/Pages/CodedValues/
├── AttributeDefinitionDialog.razor                      — refactored: @inherits DialogShellBase<...>, renders fields + <DialogShellFooter/>
└── AttributeDefinitionDialogTypes.cs                    — AttributeDefinitionDialogData DELETED; AttributeDefinitionResult / DataTypeOption / ParentCodedValueOption KEPT (dialog-internal)
```

No changes to `GradeLevelWizard.razor` or `Edit.razor` beyond
swapping the 6 call sites to `ShowShellDialogAsync`. No changes to any
confirmation-prompt site.

## 3. Phase Breakdown

### Phase 1 — Shell infrastructure + tests in `SchoolCollab.Admin.Shared`

**Scope:** New files only. No existing code is changed. bUnit tests
are folded into this phase (not deferred) because the
`SchoolCollab.Admin.Tests.Unit` project already has bUnit + Moq +
FluentAssertions and a `BunitContext` base pattern.

#### `Dialogs/DialogShellData.cs`

```csharp
namespace SchoolCollab.Admin.Shared.Components.Dialogs;

/// <summary>
/// Payload passed into a <see cref="DialogShellBase{TModel, TResult}"/>
/// dialog. The form model is opaque to the shell.
/// </summary>
public sealed record DialogShellData<TModel>(TModel Model)
    where TModel : class;

/// <summary>
/// Wrapper the shell returns via Dialog.CloseAsync. Lets
/// <see cref="DialogServiceExtensions.ShowShellDialogAsync"/>
/// unwrap the success payload without the consumer type-testing.
/// </summary>
public sealed record DialogShellResult<TResult>(TResult Value)
    where TResult : class;
```

#### `Dialogs/DialogShellBase.cs` — logic only, no markup

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace SchoolCollab.Admin.Shared.Components.Dialogs;

/// <summary>
/// Base for "form" dialogs. Owns the shared logic: the
/// <see cref="IDialogContentComponent{DialogShellData{TModel}}"/>
/// contract, the <see cref="FluentDialog"/> cascade, the
/// <c>_saving</c>/<c>_error</c> state, the submit→close and cancel
/// plumbing.
///
/// A derived dialog (a <c>.razor</c> that does
/// <c>@inherits DialogShellBase&lt;TModel, TResult&gt;</c>) provides:
///   - its form fields in markup, bound to <see cref="Model"/>;
///   - a <c>&lt;DialogShellFooter&gt;</c> at the end of its markup for
///     the shared error bar + Cancel/Save footer;
///   - an override of <see cref="SubmitAsync"/>: the side effect.
///     Return non-null to close the dialog with that result; return
///     null to keep it open (the derived dialog is responsible for
///     setting <see cref="Error"/> in that case); throw to surface the
///     exception message in the error bar and keep the dialog open.
///
/// This class has NO markup of its own — it is a plain C# abstract
/// class so it composes cleanly with FluentUI's
/// <c>ShowDialogAsync&lt;TComponent, TData&gt;</c> hosting model, which
/// renders <c>TComponent</c> as the dialog's entire content.
/// </summary>
public abstract class DialogShellBase<TModel, TResult>
    : ComponentBase, IDialogContentComponent<DialogShellData<TModel>>
    where TModel : class, new()
    where TResult : class
{
    [Parameter] public DialogShellData<TModel> Content { get; set; } = default!;
    [CascadingParameter] public FluentDialog Dialog { get; set; } = default!;

    private TModel? _model;
    private bool _saving;
    private string? _error;

    protected TModel Model => _model ??= Content?.Model ?? new TModel();
    protected bool Saving => _saving;
    protected string? Error => _error;

    /// <summary>Label on the Submit button. Default: "Save".</summary>
    protected virtual string SubmitText => "Save";

    /// <summary>Hook for derived dialogs to hydrate the model from typed content.</summary>
    protected virtual void OnModelInitialized(TModel model) { }

    /// <summary>
    /// Submits the form. Return non-null to close the dialog with the
    /// result; null to keep the dialog open (and set <see cref="Error"/>);
    /// throw to surface the message and keep the dialog open.
    /// </summary>
    protected abstract Task<TResult?> SubmitAsync(TModel model);

    protected override void OnInitialized() => OnModelInitialized(Model);

    protected async Task HandleSubmitAsync()
    {
        if (_saving) return;
        _saving = true;
        _error = null;
        try
        {
            var result = await SubmitAsync(Model);
            if (result is not null)
            {
                await Dialog.CloseAsync(new DialogShellResult<TResult>(result));
            }
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _saving = false;
        }
    }

    protected Task HandleCancelAsync() => Dialog.CancelAsync();
}
```

#### `Dialogs/DialogShellFooter.razor` — shared markup child

A small presentational component the derived dialog places at the end
of its markup. It owns the error message bar, the Cancel/Save button
row, and the `.button-row` / `.mb-3` CSS — the markup that was
duplicated across both dialogs.

```razor
@if (!string.IsNullOrEmpty(Error))
{
    <FluentMessageBar Intent="MessageIntent.Error" class="mb-3">@Error</FluentMessageBar>
}

<div class="button-row">
    <FluentButton Appearance="Appearance.Outline"
                  OnClick="OnCancel"
                  Disabled="@Saving">
        Cancel
    </FluentButton>
    <FluentButton Type="ButtonType.Submit"
                  Appearance="Appearance.Accent"
                  Disabled="@Saving"
                  @onclick="OnSubmit">
        @(Saving ? "Saving..." : SubmitText)
    </FluentButton>
</div>

<style>
    .button-row {
        display: flex;
        justify-content: flex-end;
        gap: 8px;
        margin-top: 16px;
        flex-wrap: wrap;
    }
    .mb-3 { margin-bottom: 12px; }
</style>

@code {
    [Parameter] public bool Saving { get; set; }
    [Parameter] public string? Error { get; set; }
    [Parameter] public string SubmitText { get; set; } = "Save";
    [Parameter] public EventCallback OnSubmit { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }
}
```

> **Why a child component, not a base `.razor` with a `RenderBody`
> hole?** `ShowDialogAsync<TComponent, TData>(data, parameters)`
> renders `TComponent` as the dialog's entire content; the host sets
> only `Content` and the `DialogParameters`. Nobody supplies a
> `RenderBody` parameter to the dialog, so a base-`.razor`-with-a-hole
> would render an empty body. The logic-only base + footer-child
> design keeps each derived dialog self-contained and compatible with
> the hosting model. The repo already has a precedent for generic
> razor components (`LandingPage<TItem>`), but none for a generic
> component implementing `IDialogContentComponent<>`; this design
> avoids that unproven path entirely.

#### `Dialogs/DialogServiceExtensions.cs`

```csharp
using Microsoft.FluentUI.AspNetCore.Components;

namespace SchoolCollab.Admin.Shared.Components.Dialogs;

public static class DialogServiceExtensions
{
    /// <summary>
    /// Shows a shell dialog and returns the typed result, or null if
    /// cancelled. Replaces the verbose
    /// <c>ShowDialogAsync / await Result / cast Data</c> pattern.
    ///
    /// The four constant DialogParameters fields
    /// (PrimaryAction = null, SecondaryAction = null,
    /// PreventDismissOnOverlayClick = true) match what every existing
    /// call site passes today. <paramref name="parameterOverrides"/>
    /// can override any of them for a future dialog that wants
    /// native FluentUI primary/secondary actions.
    /// </summary>
    public static async Task<TResult?> ShowShellDialogAsync<TComponent, TModel, TResult>(
        this IDialogService dialogService,
        TModel model,
        string title,
        string width = "420px",
        Dictionary<string, object>? parameterOverrides = null)
        where TComponent : ComponentBase, IDialogContentComponent<DialogShellData<TModel>>
        where TModel : class
        where TResult : class
    {
        var parameters = new DialogParameters
        {
            Title = title,
            Width = width,
            PrimaryAction = null,
            SecondaryAction = null,
            PreventDismissOnOverlayClick = true,
        };
        if (parameterOverrides is not null)
        {
            foreach (var kv in parameterOverrides)
            {
                parameters[kv.Key] = kv.Value;
            }
        }

        var dialog = await dialogService.ShowDialogAsync<TComponent, DialogShellData<TModel>>(
            new DialogShellData<TModel>(model), parameters);
        var result = await dialog.Result;
        if (result.Cancelled) return null;
        return result.Data is DialogShellResult<TResult> r ? r.Value : null;
    }
}
```

> **Note on `DialogParameters` construction.** `DialogParameters`
> exposes typed properties (`Title`, `Width`, `PrimaryAction`,
> `SecondaryAction`, `PreventDismissOnOverlayClick`) backed by a
> dictionary. Object-initializer property syntax (as used above and in
> every existing call site) compiles. This avoids the unverified
> `new DialogParameters(DefaultParams)` copy-constructor path; the
> Phase 1 build confirms the property names against the installed
> FluentUI version.

#### Tests (folded in) — `tests/SchoolCollab.Admin.Tests.Unit/DialogShellTests.cs`

bUnit + Moq, mirroring the existing `LandingPageTests` /
`GradeLevelWizardTenancyTests` style. Covers the shell contract in §7:

- `SubmitAsync_returns_non_null_closes_dialog_with_result` —
  `HandleSubmitAsync` invokes the derived `SubmitAsync`, and on a
  non-null return calls `Dialog.CloseAsync` with a
  `DialogShellResult<TResult>` wrapping the value.
- `SubmitAsync_returns_null_keeps_dialog_open_no_error` —
  `Dialog.CloseAsync` is NOT called; `Saving` resets to false.
- `SubmitAsync_throws_surfaces_message_keeps_dialog_open` —
  `Error` equals `ex.Message`; `Dialog.CloseAsync` is NOT called.
- `HandleCancel_calls_Dialog_CancelAsync` —
  `Dialog.CancelAsync` invoked exactly once.
- `ShowShellDialogAsync_cancelled_returns_null` —
  mock `IDialogService` returns a cancelled result; extension returns
  null.
- `ShowShellDialogAsync_success_returns_typed_result` —
  mock returns `DialogShellResult<TResult>`; extension unwraps it.
- `ShowShellDialogAsync_wrong_result_type_returns_null` —
  mock returns a non-`DialogShellResult<TResult>` data object;
  extension returns null (defensive).

**Acceptance:**
- `dotnet build SchoolCollab.sln` succeeds; the new types compile
  under nullable analysis.
- The 7 shell tests above pass.
- No existing project behaviour changes (the new code is not yet
  referenced by any call site).
- `@inherits DialogShellBase<TModel, TResult>` in a throwaway test
  dialog compiles and renders (the spike that proves the hosting
  composition in the note above).

### Phase 2 — Migrate both dialog containers (one PR)

**Scope:** Refactor `CodedValueDialog` and `AttributeDefinitionDialog`
to inherit the shell; collapse the 6 call sites to one-liners. The two
migrations are independent and mechanical, so they ship in one PR but
are reviewed as two subsections.

#### 2a — `CodedValueDialog.razor`

The current file mixes two modes (`Create` / `Override`) inside one
component. The refactored version keeps the mode-based structure (the
form fields differ between modes) but drops the outer shell. The
"Reset to default" button in Override mode stays inside the body — it
is a non-shell secondary action rendered above the
`<DialogShellFooter>`.

**New model** (in the dialog's `@code` block or a companion
`CodedValueDialogTypes.cs`):

```csharp
public sealed record CodedValueFormModel(
    string Mode,
    Guid? ParentId,
    CodedValueDto? CodedValue,
    bool HasOverride = false)
{
    // Bindable form fields — MUST be get; set; (not init) so that
    // EditForm's @bind-Value can write to them at runtime on each
    // keystroke. The positional record params above are init (set
    // once at construction), which is correct for Mode/ParentId/
    // CodedValue/HasOverride.
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? DisplayOrder { get; set; }

    public static CodedValueFormModel ForCreate(Guid? parentId) =>
        new("Create", parentId, null);
    public static CodedValueFormModel ForOverride(CodedValueDto cv, bool hasOverride) =>
        new("Override", cv.ParentId, cv, hasOverride);
}
```

**Refactored dialog** (sketch):

```razor
@inherits DialogShellBase<CodedValueFormModel, CodedValueDto>
@inject CodedValuesApiClient CodedValuesApi

<div class="dialog-container">
    @if (Model.Mode == "Create")
    {
        <EditForm Model="Model" OnValidSubmit="HandleSubmitAsync">
            <DataAnnotationsValidator />
            <FluentTextField @bind-Value="Model.Code" Label="Code *" Required class="full-width mb-3" />
            <FluentTextField @bind-Value="Model.Name" Label="Name *" Required class="full-width mb-3" />
            <FluentTextField @bind-Value="Model.Description" Label="Description" Rows="2" class="full-width mb-3" />
            <FluentNumberField @bind-Value="Model.DisplayOrder" Label="Display Order" class="full-width mb-3" />
        </EditForm>
    }
    else
    {
        <EditForm Model="Model" OnValidSubmit="HandleSubmitAsync">
            <DataAnnotationsValidator />
            <p class="hint">Overriding: <strong>@GetDefaultName()</strong></p>
            <FluentTextField @bind-Value="Model.Name" Label="Override Name" Placeholder="@GetPlaceholder()" class="full-width mb-3" />
            <FluentTextArea @bind-Value="Model.Description" Label="Description (optional)" Rows="2" class="full-width mb-3" />
            @if (_hasOverride)
            {
                <FluentButton Appearance="Appearance.Neutral" OnClick="RemoveOverrideAndCloseAsync" Disabled="@_removing" class="mb-3">
                    Reset to default
                </FluentButton>
            }
        </EditForm>
    }

    <DialogShellFooter Saving="@Saving"
                       Error="@Error"
                       SubmitText="@(Model.Mode == "Create" ? "Create" : "Save Override")"
                       OnSubmit="HandleSubmitAsync"
                       OnCancel="HandleCancelAsync" />
</div>

@code {
    private bool _hasOverride;
    private bool _removing;
    private CodedValueDto? _codedValue;

    protected override void OnModelInitialized(CodedValueFormModel model)
    {
        _codedValue = model.CodedValue;
        _hasOverride = model.HasOverride;
        // Name/Description left empty for fresh override entry; the
        // placeholder shows the default name as a hint (unchanged UX).
    }

    protected override async Task<CodedValueDto?> SubmitAsync(CodedValueFormModel model)
    {
        if (model.Mode == "Create")
        {
            if (string.IsNullOrWhiteSpace(model.Code) || string.IsNullOrWhiteSpace(model.Name))
            {
                Error = "Code and Name are required.";
                return null;
            }
            await CodedValuesApi.CreateAsync(new CreateCodedValueRequest(
                model.Code.Trim().ToUpperInvariant(),
                model.Name.Trim(),
                model.Description,
                model.ParentId,
                model.DisplayOrder ?? 0));
            var created = await CodedValuesApi.GetByCodeAsync(
                model.Code.Trim().ToUpperInvariant(), model.ParentId);
            if (created is null)
            {
                Error = "Created but could not fetch the new coded value.";
                return null;
            }
            return created;
        }

        // Override mode
        if (_codedValue is null) { Error = "No coded value selected."; return null; }
        var result = await CodedValuesApi.UpsertOverrideAsync(
            _codedValue.Id,
            string.IsNullOrWhiteSpace(model.Name) ? null : model.Name.Trim(),
            string.IsNullOrWhiteSpace(model.Description) ? null : model.Description);
        _hasOverride = true;
        return result;
    }

    // Non-shell secondary action: closes the dialog directly with the
    // re-fetched coded value (same behavior as today's RemoveOverrideAsync).
    private async Task RemoveOverrideAndCloseAsync()
    {
        if (_codedValue is null || _removing) return;
        _removing = true; Error = null;
        try
        {
            await CodedValuesApi.RemoveOverrideAsync(_codedValue.Id);
            _hasOverride = false;
            var original = await CodedValuesApi.GetByIdAsync(_codedValue.Id);
            if (original is not null)
                await Dialog.CloseAsync(new DialogShellResult<CodedValueDto>(original));
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { _removing = false; }
    }

    private string GetDefaultName() => _codedValue?.DefaultName ?? _codedValue?.Name ?? string.Empty;
    private string GetPlaceholder()
    {
        var n = GetDefaultName();
        return string.IsNullOrEmpty(n) ? "Enter custom name for your tenant" : $"e.g. {n}";
    }
}
```

> **Note on `Error` writes from `SubmitAsync`.** `Error` is a
> `protected string?` property on the base. The derived dialog assigns
> it directly (e.g. `Error = "Code and Name are required.";`). Because
> `SubmitAsync` runs inside `HandleSubmitAsync`'s try block, a direct
> assignment is surfaced on the next render via `DialogShellFooter`'s
> `Error` parameter. Throwing is the alternative and is also surfaced;
> both paths are covered by Phase 1 tests.

**Collapsed call site** (`GradeLevelWizard.razor` — all 4 sites follow
this shape):

```csharp
private async Task OpenOverrideDialogAsync()
{
    if (!_codedValueIdNullable.HasValue) return;
    if (_codedValueDropdown is not null) await _codedValueDropdown.RefreshAsync();
    var cv = await CodedValuesApi.GetByIdAsync(_codedValueIdNullable.Value);
    if (cv is null) return;

    var result = await DialogService.ShowShellDialogAsync<
        CodedValueDialog, CodedValueFormModel, CodedValueDto>(
        CodedValueFormModel.ForOverride(cv, cv.IsOverridden),
        title: "Override Grade Name");

    if (result is not null)
    {
        if (_codedValueDropdown is not null) await _codedValueDropdown.RefreshAsync();
        _selectedCodedValue = await CodedValuesApi.GetByIdAsync(_codedValueIdNullable.Value);
    }
}
```

**Acceptance (2a):**
- All four `GradeLevelWizard.razor` flows (Create grade, Override
  grade, Create subject, Override subject) work end-to-end.
- `CodedValueDialogData.cs` is deleted; the 4 call sites no longer
  reference `CodedValueDialogData` or `CodedValueDialogResult`.
- No `new DialogParameters { ... }` literal and no
  `result.Data is CodedValueDialogResult` test remain in
  `GradeLevelWizard.razor`.
- Net: `CodedValueDialog.razor` ~ −30 lines; `GradeLevelWizard.razor`
  ~ −50 lines.

#### 2b — `AttributeDefinitionDialog.razor`

**New model** (the dialog reads only `ExistingDefinition` and
`ParentValues`; `Api` and `CodedValueId` stay on the caller and are
NOT carried in the dialog model):

```csharp
public sealed record AttributeDefinitionFormModel(
    CodedValueAttributeDefinitionDto? ExistingDefinition = null,
    CodedValueDto[]? ParentValues = null)
{
    // Bindable — get; set; (not init).
    public string? Key { get; set; }
    public string? DisplayName { get; set; }
    public AttributeDataType DataType { get; set; } = AttributeDataType.Text;
    public string? SourceCode { get; set; }
    public bool IsRequired { get; set; }
    public bool AllowMultiple { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public string? RegexPattern { get; set; }
}
```

`AttributeDefinitionResult` (the `TResult`), `DataTypeOption`, and
`ParentCodedValueOption` (dialog-internal display types) are KEPT —
they are not part of the duplicated opening/closing/result-passing
boilerplate. They move out of `AttributeDefinitionDialogTypes.cs` into
the dialog's `@code` block or a slimmed companion file.

**Collapsed call site** (`Edit.razor` — both sites follow this shape):

```csharp
private async Task OpenAddDefinitionDialog()
{
    if (_item is null) return;
    var parentValues = await Api.GetRootValuesAsync() ?? [];
    var model = new AttributeDefinitionFormModel
    {
        ParentValues = parentValues.Where(p => p.Id != _item.Id).ToArray(),
    };
    var defResult = await DialogService.ShowShellDialogAsync<
        AttributeDefinitionDialog, AttributeDefinitionFormModel, AttributeDefinitionResult>(
        model, title: "Add Attribute Definition", width: "560px");
    if (defResult is not null)
        await SaveDefinitionFromResultAsync(defResult, _loadCts?.Token ?? CancellationToken.None);
}
```

`SaveDefinitionFromResultAsync` is unchanged — it already takes the
`AttributeDefinitionResult` and the caller's `Api`/`_item.Id`.

**Acceptance (2b):**
- Add and Edit attribute definition flows work end-to-end.
- `AttributeDefinitionDialogData` is deleted; the 2 call sites no
  longer reference it. `AttributeDefinitionResult` /
  `DataTypeOption` / `ParentCodedValueOption` are preserved.
- No `new DialogParameters { ... }` literal and no
  `result.Data is AttributeDefinitionResult` test remain in
  `Edit.razor`.
- Net: `AttributeDefinitionDialog.razor` ~ −25 lines; `Edit.razor`
  ~ −25 lines.

## 4. Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| `@inherits DialogShellBase<TModel, TResult>` fails to compile in a `.razor` file (generic base + interface) | Phase 2 blocked. | Phase 1 includes a throwaway test dialog that does exactly this; the spike proves composition before any real dialog is touched. The repo already ships generic razor (`LandingPage<TItem>`); the only new combination is the `IDialogContentComponent<>` interface, which is inherited via standard C# inheritance (no `@implements` re-declaration needed). |
| `DialogShellFooter` is a separate component, so the derived dialog must remember to render it | A dialog could omit the footer and lose Cancel/Save. | The Phase 1 test dialog establishes the canonical placement; the two Phase 2 dialogs are reviewed against it. A future hardening (a `ChildContent`-only shell wrapper) is noted as out-of-scope. |
| Derived `SubmitAsync` writes `Error` directly (protected setter) | Slightly unusual API; a derived dialog could forget to set `Error` on the null-return path. | Documented in the base XML doc and exercised by the Phase 1 `returns_null_keeps_dialog_open` test. Throwing is the alternative and is also handled. |
| `DialogParameters` property names differ across FluentUI versions | Build breaks if a name (`PrimaryAction`, etc.) was renamed. | Phase 1 build confirms against the installed version; the existing call sites already use these exact names today, so they are known-good. |
| Removing `CodedValueDialogData` / `AttributeDefinitionDialogData` is a breaking change for an unknown external consumer | Compile error elsewhere. | Confirmed by grep: the only references are inside the two dialog files and the 6 call sites enumerated in §1, all of which are migrated in Phase 2. |
| The "Reset to default" button closes the dialog outside the `HandleSubmitAsync` path | A second close path exists in `CodedValueDialog`. | Unchanged from today's `RemoveOverrideAsync`; documented as an intentional non-shell secondary action. The result is still wrapped in `DialogShellResult<CodedValueDto>` so `ShowShellDialogAsync` unwraps it uniformly. |

## 5. Estimated Effort

| Phase | Effort | Risk |
|---|---|---|
| 1 — Shell infrastructure + tests | 1.5 days | Low (spike proves the composition) |
| 2 — Migrate both dialogs (2a + 2b) | 1–1.5 days | Low (mechanical) |

**Total: ~2.5–3 days** across 2 PRs (Phase 1 standalone; Phase 2 one PR
with two reviewable subsections).

## 6. Success Criteria

- A new form-style dialog container added to any admin app requires:
  - one `.razor` file that does
    `@inherits DialogShellBase<TModel, TResult>`, renders its form
    fields, and ends with `<DialogShellFooter .../>`;
  - one `TModel` record (bindable fields as `get; set;`);
  - a one-line `ShowShellDialogAsync<...>(model, title)` call from the
    consumer.
- No call site contains `new DialogParameters { ... }`.
- No call site contains `result.Cancelled` (for shell dialogs).
- No call site contains `result.Data is XxxDialogResult`.
- The two existing dialogs each have a single `SubmitAsync` override;
  the `_saving`, `_error`, `CancelAsync`, `CloseAsync` (success path),
  error `FluentMessageBar`, footer button row, and `.button-row` /
  `.dialog-container` CSS live in `DialogShellBase` /
  `DialogShellFooter` only.
- Net line count across the touched files: **−130 lines**
  (CodedValueDialog ~ −30, GradeLevelWizard ~ −50,
  AttributeDefinitionDialog ~ −25, Edit ~ −25).
- All existing flows (Create grade, Override grade, Create subject,
  Override subject, Add/Edit attribute definition) continue to work.
- The 7 Phase 1 shell tests pass.

## 7. Requirements & Acceptance Appendix

Numbered for test extraction (Phase 1). Each acceptance criterion
references the requirement it traces to. The plan body uses the team's
prose style for consistency with sibling docs; this appendix gives the
mechanical Given/When/Then form for the shell contract.

### Requirements

- **FR-1** The shell MUST provide a `SubmitAsync(TModel)` hook that
  derived dialogs override to perform their side effect.
- **FR-2** When `SubmitAsync` returns non-null, the shell MUST close
  the dialog via `Dialog.CloseAsync` with a
  `DialogShellResult<TResult>` wrapping the returned value.
- **FR-3** When `SubmitAsync` returns null, the shell MUST NOT close
  the dialog and MUST reset its busy state.
- **FR-4** When `SubmitAsync` throws, the shell MUST surface
  `ex.Message` in the error display and MUST NOT close the dialog.
- **FR-5** The shell MUST provide a cancel path that invokes
  `Dialog.CancelAsync`.
- **FR-6** `ShowShellDialogAsync` MUST return the unwrapped
  `TResult` on success, `null` on cancel, and `null` if the result
  data is not a `DialogShellResult<TResult>`.
- **FR-7** `ShowShellDialogAsync` MUST apply the four constant
  `DialogParameters` fields (`PrimaryAction = null`,
  `SecondaryAction = null`, `PreventDismissOnOverlayClick = true`,
  plus the caller's `Title` and `Width`).
- **NFR-1** The shell MUST NOT change the existing dialog UX (header,
  footer, widths, button text) — labels and copy stay owned by each
  dialog.
- **NFR-2** The shell MUST NOT introduce a new package dependency or
  FluentUI version bump.

### Acceptance Criteria

- **AC-1** (FR-2) **Given** a derived dialog whose `SubmitAsync`
  returns a non-null `TResult`, **when** `HandleSubmitAsync` runs,
  **then** `Dialog.CloseAsync` is called exactly once with a
  `DialogShellResult<TResult>` whose `Value` equals the returned
  result.
- **AC-2** (FR-3) **Given** a derived dialog whose `SubmitAsync`
  returns null, **when** `HandleSubmitAsync` runs, **then**
  `Dialog.CloseAsync` is NOT called and `Saving` is false after the
  call.
- **AC-3** (FR-4) **Given** a derived dialog whose `SubmitAsync`
  throws an exception with message M, **when** `HandleSubmitAsync`
  runs, **then** `Error` equals M, `Dialog.CloseAsync` is NOT called,
  and `Saving` is false after the call.
- **AC-4** (FR-5) **Given** the cancel handler is invoked, **when**
  `HandleCancelAsync` runs, **then** `Dialog.CancelAsync` is called
  exactly once.
- **AC-5** (FR-6) **Given** a mocked `IDialogService` whose
  `dialog.Result` is cancelled, **when** `ShowShellDialogAsync` is
  called, **then** it returns null.
- **AC-6** (FR-6) **Given** a mocked `IDialogService` whose
  `result.Data` is a `DialogShellResult<T>` wrapping value V,
  **when** `ShowShellDialogAsync` is called, **then** it returns V.
- **AC-7** (FR-6) **Given** a mocked `IDialogService` whose
  `result.Data` is not a `DialogShellResult<T>`, **when**
  `ShowShellDialogAsync` is called, **then** it returns null.
- **AC-8** (FR-7, NFR-1) **Given** a call site invokes
  `ShowShellDialogAsync(model, title: "T", width: "560px")`,
  **when** the dialog is shown, **then** the resulting
  `DialogParameters` have `Title="T"`, `Width="560px"`,
  `PrimaryAction=null`, `SecondaryAction=null`,
  `PreventDismissOnOverlayClick=true`.
- **AC-9** (NFR-2) **Given** the shell is added, **when**
  `dotnet build SchoolCollab.sln` runs, **then** no new
  `<PackageReference>` is added to any project.

### Edge Cases

- **EC-1** A derived dialog's `SubmitAsync` is called while
  `_saving` is already true (double-submit). Expected: the second
  call is ignored (guard at the top of `HandleSubmitAsync`).
- **EC-2** The model passed to `ShowShellDialogAsync` is null.
  Expected: `DialogShellData<TModel>(null)` — the shell's
  `Model` getter falls back to `new TModel()`. (Documented; not a
  recommended call-site pattern.)
- **EC-3** `Dialog.CancelAsync` is invoked while `_saving` is true
  (user clicks Cancel mid-submit). Expected: cancel proceeds
  (cancel is not guarded by `_saving` in the base; the footer's
  Cancel button is `Disabled="@Saving"`, so this is only reachable
  via a programmatic call).
