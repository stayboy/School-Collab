# Aspire 13.5.4 upgrade (CLI + hosting packages)

> Status: **implemented** — branch `chore/aspire-13-5-update` (2026-09-21). Build 0 errors,
> unit matrix **2,420/0**. Runtime (AppHost cold start) **not verified** — see Residuals.
> Related: `documents/specs/teachers-ward-portal-prefab-plan.md` §3,
> `documents/solution/portals-service-client-pattern.md`,
> `documents/runbooks/aspire-apphost-silent-hang.md`.

## Why

The Aspire CLI had been updated to `13.5.4` while every `Aspire.*` package and the AppHost SDK
stayed pinned at `13.4.5` in `Directory.Packages.props` (CPM) — a CLI newer than the packages
it drives. `aspire update` is the supported way to close that gap, so the bump was run rather
than hand-edited.

## What `aspire update` did

```
aspire update --apphost src/AppHost/SchoolCollab.AppHost --channel stable --non-interactive --yes
```

1. Bumped six Aspire packages `13.4.5 → 13.5.4`: `Aspire.Hosting.{PostgreSQL,Python,RabbitMQ,Redis}`,
   `Aspire.StackExchange.Redis.DistributedCaching`, `Aspire.RabbitMQ.Client`.
2. **Migrated the AppHost project format**: `<Project Sdk="Microsoft.NET.Sdk">` +
   `<Sdk Name="Aspire.AppHost.Sdk" Version="13.4.5" />` became
   `<Project Sdk="Aspire.AppHost.Sdk/13.5.4">`.
3. **Removed the `Aspire.Hosting.AppHost` `PackageReference`** from the AppHost csproj and its
   `PackageVersion` entry from `Directory.Packages.props` — the SDK now supplies that package.

## The follow-on step it does not do: the `Microsoft.Extensions` floor

The first build after the update failed with **NU1605 (downgrade-as-error)** in
`tests/SchoolCollab.Settings.Tests.Integration`. The 13.5.4 graph resolves
`Microsoft.Extensions.Hosting.Abstractions` **10.0.11**, which requires
`Microsoft.Extensions.Logging.Abstractions >= 10.0.11`, while CPM pinned the family at
**10.0.9** (`NU1605: Detected package downgrade: … from 10.0.11 to 10.0.9`).

Fixing only the named package surfaced the next one (`DependencyInjection.Abstractions`), so
the whole **`Microsoft.Extensions.*` 10.0.x family was moved coherently to 10.0.11** — 10
entries: Caching.Abstractions, Configuration, Configuration.Abstractions, Configuration.Binder,
DependencyInjection.Abstractions, Hosting, Hosting.Abstractions, Logging, Logging.Abstractions,
Options, Options.ConfigurationExtensions. The Aspire-era **10.6.0** pins
(`Microsoft.Extensions.{Http.Resilience,ServiceDiscovery,AI,AI.OpenAI}`) and `Caching.Hybrid`
10.1.0 were already above the floor and were left alone.

**Lesson for the next bump:** `aspire update` moves the Aspire packages only. If a build then
reports NU1605, expect a *family* move, not a single-package pin — bump the whole
`Microsoft.Extensions.*` 10.0.x set in one pass instead of iterating package by package.

## Verification

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** (88 warnings, all pre-existing NU/CS advisories) |
| Unit matrix, 10 projects | **2,420 passed / 0 failed** — Architecture 38, Core 92, Assignments 672, Assignments.Api 87, Admin 546, Families 42, Students 422, Settings 519, Students.Api 1, Settings.Api 1 |
| Portal `pytest` | 14 passed |

## Residuals (runtime — blocked by Docker on this machine)

Docker Desktop is in Resource Saver mode (`docker version` hangs), so no runtime check was
possible:

- **AppHost cold start under 13.5.4.** The SDK-attribute migration and the removal of the
  `Aspire.Hosting.AppHost` reference are *structural* changes; they should be proven by a real
  start (containers created, 0 port-allocation warnings, `Distributed application started`,
  every resource Healthy) before this is treated as fully validated.
- **`Aspire.Hosting.Python` runtime behaviour.** The portal's discovery assumption
  (`services__assignments-api__http__0`) and the spike's findings ("`WithMcpServer` absent",
  CDN-loaded prefab renderer) were verified at **13.4.5** — and the Python integration is one
  of the bumped packages, so they need a re-check at 13.5.4
  (`documents/solution/portals-service-client-pattern.md` §Findings).
- The four Testcontainers **Integration suites** remain Docker-gated and were not run.
