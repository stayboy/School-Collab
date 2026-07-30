---
name: dotnet-best-practices
description: |
  School-Collab-specific .NET/C# best practices. Replaces the generic
  awesome-copilot dotnet-best-practices guidance with the patterns this
  codebase actually uses: CQRS handlers (IQueryHandler<T,Q> / ICommandHandler<T,R>),
  primary constructors, factory-based entity creation, EF Core with tenant
  query filters, transactional outbox, FluentUI Blazor dialogs, scripted
  HttpMessageHandler tests instead of Moq. Use for every C# change in
  this repo. Triggers: "write a handler", "add a CQRS", "create entity",
  "test handler", "EF Core query", "primary constructor", "endpoint",
  "factory pattern", "tenant filter", "outbox", "dependency injection",
  "School-Collab conventions", "module structure".
---

# School-Collab .NET/C# Best Practices

Your task is to ensure .NET/C# code in `${selection}` follows the
patterns this codebase actually uses, NOT the generic Microsoft guidance
in awesome-copilot/dotnet-best-practices. The awesome-copilot version
references patterns this repo does not use (e.g. `Microsoft.SemanticKernel`,
`ResourceManager` localization, `Moq`, `CommandHandler<TOptions>`); using
them here will produce code that fails review or the build.

This skill is layered on top of the repo's other skills:
- **`bounded-context`** for the 6-project layout when adding a feature
- **`dialog-ui`** for Blazor dialogs
- **`fluentui-icons`** / **`fluentui-component-props`** for FluentUI usage
- **`coded-values`** for the coded-value system

Apply these rules whenever you touch C# in School-Collab.

## Documentation & Structure

- **XML docs are required on every public type, public method, and public
  property** that is part of an interface, an HTTP endpoint's request
  record, a domain entity, or a CQRS query/command record. Private
  helpers don't need them. `<summary>` is enough — full `<param>` /
  `<returns>` blocks are encouraged but not required.
- **Namespace layout**: `SchoolCollab.{Context}.{Layer}.{Feature}`.
  Layers: `Core` (domain + data + CQRS), `Api` (endpoints), `Admin`
  (Blazor components + pages), `Admin.Shared` (cross-admin RCL
  components), `Contracts` (integration events), `Workers` (background).
  Inside `Core`, CQRS handlers nest under `CQRS.{Feature}.{Verb}{Entity}`
  (`CQRS.GradeLevels.Commands.UpdateGradeLevel`,
  `CQRS.Subjects.Queries.ListSubjectsByGrade`). See `bounded-context`
  for the canonical `{Context}.Core/{CQRS,Domain,Data,Migrations}` shape.
- **File-per-type**. One class per `.cs` file, named to match the type.
  Razor files in `Admin/{Components,Pages}/` follow Blazor conventions
  (`MyComponent.razor` + optional `.razor.cs` code-behind).

## Design Patterns & Architecture

- **Primary constructors for dependency injection** — *this* codebase
  uses them throughout:
  `public sealed class UpdateGradeLevelHandler(StudentsDbContext db, ITenantContextAccessor tenants)`
  Don't add an explicit constructor body unless you actually need field
  capture for test substitution.
- **CQRS handlers via `ICommandHandler<T,R>` / `IQueryHandler<T,R>`**
  defined in `src/SchoolCollab.Core/CQRS/`. Commands return the new
  entity's `Guid Id`; queries return a `Dto[]` or `Dto?`. Handler class
  names are imperative: `CreateGradeLevelHandler`,
  `ListSubjectsByGradeHandler`. **Do NOT introduce `CommandHandler<TOptions>`
  or any other MediatR-style abstraction** — the repo deliberately uses
  its own tiny interfaces.
- **Domain entities are factory-created, never constructed with `new`**
  from outside the entity's own assembly. Use `GradeLevel.Create(...)`,
  `Subject.Create(...)`, `Period.Create(...)`. The factory is the place
  for invariants (e.g. start < end, required fields). `InternalsVisibleTo`
  to `SchoolCollab.{Context}.Tests.Unit` exposes the internal factory +
  setters to tests.
- **DTOs are records** (immutable projections of domain entities). Use
  the record's primary constructor — no public setters. EF Core mapping
  is configured in `Data/Configurations/{Entity}Configuration.cs`.
- **Endpoints are minimal-API lambdas** in `{Context}.Api/Endpoints/*.cs`,
  one static class per resource (`SubjectRoutes.cs`,
  `EnrollmentRoutes.cs`). Internal request/response records sit at the
  bottom of the same file. **No controllers in the API project.**
- **Interface segregation is real here**: `ICommandHandler<T,R>` and
  `IQueryHandler<T,R>` are separate interfaces; handler classes
  implement both via `class XCommandHandler(...) : ICommandHandler<...>`
  separately, never `ICommandHandlerAndQueryHandler`.
- **Tenant filter on every entity that carries a tenant** is set up
  in `{Entity}Configuration` via
  `builder.HasQueryFilter(e => e.TenantId == currentTenantId())`. The
  `ModuleDbContext` validates at startup that every entity type either
  has the filter or is on `GlobalEntityAllowList` (only `OutboxMessage`
  is global).

## Dependency Injection & Services

- **Primary constructor DI** (see above). No `[Inject]` attributes
  anywhere in the codebase — those don't exist in Blazor here.
- **Register services in `AddTenancy()` / `AddStudentsCore()` /
  `AddAuth()` / etc. extension methods** in the layer's root. Don't add
  `services.AddSingleton<...>()` calls inline in `Program.cs`.
- **Lifetimes**: DbContexts are Scoped (default). `ITenantProvider` /
  `ITenantContextAccessor` / `HybridCache` / `IActivePeriodProvider` are
  Singleton. Outbox publisher (`IIntegrationEventPublisher`) is Scoped.
  Background workers (`OutboxDispatcher<T>`) are Singleton and resolve
  their own scope per batch.
- **`Microsoft.Extensions.DependencyInjection`** is used directly. No
  Autofac, no Lamar, no Scrutor.
- **Blazor Admin uses constructor injection in `@code` blocks**:
  `@inject StudentsApiClient Api` then `private readonly StudentsApiClient _api`
  via the auto-generated backing field — never `@inject` inside methods.
  Services registered in `Program.cs` of the Admin project (`AddRazorPages`,
  `AddServerSideBlazor`, etc.) and discovered via `IServiceCollection` are
  resolved through `IServiceProvider` in `.razor.cs` code-behind.

## Resource Management & Localization

- **No `ResourceManager` in this repo** (yet). User-facing strings are
  hardcoded English in `.razor` files; per-tenant override text lives
  in the CodedValue system, not in satellite assemblies.
- **`IDisposable` / `IAsyncDisposable`** is required for any class
  that owns an `HttpClient`, `DbContext`, `IJSObjectReference`,
  `IDisposable` cache reference, or registered background subscription.
  Razor components implementing `IDisposable` must also
  `IDisposable.Dispose()` be implemented; `IAsyncDisposable` is
  preferred for components that own async resources.
- **Cancellation tokens are wired through every async call** that has
  an overload accepting one. `HandleAsync(query, CancellationToken ct)`
  passes `ct` to every `ToListAsync` / `SaveChangesAsync` /
  `SendAsync` / etc. **Do NOT swallow the token** — the integration
  test `CancelledToken_NeverReturnsServerError` pins this contract.

## Async/Await Patterns

- **`async Task` / `async Task<T>` everywhere**. No `async void` (only
  top-level event handlers, and even those use `async Task` +
  `try/catch`).
- **No `.Result` / `.Wait()` on Tasks**. If you find yourself wanting
  them, the call site is wrong — switch to `await`.
- **`ConfigureAwait(false)` is NOT used** in this codebase. The library
  targets `net10.0` and most code runs under ASP.NET Core / Blazor
  contexts where it makes no measurable difference.
- **Cancellation**: pass `ct` to every I/O call; the API layer registers
  `OperationCanceledException` → `499 Client Closed Request` (per
  `SubjectRoutes.cs` GET handlers); commands throw `OperationCanceledException`
  through their try/catch into `Results.NoContent()` or
  `Results.StatusCode(499)` depending on caller expectations.

## Testing Standards

- **`MSTest` + `FluentAssertions`** for both unit and integration
  tests. `[TestClass]` / `[TestMethod]` / `[ClassInitialize]` /
  `[TestCleanup]`. AAA layout with explicit `// Arrange / // Act / // Assert`
  comments on every test.
- **`ScriptedHandler` is the HTTP test double** (see
  `tests/SchoolCollab.Admin.Tests.Unit/DialogShellTests.cs` and
  `tests/SchoolCollab.Admin.Tests.Unit/SubjectDialogTests.cs`). It is
  a custom `HttpMessageHandler` with exact-match `(method, url)` lookup
  first, then ANY-wildcard fallback. **Do NOT use `Moq`** for HTTP —
  the repo deliberately avoids Moq because scripted handlers produce
  deterministic, inspectable call records (`handler.Calls`).
- **Unit tests** for handlers live in
  `tests/SchoolCollab.{Context}.Tests.Unit/{Verb}{Entity}Tests.cs` and
  use `StudentsTestScope` (or sibling `{Context}TestScope`) — a
  fixture that builds an `InMemoryDatabase`-backed `DbContext` with a
  real tenant context, `HybridCache`, and the entity repositories.
- **Integration tests** for endpoints live in
  `tests/SchoolCollab.{Context}.Tests.Integration/` and use
  `ApiFactory : WebApplicationFactory<Program>` with Testcontainers
  Postgres + RabbitMQ. Per-test `TRUNCATE TABLE ... CASCADE` cleanup.
  Tenant is stamped via the `x-tenant-id` header that
  `SchoolCollab.Core.Auth.TestAuthHandler` honors.
- **bUnit for Blazor components** —
  `tests/SchoolCollab.Admin.Tests.Unit/{ComponentName}Tests.cs` with
  `BunitContext` as base class. Use `Render<FluentDialogProvider>()` +
  `DialogService.ShowShellDialogAsync<TDialog, TModel, TResult>(...)`
  for dialog tests. Pump dispatcher via
  `for (i < 30) { await Task.Delay(100); cut.Render(); }`.
- **Pre-existing failing tests are NOT yours to fix**:
  `ActivityGroup*Tests.cs` in `tests/SchoolCollab.Students.Tests.Unit/`
  and `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentActivityGroupTests.cs`
  are TDD red-phase stubs for unimplemented domain entities. Always
  filter with `--filter "FullyQualifiedName!~ActivityGroup"` when
  running `dotnet test`. The pre-existing `GuardianGridTests` (2)
  failures in Admin.Tests.Unit are also unrelated to new work.
- **Test name pattern**: `Method_State_Expected` or
  `Method_Behavior_Expected` (e.g. `Cancelled_AlreadyCancelled_Throws_OperationCanceledException`,
  `WithExplicitPeriodId_FiltersToThatPeriod`). Sentences, not snake_case.

## Configuration & Settings

- **Strongly-typed options** bound via `services.Configure<TOption>(config.GetSection(...))`.
  Use `[Required]` / `[Range]` data annotations on the option POCO.
- **Per-feature setting records** live under
  `src/Settings/SchoolCollab.Settings.Core/Domain/` and are mapped to
  the `settings` Postgres schema via `EntityConfiguration` classes.
  Adding a new setting = new record + new `Configuration` + EF
  migration.
- **Coded values** (the bulk of "configuration" in this codebase) live
  in `src/Settings/SchoolCollab.Settings.Core/CQRS/CodedValues/`. Use
  the `CodedValuesApiClient` from Admin.Shared, never query coded
  values directly. See the `coded-values` skill for the full model.

## Error Handling & Logging

- **Domain exceptions** live in
  `src/{Context}/SchoolCollab.{Context}.Core/Domain/Exceptions/` as
  sealed classes extending `DomainException` base (see
  `DomainExceptions.cs`). Examples: `GradeLevelNotFoundException`,
  `ContactNotFoundException`, `ConcurrencyException`,
  `SubjectReferencedException`. **Always create a typed exception,
  never throw `InvalidOperationException` from a handler** — the
  endpoint layer maps known exceptions to HTTP status codes and an
  untyped throw becomes a 500.
- **Endpoint try/catch maps domain exceptions to HTTP responses** in
  `{Context}.Api/Endpoints/{Resource}Routes.cs`:
  - `ContactNotFoundException` / `{Entity}NotFoundException` →
    `Results.NotFound(new { ex.Message })`
  - `ConcurrencyException` → `Results.Conflict(new { ex.Message })`
  - `PeriodOverlapException` / validation exceptions →
    `Results.BadRequest(new { ex.Message })`
  - `OperationCanceledException` (when `ct.IsCancellationRequested`)
    → `Results.StatusCode(StatusCodes.Status499ClientClosedRequest)`
    (for GET endpoints with EF I/O)
  - `DbUpdateException` →
    `Results.Json(new { Message, Detail = ex.Message },
    statusCode: StatusCodes.Status503ServiceUnavailable)`
  - Everything else → `Results.Json(new { Message, Detail = ex.Message },
    statusCode: StatusCodes.Status500InternalServerError)`
  GET endpoints have no try/catch by default — add one ONLY when an EF
  or transport failure mode is reachable.
- **Logging**: `Microsoft.Extensions.Logging` via injected
  `ILogger<TCategory>`. Use structured logging with named placeholders:
  `Logger.LogError(ex, "Failed to load subjects for grade {GradeId}", gradeLevelId);`.
  Never `Console.WriteLine` in production code (only in `Main` and
  test helpers).

## EF Core & Database

- **PostgreSQL via Npgsql** with snake_case naming convention
  (`builder.ToTable("grade_levels")`). Migration files in
  `src/{Context}/SchoolCollab.{Context}.Core/Migrations/` are timestamped
  + descriptive (`20260729185904_AddEnrollmentValidationToGradeLevels.cs`).
  **Never edit a checked-in migration** — add a new one.
- **`AsNoTracking()` is mandatory on every read query** that returns a
  DTO. Without it, EF tracks every row in the change tracker and
  perf dies at scale.
- **Tenant-filter translation safety**: when joining two tenant-filtered
  entities, project the JOIN to ONE entity first (no DTO), apply
  `Distinct()` / `OrderBy()` / `ToArrayAsync()` on that single entity
  (so EF can translate to `SELECT DISTINCT ... ORDER BY ...`), then map
  to DTO in a client-side per-row projection. The InMemory provider does
  NOT enforce query filters, so unit tests pass for queries that
  Postgres refuses to translate — **every handler that joins
  tenant-filtered entities needs at least one integration test**
  (use `tests/SchoolCollab.Students.Tests.Integration/ApiFactory.cs`
  + Testcontainers pattern).
- **Transactional outbox**: every command that mutates state and needs
  to publish an integration event writes both in the same `DbContext.SaveChangesAsync`,
  via `IIntegrationEventPublisher.EnqueueAsync(...)`. The
  `OutboxDispatcher<T>` background worker drains the queue and
  publishes to RabbitMQ. Never publish directly to a bus from a handler.

## Performance & Security

- **C# 12+ / .NET 10 features are encouraged**: collection expressions
  `[]`, primary constructors, `required` keyword, file-scoped
  namespaces. Use them — they make the code shorter and more correct.
- **Parameterized queries via EF** — never raw SQL string concat.
  When raw SQL is needed, use `FromSqlInterpolated` (parameter binding
  is enforced).
- **Tenant isolation is a security boundary**: every entity read/write
  MUST go through the tenant filter. Cross-tenant reads are gated by
  `IAuthorizationService` policies (`TenantAccessHandler`) and
  `SuppressTenantGuard()` (only in design-time factories + outbox
  dispatcher).
- **No secrets in source code**. Use `IConfiguration` + environment
  variables (`ConnectionStrings__students-db`,
  `FeatureFlags__FEATURE__DisableOIDCAuth`). Per-tenant secrets live
  in KeyVault (see the `azure-keyvault-*` skills).

## Code Quality

- **SOLID principles are real, not slogans** in this codebase: the
  handler + entity + DTO + endpoint split enforces single-responsibility;
  tenant filters + scoped DbContexts enforce dependency inversion; the
  domain-factory pattern enforces Liskov; `ICommandHandler<T,R>` is
  interface segregation; bounded contexts are open-closed.
- **Avoid duplication via base classes only when 3+ implementations
  share the shape**. Handlers do NOT have a common base class — they
  share the `IQueryHandler<T,R>` interface but no method bodies. That's
  intentional: query handlers do too many different things to share
  logic.
- **Names reflect domain concepts, not Microsoft jargon**: `GradeLevel`,
  not `GradeLevelEntity`. `EnrollStudent`, not `CreateStudentEnrollmentCommand`.
  `CreateSubjectForGradeHandler`, not `CreateSubjectCommandHandler<T>`.
- **Methods focused and cohesive**: handlers are typically 30–80 lines.
  Anything over 150 lines is a smell — split into a sub-helper or
  extract a domain service.
- **Disposal**: every `HttpClient`, `HttpResponseMessage`, `IJSObjectReference`,
  `IDbContextTransaction`, `Stream` lives in a `using` block or is
  disposed via `IAsyncDisposable`.

## Cross-cutting

- **`{Context}.Core` grants `InternalsVisibleTo`** to
  `SchoolCollab.{Context}.Tests.Unit` only — not to Admin, not to Api,
  not to integration tests. Admin sees only public surface; tests
  see internal factories.
- **`Aspire.AppHost` orchestrates local dev** — every bounded context
  has its own service registration in
  `src/SchoolCollab.AppHost/Program.cs`. New service? Add it there too.
- **`FeatureFlagGate` / `TenantGate`** wrap conditional UI / API
  exposure. Use them instead of `if (featureFlag.Enabled)` checks
  inline. See `featureflags-tenant-gates` skill for the unified
  `GateBase` design.

## Anti-patterns (will fail review)

- ❌ `new MyEntity()` outside the entity's own assembly (use the factory).
- ❌ `Moq` / `NSubstitute` for HTTP testing (use `ScriptedHandler`).
- ❌ Throwing `InvalidOperationException` from a handler (use a typed
  domain exception).
- ❌ `Console.WriteLine` in production code.
- ❌ `[Inject]` attribute anywhere.
- ❌ `services.AddXxx()` calls inline in `Program.cs` of feature modules
  (use the extension methods in `AddTenancy()` / `Add{Core}()` etc.).
- ❌ Raw SQL string concat.
- ❌ Editing a checked-in EF migration.
- ❌ `async void` outside top-level event handlers.
- ❌ Cross-tenant reads without `SuppressTenantGuard()` (and even then,
  only in design-time factories or outbox dispatch).