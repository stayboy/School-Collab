# Round: mobile-severance (L2 of the mobile train)

**Provider:** pi, full Tier 3, Option A (models: glm-5.3-flash orchestrator + accept, kimi-k2.7-code plan-gate and diff-review, deepseek-v4.1-flash worker, kimi-k2.7-code escalator, minimax-m3 UI tester) — plan-gate substitute for the unavailable skill id ollama/glm-5.3:cloud; a glm-family substitute would put the gate on the plan author's own id, which round-runner-model-policy.md item 3 forbids (gate must differ from the author in family or clearly-bigger tier)
**Status:** CLOSED (`## Acceptance` verdict 2026-10-09) — plan-gate REVISE then ACCEPT; worker implemented; diff review OK with notes; AC-1…AC-8 met; ledger rows added
**Round base:** `fab7dd5e` (full: `fab7dd5eea162fcef4b6a75adeb4848f7aa08bbf`) — branch `feat/mobile-severance`, worktree `School-Collab-mobile-train`. The worktree was **clean at base** (verified 2026-10-09: `git status --short` empty, `git log --oneline -1` = the L1 squash-merge `fab7dd5e feat(mobile): Blazor Hybrid head skeleton, reuse spike, and CI job (#326)`).
**Spec:** `documents/specs/mobile-hybrid-app.md` — §3.2 (the dependency measurement + spike outcome), §8 L2 (the four acceptance criteria this round executes). Predecessor round: `documents/rounds/round-mobile-host-skeleton.md` (L1; carries the NETSDK1082 finding this round resolves).

---

## Execution log (plan-gate round 3 — 2026-10-09)

- **Plan-gate verdict: REVISE — 1 × P1, 4 × P2.** All of it is folded into `## Plan`: the P1
  rewrites W1/W3 (the `SchoolCollab.Core.Abstractions` move); the P2s correct the file count
  (12, not 9), AC-1(b)/AC-4(b) (check for the *absence of `Microsoft.AspNetCore.App`*, never
  "no frameworkReferences entry"), the `Microsoft.AspNetCore.Components.Web` version obligation
  (resolve from what `WebView.Maui` actually pulls), and the wrong "Core has no FrameworkReference"
  measured fact.
- **P1 — evidence (re-verified by the gate in the local NuGet cache):** `Core` **cannot stay on
  the head's closure**. `src/SchoolCollab.Core/SchoolCollab.Core.csproj:9-11` references
  `Microsoft.AspNetCore.Authentication.Cookies` / `.JwtBearer` / `.OpenIdConnect`, and the
  `JwtBearer` + `OpenIdConnect` **nuspecs** each declare `<frameworkReference name="Microsoft.AspNetCore.App" />`
  for `net10.0` (exactly 1 declaration each; `microsoft.aspnetcore.authentication.cookies` and
  `microsoft.aspnetcore.dataprotection.abstractions` carry **0**). So `Core` delivers the
  FrameworkReference to every consumer at closure level **even though its csproj has no
  `<FrameworkReference>` element** — element-absent is not closure-absent. With `Admin.Shared`
  still referencing `Core`, W1–W5 as originally written would have left NETSDK1082 untouched.
- **Owner-pinned decisions (round 3, 2026-10-09) — recorded as pins, NOT re-opened** (full text
  in `## Plan` § "Owner-pinned decisions — round 3"):
  1. Minimal abstraction move, option (a): only the two enums
     (`SignatureRequirementMode`, `NotificationChannel`) move; the policy shapes, both
     `IEffective…Resolver` interfaces and both resolver implementations **stay in `Core`**.
  2. Project name **`SchoolCollab.Core.Abstractions`** (zero packages / project refs /
     FrameworkReferences, `net10.0`), hosting `SchoolCollab.Core.Features` + the two enum
     namespaces; because the project name matches none of them, **every moved file carries an
     explicit `namespace` declaration**.
- **UI-tester waiver: ACCEPTED by the gate**, with the audit evidence it required:
  1. The only `.razor` **edit** in this round is `Error.razor`
     (`src/SchoolCollab.Admin.Shared/Components/Pages/Error.razor`) — removing the
     `[CascadingParameter] HttpContext` property, switching the id expression to
     `Activity.Current?.Id`; the page and its `@page "/Error"` route are kept. The other
     `.razor`-involved work is pure **moves** (4 `.razor` files move with their models and
     `.razor.css` siblings); W3 touches no `.razor` file.
  2. The bUnit suites covering the moved components, which **must be green under AC-3**:
     `ContactsEditorTests.cs`, `TeacherDetailPageBunitTests.cs`,
     `StudentFormModelMappingsTests.cs`, `FormRowOrientationTests.cs`,
     `StudentFormFieldsSectionEditTests.cs`, `EditSidebarLayoutTests.cs` (all in
     `tests/SchoolCollab.Admin.Tests.Unit/`).
  3. `## UI Tester` is marked **not-applicable**, with the parent's rationale and the gate's
     adjudication recorded there.

### Acceptance stage (2026-10-09 — orchestrator-accept run)

- **Diff review logged:** `kimi-k2.7-code`, verdict **"OK with notes"** — 0 × P0/P1; one P2
  (`.github/workflows/ci.yml:192-193` had dropped the plan's prescribed workload rationale).
  Confirmed: W1–W5 implemented as specified, the 12-file contacts move identity-preserving (8
  rename pairs at `similarity index 100%`, 4 models at 94–96%, namespace line only), CPM clean,
  no new `http(s)://` literal, no unrelated deletions. Full block in `## Review`.
- **Parent-applied rework after the review (tiny-rework allowance; parent static check, no
  reviewer re-run):** (1) the `ci.yml` workload rationale comment restored (the reviewer's P2);
  (2) five stale durable-doc pointers to moved files updated (documents-only):
  `documents/configuration.md:753`, `documents/specs/activity-group-enrollment-impl.md:45`,
  `documents/specs/keycloak-ui-auth-integration.md:282`,
  `documents/solution/assignment-request-implementation-details.md:181`,
  `documents/solution/student-guardian-plan.md:187`. Historical `docs/plans/*` and `.pr-body.md`
  deliberately left alone.
- **Patch self-inclusion defect — found and fixed (reusable lesson for every round).** The first
  two patch freezes ran `git add -A`, which staged `diffs-mobile-severance.patch` itself — each
  re-freeze embedded the previous patch as content (1,151 → 2,375 → 3,599 lines, with nested
  `++` hunks). The final freeze excludes the patch (`git reset -q -- <patch>` before
  `git diff --cached`), landing at the correct **44 entries / 1,218 lines / 17 renames**. Lesson:
  never `git add -A` before freezing a round patch — reset the patch path first.
- **Ledger backfill:** `mobile-host-skeleton` (L1) was missing from
  `documents/solution/round-runner-model-ledger.md` — a policy-decision-8 gap ("a round is not
  closed until its row exists"). Both rows (`mobile-host-skeleton`, `mobile-severance`) added at
  accept time.
- **Worker Report transcribed** into the doc from the worker run
  (`ollama-cloud/deepseek-v4.1-flash`) — verbatim-in-substance, condensed only by dropping its
  restatement of the plan.
- **Acceptance written** from the parent's authoritative build/test numbers (android 0 errors /
  0 warnings; slnx 0 errors; 102/632/126/624/1064 tests, 0 failures; AC-4(a) grep = 5 projects;
  AC-8 patch 44 / 1,218 / 17). Status set to **CLOSED**.

---

## Plan

**Goal.** L2 of the four-layer mobile train: sever the mobile head's dependency closure so that
`dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj -f net10.0-android` succeeds
with **0 errors**, then flip the mobile CI job to assert the android TFM instead of the Windows TFM.
The severance is **principled and bounded** (owner pin): it fixes the device build **and** cuts the
server-side weight out of the head's closure — it does not settle for a build-only edit.

**The blocker (L1-proven — do not re-derive, do not re-verify by experiment).**
`net10.0-android` fails NETSDK1082: there is no runtime pack for `Microsoft.AspNetCore.App` for any
android RID. The head acquires that transitive `FrameworkReference` through its project closure
(head → `Admin.Shared` → {`ServiceDefaults`, `Core`, `Students.Core`}; head csproj lines
5–14 document the failure, `src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj:5`).
The escape hatches that DO NOT work (L1-proven, `round-mobile-host-skeleton.md` § "Mid-round
revision"): `DisableTransitiveFrameworkReferenceDownloads` (identical error) and evaluation-time
`FrameworkReference Remove` (re-added by the SDK's `AddTransitiveFrameworkReferences`). The L1
head-side shim (0 errors, 24 MSB3277 against assemblies the app would not ship) is **rejected** —
a lying build that hides exactly what this round must fix. The L1 causation proof also proves what
this round does NOT need to touch: removing the head's `Admin.Shared` reference made the android
errors vanish with `Microsoft.AspNetCore.Components.WebView.Maui` and FluentUI still in place —
so **no shipped NuGet package on the head's path carries a FrameworkReference dependency** (L1
proved this for the packages still on the path once `Admin.Shared` — and with it `Core` — was
severed; `Core`'s own auth packages DO carry nuspec-declared frameworkReferences, which is the
plan-gate P1 and why `Core` must also leave the closure); the only repo `.csproj` FrameworkReference sources on the head's
 closure are exactly two — `Admin.Shared` (`src/SchoolCollab.Admin.Shared/SchoolCollab.Admin.Shared.csproj:10`,
 removed by W1) and `ServiceDefaults`
 (`src/ServiceDefaults/SchoolCollab.ServiceDefaults/SchoolCollab.ServiceDefaults.csproj:11`, dropped from the
 closure by W2 while keeping its own declaration for the server hosts). (Gate P2 (a) fold: this
 sentence was a fragment in the round-3 revision; completed here.)

### Owner-pinned decisions (2026-10-09) — recorded as pins, NOT re-opened

1. **Execution mode: full Tier 3** (plan-gate, worker, diff review, escalation lane;
   the UI leg is dispatched-or-waived per the round's UI verdict — this round: waived, gate
   adjudicated in `## UI Tester`).
2. **Principled and bounded severance** — fix the build AND cut the server-side weight; the minimal
   build-only edit is explicitly not the deliverable.
3. The `Core.Features` abstractions move to a **new zero-dependency project —
   `SchoolCollab.Core.Abstractions`** (the round-3 pin replaced the earlier working name
   `SchoolCollab.Core.Features`) with the `SchoolCollab.Core.Features` **namespace preserved**, so the consumer files are untouched
   (measured today: `git grep -ln "SchoolCollab.Core.Features" -- src tests` = **106 files** across
   `src` and `tests` on base `fab7dd5e`; the spec's "85 consumer files" is the same population
   counted at spec time — the acceptance check below is mechanical against the freshly measured
   list, not the historical count).
4. The `Students.Core`-bound contacts family — the **measured 12-file set: 4 `.razor`
   components + 4 `.cs` models + 4 `.razor.css` siblings** (gate P2 (b) fold: the earlier
   "5 components" count was stale; the measured set is 12 files): their new home is **the
   bounded context that owns the domain types** — `src/Students/SchoolCollab.Students.Application/`.

### Measured facts this plan relies on (measured on base `fab7dd5e`; citations file:line)

- **FrameworkReference sources on the head's closure:** `Admin.Shared`
  (`src/SchoolCollab.Admin.Shared/SchoolCollab.Admin.Shared.csproj:10`) and `ServiceDefaults`
  (`src/ServiceDefaults/SchoolCollab.ServiceDefaults/SchoolCollab.ServiceDefaults.csproj:11`).
  Other repo FrameworkReference carriers (`AI.Chat`, `Assignments.Application`,
  `Settings.Application`, `Students.Application`) are NOT on the head's closure.
  **`SchoolCollab.Core` is a FrameworkReference *carrier at closure level* despite having no
  `<FrameworkReference>` element** (P1): `src/SchoolCollab.Core/SchoolCollab.Core.csproj:9-11`
  references `Microsoft.AspNetCore.Authentication.Cookies` / `.JwtBearer` / `.OpenIdConnect`
  (and `…` through `System.Security.Cryptography.Xml`, csproj:9–24), and the
  `microsoft.aspnetcore.authentication.jwtbearer` and `…openidconnect` **nuspecs** each declare
  `<frameworkReference name="Microsoft.AspNetCore.App" />` for `net10.0` — re-verified in the
  local NuGet cache (exactly 1 hit each; `…authentication.cookies` and
  `…dataprotection.abstractions` carry 0). Element-absent is not closure-absent: `Core` delivers
  the FrameworkReference to every consumer, which is why leaving `Core` on the head's closure
  was rejected by the plan gate (P1) and forced the `SchoolCollab.Core.Abstractions` move in W3.
- **The head's closure:** head → `Admin.Shared` → { `ServiceDefaults`
  (Admin.Shared.csproj:19), `Core` (:20), `Students.Core` (:21) }; `Students.Core` →
  { `ServiceDefaults` (Students.Core.csproj:8), `Students.Contracts`, `Settings.Contracts`,
  `Core` }; `Students.Contracts` → `ServiceDefaults`
  (Students.Contracts.csproj:8). Head refs: Admin.Shared
  (`src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj:83`) + `Mobile.Logic`.
  **Resulting reference shape after W1–W4 (verified against the tree; the acceptance target):**
  head → `Admin.Shared` → { `SchoolCollab.Core.Abstractions`, the two FluentUI packages,
  the `Microsoft.AspNetCore.Components.*` packages }; head → `Mobile.Logic`. To get there:
  `Admin.Shared` **drops** its `Core` ProjectReference (:20), `Students.Core` (:21) and
  `ServiceDefaults` (:19), and replaces its `<FrameworkReference>` (:10) with components
  packages — final shape: `Core.Abstractions` + FluentUI only, a portable design-system RCL;
  and `SchoolCollab.Core` **adds** `SchoolCollab.Core.Abstractions` (direction Core →
  abstractions; the reverse edge is forbidden by the zero-dependency pin). **No
  FrameworkReference carrier anywhere on the head's closure.**
- **`ServiceDefaults` is unused on the closure's UI projects** — `Admin.Shared`,
  `Students.Core`, and `Students.Contracts` each carry a `ServiceDefaults` ProjectReference
  (cites above) with **zero source usages**: `git grep` for namespace/type usages of
  `SchoolCollab.ServiceDefaults` inside those three projects returns only the `.csproj` lines.
  Every *host* that calls `AddServiceDefaults()` references `ServiceDefaults` directly
  (`src/SchoolCollab.Admin/SchoolCollab.Admin.csproj:18`,
  `src/Students/SchoolCollab.Students.Api/SchoolCollab.Students.Api.csproj:8`,
  `src/Students/SchoolCollab.Students.Worker/SchoolCollab.Students.Worker.csproj:9`,
  `src/Assignments/SchoolCollab.Assignments.Api/SchoolCollab.Assignments.Api.csproj:8`,
  `src/Assignments/SchoolCollab.Assignments.Core/SchoolCollab.Assignments.Core.csproj:8`,
  `src/SchoolCollab.MigrationService/SchoolCollab.MigrationService.csproj:9`).
  `Students.Application` (the one closure member with **no** direct `ServiceDefaults` reference)
  has **zero** `using SchoolCollab.ServiceDefaults` statements
  (`git grep -n "SchoolCollab.ServiceDefaults" -- src/Students/SchoolCollab.Students.Application`
  = 0 matches), so dropping the transitive path cannot break it.
- **`Admin.Shared`'s framework usage is exactly the Blazor Components family + one `HttpContext`.**
  All `Microsoft.AspNetCore.*` namespace usings across Admin.Shared source (`.cs` + `.razor`):
  `Components` (8 files), `Components.Authorization` (2), `Components.Forms` (1),
  `Components.Routing` (4), `Components.Sections` (2), `Components.Web` (1), `Http` (1),
  plus `Microsoft.App` (the FrameworkReference itself). The `Http` hit is only
  `Components/Pages/Error.razor` — a `private HttpContext? HttpContext` cascading parameter
  (`src/SchoolCollab.Admin.Shared/Components/Pages/Error.razor:29`) used for
  `HttpContext?.TraceIdentifier` (:35) — and one `@using Microsoft.AspNetCore.Http`
  (`src/SchoolCollab.Admin.Shared/Components/_Imports.razor:7`). The `Microsoft.Extensions.*`
  usings in Admin.Shared source are `DependencyInjection` (1: `IServiceProvider` +
  `GetService<IFeatureFlagChangeNotifier>` in
  `src/SchoolCollab.Admin.Shared/Components/Gate/GateBase.razor:72,96`) and `Logging`
  (3 files) — both available as NuGet packages (Abstractions), no server behaviour.
  The only remaining grep hit, `Microsoft.Extensions.Validation.Embedded`, exists only in
  `obj/` build artifacts, not source.
- **The 12 `Students.Core`-bound files (4 `.razor` + 4 `.cs` models + 4 `.razor.css` siblings)**
  (measured: all `SchoolCollab.Students.Core.*` usings in `Admin.Shared` sit in these 4 components
  + 4 models, plus the csproj line):
  `Components/ContactChangeDialog.razor` (:13), `ContactChangeModel.cs` (:1),
  `ContactFormFields.razor` (:1), `ContactFormFieldsModel.cs` (:1), `ContactModel.cs` (:1),
  `ContactsEditor.razor` (:27 `Students.Core.Contracts`, :28 `.Domain`, :29 `.DTOs`),
  `GuardianEditFields.razor` (:1), `GuardianEditFieldsModel.cs` (:1) — with their `.razor.css`
  siblings (`ContactChangeDialog.razor.css`, `ContactFormFields.razor.css`,
  `ContactsEditor.razor.css`, `GuardianEditFields.razor.css`). Every `ContactsEditor.razor`
  dependency is satisfied by `Students.Application`'s existing imports: it `@inject`s
  `IContactsClient` (Students.Core.Contracts — Students.Application references `Students.Core` at
  Students.Application.csproj:22), Admin.Shared's `CodedValuesApiClient`/`IDialogService`
  (`_Imports.razor:16,17,20`) and `ILogger<…>` (`_Imports.razor:7`).
- **Their consumers (full enumeration):** all code-level consumers live in
  `src/Students/SchoolCollab.Students.Application/…` (page components `Guardians/GuardianDetail.razor:101`,
  `Guardians/GuardianSetupWizard.razor:106`, `Students/Edit.razor:105`,
  `Teachers/TeacherDetail.razor:65`, `Students/GuardianSection.razor:184,467,981`,
  `Students/StudentEditDialog.razor:82`, `Students/StudentFormFields.razor:147,157`,
  `Students/GuardianFormDialog.razor:45,54`, `Students/GuardianPickerDialog.razor:118`
  — plus model files `Students/GuardianAssignment.cs:22`, `GuardianAssignmentModel.cs:51`,
  `StudentFormModel.cs:57,119,167,181,197`). `Admin.Tests.Unit` bUnit tests add three **type-level**
  consumers (`ContactsEditorTests.cs` — `using SchoolCollab.Admin.Shared.Components;`,
  `TeacherDetailPageBunitTests.cs:183` `NullLogger<ContactsEditor>`,
  `StudentFormModelMappingsTests.cs:244` `new ContactModel`) and two **source-path-level** tests
  that read component files by literal path (`StudentFormFieldsSectionEditTests.cs:41,44,62`,
  `FormRowOrientationTests.cs:63,66` — literal strings
  `"SchoolCollab.Admin.Shared/Components/ContactsEditor.razor"` etc.). Everything else that greps
  the moved names is comment/documentation text only (`ContactRoutes.cs:113`, `FluentIcons.cs:34`,
  `IContactsClient.cs:10`, `StudentsApiClient.cs:2151`, `ModuleServices.cs:24`,
  `Playwright/Tests/ContactAuditSmokeTests.cs:15,74,90`).
- **`Admin.Shared`'s remaining `Core` touchpoints after severance are enum-only, moved, or
  doc-only:** `Core.AssignmentPolicies` —
  `src/SchoolCollab.Admin.Shared/Services/AssignmentPolicyApiClient.cs:3`
  (`using SchoolCollab.Core.AssignmentPolicies;`), whose only Core identifier is
  `SignatureRequirementMode` (moves to `Core.Abstractions`, W3); `Core.Notifications` —
  `src/SchoolCollab.Admin.Shared/Services/NotificationPolicyApiClient.cs:2`
  (`using SchoolCollab.Core.Notifications;`), whose only Core identifier is
  `NotificationChannel` (moves, W3). Measured (round-3 pin evidence): the two ApiClients' only
  Core identifiers are those two enums; the **two moved enum files are BCL-only** —
  `SignatureRequirementMode.cs:1` (its only import is `System.Text.Json.Serialization`) and
  `NotificationChannel.cs:1` (only `System.ComponentModel`) — gate P2 (d) fold: the earlier
  "every file in both namespaces is BCL-only" claim was over-broad and is narrowed to exactly
  these moved files — **because the namespaces are preserved, the two ApiClients need no edits for
  the enum move**.
  `Core.Features` — the gates (whole move, W3); `Core.Tenancy` — doc comment only (no compile
  dependency). The 8 `Services/*ApiClient.cs` files are portable
  (`public sealed class XApiClient(HttpClient http)`); the `System.Net.Http.Json`,
  `System.Security.Claims` and `System.Text.Json` namespaces they use are in the base
  `net10.0` targeting pack.
- **The gates' server dependency is `Core.Features` only:** `GateBase.razor:11`,
  `FeatureFlagGate.cs:3`, `Gate/FeatureEnabledCondition.cs:1` — interfaces and constants, not
  behaviour. The `Core.Features` file set: `FeatureFlagKeys.cs:13` (static constants),
  `IFeatureFlagChangeNotifier.cs:13` (interface), and `FeatureFlagService.cs:8`
  (`IFeatureFlagService` interface) **co-located with** `ConfigurationFeatureFlagService`
  (FeatureFlagService.cs:43 — a config-only implementation using only
  `Microsoft.Extensions.Configuration`, registered in
  `src/SchoolCollab.Core/Auth/AuthTenancyExtensions.cs:107`).

### Owner-pinned decisions — round 3 (2026-10-09, plan-gate REVISE) — recorded as pins, NOT re-opened

1. **Minimal abstraction move (option (a)):** beyond the pinned `Core.Features` move (pin 3
   above), the only Core types `Admin.Shared` still needs are **two enums** —
   `src/SchoolCollab.Core/AssignmentPolicies/SignatureRequirementMode.cs` (38 lines; its only
   import is `System.Text.Json.Serialization` (:1); explicit
   `namespace SchoolCollab.Core.AssignmentPolicies;` at :3) and
   `src/SchoolCollab.Core/Notifications/NotificationChannel.cs` (16 lines; only
   `System.ComponentModel` (:1); explicit `namespace SchoolCollab.Core.Notifications;` at :3).
   Move exactly those two; the policy *shapes*, both `IEffective…Resolver` interfaces and both
   resolver implementations **stay in `Core`**. Measured: the two ApiClients' only Core
   identifiers are those two enums; the **moved** files are BCL-only —
   `SignatureRequirementMode.cs:1` (only import `System.Text.Json.Serialization`) and
   `NotificationChannel.cs:1` (only `System.ComponentModel`).
2. **Project name: `SchoolCollab.Core.Abstractions`** at
   `src/SchoolCollab.Core.Abstractions/SchoolCollab.Core.Abstractions.csproj` — zero packages,
   zero project references, zero FrameworkReferences, `net10.0`. It hosts three namespaces:
   `SchoolCollab.Core.Features` (the three feature-flag abstractions),
   `SchoolCollab.Core.AssignmentPolicies` (only `SignatureRequirementMode`),
   `SchoolCollab.Core.Notifications` (only `NotificationChannel`). **Because the project name
   no longer matches any of those namespaces, every moved file must carry an explicit
   `namespace` declaration** — verified: all five source files already do
   (`FeatureFlagKeys.cs:1`, `IFeatureFlagChangeNotifier.cs:1`, `FeatureFlagService.cs:2` for the
   split, `SignatureRequirementMode.cs:3`, `NotificationChannel.cs:3`); the split
   `IFeatureFlagService.cs` carries its `namespace` line verbatim. Namespaces are preserved, so
   no consumer file changes.

### Work items

**W1 — Admin.Shared severance (the framework-reference swap + reference drops).**

1. `src/SchoolCollab.Admin.Shared/SchoolCollab.Admin.Shared.csproj`:
   - **Remove** `<FrameworkReference Include="Microsoft.AspNetCore.App" />` (line 10).
   - **Add** (no `Version` attribute — CPM, see W2):
     `<PackageReference Include="Microsoft.AspNetCore.Components" />`,
     `Microsoft.AspNetCore.Components.Authorization` (GateBase's Components.Authentication usings),
     `Microsoft.AspNetCore.Components.Web` (covers the Forms/Routing/Sections/Web namespace hits —
     the Forms/Routing/Sections assemblies ship inside this package's asset family along with the
     Components base package; if a namespace is still missing at compile, add the smallest missing
     sibling package and record the deviation — do NOT reach for the framework reference).
     Plus `Microsoft.Extensions.DependencyInjection.Abstractions` (GateBase.razor:72,96) and
     `Microsoft.Extensions.Logging.Abstractions` (the 3 Logging usings) — both already versioned
     centrally (`Directory.Packages.props` "Microsoft.Extensions" group,
     `Microsoft.Extensions.Logging.Abstractions` 10.0.11 at line 55).
   - **Remove** the `ServiceDefaults` ProjectReference (line 19), the `Students.Core`
     ProjectReference (line 21), **and the `Core` ProjectReference (line 20)** — the P1: `Core`
     is a FrameworkReference *carrier at closure level* via the JwtBearer/OpenIdConnect nuspecs
     (see the corrected measured fact), so keeping it would leave NETSDK1082 untouched.
     Admin.Shared's only remaining project reference is `SchoolCollab.Core.Abstractions`
     (W3 item 3). Keep the two FluentUI PackageReferences (lines 14–15) — final shape:
     `Core.Abstractions` + FluentUI, a portable design-system RCL.
2. **`Error.razor` fix** (removes the last `Microsoft.AspNetCore.Http` dependency):
   `src/SchoolCollab.Admin.Shared/Components/Pages/Error.razor` — delete the
   `[CascadingParameter] private HttpContext? HttpContext` property (:29) and change the id
   expression (:35) to `RequestId = Activity.Current?.Id;`. Keep the page and its `@page "/Error"`
   route (no router or code path references it — `git grep "\"/Error\"\|typeof(Error)"` over the
   Blazor hosts = 0 matches — it is URL-reachable only); the W3C/Otel `Activity.Current?.Id` is the
   trace-consistent id on every host and is what ServiceDefaults' Serilog/OTel logging emits.
   Recorded behaviour delta: the server-side `HttpContext.TraceIdentifier` fallback is gone.
3. **`_Imports.razor` fix:** remove `@using Microsoft.AspNetCore.Http` from
   `src/SchoolCollab.Admin.Shared/Components/_Imports.razor:7` (nothing else in the project uses
   that namespace after step 2).
4. **CPM (`W2`):** `Directory.Packages.props` — add `<PackageVersion>` entries for
   `Microsoft.AspNetCore.Components`, `Microsoft.AspNetCore.Components.Authorization`, and
   `Microsoft.AspNetCore.Components.Web`. **Obligation (P2 #3): resolve the version from what
   `Microsoft.AspNetCore.Components.WebView.Maui 10.0.110` actually pulls** — read the
   restored `obj/project.assets.json` for the head and pin the three entries to the resolved
   versions, never to a guessed number. Background: a naive `10.0.11` pin is unverified — the
   local NuGet cache tops out at `10.0.8` for the Components.Web family, while the head pins
   `WebView.Maui 10.0.110` (Directory.Packages.props:75); NuGet unifies to the highest version
   across the graph, so a conservative exact-match miss is safe, but the worker's recorded
   number must come from the restored assets, not the guess. Existing 10.0.x anchors for the
   Extensions group: `Microsoft.Extensions.Logging.Abstractions` 10.0.11 at :55;
   never let a chosen version conflict with the `WebView.Maui 10.0.110` dependency pins.
   No `PackageReference` may carry a `Version` attribute (NU1008/NU1009).
5. **Do NOT touch:** `Mobile.Logic` (already `net10.0`-clean),
   `src/SchoolCollab.Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj`'s package set, the four
   existing ubuntu CI jobs, any `appsettings*.json`.

**W2 — Students.Core / Students.Contracts also lose `ServiceDefaults` (yes — both).**
Remove the `ServiceDefaults` ProjectReference from
`src/Students/SchoolCollab.Students.Core/SchoolCollab.Students.Core.csproj:8` and
`src/Students/SchoolCollab.Students.Contracts/SchoolCollab.Students.Contracts.csproj:8`.
Zero usages verified (see the measured facts). **Transitive-risk check:** every project that
genuinely consumes `ServiceDefaults` types references it directly (citation list in the measured
facts); the only closure members that consumed it purely transitively (`Admin.Shared`,
`Students.Application`) have zero `using SchoolCollab.ServiceDefaults` statements. The check that
catches any residual reliance is the root-solution build (AC-2) — nothing narrower is required.
Note `AppHost`'s `IsAspireProjectResource="false"` references to `Students.Core`/`Students.Contracts`
(`src/AppHost/SchoolCollab.AppHost/SchoolCollab.AppHost.csproj:35-36`) also lose the transitive
`ServiceDefaults` view — if anything there fails to compile, the fix is to add the direct reference
to `AppHost.csproj`, not to re-add the drop.

**W3 — the new zero-dependency abstractions project (`SchoolCollab.Core.Abstractions`).**

1. **New project** `src/SchoolCollab.Core.Abstractions/SchoolCollab.Core.Abstractions.csproj` —
   SDK `Microsoft.NET.Sdk`, `TargetFramework net10.0`, `Nullable enable`, `ImplicitUsings enable`.
   **Zero packages, zero project references, zero framework references** — the moved types need
   only the BCL plus `System.Text.Json.Serialization` (`SignatureRequirementMode.cs:1`) and
   `System.ComponentModel` (`NotificationChannel.cs:1`). The project name matches **none** of the
   three hosted namespaces, so **every moved file must carry an explicit `namespace`
   declaration** (round-3 pin 2) — all five sources already do (`FeatureFlagKeys.cs:1`,
   `IFeatureFlagChangeNotifier.cs:1`, `FeatureFlagService.cs:2`, `SignatureRequirementMode.cs:3`,
   `NotificationChannel.cs:3`); the worker verifies each moved file, including the split
   `IFeatureFlagService.cs`. Namespaces are preserved with no `@namespace`/rename churn in the
   ~106 `Core.Features` consumer files or any enum consumer (owner pins 3 and round-3 pin 2).
2. **Move five abstractions files** (verbatim content, only location changes):
   `src/SchoolCollab.Core/Features/FeatureFlagKeys.cs` →
   `src/SchoolCollab.Core.Abstractions/FeatureFlagKeys.cs`;
   `Features/IFeatureFlagChangeNotifier.cs` →
   `src/SchoolCollab.Core.Abstractions/IFeatureFlagChangeNotifier.cs`;
   **split** `Features/FeatureFlagService.cs` — the `IFeatureFlagService` interface
   (:8–42, including its doc comment) moves to
   `src/SchoolCollab.Core.Abstractions/IFeatureFlagService.cs` with
   `namespace SchoolCollab.Core.Features;` carried verbatim (:2); the
   `ConfigurationFeatureFlagService` **implementation** (FeatureFlagService.cs:43–82, its
   `class` block) **stays in `SchoolCollab.Core`**, re-homed in a new file
   `src/SchoolCollab.Core/Features/ConfigurationFeatureFlagService.cs` with its
   `namespace SchoolCollab.Core.Features;` line **unchanged** (the pin preserves the namespace;
   a namespace shared across two sibling shared-kernel projects is the accepted shape here).
   Do not touch the interface's doc-comment `<see cref>` references to implementations (doc-only).
   Plus the two round-3 enum moves:
   `src/SchoolCollab.Core/AssignmentPolicies/SignatureRequirementMode.cs` →
   `src/SchoolCollab.Core.Abstractions/SignatureRequirementMode.cs` (namespace :3 unchanged), and
   `src/SchoolCollab.Core/Notifications/NotificationChannel.cs` →
   `src/SchoolCollab.Core.Abstractions/NotificationChannel.cs` (namespace :3 unchanged).
   **What stays in `Core` (round-3 pin 1):** the assignment-policy shapes, both
   `IEffective…Resolver` interfaces, both resolver implementations, and
   `ConfigurationFeatureFlagService` — none of which moves.
3. **Wiring — exactly two ProjectReference edits:**
   `src/SchoolCollab.Core/SchoolCollab.Core.csproj` gains
   `<ProjectReference Include="..\SchoolCollab.Core.Abstractions\SchoolCollab.Core.Abstractions.csproj" />`
   (direction is Core → abstractions: the pin says the new project is **zero-dependency**, so the
   reverse edge is forbidden; `Core` needs it for `AuthTenancyExtensions.cs:107`'s registration
   and its own policy/resolver code), and
   `src/SchoolCollab.Admin.Shared/SchoolCollab.Admin.Shared.csproj` gains the same ProjectReference
   explicitly (it also **loses its `Core` reference entirely** — W1 item 1 — so the gates and the
   two policy ApiClients compile against the abstractions project). Because namespaces are
   preserved, the policy files in `Core` and the two ApiClients in `Admin.Shared` need **no**
   edits for the enum move — that is what makes the P1 fix cheap. Everything else —
   `Assignments.*`, `Settings.*`, `Students.*`, the hosts, and the ~90 test files — keeps compiling
   via `Core`'s transitive reference, so **zero consumer files change** for the enum move and the
   FeatureFlags move alike (owner pins 3 and round-3 pin 2, satisfied mechanically).
4. **Register in `SchoolCollab.slnx`** under `<Folder Name="/src/">` (alphabetical slot between
   `SchoolCollab.Core` and `SchoolCollab.Families` — `SchoolCollab.Core.Abstractions` still sorts
   immediately after `SchoolCollab.Core`, so the line is
   `<Project Path="src/SchoolCollab.Core.Abstractions/SchoolCollab.Core.Abstractions.csproj" />`;
   `SchoolCollab.slnx:4-10`).
   The guard `tests/SchoolCollab.ArchitectureTests.Unit/NewProjectsSolutionRegistrationArchitectureTests.cs`
   accepts registration in **any** checked-in `.slnx` (:16–24 doc comment) and fails any project that
   is in no solution — the root slnx registration satisfies it; do NOT add the new project to
   `src/Mobile/SchoolCollab.Mobile.slnx` (it is not mobile-specific; root membership keeps it in the
   ubuntu build/test runs).

**W4 — the 12-file move (4 components + 4 models + 4 `.razor.css`) to `Students.Application`.**

1. **Move** (git-mv semantics; content edits only as listed):
   from `src/SchoolCollab.Admin.Shared/Components/` to
   `src/Students/SchoolCollab.Students.Application/Components/Contacts/`:
   `ContactChangeDialog.razor` (+`.razor.css`), `ContactChangeModel.cs`, `ContactFormFields.razor`
   (+`.razor.css`), `ContactFormFieldsModel.cs`, `ContactModel.cs`, `ContactsEditor.razor`
   (+`.razor.css`), `GuardianEditFields.razor` (+`.razor.css`), `GuardianEditFieldsModel.cs`.
2. **Namespace decision (decided — not an open fork):** the moved files take the home namespace
   `SchoolCollab.Students.Application.Components.Contacts` (folder-derived for the four `.razor`
   files under Students.Application's default `RootNamespace`; no `@namespace` directive needed —
   but verify none of the moved `.razor` files carries one; if it does, update it). Rationale: the
   owner pin demands principled severance; leaving `SchoolCollab.Admin.Shared.Components`-namespaced
   types living in the Students context would be a discoverability lie, and the consumer cost is
   small (item 3). Cost of the alternative (namespace preserved) was measured: it would save ~7
   small edits but permanently misbind the contacts family to the shared design-system assembly.
3. **Namespace edits (the full consumer list):**
   - 4 `.cs` models: `namespace SchoolCollab.Admin.Shared.Components;` →
     `namespace SchoolCollab.Students.Application.Components.Contacts;`
     (`ContactChangeModel.cs:3`, `ContactFormFieldsModel.cs:3`, `ContactModel.cs:3`,
     `GuardianEditFieldsModel.cs:3` — at their NEW paths).
   - `src/Students/SchoolCollab.Students.Application/Components/_Imports.razor`: add
     `@using SchoolCollab.Students.Application.Components.Contacts;` (keep the existing
     Admin.Shared usings at :16–21 — the moved components and other Students.Application pages still
     consume Admin.Shared shared components/services). **This single line plus the 3 `.cs` usings
     covers every razor consumer** (`GuardianDetail.razor`, `GuardianSetupWizard.razor`,
     `Edit.razor`, `TeacherDetail.razor`, `GuardianSection.razor`, `StudentEditDialog.razor`,
     `StudentFormFields.razor`, `GuardianFormDialog.razor`, `GuardianPickerDialog.razor` — none
     carries a per-file `@using` of the moved namespace).
   - 3 `.cs` model consumers currently `using SchoolCollab.Admin.Shared.Components;`:
     `Components/Students/GuardianAssignment.cs:2`, `GuardianAssignmentModel.cs:4`,
     `StudentFormModel.cs:2` → replace with
     `using SchoolCollab.Students.Application.Components.Contacts;`.
     If compiling reveals another Admin.Shared.Components type still referenced in the same file,
     keep BOTH usings and record the deviation (an unused-using is at worst a warning, not an error).
   - Tests:
     `tests/SchoolCollab.Admin.Tests.Unit/ContactsEditorTests.cs` — swap its
     `using SchoolCollab.Admin.Shared.Components;` the same way (keep the Dialogs/Constants/Services
     usings; same both-usings fallback rule);
     `tests/SchoolCollab.Admin.Tests.Unit/TeacherDetailPageBunitTests.cs:183` and
     `StudentFormModelMappingsTests.cs:244` — same namespace fix if compile demands it;
     `tests/.../StudentFormFieldsSectionEditTests.cs:41,44,62` and `FormRowOrientationTests.cs:63,66`
     — update the literal source paths
     `"SchoolCollab.Admin.Shared/Components/<file>.razor"` to
     `"Students/SchoolCollab.Students.Application/Components/Contacts/<file>.razor"`
     (match the path style those tests already use for repo-root-relative reads).
   - **No edit** for: `ContactRoutes.cs:113`, `FluentIcons.cs:34`, `IContactsClient.cs:10`,
     `ModuleServices.cs:24`, `StudentsApiClient.cs:2151`, `GuardianFormDialogTests.cs`,
     `StudentDetailSectionsTests.cs`, `EditSidebarLayoutTests.cs:198`,
     `SectionCardDialogsBunitTests.cs`, `GuardianContactsDialogTests.cs`,
     `Playwright/Tests/ContactAuditSmokeTests.cs:15,74,90` (comment/string-consumers only; updating
     stale "in Admin.Shared" comment text is optional and must be recorded if done).
4. **What this does to Admin.Shared:** after the move, Admin.Shared has zero
   `SchoolCollab.Students.Core.*` usages (the 12 moved files were the complete list — measured), so
   W1's `Students.Core` reference drop is exactly enabled by W4. The contacts/guardian family
   **leaves the mobile head's closure** (the head references `Admin.Shared` only — it never
   referenced `Students.Application`), which is also what the spec's option-(d) rule anticipated
   (spec §3.2: those 5 components "stay out of the mobile subset until a slice needs them"); L4's
   slice can reach them via `Students.Application` when one does.
5. **CSS isolation:** the four `.razor.css` files move with their components; scoped-CSS scope
   identifiers are recomputed from the new assembly+path automatically; no CSS content edits.

**W5 — CI flip + head csproj NOTE cleanup.**

1. `.github/workflows/ci.yml` mobile job (`mobile:` at :158; comment block :164–176; build step
   :197–202): change `-f net10.0-windows10.0.19041.0` (:201) to **`-f net10.0-android`**, rename the
   step "Build the mobile head (windows)" (:197) → "Build the mobile head (android)", and rewrite the
   comment block (:164–176) to record the new state: the severance round removed every
   FrameworkReference from the head's closure, so the head builds the android TFM and the Windows
   comment rationale is history. Keep: `runs-on: windows-latest`, the `dotnet workload install maui`
   step (the android TFM is part of the maui workload — do not swap it for a slimmer install), and
   the csproj-level `-f` rule (never solution-level — global-property override, L1 plan-review P2).
2. `src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj` — **delete the NOTE comment block**
   (lines 5–14): it documents NETSDK1082 as current, which is false after this round; the history
   lives in this round doc, `docs/specs/mobile-hybrid-app.md §3.2`, and the ci.yml comment. No other
   csproj change (its ProjectReferences :83–84 are unchanged).

**Contingency — if the android build still fails after W1–W2 (ordered, not open-ended).**
The L1 causation proof already excludes the head's shipped packages as FrameworkReference carriers,
so the ordered ladder is:

1. **NETSDK1082 persists** (evidence: same `ResolveRuntimePackAssets` error naming
   `Microsoft.AspNetCore.App`): a FrameworkReference reached the head from somewhere unmeasured.
   Ordered step: inspect the android build's `obj/project.assets.json` `frameworkReferences`
   section on the failing project — it records which *package* dependency delivered it — and
   escalate that exact package/project to the owner before any workaround.
2. **MSB3277 version conflicts appear** (evidence: 24-class conflict warnings, the L1 shim
   signature): the new `Microsoft.AspNetCore.Components.*` PackageVersion entries conflict with
   `WebView.Maui 10.0.110`'s pins. Ordered step: align the new entries to the version WebView.Maui
   actually resolves (read `obj/project.assets.json`), i.e. raise them past `10.0.11` — a `Directory.Packages.props`-only change.
3. **A package fails resolution for android** (evidence: NU1201 / NETSKU-compatible-asset warnings
   naming a package, not a framework): identify the single offender from the restore log; if it is a
   repo-owned closure member, sever it; if it is a third-party package (e.g. a FluentUI asset
   incompatibility — not expected: FluentUI 4.14.2 ships `net8.0`/`netstandard2.0` assets which are
   android-compatible), pause and escalate to the owner with the exact package + evidence before
   choosing a replacement.

**Constraints the worker must honor (unchanged from L1 where re-stated):**

- Citation discipline (`documents/specs/round-runner-model-policy.md`, decision 4): every repo fact
  the WORKER REPORT asserts carries `file:line`; no fact is restated from this plan without
  re-verifying it against the tree.
- **No `http://`/`https://` string literal in any `.cs` under `src/`** (pre-existing invariant; this
  round must not introduce one — it moves files, it does not add addresses).
- `.razor` moves follow `.github/copilot/rules/blazor-components.md`; C# edits follow
  `.github/copilot/rules/dotnet-best-practices.md`.
- CPM: no `Version=` attributes on `PackageReference` (NU1008/NU1009).
- Repo-scoped searches only (`git grep` under `src/`, `tests/`, `documents/`) — never `find /`.
- The parent owns authoritative builds/tests; the worker may compile-verify per
  AGENTS.md's in-session `dotnet build` rule but the round's acceptance numbers are the parent's.
- **One writer per working tree** (AGENTS.md): no parallel writers; do not touch
  `documents/specs/` (spec amendments happen at parent acceptance).

**Expected files** (W1–W5; **38 file paths** — 6 in the new abstractions project incl. its
.csproj, 5 moved/split out of `Core`, 12 moved contact-family files, plus modified csproj/workflow/test files):

```
src/SchoolCollab.Core.Abstractions/SchoolCollab.Core.Abstractions.csproj (new: zero deps, net10.0)
src/SchoolCollab.Core.Abstractions/FeatureFlagKeys.cs                    (moved verbatim; explicit namespace)
src/SchoolCollab.Core.Abstractions/IFeatureFlagChangeNotifier.cs         (moved verbatim; explicit namespace)
src/SchoolCollab.Core.Abstractions/IFeatureFlagService.cs                (split out of Core/Features/FeatureFlagService.cs; explicit namespace)
src/SchoolCollab.Core.Abstractions/SignatureRequirementMode.cs           (moved verbatim; explicit namespace at :3)
src/SchoolCollab.Core.Abstractions/NotificationChannel.cs                (moved verbatim; explicit namespace at :3)
src/SchoolCollab.Core/Features/ConfigurationFeatureFlagService.cs        (split; namespace unchanged)
src/SchoolCollab.Core/SchoolCollab.Core.csproj                           (modified: +1 ProjectReference → Core.Abstractions)
src/SchoolCollab.Admin.Shared/SchoolCollab.Admin.Shared.csproj           (modified: FrameworkReference→3 pkgs, -Core, -ServiceDefaults, -Students.Core, +Core.Abstractions)
src/SchoolCollab.Admin.Shared/Components/Pages/Error.razor              (modified: HttpContext removed)
src/SchoolCollab.Admin.Shared/Components/_Imports.razor                 (modified: -@using Microsoft.AspNetCore.Http)
src/SchoolCollab.Admin.Shared/Components/{ContactChangeDialog,ContactFormFields,ContactsEditor,GuardianEditFields}.razor(.css)   (moved — 8 files incl. .razor.css siblings)
src/SchoolCollab.Admin.Shared/Components/{ContactChangeModel,ContactFormFieldsModel,ContactModel,GuardianEditFieldsModel}.cs      (moved + namespace — 4 files)
src/Students/SchoolCollab.Students.Application/Components/Contacts/**   (the 12 moved files' new home)
src/Students/SchoolCollab.Students.Application/Components/_Imports.razor (modified: +1 @using)
src/Students/.../Components/Students/{GuardianAssignment,GuardianAssignmentModel,StudentFormModel}.cs  (modified: 1 using each)
src/Students/SchoolCollab.Students.Core/SchoolCollab.Students.Core.csproj        (modified: -ServiceDefaults)
src/Students/SchoolCollab.Students.Contracts/SchoolCollab.Students.Contracts.csproj (modified: -ServiceDefaults)
SchoolCollab.slnx                                                       (modified: +1 Project entry)
Directory.Packages.props                                                (modified: +3 PackageVersion)
.github/workflows/ci.yml                                                (modified: mobile job → -f net10.0-android)
src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj               (modified: NOTE block deleted)
tests/SchoolCollab.Admin.Tests.Unit/{ContactsEditorTests,TeacherDetailPageBunitTests,StudentFormModelMappingsTests}.cs  (modified: namespace)
tests/SchoolCollab.Admin.Tests.Unit/{StudentFormFieldsSectionEditTests,FormRowOrientationTests}.cs      (modified: source paths)
```

**Acceptance criteria (each discriminating — fails against today's tree):**

- **AC-1 (headline)** `dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj
  -f net10.0-android`: **0 errors**. The NETSDK1082 root cause is gone: no
  `FrameworkReference` remains on the head's closure — verified two ways: (a)
  `git grep -ln FrameworkReference -- src/SchoolCollab.Admin.Shared src/ServiceDefaults` proves the
  closure carriers were removed/replaced, and (b) the restored android
  `obj/project.assets.json` for the head contains **no `Microsoft.AspNetCore.App` entry** in its
  resolved `frameworkReferences` — a valid android `project.assets.json` legitimately carries
  `frameworkReferences` for the base runtime, so the discriminator is only the ASP.NET App frame,
  never "no frameworkReferences entry" (P2 #2 rewrite).
- **AC-2** `dotnet build SchoolCollab.slnx` at repo root: **0 errors** (this is also the
  transitive-`ServiceDefaults`-drop safety net for W2).
- **AC-3** Affected test suites green (parent's authoritative numbers): at minimum
  `SchoolCollab.Admin.Tests.Unit` (the moved-component bUnit suite), `SchoolCollab.Core.Tests.Unit`
  (the split `FeatureFlagServiceTests`), `SchoolCollab.Students.Tests.Unit`,
  `SchoolCollab.Assignments.Tests.Unit` (FakeFeatureFlagService), and
  `SchoolCollab.ArchitectureTests.Unit` (solution-registration guard + any-slnx union) — **0 failures**.
- **AC-4** **No `FrameworkReference` on the head's path.** Two checks: (a)
  `git grep -ln FrameworkReference -- "*.csproj"` shows the real declaration set shrunk from
  **6 → 5** projects (`Admin.Shared`'s :10 declaration replaced by packages; `ServiceDefaults`
  keeps its own :11 declaration, which remains correct for server hosts — today's count is 6 real
  declarations plus the head's NOTE comment at `SchoolCollab.Mobile.csproj:8`, which W5 deletes;
  `Core`'s package-delivered carrier was never a csproj declaration, so it never showed here — and
  that is why the graph check below, not this count, is the discriminating one);
  and (b) a graph check, not a ban: the head's restored android `obj/project.assets.json`
  contains **no `Microsoft.AspNetCore.App` entry** in its resolved `frameworkReferences`
  (same P2 #2 semantics as AC-1(b) — base-runtime entries are legitimate; never "no entry").
- **AC-5** **The moved-namespace consumer set is byte-identical outside the moved files** —
  mechanically checkable: `git diff --name-only` intersected with the consumer list from
  `git grep -lnE "SchoolCollab\.Core\.(Features|AssignmentPolicies|Notifications)" -- src tests`
  (the 106-file `Core.Features` list measured at plan time, extended to the two round-3 enum
  namespaces) must be exactly the split's descendant
  (`src/SchoolCollab.Core/Features/ConfigurationFeatureFlagService.cs`) plus the five moved
  abstractions files — and empty otherwise. **Rename accounting (gate P2 (c) fold):** the
  original `src/SchoolCollab.Core/Features/FeatureFlagService.cs` legitimately appears in the
  patch as a **rename** (git `R053` →
  `src/SchoolCollab.Core/Features/ConfigurationFeatureFlagService.cs`), since its remainder
  re-homed into that file — so the check must use the full expected set (split descendant + the
  five renamed/moved abstractions entries) and never a naive "file deleted" alarm; and the frozen
  patch **excludes the patch file itself** (the final freeze runs `git reset -q -- <patch>` before
  `git diff --cached`; see the Execution log's patch self-inclusion defect). The pinned zero-churn
  property of the namespace preservation (owner pin 3 + round-3 pin 2), proven, not claimed.
- **AC-6** The no-literal invariant is preserved: the diff review greps `src/Mobile` for
  `http(s)?://` C# literals (clean) and no other `.cs` under `src/` gains one.
- **AC-7** The new project is registered in `SchoolCollab.slnx` and
  `NewProjectsSolutionRegistrationArchitectureTests` stays green (including its negative
  discriminator semantics: the new project must NOT appear in `KnownOrphanedProjects`).
- **AC-8** The round doc's Worker Report, Review, and Acceptance sections are completed
  (Tier 3), and the round ships its `diffs-mobile-severance.patch` generated from base `fab7dd5e`.

**Scope — out:** any auth/PKCE/realm change (L3); feature screens (L4);
`Assignments.Application`'s own closure question (OD1's L4 item — its `Students.Application`
reference re-imports students' server stack, but that does not touch the head); moving
`Core.AssignmentPolicies` / `Core.Notifications` types out of `Core` — the round-3 pin (a) moves
exactly the two enums; the assignment-policy shapes, both `IEffective…Resolver` interfaces and
both resolver implementations **stay in `Core`** (their type-only consumers keep compiling via
`Core`'s new reference to `Core.Abstractions`); `SchoolCollab.Core` is **off** the head's closure
after this round (the P1 fix); the round does not audit any *other* project's closure for
package-delivered FrameworkReference carriers (only `Core` was measured and pinned); renaming/restructuring existing CI jobs; editing
`documents/specs/` (parent-acceptance task); committing/pushing (owner-gated per AGENTS.md).

**Gates:** UI: risk — 4 `.razor` files move projects and are re-namespaced; the UI-tester waiver
was ACCEPTED by the gate (see the Execution log and the Not-applicable `## UI Tester` section);
contract: none (API wire shapes untouched); migrations: none; secrets: none.

**Open decisions:** None — the fork was settled by the owner pins above (round-3 pins) and the
gate's acceptance of the UI-tester waiver.
---
## Worker Report

_Transcribed by the parent from the worker run (`ollama-cloud/deepseek-v4.1-flash`) returned
WORKER REPORT — verbatim-in-substance, condensed only by dropping its restatement of the plan._

**W1 — Admin.Shared severance.** `src/SchoolCollab.Admin.Shared/SchoolCollab.Admin.Shared.csproj`
— dropped `<FrameworkReference Include="Microsoft.AspNetCore.App" />`; added `PackageReference`s
(no `Version=`) for `Microsoft.AspNetCore.Components`, `.Components.Authorization`,
`.Components.Web`, `Microsoft.Extensions.DependencyInjection.Abstractions`,
`Microsoft.Extensions.Logging.Abstractions`; dropped the `ServiceDefaults`, `Core`,
`Students.Core` ProjectReferences; added `SchoolCollab.Core.Abstractions`. Final shape:
`Core.Abstractions` + the two FluentUI packages + the component family.
`Components/Pages/Error.razor` — deleted the `[CascadingParameter] HttpContext` property and
`HttpContext?.TraceIdentifier`; id is now `Activity.Current?.Id` only; `@page "/Error"` kept.
`Components/_Imports.razor` — removed `@using Microsoft.AspNetCore.Http` (0 remaining
`HttpContext`/`AspNetCore.Http` references in the project). `Directory.Packages.props` — 3 new
`PackageVersion` entries, all **10.0.8**, resolved from the head's restored
`obj/project.assets.json` (Components/.Web/.Forms unify at 10.0.8 across the WebView.Maui
10.0.110 + FluentUI 4.14.2 graph).

**W2 — Students.Core / Students.Contracts.** Both dropped the `ServiceDefaults`
ProjectReference (Students.Contracts' now-empty `<ItemGroup>` removed too).

**W3 — new zero-dependency project.** New
`src/SchoolCollab.Core.Abstractions/SchoolCollab.Core.Abstractions.csproj` (net10.0; zero
packages, zero project references, zero framework references — verified `deps: []` in the restored
graph). Moved verbatim, all carrying an explicit `namespace`: `FeatureFlagKeys.cs`,
`IFeatureFlagChangeNotifier.cs`, `SignatureRequirementMode.cs`, `NotificationChannel.cs`. Split:
new `IFeatureFlagService.cs` = interface + doc + `namespace` line only (no
`Microsoft.Extensions.Configuration` using); new
`src/SchoolCollab.Core/Features/ConfigurationFeatureFlagService.cs` = the `using`, `namespace`,
and the class (namespace unchanged); original `Features/FeatureFlagService.cs` deleted. Wiring:
`Core` → `Core.Abstractions`, `Admin.Shared` → `Core.Abstractions`; no reverse edge.
`SchoolCollab.slnx` gained the project (not added to the mobile slnx).

**W4 — 12-file contacts move.** All 12 git-moved to
`src/Students/SchoolCollab.Students.Application/Components/Contacts/` (4 `.razor` + 4 `.razor.css`
+ 4 model `.cs`); none carried `@namespace`; the 4 models re-namespaced to
`SchoolCollab.Students.Application.Components.Contacts`.
`Students.Application/Components/_Imports.razor` +1 `@using`;
`Components/Students/{GuardianAssignment,GuardianAssignmentModel,StudentFormModel}.cs` using
replaced (each verified to use no other `Admin.Shared.Components` type). Tests:
`TeacherDetailPageBunitTests.cs`, `StudentFormModelMappingsTests.cs` usings replaced;
`ContactsEditorTests.cs` had the new using **added** and the old kept;
`FormRowOrientationTests.cs` / `StudentFormFieldsSectionEditTests.cs` had the 5 literal source
paths repointed. Scoped CSS regenerated at the new paths; no host `<link>` change was needed.

**W5 — CI flip + NOTE cleanup.** `.github/workflows/ci.yml` — `-f net10.0-android`, step renamed
"Build the mobile head (android)", comment rewritten; `runs-on: windows-latest`,
`dotnet workload install maui`, and the csproj-level `-f` rule kept.
`src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj` — NOTE block deleted; nothing else
changed.

**Worker's own verification:** android 0 errors / 0 warnings; root `SchoolCollab.slnx` 0 errors;
Admin.Tests.Unit 632/0, Core.Tests.Unit 126/0, Students.Tests.Unit 624/0,
Assignments.Tests.Unit 1064/0, ArchitectureTests.Unit 102/0; AC-1(b)/AC-4(b) head android
`frameworkReferences` set empty and `Microsoft.AspNetCore.App` occurrences 0; AC-4(a) 5 projects;
AC-5/AC-6/AC-7 clean. (The parent re-ran all of these authoritatively — numbers below in
`## Acceptance` — which is what the acceptance cites.)

**Worker-declared deviations:** (1) `ContactsEditorTests.cs` keeps both usings (the plan's
documented fallback); (2) comments added in `Admin.Shared.csproj` and the `ci.yml` workload
comment; (3) the `Admin.Shared.csproj` comment says "no Microsoft.AspNetCore.App **framework
reference**" rather than the literal token, so the mechanical AC-4(a) grep is not false-positived
(the first draft did make it count 6); (4) index hygiene — `git reset` after the `git mv`s then
`git add -N` on the 15 new files, because plain `git diff` otherwise cannot see untracked new
files; the parent must `git add -A` before freezing the patch (which it did — and see the
Execution log's patch self-inclusion lesson for the follow-on defect that caused).

**Worker's unresolved / not verified:** stale durable-doc path references to moved files
(report-only; `documents/specs/` is parent-owned — the parent has since folded the five
live-reference ones); only the android TFM was built (Windows/iOS/MacCatalyst not re-verified in
that pass); only the five named suites were run, not the full `dotnet test`.

## Review

### Plan gate — two runs (2026-10-09)

**Run 1 — REVISE (1 × P1 + 4 × P2).** The P1 — "`Core` cannot stay on the head's path" — is the
closure-level finding recorded in the plan's measured facts (full evidence in the Execution log):
`src/SchoolCollab.Core/SchoolCollab.Core.csproj:9-11` references
`Microsoft.AspNetCore.Authentication.JwtBearer`/`.OpenIdConnect`, whose nuspecs each declare
`<frameworkReference name="Microsoft.AspNetCore.App"/>` for `net10.0`, so `Core` delivered the
framework reference **through a package** even though its csproj has no `<FrameworkReference>`
element — keeping `Admin.Shared → Core` would have left NETSDK1082 untouched. Three of the four
P2s revised the plan (the file count → the 12-file set, the AC-1(b)/AC-4(b)
absence-of-`Microsoft.AspNetCore.App` semantics, the components-package version obligation);
the fourth corrected the wrong "`Core` has no FrameworkReference" premise. All folded into
`## Plan` as the round-3 revisions, with the two owner pins (`SchoolCollab.Core.Abstractions`,
minimal two-enum move) recorded there.

**Run 2 (revised sections only) — ACCEPT (no P1), 5 × P2, all documentation-level.** Four are
DOC-TEXT folds applied to `## Plan` at accept time:

- **(a)** the sentence fragment trailing at old line ~89 ("the only repo `.csproj`
  FrameworkReference sources are") — completed with the two closure carriers and marked as the
  fold;
- **(b)** the owner-pin text's "**5** `Students.Core`-bound components" corrected to the measured
  **4 `.razor` components + 4 `.cs` models + 4 `.razor.css` = 12 files**;
- **(c)** AC-5's mechanical check now accounts for the deletion of the original
  `src/SchoolCollab.Core/Features/FeatureFlagService.cs` — it legitimately appears as a
  **rename** (git `R053` → `ConfigurationFeatureFlagService.cs`), so the check uses the full
  expected set and notes the frozen patch excludes itself;
- **(d)** "every file in both namespaces is BCL-only" narrowed to the **two moved enum files**
  (`SignatureRequirementMode.cs:1`, `NotificationChannel.cs:1`).

The fifth P2 — the `using Microsoft.Extensions.Configuration;` must not travel into the
zero-package project — was passed to the worker as a clarification and is **FIXED**:
`src/SchoolCollab.Core.Abstractions/IFeatureFlagService.cs` carries no Configuration using; the
using lives only in `src/SchoolCollab.Core/Features/ConfigurationFeatureFlagService.cs`.

**UI-tester waiver — ACCEPTED by the gate**, on the three audit items it required, recorded in the
Execution log and adjudicated in `## UI Tester` (the `Error.razor`-only UI delta statement; the six
bUnit suites covering the moved components, required green under AC-3; the not-applicable marking
of `## UI Tester`).

### Diff review — "OK with notes" (0 × P0/P1; 1 × P2 — resolved and FIXED by the parent)

Reviewer (`kimi-k2.7-code`) confirmed W1–W5 implemented as specified:

- `src/SchoolCollab.Admin.Shared/SchoolCollab.Admin.Shared.csproj:14-18` — the five package
  replacements, none carrying a `Version=` attribute; the only remaining ProjectReference is
  `SchoolCollab.Core.Abstractions`;
- `src/SchoolCollab.Admin.Shared/Components/Pages/Error.razor` keeps `@page "/Error"` with
  `RequestId = Activity.Current?.Id;`;
- `src/SchoolCollab.Admin.Shared/Components/_Imports.razor` no longer uses
  `Microsoft.AspNetCore.Http`;
- `src/SchoolCollab.Core.Abstractions/SchoolCollab.Core.Abstractions.csproj` — zero packages,
  zero project references, zero framework references; all five moved files carry their explicit
  preserved namespaces, and `IFeatureFlagService.cs` correctly omits the Configuration using;
- only `src/SchoolCollab.Core/SchoolCollab.Core.csproj` and `Admin.Shared.csproj` reference
  `Core.Abstractions`; no reverse edge;
- the 12-file contacts move is identity-preserving — the 4 `.razor` + 4 `.razor.css` pairs at
  `similarity index 100%`, the 4 models at 94–96% (namespace line only);
- the both-usings deviation in
  `tests/SchoolCollab.Admin.Tests.Unit/ContactsEditorTests.cs:13,17` is justified (the plan's
  documented fallback);
- CI flips to `-f net10.0-android` on `windows-latest` with the workload step kept; CPM clean;
  no new `http(s)://` literal under `src/`; no unrelated deletions.

**The P2:** `.github/workflows/ci.yml:192-193` had dropped the plan's prescribed workload
rationale (keep `maui`; the android TFM is part of that workload; do not swap it for a slimmer
install). **Status: FIXED by the parent** — the rationale comment was restored post-review under
the tiny-rework allowance (below); no reviewer re-run was made for it.

### Parent-applied rework after the review (tiny-rework allowance — parent static check, no reviewer re-run)

1. `.github/workflows/ci.yml` — restored the workload rationale comment (the reviewer's P2).
2. Five stale durable-doc pointers to files this round moved (documents-only):
   `documents/configuration.md:753`, `documents/specs/activity-group-enrollment-impl.md:45`,
   `documents/specs/keycloak-ui-auth-integration.md:282`,
   `documents/solution/assignment-request-implementation-details.md:181`,
   `documents/solution/student-guardian-plan.md:187`. Historical `docs/plans/*` and
   `.pr-body.md` were deliberately left alone (historical record, not live pointers).

## Acceptance

**Verdict: CLOSED.**

### AC checklist (parent's authoritative build/test numbers, 2026-10-09)

| AC | Criterion | Verdict | Evidence (authoritative) |
|---|---|---|---|
| AC-1 | `dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj -f net10.0-android` — 0 errors | **MET** | Build succeeded — **0 errors, 0 warnings** (the headline). AC-1(b) mechanically verified: the head's android `obj/project.assets.json` resolved `frameworkReferences` set is **empty**, `Microsoft.AspNetCore.App` occurrences **0**. |
| AC-2 | `dotnet build SchoolCollab.slnx` — 0 errors | **MET** | Build succeeded — **0 errors** (also the W2 transitive-`ServiceDefaults`-drop safety net). |
| AC-3 | Affected suites green — 0 failures | **MET** | ArchitectureTests.Unit **102/0** (incl. the slnx-registration guard), Admin.Tests.Unit **632/0** (the moved-component bUnit suites), Core.Tests.Unit **126/0** (the split), Students.Tests.Unit **624/0**, Assignments.Tests.Unit **1064/0**. |
| AC-4 | No FrameworkReference on the head's path | **MET** | (a) `git grep -ln FrameworkReference -- "*.csproj"` = **5** projects (was 6; `Admin.Shared` gone — this check only counts csproj declarations, per the plan's note); (b) the android assets `frameworkReferences` carry no `Microsoft.AspNetCore.App` entry (same mechanics as AC-1(b)). |
| AC-5 | Moved-namespace consumer set byte-identical outside the move | **MET** | Consumer delta = the split's descendant exactly; the original `Features/FeatureFlagService.cs` appears as its legitimate rename (`R053` → `ConfigurationFeatureFlagService.cs`); the frozen patch excludes itself. |
| AC-6 | No new `http(s)://` literal under `src/` | **MET** | Diff review confirmed clean. |
| AC-7 | `SchoolCollab.slnx` registration + guard green | **MET** | Registered in the root slnx (not the mobile slnx); `NewProjectsSolutionRegistrationArchitectureTests` green inside the 102 (not in `KnownOrphanedProjects`). |
| AC-8 | Round doc completed (Tier 3) + patch shipped | **MET** | Worker Report (transcribed), Review, and Acceptance all filled; `documents/rounds/diffs-mobile-severance.patch` frozen from base `fab7dd5e` — **44 entries, 1,218 lines, 17 renames**, excludes itself. |

### Pinned-decision compliance

All owner pins held: Tier 3 full with the gate-waived UI-tester leg; principled and bounded
severance (no build-only shortcut); `SchoolCollab.Core.Abstractions` is zero-package/
zero-reference/zero-framework with all three namespaces preserved — zero consumer-file churn for
the abstractions move; the contacts family (the measured 12-file set) moved home to
`Students.Application` under its home namespace; exactly the two enums
(`SignatureRequirementMode`, `NotificationChannel`) moved, with the policy shapes, both
`IEffective…Resolver` interfaces and both resolver implementations staying in `Core`; the only
Core-edge is `Core → Core.Abstractions` (the reverse is forbidden and absent).

### Deviations adjudicated (all accepted)

1. **`ContactsEditorTests.cs` keeps both usings** (`:13`, `:17`) — the plan's documented fallback;
   accepted.
2. **Comment-only edits** in `Admin.Shared.csproj` and the `ci.yml` workload comment — accepted.
3. **`Admin.Shared.csproj`'s comment wording** says "no Microsoft.AspNetCore.App **framework
   reference**" rather than the literal `FrameworkReference` token, so the mechanical AC-4(a)
   grep does not false-positive — accepted; noted here because the comment intentionally avoids
   the token the AC greps for.
4. **`Directory.Packages.props` resolved the Components packages to 10.0.8** — not the plan's
   guessed 10.1.1/10.0.11; read from the restored graph, exactly as the plan's P2-#3 obligation
   required (resolve, never guess).
5. **Parent-applied tiny rework** (the `ci.yml` rationale comment + the five durable-doc pointer
   updates, listed in `## Review`) accepted on the parent's static check under the tiny-rework
   allowance; no reviewer re-run.

### Residual items — none blocking; carried to L3/L4

1. ~~**`SchoolCollab.Core` still restores the EF Core, RabbitMQ (and auth) packages** — recorded
   residual weight beyond the head's now-clean closure; revisit the Core-side package set at
   L3/L4.~~ **CORRECTED BY THE PARENT AT CLOSE (measured):** this item was inherited from the plan's
   pre-P1 text and is **false after the P1 fix**. `Core` is off the head's closure entirely
   (`Admin.Shared` dropped its `Core` reference), so the head restores **no** server-side package:
   the android target graph in `src/Mobile/SchoolCollab.Mobile/obj/project.assets.json` holds 140
   entries with **0** EF Core / Npgsql / RabbitMQ / Scrutor / `System.Security.Cryptography.Xml`, and
   `libraries` holds none either. Measurement trap for whoever re-checks this: a raw
   `grep -i entityframeworkcore <assets.json>` returns 44 hits and looks alarming, but every hit sits
   under `project.frameworks.<tfm>.centralPackageVersions` — NuGet's record of
   `Directory.Packages.props`, identical for all four TFMs and **not** part of the resolved graph.
   Parse `targets`/`libraries`, never grep the raw file.
2. **OD1's `Assignments.Application → Students.Application` closure question** — unchanged by
   this round (out of scope by the plan); carried to L4.
3. Only the android TFM was built/verified (Windows/iOS/MacCatalyst not re-verified in this
   pass); the full no-arg `dotnet test` remains the PR-stage parent check.
4. Historical `docs/plans/*` and `.pr-body.md` still name the old paths — deliberately left
   (historical record).

### Ledger

Both round rows exist in `documents/solution/round-runner-model-ledger.md`:
`mobile-host-skeleton` (L1 — backfilled at this round's close; it was missing, a
policy-decision-8 gap) and `mobile-severance` (L2).

## UI Tester

**Not-applicable — waiver adjudicated (accepted by the plan gate).**

- **Parent's rationale:** the round's UI delta is a pure move — no new UI surface. The only
  `.razor` *edit* is `Error.razor` (see the Execution log), the four components moving projects
  carry untouched markup except their namespace derivation, `.razor.css` scope recomputation is an
  identity, and the mobile head cannot launch any moved component from a CI host. The six bUnit
  suites covering the moved components (`ContactsEditorTests`, `TeacherDetailPageBunitTests`,
  `StudentFormModelMappingsTests`, `FormRowOrientationTests`,
  `StudentFormFieldsSectionEditTests`, `EditSidebarLayoutTests`) are a stronger, CI-runnable
  regression proof, and AC-3 already requires them green. Dispatching anyway would buy a Windows
  launch + admin-login smoke for zero incremental coverage.
- **Gate adjudication:** the plan gate **ACCEPTED** the waiver on the audit evidence recorded in
  the Execution log (the Error.razor-only statement, the six bUnit suites required green under
  AC-3, and this section's not-applicable marking).

## Post-close re-verification (2026-10-12, owner-approved delivery)

The round closed on base `fab7dd5e`. Before delivery, `main` moved ten commits (`598ea99e`, then
`ea7b2df1` when #327 merged), so the branch was rebased and re-verified. The verdict above stands for
the round as implemented; this section re-points it at the delivered base.

- **Rebase:** `feat/mobile-severance` replayed onto `ea7b2df1` (commit `775b96f2`), **no conflicts**.
  Four files overlapped with main's new work; git auto-merged each, and all four were checked to have
  kept *both* intents rather than one silently:
  - `.github/workflows/ci.yml` — kept main's `merge_group` trigger and `ENABLE_MAUI_CI` skip gate
    (#332) **and** this round's android flip (`-f net10.0-android`, step renamed).
  - `src/SchoolCollab.Admin.Shared/SchoolCollab.Admin.Shared.csproj` — kept this round's package swap
    (no `FrameworkReference`) **and** main's two packages (`Blazored.TextEditor`, `HtmlSanitizer`,
    #336) which landed *inside* the severed project.
  - `Directory.Packages.props` — this round's three `Microsoft.AspNetCore.Components*` 10.0.8 entries
    **and** main's rich-text group.
  - `documents/specs/mobile-hybrid-app.md` — this round's §8 L2 outcome annex **and** main's
    "currently SKIPPED" note on the mobile job.
- **Carriers:** still 5 repo-wide (`AI.Chat`, `Assignments.Application`, `ServiceDefaults`,
  `Settings.Application`, `Students.Application`) — none on the head's closure.
- **Re-verified on `ea7b2df1` (parent-run, authoritative):** android **0 errors / 3 warnings**, the 3
  being **pre-existing** `Admin.Shared` nullable warnings (`DialogServiceExtensions.cs:145` CS8601;
  `AssignmentAiPromptApiClient.cs:72` and `SignatureConsentTextApiClient.cs:67` CS8603), not introduced
  here; root `SchoolCollab.slnx` 0 errors; ArchitectureTests.Unit **105** (up from 102 — main's new
  `RazorMarkupHygieneArchitectureTests`, which passes against the 12 moved `.razor` files),
  Admin **651**, Core **126**, Students **624**, Assignments **1109**, Mobile **8** — 0 failures.
- **CI:** the mobile job remains skipped behind `ENABLE_MAUI_CI` (#332), so this round's android
  evidence is local; enabling the variable is the owner's switch (Q3(a), 2026-10-12).
- **Observed while re-syncing (not this round's defect):** #327's required `Build & Test` failed on
  `AssignmentCreateBunitTests.Create_MandatoryPolicy_SubmitsWithoutTheRetiredAuthorInputs` (bUnit
  `WaitForFailedException` on `#authoring-targets .targets-audience-item__value`) — a CI-only flake in
  the same class and on the same assertion as the 2026-10-09 flake (`Create_UnsetReviewPolicy_…`) — and
  re-ran green. `main`'s own push runs were green throughout.