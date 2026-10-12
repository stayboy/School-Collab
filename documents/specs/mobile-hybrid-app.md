# Mobile app — Blazor Hybrid head (feature spec)

**Status:** spec / plan-stage · authored 2026-10-09 · **no code yet**
**Predecessor (findings):** `documents/solution/maui-agent-skills-readiness.md`
**Delivery:** 4-layer `gh stack` train, one Tier-3 round per layer: L1 host+spike+inventory → L2 severance → L3 credential → L4 first slice. **L1 is a full round** — the spike writes `.razor` files, the deterministic UI trigger fires, and the UI tester is dispatched (this header's earlier "L1 runs lean — no UI files" contradicted §8's own AC-3 and is corrected 2026-10-09, plan-review pass).

---

## 1. Goal

A Blazor Hybrid MAUI app that reuses this repo's existing Razor component layer, authenticates
against Keycloak as a public client, and talks to the module APIs with a bearer token. Phone and
tablet from one codebase via adaptive layouts.

**Non-goals (explicitly out):** an offline store / background sync; push notifications; camera or
other device-native capability; reusing the 37 `@page` admin screens as shipped markup; AppHost
orchestration of the mobile head.

## 2. Pinned decisions

Resolved across the 2026-10-09 grill rounds; each is a decision, not a default.

| # | Decision | Consequence |
|---|---|---|
| D1 | **Blazor Hybrid**, not native XAML | The 94 components / ~28k lines in §3 are the asset; native would mean a second design system |
| D2 | **Phone + tablet from one codebase**, adaptive layouts | 37 `@page` screens are **reworked, not ported** — they are the reference for behaviour, not markup |
| D3 | **Separate solution** under `src/Mobile/` | `SchoolCollab.slnx` untouched, ubuntu CI stays green; must **not** be a second `.slnx` at the repo root |
| D4 | **API contract unchanged** | A Keycloak access token as `Authorization: Bearer` is already accepted on all three APIs (`AddAuthAndTenancy` → `.AddJwtBearer` with Keycloak authority + clientId audience) |
| D5 | **New public Keycloak client + PKCE** | Hybrid has no cookie, so the credential is token-based; the confidential `school-collab-client` cannot be used from a device |
| D6 | **Client-side `AuthenticationStateProvider` is mandatory** | `Admin.Shared` *is* the design system being reused and `Gate/GateBase.razor` lives in it; a `BlazorWebView` supplies none, and `TenantGate`'s `[Authorize]` needs one. The gates therefore come along by construction |
| D7 | **No AppHost orchestration** | The AppHost's value to a host is `WithReference` service-discovery env vars, which a device cannot resolve. Dev loop: AppHost for servers, then F5 the mobile project onto a device/emulator |
| D8 | **Base URLs come from configuration, never C# literals** | See §6 — **corrected 2026-10-09:** `CrossModuleWiringTests` *silently skips* localhost-class hosts (`excludedHosts`), so CI does **not** catch a localhost literal; the plan/review discipline is the only defense |

## 3. What is reused

Measured (see the readiness doc §5.1): **94 non-page components / 28,086 lines** in
`Admin.Shared` (24), `Students.Application` (47), `Assignments.Application` (16),
`Settings.Application` (7) — predominantly dialogs (44), section/editors (20), field/forms (9),
grids (6). The 37 `@page` screens (~12,201 lines) are reference only.

`SchoolCollab.Families` shares no RCL, so nothing in it is reused.

### 3.1 Dependency blocker (found 2026-10-09, before L1 was planned)

The 94 components are **not currently consumable from a device**, because `Admin.Shared`'s reference
closure drags the whole server stack into the mobile app:

```
Admin.Shared  ->  SchoolCollab.Core          (ASP.NET auth, EF Core, RabbitMQ.Client,
                                              System.Security.Cryptography.Xml)
              ->  SchoolCollab.Students.Core (EF Core, Npgsql, EFCore.NamingConventions,
                                              Aspire.RabbitMQ.Client, rate limiting)
              ->  ServiceDefaults             (ServiceDiscovery, OpenTelemetry, Serilog.AspNetCore)
```

What `Admin.Shared` actually *uses* from that closure is small — `SchoolCollab.Students.Core`
(10 usings), `SchoolCollab.Core.Features` (3), `SchoolCollab.Core.AssignmentPolicies` (1),
`SchoolCollab.Core.Notifications` (1) — i.e. UI-facing **types** (enums, DTOs, policy/notification
shapes), not server behaviour. That is the classic shape where a UI library depends on a domain
project that also happens to contain the `DbContext`.

**Unverified, and the reason for a spike:** whether a `net10.0-android` TFM even restores and builds
this closure. NuGet compatibility says it should resolve; whether it compiles, and whether it then
survives publish/trim, is untested. Restore success would not be evidence — the real objection is
that EF Core, Npgsql, RabbitMQ, ASP.NET auth, OpenTelemetry and `Serilog.AspNetCore` have no business
shipping in a phone app.

Consequence for this spec: §3's "reusable layer" is reusable **in principle**, and reaching it needs
either a dependency severance (extracting the UI-facing types out of `Students.Core` / `Core` into a
mobile-safe assembly) or a decision to reuse only the components that are already free of that
closure. The plan-stage fork is `## 9 OD0`.

### 3.2 Second-pass measurement (2026-10-09, before the L1 plan)

Re-verified after the first pass was challenged — it corrects §3.1's size estimate **downward** for
severance cost and pins the component-level split:

- **The RCL reference graph, unfiltered this time** (the first pass's display filter hid `Contracts`
  refs): `Admin.Shared → ServiceDefaults, Core, Students.Core`;
  `Students.Application → Admin.Shared, Students.Contracts, Students.Core`;
  `Settings.Application → Admin.Shared, AI.Chat, AI.Server, Settings.Core, Core`;
  `Assignments.Application → Admin.Shared, AI.Abstractions, Assignments.Contracts,
  Students.Application`. Two follow-ons: `Settings.Application` references **`AI.Server` — a web host
  project** — so its 7 components are definitively server-only; and `Assignments.Application`
  inherits the `Students.Core` closure **through its `Students.Application` reference**, so the
  L4 slice has its own severance question (see OD1).
- **`ServiceDefaults` is unused by `Admin.Shared`** — zero namespace/type usages; the ProjectReference
  appears droppable (hosts call `AddServiceDefaults()` themselves from their own references).
- **19 of 24 non-page `Admin.Shared` components are already clean** of server namespaces. The 5 dirty
  ones: `ContactChangeDialog`, `ContactFormFields`, `ContactsEditor`, `GateBase`, `GuardianEditFields`.
- **The gates' server dependency is `Core.Features` only** — `IFeatureFlagService`,
  `IFeatureFlagChangeNotifier`, `FeatureFlagKeys` — **interfaces and constants, not behaviour**. Moving
  them to a mobile-safe abstractions home is a small shared-kernel change, not a refactor.
- **The contacts/guardian family binds domain entities directly** — `Contact`, `Student`,
  `Guardian`, `GuardianRole`, `ContactChannel`, `ContactOwnerType` from `Students.Core.Domain` (the
  8 files carrying that using are that component family plus its form models). Severing those means
  DTOs + rewritten bindings — days, not hours — so under the accepted (d) rule those 5 components
  simply **stay out of the mobile subset** until a slice needs them.

**Net effect on the accepted plan (b)+(d):** day-one severance shrinks to *drop one unused
ProjectReference + move the `Core.Features` abstractions*; the remaining `Core` reference is what the
L1 spike measures under `net10.0-android` (with `Students.Core` still in the closure, since
severance lands in L2). The spike's deliverable is therefore exactly: does the closure
restore/build/publish-trim, and what does it cost?

**Spike outcome (L1 worker pass, 2026-10-09).** The closure **restores and compiles** in every TFM
(all eight closure projects), but `Admin.Shared` and `ServiceDefaults` each declare
`<FrameworkReference Include="Microsoft.AspNetCore.App"/>`, which NuGet records transitively for
the head in **every** TFM — and a self-contained mobile build needs a runtime pack per framework
reference, which `Microsoft.AspNetCore.App` has **none of** for android RIDs → **NETSDK1082**,
two errors. Causation proven by removing the head's `Admin.Shared` reference (the errors vanish);
the documented escape hatches do not work (`DisableTransitiveFrameworkReferenceDownloads` →
identical error; evaluation-time `FrameworkReference Remove` → re-added by the SDK's
`AddTransitiveFrameworkReferences`). A head-side shim that strips the reference builds 0-error but
emits **24 MSB3277** version conflicts against assemblies the app will not ship — **rejected as a
lying build**. **Consequence: severance is a prerequisite for any device build, not an option.**
The Windows TFM (`net10.0-windows10.0.19041.0`) builds 0 errors / 0 warnings with the closure
intact — which is what L1 asserts (parent-adjudicated option B, worker intercom).

## 4. Solution and project layout

```
src/Mobile/
  SchoolCollab.Mobile.slnx          # NOT at the repo root (two .slnx in one dir is an error)
  SchoolCollab.Mobile/              # the MAUI Blazor Hybrid head (thin)
  SchoolCollab.Mobile.Logic/        # net10.0 RCL: view models, clients, auth state  <-- CI-testable
tests/SchoolCollab.Mobile.Tests.Unit/   # net10.0, MTP, runs in the existing ubuntu job
```

- **Head TFM:** `net10.0-android;net10.0-ios;net10.0-maccatalyst` (+ `net10.0-windows10.0.19041.0`
  on Windows). A `net10.0` RCL is consumable from those TFMs, so **no RCL re-targeting is needed**.
- **The Logic split is load-bearing, not cosmetic:** MAUI UI targets cannot build or test on
  ubuntu, so anything testable must live in a `net10.0` library that the **existing**
  `build-and-test` job can see. The head keeps only host wiring and layout.
- **Solution membership is split by TFM:** the **head lives only in `SchoolCollab.Mobile.slnx`**;
  `SchoolCollab.Mobile.Logic` and `tests/SchoolCollab.Mobile.Tests.Unit` are `net10.0` and are added
  to **`SchoolCollab.slnx`** (which already hosts 17 test projects) so the ubuntu job builds and
  tests them with no workflow change. The head is never added to the root solution.
- **CPM:** new `PackageVersion` entries in `Directory.Packages.props` under a new
  `ItemGroup Label="MAUI"`; the `.csproj` references carry **no** `Version` attribute.
  Latest stable at time of writing: `Microsoft.Maui.Controls` / `…Components.WebView.Maui`
  **10.0.110** (matches the installed 10.0.x workload manifests) — re-verify at implementation.
- `global.json` already pins `Microsoft.Testing.Platform` as the test runner; the new test project
  follows the existing MTP conventions (`.github/copilot/rules/testing.md`).

## 5. Credential and registration path (L2)

**Flow:** system-browser OIDC (`WebAuthenticator`) → Authorization Code + PKCE against the new
public client → access + refresh tokens in `SecureStorage` → `DelegatingHandler` attaches
`Authorization: Bearer` → APIs validate against Keycloak with the configured audience.

**Realm changes** (`src/AppHost/SchoolCollab.AppHost/school-collab-realm.json`), modelled on the
existing confidential client:

| Element | Value |
|---|---|
| New client | public (`publicClient: true`), `standardFlowEnabled: true`, direct access **off** |
| Claim mappers — copy verbatim from `school-collab-client` | `tenant-id`, `tenant-name`, `tenant-type`, `teacher-id` (`oidc-usermodel-attribute-mapper`) and `roles` (`oidc-usermodel-realm-role-mapper`) |
| Audience mapper | `oidc-audience-mapper` with `included.client.audience = school-collab-client`, mirroring `audience-school-collab-client` — this is what makes `Auth:Keycloak:ClientId` audience validation pass |
| Redirect URI | the app's custom scheme (e.g. `schoolcollab://signin-oidc`) + the dev loopback, if used |

**Client-side registration path:** replace the 22 registrations / ~204 lines of the module
`ModuleServices.cs` with a client-side registration that keeps the **same interfaces** and swaps
the base address + handler. The components consuming them (`@inject CodedValuesApiClient`,
`IContactsClient`, …) do not change.

**Auth bridge:** implement `AuthenticationStateProvider` over the token's claims and call
`NotifyAuthenticationStateChanged`; the claim spelling is the realm-mapper contract
(`tenant_id`, `teacher_id`, `roles`), so `TenantGate` / `FeatureFlagGate` work unchanged.
Server-side tenancy and feature-flag enforcement are unaffected — the gates stay UI visibility.

## 6. Guard interactions (read before writing L1 code)

- **`CrossModuleWiringTests` scans `src/**/*.cs`** (excluding `bin`, `obj`, `AppHost`). Its
  literal regex is `"(?:(?:https?\+http)|http)://(?<host>[a-z][a-z0-9-]*)"` — note that plain
  `https://` literals never match it at all — and it fails on **unknown** service names.
  **Correction (2026-10-09, plan-review pass):** `ScanClientRegistrations` carries an `excludedHosts`
  set — `localhost`, `host.docker.internal`, `0.0.0.0`, `example.com`, `example.org` — whose matches
  are **silently skipped**, so a `http://localhost…` literal does NOT redden the build or its CI
  job. The guard is therefore no defense for this constraint; the plan-level discipline is the only
  one: **all API base URLs live in `appsettings*.json` or a config class bound from JSON — never as
  a C# string literal under `src/`** — and each round's diff review must grep `src/Mobile` for
  `http(s)?://` literals.
- **The mobile head is not an AppHost resource.** It is deliberately invisible to
  `AppHostEndpointPortingArchitectureTests` (endpoint count pinned at exactly **5**) and to
  `CrossModuleWiringTests`' host attribution, which derives hosts from `AddProject<…>` in
  `Program.cs`. That is the accepted consequence of D3/D7 and must be recorded as conscious, not
  incidental, at L2.
- **`AppHostRealmImportArchitectureTests`** guards the realm file's name and bind-mount agreement —
  L2 edits that file, so re-read that guard before changing it.

## 7. CI (L1)

A new `mobile` job in `.github/workflows/ci.yml`, `runs-on: windows-latest`:
`dotnet workload install maui` (GitHub runners do **not** ship MAUI workloads — minutes per run, the
known price of the job), then restore + build the **head csproj directly** with
**`-f net10.0-windows10.0.19041.0`** — never solution-level `-f` (it would force the TFM onto the
plain-`net10.0` Logic/Tests members). **Revised 2026-10-09 (spike finding, parent-adjudicated option
B):** the android TFM cannot be L1's assertion — the transitive `Microsoft.AspNetCore.App`
framework reference blocks every device TFM (§3.2). L1 asserts the Windows TFM; the android
assertion moves to L2, where severance is a proven prerequisite. The four existing ubuntu jobs are
untouched. The mobile **unit** tests run in the existing ubuntu job via the root solution (§4's
membership rule) — `SchoolCollab.Mobile.Logic` and `SchoolCollab.Mobile.Tests.Unit` are `net10.0`.

## 8. Acceptance criteria

**L1 — host + spike + CI** *(amended 2026-10-09, worker intercom / option B)*
1. `dotnet build SchoolCollab.slnx` at the repo root: 0 errors, with the **head absent** from the
   root solution and `Mobile.Logic` + `Mobile.Tests.Unit` present and green on ubuntu (the §4
   membership rule, reversing this criterion's earlier wording).
2. `dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj -f net10.0-windows10.0.19041.0`:
   0 errors. The android TFM is L2's criterion — §3.2's spike outcome; asserting it in L1 would
   require the rejected shim.
3. A real component from `Admin.Shared` (e.g. `FieldDisplay`) renders in the head on the Windows
   target — the reuse spike; a green build alone does not satisfy this.
4. The new `mobile` CI job exists on `windows-latest` (workload install, head-csproj
   `-f net10.0-windows10.0.19041.0` build) and the four existing job names are unchanged.
   **It is currently SKIPPED** (owner, 2026-10-10): the job is gated on the repository variable
   `ENABLE_MAUI_CI`, which is unset, because it costs minutes per run while the mobile work it
   gates is still in progress. It still *exists* — re-enable with
   `gh variable set ENABLE_MAUI_CI --body true`, or delete the gate in `.github/workflows/ci.yml`
   once the mobile work stabilises.
5. Existing ubuntu jobs unaffected; `CrossModuleWiringTests` + `SchoolCollab.ArchitectureTests.Unit`
   green, including the extended any-slnx registration guard and its negative discriminator.

**L2 — severance (the spike's proven prerequisite)**
1. `dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj -f net10.0-android`:
   **0 errors** — the headline criterion; the §3.2 root cause is gone: no transitive
   `Microsoft.AspNetCore.App` framework reference reaches the head.
2. `Admin.Shared`'s unused `ServiceDefaults` ProjectReference is dropped; the four web hosts and the
   root solution build/test green.
3. The `Core.Features` abstractions (`IFeatureFlagService`, `IFeatureFlagChangeNotifier`,
   `FeatureFlagKeys`) move to a mobile-safe home; `GateBase`/`TenantGate`/`FeatureFlagGate` compile
   and the gates' tests stay green.
4. The mobile CI job's assertion flips to the android TFM.

**L3 — credential + registration path**
1. The realm JSON contains the new public client with all five claim mappers plus the audience
   mapper; `AppHostRealmImportArchitectureTests` still passes.
2. PKCE acquisition returns a token whose claims carry `tenant_id` / `teacher_id` / `roles`
   (assert against a decoded token, no live Keycloak in unit tests).
3. One authenticated call reaches an API from the device/emulator and returns 200, proved by a
   server-side log line — not by UI appearance.
4. **No** C# literal under `src/` matches the base-address regex, and the diff review greps
   `src/Mobile` for `http(s)?://` literals (§6: the guard's `excludedHosts` means CI cannot catch
   them).
5. `AuthenticationStateProvider` yields an authenticated principal and `TenantGate` renders its
   tenant branch instead of the fallback.

**L4 — first vertical slice**
1. The slice's screens are mobile-first at ≤ 400 dp and widened for tablet, using the repo's
   CSS-isolation conventions (`.razor.css` per component; no inline `style`).
2. `maui-safe-area` handling verified on both form factors (insets + keyboard).
3. Unit tests cover the slice's view models in `SchoolCollab.Mobile.Tests.Unit` (runs on ubuntu).
4. The slice's own severance-or-copy decision for `Assignments.Application` →
   `Students.Application` → `Students.Core` (OD1's second-pass addition) is planned in its own
   round.

## 9. Open decisions for the plan stage

Genuine forks the spec does not determine. The L1 plan declares these (grill format) rather than
deciding them silently.

**OD0 — How the §3.1 dependency blocker is resolved, and what that does to the train's shape.**
Options: **(a)** extract first — move the UI-facing types out of `Students.Core`/`Core` into a
mobile-safe assembly, re-point `Admin.Shared`, verify the four web hosts still build, *then* build the
host (highest confidence, touches the shared kernel → human-gated); **(b)** spike first — add the head,
attempt one `Admin.Shared` reference under `net10.0-android`, measure exactly what breaks, and emit the
extraction inventory, then extract with facts in hand (cheapest information, some throwaway);
**(c)** do not reuse the components — keep FluentUI Blazor as the design language and write the mobile
UI fresh (walks back D1/D2, but honest if the severance cost rivals a fresh UI); **(d)** reuse only the
component subset already free of the closure, deferring severance indefinitely (cheap, narrows reuse).
*This subsumes the earlier OD1 (which vertical slice) until it is settled, since the slice's shape
depends on which components are reachable.*

**OD1 — Which vertical slice L3 delivers.** Recommend **assignments list → detail** (teacher-facing):
it exercises the richest reusable layer (44 dialogs, the authoring sections) and the most
representative API surface. Cost: the assignment pages are the largest of the 37; a narrower
alternative is student detail. *Note: the audience question from round 2 was never answered
explicitly — this is where it lands.* **Second-pass addition:** the assignments components live in
`Assignments.Application`, whose `Students.Application` reference re-imports the `Students.Core`
closure even after L2 — so L4 opens with its own severance-or-local-copy decision, planned then,
not now.

**OD2 — Token refresh policy.** Recommend **silent refresh via the refresh token, falling back to
interactive `WebAuthenticator` re-auth when Keycloak rejects it**, with the refresh token in
`SecureStorage`. Cost: silent-refresh failures must be indistinguishable from session end to the
user (the auth service already models `session_ended` as a distinct 410 — mobile should map the
same way).

**OD3 — How much of the Logic split is enforced.** Recommend **route all view models and API
clients through `SchoolCollab.Mobile.Logic`** so they are CI-testable, leaving only host wiring and
layout in the head. Cost: slightly more ceremony per screen; the alternative (logic in the head) is
invisible to CI.

**OD4 — Dev-loop base URL.** Recommend **a `appsettings.Development.json` value pointing at the
AppHost's published API endpoints** (Android emulator via `10.0.2.2`, physical device via LAN),
never a literal. Cost: the dev cert must be trusted on the device or the endpoints used over http.

## 10. Follow-ups (not this train)

- Offline capability and push, if the app later needs them (would reopen D1).
- Whether the mobile head ever becomes an AppHost resource (`Aspire.Hosting.Maui` is preview-only
  at this SDK line).
- Extending `CrossModuleWiringTests` to cover a mobile host's wiring, or recording the mobile head
  as permanently outside its model.
- Carried over: `use-js-interop`'s upstream sample reverted verbatim;
  `%TEMP%\hermes-blazor-quarantine\` still holds nine superseded skill copies.
