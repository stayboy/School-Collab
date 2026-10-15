# Round `tenant-default-selection`

Provider: pi (models: `ollama-cloud/deepseek-v4.1-flash` worker, `ollama-cloud/kimi-k2.7-code` reviewer) — **Tier 2 (light)**
**Status:** **CLOSED** (verification complete; not yet committed — the commit/push gates are the owner's). Patch: `documents/rounds/diffs-tenant-default-selection.patch` (7 files, 0 deletions).
**Branch:** `feat/default-tenant-dropdown-action`
**Round base:** `faac9f66b3b918fe2cc7bd2d1598f452eac0aeeb`
**Dirty at round start (NOT part of this round — another session's in-flight WIP):** 11 modified
(`documents/specs/recording-materials.md`, `AssignmentAuthoring.razor`, `InstructionEditorList.razor`,
`MaterialEditDialog.razor(.css)`, `MaterialEditDialogModels.cs`, `FluentIcons.cs`,
`WardMaterialPlayer.razor`, `InstructionEditorListBunitTests.cs`, `MaterialEditDialogBunitTests.cs`,
`WardAssignmentPlayerBunitTests.cs`) + 2 untracked (`push-out.txt`,
`tests/SchoolCollab.ArchitectureTests.Unit/MaterialPreviewWiringArchitectureTests.cs`).
**Isolation:** the round patch is frozen **by path** (`git diff HEAD -- <round paths>`) rather than
against a tree snapshot — the foreign delta sits under an ACTIVE writer, so a snapshot base would
attribute that writer's later edits to this round. Consequence: the architecture suite run during
verification includes the foreign untracked test above; recorded, not attributed.

## Pinned decisions (owner)

- **Q3 → (B), owner-accepted.** The ability to clear an explicit switcher pick is **restored** on the
  `/tenants` page as a dev-only session-pin line offering `Follow the portal default`
  (`SetSelectedTenantIdAsync(null)`). Verified fact behind the fork: `DevTenantSwitcher.razor:296` is
  the only production call site of `SetSelectedTenantIdAsync`, so removing the pseudo-entry removes
  the capability unless it is re-homed. The pin line renders independently of the preference banner,
  so the action stays reachable when a pick exists with **no** preference recorded.

## Plan

### Goal

In the dev tenant switcher's dropdown, **remove the `(default: X)` pseudo-entry and instead select
the resolved default tenant's own row**, so the control's selected value *is* the tenant the portal
is operating under. The half that *applies* the resolved default (publication to `dev:tenant-default`
plus the one-shot reconciliation reload) is untouched — this round changes what the dropdown
*shows and selects*, never whether the default is applied.

### Scope — expected files (bounded; anything else is scope creep)

1. `src/SchoolCollab.Admin/Components/Layout/DevTenantSwitcher.razor`
2. `tests/SchoolCollab.Admin.Tests.Unit/DevTenantSwitcherTests.cs`
3. `src/Settings/SchoolCollab.Settings.Application/Components/Pages/Tenants/Index.razor` (+ `Index.razor.css`
   if the pin line needs a rule)
4. `tests/SchoolCollab.Settings.Tests.Unit/Tenants/TenantsIndexTests.cs`
5. `documents/specs/tenant-default-dropdown-action.md` (FR-2, FR-3, FR-7, §4 table, §5 tests, acceptance)
6. `documents/solution/admin-default-tenant.md` (§3.3, §3.4, §4, §5 wording)

**Explicitly out of scope:** `AdminDefaultTenantResolver`, `DevTenantSelection`, `TestAuthHandler`,
`TenantPropagationDelegatingHandler`, `VisibleTenantService`, the `/tenants` banner, the
`IsSeparator`/`IsAction` sentinels, the confirm-dialog flow, and the `(system / no tenant)` row
(kept — `admin-default-tenant.md` §2.4 records it as owner-settled and load-bearing for
shared-blueprint coded-value editing).

### Behaviour contracts

- **B1** `BuildOptions()` produces exactly `(system / no tenant)` (value `system`) followed by one
  entry per tenant (value = the tenant id). The `DefaultOptionValue` const (`""`), the `(default: X)`
  label and every use of the const are deleted; no rendered label contains `(default`.
- **B2** Selected-row resolution from `_explicitSelection`:
  - `Guid.Empty` → the `(system / no tenant)` row;
  - a real Guid **present in `_tenants`** → that tenant's row;
  - `null`, **or a Guid not present in `_tenants`** → the row of the *resolved* default, i.e.
    `_effective` when it is a real tenant present in `_tenants`, otherwise the `(system / no tenant)`
    row. The result is never `null` (a null `SelectedOption` would leave the select blank).
- **B3** B2 is **display-only**: it never writes the selection slot. After a no-pick render,
  `IDevTenantSelection.GetSelectedTenantIdAsync()` still returns `null`. Only a user pick writes it.
- **B4** `OnSelectionChanged` is unchanged apart from the deleted `DefaultOptionValue` arm; a tenant
  row sets an explicit pick + forced reload, the system row sets `Guid.Empty`. The action-zone
  interception (`IsSeparator`/`IsAction` → `RestoreTenantSelection()` + the confirm flow) and the
  `@key` epoch are untouched.
- **B5** The action-zone rule is unchanged: visible iff the *selected* row is a real tenant whose id
  ≠ `_preference`. **Intended consequence:** on a no-pick load with a resolved real default and no
  preference the zone is now **visible** (it was hidden behind the pseudo-entry) — that is what makes
  `Set as default` reachable without re-picking the tenant first.
- **B6** The reconciliation is untouched: `ResolveDefaultsAsync` still resolves and publishes via
  `AdminDefaultTenantResolver.ResolveAndApplyAsync`, and `OnAfterRenderAsync(firstRender)` still
  forces exactly one reload via `NeedsReload`. Nothing may remove, gate or re-order it.
- **B7** `/tenants` reads the explicit **pick** (`IDevTenantSelection.GetSelectedTenantIdAsync`) in the
  same parallel load as the preference, and renders a **dev session-pin line** when a pick exists:
  `Pinned this session: <name>` — name resolved exactly like the preference (registry name;
  `system / no tenant` for `Guid.Empty`; raw id when not in the registry) — plus a
  `Follow the portal default` action that calls `SetSelectedTenantIdAsync(null)` and then
  `Nav.NavigateTo(Nav.Uri, forceLoad: true)` so the change takes effect in this circuit. A failed
  write surfaces on the existing `_error` strip and the line stays. The line renders **independently
  of the preference banner** (a pick may exist with no preference). A failed pick read is non-fatal
  (log, treat as no pick).
- **B8** The existing preference banner's text, render condition and `Clear` behaviour are unchanged;
  the 7 existing `TenantsIndexTests` must keep passing unmodified.

### Tests (`tests/SchoolCollab.Admin.Tests.Unit/DevTenantSwitcherTests.cs`)

Replace (they assert the removed entry):
- `RendersSystemDefaultAndEveryTenant` → `RendersSystemAndEveryTenant_WithNoDefaultPseudoEntry`:
  labels are exactly `["(system / no tenant)", "Alpha", "Beta"]`, and no label contains `(default`.
- `WithNoExplicitPick_SelectsTheResolvedDefaultEntry` →
  `WithNoExplicitPick_SelectsTheResolvedDefaultTenantsRow`: `SelectedOption.Value == Alpha`
  (the fixture's `isDefault: true`) **and** the stub's selection is still `null` (B3).
- `WithAPreference_TheDefaultEntryNamesThePreference` →
  `WithAPreference_SelectsThePreferredTenantsRow`: `preference: Beta`, `selected: null` →
  `SelectedOption.Value == Beta`.
- `ActionZone_IsHidden_WhenTheDefaultEntryIsSelected` → deleted (the state no longer exists).

Add:
- `ActionZone_IsShown_OnANoPickLoad_WhenTheResolvedDefaultDiffersFromThePreference`
  (`selected: null`, `preference: null`, platform default present) (B5).
- `WithNoDefaultDesignated_SelectsTheSystemRow` (no `isDefault` tenant, no preference) (B2).
- `WithAResolvedDefaultNotInTheRegistry_SelectsTheSystemRow` (`preference` = unknown Guid, no
  `isDefault` tenant) (B2).

Keep unchanged: `WithAnExplicitSystemPick_SelectsTheSystemEntry`,
`WithAnExplicitTenantPick_SelectsThatTenant`, `ActionZone_IsShown_ForARealTenant_WithNoRecordedPreference`,
`ActionZone_IsHidden_WhenTheSelectedTenantIsAlreadyThePreference`,
`ActionZone_IsShown_WhenThePreferenceIsTheSystemContext`,
`ActionZone_SeparatorIsDisabled_AndTheActionEntryIsNot`, `ClickingTheSeparator_DoesNothing`,
`SetAsDefault_Confirmed_WritesThePreference_PublishesIt_AndReloads`,
`SetAsDefault_Cancelled_WritesNothing_AndKeepsTheSelection`, `SetAsDefault_ApiFailure_KeepsTheSelection_AndDoesNotReload`,
`SetAsDefault_DoesNotShowASuccessToast`, `TheConfirmPrompt_NamesTheTenant_WithTheSanctionedWording`.

### Tests (`tests/SchoolCollab.Settings.Tests.Unit/Tenants/TenantsIndexTests.cs`) — B7/B8

The harness must register a stub `IDevTenantSelection` (the page now injects it;
`AuthTenancyExtensions` registers a null-store fallback, but the test asserts on the stub).

Add:
- `Pin_IsHidden_WhenNoExplicitPick`.
- `Pin_NamesThePinnedTenant_AndOffersFollowThePortalDefault`.
- `Pin_IsShown_EvenWithNoPreferenceRecorded` — the reachability case that motivated (B).
- `FollowThePortalDefault_ClearsThePick_AndReloads` — asserts the clear call **and** that the
  preference was not touched (it is `SetSelectedTenantIdAsync`, not `SetMyDefaultTenantAsync`).
- `FailedFollowThePortalDefault_SurfacesTheError_AndKeepsThePin`.

Keep unmodified: the existing 7 `Banner_*` / `Clear_*` / `Failed*` tests (B8).

### Acceptance criteria

1. `dotnet build SchoolCollab.slnx` — 0 errors.
2. `dotnet test tests/SchoolCollab.Admin.Tests.Unit` — 0 failures.
3. `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` — 0 failures.
4. `dotnet test tests/SchoolCollab.Settings.Tests.Unit` — 0 failures.
4. `rg '\(default' src/SchoolCollab.Admin/Components/Layout/DevTenantSwitcher.razor tests/SchoolCollab.Admin.Tests.Unit/DevTenantSwitcherTests.cs`
   returns nothing.
5. B3 holds as a test assertion (no selection-slot write from a display-only render), and B7's
   follow action writes the **selection** slot, never the preference endpoint.
6. The spec (`documents/specs/tenant-default-dropdown-action.md`) and
   `documents/solution/admin-default-tenant.md` describe the new behaviour; the spec's §4 state
   table no longer lists a `(default …)` row and does list the `/tenants` pin line.

### Open decisions

None outstanding — Q3 was settled by the owner as **(B)** and is pinned above. The plan is
implementable standalone.

## Worker Report

WORKER REPORT
Changed files: src/SchoolCollab.Admin/Components/Layout/DevTenantSwitcher.razor; tests/SchoolCollab.Admin.Tests.Unit/DevTenantSwitcherTests.cs; src/Settings/SchoolCollab.Settings.Application/Components/Pages/Tenants/Index.razor; src/Settings/SchoolCollab.Settings.Application/Components/Pages/Tenants/Index.razor.css; tests/SchoolCollab.Settings.Tests.Unit/Tenants/TenantsIndexTests.cs; documents/specs/tenant-default-dropdown-action.md; documents/solution/admin-default-tenant.md
Build: not clean — 2 errors, both the same environmental lock (`MSB3027`/`MSB3021`: `src/SchoolCollab.Admin/bin/Debug/net10.0/SchoolCollab.Settings.Application.dll` held by the running `SchoolCollab.Admin.exe` PID 38772); no compiler errors in that pass (`CS8506` raised by the first build was fixed in the same file). Test projects never compiled.
Tests: not run — MSB3027/MSB3021 lock; the supervisor denied stopping the process or retrying, so the parent owns the pass.
Deviations from plan:
- `RendersSystemAndEveryTenant_WithNoDefaultPseudoEntry` renders with `preference: Alpha` (plan left the args unstated): under B2+B5 a no-pick load selects Alpha's row, which shows the action zone, so "exactly three labels" only holds while the zone is hidden; the test's subject (no pseudo-entry) is unchanged.
- The "no label contains `(default`" assertion is written as `NotContain(label => label.Contains("default"))` — stricter, and the only way both that assertion and acceptance criterion 4's `rg '\(default'` → nothing can hold for the same file.
- The `system` switch arm became `(Guid?)Guid.Empty` and the arm's orphaned "clearing back to the default" comment was reworded; deleting the `(Guid?)null` arm alone left CS8506 (no best type for the switch expression).

### Parent verification — build lock, and why the artifacts path did not work

The Admin host is a child of the running Aspire AppHost (**PID 8280 `SchoolCollab.AppHost.exe`**, started 06:52 today; the whole dev topology — Auth, Settings/Students/Assignments APIs, AI.Server, Families, both workers — is up). It holds `src/SchoolCollab.Admin/bin/Debug/net10.0/SchoolCollab.Settings.Application.dll`, so MSBuild cannot re-copy the RCL dll after the `/tenants` edit. The owner's live stack was **not** killed; the lock was surfaced to them.

Non-invasive attempt: the parent ran the authoritative pass with `--artifacts-path <temp>` (build: **0 errors**, no lock — the compile gate is satisfied). The **test** run from that path is **not valid evidence**, and the deviation is recorded: this repo's suites contain source/file-inspection tests that resolve the repository root from `AppContext.BaseDirectory`, which the relocated output directory breaks. Measured with the artifacts path:

| Suite | Result | Verdict |
|---|---|---|
| `SchoolCollab.Admin.Tests.Unit` | 686 total / **174 failed** | 174/174 environmental: `Expected File … to exist … because source should exist at 'C:\\Users\\skwar\\AppData\\Local\\src\\…'` |
| `SchoolCollab.Settings.Tests.Unit` | 566 total / **5 failed** | 5/5 environmental (`must run from inside the repository`, `DashboardCard.razor.css` resolved under `AppData\\Local`) |
| `SchoolCollab.ArchitectureTests.Unit` | 112 total / **104 failed** | 104/104 environmental (repo-source scanners) |
| `DevTenantSwitcherTests` (filtered) | **19 / 19 passed** | this round's surface is green |
| `TenantsIndexTests` (filtered) | **12 / 12 passed** | this round's surface is green |

The **authoritative default-path pass is still outstanding** and is blocked on the file lock; it is the only thing that can close this round's acceptance criteria 1–4.


## Review

REVIEW
Verdict: PASS
P1: none
P2: none
Best-practices: no overwrites / skills honored / readable

Model: `ollama-cloud/kimi-k2.7-code`, static diff-only — never built, tested or wrote anything. Reviewed the frozen patch against `## Plan` B1–B8 plus the pinned Q3=(B) decision, and the worker's three declared deviations.

**Parent adjudication of the declared deviations — all three accepted**, none a defect:

1. `RendersSystemAndEveryTenant_WithNoDefaultPseudoEntry` renders with `preference: Alpha`. Correct: with a preference equal to the resolved default, B5's condition hides the action zone, which is the only state in which "exactly three labels" is a meaningful claim. With `preference: null` the zone is (intentionally) visible.
2. The absent-pseudo-entry assertion is `NotContain(label => label.Contains("default"))` — strictly stronger than the plan's `(default` text and the only form that can hold together with acceptance criterion 4.
3. The `system` switch arm became `(Guid?)Guid.Empty`: deleting the `(Guid?)null` arm alone leaves CS8506 (no best type for the switch expression). The residual comment was reworded accordingly.

**Parent cross-checks on the frozen tree** (independent of the reviewer): criterion 4 `rg '\(default'` over the component and its tests → **no match**; `BuildOptions()` renders exactly the system row plus one row per tenant, with the action zone appended only per B5; `OnSelectionChanged`'s switch has no pseudo-entry arm and keeps the meaningful `Guid.Empty` system arm (B4).

## Acceptance

**Verdict: CLOSED** — Tier 2, parent-transcribed on the reviewer's PASS plus the parent's authoritative default-path pass.

| Criterion | Result |
|---|---|
| 1. `dotnet build SchoolCollab.slnx` — 0 errors | ✅ **0 errors** (warnings only, all pre-existing) |
| 2. `dotnet test tests/SchoolCollab.Admin.Tests.Unit` | ✅ **686 passed / 0 failed** (round-1 baseline 684 → +2 = the two net-new switcher tests) |
| 3. `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` | ✅ **112 passed / 0 failed** |
| 4. `dotnet test tests/SchoolCollab.Settings.Tests.Unit` | ✅ **566 passed / 0 failed** (baseline 561 → +5 = the five new pin-line tests) |
| 5. `rg '\(default'` over the component + its tests | ✅ no match |
| 6. B3 holds as an assertion (a display-only render writes no selection slot) | ✅ `WithNoExplicitPick_SelectsTheResolvedDefaultTenantsRow` |
| 7. Spec + `admin-default-tenant.md` describe the new behaviour | ✅ |

- **Rework iterations: 0.** Reviewer PASS on the first pass; no P1, no P2.
- **File lock:** the authoritative pass required stopping the owner's Aspire dev stack (the `SchoolCollab.AppHost.exe` chain, 22 processes) — authorized as Q5=(B). No source file and no git state was touched by the stop.
- **Rejected, and why it mattered:** the non-invasive `--artifacts-path` run compiles clean but is NOT admissible evidence for this repo; 174/5/104 failures were all source-path resolution (`AppContext.BaseDirectory` → relocated repo root), with this round's own tests green at 19/19 and 12/12. Recorded so the next round does not spend a cycle rediscovering it.
- **Accepted residual (owner-eye; not one of the criteria):** the *visual* result — the header switcher with no pseudo-entry, and the `/tenants` session-pin line's layout — was never observed in a browser, because the stack had to be stopped for the pass and this tier dispatches no UI tester. Behaviour is covered by bUnit; styling was checked only as class↔markup resolution. Worth one glance after you restart the AppHost.
- **Not committed.** The change set (7 files + this round doc + the patch) awaits the owner's commit instruction.

