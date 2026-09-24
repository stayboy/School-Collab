Provider: pi, Tier 2 light (models: deepseek-v4.1-flash worker, kimi-k2.7-code static diff reviewer) · base 670fff17 · branch stack/20-flag-admin-gate · owner ruling 2026-09-24: *skip the role policy for now; proper roles and policies are a later discussion*

## Plan

*(Tier 2 — the parent authors the plan and transcribes the acceptance; one worker run plus one static diff reviewer; ≤1 rework iteration.)*

### Goal

Remove the `flag_admin` authorization policy. It is **unsatisfiable as written** and therefore enforces nothing while silently disabling flag administration:

- The policy requires a claim **named** `role` with value `flag_admin` (`Program.cs:47-48`). The realm issues no `flag_admin` role (only `user-admin` and `platform-admin`), and the realm's role mapper emits `roles`, which the OIDC handler's default inbound mapping renames to `ClaimTypes.Role` — so no realm-issued token can carry a literal `role` claim.
- Consequence, both directions: under OIDC **every flag write 403s** (an accidental deny-all that breaks the legitimate admin path), and in dev the policy is **skipped** (`requireFlagAdmin = oidcEnabled`, `ConfigEndpoints.cs:20`), so any TestAuth-authenticated caller may write.

**Accepted trade-off (owner ruling, explicit):** after the removal, in OIDC mode any authenticated **bearer** user may write global feature flags. This matches the posture of the other eight Settings write groups, which require only `RequireAuthenticatedUser().AddAuthenticationSchemes(Bearer)`; audit attribution (`ClaimsPrincipalActorAccessor`) is unchanged.

**Deferred, not resolved here:** *proper* roles and policies for flag administration are a **later discussion** (owner, 2026-09-24). This round does not introduce a replacement role, does not touch the realm, and must not leave a half-built gate behind.

### Scope

**IN** — the six files below, and nothing else.

| # | Path | N/M | Why |
|---|---|---|---|
| 1 | `src/Settings/SchoolCollab.Settings.Api/Program.cs` | M | Delete the `AddAuthorization` registration of the `flag_admin` policy (`:44-49`) and its comment. Nothing else in the builder changes. |
| 2 | `src/Settings/SchoolCollab.Settings.Api/ConfigEndpoints.cs` | M | Drop the now-unused `requireFlagAdmin` local (`:20`), stop passing it to both mappers (`:33-34`), and rewrite the two doc-comment/comment fragments that describe skipping a role policy in dev — they must no longer mention a role policy at all. Keep the group's `RequireAuthenticatedUser().AddAuthenticationSchemes(Bearer)` opt-in (`:25-30`) **exactly as-is**: it is what keeps forwarded bearer calls working. |
| 3 | `src/Settings/SchoolCollab.Settings.Api/Endpoints/ConfigFlagRoutes.cs` | M | Drop the `bool requireFlagAdmin` parameter (`:12`), the **seven** `.ApplyAdminPolicy(requireFlagAdmin)` calls (`:33, 67, 82, 89, 95, 106, 112`), and the whole `AdminPolicyExtensions` class with its doc-comment (`:123-132`). The write routes keep no route-level authorization call — they are covered by the `/api/config` group's opt-in, exactly like the group's other routes. |
| 4 | `src/Settings/SchoolCollab.Settings.Api/Endpoints/ConfigTenantFlagOverrideRoutes.cs` | M | Same removal: the `bool requireFlagAdmin` parameter (`:12`) and the **two** `.ApplyAdminPolicy(requireFlagAdmin)` calls (`:40, 52`). *(This file was missing from the parent's first draft of the change set — found by a repo-wide grep of `requireFlagAdmin`/`ApplyAdminPolicy` before the worker was dispatched.)* |
| 5 | `tests/SchoolCollab.ArchitectureTests.Unit/BearerForwardingWiringArchitectureTests.cs` | M | Replace the ar-24 `flag_admin` assertion (`:83-87`, which pins "Program.cs must contain `flag_admin`") with assertions that pin the **new** shape: `Program.cs` does **not** contain `flag_admin`; each of the two route files does **not** contain `ApplyAdminPolicy`; and `ConfigEndpoints.cs` still contains exactly **one** standard opt-in (`OptIn`), i.e. the group-level bearer opt-in that now carries those routes. Keep the test's non-vacuity (it must fail if someone re-adds a route-level gate) and update its name/comment so it no longer describes the removed policy. |
| 6 | `documents/configuration.md` | M | The Settings bullet (`:550-551`) currently reads "and the **`flag_admin` policy** (`Program.cs`) which now adds `AddAuthenticationSchemes(Bearer)` (role claim kept)". Restate it: the `/api/config` group (including the flag write and tenant-override routes) opts in via the group-level policy in `ConfigEndpoints.cs`; there is **no** `flag_admin` role policy. Add one line recording the deferred item and the accepted trade-off above. |
| 7 | `documents/solution/central-config-service-plan.md` | M | **Added by the parent after the static review** (the reviewer's residue sweep found it, and the plan's own closure rule requires the row before the file may be touched): item 7 of that durable plan record still states "**`flag_admin` OIDC role** gates write endpoints". `documents/solution/` is this repo's **durable technical memory** (not the ephemeral `documents/rounds/`), so a stale clause there is exactly the "trace *why*" gap `AGENTS.md` exists to prevent. Mark the clause **superseded** with the reason (no `flag_admin` role is issued; the claim-name mismatch) and the current posture (authenticated-bearer-only via the group opt-in; roles/policies deferred). Do **not** rewrite the historical record wholesale — mark, do not erase. |

**OUT — explicitly**

- No replacement role, no realm change, no `platform-admin`/`user-admin` grant, no new policy.
- No change to the read routes, the audit routes, `ConfigResolveRoutes`, or the group-level bearer opt-in.
- No change to any other Settings endpoint group, any other host, or the Admin UI.
- No new file (so no `.slnx` `<File>` row is needed), no migration, no MassTransit contract, no CPM change.

### Acceptance criteria (verdicts stay open until the parent accepts)

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| F-A | No `flag_admin` policy or reference remains in `src/` — `git grep -n 'flag_admin\|ApplyAdminPolicy\|AdminPolicyExtensions' -- src/` returns **nothing** | — | repo grep (parent-verified) |
| F-B | The flag write and tenant-override routes are still **authenticated + bearer-eligible** through the `/api/config` group opt-in; the group policy is unchanged and still applied when OIDC is on | — | `BearerForwardingWiringArchitectureTests` (new assertions); ArchitectureTests.Unit standalone |
| F-C | Reads, audit and resolve routes are untouched; `ConfigResolveRoutes.cs` still carries its own opt-in | — | diff review (no hunk in that file) |
| F-D | Docs match the code: `configuration.md` no longer advertises a `flag_admin` policy, states the group-level opt-in, and records the deferred roles/policies item + the accepted trade-off | — | diff review of the doc hunk |
| F-E | Build/test inheritance: `dotnet build SchoolCollab.slnx` 0 errors; every touched suite 0 failures (`SchoolCollab.ArchitectureTests.Unit` run **standalone**); no test was weakened to pass | — | parent-authoritative run |

### Discriminating tests / probes (what pre-fix behaviour fails each)

- The new arch assertion `Program.cs` **does not** contain `flag_admin` — pre-fix it does → fails. This is the round's primary discriminator.
- The new assertion that the two route files contain **no** `ApplyAdminPolicy` — pre-fix both do → fails.
- The `ConfigEndpoints.cs` "exactly one `OptIn`" assertion — it must **still pass** after the group opt-in is untouched, and it fails if the worker removes the group policy while deleting the route-level gate (the regression that would break forwarded bearer calls).
- Revert probes to be reported with observed counts: (a) re-add `RequireAuthorization("flag_admin")` to one write route → the first two assertions fail; (b) delete the group opt-in in `ConfigEndpoints.cs` → the one-`OptIn` assertion fails.

### Worker task notes

- Worker `ollama-cloud/deepseek-v4.1-flash`; **30-minute cap**; repo-scoped searches only (**never `find /`**); never commit/push/`gh`; never edit this round doc.
- Scope is exactly the six rows; a necessary touch outside them → **stop and report**, never widen silently.
- `dotnet build SchoolCollab.slnx` after the edits (no new files ⇒ no `.slnx` row; CPM: never a `Version=` on a `PackageReference`).
- Tests, one grep each, ≤2 attempts: `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` (**standalone**) and `dotnet test tests/SchoolCollab.Settings.Tests.Unit`; report exact totals.
- Comment honesty: the deleted `AdminPolicyExtensions` doc-comment claims "reads stay cookie-gated" — verify that claim against the code before reusing any of its wording elsewhere; do not carry a stale statement into the replacement comment.

## Worker Report

`0031b390` (`deepseek-v4.1-flash`) — 6 files, **+51/−45**, nothing staged.

    WORKER REPORT
    Changed files: Program.cs; ConfigEndpoints.cs; Endpoints/ConfigFlagRoutes.cs; Endpoints/ConfigTenantFlagOverrideRoutes.cs; tests/.../BearerForwardingWiringArchitectureTests.cs; documents/configuration.md
    Build: 0 errors (no new warnings; the 13 remaining are pre-existing)
    Tests: ArchitectureTests.Unit (standalone) 62/62 | Settings.Tests.Unit 519/519
    Probes: (a1) re-added `.RequireAuthorization("flag_admin")` to the Create route -> 1 failure, `Count(flagRoutes,"RequireAuthorization")` found 1; (a2) re-added the `ApplyAdminPolicy` helper + one call -> 1 failure, count found 2; (b) deleted the group opt-in -> 1 failure, `OptInCount(ConfigEndpoints.cs)` found 0 (fires in the per-Settings-file loop); (c) `git grep 'flag_admin|ApplyAdminPolicy|AdminPolicyExtensions|requireFlagAdmin' -- src/` -> no output. All restores md5/`diff -q` verified.
    Deviations: (1) dropped the now-unused `using Microsoft.AspNetCore.Authorization;` from Program.cs; (2) **added two `Count(<routeFile>,"RequireAuthorization").Should().Be(0)` assertions** because the plan's probe (a) as written could not discriminate — re-adding a route-level gate trips neither the Program.cs-absence nor the `ApplyAdminPolicy`-absence assertion; (3) `configuration.md` site count "10 sites" -> "9 sites" (the deleted policy was the 10th); (4) doc-comment rewrites (the group opt-in now described as carrying reads *and* writes).

Deviation (2) is the round's most valuable worker judgement: it **strengthened** the guard rather than shipping an assertion that
could not fail, and reported the non-discrimination it found instead of hiding it.

## Review

### Diff review (`69cbab7f`, `ollama-cloud/kimi-k2.7-code`, Tier 2 static)

    REVIEW
    Verdict: P2-only
    P1: none
    P2: BearerForwardingWiringArchitectureTests.cs:105-106 — redundant with the per-Settings-file loop two lines above (it already asserts ConfigEndpoints.cs has exactly one OptIn).
    P2: documents/solution/central-config-service-plan.md:60 — superseded decision record still states "`flag_admin` OIDC role gates write endpoints", now stale after this round.
    Closure: yes — the 6 changed files match the plan rows exactly.
    Bearer trap: yes — ConfigEndpoints.cs:23-28 keeps the same `if (oidcEnabled) { group.RequireAuthorization(policy => policy.RequireAuthenticatedUser().AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme)); }` block byte-for-byte; nothing in the patch touches it.
    Test honesty: the two route-file `RequireAuthorization == 0` assertions are discriminating and not over-broad (a route-level gate would bypass the group's Bearer scheme choice); the extra ConfigEndpoints.cs OptIn assertion is redundant.
    Repo-wide residue: offending: documents/solution/central-config-service-plan.md:60 — otherwise intentional.
    Best-practices: clean — the unused `using Microsoft.AspNetCore.Authorization;` was correctly removed from Program.cs; `SchoolCollab.Core.Auth` is still required; the deleted helper's stale "reads stay cookie-gated" claim was not carried forward.

**Parent disposition (Tier 2).** No P1, so no rework loop is required. Both P2s were **fixed by the parent**
(`diffs-flag-admin-gate-fix1.patch`): the redundant assertion was deleted with a comment recording that the
per-file loop already covers `ConfigEndpoints.cs`, and the durable plan record's item 7 was marked
**superseded** (the plan's row 7 was added *before* the file was touched, per the closure rule). The
one-file scope expansion is recorded here rather than silently absorbed.

## Acceptance

### Verdict: CLOSED — static/unit level (Tier 2, parent-transcribed)

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| F-A | No `flag_admin` policy or reference remains in `src/` | **MET** | parent-run `git grep 'flag_admin\|ApplyAdminPolicy\|AdminPolicyExtensions\|requireFlagAdmin' -- src/` → **no output** |
| F-B | The flag-write and tenant-override routes stay authenticated + bearer-eligible via the `/api/config` group opt-in | **MET** | reviewer confirmed `ConfigEndpoints.cs:23-28` byte-for-byte unchanged; worker probe (b) proves the group opt-in is guarded (its removal fails the suite); Arch 62/0 |
| F-C | Reads, audit and resolve routes untouched | **MET** | closure: `ConfigResolveRoutes.cs` is not in the patch; no hunk in the audit routes |
| F-D | Docs match the code, incl. the accepted trade-off and the owner's deferral | **MET** | `configuration.md:497` now states there is **no** `flag_admin` role policy; `documents/solution/central-config-service-plan.md` item 7 **marked superseded** with the reason |
| F-E | Build/test inheritance; no test weakened to pass | **MET** | build **0 errors**; Arch **62/0** (standalone); Settings.Tests.Unit **519/0**; the only test edits *added* discriminating assertions (and removed one redundant one) |

**Build / test numbers (parent-authoritative):** `dotnet build SchoolCollab.slnx` **0 errors** ·
`SchoolCollab.ArchitectureTests.Unit` **62 / 0** (standalone) · `SchoolCollab.Settings.Tests.Unit` **519 / 0**.
Frozen artifacts: `diffs-flag-admin-gate.patch` (6 files) + `diffs-flag-admin-gate-fix1.patch` (2 files,
the parent-applied P2 fixes). Base `670fff17`, branch `stack/20-flag-admin-gate`.

### P1 ledger — none

No P1 was raised at any stage: the plan needed no plan-review (Tier 2), and the static diff review returned
**P2-only** with the closure exact and the bearer trap verifiably intact. **Zero rework iterations used** (Tier 2 bound ≤1).

### P2 ledger — both fixed, none carried

1. A redundant `ConfigEndpoints.cs` opt-in assertion (the per-Settings-file loop already covers it) — deleted, with a
   comment recording why, after confirming `ConfigEndpoints.cs` is in the `SettingsEndpointFiles` array (`:39`).
2. **A durable** record (`documents/solution/central-config-service-plan.md` item 7) still asserting "`flag_admin` OIDC role
   gates write endpoints" — **marked superseded** with the reason and the current posture, not erased. The plan was amended
   with a 7th expected-files row *before* the file was touched, per the closure rule that an unlisted file is a P1.

### Accepted trade-off (owner ruling, restated for the record)

With the gate removed, **under OIDC any authenticated bearer user may write global feature flags**. That matches the
posture of the API's other eight write groups (authenticated bearer; audit attribution unchanged via
`ClaimsPrincipalActorAccessor`). Previously the effective behaviour was worse in both directions: an accidental deny-all
under OIDC and a no-op in dev. **Proper roles and policies for flag administration are deferred by the owner to a later
discussion (2026-09-24)** — this round deliberately leaves no replacement role, no realm change, and no half-built gate.

### Verified vs not verified

Static/unit-verified: the policy's absence (grep), the routes' continued coverage by the group opt-in (arch guard +
probe), and the docs. **Not verified live:** no AppHost run was performed, so the *effective* authorization of an OIDC-mode
flag write (now authenticated-bearer-only) is not exercised end-to-end in this round — the assertion is about wiring and
absence, not about a running Keycloak-mode request. `Settings.Tests.Integration` (which carries the repo's known
pre-existing environmental OpenRouter failures) was **not** run and is not claimed.

### Process record

Tier 2: parent-authored plan → one worker run → patch frozen → parent build/tests + one static diff review →
parent adjudication. **One scope expansion** (row 7, recorded before the touch). **Two parent-applied P2 fixes.**
One worker deviation that *strengthened* the guard rather than following the plan's weaker probe.
No mid-round escalation; no timeout; no build failure.
