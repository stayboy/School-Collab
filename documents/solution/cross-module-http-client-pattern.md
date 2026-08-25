# Cross-module HTTP client reference pattern

## Problem

When one Aspire service calls another via `IHttpClientFactory`, the factory
rotates the cached `HttpMessageHandler` chain every **2 minutes** by default.
While a request is in flight, or when a connection from the pool is closed by
the remote end, the next `SendAsync` can fail with:

```text
System.ObjectDisposedException: Cannot access a disposed object.
Object name: 'System.Net.Sockets.NetworkStream'.
```

The `TenantPropagationDelegatingHandler` is the outermost handler in the admin
→ API pipeline. It only adds the `x-tenant-id` header; it does **not** block
calls. Because it is the first custom handler, the disposed-stream exception
bubbles up through it, so the symptom looks like "the tenant propagator is
blocking module-to-module API calls."

## Reference-pattern solution

Use `SchoolCollab.Core.Http.CrossModuleHttpClientExtensions.AddCrossModuleHttpClient`
for every HTTP client that crosses an Aspire service boundary. It configures:

1. **Longer handler lifetime** — `30 minutes` instead of the factory default,
   so rotation rarely interrupts an active call.
2. **`CrossModuleRetryDelegatingHandler`** — retries once on the transient
   handler/connection-level failures:
   - `ObjectDisposedException`
   - `IOException` with a disposed `NetworkStream`
   - `HttpRequestException`
   - `500` / `408` responses
3. **Transient `TenantPropagationDelegatingHandler`** — registered as transient
   so it is never captured by a disposed DI scope or reused beyond its handler
   chain lifetime.

## Usage

```csharp
// Blazor admin module → API (needs tenant header)
services.AddCrossModuleHttpClient<StudentsApiClient>("https+http://students-api", propagateTenant: true);

// API → API (tenant is already resolved from the inbound request; no dev-switcher header)
services.AddCrossModuleHttpClient("students-core-coded-values", "http://settings-api", propagateTenant: false)
    .AddTypedClient<CodedValuesApiClient>();
```

### Aspire AppHost: `WithReference` is required

For the `AddCrossModuleHttpClient` base address to resolve, the **calling**
project must have a `.WithReference(otherApi)` in the AppHost. Without it,
service discovery has no endpoint for the target service and the call falls back
to literal DNS, producing:

```text
No such host is known. (settings-api:80)
```

Example for `Students.Api` calling `Settings.Api`:

```csharp
var studentsApi = builder.AddProject<Projects.SchoolCollab_Students_Api>("students-api")
    .WithReference(studentsDb)
    .WithReference(settingsApi)   // enables service discovery for the CodedValues hop
    ...;
```

This is the most common failure mode when the local-projection flag is off:
the HTTP client exists, but Aspire has not been told to inject the target
service's endpoint.

### Guard test

`CrossModuleWiringTests` (tests/SchoolCollab.Core.Tests.Unit/Architecture)
enforces this mechanically: it scans every cross-module base address in
`src/**/*.cs` against the AppHost's `WithReference` wiring and fails the build
with an actionable message when a reference is missing or the service name is
a typo.

### CI enforcement

`.github/workflows/ci.yml` runs a dedicated **Cross-module wiring guard** job
on every PR/push to `main`. It builds `SchoolCollab.Core.Tests.Unit` and runs
only `CrossModuleWiringTests`, so a missing `.WithReference()` blocks the PR
with a clear failure before the change reaches a runtime environment.

## When to use this vs. the local-projection pattern

This reference pattern is the right default for **operational, non-reference**
cross-module calls (e.g., delete-guard checks, recipient resolution, assignment
references). For **reference data** that is read repeatedly during writes and
must be available even if the remote module is unreachable, prefer the
local-projection pattern (`Students:UseLocalCodedValueProjection`) described in
`adr-cross-module-calls.md`.

## See also

- `src/SchoolCollab.Core/Http/CrossModuleHttpClientExtensions.cs`
- `src/SchoolCollab.Core/Http/CrossModuleRetryDelegatingHandler.cs`
- `src/SchoolCollab.Core/Auth/TenantPropagationDelegatingHandler.cs`
- `documents/solution/adr-cross-module-calls.md` (local-projection ADR)
