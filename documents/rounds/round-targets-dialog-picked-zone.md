# Round — targets-dialog-picked-zone

**Status:** CLOSED
**Tier:** 2 (Light round — orchestrator plan, one worker, static diff reviewer)
**Date:** 2026-10-07

## Goal

Two owner-directed enhancements to the assignment **Targets & audience** add dialog:
1. the picked pills had no visual home — give them an always-rendered, demarcated section;
2. switching the Category must **reset** the picks.

## Plan

- **Change 1** — replace the conditional chip row (`@if (Model.PickedEntries.Count > 0) { <div class="entity-grid-chips"> … }`) with an **always-rendered** `<div class="picked-targets">` zone carrying a muted caption (`Picked targets`), an empty-state hint (`No targets picked yet.`), and the same identity-dismiss `<Chip>` loop when non-empty. Zone stays ABOVE the value picker.
- **Change 2** — add one model method `TargetsAndAudienceDialogModel.ClearPicked()` and call it in `OnCategoryChanged` when the category actually changes, before `Model.Category` is assigned. The `Everyone` checkpoint path (`PickEveryone()`) is preserved.
- Scope guard: no public API beyond `ClearPicked()`; CSS in the isolated `.razor.css` only.

## Implemented

| File | Change |
|---|---|
| `…/Assignments/TargetsAndAudienceDialog.razor` | always-rendered `.picked-targets` zone (caption + hint + chips); `OnCategoryChanged` clears first |
| `…/Assignments/TargetsAndAudienceDialog.razor.css` | scoped `.picked-targets` / `__caption` / `__hint` (1px Fluent-token border, radius, padding); `.entity-grid-chips` + `.form-select--dialog` kept |
| `…/Assignments/TargetsAndAudienceDialogModel.cs` | `public void ClearPicked() => PickedEntries.Clear();` — dedupe/Everyone methods untouched |
| `tests/…Assignments.Tests.Unit/TargetsAndAudienceDialogTests.cs` | 5 added + 1 guard test; 2 tests rewritten (keep-across-switch rule retired) |

## Review

Static diff reviewer (`reviewer`, fresh context) — **PASS**, no P1. Findings and disposition:

| # | Sev | Finding | Disposition |
|---|---|---|---|
| 1 | P2 | `TargetsAndAudienceEntry.cs` + `AssignmentAuthoring.razor` still claimed chips "survive a category switch" — falsified by `ClearPicked()`; `AssignmentGradeUiMarkupArchitectureTests` pins the `mixed-kind`/`MIXED-KIND` tokens | **Fixed** — both reworded, tokens preserved |
| 2 | P2 | the load-bearing same-category guard (`option.Value == Model.Category`) was untested; deleting it would keep the suite green | **Fixed** — `CategoryReselected_SameValue_KeepsThePickedChips` added |
| 3 | P3 | brief named the wrong test file (round's tests live in `TargetsAndAudienceDialogTests.cs`) | Recorded here |
| 4 | P3 | new CSS duplicates the `EnrollmentExceptionsDialog` frame | Accepted (repo already repeats this frame) |
| 5 | P3 | no round artifact existed for this round | This document |

## Verification (parent, authoritative)

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | succeeded — 0 errors |
| `dotnet test …Assignments.Tests.Unit` | **895 / 0 failed** |
| `dotnet test …ArchitectureTests.Unit` | **102 / 0 failed** |

## Residual risk

- The dialog can no longer produce a mixed-kind batch in one submission; the model/page API still accept one (now documented correctly).
- Border/caption legibility in light and dark themes was not UI-tested (no UI tester on a Tier-2 round).
