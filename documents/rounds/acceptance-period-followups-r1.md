# Acceptance: round `period-followups-r1`

- **Kind:** orchestrator acceptance doc (four-agent `orchestrator-worker-reviewer` round). Written only by the orchestrator.
- **Round slug:** `period-followups-r1`.
- **Provider:** pi (role-model ids were not recorded in the worker/reviewer hand-off reports).
- **Inputs:** `documents/rounds/plan-period-followups-r1.md`; worker final report; reviewer final report. **No separate `review-period-followups-r1.md` exists** — the reviewer returned findings inline in its final response; they are recorded in the "Reviewer findings handling" table below.
- **Branch:** `docs/followups-and-docs-layout`; all changes are uncommitted working-tree state (nothing staged).

## Per-item status

### §1 — Stale `documents/specs/` self-references (doc-only) — ✅ PASS

- **Gate verified this pass:** `git grep -c "documents/specs/" -- "documents/rounds/*-drop-periodtype.md"` → **0 matches** (no output, exit 1).
- Per-file: 6/3/4/2 stale refs (15 total) → 0 across `acceptance-`, `plan-`, `review-`, `ui-tester-drop-periodtype.md` (12 +1-line header/path edits total).
- **§10 false claim fixed:** `acceptance-drop-periodtype.md` now reads "…Round docs were moved to `documents/rounds/`."; the historical note about the reverted specs→rounds move was **reworded** ("the old specs folder"), not deleted — historical meaning preserved.
- Worker changed path targets + the §10 sentence only; no other prose touched.

### §4 — P2-5/P2-6 fix in `PeriodForm.razor` + bUnit test — ✅ PASS

- **P2-5 (verified in diff):** new `_parentBlocked` field, `ShowBlockedPanel` property (`!PeriodId.HasValue && _parentBlocked`), `BlockedParentMessage` const. The None-division `?parent=` branch sets `_parentBlocked = true` — `_error` is again the pure save/validate surface. When blocked, the editable form (Division select, parent, name + Suggest/Backfill, Dates, tip, submit row) is replaced by a `FluentMessageBar` (Warning, `class="mt-3"`) + "Back to periods" `FluentButton` wired to `CancelAsync` (honours `OnCancel` → `CancelRoute`; no hardcoded route).
- **P2-6 (verified in diff):** prefill gate changed to `PrefillAcademicYear && string.IsNullOrWhiteSpace(_name) && !_parentBlocked` with an explicit P2-6 comment — the skip is now a documented consequence of the blocked state, not an `_error` side effect.
- **Untouched, confirmed absent from `git status`:** host pages `Create.razor`/`Edit.razor` (plan: verify only), `SubmitAsync`, wizard params, and all deferred-item files (`Periods.razor`, `SubPeriodsSection.razor` P2-4, `SubPeriodsListDialog.razor` P2-7, `JoinGroupsDialog.razor` P2-8).
- **Test scaffolding (verified):** `SchoolCollab.Students.Tests.Unit.csproj` gained `<PackageReference Include="bunit" />` (CPM-compliant, no `Version`) + Application project reference. New `PeriodFormBlockedParentTests.cs` implements exactly the 4 planned cases — blocked render (warning text, no Division select, Back button present), back navigation to `CancelRoute`, no-prefill (empty name input), Terms positive control (normal form) — using a scripted `HttpMessageHandler` (no networking/timers) and a real `PeriodDto` serialized with web-default JSON options matching `StudentsApiClient.ListPeriodsAsync`.

### Build/test numbers — child-run (parent appends authoritative §2 numbers)

| Check | Child-run (worker + reviewer reports) | Acceptance re-run (this session) |
|---|---|---|
| `dotnet build SchoolCollab.sln -c Debug --nologo -v q` | 0 errors, 6 warnings (pre-existing) | **0 errors**, 6 warnings — all pre-existing NuGet audit advisories (NU1903 SQLitePCLRaw/SSH.NET, NU1902 AngleSharp) |
| `dotnet test tests/SchoolCollab.Students.Tests.Unit` | 364/0 (360 baseline + 4 new) | **364/364 passed** (`--no-build -c Debug`, 4s 471ms) — acceptance re-run |
| `dotnet test tests/SchoolCollab.Admin.Tests.Unit` | 502/0 | not re-run (§2 full matrix is parent-owned) |
| `dotnet test tests/SchoolCollab.Settings.Tests.Unit` | 446/0 | not re-run (§2 full matrix is parent-owned) |

**Parent action:** append the authoritative §2 build/test numbers from the clean-commit verification below.

### Authoritative §2 numbers — parent-run (2026-08-31, post-rework iteration 1)

Run by the parent agent on the final working tree (after rework pass 1 touched `PeriodForm.razor` + the test file):

| Check | Parent-run result |
|---|---|
| `dotnet build SchoolCollab.sln -c Debug --nologo -v q` | **Build succeeded — 0 errors, 6 warnings** (pre-existing NuGet audit advisories) |
| `dotnet test tests/SchoolCollab.Students.Tests.Unit` | **364/364 passed, 0 failed, 0 skipped** (6.4s) — includes the 4 new `PeriodFormBlockedParentTests` cases + rework regression pins |
| `dotnet test tests/SchoolCollab.Admin.Tests.Unit` | **502/502 passed, 0 failed, 0 skipped** (43.7s) |
| `dotnet test tests/SchoolCollab.Settings.Tests.Unit` | **446/446 passed, 0 failed, 0 skipped** (2.6s) |

All four §2 acceptance gates **pass** on the final post-rework tree. **Round §2: GREEN.**

### Rework pass 1 (UI-tester loop, 2026-08-31)

- **Trigger:** UI tester pass 1 returned FINDINGS — P1: false create-hint rendered above the blocked panel (`PeriodForm.razor`); P2-1 spacing (reviewer R-2); P2-2 no focus management; P2-3 defensive `CancelAsync` no-op deferred; out-of-round observations → parent backlog.
- **Rework (worker-rework2, `13bfe607`):** hint paragraph gated `@if (!ShowBlockedPanel)` (h3 title kept); Back button wrapped in `<div class="mt-3">`; `Autofocus="true"` on the Back button; regression-pin assertions appended to the existing blocked-render test (suite held at 364 — no new test methods).
- **Re-verification (ui-tester-r2, `fc0375d7`):** **TESTER VERDICT: PASS** — all 5 criteria confirmed; focus verified per documented FluentButton API contract (browser-focus not exercised by bUnit; Playwright would be required, no rework needed).

### Command quirk (documented)

`dotnet test … --nologo` fails on this MTP-based test project (exit 5, `Unknown option '--nologo'`). Correct invocation:
`dotnet test tests/SchoolCollab.Students.Tests.Unit/SchoolCollab.Students.Tests.Unit.csproj --no-build -c Debug`.
This matches the reviewer's earlier resolution; the acceptance re-run confirms `--nologo` was the offending flag.

## P2 disposition table (plan §"P2 disposition table") — ✅ CONFIRMED

| ID | Disposition | Acceptance confirmation |
|---|---|---|
| P2-1 Group filter year-scoping | ✅ Verified fixed upstream | Not in round diff; upstream evidence cited in plan. |
| P2-2 Activate Draft-only | ✅ Verified fixed upstream | Not in round diff; upstream evidence cited in plan. |
| P2-3 `GetKindLabel` "Term" fallback | ⏸ Deferred | `Periods.razor`/`SubPeriods.razor`/`SubPeriodsListDialog.razor`/`SubPeriodsSection.razor` absent from `git status` — no code exists. |
| P2-4 `_typeText` dead assignment | ⏸ Deferred | `SubPeriodsSection.razor` absent from `git status`. |
| P2-5 None-division `?parent=` stuck form | ✅ Fixed this round | `PeriodForm.razor` blocked panel, verified in diff; covered by bUnit test 1–2. |
| P2-6 Prefill silently skipped via `_error` | ✅ Fixed this round (folded) | Gate now `!_parentBlocked`; covered by bUnit test 3. |
| P2-7 Inactive-row state drift dialog/grid | ⏸ Deferred | `SubPeriodsListDialog.razor` absent from `git status`. |
| P2-8 `JoinGroupsDialog` kind fallback | ⏸ Deferred | `JoinGroupsDialog.razor` absent from `git status`. |

## Reviewer findings handling (reviewer returned findings inline; no review doc in repo)

| # | Finding | Severity | Handling |
|---|---|---|---|
| R-1 | `.pi/skills/orchestrator-worker-reviewer/SKILL.md` modified (+61 lines: Cline/`clinepass` provider profiles) — neither the plan nor the worker task authorized editing the orchestrator skill file | P2 | Acknowledged and verified: additive, non-destructive, doc-only, unrelated to P2-5/P2-6 code. Acceptance does not revert (no-fix constraint). **Parent must revert or quarantine this file before the working tree is committed.** Listed under residual risks. |
| R-2 | Blocked-panel "Back to periods" button sits directly after the `FluentMessageBar` with no spacing wrapper (`mt-3`/gap) — inconsistent with existing markup, cosmetic | P2 | Verified in diff: `class="mt-3"` is on the message bar only, the button has none. Functionally harmless; routed to the UI tester (below) as a look-at item for a possible one-line touch-up. Not a blocker. |
| R-3 | CLI quirk on `dotnet test` (reviewer resolved via csproj path + `--no-build`) | note | Confirmed root cause this session (--nologo unsupported by MTP); invocation documented above. No action. |

## Merge verdict

**OK with notes** — no P0/P1 blockers. Only pre-commit action: the parent decides R-1 (revert or quarantine the skill-file edit).

## UI-TESTER SCOPE HANDOVER (round `period-followups-r1`)

The round touched UI (`PeriodForm.razor`). Closed list derived from `git status --short` plus the two unchanged host surfaces the plan requires verified:

TESTER-SCOPE-HANDOVER
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor` — **changed**: new blocked panel (Warning `FluentMessageBar` + "Back to periods" `FluentButton` → `CancelAsync`) and `_parentBlocked` prefill gate (P2-5/P2-6); primary adversarial surface.
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Create.razor` — **unchanged**: create-from-`?parent=` host page; verify the blocked panel renders correctly in the real host context (incl. R-2 button spacing).
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Edit.razor` — **unchanged**: edit host page; verify edit-mode rendering and the bottom `_error` bar are unaffected by the markup restructure.
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriods.razor` (~line 36 row action) — **unchanged**: first `create?parent=` entry point; confirm navigation still lands in the blocked or normal form as appropriate.
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriodsListDialog.razor` (~line 242 action) — **unchanged**: second `create?parent=` entry point (dialog); same blocked-path verification.
- Test scaffolding only — `tests/SchoolCollab.Students.Tests.Unit/PeriodFormBlockedParentTests.cs` + `bunit`/Application references in `SchoolCollab.Students.Tests.Unit.csproj`: bUnit-only, no runtime UI impact; **no scoped CSS files were added** (blocked panel reuses FluentMessageBar/FluentButton + `mt-3`).
END-TESTER-SCOPE-HANDOVER

## Evidence

- **Changed files (working tree):** `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor`; `documents/rounds/acceptance-drop-periodtype.md`; `documents/rounds/plan-drop-periodtype.md`; `documents/rounds/review-drop-periodtype.md`; `documents/rounds/ui-tester-drop-periodtype.md`; `tests/SchoolCollab.Students.Tests.Unit/SchoolCollab.Students.Tests.Unit.csproj`; `.pi/skills/orchestrator-worker-reviewer/SKILL.md` (R-1, unauthorized). New: `documents/rounds/plan-period-followups-r1.md` (orchestrator plan), `tests/SchoolCollab.Students.Tests.Unit/PeriodFormBlockedParentTests.cs`.
- **Tests added:** `PeriodFormBlockedParentTests.cs` — 4 bUnit tests (blocked render, back-navigation, no-prefill, Terms positive control).
- **Commands run:** see table in "Build/test numbers" and the §1 grep gate + `git diff --cached --stat` (empty → nothing staged).
- **Residual risks:** R-1 unauthorized skill-file edit (parent revert/quarantine before commit); R-2 cosmetic blocked-panel button spacing (defer to UI tester); pre-existing NU1903/NU1902 NuGet audit advisories (backlog, not caused by this round); P2-3/4/7/8 remain deferred by plan decision.
- **No staged files:** confirmed — `git diff --cached --stat` is empty.

## Rework plan (tester pass 1)

The UI tester returned **FINDINGS** on the delivered blocked-panel UI. Dispositions and the exact implementation design follow. All fixes go through a worker (orchestrator does not edit code); the worker may touch **only** `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor` and `tests/SchoolCollab.Students.Tests.Unit/PeriodFormBlockedParentTests.cs` — no new files, no plan/acceptance/solution doc edits, no commit/push.

### Findings and dispositions

| # | Finding | Severity | Disposition | Where |
|---|---|---|---|---|
| P1 | False create-hint in blocked state: `@if (ShowHeader)` block sits outside the `ShowBlockedPanel` else-branch gate, so hosts with `ShowHeader` defaulting true (`Create.razor`, `Edit.razor`) render the h3 + `CreateHint` ("Pre-filled with the current academic year… Use the buttons to suggest or backfill…") above the warning + Back button — every word is factually false under the blocked panel. Round-created visibility defect. | P1 | **Fixed this iteration** | `PeriodForm.razor` |
| P2-1 | Blocked-panel Back button has no spacing wrapper: message bar has `class="mt-3"`, button sits flush beneath; inconsistent with the file's spacing patterns. | P2 | **Fixed this iteration** | `PeriodForm.razor` |
| P2-2 | No focus management on the blocked-panel Back button: after navigation, focus is not on the only action; keyboard/SR users must tab to it. | P2 | **Fixed this iteration** — `FluentButton` v4.14.2 has a first-class `Autofocus` parameter (XML doc: "Determines if the element should receive document focus on page load"), and the web-components bundle forwards it (`?autofocus="${e=>e.autofocus}"` on the inner `<button class="control" part="control">`). Clean parameter-level fix, no hack. | `PeriodForm.razor` |
| P2-3 | `CancelAsync` silent no-op when neither `OnCancel` nor `CancelRoute` set; current hosts always pass `CancelRoute`. | P2 | **DEFER — do not fix** (defensive-only future-caller concern; consistent with the round's deferred-defensive-items pattern P2-3/4/7/8). | — |
| Notes | (a) init loading window shows an empty header before the API resolves (missed `FluentProgressRing` opportunity); (b) no test for the API-failure-during-init path (`_error` coexisting with the blocked panel); (c) `InitialParentPeriodId` XML doc should document the blocked-panel outcome for callers. | notes | **BACKLOG ONLY** — out-of-round observations for the parent. Do **not** implement this iteration. | — |

### Implementation design (exact, all inside `PeriodForm.razor`)

1. **P1 — chosen variant: keep the h3, suppress only the false hint paragraph.** The page still *is* "New period" (both blocked and normal are create-mode intents), so the title stays accurate and useful; everything else in the header (the `CreateHint` paragraph) is false under the panel. Gate the `<p class="form-hint">` with `!ShowBlockedPanel`:

   ```razor
   @if (ShowHeader)
   {
       <div class="form-section-header">
           <div>
               <h3>@HeaderText</h3>
               @if (!ShowBlockedPanel)
               {
                   <p class="form-hint">@(PeriodId.HasValue ? EditHint : CreateHint)</p>
               }
           </div>
       </div>
   }
   ```

   Being inside `PeriodForm.razor`, every host benefits (`Create.razor`, `Edit.razor`, any future host); the wizard host (`ShowHeader=false`) is unaffected (hint never rendered there). In edit mode `ShowBlockedPanel` is always false, so `EditHint` is unchanged.

2. **P2-1 — spacing wrapper:** wrap the Back button in `<div class="mt-3">`, the same utility pattern both `FluentMessageBar`s in this file already use:

   ```razor
   <FluentMessageBar Intent="MessageIntent.Warning" class="mt-3">@BlockedParentMessage</FluentMessageBar>
   <div class="mt-3">
       <FluentButton Appearance="Appearance.Accent" Autofocus="true"
                      OnClick="CancelAsync">Back to periods</FluentButton>
   </div>
   ```

3. **P2-2 — focus management:** `Autofocus="true"` on the Back `FluentButton` (as above). Caveat carried to the tester: the bUnit test can only pin the rendered `autofocus` attribute (regression pin); the actual focus landing is browser behaviour and is part of the tester's re-verify. If the tester proves focus does not land, pass 2 downgrades P2-2 to defer-with-note — no JS hacking inside the component.

4. **Test extension (regression pins), in the existing blocked-render test `BlockedParent_NoneDivision_RendersWarning_NoForm_BackButton`** in `PeriodFormBlockedParentTests.cs` (new assertions appended after the existing ones — no new test methods required, suite count stays 364):
   - P1 pin: `cut.Markup.Should().NotContain("Use the buttons to suggest or backfill")` — the false `CreateHint` fragment is absent in the blocked render. `EditHint` never renders in create mode; not pinned additionally.
   - P1 pin (keep-the-title decision): `cut.Markup.Should().Contain("<h3>")` (or `"New period"`) — the header title still renders in the blocked state.
   - P2-1 pin: `cut.Find("div.mt-3 fluent-button")` (descendant selector) resolves to the button whose text contains "Back to periods" — the spacing wrapper exists. If `FluentMessageBar` also renders a `div.mt-3` ancestor, the descendant selector still uniquely pins the wrapper; use `div.mt-3 > fluent-button` as fallback only if the loose selector over-matches.
   - P2-2 pin: `cut.Markup.Should().Contain("autofocus")` — FluentButton renders the attribute for `Autofocus="true"`. Contingency: if the lib does not splat the attribute into bUnit markup, drop **only this assertion**, keep the `Autofocus="true"` parameter, and note it in the worker report (tester verifies focus in-browser). Do not force it via `AdditionalAttributes`.

### Worker command gates

- `dotnet build SchoolCollab.sln -c Debug --nologo -v q` → **0 errors** (6 pre-existing NuGet audit warnings acceptable).
- Then `dotnet test tests/SchoolCollab.Students.Tests.Unit/SchoolCollab.Students.Tests.Unit.csproj --no-build -c Debug` → **0 failures**.
- **Documented quirk:** do **NOT** pass `--nologo` to `dotnet test` on MTP test projects (exit 5, `Unknown option '--nologo'`) — see "Command quirk" above.
- No commit/push; changes remain uncommitted working-tree state.