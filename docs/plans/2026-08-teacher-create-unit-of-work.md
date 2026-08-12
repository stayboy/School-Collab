# Plan — Teacher create Unit of Work (atomic teacher + grade/activity assignments)

> **Goal:** Make "create a teacher for a grade with its assignment" an all-or-nothing
> unit of work. If any step in the create process is disrupted (bad grade id, duplicate
> link, DB transient fault), the **entire** create — teacher row, qualifications, and
> every grade/activity link — is rolled back. No orphaned teacher, no partial
> assignments.
>
> **Branch target:** `feature/teacher-create-unit-of-work`.
> **Convention references:** `CreateTeacherWithAssignments` stub (existing, dead code),
> `ModuleDbContext` tenant/audit stamping, `RepositoryBase` auto-save pattern,
> `OutboxDispatcher` (`BeginTransactionAsync` is the only transaction usage today),
> `ICommandHandler<T,TResult>` CQRS pair (no pipeline/behaviors), `ApiFactory`
> integration-test harness (Testcontainers Postgres).

---

## 0. Prompt-to-plan traceability

| Prompt requirement | Plan section | Resolution |
|---|---|---|
| A unit of work for the multiple steps of creating a teacher for a grade with its assignment | §2, §3 | `IUnitOfWork` + `CreateTeacherWithAssignmentsHandler` wrapping teacher + qualifications + grade/activity links in one EF Core transaction |
| **Verify from repo** whether the whole process fails on a single disruption today | §1 | **Verified:** it does NOT — multi-request, per-`SaveChangesAsync` commits, no rollback (orphan risk confirmed) |
| Create a plan for a repo pattern if no approach exists | §2, §3, §4 | `IUnitOfWork` abstraction in `SchoolCollab.Core` + first concrete handler; reusable for other multi-step flows |
| Prove the whole-process-rolls-back guarantee | §5 | Integration test: inject a bad grade id mid-batch, assert zero rows persisted |

---

## 1. Current-state finding — the create flow is NOT atomic (verified from repo)

The prompt asks to **verify** whether the whole process fails on a single disruption.
Findings from read-only exploration of `src/`:

1. **A `CreateTeacherWithAssignments` command already exists — but it is dead code.**
   `Students/SchoolCollab.Students.Core/CQRS/Teachers/Commands/CreateTeacherWithAssignments/CreateTeacherWithAssignments.cs`
   defines the record with a doc-comment claiming *"Atomically creates a teacher with
   grade and activity assignments (Unit of Work pattern). All operations succeed or
   fail together."* There is **no handler**, **no API endpoint**, and **no client
   caller**. `grep -rn CreateTeacherWithAssignments src tests` returns only that one
   file. The intent was planted but never built.

2. **No Unit-of-Work / transaction abstraction exists anywhere in the app code.**
   `grep -rni IUnitOfWork|BeginTransaction|ExecuteTransactionAsync|IExecutionStrategy`
   across `src` returns exactly **two** hits, neither in the request path:
   - `Core/Messaging/OutboxDispatcher.cs` — a background service that opens its own
     transaction for outbox dispatch.
   - `MigrationService/Seeding/CodedValueSeeder.cs` — seed-time only.
   The CQRS layer is a bare interface pair (`ICommandHandler<T>` /
   `ICommandHandler<T,TResult>`) with **no pipeline, no behaviors, no dispatch
   wrapper** — there is no place where a transaction could be applied centrally.

3. **Every repository method commits independently.**
   `Core/Data/Repositories/RepositoryBase.cs` — `AddAsync`/`UpdateAsync`/`DeleteAsync`
   each call `Db.SaveChangesAsync` immediately. `TeacherRepository.AddQualificationAsync`,
   `AddGradeLevelAsync`, `AddActivityAssignmentAsync` likewise each call
   `SaveChangesAsync` themselves. So a handler that calls two repository methods issues
   **two separate transactions**; there is no ambient transaction to roll them back
   together.

4. **The live create flow is multi-HTTP-request and non-transactional.**
   `TeacherEditDialog.razor` → `SubmitAsync` (lines ~488–543):
   - `Api.CreateTeacherAsync(...)` → `POST /teachers` → `CreateTeacherHandler`
     (saves teacher, then one `SaveChangesAsync` per qualification).
   - `ReconcileAssignmentsAsync(...)` → a **loop** of N ×
     `Api.LinkTeacherGradeAssignmentAsync` / `Api.LinkTeacherActivityAssignmentAsync`,
     each a **separate HTTP request** → separate handler → separate `SaveChangesAsync`.
   Each request gets its own scoped `StudentsDbContext` (`AddDbContextFactory` +
   scoped repositories in `Students.Core/Extensions.cs`), so there is no shared
   ambient transaction across requests.

   **Failure mode (the disruption):** if request #3 in the loop throws (bad grade id →
   `GradeLevelNotFoundException`, duplicate → `TeacherLinkAlreadyExistsException`, or a
   transient Postgres fault), requests #1–#2 have **already committed**. The teacher
   row and the first links persist; the later links never do. Result: an **orphaned
   teacher with partial assignments** — exactly the "single disruption fails the whole
   process" the prompt describes. No rollback exists.

5. **No spec/plan doc governs transactionality for this flow.** `grep -ni
   transaction|unit.of.work|atomic` across `documents/specs` and `docs/plans` returns
   only tangential, unrelated mentions.

**Conclusion:** no transactional approach exists for the create-teacher-with-assignment
flow. The repo anticipated it (`CreateTeacherWithAssignments`) but never implemented
it. This plan builds it.

---

## 2. The pattern — a request-scoped Unit of Work

A true all-or-nothing guarantee is only achievable **inside a single request** in this
architecture (per-request `DbContext`, no cross-request ambient transaction). So the
pattern collapses the N-step client loop into **one API call** that wraps every write
in **one EF Core transaction** and commits once at the end.

Two pieces:

1. **`IUnitOfWork`** — a thin, reusable abstraction in `SchoolCollab.Core` that
   wraps `Database.BeginTransactionAsync` + `ExecuteTransactionAsync` (EF Core's
   `IExecutionStrategy`, which retries on Postgres transient serialization faults) +
   a single `SaveChangesAsync`. It is **not** an ambient/scope thing — it is an
   explicit callable the handler invokes. This keeps it compatible with the repo's
   existing per-method-`SaveChanges` repositories (the UoW path simply does not call
   those overloads).

2. **`CreateTeacherWithAssignmentsHandler`** — the concrete compound handler. It
   builds the full EF Core tracking graph (teacher + qualifications + grade links +
   activity links) using the existing domain factory methods (`Teacher.Create`,
   `TeacherGradeLevel.Create`, `TeacherActivityAssignment.Create`), runs the same
   validations the per-link handlers run, and commits the whole graph through the
   `IUnitOfWork` in one transaction.

### Why this shape (and not a pipeline behavior or repo overhaul)

- **No CQRS pipeline exists today.** Adding a transaction *behavior*/middleware would
  require introducing a mediator/dispatcher the repo does not have — a far larger
  change than the prompt needs. The explicit `IUnitOfWork` callable matches the
  repo's current explicit-handler style and is opt-in per compound command.
- **Repository auto-save is not touched.** Rewriting `RepositoryBase` to be
  save-deferring is a repo-wide refactor with blast radius across every bounded
  context. The compound handler instead uses the `DbContext` + entity factories
  directly (tracking-only), exactly as `CreateTeacherHandler` does before its
  `repository.AddAsync` save. Single `SaveChangesAsync` at the end.
- **Reusability is preserved.** `IUnitOfWork` lives in `SchoolCollab.Core` so later
  compound flows (edit/reconcile, student transfer, guardian link) adopt it without a
  new abstraction each time (see §6 follow-ups).

---

## 3. Implementation steps

### 3.1 `IUnitOfWork` abstraction (new) — `src/SchoolCollab.Core/Data/IUnitOfWork.cs`

```csharp
public interface IUnitOfWork<out TContext> where TContext : ModuleDbContext
{
    /// <summary>
    /// Executes <paramref name="action"/> inside a single EF Core transaction with
    /// the <see cref="IExecutionStrategy"/> retry for transient Postgres faults.
    /// <paramref name="action"/> tracks entities on the context and must call
    /// <c>SaveChangesAsync</c> — the UoW commits the transaction only if the action
    /// returns without throwing. Any exception rolls back the whole batch.
    /// </summary>
    Task<TResult> ExecuteAsync<TResult>(
        Func<TContext, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default);
}
```

- Concrete `UnitOfWork<TContext>` in `Core/Data/UnitOfWork.cs` resolves `TContext`
  from DI, calls `Database.CreateExecutionStrategy().ExecuteTransactionAsync(...)`
  (which begins a tx, runs the action, calls `SaveChangesAsync`, commits), and
  propagates domain exceptions unchanged so the API layer maps them to 4xx.
- Register in each bounded context's `Extensions.Add...Core`:
  `services.AddScoped<IUnitOfWork<StudentsDbContext>, UnitOfWork<StudentsDbContext>>();`

### 3.2 `CreateTeacherWithAssignmentsHandler` (new) — replaces the stub's intent

File: `Students.Core/CQRS/Teachers/Commands/CreateTeacherWithAssignments/CreateTeacherWithAssignmentsHandler.cs`

```
public sealed class CreateTeacherWithAssignmentsHandler(
    IUnitOfWork<StudentsDbContext> uow,
    IEntityCodeGenerator entityCodeGenerator,
    ITeacherRepository teacherRepo,        // read-only checks (no auto-save used)
    IGradeLevelRepository gradeLevelRepo,
    IActivityGroupRepository activityGroupRepo,
    ITenantProvider tenantProvider,
    HybridCache cache,
    ILogger<...> logger) : ICommandHandler<CreateTeacherWithAssignments, Guid>
```

Handler body (sketch):

1. `tenantProvider.RequireTenantContext(...)` up front (same as `CreateTeacherHandler`).
2. Pre-validate every reference id **before** tracking anything:
   - each `GradeAssignment.GradeLevelId` exists (tenant-scoped) → else `GradeLevelNotFoundException`
   - each `ActivityAssignment.ActivityGroupId` exists → else `ActivityGroupNotFoundException`
   - optional `SubjectId` is an enrolled topic for that grade
   - de-duplicate within the batch → `TeacherLinkAlreadyExistsException`
3. `return await uow.ExecuteAsync(async (db, ct) => { ... }, cancellationToken);`
   Inside the tx:
   - `var staffNumber = await entityCodeGenerator.GenerateAsync("STAFF_CODE", ct);`
   - `var teacher = Teacher.Create(...).WithTenant(tenantProvider); db.Teachers.Add(teacher);`
   - add `TeacherQualification.Create(...)` rows to `db.TeacherQualifications` (no Save)
   - add `TeacherGradeLevel.Create(...)` rows (no Save)
   - add `TeacherActivityAssignment.Create(...)` rows (no Save)
   - **single** `await db.SaveChangesAsync(ct);` → `IUnitOfWork` commits the tx.
4. `await cache.RemoveByTagAsync("teachers", ct);` after commit.
5. Return `teacher.Id`.

> All entity creation goes through the **same domain factory methods** the existing
> handlers use, so domain invariants (`.WithTenant`, role/subject rules) are unchanged.
> The only difference is *when* `SaveChangesAsync` runs — once, inside the tx.

### 3.3 API endpoint — `src/Students/SchoolCollab.Students.Api/Endpoints/TeacherRoutes.cs`

Add next to the existing `POST /teachers`:

```csharp
group.MapPost("/with-assignments", async (
    [FromBody] CreateTeacherWithAssignments command,
    [FromServices] ICommandHandler<CreateTeacherWithAssignments, Guid> handler,
    CancellationToken ct) =>
{
    try
    {
        var id = await handler.HandleAsync(command, ct);
        return Results.Created($"/teachers/{id}", new { id });
    }
    catch (GradeLevelNotFoundException) { return Results.Problem(..., 404); }
    catch (ActivityGroupNotFoundException) { return Results.Problem(..., 404); }
    catch (TeacherLinkAlreadyExistsException) { return Results.Problem(..., 409); }
});
```

Map the same domain exceptions the single-link endpoints already map, so the client
sees consistent 4xx codes.

### 3.4 Client switch — `TeacherEditDialog.razor` `SubmitAsync` (create branch only)

Replace the create branch (lines ~492–507) — which today does `CreateTeacherAsync` +
`ReconcileAssignmentsAsync` + a fallback context-grade link — with a single call:

```csharp
teacherId = await Api.CreateTeacherWithAssignmentsAsync(new CreateTeacherWithAssignmentsRequest(
    model.TitleCodedValueId, model.FirstName.Trim(), model.LastName.Trim(),
    string.IsNullOrWhiteSpace(model.DisplayName) ? null : model.DisplayName.Trim(),
    model.GenderCodedValueId, model.DateOfBirth, model.LevelOfEducationCodedValueId,
    QualificationIds(),
    GradeAssignments: model.Assignments.Where(a => !a.IsActivity).Select(a => new(...)).ToArray(),
    ActivityAssignments: model.Assignments.Where(a => a.IsActivity).Select(a => new(...)).ToArray()));
```

- The context-grade force-include (the `if (model.ContextGradeLevelId is { } ctx ...)`
  fallback) folds into the batch built before the call.
- **Edit branch is out of scope** (see §6); `ReconcileAssignmentsAsync` stays as-is
  for edits until the follow-up `UpdateTeacherWithAssignments` command lands.

### 3.5 Client API surface — `StudentsApiClient`

Add `CreateTeacherWithAssignmentsAsync` mirroring the existing
`CreateTeacherAsync`/`LinkTeacherGradeAssignmentAsync` helpers; add the
`CreateTeacherWithAssignmentsRequest`/`GradeAssignmentRequest`/`ActivityAssignmentRequest`
DTOs to the shared contracts the client already consumes.

---

## 4. Rollback contract — what the guarantee looks like

With §3 in place, a single disruption produces:

| Disruption | What happens |
|---|---|
| Non-existent `GradeLevelId` in the batch | Pre-validation throws `GradeLevelNotFoundException` **before** the tx starts. No rows written. |
| Duplicate grade link already persisted (or duplicated within the batch) | `TeacherLinkAlreadyExistsException` thrown pre-tx (within-batch) or by a DB unique constraint inside the tx. Tx rolls back: teacher + qualifications + all links reverted. |
| Transient Postgres fault during `SaveChangesAsync` | `IExecutionStrategy` retries the tx; if it still fails, the tx rolls back and the exception propagates as 5xx. No partial commit. |
| Any unhandled exception inside the action | `IUnitOfWork` rolls back; caller sees the raw exception → 5xx. No rows persisted. |

The **invariant**: after `POST /teachers/with-assignments` returns non-2xx, the
teacher and **all** of its assignments either all exist or none exist. There is no
middle state reachable through this endpoint.

---

## 5. Verification — integration test proving the rollback

New file: `tests/SchoolCollab.Students.Tests.Integration/TeacherCreateWithAssignmentsEndpointTests.cs`,
using the existing `ApiFactory` (Testcontainers Postgres + real `StudentsDbContext` +
migrations). Two cases:

1. **Happy path — all persisted together.**
   POST a valid `CreateTeacherWithAssignments` with 1 teacher, 2 qualifications, 2
   grade links, 1 activity link (2 grades). Assert `GET /teachers/{id}` returns the
   teacher with exactly those qualifications + links (count + ids).

2. **Disruption path — whole process rolls back (the prompt's core guarantee).**
   POST a `CreateTeacherWithAssignments` whose 3rd `GradeAssignment` references a
   `GradeLevelId` that does **not** exist. Assert:
   - the endpoint returns **404** (mapped from `GradeLevelNotFoundException`), and
   - `GET /teachers` (list) and a direct `GET /teachers/{any-id}` show **no new teacher**
     was persisted — i.e. the teacher row, the qualifications, and the two valid grade
     links that preceded the bad one in the batch are **all absent**. This is the
   proof that a single disruption fails the whole process atomically.

   Variant B (stronger, exercises the DB-rollback path rather than pre-validation):
   pre-seed a `TeacherGradeLevel` for `(teacher, gradeA, subjectX)`, then POST a
   batch that recreates the same `(gradeA, subjectX)` link among otherwise-valid
   rows. The unique constraint fires **inside** the tx → `TeacherLinkAlreadyExistsException`
   → 409, and the new teacher + the other links are absent. This proves the
   transaction rolls back a fault that occurs *after* some rows are already tracked.

Both tests run against real Postgres (not the in-memory provider) so the transaction
semantics and unique constraints are real — mirroring the `ApiFactory` convention.

### Build / unit guard

- `dotnet build SchoolCollab.sln` clean.
- Existing `CreateTeacherHandler` / `LinkTeacherGradeLevelHandler` unit tests stay
  green (they are untouched — the single-step endpoints remain for callers that
  don't use the compound flow, e.g. the grade-detail "add teacher" row).

---

## 6. Out of scope / follow-ups (documented, not done now)

- **Edit path atomicity.** `TeacherEditDialog` edit branch + `ReconcileAssignmentsAsync`
  is the same multi-request, non-transactional shape and has the same orphan/partial
  risk. A follow-up `UpdateTeacherWithAssignments` compound command + `IUnitOfWork`
  would cover it. Left out of this plan to keep the create guarantee reviewable.
- **Generalizing `IUnitOfWork` to other modules.** `Assignments` and `Settings` contexts
  can register their own `IUnitOfWork<TContext>` and adopt the pattern for their own
  multi-step commands (e.g. publish-assignment + submission-row fan-out).
- **A CQRS pipeline/behavior for transactions.** Not introduced here (no dispatcher
  exists); revisit only if compound commands proliferate.
- **Outbox interaction.** If teacher-create ever emits an integration event, the
  outbox row must be written in the **same** tx (the existing outbox config writes
  on `SaveChanges`). `IUnitOfWork.ExecuteAsync` already wraps that in one tx, so an
  outbox message added during the action commits/rolls back atomically — no extra
  work, just a note for the future.

---

## 7. Files touched

| New / edit | Path |
|---|---|
| new | `src/SchoolCollab.Core/Data/IUnitOfWork.cs` |
| new | `src/SchoolCollab.Core/Data/UnitOfWork.cs` |
| edit | `src/SchoolCollab.Core/Extensions.cs` (or a Core DI extension) — register `IUnitOfWork<StudentsDbContext>` |
| edit | `src/Students/SchoolCollab.Students.Core/Extensions.cs` — register `IUnitOfWork<StudentsDbContext>` + the new handler |
| new | `src/Students/SchoolCollab.Students.Core/CQRS/Teachers/Commands/CreateTeacherWithAssignments/CreateTeacherWithAssignmentsHandler.cs` |
| edit | `src/Students/SchoolCollab.Students.Api/Endpoints/TeacherRoutes.cs` — `POST /teachers/with-assignments` |
| edit | `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs` (or equivalent) — `CreateTeacherWithAssignmentsAsync` + request DTOs |
| edit | `src/Students/SchoolCollab.Students.Application/Components/Students/TeacherEditDialog.razor` — create branch single-call |
| new | `tests/SchoolCollab.Students.Tests.Integration/TeacherCreateWithAssignmentsEndpointTests.cs` |