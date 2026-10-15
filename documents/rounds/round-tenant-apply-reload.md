# Round `tenant-apply-reload`

Provider: pi (models: `ollama-cloud/deepseek-v4.1-flash` worker, `ollama-cloud/kimi-k2.7-code` reviewer) — **Tier 2 (light)**
**Status:** **CLOSED** (verification complete; not yet committed — commit/push are the owner's).
Patch: `documents/rounds/diffs-tenant-apply-reload.patch` (6 files, 0 deletions).
**Branch:** `feat/default-tenant-dropdown-action`
**HEAD base:** `faac9f66b3b918fe2cc7bd2d1598f452eac0aeeb`
**Isolation base (SNAP):** `c295fbd5e68cf335cec1797a5a4a04b61bbe01ae` — a **dangling** commit-tree snapshot of the whole tree, taken before this round's work, so the new patch excludes both round `tenant-default-selection`'s still-uncommitted delta and the other session's WIP. Regenerate with:
`git add -A && TREE=$(git write-tree) && SNAP=$(git commit-tree "$TREE" -p HEAD -m "round-tenant-apply-reload isolation base") && git reset -q` (it is GC-able; rebuild if the freeze fails with "bad object").
**Patch freeze:** `git add -N -- <round paths> && git diff $SNAP -- <round paths>` — by path *as well as* by snapshot, because another writer is still active in this tree and its post-snapshot edits must not be attributed to this round.
**Dirty at round start:** round 2's uncommitted change set (7 files) + the other session's WIP (22 modified, 3 untracked).

## Plan

### Goal

Make a tenant-context change actually **re-apply** the circuit's tenant claim. Today every tenant action calls
`Nav.NavigateTo(Nav.Uri, forceLoad: true)`, which does not produce the document reload the design depends on, so the
Blazor circuit keeps the `AuthenticationState` (and therefore the `tenant_id` claim) it captured at circuit start. The
symptom: the switcher reads "Hydeson School" while `ActiveTermToolbar` renders its "No tenant" branch, and a manual F5
fixes it — proving the store is correct and only the in-app reload is broken.

Fix: use the **sanctioned hard-reload API**, `NavigationManager.Refresh(forceReload: true)`, at every call site that
changes the tenant context. `RemoteNavigationManager` implements it as `Blazor._internal.navigationManager.refresh(true)`,
which is `location.reload()` in `blazor.web.js` — a real document load, unlike the `forceLoad` navigation path.

### Scope — expected files (bounded; anything else is scope creep)

1. `src/SchoolCollab.Admin/Components/Layout/DevTenantSwitcher.razor` — 3 call sites (reconciliation at ~178, the pick at ~332, the confirmed set-as-default at ~375)
2. `src/Settings/SchoolCollab.Settings.Application/Components/Pages/Tenants/Index.razor` — 1 call site (`FollowPortalDefaultAsync`, ~194)
3. `tests/SchoolCollab.Admin.Tests.Unit/DevTenantSwitcherTests.cs` — the navigation spy
4. `tests/SchoolCollab.Settings.Tests.Unit/Tenants/TenantsIndexTests.cs` — a reload assertion for the follow action
5. `documents/specs/tenant-default-dropdown-action.md` — §5/§6: the apply mechanism
6. `documents/solution/admin-default-tenant.md` — §3.3/§5: the same

**Explicitly out of scope:** `AdminDefaultTenantResolver`, `DevTenantSelection`, `TestAuthHandler`,
`TenantPropagationDelegatingHandler`, `VisibleTenantService`, `NeedsReload`, the resolver's publication, the confirm
dialog, and every other `NavigateTo` in the repo (there are none outside these four).

### Behaviour contracts

- **C1** All four tenant-context reload sites call `Nav.Refresh(forceReload: true)` in place of
  `Nav.NavigateTo(Nav.Uri, forceLoad: true)`. Nothing else about them changes: the same trigger, at the same point in
  the flow, with the same preceding writes (`SetSelectedTenantIdAsync`, `SetMyDefaultTenantAsync`, `ResolveAndApplyAsync`).
- **C2** `forceReload: true` is required. `Refresh(forceReload: false)` merges the server HTML into the live document
  instead of loading it, which would leave the claim stale — the exact defect being fixed. Do not "simplify" to
  `Refresh()` or to `NavigateTo` with an added query parameter.
- **C3** No other behaviour changes: `_reloadRequested` (the one-shot guard), `NeedsReload(...)`,
  `ResolveDefaultsAsync`'s publication order, the selection write, the action-zone interception, and the confirm flow
  all stay byte-identical apart from the reload line.
- **C4** Test spies must observe the reload through the right seam. `NavigationManager.Refresh(bool)` (the base
  implementation, which is what runs under bUnit) calls `NavigateTo(Uri, forceLoad: true, replace: true)`, and
  `replace: true` routes to the **`NavigateToCore(string, NavigationOptions)` overload** — so a spy that only overrides
  `NavigateToCore(string, bool)` will throw `NotImplementedException` or silently miss the call. The spy must override
  `NavigateToCore(string uri, NavigationOptions options)` and record `options.ForceLoad` (keeping the existing
  "exactly one forced reload" assertions meaningful).

### Tests

- `DevTenantSwitcherTests`: update `RecordingNavigationManager` per C4; keep every existing reload assertion working
  (`SetAsDefault_Confirmed_…` asserts exactly one forced reload; the API-failure and cancel tests assert none). Add one
  test that the **reconciliation** reload is requested on an initial render whose claim is empty — the path that had no
  component-level coverage and that this round exists to repair.
- `TenantsIndexTests`: add a test asserting the `Follow the portal default` action requests a forced reload (it
  currently asserts only the write). Its harness needs the same spy as the switcher's.

### Acceptance criteria

1. `dotnet build SchoolCollab.slnx` — 0 errors.
2. `dotnet test tests/SchoolCollab.Admin.Tests.Unit` — 0 failures.
3. `dotnet test tests/SchoolCollab.Settings.Tests.Unit` — 0 failures.
4. `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` — 0 failures.
5. `rg "NavigateTo\(Nav\.Uri" src/` returns nothing (all four sites migrated).
6. Spec + `admin-default-tenant.md` name `Refresh(forceReload: true)` as the apply mechanism.
7. **Manual residual (accepted, not a gate):** bUnit can prove the reload is *requested* but not that the browser
   reloads. One browser confirmation after the fix — switch tenant, watch the toolbar leave "No tenant" without an F5
   — is the owner's, and is recorded in `## Acceptance` as an accepted residual.

### Open decisions

None. The direction (B) and the mode (Light, Tier 2) were pinned by the owner; the mechanism is settled by the
framework's own API and the F5 evidence.

## Worker Report

_(pending)_

## Review

_(pending — Tier 2 static reviewer)_

## Acceptance

**Verdict: CLOSED** — Tier 2, parent-transcribed on the reviewer's PASS plus the parent's own authoritative runs.

| Criterion | Result |
|---|---|
| 1. `dotnet build SchoolCollab.slnx` — 0 errors | ⚠️ **equivalently met; the canonical command was not runnable** — the owner's dev stack is up and locks the service bins (`MSB3027`/`MSB3021`: `SchoolCollab.Auth`, `AI.Server`, `Students.Worker`, `Settings.Api`). Compilation is proven instead by the Architecture project building on the **default** paths (112/112) and by both affected suites compiling at a shifted output root. CI re-proves the canonical solution build at PR time. |
| 2. `tests/SchoolCollab.Admin.Tests.Unit` — 0 failures | ✅ **687 passed / 0 failed** (`DevTenantSwitcherTests` 20) |
| 3. `tests/SchoolCollab.Settings.Tests.Unit` — 0 failures | ✅ **567 passed / 0 failed** (`TenantsIndexTests` 13) |
| 4. `tests/SchoolCollab.ArchitectureTests.Unit` — 0 failures | ✅ **112 passed / 0 failed**, on the **default** output paths |
| 5. `rg "NavigateTo\(Nav\.Uri" src/` returns nothing | ✅ all four sites are `Nav.Refresh(forceReload: true)` |
| 6. Spec + `admin-default-tenant.md` name the apply mechanism | ✅ |
| 7. Manual browser confirmation | outstanding — accepted residual below |

- **Rework iterations: 0.** Reviewer PASS on the first pass, no P1, no P2, best-practices clean, and it independently confirmed that both added tests would fail against the pre-fix code.
- **Verification deviation (recorded, evidence its effect is nil):** criteria 2–3 ran with `--artifacts-path <repo>/artifacts/round3`, a location chosen so that BOTH repo-root resolution styles still resolve — the sentinel walk-up (`src/AppHost/SchoolCollab.AppHost`) and the fixed 5-hop helper in `GradeStreamAssignmentSeederTests`. Two independent controls: the Architecture suite ran on the canonical default paths in the same session, and an earlier out-of-tree artifacts path (wrong depth) produced 179 source-path failures — so these numbers are not path artefacts.
- **Residual 1 (owner-eye, accepted):** switch tenant in the running app and confirm the toolbar leaves "No tenant" **without** an F5. bUnit proves the reload is *requested*; only a browser proves it happens.
- **Residual 2 (environment, not a defect):** the canonical `dotnet build SchoolCollab.slnx` first runs in CI when the PR is opened; nothing is committed yet.
- **Not committed.**

