# Round: mobile-credential (L3 of the mobile train — credential + client-side registration path)

**Provider:** pi, full Tier 3 (auth-adjacent) — models: glm-5.3-flash orchestrator + accept, kimi-k2.7-code plan-gate and diff-review (the canonical ollama/glm-5.3:cloud gate id is unavailable; a glm-family substitute would put the gate on the plan author's own id, which round-runner-model-policy.md item 3 forbids), deepseek-v4.1-flash worker, kimi-k2.7-code escalator, minimax-m3 UI tester
**Status:** CLOSED (2026-10-13) — plan gate REVISE → ACCEPT (rev 2); worker run (deepseek-v4.1-flash), three-slice diff review (kimi-k2.7-code), rework passes R1–R15 all complete, and the **UI-tester stage complete**: run 1 (minimax-m3, static adversarial pass — 3 P1 + 5 P2) → parent adjudication (P1-C **demoted** to P2) → rework R11–R15 → run 2 (re-verification only): **the delivered UI is acceptable as it stands** (`## UI Tester`). **Acceptance written on the parent's authoritative numbers, with §8 L3 criterion 3 (= L3.3) carried OPEN — owner-run pending by owner pin 2** — it is never marked met. `Mobile.Tests.Unit` is now **83/0**, and the ledger row records **four bounded rework passes** (`round-runner-model-ledger.md`). Details: `## Acceptance`.
**Round base:** `6cb415ec` (full: `6cb415ec docs+test: hot-reload dev loop standard; live AI tests opt-in (#340)`) — branch `feat/mobile-credential` (created from `origin/main`), worktree `School-Collab-mobile-train`. The worktree was **clean at base** (verified 2026-10-12: `git status --short` empty, `git log --oneline -1` = `6cb415ec`).
**Spec:** `documents/specs/mobile-hybrid-app.md` — §2 pinned decisions (D5, D6, D8), §5 credential + registration path, §6 guard interactions, §8 L3 acceptance, §9 open decisions (OD2 carried here). Predecessor rounds: `documents/rounds/round-mobile-host-skeleton.md` (L1), `round-mobile-severance.md` (L2, severed the head's `Microsoft.AspNetCore.App` closure so device TFMs build).
**Spec-numbering note (record, do not renumber the spec here):** spec §5 is titled "Credential and registration path (L2)" while spec §8's acceptance list titles the same layer "L3 — credential + registration path". This round follows **§8's L3 framing**; the §5 heading's "(L2)" is a stale label to be corrected at spec-amendment time (a parent task at acceptance, not worker work).

---

## Execution log (filled — planning through accept, the UI-tester stage and the final documentation pass complete)

- **Plan written:** 2026-10-12, glm-5.3-flash, on base `6cb415ec`.
- **Plan-gate:** **REVISE** (2026-10-12, kimi-k2.7-code; plan rev 2 folds everything below)
  — two P1s, four P2s, all folded into this rev:
  - **Plan-gate re-read (rev 2):** **ACCEPT** (kimi-k2.7-code) — both P1s confirmed resolved,
    the four P2s confirmed folded. One residual P2 — a stale citation — surfaced in the re-read
    and is fixed in this doc: `Resolve`'s throw on an unknown name was cited as
    `MobileApiOptions.cs:81-89` but the throw is at **`:76-79`** (`:81-89` is `ToBaseAddress`);
    corrected at both occurrence sites (this log's P2 8-client mapping bullet and W2 item 9).
    The round doc is now ready for the worker.
  - **P1 intent-filter placement:** the original W3 item 5 said to add `<intent-filter>` to
    `AndroidManifest.xml` — but that manifest has **no `<activity>` element**
    (`AndroidManifest.xml:3` is an `<application …/>` with no activity children); `MainActivity`
    is generated from the `[Activity]` attribute (`MainActivity.cs:7-8`). An orphan
    `<intent-filter>` is invalid and would fail the android TFM. Fixed: attribute-based wiring
    with the scheme/host decision — see W3 item 5 (rev 2).
  - **P1 `MauiAppBuilder` has no `Environment`:** the original W3 item 4 said
    `builder.Environment.IsDevelopment()` — but the builder is a `MauiAppBuilder`
    (`MauiProgram.cs:18`), which has `Configuration`/`Services`/`Logging`, **no `Environment`**.
    Fixed: the `DOTNET_ENVIRONMENT` env-var check, inside `AddAppSettings` before the extra
    stream is added — see W3 item 4 (rev 2).
  - **P2 8-client mapping under-specified** → W2 item 9 now names the single resolvable string
    all eight clients bind to (`Resolve("SettingsApi")` — `Resolve` throws on any other name,
    `MobileApiOptions.cs:76-79`; citation corrected on the gate re-read's residual P2 — the
    throw is at `:76-79`, `:81-89` is `ToBaseAddress`).
  - **P2 test-inventory inconsistency** → W5 gains `MobileAuthOptionsTests` so W5 and the
    expected-file list agree (list now 33 entries incl. the conditional AndroidManifest
    `<queries>` line and the new `MainActivity.cs` entry from P1-1).
  - **P2 custom-scheme hijack posture** → PKCE + `state` named the deliberate mitigation (with a
    mandatory code comment) and the loopback alternative recorded as the preferred posture where
    practical — see Constraints and contingency 3.
  - **P2 anti-logging constraint** → new worker constraint: no auth-path type logs or embeds a
    token, refresh token, or `Authorization` header content in an exception message.
  - **Gate adjudications (recorded as gate-adjudicated — NOT owner pins, 2026-10-12):** all
    four open decisions were reviewed and agreed — **OD-1** refresh-on-demand with an explicit
    no-401-resend boundary (stays gate-adjudicated — the owner grill issued no pin on OD-1);
    **OD-2** criterion 3 recorded OPEN / owner-run pending; **OD-3** the real token path with
    `FEATURE:DisableOIDCAuth` **OFF** (flag posture documented, never committed as a default);
    **OD-4** the registration code in `Mobile.Logic` — adjudicated and closed in the plan.
  - **Owner grill folded (owner, 2026-10-13):** the grill's three answers are folded below as
    **owner pins** — see the "Owner pins from the owner grill" section in the plan: (1) the
    realm-guard widening APPROVED; (2) §8 L3 criterion 3 recorded OPEN — owner-run pending;
    (3) the dev-loop posture is the real token path with `FEATURE:DisableOIDCAuth` OFF for this
    round's evidence. The pins **match** the gate's own adjudications on OD-2/OD-3, so no
    further plan-review run was needed; OD-1 and OD-4 carry no owner pin and stay
    gate-adjudicated / closed as above. The superseded "owner grill pending" wording in the
    earlier log entry and the OD blocks has been replaced.
  - **Amendment note:** the gate confirms the round's finding that
    `AppHostRealmImportArchitectureTests`' exact-client-set assertion
    (`AppHostRealmImportArchitectureTests.cs:331-337`) must be amended is an **honest
    amendment** — the guard's negative halves (no `portal` substring, no `/bootstrap` suffix in
    any redirectUris, `:339-343`) survive the three-client bump intact. **Owner-approved
    (owner pin 1, 2026-10-13):** the widening is recorded as a deliberate, owner-approved
    widening of a security guard — reason: a device cannot hold a client secret, so a third
    **public** client is mandatory, and the guard pins the client set exactly — not silently
    absorbed.
- **Worker run:** complete (deepseek-v4.1-flash, 2026-10-13) — W1–W5 implemented, **plus a real Keycloak container run proving the credential end-to-end**: realm import clean, a full Authorization Code + PKCE (S256) sign-in accepted with `client_id=school-collab-mobile-client` / `redirect_uri=schoolcollab://signin-oidc`, the real token carrying `iss`, `aud="school-collab-client"`, `azp`, `tenant_id`, `tenant_name`, `tenant_type`, `teacher_id`, `roles` (after a runtime grant), `scope`, `exp−iat=300`, plus a refresh-token round trip with rotation. The worker also **found** the seeded `dev-teacher` held no `realmRoles` (so `roles` was absent for *both* clients) — fixed later as R10. Frozen patch: `diffs-mobile-credential.patch` (33 files, 5313 lines, 20 new — the round doc among them).
- **Diff review:** complete (kimi-k2.7-code) — the 4940-line whole-patch review was split into **three parallel slices** (A realm + guard + CPM + Logic: 2 P1 + 4 P2; B head + platform: 0 P1 + 3 P2; C tests + round doc: 0 P1 + 4 P2); parent adjudication (P1-A1 confirmed → R1; P1-A2 demoted to P2 → R2); rework passes R1–R6, R7–R8, R9–R10; static re-verify on the rework delta → **ACCEPT** (one residual P2: transport failures escaping the handler — closed by R9); final pass R10 closed the seeded-role gap. Full chain in `## Review`.
- **Acceptance:** written (orchestrator-accept, 2026-10-13) on the **parent's authoritative numbers** — `dotnet build` of the mobile csproj `-f net10.0-android` 0/0, `dotnet build SchoolCollab.slnx` 0 errors, all six suites green (Mobile.Tests.Unit 78/0, ArchitectureTests.Unit 111/0, Core.Tests.Unit 126/0, Admin.Tests.Unit 651/0, Students.Tests.Unit 624/0, Assignments.Tests.Unit 1118/0), the `src/Mobile` `http(s)://` C# grep clean. **L3.3 carried OPEN — owner-run pending (owner pin 2)**; the device procedure reference is W6/the `## Acceptance` boundary statement. Verdict **CLOSED** with L3.3 open and residuals accepted.
- **Ledger row:** added (`documents/solution/round-runner-model-ledger.md`, `mobile-credential` row, 2026-10-13). Policy decision 7 satisfied.
- **UI tester run 1** (minimax-m3, 2026-10-13, static adversarial pass over the dispatch scope — no
  device/emulator exists in this environment): verdict **FINDINGS — 3 P1 + 5 P2**. **P1-A** a
  transport failure during the **code exchange** (`OidcLoginService.ExchangeCodeAsync`) escaped the
  page's catch list into the page's `<ErrorBoundary>`, replacing the whole page — sign-in button
  included — the R9 normalisation having covered the *refresh* leg only; **P1-B** the boundary's
  `ErrorContent` hard-coded "The Admin.Shared component failed to render" while it also wraps the
  auth section; **P1-C** a user-cancelled sign-in rendered red as "Sign-in failed". P2s: no
  `aria-busy` (A), a dead `gap` rule (B), a theoretical init race (C), stale local claim fields
  after a session change (D), and the inherited `TenantGate` fallback banner naming a dev tenant
  switcher this head does not have (E). Out-of-round observations recorded as the parent's. Full
  chain in `## UI Tester`.
- **Parent adjudication (2026-10-13, the parent is the acceptance authority):** P1-A and P1-B
  **accepted** — same root cause, the exchange leg never got the normalisation the refresh leg got;
  **P1-C demoted to P2** by the parent (a real presentation wrong, nothing broken — demoted, not
  withdrawn by the tester); P2-A and P2-E accepted; P2-B and P2-C left with **no action**; P2-D
  recorded as a residual (no sign-out control exists yet — folds into acceptance residual 5).
- **UI rework (R11–R15):** R11 normalises the exchange leg's transport failures
  (`HttpRequestException`/`TimeoutException`/`IOException` plus a **guarded**
  `TaskCanceledException` so genuine caller cancellation still propagates) into the typed
  `SignInFailedException` the message bar already owns; R12 generalises the boundary text to the
  page; R13 renders a cancellation with a non-error intent and a matching title while every genuine
  failure stays red; R14 gives `<TenantGate>` an explicit head-side `<Fallback>`; R15 adds
  `aria-busy`. R11 carries **5 new test cases** (three transport shapes, one client-timeout, one
  caller-cancellation that must still propagate), discrimination proven by temporary probes then
  reverted. `Mobile.Tests.Unit` is now **83/83**.
- **UI tester run 2 (re-verification only):** every P1 **resolved** (P1-A by R11, P1-B by R12,
  P1-C correctly re-classed and fixed by R13), the two accepted P2s fixed (P2-A by R15, P2-E by
  R14), and **no new user-visible defect** on the in-scope surfaces — the button cannot stick
  disabled (`finally` covers every exit), the success path still refreshes the claims, genuine
  failures still render red, and the reuse section's boundary still catches a real render failure.
  Verdict: **the delivered UI is acceptable as it stands.** Detail in `## UI Tester`.
- **Parent's post-rework pass (authoritative numbers):** android build `-f net10.0-android`
  **0 errors / 0 warnings**; root `SchoolCollab.slnx` **0 errors**; `Mobile.Tests.Unit` **83/0**;
  `ArchitectureTests.Unit` **111/0**. Patch re-frozen: **34 files, 5839 lines, 20 new,
  self-reference 0**; `Directory.Packages.props` still a 16-line insertion diff. Recorded in
  `## Acceptance`.
- **Rework count:** the round's rework is now **four bounded passes** — R1–R6, R7–R8, R9–R10,
  R11–R15 — recorded in the ledger row.

---

## Plan

**Goal.** L3 of the mobile train: give the Blazor Hybrid head a real credential and a real
registration path. System-browser OIDC (Authorization Code + PKCE via `WebAuthenticator`) against a
**new public Keycloak client**; access/refresh tokens in `SecureStorage`; a bearer
`DelegatingHandler` on the mobile HTTP clients; the existing APIs validate the token unmodified; a
client-side `AuthenticationStateProvider` makes `TenantGate` render its tenant branch; the module
API clients the head can reference are registered client-side against configured base addresses
(`MobileApiOptions`, spec D8). **No cookie on the device** (owner pin; spec D5).

### Owner-pinned decisions (2026-10-12) — recorded as pins, NOT re-opened

1. **Tier: full Tier 3** (plan gate → worker → static diff review → orchestrator-accept; UI leg
   dispatched-or-waived per the round's UI verdict — this round's `## UI Tester` carries the
   default risk posture, the waiver decision is the gate's).
2. **Credential shape (spec §2 D5 / §5):** a **new public client** with PKCE in the system browser
   via `WebAuthenticator`; **no cookie** on the device; the confidential `school-collab-client`
   is not usable from a device (no client secret can be embedded).
3. **Client-side `AuthenticationStateProvider` is mandatory** (spec D6; readiness doc §4.2:
   `src/SchoolCollab.Admin.Shared/Components/Gate/GateBase.razor:71` injects `AuthenticationStateProvider`
   and `:95/:145` subscribe/unsubscribe `AuthenticationStateChanged` — a `BlazorWebView` supplies none).
4. **Tokens live in `SecureStorage`, never `sessionStorage`/`localStorage`** (the control accepted
   in the L2 readiness doc, `maui-agent-skills-readiness.md` §7 "Closed (2026-10-09)" bullet).
5. **Base addresses stay configuration** (spec D8): no `http(s)://` C# literal under `src/`. The
   guard cannot backstop this — `CrossModuleWiringTests` carries an `excludedHosts` set
   (`tests/SchoolCollab.Core.Tests.Unit/Architecture/CrossModuleWiringTests.cs:300-302`:
   `localhost`, `host.docker.internal`, `0.0.0.0`, `example.com`, `example.org`) whose matches are
   **silently skipped**, so a `http://localhost…` literal never reddens CI. Enforcement is this
   plan's constraint plus a mandatory diff-review grep of `src/Mobile` for `http(s)?://` literals.
6. **The mobile CI job stays gated** behind `ENABLE_MAUI_CI` (`.github/workflows/ci.yml:174`) —
   unchanged this round (owner pin; the round touches `ci.yml` **not at all**).

### Owner pins from the owner grill (owner, 2026-10-13) — owner-pinned, NOT gate-adjudicated

The parallel owner grill answered three items; each **matches** the gate's own adjudication, so
no further plan-review run was needed. Recorded as pins:

1. **The realm-guard widening is APPROVED (owner, 2026-10-13).** The mobile public client
   (`school-collab-mobile-client`) is to be named in `AppHostRealmImportArchitectureTests`'
   exact-client-set assertion
   (`RealmImportFile_DeclaresNoPortalOidcClient_AndNoPortalRedirectUri`,
   `AppHostRealmImportArchitectureTests.cs:331-337`), keeping the guard's negative halves intact
   (no `portal` substring, no `/bootstrap` suffix — `:339-343`). Recorded as a **deliberate,
   owner-approved widening of a security guard**. Reason: a device cannot hold a client secret,
   so a third **public** client is mandatory (spec D5), and the guard pins the client set
   exactly. W1 item 1 implements it as written.
2. **§8 L3 criterion 3 is recorded OPEN — owner-run pending.** The end-to-end authenticated call
   from a device/emulator returning 200 (proved by a server-side log line) is **not** satisfied
   by this round. The round delivers the realm (W1), the PKCE path (W2 item 6), the token store
   (W2 items 2/4), the bearer handler (W2 item 7), the auth-state provider (W2 item 8) and the
   ubuntu-runnable tests (W5), plus the written device procedure (W6). The criterion must never
   be silently marked met; `## Acceptance` carries it as an explicit open item with the
   follow-up reference (W6's written device procedure). — see OD-2.
3. **Dev-loop posture is the REAL TOKEN PATH**, `FEATURE:DisableOIDCAuth` **OFF**, for this
   round's evidence. The flag posture stays documented (the `10.0.2.2` emulator override and the
   Windows loopback fallback too) but must **never** be committed as a default. Reason: with the
   flag ON, everything the round proves would bypass the credential it exists to prove. — see
   OD-3.

### Measured facts this plan relies on (measured on base `6cb415ec`; each carries `file:line`)

- **Realm, current shape:** `src/AppHost/SchoolCollab.AppHost/school-collab-realm.json` — realm
  `school-collab` (`:2`), realm roles `user-admin`/`platform-admin`/`teacher`/`staff` (`:7-13`),
  exactly **2 clients**: `school-collab-client` (`:34`) — confidential (`publicClient:false :37`,
  `standardFlowEnabled:true :38`, `directAccessGrantsEnabled:true :39`, secret `:45`), redirect
  URIs `:41-53`, `webOrigins :57-60`, `attributes["post.logout.redirect.uris"] :55` — and the
  service-account client `school-collab-auth-admin` (`:142-150`). The first client carries **6
  protocol mappers**: `tenant-id :65-76`, `tenant-name :78-89`, `tenant-type :91-102`,
  `teacher-id :104-115`, `roles` (realm-role mapper) `:127-137`, and
  `audience-school-collab-client :117-123` (an `oidc-audience-mapper` with
  `included.client.audience: "school-collab-client"` `:121`). The dev user `dev-teacher` seeds
  `tenant_id 00000000-0000-0000-0000-000000000002` / `teacher_id …0003` attributes (`:166-171`) —
  the same fixed ids the MigrationService DevIdentitySeeder pins (`Program.cs:53` comment).
- **The realm guard:** `tests/SchoolCollab.ArchitectureTests.Unit/AppHostRealmImportArchitectureTests.cs`
  enforces: strict JSON, comments disallowed (`:29-43`); filename derived from the file's own
  `realm` property (`:47-55`) with `keycloakRealmPath` (`:56-64`), csproj copy item (`:65-73`) and
  bind-mount target (`:74-86`) agreement; top-level `roles.realm` with the two admin roles
  (`:87-104`); the service-account client block (`:105-139`); `school-collab-client`'s roles
  mapper on both token paths (`:140-162`); the exact five `claim.name` values of the
  ClaimSetFactory contract (`:163-195`); redirect URIs + webOrigins presence (`:197-219`); and —
  **load-bearing for this round** — an **exact-set clientId assertion**
  `RealmImportFile_DeclaresNoPortalOidcClient_AndNoPortalRedirectUri` (`:310`) whose
  `clientIds.Should().BeEquivalentTo(new[] { "school-collab-client", "school-collab-auth-admin" })`
  (`:331-337`) means **any third client fails THIS guard today**. Adding the mobile client
  therefore includes a deliberate, recorded amendment of that expected set plus new client-specific
  assertions (W1) — the same "bumping this is a deliberate act" discipline the endpoint-count
  guard pins (`AppHostEndpointPortingArchitectureTests.cs:37`).
- **API-side validation (what the audience mapper must emit, resolved — not guessed):**
  `src/SchoolCollab.Core/Auth/AuthTenancyExtensions.cs:170-189` —
  `.AddJwtBearer` with `options.Authority = keycloakAuthority` (`:172`), **`options.Audience =
  keycloakClientId`** (`:175`), `RoleClaimType = ClaimTypes.Role` (`:182`), dev-only
  `RequireHttpsMetadata = false` (`:187`); read from `Auth:Keycloak:Authority` /
  `Auth:Keycloak:ClientId` with defaults `"https://keycloak.local/realms/school-collab"` /
  **`"school-collab-client"`** (`:219-224`). The AppHost supplies the values: parameter
  `keycloak-client-id` defaults to `"school-collab-client"`
  (`src/AppHost/SchoolCollab.AppHost/Program.cs:66`, committed at
  `src/AppHost/SchoolCollab.AppHost/appsettings.json:31`) and is fanned to the four Auth:Keycloak
  hosts as `Auth__Keycloak__ClientId` (`Program.cs:112`, `:576`) with the realm-URL authority
  (`Program.cs:111`, `:575`). **Conclusion: the new client's audience mapper must emit
  `included.client.audience = "school-collab-client"` — the value the APIs' `AddJwtBearer`
  audience validation accepts — NOT the new client's own clientId.** The bearer path reads
  `roles` from the access token through the JwtBearer default inbound mapping
  (`AuthTenancyExtensions.cs:175-183` comments confirm; `:182` pins `RoleClaimType`), and
  `tenant_id/tenant_name/tenant_type/teacher_id` come through unmapped (no ClaimActions —
  `:176-183` note, P1-2).
- **Bearer endpoints accept this token today:** endpoint groups are already bearer-gated when
  `FEATURE:DisableOIDCAuth` is OFF — e.g.
  `src/Settings/SchoolCollab.Settings.Api/CodedValueEndpoints.cs:23-28`
  (`.RequireAuthenticatedUser().AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme)`),
  `src/Assignments/SchoolCollab.Assignments.Api/AssignmentEndpoints.cs:16-23`, `:34`, `:52`,
  `:66`. **API-side auth work needed: none** (readiness doc §4.1/G2 — "no API-side auth work",
  `maui-agent-skills-readiness.md:172`).
- **`FEATURE:DisableOIDCAuth` is an existing startup switch** fanned by the AppHost to all six
  `AddAuthAndTenancy` consumers (`src/AppHost/SchoolCollab.AppHost/Program.cs:285,328,355,433,562,597`)
  and consumed by the endpoint groups above; with it ON the bearer scheme + policy are not even
  registered (`AuthTenancyExtensions.cs:117-137` dev branch).
- **No production `AuthenticationStateProvider` exists** — the tree's ~40 hits are all test fakes
  (e.g. `tests/SchoolCollab.Admin.Tests.Unit/ActivityGroupsPageTests.cs`,
  `EnrollStudentDialogBunitTests.cs`, `FeatureFlagGateTests.cs`; readiness doc §4.2:
  `maui-agent-skills-readiness.md:151-156` counts zero real implementations and zero
  `CascadingAuthenticationState` registrations — re-measured today: `git grep "AuthorizeView\|CascadingAuthenticationState" -- src`
  = **0** hits in `src/`). L3 writes the first real one. There is also **no per-instance auth
  cascade anywhere** — the gates reach auth state exclusively via the `AuthenticationStateProvider`
  service, which L3 must therefore register in the head's container.
- **The gates' client-side claim contract:** `TenantGate` derives from `GateBase` and supplies
  `TenantSelectedCondition` (`src/SchoolCollab.Admin.Shared/Components/TenantGate.cs:33-35`),
  which calls `VisibleTenantService.GetScopeAsync()`; that service reads the
  **`tenant_id` claim** from `AuthenticationStateProvider.GetAuthenticationStateAsync()`
  (`src/SchoolCollab.Admin.Shared/Services/VisibleTenantService.cs:33-46` — `:39` is the exact
  `user.FindFirst("tenant_id")` read). **Conclusion: the new provider must expose the raw realm
  claim spellings (`tenant_id`, `tenant_name`, `roles`) on its principal, NOT the server-side
  mapped `ClaimTypes.*` spellings — and `NotifyAuthenticationStateChanged` must fire on session
  changes so `GateBase`'s subscription (`GateBase.razor:95`) re-evaluates.**
- **`MobileApiOptions` already binds the `MobileApi` section** and normalizes to absolute
  `Uri` + trailing slash (`src/Mobile/SchoolCollab.Mobile.Logic/MobileApiOptions.cs:31-35,71-100`:
  `SectionName :30`, `ApiNames :35-39`, `Resolve :81-86`, `ToBaseAddress :104-120`; a missing value
  degrades one API without failing the configuration, `:89-91`). The head registers it in
  `MauiProgram.cs` from an embedded `appsettings.json`
  (`src/Mobile/SchoolCollab.Mobile/MauiProgram.cs:13,27,60-63`; the embedded-resource item is
  `src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj:60`).
- **The head's committed base addresses already match the three API projects' standalone
  launch profiles:** `src/Mobile/SchoolCollab.Mobile/appsettings.json` binds
  `https://localhost:7450` (Students), `https://localhost:7460` (Settings),
  `https://localhost:7199` (Assignments) — the same `applicationUrl` ports as
  `src/Students/SchoolCollab.Students.Api/Properties/launchSettings.json:9`,
  `src/Settings/SchoolCollab.Settings.Api/Properties/launchSettings.json:9`,
  `src/Assignments/SchoolCollab.Assignments.Api/Properties/launchSettings.json:5`. (Under
  `aspire run` the APIs get Aspire-controlled ports; the committed values imply a standalone-APIs
  dev posture — recorded as the dev-loop posture decision, OD-3 below.)
- **The registration claim, corrected against the tree.** The spec says the client-side
  registration replaces "22 registrations / ~204 lines of `ModuleServices.cs`"
  (`documents/specs/mobile-hybrid-app.md:162`; readiness doc
  `maui-agent-skills-readiness.md:203` — "(Settings 12, Assignments 7, Students 3)"). Measured
  today: the three module `ModuleServices.cs` files are 34 + 109 + 58 = **201 lines**
  (`src/Students/SchoolCollab.Students.Application/ModuleServices.cs`,
  `src/Settings/SchoolCollab.Settings.Application/ModuleServices.cs`,
  `src/Assignments/SchoolCollab.Assignments.Application/ModuleServices.cs`), and the server-side
  composition point is `src/SchoolCollab.Admin/Program.cs:46-48`
  (`AddSettingsModule` / `AddAssignmentsModule` / `AddStudentsModule`). **But the mobile head can
  only reference the Admin.Shared client layer, not those modules:** after L2's severance the
  head's closure is exactly `head → Admin.Shared` + `head → Mobile.Logic`
  (`src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj:72-73`), and the module Application
  projects are NOT in it — `StudentsApiClient` lives in `Students.Core.Contracts` (severed), so a
  literal "replace all 22" is **unimplementable at L3**. The head-reachable client population is
  the **8 API clients of `src/SchoolCollab.Admin.Shared/Services/`**
  (`AssignmentAiPromptApiClient.cs`, `AssignmentPolicyApiClient.cs`, `CodedValuesApiClient.cs`,
  `ConfigFlagsApiClient.cs`, `EntityCodeRulesApiClient.cs`, `NotificationPolicyApiClient.cs`,
  `SignatureConsentTextApiClient.cs`, `TenantsApiClient.cs`) plus `VisibleTenantService` —
  exactly the set the server registers in
  `src/Settings/SchoolCollab.Settings.Application/ModuleServices.cs:49-119` against
  `settings-api`. L3 registers exactly that 8-client set client-side (W5); the Students/Assignments
  client registrations arrive with L4's slice (spec §8 L4 criterion 4 already plans the slice's
  own severance-or-copy decision).
- **The Logic split is already load-bearing and platform-free:**
  `src/Mobile/SchoolCollab.Mobile.Logic/SchoolCollab.Mobile.Logic.csproj:4,10-13` (`net10.0`,
  "everything CI has to see lives here and only host wiring stays in the head"). **Consequence
  for auth:** `WebAuthenticator`/`SecureStorage` are MAUI Essentials APIs (shipped with
  `Microsoft.Maui.Controls`, head csproj `:68`) unavailable to a plain-`net10.0` library — so the
  acquirer and the store must be **interfaces in `Mobile.Logic`, implemented in the head**, and
  all testable logic (PKCE + token exchange + refresh + claims decoding + the provider + the
  bearer handler) lives in `Mobile.Logic`, tested by
  `tests/SchoolCollab.Mobile.Tests.Unit/` (root-solution member, `SchoolCollab.slnx:119`).
  `MobileAuthenticationStateProvider` is hostable in `Mobile.Logic` because
  `Microsoft.AspNetCore.Components.Authorization` is a standalone package already centralised
  (`Directory.Packages.props:45`, resolved 10.0.8 in L2).
- **CI context (unchanged):** the `mobile` job is gated (`ci.yml:174`); the four ubuntu jobs build
  `Mobile.Logic` + `Mobile.Tests.Unit` via the root solution (`ci.yml` untouched; L2 round doc
  confirms both memberships, `SchoolCollab.slnx:59-64,119`).

### Work items

**W1 — the realm change: new public client `school-collab-mobile-client`.**

Add a third client to `src/AppHost/SchoolCollab.AppHost/school-collab-realm.json` (strict JSON —
the parser guard rejects comments, `AppHostRealmImportArchitectureTests.cs:29-43`). The full JSON
shape, modelled on the existing client (`:34-141`) with the public-client substitutions:

```json
{
  "clientId": "school-collab-mobile-client",
  "name": "School Collab Mobile",
  "enabled": true,
  "publicClient": true,
  "standardFlowEnabled": true,
  "directAccessGrantsEnabled": false,
  "protocol": "openid-connect",
  "redirectUris": [
    "schoolcollab://signin-oidc"
  ],
  "webOrigins": [],
  "protocolMappers": [
    { "tenant-id" … copied verbatim from school-collab-client:65-76, },
    { "tenant-name" … :78-89 verbatim },
    { "tenant-type" … :91-102 verbatim },
    { "teacher-id" … :104-115 verbatim },
    { "audience-school-collab-client" … :117-123 verbatim INCLUDING key + name —
      the copy carries the same name and the same included.client.audience },
    { "roles" … :127-137 verbatim (realm-role mapper, multivalued, access/id/userinfo token.claim) }
  ]
}
```

Concrete mapper JSON (the worker copies this block verbatim from the existing client — the values
below are the copy's content, each one sourced from the named line range; see the measured facts):

- 4 attribute mappers: `user.attribute == claim.name` per pair (`tenant_id`, `tenant_name`,
  `tenant_type`, `teacher_id`), `access.token.claim=true`, `id.token.claim=true`,
  `userinfo.token.claim=true`, `jsonType.label="String"` (`school-collab-realm.json:65-115`).
- Audience mapper (`school-collab-realm.json:117-123` shape): name
  `audience-school-collab-client`, `protocolMapper: "oidc-audience-mapper"`,
  `included.client.audience: "school-collab-client"`, `access.token.claim: true`,
  `id.token.claim: true`.
- Roles mapper (`:127-137` shape): `protocolMapper: "oidc-usermodel-realm-role-mapper"`,
  `claim.name: "roles"`, `jsonType.label: "String"`, `access/id/userinfo.token.claim: true`,
  `multivalued: true`.

**Why the audience mapper emits `school-collab-client` (not the new client's id):** the APIs'
`AddJwtBearer` validates `options.Audience = Auth:Keycloak:ClientId`
(`AuthTenancyExtensions.cs:175`), whose AppHost-fanned value is exactly `school-collab-client`
(`Program.cs:66` + `appsettings.json:31`, fanned `Program.cs:112,576`) — the audience a token must
carry is therefore the **resource** audience, and the new client is only the authorization
`client_id` in the OIDC request. This mirrors the existing audience mapper's role
(`school-collab-realm.json:117-123`) and satisfies spec D4 (API contract unchanged).

Other client-representation choices, stated so the worker does not improvise:

- **No `secret`** — public clients carry none; embedding one would be the vulnerability D5 exists
  to avoid.
- **`webOrigins: []`** — CORS is a browser-to-server browser-context concern; the device's token
  exchange is a native HTTP `POST /token` where CORS does not participate. The guard's
  webOrigins assertion is scoped to `school-collab-client` only
  (`AppHostRealmImportArchitectureTests.cs:210-217`), so a missing/empty webOrigins on the new
  client does not trip it.
- **No `attributes` block** (no `post.logout.redirect.uris`): user-initiated logout is **scope-out
  for this round** (see Scope — out). Add it when logout is implemented.

**What keeps the guard green (W1's test amendments, deliberately — the exact-set assertion
`:331-337` fails otherwise):**

1. Amend `RealmImportFile_DeclaresNoPortalOidcClient_AndNoPortalRedirectUri`
   (`AppHostRealmImportArchitectureTests.cs:331-337`): the expected set becomes
   `{ "school-collab-client", "school-collab-auth-admin", "school-collab-mobile-client" }`,
   with a comment recording the L3 client and D5. The test's negative halves
   (`:339-343`: no `portal` substring, no `/bootstrap` suffix in ANY redirectUris) are untouched
   — the new client's `schoolcollab://signin-oidc` satisfies both.
2. Add new test methods in the same file mirroring the existing client's coverage for the mobile
   client: `publicClient:true` + `standardFlowEnabled:true` + `directAccessGrantsEnabled:false`
   (public-client shape, D5); no `secret` property; the four attribute mappers + roles + audience
   mappers present with the **same two-token-path flags** the existing roles-mapper test asserts
   (`:140-162` parity); the exact claim-name set
   `{tenant_id, tenant_name, tenant_type, teacher_id, roles}` via the same claim-name extraction
   (`:163-195` pattern); `included.client.audience == "school-collab-client"` (the resolved
   API-side audience, `AuthTenancyExtensions.cs:175,224`); and
   `redirectUris` contains a value with scheme `schoolcollab`. These new assertions are what
   makes §8 L3 criterion 1 mechanically checkable, not prose.
3. Everything else in the guard needs nothing: the filename/`realm` property is unchanged
   (`:47-64`), `roles.realm` is unchanged (`:87-104`), the service-account client block is
   unchanged (`:105-139`), and the auth-RP/post-logout redirect assertions target
   `school-collab-client` only (`:220-245`).

**W2 — `Mobile.Logic` gains the auth + registration machinery (all ubuntu-testable).**

New files (names and members; the worker implements, the plan-gate may reword members but not move
them out of this project without an owner decision):

1. `MobileAuthOptions.cs` — binds section `MobileAuth` from the head's config (same
   `FromConfiguration(IConfiguration)` pattern as `MobileApiOptions`
   `MobileApiOptions.cs:57-65,30`): `Authority` (realm URL), `ClientId`, `RedirectScheme`,
   `RedirectHost`, `Scope` (default `openid`), and `RequiredScopes` list. **`RedirectPath` is
   deliberately named `RedirectHost`** (gate P1-1 consequence): the redirect URI
   `schoolcollab://signin-oidc` parses with `signin-oidc` as the URI **authority/host**, not a
   path (it has none) — the option carries host `signin-oidc` and the redirect URI is built as
   `{RedirectScheme}://{RedirectHost}`, which both the Android `IntentFilter` (`DataHost`, W3
   item 5) and Keycloak's `redirectUris` entry must equal. **No URL construction in
   the class beyond joining Authority + path segments** — no scheme/host literals.
2. `ITokenStore.cs` — async interface: `ReadAsync(key, ct)`, `WriteAsync(key, value, ct)`,
   `ClearAsync(key, ct)`, with key constants `access_token`, `refresh_token`, `expires_at`
   pinned as one constants block (single spelling).
3. `JwtPayloadReader.cs` — base64url-decodes a JWT's middle segment and parses
   `System.Text.Json` to claim key/values (no signature verification — device-side the token is
   an opaque bearer; verification is the server's job, and the payload's claims are used for UI
   gating only; this is stated here because the worker's test must assert the *absence* of
   signature logic as deliberate). Multivalued claims (`roles`) read as one claim per array
   element.
4. `TokenSession.cs` — the mutable session over `ITokenStore` +
   `TimeProvider` (repo convention — the auth stores use it, `src/SchoolCollab.Auth/AuthServiceExtensions.cs:53`):
   holds the access token + absolute expiry (expiry − 30 s clock skew), a
   `GetBearerTokenAsync(ct)` that returns the cached token when unexpired, a
   `RefreshAsync(ct)` that `POST`s `{Authority}/protocol/openid-connect/token`
   (`grant_type=refresh_token`, client_id, refresh_token) via an
   `IHttpClientFactory`-named client (`"oidc-token"`, `BaseAddress = options.Authority` bound at
   registration — no literal), maps a refresh failure to a typed
   `TokenSessionExpiredException` **and clears the store** (session end), and
   `ApplyTokensAsync(tokenResponse)` consumed by `OidcLoginService`. Refresh policy per OD-1
   (gate-adjudicated 2026-10-12, unchanged by the owner grill: refresh-on-demand with the
   explicit no-401-resend boundary this round) — the worker implements as written.
5. `ISystemBrowserAuthenticator.cs` — `Task<IDictionary<string,string>> AuthenticateAsync(Uri
   authorizeUrl, Uri callbackUri, CancellationToken ct)`; implemented head-side over
   `WebAuthenticator` (W3). Failure/cancellation contract: the implementation throws
   `SignInCancelledException` (a Logic-owned type) on user cancellation, which
   `OidcLoginService` surfaces to the UI.
6. `OidcLoginService.cs` — builds the authorize request: `code_verifier` = 32 random bytes →
   base64url (BCL `RandomNumberGenerator`), `code_challenge`
   = base64url(SHA-256(verifier)) (`S256` — plain PKCE; documented, testable), random `state`,
   `response_type=code`, `client_id`/`redirect_uri`/`scope` from `MobileAuthOptions`; calls
   `ISystemBrowserAuthenticator`; verifies the returned `state` matches before consuming the
   `code` (mismatch → typed exception, no token persisted); exchanges the code at
   `{Authority}/protocol/openid-connect/token` (`grant_type=authorization_code`, `code_verifier`,
   `redirect_uri`) — the verifier lives in memory only, never in `SecureStorage`; on success
   calls `TokenSession.ApplyTokensAsync`. Exposes `SignInAsync` / `SignOutAsync`
   (SignOut = clear the store + `NotifyAuthenticationStateChanged`, no Keycloak end-session —
   logout flow is scope-out) and a `SignInState` property. No `nonce`: the client-side provider
   does not verify the id_token signature, so a nonce check would be theater — documented in the
   code as a deliberate omission (state + PKCE are the replay/interception defenses that matter
   for a native code flow).
7. `BearerTokenDelegatingHandler.cs` — `DelegatingHandler` overriding `SendAsync`:
   `TokenSession.GetBearerTokenAsync` → set `Authorization: Bearer …` (skip when the request
   already carries the header); per OD-2: on an empty/expired token attempt silent refresh first,
   send after a successful refresh, send unauthenticated after a failed one (server truth = 401)
   — **no automatic resend of a 401'd request this round**. Registered TRANSIENT (the
   delegating handler must not capture per-request state or be shared across handler pools —
   the same rule the server-side comments record,
   `ModuleServices.cs:32-34` note and `BearerForwardingDelegatingHandler.cs:11-13` remarks;
   the device-side handler follows the identical lifecycle reasoning).
8. `MobileAuthenticationStateProvider.cs` —
   `AuthenticationStateProvider` (package
   `Microsoft.AspNetCore.Components.Authorization`, `Directory.Packages.props:45`): on start,
   reads the session (decode access-token claims via `JwtPayloadReader`, raw claim spellings —
   measured contract above); `NotifyAuthenticationStateChanged` on start-of-day, after sign-in and
   after session-end; exposes the `ClaimsPrincipal` (raw `tenant_id`, `tenant_name`, `roles`
   entries); subscribes nothing — `TokenSession` signals it (one direction, no loops).
9. `MobileClientRegistrationExtensions.cs` — `AddMobileAdminClients(this IServiceCollection,
   MobileApiOptions)` registering, mirroring the server's Settings module shape
   (`Settings.ModuleServices.cs:49-119`), the 8 `Admin.Shared.Services` clients bound to
   `MobileApiOptions.Resolve(name)` (all eight map to `SettingsApi` — that is where the server
   points them, `ModuleServices.cs:51` `settings-api`) each with
   `.AddHttpMessageHandler<BearerTokenDelegatingHandler>()`; plus `AddScoped<VisibleTenantService>()`
   (the gates need it; the server does this at `Settings.ModuleServices.cs:118-121`).
   **Single-name binding, stated so the worker cannot improvise (gate P2):** every one of the
   eight registrations resolves its base address with **exactly one string —
   `"SettingsApi"`** (`MobileApiOptions.Resolve("SettingsApi")`). That name is the only match
   in `MobileApiOptions.ApiNames` (`MobileApiOptions.cs:35-39`: `StudentsApi`, `SettingsApi`,
   `AssignmentsApi` — and `Resolve` throws `ArgumentOutOfRangeException` on any string outside
   those three, `MobileApiOptions.cs:76-79`) — a per-client invented name (e.g.
   `"CodedValuesApi"`) is a guaranteed runtime throw, not a configuration miss. No other name
   may appear in `AddMobileAdminClients`.
   `AddMobileAuth(this IServiceCollection, MobileAuthOptions)` registering the store interface
   binding (implementation arrives head-side in W3), `TokenSession` (singleton),
   `OidcLoginService`, `MobileAuthenticationStateProvider` (singleton — the gates subscribe to
   `AuthenticationStateChanged` once and a scoped provider would leak subscriptions
   `GateBase.razor:95,145`), and the named `"oidc-token"` HttpClient.
10. **CPM + csproj:** `SchoolCollab.Mobile.Logic.csproj` gains
    `ProjectReference → SchoolCollab.Admin.Shared` (portable RCL — the same edge every module
    Application carries server-side, e.g. `Settings.Application` is where these registrations
    come from) and `PackageReference`s (no `Version` — NU1008/NU1009):
    `Microsoft.AspNetCore.Components.Authorization` (already centralised,
    `Directory.Packages.props:45`) and `Microsoft.Extensions.Http` (**new central entry** —
    needed for `AddHttpClient<…>`; NOT currently in `Directory.Packages.props`, only
    `Microsoft.Extensions.Http.Resilience :70` is; pin the version from what the restored graph
    supplies — read `obj/project.assets.json`, never guess, the L2 plan's P2 obligation pattern).
    `Directory.Packages.props` gains the one `PackageVersion` entry.

**W3 — the head: platform wiring + `SecureStorage` + `WebAuthenticator` implementations.**

1. `Services/SecureStorageTokenStore.cs` — implements `ITokenStore` over
   `SecureStorage.GetAsync/SetAsync/RemoveAsync` (Essentials ships with the head's
   `Microsoft.Maui.Controls` reference, head csproj `:68`). Keys = W2's constants.
2. `Services/SystemBrowserAuthenticator.cs` — implements `ISystemBrowserAuthenticator` over
   `WebAuthenticator.AuthenticateAsync(new WebAuthenticatorOptions { Url = authorizeUrl,
   CallbackUrl = callbackUri })`; maps cancellation to `SignInCancelledException`. Nothing else
   (no platform #if here — Essentials is cross-platform).
3. `MauiProgram.cs` — register the pieces:
   `builder.Services.AddSingleton(MobileAuthOptions.FromConfiguration(builder.Configuration))`
   (alongside the existing `MobileApiOptions` line, `MauiProgram.cs:28`), then
   `AddMobileAuth(…).AddMobileAdminClients(MobileApiOptions)` with the two head-side
   implementations (`ITokenStore`, `ISystemBrowserAuthenticator`) bound in `MauiProgram`
   (the head is the only place Essentials types compile — W2's registration API accepts them as
   ordinary `AddSingleton<ITokenStore, SecureStorageTokenStore>()` calls).
4. `appsettings.json` + **new embedded** `appsettings.Development.json` — the `MobileAuth`
   section (`MobileAuth: { Authority, ClientId, RedirectScheme, RedirectHost, Scope }`) plus,
   if a dev override is required, `MobileApi` adjustments. The Development file is loaded when
   the app runs in Development — **mechanism (gate P1-2, chosen):** the builder is a
   `MauiAppBuilder` (`MauiProgram.cs:18`), which exposes `Configuration`, `Services` and
   `Logging` — **it has no `.Environment`**, so the check is
   `Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") == "Development"`, evaluated
   **inside `AddAppSettings(MauiAppBuilder)`** (`MauiProgram.cs:44`) immediately before the
   second `AddJsonStream` is appended. It must live there and not later: configuration sources
   must be added **before** `builder.Build()` (`MauiProgram.cs:36`), so the alternative —
   resolving `IHostEnvironment` from the built host — is too late to add a source and is
   rejected. (A second `AddAppSettings`-style stream in `MauiProgram.cs:26-44`.) **Values are never guessed:** the committed dev values are written
   from the dev-loop run measured during the round (Keycloak reachable at
   `http://localhost:8080/realms/school-collab` with a standalone container — the authority
   spelling `…/realms/<realm>` is what `ReadKeycloakClientSettings` expects
   `AuthTenancyExtensions.cs:221-222`). The Development file targets the Windows dev loop
   (`localhost`); Android-emulator values (`10.0.2.2`) are documented as an owner-run override,
   not silently committed for one posture and breaking the other (OD-3 + residual risks).
5. **Android callback scheme — `[IntentFilter]` attribute on `MainActivity`; NOT a manifest
   edit.** Verified (gate P1-1): `AndroidManifest.xml` has **no `<activity>` element** —
   `AndroidManifest.xml:3` is one self-closing `<application …/>` with only icon/backup
   attributes, no children; `MainActivity` is generated from the `[Activity]` attribute
   (`src/Mobile/SchoolCollab.Mobile/Platforms/Android/MainActivity.cs:7-8`). An orphan
   `<intent-filter>` in that file is invalid XML for this project and would fail the android
   TFM build. **Chosen wiring: attribute-based manifest generation — the same mechanism that
   emits the activity itself.** `MainActivity.cs` gains, alongside the existing `[Activity]`
   attribute:
   `[IntentFilter(new[]{ Intent.ActionView }, Categories = new[]{ Intent.CategoryDefault,
   Intent.CategoryBrowsable }, DataScheme = "schoolcollab", DataHost = "signin-oidc")]`
   (`Android.App.IntentFilterAttribute`; MAUI's source-embedded manifest generation merges it
   into the generated `<activity>` block at build time). **Scheme + host, and why not
   scheme-only:** with `DataScheme` alone the filter would accept every `schoolcollab://…` URI
   any app sends; pinning `DataHost = "signin-oidc"` matches exactly the callback the login
   service builds (`{RedirectScheme}://{RedirectHost}`, W2 item 1). No `DataPath` — the
   redirect URI carries no path component, so there is nothing to pin. **What `WebAuthenticator`
   needs to match:** on Android it resolves its pending task when an intent carrying the
   callback's scheme+authority re-launches the app, so the filter's data must equal the
   `WebAuthenticatorOptions.CallbackUrl`'s scheme+authority — the exact-string discipline
   Keycloak applies to the same URI in `redirectUris` (W1 / contingency 3). The `<queries>`
   browser block (Android 11+ visibility) remains a genuine manifest-level element: add it to
   `AndroidManifest.xml` only if the android build or an emulator run shows the
   silent-no-browser failure, recorded as a named deviation in the Worker Report.
6. **iOS + MacCatalyst `Info.plist`**
   (`src/Mobile/SchoolCollab.Mobile/Platforms/iOS/Info.plist`,
   `Platforms/MacCatalyst/Info.plist`): add `CFBundleURLTypes` with
   `CFBundleURLName com.schoolcollab.mobile` and `CFBundleURLSchemes [schoolcollab]` — matching
   `ApplicationId com.schoolcollab.mobile` (head csproj `:23`).
7. **Windows (unpackaged, `WindowsPackageType=None` head csproj `:30`):** Essentials'
   `WebAuthenticator` on Windows requires the WAB callback to resolve through the app's package
   protocol registration; an unpackaged app cannot register the `schoolcollab://` scheme the
   import-free dev loop needs. **Recorded as a contingency, not silently patched:** the Windows
   dev loop runs only if `WebAuthenticator` works for it (try it); otherwise the ordered fallback
   below applies. No `Package.appxmanifest` change this round (the app is deliberately unpackaged
   — L1's dev-loop decision, head csproj `:29-30` comment).

**W4 — the head's auth UI wire-up (the minimum to make criterion 5 observable).**

`src/Mobile/SchoolCollab.Mobile/Components/Pages/Spike.razor` gains a small section (styles into
`Spike.razor.css` — CSS-isolation convention, no inline `style`): a sign-in button
(`OidcLoginService.SignInAsync`, showing the typed `SignInCancelledException` message as a
`FluentMessageBar`) and a `<TenantGate>` block whose child renders real `FieldDisplay` values
(tenant name + teacher id) once `VisibleTenantService` sees a real `tenant_id`
(`VisibleTenantService.cs:39-46`) — before sign-in it must render the fallback banner
(`GateBase.razor:53-58`, `TenantGate`'s `ShowDefaultBanner` default): **that fallback-vs-tenant
flip IS criterion 5's observable**. `MainLayout.razor` and `Routes.razor` stay untouched.

**W5 — tests (ubuntu, `tests/SchoolCollab.Mobile.Tests.Unit/`, MTP conventions —
`.github/copilot/rules/testing.md`, the project joins the root solution `SchoolCollab.slnx:119`).**

1. `JwtPayloadReaderTests` — base64url decode, `roles` as multivalued, malformed inputs throw.
2. `TokenSessionTests` — unexpired token reused; expired → refresh POST shape verified against a
   fake handler (exact request body keys); refresh 4xx → store cleared +
   `TokenSessionExpiredException`; empty/absent token → null bearer + no refresh storm (one
   attempt per call).
3. `OidcLoginServiceTests` — authorize URL contains `code`, `code_challenge` = S256 of the
   verifier, `state`, `client_id`, `redirect_uri`, `scope`; state mismatch rejected; code
   exchange POST verified; success path stores tokens + fires the auth-state notification;
   cancellation exception surfaces as-is.
4. `MobileAuthenticationStateProviderTests` — empty session → unauthenticated state;
   stored token → authenticated principal with `tenant_id` + `roles` claims; sign-out clears.
   (The claim-spelling contract asserted here is the same one VisibleTenantService reads,
   `VisibleTenantService.cs:39`.)
5. `BearerTokenDelegatingHandlerTests` — header attached when a token exists; unauthenticated
   send has no header; expired-token → refresh → retried-with-new-token path; refresh failure →
   request sent without header.
6. `MobileClientRegistrationTests` — `AddMobileAdminClients` on an empty `ServiceCollection`
   + `MobileApiOptions` resolves every registered Admin.Shared client with the expected
   `BaseAddress` and the bearer handler in the pipeline; every client's base address comes
   from the single `"SettingsApi"` binding (asserting the gate-P2 rule — no invented
   per-client name reaches `Resolve`); missing configuration degrades one API
   (`MobileApiOptions.Resolve` returns null, `MobileApiOptions.cs:86-91`).
7. `MobileAuthOptionsTests` — binds `MobileAuth` from an in-memory configuration; missing
   section yields the defaults (`Scope = openid`); the options carry the scheme/host spelling
   (`RedirectScheme`, `RedirectHost`) and the class has no `http(s)://` literal (the D8 grep
   at class granularity). *(Added in rev 2 so this list and the expected-file list agree —
   the list already carried this file; the gate flagged the omission here.)*
8. The realm-guard amendments of W1 are `SchoolCollab.ArchitectureTests.Unit` tests (not mobile
   tests) and run in the root solution's ubuntu build.

**W6 — dev-loop procedure, recorded in the round doc (not a repo file this round).**

The Worker Report and Acceptance sections record the measured run: (a) standalone Keycloak
container with the same realm file bind-mounted (`quay.io/keycloak/keycloak:26.4.7` — the tag the
AppHost pins, `Program.cs:81` — `start-dev --import-realm`), host port `8080`; (b) the three APIs
`dotnet run` on their committed launch-profile URLs (`launchSettings.json:5-9`, the values the
head's `appsettings.json` already binds); (c) the Windows desktop head F5, sign-in, one
authenticated call, the server-side log line captured. The owner-run device/emulator leg
(OD-3/criterion-3 record) is documented as an ordered, reproducible procedure — never asserted
as done when it was not run.

### Contingency — if `WebAuthenticator` / the redirect cannot be exercised on a dev host (ordered, not open-ended)

1. **Windows desktop fails on the WAB callback** (evidence: `NotImplementedException` /
   protocol-registration failure at `WebAuthenticator.AuthenticateAsync`, consistent with the
   unpackaged posture, head csproj `:30`): do NOT add MSIX packaging to force it (that would
   reverse L1's unpackaged dev-loop decision). Ordered next step: run the android TFM in an
   emulator if the local workload allows (`dotnet workload install maui` has already been paid
   once in CI); the emulator path needs `10.0.2.2`-based dev values (local override of the
   Development file, documented; plus the MAUI dev-trusted-cert procedure for https, or http
   launch URLs `5450/5460/5199` with cleartext allowed — use the API projects' `http://`
   applicationUrl halves, already committed facts, `launchSettings.json:5-9`).
2. **Neither Windows nor an emulator is available in the round window** (evidence: no device-capable
   TFM run): the round's deliverable becomes the unit-tested pieces (W5 — every acquisition,
   exchange, refresh, provider and handler behaviour is fake-driven green on ubuntu) + the realm
   import (W1) + the documented device procedure (W6), with criterion 3 recorded **OPEN —
   owner-run step**, not silently met (OD-2). No partial UI claims ("works on the desktop" while
   unattested) reach the Acceptance section.
3. **Keycloak rejects the custom-scheme redirect** (evidence: the authorize request fails with
   `invalid_redirect_uri` at the first real sign-in attempt — the realm import accepted the
   client but the URI match failed): verify the imported client's redirectUris **exactly** equal
   `schoolcollab://signin-oidc` (Keycloak matches exact strings — the `##`-separator lesson at
   `AppHostRealmImportArchitectureTests.cs:246-278` is the precedent for exact-match URI
   handling); a `localhost`-loopback redirect (`http://127.0.0.1:<port>`) is the recorded
   alternative shape for the Android emulator run (WebAuthenticator supports a local-address
   callback) — **and loopback is the preferred posture where practical** (it owns no scheme at
   all, unlike the unverified `schoolcollab://` scheme; Constraints, "Custom-scheme hijack
   posture") — change it as a named deviation in the Worker Report, never as a silent edit of
   the scheme.

### Constraints the worker must honor

- Citation discipline (`documents/specs/round-runner-model-policy.md`, decision 4): every repo
  fact the WORKER REPORT asserts carries `file:line`; no fact restated from this plan without
  re-verifying against the tree.
- **No `http(s)://` string literal in any `.cs` under `src/`** (spec D8 + owner pin 5; enforced by
  diff-review grep of `src/Mobile` because `CrossModuleWiringTests`' `excludedHosts`
  `CrossModuleWiringTests.cs:300-302` silently skips `localhost`).
- **No `ci.yml` change** (owner pin 6); mobile job stays `ENABLE_MAUI_CI`-gated (`ci.yml:174`).
- Realm file: strict JSON only (`AppHostRealmImportArchitectureTests.cs:29-43`); filename and
  `realm` property untouched (`:47-64`); unknown Keycloak fields are import-fatal — the
  `postLogoutRedirectUris` top-level lesson (`:246-278`).
- CPM: no `Version=` attributes on `PackageReference` (`Directory.Packages.props` policy).
- One writer per working tree (AGENTS.md); repo-scoped searches only (`git grep` under `src/`,
  `tests/`, `documents/`); no `dotnet build`/`dotnet test` by the ORCHESTRATOR (this plan run) —
  the parent owns authoritative builds; the WORKER may compile-verify per AGENTS.md's in-session
  build rule.
- C# edits follow `.github/copilot/rules/dotnet-best-practices.md` (mandatory read before any
  `.cs`/`.razor` edit); razor/XAML edits follow `.github/copilot/rules/blazor-components.md`.
- **No token material in logs or exception messages (gate P2, worker constraint):** no type on
  the auth path — `ITokenStore`/`SecureStorageTokenStore`, `TokenSession`, `OidcLoginService`,
  `BearerTokenDelegatingHandler`, `MobileAuthenticationStateProvider` — may log, or include in
  an exception message, a **token value, a refresh-token value, or `Authorization` header
  content**. Exception text names *what* failed (`"refresh token rejected (invalid_grant)"`),
  never *the credential*; no `Console.WriteLine`/debug print of request bodies or header
  values, including under `#if DEBUG`. The diff reviewer greps the auth-path files for those
  key names appearing inside `Log*`, `WriteLine`, interpolated exception strings or
  `ToString`s.
- **Custom-scheme hijack posture (gate P2):** `schoolcollab://` is an unverified, non-owned
  scheme — any Android app can register the same filter and receive the redirect. The deliberate
  mitigations are **PKCE + `state`**: a hijacker that captures the redirect holds a short-lived
  `code` that is useless without the in-memory `code_verifier` (never stored), and a forged
  callback fails the `state` match before any token is persisted. The worker adds a code
  comment to `OidcLoginService` (and `MobileAuthOptions`) stating exactly this. Documented
  posture: the **loopback alternative `http://127.0.0.1:<port>` is the preferred shape where
  practical** — it owns no scheme at all and is not hijackable by another app — but it needs a
  per-run listener and platform loopback permissions; the custom scheme ships this round **with
  the mitigations above explicit in code**, and any practical switch to loopback is a named
  deviation in the Worker Report (contingency 3), never a silent edit.
- The parent may amend `documents/specs/` at acceptance; the worker/plan do not touch it.

### Expected file list

```
src/AppHost/SchoolCollab.AppHost/school-collab-realm.json
                                                (modified: +third client, strict JSON)
tests/SchoolCollab.ArchitectureTests.Unit/AppHostRealmImportArchitectureTests.cs
                                                (modified: exact-set + new mobile-client tests)
src/Mobile/SchoolCollab.Mobile.Logic/MobileAuthOptions.cs                     (new)
src/Mobile/SchoolCollab.Mobile.Logic/ITokenStore.cs                           (new)
src/Mobile/SchoolCollab.Mobile.Logic/JwtPayloadReader.cs                      (new)
src/Mobile/SchoolCollab.Mobile.Logic/TokenSession.cs                          (new)
src/Mobile/SchoolCollab.Mobile.Logic/ISystemBrowserAuthenticator.cs           (new)
src/Mobile/SchoolCollab.Mobile.Logic/OidcLoginService.cs                      (new)
src/Mobile/SchoolCollab.Mobile.Logic/BearerTokenDelegatingHandler.cs          (new)
src/Mobile/SchoolCollab.Mobile.Logic/MobileAuthenticationStateProvider.cs     (new)
src/Mobile/SchoolCollab.Mobile.Logic/MobileClientRegistrationExtensions.cs    (new)
src/Mobile/SchoolCollab.Mobile.Logic/SchoolCollab.Mobile.Logic.csproj
                                                (modified: +Admin.Shared ref, +2 packages, CPM-clean)
src/Mobile/SchoolCollab.Mobile/Services/SecureStorageTokenStore.cs            (new)
src/Mobile/SchoolCollab.Mobile/Services/SystemBrowserAuthenticator.cs         (new)
src/Mobile/SchoolCollab.Mobile/MauiProgram.cs
                                                (modified: auth wiring + dev appsettings stream)
src/Mobile/SchoolCollab.Mobile/appsettings.json                               (modified: +MobileAuth)
src/Mobile/SchoolCollab.Mobile/appsettings.Development.json                   (new embedded; values measured)
src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj
                                                (modified: +EmbeddedResource appsettings.Development.json)
src/Mobile/SchoolCollab.Mobile/Platforms/Android/MainActivity.cs
                                                (modified: +[IntentFilter] attribute — scheme+host wiring W3 item 5; NOT a manifest edit)
src/Mobile/SchoolCollab.Mobile/Platforms/Android/AndroidManifest.xml
                                                (modified ONLY if the <queries> browser block proves needed — a named deviation)
src/Mobile/SchoolCollab.Mobile/Platforms/iOS/Info.plist                       (modified: CFBundleURLTypes)
src/Mobile/SchoolCollab.Mobile/Platforms/MacCatalyst/Info.plist               (modified: CFBundleURLTypes)
src/Mobile/SchoolCollab.Mobile/Components/Pages/Spike.razor                   (modified: sign-in + TenantGate)
src/Mobile/SchoolCollab.Mobile/Components/Pages/Spike.razor.css               (modified: gated section styles)
tests/SchoolCollab.Mobile.Tests.Unit/JwtPayloadReaderTests.cs                 (new)
tests/SchoolCollab.Mobile.Tests.Unit/TokenSessionTests.cs                     (new)
tests/SchoolCollab.Mobile.Tests.Unit/OidcLoginServiceTests.cs                 (new)
tests/SchoolCollab.Mobile.Tests.Unit/MobileAuthenticationStateProviderTests.cs(new)
tests/SchoolCollab.Mobile.Tests.Unit/BearerTokenDelegatingHandlerTests.cs     (new)
tests/SchoolCollab.Mobile.Tests.Unit/MobileClientRegistrationTests.cs         (new)
tests/SchoolCollab.Mobile.Tests.Unit/MobileAuthOptionsTests.cs                (new)
tests/SchoolCollab.Mobile.Tests.Unit/SchoolCollab.Mobile.Tests.Unit.csproj
                                                (modified: +DI/packages the auth tests need)
Directory.Packages.props                                      (modified: +Microsoft.Extensions.Http central entry)
```

**Not touched:** `SchoolCollab.slnx`/`SchoolCollab.Mobile.slnx` (no membership changes);
`.github/workflows/ci.yml` (owner pin 6); any `appsettings.json` of the four Auth:Keycloak hosts
(the API-side posture is unchanged — spec D4); `documents/specs/` (parent-acceptance task);
AppHost `Program.cs` (the dev loop runs standalone servers — OD-3; no keycloak host-port pin
this round).

### Acceptance criteria (the spec's §8 L3 five criteria verbatim as the spine, plus round hygiene)

Spec §8 L3, each restated as something verifiable:

- **L3.1** — "The realm JSON contains the new public client with all five claim mappers plus the
  audience mapper; `AppHostRealmImportArchitectureTests` still passes."
  **Mechanical, CI/ubuntu:** the amended exact-set test + the new mobile-client assertions run in
  `SchoolCollab.ArchitectureTests.Unit` on ubuntu; the whole suite 0 failures.
- **L3.2** — "PKCE acquisition returns a token whose claims carry `tenant_id` / `teacher_id` /
  `roles` (assert against a decoded token, no live Keycloak in unit tests)." **Mechanical,
  ubuntu:** record the real decoded access token from the round's dev-loop run (its claims quoted
  in the Worker Report) as the reference payload; `OidcLoginServiceTests` +
  `MobileAuthenticationStateProviderTests` assert the same claim spellings against fixture
  tokens — no live Keycloak involved.
- **L3.3** — "One authenticated call reaches an API from the device/emulator and returns 200,
  proved by a server-side log line — not by UI appearance." **Split, honestly (OD-2):** on CI —
  impossible (no device); this round alone — deliverable = the bearer handler + the documented
  device procedure (W6) + a Windows-desktop 200-OK with its log line if Windows WAB cooperates
  (contingency item 1/2 govern what is recorded). The owner-run emulator/device leg is recorded
  in `## Acceptance` as **an explicit OWNER-RUN PENDING line** — the criterion is NOT silently
  marked met; the spec amendment at acceptance time carries the owner's run evidence when it
  happens.
- **L3.4** — "No C# literal under `src/` matches the base-address regex, and the diff review
  greps `src/Mobile` for `http(s)?://` literals (§6: the guard's `excludedHosts` means CI cannot
  catch them)." **Mechanical:** `git grep -nE '"https?://[^"]+"' -- src/Mobile/**/*.cs` runs
  empty; the diff reviewer re-runs it. (The realm's redirect URIs are JSON, not C# — outside the
  regex's domain and inside D8's rule, which governs base addresses.)
- **L3.5** — "`AuthenticationStateProvider` yields an authenticated principal and `TenantGate`
  renders its tenant branch instead of the fallback." **Mechanical, ubuntu:** the provider
  yields an authenticated principal with `tenant_id` under the exact
  `MobileAuthenticationStateProviderTests` + `VisibleTenantService`'s real read path
  (`VisibleTenantService.cs:39`); the branch render is bUnit-assertable in the head? No — the
  head is device-TFM only. The **ubuntu-mechanical half** is the provider contract; the
  **render half** is the device/desktop run recorded in W6 (and the UI-tester leg if dispatched).
  Recorded with the same honesty rule as L3.3.

Round hygiene (mechanical): root-solution build 0 errors with `Mobile.Logic` + Mobile.Tests.Unit
green; the `android` head build (mobile CI posture) unaffected or fixed in the same change set;
`SchoolCollab.ArchitectureTests.Unit` 0 failures; the diff-review `http(s)?://` grep empty;
`diffs-mobile-credential.patch` frozen from base `6cb415ec` excluding itself.

### Scope — out

User-initiated logout / Keycloak end-session round-trip (`post.logout.redirect.uris` stays
unregistered for the new client); any `ci.yml` edit or `ENABLE_MAUI_CI` gate change; any
Students/Assignments module client registration (`StudentsApiClient` is severed from the head's
closure — L4's slice decision, spec §8 L4 criterion 4); any `documents/specs/` amendment (parent
task); AppHost `Program.cs` edits (OD-3: the dev loop runs standalone servers); commit/push (owner-gated per AGENTS.md).

### Gates

- **UI:** low — one head page (`Spike.razor` + its `.razor.css`) gains a gated section and a
  sign-in control; the shared component layer is rendered unmodified. UI tester dispatch is the
  default posture for the round's UI delta; a waiver must be gate-adjudicated like L2's
  (`round-mobile-severance.md` § Execution log) and recorded in `## UI Tester`.
- **Contract:** none — API wire shapes untouched (D4); the new realm client is realm-side
  configuration, not an API contract change.
- **Migrations:** none.
- **Secrets:** none — the realm gains NO secret entry (public client); no new AppHost parameter;
  `appsettings.Development.json` carries no secret (PKCE public flow needs none).
- **Auth/security surface:** this IS one — the round is auth-adjacent by owner pin 1, which is
  exactly why it runs full Tier 3 (round-runner-model-policy.md item 4, trigger (ii)) and why the
  owner pins above were re-stated, not auto-accepted.

### Open decisions (grill format — numbered, each with a recommendation and its cost)

> **Adjudication status (updated 2026-10-13).** The gate reviewed and agreed all four
> (gate-adjudicated, 2026-10-12): **OD-1** refresh-on-demand with the explicit no-401-resend
> boundary (stays gate-adjudicated — the owner grill issued no pin on it); **OD-2** criterion 3
> recorded **OPEN / owner-run pending** unless a real device run with a server-side log line
> happens; **OD-3** the real token path with `FEATURE:DisableOIDCAuth` **OFF** (the flag posture
> documented, never committed as a default); **OD-4** the registration code in `Mobile.Logic`
> (**adjudicated and closed** — no further review input needed). The owner grill (2026-10-13)
> has answered OD-2 and OD-3 — their answers are folded below as **owner pins** ("Owner pins
> from the owner grill", above); the pins **match** the gate's adjudications, so no further
> plan-review run was needed.

**OD-1 — Token refresh policy (the spec §9's OD2, carried into L3's plan; spec §7 already flags
"token lifetime/refresh policy and where the refresh token lives on the device" as the
remaining sub-decision, `maui-agent-skills-readiness.md:252`).**
Options: **(a)** silent refresh on a background timer (proactive, always-fresh); **(b)** refresh
on demand in the bearer handler — pre-send check, refresh when expired or absent, fall back to
interactive re-auth when Keycloak rejects the refresh token; **(c)** reuse-until-expiry — no
refresh token use at all, sign-in again on expiry.
**Recommend (b).** The spec's own OD2 recommendation is silent refresh with an interactive
fallback (`documents/specs/mobile-hybrid-app.md:252-256` — that OD is *this* decision restated,
so adopting it here closes the loop rather than reopening it); (b) implements it without a
timer: no MAUI background-timeout surface in Logic, no clock-drift issues, the expiry check
runs exactly where a request exists, and Keycloak's SSO-session-bound refresh token makes "map
refresh failure to session end" (the spec's `session_ended`/410 parity note) a single code path.
Cost: a rare double-401 when token and refresh both just expired (one extra interactive
sign-in); (a) costs a battery/polling surface plus timer-test complexity in Logic; (c) costs the
worst UX (sign-in prompt mid-use) but the least code.

**Gate adjudication (2026-10-12):** agreed as recommended — **(b) refresh-on-demand, with the
explicit no-401-resend boundary** the bearer handler already encodes ("no automatic resend of a
401'd request this round", W2 item 7). **Status (2026-10-13):** stays gate-adjudicated — the
owner grill issued pins on OD-2/OD-3 only; no owner pin was sought or issued on OD-1. The
worker implements (b) as written.

**OD-2 — Satisfiability of §8 L3 criterion 3 (the 200-OK device call, proved by a server-side
log line).**
The repo cannot answer whether a device/emulator run is available to this round — nothing in the
tree records the owner's device inventory, and CI cannot provide one (the mobile job is a build,
not a run, and is gated `ci.yml:174`).
**Recommend:** the round delivers everything testable on ubuntu (W5's full fake-driven coverage)
plus the documented reproducible device procedure (W6), optionally upgraded by the
Windows-desktop 200-OK run when WAB cooperates (contingency 1); `## Acceptance` records
criterion 3 as **OPEN — OWNER-RUN step pending**, and the spec amendment at acceptance time
carries the log-line evidence when the owner performs it. Cost: the criterion stays honest (a
"met" row it does not deserve is never written); the alternative — mark it met from the Windows
desktop or from a fake — would repeat exactly the "lying build" failure mode the L1 round's
shim rejection was written to prevent (`round-mobile-host-skeleton.md` § Mid-round revision).

**Gate adjudication (2026-10-12):** agreed as recommended — **criterion 3 is recorded OPEN /
owner-run pending** unless a real device run with a server-side log line happens (Windows-desk-
top-only or fake evidence never satisfies it). **Owner pin (2026-10-13):** confirmed —
criterion 3 stays **OPEN — owner-run pending** (owner pin 2 above); `## Acceptance` must carry
it as an explicit open item with the follow-up reference (W6's written device procedure). No
further review input needed; the worker implements as written.

**OD-3 — The dev-loop posture: run the device loop with `FEATURE:DisableOIDCAuth` ON (no token
needed) or against the real token path.**
Options: **(a)** flag ON — the APIs drop their bearer requirements
(`CodedValueEndpoints.cs:23-28` pattern) and the app needs no credential to render; the client
however then also needs the `x-tenant-id` dev header (tenant comes only from TestAuth's
dev-selection path in that mode — readiness doc G3, `maui-agent-skills-readiness.md:173`),
which is server-side-auth posture, not today's token claims.
**(b)** flag OFF — the real credential is the dev loop.
**Recommend (b) this round** — L3 exists to exercise the credential; a DisableOIDCAuth dev loop
would validate everything except the round's headline. Where (b) is impractical (no device
capable host in the window), the UI-side inner loop may run flag ON ad hoc, and everything the
real path covers is covered by W5's tests + W6's procedure. Cost of (b): the standalone-IdP
routine (one docker run + three `dotnet run`s, W6) and the Windows-WAB uncertainty (contingency
1); cost of (a): the round's acceptance would be evidence-free on the credential and the
`x-tenant-id` head-side handler would be *this* round's new code — scope the round does not ask
for.

**Gate adjudication (2026-10-12):** agreed as recommended — **the real token path, flag OFF**;
the flag posture stays documented and `FEATURE:DisableOIDCAuth` is **never committed as a
default** (its startup-flag governance, spec `startup-flag-governance.md`, is untouched this
round). **Owner pin (2026-10-13):** confirmed — for this round's evidence the dev-loop posture
is the **real token path** with `FEATURE:DisableOIDCAuth` **OFF** (owner pin 3 above); the flag
posture stays documented (the `10.0.2.2` emulator override and the Windows loopback fallback
too) and is **never committed as a default**. Reason: with the flag ON everything the round
proves would bypass the credential it exists to prove. No further review input needed.

**OD-4 — Where the client-side registration code lives (only if the gate re-opens it).**
The plan places it in **`Mobile.Logic`** (`MobileClientRegistrationExtensions.cs`) with a
`ProjectReference` to `Admin.Shared`, because (i) D3/OD3-of-the-spec pins "anything CI has to
see lives in `Mobile.Logic`" (`SchoolCollab.Mobile.Logic.csproj:10-13`), (ii) the server-side
precedent is exactly a library-owned registration module referencing Admin.Shared
(`src/Settings/SchoolCollab.Settings.Application/ModuleServices.cs:15`), and (iii) it makes the
registration's base-address/handler wiring unit-testable on ubuntu (W5 item 6). Cost: Logic
grows a reference to Admin.Shared (which transitively carries FluentUI into Logic's graph —
already true of the head, and harmless on a `net10.0` library). The alternative (registration
in `MauiProgram`, logic-free head) would make the head the bearer of 100+ lines of untestable
service wiring and contradicts the Logic split's reason for existing; kept out.

**Gate adjudication (2026-10-12):** agreed as recommended — the registration stays in
**`Mobile.Logic`**. **Adjudicated and closed** — unlike OD-2/OD-3 (which the owner grill later
pinned) and OD-1 (explicitly gate-adjudicated), this one required no owner input; the worker
may implement W2 item 9 as written.

---

## Worker Report

**Transcribed by the parent from the worker run (deepseek-v4.1-flash, 2026-10-13).**

### Delivered — W1–W5 as planned

- **W1 — realm + guard** (`src/AppHost/SchoolCollab.AppHost/school-collab-realm.json`,
  `tests/SchoolCollab.ArchitectureTests.Unit/AppHostRealmImportArchitectureTests.cs`): the third
  client `school-collab-mobile-client` added (`school-collab-realm.json:142`) — `publicClient:
  true` (`:145`), standard flow enabled, `directAccessGrantsEnabled: false`, **no `secret`**, no
  `attributes` block, `redirectUris` = `["schoolcollab://signin-oidc"]`, `webOrigins: []`; the six
  protocol mappers copied verbatim from `school-collab-client` (the four attribute mappers, the
  audience mapper emitting `included.client.audience = "school-collab-client"` — the resource
  audience the APIs' `AddJwtBearer` validates — and the multivalued `roles` realm-role mapper).
  The guard was widened exactly as owner pin 1 approved: the exact-client-set assertion now names
  the mobile client, plus new mobile-client assertions (public-client shape, no secret, claim-name
  set, audience, custom-scheme redirect URI); the negative halves (no `portal` substring, no
  `/bootstrap` suffix) survive.
- **W2 — the Logic machinery** (`src/Mobile/SchoolCollab.Mobile.Logic/`): nine files —
  `MobileAuthOptions.cs` (binds `MobileAuth`; `RedirectHost` spelling, no scheme/host literals),
  `ITokenStore.cs` (`ReadAsync`/`WriteAsync`/`ClearAsync` + the three key constants),
  `JwtPayloadReader.cs` (base64url middle-segment decode, multivalued `roles`, **no signature
  verification — deliberate, commented**, `JwtPayloadReader.cs:14-16` carries the raw-realm-
  spelling contract), `TokenSession.cs` (cached token with 30 s skew, refresh-on-demand per
  OD-1, `ClearAsync` at `:348`), `ISystemBrowserAuthenticator.cs` (`SignInCancelledException`
  contract), `OidcLoginService.cs` (`SignInAsync` `:82` / `SignOutAsync` `:133`, `code_verifier`
  minted in memory only, `code_challenge` = SHA-256 of verifier — **PKCE `S256`, plain
  method never used** (`:60`), random `state` verified ordinally before any token is persisted
  (`:101-104`); the PKCE + `state` hijack-mitigation comment is in place),
  `BearerTokenDelegatingHandler.cs` (skip when the header exists; refresh-on-demand; no 401
  resend), `MobileAuthenticationStateProvider.cs` (raw realm claim spellings,
  `NotifyAuthenticationStateChanged` on every session change, `:80`),
  `MobileClientRegistrationExtensions.cs` (`AddMobileAuth` + `AddMobileAdminClients` — the eight
  `Admin.Shared` clients each resolved with exactly one name, `"SettingsApi"`; the `Resolve`
  throw is at `MobileApiOptions.cs:76-79`, the gate-corrected citation) — plus the csproj's
  `Admin.Shared` reference and the CPM entries.
- **W3 — the head** (`src/Mobile/SchoolCollab.Mobile/`): `Services/SecureStorageTokenStore.cs`,
  `Services/SystemBrowserAuthenticator.cs`; `MauiProgram.cs` wires
  `MobileAuthOptions.FromConfiguration` (`:43`) → `AddMobileAuth(…).AddMobileAdminClients(…)`
  (`:55-56`); the Development overlay loads via the gate-mandated `DOTNET_ENVIRONMENT` check
  (`:25`) with the embedded `appsettings.Development.json` (`:22`); the Android callback is the
  **attribute-based `[IntentFilter]` on `MainActivity`** (`MainActivity.cs:24` — `DataScheme =
  "schoolcollab"`, `DataHost = "signin-oidc"`; NOT a manifest edit, per the gate's P1-1 ruling);
  iOS + MacCatalyst `Info.plist` gained `CFBundleURLTypes`. The conditional
  `AndroidManifest.xml` `<queries>` addition proved unnecessary — the file was never touched.
- **W4 — the auth UI** (`Components/Pages/Spike.razor` + `Spike.razor.css`): the sign-in button
  with an in-flight disabled state (`Disabled="@_signingIn"`), the typed error surfaced as a
  `FluentMessageBar` (`Intent="MessageIntent.Error" Title="Sign-in failed"`), and a
  `<TenantGate>` whose child renders real `FieldDisplay` values (`tenant_name`/`teacher_id`);
  styles in the isolated CSS. The fallback-banner → tenant-branch flip is the observable.
- **W5 — tests** (`tests/SchoolCollab.Mobile.Tests.Unit/`): the seven planned suites
  (`JwtPayloadReaderTests`, `TokenSessionTests`, `OidcLoginServiceTests`,
  `MobileAuthenticationStateProviderTests`, `BearerTokenDelegatingHandlerTests`,
  `MobileClientRegistrationTests`, `MobileAuthOptionsTests`) — **78 tests, 0 failures** (parent's
  authoritative run).

### Beyond the plan — a real Keycloak container run (worker-sourced evidence)

The worker stood up a **real Keycloak container** (`start-dev --import-realm`, the AppHost-pinned
image tag) and proved the credential end-to-end: the realm import completed clean, and a full
**Authorization Code + PKCE (`S256`)** sign-in was accepted with `client_id=school-collab-
mobile-client` and `redirect_uri=schoolcollab://signin-oidc`. The resulting real access token
carried: `iss`, **`aud = "school-collab-client"`** (the resource audience, exactly as the plan's
Resolved audience-mapper requirement predicted), `azp = school-collab-mobile-client`, `tenant_id`,
`tenant_name`, `tenant_type`, `teacher_id`, `roles` (after a runtime realm-role grant), `scope`,
and `exp − iat = 300`. A **refresh-token round trip** succeeded with rotation. Reference payload
per L3.2. The worker also **found** that the seeded `dev-teacher` held no `realmRoles` — so `roles`
was absent for *both* clients' tokens — recorded as a finding and fixed later as rework item R10
(one line: `"realmRoles": ["teacher"]` at `school-collab-realm.json:263`; the realm guard stayed
19/0 in that class).

### Constraints honored

- **Anti-logging (gate P2 constraint):** no auth-path type logs or embeds token / refresh-token /
  `Authorization` header material — exception text names *what* failed, never *the credential*;
  `BearerTokenDelegatingHandler.cs` closes its remarks with the no-logging rule explicitly.
- **No `http(s)://` C# literal under `src/Mobile`** — the grep is clean (D8; the guard's
  `excludedHosts` means CI cannot catch these, hence the manual grep).
- No `ci.yml` touch; strict JSON in the realm file; CPM-clean (no `Version=` on any
  `PackageReference`).

---

## Review

The full adjudication chain, recorded in order (reviewer: kimi-k2.7-code throughout; the
4940-line whole-patch run was split into three parallel slices per the skill's ~3k-line
calibration):

1. **Plan gate run 1 → REVISE** (2 P1 + 4 P2): **P1-1** an orphan `<intent-filter>` in the MAUI
   manifest — invalid, because the activity is attribute-generated (`AndroidManifest.xml:3` has
   no `<activity>` element; `MainActivity` is generated from the `[Activity]` attribute) —
   resolved by the attribute-based `[IntentFilter]` wiring now at `MainActivity.cs:24`;
   **P1-2** `builder.Environment` does not exist on `MauiAppBuilder` — resolved by the
   `DOTNET_ENVIRONMENT` env-var check inside `AddAppSettings` (`MauiProgram.cs:25`). Plus four
   P2s: the 8-client `MobileApiOptions` name mapping (single resolvable name, "SettingsApi"), a
   test-inventory mismatch (`MobileAuthOptionsTests` added to W5), the custom-scheme mitigation
   (PKCE + `state`) made explicit with a mandatory code comment, and an anti-logging constraint
   (no token material in logs or exception messages).
2. **Plan gate run 2 (revised sections) → ACCEPT** — one residual citation P2, since fixed:
   the `Resolve` throw is at `MobileApiOptions.cs:76-79`, not `:81-89` (`:81`+ is
   `ToBaseAddress`). Corrected at both occurrence sites.
3. **Worker run** (deepseek-v4.1-flash) implemented W1–W5 and, beyond the plan, ran a real
   Keycloak container to prove the credential end-to-end: realm import clean; a full
   **Authorization Code + PKCE (S256)** sign-in accepted with
   `client_id=school-collab-mobile-client`, `redirect_uri=schoolcollab://signin-oidc`; the real
   token carried `iss`, **`aud="school-collab-client"`**, `azp`, `tenant_id`, `tenant_name`,
   `tenant_type`, `teacher_id`, `roles` (after a runtime grant), `scope`, `exp−iat=300`, plus a
   refresh-token round trip with rotation. It also **found** that the seeded `dev-teacher` held
   no `realmRoles`, so `roles` was absent for *both* clients — fixed later as R10.
4. **Three parallel slice reviews** (kimi-k2.7-code, the 4940-line whole-patch review split
   per the skill's ~3k calibration):
   - **A (realm + guard + CPM + Logic): 2 P1 + 4 P2.**
   - **B (head + platform): 0 P1, 3 P2** — raw platform failures escaping the typed path; no
     ubuntu coverage for the two platform classes; no `SecureStorage` failure handling.
   - **C (tests + round doc): 0 P1, 4 P2** — the CPM whole-file diff; the 33-vs-32
     expected-file discrepancy; no test that the *token* client lacks the bearer handler; a
     missing CPM comment.
5. **Parent adjudication:** **P1-A1 confirmed** — a CRLF normalization had rewritten all 160
   lines of `Directory.Packages.props`; whitespace-ignored it was 8 insertions — fixed in R1.
   **P1-A2 demoted to P2** — no NU1605 ever fired (an inconsistency, not a failure) — fixed in
   R2 by aligning `Microsoft.Extensions.DependencyInjection` to **10.0.11** (provenance comment
   added).
6. **Rework pass 1 (R1–R6):** R1 minimal-diff/EOL; R2 version alignment with a provenance
   comment; R3 fresh anonymous `AuthenticationState` per call; R4 platform failures normalised
   to `SignInFailedException` with the type name only; R5 `SecureStorage` read degrades to "no
   session" / write surfaces `TokenStoreException`; R6 a discriminating test that the named
   token client sends **no** `Authorization` header (proven by a temporary, hash-verified probe,
   then reverted).
7. **Rework pass 2 (R7–R8):** R7 (security-relevant, worker-found) `TokenSession.ClearAsync`
   now drops the **in-memory** session *before* sweeping the store and returns the first
   refusal instead of throwing (`TokenSession.cs:348`), so a refused clear can no longer abort
   sign-out with a live session; the write path **discards** the session when the store refuses
   a write (never memory-only). R8 a store failure degrades to a single unauthenticated send and
   surfaces in the spike's message bar, not the `ErrorBoundary`.
8. **Re-verify (static, on the rework delta) → ACCEPT** — all of R1–R8 confirmed **in the
   code**; both new tests judged genuinely discriminating; anti-logging clean; one residual P2
   (transport failures still escaping the handler).
9. **Final pass (R9–R10):** **R9** closes the session/transport boundary — `HttpRequestException`
   / `TimeoutException` / `IOException` degrade to the unauthenticated send, and (an
   empirically-measured finding: an `HttpClient`'s own timeout surfaces as
   `TaskCanceledException` with an inner `TimeoutException`, never a bare `TimeoutException`) a
   **guarded** `catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)`
   was added so genuine caller cancellation still propagates — the catch clauses live at
   `BearerTokenDelegatingHandler.cs:58,64,72,85`, with the timeout/cancellation reasoning
   documented in the handler's remarks. Four temporary probes (all reverted, handler restored
   and hash-verified) proved each clause discriminating — including one proving the cancellation
   guard is **not vacuous**. **R10** gave the seeded `dev-teacher` `"realmRoles": ["teacher"]`
   (one line, strict JSON; `roles.realm` already declares `teacher` —
   `school-collab-realm.json:263`); the realm guard stays green (19/0 in that class).

**Gate-adjudicated (not owner-pinned), carried unchanged:** OD-1 refresh-on-demand with the
no-401-resend boundary; OD-4 the registration code in `Mobile.Logic`. **Owner pins (not
re-opened):** the realm-guard widening (negative halves survive); L3.3 OPEN — owner-run pending;
the real-token-path dev posture with `FEATURE:DisableOIDCAuth` OFF, never committed as a
default.

---

## Acceptance

**Written by the orchestrator-accept run (2026-10-13) on the parent's authoritative numbers —
these are the only figures this section cites; the parent's post-rework pass (2026-10-13,
after the UI-tester rework R11–R15) re-took them, and both passes are recorded below.** The frozen
patch is `documents/rounds/diffs-mobile-credential.patch` — re-frozen after the UI rework:
**34 files, 5839 lines, 20 new, self-reference 0** (the worker-time freeze it supersedes was
33 files, 5313 lines), taken from branch `feat/mobile-credential` on base `6cb415ec`.

### Authoritative build/test numbers

| Check | Result |
|---|---|
| `dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj -f net10.0-android` | **0 errors, 0 warnings** |
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| Mobile.Tests.Unit | **83 / 0** (78 pre-rework; R11 added five exchange-leg cases) |
| ArchitectureTests.Unit | **111 / 0** |
| Core.Tests.Unit | **126 / 0** |
| Admin.Tests.Unit | **651 / 0** |
| Students.Tests.Unit | **624 / 0** |
| Assignments.Tests.Unit | **1118 / 0** |
| `src/Mobile` `http(s)://` C# literal grep | **clean** |

**Numbers provenance:** the figures above are the **parent's post-rework pass** (2026-10-13, after
R11–R15): android **0 errors / 0 warnings**, root `SchoolCollab.slnx` **0 errors**,
`Mobile.Tests.Unit` **83/0**, `ArchitectureTests.Unit` **111/0**; the other suites are unchanged
from the accept pass (Core 126, Admin 651, Students 624, Assignments 1118 — all /0), and
`Directory.Packages.props` is still a 16-line insertion diff.

### §8 L3 criteria — the spine

- **L3.1 — realm client + the guard: MET.** The realm JSON carries
  `school-collab-mobile-client` (`school-collab-realm.json:142-…`: public client, no secret,
  `schoolcollab://signin-oidc` redirect, the six mappers verbatim with the resource-audience
  mapper) and the amended guard passes — `AppHostRealmImportArchitectureTests` runs within
  ArchitectureTests.Unit's **111/0**; the exact-set assertion was widened deliberately by
  owner pin 1 and the negative halves survive. Beyond the suite, the **real container import
  completed clean** (worker run, above).
- **L3.2 — claims `tenant_id`/`teacher_id`/`roles`: MET**, on mechanism and on the real IdP.
  The whole-payload evidence comes from the worker's real PKCE run (`iss`, `aud=
  "school-collab-client"`, `azp`, `tenant_id`, `tenant_name`, `tenant_type`, `teacher_id`,
  `roles`, `scope`, `exp−iat=300`, plus a rotated refresh round trip) and the 83-test
  Mobile.Tests.Unit suite (78 pre-rework; R11 added five exchange-leg cases) asserts the same claim
  spellings against fixtures (no live Keycloak in
  unit tests). R10's `"realmRoles": ["teacher"]` seed (`school-collab-realm.json:263`) is what
  makes `roles` **true** for the seeded dev user on both clients — before R10 the real token
  honestly lacked it.
- **L3.3 — the device/emulator 200-OK with a server-side log line: **OPEN — OWNER-RUN PENDING**
  (owner pin 2, 2026-10-13); it is never silently marked met.** The precise boundary: everything
  short of a **device/emulator** is proven — the real IdP issued and rotated the token in a real
  container run, the bearer handler and session are 83 test-green, and the android TFM builds
  0/0 — but **no authenticated call was made from a device or emulator to an API**, so no
  200-OK with a server-side log line exists. Desktop-host runs do **not** satisfy this criterion
  (OD-2's own rule: Windows-desktop-only or fake evidence never satisfies it). Follow-up: the
  worker's written device procedure (plan W6) — standalone Keycloak container with the realm
  bind-mounted, the three APIs on their committed launch-profile URLs, then the device/emulator
  sign-in and one authenticated call, verified by the server-side log line.
- **L3.4 — no base-address literal: MET.** The mechanical grep over `src/Mobile` `C#` sources is
  clean. Why CI's guard cannot catch it: `CrossModuleWiringTests`' `excludedHosts` set
  (`CrossModuleWiringTests.cs:300-302` — `localhost`, `host.docker.internal`, `0.0.0.0`,
  `example.com`, `example.org`) silently skips matches, so the diff-review grep — re-run clean —
  is the enforcement of record.
- **L3.5 — `AuthenticationStateProvider` yields an authenticated principal and `TenantGate`
  renders its tenant branch: MET to the extent the code and tests prove it.** The ubuntu-
  mechanical half is proven: `MobileAuthenticationStateProviderTests` asserts the authenticated
  principal with raw `tenant_id`/`roles` spellings — the exact contract `VisibleTenantService
  .GetScopeAsync()` reads (`VisibleTenantService.cs:39`) — and the provider notifies on every
  session change so `GateBase`'s subscription re-evaluates; `Spike.razor` wires the `<TenantGate>`
  whose child renders `tenant_name`/`teacher_id`. Be explicit: **the device-rendered half (the
  flip actually seen on screen) rides on L3.3** — it is owner-run pending by the same pin.
  Post-rework, this criterion additionally covers the **reworked failure paths of the auth UI**
  (R11–R15, `## UI Tester`): the exchange leg's transport failures normalise into the typed
  `SignInFailedException`, so the page's `<ErrorBoundary>` can never replace the sign-in surface;
  the boundary copy names the page; a user-cancelled sign-in renders non-error with a matching
  title; `<TenantGate>` carries an explicit head-side `<Fallback>`; the button carries `aria-busy`.
  Run 2 of the UI tester re-verified these and found **no new user-visible defect** — verdict: the
  delivered UI is **acceptable as it stands**. R11 is unit-proven (five new cases,
  `OidcLoginServiceTests.cs:150-217`); R12–R15 are proven by the android compile plus inspection
  only — the head has no test project and no bUnit harness for `Spike.razor` (residual 8).

### Pinned-decision compliance

- **Realm-guard widening** — applied exactly as owner pin 1 approved (names the mobile client;
  negative halves intact).
- **L3.3 OPEN** — carried per owner pin 2 (above); never marked met.
- **Dev-loop posture** — the real token path, `FEATURE:DisableOIDCAuth` OFF (owner pin 3); all
  evidence this round is from the real credential path; the flag posture stays documented and
  was never committed as a default (no host `appsettings.json` was touched).
- **OD-1 (gate-adjudicated)** — refresh-on-demand with the no-401-resend boundary, encoded in
  `TokenSession`/`BearerTokenDelegatingHandler` and its remarks.
- **OD-4 (gate-adjudicated)** — the registration code lives in `Mobile.Logic`
  (`MobileClientRegistrationExtensions.cs`).

### Deviations

1. **An unplanned CPM entry:** `Microsoft.Extensions.DependencyInjection` entered
   `Directory.Packages.props` (not in the plan's W2 item 10 list) — it was added by rework R2's
   version alignment (review P1-A2, demoted to P2: no NU1605 ever fired) and is now **declared**
   in this section, with a provenance comment in the file.
2. **Expected-file list 33 vs the patch's 32 code files:** the conditional
   `AndroidManifest.xml` `<queries>` change was never needed (the attribute-based
   `[IntentFilter]`, `MainActivity.cs:24`, proved sufficient), so the planned conditional 33rd
   file never materialized. The patch's 33 diff entries are those 32 files plus the round doc
   itself (new file at freeze time).

### Residuals (non-blocking; each with its follow-up)

1. **L3.3** — the owner-run device/emulator leg (procedure: plan W6). The only open criterion.
2. **iOS/MacCatalyst TFMs unbuilt** and the **Windows desktop sign-in leg unverified** — only
   `net10.0-android` and the root solution were built. Follow-up: owner-run TFM builds before
   any iOS/desktop claim.
3. **Nothing pins the seeded user's `realmRoles`** — R10's `"realmRoles": ["teacher"]` is realm
   data, not guarded shape; the guard asserts the `roles` *mapper*, not the seed's membership.
   Follow-up: if a future realm edit can silently drop `roles` again, consider a guard assertion
   (owner call).
4. **The named token client has no explicit `Timeout`** (100 s default) — the R9 guarded timeout
   clause depends on the default firing; the 100 s wall is not a stated posture. Follow-up: pin
   an explicit timeout on the `"oidc-token"` client if the wall matters.
5. **The head has no sign-out control** — R7's refused-clear ordering is exercised at
   session/API level in tests, not from UI. Fold-in from the UI-tester adjudication (P2-D, left
   with no action by the parent): the page's local claim fields (`Spike.razor:107-108`) would go
   stale after a session change the page did not itself run, but today nothing can change the
   session behind the page's back. Follow-up: the sign-out UI arrives with the logout slice
   (scope-out this round).
6. **Keystore failures are fake-driven only** — `SecureStorageTokenStore`'s refusal paths are
   tested behind `ITokenStore` fakes; a real device keystore failure is unobserved. Follow-up:
   covered naturally by the L3.3 owner leg if a failure occurs.
7. **Android's handler mapping of DNS/socket/TLS failures onto `HttpRequestException` is
   recalled, not measured** — the R9 degradation is proven for the exception types as thrown;
   that the Android stack delivers network failures as `HttpRequestException` is platform
   knowledge, not an empirically measured fact on this codebase. Follow-up: observe in the owner
   leg if a network failure occurs.
8. **R12–R15 have no automated coverage** — the head has no test project and no bUnit harness
   for `Spike.razor`, so the boundary-copy generalisation (R12), the cancelled-vs-failed
   rendering (R13), the explicit `<Fallback>` (R14) and the `aria-busy` flag (R15) are proven by
   the android TFM compile plus inspection, and by run 2 of the UI tester — not by a suite.
   Follow-up: if the head gains a test surface (bUnit or otherwise), port R12–R15's assertions
   there.
9. **R11's typed message is one fixed string** (`UnreachableTokenEndpointMessage`) — it does not
   distinguish "unreachable endpoint" from "client deadline elapsed" and carries no inner
   exception (nothing leaks — the anti-logging constraint holds). Follow-up: split the message
   only if a support need arises; the single string is the simpler shape under the constraint.
10. **A malformed stored token signs the user out silently** — `MobileAuthenticationStateProvider`
    catches `FormatException` (`MobileAuthenticationStateProvider.cs:60-64`) and returns the
    anonymous state with no signal to the user or to diagnostics. Follow-up: a surface (a banner
    or a log) when the logout slice exists.
11. **The browser leg does not discriminate a platform cancellation from a user dismissal** —
    `SystemBrowserAuthenticator` (`SystemBrowserAuthenticator.cs:37-40`) catches
    `OperationCanceledException` unconditionally and maps it to `SignInCancelledException`, so a
    platform-raised `TaskCanceledException` — a genuine failure, not the user closing the
    browser — surfaces as **"cancelled"** (the Info presentation) rather than "failed". This is
    the mirror image of the exchange leg, which R11 taught to discriminate:
    `OidcLoginService.ExchangeCodeAsync` normalises only its own deadline and rethrows caller
    cancellation via `when (!cancellationToken.IsCancellationRequested)`. Follow-up: aligning the
    two legs (a guarded catch on the browser leg too) is a follow-up, not a defect this round
    introduced.

### Verdict

**CLOSED** — with **L3.3 carried OPEN by owner pin** (owner-run pending, procedure W6) and the
**eleven** residuals above accepted as non-blocking with named follow-ups. The ledger row exists
(four bounded rework passes recorded); the UI-tester stage is complete — run 2's verdict: the
delivered UI is **acceptable as it stands** (`## UI Tester`).

---

## UI Tester

**Dispatch scope — authoritative and verbatim for the tester (the deterministic trigger FIRES:
a real UI surface changed). The tester must not derive or widen its own scope.**

- **Changed UI surfaces (the only ones in scope):**
  `src/Mobile/SchoolCollab.Mobile/Components/Pages/Spike.razor` and
  `src/Mobile/SchoolCollab.Mobile/Components/Pages/Spike.razor.css`. Everything outside these
  two files is out of scope.
- **What they render:** a **sign-in button** with its **in-flight disabled state**
  (`FluentButton … Disabled="@_signingIn" OnClick="SignInAsync"`); the **typed error message
  bar** (`FluentMessageBar` `Intent="MessageIntent.Error"`, `Title="Sign-in failed"`, showing the
  sign-in failure's message — including `SignInCancelledException` and the normalised
  `SignInFailedException`); and a `<TenantGate>` whose child renders **`tenant_name`** and
  **`teacher_id`** via `FieldDisplay` once authenticated — the observable being the gate's flip
  from its **default fallback banner** (pre-sign-in) to the **tenant branch** (post-sign-in).
  The page route is `@page "/"` — the **navigation entry point is the `Spike` page (the app's
  root route)**.
- **Auth-state interaction they depend on:** `MobileAuthenticationStateProvider`
  (`SchoolCollab.Mobile.Logic`) notifying on session changes, plus the Logic session types
  (`TokenSession`, `OidcLoginService`, `TokenSessionExpiredException`, `SignInFailedException`,
  `SignInCancelledException`, `TokenStoreException`). The gate's read path is
  `VisibleTenantService`.GetScopeAsync() → the principal's **`tenant_id`** claim.
- **ApiClient methods involved: none directly** — this UI's path is the auth stack
  (sign-in → session → auth state → gate), not the module API clients.
- **Environment constraint, stated plainly: no device or emulator exists in this environment.**
  This is a **static adversarial pass over the UI behaviour and its failure paths** — markup,
  disabled-state transitions, error-surface rendering, gate flip inputs, and the CSS-isolation
  styles. **Anything device-only (actual sign-in navigation, real `WebAuthenticator` round
  trips, on-screen tenant values from a live token) is out of scope by construction** and is
  covered by the owner-run L3.3 leg instead.

### Run 1 — minimax-m3, static adversarial pass (2026-10-13)

**Verdict: FINDINGS — 3 P1 + 5 P2.** The pass ran over the two in-scope files, the auth-state
interaction and the failure paths, statically — no device/emulator exists in this environment
(the dispatch scope's constraint). The tester's out-of-round observations were recorded as the
parent's, not as tester findings. The findings as reported (run 1 cited them against the
pre-rework tree, which the rework below then replaced; each ref below locates the finding's
subject in the delivered tree):

- **P1-A** — a transport failure during the **code exchange**
  (`OidcLoginService.ExchangeCodeAsync`, delivered tree
  `src/Mobile/SchoolCollab.Mobile.Logic/OidcLoginService.cs:173`) escaped the page's catch list
  into the page's `<ErrorBoundary>`, replacing the whole page — including the sign-in button —
  with a misleading error bar. The R9 normalisation had covered the **refresh** leg
  (`BearerTokenDelegatingHandler`) but not the **exchange** leg.
- **P1-B** — the boundary's `ErrorContent` hard-coded "The Admin.Shared component failed to
  render" while the boundary wraps the auth section too — an auth-section render failure would
  be misattributed (`Spike.razor:83-87` delivered).
- **P1-C** — a **user-cancelled** sign-in was rendered red with the title "Sign-in failed",
  indistinguishable from a genuine failure.
- **P2-A** — no `aria-busy` on the sign-in button (`Spike.razor:51` delivered).
- **P2-B** — a dead `gap` rule: `.spike__auth-actions` carries `gap: 0.5rem` but holds a single
  button (`Spike.razor.css:30-33` delivered).
- **P2-C** — a theoretical init race between `OnInitializedAsync`'s claim refresh and a session
  notification, not constructible through the delivered flow.
- **P2-D** — stale local claim fields (`Spike.razor:107-108` delivered) after a session change
  the page did not itself run.
- **P2-E** — the inherited `TenantGate` fallback banner tells the user to use a "dev tenant
  switcher" that does not exist in this head (the shared `GateBase` default banner, rendered on
  every cold start pre-sign-in).

### Parent adjudication (2026-10-13)

The parent is the acceptance authority; the tester does not self-confirm its findings.

- **P1-A and P1-B accepted** — same root cause: the exchange leg never got the normalisation the
  refresh leg got.
- **P1-C demoted to P2** by the parent — a real presentation wrong, nothing broken. **Demoted,
  not withdrawn**: the finding stood and its fix was carried; only its severity changed.
- **P2-A and P2-E accepted**; **P2-B and P2-C left with no action** (cosmetic / non-constructible);
  **P2-D recorded as a residual** — no sign-out control exists yet, so no session change can occur
  behind the page's back today (folded into `## Acceptance` residual 5).

### UI rework (R11–R15) — the deterministic implementation of the adjudicated set

- **R11** (P1-A, and P1-B's root cause) — `ExchangeCodeAsync` normalises the exchange leg's
  transport failures (`HttpRequestException`/`TimeoutException`/`IOException`, plus a **guarded**
  `TaskCanceledException` — `when (!cancellationToken.IsCancellationRequested)` — so genuine
  caller cancellation still propagates) into the typed `SignInFailedException` the message bar
  already owns (`OidcLoginService.cs:209-233`). Nothing escapes to the `<ErrorBoundary>`.
  R11 carries **5 new test cases**
  (`tests/SchoolCollab.Mobile.Tests.Unit/OidcLoginServiceTests.cs:150-217`): three transport
  shapes (`RequestFailed`/`TimedOut`/`ReadFailed`, `:151-153`), one client-timeout leg — the
  token client's own `HttpClient.Timeout`, which measured on this stack surfaces as a
  `TaskCanceledException`, never a bare `TimeoutException` (`:175`) — and one caller-cancellation
  case that **must still propagate** as `OperationCanceledException` (`:194`). Discrimination was
  proven with temporary probes, then reverted; no probe code remains in the delivered tests.
- **R12** (P1-B) — the boundary's `ErrorContent` text generalised to the page ("The reuse spike
  page failed to render", `Spike.razor:83-87`).
- **R13** (P1-C) — the message bar's intent and title follow the **outcome**, not the fact that
  something was reported (`Spike.razor:104-105`, reset to the error presentation on every
  attempt at `:114-118`): a `SignInCancelledException` renders `MessageIntent.Info` with the
  title "Sign-in cancelled" (`Spike.razor:125-132`); every genuine failure stays red.
- **R14** (P2-E) — `<TenantGate>` gains an explicit head-side `<Fallback>` (`Spike.razor:74-79`)
  whose copy matches this head, instead of the shared `GateBase` default that names a dev tenant
  switcher this head does not have.
- **R15** (P2-A) — `aria-busy="@_signingIn"` on the sign-in button (`Spike.razor:51`), the
  screen-reader half of the disabled state.

### Run 2 — minimax-m3, re-verification only (2026-10-13)

With R11's five cases, `Mobile.Tests.Unit` is now **83/83**; R12–R15 sit outside the unit suite —
the head has no test project and no bUnit harness for `Spike.razor` (`## Acceptance` residual 8).
Scoped to verifying the rework, not a second full hunt. Per-finding verdicts on run 1's set:

| Finding | Verdict |
|---|---|
| P1-A (exchange-leg transport leak into the boundary) | **Resolved** — R11 normalises it into the typed failure; a transport failure can no longer reach the boundary |
| P1-B (boundary copy misattributed) | **Resolved** — R12 names the page |
| P1-C (cancelled rendered red as "Sign-in failed") | **Resolved** — correctly re-classed by the parent at adjudication; R13 renders the cancellation with a non-error intent and the matching title |
| P2-A (no `aria-busy`) | **Resolved** — R15 |
| P2-E (fallback banner names a non-existent switcher) | **Resolved** — R14 |
| P2-B (dead `gap` rule), P2-C (theoretical init race) | **Left with no action**, as adjudicated |
| P2-D (stale claim fields after a session change) | **Residual** — folded into `## Acceptance` residual 5 |

**No new user-visible defect** on the in-scope surfaces: the sign-in button cannot stick disabled
(`finally` covers every exit, `Spike.razor:145-148`), the success path still refreshes the claims
(`Spike.razor:122-123` → `:162-163`), genuine failures still render red (the intent/title reset on
every attempt), and the reuse section's boundary still catches a real render failure.

**Verdict: the delivered UI is acceptable as it stands.**