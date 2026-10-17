# Endpoint Organization Pattern

This document defines the standard pattern for organizing HTTP endpoint mappings
across the SchoolCollab solution's minimal-API projects. The goal is to keep each
bounded context's endpoint surface area maintainable as the number of routes grows,
while preserving a single, consistent URI prefix and authorization policy.

It is **mandatory** for every API project under `src/`.

This document pairs with [`cqrs-organization-pattern.md`](./cqrs-organization-pattern.md) — every specialty in an API's `Endpoints/` folder must have a matching specialty in the corresponding `*.Core` project.

## 1. Scope

This rule applies to every minimal-API project in `src/`, including:

- `SchoolCollab.Students.Api`
- `SchoolCollab.Assignments.Api`
- `SchoolCollab.CodedValues.Api`
- Any future bounded-context API (e.g. `Attendance.Api`, `Timetables.Api`).

It does **not** apply to:

- Razor Pages / Blazor hosts under `SchoolCollab.Admin.*` — those route via
  Razor file conventions, not minimal API mappings.
- Background workers that do not expose HTTP endpoints.

## 2. Required Folder Structure

Every API project that maps HTTP routes must contain:

```
<ApiProject>/
├── Program.cs
├── <Domain>Endpoints.cs              ← orchestrator: group + auth, wires each Map*Routes
└── Endpoints/                        ← flat folder, all route files side-by-side
    ├── <Specialty1>Routes.cs
    ├── <Specialty2>Routes.cs
    └── ...
```

### Folder rules

- **One folder, flat.** All specialty route files live in a single `Endpoints/`
  folder. Do **not** create a parallel subfolder per specialty
  (e.g. `Endpoints/Students/StudentRoutes.cs`) — the file name already conveys
  the specialty, and the redundant folder adds navigation cost without value.
- **The orchestrator file stays at the project root**, next to `Program.cs`. It
  owns the `MapGroup(prefix)` call, the auth/feature-flag policy, and the
  chain of `Map*Routes()` calls. It must **not** contain any inline endpoint
  mappings.
- **Request/response DTO records that are bound only to a single specialty's
  routes live in that specialty's file** (e.g. `TransferStudentRequest` lives
  next to the enrollments endpoints that use it). They must **not** be
  duplicated in `Program.cs` or the orchestrator.

## 3. Naming

| Element | Convention | Example |
|---|---|---|
| Orchestrator static class | `<Domain>Endpoints` (camel of the URI group root) | `StudentEndpoints`, `AssignmentEndpoints`, `CodedValueEndpoints` |
| Orchestrator entry-point method | `Map<Domain>Endpoints(this WebApplication, ...)` | `MapStudentEndpoints`, `MapAssignmentEndpoints` |
| Specialty static class | `<Specialty>Routes` (PascalCase, singular) | `StudentRoutes`, `GradeLevelRoutes`, `SubjectRoutes` |
| Specialty extension method | `Map<Specialty>Routes(this RouteGroupBuilder)` | `MapStudentRoutes`, `MapGradeLevelRoutes` |
| Specialty file name | `<Specialty>Routes.cs` | `EnrollmentRoutes.cs` |
| Specialty namespace | `SchoolCollab.<Domain>.Api.Endpoints` | `SchoolCollab.Students.Api.Endpoints` |

All specialty files share the same namespace (`SchoolCollab.<Domain>.Api.Endpoints`),
so no per-specialty namespace changes are needed when adding a new one.

## 4. Wiring Contract

Each specialty extension method:

- Accepts a `RouteGroupBuilder` (the one created by the orchestrator) and
  **returns it unchanged**, so the orchestrator can chain calls.
- Maps routes using **relative paths only** (e.g. `group.MapGet("/grade-levels", ...)`),
  never the full URI. The orchestrator owns the absolute prefix.
- Does **not** call `RequireAuthorization()` itself. The orchestrator applies the
  policy once on the group.
- Does **not** inspect feature flags. That responsibility belongs to the
  orchestrator (see `FEATURE:DisableOIDCAuth` in the Students API).

### Orchestrator template

```csharp
using SchoolCollab.Core.Features;
using SchoolCollab.<Domain>.Api.Endpoints;

namespace SchoolCollab.<Domain>.Api;

public static class <Domain>Endpoints
{
    public static WebApplication Map<Domain>Endpoints(
        this WebApplication app, IFeatureFlagService featureFlags)
    {
        var group = app.MapGroup("/<uri-prefix>");
        group.RequireAuthIfOidc(featureFlags);

        group
            .Map<Specialty1>Routes()
            .Map<Specialty2>Routes()
            .Map<Specialty3>Routes();

        return app;
    }
}
```

### The authorization guard (single write-point)

Never hand-roll the flag check on a group. `RequireAuthIfOidc`
(`src/SchoolCollab.Core/Auth/EndpointGroupAuthExtensions.cs`) is the one place that reads
`FEATURE:DisableOIDCAuth` for a group policy, and it has two overloads:

```csharp
group.RequireAuthIfOidc(featureFlags);                            // authenticated user on the Bearer scheme (ar-24 opt-in)
group.RequireAuthIfOidc(featureFlags, RequireSomePolicy);         // caller-supplied policy, e.g. a gateway-scheme role gate
```

- The **default** overload is the ar-24 receiving-side opt-in: it pins
  `AuthTenancyExtensions.BearerScheme`, so an API group can accept a forwarded bearer token.
- The **callback** overload is for policies that must *not* pin Bearer — the portal-session role gates
  (`RequireAssignmentReader` / `RequireAssignmentWriter`) and the portal-facing admin policy all pin
  `PortalSessionAuthenticationHandler.GatewaySchemeName`, which routes a portal session, a bearer caller
  and a dev caller correctly through one policy.
- A `null` flag service means **gate on** (the production posture) — never gate-open. That preserves the
  endpoint-test hosts, which compose the pipeline from public extensions without registering the flag service.

Guarded by `MinimalApiCompositionArchitectureTests` and `BearerForwardingWiringArchitectureTests`: the
helper is the only file allowed to contain the Bearer opt-in, and no endpoint file may contain a
hand-rolled `if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))` block.

### Specialty template

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.<Domain>.Core.Commands.<Verb><Entity>;
using SchoolCollab.<Domain>.Core.Queries.<Verb><Entity>;

namespace SchoolCollab.<Domain>.Api.Endpoints;

public static class <Specialty>Routes
{
    public static RouteGroupBuilder Map<Specialty>Routes(this RouteGroupBuilder group)
    {
        // ── <Specialty> ──────────────────────────────────────────────────────

        group.MapGet("/", async (...) => ...);
        group.MapGet("/{id:guid}", async (...) => ...);
        // ...

        return group;
    }
}

internal record Update<Specialty>Request(...);
```

## 5. URIs Are Stable

The refactor described in this document is a **source-layout change only**. It
must not change any route URI, HTTP method, request shape, or response shape.
All existing integration tests, OpenAPI specs and external callers
callers (e.g. the `StudentsApiClient` in `SchoolCollab.Students.Admin`) must
continue to work without modification.

When extracting an endpoint from a monolithic file into a specialty file:

1. Move the route mapping verbatim — keep the path template, the handler
   signature, the `Results.*` call, and the exception-to-status mapping.
2. Move any request DTO record that the handler binds from `[FromBody]`.
3. Leave the orchestrator's prefix and auth policy untouched.

## 6. Audit Checklist

When reviewing a new API or a PR that adds/changes endpoints, verify:

- [ ] All specialty route files are directly under `Endpoints/` (no nested
      per-specialty subfolders).
- [ ] The orchestrator file contains no inline `MapGet`/`MapPost`/etc. calls —
      only `MapGroup`, auth, and chained `Map*Routes()` calls.
- [ ] `Program.cs` contains no request/response record DTOs (except `Program`
      itself for the integration-test `public partial class Program` marker).
- [ ] The group's policy is applied through `RequireAuthIfOidc(...)`, and the file contains no
      hand-rolled `if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))` block.
- [ ] New or edited endpoints return `TypedResults` (`.Produces`-visible response types), not the
      untyped `Results` factory. Existing `Results.*` sites are converted on touch — not swept.
- [ ] No route URI in the source differs from the route URI listed in
      OpenAPI / `.http` files / external client code.
- [ ] The `dotnet build` for the project succeeds with 0 errors and 0 new
      warnings.
- [ ] The unit/integration smoke test for the project still passes.

## 7. Deferred and declined upstream recommendations

Source: the `dotnet-webapi` skill (`dotnet/skills` → `plugins/dotnet-aspnetcore/skills/dotnet-webapi`,
MIT), evaluated 2026-10-17 — `documents/specs/minimal-api-composition.md`.

### 7.1 Deferred — ProblemDetails validation

The skill prescribes data-annotation validation via an endpoint filter returning `ValidationProblem`
(RFC 7807). Deferred 2026-10-17: the solution returns no `ProblemDetails` today and validates inside
handlers (e.g. `ValidationException` in `AssignmentRoutes.cs`), so adopting the filter model is an API
**error wire-shape** change and needs a human decision. Revisit as its own round with a client-
compatibility inventory (`SchoolCollab.Admin.Shared` API clients, portal Python clients, bUnit/Playwright
assertions).

### 7.2 Declined — `.http` request artifacts

Declined as a committed standard, permitted ad hoc, for four reasons:

1. **Duplicate of a generated artifact.** Every API host already maps OpenAPI
   (`AddOpenApi`/`MapOpenApi` in `Assignments.Api/Program.cs:210,271`, `Settings.Api/Program.cs:43,49`,
   `Students.Api/Program.cs:85,94`), so a `.http` file is a hand-maintained copy that can only drift from
   the generated document — silently, because nothing executes it.
2. **No CI execution path.** The VS Code and JetBrains HTTP clients run in-IDE only; there is no
   first-party runner, so a `.http` file cannot be a gate — and the repo's testing standard only counts
   artifacts that can fail.
3. **The need is already met executably.**
   `tests/SchoolCollab.Assignments.Api.Tests.Unit/*RouteTests.cs` drive real HTTP pipelines
   (reader/writer/portal-session policies, guardian token filter, guardian sign-off) through a minimal
   TestServer host, and the Students/Settings integration + Playwright suites cover the rest.
4. **Worst fit for this repo's hardest axis.** Every call needs a Keycloak bearer or portal-session
   credential plus a tenant id, which `.http` can only satisfy by out-of-band token minting and a pinned
   tenant — exactly what `TestAuthHandler`, the dev tenant switcher and `WebApplicationFactory` solve in code.

Cheap repo-wide coverage comes instead from `OpenApiSurfaceArchitectureTests`, which asserts the mapped
surface against the generated document and pins the count of operations that document a response schema
(the current baseline is 0, because handlers still return untyped `Results.*` — raise that number
deliberately when converting handlers).

No `.http` file is committed, reviewed or counted as coverage. A throwaway one for a local probe is fine.

## 8. Worked Example: Students API

The canonical reference implementation is `SchoolCollab.Students.Api`. After
applying this pattern the layout is:

```
src/Students/SchoolCollab.Students.Api/
├── Program.cs
├── StudentEndpoints.cs              ← orchestrator
└── Endpoints/
    ├── StudentRoutes.cs
    ├── GradeLevelRoutes.cs
    ├── SubjectRoutes.cs
    ├── PeriodRoutes.cs
    ├── EnrollmentRoutes.cs
    ├── GradeSubjectAssignmentRoutes.cs
    └── StudentTopicAssignmentRoutes.cs
```

All seven files live in the namespace `SchoolCollab.Students.Api.Endpoints`.
The orchestrator owns the `/students` group, OIDC enforcement, and the
`FEATURE:DisableOIDCAuth` switch.
