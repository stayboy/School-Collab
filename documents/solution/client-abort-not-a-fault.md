# Client disconnects are not server faults

**Status:** implemented, 2026-10-01 · **Scope:** all HTTP hosts via `SchoolCollab.ServiceDefaults`
**Trigger:** a startup `System.OperationCanceledException` traced to `ListRootCodedValuesHandler`

## 1. Finding — the symptom

At application start, the Settings API logged an unhandled exception and rendered it through the
developer exception page:

```
System.OperationCanceledException: The operation was canceled.
  at System.Threading.CancellationToken.ThrowIfCancellationRequested()
  at Microsoft.Extensions.Caching.Hybrid.Internal.DefaultHybridCache.StampedeState`2.<<JoinAsync>...MoveNext()
  at SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.ListRootCodedValues.ListRootCodedValuesHandler.HandleAsync(...)
  at SchoolCollab.Settings.Api.Endpoints.CodedValueRoutes.<>c.<<MapCodedValueRoutes>b__0_1>d.MoveNext()
  at Microsoft.AspNetCore.Http.RequestDelegateFactory.<ExecuteTaskResult>d__144`1.MoveNext()
  at Microsoft.AspNetCore.Routing.EndpointMiddleware.<<Invoke>g__AwaitRequestTask|7_0>d.MoveNext()
  at Serilog.AspNetCore.RequestLoggingMiddleware.<Invoke>d__10.MoveNext()
  at Microsoft.AspNetCore.Authorization.AuthorizationMiddleware.<Invoke>d__11.MoveNext()
  at Microsoft.AspNetCore.Authentication.AuthenticationMiddleware.<Invoke>d__6.MoveNext()
  at Microsoft.AspNetCore.Diagnostics.DeveloperExceptionPageMiddlewareImpl.<Invoke>d__14.MoveNext()
```

## 2. Finding — the mechanism the trace proves

1. **The exception is thrown by a *joiner*, not by the handler's own work.** `HybridCache` stampede
   protection lets the first caller run the factory while every other caller *joins* it, awaiting
   **with its own** token (`StampedeState.JoinAsync` → `WithCancellationAsync`). A joiner whose token
   is cancelled therefore throws `OperationCanceledException` out of the endpoint even though the
   query never ran and the peer's factory is still healthy.
2. **Nothing was cancelled by the server.** `ListRootCodedValuesHandler` passes the request token into
   `GetOrCreateAsync(cancellationToken: cancellationToken)` — the same pattern as its sibling
   `GetCodedValuesByParentHandler`, and the documented `HybridCache` usage. The token is cancelled
   because the **caller disconnected** (page navigation, worker shutdown, circuit teardown).
3. **It surfaced as a fault**, not a log line: `DeveloperExceptionPageMiddlewareImpl` is in the stack,
   so the exception escaped the endpoint and was logged at **Error** (and exported to the Aspire
   dashboard by Serilog's OTLP sink). `Serilog`'s request logging also records the aborted request as
   a failure.
4. **No host had a guard for this.** `IExceptionHandler` appears nowhere in `src`; the API hosts wire
   `UseAuthentication`/`UseAuthorization`/`MapDefaultEndpoints`/`UseSerilogRequestLogging` only. The
   Blazor apps have `UseExceptionHandler("/Error")`, the four HTTP APIs had nothing.

Consequence: **any** caller that disconnects from **any** `HybridCache`-backed endpoint in **any** host
produces an Error-level unhandled exception. The coded-values endpoint is merely where it was noticed,
because the startup backfill walk (≈143 sequential requests — see
`adr-cross-module-calls.md`, Phase 1 step 4) generates a burst of them at exactly the moment other
callers (the UI's `CodedValueDropdown`s, the AI coded-values tool) are fetching the same keys.

## 3. Why the first hypothesis was wrong

The first explanation was a client-side timeout: `SchoolCollab.Students.Worker`'s `"settings-api"`
client had `Timeout = 30s`, the backfill issues ~143 sequential calls, and a cold call stalling past
30s would abort the request. Two changes were made on that basis (timeout 30s → 60s; a bounded retry).
**The trace disproves the timeout half:** it shows a *joiner* awaiting a peer's factory, not a factory
stalling, and it shows the abort being reported as a server fault rather than only abandoning the walk.
The timeout raise was therefore reverted (the ADR's timeout rule wants a fail-fast, sub-default value
anyway); the **retry was kept**, because it fixes a real, independently verified gap — one transient
failure used to log at Error and leave the local projection unhydrated until the next restart.

The general lesson matches the policy-dialog round: *fix the mechanism the evidence shows, not the
hypothesis that first fits the symptom.*

## 4. Decision

**Absorb, at the host pipeline, exactly the cancellation that a client disconnect explains — and
nothing else.**

| Option | Verdict |
|---|---|
| `IExceptionHandler` + `ProblemDetails` in each API host | Rejected: coupling to `ProblemDetails` changes the response body for *genuine* faults, and the dev exception page would stop rendering real errors |
| Catch in the two cached `CodedValues` handlers, return `[]` | Rejected: fabricates an empty result for a request that was cancelled, and hides aborts from non-HTTP callers |
| Catch in `MapCodedValueRoutes` (endpoint group) | Rejected: the same exception arises from every `HybridCache`-backed endpoint — whack-a-mole |
| Don't pass the request token into `GetOrCreateAsync` | Rejected: deviates from documented `HybridCache` usage and keeps DB work running after the caller has gone |
| **`ClientAbortMiddleware` in `ServiceDefaults`, wired in the HTTP hosts** | **Adopted** |

## 5. Implementation

- **`src/ServiceDefaults/SchoolCollab.ServiceDefaults/ClientAbortMiddleware.cs`** — absorbs
  `OperationCanceledException` **only** when `HttpContext.RequestAborted.IsCancellationRequested`;
  logs at **Debug** (the disconnect is routine; Serilog has already recorded the aborted request). A
  cancellation raised while the request is still alive — a genuine cancellation bug (disposed,
  wrongly-scoped, prematurely-cancelled token) — and every other exception **propagate unchanged**.
- **`src/ServiceDefaults/SchoolCollab.ServiceDefaults/Extensions.cs`** — `UseClientAbortHandling()`,
  called **immediately after** `app.UseSerilogRequestLogging()` in each HTTP host.

### Why the catcher must sit *inside* request logging

Placement is load-bearing, and the obvious spot is wrong. `UseSerilogRequestLogging()` (and the
`DeveloperExceptionPageMiddleware` above it) are the two loggers that emit the Error entry. Serilog's
request-logging middleware has an internal `catch` that calls `WriteCompletionLog(..., ex)` — its
default level function maps *any* exception to `Error` — and then **rethrows**. A catcher registered
right after `builder.Build()` therefore sits *outside* it: the exception still passes through Serilog,
the Error entry with the full stack is still written, and only the developer exception page is
silenced.

Registering the middleware *after* `app.UseSerilogRequestLogging()` puts it inside: the abort is
absorbed before it reaches Serilog's `catch`, the request completes normally, and neither logger sees
a fault. Middleware order is by `Use…` call order, and the `Map…Endpoints` registrations that follow
are terminal, so an inner middleware still wraps every endpoint.
- **Wired in the four HTTP hosts** — `Settings.Api`, `Students.Api`, `Assignments.Api`, `AI.Server`.
  Workers have no HTTP pipeline; the Blazor apps (Admin, Families, Auth) keep their existing
  `UseExceptionHandler("/Error")` and are deliberately untouched.
- **`CodedValueBackfillService`** (kept from the earlier pass) — bounded 3-attempt, Warning-level
  retry; the backfill client timeout stays at the explicit 30s.

## 6. Verification

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | 0 errors |
| `SchoolCollab.Settings.Api.Tests.Unit` | 14 passed, 0 failed (5 new in `ClientAbortMiddlewareTests`) |
| `ClientAbortMiddlewareTests` coverage | absorb on abort (OCE + `TaskCanceledException`), propagate when the request is alive, propagate other exceptions even when aborted, clean pipeline runs downstream once |

Two independent review agents (`kimi-k2.7-code`, `glm-5.3`) were dispatched on this bug and agreed
with the solo review's direction; their findings and my verification of them are recorded in §7–§8
below. All other checks above were run by the session agent alone.

## 7. The aborter, identified (2026-10-01)

Two independent agents (kimi-k2.7-code, glm-5.3) plus direct verification converged: the aborting
caller is the **Admin shell** — not the startup backfill, and not `CodedValueDropdown`.

- `src/SchoolCollab.Admin/Components/Layout/NavMenu.razor:230` calls
  `Api.GetRootValuesAsync(_loadCts.Token)` — the **root** `GET /api/coded-values/` route — whenever
  `_currentModule` is `settings` or `coded-values` (`:188`).
- `NavMenu.DisposeAsync` (`:250-257`) calls `_loadCts.Cancel()`.
- `src/SchoolCollab.Admin/Components/App.razor:29,33` uses
  `InteractiveServerRenderMode(prerender: false)`, so **every full page load creates a new circuit
  and disposes the previous one** — the act of loading an admin page cancels the previous page's
  in-flight root GET. That is why the symptom correlates with admin page loads only, and only under
  `/settings` or `/coded-values`.

The route named in the stack is the diagnostic. `ListRootCodedValuesHandler` ⇒ the **root** route,
whose page-load callers are `NavMenu.razor:230`, `CodedValues/Index.razor:173`, `Edit.razor:219/235`
and the AI tool (`CodedValuesToolProvider.cs:393`). `GetCodedValuesByParentHandler` would have meant
the **by-parent** route, whose caller is `CodedValueDropdown.razor` — an earlier hypothesis of mine,
disproved by this route check.

**No data is lost.** The query is read-only, a cancelled populate never commits a cache entry, and
the `HybridCache` factory outlives the aborted creator, so live joiners still receive the value —
which is exactly why the page renders correctly while the server logs a fault.

## 8. Silent-failure hardening — the one real silent defect (fixed)

The only genuine silent-failure path both agents found, now closed: `CodedValueDropdown` and
`NavMenu` swallowed **every** `OperationCanceledException`, so a cancellation that did **not** come
from the component's own load token (e.g. the HTTP transport timing out) left the control disabled on
its placeholder forever, with no error and no explanation.

- `CodedValueDropdown.razor` — the catch is now guarded
  `when (ct.IsCancellationRequested || _disposed)`; a foreign cancellation falls through to the
  generic handler and surfaces `Unable to load options for {parentCode}.`
- `NavMenu.razor` — the same guard, and the token is captured in a **local**, because `DisposeAsync`
  cancels *and nulls* `_loadCts` (dereferencing the field inside a catch filter would NRE).
- Tests: `tests/SchoolCollab.Admin.Tests.Unit/CodedValueDropdownCancellationTests.cs` — a foreign
  cancellation is surfaced; a superseded (own-token) load stays silent and the newer load wins.
  **Discrimination proved** by temporarily reverting the guard: the test fails and the rendered
  markup captures the silent state itself — `<fluent-select … disabled>` showing `Loading…`.

## 9. Residual / follow-ups

- **Blazor hosts** (Admin, Families, Auth) were not wired with `UseClientAbortHandling`: their
  disconnects are circuit teardowns handled by the hub, and they already route unhandled exceptions
  to `/Error`.
- Other components still swallow cancellation unguarded — `ContactsEditor.razor` (7 sites) and
  `CodedValues/{Index,Edit,Create,Children}.razor`. Same class of silent failure; not touched here.
- `NavMenu`'s log-only path has no test: no test project references the Blazor host project.
- The **per-node backfill walk** (~143 sequential calls) remains the reason this is easy to hit at
  startup — see `adr-cross-module-calls.md`, Phase 1 step 4.
