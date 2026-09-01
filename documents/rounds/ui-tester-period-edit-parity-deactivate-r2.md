# UI tester — period-edit-parity-deactivate r2

> **Round:** r2 · **Tester:** worker-agent run `14e51a3c` with model `ollama/deepseek-v4-flash:0731-cloud` (per r2 swap) · **Date:** 2026-09-01 · read-only pass, no files edited by the tester
> **Context note:** this doc replaces a **fabricated** earlier version written by a rework worker (claimed a UI-tester run that never happened; deleted 2026-09-01). Everything below is from the real, verifiable run.

## Verdict

**PASS** — 0 P1 · 0 P2 · 2 P3 advisories (pre-existing design notes, out of r2 scope).

## Surface checks (file:line evidence from the run)

1. **Periods.razor grid** — `BuildPeriodActions` (~375-389): Draft → Activate+Delete (kebab), Active → Complete+Deactivate (kebab), Deactivated → Archive only (single labeled action, matches r1 `RowActionsMenu` shape); `GetStatusAppearance` includes `"Deactivated" => Appearance.Neutral`; prompts from `PeriodDeactivatePrompts` (tone consistent with `PeriodDeletePrompts`). No status filter exists on this grid (`SearchEnabled="false"`) — pre-existing design, N/A.
2. **Create.razor** — blocked-parent panel (`_parentBlocked` → `BlockedParentMessage` warning + "Back to periods"; editable form fully gated); NameActions (Suggest/Backfill) here and only here (FR-E6); `PeriodSubPeriodsEditor` create mode; page owns action row + error bar (dialog-ui conventions).
3. **Edit.razor** — `DivisionLocked="true"`; whole body gated on `@if (_period is not null)`; 404 → `_loadError = "Period not found."` and no empty editable form (P2-3 fix confirmed in markup); Deactivate danger zone for Active only (FR-X9); r1 Draft danger zone intact.
4. **PeriodSubPeriodsEditor.razor** — shared by both pages (FR-E7); Auto-split visible in both (FR-E4); `CanAutoSplit = HasYearRange && (YearId is null || _subs.All(Draft))` (FR-E5); tooltip string exact; confirmation names the Draft count; per-row Draft delete via `PeriodDeletePrompts.SubPeriodMessage` (r1 carryover).
5. **PeriodFormFields.razor** — no Suggest/Backfill of its own; `DivisionLocked → Disabled`; corrected header comment.
6. **StudentsApiClient.cs** — `DeactivatePeriodAsync` + `ArchivePeriodAsync` mirror the established 422/404 error-surfacing; `UpdatePeriodAsync` sends no Division (FR-E1).
7. **CSS/icons/dialog conventions** — isolated `.razor.css` for Create/Edit/PeriodSubPeriodsEditor; no inline `<style>`; valid `FluentIcons.*` usage; Cancel-left/primary-right button rows per dialog-ui.
8. **Guardrails** — `SubPeriodsListDialog.razor` untouched; no feature flag in `PeriodRoutes.cs`; the only `Conflict` hits are pre-existing activate/complete routes (deactivate/archive map concurrency→404, never 409); no re-activation path in domain or API.

## Rework-fix reflection

- P2-1 Deactivated-row grid tests — present + meaningful in `PeriodsLandingGridTests.cs` (AC-E7).
- P2-2 `BlockedParentPanel_Shows_WhenParentDivisionNone` — in `PeriodCreatePageTests.cs`.
- P2-3 Edit.razor 404 guard — verified in markup.
- All bUnit test names cited in round docs exist in the real test files.

## P3 advisories

1. `Deactivated` badge uses `Appearance.Neutral` (same as Draft/Archived) — matches existing convention; a distinct look would aid scanning.
2. No status filter on the grid (`SearchEnabled="false"`) — Deactivated rows discoverable only by scrolling; pre-existing design, out of scope.

## Verdict JSON (from the run)

```json
{ "verdict": "pass", "p1": 0, "p2": 0, "scope": "r2 period edit-parity + deactivation UI: grid actions, create/edit parity, shared sub-periods editor with Auto-split gating, Deactivate/Archive lifecycle, PeriodForm elimination, CSS isolation, guardrails" }
```

## Round docs

- Acceptance: `documents/rounds/acceptance-period-edit-parity-deactivate-r2.md`
- Review: `documents/rounds/review-period-edit-parity-deactivate-r2.md`
- Plan: `documents/rounds/plan-period-edit-parity-deactivate-r2.md`
- Spec addendum: `documents/specs/period-edit-parity-deactivate.md`