# Round — authoring-ux7-residuals

Provider: pi/ollama-cloud · **Tier 2 (Light — worker + static diff-only reviewer)** · UI-only behavioural round
Models: parent (plan owner) · worker `ollama-cloud/deepseek-v4.1-flash` · reviewer `ollama-cloud/kimi-k2.7-code`
Round base: `66c2676a84f7727932fa7b57c5775727fa3c4f49` (main, clean tree at round start)
Branch: `stack/5-authoring-ux7-residuals`

## Plan

### Goal

Close **UX-7** — the last unimplemented contract in `documents/specs/assignment-authoring-compartments.md`
("Leaving the page with unsaved changes raises a confirmation", spec `:106`) — and the five UI P2s deferred
when round `assignment-targeting-r2` closed: **F7, F12, F13, F15, F17**. Also give the deferred-residual
list a durable home, because `documents/rounds/` is declared ephemeral by `AGENTS.md` and
`documents/rounds/README.md`.

### Settled decisions (grill-me rounds 1–2, 2026-10-02 — owner-accepted)

- **D1 — scope** = UX-7 + F7 + F12 + F13 + F15 + F17. Nothing else.
- **D2 — UX-7 covers BOTH mechanisms**, because they cover disjoint cases:
  - **in-app navigation** → `NavigationManager.RegisterLocationChangingHandler` (cancels the navigation and
    shows a FluentUI confirmation dialog);
  - **browser unload** (tab close / reload / external URL) → `window.beforeunload` via a collocated JS
    module, which can only show the browser's own prompt (platform-forced, not a free choice).
- **D3 — "dirty" is a BASELINE COMPARISON**, not an event flag: a value changed and then changed back to its
  loaded value is **not** dirty. (An event flag is cheaper but raises false prompts, which is how users learn
  to click through the guard.)
- **D4 — F17** is fixed by implementing `OnParametersSetAsync` in the component to reload when `Id` or `Mode`
  changes.
- **D5 — F7** is fixed by extending the **shared** `RowAction` with an optional `DisabledReason` and rendering
  it in `RowActionsMenu`. The change is **additive**: `DisabledReason` is `init`-only with a null default, and
  no existing factory signature changes.
- **D6 — F13** is fixed by fire-and-forget after `_loading = false` (the debounce already owns cancellation via
  `_previewCts`, so an unawaited schedule has a well-defined lifetime).
- **D7** a **"Deferred / known gaps"** section is added to the durable spec.

### Why F17 is the highest-severity item here (not a cosmetic P2)

`AssignmentAuthoring.razor` declares 3 `[Parameter]`s and has **no `OnParametersSet`**, while
`Pages/Assignments/Edit.razor:13` renders `<AssignmentAuthoring Mode="Edit" Id="Id" />`. Navigating
`/assignments/{a}/edit` → `/assignments/{b}/edit` **reuses the same component instance** (same type, same
render-tree position), so Blazor only updates `Id` and the form keeps assignment **A's** loaded state under
B's URL — a stale-write path. This is the same defect class as R2's F1.

### Expected files (bound the diff — keep it reviewable)

| File | Change |
|---|---|
| `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentAuthoring.razor` | UX-7 guard wiring + dirty baseline; F12 wording; F13 schedule placement; F15 reason relocation; F17 `OnParametersSetAsync` |
| `.../Components/Pages/Assignments/AssignmentAuthoring.razor.js` | **new** — collocated JS module exposing `registerBeforeUnload(handler)` / `unregisterBeforeUnload()` |
| `.../Components/Pages/Assignments/AssignmentEditFormModel.cs` | dirty-baseline support (snapshot + comparison), if it does not fit cleanly in the component |
| `src/SchoolCollab.Admin.Shared/Components/RowAction.cs` | `DisabledReason` property + factory pass-through (additive) |
| `src/SchoolCollab.Admin.Shared/Components/RowActionsMenu.razor` | render the disabled reason (title/aria) on the single-action button and the kebab item |
| `documents/specs/assignment-authoring-compartments.md` | **"Deferred / known gaps"** section (D7) |
| `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentAuthoringBunitTests.cs` (+ a model test file if needed) | the discriminating tests below |

**Explicitly out of scope:** no EF migration, no endpoint/contract change, no new feature flag, no
`Directory.Packages.props` edit, no per-assignment policy override, no R3/AI work. If the worker finds itself
needing any of these, **stop and report** (mid-round escalation rule).

### Acceptance criteria (each must be a DISCRIMINATING test — it must fail against `66c2676a`)

1. **UX-7 / in-app (#1)** — with an unsaved change present, an in-app navigation attempt is intercepted and
   does **not** proceed; with no change, it proceeds.
2. **UX-7 / baseline (#1, D3)** — change a value and change it back to its loaded value ⇒ **not dirty** (no
   interception). This is the test that proves D3 rather than an event flag.
3. **UX-7 / unload (#2, D2)** — the collocated module is invoked to register the unload guard **only while
   dirty**, and the guard is unregistered on dispose. (The browser prompt itself is not bUnit-assertable;
   assert the dirty-flag contract and the interop call, and say so in the test's comment.)
4. **F17 (#1)** — render, then change the `Id` parameter on the same instance: the form **reloads** for the new
   assignment (assert the newly-loaded title/state appears and the old one is gone). Fails against `66c2676a`,
   where the instance keeps the old data.
5. **F7 (#1)** — a disabled `RowAction` carrying a `DisabledReason` renders that reason as its accessible
   description/title (use `UseMenuService="false"` so the item is inline-assertable); a disabled action with
   **no** reason renders no description (no invented text); and an enabled action is unaffected.
6. **F12 (#2)** — with **no** targets, the preview area does **not** render `Students matched: 0`; it renders an
   actionable hint instead. With targets, the counts still render unchanged.
7. **F13 (#2)** — the initial load does not await the 500 ms debounce before `_loading` clears (assert the form
   renders with the fake `TimeProvider` never advanced, and the preview settles after it is advanced).
8. **F15 (#2)** — the constraint reason paragraph is reachable from the picker group it explains (assert its
   position relative to the picker rows, and that each picker's `aria-describedby` resolves to a paragraph that
   actually renders in that state — do not regress R2-10's derived-IDREF fix).
9. **D7 (#2)** — the spec carries a "Deferred / known gaps" section listing the still-open residuals (at
   minimum the P2-c transaction-rollback proof) with enough context to be actionable.

### Reviewer acceptance criteria (what the static diff review must adjudicate)

- The UX-7 guard is **fail-safe**: it must not block every navigation (a guard that always prompts is as broken
  as none), and it must not fire on a non-dirty form.
- `RegisterLocationChangingHandler`'s disposable is **disposed** with the component; the JS module is disposed
  too, and `JSDisconnectedException` during teardown is handled (the repo has been bitten by this — see
  `blazor-jsinterop-module`).
- F17's reload cannot fire an **infinite loop** (`OnParametersSetAsync` runs on every parameter set; the reload
  must be conditional on a *changed* `Id`/`Mode`, not on any set).
- F17 + UX-7 **interact correctly**: a dirty form must be guarded when the `Id` changes, not silently reloaded
  over unsaved work.
- The `RowAction` change is genuinely **additive** (no existing call site or factory signature changed) and the
  22 callers are unaffected.
- Multi-select stays `SelectedOptions`/`SelectedOptionsChanged` (never `@bind-SelectedValues`); no inline
  `<style>`; CSS isolation respected.

## Worker Report

**Run `514e4b14` (`deepseek-v4.1-flash`) — work completed, delivery failed.** The async runner process crashed
(`0xC0000409` / exit `3221226505`, STATUS_STACK_BUFFER_OVERRUN) *after* writing its report, so the harness's
stale-run reconciliation marked the run **failed** even though the artifact was complete. The parent recovered
it from the run's `output-0.log` rather than re-dispatching — a retry would have redone finished work and
raced the existing diff. The recovered tree compiled and every suite was green, which also proves the crash
landed *after* the worker's revert-then-restore pre-fix probe (**not** mid-restore, which would have left the
tree broken).

Deliverable: **7 modified + 2 new** files; patch 1,320 lines · 9 diffs · 0 `deleted file mode`.

| File | Change |
|---|---|
| `AssignmentAuthoring.razor` | UX-7 guard + dirty baseline; F12 hint; F13 schedule placement; F15 reason relocation; F17 `OnParametersSetAsync` |
| `AssignmentAuthoring.razor.js` | **new** — collocated `beforeunload` module |
| `AssignmentEditFormModel.cs` | dirty-baseline support |
| `RowAction.cs` + `RowActionsMenu.razor` | additive `DisabledReason` + rendering |
| `assignment-authoring-compartments.md` | "Deferred / known gaps" section (D7) |
| `RowActionsMenuTests.cs`, `AssignmentAuthoringSpecGapsTests.cs`, `AssignmentAuthoringBunitTests.cs` | 16 new tests |

**Pre-fix probe (the discriminating-test evidence):** with the production change reverted, Assignments
**812/9 failed**, Admin **623/1 failed**, Architecture **75/2 failed** — behavioural assertion failures, with the
round's new public constants kept so the test file still compiled (i.e. genuine red tests, not compile errors
masquerading as failures). Restored afterwards. The worker also verified the collocated module resolves as a
static web asset at `_content/SchoolCollab.Assignments.Application/.../AssignmentAuthoring.razor.js`.

Parent-verified on the recovered tree: build **0 errors** · Assignments **812/0** · Admin **623/0** ·
Architecture **75/0** · Students **623/0**.

**Disclosed deviations** (test-only placement, outside the plan's expected-files table): the F7 test lives in
`tests/SchoolCollab.Admin.Tests.Unit/RowActionsMenuTests.cs` (the source-named test file for the shared
component), and a D7 document-guard test in
`tests/SchoolCollab.ArchitectureTests.Unit/AssignmentAuthoringSpecGapsTests.cs` — a project that already owns
document/config invariants and is in the gate list.

**Residuals disclosed by the worker:** `IsDirty` serializes the save payload per render
(`OnAfterRenderAsync`); a host that changes `Id` without a `NavigationManager` navigation reloads unprompted
(unreachable via the repo's route parameter — documented at the F17 guard); `IDisposable` → `IAsyncDisposable`
on the component; and a **pinned known gap** that FluentUI 4.14.2's `FluentAutocomplete` drops a declared
`aria-describedby` on its inner text field (recorded with a change-detector assertion).

## Review

**Diff review `fe56be66` (`kimi-k2.7-code`) — Verdict: P1 (BLOCK).** Recovered from the run's
`output-0.log`: the runner process died **after** the reviewer had written its verdict, so reconciliation
marked the run failed on delivery. The verdict is complete and is transcribed here.

### P1 — fixed in the rework
**Stale `beforeunload` listener can trap a user on a read-only page** (`AssignmentAuthoring.razor:1168-1172`).
`SyncBeforeUnloadGuardAsync` opens with `if (_disposed || IsReadOnly) return;` — returning **before** the
`dirty == _beforeUnloadRegistered` check and the `unregisterBeforeUnload` call. If the form was dirty (listener
attached) and a lifecycle action (Publish / Unpublish / Close / Archive) flips `EffectiveMode` to View, the
listener is never removed and the browser's unload prompt fires on a page with **no unsaved work** — the
fail-unsafe state the acceptance criteria forbid. **Parent-verified against source** (the early return is real),
not relayed. Required change, supplied by the review: drop the `IsReadOnly` early return so the state machine
unregisters on the dirty→clean transition, keeping the module import lazy.

### P2 (report-only, carried forward)
- **UX-7/F17 defense-in-depth** (`:1221-1229`): `OnParametersSetAsync` does not itself refuse a reload while
  `IsDirty`. The registered location-changing handler runs before a navigation commits, so the guard does win
  on the repo's routes — a dirty gate inside `LoadAsync` would be belt-and-braces.
- **`IsDirty` cost per render** (`:1117-1118`, `:1123-1128`): `OnAfterRenderAsync` → `SyncBeforeUnloadGuardAsync`
  → `IsDirty` → `CaptureSaveSnapshot` → `JsonSerializer.Serialize` of the full update request, every render.
  Real but acceptable this round; candidate for incremental dirty tracking.
- **Pinned FluentUI a11y gap is real** (`:249-257`, test `:1817-1819`): `FluentAutocomplete` does not forward
  `aria-describedby` to its inner text field, so the students picker exposes no accessible reason when
  disabled. Pinning + documenting is right for this round; the hole itself needs a future fix (wrap the
  control or upgrade FluentUI).
- **Vacuity note:** four tests pass against `66c2676a` by design — three are intentional fail-safe /
  non-regression anchors (`UnchangedForm_InAppNavigation_ProceedsWithoutAConfirmation`,
  `SameParametersReset_DoesNotReloadTheAssignment` (its own comment acknowledges it),
  `WithTargets_PreviewStillRendersTheServerResolvedCounts`), and one guards the new property
  (`ActionWithoutReason_KeepsItsLabelAndInventsNoDescription`). None is a mislabelled discriminating test.

### Adjudicated — the disclosed deviations are honest scope
`tests/SchoolCollab.Admin.Tests.Unit/RowActionsMenuTests.cs` is the source-named test file for
`SchoolCollab.Admin.Shared`, and the D7 invariant belongs in `tests/SchoolCollab.ArchitectureTests.Unit/`,
which already owns document/config invariants. The review's words: "honest scope, not drift".

### Confirmed correct
Baseline dirty comparison (D3); collocated JS module with lazy import; `JSDisconnectedException` handling and
`IAsyncDisposable` disposal; additive `RowAction` factory signatures; and **F17's gate compares
`_loadedId`/`_loadedMode`, so it cannot infinite-loop** (parent-verified at `:1223`).

## Acceptance

**Round CLOSED — accepted.** Final parent-authoritative verification on the frozen tree:

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| Assignments.Tests.Unit | **813 / 0** |
| Admin.Tests.Unit | 623 / 0 |
| ArchitectureTests.Unit | 75 / 0 |
| Students.Tests.Unit | 623 / 0 |
| Students.Api.Tests.Unit | 25 / 0 |
| Assignments.Api.Tests.Unit | 97 / 0 |
| Patch `diffs-authoring-ux7-residuals.patch` | 1,407 lines · 9 diffs · 0 `deleted file mode` · 2 new |
| Index / branch | clean / `stack/5-authoring-ux7-residuals` (base `66c2676a`) |

**Delivered (D1):** UX-7 unsaved-changes guard in **both** halves — in-app `RegisterLocationChangingHandler` with
cancel/confirm, plus the collocated `beforeunload` module — on D3's baseline dirty comparison (changed-then-
reverted is not dirty); **F17** `OnParametersSetAsync` reload gated on a *changed* `Id`/`Mode` (closing the
stale-instance write path); **F7** additive `RowAction.DisabledReason` + rendering; **F12** no-targets preview
hint; **F13** unawaited initial preview schedule; **F15** reason-paragraph relocation; **D7** the spec's
"Deferred / known gaps" section. 17 new tests, with the discriminating set proven to fail pre-fix (9 Assignments
/ 1 Admin / 2 Architecture at the first pass; the P1 rework's test failed pre-fix with a specific assertion).

**Gate history:** worker run `514e4b14` **completed its work but its runner process crashed on delivery**
(`0xC0000409`) — recovered from the run log, deliberately **not** re-dispatched (a retry would have redone
finished work and raced the existing diff); the recovered tree compiling green also proved the crash landed
*after* the worker's revert-then-restore probe. Diff review `fe56be66` returned **P1 (BLOCK)** and its runner
*also* crashed post-verdict — verdict recovered from the run log. That P1 was **parent-verified against source**
before any rework was spent on it. Bounded rework `3239e1d0` fixed it with a discriminating test; bounded
re-check `729a440a` → **PASS, no issues found**.

**Recorded, non-blocking residuals (carried forward, not dropped):**
- **FluentUI `FluentAutocomplete` drops a declared `aria-describedby`** on its inner text field, so the students
  picker exposes no accessible reason when disabled. Pinned with a change-detector test and recorded in the
  spec's gaps section; the underlying hole needs a future fix (wrap the control or upgrade the package).
- **`IsDirty` serializes the full save payload on every render** (`OnAfterRenderAsync` →
  `SyncBeforeUnloadGuardAsync` → `CaptureSaveSnapshot`). Real cost, acceptable this round; candidate for
  incremental dirty tracking.
- **UX-7/F17 defense-in-depth:** `OnParametersSetAsync` does not itself refuse a reload while dirty; the
  location-changing handler runs first, so the guard wins on the repo's routes.
- **P2-c transaction-rollback proof** (from round `assignment-targeting-r2`) remains open in the gaps section.

**Merge: NOT authorized.** No branch push, no PR, no merge without an explicit owner instruction.
