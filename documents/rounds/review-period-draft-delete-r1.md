# Review — period-draft-delete r1

> **Round:** r1 · **Spec:** [period-draft-delete.md](../specs/period-draft-delete.md) · **Plan:** [plan-period-draft-delete-r1.md](plan-period-draft-delete-r1.md)
> **Provider:** pi · Reviewer model: `ollama/kimi-k2.7-code:cloud` (read-only; report persisted by parent from reviewer's inline response)
> **Parent-verified numbers:** build 0 errors (6 pre-existing NuGet audit warnings); Students.Tests.Unit 398 passed 0 failed; Admin.Tests.Unit 514 passed 0 failed; Students.Tests.Integration NOT RUN — pre-existing compile errors (CS7036 `Division` ctor in `PeriodWizardOpenTermGateTests.cs`, `EnrollWithStreamEndpointTests.cs`, `StudentsApiClientEndToEndEnrollmentTests.cs`) predating this round; files untouched by worker (git-status verified).

**One-line verdict:** Implementation passes the spec; two minor P2 readability/data-quality notes, no blockers.

## Findings

| Severity | File:Line | Issue | Suggested fix |
|---|---|---|---|
| P2 | `Periods.razor:311-313` | Comment claims Draft Delete renders a "labeled, enabled FluentButton"; with 2+ actions it actually renders a kebab + FluentMenu (approved deviation). | Reword comment to "kebab menu item", keep tab-stop/keyboard note. |
| P2 | `PeriodSubPeriodsSectionDeleteTests.cs:82-83` | `SubJson` helper omits `division`; passes only because `GetKindLabel` defaults to "Term" when Division is null. | Add `"division":"Terms"` (or `"Semesters"`) to match the real DTO shape. |

No P1 / blocker findings.

## Deviation (supervisor-approved, verified implemented)

Plan §8.1/§9 assumed `RowActionsUseMenuService="false"` keeps 2-action Draft rows as labeled buttons. Fact: the shared `RowActionsMenu` renders labeled buttons only for single-action rows; 2+ actions → kebab + `FluentMenu`. Approved: Draft rows (Activate + Delete) render as kebab; 3 pre-existing grid tests minimally updated (`Periods_RowActions_NoneDivisionYear_ActivateEnabled`, `TermsYear_WithDraftSub_ActivateEnabled`, `Guard_DisablesActivateForTermsYearNoDraftSub`). Kebab satisfies FR-D9/NFR-D3/AC-D8/AC-D9.

## FR / NFR / AC traceability

| ID | Evidence files | Status |
| --- | --- | --- |
| FR-D1 | `DeletePeriod.cs`, `DeletePeriodHandler.cs`, `PeriodRoutes.cs`, `PeriodDeleteHandlerTests.cs` | covered |
| FR-D2 | `Period.cs:160-169`, `PeriodNotDeletableException.cs`, `PeriodRoutes.cs:188-190`, `PeriodDeleteHandlerTests.cs` | covered |
| FR-D3 | `DeletePeriodHandler.cs:37-51`, `PeriodRepository.cs` (cascade), `PeriodDeleteHandlerTests.cs`, `PeriodDeleteEndpointTests.cs` | covered |
| FR-D4 | `DeletePeriodHandler.cs` (no parent guard), `PeriodDeleteHandlerTests.cs` | covered |
| FR-D5 | `DeletePeriodHandler.cs:28-31` (tenant filter via `GetAsync`), `PeriodDeleteHandlerTests.cs`, `PeriodDeleteEndpointTests.cs` | covered |
| FR-D6 | `Period.cs:171-179`, `IPeriodRepository.cs`, `PeriodRepository.cs:119-123`, `DeletePeriodHandler.cs:53-58`, `PeriodDeleteHandlerTests.cs` | covered |
| FR-D7 | `DomainEvents.cs`, `Period.cs:167`, `PeriodDeleteHandlerTests.cs` | covered |
| FR-D8 | `PeriodRoutes.cs:178-196`, `PeriodDeleteEndpointTests.cs` | covered |
| FR-D9 | `Periods.razor:290-314`, `PeriodDeletePrompts.cs`, `PeriodsLandingGridTests.cs` | covered |
| FR-D10 | `Edit.razor:74-86`, `Edit.razor.css:19-37`, `PeriodDeletePrompts.cs`, `PeriodEditPageTests.cs` | covered |
| FR-D11 | `StudentsApiClient.cs:1386-1398` | covered |
| FR-D12 | `SubPeriodsSection.razor:63-72,133-156`, `PeriodDeletePrompts.cs`, `PeriodSubPeriodsSectionDeleteTests.cs` | covered |
| NFR-D1 | `DeletePeriodHandler.cs` (single DeleteAsync → one SaveChanges), `RepositoryBase.cs:42-46`, both test sets | covered |
| NFR-D2 | `DeletePeriodHandler.cs:60-66`, `PeriodRoutes.cs:191-195` (ConcurrencyException → 404) | covered |
| NFR-D3 | `Periods.razor:290-314` (kebab Delete reachable/announceable), `PeriodsLandingGridTests.cs:468-481` | covered |
| AC-D1..D10 | `PeriodDeleteHandlerTests.cs` (45-179), `PeriodDeleteEndpointTests.cs` (51-147), `PeriodsLandingGridTests.cs` (468-499), `PeriodSubPeriodsSectionDeleteTests.cs` (87-114) | all covered |

## Out-of-scope guardrails

| Guardrail | Status |
| --- | --- |
| `SubPeriodsListDialog.razor` (+`.css`) untouched | pass |
| No soft delete / recycle bin | pass |
| No bulk delete | pass |
| No feature flag | pass |
| No integration/outbox event | pass |
| No migrations | pass |
| No per-row sub-period removal | pass |
| No 409 on delete route | pass |

## Best-practices check

Repo rules honored: handler flow ordering (404 → domain guard → sub-period guard → FR-D6 housekeeping → single Remove) explicit and matching plan; `PeriodDeletePrompts` centralizes confirmation wording (grid, edit danger zone, sub-period rows); CSS isolation respected (Edit.razor.css only); no nested dialogs; naming parity with Activate/Complete handlers; no dead code or over-abstraction found.

**P2-1 / P2-2 disposition:** fixed by parent post-review (surgical edits; see acceptance doc); rebuild + affected tests re-run from parent.