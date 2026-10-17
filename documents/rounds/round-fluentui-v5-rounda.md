Provider: pi, full Tier 3, Option A (models: glm-5.3-flash orchestrator, glm-5.3:cloud plan-review, deepseek-v4.1-flash worker, kimi-k2.7-code reviewer, kimi-k2.7-code escalator, minimax-m3 tester)
Mode: attended (owner pinned Round A full)
Tier: 3 full (UI round expected: .razor surfaces in theme + dialog slices)
Base: f41420b82de6aeb9e4226d571e046c7286cede25, branch fluentui-v5, spike state uncommitted (pin moves in Directory.Packages.props)
Status: CLOSED — Round A accepted (port 4.14.2→5.0.1 complete; gates: main solution 0 errors, MAUI head 0/0, `dotnet test` 4092/4098 with the 2 failures being opt-in live-AI tests). Round B handoff + 6 deferred items recorded below.

Tree note: the base tree contains **2 pre-existing untracked spec docs**
(`documents/specs/assignment-approval-workflow.md`,
`documents/specs/assignment-notification-templates.md`). They predate this round,
belong to other work, and **must NOT enter the round patch** (`diffs-fluentui-v5-rounda.patch`).
The spike state — CPM pin moves in `Directory.Packages.props`, uncommitted on
`fluentui-v5` — IS the Round A base (pinned Q7) and DOES belong to the patch.

Single source of truth: `documents/solution/fluentui-v5-upgrade-path.md` (research
doc; §7 spike log = authoritative compiler-error inventory). v4→v5 API mapping:
the official migration guides under `C:/Users/skwar/AppData/Local/Temp/Migration*.md`
(Dialog, DesignTheme, Toast, Select, DataGrid, General). This plan verifies all
load-bearing v5 APIs against the **actual 5.0.1 assembly** in the local NuGet cache
(see §"Verified v5 API facts") — where a guide and the binary disagree, the binary wins.

---

## Plan

### Goal

Round A of the FluentUI v5 upgrade: adopt **5.0.1** as the working
version of `Microsoft.FluentUI.AspNetCore.Components` (+ `…Icons`) and port the
**infrastructure seams** — dialog system, theming, provider wiring, toast-service
rename, and the minimum mechanical compile-fix set — so that
`dotnet build SchoolCollab.slnx` returns **0 errors** and `dotnet test` returns
**0 failures** on the affected projects. Rounds B–D (mechanical renames, visual
review, compat-layer shed) follow; nothing in this round may be priced as theirs.

### Scope IN

1. **Dialog infrastructure port onto v5** (`src/SchoolCollab.Admin.Shared/Components/Dialogs/` + derived dialogs, as far as the compiler requires):
   - `DialogShellBase.cs` — re-hosted on v5: base class `FluentDialogInstance`
     (replaces `ComponentBase` + `IDialogContentComponent<DialogShellData<TModel>>`),
     cascade `IDialogInstance DialogInstance` (replaces `FluentDialog Dialog`,
     `Dialog.CloseAsync(...)` → `DialogInstance.CloseAsync(...)`), result flow
     unchanged (`await Instance.Result` → `DialogShellResult<TResult>` unwrap).
     Public `Model`/`Saving`/`Error`/`SubmitText`/`SavingText`/`OnModelInitialized`/
     `SubmitAsync`/`HandleSubmitAsync`/`HandleCancelAsync` surface stays.
   - `DialogServiceExtensions.cs` — `DialogParameters` → `DialogOptions`;
     `BuildShellParameters`, `ShowShellDialogAsync`, `ShowReadonlyDialogAsync`,
     `ShowConfirmDialogAsync` all port. Callers' signatures
     (`model, title, size, height`) must NOT change unless the compiler forces it —
     ~40 call-site files must not churn.
   - `ConfirmDialog.razor`, `ReadOnlyDialogShell.razor`, `DialogSize.cs` (+ `DialogShellData.cs`).
   - Derived dialogs (`@inherits DialogShellBase<…>` — 8 Assignments, 1 Settings
     `CodedValueDialog` + `AttributeDefinitionDialog` etc., ~25 Students):
     only the edits the compiler forces (`@implements IDialogContentComponent` removal,
     cascade rename, footer/handler plumbing). **No refactor.**
   - **ReadOnly contract — ALL 8 `IDialogContentComponent<DialogParameters>` dialogs**
     (the v4-`Content.TryGet<T>(key)` pattern, deliberately converted to typed
     `[Parameter]`s — see "ReadOnly contract" note below). Inventory (verified in tree,
     `src/Students/SchoolCollab.Students.Application/Components/…`):
     1. `GuardianContactsDialog.razor:41`
     2. `Pages/Periods/SubPeriodsListDialog.razor:13`
     3. `Students/SectionListDialog.razor:37` (generic `SectionListDialog<T>`)
     4. `Students/GradeTopicsDialog.razor:5`
     5. `Students/TopicStrandsDialog.razor:9`
     6. `Students/TeacherRoleDialog.razor:6`
     7. `Students/StudentCreateDialog.razor:7` — **edit form** with
        concurrency-conflict handling, not a read-only view
     8. `Students/StudentEditDialog.razor:7` — **edit form** with
        concurrency-conflict handling, not a read-only view
     All 8 get the same deliberate TryGet→typed-`[Parameter]` conversion (v5 spreads
     `DialogOptions.Parameters` onto real `[Parameter]` properties). Callers (verified
     in tree, all going through `ShowReadonlyDialogAsync`): `GradeLevels/Detail.razor`
     at lines 869, 977, 1099, 1226, 1266; `Pages/Periods/Periods.razor:410`;
     `Students/Detail.razor:848`; `GradeLevels/Detail.razor:624` → surfaced as
     `SectionCard.razor:315` — callers updated accordingly (keyed values become named
     arguments matching the new `[Parameter]`s).
     This is a **contract change** — record it in the Worker Report; the durable spec
     `documents/solution/dialog-parameter-binding.md` is updated in Round D, not now.
2. **Theming port** (pinned Q4/Q5):
   - `ThemeSwitcher.razor` keeps its shell (mode cycle button + accent picker
     + `@bind-Mode`/`@bind-Accent` surface) but drops `DesignThemeModes`/`OfficeColor`
     — becomes light/dark cycle + accent choice (the six Office colors map to
     `--colorBrand*` hex overrides; see "Accent mapping" below).
   - `src/SchoolCollab.Admin/Components/Layout/MainLayout.razor` and
     `src/SchoolCollab.Families/Components/Layout/MainLayout.razor` — remove
     `<FluentDesignTheme …>`, keep `<FluentToastProvider/>` etc. (verify each
     provider type still exists in 5.0.1; keep what compiles, list what vanished),
     add the self-owned light/dark CSS-variable system: `color-scheme` +
     `[data-theme]` custom-property blocks + `--colorBrand*` accent overrides.
     **Persistence kept**: reuse the existing localStorage keys
     (`theme` Admin, `families-theme` Families) via a small
     `IJSRuntime`-based service in `Admin.Shared/Components/Theme/` (plain JSON,
     no custom JS module unless needed — `dotnet-best-practices` "Never" list applies).
     System mode = follow `prefers-color-scheme` (CSS media query on the
     custom-property blocks; no JS matchMedia required).
   - `<FluentProviders>` wrapper at each host app root — Admin and Families wrap the
     routed content (in `Components/Routes.razor` around the `RouteView`, or equivalent
     root `App.razor`/MainLayout position — worker discovers the cleanest root and records it);
     Mobile wraps at its `Components/Routes.razor` root (Mobile has no
     `App.razor`; its `MauiProgram.cs:67` `AddFluentUIComponents()` registration is
     unchanged).
   - Accent mapping (starting point, worker-adjustable, recorded in Worker Report):
     Teams `#6264A7`, Word `#2B579A`, Excel `#217346`, PowerPoint `#D24726`,
     Outlook `#0F6CBD`, OneNote `#7719AA` → written as the `--colorBrand*` override
     set per accent choice.
3. **Mechanical compile-fix set** (minimum to build — nothing visual beyond that):
   - `FluentSelect` explicit `TOption`/`TValue` in
     `Components/DropdownForEnum.razor:32`, `Components/DropdownComponent.razor:70`,
     `Components/CodedValueDropdown.razor:60`, `Components/Theme/ThemeSwitcher.razor:15`
     (v5: two generics, `SelectedOption`/`SelectedOptionChanged` → `Value`/`ValueChanged`).
   - `Components/CodedValueDialog.razor` (bind-Value inference, RZ9991 ×5).
   - `Components/Landing/LandingGridSettings.cs:21` — `GenerateHeaderOption` →
     `DataGridGeneratedHeaderType` (verified rename; check consumers).
   - `IToastService` → `INotificationService` **in 13 razor files, 22 `ShowSuccess`
     call sites** (the research doc says 16 files; the current tree greps to 13 razor
     files — authoritative is `grep -rl IToastService src/`; the 3-file discrepancy is
     recorded, not chased). Verified replacement (5.0.1 binary):
     `INotificationService.ShowSuccessToastAsync(string title, string? message = null,
     int? lifetime = null, string? dismissLabel = null, Func<ToastEventArgs,Task>? = null)`
     (`IToastService` does **not** exist in v5; it does not even remain as an alias).
     Map `Toast.ShowSuccess(msg)` → `NotificationService.ShowSuccessToastAsync(msg)`
     (old text lands as `title`, matching v4's title-first rendering); use a `_ =`
     discard where the call site is a non-async handler, or await if already async.
     The two `<FluentToastProvider />` usages stay if the component exists in v5
     (it does — verify at build).
4. **Test fallout**:
   - `tests/SchoolCollab.ArchitectureTests.Unit/DotNetBestPracticesArchitectureTests.cs:117`
     `Dialog_ContentParameter_Only_For_ReadonlyDialogs` — this is the test that
     *enforces* the v4 readonly contract (it scans `.razor` files containing the
     literal `IDialogContentComponent<DialogParameters>` and fails on any non-`Content`
     `[Parameter]`). The port removes that marker string from all 8 dialogs, so the
     test goes silently **vacuous** (passes while discriminating nothing). **Decision
     (plan revision 1): invert the test in Round A** — re-target it at the new v5
     contract so it keeps discriminating: scan `.razor` files declaring
     `@inherits DialogShellBase<` (the v5 shell-dialog marker) and fail on data-input
     `[Parameter]`s that are not supplied via `DialogOptions.Parameters` keys (the
     failure mode flips from "declared a data `[Parameter]`" to "declared a data
     `[Parameter]` with no matching `DialogOptions.Parameters` key at a call site" —
     implement the simplest discriminating form; keep the empty-guid edit-dialog
     rationale in the doc comment). If inversion proves non-mechanical at build,
     STOP and report rather than improvising. Deferral to Round D was considered and
     rejected: a round that deletes a contract must not leave its enforcement test
     vacuous (the honest-test rule would flag it post-hoc anyway).
   - Exact grep: **37 files** under `tests/` reference
     `ShowConfirmDialogAsync|ShowShellDialogAsync|ShowReadonlyDialogAsync|DialogShellBase`
     (mostly `SchoolCollab.Admin.Tests.Unit`), plus ~60 test files calling
     `Services.AddFluentUIComponents()`. Update **only where the compiler or a genuine
     contract change forces it**: `IDialogContentComponent`/`DialogParameters` construction,
     `FluentDesignTheme`-related asserts, `IToastService` fakes, `GenerateHeaderOption`.
     **Do not re-spec the bUnit suite wholesale.** If `FluentProviders` (or a
     `LibraryConfiguration` cascade) is required for test rendering, add it via the
     shared test renderer/base — one place, not 60 files.

### Scope OUT (Rounds B–D)

- TextField/NumberField→TextInput/NumberInput, Anchor→Link, Nav family,
  ProgressRing→Spinner, Search renames **beyond compile-blocking sites** (Round B).
- FluentIcon default-color visual pass, `::deep` audit (43 scoped CSS files),
  list-component `SelectedItems` migrations beyond compile sites, DataGrid
  multi-sort surface (Round C). Note: scoped CSS uses the repo's own bundling
  (host `<link>` tags + RCL `bundle.scp.css`), so current `::deep` selectors keep
  functioning like today — the audit is Round C's, not now's.
- Compat-layer cleanup: `Appearance.*` → `ButtonAppearance.*` stays on the
  `Microsoft.FluentUI.AspNetCore.Components.Migration` compat layer this round;
  **except** where the compiler requires the new enum now (`DialogOptionsFooterAction`
  takes `ButtonAppearance`). No new `Migration`-namespace dependencies may be added.
- Deviation rule: if a non-Round-A file needs a purely mechanical change to
  compile, fix it **minimally** and record it in the Worker Report's deviation log
  (path + error class). This is expected for the downstream projects
  (Families, Mobile, Admin, AI.Chat, Assignments.Application, Students.Application,
  Settings.Application) that surface errors incrementally once Admin.Shared builds.

### Worker task spec (standalone — you read this plan, the code, and the docs it cites)

Read first: `.github/copilot/rules/dotnet-best-practices.md` +
`.github/skills/dotnet-best-practices/SKILL.md` (load-bearing for every `.cs`/`.razor`
edit — the "Never" list is CI-enforced by `SchoolCollab.ArchitectureTests.Unit`),
`.github/skills/dialog-ui/SKILL.md` (**plan revision 1 — superseded for this round:**
its v4 plumbing statements are overridden by this round doc's "Verified v5 API facts"
table — specifically §1's `ShowShellDialogAsync` statement (`PreventDismissOnOverlayClick
= true`), the shell table's `ComponentBase` + `IDialogContentComponent<DialogShellData<TModel>>`
row, §6/§7's `GenerateHeaderOption.Sticky` and `DialogParameters.Height`. Read it for
structural conventions only; skill refresh itself stays Round D per research doc §5.4), `documents/rounds/README.md`,
`documents/solution/fluentui-v5-upgrade-path.md` (§2, §3, §7),
`C:/Users/skwar/AppData/Local/Temp/MigrationFluentDialog.md`,
`MigrationFluentToast.md`, `MigrationFluentDesignTheme.md`, `MigrationFluentSelect.md`,
`MigrationFluentDataGrid.md`, `MigrationGeneral.md`.

Do NOT commit, push, or PR. Do NOT touch the 2 pre-existing untracked spec docs.
Write results per §"Worker Report" below.
**Build protocol:** project-scoped builds are allowed while other services run;
if `SchoolCollab.Admin.Shared` + downstream all-green is pending, prefer
`dotnet build src/SchoolCollab.Admin.Shared/…csproj` first, then
`dotnet build SchoolCollab.slnx`. If MSB3021/MSB3027 locks appear, STOP and report —
do not taskkill on your own (the AppHost `dotnet watch` ownership is an owner call).

Suggested order (each step closes an §7 error class; end state = 0 errors):

- **S1 CPM pins** (already in spike state): confirm
  `Microsoft.FluentUI.AspNetCore.Components` + `…Icons` = 5.0.1,
  `Microsoft.Extensions.Http` = 10.0.12, the 10.0.12 moves for
  `Microsoft.AspNetCore(.Web) Components` + the Extensions set from §7. Clean restore
  is the gate (NU1605 = 0).
- **S2 Admin.Shared compile**: dialog infra (scope 1) + ThemeSwitcher/theming (scope 2)
  + DropdownForEnum/DropdownComponent/CodedValueDropdown/CodedValueDialog +
  LandingGridSettings (scope 3). Gate: `dotnet build` on
  `SchoolCollab.Admin.Shared.csproj` = 0 errors (closes §7 rows 1–5).
- **S3 Providers + roots**: `<FluentProviders>` at the three host roots (scope 2).
- **S4 Downstream discovery loop** (one bounded pass): build Families → Mobile →
  Admin → AI.Chat → Assignments.Application → Students.Application →
  Settings.Application in turn; fix each project's forced errors (mechanical-set +
  deviate-and-log rule). If the loop exceeds ~25 error sites or a non-mechanical fix appears, STOP the
  loop and report — do not improvise structural rewrites. **Exception (plan
  revision 1):** the TryGet→typed-`[Parameter]` conversion of the 8 ReadOnly-contract
  dialogs in Students.Application (scope 1) is *planned* work, not a stop trigger —
  converting them and updating their `ShowReadonlyDialogAsync` callers is in-scope
  and must be logged in the Worker Report as the round's contract change, not as
  an "unplanned deviation".
- **S5 Toast rename** (scope 3, 13 files/22 sites) — can be done with S4.
- **S6 Test fallout** (scope 4): update the 37 grep-named dialog/theme test files +
  any `IToastService`/`GenerateHeaderOption` fakes where forced.

### Reviewer task spec (static, diff-only — `diffs-fluentui-v5-rounda.patch`)

- Verify the compat shim rule: no NEW `Migration`-namespace usages added in source.
- Verify no call-site churn beyond forced edits (especially the ~40 dialog-call-site
  and 13 toast files: signature-preserving ports only).
- Verify scope discipline: no TextField/Anchor/ProgressRing renames beyond
  compile-blocking sites; no `::deep` edits; no test re-spec; the 2 untracked
  spec docs and `dotnet watch` process state untouched; **no commits**.
- Spot-check the dialog port against §"Verified v5 API facts" (binary-verified API,
  not guide-only): `FluentDialogInstance` base, `IDialogInstance` cascade,
  `DialogOptions.Parameters`, `ShowDialogAsync<TDialog>` single-generic,
  `ShowSuccessToastAsync(title, message?)`.
- Verify the honest-test rule (below) holds for every touched test.

### Acceptance criteria (each checkable)

- **AC-1** `dotnet build SchoolCollab.slnx` (from repo root, `.slnx`) = **0 errors**;
  restore NU1605 = 0. Closes §7 error classes: `DesignThemeModes`/`OfficeColor`
  (RZ/C S0246), `FluentSelect` generic inference (RZ10000/RZ10001),
  `bind-Value` inference (RZ9991 ×5), `IDialogContentComponent`/`DialogParameters`/
  `IDialogReference` (CS0246 ×32), `GenerateHeaderOption` (CS0246). Target ~5 distinct
  classes / 50 error instances from the spike inventory (32 CS0246 + 10 RZ9991 +
  6 RZ10000 + 2 RZ10001); downstream projects add the bounded-discovery delta.
- **AC-2** `dotnet test` = **0 failures** on the affected projects:
  `SchoolCollab.Admin.Tests.Unit`, `SchoolCollab.Students.Tests.Unit`,
  `SchoolCollab.Assignments.Tests.Unit`, `SchoolCollab.Settings.Tests.Unit`,
  `SchoolCollab.Families.Tests.Unit`, `SchoolCollab.Mobile.Tests.Unit`,
  **and** `SchoolCollab.ArchitectureTests.Unit` (dotnet-best-practices gate).
  Run the full suite if time allows; the named projects are the minimum bar.
  **Honest-test rule:** a test whose assertion cannot fail against the pre-fix code
  (e.g. only the fluent version pinned, or a tautology introduced while porting)
  must NOT be counted as discriminating; it is at best a compile check and must be
  reported as such in the Worker Report — not silently left at "test passes".
- **AC-3** Dialog seams ported per the verified facts: `DialogShellBase` derives from
  `FluentDialogInstance`, closes via `DialogInstance.CloseAsync(...)`/`CancelAsync()`,
  `ShowShellDialogAsync` uses `ShowDialogAsync<TDialog>(DialogOptions)` with content in
  `DialogOptions.Parameters`, `PreventDismissOnOverlayClick` is **dropped** (v5 removed it). The
  dismissal posture is governed by pinned **Q8** below: verify at build whether v5
  `DialogOptions` (`Modal=true`, `PreventDismissOnEscape`, …) preserves the current
  overlay- and ESC-dismiss-blocked posture across the ~33 shell dialogs; if the delta
  is real and unrestrainable, report it as a **blocking deviation** — it is not
  worker-settled. The shell's own
  Cancel/Save footer behavior (no FluentUI footer actions: `Footer` left unset /
  primary+secondary as null) is preserved.
- **AC-4** Theming works without `FluentDesignTheme`: both MainLayouts render the
  light/dark custom-property blocks + `color-scheme`, persistence via the two
  existing localStorage keys, accent picker produces `--colorBrand*` overrides
  (pinned Q4/Q5). `ThemeSwitcher`'s `@bind-Mode`/`@bind-Accent` public surface kept
  (types may change — record new shapes in Worker Report).
- **AC-5** `<FluentProviders>` present at all 3 host roots; `AddFluentUIComponents()`
  registrations unchanged in the 3 hosts; `Directory.Packages.props` shows the exact
  pin moves (FluentUI 5.0.1 ×2, Http 10.0.12, plus §7's 10.0.12 set).
- **AC-6** Deviation log present in the Worker Report: every file edited outside
  scope 1–3 with its error class; zero edits to the 2 untracked spec docs; zero
  `Migration`-namespace additions; zero commits by the worker.

### Verified v5 API facts (5.0.1 binary + xml, 2026-10-15 — worker trust but re-verify at build)

| Area | Verified shape |
|---|---|
| Show | `IDialogService.ShowDialogAsync<TDialog>(DialogOptions)` (single generic; constraint `ComponentBase`) or `(Type, DialogOptions)`; `ShowDrawerAsync<TDialog>` analogs. No `ShowDialogAsync<TComponent,TData>(data, params)`, no `IDialogContentComponent<T>`, no `DialogParameters`, no `IDialogReference` (→ `IDialogInstance`). |
| Content params | `DialogOptions.Parameters : IDictionary<string, object?>` — each entry **must correspond to a `[Parameter]` property** of `TDialog` (v5 spreads parameters; v4's `Content.TryGet` dict is superseded). |
| Body base | `FluentDialogInstance : ComponentBase` with `[Cascading] IDialogInstance DialogInstance` + `Localizer`. Close/cancel via `DialogInstance` (`CloseAsync()`, `CloseAsync(DialogResult)`, `CloseAsync<T>(T)`, `CancelAsync()`). Caller awaits `IDialogInstance.Result` (`Task<DialogResult>`, `Cancelled`/`Data`). |
| Options | `Header` (`DialogOptionsHeader{ Title, CloseAction, InfoAction, AddAction }`), `Footer` (`DialogOptionsFooter{ PrimaryAction, SecondaryAction, PrimaryFirst }` of `DialogOptionsFooterAction{ Label, Appearance: ButtonAppearance, Disabled, OnClickAsync, Visible, … }`), `Width`/`Height`/`Modal`/`Alignment`/`PreventDismissOnEscape`/`OnStateChange`/`Size`/`Margin`/`Padding`/`Data`. **Removed**: `PreventDismissOnOverlayClick`, `Aria*`, dialog-body style; `DialogType` → `Alignment` (`DialogAlignment.Default/Start/End`). |
| Toast | `IToastService` gone entirely. `INotificationService` (`NotificationService` impl, auto-registered): `ShowSuccessToastAsync(string title, string? message = null, int? lifetime = null, string? dismissLabel = null, Func<ToastEventArgs,Task>? = null)`, `ShowSuccessBarAsync(string section, string? title, string? message)`, `ShowMessageBarAsync(MessageBarOptions)`, `CloseAsync`, `CloseAllToastsAsync`, `GetToastInstance`, plus Warning/Error variants. |
| Providers | `FluentProviders` exists (`FluentProviders(LibraryConfiguration)` ctor); cascade `LibraryConfiguration` is what root wiring restores. |
| List components | `FluentSelect`/`FluentCombobox`/`FluentListbox` : `FluentListBase<TOption, TValue>` — two generics; `Value` is `TValue?`; `SelectedOption(Changed)` removed → `Value`/`ValueChanged`; `OptionValue` returns `TValue?` |
| DataGrid | `GenerateHeaderOption` → `DataGridGeneratedHeaderType` (type rename); `Align`→`DataGridCellAlignment`, `SortDirection`→`DataGridSortDirection`; `SortByColumn`/`SortByAscending` removed from `GridItemsProviderRequest`/`DataGridSortEventArgs` (only matters where the repo reads them — Round C unless forced). |
| Theming | `FluentDesignTheme`/`FluentDesignSystemProvider`/`DesignThemeModes`/`OfficeColor` gone. CSS-variable system: `color-scheme`, `[data-theme]` blocks, `--colorBrand*` overrides; C# token constants available (`StylesVariables` etc.). |

### Pinned decisions (immovable — do not re-open)

- **Q1** (2026-10-15): adopt 5.0.1 now (not 5.0.0, not a wait).
- **Q2** (2026-10-15): spike-first phased strategy — this round is Round A of ≥4 rounds.
- **Q3** (2026-10-15): Round A is **Tier 3 full, attended** (this doc's Mode line).
- **Q4** (grill round 2): theming migrates in Round A to a self-owned CSS-variable
  system (light/dark custom-property blocks + `color-scheme`; `ThemeSwitcher` keeps
  shell + persistence). No pin-to-light deferral.
- **Q5** (grill round 2): accent-color picker kept, implemented as `--colorBrand*`
  CSS-variable overrides.
- **Q6** (grill round 2): dialog infra ports onto the v5 API
  (`FluentDialogInstance`/`DialogOptions`) in Round A — **no local compat shims**.
- **Q7** (grill round 2): spike state (pin moves) stays uncommitted on `fluentui-v5`
  as the Round A base; the two pre-existing untracked spec docs stay out of the
  round patch.
- **Q8** (pre-dispatch pin, plan revision 1 per PLAN REVIEW): shell-dialog
  overlay/ESC dismissal posture. Q6 pins the API port only and never addressed
  dismissal semantics. Procedural rule: the worker **verifies at build** whether v5
  `DialogOptions` (`Modal=true`, `PreventDismissOnEscape`, etc.) can preserve the
  current overlay/ESC-dismiss-blocked posture across the ~33 shell dialogs; if the
  delta (stray overlay click discards in-progress form state) is real and
  unrestrainable via DialogOptions, the worker MUST report it as a **blocking
  deviation** and the parent escalates the acceptance to the owner — it is **NOT**
  settled as a Q6 consequence.

### Open decisions

None — all forks in this round are pinned (Q1–Q8). The only worker-level judgment
calls are explicitly bounded and must be recorded in the Worker Report: (a) exact
`FluentProviders` insertion point per host, (b) accent hex mapping, (c) discard-vs-await
at the 22 toast sites, (d) the concrete inverted form of
`Dialog_ContentParameter_Only_For_ReadonlyDialogs`. Item (e) from the pre-revision
plan — shell-dialog overlay/ESC posture — is no longer worker-settled: it is pinned
as **Q8** (verify at build; real-and-unrestrainable delta ⇒ blocking deviation,
owner escalation). The spike's
compiler-error inventory must be resolved for `SchoolCollab.Admin.Shared` AND the
projects that compile after it — their errors surface incrementally, which is why
the plan grants exactly one bounded discovery loop (S4), not open-ended fixing.

---

## Worker Report

_(to be filled by the worker run — include: per-S1…S6 outcomes, error-class → fix
mapping, the discovery-loop delta, deviation log, toast-site tally, chosen judgment
calls (a)–(e), honest-test flags, final build/test numbers)_

## Review

_(to be filled by the reviewer run — static diff-only verdict per the reviewer task spec)_

## Acceptance

_(to be filled by the parent after review + test gates)_

## UI Tester

_(to be filled by the UI-tester run for this UI round: light/dark/accent toggle
persistence on Admin + Families, one shell-dialog open/cancel/save flow per app,
ConfirmDialog primary/secondary/ESC paths, toast appearance after
`ShowSuccessToastAsync` rename)_
---

## Review

### Plan review (step 2b, BEFORE worker dispatch — model ollama/glm-5.3:cloud, run 36aa692f)

PLAN REVIEW
Verdict: REWORK
P1: Plan §Scope IN 1 "ReadOnly contract" — TryGet-contract inventory named 3 of 8 IDialogContentComponent<DialogParameters> dialogs; missed GradeTopicsDialog.razor:5, TopicStrandsDialog.razor:9, TeacherRoleDialog.razor:6, StudentCreateDialog.razor:7, StudentEditDialog.razor:7 (src/Students/SchoolCollab.Students.Application/Components/…; last two are edit forms with concurrency-conflict handling). Callers via ShowReadonlyDialogAsync: GradeLevels/Detail.razor:869,977,1099,1226,1266; Periods.razor:410; Detail.razor:848; SectionCard.razor:315. S4's stop rule would halt the worker on exactly this foreseeable work.
P2: Plan §Scope IN 4 — misses Dialog_ContentParameter_Only_For_ReadonlyDialogs (DotNetBestPracticesArchitectureTests.cs:117): the port removes the IDialogContentComponent<DialogParameters> marker from all 8 files, so the test goes silently vacuous.
P2: Plan §Worker task spec — .github/skills/dialog-ui/SKILL.md §1/shell table/§6-§7 mandate the exact v4 constants this round deletes; needs a supersession line vs the "Verified v5 API facts" table (skill refresh stays Round D).
Open decisions: shell-dialog overlay/ESC dismissal posture — not a Q6 consequence; ➡️ verify at build whether v5 DialogOptions preserves the dismiss-blocked posture; if real and unrestrainable, escalate to owner (now pinned as Q8).
Gates: UI: risk · contract: risk (TryGet→[Parameter] change legitimate, boundary miscounted 3/8, enforcement test goes vacuous) · migrations: none · secrets: none
Acceptance honesty: ok — all ACs discriminate against the spike-state base; one untouched test (Dialog_ContentParameter_Only_For_ReadonlyDialogs) escapes the honest-test rule and goes vacuous.

Revision (run 9d0cd11a): all 5 amendments applied (8-dialog inventory + caller list; S4 stop-rule exception; invert-in-Round-A decision for the arch test; skill supersession line; Q8 pin; Open decisions → none, judgment calls (a)–(d)).

### Plan review re-read (same iteration — model ollama/glm-5.3:cloud, run 4c5fa3b4)

PLAN REVIEW (re-read)
Verdict: ACCEPT
P1: none
Notes: All 4 findings closed as amended and verified in tree (8-dialog inventory line-cites exact; arch test at DotNetBestPracticesArchitectureTests.cs:117 confirmed vacuous-by-marker-removal, invert-in-Round-A decision recorded). Residual cosmetic nit (not blocking): Worker Report template still says "(a)–(e)" — stale range vs redefined (a)–(d). Plan clears the worker for dispatch.

---

## Worker Report (pass 1 — model ollama-cloud/deepseek-v4.1-flash, run 986ec271)

Full report artifact: C:/Users/skwar/.pi/agent/sessions/--C--Users-skwar-source-repos-School-Collab--/subagent-artifacts/986ec271-a87d-4209-86e2-e5ab8bdc991a_worker_output.md
Status: PARTIAL — S1 ✔ · S2 partial (Admin.Shared 25→9 errors, ALL ThemeSwitcher.razor — HELD awaiting Q9) · S3 not started (held with theming) · S4 stop rule invoked (below) · S5 ✔ (13 files/22 toast sites) · S6 partial (arch test inverted + green; mock seam NOT converted).

- **Q8 outcome — restrained, not a blocking deviation**: 5.0.1 `FluentDialog.GetModalAttribute()` maps `Modal=true` → `type="alert"`; shipped `fluent-dialog` dismisses on backdrop click only when `type !== "alert"` → shell posture survives via `Modal = true` + `PreventDismissOnEscape = true`. `PreventDismissOnOverlayClick` gone and dropped. ConfirmDialog maps v4 `Modal=true + PreventDismissOnOverlayClick=false` → v5 `Modal=false` (`type="modal"`: backdrop kept, backdrop/ESC dismissal allowed — v4 parity). Header X routes to `HandleCancelAsync`.
- **Contract change (deliberate, in-plan)**: `TryGet<T>` → typed `[Parameter]` on all 8 ReadOnly dialogs; key constants are `nameof(Parameter)` so **all 8 call sites unchanged**. `BuildShellParameters` → `BuildShellOptions` (DialogOptions). `DialogShellBase.OnInitializeDialog` clears footer Primary/Secondary Action labels v5 pre-fills with "OK"/"Cancel" (preserves v4 no-FluentUI-footer posture). `ReadOnlyDialogShell.razor` unchanged. Enforcement test inverted → `Dialog_Parameters_AreDeclaredAndSuppliedByCallers`, mutation-proven discriminates.
- **S4 stop rule invoked (ceiling under-estimated, not scope creep)**: 47 `SelectedOption*` attribute lines across 17 downstream files need the two-generic conversion (pattern proven on the 4 Admin.Shared wrappers: `SelectedOption(Changed)`→`Value(Changed)`, `OptionValue` → identity, object-typed `TValue` — selection never string-parsed per 5.0.1 IL); + 46 `ShowDialogAsync<…>` mock setups and 17/16 `IDialogReference`/`IDialogService` mocks in test files (two-generic overload deleted → single `ShowDialogAsync(Type, DialogOptions)`; seeding reads `options.Parameters["Content"]`); Admin's `DialogShellTests`/`EnrollmentExceptionsDialogTests`/`GradeLevelEditDialogTests` drive the real service (harness rework). Worker stopped per plan instead of improvising.
- **Silent-break inventory** (Q3 adopted): FluentTextField 115, FluentAnchor 50, FluentNumberField 32, Nav family 30, FluentHeader 2, FluentMenuProvider 1, FluentSearch 1 → render as unknown elements / vanish; ProgressRing/Switch/GridItem shims DO exist (research §2 shims claim half-true: compat layer = 3 extension classes only).
- **Build/Test**: Admin.Shared build 9 errors (all ThemeSwitcher, held) · solution build not run (protocol) · ArchitectureTests 112/112 green (+ mutation proof) · other test projects not run (blocked).
- **Deviations**: none unplanned; judgment call (c) = await all 22 toast sites; (a),(b) held with Q9; nothing staged/committed; pre-existing untracked spec docs untouched.

### Supervisor adjudications (parent, attended round — logged for acceptance)

- **A1 (worker Q2 = header/provider)**: ADOPTED — port `<FluentHeader>` → `FluentLayoutItem Area="LayoutArea.Header" Height="50"` in both MainLayouts (their files are already in the theming slice, and the theme switcher lives in the header); drop `<FluentMenuProvider />` (gone in v5, menus need no provider); retire/update `Layout_Registers_FluentMenuProvider` + `TenantsIndexTests.cs:214` under the honest-test rule.
- **A2 (worker Q3 = round posture)**: ADOPTED — Round A stays as pinned (gates = build 0 errors + tests 0 failures); silent-break inventory itemised above; deferral of the 200+ rename sites stays Round B.
- **A3 (stop rule vs pinned ACs)**: the ~25 ceiling is **superseded by the pinned AC-1/AC-3** (solution build 0 errors + tests 0 failures are hard gates) — the Select conversions and mock-seam conversions are compile-forced Round-A work, not discretionary scope. Worker is authorized to exceed the ceiling with everything logged. No owner fork.

### Owner pins (attended, grill round 4 — 2026-10-15, "agreed")

- **Q10 (theming palette mechanism) — (b) adopted.** Q4's wording is amended: the palette is
  applied through the library's shipped theme JS API (`setThemeMode` → inline custom
  properties + `data-theme="dark"` on `<body>`), **not** hand-authored light/dark
  custom-property blocks. Everything Q4 pinned *behaviourally* is preserved: the repo-owned
  `ThemeSwitcher` shell, light/dark/system cycle, `color-scheme`, and the existing
  `theme` / `families-theme` storage keys for rehydration. Rationale: 5.0.1 ships no palette
  values on disk (verified against the package static web assets) — hand-authoring = inventing
  ~40 unpinned tokens. Accepted consequence: the v5 dark palette will not be pixel-identical
  to v4's look; needs one eyeball pass at acceptance (visual parity, not a code risk).
- **Q11 (UI tester timing) — deferred to immediately after Round B**, on the condition that
  Round B follows directly. Reason: Round A's delivered UI is knowingly gutted by ~215
  silent-break open tags (FluentTextField/Anchor/NumberField/Nav/Header/Search render as
  unknown elements until the Round B renames), so a tester pass now would file P1s that
  Round B fixes anyway and would formally open the rework loop. **Round A acceptance =
  build (0 errors) + tests (0 failures) + static review** (the reviewer still covers the
  dialog/theme/provider diffs). If Round B does not follow immediately, the tester runs
  post-acceptance as a standalone pass so nothing ships unverified.

### Open forks queued (prerequisites now settled by Q10)

- **Q12** — accent implementation: library brand API (single writer) vs Q5's literal
  `--colorBrand*` overrides.
- **Q13** — storage-key ownership: repo keys (`theme`/`families-theme`) vs the library's
  `fluentui-blazor:theme-settings`, and whether to migrate existing stored preferences.
- Both are asked of the owner before the theming pass runs; the parts dispatched meanwhile
  (providers wiring, downstream list-component conversions, test mock-seam conversion) do
  not depend on them.

### Guide-informed amendments (2026-10-15, after reading all 62 dev-branch migration pages)

- **A1 refinement (guide-verified, steered to the worker):** the port is not a tag swap.
  `MigrationFluentLayout.md`: `FluentHeader`/`FluentBodyContent`/`FluentFooter`/`FluentMainLayout` are
  removed; the replacement is a grid-based `<FluentLayout>` wrapper whose regions are
  `<FluentLayoutItem Area="LayoutArea.Header|Navigation|Content|Aside|Footer">`. Both MainLayouts must
  be restructured that way.
- **`ScopedCssEnabled=false` / `DisableScopedCssBundling` — resolved, NOT adopted.** Probe result:
  it is the library/demo csproj's own choice (Blazor SDK property, `Sdk.StaticWebAssets.CurrentVersion.targets:144`);
  adopting it would disable CSS isolation for all 43 repo `.razor.css` files. Repo rules win; the worker
  was told not to touch `::deep` or those properties. Recorded in the findings doc §7.2.
- **Guide/binary conflicts resolved in favour of the binary** (findings doc §7.2): Toast→`INotificationService`
  (not `FluentMessageBar`); `FluentWizard` re-introduced (not removed); no `FluentTheme` component exists.
- **Silent-break inventory corrected to 11 components / 238 tags** (findings doc §7.3, arity-aware parse).
  Supersedes the "~215" figure in the pass-1 worker report.

### Worker pass 2 (`0939b878`, complete) — providers, layout, list conversions, mock seam

- **S3 providers (judgment call (a)):** `<FluentProviders>` wraps the whole `<Router>` in all three host
  roots — `src/SchoolCollab.Admin/Components/Routes.razor`, `…/Families/Components/Routes.razor`,
  `src/Mobile/SchoolCollab.Mobile/Components/Routes.razor` — so the `NotFoundPage` path is covered and
  `App.razor` stays untouched. `AddFluentUIComponents()` unchanged at all 3 registrations.
- **A1 + guide refinement:** both MainLayouts restructured to `<FluentLayout>` +
  `<FluentLayoutItem Area="LayoutArea.Header|Navigation|Content">`; `FluentHeader` and `FluentMenuProvider`
  gone; Admin sidebar fragment became a conditional `Navigation` item. CSS follow-through:
  `src/SchoolCollab.Admin/Components/Layout/MainLayout.razor.css` (dead `.layout-body`/`.layout-content`
  rules deleted, height-chain comment rewritten) and `src/SchoolCollab.Admin.Shared/wwwroot/app.css`
  (`.content` `flex:1 1 0` → `height:100%`). `ThemeSwitcher` / `<FluentDesignTheme>` held for pass 3.
- **Test pins retired (honest-test rule):** `MainLayoutProvidersTests.Layout_Registers_FluentMenuProvider`
  deleted (component absent in 5.0.1 → assertion unfalsifiable) and its line dropped from
  `Layout_Registers_All_FluentUI_Providers` (the `<FluentDesignTheme` pin stays for pass 3);
  `tests/SchoolCollab.Settings.Tests.Unit/Tenants/TenantsIndexTests.cs` `Render<FluentMenuProvider>()` seam removed.
- **S4 list conversion:** 26 src files / 42 sites — `TValue` + `SelectedOption(Changed)`→`Value(Changed)`,
  `@bind-SelectedOption`→`@bind-Value`, `SelectedOptions(Changed)`→`SelectedItems(Changed)`,
  `OptionValue`→`OptionValueToString` (value-attribute semantics preserved), key-bound sites keep
  `TValue="string"`. Compile-forced extras: `FluentOption`→`FluentOptionString` (18 tags/6 files),
  `OptionComparer`→`OptionSelectedComparer` (2, was a *silent* break), `ComboboxAutocomplete` dropped.
- **Mock seam:** 17 test files / 26 `IDialogReference` mocks / 46 `ShowDialogAsync<…>` setups →
  v5 `Task<DialogResult> ShowDialogAsync(Type, DialogOptions)`, content seeded via `options.Parameters["Content"]`.
  Real-service harnesses reworked: `DialogShellTests` (AC-8 rewritten onto `DialogOptions`; the Q8
  `Modal`/`PreventDismissOnEscape` posture is now a pinned assertion), `EnrollmentExceptionsDialogTests`,
  `MaterialEditDialogBunitTests` ×2. `GradeLevelEditDialogTests` needed no rework (drives the preserved
  `ShowShellDialogAsync`). `AssignmentGradeUiMarkupArchitectureTests` pin `SelectedOptionChanged`→`ValueChanged`.
- **Build:** `Admin.Shared` = 9 errors, **all** `ThemeSwitcher.razor` (8 CS0246 `DesignThemeModes`/`OfficeColor`,
  1 RZ10001 `FluentSelect` inference) + 4 RZ10012 warnings at silent-break sites (compiler evidence of degradation).
  Solution-wide build not run (protocol: Admin.Shared red first). **Downstream projects cannot compile at all**
  while Admin.Shared is red → the ~42 razor conversions have had **no compiler feedback** (reviewer + first
  green build are the checks).
- **Tests:** `SchoolCollab.ArchitectureTests.Unit` 112/112 pass. All other test projects un-runnable (compile-blocked) —
  23 edited test files are compiler-unverified.
- **Deviations:** steered scope widening (layout restructure + 2 CSS follow-throughs); compile-forced fixes above;
  ~3 turns of throwaway reflection probe before the parent's steer (probe deleted; its 3 load-bearing results kept);
  subsequently API-checked against the v5.0.1 source **tag** rather than by building.

#### New blocking classes surfaced (compile-forced, no owner decision needed) → dispatched as pass 2b

1. `ShowConfirmationAsync` v4 `(message, primaryText, secondaryText, title?)` → v5
   `(message, title?, primaryButton?, secondaryButton?)` **and** `Task<IDialogReference>` → `Task<DialogResult>`;
   **15 files / 26 sites** — a positional "fix" would silently swap the buttons (compile error *plus* silent swap).
2. `DialogParameters` type gone → **10 test files** build it in render helpers / pass as dialog `Content`.
3. `<fluent-select>` → `<fluent-dropdown>` (v5 list base renders `fluent-dropdown` + `fluent-listbox`):
   **46 refs / 13 files** (9 test files doing `Find("fluent-select")`, 4 src/CSS sites); `current-value` is no
   longer rendered by C# → `DropdownComponentTests`'s assertion needs rework, not a rename.

**Scoped-CSS / `::deep` hold: CLEARED by the parent.** The `ScopedCssEnabled` probe resolved as *not adopted*
(findings §7.2), so isolation and `::deep` stay as-is; CSS rules that name the v5 DOM (`fluent-select` →
`fluent-dropdown`) are free to be renamed to match, since that is forced by the component's DOM change.

### Worker pass 2b (`e20a753e`, complete) — the three blocking classes + the wave behind them

- **`ShowConfirmationAsync` (class 1):** 16 razor files / 25 sites converted to `var result = await X;` with
  **named** `primaryButton:`/`secondaryButton:` args; label audit recorded — every primary/secondary label
  byte-identical and order preserved. Call shape verified against the v5.0.1 tag
  (`DialogService.cs:137`). Also fixed a 4-site same-class leftover in `Students/…/GradeLevels/Detail.razor`
  (`ShowReadonlyDialogAsync` + `DialogResult.Value`), a logged deviation.
  **Three v4 semantics the library owns and no call site can restore** (reported, not "fixed"):
  (a) default title `"Confirm"` → localised `"Confirmation"` (hard-coding English would kill localisation);
  (b) **overlay posture** — v4 `ShowMessageBoxAsync` set `Modal = string.IsNullOrEmpty(secondaryAction)`, so
  every one of these sites (all pass a secondary button) rendered *no* dark overlay and a backdrop click
  dismissed; v5 `ShowConfirmationAsync` always sets `Modal = true` → backdrop click is swallowed; ESC is
  unchanged. Fail-safe (every site treats `Cancelled` as "do nothing"), but a real v4→v5 behaviour change;
  (c) message no longer raw `MarkupString` (v5 sanitises; no site passes HTML) and icon colour Success → Default.
- **`DialogParameters` (class 2):** 9 test files converted to typed seeding on the render builder
  (v5 spreads `DialogOptions.Parameters` onto `[Parameter]`s by `nameof` key) — helper-to-seeding-action
  refactors, not renames. `GuardianContactsDialogTests` re-targeted onto the live v5 mechanism
  (`nameof(GuardianId)` key + `[Parameter]`); `grep DialogParameters` over src+tests now clean.
- **`<fluent-select>` (class 3):** 5.0.1 renders `<fluent-dropdown type="dropdown|combobox">` + `<fluent-listbox>`
  with **zero** `fluent-select`/`fluent-combobox` rules in the bundle → both element names renamed
  (38 test refs / 9 files, 6 src/CSS refs / 5 files). `current-value` reworked, not renamed: both assertions now
  read `FindAll("fluent-option").Where(o => o.HasAttribute("selected"))` — the mechanism that actually exists.
- **Verification ceiling reached:** `Admin.Shared` still **9 errors, byte-identical to the pass-2 baseline, all
  `ThemeSwitcher.razor`** (+4 pre-existing RZ10012 warnings); ArchitectureTests **112/112**. **7 test projects
  cannot compile or run** (Admin, Students, Settings, Assignments, Families, Mobile, Students.Integration):
  41 files edited on the test side and every downstream razor edit are **compiler-unverified**.

#### Further test-side wave surfaced (compile errors; blocks AC-1/AC-2)

1. `SelectedOption`/`SelectedOptionChanged` (removed in v5) — **47 refs / 12 test files**; note
   `SelectedOptions` is *still valid* in v5 (do not blanket-rename).
2. `Mock<IToastService>` — 5 refs / 2 files (`DevTenantSwitcherTests`, `AssignmentIndexBunitTests`).
3. v4 dialog markup/instance asserts — 6 refs / 3 files: `ConfirmDialogTests` (`Contain("modal=\"true\"")`
   vs v5 `type="modal"`; `dialog.Instance.Instance.Parameters.Modal` → `IDialogInstance` has no `Parameters`)
   and `GradeDialogsBunitTests:158`.
4. Stale prose/crefs (non-blocking): `BuildShellParameters` mentions in 5 files, `DialogSize.cs:37–38`
   broken crefs, `DialogShellBase.cs:43`, `ReadOnlyDialogShell`, `GradeTopicsDialog.razor.css`,
   `docs/plans/*`, `documents/solution/client-abort-not-a-fault.md`.

**Sequencing forced by the build graph:** pass 3 (theming) must land before *any* test project compiles, so no
test-side conversion — this wave or pass 2's — can be verified until then.

### De-risking proposal (parent technique, pending owner word): temporary theme stub

`ThemeSwitcher.razor` (9 errors) + the two MainLayouts' `<FluentDesignTheme>` references are the only things
keeping the *entire solution* red. Proposal: a short **stub leg** that replaces the theming markup with a
compile-safe placeholder purely to run the first real `dotnet build SchoolCollab.slnx` + every test project —
converting ~126 unverified file edits into verified ones and producing the true downstream error inventory that
the test-fallout pass needs. The stub is temporary and explicitly logged; pass 3 restores the real theming
immediately after. Not started — awaiting the owner's word with the Q12–Q15 answers.

### Rule conflict found while grilling the confirmation work (probe result, 2026-10-15)

`.github/copilot/rules/blazor-components.md` (§ destructive actions) states: the reusable
`ConfirmDialog` "renders a **modal** dialog (dark overlay, `Modal = true`) that is **dismissible by
clicking the overlay** (`PreventDismissOnOverlayClick = false`) or pressing ESC. **Do NOT use
FluentUI's `ShowConfirmationAsync`/`ShowMessageBoxAsync` for destructive confirmations** — those hide
the dark overlay whenever a secondary (Cancel) button is present."

- **Not introduced by this round.** `git grep` at `HEAD` shows the same ~10 files already calling
  the library's `ShowConfirmationAsync`; pass 1/2 migrated those call sites *faithfully* (call shape +
  named args), which is exactly what the brief asked. The rule violation **pre-exists**.
- The rule's stated *rationale* is v4-specific and has gone **stale in v5**: v5's
  `ShowConfirmationAsync` always sets `Modal = true`, so the overlay is always shown (backdrop click
  then swallowed; ESC still cancels). The *instruction* — use the repo-owned confirmation — stands on
  other grounds: one mechanism, overlay+ESC dismissal parity with v4, and a single bUnit mock path.
- The repo's own `DialogServiceExtensions.ShowConfirmDialogAsync` is already v5-correct and documented
  (`Modal = false` → `type="modal"` keeps the backdrop *and* allows overlay/ESC dismissal; `Width = 420px`).
- The 25 shipped sites call the **library** API directly (`DialogService.ShowConfirmationAsync(msg,
  primaryButton:, secondaryButton:)` + `result.Cancelled`), i.e. routing them through the repo helper is a
  real 25-site change (`result.Cancelled` → `!confirmed`) in 16 files, plus the 2–3 test files that mock
  `ShowConfirmationAsync` (`PeriodEditPageTests`, `PeriodsLandingGridTests`).

### Owner pins (attended, grill round 7 — 2026-10-15: "all as recommended")

- **Q12 → (a)** Accent via `IThemeService.SetThemeAsync(color, isExact)` — one writer, same `--colorBrand*`
  tokens Q5 pinned; `GetBrandColorAsync()` rehydrates the picker.
- **Q13 → (a)** The library store `fluentui-blazor:theme-settings` becomes the single source of truth
  (read `GetThemeSettingsAsync()`, write `SetThemeAsync()`), plus a one-time migration of the legacy
  `theme` / `families-theme` keys. Rejected (b) (cannot apply pre-circuit → dark-mode load flash) and (c).
- **Q14 → (a)** Adopt the guides' documented `<head>` CSS flash mitigation now; verify at acceptance and
  escalate to a cookie/inline-script only if a flash is actually observed.
- **Q15 → (a)** Install/inspect the library-shipped first-party skill
  (`examples/Demo/FluentUI.Demo.Client/wwwroot/skills/fluentui-blazor-usage/`) at round close and cite it
  from the findings doc.
- **Q17 → yes** Run the temporary stub leg before pass 3: theming markup replaced by a compile-safe
  placeholder *purely* to obtain the first full solution build + the real test inventory. Temporary, logged,
  superseded by pass 3.
- **Q18 → (a)** Fix the pre-existing rule conflict now, in Round A: route all ~25 library
  `ShowConfirmationAsync` sites through the repo's `ShowConfirmDialogAsync` (`result.Cancelled` → `!confirmed`),
  16 files + 2–3 test files. Removes the v4→v5 dismissal delta and restores compliance with
  `.github/copilot/rules/blazor-components.md`.
- **Standing instruction from the owner this round:** **update the repo-owned skill(s)/rules to match v5**
  (`::` no vendored skills — `.github/skills/*` and `.github/copilot/rules/*` are editable repo content).

### Remaining leg plan (sequential, one writer per tree)

1. **Stub leg** (dispatched) — compile-safe theming placeholder + first full `dotnet build SchoolCollab.slnx`
   + every test project; deliverable = the true downstream error/test inventory. No fixes.
2. **Pass 3 (real theming)** — `ThemeSwitcher` on `IThemeService` per Q12–Q14 (owned shell/cycle/persistence,
   library store as source of truth, one-time legacy-key migration), plus Q18(a) confirmation unification.
3. **Test-fallout leg** — the compile-blocking wave (`SelectedOption`/`SelectedOptionChanged` 47 refs / 12 files,
   `Mock<IToastService>` 5 refs / 2 files, v4 dialog markup/instance asserts 6 refs / 3 files) → first green
   build + full test run.
4. **Parent work (not delegated):** v5 update of `.github/skills/dialog-ui/SKILL.md` and
   `.github/copilot/rules/blazor-components.md` (+ any other v4-only API mentions in repo-owned rules/skills),
   then freeze `diffs-fluentui-v5-rounda.patch`, static review, `## Acceptance`, `**Status:**`.

### Skill / rules v5 update (owner instruction, 2026-10-15) — parent work, done so far

Governing rule applied: **a skill must describe the code as it is.** So v5 API/posture changes the round
has *already made* were updated now; component **renames the tree has not yet performed** stay as
written (they are still accurate today) and are queued to their rename round.

**Updated now** (all repo-owned, editable content):

| File | v5 changes |
|---|---|
| `.github/skills/dialog-ui/SKILL.md` | New v5 status block (v4 dialog types gone → `FluentDialogInstance`/`IDialogInstance`, `DialogOptions`); `DialogShellBase` table row rewritten (`Content`, `IDialogInstance` cascade, `OnInitializeDialog`/`OnActionClickedAsync`); `DialogServiceExtensions` row now names `BuildShellOptions`/`ShowShellDialogAsync`/`ShowReadonlyDialogAsync`/`ShowConfirmDialogAsync`; the `PreventDismissOnOverlayClick = true` bullet replaced by the v5 posture (`Modal = true` → `type="alert"`, `PreventDismissOnEscape = true`); checklist + §7 height guidance moved from `DialogParameters.Height` to `DialogOptions.Height` and re-verified against 5.0.1 |
| `.github/copilot/rules/blazor-components.md` | Toast rule rewritten for v5 (`INotificationService`, awaited `ShowSuccessToastAsync`, the full toast/bar method set, interface-only injection); destructive-confirmation rule got the v5 rationale (`Modal = false` → `type="modal"` = backdrop kept + overlay/ESC dismissible; `Modal = true` → `type="alert"` swallows the backdrop) plus the Round-A note that the ~25 library-API sites were routed through the repo helper; `FluentAnchor` section marked as removed in 5.0.1 (`FluentLink`/`FluentAnchorButton`, silent `RZ10012` until renamed); drawer section marked superseded with the verified v5 mapping (`DialogType.Panel` → `DialogOptions.Alignment` + `<fluent-drawer>`, `DialogParameters` → `DialogOptions`, two-generic `ShowDialogAsync` gone, `IDialogContentComponent<TData>` gone) and pointed at `MigrationFluentDialog.md` rather than inventing the header-`CloseAction` mechanics |
| `.github/copilot/rules/section-card.md` | `IDialogContentComponent<DialogParameters>` heading/bullet replaced by the v5 typed-`[Parameter]` mechanism (+ the `XxxKey = nameof(Xxx)` key convention and the `dialog-parameter-binding.md` pointer); the `await dialog.Result` code block corrected — v5 `ShowReadonlyDialogAsync` returns the `DialogResult` directly, there is no `IDialogReference.Result` task |
| `.github/skills/dropdown-ui/SKILL.md` | New §0 v5 status table for the list components (`SelectedOption(Changed)` → `Value(Changed)`, `SelectedOptions` → `SelectedItems`, required `TValue`, `OptionValue` → `OptionValueToString`, `OptionComparer` → `OptionSelectedComparer`, `FluentOptionString`, `ComboboxAutocomplete` removal, DOM `<fluent-select>` → `<fluent-dropdown>`), the CSS/test-selector and `current-value` consequences, and the two inline v4 spellings in §3/§5 |

**Queued (not yet stale — the code still matches):**

- **Round B (the rename round):** the component *names* in `.github/copilot/rules/blazor-components.md`
  (input table rows, the `FluentTextField OnClear` section, width examples) and
  `.github/skills/fluentui-component-props/SKILL.md`, `.github/skills/fluentui-icons/SKILL.md`
  (`FluentNavLink`), `.github/skills/bounded-context/**` templates — update each in the same change that
  renames the code.
- **Before this round closes (parent):** `.github/skills/fluentui-component-props/SKILL.md`'s
  `FluentDialog` / `DialogType` (via `DialogParameters`) section — the dialog API it documents is already
  invalid (done last so it matches the final `DialogOptions` surface after pass 3).
- **Round D:** the durable specs under `documents/specs/**` that name v4 dialog/toast APIs (10+ files),
  and the project-memory copies of these lessons.

### Stub leg (`e60275af`, complete) — the deadlock broke, and it revealed more

- **Stub diff (3 files, nothing else):** `ThemeSwitcher.razor` body → 21-line inert placeholder carrying the verbatim
  marker `@* TEMPORARY STUB (round-fluentui-v5-rounda, pass 3 replaces this) … *@` (surface temporarily
  `string? Mode`/`ModeChanged`/`string? Accent`/`AccentChanged`, `IDisposable` dropped); Admin + Families
  `MainLayout.razor` had their `<FluentDesignTheme …>` roots removed under the same marker, with
  `DesignThemeModes`/`OfficeColor`/`LuminanceChangedEventArgs` (all absent in 5.0.1) replaced by string
  placeholders so `@bind-Mode`/`@bind-Accent` still compile. No test file touched.
- **First full solution build (the deliverable):** `dotnet build SchoolCollab.slnx` → **20 errors, all in
  `SchoolCollab.Admin.Shared`**; the 9 theming errors are gone. 298 warnings (112 NU1903 + 27 NU1902 advisories,
  13 CS0618 obsolete-`Appearance`/`FluentProgressRing`, 4 RZ10012 silent-break, 2 CS8603, 1 ASPIRE010).
  9.30 s, no MSB3021/3027 lock.
- **28 projects build clean**, including all four Api/Worker hosts, MigrationService and ArchitectureTests.
- **Tests runnable:** ArchitectureTests 112/112, Core 133/133, Auth 224/224, Assignments.Api 172/172,
  Settings.Api 15/15, MigrationService 9/9 → **665 passed / 0 failed**.
- **Corrected premise (my steering note was wrong, the worker's finding is right):** the ~40 errors in
  `Admin.Tests.Unit` were **not** the test-side wave — they are the 20 `Admin.Shared` errors duplicated into
  the summary. All six dependent test projects report *only* those 20 and never compile their own sources, so
  **the true test-side inventory is still completely unmasked**. Fixing `Admin.Shared` is the gate.
- **Pin status (honest, needs pass 4):** the literal `<FluentDesignTheme` no longer occurs in the Admin layout,
  so `MainLayoutProvidersTests`' pin would now **fail honestly** rather than pass vacuously — but it cannot run
  while the project does not compile. Pass 4 must replace it with the real v5 theming assertion.

#### New `Admin.Shared` compile errors — v5 renames the earlier fact table missed (all Round-A scope)

| Root cause | Sites | Fix direction |
|---|---|---|
| `MessageIntent` → **`MessageBarIntent`** | `DialogShellFooter.razor:31`, `ConfirmDialog.razor:16`, `LandingPage.razor` ×3, `EntityGrid.razor` ×2, `CodedValueDropdown.razor:57` (8) | rename the enum and its usages |
| `Appearance` enum → **`ButtonAppearance`** / **`BadgeAppearance`** (CS1503) | `DialogShellFooter` ×3, `ConfirmDialog` ×2, `Chip.razor:33` (`BadgeAppearance?`), `RowActionsMenu` ×2, `CodedValueDialog:49` (9) | consult `MigrationFluentButton.md`/`MigrationFluentBadge.md` Appearance mapping; prefer the new enum at the site, use the compat `To*Appearance()` only where a v4 value must survive |
| **`<Dialog>` component removed** | `CodedValueDialog.razor:165` | v5 body base `FluentDialogInstance` + cascading `IDialogInstance` (`DialogInstance.CloseAsync()`) — mirror the tree's own read-only dialogs; `MigrationFluentDialog.md` |
| CS1662 in generated `.g.cs` (×2) | `LandingPage`, `CodedValueDialog` | cascade of the above (lambda/`Action` conversion from the `Appearance`/`Dialog` sites) — expect them to clear with the three root fixes |

### Leg plan (revised after the stub report)

1. **Leg A (dispatched):** fix the 20 `Admin.Shared` errors, **stub left in place**, then re-run the full solution
   build and the *now meaningful* full test inventory. No test-file fixes.
2. **Leg B (pass 4):** real theming on `IThemeService` per Q12–Q14 (delete the stub), plus the honest
   `MainLayoutProvidersTests` pin.
3. **Leg C:** Q18(a) confirmation unification (25 sites → `ShowConfirmDialogAsync`).
4. **Leg D:** test-side fallout → first green build + full test run.
5. Freeze diff → static review → `## Acceptance` → status flip; skill tail (`fluentui-component-props` dialog section) before close.

### Leg A (`350dfcc4`, complete) — 20 fixes applied; the real total is 101

- **PART 1 done and verified:** all 20 `Admin.Shared` errors fixed in 8 files —
  `MessageIntent`→`MessageBarIntent` (×8), `Appearance.Outline`→`ButtonAppearance.Outline` /
  `Appearance.Accent`→`ButtonAppearance.Primary` / `BadgeAppearance` (×9, values taken from the guides'
  mapping tables, not guessed), `<Dialog>`→cascading `IDialogInstance` (`CodedValueDialog.razor:165`).
  `dotnet build src/SchoolCollab.Admin.Shared` → **exit 0, 0 errors**; both CS1662 cascades cleared with the
  root fixes; the 4 obsolete-`Appearance` CS0618 warnings also cleared as a side effect.
  `FluentProgressRing` CS0618 + RZ10012 warnings left untouched, as instructed.
- **PART 2 — the floor became the truth:** `dotnet build SchoolCollab.slnx` → **101 errors, zero test-side**.
  With `Admin.Shared` green, MSBuild stopped skipping its dependents and a src-side wave surfaced in the three
  that now compile: `Students.Application` **66** (65 × RZ9991 `@bind-Value` on removed
  `FluentTextField`/`FluentSearch`; 1 × CS0305 `FluentAutocomplete` two type args),
  `Families` **32** (19 × `MessageIntent`, 5 × `Appearance`, 5 × CS1662 cascades,
  2 × CS0117 `Color.Red`/`Color.Green` **removed**, 1 × RZ9991), `AI.Chat` **3**.
- **Consequence:** Round A's AC-1 ("build 0 errors") is **unreachable** without the
  `FluentTextField`→`FluentTextInput` rename wave — those are compile-forced errors, not optional.
  The round boundary was drawn on a false premise → raised to the owner as **Q19** (absorb the
  error-producing sites into Round A per the A3 precedent, leaving the warning-only silent breaks to Round B).
- **Test inventory (finally partly measurable):** `Mobile.Tests.Unit` is the only named test project that
  compiles → **141/141 pass**; `ArchitectureTests.Unit` **112/112**; the other six remain dependency-blocked,
  reporting only the src errors (0 of their own) → **the test-side wave is still unmasked**.
- **Pin:** `MainLayoutProvidersTests`' `<FluentDesignTheme` literal count in the Admin layout is now **0**
  (stub marker intact), so the pin would fail honestly rather than pass vacuously — it cannot run while the
  project does not compile. Pass 4 (Leg B) replaces it.
- **New guide facts to respect in the rename leg:** the `Color` enum lost values (`Red`, `Green` removed) and
  renamed `Neutral`→`Default`, `Accent`→`Primary` (`MigrationColor.md`); `FluentAutocomplete` in v5 takes two
  type parameters; `Appearance` splits per component (`ButtonAppearance` / `BadgeAppearance` /
  `FluentInputAppearance`).
- Worker stopped on supervisor instruction; no further builds, no test file touched, nothing staged.

### Leg order (Leg B started, independent of Q19)

- **Leg B (running):** real theming on `IThemeService` per Q12–Q14 — deletes the stub.
- **Leg B0 (awaiting Q19):** the compile-forced rename wave + the Families/AI.Chat mechanical pass.
- **Leg C:** Q18(a) confirmation unification (25 sites → `ShowConfirmDialogAsync`).
- **Leg D:** test-side fallout → first green build + full test run.

### Owner pin — Q19 (attended, 2026-10-15: "all as recommended"): round boundary moved

**Q19 → (a) adopted.** Round A absorbs the **error-producing** (compile-forced) rename sites, applying this
round's own A3 precedent ("compile-forced work belongs to Round A"); **warning-only** silent-break sites stay
in Round B. Concretely, Leg B0 covers:

- `src/Students/SchoolCollab.Students.Application` — 66 errors (65 × RZ9991 `@bind-Value` on the removed
  `FluentTextField`/`FluentSearch` → `FluentTextInput`; 1 × CS0305 `FluentAutocomplete` now needs two type args).
- `src/SchoolCollab.Families` — 32 (19 × `MessageIntent`, 5 × `Appearance`, 5 CS1662 cascades,
  2 × CS0117 `Color.Red`/`Color.Green` removed, 1 × RZ9991).
- `src/AI/SchoolCollab.AI.Chat` — 3.
All three must consult the guides first (per the standing owner rule): `MigrationFluentTextField.md`,
`MigrationFluentAutocomplete.md`, `MigrationFluentMessageBar.md`, `MigrationFluentButton.md`,
`MigrationColor.md` (the `Color` enum lost `Red`/`Green` and renamed `Neutral`→`Default`, `Accent`→`Primary`).

**Round B's scope is now narrowed** to the warning-only silent-break renames (`RZ10012`): `FluentAnchor`→
`FluentLink` (50), `FluentNumberField`→`FluentNumberInput` (32), the Nav family (30),
`FluentProgressRing`→`FluentSpinner` (63), plus the v5 DOM/`::deep` selector audit — a genuinely Light round.

Leg B0 is **sequenced behind Leg B** (one writer per working tree); it starts the moment the theming leg returns.

### Leg B / pass 4 (`ffc4b944`, complete) — real v5 theming, stub deleted

7 files: `ThemeSwitcher.razor` (stub deleted; real `IThemeService` switcher — 3-state
Light/Dark/System button + 6-accent picker on `FluentSelect<ThemeAccent,string>`),
**new** `Components/Theme/LegacyThemeMigration.cs` (one-time v4-key migration, mode-only, key deleted
after read, skipped-and-deleted when the v5 store already holds settings), both `MainLayout.razor`
(stub markers + dead `DesignThemeModes`/`OfficeColor`/`LuminanceChangedEventArgs` removed; Admin's
unused `ILogger` injection dropped), both `App.razor` heads (Q14 mitigation verbatim, no cookie/script),
and `MainLayoutProvidersTests.cs` (dead `<FluentDesignTheme` pin replaced by real assertions +
`Layout_Does_Not_Reintroduce_The_Removed_DesignTheme_Root`).

- **Builds:** `Admin.Shared` **PASS exit 0, 0 errors, 0 warnings**. `Admin` FAIL (138 lines, all
  `Students.Application`+`AI.Chat` — Leg B0's wave; none in the 7 files). `Families` FAIL (32, the
  documented wave; none in the 7 files). The brief's "Admin exit 0" was not achievable — Leg B0 is the gate.
- **Verified beyond the compiler** (the test project is dependency-blocked, so it probed out-of-repo with
  the repo's own bUnit 2.7.2 + 5.0.1): switcher renders 1 `fluent-button` + 1 `fluent-dropdown`
  (`aria-label="Accent colour"`) + 6 `fluent-option`s with the right names/hex values; clicking cycles
  Light→Dark→System; the scoped-CSS class survives; the migration was scripted across 8 cases including
  corrupt JSON, `"DARK"` case-insensitivity, "v5 store present → no apply but key removed" and
  "no legacy key → no removal". Also binary-verified: `IThemeService` **is** registered by
  `AddFluentUIComponents`, `ThemeMode` carries `JsonStringEnumConverter<ThemeMode>` (so the JS strings
  deserialize), and the library store key is exactly `fluentui-blazor:theme-settings`.
- **Deviations accepted (parent):** (1) accent rehydration uses `ThemeSettings.Color` rather than
  `GetBrandColorAsync()` — the latter reads the *derived* `--colorBrandForeground1` token, which cannot be
  matched back to a named accent; Q12's mechanism is unchanged. (2) The legacy **accent** is intentionally
  not migrated (mode only) → a v4 user on a non-default accent reverts to the default brand on first
  post-upgrade load; logged as a Round-D candidate, not a blocker. (3) The switcher exposes no
  `Mode`/`Accent` parameters and the layouts' `@bind-*` pair is gone — correct under Q13 (a layout-held
  mirror would be write-only dead state). (4) Migration runs before the state read-back, or the button
  would report `System` while the migrated mode is already applied.
- **Residual risk:** the new `MainLayoutProvidersTests` assertions are validated by the equivalent probe
  but not by `dotnet test` until Leg D unblocks the test project.

### Leg B0 (`88a0dc8f`, complete) — Families + AI.Chat green; wave unmasked at 406; one CI rule broken

- **Green:** `Families` 32→**0** (18 × `MessageIntent`→`MessageBarIntent`, 5 × `Appearance.Accent`→
  `ButtonAppearance.Primary`, `Appearance` helper → `BadgeAppearance?` with `Filled/Filled/Ghost`,
  `Color.Red/Green`→`BadgeColor.Danger/Success`, 1 × `FluentTextField`→`FluentTextInput`; warning-only
  `FluentAnchor`/`FluentProgressRing` left alone). `AI.Chat` 3→**0**.
- **`Students.Application`:** the 66-site pinned slice is fixed in place (52 × `FluentTextField`→`FluentTextInput`
  at the `@bind-Value` error sites only, 13 × `FluentNumberField`→`FluentNumberInput`, 1 ×
  `FluentAutocomplete<T>`→`<T,T>`) — **and 0 edits inside the 406**, verified mechanically
  (`git diff -U0 … | grep -c` → 0). The 406 are: 191 CS1503 `Appearance`, 115 CS0103 `MessageIntent`,
  55 CS1662 cascades, 44 CS0104 `DialogSize` ambiguity, 1 CS0117 `Color.Red`.
- **Solution build 446** = 406 `Students.Application` + **39 `Settings.Application`** (newly unmasked —
  same `MessageIntent`/`Appearance` class, plus ~a dozen test files elsewhere) + 1 `Families.Tests.Unit`
  (its own test-side `cut.FindComponent<FluentTextField>()`, left for Leg D per instruction).
- **18 test projects build; 12 with 0 errors; 6 blocked** by the inherited src wave (`Admin.Tests.Unit` 445,
  `Assignments.Tests.Unit` 406, `Students.Tests.Unit` 406, `Students.Tests.Integration` 406,
  `Settings.Tests.Unit` 445, `Families.Tests.Unit` 1-own).
- **⚠ CI rule broken by the theming leg (Leg B):** `ArchitectureTests.Unit` is now **111/112** —
  `NoInlineStyleOrScriptElements_InRazor` fails on the inline `<style>` blocks the Q14 flash mitigation added to
  `src/SchoolCollab.Admin/Components/App.razor` and `src/SchoolCollab.Families/Components/App.razor`
  (absent at `HEAD`). The *mechanism* (early head CSS) was owner-pinned, but the *implementation* violates an
  explicit repo rule that CI enforces, so the **repo rule wins**: the mitigation must move to a linked
  stylesheet in each host's `<head>` (still pre-render, still cached), not an inline element.
  Dispatched as the arch-fix leg.

### Arch-fix — done by the parent (the delegated run hung and was superseded)

The dispatched arch-fix leg (`b07f77ff`) hung on a mangled `/dev/null` redirect (240 s, no command controller
available for that child, so it could not be unblocked) and was interrupted with **zero edits on disk**. The
parent completed the fix directly — it was four small edits:

- `src/SchoolCollab.Admin/Components/App.razor` and `src/SchoolCollab.Families/Components/App.razor`: the inline
  `<style>` mitigation block removed; the note now points at `css/app.css` (the sheet each host links first in
  the head, so the dark background still lands before the library's startup theme applies).
- `src/SchoolCollab.Admin/wwwroot/css/app.css` and `src/SchoolCollab.Families/wwwroot/css/app.css`: the guide's
  `@media (prefers-color-scheme: dark) { body { background-color:#292929; color:#ffffff } }` snippet appended,
  with the rationale comment (repo forbids inline style/script in Razor).

Verified: `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` → **112/112, 0 failed**
(`NoInlineStyleOrScriptElements_InRazor` green again); `dotnet build src/SchoolCollab.Families` → **0 errors**
(30 warnings). `SchoolCollab.Admin` cannot be verified yet — it is still blocked by the inherited
`Students.Application` wave (406) → Leg B1.

### Owner pins — Q20/Q21/Q22 (attended, grill round 8, 2026-10-15: "all as recommended")

- **Q20 → (a)** Rename the repo's `DialogSize` to an unambiguous name — **`DialogWidth`** (it is a width
  ladder: Small/Medium/Large/ExtraLarge → `ToCssWidth()`), updating every reference plus the `dialog-ui`
  skill. Rationale: v5 added `Microsoft.FluentUI.AspNetCore.Components.DialogSize`
  (`Small/Medium/Large/Full`) — same name, near-identical member names, different meaning; a rename removes
  the ambiguity class permanently instead of aliasing around it.
- **Q21 → (a)** Keep absorbing the compile-forced wave into Round A (now ~445 src errors:
  `Students.Application` 406 + `Settings.Application` 39), with the test-side follow-up in Leg D. Not (b):
  those errors are compile-forced either way, so deferring only postpones the work while removing every
  verification gate.
- **Q22 → (a)** Extend the one-time theme migration to apply the legacy **accent** too (`primaryColor` →
  `SetThemeAsync(color, isExact: true)`) under the same one-shot guards, so a v4 user's non-default accent
  survives the upgrade. Mode-only migration satisfied Q13's letter and broke its spirit.
- **Parent sequencing decision (offered in the round, adopted):** B1 is split by project so each diff stays
  reviewable — **B1a** `Students.Application` (406 + the `DialogSize` rename), **B1b** `Settings.Application` (39).
  The mechanical `DialogSize`→`DialogWidth` sweep is done by the parent directly (the delegated shell mangles
  globs/sed); the guide-driven semantic fixes stay with a worker.

### Parent mechanical sweeps (2026-10-15, Q20/Q21 pins in flight)

Done by the parent directly (scripted, because the delegated shell mangles globs/sed — and one such hang already cost
a leg):

- **`DialogSize` → `DialogWidth`** (Q20a): **162 occurrences across 48 files** (src + tests), definition file
  renamed `DialogSize.cs` → `DialogWidth.cs`, stale doc-crefs in it fixed (`BuildShellParameters` →
  `BuildShellOptions`, `DialogParameters.Width` → `DialogOptions.Width`), the AC-8 test method renamed for
  consistency, and live docs updated (`.github/skills/dialog-ui/SKILL.md`, 3 × `documents/solution/*`,
  4 × `documents/specs/*`). **Reverted:** the sweep had also rewritten 13 past-round `diffs-*.patch` files and
  6 closed round docs — frozen artifacts must stay as delivered; `git checkout --` restored them.
  Consequence: `Students.Application` 406 → **362** (all 44 CS0104 gone); `Admin.Shared` still **0 errors**.
- **`MessageIntent` → `MessageBarIntent`** (pure token rename, no judgment): **211 occurrences across 72 files**
  (src + tests — the test-side part is Leg D work pre-empted). Binary-checked first: 5.0.1 has
  `MessageBarIntent`, not `MessageIntent`. Consequence: `Students.Application` 362 → **242**.
- **Corrected figure:** `Settings.Application` was under-reported at 39 by the solution-wide summary; built
  directly it reports **119**. So the remaining wave is 242 + 119, all of the `Appearance`/`Color`/cascade class.
- `ArchitectureTests.Unit` stays **112/112** after both sweeps.

Sequencing (parent): **B1a** = `Students.Application` 242 → 0; **B1b** = `Settings.Application` 119 → 0.
Q22(a) (legacy-accent migration) is parent work, done after B1a returns (same tree, one writer at a time).

### Leg B1a (`1efd94ef`, complete) — `Students.Application` 242 → 0

45 files. Guide-cited mappings: `Appearance.Accent`→`ButtonAppearance.Primary` (43), `Outline`→`Outline`,
`Lightweight`→`Transparent`/`Ghost`, `Neutral`→`Default`/`Filled`, `Stealth`→`Subtle`, `Hypertext`→`Default`;
`Color="Color.Red"`→`BadgeColor.Danger`; six `Size="Size.Small"`→`ButtonSize.Small`; six `FluentDatePicker`
`TValue` added (generic inference was emitting an untyped `EventCallback`); the
`GuardianPickerDialog` v4 static locator `Dialog.Instance.Parameters.Title` → `DialogInstance.Options.Header.Title`
with `TogglePrimaryActionButton` deleted (no v5 equivalent). Verification: project **0 errors**,
`ArchitectureTests` **112/112**, solution total now **152** =
`Settings.Application` 119 + `Assignments.Application` 30 (newly unmasked) + 3 test-side.

**Process hazard worth inheriting (worker's own audit note):** its first sweep was a **line-addressed** sed
script shared across 45 files, so six sites received a *neighbouring file's* mapping; all six were found by an
independent re-scan and corrected. The parent's sweeps used token-anchored regex instead, which cannot have
that failure mode — the review should confirm no line-addressed sweep survives anywhere.

### Parent work — Q22(a) legacy-accent migration (done)

- Fact-checked the v4 payload against **4.14.2's own JS**: it writes `{ mode, primaryColor, neutralColor }`
  under the host-supplied storage name, and accepts either a hex or an `OfficeColor` **name** for
  `primaryColor` (`t == null || !t.startsWith('#') ? getHexColor(t) : t`).
- New `src/SchoolCollab.Admin.Shared/Components/Theme/ThemeAccents.cs`: the six accents (`ThemeAccent`
  record + `ThemeAccents.All`) lifted out of `ThemeSwitcher.razor` into one place so the picker and the
  migration cannot drift, plus `ResolveLegacyColor` (hex passthrough, else name → hex).
- `LegacyThemeMigration`: now applies **both** the mode and the accent (the latter with `isExact: true`,
  matching the picker) under the existing one-shot guards, and still deletes the key even on unparsable JSON.
  `neutralColor` is dropped (no v5 equivalent). `ThemeSwitcher` consumes `ThemeAccents.All`.
- `Admin.Shared` builds at **0 errors** with the change.
- **Test debt recorded for Leg D:** `LegacyThemeMigration` has no in-repo test (it was validated by an
  out-of-repo probe); Leg D adds `LegacyThemeMigrationTests` covering mode-only, accent-only, both,
  v5-store-present (skip both, delete key), absent key, unparsable JSON, and a legacy accent **name**.

### Owner pins — Q23/Q24 (attended, grill round 9, 2026-10-15: "all as recommended")

- **Q23 → (a)** Add a shared **`DialogShellBody`** wrapper that renders `<FluentDialogBody>` around a dialog's
  content — symmetric with the existing `DialogShellFooter` — and adopt it across the **33** `DialogShellBase`
  dialogs + the **4** read-only dialogs, restoring the header **title** and the **close-X** (routed through the
  cancel path `DialogShellBase.OnActionClickedAsync` already wires). Rationale: v5 renders that chrome only
  through `FluentDialogBody` (`FluentSlot.DialogTitle`/`DialogClose`), and nothing in the repo rendered it, so
  every migrated dialog was title-less and X-less **without a compiler error**; one shared primitive keeps the
  chrome contract in one place (`dialog-ui` skill §1). `dialog-ui` skill text updated in the same leg.
- **Q24 → (a)** Restore the Active/Inactive distinction (and the six status helpers) that the guide-mandated
  badge mapping flattened (`Appearance.Accent` and `Appearance.Neutral` both → `BadgeAppearance.Filled`) by
  carrying the semantics in **`BadgeColor`** instead of appearance — in Round A, not deferred. A status chip
  that cannot distinguish Active from Inactive is a readability regression, not cosmetic drift.

### Leg B1b (`e7d11a36`, complete) — source side fully green

- `Settings.Application` **119 → 0** (21 files), `Assignments.Application` **30 → 0** (17 files; the 30 was
  itself a masked undercount), plus the one authorized out-of-scope line
  (`src/SchoolCollab.Admin/.../MainLayout.razor:35` `Appearance.Stealth` → `ButtonAppearance.Subtle`) = 39 files.
- **`dotnet build SchoolCollab.slnx` → 16 errors, all test-side, 0 source-side.** `ArchitectureTests` **112/112**.
- Confirmed unmasking: the two test projects that moved 8 → 16 errors reference `SchoolCollab.Admin`, so they
  were *skipped* while it failed to compile.
- Fix classes were the proven ones (per-component `Appearance` mapping, `Color`→`BadgeColor`, arity, removed
  components) with guide citations; no line-addressed sed this time.
- **Test-side surface measured read-only by the parent** (Leg D's input): `SelectedOption*` 65 refs,
  `FluentTextField` 38, `IToastService` 5, `DialogParameters` 1, `PreventDismissOnOverlayClick` 1,
  `fluent-select` 2 — plus the compiler's 16 (CS0246/CS0305 class). So Leg D is materially bigger than the
  "7" first reported: the counts only became visible once the source side compiled.
- **Leg order (parent):** Leg D (test fallout → 0 build errors + a real test run) **first**, because a
  compiling test project is the only way to verify the pending Q23 (`DialogShellBody`) and Q24 (badge colour)
  work with bUnit rather than by probe; then Q23+Q24; then a final full test run, freeze, review, acceptance.

### Leg D (parent-continued after the delegated run hung) — compile fallout cleared; test triage is the next leg

The delegated Leg D run wedged twice on the mangled-redirect trap (no command controller for that child), so the parent
took it over. Work done, all scripted/edited by the parent (each verified by a build):

- **arity sweep**: 36 single-arg `FluentSelect<>`/`FluentAutocomplete<>` paired to `<T, T>` across 15 test files
  (guard: never auto-pair `FluentSelect<ThemeAccent, string>` — `TValue != TOption` there).
- **member renames** (60 occurrences / 16 files): `SelectedOptionsChanged`→`SelectedItemsChanged` (9),
  `SelectedOptions`→`SelectedItems` (5), `SelectedOptionChanged`→`ValueChanged` (25), `SelectedOption`→`Value` (21).
  Verified safe by boundary analysis: the repo's domain member is `SelectedOptionId` and the callbacks are
  `OnSelectedOptionChanged`, neither of which matches `\bSelectedOption(Changed)?\b`; the only src field named
  `SelectedOption` is private.
- **`FluentDatePicker`** 3 sites → `<System.DateTime?>` (type read off the dialog's model, not guessed);
  **`FluentTextField`→`FluentTextInput`** 5 sites in 3 test files (src was already converted);
  **`IToastService`→`INotificationService`** in 2 files; **`Dialog`→`DialogInstance`** in the auto-close fixture;
  **`Appearance.Neutral`→`ButtonAppearance.Default`/`BadgeAppearance.Filled`** in 2 test fixtures;
  **v4 dialog assertions** (`Parameters.Modal`/`PreventDismissOnOverlayClick`, `modal="true"`) reworked onto the
  real v5 members (`Options.Modal`, `Options.PreventDismissOnEscape`, `type="modal"`) at 3 sites;
  two `bool?`→`bool` lambda mismatches (`Disabled ?? false`); the toast `Verify`s rewritten to
  `ShowSuccessToastAsync`; and one assertion rebuilt as explicit per-field checks to dodge an overload-resolution
  artifact (not a type change — `FluentTextInput.Placeholder` is `string?`, verified against the v5.0.1 source tag).

**Solution build: 16 → 4 errors.** The remaining four are all `CS0246` in `Assignments.Tests.Unit`, referencing the
two removed components whose src sites are **warning-only** (Round B scope per the Q19(a) boundary):
`FluentBreadcrumbItem` (2 sites, `AssignmentAuthoringBunitTests`) and `FluentAnchor` (2 sites, `InstructionEditorListBunitTests`).
`FluentAnchor` has a v5 replacement (`FluentLink`); `FluentBreadcrumbItem` has **none** — `MigrationFluentBreadcrumb.md`
says "(no direct replacement)", so the repo's breadcrumb markup must be re-implemented, not renamed. Raised as **Q25**.

**Test run (all projects that compile):** Admin 691/118 failed, Settings 579/38, Students 624/5, Families 49/3,
Mobile 141/0, Architecture 112/0 → **164 failures** to triage (Leg D2). Sequencing decision (parent): **Q23/Q24 land
first**, because a sizeable share of those failures are the *expected* fallout of the not-yet-restored dialog chrome
and the flattened badge appearance; triaging them first would mean rewriting assertions twice.

### MILESTONE — `dotnet build SchoolCollab.slnx` → **0 errors** (2026-10-15)

The last two compile errors were Q25(a): the removed-API assertions in `Assignments.Tests.Unit`.
Implemented honestly, and *better* than a bare `[Ignore]` (which is runtime-only, so the file still had to compile):

- **`InstructionEditorListBunitTests`** (2 sites): the typed `FindComponents<FluentAnchor>()` +
  `Appearance == Appearance.Hypertext` assertions became a DOM-level truth — `link.LocalName.Should().NotBe("fluent-button")` —
  so the *rest* of those two tests (the "Edit Note" label, the bare-take route, the dialog-open check) keeps running.
  A comment names Round B (`FluentAnchor → FluentLink`).
- **`AssignmentAuthoringBunitTests.Breadcrumb_RendersTheTrailOnEveryRoute`**: `FluentBreadcrumbItem` has **no** v5
  replacement, so its crumb *structure* assertions (counts + per-crumb `Href`/`ChildContent`) were replaced with a
  named Round-B note; the test still runs and pins what the app renders today
  (`#authoring-breadcrumb` text). The `[Ignore]` was removed once the body compiled again — nothing is skipped.

**Test census (all projects that compile, one run each):** 3361 tests / **253 failed** —
Admin 691/118, Assignments 1165/89, Settings 579/38, Students 624/5, Families 49/3, Mobile 141/**0**,
Architecture 112/**0**. (`Students.Tests.Integration` needs infra and is out of this census, as in earlier legs.)

**Next, in this order (parent decision):**
1. **Q23 + Q24** — the pinned UI restoration (shared `DialogShellBody` → title + close-X; `BadgeColor` distinction),
   which is expected to clear a material share of the 253, and is bUnit-verifiable now that the suite runs.
2. **Leg D2** — triage the remainder; anything that is not v5-rename fallout is reported as a finding rather than
   papered over (honest-test rule).

### Q23/Q24 landed (the delegated run timed out *after* finishing the work — only its report was lost)

On-disk verification by the parent: `DialogShellBody.razor` exists and documents the two non-obvious facts
(title comes from `DialogOptions.Header.Title`; `Header.CloseAction.Visible` defaults to **false**, so the X
needs it set — `BuildShellOptions` does), **42** razor files adopt it, **31** `BadgeColor` usages landed,
`.github/skills/dialog-ui/SKILL.md` documents the new primitive, and `dotnet build SchoolCollab.slnx` stays at **0 errors**.

**But the failures did not move: 253 → 253** (Admin 119, Assignments 88, Settings 38, Students 5, Families 3).
The hypothesis that a material share were chrome/badge fallout is **falsified** — recorded so nobody re-runs it.

### Top failure class root-caused: v5 service interfaces are **not mockable** (probe, 2026-10-15)

`IFluentServiceBase<TComponent>` declares members with **`internal` accessors**:

```csharp
public interface IFluentServiceBase<TComponent> : IDisposable
{
    public string? ProviderId { get; internal set; }
    internal ConcurrentDictionary<string, TComponent> Items { get; }
    internal Func<TComponent, Task> OnUpdatedAsync { get; set; }
}
```

`IDialogService : IFluentServiceBase<IDialogInstance>` (and the notification service likewise), so every
`new Mock<IDialogService>()` throws
`TypeLoadException: Method 'set_ProviderId' in type 'Castle.Proxies.IDialogServiceProxy_n' does not have an implementation`
— Castle's proxy lives in another assembly and cannot implement `internal` abstract members. A clean
`bin`/`obj` rebuild does **not** fix it (tested). Blast radius: **36 `Mock<IDialogService>`** instances in
16 test files (+3 `INotificationService`); `TypeLoadException` lines: Admin 19, Assignments 45, Settings 25, Students 2.

**Fix (parent decision — test infrastructure only, no production or architecture change):** mock the
**concrete** class instead — verified against the v5.0.1 source tag:
`public partial class DialogService : FluentServiceBase<IDialogInstance>, IDialogService` — non-sealed, with a
**virtual** `ShowDialogAsync`, and the internal members already implemented by the class base, so Castle has
nothing left to synthesise. The helper must pass the ctor args (`IServiceProvider`, `IFluentLocalizer?`) and
register the object as `IDialogService`. Non-virtual members that a test needs to intercept get reported rather
than worked around; only then would a repo-owned seam be justified (and that *would* be an owner decision).

### Remaining failure classes (Admin sample, to be triaged as Leg D2)

- `Expected PositionLabels(harness.Dialog)` (6) — assertion drift, needs per-case reading.
- `Expected cut.FindAll("fluent-listbox") to be empty …` (4) — assertions written against the old select DOM.
- `System.InvalidOperationException: Sequence contains no matching element` (5) — finder helpers looking for
  the v4 component/args (the arity change).
- source-text pins on v4 markup (e.g. `<FluentOption Value="…">`, comment headers) — update to what the code
  now contains.
- `Expected style <null> to contain "width:80px"` — field-width inline style.
- `Unable to resolve service for type '…Co…'` — a test fixture missing a registration.

### Leg D2a (`127e7944`, complete) — 253 → **187** failures (−66)

Concrete-`DialogService` mocks replaced the unmockable interface mocks: 21 of 26 sites via one
`FluentServiceMocks.cs` helper per affected test project (4 copies — no shared test-support project exists).
Root cause independently confirmed by the worker: `IFluentServiceBase<T>` implements `ProviderId`/`Items`/
`OnUpdatedAsync` as **private final** (explicit) interface implementations.

Two non-obvious facts it documented in the helper:
1. `DialogService`'s ctor resolves `IJSRuntime` from the `IServiceProvider` handed to it, so passing bUnit's own
   provider *initialises* the service and bUnit 2.x then refuses further registrations — the mock must be built
   over a private standalone provider with a stub `IJSRuntime`.
2. `ShowConfirmationAsync`, `ShowMessageBoxAsync`, `CloseAsync`, the `Show*Async` wrappers and **every** public
   `NotificationService` member are emitted **`virtual final`** → Moq rejects Setup/Verify on them. The worker
   stopped rather than working around it, and explicitly refused to convert `DevTenantSwitcherTests`'s
   `VerifyNoOtherCalls()` because with a final member the real body runs and the assertion would go **vacuous**
   ("buying a false green") — correct call.

| Project | Failed before | Failed now |
|---|---|---|
| Admin | 119 | **114** |
| Assignments | 88 | **52** |
| Settings | 38 | **13** |
| Students | 5 | 5 (composition improved: no longer `TypeLoadException`) |
| Families | 3 | 3 |
| **Sum** | **253** | **187** |

`dotnet build SchoolCollab.slnx` stays **0 errors**. No `src/` file touched.

### ⚠ Process miss found by the parent (my error): **Q18(a) was pinned but never dispatched**

`grep` of production shows **25 `ShowConfirmationAsync` call sites** still live in 16 files
(Settings: `CodedValues/Approvals` ×2, `Children`, `Index` ×2, `EntityCodeRules/Edit`, `Index` ×2;
Students: `ActivityGroups` ×2, `Guardians`, `Periods` ×6, `PeriodSubPeriodsEditor`, `PeriodUpsert` ×2,
`SubPeriodsListDialog`, `GradeLevels/Index`, `Students/Index` ×2, `Subjects`, `Teachers`).

Consequences, all three of which the pinned decision was meant to remove:
1. The rule in `.github/copilot/rules/blazor-components.md` ("Do NOT use FluentUI's
   `ShowConfirmationAsync`/`ShowMessageBoxAsync` for destructive confirmations") is still violated in production.
2. The v4→v5 dismissal delta stands (v5 always sets `Modal = true`, so a backdrop click no longer cancels).
3. **It is why five test sites cannot be mocked at all** — they mock the final member the production code calls.

Implementing Q18(a) therefore fixes all three *and* unblocks those 5 test sites through the interceptable
`ShowDialogAsync(Type, DialogOptions)` that the repo helper uses — with no production seam required.

### Owner pins — Q26/Q27 (attended, grill round 11, 2026-10-15: "all as recommended")

- **Q26 → (a) Implement the still-pending Q18(a) now.** Route all **25** production `ShowConfirmationAsync` sites
  (16 files, Settings + Students) through the repo's `ShowConfirmDialogAsync`, and re-point the **5** test sites
  that mock the now-`final` member onto the interceptable `ShowDialogAsync(typeof(ConfirmDialog), options)`.
  Clears the rule violation, the v4→v5 backdrop-dismissal delta, and the mock blocker — no production seam.
- **Q27 → (a) Toast assertions through the rendered UI.** The 3 sites that cannot mock `INotificationService`
  (every member is `final`) are re-pointed onto the rendered outcome — real service + `FluentToastProvider`,
  assert the toast appears — rather than adding a repo-owned notification seam. No production change.

Remaining after these: the Leg-D2b drift classes (~179 failures) — ordinary triage, no further decisions expected.

### Leg D2b (`80cecf9c`, complete) — Q18(a) implemented; a **production** menu bug surfaced

- **Q18(a) done:** 25 production call sites in 16 files converted to the repo's `ShowConfirmDialogAsync`
  (`result.Cancelled` → `!confirmed`, labels byte-identical, no titles/secondary text invented). Source grep for
  `ShowConfirmationAsync` now returns only the doc-comment mention in `DialogServiceExtensions.cs:149`.
  Five of the seven targeted Admin tests re-pointed onto `ShowDialogAsync(typeof(ConfirmDialog), …)` now pass.
  **Admin 114 → 109**; Settings/Students/Assignments unchanged; build 0 errors.
- **Housekeeping:** an earlier leg had left one entry **staged**
  (`RM …/FluentTextFieldOnClearRegressionTests.cs → FluentTextInputOnClearRegressionTests.cs`) — unstaged by the parent;
  the index is clean again (the round's posture is nothing staged).

**New root cause (production, not test-only) — `FluentMenuItem` click semantics changed in v5.**
`RowActionsMenu.razor` wires every kebab action as `<FluentMenuItem Label="…" OnClick="@(…)" />` (and the
single-action path likewise), but v5's menu item no longer raises a Blazor `onclick`: bUnit reports
`Bunit.MissingEventHandlerException: … no event handler for 'onclick'. It does however have an event handler for
the 'onmenuitemchange' event`. That single change explains `Admin.Periods_Delete_*` ×2,
`Students.GradeTopicsDialogGridTests.KebabRemove_*` and `Assignments.QuestionEditorSectionBunitTests` together —
and, if the browser agrees with bUnit, it means **the row-action kebab menus do nothing when clicked**, i.e. a
user-visible regression from the upgrade. Dispatched as its own leg (D2c): confirm the v5 event from
`MigrationFluentMenu.md` + the 5.0.1 binary, fix `RowActionsMenu` (and any other `FluentMenuItem` site), update the
affected tests to drive the real event, and repair one test the worker flagged as **passing vacuously**
(`Periods_Delete_Cancelled_DoesNotCallApi` now clicks a menu item whose handler never runs, so it would pass
whether or not the confirm fired).

### Leg D2c (`854c8b49`, complete) — menu event was a *test* bug; **no production change**

**Correction to the previous entry's hypothesis:** there is **no** production bug. Verified from the v5.0.1 source/binary:
`FluentMenuItem.OnClick` exists and is typed `EventCallback<MenuItemEventArgs>`; `FluentMenuItem.razor` renders
`@onmenuitemchange="@OnChangeHandlerAsync"` and calls `OnClick` from that handler, and the handler drops events
whose `Id` ≠ the item's own `Id`. `RowActionsMenu.razor` was therefore **already correct** (it passes the v5
parameter). The defect was in the *tests*, which drove a bare `.Click()`; the canonical drive (from the library's own
`FluentMenuItemTests`) is `TriggerEvent("onmenuitemchange", new MenuItemEventArgs { Id = … })`. Only tests changed.

Numbers: Admin **109 → 107**, Assignments **52 → 33**, Students **5 → 2**, Settings 13, Families 3 →
**158 failing** (from 253 at the start of Leg D2). Build 0 errors. `Periods_Delete_Cancelled_DoesNotCallApi`'s
vacuous pass was repaired as instructed.

### Remaining failure classes (D2d input; measured, not guessed)

86 test references still use pre-v5 DOM names — `fluent-text-field` (30), `fluent-anchor` (34),
`fluent-number-field` (12), `fluent-messagebar` (7) — plus markup/attribute assertions (`accent`→`primary`,
`modal="true"`→`type="modal"`), a `FluentCard` fixture missing `AddFluentUIComponents()`, and the `FluentAnchor`
*behaviour* assertions (the anchor still renders as an unknown element — Round B converts it — so those tests need
the Q25(a) treatment: assert the DOM truth with a named Round-B note, never a weakened assertion).
Q27's toast-through-the-UI work is still outstanding and is the last pinned item.

### Leg D2d (`6b5f1607`, partial — time-boxed) — 158 → **130** failures, with several corrections

Numbers: Admin 107 → **99**, Assignments 33 → **24**, Settings 13 → **4**, Students 2 → **0**, Families 3 → 3.
Build 0 errors. No `src/` change. Probe-verified v5 rendering facts (all corrections to the parent's earlier notes):

- `FluentTextInput` renders **inside a `<fluent-field>` wrapper**, and a `class` given to `FluentTextInput` lands on
  the *wrapper* — so `fluent-text-field.contacts-value` must become `fluent-field.contacts-value fluent-text-input`.
- `FluentNumberInput` renders as `<fluent-text-input inputmode="numeric">` — there is **no** `fluent-number-input` tag.
- `FluentMessageBar` → `<fluent-message-bar>`; the v4 `intent-error` token is now `intent="error"`.
- `FluentAnchor` does not exist in 5.0.1 at all and renders as an **unknown element that keeps PascalCase**
  (`<FluentAnchor Appearance="…" Href="…">`), so `Find("fluent-anchor")` matches **nothing** — the parent's
  "the element still exists" note was wrong.
- `FluentCard` needs `Services.AddFluentUIComponents()` or it cannot resolve `LibraryConfiguration`.
- **v5's dialog renders no close button in the markup it produced** → see below.

**Q23 is incomplete (parent-verified).** `dotnet test --filter ActivityGroupSpanDialogTests` gives four real
`Bunit.ElementNotFoundException: No elements were found that matches the selector 'fluent-button[aria-label='Close']'`,
and that dialog *is* opened through `ShowShellDialogAsync` → `BuildShellOptions`, which sets
`options.Header.CloseAction.Visible = true` (line 61). So the close-X never renders despite the option. Because the
shell dialogs also set `PreventDismissOnEscape = true`, a shell dialog can currently only be dismissed by its own
footer Cancel/Save — the regression the Q23 pin exists to prevent. Root cause + fix dispatched (production change).

**Honest-test defect reported, not banked:** `ConfirmDialogTests` ~39/67 use
`cut.WaitForAssertion(() => cut.FindAll("…").Any())` — the `bool` is discarded by the `Action` overload, so those
assertions pass regardless. Also on the list: `Index_ShowsSpinner_WhileLoading` expects `"progress"` where v5 renders
`<fluent-spinner>`; five `fluent-text-field.contacts-value` selectors; and a Families `NotContain("fluent-button")`
assertion that is now false because the v5 message-bar's dismiss control *is* a button (the intent was "no Sign
button" — refine, do not widen). Items the worker flagged as possible **product** behaviour changes rather than test
drift: `ScoringFieldsSection` rendering a number as `"75.00"` with an empty TeacherGraded label,
`QuestionGenerationSection`'s tooltip text no longer rendered, and Settings' `FluentTextInput_OnClearCallback`
handler count 4 > 2 — all to be investigated, not asserted away.

### Leg D2e (crashed, work recovered) — **Q23 completed for real; 130 → 107 failures**

The async runner process died (`0xC0000409`) before writing a report, but the work it had already done is on disk
and verified by the parent: `dotnet build SchoolCollab.slnx` → **0 errors**, and
`dotnet test` per project → Admin **76**, Assignments **24**, Settings **4**, Students **0**, Families **3**
(= **107** failing, from 253 at the start of this sequence, and from 130 when it started).

**The close-X now renders** — its own log recorded `Part 1 is done (Admin 99→80; the 19 close-X refs pass)`, and the
Admin drop from 99 to 76 after its tail edits corroborates it. That retires the parent-verified gap where a shell
dialog could only be dismissed by its footer buttons (ESC is blocked by design). The production change is in
`src/SchoolCollab.Admin.Shared/Components/Dialogs/` (DialogServiceExtensions + the body/header wiring).

**Remaining classes (107):** the `FluentAnchor` refs (~34 — Round-B boundary; the component renders as a
PascalCase **unknown element**, so the DOM tag is the lower-cased component name, not `fluent-anchor`),
Assignments' anchor-click tests, the Admin tail (`contacts-value` wrapper selectors ×5, `"progress"` vs the v5
spinner, the vacuous `ConfirmDialogTests` assertions, the three investigable behaviour items), and Families'
message-bar/`NotContain` refinements.

**Parent decision (applying the precedent the owner already approved at Q25(a), so not re-asked):** every remaining
removed-component assertion is repaired by *asserting what the app actually renders today* plus a short named
Round-B comment — never a weakened assertion and never a deleted one — because leaving the branch red would defeat
the purpose of Leg D, and Round B converts those components anyway. Dispatched as Leg D2f; Q27's toast-through-the-UI
work follows it as the last pinned item.

### Leg D2f (`28ec7a1a`, complete) — 107 → **66** failures

Admin 76 → **51**, Assignments 24 → **15**, Settings 4 → **0**, Families 3 → **0**, Students 0 → **0**.
Build 0 errors; test-side only.

Probe-verified rendered truth (probe deleted): `FluentAnchor` renders as the unknown element **`fluentanchor`** and
`FluentTextField` as **`fluenttextfield`** (the lower-cased component name — hence `Find("fluent-anchor")` never
matched); their `onclick` handlers are emitted as **literal `onclick="<methodname>"` attributes with no Blazor
handler**, which is why every anchor `.Click()` throws `MissingEventHandlerException`. Applied the Q25(a) precedent
across 13 test files (selectors track the rendered tag; click assertions became rendered-truth assertions with a
named Round-B comment), fixed the spinner expectation, converted the two `fluent-text-field` sites, and **repaired
the vacuous `ConfirmDialogTests` assertions** (the discarded-`bool` `.Any()` is now a real `.Should().NotBeEmpty()`
inside `WaitForAssertion`).

**Investigations it reported rather than asserted away:**

| Item | Finding | Verdict |
|---|---|---|
| `ScoringFieldsSection` `"75.00"` | v5 formats the bound `decimal` per current culture; the model value **is** echoed | test expectation to update |
| Empty TeacherGraded label | production passes `Label="@ScoringInapplicableShortLabel"` ("Not applicable"); v5's element has empty `TextContent` — the label may no longer render as child text | **undecided** — needs one markup probe; flagged for the reviewer (possible label loss) |
| `QuestionGenerationSection` tooltips | production still passes `Tooltip="…"`; v5 markup contains no tooltip text (rendered out-of-band via JS) | expectation update, pending a UI check that v5 surfaces `Tooltip` |

**⚠ Possible product regressions to verify (flagged, not fixed):**
1. **The width ladder's mechanism moved.** `DropdownComponent`/`FieldWidth` put the width on `FluentSelect` as an
   inline `style`; in v5 that `style` (like `class`, per the last leg's probe) lands on the **`fluent-field`
   wrapper**, not on the control — the failing test `DropdownComponent_Width_EmitsInlineStyle_OnFluentSelect` is the
   evidence. Whether the ladder still sizes the visible control needs a real check, since
   `input-width-scale`/`dropdown-ui` document that mechanism as load-bearing.
2. `MediaDialog_PreviewRendersTheKindNativeElement` — a `Single(predicate)` now matches more than one element
   (v5 preview markup).
3. Source-text/regex tests compare against pre-v5 markup (`FluentOptionString` shapes).

**Q27 status:** the last pinned item is exactly this leg's first blocker — 4 × `AssignmentIndexBunitTests`
`TypeLoadException: set_ProviderId` from `new Mock<INotificationService>()`. Since **every** public
`NotificationService` member is `virtual final` (so the concrete-mock route that fixed the dialogs cannot intercept
anything), the owner-pinned Q27(a) applies: assert the **rendered** outcome instead of a call log.

### Leg D2g (`6eec9a16`, complete) — Q27 done; 66 → **57** failures

Admin 51 → **49**, Assignments 15 → **8**; Settings/Students/Families at **0**. Build 0 errors. Test-side only.

- **Q27 (pinned) complete — 4/4, strengthened, none weakened.** `AssignmentIndexBunitTests` renders the real toast
  surface (`<FluentToastProvider/>` as its own root beside the page) and asserts the rendered toast
  (`<div class="fluent-toast-provider">` → `<fluent-toast-b intent="success|error">`, text in `<div slot="title">`):
  success = exactly one success toast whose text contains "duplicated"; failure = **no** toast (a real absence, not a
  `Times.Never`). Key discovery: with no rendered provider, `ShowSuccessToastAsync` throws
  `FluentServiceProviderException`, and the page awaits the toast **inside the same `try` as the row mutation** — so a
  page rendered without the provider silently **skips the mutation**; two of the four tests would otherwise have been
  vacuous. Rendering the toast as a separate root (not a wrapper) also stops toast text leaking into page-markup
  assertions that would then have passed for the wrong reason.
- **Checkbox label: verified NOT a regression.** v5 renders `<FluentCheckbox Label="X">` as
  `<fluent-field><label slot="label">X</label><fluent-checkbox slot="input">…</fluent-checkbox></fluent-field>` — the
  label is simply no longer child text of the control. Both affected tests now read
  `QuerySelector("label[slot='label']")`, preserving the owner's short-label intent.
- Fixed the `MediaDialog` preview matchers (split into audio/video halves) and two source-text pins.

**⚠ Product items raised for the owner (neither is a test question):**

1. **The width ladder's mechanism moved and cannot be verified in bUnit.** Probe-verified rendering for
   `Width="FieldWidth.W1"` on `DropdownComponent`:
   `<fluent-field class="dropdown-component" style="width:80px; …"><fluent-dropdown slot="input">…` — the inline
   style (and the class) land on the **`fluent-field` host**, and the control carries none. Whether the host width
   reaches the visible control depends on `@fluentui/web-components`' `fluent-field` shadow CSS, which is in neither
   this repo nor the 5.0.1 package (`reboot.css`/`default-fuib.css` have no rules for it) → **only a live browser pass
   can settle it**. The worker deliberately left both width tests red rather than re-pin them to the wrapper, which
   would create exactly the "passes whether or not the feature works" test the honest-test rule forbids. Note that
   `src/SchoolCollab.Admin/wwwroot/css/app.css`'s W1–W9 comment and the `input-width-scale`/`dropdown-ui` skills all
   describe the old mechanism.
2. **`ShowSuccessToastAsync` is awaited inside the mutation's `try`** → in production the call completes when the toast
   *closes* (`timeout="7000"`), so follow-up work (e.g. the duplicate's list reload) waits up to ~7 s. bUnit's loose
   JS returns in ~26 ms, which is why no test shows it. This is a behaviour change introduced by this round's own
   toast migration (v4's `Toast.ShowSuccess` did not gate the caller) and needs a decision.

**Remaining 57 failures** are the residual unrelated classes (notes/tabs, tooltip markup, `fluent-badge
appearance="neutral"` vs Filled/Ghost, `fluent-tabs`/`fluent-text-field` selectors, period/stream/grid classes).

### Owner pins — Q28/Q29 (grill round 12) + the official AI skill installed

- **Q28 → (a)** Make the width ladder deterministic: add an explicit rule so the slotted control inherits the
  `fluent-field` host's width (instead of relying on `@fluentui/web-components` shadow CSS that exists in neither this
  repo nor the 5.0.1 package), then re-pin the two width tests onto the real mechanism and correct the W1–W9 comment
  in `src/SchoolCollab.Admin/wwwroot/css/app.css` plus the `input-width-scale` / `dropdown-ui` skills.
- **Q29 → (a)** Ungate the toast from the mutation's follow-up work: the S5 conversion made every toast site
  `await NotificationService.ShowSuccessToastAsync(…)` **inside the mutation's `try`**, and that call completes when
  the toast *closes* (`timeout="7000"`) → up to ~7 s before the list reload/navigation. Reorder so the follow-up work
  is not gated while the user-visible toast is preserved (22 sites, mostly mechanical).
- **Standing instruction (owner):** infer from the migration guide **and** the FluentUI Blazor documentation
  (`https://www.fluentui-blazor.net/`) — the docs site is a Blazor WASM SPA, so its real content is fetched as markdown
  from the repo (`examples/Demo/FluentUI.Demo.Client/Documentation/**` on `dev`), per the `fetch-rendered-web-content`
  skill.
- **Official AI skill installed (owner instruction):** `https://www.fluentui-blazor.net/Mcp/AISkills` documents the
  first-party **`fluentui-blazor-usage`** skill (4 files: `SKILL.md` + `references/{SETUP,THEMING,DATAGRID}.md`).
  Installed as a **vendored** copy at `~/.agents/skills/fluentui-blazor-usage/` (the site's own instruction is
  `.github/skills/`, but this repo's `AGENTS.md` reserves `.github/skills/` for *repo-authored, editable* content and
  keeps upstream copies user-level with provenance). Frontmatter now carries
  `source:` (site URL + raw URL), `updated: 2026-10-16 (dev branch; repo pins 5.0.1)` and `vendored: true — do not
  edit locally`. The skill's own "Key v5 Differences from v4" table and pitfalls are the first thing future sessions
  should read for this package.

### Leg D2h (`4e0516f8`, complete) — Q28(a) + Q29(a) implemented; Admin 49 → **47**

**Q28(a) — the width ladder is now deterministic (both halves pinned).**
- Production rule added to `src/SchoolCollab.Admin.Shared/wwwroot/app.css` (the shared global sheet the Admin host links,
  already the home of FluentUI web-component element rules):
  `fluent-field > fluent-dropdown, > fluent-text-input, > fluent-text-area, > fluent-number-field, > fluent-date-picker`
  `{ width: inherit; }`. Basis (decisive, not inferred): v5's `FluentSelect` renders
  `<fluent-field class="…" style="width:80px"><fluent-dropdown slot="input">`, and the web-components CSS compiled into the
  5.0.1 package's own bundle gives the field only `display:inline-grid`, `justify-items:start` and
  `::slotted([slot='input']){grid-area:input}` — **no width** — which is exactly why the ladder previously sized only
  the invisible wrapper. The v4 tag (`fluent-select`) was deliberately **not** added (the repo's own `dropdown-ui` §0
  requires v5 CSS to target `fluent-dropdown`).
- Both width tests **re-pinned rather than moved**: they assert the style on the `fluent-field` host (width/min-width/
  max-width), assert the control carries **no** style, **and** assert the slotted-control declaration exists in the real
  stylesheet — so deleting the rule breaks the ladder and fails the test. Class now 8/8 green.
- Docs corrected: the W1–W9 comment in `src/SchoolCollab.Admin/wwwroot/css/app.css`, `FieldWidth.cs` remarks +
  `ToCssStyle` summary, `.github/skills/dropdown-ui/SKILL.md` §3, and the W1–W9 bullet in
  `.github/copilot/rules/blazor-components.md` — whose pointer to a **non-existent** `input-width-scale` skill was
  replaced with the real loci (that skill exists only as a project-memory skill, not as repo content).

**Q29(a) — implemented surgically (parent-verified).** Only **one** toast site had follow-up work after the toast
(`Students/Periods/PeriodUpsert.razor` create flow): the toast is now the last statement, after
`Nav.NavigateTo`, with the reason in a code comment — `ShowSuccessToastAsync` completes only when the toast closes
(7 s default), and the root-layout `FluentToastProvider` survives the route change. The other 22 sites already had the
toast last, so nothing downstream is gated; leaving their `await` in place is correct, not an omission.

Verification: `dotnet build SchoolCollab.slnx` → 0 errors; Admin 49 → **47**; Assignments 8, Settings 0, Students 0,
Families 0 (unchanged). Docs cited by the worker: the official `FluentField.md` docs plus the 5.0.1 package's compiled
web-components stylesheet, and the vendored first-party skill was read as a cross-check.

### Skill migration (owner correction: authored skills are migrated, not removed)

The scan distinguishes **vendored** (lock entry / `source:` frontmatter → never edited, only refreshed/removed) from
**authored** (must be updated). The earlier `fluentui-blazor` removal was the vendored case (lock entry →
`github/awesome-copilot`, a full v4 guide). The 9 authored skills that still teach v4 are being migrated with the
round's verified mapping — see the leg D2i report for per-file outcomes.

### Skill migration — partial; parent verification disagrees with the leg's self-report

The leg migrated the 10 files and reported every residual mention as "legitimate" (trigger spellings, v4→v5
comparison text, pending-rename notes). The parent's independent marker scan does **not** support that for all of
them: `fluentui-autocomplete-server-search` (6 × `SelectedOption`, **zero** v5 markers),
`fix-flaky-bunit-fluentui-after-cascade` (2 × `SelectedOption`, zero v5 markers) and
`fluentui-icons-in-school-collab` (nav names, zero v5 markers) look unmigrated, `fluentui-icons` has 8 ×
`FluentNavMenu`/`NavLink`/`NavGroup` against a single v5 marker, and the repo-owned
`.github/skills/fluentui-component-props` still has `FluentTextField` ×6 / `FluentAnchor` ×2 / `DialogType` ×3.

Dispatched an audit leg whose rule is: classify **every** residual occurrence as either *intentional* (a trigger
spelling kept for discovery, a v4→v5 mapping/comparison sentence, or a code-rename-still-pending note) or a
*current-usage statement teaching a removed API* — and migrate the latter. It must report per file the line numbers of
everything it left, so the claim is checkable rather than asserted.

### Skill migration — parent audit outcome (corrects the entry above)

The entry above said an audit "leg" was dispatched and implied three files were unmigrated. **Both statements were wrong
and are corrected here.** No audit leg was dispatched — the parent performed the audit. And the three files named
(`fluentui-autocomplete-server-search`, `fix-flaky-bunit-fluentui-after-cascade`, `fluentui-icons-in-school-collab`) are
**v5-first**: their v4 names appear only in deliberate annotations. The parent's marker scan had no v5 spellings in its
pattern list (it lacked `ValueChanged`, `OptionSelectedComparer`, `OptionValueToString`, `FluentNavItem`,
`FluentTextInput`, `fluent-dropdown`), so it counted an *absent marker* as an *unmigrated file* — a false negative built
on a bad instrument, not evidence. Lesson: a marker scan measures the markers it knows; classify by reading the lines.

Verified by line: `fix-flaky` L41 "v5 binds the picker to `Value`/`ValueChanged`; v4 spelled it
`SelectedOption`/`SelectedOptionChanged`"; `fluentui-icons-in-school-collab` L9 "FluentNavLink Icon (v5 FluentNavItem
Icon)"; `input-width-scale` L11–12 and L20 map v4→v5 and use `FluentTextInput`/`fluent-dropdown`;
`hermes/fluentui-autocomplete` L12/72/93/94 annotate v4 in parentheses. **9 of the 10 files are correct as delivered.**

**One real defect found — `.github/skills/fluentui-component-props/SKILL.md`.** The leg migrated only its dialog section
(§L148). Its remaining sections still teach v4 as current usage, in the one file agents read *before* touching any
fluent component prop: L68 (`Appearance` "used by `FluentAnchor`/`FluentSelect`" — removed components, and v5 split that
enum per component into `BadgeAppearance`/`ButtonAppearance`), L82 (`FluentInputAppearance` "used by `FluentTextField`,
`FluentNumberField`, `FluentSearch`" — all renamed/removed), L90–96 (the ❌-example `<FluentTextField
Appearance="Appearance.Accent" />` is now doubly wrong and contradicts the file's own v5 table at L196), L130
(`### FluentProgressRing — Stroke` — removed). Also the description triggers list `MessageIntent`, which the repo
renamed to `MessageBarIntent`. Queued as the next docs leg (after the running test leg, one writer per tree); each enum
must be verified against the 5.0.1 package XML, not guessed, and `FluentSpinner`'s substitute for `Stroke` must be
established from the binary before it is written down.

### Leg D2j (`d149f973`, complete) — Assignments 8 → **0**, Admin 47 → **34**

Test-side rendered-truth re-pins across 14 test files, **plus a real user-visible bug fixed in `src/`**: v5's `FluentTab`
header parameter is `Header`, not `Label`, so **all 7 tab call sites rendered blank tab headers**
(`AssignmentAuthoring.razor` ×2, `Detail.razor` ×5). **Parent-verified against the 5.0.1 package XML**
(`…/microsoft.fluentui.aspnetcore.components/5.0.1/lib/net10.0/*.xml`): `P:…FluentTab.Header`,
`HeaderClass`, `HeaderStyle`, `HeaderTemplate` all exist and there is **no `Label`** — the rename is correct, not guessed.
One vacuous test repaired (`TopicEditDialogTests` asserted `fluent-text-field` *absent* — true for the wrong reason after
the rename; now asserts `fluent-text-input`). Evidence for every re-pin came from upstream razor sources fetched with
`curl -s -o <temp>`: `FluentNumberInput.razor` renders `fluent-text-input` with `string.Format(Culture,"{0:N}")` and
applies `Min` via JS (so `min` is never markup); `FluentTabs.razor` renders a plain `<div>` holding `<fluent-tablist>`
with panels as `div[role=tabpanel]` (there is no `fluent-tabs` tag); `FluentSelect` renders `fluent-dropdown` + inner
`fluent-listbox`, where `FluentListbox` renders `div.fluent-listbox`.

**Findings this leg reported rather than papered over (queued):** `<FluentTab Icon=…>` — 5.0.1 exposes `IconStart` and
`IconColor` with no `Icon`, so 5 tab icons never render (parent confirmed the XML); `FluentTabStrip.razor.css` binds
`fluent-tabs` / `fluent-tab-panel` / `fluent-tabs::part(activeIndicator)`, none of which exist in v5, so the folder-tab
chrome, panel border and indicator suppression are dead CSS; `Periods_RowActions_NonDraft_NoDelete` (a Delete item found
for a non-Draft row) and `StreamDropdownDoubleFireDiagnosticTests` (callback count 1 vs 2) are flagged as **decision
classes** — investigate and classify, never re-pin blind. All four are in leg D2k.

### Parent-side hygiene and review (this turn)

- **43 probe scratch files deleted from the repo root** (3636 KB) — `*.txt` / `*.razor` / `*.cs` dumps left by earlier
  legs' documentation fetches (`badgecolor.cs.txt`, `tree.txt`, `migbadge.md`, …). All root-level untracked entries were
  probe dumps; every legitimate new file (round doc, findings doc, `DialogShellBody.razor`, `DialogWidth.cs`,
  `LegacyThemeMigration.cs`, `ThemeAccents.cs`, the `FluentServiceMocks` / `FluentMenuItemTestEvent` helpers, the four new
  test files) lives under `src/`, `tests/` or `documents/` and was kept.
- **7 `documents/` edits reviewed and KEPT**: all 17 changed lines are pure `DialogSize`→`DialogWidth` fallout and nothing
  else. Decision: they belong to Round A, because the rename itself is Round A's (Q20(a)) and a living spec that cites a
  type which no longer exists is simply wrong. (The *frozen* artifacts — earlier rounds' `diffs-*.patch` and closed round
  docs — were already reverted for the opposite reason and stay byte-frozen.)
- **Round-D doc backlog inventoried, not silently fixed.** `git ls-files documents` at HEAD holds 29 `.md` files with ≥3
  v4-API mentions, but the majority are `documents/rounds/*` — **frozen historical records** written when v4 was current;
  rewriting them would falsify the record of what those rounds did. The durable files needing Round-D treatment are:
  `solution/dialog-parameter-binding.md` (13 × `DialogParameters`), `specs/dialog-parameter-binding-docs-followup.md` (13),
  `solution/dialog-consolidation-plan.md` (22), `solution/landing-page-wrapper.md` (10), `specs/grade-detail-modern-ui-plan.md`
  (10 × `FluentAnchor`), `specs/teacher-edit-dialog-modernization.md` (9), and
  `solution/fluentui-dead-selectedvalues-binding.md` (8 × `SelectedOption`).

### Leg D2k (`9d2c9efc`, complete) — Admin 34 → **23**; both src UI defects fixed

**PART 1 (src, both done and evidenced):**
1. `<FluentTab Icon=…>` → **`IconStart`** at all 3 call sites in
   `src/Assignments/…/Pages/Assignments/Detail.razor` (47/125/168); whole-tree grep found no others. Verified against the
   5.0.1 XML (`P:…FluentTab.IconStart`, `P:…FluentTab.IconColor`; no `Icon`).
2. `FluentTabStrip.razor.css` re-pointed to the real v5 DOM, established from the **v5.0.1 tag** razor sources:
   host = plain `<div>` holding `<fluent-tablist>` (so `fluent-tabs{width:100%}` → `fluent-tablist`); panels =
   `<div class="fluent-tab-panel" role="tabpanel">` (so `fluent-tab-panel:not([hidden])` →
   `.fluent-tab-panel:not([hidden])`); `fluent-tab[aria-selected="true"]` stays valid (the tablist JS sets
   `aria-selected`/`hidden` — confirmed in the 5.0.1 bundle). **Finding:** `fluent-tabs::part(activeIndicator)` has **no
   v5 equivalent** — 0 hits for `activeIndicator` and `exportparts` in the 5.0.1 bundle; the accent indicator is
   shadow-internal (`:host::after` + `::slotted([slot='tab'][aria-selected='true'])::before`). The dead rule was removed
   with a comment recording this (see Q32 below).

**PART 2 — 11 of 34 cleared, one decision class fixed, one left untouched:**
`ContactsEditorTests` ×4 (v5 puts the caller's class on the `<fluent-field>` wrapper, not the control),
`GradeLevelCreateEditPageTests` ×2 + `GradeLevelDetailPageTests` (v5 pads label text with whitespace),
`AssignmentPolicyEditorTests` + `GradeNotificationPolicyEditorTests` (a global `fluent-menu-item` probe now hits one
"Reset" per row → re-find the row, then query *its own* items, `Single` still enforced), `FormRowCellBunitTests`
(bUnit setup needed `AddFluentUIComponents()` for `FluentIcon`'s injected `LibraryConfiguration`).

- **Decision class 1 — `Periods_RowActions_NonDraft_NoDelete` = STALE EXPECTATION, not a product bug (fixed).**
  `Periods.razor:349-375` adds `Delete` only for `Status == "Draft"` (behaviour claim holds); the fixture lists a Draft
  row too, and v5 renders a **closed** kebab's `fluent-menu-item` children into the DOM (v4 materialised them on open),
  so the *unscoped global* probe matched the Draft row's own, correct Delete. Re-pinned to the non-Draft rows — sharper,
  same intent.
- **Decision class 2 — `StreamDropdownDoubleFireDiagnosticTests` = real v5 behaviour gap; NOT re-pinned, still failing
  (see Q31 below).** `CodedValueDropdown.SyncSelection()` only mutates `_selectedOption` and never raises
  `SelectedIdChanged`; v4's `ListComponentBase` emitted the clear when the bound item disappeared from `Items`, and v5's
  `FluentSelect` does not — so on a grade/filter change the **stale stream id stays bound**. Both scenarios observe
  `AfterValues = [<pick>]` (one entry, no clear, no double-fire); sibling scenarios pass, so the assertion is neither
  vacuous nor wrong in intent.

**Root cause of the remaining 23 failures: `FluentAnchor` does not exist in 5.0.1** (only `FluentLink` and
`FluentAnchorButton` do). The `RZ10012 … unexpected name 'FluentAnchor'` warnings are the proof, and failure dumps show
literal `<FluentAnchor …>` elements reaching the DOM with no `blazor:onclick` → `OnClick` never fires, navigation dead.
Downstream clusters: `SectionCardDialogsBunitTests`, `GradeLevelDetailPageTests` (students/teachers cards),
`GuardianDrawerAddModeTests` ×2, `GuardianContactsDialogTests`, `EnrollmentExceptionsDialogTests` (empty
`PositionLabels`), `PeriodEditPageTests`, `ActivityGroupsPageTests`, `EnrollStudentDialogBunitTests`,
`GradeLevelsTenancyTests`, plus `DevTenantSwitcherTests` ×6 (`onclick` → `onmenuitemchange` + concrete-service mocks).
The worker did **not** perform the rename — it is ~50 call sites and a Round-A-vs-Round-B scope decision (Q30 below).

Verification: `dotnet build SchoolCollab.slnx` **0 errors** (322 warnings); Admin 695 total / **23 failed**;
Assignments 1166/0; Settings 579/0; Students 624/0; Families 49/0. Nothing staged. The worker's own `tmp-Fluent*` probe
files were removed from the repo root by it.

### Grill round (posed after D2k) — Q30–Q32

The remaining work is no longer a question of *how* but of *scope and behaviour*, which is the owner's call, not the
codebase's. Three questions, each with a recommendation: Q30 (does Round A absorb the `FluentAnchor` family, or ship with
an itemised Round-B-gated failure list?), Q31 (does the stream wrapper own its clear-on-stale-selection contract, or does
the expectation get re-scoped?), Q32 (accept the v5-native tab indicator, or hand-author the v4 look?).

### Grill round Q30–Q32 — SETTLED ("all as recommended")

- **Q30 (a) — Round A ABSORBS the `FluentAnchor` family.** The warning-only/Round-B classification from Q19(a) is
  overridden *for this family only*, because it is the sole root cause of the 23 remaining failures and dead navigation is
  the very "UI regression" Round A's AC names: a suite read as "23 known failures" would erode the test gate the repo
  treats as absolute. Round B keeps the other four families (`FluentNumberField` 32, nav 30, `FluentProgressRing` 63, the
  DOM/`::deep` audit) and becomes purely mechanical.
  **Not included:** `FluentBreadcrumb`/`FluentBreadcrumbItem` (6 tags) — v5 ships **no replacement**, so it is not a
  rename but an authoring task; it stays Round B, as already recorded by the Q25(a) note in `AssignmentAuthoringBunitTests`.
- **Q31 (a) — the wrapper owns the contract.** `CodedValueDropdown` clears the selection (and raises the callback) when
  the reloaded items no longer contain the bound id, instead of inheriting v4's undocumented library side-effect. The
  diagnostic suite re-asserts **that** contract, not its absence.
- **Q32 (a) — accept the v5-native tab indicator.** No hand-authored indicator over upstream shadow internals; the comment
  recording the absent `::part(activeIndicator)` hook stays, and the deferred UI-tester pass confirms the look.

### Leg D2l (`248eb94d`, complete) — Q30(a) rename + Q31(a) wrapper contract; Admin 23 → **41**

**The number went UP, and the reason is sound + disclosed:** the rename fixed the inert-element clusters
(`EnrollmentExceptionsDialogTests`, `ActivityGroupsPageTests`, `EnrollStudentDialogBunitTests`, the
GuardianDrawer/GuardianContacts/PeriodEdit/GradeLevelsTenancy set) and simultaneously broke ~20 test pins that
referenced the **old tag name/source text** — "broke-on-fix" unmasking, not new defects (43 immediately, 41 after its own
re-pins). PART 3 (`DevTenantSwitcherTests` ×6) was not attempted: time-box blown at ~55 min, and it chose to bank a
verified PART 1+2 with a precise worklist.

**Decisive API evidence (cites source, not compile-success):** 5.0.1 has **no `FluentAnchor` type at all**;
`FluentAnchorButton` exists but exposes **no `OnClick`** (`{Href,Target,Appearance,…}`); `FluentLink` owns
`OnClick` (`EventCallback<MouseEventArgs>`, rendered as `@onclick` with `clickable="@OnClick.HasDelegate"`); and
`LinkAppearance` has exactly **two** members (`Default`, `Subtle`). Per `MigrationFluentAnchor.md`:
`Appearance.Hypertext` → `LinkAppearance.Default` (30 sites), `Appearance.Lightweight` → `LinkAppearance.Subtle` (12).
**All 45 sites therefore became `FluentLink`** — including the 20 `Href="#"` + `OnClick` sites, because classifying them
as `FluentAnchorButton` would have re-introduced the exact defect under repair (`OnClick` silently discarded into
`AdditionalAttributes`). That is the brief's own "carry no parameter over just because it compiled" rule taking
precedence over its literal mapping, and it is the right call. CSS re-pointed `fluent-anchor` → `fluent-link`
(4 files + comments).

**Q31(a) implemented:** `CodedValueDropdown.SyncSelection()` → `SyncSelectionAsync(CodedValueDropdown? priorSelection)`
reconciles `SelectedId` against the current items and **raises `SelectedIdChanged.InvokeAsync(null)` itself** when the
bound id is gone; `LoadAsync` captures the prior selection before the load blanks it and runs the reconcile in phase 2.
The "priorSelection is not null" guard makes it idempotent, so a mid-load re-render and phase 2 cannot double-fire.
The diagnostic suite's only change was to wait on the **callback** rather than on `Items.Count` (the clear lands one
`Task.Delay(1)` after phase 1) — `HaveCount(2)`, `[1] == null`, `BoundId == null` and the quiesce checks are untouched;
4/4 pass. **This is the "wrapper owns its contract" decision, implemented and tested — not an expectation re-scoped.**

**Parent verifications of this leg's disclosed deviations:**
- The 45-site `perl -pi` batch pass (instead of per-site edits) was **blasting-radius checked**: `<FluentAnchor` tags
  remaining in `src/` = **0**, `<FluentLink>` = **50**, `<FluentAnchorButton>` = 0, `fluent-anchor` CSS = **0**, and
  nothing outside `src/`/`tests/`/`documents/`/`.github/`/`Directory.Packages.props` was touched.
- The **deleted test file** `tests/…/Components/FluentTextFieldOnClearRegressionTests.cs` is a **rename, not a coverage
  cut**: 4 test methods at HEAD → 4 in the new `FluentTextInputOnClearRegressionTests.cs`, the same three assertions
  (`HasNoOnClearMember`, `DoesNotRenderBuiltInClearButton`, `OnClearCallback_DoesNotInstallEventHandler`) with the v5
  spelling. Coverage preserved 1:1.
- Warning count moved 322 → 472 between D2k and D2l builds; **not a regression** — the counts came from builds of
  different scope/reuse (RZ10012 `FluentAnchor` warnings are now *gone*). Remaining warning families are the Round-B
  renames only: `FluentBreadcrumb`/`FluentBreadcrumbItem` (6), the nav family, and `FluentTextField` in Settings.

### Grill round (posed) — Q33–Q34

Two judgment calls the leg flagged rather than decided. Q33 is a **new decision created by v5's own API shape**
(`FluentAnchorButton` has no `OnClick`); Q34 turned out to be **pre-existing**, not port-caused, once the v4.14.2 XML was
checked (`FluentAnchor` had no `Disabled` either) — so its recommendation is to defer, not to expand Round A again.

### Leg D2m (`a361d80c`) — HARNESS DEATH, work recovered; Admin 41 → **13**

The async runner process exited/disappeared before writing a result, so the run was marked failed by stale-run
reconciliation. **Diagnosis: harness death, not a worker crash mid-edit** — the run's `events.jsonl` was still being
written **1 s** before the check, and five target test files carried mtimes from 08:22:54–08:24:42Z, i.e. the leg was
productively editing right up to the failure. Its edits persisted, so this is a **resume**, never a re-run.

Parent-measured state after the death (the first unfinished step was *measurement*, and the parent does short
verification-shaped work directly):
- `dotnet build SchoolCollab.slnx` → **0 errors** (279 warnings, incremental).
- Admin `dotnet test` → **695 total / 13 failed / 682 passed / 48 s** — from 41, so the leg cleared 28 before dying.
- The 13 decompose as **10 real + 3 deliberately excluded**: `NewButton_Navigates_To_CreateRoute`,
  `OnCreate_Invokes_Callback_Instead_Of_Navigating`, `OnCreate_NotSet_FallsBack_To_NavigateRoute` are the **LandingPage**
  tests carved out pending Q33 (the owner's decision was still open when the runner died).
- The 10 in scope: `SetAsDefault_Cancelled_WritesNothing_AndKeepsTheSelection` (last of its 6 — the other 5 pass),
  `ItemOnClick_With_Null_Key_Is_Blocked` (**new to the worklists → classify, do not assume**),
  `ActivePeriod_DisplaysAcademicYearFormat_YYYY_Slash_YYYY`, `DetailsPage_ActiveChips_CarryTheActiveColour`,
  `NonEmptyList_GatesThePositionLadder`, `Positions_AreOneToFourPlusAnyHigherDeclared_NotOnlyTheExistingPeriods`,
  `Reset_UnTicksTheChosenSequence_WhereTheLadderSurvivesIt`, `SubPeriodsSection_InCellEdit_SaveCallsUpdate`,
  `AddDrawer_ToggleToExistingGuardian_SurvivesTypeaheadFocusAndInput`,
  `Component_Closes_Via_Cascading_FluentDialog_And_ReopensOnGuardianIdChange`.

Resumed as leg D2n with **the exact 10-name list** (not the old brief) plus the explicit LandingPage/AiChat carve-outs, so
the recovery cannot silently re-do finished work or cross an open owner decision. Note the harness death cost a report but
not the work — the same lesson as the earlier crashed legs: reconcile from disk, resume at the first unfinished step.

### Leg D2n (`2285c0d7`, complete) — all 10 in-scope failures fixed; Admin **3** (the excluded Q33 trio)

Parent-verified after the leg: `dotnet build SchoolCollab.slnx` **0 errors / 279 warnings**; Admin 695 total /
**3 failed / 692 passed** — and the 3 are exactly `NewButton_Navigates_To_CreateRoute`,
`OnCreate_Invokes_Callback_Instead_Of_Navigating`, `OnCreate_NotSet_FallsBack_To_NavigateRoute` (the LandingPage trio
excluded pending Q33). Assignments 1166/0, Settings 579/0, Students 624/0, Families 49/0.

Three of the ten were **product** defects, not test ports:
1. **`ConfirmDialog` was the one dialog not wrapped in `<DialogShellBody>`** — v5 renders a dialog header only through
   `FluentDialogBody`/`FluentSlot.DialogTitle`, so `DialogOptions.Header.Title` ("Set default tenant") never reached the
   DOM. Wrapped; footer deliberately left unset (it is not a `FluentDialogInstance` and `ShowConfirmDialogAsync` leaves
   the footer unset), so no built-in OK/Cancel is introduced. **Visible consequence: every confirm prompt now renders a
   title, across ~25 call sites** — the same Q23 defect class, at its last site.
2. A read-only Period field still used `FluentTextField` and therefore rendered as an inert unknown element; ported to
   `FluentTextInput`, with the test now reading `fluent-field.enroll-form-input--readonly fluent-text-input` and its
   `value` attribute (v5's `current-value` is gone).
3. `PeriodSubPeriodsEditor.StartEdit` — a parent-state mutation that needed a self-render (`StateHasChanged`).

One failure was a **half-applied edit from the dead leg** (`ItemOnClick_With_Null_Key_Is_Blocked`: the expected text was
rewritten but the fixture field that makes the fallback path meaningful never was) — repaired truthfully, not by
loosening the assertion.

**DISCLOSED, not a new decision:** ~18 `FluentTextField` tags (plus `FluentNumberField` ~32, nav family ~30,
`FluentProgressRing` ~63, `FluentBreadcrumb` 6) are still v5-absent and therefore render as **inert elements**. That is a
real, user-visible defect shipping until Round B. It is *not* escalated as a question because the settled rules already
answer it: Q19(a) assigns warning-only silent breaks to Round B, and Q30(a) absorbed only the `FluentAnchor` family —
explicitly because that family was the sole blocker of Round A's zero-failure gate. Round B's opening worklist is
therefore these five families, and the deferred UI-tester pass (Q11) is the instrument that will catch any other
silent break the green suite cannot see.

### Skill migration — `fluentui-component-props` finished BY THE PARENT (two delegated attempts failed)

The first docs leg (D2i) migrated only this file's dialog section; the follow-up leg timed out after 30 minutes **having
written nothing** (file mtime unchanged, 0 v5 markers). The parent therefore rewrote it directly, against verified 5.0.1
surface: `FluentButton.Appearance` is `ButtonAppearance?`, `FluentBadge.Appearance` is `BadgeAppearance?`, `FluentLink.Appearance`
is `LinkAppearance` (non-nullable, defaults `Default`) — all three read from the **v5.0.1 tag razor sources**, not inferred
from the enum names. Enum members from the package XML: `BadgeAppearance` = Filled/Ghost/Outline/Tint; `ButtonAppearance` =
Default/Outline/Primary/Subtle/Transparent; `FluentInputAppearance` = Filled/Outline; `LinkAppearance` = Default/Subtle;
`MessageBarIntent` = Custom/Error/Info/Success/Warning (`MessageIntent` has **zero** members in 5.0.1, confirming the repo
rename); `Color` retains `Neutral` **and** `Default` (so the coarse "Neutral→Default" mapping does not apply to that enum)
and has no `Red`/`Green`. The file's unverified leftovers (DataGrid table, `TextAreaResize`) are now explicitly marked
*(unverified)* rather than asserted. The badge migration guide (`MigrationFluentBadge.md` at the v5.0.1 tag) supplied the
`Content`/`ChildShape` split, `BadgeColor`, and the removed parameters (`OnClick`, `Fill`, `Circular`, `Width`/`Height`,
`Dismiss*`).

### FINDING (parent) — the MAUI head is a gate blind spot, and this round broke it

`src/Mobile/SchoolCollab.Mobile` is **deliberately not registered** in `SchoolCollab.slnx` (the file says so in a comment);
it builds only from its own `src/Mobile/SchoolCollab.Mobile.slnx`. Consequences:

- Every "0 errors" claim in this round covers the 28 main-solution projects and **never compiles the MAUI head**.
- The head still carries **19** v4-style `Appearance="Appearance.Outline|Accent|Neutral"` usages plus **3** `FluentProgressRing`
  (all v5-invalid). Because 5.0.1 types those parameters as `ButtonAppearance?` / `BadgeAppearance?`, those lines are
  **compile errors** — the head cannot build. It *could* build in v4, so this is a **regression introduced by this round's
  package bump**, not a pre-existing defect (contrast Q34, where v4's `FluentAnchor` had no `Disabled` either).
- The round also **edited 7 of that head's files** (`AssignmentCard`, `AssignmentRecipientsSection`,
  `AssignmentSubmissionsSection`, `Detail`, `Index`, `Spike`, `Routes.razor` — the last for `<FluentProviders>`) with no
  compiler in the loop: those edits are unverified.
- Workloads on this machine: `android`, `ios`, `maccatalyst`, `maui-windows`, so a `net10.0-windows10.0.19041.0` build of the
  head is possible (the other TFMs may not be, on Windows).
- Related doc nit found while checking: `Admin.Shared/Components/FieldDisplay.razor`'s `@* … *@` header comment still
  teaches `<FluentBadge Appearance="Appearance.Accent">`; it is not compiled, so it breaks nothing, but it is v4 guidance
  in a file agents read.

Raised as **Q35** (round A vs. the MAUI head). Until it is answered, Round A's acceptance must state the **scope of its
gate** explicitly: "0 errors over `SchoolCollab.slnx` — which does not include the MAUI head".

### Grill round Q35 — SETTLED (a): Round A absorbs the MAUI head

The owner confirmed absorbing it. Consequences recorded: Round A's gate gains a **second build target** — the MAUI head
via its own solution (`src/Mobile/SchoolCollab.Mobile.slnx`, built on Windows as
`dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj -f net10.0-windows10.0.19041.0`) — because
`SchoolCollab.slnx` does not include it. The 19 `Appearance="Appearance.*"` usages, the 3 `FluentProgressRing` sites and the
7 files this round edited blind all come into scope, and the `FieldDisplay.razor` v4 header-comment nit is fixed with them.
Acceptance can no longer say "0 errors" without naming the target it covers; it must name **both**.

### Grill round Q33/Q34/Q36 — SETTLED ("all as recommended")

- **Q33 (a) — the LandingPage create control becomes `<FluentButton Appearance="ButtonAppearance.Primary">`
  carrying `OnClick`.** The census settled the scope: of every pre-rename anchor only **one** ever carried
  `Appearance.Accent` (26 were `Hypertext`, 14 `Lightweight` — genuinely link-styled), and 11 anchors had an `OnClick`.
  `FluentAnchorButton` cannot carry the action at all (no `OnClick`), so `FluentLink` lost the accent-button look or
  `FluentAnchorButton` lost the click; `FluentButton` keeps both and matches what the host contract
  (`OnCreate` callback, else navigate to the create route) and the tests' own names (`NewButton_*`) already described.
- **Q34 (a) — AiChat's inert `Disabled` is DEFERRED as a named pre-existing defect.** Verified pre-existing: v4.14.2's
  `FluentAnchor` had no `Disabled` either, so it has never worked; the click is a legitimate user intent ("Clear chat"),
  and an inert attribute is not a regression this port caused. Recorded in the handover defects list below.
- **Q36 (a) — the UI-tester pass stays deferred to after Round B, but carries a written scope list of Round A's visible
  deltas** (below), so the deferral loses nothing.

### Handover to Round B / the deferred UI-tester pass

**Pre-existing defects (NOT caused by this port — do not present them as regressions):**
1. `src/AI/SchoolCollab.AI.Chat/Components/AiChat.razor:115` — `Disabled="@_isStreaming"` on a `<FluentLink>` is inert
   (`FluentLink` has no `Disabled`; v4's `FluentAnchor` didn't either). The streaming state cannot disable it.
   Minimal fix if ever wanted: guard `ClearAsync` on `_isStreaming`, or convert the control to a `FluentButton`.

**Round A's visible UI deltas — for the deferred UI-tester pass (Q36a):**
1. **Every confirm dialog now renders a title.** `ConfirmDialog` was the one dialog not wrapped in `<DialogShellBody>`,
   and v5 renders the header (title + close-X) *only* through it — so "Set default tenant" and its peers were previously
   title-less. ~25 call sites see this change.
2. **Folder-tab chrome re-pointed to the real v5 DOM** (`fluent-tablist`, `.fluent-tab-panel[role=tabpanel]`), and the
   v4 `::part(activeIndicator)` accent indicator is now the **v5-native** one (no equivalent hook exists in 5.0.1) —
   the indicator's look should be eyeballed.
3. **45 anchors converted** from inert elements to `FluentLink`: 30 `Appearance.Hypertext` → `LinkAppearance.Default`,
   14 `Appearance.Lightweight` → `LinkAppearance.Subtle`, and the single accent one above to a button. The 14 `Subtle`
   conversions are the ones most likely to look different.
4. **The MAUI head** (`src/Mobile/SchoolCollab.Mobile`) is being ported with no runtime/visual validation at all — it is
   not in `SchoolCollab.slnx` and has never been exercised by this round's gates.
5. Inputs and dropdowns are unchanged in look but **now render inside a `<fluent-field>` wrapper** (v5), which is where
   width/`class` land — the W1–W9 width ladder was made deterministic for that (Q28a); worth a visual confirmation.

### Independent static review (`cdc9831b`, complete) — verdict **BLOCK**, on one finding

The first review attempt died reading the whole ~250-file diff; the narrowed, pattern-driven pass completed. Its verdicts:

- **Step 1 (test honesty) — BLOCK.** One defect, and it is this round's own: the two
  `CreateEnabled_False_Hides_NewButton` tests assert the **absence of the obsolete `<fluentanchor>` tag**, which can never
  exist now that the control migrated off `FluentAnchor` — so they pass whether or not `CreateEnabled=false` actually hides
  the New control. That is exactly the "a test that passes whether or not the feature works" case the honest-test rule
  forbids. Steered into leg D2p (which is already in those files for Q33) to assert the real v5 truth: the control is
  findable when `CreateEnabled=true` and **not** findable when `false`. Everything else honest — the reviewer found no
  `[Ignore]`/`Skip`/commented-out tests and no source-level test filters, and no other tautological assertion.
- **Step 2 (high-risk custom code) — OK.** `ConfirmDialog` correctly wrapped in `<DialogShellBody>`/`<FluentDialogBody>`
  and footer-free (`ShowConfirmDialogAsync` leaves `Footer` unset, and the dialog is not a `FluentDialogInstance`, so no
  built-in OK/Cancel leaks in); `CodedValueDropdown.SyncSelectionAsync` raises `SelectedIdChanged(null)` **only** when
  `selected is null && priorSelection is not null`, with `_isLoadingItems` suppressing spurious reassertions across the
  phase-1/phase-2 load — exercised by the diagnostic suite's Scenario C (mid-load re-render, exactly one clear);
  `LegacyThemeMigration.RunAsync` runs before the theme read-back and its one-shot guard is correct.
- **Step 3 (new test helpers) — OK.** Concrete `DialogService` mocks (`IFluentServiceBase<T>`'s internal accessors make
  `Mock<IDialogService>` throw) with the real `ShowDialogAsync(Type, DialogOptions)` → `DialogResult` shape, and
  `MenuItemClick` raising `onmenuitemchange` with `MenuItemEventArgs { Id = … }`.
- **Step 4 (silent-break inventory) — OK for source.** No `FluentDesignTheme`, `FluentMenuProvider`, `FluentDialogHeader`/
  `Footer`, `IToastService`, `IMessageService`, bare `FluentAnchor`, `FluentProgress` or `SelectedOptions` remain outside
  comments. The only v4 names left in markup are the five deliberately scoped Round-B sets — which independently confirms
  that inventory is complete.
- **Not verifiable statically (recorded):** the "exclusion" of the three failing Admin tests is the parent's explicit
  carve-out (Q33), not a filter in source — they are genuinely counted as failures; and `FluentTextField`/`FluentNumberField`
  do not appear in the 5.0.1 XML docs yet the build passes with them in markup, which is consistent with Razor rendering an
  unknown component name as an inert HTML element (the RZ10012 mechanism) rather than a hard error.

### Parent work while the legs run

- Fixed the `FieldDisplay.razor` header-comment nit: the `@* … *@` usage example taught
  `<FluentBadge Appearance="Appearance.Accent">`, which is v4 API inside a file agents read before editing card content.
  Now `<FluentBadge Appearance="BadgeAppearance.Filled" Color="BadgeColor.Success">Active</FluentBadge>` — the repo's own
  v5 idiom (and the Q24 pairing of appearance + colour). Comments are not compiled, so this is guidance hygiene, not a fix.
- The inert-attribute sweep (`b335bd6a`) is being salvaged: it spent ~57 turns and 250k tokens with its shell mangling
  every scripted attempt (`bash(&1)` fragments, `GetString` overload errors, `$props[$name]`), so it was steered to stop
  scripting and report only what it had already established. Its subject is **exploratory, not a gate** — the reviewer's
  Step 4 already established that the five Round-B families are the only remaining v4 names in markup, so a partial answer
  is acceptable and cannot block acceptance.

### The inert-attribute sweep (`b335bd6a`, complete) — a class this round had NOT covered

The scout produced a real table once it was told to stop scripting: **124 confirmed inert-attribute occurrences across 14
components**, plus 9 tag names whose parameter sets it could not establish (those are the five Round-B families — already
known). Its method was sound: real `[Parameter]` properties read from the 5.0.1 assembly's metadata, resolved through the
full inheritance chain (including generic bases such as `FluentTextArea : FluentInputImmediateBase<string>`) and the
external `InputBase<TValue>` properties, matched case-insensitively — its `coded=304 tag=0 row=76` note was it proving
`FluentTextArea` legitimately exposes `Immediate`, i.e. excluding a false positive.

**Why this matters:** the round's inventory covered removed *components* (which at least emit an `RZ10012` warning) and
renamed *enums* (which are compile errors). It never covered removed *parameters*, which on a component with
`AdditionalAttributes` are captured as meaningless HTML attributes and render **silently — with no warning and no error**.

**Parent classification against the 4.14.2 XML (the fact that separates our regressions from long-standing bugs):**

*Port regressions — the parameter existed in v4.14.2 and is gone in 5.0.1:*
| Attribute | Sites | Note |
|---|---|---|
| `FluentTextArea.Rows` | ~16 | v5 has no `Rows`; sizing must come from `Resize`/`AutoResize` or CSS |
| `FluentMenu.Anchor` + `UseMenuService` | 1 (`Admin.Shared/Components/RowActionsMenu.razor:64`) | the repo's row-actions kebab — used across every table |
| `FluentBadge.DismissTitle` + `OnDismissClick` | 1 (`Admin.Shared/Components/Chip.razor:32`) | v5 **removed** badge dismissal entirely — the chip's dismiss affordance is dead and has no direct replacement |
| `FluentCard.AreaRestricted` | 5 | |
| `FluentDivider.Role` | 2 | |
| `FluentInputFile.OnFileCountExceeded` | 1 | a guard callback that can never fire |

*Pre-existing — already inert in v4.14.2, so NOT caused by this port:* `FluentStack.Spacing` (15) and `.Gap` (5),
`FluentIcon<T>.Icon` (11), `FluentButton.AriaLabel` (8), `FluentCheckbox.HelpText` (2), `FluentDataGrid.AriaLabel` (2) and
`.Resizable` (1), `FluentRadio`'s `Group`/`Checked`/`CheckedChanged` (3), `FluentAutocomplete.AutoComplete` (2),
`FluentLink.Disabled` (1, = Q34). ~66 occurrences of long-standing, silently-inert markup.

Raised as **Q37** (scope: absorb the port-caused parameter regressions, or hand them to Round B).

### Legs D2p + D2q complete — both build gates green, and the full test gauntlet run

**D2p (Q33a + the review's blocker) — complete, parent-verified.**
- `Landing/LandingPage.razor`'s single create control is now `<FluentButton Appearance="ButtonAppearance.Primary" OnClick="HandleCreateClicked">`,
  with the handler, label and the `OnCreate`-else-`CreateRoute` contract byte-identical (`FluentButton.OnClick` /
  `.Appearance` confirmed present in the 5.0.1 XML). `FluentAnchorButton` could never have carried this action — it has
  no `OnClick` — so the choice was between losing the accent look and losing the click; `FluentButton` keeps both.
- The review's vacuous pin is genuinely de-vacuumed: `CreateEnabled_False_Hides_NewButton` (both Landing test files) now
  asserts the **action bar still renders** and then that the *same* `fluent-button` + label predicate the positive siblings
  use matches nothing — so an empty match can no longer be confused with a failed render.
- **Parent-measured:** `dotnet build SchoolCollab.slnx` 0 errors (279 warnings); Admin **695 total / 0 failed / 695 passed.**

**D2q (MAUI head, Q35a) — complete, parent-verified.**
- The head now builds: `dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj -f net10.0-windows10.0.19041.0 -m:1 -p:BuildInParallel=false`
  → **0 errors / 0 warnings**, `Build succeeded`.
- Residual v4 markup in the head: `Appearance="Appearance.…"` **0**, `FluentProgressRing` **0**; the only `FluentSearch`
  matches are inside the explanatory comment that documents the v5 merge (`FluentSearch` → `FluentTextInput` +
  `TextInputType.Search`), not live markup. Nothing was touched outside `src/Mobile/SchoolCollab.Mobile/`.
- Beyond the Appearances: `FluentSearch`→`FluentTextInput` (with `Immediate`), 12 + 4 button appearances, spinners,
  badges re-paired with `BadgeColor`, and two code-behind wrappers retyped `Appearance`→`BadgeAppearance`. `Routes.razor`
  was checked and needed no change — `<FluentProviders>` is the same correct v5 root cascade as Admin/Families.

**FULL TEST GAUNTLET (parent-run, `dotnet test` at the repo root) — 4095 total / 2 failed / 4090 passed.**
Both failures are **opt-in live-AI tests, unrelated to this port**: `OpenRouterClient_UsesApiKey_AuthenticatesSuccessfully`
and `OpenRouterProvider_WithGoogleGemma4_RespondsToSimplePrompt` (`Settings.Tests.Integration/ChatProviderLiveTests`),
which make real OpenRouter calls and require `SCHOOLCOLLAB_RUN_LIVE_AI_TESTS=1`; their sibling cases self-report
`Assert.Inconclusive` for the same reason. Every FluentUI-relevant suite passes: Admin, Assignments (unit + integration +
API), Settings (unit + API), Students (unit + integration + API), Families, Core, Auth, Architecture, MigrationService,
Mobile.Tests.Unit. (The two Playwright projects report "Zero tests ran" — they are not part of this round's gate.)

### Grill round Q37 — SETTLED (a): Round A absorbs the six port-caused parameter classes

Owner confirmed. Absorbed: `FluentTextArea.Rows` (~16), `FluentMenu.Anchor`+`UseMenuService` (RowActionsMenu),
`FluentBadge.DismissTitle`+`OnDismissClick` (Chip), `FluentCard.AreaRestricted` (5), `FluentDivider.Role` (2),
`FluentInputFile.OnFileCountExceeded` (1). Reasoning recorded: same class as Q30 — silent, user-visible, and caused by
*this* round's upgrade bump, so it is the round's to fix; for `FluentTextArea.Rows` and the badge dismiss the
instruction is explicitly "fix, or record with evidence that v5 has no equivalent" rather than a rushed substitute.

**Deferred to the backlog (NOT this round's work — already inert in v4.14.2, so long-standing rather than regressions).**
These are recorded here so Round B/the backlog owns them with the parent's XML evidence:
`FluentStack.Spacing` (15) and `.Gap` (5) — v4's `FluentStack` had neither, the real v5 names are
`HorizontalGap`/`VerticalGap`; `FluentIcon<T>.Icon` (11) — the v5/v4 parameter is `Value`; `FluentButton.AriaLabel` (8) —
no such parameter in either version, use the native `aria-label`; `FluentCheckbox.HelpText` (2) — v5 spells it
`Message`/`LabelInfo`; `FluentDataGrid.AriaLabel` (2) and `.Resizable` (1) — the latter is `ResizableColumns` in v5;
`FluentRadio`'s `Group` (wrap in `FluentRadioGroup`), `Checked`/`CheckedChanged` (`Value`/`ValueChanged`); and
`FluentAutocomplete.AutoComplete` (2) plus `FluentLink.Disabled` (1, = Q34). ~66 occurrences total.

### Grill round Q43/Q44/Q45 — SETTLED ("all as recommended")

**Q43 (a)** — `DialogShell` declares **the one** `EditForm`; dialogs stop declaring it (`Model`/`OnValidSubmit` become shell
parameters). **Q44 (a)** — the five shared `*FormFields` components (`StudentFormFields`, `PeriodFormFields`,
`GradeLevelFormFields`, `GuardianFormFields`, `TopicFormFields`) become **form-free**, rendering fields against the ambient
`EditContext` (`FluentField.CurrentEditContext` already supports this), which forces their page consumers to declare their
own form (~10 call sites: `PeriodUpsert`, grade-level `Create`/`Edit`, the topic and guardian call sites).
**Q45 (a)** — form-ownership consolidation is **kept out of the Round B merge leg** and staged as its own leg with a real
validation test per surface (invalid submit blocks; valid submit reaches the API).

**The fact that produced Q43**: 5.0.1 ships **no `FluentEditForm`** (nor `FluentValidator`/`FluentForm` — 0 declared
types). `FluentField` is the only form-adjacent type and its coupling is the single parameter `CurrentEditContext`.
`FluentDialog` and `FluentDialogBody` expose **no** form-related parameter at all, so the dialog can neither supply, wrap
nor inherit a form — an `EditForm` (ASP.NET's, not FluentUI's) must exist somewhere in the tree, and FluentUI only consumes
its cascading `EditContext` at field level.
**And the "extra EditForm" did not exist**: of the 47 files declaring `<EditForm>`, 5 are `*FormFields`, 33 are dialogs and
**0 are pages** — one form per editable surface already. The defect was **ownership inconsistency**, not duplication. That
is why Q43+Q44 must land together: a shell that took the form while those five components kept theirs would render
**nested forms** in the seven dialogs that consume them.

### Round B handoff (all deferred work, with its trigger)

1. **The five warning-only rename families** (Round B's original scope — each renders as an inert element with an
   `RZ10012` warning): `FluentTextField` ~18, `FluentNumberField` ~32, `FluentNavMenu`/`FluentNavLink`/`FluentNavGroup` ~30,
   `FluentProgressRing` ~63, `FluentBreadcrumb`/`FluentBreadcrumbItem` 6 (this one has **no** v5 replacement — authoring, not
   a rename). Plus the v5 DOM/`::deep` selector audit.
2. **The dialog merge (Q38–Q42, mechanical)**: introduce `<DialogShell>` owning the **body wrapper + `dialog-container` +
   validator + `DialogShellFooter`**, taking the form as its child. 42 dialogs, six shapes → ~3 codemod buckets; keep
   `ReadOnlyDialogShell` separate; fuse into the same pass as item 1 so each dialog is touched **once**; extend
   `DialogChromeArchitectureTests` and the `dialog-ui` skill in the same PR. Must **not** own the `EditForm` (see item 3).
3. **Form-ownership consolidation (Q43–Q45, behavioural)**: `DialogShell` declares the one `EditForm`; the five
   `*FormFields` become form-free; their page consumers declare their own. Needs a validation test per surface. Staged
   separately because a mistake here fails *silently* (a form that no longer validates) — the one failure mode a green
   suite cannot see.
4. **Pre-existing inert-attribute backlog (~66 occurrences, NOT caused by this port)** — already inert in v4.14.2, with the
   parent's XML evidence in the Q37 entry above (`FluentStack.Spacing`/`Gap`, `FluentIcon<T>.Icon`, `FluentButton.AriaLabel`,
   `FluentCheckbox.HelpText`, `FluentDataGrid.AriaLabel`/`Resizable`, `FluentRadio`'s `Group`/`Checked`/`CheckedChanged`,
   `FluentAutocomplete.AutoComplete`).
5. **Pre-existing defect**: `AiChat.razor`'s inert `Disabled` on a `FluentLink` (Q34) — v4 was identical, so it is not a
   regression; fix by guarding the handler or converting the control.
6. **The deferred UI-tester pass (Q36)** carries Round A's visible deltas: titled confirm dialogs (~25 call sites), the
   v5-native tab indicator, the 14 `LinkAppearance.Subtle` anchor restylings, the MAUI head, and the `<fluent-field>`
   width-wrapper change.

## Acceptance

**Round A delivered the v4.14.2 → v5.0.1 port of the main solution and the MAUI head, with every gate measured by the
parent (not taken from a leg's self-report):**

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** (warnings ~279–305 depending on incremental reuse; all pre-existing NU/obsolete or the Round-B rename families) |
| MAUI head — **not** in that solution — `dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj -f net10.0-windows10.0.19041.0 -m:1 -p:BuildInParallel=false` | **0 errors / 0 warnings**, Build succeeded |
| `dotnet test` (whole repo) | **4098 total / 2 failed / 4092 passed** |
| The 2 failures | opt-in **live-AI** tests (`Settings.Tests.Integration/ChatProviderLiveTests`, need `SCHOOLCOLLAB_RUN_LIVE_AI_TESTS=1`) — unrelated to this port. (One earlier run reported 3; the re-run reported 2 — a flake, verified by re-running.) |
| Per-suite | Admin **697/0**, Assignments **1167/0** (unit; integration + API green), Settings **579/0**, Students **624/0**, Families **49/0**, Architecture **114/0**, Core/Auth/MigrationService/Mobile.Tests.Unit green |
| Independent static review | complete — one **blocker** (two vacuously-passing `CreateEnabled_False_Hides_NewButton` assertions) found, fixed, and re-verified; all other verdicts OK |
| Frozen artifact | `documents/rounds/diffs-fluentui-v5-rounda.patch` — 279 files, 1.4 MB, frozen 10:31Z |
| Working tree | **nothing staged, nothing committed** (per the round's standing hold) |

**Scope absorbed during the round, each by an explicit owner decision:** the `FluentAnchor` family (Q30 — it alone blocked
the zero-failure gate), the MAUI head (Q35 — this round's package bump broke it and no gate could see it), and the six
port-caused inert-**parameter** classes (Q37).

**Explicitly NOT satisfied by this round, and deliberately deferred to items 1–6 above:** the five rename families, the
dialog merge, form-ownership consolidation, the ~66 pre-existing inert attributes, and the UI-tester pass. Round A's gate
must therefore be read as **"0 errors over `SchoolCollab.slnx` *and* the MAUI head, 0 failures over the whole repo except
two opt-in live-AI tests"** — not as "the repo is v5-clean".
