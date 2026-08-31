# UI Tester Report — Period Activation Guard & Atomic Period Create (r1)

> Agent: pi UI tester (ollama/minimax-m3:cloud, role `worker`). Static, code-level
> bug hunt over delivered UI work per the orchestrator's scope handover. No browser
> available; findings are sourced from read/grep inspection of the changed files
> and supporting server types.

## Scope verified

Files inspected for the defect classes:
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor.css`
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Periods.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Create.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Edit.razor`
- `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs`
- `src/Students/SchoolCollab.Students.Api/Endpoints/PeriodRoutes.cs`
- `src/Students/SchoolCollab.Students.Core/CQRS/Periods/Commands/CreatePeriod/CreatePeriod.cs`
- `src/Students/SchoolCollab.Students.Core/CQRS/Periods/Commands/CreatePeriod/CreatePeriodHandler.cs`
- `src/Students/SchoolCollab.Students.Core/CQRS/Periods/Commands/ActivatePeriod/ActivatePeriodHandler.cs`
- `src/Students/SchoolCollab.Students.Core/Domain/Exceptions/PeriodGuardException.cs`
- `src/SchoolCollab.Admin.Shared/Components/RowAction.cs`, `RowActionsMenu.razor` (shared surface used by the changed row)

## Findings by defect class

- **Swallowed errors** — None. `OnActivateAsync`, `OnCompleteAsync`, `ReloadAsync`, and `SubmitAsync` all set `_error = ex.Message` on exception. `OnInitializedAsync` in `PeriodForm.razor` catches and sets `_error`. `Periods.razor` calls `StateHasChanged()` after setting `_error` so the message bar re-renders.
- **Perpetual spinners** — None. `PeriodForm.SubmitAsync` resets `_saving` in a `finally` block guarded by `_disposed`; the Save button's `Disabled="@(_saving)"` clears. `Periods.razor` initial-load resets `_loading` in `finally`. `OnActivateAsync` has no spinner (no `_activating` flag) — it awaits the dialog, the activate API, and `ReloadAsync`, then completes; no flag can leak.
- **Missing error surfaces** — None in scope. Server 422 messages reach the user via `HttpRequestException.Message` (set by `StudentsApiClient` on non-2xx); the verbose prefix `CreatePeriod failed (422 Unprocessable Entity): {…}` is visible in the error bar and the inner `message` text is readable. The 400 path (`ArgumentException` → `Results.BadRequest`) follows the same route. No silent 5xx swallow.
- **Invisible/unhelpful validation** — None. Client validation in `SubmitAsync` and `TryBuildSubPeriods` runs synchronously before the API call and surfaces every violation in `_error` with row-specific detail (`Sub-period '<name>' end date must be on or after its start date.`, containment, overlap). Server-side 400/422 messages surface through the same bar on a round-trip violation.
- **Wrong bindings** — None. `@bind-Value="_name"` (`string`) ↔ `<FluentTextField>`; `@bind-Value="_start"` / `_end` (`DateTime?`) � `<FluentDatePicker>`. Sub-period rows use `row.Name` / `row.Start` / `row.End` — `DateTime?` for the date pickers, converted to `DateOnly` only at submit time so the row state and the wire format align (server `CreatePeriod` accepts `DateOnly`; client `CreatePeriodRequest` also uses `DateOnly`). No silent date-conversion loss.
- **DTO/property mismatches** — Field-by-field check:
  - Request: `CreatePeriodRequest(Name, StartDate, EndDate, Division, ParentPeriodId, SubPeriods)` ↔ server `CreatePeriod(Name, StartDate, EndDate, Division, ParentPeriodId, SubPeriods)`. Names match; JSON policy is `JsonSerializerDefaults.Web` (camelCase + case-insensitive) end-to-end on both client `ReadFromJsonAsync`/`PostAsJsonAsync` defaults and ASP.NET Core minimal-API defaults (no `ConfigureHttpJsonOptions` override in `src/Students/SchoolCollab.Students.Api/Program.cs`). Sub-period type names (`SubPeriodDefinitionRequest` vs `SubPeriodDefinition`) are erased over the wire; both serialize `{name, startDate, endDate}`.
  - Response: server emits `Results.Created($"/periods/{YearId}", new { id = YearId, subPeriodIds = SubPeriodIds })` → wire `{ id, subPeriodIds }` (camelCase) → client parses to `private sealed record CreatePeriodIdResponse(Guid Id, IReadOnlyList<Guid> SubPeriodIds)`. Case-insensitive match → both `Id` and `SubPeriodIds` are populated correctly. The form then calls `Api.GetPeriodByIdAsync(id)` after create, so the saved `PeriodDto` passed to `OnSaved` includes the year only — not sub-period rows — but the landing grid does its own `ListPeriodsAsync` on navigation so all rows render.
- **Silent no-ops on save/cancel/activate** — None. `CancelAsync` honors `OnCancel.HasDelegate` first, then `CancelRoute`; both `Create.razor` and `Edit.razor` pass `CancelRoute="/students/periods"`. `SubmitAsync` always invokes `OnSaved` on success; on failure `_error` is set so the user sees why nothing happened.
- **Accessibility regressions** — Disabled Activate menu item uses `RowAction.Disabled=true` which renders `disabled` on the underlying `FluentMenuItem` (kebab case) and `Disabled` on `FluentButton` (single-action case), with the explanatory label as `Title` (tooltip). Screen readers navigating the menu hear the label. Pre-existing repo pattern; this round doesn't regress it. No new focus traps or missing labels introduced.
- **Broken refresh/navigation after create/activate/guard failure** — None. On guard failure, `_error` is set and `ReloadAsync` is NOT called (the API threw, so we keep the list as-is and surface the message). On activate success, `ReloadAsync` re-fetches the list and the grid re-renders. On create success, `OnSaved` triggers `Create.razor.OnSavedAsync` which navigates to `/students/periods`; the landing page's `OnInitializedAsync` re-loads. After a failed save the form stays on the page with the error bar.

## P1 findings

None.

## P2 findings (carry-over from reviewer, plus UI-specific nits)

- [P2] `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor:108` — `ShowSubPeriodsSection` includes redundant literal-string OR checks alongside `nameof(AcademicYearDivision.Terms)`; the literals evaluate identically. Cleanest fix: remove the `|| _divisionSelect == "Terms" || _divisionSelect == "Semesters"` half. (Reviewer P2.)
- [P2] `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor:262-280` — `AutoSplitSubPeriodsAsync` produces an invalid first half when the year range is exactly 1 day (`firstEndDay = start + 0 - 1 = start - 1`); guard with `dayCount >= 2` or clamp. (Reviewer P2.)
- [P2] `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs:1332-1345` — On non-2xx, `CreatePeriodAsync` throws `HttpRequestException($"CreatePeriod failed ({(int)status} {status}): {body}", …)` where `body` is the raw JSON envelope `{"message":"..."}`. The error bar surfaces the whole `ActivatePeriod failed (422 Unprocessable Entity): {"message":"..."}` string verbatim. Functional — the user does see the actual server message — but verbose and the JSON brackets leak. Same pre-existing pattern in `UpdatePeriodAsync`, `ActivatePeriodAsync`, `CompletePeriodAsync`, etc. Out-of-round cleanup candidate (repo-wide).
- [P2] `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Periods.razor:236-239` — For a Draft year with zero Draft sub-periods, `RowAction` is `disabled: true`; the row's single-action render path produces a `FluentButton` with `Disabled=true` and `Title="Add a draft Term/Semester to activate"`. Keyboard-only users cannot focus a `disabled` HTML button, so they cannot hear the tooltip on focus. The kebab fallback only appears with 2+ actions, so for guarded Draft years the action is keyboard-inaccessible. Pre-existing repo pattern (RowActionsMenu); not a regression, but the tooltip is invisible to keyboard users on the guarded row.

## Out-of-round observations

- `Edit.razor` keeps the `SubPeriodsSection` (the read-only add/list sub-period UI for an existing year). It is unchanged by this round (FR-C6) and was not in the scope list — no findings.
- `SubPeriodsSection.razor` and `SubPeriodsListDialog.razor` are unchanged — no findings.

## Diff summary

| File | Change | UI impact |
|---|---|---|
| `PeriodForm.razor` | Sub-periods section (add/remove rows, Auto-split, client validation); division-aware visibility; blocked-parent panel for None-division parents | New create flow affordance |
| `PeriodForm.razor.css` | Styles for sub-period rows / section | New visual surface |
| `Periods.razor` | Disabled Activate + tooltip for guarded Draft years; guard message surfaces via existing `_error` bar | New disabled-state affordance |
| `StudentsApiClient.cs` | `CreatePeriodRequest.SubPeriods`, `SubPeriodDefinitionRequest`, `CreatePeriodIdResponse`; error-body capture for non-2xx | Wires atomic create round-trip |
| `PeriodRoutes.cs` | 422 mapping for `PeriodFrameworkMismatchException`/`PeriodContainmentException`/`PeriodOverlapException`; `{ id, subPeriodIds }` response shape | Server-side error surfaces |

## Verdict

TESTER_VERDICT: PASS