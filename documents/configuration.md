# School-Collab Configuration Reference

This document is the **single source of truth** for every configuration
value that a developer, operator, or CI pipeline can set on the
School-Collab platform. It covers:

- The Aspire AppHost (infrastructure resources + shared secrets)
- Every bounded-context API and worker (per-service options)
- The feature-flag system (centralised in the AppHost)
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
7. [AI provider configuration (`SchoolCollab.AI.Server`)](#7-ai-provider-configuration-schoolcollabaiserver)
8. [Connection strings (Aspire-injected)](#8-connection-strings-aspire-injected)
9. [Logging](#9-logging)
10. [Local-development quickstart](#10-local-development-quickstart)
11. [Environment-variable reference](#11-environment-variable-reference)
12. [Production checklist](#12-production-checklist)
13. [`Assignments` — attachment upload + file store (`assignments-api`)](#13-assignments--attachment-upload--file-store-assignments-api)

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
       postgres    rabbitmq       redis    migrator  (UI / APIs)
            │          │             │         │         │             │             │
            │          │             │         │         │             ▼             ▼
            │          │             │         │         │      settings-api   assignments-api
            │          │             │         │         │      settings-ai    students-api
            │          │             │         │         │      students-worker    admin
            │          │             │         │         │
            ▼          ▼             ▼         ▼
        settings-db         assignments-db  students-db
        (RabbitMQ exchanges per bounded context)
        assignments-db
        students-db
```

**Resource names that appear in the Aspire service-discovery env-var prefix `services__<name>__<scheme>__0`:**

| Aspire name | Type | Used by |
| :--- | :--- | :--- |
| `postgres` | Container (`postgres`) | All bounded-context DBs |
| `rabbitmq` | Container (`rabbitmq`) | All APIs + Students.Worker |
| `cache` | Container (`redis`) | APIs + Worker |
| `migrator` | Project | (one-shot) |
| `settings-db` | Postgres database | migrator, settings-api, assignments-api, students-api |
| `assignments-db` | Postgres database | migrator, assignments-api |
| `students-db` | Postgres database | migrator, students-api, students-worker |
| `settings-api` | Project | admin, settings-ai |
| `settings-ai` | Project | admin |
| `assignments-api` | Project | admin |
| `students-api` | Project | admin |
| `students-worker` | Project | — |
| `portals` | Python app (uv / FastAPI + Prefab UI) in `src/SchoolCollab.Portals/` | — |

> 📌 **Persistent volumes.** `postgres` and `rabbitmq` are wired with
> `WithDataVolume()` + `WithLifetime(ContainerLifetime.Persistent)`. The
> passwords must therefore remain **stable across AppHost sessions**; see
> [§2](#2-aspire-apphost--shared-infrastructure).

---

## 2. Aspire AppHost — shared infrastructure

Defined in `src/AppHost/SchoolCollab.AppHost/Program.cs`.

The AppHost is the **single source of truth** for every value the
distributed application needs at launch: container credentials, outbox
exchange names, AI provider configuration, and any other cross-service
knob. Operators (and other developers on first clone) should be able to
look at exactly **one file** —
`src/AppHost/SchoolCollab.AppHost/appsettings.json` under `Parameters:` —
to find every value they might need to set. Per-service `appsettings.json`
files only carry values that genuinely belong to that single service
(logging, per-feature overrides).

| Key / parameter | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `postgres-password` | Aspire secret parameter (`AddParameter`) | _none — must be supplied_ | Superuser password for the local Postgres container. **Pinned** so the persisted volume keeps working across runs. |
| `rabbitmq-password` | Aspire secret parameter (`AddParameter`) | _none — must be supplied_ | `RABBITMQ_DEFAULT_PASS` for the local RabbitMQ container. Pinned for the same reason as Postgres. |
| `outbox-exchange-settings` | Aspire parameter | `settings` | Outbox topic exchange name for the Settings bounded context. Injected as `Outbox__ExchangeName` into `settings-api`. |
| `outbox-exchange-assignments` | Aspire parameter | `assignments` | Outbox topic exchange name for the Assignments bounded context. Injected as `Outbox__ExchangeName` into `assignments-api` and `assignments-worker` (the worker's `AddAssignmentsCore` registers the shared outbox dispatcher too), and as `RabbitMq__Subscriber__ExchangeName` into `assignments-worker`. |
| `outbox-exchange-students` | Aspire parameter | `students` | Outbox topic exchange name for the Students bounded context. Injected as `Outbox__ExchangeName` into `students-api` and `students-worker`. |
| `ai-default-provider` | Aspire parameter | `openrouter` | Active AI provider name — `ollama` (local) or `openrouter` (cloud). Injected as `codedvalue-ai-provider`. |
| `ollama-endpoint` | Aspire parameter | `http://localhost:11434/v1` | Local Ollama OpenAI-compatible endpoint. Injected as `Ollama__Endpoint`. |
| `ollama-default-model` | Aspire parameter | `gemma4:31b-cloud` | Model name to use when provider is `ollama`. Injected as `Ollama__DefaultModel`. |
| `openrouter-endpoint` | Aspire parameter | `https://openrouter.ai/api/v1` | OpenRouter API base URL. Injected as `OpenRouter__Endpoint`. |
| `openrouter-default-model` | Aspire parameter | `google/gemma-4-31b-it` | Model name to use when provider is `openrouter`. Injected as `OpenRouter__DefaultModel`. |
| `openrouter-api-key` | Aspire secret parameter (`AddParameter`) | _none — must be supplied to enable cloud models_ | OpenRouter API key. Injected as `OpenRouter__ApiKey`. The AI host logs a warning and falls back to a no-op client when the key is missing. |
| `feature-flag-disable-oidc-auth` | Aspire parameter | `false` | Replace Keycloak OIDC with `TestAuthHandler` for local development. Injected as `FeatureFlags__FEATURE__DisableOIDCAuth` into `settings-api`, `assignments-api`, `students-api`, and `admin` (not `families` — the Families host carries its own dev default in its `appsettings.json`; see §5). See §5. |
| `period-activation-tolerance-days` | Aspire parameter | `10` | Default number of days a period may be activated before its `StartDate` or after its `EndDate` (the activation window `[StartDate − tol, EndDate + tol]`). Injected as `Students__PeriodActivationToleranceDays` into `students-api` and `students-worker`; read as `Students:PeriodActivationToleranceDays`. A per-period override (`Period.ActivationToleranceDays`) takes precedence. See `period-activation-window-auto-activation.md` FR-W2. |
| `assignment-file-store-root` | Aspire parameter | `assignment-files` | Local root directory for the assignments file store (relative paths resolve against `AppContext.BaseDirectory`; the directory is created on first write). Injected as `Assignments__FileStore__RootPath` into `assignments-api`; read as `Assignments:FileStore:RootPath`. WS-A1 / D-1: local FS dev implementation; Azure Blob deferred. |
| `assignment-upload-max-file-bytes` | Aspire parameter | `26214400` (25 MiB) | Per-file size cap enforced at the staging endpoint (`POST /assignments/attachments/stage`). Injected as `Assignments__AttachmentUpload__MaxFileSizeBytes`; read as `Assignments:AttachmentUpload:MaxFileSizeBytes`. **Note:** the default stays under Kestrel's default ~30 MB request-body limit; raising this parameter above ~30 MB also requires raising `Microsoft.AspNetCore.Server.Kestrel.Core.Limits.MaxRequestBodySize` on the assignments-api Kestrel options. |
| `assignment-upload-max-total-bytes` | Aspire parameter | `104857600` (100 MiB) | Total attachment size cap enforced on create/update (the sum of every staged file's `FileSize`). Injected as `Assignments__AttachmentUpload__MaxTotalSizeBytes`; read as `Assignments:AttachmentUpload:MaxTotalSizeBytes`. |
| `assignment-upload-allowed-extensions` | Aspire parameter | `.pdf,.doc,.docx,.ppt,.pptx,.xls,.xlsx,.png,.jpg,.jpeg,.gif,.webp,.txt,.md,.csv` | Comma-separated allowlist of file extensions accepted by the staging endpoint (case-insensitive). Injected as `Assignments__AttachmentUpload__AllowedExtensions`; the config binder splits it into the `AllowedExtensions` array. See §13 for the full property table. |
| `feature-flag-require-assignment-approval` | Aspire parameter | `false` | Cold-start value for `FeatureFlags:FEATURE:RequireAssignmentApproval` (WS-A2 / spec §7 Q2). Injected as `FeatureFlags__FEATURE__RequireAssignmentApproval` into `assignments-api` and `admin`. The runtime authority is the Settings Config-service flag (the migration service seeds a default-OFF row, and tenants opt in via `/config-flags`). See §5. |
| `smtp-host` | Aspire parameter | `localhost` | WS-E2 (ar-16) SMTP host for the MailKit email sender. Injected as `Smtp__Host` into `assignments-api`; read as `Smtp:Host`. **`Smtp:Host` blank/unset selects the log-and-skip `NullEmailSender`** (dev/standalone default — it reports success, so an unconfigured host never fills the ar-17 failure list). The AppHost also runs a MailPit dev container (`axllent/mailpit`; SMTP 1025, web inbox `http://localhost:8025`) on the same port. |
| `smtp-port` | Aspire parameter | `1025` | SMTP port. Injected as `Smtp__Port`; read as `Smtp:Port` (MailPit's default 1025). |
| `smtp-user` | Aspire parameter | `dev-user` (dev-only, `appsettings.Development.json`) | Optional SMTP user (blank ⇒ anonymous — MailPit accepts anonymous mail). Injected as `Smtp__User`; read as `Smtp:User`. A **dev-only** default is committed in `appsettings.Development.json` so a plain `aspire run` does not prompt; a real relay's value comes from user-secrets / env-var, which override it (production never loads the Development file). |
| `smtp-password` | Aspire **secret** parameter (`AddParameter(name, secret: true)`) | `dev-only-smtp-password` (dev-only, `appsettings.Development.json`) | Optional SMTP password, paired with `smtp-user`. Injected as `Smtp__Password`; read as `Smtp:Password`. A **dev-only** default is committed in `appsettings.Development.json`; a relay that requires auth supplies the real value via user-secrets / env-var, which override it. **Never commit a production secret.** |
| `smtp-from-address` | Aspire parameter | `no-reply@schoolcollab.local` | From address used when a rendered message carries none. Injected as `Smtp__FromAddress`; read as `Smtp:FromAddress`. |
| `keycloak-client-id` | Aspire parameter | `school-collab-client` | OpenID Connect client ID of the Keycloak dev container's `school-collab-client` client. Injected as `Auth__Keycloak__ClientId` into `assignments-api`, `students-api`, `settings-api`, and `admin`. See §4. |
| `keycloak-admin-password` | Aspire **secret** parameter (`AddParameter(name, secret: true)`) | `dev-only-keycloak-admin` (dev-only, `appsettings.Development.json`) | Bootstrap admin password (`KC_BOOTSTRAP_ADMIN_PASSWORD`) for the `keycloak` dev container. A **dev-only** default is committed in `appsettings.Development.json`, so a plain `aspire run` starts Keycloak without prompting; a non-dev value comes from user-secrets (`Parameters:keycloak-admin-password`) or env-var `Parameters__keycloak_admin_password`, which override it (production never loads the Development file). One of the two parameters AC#4 needs; see §4. |
| `keycloak-client-secret` | Aspire **secret** parameter (`AddParameter(name, secret: true)`) | `dev-only-school-collab-client-secret` (dev-only, `appsettings.Development.json`) | OpenID Connect client secret for `school-collab-client`, injected as `Auth__Keycloak__ClientSecret` into the four hosts above. The committed dev-only default **must equal the realm file's client `secret`** — `AppHostDevParameterDefaultsArchitectureTests` guards the parity, because a mismatch surfaces only when Keycloak rejects client authentication. A user-secrets / env-var value overrides it; production supplies a real secret store value. The other of the two parameters AC#4 needs; see §4. |

**`assignments-worker` (E3 / ar-19) wired in `Program.cs`, no new parameter.**

The AppHost registers `assignments-worker` (`SchoolCollab.Assignments.Worker`) with
`WithReference(assignmentsDb, rabbit, settingsApi, studentsApi)` and
`WaitFor(rabbit) + WaitForCompletion(migrator)`. It does **not** add a `Parameters:`
entry: it reuses the existing `outbox-exchange-assignments` parameter, injected **twice** —
as `Outbox__ExchangeName = assignments`, because the worker's `Program.cs` calls
`AddAssignmentsCore`, which unconditionally registers the shared `OutboxDispatcher` whose
`OutboxOptions` are validated at startup (without this the host dies with
`OptionsValidationException: ExchangeName must be set in the 'Outbox' configuration
section`) — and as `RabbitMq__Subscriber__ExchangeName = assignments` so the worker
subscribes to the same assignments exchange that `assignments-api` publishes
`AssignmentPublishedIntegrationEvent` to. The Published handler kicks one reminder-sweep
pass; the sweeps read policy (`settings-api`) and contacts (`students-api`) through the
same named-HTTP-client service-discovery wires as `assignments-api`.
The worker deliberately does **not** receive the `smtp-*` parameters: it only *queues*
`NotificationLog` rows, and the drain that sends them — the only component that resolves
`IEmailSender` — is the API-side `NotificationDispatchSweepService` in `assignments-api`,
which already carries them. The worker's local `appsettings.json` carries only the RabbitMq
subscriber defaults (see §11).

**Where to set them:**

`src/AppHost/SchoolCollab.AppHost/appsettings.json` is the canonical file
for non-secret defaults — open it, change the value, re-run the AppHost:

```json
{
  "Parameters": {
    "postgres-password": "<set via user-secrets or env-var>",
    "rabbitmq-password": "<set via user-secrets or env-var>",
    "ai-default-provider": "openrouter",
    "openrouter-default-model": "google/gemma-4-31b-it"
  }
}
```

For secrets (`postgres-password`, `rabbitmq-password`,
`openrouter-api-key`) — do **not** commit them to source
control. Use the AppHost's user-secrets store (preferred for local dev):

```bash
cd src/AppHost/SchoolCollab.AppHost
dotnet user-secrets set "Parameters:postgres-password" "postgres"
dotnet user-secrets set "Parameters:rabbitmq-password" "rabbit"
dotnet user-secrets set "Parameters:openrouter-api-key" "<your-key>"
dotnet user-secrets set "Parameters:smtp-password" "<relay-password>"
# ar-20 Keycloak dev container (both secrets, see §4)
dotnet user-secrets set "Parameters:keycloak-admin-password" "<bootstrap-admin-password>"
dotnet user-secrets set "Parameters:keycloak-client-secret" "dev-only-school-collab-client-secret"
```

Or via env-vars (preferred for CI):

```bash
export Parameters__postgres-password=postgres
export Parameters__rabbitmq-password=rabbit
export Parameters__openrouter-api-key=<your-key>
export Parameters__smtp_password=<relay-password>
export Parameters__keycloak_admin_password=<bootstrap-admin-password>
export Parameters__keycloak_client_secret=dev-only-school-collab-client-secret
```

The four **dev-only** exceptions to the rule above are `smtp-user`,
`smtp-password`, `keycloak-admin-password` and `keycloak-client-secret`: each
carries a committed default in `appsettings.Development.json` so a plain
`aspire run` starts with no prompt. That file is never loaded outside
Development, `AppHostDevParameterDefaultsArchitectureTests` asserts none of the
four keys reach the non-Development `appsettings.json`, and any value set below
overrides the committed dev literal.

Aspire's `AddParameter(name, secret: true)` flags secrets so that they are
masked in the Aspire dashboard and treated as sensitive in any deployment
manifest. The non-secret parameters above are visible in plain text. A secret
parameter with no value in **any** config source is prompted for on first
`aspire run` — supplying the four dev-only defaults is what removes that prompt
in Development.

> ⚠️ **Why pinning matters.** Without a stable password, Aspire regenerates
> one on every run; the persisted data volume keeps the *previous*
> `postgres` / `guest` user, which silently breaks every connection with
> `password authentication failed for user "postgres"` / `invalid credentials`.

> 📌 **Why centralise in the AppHost?** A developer cloning the repo, a
> new operator, and a CI pipeline should all be able to look at exactly
> one file to discover every configurable value the application exposes.
> Per-service `appsettings.json` files carry only what *that single
> service* needs. The split reflects that some values are inherently
> cross-cutting (exchange topology, AI provider) and others are local
> (logging, retry policy). See
> [`.github/copilot/rules/ai-services.md`](../.github/copilot/rules/ai-services.md)
> for the AI side.

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

### Per-app wiring

Each bounded context that owns an `OutboxMessage` table relies on
`Outbox.ExchangeName` being supplied to it as an env-var by the AppHost:

| App | Injected env-var | Sourced from |
| :--- | :--- | :--- |
| `settings-api` | `Outbox__ExchangeName` | `Parameters:outbox-exchange-settings` |
| `assignments-api` | `Outbox__ExchangeName` | `Parameters:outbox-exchange-assignments` |
| `students-api` | `Outbox__ExchangeName` | `Parameters:outbox-exchange-students` |
| `students-worker` | `Outbox__ExchangeName` | `Parameters:outbox-exchange-students` |

Per-service `appsettings.json` files intentionally have **no `Outbox`
section** — the value reaches every consumer exclusively through the
AppHost-injected env-var. See `src/AppHost/SchoolCollab.AppHost/Program.cs`
for the wiring.

### Adding a new bounded context

To onboard a new `<Domain>.Core` (e.g. `SchoolCollab.Attendance.Core`):

1. Add an `outbox-exchange-<domain>` entry to
   `src/AppHost/SchoolCollab.AppHost/appsettings.json` under `Parameters:`.
2. Declare the parameter in `AppHost/Program.cs` via
   `builder.AddParameter("outbox-exchange-<domain>")` and wire it onto the
   new API / Worker with
   `.WithEnvironment("Outbox__ExchangeName", param)`.
3. Call `services.AddOutbox<AttendanceDbContext>(builder.Configuration)`
   once during DI setup in the new API / Worker.
4. (Optional) Override `BatchSize` / `PollInterval` in the AppHost
   `Parameters:` section (fan out as `Outbox__BatchSize`,
   `Outbox__PollInterval`) for production tuning.
5. Update §11 ("Environment-variable reference") and §12 ("Production
   checklist") in the same PR. See
   [`.github/copilot/rules/configuration-documentation.md`](../.github/copilot/rules/configuration-documentation.md).
6. The ArchTests in `tests/SchoolCollab.ArchitectureTests.Unit` will
   automatically enforce that no local `Messaging/` folder reappears.

See [`shared-kernel-extraction-pattern.md`](./solution/shared-kernel-extraction-pattern.md)
§3 for the full migration story.

---

## 4. `Auth:Keycloak` — OIDC authentication

Wired by `SchoolCollab.Core.Auth.AuthTenancyExtensions.AddAuthAndTenancy(IConfiguration)`
(used by every API + Admin). The AppHost ships a **Keycloak dev container** (`quay.io/keycloak/keycloak:26.2`)
that imports the committed `src/AppHost/SchoolCollab.AppHost/school-collab-realm.json` realm
(`school-collab`) and fanned the `Auth:Keycloak:*` values onto `assignments-api`, `students-api`,
`settings-api`, and `admin`.

The realm import file is **DEV-ONLY — its credentials are for local/dev use only**:
`dev-teacher` / `dev-only-password`, and the `school-collab-client` secret
`dev-only-school-collab-client-secret`. **Neither value may ever be reused in a real
(production/demo) realm.** The `tenant_id` / `teacher_id` protocol-mapper values pin the
FIXED ids seeded by the MigrationService `DevIdentitySeeder` (Dev School `…0002`, Dev
Teacher `…0003`), so a dev login resolves to real backing rows on first boot.

**Readiness gate (ar-21).** The keycloak container sets `KC_HEALTH_ENABLED=true`, declares a
management endpoint (targetPort 9000) and binds an HTTP readiness health check
(`/health/ready`) to it. All four `Auth:Keycloak:*` consuming hosts — `assignments-api`,
`students-api`, `settings-api`, and `admin` — `.WaitFor(keycloak)`, so none of them fetches
OIDC metadata / validates the issuer while the realm import is still in progress (Keycloak
does not fully start until the import completes).

**Re-import on every recreate (ar-21).** No keycloak data volume is declared, so the realm
file re-imports on every container recreate — edit the realm and recreate the container to
pick it up; no `docker volume rm` is needed.

| Key | Default | Description |
| :--- | :--- | :--- |
| `Auth:Keycloak:Authority` | `https://keycloak.local/realms/school-collab` | OIDC issuer URL (Keycloak realm URL). Under Aspire this is the Keycloak container's HTTP endpoint reference-expression (resolved to its live URL at launch). **IDX10205 caveat:** when you mint a token by hand (below), send the request to the **same host form** as this value — if `Authority` is the dev container's `http://...` URL, hit the `http://` token endpoint (not `https://`), otherwise token validation rejects the token with IDX10205 (mismatched issuer). |
| `Auth:Keycloak:ClientId` | `school-collab-client` | OpenID Connect client ID (`keycloak-client-id` AppHost parameter). |
| `Auth:Keycloak:ClientSecret` | `dev-only-school-collab-client-secret` (dev-only) | OpenID Connect client secret. The code fallback is the literal `"secret"` (a pre-ar-20 placeholder still present in `AuthTenancyExtensions`); **dev** uses the committed dev-only default for `Parameters:keycloak-client-secret` in `appsettings.Development.json`, which **must equal** the realm file's client `secret` (guarded); **production** MUST substitute a real secret-store value — no production secret is committed. |

> 🔐 **Secrets.** `ClientSecret` and the Keycloak bootstrap admin password are the most
> sensitive values here. Use one of:
> - nothing for local dev — `appsettings.Development.json` commits the pinned dev-only
>   defaults. Override them with
>   `dotnet user-secrets set "Parameters:keycloak-admin-password" "..."` and
>   `dotnet user-secrets set "Parameters:keycloak-client-secret" "dev-only-school-collab-client-secret"`
> - Aspire `AddParameter(..., secret: true)` + reference (CI) — the dev default is the only
>   committed value; production supplies a secret-store value (see §2)
> - Azure Key Vault / your platform's secret manager (production)

### Dev-only `RequireHttpsMetadata = false`

The Keycloak dev container exposes an **HTTP** authority (`http://<host>:<port>`, realm
`school-collab`), so the OIDC + JwtBearer handlers are wired with
`RequireHttpsMetadata = false`. **Today that value is set unconditionally** — it is *not*
environment-gated (see the commented lines in `AuthTenancyExtensions`), so a Production
deployment must point `Auth:Keycloak:Authority` at an **https** URL. Relaxing HTTPS metadata
validation outside the dev container is unsafe; gating this value on the host environment is
a recorded follow-up.

### Minting a bearer token with username/password (dev, ROPC)

The dev client `school-collab-client` has **direct access grants enabled**, so you can mint a
token with the seeded `dev-teacher` user via the password grant against Keycloak's token endpoint:

```bash
REALM_URL="http://localhost:<keycloak-http-port>/realms/school-collab"
curl -s -X POST "$REALM_URL/protocol/openid-connect/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password" \
  -d "client_id=school-collab-client" \
  -d "client_secret=dev-only-school-collab-client-secret" \
  --data-urlencode "username=dev-teacher" \
  --data-urlencode "password=dev-only-password"
```

The response `access_token` carries `tenant_id=…0002`, `tenant_name="Dev School"`, `tenant_type="School"`,
`teacher_id=…0003` (the fixed ids seeded by the MigrationService `DevIdentitySeeder`), plus `aud=school-collab-client`
(the oidc-audience-mapper). Use it as `Authorization: Bearer <access_token>` against the bearer endpoint groups
(e.g. `GET /assignments`). Mint the token from the **same host form as `Auth:Keycloak:Authority`** (IDX10205 caveat above).

> ⚠️ **ROPC is deprecated (OAuth 2.1).** The password grant above is for local dev / simple clients
> only. **Production stays on the authorization-code flow with PKCE** (the `AddOpenIdConnect` code
> flow + `ResponseType = "code"` — the browser/cookie path) — never use the password grant in a
> real deployment.

### The two parameters AC#4 needs

Acceptance criterion #4 is exercised **out of the box** in Development: both parameters
carry dev-only defaults in `appsettings.Development.json`, so a plain `aspire run` starts
Keycloak with no prompt. Production supplies real values — there is no committed
production secret.

| Parameter | Dev default | Purpose |
| --- | --- | --- |
| `Parameters__keycloak_admin_password` (user-secrets `Parameters:keycloak-admin-password`) | `dev-only-keycloak-admin` | Keycloak bootstrap admin password (`KC_BOOTSTRAP_ADMIN_PASSWORD`). |
| `Parameters__keycloak_client_secret` (user-secrets `Parameters:keycloak-client-secret`) | `dev-only-school-collab-client-secret` | Dev client secret — must equal the realm file's `school-collab-client` `secret` (guarded). |

### Real vs TestAuth modes

When `FEATURE:DisableOIDCAuth` is enabled (see [§5](#5-featureflags--central-configuration-service)),
the OIDC + JwtBearer registration is replaced with `TestAuthHandler`, which auto-authenticates
every request as a test user without Keycloak — intended for local development / CI only.

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

### ar-24 — bearer forwarding + receiving-side Bearer opt-in

Post-flip (`FeatureFlags:FEATURE:DisableOIDCAuth=false`), an API host making a cross-context hop
forwards the inbound request's `Authorization` header through `BearerForwardingDelegatingHandler`
(real-auth only; a no-op in TestAuth/dev). The receiving groups must therefore accept a bearer:

- **Assignments**: the `/assignments` and `/students` groups opt into Bearer (`AssignmentEndpoints.cs`).
- **Students**: all five groups opt in (`StudentEndpoints.cs`) — incl. **`/teachers`** (the directory check calls it).
- **Settings**: 10 sites — the 8 `*Endpoints.cs` groups, `Endpoints/ConfigResolveRoutes.cs` (tenant reads),
  and the **`flag_admin` policy** (`Program.cs`) which now adds `AddAuthenticationSchemes(Bearer)` (role claim kept).

**Named residuals (post-flip, not fixed this round):** `Assignments.Worker`→settings/students 401s (no caller
token; client-credentials barred by the no-new-secrets gate) **and the identically-unattached
`Students.Worker` background client (`Program.cs:53`, used by `CodedValueBackfillService`)**; the Blazor
interactive clients (Families/Admin + the Application-layer typed clients) carry the OIDC **cookie**, not a
bearer — no circuit-safe token source yet (their review/approve calls still work via the server-side
claim-wins handlers); and **`RejectAssignment` has claim-wins but no tenant cross-check** (the fifth write
path — defense-in-depth only today: the EF global tenant filter makes it non-exploitable).
`AllowAutoRedirect=false` on both
Assignments.Api named clients so a real-auth 302-challenge surfaces as a non-2xx (fail-closed directory check).
---

## 5. `FeatureFlags` — central configuration service

> **Updated.** There are now two kinds of feature flag (see
> [`solution/feature-flag-workflow.md`](./solution/feature-flag-workflow.md) and
> [`solution/central-config-service-plan.md`](./solution/central-config-service-plan.md)):
>
> - **Runtime, mutable, tenant-overridable flags** (e.g.
>   `FEATURE:EnableCodedValuesAiChat`) are owned by the central Settings
>   bounded context (`settings-api` + `settings-db`, see
>   [`solution/settings-context-merge-spec.md`](./solution/settings-context-merge-spec.md)
>   for the merge history), managed via the admin UI at `/config-flags`,
>   resolved by `ConfigFeatureFlagService` with a HybridCache L1/L2 +
>   `IConfiguration` fallback. Every mutation is audited.
> - **`FEATURE:DisableOIDCAuth`** remains a *deployment-time startup auth-mode
>   switch*: it is **no longer** an AppHost `Parameters:` value. Each consumer
>   carries it in its own `appsettings.json` (`FeatureFlags:FEATURE:DisableOIDCAuth`,
>   dev default `"true"`) and production overrides it via the env var
>   `FeatureFlags__FEATURE__DisableOIDCAuth=false`. It is read from
>   `IConfiguration` directly at startup (auth schemes are registered once and
>   cannot be flipped at runtime), so it is **not** a Config-service flag.

The historical AppHost-`Parameters:` description below is retained for context
but is **superseded** by the two-kind model above.

### Runtime flags (current)

| Flag | Default | Notes |
| :--- | :--- | :--- |
| `FEATURE:EnableCodedValuesAiChat` | `true` | Gates the AI-chat surfaces on the CodedValues landing page. Seeded by the migration service; tenant-overridable. Cold-start fallback in `SchoolCollab.Admin/appsettings.json`. |
| `FEATURE:EnableActivityGroups` | `false` | Gates the activity-group management surface: Admin **Activity Groups** nav/page, group CRUD + membership endpoints in `SchoolCollab.Students.Api`, and the assignment↔group link endpoints + `SelectedGroups` targeting in `SchoolCollab.Assignments.Api`. Ships **dark** (default OFF) per [`activity-group-enrollment.md`](./specs/activity-group-enrollment.md) NFR-11. Seeded by the migration service; tenant-overridable. Cold-start fallback in `SchoolCollab.Admin/appsettings.json`. The global default remains OFF; the migration service additionally seeds a `TenantFeatureFlagOverride` turning the flag ON for the pilot tenant `Hydeson School` only (Phase 6.1 — see below). |
| `FEATURE:RequireAssignmentApproval` | `false` | Gates the assignment approval workflow (WS-A2 / spec §7 Q2): when on, every assignment requires approval before publish — the publish + schedule command handlers throw `AssignmentApprovalRequiredException` (HTTP 400), and the Admin Assignments Index/Detail UI surfaces Submit-for-approval / Approve / Reject controls. Ships **dark** (default OFF). Seeded by the migration service; tenant-overridable via `/config-flags`. Cold-start fallback in `SchoolCollab.Admin/appsettings.json` + AppHost parameter fan-out. |
| `FEATURE:EnableDeepLinks` | `false` | Gates the Families public deep-link landing route group (`GET /deeplink/{token}`) for assignment recipients (WS-E1 / `ar-14-deep-links`) **and the guardian sign-off surface (WS-F3 / `ar-15-signoff-relocation`)**: the Assignments API's public `/guardian/...` route group (token-gated by `GuardianTokenEndpointFilter`, which 401s when the flag is OFF) and the Families-hosted `Ward/SignOff.razor` guardian page. Ships **dark** (default OFF): tokens are minted at publish regardless of this flag, and the runtime flag only decides whether the public route validates, stamps `OpenedAt`, signs the guardian into a cookie, redirects to the ward surface, and accepts the guardian route group's `x-deeplink-token` calls. Seeded by the migration service; tenant-overridable via `/config-flags`. Cold-start fallback in `SchoolCollab.Families/appsettings.json`. |

### Pilot-tenant override (Phase 6.1)

`FEATURE:EnableActivityGroups` is enabled for exactly one pilot tenant at seed
time via a `TenantFeatureFlagOverride` row, without changing the global default.

- **Mechanism:** a `TenantFeatureFlagOverride` row (`IsEnabled = true`,
  `EffectiveFrom`/`EffectiveTo` null → always in effect) is seeded idempotently
  by `SchoolCollab.MigrationService` after the tenant + flag seeds.
- **Pilot tenant:** `Hydeson School` (configurable via
  `PilotActivityGroupFlagOverrideSeeder.PilotTenantName`).
- **Traceability:** a `FlagAuditEntry` with `ChangeKind = OverrideCreated`, actor
  `system:migrator` / "Migration Service", is written with the override.
- **Effect:** `ConfigFeatureFlagService` (`ResolveFlagsForTenantHandler`) resolves
  the flag to ON for the pilot tenant and to the global OFF default for every
  other tenant. To turn the pilot off, delete the override row via the admin
  `/config-flags` tenant-override surface (or run the `DeleteTenantFlagOverride`
  command).

### Historical AppHost-`Parameters:` model (superseded)

Feature flags were previously **centralised in the AppHost** under
`Parameters:feature-flag-*` and fanned out to each consumer via
`WithEnvironment("FeatureFlags__FEATURE__...", param)`. That
`feature-flag-disable-oidc-auth` parameter has been removed; `DisableOIDCAuth`
now lives in each consumer's `appsettings.json` as described above. Runtime
flags moved to the Config service.

### Introduced flags

| Flag | Default | Consumers |
| :--- | :--- | :--- |
| `FEATURE:DisableOIDCAuth` | `false` | `SchoolCollab.Admin`, `SchoolCollab.Families`, `SchoolCollab.Assignments.Api`, `SchoolCollab.Settings.Api`, `SchoolCollab.Students.Api` |
| `FEATURE:EnableActivityGroups` | `false` | `SchoolCollab.Admin`, `SchoolCollab.Assignments.Api`, `SchoolCollab.Students.Api` |
| `FEATURE:RequireAssignmentApproval` | `false` | `SchoolCollab.Admin` (Assignments Index/Detail UI), `SchoolCollab.Assignments.Api` (publish + schedule handlers) |
| `FEATURE:EnableDeepLinks` | `false` | `SchoolCollab.Families` (public `/deeplink/{token}` landing + `Ward/SignOff.razor` guardian page), `SchoolCollab.Assignments.Api` (mint at publish + public `/guardian/...` sign-off route group), `SchoolCollab.MigrationService` (seed) |

### Setting a flag

In a developer / CI environment, override the AppHost `Parameters:`
value via user-secrets (preferred) or env-var:

```bash
cd src/AppHost/SchoolCollab.AppHost
# dev-only override
dotnet user-secrets set "Parameters:feature-flag-disable-oidc-auth" "true"
```

Or directly edit the default in
`src/AppHost/SchoolCollab.AppHost/appsettings.json` under `Parameters:`.
That file is the canonical record of every flag's default value and
**must** be updated in the same PR that adds a new flag — see
[`.github/copilot/rules/configuration-documentation.md`](../.github/copilot/rules/configuration-documentation.md).

> 📝 The `:` in the flag key is intentional. `FeatureFlagService.CollectFlags`
> recurses into nested sections, so `FEATURE:DisableOIDCAuth` surfaces as
> the dotted key `FEATURE:DisableOIDCAuth` — keep the colon when adding new
> flags.

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

if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
{
    group.RequireAuthorization();
}
```

### Adding a new flag

1. Add a constant to `src/SchoolCollab.Core/Features/FeatureFlagKeys.cs`
   using the form `FEATURE:<FlagName>`.
2. Add a `Parameters:feature-flag-<name>` entry to
   `src/AppHost/SchoolCollab.AppHost/appsettings.json` (with the default
   value).
3. In `src/AppHost/SchoolCollab.AppHost/Program.cs`, declare the
   parameter with `builder.AddParameter("feature-flag-<name>")` and
   wire it onto every consumer with
   `.WithEnvironment("FeatureFlags__FEATURE__<FlagName>", param)`.
4. Reference the new constant (`FeatureFlagKeys.<FlagName>`) in consumers,
   endpoint authorization gating, `FeatureFlagGate` markup, and migration
   seeds instead of duplicating the raw string.
5. Add the flag to the **Introduced flags** table in this section.
6. Update §2 (parameters table), §11 (env-var reference), and §12
   (production checklist) in the same PR.

### Why no `SchoolCollab.Config` service?

Earlier revisions routed feature flags through a separate
`SchoolCollab.Config` service via `AddRemoteFeatureFlags`, which fetched
`GET /api/features` at startup and overlaid the JSON into
`IConfiguration`. That design was retired (see
[`solution/centralized-feature-flags-implementation.superseded.md`](./solution/centralized-feature-flags-implementation.superseded.md))
because:

- The Config service was a thin proxy over its own local JSON file — the
  HTTP indirection did not provide any of the properties that justify a
  real central config service (caching, change notifications, per-tenant
  overrides, audit trail).
- The synchronous HTTP call added a 10-second worst-case startup delay to
  every API and Admin on every cold start.
- Using the same `Parameters:` pattern as outbox exchanges and AI config
  gives one mental model for "how a cross-service value gets
  distributed".

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

## 7. AI provider configuration (`SchoolCollab.AI.Server`)

> **Centralisation note.** All AI provider configuration is owned by the
> AppHost — see [§2](#2-aspire-apphost--shared-infrastructure) for the
> canonical table of `Parameters:ai-*` entries. This section now only
> documents the **runtime contract** (which keys the AI host reads, and
> how `ChatModelResolver` resolves them to a working `(provider, model)`
> pair) — the *values* are no longer configured in
> `src/SchoolCollab.AI.Server/appsettings.json`.

Two providers are wired in `src/SchoolCollab.AI.Server/Program.cs` and selected
by the `codedvalue-ai-provider` configuration key at startup (its value
is sourced from the AppHost's `Parameters:ai-default-provider` parameter).
The remaining keys come from the AppHost's `Ollama:*` /
`OpenRouter:*` parameter block.

**Runtime keys read by the AI host** (all injected as env vars by the
AppHost — see §2 for the parameter names):

| Key | Default | Description |
| :--- | :--- | :--- |
| `codedvalue-ai-provider` | `ollama` | Active provider name — `ollama` (local) or `openrouter` (cloud). Normalised by `ChatModelResolver.NormalizeProvider` (case-insensitive; unknown values fall back to `ollama`). |
| `Ollama:Endpoint` | `http://localhost:11434/v1` | Local Ollama OpenAI-compatible endpoint. |
| `Ollama:DefaultModel` | `gemma4:31b-cloud` | Model name to use when provider is `ollama`. Falls back to `ChatModelResolver.DefaultOllamaModel` when absent. |
| `OpenRouter:Endpoint` | `https://openrouter.ai/api/v1` | OpenRouter API base URL. |
| `OpenRouter:DefaultModel` | `google/gemma-4-31b-it` | Model name to use when provider is `openrouter`. Falls back to `ChatModelResolver.DefaultOpenRouterModel` when absent. Default is the paid, stable Google Gemma 4 31B IT model — deliberately **not** the `:free` tag, which is heavily rate-limited and intermittently returns empty responses or non-rate-limit-formatted errors that the live integration tests can't classify. |
| `OpenRouter:ApiKey` | _none — optional, supplied via `Parameters:openrouter-api-key`_ | OpenRouter API key. When absent, the provider logs a warning and the `openrouter` client is still registered but every request will fail at the provider. |

**Switching provider / model at runtime** — change the relevant
`Parameters:*` entry in
`src/AppHost/SchoolCollab.AppHost/appsettings.json` (or override via env
var / user-secrets / Azure App Configuration in deployment), then re-run
the AppHost. Do **not** edit
`src/SchoolCollab.AI.Server/appsettings.json` — it no longer carries these
keys.

**Production example — OpenRouter with Claude:**

1. `src/AppHost/SchoolCollab.AppHost/appsettings.json` (or the
   deployment-equivalent):

   ```json
   "Parameters": {
     "ai-default-provider": "openrouter",
     "openrouter-default-model": "anthropic/claude-3.5-sonnet"
   }
   ```

2. AppHost user-secrets (or env-var in CI):

   ```bash
   dotnet user-secrets --project src/AppHost/SchoolCollab.AppHost set \
     "Parameters:openrouter-api-key" "<key>"
   ```

> 🔐 **Never commit `Parameters:openrouter-api-key`** — use the AppHost's
> user-secrets store (whose `UserSecretsId` is also embedded in the
> integration test csproj as `<AppHostUserSecretsId>` so live tests pick
> up the same key) or your platform's secret store in CI/production.

---

## 8. Connection strings (Aspire-injected)

Aspire injects connection strings into the apps that call
`.WithReference(<resource>)`. They land under the standard
`ConnectionStrings:<resource-name>` keys:

| Resource | Injected key | Used by |
| :--- | :--- | :--- |
| `settings-db` | `ConnectionStrings:settings-db` | `SchoolCollab.Settings.Api`, `SchoolCollab.Assignments.Api`, `SchoolCollab.Students.Api`, migrator |
| `assignments-db` | `ConnectionStrings:assignments-db` | `SchoolCollab.Assignments.Api`, migrator |
| `students-db` | `ConnectionStrings:students-db` | `SchoolCollab.Students.Api`, `SchoolCollab.Students.Worker`, migrator |
| `cache` | `ConnectionStrings:cache` | APIs + Worker (also surfaced as `Aspire:StackExchange:Redis:ConnectionString`) |
| `rabbitmq` | `ConnectionStrings:rabbitmq` | APIs + Worker (via `AddRabbitMQClient("rabbitmq")`) |

> 📝 You should **not** set these by hand in `appsettings.json` when
> running under Aspire — they will be overwritten by service-discovery.
> They are documented here only so you know what to expect when running
> a single API outside the AppHost (e.g. from VS / VS Code).

> ⚠️ **Every host that reads a connection string needs a matching `.WithReference`.**
> `Settings.Core/Extensions.cs` (`AddSettingsCore` / `AddTenantDirectory`) and its
> Students/Assignments peers fall back to a hardcoded
> `Host=localhost;Port=5432` when the key is absent — the host still reports
> **Healthy** (Aspire probes the endpoint), but the Settings outbox dispatcher
> registered by `AddSettingsCore` then logs a connection error on every retry and
> entity-code generation fails. A host is affected whenever its `Program.cs` calls
> `AddSettingsCore(...)` (needed for `IEntityCodeGenerator` over `settings-db`).
> `AppHostSettingsDbWiringArchitectureTests` in `SchoolCollab.ArchitectureTests.Unit`
> enforces the reference for every such host.

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
(`app.UseSerilogRequestLogging()` in `src/SchoolCollab.AI.Server/Program.cs`).

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

Edit `Parameters:feature-flag-disable-oidc-auth` in
`src/AppHost/SchoolCollab.AppHost/appsettings.json` from `"false"` to
`"true"` (or override via the AppHost's user-secrets — see §5). With
this on, Keycloak is not required for local dev.

### 4. (Optional) Configure an AI provider

Non-secret AI defaults already live in
`src/AppHost/SchoolCollab.AppHost/appsettings.json` under `Parameters:*`
(see §2). To enable OpenRouter or switch the provider:

```bash
cd src/AppHost/SchoolCollab.AppHost
# pick a cloud model
dotnet user-secrets set "Parameters:openrouter-api-key" "<your-key>"
# (optional) flip the active provider / model — only needed if you want a different combination
# dotnet user-secrets set "Parameters:ai-default-provider" "openrouter"
# dotnet user-secrets set "Parameters:openrouter-default-model" "anthropic/claude-3.5-sonnet"
```

Or keep the default `ai-default-provider=openrouter` and run Ollama
locally instead — flip `Parameters:ai-default-provider` to `ollama` and
edit `Parameters:ollama-endpoint` and `Parameters:ollama-default-model`
if your local endpoint differs from `http://localhost:11434/v1`.

### 5. Run

```bash
cd src/AppHost/SchoolCollab.AppHost
dotnet run
```

Aspire launches the dashboard and starts every project in dependency order.

### 6. Verify

```bash
# Feature flags (env-var-injected; same value the host already saw at startup)
echo $FeatureFlags__FEATURE__DisableOIDCAuth

# AI provider
curl http://localhost:<ai-port>/api/ai/config
```

---

## 11. Environment-variable reference

ASP.NET Core maps `Section:Key` → `Section__Key` (double underscore).
Aspire additionally replaces `-` with `_` in `Parameters` config keys
when forming their env-var name, so the canonical form is
`Parameters__{name-with-underscores}`. Every key in this document has a
matching env-var form:

| Config key | Env-var |
| :--- | :--- |
| `Parameters:postgres-password` | `Parameters__postgres_password` |
| `Parameters:rabbitmq-password` | `Parameters__rabbitmq_password` |
| `Parameters:outbox-exchange-settings` | `Parameters__outbox_exchange_coded_values` |
| `Parameters:outbox-exchange-assignments` | `Parameters__outbox_exchange_assignments` |
| `Parameters:outbox-exchange-students` | `Parameters__outbox_exchange_students` |
| `Parameters:ai-default-provider` | `Parameters__ai_default_provider` |
| `Parameters:ollama-endpoint` | `Parameters__ollama_endpoint` |
| `Parameters:ollama-default-model` | `Parameters__ollama_default_model` |
| `Parameters:openrouter-endpoint` | `Parameters__openrouter_endpoint` |
| `Parameters:openrouter-default-model` | `Parameters__openrouter_default_model` |
| `Parameters:openrouter-api-key` | `Parameters__openrouter_api_key` |
| `Parameters:feature-flag-disable-oidc-auth` | `Parameters__feature_flag_disable_oidc_auth` |
| `Parameters:feature-flag-require-assignment-approval` | `Parameters__feature_flag_require_assignment_approval` |
| `Parameters:period-activation-tolerance-days` | `Parameters__period_activation_tolerance_days` |
| `Parameters:assignment-file-store-root` | `Parameters__assignment_file_store_root` |
| `Parameters:assignment-upload-max-file-bytes` | `Parameters__assignment_upload_max_file_bytes` |
| `Parameters:assignment-upload-max-total-bytes` | `Parameters__assignment_upload_max_total_bytes` |
| `Parameters:assignment-upload-allowed-extensions` | `Parameters__assignment_upload_allowed_extensions` |
| `Parameters:smtp-host` | `Parameters__smtp_host` |
| `Parameters:smtp-port` | `Parameters__smtp_port` |
| `Parameters:smtp-user` | `Parameters__smtp_user` |
| `Parameters:smtp-password` | `Parameters__smtp_password` |
| `Parameters:smtp-from-address` | `Parameters__smtp_from_address` |
| `Parameters:keycloak-client-id` | `Parameters__keycloak_client_id` |
| `Parameters:keycloak-admin-password` | `Parameters__keycloak_admin_password` |
| `Parameters:keycloak-client-secret` | `Parameters__keycloak_client_secret` |
| `Students:PeriodActivationToleranceDays` | `Students__PeriodActivationToleranceDays` |
| `Assignments:FileStore:RootPath` | `Assignments__FileStore__RootPath` |
| `Assignments:AttachmentUpload:MaxFileSizeBytes` | `Assignments__AttachmentUpload__MaxFileSizeBytes` |
| `Assignments:AttachmentUpload:MaxTotalSizeBytes` | `Assignments__AttachmentUpload__MaxTotalSizeBytes` |
| `Assignments:AttachmentUpload:AllowedExtensions` | `Assignments__AttachmentUpload__AllowedExtensions` |
| `Smtp:Host` | `Smtp__Host` |
| `Smtp:Port` | `Smtp__Port` |
| `Smtp:User` | `Smtp__User` |
| `Smtp:Password` | `Smtp__Password` |
| `Smtp:FromAddress` | `Smtp__FromAddress` |
| `Smtp:UseStartTls` | `Smtp__UseStartTls` |

> **TLS limitation (ar-16):** only STARTTLS (`UseStartTls = true`) and plaintext
> (`false`) are supported. **Implicit TLS on connect — i.e. the port-465
> "SMTPS" convention — is NOT available to the MailKit sender in this round.**
> A relay that only accepts port-465 implicit TLS (or a `SslOnConnect`
> requirement) needs a follow-up change to add the `SslOnConnect` option to
> `SmtpOptions` + `MailKitEmailSender`. Use port 587 (STARTTLS) or 1025 (MailPit
> dev) as-is.
| `Assignments:AttachmentUpload:StagingRetentionHours` | `Assignments__AttachmentUpload__StagingRetentionHours` |
| `Assignments:AttachmentUpload:SweepIntervalHours` | `Assignments__AttachmentUpload__SweepIntervalHours` |
| `Outbox:ExchangeName` | `Outbox__ExchangeName` |
| `RabbitMq:Subscriber:ExchangeName` | `RabbitMq__Subscriber__ExchangeName` |
| `RabbitMq:Subscriber:QueueName` | `RabbitMq__Subscriber__QueueName` |
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
| `ConnectionStrings:settings-db` | `ConnectionStrings__settings-db` |
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
      `Parameters:postgres-password`, `Parameters:rabbitmq-password`,
      `Parameters:openrouter-api-key`.
- [ ] **`Auth:Keycloak:ClientSecret`** is sourced from a secret store. The
      `appsettings.Development.json` dev defaults (this one, `keycloak-admin-password`,
      `smtp-user`, `smtp-password`) are never loaded in production and must not be relied
      on there.
- [ ] **`Parameters:feature-flag-disable-oidc-auth`** is `false`
      (or omitted) in production. The flag is sourced from the AppHost
      `Parameters:` block and fanned out as `FeatureFlags__FEATURE__DisableOIDCAuth`.
- [ ] **OIDC `Authority`** points to the production Keycloak realm.
- [ ] **Outbox `ExchangeName`** values match the consumer subscriptions
      for each bounded context (`Parameters:outbox-exchange-*`).
- [ ] **AI provider keys** are sourced from a secret store
      (`Parameters:openrouter-api-key`), and the active provider
      (`Parameters:ai-default-provider`) and model
      (`Parameters:*-default-model`) match the intended deployment.
- [ ] **Outbox migration safety** — when bumping `OutboxMessage` schema
      (e.g. the `ProcessedAt` → `DispatchedAt` rename in Phase 3), drain
      each service's outbox before applying the EF migration. See
      [`messaging-consolidation-plan.md`](./solution/messaging-consolidation-plan.md)
      §3.3 for the full deployment-order recipe.
- [ ] **Assignment upload caps + allowlist** are sourced from the AppHost
      `Parameters:assignment-upload-*` block. When raising the per-file cap
      above Kestrel's default ~30 MB request-body limit, also raise
      `Microsoft.AspNetCore.Server.Kestrel.Core.Limits.MaxRequestBodySize`
      on the `assignments-api` Kestrel options — otherwise the cap is
      unreachable because Kestrel rejects the multipart body first. The
      orphan sweep runs every `SweepIntervalHours` (default 24 h) and
      deletes staged files older than `StagingRetentionHours` (default
      48 h) that are NOT referenced by any assignment attachment,
      resource, or content-module row (decision (b)).
- [ ] **Logging levels** are `Information` (or stricter) by default in
      production; override per-category for noisy modules.

---

## 13. `Assignments` — attachment upload + file store (`assignments-api`)

The first upload surface in the platform (WS-A1 / FR-210–212 / EC-4).
The wizard stages each file at selection time (decision (b) —
stage-at-selection honestly reconciled against the spec's preferred
stage-at-submit, which is impossible for a single-create-call wizard)
via `POST /assignments/attachments/stage`, then rides the returned
`StoragePath` on the create payload.

### `Assignments:FileStore`

**Type:** [`SchoolCollab.Assignments.Core.Services.AssignmentFileStoreOptions`](../../src/Assignments/SchoolCollab.Assignments.Core/Services/AssignmentFileStoreOptions.cs)
**Section name:** `Assignments:FileStore`
**Bound in:** `AddAssignmentsCore` (reaches the Api via its existing
`AddAssignmentsCore` call — no Program.cs registration needed)

| Property | Default | Description |
| :--- | :--- | :--- |
| `RootPath` | `assignment-files` | Local root directory for staged files. Relative paths resolve against `AppContext.BaseDirectory` at construction time (the directory is created on first write). Sourced from the AppHost parameter `assignment-file-store-root`. |

### `Assignments:AttachmentUpload`

**Type:** [`SchoolCollab.Assignments.Core.Services.AttachmentUploadOptions`](../../src/Assignments/SchoolCollab.Assignments.Core/Services/AttachmentUploadOptions.cs)
**Section name:** `Assignments:AttachmentUpload`
**Bound in:** `AddAssignmentsCore`

| Property | Default | Description |
| :--- | :--- | :--- |
| `MaxFileSizeBytes` | `26214400` (25 MiB) | Per-file size cap enforced at stage by `StagedFileValidator`. Sourced from `assignment-upload-max-file-bytes`. **Note:** stays under Kestrel's default ~30 MB request-body limit; raising it above ~30 MB also requires raising `Microsoft.AspNetCore.Server.Kestrel.Core.Limits.MaxRequestBodySize`. |
| `MaxTotalSizeBytes` | `104857600` (100 MiB) | Total attachment size cap enforced on create/update (defense in depth on top of the per-file cap). Sourced from `assignment-upload-max-total-bytes`. |
| `AllowedExtensions` | `.pdf,.doc,.docx,.ppt,.pptx,.xls,.xlsx,.png,.jpg,.jpeg,.gif,.webp,.txt,.md,.csv` | Allowlist of file extensions (case-insensitive). Sourced from `assignment-upload-allowed-extensions` (comma-separated; the config binder splits it into the array). |
| `StagingRetentionHours` | `48` | How long staged files live before the sweep is allowed to delete them (hours). Drives `StagedFileSweepService`. Not currently fanned out from the AppHost — override via env-var `Assignments__AttachmentUpload__StagingRetentionHours` if needed. |
| `SweepIntervalHours` | `24` | How often the orphan sweep runs (hours). Drives `StagedFileSweepService`. Not currently fanned out from the AppHost — override via env-var `Assignments__AttachmentUpload__SweepIntervalHours` if needed. |

### Orphan sweep (decision (b))

`StagedFileSweepService` (a hosted BackgroundService in `assignments-api`)
runs every `SweepIntervalHours` and deletes staged files older than
`StagingRetentionHours` that are NOT referenced by any row in
`assignment_attachments.storage_path`, `assignment_resources.storage_path`,
or `assignment_content_modules.storage_path`. The reference check is the
correctness core — a missing check would delete the very blobs the
created assignment points at. The sweep uses
`IgnoreQueryFilters(["Tenant"])` (the sanctioned opt-out the tenancy
tests exercise) for a read-only cross-tenant projection and performs NO
tenant-entity writes.

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
- [`solution/centralized-feature-flags-implementation.superseded.md`](./solution/centralized-feature-flags-implementation.superseded.md)
  — the retired `SchoolCollab.Config` HTTP overlay design, retained for
  historical context. Feature flags now use the AppHost `Parameters:`
  pattern described in §5.
- [`solution/feature-flag-workflow.md`](./solution/feature-flag-workflow.md)
  — adding + retiring feature flags.
- `tests/SchoolCollab.ArchitectureTests.Unit/OutboxArchitectureTests.cs`
  — the regression guard that enforces the shared-kernel + outbox
  patterns at build time.