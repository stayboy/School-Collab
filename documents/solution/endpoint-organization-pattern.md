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
        if (!featureFlags.IsEnabled("FEATURE:DisableOIDCAuth"))
        {
            group.RequireAuthorization();
        }

        group
            .Map<Specialty1>Routes()
            .Map<Specialty2>Routes()
            .Map<Specialty3>Routes();

        return app;
    }
}
```

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
All existing integration tests, `*.http` files, OpenAPI specs, and external
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
- [ ] No route URI in the source differs from the route URI listed in
      OpenAPI / `.http` files / external client code.
- [ ] The `dotnet build` for the project succeeds with 0 errors and 0 new
      warnings.
- [ ] The unit/integration smoke test for the project still passes.

## 7. Worked Example: Students API

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
