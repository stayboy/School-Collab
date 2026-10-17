# FluentUI v5 upgrade path (v4.14.2 → 5.0.x)

**Status**: Findings (research). No implementation started.
**Researched**: 2026-10-15.
**Source of truth**: official per-component migration guides on the `dev` branch of
`microsoft/fluentui-blazor` —
`examples/Demo/FluentUI.Demo.Client/Documentation/GetStarted/Migration/*.md`
(index page: `MigrationIndex.md`; note the `main`-branch `UpgradeGuide.md` only covers up
to v4.11 and has NOT been updated for v5).

---

## 1. Current repo state

- `Directory.Packages.props` pins both `Microsoft.FluentUI.AspNetCore.Components` and
  `Microsoft.FluentUI.AspNetCore.Components.Icons` at **4.14.2** (CPM — no `Version=` in csproj).
- Consumers (csproj `PackageReference`): AI.Chat, Assignments.Application, Admin,
  Admin.Shared, Families, Mobile (MAUI Hybrid), Settings.Application,
  Students.Application — **9 projects, ~196 files** touch the library.
- Hosts: 3 `AddFluentUIComponents()` registrations —
  `src/Mobile/SchoolCollab.Mobile/MauiProgram.cs:67`,
  `src/SchoolCollab.Admin/Program.cs:42`, `src/SchoolCollab.Families/Program.cs:73`.
  bUnit tests register the same `Services.AddFluentUIComponents()` in ~60 test files.
- Theming: `<FluentDesignTheme @bind-Mode>` with theme persistence in
  `src/SchoolCollab.Admin/Components/Layout/MainLayout.razor` and
  `<FluentDesignTheme StorageName="families-theme" />` in
  `src/SchoolCollab.Families/Components/Layout/MainLayout.razor`.
- Toasts: `<FluentToastProvider />` in both layouts; `@inject IToastService` +
  `Toast.ShowSuccess(...)` in **16 source files (22 call sites)**
  (Settings, Students, Assignments pages).
- Custom dialog infrastructure (the biggest single concern): 
  `src/SchoolCollab.Admin.Shared/Components/Dialogs/` — `DialogShellBase<TModel,TResult>`
  (implements v4 `IDialogContentComponent<T>`; cascading `FluentDialog`;
  `Dialog.CloseAsync` / `Dialog.CancelAsync`), `DialogServiceExtensions`
  (`BuildShellParameters`, `ShowShellDialogAsync`, `ShowReadonlyDialogAsync`,
  `ShowConfirmDialogAsync` — heavy users of v4 `DialogParameters` incl. the removed
  `PreventDismissOnOverlayClick`), `ConfirmDialog`, `ReadOnlyDialogShell`, `DialogSize`.
  Derived dialogs: 8 in Students, 5-6 in Assignments (ContextPick, MaterialEdit, Publish,
  QuestionEdit), 1 `IDialogContentComponent<ConfirmDialogContent>`.
  Plus ~30 test files across Admin/Assignments/Students/Settings that exercise these.
- Scoped CSS: **43 `.razor.css` files use `::deep`** to target FluentUI internals
  (see §4 caveat).
- Repo-owned icon constants: `src/SchoolCollab.Admin.Shared/Constants/FluentIcons.cs`
  (repo's own static class of `Icon` fields wrapping `Icons.Regular.Size20.*` factories) —
  **safe** against v5 icon changes; only its `Icon`/factory types' availability matters.
- Legacy usage surface (open-tag counts, `src/`, non-obj):
  `FluentTextField` 119, `FluentProgressRing` 63, `FluentSelect` 55, `FluentAnchor` 50,
  `FluentNumberField` 33, `FluentCheckbox` 29, `FluentNavLink` 24, `FluentTextArea` 22,
  `FluentDataGrid` 18, `FluentMessageBar` ~224, `FluentButton` ~279, `FluentIcon` ~380,
  `FluentWizard` 5, `FluentNavGroup` 5, `FluentSwitch` 4, `FluentAutocomplete` 3,
  `FluentMenu` 3, `FluentTooltip` 2, `FluentTabs` 2, `FluentSearch` 1, `FluentNavMenu` 1,
  `FluentCombobox` 1. Appearance enum usage: Accent 155, Neutral 122, Lightweight 79,
  Outline 70, Hypertext 30, Stealth 29.

## 2. v5 facts

- v5.0.0 GA **2026-09-25**; **5.0.1** published 2026-10-14 (bug-fix only).
- `Microsoft.FluentUI.AspNetCore.Components` 5.0.1 targets **net8.0 / net9.0 / net10.0 /
  net11.0-rc** — works for the repo's net10.0.
- Matching `…Components.Icons` **5.0.1** exists (version must be kept aligned).
- 5.0.1 dependencies: `Microsoft.AspNetCore.Components.Web` + `Microsoft.Extensions.Http`
  at **10.0.12** for the net10.0 target. **CPM action**: the repo pins
  `Microsoft.Extensions.Http` at `10.0.8` (`Directory.Packages.props`, the L3 comment
  justified the pin as "the version the graph already resolves" — that was true under
  4.14.2; under 5.0.1 the graph resolves 10.0.12, so the pin must move to ≥10.0.12 or
  restore walks it back).
- v5 ships a **migration compat layer** (`Microsoft.FluentUI.AspNetCore.Components.Migration`
  namespace; library source `src/Core/Migration/`): old `Appearance` enum aliased with
  `ToButtonAppearance()`-style converters, obsoleted component shims
  (`FluentProgressRing`, `FluentProgress`, `FluentSwitch`, `FluentGridItem`), and a
  v4-shaped `FluentComponentBase`. This means a plain version bump *may* compile with
  only errors on truly-removed members — the spike (§5) validates exactly this.
  The shims are temporary support; the end-state plan should not depend on them.
- Registration stays `AddFluentUIComponents()`; a new **`<FluentProviders>`** component
  must wrap the app root in each host (provides `LibraryConfiguration` cascading value).

## 3. v5 breaking changes mapped to this repo (by impact)

Official impact labels from `MigrationIndex.md`, cross-checked against repo usage.

| v5 change | Official impact | Repo exposure | Notes |
|---|---|---|---|
| **Dialog rearchitecture** | **Critical** | `DialogShellBase`, `DialogServiceExtensions`, `ConfirmDialog`, `ReadOnlyDialogShell`, 8 Students dialogs, 5-6 Assignments dialogs, ~30 test files | `IDialogContentComponent<T>` replaced by `FluentDialogInstance` base class; `DialogParameters` → `DialogOptions` (`Header.Title`, `Footer.PrimaryAction/SecondaryAction` as `DialogOptionsFooterAction` objects); **`PreventDismissOnOverlayClick` removed** (used by every shell dialog); `Dialog.CloseAsync/CancelAsync` → `HideAsync` lifecycle with `OnStateChange`/`DialogState`; `DialogType` → `Alignment` (`Start`/`End` drawers, `<fluent-drawer>` element); `IDialogService-*` interfaces unified; `DialogInstance` → `IDialogInstance`. The repo's own `DialogShellBase`/`DialogShellFooter` shell can be *ported* (it renders its own footer buttons — the shell design survives, the base/plumbing does not). |
| **List components generics** | **Critical** | `FluentSelect` ×55, `FluentCombobox` ×1, plus `FluentAutocomplete` ×3 | `FluentSelect`/`FluentCombobox` now take **two** type parameters `TOption`+`TValue` (`Value` is now `TValue?`, formerly `string?`); `SelectedOptions(Changed)` → `SelectedItems(Changed)`; `Disabled` now nullable; `OptionValue` returns `TValue?`. Every 2-generic usage site needs a parameter shape review. |
| **TextField/Search → TextInput** | High | `FluentTextField` ×119, `FluentSearch` ×1 | → `FluentTextInput`; `TextFieldType`→`TextInputType`, `Maxlength/Minlength`→`MaxLength/MinLength` (casing), `Size` becomes `TextInputSize?` enum, `ChildContent` removed (use `StartTemplate`/`EndTemplate`), `FilledDarker` replaces `Filled`. Mechanical but the single largest diff. |
| **Anchor → Link / AnchorButton** | High | `FluentAnchor` ×50 | `Appearance.Hypertext` (repo's 30 uses) → `FluentLink` `LinkAppearance.Default`; accent/neutral button-styled anchors → `FluentAnchorButton`; `Target` becomes `LinkTarget` enum; `Hreflang/Rel/ReferrerPolicy` re-typed. |
| **Toast: IToastService → INotificationService** | High | 16 files, 22 `Toast.ShowSuccess` calls; 2 `<FluentToastProvider />` | Toast *components* survive in v5 (`FluentToast`, `FluentToastProvider`, plus new `INotificationService` in `src/Core/Components/Toast/Services/`); only the *content components* (Communication/Confirmation/ProgressToast) were removed. Repo only uses `ShowSuccess` — check the new method name/signature during the spike. Verify whether `IToastService` alias remains. |
| **Nav system** | High | `FluentNavMenu` ×1, `FluentNavGroup` ×5, `FluentNavLink` ×24 | → `FluentNav` / `FluentNavCategory` / `FluentNavItem`; `Icon` → `IconRest`/`IconActive`; `Collapsible`, `Expanded`, `CustomToggle`, nav `Title`, `ForceLoad` removed. Likely confined to 1-2 layout files. |
| **DesignTheme removed** | High | 2 layouts (`<FluentDesignTheme @bind-Mode>`, `StorageName="families-theme"`) | Both `FluentDesignTheme` and `FluentDesignSystemProvider` removed; theming is now CSS-variable-based (`FluentTheme` CSS system, `color-scheme: dark`, `--colorBrand*` overrides, `StylesVariables` C# constants). **The light/dark toggle + persistence must be re-implemented** (this is the least mechanical change). |
| **Button appearance enum** | Medium | `FluentButton` ×279 + `Appearance.*` enum usage (Accent 155, Neutral 122, Lightweight 79, Outline 70, Stealth 29, Hypertext 30) | `Appearance` enum → `ButtonAppearance` (`Accent`→`Primary`, `Neutral`→`Default`, `Stealth`→`Subtle`, `Lightweight`→`Transparent`); the v5 compat layer ships `ToButtonAppearance()` so this can **phase 2** from a working build. |
| **ProgressRing → Spinner** | Medium | `FluentProgressRing` ×63 | Renamed `FluentSpinner`; `Paused/Value/Color/ChildContent` removed, `Stroke`→`Size` (`SpinnerSize?`). Repo shims keep it compiling but check for `Paused` usage. |
| **DataGrid** | Medium | `FluentDataGrid` ×18 | `SortByColumn`/`SortByAscending` removed from sort requests (multi-level sorting API introduced; `ApplySorting()`/`GetSortByProperties()` keep working). |
| **NumberField → NumberInput** | Medium | `FluentNumberField` ×33 | Same rename family as TextField. |
| **Badge / CounterBadge / Menu / MenuButton / Overlay / Popover / Tabs / Splitter / Layout** | High each | Tabs ×2, Menu ×3, Tooltip ×2, Badge, others mostly Light | Small counts; check one-by-one during implementation. |
| **Wizard** | re-introduced, same API | `FluentWizard` ×5 | `FluentWizard`/`FluentWizardStep` re-introduced in v5 preserving the V4 API; minor changes only, see `MigrationFluentWizard.md`. |
| **FluentIcon default color** | Low | `FluentIcon` ×380 | Default color changes `Color.Accent` → `currentColor` — icons inherit parent CSS color. Repo's icon-heavy UIs need a **visual review pass**; mechanical code unaffected (repo icons come via its own `Constants/FluentIcons.cs`). |
| **`FluentComponentBase` changes** | — | Any repo component inheriting it | Constructor now requires `LibraryConfiguration`; `Element` removed from base; `ParentReference` removed. Check repo for direct inheritance (none found in dialogs; verify repo-wide). |
| **`FluentValidationMessage` removed; `FluentField` new** | Medium | usage to census | Labels/validation move to `FluentField`. |
| **Scoped CSS bundling disabled** | — | 43 `.razor.css` files using `::deep` | v5 ships scoped-CSS off; if the repo keeps its own bundling (Blazor Web default), `::deep` targeting FluentUI internals may go dead — audit those selectors. |
| **bUnit interplay (~60-90 test files)** | — | `Services.AddFluentUIComponents()` in tests | New `FluentProviders`/`LibraryConfiguration` may require test-renderer adjustments; the repo has prior art in v4-vs-bUnit quirks (see `fix-flaky-bunit-fluentui-after-cascade` skill). Re-run the whole `dotnet test` suite after the bump. |
| **`Microsoft.Extensions.Http` CPM pin** | build-breaking | `Directory.Packages.props` `10.0.8` | Must move to ≥10.0.12 (v5.0.1 net10.0 dependency) — same rationale as the original L3 pin (declare what the graph already resolves). |

Removed with **no direct replacement** (checked against repo): `FluentFlipper`,
`FluentHorizontalScroll`, `FluentCollapsibleRegion`, `FluentAccessibility`,
`FluentEditForm`, `FluentProfileMenu`, `FluentBreadcrumb` — none observed in repo
usage census (census had Flipper/HorizontalScroll/etc. at zero).

## 4. Why this is a Tier-3-scale change (not a version bump)

- The **dialog** change is Critical in the official index and this repo's dialogs are its
  deepest custom integration (~20 dialog components + shell infra + ~30 test files
  + the `dialog-ui` / dialog-shell conventions + `dialog-parameter-binding.md` contract).
- Theming is a functional migration (light/dark toggle, persistence), not a rename.
- 9 consumer projects, 3 hosts (Server, MAUI Hybrid, Server), ~200 markup files,
  60+ test files.

## 5. Recommended upgrade path (findings → proposed implementation)

0. **Spike (branch, not PR-ready)**: bump both CPM pins to `5.0.1` +
   `Microsoft.Extensions.Http` → `10.0.12`, wrap app roots in `<FluentProviders>`,
   re-enable whatever theming seam exists, and run `dotnet build` +
   `dotnet test` **with the migration compat layer in place**. Record: which errors
   remain (expected: `PreventDismissOnOverlayClick`, `IDialogContentComponent`,
   list-component generics, `IToastService`), and whether the compat `Appearance`
   enum keeps every `Appearance.*` site compiling. This calibrates the real work.
1. **Round A — infra seam**: dialog system port (`DialogShellBase` → v5
   `FluentDialogInstance`/`DialogOptions`), toast service rename, `FluentProviders`
   wiring, theming re-implementation (light/dark + storage), CPM pin bump. Gate:
   build green + full `dotnet test` green.
2. **Round B — mechanical renames** (scripted + reviewed): TextField→TextInput (119),
   NumberField→NumberInput (33), Anchor→Link (50), NavMenu family (30),
   ProgressRing→Spinner (63), Search (1). bUnit suite re-run per project.
3. **Round C — visual/behavioural review**: FluentIcon color inheritance (accent now
   explicit), list-component generic migrations (`SelectedItems`), DataGrid sorting,
   Badge/CounterBadge/Menu high-impact items, `::deep` audit across 43 scoped CSS files,
   MAUI Hybrid smoke test.
4. **Round D — shed the compat layer**: replace `Appearance.*` enum usages with
   `ButtonAppearance.*` (155 Accent etc.), remove `Migration`-namespace usages from
   source, update `documents/configuration.md` + the repo skills
   (`fluentui-icons-in-school-collab`, `dialog-ui`, `fluentui-component-props`).

Deliver each round as `documents/rounds/round-<slug>.md` per the orchestrator skill.

## 6. Verification gates (per AGENTS.md)

- `dotnet build SchoolCollab.slnx` — zero errors after every change.
- `dotnet test` — 0 failures before any PR (the ~90 FluentUI-touching test files are the
  primary regression surface; bUnit + FluentUI-v5 interplay is the risk area).
- Playwright/preview smoke for theming (light/dark toggle persists) and one shell-dialog
  open/cancel/save flow per app after Round A.
---

## 7. Spike log (round fluentui-v5, 2026-10-15)

Base: branch `fluentui-v5` from `main` @ `f41420b8` (tree had 3 untracked docs, none
touched by the round). Environment note: the `dotnet watch` AppHost dev loop was running
and locked output binaries (MSB3021/3027, 60 hits) — stopped by owner instruction
(`taskkill /PID 32908 /T /F`); restart the watch after the round closes.

**Pins moved** (`Directory.Packages.props`):
- `Microsoft.FluentUI.AspNetCore.Components` and `…Icons`: 4.14.2 → **5.0.1**
- `Microsoft.Extensions.Http`: 10.0.8 → **10.0.12** (5.0.1 net10.0 dependency)
- First restore: 72 × NU1605 downgrades across the whole graph → moved
  `Microsoft.AspNetCore(.Web)` Components 10.0.8 → 10.0.12 and all
  `Microsoft.Extensions.{Configuration{,.Abstractions,.Json,.Binder},DependencyInjection{,.Abstractions},Logging{,.Abstractions,.Debug}}`
  10.0.11 → **10.0.12**. After the pin moves, restore is clean for the whole solution.

**Post-unlock full build (exit 1):** 32 × CS0246, 10 × RZ9991, 6 × RZ10000, 2 × RZ10001 —
all in `SchoolCollab.Admin.Shared` (downstream projects skipped; their inventory emerges
as Round A fixes Admin.Shared). Distinct error surface:

| v5 change | Sites (file:line) |
|---|---|
| `DesignThemeModes`/`OfficeColor` removed | `src/SchoolCollab.Admin.Shared/Components/Theme/ThemeSwitcher.razor` (24,25,27–32,71) |
| `FluentSelect` missing/cannot-infer type args (RZ10000/RZ10001) | `Components/DropdownForEnum.razor:32`, `Components/DropdownComponent.razor:70`, `Components/CodedValueDropdown.razor:60`, `Components/Theme/ThemeSwitcher.razor:15` |
| `bind-Value` inference failures (RZ9991) | `Components/CodedValueDialog.razor` (22–24,25,41) — symptom of the list-generics change |
| `IDialogContentComponent<>`/`DialogParameters`/`IDialogReference` removed | `Components/Dialogs/DialogShellBase.cs:43`, `DialogServiceExtensions.cs` (46,78,127,133), `ConfirmDialog.razor:14` |
| `GenerateHeaderOption` removed | `Components/Landing/LandingGridSettings.cs:21` |

**Pinned decisions (owner, 2026-10-15, grill round 2 — "all as recommended"):**
- **Q4**: theming migrates in Round A to a self-owned CSS-variable system (light/dark
  custom-property blocks + `color-scheme`, `ThemeSwitcher` keeps its shell + persistence).
  No pin-to-light deferral.
- **Q5**: accent-color picker kept, implemented as `--colorBrand*` CSS-variable overrides.
- **Q6**: dialog infra ports onto the v5 API (`FluentDialogInstance`/`DialogOptions`) in
  Round A — no local compat shims.
- **Q7**: spike state (pin moves) stays **uncommitted** on `fluentui-v5` as the Round A
  base; the two pre-existing untracked spec docs stay out of the round patch.
- Round 1 pins (2026-10-15): adopt 5.0.1 now; spike-first phased strategy; Tier 3 full
  for Round A (attended).

### 7.1 Theming addendum — the migration guide is incomplete (binary-verified 2026-10-15)

`MigrationFluentDesignTheme.md` presents v5 theming as "CSS variables managed through static C#
classes (`StylesVariables`)". That describes only the *manual* path. The 5.0.1 package additionally
ships a full **`IThemeService`** (impl `ThemeService(IJSRuntime)`, DI-registered by
`AddFluentUIComponents()`) — the supported way to keep a working theme switcher:

- `SetThemeAsync(ThemeMode)` — `ThemeMode` = `Light | Dark | System` (**exact 1:1 with the repo's
  existing 3-state cycle**); `SetThemeAsync(ThemeColorVariant)`, `SetThemeAsync(ThemeColorVariant, ThemeMode)`.
- `SetThemeAsync(string color, bool isExact)` — "sets a custom theme based on the specified brand
  color using the current effective mode" → the accent picker's API; `GetBrandColorAsync()` rehydrates it.
- `GetThemeSettingsAsync()`, `IsDarkModeAsync()`, `IsSystemDarkAsync()`, `SwitchThemeAsync()`,
  `ClearStoredThemeSettingsAsync()`, `CreateCustomThemeAsync(ThemeSettings)`, `GetColorRampAsync()`.
- `SetThemeToElementAsync(ElementReference, ThemeSettings|Theme)` — scoped (subtree) theming; not needed here.
- Types: `ThemeSettings(string color, double hueTorsion, double vibrancy, ThemeMode mode, bool isExact)`,
  `Theme`, `ThemeColorVariant` (`Default | Teams` — the OfficeColor successor; the repo's switcher has
  no Office selector, so not adopted).

Persistence + application are the shipped JS's job: storage key **`fluentui-blazor:theme-settings`**
(localStorage, mode + brand), applied as `data-theme` / `data-theme-color` on `<body>` plus inline
custom properties on `document.documentElement`; `clearStoredThemeSettings()` removes it; a system-theme
`matchMedia` change listener exists. Consequence for the round: our `theme` / `families-theme` keys are
neither used nor needed by the library, and a C#-only boot path risks a flash of light theme before the
circuit starts — so storage ownership is a genuine fork (Q13), not a formality.

### 7.2 Guide-vs-binary reconciliation (dev-branch guides read in full, 2026-10-15)

All 62 pages of `Documentation/GetStarted/Migration/*.md` were fetched to
`%TEMP%\v5mig\` and read. They are the authoritative prose source, but **three of their tables
are stale — the 5.0.1 binary wins**, verified by parsing the shipped XML docs (409 types):

| Guide claim | Binary fact | Verdict |
|---|---|---|
| Index: `FluentToast`/`FluentToastProvider` → `FluentMessageBar` | `INotificationService` + `FluentToastProvider` both ship; `MigrationToast2.md` documents the notification service | Guide table stale; the Round-A `S5` notification-service port is correct |
| Index: `FluentWizard` → *(no direct replacement)* | `FluentWizard` + `FluentWizardStep` ship; `MigrationFluentWizard.md` says "Component re-introduced" with breaking changes | Index row wrong; Wizard is kept, not removed |
| Index: new component `FluentTheme` (CSS system) | **No** `FluentTheme` type exists in 5.0.1 | Guide row wrong; theming is the CSS-variable + `IThemeService` model (§7.1) |

Also settled (probe, not opinion): the `MigrationGeneral.md` "Scoped Css Bundling" bullet
(`<ScopedCssEnabled>false</ScopedCssEnabled>`, `<DisableScopedCssBundling>true</DisableScopedCssBundling>`)
describes the **library/demo's own csproj** — `ScopedCssEnabled` is a Blazor SDK property
(`Sdk.StaticWebAssets.CurrentVersion.targets:144`) that, when false, skips `ResolveCssScopes` and never
imports the ScopedCss targets, i.e. switches component CSS isolation off entirely; the library's own
components then carry no scope attribute, which is why *its* `::deep` rules became dead. This repo sets
neither property and must not adopt them: doing so would disable isolation for all 43 repo `.razor.css`
files, contradicting `AGENTS.md` + the `blazor-css-isolation` skill. **Repo rules win → not adopted.**

### 7.3 Definitive v5 rename map and silent-break inventory (parsed from 5.0.1 vs `src/**/*.razor`)

v4 → v5 component renames that the repo actually uses (`MigrationIndex.md` + binary, arity-aware regex):

| v4 (repo tags) | v5 | repo tags |
|---|---|---|
| `FluentTextField` | `FluentTextInput` | 115 |
| `FluentAnchor` | `FluentLink` (+ new `FluentAnchorButton` for button-styled) | 50 |
| `FluentNumberField` | `FluentNumberInput` | 32 |
| `FluentNavLink` | `FluentNavItem` | 24 |
| `FluentNavGroup` | `FluentNavCategory` | 5 |
| `FluentNavMenu` (+ `FluentNavMenuTree`) | `FluentNav` | 1 |
| `FluentSearch` | `FluentTextInput` (merged) | 1 |
| `FluentProgressRing` | `FluentSpinner` (old type still shipped, presumed compat shim) | 63 |
| `FluentProgress` | `FluentProgressBar` | 0 |
| `FluentLabel` | `FluentText` (typography; `FluentLabel` still ships) | 1 |
| `FluentBreadcrumb` / `FluentBreadcrumbItem` | *(no direct replacement)* | 6 |
| `FluentTabStrip` | removed/merged into `FluentTabs` | 2 |
| `FluentDesignTheme` / `FluentDesignSystemProvider` / `FluentMenuProvider` | removed (theming → `IThemeService`; menu provider → `FluentProviders`) | 2 / 0 / 1 |

**Silent-break total (types absent from 5.0.1 → render as unknown HTML elements, build stays green):
11 distinct components / 238 tags** — `FluentTextField` 115, `FluentAnchor` 50, `FluentNumberField` 32,
`FluentNavLink` 24, `FluentNavCategory`/`NavGroup` 5, `FluentBreadcrumb*` 6, `FluentTabStrip` 2,
`FluentDesignTheme` 2, `FluentSearch` 1, `FluentNavMenu` 1. (This supersedes the earlier "~215" estimate
in the worker report; the difference is the arity-aware parse plus Breadcrumb/TabStrip.)
Also v5-relevant, non-rename: `Color` enum renames `Neutral`→`Default`, `Accent`→`Primary`
(`MigrationColor.md`), and `FluentIcon`'s **default colour changed**.
