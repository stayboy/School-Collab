# Round — `portal-claims-port-binding`

**Status:** **accepted** 2026-10-06 — reviewer approved with non-blocking notes; guard red-then-green probe passed; P2 amended pre-PR
**Tier:** Light (Tier 2) — one worker run + an independent static diff reviewer
**Models:** parent `ollama-cloud/deepseek-v4.1-flash` (session) · worker `ollama-cloud/deepseek-v4.1-flash` · diff reviewer `ollama-cloud/kimi-k2.7-code`
**Branch:** none — no branch/commit authorized.

## Plan

### The defect (confirmed, not suspected)

The Assignments host registers the portal-session scheme but never binds the **port its handler's
constructor requires**, so activating the handler throws instead of authenticating.

| Step | Evidence |
|---|---|
| The handler requires the interface | `src/SchoolCollab.Core/Auth/PortalSessionAuthenticationHandler.cs:105` — ctor takes `IPortalSessionClaimsReader claimsReader` |
| `AddScheme` registers the handler **in DI**, so the ctor deps must resolve | `PortalSessionAuthenticationExtensions.AddPortalSessionAuthentication` → `AddScheme<PortalSessionAuthenticationOptions, PortalSessionAuthenticationHandler>` |
| The host binds only the **concrete** reader | `src/Assignments/SchoolCollab.Assignments.Api/Program.cs:249` → `AddCrossModuleHttpClient<PortalSessionClaimsReader>(…)` → `services.AddHttpClient<TClient>(…)` registers `TClient` **alone** (`src/SchoolCollab.Core/Http/CrossModuleHttpClientExtensions.cs:36`) |
| Nothing binds the interface in this host | Repo-wide, the only production registration is the auth service's own (`src/SchoolCollab.Auth/AuthServiceExtensions.cs:121` — a different host) |
| **The path is live, not latent** | `src/Assignments/SchoolCollab.Assignments.Api/AssignmentEndpoints.cs:112` and `:140` do `.AddAuthenticationSchemes(PortalSessionAuthenticationHandler.GatewaySchemeName)` + `RequireAuthenticatedUser` + `RequireRole(…)` |
| The trigger is the portal's normal call | `SelectAuthenticationScheme` returns the portal scheme whenever `X-Portal-Session` is present (flag-independently), and the portal sends that header on teacher-route calls (`src/SchoolCollab.Portals/api/assignments_api_client.py:18`) |
| Why no test caught it | Every Assignments test host **stubs** the port (`AssignmentReaderPolicyRouteTests.cs:221`, `AssignmentWriterPolicyRouteTests.cs:223`, `PortalSessionReaderPolicyTests.cs:147`, `RealAuthRouteTests.cs:58`) |

**Consequence:** `InvalidOperationException: Unable to resolve service for type
'SchoolCollab.Core.Auth.IPortalSessionClaimsReader' while attempting to activate
'PortalSessionAuthenticationHandler'` → **500** on portal-authenticated teacher calls, and a 500 where the
challenge owes a bare 401.

**The adoption round named this failure mode and applied its remedy only to test hosts.** Its own D7
doctrine (`documents/rounds/round-portal-session-adoption.md:102`, revision 2 at `:13`) requires every
host to gain *"both the `AddPortalSessionAuthentication` call … and a stub `IPortalSessionClaimsReader`
(the moved handler's required ctor dep … **an unregistered dep is a DI failure, not a 401**)"*. The
production host was never given the binding, so this round is closing a gap the doctrine already
identified — not inventing a new rule.

### Settled by the owner grill (2026-10-06)

1. **Binding shape (Q1 → a):** a **forwarding line in `Assignments.Api/Program.cs`**, beside the existing
   typed-client registration. The Core seam that makes the drift unrepresentable (b) and the
   two-type-parameter overload (c) are **deferred** — they exist to serve a second remote adopting host,
   and the portal's scope closed at read/review/grade with no authoring route. Revisit on the day a
   second host adopts the scheme.
2. **Prevention (Q2 → a):** a **source-inspection architecture guard only**. `ValidateOnBuild = true` on
   the host is **rejected**: the AppHost is never started in CI, so it would have caught this only when a
   human ran the app, and it is a host-wide posture change that could surface unrelated latent gaps.
3. **Proof strength (Q3 → a):** the guard alone; **no** refactor of `Program.cs` into a callable
   registration extension and **no** real-DI-resolution test. The honest residual is recorded below.
4. **Acceptance (Q4 → guard only):** **no live-host probe.** The owner accepted the guard as sufficient
   evidence.

### Change set (test-only plus one production line)

| # | File | Change |
|---|---|---|
| 1 | `src/Assignments/SchoolCollab.Assignments.Api/Program.cs` | bind the port: `AddTransient<IPortalSessionClaimsReader>(sp => sp.GetRequiredService<PortalSessionClaimsReader>())`, immediately after the `AddCrossModuleHttpClient<PortalSessionClaimsReader>` call, with a comment naming the DI-failure-not-a-401 rule |
| 2 | `src/Assignments/SchoolCollab.Assignments.Api/Services/PortalSessionClaimsReader.cs` | **rewrite** the falsified remark — it claims the host registers the reader "via `AddCrossModuleHttpClient`", which binds the concrete class only |
| 3 | `tests/SchoolCollab.ArchitectureTests.Unit/PortalSessionClaimsPortWiringArchitectureTests.cs` (new) | the guard |

**Guard rules (follows the `BearerForwardingWiringArchitectureTests` / `AppHostEndpointPortingArchitectureTests`
idiom — `FindRepoRoot()` walk-up, `Read(...)` helper, count-based assertions with named reasons, and
**line comments stripped before matching** so a commented-out call cannot pass or fail it):**
- every host source calling `.AddPortalSessionAuthentication(` must also register
  `IPortalSessionClaimsReader` **somewhere inside that host's own project directory** (the nearest
  ancestor holding a `*.csproj`), not merely the same file — the bind is a DI act that a host may legally
  keep in a registration extension;
- the set of hosts found is **pinned to exactly the two that exist** (the auth service and the Assignments
  API) — a third adopting host must extend the pin, so the guard grows with its class;
- **the guard must not be able to go vacuous**: it also reads the handler and asserts its constructor
  still names `IPortalSessionClaimsReader`, so the whole guard fails loudly if the port ever stops being
  a required dependency.

### Verification

1. `dotnet build SchoolCollab.slnx` — 0 errors.
2. `dotnet run --project tests/SchoolCollab.ArchitectureTests.Unit -c Debug` — the guard's suite (93 tests
   → 96 with its three methods), 0 failed.
3. `dotnet run --project tests/SchoolCollab.Assignments.Api.Tests.Unit -c Debug` — the reader-policy and
   real-auth route suites, 0 failed.
4. **Red-then-green probe on the guard itself:** with the Program.cs bind temporarily removed the guard
   must **fail**; restored, it must pass. The guard is only accepted if it demonstrably catches *this*
   defect.
5. Independent static diff review (reviewer, patch by path).

### Out of scope, deliberately

The Core seam (b), `ValidateOnBuild`, a DI-resolution test over the extracted registration, the live
500→401 host probe, and any change to the auth service's own registration.

## Worker Report

Delivered as planned: 3 files, no new project, no new package, nothing staged.

- **`Program.cs:252`** — the bind, immediately after the `AddCrossModuleHttpClient<PortalSessionClaimsReader>(…)` statement (the surrounding comment block untouched; `using SchoolCollab.Core.Auth;` was already present):
  ```csharp
  builder.Services.AddTransient<IPortalSessionClaimsReader>(sp => sp.GetRequiredService<PortalSessionClaimsReader>());
  ```
  with a comment naming the rule (an unregistered ctor dep is a request-time DI failure, a 500 not a 401) and why it forwards to the typed client rather than `AddTransient<IFace, Impl>()` (so the typed client's own pipeline — base address, retry handler, handler lifetime — is what gets used).
- **`PortalSessionClaimsReader.cs`** — the falsified `<remarks>` sentence **rewritten**: the host registers the typed client via `AddCrossModuleHttpClient<PortalSessionClaimsReader>` **and then forwards `IPortalSessionClaimsReader` to it with its own registration line**, because the helper binds the concrete class alone. Behaviour unchanged.
- **`PortalSessionClaimsPortWiringArchitectureTests.cs`** (new) — three methods, following the `BearerForwardingWiringArchitectureTests` / `AppHostEndpointPortingArchitectureTests` idiom (`FindRepoRoot()` walk-up, `Read(...)`, count-based assertions with named reasons, line comments stripped before matching):
  - **(a) `EveryAdoptingHost_AlsoBindsTheClaimsPort`** — scans every `.cs` under `src/` (bin/obj excluded) for `.AddPortalSessionAuthentication(` (the leading dot distinguishes the call from Core's own declaration), groups hits by nearest ancestor directory holding a `*.csproj`, and requires each host's **own project tree** to register `Add{Singleton\|Transient\|Scoped}<IPortalSessionClaimsReader>`. Host-project-scoped, not file-scoped, so a host may legitimately keep its bind in a registration extension.
  - **(b) `AdoptingHosts_ArePinnedToTheTwoThatExist`** — `HaveCount(2)` against the pinned pair (`src/SchoolCollab.Auth` via `AuthServiceExtensions.cs`; `src/Assignments/SchoolCollab.Assignments.Api` via `Program.cs`), each with the adoption call in the pinned file, so a third adopting host must extend the pin ("the guard grows with its class").
  - **(c) `HandlerConstructor_StillRequiresTheClaimsPort`** — reads the handler and asserts its primary-constructor parameter list still names `IPortalSessionClaimsReader`, so rules (a)+(b) cannot go vacuous if the port ever stops being a required dependency.

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** (pre-existing warnings only) |
| `dotnet run --project tests/SchoolCollab.ArchitectureTests.Unit -c Debug` | **96/96, 0 failed** (93 before; +3 methods) |
| `dotnet run --project tests/SchoolCollab.Assignments.Api.Tests.Unit -c Debug` | **158/158, 0 failed** |
| **Red-then-green probe** on the guard | **PASSED the probe**: with the `Program.cs` bind commented out, `EveryAdoptingHost_AlsoBindsTheClaimsPort` **failed** with the named DI-failure reason ("…an unregistered ctor dependency is a DI failure at request time — an HTTP 500 where the challenge owes a bare 401 — not a build error…, but found 0"), while the auth host kept passing in the same run (3 total / 1 failed / 2 succeeded). After restoring the file (sha256 `b9db9f8c…` identical to the pre-probe hash) the filtered class was 3/3 green, and both full suites were re-run green. |
| `git status --short` | only the three files, plus the pre-existing unrelated entries (the `documents/**` sweep, the unrelated `AssignmentIndexBunitTests.cs`) — nothing staged |

**Residual risks the worker named (kept verbatim in substance):** (i) **no runtime proof** — the guard is source-shape evidence; no test drives the real `Assignments.Api` pipeline with a portal session header, which is the Q4 decision (guard-only acceptance); (ii) rule (a) proves *a* registration exists in the host project, not that it resolves to the remote typed client — a stub registration in production code would satisfy it; (iii) the scan root is deliberately `src/` (test hosts are outside the guard's class) and pinning the adoption *file* per host is an intentional over-pin stated in the failure message; (iv) the comment stripper is the repo's naive `//` stripper — the worker verified no scanned file carries a `//`-bearing string literal ahead of a matched token.

## Review

Static diff reviewer (`ollama-cloud/kimi-k2.7-code`), read-only, patch passed by path. **Verdict: approve with notes — "No merge-blocking issues found."**

| Question | Verdict | Evidence |
|---|---|---|
| Q1 — bind correctness and lifetime | **Correct** | The handler is constructed per request from the request's service scope (`AddScheme<…, PortalSessionAuthenticationHandler>`), so resolving the **transient** port from that scope is right and is not a captive-dependency/root-scope leak. `GetRequiredService<PortalSessionClaimsReader>()` returns the instance produced by the `AddHttpClient<PortalSessionClaimsReader>` registration — base address `https+http://auth`, the retry handler, the 30-minute handler lifetime — so the forward reuses the typed client's pipeline instead of a fresh default `HttpClient`. The interface had no prior registration in this file, so `AddTransient` is additive, not clobbering, and there is no ordering hazard. |
| Q2(a) — can the guard pass with a *wrong* registration? | **Yes — accepted residual** | A stub registration anywhere in the host project would satisfy rule (a). The reviewer judged this acceptable, not a P1, because the defect class is "host adopts the scheme but forgets to register the interface", which is exactly what the guard catches. |
| Q2(b) — `ProjectDirectoryOf` resolution | **Verified correct** | Both hosts resolve to their own project: `AuthServiceExtensions.cs` → `SchoolCollab.Auth.csproj`; `Program.cs` → `SchoolCollab.Assignments.Api.csproj`. A nested project would resolve to the closest one, which is the desired grouping. |
| Q2(c) — the first-`)` ctor parse | **Works today; P2 brittleness** | The real primary-ctor list (`PortalSessionAuthenticationHandler.cs:101-105`) has no nested parens, so the first `)` is the list's close. A future parameter type containing `)` before the port would truncate early — and the guard would **fail loudly**, not silently pass. |
| Q2(d) — the `//` stripper | **Adequate today; P3 latent** | The only `//`-bearing literal in the matched region is `"https+http://auth"`, which sits on a different line from every matched token, so no match can be corrupted. |
| Q2(e) — the host/file pin | **Not over-pinning** | Intentional and consistent with the repo's *pinned updates* doctrine; its failure message states that moving the adoption call inside the same project stays legal but requires updating the pin — structural drift becomes a deliberate act rather than a silent vacuous pass. |
| Q3 — scope | **Bounded** | The only behaviour change is the forward; `PortalSessionClaimsReader.cs` changes only its `<remarks>`; the new test adds no project, no package, no `.slnx` row. |
| Q4 — overlap with `BearerForwardingWiringArchitectureTests` | **Safe coexistence** | That guard pins bearer-handler attachment and `AllowAutoRedirect = false` counts; this one pins that an adopting host binds the port. Orthogonal failure modes — neither duplicate nor contradiction. |

### Findings

| # | Severity | Finding | Smallest fix |
|---|---|---|---|
| 1 | **P2** | Rule (c) parses the primary-ctor parameter list by searching for the first bare `)` (`PortalSessionClaimsPortWiringArchitectureTests.cs:135`). | Parse to the balanced closing paren of the ctor list (or stop at the derived-type separator line) instead of the first `)`. |
| 2 | **P3** | `StripLineComments` strips `//` anywhere on a line, including inside a string literal (`:238-245`). | Only matters if a future matched line gains a `//`-bearing literal; a stateful lexer would close it. |
| 3 | **P2** | Rule (a) is shape-based: a stub registration in production code would satisfy it. | None without the Q3-rejected real-DI-resolution test (owner decision). |

## Acceptance

**Accepted 2026-10-06.**

| Evidence | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | 0 errors |
| Architecture suite (`dotnet run --project tests/SchoolCollab.ArchitectureTests.Unit -c Debug`) | **96/96, 0 failed** (93 + the guard's 3 methods) |
| Assignments.Api suite | **158/158, 0 failed** |
| **Red-then-green probe on the guard** (the round's decisive evidence) | With the `Program.cs` bind commented out the guard **failed** with its named DI-failure reason while the auth host still passed; after a sha256-identical restore it went **3/3 green**. A guard that cannot fail on the defect is ceremony — this one demonstrably fails on it. |
| Independent static diff review | **approve with notes**; no merge-blocking issue |

**Accepted with two guard-internal notes deferred, not reworked:** the P2 first-`)` parse and the P3 `//` stripper both live **inside the guard**, and each can only ever produce a **loud false failure** — neither can mask the defect this round closes. They are left in place rather than reworked after the review so that the artifact the reviewer approved remains the round's frozen patch; `diffs-portal-claims-port-binding.patch` is therefore **not** rewritten. The P2 shape-vs-semantics residual (finding 3) is the owner's Q4 decision, recorded so it cannot be mistaken for an oversight.

**Owner-accepted residual (no runtime proof):** nothing exercises the real `Assignments.Api` pipeline with a portal session header — the four Assignments test hosts stub the port — so the `InvalidOperationException` → fail-closed transition is evidenced by DI shape plus the guard, not by a live probe. This is the deliberate Q4 acceptance ("guard is enough"), not a gap.

**Durable fold:** the host-side binding rule and the guard's name are folded into `documents/specs/keycloak-ui-auth-integration.md` (the D19 home), so this round's outcome survives the bulk-trash of `documents/rounds/` per its README.

## Amendment (2026-10-06, pre-PR — no third review pass)

Finding 1 (P2) was applied **after** the round was accepted, using the reviewer's own pre-cleared
smallest fix rather than a rework dispatch, on the repo's established precedent for applying a
guard-prescribed fix verbatim without a further review pass (adoption round, plan revision 2: *"the
bound is spent, so this revision applies the reviewer's pre-cleared smallest-fixes verbatim and no
third review pass runs"*).

- **What changed:** `HandlerConstructor_StillRequiresTheClaimsPort` no longer delimits the handler's
  primary-constructor parameter list at the **first `)`**. It now ends the slice at the **derived-type
  separator** (the first line after the declaration whose first non-whitespace character is `:`). A
  parameter type that later gains a nested parenthesis — a generic constraint, a tuple, a delegate —
  can no longer truncate the read and fail the guard against correct code. The assertion itself,
  its subject and its message are unchanged.
- **Behavioural coverage:** none lost. The guard still fails without the bind (rule (a) unchanged) and
  still fails if the port stops being a constructor dependency (rule (c)'s assertion unchanged).
- **Evidence:** the Architecture suite was re-run after the amendment (96/96) — see the delivery PR.
- **Frozen artifact untouched:** `diffs-portal-claims-port-binding.patch` is **not** rewritten and
  remains the point-in-time snapshot the reviewer approved; the amendment is recorded here, beside it.
  Findings 2 (P3, the naive `//` stripper) and 3 (P2, shape-vs-semantics, owner-accepted by Q4) stay
  deferred as recorded above.

## UI Tester

N/A — no UI delivered.
