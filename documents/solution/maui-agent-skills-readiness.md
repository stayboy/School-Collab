# MAUI readiness — skills, and what the AppHost can and cannot do for Blazor Hybrid

**Status:** findings only — no MAUI code exists; implementation section intentionally empty
**Scope:** agent tooling (skills) + an AppHost/architecture readiness assessment for planned MAUI work
**Related:** `documents/solution/blazor-agent-skills-adoption.md` (the `dotnet-blazor` adoption),
`documents/solution/aspire-13-5-upgrade.md`, `documents/solution/adr-cross-module-calls.md`,
`documents/solution/cross-module-http-client-pattern.md`

## 1. Decisions (settled 2026-10-09)

1. **App shape:** Blazor Hybrid — a MAUI shell hosting this repo's existing Razor components. Settled
   2026-10-09 on the measured reuse in §5.1, with no device-native features planned (no offline store,
   no push/camera requirement). **Form factor (settled 2026-10-09): both phone and tablet from one
   codebase**, via adaptive layouts. Consequence: the 37 `@page` screens are **reworked, not shipped** —
   the 94 components are the durable asset and the pages are reference material for what each screen
   already solves (state handling, validation, API calls).
2. **Placement:** a **separate solution** (e.g. `src/Mobile/SchoolCollab.Mobile.slnx`) referencing the
   RCLs, with its own CI job — never in `SchoolCollab.slnx`, and never a second `.slnx` at the repo
   root (two solution files in one directory is an error here).
3. **Skills:** all 8 `dotnet/skills` → `dotnet-maui` skills adopted, installer-managed.
4. **Record:** this document.

> **Decision 1 was challenged and is now settled as Hybrid (2026-10-09).** It was briefly marked
> under review when the module RCLs looked like desktop-only pages. Measured properly (§5.1) the
> reuse is **94 components / ~28k lines**, and the adaptation cost is **~200 lines** of client-side
> registration — so the earlier "a half-dozen primitives" reading was wrong and the Hybrid case is
> stronger than that review implied. The credential estate does **not** support the choice either way
> (§4.2); it is a constant. What decides it is UI reuse plus the absence of device-native requirements.

## 2. Adopted skills

`dotnet/skills` → `plugins/dotnet-maui`, **v0.1.16, MIT** — skills-only (all three plugin manifests
declare only `./skills/`; no MCP servers, no agents). Installed 2026-10-09 with

```bash
npx --yes skills add dotnet/skills -g -y -s dotnet-maui-doctor -s maui-app-lifecycle \
  -s maui-collectionview -s maui-data-binding -s maui-dependency-injection \
  -s maui-safe-area -s maui-shell-navigation -s maui-theming
```

Canonical copies `~/.agents/skills/<name>/`, lock entries `source: dotnet/skills`, refresh with
`npx --yes skills update <name> -g -y`. (`add` reports a spurious `"status":"failed"` /
*PromptScript does not support global skill installation* on success — verify the filesystem + lock.)

| Skill | SKILL.md | Refs | Covers |
|---|---|---|---|
| `dotnet-maui-doctor` | 9.5 KB | 11 | Environment: SDK, workloads, JDK, Android SDK, Xcode, WinSDK — requirements read from NuGet `WorkloadDependencies.json`, never hardcoded |
| `maui-collectionview` | 20.2 KB | 1 | List/grid layouts, selection, grouping, incremental loading, SwipeView, ScrollTo, ListView migration |
| `maui-theming` | 16.4 KB | 1 | `AppThemeBinding`, `ResourceDictionary` themes, `DynamicResource`, runtime switching, Android `ConfigChanges.UiMode` |
| `maui-shell-navigation` | 15.3 KB | 1 | Shell hierarchy, `GoToAsync`, routes, `IQueryAttributable`, tabs/flyout, navigation guards |
| `maui-data-binding` | 14.8 KB | 1 | Compiled bindings / `x:DataType`, `ObservableObject`, converters, `XC0022`/`XC0025` as errors |
| `maui-dependency-injection` | 14.4 KB | 1 | `MauiProgram` registration, lifetimes, Shell auto-resolution, DI pitfalls |
| `maui-safe-area` | 12.9 KB | 1 | .NET 10 `SafeAreaEdges`/`SafeAreaRegions`, keyboard avoidance, **Blazor Hybrid CSS safe areas** |
| `maui-app-lifecycle` | 9.2 KB | 1 | App states, `Window` lifecycle events, `ConfigureLifecycleEvents`, platform mapping |

**Verification** (pinned clone `main`@`3d38ac34`, every file byte-compared and every lock
`skillFolderHash` checked against `git rev-parse HEAD:<skill-dir>`): **8/8 PASS**. Lock 58 → 66.

**Local environment:** `dotnet workload list` already reports `android 36.1.69`,
`ios 26.5.10301`, `maccatalyst 26.5.10301`, `maui-windows 10.0.20` — so `dotnet-maui-doctor` has
little to fix on this machine; its value is CI runners and other machines.

> **Naming hazard:** in this repo "hybrid" already means *hybrid tenancy* — `IHybridTenantEntity`,
> `HybridQueryFilterTests`, `docs`/specs on the global-value → tenant-override flow. Say
> **"Blazor Hybrid"** in full, or the term will be read as tenancy.

## 3. How the AppHost is used today

`src/AppHost/SchoolCollab.AppHost/Program.cs` — **598 lines**, `Aspire.AppHost.Sdk/13.5.4`, a single
composition root; `Aspire.Hosting.{PostgreSQL,Python,RabbitMQ,Redis}` all pinned `13.5.4` in
`Directory.Packages.props`.

**Resources:** `postgres` (+ `settings-db`, `assignments-db`, `students-db`), `rabbitmq`, `redis`,
`keycloak` (container `quay.io/keycloak/keycloak:26.4.7`, committed realm bind-mounted, health-checked
on `management:9000`), `mailpit`, `migrator`, `settings-api`, `settings-ai`, `students-api`,
`assignments-api`, `assignments-worker`, `students-worker`, `auth`, `portals` + `auth-portal`
(uvicorn), `admin`, `families`.

**Three load-bearing patterns:**

1. **Deployment-time defaults live in `Parameters:` and fan out through `.WithEnvironment(...)`** —
   ~30 `AddParameter(...)` declarations and **62** `.WithEnvironment(...)` call sites. This is the
   one-write-point rule of `documents/specs/startup-flag-governance.md`; startup switches such as
   `FeatureFlags__FEATURE__DisableOIDCAuth` and `...DisableKeycloakLoginUi` are injected this way
   (e.g. lines 281, 324, 351, 429, 434, 558, 561, 593).
2. **`.WithReference(...)` (46 sites) is service discovery** — every cross-module base-address
   literal must have a matching `WithReference` on the *calling* host, and
   `tests/SchoolCollab.Core.Tests.Unit/Architecture/CrossModuleWiringTests.cs` **parses this
   `Program.cs` as text** to enforce it (its host model is derived from `AddProject<...>`).
3. **The AppHost file is itself policed.** `AppHostEndpointPortingArchitectureTests` pins the
   endpoint-declaration count at exactly **5** (`ExpectedEndpointDeclarationCount = 5`), allows an
   equal `port`/`targetPort` pin only on a container, and pins `auth-portal` at host `port: 5700`
   with no `targetPort`. Siblings: `AppHostLoginUiFlagWiringArchitectureTests`,
   `AppHostStartupFlagWiringArchitectureTests`, `AppHostRealmImportArchitectureTests`,
   `AppHostOutboxExchangeWiringArchitectureTests`, `AppHostSettingsDbWiringArchitectureTests`,
   `PortalSessionAdoptionArchitectureTests`.

**Blazor hosts:** `admin` (`AddProject<Projects.SchoolCollab_Admin>`, lines 547–574), `families`
(581–595), `auth` (423–434). Admin and Families are **Blazor Server** —
`.AddInteractiveServerComponents()` + `.AddInteractiveServerRenderMode()`, with
`App.razor` rendering `<Routes @rendermode="new InteractiveServerRenderMode(prerender: false)" />`
over `blazor.web.js`. UI lives in four `Microsoft.NET.Sdk.Razor` RCLs at `net10.0`:
`SchoolCollab.Admin.Shared` and `{Assignments,Settings,Students}.Application`.

**Identity, as configured today:**

- Keycloak client `school-collab-client` is **confidential** (`publicClient: false`). Its
  `redirectUris` are exclusively `localhost:5300 / 5400 / 55458` + `/signin-oidc` and
  `/signout-callback-oidc`, and `webOrigins` are localhost-only
  (`src/AppHost/SchoolCollab.AppHost/school-collab-realm.json`).
- Auth is **OIDC + cookie**, server-side (`app.UseAuthentication()` / `UseAuthorization()`), plus the
  D6 portal handshake: `PortalHandshakeExtensions` posts a one-time code to `POST /auth/redeem` and
  receives **claims as data — never a token**.
- Tenant reaches APIs in one of two ways: as **token claims** in production OIDC; or, in
  dev/TestAuth mode, as an `x-tenant-id` header stamped by `TenantPropagationDelegatingHandler` from
  the Redis-backed `IDevTenantSelection` (`src/SchoolCollab.Core/Auth/DevTenantSelection.cs`). The
  handler's own docs pin the boundary: the header is honoured only by `TestAuthHandler` and
  "cannot be spoofed in production OIDC where `TestAuthHandler` is not registered".
- The APIs' other accepted credential is the **portal session** — `PortalSessionClaimsReader`
  resolves it by a server-to-server `GET /auth/session/{id}/claims` through the cross-module client,
  failing closed on any transport, status or shape failure.

Nothing calls `WithExternalHttpEndpoints()`; the web UIs are reached through Aspire's localhost proxy.

### 4.1 What the APIs already accept (checked 2026-10-09)

This corrects an earlier draft of §4 that claimed no bearer path exists. **It does.**

| Credential | Where it works today | Evidence |
|---|---|---|
| **Keycloak access token** as `Authorization: Bearer …` — **live on every API host** | JwtBearer with `Authority = Auth:Keycloak:Authority` and `Audience = Auth:Keycloak:ClientId`; the realm's `oidc-audience-mapper` emits `aud=<clientId>` on the access token, and `RequireHttpsMetadata=false` is dev-only | `.AddJwtBearer(...)` in `AuthTenancyExtensions.AddAuthAndTenancy` (registered only in the OIDC-enabled branch); called by `Assignments.Api`, `Students.Api`, `Settings.Api`, `Auth`, `Admin`, `Families` |
| **OIDC cookie** (browser) | The Blazor Server hosts | `AddCookie()` + `AddOpenIdConnect(...)`, `DefaultScheme = Cookie` |
| **Opaque portal session id** in the **`X-Portal-Session`** header | **Assignments API only** — scheme `PortalSession`, gateway `PortalSessionGateway`, fallback pinned to `Bearer` | `PortalSessionAuthenticationHandler`; `AddPortalSessionAuthentication(AuthTenancyExtensions.BearerScheme)` |
| **TestAuth** (dev bypass) | Every host when `FEATURE:DisableOIDCAuth=true` | `AddAuthAndTenancy`'s dev branch → `TestAuthHandler` |
| **Data-protected scoped token** (precedent, not general auth) | Guardian deep links into Families, validated by an endpoint filter | `DeepLinkProtector` / `DeepLinkTokenPayload` / `DeepLinkTokenMinter` / `GuardianTokenEndpointFilter` |

Consequences worth stating plainly:

- A mobile client presenting a **Keycloak access token** is authenticated by the existing `Bearer` scheme on **all three APIs with no API change**. The only realm-side requirement is that the token's `aud` be one the APIs accept — today exactly `Auth:Keycloak:ClientId` (`school-collab-client`, the AppHost parameter `keycloak-client-id`) — so a *new* public client must carry a mapper emitting that audience (or the configured audience must be widened).
- A new client also needs the pinned claim mappers (`tenant-id/name/type`, `teacher-id`, `roles` → `tenant_id`/…), because `PortalClaims`' constants **are** the realm-mapper contract (D9): change one without the other and the tenant/role claims vanish.
- The **portal-session** scheme is a per-host registration, present on Assignments only. Reusing it from mobile would mean registering it on the other APIs *and* finding a way for a device to obtain a session id — the existing minting paths are the server-to-server Direct Grant (`/auth/exchange`, which would put user credentials on the device) or browser-mediated redemptions. That is the least attractive of the options.
- `AppCallbackAllowlist` is a fail-closed exact-prefix matcher over `Auth:AppCallbackPrefixes`: any new browser-mediated handshake redirect (including a mobile one) needs an allowlist entry, and an empty configured value is a startup failure by design.

### 4.2 The credential estate is neutral between Hybrid and native (added 2026-10-09)

A natural assumption is that Blazor Hybrid inherits the web auth story. It does not. Both Hybrid and
native XAML must, item for item, do the same credential work: acquire a Keycloak token as a **public**
client (`WebAuthenticator` + PKCE), store it (`SecureStorage`), refresh it, attach it with a
`DelegatingHandler`, and address the APIs by **absolute** base URL. Hybrid carries one item native does
not, and it is specific to the shared UI:

> `SchoolCollab.Admin.Shared/Components/Gate/GateBase.razor` injects `AuthenticationStateProvider`,
> subscribes to `AuthenticationStateChanged`, and `Components/TenantGate.cs` carries `[Authorize]`.
> **No client-side provider exists anywhere in `src/`** — no `class … : AuthenticationStateProvider`,
> and no `AddCascadingAuthenticationState` / `AddAuthenticationStateSerialization` registration. The
> only implementations in the tree are bUnit fakes in `tests/`. Today the framework supplies it from
> the OIDC cookie principal; a Hybrid host must build the bridge from the token's claims.

The parts of the current estate that *are* directly reusable — the OIDC cookie, the `X-Portal-Session`
scheme, and the framework-supplied auth state the gates read — are reusable **only by a browser**:
that is, by the thin WebView wrapper over the server-hosted app, which was rejected on capability
grounds (no offline, no native UI, and not Blazor Hybrid in the agreed sense). Anything running the UI
in-process, Hybrid included, loses all three.

Hybrid does reuse the credential **consumers** — the typed clients (`@inject CodedValuesApiClient`,
`IContactsClient`, …) and the loading/error UI around them keep their call shapes when the same
interfaces are re-registered over a bearer handler. That is real but modest, and it is not a
*credential* benefit. **Do not cite the credential estate as a reason to prefer Hybrid.**

**The bridge is mandatory under decision 1, so the gates follow automatically (2026-10-09).** This is
worth stating because it removes a decision rather than adding one: `Admin.Shared` *is* the design
system being reused, `Components/Gate/GateBase.razor` lives in it, and a `BlazorWebView` supplies no
`AuthenticationStateProvider` of its own while `AuthorizeView` / `[Authorize]` (which
`Components/TenantGate.cs` uses) cannot work without one. So the client-side provider must be built
for the shared design system to function **at all** — not merely to make the gates available. Once it
exists, `TenantGate` and `FeatureFlagGate` work as designed, reading `tenant_id`/`roles` from the
token's claims (the same claim spelling the realm mappers already emit). Server-side enforcement of
tenancy and feature flags is unaffected either way, so the gates remain UI visibility only.

## 4. Does the AppHost support Blazor Hybrid? — not today

Five gaps, each with its evidence. None is in the AppHost's *shape*; four are elsewhere and will
bite later if only the AppHost is considered.

| # | Gap | Evidence |
|---|---|---|
| **G1** | **No MAUI orchestration.** `Aspire.Hosting.Maui` exists but is **preview-only**: `13.5.3-preview.1.26425.3`, `13.5.4-preview.1.26464.4` (the line matching this repo's SDK), `13.6.0/13.6.1-preview…`. Not in `Directory.Packages.props`. | NuGet flat-container index for `aspire.hosting.maui`; `Directory.Packages.props` |
| **G2** | **The identity model does not carry over — but the API side already does.** A MAUI app is a *public* client: no shared cookie, no client secret, so the browser cookie path is unavailable. What is missing is realm-side: a **public client**, a **custom-scheme (or loopback) redirect**, and mapper parity so its token carries the pinned claims and an `aud` the APIs accept (see §4.1). **No API-side auth work is needed.** | `school-collab-realm.json`; `AuthTenancyExtensions.cs`; Admin `Program.cs` |
| **G3** | **Tenant propagation is a dev-only path.** On a device the dev loop needs `x-tenant-id`, which only `TestAuthHandler` honours; production token claims are the real mechanism. Anything reading the tenant from `IHttpContextAccessor` has no equivalent off-server. | `TenantPropagationDelegatingHandler.cs` remarks; `DevTenantSelection.cs` (Redis) |
| **G4** | **Device networking + dev certs.** `https+http://<resource>` discovery is host-side only. A device needs a concrete base URL (Android emulator → host loopback via `10.0.2.2`; a physical device → LAN address or a dev tunnel) and must trust the ASP.NET dev cert, which emulators do not by default. No endpoint here is published externally. | `Program.cs` (0 × `WithExternalHttpEndpoints`); `CrossModuleWiringTests` base-address literals |
| **G5** | **The guards would stop covering the mobile head.** `CrossModuleWiringTests` derives "hosted projects" from `AddProject<...>` in this `Program.cs`; a mobile head living in its own solution (decision 2) is invisible to it — the guard goes quietly narrower rather than red. Adding a MAUI resource with endpoints also requires deliberately bumping the count pin of 5. | `CrossModuleWiringTests.cs`; `AppHostEndpointPortingArchitectureTests.cs` |

## 5. Revision to the "RCLs are reusable" premise

The four RCLs are `net10.0` and a `net10.0-android` head **can** consume them — that part holds.
What does **not** transfer is their **service registration**. Each `*.Application` module's
`ModuleServices.cs` registers cross-module clients that assume a *server process*: discovery-style
base addresses, the Redis-backed `IDevTenantSelection` tenant handler, and (in Assignments)
`PortalSessionClaimsReader`'s server-to-server claim read.

So the honest split: **the UI is reusable and the registration is bounded.** A Hybrid head needs a
client-side registration path — absolute base URLs from configuration plus a bearer-token delegating
handler — replacing **22 registrations across ~204 lines** of `ModuleServices.cs` (Settings 12,
Assignments 7, Students 3). §4.1 removes the API-side half of that cost (the bearer scheme and its
audience validation already exist). What remains genuinely new is the client-side auth bridge in
§4.2, and only if the gate-bearing components are reused. "Reuse is cheap" is the right conclusion
once the numbers are in; the earlier framing of this paragraph (reuse not cheap, registration the
largest cost) was written before they were.

### 5.1 Measured reuse (2026-10-09)

Non-page components — the layer a mobile app can adopt — versus `@page` screens, which are
admin-shaped. Lines include each component's `.razor.cs`.

| RCL | Reusable components | Lines | `@page` screens | Lines |
|---|---:|---:|---:|---:|
| `SchoolCollab.Admin.Shared` (design system) | 24 | 4,659 | 1 | 36 |
| `SchoolCollab.Students.Application` | 47 | 15,680 | 18 | 7,292 |
| `SchoolCollab.Assignments.Application` | 16 | 6,419 | 5 | 1,531 |
| `SchoolCollab.Settings.Application` | 7 | 1,328 | 13 | 3,342 |
| **Total** | **94** | **28,086** | **37** | **12,201** |

Composition of the 94: 44 dialogs, 20 section/editor, 9 field/form, 6 grid/list, 15 other — i.e. the
reusable layer is predominantly dialog and section surfaces, which is exactly what a task-oriented
mobile app wants.

Two corrections this table forces on the earlier review:

- "A half-dozen primitives travel well" was **wrong**; it is 94 components and ~28k lines.
- The 37 `@page` screens are not *technically* barred from Hybrid — a `BlazorWebView` supports the
  `Router`, so `@page` components render. They are barred only by *UX shape*: dense desktop screens.
  Shipping them is a legitimate choice for an internal, tablet-first tool (≈40k lines addressable);
  not shipping them keeps ~28k.

`SchoolCollab.Families` remains the exception: it references **no** RCL (9 `.razor`, 5 `@page`, own
UI, own Redis/DataProtection stack), so a guardian-facing app would inherit only FluentUI itself.

## 6. The CI constraint (from the adoption round)

`.github/workflows/ci.yml` is the only workflow: **`ubuntu-latest`**, `dotnet 10.x`, solution-wide
`dotnet build` / `dotnet test`, relying on no-arg single-solution discovery. A project targeting
`net10.0-ios` / `net10.0-maccatalyst` cannot build on Linux, so a MAUI head must not enter
`SchoolCollab.slnx` — hence decision 2 (separate solution, own job), which must also live in a
subfolder to avoid a second root-level solution file.

## 7. Open items

- **Mobile credential — settled by derivation, not by preference:** Hybrid has no cookie, so the
  credential is **(a) a Keycloak access token presented as `Authorization: Bearer`**, via a **new
  public client** with PKCE (`WebAuthenticator`) in the system browser. Realm-side work: the client,
  mapper parity for the pinned `tenant_id`/`teacher_id`/`roles` claims, an audience mapper emitting
  the `aud` the APIs validate, and the custom-scheme redirect. API-side work: none. Remaining
  sub-decision: token lifetime/refresh policy and where the refresh token lives on the device.
- **Client-side registration path (from §5):** the Hybrid head re-registers the module clients with
  absolute base URLs + a bearer `DelegatingHandler`, replacing 22 registrations / ~204 lines of
  `ModuleServices.cs`. The interface shapes and the components consuming them do not change.
- **Adaptive layout:** phone and tablet from one codebase, so screen layouts are built mobile-first
  and widened, per the repo's CSS-isolation conventions (`.github/copilot/rules/blazor-components.md`)
  and with `maui-safe-area` for insets/keyboard. The 37 existing pages are the reference for behaviour,
  not markup to port.
- **Placement + CI:** a `src/Mobile/` solution (never `SchoolCollab.slnx`, never a second `.slnx` at the
  repo root) with its own `windows-latest` job, so the ubuntu build stays green.
- **Execution mode for implementation — not yet chosen.** AGENTS.md requires an explicit choice
  (Solo / Light round / Full round) before implementation begins; nothing below that line has started.
- **Carried over:** `use-js-interop`'s upstream sample reverted verbatim, and
  `%TEMP%\hermes-blazor-quarantine\` still holds nine superseded skill copies.
- **AppHost orchestration vs. out-of-band run.** Adopt the preview `Aspire.Hosting.Maui` (plus a CPM
  entry and the endpoint-count bump), or run the mobile head outside Aspire. Not urgent: both work.
- **Repo-owned mobile conventions** (tenant, auth, absolute base URLs, CPM) — a rule or skill once
  §7's first item is settled.
- **Extend or exclude the wiring guards** for the mobile head (G5).
- **Carried over from the Blazor round:** `use-js-interop`'s upstream sample came back verbatim,
  and `%TEMP%\hermes-blazor-quarantine\` still holds the nine superseded copies.
