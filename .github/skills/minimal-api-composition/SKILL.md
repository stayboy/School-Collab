---
name: minimal-api-composition
description: |
  SchoolCollab minimal-API composition conventions. Use when adding, moving, or
  reviewing an ASP.NET Core endpoint in this repo: new route, new API project,
  MapGroup, RequireAuthorization, feature-flag-gated group, "where does this
  endpoint go", "Results vs TypedResults", or "why is my group 401/403 in
  tests". Triggers: "add an endpoint", "new API route", "minimal API",
  "MapGroup", "RouteGroupBuilder", "RequireAuthIfOidc", "RequireAuthorization",
  "DisableOIDCAuth", "TypedResults", "Results.Ok", "OpenAPI schema missing",
  "SpecialtyRoutes", "orchestrator endpoints".
---

# Minimal-API composition (SchoolCollab)

Full pattern, templates and rationale: `documents/solution/endpoint-organization-pattern.md`.
This skill is the short form plus the deltas that only exist in this repo. Both are
guarded by `MinimalApiCompositionArchitectureTests`, `BearerForwardingWiringArchitectureTests`
and `OpenApiSurfaceArchitectureTests` — if you fight the guards, you are off-pattern.

---

## 1. Two layers, always

```
<ApiProject>/
├── Program.cs
├── <Domain>Endpoints.cs     ← orchestrator: MapGroup + policy + chained Map*Routes()
└── Endpoints/
    ├── <Specialty1>Routes.cs
    └── <Specialty2>Routes.cs
```

**Orchestrator** — `Map<Domain>Endpoints(this WebApplication app, IFeatureFlagService featureFlags)`:
owns `app.MapGroup(prefix)`, the group's authorization policy, feature-flag gating, and the chain of
`Map*Routes()` calls. No inline `MapGet`/`MapPost` — the guard fails the build if you add one.

**Specialty** — `Map<Specialty>Routes(this RouteGroupBuilder group)`: relative paths only, returns the
group for chaining. It must **not** call `RequireAuthorization` and must **not** read feature flags —
it inherits both from the group. (`ConfigResolveRoutes.cs` is the one documented exception: it takes a
`bool oidcEnabled` and gates a single route at route level.)

Dead ends to avoid: a parallel `Endpoints/<Specialty>/` subfolder (one flat folder, the file name carries
the specialty); a controller (no controllers in any API host); a route mapped in `Program.cs`.

## 2. Never hand-roll the auth guard

The four-line block below is forbidden. It is now one call, and the flag is read in exactly one file
(`src/SchoolCollab.Core/Auth/EndpointGroupAuthExtensions.cs`):

```csharp
// FORBIDDEN — 21 copies of this were removed 2026-10-17
if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
{
    group.RequireAuthorization(policy => policy
        .RequireAuthenticatedUser()
        .AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme));
}

// INSTEAD
group.RequireAuthIfOidc(featureFlags);                        // authenticated on the Bearer scheme
group.RequireAuthIfOidc(featureFlags, RequireAssignmentReader); // caller-supplied policy
```

Pick the overload by **what the group must accept**, not by taste:

| Group serves | Overload |
|---|---|
| API-to-API callers (forwarded bearer token, ar-24) | default |
| Portal-session users, mixed callers, role gates | callback with a policy that pins `PortalSessionAuthenticationHandler.GatewaySchemeName` |
| Route-level gate on a non-group builder | out of scope — see `ConfigResolveRoutes.cs` and copy that comment |

`null` flag service means **gate on** (the endpoint-test hosts compose the pipeline without the flag
service). Never "fix" a failing test by making `null` mean gate-open.

## 3. Response typing — the proven pattern

Return `TypedResults` **with an explicit union return type**. `TypedResults` alone is enough: measured 2026-10-17 on
24 converted endpoints, it emits real response schemas through `AddOpenApi` **without any `.Produces<T>()`**. Do not
add `.Produces` metadata "to be safe" — it duplicates what the compiler already infers.

```csharp
// 2 outcomes: annotate the lambda, success arm first
group.MapGet("/{id:guid}", async Task<Results<Ok<CodedValueDto>, NotFound>> (
    Guid id, [FromServices] IQueryHandler<GetCodedValueById, CodedValueDto?> handler, CancellationToken ct) =>
{
    var result = await handler.HandleAsync(new GetCodedValueById(id), ct);
    return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
});
```

Rules that cost a compile cycle if you learn them the hard way:

- **`TypedResults` and the union must both be present.** `TypedResults.Ok(x)` in a bare lambda still infers `IResult`
  in a ternary — `Ok<T>` and `NotFound` share no common struct type. Annotate `Task<Results<…>>`.
- **More than 3 arms → fall back to `Task<IResult>`** and say why in a comment. `Results<…>` supports at most 8, but a
  4-arm union stops being readable; the converted `approve` handler is the sanctioned example.
- **An anonymous payload cannot appear in a union.** If a handler returns `new { ex.Message }` or `new { id }`, name the
  payload first (`internal sealed record MessageResponse(string Message)`, `IdResponse(Guid Id)`) — keeping property
  names and order exactly as the anonymous object had them so the JSON is byte-identical. Prove it with an assertion on
  the body text, not by inspection.
- **Preserve the `Created` location string** verbatim (`TypedResults.Created($"/api/…/{id}", new IdResponse(id))`).
  A changed `Location` header is a wire change nobody notices until a client breaks.
- **Never introduce `ProblemDetails`** while converting — error-shape work is a separate, human-gated decision
  (`endpoint-organization-pattern.md` §7.1).

**The pin.** `OpenApiSurfaceArchitectureTests` counts operations that document at least one response schema, per host,
and pins the number. After the CodedValue pilot it is **19 of 66 Settings operations** (12 document a 200/201 body,
7 a 400/409 message body only, 5 have no body at all), Students 0 of 146. A 409/400 arm **with a payload documents
`content`** — do not assume 4xx arms are bodyless. Raise the pin deliberately in the PR that converts handlers, to the
measured value, never to a predicted one. Scope rule: **new and touched endpoints only** — no sweep of the remaining
legacy `Results.*` sites.

## 4. Errors and validation — do not import the upstream model

Validation stays exception-driven (handlers throw; the host maps). The `dotnet-webapi` skill's
endpoint-filter + `ValidationProblem` (RFC 7807) model is **deferred**, not forgotten: it is a wire-shape
change and needs a human decision. Rationale and revisit conditions:
`documents/solution/endpoint-organization-pattern.md` §7.1.

Likewise `.http` files are **declined as a committed standard** (§7.2). Cheap repo-wide coverage comes
from `OpenApiSurfaceArchitectureTests`; a throwaway local `.http` probe is fine, but do not commit one.

## 5. Before you call it done

```bash
dotnet build SchoolCollab.slnx
dotnet test tests/SchoolCollab.ArchitectureTests.Unit/SchoolCollab.ArchitectureTests.Unit.csproj
```

Both must be green. The guards that fail most often, and what each one means:

| Guard failure | Meaning |
|---|---|
| `RequireAuthIfOidc(...)` count off by one | you added a group and forgot the guard, or removed a group the test still expects |
| `hand-rolled ... DisableOIDCAuth` should be 0 | you copy-pasted the old block instead of calling the helper |
| `Bearer opt-in` count ≠ 1 | the helper was bypassed or a second opt-in appeared |
| `.MapGet(` found in an orchestrator | routes belong in a `*Routes.cs` file |
| OpenAPI path count mismatch | a route is mapped but not in the document (or a group root lost its path) |

Known, pre-existing defects that are *not* yours to fix while passing through:
`Assignments.Api`'s OpenAPI document cannot be generated (`JsonException` converting `System.Guid` from a
request-body schema) — `OpenApiSurfaceArchitectureTests` pins the failure so it fails loudly when fixed.
