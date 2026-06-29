# Shared Kernel Extraction Pattern

This document defines the standard pattern for identifying and extracting
**duplicated** types and interfaces into the shared kernel
(`SchoolCollab.Core`), so they can be reused by every bounded context
(`SchoolCollab.Students.Core`, `SchoolCollab.Assignments.Core`,
`SchoolCollab.CodedValues.Core`, and any future `<Domain>.Core` project).

It is **mandatory** for every `*.Core` project under `src/`.

It pairs with [`endpoint-organization-pattern.md`](./endpoint-organization-pattern.md)
and [`cqrs-organization-pattern.md`](./cqrs-organization-pattern.md).

## 1. The Shared Kernel: `SchoolCollab.Core`

`SchoolCollab.Core` is the only project in the solution that is allowed to
contain types referenced by more than one `*.Core` project. It already
hosts the cross-cutting primitives the rest of the solution depends on:

```
src/SchoolCollab.Core/
├── Auth/         (AuthTenancyExtensions, TenantClaimsTransformation, TestAuthHandler)
├── Data/         (IEntity, ModuleDbContext, EntityTypeConfigurationBase, ...)
├── Features/     (IFeatureFlagService, FeatureFlagConfigurationExtensions, ...)
├── Identity/     (User)
├── Tenancy/      (ITenantProvider, BaseTenantEntity, TenancyServiceExtensions, ...)
├── CQRS/         (ICommand, IQuery, ICommandHandler<>, ICommandHandler<,>, IQueryHandler<,>)
└── Messaging/    (IIntegrationEventPublisher, OutboxMessage)
```

The two new groups added by this pattern — `CQRS/` and `Messaging/` — are
named to match the rest of the kernel: a single PascalCase word, no
`<Domain>` prefix, no sub-folder for the abstractions.

## 2. What Belongs in the Shared Kernel

A type belongs in `SchoolCollab.Core` only when **all three** of the
following are true:

1. **Byte-identical semantics across every consumer.** The body, signatures,
   constraints, and XML doc comments are exactly the same in every
   `<Domain>.Core` project that defines it.
2. **Multiple consumers exist today.** At least two `<Domain>.Core` projects
   already have a local copy of the type. Single-consumer types stay local
   (rule of three: extract on the second duplicate, not the first).
3. **It does not depend on any domain type.** A shared type must not import
   `SchoolCollab.<Domain>.Core.*` namespaces or hold a `<Domain>DbContext`
   reference. It is allowed to depend on other shared types in
   `SchoolCollab.Core`.

If any of these fail, keep the type local. The next section explains the
concrete shape of these checks.

## 3. The "True Duplicate" Test

Two files in two different `<Domain>.Core` projects are **not necessarily
duplicates** just because they share a name. Before extracting, verify:

| Check | How to verify | Example of "looks like, isn't" |
|---|---|---|
| Same body | Diff the two files (excluding `namespace` lines and `using` directives) | `IIntegrationEventPublisher` in Assignments has `PublishAsync(object)`; in Students + CodedValues it has `EnqueueAsync<T>(T) where T : class` |
| Same field/method set | List public members on both | `OutboxMessage` in Assignments has `CreatedAt` + `ProcessedAt`; in Students + CodedValues (and now the shared kernel) it has `OccurredAt` + `DispatchedAt` + `Attempts` + `LastError` |
| Same constraints | Compare generic constraints side-by-side | n/a in current code, but a `where T : class` vs `where T : notnull` is a real semantic difference |
| Same XML doc / contract intent | Read the doc comments | If one says "publishes to RabbitMQ synchronously" and the other says "writes to outbox for async delivery", the contract is different even if the method signatures match |

**When in doubt, do not extract.** A "consolidate later" cleanup PR is
cheap; reverting an extract that broke a consumer is not.

## 4. Folder and Namespace Conventions

Inside `SchoolCollab.Core`, each shared concern gets its own top-level
group folder with the same name as its namespace segment:

| Group | Folder | Namespace | Files |
|---|---|---|---|
| CQRS abstractions | `CQRS/` | `SchoolCollab.Core.CQRS` | `ICommand.cs`, `ICommandHandler.cs` (both arities), `IQuery.cs`, `IQueryHandler.cs` |
| Messaging primitives | `Messaging/` | `SchoolCollab.Core.Messaging` | `IIntegrationEventPublisher.cs`, `OutboxMessage.cs` |
| Tenancy primitives | `Tenancy/` | `SchoolCollab.Core.Tenancy` | `ITenantProvider.cs`, `BaseTenantEntity.cs`, ... |
| Data primitives | `Data/` | `SchoolCollab.Core.Data` | `IEntity.cs`, `ModuleDbContext.cs`, ... |
| Auth primitives | `Auth/` | `SchoolCollab.Core.Auth` | `AuthTenancyExtensions.cs`, ... |

### Folder rules

- **One folder per concern, directly under the project root.** The folder
  name is the namespace segment. Do **not** add a `Common/` or `Shared/`
  super-folder.
- **No sub-folders inside a group folder.** Each abstraction lives at the
  top level of its group folder. Do not split by arity, by feature, or by
  any other axis.
- **One file per public type.** Match the file name to the type name
  exactly (e.g. `ICommandHandler.cs` contains `ICommandHandler<>` and
  `ICommandHandler<,>` — both arities are in one file because they form
  one cohesive abstraction).
- **No domain types leak in.** A shared file may not import any
  `SchoolCollab.<Domain>.Core.*` namespace. If the abstraction needs a
  type from a domain (e.g. a generic constraint referencing a domain base
  class), it does not belong in the kernel.

## 5. Naming

| Element | Convention | Example |
|---|---|---|
| Group folder | PascalCase, no `<Domain>` prefix | `CQRS`, `Messaging`, `Tenancy` |
| Abbreviation file name | `<TypeName>.cs` (no `I` prefix stripped) | `ICommandHandler.cs` |
| Abbreviation namespace | `SchoolCollab.Core.<Group>` | `SchoolCollab.Core.CQRS` |
| Shared type public name | Same as the local duplicate's name | `ICommand`, `OutboxMessage` |

## 6. Consumer Update Rules

When a type moves to `SchoolCollab.Core`, every consumer must update its
`using` directives and any fully-qualified references:

| Before | After |
|---|---|
| `using SchoolCollab.<X>.Core.CQRS;` | `using SchoolCollab.Core.CQRS;` |
| `using SchoolCollab.<X>.Core.Messaging;` | `using SchoolCollab.Core.Messaging;` |
| `SchoolCollab.<X>.Core.CQRS.ICommandHandler<T>` (fully qualified) | `SchoolCollab.Core.CQRS.ICommandHandler<T>` (or just `ICommandHandler<T>` with the right `using`) |
| `SchoolCollab.<X>.Core.CQRS.IQueryHandler<T, R>` (fully qualified) | `SchoolCollab.Core.CQRS.IQueryHandler<T, R>` (or just `IQueryHandler<T, R>`) |
| `namespace SchoolCollab.<X>.Core.CQRS;` (a file whose namespace is exactly the local CQRS root) | **Delete the file** — the abstractions no longer live here |

`Extensions.cs` files that previously had:
```csharp
.AddClasses(classes => classes.AssignableTo(typeof(CQRS.ICommandHandler<>)), publicOnly: false)
```
should drop the `CQRS.` prefix and rely on the `using`:
```csharp
.AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<>)), publicOnly: false)
```

## 7. When NOT to Extract

Do **not** extract a type to the shared kernel if any of the following
apply:

- **The name matches but the contract is different.** Example: Assignments
  has a domain-specific `IIntegrationEventPublisher` whose
  `PublishAsync(object payload, CancellationToken ct)` cannot be
  implemented as `EnqueueAsync<T>(T, CancellationToken) where T : class`
  without a behavioural change. Leave the local copy in place; record the
  inconsistency in an inline `// Note:` comment and the audit checklist
  below.
- **The type depends on a domain DbContext or other domain types.** The
  shared kernel must not reference `SchoolCollab.<Domain>.Core.Data.*`.
  In our codebase, both `OutboxIntegrationEventPublisher` and
  `OutboxDispatcher` reference the local `<Domain>DbContext`, so the
  *implementation* stays local even though the *interface* and the
  *entity* can be shared.
- **Only one project uses it today.** Wait for the second consumer to
  appear. Premature consolidation locks in an interface that no one has
  pressure-tested against a second domain.
- **The local copy is being deprecated in favour of the shared one.** Do
  the deprecation in a separate PR so the diff is reviewable.

## 8. Audit Checklist

When reviewing a PR that adds a new top-level type to a `<Domain>.Core`
project, verify:

- [ ] The new type is **not** a byte-identical duplicate of something that
      already lives in `SchoolCollab.Core`. If it is, extract the shared
      copy and have the new local type use it.
- [ ] The new type does **not** re-declare a primitive that already lives
      in `SchoolCollab.Core/Data/`, `SchoolCollab.Core/Auth/`,
      `SchoolCollab.Core/Tenancy/`, etc.
- [ ] The new type does not import any `SchoolCollab.<Domain>.Core.*`
      namespace (it would not belong in the kernel — but this is a hint
      that the type is too domain-specific to share).
- [ ] `dotnet build SchoolCollab.sln` succeeds with 0 errors and 0 new
      warnings.
- [ ] All unit and integration tests still pass.

When reviewing a PR that adds a new abstraction to `SchoolCollab.Core`
itself, verify:

- [ ] The abstraction has **at least two** `<Domain>.Core` consumers today.
- [ ] The local duplicates it replaces have been **deleted** from every
      `<Domain>.Core` (or, in the case of a domain-specific variant, the
      audit comment has been added explaining why it stays).
- [ ] The folder, namespace, and file-name conventions in §4–§5 are
      followed.
- [ ] All `<Domain>.Core` consumer `using` directives and fully-qualified
      references have been updated per §6.

## 9. Currently-Shared Types (Reference Inventory)

These types currently live in the shared kernel after the initial
extraction. Any new shared type should be added here in the same PR that
introduces it.

### `SchoolCollab.Core/CQRS/`
- `ICommand` — marker for state-changing operations.
- `ICommandHandler<TCommand>` — handles a void command.
- `ICommandHandler<TCommand, TResult>` — handles a command that returns a result.
- `IQuery<TResult>` — marker for read-only operations.
- `IQueryHandler<TQuery, TResult>` — handles a query.

### `SchoolCollab.Core/Messaging/`
- `IIntegrationEventPublisher` — transactional outbox publisher contract
  (`EnqueueAsync<T>(T, CancellationToken) where T : class`).
- `OutboxMessage` — the outbox row entity (`IEntity`, with `OccurredAt`,
  `DispatchedAt`, `Attempts`, `LastError`).
- `OutboxOptions` — configuration record bound from the `Outbox`
  configuration section (`ExchangeName`, `BatchSize`, `PollInterval`).
- `OutboxIntegrationEventPublisher<TContext>` — default implementation of
  `IIntegrationEventPublisher`. Generic over the bounded-context
  `DbContext`; uses `IDbContextFactory<TContext>` to create a short-lived
  context per call.
- `OutboxDispatcher<TContext>` — `BackgroundService` that drains
  `OutboxMessage` rows to RabbitMQ with publisher confirms and
  `FOR UPDATE SKIP LOCKED`. Generic over the bounded-context `DbContext`;
  reads the exchange name from `IOptionsMonitor<OutboxOptions>`.
- `OutboxExtensions.AddOutbox<TContext>(IConfiguration, string? sectionName)`
  — DI helper that wires the options, publisher, and dispatcher for a
  bounded context.

### Per-domain EF configuration (consolidated)

The `OutboxMessageConfiguration` is shared in
`SchoolCollab.Core/Data/Outbox/OutboxMessageConfiguration.cs`. Each
module passes its own `OutboxConfigurationFlags` via the fluent
`IOutboxConfigurationBuilder` callback to
`services.AddOutbox<TContext>(configuration, outbox => ...)`. The
default flags match the common-case (Students); CodedValues overrides
to set `jsonb` on the payload column, a larger `Type` max length, a
`0` default on `Attempts`, and a partial index on `OccurredAt`;
Assignments opts in to the partial index only. See
[`outbox-message-configuration-consolidation-plan.md`](./outbox-message-configuration-consolidation-plan.md)
for the full rationale and the resolved design questions.

### Plan for further consolidation

`OutboxIntegrationEventPublisher`, `OutboxDispatcher`, `OutboxMessage`,
and `OutboxMessageConfiguration` are now shared and used by
Students, CodedValues, **and** Assignments. The
[messaging-consolidation-plan](./messaging-consolidation-plan.md)
Phases 1–3 are complete; Phases 4–6 (folder cleanup, doc updates,
ArchTests) remain.

New `<Domain>.Core` projects must **not** copy any local
`Messaging/` files. They should call
`services.AddOutbox<TContext>(configuration, outbox => ...)` from the
kernel and rely on the shared `OutboxMessageConfiguration` plus the
per-module flags.

## 10. Worked Example: CQRS Abstractions

The CQRS group was extracted from three local `CQRS/ICommand.cs`,
`ICommandHandler.cs`, `IQuery.cs`, `IQueryHandler.cs` files (one per
`<Domain>.Core`) into a single `SchoolCollab.Core/CQRS/` group:

```
src/SchoolCollab.Core/CQRS/
├── ICommand.cs          → namespace SchoolCollab.Core.CQRS;
├── ICommandHandler.cs   → namespace SchoolCollab.Core.CQRS;  (contains both arities)
├── IQuery.cs            → namespace SchoolCollab.Core.CQRS;
└── IQueryHandler.cs     → namespace SchoolCollab.Core.CQRS;
```

Every consumer in `SchoolCollab.Students.Core/CQRS/...`,
`SchoolCollab.Assignments.Core/CQRS/...`, and
`SchoolCollab.CodedValues.Core/CQRS/...` had its
`using SchoolCollab.<X>.Core.CQRS;` redirected to
`using SchoolCollab.Core.CQRS;` (the namespace *declarations* of the
specialty sub-folders stay the same, e.g.
`SchoolCollab.Students.Core.CQRS.Students.Commands.CreateStudent`).

Local `Extensions.cs` files in each `<Domain>.Core` were updated to use
the unqualified type names (`ICommandHandler<>`, `IQueryHandler<,>`) and
add `using SchoolCollab.Core.CQRS;` to their import list.

## 11. Worked Example: Transactional Outbox

The transactional outbox was extracted across three rounds (see the
[messaging-consolidation-plan](./messaging-consolidation-plan.md)
and the follow-on
[outbox-message-configuration-consolidation-plan](./outbox-message-configuration-consolidation-plan.md))
from local `Messaging/` folders in each `<Domain>.Core` into a
single kernel group:

```
src/SchoolCollab.Core/Messaging/
├── IIntegrationEventPublisher.cs                       (contract)
├── OutboxMessage.cs                                    (entity)
├── OutboxOptions.cs                                    (configuration)
├── OutboxIntegrationEventPublisher.cs                  (default impl, generic over DbContext)
├── OutboxDispatcher.cs                                 (BackgroundService, generic over DbContext)
└── OutboxExtensions.cs                                 (AddOutbox<TContext>(IConfiguration) DI helper)

src/SchoolCollab.Core/Data/Outbox/
├── IOutboxConfigurationBuilder.cs                      (fluent per-module flags)
├── OutboxConfigurationBuilder.cs                       (internal default impl)
├── OutboxConfigurationFlags.cs                         (immutable record + Default)
└── OutboxMessageConfiguration.cs                       (shared EF mapping)
```

After the migration, the local `Messaging/` folders in each
`<Domain>.Core` were deleted entirely. Each bounded context now
adds the outbox in **under 10 lines** in its `Extensions.cs`:

```csharp
services.AddOutbox<TContext>(configuration);                      // common-case
services.AddOutbox<TContext>(configuration, outbox => outbox      // CodedValues
    .SetTypeMaxLength(500)
    .UseJsonbPayload()
    .UseAttemptsDefaultZero()
    .UsePartialIndexOnOccurredAt());
```

plus one entry in the module's `appsettings.json`:

```json
"Outbox": { "ExchangeName": "students" }
```

plus one `DbSet<OutboxMessage>` in the `DbContext` and one line in
its `OnModelCreating`:

```csharp
modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration(OutboxMapping.FlagsFor<TContext>()));
```

The original Assignments implementation was on a different contract
(local `PublishAsync(object)` instead of `EnqueueAsync<T>(T)`, local
`OutboxMessage` with `CreatedAt`/`ProcessedAt` fields, per-scope
`IConnectionFactory` rather than shared `IConnection`). Bringing
it onto the shared contract was Phase 3 of the
[messaging-consolidation-plan](./messaging-consolidation-plan.md)
and required a destructive EF migration (column renames
`processed_at` → `dispatched_at`, `error` → `last_error`,
`created_at` → `occurred_at`; new `attempts` column with default
`0`; new partial index on `occurred_at WHERE dispatched_at IS NULL`).

## 12. Audit Checklist

A `<Domain>.Core` project is **not** in compliance with this
pattern if any of the following are present:

- `Messaging/IIntegrationEventPublisher.cs` (local interface).
- `Messaging/OutboxIntegrationEventPublisher.cs` (local default impl).
- `Messaging/OutboxDispatcher.cs` (local `BackgroundService`).
- `Data/OutboxMessage.cs` (local entity).
- `Data/Configurations/OutboxMessageConfiguration.cs` (local EF
  mapping for the outbox table).
- A direct `using RabbitMQ.Client;` or `using IConnection;` /
  `using IConnectionFactory;` reference in any `.cs` file.
- A direct `<PackageReference Include="RabbitMQ.Client" />` or
  `<PackageReference Include="Aspire.RabbitMQ.Client" />` in the
  `<Domain>.Core.csproj`.

The shared implementation in `SchoolCollab.Core/Messaging/` is the
only authorised outbox plumbing. Phase 6 of the
[messaging-consolidation-plan](./messaging-consolidation-plan.md)
turns this checklist into an `ArchTests` regression guard.
