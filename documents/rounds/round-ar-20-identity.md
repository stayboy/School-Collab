Provider: pi ollama — Tier 3 LEAN (no UI trigger; API-first auth, no login form this round): orchestrator glm-5.3-flash, worker deepseek-v4-flash-0731, reviewer deepseek-v4.1-flash, no UI tester. Base 11cc37e1 (main, clean).

## Plan

### Goal

Make authentication work end-to-end and finish D-6's identity wiring:

1. Ship a Keycloak dev container (realm `school-collab`, direct-access-grants client, seeded dev user, tenant/teacher protocol mappers) in the AppHost, mirroring the existing `mailpit` block.
2. Add real JWT bearer validation on the API hosts (today there is ZERO bearer validation) and finish the claim-mapping TODO at `src/SchoolCollab.Core/Auth/AuthTenancyExtensions.cs:78` so `tenant_id`/`tenant_name`/`tenant_type`/`teacher_id` surface identically from real Keycloak (OIDC + JWT) and TestAuth.
3. Introduce `ICurrentUser` (net-new, `SchoolCollab.Core/Auth`) resolving teacher id + tenant from the `ClaimsPrincipal` across OIDC cookie, bearer JWT, and TestAuth, and thread it into every attribution site so write attribution becomes server-authoritative instead of request-field based.
4. Seed a dev Teacher row (fixed well-known Guid) so a real Keycloak dev login resolves to a REAL teacher id, and document the whole real-path flow (how to get a JWT with username/password) in `documents/configuration.md` §4.

### Scope

**In:**
- AppHost: Keycloak container + realm import file + secret parameter + `Auth:Keycloak:*` env injection.
- CPM: `Microsoft.AspNetCore.Authentication.JwtBearer` version entry + versionless PackageReference in `SchoolCollab.Core`.
- `AuthTenancyExtensions`: `AddJwtBearer` in the OIDC branch (real-auth mode only) + finish the ClaimActions TODO.
- Net-new `ICurrentUser`/`CurrentUser` in `SchoolCollab.Core/Auth` + registration in `AddAuthAndTenancy`.
- Attribution threading in the four Assignments command/handler sites + doc-comment deprecation of the wire-contract `TeacherId`/`ApproverId` fields (owner call: **keep-but-ignore + deprecate** — NO contract-shape change this round).
- Dev teacher seeding in the MigrationService.
- `documents/configuration.md` §2 (new parameter rows), §4 (full rewrite of the real Keycloak flow incl. the ROPC caveat), §5 (flag rows unchanged posture, confirmed).

**Out (explicit):**
- **NO `.razor` / `.css` / `.js` files may be modified.** The Blazor Admin keeps its existing OIDC code flow untouched. If the worker concludes a UI file is unavoidable, STOP and report as an owner question.
- **NO contract-shape change**: `ApproveAssignmentRequest(Guid ApproverId)`, `ReviewAssignmentRequest(Guid TeacherId, …)`, `ReviewSubmissionRequest(Guid TeacherId, …)`, `OverrideStudentSubmissionAttemptsRequest(Guid TeacherId)` keep their current wire shape (field stays, doc-comment marked server-ignored-when-principal-present). No `sub` column, no migration, no schema change.
- No production teacher provisioning (how real users' teacher attributes get maintained is a D-6 follow-up), no Families host bearer, no UI login form, no Playwright changes.
- `AssignmentSummaryDto.AiPromptOverride` residual stays a documented D-6 follow-up — do not touch it this round.

### Key decisions (pinned — do not re-litigate)

| # | Decision | Why |
|---|---|---|
| 1 | **JwtBearer package/version**: add `<PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.9" />` to `Directory.Packages.props` (matches the existing `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.9 — same 10.0.x standalone-package line; `Cookies` stays at 2.3.11, it has its own version line) + versionless `<PackageReference>` in `src/SchoolCollab.Core/SchoolCollab.Core.csproj` (where `AuthTenancyExtensions` lives — same file that already PackageReferences OpenIdConnect/Cookies). CPM rule: versions ONLY in `Directory.Packages.props`. | One family version; Core is the only consumer. |
| 2 | **Scheme coexistence**: register `AddJwtBearer` with scheme name `"Bearer"` (const `AuthTenancyExtensions.BearerScheme`) **only in the OIDC branch** of `AddAuthAndTenancy` (i.e. when `FEATURE:DisableOIDCAuth` is false). Keep `DefaultScheme = Cookie`, `DefaultChallengeScheme = OIDC` (Admin/Families browser flows unaffected — they never reference the Bearer scheme). API endpoint groups select the scheme per the existing flag-conditional grouping posture: in the `!DisableOIDCAuth` branch use `RequireAuthorization(policy => policy.RequireAuthenticatedUser().AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme))`; in the TestAuth branch the existing default-scheme `RequireAuthorization()` stays. The `[Authorize]` posture per endpoint group remains conditional on `IFeatureFlagService` exactly as today (`AssignmentEndpoints.cs:15`). | One registration point serves all hosts; dev/CI stays container-free. |
| 3 | **Settings.Api and Students.Api DO get bearer this round** — for free: both (like Assignments.Api and Admin/Families) already call `AddAuthAndTenancy`, so the shared extension is the only change. Only Assignments endpoint *groups* wire the Bearer scheme into their authorization posture this round; Settings/Students endpoint-group wiring is a cheap follow-up (their authorization posture is currently minimal) — noted as residual, not blocked. | Avoids a per-host fork of auth registration. |
| 4 | **Dev teacher seeding**: a dev-only `DevIdentitySeeder` in `src/SchoolCollab.MigrationService/Seeding/` (registered + invoked in `MigrationService/Program.cs` alongside `TenantSeeder`), which: (a) seeds a **fixed-Guid dev tenant** `Dev School` = `Guid.Parse("00000000-0000-0000-0000-000000000002")` (the SystemTenantId `…0001` convention precedent, `TenantSeeder.cs:35`) and (b) seeds a `Teacher.Create` row **"Dev Teacher"** = `Guid.Parse("00000000-0000-0000-0000-000000000003")` in that tenant, both idempotent (skip if exists), both running only when the MigrationService runs (all envs in dev; harmless in CI). `Teacher.Create` requires title/first/last — pass the minimal valid set per `CreateTeacher` (`src/Students/SchoolCollab.Students.Core/CQRS/Teachers/Commands/CreateTeacher/CreateTeacher.cs`); `StaffNumber` auto-generates. The committed realm file's user attributes carry **exactly** these two Guids (`tenant_id`, `teacher_id`) so the mapper value matches the seeded rows. Random-Guid sample tenants (`Hydeson School` etc.) are untouched. | Realm import JSON is static, so claim values must be fixed Guids; seeding gives them real backing rows. |
| 5 | **"Keep-but-ignore + deprecate" per call site** — the rule everywhere is: **if the principal carries `teacher_id` → it wins; the request field is ignored. If no teacher claim (TestAuth/dev, existing integration tests) → the request field is still honored** (this is what keeps container-free CI and the current test suite green). | Server-authoritative without breaking dev/CI. |
| 6 | **Host wiring**: `Auth:Keycloak:Authority` reaches hosts via Aspire service discovery (`WithReference(keycloak)` + `WithEnvironment("Auth__Keycloak__Authority", <realm reference-expression>)`), NOT hardcoded URLs. `Auth:Keycloak:ClientId` / `ClientSecret` are fanned out via `WithEnvironment` from AppHost parameters. | AppHost = single source of truth (§2). |

### Expected files (≈17)

| File | Change |
|---|---|
| `Directory.Packages.props` | +JwtBearer 10.0.9 |
| `src/SchoolCollab.Core/SchoolCollab.Core.csproj` | +versionless JwtBearer PackageReference |
| `src/SchoolCollab.Core/Auth/AuthTenancyExtensions.cs` | `BearerScheme` const; `AddJwtBearer` in OIDC branch (Authority/Audience from `Auth:Keycloak:*`); **finish the :78 TODO** — `options.ClaimActions.MapJsonKey("tenant_id", "tenant_id")` (+ `tenant_name`, `tenant_type`, `teacher_id`); register `ICurrentUser` |
| `src/SchoolCollab.Core/Auth/ICurrentUser.cs` | net-new interface + XML docs |
| `src/SchoolCollab.Core/Auth/CurrentUser.cs` | net-new impl (`IHttpContextAccessor` + `ITenantProvider`): `Guid? TeacherId` (from `teacher_id` claim), tenant from `TenantProvider` post-transformation, `bool IsAuthenticated`; typed-error free, no `InvalidOperationException` |
| `src/SchoolCollab.Core/Auth/TestAuthHandler.cs` | add `TestAuthHandlerOptions.TeacherId` (default `Guid.Empty` ⇒ **no** `teacher_id` claim emitted — preserves today's posture); emits the claim when set (tests use this) |
| `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/CreateAssignmentCommand/CreateAssignmentCommandHandler.cs` | `createdByTeacherId:` ← `currentUser.TeacherId ?? Guid.Empty` (replaces the `Guid.Empty // TODO` at :56); when a teacher claim IS present, validate via `ITeacherDirectory` → unknown ⇒ typed exception |
| `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/ApproveAssignmentCommand/ApproveAssignmentCommandHandler.cs` | approver ← principal-wins rule (decision 5) |
| `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/RejectAssignmentCommand/RejectAssignmentCommandHandler.cs` | same principal-wins rule |
| `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/ReviewAssignmentCommand/ReviewAssignmentCommandHandler.cs` | same principal-wins rule (`assignment.AddReview`) |
| `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/OverrideStudentSubmissionAttempts/OverrideStudentSubmissionAttemptsCommandHandler.cs` | same principal-wins rule |
| `src/SchoolCollab.Assignments.Contracts/ContractTypes.cs` | doc-comment deprecation on the four `TeacherId`/`ApproverId` fields (~:224/:366/:413/:460/:526): "server-authoritative from ar-20; principal wins; kept on the wire for TestAuth/dev" — **no `[Obsolete]` attribute, no shape change** |
| `src/Assignments/SchoolCollab.Assignments.Core/Services/ITeacherDirectory.cs` + `src/Assignments/SchoolCollab.Assignments.Api/Services/TeacherDirectoryHttpClient.cs` | net-new port (`Task<bool> ExistsAsync(Guid teacherId, CancellationToken)`) over the existing `"students-api"` named client + `GET /teachers/{id:guid}` (route already exists, `TeacherRoutes.cs:61`); register in `Program.cs` |
| `src/AppHost/SchoolCollab.AppHost/Program.cs` | Keycloak container (mirror `mailpit` block, `Program.cs:38`): `AddContainer("keycloak", "quay.io/keycloak/keycloak")`, `start-dev --import-realm` args, HTTP endpoint, `.WithBindMount(… realm-school-collab.json …, "/opt/keycloak/data/import/")`, `KC_BOOTSTRAP_ADMIN_PASSWORD` from a new **secret** parameter `keycloak-admin-password` (`AddParameter(name, secret: true)`, mirror `smtp-password` — NO committed default, user-secrets `Parameters:keycloak-admin-password`); `WithEnvironment("Auth__Keycloak__*")` fan-out + `WithReference(keycloak)` onto `assignments-api`, `students-api`, `settings-api`, `admin` |
| `src/AppHost/SchoolCollab.AppHost/realm-school-collab.json` | committed realm import: realm `school-collab`; client `school-collab-client` with `directAccessGrantsEnabled: true` + standard flow enabled; dev user `dev-teacher` (dev-only password, **labelled dev-only in the file's top comment**); protocol mappers emitting `tenant_id=…0002`, `tenant_name="Dev School"`, `tenant_type="School"`, `teacher_id=…0003`. **Dev-only credentials — explicitly labelled; never reuse in production.** |
| `src/AppHost/SchoolCollab.AppHost/appsettings.json` | non-secret Parameters entries only (e.g. `keycloak-realm-name` if needed); the two secrets (admin password, client secret) get **no** committed default |
| `documents/configuration.md` | §2 +2 parameter rows; **§4 full rewrite** (real Keycloak flow, `curl` token request, ROPC-deprecated caveat: production remains code flow + PKCE, dev-flip instructions); §5 flag rows confirmed |

Tests (new files, all unit/scripted — no Keycloak in CI):
- `tests/SchoolCollab.Core.Tests.Unit/Auth/CurrentUserTests.cs` — ICurrentUser per scheme claim shape.
- `tests/SchoolCollab.Core.Tests.Unit/Auth/AuthTenancyClaimMappingTests.cs` — claim mapping surfaces `tenant_id`/`teacher_id`.
- `tests/SchoolCollab.Assignments.Tests.Unit/…` — attribution tests (handler-level, scripted).

### Acceptance criteria

1. `dotnet build SchoolCollab.sln` → **0 errors** (warnings reviewed; no new CA/CS noise from the new code).
2. `dotnet test` → **0 failures**; all existing suites keep their counts (no regression), PLUS the new discriminating tests:
   - **≥3 tests** `CurrentUserTests`: resolves teacher+tenant from (a) OIDC-cookie claim shape, (b) bearer-JWT claim shape, (c) TestAuth claim shape; proves no-`teacher_id` ⇒ `TeacherId == null` (never throws).
   - **≥1 test** proving the claim mapping surfaces `tenant_id`/`teacher_id` identically for the real and TestAuth shapes.
   - **≥1 test** proving `CreateAssignment` no longer writes `Guid.Empty` when the principal carries `teacher_id`.
   - **≥1 test** proving request-field `TeacherId` is **ignored** when a principal with `teacher_id` is present (e.g. `AddReview` stamps the principal's teacher).
   - **≥1 test** proving the TestAuth/dev fallback: no `teacher_id` claim ⇒ request field still honored (guards CI).
3. No `.razor`/`.css`/`.js` modified; no contract record shape changed (diff-only check).
4. Real Keycloak path verified **manually during implementation** (documented in the Worker Report): run AppHost → container healthy → set `FeatureFlags__FEATURE__DisableOIDCAuth=false` on `assignments-api` → `curl` password-grant token request against `/realms/school-collab/protocol/openid-connect/token` (username `dev-teacher`, direct access grant) → `GET /assignments` with `Authorization: Bearer …` → 200 with the `Dev School` tenant context, and `CreateAssignment` attributing to teacher `…0003`.
5. No secrets committed: realm file creds and any dev passwords are labelled dev-only; `keycloak-admin-password` has no committed default.
6. Repo rules: CPM respected; net10.0; no MediatR; XML docs on new public members; primary constructors; typed exceptions only; feature flags still centralized in AppHost `Parameters:`; endpoint grouping conditional on `IFeatureFlagService`; no direct cross-context project references (teacher existence check goes through the students-api HTTP port, like `IStudentDirectory`).

**Honest verification statement:** CI cannot run Keycloak (container-free TestAuth stays the CI path per owner decision b). The real-path verification is (4) above, done manually and recorded in the Worker Report; if the container path cannot be completed in the worker's environment, the residual is stated explicitly and CI-level confidence rests on the claim-shape unit tests.

### Reviewer acceptance criteria

- Diff-only static review: no UI files touched; no contract shape change; no version in any `.csproj` `PackageReference`; `<Sdk Version>` untouched; secrets not committed; no `InvalidOperationException`; no MediatR; no direct cross-context references; AppHost parameter fan-out matches §2 conventions (`WithEnvironment`, service discovery refs).
- Auth correctness: JwtBearer registered only in the OIDC branch; TestAuth dev posture unchanged (`FEATURE:DisableOIDCAuth=true` default in all host appsettings preserved); endpoint-group authorization still conditional on `IFeatureFlagService`; Admin/Families cookie flows untouched (DefaultScheme stays Cookie).
- Claim mapping: the :78 TODO is gone; ClaimActions cover all four claims; `TenantClaimsTransformation` posture unchanged.
- Attribution: principal-wins rule implemented at all five handler sites; TestAuth fallback intact; XML-doc deprecations present, no `[Obsolete]` compile break.
- Tests: the discriminating set exists and passes; per-suite counts reported in the Worker Report match the Reviewer's own `dotnet test` run.
- Worker Report includes the manual Keycloak verification result (or an explicit residual statement).

### Worker task spec (compact)

Implement exactly the expected-files table above. Sequence: (1) CPM + csproj; (2) `AuthTenancyExtensions` (BearerScheme, AddJwtBearer, ClaimActions TODO, `ICurrentUser` registration) + `ICurrentUser`/`CurrentUser` + TestAuth `TeacherId` option; (3) MigrationService `DevIdentitySeeder` (fixed Guids `…0002` tenant / `…0003` teacher, idempotent); (4) realm JSON + AppHost container/parameter/wiring; (5) `ITeacherDirectory` port + handler threading at all five sites (principal-wins rule, decision 5); (6) contract doc-comment deprecations; (7) configuration.md §2/§4/§5; (8) the discriminating tests; (9) `dotnet build` (0 errors) + `dotnet test` (0 failures); (10) the manual container verification of acceptance #4 (record result verbatim). Read `.github/copilot/rules/dotnet-best-practices.md` before the first `.cs` edit. STOP and report if any `.razor`/`.css`/`.js` edit seems needed, or if JwtBearer 10.0.9 does not restore (then pin the nearest 10.0.x that restores and note it). Fill `## Worker Report` with: files changed, build/test results per suite, manual-verification evidence, residuals.

### Reviewer task spec (compact)

Static, diff-only (`git diff 11cc37e1`). Verify against the Reviewer acceptance criteria checklist; run `dotnet build` + `dotnet test` independently; confirm per-suite counts; confirm no UI/contract/secrets violations; spot-check the five handler sites for the principal-wins rule and the TestAuth fallback. Write `## Review` with a verdict (accept / fix-first) and per-criterion findings.

### Risks / residuals

- **ROPC deprecation** (recorded caveat): direct access grants are deprecated in OAuth 2.1 — scoped to dev/simple-client; configuration.md §4 states production remains code flow + PKCE.
- **Fixed-Guid tension**: the dev tenant/teacher fixed Guids follow the `SystemTenantId` precedent but are a new convention — reviewer should confirm the dev-only labelling and that no production path can authenticate against them.
- **Keycloak container startup latency / import flakiness**: `start-dev --import-realm` import is one-shot on empty state; a persistent volume could skip re-import after realm edits — document `--import-realm` behavior + realm-change re-import step in the Worker Report notes.
- **Settings/Students endpoint-group bearer posture** not wired this round (decision 3) — residual follow-up.
- **Production teacher provisioning** (who sets `teacher_id` user attributes in real realms) — D-6 follow-up, out of scope.
- **`AssignmentSummaryDto.AiPromptOverride`** residual — documented D-6 follow-up, untouched.
- **JwtBearer version availability**: 10.0.9 planned to match OpenIdConnect; if unavailable, nearest 10.0.x with a note (worker instruction above).

### Plan amendments (post plan-review)

Steered to the worker mid-implementation (see `## Review` → PLAN REVIEW, 2026-09-18). These amend the expected-files table and the pinned decisions; the diff-review is judged against **this** amended plan.

| P1 | Amendment |
|---|---|
| 1 | Realm adds an **`oidc-audience-mapper`** (`included.client.audience: school-collab-client`) AND JwtBearer pins `ValidAudiences` (Keycloak puts `azp`, not `aud` → IDX10214 otherwise) |
| 2 | Mapper **types + token targets pinned**: `oidc-hardcoded-claim-mapper` (tenant_*) / `oidc-usermodel-attribute-mapper` (teacher_id), each with `access.token.claim` + `id.token.claim` (+ `userinfo.token.claim` for the ClaimActions path). ClaimActions cannot serve the bearer path |
| 3 | `RequireHttpsMetadata = false` **dev-only, commented**, on OIDC + JwtBearer; document minting the token via the same host form as `Auth:Keycloak:Authority` (IDX10205) |
| 4 | Dev seeder: fixed Ids written via raw SQL / `Entry().Property("Id")`, inside `RunWithExplicitTenantAsync`; existence check via `IgnoreQueryFilters(["Tenant"])`; `StaffNumber` passed explicitly (no auto-generation on a direct insert). Still **no new migration** |
| 5 | Service-to-service token gap: either add forwarding to the `students-api` client (new file — declare it) **or** restate AC#4 without the teacher-existence gate and record the gap as the round's headline residual |
| 6 | **Fallback keyed on auth mode, not claim absence**: real-auth mode with a missing/mismatched `teacher_id` → typed rejection (never honor the wire field); the wire-field fallback survives only in the TestAuth/dev branch |
| 7 | Existing positional handler-construction tests are **in-scope edits** (listed in the steer): the build is red without them |

P2 corrections applied in the same steer: the TestAuth branch has no `RequireAuthorization()` to "keep"; explicit scheme challenge on API groups (the worker's "302-to-Keycloak" note was later **refuted by the diff-review** — `AuthorizationMiddleware` uses the policy's `AuthenticationSchemes`, so bearer groups already answer **401** + `WWW-Authenticate`; that residual was dropped); contract doc-comment targets corrected (`:228` added, `:526` is a **response** DTO → different wording); Keycloak image version pinned + health-check/`WaitFor` or softened AC#4 wording; client-secret dev-only literal pinned in both places; new recorded residuals (Admin UI cannot authenticate to the bearer-only groups after the flip; Families' Authority not fanned out).

## Worker Report

## Review

### PLAN REVIEW (step 2b) — `5daed85e`, `ollama-cloud/deepseek-v4.1-flash`, read-only

**Retroactive-order deviation (recorded honestly):** this round's plan review ran *after* the worker was already dispatched (the owner requested the gate on 2026-09-18, mid-round). Going forward the skill requires it **before** dispatch (step 2b). Severity was therefore triaged into steer-now vs fix-in-rework, and the plan was **amended** (above) rather than re-dispatched.

**Verdict: REWORK — 7 P1.** Code-grounded throughout (every finding cites file:line); it vindicates the gate by showing that acceptance #4's real-path verification could not have passed for **three independent reasons**.

| P1 | Finding |
|---|---|
| 1 | No audience mapper → `azp` not `aud` on access tokens → `ValidateAudience` fails IDX10214 → the bearer call can never 200 |
| 2 | Mapper **types and token targets unpinned**; the repo's claim contract promises only the **ID token**, and `ClaimActions` shapes only **userinfo** — neither can serve the bearer path |
| 3 | No `RequireHttpsMetadata=false` story for an HTTP Authority → the dev flip fails before any token is validated |
| 4 | Dev seeder **not implementable as written**: `Tenant.Create`/`Teacher.Create` hardcode `Guid.NewGuid()`; the `…0001` precedent is a raw-SQL migration; tenant save-guards + the strict filter break "skip if exists"; `StaffNumber` does not auto-generate on a direct insert |
| 5 | `ITeacherDirectory` is a **service-to-service 401** in exactly the mode AC#4 exercises (no token/tenant forwarding exists anywhere in `src`) |
| 6 | **Decision 5 is insecure as written**: the fallback is keyed on *claim absence*, so in real-auth mode any authenticated user without a correct `teacher_id` has their wire `TeacherId`/`ApproverId` honored — silently re-opening the owner's server-authoritative decision |
| 7 | The expected-files list omits the **mandatory existing-test edits** (positional handler constructors) — the build would be red and AC#2's "counts preserved" would be dishonest |
| P2s | TestAuth branch has no `RequireAuthorization()` to keep; 302-to-Keycloak instead of 401 on bearer groups; `:526` is a **response** DTO (wrong deprecation target; `:228` missing); image version unpinned + no health check; client-secret contradiction; Admin-UI + Families-Authority residuals |
| Acceptance honesty | **Not ok as planned**: 1 test is a regression guard by construction, 3 can pass vacuously unless the mechanism is pinned (real handler path over a test-signed JWT; principal-mapped assertion; differing non-empty Guid; fake `ICurrentUser` + stubbed directory) |
| `.razor` claim | **Agreed — no UI file is forced** |

All seven P1s + the P2 corrections + the acceptance-honesty fixes were **steered to the worker** while it was still writing those files.

### DIFF REVIEW (step 4b) — `028705c5`, `ollama-cloud/deepseek-v4.1-flash`, read-only, over the frozen patch

**Verdict: REWORK — 1 P1, 8 P2.** Plan-amendment conformance: **P1-1, P1-2, P1-3, P1-5, P1-6, P1-7 confirmed**; P1-4 confirmed in mechanism but not as written (see P1 below).

| Severity | Finding |
|---|---|
| **P1** | `DevIdentitySeeder.cs:45-51` + `:57-64` — `ExecuteSqlRawAsync(sql, DevSchoolTenantId, ct)` binds to `params object[]`, so the `CancellationToken` is passed as a **SQL parameter value** (EF auto-converts to a `DbParameter`; `CancellationToken` has no type mapping) → conversion failure at migrator startup (caught → `exitCode = 1` → every host's `WaitForCompletion(migrator)` blocks) or a bogus parameter with a silently ignored token. Fix: `ExecuteSqlRawAsync(sql, new object[] { DevSchoolTenantId }, ct)`. **On AC#4's critical path — fix before interpreting any AC#4 result.** |
| P2 | `AssignmentRoutes.cs:414-436` (+ `:522-538`, `:733-749`) — the two new typed exceptions are unmapped at the HTTP boundary (only `AssignmentNotFoundException`/`ArgumentException`/`InvalidOperationException` are caught) → real-auth rejections surface as **500**; fail-closed but the round's own rationale (map distinctly) is undelivered |
| P2 | `AuthTenancyClaimMappingTests.cs:36-47` — vacuous w.r.t. the `:78` TODO it claims to cover (builds an identity from `ReadJwtToken`; never touches `ClaimActions`/any handler) |
| P2 | `CurrentUserTests.cs:44-59` — no test drives the **real bearer path** (test-signed JWT through `JwtBearerHandler`, no metadata fetch) → broken scheme wiring cannot fail any test |
| P2 | `AuthTenancyExtensions.cs:100-107` — the four OIDC `ClaimActions.MapJsonKey` calls are **inert** (`GetClaimsFromUserInfoEndpoint` defaults `false`, so no userinfo payload is mapped); the cookie path works only because `id.token.claim=true` puts the claims in the id_token. Comment is wrong — drop the lines or enable the fetch |
| P2 | `DevIdentitySeeder.cs:45-70` — (a) **no environment gate** (dev rows inserted wherever the migrator runs, incl. production); (b) idempotency covers only the `id` conflict while `tenants` also has a **unique index on `name`** (`20260707082801_AddTenantRegistry.cs:30-33`) → a pre-existing "Dev School" with a different id raises a unique violation → migrator exit 1 |
| P2 | `realm-school-collab.json:106-110` — user `attributes` should be **arrays** (Keycloak `Map<String,List<String>>`), not scalar strings; the whole bearer path depends on them surviving import → confirm on AC#4 |
| P2 | Report accuracy — the "302→Keycloak instead of 401" residual is **wrong**: the policy's `AuthenticationSchemes` drive the challenge, so bearer groups already yield 401 + `WWW-Authenticate`. Dropped from the record |
| P2 | Nit — misindented argument block at `Handlers/CreateAssignmentCommandHandlerEntityCodeTests.cs:63-65` |

**Security resolution (the key question):** **positive and verified.** All five handlers key the fallback on `IFeatureFlagService.IsEnabledAsync(DisableOIDCAuth)` (Create `:37-57`, Approve `:30-35`, Reject `:29-34`, Review `:25-30`, Override `:34-39`), which in `assignments-api` resolves to the config-only `ConfigurationFeatureFlagService` — the same `IConfiguration` that chose the scheme at startup — so the wire `TeacherId`/`ApproverId` **cannot** be honoured while bearer/OIDC auth is active, and a missing/mismatched claim fails closed with `MissingTeacherPrincipalException`. Tenant resolution is server-authoritative (JWT `tenant_id` → `TenantClaimsTransformation`/`TenantProvider`; `x-tenant-id` is honoured only by `TestAuthHandler`, i.e. dev). **Nothing caller-supplied can spoof the tenant or the teacher.** Scope/CI gates confirmed clean (no UI, no migration, no contract-shape change, no `[Obsolete]`, CPM, `DisableOIDCAuth=true` untouched in all five hosts, only labelled dev-only credentials committed).

**Further residuals the reviewer adds (recorded):** (a) **`DisableOIDCAuth` split-brain risk** — handlers read the flag *per request* while the scheme is fixed at *startup*; they agree today only because `assignments-api` uses the config-only flag service — if it ever adopts `AddConfigFeatureFlagClient`, a tenant override could re-enable the wire-field fallback under real auth (hardening: read the startup-decided switch, or assert the principal came from the TestAuth scheme); (b) no cross-tenant validation of the `teacher_id` claim on approve/reject/review/override (the IdP is trusted; only create validates existence, and that check currently 401s in real-auth mode); (c) the ward `/students` group is bearer-only too — same class as the recorded Admin-UI residual; (d) the §4 rewrite must pin the dev client-secret literal, the IDX10205 host-form caveat, and that AC#4 needs `Parameters__keycloak_admin_password` + `Parameters__keycloak_client_secret` (no committed defaults).

## Acceptance

**Verdict: CLOSED (shippable) — with one headline verification residual (manual Keycloak AC#4).**

Pipeline: orchestrator-plan (`ac3f8b1b`) → **PLAN REVIEW** (`5daed85e`, `glm-5.3-flash`-class reviewer, **REWORK — 7 P1**, steered into the running worker) → worker (`edcd81ff`) → **DIFF REVIEW** (`028705c5`, **REWORK — 1 P1, 8 P2**) → bounded rework (`d1fe4b79`, all findings + the `configuration.md` deliverable) → **RE-VERIFY** (`1e0b1cfa`, **ACCEPT — 0 P1**, 5 P2) → parent fixes pass → final matrix.

### Defect ledger (what each gate caught)

| Gate | Caught |
|---|---|
| **Plan review** (first use of the new step 2b) | 7 P1 — the audience mapper gap (`azp`≠`aud` → IDX10214), unpinned mapper types/token targets (ClaimActions cannot serve the bearer path), no `RequireHttpsMetadata` story, a seeder that was not implementable as written (hardcoded `Guid.NewGuid()`, precedent was a migration, tenant save-guards), the service-to-service 401, **a security hole in a pinned decision** (fallback keyed on claim absence instead of auth mode), and the omitted existing-test edits. Plus the finding that 3 of 5 "discriminating" tests could pass vacuously |
| **Diff review** | 1 P1 — `ExecuteSqlRawAsync(sql, id, ct)` binding `ct` as a **SQL parameter value** (migrator exit 1 → every host's `WaitForCompletion(migrator)` blocks; on AC#4's critical path) + 8 P2 (dev rows seeded with no env gate; `name`-index idempotency gap; typed exceptions unmapped → 500; inert ClaimActions; scalar realm attributes; vacuous tests; a wrong residual claim) |
| **Re-verify** | ACCEPT; 5 P2 — submission-review path documented as threaded when it is not, `RequireHttpsMetadata` claimed dev-only while set unconditionally, an unsupported ClientSecret default cell, no test for the round's *own* registration, no route test for the 403/409 mapping |
| **Parent fixes pass** | Landed the 3 doc-accuracy fixes + the missing wiring test. **That test immediately caught a real latent defect class**: with the flag key written the intuitive way (`FeatureFlags:DisableOIDCAuth`) instead of the canonical `FeatureFlags:FEATURE:DisableOIDCAuth`, both tests silently exercised the **OIDC** branch — the "TestAuth" test enumerated `{Cookies, OpenIdConnect, Bearer}` |

### Final figures

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.sln` | **0 errors** (plain; the worker's `--ignore-failed-sources` was a machine-local user-level NuGet source, not a repo issue) |
| **Authoritative matrix** | **2,391 / 0** — Core **92** (+2 wiring tests), Assignments **663**, Api 80, Integration 4, Families 42, Students 422, Students.Api 1, Settings 519, Settings.Api 1, Admin 546, Architecture 21 |
| Patch | **38 files, +1342 / −55**, drift-free (dirty-base = the plan-review-formalization skill edits, committed separately) |
| Security posture | **verified positively**: the wire `TeacherId`/`ApproverId` cannot be honoured under real auth (all five handlers key on the startup-decided flag source → `ConfigurationFeatureFlagService`); missing/mismatched claim fails closed; tenant resolution is server-authoritative (JWT `tenant_id`; `x-tenant-id` is dev-only) |
| Gates | no UI file, no contract-shape change, no migration, no committed secret beyond labelled dev-only values, CPM respected, `DisableOIDCAuth=true` untouched in all five hosts |

### Residuals (recorded)

**Headline — AC#4 (manual Keycloak end-to-end): DEFERRED 2026-09-18 to round ar-21**
(`documents/rounds/round-ar-21-keycloak-apphost-hardening.md`). AC#4 is prerequisite-blocked, not merely unrun: the
AppHost Keycloak block has no readiness gate, no `targetPort`, a realm file that violates Keycloak's
`<realm>-realm.json` naming convention, and a realm file that is **not strict JSON** (comments inside the object).
ar-21 owns those fixes and the AC#4 run. **ar-20 therefore closes as-is, with no open AC#4 obligation.** The
original residual text is retained below as provenance.

Historical detail: AC#4 was not run. Needs `Parameters__keycloak_admin_password` + `Parameters__keycloak_client_secret` supplied and `DisableOIDCAuth=false`. Pre-flight notes from the re-verify: (i) confirm the migrator logs **Development** — the new seeder gate silently no-ops otherwise (no `launchSettings.json` in `MigrationService`); (ii) the Keycloak endpoint is declared without `targetPort`/health-check/`WaitFor` (unlike the mailpit precedent) — verify the container port and add a health gate; (iii) verify Keycloak's realm import accepts the `//` comments in `realm-school-collab.json` (move the dev-only note out of the JSON if the import rejects them). **Expected deviation:** AC#4's *create* leg cannot attribute to `…0003` while no service-to-service token forwarding exists (accepted plan P1-5 option (b)) — expect 409/`UnknownTeacher` there unless AC#4 is restated.

**Others:** the submission-review path (`ReviewSubmissionRequest.TeacherId` → `ReviewSubmissionCommandHandler`) is **not** identity-threaded — its doc comments now say so explicitly (D-6 follow-up); the `DisableOIDCAuth` **split-brain risk** (handlers read the flag per request, the scheme is fixed at startup — they agree only because `assignments-api` uses the config-only flag service; hardening: assert the principal came from TestAuth or read the startup switch); no cross-tenant validation of the `teacher_id` claim on approve/reject/review/override (IdP trusted); the ward `/students` group and the Admin UI's assignment calls are bearer-only after a flip (Admin cannot authenticate until token forwarding exists); dev teacher `staff_number` NULL; `TestAuthShape_…` remains partly vacuous; no route-level test for the 403/409 mapping; image `quay.io/keycloak/keycloak:26.2` pinned.