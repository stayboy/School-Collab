# Messaging Consolidation Plan

Status: **complete** (Phases 1–3 landed; Students, CodedValues, and Assignments are all on the shared `IIntegrationEventPublisher` contract, shared `OutboxMessage` entity, shared `OutboxDispatcher<TContext>`, and shared `OutboxMessageConfiguration`). The follow-on outbox-configuration consolidation plan is also complete — see [`outbox-message-configuration-consolidation-plan.md`](./outbox-message-configuration-consolidation-plan.md). Phases 4–6 (folder cleanup, doc updates, ArchTests) remain.

This plan supersedes the "known domain-specific variants" note in
[`shared-kernel-extraction-pattern.md`](./shared-kernel-extraction-pattern.md)
and the "follow-up reconciliation" mention in §9 of that doc.

## 1. Background and Goals

The transactional outbox pattern is implemented in every `<Domain>.Core`
project with **near-identical** code:

| File | Students | CodedValues | Assignments |
|---|---|---|---|
| `IIntegrationEventPublisher` | shared (kernel) | shared (kernel) | local — `PublishAsync(object)` (different signature) |
| `OutboxMessage` entity | shared (kernel) | shared (kernel) | local — `CreatedAt`/`ProcessedAt` (different fields) |
| `OutboxIntegrationEventPublisher` | local | local | local — uses local `OutboxMessage` |
| `OutboxDispatcher` (BackgroundService) | local — exchange `"students"` | local — exchange `"coded-values"` | local — exchange `"assignments"`, takes `IConnectionFactory` not `IConnection` |
| `OutboxMessageConfiguration` | local — `MaxLength(200)`, no `jsonb` | local — `MaxLength(500)`, `jsonb` payload, partial index | local — `MaxLength(200)`, partial index on `processed_at` |
| `Extensions.Add<Domain>Core` | registers `IIntegrationEventPublisher` + `OutboxDispatcher` | same | same |

### Why this is duplication, not "intentional per-domain code"

- The `OutboxIntegrationEventPublisher` body is byte-identical between
  Students and CodedValues apart from the `<Domain>DbContext` type name.
- The `OutboxDispatcher` body is byte-identical between Students and
  CodedValues apart from the **exchange name** and the **DbContext type**.
  The exchange name is the only per-domain configuration value.
- Assignments is on an older, less-feature-rich implementation
  (no publisher confirms, no `OccurredAt`/`Attempts`/`LastError`,
  `IConnectionFactory` per-scope rather than shared `IConnection`,
  different `OutboxMessage` field set, different `IIntegrationEventPublisher`
  signature). Bringing it onto the shared contract is a worthwhile
  upgrade on its own.

### Goals

1. **One implementation** of the outbox pattern lives in
   `SchoolCollab.Core/Messaging/`.
2. Each `<Domain>.Core` project retains only the per-domain
   configuration: the EF mapping for the outbox table and the wiring
   of the dispatcher. These stay local because they reference the
   domain's own `DbContext` and need to know the domain's exchange
   name.
3. URI, exchange names, and table names are **unchanged** so existing
   databases, RabbitMQ topologies, and integration tests continue to
   work without any infrastructure changes.
4. The Assignments outbox is migrated to the shared contract in the
   same effort — a one-time convergence rather than a long-lived
   variant.
5. The `IIntegrationEventPublisher` contract stays exactly as it is
   today (`EnqueueAsync<T>(T, CancellationToken) where T : class`).

### Non-goals

- Changing the exchange name scheme. Each module keeps its own exchange
  name (`students`, `coded-values`, `assignments`).
- Renaming the outbox table. All three modules use `outbox_messages`.
- Introducing a new message bus abstraction (NServiceBus, MassTransit,
  etc.). The outbox stays on raw RabbitMQ.Client.
- Extracting the dispatcher into a separate Worker host. It stays
  inside each module's `BackgroundService` registration.

## 2. Target Final Layout

After all phases:

```
src/SchoolCollab.Core/Messaging/
├── IIntegrationEventPublisher.cs                       (unchanged from today)
├── OutboxMessage.cs                                    (unchanged from today)
├── OutboxOptions.cs                                    NEW — exchange name + batch size + poll interval
├── OutboxIntegrationEventPublisher.cs                  NEW — generic over DbContext via factory
├── OutboxDispatcher.cs                                 NEW — generic dispatcher driven by IOptions<OutboxOptions>
└── OutboxExtensions.cs                                 NEW — AddOutbox<TContext>(IConfiguration) helper

src/SchoolCollab.Students.Core/
├── Data/Configurations/OutboxMessageConfiguration.cs   (keeps the local EF mapping)
└── Extensions.cs                                       (calls AddOutbox<StudentsDbContext>)

src/SchoolCollab.CodedValues.Core/
├── Data/Configurations/OutboxMessageConfiguration.cs
└── Extensions.cs

src/SchoolCollab.Assignments.Core/
├── (no Data/OutboxMessage.cs)                          DELETED — now uses the shared one
├── Data/Configurations/OutboxMessageConfiguration.cs   (rewritten for the shared fields)
├── Migrations/                                         (new migration: add OccurredAt/Attempts/LastError, drop ProcessedAt/Error)
└── Extensions.cs
```

After migration, the local `Messaging/` folders in each `<Domain>.Core`
are **deleted** entirely.

## 3. Phase Breakdown

### Phase 1 — Add the shared outbox primitives in `SchoolCollab.Core`

**Scope:** New files only. No existing code is changed.

- `src/SchoolCollab.Core/Messaging/OutboxOptions.cs`:
  ```csharp
  public sealed class OutboxOptions
  {
      public const string SectionName = "Outbox";
      public string ExchangeName { get; set; } = default!;
      public int BatchSize { get; set; } = 100;
      public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
  }
  ```
- `src/SchoolCollab.Core/Messaging/OutboxIntegrationEventPublisher.cs`:
  - Generic over the `DbContext` type via `IDbContextFactory<TContext>` (so
    the publisher does not need the caller's scoped `DbContext`).
  - The Students/CodedValues bodies are byte-identical apart from the
    `DbContext` type — easy to genericise.
  - The `AddOutbox<TContext>` extension registers the publisher keyed by
    `IIntegrationEventPublisher` for the domain.

- `src/SchoolCollab.Core/Messaging/OutboxDispatcher.cs`:
  - Takes `IOptions<OutboxOptions>`, `IServiceScopeFactory`,
    `IConnection` (shared), `ILogger<OutboxDispatcher>`.
  - Reaches the outbox table via `IDbContextFactory<TContext>` +
    `Set<OutboxMessage>()` — no need to know the concrete `<Domain>DbContext`.
  - All the FOR UPDATE SKIP LOCKED logic, publisher confirms, attempt
    tracking, and error capture move here from the local dispatchers.

- `src/SchoolCollab.Core/Messaging/OutboxExtensions.cs`:
  ```csharp
  public static IServiceCollection AddOutbox<TContext>(
      this IServiceCollection services,
      IConfiguration configuration,
      string sectionName = OutboxOptions.SectionName)
      where TContext : DbContext
  {
      services.Configure<OutboxOptions>(configuration.GetSection(sectionName));
      services.AddScoped<IIntegrationEventPublisher, OutboxIntegrationEventPublisher<TContext>>();
      services.AddHostedService<OutboxDispatcher<TContext>>();
      return services;
  }
  ```

**Acceptance:**
- `dotnet build SchoolCollab.sln` succeeds.
- No existing project behaviour changes (the new code is not yet
  wired in).
- New `OutboxOptions` class is unit-testable in isolation.

### Phase 2 — Migrate Students + CodedValues to the shared outbox

**Scope:** Replace the local `OutboxIntegrationEventPublisher`,
`OutboxDispatcher`, and the two corresponding `using ... .Messaging;`
lines + `services.Add*` calls in `Extensions.cs` with a single
`services.AddOutbox<StudentsDbContext>(builder.Configuration)` call.

The two EF `OutboxMessageConfiguration.cs` files **stay local** —
they configure the same shared `OutboxMessage` entity but with
per-domain table/index details (e.g. CodedValues' `jsonb` payload
column and partial index on `OccurredAt`).

Add to `appsettings.json` / `appsettings.Development.json` for each
API + Worker host that uses the module:

```json
"Outbox": {
  "ExchangeName": "students"
}
```

**Acceptance:**
- All Students + CodedValues unit + integration tests pass.
- The local `Messaging/OutboxIntegrationEventPublisher.cs` and
  `Messaging/OutboxDispatcher.cs` are deleted.
- The local `Messaging/` folder is empty and also removed.

### Phase 3 — Migrate Assignments to the shared `OutboxMessage` and the shared publisher signature

**Scope:** This is the substantive change. The Assignments outbox is
on a different contract today.

3.1 **Update the local `IIntegrationEventPublisher`**:
   - Rename `PublishAsync(object payload, CancellationToken ct)` to
     `EnqueueAsync<T>(T message, CancellationToken cancellationToken = default)
       where T : class`.
   - This is a **breaking change** for the five Assignments command
     handlers that call `publisher.PublishAsync(...)`. Update them to
     call `publisher.EnqueueAsync<T>(...)` with the correct generic
     argument. The handlers currently construct anonymous types
     (`new { assignment.Id, assignment.Title, ... }`); each call site
     gets a named record (or reuses the existing
     `SchoolCollab.Assignments.Contracts.Events.AssignmentClosedEvent`
     family if those exist).

3.2 **Switch to the shared `OutboxMessage`**:
   - Delete `src/Assignments/SchoolCollab.Assignments.Core/Data/OutboxMessage.cs`.
   - Update `OutboxIntegrationEventPublisher.cs` and
     `OutboxMessageConfiguration.cs` to use `SchoolCollab.Core.Messaging.OutboxMessage`.
   - Rename the local fields in the `OutboxMessageConfiguration`:
     - `ProcessedAt` → `DispatchedAt`
     - `Error` → `LastError`
     - Add `OccurredAt` and `Attempts` with sensible defaults
       (`OccurredAt` not null, `Attempts` with a database default of 0).

3.3 **Add a migration** to the existing `AssignmentsDbContext`:
   - Add `OccurredAt TIMESTAMPTZ NOT NULL DEFAULT now()`.
   - Add `Attempts INT NOT NULL DEFAULT 0`.
   - Add `LastError TEXT NULL`.
   - Add `DispatchedAt TIMESTAMPTZ NULL`.
   - Drop `ProcessedAt`.
   - Drop `Error`.
   - Rename the index `ix_outbox_messages_processed_at` to
     `ix_outbox_messages_dispatched_at` (with the same partial filter
     on the new column).
   - This is **a destructive schema change**. Production deployments
     that already have rows in `outbox_messages` with `ProcessedAt` set
     will lose the "already dispatched" flag and re-dispatch those rows
     on the next dispatcher run. **Mitigation:** drain the queue before
     deploying the migration (set the Assignments service replicas to 0,
     wait for in-flight messages, run the migration, redeploy).
   - **Alternative (safer):** keep `ProcessedAt` as a shadow column
     and copy its value into `DispatchedAt` during the migration. Then
     drop `ProcessedAt` in a follow-up migration. Document this
     trade-off in the PR description.

3.4 **Migrate the dispatcher to the shared one**:
   - Replace the local `OutboxDispatcher` body with the shared
     `OutboxDispatcher<AssignmentsDbContext>` (from Phase 1).
   - The exchange name moves from a hard-coded `"assignments"` string
     to `IOptions<OutboxOptions>.ExchangeName` configured via
     `appsettings.json`.

3.5 **Switch the `IConnection` registration**:
   - Today Assignments registers `IConnectionFactory` per scope and
     creates a new connection inside the dispatcher loop. The shared
     dispatcher expects a single shared `IConnection` (matching what
     Students + CodedValues already do). Update the Aspire
     `AddRabbitMQClient("rabbitmq")` registration or
     `Extensions.AddAssignmentsCore` to register a singleton
     `IConnection` (this is the standard Aspire pattern; verify by
     looking at how Students + CodedValues already do it).

**Acceptance:**
- All Assignments unit + integration tests pass.
- Manual smoke test: enqueue an event from a handler, see it
  published to the `assignments` exchange within one poll interval.
- The local `Messaging/IIntegrationEventPublisher.cs`,
  `Messaging/OutboxIntegrationEventPublisher.cs`,
  `Messaging/OutboxDispatcher.cs` are deleted.
- The `Data/OutboxMessage.cs` is deleted.

### Phase 4 — Remove the local `Messaging/` folders

Once Phases 2 and 3 land:

- Delete `src/Students/SchoolCollab.Students.Core/Messaging/` (the
  empty directory).
- Delete `src/CodedValues/SchoolCollab.CodedValues.Core/Messaging/`
  (the empty directory).
- Delete `src/Assignments/SchoolCollab.Assignments.Core/Messaging/`
  (the empty directory).

If any project still has a local `Messaging/` folder after this phase,
that's a signal that the migration is incomplete and should be
investigated.

**Acceptance:** No `Messaging/` folder exists in any
`SchoolCollab.<Domain>.Core/` project.

### Phase 5 — Update the pattern docs

- Update [`shared-kernel-extraction-pattern.md`](./shared-kernel-extraction-pattern.md)
  §9 ("Currently-Shared Types") to:
  - Add `OutboxOptions`, `OutboxIntegrationEventPublisher<TContext>`,
    `OutboxDispatcher<TContext>`, `OutboxExtensions` to the
    `SchoolCollab.Core/Messaging/` inventory.
  - Delete the "Known domain-specific variants" subsection
    (Assignments has been aligned).
- Add a new "**Transactional Outbox Pattern**" section that
  documents the migration as a worked example alongside the
  existing CQRS example.
- Add an **audit checklist** for `Messaging/`: if a `<Domain>.Core`
  project contains any of the four file types, the audit fails and
  the project is not in compliance.

### Phase 6 — Long-term: enforce the pattern

Add an `ArchTests` project (or extend an existing one) that scans
the assembly metadata and asserts:

- No `<Domain>.Core` project contains a `Messaging/` folder.
- Every `<Domain>.Core` project that registers an `OutboxMessage` in
  its `DbContext` also calls `services.AddOutbox<TContext>(...)`.

This is the regression guard so the duplication does not creep back
in.

## 4. Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Assignments migration is destructive (drops `ProcessedAt`/`Error`) | Already-dispatched rows re-published on the next run; consumers see duplicates. | Document the safe deployment order in the PR: drain first, migrate, redeploy. Or do a 2-migration approach (Phase 3.3 alternative). |
| Generic `OutboxIntegrationEventPublisher<TContext>` is slower than today's per-context registration due to EF reflection | Negligible — `Set<OutboxMessage>()` is a no-op lookup. | Add a micro-benchmark in the test suite if concerned. |
| Removing the Assignments local `IIntegrationEventPublisher` is a breaking change for the five command handlers | Compile error in those handlers. | Phase 3.1 explicitly updates each call site. Test coverage exists for the affected handlers. |
| Generic `OutboxDispatcher<TContext>` requires `IDbContextFactory<TContext>` to be registered | EF Core 10+ registers this by default. | Verify in the existing modules during Phase 1; add explicit registration if needed. |
| Two implementations coexist temporarily during Phases 2 and 3 | Inconsistent behaviour between modules for the duration of the rollout. | Keep each phase to one PR; deploy and verify each before starting the next. |
| `OutboxOptions` configuration is misread at runtime and the wrong exchange is used | Messages go to the wrong exchange. | Phase 1 includes a configuration-binding test. Phase 2/3 manual smoke test confirms exchange name from logs. |

## 5. Estimated Effort

| Phase | Estimated effort | Risk |
|---|---|---|
| 1 — Shared primitives | 1–2 days | Low |
| 2 — Students + CodedValues migration | 0.5 day | Very low (no semantic change) |
| 3 — Assignments migration | 2–3 days | Medium (breaking change to the local `IIntegrationEventPublisher`; EF migration) |
| 4 — Folder cleanup | 0.1 day | Trivial |
| 5 — Doc updates | 0.5 day | Trivial |
| 6 — ArchTests | 1 day | Low |

**Total: ~5–7 working days** spread across 4 PRs (Phases 1+5 can
share a PR; Phases 2+4 can share a PR; Phase 3 is its own PR; Phase 6
is its own PR).

## 6. Success Criteria

- A new `<Domain>.Core` project (e.g. `SchoolCollab.Attendance.Core`)
  can add the transactional outbox in **under 10 lines**: one
  `OutboxMessageConfiguration` + one `services.AddOutbox<TContext>(...)`
  call + one config section.
- All four `Messaging/Outbox*` files in `<Domain>.Core` projects are
  gone.
- The Assignments outbox is on the same contract as the other modules
  (same `OutboxMessage` fields, same `IIntegrationEventPublisher`
  signature, same dispatcher behaviour).
- No `SchoolCollab.<Domain>.Core` project imports
  `RabbitMQ.Client` directly.
- The pattern is enforced by an ArchTest in Phase 6.
