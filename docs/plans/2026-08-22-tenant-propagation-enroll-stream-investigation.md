# Investigation: `TenantPropagationDelegatingHandler` on enroll-to-grade-and-stream

Date: 2026-08-22
Status: fix applied – root cause resolved (see "Live failure" below); all Class A + B follow-ups DONE (see Action plan)

## Summary

When enrolling a student to a **grade + stream**, the tenant is not propagated to the
settings-api for any of the coded-value reads the flow depends on. `TenantPropagationDelegatingHandler`
— the robust, topology-independent way to carry the dev-selected tenant across hosts — is only
attached to `StudentsApiClient`, not to the coded-value clients (client side or server side).
Because `CodedValue` is a hybrid-tenant entity, the settings-api falls back to the default
(`Guid.Empty`) tenant when the shared Redis `IDevTenantSelection` is unavailable, hiding the
tenant's own streams. A **grade-only** enrollment usually still works; the failure surfaces when a
**stream** is selected.

---

## Live failure on enrolling an existing student

`enrollstudentdialog.razor` failed in `TenantPropagationDelegatingHandler.SendAsync` with:

```
System.ObjectDisposedException: 'Cannot access a disposed object. Object name: 'System.Net.Sockets.NetworkStream''
```

**Root cause:** the handler was registered **Scoped** (`AddScoped`/`TryAddScoped`) on every
`AddHttpMessageHandler<TenantPropagationDelegatingHandler>()` client. The ASP.NET Core
`IHttpClientFactory` **caches and reuses** the message-handler chain across requests. A
**scoped** DelegatingHandler is resolved from a request scope and **disposed when that scope
ends**, but the cached chain keeps referencing it → a later request (a second dialog interaction,
or re-render) reuses the disposed handler and its downstream `SocketsHttpHandler` connection →
`ObjectDisposedException` on the `NetworkStream`. The handler is stateless (it only holds the
singleton `IDevTenantSelection` + `ILogger`), so Scoped buys nothing while causing the reuse bug.

**Fix (all registrations → Singleton):**
- `src/Students/SchoolCollab.Students.Application/ModuleServices.cs:15` — `StudentsApiClient`
- `src/Assignments/SchoolCollab.Assignments.Application/ModuleServices.cs:29` — `AssignmentsApiClient`
- `src/Settings/SchoolCollab.Settings.Application/ModuleServices.cs:31,43` — the four settings clients
- Same for `TenantForwardingDelegatingHandler` (holds only singleton `IHttpContextAccessor`):
  `src/Students/SchoolCollab.Students.Api/Program.cs:35` and
  `src/AI/SchoolCollab.AI.Server/Program.cs:85`

`TenantPropagationDelegatingHandler.SendAsync` also gained fault isolation (first guard): a cache
read failure (Redis down) proceeds **without** the header instead of failing the call; genuine
token cancellation still propagates.

1. `TenantPropagationDelegatingHandler`
   (`src/SchoolCollab.Core/Auth/TenantPropagationDelegatingHandler.cs`) reads the dev-selected tenant
   from `IDevTenantSelection` (Redis) and stamps an `x-tenant-id` header on each outbound request.
   Its own doc comment says it is the topology-independent path that "works even when the API host
   cannot read the shared cache."

2. Each API host's `TestAuthHandler` (`TestAuthHandler.cs:49-53`) resolves the request tenant in
   priority order: **`x-tenant-id` header → `IDevTenantSelection` (Redis) → default `Guid.Empty`**.
   It writes a `tenant_id` claim, which is flowed by `TenantClaimsTransformation` →
   `TenantProvider` → `ModuleDbContext.CurrentTenantId`, feeding the `"Tenant"` query filter.

3. `CodedValue` is a **hybrid-tenant** entity (`CodedValueConfiguration.cs:10-16`) with filter
   `TenantId == CurrentTenantId OR TenantId == null`. Tenant-owned rows (e.g. a school's GRSTREAMS)
   are hidden from every other tenant context, including the default `Guid.Empty` one.
---

## Root cause: the handler is only wired to `StudentsApiClient`

The enroll dialog (`EnrollStudentDialog.razor`) is rendered by the unified Admin host
(`SchoolCollab.Admin/Program.cs`), which calls `AddStudentsModule()` and `AddSettingsModule()`.

Registrations compared:

| File | Client | `x-tenant-id` handler |
|---|---|---|
| `src/Students/SchoolCollab.Students.Admin/ModuleServices.cs:15-18` | `StudentsApiClient` | **yes** (`.AddHttpMessageHandler<TenantPropagationDelegatingHandler>()`) |
| `src/Settings/SchoolCollab.Settings.Admin/ModuleServices.cs:25-26` | Admin.Shared `CodedValuesApiClient` (→ `https+http://settings-api`) | **no** |
| `src/Students/SchoolCollab.Students.Api/Program.cs:29-40` | `ICodedValuesApiClient` (Students.Core) + Admin.Shared client → `http://settings-api` | **no** (and no inbound-header forwarding) |

The enroll-with-a-stream path performs coded-value reads from the settings-api, and **none** of them
carry the tenant header:

| Enrollment step | Client (target) | `x-tenant-id` |
|---|---|---|
| Load periods / grade levels / submit `EnrollStudent` | `StudentsApiClient` (students-api) | yes |
| Grade dropdown (`GRADES`) load | Admin.Shared `CodedValuesApiClient` (settings-api) | no |
| Stream dropdown load (GRSTREAMS + `gradeLevel` attribute filter) | Admin.Shared `CodedValuesApiClient` (settings-api) | no |
| Server-side stream validation `GetByIdAsync(streamId)` in `EnrollStudentHandler.ValidateStreamAsync` (line 162) | Students.Core `ICodedValuesApiClient` (settings-api) | no |

Client-side stream picker path:
`CodedValueDropdown.razor:359-360` → `GetChildrenByParentCodeFilteredByAttributeAsync` →
`GET /api/coded-values/by-parent?parentCode=GRSTREAMS&attributeKey=gradeLevel&attributeValue=<grade>` —
list is computed against the wrong tenant when the header is missing.

Server-side stream validation path:
`EnrollStudentHandler.ValidateStreamAsync` → `ICodedValuesApiClient.GetByIdAsync(streamCodedValueId)`
→ settings-api `GET /api/coded-values/{id}`. Under the wrong tenant a tenant-owned stream returns
`null` → `StreamGradeMismatchException` → HTTP 400 → dialog error.

---

## Why it manifests on "grade + stream" (not grade only)

- Grade-level reads and the enroll submission go through `StudentsApiClient`, which **does** carry
  the header → the students-api resolves the correct tenant.
- Grade coded values are typically shared-blueprint (`TenantId == null`) rows, so they stay visible
  even under a mis-resolved default tenant.
- Streams are usually tenant-owned GRSTREAMS children; the hybrid filter hides them when the
  settings-api cannot resolve the tenant, so picking/validating a stream fails.

---

## Minimal change needed (review conclusion — REVISED)

### Why the previous proposal is wrong

The previous version proposed adding `TenantPropagationDelegatingHandler` to the **students-api's**
`ICodedValuesApiClient` registration. **That does not work** for an API→API hop:

`TenantPropagationDelegatingHandler` reads the tenant from `IDevTenantSelection` (Redis/cached
selection) — **not** from the current request's `HttpContext`. This is correct when the caller is
the **originator** of the selection (the admin shell, which just wrote to Redis via the dev tenant
switcher). But on the students-api, the caller is **not the originator**: the admin shell wrote the
selection. If the students-api uses `AddDistributedMemoryCache()` (no Redis, `Program.cs:18-21`),
its `IDevTenantSelection` is **empty**, the handler stamps **nothing**, and the settings-api still
falls back to `Guid.Empty`.

Meanwhile, the students-api **already resolved** the correct tenant for the request — `TestAuthHandler`
read the `x-tenant-id` header that `StudentsApiClient`'s handler stamped (`TestAuthHandler.cs:49-53`,
header-first), and wrote the `tenant_id` claim. That resolved tenant is sitting in `HttpContext.User`
but `TenantPropagationDelegatingHandler` ignores it entirely. Reusing the handler for an API→API hop
is using it for something it was explicitly not designed for (its doc says "originator→API",
`TenantPropagationDelegatingHandler.cs:8`).

### Correct minimal fix: one line, client-side

**Add `TenantPropagationDelegatingHandler` to `AddSettingsModule`'s `CodedValuesApiClient`** in
`src/Settings/SchoolCollab.Settings.Admin/ModuleServices.cs:25-26`:

```csharp
services.AddScoped<TenantPropagationDelegatingHandler>();
services.AddHttpClient<CodedValuesApiClient>(client =>
    client.BaseAddress = new Uri("https+http://settings-api"))
    .AddHttpMessageHandler<TenantPropagationDelegatingHandler>();
```

This is correct because:
- The admin host **IS** the originator of `IDevTenantSelection` — it just wrote the selection via
  the dev tenant switcher. Reading it back is reliable (same pattern as `StudentsApiClient`).
- The stream picker (`EnrollStudentDialog.razor:305-311`) calls `CodedValuesApiClient`
  `GetChildrenByParentCodeFilteredByAttributeAsync` → settings-api `GET /api/coded-values/by-parent?parentCode=GRSTREAMS&attributeKey=gradeLevel&attributeValue=<grade>`.
  Without the header, tenant-owned streams are hidden by the hybrid filter → **empty dropdown** is
  the most visible symptom.
- This is a one-liner that mirrors the existing `StudentsApiClient` registration exactly.
- The handler is already registered by `AddStudentsModule` on the admin host, so the
  `AddScoped<TenantPropagationDelegatingHandler>()` line is a safety-net for hosts that might call
  `AddSettingsModule` without `AddStudentsModule` (e.g. test hosts).

The server-side validation path (`EnrollStudentHandler.ValidateStreamAsync` → students-api →
settings-api) is **not** broken in the standard topology: when Redis is shared, the settings-api's
`TestAuthHandler` resolves the tenant via `IDevTenantSelection` fallback. When Redis is NOT shared,
neither the students-api's `IDevTenantSelection` nor the settings-api's can read it — so reusing
`TenantPropagationDelegatingHandler` there adds no value. The correct fix for that hop (a
request-context-forwarding handler reading `HttpContext.User.FindFirst("tenant_id")`) is a separate
concern and can be deferred.

### Out of scope

- Any change to `EnrollStudentDialog.razor`, `EnrollStudentHandler`, `EnrollmentRoutes`, the
  Admin.Shared `CodedValuesApiClient` class itself, or `Students.Api/Program.cs`.
- A request-context-forwarding handler for the students-api → settings-api hop (follow-up if
  Redis-less topologies become a requirement).

---

## Cross-module audit: does anyone else suffer the same issue?

Audited every cross-host HttpClient registration (2026-08-22). Two classes of gap exist:

**Class A — admin-shell→API clients missing the handler** (same as the enroll bug; fix = attach
`TenantPropagationDelegatingHandler`, admin IS the selection originator):

| Client | Target | Tenant-sensitivity | Status |
|---|---|---|---|
| `StudentsApiClient` | students-api | strict entities | ✅ covered |
| `AssignmentsApiClient` (`Assignments.Application/ModuleServices.cs:29-34`) | assignments-api | strict entities | ✅ covered |
| `CodedValuesApiClient` | settings-api | hybrid entities | ✅ covered (this fix) |
| **`NotificationPolicyApiClient`** | settings-api | **strict** (`TenantNotificationPolicy`) | ❌ **affected** — GET resolves the default tenant's policy (wrong data or empty); PUT writes under the default tenant or trips the `TenantContextRequiredException` save-guard |
| **`EntityCodeRulesApiClient`** | settings-api | **hybrid** (`EntityCodeRule`) | ❌ **affected** — tenant-owned rules invisible in the CRUD UI; code generation resolves blueprint-only |
| **`ConfigFlagsApiClient`** | settings-api | global flags + **strict** per-tenant overrides (`TenantFeatureFlagOverride`) | ❌ **affected** — the flag UI resolves per-tenant overrides by `CurrentTenantId`; without the header, overrides are invisible and toggles write against the default tenant |
| `TenantsApiClient` | settings-api | global registry (intentionally cross-tenant) | ✅ not applicable |

**Class B — API→API hops with no header forwarding** (fix = a small request-context-forwarding
handler reading `HttpContext`; reusing `TenantPropagationDelegatingHandler` does NOT work here,
per the analysis above):

| Hop | Caller file | Impact |
|---|---|---|
| students-api → settings-api | `Students.Api/Program.cs:29-40` (`ICodedValuesApiClient` + Admin.Shared client) | stream validation (`ValidateStreamAsync`) resolves the default tenant when Redis fallback fails — documented above |
| **settings-ai → settings-api** | `AI.Server/Services/CodedValuesAiExtensions.cs:23` (AI Tools `ICodedValuesApiClient`) | coded-value AI tools list/read the default tenant's coded values — the CodedValues drawer chat answers from the wrong tenant's data |
| **students-api → assignments-api** | `Students.Api/Services/ActivityGroupAssignmentQueryHttpClient.cs:25` (named client `"assignments-api"`) | activity-group delete-guard queries assignments for the wrong tenant; assignments are strict-tenant, so the guard can false-negative (allowing a delete that should be blocked) |

**Verified not affected:** `Settings.Api` makes no outbound HTTP calls; the Admin host's cached
feature-flag client (`AddConfigFeatureFlagClient`) reads the DB in-process so its tenant comes from
its own auth context; Workers/MigrationService have no user tenant context.

**Recommended follow-up order:** Class A is three one-line registrations in
`Settings.Application/ModuleServices.cs` (same pattern as this fix). Class B warrants one shared
`TenantForwardingDelegatingHandler` (reads `HttpContext.User.FindFirst("tenant_id")` /
forwards the inbound `x-tenant-id`) attached to the three API→API clients — one new class,
three registrations.

---

## Action plan (review status)

### Done ✅

1. **Root-cause analysis** — tenant not propagated on coded-value reads in the enroll flow;
   documented above with file:line evidence.
2. **Minimal fix applied** — `Settings.Application/ModuleServices.cs`: registered
   `TenantPropagationDelegatingHandler` (`TryAddScoped`) and attached it to `CodedValuesApiClient`
   via `.AddHttpMessageHandler<...>()`. Verified: build clean, 443/443 Admin unit tests +
   439/439 Settings unit tests pass.
3. **Cross-module audit** — every cross-host HttpClient registration classified (tables above).
4. **Regression tests added** — `tests/SchoolCollab.Settings.Tests.Unit/ModuleServicesTenantPropagationTests.cs`:
   resolves the real `CodedValuesApiClient` from a DI container built with `AddSettingsModule()`
   and asserts via a capturing primary handler that (a) the `x-tenant-id` header is stamped with
   the dev-selected tenant, and (b) no header is stamped when no tenant is selected. Suite:
   441/441 passing.
5. **End-to-end enrollment integration test added** —
   `tests/SchoolCollab.Students.Tests.Integration/EnrollWithStreamEndpointTests.cs`: real
   `POST /students/enrollments` against Testcontainers Postgres + RabbitMQ with a grade AND a
   stream. The settings-api hop is served by a capturing stub (echoing the requested id and the
   matching `gradeLevel` attribute) so the real `CodedValuesApiClient` pipeline runs. Asserts:
   201 Created; stream validation actually called the settings hop
   (`GET /api/coded-values/{streamId}`); the persisted enrollment carries the grade level and the
   stream; and a stream referencing another grade is rejected 400 with nothing persisted.
   Note: the endpoint uses the Option B contract (commit 7d8a93f) — the request carries
   `GradeCodedValueId`, resolved server-side to the GradeLevel row.
   Both tests pass; the one other failure in the full Students integration suite
   (`WithExplicitEffectiveDate_FiltersToThatDate`) was verified pre-existing on clean `main`
   (fails with these changes stashed) and is unrelated.

### Follow-ups — DONE ✅ (this session)

**Class A** (admin→API one-liners, all in `Settings.Application/ModuleServices.cs`, mirroring fix #2):
- ✅ A1 `NotificationPolicyApiClient` · ✅ A2 `EntityCodeRulesApiClient` · ✅ A3 `ConfigFlagsApiClient`
- `TenantsApiClient` deliberately left unscoped (global registry).

**Class B** (API→API forwarding):
- ✅ B1 New `TenantForwardingDelegatingHandler`
  (`src/SchoolCollab.Core/Auth/TenantForwardingDelegatingHandler.cs`): forwards the inbound
  `x-tenant-id` header (preferred — it's exactly what `TestAuthHandler` consumed), falling back to
  the authenticated principal's `tenant_id` claim; stamps nothing when neither is present
  (prod-safe, background callers stay unscoped).
- ✅ B2 students-api → settings-api: attached to both the Students.Core `ICodedValuesApiClient` and
  the Admin.Shared `CodedValuesApiClient` registrations in `Students.Api/Program.cs`.
- ✅ B3 settings-ai → settings-api: `CodedValuesAiExtensions.AddCodedValuesAiTools` gained an
  optional `Action<IHttpClientBuilder>` hook; `AI.Server/Program.cs` registers
  `IHttpContextAccessor` (the host had none — it runs without auth middleware) and attaches the
  forwarder. Note: this added a `SchoolCollab.Core` project reference to `AI.Server`.
- ✅ B4 students-api → assignments-api: forwarder attached to the named `"assignments-api"` client
  used by the activity-group delete-guard (data-integrity fix).

**Tests for Class B:**
- `tests/SchoolCollab.Core.Tests.Unit/Auth/TenantForwardingDelegatingHandlerTests.cs` — 5 cases:
  header forwarded; claim fallback; header precedence over claim; no HttpContext → no header;
  empty tenant → no header. Core suite: 69/69 passing.
- The enrollment integration test now ALSO asserts the forwarded header value on the captured
  settings hop request == the enroll request's tenant — end-to-end proof that both classes of fix
  work together.
- **End-to-end enrollment integration test added** —
   `tests/SchoolCollab.Students.Tests.Integration/EnrollWithStreamEndpointTests.cs`: real
   `POST /students/enrollments` against Testcontainers Postgres + RabbitMQ with a grade AND a
   stream. Asserts: 201 Created; stream validation called the settings hop
   (`GET /api/coded-values/{streamId}`) with the forwarded tenant header; the persisted enrollment
   carries grade level + stream; a stream referencing another grade is rejected 400 with nothing
   persisted. Uses the Option B contract (`GradeCodedValueId`, commit 7d8a93f).
   The one other failure in the full Students integration suite
   (`WithExplicitEffectiveDate_FiltersToThatDate`) was verified pre-existing on clean `main`
   (fails with these changes stashed) and is unrelated.

### Class A — DONE ✅ (was: pending admin→API one-liners)

| # | Change | File | Risk |
|---|---|---|---|
| A1 | Attach handler to `NotificationPolicyApiClient` | `Settings.Application/ModuleServices.cs` | low — strict entity, currently wrong-tenant reads/writes |
| A2 | Attach handler to `EntityCodeRulesApiClient` | same file | low — hybrid entity, blueprint-only visibility |
| A3 | Attach handler to `ConfigFlagsApiClient` | same file | low — per-tenant flag overrides invisible/mis-written |

All three mirror the applied fix exactly; no new classes; covered by existing test suites.

### Class B — DONE ✅ (was: pending API→API forwarding)

| # | Change | Files |
|---|---|---|
| B1 | New shared `TenantForwardingDelegatingHandler` in `SchoolCollab.Core/Auth` — forwards inbound `x-tenant-id` header (fallback: `HttpContext.User.FindFirst("tenant_id")`) onto outgoing requests; no-op when neither is present (prod-safe) | new file |
| B2 | Attach B1 to students-api → settings-api clients (`ICodedValuesApiClient` + Admin.Shared client) | `Students.Api/Program.cs` |
| B3 | Attach B1 to settings-ai → settings-api client | `AI.Server/Services/CodedValuesAiExtensions.cs` |
| B4 | **Attach B1 to students-api → assignments-api** (`"assignments-api"` named client) — highest priority in this class: delete-guard false-negative is a data-integrity issue | `Students.Api/Program.cs` + `ActivityGroupAssignmentQueryHttpClient.cs` |

Note for B1 design: prefer forwarding the **inbound header** over the claim when both exist — the
header is what `TestAuthHandler` consumed, so forwarding it reproduces the receiver's resolution
exactly. Unit tests: header present → forwarded; only claim → stamped as header; neither → no
header added.

### Suggested sequencing

1. ✅ Class A + B1/B4 landed together (A = same pattern as shipped fix; B4 = integrity bug).
2. ✅ B2/B3 followed (correctness of cross-module reads).
3. The "Redis-less topology" hardening is now MOOT for API→API hops —
   `TenantForwardingDelegatingHandler` never touches `IDevTenantSelection`.

### Verification results

- Core unit: 69/69 (incl. 5 new `TenantForwardingDelegatingHandlerTests` cases).
- Settings unit: 441/441 · Admin unit: 443/443.
- Students integration (`EnrollWithStreamEndpointTests`): 2/2 — now also asserting the forwarded
  `x-tenant-id` on the settings hop equals the enroll request's tenant.
- Full Students integration suite: 41/42; the single failure
  (`WithExplicitEffectiveDate_FiltersToThatDate`) verified pre-existing on clean `main`.

---

## Supporting evidence (files)

- `src/SchoolCollab.Core/Auth/TenantPropagationDelegatingHandler.cs`
- `src/SchoolCollab.Core/Auth/TestAuthHandler.cs` (header precedence, lines 45-53)
- `src/SchoolCollab.Core/Auth/TenantClaimsTransformation.cs`
- `src/SchoolCollab.Students.Api/Program.cs` (settings client registrations, lines 27-40)
- `src/Settings/SchoolCollab.Settings.Admin/ModuleServices.cs` (lines 20-42)
- `src/Students/SchoolCollab.Students.Admin/ModuleServices.cs` (lines 11-18)
- `src/Settings/SchoolCollab.Settings.Core/Data/Configurations/CodedValueConfiguration.cs`
- `src/Settings/SchoolCollab.Settings.Core/Data/SettingsDbContext.cs` (GlobalEntityAllowList, lines 45-68)
- `src/SchoolCollab.Admin.Shared/Services/CodedValuesApiClient.cs`
- `src/SchoolCollab.Admin.Shared/Components/CodedValueDropdown.razor` (attribute filter load, lines 354-360)
- `src/Students/SchoolCollab.Students.Core/CQRS/Enrollments/Commands/EnrollStudent/EnrollStudentHandler.cs` (lines 51-54, 154-181)
- `src/Students/SchoolCollab.Students.Application/Components/Students/EnrollStudentDialog.razor` (submit + stream)