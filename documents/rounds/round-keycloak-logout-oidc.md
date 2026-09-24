Provider: pi, Tier 3 lean (models: glm-5.3-flash orchestrator, deepseek-v4.1-flash worker, **deepseek-v4.1-flash plan-review — OWNER OVERRIDE 2026-09-24**: the plan reviewer therefore shares the worker's model, a recorded deviation from the skill's "the verifier must not be the implementer's own model" guardrail; the per-pass **diff reviewer stays kimi-k2.7-code**, so the implementation review remains independent of the implementer; no UI tester — UI trigger does not fire) · base 670fff17 · tree dirty at round start: .gitignore only (approved repo-hygiene edit, committed separately) · branch stack/18-keycloak-logout-oidc

## Plan

Round `keycloak-logout-oidc` closes the two owner-gated items round B left open (see
`documents/rounds/round-keycloak-ui-auth-b.md` § "Static-verified vs LIVE / OWNER-GATED"):

1. **D13 logout = option (ii)** (owner-settled, **binding**): `DELETE /auth/session/{id}` returns
   the **fully-built `end_session` URL including `id_token_hint` AND `post_logout_redirect_uri`**;
   the portal clears its cookie and 302s the browser to that URL. Two hard rules ride with it:
   (a) the portal treats the returned URL as an **opaque string** — never parsed, logged or
   persisted; (b) **AC11 is reworded** to name what it actually protects — no access/refresh
   token, no key material, no client secret — so the browser-visible `id_token_hint` logout
   *hint* is not conflated with a bearer credential. The owner **rejected** option (iii) (the
   auth service performing the browser-facing redirect) and noted (iv) (permanently token-free)
   as the only shape that would keep the hint out of the browser URL.
2. **The auth service's dev OIDC posture = (B) scoped as (b1)** (owner-settled, **binding**):
   `AddAuthAndTenancy` gains an **explicit opt-in** — `requireOidcRelyingParty: true` — so the
   OIDC relying-party **pipeline** is registered **for the auth service only**, with `TestAuth`
   remaining the default scheme when `FEATURE:DisableOIDCAuth` is on. Every other host's dev
   pipeline must stay unchanged. Rationale: D16 makes the auth service *the* relying party; the
   dev bypass exists to spare **consumers** from needing Keycloak, not to strip the RP of its
   challenge ability. The dev-bypass clause is **amended** to carve this out.

Spec-text note (one spelling): the shared registration's real name is
**`AuthTenancyExtensions.AddAuthAndTenancy`** (`src/SchoolCollab.Core/Auth/AuthTenancyExtensions.cs:44`)
— the shorthand "AddSchoolCollabAuth" from the decision thread does not exist in code; every
traceable reference below uses the source name.

### Goal

A dev topology where (a) the portal's logout flow ends at Keycloak's real `end_session` URL
carrying `id_token_hint` + `post_logout_redirect_uri`, with the portal an opaque forwarder, and
(b) the auth service — and only the auth service — can run the OIDC **relying-party pipeline**
even under `FEATURE:DisableOIDCAuth=true`, so the scheme `PasskeyEndpoints.cs:121` challenges by
name (`ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, …)`) resolves and the
`/signin-oidc` callback has a sign-in scheme to persist into.

**What this does NOT claim** (PLAN REVIEW feasibility residual): the end-to-end passkey *ceremony*
in a live browser still needs a real authenticator and stays **owner-gated**. This round proves the
pipeline **composes** — the challenge scheme and the cookie sign-in scheme are registered with
`TestAuth` still the default — not that a human can complete WebAuthn in dev.

### Scope IN

- `documents/specs/keycloak-ui-auth-integration.md` — D13/§9 clarification to (ii); **AC11
  reworded**; **AC9 (`:416`) and §9's "Tokens never reach the browser or Python state" (`:247`)
  given the same `id_token_hint` carve-out** (they currently contradict option (ii) —
  PLAN REVIEW P1-4); and the dev-bypass clause amended at its **real home, §2 (`:56-57`)** —
  never §14 (PLAN REVIEW P2-1).
- `src/SchoolCollab.Core/Auth/AuthTenancyExtensions.cs` — the (b1) opt-in parameter + the
  conditional OIDC-RP **pipeline** registration in the `disableOIDC` branch.
- `src/SchoolCollab.Auth/` — `BuildEndSessionUrl()` (hint + post-logout URI), the id-token
  capture on the revocation record, the post-logout redirect option key, the auth service call
  site.
- `src/AppHost/SchoolCollab.AppHost/` — the auth portal's **pinned host port**, the fan-out of
  the new post-logout key, and the realm's `postLogoutRedirectUris`.
- `src/SchoolCollab.AuthPortal/app.py` — the stale option-(iii) doc-comment only (behaviour is
  already exactly (ii); verified at `app.py:792-835`).
- `src/SchoolCollab.AuthPortal/tests/test_session_routes.py` — **extend** the existing
  `endSessionUrl` stub / 302 pin (`:479-510`), not "add" (PLAN REVIEW P2-5).
- `documents/configuration.md` in the same change set as each code pass.
- Tests: `SchoolCollab.Core.Tests.Unit` (wiring), `SchoolCollab.Auth.Tests.Unit` (provider /
  store / endpoints / options fixtures), `SchoolCollab.ArchitectureTests.Unit` (realm guard —
  run **standalone**).

### Scope OUT (explicit)

- **OIDC back-channel logout** — deferred (spec §16.3); RP-initiated only.
- **The `Settings.Api` `flag_admin` gate** — the standing review-region carry; not this round.
- **Any provider other than Keycloak** — D15's seam stays one-implementation; no `IAuthProvider`
  member added or reordered (the pass-3 capture is an additive field on the *revocation record*,
  not a seam change).
- **No portal behaviour change** — the portal already revokes, clears the cookie and 302s to the
  received URL (`app.py:835`); the only portal code touch is its doc-comment.
- **No Blazor-host logout changes** — the OIDC handler's `SignOut` is untouched; the realm's
  `postLogoutRedirectUris` addition (pass 3) covers the URIs it already sends.
- **No other host's pipeline changes** — the five other `AddAuthAndTenancy` call sites are not
  edited; a guard test pins their dev posture **scheme-for-scheme** (TestAuth still default, no
  OIDC scheme, no cookie scheme). Source byte-identity of those hosts is the diff review's job,
  not the test's (PLAN REVIEW P2-3).
- The **`.gitignore` edit is NOT a pass** — it is committed separately (approved repo-hygiene
  edit).

### Pass 1 — docs/config first

| Pass | Path | N/M | Why |
|---|---|---|---|
| 1 | `documents/specs/keycloak-ui-auth-integration.md` | M | (a) D13 and §9 restated as **option (ii) with the opaque-URL rule**: the portal treats the returned `end_session` URL as an opaque string — never parses, logs or persists it — and option (iii) is recorded as owner-rejected, (iv) as the only hint-free alternative. (b) **AC11 reworded** to name what it protects: *the portal holds no credential that grants or extends access — no access token, no refresh token, no cookie-signing or other key material, no client or service-account secret — and performs no direct HTTP call to `settings-api`/`students-api`; every identity, privilege and token-bearing read is mediated by the auth service. The `id_token_hint` in the RP-initiated `end_session` URL is a logout hint over an already-spent token, not a bearer credential (D13, option ii).* (c) **NEW (PLAN REVIEW P1-4)** — **AC9 (`:416`) and §9's "Tokens never reach the browser or Python state" (`:247`) receive the same carve-out.** Without it the spec contradicts the shipped behaviour: option (ii) deliberately places the hint in a browser URL and, transiently, in the portal's opaque `revocation.end_session_url` field. The amended wording must say exactly that — a spent-token logout hint, present transiently as an opaque response field the portal never parses/logs/persists, is not a credential the portal *holds*; the invariant that survives is "no token that grants or extends access". (d) The dev-bypass clause is amended at its **real home, §2 (`:56-57`)** — never §14, which is "Security considerations" (`:329`) and contains no dev-bypass clause (PLAN REVIEW P2-1). (e) **NEW — re-read P1: D12's row (`:120`) and its rationale cell join the rewrite.** D12 ends "…the portal uses them only via mediated auth-service endpoints (D17). **Tokens never live in the browser or in Python state**", and its rationale cell says "nothing sensitive in browser or Python state". Option (ii) contradicts both exactly as AC9/§9`:247` did, so D12 receives the identical carve-out wording — otherwise pass 1 leaves the spec still contradicting the shipped behaviour and **K-B is unmet**. |
| 1 | `documents/configuration.md` | M | the sentences describing the portal's no-credential posture and the `DELETE /auth/session/{id}` response contract (§4 region) aligned with the reworded AC11 and option (ii); §5's auth-service row notes the dev OIDC-RP carve-out lands with pass 2 (cross-referenced, not duplicated). |

**Closure note (pass 1):** docs-only — no code, no fixtures. The pass's verification is the diff
review, not a test run. **Five** clause rewrites ship together or none do: D13/§9 (option ii +
opaque rule), AC11, **AC9 + §9's token-never-reaches-browser/Python claim**, **D12 `:120` + its
rationale cell**, and §2's dev-bypass clause. Any doc drift discovered during pass 2/3 is fixed **in that pass's change set**, never by
reopening pass 1.

### Pass 2 — (b1): the auth-service RP opt-in

**Design correction (PLAN REVIEW P1-2).** The first draft registered *only* the OIDC scheme. That
cannot make the passkey path work: `PasskeyEndpoints`' `/complete` reads a **Cookies-scheme**
ticket that the OIDC handler writes via `SignInScheme`, so with no cookie scheme registered the
`/signin-oidc` callback fails closed. The opt-in therefore registers the **RP pipeline —
`.AddCookie()` + the shared `.AddOpenIdConnect(...)` options configurator — with `TestAuth`
remaining the DEFAULT scheme.**

| Pass | Path | N/M | Why |
|---|---|---|---|
| 2 | `src/SchoolCollab.Core/Auth/AuthTenancyExtensions.cs` | M | add an optional parameter `bool requireOidcRelyingParty = false` (trailing default → the five other call sites compile unchanged). In the `if (disableOIDC)` branch (`:83-87`), when the opt-in is set, additionally register the **RP pipeline**: `.AddCookie()` and the OIDC scheme using **the same private options configurator the `else` branch uses** (factored out of the `:120-152` lambda — one concept, one spelling; that lambda depends only on `IConfiguration`-derived values, so the `else` branch stays behaviourally identical). The `??` defaults (`Authority`/`ClientId`/`ClientSecret`) mean registration cannot fail startup with Keycloak absent — metadata fetch is lazy (PLAN REVIEW feasibility residual; confirm on first challenge). `TestAuth` remains `DefaultScheme`; the policy scheme, JwtBearer and the portal-redirect handler are **not** registered in this branch. |
| 2 | `src/SchoolCollab.Auth/Program.cs` | M | the sole opt-in call site: `builder.Services.AddAuthAndTenancy(builder.Configuration, requireOidcRelyingParty: true);` (`:21`). The other five call sites (Admin `:22`, Families `:22`, Settings.Api `:30`, Students.Api `:88`, Assignments.Api `:188`) are **not edited** — the guard pins them. |
| 2 | `tests/SchoolCollab.Core.Tests.Unit/Auth/AuthWiringTests.cs` | M | **both-direction guard** plus the RP-pipeline composition proof. Build the `ServiceCollection` twice with `FEATURE:DisableOIDCAuth=true` — once with the opt-in, once without — and resolve `IAuthenticationSchemeProvider`: **opt-in** → `GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme)` **and** the cookie sign-in scheme **by name** — `CookieAuthenticationDefaults.AuthenticationScheme`, which is what `/complete` reads (`PasskeyEndpoints.cs:164`) and OIDC's default `SignInScheme`; a misnamed `.AddCookie("Other")` must NOT satisfy the guard — both resolve, and `GetDefaultScheme()` is still `TestAuth`; **non-opt-in** → no OIDC scheme, no cookie scheme, default scheme `TestAuth` (the load-bearing direction — it fails if anyone hoists the registration out of the opt-in). **This replaces the first draft's pass-2 `AuthEndpointTestHost` + `PasskeyBootstrapTests` rows** (PLAN REVIEW P1-2): that host pins `FEATURE:DisableOIDCAuth=false` (`AuthEndpointTestHost.cs:110-115`), composes with no opt-in (`:141`) and lacks `IDistributedCache`, so the flag-ON posture is unreachable there and the row would have been functionally empty. |
| 2 | `documents/configuration.md` | M | §2's `feature-flag-disable-oidc-auth` row and the §4/§5 dev-mode prose gain the carve-out sentence: the **auth** service registers the OIDC **relying-party pipeline (cookie + OIDC)** even when the flag is ON (opt-in parameter, `TestAuth` still default); every other consumer's dev pipeline is unchanged. Same change set. |

**Closure note (pass 2):** closed over (a) **all six `AddAuthAndTenancy` call sites** — one edited,
five pinned by the guard; (b) the **OIDC options configurator's two consumers** (both branches, so
the factoring cannot drift); and (c) the **`ServiceCollection` the composition test builds**. The
`AuthEndpointTestHost` / `PasskeyBootstrapTests` pass-2 rows are **deliberately dropped, not
forgotten** (P1-2 above). No startup-validated key is added in this pass, so no `FirstValidationError`
/ `AcceptsCompleteConfiguration` fixture is touched — pass 3 owns that class of closure.

### Pass 3 — D13 option (ii): hint + post-logout URI

**Design corrections from PLAN REVIEW P1-1 and P2-2.**

**P1-1 — the realm literal must be able to match the fanned value.** `AddProject` hosts get fixed
ports from `launchSettings.json` (`5300/7300`, `5400/7400`, `55458/55459`), which is exactly why the
realm carries them as literals. `auth-portal` is `AddUvicornApp` — a Python resource with **no
`launchSettings.json` and no pinned port** — so `authPortal.GetEndpoint("http")` (`Program.cs:419`)
is run-dependent and can never equal a committed literal; Keycloak would reject the logout
(`post_logout_redirect_uri` unregistered) and no hermetic test could see it. **The portal's host port
is therefore pinned** (`WithHttpEndpoint`, mirroring the .NET hosts' fixed dev ports), and the realm
literal and the fan-out expression are stated together so they agree **by construction**: pinned port
`5700` → fanned `$"{authPortal.GetEndpoint("http")}/"` and realm `http://localhost:5700/` — spelled
identically, trailing slash included (Keycloak matches `post_logout_redirect_uri` **exactly**; a
wildcard would broaden the allowlist and is rejected).

**P2-2 — the ordering wording was self-contradictory.** `Revoke` *produces* the revocation record;
there is no "read before revoke". The working shape is: `PortalSessionEntry` already carries
`IdToken`, `PortalSessionStore.Revoke` returns it on `PortalSessionRevocation`, and
`KeycloakAuthProvider.LogoutAsync` uses the returned record (`Revoke` at `:319`,
`BuildEndSessionUrl` at `:331`). **No second store read** — a `Get`-then-`Revoke` shape is
race-prone and returns null for an expired entry.

| Pass | Path | N/M | Why |
|---|---|---|---|
| 3 | `src/SchoolCollab.Auth/Services/PortalSessionStore.cs` | M | `PortalSessionRevocation` gains `string? IdToken`, populated from the entry's existing `IdToken` at revocation time (`Revoke`, `:174-179`) and returned to the in-service caller only — never a response payload (comment pinned). Unknown/expired → null. No `Get`-then-`Revoke` shape. |
| 3 | `src/SchoolCollab.Auth/Providers/KeycloakAuthProvider.cs` | M | `LogoutAsync` passes the revocation record's id token to `BuildEndSessionUrl(string idToken)`, which emits `{authority}{LogoutPath}?id_token_hint={token}&post_logout_redirect_uri={uri}` (both `Uri.EscapeDataString`-encoded). The doc-comment's option-(iii) remarks are replaced with the option-(ii) adjudication + the opaque-URL rule; the `client_id`-only fallback wording is retired. |
| 3 | `src/SchoolCollab.Auth/Providers/IAuthProvider.cs` | M | `ProviderLogout` doc-comments updated: `EndSessionUrl` now carries `id_token_hint` + `post_logout_redirect_uri` (D13 option ii) and is opaque to the portal. No member signature changes (seam frozen, D15). |
| 3 | `src/SchoolCollab.Auth/Options/AuthServiceOptions.cs` | M | new `PostLogoutRedirectUri` key, **startup-validated** (`FirstValidationError`, fail-closed like `AppCallbackPrefixes`). No code fallback. |
| 3 | `src/AppHost/SchoolCollab.AppHost/Program.cs` | M | (a) **pin the portal's host port** so the realm literal can equal the fanned value. Mechanics pinned by the re-read: the pin **updates the toolkit-created `"http"` endpoint in place** — never adds a second annotation, because `GetEndpoint("http")` is resolved at `:419`, `:430`, `:436`, `:454` and `:485`; pass **both** `port` and `targetPort` (repo precedent `:42` passes both — ar-21's F2 recorded a missing-`targetPort` defect). The endpoint name `"http"` is provably the toolkit's (those five call sites already resolve it), so no guessing is required. **Residual (runtime-only, owner-gated):** that the *effective* portal URL is `5700` at runtime is not observable by any test in this round — the realm guard proves only the textual literal; record it beside the WebAuthn residual. (b) fan `Auth__PostLogoutRedirectUri` = `$"{authPortal.GetEndpoint("http")}/"` to the **auth service only** (placed after `:409` where `authPortal` exists — the `:430`/`:436` precedent — never beside `WireKeycloakAuth` at `:401`). |
| 3 | `src/AppHost/SchoolCollab.AppHost/school-collab-realm.json` | M | `school-collab-client` gains a **`postLogoutRedirectUris`** block (none exists today, so `post_logout_redirect_uri` has nothing to match): the exact literal `http://localhost:5700/` plus the four existing per-app `/signout-callback-oidc` URIs (5300/7300/5400/7400, both schemes). **Row rationale (PLAN REVIEW P2-4):** a *portal* URL is legitimate here even though the D16 negative assertion forbids a portal **`redirectUris`** entry (`AppHostRealmImportArchitectureTests.cs:278-283`) — nothing code- or token-bearing is delivered to a post-logout URI; the two assertions must coexist and the guard comment must say why, so D16's rationale is not silently narrowed. Strict-JSON constraints hold. |
| 3 | `tests/SchoolCollab.ArchitectureTests.Unit/AppHostRealmImportArchitectureTests.cs` | M | guard extension: `postLogoutRedirectUris` present; contains the four `signout-callback-oidc` URIs; **contains the exact portal landing literal that agrees with the port pinned in `Program.cs`** (a static cross-check of P1-1, so the two can never drift); the D16 `redirectUris` negative stays green and is commented with the distinction above; strict JSON / filename / bind-mount parity stay green. |
| 3 | `tests/SchoolCollab.Auth.Tests.Unit/KeycloakAuthProviderTests.cs` | M | **two existing assertions are INVERTED, not "still green" (PLAN REVIEW P1-3)**: `:292` `Contain("client_id=…")` and `:299` `NotContain(ClaimBearingIdToken)` both assert the retired shape. Also rename the stale test name `:282` `…ReturnsATokenFreeEndSessionUrl` and its comment `:294-296` ("id_token_hint is deliberately absent") — re-read P2. Rewrite as: the URL carries `id_token_hint` **and** `post_logout_redirect_uri` (correctly encoded); the hint **equals** the custody id token; the access and refresh token **values** never appear. Plus `Revoke` returns the id token to the caller — **value equality is the discriminating assertion**; the first draft's ordering-probe sentence is dropped (with `Revoke` returning `IdToken`, a capture-after-revoke implementation would *pass* it). |
| 3 | `tests/SchoolCollab.Auth.Tests.Unit/PortalSessionStoreTests.cs` | M | `Revoke` returns the session's id token (and refresh token) on the revocation record; unknown/expired session → both null. |
| 3 | `tests/SchoolCollab.Auth.Tests.Unit/SessionEndpointTests.cs` | M | **two existing assertions are INVERTED (PLAN REVIEW P1-3), at `:250` (`endSessionUrl.Should().Contain("client_id=school-collab-client")`) and `:261` (`body.Should().NotContain(IdToken)`) — the latter asserts the seeded id-token *value* is absent, which the hint now **is**.** (The re-read corrected the first draft's `:265`/`:281` refs.) Also rename the stale names/comments that still encode the retired shape: `:237` `…_WithNoTokenInBody`, and the comment at `:252-258`. **A fifth, same-pattern assertion at `:78` (the GET body) stays GREEN and must NOT be inverted** — that body carries no hint (re-read P2); say so in the row so a worker does not "fix" it. Rewrite `:250`/`:261` as a **value-based** scan: `endSessionUrl` carries both parameters; the **access- and refresh-token values** are absent from **every** response body; the hint value appears exactly once, in the opaque URL. |
| 3 | `tests/SchoolCollab.Auth.Tests.Unit/AuthServiceOptionsTests.cs` | M | closure fixture: `Valid()` gains `PostLogoutRedirectUri`; new `Validator_RejectsEmptyPostLogoutRedirectUri` naming the key (otherwise `Validator_AcceptsCompleteConfiguration` goes red). |
| 3 | `tests/SchoolCollab.Auth.Tests.Unit/AuthServiceSmokeTests.cs` | M | closure fixture: `OptionsValidator_AcceptsCompleteConfiguration` gains the new key. |
| 3 | `tests/SchoolCollab.Auth.Tests.Unit/AuthEndpointTestHost.cs` | M (**single touch — pass 3 only**) | closure fixture: the composed host's in-memory config dictionary gains `Auth:PostLogoutRedirectUri` (a startup-validated key absent there fails `ValidateOnStart`). The pass-2 touch was dropped as functionally empty (P1-2), so this file is touched **once** — not twice as the first draft said. |
| 3 | `src/SchoolCollab.Auth/Endpoints/SessionEndpoints.cs` | M | `DeleteSessionResponse` / `Delete` doc-comments: the returned URL is **opaque to the portal** (never parsed, logged or persisted); no code change. |
| 3 | `src/SchoolCollab.AuthPortal/app.py` | M | **doc-comment only** (`:797-803`): delete the stale option-(iii) paragraph; state option (ii) + the opaque-URL rule. Behaviour untouched — `_signed_out_response(revocation.end_session_url)` already 302s to the received URL (`:835`). |
| 3 | `src/SchoolCollab.AuthPortal/tests/test_session_routes.py` | M (**extend**, not add) | the file already stubs `endSessionUrl` and pins the 302 (`:479-510`): extend it so the stubbed URL **contains both parameters** and is 302'd **byte-identically** into `Location` (no re-encoding, no parsing, no logging), with the session cookie cleared on the same response. |
| 3 | `documents/configuration.md` | M | §4 row for `Auth:PostLogoutRedirectUri` (+ §11 env-var row `Auth__PostLogoutRedirectUri`, auth only), the **pinned portal port** note, and the realm `postLogoutRedirectUris` note. Same change set. |

**Closure note (pass 3):** closed over (a) the **call site** of `BuildEndSessionUrl` — exactly one
(`KeycloakAuthProvider.cs:331`); (b) the **revocation record's producer and readers**
(`PortalSessionStore.Revoke` → `KeycloakAuthProvider.LogoutAsync` + their tests); (c) the
**"complete configuration" fixtures a new startup-validated key breaks** — `AuthServiceOptionsTests`
(`Valid()` + `Validator_AcceptsCompleteConfiguration`), `AuthServiceSmokeTests`
(`OptionsValidator_AcceptsCompleteConfiguration`), and `AuthEndpointTestHost`'s in-memory config;
(d) the **four existing assertions that assert the retired shape** (P1-3, named in the rows above);
and (e) the **realm literal ↔ pinned-port agreement** (P1-1), which is a convention checked by the
extended realm guard rather than a fact.

**Execution split (parent, recorded 2026-09-24).** Pass 3 is dispatched as **3a** (the C#
auth-service rows: `PortalSessionStore`, `KeycloakAuthProvider`, `IAuthProvider`, `AuthServiceOptions`,
`SessionEndpoints`, and their six test files) and **3b** (AppHost port pin + `Auth__PostLogoutRedirectUri`
fan-out, the realm's `postLogoutRedirectUris`, the realm guard, the portal doc-comment + pytest
extension, and `configuration.md`'s §11 / pinned-port / realm notes). Reason: 17 files across four
projects exceeds a safe single 30-minute worker window (the ar-15 timeout lesson). **The
expected-files rows above are unchanged** — only the dispatch is split, both halves are reviewed
against these same rows, and no file is touched by both halves: **all `documents/configuration.md`
hunks belong to 3b** (3a's brief barred that file, which is why its §4 option-key row is written in
3b; corrected here after 3a flagged the conflict instead of guessing).

### Acceptance criteria (round-style; verdicts stay open until the parent accepts)

| # | Criterion (spec AC / decision) | Verdict | Evidencing pass + suite |
|---|---|---|---|
| K-A | **D13 = option (ii):** `DELETE /auth/session/{id}` returns the fully-built `end_session` URL carrying **`id_token_hint` + `post_logout_redirect_uri`**; the portal clears its cookie and 302s to it; the URL is treated as opaque (no parse/log/persist) | — | 3 — `KeycloakAuthProviderTests`, `SessionEndpointTests`, portal `test_session_routes` |
| K-B | **Spec consistency:** AC11 reworded to name access/refresh token, key material and client secret while explicitly not conflating the `id_token_hint` logout hint with a bearer credential; **AC9 (`:416`), §9 (`:247`) and D12 (`:120` + its rationale cell) carry the same carve-out**; option (iii) recorded as owner-rejected; §2's dev-bypass clause amended at its real location | — | 1 — diff review of the spec change (explicitly not a test) |
| K-C | **(b1):** with `FEATURE:DisableOIDCAuth=true`, the auth service (opt-in only) registers the OIDC RP **pipeline** with `TestAuth` still the default scheme; every other host's dev posture is **scheme-for-scheme** unchanged (source byte-identity is the diff review's job); §2's clause amended accordingly | — | 2 — `AuthWiringTests` (both directions) |
| K-D | **RP pipeline composes under flag-ON:** the scheme `PasskeyEndpoints.cs:121` challenges by name resolves, and a cookie sign-in scheme exists for the `/signin-oidc` callback. **The live browser ceremony is explicitly NOT claimed** (owner-gated) | — | 2 — `AuthWiringTests` scheme-provider assertion |
| K-E | **Realm:** `school-collab-client.postLogoutRedirectUris` exists with the four Blazor `signout-callback-oidc` URIs **and the portal landing literal that agrees with the pinned port**; strict-JSON/filename/parity guards stay green | — | 3 — `AppHostRealmImportArchitectureTests` (standalone) |
| K-F | **No-token invariant preserved (value-based):** no response body ever carries an access- or refresh-token **value**; the only browser-visible token-shaped material is the `id_token_hint` logout hint inside the opaque `end_session` URL | — | 3 — `SessionEndpointTests` value scans, `KeycloakAuthProviderTests` negative assertions |
| K-G | **Config/docs in the same change sets:** every new key documented in `documents/configuration.md` in the pass that adds it; spec clauses amended in pass 1 | — | 1/2/3 — per-pass config rows + diff review |
| K-H | **Build/tests (inheritance):** `dotnet build SchoolCollab.slnx` 0 errors; every touched suite 0 failures (`SchoolCollab.ArchitectureTests.Unit` run **standalone**); every new guard discriminating (pre-fix probe + observed counts) | — | all passes — parent-verified at acceptance |

### Per-pass discriminating tests (what pre-fix behaviour fails each)

**Pass 1 —** no tests (docs). Verification: the diff reviewer checks the **four** clause rewrites
(D13/§9 opaque rule, AC11, AC9 + §9's browser/Python claim, §2's dev-bypass clause) against the
binding decisions above.

**Pass 2:**

- `AuthWiringTests` opt-in direction — pre-fix: no `requireOidcRelyingParty` parameter exists
  (compile), and with the flag ON no OIDC scheme **and no cookie scheme** are registered → the
  scheme-resolution assertions fail. **Honest note:** this discriminates via a new parameter and
  scheme absence, not via behaviour.
- `AuthWiringTests` default-scheme pin — pre-fix: already true; it catches an over-wide
  implementation that flips `DefaultScheme` to OIDC.
- `AuthWiringTests` non-opt-in direction (`NonOptInHosts_DevPipelineIsSchemeForSchemeUnchanged` — renamed by the re-read from `…IsByteIdentical`, which contradicted the plan's own P2-3 fix) — pre-fix:
  passes; it is the **load-bearing direction**: it fails if the OIDC/cookie registration is hoisted
  out of the opt-in, pinning the five unedited call sites' posture.

**Pass 3:**

- `KeycloakAuthProviderTests.LogoutAsync_ReturnsUrlWithIdTokenHintAndPostLogoutRedirectUri` —
  pre-fix: the URL is `…logout?client_id=…` → **both** parameter assertions fail.
- `KeycloakAuthProviderTests`/`PortalSessionStoreTests` `Revoke` returns the id token — pre-fix:
  `PortalSessionRevocation` has no `IdToken` member → fails. **The discriminating assertion is the
  returned value's equality with the custody id token**, not an ordering probe.
- `KeycloakAuthProviderTests` no-access/refresh-value guard — pre-fix passes trivially; post-fix it
  pins the hint-only shape (a naive "send the whole token set" implementation fails it).
- `SessionEndpointTests` delete-shape + value scan — pre-fix: the body's `endSessionUrl` lacks both
  parameters, and `:281` positively asserts the hint value's **absence** → fails.
- Portal `test_session_routes` opaque-forward pin — the C# side is the pass-level discriminator;
  this test pins the **invariant** (byte-identical forward + cleared cookie + no URL logging) that
  would fail if anyone later parses or re-encodes the URL. Conceded as non-discriminating pre-fix.
- `AppHostRealmImportArchitectureTests` post-logout guard — pre-fix: no `postLogoutRedirectUris`
  block → the presence/URI-membership and pinned-port-agreement assertions fail.
- `AuthServiceOptionsTests.Validator_RejectsEmptyPostLogoutRedirectUri` — pre-fix: no key, no
  validator clause → fails (and `Validator_AcceptsCompleteConfiguration` fails after the key exists
  without the fixture update, which is why the fixture rows are in the table).

### Worker task-spec skeleton (each pass)

- Worker: `ollama-cloud/deepseek-v4.1-flash`; **30-minute cap**; repo-scoped searches only;
  **never `find /`**; never commit/push/`gh`; never edit this round doc.
- Scope: exactly the pass's expected-files rows; any discovered necessary touch outside the rows →
  stop and escalate, never widen silently.
- Build after every code change: `dotnet build SchoolCollab.slnx` (the `.slnx` has **no globbing** —
  every new file needs an explicit `<File>` item; CPM: versions only in `Directory.Packages.props`,
  never a `Version=` on a `PackageReference`).
- Tests: `dotnet test tests/<Project>` with no extra flags; `SchoolCollab.ArchitectureTests.Unit`
  run **standalone**.
- Result read rule (one grep, ≤2 attempts):
  `dotnet test tests/SchoolCollab.Auth.Tests.Unit 2>&1 | grep -E "^\s*failed |total:|failed:" | head -40`
  (substitute the project under test).
- Report: changed files, build/test numbers with the discriminating probes (revert → observed
  failure counts → restore, md5-verified), deviations, open risks.

### Reviewer task-spec skeleton (each pass)

- **Diff** reviewer: `ollama-cloud/kimi-k2.7-code`, read-only, bounded to the frozen per-pass patch.
- Verdict PASS / P1 (blocks: correctness, security, plan-conformance, missing closure rows,
  non-discriminating guards) / P2 (carried, behaviour-neutral); best-practices sweep per
  `.github/copilot/rules/dotnet-best-practices.md` for `.cs`/`.razor` changes.
- Diff-review check: the patch's file list must equal the pass's expected-files rows (the B5 freeze
  lesson); any extra file is a P1 until the parent amends the rows.

### Design questions resolved in this plan (not deferred)

1. **Naming:** the shared registration is `AddAuthAndTenancy` (`AuthTenancyExtensions.cs:44`); the
   opt-in parameter is added there and the one opt-in call site is `src/SchoolCollab.Auth/Program.cs:21`.
2. **Opt-in shape (revised):** one optional bool `requireOidcRelyingParty` (default `false`, so the
   five other call sites compile unchanged); the OIDC options block is factored into one private
   configurator shared by both branches; the flag-ON opt-in branch registers the **RP pipeline
   (cookie + OIDC)**, never the policy scheme / JwtBearer / portal-redirect handler, and never
   changes the default scheme.
3. **`post_logout_redirect_uri` target + key (revised for P1-1):** a new startup-validated
   `Auth:PostLogoutRedirectUri`, fanned **only** to the auth service; its value is the portal
   landing URI, made stable and realm-matchable by **pinning the portal's host port** (`5700`), so
   the fanned expression and the committed realm literal agree by construction.
4. **Hint capture (revised for P2-2):** `PortalSessionEntry` already carries `IdToken`;
   `PortalSessionStore.Revoke` returns it on the `PortalSessionRevocation` record and
   `KeycloakAuthProvider.LogoutAsync` consumes that record — no second store read, no
   ordering trick; the `ProviderLogout`/seam shape is unchanged.
5. **Portal surface:** behaviour is already option (ii) end-to-end (`app.py:792-835`); the only
   portal code edit is the stale option-(iii) doc-comment.
6. **Test-host reachability (P1-2):** the flag-ON posture is proven at the `ServiceCollection`
   level in Core (`AuthWiringTests`), because `AuthEndpointTestHost` pins the flag off and cannot
   compose the opt-in posture without a materially larger change.

### Deferred to the owner

- None. Both decisions (D13 = option (ii); dev posture = (B) as (b1)) are binding and implemented as
  specified. **Two items are explicitly NOT claimed:** (1) the live browser WebAuthn ceremony (K-D)
  — owner-gated, as recorded in the Goal; and (2) that the **effective portal URL is `5700` at
  runtime** — the realm guard checks the textual literal only, so the runtime equality is
  owner-gated too (re-read P2). (Back-channel logout, the `Settings.Api` `flag_admin` gate,
  and other providers remain out of scope as stated above — existing deferrals, not new questions.) (Back-channel logout, the `Settings.Api` `flag_admin` gate,
  and other providers remain out of scope as stated above — existing deferrals, not new questions.)

## Review

### PLAN REVIEW — iteration 1 (`a73f73c1`, `ollama-cloud/deepseek-v4.1-flash`, owner-override model)

    PLAN REVIEW
    Verdict: REWORK
    P1:
    - Pass 3, AppHost + realm rows: the realm must carry a CONCRETE portal landing URI, but the value the plan fans out is run-dependent — authPortal has no pinned host port (AddUvicornApp; no launchSettings), while school-collab-realm.json is a committed static import whose four app URIs are literals. The B5b precedent works only because that URI never needs to be in the realm. Keycloak therefore rejects the logout and no hermetic test can see it; K-E's "portal landing" assertion has no defined expected value. Fix: pin the portal's host port (or a wildcard) and state the literal in both rows so they agree by construction.
    - Pass 2, AuthEndpointTestHost row + K-D: the flag-ON posture K-D needs is unreachable from that host (it pins FEATURE:DisableOIDCAuth=false, composes with no opt-in, and lacks IDistributedCache) — the row is functionally empty and the flag-ON test has no host. Fix: restate K-D as a scheme-composition test in Core and drop the double-counted row, or make the row explicitly flag-ON + memory cache (materially larger). ALSO: registering only the OIDC scheme does not complete the ceremony — PasskeyEndpoints' /complete reads a Cookies-scheme ticket the OIDC handler writes via SignInScheme, so the callback fails closed without a cookie scheme.
    - Pass 3 closure rows / K-F: the plan claims the raw-body token scan "stays green", but two files assert the retired shape and will red-light — SessionEndpointTests:265 (client_id) and :281 (NotContain(IdToken) — the seeded id-token VALUE, which the hint now IS); KeycloakAuthProviderTests:292 (client_id) and :299 (NotContain(ClaimBearingIdToken)). Restate as value-based and name the inversions in the rows.
    - Pass 1 / K-B: the AC11 reword alone does not preserve the invariant it claims — AC9 (:416, "nor in Python state") and §9 (:247, "Tokens never reach the browser or Python state") are not in the rewrite, yet option (ii) puts the hint in a browser URL and in the portal's Python-held field. Amend both in the same change set.
    P2: §14 mis-cited (the dev-bypass clause is §2, :56-57; §14 has none); pass-3 ordering wording self-contradictory (Revoke produces the record — no "before"); "byte-identical" overstates what a scheme-level test can pin; record why a portal URL is legitimate in postLogoutRedirectUris beside the D16 negative; Scope IN omits test_session_routes.py and it is an EXTEND; the ordering probe passes for a capture-after-revoke implementation.
    Feasibility residuals: authority metadata fetch assumed lazy (confirm on first challenge); whether (b1) is meant to make the flag-ON passkey ceremony complete end-to-end (see the cookie-scheme P1).
    Acceptance-honesty notes: K-F/"scan stays green" (P1-3), K-C "byte-identical" (P2), K-D evidence route (P1-2), the ordering-probe sentence (P2), all overstated.

**Parent disposition (lean — the parent revises the plan):** all four P1s accepted and fixed in the
plan above (P1-1 portal port pin + literal agreement by construction; P1-2 opt-in = RP pipeline
(cookie + OIDC) + the pass-2 host rows dropped and the proof moved to Core; P1-3 the four inverted
assertions named and the scan restated as value-based; P1-4 AC9 + §9 added to pass 1). All P2s
accepted and applied. The feasibility residual on `(b1)`'s intent is resolved **inside** the settled
decision (the pipeline, not the scheme alone) and the live ceremony is explicitly excluded from the
round's claims. **Plan-review iterations: 1 of 1 used** — the skill's bound. The re-read (`54ff3aec`) returned
**REWORK with 1 completeness P1 + 5 P2s**, all applied **verbatim** by the parent under a recorded
**bound deviation**: the residual P1 was a completeness item (add D12 `:120` + its rationale cell to
the pass-1 rewrite list) with an exactly dictated fix, so it was applied without spending an
unsanctioned second iteration. The re-read also **confirmed, against the code**, that P1-1, P1-2 and
P1-3 are fixed (`5700` free in `Program.cs` and all eight `launchSettings.json`; `/complete` reads the
ticket scheme-explicitly at `PasskeyEndpoints.cs:164`, so `.AddCookie()` + OIDC under
TestAuth-as-default genuinely suffices and no `SignInScheme` override is needed; the four inversions
are exactly `:250`/`:261`/`:292`/`:299`, and `:78` stays green) and that dropping the two pass-2 rows
loses no flag-ON coverage (that host pins the flag off). **The plan therefore carries no open P1.**

### PASS 1 + PASS 2 — diff review (`1490d6b0`, `ollama-cloud/kimi-k2.7-code`)

    REVIEW
    Verdict: PASS
    P1: none
    P2: none
    Closure: yes — p2's 4 files == the pass-2 rows
    Best-practices: clean
    AC11 trace-ref judgment: immaterial — the traceability table covers only R1–R6; AC refs are inline parentheticals, and D12/D17 stay reachable via D13's rationale and AC11's own prose

Pass 1 (docs) was folded into this review rather than spending a separate run: its diff is two
documents and its content is fully visible, while pass 2 supplied the code judgments a reviewer
earns its cost on. Both patches were handed to the reviewer, so K-B was independently verified.

### PASS 3 (3a + 3b) — diff review (`aa6b6db1`, `ollama-cloud/kimi-k2.7-code`)

    REVIEW
    Verdict: P2-only
    P1: none
    P2: AuthServiceOptions.cs:14 — malformed XML doc comment introduced by the patch (`(<see cref="AppCallbackPrefixes"));` loses the closing `>` and gains a `)`); violates the repo's XML-doc must-do.
    P2: AppHostRealmImportArchitectureTests.cs:449 — the pinned-port regex is scoped to the text after the portal's creation call, so a resource pinned with `name: "http"` between that call and the portal's own pin could be matched instead. Non-blocking (no such call exists today).
    Closure: yes — p3a's 11 files + p3b's 6 files == pass 3's 17 rows exactly; `configuration.md` appears only in p3b as the split note requires
    Found⇒IdToken invariant: holds — `PortalSessionEntry.IdToken` is a non-nullable `string` and `Revoke` only returns `Found: true` when it atomically removes a live entry, so `revocation.IdToken` cannot be null at `KeycloakAuthProvider.cs:335`
    Config-fixture closure: complete — `AuthServiceOptionsTests.Valid()` / `Validator_AcceptsCompleteConfiguration`, `AuthServiceSmokeTests.OptionsValidator_AcceptsCompleteConfiguration`, `AuthEndpointTestHost`, `SessionEndpointTests.BuildProvider`; no other call site missed
    Best-practices: the XML-doc regression + the regex-scoping note; otherwise clean (no MediatR, no `Console.WriteLine`, no Moq/NSubstitute, immutable records, no inline `services.Add*()` in feature hosts)

**Both P2s fixed by the PARENT** (`diffs-keycloak-logout-oidc-p3-fix1.patch`, 2 files) rather than a
worker round-trip — both are comment/regex-level and were re-verified by the parent's authoritative
pass. One correction to the review's reasoning, recorded for accuracy: the malformed `<see cref>` does
**not** produce a CS1570/CS1584 warning — `Directory.Build.props` has doc-file generation **off**
(planned "on, with `TreatWarningsAsErrors`, once that debt is cleared"), and the build reports the same
13 pre-existing warnings with **zero** CS15xx. The defect was real (broken markup in a public type's
doc) and is fixed; the predicted symptom does not reproduce here. The regex was tightened to scope the
match to the portal's **own creation statement** (up to its terminating `;`), which removes the latent
coupling while keeping the guard fail-loud if the pin ever leaves that chain — extraction probe
re-confirms `5700`.

## Acceptance

### Verdict: CLOSED — at the static/unit level

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| K-A | D13 = option (ii): the `DELETE` returns the fully-built `end_session` URL carrying `id_token_hint` + `post_logout_redirect_uri`; the portal clears its cookie and 302s to it; the URL is opaque | **MET** | 3a — `KeycloakAuthProviderTests` (`:299` hint **equals** the custody id token, `:301` landing URI, `:307-309` encoding, `:315` no `client_id=`), `SessionEndpointTests` (`:262`/`:265`/`:267`, value scan `:272-277`, hint exactly once `:278`); 3b — portal `test_session_routes` byte-identical forward |
| K-B | Spec consistency: AC11 reworded; AC9 (`:428`), §9 (`:251-255`) and D12 (`:124` + rationale cell) carry the same carve-out; option (iii) recorded rejected; §2 (`:56-61`) dev-bypass clause amended | **MET** | 1 — reviewer PASS on the docs patch (folded); five clauses present with one identical carve-out sentence |
| K-C | (b1): with the flag ON the auth service registers the OIDC RP **pipeline** (cookie + OIDC) with `TestAuth` still default; other hosts scheme-for-scheme unchanged; §2 amended | **MET** | 2 — `AuthWiringTests`; probes A (opt-in removed → 1 failure) and B (cookie renamed `"Other"` → 1 failure, proving the scheme is pinned **by name**) |
| K-D | RP pipeline composes under flag-ON (the challenged scheme resolves; a cookie sign-in scheme exists). **Live ceremony explicitly NOT claimed** | **MET (static)** | 2 — `AuthWiringTests` scheme-provider assertion (`GetDefaultAuthenticateSchemeAsync`, the net10.0-correct substitution) |
| K-E | Realm: `school-collab-client.postLogoutRedirectUris` with the four `/signout-callback-oidc` URIs + the portal landing literal agreeing with the pinned port | **MET** | 3b — realm guard, Arch 63/0; probes: delete the block → presence assertion fails; literal `5701` → the **drift** assertion fails (not merely presence) |
| K-F | No-token invariant (value-based): no body carries an access/refresh token **value**; the only token-shaped browser-visible material is the hint inside the opaque URL | **MET** | 3a — `SessionEndpointTests` value scan + `KeycloakAuthProviderTests` negatives; the GET-body scan (now `:85`) legitimately stays green |
| K-G | Config/docs in the same change set as each code pass | **MET** | 1 (spec + §4 posture), 2 (§2/§4/§5 carve-out), 3b (§4 option-key row, §11 env-var row, pinned-port + realm notes) — the split conflict 3a escalated was adjudicated by assigning **all** `configuration.md` hunks to 3b |
| K-H | Build/tests inheritance; every new guard discriminating | **MET** | build **0 errors**; Auth **214/0**, Core **112/0**, Arch **63/0**, portal **123/0**; every guard has a recorded revert-probe with observed counts |

### Build / test numbers (parent-authoritative, final state)

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** (13 pre-existing warnings; no CS15xx) |
| `SchoolCollab.Auth.Tests.Unit` | **214 passed / 0 failed** (211 at base) |
| `SchoolCollab.Core.Tests.Unit` | **112 / 0** (110 at base) |
| `SchoolCollab.ArchitectureTests.Unit` (standalone) | **63 / 0** (62 at base) |
| `SchoolCollab.AuthPortal` pytest | **123 passed / 0 failed** |

Frozen artifacts: `diffs-keycloak-logout-oidc-p1.patch` (2 files), `-p2.patch` (4), `-p3a.patch` (11),
`-p3b.patch` (6), `-p3-fix1.patch` (2, the parent-applied P2 fixes). Base `670fff17`; branch
`stack/18-keycloak-logout-oidc`. The `.gitignore` hygiene edit is committed separately, not a criterion.

### P1 ledger — none outstanding

**Five P1s, all caught BEFORE dispatch and all closed in the plan** (never in a reviewed diff): the
plan review's four — the realm/liveness mismatch (`postLogoutRedirectUris` cannot equal a run-dependent
port), the unreachable flag-ON test host plus the **cookie-scheme gap** that would have produced a
pipeline that challenges correctly and then fails closed at `/signin-oidc`, the four assertions
encoding the retired shape, and the spec clauses (AC9/§9) still contradicting option (ii) — and the
re-read's completeness P1 (D12's row). **Zero P1 in any delivered diff**: pass 1+2 review PASS (none),
pass 3 review P2-only (none).

### Residual P2s

**None carried.** The two the final review raised were both fixed by the parent (`p3-fix1`). Two items
are recorded as documented invariants rather than defects: the `Found: true ⇒` non-null id token that
`KeycloakAuthProvider.cs:335` relies on (probe (b) shows a break fails **loud** with
`ArgumentNullException` at `:372`, never a hint-less URL), and the guard's statement-scoped regex
(tightened in `p3-fix1`; a pin moved off the portal's chain now fails the guard visibly).

### Static-verified vs LIVE / OWNER-GATED

This round's claims are **static and unit-test-verified only**. The owner must accept as open:

1. **The live WebAuthn passkey ceremony** — needs a real browser and authenticator. The round proves
the RP pipeline **composes** (K-D); it does not prove a human can authenticate in dev.
2. **That the portal's effective runtime URL is `5700`** — the realm guard proves the *textual* literal
and the pinned port agree; that Aspire actually binds the portal there at runtime is only observable
in a live AppHost run. (The toolkit behaviour was read from `Aspire.Hosting.Python` 13.5.4 sources —
`WithHttpEndpoint` updates an existing same-named endpoint in place — but reading is not running.)
3. **A live Keycloak logout round-trip** — that a real realm accepts the `post_logout_redirect_uri`
and terminates the SSO session end-to-end. The realm now registers the literal by construction, and
the URL is asserted in-host; the round-trip itself is hermetic-free and owner-gated.

### Process record

- **Passes:** 1 (docs) → 2 (RP opt-in) → **3 split by project boundary into 3a/3b** (11 + 6 files), because
  17 files across four projects exceeds a safe single 30-minute worker window (the ar-15 timeout lesson).
  The expected-files rows were unchanged; only the dispatch was split.
- **Plan review: 2 iterations** (the skill's bound) — 4 P1s + the re-read's 1 completeness P1, with the
  second iteration's residual P1 closed by **dictated fixes applied verbatim under a recorded bound deviation**.
- **Per-pass reviews:** 1 run covering passes 1+2 (the docs pass folded into it), 1 run covering 3a+3b.
- **Mid-pass escalations:** none — no worker timed out, hung, or blocked. Two supervisor escalations were
  *adjudications*, not failures: the `configuration.md` ownership conflict (got to 3b) and the worker's
  refusal to decide it alone.
- **Parent-applied fixes:** 2 (both P2, `p3-fix1`), each re-verified by the authoritative pass.
- **Correction recorded:** a reviewer's predicted symptom (a doc-comment build warning) does not
  reproduce, because doc-file generation is off in `Directory.Build.props`. The defect was real anyway.
- **UI trigger:** did not fire — the only portal change is a docstring; no `.razor`/`.css`/`.js`/`wwwroot`/
  ApiClient file is touched, so no UI tester was dispatched.

## UI Tester

*(placeholder — the UI-trigger criteria did not fire for this round: the portal surface touched is
one doc-comment with no behavioural change, so no UI-tester dispatch is warranted. If a later
addendum adds portal behaviour, reopen this section.)*
