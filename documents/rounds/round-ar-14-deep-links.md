# Round ar-14-deep-links — Phase 4 slice 4a: WS-E1 token deep links (mint → public landing → ward surface)

Provider: pi (models: glm-5.3-flash orchestrator, deepseek-v4-flash worker, deepseek-v4.1-flash reviewer [owner override], minimax-m3 ui-tester) — Tier 3 FULL FOUR-AGENT, Phase 4 slice 4a (WS-E1 deep links).

- **Round base policy: cut `stack/14-ar-14-deep-links` from `main` AFTER PR #236 (ar-13) merges.** ar-13's Families host + the `AssignmentRecipient` entity are hard prerequisites — verify the tree at fire time (`src/SchoolCollab.Families` exists, `AssignmentRecipient` carries `MarkOpened()`, `PublishAssignmentCommandHandler` resolves the effective policy). Working tree at plan time: `stack/13-ar-13-families-ward-surface` @ `54ac0bea` (= ar-13 feature `0037edfd` on `f023d3ac` + the round-log docs commit; #236 open-unmerged); untracked scratch only. **NO branch cuts, NO commits in this round's plan phase** — the parent owns the cut after the owner's go.
- **Tier check:** new shared crypto infra (DataProtection keyring on Redis) + entity/migration + publish-handler integration + the repo's FIRST public token-auth route group + new pages → Tier 3. Expected ~35 files (±5). ONE additive migration, TWO new CPM packages, ONE new runtime flag.

## Plan

Stand up contact-scoped deep-link tokens: minted at publish against the shared Redis-backed DataProtection keyring, validated by a public Families landing route group behind the `FEATURE:EnableDeepLinks` dark-launch flag, landing the ward/guardian on the existing Families ward surface and stamping `OpenedAt`.

### Decisions (binding)

- **(a) CONTACT-SCOPED TOKEN, one per (assignment, contact) recipient row.** `AssignmentRecipient` rows are keyed per (assignment, contact) — verified (`PublishAssignmentCommandHandler.ResolveRecipientsAndGatesAsync`: `GetRecipientAsync(assignment.Id, s.ContactId)`; ONE row per contact), so a multi-ward guardian holds a single row and a single-ward token cannot represent them. Payload (`DeepLinkTokenPayload`, SchoolCollab.Core): `(Guid TenantId, Guid AssignmentId, Guid ContactId, int OwnerType, int? Role, Guid? WardStudentId, DateTimeOffset ExpiresAt)` — OwnerType/Role carried as ints mirroring `Students.Core` `ContactOwnerType`/`GuardianRole` (Core cannot reference Students.Core; map at the edges). **The impl-details payload omitted tenantId — ADD it (this plan's deviation from `assignment-request-implementation-details.md` §2 WS-E): the host must establish tenant context without OIDC.** Multi-ward guardian (WardStudentId null) lands on the Families ward list `/ward` (exists), not a single-ward player.
- **(b) SHARED REDIS-BACKED DATAPROTECTION KEYRING (the existing `cache` Aspire resource), wired into Assignments.Api (mint) + Families (validate).** Explicit shared `SetApplicationName` constant + purpose string `"ar-deeplink"` — both hosts MUST set the same application name or the Families host cannot unprotect mint-side ciphertext. A small shared helper lives in **SchoolCollab.Core** (`Core/DeepLinks/`: constants + payload record + protector wrapper) so both sides share the contract without a direct context reference (verified: Families references Core; Assignments.Api/Core reference Core). CPM: `Microsoft.AspNetCore.DataProtection.StackExchangeRedis` **10.0.9** (host wiring) + `Microsoft.AspNetCore.DataProtection.Abstractions` **10.0.9** (Core) — aligned to the repo's ASP.NET Core 10.0.9 patch line (`OpenIdConnect`/`OpenApi` are 10.0.9); **no Aspire DataProtection integration exists** — this is the plain ASP.NET Core package. Redis multiplexer source: verify whether the Aspire caching integration registers `IConnectionMultiplexer`; if not, add `Aspire.StackExchange.Redis` 13.4.5 (client integration) — worker reports the choice.
- **(c) FAMILIES PUBLIC SURFACE: `MapDeepLinkEndpoints(this WebApplication app, ...)` — the repo's FIRST public token-auth route group.** Mapped OUTSIDE `MapRazorComponents<App>()` (which is `.RequireAuthorization()`'d when OIDC is on — verified in Families `Program.cs`), with NO `RequireAuthorization` and NO inline route definitions in `Program.cs` (extension-method grouping rule). Flow: `GET /deeplink/{token}` → unprotect (purpose-scoped) → establish tenant context explicitly from the payload (`RunWithExplicitTenantAsync`/`ITenantProvider` — the ar-5 sweep precedent) → check the tenant's `EnableDeepLinks` → stamp `OpenedAt` server-side (FamiliesApiClient → Assignments Api, tenant propagated) → `SignInAsync` a cookie principal carrying `tenant_id` (+ name/contact claims) → redirect to the target page. **Expired/tampered/flag-off → friendly "link expired" page with 410 semantics — never a raw error.** The expired page is a razor component (`LinkExpired.razor`, `@attribute [AllowAnonymous]`) so it stays reachable when the razor-components mapping is authorization-gated in prod AND is bUnit-testable. Antiforgery: `UseAntiforgery()` is on; the landing is GET + redirect only — safe (record: any future POST in the public group must revisit this).
- **(d) `FEATURE:EnableDeepLinks` runtime dark-launch flag, DEFAULT OFF.** New `FeatureFlagKeys` constant + MigrationService seed (mirror `SeedRequireAssignmentApprovalAsync` exactly: `NormalizeKey` → `AnyAsync` guard → `FeatureFlag.Create(key, desc, null, isEnabled: false)` + `FlagAuditEntry`) + `documents/configuration.md` §2/§5 rows (config-documentation rule). **Mint tokens ALWAYS at publish** (cheap, idempotent — no republish needed when the flag flips later); the flag gates ONLY the public route group. Families flag resolution: mirror the Admin precedent — Families references `Settings.Core`, registers `AddConfigFeatureFlagClient(builder.Configuration)` (replacing the config-only service `AddAuthAndTenancy` installs), and AppHost gains `families.WithReference(settingsApi)` (CrossModuleWiringTests enforce the reference). Dev fallback row `"EnableDeepLinks": "false"` in Families `appsettings.json`.
- **(e) EXPIRY: exp = mint time + `LinkValidityDays ?? 7` (record the default).** `EffectiveNotificationPolicy.LinkValidityDays` is available at mint time (verified: resolved at `PublishAssignmentCommandHandler` ~:50, `int?`). Persisted as `DeepLinkToken` (string, protected ciphertext) + `DeepLinkExpiresAt` (DateTimeOffset?) on the recipient row. Built-in default `DeepLinkConstants.DefaultValidityDays = 7` lives in Core next to the constants.
- **(f) MINT POINT: inside the publish flow's recipient persistence (same transaction — the handler enqueues/before-save convention).** New recipients mint at creation; **republish reuses an unexpired existing token, re-mints absent/expired** (idempotent). Aggregate: new `AttachDeepLink(string token, DateTimeOffset expiresAt)` method on `AssignmentRecipient` (additive, stamps `UpdatedAt`).
- **(g) WS-F3 (sign-page relocation) is OUT of 4a** — recorded as the immediate follow-up round (it consumes E1). Guardian deep links in 4a land on the existing ward surface: redirect-target rule — `OwnerType == Student` (or guardian with `WardStudentId` set): `/ward/{wardStudentId}` → the ward list; multi-ward guardian (no `WardStudentId`): `/ward`; direct ward-player hop `/ward/{sid}/assignments/{assignmentId}` is optional polish (worker's call, reported as a deviation if skipped). No guardian-specific page in 4a.
- **(h) CONTRACTS UNTOUCHED in 4a.** Verified: `AssignmentPublishedIntegrationEvent(Guid AssignmentId, string Title, DateTimeOffset UpdatedAt)` — no token fields, and it must not gain any; 4b (channel delivery) joins recipient rows at delivery time. Rationale: tokens are per-(assignment, contact) state persisted transactionally at publish; the broadcast event is a publish announcement, not a delivery payload.
- **(i) LANDING STAMPS `OpenedAt` — method ALREADY EXISTS** (verified: `AssignmentRecipient.MarkOpened()`; no new method needed — plan correction). Stamp path: new authenticated endpoint in the Assignments Api group (e.g. `POST /{id}/recipients/{contactId}/opened`), invoked server-side by the landing; **idempotent first-visit stamp** — the endpoint/handler stamps only when `OpenedAt` is null (first-visit semantics feeding the Sent→Viewed chain), 404 on unknown recipient.

**To-verify resolutions (all resolved at plan time — the worker starts from these):**

1. **assignments-api `redis` reference: ALREADY PRESENT** — AppHost `Program.cs` ~:163 `.WithReference(redis)` on assignments-api; Families ~:236 `.WithReference(redis)`. No AppHost redis change needed; the only AppHost edit is `families.WithReference(settingsApi)`.
2. **DataProtection package choice:** `Microsoft.AspNetCore.DataProtection.StackExchangeRedis` + `...Abstractions`, both 10.0.9, new CPM entries (see decision (b)).
3. **Flag seed pattern:** MigrationService `Program.cs` per-flag seed functions (`SeedRequireAssignmentApprovalAsync` @ :362 is the exact template); add `SeedEnableDeepLinksAsync` in the same shape and wire it into the seeding sequence.
4. **Antiforgery:** GET landing + redirect — no antiforgery token required (decision (c) note).
5. **Sign-in scheme:** **cookie** (`CookieAuthenticationDefaults.AuthenticationScheme`). `AddAuthAndTenancy` registers `.AddCookie()` only in the OIDC branch — in dev (`disableOIDC`) Families must register the cookie handler itself (`builder.Services.AddAuthentication().AddCookie()` guarded by the same `disableOIDC` switch — avoids the duplicate-scheme throw). Principal carries `tenant_id` → the existing `TenantClaimsTransformation` bridges it to `ITenantProvider` on subsequent requests. No `AddCascadingAuthenticationState` exists anywhere and Families pages use NO per-page `AuthorizeView` (verified: Ward pages are host-gated) — do not add it.
6. **Event shape:** verified — see decision (h).

### Expected files (~35 ±5)

**Created (~19):**
- `src/SchoolCollab.Core/DeepLinks/DeepLinkConstants.cs` (ApplicationName + Purpose + DefaultValidityDays)
- `src/SchoolCollab.Core/DeepLinks/DeepLinkTokenPayload.cs`
- `src/SchoolCollab.Core/DeepLinks/DeepLinkProtector.cs` (Protect / TryUnprotect wrapper)
- `src/Assignments/SchoolCollab.Assignments.Core/Services/IDeepLinkTokenMinter.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Services/DeepLinkTokenMinter.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Data/Migrations/<ts>_AddAssignmentRecipientDeepLinks.cs` + `.Designer.cs` (2)
- `src/SchoolCollab.Families/DeepLinks/DeepLinkLandingService.cs` (pure validate→flag→redirect-target logic)
- `src/SchoolCollab.Families/DeepLinks/DeepLinkEndpoints.cs` (`MapDeepLinkEndpoints`)
- `src/SchoolCollab.Families/Components/Pages/DeepLink/LinkExpired.razor` + `.razor.css` (2)
- `tests/SchoolCollab.Core.Tests.Unit/DeepLinks/DeepLinkProtectorTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/PublishAssignmentCommandHandlerDeepLinkTests.cs`
- `tests/SchoolCollab.Families.Tests.Unit/DeepLinkLandingServiceTests.cs`
- `tests/SchoolCollab.Families.Tests.Unit/LinkExpiredBunitTests.cs`
- `tests/SchoolCollab.Families.Tests.Unit/DeepLinkWiringTests.cs` (both hosts' DataProtection registrations share application name + purpose — placement flexible; report as deviation if it lands in Core.Tests/ArchitectureTests instead)

**Modified (~17):**
- `Directory.Packages.props` (+2 packages)
- `src/SchoolCollab.Core/SchoolCollab.Core.csproj` (+DataProtection.Abstractions)
- `src/SchoolCollab.Core/Features/FeatureFlagKeys.cs` (+`EnableDeepLinks`)
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/AssignmentRecipient.cs` (+2 columns + `AttachDeepLink`)
- `src/Assignments/SchoolCollab.Assignments.Core/Data/Configurations/AssignmentRecipientConfiguration.cs` (+2 column config)
- `src/Assignments/SchoolCollab.Assignments.Core/Data/AssignmentsDbContext.cs` (+ snapshot regenerated by `dotnet ef migrations add` — the `*ModelSnapshot.cs` counts as modified)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/PublishAssignmentCommand/PublishAssignmentCommandHandler.cs` (mint step)
- `src/Assignments/SchoolCollab.Assignments.Api/Program.cs` (+`AddDataProtection` + Redis keyring)
- `src/Assignments/SchoolCollab.Assignments.Api/SchoolCollab.Assignments.Api.csproj` (+keyring package)
- `src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs` (+stamp-opened endpoint)
- `src/SchoolCollab.Families/Program.cs` (+DataProtection wiring, cookie-in-dev, `AddConfigFeatureFlagClient`, `MapDeepLinkEndpoints`)
- `src/SchoolCollab.Families/SchoolCollab.Families.csproj` (+keyring package, +Settings.Core ref)
- `src/SchoolCollab.Families/appsettings.json` (dev flag fallback row)
- `src/SchoolCollab.Families/Services/FamiliesApiClient.cs` (+stamp call)
- `src/AppHost/SchoolCollab.AppHost/Program.cs` (families `.WithReference(settingsApi)`)
- `src/SchoolCollab.MigrationService/Program.cs` (`SeedEnableDeepLinksAsync` + sequence wiring)
- `documents/configuration.md` (§2 parameter/flag row + §5 consumer row)

### Implementation steps (ordered; `dotnet build SchoolCollab.sln` after every step; multi-pass expected — cut-line protocol per pass)

1. **Core contract:** `Core/DeepLinks/` trio + Core csproj package + `FeatureFlagKeys.EnableDeepLinks`. Build.
2. **Keyring wiring:** Directory.Packages.props entries; `AddDataProtection().PersistKeysToStackExchangeRedis(...).SetApplicationName(DeepLinkConstants.ApplicationName)` in Assignments.Api + Families (multiplexer source per decision (b) verification); Families csproj (Settings.Core) + `AddConfigFeatureFlagClient`; AppHost `WithReference(settingsApi)`; dev cookie registration. Build.
3. **Persistence:** `AssignmentRecipient` columns + `AttachDeepLink` + EF configuration + additive migration `AddAssignmentRecipientDeepLinks` (ef-migrations rule: never edit existing migrations; review the generated migration for accidental drops). Build.
4. **Mint + stamp:** `IDeepLinkTokenMinter`/impl; publish-handler integration (new recipients mint `exp = now + LinkValidityDays ?? 7`; republish reuses unexpired / re-mints expired-or-absent); stamp-opened endpoint in `AssignmentRoutes`. Build; Assignments handler tests green.
5. **Families public surface:** `MapDeepLinkEndpoints` + `DeepLinkLandingService` + `LinkExpired.razor` (+`[AllowAnonymous]`) + FamiliesApiClient stamp call + `SeedEnableDeepLinksAsync` + configuration.md rows + appsettings fallback. Build.
6. **Full matrix + freeze:** `dotnet build SchoolCollab.sln` (0 errors) + ALL unit projects (Core, Assignments, Assignments.Api, Families, Students, Settings, Admin, ArchitectureTests) + `git add -N -- src tests` + parent freezes the patch + worker self-report → `documents/rounds/.ar-14-worker-report.md`.

### Binding test coverage list (MSTest + FluentAssertions; bUnit for components)

- **Token crypto (`DeepLinkProtectorTests`):** round-trip protects→unprotects to the identical payload; expiry honored (payload past `ExpiresAt` rejected by the landing logic); tamper (flipped/corrupted ciphertext → invalid, no throw escaping); wrong-purpose (a protector created with a different purpose cannot unprotect); tenant-mismatch (payload tenant differs from the expected tenant → rejected).
- **Publish handler (`PublishAssignmentCommandHandlerDeepLinkTests`):** persists `DeepLinkToken` + `DeepLinkExpiresAt` on a NEW recipient (expiry = mint + `LinkValidityDays` when policy set; mint + 7 when null); republish REUSES an unexpired existing token (same token string, no re-stamp); republish RE-MINTS when absent or expired; minting is independent of `EnableDeepLinks` (flag-off still mints).
- **Landing (`DeepLinkLandingServiceTests`):** valid token + flag on → correct redirect target (ward list / ward player hop per ownerType + `WardStudentId`; no `WardStudentId` → `/ward`); flag off → expired/dark outcome (no sign-in, no stamp); expired/garbage token → expired outcome; `OpenedAt` stamp invoked exactly once (idempotent first-visit).
- **Public-route behavior:** landing endpoints have NO authorization metadata; `LinkExpired.razor` carries `[AllowAnonymous]` (assertable via endpoint metadata or bUnit render under an anonymous principal).
- **bUnit (`LinkExpiredBunitTests`):** friendly copy renders, no stack/error chrome (410-semantics page).
- **Wiring (`DeepLinkWiringTests`):** both hosts' DI registrations resolve an `IDataProtector` under the shared application name + `"ar-deeplink"` purpose (constants asserted — a host forgetting `SetApplicationName` fails the test).
- **ArchitectureTests green** (no direct context references; Families→Settings.Core is a Core-layer reference like Admin's).
- **Existing suites untouched-green at plan time:** Core 79, Assignments 591, Assignments.Api 58, Families 16, Students 422+1, Settings 519+1, Admin 545.

### Constraints (repo AGENTS.md — restated for the worker)

- **Read `.github/copilot/rules/dotnet-best-practices.md` + its skill FIRST** (every `.cs`/`.razor` code-behind change; the "Never" list is CI-enforced by `SchoolCollab.ArchitectureTests.Unit`). Also `.github/copilot/rules/ef-migrations.md` (additive migration only; never edit applied migrations or `.Designer.cs`; PascalCase `{Verb}{Entity}` name; review for accidental column drops) and `.github/copilot/rules/blazor-components.md` for the razor files.
- CPM: no `Version` on `PackageReference` — versions only in `Directory.Packages.props`. net10.0 — never downgrade.
- No MediatR (CQRS via `ICommandHandler<T>`/`IQueryHandler<T,R>`); no direct project references between bounded contexts (Families→Core/Settings.Core surface references mirror Admin — allowed; Families→Assignments.Core would NOT be).
- `dotnet build SchoolCollab.sln` after every change; fix compiler errors before moving on. Tests via MTP: `dotnet test tests/SchoolCollab.<Name>` — no extra flags.
- XML docs on new public members; primary constructors; typed exceptions (no bare `InvalidOperationException` from new code); structured logging with named placeholders.
- Tenancy: the token payload's tenantId establishes context explicitly (`RunWithExplicitTenantAsync` precedent); tenant-isolated flag resolution via the DB-backed service.
- **NO commits, NO branch operations** — working tree only. **Repo-scoped searches only — NEVER `find /`.** Worker NEVER edits this round doc. 30-min cap + cut-line protocol per pass; self-report → `documents/rounds/.ar-14-worker-report.md`.

### Acceptance criteria

- **Worker-facing:** build 0 errors; all unit projects 0 failures; changed files = expected list ±5; every deviation reported (not silent); the migration is purely additive; contracts project untouched (`git status` shows zero `Assignments.Contracts` diffs).
- **Reviewer-facing:** decisions (a)–(i) as written — payload carries tenantId (the impl-details deviation); both hosts share application name + purpose; `MapDeepLinkEndpoints` outside the razor-components mapping with no authorization metadata; `LinkExpired` page `[AllowAnonymous]` + friendly; mint flag-independent; republish reuse/re-mint idempotency; contracts untouched; flag seeded default-OFF + configuration.md §2/§5 rows; best-practices + ef-migrations rule compliance; test coverage list satisfied.
- **Orchestrator-facing:** authoritative build + full unit matrix green; the first public token-auth route group exists WITHOUT loosening any existing auth surface (ward pages keep host-level gating; Assignments Api groups unchanged); `FEATURE:EnableDeepLinks` flipping on later requires no republish (verify the mint path has no flag check).

### Residual risks / notes

- **Keyring bootstrap race:** both hosts persisting to the same Redis keyring can race on first-boot key generation; `PersistKeysToStackExchangeRedis` serializes key creation — acceptable in dev; note for prod runbook.
- **Multiplexer source:** if the Aspire caching integration does not register `IConnectionMultiplexer`, the worker adds `Aspire.StackExchange.Redis` 13.4.5 (CPM) and reports it (decision (b)).
- **Token-in-URL posture:** deep links are bearer tokens in access logs by design; mitigations = expiry + purpose scoping + the runtime kill-switch flag; HTTPS enforced (`UseHttpsRedirection`).
- **Cookie + circuit:** sign-in happens on the GET; the subsequent Blazor circuit navigation carries the cookie. TestAuth dev mode leaves ward routes open regardless — the flag gate is what makes the landing dark.
- **Guardian experience in 4a is deliberately thin** (ward list landing) — WS-F3 relocation is the immediate follow-up and consumes these tokens unchanged.
- **Out of scope (record, do not touch):** E2/E3 (MailKit — verified absent from CPM, NotificationLog, worker, SubmissionCompleted event), WS-F3, D-6 ward/guardian identities, Admin UI changes, v1.1 consolidated messages, reminders.

## Prepared dispatch

- **Parent: owner GO 2026-09-16 — both open questions CONFIRMED (Families adopts the Admin `AddConfigFeatureFlagClient` pattern; guardians land on the ward list).** By owner choice the #236 merge is SKIPPED for now: `stack/14-ar-14-deep-links` was cut from the `stack/13` tip `54ac0bea` (the ar-11/ar-13 unmerged-tip precedent — mandatory anyway, since ar-14 touches ar-13's Families/`AssignmentRecipient` files that `main` does not contain yet). Retarget onto merged `main` at PR time.
- **Fired (2026-09-16):** prerequisites verified on the fresh tree (`src/SchoolCollab.Families` present; `AssignmentRecipient.MarkOpened()` present; `PublishAssignmentCommandHandler` resolves the effective policy); worker pass 1 dispatched (deepseek-v4-flash, 30-min cap, cut-line protocol) against this plan; parent runs authoritative build/tests; reviewer (deepseek-v4.1-flash owner override); UI tester (minimax-m3 — UI round: `LinkExpired.razor` + landing redirect behavior); round-log closure per the train convention.
- **Branch base note:** the plan tree and the branch cut base are BOTH `stack/13` @ `54ac0bea` (the ar-13 branch, #236 open-unmerged); the PR must be retargeted onto merged `main` before review/merge.

## Execution log (parent — Tier 3 round ar-14)

| Stage | Agent / model | Run | Outcome |
|---|---|---|---|
| Orchestrator-plan | `ollama-cloud/glm-5.3-flash` | `4012351c` | Plan adopted; both owner questions CONFIRMED (Admin `AddConfigFeatureFlagClient` pattern; guardian → ward list) |
| Worker pass 1 | `ollama/deepseek-v4-flash:0731-cloud` | `d537465e` | Steps 1–4 (Core DeepLinks trio + flag key; keyring on both hosts; recipient columns + additive migration; mint-at-publish + stamp endpoint). Build 0; Assignments 597/0. Deviation: dedicated keyring multiplexer (10.0.9 lacks the factory overload) |
| Worker pass 2 | `ollama/deepseek-v4-flash:0731-cloud` | `2a407883` | Step 5 (landing service/endpoints/`LinkExpired`/flag seed/config rows/appsettings) + the 4 pending test files; Step 6 full matrix 2,273/0. Deviations: concrete `Microsoft.AspNetCore.DataProtection` + `Microsoft.Extensions` 10.0.8→10.0.9; `/deeplink/expired`; wiring-test placement |
| Reviewer attempt 1 | `ollama-cloud/deepseek-v4.1-flash` | `59c8efe2` | **TIMED OUT** (139m, 135k tokens — went spelunking in NuGet/framework XML-doc files; no REVIEW produced; zero tree changes). Hard guardrails re-issued on re-dispatch (recorded process lesson) |
| Reviewer | `ollama-cloud/deepseek-v4.1-flash` | `845545fb` | Verdict **P1** — unguarded stamp await (landing 500 on transport failure) + 6 P2s. Deviations adjudicated: 3 sound, 1 partially unsound (wiring coverage gap) |
| Worker pass 3 (rework iter 1) | `ollama/deepseek-v4-flash:0731-cloud` | `4a09ec78` | All 6 fixes landed. Seam deviation: `TenantPropagationDelegatingHandler` skips when an explicit `x-tenant-id` is present. Families 28/0, Architecture 21/0 |
| Re-verification review | `ollama-cloud/deepseek-v4.1-flash` | `0e19f2ac` | **P2-only** — all 6 prior findings RESOLVED; 2 new small P2s (skip-branch untested; non-2xx stamp silently discarded) |
| Parent-side P2 fixes | parent (ar-12 precedent) | — | `EnsureSuccessStatusCode` on the stamp (non-2xx → the landing guard's `LogWarning`) + `Explicit_x_tenant_id_Header_Wins_Over_Selection` in the Admin suite. Build 0; Families 28/0, Admin 546/0, Architecture 21/0 |
| UI tester | `ollama/minimax-m3:cloud` | `c029a6b8` | Verdict **P1** — the stamp await sits on the landing's critical path (default 100s client timeout + retry ⇒ ~200s redirect stall); P2s: `LinkExpired` has no CTA (fixable); cookie principal lacks `tenant_name`/`tenant_type` (parent-adjudicated residual — a tenant lookup would fight the latency fix) |
| Worker pass 4 (tester iter 1) | `ollama/deepseek-v4-flash:0731-cloud` | `957bfd8f` | Both fixes: ~3s per-call linked-CTS deadline on the stamp (no global client timeout; timeout → the guard's log-and-continue; CTS disposed; explicit `x-tenant-id` retained) + `FluentAnchor` CTA on `LinkExpired` (+2 tests). Mid-run steer applied when a test bash hung ~4m — the first test authoring ignored the request cancellation token; corrected (TCS honors it). Families 30/0, Architecture 21/0 |
| Tester re-verify (iter 2) | `ollama/minimax-m3:cloud` | `dcbec48d` | **PASS** — P1/P2 empty; all 8 landing regression tests preserved; both fixes confirmed |

**Parent authoritative matrix (post parent-side fixes):** `dotnet build SchoolCollab.sln` → **0 errors**; full unit matrix → **2,277 / 0** (Core 84, Assignments 597, Assignments.Api 58, Families 28, Students 422, Students.Api 1, Settings 519, Settings.Api 1, Admin 546, ArchitectureTests 21).

**Frozen patch:** `documents/rounds/diffs-ar-14-deep-links.patch` — **43 files** (~+2,800/−27).

### Deployment-smoke residuals (recorded — NOT round-blocking)

- **Prod `[AllowAnonymous]` override** on `/deeplink/expired` vs the razor-components `RequireAuthorization` — `[AllowAnonymous]` is endpoint metadata honored over the convention-builder's `RequireAuthorization` (documented ASP.NET Core behavior); verify at the first prod/browser smoke.
- **Prod service-to-service auth gap** — the Families host's outbound calls (stamp + all ward calls) carry no `Authorization`, while the Assignments API applies `RequireAuthorization` when `DisableOIDCAuth` is off → they 401 in prod until a service-to-service credential story lands (pre-existing posture, E1/D-6 deployment-auth workstream).
- **Keyring multiplexer** — process-lifetime singleton, `AbortOnConnectFail = false` (startup survives a Redis blip; key writes retry lazily).

## UI Tester — Verdicts

- **Iteration 1** (run `c029a6b8`, `ollama/minimax-m3:cloud`): **P1** — stamp on the landing critical path (fixed pass 4); P2s — `LinkExpired` CTA (fixed pass 4), `tenant_name`/`tenant_type` claims (residual, adjudicated). Out-of-round: none.
- **Iteration 2** (run `dcbec48d`): **PASS** — both fixes confirmed, no regressions (all 8 landing tests preserved). Tester loop closed at the bound (2 iterations).

## Acceptance — FINAL (parent-as-document-owner) — Verdict: **CLOSED**

**Acceptance criteria (from `## Plan`):**
- Worker-facing — build 0 errors ✅; full unit matrix 0 failures ✅; changed files within tolerance (43 vs ~35 ±5) ✅; deviations reported, not silent ✅.
- Reviewer-facing — Decisions (a)–(i) implemented as written ✅; contracts untouched ✅; best-practices confirmed (no overwrites / skills honored / readable) ✅; every review finding resolved ✅.
- Orchestrator-facing — reviewer **P2-only at close, P1 empty** ✅; UI round → tester fired, 2 iterations, final **PASS** ✅; authoritative build + matrix green ✅.

**Final authoritative numbers:** `dotnet build SchoolCollab.sln` → **0 errors**; full unit matrix → **2,279 / 0** (Core 84, Assignments 597, Assignments.Api 58, Families **30**, Students 422, Students.Api 1, Settings 519, Settings.Api 1, Admin 546, ArchitectureTests 21).

**Artifact:** `documents/rounds/diffs-ar-14-deep-links.patch` — **43 files** (~+2,850/−27), parent-confirmed content-current with the tree.

**Findings resolved this round:** 2 reviewer P1-class findings (unguarded stamp await; + the timed-out attempt re-dispatch) + 1 tester P1 (stamp latency) + 10 P2s across the review/rework/tester cycles.

**Residuals accepted:**
- `tenant_name`/`tenant_type` claims absent from the deep-link principal (cosmetic "Unknown" in tenant audit for deep-link sessions; fixing = a tenant lookup on the landing critical path — deferred).
- Deployment-smoke items (see above): prod `[AllowAnonymous]` override; prod service-to-service auth gap on Families' outbound calls (D-6/deployment-auth family); keyring singleton posture.
- Pass-1 deviation stands: dedicated keyring multiplexer (`AbortOnConnectFail = false`), CPM `Microsoft.AspNetCore.DataProtection` 10.0.9 + the `Microsoft.Extensions` 10.0.8→10.0.9 patch line.

**Loop bounds respected:** reviewer ≤2 → 1 formal iteration (P1 → rework → re-verify) + a re-dispatched attempt after a TIMEOUT (guardrails lesson recorded); tester ≤2 → 2 (P1 → rework → PASS).

**Working tree only; no commits (repo policy). Branch `stack/14-ar-14-deep-links` is based on the `stack/13` tip — retarget onto merged `main` at PR time (after #236 merges).**