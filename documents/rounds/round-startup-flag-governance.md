# Round: startup-flag-governance

Provider: pi, Tier 2 (models: deepseek-v4.1-flash worker, kimi-k2.7-code reviewer) — Option A
Round base: 309440e7abefee43c547dbe195d6d46c8923c3f7 (branch `stack/22-startup-flag-governance`, stacked on `stack/21-apphost-endpoint-pinning` = PR #262 head — **owner override of the Q6 "after #262 merges" sequencing, 2026-09-25: "do not merge yet, use stack pr with new branch"**; delivery = stacked PR, base `stack/21-apphost-endpoint-pinning`)
Dirty tree at round start (`git diff --name-only`): `src/SchoolCollab.Auth/appsettings.json` (owner's local experiment: `FEATURE:DisableOIDCAuth` flipped `"true"`→`"false"`; **subsumed** by this round — the plan deletes that block entirely; the round diff is isolated against the base SHA)
Plan source: `documents/specs/startup-flag-governance.md` (ADOPTED 2026-09-25, grill Q1–Q6)

## Plan

### Goal

`FEATURE:DisableOIDCAuth` becomes an AppHost parameter fanned out to its six consumers —
the exact mechanism its sibling startup switch `FEATURE:DisableKeycloakLoginUi` already
uses — with **zero per-host copies**, a **fail-closed committed base default**, and the dev
posture carried by the AppHost's own Development file. The registration-time read
(`AuthTenancyExtensions.IsFlagEnabled`, `:272-278`) is untouched. Adopted design + evidence:
`documents/specs/startup-flag-governance.md` (read it on ambiguity — it is authoritative).

### Scope

IN: the 13 files below. OUT: runtime flags and the Config service (already correct);
`AuthTenancyExtensions.cs` (the read stays exactly where it is); the
`RequireAssignmentApproval` hybrid; any fan-out to auth-portal, the workers, or the
migrator (verified non-consumers); any behavioral change beyond the flag's source.

### Edits (exact)

**A. The parameter and its two committed values**

1. `src/AppHost/SchoolCollab.AppHost/Program.cs` — beside the existing flag parameters
   (`requireAssignmentApproval`, `disableKeycloakLoginUi`, ~:158-167), add:

   ```csharp
   // Startup auth-mode switch (documents/specs/startup-flag-governance.md). Read at
   // REGISTRATION time by AddAuthAndTenancy (IsFlagEnabled) — auth schemes are fixed at
   // startup, so it is NOT a Config-service flag. Committed base default "false" is
   // fail-closed: a publish must never bake TestAuth into a manifest. The dev "true"
   // lives in appsettings.Development.json Parameters, beside the Keycloak dev secrets —
   // this repo's proven home for dev-only values. Fanned below to exactly the six hosts
   // that call AddAuthAndTenancy.
   var disableOidcAuth = builder.AddParameter("feature-flag-disable-oidc-auth");
   ```

   Then add `.WithEnvironment("FeatureFlags__FEATURE__DisableOIDCAuth", disableOidcAuth)`
   to the **six** resource chains — `auth`, `admin`, `families`, `settings-api`,
   `students-api`, `assignments-api` — each beside that block's existing `WithEnvironment`
   calls, mirroring the login-UI flag's chain placement.

2. `src/AppHost/SchoolCollab.AppHost/appsettings.json` — add
   `"feature-flag-disable-oidc-auth": "false",` to `Parameters` (after
   `feature-flag-disable-keycloak-login-ui`, ~:25).

3. `src/AppHost/SchoolCollab.AppHost/appsettings.Development.json` — add
   `"feature-flag-disable-oidc-auth": "true"` to `Parameters` (beside the `keycloak-*`
   dev entries).

**B. Remove the six scattered entries (zero per-host copies; delete the whole block when nothing else remains)**

4. `src/SchoolCollab.Auth/appsettings.json` — delete the entire `"FeatureFlags": { … }`
   block (holds only the OIDC flag — including the owner's current local `"false"` flip;
   delete regardless).
5. `src/SchoolCollab.Admin/appsettings.json` — delete ONLY the `"DisableOIDCAuth": "true",`
   line inside the nested `FEATURE` block; KEEP the five runtime cold-start keys
   (`EnableCodedValuesAiChat`, `EnableGradeLevelSetupOnEnrollDialog`, `EnableEnrollmentValidation`,
   `EnableActivityGroups`, `RequireAssignmentApproval`) — they are a different, documented
   mechanism and are NOT in this round's scope.
6. `src/SchoolCollab.Families/appsettings.json` — delete the `"DisableOIDCAuth": "true",`
   line AND the stale 3-line comment above it (`:10-12`, documents the old per-host model);
   KEEP `EnableDeepLinks`.
7. `src/Settings/SchoolCollab.Settings.Api/appsettings.json` — delete the whole
   `FeatureFlags` block (flat shape, holds only the OIDC flag).
8. `src/Students/SchoolCollab.Students.Api/appsettings.json` — delete only the
   `DisableOIDCAuth` line; KEEP `EnableEnrollmentValidation` (and leave the unrelated
   `UseLocalCodedValueProjection` section untouched).
9. `src/Assignments/SchoolCollab.Assignments.Api/appsettings.json` — delete the whole
   `FeatureFlags` block.

**C. The guards (NEW file — mirror, do not reinvent)**

10. `tests/SchoolCollab.ArchitectureTests.Unit/AppHostStartupFlagWiringArchitectureTests.cs`
    — adapt the proven structure of `AppHostLoginUiFlagWiringArchitectureTests.cs`
    (resource-block parser, `ReadParameters`, fail-loud helpers, non-vacuity pattern `:199-209`):

    - **Guard A — `StartupSwitches_AreAbsentFromEveryPerHostAppsettings`.** Scan every
      `src/**/appsettings*.json` (base AND environment-specific; exclude bin/obj) for the
      two startup-switch keys — `FeatureFlagKeys.DisableOIDCAuth` and
      `FeatureFlagKeys.DisableKeycloakLoginUi` — in **both** shapes (nested
      `FeatureFlags:FEATURE:<Name>` and flat `"FEATURE:<Name>"`). Assert absent everywhere.
      Runtime `Enable*` keys are out of scope. Failure message names the file and points at
      the AppHost parameter. Fail loud if the scan locates no appsettings files.
    - **Guard B — `StartupFlagParameter_IsDeclaredWithFailClosedDefault_AndFannedOutToExactlyTheSixConsumers`.**
      Assert: `Program.cs` contains `AddParameter("feature-flag-disable-oidc-auth")`;
      base `AppHost/appsettings.json` `Parameters` has the value `"false"` (fail-closed
      rationale in the message); `AppHost/appsettings.Development.json` `Parameters` has
      `"true"` (dev-posture rationale); the fan-out literal
      `WithEnvironment("FeatureFlags__FEATURE__DisableOIDCAuth", disableOidcAuth)` appears
      in **exactly** the six resource blocks {auth, admin, families, settings-api,
      students-api, assignments-api} (block-scoped ordered-set assertion, like
      `FlagFanOut_ReachesExactlyTheFourLoginUiConsumers`); AND a whole-file count of the
      literal == 6, so a fan-out outside a parsed block cannot slip through.
    - **Non-vacuity** — the block parser must find the known resource blocks (mirror
      `Scan_ActuallyFoundTheAppHostResources`).
    - Raw-string care: inside `"""…"""` literals a `""` is TWO quote characters — prefer
      single `"` (the CS8998/regex trap hit this session).

**D. Docs**

11. `documents/configuration.md` —
    - §2 parameter table (~:134): add the `feature-flag-disable-oidc-auth` row (Aspire
      parameter; base default `false`, Development file carries `true`; "startup
      auth-mode switch — registration-time read, registration-secure: not a Config-service
      flag") with the spec link.
    - §5: update the two-kind bullet — `DisableOIDCAuth` is now AppHost parameter +
      fan-out like its sibling (no longer "each consumer carries it in its own
      appsettings.json"); update the consumers row (~:650) accordingly (six hosts, fanned;
      the auth-service dev carve-out note stays).
    - §5 "Setting a flag" (~:662-670): rewrite — dev override = user-secrets on the
      **AppHost** project (`Parameters:feature-flag-disable-oidc-auth`); ad-hoc standalone
      host debugging = the env var `FeatureFlags__FEATURE__DisableOIDCAuth=true`; fix the
      claim that the file "is the canonical record of every flag's default value" to
      "…of deployment-time parameter defaults".
    - Env-var spelling table (~:1048): fix to `FeatureFlags__FEATURE__DisableOIDCAuth`
      (currently the fragile single-underscore + literal-colon form).
    - Add the publish caveat beside the parameter row: publish/deploy must run in a
      non-Development environment or the Development-file value gets baked.
12. `AGENTS.md` — amend the "Feature Flags" paragraph to the two-kind model: runtime flags
    are owned by the central Config service (settings-api + `/config-flags`), deployment-time
    startup switches live in the AppHost `Parameters:` block fanned via `WithEnvironment`;
    link `documents/specs/startup-flag-governance.md` and
    `documents/solution/feature-flag-workflow.md`. Keep the §2 mapping duty and the
    "do not scatter" rule.
13. `documents/solution/feature-flag-workflow.md` — "Deployment-time flags (rare)"
    section: replace the per-consumer-appsettings steps with the AppHost-parameter pattern
    (AddParameter + base `false` / Development `true` + fan-out + the two guards), with the
    spec link.

### Expected files (closure — 13)

1-3 as in A; 4-9 as in B; 10 as in C; 11-13 as in D. Parent-owned, NOT worker scope:
`documents/specs/startup-flag-governance.md`, this round doc, `diffs-startup-flag-governance.patch`.

### Build, tests, probes

- `dotnet build SchoolCollab.slnx` (from repo root — the solution is `.slnx`; 0 errors required).
- `dotnet test` (NO extra flags) on `tests/SchoolCollab.ArchitectureTests.Unit` (expect
  66 → ~70, all green), `tests/SchoolCollab.Core.Tests.Unit` (114), `tests/SchoolCollab.Auth.Tests.Unit`
  (214), `tests/SchoolCollab.Admin.Tests.Unit` (546).
- Test-output gate (hard rule): exactly one result-read pipeline per run
  (`dotnet test tests/<X> 2>&1 | grep -E "total:|failed:" | head -40` or the PowerShell
  equivalent), ≤2 attempts, redirect to a file once and read the tail if truncated; never
  write debug log files into the repo.
- **Guard discrimination probes (mandatory; byte-identical restore, md5 proof):**
  1. re-add `"FEATURE:DisableOIDCAuth": "true"` to `src/SchoolCollab.Auth/appsettings.json`
     → Guard A must FAIL;
  2. delete one fan-out line from `Program.cs` → Guard B must FAIL;
  3. flip the base default to `"true"` in `AppHost/appsettings.json` → Guard B must FAIL;
  then restore all three byte-identically (verify with md5 hashes or an empty `git diff`
  on those files) and re-run the suite green. A guard that cannot fail is not a guard.

### Constraints

- Implement exactly the plan; never edit this round doc, the spec, or anything under
  `documents/rounds/`; no commits, no branch switches, no pushes.
- Repo-scoped searches only (grep under `src/`, `tests/`, `documents/`); never `find /`.
- On ambiguity read the spec (`documents/specs/startup-flag-governance.md`); on a
  blocking decision, contact the supervisor rather than guessing.

### Acceptance criteria (Tier 2 — reviewer verifies against these)

- AC1: build 0 errors; AC2: all four suites green with unchanged-or-higher counts.
- AC3: zero per-host `appsettings*.json` carries either startup-switch key in either
  shape (Guard A enforces; runtime cold-start keys survive untouched).
- AC4: the parameter is declared; base default `false`; Development override `true`;
  exactly six fan-outs with the exact spelling `FeatureFlags__FEATURE__DisableOIDCAuth`
  on the six named resources (Guard B enforces).
- AC5: all three discrimination probes failed as designed, then restored byte-identically.
- AC6: the four doc targets updated (configuration.md ×4 targets, AGENTS.md sentence,
  feature-flag-workflow.md steps, Families' stale comment gone).
- AC7: no overwrite of pre-existing code outside scope (esp. Admin/Students appsettings
  runtime keys and Program.cs's unrelated regions).

## Worker Report

Worker run `5fa9119a` (`deepseek-v4.1-flash`, 2026-09-25) — completed, no escalation, 30-min cap respected.

**Changed files (13 — matches the plan closure):**
- `src/AppHost/SchoolCollab.AppHost/Program.cs` — parameter + 6 fan-outs (+ 2 comments made false by the change, see deviations)
- `src/AppHost/SchoolCollab.AppHost/appsettings.json` (base `"false"`), `appsettings.Development.json` (dev `"true"`)
- the six per-host `appsettings.json` — scattered copies removed (auth block incl. the owner's local `"false"` flip, deleted as instructed)
- `tests/SchoolCollab.ArchitectureTests.Unit/AppHostStartupFlagWiringArchitectureTests.cs` — NEW (Guard A, Guard B, non-vacuity)
- `documents/configuration.md` (6 targets), `AGENTS.md`, `documents/solution/feature-flag-workflow.md`

**Build:** 0 errors (`dotnet build SchoolCollab.slnx`, verified twice — after the guard landed and on the final restored state).
**Tests:** ArchitectureTests.Unit **69/0** (66 → +3 guards) · Core.Tests.Unit **114/0** · Auth.Tests.Unit **214/0** · Admin.Tests.Unit **546/0**.

**Discrimination probes — all three failed as designed, then restored byte-identically (md5 unchanged, suite green again at 69/0):**
1. key re-added to the auth appsettings → Guard A failed (`src\SchoolCollab.Auth\appsettings.json → FeatureFlags:"FEATURE:DisableOIDCAuth"`);
2. families fan-out deleted → Guard B failed (`it misses {"families"}`);
3. base default flipped to `"true"` → Guard B failed on the fail-closed default.

**Deviations from plan (4, all reported in-file/in-scope):**
1. Two extra comment edits inside `Program.cs`: the model comment (~:233, said the flag "is read by each consumer from its own appsettings.json") and the Families block comment (~:520, said "no AppHost flag param needed") — both became false the moment the fan-out landed; leaving them would ship a documented lie in the same file.
2. Guard A covers **four** shapes, not two (nested, flat colon key, root-level joined key, and the bare `FEATURE:<Name>` key — the last is the shape `IsFlagEnabled` falls back to via `configuration[featureKey]`, so a scatter written that way would otherwise pass); scan also excludes `.venv`/`node_modules`.
3. The Families stale comment block was 4 lines (`:9-12`), not 3 — all four removed.
4. (Recorded, not a deviation: the pre-round owner flip was present and deleted as instructed.)

**Out-of-round observation (not touched):** `src/SchoolCollab.Auth/Options/AuthServiceOptions.cs` still names `postLogoutRedirectUris` in a doc comment describing Keycloak's client-side allowlist; that field was replaced by the client attribute in PR #262's realm fix, so the prose is now historically wrong but conceptually accurate. Not in this round's closure.

## Review

Diff-review pass: run `75c4da02` (`kimi-k2.7-code`, static — never built/tested), 2026-09-25, against `diffs-startup-flag-governance.patch` + § Plan.

REVIEW
Verdict: PASS
P1: none
P2: src/Assignments/SchoolCollab.Assignments.Api/appsettings.json:9 — file lost its final newline; violates repo `.editorconfig insert_final_newline = true`
Best-practices: no overwrites; skills honored; readable

**Adjudication (parent):** no P1 → the Tier 2 rework iteration (bound ≤1) was never spent. The single P2 was fixed by the parent post-review (see Acceptance). The worker's four reported deviations were adjudicated **legitimate completion of the plan's intent, not scope creep**: the two Program.cs comment edits removed statements the fan-out had rendered false (the reviewer's no-overwrite check passed); Guard A's fourth shape (the bare `FEATURE:<Name>` key) closes the exact fallback `IsFlagEnabled` reads (`configuration[featureKey]`, `AuthTenancyExtensions.cs:272-278`), strengthening the round's own invariant; the `.venv`/`node_modules` scan exclusions contain no host configs; the Families comment was 4 lines.

## Acceptance

Parent-transcribed verdict (Tier 2): **CLOSED — 2026-09-25. Zero P1s, zero outstanding P2s.**

**Criteria (from § Plan):**
- **AC1** build 0 errors — ✓ parent `dotnet build SchoolCollab.slnx` (re-run after the P2 fix).
- **AC2** suites green — ✓ parent pass: ArchitectureTests.Unit **69/0** (+3 guards), Core **114/0**, Auth **214/0**, Admin **546/0**; parent numbers identical to the worker's report.
- **AC3** zero per-host copies — ✓ Guard A (in the 69/0) + parent grep spot-check: no `DisableOIDCAuth`/`DisableKeycloakLoginUi` in any per-host `appsettings*.json`; the runtime cold-start keys survived in Admin/Students/Families.
- **AC4** parameter + values + fan-out — ✓ Guard B: `AddParameter("feature-flag-disable-oidc-auth")`; base `"false"` (`appsettings.json:26`); Development `"true"` (`appsettings.Development.json:9`); fan-out literal count **6** on exactly {auth, admin, families, settings-api, students-api, assignments-api}.
- **AC5** discrimination probes — ✓ all three failed as designed (Guard A on the re-added key; Guard B `misses {"families"}`; Guard B on the flipped base default), restored byte-identically (md5 unchanged), suite green again.
- **AC6** doc targets — ✓ reviewer-verified against the plan (configuration.md §2/§5/"Setting a flag"/env-var spelling/publish caveat; AGENTS.md two-kind sentence; feature-flag-workflow.md deployment-time section; Families' stale comment gone).
- **AC7** no overwrites — ✓ reviewer: "no overwrites; skills honored; readable".

**P2 disposition:** the reviewer caught ONE missing final newline (Assignments.Api). The parent's same-class sweep of all eight touched `appsettings*` files found **two more** the review missed (Admin, Students.Api) — all three fixed by appending the file's own EOL (byte-level, no content change); build re-run **0 errors**, ArchitectureTests re-run **69/0**. The frozen patch remains the reviewed artifact; the fixes ride in the final commit.

**Residuals (recorded, not blocking):**
1. Out-of-round observation: `src/SchoolCollab.Auth/Options/AuthServiceOptions.cs` doc comment still names `postLogoutRedirectUris` (the client field PR #262 replaced with the `post.logout.redirect.uris` attribute) — prose historically wrong, conceptually accurate. Owner backlog call; outside this round's closure.
2. Live `aspire run` smoke of the Development-file parameter → fan-out → TestAuth is **owner-gated** (the mechanism is proven twice over — the sibling login-UI flag and the Keycloak dev secrets — but the live path is only observable by running the AppHost).
3. Delivery is a stacked PR on `stack/21-apphost-endpoint-pinning` (PR #262 head, unmerged by owner instruction 2026-09-25). After #262 squash-merges, this PR goes `DIRTY` (squash-twin merge base) and needs the verified `rebase --onto origin/main` restack (ar-15/#239 procedure) before its merge.