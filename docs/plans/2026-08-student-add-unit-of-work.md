# Plan — Student add Unit of Work (atomic student + enrollment + guardians + contacts)

> **Goal:** Make "add a student from the grade details page" an all-or-nothing unit of
> work. Today the flow issues several separate HTTP requests (create student → link
> guardians → enrol), each with its own transaction. If any step fails mid-way, the
> student is orphaned with partial guardians, or the student exists but is not enrolled.
> Using the `IUnitOfWork<TContext>` pattern first built for teacher-create, collapse
> this into one atomic operation.
>
> **Branch target:** follow the teacher-UoW delivery (own stack on top of
> `stack/8-teacher-dialog-v4`).
> **Convention references:** `IUnitOfWork<TContext>` / `UnitOfWork<TContext>` in
> `SchoolCollab.Core` (teacher-create, committed), `CreateTeacherWithAssignmentsHandler`
> (the reference compound handler), `CreateStudentHandler` + `EnrollStudentHandler` +
> `CreateGuardianHandler` + `LinkGuardianToStudentHandler` + `AddContactHandler`
> (the single-step handlers being folded), `StudentCreateDialog.razor` +
> `GradeLevels/Detail.razor#OpenStudentCreateAsync` (the client orchestration).

---

## 0. Prompt-to-plan traceability

| Prompt requirement | Plan section | Resolution |
|---|---|---|
| Is Unit of Work documented in repo for adoption? | §1 | **No** — only the abstraction is committed; no adoption guide exists in `documents/solution/` |
| Plan UoW adoption for adding students (enrollment, guardians, contacts) from grade details | §2–§6 | `CreateStudentWithLinkedDataHandler` + `IUnitOfWork<StudentsDbContext>`; fold CreateStudent → guardians → contacts → enroll into one tx |
| First push current work to stacked PR | (done) | `stack/8-teacher-dialog-v4` pushed: teacher-UoW implementation `14449f6` |

---

## 1. Current-state finding — the student-add flow is NOT atomic (verified from repo)

The user asks whether Unit of Work is documented for adoption. Findings:

1. **The abstraction is committed; the adoption pattern is not.** `src/SchoolCollab.Core/Data/IUnitOfWork.cs`
   + `UnitOfWork.cs` are committed (from teacher-create). But `grep` for `IUnitOfWork|Unit of Work`
   across `documents/solution/` returns **nothing**. The reusable pattern is only described in the
   uncommitted `docs/plans/2026-08-teacher-create-unit-of-work.md`. There is no `documents/solution/unit-of-work-pattern.md`
   for future adopters.

2. **The live "add student from grade detail" flow is multi-request and non-transactional.**
   Two layers, each a separate HTTP request → separate handler → separate `SaveChangesAsync`:
   - `StudentCreateDialog.OnSaveAsync`:
     1. `Api.CreateStudentAsync` → `POST /students` → `CreateStudentHandler`
     2. per guardian draft: `Api.CreateGuardianAsync` (if new) → `CreateGuardianHandler`, then
        `Api.LinkGuardianAsync` → `LinkGuardianToStudentHandler`
   - `GradeLevels/Detail.razor#OpenStudentCreateAsync` (after the dialog returns the new student id):
     3. `Api.EnrollStudentAsync` → `POST /enrollments` → `EnrollStudentHandler`
   - Contacts are not collected in this dialog today (added separately via the student view), so the
     initial scope is **student + guardians + enrollment**. Contacts are a documented follow-up that
     reuses the same command shape.

   **Failure mode (the disruption):** if guardian link #2 fails, the student + guardian #1 link are
   already committed (orphaned student with a partial guardian set). If enrollment fails after the
   dialog closes, the student exists but is not on the grade card — the exact "single disruption
   fails the whole process" problem. No rollback exists.

3. **Each single-step handler auto-commits.** `CreateStudentHandler` calls `repository.AddAsync`
   (which `SaveChangesAsync`), then enqueues the outbox `StudentCreated` event. `EnrollStudentHandler`
   validates active period + stream + enrollment specs, then `repository.AddAsync`. `CreateGuardianHandler`
   adds the guardian + name history. `LinkGuardianToStudentHandler` validates both sides + duplicate,
   then adds the link. Folding them into one handler means replicating their validation inline (or
   calling domain factories directly) and saving once.

4. **Outbox is persisted in its own transaction (not the UoW's).** `OutboxIntegrationEventPublisher`
   creates a short-lived `DbContext` via `IDbContextFactory` and calls `SaveChangesAsync` on it, so
   each `EnqueueAsync` commits immediately and **independently** of the `IUnitOfWork` data
   transaction. Consequence: enqueue `StudentCreated`/`StudentEnrolled`/`GuardianCreated` **after**
   the UoW commits (matching `CreateStudentHandler`), never inside `ExecuteAsync`'s action —
   enqueuing before the data `SaveChangesAsync` would commit the event row even if the data tx later
   rolls back, producing a phantom event.

**Conclusion:** no atomic path exists for student+guardians+enrollment. The `IUnitOfWork<TContext>`
abstraction is ready; this plan adopts it and (crucially) documents the pattern for future use cases.

---

## 2. The pattern — reuse `IUnitOfWork<TContext>` + a compound command

Same shape as teacher-create, but with a wider graph:

1. Reuse the existing `IUnitOfWork<StudentsDbContext>` and `UnitOfWork<TContext>` (already registered
   in `Students.Core/Extensions.cs`). **No new transaction infrastructure.**
2. Add a compound command `CreateStudentWithLinkedData` carrying the student demographics + guardian
   drafts + optional enrollment target (grade + period) + optional contacts.
3. Add `CreateStudentWithLinkedDataHandler` that:
   - requires tenant context;
   - pre-validates all reference ids (grade, period, stream, existing guardian ids) **before** tracking;
   - runs `IUnitOfWork<StudentsDbContext>.ExecuteAsync` and builds the full tracking graph
     (student + guardian(s) + name-history + guardian-links + contacts + enrollment) using the domain
     factory methods;
   - calls `SaveChangesAsync` **once**; the UoW commits only if nothing throws.
4. Expose `POST /students/with-linked-data` (or extend `/students` with a compound variant) and switch
   the grade-detail add flow to a single client call.

### Why this shape (consistent with teacher-create)

- **No new transaction infra** — the teacher-UoW `IUnitOfWork` is reused as-is.
- **Repositories aren't rewritten** — the compound handler uses the DbContext + domain factories
  directly (tracking-only), exactly like `CreateTeacherWithAssignmentsHandler`; single `SaveChangesAsync`.
- **Validation is preserved** — the active-period check, stream validation, and enrollment
  specification guards from `EnrollStudentHandler` run inside the handler (feature-flag aware), and
  reference-id checks run pre-tx so bad ids fail fast with 4xx domain exceptions.
- **Outbox events are enqueued after commit** — the publisher persists each enqueue in its own
  `DbContext`/transaction immediately (it does **not** ride the UoW tx), so events are enqueued
  after the UoW returns successfully, exactly as `CreateStudentHandler` already does. (Same
  pre-existing at-least-once gap: if the enqueue fails after commit, the event is lost — inherited
  from every single-step handler, not introduced here.)

---

## 3. Implementation steps

### 3.1 Compound command — `CQRS/Students/Commands/CreateStudentWithLinkedData/CreateStudentWithLinkedData.cs`

```csharp
public sealed record CreateStudentWithLinkedData(
    string FirstName, string LastName, DateOnly? DateOfBirth, Guid? GenderCodedValueId,
    Guid? TitleCodedValueId,
    GuardianDraft[]? Guardians = null,      // each: existing GuardianId? OR new demographics
    Guid? EnrollmentGradeLevelId = null,    // null = create without enrolling (grade-detail passes this)
    Guid? EnrollmentPeriodId = null,
    Guid? StreamCodedValueId = null,
    DateOnly? EnrolledOn = null,
    ContactDraft[]? Contacts = null) : ICommand;   // follow-up; reserved shape
```

Child records `GuardianDraft(RelationshipCodedValueId, Role, IsEmergencyContact, ActingGuardianId, ExistingGuardianId?, NewGuardian?...)`
and `ContactDraft(Channel, Value, Label, CountryCode, DisplayOrder)`.

> Note: `CreateStudent` currently takes a `StudentNumber` param that the handler ignores (it
> auto-generates via `STUDENT_CODE`). The compound command should **not** carry a student number —
> it is generated server-side, matching `CreateTeacherWithAssignments`.

### 3.2 Handler — `CreateStudentWithLinkedDataHandler`

Sketch (mirrors `CreateTeacherWithAssignmentsHandler`):

1. `tenantProvider.RequireTenantContext(...)`.
2. Pre-validate (outside tx, fast-fail to 4xx):
   - if `EnrollmentGradeLevelId` set → grade exists (`GradeLevelNotFoundException`);
   - if `EnrollmentPeriodId` set → matches the active period (reuse `EnrollStudentHandler`'s
     `PeriodNotOpenException` logic via `IActivePeriodProvider`);
   - if `StreamCodedValueId` set → stream valid for the grade (reuse `ValidateStreamAsync`);
   - if a `GuardianDraft.ExistingGuardianId` set → guardian exists (`GuardianNotFoundException`);
   - if enrollment validation feature flag on → run `ICompositeEnrollmentSpecification` guards.
3. `return await uow.ExecuteAsync(async (ctx, ct) => { ... }, ct)`:
   - `studentNumber = await entityCodeGenerator.GenerateAsync("STUDENT_CODE", ct)`;
   - `var student = Student.Create(...).WithTenant(...); ctx.Students.Add(student);`
   - for each guardian: if new, `Guardian.Create(...).WithTenant(...)` + `AddInitialNameHistory()` +
     `ctx.Guardians.Add`; then `StudentGuardian.Create(...).WithTenant(...); ctx.StudentGuardians.Add`;
   - for each contact: `Contact.Create(...).WithTenant(...); ctx.Contacts.Add`;
   - if enrollment target set: `StudentEnrollment.Create(...).WithTenant(...); ctx.StudentEnrollments.Add`;
   - **single** `await ctx.SaveChangesAsync(ct);`
   - return `student.Id`.
4. After the UoW returns (i.e. after the single commit): invalidate cache with **separate
   single-tag calls** — `await cache.RemoveByTagAsync("students", ct);` and
   `await cache.RemoveByTagAsync("contacts", ct);` (the `RemoveByTagAsync` API takes one tag per
   call; a `"guardians"` tag is not used anywhere — verify what tag guardian reads use, or skip
   it). Then enqueue `StudentCreated` / `StudentEnrolled` / `GuardianCreated` via
   `publisher.EnqueueAsync(...)` — after commit, **not** inside `ExecuteAsync`'s action, because
   the publisher persists each enqueue in its own `DbContext` immediately; enqueuing before the
   data `SaveChangesAsync` would leave a committed phantom event if the data tx then rolled back.

### 3.3 Endpoint — `StudentRoutes.cs`

`POST /students/with-linked-data` mapping `CreateStudentWithLinkedData` → `Guid`, with 404/409 error
mapping (GradeLevelNotFound, GuardianNotFound, PeriodNotOpen, StreamGradeMismatch, validation exceptions).

### 3.4 Client + dialog — switch the grade-detail add flow

- `StudentsApiClient.CreateStudentWithLinkedDataAsync(...)` + DTOs.
- `StudentCreateDialog.OnSaveAsync`: build guardian + contact drafts from the form and make a single
  call. **Remove** the per-guardian `CreateGuardianAsync`/`LinkGuardianAsync` loop.
- `GradeLevels/Detail.razor#OpenStudentCreateAsync`: pass the grade id (+ active period) into the
  compound request so enrollment is part of the same tx, eliminating the separate `EnrollStudentAsync`
  call and the "no active period" halfway state.

---

## 4. Rollback contract

| Disruption | Outcome |
|---|---|
| Bad grade / period / stream / guardian id | Pre-validation throws domain exception before tx → 4xx, nothing written |
| Duplicate guardian link in batch | Within-batch check (two drafts referencing the same existing guardian id) throws the existing `GuardianLinkAlreadyExistsException` before tracking → 4xx (endpoint already maps to 409), nothing written |
| Enrollment spec guard fails (age/gender/single-active) | Typed `EnrollmentValidationException` → rollback, student + guardians + contacts reverted |
| Transient Postgres fault during `SaveChangesAsync` | `IExecutionStrategy` retries; if still failing, tx rolls back → 5xx, nothing partial |

**Invariant:** after the compound call returns non-2xx, the student and all its linked data either all
exist or none exist.

---

## 5. Verification — integration tests

Extend `tests/SchoolCollab.Students.Tests.Integration` (same `ApiFactory` + Testcontainers Postgres +
`StubEntityCodeGenerator` already added for teacher-create):

1. **Happy path** — POST student + 2 guardians + enrollment target → assert student, 2 guardians,
   2 guardian-links, 1 enrollment all present.
2. **Disruption — missing guardian** — one guardian draft references a non-existent guardian id →
   assert 4xx and **no student / no guardian / no enrollment persisted** (rollback proof).
3. **Disruption — invalid enrollment** — bad grade id (or failing enrollment spec) mid-batch →
   assert 4xx and zero rows persisted.
4. **Disruption — duplicate guardian link** — batch contains a duplicate student↔guardian link →
   assert 409 and zero rows persisted.

---

## 6. Out of scope / follow-ups

- **Contacts in the grade-detail add flow.** The dialog doesn't collect contacts today. The compound
  command reserves a `ContactDraft[]` shape; wiring the UI to collect contacts is a separate change.
- **Edit path atomicity.** `StudentEditDialog` + guardian/contact reconciliation has the same
  multi-request shape; a follow-up `UpdateStudentWithLinkedData` compound command would cover it.
- **Generalize across modules.** The `Assignments` and `Settings` contexts can register their own
  `IUnitOfWork<TContext>` for their multi-step commands.

---

## 7. Documentation for adoption (the original question)

Add `documents/solution/unit-of-work-pattern.md` — a reusable pattern doc (per the repo convention
that pattern docs live in `documents/solution/`, e.g. `cqrs-organization-pattern.md`). It should:
- state **when** to use UoW (multi-step compound create/update that must be atomic — create-teacher,
  create-student+linked-data, publish-assignment fan-out);
- show the `IUnitOfWork<TContext>` contract + registration;
- show the compound-handler shape (pre-validate refs → `ExecuteAsync` → build graph → single
  `SaveChangesAsync` → [after the UoW returns] cache invalidation → outbox events);
- list the pitfalls (repositories auto-save — don't call them inside the action; single
  `SaveChangesAsync`; pre-validate before tracking; the outbox publisher persists each enqueue in
  its own `DbContext` immediately and does **not** participate in the UoW transaction — enqueue
  AFTER the UoW commits, never inside `ExecuteAsync`'s action or a data-tx rollback leaves a
  committed phantom event; cache invalidation is non-transactional — do it after commit).

---

## 8. Files touched

| New / edit | Path |
|---|---|
| new | `documents/solution/unit-of-work-pattern.md` |
| new | `src/Students/SchoolCollab.Students.Core/CQRS/Students/Commands/CreateStudentWithLinkedData/CreateStudentWithLinkedData.cs` |
| new | `.../CreateStudentWithLinkedDataHandler.cs` |
| edit | `src/Students/SchoolCollab.Students.Api/Endpoints/StudentRoutes.cs` — `POST /students/with-linked-data` |
| edit | `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs` — `CreateStudentWithLinkedDataAsync` + DTOs |
| edit | `src/Students/SchoolCollab.Students.Application/Components/Students/StudentCreateDialog.razor` — single call |
| edit | `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor` — pass grade/period into the compound call |
| new | `tests/SchoolCollab.Students.Tests.Integration/CreateStudentWithLinkedDataEndpointTests.cs` |