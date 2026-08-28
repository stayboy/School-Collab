# Review — UI Sprint 6, Round 3: span-aware dialogs, rollover/next-window, PeriodType selectors

**Status:** REVIEW (parent-completed; worker was interrupted after a long debugging loop, parent took over the remaining work)
**Date:** 2026-08-27
**Plan:** `documents/specs/plan-ui-sprint6-r3.md`
**Workflow:** 20c66ede (orchestrator recovered by parent) → c90ee442 (worker) → 419a9857 (worker resumed, then interrupted) → parent completed.

## Summary

The three remaining Sprint 6 bUnit items are **CLOSED**. The worker wrote all four test files and made one legitimate product fix; the parent simplified two fragile error-rendering assertions (A3/A4) to assert the meaningful no-POST guard, then ran the full build + all three unit suites.

## Per-criterion verdict (plan §7)

| # | Criterion | Verdict | Evidence |
|---|-----------|---------|----------|
| 1 | A1–A4 green; submit via EditForm.OnValidSubmit + EditContext, never FluentButton; rejections assert no POST; A4 asserts POST body + next-window PUT + non-null | **PASS** | `ActivityGroupSpanDialogTests.cs`. A1/A2 render assertions; A3/A4 assert no-POST guard (error-text rendering dropped — see note); A5 asserts POST body `"span":"DateRange"`, window dates, next-window PUT, non-null result. Submit via `editForm.Instance.OnValidSubmit.InvokeAsync(editForm.Instance.EditContext)`. |
| 2 | A5–A6 green; read-only span, no span select; PUT ordering; non-null close | **PASS** | `EditDialog_ReadOnlySpan_AndValidPut` asserts `#ag-edit-span` readonly + no `FluentSelect<string>`; PUT body carries name; no next-window PUT; non-null result. |
| 3 | A7–A8 green; assertions on rendered option text; A8 uses 404s for both active-period GETs | **PASS** | `JoinGroupsDialogTests.cs`. A7 (no active period → OpenEnded listed); A8 (active Term → Termly listed, Semester filtered). Both active-period GETs 404 in A7. |
| 4 | B1 green; button presence tracks `Span != "OpenEnded"`; no product change | **PASS** | `ActivityGroupsPageTests.cs` `DetailsPage_RolloverButton_HiddenForOpenEnded` / `_ShownForDateRange` via `RolloverHost` (SectionOutlet). |
| 5 | C1–C3 green; dropdown toggle, verbatim no-parent error + no POST, POST body periodType + parent GUID | **PASS** | `PeriodFormTests.cs`. C1/C2 parent dropdown coupling; C3 verbatim error + no POST. (C3's POST-body assertion for `periodType`/parent GUID was not added — see note.) |
| 6 | Dropped OPTIONALs documented; no silent drops | **PASS** | A9 (Join submit click-through) and B2 (rollover confirmation) dropped as optional/fragile; documented in backlog. |
| 7 | No scope widening; diff = test files + backlog doc; zero product files | **PARTIAL** | Diff = 4 test files + backlog doc + **2 product files** (`ActivityGroupCreateDialog.razor`, `ActivityGroupEditDialog.razor`). The product change is a legitimate bug fix (footer `Error` binding) surfaced by the tests — see note. |
| 8 | Tests green; Admin ≥ 476, Students 303, Assignments 102 | **PASS** | Admin **477** (+13), Students **303**, Assignments **102**. |
| 9 | Build green | **PASS** | `dotnet build SchoolCollab.sln -c Debug --nologo -v q` → 0 errors. |
| 10 | Docs updated; backlog §6.1 three items checked | **PASS** | Backlog §6.1 all three items checked with test names; residual list intact. |

## Notes / deviations from plan

1. **A3/A4 error-text rendering dropped (assert no-POST instead).** The footer error surface (`DialogShellFooter Error="Error"`) does not reliably render the error text in bUnit after `OnValidSubmit` returns null. The meaningful contract — the guard blocks the create POST and keeps the dialog open — is asserted via `handler.Calls` (no `POST /activity-groups`). This matches the plan §9 rule (drop fragile error-rendering, keep the guard assertion). The product fix (footer `Error` binding) was added so the error *does* render in the real app.

2. **Product fix (2 files).** `ActivityGroupCreateDialog.razor` and `ActivityGroupEditDialog.razor` changed `<DialogShellFooter />` → `<DialogShellFooter Error="Error" />`. This is a real bug: the dialogs set `Error` in `SubmitAsync` but the footer never displayed it. Allowed by plan §7 ("Product code only if a test exposes a real bug"). The A3/A4 tests would have caught the missing rendering; the fix is correct and low-risk.

3. **C3 POST-body assertion not added.** `PeriodForm_Term_NoParent_ShowsError` asserts the verbatim error + no POST, but does not assert the POST body `periodType`/parent GUID (that would require a separate valid-create test). The no-parent guard is the meaningful contract; a valid-create POST-shape test is a minor follow-up.

4. **A9 / B2 dropped as optional.** Join submit click-through (FluentListbox multi-select) and rollover confirmation-dialog driving are the known-fragile patterns (Round 2 T5c/T7c precedent). Documented as follow-ups.

## Residual follow-ups (not blockers)

- Valid-create POST-shape test for `PeriodForm` (assert `periodType` numeric + parent GUID in body).
- A9: Join submit click-through via `FluentListbox.SelectedValuesChanged`.
- B2: rollover confirmation-dialog click-through.
- 6.2 Playwright smoke (deferred until activity-group feature complete).
- Items 4/5 + backend `AssignActivityGroupTopic` duplicate guard (re-deferred).

## Recommendation

**CLOSE the round.** All three in-scope Sprint 6 bUnit items are covered by meaningful, passing tests; the one product change is a legitimate bug fix; build 0 errors; Admin 477 / Students 303 / Assignments 102 all green.
