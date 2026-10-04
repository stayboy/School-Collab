# Round — teacher-scope-auth

Round `teacher-scope-auth` · **Tier 3 full** · 2026-10-03
Models: orchestrator `ollama-cloud/glm-5.3-flash` · plan-review `ollama-cloud/glm-5.3` · worker `ollama-cloud/deepseek-v4.1-flash` · reviewer `ollama-cloud/kimi-k2.7-code`
Base: `main` (after `#295`) · Branch: `stack/8-teacher-scope-auth`

Settled inputs: portal grill round 2026-10-03 — **Q1** teacher portal is the MVP · **Q2** strictly
read-only (no grade write) · **Q3** bundle T2+T3+T4, fail-closed · **Q5** stamp a dev `teacher_id` ·
**Q7** follow the repo's documented cross-context pattern · **Q8** this is round 1 of 2 (the portal
surface + Python CI job is round 2).

## Plan

> **Amended after the plan-review gate** (`84cc210e`, `glm-5.3`, verdict **REWORK** — 6 P1 + 4 P2). Every
> finding is folded in below and marked **[PR-n]**. Per the gate's dispatch condition, the amended plan
> proceeds with no second plan review.

### Goal

Land the **backend authorization base** the teacher portal will consume — realm roles, a teacher-scope
port, the scope filter, and API-side flag-conditional policies — **fail-closed**, so the portal's read
surfaces (round 2) are built on an authorization base that is already correct and already tested.

### Why this is its own round

Q8: the two halves have different test stacks and different risk. This half is where the security risk
lives, it is verifiable with the existing .NET suites including the real-Postgres Integration suite, and
it must land **before** any surface consumes it. Round 2 is UI plus CI wiring and is never Tier 3.

### Deliverables

**D1 — realm roles (T2).** Add `{ "name": "teacher" }` and `{ "name": "staff" }` to
`src/AppHost/SchoolCollab.AppHost/school-collab-realm.json` → `roles.realm` (currently `user-admin`,
`platform-admin`). Declarative definitions only; assignment to users stays in the auth admin UI (D11).
The `roles` claim mapper already exists (~L125-129) and `PortalSessionAuthenticationHandler` plus both
the OIDC and JwtBearer `RoleClaimType = ClaimTypes.Role` pins make `RequireRole` resolve.

**[P2-4] Hoist the role names to shared constants** per `.github/copilot/rules/shared-constants.md`.
`user-admin` is currently duplicated between the realm json and `AuthEndpointGroup.UserAdminRoleName`;
`teacher`/`staff` must not replicate that. The realm json stays literal (it is Keycloak config); the C#
side reads from one constants type.

**D2 — teacher-scope port (T3).** Following the documented cross-context pattern *and* the
`IContactResolver` shape:

| Piece | Location | Note |
|---|---|---|
| `ITeacherScopeProvider` + `TeacherScope` | `Assignments.Core/Services/` | Interface only — Core must not gain a Students reference (§1 of `cross-context-rule-followups.md` is an **open violation**; do not deepen it) |
| `TeacherScopeHttpClient` | `Assignments.Api/Services/` | Implements the port over the `students-api` named client → `GET /teachers/{teacherId}/grade-assignments` |
| Local DTO mirror | `Assignments.Api/Services/` | **Never** reference `Students.Core`/`Students.Application` types |

`TeacherScope` shape: `bool IsUnrestricted` (staff/admin), `IReadOnlyList<TeacherSubjectGrade> Taught`,
`bool IsEmpty`. **Fail-closed:** a transport failure, non-2xx, malformed body or empty list yields an
**empty** scope — never `IsUnrestricted` — for a caller whose only role is `teacher`.

**[P1-5] `TeacherSubjectGrade.TopicId` must be `Guid?`, and `null` means grade-wide.** `TeacherGradeLevel.TopicId`
is **optional** — `TeacherGradeLevel.cs:8-10`: *"a row is either a plain grade + role assignment or grade +
subject + role"*, and the Students DTO's `SubjectId` is `Guid?`. A filter requiring an exact
`(TopicId, GradeLevelId)` pair silently drops every grade-wide teacher. Match rule: `GradeLevelId` must
equal, **and** (`TopicId is null` ⇒ grade-wide match; otherwise `TopicId` must equal).
`TeacherRoleCodedValueId` needs no filter leg — it is metadata on the teaching row, not an assignment
dimension, so the disjunction is complete once the null-`TopicId` leg is added.

**[P1-6]/[PR] Registration — migrate, never double-register.** Replace the bare
`builder.Services.AddHttpClient("students-api")` at `Assignments.Api/Program.cs:126` with:

```csharp
builder.Services.AddCrossModuleHttpClient("students-api", "https+http://students-api", propagateTenant: false)
    .AddHttpMessageHandler<BearerForwardingDelegatingHandler>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
```

keeping the chained ar-24 posture (which the pattern doc omits). **One registration, never a second of the
same name.** Regression surface to re-verify: that single named client also feeds `IContactResolver`,
`ITeacherDirectory`, `IStudentDirectory`, `IActivityGroupLookup`, `IAssignmentTargetResolver` and
`ITopicAssignmentLookup`. The **Worker's** own `students-api` (`Assignments.Worker/Program.cs:37`) is
untouched.

**[Q7 record] Replication is rejected for THIS hop, on record.** The alternative — subscribe to Students'
teaching-link events and replicate the taught set into Assignments — is rejected because this data is
**authorization input**: a replicated copy means a teacher whose link was **revoked** keeps seeing those
assignments until replication catches up, i.e. a **fail-open window on an access decision**. The ADR's own
classification puts live-consistency reads on the HTTP path (`adr-cross-module-calls.md`, the
`StudentsContactResolver` / `ActivityGroupLookupHttpClient` rows). Cross-context *events* via the
transactional outbox remain the mechanism for state changes — not for a read that gates access.

**D3 — the scope filter (T3).** Apply `TeacherScope` to the assignment **list** and the
**review-queue/submissions** reads. A `teacher` sees assignments where `CreatedByTeacherId == callerTeacherId`
**OR** (`GradeLevelId`, optionally `TopicId`) ∈ `Taught` per D2's match rule. `staff`/admins see
tenant-wide. The caller's id comes from `ICurrentUser.TeacherId`; the scope is resolved at the
**endpoint** and passed on the query — never fetched inside a Core handler.

**[P1-3] Cache discipline — the leak that would have shipped.** `ListAssignmentsQueryHandler` caches the
list under the **tenant-wide** key `assignments:list:{tenantId}:{status}` (`:30, :43`). The scope filter
**MUST** be applied **after** `cache.GetOrCreateAsync` returns (the cached value is the tenant-wide,
unfiltered projection), **or** the cache key must incorporate a scope hash. Applying the filter inside the
cached delegate would serve one teacher's filtered list to every other teacher and admin in the tenant.
A **two-caller test** must prove no bleed (caller A's list never observed by caller B).

**[P1-4] Role-less principals — state the posture explicitly.** Every TestAuth principal (all of CI, the
Integration suite, and dev) carries **no role claims** (`TestAuthHandler` emits none). The design is:

| Caller | Posture |
|---|---|
| **No recognized role** | **tenant-wide** — preserving today's behaviour for CI/dev/TestAuth. This cannot fail open in real auth, because the D4 policy gates every teacher-scoped read *before* the filter |
| `teacher` **with** a `teacher_id` | scoped per the disjunction |
| `teacher` **without** a `teacher_id` claim | **empty scope — never tenant-wide**, and never the review-queue route's dev `teacherId` fallback |
| `staff`/admin | tenant-wide |

**[P2-1] Name the queries exactly.** The review queue is `GetSubmissionsForReview` /
`GetSubmissionsForReviewHandler` (route `GET /assignments/{id}/submissions/review-queue`); the list-per-assignment
read is `ListSubmissionsByAssignment` (`GET /{id}/submissions`).

**[P2-2] Direct-id reads bypass the list scope — close them.** `GET /{id}` (`GetAssignmentByIdQuery`),
`GET /{id}/submissions` and `GET /{id}/students/{studentId}/submission` are reachable **by id**, so a
teacher-only principal could read an out-of-scope assignment even with the list filtered. Each must apply
the same scope check and answer **404/403** for an out-of-scope id. (These are also round-2 portal
surfaces per T5, so the check belongs here, in the authorization half.)

**D4 — authorization policies (T4).** Named policies added flag-conditionally, following the existing
shape (`if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth)) { … }`).

**[P1-1] Apply per NESTED SUB-GROUP, never group-wide, and enumerate the routes.** The `/assignments`
group today requires only `RequireAuthenticatedUser()` + Bearer (`AssignmentEndpoints.cs:15-23`) and also
serves the Families **ward/guardian** flow and the create-wizard reads, all consumed by role-less
principals. A group-wide policy breaks all of them; the `/students` ward group and the activity-groups
group break identically.

| | Routes |
|---|---|
| **Covered** (teacher-portal reads) | `GET /assignments` (list) · `GET /assignments/{id}` · `GET /{id}/submissions` · `GET /{id}/submissions/review-queue` · `GET /{id}/students/{studentId}/submission` · `GET /{id}/sign-off-statuses` |
| **Explicitly NOT covered** | ward/guardian routes (`…/modules`, `…/progress`, `…/students/{studentId}/submission`, `…/submit-on-behalf`, `…/guardian-review`, `…/sign-off`, `…/gates/student/{studentId}`, `…/recipients/{contactId}/opened`) · the `/students` ward group · the `/guardian` public group · the activity-groups group · **all** create/edit/publish/approve writes |

The policy is one disjunctive `require-assignment-reader` = `teacher` ∨ `staff` ∨ `user-admin` ∨
`platform-admin` — the right shape **only** when applied per-route as above.

**[P1-2] Operational prerequisite — the flag flip needs role assignment first.** Realm roles are assigned
manually (D11) and the seeded `dev-teacher` user carries **no realm role** (attributes only). So the policy
would 403 the current Admin population until roles exist. Therefore: the policy lands **behind
`FEATURE:DisableOIDCAuth`** (flag on ⇒ no policy ⇒ today's behaviour), and the rollout prerequisite is
**assign `teacher`/`staff` (or an admin role) to every existing assignment-reading user BEFORE the flag is
flipped**. Recorded as an explicit operational prerequisite in the round doc and, on close, in the spec.

**D5 — dev teacher identity (Q5).** `TestAuthHandlerOptions.TeacherId` defaults to `Guid.Empty`, so no
`teacher_id` claim is emitted and a teacher-scope filter is unexercisable under the dev bypass. Add a
**dev-configurable** teacher id via the existing named-options pattern
(`Configure<TestAuthHandlerOptions>(TestAuthScheme, …)`) in Assignments.Api's dev wiring. The default
posture stays claim-less in CI unless a test opts in. ([PR] The claim-emitting/omitting leg already exists
at `TestAuthHandler.cs:82-86`; only the config binding is new.)

**D6 — tests.** Discriminating at each seam:
1. Realm roles present in the json (weakly discriminating — fails only if absent; labelled as such).
2. Scope filter in both directions: creator-only; taught grade-wide (`TopicId` null); taught subject+grade;
   and **not** someone else's untaught assignment.
3. **[P2-3] Fail-closed asserted AT THE LIST LEVEL** — seeded foreign assignments + a failing/malformed
   Students hop must yield **no rows**; an assertion on the `TeacherScope` object alone would not
   discriminate, because a tenant-wide fallback would still return rows and must fail the test.
4. **[P1-3] Two-caller cache-bleed test** — A's filtered list is never observed by B.
5. Policy: teacher passes, staff passes, **admin passes** (the P1-2 compatibility case), unauthenticated
   does not, and every "explicitly NOT covered" route still admits a role-less principal.
6. **[P1-4] Role-less ⇒ tenant-wide; `teacher` without `teacher_id` ⇒ empty.**
7. **[P2-2] Out-of-scope direct-id read ⇒ 404/403.**

### Acceptance criteria

1. D2's port follows the documented `AddCrossModuleHttpClient` pattern via the **migrated single**
   registration (never a second of the same name); `CrossModuleWiringTests` passes and the six existing
   consumers of `students-api` still work.
2. **No new project reference** between bounded contexts; no Students type in an Assignments signature.
3. **[P2-3] Fail-closed proven at the list level** with a failing Students hop — the test fails if the
   implementation falls back to tenant-wide.
4. **Admin compatibility proven** — an admin-only principal still passes the policy; teacher passes;
   unauthenticated does not; and every non-covered route still admits a role-less principal.
5. **[PR — regression guard, not a discriminator]** With `FEATURE:DisableOIDCAuth=true`, no group
   requires authorization. This is **already true** pre-fix (the policy block sits inside
   `if (!DisableOIDCAuth)`), so it cannot fail against the pre-fix code and is labelled a regression guard.
6. **[PR — regression guard]** Dev teacher id: claim-less by default, honoured when configured. The
   claim leg already exists at `TestAuthHandler.cs:82-86`; only the config binding is new. Labelled a
   regression guard accordingly.
7. **[P1-3]** No cache bleed across two callers.
8. Build 0 errors; Architecture, Assignments, Api and **Integration (real Postgres)** suites green.

### Expected files

| Area | Files |
|---|---|
| Realm | `src/AppHost/SchoolCollab.AppHost/school-collab-realm.json` |
| Port | **new** `Assignments.Core/Services/ITeacherScopeProvider.cs`, **new** `Assignments.Api/Services/TeacherScopeHttpClient.cs` (+ local DTO mirror) |
| Wiring | `Assignments.Api/Program.cs` (**migrate** the `students-api` registration, `:126`) |
| Auth | `AssignmentEndpoints.cs` (per-route policy + nested sub-group), shared role constants, `TestAuthHandler` dev config binding |
| Queries | `ListAssignmentsQuery(+Handler)` — **filter after the cache read** · `GetSubmissionsForReview(+Handler)` · `ListSubmissionsByAssignment` · `GetAssignmentByIdQuery` (P2-2 scope check) |
| Tests | unit (scope incl. grade-wide, fail-closed at list level, cache bleed, policy incl. admin + role-less, out-of-scope id), architecture (no-new-reference + wiring guard) |

### Out of scope

The portal UI surface and the Python CI job (round 2 per Q8); the grade **write** (Q2 — read-only);
TGT-15; the pre-existing §1 violation in `cross-context-rule-followups.md` (recorded, not repaired here);
the AGENTS.md `:269` wording fix (`cross-context-rule-followups.md` §3 — a separate docs commit); any
change to who may create/edit/publish/approve.

## Plan Review

**Gate `84cc210e` (`ollama-cloud/glm-5.3`) — Verdict: REWORK.** 6 P1 + 4 P2, all folded into the plan
above ([P1-n]/[P2-n] markers). Verified correct in the plan: admin role names are exactly
`user-admin`/`platform-admin`; the `roles` claim mapper exists; both the OIDC and JwtBearer
`RoleClaimType` pins make `RequireRole` resolve; `ICurrentUser.TeacherId` is registered scoped and
reachable at every endpoint; `GET /teachers/{id}/grade-assignments` + `TeacherGradeAssignmentDto` exist;
AppHost `.WithReference(studentsApi)` is present (~L326); no contract, migration or secret impact. The D5
seam and the D3 seam choice (resolve at the endpoint, pass on the query — matching both existing
threadings) were both confirmed sound, and `TeacherRoleCodedValueId` needs no filter leg.

**Gate findings, verbatim in substance:**

| # | Finding | Folded |
|---|---|---|
| P1-1 | D4's route coverage unenumerated; group-wide breaks the Families ward/guardian flow, the `/students` ward group, the activity-groups group and every role-less Admin caller | per nested sub-group + the enumerated covered/not-covered tables |
| P1-2 | "Never replace the admin path" unproven — roles are manual (D11) and `dev-teacher` holds no realm role, so the policy 403s the current Admin population | operational prerequisite + the behind-the-flag staging |
| P1-3 | `ListAssignmentsQueryHandler` caches tenant-wide (`assignments:list:{tenantId}:{status}`) → filtering inside the cached delegate leaks one teacher's list to the tenant | filter after `GetOrCreateAsync` (or scope-hash key) + the two-caller test |
| P1-4 | Role-less principal unspecified — TestAuth emits no role claims, so role-less ⇒ empty would darken CI, Integration and dev | the four-row posture table |
| P1-5 | `TeacherScope` cannot represent a grade-only row — `TeacherGradeLevel.TopicId` is optional | `TopicId` nullable, null ⇒ grade-wide |
| P1-6 | D2 contradicted acceptance 1 (bare `AddHttpClient` vs the documented helper) | migrate the single registration; never a second |
| P2-1 | Name the query exactly | `GetSubmissionsForReview` / `GetSubmissionsForReviewHandler` |
| P2-2 | Direct-id reads bypass the list scope | closed with a 404/403 scope check |
| P2-3 | The fail-closed test must assert at the list level | acceptance criterion 3 |
| P2-4 | Hoist role names to shared constants | D1 |

**Acceptance honesty:** criterion 5 and the dev-teacher-id half of criterion 6 **cannot fail against the
pre-fix code** and are labelled regression guards rather than discriminators (the gate's own finding). The
realm-roles assertion is weakly discriminating and labelled as such.

## Worker Report

**Worker `53d5e621` + bounded rework `affc7f22` (`ollama-cloud/deepseek-v4.1-flash`).** 21 files + 3 rework files; nothing
staged/committed. Two files in the tree are **not** this round's: `AGENTS.md` and
`documents/solution/cross-context-rule-followups.md` (the separate docs commit — excluded from this round's patch).

### What landed

- **D1** — realm gains `teacher` + `staff`; role names hoisted to one shared
  `SchoolCollab.Core/Constants/RealmRoleNames.cs`, and the pre-existing duplicate `AuthEndpointGroup.UserAdminRoleName`
  deleted in favour of it **[P2-4]**. Realm json stays literal (it is Keycloak config).
- **D2** — `ITeacherScopeProvider` + `TeacherScope` in `Assignments.Core` (interface only, no Students reference);
  `TeacherScopeHttpClient` + a local DTO mirror in `Assignments.Api` over the **migrated single** `students-api` client;
  `TeacherSubjectGrade.TopicId` is `Guid?` with `null` ⇒ grade-wide **[P1-5]**. Fail-closed: failure / non-2xx / malformed ⇒
  `TeacherScope.Empty`, never `Unrestricted`.
- **Registration [P1-6] — migrated, not added:**
  `AddCrossModuleHttpClient("students-api", "https+http://students-api", propagateTenant: false)` + the retained
  `BearerForwardingDelegatingHandler` / `AllowAutoRedirect = false` posture. `TeacherScopeWiringArchitectureTests` asserts
  **one** registration of that name and zero bare `AddHttpClient("students-api"` in that host; `CrossModuleWiringTests` and
  `BearerForwardingWiringArchitectureTests` pass unchanged; the Worker's own registration is pinned untouched.
- **D3** — scope resolved at the endpoint (`AssignmentScopeResolver`, endpoint layer because `ICurrentUser` exposes no roles)
  and passed on the query; the filter applies **after** `cache.GetOrCreateAsync` for both the list and the by-id read **[P1-3]**;
  three id-addressed reads answer 404 for an out-of-scope id **[P2-2]**.
- **D4** — one disjunctive reader policy (`teacher ∨ staff ∨ user-admin ∨ platform-admin`, bearer + authenticated) on a
  **nested sub-group** carrying exactly the six covered GETs, inside `if (!DisableOIDCAuth)` **[P1-2]**; the 22 not-covered
  routes verified to keep admitting a role-less principal **[P1-1]**.
- **D5** — `TestAuth:TeacherId` bound via the named-options pattern, claim-less by default, documented in
  `documents/configuration.md` per the config-documentation rule.

### The rework closed the worker's own disclosed residual

`GET /{id}/sign-off-statuses` was **policy-gated but not scope-gated**, so a `teacher`-roled principal could read another
teacher's per-ward rows by id. **The worker found and disclosed this itself rather than shipping the asymmetry** — the
behaviour I want. Parent confirmed it in source (`AssignmentRoutes.cs:184`) and ruled: fix, do not defer. It now calls the
**same** `IsAssignmentVisibleAsync` helper as its sibling reads (one mechanism, three call sites: `:116`, `:137`, `:197`),
answering 404 byte-identically to an unknown id. New test
`Http_SignOffStatuses_OutOfScopeId_Is404_AndTheInScopeIdsReadTheRows` was **run against the unmodified route** to prove it
fails pre-fix: `Expected foreign.StatusCode to be NotFound {404} … but found OK {200}`.

**Harness completion (deliberate, parent-authorised):** `SignOffRoutesTests.Statuses_Returns200List` went red with a
request-time `InvalidOperationException: No service for type 'ICurrentUser'` — its minimal host registered only the sign-off
handler, and the route legitimately gained three dependencies. The parent ruled: complete the harness, do not leave the suite
red. Exactly three registrations were added (role-less `ICurrentUser`, unrestricted scope provider, stub detail handler);
**no assertion, expectation or 200-outcome changed** — parent-verified: 0 assertion-ish lines in that file's diff. That the
test went red is the pre-existing coverage doing its job: it caught that the route's dependency surface changed.

### Verified (parent re-ran all of it, incl. the real-Postgres suite)

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| `ArchitectureTests.Unit` | **80 / 0** (was 75) |
| `Assignments.Tests.Unit` | **884 / 0** (was 871) |
| `Assignments.Api.Tests.Unit` | **138 / 0** (was 111) |
| `Assignments.Tests.Integration` (real Postgres) | **20 / 0** (was 19) |

### Deviations (all reported; parent rulings in brackets)

1. **D4's policy is inline** (`AssignmentEndpoints.RequireAssignmentReader`), not a named policy registered in
   `AddAuthorization`: a named policy is unresolvable in hosts composing from `AddAuthAndTenancy` + `MapAssignmentEndpoints`
   without Assignments' `Program.cs`, giving 500 where a pre-existing test asserts 401. **[Deferred to the reviewer —
   inline may be right, or the policy may belong in a shared registration.]**
2. **`ListSubmissionsByAssignment` carries no scope leg**; the P2-2 gate for `GET /{id}/submissions` is applied at the route
   via the scope-aware by-id read (same rule, same 404). Adding a leg would break a pre-existing test's construction.
   **[Accepted]**
3. **`GET /{id}/sign-off-statuses` initially policy-gated but not scope-gated** — the disclosed residual. **[Rejected as a
   deferral; fixed in the rework.]**
4. **Scope resolution lives in a new endpoint-layer `AssignmentScopeResolver`** because `ICurrentUser` exposes no roles; roles
   come from the `ClaimsPrincipal`. **[Accepted]**
5. **Failure posture:** a 200 with `[]` yields a *resolved* taught set with the own-creation leg intact, while
   transport/non-2xx/malformed yields `TeacherScope.Empty`. **[Accepted]** — a resolved-but-empty taught set is never
   `Unrestricted`, which is the plan's intent.

### Disclosed risks (recorded, not actioned)

- The review queue now returns submissions for **taught** as well as created assignments — intended D3 behaviour; no
  pre-existing test covered the queue body.
- Under the dev bypass `TestAuthHandler` emits no role claims, so the **role-less ⇒ tenant-wide** posture still applies there;
  a configured `TestAuth:TeacherId` alone scopes nothing (roles + the manual D11 assignment are the gate). Documented.
- The gate adds one extra scope-aware detail read per sign-off-statuses call (both reads HybridCache-backed), and is now a
  hard dependency of that route.
- **Manual role assignment (D11) remains the prerequisite before the flag flips** — the policy would otherwise 403 every
  existing assignment reader. Recorded in `AssignmentEndpoints` and here.

## Review

**Diff reviewer `d5960f8b` (`kimi-k2.7-code`) — Verdict: `P2-only`, no P1, "round can be accepted: yes".**

| Seam | Verdict |
|---|---|
| **Registration migration [P1-6]** | ok — `Assignments.Api/Program.cs:137` is the **single** `AddCrossModuleHttpClient("students-api", "https+http://students-api", propagateTenant: false)` chained with `BearerForwardingDelegatingHandler` + `AllowAutoRedirect = false`; no second registration; `Assignments.Worker/Program.cs:37` keeps its own bare client untouched |
| **Cache discipline [P1-3]** | ok — `ApplyScope` runs **after** `cache.GetOrCreateAsync` in `ListAssignmentsQueryHandler`, and the by-id read gates after its cache read; the two-caller test would fail if the filter moved inside the cached delegate |
| **Policy coverage [P1-1]** | ok — the reader sub-group carries exactly the six covered GETs (`AssignmentRoutes.cs:75,90,106,126,146,187`); the test enumerates all **22** not-covered routes and proves they carry no role requirement and still admit a role-less principal |
| **Grade-wide + role posture [P1-5]/[P1-4]** | ok — `TeacherScope.Allows` uses `(row.TopicId is null \|\| row.TopicId == topicId)`; the four-row posture is implemented at `AssignmentScopeResolver.cs:40-50` and each row is tested; legacy `Scope: null` callers stay tenant-wide |
| **One-mechanism gate (rework)** | ok — `IsAssignmentVisibleAsync` is the single visibility rule at all three call sites; the sign-off route answers 404 byte-identically to an unknown id and never invokes the rows handler |
| **Harness completion honest** | **yes** — `SignOffRoutesTests.cs` added only the three registrations + three honest stubs (role-less `ICurrentUser`, unrestricted scope provider, stub detail handler); no assertion, expectation or 200-outcome changed |
| **Inline-vs-named policy** (deviation #1, deferred to the reviewer) | **inline ok** — matches the `AuthEndpointGroup.ConfigurePortalFacingAdminPolicy` precedent; a named policy would need registration in hosts composing `AddAuthAndTenancy` + `MapAssignmentEndpoints` **without** `Assignments.Api/Program.cs` (e.g. `RealAuthRouteTests.cs`), so a shared registration would mean either polluting Core with an Assignments-specific policy or editing those existing test hosts |
| Best-practices | no overwrites / skills honoured / readable |

**P2s:**

1. `ListAssignmentsScopeFilterTests.cs:198` — `List_NoRecognisedRole_IsTenantWide` is a **posture guard that would also pass pre-fix**, yet the class doc-comment claimed *"every case here fails against the pre-fix code"*. **The parent fixed it at acceptance** (comment-only: a `<summary>` labelling the case as NOT a discriminator, and the class comment corrected to name the exception). Disclosed here because it is a post-review change to a reviewed artifact, even though comments cannot alter behaviour; build and the suites were re-run green afterwards (80/884/138) and the patch refrozen (3,407 lines).
2. `AGENTS.md:269` + `documents/solution/cross-context-rule-followups.md:5` are in the working tree but belong to a **separate docs commit** — the reviewer asked that they not be bundled. **Already satisfied:** the frozen patch's file headers contain **neither** (verified: 0).

**Acceptance honesty:** criterion 5 and the dev-teacher-id half of criterion 6 are labelled regression guards in the tests themselves; the realm-roles assertion is labelled weakly discriminating; and the one mislabelled posture guard is now labelled (P2-1).

## Acceptance

**Verdict: CLOSED.**

**Tier 3 full** — orchestrator plan → **plan-review gate** `84cc210e` (`glm-5.3`, **REWORK, 6 P1 + 4 P2**, all folded in) → worker `53d5e621` → bounded rework `affc7f22` (the sign-off scope gap) → diff review `d5960f8b` (`kimi-k2.7-code`, **P2-only**) → parent acceptance.

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| `ArchitectureTests.Unit` | **80 / 0** (was 75) |
| `Assignments.Tests.Unit` | **884 / 0** (was 871) |
| `Assignments.Api.Tests.Unit` | **138 / 0** (was 111) |
| `Assignments.Tests.Integration` (real Postgres) | **20 / 0** (was 19) |
| Patch | `diffs-teacher-scope-auth.patch` — 32 files, isolated against `0a5e18b6`, docs commit excluded |

**Acceptance criteria:** 1 (documented pattern via the migrated single registration, `CrossModuleWiringTests` green, six consumers intact) · 2 (no new project reference; no Students type in an Assignments signature — asserted by `TeacherScopeWiringArchitectureTests`) · 3 (fail-closed proven **at the list level** with a failing Students hop; a tenant-wide fallback would return rows and fail) · 4 (admin compatibility + teacher/staff/unauthenticated + every non-covered route still admits a role-less principal) · 5 and 6 (labelled regression guards) · 7 (two-caller cache-bleed guard) · 8 (build + all four suites). **All met.**

**What this round bought:** the authorization base the teacher portal consumes — realm roles, a fail-closed teacher scope over the documented HTTP port pattern, a filter that cannot leak through the tenant-wide cache, a per-route policy that admits teachers without locking admins out, and a dev teacher identity. Three defects were caught that the round's own tests would not have found: the **cache-bleed path** (plan gate), the **sign-off scope gap** (the worker's own disclosure), and the **mislabelled posture guard** (diff review).

**Operational prerequisite, recorded (must precede the flag flip):** realm roles are assigned manually (D11) and the seeded `dev-teacher` holds no realm role, so the reader policy would 403 every existing assignment reader until `teacher`/`staff` (or an admin role) is assigned. The policy lands **behind `FEATURE:DisableOIDCAuth`**, so nothing changes until the flag flips — and the assignment must happen before it does.

**Carried / disclosed, not actioned:** the review queue now returns submissions for **taught** as well as created assignments (intended D3); under the dev bypass the role-less ⇒ tenant-wide posture still applies and a configured `TestAuth:TeacherId` alone scopes nothing; the gate adds one extra HybridCache-backed detail read per sign-off-statuses call and is now a hard dependency of that route.

**Round 2 (next, per Q8):** the portal surface (`views/teacher/`) + the Python CI job — Solo/Light, never Tier 3 — consuming this round's policy, port and dev teacher identity unchanged.
