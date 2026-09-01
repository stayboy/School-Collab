# Implementation Plan — Edit/Create Form Parity + Period Deactivation (round r2)

> **Round:** r2 · **Status:** PLAN (not yet implemented) · **Implementer:** TBD (solo or 4-agent — user to choose)
> **Builds on:** closed round r1 (`plan-period-draft-delete-r1.md` — Draft-period delete shipped)
> **Mandatory pre-reads (worker):** `AGENTS.md`, `.github/copilot/rules/dotnet-best-practices.md` (+ backing skill). Honored skills: `dialog-ui`, `blazor-css-isolation`, `fluentui-icons`, `author-component`, `collect-user-input`.
> **Build rule:** `dotnet build SchoolCollab.sln` after **every** code change; fix errors before continuing. `dotnet test` must NOT be passed `--nologo`.

## 0. User decisions locked (2026-08-31)

1. **Division immutable on edit.** `AcademicYearDivision` is set at create time and cannot be changed on an existing period. The Division field stays visible on the edit form (same look as create) but is disabled.
2. **Edit ↔ create form parity, autosplit visible on edit too.** The edit form must render the same field set + sub-periods section as the create form, including the **Auto-split** button. (Today the edit page renders a *separate* `SubPeriodsSection` above the form and hides the in-form sub-periods/autosplit section — that split is removed.)
3. **Overlap: option 3c — new `Deactivated` state.** A new `PeriodStatus.Deactivated` is added. **Only Active periods can be deactivated.** Deactivated periods are **excluded from the no-overlap check**, so a corrected new period can be created in the freed range. Deactivated periods are **not** deletable (still non-Draft). Re-activation is **out of scope** this round.
4. **Review r1 against the initial spec.** The r2 reviewer re-verifies the r1 delete implementation against `documents/specs/period-draft-delete.md` (FR-D1..D12 / NFR-D1..D3 / AC-D1..D10) in addition to reviewing the r2 changes.

## 1. Verified code facts (planner checked 2026-08-31)

| Fact | Evidence |
| --- | --- |
| `PeriodStatus` enum: `Draft=0, Active=1, Completed=2, Archived=3` | `Domain/PeriodStatus.cs` |
| `Period.Update(name, start, end, division, parentPeriodId?)` sets `Division` | `Domain/Period.cs` |
| `UpdatePeriod` command carries `AcademicYearDivision Division`; handler has a year↔sub flip guard + parent-division-match + containment checks | `CQRS/Periods/Commands/UpdatePeriod/{UpdatePeriod.cs, UpdatePeriodHandler.cs}` |
| `GetOverlappingPeriodsAsync` does NOT filter by status — blocks vs all statuses | `Data/Repositories/PeriodRepository.cs:52-65` |
| `ActivatePeriodHandler` precedent: auto-closes prior Active year + cascade-completes its Active sub-periods; `ICommandHandler`; `HybridCache.RemoveByTagAsync("students")`; integration event published | `CQRS/Periods/Commands/ActivatePeriod/ActivatePeriodHandler.cs` |
| Named domain exceptions one-per-file in `Domain/Exceptions/`; `PeriodNotDeletableException` is the 422-mapped precedent; `PeriodGuardException`/`PeriodNotOpenException`/`PeriodOverlapException` exist | `Domain/Exceptions/` |
| `PeriodFormFields` is the shared field component (Division/Parent/Name/Dates) used by `PeriodForm` for both create and edit; `DivisionLocked` param already exists (currently `_isSubPeriod`) | `PeriodFormFields.razor`, `PeriodForm.razor:53` |
| `PeriodForm.ShowSubPeriodsSection = !PeriodId.HasValue && !_isSubPeriod && !ShowBlockedPanel` → autosplit hidden on edit | `PeriodForm.razor:224-225` |
| `Edit.razor` renders a separate `SubPeriodsSection` above `PeriodForm`; `ShowSubPeriodsSection` (page) = year & division != None | `Edit.razor:50-65` |
| `SubPeriodsSection` is an inline add/edit/delete list for existing sub-periods (no autosplit); delete affordance added in r1 | `SubPeriodsSection.razor` |
| `PeriodFormFields` renders `<NameActions>` whenever non-null; `PeriodForm` always passes Suggest/Backfill → they show on edit too | `PeriodFormFields.razor:68`, `PeriodForm.razor:55-64` |
| `PeriodConfiguration` maps `Status` with `HasDefaultValue(PeriodStatus.Draft)`; enum stored as int → adding a value needs **no migration** | `Data/Configurations/PeriodConfiguration.cs:34` |
| Client sends enums as **integer** (System.Text.Json has no `JsonStringEnumConverter`); `PeriodDto.Status` is a string mapped in the API projection | repo memory + `Contracts` |

## 2. Spec addendum to author first (durable, in `documents/specs/`)

Write `documents/specs/period-edit-parity-deactivate.md` capturing decisions 1–3 as the source of truth (FR-E1..En for edit-parity, FR-X1..Xn for deactivate). This round plan references it. (Per repo "Finding → Implementation" standard — the spec is the durable memory; this plan is the ephemeral round doc.)

## 3. Domain changes (`SchoolCollab.Students.Core/Domain`)

1. **`PeriodStatus.cs`** — add `Deactivated = 4`.
2. **`Period.cs`**:
   - `Update(...)`: drop the `AcademicYearDivision division` parameter (Division immutable). Keep `name`, `startDate`, `endDate`, `parentPeriodId?`. Do **not** mutate `Division`. Update the doc comment.
   - New `Deactivate()`: `if (Status != PeriodStatus.Active) throw new PeriodNotDeactivatableException(...)`; set `Status = Deactivated`; `UpdatedAt = now`; add `PeriodDeactivatedEvent(Id, Name)`. No idempotent early-return (mirrors `Delete` semantics — deactivating an already-Deactivated period is a 422, not a no-op).
   - `Archive()`: allow `Deactivated → Archived` (terminal record-keeping). Keep `Active/Completed → Archived` paths. Update guard: `if (Status == PeriodStatus.Archived) return;` stays; add comment that Deactivated may also be archived.
   - `Delete()`: unchanged (Draft-only). Deactivated periods are NOT deletable — the existing `Status != Draft` guard already rejects Deactivated.
3. **`Domain/Events/DomainEvents.cs`** — add `record PeriodDeactivatedEvent(Guid Id, string Name)` (parity with `PeriodCompletedEvent`).
4. **`Domain/Exceptions/PeriodNotDeactivatableException.cs`** — new file, mirrors `PeriodNotDeletableException.cs` (named exception → 422 in routes).

## 4. CQRS (`SchoolCollab.Students.Core/CQRS/Periods/Commands`)

1. **`UpdatePeriod`** — remove `AcademicYearDivision Division` from the command record. `UpdatePeriodHandler`:
   - Drop the division parameter from the `period.Update(...)` call.
   - Remove the now-unreachable year↔sub flip guard (division can no longer flip). Keep the sub-period parent-match + containment checks (still relevant: a sub-period's parent must be a top-level year; containment within parent range). The `parent.Division != command.Division` check becomes `parent.Division != period.Division` (load parent, assert the sub-period's immutable division still matches its parent — defense against stale client input).
   - Overlap check unchanged (still rejects overlap vs all non-Deactivated periods).
2. **`DeactivatePeriod` (new)** — `sealed record DeactivatePeriod(Guid Id) : ICommand`. `DeactivatePeriodHandler : ICommandHandler<DeactivatePeriod>`:
   - `GetAsync` → `PeriodNotFoundException` (404).
   - `period.Deactivate()` (Active-only guard → `PeriodNotDeactivatableException` → 422).
   - **Cascade:** if the period is a top-level year, load its Active sub-periods (`GetActiveSubPeriodsAsync`) and `Deactivate()` each (so an Active year doesn't leave Active orphans under a Deactivated parent). Guard first, then mutate all, then a single `SaveChanges` (atomic — NFR parity with r1).
   - `DbUpdateConcurrencyException` → `ConcurrencyException` (→ 404, never 409).
   - `cache.RemoveByTagAsync("students")`.
   - **No integration event** this round (domain event only, parity with `DeletePeriodHandler`). Flag as open if observability needs the integration event.

## 5. Repository (`SchoolCollab.Students.Core/Data/Repositories`)

- **`PeriodRepository.GetOverlappingPeriodsAsync`** — add `&& p.Status != PeriodStatus.Deactivated` to the query. **Only Deactivated is excluded** (Archived/Completed still block — decision 3). Used by both `CreatePeriodHandler` and `UpdatePeriodHandler`, so creating a corrected period in a range freed by deactivation now succeeds.
- No new repo method needed for deactivate (uses existing `GetAsync` + `GetActiveSubPeriodsAsync` + `DeleteAsync`'s concurrency wrapper pattern).

## 6. API (`SchoolCollab.Students.Api/Endpoints/PeriodRoutes.cs`)

1. **`POST /students/periods/{id}/deactivate`** (grouped via `MapPeriodRoutes`): returns `204 NoContent` on success; `404` on `PeriodNotFoundException`/`ConcurrencyException`; `422 Results.Json(new { ex.Message }, statusCode: 422)` on `PeriodNotDeactivatableException`. Never 409.
2. **Update route** — adjust the `MapPut("/periods/{id:guid}")` binding to the updated `UpdatePeriod` command (no Division in the request DTO). Keep existing 422/404/204 mapping.

## 7. Client (`StudentsApiClient.cs`)

- `DeactivatePeriodAsync(Guid id, CancellationToken)` — mirrors `ActivatePeriodAsync`/`DeletePeriodAsync` error surfacing (422 body → `HttpRequestException` with `StatusCode`).
- `UpdatePeriodAsync` (or the existing update wrapper) — stop sending `Division` in the request body (drop the enum integer). Keep sending `ParentPeriodId` where relevant.

## 8. UI (`SchoolCollab.Students.Application/Components/Pages/Periods`)

### 8.0 Eliminate `PeriodForm` wrapper — follow the Topic pattern (user directive 2026-08-31)

Verified pattern: `TopicFormFields.razor` is a **single** field-rows component; the **owning `<EditForm>` is supplied by the consuming dialog/page** — there is no `TopicForm.razor` wrapper. A prior round deviated by creating a `PeriodForm.razor` wrapper **plus** a `PeriodFormFields.razor` child. This round removes the wrapper so Period matches Topic.

- **Delete `PeriodForm.razor`** (and fold `PeriodForm.razor.css` styles into the consuming pages' scoped CSS — `Create.razor.css` / `Edit.razor.css` — or a shared form CSS; no inline `<style>`).
- **`PeriodFormFields.razor` stays** as the shared field-rows component (Division → Parent → Name → Dates + `<NameActions>` slot). It already exposes `IPeriodFormModel`, `IsSubPeriod`, `DivisionLocked`, `AcademicYears`, `DivisionChanged`, `NameActions` — these remain.
- **Owning-form logic moves into the consuming pages** (`Create.razor`, `Edit.razor`): each page supplies its own `<EditForm>` + `<DataAnnotationsValidator />` + submit/cancel row + error bar + save/load logic. `PeriodFormModel` (the `IPeriodFormModel` impl) and the load/save/autosplit helpers currently in `PeriodForm` move to the pages (or a small shared `PeriodFormService` if duplication is excessive — prefer page-local first to match Topic).
- **Blocked-parent panel** (create-only, None-division parent → dead-end form) moves into `Create.razor` only.
- **Suggest/Backfill `<NameActions>`** are create-only: `Create.razor` passes them into `PeriodFormFields`; `Edit.razor` passes `null` (so no Suggest/Backfill on edit — resolves **open question A** to "hide on edit").
- **Sub-periods + Auto-split section** (the create-only in-form section that was inside `PeriodForm`) becomes its **own shared component** — see §8.2 — consumed by both pages, NOT nested in a deleted wrapper.
- Update `Create.razor` / `Edit.razor` references from `<PeriodForm …/>` to the new page-owned form markup + `<PeriodFormFields …/>` + the shared sub-periods component. No other consumers of `PeriodForm` exist (verified: only `Create.razor:27` and `Edit.razor:64` reference it).
- bUnit tests that hosted `PeriodForm` directly must be re-homed against `Create.razor`/`Edit.razor` (or the new shared sub-periods component).

### 8.1 Division immutable on edit
- `PeriodFormFields` is rendered by both pages; the page passes `DivisionLocked="@(_isSubPeriod || isEdit)"` where `isEdit` is true on `Edit.razor` (the period being edited already exists). Field stays visible (same look as create) but disabled when editing.

### 8.2 Edit ↔ create form parity + autosplit on edit
- **New shared sub-periods component** (e.g. `PeriodSubPeriodsEditor.razor` — replaces both the deleted in-form section of `PeriodForm` and the retired `SubPeriodsSection.razor`). Consumed by `Create.razor` and `Edit.razor` for any top-level Terms/Semesters year (not a sub-period, not a blocked parent). It renders the sub-period list + the **Auto-split** button + add/edit/delete affordances (folding in r1's per-row Draft delete + `SubPeriodsSection`'s inline add/edit).
  - **Create mode** (page is creating a new year): in-memory definitions persisted atomically with the year (existing behavior, relocated).
  - **Edit mode** (page is editing an existing year): loads the year's existing sub-periods and supports per-row add/edit/delete live. The Auto-split button is visible.
- **Auto-split-on-edit semantic (decision):**
  - Enabled only when the year has a valid date range AND **all existing sub-periods are Draft or there are none** (protects Active/Completed/Archived operational data).
  - When existing Draft sub-periods would be replaced, show a confirmation dialog naming the count before regenerating equal spans across the year range.
  - When non-Draft sub-periods exist, disable Auto-split with an explanatory tooltip ("Cannot auto-split while non-Draft sub-periods exist. Deactivate or complete them first.").
- `Edit.razor`: remove the standalone `<SubPeriodsSection>` block and its separator; render the new shared sub-periods component inside the edit form, beside `PeriodFormFields`. Keep the r1 danger-zone section (Draft-only delete) below the form.
- `SubPeriodsSection.razor` (+ `.razor.css`): retire/delete after the shared component folds it in. **Guardrail:** confirm no other caller references `SubPeriodsSection` before deleting (grep first). Its r1 per-row Draft delete affordance moves into the shared component.
- `blazor-css-isolation`: merge `SubPeriodsSection.razor.css` + `PeriodForm.razor.css` styles into `Create.razor.css` / `Edit.razor.css` / the new shared component's scoped CSS. No inline `<style>`.

### 8.3 Deactivate action (Active periods)
- `Periods.razor` grid: add a **Deactivate** row action for `Status == "Active"` rows (kebab, since Active rows currently have Complete → now 2 actions → kebab). `OnDeactivateAsync` using a shared `PeriodDeactivatePrompts` confirmation → `Api.DeactivatePeriodAsync` → `ReloadAsync` + `_error` bar.
- `Edit.razor`: add a **Deactivate** action for `_period is { Status: "Active" }` (visible in a controls region — NOT in the Draft danger zone). Confirmation + navigate/refresh.
- Shared `PeriodDeactivatePrompts.cs` (mirrors `PeriodDeletePrompts.cs`) for confirmation wording.
- Grid display: render `Deactivated` status text; no Delete/Activate/Complete/Deactivate actions on Deactivated rows. **Archive action IS exposed on Deactivated rows** (Open B resolved: yes — `Deactivated → Archived` cleanup path).
- Status filter/legend: add "Deactivated" to any status filter dropdown on the landing grid.

### 8.4 PeriodDto / status string mapping
- Add `"Deactivated"` to the API→DTO `Status` string projection (find the mapping used by `PeriodDto.Status`). Grid `GetKindLabel`/status switches updated.

## 9. Tests

### 9.1 Unit (`SchoolCollab.Students.Tests.Unit`)
- **`PeriodDeactivateHandlerTests.cs`** (new): AC for deactivate — Active→Deactivated; non-Active→`PeriodNotDeactivatableException`; cascade deactivates Active sub-periods of a year; Deactivated excluded from overlap (create a new period in the same range succeeds after deactivating the prior); tenant scoping (404 on other tenant's id); concurrency→404; domain event raised.
- **`PeriodUpdateImmutabilityTests.cs`** (new or extend `PeriodGuardAndAtomicCreateTests`/overlap tests): `UpdatePeriod` no longer accepts/changes Division; editing a year keeps its Division; overlap still rejects vs Active/Completed/Archived but allows vs Deactivated.
- **`PeriodFormParityTests`** (bUnit, `Admin.Tests.Unit` or `Students.Tests.Unit`): edit form renders the sub-periods section + Auto-split button for a Terms year; Division select is disabled on edit; Auto-split disabled when a non-Draft sub-period exists; Auto-split confirmation appears when replacing Draft subs.
- **`PeriodsLandingGridTests`** (extend): Deactivate action present on Active rows, absent on Deactivated/Draft/Completed/Archived; Deactivated rows show no Delete.
- Update `PeriodEditPageTests` for the Deactivate action + removed separate SubPeriodsSection.
- Known flaky `Periods_RowActions_CollapseToSingle` — rerun once if it flakes without a code cause.

### 9.2 Integration (`SchoolCollab.Students.Tests.Integration`)
- **`PeriodDeactivateEndpointTests.cs`** (new): `POST /periods/{id}/deactivate` → 204; repeat → 422 (already Deactivated) per domain (no idempotent early-return); 404 unknown/other-tenant; 422 on non-Active; DB-level: a new `POST /periods` in the same date range succeeds after deactivating the blocker.
- **Blocked by the pre-existing `CS7036 Division` compile errors** in this project (r1 residual). The worker must either fix those pre-existing errors (update the stale `CreatePeriod`/`Period.Create` call sites in `PeriodWizardOpenTermGateTests.cs`, `EnrollWithStreamEndpointTests.cs`, `StudentsApiClientEndToEndEnrollmentTests.cs` to pass `Division`) OR report the integration tests NOT RUN. **Recommended:** fix the stale call sites (small, mechanical) so the new integration tests can actually run and AC-D2/D5/D7 from r1 also finally get covered. Scope this as part of r2.

## 10. Reviewer: re-check r1 against the initial spec (decision 4)

The r2 reviewer's task includes a **conformance pass on the r1 delete work** against `documents/specs/period-draft-delete.md` FR-D1..D12 / NFR-D1..D3 / AC-D1..D10 — re-confirming the r1 reviewer's traceability (all 25 covered) still holds after r2's edits (especially the `SubPeriodsSection` unification, which moves the r1 sub-period row delete affordance into the unified section, and the `_loadError`/P2 fixes). Any r1 regression introduced by r2 is a P1.

## 11. Guardrails / out of scope

- No soft delete / recycle bin for periods.
- No bulk delete / bulk deactivate.
- No re-activation of Deactivated periods this round (Deactivated → Archived allowed; Deactivated → Active not wired).
- Deactivated periods are NOT deletable (Draft-only delete unchanged).
- No feature flag.
- No integration/outbox event for deactivate (domain event only).
- No migrations (enum stored as int; new value needs no schema change).
- `SubPeriodsListDialog.razor` remains untouched (r1 out-of-scope carryover).
- No 409 on any new route.

## 12. Risks & open questions

- **Risk 0 (new, biggest): `PeriodForm` wrapper elimination.** Deleting `PeriodForm.razor` and moving its ~24 KB of owning-form logic (EditForm, submit, save/load, autosplit, blocked-parent panel, PeriodFormModel) into `Create.razor` + `Edit.razor` is the largest structural change in r2. Two concrete hazards: (a) save/load + autosplit logic must not be silently lost or diverged between the two pages; (b) bUnit tests that hosted `PeriodForm` directly must be re-homed. Reviewer must verify create and edit both still save correctly and r1 delete still works after the move.
- **Risk A: sub-period surface unification.** Folding the old `SubPeriodsSection` + the deleted in-form section into the new shared `PeriodSubPeriodsEditor` touches both create and edit paths. Must preserve r1's per-row Draft delete + the add/edit flows. Reviewer must verify no r1 regression.
- **Risk B: Auto-split-on-edit replacing Draft sub-periods** — confirm semantics (replace vs. append). Plan default: replace Draft-only with confirmation; disabled when non-Draft exist.
- **Open A (RESOLVED 2026-08-31):** Suggest/Backfill buttons are **create-only** — `Create.razor` passes `<NameActions>` into `PeriodFormFields`; `Edit.razor` passes `null`. (Hides them on edit.)
- **Open B (RESOLVED 2026-08-31):** Archive IS exposed on Deactivated rows this round (Deactivated → Archived cleanup path).
- **Open C:** Does deactivating the active year break the enrollment guard (`EnrollStudentHandler` requires an active period)? Yes — by design, enrollment pauses until a corrected period is activated. Surface this in the spec addendum as a documented side effect.
- **Open D (RESOLVED 2026-08-31):** Fix the pre-existing integration-project compile errors as part of r2 (update stale `CreatePeriod`/`Period.Create` call sites in `PeriodWizardOpenTermGateTests.cs`, `EnrollWithStreamEndpointTests.cs`, `StudentsApiClientEndToEndEnrollmentTests.cs` to pass `Division`) — unblocks r1 + r2 integration coverage.
- **Open E (RESOLVED 2026-08-31):** Shared save/load/autosplit logic lives **page-local** in `Create.razor` + `Edit.razor` (Topic-faithful); extract a `PeriodFormService` only if duplication is clearly harmful.

## 13. Round docs

- Plan (this doc): `documents/rounds/plan-period-edit-parity-deactivate-r2.md`
- Acceptance (orchestrator-owned, to create at round start): `documents/rounds/acceptance-period-edit-parity-deactivate-r2.md`
- Review / UI-tester docs: `documents/rounds/{review,ui-tester}-period-edit-parity-deactivate-r2.md`
- Spec addendum (durable): `documents/specs/period-edit-parity-deactivate.md` (author first)