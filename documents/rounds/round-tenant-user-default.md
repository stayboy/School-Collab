# Round `tenant-user-default`

Provider: pi (models: `ollama-cloud/deepseek-v4.1-flash` worker, `ollama-cloud/kimi-k2.7-code` reviewer) — **Tier 2 (light)**
**Status:** **CLOSED** — Tier 2; updates PR #351 (owner's commit instruction, 2026-10-15).
**Branch:** `feat/default-tenant-dropdown-action`
**Round base:** `6aa16981` — this round is a follow-up on the same branch and updates PR #351 (Q7: no independent PR).
**Dirty at round start (NOT part of this round — another session's in-flight media/assignments WIP):** 15 modified + 1 untracked (`.github/copilot/rules/testing.md`, `documents/runbooks/dev-loop-hot-reload.md`, `documents/rounds/round-media-pill-listing.md`, `documents/specs/recording-materials.md`, six `src/Assignments/**` files, `FluentIcons.cs`, `WardMaterialPlayer.razor`, three Assignments/Families test files, untracked `MaterialPreviewWiringArchitectureTests.cs`).
**Isolation:** the round patch is frozen **by path** (the foreign delta sits under an ACTIVE writer, so a snapshot base would attribute that writer's later edits to this round).

## Pinned decisions (owner, 2026-10-15 grill round)

- **Q1 → Light Tier 2.**
- **Q2 → add alongside.** Add a `Set as my default` row action beside the platform-default action; do not replace it.
- **Q3 → disabled entry.** On the row that is already the operator's default, render that action **disabled with a reason** (mirroring the platform `Portal default` entry), not hidden.
- **Q4 → confirm.** Confirm via `ShowConfirmDialogAsync`, reusing the switcher's exact wording.
- **Q5 → hard reload; pick untouched.** After the write, `Nav.Refresh(forceReload: true)`; the explicit session pick is not touched.
- **Q6 → no rename.** The banner keeps `Your default tenant: <name>`.
- **Q7 → same branch.** Commit to `feat/default-tenant-dropdown-action` and update PR #351.

## Plan

### Goal

On `/tenants`, make the operator's own default-tenant preference **settable from the page**, and
rename the column that surfaces it so the two defaults read unambiguously:

1. Column `Your default` → **`User default`**; its badge text `Yours` → **`Yes`**.
2. A grid **row action** `Set as my default` records the signed-in operator's preference for that
   row's tenant (`PUT /api/tenants/me/default`), confirm-gated, then hard-reloads.
3. The row that is already the operator's default shows that action **disabled with a reason**.

Everything else on the page is unchanged: the preference banner (`Your default tenant: <name>` +
`Clear`), the dev session-pin line (`Follow the portal default`), the platform-default row action
(`Make portal default` / disabled `Portal default`), and the empty-platform-default note.

### Scope — expected files (bounded; anything else is scope creep)

1. `src/Settings/SchoolCollab.Settings.Application/Components/Pages/Tenants/Index.razor`
2. `tests/SchoolCollab.Settings.Tests.Unit/Tenants/TenantsIndexTests.cs`
3. `documents/specs/tenant-default-dropdown-action.md`
4. `documents/solution/admin-default-tenant.md`

**Explicitly out of scope:** `Index.razor.css` (no new styling — the shared `RowActionsMenu`
renders the kebab), `DevTenantSwitcher`, `AdminDefaultTenantResolver`, `TenantsApiClient`,
`IDevTenantSelection`, the Settings API, the banner wording, and every file outside the four above.

### Behaviour contracts

- **C1 — the column.** `Title="User default"` (was `Your default`); the matching row's
  `FluentBadge` text is `Yes` (was `Yours`), `Appearance="Appearance.Lightweight"` unchanged; other
  rows keep the muted `—`. `Tooltip="true"` must stay (FluentUI gates `HeaderTooltip` on it) and the
  `HeaderTooltip` text is updated to the `User default` vocabulary and to name the new row action.
- **C2 — the row actions.** `RowActions(tenant)` returns exactly two entries, platform first:
  - platform: `tenant.IsDefault` → disabled `Portal default` (reason unchanged); else
    `Make portal default` → `MakeDefaultAsync(tenant)` (unchanged);
  - user: `_preference == tenant.Id` → disabled `Set as my default` with
    `disabledReason: "This tenant is already your default."`; else `Set as my default` →
    `SetMyDefaultAsync(tenant)`.
  Both entries use an existing `FluentIcons` constant (platform keeps `Star`; user uses `Person`).
  Every row therefore carries 2 actions → the shared menu renders the kebab for all rows.
- **C3 — the write.** `SetMyDefaultAsync(TenantDto tenant)`:
  1. confirm via `DialogService.ShowConfirmDialogAsync($"Open the admin portal on '{tenant.Name}' by default from now on?", primaryText: "Set as default", title: "Set default tenant")`
     (the switcher's exact wording); cancel/dismiss writes nothing and does not reload;
  2. `await Api.SetMyDefaultTenantAsync(tenant.Id)`; set `_preference = tenant.Id`;
  3. `Nav.Refresh(forceReload: true)` — the Admin host re-resolves the preference on the next load;
     this RCL cannot call `AdminDefaultTenantResolver` (it lives in the host).
  A failure logs and sets `_error` (surfaces on the `LandingPage` error strip) and does **not**
  reload. The explicit session pick (`IDevTenantSelection`) is never written here.
- **C4 — injections.** `Index.razor` gains `@inject IDialogService DialogService`. No other
  injection changes.
- **C5 — naming.** The existing platform method stays `MakeDefaultAsync`; the new one is
  `SetMyDefaultAsync`. The banner, the pin line, `LoadPreferenceAsync`, `LoadPickAsync`,
  `FollowPortalDefaultAsync`, `ClearPreferenceAsync`, `TenantDisplayName`, and the empty-state note
  are untouched apart from the column edits.

### Tests (`tests/SchoolCollab.Settings.Tests.Unit/Tenants/TenantsIndexTests.cs`)

Harness: the page now injects `IDialogService`, so the test registers a mocked one. Reuse the
pattern in `tests/SchoolCollab.Admin.Tests.Unit/DevTenantSwitcherTests.cs` (`RegisterConfirmDialog`):
a `Mock<IDialogService>` whose `ShowDialogAsync<ConfirmDialog, ConfirmDialogContent>` returns a
`Mock<IDialogReference>` with a fixed `Result`, capturing the `ConfirmDialogContent` so the wording
can be pinned. Register it **after** `AddFluentUIComponents()`.

Update (the rename):
- `Grid_NamesBothDefaultColumns` — assert `User default` (was `Your default`); keep the
  `NotContain("Portal default")` check on the `th` texts.
- `Grid_BadgesTheOperatorsOwnDefault` — badge text `Yes`.
- `Grid_ShowsNoYoursBadge_WhenNoPreferenceIsRecorded` → rename to
  `Grid_ShowsNoYesBadge_WhenNoPreferenceIsRecorded`, asserting no badge reads `Yes`.
- `Grid_HeaderTooltips_ExplainTheTwoDefaults` — keep it green against the updated tooltip text
  (assert a stable substring of each header's tooltip).

Add:
- `RowActions_OfferSetAsMyDefault_ForANonPreferredTenant` — the non-preferred row offers an enabled
  `Set as my default`; the platform action is still present.
- `RowActions_DisableSetAsMyDefault_ForThePreferredRow` — `_preference == tenant.Id` → the entry is
  disabled and names its reason.
- `SetAsMyDefault_Confirmed_WritesThePreference_AndReloads` — confirm → exactly one
  `PUT /api/tenants/me/default` whose body carries the tenant id, and exactly one forced reload.
- `SetAsMyDefault_Cancelled_WritesNothing_AndDoesNotReload`.
- `FailedSetAsMyDefault_SurfacesTheError` — the PUT fails → the error strip is set and no reload.
- `SetAsMyDefault_DoesNotTouchTheSessionPick` — a pick in force is still in the selection slot
  after the write.

Assert row actions either through `LandingPage<TenantDto>.RowActions` if it exposes the delegate,
or by driving the kebab and reading the `FluentMenuItem` labels/disabled state — whichever the
component actually supports; do not invent a seam.

Keep unmodified: the banner tests, the pin-line tests, the platform-default badge/note tests, and
every existing reload assertion.

### Acceptance criteria

1. `dotnet build SchoolCollab.slnx` — 0 errors (run via a gitignored `--artifacts-path` because the
   running dev stack locks `src/**/bin`; CI re-proves the default-path build).
2. `dotnet test tests/SchoolCollab.Settings.Tests.Unit` — 0 failures.
3. `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` — 0 failures.
4. The `/tenants` grid column header is `User default` and its badge reads `Yes`; no column header
   reads `Your default`; the banner still reads `Your default tenant:`.
5. The row action set is exactly platform + user, and the already-preferred row's user action is
   disabled with a reason.
6. Spec (§FR-7 / §4.2 / §5) and `admin-default-tenant.md` (§3.4) describe the set-from-the-page
   action and the rename.

### Open decisions

None — the owner's grill round settled every fork (pinned above).

## Worker Report

WORKER REPORT

Changed files (exactly 4):
1. `src/Settings/SchoolCollab.Settings.Application/Components/Pages/Tenants/Index.razor`
2. `tests/SchoolCollab.Settings.Tests.Unit/Tenants/TenantsIndexTests.cs`
3. `documents/specs/tenant-default-dropdown-action.md`
4. `documents/solution/admin-default-tenant.md`

The foreign Assignments/Families/media WIP was never touched.

- Column `Your default` → `User default`; badge `Yours` → `Yes`; `Tooltip="true"` kept; `HeaderTooltip` reworded to the new vocabulary and names the row action. Banner, pin line, `LoadPreferenceAsync`/`LoadPickAsync`/`FollowPortalDefaultAsync`/`ClearPreferenceAsync`/`TenantDisplayName`/`MakeDefaultAsync` and the empty-state note untouched.
- `RowActions(tenant)` returns exactly two entries (platform first, user second); the already-preferred row's user entry is disabled with `"This tenant is already your default."` (`FluentIcons.Person`).
- New `SetMyDefaultAsync(TenantDto)`: switcher-exact confirm → `PUT /api/tenants/me/default` → `_preference = tenant.Id` → `Nav.Refresh(forceReload: true)`; cancel writes/reloads nothing; failure logs, sets `_error`, `StateHasChanged()`, no reload; `IDevTenantSelection` never written. `@inject IDialogService DialogService` added.
- Self-reported validation: solution build 0 errors; Settings unit 579/579; Architecture 112/112 (both via the gitignored `artifacts/` root, dev-stack lock).

Deviations (recorded):
1. `StateHasChanged()` added in `SetMyDefaultAsync`'s catch — **parent-approved mid-run** after the worker escalated that the plan's literal catch body cannot produce the plan's stated "surfaces on the error strip" (row actions run from the menu's own event handler; the page re-renders only from its own). `MakeDefaultAsync`'s catch left untouched.
2. Test harness renders a `<FluentMenuProvider />` root (test-only); with two actions per row every row renders the kebab, which needs the provider the layout supplies.
3. Harness renames `clearStatus` → `preferenceWriteStatus`, `ClearCalls` → `PreferenceWrites` (the endpoint now serves both clear and set); 4 call sites updated.
4. Docs: `admin-default-tenant.md` §5's "the header dropdown only ever sets" clause corrected, and the spec §7 2026-10-14 "per-row default rejected" bullet records the 2026-10-15 re-adoption — both made false by this change.
5. `Index.razor`'s header comment and the `_preference` field doc updated for accuracy.

Open risk (self-reported, out of scope): the pre-existing latent gap in `MakeDefaultAsync`'s catch (a failed platform-default write also never re-renders the page).

## Review

REVIEW

Verdict: PASS
P1: none
P2: two (below)
Best-practices: PASS — no overwrites / repo skills honored / readable

Model: `ollama-cloud/kimi-k2.7-code`, static diff-only — never built, tested or wrote anything.

**Correct, with evidence:** C1 (`Index.razor:100-104`, banner `:88`); C2 (`:251-283`, `FluentIcons.Person` exists at `FluentIcons.cs:18`); C3 (`:296-322`, switcher-exact wording, cancel no-op, failure no-reload); C4 (`:7`, one new injection); C5 (the untouched surfaces really are untouched). **Test discrimination:** all six new tests would fail against the pre-fix single-action `RowActions`, and the renamed tests pin the new `User default` / `Yes` wording.

**P2 findings:**
1. `TenantsIndexTests.cs:214` — `Render<FluentMenuProvider>()` is a separate bUnit root, not an ancestor; if it ever fails, prefer `RowActionsMenu.razor:23`'s `UseMenuService="false"` or wrap the page inside the provider.
2. `.github/copilot/rules/testing.md` requires a live UI pass for `.razor` changes before acceptance.

## Acceptance

**Verdict: CLOSED** — Tier 2, parent-transcribed on the reviewer's PASS plus the parent's authoritative pass.

| Criterion | Result |
|---|---|
| 1. `dotnet build SchoolCollab.slnx` — 0 errors | ✅ **0 errors** (via the gitignored `artifacts/tenant-user-default` root — the running dev stack locks `src/**/bin`; CI re-proves the canonical default-path build) |
| 2. `SchoolCollab.Settings.Tests.Unit` — 0 failures | ✅ **579 passed / 0 failed** (baseline 573 → +6 = the six new tests) |
| 3. `SchoolCollab.ArchitectureTests.Unit` — 0 failures | ✅ **112 passed / 0 failed** |
| 4. Column `User default`, badge `Yes`, no `Your default` column header, banner still `Your default tenant:` | ✅ |
| 5. Row action set is exactly platform + user; the already-preferred row's user action disabled with a reason | ✅ |
| 6. Spec (§FR-7/§4.2/§5/§7) + `admin-default-tenant.md` §3.4/§5 describe the change | ✅ |

- **Rework iterations: 0.** Reviewer PASS on the first pass; no P1, no P2 requiring rework.
- **Deviation 1** (`StateHasChanged()` in the catch) was parent-approved mid-run; it is the one place the plan's literal contract could not produce the plan's stated behaviour. The other four deviations are test-only or doc-accuracy and are recorded above.
- **P2.1 adjudicated accepted, no rework:** the row-action tests assert through the public `LandingPage<T>.RowActions` delegate (correct seam — `FluentMenu` does not materialise items while closed), and the authoritative run is green with those tests discriminating, so the separate provider root is sufficient.
- **Accepted residual (owner-eye, Q1=B): the live UI pass on `/tenants` was not run.** The running Admin is a stale build (the round compiled to a gitignored `artifacts/` root; nothing was hot-reloaded), so a live pass needs an Admin rebuild/restart — the owner's stack. Blocks merge until observed. The rule cited by the reviewer is present only in the uncommitted working tree (the media-arc WIP), not in committed `HEAD`; it is treated as the repo norm.
- **Committed on the owner's instruction; updates PR #351.**
