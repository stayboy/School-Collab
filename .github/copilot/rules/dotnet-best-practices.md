# .NET/C# Best Practices (School-Collab)

Read this **before writing or editing any `.cs` or `.razor` code-behind** in this repo.
It is the load-bearing, one-screen version of the authoritative skill
`.github/skills/dotnet-best-practices/SKILL.md` — read that skill for the "why" and the
full pattern catalogue. If this file and the skill disagree, the skill is the reference.

## Must do

- **CQRS**: handlers implement the repo's own `ICommandHandler<T,R>` / `IQueryHandler<T,R>`
  from `src/SchoolCollab.Core/CQRS/`. Handler names are imperative (`CreateGradeLevelHandler`).
  Never introduce MediatR or a `CommandHandler<TOptions>` base.
- **Primary constructors** for constructor injection
  (`sealed class X(StudentsDbContext db) : ICommandHandler<...>`). No `MediatR`, no Autofac.
  Blazor DI uses `@inject` + readonly backing fields; `[Inject]` is tolerated only in the
  existing gate/dialog components in `Admin.Shared` and `Students.Application` (legacy).
- **Domain entities are factory-created**: `GradeLevel.Create(...)` — never `new GradeLevel()`
  outside its owning `.Core` assembly. Factories hold the invariants.
- **DTOs are records** (immutable, primary constructor, no public setters).
- **Minimal-API endpoints** in `{Context}.Api/Endpoints/*.cs` — no controllers.
- **Tenant isolation** on every tenant entity (query filter in `{Entity}Configuration`).
- **`AsNoTracking()`** on every read that returns a DTO.
- **Scripted `HttpMessageHandler`** for HTTP testing — not Moq/NSubstitute.
- **XML `<summary>` docs** on every public handler / CQRS record / endpoint request / entity.
- **Structured logging** with named placeholders via injected `ILogger<T>`.
- **Typed domain exceptions** in `{Context}.Core/Domain/Exceptions` — never throw
  `InvalidOperationException` from a handler.
- **Transactional outbox** for cross-context events — never publish to a bus from a handler.

## Never (fails review and CI)

- ❌ MediatR — `IMediator`, `using MediatR`, `MediatR.`, `CommandHandler<TOptions>`.
- ❌ `Microsoft.SemanticKernel`, `ResourceManager` localization, generic awesome-copilot guidance.
- ❌ `Console.WriteLine` in production code (only `Main` / test helpers).
- ❌ `new {DomainEntity}()` outside its owning `.Core` assembly.
- ❌ Raw SQL string concat — use EF / `FromSqlInterpolated`.
- ❌ Editing a checked-in EF migration — add a new one.
- ❌ `services.Add*()` inline in feature `Program.cs` — use `Add{Layer}()` extension methods.
- ❌ `async void` outside top-level event handlers.
- ❌ Cross-tenant reads without `SuppressTenantGuard()`.
- ❌ Moq/NSubstitute for HTTP mocking in tests — use `ScriptedHandler`.

## Enforcement

The checkable subset is enforced in CI by
`tests/SchoolCollab.ArchitectureTests.Unit/DotNetBestPracticesArchitectureTests.cs`
(no MediatR, no SemanticKernel, no `Console.WriteLine` in production code) and by the
repo `.editorconfig` (code style + analyzer severities). Treat build warnings seriously —
new code should be warning-free.