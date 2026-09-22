# Round keycloak-dev-defaults — prompt-free dev startup for the Keycloak + SMTP secrets

Provider: pi **ollama-cloud** profile — worker `ollama-cloud/deepseek-v4.1-flash`, reviewer `ollama-cloud/glm-5.3-flash` (light-mode set, owner 2026-09-16: the verifier must not share the worker's model; ids verified against the session registry `ollama-cloud` provider, and identical to the ar-18 light precedent). **Tier 2 light round**, base `ba8cf25f`. Tree state at round start: tracked tree clean; two pre-existing **untracked** paths unrelated to this round (`brainstorms/`, `documents/rounds/.session-state.md`) — the round patch is isolated with `git diff` + an intent-to-add for the new test file.

## Plan

### Goal

A plain `aspire run` / `dotnet run --project src/AppHost/SchoolCollab.AppHost` currently **stops and prompts** for four secret parameters that have no value in any config source. Make Development startup prompt-free by committing **dev-only** defaults, while leaving Production / `aspire publish` behaviour exactly as it is today (no committed production secret).

### Diagnosis (verified in recon, with evidence)

`src/AppHost/SchoolCollab.AppHost/Program.cs` declares four `secret: true` parameters whose names appear in **no** configuration source:

| Line | Declaration | Value sources today |
|---|---|---|
| `:64` | `builder.AddParameter("keycloak-admin-password", secret: true)` | none (neither `appsettings.json` nor the AppHost user-secrets `71bc1e6c-899e-4131-98f2-60199f7d3ba2/secrets.json`) |
| `:65` | `builder.AddParameter("keycloak-client-secret", secret: true)` | none |
| `:158` | `builder.AddParameter("smtp-user")` | none |
| `:159` | `builder.AddParameter("smtp-password", secret: true)` | none |

`postgres-password`, `rabbitmq-password`, `openrouter-api-key` and `cache-password` **are** in the AppHost user-secrets, which is why they do not prompt. The owner's out-of-tree `%TEMP%\ar21\run-apphost*.cmd` scripts mask the gap by exporting `Parameters__keycloak_admin_password` / `Parameters__keycloak_client_secret` / `Parameters__smtp_*` — a plain `aspire run` does not.

### Scope IN

1. `src/AppHost/SchoolCollab.AppHost/appsettings.Development.json`
2. `src/AppHost/SchoolCollab.AppHost/Program.cs` — **comments only**
3. `tests/SchoolCollab.ArchitectureTests.Unit/AppHostDevParameterDefaultsArchitectureTests.cs` — new guard
4. `documents/configuration.md`
5. `documents/solution/keycloak-dev-parameter-defaults.md` — new Finding→Implementation note

### Scope OUT (do not touch)

- Any Production/publish configuration, manifest, or `appsettings.json` **values**.
- `Program.cs` **behaviour** — no `builder.Environment.IsDevelopment()` branching, no `AddParameter` overload changes.
- The `secret: true` flags — they stay (the dashboard must keep masking these values, and deployment manifests must keep treating them as sensitive).
- `school-collab-realm.json` contents.
- Parameter **names** (they are documented, env-var-mapped, and guarded).
- The three user-secret-backed parameters (`postgres-password`, `rabbitmq-password`, `openrouter-api-key`) — they already resolve.

### Binding decisions (implement as written; do not re-open)

**(a) Where the dev defaults live.** In `appsettings.Development.json` under `Parameters`. Rationale: `appsettings.json` → `appsettings.{Environment}.json` → user-secrets → environment variables is the default configuration chain, so the committed dev literal is **overridable** by user-secrets and by the existing `Parameters__…` env vars (this is what keeps the owner's scripts working unchanged); and the file is **not loaded** outside Development, so Production keeps requiring an explicit value. Both AppHost launch profiles already set `ASPNETCORE_ENVIRONMENT=Development` + `DOTNET_ENVIRONMENT=Development` (`src/AppHost/SchoolCollab.AppHost/Properties/launchSettings.json`).

**(b) The exact literals.**

| Parameter | Dev literal |
|---|---|
| `keycloak-admin-password` | `dev-only-keycloak-admin` |
| `keycloak-client-secret` | `dev-only-school-collab-client-secret` |
| `smtp-user` | `dev-user` |
| `smtp-password` | `dev-only-smtp-password` |

`keycloak-client-secret` **must** equal the realm file's client secret — `school-collab-realm.json` line 16, the `clientId: "school-collab-client"` client's `"secret"`. A mismatch silently breaks OIDC client authentication (Keycloak rejects `Auth:Keycloak:ClientSecret`).

**(c) Do not add these keys to the non-Development `appsettings.json`.** The new guard asserts their absence there — that is the assertion which keeps the "no committed production secret" posture honest.

**(d) `Program.cs` comment corrections only.** Two existing comments assert a state that (b) makes false and must be rewritten to point at the dev-default file while keeping the production warning:
- the Keycloak block (~`:54-57`): "`keycloak-admin-password` … gets NO committed default (operator supplies via `Parameters__keycloak_admin_password` / user-secrets)" and "`keycloak-client-secret` has a DEV-ONLY value … supplied via user-secrets/Parameters".
- the SMTP block (~`:152-155`): "`smtp-user` / `smtp-password` have NO committed default … a real relay's credentials come from user-secrets … the same posture as Parameters:openrouter-api-key".
Correct wording: dev-only defaults are committed in `appsettings.Development.json`; a real relay's credentials and any non-dev value must come from user-secrets / env-var, which override the committed dev literal; production never loads the Development file.

### Implementation steps

1. **`appsettings.Development.json`** — keep the existing `Logging` block and add a sibling `Parameters` object with the four keys/literals from (b). Valid strict JSON (2-space indent, matching the AppHost's other JSON files).
2. **`Program.cs`** — apply decision (d). No other edit; the file must otherwise be byte-identical (no reformatting, no reordering).
3. **New guard test** `tests/SchoolCollab.ArchitectureTests.Unit/AppHostDevParameterDefaultsArchitectureTests.cs` — model it on `AppHostRealmImportArchitectureTests.cs` (same namespace, same MSTest + FluentAssertions style, same walk-up-from-`AppContext.BaseDirectory` AppHost-dir discovery; it may copy or re-derive that helper — do **not** modify the existing file). It must read the JSON files with `JsonDocument` (no substring scanning) and contain exactly these three tests:
   - `DevClientSecret_MatchesRealmFileClientSecret_ForSchoolCollabClient` — parse `school-collab-realm.json`; from its `clients` array select the element whose `clientId` is `"school-collab-client"`; assert its `secret` **equals** `Parameters:keycloak-client-secret` in `appsettings.Development.json`. Failure message must name the OIDC-client-authentication consequence.
   - `SecretParameters_HaveNonEmptyDevDefaults` — assert the four keys from (b) exist and are non-empty strings in `appsettings.Development.json`'s `Parameters`.
   - `PromptingSecretParameters_AreAbsentFromNonDevelopmentAppSettings` — assert the same four keys are **absent** from `src/AppHost/SchoolCollab.AppHost/appsettings.json`'s `Parameters` (the production-posture guard).
   Discovery must **throw** when the AppHost directory or either JSON file is missing (the `AppHostRealmImportArchitectureTests` precedent — never pass vacuously by finding nothing).
4. **`documents/configuration.md`** — the `configuration-documentation.md` rule requires this in the same change set:
   - §2 rows `smtp-user` (`:137`), `smtp-password` (`:138`), `keycloak-admin-password` (`:141`), `keycloak-client-secret` (`:142`): change the Default column from `_none_` / `_none — must be supplied_` to the dev literal, and replace the "No committed default" prose with: a **dev-only** default is committed in `appsettings.Development.json`; production/relay values come from user-secrets or env-var, which override it. Keep the 🔐 secret framing and keep the §4 cross-references.
   - §2 "For secrets (…)" paragraph (~`:172-187`): keep "never commit **production** secrets", but name the four dev-only exceptions and the file they live in, so the instruction is no longer contradicted by the repo contents.
   - §4 "The two parameters AC#4 needs" (~`:355-362`): the heading clause "(no committed defaults)" is now false for dev — reword to state that AC#4 is exercised out of the box in Development, and that Production supplies real values.
   - §4 `Auth:Keycloak:ClientSecret` row (~`:310`) and the Secrets block (~`:314-315`): mention the committed dev default + the realm-file parity requirement.
   - §11 env-var reference (`:827-828` and the `smtp-user`/`smtp-password` rows ~`:823-824`): the env-var mapping is unchanged, but note the new default where the row states one.
   - §12 Production checklist: read it and, **only if** it lists any of these four parameters as requiring a supplied value, add the one line that the Development default must not be relied on in production. If it does not mention them, leave §12 alone and say so in the WORKER REPORT.
   - Update the file's "Last updated" footer if it has one.
5. **`documents/solution/keycloak-dev-parameter-defaults.md`** — short durable note per `AGENTS.md` "Finding → Implementation": the Finding (the four prompting parameters + why they prompted), the decision (dev-only defaults in `appsettings.Development.json`, overridable by user-secrets/env), the production posture (unchanged, guard-asserted), and the verification results.
6. **Verify** (worker): `dotnet build SchoolCollab.slnx`; then `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` and read the `total:`/`failed:` lines with a single grep.

### Expected files (the reviewer's conformance baseline)

| # | Path | Change |
|---|---|---|
| 1 | `src/AppHost/SchoolCollab.AppHost/appsettings.Development.json` | modified — `Parameters` block added |
| 2 | `src/AppHost/SchoolCollab.AppHost/Program.cs` | modified — comments only, 2 blocks |
| 3 | `tests/SchoolCollab.ArchitectureTests.Unit/AppHostDevParameterDefaultsArchitectureTests.cs` | **new** |
| 4 | `documents/configuration.md` | modified — §2 (4 rows + secrets paragraph), §4 (2 spots), §11 (defaults noted), optionally §12 + footer |
| 5 | `documents/solution/keycloak-dev-parameter-defaults.md` | **new** |

Any additional changed path is a scope deviation and must be reported; any of these five missing is a plan violation.

### Acceptance criteria

- **AC1** — `appsettings.Development.json` contains a `Parameters` object with all four keys from (b) at their exact literals, and its JSON still parses strictly.
- **AC2** — `appsettings.json` (non-Development) contains **none** of the four keys.
- **AC3** — `keycloak-client-secret`'s dev literal equals the `school-collab-client` client's `secret` in `school-collab-realm.json`, asserted by the new guard test.
- **AC4** — the three new guard tests fail against the pre-fix tree (i.e. they are discriminating, not vacuous): with no `Parameters` block in `appsettings.Development.json`, all three must fail. The worker must reason this explicitly; the parent will confirm by inspection of the assertions.
- **AC5** — `dotnet build SchoolCollab.slnx` → **0 errors**.
- **AC6** — `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` → **0 failures**, total **≥ 39** (38 before this round).
- **AC7** — `Program.cs` diff is comments only (no statement/behaviour change); the two `secret: true` flags and all `AddParameter` call signatures are byte-identical.
- **AC8** — `documents/configuration.md` no longer states "No committed default" for any of the four parameters, and `documents/solution/keycloak-dev-parameter-defaults.md` exists.

### Reviewer acceptance criteria (Tier 2)

The static reviewer must confirm, against `diffs-keycloak-dev-defaults.patch` + the plan:

1. **Scope conformance** — the changed-path set equals the 5 expected files (no extras, none missing).
2. **AC7 by diff inspection** — `Program.cs` hunks touch only comment lines; no `AddParameter` signature or `secret:` flag changed.
3. **AC4 by reading the assertions** — each new test can actually fail pre-fix (no assertion that is vacuously true, e.g. asserting a key is "not non-empty" or comparing a value to itself); discovery throws rather than silently skipping.
4. **AC1/AC2/AC3 by reading the JSON + realm file** — literals match decision (b); the realm file is unchanged; `appsettings.json` gained nothing.
5. **No overwrites** — no pre-existing code, test, or documentation block deleted or rewritten beyond the plan's stated edits (unrelated reformatting is a finding). In particular `AppHostRealmImportArchitectureTests.cs` must be untouched.
6. **Repo skills honored** — `dotnet-best-practices` (C# test style), `configuration-documentation` (the §2/§4/§11/§12 coverage above).

### Constraints for the worker

- Repo-scoped searches only (`src/`, `tests/`, `documents/`) — **never** `find /` or a filesystem-wide scan.
- Test invocation: `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` with **no extra flags**; read results with one `grep -E "^\s*(total|failed|succeeded):"` — no ad-hoc pipelines, no debug log files. At most 2 attempts per result read.
- Build: `dotnet build SchoolCollab.slnx` (the `.slnx`, not a `.sln`).
- Do **not** edit this round doc. Do **not** run `git commit` / `git push` / any `gh` command. Do not create branches.
- If the build or the guard test fails in a way not covered by this plan, stop and report it as a deviation rather than improvising scope.

### Worker task spec

You are the WORKER for Tier-2 round `keycloak-dev-defaults`. Read this `## Plan` section completely — it is your single source of truth. Implement steps 1–6 in order, stay strictly inside the "Scope IN" list and the 5-file expected-files table, honour every binding decision (a)–(d) as written, then run the build and the `SchoolCollab.ArchitectureTests.Unit` suite. Read `AGENTS.md` and `.github/copilot/rules/dotnet-best-practices.md` before touching the `.cs`, and `.github/copilot/rules/configuration-documentation.md` before the docs. Return **only** the WORKER REPORT block (Changed files / Build / Tests / Deviations from plan).

## Worker Report

> Worker `ollama-cloud/deepseek-v4.1-flash`, 206s. Returned verbatim.

```
WORKER REPORT
Changed files:
- src/AppHost/SchoolCollab.AppHost/appsettings.Development.json (M — Parameters block added: dev-only-keycloak-admin, dev-only-school-collab-client-secret, dev-user, dev-only-smtp-password)
- src/AppHost/SchoolCollab.AppHost/Program.cs (M — comments only; verified no non-comment changed line in the diff)
- tests/SchoolCollab.ArchitectureTests.Unit/AppHostDevParameterDefaultsArchitectureTests.cs (new — 3 tests)
- documents/configuration.md (M — §2 4 rows + secrets paragraph + AddParameter prompt note; §4 AC#4 heading/table, Auth:Keycloak:ClientSecret row, Secrets block; §12 line)
- documents/solution/keycloak-dev-parameter-defaults.md (new)

Build: 0 errors (dotnet build SchoolCollab.slnx; 13 pre-existing warnings only — NU1902/NU1903/ASPIRE010, none from the changed files).

Tests: tests/SchoolCollab.ArchitectureTests.Unit: 41 passed, 0 failed (38 before -> 41 after; AC6 >=39 met). AC4 verified empirically: with the Parameters block temporarily removed, exactly the 3 new tests failed (total 41 / failed 3 / succeeded 38); the file was then restored and checksum-matched (md5 1e5d5097...), and the suite re-ran green at 41/0. AC1/AC2/AC3/AC8 also machine-checked: literals exact, no key leaked into appsettings.json, realm file untouched, no "No committed default" left for the four parameters.

Deviations from plan:
1. Test 3 implemented as the dev-only scoping invariant, not absence-only — AC4 requires all three new tests to fail pre-fix, but step 3's test-3 spec (assert the four keys are absent from appsettings.json) cannot fail pre-fix by construction: pre-fix they are absent from both files, so an absence-only assertion passes. I kept the production-posture assertion and added its necessary other half (devParameters.Should().ContainKey(key, "...must be supplied ONLY as a dev default...")), so the test asserts the pairing "present in Development AND absent from non-Development". Renamed PromptingSecretParameters_AreDevOnly_NotInNonDevelopmentAppSettings accordingly. Flagged for the reviewer, since AC4 and step 3 conflict.
2. §11 left unmodified — its table is | Config key | Env-var | with no default column, so no row states a default for the plan's "note the new default where the row states one" to annotate.
3. §12 edited (the plan's conditional fired): it lists Auth:Keycloak:ClientSecret as requiring a secret store, so the one line was added that the Development default must not be relied on in production (extended to name all four).
4. Two further in-file edits inside expected file #4: configuration.md has no "Last updated" footer, so none was updated; and the §2 sentence stating that AddParameter(name, secret: true) is "prompted for on first aspire run" was corrected, because this round removes that prompting.

No other path was changed; the change set is exactly the 5 expected files. AppHostRealmImportArchitectureTests.cs untouched. No commit/push/gh run.
```

## Review

> Static diff-only reviewer `ollama-cloud/glm-5.3-flash`. No plan-review pass (Tier 2 has no plan gate). The reviewer never built or tested; its numbers (none volunteered) are discarded by rule.

```
REVIEW
Verdict: PASS
P1: (none)
P2: (none)
Best-practices: no overwrites / skills honored / readable
```

**Deviation judgments — all four accepted as correct:**

1. **Test-3 rework** — *correct and better.* It is a strict superset of binding decision (c): it still asserts absence from `appsettings.json` (AC2) and adds presence-in-Development. AC4 holds either way — pre-fix, `ReadParameters(DevSettingsPath)` throws on the missing `Parameters` section (`AppHostDevParameterDefaultsArchitectureTests.cs:95-102`), so the test fails; post-removal of a single dev key, `ContainKey` fails. **Pass-1 nuance — CORRECTED by pass 2 (see below):** pass 1 called the worker's rationale "imprecise" and claimed absence-only would also have failed pre-fix via the throwing helper. That is **wrong**: `appsettings.json` already contains a 19-key `Parameters` object, so `ReadParameters(SettingsPath)` does **not** throw and an absence-only `NotContainKey` assertion **would have passed against the pre-fix tree** — violating AC4. The worker's original rationale was accurate and the rename is fully justified.
2. **§11 unmodified** — correct. Its table is a two-column `| Config key | Env-var |` with no Default column and no prose stating a default, so the plan's condition never fired.
3. **§12 edited** — correct and in scope; the pre-fix checklist did list `Auth:Keycloak:ClientSecret` as requiring a secret store.
4. **Extra §2 edits** — both correct: no "Last updated" footer exists, and leaving the "prompted for on first `aspire run`" sentence would have contradicted this round's own effect. Posture check: every remaining "no committed default" statement in `configuration.md` now refers only to `postgres-password` / `rabbitmq-password` / `openrouter-api-key` (lines 117/118/127), which are explicitly out of scope.

**Reviewer evidence, criteria 1-6:** changed-path set is exactly the 5 expected files; `Program.cs` hunks are `//`-only with `AddParameter` calls and `secret: true` flags byte-identical; all three new tests fail pre-fix (tests 1-2 via the throwing helpers; test 3 via the widened pairing — see the pass-2 correction below) and no assertion compares a value to itself (the secret test compares the dev file against the realm file — two different sources); the four (b) literals are exact, `appsettings.json` gained none of them, and the parity assertion reads the `clientId == "school-collab-client"` client's `secret`; the realm file is not in the diff and exactly one `*-realm.json` exists; `AppHostRealmImportArchitectureTests.cs` is absent from the patch; the new test mirrors the precedent's namespace/style/discovery and the docs follow the `configuration-documentation` coverage.

### Pass 2 — independent re-review on `ollama-cloud/kimi-k2.7-code` (owner reviewer default, 2026-09-22)

Dispatched because the round's reviewer default changed after pass 1. Independence guards applied: the reviewer was told to read the `## Plan` section **only** (explicitly not pass 1's verdict), that the two `.pi/skills/…` edits are an out-of-round concern to ignore, and to adjudicate the step-3 plan-vs-implementation conflict itself rather than assume the worker was right.

```
REVIEW
Verdict: PASS
P1: (none)
P2: (none)
Best-practices: no overwrites / skills honored / readable
Step-3 judgment: (i) correct and required — the plan text is at fault.
```

**Pass 2's step-3 finding (supersedes pass 1's nuance; parent-verified).** Pre-fix, `appsettings.json` **already contained a `Parameters` object** (19 unrelated keys — `outbox-exchange-*`, `ai-default-provider`, `keycloak-client-id`, …) and none of the four target keys. So an absence-only `NotContainKey` assertion would have **passed** against the pre-fix tree and AC4 would have been violated. The worker's widening to the dev-only pairing (*present in `appsettings.Development.json`* ∧ *absent from `appsettings.json`*) was therefore **correct and required**, its stated rationale was accurate, and pass 1's "imprecise" note was itself the imprecise one. **AC4 is sound; the plan's step-3 wording is the defective artifact** — it is internally inconsistent with its own AC4 unless the test also touches the Development file. Parent independently confirmed the premise (19-key `Parameters` object present; zero target keys; the guard's `ReadParameters` throws only when the section is missing, `AppHostDevParameterDefaultsArchitectureTests.cs:99`).

Pass 2 reached PASS independently on the same evidence set, so the round verdict is unchanged and now doubly verified. Pass 1's evidence paragraph above is otherwise confirmed, except for the two sentences corrected inline.

**Effect on the round's model traceability:** the round therefore ran reviewer passes on two models — pass 1 `ollama-cloud/glm-5.3-flash` (the pre-change default) and pass 2 `ollama-cloud/kimi-k2.7-code` (the new default). The pass-1 PASS is retained for the record; the pass-2 verdict is the one reached under the current default.

## Acceptance

> Tier 2 — parent-authored plan, parent-transcribed verdict on the reviewer's PASS plus the parent's own authoritative pass.

| Criterion | Verdict | Evidence |
|---|---|---|
| AC1 dev defaults present at exact literals | PASS | `appsettings.Development.json` `Parameters`: `dev-only-keycloak-admin`, `dev-only-school-collab-client-secret`, `dev-user`, `dev-only-smtp-password` (parent-inspected; strict JSON) |
| AC2 absent from non-Development appsettings | PASS | `grep` over `appsettings.json` → none of the four keys; guard test 3 asserts it |
| AC3 dev client secret == realm file client secret | PASS | guard test 1 compares against `school-collab-client`'s `secret`; realm file unchanged |
| AC4 new tests are discriminating | PASS | worker probed empirically (Parameters block removed → 3 failed / 38 passed, restored → 41/0); reviewer independently confirmed each assertion is non-vacuous |
| AC5 build | PASS | `dotnet build SchoolCollab.slnx` → **0 Error(s)**, 13 pre-existing warnings |
| AC6 affected/repo-wide suite | PASS | `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` → **total 41, failed 0, succeeded 41** (38 before this round) |
| AC7 `Program.cs` comments only | PASS | parent inspection of `git diff -U0`: every `+`/`-` line is a `//` comment; no statement, `AddParameter` signature, or `secret:` flag changed |
| AC8 docs updated | PASS | no "No committed default" remains for the four parameters; `documents/solution/keycloak-dev-parameter-defaults.md` created |

**Runtime proof (the round's actual goal, verified after the reviewer pass):** a cold start launched with `Parameters__keycloak_*` **and** `Parameters__smtp_*` **removed** from the environment produced **zero prompt / missing-parameter signals** in the AppHost log (`apphost-devdefaults.log`), reached `Distributed application started.` on `13.5.4+9c1b401`, and brought up all six containers including `keycloak`. Before this round, the same launch shape stopped on a prompt. AppHost was torn down afterwards; containers removed, `ar10repro` kept.

**Findings:** 0 P1, 0 P2. Rework iterations: 0. Reviewer loop 0/1 used.

**Round verdict: CLOSED.** Exactly the 5 expected files changed; the frozen patch is `diffs-keycloak-dev-defaults.patch`. Two scope-neutral notes: (i) the plan's step-3 test-3 wording conflicted with its own AC4 and the worker resolved it correctly under reviewer confirmation — the plan text is left as written, the resolution is recorded here; (ii) this round ran its reviewer on `ollama-cloud/glm-5.3-flash`; the owner's reviewer default is changed to `kimi-k2.7-code` (`ollama-cloud` profile) / `z-ai/glm-5.3` (`cline` profile) **after** this round, so it applies to subsequent rounds only.
