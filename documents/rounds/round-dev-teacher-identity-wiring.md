# Round — `dev-teacher-identity-wiring`

**Status:** CLOSED — committed as `ed4836d5` (wiring + guards + binding fix) and `356952c8` (docs); **PR #299** (merged 2026-10-04), layer 4 of stack #297. *(Status line recovered 2026-10-06 from an orphaned stash entry — the closure record existed only there.)*
**Tier:** Light (Tier 2) — worker + independent static diff reviewer
**Base:** `stack/9-portal-teacher-surface` (`b29e02ba`); this layer is `stack/10-dev-teacher-identity-wiring`
**Stack:** #297 (layer 4)

## Goal

Make the **dev teacher identity** reachable end-to-end in a live dev run, so the two surfaces
delivered by rounds `teacher-scope-auth` (layer 2) and `portal-teacher-surface` (layer 3) stop
being unreachable under the OIDC bypass.

**The gap, precisely:** `TestAuth:TeacherId` is set **nowhere** — not in
`Assignments.Api/appsettings.json` nor `appsettings.Development.json` — and
`PORTAL_DEV_TEACHER_ID` has **no AppHost fan-out**. So in `aspire run`:

- `assignments-api` runs the dev bypass **claim-less** (`TestAuthHandlerOptions.TeacherId` stays
  `Guid.Empty`), leaving round 1's teacher-scoped read path and the teacher-attributed write path
  unexercisable; and
- the portal's two new client methods are **unconfigured**, so `/teacher/assignments/{id}` renders
  its degraded `MissingConfigurationError` view.

Both halves are the *same identity* and neither is connected. This round connects them to the
identity that already exists.

## Settled rules this round follows (cited, not restated)

The decisions behind this round were already made by the repo — this plan applies them rather than
re-deciding them:

| Settled by | Rule applied here |
|---|---|
| **`AGENTS.md` §Feature Flags (the startup-switch bullet)** | The committed **base default is fail-closed** (a publish must never bake a dev bypass into a manifest), and `appsettings.json` under `Parameters:` is **the canonical record of every deployment-time parameter default** — updated in the same PR that adds one. |
| **`AGENTS.md`** (same bullet) | Fan the value onto the **consumer sites**; the fan-out is the mechanism, and a value must never be scattered across individual service `appsettings.json` files. Guards: `AppHostStartupFlagWiringArchitectureTests`. |
| **`documents/specs/startup-flag-governance.md` §5** | The adopted guard design ("CI-enforced, all hermetic source scans") — this round's new guard follows it. |
| **`documents/specs/startup-flag-governance.md` §8** | The verification plan: build, full test suites, **one AppHost smoke**. |
| **`round-ar-20-identity.md` D4** | The dev identity's source of truth: `DevIdentitySeeder.DevTeacherId` = `…0003` inside `DevSchoolTenantId` = `…0002`; the committed realm file carries **the same two Guids**, so the mapper values match the seeded rows. |
| **`configuration.md` §4 "Real vs TestAuth modes"** | `TestAuth:TeacherId` is already documented, including its claim-less-by-default posture; this round updates that row rather than adding a competing one. |

## Deliverables

| # | Deliverable |
|---|---|
| **D1** | AppHost parameter `dev-teacher-id`: declared with `builder.AddParameter("dev-teacher-id")`; **empty string** base default in `appsettings.json` under `Parameters:`; the real dev value `00000000-0000-0000-0000-000000000003` committed in the AppHost's own `appsettings.Development.json` (the `feature-flag-disable-oidc-auth` pattern: fail-closed base, dev posture in the Development file). |
| **D2** | Fan-out to the API: `.WithEnvironment("TestAuth__TeacherId", devTeacherId)` on the `assignments-api` resource, so the dev bypass emits the `teacher_id` claim. |
| **D3** | Fan-out to the portal: `.WithEnvironment("PORTAL_DEV_TEACHER_ID", devTeacherId)` on the `portals` resource (round 2's D5 seam), so the queue's wire fallback agrees with the claim. |
| **D4** | Guard: assert the AppHost's committed dev value equals `DevIdentitySeeder.DevTeacherId`, mirroring the existing `DevClientSecret_MatchesRealmFileClientSecret_ForSchoolCollabClient` in the same file — the precedent for policing an AppHost default against an external file's value. Rejected alternative: the AppHost referencing `MigrationService` to read the const (same protection, couples orchestration to migrations). |
| **D5** | `documents/configuration.md`: add the §2 parameter row and update §4's existing `TestAuth:TeacherId` row to name the fan-out site; note the `portals` row's new variable. |
| **D6** | Verification per §8: `dotnet build SchoolCollab.slnx`, the **full** test suites, and **one** `aspire run` smoke. |

## Acceptance criteria

1. `dotnet build SchoolCollab.slnx` → 0 errors; `dotnet test` → 0 failures.
2. The new guard **fails** when the AppHost's dev value is drifted away from
   `DevIdentitySeeder.DevTeacherId` (discrimination probe: change one digit, observe the red, restore).
3. **Fail-closed confirmed:** with no Development file in play, the parameter resolves to the empty
   base default — no dev identity is baked into a published manifest.
4. `configuration.md` records the parameter and every fan-out site.
5. **One `aspire run` smoke** shows `/teacher` → `/teacher/assignments/{id}` rendering **live data
   rather than the degraded view**. Per Q4 the dev tenant is *selected*, not configured: the smoke
   must state which tenant was in scope, so an empty queue is provably "no submissions" and not
   "no tenant". If the AppHost cannot be stood up in this environment, the round **records that
   honestly** instead of claiming a green behavioural result.
6. The round is **wiring only**: no change to `TestAuthHandler` semantics and no change to any portal
   Python file.

## Expected files

| File | Change |
|---|---|
| `src/AppHost/SchoolCollab.AppHost/Program.cs` | D1 parameter + D2/D3 fan-outs |
| `src/AppHost/SchoolCollab.AppHost/appsettings.json` | D1 base default (empty) — the canonical record |
| `src/AppHost/SchoolCollab.AppHost/appsettings.Development.json` | D1 dev value |
| `tests/SchoolCollab.ArchitectureTests.Unit/AppHostDevParameterDefaultsArchitectureTests.cs` | D4 guard (same file as the precedent it mirrors) |
| `documents/configuration.md` | D5 rows |

**Added by the review's P1 rework** (widened from the table above, deliberately — the P1 is a precondition of AC3):

| File | Change |
|---|---|
| `src/Assignments/SchoolCollab.Assignments.Api/Auth/AssignmentDevTeacherIdentity.cs` | The blank-tolerant read (see Review P1) |
| `tests/SchoolCollab.Assignments.Api.Tests.Unit/AssignmentDevTeacherIdentityTests.cs` | The four cases pinning it |

## Out of scope

- **Any portal Python change** — round 2 delivered the reader; this round only supplies its env var.
- **Role assignment (D11)** and **the portal's real-auth gap** — both remain prerequisites that must
  land before `FEATURE:DisableOIDCAuth` flips (recorded in
  `documents/specs/teachers-ward-portal-prefab-plan.md`).
- **Pinning a dev tenant default** — `TestAuthHandlerOptions.TenantId` staying `Guid.Empty` is a
  deliberate posture ("the per-tenant UI is hidden until a real tenant is selected"); the dev tenant
  is chosen via the admin switcher in TestAuth mode and supplied by the realm file in real auth.
- Changing `TestAuthHandler` or widening the dev bypass.

## Worker Report

**Worker `77088e87`** (implementation) + the resumed run `7465487f` (P1 rework).

| # | Result |
|---|---|
| **D1** | `var devTeacherId = builder.AddParameter("dev-teacher-id");` (`Program.cs:191`); base `"dev-teacher-id": ""` in `appsettings.json:27`, `"…0003"` in `appsettings.Development.json:10` — the `feature-flag-disable-oidc-auth` two-value pattern. |
| **D2** | `.WithEnvironment("TestAuth__TeacherId", devTeacherId)` on `assignments-api` (`Program.cs:355`). |
| **D3** | `.WithEnvironment("PORTAL_DEV_TEACHER_ID", devTeacherId)` on the `portals` Uvicorn app (`Program.cs:568`). |
| **D4** | **A guard pair** in `AppHostDevParameterDefaultsArchitectureTests.cs`: `DevTeacherIdDefault_MatchesDevIdentitySeederDevTeacherId` (`:124`) and `DevTeacherIdDefault_BaseValueIsEmpty_FailClosed` (`:139`). The parity guard reads `DevIdentitySeeder.cs` hermetically (regex over the source, no AppHost→MigrationService reference) and **throws if the declaration is absent** — so it cannot pass vacuously. Probes: dev value drifted → red; base value non-empty → red; both restored → green. |
| **D5** | `configuration.md`: new §2 row; §4's existing `TestAuth:TeacherId` row updated **in place** (no competing row); the `portals` row notes the new variable. |
| **Rework** | `AssignmentDevTeacherIdentity` now reads the raw string and treats null/whitespace as `Guid.Empty`; the class doc-comment is unedited because it is now simply true. Four regression cases (absent / empty / whitespace / valid) resolve the real `IOptionsMonitor` for the `TestAuth` scheme. **Discriminator:** restoring the old `GetValue` line → **138 passed / 2 failed**, both blanks failing with `InvalidOperationException: Failed to convert configuration value '' / '   ' at 'TestAuth:TeacherId'` while absent and valid stayed green (so the failure is attributable to the blank conversion, not the harness); new code → **140 / 0**. |

**Evidence:** `dotnet build SchoolCollab.slnx` **0 errors** · `Assignments.Api.Tests.Unit` **140 / 0** · `ArchitectureTests.Unit` **85 / 0** · no `.py`, no `src/SchoolCollab.Core/Auth/`, `TestAuthHandler` untouched. Three `dotnet test` failures in `SchoolCollab.Settings.Tests.Integration` → `CodedValueAIServiceLiveTests` (live OpenRouter **HTTP 400**) are pre-existing/environmental — proven by the worker re-running that project against the pristine `git show HEAD:…/appsettings.json` with identical failures, and independently accepted by the reviewer.

## Review

**Static diff reviewer `5a774f2d` (`kimi-k2.7-code`) — Verdict: `P1`, "round can be accepted: no — bounded rework first".**

**P1 — `AssignmentDevTeacherIdentity.cs:36`: the fail-closed base default was a crash, not an absence.** `GetValue<Guid>(key, Guid.Empty)` applies its fallback only when the key is **absent**; a **present empty string** — exactly what the committed base fan-out `TestAuth__TeacherId=""` produces — is run through type conversion and throws `InvalidOperationException: Failed to convert configuration value '' … to type 'System.Guid'`. So AC3's "a publish outside Development yields **no dev identity**" was in fact "…yields a startup/options-time crash", and the class's own doc-comment ("an absent or **empty** value stays claim-less") was false. The reviewer ruled it **in scope now** on three grounds: the binding is the class that consumes this round's new fan-out; its doc-comment already promises the behaviour; and the fix does not alter `TestAuthHandler` semantics (AC6). Fixed in the rework above.

**Everything else clean:** guard pair hermetic (`File.ReadAllText` + regex only, no process launch), **anti-vacuous** (throws if the `Guid.Parse` literal cannot be found), and discriminating; fan-out names verified exactly against both consumers (`AssignmentDevTeacherIdentity.TeacherIdConfigKey` and `Portals/app.py:76`), with `devTeacherId` referenced at those two sites only; the helper refactor (`FindAppHostDir` → `FindRepoRoot` + `FindRepoFile`) behavior-identical with no guard weakened; scope clean; best practices honoured (`Should().Be(..., "…")` with real failure messages, not bare boolean assertions).

**Process note worth keeping:** this P1 was **self-flagged by the worker as an open risk** rather than silently decided, then independently escalated by the reviewer. A solo pass would have shipped a fail-closed default that crashes — the opposite of what it was meant to guarantee.

## Acceptance

**Verdict: CLOSED** (commit/push still gated on an explicit instruction).

**Tier 2 (Light).** Parent authored the plan; worker `77088e87` implemented; reviewer `5a774f2d` returned a **P1**; boundework rework `7465487f`; parent verified and ran the smoke. The rework was **exactly the fix the reviewer prescribed**, in the file and test it named, so the P1 is closed on the reviewer's own terms.

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| `Assignments.Api.Tests.Unit` | **140 / 0** |
| `ArchitectureTests.Unit` | **85 / 0** (83 + the 2 new guards) |
| `dotnet test` (full) | 3441 / 3 — the 3 are `CodedValueAIServiceLiveTests` (live OpenRouter HTTP 400), **pre-existing/environmental** |
| Scope | no `.py`, no `src/SchoolCollab.Core/Auth/`, `TestAuthHandler` untouched |

**Acceptance criteria:** **AC1** met (above). **AC2** met (probes: dev-value drift → red; non-empty base → red; and the rework's discriminator — old `GetValue` → **138/2** with `InvalidOperationException: Failed to convert configuration value '' / '   '`, new code → **140/0**). **AC3** met **only after the rework** — see Review P1. **AC4** met (`configuration.md` §2 + §4 in place). **AC5** met — evidence below. **AC6** met.

### AC5 — the live smoke (parent-run, on the post-rework tree)

`dotnet run --project src/AppHost/SchoolCollab.AppHost` with `DOTNET_ENVIRONMENT=Development`; portal at `https://localhost:53288` (uvicorn, Aspire self-signed cert).

| Probe | Result |
|---|---|
| `GET /health` | 200 — `discovery_error: null`, `api_base_url: http://localhost:5199` (service discovery resolved) |
| `GET /teacher` | 200 — renders *"Teacher portal — assignments … Live data from assignments-api"*, badges `HTTP 200` / `0 assignments`, and the **explicit** empty state *"No assignments came back for this teacher. An empty result is a succ…"*. **Zero** degradation markers. |
| `GET /teacher/assignments/00000000-0000-0000-0000-0000000000ff` | 200 with an **honest** degraded view naming the true cause: *"assignments-api at http://localhost:5199 returned an unusable response: HTTP 404 for /…/submissions/review-queue"* — and **`PORTAL_DEV_TEACHER_ID` x0, `MissingConfiguration` x0**. `/health`'s `last_fetch` records the same call. |

**The discriminating fact is the absence, not the presence:** the queue page degraded for a *genuine* 404 while never mentioning its configuration key. That proves `resolve_dev_teacher_id()` **succeeded inside the live process** — D3's fan-out is real, not merely wired — and that the portal issued the actual `/submissions/review-queue` call round 2 built. Round 2's config-degraded surface is gone; what remains is honest degradation for a real upstream error.

**Limitations recorded honestly rather than papered over:**

1. **The dev tenant could not be selected from this shell** — `docker exec cache-… redis-cli SET dev:tenant-selection …` returned *"Connection reset by peer"* (the Aspire cache is not reachable that way). So the list legitimately showed **0 assignments**, and the empty state — not the degraded view — was correct. This is the Q4 prerequisite working as designed: the tenant is *selected*, not configured, and an empty queue is indistinguishable from no-data **only** if the tenant is unset — which is why the smoke states the tenant was unset.
2. **D2 (the API-side fan-out) is proven statically, not observed live** — the guard pins the AppHost's dev value and both sites consume the one parameter, but the API's emitted `teacher_id` claim was not inspected in-process.

### Carried, not lost

1. **The dev tenant switcher prerequisite** for a dev wanting *live* portal data (see above) — a dev-run note, not a defect.
2. **D11 role assignment** and **the portal's real-auth gap** — still must land before `FEATURE:DisableOIDCAuth` flips (recorded in `documents/specs/teachers-ward-portal-prefab-plan.md`).
3. A fresh reviewer pass over the **rework** (2 files) is available if belt-and-braces is wanted; the fix is verbatim the reviewer's prescription.
