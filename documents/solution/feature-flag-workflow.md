# Feature Flag Workflow

This document outlines how feature flags are added, observed, and retired in
the School-Collab solution.

## Overview

There are now **two distinct kinds** of "feature flag", and they are handled
differently. Read this before adding one.

| Kind | Example | Owner | Mutable at runtime? | Tenant-overridable? |
|------|---------|-------|---------------------|---------------------|
| **Deployment-time switch** | `FEATURE:DisableOIDCAuth` | `IConfiguration` (appsettings / env var) | No — read once at startup | No |
| **Runtime feature flag** | `FEATURE:EnableCodedValuesAiChat` | The central **Config service** (`SchoolCollab.Config`) | Yes — via the admin UI / API | Yes |

The split exists because ASP.NET Core auth schemes are registered once at
startup: `FEATURE:DisableOIDCAuth` is a *startup auth-mode switch*
(`AddAuthAndTenancy` reads it from `IConfiguration` directly), so it cannot be
flipped at runtime and is **not** a Config-service flag. Everything else a flag
might gate (a UI surface, a code path) is a *runtime, mutable, tenant-overridable*
flag and belongs in the Config service.

The central Config service is specified in
[`central-config-service-plan.md`](./central-config-service-plan.md) — that
document is the authority for the Config bounded context. See also
[`documents/configuration.md`](../configuration.md) §5.

## Deployment-time flags (rare)

Use only for genuine startup decisions that cannot be deferred to runtime
(e.g. `FEATURE:DisableOIDCAuth`).

1. Add the value to each consumer's `appsettings.json`
   (`FeatureFlags:FEATURE:<Area>`), with the dev default.
2. Production overrides it via the env var
   `FeatureFlags__FEATURE__<Area>=<true|false>`.
3. Consumers read it via `IConfiguration` (directly for startup decisions, or
   through `ConfigurationFeatureFlagService`).

## Runtime feature flags (the common case)

These live in the Config service's `config-db` and are managed through the
admin UI at `/config-flags` (or the `/api/config/*` endpoints).

### Adding a new runtime flag

1. **Pick a key** of the form `FEATURE:<AreaName>` (positive `Enable*` form is
   preferred for new flags, e.g. `FEATURE:EnableNewDashboard`).
2. **Create it in the admin UI** at `/config-flags` → *New Flag*. Set the
   default state and a reason. (Or seed it in the migrator for flags that must
   exist from day one, like `FEATURE:EnableCodedValuesAiChat`.)
3. **Consume it** by injecting `IFeatureFlagService` and calling
   `IsEnabledAsync("FEATURE:<Area>")`. The host must have registered the
   cached client via `AddConfigFeatureFlagClient(...)` (the unified admin host
   does; an API that needs a runtime flag calls it too).
4. **Tenant overrides** are added from the flag's detail page — one per tenant,
   with a required reason.

### How a runtime flag resolves

`IsEnabledAsync` → `ConfigFeatureFlagService` → HybridCache (L1 30s / L2 5min)
→ `GET /api/features/{tenant}` → resolver applies
`tenant override ?? global default`. If the Config API is unreachable it falls
back to `IConfiguration["FeatureFlags:FEATURE:<Area>"]` (warn-once per key), so
a consumer keeps working when Config is down. Changes propagate within the L1/L2
TTL ("sensible ITL"); a push-invalidation subscriber narrows that to ~1s in a
follow-up (see the plan §15).

### Observing changes

Every mutation writes an append-only `flag_audit_entries` row (who / when /
before → after / reason), viewable on each flag's detail page. A
`FeatureFlagChanged` event is also published on the `config` RabbitMQ exchange
(via the transactional outbox) for the future push-invalidation subscriber.

### UI Gating in Blazor

For Razor pages, gate UI on a runtime flag with the `<FeatureFlagGate>` component
(lives in `SchoolCollab.Admin.Shared`, designed in
[`documents/specs/ui-gate-component.md`](../specs/ui-gate-component.md)):

```razor
<FeatureFlagGate Key="FEATURE:EnableCodedValuesAiChat">
    <AiChatPanel />
</FeatureFlagGate>
```

`<FeatureFlagGate Key="FEATURE:<Area>">` resolves the same
`IsEnabledAsync("FEATURE:<Area>")` call described above, but as a declarative,
**reactive** surface: it re-evaluates (and re-renders) live when the flag is
toggled in the Config UI, with no page reload. It is tenant-aware automatically
because the underlying `ConfigFeatureFlagService` resolves per tenant. Use it
instead of an inline `IsEnabledAsync` + `if` when the gated thing is a block of
UI. Endpoints and startup switches keep using `IFeatureFlagService` directly.

## Retiring a flag

Once the gated behaviour is permanent, remove the `IsEnabledAsync` call site
and archive the flag from the admin UI (archived flags are excluded from
resolution but kept for audit). Delete it (soft-delete, recoverable) once no
audit window needs it.