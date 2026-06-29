# School-Collab Configuration Reference

This document is the **single source of truth** for every configuration
value that a developer, operator, or CI pipeline can set on the
School-Collab platform. It covers:

- The Aspire AppHost (infrastructure resources + shared secrets)
- Every bounded-context API and worker (per-service options)
- The centralised `SchoolCollab.Config` service
- The AI provider stack
- Authentication, feature flags, and logging
- Local-development defaults and production overrides
- Environment-variable mapping for container deployments

> **Conventions used in this document**
>
> - All paths are relative to the repository root (`C:\Users\skwar\source\repos\School-Collab`).
> - `appsettings.json` is the base file; `appsettings.{Environment}.json` overlays it.
> - ASP.NET Core's environment-variable provider maps `Section:Key` → `Section__Key`
>   (double underscore). Every key in this document has a matching env-var
>   recipe in [§11](#11-environment-variable-reference).
> - Secrets (passwords, API keys, OIDC client secrets) **must never** be committed
>   to `appsettings.json`. Use either Aspire `AddParameter(..., secret: true)`,
>   .NET user-secrets, or your deployment platform's secret store.
> - **Every change** to a configuration value (add, remove, rename, default change)
>   must update this document in the same PR — see
>   [`.github/copilot/rules/configuration-documentation.md`](../.github/copilot/rules/configuration-documentation.md).

---

## Table of Contents

1. [Topology overview](#1-topology-overview)
2. [Aspire AppHost — shared infrastructure](#2-aspire-apphost--shared-infrastructure)
3. [`Outbox` — transactional outbox dispatcher](#3-outbox--transactional-outbox-dispatcher)
4. [`Auth:Keycloak` — OIDC authentication](#4-authkeycloak--oidc-authentication)
5. [`FeatureFlags` — central configuration service](#5-featureflags--central-configuration-service)
6. [`Promotion` — Students.Worker scheduled job](#6-promotion--studentsworker-scheduled-job)
7. [AI provider configuration (`SchoolCollab.AI`)](#7-ai-provider-configuration-schoolcollabai)
8. [Connection strings (Aspire-injected)](#8-connection-strings-aspire-injected)
9. [Logging](#9-logging)
10. [Local-development quickstart](#10-local-development-quickstart)
11. [Environment-variable reference](#11-environment-variable-reference)
12. [Production checklist](#12-production-checklist)

---

## 1. Topology overview

School-Collab runs as an [Aspire](https://learn.microsoft.com/dotnet/aspire/) distributed
application. The composition is fixed by `src/AppHost/SchoolCollab.AppHost/Program.cs`:

```
                                ┌─────────────────────────────┐
                                │  SchoolCollab.AppHost (.NET) │
                                │  src/AppHost/                │
                                └──────────────┬───────────────┘
                                               │
            ┌──────────┬─────────────┬─────────┼─────────┬─────────────┬─────────────┐
            ▼          ▼             ▼         ▼         ▼             ▼             ▼
       postgres    rabbitmq       redis    migrator    config      (UI / APIs)
            │          │             │         │         │             │             │
            │          │             │         │         │             ▼             ▼
            │          │             │         │         │      coded-values-api   assignments-api
            │          │             │         │         │      coded-values-ai    students-api
            │          │             │         │         │      students-worker    admin
            │          │             │         │         │
            ▼          ▼             ▼         ▼         ▼
        coded-values-db        (RabbitMQ exchanges per bounded context)
        assignments-db
        students-db
```

**Resource names that appear in the Aspire service-discovery env-var prefix `services__<name>__<scheme>__0`:**

| Aspire name | Type | Used by |
| :--- | :--- | :--- |
| `postgres` | Container (`postgres`) | The three DBs |
| `rabbitmq` | Container (`rabbitmq`) | All APIs + Students.Worker |
| `cache` | Container (`redis`) | APIs + Worker |
| `config` | Project | All APIs + Admin |
| `migrator` | Project | (one-shot) |
| `coded-values-db` | Postgres database | migrator, coded-values-api |
| `assignments-db` | Postgres database | migrator, assignments-api |
| `students-db` | Postgres database | migrator, students-api, students-worker |
| `coded-values-api` | Project | admin, coded-values-ai |
| `coded-values-ai` | Project | admin |
| `assignments-api` | Project | admin |
| `students-api` | Project | admin |
| `students-worker` | Project | — |

> 📌 **Persistent volumes.** `postgres` and `rabbitmq` are wired with
> `WithDataVolume()` + `WithLifetime(ContainerLifetime.Persistent)`. The
> passwords must therefore remain **stable across AppHost sessions**; see
> [§2](#2-aspire-apphost--shared-infrastructure).

---

## 2. Aspire AppHost — shared infrastructure

Defined in `src/AppHost/SchoolCollab.AppHost/Program.cs`.

| Key / parameter | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `postgres-password` | Aspire secret parameter (`AddParameter`) | _none — must be supplied_ | Superuser password for the local Postgres container. **Pinned** so the persisted volume keeps working across runs. |
| `rabbitmq-password` | Aspire secret parameter (`AddParameter`) | _none — must be supplied_ | `RABBITMQ_DEFAULT_PASS` for the local RabbitMQ container. Pinned for the same reason as Postgres. |

**Where to set them:**

`src/AppHost/SchoolCollab.AppHost/appsettings.json`:

```json
{
  "Parameters": {
    "postgres-password": "<set via user-secrets or env-var>",
    "rabbitmq-password": "<set via user-secrets or env-var>"
  }
}
```

Or via the user-secrets store (preferred for local dev):

```bash
cd src/AppHost/SchoolCollab.AppHost
dotnet user-secrets set "Parameters:postgres-password" "postgres"
dotnet user-secrets set "Parameters:rabbitmq-password" "rabbit"
```

Or via env-vars (preferred for CI):

```bash
export Parameters__postgres-password=postgres
export Parameters__rabbitmq-password=rabbit
```

> ⚠️ **Why pinning matters.** Without a stable password, Aspire regenerates
> one on every run; the persisted data volume keeps the *previous*
> `postgres` / `guest` user, which silently breaks every connection with
> `password authentication failed for user "postgres"` / `invalid credentials`.

---

## 3. `Outbox` — transactional outbox dispatcher

The shared outbox dispatcher lives in `SchoolCollab.Core/Messaging/`
and is bound from the `Outbox` configuration section by
`OutboxExtensions.AddOutbox<TContext>(IConfiguration, string? sectionName)`.

**Type:** [`SchoolCollab.Core.Messaging.OutboxOptions`](../../src/SchoolCollab.Core/Messaging/OutboxOptions.cs)
**Section name:** `Outbox`

| Property | Default | Description |
| :--- | :--- | :--- |
| `ExchangeName` | _required_ | RabbitMQ topic exchange that dispatched events are published to. **Each bounded context must use a unique exchange name** so consumers can subscribe per-module. |
| `BatchSize` | `100` | Max rows claimed per `FOR UPDATE SKIP LOCKED` batch. |
| `PollInterval` | `00:00:01` | Idle delay between empty batches (TimeSpan, e.g. `00:00:05`). |

### Per-service overrides

Each bounded context that owns an `OutboxMessage` table sets its own
`ExchangeName` in `appsettings.json`:

| Project | Config file | Exchange name |
| :--- | :--- | :--- |
| `SchoolCollab.Students.Api` | `src/Students/SchoolCollab.Students.Api/appsettings.json` | `students` |
| `SchoolCollab.Students.Worker` | `src/Students/SchoolCollab.Students.Worker/appsettings.json` | `students` |
| `SchoolCollab.Assignments.Api` | `src/Assignments/SchoolCollab.Assignments.Api/appsettings.json` | `assignments` |
| `SchoolCollab.CodedValues.Api` | `src/CodedValues/SchoolCollab.CodedValues.Api/appsettings.json` | `coded-values` |

**Example** — `src/Students/SchoolCollab.Students.Api/appsettings.json`:

```json
{
  "Outbox": {
    "ExchangeName": "students"
  }
}
```

### Adding a new bounded context

To onboard a new `<Domain>.Core` (e.g. `SchoolCollab.Attendance.Core`):

1. Add an `Outbox` section to the new API's `appsettings.json`:

   ```json
   "Outbox": {
     "ExchangeName": "attendance"
   }
   ```

2. Call `services.AddOutbox<AttendanceDbContext>(builder.Configuration)` once
   during DI setup.
3. (Optional) Override `BatchSize` / `PollInterval` in `appsettings.Production.json`
   for production tuning.
4. The ArchTests in `tests/SchoolCollab.ArchitectureTests.Unit` will
   automatically enforce that no local `Messaging/` folder reappears.

See [`shared-kernel-extraction-pattern.md`](./solution/shared-kernel-extraction-pattern.md)
§3 for the full migration story.

---

## 4. `Auth:Keycloak` — OIDC authentication

Wired by `SchoolCollab.Core.Auth.AuthTenancyExtensions.AddAuthAndTenancy(IConfiguration)`
(used by every API + Admin).

| Key | Default | Description |
| :--- | :--- | :--- |
| `Auth:Keycloak:Authority` | `https://keycloak.local/realms/school-collab` | OIDC issuer URL (Keycloak realm URL). |
| `Auth:Keycloak:ClientId` | `school-collab-client` | OpenID Connect client ID. |
| `Auth:Keycloak:ClientSecret` | `secret` | OpenID Connect client secret. **Override in production.** |

**Example** — `src/SchoolCollab.Students.Api/appsettings.Production.json`:

```json
{
  "Auth": {
    "Keycloak": {
      "Authority": "https://login.example.com/realms/school-collab",
      "ClientId": "school-collab-prod",
      "ClientSecret": "<from secret store>"
    }
  }
}
```

> 🔐 **Secrets.** `ClientSecret` is the most sensitive value in the
> configuration tree. Use one of:
> - `dotnet user-secrets set "Auth:Keycloak:ClientSecret" "..."` (local dev)
> - Aspire `AddParameter(..., secret: true)` + reference (CI)
> - Azure Key Vault / your platform's secret manager (production)

When `FEATURE:DisableOIDCAuth` is enabled (see [§5](#5-featureflags--central-configuration-service)),
the OIDC registration is replaced with `TestAuthHandler`, which auto-authenticates
every request as a test user — intended for local development only.

---

## 5. `FeatureFlags` — central configuration service

SchoolCollab has **two** feature-flag surfaces:

1. **Local config** (in-process) — read from the `FeatureFlags` section of
   the running service's `appsettings.json`.
2. **Remote config** — fetched from the `SchoolCollab.Config` service
   (`GET /api/features`) and merged into `IConfiguration` at startup via
   `builder.Configuration.AddRemoteFeatureFlags("https+http://config")`.

> **Order matters.** The remote `FeatureFlags` overlay is added *before*
> any module's `appsettings.json` is built, so locally-defined flags take
> precedence when both are set. This lets you override a remote value
> during local dev.

The `SchoolCollab.Config` service has **no own `appsettings.json`**
(only `appsettings.Development.json`) and serves whatever its own
`FeatureFlags` section contains. The Admin + every API register the
remote overlay.

### Introduced flags

| Flag | Purpose | Default | Consumers |
| :--- | :--- | :--- | :--- |
| `FEATURE:DisableOIDCAuth` | Replace Keycloak OIDC with `TestAuthHandler` for local development. | `false` | `SchoolCollab.Admin`, `SchoolCollab.Assignments.Api`, `SchoolCollab.CodedValues.Api`, `SchoolCollab.Students.Api` |

### Configuring flags

Flags are read from the `FeatureFlags` section. **Set them in the
`SchoolCollab.Config/appsettings.Development.json` (local)** or pass
through the central API.

**Example** — `src/SchoolCollab.Config/appsettings.Development.json`:

```json
{
  "FeatureFlags": {
    "FEATURE:DisableOIDCAuth": "true"
  }
}
```

> 📝 The `:` in the key is intentional. `FeatureFlagService.CollectFlags`
> recurses into nested sections, so `FEATURE:DisableOIDCAuth` surfaces as
> the dotted key `FEATURE:DisableOIDCAuth` in `GET /api/features` — keep the
> colon when adding new flags.

### Using a flag in code

```csharp
public class MyService(IFeatureFlagService featureFlags)
{
    public void DoWork()
    {
        if (featureFlags.IsEnabled("FEATURE:MyNewFeature"))
        {
            // New logic
        }
    }
}
```

### Conditional authorization pattern

```csharp
var featureFlags = app.Services.GetRequiredService<IFeatureFlagService>();
var group = app.MapGroup("/api/my-feature");

if (!featureFlags.IsEnabled("FEATURE:DisableOIDCAuth"))
{
    group.RequireAuthorization();
}
```

See `src/SchoolCollab.Config/README.md` for the full flag catalogue.

---

## 6. `Promotion` — Students.Worker scheduled job

Defined in `src/Students/SchoolCollab.Students.Worker/Services/PromotionOptions.cs`
and bound via `builder.Configuration.GetSection(PromotionOptions.SectionName)`.

**Type:** [`SchoolCollab.Students.Worker.Services.PromotionOptions`](../../src/Students/SchoolCollab.Students.Worker/Services/PromotionOptions.cs)
**Section name:** `Promotion`

| Property | Default | Description |
| :--- | :--- | :--- |
| `CronExpression` | `0 2 * * *` | Human-readable cron expression for logging only — actual scheduling uses `PollInterval`. |
| `PollInterval` | `01:00:00` | How often the service polls for periods that need processing. |
| `ErrorDelay` | `00:05:00` | Delay after an error before retrying. |

**Example** — `src/Students/SchoolCollab.Students.Worker/appsettings.Production.json`:

```json
{
  "Promotion": {
    "CronExpression": "0 3 * * *",
    "PollInterval": "01:00:00",
    "ErrorDelay": "00:10:00"
  }
}
```

> 📝 The Worker currently has **no `appsettings.Development.json`**. Local
> dev inherits the defaults from `PromotionOptions`.

---

## 7. AI provider configuration (`SchoolCollab.AI`)

Defined in `src/SchoolCollab.AI/Program.cs`. Two providers are wired and
selected by `codedvalue-ai-provider` at startup.

| Key | Default | Description |
| :--- | :--- | :--- |
| `codedvalue-ai-provider` | `ollama` | Active provider name — `ollama` (local) or `openrouter` (cloud). |
| `Ollama:Endpoint` | `http://localhost:11434/v1` | Local Ollama OpenAI-compatible endpoint. |
| `Ollama:DefaultModel` | `gemma4:31b-cloud` | Model name to use when provider is `ollama`. |
| `OpenRouter:Endpoint` | `https://openrouter.ai/api/v1` | OpenRouter API base URL. |
| `OpenRouter:DefaultModel` | `google/gemma-4-31b-it:free` | Model name to use when provider is `openrouter`. |
| `OpenRouter:ApiKey` | _none — optional_ | OpenRouter API key. When absent, the provider logs a warning and the `openrouter` client is still registered but every request will fail at the provider. |

**Example** — `src/SchoolCollab.AI/appsettings.Development.json`:

```json
{
  "codedvalue-ai-provider": "ollama",
  "Ollama": {
    "Endpoint": "http://localhost:11434/v1",
    "DefaultModel": "gemma4:31b-cloud"
  }
}
```

**Production** — `src/SchoolCollab.AI/appsettings.Production.json`:

```json
{
  "codedvalue-ai-provider": "openrouter",
  "OpenRouter": {
    "Endpoint": "https://openrouter.ai/api/v1",
    "DefaultModel": "anthropic/claude-3.5-sonnet",
    "ApiKey": "<from secret store>"
  }
}
```

> 🔐 **Never commit `OpenRouter:ApiKey`** — use user-secrets locally and
> your platform's secret store in CI/production.

---

## 8. Connection strings (Aspire-injected)

Aspire injects connection strings into the apps that call
`.WithReference(<resource>)`. They land under the standard
`ConnectionStrings:<resource-name>` keys:

| Resource | Injected key | Used by |
| :--- | :--- | :--- |
| `coded-values-db` | `ConnectionStrings:coded-values-db` | `SchoolCollab.CodedValues.Api`, migrator |
| `assignments-db` | `ConnectionStrings:assignments-db` | `SchoolCollab.Assignments.Api`, migrator |
| `students-db` | `ConnectionStrings:students-db` | `SchoolCollab.Students.Api`, `SchoolCollab.Students.Worker`, migrator |
| `cache` | `ConnectionStrings:cache` | APIs + Worker (also surfaced as `Aspire:StackExchange:Redis:ConnectionString`) |
| `rabbitmq` | `ConnectionStrings:rabbitmq` | APIs + Worker (via `AddRabbitMQClient("rabbitmq")`) |

> 📝 You should **not** set these by hand in `appsettings.json` when
> running under Aspire — they will be overwritten by service-discovery.
> They are documented here only so you know what to expect when running
> a single API outside the AppHost (e.g. from VS / VS Code).

---

## 9. Logging

Every project has its own `Logging:LogLevel` block in `appsettings.json`.
The convention is:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

The `AppHost` overrides `Aspire.Hosting.Dcp` to `Warning` because that
category is very chatty at `Information`.

For `appsettings.Development.json` the convention is:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  }
}
```

Serilog request logging is enabled in the AI service only
(`app.UseSerilogRequestLogging()` in `src/SchoolCollab.AI/Program.cs`).

---

## 10. Local-development quickstart

The fastest path to a working local environment:

### 1. Clone + restore

```bash
git clone <repo>
cd School-Collab
dotnet restore
```

### 2. Set Aspire AppHost secrets (one-time)

```bash
cd src/AppHost/SchoolCollab.AppHost
dotnet user-secrets set "Parameters:postgres-password" "postgres"
dotnet user-secrets set "Parameters:rabbitmq-password" "rabbit"
```

### 3. (Optional) Disable OIDC for local dev

`src/SchoolCollab.Config/appsettings.Development.json` already has
`FEATURE:DisableOIDCAuth=true` so Keycloak is not required.

### 4. (Optional) Configure an AI provider

Edit `src/SchoolCollab.AI/appsettings.json` to switch
`codedvalue-ai-provider` between `ollama` (default, local) and `openrouter`
(cloud, requires `OpenRouter:ApiKey`).

### 5. Run

```bash
cd src/AppHost/SchoolCollab.AppHost
dotnet run
```

Aspire launches the dashboard and starts every project in dependency order.

### 6. Verify

```bash
# Feature flags (central config)
curl http://localhost:<config-port>/api/features

# AI provider
curl http://localhost:<ai-port>/api/ai/config
```

---

## 11. Environment-variable reference

ASP.NET Core maps `Section:Key` → `Section__Key` (double underscore).
Every key in this document has a matching env-var form:

| Config key | Env-var |
| :--- | :--- |
| `Parameters:postgres-password` | `Parameters__postgres-password` |
| `Parameters:rabbitmq-password` | `Parameters__rabbitmq-password` |
| `Outbox:ExchangeName` | `Outbox__ExchangeName` |
| `Outbox:BatchSize` | `Outbox__BatchSize` |
| `Outbox:PollInterval` | `Outbox__PollInterval` |
| `Auth:Keycloak:Authority` | `Auth__Keycloak__Authority` |
| `Auth:Keycloak:ClientId` | `Auth__Keycloak__ClientId` |
| `Auth:Keycloak:ClientSecret` | `Auth__Keycloak__ClientSecret` |
| `FeatureFlags:FEATURE:DisableOIDCAuth` | `FeatureFlags__FEATURE:DisableOIDCAuth` |
| `Promotion:CronExpression` | `Promotion__CronExpression` |
| `Promotion:PollInterval` | `Promotion__PollInterval` |
| `Promotion:ErrorDelay` | `Promotion__ErrorDelay` |
| `codedvalue-ai-provider` | `codedvalue-ai-provider` |
| `Ollama:Endpoint` | `Ollama__Endpoint` |
| `Ollama:DefaultModel` | `Ollama__DefaultModel` |
| `OpenRouter:Endpoint` | `OpenRouter__Endpoint` |
| `OpenRouter:DefaultModel` | `OpenRouter__DefaultModel` |
| `OpenRouter:ApiKey` | `OpenRouter__ApiKey` |
| `ConnectionStrings:coded-values-db` | `ConnectionStrings__coded-values-db` |
| `ConnectionStrings:assignments-db` | `ConnectionStrings__assignments-db` |
| `ConnectionStrings:students-db` | `ConnectionStrings__students-db` |
| `ConnectionStrings:cache` | `ConnectionStrings__cache` |
| `ConnectionStrings:rabbitmq` | `ConnectionStrings__rabbitmq` |

> ℹ️ `TimeSpan` values (`Outbox:PollInterval`, `Promotion:PollInterval`,
> `Promotion:ErrorDelay`) follow the standard .NET format: `dd.hh:mm:ss`
> or `hh:mm:ss`. Examples: `00:00:05`, `01:00:00`, `1.02:03:04`.

---

## 12. Production checklist

Before deploying, verify:

- [ ] **Persistent volumes** are backed up — `postgres` + `rabbitmq` data
      volumes are persistent; losing them loses all module data and any
      unprocessed outbox rows.
- [ ] **AppHost secrets** are sourced from a secret store, not committed:
      `Parameters:postgres-password`, `Parameters:rabbitmq-password`.
- [ ] **`Auth:Keycloak:ClientSecret`** is sourced from a secret store.
- [ ] **`FEATURE:DisableOIDCAuth`** is `false` (or omitted) in production.
- [ ] **OIDC `Authority`** points to the production Keycloak realm.
- [ ] **Outbox `ExchangeName`** matches the consumer subscriptions for
      each bounded context.
- [ ] **AI provider keys** are sourced from a secret store (`OpenRouter:ApiKey`).
- [ ] **Outbox migration safety** — when bumping `OutboxMessage` schema
      (e.g. the `ProcessedAt` → `DispatchedAt` rename in Phase 3), drain
      each service's outbox before applying the EF migration. See
      [`messaging-consolidation-plan.md`](./solution/messaging-consolidation-plan.md)
      §3.3 for the full deployment-order recipe.
- [ ] **Logging levels** are `Information` (or stricter) by default in
      production; override per-category for noisy modules.

---

## See also

- [`solution/shared-kernel-extraction-pattern.md`](./solution/shared-kernel-extraction-pattern.md)
  — the source of truth for *which* code goes in `SchoolCollab.Core` and
  how to extract duplicated types.
- [`solution/endpoint-organization-pattern.md`](./solution/endpoint-organization-pattern.md)
  — how endpoints are split into per-specialty files inside each API.
- [`solution/cqrs-organization-pattern.md`](./solution/cqrs-organization-pattern.md)
  — how commands/queries are grouped inside each `<Domain>.Core`.
- [`solution/messaging-consolidation-plan.md`](./solution/messaging-consolidation-plan.md)
  — the full plan that produced the shared outbox; relevant for the
  destructive-migration deployment order.
- [`solution/auth-tenancy-pattern.md`](./solution/auth-tenancy-pattern.md)
  — the OIDC + tenant wiring used by `AddAuthAndTenancy`.
- [`solution/centralized-feature-flags-implementation.md`](./solution/centralized-feature-flags-implementation.md)
  — the `SchoolCollab.Config` service architecture.
- [`solution/feature-flag-workflow.md`](./solution/feature-flag-workflow.md)
  — adding + retiring feature flags.
- `src/SchoolCollab.Config/README.md` — the flag catalogue and the
  `GET /api/features` endpoint.
- `tests/SchoolCollab.ArchitectureTests.Unit/OutboxArchitectureTests.cs`
  — the regression guard that enforces the shared-kernel + outbox
  patterns at build time.