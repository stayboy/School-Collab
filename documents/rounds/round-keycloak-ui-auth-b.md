Provider: pi, full Tier 3, Option A (models: glm-5.3-flash orchestrator, glm-5.3 plan-review, deepseek-v4.1-flash worker, kimi-k2.7-code reviewer, kimi-k2.7-code escalator, minimax-m3 tester) | base: eb651ba8604c1b56aee8a3b32a5adbc7cebf6581 | spec: documents/specs/keycloak-ui-auth-integration.md | round A: documents/rounds/round-keycloak-ui-auth.md

## Plan

Round B of a split pair. Round A (accepted) landed the Keycloak realm additions, the
`SchoolCollab.Auth` service skeleton + registration, the Direct-Grant exchange, the one-time-code
store, portal-session custody, the claim factory, the Admin REST client, the audit log, the
exchange/redeem/session/admin endpoint groups with the rate limiter and the `user-admin` gate, and
the `ClaimTypes.Role` pin on both handlers. Round B delivers the original passes 4-5: the prefab
auth portal, the Blazor hosts' challenge/login/logout plumbing, the flag fan-out, and the two
live P1 designs — planned fresh from A's landed state and from the **owner's 2026-09-22 spec
amendment** (D12 amended, D15-D18 new, AC11/AC12 new, prefab-form gate §16.1 RESOLVED), which
**supersedes** round A's `### Deferred to round B` section where they conflict.

### Goal

Make the UI end real: (1) an unauthenticated request to a gated Blazor page lands on **Keycloak's
hosted page** when the login-UI flag is OFF and on the **prefab auth portal's login form** when it
is ON — chosen at startup by a policy scheme (P1-2 design below); (2) the portal form exchanges
credentials through the auth service and completes the D6 one-time-code handshake so the Blazor
host signs in with the **same claim set** the OIDC path produces; (3) the passkey path (D16) makes
the **auth service** the OIDC relying party — the portal gets no OIDC client and no tokens, only a
single-use bootstrap code; (4) the portal holds **no credential of any kind** (AC11): all
token-bearing reads are mediated (D17), the session read returns claims as data, and a failed
refresh yields a distinct `session ended` status (D18); (5) the prefab admin UI creates tenant
users bound to a tenant + teacher row, assigns realm roles and edits claim attributes through the
auth service.

**Supersession note (spec §5.1 step 4 — plan-review P2-1).** The spec's flag-OFF narrative line
"the auth portal's own login route uses the same code flow with the portal as the client audience"
is **superseded by D16**: the portal has **no OIDC client and no redirect URI in the realm**, and
its own login is the **prefab form + `/auth/exchange` path under BOTH flag states**. D4 (the UI
toggle) governs only which login UI the **Blazor apps** present; the portal's password login is
always the custom form, and its passkey login is always the D16 handoff with the auth service as
the relying party.

### Scope IN

- `src/SchoolCollab.Core/` — `FeatureFlagKeys.DisableKeycloakLoginUi`; the challenge-path policy
  scheme + portal-redirect challenge handler in `AuthTenancyExtensions.cs` (+1 new handler file);
  the shared Blazor handshake redemption extension (base address taken as a **parameter** — P1-1).
- `src/AppHost/SchoolCollab.AppHost/` — the new flag parameter + fan-out to four consumers; the
  auth-portal `AddUvicornApp` registration (mirroring `Program.cs:408-411`); `WithReference(auth)`
  on admin + families; `WithReference(settingsApi/studentsApi)` on the auth service; realm
  redirect-URI additions for the auth-service RP (both dev schemes); the app-callback allowlist
  fan-out; `documents/configuration.md` touches in the same change sets.
- `src/SchoolCollab.Auth/` — the D15 provider seam (one interface, one Keycloak implementation);
  the passkey RP + bootstrap-code flow (D16); the per-app callback allowlist enforced at code
  issuance on BOTH paths (plan-review P1-3); the mediated picker reads (D17); the session
  lifecycle changes (D18) including the superseding of round A's refresh-token-return contract
  (owner-adjudicated, see Open items).
- `src/SchoolCollab.AuthPortal/` (**new** Python project mirroring `src/SchoolCollab.Portals`) +
  its pytest suite, slnx `File` items, and the extended portal solution-items guard.
- `src/SchoolCollab.Admin/Program.cs`, `src/SchoolCollab.Families/Program.cs` — handshake
  callback, login/logout endpoints, typed redeem client.
- Tests: `SchoolCollab.Core.Tests.Unit`, `SchoolCollab.Auth.Tests.Unit`,
  `SchoolCollab.ArchitectureTests.Unit`, the new portal's pytest suite.

### Scope OUT

- No changes to the settings/students/assignments bounded contexts (the mediated reads consume
  their existing read-only registries over HTTP — zero context code changes).
- `src/SchoolCollab.Portals` (ward/teacher) does not adopt the auth portal.
- No OIDC back-channel logout (D13 deferral); no runtime realm-role CRUD (D11); no
  `DisableOIDCAuth`/`TestAuthHandler` changes; no issuer-model upgrade path (D17 records it as
  future direction only); no MCP host and no prefab-form re-verification spike (§16.1 resolved —
  prefab-ui 0.20.2 has full form/input support and `Fetch.post` REST wiring).
- No Keycloak image/realm-role changes beyond the redirect-URI additions (round A landed the rest).
- No dedicated `school-collab-auth-rp` client (owner adjudication 2: the RP reuses
  `school-collab-client`; a dedicated client is a later additive realm/config migration).
- No `Directory.Packages.props` changes expected (mediated reads are typed `HttpClient` via
  `IHttpClientFactory` — framework-provided). No commit/push/gh at any point.

### Expected files (reviewer conformance baseline) — round B

**Pass B1 — flag key + challenge-path interception (Core only)**

| Pass | Path | N/M | Purpose |
|---|---|---|---|
| B1 | `src/SchoolCollab.Core/Features/FeatureFlagKeys.cs` | M | add `DisableKeycloakLoginUi` (precedent `DisableOIDCAuth` :20, `RequireAssignmentApproval` :61) |
| B1 | `src/SchoolCollab.Core/Auth/AuthTenancyExtensions.cs` | M | register an `AddPolicyScheme` as `DefaultChallengeScheme` (:87); startup flag selects the forward-challenge target (P1-2 design, below) |
| B1 | `src/SchoolCollab.Core/Auth/PortalRedirectChallengeHandler.cs` | **N** | the flag-ON challenge handler: 302 → `{Auth:Portal:LoginUrl}?return_uri=<request-derived handshake callback>`; **401 fail-closed** when the portal login URL is not configured |
| B1 | `tests/SchoolCollab.Core.Tests.Unit/Auth/AuthWiringTests.cs` | M | policy scheme registered; per-flag forwarding target; OIDC branch unchanged when OFF |
| B1 | `tests/SchoolCollab.Core.Tests.Unit/Auth/PortalRedirectChallengeHandlerTests.cs` | **N** | 302 shape incl. `return_uri`; 401 fallback |

**Pass B2 — auth-portal skeleton (Python, mirrors `src/SchoolCollab.Portals`)**

| Pass | Path | N/M | Purpose |
|---|---|---|---|
| B2 | `src/SchoolCollab.AuthPortal/pyproject.toml` | **N** | uv project mirroring the portals `pyproject.toml` (fastapi, uvicorn, prefab-ui 0.20.2) |
| B2 | `src/SchoolCollab.AuthPortal/app.py` | **N** | FastAPI app + health + route table (created here; 2nd touch B8, 3rd touch B9) |
| B2 | `src/SchoolCollab.AuthPortal/api/__init__.py` | **N** | package marker — the `SchoolCollab.Portals` shape carries it (portals guard reviewed-set pin) |
| B2 | `src/SchoolCollab.AuthPortal/api/auth_api_client.py` | **N** | typed auth-service client: exchange, bootstrap redeem, session read/revoke — **no Keycloak calls, no token fields** (AC11) |
| B2 | `src/SchoolCollab.AuthPortal/api/dto.py` | **N** | request/response DTOs |
| B2 | `src/SchoolCollab.AuthPortal/api/errors.py` | **N** | typed error taxonomy → error-card views (AC10) |
| B2 | `src/SchoolCollab.AuthPortal/uv.lock` | **N** | lockfile — the portal guard's `IsPortalSourceFile` counts `.lock` (`PortalsSolutionItemsArchitectureTests.cs:137`), so the slnx needs the `File` item |
| B2 | `SchoolCollab.slnx` | M | `File` items + `Folder` nodes under `/src/SchoolCollab.AuthPortal/` (`.slnx` has no globbing) |
| B2 | `tests/SchoolCollab.ArchitectureTests.Unit/PortalsSolutionItemsArchitectureTests.cs` | M | generalize the portal-folder walk to include `src/SchoolCollab.AuthPortal/` (the guard is today hardcoded to `src/SchoolCollab.Portals`, prefix :31, walk :124) while keeping the Portals reviewed-set pin untouched |
| B2 | `src/SchoolCollab.AuthPortal/tests/test_app_routes.py` | **N** | route + degraded-state coverage (the portals `dependency_overrides` pattern; `tests/` carries no `__init__.py` — mirrors the portals set) |
| B2 | `src/SchoolCollab.AuthPortal/tests/test_auth_api_client.py` | **N** | client request shapes; **asserts no token-shaped field exists on any DTO** (AC11 axis) |

**Pass B3 — AppHost wiring: flag parameter, fan-out, portal registration, transport references**

| Pass | Path | N/M | Purpose |
|---|---|---|---|
| B3 | `src/AppHost/SchoolCollab.AppHost/appsettings.json` | M | `Parameters:` key `feature-flag-disable-keycloak-login-ui` (precedent :24) |
| B3 | `src/AppHost/SchoolCollab.AppHost/Program.cs` | M | read the flag parameter; fan-out **pinned per consumer** (plan-review P2-5): `Auth:Portal:LoginUrl` → **admin + families only** (browser-facing; must NOT reach the auth service — the B1 401-fail-closed story requires it absent there); `Auth:Portal:BootstrapRedirectUrl` → **auth only** (server-side D16 redirect target); `FeatureFlags__FEATURE__DisableKeycloakLoginUi` → all four (admin, families, auth, auth-portal — precedents :296, :376); `AddUvicornApp("auth-portal", …)` + `WithUv()` + `WithReference(auth)` (**cannot** use `WireKeycloakAuth` — typed `ProjectResource` :107); `.WithReference(auth)` on admin (:370) + families (:395) for the handshake transport; `.WithReference(settingsApi)` + `.WithReference(studentsApi)` on the auth resource (:364) for the mediated reads; the portal's public base URL env from `authPortal.GetEndpoint("http")` (owner-adjudicated pattern) |
| B3 | `tests/SchoolCollab.ArchitectureTests.Unit/AppHostLoginUiFlagWiringArchitectureTests.cs` | **N** | discriminating guard: flag fan-out reaches exactly the four consumers; `Auth:Portal:LoginUrl` is fanned to admin/families and **not** to auth; the auth-portal registration uses `AddUvicornApp` + `WithUv`; admin + families carry `.WithReference(auth)` |
| B3 | `documents/configuration.md` | M | §2 parameter row + §11 env-var rows (flag ×4 consumers; `Auth:Portal:LoginUrl` ×2 Blazor hosts; `Auth:Portal:BootstrapRedirectUrl` ×auth) — same change set |

**Pass B4 — Blazor hosts: handshake redemption, login/logout, typed redeem client**

| Pass | Path | N/M | Purpose |
|---|---|---|---|
| B4 | `src/SchoolCollab.Core/Auth/PortalHandshakeExtensions.cs` | **N** | shared by both hosts: `AddPortalHandshake(this IServiceCollection, string baseAddress, …)` — the base address is a **parameter, not a config-bound value** (plan-review P1-1: a literal inside Core fails `CrossModuleWiringTests` for the four other Core consumers — students-api/settings-api/settings-ai/assignments-api; a config-bound value is invisible to the guard and dies at runtime); the typed redeem `HttpClient` mirrors `AddCrossModuleHttpClient` (`FamiliesModuleServices.cs:17` literal-at-call-site pattern) — each host passes the literal `"https+http://auth"`; plus the `/signin-handshake` redemption endpoint group and the flag-ON login/logout redirect endpoints (grouped, never inline) |
| B4 | `src/SchoolCollab.Admin/Program.cs` | M | call `AddPortalHandshake("https+http://auth")` + map the handshake group (mirror of the :97-100 conditional gate) |
| B4 | `src/SchoolCollab.Families/Program.cs` | M | same (mirror of :136-139) |
| B4 | `tests/SchoolCollab.Core.Tests.Unit/Auth/PortalHandshakeTests.cs` | **N** | redeem → sign-in cookie carrying the claim set incl. roles; replay/TTL rejected; logout → OIDC SignOut |

**Pass B5 — auth service: D15 provider seam + D18 session lifecycle + portal-session scheme**

| Pass | Path | N/M | Purpose |
|---|---|---|---|
| B5 | `src/SchoolCollab.Auth/Providers/IAuthProvider.cs` | **N** | the ONE portal-facing seam (D15): authenticate, session create/read/revoke, claims, refresh, logout |
| B5 | `src/SchoolCollab.Auth/Providers/KeycloakAuthProvider.cs` | **N** | the single implementation, delegating to round A's `DirectGrantExchanger` / `PortalSessionStore` / `ClaimSetFactory` / `KeycloakAdminClient` — no speculative abstraction |
| B5 | `src/SchoolCollab.Auth/Auth/PortalSessionAuthenticationHandler.cs` | **N** | converts a portal-presented session id into a principal via `PortalSessionStore` (D12 server-side validation) so portal-facing endpoints authenticate without the portal ever holding a token |
| B5 | `src/SchoolCollab.Auth/Endpoints/SessionEndpoints.cs` | M | session read returns the claim set **as data**; transparent in-custody refresh; distinct `session ended` status on failed refresh (D18); `DELETE` now performs the Keycloak refresh-token revocation **server-side** and returns the built `end_session` URL — **no refresh token in any response** (owner-adjudicated supersession of round A's AC9-exemption contract per the amended AC11) |
| B5 | `src/SchoolCollab.Auth/AuthServiceExtensions.cs` | M (1st of 4) | register the provider, the session-auth scheme, the updated session services |
| B5 | `src/SchoolCollab.Auth/Endpoints/AuthEndpointGroup.cs` | M (1st of 2) | attach the portal-session policy to the portal-facing groups (bearer `user-admin` gate untouched) |
| B5 | `tests/SchoolCollab.Auth.Tests.Unit/KeycloakAuthProviderTests.cs` | **N** | every `IAuthProvider` operation routes through the Keycloak implementation; failure taxonomy preserved |
| B5 | `tests/SchoolCollab.Auth.Tests.Unit/PortalSessionAuthenticationHandlerTests.cs` | **N** | valid session → principal with the claim set; unknown/expired session → rejected |
| B5 | `tests/SchoolCollab.Auth.Tests.Unit/SessionEndpointTests.cs` | M | session-ended status distinct from 404; **raw-body scan: no `refresh_token`/`access_token`/`id_token` in ANY response, including DELETE** |

**Pass B5b — per-app callback allowlist (plan-review P1-3 — enforced at code issuance)**

| Pass | Path | N/M | Purpose |
|---|---|---|---|
| B5b | `src/SchoolCollab.Auth/Options/AuthServiceOptions.cs` | M | add `AppCallbackPrefixes` (semicolon-separated exact-prefix list; validated non-empty at `ValidateOnStart`) — dev values: the four Blazor handshake callbacks (`http/https://localhost:{5300,7300,5400,7400}/signin-handshake`) + the portal's bootstrap redemption URI |
| B5b | `src/SchoolCollab.Auth/Services/AppCallbackAllowlist.cs` | **N** | fail-closed prefix matcher (scheme + host + port + path); behavioural class → tested |
| B5b | `src/SchoolCollab.Auth/Endpoints/ExchangeEndpoints.cs` | M | `/auth/exchange` **rejects a `RedirectUri` not matching the allowlist before any code is issued** (fail-closed status in the failure taxonomy; the crafted-`return_uri` attack cannot yield a code) |
| B5b | `src/SchoolCollab.Auth/AuthServiceExtensions.cs` | M (2nd of 4) | register the allowlist + options extension |
| B5b | `src/AppHost/SchoolCollab.AppHost/Program.cs` | M (2nd touch) | fan `Auth:AppCallbackPrefixes` to **auth** and `AuthPortal:AppCallbackPrefixes` to the **portal** from one AppHost parameter |
| B5b | `tests/SchoolCollab.Auth.Tests.Unit/AppCallbackAllowlistTests.cs` | **N** | allowed prefix issues a code; attacker URI rejected (fail-closed); malformed input rejected; matching is exact-prefix (host+port+path), not a loose substring |
| B5b | `tests/SchoolCollab.Auth.Tests.Unit/ExchangeEndpointTests.cs` | M | non-allowlisted `RedirectUri` → rejected, **no code in the response** |
| B5b | `tests/SchoolCollab.Auth.Tests.Unit/AuthServiceOptionsTests.cs` | M | the `Valid()` fixture (`:16`) sets only the four Keycloak fields — add `AppCallbackPrefixes`; plus a `Validator_RejectsEmptyAppCallbackPrefixes` test naming `Auth:AppCallbackPrefixes` (otherwise `Validator_AcceptsCompleteConfiguration` at `:61-63` goes red) |
| B5b | `tests/SchoolCollab.Auth.Tests.Unit/AuthEndpointTestHost.cs` | M | the in-memory config dictionary (`:90`) lacks `Auth:AppCallbackPrefixes`, so host start fails the `ValidateOnStart` demand (`:86`) and red-lights every composed-host test — including B5b's own `ExchangeEndpointTests` M row |
| B5b | `tests/SchoolCollab.Auth.Tests.Unit/AuthServiceSmokeTests.cs` | M (**parent-amended mid-pass**) | a THIRD fixture of the same class the P1 amendment covered: `OptionsValidator_AcceptsCompleteConfiguration` (`:52`) builds a "complete configuration" from the four Keycloak fields only (`:41-50`), so the new required key red-lit it. Mechanical fixture parity — add `AppCallbackPrefixes`; the pass also swept the project for any other same-shaped fixture. |
| B5b | `documents/configuration.md` | M (2nd touch) | §4/§11 rows for the new options key + both consumer env names |

**Rationale (plan-review P1):** without the two test rows the worker hits a stop-and-report scope
violation mid-pass — the options change necessarily touches both files. The alternatives are not
viable: skipping `ValidateOnStart` for the new key breaks the startup-guard/tests-cannot-drift
contract, and a hardcoded non-empty default would make production fail-open.

**Pass B6 — auth service: D16 passkey RP + realm redirect URIs**

| Pass | Path | N/M | Purpose |
|---|---|---|---|
| B6 | `src/AppHost/SchoolCollab.AppHost/school-collab-realm.json` | M | add the auth service's fixed dev `/signin-oidc` redirect URIs **for both schemes** (launchSettings `applicationUrl: "https://localhost:55458;http://localhost:55459"` → `https://localhost:55458/signin-oidc` + `http://localhost:55459/signin-oidc`), matching round A's per-host both-scheme registration (`school-collab-realm.json:40-49`); **NO portal OIDC client and NO portal redirect URI anywhere in the realm** (D16) |
| B6 | `tests/SchoolCollab.ArchitectureTests.Unit/AppHostRealmImportArchitectureTests.cs` | M | assert the auth-RP redirect URIs are present (both schemes) AND that no client exists for the portal (the D16 negative invariant) |
| B6 | `src/SchoolCollab.Auth/Endpoints/PasskeyEndpoints.cs` | **N** | `GET /auth/passkey/login` (OIDC challenge; `app_redirect_uri` validated against the B5b allowlist before the challenge and **persisted with the OIDC pending state — the correlation cookie / authentication properties — across the ceremony**) + `GET /auth/passkey/complete` (**re-validates the persisted value against the allowlist before the D6 continuation code is issued; `/complete` never re-trusts a fresh query param**) → custody session created → single-use bootstrap code → 302 to the portal bootstrap URL (`Auth:Portal:BootstrapRedirectUrl`, B3's pin); on a sign-in that began at a Blazor page the D6 one-time code for the app's callback is issued from the same exchange. **Parent-amended: also hosts `POST /auth/bootstrap/redeem`** — the server-to-server redemption the portal client pins (B2): `{code, redirectUri}` -> `{sessionId, appRedirectUri?, appCode?}`, single-use/TTL/allowlist-bound, **no token in the body** (AC12). This endpoint appeared in NO pass's rows; B2's diff review flagged it as B6's to implement |
| B6 | — | **D13 carry, NOT in this pass (owner decision pending)** | the browser-facing logout redirect (302 to `end_session` carrying `id_token_hint` + `post_logout_redirect_uri` in the `Location` header, so the id token never enters a response body or Python state) and the realm registration of `school-collab-client`'s post-logout redirect URI. Gated on the owner's (ii)/(iii) choice; will be dispatched as a small carve-out (B6b or B8) rather than widening B6 |
| B6 | `src/SchoolCollab.Auth/AuthServiceExtensions.cs` | M (3rd of 4) | bootstrap-code store reuse (`OneTimeCodeStore` primitive) + portal redirect configuration |
| B6 | `src/SchoolCollab.Auth/Endpoints/AuthEndpointGroup.cs` | M (2nd of 3 — the 3rd is B7's, parent-amended) | wire the passkey group |
| B6 | `tests/SchoolCollab.Auth.Tests.Unit/PasskeyBootstrapTests.cs` | **N** | bootstrap code single-use / TTL / URI-bound (allowlist-enforced); **the D6 continuation code is issued only for the persisted, allowlist-validated `app_redirect_uri` — a fresh query param at `/complete` is never re-trusted**; **AC12 axis: no authorization code or token reaches Python in any portal-bound response** — the D16 continuation (D6 one-time code + app callback for Blazor-originated sign-ins) is carried as designed, not suppressed |
| B6 | `documents/configuration.md` | M (3rd touch) | §4 realm rows for the auth-RP redirect URIs + the no-portal-client statement |

**Pass B7 — auth service: D17 mediated picker reads**

| Pass | Path | N/M | Purpose |
|---|---|---|---|
| B7 | `src/SchoolCollab.Auth/Services/MediatedReaders.cs` | **N** | typed `HttpClient`s for tenants (settings-api `GET /api/tenants`) and teachers (students-api); the **session's custody access token is used server-side**; no token ever enters a response |
| B7 | `src/SchoolCollab.Auth/Endpoints/MediatedReadEndpoints.cs` | **N** | `GET /auth/pickers/tenants`, `GET /auth/pickers/teachers` — **authorization explicit**: each requires a valid portal session (B5's `PortalSessionAuthenticationHandler` scheme) and runs **as the session's user** — the custody access token forwarded server-side carries that user's `tenant_id`, so the data APIs' existing tenant middleware (`TenantClaimsTransformation`) scopes results exactly as for a direct authenticated call; data-only responses |
| B7 | `src/SchoolCollab.Auth/AuthServiceExtensions.cs` | M (4th of 4) | register the mediated readers |
| B7 | `tests/SchoolCollab.Auth.Tests.Unit/MediatedReadEndpointTests.cs` | **N** | session required (anonymous → 401); data-only responses (raw-body token scan); upstream failure → fail-closed taxonomy; the custody token is never echoed |
| B7 | `src/SchoolCollab.Auth/Providers/IAuthProvider.cs` | M (**parent-amended mid-pass**) | one ADDITIVE custody read: an access-token accessor so the mediated readers never touch `PortalSessionStore` directly — the gap the worker escalated (D15's 7 portal-facing operations must stay exactly as pinned; prefer a separate `ISessionTokenAccessor` interface) |
| B7 | `src/SchoolCollab.Auth/Providers/KeycloakAuthProvider.cs` | M (**parent-amended mid-pass**) | implements the accessor over the existing `CurrentTokens` + `RefreshTokensAsync`, refreshing transparently so a stale access token can never be forwarded; status taxonomy `Active/NotFound/SessionEnded/UpstreamUnreachable` maps to the endpoints' fail-closed responses |
| B7 | `src/SchoolCollab.Auth/Endpoints/AuthEndpointGroup.cs` | M (**parent-amended mid-pass, 3rd of 3**) | `group.MapMediatedReadEndpoints();` inside the `/auth` group — the new extension method had no call site in B7's rows, so the two routes would have existed in no host. The `Program.cs` alternative was rejected as off-pattern (the host gathers everything through `MapAuthEndpoints`) |

**Pass B8 — portal login / session / logout views (D4, D6, D12, D13, D18)**

| Pass | Path | N/M | Purpose |
|---|---|---|---|
| B8 | `src/SchoolCollab.AuthPortal/views/__init__.py` | **N** | package marker — the `SchoolCollab.Portals` shape carries it; created with the first `views/` file so the B2 guard's on-disk walk and the slnx stay consistent |
| B8 | `src/SchoolCollab.AuthPortal/views/login.py` | **N** | prefab `Form.from_model` + `Input` (password) + reactive `Field`/`FieldError`; submission via built-in `Fetch.post` to the portal's own FastAPI route; **"Sign in with passkey"** button linking to the auth-service passkey handoff (D14/D16) |
| B8 | `src/SchoolCollab.AuthPortal/views/errors.py` | **N** | fail-closed error cards at HTTP 200 (AC10) + the AC10 `session ended` card + the invalid-`return_uri` card |
| B8 | `src/SchoolCollab.AuthPortal/app.py` | M (2nd touch) | `POST /login`: **validates `return_uri` against `AuthPortal:AppCallbackPrefixes` before rendering/redirecting** (invalid → AC10 error card, never an open redirect); **antiforgery** (plan-review P2-4, spec §14): a one-time random token issued with each login-page render, stored server-side in Python memory, required and consumed on `POST /login` — **no signing key in Python** (D12) — plus the stated mitigations: JSON content-type only (a cross-site form POST cannot set `application/json` without a CORS preflight, which fails), SameSite=Lax session cookie, no CORS policy for foreign origins; then credentials → `/auth/exchange` via the api client → set the opaque HttpOnly SameSite=Lax session cookie → **HTTP 200 JSON `{"redirect_uri": "<validated return_uri>?code=…"}`**, and the view navigates **in-tab** via a registered `js_actions` `navigate` handler (`window.location.assign`) — **parent-amended: the plan's `Fetch.post`→302 pairing is not executable, because a browser fetch follows the 302 cross-origin, consuming the one-time code, dropping `Set-Cookie` and being CORS-blocked, so the user is never signed in (plan-review feasibility miss, caught by the worker before any write)**; bootstrap redemption route (D16) — reached by a top-level browser navigation, so it keeps a plain **302**; session read on render (D18 — `session ended` → clear cookie + error card); logout route (D13: revoke via auth service → redirect to the returned `end_session` URL) |
| B8 | `SchoolCollab.slnx` | M (2nd touch) | `File` items for the files this pass creates (plan-review P1-2: the B2-generalized guard fails `EveryPortalFile_IsListedInTheSolution` on any unlisted file, and `EveryPortalSolutionItem_ExistsOnDisk` forbids pre-listing not-yet-existing paths) |
| B8 | `tests/SchoolCollab.ArchitectureTests.Unit/PortalsSolutionItemsArchitectureTests.cs` | M (**parent-amended** — the sixth wiring gap) | extend `PortalSolutionFolderNodes_MirrorThePackageLayout` to pin the **AuthPortal** folder nodes now that `views/` exists — B2's diff review raised this as a P2 and no pass's rows carried it, so without the row the new folder nodes ship unpinned |
| B8 | `src/SchoolCollab.AuthPortal/tests/test_login_view.py` | **N** | form model, validation constraints, passkey button presence |
| B8 | `src/SchoolCollab.AuthPortal/tests/test_session_routes.py` | **N** | exchange → cookie set → redirect; invalid `return_uri` → error card; bootstrap redemption; session-ended → cookie cleared + error card; logout; **antiforgery: POST without/consuming the token is rejected** |

**Pass B9 — portal admin views (D8, D10, D11)**

| Pass | Path | N/M | Purpose |
|---|---|---|---|
| B9 | `src/SchoolCollab.AuthPortal/views/admin_users.py` | **N** | users list (`DataTable`), create/edit (`Form.from_model`: username, credential, attributes), tenant `Select` + searchable teacher `Combobox` fed by the mediated reads |
| B9 | `src/SchoolCollab.AuthPortal/views/admin_roles.py` | **N** | realm-role assign/unassign from the existing role set (definitions untouched, D11) |
| B9 | `src/SchoolCollab.AuthPortal/api/admin_api_client.py` | **N** | typed client for the auth service's `/auth/admin/*` surface, authenticated by the portal session (D12) |
| B9 | `src/SchoolCollab.AuthPortal/app.py` | M (3rd touch) | register admin routes, gated by an authenticated `user-admin` session |
| B9 | `SchoolCollab.slnx` | M (3rd touch) | `File` items for the files this pass creates (same P1-2 rationale as B8) |
| B9 | `src/SchoolCollab.AuthPortal/tests/test_admin_views.py` | **N** | view wiring, picker sources = mediated endpoints, session gate rejects non-admin |
| B10 | `src/SchoolCollab.Auth/Services/KeycloakAdminClient.cs` | M (**residual pass**) | R3: `KeycloakUser` gains `Attributes` — **Keycloak's wire shape is string → ARRAY of strings** (the realm stores the dev user that way at `school-collab-realm.json:163-166` and the four `oidc-usermodel-attribute-mapper`s read it at `:62-104`), camelCase; `CreateUserAsync` captures the new id from the `201 Location` header |
| B10 | `src/SchoolCollab.Auth/Endpoints/AdminUserEndpoints.cs` | M (**residual pass**) | R3: forward `Attributes` on create and update; the `201` body carries the created user id (`:77` currently returns only `{ status = "created" }`) |
| B10 | `tests/SchoolCollab.Auth.Tests.Unit/KeycloakAdminClientTests.cs` | M (**residual pass**) | R3: the create/update wire body carries the camelCase `attributes` map with array values; the create captures the id from `Location` |
| B10 | `tests/SchoolCollab.Auth.Tests.Unit/AdminEndpointTests.cs` | M (**residual pass**) | R3: attribute round-trip through the endpoint; the `201` body carries the id |
| B10 | `src/SchoolCollab.Auth/Services/AppCallbackAllowlist.cs` | M (**residual pass**) | R1: refuse any `.`/`..` path segment **explicitly** — the C# matcher is currently safe only because `Uri.AbsolutePath` normalizes dot segments, while the Python matcher refuses them outright; the two implementations of one control must be contractually identical |
| B10 | `tests/SchoolCollab.Auth.Tests.Unit/AppCallbackAllowlistTests.cs` | M (**residual pass**) | R1: the explicit dot-segment refusal test (mirroring the Python `…_RejectsADotSegmentPathSpelling`) |
| B10 | `documents/configuration.md` | M (**residual pass**) | R2: note that the portal session cookie carries `Secure` when `AuthPortal:PublicBaseUrl` is https (conditional; dev http is off) |
| B11 | `src/SchoolCollab.AuthPortal/views/login.py` | M (**UI-tester rework**) | finding 1: the field error state must not appear on **first paint** and must not double up with the Alert banner after a failed submit — make invalidity **submit-driven** (e.g. an `attempted` client state set in the submit action chain, `invalid=Rx("attempted && !field")`) or suppress the competing layer; a user who has not typed anything must see a clean form |
| B11 | `src/SchoolCollab.AuthPortal/views/admin_users.py` | M (**UI-tester rework**) | findings 2 + 3: remove the per-user duplication (a user must be reachable from the list **once**, without re-scanning a second "Manage" card); and resolve the Tenant/Teacher columns to the **readable picker labels** (`PickerTenant.label`/`PickerTeacher.label`) instead of raw `tenant_id`/`teacher_id` — a deliberate "—" when a value cannot be resolved, never an opaque id |
| B11 | `src/SchoolCollab.AuthPortal/tests/test_login_view.py` | M (**UI-tester rework**) | pin the first-paint clean form and the single-error-layer behaviour after a failed submit |
| B11 | `src/SchoolCollab.AuthPortal/tests/test_admin_views.py` | M (**UI-tester rework**) | pin the single list (no duplicate per-user card) and the resolved-label columns (readable name, or the deliberate "—") |

**Round B totals: 67 rows over 56 distinct paths — 35 new rows (35 new distinct paths), 32 modified
rows (21 modified distinct paths).** Six paths are touched
more than once, and every such row is listed so the per-pass conformance baseline is exact:
`src/AppHost/SchoolCollab.AppHost/Program.cs` (B3 + B5b); `documents/configuration.md`
(B3 + B5b + B6); `src/SchoolCollab.Auth/AuthServiceExtensions.cs` (B5 + B5b + B6 + B7);
`src/SchoolCollab.Auth/Endpoints/AuthEndpointGroup.cs` (B5 + B6);
`src/SchoolCollab.AuthPortal/app.py` (B2 create + B8 + B9); `SchoolCollab.slnx`
(B2 + B8 + B9). **Note vs the plan-review's verified arithmetic (65 rows / 54 distinct paths /
35 N rows / 30 M rows — 19 modified distinct paths):** that row-by-row count predates exactly the
two P1-mandated M rows (`AuthServiceOptionsTests.cs`, `AuthEndpointTestHost.cs`), which this same
review added to B5b; the figures above are recomputed from the final tables. Earlier notes are
superseded: the 53/47 count predated the P1-2 `slnx` rows (B8/B9), the P2-6 `__init__.py` rows and
P1-3's allowlist pass (which adds `Program.cs`'s 2nd touch and `configuration.md`'s 2nd/3rd); the
reviewer can recount row-by-row against the final tables. Patch
artifacts are frozen by the parent as `documents/rounds/diffs-keycloak-ui-auth-b<passtag>.patch`.

### The live P1 designs (as revised by the plan review)

**P1-2 — the flag-ON challenge path (premise verified at source).**
`AuthTenancyExtensions.cs:87` unconditionally sets `DefaultChallengeScheme = OpenIdConnect`, so a
gated page 302s to Keycloak even with the flag ON; nothing routes to the portal. Design: in the
OIDC branch of `AddAuthAndTenancy`, register an `AddPolicyScheme` ("LoginUiChallenge") as the
`DefaultChallengeScheme` whose `ForwardChallenge` target is selected **at startup** from the flag
(D5's startup read): OFF → forward to OpenIdConnect exactly as today; ON → forward to the new
`PortalRedirectChallengeHandler`, which 302s to `{Auth:Portal:LoginUrl}?return_uri=<the request's
scheme/host>/signin-handshake` (plus the original path as `ReturnUrl` for post-handshake
continuation). Fail-closed: with the flag ON and no portal login URL configured, the handler
returns **401**, never a blank redirect — which also keeps the auth service's own bearer-gated
surface 401-correct, since the auth service receives the flag but **not** the browser-facing login
URL (P2-5: the keys are pinned per consumer in B3). The policy scheme is registered once at
startup — consistent with the repo's "schemes are registered once" constraint and with the
`DisableOIDCAuth` startup-switch precedent (`Admin/Program.cs:57-61`). Only pass B1 files are
involved; the Blazor hosts consume it in B4.

**P1-3 — the handshake transport (premise verified at source; revised per plan-review P1-1).**
`CrossModuleWiringTests.cs:59` (`CrossModule_BaseAddresses_HaveMatchingAppHostReference`) fails any
cross-module base address whose owning project lacks a matching `.WithReference` — and it enforces
that rule for **every hosted direct consumer of the library** that registers the client, so a
literal `https+http://auth` inside a Core extension would fail the suite for students-api,
settings-api, settings-ai and assignments-api (they consume Core without referencing `auth`; the
migrator at `AppHost/Program.cs:233-240` is the repo precedent), while a config-bound base address
is invisible to the guard and dies at runtime with "No such host is known". Fix: the Core extension
takes the base address **as a parameter** and each host passes the literal `"https+http://auth"` at
its call site (the `FamiliesModuleServices.cs:17` pattern, `AddCrossModuleHttpClient<FamiliesApiClient>("https+http://assignments-api", …)`) —
only Admin and Families register the handshake client, so the strict per-host rule applies to
exactly the two hosts B3 wires with `.WithReference(auth)`. The B3 guard pins both references so
the transport cannot silently rot.

**P1-3/plan-review — the per-app callback allowlist (closes the open-redirect hole).**
Round A's `/auth/exchange` takes the caller's `RedirectUri` (`ExchangeEndpoints.cs:24`), and
`/auth/redeem` is caller-unauthenticated with only URI binding — the "in-network only" trust
boundary does not hold once the portal's `/login?return_uri=…` is browser-reachable. A crafted
`portal/login?return_uri=attacker` link would otherwise yield an attacker-redeemable one-time code
carrying the victim's full claim set. Design: one **allowlist of exact callback prefixes**
(scheme + host + port + path — e.g. the four Blazor handshake callbacks and the portal's bootstrap
redemption URI), held in auth-service config (`Auth:AppCallbackPrefixes`, B5b) and fanned to the
portal as its own copy (`AuthPortal:AppCallbackPrefixes`). The **load-bearing control is at
issuance, on BOTH paths**: `/auth/exchange` (password path, B5b) and `/auth/passkey/*`
(passkey/bootstrap path, B6) reject a non-allowlisted redirect URI **before any code is issued** —
so an attacker URI can never yield a redeemable code, regardless of what the portal does. The
portal-side check (B8) is defense-in-depth + UX: `POST /login` validates `return_uri` against the
portal's copy before rendering the form or redirecting; an invalid URI renders the AC10 error
card. Redemption keeps its round-A redirect-URI binding on top of the allowlist.

**D16 bootstrap design (concretely).** The portal's passkey button links to
`GET /auth/passkey/login?app_redirect_uri=<allowlisted callback>`; the auth service validates it
against the allowlist (B5b) and challenges its OIDC handler (it is the RP — the realm has no portal
client), Keycloak runs the WebAuthn ceremony, the handler's auto-mapped `/signin-oidc` returns to
the auth service, and `GET /auth/passkey/complete` (now cookie-authenticated on the auth service)
completes the exchange in custody, creates the portal session, and 302s to the portal with a
single-use, short-TTL, URI-bound bootstrap code (the round-A `OneTimeCodeStore` primitive). The
portal redeems the bootstrap code for its session id; if a Blazor app originated the sign-in, the
same redemption response carries the D6 one-time code + app callback so the portal redirects the
browser onward — no authorization code or token ever reaches Python (AC12).

### Binding constraints (every pass)

- `dotnet build SchoolCollab.slnx` after **every** code change — 0 errors. `.slnx` is the single
  solution and has **no globbing**; the auth-portal's Python files are explicit `File` items added
  **in the pass that creates each file** (B2/B8/B9 rows).
- `dotnet test tests/SchoolCollab.<Name>` with **no extra flags** (MTP rejects them); read results
  with one grep for `total:`/`failed:`. Portal pytest runs via `uv run pytest` in the project dir.
- **CPM**: versions only in `Directory.Packages.props`; a `PackageReference` never carries
  `Version`. Expected untouched all round.
- **AC11 is the round's letter-grade constraint**: the portal holds no credential of any kind — no
  Keycloak token, no API token, no cookie-signing key, no client secret — and performs no direct
  call to settings-api/students-api; every identity, privilege and token-bearing read is mediated.
  This **supersedes round A's** `DELETE /auth/session/{id}` refresh-token-return contract (B5,
  owner-adjudicated).
- **AC12**: no authorization code or token reaches Python on the flag-ON passkey path.
- **Open-redirect posture**: every redirect URI that can receive a code is allowlisted
  (scheme+host+port+path, exact prefix); the allowlist is enforced at issuance on both the password
  and passkey paths, fail-closed; the portal re-validates before rendering/redirecting.
- **Antiforgery on the anonymous credential path** (spec §14): one-time server-stored token on
  `POST /login` (no signing key in Python, D12), JSON-only request body, SameSite=Lax cookies, no
  foreign CORS.
- The **D15 seam is real but minimal**: one interface, one Keycloak implementation; the Admin REST
  surface stays explicitly Keycloak-specific; no speculative second-provider scaffolding.
- Provider/portal boundary: the portal talks **only** to the auth service (plus the browser); it
  never calls Keycloak directly (D6/D7). The auth service is the only credential custodian.
- Prefab forms submit via built-in `Fetch.post(url, body=…)` to the portal's own FastAPI routes —
  no MCP host (gate §16.1 RESOLVED; do not re-verify, do not spike).
- The Python portal's AppHost registration cannot use `WireKeycloakAuth` (typed `ProjectResource`)
  — explicit `WithEnvironment` calls mirroring `AddUvicornApp` (`Program.cs:408-411`).
- Realm edits keep `AppHostRealmImportArchitectureTests` green (strict JSON, filename, bind-mount
  parity) and add no portal OIDC client (D16 negative invariant, guarded in B6).
- API endpoints grouped in extension methods, never inline in `Program.cs`; Python routes mirror
  the portals `api/` + `views/` split (incl. `__init__.py` package markers). No `services.Add*()`
  inline in feature hosts.
- `documents/configuration.md` updated in the same change set as any config addition (B3, B5b, B6).
- Every new invariant gets a **discriminating** guard; the worker proves it can fail (temporary
  removal probe + observed counts) and reports the axis each probe covers.
- `net10.0`; CQRS via `ICommandHandler`/`IQueryHandler` + Scrutor — never MediatR; no direct
  project references between bounded contexts (the auth service references Core + ServiceDefaults
  only; settings/students contact is HTTP).
- Worker passes never commit, push, or run `gh`. Repo-scoped searches only — never `find /`.
- **30-minute cap per pass.** On the cap, stop, write the WORKER REPORT with what landed and what
  remains, and let the parent re-scope — do not overrun.

### Test coverage per pass

| Pass | Unit / pytest coverage | Architecture guards |
|---|---|---|
| B1 | `AuthWiringTests` (policy scheme + per-flag forward target); `PortalRedirectChallengeHandlerTests` (302 shape, `return_uri`, 401 fail-closed) | — |
| B2 | pytest: routes + typed auth-api client with a stubbed auth service (`dependency_overrides`); no-token-field DTO assertion (AC11) | extend `PortalsSolutionItemsArchitectureTests` to walk `src/SchoolCollab.AuthPortal/` too (incl. `uv.lock`) |
| B3 | — | new `AppHostLoginUiFlagWiringArchitectureTests`: flag fan-out to exactly 4 consumers; `Auth:Portal:LoginUrl` **absent from the auth consumer**; `AddUvicornApp` + `WithUv` portal registration; `.WithReference(auth)` on admin + families |
| B4 | `PortalHandshakeTests`: redeem → cookie with full claim set (incl. roles); replay/TTL rejected; logout SignOut | `CrossModuleWiringTests` stays green via the literal-at-call-site base address (asserted, not edited) |
| B5 | provider seam operations; session-auth handler valid/unknown/expired; session read returns claims as data; `session ended` distinct status; no-token-in-any-body raw scans incl. DELETE | — |
| B5b | allowlist matcher (exact-prefix, fail-closed, malformed input); exchange rejects non-allowlisted `RedirectUri` with no code issued | — |
| B6 | bootstrap code single-use / TTL / URI-bound + allowlist rejection on the passkey path; portal-bound redemption carries only session id (+ the D6 continuation payload, never a code/token for Python) | realm guard: auth-RP redirect URIs (both dev schemes) present + no portal client |
| B7 | mediated reads: anonymous → 401; data-only (raw-body token scan); upstream failure taxonomy fail-closed | — |
| B8 | pytest: form model + validation; antiforgery token required + single-use; invalid `return_uri` → error card; exchange→cookie→redirect; bootstrap redemption; session-ended → cookie cleared + AC10 card; logout | — |
| B9 | pytest: admin views wiring; pickers sourced from mediated endpoints; non-admin session rejected | — |

Every new behavioural class gets a test (`testing.md`: MSTest + Moq + FluentAssertions for C#;
pytest for the portal; no untested behavioural code).

### Acceptance criteria — round B

| # | Criterion | Pass | Verifiable by |
|---|---|---|---|
| **B-A** (spec AC7, D5) | With the flag **OFF** the challenge is byte-for-byte today's OIDC behaviour; with it **ON** an unauthenticated gated-page request 302s to the portal `/login?return_uri=…`; flag-ON with no portal URL configured → 401 fail-closed; the selection is made at startup | B1 | `AuthWiringTests`, `PortalRedirectChallengeHandlerTests` |
| **B-B** (spec AC2, D6) | Admin and Families redeem the one-time code via a typed client whose literal `https+http://auth` base address is matched by `.WithReference(auth)` on both registrations; redemption signs the host in with its own cookie carrying the same claim set (tenant, `teacher_id`, `roles`) as the OIDC path; replay/TTL/URI-mismatch still rejected | B3/B4 | `AppHostLoginUiFlagWiringArchitectureTests`, `CrossModuleWiringTests`, `PortalHandshakeTests` |
| **B-C** (D15) | All portal-facing operations sit behind ONE `IAuthProvider` interface with the Keycloak implementation behind it; every seam operation routes through the implementation with the round-A failure taxonomy preserved | B5 | `KeycloakAuthProviderTests`; **"no speculative second-provider scaffolding exists" is a static-review check, not a unit assertion** (see reviewer criterion 4) |
| **B-D** (D16, AC12) | The realm declares the auth-service RP redirect URIs (both dev schemes) and **no portal OIDC client/redirect URI**; the bootstrap code is single-use, short-TTL, URI-bound against the app-callback allowlist; **on the passkey path no authorization code or token reaches Python** — portal-bound responses carry at most the bootstrap code (plus, for a Blazor-originated sign-in, the D6 one-time code + app callback per D16's continuation, never a token) | B6 | `PasskeyBootstrapTests`, realm guard |
| **B-E** (D17, D18, AC11) | The portal holds no credential and makes no direct settings/students call; mediated reads require a valid portal session, run as the session's user (tenant scoping preserved by the data APIs' tenant middleware), and return data only; the session read returns the claim set as data; a failed refresh returns the distinct `session ended` status; `DELETE` revokes server-side and returns the `end_session` URL — **no token in any response body, including DELETE** | B5/B7 | `SessionEndpointTests`, `MediatedReadEndpointTests`, `PortalSessionAuthenticationHandlerTests` |
| **B-F** (spec AC10, §14, §16.1) | The prefab login form renders with password input + reactive validation + the passkey button; submission posts to the portal's own FastAPI route via `Fetch.post`; **`POST /login` is antiforgery-protected** (one-time server-stored token, JSON-only body, SameSite=Lax, no foreign-origin CORS); invalid `return_uri` → error card; degraded states (auth service/Keycloak down) render error cards at HTTP 200 | B8 | pytest `test_login_view`, `test_session_routes` |
| **B-G** (D8, D10, D11) | The admin views list/create/edit tenant users bound to a tenant + teacher row (pickers via mediated reads), assign/unassign realm roles, and are gated by an authenticated `user-admin` session | B9 | pytest `test_admin_views` |
| **B-H** (round-A A-G inheritance) | Whole-solution build 0 errors; every affected suite 0 failures; every new guard is discriminating (removal probe + observed counts reported); the config additions are documented in their own change sets | all | per-pass commands |

**Live/cold-start items owned by the round's runtime checks (and the UI tester), not by unit
tests:** the flag-OFF hosted-page + passkey conditional UI end-to-end; the flag-ON form →
handshake → Blazor cookie with identical claims against a running Keycloak; logout ending the
Keycloak SSO session; the admin UI creating a real user; passkey conditional UI and the WebAuthn
ceremony; the sufficiency of the imported `realm-management` quartet on first Admin REST use.

### Worker task spec

Each pass is dispatched with this contract:

> You are the WORKER for **pass B-N** of **round B** of Tier 3 round `keycloak-ui-auth`. Read the
> round doc `documents/rounds/round-keycloak-ui-auth-b.md` — the `### Pass B-N` details in the plan
> plus that pass's expected-files rows are your exact scope and conformance baseline. Implement
> **only** that pass; any file outside the pass's expected-files list is a scope deviation — stop
> and report it. Read `AGENTS.md`; for C# changes `.github/copilot/rules/dotnet-best-practices.md`;
> for tests `.github/copilot/rules/testing.md`; for config `.github/copilot/rules/configuration-documentation.md`.
> The spec `documents/specs/keycloak-ui-auth-integration.md` is **binding** — D1-D18 (D12 as
> amended; D15-D18), AC1-AC12 — read it before any contract decision. Build with
> `dotnet build SchoolCollab.slnx` (0 errors) and run the pass's listed suites with no extra flags;
> for Python passes run `uv run pytest` in `src/SchoolCollab.AuthPortal`. **AC11/AC12 are hard**:
> no credential, token, authorization code or cookie-signing key ever reaches Python state or a
> portal-bound response; the portal never calls Keycloak or the data APIs directly. Redirect URIs
> that can receive a code are allowlisted and enforced at issuance — never trust a caller-supplied
> URI. Where a step says stop and report, do exactly that rather than guessing. **30-minute cap** —
> on the cap, stop and report what landed and what remains. Prove every new guard discriminates
> (temporary removal probe, observed counts, name the axis covered). Do not edit the round doc;
> never commit/push/gh; repo-scoped searches only, never `find /`. Return ONLY the WORKER REPORT
> block: Changed files / Build / Tests / Deviations from plan.

### Reviewer acceptance criteria

The static reviewer (`kimi-k2.7-code`, read-only, never builds) checks, per pass, against the
pass's frozen diff + its expected-files rows:

1. **Scope conformance** — the changed-path set equals the pass's expected files (no extras, none
   missing); no other pass's files touched; multi-touch paths annotated with the correct touch
   number; the `slnx` rows in B8/B9 cover exactly the files those passes create.
2. **No overwrites** — nothing pre-existing deleted or reformatted beyond the pass's stated edits;
   round A's guards (realm import, dev-parameter, solution registration, portals solution items,
   wiring guards) keep their unrelated assertions intact.
3. **Skills honoured** — `dotnet-best-practices` (C#), `testing.md` (MSTest/Moq/FluentAssertions;
   pytest pattern for the portal; no untested behavioural class), `configuration-documentation.md`
   (config additions documented in the same change set).
4. **Pinned-decision conformance** — D1-D18 not weakened for B's scope: the flag is a pure UI
   toggle (AC7); one-time + bootstrap codes single-use/TTL/URI-bound (D6/D16) and **allowlist-
   enforced at issuance on both paths** (P1-3); the auth service is the OIDC RP and the realm has
   **no portal client** (D16/AC12); tokens server-side only and no token in any response body
   **including DELETE** (D12 amended, AC11); mediated reads only, session-scoped and tenant-preserving
   (D17/P2-9); claims-as-data session reads with the distinct `session ended` status (D18); the
   portal's login is the form+exchange path under both flag states (§5.1-step-4 supersession; D4
   governs the Blazor apps only); **no speculative second-provider scaffolding exists** (static
   check — D15); declarative realm config (D3/D11).
5. **Guard honesty** — every new guard is discriminating (can fail, derives from source, does not
   pass vacuously), and the reviewer confirms the worker's removal-probe evidence, including which
   axis each probe covered.
6. **No overclaiming** — cold-start/live-Keycloak items are attributed to the runtime checks and UI
   tester, never to unit tests; the prefab-form support is treated as resolved (no re-verification
   claimed or planned).
7. **Python-side review** — the portal mirrors the `SchoolCollab.Portals` shape (`api/` + `views/`
   split with `__init__.py` markers, dependency-overridden tests, fail-closed error cards), holds
   no credential, enforces the antiforgery token + `return_uri` validation on `POST /login`, and
   the slnx `File` items + folder nodes match the guard's expectations (including `uv.lock`).

### Open items for the owner — adjudicated (parent, post-plan-review; none open)

1. **Round-A contract supersession — CONFIRMED.** AC11 supersedes round A's
   `DELETE /auth/session/{id}` refresh-token-return contract: B5 revokes the refresh token
   server-side and returns the built `end_session` URL (D13). B5's `SessionEndpointTests` row
   carries the assertion change; the round-A AC9 exemption is closed.
2. **OIDC RP client identity — PLAN DEFAULT CONFIRMED: reuse `school-collab-client`** (zero new
   secrets). A dedicated `school-collab-auth-rp` confidential client remains a **later additive
   realm/config migration**, not round-B scope.
3. **Portal public-URL distribution — CONFIRMED**: `WithEnvironment` from the portal's endpoint,
   the same pattern as the Keycloak endpoint fan-out.
4. **No new NuGet packages — CONFIRMED**; report-only if one proves genuinely necessary.
5. **UI tester pin — CONFIRMED**: `minimax-m3` stands; the tester runs this round (round B has UI
   surfaces).

## Worker Report

**Pass B1 — flag key + challenge-path interception** (`ollama-cloud/deepseek-v4.1-flash`; patch `documents/rounds/diffs-keycloak-ui-auth-b1.patch`, 545 lines, the 5 files below).

    WORKER REPORT (pass B1)
    Changed files (5):
      src/SchoolCollab.Core/Features/FeatureFlagKeys.cs (M — DisableKeycloakLoginUi);
      src/SchoolCollab.Core/Auth/AuthTenancyExtensions.cs (M — LoginUiChallengeScheme const; flag read once at startup; AddPolicyScheme as DefaultChallengeScheme with per-flag ForwardChallenge; PortalRedirect scheme registered only when the flag is ON, bound to a startup read of Auth:Portal:LoginUrl);
      src/SchoolCollab.Core/Auth/PortalRedirectChallengeHandler.cs (N — 302 -> {LoginUrl}?return_uri={scheme}://{host}{pathBase}/signin-handshake?ReturnUrl=<escaped path+query>; 401 fail-closed on missing/blank login URL; HandleAuthenticateAsync = NoResult);
      tests/SchoolCollab.Core.Tests.Unit/Auth/AuthWiringTests.cs (M — 3 tests: default challenge scheme is the policy scheme; OFF forwards to OIDC and PortalRedirect is not registered; ON forwards to PortalRedirect with OIDC still registered; TestAuth registers neither);
      tests/SchoolCollab.Core.Tests.Unit/Auth/PortalRedirectChallengeHandlerTests.cs (N — 4 tests through IAuthenticationService on a real DefaultHttpContext).
    Build: 0 errors.
    Tests: SchoolCollab.Core.Tests.Unit 99 passed / 0 failed (92 at round-A base); smoke on hosts composing AddAuthAndTenancy: Auth.Tests.Unit 102/0, Admin.Tests.Unit 546/0.
    Deviations from plan: none in scope — the changed set equals B1's rows; documents/configuration.md deliberately untouched (its rows are B3's).
    Plan-premise verification: the OIDC branch set DefaultChallengeScheme = OpenIdConnect unconditionally (pre-edit AuthTenancyExtensions.cs:87-88) — the flag alone could never divert the challenge; the AddPolicyScheme interception is proven empirically (the flag-ON tests issue a challenge with no explicit scheme and observe the 302 through the framework's policy-scheme forwarding).
    Discriminating probes (invariant removed -> observed counts -> file restored):
      A — fail-closed branch replaced by a Keycloak-redirect fallback: 99 total / 2 failed (the two 401 tests). Axis: fail-closed 401 — no Keycloak fallback, no blank redirect.
      B — ForwardChallenge hardcoded to OpenIdConnect (flag selection removed): 99 / 5 failed. Axis: flag-ON target selection / interception actually reachable.
      C — redirect payload dropped (return_uri + nested ReturnUrl): 99 / 2 failed (both exact-Location tests). Axis: 302 redirect shape.
      D — ForwardChallenge hardcoded to PortalRedirect (OFF path re-targeted): 99 / 1 failed. Axis: flag-OFF = today's OIDC challenge (AC7).
    Non-scope notes: (1) two of the five files also carry round A's still-uncommitted delta (the D9 RoleClaimType pin + its assertions) — present in the frozen patch, already reviewed and accepted in round A (`diffs-keycloak-ui-auth-roleclaim-fix.patch`); the diff reviewer is instructed not to re-litigate it. (2) Interfaces B1 pins for later passes: scheme names `LoginUiChallenge` / `PortalRedirect`; the `return_uri=.../signin-handshake?ReturnUrl=<escaped original path+query>` shape; and the portal scheme exists only with the flag ON — so B8's allowlist check must be a true scheme+host+port+path PREFIX match (the nested ReturnUrl query must not break it, per B5b's design). (3) The flag is read with the existing IsFlagEnabled helper on `FeatureFlags:FEATURE:DisableKeycloakLoginUi` (+ bare-key fallback), matching B3's planned WithEnvironment fan-out.

**Pass B2 — auth portal skeleton (Python, mirrors `SchoolCollab.Portals`)** (`ollama-cloud/deepseek-v4.1-flash`; patch `documents/rounds/diffs-keycloak-ui-auth-b2.patch`, 2254 lines, the 11 files below).

    WORKER REPORT (pass B2)
    Changed files (11 — all 11 B2 rows; 9 N + 2 M):
      src/SchoolCollab.AuthPortal/pyproject.toml (N — fastapi/uvicorn/httpx/prefab-ui==0.20.2, dev pytest+pytest-asyncio, identical pytest ini);
      src/SchoolCollab.AuthPortal/uv.lock (N — resolved offline from the local uv cache; package+version set identical to SchoolCollab.Portals/uv.lock except the project name);
      src/SchoolCollab.AuthPortal/app.py (N — FastAPI app, lifespan-owned async client, get_auth_api_client dependency seam, ServiceDiscoveryError handler; GET / (auth-service endpoint + mediated session read (D18) + route table) and GET /health; always 200 — AC10);
      src/SchoolCollab.AuthPortal/api/__init__.py (N — public surface re-exports);
      src/SchoolCollab.AuthPortal/api/auth_api_client.py (N — AuthApiClient: exchange / bootstrap redeem / session read / revoke; AuthServiceEndpoint + Aspire discovery; typed failure-code mapping; token-shaped-body refusal);
      src/SchoolCollab.AuthPortal/api/dto.py (N — ExchangeResult, BootstrapRedemption, SessionData (claims-as-data + roles + is_admin), SessionRevocation; no token field anywhere);
      src/SchoolCollab.AuthPortal/api/errors.py (N — PortalApiError/ServiceDiscovery/ApiUnavailable/ApiResponse + SessionEndedError, BootstrapCodeRejectedError, TokenInResponseError);
      src/SchoolCollab.AuthPortal/tests/test_app_routes.py (N — 9 tests: route table + resolved endpoint, signed-out, mediated session render (asserts GET /auth/session/{id} and that the session id is never echoed), session-ended card, unreachable card, discovery-failure card, health ok/degraded/last-call);
      src/SchoolCollab.AuthPortal/tests/test_auth_api_client.py (N — 27 tests incl. parametrized: request shapes for all four calls, failure taxonomy, D18 session-ended vs 404, path quoting, AC11 token-body refusal (every token key + nested), AC11 structural DTO scan);
      SchoolCollab.slnx (M — Folder nodes /src/SchoolCollab.AuthPortal/, /api/, /tests/ with one File per new file, uv.lock included);
      tests/SchoolCollab.ArchitectureTests.Unit/PortalsSolutionItemsArchitectureTests.cs (M — PortalFolderPrefixes array; walk + EveryPortalFile_IsListedInTheSolution + EveryPortalSolutionItem_ExistsOnDisk now span both portals with a per-prefix anti-vacuity assertion; the Portals reviewed-set pin and folder-node pin untouched).
    Build: 0 errors (13 pre-existing package-advisory warnings, none from the changed file).
    Tests: SchoolCollab.ArchitectureTests.Unit 53 passed / 0 failed; AuthPortal pytest (uv run pytest -q) 36 passed / 0 failed.
    Discriminating probes (removed -> observed -> restored):
      A — dropped the uv.lock <File> item from the slnx: 53 total / 1 failed, naming exactly src/SchoolCollab.AuthPortal/uv.lock. Axis: slnx completeness for the new portal (the B8/B9 failure mode).
      B — pointed the AuthPortal folder prefix at src/SchoolCollab.AuthPortalX/: 53 / 1 failed (the per-prefix anti-vacuity assertion). Axis: the generalized walk actually reaches the second portal project.
    Deviations from plan:
      - Shape deviation from the Portals mirror: no api/service_discovery.py (B2 has no such row) — endpoint resolution is colocated in api/auth_api_client.py; views/ arrives with B8.
      - CROSS-PASS WIRE PINS B2 had to choose (the plan names the client's methods but not the wire shapes); B5/B5b/B6 must serve them:
          POST /auth/bootstrap/redeem  {code, redirectUri} -> {sessionId, appRedirectUri?, appCode?}
          GET  /auth/session/{id}      -> claim-set-as-data {tenantId, tenantName, tenantType, teacherId, roles} (snake_case also accepted) + optional expiresInSeconds
          DELETE /auth/session/{id}    -> {endSessionUrl}   (B5's supersession of round A's {refreshToken})
          the client keys the D18 state on the body code {"error":"session_ended"} on ANY non-2xx, so B5's HTTP status choice stays free
          consumed error codes: invalid_credentials, disabled_user, keycloak_unreachable/upstream_unreachable, session_not_found, session_ended, invalid_code, code_expired, redirect_uri_mismatch, redirect_uri_required
      - B2 renders degraded states from one index builder inside app.py (views/errors.py is B8's row); D18 cookie-clearing on session ended and the return_uri allowlist check remain B8's. GET / performs the mediated session read that gives the B2 test row its dependency_overrides + degraded coverage.
      - uv resolved and ran fully offline from the local cache (no improvised runner, no vendored packages).

## Review

### PLAN REVIEW (step 2b — before the worker) — `ollama/glm-5.3:cloud`, read-only

Run `7721fd25-e3ea-46f0-92e3-35ede6ba54f4`, iteration 1, against the plan as first authored.

    PLAN REVIEW
    Verdict: REWORK
    P1: P1-3 transport — `CrossModuleWiringTests:59` applies the per-host literal rule to EVERY hosted consumer of the registering library (Core is consumed by students-api, settings-api, settings-ai and assignments-api), so a literal `https+http://auth` inside the Core extension fails the suite for four unrelated hosts; pin the shape — base address a parameter, literal at each host call site (`FamiliesModuleServices.cs:17` pattern)
    P1: B8/B9 expected-files have no `SchoolCollab.slnx` row, yet B2 generalizes the portals guard to walk `src/SchoolCollab.AuthPortal/` — every B8/B9-created file fails `EveryPortalFile_IsListedInTheSolution`; add the slnx M rows to B8/B9
    P1: redirect-target allowlist claimed but undesigned (security) — browser-reachable `portal/login?return_uri=…` plus a caller-unauthenticated `/auth/redeem` with only URI binding means a crafted link yields an attacker-redeemable code carrying the victim's claim set; design the per-app allowlist, enforce at issuance on BOTH paths, add rejection tests + config-doc rows
    P2: §5.1 step 4 vs D16 · B-D/B6 wording · B6 realm https scheme · B8 antiforgery · B3 per-consumer env keys · `__init__.py` rows · totals arithmetic · B-C untestable clause
    Gates: UI: risk · contract: risk · migrations: none · secrets: none

Orchestrator revision run `19eeb925-e84e-4575-aeb1-410fd9b58aa4` closed all 3 P1s + 8 P2s, adding pass **B5b** (allowlist: options key + fail-closed matcher + issuance enforcement on both paths + rejection tests + config rows), the B8/B9 slnx rows, the parameterized transport, and the §5.1-step-4 supersession note.

### PLAN REVIEW — re-read (bound) — `ollama/glm-5.3:cloud`, read-only

Run `08656e39-cd9f-476c-bdd7-33ae4620013f`. All three P1s verified CLOSED. REWORK returned on a single completeness P1 + 2 P2s:

    P1: B5b expected-files table omitted the two test files its own `ValidateOnStart` change necessarily touches — `AuthServiceOptionsTests.cs` (the `Valid()` fixture at `:16` sets only the four Keycloak fields; `Validator_AcceptsCompleteConfiguration` at `:61-63` goes red) and `AuthEndpointTestHost.cs` (`:90` in-memory config lacks `Auth:AppCallbackPrefixes`, so host start fails the `:86` `ValidateOnStart` demand and red-lights every composed-host test). The avoidance implementations (skip validation / hardcoded non-empty default) respectively break the startup-guard/tests-cannot-drift contract or make production fail-open
    P2: totals split still wrong (31 was the pre-revision N-row count carried forward un-recomputed)
    P2: B6 — pin that the validated `app_redirect_uri` is persisted across the OIDC ceremony (or re-validated at `/complete` where the D6 continuation code is issued)
    Gates: UI: risk · contract: risk · migrations: none · secrets: none · Acceptance honesty: ok

**Bound deviation (parent, 2026-09-23).** The ≤1 plan-review iteration bound was reached, and the surviving P1 was a two-row completeness fix that the same reviewer had dictated verbatim, with both file paths and line numbers. Rather than spend a third full cycle, the parent had the orchestrator apply exactly the dictated fixes (run `b962903e-5354-4c07-b21b-cad7b8adf3be`) and verified them on disk. **Plan gate: CLOSED** — no open P1. Recorded here rather than hidden so the round's process cost stays traceable.

### DIFF REVIEW — pass B1 — `ollama-cloud/kimi-k2.7-code`, read-only

Run `71cf60e2-0622-4ce0-aa6c-f64d707b6bbb`, against `documents/rounds/diffs-keycloak-ui-auth-b1.patch`.

    REVIEW
    Verdict: PASS
    P1: none
    P2: src/SchoolCollab.Core/Auth/PortalRedirectChallengeHandler.cs:50 — explicit constructor for DI; dotnet-best-practices prefers a primary constructor (sealed class X(...) : AuthenticationHandler<...>(...))
    Best-practices: skills honored / no overwrites / readable

Verified: **scope conformance** — exactly the 5 B1 files; no other pass's files in the B1 delta · **no overwrites** — round A's D9 `RoleClaimType = ClaimTypes.Role` pin and its assertions intact, B1 additions additive/targeted · **D5** — the flag is read once at startup (`AuthTenancyExtensions.cs:61`) and baked into the policy scheme's `ForwardChallenge` · **AC7** — OFF forwards to OIDC and `PortalRedirect` is registered only inside `if (disableKeycloakLoginUi)`, so the OFF pipeline is today's OIDC challenge path · **D4/D16** — no second provider; the flag only reroutes the challenge, leaving validation/authorization untouched · **fail-closed** — `HandleChallengeAsync` returns 401 with no `Location` header on a missing/blank login URL, with no Keycloak fallback · **TestAuth** — both schemes absent (the whole OIDC branch is skipped) · **guard honesty** — the seven new tests are discriminating, each asserted axis matches its name, and the four worker probe counts are plausible from the code · **redirect security** — `return_uri` is built from the request's `Scheme`/`Host`/`PathBase` plus the fixed `/signin-handshake` path; the original path+query is captured as `ReturnUrl` and `Uri.EscapeDataString`-escaped; the `Location` header is the trusted config-bound `loginUrl` + separator + escaped `returnUri`; no caller-controlled input is inserted unescaped; the 401 path sets only the status code and logs a warning without leaking the missing-URL state · **`configuration.md` non-touch confirmed correct** (B3 owns those rows).

**Residual P2 (parent-adjudicated, carried):** the primary-constructor style nit at `PortalRedirectChallengeHandler.cs:50`. Behaviour-neutral and not CI-enforced (ArchitectureTests 53/0); carried to the round's final acceptance rather than spending a worker run on it.

**Pass B1 — ACCEPTED.** Parent authoritative pass: `dotnet build SchoolCollab.slnx` 0 errors; Core.Tests.Unit **99/0**; ArchitectureTests.Unit **53/0**; Auth.Tests.Unit **102/0**; Admin.Tests.Unit **546/0**.

### DIFF REVIEW — pass B2 — `ollama-cloud/kimi-k2.7-code`, read-only

Run `0d312610-ae6a-4441-a495-7e503ad66cae`, against `documents/rounds/diffs-keycloak-ui-auth-b2.patch`.

    REVIEW
    Verdict: PASS
    P1: none
    P2: tests/SchoolCollab.ArchitectureTests.Unit/PortalsSolutionItemsArchitectureTests.cs:148 — PortalSolutionFolderNodes_MirrorThePackageLayout still asserts the folder-node shape only for Portals; AuthPortal folder nodes are not pinned (B8/B9 will need to extend it when views/ is added)
    P2: api/auth_api_client.py:213 + tests/test_auth_api_client.py:253 — DELETE /auth/session/{id} expects {end_session_url}; round A's SessionEndpoints.Delete still returns {refreshToken} — a declared cross-pass dependency (B5 supersedes; owner confirmed), not a B2 bug
    P2: api/auth_api_client.py:201 + tests/test_auth_api_client.py:190 — GET /auth/session/{id} expects claims-as-data; round A returns metadata only — declared dependency (B5, D18)
    P2: api/auth_api_client.py:186 + tests/test_auth_api_client.py:139 — POST /auth/bootstrap/redeem is a new endpoint; B6 must implement {code, redirectUri} -> {sessionId, appRedirectUri?, appCode?}
    Best-practices: no overwrites / skills and conventions honored / readable

Verified: **scope conformance** — the changed-path set equals B2's 11 rows; no Core or auth-service C# files touched · **no overwrites** — the Portals reviewed-set pin and folder-node pin untouched by the generalization; round A's slnx entries intact · **conventions** — the `SchoolCollab.Portals` mirror shape honored (`api/__init__.py` present, none in `tests/`); no config key added, so the `configuration.md` non-touch is correct · **D7/D12/AC9/AC11** — no credential in the portal, no Keycloak call from the client, no token field on any DTO, the client refuses token-shaped bodies · **D17** — the portal calls only the auth service · **D18** — the session read returns claims as data and `session ended` is distinguishable · **AC10** — every degraded state renders at HTTP 200 · **guard honesty** — the generalized walk + anti-vacuity assertion and the AC11 structural scan are discriminating, and both probe axes are plausible · the session id is never echoed; no secret or token in a response body or a log line.

**Carried into later passes (parent):**
- **B5** must serve B2's pinned shapes: `DELETE /auth/session/{id}` → `{endSessionUrl}` (superseding round A's `{refreshToken}`); `GET /auth/session/{id}` → claims-as-data `{tenantId, tenantName, tenantType, teacherId, roles}` (+ optional `expiresInSeconds`); the portal keys D18 on `{"error":"session_ended"}` on any non-2xx, so the HTTP status choice stays B5's.
- **B6** must implement `POST /auth/bootstrap/redeem` `{code, redirectUri}` → `{sessionId, appRedirectUri?, appCode?}`.
- **B8/B9** must extend `PortalSolutionFolderNodes_MirrorThePackageLayout` to pin the AuthPortal folder nodes once `views/` exists (P2 above).

**Pass B2 — ACCEPTED.** Parent authoritative pass: `dotnet build SchoolCollab.slnx` 0 errors; ArchitectureTests.Unit **53/0**; AuthPortal pytest **36 passed**.

**Pass B3 — AppHost flag parameter, per-consumer fan-out, portal registration, transport** (`ollama-cloud/deepseek-v4.1-flash`; patch `documents/rounds/diffs-keycloak-ui-auth-b3.patch`, the 4 files below).

    WORKER REPORT (pass B3)
    Changed files (4 — all of B3's rows):
      src/AppHost/SchoolCollab.AppHost/appsettings.json (M — Parameters: "feature-flag-disable-keycloak-login-ui": "false");
      src/AppHost/SchoolCollab.AppHost/Program.cs (M — flag param; per-consumer fan-out; AddUvicornApp auth-portal + WithUv + WithReference(auth); WithReference(settingsApi/studentsApi) on auth; WithReference(auth) on admin+families);
      tests/SchoolCollab.ArchitectureTests.Unit/AppHostLoginUiFlagWiringArchitectureTests.cs (N — 7 tests, block-scoped source guard);
      documents/configuration.md (M — §2 param row, §4 new Auth:Portal:* subsection, §5 flag row, §11 five env-var rows, §12 checklist item).
    Build: 0 errors (13 pre-existing warnings, none from the changed files).
    Tests: ArchitectureTests.Unit 60 passed / 0 failed (53 pre-existing + 7 new); Core.Tests.Unit 99/0 (owns CrossModuleWiringTests, which parses the AppHost).
    Discriminating probes (invariant removed -> observed -> restored; git diff byte-identical after each):
      A remove admin's .WithReference(auth): 60/1 (HandshakeTransport_ReferencesSurviveOnBothSides). Axis: the P1-1/P1-3 handshake transport reference.
      B leak Auth:Portal:LoginUrl onto the auth service: 60/1 (PortalLoginUrl_ReachesTheBlazorHosts_AndIsAbsentFromTheAuthService). Axis: the P2-5 ABSENCE invariant (the fail-closed 401 story).
      C remove the portal's .WithUv(): 60/1 (AuthPortal_IsRegisteredAsAUvicornApp_WithUv_AndReferencesTheAuthService). Axis: uv registration shape.
      D replace the portal's flag fan-out with the literal "false": 60/1 (FlagFanOut_ReachesExactlyTheFourLoginUiConsumers). Axis: the value must come from the AppHost parameter.
      E flip the Parameters: default to "true": 60/1 (FlagParameter_IsDeclaredInProgram_AndDefaultsOffInAppSettings). Axis: D5 default OFF.
      F add a 5th fan-out (students-api): 60/1 (FlagFanOut_ReachesExactlyTheFourLoginUiConsumers). Axis: "exactly four" (presence AND extra-consumer halves).
    Deviations from plan:
      - documents/configuration.md also touches §4 (new Auth:Portal:* key table), §5 (Introduced-flags row) and §12 (checklist item) beyond the row's literal "§2 + §11" — required by configuration-documentation.md (per-section table + new-flag/production steps). Same file, same change set.
      - CROSS-PASS WIRE PINS B3 had to choose (the plan names the fan-outs, not the key/path names): AuthPortal:PublicBaseUrl (AuthPortal__PublicBaseUrl) on the portal, and Auth:Portal:BootstrapRedirectUrl = {auth-portal endpoint}/bootstrap on auth. Both derive from authPortal.GetEndpoint("http") (no hardcoded port). BootstrapRedirectUrl has NO reader yet by design (B6 reads it); B8 must serve the portal route GET /bootstrap and B5b's portal-side allowlist copy should contain that URI.
      - The portal's .WithReference(auth) + AuthPortal__PublicBaseUrl are written as one `authPortal = authPortal.…` reassignment after the auth resource — a cross-referencing pair (auth needs the portal endpoint; the portal needs auth's discovery env) cannot both be fully in-chain in a sequential host file; the guard's block parser absorbs reassignments.
      - Program.cs's diff also carries round A's still-uncommitted delta (the auth resource block, keycloak-auth-admin-secret, the Keycloak image bump) — pre-existing in the working tree, not a B3 edit (the same situation as B1's two files).
    Open risks: BootstrapRedirectUrl/PublicBaseUrl are ahead of their consumers (B5b/B6/B8) — a later rename is one line + one doc row; uv sync on the portal's first start and the live endpoint resolution of the three portal URLs are runtime-only (cold-start / UI-tester checks); no secret or token is placed anywhere new (the only new env values are a boolean, two absolute portal URLs and one portal base URL).

### DIFF REVIEW — pass B3 — `ollama-cloud/kimi-k2.7-code`, read-only

Run `cec29446-b60a-4f81-a378-c36037464f96`, against `documents/rounds/diffs-keycloak-ui-auth-b3.patch`.

    REVIEW
    Verdict: PASS
    P1: none
    P2: none
    Best-practices: no overwrites / skills honored / readable — guard is discriminating (block-scoped parser reaches all four consumers; "exactly four" asserts both presence and absence; the LoginUrl-absence check inspects the auth block AND counts total occurrences, so a leaked / renamed / unparsed fan-out fails; no plausible reformat or renamed key lets a broken host pass); round-A delta intact; existing architecture-guard files untouched.

Verified: scope conformance — exactly B3's 4 rows · no overwrites — round A's `Program.cs`/`configuration.md` delta intact, existing fan-out sites and resource blocks not reformatted, the five pre-existing architecture guards unharmed · D5 — the parameter is declared in the AppHost `Parameters:` block, defaults OFF, and is read at startup by the consumers · AC7 — flag OFF leaves the challenge path untouched · the P2-5 per-consumer split holds (`Auth:Portal:LoginUrl` → admin+families only, with its absence from the auth service asserted; `Auth:Portal:BootstrapRedirectUrl` → auth only) · no secret or token moved anywhere new (only a boolean and three URLs) · the `configuration.md` rows cover every new key/parameter in the same change set · cross-pass pins compatible with B5b (portal URI in its allowlist copy), B6 (`BootstrapRedirectUrl` reader) and B8 (`GET /bootstrap`).

**Pass B3 — ACCEPTED.** Parent authoritative pass: `dotnet build SchoolCollab.slnx` 0 errors; ArchitectureTests.Unit **60/0**; Core.Tests.Unit **99/0**.

**Pass B4 — Blazor host handshake redemption, login/logout, typed redeem client** (`ollama-cloud/deepseek-v4.1-flash`; patch `documents/rounds/diffs-keycloak-ui-auth-b4.patch`, the 4 files below).

    WORKER REPORT (pass B4)
    Changed files (exactly B4's 4 rows, no extras):
      src/SchoolCollab.Core/Auth/PortalHandshakeExtensions.cs (N — PortalHandshakeClient typed redemption client + PortalClaimSet/PortalRedemption + AddPortalHandshake(baseAddress) over AddCrossModuleHttpClient<T>(…, propagateTenant: true) + MapPortalHandshakeEndpoints() mapping /signin-handshake, /login, /logout as one group);
      src/SchoolCollab.Admin/Program.cs (M — AddPortalHandshake("https+http://auth") at :55; MapPortalHandshakeEndpoints() inside the existing if (!disableOIDC) gate at :103-110);
      src/SchoolCollab.Families/Program.cs (M — same two edits at :78-84 and :141-148);
      tests/SchoolCollab.Core.Tests.Unit/Auth/PortalHandshakeTests.cs (N — 11 tests on a real in-process Kestrel host composed from the public extensions, with a scripted auth-service handler).
    Build: 0 errors (13 pre-existing NuGet-advisory warnings, none from the changed files).
    Tests: Core.Tests.Unit 110 passed / 0 failed (99 pre-existing + 11 new); ArchitectureTests.Unit 60/0; Families.Tests.Unit 42/0; Admin.Tests.Unit 546/0; Auth.Tests.Unit 102/0 (smoke, untouched project).
    Deviations from plan:
      - None in scope. documents/configuration.md deliberately untouched: B4 adds no config key (the base address is a parameter; Auth:Portal:LoginUrl and the flag were documented in B3), so the config-rows rule has nothing to carry.
      - /login and /logout do NOT branch on the flag themselves — /login defers to the startup-selected challenge (Results.Challenge(…, RedirectUri = local returnUrl)), so the flag-ON portal redirect is B1's PortalRedirectChallengeHandler and flag-OFF is Keycloak: one spelling of the login-UI choice, no duplicated redirect construction, AC7 untouched (proved by probe E). /logout is Results.SignOut([Cookie, OIDC]) under both flag states (D13).
      - /signout-callback-oidc is intentionally NOT mapped (not in B4's rows). The OIDC handler derives post_logout_redirect_uri from its own SignedOutCallbackPath — empirically observed, matching the per-host …/signout-callback-oidc URIs the realm registers — so the worker flags the live post-logout landing as hitting a path nobody serves yet: OPEN RISK for the cold-start / UI-tester pass, not a unit assertion. (See the diff review above for the adjudication.)
      - The handshake's rejection status is a single 401 for every upstream failure class (the class stays in the log) rather than mirroring the auth service's statuses — a deliberate anti-oracle choice, documented in the code.
    Discriminating probes (invariant removed -> observed -> restored; all 4 files md5-verified byte-identical afterwards):
      A drop the escaped ReturnUrl from the rebuilt callback URI: 110 total / 1 failed (Handshake_PostsTheCodeToTheAuthService_BoundToThisHostsExactCallbackUri). Axis: the exact redirect-URI binding the auth service enforces.
      B emit roles as a literal "roles" claim instead of ClaimTypes.Role: 1 failed (Handshake_WithAValidCode_SignsTheCallerIntoTheHostCookieWithTheOidcClaimSet). Axis: D9 claim parity so RequireRole resolves on the handshake path.
      C rejection path returns a redirect instead of 401: 4 failed (already-redeemed, expired, unreachable, no-code). Axis: fail-closed — no cookie, no Location, one bounded status, no failure class disclosed.
      D drop the local-path guard on the post-handshake redirect: 1 failed (Handshake_WhenReturnUrlIsNotSiteLocal_DoesNotRedirectOffSite). Axis: open-redirect guard.
      E force /login to build a portal redirect itself instead of challenging: 2 failed (both flag-state login tests). Axis: AC7 / the login UI must be chosen by the startup-selected challenge, not by the endpoint.
      F sign out only the cookie (drop the OIDC scheme): 1 failed (Logout_SignsOutThroughTheOidcHandler). Axis: D13 OIDC end_session sign-out.
      G drop the blank-code guard: 1 failed (Handshake_WithoutACode_NeverCallsTheAuthService). Axis: no upstream call (and no sign-in) without a code.
      H move the literal "https+http://auth" into Core (ignoring the parameter): 1 failed (CrossModule_BaseAddresses_HaveMatchingAppHostReference). Axis: P1-1 — the literal must live at the two hosts' call sites, so the guard holds for the four Core consumers not wired to auth.

### DIFF REVIEW — pass B4 — `ollama-cloud/kimi-k2.7-code`, read-only

Run `35110404-5ae3-4030-896b-b7d6dde7fe16` **failed with a transport error and produced no output**; resumed same-protocol as `4d5dfefe-b60e-4c4c-a390-47d8057233a9`. Verdict against `documents/rounds/diffs-keycloak-ui-auth-b4.patch`.

    REVIEW
    Verdict: P1
    P1: src/SchoolCollab.Core/Auth/PortalHandshakeExtensions.cs:84 — ReadFromJsonAsync<PortalClaimSet> uses ambient/default JSON options instead of the repo's JsonSerializerDefaults.Web convention. /auth/redeem returns camelCase (tenantId, tenantName, teacherId, roles — verified at tests/SchoolCollab.Auth.Tests.Unit/RedeemEndpointTests.cs:77) while PortalClaimSet declares PascalCase properties; with System.Net.Http.Json default options the deserialization returns null, so a valid code is rejected as redeem_failed and the handshake cannot sign the caller in. Fix: a static JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) passed to BOTH PostAsJsonAsync (:74) and ReadFromJsonAsync (:84), matching every other cross-service client in the repo.
    P2: none
    Best-practices: no overwrites / skills honored / readable — the two Program.cs edits are minimal and additive; existing gates and endpoint groups intact; the new tests cover every behavioural axis the worker claimed.

Adjudications (both worker open risks resolved): **cross-pass callback spelling is CONSISTENT** — B4's `BuildCallbackUri` rebuilds the exact `{scheme}://{host}{pathBase}/signin-handshake?ReturnUrl={escaped}` shape that B1's `PortalRedirectChallengeHandler` mints, so B5b's exact path-prefix allowlist matches before the nested `ReturnUrl` query and round A's `OneTimeCodeStore` exact-string binding will work provided B8 passes the `return_uri` through unchanged. **The worker's `/signout-callback-oidc` concern is a MISREADING** — ASP.NET Core's OIDC `RemoteAuthenticationHandler.ShouldHandleRequestAsync` serves `SignedOutCallbackPath` automatically when `UseAuthentication()` runs, and the realm already registers that path as the post-logout target, so B4 correctly leaves it unmapped.

**Rework (reviewer P1 → worker rework, Tier-3 iteration 1 of ≤2): in flight** — resume of the B4 worker run.

**Rework iteration 1 — outcome: the P1's failure mode is REFUTED; the fix is retained as hardening.**

    WORKER REPORT (pass B4, rework 1)
    Changed files (inside B4's 4 rows): src/SchoolCollab.Core/Auth/PortalHandshakeExtensions.cs (M — static JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web), passed to BOTH PostAsJsonAsync and ReadFromJsonAsync); tests/SchoolCollab.Core.Tests.Unit/Auth/PortalHandshakeTests.cs (M — the scripted service now emits camelCase BYTES via a PascalCase ServiceClaimSet serialized with Web defaults, per RedeemEndpointTests.cs:77, plus a pin asserting the fixture body contains "tenantId"/"teacherId"); the two Program.cs files md5-unchanged.
    Build: 0 errors. Tests: Core.Tests.Unit 110/0; ArchitectureTests.Unit 60/0; Families 42/0; Admin 546/0; Auth 102/0.
    Probes: Q1 (client options forced to JsonSerializerDefaults.General, i.e. case-sensitive): 110 total / 5 failed — the REAL discriminating axis. Q2 (producer emits PascalCase): 110 / 1 failed on the new fixture pin (Q2 alone is NOT discriminating for the read binding — Web options are case-insensitive by design). Q3 (exact pre-fix code + camelCase producer): 110 / 0 failed — the counter-evidence.
    Incidental: an earlier probe's text-mode restore had flattened the test file to LF; CRLF bytes restored (md5 back to the pre-probe state) and a stray typo in a <see cref> tag corrected.

**Parent adjudication (verified directly against the frozen artifact, not taken on trust).** The reviewer's P1 premise is **FALSE**:
1. The ORIGINAL fixture was a **hand-written camelCase JSON literal** — `{"tenantId":…,"tenantName":"Dev School","tenantType":"School",…}` at `diffs-keycloak-ui-auth-b4.patch:450` — not a serializer call using the SUT's options, so the "self-consistent mock" reasoning does not apply to it.
2. The pre-fix client used the **parameterless** overloads (`:111` `PostAsJsonAsync(…)`, `:121` `ReadFromJsonAsync<PortalClaimSet>(cancellationToken)`).
3. `System.Net.Http.Json`'s parameterless overloads default to `JsonSerializerDefaults.Web` (camelCase naming + case-insensitive reads). The well-known asymmetry is with `JsonSerializer.Serialize` (General), not with `HttpClient`'s JSON extensions — so the camelCase payload bound correctly before the fix.
4. Empirically: Q3 (pre-fix code + camelCase producer) → 110/0; Q1 (case-sensitive General) → 5 failures. The tests genuinely exercise the casing axis.

Therefore the P1 is **downgraded to P2-grade hardening**: the explicit `JsonSerializerDefaults.Web` is still worth keeping (it matches every other cross-service client and removes reliance on ambient defaults), and the reworked fixture — which now emits camelCase bytes through Web defaults and pins them — genuinely strengthens the test. **No behavioural defect was ever present.**

### DIFF REVIEW — pass B4 — re-verify (iteration 1 re-check) — `ollama-cloud/kimi-k2.7-code`, read-only

Run `653cfe3d-7786-4bd3-92d0-94ff4fb5b88c` (resumed from `4d5dfefe`, itself resumed from the transport-failed `35110404`), against `documents/rounds/diffs-keycloak-ui-auth-b4-fix1.patch` plus the current sources.

    REVIEW
    Verdict: PASS
    P1: none
    P2: src/SchoolCollab.Core/Auth/PortalHandshakeExtensions.cs:72 — the explicit JsonSerializerDefaults.Web options are defensive hardening, not a required functional fix; System.Net.Http.Json's parameterless overloads already default to Web (camelCase + PropertyNameCaseInsensitive), so the prior P1 failure mode was a false positive.
    Best-practices: no overwrites / skills honored / readable — the rework diff touches only the two B4 files and leaves both Program.cs files byte-identical to the prior B4 state. All previously verified axes remain intact: D9 ClaimTypes.Role parity in CreatePrincipal, single-401 fail-closed, site-local ReturnUrl guard, no-code-no-upstream-call, AC7 login-challenge deferral through the policy scheme, D13 OIDC sign-out, and the P1-1 literal-at-call-site base address. The reworked test genuinely pins producer-side wire casing (PascalCase ServiceClaimSet serialized with Web defaults + a bytes assertion; a case-sensitive client would go red). Cross-pass callback spelling remains consistent with B5b's exact-prefix allowlist and round A's exact-string OneTimeCodeStore binding; /signout-callback-oidc is still correctly served by RemoteAuthenticationHandler.

**P1 WITHDRAWN as a false positive** — independently verified by the parent against the frozen artifact (`diffs-keycloak-ui-auth-b4.patch:450` fixture, `:111`/`:121` parameterless overloads). The fix is retained as **P2-grade hardening**; the residual P2 is carried.

**Pass B4 — ACCEPTED.** Parent authoritative pass (post-fix): `dotnet build SchoolCollab.slnx` 0 errors; Core.Tests.Unit **110/0**; ArchitectureTests.Unit **60/0**; Families.Tests.Unit **42/0**; Admin.Tests.Unit **546/0**.

**Pass B5 — auth-service D15 seam, D18 lifecycle, portal-session auth scheme** (`ollama-cloud/deepseek-v4.1-flash`; patch `documents/rounds/diffs-keycloak-ui-auth-b5.patch`, the 9 files below).

    WORKER REPORT (pass B5)
    Changed files (9 — exactly B5's 9 rows; no extras):
      src/SchoolCollab.Auth/Providers/IAuthProvider.cs (N — the ONE D15 seam: 7 members = D15's 7 operations; token-free portal-facing result records);
      src/SchoolCollab.Auth/Providers/KeycloakAuthProvider.cs (N — the single implementation: authenticate/claims delegate to round A's DirectGrantExchanger/ClaimSetFactory, custody to PortalSessionStore, plus the D18 refresh lifecycle and the D13 server-side refresh-token revocation + end_session URL builder);
      src/SchoolCollab.Auth/Auth/PortalSessionAuthenticationHandler.cs (N — opaque session id in X-Portal-Session → principal with the D9 claim set incl. ClaimTypes.Role; NoResult + bare 401 fail-closed; plus an always-registered gateway policy scheme);
      src/SchoolCollab.Auth/Endpoints/SessionEndpoints.cs (M — GET = claims as data {sessionId,tenantId,tenantName,tenantType,teacherId,roles,expiresInSeconds}; 404 session_not_found vs 410 session_ended vs 502 upstream_unreachable; DELETE = server-side revocation + {endSessionUrl}, round A's {refreshToken} superseded);
      src/SchoolCollab.Auth/AuthServiceExtensions.cs (M, 1st of 4 — provider typed client + singleton seam mapping, portal-session scheme + gateway policy scheme);
      src/SchoolCollab.Auth/Endpoints/AuthEndpointGroup.cs (M, 1st of 2 — portal-facing admin policy via the gateway scheme; the bearer user-admin gate requirement itself untouched);
      tests/SchoolCollab.Auth.Tests.Unit/KeycloakAuthProviderTests.cs (N — 19);
      tests/SchoolCollab.Auth.Tests.Unit/PortalSessionAuthenticationHandlerTests.cs (N — 9);
      tests/SchoolCollab.Auth.Tests.Unit/SessionEndpointTests.cs (M — 10, incl. the superseded DELETE assertion).
    Build: 0 errors (15 pre-existing warnings, none from the changed files), after every change.
    Tests: Auth.Tests.Unit 135 passed / 0 failed (102 at the B4 base; +33 new); ArchitectureTests.Unit 60/0; Core.Tests.Unit 110/0.
    Discriminating probes (invariant removed -> observed -> restored; md5-verified byte-identical afterwards):
      A gateway selector ignored the session header: 135/2 (the two portal-session admin-policy tests). Axis: the policy attachment is real, not vacuous.
      B end_session URL carried an id_token_hint: 135/2 (the DELETE no-token scan + the provider's token-free URL test). Axis: AC11 / B-E "no id_token in ANY response body, including DELETE".
      C refresh-rejection branch removed (400/401 -> UpstreamUnreachable): 135/2 (session_ended distinctness, provider + endpoint). Axis: D18's session_ended taxonomy.
    Deviations from plan:
      1. portal-session policy mechanics: the policy names ONE always-registered gateway scheme instead of enumerating PortalSession/Bearer/Cookies/TestAuth. Listing schemes whose handler is absent in the other flag state is a 500, not a 401 — proven empirically (16 round-A admin tests red-lit with "No authentication handler is registered for the scheme 'TestAuth'"). The gateway forwards authentication to the app's own default scheme, so cookie/bearer/TestAuth behave exactly as before and the challenge is the bare 401 the round-A gate pins.
      2. D13 PARAMETER-LIST DEVIATION (flagged for reviewer/parent adjudication): the returned end_session URL is {authority}/protocol/openid-connect/logout?client_id={clientId} — NO id_token_hint and NO post_logout_redirect_uri. Reason: this pass's own contract (and AC11) forbids any id_token in ANY response body, and an id_token_hint in the URL is that token; the realm registers no post-logout URI for school-collab-client (realm edits are B6's), so passing one would be rejected. Keycloak accepts client_id when id_token_hint is absent. The live logout landing is a cold-start/UI-tester check.
      3. Refresh persistence gap (design note, no scope widening): D18's refresh-in-custody needs to replace the custody token set, but round A's PortalSessionStore exposes create/read/revoke only and is NOT in B5's rows. The provider therefore tracks refreshed (rotated) token sets in-process, keyed by session id, bounded/swept like the store and dropped on revoke/logout; the token-existence invariant is unchanged (server-side only, never serialized, never logged). Cross-pass note for B7: read tokens through IAuthProvider, not PortalSessionStore directly, or a stale access token gets forwarded.
      4. No documents/configuration.md row: B5 adds no config key (the provider reuses Auth:Keycloak:*).
      5. New cross-pass contract pinned for B9: the portal presents its session id in the X-Portal-Session header (PortalSessionAuthenticationHandler.SessionHeaderName).

### DIFF REVIEW — pass B5 — `ollama-cloud/kimi-k2.7-code`, read-only

Run `c5f0d797-5897-4f8b-a87c-5bc913c9d932`, against `documents/rounds/diffs-keycloak-ui-auth-b5.patch`.

    REVIEW
    Verdict: P1
    P1: documents/rounds/diffs-keycloak-ui-auth-b5.patch — the frozen diff contained only 5 files; the 4 modified files required by B5's rows were missing (SessionEndpoints.cs, AuthServiceExtensions.cs, AuthEndpointGroup.cs, SessionEndpointTests.cs). The working tree has the B5 content; the artifact did not match the plan's 9-file baseline.
    P1: src/SchoolCollab.Auth/Providers/KeycloakAuthProvider.cs:290 — BuildEndSessionUrl() returns {authority}/protocol/openid-connect/logout?client_id={clientId} with neither id_token_hint nor post_logout_redirect_uri; deviates from D13/§9 and weakens RP-initiated logout (Keycloak confirmation page, may not terminate the specific SSO session, no post-logout landing). Adjudication offered: option (ii) — restore D13 by returning the URL WITH id_token_hint + post_logout_redirect_uri; and make the raw-body scans distinguish URL parameter NAMES from token VALUES.
    P2: KeycloakAuthProvider.cs:70/375 — refreshed token sets held in a second in-process _refreshed map instead of replacing the custody entry: two sources of truth; a direct PortalSessionStore reader sees stale tokens; a restart loses the rotated refresh token. Recommend a parent-authorized PortalSessionStore token-replace method rather than widening B5 scope.
    P2: KeycloakAuthProvider.cs:152 — the refresh/expiry race: if the custody entry expires during the async refresh, CurrentTokens returns null and tokens!.IdToken throws a 500 instead of a clean session-state response.
    Best-practices: skills honored; extension methods/no-inline-registration conventions followed; new behavioural classes have unit tests; round-A exchange/redeem/admin behaviour and the bearer user-admin role gate preserved; no unrelated reformatting. Gateway-scheme rationale and fail-closed NoResult + bare-401 behaviour consistent with the code.

**P1-1 (incomplete patch) — PARENT FREEZE ERROR, not the worker's.** Root cause: the whole `src/SchoolCollab.Auth/` and `tests/SchoolCollab.Auth.Tests.Unit/` trees are **untracked** (round A is uncommitted), so `git diff` reports nothing for them unless every file is `git add -N`'d first; the parent intent-added only the 5 files believed to be new, so B5's 4 "M" rows were silently omitted. Re-frozen with all 9 intent-added and the file list verified against the rows. **Process lesson (recorded): a patch freeze must diff its file list against the pass's expected-files rows BEFORE the reviewer is dispatched, and any pass touching the untracked `SchoolCollab.Auth` / `SchoolCollab.AuthPortal` trees must intent-add every touched file.** The reviewer's other findings are unaffected — it reviewed the working-tree content.

**P1-2 (D13) — PARENT ADJUDICATION: option (iii), carried to B6.** The reviewer recommended (ii) (return the URL *containing* the hint). The parent chooses **(iii)**: the round restores D13 by having the **auth service perform a browser-facing logout redirect** that carries `id_token_hint` + `post_logout_redirect_uri` in a `Location` header — so the id token never enters a response body and never reaches Python state, honouring the owner's explicit ruling ("no keycloak token in python state. That should read from c# auth"). Rationale: (ii) is defensible but puts a Keycloak id token in Python memory, which is exactly what the owner ruled out; (iii) matches what the Blazor hosts already do (their OIDC handler redirects the browser to `end_session` with `id_token_hint`). **Consequences:** B5's `BuildEndSessionUrl()` stays token-free (documented in-code); **B6** adds the browser-facing logout redirect endpoint *and* registers `school-collab-client`'s post-logout redirect URI; **B8**'s portal logout becomes a browser redirect to the auth service rather than a URL string; the spec needs a one-line D13 clarification. The round doc's expected-files rows for B6/B8 are amended accordingly.

**Plan amendment (parent, authorized):** `src/SchoolCollab.Auth/Services/PortalSessionStore.cs` and `tests/SchoolCollab.Auth.Tests.Unit/PortalSessionStoreTests.cs` are **added to B5's rows** (cross-pass touch, the round-A `OneTimeCodeStore` precedent) so the refresh path replaces the custody tokens in place instead of keeping a second in-process map.

**Rework (Tier-3 iteration 1 of ≤2): in flight.**

**Rework iteration 1 — applied (5 files inside B5's rows plus the 2 parent-authorized).**

    WORKER REPORT (pass B5, rework 1)
    Changed files (5): src/SchoolCollab.Auth/Services/PortalSessionStore.cs (M, NEWLY AUTHORIZED — PortalSessionEntry gains AccessTokenExpiresAtUtc; new ReplaceTokens(sessionId, accessToken, refreshToken, idToken, expiresInSeconds) -> PortalSessionEntry? — a value-compared TryUpdate swap that keeps the session id and the session's absolute expiry, returns the authoritative replacement, and returns null on unknown/expired (nothing written, no session created or resurrected));
      src/SchoolCollab.Auth/Providers/KeycloakAuthProvider.cs (M — the _refreshed map, Track/SweepDeadRefreshes/TrackedTokenSet/MaxTrackedRefreshes/SweepEveryNRefreshes and the ExpiresAtUtc - PortalSessionTtl derivation ALL removed; refresh writes through ReplaceTokens and the read serves claims from what the store wrote; a null replacement is the D18 session_ended state; BuildEndSessionUrl() unchanged, comment records the parent's option (iii));
      tests/SchoolCollab.Auth.Tests.Unit/PortalSessionStoreTests.cs (M, NEWLY AUTHORIZED — 3 tests: replace keeps the id + absolute expiry and revocation still hands out the ROTATED refresh token; replace on unknown OR expired is REJECTED (null, nothing written, no resurrect) and the dead entry stays gone; replace does not extend the session TTL);
      tests/SchoolCollab.Auth.Tests.Unit/SessionEndpointTests.cs (M — value-based scans; new Get_SessionExpiresWhileTheRefreshIsInFlight_IsSessionEnded_NotA500);
      tests/SchoolCollab.Auth.Tests.Unit/KeycloakAuthProviderTests.cs (M — value-based URL scan; the rotation test now also pins the custody store itself as the single source of truth (sessions.Get(id).RefreshToken == fresh); new Refresh_SessionExpiresWhileKeycloakAnswers_EndsTheSession_InsteadOfThrowing).
    Build: 0 errors after every change (13 pre-existing warnings; the removed overlay took two with it).
    Tests: Auth.Tests.Unit 140 passed / 0 failed (135 + 5 new); ArchitectureTests.Unit 60/0.
    Discriminating probes (invariant removed -> exact counts -> md5-verified byte-identical restore):
      1 no store swap on refresh: 140/2 (rotated-token test + the transparent-refresh read). Axis: D18 refresh must persist into the ONE custody store.
      2 store replace ignores expiry: 140/3 (store rejection test + both mid-flight-expiry tests). Axis: replace rejects unknown/expired and is what decides the end state.
      3 vanished session treated as a successful refresh (resurrect): 140/2 (both mid-flight-expiry tests). Axis: P2-2's race yields D18 session_ended, not a resurrected session.
      4 a token VALUE appended to the logout URL: 140/3 (both DELETE/body scans + the URL scan). Axis: the value-based response scans.
      5 gateway ignores the session header: 140/2 (both portal-session policy tests). Axis: the portal-facing policy attachment.
      6 Keycloak-rejected refresh reported as an outage: 140/2 (provider + endpoint taxonomy tests). Axis: D18 session_ended vs upstream_unreachable.
    Deviations / notes:
      1. P2-2 is fixed more strongly than requested, and the worker's FIRST attempt at the literal guard was rejected for honesty: the post-refresh re-read guard was proved NON-DISCRIMINATING (probe F: 140/0) because the window is already closed by the store's atomic swap — an unreachable null-check. It was removed rather than shipped as a vacuous guard; the race is now decided inside ReplaceTokens (expiry check and write in the same call) -> null -> SessionEnded -> the endpoints' clean D18 response.
      2. ReplaceTokens returns the authoritative entry (not a bool) so the read path needs no second read of custody — that is what closes the race.
      3. PortalSessionEntry gained AccessTokenExpiresAtUtc (same authorized file): the custody record is now the one truthful place for both the session's and the token set's expiry.
      4. D13: URL unchanged (token-free, client_id only); only a comment records option (iii) and that the logout endpoint + realm post-logout URI are B6's.
      5. Value-based scans applied to B5's scans only; round A's RedeemEndpointTests.cs (a key-name scan) is not a B5 row and was left untouched.
      6. No config key added, so documents/configuration.md stays untouched.

**Pass B5 — ACCEPTED, with one scoped caveat.** Parent authoritative pass (post-rework): `dotnet build SchoolCollab.slnx` 0 errors; Auth.Tests.Unit **140/0**; ArchitectureTests.Unit **60/0**; Core.Tests.Unit **110/0**. **Caveat:** the D13 *mechanism* is an open owner decision — B5's `BuildEndSessionUrl()` is token-free as adjudicated under option (iii); if the owner instead chooses (ii), the delta is one method in this file (add `id_token_hint` + `post_logout_redirect_uri`) and would be folded into the B6 change set rather than a new round. Everything else in B5 stands closed.

### DIFF REVIEW — pass B5 — re-verify (rework iteration 1 re-check) — `ollama-cloud/kimi-k2.7-code`, read-only

Run `8bca37b3-ef80-4556-9356-80df1787bc10` (resumed from `c5f0d797`), against the re-frozen 11-file `documents/rounds/diffs-keycloak-ui-auth-b5.patch`.

    REVIEW
    Verdict: PASS
    P1: none
    P2: none
    Best-practices: no overwrites / skills honored / readable; D13 correctly deferred to B6 with accurate in-code rationale; refresh race closed by atomic ReplaceTokens CAS so no vacuous guard needed; value-based no-token scans verified

**Both P1s closed** (P1-1 by the parent correcting the freeze — 11 files now, matching the amended rows; P1-2 by the parent's option-(iii) adjudication, D13 restoration carried to B6) and **both P2s closed** (single custody store via the authorized `ReplaceTokens`; the race decided inside the atomic swap rather than by a vacuous guard). **Pass B5 — CLOSED.**

**Pass B5b — per-app callback allowlist (the plan-review P1 security fix)** (`ollama-cloud/deepseek-v4.1-flash`; patch `documents/rounds/diffs-keycloak-ui-auth-b5b.patch`, the 11 files below).

    WORKER REPORT (pass B5b)
    Changed files (11 — B5b's 10 rows plus the parent-amended AuthServiceSmokeTests row):
      src/SchoolCollab.Auth/Options/AuthServiceOptions.cs (M — AppCallbackPrefixes + non-empty ValidateOnStart guard);
      src/SchoolCollab.Auth/Services/AppCallbackAllowlist.cs (N — fail-closed exact-prefix matcher (IOptions ctor + value ctor));
      src/SchoolCollab.Auth/Endpoints/ExchangeEndpoints.cs (M — allowlist enforced at issuance, before the Direct-Grant call);
      src/SchoolCollab.Auth/AuthServiceExtensions.cs (M — AddSingleton<AppCallbackAllowlist>());
      src/AppHost/SchoolCollab.AppHost/Program.cs (M — one parameter → Auth__AppCallbackPrefixes (auth: the 4 handshake callbacks + the portal bootstrap URI from its endpoint) / AuthPortal__AppCallbackPrefixes (portal: app callbacks only));
      tests/SchoolCollab.Auth.Tests.Unit/AppCallbackAllowlistTests.cs (N — 15 tests / 27 rows: exact-prefix, attacker host, malformed both sides, empty = deny);
      tests/SchoolCollab.Auth.Tests.Unit/ExchangeEndpointTests.cs (M — allowlisted default URI + nested-ReturnUrl positive + non-allowlisted rejection (no code, no upstream call));
      tests/SchoolCollab.Auth.Tests.Unit/AuthServiceOptionsTests.cs (M — Valid() + Validator_RejectsEmptyAppCallbackPrefixes);
      tests/SchoolCollab.Auth.Tests.Unit/AuthEndpointTestHost.cs (M — in-memory config gains Auth:AppCallbackPrefixes);
      tests/SchoolCollab.Auth.Tests.Unit/AuthServiceSmokeTests.cs (M — parent-authorised 3rd fixture);
      documents/configuration.md (M — §2 parameter row + §4 subsection/rows for both keys + §11 env rows).
    Build: 0 errors (13 pre-existing warnings, none from the B5b files).
    Tests: Auth.Tests.Unit 169 passed / 0 failed; ArchitectureTests.Unit 60/0 (its AppHost guards read the edited Program.cs).
    Probes (remove invariant -> exact counts -> restore, md5-verified byte-identical):
      P1 component match -> loose substring: 7 failed (incl. Exchange_NonAllowlistedRedirectUri returning 200 = a code minted). Axis: the matcher must be exact-prefix, not substring.
      P2 empty allowlist returns true (fail-open): 5 failed. Axis: default-deny.
      P3 validator guard off: 2 failed. Axis: the startup non-empty rule.
      P4 /auth/exchange issuance guard off: 1 failed (attacker URI -> 200 with code). Axis: the load-bearing security control.
      P5 test-host config key removed: 36 failed (every composed-host test red at start). Axis: the AuthEndpointTestHost row's rationale.
      P6 query no longer ignored (PathAndQuery equality): 4 failed (incl. B4's nested-ReturnUrl handshake -> 400). Axis: B4's query-bearing callback spelling must match.
    Fixture sweep (parent-requested): validator-acceptance fixtures exist in exactly 2 files — AuthServiceOptionsTests.Valid() and AuthServiceSmokeTests:41-50. The 6 other `new AuthServiceOptions` sites (DirectGrantExchangeTests:27, KeycloakAdminClientTests:94, OneTimeCodeStoreTests:31, PortalSessionStoreTests:29, RedeemEndpointTests:193 EndpointTestSupport.OptionsFor, SessionEndpointTests:327) are OptionsWrapper-only and never run ValidateOnStart; AddAuthServiceOptions is called only by Program.cs and AuthEndpointTestHost. **No fourth instance.**
    Deviations from plan:
      1. AuthServiceSmokeTests.cs — necessarily touched by the approved ValidateOnStart change (its "complete configuration" fixture went red: 1 failure before the one-line fix); escalated, parent authorised, and added as a B5b row.
      2. documents/configuration.md §2 — one extra parameter row beyond the pinned §4/§11 (required by configuration-documentation.md for a new AddParameter call). No other section touched.
      3. No appsettings.json Parameters entry: the new parameter carries its dev default inline (AddParameter("app-callback-prefixes", "<4 handshake callbacks>"), precedent keycloak-client-id), keeping the touched-file set exactly B5b's rows; §2 documents it as an inline default. Open to a one-line move to appsettings.json if the parent prefers.
    Open risks / carries:
      - Fan-out manifest-verified: `dotnet run --publisher manifest` shows Auth__AppCallbackPrefixes = {app-callback-prefixes.value};{auth-portal.bindings.http.url}/bootstrap and AuthPortal__AppCallbackPrefixes = {app-callback-prefixes.value} (real reference expressions, no hardcoded port, no stringified parameter name).
      - New rejection body: 400 {"error":"redirect_uri_not_allowed"} (the existing redirect_uri_required is untouched) — **B8's error-card mapping needs that code**; **B6 must call the same AppCallbackAllowlist for /auth/passkey/***.
      - The portal's copy deliberately EXCLUDES the portal's own bootstrap URI (the portal validates app callbacks only; the bootstrap URI belongs on the AUTH side where issuance happens).

### DIFF REVIEW — pass B5b — `ollama-cloud/kimi-k2.7-code`, read-only

Run `119483a3-1918-4994-b005-d79733bf6a46` **timed out at 30 min with no output** (an over-heavy read: the 1831-line patch largely as full-file additions, plus the whole plan, plus the spec, plus seven judgment areas). Re-dispatched **narrowed** as `3eb430cb-0b56-47b6-b25d-d9917bc5d957` — five target files, four judgments, 45-min timeout; same model, same pass, same frozen artifact.

    REVIEW
    Verdict: P1
    P1: src/SchoolCollab.Auth/Services/AppCallbackAllowlist.cs:67 — the path match uses candidate.AbsolutePath.StartsWith(prefix.Path, Ordinal), so http://localhost:5300/signin-handshake-evil and http://localhost:5300/signin-handshakeX pass the allowlist; the control is not genuinely exact on path. Fix: require an exact path, or a prefix followed by '/'
    P1: tests/SchoolCollab.Auth.Tests.Unit/AppCallbackAllowlistTests.cs:51-92 — the suite exercises slash-terminated path extensions and wrong paths but never asserts that …/signin-handshake-evil or …/signin-handshakeX are REJECTED, so the suffix/prefix confusion above is not caught
    P2: src/SchoolCollab.Auth/Services/AppCallbackAllowlist.cs:57 — userinfo is not rejected, so http://attacker@localhost:5300/signin-handshake parses with host=localhost (Uri.UserInfo="attacker") and is accepted; add a UserInfo.Length == 0 guard
    Best-practices: scope limited to B5b's rows; no unrelated `new AuthServiceOptions` sites altered; enforcement is before the Direct-Grant call and the rejection carries no code; the inline dev default is ACCEPTABLE (it mirrors keycloak-client-id's non-secret literal, stays env/user-secret overridable, and is not a committed production secret)

**Parent note.** The parent's own pre-check reached the same structural conclusion (component-wise on a parsed `Uri`; default-deny; the classic evil-substring trap documented and rejected) and had independently flagged the path-prefix boundary as the debatable point — the reviewer confirmed it as a **defect**, not a nuance. **The userinfo P2 is a shape the parent's pre-check got WRONG**: it reasoned that userinfo resolves host=evil for `http://localhost:5300@evil/…` (true, and rejected), but `http://attacker@localhost:5300/…` resolves host=localhost with `UserInfo="attacker"` and passes (false). An independent reviewer catching the parent's blind spot is precisely the value of this pass.

**Rework (P1 -> worker rework, Tier-3 iteration 1 of ≤2): in flight.**

**Rework iteration 1 — applied (2 files, both inside B5b's rows; nothing else in the pass changed).**

    WORKER REPORT (pass B5b, rework 1)
    Changed files (2): src/SchoolCollab.Auth/Services/AppCallbackAllowlist.cs (M — P1-1 path boundary + NormalizePath; P2 userinfo on both the candidate and the configured entry; doc/comment updates); tests/SchoolCollab.Auth.Tests.Unit/AppCallbackAllowlistTests.cs (M — P1-2 accept/reject cases + 2 userinfo-direction tests; 4 new methods).
    Build: 0 errors. Tests: Auth.Tests.Unit 173 passed / 0 failed (was 169; +4); ArchitectureTests.Unit 60/0.
    Fixes: P1-1 — MatchesPath(prefixPath, candidatePath) now accepts only candidatePath == prefixPath or candidatePath.StartsWith(prefixPath + "/"), and Parse stores NormalizePath(AbsolutePath) (trailing / stripped, bare / kept) so the comparison can never build a //; B4's spelling still matches (…/signin-handshake?ReturnUrl=<escaped> has AbsolutePath == /signin-handshake → exact). P1-2 — added IsAllowed_AcceptsATrailingSlashSpellingOfAnAllowlistedPath and IsAllowed_RejectsASuffixAppendedToAnAllowlistedPath (-evil, X, -evil?ReturnUrl=…); the exact + B4-query accept tests stay. P2 — candidate.UserInfo.Length > 0 → deny; parsed.UserInfo.Length > 0 → entry dropped, with tests for both directions (the entry-direction test is designed so it cannot pass if the entry is merely kept, since userinfo is not a compared component).
    Probes (remove invariant -> exact counts -> restore, md5-verified byte-identical):
      A1 path boundary reverted to plain StartsWith: 1 failed — IsAllowed_RejectsASuffixAppendedToAnAllowlistedPath (the new reject test; the pre-fix code passed it).
      A2 path match reduced to exact-only: 2 failed — the two accept-side tests, i.e. the fix did not over-tighten and B4's spelling is pinned.
      B both userinfo guards removed: 2 failed — one per direction, each independently discriminating.
    Deliberate consequence: with the boundary rule, a bare `/` entry (e.g. http://host:5300/) now matches the root path only rather than every path on that origin — fail-closed; no real callback entry is a bare origin.

**Re-verify: in flight.**

### DIFF REVIEW — pass B5b — re-verify (rework iteration 1 re-check) — `ollama-cloud/kimi-k2.7-code`, read-only

Run `cd5af5dc-5ffd-43eb-8def-87a6a5634066` (resumed from the narrowed `3eb430cb`), against the re-frozen 11-file `documents/rounds/diffs-keycloak-ui-auth-b5b.patch`.

    REVIEW
    Verdict: PASS
    P1: none
    P2: none
    Best-practices: scope limited to B5b rows only; no unrelated overwrites; enforcement remains before the Direct-Grant call with a token/code-free redirect_uri_not_allowed body; readable.

**Both P1s and the P2 closed**: the path boundary is now exact-or-`/`-boundary with a normalized stored path (B4's query spelling still an exact match, suffix shapes rejected), the missing rejection cases are asserted, and userinfo is rejected on the candidate and on the configured entry — each direction independently discriminating per the worker's probes (A1 revert → 1 failure; A2 exact-only → 2 failures, proving no over-tightening; B remove both guards → 2 failures). **Pass B5b — CLOSED.** Parent authoritative pass: `dotnet build SchoolCollab.slnx` 0 errors; Auth.Tests.Unit **173/0**; ArchitectureTests.Unit **60/0**.

**Process note.** The first B5b review attempt **timed out at 30 min with no output** on an over-heavy read (a 1831-line patch shown largely as full-file additions, plus the whole plan, plus the spec, plus seven judgment areas). The narrowed re-dispatch — five target files, four judgments, 45-min timeout — returned a P1 verdict promptly. Reviewer task sizing is therefore a real lever, not just worker sizing, and belongs in the skill's guidance.

**Pass B6 — D16 passkey relying party, bootstrap redemption, realm auth-RP URIs** (`ollama-cloud/deepseek-v4.1-flash`; patch `documents/rounds/diffs-keycloak-ui-auth-b6.patch`, the 7 files below).

    WORKER REPORT (pass B6)
    Changed files (7 — exactly B6's rows): src/SchoolCollab.Auth/Endpoints/PasskeyEndpoints.cs (N — GET /auth/passkey/login, GET /auth/passkey/complete, POST /auth/bootstrap/redeem + BootstrapCodeStore/BootstrapContinuation/BootstrapRedemptionResult companion types in the same file); src/SchoolCollab.Auth/AuthServiceExtensions.cs (M 3rd of 4 — BootstrapCodeStore singleton); src/SchoolCollab.Auth/Endpoints/AuthEndpointGroup.cs (M 2nd of 2 — three routes wired); src/AppHost/SchoolCollab.AppHost/school-collab-realm.json (M — https://localhost:55458/signin-oidc + http://localhost:55459/signin-oidc added to school-collab-client.redirectUris; no portal client, no portal redirect URI); tests/SchoolCollab.ArchitectureTests.Unit/AppHostRealmImportArchitectureTests.cs (M — presence guard + D16 negative guard, 2 new methods); tests/SchoolCollab.Auth.Tests.Unit/PasskeyBootstrapTests.cs (N, 16 tests); documents/configuration.md (M 3rd touch — §4 round-B realm paragraph).
    Build: 0 errors (16 pre-existing warnings). Tests: Auth.Tests.Unit 189 passed / 0 failed; ArchitectureTests.Unit 62 passed / 0 failed (+2).
    Discriminating probes (remove invariant -> exact counts -> restore, md5-verified):
      realm auth-RP URIs removed (JSON kept valid) -> ArchitectureTests 1 failure, exactly RealmImportFile_SchoolCollabClient_RegistersTheAuthServiceRelyingPartyRedirectUris_BothDevSchemes.
      D16 negative invariant broken (a school-collab-auth-portal OIDC client with redirectUris added) -> 1 failure, exactly RealmImportFile_DeclaresNoPortalOidcClient_AndNoPortalRedirectUri.
      /login allowlist-before-challenge removed -> AuthTests 2 failures (Login_NonAllowlistedAppRedirectUri_Is400_AndNeverChallenges, PasskeyRoutes_AreMappedAndFailClosed).
      /complete persisted-value re-validation removed -> 1 failure, exactly Complete_PersistedTargetWithdrawnFromTheAllowlist_Is400_AndIssuesNoCode.
      (A first probe run left a trailing comma and failed every realm test — a probe artifact, corrected before the counts above were recorded.)
    Deviations from plan: none in scope. Two notes: (1) byte-level — the configuration.md edit left the file LF-normalized whereas B5b left it CRLF on disk (diff-invisible under autocrlf; same numstat shape); (2) BootstrapCodeStore is a DEDICATED OneTimeCodeStore instance + the D16 continuation record, so a bootstrap code can never be redeemed at /auth/redeem (claim-set read) and vice versa.
    Open item (a) — LIVE-PATH BLOCKER, not in any B6 row: src/SchoolCollab.Auth/appsettings.json sets FeatureFlags:FEATURE:DisableOIDCAuth=true, so an AppHost dev run registers TestAuth INSTEAD of OIDC and the passkey RP challenge has no OIDC scheme to challenge. The live WebAuthn ceremony (AC3/AC12's live half) cannot pass until the auth service's OIDC posture is decided — see the open item below.
    Pins for B8: (b) a persisted app_redirect_uri sharing the portal bootstrap URL's ORIGIN is treated as the portal's own sign-in and gets NO appCode — so the portal must pass its own origin (its /login or /bootstrap URL) for a portal-initiated passkey login; (c) the redemption's redirectUri must equal the normalized Auth:Portal:BootstrapRedirectUrl exactly (scheme://host[:port]/path) — B8 must send that spelling.
    D13 carry untouched by instruction: no browser-facing logout redirect, no post_logout_redirect_uri in the realm, the KeycloakAuthProvider.BuildEndSessionUrl() comment intact.

**New open item for the owner (found by B6, out of its scope):** the auth service's dev OIDC posture. `DisableOIDCAuth=true` in the auth service's own `appsettings.json` means its OIDC RP scheme is absent in dev, so the live passkey path cannot be exercised (and the cold-start/UI-tester checks on it cannot pass). Options: (i) register the auth service's OIDC RP scheme **unconditionally** (it is the relying party — the app-level dev bypass is about *validating* tokens, not about being able to drive a challenge); (ii) leave the flag and accept that the live passkey path is verified only outside dev; (iii) a third posture. **This is the same class as the `Settings.Api` `flag_admin` carry: a pre-existing flag interacting badly with new code.** Needs a small carve-out pass (B6b) or a decision to defer.

### DIFF REVIEW — pass B6 — `ollama-cloud/kimi-k2.7-code`, read-only (bounded task)

Run `a4f0ddaa-30ce-4d91-b8c4-53c9d90679e4`, against `documents/rounds/diffs-keycloak-ui-auth-b6.patch` (7 files). Deliberately bounded reading (five targets, six judgments) per the B5b timeout lesson.

    REVIEW
    Verdict: PASS
    P1: none
    P2: src/SchoolCollab.Auth/appsettings.json:10 — the dev default FEATURE:DisableOIDCAuth=true with no AppHost override for the auth service means AddAuthAndTenancy registers only TestAuth, while /auth/passkey/login explicitly challenges OpenIdConnectDefaults.AuthenticationScheme; a default dev AppHost run therefore has no OIDC handler and the live WebAuthn path is blocked. Confirmed; a pre-existing dev-default/flag-fan-out policy issue, so an out-of-round owner item rather than a B6 code defect.
    Best-practices: no overwrites; skills honored; readable

**The worker's live-path finding is CONFIRMED by an independent read** and is now the round's second out-of-round owner item (with the `Settings.Api` `flag_admin` gate). Everything else in B6 verified: the allowlist enforced before the challenge **and** re-validated from the persisted value at `/complete` (no fresh query parameter can influence the minted `appCode`'s target), the redemption contract matches B2's pin with no token or authorization code in any portal-bound response (AC12), the dedicated `BootstrapCodeStore` really does isolate bootstrap codes from `/auth/redeem` claim-set codes, and both new realm guards discriminate in both directions (auth-RP URIs on both schemes present; the D16 negative invariant).

**Pass B6 — ACCEPTED.** Parent authoritative pass: `dotnet build SchoolCollab.slnx` 0 errors; Auth.Tests.Unit **189/0**; ArchitectureTests.Unit **62/0**.

**Pass B7 — D17 mediated picker reads** (`ollama-cloud/deepseek-v4.1-flash`; patch `documents/rounds/diffs-keycloak-ui-auth-b7.patch`, the 7 files below).

    WORKER REPORT (pass B7)
    Changed files (7 — B7's 4 rows plus the two parent-amended rows):
      src/SchoolCollab.Auth/Providers/IAuthProvider.cs (M — amended row A: new ISessionTokenAccessor + ProviderAccessToken/ProviderAccessTokenStatus; IAuthProvider's 7 portal-facing members UNTOUCHED, condition 2 honoured by a separate interface);
      src/SchoolCollab.Auth/Providers/KeycloakAuthProvider.cs (M — amended row A: implements both interfaces; GetAccessTokenAsync reuses CurrentTokens/RefreshTokensAsync with the decision-to-refresh and the custody write in ONE call, no second read (condition 1); statuses map to the D18/degraded responses (condition 3));
      src/SchoolCollab.Auth/Services/MediatedReaders.cs (N — MediatedReader<TItem> base + TenantDirectoryReader (settings-api api/tenants) + TeacherDirectoryReader (students-api teachers), MediatedRead<T>/MediatedReadStatus, PickerTenant/PickerTeacher);
      src/SchoolCollab.Auth/Endpoints/MediatedReadEndpoints.cs (N — /auth/pickers/tenants + /auth/pickers/teachers; explicit RequireAuthorization(AddAuthenticationSchemes(PortalSession).RequireAuthenticatedUser()); session -> ISessionTokenAccessor -> forwarded Authorization: Bearer -> typed projection);
      src/SchoolCollab.Auth/AuthServiceExtensions.cs (M 4th of 4 — AddCrossModuleHttpClient<TenantDirectoryReader>("https+http://settings-api", propagateTenant: false) + the students-api twin, literal at the call site; ISessionTokenAccessor resolved through the seam singleton);
      src/SchoolCollab.Auth/Endpoints/AuthEndpointGroup.cs (M — amended row B, 3rd of 3: group.MapMediatedReadEndpoints());
      tests/SchoolCollab.Auth.Tests.Unit/MediatedReadEndpointTests.cs (N — 13 cases: 10 methods + one 3-row [DataRow]).
    Build: 0 errors after every change. Tests: Auth.Tests.Unit 202 passed / 0 failed (+13); Core.Tests.Unit 110/0 (CrossModuleWiringTests: the two new literals are satisfied by B3's existing .WithReference, nothing moved into a shared library); ArchitectureTests.Unit 62/0.
    Discriminating probes (remove invariant -> exact counts -> restore, md5-verified):
      P1 accessor stops refreshing (if (false)): 202/3 — the stale-token test (forwarded credential must be the ROTATED one) + the two D18 refresh states. Axis: the stale-token hazard the accessor exists to prevent.
      P2 picker group's RequireAuthorization dropped: 202/1 (Pickers_WithAnUnknownSession_Are401_AndDiscloseNothing). **HONEST NOTE from the worker:** the anonymous test ALONE does not discriminate the policy — it still passed, because the mediator's own fail-closed guard answers 401 too; both test comments were corrected to state which test carries which half of the axis.
      P3 group.MapMediatedReadEndpoints() removed: 202/2 (both hosted picker tests answer 404). Axis: the amended mapping row / routes reachable from the composed host.
      P5 the mediator's null-session 401 guard disabled: 202/1 (Tenants_WithoutAPresentedSession_Is401_AndNeverReadsUpstream). Axis: fail-closed on an unidentified caller + no upstream call.
      P4 (observed while bringing the suite green): with the accessor registered as its own KeycloakAuthProvider instance, MediatedReaders_AndTheCustodyAccessor_AreRegistered went red (202/1); fixed by resolving through the seam singleton, re-verified 202/0. Axis: ONE custody view.
    Mapping sweep (parent-requested): the auth service declares exactly four mapping extensions — MapAuthEndpoints (Program.cs:42, AuthEndpointTestHost:195), MapAdminUserRoutes/MapAdminRoleRoutes (AuthEndpointGroup:86-87), MapMediatedReadEndpoints (this pass). **No other mapping extension lacks a call site.**
    Design notes: one degraded status covers every upstream failure class (rejected forward, 5xx, unparseable body) -> 502 {"error":"upstream_unreachable"}, the class logged server-side, no upstream status/body echoed; the picker projections drop all non-picker upstream fields; propagateTenant: false (identity travels in the forwarded bearer token; a dev-tenant header would be a second, weaker scope); no config key added so configuration.md is correctly untouched.
    Pins for B9: [{id,name,type}] and [{id,firstName,lastName,displayName}], camelCase; error codes reused from B2's map (session_not_found 401/404, session_ended 410, upstream_unreachable 502); session header X-Portal-Session.

### DIFF REVIEW — pass B7 — `ollama-cloud/kimi-k2.7-code`, read-only (bounded task)

Run `43cbcf5a-e838-42e1-ae54-5a2ac749c2ab`, against `documents/rounds/diffs-keycloak-ui-auth-b7.patch` (7 files).

    REVIEW
    Verdict: PASS
    P1: none
    P2: none
    Best-practices: no overwrites; D15's 7 IAuthProvider members untouched and the accessor lives in a separate ISessionTokenAccessor interface; grouped endpoints, typed HttpClients with propagateTenant:false, and discriminating tests all honor repo rules. The worker's honest split is correct: Pickers_WithAnUnknownSession_Are401_AndDiscloseNothing pins the policy attachment (401 vs 404 without the policy), while Tenants_WithoutAPresentedSession_Is401 pins the mediator's own fail-closed guard. The B9 picker/error shapes and X-Portal-Session header align with B2's tolerant DTOs and error taxonomy.

**Pass B7 — ACCEPTED.** Parent authoritative pass: `dotnet build SchoolCollab.slnx` 0 errors; Auth.Tests.Unit **202/0**; Core.Tests.Unit **110/0**; ArchitectureTests.Unit **62/0**. The C# half of round B is complete: B1 (challenge interception) B2 (portal skeleton) B3 (AppHost wiring) B4 (handshake) B5 (seam/lifecycle/session auth) B5b (allowlist) B6 (passkey RP + realm) B7 (mediated reads).

**Pass B8 — portal login / session / logout views** (`ollama-cloud/deepseek-v4.1-flash`; patch `documents/rounds/diffs-keycloak-ui-auth-b8.patch`, the 8 files below).

    WORKER REPORT (pass B8)
    Changed files (8, all inside B8's amended rows): views/__init__.py (N); views/login.py (N — Form.from_model (pydantic LoginFormModel, SecretStr -> Input(input_type="password")), reactive Field(invalid=Rx("!field"))/FieldError, Fetch.post("/login", body={username,password,antiforgery_token,return_uri}), "Sign in with passkey" button -> auth service /auth/passkey/login?app_redirect_uri={portal}/bootstrap); views/errors.py (N — bounded code->copy table, build_error_card / build_session_ended_card (D18) / build_invalid_return_uri_card, all HTTP 200); app.py (M 2nd touch — GET/POST /login, GET /bootstrap, GET /logout; AntiforgeryTokenStore (one-time, in-process, no signing key); Python mirror of B5b's allowlist matcher; D18 cookie-clear cards on the index); tests/test_login_view.py (N); tests/test_session_routes.py (N); SchoolCollab.slnx (M 2nd touch — views/ folder node + 3 Files + 2 test Files); PortalsSolutionItemsArchitectureTests.cs (M, parent-amended row — PortalSolutionFolderNodes_MirrorThePackageLayout now walks both portal prefixes and pins all 8 nodes).
    Build: 0 errors (13 pre-existing warnings).
    Tests: AuthPortal pytest 63 passed / 0 failed (36 before B8 -> +27); ArchitectureTests.Unit 62/0.
    Discriminating probes (md5 before -> mutate -> exact count -> restore -> md5 identical: app.py bff5c062…, .slnx 2224d503…): 1 remove the 3 views/ File items -> EveryPortalFile_IsListedInTheSolution 1 failed/62 (names the 3 unlisted files); 2 move the views/ Folder node -> PortalSolutionFolderNodes_MirrorThePackageLayout 1 failed/62; 3 remove the folder node entirely -> 2 failed/62; 4 neutralise the return_uri check on GET /login -> 2 pytest failures (invalid-return_uri card + unconfigured-allowlist refusal); 5 neutralise state.antiforgery.consume(...) -> 1 failure (required + single-use); 6 drop _clear_session_cookie on the session_ended branch -> 1 failure; 7 drop the bootstrap continuation allowlist check -> 1 failure; 8 drift the redemption URI by one trailing slash -> 1 failure (B6's exact-spelling binding).
    Deviations from plan (all parent-approved):
      1. POST /login answers HTTP 200 {"redirect_uri": "<validated return_uri>?code=…"} + Set-Cookie, NOT a 302 (prefab Fetch follows redirects with no redirect option) — see the parent-amended row; in-tab navigation via PrefabApp(js_actions=...) + CallHandler, used by both the form and the passkey button; /bootstrap keeps a plain 302 and is asserted.
      2. Failures navigate to /login?return_uri=<validated>&error=<bounded code> — a same-origin AC10 card page at HTTP 200, so a fetch-submitted failure is still a PAGE and never a foreign URL; a rejected return_uri is carried only when already validated.
      3. The allowlist matcher + antiforgery store live in app.py (B8 has no new-file row for them; api/ is frozen). No X-Portal-Session header is needed on any B8 call (B5's session routes take the id in the path).
      4. session_not_found also clears the cookie + renders its card (in-spirit D18).
      5. redirect_uri_not_allowed (B5b) reaches Python as ApiResponseError — B2's client maps unknown codes there and is frozen, so code_for_error surfaces it by scanning the client's own bounded detail text; the card is reachable at /login?error=redirect_uri_not_allowed.
    Open risks: no Secure cookie flag (D12 pins HttpOnly + SameSite=Lax; dev is plain http — production HTTPS must add it); the reactive invalid=Rx("!field") pattern shows the error state on FIRST PAINT (prefab's documented pattern — flagged for the reviewer/UI tester); the renderer is CDN-delivered by default (jsDelivr must be reachable for a UI check); the live POST->location.assign(return_uri?code=…) hop is a UI-tester check (pytest pins the body/URL/cookie, not the navigation).

### DIFF REVIEW — pass B8 — `ollama-cloud/kimi-k2.7-code`, read-only (bounded task)

Run `bab0700a-ecaa-4f6c-9e20-5f14b3a9b385`, against `documents/rounds/diffs-keycloak-ui-auth-b8.patch` (8 files).

    REVIEW
    Verdict: P1
    P1: src/SchoolCollab.AuthPortal/app.py:254-313 — the allowlist matcher has no dedicated pytest coverage for suffix, userinfo, port-defaulting, percent-encoded path, malformed prefix, or case vectors; the route tests only exercise a non-allowlisted host and an empty list, leaving the second security control unproven.
    P2: app.py:355-363 — the session cookie omits Secure with only a code comment; needs a documented production note or a scheme-conditional Secure guard for HTTPS deployments.
    P2: app.py:585-620 — the pre-route JSON/antiforgery guards return 415/400 rather than the AC10 HTTP-200 card shape the contract states for failures; acceptable as transport guards but contract-inconsistent.
    Best-practices: no overwrites / skills honored / readable.

**Verified sound:** the Python matcher is semantically equivalent to the fixed C# matcher (exact scheme+host+port, `/`-boundary path extension only, query/fragment ignored, userinfo rejected, malformed/blank entries dropped, default-deny) with **no weaker vector found**; the amended contract holds (`POST /login` 200 + cookie + `redirect_uri`; in-tab navigation from both the form and the passkey button; `GET /bootstrap` a plain 302); the antiforgery token is one-time/consumed/no-key with a JSON-only request requirement; every failure path renders an AC10 card at HTTP 200 with no foreign-URL redirect, and `session_ended`/`session_not_found`/logout all clear the cookie; the two architecture-guard probes hold (slnx lists every portal source file and the four expected folder nodes, so neither guard is vacuous). **Adjudications:** (a) surfacing `redirect_uri_not_allowed` by scanning the frozen client's bounded `detail` text is ACCEPTABLE — no cross-pass edit required; (b) `Field(invalid=Rx("!field"))` on first paint is the DOCUMENTED prefab pattern, not a defect.

**Rework (P1 -> worker rework, Tier-3 iteration 1 of ≤2): in flight.**

**Rework iteration 1 — applied (1 new file + 3 modified, all inside B8's rows plus the parent-amended row).**

    WORKER REPORT (pass B8, rework 1)
    Changed files (4): tests/test_app_callback_allowlist.py (N, parent-amended row — 31 tests mirroring B5b's C# vector set, names mirrored for row-by-row parity); app.py (M — matcher parity fix + scheme-conditional Secure + transport-guard comment); SchoolCollab.slnx (M — File item for the new test module); tests/test_session_routes.py (M — the two Secure branch tests); views/errors.py (M — the transport-guard/card distinction in the module docstring).
    Build: 0 errors. Tests: portal pytest 96 passed / 0 failed (63 -> 96: +31 matcher, +2 Secure); ArchitectureTests.Unit 62/0.
    P1 CLOSED: the parity suite mirrors the C# names (accepted exact/query/trailing-slash/path-extension/portal-bootstrap spellings; rejected suffix, userinfo both directions, host containing an allowlisted prefix, wrong scheme/host/port, wrong path, case, malformed candidates and entries, empty/blank list, bare-origin-for-a-deeper-path) plus two additions for this copy: percent-encoded path spelling and dot-segment path spelling.
    FINDING BEYOND THE P1/P2s (fail-closed, ratified by the parent below): building the parity suite surfaced a REAL non-equivalence. Measured with .NET's [Uri]: http://localhost:5300/signin-handshake/../evil yields AbsolutePath=/evil (the C# matcher refuses it); Python's urlsplit keeps the RAW path, so a plain `/`-boundary check read it as a deeper path under the prefix and ACCEPTED a spelling whose browser-resolved destination is a different resource on the allowlisted origin — a parser differential. _split_uri now compares paths as written (no percent-decoding) and refuses any `.`/`..` segment outright, landing on the C# outcome without re-implementing RFC 3986 remove_dot_segments. It can only narrow (the one spelling it refuses that C# would resolve-and-accept is /x/./y); code comment and test both carry the measurements. Confirmed for parity: .NET keeps %2F/%73 escaped, so the C# copy refuses percent-encoded spellings too — that vector is parity, not extra strictness.
    P2-1 CLOSED — scheme-conditional Secure chosen (not the doc note): Secure follows the scheme of the configured AuthPortal:PublicBaseUrl (unset => off, like dev), so HttpOnly + SameSite=Lax stay exactly as D12 pins them and a production HTTPS deployment does not ship a cookie the browser would also send over http. Tests: test_session_cookie_has_no_secure_flag_over_http / test_session_cookie_is_secure_over_https. configuration.md deliberately untouched (no new key; the one clause worth adding belongs to the parent/B9 change set).
    P2-2 CLOSED — behaviour unchanged: a TRANSPORT GUARDS comment in login_submit plus one paragraph in views/errors.py's docstring state that 415/400 refusals are pre-route guards for malformed/forged requests, distinct from the page-shaped AC10 cards a sign-in OUTCOME renders.
    Probes (md5-restored): A drop the `/`-boundary -> 3 failed/31 (suffix, bare-origin, percent-encoded); B drop both userinfo checks -> 2 failed/31; C fail open on an empty allowlist -> 7 failed/52; D Secure hardcoded on -> 1 failed/21; E Secure hardcoded off -> 1 failed/21; F drop the dot-segment refusal -> 1 failed/31.

**Re-verify: in flight.**

### DIFF REVIEW — pass B8 — re-verify (rework iteration 1 re-check) — `ollama-cloud/kimi-k2.7-code`, read-only

Run `b0c3ab73-72ac-4e8c-b736-a018bdf9a49b` (resumed from `bab0700a`), against the re-frozen 9-file `documents/rounds/diffs-keycloak-ui-auth-b8.patch`.

    REVIEW
    Verdict: P2-only
    P1: none
    P2: src/SchoolCollab.Auth/Services/AppCallbackAllowlist.cs:72 — the C# matcher relies on Uri dot-segment normalization rather than an explicit `.`/`..` refusal; add the explicit check (and a test) for parity/defence-in-depth with the Python matcher. Not a current exploit.
    Best-practices: no overwrites / skills honored / readable — the rework touches only the expected B8 files, the C# P2 is a minimal cross-pass parity hardening, and the new tests are discriminating.

Verified: P1 CLOSED (31 mirrored tests; the probes are supported by the names and assertions); P2-1 CLOSED (`secure=_public_base_url_is_https()` with both branches tested, `HttpOnly`/`SameSite=Lax` unchanged); P2-2 CLOSED by documentation (no behaviour change needed); the dot-segment differential CLOSED on the Python side (refuses any `.`/`..` segment; asserts `/../evil`, `/./continuation`, `/../signin-handshake` rejected); **no regression** — `POST /login` 200 + `redirect_uri` + `Set-Cookie`, in-tab `js_actions` navigation, `/bootstrap` 302, one-time server-side antiforgery with no signing key, cookie cleared on `session_ended`/`session_not_found`/`logout`, AC10 cards at HTTP 200, all changes inside B8's rows.

**Pass B8 — ACCEPTED.** Parent authoritative pass: `dotnet build SchoolCollab.slnx` 0 errors; ArchitectureTests.Unit **62/0**; AuthPortal pytest **96 passed**.

**Carried residuals before the round's accept:**
- **R1 (reviewer-required P2)** — C# `AppCallbackAllowlist.cs` explicit `.`/`..` segment refusal + a test, for contractual parity with the Python matcher.
- **R2** — a `documents/configuration.md` clause noting that the portal session cookie carries `Secure` when `AuthPortal:PublicBaseUrl` is https.
- **R3 (owner-gated)** — the D13 logout mechanism and the auth service's dev OIDC posture; both block only the round's *live* verification claims.

**Pass B9 — portal admin views (users, roles, typed admin client)** (`ollama-cloud/deepseek-v4.1-flash`; patch `documents/rounds/diffs-keycloak-ui-auth-b9.patch`, the 6 files below).

    WORKER REPORT (pass B9)
    Changed files (6 — exactly B9's rows): views/admin_users.py (N — users DataTable (username/email/tenant/teacher/roles/enabled); Form.from_model(AdminUserFormModel) create+edit tree with a tenant Select and a searchable teacher Combobox inserted between the username/credential and the D10 claim-attribute fields, both pickers rendered from B7's mediated rows; build_admin_card (AC10 gate/degradation cards); portal route constants /admin/users, /admin/users/{id}/edit, /admin/users/{id}/roles); views/admin_roles.py (N — build_roles_view: the existing realm role set, one Assign (Fetch.post) + Unassign (Fetch.delete) per role against the portal's own /admin/users/{id}/roles; role DEFINITIONS never edited (D11)); api/admin_api_client.py (N — AdminApiClient presenting X-Portal-Session on every call: /auth/admin/users GET/POST/PUT, /{id}, /{id}/reset-password, /auth/admin/roles, /{id}/role-mappings POST/DELETE, plus B7's /auth/pickers/tenants|teachers; tolerant DTOs; typed error map; AC11 token-shape scan; no credential retained); app.py (M 3rd touch — require_admin_session gate (D18 read -> roles must contain user-admin; missing/dead/non-admin/degraded -> AC10 card at HTTP 200, cookie cleared when the session is dead), get_admin_api_client (cookie id -> header), AdminAccessRefused handler, 7 admin routes, discovery-error branch for /admin*); tests/test_admin_views.py (N — 21 tests); SchoolCollab.slnx (M 3rd touch — 4 File items).
    Build: 0 errors. Tests: AuthPortal pytest 117 passed / 0 failed (+21); ArchitectureTests.Unit 62/0.
    Discriminating probes (md5-verified restores): A the 4 slnx File items removed -> ArchitectureTests 1 failed, naming exactly the 4 unlisted files; B the `if not session.is_admin:` block removed -> 115/2 (the two gate tests) — axis: D8's user-admin gate; C the SESSION_HEADER_NAME header dropped from the client -> 116/1 (the header assertion on every admin/picker call) — axis: D12's session presentation; D create's credential chain skipped -> 115/2; E the role-mapping body loses the role id -> 116/1. HONEST NOTE: a probe that emptied the picker rows did NOT discriminate (117/0) — that branch is asserted by the direct view test (…omits_the_create_form_without_pickers), not by the route test; it is not claimed as a route-level guard.
    Deviations from plan:
      1. api/admin_api_client.py also carries B7's two mediated picker reads (/auth/pickers/*, not under /auth/admin/*): that file is the pass's only new client surface and api/auth_api_client.py is frozen (not in B9's rows).
      2. No frozen file edited: the new client reuses the package-internal api.dto coercions and api.auth_api_client._token_shaped_keys (one definition of the AC11 rule); admin-only card copy lives in views/admin_users.build_admin_card because views/errors.py is B8's file.
      3. Tests override both get_auth_api_client and get_admin_api_client; the latter keeps Depends(require_admin_session) so the REAL gate stays in the graph (overriding alone would have bypassed it).
      4. **CROSS-PASS FUNCTIONAL GAP (flagged, not patched) — AC5-relevant:** the auth service's create/update body is `KeycloakUser` (no `attributes`, no credentials) and its create response names no user id, so the portal's **D10 claim-attributes editor cannot persist**; the portal sends the attributes anyway and chains create -> resolve-by-exact-username -> reset-password for the initial credential (?error=credential_not_set if resolution fails). Attribute persistence in Keycloak is a cold-start/UI-tester item **or a follow-up C# pass**.
      5. The users list's roles column is tolerant ("—" when Keycloak's user list omits role mappings); no mediated read exposes a user's current mappings, so the roles page lists the realm role set with assign/unassign per role rather than pre-marking state.
      6. A non-admin/absent session renders the card at HTTP 200 (never a redirect: the portal's own /login would refuse /admin/users as a return_uri — a loop); mutation transport guards (415/400) follow B8's documented precedent.
    Open risks: no test covers the real get_admin_api_client cookie->header wiring end-to-end (the header is pinned on AdminApiClient; the cookie read is 3 lines); the pass ran past the 30-minute cap while finishing validation (all work landed, nothing remains).

**Residual R3 (added by B9, AC5-relevant, pending the review's adjudication):** the auth service's admin create/update must accept and persist the D10 `attributes` and return the created user id, or AC5's "edit its claim attributes" is not met. Planned as part of the **B10 residuals pass** with R1/R2.

**PARENT VERIFICATION of R3 (independent, at source):** confirmed. `src/SchoolCollab.Auth/Services/KeycloakAdminClient.cs:65` defines `KeycloakUser(string? Id, string Username, bool Enabled, string? Email, string? FirstName, string? LastName)` — **no `Attributes`** — and `src/SchoolCollab.Auth/Endpoints/AdminUserEndpoints.cs:65` passes the caller's request straight into `CreateUserAsync`, returning `{ status = "created" }` at `:77` with **no created user id**. So the D10 claim attributes cannot persist through the admin surface, and **AC5's "edit its claim attributes" is UNMET today**. This is not a B9 defect: round A explicitly **deferred AC5 to round B**, so **round B owns the fix**. Required: `KeycloakUser` gains `Attributes` (Keycloak's `attributes` map, camelCase on the wire), create/update forward them, and the create surfaces the new id (Keycloak returns it in the `201 Location` header) so the portal need not resolve-by-username. Planned as **B10**, together with R1 (C# dot-segment refusal) and R2 (the `Secure` clause).

### DIFF REVIEW — pass B9 — `ollama-cloud/kimi-k2.7-code`, read-only (bounded task)

Run `d9de8d12-17d8-4835-b2fa-672263073865`, against `documents/rounds/diffs-keycloak-ui-auth-b9.patch` (6 files).

    REVIEW
    Verdict: P1
    P1: src/SchoolCollab.Auth/Services/KeycloakAdminClient.cs:65 — KeycloakUser has no attributes member, and CreateUserAsync/UpdateUserAsync (348/358) together with AdminUserEndpoints.cs:53/83 use it as the create/update body, so the D10 claim attributes sent by the portal cannot persist; the create endpoint also returns only { status = "created" } (AdminUserEndpoints.cs:77) with no user id. AC5 is therefore unmet end-to-end; the follow-up C# pass (B10) must add an attributes map to KeycloakUser, propagate it through the Keycloak Admin REST client, and return the created user id.
    P2: none
    Best-practices: no overwrites / skills honored / readable — the only P1 is the upstream AC5 residual the worker already flagged, **not a defect in B9's six changed files**.

Verified: scope (6 files = B9's rows; frozen files untouched); the `require_admin_session` gate enforces `user-admin` from the **session's own roles**, clears the cookie on a dead session, and stays in the graph when tests override the client dependency; the pickers go to the **mediated** endpoints with path/host/header assertions and no direct upstream call; `X-Portal-Session` on every admin/picker call; no credential retained or echoed; the AC11 token-shape scan is real; the withdrawn probe is properly scoped (no route-level guard claimed that does not exist); role assign/unassign is id-keyed with no runtime definition edit (D11).

**Pass B9 — ACCEPTED** (its own six files are clean; the P1 is **R3**, carried to B10). Parent authoritative pass: `dotnet build SchoolCollab.slnx` 0 errors; ArchitectureTests.Unit **62/0**; AuthPortal pytest **117 passed**.

**Pass B10 — the residuals pass (R3 the AC5 blocker, R1 matcher parity, R2 doc clause)** (`ollama-cloud/deepseek-v4.1-flash`; patch `documents/rounds/diffs-keycloak-ui-auth-b10.patch`, the 7 files below).

    WORKER REPORT (pass B10)
    Changed files (7 — exactly B10's rows):
      src/SchoolCollab.Auth/Services/KeycloakAdminClient.cs (M — KeycloakUser gains IReadOnlyDictionary<string, string[]>? Attributes (camelCase member, attribute names verbatim, null => omitted); new AdminUserCreateResult(Status, UserId, Detail); CreateUserAsync reads the minted id off the 201 Location last path segment (TryGetCreatedUserId, percent-decoded); UpdateUserAsync doc notes the PUT body's attributes are what the user's attributes become);
      src/SchoolCollab.Auth/Endpoints/AdminUserEndpoints.cs (M — create/update forward the whole representation incl. attributes; the 201 body is { id = result.UserId, status = "created" });
      src/SchoolCollab.Auth/Services/AppCallbackAllowlist.cs (M — explicit HasDotSegment refusal over the RAW Uri.OriginalString path, query/fragment cut off);
      tests/SchoolCollab.Auth.Tests.Unit/KeycloakAdminClientTests.cs (M — 6 new tests; the scripted handler replays a canned Location header);
      tests/SchoolCollab.Auth.Tests.Unit/AdminEndpointTests.cs (M — 2 new tests; StubAdminCapturingBodies captures POST/PUT bodies and can attach a 201 Location);
      tests/SchoolCollab.Auth.Tests.Unit/AppCallbackAllowlistTests.cs (M — IsAllowed_RejectsADotSegmentPathSpelling, mirroring the Python name);
      documents/configuration.md (M — R2 clause: the portal session cookie carries Secure iff AuthPortal:PublicBaseUrl is https; unset => off).
    Build: 0 errors after every change. Tests: Auth.Tests.Unit 211 passed / 0 failed (+9); ArchitectureTests.Unit 62/0.
    Discriminating probes (md5-verified restores; baseline 211/0):
      A JsonIgnore on Attributes (severing the propagation path; deleting the member cannot compile): 211/5 — the create/update attribute tests, the array-shape read test, and the endpoint round-trip/201-id tests. Restore md5 e37ea6be…
      B TryGetCreatedUserId returns null: 211/2 — the Location-capture test and the endpoint 201-id test.
      C dot-segment clause dropped: 211/1 — IsAllowed_RejectsADotSegmentPathSpelling, on the /./continuation vector (Expected False, found True). **MEASUREMENT: the explicit refusal is load-bearing for `/./` ONLY** — Uri normalization already refuses /../evil and /../signin-handshake. Restore md5 bb7274a5…
    Deviations from plan:
      1. AdminUserCreateResult (a new record beside the file's existing Admin*Result records) rather than widening AdminOperationResult — only CreateUserAsync has an id to report.
      2. The dot-segment refusal reads the RAW spelling (Uri.OriginalString), not AbsolutePath: by the time components are available remove_dot_segments has already run, so an AbsolutePath check would be DEAD CODE and the ./ spelling would still be accepted (probe C proves it).
      3. The create-audit target stays the created username (audit semantics were not in scope; the accepted CreateUser_EmitsUserCreatedAudit still pins it) — only its stale explanatory comment was rewritten.
      4. Two tests beyond the named list, both inside B10's files: GetUser_ReadsTheAttributeMapInKeycloaksArrayShape (the editor's read half) and CreateUser_WithoutALocationHeader_SucceedsWithNoUserId.
      5. Portal untouched; its resolve-by-username chain is now a fallback, as instructed.
    Open risks: attribute persistence is proven only at the wire contract (Keycloak's realm accepting it is a live/UI-tester item); the created id is percent-decoded defensively though Keycloak returns a raw UUID; HasDotSegment splits on `/` only, matching the Python copy.

### DIFF REVIEW — pass B10 — `ollama-cloud/kimi-k2.7-code`, read-only (bounded task)

Run `40c5224a-5418-4b93-bf43-a1ad52656dd4`, against `documents/rounds/diffs-keycloak-ui-auth-b10.patch` (7 files).

    REVIEW
    Verdict: PASS
    P1: none
    P2: none
    Best-practices: no overwrites within B10 scope; skills honored (dotnet-best-practices, testing.md); readable

**Pass B10 — ACCEPTED.** Parent authoritative pass: `dotnet build SchoolCollab.slnx` 0 errors; Auth.Tests.Unit **211/0**; ArchitectureTests.Unit **62/0**. **All eleven passes of round B are now closed (B1-B10) with zero outstanding findings**, and the R1 dot-segment clause is contractually identical in both implementations (with the measured detail that it is load-bearing for `/./` only).

## Acceptance

### Criteria checklist — round B (parent-authoritative; the accept does not re-run anything)

| # | Criterion (spec AC) | Verdict | Evidencing pass + suite |
|---|---|---|---|
| **B-A** (AC7, D5) | Flag OFF = byte-for-byte today's OIDC challenge; flag ON → 302 to `portal /login?return_uri=…`; flag-ON with no portal URL → 401 fail-closed; startup-selected | **MET** | B1 — `AuthWiringTests`, `PortalRedirectChallengeHandlerTests` (Core.Tests.Unit 110/0 final; 4 discriminating probes incl. the OFF-path re-target probe) |
| **B-B** (AC2, D6) | Typed redeem client with literal `https+http://auth` matched by `.WithReference(auth)` on both hosts; redemption signs in with the OIDC claim set; replay/TTL/URI-mismatch rejected | **MET** | B3/B4 — `AppHostLoginUiFlagWiringArchitectureTests` (62/0 final, 7 tests), `CrossModuleWiringTests` (untouched, asserted), `PortalHandshakeTests` (Core.Tests.Unit 110/0; 8 probes incl. the P1-1 literal-in-Core probe) |
| **B-C** (D15) | One `IAuthProvider` interface, one Keycloak implementation; all 7 seam operations route through it; taxonomy preserved | **MET** | B5 — `KeycloakAuthProviderTests` (Auth.Tests.Unit 211/0 final); the "no speculative second-provider scaffolding" clause was verified as the static-review check it was pinned to be (B1/B4/B5 diff reviews) — `ISessionTokenAccessor` (B7) is additive and `IAuthProvider`'s 7 members stayed untouched |
| **B-D** (D16, AC12, AC13-passkey-half) | Realm declares both auth-RP `/signin-oidc` URIs and **no portal client**; bootstrap code single-use/short-TTL/URI-bound; allowlist enforced before the challenge and re-validated from the persisted value at `/complete`; no code or token in any portal-bound response | **MET (static)** | B6 — `PasskeyBootstrapTests` (16 tests), realm guard (+2 methods, ArchitectureTests.Unit 62/0 final). **Caveat:** the live WebAuthn ceremony is NOT verified — see the LIVE/OWNER-GATED split below |
| **B-E** (D17, D18, AC11) | Portal holds no credential, no direct data-API call; mediated reads session-scoped, run as the session's user, data-only; session read = claims as data; distinct `session ended`; `DELETE` revokes server-side and returns `endSessionUrl`; no token in any body incl. DELETE | **MET** | B5/B7 — `SessionEndpointTests` (value-based no-token scans incl. DELETE), `PortalSessionAuthenticationHandlerTests`, `MediatedReadEndpointTests` (Auth.Tests.Unit 211/0; Core.Tests.Unit 110/0 for the literal transport rule) |
| **B-F** (AC10, §14, §16.1) | Prefab form + reactive validation + passkey button; `Fetch.post` to the portal's own route; antiforgery (one-time server-stored token, JSON-only, SameSite=Lax); invalid `return_uri` → error card; degraded states = AC10 cards at HTTP 200 | **MET** | B8 — pytest `test_login_view`, `test_session_routes`, `test_app_callback_allowlist` (portal pytest 117 passed final; 8+6 discriminating probes) |
| **B-G** (D8, D10, D11) | Admin views create/edit tenant users bound to tenant + teacher (mediated pickers), assign/unassign realm roles (definitions untouched), gated by `user-admin` session; D10 claim attributes persist | **MET (B9 + B10)** | B9 — pytest `test_admin_views` (21 tests); **AC5 was met only via the B10 residual** (see below) — attribute persistence proven at the wire contract, not against a live realm |
| **B-H** (A-G inheritance) | Build 0 errors; every affected suite 0 failures; every new guard discriminating (probe + observed counts); config documented in its own change set | **MET** | All passes — see the build/test table; per-pass probe reports and the `configuration.md` rows (B3, B5b, B6, B10) are in the Review region |

**Round-A-deferred AC closed this round: AC5** ("the admin UI can … edit its claim attributes"). Round A explicitly deferred it; round B owns the fix, and the B9 diff review's P1 (= residual R3) named the exact gap — `KeycloakUser` had no `attributes` and create returned no user id. **B10 closed it**: `KeycloakUser` gains `Attributes` (Keycloak's camelCase string→array wire shape, matching the realm's dev user and the four attribute mappers), create/update forward them, and the `201` body carries the id captured from the `Location` header — so the portal's D10 attributes editor can persist (its resolve-by-username chain is now only a fallback). **Without B10, AC5 would be unmet.** The persistence claim rests on the wire contract (`KeycloakAdminClientTests`/`AdminEndpointTests` round-trip + array-shape tests); a live Keycloak realm accepting the write is a UI-tester item.

### Build / test numbers (parent-authoritative, final round state)

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** (13 pre-existing warnings) |
| `SchoolCollab.Auth.Tests.Unit` | **211 passed / 0 failed** (102 at round base) |
| `SchoolCollab.AuthPortal` pytest | **117 passed / 0 failed** (36 at base) |
| `SchoolCollab.Core.Tests.Unit` | **110 / 0** |
| `SchoolCollab.ArchitectureTests.Unit` | **62 / 0** (53 at base) |
| `SchoolCollab.Families.Tests.Unit` | **42 / 0** |
| `SchoolCollab.Admin.Tests.Unit` | **546 / 0** |

Frozen per-pass patches: `documents/rounds/diffs-keycloak-ui-auth-b{1,2,3,4,5,b5b,6,7,8,9,10}.patch` (+ `b4-fix1`). Base `eb651ba8`; the round is deliberately uncommitted.

### P1 ledger — CLOSED overall

Every P1 raised in the round is closed; none is outstanding. Three P1s were resolved **without becoming a code fix in a reviewed diff**:

1. **The plan-review P1 (attacker-chosen `return_uri`)** — a crafted `portal/login?return_uri=attacker` link would have yielded an attacker-redeemable one-time code carrying the victim's claim set, because `/auth/redeem` is caller-unauthenticated and the portal's `/login` is browser-reachable. It was fixed at **plan level** by designing the per-app callback allowlist and dispatching it as pass **B5b** (options key + fail-closed matcher + enforcement at issuance on both paths + rejection tests + config rows), with the C# rework (exact-`/`-boundary path, userinfo rejection both directions) and the B8 Python mirror (31-test parity suite + the dot-segment differential fix) following in their own rework iterations. The control is now load-bearing at issuance on both paths; the portal-side check is defense-in-depth.
2. **B4's P1 — WITHDRAWN as a false positive.** The reviewer claimed `ReadFromJsonAsync<PortalClaimSet>` would bind null under camelCase because of missing `JsonSerializerDefaults.Web`. The parent verified the frozen artifact directly: the original test fixture was a hand-written camelCase JSON literal, the client used the **parameterless** `PostAsJsonAsync`/`ReadFromJsonAsync` overloads, and those default to `JsonSerializerDefaults.Web` (camelCase + case-insensitive) — probe Q3 (pre-fix code + camelCase producer) passed 110/0, and probe Q1 (forced case-sensitive General) produced the 5 real failures. **No behavioural defect was ever present.** The explicit `Web` options were retained as P2-grade hardening; the reworked fixture (camelCase bytes through Web defaults, pinned) genuinely strengthens the test.
3. **B5's P1-1 — the PARENT'S freeze error, not the worker's.** The frozen `diffs-keycloak-ui-auth-b5.patch` contained only 5 of the 9 row files: the whole `SchoolCollab.Auth` / `Auth.Tests.Unit` trees are untracked (round A is uncommitted), so `git diff` reports nothing without `git add -N`, and the parent intent-added only the believed-new files. The fix was a **re-freeze with all 9 intent-added and a file-list-vs-rows verification** — no code changed, and the re-verification against the corrected 11-file artifact (after the parent-authorized `PortalSessionStore` amendment) returned PASS with no findings. Process lesson recorded: a patch freeze must diff its file list against the pass's expected-files rows before the reviewer is dispatched.

Remaining P1 dispositions for completeness: plan-review iteration 1's other two P1s (transport parameter, B8/B9 slnx rows) closed by the revision; the re-read's completeness P1 closed by the bound deviation (dictated fixes applied verbatim); B5's P1-2 (D13) closed by the parent's option-(iii) adjudication (see below); B5b's two P1s and B8's and B9's P1s closed by their reworks/B10.

### Residual P2s (carried, behaviour-neutral, none CI-enforced)

- `src/SchoolCollab.Core/Auth/PortalRedirectChallengeHandler.cs:50` — explicit constructor where dotnet-best-practices prefers a primary constructor (B1 review; parent-adjudicated as carried to acceptance).
- `src/SchoolCollab.Core/Auth/PortalHandshakeExtensions.cs:72` — the explicit `JsonSerializerDefaults.Web` options are defensive hardening, not a required fix (B4 re-verify; the P1 was withdrawn).
- `redirect_uri_not_allowed` detail-text surfacing — B8 surfaces the B5b rejection code by scanning the frozen client's bounded `detail` text (the client maps unknown codes to `ApiResponseError`); adjudicated acceptable, no cross-pass edit.
- The 415/400-vs-card distinction — pre-route transport guards (malformed/forged requests) return 415/400, distinct from the AC10 HTTP-200 cards a sign-in *outcome* renders; closed by documentation (transport-guards comment + `views/errors.py` docstring), behaviour unchanged.
- `Field(invalid=Rx("!field"))` shows the error state on first paint — the documented prefab pattern, adjudicated not a defect; flagged as a UI-tester watch item.
- B6's reviewer-recorded P2 (the dev `DisableOIDCAuth=true` posture) is promoted to the **owner-gated** list below — it blocks only live verification, not any static claim.

### Static-verified vs LIVE / OWNER-GATED — mandatory split

Round B's claims above are **static and unit-test-verified only**. The following cannot be claimed as verified, and the owner must accept them as open:

- **The dev WebAuthn passkey ceremony (AC3's live half).** `src/SchoolCollab.Auth/appsettings.json` sets `FeatureFlags:FEATURE:DisableOIDCAuth=true`, so a dev AppHost run registers `TestAuth` instead of OIDC and `/auth/passkey/login` has no OIDC handler to challenge — the live conditional-UI ceremony cannot be exercised until the owner decides the auth service's dev OIDC posture (register the RP scheme unconditionally / leave the flag and verify outside dev / a third posture). Found by B6, independently confirmed by the B6 reviewer; an out-of-round owner item (carve-out pass B6b or a decision to defer).
- **The full D13 logout mechanism.** The browser-facing redirect carrying `id_token_hint` + `post_logout_redirect_uri` is carried pending the owner's (ii)/(iii) decision (recorded at B5's adjudication; B5's `BuildEndSessionUrl()` is **token-free** — `client_id` only — and documented in-code, so no token enters any response body or Python state under the shipped design). If the owner chooses (ii), the delta is one method plus the realm's post-logout URI, foldable into a B6-scoped change set.
- **The browser-level in-tab navigation hop.** After the 200-JSON login (`{"redirect_uri": …}` + `Set-Cookie`), the `js_actions` `navigate` handler (`window.location.assign`) executes in a browser only; pytest pins the body, the URL and the cookie — not a browser performing the assignment.
- **B10's attribute persistence against a live realm.** Proven at the wire contract only (`attributes` map with array values, `201 Location` id capture); a real Keycloak realm accepting the write, and the admin UI exercising it end-to-end, are UI-tester items.

Also stated plainly: **AC5 was met only via B10** — before the residual pass, the claim-attributes editor could not persist (no `attributes` on `KeycloakUser`, no created-user id in the `201` body); B9's portal side was correct but inert without it.

### Process record

- **11 passes** (B1–B10 + the B10 residuals pass; B4 carries one rework iteration, B5 one, B5b one, B8 one) over the frozen patches `b{1,2,3,4,5,b5b,6,7,8,9,10}.patch` (+ `b4-fix1`).
- **2 plan-review iterations.** Iteration 1 returned 3 P1s + 8 P2s (all closed by the revision). The second returned a single **completeness P1** (B5b's two necessarily-touched test files) that the reviewer had dictated verbatim — file paths and line numbers included; since the ≤1-iteration bound was reached, the parent had the orchestrator apply exactly the dictated fixes and verified them on disk. Recorded as a **bound deviation**, not hidden.
- **Diff reviews** covered every pass against its frozen patch, including **two re-verification reviews** (B4 and B5b) after rework, and one narrowed re-dispatch (B5b's first review timed out at 30 min — reviewer task sizing is a real lever).
- **Authorized mid-pass supervisor escalations:** B7's `ISessionTokenAccessor` + its `MapMediatedReadEndpoints()` call site; B8's `Fetch.post`-cannot-follow-302 decision (the plan's 302 pairing was infeasible — a browser fetch follows the 302 cross-origin, consuming the code and dropping `Set-Cookie`); B5b's third fixture (`AuthServiceSmokeTests`); B6's `POST /auth/bootstrap/redeem` (flagged by B2's review, implemented in B6).
- **Six plan-completeness gaps found** (artifact listed, wiring/callers/fixtures omitted): the B4/B5 cross-pass callback spelling; B7's accessor + its call site; B8's folder-node pin row; B5b's two test rows; B2's `/auth/bootstrap/redeem` needing an implementer; B9's AC5 attribute gap.
- **Two P2s downgraded/withdrawn on evidence:** B4's casing P1 (withdrawn — the parameterless `System.Net.Http.Json` overloads already default to Web; probe Q3) and the B5 rework's rejected vacuous guard (the post-refresh re-read check was proved non-discriminating at 140/0 and removed rather than shipped).
- **Two open owner decisions** (both block only live verification): the auth service's dev OIDC posture (`DisableOIDCAuth=true` vs the passkey RP) and the D13 logout mechanism's final shape — plus the standing `Settings.Api` `flag_admin` carry from the review region.

### UI-tester handover (closed list — the tester never derives its own scope)

Changed UI surfaces, the pages/routes that render them, the client methods they call, and the navigation entry points:

| # | Entry | Rationale |
|---|---|---|
| 1 | `src/SchoolCollab.AuthPortal/app.py` — routes `/`, `/health`, `GET\|POST /login`, `GET /bootstrap`, `GET /logout`, the `/admin/*` pages | The portal's entire route table; every page the tester can reach, incl. the antiforgery-protected `POST /login`, the plain-302 `/bootstrap`, and the admin gate |
| 2 | `src/SchoolCollab.AuthPortal/views/login.py` | The prefab login form (password input, reactive validation, passkey button → `/auth/passkey/login?app_redirect_uri={portal}/bootstrap`) and the `js_actions` `navigate` handler — the in-tab hop after the 200-JSON login |
| 3 | `src/SchoolCollab.AuthPortal/views/errors.py` | The AC10 error cards (degraded states, `session ended`, invalid `return_uri`) the tester must see rendered at HTTP 200 |
| 4 | `src/SchoolCollab.AuthPortal/views/admin_users.py` | The users DataTable + create/edit form (tenant Select, teacher Combobox, D10 claim-attributes editor) — the AC5 surface, incl. `?error=credential_not_set` |
| 5 | `src/SchoolCollab.AuthPortal/views/admin_roles.py` | The role assign/unassign page (per-role buttons against `/admin/users/{id}/roles`) |
| 6 | `src/SchoolCollab.AuthPortal/api/auth_api_client.py` | The portal's auth-service calls the views drive: exchange, bootstrap redeem, session read/revoke — the tester observes their effects (cookie set/cleared, cards) |
| 7 | `src/SchoolCollab.AuthPortal/api/admin_api_client.py` | The admin/picker calls behind the admin views (`/auth/admin/*`, `/auth/pickers/*` with `X-Portal-Session`) |
| 8 | `src/SchoolCollab.Admin/Program.cs` | The admin host's handshake group (`/signin-handshake`, `/login`, `/logout`) — entry points for the flag-ON handshake and logout flows |
| 9 | `src/SchoolCollab.Families/Program.cs` | The same handshake group on the families host |
| 10 | `src/SchoolCollab.Core/Auth/PortalHandshakeExtensions.cs` | The shared redemption endpoint group + typed redeem client both hosts map — the code the callback lands on |
| 11 | `src/SchoolCollab.Core/Auth/PortalRedirectChallengeHandler.cs` | The flag-ON 302 to `portal /login?return_uri=…` (and the 401 fail-closed posture) the tester sees when hitting a gated page |
| 12 | `src/AppHost/SchoolCollab.AppHost/Program.cs` (portal registration + flag fan-out) | How the tester starts the topology: `auth-portal` uvicorn app, the `feature-flag-disable-keycloak-login-ui` parameter, and the per-consumer env keys |

Out of the tester's scope (owner-gated, listed above): the dev WebAuthn ceremony, the full D13 browser-facing logout redirect, and live-realm attribute persistence beyond what the running realm demonstrates.

### Verdict

**CLOSED.** All eleven passes closed with zero outstanding findings; all eight round-B criteria met at the static/unit level; the P1 ledger is closed (one plan-level fix → B5b, one withdrawn false positive, one parent freeze error); residuals are the behaviour-neutral P2s listed above. The owner must additionally accept the four LIVE/OWNER-GATED items (dev WebAuthn ceremony, full D13 logout mechanism, the browser-level navigation hop, live-realm attribute persistence) — none of them blocks a static claim made in this round.

## UI Tester

Run `5fc4258b-40e5-41b8-8fb4-e383943d99b0` — `ollama/minimax-m3:cloud`, read-only, scope taken **verbatim** from the accept's closed 12-entry handover.

    UI TEST
    Scope ack: Confirmed — read all 12 handed-over surfaces (portal app.py routes; views/login.py, views/errors.py, views/admin_users.py, views/admin_roles.py; api/auth_api_client.py, api/admin_api_client.py; Admin/Program.cs; Families/Program.cs; PortalHandshakeExtensions.cs; PortalRedirectChallengeHandler.cs; AppHost Program.cs — portal registration + fan-out only).
    Verdict: P2-only
    P1: none
    P2: views/login.py:138 — Field(invalid=Rx("!{name}")) binds invalidity to "input is empty", so the FieldErrors ("Enter your username." / "Enter your password.") are visible BEFORE the user has touched the form; after a failed submit the page re-renders an Alert banner PLUS the still-visible field errors on a freshly empty form — two error layers competing for attention (sign-in still completes).
    P2: views/admin_users.py:268 — the users list renders a duplicate of the DataTable: a "Manage" card below the table iterates every user and emits a row with display-name + Edit + Roles. Edit/Roles navigation lives only in that duplicate list (the DataTable rows are not clickable), so any non-trivial user count doubles the per-user UI and the only way to reach a user from the table is to find them again in Manage.
    P2: views/admin_users.py:374 — the DataTable Tenant/Teacher columns write the raw user.tenant_id / user.teacher_id (opaque identifiers) instead of resolving them through the picker rows; PickerTenant.label / PickerTeacher.label carry the names the form renders, but _table_row does not look them up, so the table shows ids the user cannot interpret while the create/edit form shows readable names.
    Out-of-round observations: none

**No P1** — no flow is blocked, so no orchestrator rework plan was required. The parent nonetheless dispatched a bounded **UI polish pass (B11)** for all three findings (they are cheap, highly visible, and in two files), after which the tester **re-verifies only** (the protocol's tester loop) and the parent statically checks the rework diff for conformance (no reviewer re-run).

**B11 — the UI polish pass (`ollama-cloud/deepseek-v4.1-flash`; patch `documents/rounds/diffs-keycloak-ui-auth-b11.patch`, 4 files).**

    WORKER REPORT (pass B11)
    Changed files (4 — exactly B11's rows; no new files): views/login.py, views/admin_users.py, tests/test_login_view.py, tests/test_admin_views.py.
    Build: 0 errors. Tests: AuthPortal pytest 123 passed / 0 failed (117 -> +6); ArchitectureTests.Unit 62/0.
    Finding 1 fixed with the endorsed submit-driven shape: PrefabApp(state={"attempted": False}) + invalid=Rx("attempted && !<field>") + on_submit=[SetState("attempted", True), Fetch.post(...)]. The leading SetState is LOAD-BEARING, not cosmetic: prefab evaluates the whole attribute template and falls back to the raw (truthy) template string when the key is undefined, so an undeclared gate would repaint the first-paint bug. _submit_action now returns list[Action]; the request moved to a private _login_request(...) so each action has one job.
    Finding 2 fixed: rows carry the actions (a component-valued cell) and _manage_section was deleted — exactly one list, a user reachable once.
    Finding 3 fixed: _picker_labels builds id -> label maps from the picker rows and drops any row whose label fell back to its own id; the table renders the resolved label or UNRESOLVED_CELL ("—"), never a raw id (the one judgement call in the pass).
    Probes (reverted, run, restored, md5sum -c OK): P1 revert invalid=Rx(f"!{name}") -> 3 failed/120 passed; P2 drop state={attempted: False} -> 2/121; P3 re-add the duplicate Manage card -> 1/122; P4 revert the columns to raw ids -> 2/121. Two pinned tests updated deliberately (the submit action is now a 2-action chain; the table columns now include actions). Not run: an end-to-end browser check (out of a bounded polish pass; the tester owns it).

    UI TEST — re-verify (tester loop, iteration 1)
    Scope ack: Confirmed — re-verify limited to the two touched view modules and their two test files; the three prior findings were the only re-hunt targets.
    Verdict: PASS
    P1: none
    P2: none
    Out-of-round observations: views/admin_users.py:107 imports Badge but no longer uses it in this module's tree (surviving uses are in build_admin_card) — a dead import to clean up next time the file is touched.

**All three UI-test findings CLOSED and re-verified**: the first paint is clean (the `state={ATTEMPTED_STATE: False}` declaration is what keeps `{{ attempted && !username }}` from resolving to the raw truthy template string), a failed submit shows exactly one Alert with the gate reset, the list reaches each user once through the table's own row actions, and the Tenant/Teacher columns render `Demo (school)` / `Ada Lovelace` or the deliberate em-dash. Sign-in and the admin pages did not regress. **Round B is CLOSED.**