# Startup flag governance — one home for the deployment-time switches

> **Status: ADOPTED — 2026-09-25 grill round, all six recommendations accepted (Q1–Q6).
> IMPLEMENTED 2026-09-25: Tier 2 round `startup-flag-governance`, CLOSED (reviewer PASS, 0 P1,
> P2s fixed) — see [`../rounds/round-startup-flag-governance.md`](../rounds/round-startup-flag-governance.md).
> Sequenced as a stacked PR on PR #262 per owner instruction ("do not merge yet"), overriding the
> Q6 "after #262 merges" note.**
>
> Related: [`../solution/feature-flag-workflow.md`](../solution/feature-flag-workflow.md)
> (the two-kind model — the authority this spec extends), [`../configuration.md`](../configuration.md) §5,
> [`../AGENTS.md`](../AGENTS.md) "Feature Flags",
> [`../solution/central-config-service-plan.md`](../solution/central-config-service-plan.md),
> `tests/SchoolCollab.ArchitectureTests.Unit/AppHostLoginUiFlagWiringArchitectureTests.cs`
> (the guard pattern this spec generalises), the deferred flag-administration roles/policies
> discussion (owner, 2026-09-24).

## 0. What was adopted

`FEATURE:DisableOIDCAuth` becomes an **AppHost parameter fanned out to its six consumers** —
exactly the mechanism its sibling startup switch, `FEATURE:DisableKeycloakLoginUi`, already
uses — with **zero per-host copies** of the value, a **fail-closed committed default**, and
the dev posture carried by the AppHost's own Development file. The **read stays where it
is**: a registration-time `IConfiguration` lookup inside `AddAuthAndTenancy`. Governance
changes *where the default is written*, never *when or how the value is read*.

| Decision | Settled outcome (2026-09-25) |
| :--- | :--- |
| D0 — adopt | **Yes.** Ratified model: three homes, one write-point each — AppHost `Parameters:` for deployment-time config incl. startup switches; the Config service for runtime flags; per-host files for `Logging`/`AllowedHosts` and documented fallbacks only |
| D1 — per-host Development fallback | **DROPPED (reversal of the draft).** Zero per-host copies; ad-hoc standalone debugging uses the documented env-var one-liner (§2). Playwright requires the full AppHost anyway; tests inject in-memory config |
| D2 — base `appsettings.json` carries no value | **Yes** — fail-closed OIDC |
| D3 — default's home | **AMENDED (reversal of the draft).** Base default `"false"` (what `aspire publish` bakes) + `"true"` in `AppHost/appsettings.Development.json` `Parameters:` (what `aspire run` uses) — the repo's proven pattern for dev-only values (the Keycloak dev secrets live exactly there and demonstrably reach the containers) |
| D4 — parameter id | `feature-flag-disable-oidc-auth` (kebab-case like its sibling; identical to the historic removed id, so the previously stale docs snippet becomes retroactively correct) |
| D5 — mode / sequencing | **Tier 2** (worker + static diff reviewer); branch **after PR #262 merges** (both rounds touch `AppHost/Program.cs`) |

## 1. Findings (reviewed, with evidence)

**F1 — The repo already has a two-kind flag model.** Runtime, mutable, tenant-overridable
flags live in the Config service (`settings-api` + config-db, admin UI `/config-flags`,
`ConfigFeatureFlagService`, audited). Deployment-time startup switches are read once from
`IConfiguration` at registration. Authority: `feature-flag-workflow.md`, `configuration.md` §5.
(One deliberate hybrid: `RequireAssignmentApproval` is a *runtime* flag whose **cold-start
value** also has an AppHost parameter — the one flag that legitimately spans both homes.)

**F2 — `DisableOIDCAuth` is registration-time, not compile-time.** No `#if`, no `const`, no
MSBuild property anywhere. `AuthTenancyExtensions.cs:109` reads it inside `AddAuthAndTenancy`
via `IsFlagEnabled` (`:272-278`):

```csharp
var value = configuration[$"FeatureFlags:{featureKey}"] ?? configuration[featureKey];
return bool.TryParse(value, out var enabled) && enabled;
```

Two load-bearing properties: (a) it accepts **two JSON shapes** — nested
`FeatureFlags:FEATURE:DisableOIDCAuth` *or* the bare `FEATURE:DisableOIDCAuth` key — which is
why the scatter below drifted in shape without anything failing; (b) a missing or unparseable
value parses to `false`, i.e. **OIDC** — already fail-closed in the right direction.

**F3 — It is the one startup switch not AppHost-governed.** Six consumers carry it in their
**base** `appsettings.json`, dev default `"true"`:

| Host | Today's source | Shape |
| :--- | :--- | :--- |
| `auth` | `src/SchoolCollab.Auth/appsettings.json:10` | flat `"FEATURE:DisableOIDCAuth"` |
| `admin` | `src/SchoolCollab.Admin/appsettings.json:11` | nested `"FEATURE": { … }` |
| `families` | `src/SchoolCollab.Families/appsettings.json:15` | nested (+ design comment `:10-12`) |
| `settings-api` | `src/Settings/SchoolCollab.Settings.Api/appsettings.json:10` | flat |
| `students-api` | `src/Students/SchoolCollab.Students.Api/appsettings.json:11` | nested |
| `assignments-api` | `src/Assignments/SchoolCollab.Assignments.Api/appsettings.json:11` | nested |

The AppHost declares **no** parameter for it and fans it to **nobody** — deliberate, per the
comment at `Program.cs:218-225` and §5's "Historical AppHost-`Parameters:` model (superseded)".
Meanwhile `DisableKeycloakLoginUi` — the *same class* of flag, read by the *same helper* in
the *same registration path* — **is** AppHost-governed: parameter `Program.cs:167`, default
`AppHost/appsettings.json:25`, four fan-outs (`:400, :437, :486, :517`), guarded by
`AppHostLoginUiFlagWiringArchitectureTests`. Two flags, one kind, two mechanisms.

**F4 — That contradicts the repo's binding rule.** AGENTS.md: *"Feature flags are centralized
in the AppHost's `Parameters:` block … Do not scatter flag values across individual service
`appsettings.json` files."* Six scattered entries violate the letter of it today. (The same
sentence also overstates centralisation for *runtime* flags, which §5 correctly moved to the
Config service — the AGENTS.md amendment belongs in the implementation round.)

**F5 — Stale docs cluster around the removed parameter.** §5's *"Setting a flag"* still
instructs `dotnet user-secrets set "Parameters:feature-flag-disable-oidc-auth" "true"` — a
parameter that no longer exists — and still claims `AppHost/appsettings.json` "is the
canonical record of every flag's default value". `configuration.md:1048` spells the env var
`FeatureFlags__FEATURE:DisableOIDCAuth` (single `_` + a literal `:` in the *name*) while
`:1049` uses the portable `FeatureFlags__FEATURE__DisableKeycloakLoginUi` form.

**F6 — The current default is fail-open, and would survive into the first deployment.** Dev
`"true"` lives in the **base** file, so any deployment that forgets the env var silently
runs `TestAuth` — a fake authenticated principal — on all six hosts at once. There is **no
production deployment today** (verified: no bicep, no azd, no Dockerfile, no publish
profile; CI is build+test only), so the risk is latent — but defaults are forever, and
adoption is the cheapest moment to fix one.

**F7 — The blast radius of a wrong value is asymmetric.** Flag wrongly ON in prod: fake
identity everywhere. Wrongly OFF in dev: hosts that need Keycloak lose the dev bypass. The
adopted design makes the first state unconstructible (D3) and keeps the second a deliberate,
visible act.

## 2. Adopted design

**One parameter, six fan-outs, two AppHost-side values, zero per-host copies.**

```
AppHost/appsettings.json               Parameters: { "feature-flag-disable-oidc-auth": "false" }  ← fail-closed; what publish bakes
AppHost/appsettings.Development.json   Parameters: { "feature-flag-disable-oidc-auth": "true" }   ← dev posture (aspire run)
AppHost/Program.cs                     var disableOidcAuth = builder.AddParameter("feature-flag-disable-oidc-auth");
                                       … .WithEnvironment("FeatureFlags__FEATURE__DisableOIDCAuth", disableOidcAuth)  ×6
<each of the six hosts>/appsettings.json   (entry DELETED — no replacement file is created)
```

The dev `"true"` lives beside the Keycloak dev secrets in the AppHost's own Development
`Parameters:` block — a **proven** mechanism in this repo (those values demonstrably reach
the Keycloak container in dev runs). Both values are pinned by Guard B with per-value
rationales.

**Fan-out set (exactly six):** `auth`, `admin`, `families`, `settings-api`, `students-api`,
`assignments-api` — each `.WithEnvironment` placed beside that block's existing fan-outs,
mirroring the login-UI flag's chain placement. The Python portal is **not** a consumer
(verified — it holds no OIDC client and reads neither flag's OIDC variant); nor are the
workers or the migration service (none call `AddAuthAndTenancy`).

**Resolution matrix** (env vars outrank appsettings; the AppHost resolves parameters from
`appsettings.json` → `appsettings.Development.json` → user-secrets → env):

| Launch mode | Source | Value |
| :--- | :--- | :--- |
| `aspire run` (Development — the dev default) | Development-file parameter → fanned env var (wins) | `true` — TestAuth, dev posture unchanged, incl. the Playwright suites' documented "TestAuth is active in dev" assumption |
| `aspire publish` / any Production AppHost run | base parameter → manifest | `false` — OIDC, fail-closed **by construction**; forgetting an override is no longer possible, only deliberately overriding to `true` is |
| standalone `dotnet run` (any environment) | nothing configured | `false` → OIDC; ad-hoc debugging sets one documented env var: `FeatureFlags__FEATURE__DisableOIDCAuth=true` |
| unit/architecture tests | in-memory config injection | per-test (unchanged) |

**What does NOT change:** the registration-time read (`IsFlagEnabled`, `AddAuthAndTenancy`);
the two-kind model and the Config service's ownership of runtime flags; the `FEATURE:` colon
convention; the `auth` service's `requireOidcRelyingParty: true` dev carve-out; the
`FeatureFlagGate`/TenantGate UI components (runtime flags only); the runtime cold-start
fallbacks in Admin/Families/Students `appsettings.json` (documented resilience copies for
when Config is unreachable — a different mechanism from the startup switch, explicitly not
touched).

**Why both committed values are pinned, not just the parameter's existence:** the sibling
flag's guard already pins its default with a rationale ("opt-in, stays OFF"
`AppHostLoginUiFlagWiringArchitectureTests:194`). Here the two values carry the security
argument: base `false` because a manifest must never inherit TestAuth; Development `true`
because adoption day must not change what any developer or the Playwright suites see.

## 3. Why this cannot break the startup switch

The constraint on this flag is *registration* time, not compile time — auth schemes are
registered once at startup and frozen for the process. An AppHost fan-out satisfies that
constraint *by construction*:

1. Aspire sets a child process's environment **before the process starts**;
   `WebApplication.CreateBuilder` loads environment variables by default; so the fan-fed
   value is present at the same instant an appsettings value would be — and outranks it.
2. The identical mechanism already carries the identical class of flag today:
   `DisableKeycloakLoginUi` is read by the same `IsFlagEnabled` helper in the same
   registration path, from an AppHost parameter, in four places, guarded — this design
   generalises a working precedent rather than inventing one.
3. The production path for `DisableOIDCAuth` is **already an env var**
   (`FeatureFlags__FEATURE__DisableOIDCAuth`, per `Program.cs:221-225` and Families'
   appsettings comment) — adoption adds no new mechanism, it only relocates the default.

The one change that *would* break the switch — routing it through the runtime Config
service, whose `IsEnabledAsync` resolves per request, long after scheme registration — is
**explicitly out of scope**. That is the entire reason the two-kind model exists.

## 4. Breaking gotchas & mitigations

| # | Gotcha | Mitigation (adopted) |
| :--- | :--- | :--- |
| 1 | Only AppHost-created resources get the env var: standalone `dotnet run` and ad-hoc debug launches would resolve nothing | **Accepted as designed**: standalone runs get OIDC; ad-hoc debugging uses the one documented env var (`FeatureFlags__FEATURE__DisableOIDCAuth=true`). Facts that made this safe: the Playwright suites require the **full AppHost** (documented at `Students.Tests.Playwright/Tests/ContactAuditSmokeTests.cs:20-23`, `Settings.Tests.Playwright/PlaywrightSettings.cs:18-19`); unit tests inject in-memory config (`AuthTenancyTests.cs:21,45`, `AuthWiringTests.cs:42`, `DevTenantSelectionTests.cs:95`, `PortalHandshakeTests.cs:375`, `FeatureFlagServiceTests.cs:23`) |
| 2 | Silent precedence: an env var outranks appsettings, so a *leftover* per-host entry would be masked, not caught — the failure mode of the current design | Guard A makes any per-host entry (base **or** environment-specific, either shape) a build-breaking failure; the mask becomes a loud red |
| 3 | Incomplete fan-out: six consumers, and `assignments-api` is the easy one to miss | Guard B asserts the **exact** six-resource set *and* a whole-file count of the fan-out literal — the double assertion the login-UI guard already uses (`AppHostLoginUiFlagWiringArchitectureTests:92-101`) |
| 4 | Env-var spelling: a `:` inside the *variable name* (`FeatureFlags__FEATURE:DisableOIDCAuth`, as documented at `configuration.md:1048`) is fragile across shells/platforms | Standardise on `FeatureFlags__FEATURE__DisableOIDCAuth` (the `__`→`:` mapping is already documented inline at `Program.cs:271`); fix the `:1048` row |
| 5 | Fail-open default (F6) | Base `false` + Development `true` (D3): the unsafe state stops being **constructible**, not merely documented |
| 6 | A "no flags in appsettings" guard could false-positive on the runtime cold-start fallbacks that legitimately live in Admin/Families/Students appsettings | Guard A is scoped to the two startup-switch keys only (`FeatureFlagKeys.DisableOIDCAuth`, `FeatureFlagKeys.DisableKeycloakLoginUi`); runtime keys are out of its scan |
| 7 | Shape drift is invisible at read time (`IsFlagEnabled` accepts the nested **and** the bare key — F2) | Guard A scans **both** shapes, so a re-scatter in either form is caught |
| 8 | An Aspire parameter with no default makes `aspire run` prompt and block | The committed base default is mandatory and guarded (mirrors `FlagParameter_IsDeclaredInProgram_AndDefaultsOffInAppSettings:186-196`, which exists because "a `Parameters:` entry is not evidence the parameter exists — the round-A lesson") |
| 9 | Parameter values are strings; `bool.TryParse` silently treats garbage as `false` (→ OIDC) | Document `true`/`false` as the only accepted values; Guard B pins both committed values, so a typo'd default is a test failure, not a runtime surprise |
| 10 | The production override path moves: the **parameter** becomes the override point (user-secrets in dev, manifest at deploy), replacing per-container env vars | Documented behaviour change, folded into the `Setting a flag` rewrite + configuration.md §2 row; with base `false` the manifest is correct *without* any override, so an operator's only reason to touch the parameter is a deliberate TestAuth-style act |
| 11 | Six raw `.WithEnvironment` lines invite a copy-paste typo in the env-var name or a fan-out landing on the wrong resource block | Same posture as the login-UI flag (raw lines, no helper): the block parser + whole-file count in Guard B make a typo'd or misplaced line a failure, and the per-resource assertion names the offending block |
| 12 | The stale docs actively teach the wrong thing (F5) | Implementation round's doc set: rewrite `Setting a flag` (correct again post-adoption, referencing the new parameter), fix `:1048`, update the `:650` consumers table, remove Families' old-model comment (`appsettings.json:10-12`), amend AGENTS.md's sentence to the two-kind model, and update `feature-flag-workflow.md`'s deployment-time section |
| 13 | **New (from adoption):** `aspire publish` run in a **Development** environment would read the Development-file parameter and bake `true` into the manifest | Document in the round's doc set: publish/deploy must run in a non-Development environment (the normal case for any real deployment); the committed base `false` is what any correctly-run publish sees |

## 5. Guards (CI-enforced, all hermetic source scans)

Mirroring `AppHostLoginUiFlagWiringArchitectureTests` (block parser, fail-loud helpers,
non-vacuity check `:199-209`), in a sibling file
`AppHostStartupFlagWiringArchitectureTests.cs`:

- **Guard A — `StartupSwitches_AreAbsentFromEveryPerHostAppsettings`.** Scans every
  `src/**/appsettings*.json` (base *and* environment-specific) for either shape of the two
  startup-switch keys. Runtime cold-start keys are out of scope (gotcha 6). Zero per-host
  copies is the invariant (D1), so base and Development files are covered by one scan.
  *(Simplification over the draft: the draft's separate Guard C — Development-fallback
  presence — merged into A when D1 was reversed.)*
- **Guard B — `StartupFlagParameter_IsDeclaredWithFailClosedDefault_AndFannedOutToExactlyTheSixConsumers`.**
  Asserts the `AddParameter("feature-flag-disable-oidc-auth")` literal in `Program.cs`; the
  committed **base** default `"false"` in `AppHost/appsettings.json` `Parameters:` with the
  fail-closed rationale; the committed **Development** override `"true"` in
  `AppHost/appsettings.Development.json` `Parameters:` with the dev-posture rationale; the
  exact six-resource consumer set; and a whole-file count of the fan-out literal equal to six.
- **Non-vacuity** — the parser must find the known resource blocks (the login-UI guard's
  `Scan_ActuallyFoundTheAppHostResources` pattern), so neither guard can pass over an
  empty parse.

## 6. Rollout — two steps, each shippable alone

**Step 1 (additive, behaviour-identical in dev).** Parameter + six fan-outs, with the two
AppHost-side values. `aspire run` fans `true` (Development file) over per-host base `true` —
resolved value unchanged everywhere in dev; standalone runs still read their old base
entries. *Verification:* build, full test suites, one AppHost smoke (dev still TestAuth).

**Step 2 (subtractive).** Remove the six base entries (D1 adds no replacement files), add
Guards A–B, apply the doc set (gotchas 12–13). *Verification:* the same suites, plus the
guard discrimination probes — re-add one base entry (Guard A fails), delete one fan-out
(Guard B fails), flip the base default to `"true"` (Guard B fails on the fail-closed
rationale), byte-identical restore — and the documented one-liner for ad-hoc standalone
debugging verified once by hand.

Sequencing rationale: step 1 cannot regress anything (it only adds a higher-precedence copy
of today's resolved value), and step 2's deletions are guarded the moment they land. Both
steps land as one Tier 2 round; the split exists so any revert is bisectable.

## 7. Adoption record (grill round, 2026-09-25)

| Q | Question | Settled |
| :--- | :--- | :--- |
| Q1 | Ratify the three-home model (one write-point per kind, deliberate copies stay)? | **Yes** |
| Q2 | Worth a round now, given no production deployment exists (risk latent)? | **Yes — adopt now**; defaults are forever, and every flag added before governance multiplies the scatter |
| Q3 | Default's home: base `true` + deploy override, or base `false` + Development `true`? | **base `false` + Development `true`** — the repo's own Keycloak-dev-secrets precedent; the unsafe state becomes unconstructible |
| Q4 | Who runs hosts standalone — keep six Development fallback files? | **Drop them** (reversal of the draft): Playwright needs the full AppHost, tests inject config, ad-hoc debug uses one env var |
| Q5 | D2 base empty; D4 parameter id? | **Both confirmed** |
| Q6 | Sequencing + mode | **After PR #262 merges; Tier 2** |

Honesty note: the grill **reversed two of the draft's own recommendations** (D1's fallback
files, D3's base-`true` default). Both reversals are recorded above so the spec's history is
traceable: the draft satisfied the constraint, the adopted design removes it.

## 8. Verification plan for the implementation round

- `dotnet build SchoolCollab.slnx` — 0 errors (authoritative pass: parent).
- `dotnet test` on: `ArchitectureTests.Unit` (guards land here; expect 66 → 68),
  `Core.Tests.Unit` (the flag's read path; 114), `Auth.Tests.Unit` (214, flat-shape host),
  `Admin.Tests.Unit` (546, nested-shape host with surviving runtime keys) — 0 failures.
- Guard discrimination probes (worker, then byte-identical restore): re-add a base entry →
  Guard A fails; delete a fan-out → Guard B fails; flip the base default → Guard B fails.
- Doc closure: `configuration.md` (§2 parameter row, §5 two-kind note + consumers table +
  `Setting a flag` rewrite + `:1048` spelling), `AGENTS.md` (two-kind sentence), Families'
  appsettings comment, `feature-flag-workflow.md` (deployment-time section).
- **Residual (owner-gated, not in the round):** a live `aspire run` smoke proving the
  Development-file parameter fans `true` into the topology (the mechanism is proven twice
  over by the sibling flag and the Keycloak dev secrets, but the live path is only
  observable by running the AppHost).

## 9. Out of scope

Flag-administration roles/policies (deferred by the owner, 2026-09-24); runtime flags and
the Config service (already correct); `<FeatureFlagGate>`/TenantGate UI gating; the
Docker-backed realm-import smoke test and the portal link (parked for the next stack,
2026-09-25); folding the `RequireAssignmentApproval` hybrid into a single home (a separate,
deliberate design question).

## Appendix A — evidence index

| Claim | Evidence |
| :--- | :--- |
| Registration-time read | `src/SchoolCollab.Core/Auth/AuthTenancyExtensions.cs:109`, `:272-278` |
| Dual-shape read + `bool.TryParse` fail-closed | `AuthTenancyExtensions.cs:274-277`; bare-key warning `tests/…/AuthWiringTests.cs:40` |
| Six scattered base entries | the table in F3 |
| AppHost deliberately has no parameter | `src/AppHost/SchoolCollab.AppHost/Program.cs:218-225` |
| Sibling flag fully governed + guarded | `Program.cs:158,167`, `:400,437,486,517`; `AppHost/appsettings.json:24-25`; `AppHostLoginUiFlagWiringArchitectureTests.cs:84-102,186-196,199-209` |
| `__`→`:` mapping documented inline | `Program.cs:271` |
| Production path is already an env var | `Program.cs:221-225`; `Families/appsettings.json:10-12` |
| Stale/contradictory docs | `configuration.md` §5 *"Setting a flag"*, `:650`, `:1048-1049`, `:674-675`; `AGENTS.md` "Feature Flags" |
| Portal/workers/migrator are not consumers | portal grep (no flag read); `AddAuthAndTenancy` call sites = the six hosts |
| Tests unaffected (in-memory config) | the test files listed in gotcha 1 |
| Playwright requires the full AppHost; dev TestAuth assumed | `Students.Tests.Playwright/Tests/ContactAuditSmokeTests.cs:20-23`; `Settings.Tests.Playwright/PlaywrightSettings.cs:18-19,30-32` |
| AppHost Development `Parameters:` is the proven home for dev-only values | `AppHost/appsettings.Development.json` (Keycloak dev secrets), verified reaching containers in dev |
| No production deployment exists today | no bicep/azd/Dockerfile/pubxml in the repo; `.github/workflows/` = `ci.yml` only |
| 29 AppHost parameters today (the deployment-time inventory) | `AddParameter` inventory, `Program.cs` |
| Runtime cold-start fallbacks are deliberate | `configuration.md` §5 ("Cold-start fallback in SchoolCollab.Admin/appsettings.json" et al.) |