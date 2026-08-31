# Plan — Phase 6.1: Pilot-tenant override for `FEATURE:EnableActivityGroups`

> Owner: Phase-6.1 orchestrator. Implementation spec for the worker + reviewer.
> Source of truth: `documents/specs/activity-group-enrollment-impl.md` Phase 6.1,
> `documents/specs/activity-group-enrollment.md` NFR-11 + Phase 6.
> Approach chosen by the user: **PILOT-TENANT OVERRIDE** (not a global flip).
> The global default stays **OFF**; a `TenantFeatureFlagOverride` row turns the
> flag **ON** for a single pilot tenant only.

---

## 1. Goal

Enable `FEATURE:EnableActivityGroups` for exactly one pilot tenant at seed time,
without changing the global default, by inserting a `TenantFeatureFlagOverride`
row (`IsEnabled = true`) for that tenant + flag. The override must be seeded by
the migration service, run **after** `TenantSeeder.SeedAsync()` (so the pilot
tenant id is known) and **after** `SeedEnableActivityGroupsAsync` (so the flag
exists), be **idempotent**, and record a `FlagAuditEntry` (system actor) for
traceability. Document the override in `documents/configuration.md` §5.

### Why an override, not `appsettings.PilotTenant.json`

The impl checklist Phase 6.1 literally reads *"Default
`FEATURE:EnableActivityGroups` ON in `appsettings.PilotTenant.json`; update
`documents/configuration.md` §2."* That wording predates the centralized Settings
feature-flag model (`documents/configuration.md` §5 — runtime, mutable,
tenant-overridable flags owned by the Settings bounded context and resolved by
`ConfigFeatureFlagService`). Under that model the correct, audit-traceable,
tenant-scoped mechanism is a `TenantFeatureFlagOverride` row, not a per-tenant
appsettings file. The user explicitly chose the override approach. This plan
therefore **does not** create `appsettings.PilotTenant.json`; it seeds an
override row and updates `configuration.md` **§5** (where the runtime flag table
lives). The checklist text should be re-read under this model.

---

## 2. Pilot-tenant selection

- **Pilot tenant: `"Hydeson School"`** (one of the two sample tenants seeded by
  `TenantSeeder` — `src/SchoolCollab.MigrationService/Seeding/TenantSeeder.cs`,
  `SampleTenants`).
- The pilot tenant name is exposed as a `public const string PilotTenantName`
  on the new seeder so it is configurable in one place and assertable in tests.
  It is resolved to a `Guid` from the `Dictionary<string,Guid>` returned by
  `TenantSeeder.SeedAsync()` at runtime (no hardcoded Guid — matches the repo
  convention noted in `TenantSeeder`).
- `Little Legends` is intentionally **not** given an override (control tenant —
  the flag stays OFF there, proving the override is tenant-scoped).

---

## 3. Exact change list

### 3.1 NEW `src/SchoolCollab.MigrationService/Seeding/PilotActivityGroupFlagOverrideSeeder.cs`

A `public sealed class` mirroring `TenantSeeder` (same DI shape, same
idempotent-by-existence pattern). Constructor-injects `SettingsDbContext`,
`ITenantContextAccessor`, `ILogger<PilotActivityGroupFlagOverrideSeeder>`.

Public surface:

```csharp
public sealed class PilotActivityGroupFlagOverrideSeeder(
    SettingsDbContext db,
    ITenantContextAccessor tenantContextAccessor,
    ILogger<PilotActivityGroupFlagOverrideSeeder> logger)
{
    public const string PilotTenantName = "Hydeson School";
    private const string ActorId = "system:migrator";
    private const string ActorName = "Migration Service";

    public async Task SeedAsync(IReadOnlyDictionary<string, Guid> tenantIdsByName, CancellationToken ct = default);
}
```

`SeedAsync` behavior (in order):

1. **Resolve pilot tenant id.** Look up `PilotTenantName` in `tenantIdsByName`.
   If absent, log a warning (`"Pilot tenant {Name} not found in seeded tenant registry; skipping activity-groups override"`) and return. (Defensive — TenantSeeder runs first, so this should not happen in normal flow.)
2. **Resolve the flag.** Compute `var key = FeatureFlag.NormalizeKey(FeatureFlagKeys.EnableActivityGroups);` and load the live flag row (`f.Key == key && !f.IsDeleted`). If absent, log a warning and return (defensive — `SeedEnableActivityGroupsAsync` runs first). Use a plain `FirstOrDefaultAsync` (no `IgnoreQueryFilters` needed — the flag is global/`TenantId == null`, visible under the default context).
3. **Idempotency check.** Query existing override cross-tenant:
   ```csharp
   var existing = await db.TenantFlagOverrides
       .IgnoreQueryFilters(["Tenant"])
       .AnyAsync(o => o.TenantId == pilotTenantId
                   && o.FeatureFlagId == flag.Id
                   && !o.IsDeleted, ct);
   ```
   If `existing`, log `"Pilot override for {Tenant}/{Flag} already present; skipping"` and return (no audit row — a re-run is a true no-op, mirroring the other seeders' skip behavior).
4. **Create + audit under the pilot tenant.** The override row is a strict tenant entity owned by `pilotTenantId`, but the migration service runs under the default (`Guid.Empty`) context. Use the sanctioned cross-tenant write path the `UpsertTenantFlagOverrideHandler` already uses — `tenantContextAccessor.RunWithExplicitTenantAsync(pilotTenantId, …)` — so the save-guard's `PrepareChanges` stamps the correct `TenantId` and accepts the write:
   ```csharp
   await tenantContextAccessor.RunWithExplicitTenantAsync(pilotTenantId, async ct2 =>
   {
       var reason = $"Pilot rollout: enable activity groups for '{PilotTenantName}' (Phase 6.1, NFR-11). Global default remains OFF.";
       var overrideRow = TenantFeatureFlagOverride.Create(
           tenantId: pilotTenantId,
           featureFlagId: flag.Id,
           isEnabled: true,
           value: null,
           reason: reason,
           effectiveFrom: null,
           effectiveTo: null);
       db.TenantFlagOverrides.Add(overrideRow);

       db.FlagAuditEntries.Add(FlagAuditEntry.Create(
           tenantId: pilotTenantId,
           featureFlagId: flag.Id,
           featureFlagKey: flag.Key,
           changeKind: FlagChangeKind.OverrideCreated,
           previousIsEnabled: null,   // no prior tenant override
           newIsEnabled: true,
           reason: reason,
           actorId: ActorId,
           actorDisplayName: ActorName));

       await db.SaveChangesAsync(ct2);
       return 0;
   }, ct);
   ```
   `EffectiveFrom`/`EffectiveTo` are `null` ⇒ `IsInEffectAt(now)` is always true
   ⇒ the resolver (`ResolveFlagsForTenantHandler`) pins the flag ON for the
   pilot tenant immediately and indefinitely, while the global default stays
   OFF for every other tenant.
5. Log success: `"Seeded pilot override for {Tenant}/{Flag} (IsEnabled=true)"`.

Notes for the worker:
- Use the **value-valued** `TenantFeatureFlagOverride.Create(..., value, ...)` 8-arg overload (pass `value: null`) — `EnableActivityGroups` is a boolean flag.
- Do **not** enqueue an outbox/integration event from the seeder. The command handler enqueues `FeatureFlagChanged`, but the seeder runs during migration, before the outbox dispatcher is consuming; the other flag seeders in `Program.cs` deliberately do not publish events either. The audit row is the traceability record. (If a reviewer asks: the seeded override is read by `ConfigFeatureFlagService` on next resolution/cache refresh; no event is required for correctness.)
- `FlagChangeKind.OverrideCreated` (= 9) is the correct kind for a brand-new override row (matches `UpsertTenantFlagOverrideHandler` create branch).

### 3.2 EDIT `src/SchoolCollab.MigrationService/Program.cs`

- Register the seeder in the DI section next to `TenantSeeder`:
  ```csharp
  builder.Services.AddScoped<PilotActivityGroupFlagOverrideSeeder>();
  ```
- In the Settings seed block, **capture** the tenant-id dictionary returned by `TenantSeeder` and call the new seeder **immediately after** it (so the pilot tenant id is available; the flag was already seeded earlier by `SeedEnableActivityGroupsAsync`):
  ```csharp
  var tenantIdsByName = await tenantSeeder.SeedAsync();

  // Phase 6.1: turn FEATURE:EnableActivityGroups ON for the pilot tenant only
  // (TenantFeatureFlagOverride). Global default stays OFF. Runs AFTER the flag
  // seed and AFTER the tenant seed. Idempotent. See documents/specs/plan-phase6-1.md.
  var pilotOverrideSeeder = scope.ServiceProvider.GetRequiredService<PilotActivityGroupFlagOverrideSeeder>();
  await pilotOverrideSeeder.SeedAsync(tenantIdsByName);
  ```
- Replace the existing `await tenantSeeder.SeedAsync();` line (which currently discards the return value) with the `var tenantIdsByName = …` capture above.
- Add the necessary `using SchoolCollab.Core.Features;` is already present; ensure `ITenantContextAccessor` is available — it is resolved inside the seeder, so no extra using is needed in `Program.cs` beyond the `PilotActivityGroupFlagOverrideSeeder` type (in `SchoolCollab.MigrationService.Seeding`, same namespace as `TenantSeeder` already imported).

### 3.3 EDIT `documents/configuration.md` §5 (`FeatureFlags — central configuration service`)

- In the **"Runtime flags (current)"** table, update the
  `FEATURE:EnableActivityGroups` row's **Notes** to state that the global default
  remains OFF and that the migration service additionally seeds a
  `TenantFeatureFlagOverride` turning the flag ON for the pilot tenant
  `Hydeson School` only (Phase 6.1). Keep the existing description of what the
  flag gates.
- Add a short subsection immediately after the "Runtime flags (current)" table
  titled **"Pilot-tenant override (Phase 6.1)"** documenting:
  - Mechanism: `TenantFeatureFlagOverride` row (`IsEnabled = true`,
    `EffectiveFrom`/`EffectiveTo` null → always in effect), seeded idempotently
    by `SchoolCollab.MigrationService` after the tenant + flag seeds.
  - Pilot tenant: `Hydeson School` (configurable via
    `PilotActivityGroupFlagOverrideSeeder.PilotTenantName`).
  - Traceability: a `FlagAuditEntry` with `ChangeKind = OverrideCreated`,
    actor `system:migrator` / "Migration Service", is written with the override.
  - Effect: `ConfigFeatureFlagService` (`ResolveFlagsForTenantHandler`) resolves
    the flag to ON for the pilot tenant and to the global OFF default for every
    other tenant. To turn the pilot off, delete the override row via the admin
    `/config-flags` tenant-override surface (or run the
    `DeleteTenantFlagOverride` command).
- Do **not** touch §2 (AppHost `Parameters:`) — this flag is not an AppHost
  parameter; the checklist's "§2" wording is superseded (see §1 of this plan).

### 3.4 NEW `tests/SchoolCollab.Settings.Tests.Unit/Tenancy/PilotActivityGroupFlagOverrideSeederTests.cs`

A focused unit test for the new seeder. Uses `SettingsDbContext` with the
InMemory provider + the real `AddTenancy()` (so `ITenantContextAccessor` is the
production `TenantContextAccessor`). Mirrors the InMemory + `AddTenancy` setup
already used by `MigrationServiceTenancyTests.cs`.

Required `using`s include `SchoolCollab.MigrationService.Seeding`,
`SchoolCollab.Core.Features`, `SchoolCollab.Settings.Core.Domain`,
`SchoolCollab.Core.Tenancy`.

Test cases (all via the seeder's `SeedAsync`):

1. **`Seeds_Override_And_Audit_When_Pilot_Tenant_And_Flag_Exist`** — Given an
   InMemory `SettingsDbContext` with a global `FeatureFlag`
   (`FEATURE:EnableActivityGroups`, `IsEnabled = false`) and a `Tenant`
   `"Hydeson School"`, when `SeedAsync(tenantIdsByName)` is called, then exactly
   one `TenantFeatureFlagOverride` row exists for the pilot tenant + flag with
   `IsEnabled = true`, a non-empty `Reason`, null `Value`, null
   `EffectiveFrom`/`EffectiveTo`; and exactly one `FlagAuditEntry` exists with
   `ChangeKind = OverrideCreated`, `TenantId == pilotTenantId`,
   `NewIsEnabled == true`, `PreviousIsEnabled == null`, `ActorId ==
   "system:migrator"`. Assert the override `TenantId == pilotTenantId`.
2. **`Is_Idempotent_Second_Run_Is_NoOp`** — Call `SeedAsync` twice on the same
   setup. Assert exactly one override row and exactly one audit entry remain
   (the second run skips without writing a second audit).
3. **`Skips_When_Pilot_Tenant_Not_In_Dictionary`** — Given the flag exists but
   the tenant dictionary is empty (`new Dictionary<string,Guid>()`), when
   `SeedAsync` is called, then zero override rows and zero audit entries are
   created (and no exception is thrown).
4. **`Skips_When_Flag_Not_Seeded_Yet`** — Given the tenant exists but no
   `FEATURE:EnableActivityGroups` flag row exists, when `SeedAsync` is called,
   then zero override rows and zero audit entries are created (defensive — no
   crash).

### 3.5 EDIT `tests/SchoolCollab.Settings.Tests.Unit/SchoolCollab.Settings.Tests.Unit.csproj`

Add a project reference to the migration service so the test can reach the new
seeder class (one line in the existing `<ItemGroup>` of `<ProjectReference>`s):

```xml
<ProjectReference Include="..\..\src\SchoolCollab.MigrationService\SchoolCollab.MigrationService.csproj" />
```

This is the only test-infra change; no new test project is created (keeps scope
tight). The MigrationService assembly already references Settings.Core
(the DbContext + domain types the test uses), so the reference graph is
consistent.

---

## 4. Files touched (summary)

| File | Action |
| :--- | :--- |
| `src/SchoolCollab.MigrationService/Seeding/PilotActivityGroupFlagOverrideSeeder.cs` | NEW |
| `src/SchoolCollab.MigrationService/Program.cs` | EDIT (register + call seeder after `TenantSeeder`) |
| `documents/configuration.md` | EDIT §5 (flag table note + new "Pilot-tenant override (Phase 6.1)" subsection) |
| `tests/SchoolCollab.Settings.Tests.Unit/Tenancy/PilotActivityGroupFlagOverrideSeederTests.cs` | NEW |
| `tests/SchoolCollab.Settings.Tests.Unit/SchoolCollab.Settings.Tests.Unit.csproj` | EDIT (add MigrationService ProjectReference) |

No DB migration is required — `TenantFeatureFlagOverride` + `FlagAuditEntry`
tables already exist (Settings context). No entity/config changes. No new
feature-flag row.

---

## 5. Acceptance criteria

1. **Pilot override seeded ON** for tenant `Hydeson School` + flag
   `FEATURE:EnableActivityGroups` with `IsEnabled = true`, `Value = null`,
   null effective window, and a clear `Reason` mentioning the pilot rollout.
2. **Idempotent** — a second migration run does not create a duplicate override
   row and does not create a second audit entry (existence check short-circuits).
3. **Runs after both the flag seed and the tenant seed** — verified by call
   order in `Program.cs` (flag seed earlier in the block; `TenantSeeder` then
   the new seeder, consuming the returned tenant-id dictionary).
4. **Audit traceability** — one `FlagAuditEntry` with
   `ChangeKind.OverrideCreated`, `TenantId = pilotTenantId`,
   `NewIsEnabled = true`, `PreviousIsEnabled = null`, system actor
   `system:migrator` / "Migration Service", and the same `Reason` as the
   override.
5. **Global default unchanged** — `SeedEnableActivityGroupsAsync` still seeds
   the flag with `isEnabled: false`; no edit to that method; the only ON state
   is the per-tenant override.
6. **Documentation updated** — `documents/configuration.md` §5 documents the
   pilot-tenant override (mechanism, pilot tenant, audit, effect, how to turn
   it off).
7. **Tests pass** — the 4 new unit tests pass; `dotnet build` for the solution
   (or at least the Settings.Tests.Unit + MigrationService projects) succeeds.
8. **No scope creep** — no `appsettings.PilotTenant.json` created, no global
   flip, no entity/migration changes, no changes to other flag seeds.

---

## 6. Test expectations

- New unit tests: 4 (listed in §3.4). All must pass.
- Build: `dotnet build src/SchoolCollab.MigrationService/SchoolCollab.MigrationService.csproj`
  and `dotnet build tests/SchoolCollab.Settings.Tests.Unit/SchoolCollab.Settings.Tests.Unit.csproj`
  both succeed.
- Test run: `dotnet test tests/SchoolCollab.Settings.Tests.Unit/SchoolCollab.Settings.Tests.Unit.csproj`
  — new tests pass and no existing `MigrationServiceTenancyTests` regress.
- Manual/integration (not blocking, but the real end-to-end proof): run the
  migration service against a fresh dev DB and verify via the admin
  `/config-flags` tenant-override view (or a `ResolveFlagsForTenant` query) that
  `EnableActivityGroups` resolves ON for `Hydeson School` and OFF for
  `Little Legends`. The unit tests stand in for this in CI.

---

## 7. Residual risks / notes

- **Cache refresh:** `ConfigFeatureFlagService` uses a HybridCache L1/L2. After
  the seeder writes the override, a running API/Admin may serve the stale global
  default until the cache TTL elapses or the override mutation invalidates it.
  This only affects a *running* system during a re-seed; a fresh migrator run
  precedes app start in the Aspire topology, so the cache is cold. Non-blocking.
- **Pilot tenant choice is a constant**, not env-driven. If operators need a
  different pilot tenant without a code change, a follow-up can lift
  `PilotTenantName` to configuration. Out of scope for 6.1 (the task explicitly
  allows "choose Hydeson School, or make it configurable"; we choose the former
  for simplicity).
- **No outbox event** is published by the seeder (see §3.1 note). This is
  consistent with the existing `Program.cs` flag seeds. A reviewer may flag this;
  the rationale is documented in the seeder.
- **`appsettings.PilotTenant.json` is intentionally NOT created** — diverges
  from the literal checklist wording, per the user's chosen override approach
  and the centralized flag model (see §1).

---

## 8. Acceptance

> Performed by the Phase-6.1 orchestrator after the worker's implementation and
> the reviewer's report. Review report persisted at
> `documents/specs/review-phase6-1.md`.

### Per-criterion verdict

| # | Criterion | Verdict | Evidence |
| --- | --- | --- | --- |
| 1 | Pilot override seeded ON for `Hydeson School` + `FEATURE:EnableActivityGroups`, `IsEnabled=true`, `Value=null`, null effective window, pilot-rollout `Reason` | **PASS** | `PilotActivityGroupFlagOverrideSeeder.cs`: `PilotTenantName` (L38), `TenantFeatureFlagOverride.Create(isEnabled:true, value:null, effectiveFrom:null, effectiveTo:null, reason:…)` (L91–100) |
| 2 | Idempotent — second run creates no duplicate override and no second audit | **PASS** | Cross-tenant existence check `IgnoreQueryFilters(["Tenant"]).AnyAsync(...)` short-circuits before any write (L74–84); `Is_Idempotent_Second_Run_Is_NoOp` asserts 1 override + 1 audit after 2 runs |
| 3 | Runs after the flag seed AND the tenant seed | **PASS** | `Program.cs`: `SeedEnableActivityGroupsAsync` (L98) → `var tenantIdsByName = await tenantSeeder.SeedAsync()` (L109) → `pilotOverrideSeeder.SeedAsync(tenantIdsByName)` (L115) |
| 4 | Audit traceability — one `FlagAuditEntry`, `OverrideCreated`, `TenantId=pilot`, `NewIsEnabled=true`, `PreviousIsEnabled=null`, actor `system:migrator`/`Migration Service`, same reason | **PASS** | `FlagAuditEntry.Create(... OverrideCreated, previousIsEnabled:null, newIsEnabled:true, actorId:ActorId, actorDisplayName:ActorName)` inside `RunWithExplicitTenantAsync` (Seeder.cs L102–109); happy-path test asserts all fields |
| 5 | Global default unchanged — flag still seeded `isEnabled:false`; no global flip | **PASS** | `Program.cs` `SeedEnableActivityGroupsAsync` `isEnabled: false` (L336–342); no edit to that method; only ON state is the per-tenant override |
| 6 | `documents/configuration.md` §5 documents the override (mechanism, pilot tenant, audit, effect, how to turn off) | **PASS** | `configuration.md` §5 runtime-flags table note updated + new "Pilot-tenant override (Phase 6.1)" subsection (mechanism, pilot tenant, traceability, effect, disable via admin `/config-flags` or `DeleteTenantFlagOverride`) |
| 7 | Tests pass + build succeeds | **PASS** | `dotnet build SchoolCollab.sln` → 0 errors. `dotnet test` Settings.Unit 446/446, Students 316/316, Admin 477/477 — all green (incl. the 4 new seeder tests) |
| 8 | No scope creep — no `appsettings.PilotTenant.json`, no global flip, no entity/migration changes, no edits to other flag seeds | **PASS** | `git status`: no `appsettings.PilotTenant.json`; no new migration/entity files; `SeedEnableActivityGroupsAsync` untouched; only the 5 planned files changed |

### Overall verdict

**CLOSED.**

All eight acceptance criteria are satisfied. The implementation matches the
plan exactly: a single new seeder class, the planned `Program.cs` wiring
(register + capture `tenantIdsByName` + call after both seeds), the §5
documentation, the 4 new unit tests, and the one `.csproj` project reference.
Build succeeds (0 errors) and all three relevant unit test projects pass
(Settings.Unit 446, Students 316, Admin 477). Nothing is staged.

### Residual items

- **None blocking.** All criteria green; build + tests executed during the
  acceptance pass.
- **Deferred (out of scope, documented in §7):** `PilotTenantName` is a
  compile-time constant rather than env-driven configuration; a follow-up can
  lift it to configuration if operators need a different pilot tenant without a
  code change. Non-blocking for 6.1.
- **Not exercised here (manual/integration, per §6):** an end-to-end migration
  against a fresh dev DB confirming `ResolveFlagsForTenant` returns ON for
  `Hydeson School` and OFF for `Little Legends`. The 4 unit tests stand in for
  this in CI; the real end-to-end run is non-blocking.
- **Cache refresh:** a *running* API/Admin may serve the stale global OFF until
  the HybridCache TTL elapses after a re-seed. A fresh migrator run precedes app
  start in the Aspire topology, so the cache is cold at startup. Non-blocking.
- **No outbox event** published by the seeder — consistent with the existing
  `Program.cs` flag seeds; rationale documented on the seeder class.