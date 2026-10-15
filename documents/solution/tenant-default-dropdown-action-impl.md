# Implementation plan: default-tenant actions (dropdown + `/tenants` banner)

**Status:** implemented (2026-10-14, solo) · **Scope:** UI-only, dev/TestAuth mode  
**Spec:** `documents/specs/tenant-default-dropdown-action.md` (implemented)  
**Related ADR:** `documents/solution/admin-default-tenant.md`

---

## 1. What we know today

- The spec `documents/specs/tenant-default-dropdown-action.md` is a **draft in the working tree** (`??`). It has not been implemented yet.
- `src/SchoolCollab.Admin/Components/Layout/DevTenantSwitcher.razor` still ships the two toolbar `FluentButton`s the spec wants removed: `Default` (line 64) and `Clear` (lines 73–75).
- `src/SchoolCollab.Admin/Components/Layout/DevTenantSwitcher.razor.css` styles them with `.dev-tenant-pref-button`.
- `src/Settings/SchoolCollab.Settings.Application/Components/Pages/Tenants/Index.razor` deliberately excludes the per-operator preference; the spec requires adding a page-level clear banner there.
- The API surface already exists:
  - `GET|PUT /api/tenants/me/default` through `TenantsApiClient.GetMyDefaultTenantAsync` / `SetMyDefaultTenantAsync(Guid?)`
  - `AdminDefaultTenantResolver.ResolveAndApplyAsync` and `GetPreferenceAsync`
- The repo has a sanctioned confirm-dialog helper: `DialogServiceExtensions.ShowConfirmDialogAsync` (`src/SchoolCollab.Admin.Shared/Components/Dialogs/DialogServiceExtensions.cs:166`).

**Conclusion:** we did not miss the spec — the buttons are still there because this rework is a separate, unstarted piece of work.

---

## 2. Spec expectations recap

| ID | Requirement |
|----|-------------|
| **FR-1** | Remove both toolbar buttons and all supporting code from `DevTenantSwitcher`. |
| **FR-2** | Add an action zone inside the dropdown: separator + `Set as default` entry. The option uses a sentinel value (e.g. `__set-my-default__`) and `TenantOption` gains an `IsAction` flag. |
| **FR-3** | The action zone shows **only** when a real tenant is selected and that tenant differs from the recorded preference. |
| **FR-4** | Selecting the action entry must **never** become the selected value; instead it opens a confirm dialog (title `Set default tenant`, message `Open the admin portal on '<tenant name>' by default from now on?`, primary `Set as default`, secondary `Cancel`). Cancel/dismiss does nothing. |
| **FR-5** | On confirm: `SetMyDefaultTenantAsync(selectedTenantId)`, `ResolveAndApplyAsync(PlatformDefaultOf(_tenants))`, then `Nav.NavigateTo(Nav.Uri, forceLoad: true)`. The explicit pick slot is untouched. |
| **FR-6** | `Set as default` is never the dropdown's selected value before, during, or after the dialog. |
| **FR-7** | `/tenants` page: load `GET /api/tenants/me/default` in parallel, show a banner `Your default tenant: <name>` with a `Clear` button when a preference exists; clear calls `SetMyDefaultTenantAsync(null)`, hides the banner, no reload. |

---

## 3. Proposed implementation

### 3.1 `DevTenantSwitcher.razor` — dropdown-only actions

1. **Delete the two `FluentButton`s** and their supporting members:
   - `SetDefaultTitle`
   - `TargetTenantId`
   - `SetAsMyDefaultAsync`
   - `ClearMyDefaultAsync`
   - `_busy`
2. **Add `DialogService` injection** (`@inject IDialogService DialogService`) so the sanctioned confirm dialog can be shown.
3. **Extend `TenantOption`** with an `IsAction` flag and a new sentinel for the action entry:
   ```csharp
   public sealed record TenantOption(string Label, string Value, bool IsAction = false);
   private const string SetDefaultActionValue = "__set-my-default__";
   ```
4. **Build options with the action zone conditionally**:
   - Determine `_preference` from `ResolveDefaultsAsync` already loaded.
   - Determine whether the currently selected value is a real tenant and differs from `_preference`.
   - If so, append `new TenantOption("──── my default ────", SetDefaultActionValue + "-sep", IsAction: true)` (separator, disabled) and `new TenantOption("Set as default", SetDefaultActionValue, IsAction: true)` (action entry, enabled).
   - Use a style/CSS class to visually distinguish the separator (e.g. `font-style: italic; color: var(--neutral-foreground-rest)`), and the action entry (e.g. bold or accent color).
5. **Intercept selection of the action entry in `OnSelectionChanged`**:
   ```csharp
   if (option is { IsAction: true, Value: SetDefaultActionValue })
   {
       // Restore selection to the previously selected real tenant before any await.
       _selectedOption = previousRealSelection;
       var tenantId = Guid.Parse(previousRealSelection.Value);
       var confirmed = await DialogService.ShowConfirmDialogAsync(
           $"Open the admin portal on '{TenantNameOf(tenantId)}' by default from now on?",
           primaryText: "Set as default",
           title: "Set default tenant");
       if (confirmed)
       {
           await TenantsApi.SetMyDefaultTenantAsync(tenantId);
           await DefaultTenant.ResolveAndApplyAsync(
               AdminDefaultTenantResolver.PlatformDefaultOf(_tenants!), CancellationToken.None);
           Nav.NavigateTo(Nav.Uri, forceLoad: true);
       }
       return;
   }
   ```
   - Store the previous real selection in a field before changing `_selectedOption` so the restore is synchronous.
   - For the separator value (e.g. `__set-my-default__-sep`) treat selection as a no-op and restore.
6. **Remove `.dev-tenant-pref-button` rules** from `.razor.css`. Add rules for `.dev-tenant-action-separator` and `.dev-tenant-action-entry`.
7. **Keep the existing width** (`Width="180px"`) unless the new labels overflow — then consult the `input-width-scale` skill.

### 3.2 `Settings.Application/Components/Pages/Tenants/Index.razor` — clear banner

1. Add a field for the preference and its name:
   ```csharp
   private Guid? _preference;
   private string? _preferenceError;
   ```
2. Load it in parallel with the tenant list:
   ```csharp
   private async Task LoadAsync()
   {
       try
       {
           var listTask = Api.ListTenantsAsync();
           var prefTask = Api.GetMyDefaultTenantAsync();
           await Task.WhenAll(listTask, prefTask);
           _items = await listTask;
           _preference = await prefTask;
           _error = null;
           _preferenceError = null;
       }
       catch (Exception ex)
       { ... }
   }
   ```
   - A failed preference read must be non-fatal: log and set `_preference = null`.
3. Render the banner above `LandingPage` when `_preference` is not null:
   ```razor
   @if (_preference is { } pref)
   {
       <FluentMessageBar Title="Your default tenant"
                         Intent="Intent.Info"
                         AllowDismiss="false">
           @PreferenceDisplayName(pref)
           <FluentButton Appearance="Appearance.Stealth"
                         OnClick="@ClearPreferenceAsync">Clear</FluentButton>
       </FluentMessageBar>
   }
   ```
   - Resolve name from `_items`: `Guid.Empty` → `"system / no tenant"`; unknown id → raw id string.
4. Implement `ClearPreferenceAsync`:
   ```csharp
   private async Task ClearPreferenceAsync()
   {
       try
       {
           await Api.SetMyDefaultTenantAsync(null);
           _preference = null;
       }
       catch (Exception ex)
       {
           Logger.LogError(ex, "Failed to clear default tenant preference");
           _error = ex.Message;
       }
   }
   ```
5. Update the header comment: the preference is now surfaced on this page for clearing.

### 3.3 `documents/solution/admin-default-tenant.md` documentation updates

- §3.4 Surfaces: replace "offers Default and Clear buttons" with "dropdown `Set as default` action + `/tenants` clear banner".
- §5 Limits: replace the "Clearing a preference is required … Hence the Clear action" bullet to point at the `/tenants` banner.

---

## 4. Test plan

### 4.1 Rework `tests/SchoolCollab.Admin.Tests.Unit/DevTenantSwitcherTests.cs`

**Remove** (they test the old buttons):
- `MakeDefault_RecordsTheCurrentTenant_AndPublishesIt`
- `ClearIsNotOfferedWhenNoPreferenceExists`
- `ClearIsOfferedWhenAPreferenceExists`

**Keep/adjust**:
- `RendersSystemDefaultAndEveryTenant` — extend assertions to verify the action zone is **absent** when no real tenant is selected (default/system case).
- `WithNoExplicitPick_SelectsTheResolvedDefaultEntry`
- `WithAnExplicitSystemPick_SelectsTheSystemEntry`
- `WithAnExplicitTenantPick_SelectsThatTenant`
- `WithAPreference_TheDefaultEntryNamesThePreference`

**Add** (mapped to spec FRs):

| Test | Spec | What to assert |
|------|------|----------------|
| `ActionZone_Hidden_ForSystemAndDefaultEntries` | FR-3 | Selecting `(system / no tenant)` or `(default: X)` yields only the base option list; no separator, no action entry. |
| `ActionZone_Shown_ForRealTenant_WhenPreferenceDiffers` | FR-3 | With explicit real tenant selected and no preference (or different preference), the list ends with separator + `Set as default`. |
| `ActionZone_Hidden_WhenSelectedTenantIsAlreadyPreference` | FR-3 | If preference equals selected tenant, no action zone. |
| `ActionZone_NeverSelected_DuringAndAfterDialog` | FR-4, FR-6 | Clicking `Set as default` leaves `SelectedOption` on the tenant entry while the dialog is open and after cancel. |
| `SetDefault_Cancel_DoesNotCallPut` | FR-4 | Mock `IDialogService` to return cancelled; assert no `PUT /api/tenants/me/default` call recorded. |
| `SetDefault_Confirm_CallsPutWithSelectedTenant_AndPublishes_AndReloads` | FR-5 | Mock dialog confirmed; assert PUT body contains selected tenant id, `IDevTenantSelection` applied slot updated, `NavigationManager.NavigateTo(..., forceLoad: true)` invoked. |

**Test infrastructure needed in `DevTenantSwitcherTests`**:
- Add `IDialogService` mock helper (same pattern as `AssignmentIndexBunitTests.cs:570–613`).
- Add `NavigationManager` mock or capture the navigate call (the existing `Nav.NavigateTo` in `OnSelectionChanged` already requires this in current tests; check if it is mocked today).
- For the "never selected" assertion, drive the select through `FluentSelect` change events (see `dropdown-ui` skill) or call `OnSelectionChanged` directly via `InvokeAsync` after locating the option element.

### 4.2 New `tests/SchoolCollab.Settings.Tests.Unit/TenantsIndexTests.cs`

Create a new bUnit test class for the `/tenants` page. It needs:
- `TenantsApiClient` mock or scripted `HttpMessageHandler`.
- `ILogger<Index>` stub.
- `LandingPage` / `FluentUI` components registered (`Services.AddFluentUIComponents()`).

| Test | Spec | What to assert |
|------|------|----------------|
| `Banner_Hidden_WhenNoPreference` | FR-7 | No preference → no banner markup. |
| `Banner_ShowsTenantName_WhenPreferenceIsRealTenant` | FR-7 | Preference = Alpha → banner shows `Your default tenant: Alpha` and a `Clear` button. |
| `Banner_ShowsSystemLabel_WhenPreferenceIsGuidEmpty` | FR-7 | Preference = `Guid.Empty` → banner shows `system / no tenant`. |
| `Banner_ShowsRawId_WhenPreferenceUnknown` | FR-7 | Preference = unknown Guid → banner falls back to raw id string. |
| `ClearPreference_PutsNull_AndHidesBanner` | FR-7 | Click `Clear`; assert `PUT /api/tenants/me/default` body is `null` (or empty object per DTO), banner hidden, grid untouched. |
| `FailedPreferenceRead_DegradesToNoBanner` | FR-7 | `GET /api/tenants/me/default` fails; page still renders the grid and no banner. |
| `FailedClear_SurfacesErrorStrip` | FR-7 | `PUT /api/tenants/me/default` fails; `_error` strip appears. |

**Note:** the existing `SchoolCollab.Settings.Tests.Unit` project must be inspected for its `BunitContext` / `TestContext` base class and whether it already references `SchoolCollab.Settings.Application`.

---

## 5. Execution order

1. Read the `dialog-ui`, `dropdown-ui`, `blazor-css-isolation`, and `test-dialog-opener-components` (for dialog mocking) skills.
2. Implement `DevTenantSwitcher` changes.
3. Run `dotnet build src/SchoolCollab.Admin/SchoolCollab.Admin.csproj`; fix compile errors.
4. Update `DevTenantSwitcherTests`.
5. Implement `/tenants` banner.
6. Run `dotnet build src/Settings/SchoolCollab.Settings.Application/...`.
7. Create `TenantsIndexTests`.
8. Run `dotnet test tests/SchoolCollab.Admin.Tests.Unit` and `dotnet test tests/SchoolCollab.Settings.Tests.Unit`.
9. Run `dotnet build SchoolCollab.slnx`.
10. Update `documents/solution/admin-default-tenant.md`.
11. Flip spec status line to implemented and add verification results.

---

## 6. Verification gates

| Gate | Must be |
|------|---------|
| `dotnet build SchoolCollab.slnx` | 0 errors |
| `dotnet test tests/SchoolCollab.Admin.Tests.Unit` | 0 failures |
| `dotnet test tests/SchoolCollab.Settings.Tests.Unit` | 0 failures |
| UI assertions | Header select stands alone (no `Default`/`Clear` buttons); dropdown action zone behaves per §3; `/tenants` banner appears when expected. |

---

## 7. Open risks

- **Dropdown interception timing:** FluentUI `FluentSelect` may fire `SelectedOptionChanged` before or after DOM selection settles. The restore-to-previous selection must be synchronous before any `await`; verify in bUnit that `_selectedOption` does not briefly equal the action sentinel.
- **Forced reload in tests:** `Nav.NavigateTo(..., forceLoad: true)` needs a mock `NavigationManager` in the test, otherwise Blazor will throw.
- **Separator disabled vs. selectable:** A disabled `FluentOption` cannot be selected but also may not be styleable. If the repo uses the native `<fluent-option disabled>` attribute, styling via scoped CSS should work; otherwise reconsider using a non-selectable but enabled option with interception.
- **Settings test project setup:** confirmed unnecessary — `SchoolCollab.Settings.Tests.Unit` already references `Settings.Application`, `Admin.Shared`, `bunit`, `MSTest`, `Moq` and `FluentAssertions`, so `TenantsIndexTests` needs no new project or package references.

---

## 9. Implementation outcome (2026-10-14, solo)

§3–§4 landed on `feat/default-tenant-dropdown-action`. Files:

- `src/SchoolCollab.Admin/Components/Layout/DevTenantSwitcher.razor(.css)`
- `src/Settings/SchoolCollab.Settings.Application/Components/Pages/Tenants/Index.razor(.css)`
- `tests/SchoolCollab.Admin.Tests.Unit/DevTenantSwitcherTests.cs` — 17 tests
- `tests/SchoolCollab.Settings.Tests.Unit/Tenants/TenantsIndexTests.cs` — 7 tests (the page's first)
- `documents/solution/admin-default-tenant.md` §3.3 / §3.4 / §5
- `documents/specs/tenant-default-dropdown-action.md` status line + verification

### Deltas from the plan, found in the FluentUI 4.14.2 source

1. **The disabled separator did not need a binding rewrite.** `ListComponentBase` exposes
   `OptionDisabled` (`Func<TOption,bool>`), and `ListComponentBase.razor` forwards it to each
   Items-bound `FluentOption` as `Disabled`. So `OptionDisabled="@(o => o.IsSeparator)"` keeps the
   spec-mandated `Items`/`OptionText`/`OptionValue` binding. A disabled option also short-circuits
   `FluentOption.OnClickHandlerAsync`, so the separator needs no interception at all.
2. **Rejecting the action entry needs a forced re-creation.** FluentSelect records the clicked
   entry as its own internal `SelectedOption` and re-reads the *parameter* only when its value
   changes — and the bound value never changes, so a plain `StateHasChanged()` would leave the
   select pointing at the entry just rejected. The component bumps an `@key` epoch instead, which
   re-creates the select with the tenant selection re-applied (FR-6).
3. **FR-5's re-publish is a deliberate no-op in this state.**
   `AdminDefaultTenantResolver.ResolveAndApplyAsync` short-circuits while an explicit pick is in
   force, and the action zone only renders for such a pick. The call is kept (it is correct if the
   selection is ever cleared), but the observable contract is the PUT plus the forced reload —
   which is what the test asserts.
4. **Test harness split.** Cancel runs through the REAL `FluentDialogProvider` (spec §5's
   mandate); confirm runs against a mocked `IDialogService` (testing.md's sanctioned write-back
   harness) so the PUT body and the captured prompt wording can be asserted directly.
5. **Test count.** §4.1's six new switcher tests became twelve once §8.3's additions landed.

### Verification

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | 0 errors |
| `dotnet test tests/SchoolCollab.Admin.Tests.Unit` | 684 passed / 0 failed |
| `dotnet test tests/SchoolCollab.Settings.Tests.Unit` | 561 passed / 0 failed |
| `DevTenantSwitcherTests` | 17 / 17 |
| `TenantsIndexTests` | 7 / 7 |

---

## 8. Additional spec-derived checks and clarifications

These are taken directly from `documents/specs/tenant-default-dropdown-action.md` and should be added to the work above.

### 8.1 Exact confirm-dialog parameters

The prompt must call the repo-sanctioned helper with these exact values (FR-4):

```csharp
var confirmed = await DialogService.ShowConfirmDialogAsync(
    $"Open the admin portal on '{TenantNameOf(tenantId)}' by default from now on?",
    primaryText: "Set as default",
    title: "Set default tenant");
```

- `secondaryText` is omitted so it defaults to `"Cancel"`.
- Do **not** use `IDialogService.ShowConfirmationAsync` / `ShowMessageBoxAsync`.

### 8.2 Cancel test must use the real dialog provider

Spec §5 explicitly says the cancel test must use the **sanctioned end-to-end harness**: real `IDialogService` plus a rendered `FluentDialogProvider` (per `.github/copilot/rules/testing.md` §124). Update the test plan in §4.1:

- **Remove** the `SetDefault_Cancel_DoesNotCallPut` row that proposes a mock.
- **Replace with** `SetDefault_Cancel_ViaRealProvider_DoesNotCallPut`:
  1. Render `DevTenantSwitcher` **and** `FluentDialogProvider` in the same `BunitContext`.
  2. Select the `Set as default` option.
  3. Wait for the confirm dialog to appear (use one named `DialogRoundTripBudget` per the testing rule).
  4. Click the secondary (`Cancel`) button or the overlay/ESC.
  5. Assert no `PUT /api/tenants/me/default` call was recorded and `SelectedOption` stayed on the tenant entry.

The confirm test may still use a mocked `IDialogService` for the write-back assertion, or also go through the real provider; either is acceptable, but the cancel test must be real-provider.

### 8.3 Additional DevTenantSwitcher tests

Add these to §4.1:

| Test | Spec | What to assert |
|------|------|----------------|
| `ActionZone_SeparatorPresent_IffActionEntryPresent` | FR-2 / FR-3 | For every state in §4 reference table, the separator and the `Set as default` entry appear or disappear together. |
| `ActionZone_SetAsDefault_IsEnabled_NotDisabled` | FR-2 | The action entry must not carry a `disabled` attribute; the separator does. |
| `SetDefault_Confirm_DoesNotShowSuccessToast` | FR-5 | After confirm, `IToastService.ShowSuccess` (or any toast call) is **not** invoked; the forced reload replaces the toast. |
| `SetDefault_ApiFailure_LeavesSelectionUnchanged_AndLogsError` | FR-5 | If `SetMyDefaultTenantAsync` throws, `_selectedOption` stays on the tenant entry, `NavigationManager` is **not** called, and `ILogger` receives an error. |
| `SeparatorSelection_DoesNothing` | FR-2 / FR-4 | Selecting the decorative separator restores the previous selection and does not open the dialog or call the API. |

### 8.4 Sentinel-value contract

The action sentinel must satisfy all of these (FR-2):

- Distinct from `DefaultOptionValue` (`""`).
- Distinct from `SystemOptionValue` (`"system"`).
- Never `Guid.TryParse`-able.
- The separator sentinel must also be non-parseable and distinct from the action sentinel.

Add a comment or, better, a unit assertion in the test class verifying `Guid.TryParse(SetDefaultActionValue, out _)` and `Guid.TryParse(SetDefaultSeparatorValue, out _)` are both `false`.

### 8.5 `/tenants` page: unknown preference name fallback

Spec FR-7 requires: if the preference tenant is not in the loaded list, the banner shows the raw id string. Ensure `PreferenceDisplayName` implements this explicitly and add a dedicated test (`Banner_ShowsRawId_WhenPreferenceUnknown`) — already listed in §4.2, but it is easy to overlook because the happy path is more obvious.

### 8.6 Keep the `(default: X)` entry accurate after clear

Although `/tenants` clear does not reload, the next time `DevTenantSwitcher` loads it will re-resolve the default. Add a regression-style note: the `/tenants` `Clear` must call `SetMyDefaultTenantAsync(null)` exactly, **not** `SetDefaultTenantAsync` (which sets the *platform* default), so the dropdown's `(default: X)` entry resolves to the platform default rather than the old preference.

### 8.7 Documentation updates, re-stated

Same-PR updates already listed in §3.3 and §5 step 11:

- `documents/solution/admin-default-tenant.md` §3.4 and §5.
- Flip the spec's own `Status: Draft` line to `Status: implemented` and append verification results.

### 8.8 Nothing else from the spec is outstanding

All other spec items (FR-1…FR-7, §5 test reworking, §7 out-of-scope rejections) are already covered in §2–§6 above. The only cleanup not yet listed is deleting the now-dead `.dev-tenant-pref-button` CSS class, which is included in §3.1 step 6.
