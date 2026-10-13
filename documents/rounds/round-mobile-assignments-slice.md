# Round: mobile-assignments-slice (L4 of the mobile train — first vertical slice)

**Provider:** pi, full Tier 3 (UI) — models: glm-5.3-flash orchestrator + accept, kimi-k2.7-code plan-gate and diff-review (the canonical ollama/glm-5.3:cloud gate id is unavailable; a glm-family substitute would put the gate on the plan author's own id, which round-runner-model-policy.md item 3 forbids), deepseek-v4.1-flash worker, kimi-k2.7-code escalator, minimax-m3 UI tester
**Status:** CLOSED — worker + two-slice diff review + acceptance complete; UI tester run complete (run 1: 3 P1 + 12 P2 → adjudicated → rework R1–R11 → re-verify **ACCEPTABLE AS IT STANDS**; record in `## UI Tester`); **L4.2 carried OPEN (device-bound)**
**Round base:** `fd10e19d` (**stacked** — see the premise correction below), full `fd10e19d feat(mobile): credential + client-side registration path for the Hybrid head (L3)` — branch `feat/mobile-assignments-slice`, worktree `School-Collab-mobile-train`. Tree **clean at base** (verified at plan time: `git status --short` empty).
**Spec:** `documents/specs/mobile-hybrid-app.md` — §2 pinned decisions (D2, D6, D8), §4 layout, §8 L4 (the four acceptance criteria this round executes), §9 OD1/OD3. Predecessor rounds: `round-mobile-host-skeleton.md` (L1), `round-mobile-severance.md` (L2), `round-mobile-credential.md` (L3).

---

## Execution log

- **Plan written:** 2026-10-13, glm-5.3-flash orchestrator, measured on the round base.
- **Premise correction (supervisor decision, before plan writing) — the base was wrong in the brief and is now fixed on the branch.** The parent's brief declared base `6cb415ec` (from `origin/main`). Verified against the tree: that base contains L2 (`d2b2e98d`, #338) but **not L3**; the L3 commit `fd10e19d` existed only on branch `feat/mobile-credential`, and `round-mobile-credential.md` existed only there. On `6cb415ec` the realm JSON carried no public mobile client and `SchoolCollab.Mobile.Logic` held only the L1-era `MobileApiOptions.cs` — several facts the brief pins as settled were false at the declared base. **Supervisor decision (a):** L4 stacks on L3 — `git checkout -B feat/mobile-assignments-slice fd10e19d` was run by this orchestrator before writing citations (git-only, local, non-destructive; nothing pushed; `feat/mobile-credential` untouched; tip verified at `fd10e19d`, tree clean, L3 files present: `round-mobile-credential.md`, `BearerTokenDelegatingHandler.cs`, `MobileClientRegistrationExtensions.cs`, et al.).
- **Standing risk recorded (supervisor):** L3 is **committed but unpushed and unmerged**. L4's delivery is a `gh-stack` layer on top of L3's PR, or lands only after L3 merges; every push/PR/merge remains owner-gated. **If L3 is reworked after its PR review, L4's base moves and this branch rebases onto L3's new tip.**
- **Facts cited below are therefore of two kinds:** (a) facts true at the base commit *before* L3 (L1/L2-era — `MobileApiOptions.cs`, the head csproj shape, `AssignmentsApiClient`, the screens, the Contracts project); and (b) **L3-delivered facts** — the auth stack and realm seed — cited to their files on this branch and marked *(L3)* each time, never presented as base facts.
- **Plan gate (2026-10-13, kimi-k2.7-code): REVISE — one P1 + three P2s, all folded below.** Every line number the revision touches was re-verified against the tree at `fd10e19d` before being corrected.
  - **P1 — the authorization services the plan forgot.** W4 wraps both new screens in `<AuthorizeView>`, but the head's DI (as planned) registers only `AddMobileAuth(authOptions)` + `AddMobileAdminClients(apiOptions)` (`src/Mobile/SchoolCollab.Mobile/MauiProgram.cs:55-56`); `AuthorizeView` needs `IAuthorizationService`/`IAuthorizationPolicyProvider`, and a `git grep -E "AddAuthorizationCore|AddAuthorization\b" -- src/Mobile` returns **0** source matches (re-verified). The server registers the same through `services.AddAuthorization()` (`src/SchoolCollab.Core/Auth/AuthTenancyExtensions.cs:209`), which the head deliberately does not call — so as written, the first `<AuthorizeView>` render would throw. **Folded as the gate's smallest fix:** W4's head wiring adds `builder.Services.AddAuthorizationCore();` beside `AddFluentUIComponents()` — **no new package is required**, because `Microsoft.AspNetCore.Components.Authorization` (already referenced by `Mobile.Logic` since L3, `SchoolCollab.Mobile.Logic.csproj:14`) pulls `Microsoft.AspNetCore.Authorization 10.0.8` as a package dependency (verified in the head's resolved `obj/project.assets.json`; the resolved dll already flows into the head's output closure), and `AddAuthorizationCore()` lives in that package. **Considered and rejected (decision, not omission):** dropping `<AuthorizeView>` in favour of `TenantGate` alone would also compile, but the plan keeps `AuthorizeView` so the slice has a route-level signed-out affordance distinct from the tenant gate (an unauthenticated visitor sees the sign-in call to action, matching the L3 Spike-page pattern) — the one-line registration is cheaper than re-shaping both screens' gate structure.
  - **P2-1 — the method table says 13 but lists 12.** Re-verified against the screens: the two screens call exactly the 12 methods in W1's table (`ListAsync`, `GetByIdAsync`, `PublishAsync`, `UnpublishAsync`, `CloseAsync`, `SubmitForApprovalAsync`, `ApproveAsync`, `RejectAsync`, `DeleteAsync`, `GetRecipientsAsync`, `ListSubmissionsAsync`, `GetSubmissionAsync`). No 13th method earns its place — `DuplicateAsync` is excluded by OD-3 and `GetEffectivePolicyAsync`/`ReviewAsync`/`ReviewSubmissionAsync` by OD-2/out-of-scope. **Corrected to 12** everywhere the count appears.
  - **P2-2 — the view models are never registered.** W3 defines both VMs with constructor-injected dependencies and W4 wired the head without registering them (a `@inject` would throw at runtime). **Folded:** W2's `AddMobileAssignmentsApiClient` extension now registers both view models (lifetimes and rationale in W2) so the head wiring needs only the one extension call, and W5's registration test asserts them.
  - **P2-3 — drifted citations.** The gate measured `MobileApiOptions.cs:23-27, :61-64, :76-79` and the `AddHttpClient<TClient>` helper at `MobileClientRegistrationExtensions.cs:114-115` — both corrected below. The re-verification additionally found and corrected: the MauiProgram L3 wiring block `:40-56` with the registrations at `:55-56` (the plan had `:42-51`, the gate's issue text `:50-51` — neither matches the tree), the bearer-handler transient registration at `MobileClientRegistrationExtensions.cs:55` (was `:57`), the dual `MobileAuthenticationStateProvider` registration at `:98-100` (was `:96-100`), the `AddMobileAdminClients` method span `:44-70` (was `:44-71`), and the `ThrowIfNull` doc-pattern lines `:46-47` (was `:46-50`).
  - **Gate adjudications — all four sound, recorded as settled; re-opened only by a fresh owner instruction:**
    - **OD-1 (slice-scoped 12-method client + copied `EnumHelper`) — sound.** The verbatim move is infeasible: `EffectiveAssignmentPolicy` is pinned to `Core` (`src/SchoolCollab.Core/AssignmentPolicies/EffectiveAssignmentPolicy.cs:1`; L2 round-3 pin 1) and `Core` stays a FrameworkReference carrier via its `JwtBearer`/`OpenIdConnect` packages (`src/SchoolCollab.Core/SchoolCollab.Core.csproj:9-11`); an interface-only contract would still require editing `Assignments.Application`, which the pins forbid and which would collide with open PR #342.
    - **OD-2 (grading/review forms out) — sound.** The read-only submissions view ships; the review/grade forms defer to a forms-first round with the safe-area keyboard leg verified on device.
    - **OD-3 (Create/Edit/Duplicate out) — sound.** Deferred with the authoring slice; the wire cost (one POST) stays trivial to add later.
    - **OD-4 (spec-OD3 settled by precedent) — sound.** The L3 OD-4 adjudication + spec D3 stand; no new architecture test this round (the backlog's G5 item is the vehicle).
  - **Gate posture confirmations recorded:** security/tenancy — no tenant-propagation handler on the mobile path, correct by design: the device is a user-agent caller and the API resolves `tenant_id` from the authenticated principal (the head carries only the bearer token). Acceptance honesty — mechanical (greps, file presence, `dotnet test`) vs device-bound (rendered insets, keyboard avoidance) checks stay split exactly as L3 split them; AC-2's rendered leg stays carried OPEN.
- **Worker blockers adjudicated (2026-10-13):** before writing a line, the worker (deepseek-v4.1-flash) raised four verified blockers — `AddAuthorizationCore()` was necessary but **insufficient** (`AddCascadingAuthenticationState()` also needed), `MainLayout.razor` needed a scoped root element + `<FluentDialogProvider />` (or AC-2 would be mechanically unsatisfiable), the `SignOffStateDto` string-enum converter (the host writes `"signOffState":"None"`), and no toasts (inline message/state surfaces instead of `IToastService`). The parent approved all four; zero rework passes were needed after delivery. Recorded in `## Worker Report` and `## Acceptance` — Amendments.
- **Review (2026-10-13, kimi-k2.7-code) — lane failure recorded honestly.** The planned three-slice async review failed to start **all three** background children (the host npm package does not provide `@earendil-works/chord`; `Resume: unavailable`); the reviews ran as **two blocking children** of the same protocol (slice A: the implementation; slice B: tests + doc) — the mode every other run this session has used. Slice A: OK with notes, 0 P1, 1 P2 (the iOS double top inset). Slice B: tests confirmed discriminating; BLOCK raised against the doc's pre-accept state (expected-file tally 33 vs the patch's 25, the amendments/judgement calls unrecorded, pending placeholders) — the parent adjudicated the placeholders as the accept run's own job and the reconciliation as a real requirement. Details in `## Review`.
- **Acceptance written (2026-10-13):** on the parent's authoritative numbers (android-TFM build, slnx build, the four test suites, the greps). **L4.2 is carried OPEN (device-bound)** — the round's single residual criterion, exactly as L3 carried L3.3. **The UI tester (minimax-m3, static adversarial pass) is dispatched next** — scope handover in `## UI Tester`.
- **UI-tester stage (2026-10-13, minimax-m3 — static adversarial pass):** **run 1** over the handover's surfaces returned **3 P1 + 12 P2**. **Parent adjudication:** P1-1 and P1-2 accepted — **one root cause** (action failures routed through the load-error surface, which also never cleared `Error` on success); P1-3 folded into that root cause; a prioritised subset of the P2s fixed; **three P2s deliberately not fixed** (search text surviving sign-out; the shared `Admin.Shared` dialog's 420 px width — repo-wide; the un-cancelled in-flight search on clear — cosmetic). **Rework R1–R11** (deepseek-v4.1-flash worker): failure surfaces split (`Error`/`ErrorTitle` for fetches vs `ActionError`/`ActionErrorTitle` + `RetryActionAsync()` for actions, `ClearFailures()` on every successful load), the detail's error branch gained a Back control, per-row `IsActionPending(id)` replaced the global fetch flag, `DisplayItems` became filter-aware (both slices), failure-specific bar titles, `CanDecideApproval` gating with an explanatory info bar, bottom-inset padding on the fixed bar, distinct fallback copy, `role="status"` + `aria-label` on the loading surfaces, and a `ReloadWarning` with a Refresh action replacing the silent post-action reload divergence. **19 new tests** — the Mobile suite **123 → 141**; discrimination proven by re-injecting the pre-fix behaviour into both view models (**12** then **5** failures, one per discriminating test), with the worker honestly flagging that one test is a regression guard rather than a pre-fix discriminator and that the four markup-only fixes have no head test harness (static verification only). **Re-verify attempt 1 returned no verdict at all** — the child misread its task and reported on `git status`; per the skill, a missing verdict is *no review, never clean*, so it was **re-dispatched, not accepted**. **Run 2 (re-verify): ACCEPTABLE AS IT STANDS** — all three P1s resolved (verified by tracing the render branches: the load-error and action-error surfaces are mutually exclusive on `Index.razor`, the two channels are fully separated in the view model, `ClearFailures` runs on both success paths, and the stale-row delete case is a no-op), `role="status"` used once per loading state, the filter-aware display honest including its empty message, and the inset padding not fighting the page's own reservation. Run 2 found **one new low-severity defect** (a stale `ReloadWarning`) and **one asymmetry** (the detail's action-error bar has no retry control), both recorded as residuals and deliberately not reworked (the tester called them a small follow-up; the round is at its rework bound) — see `## UI Tester` residuals and `## Acceptance`. **Parent's post-rework authoritative numbers:** android TFM **0 errors / 0 warnings**; root `SchoolCollab.slnx` **0 errors**; `Mobile.Tests.Unit` **141/0**; `ArchitectureTests.Unit` **111/0**; re-frozen patch **26 files, 5154 lines (19 new)**.

---

## Plan

**Goal.** L4 — the mobile train's first real screens: the teacher-facing **assignments list → detail** slice, reworked fresh in the head (spec D2), reusing the portable layer (Admin.Shared components, `Assignments.Contracts` DTOs, the L3 auth stack), on a device TFM that still builds (`net10.0-android` is the headline; the mobile CI job's `ENABLE_MAUI_CI` gate is **not** touched).

### Owner-pinned decisions — recorded as pins, NOT re-opened

1. **Audience: teacher-facing.** Assignments list → detail (spec §9 OD1's recommendation). The seeded `dev-teacher` carries the `teacher` role *(L3: the realm seed on this branch)*, so the gate path is exercised for real.
2. **The two screens are reworked fresh in the head** — spec D2's "the 37 screens are reworked, not ported". The head must **not** reference `Assignments.Application` (it references `Students.Application`, which carries `<FrameworkReference Include="Microsoft.AspNetCore.App"/>`-delivering closure members — the NETSDK1082 root cause L2 removed).
3. **The mobile CI job stays gated** behind `ENABLE_MAUI_CI` (`if: ${{ vars.ENABLE_MAUI_CI == 'true' }}`, .github/workflows/ci.yml:174) — unchanged this round.
4. **Base addresses remain configuration** (spec D8): **no `http(s)://` C# literal under `src/`**. The assignments base address already exists as configuration: `MobileApi.AssignmentsApi` (`src/Mobile/SchoolCollab.Mobile/appsettings.json:4`, `"https://localhost:7199"`). CI's guard cannot catch a `localhost` literal (`excludedHosts`, spec §6), so the diff review greps `src/Mobile` for `http(s)?://` literals.
5. **Reuse the portable layer:** Admin.Shared's portable components (`FieldDisplay` et al. — L2 made it portable: `src/SchoolCollab.Admin.Shared/SchoolCollab.Admin.Shared.csproj` references only `Core.Abstractions` + FluentUI + the Components packages), `Assignments.Contracts` DTOs, and the L3 auth stack in `Mobile.Logic`.
6. *(Supervisor, execution order)* L4 stacks on L3 per the gh-stack policy; see the execution log.

### Measured facts (measured on the round base `fd10e19d`; discipline: `documents/specs/round-runner-model-policy.md` decision 4)

**The head and its closure (post-L2, pre-L4):**

- The head references exactly `Admin.Shared` + `Mobile.Logic` (`src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj:72-73`); its closure carries **no** FrameworkReference carrier — the property L2 delivered and this round must survive (any new reference must be zero-framework-dependency).
- `Mobile.Logic` is plain `net10.0`, packages: Components.Authorization + Configuration.Abstractions/Binder + Http (no version attributes — CPM) and one ProjectReference to `Admin.Shared` (`src/Mobile/SchoolCollab.Mobile.Logic/SchoolCollab.Mobile.Logic.csproj:10-24`). The load-bearing comment pins the round contract: "everything CI has to see lives here and only host wiring stays in the head" (`SchoolCollab.Mobile.Logic.csproj:8-10`).
- `MobileApiOptions` already declares the three names — `StudentsApi`, `SettingsApi`, `AssignmentsApi` (`src/Mobile/SchoolCollab.Mobile.Logic/MobileApiOptions.cs:23-27`, property `:37`); `Resolve` normalizes to an absolute `Uri` with a trailing slash or returns `null` for an unconfigured value (`MobileApiOptions.cs:61-64`); it **throws** on an unknown name (`MobileApiOptions.cs:76-79`). Base-URL values are configuration (`appsettings.json:2-6`); the Development overlay configures only the dev authority (`appsettings.Development.json:1-4`).
- The head's `MauiProgram` (L3 state) wires: embedded `appsettings.json` (+ dev overlay by `DOTNET_ENVIRONMENT`), `MobileApiOptions`/`MobileAuthOptions` singletons, the two platform seam implementations (`SecureStorageTokenStore`, `SystemBrowserAuthenticator`), then `.AddMobileAuth(authOptions)` + `.AddMobileAdminClients(apiOptions)` (`src/Mobile/SchoolCollab.Mobile/MauiProgram.cs:40-56` — the two registrations at `:55-56`).
- The head's router discovers `@page` components in the head assembly only (`src/Mobile/SchoolCollab.Mobile/Components/Routes.razor:1-8`); today the head has only `NotFound.razor` + the `Spike` page. **No FluentUI *services* are registered yet anywhere in the head** (verified: `AddFluentUIComponents` has zero matches under `src/Mobile`) — pure display components like `FieldDisplay` work, but the services the screens need (`IDialogService`, `IToastService`) do not exist until the registration is added (precedent: `src/SchoolCollab.Admin/Program.cs:41`).

**The L3 auth stack *(L3)* — what the slice builds on:**

- `BearerTokenDelegatingHandler` attaches `Authorization: Bearer <token>` to every outgoing call; a failed refresh degrades to an unauthenticated send and the server's 401 (`src/Mobile/SchoolCollab.Mobile.Logic/BearerTokenDelegatingHandler.cs:1-31`); registered `transient` (`MobileClientRegistrationExtensions.cs:55`).
- `MobileClientRegistrationExtensions.AddMobileAdminClients` is the pattern to mirror: one `apiOptions.Resolve(<apiName>)` for the group (`src/Mobile/SchoolCollab.Mobile.Logic/MobileClientRegistrationExtensions.cs:44-70`), `TryAddTransient<BearerTokenDelegatingHandler>()`, then `AddClient<TClient>` — `AddHttpClient<TClient>(client => client.BaseAddress = baseAddress).AddHttpMessageHandler<BearerTokenDelegatingHandler>()` (`MobileClientRegistrationExtensions.cs:114-115`, the `AddClient<TClient>` helper `:111-116`).
- `MobileAuthenticationStateProvider` is registered under both its concrete type and `AuthenticationStateProvider` resolving to the **same** instance (`MobileClientRegistrationExtensions.cs:98-100`), so the screens' claim reads (below) and the gates see the same session.
- **The head's DI has no authorization services (gate P1, re-verified):** `git grep -E "AddAuthorizationCore|AddAuthorization\b" -- src/Mobile` returns **0** source matches; `<AuthorizeView>` (W4) needs `IAuthorizationService`/`IAuthorizationPolicyProvider`, which `AddAuthorizationCore()` registers. The server gets the same through `services.AddAuthorization()` (`src/SchoolCollab.Core/Auth/AuthTenancyExtensions.cs:209`), which the head deliberately does not call. **No new package is required:** `Mobile.Logic` already references `Microsoft.AspNetCore.Components.Authorization` (`SchoolCollab.Mobile.Logic.csproj:14`), whose package depends on `Microsoft.AspNetCore.Authorization 10.0.8` (verified in the head's resolved `obj/project.assets.json`; the dll already flows into the head's output closure) — and `AddAuthorizationCore()` lives in that package.
- The realm seed *(L3)* adds the public client `school-collab-mobile-client` (`realm.json:142-145`), realm role `teacher` (`realm.json:11`), and the `dev-teacher` user (`realm.json:244-246`) — `src/AppHost/SchoolCollab.AppHost/school-collab-realm.json`.
- L3's test conventions this round reuses: MSTest `[TestClass]`/`[TestMethod]` + FluentAssertions, configuration built in-test, `NullLogger` (`tests/SchoolCollab.Mobile.Tests.Unit/MobileClientRegistrationTests.cs:1-16`); the suite currently holds **80** `[TestMethod]` attributes; the L3 round doc records **83/0** tests (`round-mobile-credential.md`, Status line).

**The server-side reference behaviour (read-only reference — the worker does NOT edit these files; open PR #342 touches this area, so the diff stays out):**

- `AssignmentsApiClient` lives at `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentsApiClient.cs` — `public sealed class` (`:26`), ctor `(HttpClient http, ILogger<AssignmentsApiClient> logger)` (`:32`-33), **44 public Task methods** (measured: `ListAsync :82`, `GetByIdAsync :94`, `GetEffectivePolicyAsync :117`, `GetAuthoringChildrenAsync :139`, `CreateAsync :186`, `UpdateAsync :223`, `PublishAsync :230,236`, `UnpublishAsync :242`, `CloseAsync :248`, `ArchiveAsync :258`, `ScheduleAsync :269`, `SubmitForApprovalAsync :276`, `ApproveAsync :284`, `RejectAsync :292`, `ReviewAsync :298`, `DeleteAsync :304`, `DuplicateAsync :315`, `GetRecipientsAsync :327`, `ListSubmissionsAsync :337`, `GetSubmissionAsync :345`, `ReviewSubmissionAsync :355`, `ReviewGateAsync :361`, `EnableSubmissionAsync :367`, `OverrideStudentSubmissionAttemptsAsync :376`, `GetSignatureConsentTextAsync :391`, `ListSignOffStatusesAsync :402`, `GetNotificationFailuresAsync :417`, `GetSignOffContextAsync :428`, `SignOffAsync :439`, `ReassignSignerAsync :449`, `FinalizeSignOffAsync :460`, `GetCertificateAsync :472`, `GetAiPromptPolicyAsync :486`, questions-draft methods `:497-526`, recorder/extraction/staging `:545-600+`). It is registered **server-side** via `AddCrossModuleHttpClient<AssignmentsApiClient>("https+http://assignments-api", propagateTenant: true)` (`src/Assignments/SchoolCollab.Assignments.Application/ModuleServices.cs:33`) — the head cannot use this shape: that registration is the cross-module service-discovery + tenant-propagation pattern for hosts, while the head's clients bind to `MobileApiOptions.Resolve(...)` + the bearer handler (L3 pattern).
- **The feasibility fact the structural fork turns on (resolved — measured):** the client's `SchoolCollab.Core.AssignmentPolicies` using (`AssignmentsApiClient.cs:7`) resolves to exactly **two** identifier occurrences of `EffectiveAssignmentPolicy` (`:117,126`, the `GetEffectivePolicyAsync` return type). `EffectiveAssignmentPolicy` **stays in `SchoolCollab.Core`** — it is NOT in the L2 abstractions project (`src/SchoolCollab.Core/AssignmentPolicies/EffectiveAssignmentPolicy.cs:1`; `src/SchoolCollab.Core.Abstractions/` contains only `FeatureFlagKeys`, `IFeatureFlagChangeNotifier`, `IFeatureFlagService`, `NotificationChannel`, `SignatureRequirementMode`) — and `Core` itself is a **FrameworkReference carrier at closure level** (L2's proven gate P1: its `JwtBearer`/`OpenIdConnect` packages declare the framework reference in their nuspecs). **Consequence: option (i)-verbatim ("move the whole 44-method client to a portable home") is infeasible** — `EffectiveAssignmentPolicy` cannot come along without either moving the record into `Core.Abstractions` (a shared-kernel change that contradicts L2's owner pin: round-3 pin 1, "policy shapes stay in `Core`", `round-mobile-severance.md`) or re-dragging `Core` onto the head's closure (the NETSDK1082 root cause, re-opened). Also: the record itself is **not needed by the slice** — neither screen calls `GetEffectivePolicyAsync` (measured: the screens' method usage below).
- **The helpers the screens use:** `src/Assignments/SchoolCollab.Assignments.Application/Helpers/` holds 8 static classes; **both screens use exactly one of them — `EnumHelper`** (measured: `Index.razor:38,41,44`, `Detail.razor:153-155,179,196,248`; the other 7 helpers serve the authoring/questions/materials components the slice excludes). `EnumHelper` is **17 lines, BCL-only** (`using System.ComponentModel;` — `EnumHelper.cs:1-17`, `GetDescription` reads `DescriptionAttribute`s).
- **The screens:**
  - **List** — `Components/Pages/Assignments/Index.razor` (568 lines, `@page "/assignments"`, `:1`): injects `AssignmentsApiClient Api`, `ILogger<Index>`, `IToastService Toast` (`:5-7`); renders via the `LandingPage` data-grid wrapper (`:9-46`, `src/SchoolCollab.Admin.Shared/Components/Landing/LandingPage.razor` — a **desktop grid** shape, not a mobile card list). Load: `Api.ListAsync(statusFilter)` on an option-value switch (`:183-195`, `:84-90`); **search**: fetch-all + client-side `Title.Contains` filter (`:530-554`); **optimistic lifecycle actions** with rollback: Publish (`:216-237`), Unpublish (`:239-260`), Close (`:262-283`), Submit-for-approval (`:322-345`); list-removal Delete with toast (`:297-316`); `DuplicateAsync` + full reload (`:355-376`); row actions are status-branching (`:428-478`, navigations to `/assignments/{id}/edit` and kebab-style `FluentMenu` actions).
  - **Detail** — `Components/Pages/Assignments/Detail.razor` (661 lines, `@page "/assignments/{Id:guid}"`, `:1`): injects `Api`, `IDialogService`, `NavigationManager`, `ILogger`, `AuthenticationStateProvider` (`:6-10`). Load: `Api.GetByIdAsync(Id)` (not-found ⇒ "Assignment not found" message, `:343`/`:117-119`), then `GetRecipientsAsync` + `ListSubmissionsAsync` — each independently failure-degraded to a warning, never failing the page (`:352-374`). Lifecycle reload: `ReloadItemAsync` (`:399-412`). **Approval surface** gated on the server-derived `_item.RequiresApproval`: chip + submit/approve/reject (`:110-133`, handlers `:415-509`); the **acting teacher** comes from the `teacher_id` claim via `AuthStateProvider` with `Guid.Empty` fallback (`:463-469` — the same claim spelling L3's realm mappers emit). Tabs: **Overview** renders `AssignmentAuthoring` (View mode, `:55`) — a server-authoring component, **out** of the mobile slice; **Recipients** (table read, `:130-166`); **Submissions** (table read + per-student read view + review/grade forms, `:167-260`, handlers `:553-620`); **Guardian Sign-off** (`SignOffSection`, gated `:264-271`) and **Notifications** (`NotificationFailuresSection`, gated `:273-282`) — **out**. Closed-assignment "Submit Review" form (`:93-120`) — **out** (grading surface).
- **Wire contract of the slice's calls (all shapes already proven on the server client — the worker mirrors them, no route invention):** list `GET /assignments?status=` (`:82-91`, the API's `GET /` route takes `[FromQuery] AssignmentStatus? status`, `src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs:78-79`); by-id `GET /assignments/{id}` with **null on 404** (`:94-102`, route `AssignmentRoutes.cs:93`); publish `POST /assignments/{id}/publish` with a JSON body `PublishAssignmentRequest?` (`:230-235`; `null` when no contact filter — `Contracts/ContractTypes.cs:665`); unpublish/close `POST .../unpublish|close` with empty body (`:242-253`); submit-for-approval `POST .../submit-for-approval` (`:276-281`); approve/reject `POST .../approve|reject` with `{ApproverId}` (`:284-295`; `ApproveAssignmentRequest`/`RejectAssignmentRequest`, `ContractTypes.cs:380,386`); delete `DELETE /assignments/{id}` (`:304-307`); recipients/submissions `GET .../recipients`, `GET .../submissions`, `GET .../students/{studentId}/submission` (`:327-350`, routes `AssignmentRoutes.cs:983,109,129` — all in the `readerGroup`).
- **DTOs (all in the clean contracts project):** `src/Assignments/SchoolCollab.Assignments.Contracts/SchoolCollab.Assignments.Contracts.csproj:1-7` is `net10.0` with **no PackageReferences, no ProjectReferences, no FrameworkReferences** — freely usable from the head without touching the closure property. Types: `AssignmentSummaryDto` (19 fields incl. `Status`, `DueDate`, `MaxScore`, `MandatoryReview`, `RequiresApproval`, `ApprovalStatus` — `ContractTypes.cs:191-222`), `AssignmentRecipientDto` (`:806`), `SubmissionForReviewDto` (`:685`), `SubmissionDetailDto` (`:849`), the enums `AssignmentStatusDto`/`ApprovalStatusDto`/`AssignmentTypeDto`/`GradingFormatDto`/`TargetAudienceTypeDto`/`ReviewStateDto` (`:5,22,29,39,49,677`).
- **Auth posture of the reads (risk, not plan blocker):** the whole `/assignments` group requires `BearerScheme` + authenticated user (`src/Assignments/SchoolCollab.Assignments.Api/AssignmentEndpoints.cs:16-23`); the seven-plus reader GETs the slice hits sit in the `readerGroup` whose policy additionally pins the **portal-session gateway scheme** and the four-role disjunction `teacher ∨ staff ∨ user-admin ∨ platform-admin` (`AssignmentEndpoints.cs:66-74`, `:107-121`; role constants `src/SchoolCollab.Core/Constants/RealmRoleNames.cs:21-31`). Whether a Keycloak-bearer-only principal satisfies the *combined* group + sub-group policies is unproven at plan stage — L4's unit tests mock the transport, so this materializes only at the device criterion (see L3.3, carried OPEN). If the device run shows 403 on these reads, the fix is **server-side auth** (owner-gated, portal-session round's model) — **not** a mobile-side bypass.
- **Open PR #342** (`feat/recorder-takeover`, "inline recorder takeover + staged media pill") changes the *server-side* assignments recorder UI. L4 writes fresh mobile markup in the head and **must not edit anything under `src/Assignments/`**, so there is no textual conflict; the recorder's *behaviour* may move under that PR — noted, not depended on.
- **`maui-safe-area`** (installed skill `~/.agents/skills/maui-safe-area/SKILL.md`, repo-agents rule points): for a Blazor Hybrid app the settled recipe is **`SafeAreaEdges = None` on the host page and CSS `env(safe-area-inset-*)` handles the insets** (SKILL.md:228, "Blazor Hybrid Integration", :230-242 — "let CSS handle safe areas to avoid double-padding"). The head's host page already carries `viewport-fit=cover` and the template's `status-bar-safe-area` div (`src/Mobile/SchoolCollab.Mobile/wwwroot/index.html:6,21`); `MainLayout.razor.css` is currently **empty** (`Components/Layout/MainLayout.razor.css`) — the insets are not yet handled anywhere.
- **CSS-isolation rules** (`.github/copilot/rules/blazor-components.md:318-358`): every component gets a companion `.razor.css`; **no `style="…"` on elements** in `.razor` files (exceptions only for one-off dynamic values via a CSS custom property); page-specific styles never go into `wwwroot/app.css`.

### The structural question (the round's fork — resolved facts, recommended answer)

The slice cannot reference `Assignments.Application` (pin 2), and its typed client + helper live there. Three options were measured:

- **(i) Move the client to a portable home — infeasible as stated.** The client's only `Core.AssignmentPolicies` dependency is the `EffectiveAssignmentPolicy` record, which stays in `Core` (L2's round-3 pin 1) while `Core` is a framework-reference carrier at closure level (L2's proven gate P1). A verbatim move therefore can't compile without a shared-kernel change L2 pinned *out*, or without re-opening NETSDK1082. A *partial* move (43 portable methods, `GetEffectivePolicyAsync` stays) splits one class across two assemblies consumed by 26 server `.razor` files — churn for churn's sake.
- **(ii) Write a slice-scoped mobile client in `Mobile.Logic` — RECOMMENDED.** `MobileAssignmentsApiClient`: only the methods the two screens call (12 methods, table in W1), all signatures touching only `Assignments.Contracts` DTOs + BCL (every involved request record is in Contracts — `ContractTypes.cs:267,321,380,386,665,667,706,719,806,849`). Registered exactly like L3's clients: `AddHttpClient<T>(BaseAddress = apiOptions.Resolve(nameof(MobileApiOptions.AssignmentsApi))).AddHttpMessageHandler<BearerTokenDelegatingHandler>()` (mirror `MobileClientRegistrationExtensions.cs:51,114-115`). Cost: a second code path for the same wire shapes (drift risk) — mitigated by mirroring the exact route/verb/body shapes from `AssignmentsApiClient.cs` per-method (cited above) and by round-tripping the string-enum JSON in unit tests; revisit a unified move only when a second module slice needs the same 30+ other methods.
- **(iii) Move only the helper, skip the client (client stays handwritten inline in the pages)** — rejected: calls untestable per §8 L4.3's view-model contract and OD3's logic-split intent.
- **The helper (`EnumHelper`): copy, not move.** It is 17 BCL-only lines; both screens use only one method. Moving it would churn **10 files** in `src/Assignments/` (9 of them `.razor` pages + `QuestionPromptComposer.cs`) — **violating the pin to keep L4's diff out of `Assignments.Application` entirely** (PR #342 conflict avoidance). The copy (`AssignmentEnumDisplay`, the slice's display formatter in `Mobile.Logic`) is a conscious, recorded duplication whose drift cost is bounded to display text.

**OD3 (spec §9 — how much of the Logic split is enforced): stays resolved-by-precedent, no new guard.** The L3 round already adjudicated the placement principle ("D3/OD3-of-the-spec pins 'anything CI has to see lives in `Mobile.Logic`'" — `round-mobile-credential.md`, OD-4 adjudication) and the spec's D3 comment is load-bearing in the project file (`SchoolCollab.Mobile.Logic.csproj:8-10`). L4 follows it: both view models, the slice client, the registration extension and the display helper live in `Mobile.Logic`; the head keeps host wiring + layout only. No architecture-test extension this round (that is the backlog's separate G5 item).

### Work items

**W1 — `MobileAssignmentsApiClient` (new, `SchoolCollab.Mobile.Logic`).**

File `src/Mobile/SchoolCollab.Mobile.Logic/MobileAssignmentsApiClient.cs`:

- `public sealed class MobileAssignmentsApiClient(HttpClient http, ILogger<MobileAssignmentsApiClient> logger)` — the L3 client shape (primary constructor is fine; L3's `AddClient` registers via `AddHttpClient<TClient>`).
- A private `static readonly JsonSerializerOptions` mirroring the server client's **string-enum converter set for exactly the enums the slice touches**: `AssignmentTypeDto`, `AssignmentStatusDto`, `GradingFormatDto`, `TargetAudienceTypeDto`, `ApprovalStatusDto`, `ReviewStateDto`, `SubmissionSourceDto`, `GuardianRoleDto`, `NotificationKindDto`, `ContactOwnerTypeDto`, `ContactChannelDto` (read-side tolerance for the delivery enums mirrors `AssignmentsApiClient.cs:35-60`, including the documented `NotificationKindDto`/`ContactChannelDto` numeric-wire tolerance note).
- **The 12 slice methods** (gate P2-1: the count and the table now agree — the screens call exactly these 12; Duplicate is excluded by OD-3 and `GetEffectivePolicyAsync` has no caller this slice), each mirroring the server client's route/verb/body/log semantics verbatim (route/verb/table above):

| Method | Mirrors (route · verb · body) | Returns |
|---|---|---|
| `ListAsync(status?)` | `:82-91` · GET `/assignments[?status=<name>]` | `AssignmentSummaryDto[]?` |
| `GetByIdAsync(id)` | `:94-102` · GET `/assignments/{id}` (null on 404) | `AssignmentSummaryDto?` |
| `PublishAsync(id)` | `:230-234` · POST `/{id}/publish`, body `null` | — |
| `UnpublishAsync(id)` | `:242-246` · POST `/{id}/unpublish` | — |
| `CloseAsync(id)` | `:248-252` · POST `/{id}/close` | — |
| `SubmitForApprovalAsync(id)` | `:276-281` · POST `/{id}/submit-for-approval` | — |
| `ApproveAsync(id, approverId)` | `:284-289` · POST `/{id}/approve`, `ApproveAssignmentRequest` | — |
| `RejectAsync(id, approverId)` | `:292-296` · POST `/{id}/reject`, `RejectAssignmentRequest` | — |
| `DeleteAsync(id)` | `:304-307` · DELETE `/{id}` | — |
| `GetRecipientsAsync(id)` | `:327-333` · GET `/{id}/recipients` | `AssignmentRecipientDto[]?` |
| `ListSubmissionsAsync(id)` | `:337-342` · GET `/{id}/submissions` | `SubmissionForReviewDto[]?` |
| `GetSubmissionAsync(id, studentId)` | `:345-353` · GET `/{id}/students/{studentId}/submission` | `SubmissionDetailDto?` (null on 404) |

- **No base-address literal**: relative paths only, bound to the client's configured `BaseAddress` (the same shape L3's tests pin, `MobileClientRegistrationTests.cs`).

**W2 — Registration (new, `SchoolCollab.Mobile.Logic`).**

New file `src/Mobile/SchoolCollab.Mobile.Logic/MobileAssignmentsClientRegistration.cs` — `public static class MobileAssignmentsClientRegistration` with
`public static IServiceCollection AddMobileAssignmentsApiClient(this IServiceCollection services, MobileApiOptions apiOptions)`:
`ArgumentNullException.ThrowIfNull` both (same doc pattern as `MobileClientRegistrationExtensions.cs:46-47`); `apiOptions.Resolve(nameof(MobileApiOptions.AssignmentsApi))` (throws on the name only if it falls out of sync with `ApiNames` — same doc pattern as the `AddMobileAdminClients` XML-doc block, `MobileClientRegistrationExtensions.cs:40-43`); `services.TryAddTransient<BearerTokenDelegatingHandler>()` (idempotent beside L3's registration) and the one `AddHttpClient<MobileAssignmentsApiClient>(client => client.BaseAddress = baseAddress).AddHttpMessageHandler<BearerTokenDelegatingHandler>()` (mirror `MobileClientRegistrationExtensions.cs:114-115`) — **and (gate P2-2) it registers the two W3 view models:**

```csharp
services.AddTransient<AssignmentsListViewModel>();
services.AddTransient<AssignmentDetailViewModel>();
```

The VMs are DI-registered here, in `Mobile.Logic` (the OD3 rule — CI-visible registrations live there), and the head's pages `@inject` them. **Lifetime: transient, deliberately.** Each Blazor component instance gets its own VM (per-navigation-fresh state, matching the desktop pages' load-per-visit state and the typed client's transient-through-factory shape); **not scoped** — a Blazor Hybrid/WASM host resolves scoped services from one app-wide root scope (singleton-like), which would leak optimistic-action row state across navigations. All VM dependencies already satisfy the registration: the typed client (factory transient), `ILogger<>`, and `AuthenticationStateProvider` (L3's singleton, `MobileClientRegistrationExtensions.cs:98-100`).
`src/Mobile/SchoolCollab.Mobile.Logic/SchoolCollab.Mobile.Logic.csproj` gains
`<ProjectReference Include="..\..\Assignments\SchoolCollab.Assignments.Contracts\SchoolCollab.Assignments.Contracts.csproj" />` — the zero-dependency contracts project only (the closure property survives: no package, no FrameworkReference; the head's device-TFM property is re-asserted by the parent's build).

**W3 — The two view models (new, `SchoolCollab.Mobile.Logic`) — §8 L4.3's subjects.**

- `src/Mobile/SchoolCollab.Mobile.Logic/AssignmentsListViewModel.cs` — `public sealed class AssignmentsListViewModel(MobileAssignmentsApiClient api, ILogger<AssignmentsListViewModel> logger)`, `IDisposable` CTS handling like the server page's `Dispose` (`Index.razor:563-568`):
  - State: `AssignmentSummaryDto[]? Items`; `AssignmentStatusDto? StatusFilter`; `string SearchText`; `bool IsLoading`; `string? Error`; display strings computed on demand via the helper (W3c).
  - `LoadAsync(CancellationToken ct = default)` → `ListAsync(StatusFilter)` (mirrors `Index.razor:161-193`: catch `OperationCanceledException` silently, other failures to `Error`).
  - `SetStatusFilterAsync(AssignmentStatusDto? filter)` → re-`LoadAsync` (mirrors `OnStatusFilterChangedAsync`, `Index.razor:556-568`).
  - `SearchAsync(string? text)` → fetch-all + client-side case-insensitive Title filter (mirrors `OnSearchValueChanged`, `Index.razor:530-554`, including its `OperationCanceledException` swallow).
  - **Optimistic lifecycle**: `PublishAsync(id)`, `UnpublishAsync(id)`, `CloseAsync(id)`, `SubmitForApprovalAsync(id)` — stamp the new `Status`/`ApprovalStatus` on the in-memory row, call the API, roll the row back to `previous` on failure (`mirrors Index.razor:216-345` pattern, shared private helper).
  - `DeleteAsync(id)` → call API, remove the row (and the search-result twin) on success (mirrors `:297-316`).
- `src/Mobile/SchoolCollab.Mobile.Logic/AssignmentDetailViewModel.cs` — `public sealed class AssignmentDetailViewModel(MobileAssignmentsApiClient api, Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider authState, ILogger<AssignmentDetailViewModel> logger)`:
  - State: `AssignmentSummaryDto? Item` (+ `NotFound` derived property), `AssignmentRecipientDto[]? Recipients`, `SubmissionForReviewDto[]? Submissions`, `SubmissionDetailDto? SelectedSubmission`, per-surface busy/error flags, `IDisposable` CTS.
  - `LoadAsync(Guid id, ct)`: `GetByIdAsync` (not-found ⇒ `Item` stays null, `NotFound = true`), then independent best-effort `GetRecipientsAsync` + `ListSubmissionsAsync` — each failure degrades to null + a `LogWarning`, never failing the page (mirrors `Detail.razor:330-390`).
  - `ReloadAsync()` — re-reads the item after a lifecycle action (mirrors `ReloadItemAsync`, `Detail.razor:399-412`).
  - `PublishAsync/UnpublishAsync/CloseAsync/SubmitForApprovalAsync` — call the API + `ReloadAsync` (mirrors the approval handlers' reload-after-act pattern, `Detail.razor:421-436`).
  - `SubmitApprovalDecisionAsync(bool approve)`: extracts the acting teacher from `teacher_id` (Guid-parsed with `Guid.Empty` fallback — mirrors `GetActingTeacherIdAsync`, `Detail.razor:463-469`), calls `ApproveAsync`/`RejectAsync`, then `ReloadAsync`, surfacing failure in `ApprovalError` (never throwing through the UI).
  - `SelectSubmissionAsync(Guid studentId)` → `GetSubmissionAsync` (read-only view model; the review/grade *forms* are out of slice scope — OD-2).
- (W3c) `src/Mobile/SchoolCollab.Mobile.Logic/AssignmentEnumDisplay.cs` — static display formatter (the 17-line `EnumHelper` behaviour, `EnumHelper.cs:1-17`): `GetDescription(Enum)` via `DescriptionAttribute` with name fallback. Header comment records the **conscious duplication** of `Assignments.Application.Helpers.EnumHelper` and why the diff stays out of that project (PR #342).
- Both view models live in `SchoolCollab.Mobile.Logic` (plain `net10.0`) — the OD3 rule above.

**W4 — The two mobile screens (new, head) + head wiring.**

Head wiring — `src/Mobile/SchoolCollab.Mobile/MauiProgram.cs`: add `builder.Services.AddFluentUIComponents();` (precedent `src/SchoolCollab.Admin/Program.cs:41`) and **`builder.Services.AddAuthorizationCore();` (gate P1 — `<AuthorizeView>` resolves `IAuthorizationService`/`IAuthorizationPolicyProvider`, which the head's DI lacks today; no new package — the reasoning is in the execution-log P1 entry)**, both next to the L3 registration block (`MauiProgram.cs:54-56`), plus `builder.Services.AddMobileAssignmentsApiClient(apiOptions);` — the one call that also brings both W3 view models (W2, gate P2-2).

**Considered and rejected (explicit decision):** dropping `<AuthorizeView>` in favour of `TenantGate` alone would also work — but the plan keeps `AuthorizeView` so the slice has a **route-level signed-out affordance distinct from the tenant gate** (an unauthenticated visitor gets the sign-in call to action at the router tree, not the tenant-gate fallback), and it matches the L3 Spike-page gate pattern (`Spike.razor:1-20`); the rejection stands unless a worker run contradicts it.

New files under `src/Mobile/SchoolCollab.Mobile/Components/Pages/Assignments/` (each `.razor` + companion `.razor.css` — the rule requires the pair, `.github/copilot/rules/blazor-components.md:322-328`):

1. **`Index.razor`** — `@page "/assignments"`. Structure, mobile-first:
   - wrapped in `<AuthorizeView>` with `<TenantGate>` inside it (the L3 Spike page is the pattern — `Spike.razor:1-20`: gate renders the claims-bearing branch only when the session is real; the fallback branch shows the sign-in call to action);
   - **status filter** as a horizontal chip row (5 options mirroring `Index.razor:115-121`'s All/Draft/Published/Closed/Scheduled/Archived) — a `FluentSelect` only above the tablet breakpoint if the worker prefers; **search** via `FluentSearch` bound to the VM (`SearchAsync`);
   - **the list is a card list** (no `FluentDataGrid` — desktop grid shape excluded): loading spinner (`FluentProgressRing`), error bar (`FluentMessageBar`), and the server page's empty-state message logic (search-aware, `Index.razor:96-100`) are all kept;
   - no inline `style=` anywhere — everything in `Index.razor.css`.
2. **`AssignmentCard.razor`** — one `AssignmentSummaryDto` row-card: title (navigates to `/assignments/{Id}`), status badge (the WS-A2 Appearance rule — Published/Scheduled Accent, Closed/Archived Neutral, Draft Lightweight, `Index.razor:47-59`), type/grading/audience display strings + review-state chip where `RequiresApproval`, due date + max score, and a **status-dependent action row** of small `FluentButton`s mirroring `BuildAssignmentActions` (`Index.razor:428-478`) minus the excluded actions (OD-2/OD-3): Draft → Publish or Submit-for-approval; Published → Unpublish + Close; Scheduled → Publish now; Closed/Archived → none. Delete is rendered for Draft/Published rows and **must be confirm-gated** (`IDialogService` confirmation with the desktop's confirm message text — `Index.razor:449-454,468-472`); Optimistic-action callbacks go through the VM.
3. **`Detail.razor`** — `@page "/assignments/{Id:guid}"` with `[Parameter] Id`. Structure: `<ErrorBoundary>` (desktop pattern, `Detail.razor:14-296`), loading/not-found/error states (`:18-28`), then wrapped in `AuthorizeView`+`TenantGate` like Index:
   - **header card**: title, status badge + mandatory-review badge (`:39-51`), and a read-only field block reusing `FieldDisplay` **unmodified** (L1's spike precedent — `Spike.razor:29-32`) for type, grading format, audience, due date, max score, description;
   - **lifecycle action bar** (bottom-anchored on phone): the same publish/unpublish/close actions as the card plus Submit-for-approval (mirrors `Detail.razor:96-133`), Duplicate **excluded** (OD-3);
   - **approval panel** when `Item.RequiresApproval`: chip + Approve/Reject buttons wired to the VM (mirrors `:110-133`, handlers via OD3's acting-teacher extraction);
   - **Recipients section** — mobile card rows of `AssignmentRecipientDto` (owner/channel/role display strings, Active/Inactive badge, broadcast Yes/No — mirroring `:134-165`), via the child component below;
   - **Submissions section** — list of `SubmissionForReviewDto` (student, version, review state, last submitted) with a read-only per-student view: versions + review result (mirrors `:167-260` minus the review/grade forms — OD-2).
   - **out**: `AssignmentAuthoring` overview tab, sign-off tab, notifications tab, all authoring routes.
4. **`AssignmentRecipientsSection.razor`, `AssignmentSubmissionsSection.razor`** — the two read-only child sections so `Detail.razor` stays a composition surface; they take the already-loaded arrays + busy flags as parameters (no independent fetching — the VM owns every call).

**Adaptive layout (≤ 400 dp first, widened):** both `.razor.css` files define the phone layout as the base and widen with a single `@media (min-width: 641px)` block per file: Index switches the card list to a 2-up grid; Detail places the lifecycle action bar and the two sections into a two-column main/aside. One breakpoint per file, recorded in a `:root`-comment inside each file (no framework import).

**W5 — Tests (new, `SchoolCollab.Mobile.Tests.Unit` — ubuntu-runnable, MTP + FluentAssertions per the existing suite).**

1. **`MobileAssignmentsApiClientTests.cs`** — a stub `HttpMessageHandler` (echo of method/route/body; the existing `BearerTokenDelegatingHandlerTests.cs` is the stub precedent) asserting, per method: the exact route (`/assignments`, `/{id}`, `/{id}/publish`, …), verb (`GET`/`POST`/`DELETE`), and body (`null`, `PublishAssignmentRequest`, `ApproveAssignmentRequest`); null-on-404 for `GetByIdAsync`/`GetSubmissionAsync`; and a JSON **string-enum round-trip** (`ListAsync` deserializes a payload carrying `"published"`/`"draft"` status names — the converter set is the point).
2. **`AssignmentsListViewModelTests.cs`** — initial load maps rows + logs; status filter reloads; `SearchAsync` filters client-side (case-insensitive, empty search clears); optimistic publish flips the row and a 500 from the stub **rolls it back** and sets `Error`; delete removes the row; cancelled token is a silent no-op.
3. **`AssignmentDetailViewModelTests.cs`** — happy load (item + recipients + submissions); not-found ⇒ `NotFound`; recipients endpoint failure ⇒ recipients null, page still loaded (`Error` for the page stays null — degrade semantics); approve extracts the acting teacher's `teacher_id` from a fake `AuthenticationStateProvider` (a hand-built `AuthenticationState` with the claim — same claim spelling the realm mapper emits) and reloads; approve failure ⇒ `ApprovalError`, page intact; `SelectSubmissionAsync` fetches the read view.
4. **`MobileClientRegistrationTests.cs`** (existing file, additive): one new test asserting `AddMobileAssignmentsApiClient` registers `MobileAssignmentsApiClient` with the configured `MobileApi.AssignmentsApi` base address and the bearer handler in its pipeline, **plus the two W3 view-model registrations (transient — gate P2-2)** (mirrors the Settings group's registration tests, `MobileClientRegistrationTests.cs:38-52` style; note `Resolve(AssignmentsApi)` uses the **Assignments** address `appsettings.json:4`, not the Settings one).

**W6 — `maui-safe-area` (insets + keyboard), Blazor Hybrid recipe (skill `~/.agents/skills/maui-safe-area/SKILL.md:228-242`).**

- `src/Mobile/SchoolCollab.Mobile/MainPage.xaml` gains the .NET 10 API `SafeAreaEdges="None"` on the page (the skill's Blazor Hybrid row: `None` on page; CSS `env()` for insets) — exactly one edit point.
- `src/Mobile/SchoolCollab.Mobile/Components/Layout/MainLayout.razor.css` (currently empty) carries the inset handling: `padding: env(safe-area-inset-top) env(safe-area-inset-right) env(safe-area-inset-bottom) env(safe-area-inset-left);` on the layout root, so **every** page (both new screens) inherits the insets; the index.html host already provides `viewport-fit=cover` (`wwwroot/index.html:6`) and the template's `status-bar-safe-area` div (`:21`).
- Keyboard: nothing per-screen — `SafeAreaEdges="None"` + the webview's default resize behaviour is the settled Blazor Hybrid posture per the skill; if the worker finds a need for a per-screen soft-input edge (e.g. a focused entry under a stuck keyboard), that is a recorded deviation, not a silent re-architecture.
- **Verification boundary (honest, L3.3-exact):** this environment has **no device or emulator**, so AC-2's *rendered* verification (status-bar/notch insets, keyboard push-up) cannot be mechanically asserted here. What is mechanically checkable in-round: the two W6 file edits exist, `MainLayout.razor.css` contains `env(safe-area-inset-*)`, and the scoped-CSS guard (no inline style) holds. The on-device leg is carried **OPEN** in `## Acceptance`, exactly as L3 carried its device criterion (`round-mobile-credential.md` Status: "criterion 3 carried OPEN — owner-run pending"; same shape here for L4.2).

### Expected files

```
src/Mobile/SchoolCollab.Mobile.Logic/MobileAssignmentsApiClient.cs            (new)
src/Mobile/SchoolCollab.Mobile.Logic/MobileAssignmentsClientRegistration.cs   (new)
src/Mobile/SchoolCollab.Mobile.Logic/AssignmentsListViewModel.cs              (new)
src/Mobile/SchoolCollab.Mobile.Logic/AssignmentDetailViewModel.cs             (new)
src/Mobile/SchoolCollab.Mobile.Logic/AssignmentEnumDisplay.cs                 (new; conscious copy of EnumHelper — header records why)
src/Mobile/SchoolCollab.Mobile.Logic/SchoolCollab.Mobile.Logic.csproj         (+1 ProjectReference: Assignments.Contracts only)
src/Mobile/SchoolCollab.Mobile/MauiProgram.cs                                 (+AddFluentUIComponents(); +AddAuthorizationCore(); +AddMobileAssignmentsApiClient(apiOptions))
src/Mobile/SchoolCollab.Mobile/MainPage.xaml                                  (+SafeAreaEdges="None")
src/Mobile/SchoolCollab.Mobile/Components/Layout/MainLayout.razor.css         (+env safe-area insets)
src/Mobile/SchoolCollab.Mobile/Components/Pages/Assignments/Index.razor(.css)         (new, 2 files)
src/Mobile/SchoolCollab.Mobile/Components/Pages/Assignments/AssignmentCard.razor(.css) (new, 2 files)
src/Mobile/SchoolCollab.Mobile/Components/Pages/Assignments/Detail.razor(.css)        (new, 2 files)
src/Mobile/SchoolCollab.Mobile/Components/Pages/Assignments/AssignmentRecipientsSection.razor(.css)   (new, 2 files)
src/Mobile/SchoolCollab.Mobile/Components/Pages/Assignments/AssignmentSubmissionsSection.razor(.css)  (new, 2 files)
tests/SchoolCollab.Mobile.Tests.Unit/MobileAssignmentsApiClientTests.cs       (new)
tests/SchoolCollab.Mobile.Tests.Unit/AssignmentsListViewModelTests.cs         (new)
tests/SchoolCollab.Mobile.Tests.Unit/AssignmentDetailViewModelTests.cs        (new)
tests/SchoolCollab.Mobile.Tests.Unit/MobileClientRegistrationTests.cs         (additive — 1 new test)
```

**Explicitly untouched:** anything under `src/Assignments/` (client, helpers, screens, ModuleServices — 0 edits; PR #342's area), `documents/specs/`, `.github/workflows/ci.yml` (the `ENABLE_MAUI_CI` gate, pin 3), `Directory.Packages.props` (no new packages — FluentUI and the HttpClient pieces are already in the closure via Admin.Shared/Logic), every `appsettings*.json` (`MobileApi.AssignmentsApi` is already configured, `appsettings.json:4`), the root `SchoolCollab.slnx` (no new projects; Logic and Tests are already members), the realm JSON (L3's work, done).

### Acceptance criteria (each discriminating; §8 L4's four criteria are the spine)

- **AC-1 (L4.1 — mobile-first + CSS isolation).** Every new `.razor` has its companion `.razor.css`; `grep -R "style="" -l -- src/Mobile/SchoolCollab.Mobile/Components` is **empty**; `Index.razor.css` and `Detail.razor.css` each carry the ≤400 dp base layout plus exactly one `@media (min-width: 641px)` widening block; the list renders as cards (no `FluentDataGrid`/`EntityGrid` in the new files). Separating what is mechanically checkable here (the greps + file presence) from what is rendered-only: the *visual* correctness of the form factors belongs to the UI tester/device leg, not to this check.
- **AC-2 (L4.2 — `maui-safe-area`).** Mechanically: `MainPage.xaml` carries `SafeAreaEdges="None"`; `MainLayout.razor.css` carries `env(safe-area-inset-*)` and no other file duplicates it; `wwwroot/index.html:6` keeps `viewport-fit=cover` (already present — must not regress). Open (**carried, not met**): rendered verification of insets + keyboard avoidance on both form factors — device-only, owner-run, recorded exactly as L3 carried L3.3.
- **AC-3 (L4.3 — view-model tests on ubuntu).** `dotnet test` over `SchoolCollab.slnx` at the repo root: the four W5 test classes green (parent's authoritative number; the suite was 83/0 at L3's accept) — and `Mobile.Tests.Unit`'s project gains **no platform-dependent dependency** (net10.0-only references).
- **AC-4 (L4.4 — the slice's severance-or-copy decision planned in its own round).** This document IS the deliverable: the resolved feasibility fact (client's `Core.AssignmentPolicies` usage = `EffectiveAssignmentPolicy` only, which stays in `Core` and is a closure-level FrameworkReference carrier — so option (i)-verbatim is infeasible) and the recommendation (option (ii), W1) are recorded with citations; the worker's diff honours it: `git diff --name-only` intersected with `src/Assignments/` is **empty**.
- **AC-5 (no new dependency surface).** The only ProjectReference added this round is `Mobile.Logic → Assignments.Contracts`; `git diff -- SchoolCollab.slnx Directory.Packages.props` is **empty**; `Mobile.Logic.csproj` keeps `<TargetFramework>net10.0</TargetFramework>` with no package additions beyond what L3 pinned.
- **AC-6 (config-only addresses).** The diff review greps `src/Mobile` for `http(s)?://` C# literals: **0 additions** (spec D8; the guard's `excludedHosts` means CI cannot catch them — §6).
- **AC-7 (build + gates).** Parent-owned authoritative builds: `dotnet build SchoolCollab.slnx` 0 errors; the head's android TFM still builds (`dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj -f net10.0-android`, 0 errors — the closure property; the CI job itself stays skipped behind `ENABLE_MAUI_CI`, pin 3); `SchoolCollab.ArchitectureTests.Unit` green (no new unregistered project, no framework-reference regression).
- **AC-8 (round hygiene).** Tier 3 sections all filled; the round ships `diffs-mobile-assignments-slice.patch` generated from base `fd10e19d` with the patch path **reset before freezing** (the L2 lesson — never `git add -A`).

### Open decisions (grill format — for the owner to settle)

- **OD-1 — the structural question: the slice-scoped mobile client vs moving the server client (the brief's options (i)/(ii)/(iii)).**
  **Recommend (ii)** — a slice-scoped `MobileAssignmentsApiClient` in `Mobile.Logic` with exactly the 12 methods (W1's table), registered against `MobileApiOptions.AssignmentsApi` + the bearer handler. **Cost:** a second code path for the same wire shapes (drift risk, mitigated by per-method mirroring of the cited server client and by the JSON round-trip tests; the *move* becomes attractive again if/when a second module needs ≥20 more assignments methods, and then it must resolve `EffectiveAssignmentPolicy`'s home with the owner, because a verbatim move is **infeasible** — the record is pinned to stay in `Core` (L2 round-3 pin 1) and `Core` re-imports the framework reference). Cost of (i): besides the above, a churn across ~26 `.razor` files and a collision with open PR #342 — rejected.
- **OD-2 — is the submissions review/grade flow inside the detail slice?**
  **Recommend NO** — the detail slice ships the read-only submissions view (list + per-student read view); the review/grade forms (`ReviewAsync`/`ReviewSubmissionAsync` — the desktop's EditForm surfaces, `Detail.razor:93-120,226-246`) defer to a follow-up round: they are forms-first surfaces whose mobile pattern (soft input, validation layout) deserves its own round with the safe-area keyboard leg verified. Cost of including: the detail screen balloons to ~two more full forms and pulls `ReviewFormModel`-shaped validation into a round whose acceptance has no device to observe keyboard behaviour on.
- **OD-3 — are Duplicate / Create / Edit in the list+detail slice?**
  **Recommend NO** — Create/Edit navigate to the authoring surface (the 37-screen train's separate slice: `/assignments/create`, `/assignments/{id}/edit` have no mobile route and pin the out-of-`Assignments.Application` constraint); Duplicate's value shows only where you can then edit the copy, and its success path is a full list reload (`Index.razor:355-376`) — deferred with the authoring slice. Cost of including: either a dead route or a re-opened authoring question this round is not scoped to decide; the *wire* cost is trivial (one POST), so it stays cheap to add later.
- **OD-4 — spec §9's OD3 (how much of the Logic split is enforced): settled or open?**
  **Recommend: record as settled-by-precedent this round** (both VMs + client + registration live in `Mobile.Logic`; OD3's adjudication is spec §9's D3 + L3's OD-4, not something to re-litigate), without adding a new architecture test this round. Cost: no mechanical enforcement of the rule (a future round could hoist logic into the head without a failing test) — the backlog's separate G5 item (`ui-implementation-backlog.md:369-372`) is the right vehicle for a guard, not this round.

### Constraints the worker must honor

- Citation discipline: every repo fact the **Worker Report** asserts carries `file:line`; no fact is restated from this plan without re-verifying it against the tree (`documents/specs/round-runner-model-policy.md`, decision 4).
- **Zero edits under `src/Assignments/`** (pin 2 + PR #342 conflict avoidance) and under `documents/specs/` (parent-acceptance task).
- No `http(s)://` C# literal anywhere under `src/` (pin 4; the diff review greps).
- CPM: no `Version=` attributes on any `PackageReference` (none are added this round; NU1008/NU1009).
- `.razor` files follow `.github/copilot/rules/blazor-components.md` (companion `.razor.css`, no inline `style`); C# follows `.github/copilot/rules/dotnet-best-practices.md`.
- No feature flags added; runtime, mutable feature-flag registration is the Config service's, and L4 reads none directly (the reads the slice makes are the server-derived `RequiresApproval` per row — `ContractTypes.cs:191-215`, `Index.razor:98-108` note).
- The parent owns authoritative builds/tests; the worker may compile-verify the `net10.0` pieces per AGENTS.md's in-session build rule but does NOT run device-TFM builds or tests as acceptance evidence.
- **One writer** per working tree; no committing/pushing (owner-gated).
- Repo-scoped searches only (`git grep`/`rg` under `src/`, `tests/`, `documents/`); at most 2 attempts per fact; never `find /`.

### Gates (the reviewer/UI-tester dispatch check from the Tier 3 skill)

- UI: yes — two new screen files + child components are `.razor`-involving work; the **UI tester is dispatched** (minimax-m3; static adversarial pass, per the L3 precedent) — L4 is exactly the layer where that leg pays.
- Contract: none — no wire changes (all calls mirror the existing server routes verbatim).
- Migrations: none. Secrets: none. Cross-module write-hop: none (Bearer + relative paths, one new read/write client of an existing API).

### Scope — out (explicitly)

The authoring/create/edit surfaces (`/assignments/create`, `/assignments/{id}/edit`, `AssignmentAuthoring`); the review/grade + sign-off + notifications tabs (OD-2, and `SignOffSection`/`NotificationFailuresSection` are separate sections pulling more contracts than the first slice needs); `GetEffectivePolicyAsync`/`GetRecipientPreviewAsync` (authoring helpers); duplicate (OD-3); token refresh changes (L3 settled — OD2 of the spec, adjudicated in L3's round); any CI/workflow change; `documents/specs/` edits; AppHost wiring; committing/pushing.

---

## Worker Report

**Transcribed by the parent from the worker run (deepseek-v4.1-flash, 2026-10-13).**

### Before any file was written — four verified blockers, all adjudicated and approved

The worker's first pass over the plan raised four blockers, each verified against the tree before
the parent was asked; the parent approved all four, so the diff carries four small amendments
beyond the plan's literal file list (each recorded again under `## Acceptance` — Amendments):

1. **`AddCascadingAuthenticationState()` — the gate's fix was necessary but insufficient.**
   `AuthorizeViewCore` throws without a cascading `Task<AuthenticationState>`, which
   `AddAuthorizationCore()` alone does not supply. The head wiring adds both
   (`src/Mobile/SchoolCollab.Mobile/MauiProgram.cs:68-69`), with a comment explaining the
   pairing (`:64-66`).
2. **`MainLayout.razor` — not in the plan's expected-file list.** The plan's W6 treated
   `MainLayout.razor.css` as the only layout touch, but a `@Body`-only layout can never match its
   own scoped CSS (Blazor scoped styles need an element in the layout to carry the scope
   attribute), so the layout gained a scoped root element (`.layout-root`) — without it AC-2
   would have been mechanically unsatisfiable — plus `<FluentDialogProvider />`, which the
   confirm-gated delete dialog requires. Approved deviation: `MainLayout.razor(+.css)` enters
   the patch.
3. **The `SignOffStateDto` string-enum converter.** The API host writes
   `"signOffState":"None"` on every submission-detail payload; without the converter in the
   client's `JsonSerializerOptions`, every `GetSubmissionAsync` read would have thrown on first
   read. The converter set carries it (`src/Mobile/SchoolCollab.Mobile.Logic/MobileAssignmentsApiClient.cs:60-62`, tolerance comment in place).
4. **No toasts.** The desktop pages depend on `IToastService`; the mobile slice renders the same
   outcomes as inline message/state surfaces, so no toast dependency is registered or consumed —
   the head's new wiring stays exactly as planned plus the three approved service calls
   (`AddFluentUIComponents`, `AddAuthorizationCore`, `AddCascadingAuthenticationState` —
   `MauiProgram.cs:67-69`).

### Delivered — W1–W6 as planned

- **W1 — `MobileAssignmentsApiClient`** (`src/Mobile/SchoolCollab.Mobile.Logic/MobileAssignmentsApiClient.cs`, new):
  the **12** slice methods, each mirroring the server client's route/verb/body/log semantics (the
  plan's W1 table), relative paths only (no base-address literal — D8), with the complete
  string-enum converter set for the read path (the slice-touching enums, the documented
  `NotificationKindDto`/`ContactChannelDto` numeric-wire tolerance note, and the
  `SignOffStateDto` converter above).
- **W2 — registration** (`MobileAssignmentsClientRegistration.cs`, new):
  `AddMobileAssignmentsApiClient(apiOptions)` — `ThrowIfNull` both, `Resolve(AssignmentsApi)`,
  `TryAddTransient<BearerTokenDelegatingHandler>()` (idempotent beside L3's), the one
  `AddHttpClient<MobileAssignmentsApiClient>(…)` — plus both view models transient (gate P2-2).
  The csproj gains the Contracts-only reference with the load-bearing comment pinning why the
  project references `Assignments.Contracts` and **never** `Assignments.Application` (the
  FrameworkReference carrier L2 removed) — note: that comment
  (`SchoolCollab.Mobile.Logic.csproj:28-31`) greps as a "carrier" string and is a comment, not a
  reference.
- **W3 — the two view models** (`AssignmentsListViewModel.cs`, `AssignmentDetailViewModel.cs`,
  new): the list VM's state machine (load, status-filter reload, client-side case-insensitive
  search, four optimistic lifecycle actions with rollback, delete-with-removal,
  cancellation-swallowing, spinner ownership when a newer load supersedes an older one), the
  detail VM's load (not-found ⇒ `NotFound`; independent recipients/submissions enrichment each
  failure-degraded, never failing the page; `teacher_id` claim extraction with `Guid.Empty`
  fallback; reload-after-lifecycle-act; approval failure surfaced on the approval surface, page
  intact), and `AssignmentEnumDisplay.cs` (the bounded 17-line `EnumHelper` copy, header
  recording the conscious duplication and the PR #342 avoidance).
- **W4 — the two screens + child components** (`Components/Pages/Assignments/`: `Index.razor(+.css)`,
  `AssignmentCard.razor(+.css)`, `Detail.razor(+.css)`, `AssignmentRecipientsSection.razor(+.css)`,
  `AssignmentSubmissionsSection.razor(+.css)` — all new): `AuthorizeView` + `<TenantGate>` on both
  routes with a per-page signed-out call to action, the mobile card list (no `FluentDataGrid`),
  the status chip row, the confirm-gated delete dialog, the detail header card + lifecycle bar +
  approval panel + the two read-only sections, mobile-first CSS with exactly one
  `@media (min-width: 641px)` widening block per page (`Index.razor.css:75`, `Detail.razor.css:118`),
  scoped CSS throughout, no inline `style=`.
- **W6 — safe-area handoff** (`MainPage.xaml` carries `SafeAreaEdges="None"` at `:7`;
  `MainLayout.razor.css` holds the single `env(safe-area-inset-*)` padding source at `:11`, with
  a header comment explaining the edge-to-edge + CSS recipe and why there is exactly one padding
  source).
- **W5 — tests** (`tests/SchoolCollab.Mobile.Tests.Unit/`): **40 new tests, 0 failures** (parent's
  authoritative **123/0** = the 83-strong L3 suite + these):
  - `MobileAssignmentsApiClientTests.cs` — **17**: per-method route/verb/body identity against a
    stub `HttpMessageHandler` (echoing method/route/body), null-on-404 for `GetByIdAsync` and
    `GetSubmissionAsync`, the string-enum round-trip (status names, recipients names, the
    `"signOffState"` name), and the reject-path throw.
  - `AssignmentsListViewModelTests.cs` — **12**: load mapping, silent cancelled-load, filter
    reload, case-insensitive search + empty-search clear, optimistic publish flip +
    rollback-on-failure, submit-for-approval status flip, delete removal, no-row no-op, spinner
    ownership, and post-dispose idempotence/no-ops.
  - `AssignmentDetailViewModelTests.cs` — **10**: happy load + both enrichments, not-found,
    recipients-degrade, reload-after-publish, approve/reject with the session's `teacher_id`
    claim, the `Guid.Empty` claim fallback, approval-failure error on the approval surface with
    the page intact, `SelectSubmissionAsync` + clear-back-to-list, reload-failure keeps the item.
  - `MobileClientRegistrationTests.cs` — **1 additive**: `AddMobileAssignmentsApiClient` binds the
    client to the configured `MobileApi.AssignmentsApi` address with the bearer handler in its
    pipeline and registers both screen view models (backward-compatible with L3's existing
    assertions in the file).

### Constraints honored

- **Zero edits under `src/Assignments/`** — the parent's authoritative grep: **0 files touched**.
- **No `http(s)://` C# literal under `src/Mobile`** — the parent's authoritative grep: clean
  (D8; the CI guard's `excludedHosts` cannot catch these).
- CPM clean (no `Version=` on any `PackageReference`); no CI/workflow change (pin 3);
  `documents/specs/` untouched; one-writer honored; nothing committed or pushed (owner-gated).

---

## Review

Reviewer: kimi-k2.7-code throughout. The verdict chain, recorded in order:

1. **Plan gate → REVISE (1 P1 + 3 P2), then ACCEPT on the revised sections.** The P1: the plan
   wrapped both screens in `<AuthorizeView>` without `AddAuthorizationCore()` — as wired, the
   first `<AuthorizeView>` render would throw. The three P2s: the "13 methods" count (the screens
   call exactly **12** — the corrected count and table are in the plan); the two view models
   never registered (folded into W2's extension); and eight drifted citations, including the
   gate's own `MauiProgram.cs:50-51` (true: `:55-56`).
2. **Base correction (supervisor decision, executed before plan writing).** The brief first
   declared L4 branched from `origin/main` (`6cb415ec`) — which holds L2 but **not** L3 — and the
   orchestrator caught it; the round's real base is **`fd10e19d`** (L3's tip), and the branch was
   reset there. Per repo policy a round building on an unmerged layer branches from that layer's
   tip. **Standing risk:** if L3 is reworked before it lands, L4's base moves and this branch
   rebases onto L3's new tip.
3. **Review lane infrastructure failure — recorded honestly, not a code problem.** The planned
   three-slice async review failed to start **all three** children — "*Background children
   require the host npm package (@earendil-works/pi-coding-agent) … does not provide
   @earendil-works/chord*" — with `Resume: unavailable`. The reviews therefore ran as **two
   blocking children of the same protocol** — slice A (the implementation) and slice B (tests +
   doc) — which is the mode every other run this session has used.
4. **Slice A (implementation) → OK with notes, 0 P1, 1 P2.** It verified all **12** client
   methods' route/verb/body/response-identity against the server client; the converter set
   complete for the read path; the tenancy posture (bearer-only, no tenant-propagation handler on
   a user-agent call); the view-model state machine (rollback, degradation, cancellation,
   `Guid.Empty` claim fallback, spinner ownership); the registrations (bearer handler,
   `AssignmentsApi` address, both VMs transient); the copied `AssignmentEnumDisplay` header; the
   four approved amendments; scoped-CSS isolation; the safe-area handoff. **The P2 — the iOS
   double top inset:** `MainLayout.razor.css:11` pads by `env(safe-area-inset-top)` while the
   pre-existing template spacer `wwwroot/app.css:65-72` (under
   `@supports (-webkit-touch-callout: none)`) already applies the same inset via the
   `.status-bar-safe-area` div `wwwroot/index.html:10` carries — the two combine into a doubled
   top inset on iOS. Recorded as the specific thing the device leg (AC-2 / L4.2) must check
   first.
5. **Slice B (tests + doc) → tests confirmed discriminating; BLOCK raised against the doc's
   pre-accept state.** It verified the converter, registration, rollback / degrade / cancellation
   / claim-fallback / spinner-ownership / post-dispose tests would each fail against a broken
   subject; that no method-agnostic matcher could mask a wrong route or verb; that the additive
   `MobileClientRegistrationTests` edit stays backward-compatible with L3's tests; and that AC-2
   is carried OPEN. Its P1 was that the doc's expected-file accounting still said **33** while
   the patch has **25**, that the four amendments and the five judgement calls were unrecorded,
   and that the pending sections were still placeholders. **Parent adjudication: the pending
   sections are the accept run's own job (this document — it writes them now), so that half is
   expected; the reconciliation and the amendment/judgement records are real and are written
   under `## Acceptance` below.**
6. **Worker (deepseek-v4.1-flash) delivered W1–W5** with the four pre-implementation blockers
   (recorded in the Worker Report) verified and parent-approved before any file was written —
   zero rework passes after delivery.

No P1 anywhere in the delivered diff. The slice-A P2 (iOS double inset) lives in the residuals;
the slice-B BLOCK resolved into this document's own acceptance records.

---

## Acceptance

### Authoritative build/test numbers (parent-run, 2026-10-13)

| Check | Result |
|---|---|
| `dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj -f net10.0-android` | **0 errors, 0 warnings** (the L2 closure property holds) — re-confirmed 0/0 by the parent's post-rework pass after R1–R11 |
| `dotnet build SchoolCollab.slnx` | **0 errors** — re-confirmed by the parent's post-rework pass |
| `SchoolCollab.Mobile.Tests.Unit` | **141 / 0** (83 L3 baseline + 40 accept-run + **19 UI-tester-rework tests**) — re-confirmed 141/0 by the parent's post-rework pass after R1–R11 |
| `SchoolCollab.ArchitectureTests.Unit` | **111 / 0** — re-confirmed by the parent's post-rework pass |
| `SchoolCollab.Core.Tests.Unit` | **126 / 0** |
| `SchoolCollab.Admin.Tests.Unit` | **651 / 0** — first pass showed 1 failure (`Grade5Selected_StreamPickerLoads_NonNullStreamGuids`), which passed on two consecutive solo re-runs. **Observation, not a defect:** the test is pre-existing (`EnrollStudentDialogBunitTests.cs`) and the failure is a FluentUI cascade **flake** — the third instance of that family. |
| `http(s)://` literal grep, `src/Mobile` | clean (AC-6 met; D8) |
| Files touched under `src/Assignments/**` | **0** (AC-4's diff leg met) |
| Framework-reference carriers | **still 5** — no new carrier (AC-5's closure property intact). Caveat: a carrier-string grep false-positives at `SchoolCollab.Mobile.Logic.csproj:28-31`, but those lines are a **comment** (why the project references `Assignments.Contracts` rather than `Assignments.Application`), not a reference — L2's acceptance recorded the same trap from the other side. |

### §8 L4 criteria — the spine

- **L4.1 — mobile-first ≤ 400 dp + tablet widening, CSS-isolation conventions — MET
  (mechanically).** Every new `.razor` has its companion `.razor.css`; the phone layout is the
  base of both page stylesheets with exactly one widening block each (`Index.razor.css:75`,
  `Detail.razor.css:118` — one `@media (min-width: 641px)` per file); no inline `style=`; no
  `FluentDataGrid` in the new files. The *rendered* correctness of the two form factors is
  device-bound and belongs to the UI-tester / device leg, not to this check.
- **L4.2 — `maui-safe-area` — OPEN (carried, device-bound).** Mechanically met: `MainPage.xaml`
  carries `SafeAreaEdges="None"` (`:7`); `MainLayout.razor.css` is the single
  `env(safe-area-inset-*)` padding source (`:11`) with no duplicate; `wwwroot/index.html`
  keeps `viewport-fit=cover`. **Carried OPEN:** rendered verification of insets + keyboard
  avoidance on both form factors — device-only, owner-run, recorded exactly as L3 carried L3.3.
  The specific thing the device leg must check first: the iOS **double top inset** (slice A's
  P2 — `MainLayout.razor.css:11` vs the template spacer `wwwroot/app.css:65-72` fed by
  `wwwroot/index.html:10`).
- **L4.3 — the slice's view models covered by `Mobile.Tests.Unit` (ubuntu-runnable) — MET.** The
  **59 new tests** in four classes: client route/verb/body identity (17), list-view-model state
  machine (12), detail-view-model load/approval/degradation (10), and the additive registration
  test (1, backward-compatible with L3's assertions in the same file) — plus the **19
  UI-tester-rework tests** (R1–R11; discrimination and its two honestly-flagged limits in
  `## UI Tester`).
- **L4.4 — the slice's own severance-or-copy decision — MET as a decision.** The slice-scoped
  **12-method** `MobileAssignmentsApiClient` in `Mobile.Logic` + the **bounded 17-line**
  `AssignmentEnumDisplay` copy of `EnumHelper`. The verbatim move is infeasible: the server
  client's `EffectiveAssignmentPolicy` dependency is pinned to `Core` (L2's round-3 pin), `Core`
  remains a closure-level framework-reference carrier, and an interface-only contract would
  require editing `Assignments.Application` — pin-forbidden, and colliding with open PR #342.
  Say explicitly: the **reuse of the pages themselves was not attempted**, per spec D2 (the 37
  screens are reworked fresh in the head).

### File reconciliation — 33 expected → 25 actual

The frozen patch `diffs-mobile-assignments-slice.patch` contains **25 files: 19 new + 6
modified**. The arithmetic, itemized:

- **19 new** = **5** Logic files (`MobileAssignmentsApiClient`,
  `MobileAssignmentsClientRegistration`, `AssignmentsListViewModel`,
  `AssignmentDetailViewModel`, `AssignmentEnumDisplay`) + **10** screen files (Index /
  AssignmentCard / Detail / RecipientsSection / SubmissionsSection, each `.razor` + `.razor.css`)
  + **3** new test files (client / list VM / detail VM) + **1** the round doc itself (swept into
  the frozen patch).
- **6 modified** = `SchoolCollab.Mobile.Logic.csproj` (+1 ProjectReference), `MauiProgram.cs`
  (+4 calls), `MainPage.xaml` (+`SafeAreaEdges="None"`), `MainLayout.razor` (scoped root +
  `<FluentDialogProvider />`), `MainLayout.razor.css` (+insets), `MobileClientRegistrationTests.cs`
  (+1 test).

The plan's expected-file block enumerates **23 paths** — 18 planned-new deliverables + 5
planned-modified. The reviewer's "33" figure came from summing the block's parentheticals as
extra files on top of the 23 ('new, 2 files' × 5, '+1 ProjectReference', '+3 registrations',
'1 new test' — 23 + 10 = 33): a tally-method artefact, not missing deliverables. The two **real**
deltas against the block:

- **+1 `MainLayout.razor`** — the approved worker amendment (a `@Body`-only layout cannot match
  its own scoped CSS; without a scoped root, AC-2 would be mechanically unsatisfiable).
- **−1 conditional `AndroidManifest.xml`** — the plan carried a conditional manifest edit for the
  safe-area recipe; it was **never needed** and never materialized (the L3 attribute-based
  callback pattern, not a manifest edit).

Net: **every planned deliverable is present, none is missing, and the only file beyond the
plan's list is the approved `MainLayout.razor`.** (Snapshot of the accept run's freeze. After
the R1–R11 UI-tester rework, the re-frozen patch stands at **26 files, 5154 lines (19 new)** —
see `## UI Tester` and the execution log.)

### Amendments — the four parent-approved worker blockers

1. `AddCascadingAuthenticationState()` beside `AddAuthorizationCore()` (`MauiProgram.cs:68-69`) —
   `AuthorizeViewCore` throws without the cascading `Task<AuthenticationState>`; the gate's one
   fix was necessary but insufficient.
2. `MainLayout.razor` scoped root element + `<FluentDialogProvider />` — scoped-CSS matchability
   for the layout, and the host provider the confirm-gated delete dialog requires.
3. The `SignOffStateDto` string-enum converter (`MobileAssignmentsApiClient.cs:60-62`) — the host
   writes `"signOffState":"None"`; without it, every submission-detail read would throw.
4. No toasts — the desktop's `IToastService` outcomes are rendered as inline message/state
   surfaces; no toast dependency is registered or consumed.

### Judgement calls — worker discretionary, adjudicated as accepted

1. **Six filter chips** where the plan's text said "5 options" but then listed six names
   (All/Draft/Published/Closed/Scheduled/Archived) — the list was the truth, the count was the
   typo; the chip row mirrors the desktop's six.
2. **Delete offered on Published rows** where the desktop offers it only on Draft — the mobile
   list widens the affordance; the confirm gate (not the row's status) is the safety boundary.
3. **Per-page signed-out call-to-action** rather than a shared child component — two pages, a
   few lines each; a shared gate component would scaffold a second use before a second use
   exists.
4. **Styling hooks only on plain authored elements** in the scoped CSS (no FluentUI styling-
   parameter coupling) — the stylesheets stay the source of truth per the CSS-isolation rule.
5. **Two post-verification robustness guards** — idempotent `Dispose()` and post-dispose no-ops
   on both view models, defensive but cheap, and pinned by tests
   (`AssignmentsListViewModelTests.cs:227` and the dispose no-op assertions).

### Pinned-decision compliance

- **Spec D2** (rework fresh, no `Assignments.Application` reference): honored — **0**
  `src/Assignments/` files in the diff; the only new reference is Contracts-only (AC-5: still 5
  carriers; `SchoolCollab.slnx` and `Directory.Packages.props` untouched).
- **Spec D8** (config-only addresses): honored — the `http(s)://` grep is clean; the client
  binds to `MobileApiOptions.AssignmentsApi` (`appsettings.json:4`).
- CI/workflow untouched (pin 3); no `appsettings*.json` changes; no realm changes (L3's, done);
  one-writer honored; **nothing committed or pushed** (owner-gated).

### Rework (the one UI-tester rework pass)

**One UI-tester rework pass (R1–R11).** Zero worker rework passes after the accept run's
delivery; the single rework pass is the UI-tester leg's R1–R11 — the failure-surface split et
al. The run-1 findings, adjudication, the no-verdict first re-verify and its re-dispatch, and
run 2's verdict are recorded in `## UI Tester`. The Mobile suite went 123 → **141** (19 new
tests) and the parent's post-rework numbers re-confirmed the table above.

### Residuals (non-blocking; each with its follow-up)

1. **AC-2's device leg** — insets + keyboard avoidance rendered on device, owner-run.
   Follow-up: the owner's device pass (the same shape as L3.3's carried OPEN).
2. **iOS double top inset risk** — `MainLayout.razor.css:11` vs the template spacer
   (`wwwroot/app.css:65-72` fed by `wwwroot/index.html:10`). Follow-up: check on device, then
   keep exactly one inset source.
3. **Bearer-only principal vs the combined group + `readerGroup` policies** — L3.3's risk,
   unchanged: whether a Keycloak-bearer-only principal satisfies the assignments API's combined
   policies materializes only at the device leg; if it 403s, the fix is **server-side auth**
   (owner-gated), not a mobile bypass.
4. **The untested `NotificationKindDto` tolerance converter** — the read-side numeric-wire
   tolerance ships without its own round-trip test. Follow-up: add that case to the client's
   tests.
5. **Two conscious duplications carry drift risk** — the 12-method client mirrors the server
   client's wire shapes and the 17-line helper mirrors `EnumHelper`; the round-trip tests pin
   the routes today. Follow-up: a future unification (or a second mobile-module slice needing
   more methods) must resolve `EffectiveAssignmentPolicy`'s home with the owner first.
6. **Delivery dependency: stacked on unpushed L3** — L4 lands as a `gh-stack` layer on L3's PR
   or after L3 merges. Follow-up: if L3 is reworked, rebase this branch onto L3's new tip.
7. **Stale `ReloadWarning` (run 2's new low-severity defect, accepted as a residual)** — a new
   action attempt clears the surface's action error but not `ReloadWarning` (the
   `RunActionAsync` / `SubmitApprovalDecisionAsync` paths of
   `src/Mobile/SchoolCollab.Mobile.Logic/AssignmentDetailViewModel.cs`; `ClearFailures` clears it
   only on successful loads), so a prior "Not refreshed" bar can sit beside a fresh
   "Could not {verb}" bar and read as if it were about the latest attempt. The tester called it
   a small follow-up; the round is at its rework bound. Follow-up: clear `ReloadWarning` at the
   start of each action attempt and pin it with a test.
8. **Detail's action-error bar has no retry control** — the list's action-error bar carries
   "Try again" (`RetryActionAsync` in `AssignmentsListViewModel.cs`); the detail's bar does
   not — the pattern was not carried across the surfaces (run 2's asymmetry, accepted as a
   residual at the rework bound). Follow-up: carry the retry control over to
   `Detail.razor`'s action-error bar.
9. **Three P2s deliberately unfixed** (parent adjudication, run 1): search text surviving
   sign-out; the shared `Admin.Shared` dialog's 420 px width (repo-wide, outside this round's
   file scope); the un-cancelled in-flight search on clear (cosmetic). Each keeps a natural
   home in the next mobile slice or the device pass.

### Verdict

**CLOSED** — all four §8 L4 criteria satisfied on their intended legs, with **L4.2 carried OPEN
(device-bound)** as the round's single residual criterion, exactly as the L3 round carried
L3.3. **Rework: one UI-tester rework pass (R1–R11)**; the tester leg re-verified
**ACCEPTABLE AS IT STANDS** with its two low-severity residuals recorded above.

---

## UI Tester

**Run complete (minimax-m3 — static adversarial pass; no device).** Run 1: **3 P1 + 12 P2**;
adjudicated; reworked (R1–R11); re-verified in run 2 — **ACCEPTABLE AS IT STANDS**, with two
low-severity residuals. The dispatch handover below is preserved as the record of the scope
that was given. The handover, findings and rework sections were written at close; the
pre-rework `file:line` anchors the tester reported no longer resolve in the tree (the R1–R11
rework replaced those files), so findings are keyed to the handed-over surfaces and the one
residual cited here carries current-tree anchors.

### Scope (handover — do not widen)

Changed UI surfaces (all under `src/Mobile/SchoolCollab.Mobile/`):

- `Components/Pages/Assignments/Index.razor` + `Index.razor.css` — the assignments list.
- `Components/Pages/Assignments/Detail.razor` + `Detail.razor.css` — the assignment detail.
- `Components/Pages/Assignments/AssignmentCard.razor` + `AssignmentCard.razor.css` — the list's
  row card.
- `Components/Pages/Assignments/AssignmentRecipientsSection.razor` + `.razor.css` — the detail's
  recipients section.
- `Components/Pages/Assignments/AssignmentSubmissionsSection.razor` + `.razor.css` — the
  detail's submissions section.
- `Components/Layout/MainLayout.razor` + `MainLayout.razor.css` — the layout's scoped root
  (`.layout-root`, the single safe-area padding source) + `<FluentDialogProvider />`.
- `MainPage.xaml` — `SafeAreaEdges="None"`.

What they render:

- **The list** (`/assignments`): the status filter chip row (All/Draft/Published/Closed/
  Scheduled/Archived), client-side title search, the mobile card list via `AssignmentCard`
  (title → `/assignments/{id}`, status badge, type/grading/audience display strings, due date,
  max score, review-state chip where `RequiresApproval`), and the **per-row lifecycle actions**
  — publish or submit-for-approval (Draft), unpublish + close (Published), publish-now
  (Scheduled) — each an optimistic row flip with rollback-on-failure, plus the confirm-gated
  delete (offered on Draft **and** Published rows this slice) with row removal on success.
- **The detail** (`/assignments/{id}`): the header card (status badge, mandatory-review badge,
  the read-only `FieldDisplay` block), the lifecycle action bar, the approval panel (chip +
  approve/reject acting via the session's `teacher_id` claim), and the two read-only child
  sections — recipients rows (owner/channel/role, Active/Inactive, broadcast Yes/No) and the
  submissions list with the per-student read view (entry to it, and clear-back-to-list).
- **The signed-out call-to-action** — rendered per page inside `<AuthorizeView>`'s fallback,
  above `<TenantGate>`'s branch, on both new routes.
- **The confirm-gated delete dialog** — hosted by `MainLayout.razor`'s `<FluentDialogProvider />`.
- **The loading / empty / error / optimistic states** — spinner ownership (a newer load
  supersedes an older one), error bars, the search-aware empty state, not-found on detail, the
  independently-failure-degraded recipients/submissions enrichments, and in-flight optimistic
  row states with rollback.

What they depend on: the L3 auth state (`MobileAuthenticationStateProvider` — one instance
shared by the gates and the claim reads, cascaded by `AddCascadingAuthenticationState`); the
view models (transient per navigation; every call goes through `MobileAssignmentsApiClient`,
owned by the VMs alone); and the `MainLayout.razor.css` inset handoff.

### Mode and boundary

**No device or emulator exists in this environment**, so this is a **static adversarial pass
over the UI behaviour and its failure paths** — reading the components, their scoped CSS, the
view-model contracts, and the auth-state interaction they depend on, and hunting for: broken
state transitions, dead or mis-gated actions, optimistic-rollback inconsistency, error paths
that throw or fail the page, CSS-isolation violations (inline styles, page styles in
`wwwroot/app.css`), and the safe-area double-inset mechanics in the markup/CSS (a static check).
**Anything device-only — rendered insets, keyboard avoidance, rendered layout on real hardware
— is out of scope by construction.** The tester must not widen its scope: no server-side files,
no L3 surfaces, no contract code review.

### Run 1 — findings (minimax-m3, static adversarial pass over the handed-over surfaces)

**3 P1 + 12 P2.** Line anchors refer to the pre-rework tree (lost after R1–R11; the re-frozen
patch is post-rework), so the findings are recorded as filed, keyed to their surfaces.

**The three P1s (the first two share one root cause):**

- **P1-1 — an action failure replaced the whole list with the load-style error bar, and the
  error was never cleared on success.** A failed row action reported on the same `Error`
  surface a failed load uses, and neither `LoadAsync` nor `SetStatusFilterAsync` cleared `Error`
  on success — so one stale action error kept the list hidden with no obvious recovery
  (`Index.razor` + `AssignmentsListViewModel.cs`).
- **P1-2 — the detail's error branch had no Back control** `Detail.razor`'s Back button lived
  inside the loaded branch only; rendering the error branch stranded the user there.
- **P1-3 — a confirm-gated delete on a row already gone** compounded P1-1 (the parent folded it
  into the same root cause).

**The twelve P2s:** search disabling every card's action buttons; an optimistic status flip
contradicting the active filter chip; a chip tap invisible mid-search; hard-coded
"Could not load…" bar titles used for action failures; Approve/Reject rendered for principals
with no `teacher_id` (a guaranteed-fail click); the fixed bottom bar ignoring the bottom inset;
both signed-out fallbacks saying "sign in"; loading spinners with no live region; a silent
post-action reload failure; the shared `Admin.Shared` dialog's fixed 420 px width; search text
surviving sign-out; an un-cancelled in-flight search on clear.

### Parent adjudication (run 1)

- **P1-1 + P1-2 — accepted** as **one root cause**: action failures routed through the
  load-error surface, which also never cleared on success — fix the split, both go away.
- **P1-3 — folded** into that root cause (the stale-row case is a no-op once failures stop
  replacing the list).
- **A prioritised subset of the P2s — fixed** in the rework: the search-vs-actions coupling,
  the optimistic flip vs the filter chip, the mid-search chip tap, the hard-coded failure
  titles, the missing-`teacher_id` gating, the bottom inset, the "sign in" fallback copy, the
  live regions, the silent reload failure, and the reload-divergence surface.
- **Three P2s — deliberately not fixed** (recorded as residuals #9 in `## Acceptance`): search
  text surviving sign-out; the shared `Admin.Shared` dialog width (repo-wide); the cosmetic
  un-cancelled in-flight search on clear.

### Rework pass (R1–R11, deepseek-v4.1-flash worker)

- **Failure surfaces split** — `Error`/`ErrorTitle` own fetches; `ActionError`/`ActionErrorTitle`
  + `RetryActionAsync()` own actions; `ClearFailures()` runs on every successful load (both
  view models).
- **Detail's error branch gained a Back control.**
- **Per-row `IsActionPending(id)`** instead of the global fetch flag (row actions stay alive
  during search and concurrent loads).
- **Filter-aware `DisplayItems`** (both slices), honest through its empty message.
- **Failure-specific bar titles** ("Could not {verb}" instead of "Could not load…").
- **`CanDecideApproval` gating** with an explanatory info bar when the principal carries no
  `teacher_id`.
- **Bottom-inset padding** on the detail's fixed action bar (not fighting the page's own
  reservation).
- **Distinct fallback copy** for the two signed-out branches (no more double "sign in").
- **`role="status"` + `aria-label`** on the loading surfaces.
- **`ReloadWarning` with a Refresh action** replaces the silent post-action reload divergence.

**Tests: 19 new — the Mobile suite 123 → 141.** Discrimination proven the honest way: the
pre-fix behaviour was re-injected into both view models and the suite **failed exactly 12 tests,
then 5 — one per discriminating test**. Two limits the worker flagged up front: **one test is a
regression guard rather than a pre-fix discriminator**, and the **four markup-only fixes (the
Back control, the two live regions' role/label work, the inset padding) have no head test
harness** — they are verified statically in the re-verify, not by tests.

### Re-verify attempt 1 — no verdict; re-dispatched

The first re-verify child **returned no verdict at all** — it misread its task and reported on
`git status`. Per the skill, **a missing verdict is no review, never clean**: it was treated as
no review and **re-dispatched**, not accepted. Recorded explicitly so a silent no-verdict pass
is never mistaken for a pass.

### Run 2 — re-verify verdict: ACCEPTABLE AS IT STANDS

- **P1-1 — resolved** (verified by render-branch tracing): the load-error and action-error
  surfaces are mutually exclusive on `Index.razor`.
- **P1-1/P1-2's shared root** — the two channels are fully separated in the view model, and
  `ClearFailures` runs on both success paths.
- **P1-3 — resolved**: the stale-row delete case is a no-op.
- **`role="status"` is used once per loading state**; the **filter-aware display is honest,
  including its empty message**; and the **inset padding does not fight the page's own
  reservation** (the run-1 P2 fixed without a double-inset regression — the same risk slice A
  flagged for iOS, to be confirmed on device).
- **One new low-severity defect — a stale `ReloadWarning`**: a new action attempt clears the
  surface's action error but not `ReloadWarning`, so a prior
  "Not refreshed" bar can sit beside a fresh "Could not {verb}" bar and read as if it were about
  the latest attempt (`AssignmentDetailViewModel.cs`, the `RunActionAsync` /
  `SubmitApprovalDecisionAsync` paths).
- **One asymmetry — the detail's action-error bar has no retry control** while the list's has
  "Try again"; the pattern was not carried across.

Both run-2 findings are **recorded as residuals rather than reworked** — the tester itself
called them a small follow-up, and the round is at its rework bound (they are residuals #7 and
#8 in `## Acceptance`).

### Post-rework facts (parent, authoritative)

android TFM **0 errors / 0 warnings**; root `SchoolCollab.slnx` **0 errors**;
`Mobile.Tests.Unit` **141/0**; `ArchitectureTests.Unit` **111/0**; re-frozen patch
**26 files, 5154 lines (19 new)**.