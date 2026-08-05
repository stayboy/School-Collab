# CQRS Handler Test-Coverage Pattern

This document defines the **mandatory** test-coverage rule for every CQRS
**command** and **query** handler in the `*.Core` projects of the SchoolCollab
solution. It exists because a handler can pass its in-memory unit tests yet
throw at runtime on the real relational provider — a failure mode that is
silent in unit tests and only surfaces in the UI as an error or infinite
spinner.

It pairs with [`cqrs-organization-pattern.md`](./cqrs-organization-pattern.md)
(how handlers are organized) and
[`endpoint-organization-pattern.md`](./endpoint-organization-pattern.md) (how
handlers are exposed as HTTP endpoints). Every handler that is reachable from
an endpoint must be covered by the tests described here.

## 1. Scope

This rule applies to every CQRS-bearing `*.Core` project in `src/`, including:

- `SchoolCollab.Students.Core`
- `SchoolCollab.Assignments.Core`
- `SchoolCollab.CodedValues.Core`
- Any future bounded-context core project.

It does **not** apply to:

- `SchoolCollab.Core` (the shared kernel) — it holds cross-cutting primitives,
  not commands/queries.
- `*.Contracts` assemblies — they hold DTOs and event types.
- `*.Admin`, `*.Worker`, `*.Api`, `*.AppHost`, `*.MigrationService` projects.

## 2. Mandatory Rule: Every Handler Has a Unit Test

Every command handler (`ICommandHandler<,>`) and query handler
(`IQueryHandler<,>`) **must** have at least one unit test covering:

- the **happy path** (the primary behavior the handler exists to perform), and
- the **key edge cases** that the handler's own guard clauses and branching
  introduce (e.g. null/empty inputs, not-found, tenant scoping, withdrawn or
  inactive records, no-current-period).

A handler with no test file is a review blocker. New handlers are expected to
ship with their test file in the same change.

## 3. Mandatory Rule: Projecting / Joining / Ordering Handlers Get an Integration Test

Handlers whose query **projects into a custom DTO type, joins across entity
sets, or orders the result** must **also** have an integration test that runs
against **real Postgres** (via the `ApiFactory` Testcontainers harness in
`tests/SchoolCollab.Students.Tests.Integration`).

**Why:** the EF Core **InMemory** provider (used by unit tests) evaluates
queries client-side, so it does not exercise SQL translation. A query that
translates fine in-memory can throw `InvalidOperationException` ("could not be
translated") on the relational provider at runtime. Unit tests alone therefore
give a false sense of safety for any handler that projects, joins, or orders.

The integration test should hit the handler's HTTP endpoint (or invoke the
handler through the API) and assert the response shape and ordering, so a
translation regression fails the build rather than the UI.

## 4. Known Pitfall: Ordering After a Custom-Type Projection

A specific, recurring translation failure: applying `OrderBy` / `ThenBy`
**after** projecting into a **custom (non-anonymous, non-entity) DTO type** is
not translatable by the relational provider. EF Core treats the custom-type
projection as a terminal client projection and throws
`InvalidOperationException` at runtime, even though the InMemory provider
evaluates it client-side and passes.

**Correct pattern** — order on an anonymous projection first, then project
into the DTO last:

```csharp
var rows = await db.StudentEnrollments
    .AsNoTracking()
    .Where(se => se.GradeLevelId == query.GradeLevelId
              && se.PeriodId == periodId.Value
              && se.Status == EnrollmentStatus.Active)
    .Join(db.Students,
        se => se.StudentId,
        s => s.Id,
        (se, s) => new
        {
            s.Id,
            s.StudentNumber,
            s.FirstName,
            s.LastName,
            s.DateOfBirth,
            s.GenderCodedValueId,
            s.IsDeleted,
            s.CreatedAt,
            s.UpdatedAt
        })
    .OrderBy(x => x.LastName)
    .ThenBy(x => x.FirstName)
    .Select(x => new StudentDto(
        x.Id, x.StudentNumber, x.FirstName, x.LastName,
        x.DateOfBirth, x.GenderCodedValueId, x.IsDeleted,
        x.CreatedAt, x.UpdatedAt))
    .ToArrayAsync(ct);
```

**Anti-pattern** (throws on Postgres, passes on InMemory):

```csharp
// OrderBy/ThenBy applied AFTER projecting into the custom StudentDto type.
.Join(db.Students, se => se.StudentId, s => s.Id,
    (se, s) => new StudentDto(/* ... */))
.OrderBy(s => s.LastName)      // not translatable on the relational provider
.ThenBy(s => s.FirstName)
```

## 5. Verification

- Every handler in `*.Core` has a unit test file (happy path + key edge cases).
- Every projecting / joining / ordering handler has an integration test in
  `tests/SchoolCollab.Students.Tests.Integration` that runs against Postgres.
- The integration suite passes against the Testcontainers Postgres harness
  (not just the InMemory unit suite).
