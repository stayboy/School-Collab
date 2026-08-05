# Architecture Reference

## Project Structure & Naming

Each bounded context lives under `src/{Context}/` and contains exactly these
projects:

| Project | Purpose | Dependencies |
|---|---|---|
| `SchoolCollab.{Context}.Core` | Domain, data, CQRS, messaging | EF Core, Npgsql, HybridCache, Scrutor |
| `SchoolCollab.{Context}.Contracts` | Integration events (plain records) | None (shared contract library) |
| `SchoolCollab.{Context}.Api` | Minimal API endpoints | Core, Aspire defaults |
| `SchoolCollab.{Context}.Application` | Blazor SSR admin pages | Core DTOs via ApiClient |
| `SchoolCollab.{Context}.Worker` | Background services | Core, Aspire defaults |
| `SchoolCollab.{Context}.Tests.Unit` | Unit tests | Core |

### Namespace Convention

```
SchoolCollab.{Context}.Core.CQRS
SchoolCollab.{Context}.Core.Commands.{Action}
SchoolCollab.{Context}.Core.Queries.{Action}
SchoolCollab.{Context}.Core.DTOs
SchoolCollab.{Context}.Core.Domain
SchoolCollab.{Context}.Core.Domain.Events
SchoolCollab.{Context}.Core.Domain.Exceptions
SchoolCollab.{Context}.Core.Domain.Enums
SchoolCollab.{Context}.Core.Data
SchoolCollab.{Context}.Core.Data.Configurations
SchoolCollab.{Context}.Core.Data.Repositories
SchoolCollab.{Context}.Core.Messaging
SchoolCollab.{Context}.Core.Caching
```

### No Cross-Context References

Contexts communicate exclusively through MassTransit integration events:

```
Core → publishes IDomainEvent internally
Core → OutboxIntegrationEventPublisher serializes to OutboxMessage
OutboxDispatcher → publishes to RabbitMQ
Contracts → defines the integration event record (plain POCO)
Other context → subscribes via MassTransit consumer
```

Never add a project reference from one context's Core to another context's
Core or Contracts project.

## Central Package Management

All package versions are in `Directory.Packages.props`. Project `.csproj` files
must NOT include `Version` on `PackageReference` entries. Only `PrivateAssets`
and other metadata stay in the `.csproj`.

Adding a new package:

1. Add `<PackageVersion Include="Package" Version="x.y.z" />` to
   `Directory.Packages.props`
2. Add `<PackageReference Include="Package" />` (no Version) in the `.csproj`

## Required Package References

### Core Project
- `Microsoft.EntityFrameworkCore.Npgsql`
- `Npgsql.EntityFrameworkCore.PostgreSQL`
- `Microsoft.EntityFrameworkCore.Design` (PrivateAssets="all")
- `Microsoft.Extensions.Caching.Hybrid`
- `Scrutor`
- `RabbitMQ.Client` (for messaging)

### API Project
- `Microsoft.AspNetCore.App` (framework reference)
- Aspire service defaults

### Admin Project
- `Microsoft.FluentUI.AspNetCoreComponents`
- `Microsoft.AspNetCore.Components.Web`

### Worker Project
- `Microsoft.Extensions.Hosting`
- Aspire service defaults
- RabbitMQ client

## ServiceDefaults

`SchoolCollab.ServiceDefaults/Extensions.cs` provides:

- `AddServiceDefaults()` — Serilog, OTLP, health checks, service discovery
- `MapDefaultEndpoints()` — health check endpoints

**Every** `Program.cs` must call `builder.AddServiceDefaults()` as the **first**
service registration. Never add `builder.Logging.AddConsole()` or
`builder.Logging.AddOpenTelemetry()` — these are centralized in ServiceDefaults.

## Logging Rules

| Level | When to use |
|---|---|
| `LogTrace` | Fine-grained internal steps (dev only) |
| `LogDebug` | Inputs/outputs of queries and commands |
| `LogInformation` | Significant business events |
| `LogWarning` | Recoverable issues, validation failures |
| `LogError` | Unhandled exceptions, infrastructure failures |

Always pass the exception object as the first argument to `LogError`/`LogWarning`:
```csharp
_logger.LogError(ex, "Failed to create {Entity} {Id}", nameof(Student), id);
```

## Aspire AppHost Wiring Order

Resources must be wired in dependency order:

1. Infrastructure (PostgreSQL, RabbitMQ, Redis)
2. Migration services (with `WaitForCompletion()`)
3. API projects (wait for migration, reference infra)
4. Worker projects (wait for migration, reference infra)
5. Admin project (reference all API projects, wait for them)

Each context gets its own PostgreSQL database. The Aspire naming convention is:
- Database resource: `{context}-db`
- API project: `{context}-api`
- Worker project: `{context}-worker`
- Admin service discovery URI: `https+http://{context}-api`

## Migration Service

Each context has a `MigrationService` project that runs EF migrations at
startup. It references the Core project and uses the `IDesignTimeDbContextFactory`.
In Aspire, it's added as a project with `WaitForCompletion()`.

## Cache Pattern

Use `HybridCache` with tag-based invalidation:

```csharp
// GetOrCreateAsync with factory
var result = await cache.GetOrCreateAsync(
    $"students:list",
    static async (_, ct) => await repo.ListAsync(ct),
    cancellationToken: ct);

// Invalidate by tag
await cache.RemoveByTagAsync("students");
```

Cache keys for search queries use SHA256 hashing via `CacheKeyHelper.Hash()`.