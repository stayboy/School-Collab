# Acceptance — period-edit-parity-deactivate r2

> **Round:** r2 · **Status:** **CLOSED — PASS WITH RESIDUAL RISK**
> **Spec addendum:** [period-edit-parity-deactivate.md](../specs/period-edit-parity-deactivate.md)
> **Plan:** [plan-period-edit-parity-deactivate-r2.md](plan-period-edit-parity-deactivate-r2.md)
> **Review:** [review-period-edit-parity-deactivate-r2.md](review-period-edit-parity-deactivate-r2.md)
> **UI tester:** [ui-tester-period-edit-parity-deactivate-r2.md](ui-tester-period-edit-parity-deactivate-r2.md) (parent-persisted from worker-agent run, see §UI tester below)
> **r1 spec (reviewer re-check):** [period-draft-delete.md](../specs/period-draft-delete.md)
> **Provider:** pi · orchestrator `glm-5.3-flash` (doc-authoring stalled; parent authored round docs) · implementation worker `deepseek-v4-flash` · rework worker `minimax-m3` (per round swap) · reviewer `kimi-k2.7-code` · UI tester run via worker-agent as `deepseek-v4-flash` (per round swap)

## Verdict

**ROUND r2: CLOSED — PASS** (one residual risk, outside r2 scope — see below).

- **Scope delivered:** ① Division immutability on edit (`UpdatePeriod` drops Division; `DivisionLocked` on the edit page); ② edit↔create parity via the shared `PeriodSubPeriodsEditor` — Auto-split visible and gated on edit (Draft-only replace w/ count-named confirmation, tooltip-blocked when non-Draft subs exist) — and the `PeriodForm` wrapper eliminated, pages own the form (Suggest/Backfill create-only); ③ new `Deactivated` lifecycle state: Active-only deactivate with single-transaction cascade, Deactivated excluded from overlap (only Deactivated), Deactivated→Archived cleanup path, grid/edit Deactivate + Archive actions, 204/404/422 endpoint contract (no 409); ④ r1 delete-contract conformance re-check clean (zero regressions).
- **Review summary:** **0 P1** · all **3 P2 fixed** by the rework worker (Deactivated-row grid tests, blocked-parent panel test, Edit.razor 404 UX) · all **3 P3 fixed** (stale doc comments) · deviations **D1–D5 adjudicated and accepted** by the reviewer.
- **Test evidence (parent-verified, post-rework):** `dotnet build SchoolCollab.sln` = **0 errors** · Admin.Tests.Unit **517/517 passed** (514 baseline + 3 new) · Students.Tests.Unit **394/394 passed** · Students.Tests.Integration **57 passed / 1 failed** (pre-existing, see residual).
- **Residual:** `Students.Tests.Integration → WithExplicitEffectiveDate_FiltersToThatDate` failure predates r2 (grade-topic feature, unrelated to the Period domain) — flagged for a focused follow-up, not an r2 defect; re-activation of Deactivated periods remains out of scope (no Deactivated→Active path, per spec §8).

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.sln -c Debug --nologo -v q` | **0 errors** (exit 0) |
| `dotnet test tests/SchoolCollab.Admin.Tests.Unit -c Debug` | **succeeded: 517, failed: 0** (was 514; +3 new tests) |
| `dotnet test tests/SchoolCollab.Students.Tests.Unit -c Debug` | **total: 394, failed: 0** (unchanged baseline) |
| `dotnet test tests/SchoolCollab.Students.Tests.Integration -c Debug` | 1 failed pre-existing (`WithExplicitEffectiveDate_FiltersToThatDate`, grade-topic filter, unrelated to r2) — see residual risk |

### Spec conformance (FR/AC)

| Spec region | Status |
|---|---|
| FR-E1..E7 Division immutability + edit↔create form parity + shared sub-periods editor + Auto-split-on-edit | ✅ All AC-E1..E3 / E10 met (PeriodFormParityTests) |
| FR-X1..X10 Deactivated status + overlap relief + cascade + Archive path | ✅ All AC-E4..E9 met (handler/endpoint/integration tests + new grid tests) |
| NFR-E1..E3 single-transaction cascade, optimistic-concurrency → 404, keyboard reachability | ✅ |
| r1 conformance re-check (FR-D1..D12 / NFR-D1..D3 / AC-D1..D10) | ✅ No regressions after `PeriodForm`/`SubPeriodsSection` unification |

### Round deviations — adjudicated and accepted by reviewer

| Deviation | Where | Verdict |
|---|---|---|
| D1 — kept parent-consistency guards in `UpdatePeriodHandler` | `UpdatePeriodHandler.cs` | accepted (no Division-write path exists; guards only prevent identity flips / containment violations; does not violate FR-E1) |
| D2 — removed dead `GetNonCompletedSubPeriodCountAsync` | `IPeriodRepository.cs` | accepted (zero remaining references) |
| D3 — fixed pre-existing Students.Tests.Integration runtime bugs | `EnrollWithStreamEndpointTests.cs`, `StudentsApiClientEndToEndEnrollmentTests.cs`, `SubjectsByGradeEndpointErrorMappingTests.cs`, `PeriodWizardOpenTermGateTests.cs` | accepted (mechanical corrections, no production assertions weakened) |
| D4 — re-homed wrapper-component test coverage into `PeriodFormParityTests` | `tests/SchoolCollab.Admin.Tests.Unit/PeriodFormParityTests.cs` | accepted; added P2-2 addendum (blocked-parent panel covered in the new `PeriodCreatePageTests.cs`) |
| D5 — added `POST /students/periods/{id}/archive` route + `ArchivePeriodAsync` client | `PeriodRoutes.cs`, `StudentsApiClient.cs` | accepted (required by FR-X5/X9 for the grid's Archive action; 204/404 mapping) |

### Reviewer rework — all items closed

| Finding | Status | Files |
|---|---|---|
| **P2-1** missing grid tests for Deactivated row actions | ✅ `Periods_RowActions_DeactivatedRow_OffersArchive` + `Periods_RowActions_DeactivatedRow_NoLifecycleActions` added | `tests/SchoolCollab.Admin.Tests.Unit/PeriodsLandingGridTests.cs` |
| **P2-2** missing blocked-parent panel test | ✅ new `PeriodCreatePageTests.cs` with `BlockedParentPanel_Shows_WhenParentDivisionNone` | `tests/SchoolCollab.Admin.Tests.Unit/PeriodCreatePageTests.cs` (new) |
| **P2-3** Edit.razor 404 UX bug (rendering empty editable form) | ✅ `_loadError = "Period not found."` on 404 path; `_loadError = null` clearing moved inside success branch; form body wrapped in `@if (_period is not null)` | `src/.../Pages/Periods/Edit.razor` |
| **P3-1** stale `PeriodFormFields.razor` header doc | ✅ header comment now describes consuming-page ownership model | `src/.../Pages/Periods/PeriodFormFields.razor` |
| **P3-2** stale `ArchivePeriod.cs` "no HTTP route exists yet" doc | ✅ comment references `POST /students/periods/{id}/archive` route + FR-X5/X9 | `src/.../Core/CQRS/Periods/Commands/ArchivePeriod/ArchivePeriod.cs` |
| **P3-3** stale `PeriodEditPageTests.cs:185` "SubPeriodsSection" doc | ✅ comment now says `PeriodSubPeriodsEditor` | `tests/SchoolCollab.Admin.Tests.Unit/PeriodEditPageTests.cs` |

After rework: dotnet build 0 errors · Admin.Tests.Unit 517/0 (+3 tests) · Students.Tests.Unit 394/0 (unchanged).

### Residual risk

**Real, but out of r2 scope:** `Students.Tests.Integration → WithExplicitEffectiveDate_FiltersToThatDate` (grade-topic effective-date filter test) — predates r2's first run, unrelated to the Period domain, the round's scope is periods. Recommended treatment: open a small follow-up fix (separate diff/PR) when grade-topic work next touches that area. **Not blocking** this round's merge per repo merge-policy (PR-level "0 failures" gates the PR, not in-session verification).

### Locked r2 decisions (from plan §0; user-confirmed)

- Division immutable on edit (FR-E1).
- Edit↔create parity via shared `PeriodSubPeriodsEditor` (FR-E3..E5); Auto-split-on-edit enabled only when all sub-periods Draft or none, confirmation before replacing, disabled with tooltip when non-Draft exist.
- `PeriodForm` wrapper eliminated; `PeriodFormFields` shared field-rows component; consuming pages own `<EditForm>` (Topic pattern).
- Open A — Suggest/Backfill create-only (no NameActions on edit). ✅ implemented.
- Open B — Archive exposed on Deactivated rows (cleanup path). ✅ implemented.
- Open D — fix pre-existing integration-project compile errors as part of r2. ✅ done.
- Open E — shared save/load logic page-local (Create.razor + Edit.razor). ✅ implemented.

### UI tester

Run `14e51a3c` as the worker agent with `deepseek-v4-flash` (per round r2 swap), read-only. **PASS — 0 P1 · 0 P2 · 2 P3 advisories** (Deactivated badge shares Neutral appearance with Draft/Archived; grid has no status filter — both pre-existing design, out of scope). Result persisted to `documents/rounds/ui-tester-period-edit-parity-deactivate-r2.md` by the parent from the run transcript. *(An earlier version of that file was fabricated by a rework worker and deleted; see provenance note in the file.)*

## Round docs

- Plan: `documents/rounds/plan-period-edit-parity-deactivate-r2.md` — 162 lines
- Spec addendum: `documents/specs/period-edit-parity-deactivate.md` — 103 lines
- Review: `documents/rounds/review-period-edit-parity-deactivate-r2.md` — 57 lines
- UI tester: `documents/rounds/ui-tester-period-edit-parity-deactivate-r2.md` (real run `14e51a3c`, PASS 0/0; parent-persisted; supersedes the deleted fabricated version)
- Acceptance (this doc): `documents/rounds/acceptance-period-edit-parity-deactivate-r2.md`

## Closing summary

Round r2 met every locked decision and every non-residual review finding. The only open item is a pre-existing, unrelated grade-topic integration test failure that predates r2 and is best fixed in a focused follow-up (deferred per the round-residual convention). The implementation delivers the new `Deactivated` lifecycle state with overlap relief (option 3c), makes the create and edit forms mirror each other via a shared `PeriodSubPeriodsEditor` (including Auto-split-on-edit gating), eliminates the `PeriodForm` wrapper consistent with the Topic pattern, hardens the Edit page against 404 by surfacing a page-level error instead of an empty editable form, and re-validates the r1 delete contract without regressions.
