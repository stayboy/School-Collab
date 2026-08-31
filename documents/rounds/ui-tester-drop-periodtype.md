# UI-tester report: drop PeriodType / adopt AcademicYearDivision

- **Round:** `drop-periodtype`
- **Plan:** `documents/specs/plan-drop-periodtype.md`
- **Scope handover source:** `documents/specs/acceptance-drop-periodtype.md` (§8)
- **Out-of-round observations:** none triggered for this pass; report stays within the handover.

## Verdict
**PASS with P2 findings.** No P1 found.

## P1 findings (blockers)
None.

## P2 findings (advisory)

### P2-1 — Group period filter does not restrict to sub-periods of the active year
- **File:** `src/Students/SchoolCollab.Students.Application/Components/Students/TopicCreateDialog.razor` (lines ~344-348, `FilterPeriodsForGroup`).
- **Status:** ✅ **Fixed** in follow-up edit. The filter now requires `p.ParentPeriodId == _activeYearId.Value` when `_activeYearId` is known; falls back to any matching sub-period only in degraded state (no active year resolved).
- **Verification:** `TopicCreateDialogTests` pass; `Admin.Tests.Unit` 502/0.

### P2-2 — Sub-period grid still shows Activate for Completed/Archived rows
- **File:** `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriods.razor` (lines ~136-141, `BuildRowActions`).
- **Status:** ✅ **Fixed** in follow-up edit. `Activate` is now disabled unless `row.Status == "Draft"`, matching `SubPeriodsListDialog.razor`.
- **Verification:** `Admin.Tests.Unit` 502/0.

### P2-3 — Sub-period kind label guard relies on null fallback (defensive fallback could mis-label)
- **Files:**
  - `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Periods.razor` line ~138 (`GetKindLabel`).
  - `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriods.razor` line ~110 (`GetKindLabel`).
  - `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriodsListDialog.razor` line ~219 (`GetKindLabel`).
  - `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriodsSection.razor` line ~278 (`GetKindLabel`).
- **Evidence:** `GetKindLabel` compares `Division` against `"Semesters"` and renders `"Term"` for anything else, including an unexpected `null`/`"Terms"` mismatch path. Under the new domain rule (sub-period `Division` is never `None`) the bug surface is narrow, but a top-level year listed in the Sub-periods page (data error: parent mismatch) would be displayed as a "Term" silently. Severity is P2 because the label is purely informational and backend constraints prevent it reaching here in normal flow.
- **Fix sketch:** assert `period.ParentPeriodId is not null && period.Division is not null`; render an "—" or visible warning badge otherwise.

### P2-4 — Sub-period edit row's `_typeText` is set but unused when division is known
- **File:** `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriodsSection.razor` line ~245 (`StartEdit`).
- **Evidence:** `StartEdit` always sets `_typeText = p.Division == "Semesters" ? "Semester" : "Term";` even when the inline type selector is hidden (when `_division is not null`). The selector is not re-shown during edit, so the value is unused until/unless the division becomes unknown. Cosmetic.
- **Fix:** drop the assignment when `_division is not null`, or use it to drive the type selector conditionally.

### P2-5 — Create-from-`?parent=` against a None-division year lands the user in a stuck form
- **File:** `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor` lines ~120-130.
- **Evidence:** when the parent academy's division is `None`, the form sets `_error = "...sub-periods are not allowed..."` and locks Division/parent; the user has no way to recover except pressing Cancel (no "Back to periods" inline affordance). The error surfaces in the bottom bar; the Division select is disabled. Functionally correct (server would reject anyway) but a UX dead-end.
- **Fix sketch:** detect the None case early and render an inline "this year does not allow sub-periods" message with a Cancel-to-periods link, instead of the form. Or warn the user before prefill.

### P2-6 — `PrefillAcademicYear` skipped silently when sub-period-`?parent=` errored (intentional but worth noting)
- **File:** `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor` line ~132.
- **Evidence:** the `PrefillAcademicYear` branch is gated on `string.IsNullOrEmpty(_error)`, which is now always true when `?parent=` was used with a None-division year (P2-5). The user lands on an error with empty Name/Start/End fields and no "this year cannot host sub-periods" affordance; this compounds P2-5.
- **Fix:** fold into the P2-5 fix.

### P2-7 — Inactive (Completed/Archived) rows in row actions surface incomplete state-mirroring
- **Files:** `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriodsListDialog.razor` lines ~79-95 (Activate vs Complete render switch is correct; flagged here for symmetry with P2-2 because the landing grid (`Periods.razor:198-204`) correctly omits actions for non-Draft/non-Active rows).
- **Evidence:** surface drift between the dialog and the grid; mild inconsistency.
- **Fix:** apply P2-2's guard consistently.

### P2-8 — `_activePeriodKind` for top-level year with Division=Terms gets mapped to "Term" in some paths
- **File:** `src/Students/SchoolCollab.Students.Application/Components/Students/JoinGroupsDialog.razor` line ~163 (`KindOf`).
- **Evidence:** for a sub-period (`ParentPeriodId is null` is false), division `"Semesters"` → `"Semester"`; everything else (including `"Terms"`) → `"Term"`. Under the new model, only sub-periods and top-level years with `Division != None` reach this function via `activeSub` or `activeYear`. The fallback is intentional ("Term" is the safest default), but a `null` division on a sub-period would silently be classified as a Term — never reachable in the new model because `Period` invariant forbids `None` for sub-periods, so a true `null` would only arise from a DTO degradation (should not happen). P2/defensive only.

## Out-of-round observations
None — every finding is on a UI surface named in §8 of the acceptance doc (components, host pages, ApiClient, navigation).

## Files examined
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Periods.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Edit.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriods.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriodsListDialog.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriodsSection.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Students/JoinGroupsDialog.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Students/TopicCreateDialog.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Students/TopicAssignmentPeriodEditDialog.razor`
- `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs` (DTO + request record wiring)
- `src/Students/SchoolCollab.Students.Core/CQRS/TopicAssignments/TopicAssignmentPeriodValidator.cs` (server validation cross-check for P2-1)
