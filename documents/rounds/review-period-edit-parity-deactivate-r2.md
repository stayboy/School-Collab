# Review — period-edit-parity-deactivate r2

> **Reviewer:** kimi-k2.7-code (review-only, no shell) · **Date:** 2026-09-01
> **Parent-verified evidence:** build 0 errors · Students.Tests.Unit PASSED · Admin.Tests.Unit PASSED · Students.Tests.Integration 1 failed (`WithExplicitEffectiveDate_FiltersToThatDate`, pre-existing grade-topic, unrelated)
> **Verdict:** **pass-with-fixes** — P1 0 · P2 3 · P3 3 · all five worker deviations ACCEPTED · **r1 conformance re-check: no regressions**

## Spec conformance (FR/AC, file:line evidence)

| Req | Evidence | Verdict |
|---|---|---|
| FR-E1 — Update drops Division | `UpdatePeriod.cs:10`; `Period.cs:88-98` | ✅ |
| FR-E2 — Division disabled on edit | `Edit.razor:76` `DivisionLocked="true"` | ✅ |
| FR-E3 — Edit mirrors create field set | `Edit.razor:70-80` | ✅ |
| FR-E4 — Auto-split visible on edit | `PeriodSubPeriodsEditor.razor:35`; `Edit.razor:71` | ✅ |
| FR-E5 — Auto-split gating + confirm naming count + tooltip | `PeriodSubPeriodsEditor.razor:131-139/153-159/225-240` | ✅ |
| FR-E6 — Suggest/Backfill create-only | `Create.razor:61-68`; no child content on edit | ✅ |
| FR-E7 — Shared editor; PeriodForm gone | `PeriodSubPeriodsEditor.razor`; pages own `<EditForm>` | ✅ |
| FR-X1 — Deactivated; Active-only | `PeriodStatus.cs:9`; `Period.cs:112-124` | ✅ |
| FR-X2 — Cascade, single save | `DeactivatePeriodHandler.cs:43-50` | ✅ |
| FR-X3 — Overlap excludes only Deactivated | `PeriodRepository.cs:74` | ✅ |
| FR-X4 — Draft-only delete intact | `Period.cs:127-134` | ✅ |
| FR-X5 — Deactivated→Archived + grid Archive | `Period.cs:100-106`; `PeriodRoutes.cs:180-203`; `Periods.razor:385-389` | ✅ |
| FR-X6 — PeriodDeactivatedEvent | `DomainEvents.cs:17`; `Period.cs:119` | ✅ |
| FR-X7 — 204/404/422, no 409 | `PeriodRoutes.cs:148-172` | ✅ |
| FR-X8 — Client DeactivatePeriodAsync | `StudentsApiClient.cs:1400-1415` | ✅ |
| FR-X9 — Grid/edit Deactivate; Deactivated row only Archive | `Periods.razor:375-389`; `Edit.razor:84-93/237-262` | ✅ |
| FR-X10 — Tenant scoping | `DeactivatePeriodHandler.cs:35`; AC-E9 test | ✅ |
| NFR-E2 — Concurrency→404 | `PeriodRepository.cs:23-31`; `PeriodRoutes.cs:167` | ✅ |
| AC-E1..E10 | Convered except AC-E7 (impl correct, test missing — P2-1) | ⚠️/✅ |

## r1 conformance re-check (§10)

All FR-D1..D12 / NFR-D1..D3 / AC-D1..D10 hold. `DeletePeriodHandler` semantics unchanged; `PeriodDeleteEndpointTests` covers 204/404/422, other-tenant 404, DB cascade, cache invalidation; per-row Draft delete moved into `PeriodSubPeriodsEditor.razor:285-306` sharing `PeriodDeletePrompts.SubPeriodMessage` (FR-D12). **No r1 regression.**

## Deviation adjudication

| Deviation | Verdict | Why |
|---|---|---|
| D1 — kept parent-consistency guards | Accept | Command no longer carries Division; guards only prevent identity flips/containment violations |
| D2 — removed `GetNonCompletedSubPeriodCountAsync` | Accept | Zero remaining references |
| D3 — integration runtime-bug fixes | Accept | Mechanical corrections; no production assertions weakened |
| D4 — re-homed wrapper test coverage | Accept with note | AC-E2/E3 covered in `PeriodFormParityTests`; blocked-parent panel lacks a test (P2-2) |
| D5 — added `POST /archive` + client | Accept | Required by FR-X5/§8.3; 204/404 mapping, idempotent archive, grid wired to Deactivated rows only |

## Findings

**P1:** none.

**P2 (must fix before acceptance):**
1. Missing `PeriodsLandingGridTests` coverage: Deactivated rows show Archive, no Delete/Activate/Complete/Deactivate (plan §9.1 asked for this; AC-E7).
2. Missing bUnit test for the create-page blocked-parent panel (`Create.razor:54-62`, plan §12 Risk 0 / §8.0).
3. `Edit.razor:147-165`: a 404 (null `_period`) still clears `_loadError` and renders an editable empty form (`ShowSubPeriodsEditor` becomes true since `_division` is null); should surface a page-level error instead.

**P3 (nits):** stale doc comment `PeriodFormFields.razor:6-8` (references deleted wrapper); `ArchivePeriod.cs:8` "no HTTP route exists yet" now false; `PeriodEditPageTests.cs:185` comment references retired `SubPeriodsSection`.

```json
{ "verdict": "pass-with-fixes", "p1": 0, "p2": 3, "notes": "r1 delete coverage is preserved with no regressions after the PeriodForm/SubPeriodsSection unification. The implementation conforms to the r2 spec; the P2 items are missing tests and a 404 UX gap, not data-integrity blockers." }
```