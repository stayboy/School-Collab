# Assignments.Worker could not start — missing `Outbox__ExchangeName`

> Owner: Cross-context messaging / AppHost wiring
> Related: `documents/configuration.md` §2 (`assignments-worker`),
> `documents/solution/messaging-consolidation-plan.md` (the shared outbox),
> `tests/SchoolCollab.ArchitectureTests.Unit/AppHostOutboxExchangeWiringArchitectureTests.cs` (the new guard)
> Fixed: 2026-09-22, round `assignments-worker-outbox-config` (Tier 2 light)

## Finding

`SchoolCollab.Assignments.Worker` could not start. It failed at `host.Run()`
(`src/Assignments/SchoolCollab.Assignments.Worker/Program.cs:56`) with:

```text
Microsoft.Extensions.Options.OptionsValidationException
  Message=ExchangeName must be set in the 'Outbox' configuration section.
   at Microsoft.Extensions.Options.OptionsFactory`1.Create(String name)
   at Microsoft.Extensions.Options.StartupValidator.Validate()
```

Reported by the owner from a real launch; the host had been dead since the ar-19 round that
introduced it (`11cc37e1`).

### Why it happened

`AddAssignmentsCore` (`src/Assignments/SchoolCollab.Assignments.Core/Extensions.cs:93`) calls
`services.AddOutbox<AssignmentsDbContext>(...)` **unconditionally** — the extension is shared with
`assignments-api`. That helper (`src/SchoolCollab.Core/Messaging/OutboxExtensions.cs:75-79`):

- binds `OutboxOptions` from the `Outbox` configuration section,
- requires a non-empty `ExchangeName` via `.ValidateOnStart()`,
- and registers `OutboxDispatcher<TContext>` as a hosted background service.

The worker's `appsettings.json` deliberately carries **no** `Outbox` section (only
`RabbitMq:Subscriber`), because the AppHost is the single fan-out point — its own comment above the
exchange parameters says the names are *"fanned out to the matching API/**Worker** via
`WithEnvironment("Outbox__ExchangeName", param)`"*.

But the AppHost's `assignments-worker` block set **only** `RabbitMq__Subscriber__ExchangeName`.
So the one value the host required was the one value it never received. The sibling `students-worker`
gets both, which is the precedent the block was written to mirror.

### Why it stayed hidden

Aspire reports a process that exits as **`Finished`** — the same state a successful one-shot
resource (the `migrator`) ends in. During the 2026-09-22 cold-start verification the worker's
`Finished` state was misread as by-design, and the `OptionsValidationException` sitting in its
resource log was missed. An `OptionsValidationException` is a hard startup failure, not a degraded
mode: nothing about the worker ran.

### Scope of the miss: exactly one value

`grep -rn ValidateOnStart src` returns only **two** call sites in the whole tree — the outbox
(`OutboxExtensions.cs:79`) and the RabbitMQ subscriber (`:126`). The subscriber's `ExchangeName` +
`QueueName` were already satisfied (worker `appsettings.json` plus the AppHost env var). Everything
else the worker registers is unvalidated.

**The `smtp-*` parameters were considered and deliberately NOT added.** The worker never resolves
`IEmailSender`, so it never constructs an email sender, and `SmtpOptions` has no startup validation:

- the worker only **queues** `NotificationLog` rows (`SweepNotificationQueuer`);
- the drain that **sends** them is the API-side `NotificationDispatchSweepService` in
  `assignments-api`, which opens a DI scope and delegates to `NotificationDispatchService`
  (`src/Assignments/SchoolCollab.Assignments.Api/Services/NotificationDispatchSweepService.cs:36`) —
  `NotificationDispatchService` is the only component that resolves `IEmailSender`;
- and `assignments-api` already carries all five `Smtp__*` variables.

Adding them to the worker would have implied the worker sends mail, which it does not.

## Implementation

One statement in the `assignments-worker` AppHost chain, plus the block comment:

```csharp
.WithEnvironment("Outbox__ExchangeName", assignmentsOutboxExchange)
```

placed next to the existing `.WithEnvironment("RabbitMq__Subscriber__ExchangeName", assignmentsOutboxExchange)`
and ordered `Outbox__…` first, matching the `students-worker` chain.

- **No new `Parameters:` entry** — `outbox-exchange-assignments` already existed and is now reused for
  both environment variables on this host.
- **No `Outbox` section added to the worker's `appsettings.json`** — the AppHost remains the single
  fan-out point (decision preserved).
- **No change to `AddAssignmentsCore` / `AddOutbox` / `OutboxOptions`.** The alternative — not
  registering the outbox for worker hosts — would have changed a shared extension that
  `assignments-api` also uses, for a one-line wiring fix.
- `documents/configuration.md` §2's `assignments-worker` paragraph now names both injections and
  records the deliberate `smtp-*` exclusion.

### Guard

`tests/SchoolCollab.ArchitectureTests.Unit/AppHostOutboxExchangeWiringArchitectureTests.cs` — modelled
on the sibling `AppHostSettingsDbWiringArchitectureTests`. It derives the affected hosts from source
in two steps (never from the AppHost's own text, which would be tautological):

1. every file calling `services.AddOutbox<` → the `Add{Layer}()Core` extension it declares
   (today: `AddAssignmentsCore`, `AddSettingsCore`, `AddStudentsCore`);
2. every `src/**/Program.cs` calling those methods → the host projects
   (`assignments-api`, `assignments-worker`, `settings-api`, `students-api`, `students-worker`).

It then requires each of those AppHost resources to carry `WithEnvironment("Outbox__ExchangeName"`,
pins the resolved host set with a tripwire (a new host must consciously update the list), pins the
registration side so a fourth outbox-registering core cannot silently change the inspected set, and
carries a non-vacuity guard so a broken regex cannot make the assertions pass while inspecting nothing.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build SchoolCollab.slnx` | **0 errors**; no warning originates from either touched file (the 88 on a clean full rebuild are the pre-existing `NU1902`/`NU1903`/`ASPIRE010` + CS families) |
| `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` | **45 / 0** (41 before; +4 new guard tests) |
| Guard discrimination probe | with the added `.WithEnvironment` line removed: **1 failed / 44 succeeded**, and the failure is exactly `OutboxRegisteringHosts_GetOutboxExchangeName`; line restored → **45 / 0** |
| AppHost cold start (`assignments-worker` reaches Running/Healthy) | **parent-run** — recorded in the round doc's acceptance (needs Docker) |

The discrimination probe is the point: the guard fails on the pre-fix tree, so it would have caught
this defect rather than merely agreeing with the fix.
