Provider: pi — Light round (Tier 2): worker `ollama-cloud/deepseek-v4.1-flash`, static diff reviewer `ollama-cloud/kimi-k2.7-code`
Mode: attended (owner pinned Q64–Q70 on 2026-10-16; delivery = stack layer on the Round B layer)
Tier: 2 (light) — one worker + one static diff-only review; no plan-review agent. The UI-tester leg rides on this round's close as the owner's Q69 decision, not as a Tier-3 promotion.
Base: layer on the Round B branch `s2-fluentui-v5-roundb` (tip `ed16684d`); layer branch `s3-fluentui-v5-fix`
Tree at round start: dirty — `documents/solution/fluentui-v5-residual-backlog.md` (18-line §G obsolete-enum addendum, uncommitted). It rides with this round; `git diff` based on `ed16684d` isolates the round delta.
Status: CLOSED — Round C complete: the blank-app regression is fixed in all three apps, verified by the discriminating shell test and a live browser probe. Gates: solution build 0 errors, MAUI head 0/0, `dotnet test` Admin 698/698 + ArchitectureTests 114/114, static review OK with no P1. Carried by the `s3-fluentui-v5-fix` layer on the FluentUI v5 stack. Residuals a–d backlogged (backlog §H–§K).

## Objective

Restore routing in all three Blazor apps and put the FluentUI v5 provider shell where the library's own
guidance puts it, after Round A introduced a defect that renders **every page of every app blank**.

## Root cause (established by the parent, 2026-10-16)

FluentUI Blazor **v5-only** `FluentProviders` (`src/Core/Infrastructure/FluentProviders.razor`) renders
exactly four providers in a `div` and has **no `ChildContent` parameter and no cascading value**:

```razor
<div id="@Id" class="@ClassValue" style="@StyleValue" @attributes="AdditionalAttributes">
    <FluentDialogProvider /><FluentToastProvider /><FluentTooltipProvider /><FluentKeyCodeProvider />
</div>
```

Round A wrapped `<Router>` inside it in all three apps' `Routes.razor`. The router's entire output is
therefore discarded: no layout, no nav, no page — on every route, in Admin, Families and Mobile — while
the build, the 4093-test suite and the Blazor circuit all report success.

Evidence:

| Source | Finding |
|---|---|
| Library source `Infrastructure/FluentProviders.razor` | no child slot, no cascading value |
| Library samples (`Samples/FluentUI.Samples.SSR`, `Demo` `Routes.razor`) | `<Router>` at **root**, unwrapped; providers in the layout |
| Installed v5 skill `~/.agents/skills/fluentui-blazor-usage` (`SKILL.md:34`, `SETUP.md:71`) | `<FluentProviders />` **self-closing**, a sibling above `<FluentLayout>`; pitfall #1: omit it and dialogs/tooltips "silently fail" |
| Live Playwright probe of `https://localhost:7300/students` | body 688 bytes = `.fluent-providers` + providers only; no `article.content`, `.brand`, `fluent-nav`; `window.Blazor` true; 0 console errors; 0 failed requests |
| Blazor circuit WebSocket frames | clean — `EndUpdateRootComponents`, `RenderBatch`, `RegisterKeyCode`, 0 error frames |

The `Routes.razor` comment claiming `FluentProviders` "supplies `LibraryConfiguration` to every FluentUI
component in the tree" is false — that comes from DI (`FluentProviders(LibraryConfiguration)` via
`AddFluentUIComponents()`).

**Why it hid:** the defect produces no error anywhere — a valid render that discards its own child — and
Round A's acceptance criterion AC-5 tested that `<FluentProviders>` was *present* at all three host roots,
not that it *wrapped nothing*. bUnit renders components in isolation and never mounts the app shell.

## Scope

1. `src/SchoolCollab.Admin/Components/Routes.razor` — delete the `<FluentProviders>` wrapper and the false comment; `<Router>` is the root element again.
2. `src/SchoolCollab.Families/Components/Routes.razor` — same.
3. `src/Mobile/SchoolCollab.Mobile/Components/Routes.razor` — same.
4. `src/SchoolCollab.Admin/Components/Layout/MainLayout.razor` — replace `<FluentToastProvider />`, `<FluentDialogProvider />`, `<FluentTooltipProvider />`, `<FluentKeyCodeProvider />` with one self-closing `<FluentProviders />`; **keep** `<FluentMessageBarProvider />` (not part of `FluentProviders`).
5. `src/SchoolCollab.Families/Components/Layout/MainLayout.razor` — same; keep `<FluentMessageBarProvider />`. (Adds toast/tooltip/key-code coverage that app did not have — intentional, per the skill's guidance.)
6. `src/Mobile/SchoolCollab.Mobile/Components/Layout/MainLayout.razor` — replace `<FluentDialogProvider />` with `<FluentProviders />`; keep any other provider.
7. `tests/SchoolCollab.Admin.Tests.Unit/` — **new** app-shell smoke test that renders the Admin `<Routes />` and asserts the layout shell actually renders.
8. `.github/copilot/rules/blazor-components.md` — record the placement rule and the "component tests do not prove the app boots" lesson.
9. `documents/solution/fluentui-v5-residual-backlog.md` — the pre-existing uncommitted §G addendum rides along unchanged.

Not in scope: any other v5 migration work, the obsolete-enum cleanup (§G), the dialog-shape leg (Q61), the docs leg (Q62).

## Plan (Tier 2 — parent-authored)

**Step 1 — unwrap the router (files 1–3).** Exactly this shape, all three files, keeping each app's own
`AppAssembly`/`AdditionalAssemblies`/`NotFoundPage` arguments and the `<FocusOnNavigate>` line untouched:

```razor
<Router AppAssembly="…">
    <Found Context="routeData">
        <RouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)" />
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
</Router>
```

Delete the `@* v5 requires this cascade … *@` comment with the element (it is factually wrong). Do not
re-add `FluentProviders` around anything.

**Step 2 — provider placement (files 4–6).** In each MainLayout render `<FluentProviders />` self-closing
at the layout root, above `<FluentLayout>` (or above the app's layout markup), replacing only the
providers it supersedes (dialog, toast, tooltip, key-code). Keep `FluentMessageBarProvider`.

**Step 3 — regression test (file 7).** New file `tests/SchoolCollab.Admin.Tests.Unit/RoutesShellRenderTests.cs`
(the test project already has a `ProjectReference` to `src/SchoolCollab.Admin/SchoolCollab.Admin.csproj`, so
the host's `Routes` component and `Layout.MainLayout` are reachable). Render the host's `<Routes />` at a
route (e.g. `/`) with the services the app needs (`AddFluentUIComponents()`, plus whatever the layout's
child components require — reuse this suite's existing mock harness; `FluentServiceMocks.cs` and the
`Test*.razor` helpers are the precedent), then assert the **layout shell** rendered — e.g. `article.content`
(the element produced by `MainLayout`, which can only appear if the Router matched and `RouteView` rendered
it). Asserting layout presence, not page content, is what makes this discriminating for this defect class
without dragging a page's API surface into the test.

**Honest-test rule (hard):** the new assertion must be proven to fail against the pre-fix shape. Do it by
temporarily re-wrapping the Router in `Routes.razor`, running the test, observing the failure, and restoring
the file. Report the observed failure text. A test that passes both ways is a defect of this round.

**Bounded fallback:** if rendering `<Routes />` proves to need service stubs beyond the suite's existing
harness, spend no more than ~20 minutes on it, then fall back to a static guard in
`tests/SchoolCollab.ArchitectureTests.Unit` asserting that no `Routes.razor` in `src/` nests `<Router`
inside any element, and record the fallback plus its reason in the WORKER REPORT. Do not silently drop the
test.

**Step 4 — rule update (file 8).** In `.github/copilot/rules/blazor-components.md`, add a short entry:
`FluentProviders` renders no `ChildContent` — it is a **self-closing sibling** at the layout root, never a
wrapper; wrapping a `Router` in it (or in any v5-only component whose child contract is unverified)
silently blanks every page. State the general check: *a green component-test suite does not prove the app
boots — verify the app shell renders*, and note that acceptance criteria must test **placement**, not mere
presence.

**Step 5 — same-class sweep (read-only, report only).** Check the v5-only components Round A adopted for
any other case where repo markup passes children or parameters the component does not forward. Report
findings; do not fix them in this round.

**Build/verification the worker runs:** `dotnet build SchoolCollab.slnx` (0 errors) and
`dotnet test tests/SchoolCollab.Admin.Tests.Unit` (+ `tests/SchoolCollab.ArchitectureTests.Unit` if file 8's
rule is test-backed). Also build the MAUI head, because file 6 is shared:
`dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj -f net10.0-windows10.0.19041.0 -m:1 -p:BuildInParallel=false`.

## Acceptance criteria

- **AC-1** All three `Routes.razor` files render `<Router>` as the root element; no `<FluentProviders>` element with children remains anywhere in `src/`.
- **AC-2** Each of the three MainLayouts renders `<FluentProviders />` self-closing exactly once; `FluentMessageBarProvider` is retained where it existed.
- **AC-3** `dotnet build SchoolCollab.slnx` → 0 errors (parent-run).
- **AC-4** MAUI head build → 0 errors (parent-run).
- **AC-5** The new shell test passes **and is proven discriminating** (observed failure with the wrapper restored).
- **AC-6** Live probe: `https://localhost:7300/students` renders a real layout — body well over 2000 chars with `article.content`/`.brand`/`fluent-nav` present and non-empty `innerText` (parent-run, dev loop restarted on the fixed tree).
- **AC-7** `.github/copilot/rules/blazor-components.md` carries the placement rule and the "tests do not prove the app boots" lesson.
- **AC-8** No P1 from the static diff review.
- **AC-9** The six documented Round-A/B visible deltas are re-verified by the UI-tester leg (Q69) once the app renders.

## Open decisions

None — Q64–Q70 were pinned by the owner on 2026-10-16 (Q66 revised to self-closing placement after
consulting the installed v5 skill).

## Worker Report

Worker `26baaf40` (`ollama-cloud/deepseek-v4.1-flash`), one writer in the tree.

**Changed files (10 in the round patch):**

| Path | Change |
|---|---|
| `src/SchoolCollab.Admin/Components/Routes.razor` | `<FluentProviders>` wrapper deleted; `<Router>` is the root element; the false comment replaced with a correct one naming the trap. |
| `src/SchoolCollab.Families/Components/Routes.razor` | Same. |
| `src/Mobile/SchoolCollab.Mobile/Components/Routes.razor` | Same (BOM + `NotFoundPage` preserved). |
| `src/SchoolCollab.Admin/Components/Layout/MainLayout.razor` | toast/dialog/tooltip/key-code providers → one self-closing `<FluentProviders />`; `<FluentMessageBarProvider />` kept. |
| `src/SchoolCollab.Families/Components/Layout/MainLayout.razor` | Same. |
| `src/Mobile/SchoolCollab.Mobile/Components/Layout/MainLayout.razor` | `<FluentDialogProvider />` → `<FluentProviders />`. |
| `tests/SchoolCollab.Admin.Tests.Unit/RoutesShellRenderTests.cs` | **New** — renders the real `Routes` and asserts the layout shell (`article.content`) renders. |
| `tests/SchoolCollab.Admin.Tests.Unit/MainLayoutProvidersTests.cs` | Pre-existing test pinned the four removed provider tags; updated to pin `<FluentProviders /` + `<FluentMessageBarProvider` (same intent). |
| `.github/copilot/rules/blazor-components.md` | New section: sibling-not-wrapper rule + "a green suite does not prove the app boots" + test placement, not presence. |
| `documents/solution/fluentui-v5-residual-backlog.md` | Pre-existing uncommitted §G addendum (obsolete-enum `CS0618`), carried unchanged. |

**Verdicts reported by the worker** (superseded by the parent's own numbers below): solution build 0 errors;
Admin suite 698/698; MAUI head 0 errors/0 warnings; full `Routes.razor` bodies and provider blocks quoted in its report.

**Discrimination proof (required by the plan).** With the Router temporarily re-wrapped, the new test failed:

```
failed Routes_At_The_Root_Route_Renders_The_Layout_Shell (540ms)
  Expected cut.FindAll("article.content") to contain a single item because the routed layout must render:
  <Router> has to stay the root element of Routes.razor (wrapping it in FluentUI v5's <FluentProviders>,
  which has no ChildContent parameter, discards every routed page and blanks the whole app with no error),
  but the collection is empty.
```

File restored and verified; the post-restore full suite is green. So the test genuinely discriminates — it is not a
vacuous pass.

**Step 5 sweep (read-only):** `FluentProviders` is the only confirmed instance. The worker swept every FluentUI
component used as a *wrapper* in `src/**/*.razor` against the v5 `ChildContent` inventory — all either declare it or
are used self-closing. One adjacent candidate found and NOT fixed (see Acceptance → residuals): `FluentWizard`.

**Deviations:** the `MainLayoutProvidersTests` edit (not in the plan; the plan's own 0-failures gate required it —
adjudicated by the parent as intent-preserving, not a weakening); comment wording; no Families/Mobile shell test
(the plan scoped the new test to the Admin suite).

## Review

Static diff review by `5991a022` (`ollama-cloud/kimi-k2.7-code`), read-only, against the frozen patch. **VERDICT: OK — no P1.**

| Sev | Finding | Disposition |
|---|---|---|
| P2 | `[.github/copilot/rules/blazor-components.md:202-203]` rule text overstated `FluentProviders`' parameter contract ("its only parameters are `ClassValue`/`StyleValue`") | **Fixed** by the parent, exactly as recommended: parenthetical removed, sentence now says "declares no `ChildContent` parameter". |
| P2 | `[src/SchoolCollab.Admin/Components/Routes.razor:14]` trailing newline dropped by the round | **Fixed** by the parent; all three `Routes.razor` now end `0a`. |

Checked-clean (reviewer-verified): all three routers root and attribute-preserving; no `<FluentProviders>` with children
anywhere in `src/`; provider host self-closing once per layout with the message-bar provider retained; no MSB locks.

## Acceptance

Parent-run authoritative pass on the fixed tree — the only source of truth for build/test numbers.

| Criterion | Result |
|---|---|
| **AC-1** all three `Routes.razor` root the Router; no `<FluentProviders>` with children in `src/` | **PASS** — worker + reviewer, parent-spot-checked |
| **AC-2** `<FluentProviders />` self-closing once per MainLayout; message-bar provider retained | **PASS** |
| **AC-3** `dotnet build SchoolCollab.slnx` | **PASS** — `Build succeeded`, **0 Error(s)**, 291 warnings (pre-existing) |
| **AC-4** MAUI head build | **PASS** — `Build succeeded`, 0 warnings, 0 errors |
| **AC-5** shell test passes **and is proven discriminating** | **PASS** — failure observed with the wrapper restored, then restored file + green suite |
| **AC-6** live probe renders a real layout | **PASS** — see table below |
| **AC-7** rule carries the placement rule + the lesson | **PASS** (P2 wording fixed) |
| **AC-8** no P1 from the static diff review | **PASS** — verdict OK |
| **AC-9** six deltas re-verified by the UI tester | **PASS** — tester leg `49e9ac6c` (its final chat message was empty; the report was recovered from disk — `tester-report.json` + 19 screenshots). 8 routes, 0 console errors, 0 failed requests, 0 page errors, 0 `_content` 404s, 0 route 404s. Deltas 3 and 4 verified with real interaction evidence (links measured: no invisible/low-contrast; dropdown opened a listbox with 20–30 options and picked). Delta 2 verified on the only page that has tabs. Delta 1 verified statically (see below). |
| Parent `dotnet test` — Admin suite | **698 / 698 passed, 0 failed** |
| Parent `dotnet test` — ArchitectureTests | **114 / 114 passed, 0 failed** |

**AC-6 evidence — live browser probe, same instrument that documented the break:**

| Route | Before (broken) | After (fixed) |
|---|---|---|
| `/` | 688 B, 21 els, innerText 0, no title | 13,592 B, 101 els, shell ✓, brand ✓, title "Home" |
| `/students` | idem | 16,754 B, 121 els, shell ✓, nav ✓, title "Students" |
| `/settings` | idem | 26,571 B, 176 els, shell ✓, nav ✓, title "Settings" |
| `/assignments` | idem | 16,254 B, 121 els, shell ✓, nav ✓, title "Assignments" |

0 console errors and 0 failed requests on every route, before and after the P2 rebuild. The provider host now renders
inside the layout (`.fluent-providers` present, once) instead of swallowing the app.

**P1 list: CLOSED** — no P1 remained at the rework bound; the two P2s were fixed inside the round.

**Tester findings — adjudicated by the parent before any rework (3 of 7 rejected):**

| Finding | Claim | Adjudication |
|---|---|---|
| F1 | every breadcrumb link carries `aria-current="page"` | **REJECTED — false positive.** "Settings Home" is `NavMenu.razor:45`; "All Students" is line 135. A parent-run probe of `/settings`, `/students/create`, `/assignments/create` found that **every** `aria-current` element is inside the side nav (2/3/4 — the exact counts reported as crumbs). The probe measured navigation, not a breadcrumb. |
| F2 | flag Delete opens no confirm dialog | **REJECTED — explained.** `ConfigFlagDetail.DeleteAsync` opens with `if (string.IsNullOrWhiteSpace(_lifecycleReason)) return;` — the flag Delete is reason-gated, so a click with no reason no-ops silently. That path never used `ConfirmDialog`. |
| F3 | nav icons render on Home only | **REJECTED — false positive.** The 11 items sit inside `<FluentNavCategory … Expanded="false">`; collapsed children are not rendered, and `NavMenu.razor` declares `IconRest` on all of them. |
| F4/F6/F7 | legacy width classes on the wrapper; zero-size empty wrappers; placeholders instead of labels | **Accepted as cosmetic** → backlog §J (the label item is an a11y weakness and should be promoted first). |
| F5 | folder-tab chrome absent on list pages | **Accepted as scope** — tabs exist on exactly one page (`/assignments/create`), where they were verified. |

Rejecting three of seven findings is the substance of the adjudication rule: relaying them into a rework would have spent a
cycle on phantom defects. The tester leg was nonetheless valuable — it cleared deltas 3, 4 and 6 with real interaction
evidence that no bUnit test produces.

**Delta 1 (confirmation dialogs render a title) — verified statically.** The tester could not reach one because the dev
tenant's lists are empty (no rows → no destructive buttons), although `ShowConfirmDialogAsync` has 46 call sites. The chain
is correct by inspection: `ConfirmDialog.razor` → `<DialogShellBody>` → `<FluentDialogBody>` (the only place v5 renders the
header/title slot and the close X), with `ShowConfirmDialogAsync`/`BuildShellOptions` setting `Header.Title` and
`CloseAction.Visible = true`. No test asserts it — recorded as backlog §I.

**Residuals settled by the owner (Q71, "all as recommended") — backlogged, not fixed in this round:**

| # | Residual | Where |
|---|---|---|
| a | `ConfirmDialog`'s title is untested (the delta-1 gap) | backlog §I |
| b | cosmetic `fluent-field` cluster + placeholder-as-label a11y weakness | backlog §J |
| c | nav marks 2–3 items `aria-current="page"`; `/students` listed twice | backlog §K |
| d | `GuardianSetupWizard` — `<FluentWizardStep>` children without `<Steps>`; **pre-existing**, verify-first | backlog §H |

**Verdict: CLOSED.** No P1; both P2s fixed in-round; all acceptance criteria PASS; residuals accepted and backlogged above.

**Residual (d) detail — adjudicated PRE-EXISTING, not a v5 regression:** `GuardianSetupWizard.razor:26` passes
`<FluentWizardStep>` children directly to `<FluentWizard>` without the required `<Steps>` element. v4.14.2's
`FluentWizard.razor` renders the same `@Steps` fragment, so the usage was already wrong before the upgrade; it does not
block this round and does not re-open the v5 arc. Full note: backlog §H (verify-first).

**Tester-scope handover (UI trigger fired: `.razor` files changed)** — Q69 leg, verbatim scope:
1. Confirm-dialog title/X/Cancel-Save after `ConfirmDialog` was wrapped in the shared shell.
2. Folder-tab chrome re-pointed to v5's DOM (active-indicator).
3. The 45 `FluentLink` conversions (30 `Default`, 14 `Subtle`) + the one accent `FluentButton` — legibility and navigation.
4. Inputs/dropdowns inside the `<fluent-field>` wrapper — row alignment, labels, a dropdown open + pick.
5. `FluentNav`/`FluentNavItem`/`FluentNavCategory` + the authored breadcrumb (crumbs, separators, `aria-current`).
6. Console errors and 404s per page — on this freshly started process a `_content/...` 404 is a real finding.
Routes: `/`, `/students`, `/settings`, `/coded-values`, `/assignments`, `/assignments/create`, `/students/create`, `/config-flags`.

