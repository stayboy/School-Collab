# Unit of Work Pattern

This document defines the standard pattern for implementing atomic multi-step operations in the SchoolCollab solution using the Unit of Work (UoW) pattern. The goal is to ensure that compound operations — such as creating a teacher with grade/activity assignments, or creating a student with linked guardians and enrollment — succeed or fail as a single atomic unit, leaving no orphaned entities or partial state.

It is **mandatory** for any compound command that modifies multiple entities in a way that would leave inconsistent state if any step fails.

## 1. Scope

This pattern applies to:

- **Compound create operations**: `CreateTeacherWithAssignments`, `CreateStudentWithLinkedData` — where a root entity and its related entities must all persist together
- **Compound update operations**: Future `UpdateStudentWithEnrollment`, `ReconcileTeacherAssignments` — where multiple related entities must be updated atomically
- **Cross-entity fan-out operations**: Future `PublishAssignmentToAllGrades` — where an operation affects multiple aggregates that must all succeed

This pattern does **not** apply to:

- **Single-entity CRUD**: Use the existing repository `AddAsync`/`UpdateAsync`/`DeleteAsync` methods which auto-save
- **Read-only queries**: Use the existing repository query methods or direct `DbContext` access
- **Background outbox dispatch**: The `OutboxDispatcher` uses its own transaction pattern (see [`messaging-consolidation-plan.md`](./messaging-consolidation-plan.md))

## 2. The IUnitOfWork Abstraction

The UoW pattern is implemented via a thin abstraction in `SchoolCollab.Core`:

```csharp
// src/SchoolCollab.Core/Data/IUnitOfWork.cs
public interface IUnitOfWork<out TContext> where TContext : ModuleDbContext
{
    /// <summary>
    /// Executes <paramref name="action"/> inside a single EF Core transaction with
    /// the <see cref="IExecutionStrategy"/> retry for transient database faults.
    /// <paramref name="action"/> tracks entities on the context and must call
    /// <c>SaveChangesAsync</c> — the UoW commits the transaction only if the action
    /// returns without throwing. Any exception rolls back the whole batch.
    /// </summary>
    Task<TResult> ExecuteAsync<TResult>(
        Func<TContext, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default);
}
```

### Concrete Implementation

```csharp
// src/SchoolCollab.Core/Data/UnitOfWork.cs
public sealed class UnitOfWork<TContext> : IUnitOfWork<TContext>
    where TContext : ModuleDbContext
{
    private readonly TContext _context;

    public UnitOfWork(TContext context) => _context = context;

    public Task<TResult> ExecuteAsync<TResult>(
        Func<TContext, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        return _context.Database.CreateExecutionStrategy().ExecuteTransactionAsync(
            async ct =>
            {
                var result = await action(_context, ct);
                await _context.SaveChangesAsync(ct);
                return result;
            },
            cancellationToken);
    }
}
```

### Registration

Register the UoW in each bounded context's `Extensions.Add...Core` method:

```csharp
// src/Students/SchoolCollab.Students.Core/Extensions.cs
public static IServiceCollection AddStudentsCore(this IServiceCollection services)
{
    // ... other registrations ...
    services.AddScoped<IUnitOfWork<StudentsDbContext>, UnitOfWork<StudentsDbContext>>();
    return services;
}
```

## 3. Compound Handler Shape

A compound command handler using UoW follows this structure:

```csharp
public sealed class CreateStudentWithLinkedDataHandler(
    IUnitOfWork<StudentsDbContext> uow,
    IEntityCodeGenerator entityCodeGenerator,
    StudentsDbContext db,
    HybridCache cache,
    ITenantProvider tenantProvider,
    IGradeLevelRepository gradeLevelRepository,
    IIntegrationEventPublisher publisher,
    ILogger<CreateStudentWithLinkedDataHandler> logger)
    : ICommandHandler<CreateStudentWithLinkedData, Guid>
{
    public async Task<Guid> HandleAsync(
        CreateStudentWithLinkedData command,
        CancellationToken cancellationToken = default)
    {
        // 1. Require tenant context (FR-4)
        tenantProvider.RequireTenantContext(nameof(CreateStudentWithLinkedData), typeof(Student));

        // 2. Pre-validate reference IDs BEFORE tracking anything
        //    - Bad IDs fail fast with domain exceptions (mapped to 4xx)
        //    - Enrollment preconditions (period open, stream matches grade)
        await ValidateReferencesAsync(command, cancellationToken);
        Guid? enrollmentPeriodId = await ValidateEnrollmentTargetAsync(command, cancellationToken);

        // 3. Capture integration events to enqueue AFTER commit
        //    (outbox publisher persists in its own DbContext — not part of UoW tx)
        var studentCreatedEvents = new List<StudentCreated>();
        var studentEnrolledEvents = new List<StudentEnrolled>();

        // 4. Execute the entire operation in a single transaction
        var studentId = await uow.ExecuteAsync(async (ctx, ct) =>
        {
            // 4a. Generate entity code (e.g., student number)
            var studentNumber = await entityCodeGenerator.GenerateAsync("STUDENT_CODE", ct);
            if (await ctx.Students.AnyAsync(s => s.StudentNumber == studentNumber, ct))
                throw new DuplicateStudentNumberException(studentNumber);

            // 4b. Create and track the root entity
            var student = Student.Create(
                    studentNumber,
                    command.FirstName,
                    command.LastName,
                    command.DateOfBirth,
                    command.GenderCodedValueId,
                    command.TitleCodedValueId)
                .WithTenant(tenantProvider);
            ctx.Students.Add(student);

            // 4c. Create and track related entities (guardians, links, contacts, enrollment)
            foreach (var draft in command.Guardians ?? [])
            {
                var guardianId = draft.ExistingGuardianId is { } existingId
                    ? existingId
                    : AddNewGuardian(ctx, draft);

                ctx.StudentGuardians.Add(
                    StudentGuardian.Create(
                            student.Id,
                            guardianId,
                            draft.Role,
                            draft.RelationshipCodedValueId,
                            draft.IsEmergencyContact,
                            draft.ActingGuardianId)
                        .WithTenant(tenantProvider));
            }

            // 4d. Capture domain events raised during entity construction
            studentCreatedEvents.AddRange(student.GetDomainEvents().OfType<StudentCreated>());
            // ... capture other events ...

            // 4e. Single SaveChangesAsync — UoW commits the transaction
            await ctx.SaveChangesAsync(ct);

            return student.Id;
        }, cancellationToken);

        // 5. AFTER commit: invalidate cache (non-transactional)
        await cache.RemoveByTagAsync("students", cancellationToken);

        // 6. AFTER commit: enqueue integration events
        //    OutboxIntegrationEventPublisher.EnqueueAsync persists each event in its own
        //    DbContext immediately — it does NOT participate in the UoW transaction.
        //    Enqueuing inside ExecuteAsync would leave phantom events if the data tx
        //    later rolled back.
        foreach (var evt in studentCreatedEvents)
            await publisher.EnqueueAsync(evt, cancellationToken);
        foreach (var evt in studentEnrolledEvents)
            await publisher.EnqueueAsync(evt, cancellationToken);

        logger.LogInformation(
            "Student {StudentId} created with number {StudentNumber} for tenant {TenantId} with {GuardianCount} guardian(s) and {EnrollmentCount} enrollment",
            studentId, studentNumber, tenantProvider.TenantId,
            command.Guardians?.Length ?? 0,
            command.EnrollmentGradeLevelId is not null ? 1 : 0);

        return studentId;
    }
}
```

## 4. Key Rules and Pitfalls

### DO:

- **Pre-validate before tracking**: Check all reference IDs (grade levels, guardians, periods) exist and are valid BEFORE calling `uow.ExecuteAsync`. This ensures bad input fails fast with domain exceptions (mapped to 4xx) rather than mid-transaction.
- **Use domain factory methods**: Always create entities via `Entity.Create(...).WithTenant(tenantProvider)` to enforce domain invariants.
- **Single SaveChangesAsync**: Call `SaveChangesAsync` exactly once inside the `ExecuteAsync` action. The UoW commits the transaction after the action returns successfully.
- **Capture events, enqueue after**: Collect domain/integration events during the action, but enqueue them AFTER the UoW returns. The outbox publisher persists each enqueue in its own DbContext — it does NOT ride the UoW transaction.
- **Invalidate cache after commit**: Cache invalidation is non-transactional. Do it after the UoW returns successfully.

### DO NOT:

- **Call repository auto-save methods inside the action**: Repository methods like `AddAsync`, `UpdateAsync`, `DeleteAsync` call `SaveChangesAsync` immediately. Using them inside `ExecuteAsync` would commit partial state. Instead, use the `DbContext` directly to track entities (`ctx.Entities.Add(entity)`) and call `SaveChangesAsync` once at the end.
- **Enqueue outbox events inside the action**: The `OutboxIntegrationEventPublisher` persists each `EnqueueAsync` call in its own DbContext via `IDbContextFactory`. If you enqueue inside `ExecuteAsync` and the data transaction later rolls back, the outbox event remains committed (phantom event). Always enqueue AFTER the UoW returns.
- **Assume ambient transaction scope**: The UoW is explicit, not ambient. It only wraps the `ExecuteAsync` action. Code outside the lambda is not in the transaction.
- **Use for cross-request operations**: The UoW only works within a single request. Do not attempt to span multiple HTTP requests with a single UoW.

## 5. Error Handling and Rollback

When any exception is thrown inside the `ExecuteAsync` action:

1. EF Core's `IExecutionStrategy` retries on transient database faults (e.g., Postgres serialization failures)
2. If the exception is not transient, the transaction is rolled back
3. No entities are persisted — the entire batch is atomic
4. The exception propagates to the handler, which can map it to an appropriate HTTP response

Example error mapping in the endpoint:

```csharp
group.MapPost("/with-linked-data", async (
    [FromBody] CreateStudentWithLinkedData command,
    [FromServices] ICommandHandler<CreateStudentWithLinkedData, Guid> handler,
    CancellationToken ct) =>
{
    try
    {
        var id = await handler.HandleAsync(command, ct);
        return Results.Created($"/students/{id}", new { id });
    }
    catch (GradeLevelNotFoundException ex) { return Results.NotFound(new { ex.Message }); }
    catch (GuardianNotFoundException ex) { return Results.NotFound(new { ex.Message }); }
    catch (PeriodNotOpenException ex) { return Results.Conflict(new { ex.Message }); }
    catch (GuardianLinkAlreadyExistsException) { return Results.Conflict(new { message = "A link already exists between this student and guardian." }); }
    catch (DuplicateStudentNumberException ex) { return Results.Conflict(new { ex.Message }); }
    catch (EnrollmentValidationException ex) { return Results.Conflict(new { ex.Message }); }
});
```

## 6. Testing

Compound UoW commands require integration tests that prove the all-or-nothing guarantee:

```csharp
[TestMethod]
public async Task Disruption_RollsBackEntireBatch()
{
    var tenant = ApiFactory.TestTenantA;
    var grade = await SeedGradeLevelAsync(tenant, 1, "Grade 1");
    var period = await SeedActivePeriodAsync(tenant, "Term 2026");

    // Disruption: pass a non-existent guardian ID mid-batch
    var response = await PostCreateAsync(tenant, new
    {
        FirstName = "Jane",
        LastName = "Doe",
        DateOfBirth = new DateOnly(2015, 1, 1),
        GenderCodedValueId = Guid.NewGuid(),
        Guardians = new[]
        {
            new { ExistingGuardianId = (Guid?)Guid.NewGuid(), Role = GuardianRole.Primary } // doesn't exist
        },
        EnrollmentGradeLevelId = grade
    });

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);

    // Assert: zero rows persisted — entire batch rolled back
    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
    (await db.Students.AnyAsync()).Should().BeFalse();
    (await db.Guardians.AnyAsync()).Should().BeFalse();
    (await db.StudentEnrollments.AnyAsync()).Should().BeFalse();
}
```

## 7. Files Touched (Reference)

| New / edit | Path |
|---|---|
| new | `src/SchoolCollab.Core/Data/IUnitOfWork.cs` |
| new | `src/SchoolCollab.Core/Data/UnitOfWork.cs` |
| new | `src/Students/SchoolCollab.Students.Core/CQRS/Students/Commands/CreateStudentWithLinkedData/CreateStudentWithLinkedData.cs` |
| new | `.../CreateStudentWithLinkedDataHandler.cs` |
| edit | `src/Students/SchoolCollab.Students.Api/Endpoints/StudentRoutes.cs` — `POST /students/with-linked-data` |
| edit | `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs` — `CreateStudentWithLinkedDataAsync` + DTOs |
| edit | `src/Students/SchoolCollab.Students.Application/Components/Students/StudentCreateDialog.razor` — single call |
| new | `tests/SchoolCollab.Students.Tests.Integration/CreateStudentWithLinkedDataEndpointTests.cs` |

## 8. Related Patterns

- [`cqrs-organization-pattern.md`](./cqrs-organization-pattern.md) — Command/Query folder structure
- [`endpoint-organization-pattern.md`](./endpoint-organization-pattern.md) — API endpoint specialties
- [`shared-kernel-extraction-pattern.md`](./shared-kernel-extraction-pattern.md) — What belongs in `SchoolCollab.Core`
- [`messaging-consolidation-plan.md`](./messaging-consolidation-plan.md) — Outbox event dispatch pattern
