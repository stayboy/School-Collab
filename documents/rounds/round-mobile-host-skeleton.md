# Round: mobile-host-skeleton (L1 of the mobile train)

**Provider:** pi, full Tier 3, Option A (models: glm-5.3-flash orchestrator, glm-5.3 plan-review — registry-resolved substitute for the skill's `ollama/glm-5.3:cloud`, which no longer exists in the active registry; resolved 2026-10-09 against `~/.pi/agent/models-store.json`, deepseek-v4.1-flash worker, kimi-k2.7-code reviewer, kimi-k2.7-code escalator, minimax-m3 tester)
**Status:** CLOSED — carried by commit `41d5bb71` on `stack/30-mobile-host-skeleton` (50 files,
+3,425/−22). Not pushed; no PR/stack registration yet (each gated on its own owner instruction).
**Round base:** `afc189e2` — worktree `School-Collab-mobile-train`, branch `stack/30-mobile-host-skeleton`. The worktree is clean at base except this round's own documents (`documents/specs/mobile-hybrid-app.md`, this round doc).
**Spec:** `documents/specs/mobile-hybrid-app.md` — §4 layout, §7 CI, §8 L1 acceptance, §3.2 the second-pass dependency measurement.

## Plan

**Goal.** L1 of the mobile train: a Blazor Hybrid MAUI head that builds, renders one real
`Admin.Shared` component (the reuse spike), and owns a Windows CI job — with the head never entering
`SchoolCollab.slnx` and the four existing ubuntu jobs untouched.

**Pinned decisions (owner, 2026-10-09).** OD0 = **(b) spike-first + (d) closure-free-subset rule**
(spec §3.2). The spike runs against `Admin.Shared` **as-is** — the closure stays intact in L1;
severance is L2. Form factor: both phone and tablet, pages reworked (D2). No device-native features.

**Scope — in:**

1. **The head.** `src/Mobile/SchoolCollab.Mobile/` scaffolded via
   `dotnet new maui-blazor -n SchoolCollab.Mobile` and adapted to repo conventions (namespace
   `SchoolCollab.Mobile`, nullable + implicit usings, file-scoped namespaces). `TargetFrameworks`:
   `net10.0-android;net10.0-ios;net10.0-maccatalyst;net10.0-windows10.0.19041.0`.
2. **The solution.** `src/Mobile/SchoolCollab.Mobile.slnx` (hand-written XML matching the root
   slnx's format) containing the head, `SchoolCollab.Mobile.Logic`, and
   `SchoolCollab.Mobile.Tests.Unit`.
3. **The Logic library.** `src/Mobile/SchoolCollab.Mobile.Logic/` — `net10.0`, containing one real
   piece: `MobileApiOptions` (a config class bound from `appsettings*.json`, holding API base URLs) —
   which proves spec D8's config-only rule from day one and gives CI something to test.
4. **The test project.** `tests/SchoolCollab.Mobile.Tests.Unit/` — `net10.0`, MTP
   (per `.github/copilot/rules/testing.md`), with a discriminating test over `MobileApiOptions`.
5. **Root solution membership (the spec §4 rule).** `SchoolCollab.slnx`: add
   `SchoolCollab.Mobile.Logic` and `SchoolCollab.Mobile.Tests.Unit`; the **head is NOT added**.
6. **The guard extension (explicit, deliberate).**
   `tests/SchoolCollab.ArchitectureTests.Unit/NewProjectsSolutionRegistrationArchitectureTests.cs`:
   extend `ReadRegisteredProjectPaths` (or the comparison) so a project is considered registered if
   it appears in **any checked-in `*.slnx`** in the repo (union all `*.slnx` outside `bin`/`obj`),
   not only the root `SchoolCollab.slnx`. Rationale in the doc comment: the guard's purpose — "no
   project silently drops out of solution build/test runs" — is preserved; a project in *no* solution
   still fails; the head is not an orphan and must not be listed in `KnownOrphanedProjects`.
   The live-existence check for `KnownOrphanedProjects` stays as-is. **Resolution rule (plan-review
   P2):** each slnx's `Project Path` values resolve against **that slnx's own directory** (the mobile
   slnx's paths are relative to `src/Mobile/`), then normalize to repo-root-relative form before
   unioning — otherwise the mobile entries never match and the guard stays red.
7. **CPM.** `Directory.Packages.props`: new `<ItemGroup Label="MAUI">` with
   `Microsoft.Maui.Controls` and `Microsoft.AspNetCore.Components.WebView.Maui` (latest stable 10.x,
   expected **10.0.110** — re-verify against NuGet at implementation; the `.csproj` carries no
   `Version` attribute).
8. **CI.** `.github/workflows/ci.yml`: new `mobile` job, `runs-on: windows-latest` — checkout,
   `setup-dotnet` 10.x, `dotnet workload install maui`, then restore + build
   **`src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj` with `-f net10.0-windows10.0.19041.0`**
   (revised mid-round, see the deviation note below) — never solution-level `-f` (plan-review P2:
   it passes `TargetFramework` as a global property that would force one TFM onto the
   plain-`net10.0` Logic/Tests members of the mobile slnx). The four existing jobs are not
   renamed or restructured; the mobile job gates PRs the same way (no `continue-on-error`).
9. **The spike.** The head references `SchoolCollab.Admin.Shared` **as-is**; a `Spike.razor` page
   renders `FieldDisplay` (one of the 19 closure-clean components, spec §3.2) with a hard-coded
   sample value, styled per the `blazor-css-isolation` skill (scoped `.razor.css`, no inline styles).
   Fallback if `FieldDisplay` needs an unregistered service: use `Chip` (also closure-clean) and
   record why in the WORKER REPORT.
10. **The inventory (the spike's actual deliverable).** The WORKER REPORT must record:
    (a) restore/build verdict for the `Admin.Shared` closure under `net10.0-android`;
    (b) every warning class emitted (trim/AOT/analyzer), summarized;
    (c) an approximate package/size delta if cheaply measurable (count of transitively restored
    packages is enough — no size forensics);
    (d) confirmation (or correction) of the 19-clean/5-dirty `Admin.Shared` split from spec §3.2.

**Scope — out:** any severance (L2 — no edits to `Admin.Shared`, `Core`, `Students.Core`,
`ServiceDefaults`); any auth/PKCE/realm change (L3); feature screens (L4); AppHost or its
`Program.cs`; the four existing CI jobs; adding the head to `SchoolCollab.slnx`; renaming existing
jobs; `.gitignore` changes beyond what the template requires.

**Constraints the worker must honor:**

- **No `http://`/`https://` string literal in any `.cs` under `src/`** (including `src/Mobile/`):
  base URLs live in `appsettings*.json` and `MobileApiOptions` binds them. **Rationale (corrected by
  the plan review):** `CrossModuleWiringTests` *silently skips* localhost-class hosts
  (`excludedHosts` = `localhost`, `host.docker.internal`, `0.0.0.0`, `example.com/org`), and plain
  `https://` literals never match its regex at all — so **CI does not catch this**; this plan
  constraint is the only defense, and the diff review must grep `src/Mobile` for `http(s)?://`
  literals.
- `.razor`/`.razor.css` follow `.github/copilot/rules/blazor-components.md` and the
  `blazor-css-isolation` skill; C# follows `.github/copilot/rules/dotnet-best-practices.md`.
- CPM: no `Version=` attributes on `PackageReference` (NU1008/NU1009).
- Repo-scoped searches only (`grep`/`rg` under `src/`, `tests/`, or the NuGet cache) — never a
  filesystem-wide scan.
- `dotnet build` for the android TFM must run on this machine's installed workloads
  (android 36.1.69, ios/maccatalyst 26.5.10301, maui-windows 10.0.20).

**Expected files** (new unless noted):

```
src/Mobile/SchoolCollab.Mobile.slnx
src/Mobile/SchoolCollab.Mobile/                       (template scaffold, adapted: csproj, MauiProgram.cs,
                                                       App.xaml(.cs), MainPage.xaml(.cs) with BlazorWebView,
                                                       Components/{Routes.razor,_Imports.razor,MainLayout.razor},
                                                       Components/Pages/Spike.razor(+.razor.css),
                                                       wwwroot/{index.html,app.css}, Platforms/*,
                                                       Properties/launchSettings.json, appicon/splash assets)
src/Mobile/SchoolCollab.Mobile.Logic/                 (csproj + MobileApiOptions.cs)
tests/SchoolCollab.Mobile.Tests.Unit/                 (csproj + MobileApiOptionsTests.cs)
SchoolCollab.slnx                                     (modified: 2 new <Project> entries)
Directory.Packages.props                              (modified: MAUI label group)
.github/workflows/ci.yml                               (modified: 1 new job)
tests/.../NewProjectsSolutionRegistrationArchitectureTests.cs   (modified: any-slnx registration)
documents/specs/mobile-hybrid-app.md                  (rides this layer)
```

**Acceptance criteria (all must be discriminating — each fails against today's tree):**

- **AC-1** `dotnet build SchoolCollab.slnx` at repo root: 0 errors, with `SchoolCollab.Mobile.Logic`
  and `SchoolCollab.Mobile.Tests.Unit` registered and the head absent.
- **AC-2** `dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj
  -f net10.0-windows10.0.19041.0`: 0 errors on this Windows machine (csproj-level `-f`; the
  android TFM is L2's criterion — see the mid-round revision below).
- **AC-3** The spike page renders `FieldDisplay` (or the recorded fallback) with a fixed sample when
  the Windows target launches — a green build alone does **not** satisfy this. **Verified by the
  dispatched UI tester** (screenshot of the launched Windows target; the scope handover is the spike
  surface) and re-verified by the parent. *Q1 from the plan review is adjudicated **full, not lean**:
  the spec header's "L1 runs lean — no UI files" was an authoring error contradicting §8's own AC-3 —
  the spike writes `.razor` files, the deterministic UI trigger fires, and the owner accepted full
  Tier 3 for the train. Spec header corrected 2026-10-09.*
- **AC-4** The `mobile` CI job exists (`windows-latest`, workload install, head-csproj
  `-f net10.0-windows10.0.19041.0` build) and the four existing job names are unchanged.
- **AC-5** `SchoolCollab.ArchitectureTests.Unit` green — including the extended
  `NewProjectsSolutionRegistrationArchitectureTests` now covering the mobile solution's membership —
  and `CrossModuleWiringTests` green. Plus a **negative discriminator** (plan-review P2): the parent
  temporarily unregisters the head from `src/Mobile/SchoolCollab.Mobile.slnx`, confirms the extended
  guard goes **red**, then restores it — so the union extension cannot be implemented vacuously.
- **AC-6** The inventory (scope item 10) is present and complete in the WORKER REPORT.

**Open decisions:** none for L1. OD1–OD4 (spec §9) belong to L3/L4 planning.

**Mid-round revision (2026-10-09, worker intercom, parent-adjudicated).** The worker proved the
spike's central question and it invalidated two acceptance criteria as written: the `Admin.Shared`
closure **restores and compiles**, but `Admin.Shared` and `ServiceDefaults` each declare
`<FrameworkReference Include="Microsoft.AspNetCore.App">`, NuGet records it transitively for the
head in **every** TFM, and a self-contained mobile build needs a runtime pack per framework
reference — which `Microsoft.AspNetCore.App` has **none of** for android RIDs → **NETSDK1082**,
two errors. Causation proven by removing the head's `Admin.Shared` reference; the documented
escape hatches (`DisableTransitiveFrameworkReferenceDownloads`, evaluation-time
`FrameworkReference Remove`) do not work. Option A (a head-side shim stripping the reference) was
**rejected**: it builds 0 errors but emits **24 MSB3277** version conflicts against assemblies the
app will not ship — a lying build that hides exactly what L2 must fix. **Adopted (B):** L1's build
assertion becomes the head's `net10.0-windows10.0.19041.0` TFM (verified 0 errors / 0 warnings
today, closure intact); the android-TFM assertion **moves to L2**, where severance is now a proven
prerequisite rather than an option. Scope item 8, AC-2 and AC-4 above are reworded accordingly;
the inventory records the finding as its headline. The Windows TFM is a device-relevant target —
it is the form factor AC-3's render proof runs on.

## Review

**Plan review (step 2b — before the worker) · model `ollama-cloud/glm-5.3` · Verdict ACCEPT, no P1, five P2s (all folded into `## Plan` above), Q1 adjudicated full by the parent.**

```text
PLAN REVIEW
Verdict: ACCEPT
P1: none
P2: Plan §Constraints (no-http-literal) — the claim "`http://localhost…` matches its regex and fails
    the build plus the dedicated guard job" is false: `CrossModuleWiringTests.ScanClientRegistrations
    (excludedHosts` set containing `localhost`, `host.docker.internal`, `0.0.0.0`,
    `example.com/org`) silently skips localhost literals, and plain `https://` literals never match
    `BaseAddressLiteralRegex` at all. The plan's blanket no-literal discipline is still correct
    (deliberately stricter than the guard), but the rationale gave false confidence that CI would
    catch a violation — reworded in Plan; CI does NOT catch localhost; the diff review must grep
    `src/Mobile` for `http(s)?://` literals.
P2: Plan scope item 6 — the union of all `*.slnx` must resolve each slnx's `Project Path` against
    that slnx's OWN directory (mobile-slnx paths are relative to `src/Mobile/`) before normalizing
    and unioning; as written the mobile entries would never match and the guard stays red. Folded
    into item 6. (The walk finds the head — `EnumerateProjects` walks all of `src/` recursively —
    so the extension is necessary and the head correctly must not join `KnownOrphanedProjects`.)
P2: Plan items 2/8 + AC-2/AC-4 — solution-level `-f` passes `TargetFramework` as a global property
    that OVERRIDES single-TFM projects (Logic/Tests would be forced to build as net10.0-android or
    fail), not skip them. Folded in: build the head csproj directly with `-f net10.0-android`, both
    locally and in CI.
P2: Plan §Expected files vs spec header "L1 runs lean — no UI files" — L1 writes `.razor` files
    (unavoidable, spec-mandated by §8 L1-3). Resolved by correcting the spec header: L1 is a FULL
    Tier-3 round; "lean" was an authoring error.
P2: AC-5 "green" alone is weak; added a negative discriminator — temporarily unregister the head
    from the mobile slnx, confirm the extended guard goes red, restore.
Open decisions: Q1: who executes AC-3's render proof (lean parent vs dispatched tester)? — ➡️
    recommended parent-verified in lean mode; ADJUDICATED BY THE PARENT AS FULL: the tester is
    dispatched (spec header corrected; the owner accepted full Tier 3 for the train).
Gates: UI: risk (razor files written in L1 — spike page + layout; mitigated by blazor-components.md +
    blazor-css-isolation citation, scoped .razor.css, no inline styles) · contract: none ·
    migrations: none · secrets: none
Acceptance honesty: ok — every AC has a component that is false against today's tree. Feasibility
    verified with evidence: FieldDisplay exists (src/SchoolCollab.Admin.Shared/Components/FieldDisplay.razor
    — parameters Label/ChildContent, no @inject, no server usings; the 19-clean/5-dirty split and
    FieldDisplay's cleanliness check out; fallback Chip exists); root slnx format and its 17 test
    projects as claimed; Directory.Packages.props has no MAUI group yet and the no-Version CPM rule
    is honored; ci.yml has exactly the four existing jobs the plan names plus the guard job.
```

*(Condensed from the reviewer's inline block; P1 line and gates preserved verbatim. The full block is
in the round's async run log.)*

**Diff review (step 4, after the worker) · model `ollama-cloud/kimi-k2.7-code` · Verdict PASS, no P1, no P2.**

```text
REVIEW
Verdict: PASS
P1: none
P2: none
Best-practices: no overwrites, skills honored, readable
```

The reviewer's out-of-band structured extras (changed-file inventory, two grep results, validation
notes) were consistent with its verdict: no `http(s)://` literal and no `Version=` attribute under
`src/Mobile`; root slnx membership exactly as planned; the guard extension preserves
`KnownOrphanedProjects` + its live-existence check and is non-vacuous; exactly one new `windows-latest`
CI job; the spike uses scoped `.razor.css`, no inline styles, no `<style>` blocks. Its recorded residual:
AC-3 (render proof) and build/test results are out of the static reviewer's scope — verified by the
dispatched UI tester and the parent's build authority respectively (both done: see `## Acceptance`).
All 8 worker-recorded deviations judged plan-conformant.

## Worker Report

*Worker: `deepseek-v4.1-flash` · completed 2026-10-09 · 1 supervisor intercom (option B adjudication)*

**Build**
- `dotnet build SchoolCollab.slnx` → **0 errors** (17 warnings incremental / 96 on first full pass — all
  pre-existing; zero from `Mobile.Logic` or `Mobile.Tests.Unit`).
- `dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj -f net10.0-windows10.0.19041.0`
  → **0 errors, 0 warnings**; produces `bin/Debug/net10.0-windows10.0.19041.0/win-x64/SchoolCollab.Mobile.exe`
  (+ `appsettings.json`) — the launchable unpackaged hand-off for the UI tester.
- `-f net10.0-android` → **2 errors** (NETSDK1082, deferred to L2 per option B).

**Tests**: `Mobile.Tests.Unit` **8/0** · `ArchitectureTests.Unit` **102/0** · `CrossModuleWiringTests` **2/0**.

**Guard self-check (AC-5)**: head registered → green; unregistered from the mobile slnx → red,
naming exactly `src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj`. Non-vacuous. Parent re-runs both.

**Deviations** (all recorded, none unauthorized):
1. **Option B** (parent-adjudicated mid-round): android-TFM assertion deferred to L2; L1 asserts
   `net10.0-windows10.0.19041.0` locally and in CI. Root cause: the transitive
   `Microsoft.AspNetCore.App` `FrameworkReference` from `Admin.Shared` + `ServiceDefaults` has no
   runtime pack for any android RID (causation proven by deleting the head's `Admin.Shared` reference).
2. Two CPM entries beyond the plan's two MAUI ones, both required by the template's own code path:
   `Microsoft.Extensions.Logging.Debug` (`builder.Logging.AddDebug()`),
   `Microsoft.Extensions.Configuration.Json` (head's embedded `appsettings.json` load — MAUI adds
   no configuration sources of its own; verified against `Microsoft.Maui.dll` + the MAUI SDK targets).
3. `Mobile.Logic` is a plain `Microsoft.NET.Sdk` `net10.0` project, not spec §4's "RCL" — plan scope
   item 3 says `net10.0` + one real piece, and there are no `.razor` files yet.
4. Plan-review P2 applied as written: CI builds the head **csproj** with csproj-level `-f`, never the slnx.
5. Head kept the template's OS-conditional `TargetFrameworks` expression (Windows-visible set = the
   plan's four); `ApplicationId` → `com.schoolcollab.mobile`.
6. Added `appsettings.json` + explicit embedded-resource load in `MauiProgram` (not in expected
   files) — the minimum making D8's "binds from appsettings" real; base URLs live only there.
7. Deleted template sample artifacts: `Components/Pages/Home.razor` (spike owns `/` — the launched
   target lands on the spike, AC-3's deterministic surface) and `dotnet_bot.svg` + its `MauiImage` line.
8. Trimmed two template comments carrying `aka.ms` URLs so the plan's `http(s)?://` grep is clean for
   `.cs`/`.razor`; XAML `xmlns` schema URLs remain necessarily; no `.cs`/`.razor` URL literal exists.

**Inventory**
- **(a)** Closure under android: restore **succeeds**, all 8 projects **compile, 0 warnings** — the
  failure is app-level runtime-pack resolution, not closure source. Windows TFM with closure as-is:
  0 errors / 0 warnings.
- **(b)** Headline: **NETSDK1082** (android, 2 errors, `ResolveRuntimePackAssets`). Windows: 0 warnings.
  Android compile phase: 0 warnings. **No trim/AOT/analyzer warnings — because `PublishTrimmed`/AOT
  do not run on `build`; publish-trim was NOT attempted in L1 (recorded gap, not a clean bill).**
  Rejected option-A probe: 0 errors but **24 MSB3277** version conflicts — not implemented.
- **(c)** **228 packages** restored for android (245 distinct libraries across all head TFMs; 151 for
  the windows TFM). `obj/project.assets.json` records `Microsoft.AspNetCore.App` as the head's
  frameworkReference on every TFM.
- **(d)** **19-clean/5-dirty CONFIRMED**: dirty = exactly `ContactChangeDialog`, `ContactFormFields`,
  `ContactsEditor`, `GateBase`, `GuardianEditFields`; `FieldDisplay` clean (FluentUI only) and is what
  `Spike.razor` renders — hard-coded sample, scoped `Spike.razor.css`, no inline styles.

**Open risks**: AC-3's render proof is the UI tester's; AC-2/AC-4 rest on the authorized reword;
android blocker is a hard L2 prerequisite. No files staged.

**Changed files** — modified: `ci.yml` (new `mobile` job), `Directory.Packages.props` (MAUI group,
  10.0.110, + 2 Microsoft.Extensions entries), `SchoolCollab.slnx` (Logic + Tests only, head absent),
  `NewProjectsSolutionRegistrationArchitectureTests.cs` (any-slnx union, own-directory resolution,
  doc rationale, orphans untouched). New: `src/Mobile/SchoolCollab.Mobile.slnx`, the head project
  (43 files), `Mobile.Logic` (csproj + `MobileApiOptions.cs`), `Mobile.Tests.Unit` (csproj + tests).

## Acceptance

**Orchestrator-accept run (2026-10-09) · static adjudication — builds/tests are the parent's authoritative
numbers (they are the round's only build/test authority); nothing was built or run in this pass.**

### AC checklist

| AC | Verdict | Evidence (parent's authoritative numbers) |
|---|---|---|
| **AC-1** — root slnx builds, Logic + Tests registered, head absent | **VERIFIED (parent)** | `dotnet build SchoolCollab.slnx` → **0 errors** (17 warnings, all pre-existing; zero from `SchoolCollab.Mobile.Logic` or `SchoolCollab.Mobile.Tests.Unit`); `SchoolCollab.slnx` carries the two new entries and not the head. |
| **AC-2** — head builds on the Windows TFM | **VERIFIED (parent)** | `dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj -f net10.0-windows10.0.19041.0` → **0 errors, 0 warnings** (as revised mid-round, option B). |
| **AC-3** — spike renders `FieldDisplay` in the launched Windows target | **VERIFIED (UI tester, minimax-m3)** | Tester launched the unpackaged exe, waited 15 s, captured `%TEMP%\sc_spine_spike.png` (2560×1440), and confirmed both hard-coded values ("Ada Lovelace", "Grade 5 Blue") visible in the rendered card; scoped CSS applied; `ErrorBoundary` never engaged. Full block in `## UI Tester`. |
| **AC-4** — `mobile` CI job exists, four existing jobs unchanged | **STATIC — verified in the patch; first real execution = the PR's CI run** | Diff review confirms exactly one new `windows-latest` job (workload install + head-csproj csproj-level `-f net10.0-windows10.0.19041.0`) and no rename/restructure of the four existing jobs. |
| **AC-5** — guard green + negative discriminator + wiring tests | **VERIFIED (parent; positive and negative)** | `ArchitectureTests.Unit` (full) **102/0** · `CrossModuleWiringTests` **2/0** · negative discriminator re-run: with the head unregistered from the mobile slnx the extended guard goes RED naming `src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj`, and the slnx was restored byte-identical — the union extension is non-vacuous. |
| **AC-6** — inventory present and complete in the Worker Report | **VERIFIED (orchestrator)** | Report items (a)–(d) all present: closure verdict, warning classes with the NETSDK1082 headline and the rejected option-A probe, package counts (228 android / 245 all TFMs / 151 windows), 19-clean/5-dirty confirmed with `FieldDisplay` clean and rendered. |

**Diff review** (kimi-k2.7-code): **PASS — P1 none, P2 none** — "no overwrites, skills honored, readable".
All 8 worker deviations judged plan-conformant (option B, CPM extras, plain-SDK Logic, csproj-level `-f`,
OS-conditional TFMs, appsettings load, template-artifact deletion, aka.ms trim). The plan's only defense
for the no-http-literal constraint — the `src/Mobile` `http(s)?://` literal grep — came back **clean**.

### Residuals (stated honestly)

1. **AC-3 — RESOLVED by the UI tester (PASS)**. The launch + screenshot proof is recorded in
   `## UI Tester`; the acceptance's original "pending" wording is superseded. The only gate left
   on this round is the owner's commit authorization.
2. **AC-4's first real execution is the PR's CI run** — the job is verified statically in the patch
   only; no workflow has run yet.
3. **Publish-trim was NOT attempted in L1.** The worker recorded this as a gap: there are **no
   trim/AOT/analyzer warnings only because `PublishTrimmed`/AOT do not run on `build`** — no trim/AOT
   bill of health is claimed or exists.
4. **The android TFM is deferred to L2 by parent-adjudicated option B** (NETSDK1082 ×2 — the transitive
   `Microsoft.AspNetCore.App` FrameworkReference has no runtime pack for any android RID). This is the
   round's headline finding, not a defect of the delivered layer.

### UI-tester scope handover (UI round — deterministic trigger fires: the spike writes `.razor` files)

**Changed UI files (exactly this list, no more):**

- `src/Mobile/SchoolCollab.Mobile/Components/Routes.razor` — the `Router`; maps `/` to the spike page
  via `MainLayout` and unmatched routes to `NotFound`.
- `src/Mobile/SchoolCollab.Mobile/Components/Layout/MainLayout.razor` — the layout the spike renders in.
- `src/Mobile/SchoolCollab.Mobile/Components/Layout/MainLayout.razor.css` — scoped CSS isolation for the layout.
- `src/Mobile/SchoolCollab.Mobile/Components/Pages/Spike.razor` — **the AC-3 surface**: route `/`;
  renders `SchoolCollab.Admin.Shared`'s `FieldDisplay` twice inside a `FluentCard`, hard-coded sample
  values (`Student` → "Ada Lovelace", `Class` → "Grade 5 Blue"), wrapped in an `ErrorBoundary` that
  surfaces render failures as a `FluentMessageBar`.
- `src/Mobile/SchoolCollab.Mobile/Components/Pages/Spike.razor.css` — scoped CSS isolation for the spike
  (per the `blazor-css-isolation` skill; no inline styles).
- `src/Mobile/SchoolCollab.Mobile/Components/Pages/NotFound.razor` — the unmatched-route fallback.
- `src/Mobile/SchoolCollab.Mobile/wwwroot/index.html` — the host page for the `BlazorWebView`.
- `src/Mobile/SchoolCollab.Mobile/wwwroot/app.css` — the head's base stylesheet.

**Surface the tester sees:** launching the Windows target lands on route `/` — the spike page rendering
`FieldDisplay` with the hard-coded sample inside a `FluentCard`; any unmatched route renders
`NotFound`. Navigation entry points: the router's initial navigation to `/` on app start (the only
in-app navigation surface in L1); there are no other pages or nav links this layer adds.

**Launchable target:** `bin/Debug/net10.0-windows10.0.19041.0/win-x64/SchoolCollab.Mobile.exe`
(produced by the parent-verified 0-error/0-warning Windows-TFM build).

**The tester's scope is exactly the file list above, nothing more** — no data loading, no backend, no
other routes, no native-device behavior.

### Verdict

**CLOSED-with-AC-3-pending-tester.** AC-1, AC-2, AC-5, AC-6 verified against the parent's authoritative
build/test numbers; AC-4 statically verified with first execution at the PR's CI run; the only open
criterion is AC-3, which is by design the dispatched UI tester's. The four residuals above (AC-3
pending, AC-4 first-run, unattempted publish-trim, android TFM moved to L2) are all consistent with —
and recorded in — the plan, the Worker Report, and the diff review; no unsupported gaps remain in L1.
Round proceeds to UI testing; nothing else blocks closure of this layer's non-UI acceptance.

## UI Tester

*UI tester: `minimax-m3` · completed 2026-10-09 · scope = the acceptance handover verbatim — the
handover is in `## Acceptance` above*

```text
UI TEST
Scope ack: Hunted only the handed-over surfaces — Routes.razor, MainLayout.razor(+css),
Spike.razor(+css), NotFound.razor, wwwroot/index.html, wwwroot/app.css. Launched the unpackaged
Win exe, gave the BlazorWebView 15 s, captured %TEMP%\sc_spine_spike.png (2560×1440), confirmed
both hard-coded FieldDisplay sample values ("Ada Lovelace", "Grade 5 Blue") are visible in the
rendered card, then closed the app via Stop-Process.
Verdict: PASS
P1: (none)
P2: (none)
Out-of-round observations: none
```

Tester evidence beyond the block: the spike title "Admin.Shared reuse spike" visible; the scoped-CSS
card padding applied; `FieldDisplay` rendered without throwing (the `ErrorBoundary` never engaged —
no error bar). `NotFound` was not externally triggerable in this packaging (no initial-route
override on the unpackaged BlazorWebView launch); its wiring is correct on paper
(`NotFoundPage="typeof(Pages.NotFound)"` in `Routes.razor`) and no defect was inferable from the
static read — recorded as packaging context, not a finding.