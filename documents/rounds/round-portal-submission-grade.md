# Round — portal-submission-grade

Round `portal-submission-grade` · **Tier 3 LEAN (no-UI)** · 2026-10-05
Profile **ollama-cloud** · Models: orchestrator-plan `ollama-cloud/glm-5.3-flash` · plan-review + diff-review `ollama-cloud/kimi-k2.7-code` (Tier 3 lean: the plan gate runs the diff reviewer's model — never the worker's) · worker `ollama-cloud/deepseek-v4.1-flash`.
Base: `main` @ `b99ae46d`. **Clean tree at round start** (skill step 0; `git status` is the authority) — the round diff is tree-wide `git diff` and no parent-owned files sit inside the expected-files table.

Settled inputs: **T5** (`documents/specs/teachers-ward-portal-prefab-plan.md:49`): the spike boundary is **read-only review + submission review/grade**; **no** create/edit/publish in the portal. **D19 / round `portal-session-adoption`**: the portal signs in through the auth portal, holds only the opaque session cookie (D12, `school_collab_teacher_session`), presents `X-Portal-Session` on identity-bearing calls, and the API resolves the D9 claim set server-side (the `PortalSession` scheme/gateway + `RequireAssignmentReader`, `src/Assignments/SchoolCollab.Assignments.Api/AssignmentEndpoints.cs`). **T4**: the API is the security boundary; portal-side hiding is UX only. **Q3 (folded, accepted follow-up):** this round pins the auth-service session wire spelling (AC7) — the same unpinned-cross-context-wire class the previous round's P1 exposed. **Q5 (folded, recorded):** the ward-portal MVP go/no-go (portal plan Phases 1, 3–4) is **deferred with the named trigger "after the teacher write half lands"** — this round *is* that write half, so its closure re-opens that decision; the deferral sentence lives in the Goal section so it cannot drift.

No new AppHost parameter, no Keycloak change, no schema change, no custody change. `PORTAL_DEV_TEACHER_ID` stays orthogonal (D4, the gate's rule 2; never the session path).

## Plan

### Goal

T5's last unfinished piece: **the teacher portal's write half — submission review/grade.** A signed-in teacher drills down (assignment list → review queue → submission detail, all landed in prior rounds), then grades on the drill-down page itself: a Prefab `Form` whose `Fetch.post` calls a **new portal route**, which — behind D19's own gate and the auth portal's antiforgery mechanics, both mirrored, not reinvented — POSTs to the **existing** Assignments API grade route. The API's grade POST becomes reachable by a portal session for the first time: today it is pinned to the Bearer scheme at the group level, so a portal-session caller 401s by construction (the exact gap this round closes). No token, secret or refresh ever reaches Python (AC9/AC11 carry over verbatim); the grade body's `teacherId` is the session's claim data under real auth (where the API's claim-wins rule overrides it anyway) and the dev-fallback under the dev bypass.

**Recorded deferral (owner decision, from the portal plan's Q5):** the ward-portal MVP go/no-go (portal plan Phases 1, 3–4) is **deferred with the named trigger "after the teacher write half lands"** — the teacher write half is what this round adds, so from this round's acceptance the go/no-go decision is un-paused; the owner re-decides it, nothing in this round pre-commits Phases 1 or 3–4.

### Non-goals

- **No create/edit/publish** in the portal — T5's boundary stays closed (authoring remains the Blazor Admin destination, T7).
- **The ward portal stays untouched** — the ward route keeps today's header-less, session-less behaviour byte-for-byte (D19 rule: the ward surface never acquires a session header, and it does not acquire a grade form either).
- **No assignment-level writes** — `POST /{id}/review` (`AssignmentRoutes.cs:874`), `override-attempts` (`:1070`), `enable-submission` (`:964`), guardian routes (`:942`) and every create/duplicate/publish route stay exactly as they are, including their group-level policy. Only the **submission grade** route (`:1105-1145`) moves.
- **No custody, schema, Keycloak, AppHost-parameter or new-flag change** — no new `AddParameter`, no `appsettings.json` change, no migration, no new env fan-out (nothing new is fanned out: the grade POST reuses `services__assignments-api__http__0`, already resolved).
- **No new auth-service surface, no custody change** — the D18 read, D13 revocation and D2 claims route are consumed as-is; `PortalSessionStore`, `KeycloakAuthProvider` and the D15 seam are untouched.
- **No identity-threading rework** of `ReviewSubmissionRequest.TeacherId` ("Not yet identity-threaded, D-6 follow-up", `ContractTypes.cs:592-602`) — that handler-side refactor is explicitly out of scope; the claim-wins rule already covers the portal path, and the plan's contract tests work across BOTH today's spellings of the handler's attribution rule (this round adds no handler change).

### Seams (evidence the plan is grounded in — verified, not trusted)

| Seam | Where |
|---|---|
| The grade route today: `POST /assignments/{id}/students/{studentId}/submission/review` — resolves (assignment, student) → submission, 404 before dispatching | `src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs:1105-1145` (route comment :1105, `MapPost` :1106) |
| Grade request DTO: `ReviewSubmissionRequest(Guid TeacherId, decimal? Score, string? Grade, string? Comments)`; response = **204 NoContent** (the record's own doc: attribution "Not yet identity-threaded", D-6 follow-up) | `src/Assignments/SchoolCollab.Assignments.Contracts/ContractTypes.cs:592-602` |
| Handler authorization: claim-wins keyed on auth mode (claim overrides the wire field; real-auth missing claim → 403 `MissingTeacherPrincipalException`; dev honors the wire field), cross-tenant → 403, only-creating-teacher → 403 | `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/ReviewSubmission/ReviewSubmissionCommandHandler.cs:33-47` |
| Grade route is mounted in `MapAssignmentRoutes` under the plain `/assignments` group whose real-auth policy is **RequireAuthenticatedUser + Bearer** — a portal-session caller fails that scheme (Bearer `NoResult`s with no token) → **401 today, by construction** | `src/Assignments/SchoolCollab.Assignments.Api/AssignmentEndpoints.cs:16-25` |
| The reader policy precedent (`RequireAssignmentReader`, inline-built, gateway-scheme-named, flag-conditional) + the reader sub-group structure | `src/Assignments/SchoolCollab.Assignments.Api/AssignmentEndpoints.cs:28-44,75-103` |
| The reader routes map (the six GETs) sits beside `MapAssignmentRoutes` — the structural model the grade move mirrors | `src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs:60-100,228` |
| The D18 session read the portal's `SessionData` actually consumes: serialized camelCase (`sessionId`, `tenantId`, `tenantName`, `tenantType`, `teacherId`, `roles`, `expiresInSeconds`); error taxonomy 404 `session_not_found` / 410 `session_ended` | `src/SchoolCollab.Auth/Endpoints/SessionEndpoints.cs:33-101` |
| Today's tolerance the Q3 pin targets: `SessionData.from_payload` accepts **both** spellings (`sessionId` OR `session_id`, …) | `src/SchoolCollab.Portals/api/dto.py:63-96` |
| The portal's read-only client (GET-only `_get_json`; the public teacher reads take per-call `headers`) | `src/SchoolCollab.Portals/api/assignments_api_client.py` |
| D19's gate, in one place: `require_teacher_session` (rules 1–3: session path / today's dev path / target-keyed redirect), header threading via `TeacherSession.headers` | `src/SchoolCollab.Portals/app.py:335-499` |
| The submission drill-down the grade form lands on (`build_submission_view`; the existing GET route) | `src/SchoolCollab.Portals/views/teacher.py:311-370`; `src/SchoolCollab.Portals/app.py:659-705` |
| **The antiforgery mechanism to mirror**: one-time tokens in process memory (`AntiforgeryTokenStore`, TTL 600 s, ≤512 live), minted per render, `JSON-only body` guard + `antiforgery.consume` at the POST route, fetch-shaped failure answers (`{"redirect_uri": …}` at HTTP 200) chosen per path in the app exception handler | `src/SchoolCollab.AuthPortal/app.py:160-163,173-234,656-728,843-854,913-965` (`POST /login` decorator `:656`; `on_discovery_error` `:929f`) |
| The prefab form-to-fetch chain to mirror: `Form.from_model(..., fields_only=True)` + reactive `invalid` + `SetState(attempted)` → `Fetch.post` with the token in the body, `CallHandler(NAVIGATE_HANDLER, $result.redirect_uri)` on success, `ShowToast` on error | `src/SchoolCollab.AuthPortal/views/login.py:55-210` (`Form.from_model(..., fields_only=True)` at `:206`) |
| The two-sided contract-test pattern from the previous round's P1 fix (drive the **real** client/reader over the **serialized** body; both spellings pinned) | `tests/SchoolCollab.Assignments.Api.Tests.Unit/PortalSessionReaderPolicyTests.cs` (the contract pair), `tests/SchoolCollab.Auth.Tests.Unit/AuthEndpointTestHost.cs` |
| The route-test host pitfall (a policy-named scheme without its handler/deps is a 500; hosts here already register `AddPortalSessionAuthentication` + a stub `IPortalSessionClaimsReader` from the previous round) | `tests/SchoolCollab.Assignments.Api.Tests.Unit/AssignmentReaderPolicyRouteTests.cs:200-213`; `RealAuthRouteTests.cs:38-56` |
| The `.slnx` tripwire + guard (explicit file paths, no globbing; folder nodes only when a **new folder** appears) | `SchoolCollab.slnx:58-81`; `tests/SchoolCollab.ArchitectureTests.Unit/PortalsSolutionItemsArchitectureTests.cs` |

### The write-authorization decision — **introduce `RequireAssignmentWriter` (option b), mirroring the reader policy, holding exactly the grade POST**

The three candidate coverings, against the evidence:

1. **Rely on the existing group policy — rejected.** The grade POST is mounted in `MapAssignmentRoutes`, whose real-auth group policy pins **Bearer** (`RequireAuthenticatedUser().AddAuthenticationSchemes(BearerScheme)`, `AssignmentEndpoints.cs:16-25`). A portal-session caller presents `X-Portal-Session`, Bearer `NoResult`s, and the route 401s — the group policy is not only insufficient, it **forbids** the portal caller. Widening the whole group to the gateway would silently re-open every create/edit/publish write to the four reader roles — a scope and security regression, not just naming.
2. **Reuse `RequireAssignmentReader` — rejected on naming, not mechanics.** Mechanically it would work (the policy already names the gateway, which routes portal sessions, bearer callers and dev callers correctly). But the reader sub-group's contract is written and guarded as "exactly the **six GETs** … everything else stays reachable by a role-less principal" (`AssignmentRoutes.cs:60-67`); folding a write into a policy and route-map whose documented, reviewed meaning is *read* rots one spelling per concept — the exact discipline the previous round's reviewers enforced.
3. **Introduce `RequireAssignmentWriter` — chosen.** A new inline-built policy, byte-for-byte the reader's *pattern*: `AddAuthenticationSchemes(PortalSessionAuthenticationHandler.GatewaySchemeName)` + `RequireAuthenticatedUser()` + `RequireRole(...)` with **the same four-role disjunction as the reader** (`teacher ∨ staff ∨ user-admin ∨ platform-admin`, `RealmRoleNames.cs`) — deliberately **not** a narrower set, for two recorded reasons: (a) the **fine-grained** grade authority is already the handler's creating-teacher + cross-tenant checks (`ReviewSubmissionCommandHandler.cs:42-47`), so the policy's job is only the coarse T4 role gate; (b) today there is **no** role gate on the grade route at all (any authenticated principal passed the group check), so any narrower disjunction would 403 a Blazor caller that grades today — the mirror set is the strictly safest narrowing, and it keeps one role disjunction in the module instead of two that can drift.

**The registration/policy diff shape**, mirroring the reader sub-group exactly:

```csharp
// AssignmentEndpoints.cs — beside the reader sub-group:
var writerGroup = group.MapGroup(string.Empty);
if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
{
    // Same P1-2 pattern as the reader: the policy is flag-conditional; under
    // FEATURE:DisableOIDCAuth there is no policy group-wide, which today's
    // dev posture already is for every /assignments route.
    writerGroup.RequireAuthorization(RequireAssignmentWriter);
}
writerGroup.MapAssignmentGradeRoutes();
```

with `RequireAssignmentWriter(AuthorizationPolicyBuilder)` built **inline** (the reader's documented reason: test hosts compose `AddAuthAndTenancy` without `Program.cs`, and an unresolvable *named* policy throws out of `AuthorizationPolicy.Combine` — `AssignmentEndpoints.cs:75-103`):

```csharp
policy
    .AddAuthenticationSchemes(PortalSessionAuthenticationHandler.GatewaySchemeName)
    .RequireAuthenticatedUser()
    .RequireRole(
        RealmRoleNames.Teacher,
        RealmRoleNames.Staff,
        RealmRoleNames.UserAdmin,
        RealmRoleNames.PlatformAdmin);
```

**Fail-closed under every flag state, stated explicitly:**
- *Real auth, portal session:* the gateway resolves `PortalSession`; an unknown/expired session or absent header is a bare 401 (the handler's unchanged `NoResult`/bare-challenge contract — `portal-session-adoption` AC3 carries over). A live but role-less session → **403 policy refusal** (T4: the API is the boundary; no Python role list).
- *Real auth, Bearer caller (Blazor):* the gateway fallback resolves Bearer — unchanged (the policy change cannot regress existing callers; the scheme set is the same one `RequireAssignmentReader` already exercises).
- *`FEATURE:DisableOIDCAuth` ON:* no policy (the reader's P1-2 posture, repo-wide dev convention, unchanged) — and the handler still binds identity (`claim wins if present, else the wire field`), so the portal's dev-path grade honours `resolve_dev_teacher_id()` exactly as the review-queue read does today.
- *Gateway/handler outage:* unchanged fail-closed behaviour (registered in every flag state since round `portal-session-adoption`; no new scheme means **no** test-host setup additions this round — both existing route-test hosts already register the gateway + the stub claims reader).

**And the route move:** the grade POST's body (its resolution, its four catch arms, its 404-before-dispatch) moves **verbatim** from `MapAssignmentRoutes` into a new `MapAssignmentGradeRoutes(this RouteGroupBuilder)` beside `MapAssignmentReaderRoutes` (`AssignmentRoutes.cs`). Nothing else moves.

### Portal shape — read-then-grade, mirroring the auth portal's one POST route

**The portal route is `POST /teacher/assignments/{assignment_id}/students/{student_id}/review`** — the form's own route, exactly the login form's role (`Fetch.post` targets the portal's own URL, never the API directly and never the auth service). Its contract:

1. **Session gating:** the route resolves the gate itself (the same `require_teacher_session` outcome logic). On any gate refusal the POST answers **in the fetch's shape** — `{"redirect_uri": <the drill-down URL>}` at HTTP 200 (`on_teacher_session_outcome`'s existing handler gains the per-path/method branch the auth portal's `on_discovery_error` handler established, `src/SchoolCollab.AuthPortal/app.py:929f`): the drill-down GET re-gates and renders the truth (dead → cookie cleared + AC10 card; degraded → card, cookie kept; a dead-session redirect must never be the POST's own work — the GET is the one place the cookie lifecycle runs).
2. **Fetch-shaped refusals, bounded codes:** every upstream or refusal outcome is a **bounded code** from a new table in `views/teacher.py` (a `GRADE_COPY` beside `SESSION_COPY` — never an upstream body, never a token, never attacker-supplied text rendered): the API's 404 → e.g. `grade_submission_missing`; its 403 (non-creating-teacher / missing claim / tenant mismatch) → `grade_refused`; transport/5xx → `grade_unavailable`. The drill-down GET gains an optional bounded `error_code` query parameter and renders it as the auth portal's `_notice` alert (mirroring `views/login.py`'s error-card-above-form shape); failures redirect back to `…?error_code=<code>` via the fetch answer, mirroring `_login_failure` (`src/SchoolCollab.AuthPortal/app.py:843-854`).
3. **Antiforgery, mirrored mechanically, not reinvented:** the portals' `PortalState` (`app.py:137`) gains an antiforgery store — the auth portal's **`AntiforgeryTokenStore` transplanted as-is** (one-time issuance at render, single-use `consume`, TTL 600 s, ≤512 live tokens, process-local memory, `spec §14`). `build_submission_view` mints/renders the token into the form body; the POST route checks **JSON-only body** (the auth portal's `_is_json_request` — a cross-site page cannot POST `application/json` without preflight) and **consumes one token** before anything else; a missing/spent token is a refusal (bounded code; **the API is never called**).
4. **The read-only-then-grade flow:** the grade form exists **only** on the drill-down reached via GET (no standalone grade route, no GET grade form); success answers `{"redirect_uri": <drill-down URL>}` so the fetch navigates back and the page re-reads the submission — which now shows `Graded` — mirroring the login chain's `CallHandler(NAVIGATE_HANDLER, …$result.redirect_uri…)` + `ShowToast`-on-error (`src/SchoolCollab.AuthPortal/views/login.py:161-198`).
5. **`X-Portal-Session` on the grade POST (D19 rule 1):** the route passes `session.headers` to the new client method; rule-2/dev grades pass **no** header (today's behaviour) and honour `resolve_dev_teacher_id()` with its intact `MissingConfigurationError` refusal; rule-3 fires exactly as for reads. A session whose claim read carries **no** usable `teacherId` refuses the grade pre-flight (bounded code, no upstream call) — the same posture the review-queue read's refusal established.
6. **No token in Python (AC9/AC11):** the grade request carries only the opaque session id in the header; the body's `teacherId` is the **claim-set data** (a GUID string), not a credential; the client's token-shape scan discipline covers the decoded grade failure bodies (the scanner is shared, its placement — `api/errors.py` hoist or local reuse — the worker states in the report).

### Deliverables

**D1 — the C# write policy + route move** (`AssignmentEndpoints.cs`, `AssignmentRoutes.cs`): `RequireAssignmentWriter` (inline, the reader's pattern, the same four-role disjunction) + the writer sub-group (flag-conditional, P1-2 pattern) + `MapAssignmentGradeRoutes` receiving the grade POST **verbatim** from `MapAssignmentRoutes`. No other route, policy, or file changes.

**D2 — the client's POST half** (`src/SchoolCollab.Portals/api/assignments_api_client.py`): `review_submission(assignment_id, student_id, *, teacher_id, score, grade, comments, headers)` — one method, the client template's shape. A `_post_json` helper mirroring `_get_json`'s fail-closed discipline (typed transport → `ApiUnavailableError`; content-type guard; decode-then-status-check so an `application/problem+json` 403 detail is mapped, never ingested raw; 2xx-with-204 → success; any decoded body token-scan-checked). Sends the **camelCase** body `{"teacherId": …, "score": …, "grade": …, "comments": …}` (ASP.NET's binding spelling). No new public DTO/result type (a 204 write returns the status) — `api/__init__.py` therefore **stays untouched**; recorded as a deliberate non-edit against the pattern's re-export rule.

**D3 — the portal route + antiforgery** (`src/SchoolCollab.Portals/app.py`): the antiforgery store on `PortalState`; `POST /teacher/assignments/{assignment_id}/students/{student_id}/review` with the JSON-only guard + token consume + gate resolution + client call + fetch-shaped success/refusal answers; the drill-down GET's bounded `error_code` parameter; the per-path/method branch in `on_teacher_session_outcome`.

**D4 — the grade form** (`src/SchoolCollab.Portals/views/teacher.py`): the grade `Card` inside `build_submission_view` — the login form's chain mirrored (`Form.from_model(GradeSubmissionModel, fields_only=True)` + reactive `invalid` + `SetState(attempted)` → `Fetch.post` to the portal's own route with the antiforgery token in the body — the chain of `views/login.py:161-206`), plus `GRADE_COPY` (bounded codes) and the portals' own navigate-handler JS (the auth portal's names are its own — one spelling per app; the portals module defines its own handler names). The component imports grow (`Form`, `Fetch`, `CallHandler`/`SetState`, `ShowToast`/toast, `Alert` family — exactly what the login chain uses). New parameters on `build_submission_view` are **defaulted** so the existing view tests stay green unmodified.

**D5 — docs.** `documents/specs/teachers-ward-portal-prefab-plan.md`: the status line + Phase 2 record (write half landed; T5's boundary — read-only review + submission review/grade — now complete; the Q5 trigger sentence referenced). No `configuration.md` change (no new env, no new flag — recorded).

### Acceptance criteria

| # | Criterion | Proving test |
|---|---|---|
| AC1 | The grade POST's authorization: a live portal-session principal with `teacher` (or any disjunction role) authorizes → 204; the same session without a disjunction role → **403**; an unknown/expired session or absent header → **bare 401**; a Bearer caller with a role → unchanged; under `FEATURE:DisableOIDCAuth` ON no policy applies (today's dev posture); the handler's creating-teacher/tenant/claim-wins checks are untouched | `AssignmentWriterPolicyRouteTests` (new, Assignments.Api.Tests.Unit — hosts already register the gateway + stub reader) |
| AC2 | Route-move parity: the grade route's body moved verbatim (same resolution, same catch arms, same 404-before-dispatch); the existing Assignments suites stay green **unmodified** (no scheme/host-setup changes needed — the policy names an already-registered gateway) | `AssignmentReaderPolicyRouteTests` + `RealAuthRouteTests` green unmodified |
| AC3 | Grade happy path: the form's fetch POST (valid antiforgery token, JSON body) presents `X-Portal-Session` **asserted at the mock transport** on the API POST; the wire body is exactly camelCase `teacherId/score/grade/comments` with the claim-data teacher id; 204 → fetch answer `{"redirect_uri": <drill-down>}` at HTTP 200 | `test_teacher_grade.py` |
| AC4 | Antiforgery refusal: a fetch POST without the JSON content-type, or with a missing/spent token → refused with a bounded code, **the API never called** (transport request count asserted); a consumed token cannot replay | `test_teacher_grade.py` |
| AC5 | Gate on the POST: dead session (410/404) → fetch-shaped `redirect_uri` answer, cookie only cleared by the subsequent GET, API POST never fired; degraded → same shape, cookie kept; session-less + `PORTALS_LOGIN_URL` → the gate's 302 (the fetch navigates to sign-in); session-less dev path → grade proceeds with `resolve_dev_teacher_id()` (refusal intact when unconfigured); a session claim read with no usable `teacherId` → pre-flight refusal, no upstream call | `test_teacher_grade.py` |
| AC6 | Grade failure mapping: the API's 404/403/transport → the fetch answer redirects to the drill-down with its **bounded** code; the drill-down renders the bounded alert; no upstream body or Problem detail text is echoed into a page | `test_teacher_grade.py` + `build_submission_view`'s `error_code` alert |
| AC7 | **The Q3 wire-spelling pin (two one-way pins meeting in the middle):** (a) C# — the auth service's real serialized D18 read body carries **exactly** the camelCase key set (`sessionId`, `tenantId`, `tenantName`, `tenantType`, `teacherId`, `roles`, `expiresInSeconds`) over `AuthEndpointTestHost`; (b) Python — the **real** `AuthApiClient.read_session` driven over a body carrying exactly those keys (plus the `.../claims` route's exact spelling) populates `SessionData` fully. A spelling drift on either side reddens its own test — the tolerance in `SessionData.from_payload` is now a pin, not an accident | `SessionWireSpellingTests` (new, Auth.Tests.Unit) + `test_teacher_session.py` (the camelCase-body pin test) |
| AC8 | No credential/token in Python on the write path: the grade request carries only the opaque session id in `X-Portal-Session`; the body's `teacherId` is claim data, never a token; a token-shaped key in any decoded grade-failure body is refused | `test_teacher_grade.py` (mirror of `test_teacher_session.py`'s AC11 tests) |
| AC9 | Guards: the `.slnx` tripwire green with exactly the one new pinned path and **no folder nodes** (the file lands under the existing `tests/` folder node — `PortalSolutionFolderNodes_MirrorThePackageLayout` unchanged, considered); the writer-policy guard assertion green (non-vacuous: it fails when the grade route is removed from the writer sub-group or the policy loses the gateway scheme); `BearerForwardingWiringArchitectureTests`, `AppHostStartupFlagWiringArchitectureTests`, `AppHostLoginUiFlagWiringArchitectureTests` and `CrossModuleWiringTests` green **unmodified** (no new client, no new flag/parameter) | `PortalsSolutionItemsArchitectureTests` (+1 path) + `PortalSessionAdoptionArchitectureTests` (+ writer-policy assertions) + the untouched suites |
| AC10 | `dotnet build SchoolCollab.slnx` 0 errors; affected suites (`Assignments.Api.Tests.Unit`, `Auth.Tests.Unit`, `ArchitectureTests.Unit`) + `uv run pytest` (portals) 0 failures, with the pre-existing tests unmodified (the only pre-existing test edits allowed: none) | worker pass + parent authoritative pass |

### Expected files

**The C# rows are required — the write-authorization decision is this round's central change**; they are not conditional. The table is exhaustive: a file outside it is scope creep — stop and report, don't improvise.

| File | Change |
|---|---|
| `src/Assignments/SchoolCollab.Assignments.Api/AssignmentEndpoints.cs` | + `RequireAssignmentWriter` (inline, reader pattern, four-role disjunction) + the flag-conditional writer sub-group + the `MapAssignmentGradeRoutes()` call (D1) |
| `src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs` | + `MapAssignmentGradeRoutes` — the grade POST moved verbatim out of `MapAssignmentRoutes`; both maps' doc comments updated (the "six GETs / everything else" contract line gains the writer split, D1) |
| `tests/SchoolCollab.Assignments.Api.Tests.Unit/AssignmentWriterPolicyRouteTests.cs` | **new** — AC1/AC2 (portal-session/bearer/403/401 matrix + verbatim-parity assertions on the moved route) |
| `tests/SchoolCollab.Auth.Tests.Unit/SessionWireSpellingTests.cs` | **new** — AC7(a): the exact camelCase key sets of the real serialized `SessionResponse` (D18 read) and `SessionClaimsResponse` (D2 claims) bodies through `AuthEndpointTestHost` |
| `tests/SchoolCollab.ArchitectureTests.Unit/PortalSessionAdoptionArchitectureTests.cs` | + writer-policy guard assertions: the grade route is mounted under the writer sub-group (not `MapAssignmentRoutes`) and `RequireAssignmentWriter` names the gateway scheme + the flag condition (D1's non-vacuous guard) |
| `src/SchoolCollab.Portals/api/assignments_api_client.py` | + `review_submission` + `_post_json` (D2; docstring's "template" list gains the first POST) |
| `src/SchoolCollab.Portals/app.py` | + antiforgery store on `PortalState`, the `POST …/review` route, the outcome-handler's per-path branch, the drill-down `error_code` parameter (D3) |
| `src/SchoolCollab.Portals/views/teacher.py` | + grade Card/Form/Fetch chain, `GRADE_COPY`, navigate-handler JS, component imports (D4) |
| `src/SchoolCollab.Portals/tests/test_teacher_grade.py` | **new** — AC3–AC6, AC8 |
| `src/SchoolCollab.Portals/tests/test_teacher_session.py` | + the AC7(b) pin test — the real client over the pinned camelCase body (listed as the edit target because the pin belongs beside the existing session-parse tests) |
| `SchoolCollab.slnx` | +1 `<File Path="src/SchoolCollab.Portals/tests/test_teacher_grade.py" />` under the existing tests folder node |
| `tests/SchoolCollab.ArchitectureTests.Unit/PortalsSolutionItemsArchitectureTests.cs` | +1 pinned path (no folder nodes) |
| `documents/specs/teachers-ward-portal-prefab-plan.md` | status line + Phase 2 record: the write half landed; the Q5 deferral trigger sentence referenced (D5) |

**Explicit non-rows (recorded so their absence is a decision, not an omission):** `src/SchoolCollab.Portals/api/__init__.py` (no new public re-exportable type — D2), `src/SchoolCollab.Portals/api/dto.py` (the grade request has no response DTO), `src/SchoolCollab.Portals/api/errors.py` (unless the token-scan sharing hoists there — an in-plan deviation the worker reports, not a silent addition), any `Documents/configuration.md` (no new env), `src/AppHost/…` (no new parameter/fan-out), and no assignment-level write route or keycloak/realm file.

The `.slnx` tripwire applies to the one new `.py` file (`tests/test_teacher_grade.py`). No other new file of any kind is in the plan.

### Worker task spec

**Contract.** Implement exactly this plan — the `## Plan` section above is the single source of truth. The expected-files table is exhaustive. Read `.github/copilot/rules/dotnet-best-practices.md` (+ its skill) before any C# edit; `documents/solution/portals-service-client-pattern.md` before any Python edit. `.github/copilot/rules/ef-migrations.md` does not apply (no migrations, no schema change). Never edit this round doc or `documents/specs/keycloak-ui-auth-integration.md`. Never commit.

**Build/test discipline.** Run `dotnet build SchoolCollab.slnx` from the repo root after every code change and fix compiler errors before continuing (MSB3021/MSB3027 ⇒ stop the holding process and surface it, don't loop). Run the affected suites:

- `dotnet test tests/SchoolCollab.Assignments.Api.Tests.Unit`
- `dotnet test tests/SchoolCollab.Auth.Tests.Unit`
- `dotnet test tests/SchoolCollab.ArchitectureTests.Unit`
- portal suite: `cd src/SchoolCollab.Portals && uv run pytest -q`

**Output rule.** For each `dotnet test tests/<X>` run, paste exactly one filtered result line:

```
dotnet test tests/<X> 2>&1 | grep -E "^\s*failed |total:|failed:" | head -40
```

— one such line per result, no ad-hoc pipelines, no re-runs hidden from the report. Report the `uv run pytest -q` tail (`N passed`) the same way. The parent is the only build/test authority — your numbers are informational.

**Report shape.** End with a structured WORKER REPORT: what was implemented per deliverable (D1–D5), the exact command outputs above, every deviation from the expected-files table (there should be none — including the token-scanner placement recorded in Portal-shape item 6), and any surprise discovered in the seams. If a plan assumption proves false at implementation time, stop that thread and report it — do not silently redesign.

### Reviewer task spec

**Static only — never build, never test; discard any build/test numbers you find yourself producing.** Two passes, both on `ollama-cloud/kimi-k2.7-code`:

1. **Plan-review pass (gate — runs BEFORE the worker is dispatched).** Judge this plan against the seams it cites: feasibility (do the cited files/lines say what the plan claims — especially the grade route's current Bearer pinning and the auth portal's antiforgery/store mechanics?), scope honesty (does the expected-files table cover exactly the plan — and are the explicit non-rows really safe?), security posture (fail-closed under every flag state: the writer policy's scheme/role set; the gate-on-POST shape; the antiforgery consume-before-anything ordering; `teacherId` as claim data; no token in Python; no upstream body echoed into a page), and acceptance honesty (can each AC's named test actually discriminate the behaviour it claims — in particular AC7's two one-way pins from both sides?). Verdict: `P1-blocked` or `no-P1`, with findings numbered.
2. **Diff-review pass.** Verify the worker's diff against the plan + the best-practices rules. Priority targets: the moved route's verbatim parity (a rewrite smuggled into the move, AC2 is the tripwire); the writer policy's scheme/roles vs the reader's (drift between the two disjunctions); the POST route's refusal ordering (JSON guard + antiforgery consume **before** any upstream call); the gate-on-POST fetch shape (an HTML card leaking into a fetch answer); the drill-down `error_code` boundedness (no raw upstream text); the camelCase grade body wire keys; the `.slnx` pair (one pinned path, no folder nodes); spelling discipline (one `X-Portal-Session` literal per project). Return the REVIEW block: per-seam verdicts, P1/P2 classification, and "round can be accepted: yes/no".

### Tier / tier-re-eval note

Assumed **Tier 3 LEAN**: the round adds Python Prefab view code (the grade form mirroring the auth portal's login chain) but the deterministic UI trigger is the `*.razor`/`*.css` file set, which does not fire on a `.py`-only view change — mirroring the previous round's lean ruling; the grade form's behaviour is proven at the view/route level by pytest. The plan-review pass is **not** skipped (every Tier 3 round has it); the parent transcribes acceptance on the reviewer's verdict + its own authoritative pass, and no UI tester is dispatched. The **parent re-evaluates the tier from the worker's diff**: if the diff turns out to touch interactive UI beyond the mirrored form (or any file outside the expected-files table), bump to a full round per the skill's mid-round escalation rule.

## Worker Report

**Worker `d7a5a22b` (`ollama-cloud/deepseek-v4.1-flash`) — D1–D5 done, no deviations from the expected-files table.**

| Deliverable | Result |
|---|---|
| D1 | `RequireAssignmentWriter` inline (gateway scheme + authenticated + the reader's exact four-role disjunction) + the flag-conditional writer sub-group + `MapAssignmentGradeRoutes()`; the grade POST **moved verbatim** out of `MapAssignmentRoutes`. The write path works because authorization middleware **combines** the parent group's `{Bearer}` with the writer sub-group's `{PortalSessionGateway}` — the union authenticates a session caller through the gateway and an existing Bearer caller through the pinned fallback; the three group-level Bearer opt-ins are unchanged |
| D2 | `review_submission` + `_post_json` (camelCase body, token-scan **before** the status is judged, 204-by-status success); no new public DTO → `api/__init__.py` untouched |
| D3 | `AntiforgeryTokenStore` on `PortalState`; `POST /teacher/assignments/{id}/students/{sid}/review`; the per-**route-template** branch in `on_teacher_session_outcome`; `error_code` on the drill-down; every refusal (incl. the gate's rule-3 302, translated) answered in the fetch shape |
| D4 | `GradeSubmissionModel` + the grade Card/`Form`/`Field`/`invalid` → `Fetch.post` → navigate chain, its own handler JS, `GRADE_COPY` (10 bounded codes), defaulted new params so existing view tests stay green |
| D5 | Portal-plan Status + Phase 2 record (write half landed, T5 complete, Q5 trigger fired); no `configuration.md` change |

**In-plan placement notes (recorded, not silent):** the token scanner is reused by importing `_token_shaped_keys`; a new bounded `grade_score_invalid` coerces the Prefab form's text score to a JSON number; `on_discovery_error` also answers the grade POST in the fetch shape; the form's reactive `invalid` fires on "attempted and all three blank" (all fields optional); `GRADE_COPY` carries the 10 codes.

**Watch items respected:** the POST is matched by the **matched route's parameterized template** (`request.scope["route"].path`, never a raw path); `AssignmentWriterPolicyRouteTests.APortalSession_AuthorizesTheGradePost_ThroughTheGateway` drives the **real HTTP pipeline** with only `X-Portal-Session` and asserts **204**; AC7(a) is labelled a contract pin in the class/method docstrings.

**Worker-reported surprises:** (i) the drill-down GET is itself gated, so in the dead-session states a token cannot be scraped from a render — the tests issue one from the process store for exactly those cases (documented on the helper); (ii) the token scan runs before the status judgement, so even a 2xx token-bearing answer is refused (`grade_response_refused`) — intended and asserted.

**Worker's numbers (informational):** build 0 errors; Core 125/0; Auth 224/0; Assignments 158/0; Architecture 93/0; portal pytest 84 passed.

## Review

**Plan-review gate (step 2b — `ollama-cloud/kimi-k2.7-code`, run `a3a8b5ff`): PASS — no P1. "Worker may be dispatched on this plan: yes."** Every load-bearing citation verified against the tree (`AssignmentRoutes.cs:1106`; `ContractTypes.cs:592-602` incl. the "Not yet identity-threaded" note; `ReviewSubmissionCommandHandler.cs:33-47`; the parent group's Bearer pin `AssignmentEndpoints.cs:16-25`; the reader precedent `:28-44,75-103`; `dto.py:63-96`; `AntiforgeryTokenStore` `app.py:173-234`; `views/login.py:161-206`; `SessionEndpoints.cs:33-101`).

The central decision was judged **sound**: `RequireAssignmentWriter` as a gateway-scheme, inline, four-role mirror — portal sessions authenticate through the gateway while the parent group's Bearer requirement still covers existing Blazor callers via the gateway's fallback; the reader sub-group's six GETs are untouched; the role disjunction matches the handler's own creating-teacher/tenant/claim-wins checks; path and method are unchanged, so only endpoint metadata moves. Security posture judged fail-closed in every flag state; T4/T5/D19/D4 and the previous round's patterns respected; ACs discriminating **except AC7(a)**, which is a drift-detection pin rather than a pre-change failing test (AC7(b) carries the pre-change discriminator).

| P2 (report-only) | Disposition |
|---|---|
| AC7(a) is a contract pin, not a pre-change failing test | Accepted as labelled — its value is drift detection |
| The parameterized grade POST path must be matched carefully in `on_teacher_session_outcome`'s new branch | **Diff-review watch item** |
| The writer sub-group inherits the parent's Bearer scheme — confirm host tests still see portal sessions authorize through the gateway | **Diff-review watch item** |

Record-keeping note: the reviewer's scope line reports an "11-row expected-files table"; the table has **13** rows (verified). Its scope verdict (honest, exhaustive) stands — the row count is a miscount, and the plan doc's own count is correct.

**Orchestrator-seat note (relevant to the standing model question):** this is the **first orchestrator-authored plan in the repo corpus to pass its gate with zero P1s on the first pass** — the previous orchestrator-authored plans drew P1 1–3 (`assignment-policy-core`, whose gate also ran on the worker's model under a recorded deviation), 5 P1 lines (`keycloak-ui-auth-b`) and P1 ×5 + P2 ×8 (`portal-session-adoption`, two revisions). The run also self-corrected citation drift on a line-numbered re-read, which is the seat's characteristic failure mode. Per the proposed (not adopted) promotion rule, the trigger did **not** fire: no ladder change.

**Diff-review (step 4b — `ollama-cloud/kimi-k2.7-code`, run `6ee50854`): P2-only — "Round can be accepted: yes."** Verified: the **pure-move claim holds** (no rewrite smuggled into the move); watch item (a) the parameterized route-template match **holds**; watch item (b) the portal-session-through-gateway proof **holds**; the mutation security posture is **sound**; scope **clean at 13/13**; AC7's two one-sided pins are **genuinely two-sided**. No P1.

| P2 | Disposition |
|---|---|
| `api/assignments_api_client.py:45` imports the auth client's underscore-private `_token_shaped_keys` — couples two clients through a private helper, against the pattern doc's per-client encapsulation | **FIXED pre-commit** — the key set + scanner hoisted into the shared `api/errors.py` (beside the `TokenInResponseError` they raise) as public `TOKEN_SHAPED_KEYS` / `token_shaped_keys()`; both clients import it from there; `git grep _token_shaped_keys -- src/SchoolCollab.Portals` is now **empty** |
| `tests/SchoolCollab.Auth.Tests.Unit/SessionWireSpellingTests.cs:12` docstring typo (`portal-session-grade` → `portal-submission-grade`) | **FIXED pre-commit** — cosmetic, comment-only |

**Parent's authoritative pass (the round's source of truth):** build **0 errors** · Core **125/0** · Auth **224/0** · Assignments.Api **158/0** · Architecture **93/0** · portal pytest **84 passed** (was 61: +22 grade tests and +1 wire pin) in 3.55s.

## Acceptance

**Verdict: CLOSED — round can be accepted.** Transcribed by the parent per the Tier 3 lean rule, on the plan-gate verdict, the diff-review verdict, and the parent's own authoritative pass.

| # | Criterion | Result |
|---|---|---|
| AC1 | grade-POST authorization matrix | **met** — `AssignmentWriterPolicyRouteTests` drives the real pipeline: session + disjunction role → **204**, role-less → **403**, unknown session → **bare 401**, Bearer caller unchanged, flag-ON dev posture unchanged |
| AC2 | move parity | **met** — the diff-review confirmed behaviour-identical movement; `AssignmentReaderPolicyRouteTests`/`RealAuthRouteTests` green **unmodified** |
| AC3 | grade happy path | **met** — `X-Portal-Session` asserted at the mock transport; camelCase `teacherId/score/grade/comments`; 204 → `{"redirect_uri": <drill-down>}` at HTTP 200 |
| AC4 | antiforgery refusal | **met** — non-JSON / missing / spent token refused with a bounded code and **the API never called** (transport count asserted); consume-before-anything ordering verified statically |
| AC5 | gate on the POST | **met** — dead session → fetch-shaped answer, API never fired, cookie cleared only by the following GET; degraded → same shape, cookie kept; session-less + `PORTALS_LOGIN_URL` → the gate's 302 translated for a fetch; dev path intact with the refusal preserved |
| AC6 | failure mapping | **met** — bounded `GRADE_COPY` codes only; no upstream body or Problem detail echoed into a page |
| AC7 | the two-sided wire pin | **met** — `SessionWireSpellingTests` (C#) + the Python pin beside the session-parse tests; judged genuinely two-sided; the `SessionData.from_payload` tolerance is now a pin, not an accident |
| AC8 | no token in Python | **met** — opaque id only on the wire; `teacherId` is claim data; a token-shaped key in any decoded body is refused (scan before status) |
| AC9 | guards | **met** — `.slnx` tripwire +1 pinned path, no folder nodes; the writer-policy assertion non-vacuous; the four untouched architecture suites green |
| AC10 | build + suites | **met** — see the parent's pass above; the pre-existing tests are unmodified (no pre-existing test edits) |

**Scope:** the re-frozen patch (post-P2-fix) is **2,892 lines / 15 file headers** — the plan's 13 rows **plus the two hoist files** (`api/errors.py`, `api/auth_api_client.py`), which is exactly the blessed post-review deviation the plan anticipated: its non-row clause read "`api/errors.py` (unless the token-scan sharing hoists there — an in-plan deviation the worker reports, not a silent addition)". Every other recorded non-row (`api/__init__.py`, `dto.py`, AppHost, `configuration.md`) is untouched. The pre-fix freeze was 2,774 lines / 13 headers.

**Review verdicts:** plan gate **PASS** (0 P1, `a3a8b5ff`); diff review **P2-only** (0 P1, `6ee50854`). The round drew **no P1 at either gate** — the cleanest in the repo corpus, and the second consecutive datapoint for the orchestrator seat's citation discipline (this plan self-verified its citations before submitting).

**Residuals:** both diff-review P2s were **fixed pre-commit** (see the Review table). Post-fix verification (parent scope-check — the skill's allowance for a tiny rework diff; no second reviewer pass was bought, recorded honestly rather than implied): `dotnet build SchoolCollab.slnx` **0 errors**; portal pytest **84 passed** (unchanged — the AC11 scans still fire, now through the hoisted helper); `git grep _token_shaped_keys -- src/SchoolCollab.Portals` is **empty**, so the coupling the reviewer flagged is mechanically gone rather than argued away.

**Observation (out of scope, candidate follow-up):** the **auth portal** carries the same smell internally — `src/SchoolCollab.AuthPortal/api/admin_api_client.py:48` imports `_token_shaped_keys` from its sibling `auth_api_client`. Pre-existing, untouched here, and fixable the same way if the two portals' `api/` conventions are ever unified.

**Tier:** 3 **lean** — the UI trigger did not fire (`.py`-only view change), so no UI tester was dispatched. **Split-review rule (adopted this session):** the patch is under the ~3k threshold, so a single-pass diff review was correct — the split fires above ~3k or after any timeout.