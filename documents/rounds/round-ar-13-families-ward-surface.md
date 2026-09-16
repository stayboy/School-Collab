# Round ar-13-families-ward-surface — Phase 2 slice 2b: Families host (F1, owner Option B) + D2 ward player UI

Provider: pi. **EXECUTED 2026-09-15 as a FULL FOUR-AGENT round (Tier 3: orchestrator-plan → worker → reviewer → orchestrator-accept → UI tester) — owner choice ("fire tier 3 with four agent orchestrator").** **REVIEWER MODEL OVERRIDE (owner, 2026-09-15): `ollama-cloud/deepseek-v4.1-flash` REPLACES `ollama/kimi-k2.7-code:cloud`** (verified resolvable in models-store.json; saved as a standing preference). Ladder (registry-refreshed 2026-09-15): orchestrator `ollama-cloud/glm-5.3-flash` → worker `ollama/deepseek-v4-flash:0731-cloud` → reviewer `ollama-cloud/deepseek-v4.1-flash` (owner override) → tester `ollama/minimax-m3:cloud`. (The skill's pi defaults drifted: `ollama/glm-5.3-flash:cloud` no longer resolves — the ollama-cloud provider now hosts the modern ids.) **Round base: the `stack/12-ar-12-ward-gating-core` tip `abd70fce` (#235 unmerged at fire time — owner gave no merge instruction; ar-13 touches ar-12 files so it stacks on the tip, the ar-11 precedent). Branch `stack/13-ar-13-families-ward-surface` cut from it. Working tree clean at base (only untracked scratch).**

- **Owner structural decision (2026-09-15): F1 = Option B** — a new `SchoolCollab.Families` bounded-context **surface** app (host), NOT a full domain context (no Core/DB/Contracts of its own; the ward/assignment data stays in the Assignments context).
- **Tier check:** new project + AppHost + auth wiring + two UI pages + JS interop → Tier 3. Expected ~35 files. **ZERO migrations**, **ZERO new packages** (FluentUI + framework refs mirror Admin's csproj), **ONE new dev-default auth flag value** (the host's own `FeatureFlags:FEATURE:DisableOIDCAuth` appsettings entry — the established startup-switch pattern, NOT a new flag kind).
- **Sources:** `documents/specs/assignment-request-feature-spec.md` §3.3 (module sequence video → guide → questions; gating; feedback modes), §3.5 step 5 (Ward Completion), §2 personas, §6 (WCAG/captions context); `documents/solution/assignment-request-go-forward-breakdown.md` WS-F (F1/F2), WS-D2, §4 Phase 2 acceptance; skills `bounded-context` (BINDING template authority for the host + ApiClient + Blazor patterns), `use-js-interop`, `blazor-components`, `blazor-css-isolation`, `fluentui-*`, rule `.github/copilot/rules/blazor-components.md`.
- **Drift-refresh facts (verified 2026-09-15 against main `d2169aef` + #235):**
  - **No deep-link token exists anywhere** — `GetSignOffContextQuery(AssignmentId, StudentId)` takes no token; the sign page is Admin-hosted teacher/OIDC. Token minting/validation is **E1 (Phase 4)** — OUT of this round (decision (c)).
  - Admin's auth pattern (the template): `AddAuthAndTenancy(configuration)` (OIDC via Keycloak; dev TestAuthHandler reads tenant from Redis → tenant_id claim → TenantClaimsTransformation); `FEATURE:DisableOIDCAuth` is a **startup** switch read directly from IConfiguration (dev default "true"); `UseAuthentication()` always, `UseAuthorization()` only when OIDC is on; `AddConfigFeatureFlagClient` for runtime flags.
  - AppHost: `builder.AddProject<Projects.SchoolCollab_Admin>("admin")`-style registration (the worker copies the exact pattern incl. `.WithReference(assignmentsApi)` + https endpoint); runtime flags live in the Settings FeatureFlag aggregate — do NOT add AppHost flag params for anything runtime-mutable.
  - ApiClient pattern (skill §3.2): typed client, `client.BaseAddress = new Uri("https+http://assignments-api")` (Aspire service discovery), registered via an `AddFamiliesModule()`-style extension.
  - ar-12 seams this round binds to: `GET /students/{sid}/assignments` (ward list, `HasLockedModules`), `GET /{id}/students/{sid}/modules` (ward view, `QuestionsUnlocked` + `PercentComplete`), `POST /{id}/students/{sid}/modules/{moduleId}/progress`, `POST /{id}/students/{sid}/submission` (existing), `GET /{id}/students/{sid}/submission` (existing result). Contracts DTOs already cover these.
  - **Carried ar-12 P2 (A-1 cohesion):** `ListWardAssignmentsAsync` lives on `IModuleProgressRepository` — this round migrates it off (decision (g)).
  - `wwwroot/js/fileDownload.js` (ar-11) is the repo's only JS interop module; the video heartbeat is the second — same `use-js-interop` pattern.

## Plan

Stand up the Families host with in-app ward auth, then build the D2 ward player on top of the ar-12 seams, and pay down the carried ar-12 P2.

### Decisions (binding)

- **(a) Project shape.** New `src/SchoolCollab.Families/` Blazor Web App (the `bounded-context` skill's surface pattern): `Program.cs` + `Components/App.razor` (+ minimal `app.css`), `Components/Pages/Ward/Index.razor` (ward list), `Components/Pages/Ward/Assignment.razor` (the player), `Services/FamiliesApiClient.cs` (thin typed client over Assignments.Contracts DTOs — ONLY the six ward-facing calls above), `Services/FamiliesModuleServices.cs` (`AddFamiliesModule()`), `wwwroot/js/wardPlayer.js`. csproj references: `SchoolCollab.Assignments.Contracts`, `SchoolCollab.ServiceDefaults`, `SchoolCollab.Core` (auth primitives), FluentUI + Razor packages mirroring Admin's csproj (CPM — no Versions). It must NOT reference `Assignments.Application` (referencing the admin RCL would register every admin `@page` route in the Families host — that is the whole point of Option B).
- **(b) Host + AppHost wiring.** Families mirrors Admin's startup: `AddServiceDefaults()`, `AddAuthAndTenancy(configuration)`, the `FeatureFlags:FEATURE:DisableOIDCAuth` startup switch with dev default "true" (TestAuth) in its own `appsettings.json`, `UseAuthentication()` always / `UseAuthorization()` per switch, `UseAntiforgery()`. Render mode set once in `App.razor` (worker mirrors Admin's exact mode — reports a deviation if Admin's mode is unsuitable for the player). AppHost: `builder.AddProject<Projects.SchoolCollab_Families>("families").WithReference(assignmentsApi)` + https endpoint, following the Admin registration verbatim. Update `documents/configuration.md` §2 with the host's startup flag row (config-documentation rule).
- **(c) Auth scope for 2b — in-app only; tokens deferred to E1.** Ward pages are `AuthorizeView`/`[Authorize]`-gated in-app views (dev: TestAuth; prod: OIDC — ward identities arrive with D-6, recorded). **Deep-link token minting/validation is E1 (Phase 4)** — this round ships NO token code (the spec's "token-auth public routes" land with the notification links they authenticate). The API's ward routes keep their current authorization posture (recorded ar-12 residual); in dev the TestAuth mode reaches them; production re-scoping is E1's. This satisfies Phase 2's acceptance ("a ward authenticated in-app").
- **(d) D2 player — Ward/Assignment.razor.** Module sequence in `ContentModule` order: **video** = `<video controls>` embed for URL modules (+ `<track>` captions slot rendered when a captions asset exists — v1 has none, the render is captions-READY per §6; full WCAG is G1/Phase 5) with a watch-percentage heartbeat (JS `timeupdate` → throttled `POST progress`, the ar-12 monotonic/idempotent route makes retries safe); **guide** = rendered link/summary with a "Mark as read" affordance → `POST progress` 100; **question block** = visible but locked until `QuestionsUnlocked` is true (disabled inputs + hint text, the FR-220 hint precedent), then answers + submit → existing submission endpoints; **result** = auto-score display + pass/fail + retry state per policy (the ar-6 feedback envelope; feedback mode honors the assignment's grading format). `_busy`/`_error` reset in `finally` + `StateHasChanged()`; items-null loading pattern; `@key` on every `@foreach`.
- **(e) Ward list — Ward/Index.razor.** Binds the ar-12 ward list (title, due date, submission state, `HasLockedModules` badge) → navigates to the player. FluentUI components only; isolated `.razor.css` per the repo convention; no inline styles.
- **(f) JS interop.** `wwwroot/js/wardPlayer.js` follows the `use-js-interop` skill exactly (dynamic `import()`, module cached on the component, `IAsyncDisposable`, `JSDisconnectedException`-safe) — exposes `attachVideoHeartbeat(videoEl, moduleId, onTick)` returning a stop function; throttled to ~1 tick/10s. bUnit tests do NOT execute the JS (assert binding calls + gating; the JS is exercised by the tester).
- **(g) Carried ar-12 P2 (A-1 cohesion).** Migrate `ListWardAssignmentsAsync` OFF `IModuleProgressRepository` — worker picks the least-churn sound home (a new small read interface or `IAssignmentRepository` extension) and reports it as a deviation with rationale; `WardRoutesTests` must stay green untouched-semantics (route contracts unchanged).
- **(h) Out (record — do not touch):** token minting/validation + public token routes (E1); API ward-route production auth re-scoping (E1); sign-page relocation (F3, after E1); D-6 identity; WCAG audit beyond captions-ready rendering (G1); rubrics/comments (Phase 5); grade/group cross-context targeting (still recipient-link); ward-list N+1 (v1); the pre-round untracked scratch files.

### Expected files (~35, ±3)

**Created (~28):** `src/SchoolCollab.Families/SchoolCollab.Families.csproj`; `Program.cs`; `appsettings.json`; `Components/{App.razor, Routes.razor, App.razor.css or app.css}`; `Components/Pages/Ward/{Index.razor, Index.razor.css, Assignment.razor, Assignment.razor.css}`; `Components/Layout/MainLayout.razor` (+css if the skill template requires); `Services/{FamiliesApiClient.cs, FamiliesModuleServices.cs}`; `wwwroot/js/wardPlayer.js`; `Properties/launchSettings.json`; `tests/SchoolCollab.Families.Tests.Unit/` (project + `WardIndexBunitTests.cs` + `WardAssignmentPlayerBunitTests.cs`).
**Modified (~7):** `src/AppHost/SchoolCollab.AppHost/Program.cs` (+ generated `Projects` entry) + `appsettings.json` only if a parameter is genuinely needed (record); `Directory.Build.props`-adjacent nothing; `SchoolCollab.sln` (new projects); `documents/configuration.md` §2 (startup flag row); `src/Assignments/.../IModuleProgressRepository.cs` + the read's new home + its DI registration (decision (g)); possibly `AssignmentRoutes.cs` (only if decision (g) moves a seam).

### Implementation steps (ordered; `dotnet build SchoolCollab.sln` after every step; multi-pass expected — cut-line protocol per pass)

1. **Host skeleton + auth:** Families project (csproj/Program/App/Routes/layout) + AppHost registration + FamiliesApiClient (six calls) + FamiliesModuleServices + launchSettings + sln entries. Build; host starts; smoke test the client wiring via a bUnit page-shell test.
2. **Ward list page:** Index.razor + css binding the ward list via the client. Build; `dotnet test tests/SchoolCollab.Families.Tests.Unit` (list bUnit cases green).
3. **Player page:** Assignment.razor + css — module sequence, gating bindings, question block, submit + result. Build; player bUnit cases green.
4. **JS heartbeat:** `wardPlayer.js` + the video heartbeat wiring (use-js-interop pattern). Build; binding-level bUnit case green.
5. **Cohesion migration (g):** move the ward-list read; Assignments.Api.Tests.Unit + Assignments.Tests.Unit stay green.
6. **Full matrix + freeze:** `dotnet build SchoolCollab.sln` (0 errors) + the NINE unit projects (eight + the new Families one — CI's `~Tests.Unit` filter picks it up automatically) + `git add -N -- src tests` + parent freezes the patch + worker self-report.

### Binding test coverage list (MSTest + FluentAssertions; bUnit + Moq)

- `WardIndexBunitTests`: list renders rows from a mocked `FamiliesApiClient`; `HasLockedModules` badge truth (shown/hidden); empty + loading states (items-null pattern).
- `WardAssignmentPlayerBunitTests`: modules render in order; question inputs disabled + hint shown when `QuestionsUnlocked == false`; enabled + submittable when true (gates on the mock DTOs); submit invokes the client with the composed answers; result view binds score/passed/retry-state; `_busy`/`_error` reset on failure (ar-8/ar-9 fix class); heartbeat binding attaches per video module (mock `IJSRuntime` — JS itself not executed).
- Families module wiring: `AddFamiliesModule` registers the typed client with the service-discovery base address (assert on `HttpClient.BaseAddress`).
- Existing suites untouched-green: Assignments.Tests.Unit 591/0, Assignments.Api.Tests.Unit 58/0 (decision (g) keeps them green), ArchitectureTests 20/0 (the new host's references satisfy the no-direct-context-reference rule — Contracts references are the designed seam).

### Constraints (repo AGENTS.md — restated for the worker)

CPM (no Version on PackageReference). net10.0. **Read the `bounded-context` skill FIRST — it is the template authority** for the host, ApiClient, and every Blazor page pattern (items-null loading, no per-page `@rendermode`, FluentUI only, `@key` on `@foreach`, `_loadCts`/dispose shape). Also `use-js-interop` + `blazor-css-isolation` + `fluentui-*` skills. No MediatR. MTP invocation `dotnet test tests/SchoolCollab.<Name>` NO extra flags. `dotnet build SchoolCollab.sln` after every change. **No git commits — working tree only.** XML docs on new public members; primary constructors; typed exceptions; no `InvalidOperationException` from new code; structured logging with named placeholders. **Repo-scoped searches only — NEVER `find /`.** Worker NEVER edits this round doc; self-report → `documents/rounds/.ar-13-worker-report.md`. 30-min cap + cut-line protocol per pass.

### Acceptance criteria

Worker-facing: build 0 errors; the nine unit projects 0 failures; changed files = expected list (±3); deviations reported, not silent.
Reviewer-facing: decisions (a)–(g) as written; Families references Contracts-not-Application (verify no admin route leakage — `Routes`/`App.razor` must not import admin namespaces); auth mirrors the Admin startup switch exactly; the player binds ar-12 DTOs without contract changes; JS interop follows the skill; best-practices check.
Orchestrator-facing: authoritative build + matrix green; reviewer PASS or P2-only triaged; **UI round → tester pass fires** (scope handover derived from the changed-file list: Ward Index + Assignment pages, wardPlayer.js, the FamiliesApiClient methods, navigation entries); P1 empty → CLOSED.

### Residual risks / notes

- The player's production auth story (ward OIDC identities) awaits D-6; deep links await E1 — both recorded so the tester doesn't bug-hunt them as defects (handover "intended, not bugs").
- Video modules are URL-based today; stored-file video playback via `IFileStore` is future work (record).
- `wardPlayer.js` is the repo's second interop module — if FluentUI's video/web-component integration collides, a plain `<video>` element is acceptable v1 (worker reports the choice).
- New test project must land in `SchoolCollab.sln` and be named `SchoolCollab.Families.Tests.Unit` so the CI filter picks it up.

## Prepared dispatch (parent — awaits #235 merge + owner mode menu)

1. ⏳ Merge #235 (ar-12) into main (owner instruction; CI green at plan time).
2. ⏳ Cut `stack/13-ar-13-families-ward-surface` from `main` (or the `stack/12` tip if #235 is still open).
3. ⏳ **Execution-mode menu to the owner** — parent recommends full four-agent (Tier 3).
4. ⏳ Worker task = decisions + steps + binding list + constraints above.

### Orchestrator plan refinements (2026-09-15)

none — plan adopted as authored.

Drift-refresh verification (repo-scoped greps against the `stack/13` working tree @ `abd70fce`, repo-scoped only): all facts hold —
- Admin `Program.cs` auth shape confirmed verbatim: `AddAuthAndTenancy(builder.Configuration)`; `DisableOIDC` parsed directly from `FeatureFlags:{FeatureFlagKeys.DisableOIDCAuth}` IConfiguration (startup switch, NOT via IFeatureFlagService); `UseAuthentication()` always; `UseAuthorization()` only `if (!disableOIDC)`; `AddConfigFeatureFlagClient`; `UseAntiforgery()`.
- Admin csproj package list confirmed: `Microsoft.FluentUI.AspNetCore.Components` + `.Icons` + `Aspire.StackExchange.Redis.DistributedCaching` (all versionless per CPM) + project refs incl. `SchoolCollab.Core`; no WASM packages (Interactive Server only).
- Admin `App.razor` render mode confirmed: `@rendermode="new InteractiveServerRenderMode(prerender: false)"` on both `HeadOutlet` and `Routes` — set once, no per-page `@rendermode` anywhere.
- AppHost registration confirmed: `builder.AddProject<Projects.SchoolCollab_Admin>("admin").WithReference(assignmentsApi)…` (lines 217–225, with `FeatureFlags__FEATURE__…` `WithEnvironment` fanout + `WaitFor` chain) — the Families registration copies this shape.
- ar-12 ward seams confirmed in `AssignmentRoutes.cs` (ward group `MapGroup("/students")` → `GET /students/{studentId}/assignments`; assignments group → `GET /{id}/students/{studentId}/modules`, `POST …/modules/{moduleId}/progress`, `POST …/submission`, `GET …/submission` returning `SubmissionDetailDto`). Contracts DTOs confirmed: `WardAssignmentListItemDto` (`HasLockedModules`, `WardSubmissionStateDto`), `WardAssignmentViewDto` (`QuestionsUnlocked`), `WardModuleViewDto` (`PercentComplete`, `MinCompletionThresholdPercent`, `Url`, `ModuleType`), `RecordModuleProgressRequest(int Percent)`; result score/passed per version on `SubmissionVersionDto` (`Score`, `Passed`) + `CurrentVersionNumber` — retry state is worker-derivable, no contract change needed.
- Cohesion carry-over confirmed real: `ListWardAssignmentsAsync` lives on `IModuleProgressRepository`/`ModuleProgressRepository` and is consumed by `ListWardAssignmentsQueryHandler` — decision (g) applies.

### Worker task (dispatch spec)

**Role:** Implement round ar-13-families-ward-surface exactly as planned. This round doc IS the plan — read it completely first (decisions (a)–(h), expected files, steps 1–6, binding test list, constraints, acceptance criteria); do not re-plan.

**Working state:** branch `stack/13-ar-13-families-ward-surface` @ base `abd70fce`; working tree has untracked scratch only. Code changes stay uncommitted in the working tree.

**Self-report:** write progress + deviations to `documents/rounds/.ar-13-worker-report.md` (scratch, untracked). **NEVER edit this round doc** (`documents/rounds/round-ar-13-families-ward-surface.md`) — it is orchestrator-owned.

**Binding guards:**
- **No git commits, pushes, or branches** — working tree only; the parent owns VCS actions.
- **CPM**: no `Version` attribute on any `PackageReference`; new package versions only via `Directory.Packages.props` (this round should need ZERO new packages — mirror Admin's csproj).
- **Skills to read FIRST and follow exactly:** `.github/skills/bounded-context/SKILL.md` (template authority for host + ApiClient + every Blazor page pattern: items-null loading, `_loadCts`/`_disposed` dispose shape, optimistic mutations, no per-page `@rendermode`, FluentUI only, `@key` on `@foreach`) + `.github/copilot/rules/blazor-components.md` + `use-js-interop` + `blazor-css-isolation` + `fluentui-*` skills.
- **MTP test invocation**: `dotnet test tests/SchoolCollab.<Name>` with NO extra flags (no `--filter`, no verbosity).
- **`dotnet build SchoolCollab.sln` after every change** (every edit touching `.cs`/`.razor`/`.csproj`/`*.props`); fix compiler errors before moving on. On MSB3021/MSB3027 file locks, surface the lock in the self-report instead of retrying.
- **Repo-scoped searches only — NEVER `find /`** or system-wide greps.
- **Repo conventions:** net10.0; no MediatR; XML docs on new public members; primary constructors; typed exceptions (no `InvalidOperationException` from new code); structured logging with named placeholders; no `Console.WriteLine`.
- **30-minute cap per pass + cut-line protocol**: if the cap hits, stop at a build-green cut line, record remaining work in the self-report, and return.

**Step order (from the plan — do not reorder; build after each):**
1. Host skeleton + auth (Families project, AppHost registration mirroring the Admin shape, FamiliesApiClient six calls, FamiliesModuleServices, launchSettings, sln entries; bUnit page-shell smoke test).
2. Ward list page (`Ward/Index.razor` + css; list bUnit cases green).
3. Player page (`Ward/Assignment.razor` + css — module sequence, gating, question block, submit + result; player bUnit cases green).
4. JS heartbeat (`wwwroot/js/wardPlayer.js` + video wiring per `use-js-interop`; binding-level bUnit case; JS itself not executed in tests).
5. Cohesion migration (decision (g): move `ListWardAssignmentsAsync` off `IModuleProgressRepository`, report the chosen home as a deviation with rationale; Assignments suites stay green).
6. Full matrix + freeze: `dotnet build SchoolCollab.sln` 0 errors + the NINE unit projects 0 failures + `git add -N -- src tests` + finalize the self-report for the parent.

**Do NOT:** dispatch anything, run the authoritative test matrix for the round record, touch files outside the expected list (±3, report as deviation), or edit the round doc.
## Review (static reviewer — `ollama-cloud/deepseek-v4.1-flash`, owner override; run `c19ab888`)

**Verdict: P1** (block — 1 functional defect in the delivered D2 heartbeat + 3 binding-plan items unmet).

Verified correct by the reviewer: constraint hard check (Families csproj references only ServiceDefaults + `Assignments.Contracts` + `SchoolCollab.Core`; no `Assignments.Application`; no admin route leakage — no `AddAdditionalAssemblies` in `Routes.razor`/`Program.cs`); AppHost registration mirrors the Admin block; sln entries; CPM clean; step-5 cohesion migration body byte-identical to the deleted implementation (tenant filter + `AsNoTracking` preserved), DI + handler swapped, 3 sanctioned test-fake stubs removed, no dangling members/fakes, `ZzDiagTests.cs` absent from patch and tree; no deletions/renames of pre-existing files (21 new, 9 modified = no overwrites); the pass-3 load fix is correct (ordering, `_loadedFor` guards, stale-state reset, reachable "no ward selected" branch, `TryGetValue` parse); JSON options threaded consistently through all six calls; bUnit fixtures mirror the repo's working pattern.

**P1 (blocking — all four verified by the parent):**
1. `Components/Pages/Ward/Assignment.razor:168` — video heartbeat attach is **unreachable**: the guard needs `_view` non-null *on the first render*, but render #1 always precedes the awaited load (`CallOnParametersSetAsync` calls `StateHasChanged()` before awaiting), so `firstRender` is already false when `_view` arrives → D2 watch-percentage reporting never happens.
2. `wwwroot/js/wardPlayer.js:12` + `Assignment.razor:314` — **decision (f)'s stop/detach function is missing**: the module exports only `attachVideoHeartbeats`; listeners are never removed (repo precedent `chatInput.js:135` detachKeydownHandler), and `DisposeAsync` only disposes the module reference.
3. `documents/configuration.md:350` (+ the §5 consumer list at `:127`) — the Families host is **not** recorded as a `FEATURE:DisableOIDCAuth` consumer, though the plan lists this file as modified (decision (b)).
4. `tests/SchoolCollab.Families.Tests.Unit` — **four mandated binding cases missing**: `AddFamiliesModule` base-address wiring; heartbeat attach per video module; result view binding score/passed/retry-state; `_busy`/`_error` reset on failure.

**P2 (parent adjudication in brackets):**
- `Index.razor:19` — error path leaves the spinner spinning [**in rework**: real UX bug — error branch sits behind the loading branch]
- `WardIndexBunitTests.cs:70` — vacuous badge assertion [**in rework**: ar-12 precedent, a test must assert real behaviour]
- `Assignment.razor:155` — stale `_view` on parameter change [**in rework**: reset with the other stale state]
- `app.css:1` — Razor comment (`@* *@`) inside CSS [**in rework**: invalid CSS comment form]
- missing `ErrorBoundary` in `Routes.razor` + absent `Index.razor.css` (plan expected file) [**in rework** if the plan/Admin precedent mandates them; else record as deviation]
- `Assignment.razor:180` sequential independent awaits; `:250` mutation reusing `_loadCts` [residual — nits, no behavioural defect]
- `Assignment.razor:43/52/245` comment / locked-input / discarded-feedback deviations [residual — declared v1 contract deviations]
- "global-namespace `FamiliesApiClient`/`FamiliesJson`" [**CORRECTED at re-verification — NOT a false positive.** The parent discarded it from a read taken *before* the rework rewrote that file; `FamiliesApiClient.cs` currently has **no** namespace declaration, so both types compile into the global namespace. Re-opened as a P2 in rework iteration 2.]
- plan references `.github/skills/use-js-interop`, which does not exist in the repo [plan-template defect: it is a *global* skill; recorded separately]

**Rework:** iteration 1 of ≤2 (Tier 3 bound). Dispatched as worker pass 4 with the failing items + patch path only.

## Execution log (parent — Tier 3 round)

**Base:** `stack/12-ar-12-ward-gating-core` tip `abd70fce` (#235 unmerged at fire time). Branch `stack/13-ar-13-families-ward-surface`. Registry refresh note: the skill's pi defaults had drifted — orchestrator now `ollama-cloud/glm-5.3-flash` (the old `ollama/glm-5.3-flash:cloud` no longer resolves); reviewer `ollama-cloud/deepseek-v4.1-flash` per owner override; `references/models.md` updated in the same round.

| Stage | Agent / model | Run | Outcome |
|---|---|---|---|
| Orchestrator-plan | `ollama-cloud/glm-5.3-flash` | `6b8bf744` | Plan **adopted as authored** (0 refinements); all drift facts verified against the tree; worker dispatch spec authored |
| Worker pass 1 | `ollama/deepseek-v4-flash:0731-cloud` | `428e6cdd` | Killed by the 30-min workflow cap mid-step (no self-report). Landed: Families host skeleton, AppHost/sln/csproj, tests project |
| Worker pass 2 | `ollama/deepseek-v4-flash:0731-cloud` | `f3008319` | Reconcile pass: made pass-1 work build-coherent, added missing `wwwroot/css/app.css`, **completed step 5** (cohesion migration via dedicated `IWardAssignmentProjectionRepository`; `ListWardAssignmentsAsync` removed from `IModuleProgressRepository`; DI + handler swapped; 3 dead test-fake stubs removed). Build 0 errors; Assignments 591/0, Assignments.Api 58/0. **Families 8/8 FAIL** — recorded, not fixed (cap) |
| Parent diagnosis + steer | parent | steer `46505f87` | Root-caused the 8/8 failure: `ComponentBase` first-render order is parameters → `OnInitializedAsync` → `OnParametersSet`, but both pages resolved the ids in `OnParametersSet` and consumed them in `OnInitializedAsync` → load skipped, perpetual spinner, zero re-renders, no error. Steered the running worker with the exact fix (the worker's fixture-gap hypothesis was wrong — the bUnit fixture was already correct) |
| Worker pass 3 | `ollama/deepseek-v4-flash:0731-cloud` | `30cf8627` | **Step 6 complete.** Moved both page loads to `OnParametersSetAsync` + `_loadedFor` reload guards; fixed the query-parse `KeyNotFoundException`; fixed a **second real defect** (client deserialized with default JSON options while the API emits string enums → string-enum options threaded through every read/write); deleted pass-1 scratch `ZzDiagTests.cs`. **Families 8/0**, full matrix 1,699/0, build 0 errors |

**Parent authoritative pass (round record):** `dotnet build SchoolCollab.sln` → **0 errors**. Nine unit projects → **1,699 tests / 0 failures** (Core 79, Assignments 591, Assignments.Api 58, Students 422, Students.Api 1, Settings 519, Settings.Api 1, ArchitectureTests 20, Families 8). Note: `ArchitectureTests` intermittently reports MTP `total: 0, exit=5` ("zero tests ran") inside a sequential batch; a standalone re-run confirms 20/0.

**Frozen patch:** `documents/rounds/diffs-ar-13-families-ward-surface.patch` — 30 files, +1,200/−42 (`git add -N -- src tests`, then `git diff -- src tests`).

**Reviewer:** `ollama-cloud/deepseek-v4.1-flash` (owner override) — static, reads the frozen patch + plan only.

### Escalation-clause deviation (recorded per pitfall #1 — "record escalations in the round doc")

Worker pass 1 was terminated by the 30-minute cap — an explicit **"times out"** trigger of the build-escalation pattern (`SKILL.md` step 3, lines 151–164). Per that clause the SAME pass scope should have been re-dispatched as an **escalation pass on the reviewer/escalator model** (`ollama/kimi-k2.7-code:cloud`; under the owner's 2026-09-15 reviewer override the escalator slot is `ollama-cloud/deepseek-v4.1-flash`), with the resulting work **statically re-verified by the HIGHER model `ollama/glm-5.3:cloud`**. The parent instead ran pass 2 (and pass 3) on the worker model and is verifying with the standard reviewer. **Deviation, logged; the higher-model re-verification is owed unless the owner waives it.** Note: both escalation ids (`ollama/kimi-k2.7-code:cloud`, `ollama/glm-5.3:cloud`) still resolve in the active registry — only the orchestrator default `glm-5.3-flash:cloud` had drifted.

### Rework iteration 1 (worker pass 4 — run `d746cbe2`, `ollama/deepseek-v4-flash:0731-cloud`)

Mid-run steer: the pass had gone down a PowerShell/assembly-metadata rabbit hole (4 min on one bash call, exit 1) trying to reverse-engineer bUnit's JS-invocation API. Steered to the documented surface (`JSInterop.Invocations`; `JSInterop.SetupModule(...)` + `VerifyInvoke`; sanctioned fallback = assert the recorded wardPlayer.js import for a Video-module view with a no-video negative case) plus a hard rule: any sub-item resisting >3 min gets landed-as-is and reported.

**P1 fixed (all parent-verified in the tree):**
1. `Assignment.razor:138/166/178/181` — `_heartbeatsAttached` one-shot gate replaces the unreachable `firstRender` condition (reset with the load key at `:166`); `DisposeAsync:333` tears down.
2. `wardPlayer.js:44` — `detachVideoHeartbeats()` exported (registry + `removeEventListener`); `Assignment.razor:335` invokes it **before** `_playerModule.DisposeAsync()` (`:338`), `JSDisconnectedException`-safe.
3. `configuration.md:127` + `:350` — `SchoolCollab.Families` added to both consumer enumerations.
4. Families tests 8 → **13**: `AddFamiliesModule_ConfiguresAssignmentsApiBaseAddress`, `VideoModule_RecordsHeartbeatImport_ForVideoModule`, `NoVideoModule_DoesNotRecordHeartbeatImport`, `ResultView_Binds_ScoreAndPassState`, `LoadFailure_SurfacesError_NoPerpetualSpinner`.

**P2 fixed:** `Index.razor` error-path reachability; `WardIndexBunitTests` badge assertion made real; `Assignment.razor` stale `_view` reset on key change; `app.css` CSS comment; `Index.razor.css` added. **Deviation-not-taken:** ErrorBoundary omitted — Admin has none (rationale sound; mirror if Admin ever adds one). **Residuals untouched as instructed:** sequential awaits, `_loadCts` reuse in a mutation, the declared v1 deviations.

**Parent authoritative re-matrix (post-rework):** `dotnet build SchoolCollab.sln` → **0 errors**; nine unit projects → **1,704 tests / 0 failures** (Core 79, Assignments 591, Assignments.Api 58, Students 422, Students.Api 1, Settings 519, Settings.Api 1, ArchitectureTests 20, Families **13**). MTP batch quirk recurred for ArchitectureTests (`total: 0, exit=5`) — confirmed 20/0 in an isolated run.

**Re-frozen patch:** `documents/rounds/diffs-ar-13-families-ward-surface.patch` — 32 files, +1,380/−42.

**Re-verification:** fresh static reviewer run (`ar13-reviewer-reverify`, `ollama-cloud/deepseek-v4.1-flash`) on the re-frozen patch.

### Re-verification review (run `adbf31a2`, `ollama-cloud/deepseek-v4.1-flash`) — Verdict: **P1**

**Prior P1s adjudicated:** P1-2 (JS detach) and P1-3 (configuration.md rows) **genuinely resolved**; the P1-1 one-shot-flag gate logic and P1-4 test set **correct at component level** — but P1-1/P1-2 are **defeated at runtime by a new P1**:

- **P1 (new, real):** `Assignment.razor:230` (+ `WardAssignmentPlayerBunitTests.cs:49`) imports the heartbeat module as `"./_content/SchoolCollab.Families/js/wardPlayer.js"`. `SchoolCollab.Families` is the **host app, not an RCL**, so that `_content/` route does not exist — the host asset is served at the app root (`staticwebassets.build.json` = Root mode, route `js/wardPlayer.js`; zero `_content/SchoolCollab.Families/` entries; Admin shows the host-vs-RCL split). The import 404s in a browser and the failure is swallowed → **the D2 watch-percentage heartbeat still never attaches**. Fix: `"./js/wardPlayer.js"` in both places.

**P2s (adjudicated into rework iteration 2):** badge assertion still permits an inverted mapping (must assert per row); mandated case (d) covers only the load-failure path, not the failed-**mutation** busy restoration; `DisposeAsync` detaches only while `_heartbeatsAttached` is true, so a video → video-less parameter change leaves stale registry entries (gate on `_playerModule`/an ever-attached flag); missing namespace in `FamiliesApiClient.cs` (**the parent's earlier "false positive" discard was wrong — corrected above**); redundant self-`using` in `FamiliesModuleServices.cs`; stray `@` in `Index.razor:42/54`. Reviewer also confirmed the ErrorBoundary deviation rationale is sound (Admin's `Routes.razor` has only `RouteView` + `FocusOnNavigate`) and that the prior P1 fixes introduced no overwrites/regressions.

**Process defect found by the reviewer (fixed at freeze):** the frozen patch used `git diff -- src tests`, so `documents/configuration.md` and `SchoolCollab.sln` changes were **not in the artifact** and P1-3/sln could not be substantiated from it. The re-freeze after pass 5 must use the full working-tree scope.

### Rework iteration 2 (worker pass 5 — run `d4e520dc`, workflow `910a275b`, `ollama/deepseek-v4-flash:0731-cloud`)

Fixes (parent-verified in the tree; worker self-report PASS 5 section in `documents/rounds/.ar-13-worker-report.md`):

- **P1** — heartbeat import path `"./_content/SchoolCollab.Families/js/wardPlayer.js"` → `"./js/wardPlayer.js"` in `Assignment.razor` (:238) and the bUnit constant (`WardAssignmentPlayerBunitTests.cs:52`). `SchoolCollab.Families` is a host app, not an RCL — the asset is root-served (`staticwebassets.build.json` Root mode), so the `_content/` route 404s and the watch-percentage heartbeat never attached.
- **P2** — badge mapping assertion now per-row positional (an inverted mapping fails); new `SubmitFailure_RestoresIdleButton_ShowsError` covers failed-**mutation** busy restoration (was load-failure only); `DetachVideoHeartbeatsAsync` gated on `_playerModule` (drops the `_heartbeatsAttached` gate) so a video → video-less parameter change clears stale registry entries; `FamiliesApiClient.cs` wrapped in `namespace SchoolCollab.Families.Services`; redundant self-`using` removed from `FamiliesModuleServices.cs`; stray `@` removed in `Index.razor`.
- **Deviation-not-taken (recorded):** file/type naming (`FamiliesModuleServices.cs`/`ModuleServices`) left as-is; the wiring test locates the named client via `.Name` (was `.FullName`, which broke once the namespace was added).
- **Tests:** Families 13 → 14.

**Parent authoritative re-matrix (post-pass-5, re-run on session restore 2026-09-16):** `dotnet build SchoolCollab.sln` → **0 errors**. Full unit matrix → **2,250 / 0**: Core 79, Assignments 591, Assignments.Api 58, Families **14**, Students 422, Students.Api 1, Settings 519, Settings.Api 1, Admin 545, ArchitectureTests 20. *(The round's earlier "nine-project" tally omitted the pre-existing `SchoolCollab.Admin.Tests.Unit`, which CI's `FullyQualifiedName~Tests.Unit` filter does include — recorded.)*

**Re-frozen patch (full working-tree scope — reviewer process defect fixed):** `documents/rounds/diffs-ar-13-families-ward-surface.patch` — **35 files**, content-identical modulo CRLF to a fresh `git diff` over `src tests SchoolCollab.sln documents/configuration.md .pi/skills/orchestrator-worker-reviewer/references/models.md` (+1,380/−42).

**Session restore note (2026-09-16):** the 2026-09-15 round session terminated immediately after the owner's "save current state" (empty assistant response at 22:45, then a model switch to `ollama-cloud/glm-5.3-flash`). All pass-5 work, the re-frozen patch, and the working tree were intact. On restore the parent re-verified the build + full matrix above and confirmed the frozen patch content *matches* the live tree before dispatching the final static re-verification.

### Final re-verification review (run `a829de16-7dd3-46b9-ad9f-bb2dc6f45233`, `ollama-cloud/deepseek-v4.1-flash`) — Verdict: **P2-only** (P1: none)

**Prior findings — all 7 adjudicated RESOLVED:** P1 `_content/…` import → `./js/wardPlayer.js` (`Assignment.razor:238` + test constant `:52`); badge mapping asserted per-row; failed-mutation busy-restoration test added; detach gated on `_playerModule` on every key change + `DisposeAsync`; `FamiliesApiClient` namespaced; redundant `using` removed; stray `@` removed. **No regressions** from pass 5.

**P2s (new/remaining):**
1. `Program.cs:52` — `UseExceptionHandler("/Error")` has no target in this host (Admin's `Error.razor` lives in `SchoolCollab.Admin.Shared`, deliberately unreferenced) → non-Development 500 re-executes to an unmatched route (blank page).
2. `Assignment.razor:23` + `Index.razor:16` — no page-level `<ErrorBoundary>`, contradicting `.github/copilot/rules/blazor-components.md` ("wrap the primary content of every page") and the repo-wide precedent (`SignOff.razor:19`, `Students/Detail.razor:23`, `CodedValues/Edit.razor:12`; list pages inherit it via `LandingPage.razor:10`). The earlier deviation-not-taken cited the router level, which is **not** what the rule mandates.
3. `documents/configuration.md:127` — §2 row over-claims injection into `families`; the AppHost intentionally does not inject it (`Program.cs:229-233`) and §5 (`:350`) is the accurate half.
4. `Assignment.razor:299,325` — mutations reuse `_loadCts`; `TryLoadResultAsync:214` / `MarkModuleReadAsync:250` dereference `_loadCts!.Token` → NRE after disposal / `ObjectDisposedException` on a mid-mutation parameter change. Violates blazor-components.md "loads vs. mutations".
5. `Assignment.razor:181-183` — independent ward-view + submission GET awaited sequentially (blazor-components.md "Parallel data loading").
6. `Assignment.razor:149-150` — all-zero GUID URLs hit the `Guid.Empty` early return leaving `_view`/`_error` null → perpetual spinner with no message (same defect class fixed in `Index.razor` in rework 1).
7. `Assignment.razor:266` — `[JSInvokable("onVideoTick")] OnVideoTickAsync(Guid,int,CancellationToken ct=default)` is invoked from `wardPlayer.js:41` with 2 args; unverified whether the trailing token breaks the JS→.NET binding, and any rejection is swallowed by the JS `.catch` → heartbeat fails silently. (Verification ask; nothing needs the token.)
8. `.pi/skills/…/models.md:12-35` — patch touches a pre-existing round-process doc outside the plan's expected files (intentional; report-only — consider excluding from the feature patch).
9. `Index.razor:53` / `Assignment.razor:250` — `ReportModuleProgressAsync` returns `false` on non-204 and both call sites discard it → silent progress-report rejection (declared v1 deviation).

**Best-practices:** no overwrites / skills honored / readable — plan (a)–(g) as written (Contracts-not-Application; no admin route leakage; AppHost `.WithReference(assignmentsApi)` satisfies `CrossModuleWiringTests`; ar-12 DTOs untouched; six client paths byte-match API templates; no pre-existing file deleted/renamed/reformatted; CSS isolation + FluentUI-only + `@key` + items-null + interop pattern honored).

## Acceptance — draft triage (superseded by the FINAL verdict below)

- **P1: none → not blocked.**
- **Triage:** items **1–7 are material** (repo-rule violations or real user-facing/runtime defects) → **bounded cleanup pass 6** (same scope, worker model). Item **8** report-only (kept — round-process doc). Item **9** recorded residual (declared v1 deviation, no fix this round).
- **After cleanup:** re-freeze patch → parent `dotnet build` + full unit matrix → UI tester pass on the delivered ward surfaces (handover below).
- **Round-doc ownership note:** this resumed session's parent writes the round doc (the 2026-09-15 orchestrator run is not re-fired); all durable state was already on disk, so resuming is the cheap path per the skill's `workflowScript`-continuation pitfall.

## UI Tester handover (Tier 3, UI round — trigger: `.razor`/`.razor.css`/`.css`/`.js` changed)

Hunt exactly these surfaces; do not derive or expand scope:

- **Pages** — `src/SchoolCollab.Families/Components/Pages/Ward/Index.razor` (routes `/ward`, `/ward/{StudentId:guid}`): ward-list rows, due date, submission state, `HasLockedModules` badge, empty/loading/error states. `…/Ward/Assignment.razor` (route `/ward/{StudentId:guid}/assignments/{AssignmentId:guid}`): D2 player — module sequence (video → guide → questions), question gate on `QuestionsUnlocked`, submit, result view, `_busy`/`_error` reset, video heartbeat + JS interop, back-navigation.
- **Layout/host/CSS** — `Components/Layout/MainLayout.razor`(+css), `Components/App.razor`, `Components/Routes.razor`, `wwwroot/css/app.css`, `Pages/Ward/Index.razor.css`, `Pages/Ward/Assignment.razor.css`.
- **JS** — `wwwroot/js/wardPlayer.js` (`attach`/`detach` heartbeat; the `onVideoTick` callback contract vs the .NET handler signature — confirm the heartbeat actually reports).
- **Client methods they call** (`Services/FamiliesApiClient.cs`): `ListWardAssignmentsAsync` (GET `/students/{sid}/assignments`), `GetWardAssignmentViewAsync` (GET `/{id}/students/{sid}/modules`), `ReportModuleProgressAsync` (POST `/{id}/students/{sid}/modules/{moduleId}/progress`), `SubmitAsync` (POST `/{id}/students/{sid}/submission`), `GetSubmissionAsync` (GET `/{id}/students/{sid}/submission`).
- **Navigation entry** — `/ward` list → player route.
- **Intended, NOT bugs** — ward OIDC identity / deep-link tokens (E1), sign-page relocation (F3), WCAG beyond captions-ready rendering (G1), rubric/comment UI (Phase 5), stored-file video playback (future).

### Cleanup pass 6 (worker — `ollama/deepseek-v4-flash:0731-cloud`, run `d62a221a`)

All 7 material P2s implemented (parent-verified in the tree):

1. Page-level `<ErrorBoundary>` added to both `Ward/Assignment.razor` (`:10-130`) and `Ward/Index.razor` (`:13-68`), FluentUI `FluentMessageBar` error content, mirroring the `SignOff.razor` precedent; inner error surfaces retained.
2. **Option A** chosen — new host-local `Components/Pages/Error.razor` (`@page "/Error"`, FluentUI-only, no `Admin.Shared` dependency); `UseExceptionHandler("/Error")` now has a target without a `Program.cs` edit.
3. Separate `_mutationCts` (`Assignment.razor:136`) — per-mutation create/dispose in `finally`; `MarkModuleReadAsync`/`SubmitAsync`/`RefreshResultAsync` use it; `TryLoadResultAsync` now takes a token (load path passes `_loadCts.Token`, mutation paths `_mutationCts.Token`); `DisposeAsync` tears it down.
4. `LoadAsync` loads the ward view + submission GET in parallel via `Task.WhenAll` (`:214`), null/error semantics preserved.
5. `Guid.Empty` early return now sets `_error = "Missing assignment or ward identifier for this session."` (`:167`) — no perpetual spinner; new bUnit `MissingIdentifiers_ShowsError_NoPerpetualSpinner`.
6. `[JSInvokable("onVideoTick")] OnVideoTickAsync(Guid moduleId, int percent)` — trailing `CancellationToken` dropped (`:285`); JS already calls with exactly 2 args; progress call passes `CancellationToken.None`.
7. `documents/configuration.md:127` — `families` removed from the §2 injection row (AppHost injects no flag param; §5 consumer row intact).

**Worker numbers:** Families **15/0** (14 → +1), Assignments 591/0, Assignments.Api 58/0, build 0 errors.
**Re-frozen patch (post-cleanup):** `documents/rounds/diffs-ar-13-families-ward-surface.patch` — **36 files**, +1,574/−47; parent-confirmed content-identical modulo CRLF to the live `git diff`.
**Parent authoritative re-matrix (post-cleanup):** `dotnet build SchoolCollab.sln` → **0 errors**; full unit matrix → **2,251 / 0** (Core 79, Assignments 591, Assignments.Api 58, Families **15**, Students 422, Students.Api 1, Settings 519, Settings.Api 1, Admin 545, ArchitectureTests 20).
**New tester surface:** `src/SchoolCollab.Families/Components/Pages/Error.razor` (route `/Error`) — added to the handover.

## UI Tester (run `408ba8e3-70cf-485f-962c-53d78137b75f`, `ollama/minimax-m3:cloud`) — Verdict: **P2-only** (P1: none)

Static adversarial analysis (no browser/Playwright in this session; source + patch + round doc read, live tree cross-checked). Scope ack: hunted exactly the handed-over surfaces.

**Reviewer's open ask — RESOLVED:** heartbeat WILL report. Pass 6's 2-arg `OnVideoTickAsync(Guid, int)` matches `wardPlayer.js:36 invokeMethodAsync('onVideoTick', moduleId, bucket)`; the hyphenated Guid string deserializes via `JsonElement.GetGuid()`; import path `./js/wardPlayer.js` is correct for the Root-mode host. No silent bind failure; the JS `.catch` is defense-in-depth only.

**P2s:**
1. `Assignment.razor:194-201` — silent heartbeat attach-failure: `_heartbeatsAttached = true` is set **before** `await AttachVideoHeartbeatsAsync()`; any non-`JSDisconnectedException` fault is swallowed, the flag stays true, no error surfaces, and every later render skips re-attach → heartbeat never reports, no retry path (ar-8/ar-9 swallowed-failure class).
2. `Assignment.razor:165-188` + `Index.razor:101-115` — load race: two rapid parameter changes can commit the OLD ward/assignment after the new one (`_loadCts.Cancel()` may not fire once the response is constructed; the `_loadedFor` guard fires on entry, not on commit). Symptom: stale ward/assignment data on rapid navigation.
3. `Assignment.razor` — no in-page back-navigation despite the handover listing it; users must use the browser back button.
4. `Assignment.razor:240-260` — mutation-triggered `LoadAsync()` (mark-read) does not reset `_view = null` first → stale `PercentComplete` shown until the refetch lands (the `OnParametersSetAsync` path does reset it).
5. `Assignment.razor:55` — guide link `rel="noopener"` without `noreferrer`. Minor privacy nit.
6. `Index.razor:39` — `GridTemplateColumns="1fr 140px 160px 120px"` declares 4 widths but the grid renders 5 columns (Assignment, Due, Status, Modules, Open). Cosmetic layout glitch.

**Out-of-round:** declared v1 deviation (`ReportModuleProgressAsync` return discarded) retained.

**Triage (parent):** items **1–6 are material enough to fix** (real swallowed failure, stale-data race, an unmet handover scope item, UX flicker, privacy nit, visible layout glitch) → **cleanup pass 7** (worker model), then tester re-verification (tester loop iteration 2 of ≤2). No P1s, so the round is not blocked.

### Cleanup pass 7 (worker — `ollama/deepseek-v4-flash:0731-cloud`, run `f8dc072c`)

All 6 tester P2s implemented (parent-verified in the tree):

1. `AttachVideoHeartbeatsAsync` → `Task<bool>`; `OnAfterRenderAsync` resets `_heartbeatsAttached = false` on a failed attach → retry on a later render (`Assignment.razor:212-217,281`); non-fatal (no hard error surface; "Mark watched" still works).
2. `_loadEpoch` guard added in `Assignment.razor` (`:157,183,229,238,257,264,385,410`) and `Index.razor` (`:75,105,113,115`) — a superseded load can no longer commit stale `_view`/score.
3. In-page back-navigation: `FluentAnchor` "Back to ward" → `/ward/{_studentId}`, gated on a non-empty id and rendered in loading/error/loaded states (`:14-17`); FluentUI-only, CSS-isolated `.ward-back`.
4. `MarkModuleReadAsync` resets `_view = null` before the refresh (`:357`).
5. Guide link `rel="noopener noreferrer"` (`:63`).
6. `Index.razor` grid → 5 column widths (`:39`).

**Tests:** Families 15 → **16/0** (+`BackNavigation_PointsAtWardList`). **Deviation (reported, not faked):** the superseded-load race has no bUnit test — MockHttp 6.0.0's `Respond()` has no in-flight-hold overload; a robust test needs a virtual/abstract client (component refactor), out of this bounded pass.
**Re-frozen patch (post-pass-7):** `documents/rounds/diffs-ar-13-families-ward-surface.patch` — **36 files**, +1,638/−47.
**Parent authoritative re-matrix (post-pass-7):** `dotnet build SchoolCollab.sln` → **0 errors**; full unit matrix → **2,252 / 0** (Core 79, Assignments 591, Assignments.Api 58, Families **16**, Students 422, Students.Api 1, Settings 519, Settings.Api 1, Admin 545, ArchitectureTests 20).

### UI Tester re-verification (run `22844622-658a-46b9-ac50-a8f075d3e991`, `ollama/minimax-m3:cloud`) — Verdict: **P2-only** (no P1; no in-scope P2)

Scope: the pass-7 rework surfaces + regression check (static — no browser). All six pass-7 fixes **confirmed**; the new `BackNavigation_PointsAtWardList` test judged real (it waits for the async load, finds the `fluent-anchor`, and asserts `href == /ward/{StudentId}` — an omitted/wrong anchor fails it). No regressions in the touched surfaces.

- Item 1: `JSDisconnectedException` returns `true` so disposed-circuit teardown is not misclassified as retryable; other faults log + fall through to the next-render retry. No swallowed-failure loop.
- Item 2: epoch bumped before the new load; guard present at every commit site (2 load paths × both result branches + mutation sites). **Sound.**
- Items 3–6: confirmed (anchor first in `ChildContent` → renders in all three states; `_view = null` at `:357`; `rel` fix at `:63`; 5 grid widths at `:39`).

**Deviation adjudicated — ACCEPTED:** the missing superseded-load bUnit test is acceptable; the epoch guard is statically sound and a faithful concurrency test needs a virtual/abstract `FamiliesApiClient` (component refactor out of this bounded pass). No new test owed.

## Acceptance — FINAL (parent-as-document-owner, resumed round) — Verdict: **CLOSED**

**Acceptance criteria (from `## Plan`):**
- Worker-facing — build 0 errors ✅; all unit projects 0 failures ✅; changed files within the expected list (36 vs ~35) ✅; deviations reported ✅.
- Reviewer-facing — decisions (a)–(g) as written ✅; Families references Contracts-not-Application, no admin route leakage ✅; auth mirrors the Admin startup switch ✅; player binds ar-12 DTOs without contract changes ✅; JS interop follows the skill ✅; best-practices — no overwrites / skills honored / readable ✅.
- Orchestrator-facing — authoritative build + matrix green ✅; reviewer **P2-only, P1 empty** ✅; UI round → **tester pass fired (2 iterations, both P2-only)** ✅; **P1 empty → CLOSED** ✅.

**Final authoritative numbers (parent, post-pass-7):** `dotnet build SchoolCollab.sln` → **0 errors**; unit matrix **2,252 / 0** (Core 79, Assignments 591, Assignments.Api 58, Families **16**, Students 422, Students.Api 1, Settings 519, Settings.Api 1, Admin 545, ArchitectureTests 20).

**Findings resolved this round:** 2 P1s (bUnit hang root-cause in both ward pages; `_content/` import path) + 15 P2s across rework 1 (5), pass 5 (6), pass 6 (7), pass 7 (6) with overlap. Final review P2-only.

**Residual P2s / declared deviations (accepted at close):**
- Superseded-load race has no bUnit test (epoch guard statically sound; test needs a client refactor).
- `ReportModuleProgressAsync` non-204 return discarded at both call sites (silent progress-report rejection) — declared v1 deviation.
- Locked-question block + feedback rendering are v1 simplifications.
- Captions `<track>` is captions-READY only (no captions field in the DTO); full WCAG is G1/Phase 5.
- Ward OIDC identity + deep-link token minting/validation (E1); sign-page relocation (F3); rubric/comment UI (Phase 5); stored-file video playback (future).
- `.pi/skills/orchestrator-worker-reviewer/references/models.md` rides in the patch (round-process doc, intentional) — consider excluding at feature-PR time.
- Repo-wide NU1902/NU1903 package-vulnerability warnings pre-exist and are unrelated to this round.

**Loop bounds respected:** Tier 3 reviewer ≤2 → used 2 (passes 4–5) + the final re-verification; tester ≤2 → used 2 (initial + re-verify). No bound exceeded.

**Artifact:** `documents/rounds/diffs-ar-13-families-ward-surface.patch` — **36 files, +1,638/−47**, content-identical modulo CRLF to the live tree. **Working tree only; no commits (repo policy).**
