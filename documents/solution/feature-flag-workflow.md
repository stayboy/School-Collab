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
(e.g. `FEATURE:DisableOIDCAuth`, `FEATURE:DisableKeycloakLoginUi`). **The same owner gate applies here** — the
owner names a startup switch before it exists; an agent proposes and waits (§ "A flag is the OWNER's decision").
The pattern is the
**AppHost parameter** — one write-point, fanned out; the adopted design, its evidence and its
guards are in [`startup-flag-governance.md`](../specs/startup-flag-governance.md).

1. Declare it once in the AppHost — `builder.AddParameter("feature-flag-<name>")` in
   `src/AppHost/SchoolCollab.AppHost/Program.cs`.
2. Commit **two** values in the AppHost's own configuration:
   - `appsettings.json` → `Parameters:feature-flag-<name>` = `"false"` — **fail-closed**,
     because this is what `aspire publish` bakes into a deployment manifest;
   - `appsettings.Development.json` → `Parameters:feature-flag-<name>` = `"true"` — the dev
     posture, beside the other dev-only values (the Keycloak dev secrets set the precedent).
   (Two values, one parameter: the env var outranks both, so a real deployment can still
   override deliberately.)
3. Fan it out to **exactly** the hosts that read it —
   `.WithEnvironment("FeatureFlags__FEATURE__<Name>", param)`.
4. Consumers read it via `IConfiguration` (`AddAuthAndTenancy` does, at registration time) or
   through `ConfigurationFeatureFlagService`.
5. Guard it: **no** per-host `appsettings*.json` may carry the key (an env-var fan-out
   silently masks such a copy), and the fan-out set must be exact — see
   `AppHostStartupFlagWiringArchitectureTests` (Guard A + Guard B) for the template.

Dev overrides go to user-secrets on the **AppHost** project
(`dotnet user-secrets set "Parameters:feature-flag-<name>" "true"`). A standalone host run
(`dotnet run` without the AppHost — the only place a parameter is fanned from) sets
`FeatureFlags__FEATURE__<Name>=<true|false>` directly.

## Runtime feature flags (the common case)

These live in the Config service's `config-db` and are managed through the
admin UI at `/config-flags` (or the `/api/config/*` endpoints).

### A flag is the OWNER's decision, not the agent's

**An agent never introduces a flag.** The owner names the key and its purpose before any work starts; if you
believe a flag is needed, **propose it and wait** — key, default state, the consumers it would gate, and why the
default alone cannot carry the risk. This applies to **both** kinds below, and to a flag added "just for the first
release". The instruction is the owner's (2026-10-26) and it exists because a flag is a product decision about
opt-in shape, not an implementation detail an agent can pick while coding.

**Do not add a flag when the new behaviour already defaults to the safe state.** If the change is opt-in — a
setting that defaults to today's behaviour, a per-record toggle, a surface nobody reaches without asking for it —
then the default **is** the safety, and a dark-launch flag only adds a switch someone must later retire. Two
consecutive rounds proved both halves of this: `FEATURE:EnableActivityGroups` was retired precisely because the
feature had become established (PR #420), and the subject-enrolment round was explicitly built **without** a flag
because `RequiresEnrollment` defaults `false` and therefore changes nothing until an admin flips one subject.

### Adding a new runtime flag

1. **Confirm the owner named it.** See above — a flag with no owner-stated key and purpose does not proceed.
2. **Pick a key** of the form `FEATURE:<AreaName>` (positive `Enable*` form is
   preferred for new flags, e.g. `FEATURE:EnableNewDashboard`).
3. **Create it in the admin UI** at `/config-flags` → *New Flag*. Set the
   default state and a reason. (Or seed it in the migrator for flags that must
   exist from day one, like `FEATURE:EnableCodedValuesAiChat`.)
4. **Consume it** by injecting `IFeatureFlagService` and calling
   `IsEnabledAsync("FEATURE:<Area>")`. The host must have registered the
   cached client via `AddConfigFeatureFlagClient(...)` (the unified admin host
   does; an API that needs a runtime flag calls it too).
5. **Tenant overrides** are added from the flag's detail page — one per tenant,
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

**Removing the flag is converting every consumer, not deleting the gate.** The key is gone by
then, so grepping it proves nothing — grep the residue shape the compiler cannot see: an
`if (false)`, a `return;` that now always fires, or a `bool enabled` that nothing ever assigns
to `true`. And verify the **capability** survived, not just the code: a test that asserts "we
called `/activity-groups`" passes against a page that then hides every option the call returned,
because bUnit cannot see `FluentOption` children while the popup is closed. Assert the rendered
consequence. (2026-10-26: the `FEATURE:EnableActivityGroups` retirement lost the Activity Group
owner filter exactly this way — `Subjects.razor` kept `if (false)`, the guard test only checked
the HTTP call, and only a pre-flight code review caught it. PR #420.)

### Replaced by a policy field (the reconciliation pattern)

A runtime flag whose knob becomes tenant/grade configuration is retired in two
releases, not one:

1. **Reconcile** — the policy field is resolved alongside the flag and the two are **OR'd**, so
   behaviour is unchanged for every tenant that only used the flag, and the new field is an
   independent trigger for anyone who configures it.
2. **Retire** — once the policy surface has shipped to UI, delete the flag's call sites, its
   rows/artefacts (the AppHost cold-start parameter, the `documents/configuration.md` rows) and
   the OR.

**In flight: `FEATURE:RequireAssignmentApproval`** (round `assignment-policy-core`, D3). Step 1
shipped in Round A — `PublishAssignmentCommandHandler` and `ScheduleAssignmentCommandHandler`
gate on `effectiveAssignmentPolicy.RequiresApprovalBeforePublish || flag`, where the effective
policy merges the tenant row (`PUT /api/settings/assignment-policy`) with an optional per-grade
override (`PUT /grade-levels/{id}/assignment-policy`). The resolver is fail-open to
`false`, so a Settings/Students outage can never turn approval ON by itself. Step 2 (the deletion)
is Round B, together with the new policy editor. Full record:
[`assignment-policy-fields.md`](./assignment-policy-fields.md) §5/§8.