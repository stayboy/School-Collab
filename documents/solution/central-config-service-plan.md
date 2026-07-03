# Central Config Service for Feature Flags — Implementation Plan

> **Status:** Active plan (replaces the prior `central-config-feature-flags-design.md`,
> which was dropped because the team moved away from it).
> **Authority:** This document is the source of truth for the Config feature-flag
> service. Follow it; do not re-litigate the decisions below unless the user asks.

## 1. Goal

A real central service that owns the source of truth for **runtime, mutable,
tenant-overridable** feature flags, with server-side caching, change
notifications, audit logging, and a real admin UI. The first concrete consumer
is **gating the AI chat on the CodedValues landing page** via
`FEATURE:EnableCodedValuesAiChat`.

## 2. Key decisions (do not relitigate)

1. **4-project bounded-context split**, matching every other context:
   `SchoolCollab.Config.Core` / `.Contracts` / `.Api` / `.Admin`.
   This solves the "Worker must not pull in `AspNetCore.App`" problem for free
   (Worker references only `.Core`, a plain `Microsoft.NET.Sdk` library).
2. **`FEATURE:DisableOIDCAuth` stays a deployment-time `IConfiguration` value.**
   It is a *startup auth-mode switch*, not a runtime flag — ASP.NET Core auth
   schemes are registered once at startup in
   `AuthTenancyExtensions.AddAuthAndTenancy`, so they cannot be flipped at
   runtime. The Config DB owns only *runtime, mutable, tenant-overridable*
   flags. `DisableOIDCAuth` is **not** migrated into the DB and is **not**
   seeded by the migrator.
3. **Propagation: ITL primary, push as a follow-up (v1.1).** The repo has no
   RabbitMQ consumer infrastructure (only the outbox publisher). v1 ships the
   *publisher* (every mutation enqueues `FeatureFlagChanged` via the existing
   outbox → `config` exchange) plus L1 30s / L2 5min TTLs (worst-case staleness
   30s warm / 5min cold — meets the "sensible ITL" bar). v1.1 adds a per-consumer
   `ConfigCacheInvalidationWorker` subscribing to `flags.changed.*` — reuses the
   v1-published events, no redesign. The user explicitly accepted ITL as sufficient.
4. **Per-tenant bulk cache key** `cfg:flags:{tenantId|GLOBAL}` (the whole resolved
   set per tenant), not per-key — one HTTP round-trip on cold start, not N.
5. **Boolean only in v1.** No `DefaultValue` text column (YAGNI); add later via
   one-line migration if String/Number/Json kinds are needed.
6. **Audit is local + transactional**, not via the outbox: `FeatureFlagAuditor`
   writes `FlagAuditEntry` in the same `SaveChanges` transaction as the mutation.
7. **`flag_admin` OIDC role** gates write endpoints; read endpoints are
   cookie-authed; `GET /api/features/global` is allow-anonymous for consumer
   startup. Read endpoints return only `Key` + `IsEnabled` (no value payloads).
8. **Seed flag**: `FEATURE:EnableCodedValuesAiChat` (Boolean, default `true`).
   This is the first real consumer and the Playwright smoke target.

## 3. Project layout

```
src/Config/
  SchoolCollab.Config.Core/         Domain, Data, CQRS, DTOs, Services, Caching
  SchoolCollab.Config.Contracts/    integration event records
  SchoolCollab.Config.Api/          ASP.NET host, endpoints, auth wiring
  SchoolCollab.Config.Admin/        Blazor pages + ModuleServices.AddConfigModule()
tests/
  SchoolCollab.Config.Tests.Unit/
  SchoolCollab.Config.Tests.Integration/
```

## 4. Domain model

### 4.1 `FeatureFlag` (global blueprint — NOT tenant-scoped)
`IEntity`, `IAuditableEntity`, `ISoftDeletableEntity`, `IHasRowVersion`.
- `Key` (unique, `FEATURE:<Area>`), `Name`, `Description?`, `Kind` (enum,
  `Boolean` only in v1), `IsEnabled` (the default state), `IsArchived`.
- Factory `Create`; mutations `Rename`, `SetDescription`, `Enable`, `Disable`,
  `Archive`, `Unarchive`, `Delete`, `Recover`.

### 4.2 `TenantFeatureFlagOverride` : `BaseTenantEntityWithAudit`, `IHasRowVersion`
- `FeatureFlagId` (FK → `FeatureFlag.Id`, cascade), `IsEnabled` (bool? — null =
  inherit global), `Reason` (required, server-validated), `EffectiveFrom?`,
  `EffectiveTo?`.
- Unique partial index `(TenantId, FeatureFlagId) WHERE is_deleted = false`.
- Mirrors `TenantCodedValueOverride` exactly.

### 4.3 `FlagAuditEntry` : `IEntity`, `IAuditableEntity` (append-only, NO soft-delete)
- `TenantId?` (null=global), `FeatureFlagId`, `FeatureFlagKey` (denormalized),
  `ChangeKind` enum, `PreviousIsEnabled?`, `NewIsEnabled?`, `Reason?`,
  `ActorId`, `ActorDisplayName`, `OccurredAt`.
- Indexes `(feature_flag_id, occurred_at desc)`, `(tenant_id, occurred_at desc)`.

### 4.4 `FeatureFlagChanged` (integration event, in `.Contracts`)
Record published via `IIntegrationEventPublisher.EnqueueAsync` → outbox →
`config` topic exchange, routing key `flags.changed`.

## 5. Storage (Postgres, snake_case) — three tables
`feature_flags`, `tenant_flag_overrides`, `flag_audit_entries`. Schema follows
§4. EF configs use the shared helpers: `ConfigureGuidId`, `ConfigureAuditProperties`,
`ConfigureSoftDeleteProperties`, `ConfigureSoftDeleteQueryFilter`,
`ConfigurePostgresRowVersion`, and `TenantEntityTypeConfigurationBase` for the
override (tenant query filter). `ConfigDbContext` extends `ModuleDbContext` and
applies `OutboxMessageConfiguration(OutboxMapping.FlagsFor<ConfigDbContext>())`
— same shape as `CodedValuesDbContext`.

## 6. Resolver + caching

- `IFeatureFlagResolver.ResolveAsync(key, tenantId?)` →
  `ResolvedFlag(Key, IsEnabled, Source, ResolvedAt)`, `Source ∈
  {TenantOverride, GlobalDefault, ConfigurationFallback}`. Logic:
  tenant override (within effective window) ?? global default ?? `IConfiguration`.
  Same `override ?? global` shape as `CodedValueResolver`.
- `ConfigFeatureFlagService : IFeatureFlagService` implements the existing sync
  API via `ITenantProvider` + adds async methods (default-interface impls keep
  old callers compiling). Backed by **HybridCache** (L1 in-proc 30s, L2 Redis
  5min) — first `AddHybridCache()` call in the repo. Cache key
  `cfg:flags:{tenantId|GLOBAL}`.
- **Fallback**: on HTTP failure, read `IConfiguration["FeatureFlags:FEATURE:..."]`;
  warn-once per key via a `HashSet<string>` guard.

### 6.1 Consumer-side wiring extension (in `.Core`)
```csharp
// SchoolCollab.Config.Core/ConfigFeatureFlagClientExtensions.cs
public static IServiceCollection AddConfigFeatureFlagClient(
    this IServiceCollection services, IConfiguration configuration)
{
    services.AddHybridCache();
    services.AddHttpClient("config-api", c => c.BaseAddress = new Uri("https+http://config-api"));
    services.Replace(ServiceDescriptor.Singleton<IFeatureFlagService, ConfigFeatureFlagService>());
    return services;
}
```
Called by **every** flag consumer host after `AddAuthAndTenancy(...)`: the Admin
host, `coded-values-api`, `assignments-api`, `students-api`, `students-worker`.
`AddAuthAndTenancy` keeps registering the `IConfiguration`-only fallback via
`TryAddSingleton`; this extension `Replace`s it. The startup auth-mode read inside
`AddAuthAndTenancy` reads `IConfiguration` directly, so ordering is safe.

### 6.2 `IFeatureFlagService` extension (in `SchoolCollab.Core`)
Add async methods with default-interface implementations that delegate to the
sync ones (so existing `ConfigurationFeatureFlagService` still compiles):
```csharp
Task<bool> IsEnabledAsync(string key, CancellationToken ct = default);          // current tenant
Task<bool> IsEnabledAsync(string key, Guid? tenantId, CancellationToken ct);   // explicit
```
Rename the old `FeatureFlagService` → `ConfigurationFeatureFlagService` (still
in Core) and keep it as the fallback impl.

## 7. Audit
`FeatureFlagAuditor` writes `FlagAuditEntry` in the same transaction. Actor from
`ClaimsPrincipal` via `IActorAccessor` (`sub` + `name` claims, or
`system:<service>` for the migrator). `Reason` is a required command field.

## 8. Admin UI (mounted in the unified `SchoolCollab.Admin` host)
- `ConfigFlags.razor` (`/config-flags`): FluentDataGrid list + search + "New
  flag" + "Show archived" — cloned from `CodedValues/Index.razor`.
- `ConfigFlagDetail.razor` (`/config-flags/{key}`): header, `FluentSwitch` for
  global state (Reason required on save), danger zone (archive/unarchive/delete),
  tenant-override sub-grid + `OverrideDialog.razor`, per-flag audit grid.
- `ConfigFlagsApiClient` in `SchoolCollab.Admin.Shared/Services`
  (`BaseAddress = "https+http://config-api"`, mirrors `CodedValuesApiClient`).
- `ModuleServices.AddConfigModule()` in `.Admin` registering the HttpClient.
- Wire `AddConfigModule()` in `SchoolCollab.Admin/Program.cs`; add
  `AddAdditionalAssemblies(typeof(Config.Admin.Components._Imports).Assembly)`;
  NavMenu link under "Admin Modules".
- bUnit tests in `SchoolCollab.Admin.Tests.Unit` (MSTest, matching existing style).

## 9. First consumer: CodedValues landing page AI chat
The flag `FEATURE:EnableCodedValuesAiChat` gates the three AI-chat surfaces on
`src/CodedValues/SchoolCollab.CodedValues.Admin/Components/Pages/CodedValues/Index.razor`:
1. The toolbar "✨ Chat" `FluentButton` (~line 38).
2. The pinned-bottom inline `CodedValuesChat` (InputOnly) in
   `SectionContent SectionName="page-footer"`.
3. The `CodedValuesChatPanel` side drawer.

Edit:
```razor
@inject IFeatureFlagService FeatureFlags
@code {
    private bool _aiChatEnabled = true;   // optimistic first paint
    protected override async Task OnInitializedAsync()
    {
        // existing load logic unchanged
        _aiChatEnabled = await FeatureFlags.IsEnabledAsync("FEATURE:EnableCodedValuesAiChat");
    }
}
```
Wrap each of the three surfaces in `@if (_aiChatEnabled) { ... }`. **No new project
reference** — `IFeatureFlagService` is in `SchoolCollab.Core`, which
`CodedValues.Admin` already references (transitively via the host). The host owns
the `ConfigFeatureFlagService` registration.

**Cold-start fallback** in `SchoolCollab.Admin/appsettings.json` only (NOT an
AppHost Parameter — that pattern is retired for runtime flags):
```json
"FeatureFlags": { "FEATURE": { "EnableCodedValuesAiChat": "true" } }
```

**Tenant-override demo**: global `true`; tenant X (no AI entitlement)
`TenantFeatureFlagOverride(IsEnabled=false, Reason="No AI entitlement")`.
Tenant X sees no chat within L1 30s; everyone else unaffected.

## 10. AppHost + MigrationService wiring
- `config-db = postgres.AddDatabase("config-db")`.
- `config-api = AddProject<SchoolCollab_Config_Api>("config-api")` with
  `WithReference(config-db/rabbit/redis)`,
  `WithEnvironment("Outbox__ExchangeName", configOutboxExchange)`,
  `WaitFor(rabbit/redis)`, `WaitForCompletion(migrator)`.
- New `outbox-exchange-config` parameter.
- Consumers get `WithReference(configApi)` (service discovery) but **not**
  `WaitFor(configApi)` — the `IConfiguration` fallback covers cold starts.
- **Remove** `Parameters:feature-flag-disable-oidc-auth` and all four
  `WithEnvironment("FeatureFlags__FEATURE__DisableOIDCAuth", …)`.
- MigrationService: 4th context (`ConfigDbContext` + `MigrateAsync` +
  `OutboxMapping.SetFlagsFor<ConfigDbContext>(…)`). Seed
  `FEATURE:EnableCodedValuesAiChat` (Boolean, `IsEnabled=true`) if absent, with
  audit actor `system:migrator`. **Do not** seed `DisableOIDCAuth`.

## 11. ArchitectureTests updates
`MigrationGuardTests` and `OutboxArchitectureTests` hard-code a `DomainCores`
array of three assemblies and a `DiscoversAllKnownDesignTimeFactories` assertion.
Add `ConfigCore` (`typeof(ConfigDbContext).Assembly`) to both arrays and add a
`ConfigDbContext` assertion. Add `Config.Core` to the ArchTests csproj
`ProjectReference`s.

## 12. Testing
- **Unit** (`Config.Tests.Unit`): resolver (override ?? global ?? fallback),
  auditor (writes row per mutation, carries actor+reason), domain state
  transitions, `ConfigFeatureFlagService` cache hit/miss/fallback (warn-once).
  Use `Microsoft.EntityFrameworkCore.InMemory` like the existing unit tests.
- **Integration** (`Config.Tests.Integration`): Testcontainers Postgres; endpoint
  CRUD + audit + tenant isolation + `flag.admin` authz. Reuse existing
  Testcontainers packages.
- **Admin bUnit** (`Admin.Tests.Unit`): mock `IFeatureFlagService.IsEnabledAsync`
  → `Index` renders chat button when true, omits when false.
- **Playwright smoke**: start AppHost → `/config-flags` → toggle
  `FEATURE:EnableCodedValuesAiChat` off (with reason) → `/coded-values` → assert
  "✨ Chat" button gone → toggle on → assert it returns → screenshot audit row.
- Keep existing `ProgramAuthFeatureFlagTests` unchanged (they drive
  `IConfiguration`, which is correct for the startup-mode `DisableOIDCAuth` flag).

## 13. Implementation order
1. Scaffold 4 projects + sln + references + ArchitectureTests arrays/csproj.
2. `Config.Core` domain entities + enums.
3. `Config.Core` Data: `ConfigDbContext` + configurations + design-time factory
   + `InitialCreate` migration (`dotnet ef migrations add InitialCreate`).
4. MigrationService: register + migrate Config; seed
   `FEATURE:EnableCodedValuesAiChat`.
5. `Config.Core` CQRS (commands: Create/Rename/Enable/Disable/Archive/Unarchive/
   Delete/Recover/UpsertOverride/DeleteOverride; queries: List/Get/ListAudit/
   ResolveForTenant) + `FeatureFlagAuditor` + `IActorAccessor`. Unit tests.
6. `Config.Core` Caching: `ConfigFeatureFlagService` + `IFeatureFlagResolver` +
   `AddHybridCache()` + `AddConfigFeatureFlagClient()`. Unit tests.
7. `Config.Contracts`: `FeatureFlagChanged` event.
8. `Config.Api`: endpoints + `flag.admin` authz + `AddOutbox<ConfigDbContext>` +
   Program.cs. Integration tests.
9. `Config.Admin`: pages + `AddConfigModule()`. bUnit tests.
10. `Admin.Shared`: `ConfigFlagsApiClient` + constants.
11. `SchoolCollab.Admin`: wire module, NavMenu, additional assembly,
    `AddConfigFeatureFlagClient`, appsettings fallback.
12. Core: extend `IFeatureFlagService` (async, default-interface impls); rename
    `FeatureFlagService` → `ConfigurationFeatureFlagService`; update
    `AuthTenancyExtensions` (keep startup `DisableOIDCAuth` read on `IConfiguration`).
13. Edit `CodedValues.Admin/Index.razor` to inject + resolve + gate the three
    chat surfaces.
14. AppHost wiring + parameter removal.
15. Docs: `configuration.md`, this doc, update `feature-flag-workflow.md`.
16. Build (0 warnings — repo culture), full test suite, Playwright smoke.

## 14. Explicit non-goals (v1)
- Not a generic config store — feature flags only.
- Not replacing AppHost `Parameters:` for infra values (DB/OIDC/AI endpoints).
- No String/Number/Json flag kinds (schema-ready via future migration only).
- No per-flag subscription routing, no kill-switch dependency graph, no
  multi-region.
- No RabbitMQ subscriber in v1 (publisher only; subscriber is v1.1).
- `DisableOIDCAuth` is not a Config flag.

## 15. v1.1 (follow-up, not blocking)
- `ConfigCacheInvalidationWorker` per consumer subscribing to `flags.changed.*`
  → `HybridCache.RemoveAsync(cfg:flags:{tenant})`. Narrows staleness from 30s
  to ~1s. Reuses v1-published events; no redesign.