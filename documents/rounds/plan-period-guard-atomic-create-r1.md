# Round Plan — Period Activation Guard & Atomic Period Create (r1)

> **Spec:** `documents/specs/period-activation-guard-atomic-create.md` (source of truth).
> **Branch:** `feature/period-activation-guard-atomic-create` (already checked out; do not switch).
> **Roles:** orchestrator owns this plan doc + later acceptance doc; worker implements; reviewer verifies.

---

## 1. Context & locked decisions

1. **Hard, always-on activation guard — no feature flag.** A top-level academic year with
   `Division = Terms/Semesters` cannot be activated unless it has at least one **Draft**
   sub-period (FR-G1, FR-G2). `None`-division years are unaffected (FR-G3); sub-period
   activation and the FR-H4a auto-activation stay unchanged (FR-G4). API maps the new
   `PeriodGuardException` to 422 (FR-G5); `Periods.razor` surfaces the failure
   (FR-G6).
2. **Atomic create of a year + its sub-periods.** `CreatePeriod` optionally carries
   sub-period definitions, allowed only for top-level Terms/Semesters years (FR-C1).
   Per-definition validation (division, containment, sibling overlap) rejects the whole
   request (FR-C2); year + sub-periods persist Draft in ONE unit of work, zero rows on
   failure (FR-C3); the response carries the year id + sub-period ids (FR-C4). UI:
   `PeriodForm.razor` sub-periods section for top-level Terms/Semesters create only
   (FR-C5); `UpdatePeriod` and the standalone Sub-periods UI stay untouched (FR-C6).

**Verified repo facts the worker can rely on:**

- `RepositoryBase.AddAsync` currently saves immediately (`src/SchoolCollab.Core/Data/Repositories/RepositoryBase.cs:31-35`) — the atomic create therefore needs a new tracked-add + single-save repository method (task 4).
- Handler DI is Scrutor assembly scanning (`AsImplementedInterfaces()`, `src/Students/SchoolCollab.Students.Core/Extensions.cs:105-113`) — changing `CreatePeriod` from `ICommand<Guid>`-shaped result to a result record re-registers automatically; `DependencyInjectionRegistrationTests` must stay green.
- `RowAction` already supports `Disabled` (`src/SchoolCollab.Admin.Shared/Components/RowAction.cs:31`); a single-action row in `RowActionsMenu` renders `Title="@action.Label"` on the FluentButton — an explanatory label doubles as the tooltip.
- The FR-H4a "no candidate" auto-activation path already logs and proceeds when a candidate disappears — this is the NFR-G2 concurrent-delete fallback; do not add locking.
- `PeriodForm.razor` already exposes `AutoActivateOnCreate` (no in-repo caller passes true today); it only auto-activates after create — guard interplay is sub-period activation only (FR-G4), no change needed.

---

## 2. Task breakdown (worker)

All production paths are under `src/Students/` (spec §2 "shorthand" resolves to the Students contexts).

### Task 1 — `PeriodGuardException` (FR-G1)

- **File (new):** `src/Students/SchoolCollab.Students.Core/Domain/Exceptions/PeriodGuardException.cs`
- Sealed domain exception, pattern-matched on `PeriodOverlapException.cs` / `PeriodNotOpenException` (message-driven ctor, optional `Guid? PeriodId` not required). XML `<summary>` doc (dotnet-best-practices).
- Message MUST name the year and the required action, e.g.:
  `Cannot activate {Division} academic year '{Name}': it has no Draft sub-period. Create and activate at least one Term/Semester first.`
– exact wording optional, the two elements (year + "create and activate at least one Term/Semester") are required by FR-G1/AC-G2 evidence.

### Task 2 — Guard in `ActivatePeriodHandler` (FR-G1, FR-G2, FR-G3, FR-G4; AC-G1..G4; NFR-G2)

- **File:** `src/Students/SchoolCollab.Students.Core/CQRS/Periods/Commands/ActivatePeriod/ActivatePeriodHandler.cs`
- Insert the guard **immediately after** `repository.GetAsync(...)` and **before any state mutation**: before `GetActiveAcademicYearAsync`, before `sp.Complete()` / `priorYear.Complete()`, before `period.Activate()`.
- Condition: `period.ParentPeriodId is null && period.Division != AcademicYearDivision.None` →
  `var subPeriods = await repository.GetSubPeriodsAsync(period.Id, cancellationToken)` (tracked, already exists)
  → if none has `Status == PeriodStatus.Draft` → `throw new PeriodGuardException(...)`.
- Do NOT touch: prior-year close cascade, sibling close, `PeriodNotOpenException` parent check, FR-H4a auto-activation block (its "no candidate" fallback is the NFR-G2 race fallback), cache invalidation, event enqueueing.
- `None`-division and sub-period activations skip the guard entirely (FR-G3/G4).
- **Partial-mutation-free proof (AC-G2):** when the guard throws the handler has issued zero mutations — no prior year completed, year row still Draft.

### Task 3 — Atomic create command + handler (FR-C1..C4; AC-C1..C3; NFR-G1)

- **Files:**
  - `src/Students/SchoolCollab.Students.Core/CQRS/Periods/Commands/CreatePeriod/CreatePeriod.cs` — extend the command record with `IReadOnlyList<SubPeriodDefinition>? SubPeriods = null` and add records:
    - `SubPeriodDefinition(string Name, DateOnly StartDate, DateOnly EndDate)` (record = DTO, dotnet-best-practices),
    - `CreatePeriodResult(Guid YearId, IReadOnlyList<Guid> SubPeriodIds)` — new command result.
    - Change `CreatePeriod : ICommand<CreatePeriodResult>`. DI re-resolves via scanning (no manual registration edits).
  - `src/Students/SchoolCollab.Students.Core/CQRS/Periods/Commands/CreatePeriod/CreatePeriodHandler.cs`:
    1. **Invalid usage (FR-C1):** when `SubPeriods` is non-empty and (`command.ParentPeriodId` is set OR `command.Division == None`) → `ArgumentException` → route maps to 400 (AC-C3 covers None+list; sub-period create with a list is the same catch).
    2. Existing sub-period-create validation and the year-level `GetOverlappingPeriodsAsync` check remain for the year itself (unchanged semantics).
    3. **Per-definition validation (FR-C2), whole-request rejection, before ANY persistence:**
       - each definition: `EndDate >= StartDate` (surfaces via `Period.Create` ArgumentException) — or pre-check explicitly for a clearer message;
       - containment in the year's range → `PeriodContainmentException` (existing 422 mapping);
       - no overlap among sibling definitions (pairwise, in-memory — definitions are not yet rows) → `PeriodOverlapException` naming the two definitions (existing 422 mapping).
       - Sub-periods of a brand-new year cannot overlap external periods beyond the already-rejected year overlap; no extra repo query needed.
    4. **Persist atomically (FR-C3):** `Period.Create(...)` the year (Draft) + `Period.Create(name, start, end, division, parentPeriodId: year.Id)` each sub-period, all `.WithTenant(tenantProvider)`; then a **single** repository call `AddRangeAsync([year, ..subs], ct)` — one `SaveChanges` of the object graph; any failure leaves zero rows.
    5. Return `CreatePeriodResult(year.Id, subIds)`; log; `cache.RemoveByTagAsync("students")` as today. No auto-activation on create — all rows Draft.
  - **Repository:** `src/Students/SchoolCollab.Students.Core/Data/Repositories/IPeriodRepository.cs` + `PeriodRepository.cs` — add `Task AddRangeAsync(IReadOnlyList<Period> periods, CancellationToken ct = default)`; implementation: `Db.Periods.AddRangeAsync(periods, ct); await Db.SaveChangesAsync(ct);` (single save; do NOT call the base `AddAsync` per row — it saves each item).
  - **DI note:** registration is scan-based; `CreatePeriodResult` flows automatically, but run `DependencyInjectionRegistrationTests`.

### Task 4 — API routes + Contracts (FR-G5, FR-C1, FR-C4)

- **File:** `src/Students/SchoolCollab.Students.Api/Endpoints/PeriodRoutes.cs`
  - Activate endpoint: add `catch (PeriodGuardException ex)` → `Results.Json(new { ex.Message }, statusCode: 422)` (FR-G5).
  - Create endpoint: change response to carry both ids: `Results.Created($"/periods/{id}", new { id, subPeriodIds })` (FR-C4). Existing `ArgumentException` → 400 catch already satisfies AC-C3; `PeriodContainmentException`/`PeriodOverlapException` → 422 already satisfy AC-C2. No other changes.
- **Application client:** `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs`
  - Extend `CreatePeriodRequest` (line ~345) with `IReadOnlyList<SubPeriodDefinitionRequest>? SubPeriods = null`; add `public record SubPeriodDefinitionRequest(string Name, DateOnly StartDate, DateOnly EndDate);` next to it.
  - `CreatePeriodAsync` (line ~1324) parses a private `record CreatePeriodIdResponse(Guid Id, IReadOnlyList<Guid> SubPeriodIds)` (or extend the private `IdResponse` locally for this call only) and returns the year id; keep non-success handling as today (it already surfaces the 422/400 body in `HttpRequestException.Message`, which feeds the UI error bars — both FR-G6 and FR-C5 error display).
- **Do NOT change** `UpdatePeriodRequest` or the PUT endpoint (FR-C6).

### Task 5 — `PeriodForm.razor` sub-periods section (FR-C5; AC-C4; FR-C2 client side)

- **Files:** `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor` (+ `PeriodForm.razor.css` if new styles are needed — per-component CSS isolation, no new inline style blocks; existing `Style="flex:1"` attributes are pre-existing, leave them).
- Render the Sub-periods section ONLY when: create mode (`PeriodId` is null), not a sub-period (`!_isSubPeriod`), and parsed division is `Terms` or `Semesters`. Hidden for `None`, for sub-period create (`?parent=`), and in edit mode.
- Rows: name + start/end date pickers; "+ Add sub-period" / per-row remove; "Auto-split into 2" helper that replaces rows with two equal halves of the year range (day-precision: first `[start, floorSplit-1]`, second `[floorSplit, end]` or equivalent — halves within one day of each other and inside the year range).
- Client validation before submit mirrors FR-C2: every row needs a name + start/end, `end >= start`, contained in the year range, no sibling overlap → inline `_error` (existing FluentMessageBar) instead of submitting. Server-side rejections still surface through `_error`.
- Submit: pass `SubPeriods` on `CreatePeriodRequest` only when the section is active and rows exist; keep `AutoActivateOnCreate` path unchanged.
- **UNCHANGED (FR-C6):** `UpdatePeriod` flow (PUT) and `SubPeriodsSection.razor` / `SubPeriodsListDialog.razor` / `Edit.razor`.

### Task 6 — `Periods.razor` guard affordances (FR-G6)

- **File:** `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Periods.razor`
- Guard-failure message bar: `OnActivateAsync` already writes the API error (which contains the 422 `PeriodGuardException` message) into `_error`, rendered by `LandingPage`'s `Error` bar — keep, and ensure a guard failure produces a clear server message (verify wording lands verbatim; no swallowing).
- Disabled Activate: in `BuildPeriodActions`, when `period.Status == "Draft" && period.ParentPeriodId is null && period.Division is "Terms" or "Semesters"` and the memoized flat list shows **zero Draft sub-periods** for that year (compute a `_draftSubPeriodCounts` dictionary alongside the existing `_subPeriodCounts`), add the Activate action with `disabled: true` and an explanatory label so the single-action FluentButton `Title` tooltip explains why (`RowActionsMenu` renders `Title=@action.Label`). Do not modify `RowAction`/`RowActionsMenu` shared components.
- `Create.razor`, `Edit.razor` unchanged.

### Task 7 — Existing-test reconciliation (guard ripple, minimal diffs)

The hard guard changes behavior for tests that activate a Terms/Semesters year with zero sub-periods. Fix by reordering seeds (create a Draft sub-period BEFORE activating the year) or, where hierarchy is incidental, switching the year to `None`. No production behavior changes to accommodate tests. Verified list:

| File | Breaking spots | Fix |
| --- | --- | --- |
| `tests/SchoolCollab.Students.Tests.Unit/PeriodHierarchyActivationTests.cs` | `Activate_Year_WithOnlyCompletedSubPeriods_StaysInGapState` (asserts the old gap state — contradicts AC-G3) | REWRITE: now expects `PeriodGuardException`; year stays Draft; no prior year closed |
| `tests/SchoolCollab.Students.Tests.Unit/ActivePeriodProviderTests.cs` | `GetActiveSubPeriod_ReturnsActiveSubPeriodForCurrentTenant`, `GetActiveSubPeriod_IsolatedPerTenant`, `Activate_SecondTerm_InvalidatesCachedActiveSubPeriod`, `Activate_SecondYear_InvalidatesCachedActiveAcademicYear`, `Complete_AcademicYear_InvalidatesCachedYearLookups` | seed a Draft sub-period before activating the Terms year |
| `tests/SchoolCollab.Students.Tests.Unit/ActivityGroupPeriodAlignedSpanTests.cs` | `SeedYearAndTermAsync`, `SeedYearAndSemesterAsync`, `Create_Termly_WhenDivisionTerms_Succeeds` | reorder: sub-period created before year activation |
| `tests/SchoolCollab.Students.Tests.Unit/AssignStudentTopicHandlerTests.cs` | `SeedActiveYearAsync` (division param) | seed a Draft sub before year activation for Terms/Semesters cases |
| `tests/SchoolCollab.Students.Tests.Unit/TopicAssignmentPeriodTests.cs` | `SeedActiveYearAsync` (incl. `withTerm:false`), lines ~48/217/240 | reorder / seed Draft sub |
| `tests/SchoolCollab.Students.Tests.Unit/UpdateTopicAssignmentPeriodTests.cs` | `SeedActiveYearAsync` | same |
| `tests/SchoolCollab.Students.Tests.Unit/PeriodHierarchyReadTests.cs` | `ActiveAcademicYear_ReturnsActivatedYear`, `ActiveSubPeriod_ReturnsActivatedSubPeriod` | reorder |
| `tests/SchoolCollab.Students.Tests.Unit/PeriodOverlapInvariantTests.cs` | `Activate_WhenAnotherIsActive_ClosesPriorAndActivatesNew` (both Terms years, no subs) | switch to `None` (invariant tested is overlap/close, not hierarchy) or seed subs |
| `tests/SchoolCollab.Students.Tests.Unit/Tenancy/StudentsStrictTenancyTests.cs` | `AC_H2_SubPeriod_Activation_IsTenantScoped` | create term before activating year |

Expected-green, still run: `AcademicYearDivisionNoneBackCompatTests` (None years), `CreateSubjectForGradeHandlerTests` (term created before year activation), `tests/SchoolCollab.Admin.Tests.Unit/PeriodsLandingGridTests` (its Draft row is a sub-period; years are Active/Completed → FR-G6 does not disable it), `tests/SchoolCollab.Students.Tests.Integration` (POST payloads without sub-period lists behave identically).

### Repo conventions (binding)

- Read + obey `.github/copilot/rules/dotnet-best-practices.md` for every `.cs`/`.razor` change: typed domain exceptions (no `InvalidOperationException` from handlers beyond existing), records for DTOs, primary-constructor DI, factory-created entities (`Period.Create` never `new Period()` outside Core), XML `<summary>` on new public handler/DTO members, no `Console.WriteLine`, no MediatR.
- Per-component CSS isolation for any new styling (`.razor.css`), no new inline `<style>` blocks.
- Testing per `.github/copilot/rules/testing.md`: MTP + MSTest + Moq + FluentAssertions; bUnit for Blazor; scripted `HttpMessageHandler` for HTTP mocking (never Moq HTTP).

---

## 3. Test plan

**Target projects:** `tests/SchoolCollab.Students.Tests.Unit` (handler + bUnit, `StudentsTestScope` in-memory EF, pattern = `PeriodHierarchyActivationTests`), `tests/SchoolCollab.Students.Api.Tests.Unit` (smoke-level only today), `tests/SchoolCollab.Admin.Tests.Unit` (bUnit for `Periods.razor`, pattern = `PeriodsLandingGridTests`).

### New file: `tests/SchoolCollab.Students.Tests.Unit/PeriodGuardAndAtomicCreateTests.cs` (NFR-C1 matrix)

Activation guard (handler-level, `StudentsTestScope`):

1. **AC-G1** — Terms year + 1 Draft term → activates; year Active; term auto-activated (FR-H4a preserved).
2. **AC-G2** — Terms year + 0 sub-periods → `PeriodGuardException`; year row still Draft; **partial-mutation-free**: an Active prior year seeded in the same tenant stays Active (no prior-year close ran).
3. **AC-G3** — Terms year with only Completed sub-periods → `PeriodGuardException` (no *Draft* candidate).
4. **AC-G4** — None year, no sub-periods → activates unchanged (204-equivalent), active-sub count 0.
5. Guard message contains the year name and "Term"/"Semester" action wording (FR-G1 evidence).
6. Sub-period activation unchanged: term under Active year activates; term under Draft year still `PeriodNotOpenException` (FR-G4).
7. Concurrent-delete fallback (NFR-G2) is covered implicitly by test 4's handler path (no candidate → log + proceed); no separate concurrency test required.

Atomic create:

8. **AC-C1** — create Terms year + 2 term definitions → 3 rows persisted, all Draft, in one save; `CreatePeriodResult.SubPeriodIds` has 2 ids matching the rows (FR-C4).
9. **AC-C2** — one definition overlapping a sibling → `PeriodOverlapException`; `Db.Periods` EMPTY (zero rows — the partial-persistence-free failure proof; same assertion shape for containment-violation → `PeriodContainmentException` with zero rows).
10. **AC-C3** — None year + sub-period list → `ArgumentException` (400 downstream); also sub-period create (parent set) with a list → `ArgumentException`.
11. Definition end<start → rejected (ArgumentException), zero rows.
12. Non-contained definition (crosses year end) → `PeriodContainmentException`, zero rows.
13. Empty list / null list on top-level Terms year → plain single-period create unchanged (back-compat).
14. Semesters year + semesters definitions works like Terms (division carry-through).

### UI tests (AC-C4, FR-G6)

- `tests/SchoolCollab.Students.Tests.Unit/PeriodFormSubPeriodsSectionTests.cs` (new, bUnit + `ScriptedHandler`, pattern = `PeriodFormBlockedParentTests`):
  - Terms/Semesters top-level create → section renders; add row; auto-split produces 2 rows; remove row; invalid row blocks submit with inline error (mirrors FR-C2).
  - None division → no section; `?parent=` sub-period create → no section; edit mode (`PeriodId`) → no section (FR-C5/AC-C4/FR-C6).
  - POST body contains serialized `subPeriods` definitions when provided.
- `tests/SchoolCollab.Admin.Tests.Unit` (owner of `Periods.razor` bUnit; extend `PeriodsLandingGridTests.cs` or add `PeriodsActivateGuardTests.cs`):
  - Terms year (top-level, Draft, zero Draft subs in the scripted list) → Activate action present but disabled with explanatory title (FR-G6).
  - Terms year with ≥1 Draft sub → Activate enabled (regression).
  - None year / Draft sub-period row → Activate enabled (FR-G3/G4 regression).

### API mapping (FR-G5)

- Handler-level tests 2/3 assert `PeriodGuardException`; the `Results.Json(..., 422)` mapping is a one-line catch reviewed against `PeriodRoutes.cs` (AC-G2 expected status). `tests/SchoolCollab.Students.Api.Tests.Unit` (smoke) only asserts assembly load — run it, no additions required unless the reviewer finds the mapping untestable elsewhere.

### Verification commands (worker + reviewer)

```
dotnet build SchoolCollab.sln -c Debug --nologo -v q
dotnet test tests/SchoolCollab.Students.Tests.Unit
dotnet test tests/SchoolCollab.Admin.Tests.Unit
dotnet test tests/SchoolCollab.Students.Api.Tests.Unit
dotnet test tests/SchoolCollab.Students.Tests.Integration
```

- NFR-C2: build 0 errors + tests 0 failures. `MigrationGuardTests` doubles as the NFR-G1 guard (no pending model changes) — it runs in `SchoolCollab.Students.Tests.Unit`.
- MSB3021/MSB3027 lock remedy: stop stray `dotnet` processes (`Get-Process dotnet` / `pkill dotnet`), then rerun ONCE; do not loop retries.

---

## 4. NFRs

- **NFR-G1** — no schema/migration changes; entity/table untouched; `MigrationGuardTests` must stay green.
- **NFR-G2** — guard + persistence stay inside the per-command context; no new locks/transactions beyond the single EF `SaveChanges`; concurrent sub-period delete between guard check and activate falls back to the FR-H4a "no candidate" path (log + proceed).
- **NFR-C2** — `dotnet build` 0 errors, `dotnet test` 0 failures before PR-readiness.

## 5. Explicitly forbidden

- New feature flag anywhere (guard is always-on).
- New EF migration or entity/schema change (incl. editing an existing migration).
- Any change to `UpdatePeriod` command/handler, the PUT `/periods/{id}` endpoint, `SubPeriodsSection.razor`, `SubPeriodsListDialog.razor`, `Edit.razor` (FR-C6).
- Git commit / push / branch switch (orchestrator gates those separately).
- Scope creep beyond the files listed in section 2 without a supervisor decision.

## 6. Acceptance-doc note (orchestrator, later phase)

Acceptance doc lands at `documents/rounds/acceptance-period-guard-atomic-create-r1.md` after worker + reviewer rounds, summarizing changed files, test results (build/test table), and FR/AC coverage evidence.