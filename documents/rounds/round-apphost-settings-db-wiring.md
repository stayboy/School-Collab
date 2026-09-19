# Round — AppHost `settings-db` wiring + silent-hang runbook (+ ar-21 AC9 residual closure)

- **Status: COMPLETE (Solo, owner-authorised via "go on as recommended") — 2026-09-19.** Provider: pi (`ollama`); no worker, no independent reviewer, no UI tester were dispatched (Solo scope: two one-line AppHost references, one guard test, two docs). The parent authored, executed, verified, and wrote this doc itself. The runtime evidence below was captured on the same machine session that diagnosed the DCP blocker.
- **Carries:** ar-21's headline residual **AC9 (orchestrated cold start)** — see `## ar-21 AC9 residual closure`.

## Scope

Two independent workstreams that both surfaced from the same event — the AppHost starting successfully for the first time on this machine:

1. **DCP silent-hang diagnosis** (ar-21 AC9's blocker) — root cause, fix, runbook.
2. **A wiring defect the blocked AppHost had hidden**: two hosts register the Settings bounded context but never received the `settings-db` connection string.

Explicitly **not** in scope: any `.razor`/`.css`, contract shapes, migrations, CPM, the `smtp-user`/`smtp-password` posture, and the ar-20 identity residual set.

## Plan

| # | Item | Kind |
|---|---|---|
| 1 | `.WithReference(settingsDb)` on `assignments-api` + `students-api` | fix |
| 2 | `AppHostSettingsDbWiringArchitectureTests` guard (every `AddSettingsCore` host references `settingsDb`) | test |
| 3 | `documents/runbooks/aspire-apphost-silent-hang.md` | docs |
| 4 | `documents/configuration.md` §1/§8 accuracy for the `settings-db` consumers + the fallback trap | docs |

A fifth candidate — dev defaults for `smtp-user` / `smtp-password` — was **dropped after reading the source**. The no-default posture is deliberate and documented (ar-16, WS-E2):

> `smtp-user` / `smtp-password` have NO committed default: MailPit accepts anonymous mail, and a real relay's credentials come from user-secrets (`Parameters:smtp-password`) or the `Parameters__smtp_password` env var — the same posture as `Parameters:openrouter-api-key`.

So the earlier "fresh clone can't start" hypothesis was **falsified** and no change was made.

## Execution

### 1. Root cause — the AppHost never started because a DCP child process was dying silently

`dcp run-controllers` (separate from `dcp start-apiserver`) exited immediately with status 1:

```json
{"ExitCode":1,"error":"failed to initialize state store: could not prepare state store
 directory 'C:\\Users\\skwar\\.dcp\\state.elevated': directory ... has invalid ownership:
 directory owner does not match current user or token owner"}
```

The API server kept running, so the AppHost connected, built its model, resolved parameters, and created the endpoint Services — then **nothing reconciled them, ever**, with no timeout and no error. The misleading `Unable to allocate a network port for service '<x>'` warnings are produced by the 1-minute service-address watch inside that reconciliation path.

Stale artifacts: `~/.dcp/state.elevated` dated **Jun 2** and `~/.dcp/state` dated **Sep 16**, with an orphaned zero-byte `state.sqlite3.migrate.lock` dated Jun 2. Matches upstream [microsoft/aspire#18922](https://github.com/microsoft/aspire/issues/18922).

**Fix (reversible):** renamed the two directories aside (`state.bak-ar21`, `state.elevated.bak-ar21`) — DCP recreated them with correct ownership. This is machine state, **not a repo change**.

**Falsified along the way** (each cost a launch): port exhaustion, Windows excluded port ranges, stale containers holding ports, missing parameters, `DOCKER_HOST`/pipe choice, Docker engine health, DCP version skew, console vs headless.

### 2. The wiring defect the blocked AppHost had hidden

With the AppHost finally up, `assignments-api` logged **1029** and `students-api` **1030** lines of:

```text
Npgsql.NpgsqlException: Failed to connect to 127.0.0.1:5432   (database 'schoolcollab_settings')
SchoolCollab.Core.Messaging.OutboxDispatcher: OutboxDispatcher loop failed; will retry after delay
```

Cause: both hosts call `AddSettingsCore(...)` — required for `IEntityCodeGenerator`, which reads `EntityCodeRule` rows from `settings-db` — but neither had `.WithReference(settingsDb)` in its AppHost resource chain, unlike `settings-api` (which logged **0**). `Settings.Core/Extensions.cs` then falls back to the hardcoded `Host=localhost;Port=5432` string, so the Settings outbox dispatcher registered by `AddSettingsCore` failed on every retry, while the resource still reported **Healthy** (Aspire only probes the endpoint).

`settings-db` was referenced only by `migrator` and `settings-api`. Fixed by adding the reference to both hosts, adjacent to their existing database references, with a comment naming the fallback.

### 3. Files changed

| File | Change |
|---|---|
| `src/AppHost/SchoolCollab.AppHost/Program.cs` | `+ .WithReference(settingsDb)` on `students-api` and `assignments-api` (+ explaining comments) |
| `tests/SchoolCollab.ArchitectureTests.Unit/AppHostSettingsDbWiringArchitectureTests.cs` | **new** — 3 tests |
| `documents/runbooks/aspire-apphost-silent-hang.md` | **new** — symptom → diagnosis → fix → red herrings |
| `documents/configuration.md` | §1 resource table + §8 connection-string table: `settings-db` consumers now list both hosts; new §8 warning on the fallback trap |

### 4. Guard-test design note (a self-inflicted failure worth recording)

The first design resolved each AppHost project constant to a directory via `SchoolCollab_X_Api → SchoolCollab.X.Api` and threw unless **exactly one** directory matched. It failed for real:

```text
Expected exactly one 'SchoolCollab.AI.Server' project directory under ...\src, found 2:
 ...\src\SchoolCollab.AI.Server, ...\src\AI\SchoolCollab.AI.Server
```

`src/SchoolCollab.AI.Server/` is a **stale leftover containing only `bin/` + `obj/`** (Jul 7) from the project's move to `src/AI/`; the sln points at `src/AI/...`. The guard was rewritten to invert the lookup — scan real `Program.cs` files under `src/` (skipping `bin`/`obj`) and match by project name — which is immune to that leftover.

Separately, the chain parser initially stopped at the first `;`, and the comment added in step 2 contained `Host=localhost;Port=5432` — so the guard failed on its own explanatory comment. Fixed on both sides: the comment now reads `Host=localhost, Port=5432`, and the parser strips trailing `//` comments before chain extraction (this is why the helper carries a dedicated XML doc).

## Evidence

### Build / tests

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.sln` | **0 errors**, 135 warnings (2 pre-existing `MSTEST0042` in `Settings.Tests.Unit`, unrelated) |
| `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` | **29 total, 0 failed** (26 pre-existing + 3 new) |
| Revert probe — remove `.WithReference(settingsDb)` from `assignments-api` | **failed** `EveryHostRegisteringSettingsCore_ReferencesSettingsDb` (1 failed / 29) |
| Revert probe — restore | `cmp` **byte-identical** → 29 / 0 |

### Runtime (orchestrated AppHost cold start, after the fix)

| Signal | Before | After |
|---|---|---|
| `Unable to allocate a network port` warnings | 8 | **0** |
| `dcp.exe` processes | 1 | 12 matches |
| Containers | none | cache, keycloak, mailpit (healthy), pgadmin, postgres, rabbitmq |
| `Distributed application started` | absent | **present** |
| `127.0.0.1:5432` errors — assignments-api / students-api / settings-api | 1029 / 1030 / 0 | **0 / 0 / 0** |
| `OutboxDispatcher started` — assignments-api / students-api / settings-api | 0 / 0 / 1 | **2 / 2 / 1** |
| `aspire describe` Healthy lines / Unhealthy+Error+Failed | — | **42 / 0** |

## ar-21 AC9 residual closure

ar-21 closed with AC9 (orchestrated cold start) as its headline residual because the AppHost could not start on this machine. It can now, so AC9 is **CLOSED** on the orchestrated run:

| AC9 clause | Evidence from the orchestrated run |
|---|---|
| All resources reach a ready state | `aspire describe` → all Running + **Healthy** (7 containers + 5 project resources + parameters) |
| `Distributed application started` | present; dashboard live on `:15006` |
| Readiness gate ordering | `migrator` ready → **`keycloak` ready** → `settings-api` → `assignments-api` → `families` / `admin` (gated hosts strictly after keycloak) |
| Realm import under the AppHost bind mount | `Realm 'school-collab' imported` / `Import finished successfully` |
| No OIDC metadata / issuer failures | **0** `IDX10205` / `IDX10214` / metadata errors; `families` logged 0 errors total |
| Dev IdP issues usable tokens | ROPC `dev-teacher` → **200**; `aud` = `azp` = `school-collab-client`, `tenant_id` = `…0002`, `teacher_id` = `…0003` (ar-20's audience mapper, working in the orchestrated realm) |
| Discovery + JWKS resolvable | `.well-known/openid-configuration` **200** (issuer `http://localhost:<keycloak-http>/realms/school-collab`), `/protocol/openid-connect/certs` **200** |
| API reachable through orchestrated wiring | `GET http://localhost:5199/assignments` with token → **200** (`[]`) |

Two honesty notes carried from ar-21:

- `GET /assignments` without a token also returns **200** — this is the **intended dev bypass** (`FEATURE:DisableOIDCAuth`, dev default `"true"`, AppHost `Program.cs:184`), not a regression. ar-21's real AC#4 proved the *enforced* path (`DisableOIDCAuth=false`, standalone) returns 401/401.
- The `[]` body reflects ar-21's already-recorded AC5 data-visibility limit (the dev database holds zero assignment rows), not a broken query.

## Acceptance

- **Item 1 (wiring fix): ACCEPTED.** Runtime evidence is discriminating (0 vs the previously measured 1029/1030) and the guard proves the invariant in CI.
- **Item 2 (guard): ACCEPTED.** Revert-probe proven discriminating; non-vacuity test asserts the scan actually found the AppHost chains and the three `AddSettingsCore` projects.
- **Item 3 (runbook): ACCEPTED.** Symptom → diagnosis → fix → red-herring table, written from the reproduced failure.
- **Item 4 (docs): ACCEPTED.** Both tables now match the AppHost; the fallback trap is documented where the connection strings are documented.
- **ar-21 AC9: CLOSED** (orchestrated evidence above).
- **No independent review** was performed (Solo, owner-authorised). The only reviewer-grade checks were the revert probe and `cmp` on restore.

## Residuals / follow-ups

- **Housekeeping:** `src/SchoolCollab.AI.Server/{bin,obj}` is a stale leftover from the move to `src/AI/` (gitignored, so invisible to git). Worth deleting — it made a name-based project lookup ambiguous.
- **`~/.dcp` backups:** `state.bak-ar21` / `state.elevated.bak-ar21` can be deleted once the AppHost is confirmed stable.
- **Aspire version skew:** CLI bundle `13.4.0` vs `Aspire.Hosting` `13.4.5` (newest `13.5.4`). The shared `~/.dcp/state` store is migrated by whichever DCP touches it first, which is what produced the stale directory — `aspire update` is advisable.
- **The `settings-db` fallback pattern is repo-wide, not settings-specific.** `Settings.Core/Extensions.cs` and its peers all fall back to `localhost:5432`; only the `settings-db` case is guarded today. A broader guard (every `ConnectionStrings:<name>` read has a matching `.WithReference`) would close the class, but needs a design decision.
- **Carried from ar-21/ar-20/ar-19 unchanged:** ar-21 AC5 data-visibility limit; the ar-20 identity residual set (service-to-service token forwarding, `DisableOIDCAuth` split-brain, cross-tenant `teacher_id` validation, ward `/students` + Admin bearer-only after a flip, dev `staff_number` NULL); the ar-19 set (republish-`Skip`-over-`Sent` note, duplicated `StudentsContactAddressResolver`, `SendoutTimeOfDay`/`SendoutIntervalMinutes` unenforced, single-instance sweeps); D1-deferred `Aspire.Hosting.Keycloak` + production realm-import story.
