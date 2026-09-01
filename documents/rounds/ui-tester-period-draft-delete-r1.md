# UI Tester Report — period-draft-delete r1

> **Round:** r1 · **Scope:** verbatim from acceptance doc's UI-tester scope handover (4 surfaces)
> **Provider:** pi · Tester model: `ollama/minimax-m3:cloud` (report persisted by parent from inline response; first tester launch stalled in 13s with zero tool calls — revived via `resume` with corrected instructions before this valid pass)

**Verdict: P2 — 0 P1, 2 P2.** Conformance-side quality holds: no Draft-gating leaks, no perpetual spinners, no swallowed errors, all three surfaces show the error bar on API failure (EC-3). Tests: both bUnit suites passed (398/514). No files edited by tester (read-only role).

## Findings

| Sev | File:Line | Defect (as reported) | Evidence | Fix suggested |
|---|---|---|---|---|
| P2 | `Periods.razor:39-47` + `OnDeleteAsync` + `PeriodDeletePrompts.cs` | Claimed: sub-period row's Type column is an `aria-hidden` `—` while the delete confirmation names "this Term/Semester" — kind context invisible to sighted users. | Reporter clicked kebab → confirm reads "Delete "Term1"? This permanently deletes this Term." while Type shows `—`. | Render the kind badge for sub-period rows in the Type column. |
| P2 | `Edit.razor:139,188` (no `_loadError = null` on success) | `_loadError` set on load/delete failure but never cleared after a subsequent successful load/delete — MessageBar persists for the component lifetime. | Offline-load repro; delete-error path at 188. | Clear `_loadError` on the success path. |

## Parent disposition (post-tester)

- **P2-1 — DISMISSED (tester premise incorrect).** The `—` lives in the **Sub-periods** column (`Periods.razor:49`, decorative null-count marker, `aria-hidden` correctly). The **Type** column renders a visible `FluentBadge` from `GetKindLabel(context)` (`Periods.razor:34`) for every row — the same function `OnDeleteAsync` feeds into `PeriodDeletePrompts.SubPeriodMessage`, so the confirmation wording and the on-screen kind are consistent and sighted-visible. No fix required.
  - Adjacent latent nit recorded out-of-round: `GetKindLabel` (`Periods.razor:143`) defaults a null-division sub-period to "Semester" while `Edit.razor:163` defaults to "Term" — inconsistent fall-through for division-less sub-periods. Not reachable from real data today (division is set at year creation and sub-periods inherit it; reviewer P2-2 added `"division":"Terms"` to the bUnit fixture to match the real DTO). Not reworked in-round.
- **P2-2 — FIXED by parent** (`Edit.razor` `OnInitializedAsync` success path now sets `_loadError = null`). Re-verified from parent: `dotnet build SchoolCollab.sln -c Debug` 0 errors; Admin.Tests.Unit 514 passed; Students.Tests.Unit 398 passed.

## Out-of-round observations (for the parent/user, not rework)

- `Periods.razor` `ReloadAsync` — on post-delete list-fetch failure, `_items` keeps pre-delete state (a phantom Delete action can linger until manual refresh). Within spec's "standard error bar" guidance; minor staleness.
- `Edit.razor` initial-load failure message format differs from the delete-failure format — pre-existing, not a round regression.

## Verdict JSON (from tester)

```json
{"verdict": "P2", "p1Count": 0, "p2Count": 2, "outOfRound": ["Periods.razor ReloadAsync stale-items nuance", "Edit.razor error-format inconsistency (pre-existing)"], "testsRun": {"Admin.Tests.Unit": "pass", "Students.Tests.Unit": "pass"}}
```

**Round verdict after disposition: PASS** — UI tester P2-1 dismissed with evidence; P2-2 fixed and re-verified. No P1s anywhere in the round.