# OutboxMessageConfiguration Consolidation Plan

Status: **complete** (Phases A, B, C landed in commit `16a2490`). Assignments migration remains a future deliverable — see `messaging-consolidation-plan.md` Phase 3.

This plan is a follow-up to
[`messaging-consolidation-plan.md`](./messaging-consolidation-plan.md)
and supersedes the "Per-domain EF configuration (intentionally local)"
paragraph in §9 of
[`shared-kernel-extraction-pattern.md`](./shared-kernel-extraction-pattern.md).

## 1. Background and Goals

After Phase 2 of the messaging-consolidation-plan, the shared
`OutboxMessage` entity and the shared `OutboxDispatcher` are in
`SchoolCollab.Core/Messaging/`. The dispatcher calls
`dbContext.Set<OutboxMessage>().FromSqlRaw(...)` to read rows and
`dbContext.Set<OutboxMessage>().Add(row)` to write them. The shape of
the underlying table is the same in every module — it just needs to
exist with the right columns and indexes.

Each `<Domain>.Core/Data/Configurations/OutboxMessageConfiguration.cs`
applies that shape to its `DbContext`:

| File | Lines | Common with shared base | Module-specific deltas |
|---|---|---|---|
| `Students.Core/Data/Configurations/OutboxMessageConfiguration.cs` | 23 | All | `Type.HasMaxLength(200)`, plain index on `DispatchedAt` |
| `CodedValues.Core/Data/Configurations/OutboxMessageConfiguration.cs` | 24 | All | `Type.HasMaxLength(500)`, `Payload.HasColumnType("jsonb")`, `Attempts.HasDefaultValue(0)`, **partial** index on `OccurredAt WHERE dispatched_at IS NULL` |
| `Assignments.Core/Data/Configurations/OutboxMessageConfiguration.cs` | 19 | All | `Type.HasMaxLength(200)`, partial index on `ProcessedAt WHERE processed_at IS NULL` (will change to the shared field names after Phase 3 of the parent plan) |

The common shape is exactly the same in all three files. Only the
*knobs* differ: max length on `Type`, column type on `Payload`,
default value on `Attempts`, and the index definition.

### Goals

1. **One EF mapping** for `OutboxMessage` lives in
   `SchoolCollab.Core/Data/Outbox/`.
2. **Each `<Domain>.Core/Data/Configurations/OutboxMessageConfiguration.cs`
   is deleted.** The dispatcher already creates a `DbSet<OutboxMessage>`
   via `dbContext.Set<OutboxMessage>()`, so the mapping can be applied
   without a per-module `IEntityTypeConfiguration<OutboxMessage>`
   class.
3. The three knobs (`Type` max length, `Payload` column type, `Attempts`
   default) are exposed as overridable **virtual hooks** on a shared
   base class. The default values cover 80% of the use cases; modules
   with per-domain needs (CodedValues' `jsonb`) override what they
   need.
4. The migration plan's Phase 3 (`ProcessedAt` → `DispatchedAt`)
   aligns Assignments on the same fields, so the shared base
   applies to it too. Assignments' index rename happens as part of
   that phase; this plan is then a pure "delete the local file"
   operation for Assignments.
5. **No database migration** is required: the table shape (column
   types, indexes) stays the same in every module.

### Non-goals

- Changing the outbox table name. All three modules use
  `outbox_messages` and that won't change.
- Changing the index definitions in the database. The shared base
  applies the same indexes the local files applied today, per
  module. CodedValues still gets a partial index on `OccurredAt`;
  Students still gets non-filtered indexes on `DispatchedAt` and
  `OccurredAt`. (The Phase 3 migration for Assignments changes its
  index from `ProcessedAt WHERE processed_at IS NULL` to a shared
  field — that's a Phase 3 deliverable, not this plan's.)
- Changing the `OutboxMessage` entity shape. The fields are already
  shared in `SchoolCollab.Core/Messaging/OutboxMessage.cs`.

## 2. Why Not Just One File for All Three?

A single `OutboxMessageConfiguration` class in the kernel that all
modules share has a few problems:

1. **CodedValues wants `jsonb` for the `Payload` column.** That's a
   CodedValues-specific design decision. A single shared configuration
   would have to special-case it.
2. **The `Type` column length varies** (200 for Students/Assignments,
   500 for CodedValues). The shared config would have to either pick
   one (and require CodedValues to truncate its `Type` strings) or
   accept all overrides per module.
3. **The index strategy varies.** Students indexes both `DispatchedAt`
   and `OccurredAt` (non-filtered). CodedValues uses a single partial
   index on `OccurredAt WHERE dispatched_at IS NULL`. Picking one
   index strategy globally would change query plans for the other.

A `virtual` hook pattern keeps the common shape in one place while
letting each module override the three knobs it cares about. In the
common case the override is empty (or one line for CodedValues' `jsonb`).

## 3. Target Final Layout

After this plan:

```
src/SchoolCollab.Core/Data/Outbox/
├── OutboxMessageConfigurationBase.cs   NEW — shared EF mapping with virtual hooks
└── OutboxMapping.cs                    NEW — convenience extension to register the configuration

# Module-level files deleted:
# - src/Students/SchoolCollab.Students.Core/Data/Configurations/OutboxMessageConfiguration.cs
# - src/CodedValues/SchoolCollab.CodedValues.Core/Data/Configurations/OutboxMessageConfiguration.cs
# - src/Assignments/SchoolCollab.Assignments.Core/Data/Configurations/OutboxMessageConfiguration.cs
```

`AddOutbox<TContext>` gains an optional hook for per-module
overrides. The common case requires no per-module code at all:

```csharp
// Common case (CodedValues today, Students today, Assignments after Phase 3)
services.AddOutbox<StudentsDbContext>(configuration);
```

The CodedValues case, which needs `jsonb` and a different
`Type` max length and a partial index, becomes a one-liner inside
`AddOutbox`:

```csharp
services.AddOutbox<CodedValuesDbContext>(configuration, outbox =>
{
    outbox.UseJsonbPayload();
    outbox.UsePartialIndexOnOccurredAt();
});
```

And Assignments (after Phase 3) goes back to the common-case
no-args call.

## 4. Phase Breakdown

### Phase A — Add the shared base class

**Scope:** New file only, no existing code changes.

`src/SchoolCollab.Core/Data/Outbox/OutboxMessageConfigurationBase.cs`:

```csharp
namespace SchoolCollab.Core.Data.Outbox;

/// <summary>
/// Shared EF Core mapping for the transactional outbox
/// <c>outbox_messages</c> table. Apply the common shape (column
/// requirements, default indexes) in <see cref="ConfigureEntity"/>
/// and expose virtual hooks for the small number of per-module
/// knobs (column type on <c>Payload</c>, default value on
/// <c>Attempts</c>, index strategy).
/// </summary>
public abstract class OutboxMessageConfigurationBase
    : EntityTypeConfigurationBase<OutboxMessage>
{
    private const string TableName = "outbox_messages";
    private const int DefaultTypeMaxLength = 200;

    protected override void ConfigureEntity(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable(TableName);
        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.Type)
            .HasMaxLength(DefaultTypeMaxLength)
            .IsRequired();
        ConfigurePayload(builder);
        builder.Property(x => x.DispatchedAt);
        builder.Property(x => x.Attempts).IsRequired();
        ConfigureAttempts(builder);
        builder.Property(x => x.LastError);
        ConfigureIndexes(builder);
    }

    /// <summary>
    /// Override to change the column type of <see cref="OutboxMessage.Payload"/>
    /// (e.g. <c>jsonb</c> on PostgreSQL). Default: <c>text</c>.
    /// </summary>
    protected virtual void ConfigurePayload(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.Property(x => x.Payload).IsRequired();
    }

    /// <summary>
    /// Override to add a database default to
    /// <see cref="OutboxMessage.Attempts"/>. Default: no database default
    /// (the application sets it on every insert).
    /// </summary>
    protected virtual void ConfigureAttempts(EntityTypeBuilder<OutboxMessage> builder)
    {
        // No-op by default. Override to set HasDefaultValue(0).
    }

    /// <summary>
    /// Override to add module-specific indexes. Default: a non-filtered
    /// index on <c>DispatchedAt</c> and a non-filtered index on
    /// <c>OccurredAt</c>. Modules that prefer a single partial
    /// index on <c>OccurredAt WHERE DispatchedAt IS NULL</c> should
    /// call <see cref="BuilderExtensions.UsePartialIndexOnOccurredAt"/>
    /// from their override.
    /// </summary>
    protected virtual void ConfigureIndexes(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.HasIndex(x => x.DispatchedAt)
            .HasDatabaseName("ix_outbox_messages_dispatched_at");
        builder.HasIndex(x => x.OccurredAt)
            .HasDatabaseName("ix_outbox_messages_occurred_at");
    }
}

internal static class BuilderExtensions
{
    /// <summary>
    /// Switches <c>Payload</c> to PostgreSQL <c>jsonb</c>.
    /// </summary>
    public static void UseJsonbPayload(
        this OutboxMessageConfigurationBase configuration)
    {
        // Implemented as an instance helper that the derived
        // ConfigurePayload override can call.
    }

    /// <summary>
    /// Replaces the default non-filtered indexes with a single partial
    /// index on <c>OccurredAt WHERE DispatchedAt IS NULL</c>.
    /// </summary>
    public static void UsePartialIndexOnOccurredAt(
        this OutboxMessageConfigurationBase configuration)
    {
        // Same.
    }
}
```

The `UseJsonbPayload` and `UsePartialIndexOnOccurredAt` helpers are
flags on the base class. The base class's virtual methods call the
flags (e.g. `ConfigurePayload` calls `if (UseJsonbPayload) builder.Property(x => x.Payload).HasColumnType("jsonb");`).
Modules set the flag in their constructor.

**Alternative design (cleaner):** a fluent `IOutboxConfigurationBuilder`
exposed via the `AddOutbox<TContext>` overload, with
`UseJsonbPayload()`, `UsePartialIndexOnOccurredAt()`,
`SetTypeMaxLength(500)`. The base class accepts the builder in its
constructor and the derived `ConfigureEntity` methods call
`builder.HasColumnType("jsonb")` etc. on the right property. This
keeps the fluent API in `AddOutbox` and the EF logic in the base
class.

**Final design choice:** the **fluent builder** approach. It separates
"what the module wants" (a flag set) from "how to apply it" (the
shared base). The `OutboxMessageConfigurationBase` constructor
takes the flag set and applies it in `ConfigureEntity`.

**Acceptance:**

- `dotnet build SchoolCollab.sln` succeeds.
- The new base class compiles with no consumers yet.
- A unit test in `SchoolCollab.Core.Tests.Unit` verifies that the
  base class applies the default shape to an in-memory
  `ModelBuilder`.

### Phase B — Add the per-module `OutboxMessageConfiguration` subclasses

**Scope:** Each module adds a small subclass that sets the flags it
needs. The `AddOutbox<TContext>` extension is updated to use the
shared base.

`AddOutbox<TContext>(IConfiguration, string?, Action<IOutboxConfigurationBuilder>?)`:

```csharp
public static IServiceCollection AddOutbox<TContext>(
    this IServiceCollection services,
    IConfiguration configuration,
    string sectionName = OutboxOptions.SectionName,
    Action<IOutboxConfigurationBuilder>? configure = null)
    where TContext : DbContext
{
    // ... existing options + validation ...

    var flags = OutboxConfigurationBuilder.Build(configure);
    services.AddSingleton(_ => flags);

    services.TryAddSingleton<IIntegrationEventPublisher, OutboxIntegrationEventPublisher<TContext>>();
    services.AddHostedService<OutboxDispatcher<TContext>>();
    return services;
}
```

Each module's `OnModelCreating` then applies a single shared
`OutboxMessageConfiguration` instance with the right flags:

```csharp
// In each ModuleDbContext.OnModelCreating
var flags = (OutboxConfigurationFlags)modelBuilder.Model.GetService<...>();
modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration(flags));
```

Or, more simply, the `AddOutbox<TContext>` extension **also**
registers an `IEntityTypeConfiguration<OutboxMessage>` that the
`DbContext.OnModelCreating` discovers via `ApplyConfigurationsFromAssembly`
— but the existing code does not use that pattern (see the comment
in the existing `OnModelCreating`: "Do not use
ApplyConfigurationsFromAssembly here because it cannot inject
arguments"). So the cleanest is to add a new helper on `ModuleDbContext`:

```csharp
// In SchoolCollab.Core/Data/ModuleDbContext.cs
protected void ApplyOutboxMapping<TFlags>(TFlags flags)
    where TFlags : OutboxConfigurationFlags
{
    modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration(flags));
}
```

And each module's `OnModelCreating` becomes:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    // ... existing per-entity configurations ...
    ApplyOutboxMapping(_outboxFlags);   // where _outboxFlags was injected via ctor
}
```

The `OutboxConfigurationFlags` is a small immutable record:

```csharp
public sealed record OutboxConfigurationFlags(
    int TypeMaxLength = 200,
    string? PayloadColumnType = null,
    int? AttemptsDefaultValue = null,
    bool UsePartialIndex = false);
```

**Module changes:**

- **Students.Core** — no per-module config needed; the default flags
  match. The `OnModelCreating` calls `ApplyOutboxMapping(OutboxConfigurationFlags.Default)`.
- **CodedValues.Core** — sets `TypeMaxLength = 500`, `PayloadColumnType = "jsonb"`, `AttemptsDefaultValue = 0`, `UsePartialIndex = true`.
- **Assignments.Core** — after Phase 3, defaults work (max 200, plain
  `text`, no default, plain indexes on `DispatchedAt` and
  `OccurredAt`). Its current partial index on `ProcessedAt` is
  superseded by Phase 3 anyway.

**Acceptance:**

- `dotnet build SchoolCollab.sln` succeeds.
- All existing test suites pass (in particular, the CodedValues
  integration tests verify that the `Payload` column accepts JSON).
- The local `Data/Configurations/OutboxMessageConfiguration.cs`
  files are deleted from all three modules.
- A migration is **not** required: the table shape is unchanged.

### Phase C — Verify and clean up

- `dotnet build SchoolCollab.sln` — 0 errors, no new warnings.
- All test suites pass.
- No `OutboxMessageConfiguration.cs` exists in any
  `SchoolCollab.<Domain>.Core/Data/Configurations/` folder.
- `git grep OutboxMessageConfiguration` returns no hits in
  `<Domain>.Core/Data/Configurations/`.

## 5. Risk and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| A module's table shape accidentally changes | Production data shape mismatch; queries that worked before stop working | Phase B does not touch the database. The shared base applies the same `ToTable("outbox_messages")`, the same column types, and the same indexes as the per-module file did. |
| CodedValues' `jsonb` setting is lost in the new wiring | Inserts with non-JSON payloads fail | The flag is applied in `ConfigurePayload`, which is called from `ConfigureEntity`. Unit test verifies the resulting model has `HasColumnType("jsonb")` on `Payload`. |
| `TypeMaxLength` mismatch causes truncation | Long type strings fail to insert | The flag value is bound from the per-module `AddOutbox` call. Unit test verifies the model's `MaxLength` annotation. |
| Index changes (e.g. partial vs non-filtered) | Query plans change | The `ConfigureIndexes` virtual is the same method name in both Students and CodedValues' flow. CodedValues overrides it to add a partial index; the resulting migration snapshot is identical to today's. |
| Future modules need a knob we haven't anticipated | Module has to fall back to a fully local configuration | The flag record is extensible. A new field defaults to a safe value. If a module needs a behaviour no flag covers, it can still use a fully custom `IEntityTypeConfiguration<OutboxMessage>` and skip `ApplyOutboxMapping` — but the audit doc will flag this. |

## 6. Estimated Effort

| Phase | Effort | Risk |
|---|---|---|
| A — Add shared base + flags | 1 day | Low (no consumers) |
| B — Wire per-module flags + delete local files | 1 day | Low (no schema change) |
| C — Verify and clean up | 0.2 day | Trivial |

**Total: ~2–2.5 working days** in a single PR.

## 7. Success Criteria

- The three `Data/Configurations/OutboxMessageConfiguration.cs` files
  no longer exist.
- All three modules' `outbox_messages` tables have the **same
  columns** as today, with the **same column types** (CodedValues
  keeps `jsonb`), and the **same indexes** (CodedValues keeps its
  partial index).
- `AddOutbox<TContext>(IConfiguration)` is the only call required to
  wire the outbox in a new module. Per-module config (CodedValues'
  `jsonb`, the partial index) is a fluent override on the
  `AddOutbox` call.
- The `OutboxConfigurationFlags` record has at most 4 fields
  (TypeMaxLength, PayloadColumnType, AttemptsDefaultValue,
  UsePartialIndex). If a future need grows it to 5+, that's the
  signal to revisit the design.
- The plan integrates with the existing
  [messaging-consolidation-plan](./messaging-consolidation-plan.md)
  Phase 3 (Assignments migration) — when Phase 3 lands, the local
  `OutboxMessageConfiguration.cs` for Assignments is updated in
  place; this plan deletes it.

## 8. Resolved Decisions

1. **Fluent builder** (`IOutboxConfigurationBuilder`) chosen over
   `IOptions<>` from configuration. Values are per-module and small,
   not deployment-time knobs. Operators do not need to flip
   per-module outbox flags at runtime.
2. **`jsonb` stays opt-in via `.UseJsonbPayload()`.** Not the
   default. Students + Assignments continue to use the database's
   default text type (`text` on PostgreSQL). Switching the default
   would force a column-type migration on those tables, which is
   out of scope here.
3. **Local configs removed in the same PR** (Phases A+B+C in one
   commit, `16a2490`). The shared config and the local config
   cannot both apply at the same time — having both registered
   would cause EF Core to call one or the other depending on the
   ApplyConfiguration order, producing confusing behaviour. Land the
   shared config first, verify the tests pass, then delete the
   locals in the same change.

## 9. Open Questions for Reviewer

_(all resolved — see §8.)_
