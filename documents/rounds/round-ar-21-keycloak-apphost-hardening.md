Tier 3 lean (explicit owner choice), provider pi (ollama) — models: orchestrator ollama-cloud/glm-5.3-flash, worker ollama/deepseek-v4-flash:0731-cloud, reviewer ollama-cloud/deepseek-v4.1-flash, plan-review ollama-cloud/deepseek-v4.1-flash (lean = reviewer model), no UI tester (there is no UI diff in scope). Base 74a42da1 (ar-20 merge commit). Dirty at start (MUST be excluded from the round diff): documents/solution/assignment-request-implementation-details.md, documents/solution/assignment-request-go-forward-breakdown.md. (Also pre-existing untracked working-tree artifacts — documents/rounds/.* helper files, brainstorms/ — not part of the round diff.)

- **Status: IN PROGRESS — Tier 3 lean, started 2026-09-18 (orchestrator run).** Scoped 2026-09-18 from ar-20's headline residual (AC#4) plus the owner's post-close gap analysis of the AppHost Keycloak block; scope-reviewed the same day (`glm-5.3:cloud`, run `219ddbed`, all findings landed — preserved verbatim in `## Review` below). Branch `stack/21-ar-21-keycloak-apphost-hardening` cut at `74a42da1`. **Owner decisions D1–D3 are PINNED** (see `### Key decisions`); the `## Plan` below is the step-2b plan-review target and the worker's standalone contract. The frozen patch `diffs-ar-21-keycloak-apphost-hardening.patch` is written by the parent after the worker run, per the round-docs policy.

## Plan

### Goal

Make the Keycloak dev-IdP path in the Aspire AppHost **actually startable and verifiable**, then run the real AC#4 end-to-end verification that ar-20 shipped unverified. ar-20 is closed and shippable; this round carries its headline residual.

### Provenance (why this round exists)

| Source | Claim |
|---|---|
| ar-20 `## Acceptance` → "Residuals (recorded)" | **Headline: AC#4 (manual Keycloak end-to-end) was NOT run.** |
| ar-20 re-verify pre-flight notes (i)–(iii) | migrator `Development` env; Keycloak endpoint has no `targetPort`/health-check/`WaitFor` (unlike mailpit); realm import may reject the `//` comments |
| ar-20 plan-amendment paragraph ("P2 corrections applied in the same steer") | "Keycloak image version pinned + **health-check/`WaitFor` or softened AC#4 wording**" — the worker took the *softened wording* branch; the hardening branch is this round |
| Owner gap analysis 2026-09-18 | the raw `AddContainer` block re-implements what the first-party `Aspire.Hosting.Keycloak` integration provides, and omits the parts it exists to provide (readiness gate). **F5 is externally sourced** — it is *not* substantiated anywhere in-repo; see F5's provenance note |

### Findings (evidence)

Each finding is code-grounded; `Program.cs` = `src/AppHost/SchoolCollab.AppHost/Program.cs`. All file:line cites re-verified against the branch tree at `74a42da1` during the orchestrator run.

| # | Finding | Evidence |
|---|---|---|
| **F1** | **Readiness race (highest impact).** The keycloak container declares no health signal and **no consuming host waits for it**. Keycloak's own docs are explicit that the server does not fully start until the realm import completes, so all four hosts can fetch OIDC metadata / validate issuer during import and fail intermittently on cold start. | `:61-69` (container: no `KC_HEALTH_ENABLED`, no management endpoint, no health check); `:75-81` `WireKeycloakAuth` adds **env only**, no `.WaitFor(keycloak)`; all four call sites — settings `:198`, students `:236`, assignments `:263`, admin `:319-321`. Contrast `:181-183`, `:194-196`, `:231-233` — db/rabbit/redis/migrator **are** gated |
| **F2** | **Endpoint omits `targetPort`.** Mailpit — the precedent the ar-20 re-verify cited — passes `port` **and** `targetPort`. | `:69` `.WithHttpEndpoint(name: "http")` vs `:42` `.WithHttpEndpoint(port: 8025, targetPort: 8025, name: "http")` |
| **F3** | **Realm file violates Keycloak's naming convention** *(the convention itself is EXTERNAL; the reversal vs it is in-repo fact)*. Keycloak requires realm import files to be named **`<realm>-realm.json`**; ours is reversed. | `:59` `realm-school-collab.json`; `.csproj:20` `<None Include="realm-school-collab.json" …>`; bind mount `:66` → `/opt/keycloak/data/import/realm-school-collab.json` |
| **F4** | **The realm file is not strict JSON — proven, not suspected.** Eight `//` comment lines sit *inside* the object. Keycloak's tolerance for comments is undocumented, so the import may simply refuse the file. | `realm-school-collab.json:1-9` — eight `//` lines sit between the opening `{` (`:1`) and `"realm"` (`:10`), i.e. **inside** the object literal: the structural fact, verifiable by reading. Session-agent run of `JSON.parse` (node) additionally fails with `Expected property name or '}' in JSON at position 4 (line 2 column 3)` |
| **F5** | **Hand-rolled where a first-party integration reportedly exists** *(EXTERNAL — verified by the session agent against aspire.dev/docs on 2026-09-18; **zero in-repo substantiation**: no package ref, no CPM entry, no code — the only in-repo mentions are owner-authored docs, i.e. circular provenance. Treat the API inventory below as unconfirmed until a restore proves it)*. `Aspire.Hosting.Keycloak` (`AddKeycloak(...)`) reportedly supplies exactly what F1/F2 lack — a **management endpoint on 9000**, `KC_HEALTH_ENABLED=true`, **`.WithHttpHealthCheck("management", "/health/ready")`**, run-mode `start-dev` / publish-mode `start`, `--import-realm`, `WithRealmImport`, `WithDataVolume`, `WithOtlpExporter` — plus client-side `Aspire.Keycloak.Authentication` (`AddKeycloakJwtBearer(serviceName, realm, …)`). Note: the *client* integration reportedly carries the same caveat we already documented (service discovery's `https+http://` does not satisfy `RequireHttpsMetadata=true`), and its `WithRealmImport` is reportedly **dev-time only** (not supported by `aspire publish`/deploy). | our raw block `:61-69`; integration per aspire.dev/integrations/security/keycloak + `dotnet/aspire` source (external) |
| **F6** | **Misleading comment.** The block tells the reader to "remove the keycloak data volume … to re-import a changed realm file", but **no volume is declared** — every container recreate re-imports (which is *good* for dev and should be stated as such, or a real volume should be added deliberately). | `:63-65`; `WithDataVolume` appears only for postgres (`:13`) and rabbit (`:25`) — there is no **data-persistence** volume for Keycloak anywhere (the single-file realm **import** bind mount at `:66` is not a data volume) |
| **F7** | **Secret friction.** `keycloak-client-id` has a dev default; both secrets have none, while the committed realm pins a fixed dev-only client secret — so a dev must type the exact literal. The official integration instead **generates** a default admin-password parameter. Our stricter posture is defensible; the inconsistency is the open question. | `:53-55`; `configuration.md:140-141`, `:183-185` |
| **F8** | **No repo guard exists** for the realm artifact (validity / naming / import-path agreement), and there is no AppHost test project. The natural home is the existing repo-artifact guard precedent. | `tests/SchoolCollab.ArchitectureTests.Unit/SeedCsvArchitectureTests.cs:155-165` (`FindSeedDataDir` walks up from `AppContext.BaseDirectory`) |

**Deliberately kept (not a defect):** `Auth__Keycloak__Authority` is injected as an endpoint-reference env expression (`:79`) instead of named-client service discovery — deliberate per ar-20 plan decision 4, and it sidesteps the `RequireHttpsMetadata`/`https+http://` mismatch. Do not "fix" this without deciding D1.

**Also confirmed still-live (ar-20 residual, unchanged):** the AC#4 *create* leg cannot attribute to `…0003` while no service-to-service token forwarding exists → expect **409 `UnknownTeacher`** (accepted P1-5 option (b)).

### Key decisions (pinned — do not re-litigate)

| # | Decision | Resolution (owner-pinned 2026-09-18) |
|---|---|---|
| **D1** | **Hardening shape = (a) MINIMAL, on the raw container.** Concretely: enable Keycloak's health/readiness signal and gate the hosts on it (`KC_HEALTH_ENABLED=true` + the management port + a health check + `.WaitFor(keycloak)` on **all four** hosts that receive `Auth:Keycloak:*` = assignments-api, students-api, settings-api, admin); add an explicit `targetPort` (8080) to the Keycloak http endpoint; rename the realm import file to the Keycloak-conventional `<realm>-realm.json` and strip its `//` comments (moving the dev-only notes into the AppHost comment + `configuration.md` §4); fix the misleading data-volume comment. **DO NOT adopt `Aspire.Hosting.Keycloak`** — that is deferred; F5 stays an external observation only. Tier settles to **Tier 3 lean** by owner choice; plan review (step 2b) is mandatory and runs on the reviewer model. | Owner |
| **D2** | **Both Keycloak secrets stay Aspire secret parameters with NO committed default.** Do not add a default. `Directory.Packages.props` and `appsettings.json` are expected **unchanged**. | Owner |
| **D3** | **Stay volume-less: do NOT add a data volume.** Reword the `:63-65` comment to state that the realm re-imports on every container recreate (the dev-correct behaviour). | Owner |
| — | Also pinned: no `.razor`/`.razor.css`/`.css`/`.js`; no contract-shape change; no EF migration; no production deploy story (that is Phase 5 WS-G); `DisableOIDCAuth=true` stays the dev/CI default; no new public API beyond the one new ArchitectureTests guard file. | Owner |

#### Preserved from the scoped state (2026-09-18, pre-pinning — history, superseded by line 1 + the table above)

- **Provider profile:** pi `ollama` (round default; `clinepass` not selected).
- **Tier:** *not yet pinned* — decision **D1** settles it (see "Open decisions"). No `.razor` in scope → any Tier 3 for this round is **lean** (no UI tester; the UI trigger is diff-derived and there is no UI diff). Plan review (step 2b) is mandatory on every Tier 3 round **regardless of lean/full**: on **lean** rounds — and on all light rounds — it runs on the round's **reviewer** model; only a **full** Tier 3 round uses `glm-5.3:cloud`. Do not dispatch the worker with a plan-review model mismatch.
- **Entry criteria:** owner pins **D1–D3** (and, for D1(b)/(c), verifies the integration package id + API surface on first restore — see F5). **Base pinned: `74a42da1`** — the ar-20 merge commit (PR #244, squash-merged 2026-09-18); the ar-21 branch is cut and checked out at that SHA (local only, not pushed).
- **Program position:** D-6 identity follow-up, between ar-20 and Phase 5 (WS-G).

**Open decisions — RESOLVED (original table preserved):**

| # | Decision | Options | Consequence |
|---|---|---|---|
| **D1** | Hardening shape (settles the tier) | (a) minimal, on the raw container: F1–F4 fixes only; (b) adopt `Aspire.Hosting.Keycloak` + `WithRealmImport` (+ decide the client-side question); (c) (b) plus a production realm-import story. **Entry condition for (b)/(c):** verify the package id + API surface on first restore — F5 is externally sourced only, so do not pin (b)/(c) on an unconfirmed API inventory | (a) design settled → **Tier 2 light**; (b)/(c) design open, AppHost restructure → **Tier 3 lean** (plan review mandatory) |
| **D2** | Client-secret dev default | keep both secrets default-less (status quo) / give `keycloak-client-secret` the pinned dev-only literal as a non-secret default / adopt the integration's generated admin-password parameter | dev friction vs secret hygiene; touches `Program.cs`, `appsettings.json`, `configuration.md` §2/§4 |
| **D3** | Persistence | declare a real `WithDataVolume()` and document the realm-edit wipe procedure / stay volume-less and state "re-imports on every recreate" | changes F6's comment from a false instruction into a documented procedure |

**Proposed execution (preserved):** 1. Tier per D1; branch cut from `main`; orchestrator plan (Tier 3) → **plan review** → worker → frozen patch → diff review → re-verify (→ UI tester only if a `.razor` appears, which is not expected). 2. Parent runs the **cold-start + AC#4** host steps (only the parent has host access), records verbatim output, then transcribes acceptance.

### Scope

**In scope** — `src/AppHost/SchoolCollab.AppHost/**` (container block, `WireKeycloakAuth`, csproj copy item; `appsettings.json` expected **unchanged** per D2), the realm import file (rename + comment strip), `documents/configuration.md` §2/§4, one new ArchitectureTests guard, and the **manual AC#4 run** recorded verbatim in this doc's `## Acceptance`.

**Out of scope (explicit).** No `.razor`/`.css`/`.js`. No contract-shape change. No migration. No production deploy story for the realm (the `aspire publish` realm-import caveat is a **Phase 5 WS-G / deploy-docs** item, not this round). No change to `DisableOIDCAuth=true` as the dev/CI default. These ar-20 identity residuals stay out and are candidates for a later identity-hardening round:

- submission-review path not identity-threaded;
- `DisableOIDCAuth` **split-brain** risk (handlers read the flag per request while the scheme is fixed at startup) — hardening = assert the principal came from TestAuth, or read the startup switch;
- no cross-tenant `teacher_id` validation on approve/reject/review/override;
- ward `/students` + Admin UI bearer-only after a flip;
- dev `staff_number` NULL; no route-level 403/409 test;
- `TestAuthShape_…` remains **partly vacuous** (it can pass by construction — asserts a shape it does not independently observe);
- service-to-service token forwarding (the AC#4 create-leg deviation).

### Expected files (worker implements EXACTLY this table — nothing more)

| # | Path | Change |
|---|---|---|
| 1 | `src/AppHost/SchoolCollab.AppHost/Program.cs` | Keycloak block only: (a) `.WithEnvironment("KC_HEALTH_ENABLED", "true")` **[the env name and Keycloak's management-port semantics are EXTERNAL-UNVERIFIED]**; (b) a management endpoint on the Keycloak health port (`.WithHttpEndpoint(targetPort: 9000, name: "management")` — **no pinned host port**: Aspire assigns an ephemeral host port, so the endpoint is still proxy-exposed) and an HTTP readiness health check bound to it (`.WithHttpHealthCheck(path: "/health/ready", endpointName: "management")` **[the exact overload in the pinned Aspire 13.4.5 is EXTERNAL-UNVERIFIED — adapt minimally if it differs and say so in the worker report]**); (c) `.WithHttpEndpoint(targetPort: 8080, name: "http")` — **targetPort only, host port deliberately left to Aspire's ephemeral assignment** (the Authority is an endpoint-reference expression, so no stable host port is needed; this answers AC4's "pin deliberately" clause and is stated in the round notes); (d) the readiness gate: extend `WireKeycloakAuth` with `.WaitFor(keycloak)` (covers settings-api `:198`, students-api `:236`, assignments-api `:263`) **and** add `.WaitFor(keycloak)` to the admin inline block (`:319-321`) — all four hosts gated; (e) `keycloakRealmPath`, the bind-mount target, **and the Keycloak block header comment at `:45`** (which still names the old file — it is inside "Keycloak block only") renamed to `school-collab-realm.json` (bind target `/opt/keycloak/data/import/school-collab-realm.json`); (f) F6 comment reworded per D3; (g) the realm file's eight dev-only `//` note lines move into the AppHost Keycloak block comment (DEV-ONLY credential warning preserved verbatim in intent). If the exact Aspire 13.4.5 overload for the health check differs from the above, adapt minimally and note it in the worker report; STOP if no HTTP health-check API exists on the container builder. |
| **1b** (CONDITIONAL — see AC6) | `src/AppHost/SchoolCollab.AppHost/Program.cs` (the **migrator** resource block, `:175-184`) | **Applied ONLY IF the parent's pre-flight check shows the migrator is NOT running in `Development`** (the migrator-`Development` observation is **AC6**; the cold-start half is AC9) (i.e. `DevIdentitySeeder` no-ops): set the environment value on the migrator resource so the seeder runs. This row exists so AC6's fix is *inside* the table rather than a violation of "nothing more"; with no failing evidence it MUST NOT be applied. If the environment cannot be set from the AppHost at that point, STOP and report (AC6's STOP path). |
| 2 | `src/AppHost/SchoolCollab.AppHost/school-collab-realm.json` | **NEW (rename of `realm-school-collab.json` — git must record it as a rename).** Strip the eight `//` comment lines (`:1-9`); **all other content byte-identical** (AC1 semantic preservation). No filename literal inside the file. |
| 3 | `src/AppHost/SchoolCollab.AppHost/SchoolCollab.AppHost.csproj` | Copy item `:20` → `<None Include="school-collab-realm.json" …>`; the ar-20 comment above it updated (filename + pointer to the AppHost comment for the dev-only notes). |
| 4 | `tests/SchoolCollab.ArchitectureTests.Unit/AppHostRealmImportArchitectureTests.cs` | **NEW** — the AC2 guard (strict JSON; filename derived from the file's own `realm` property; the three agreement sites + the `/opt/keycloak/data/import/` prefix). Follow the `SeedCsvArchitectureTests` precedent (`FindSeedDataDir` walk-up from `AppContext.BaseDirectory`; that precedent **throws** when discovery fails — `:153-167`); **the discovery helper must throw on ZERO or MULTIPLE realm candidates**, so a renamed file cannot pass by silently finding nothing; `FluentAssertions` + MSTest like its neighbours; read-only file access, no project references added. |
| 5 | `documents/configuration.md` | §4: update the realm-filename reference at `:283` to `school-collab-realm.json`; add the readiness-gate note (all four `Auth:Keycloak:*` hosts wait on the keycloak health signal); state the re-import-on-every-recreate behaviour per D3; carry the realm file's dev-only credential warning (moved from the JSON comments). §2: **no parameter rows change** (D2 = status quo) — touch §2 only if a §4 sentence forces a one-line correction. |
| 6 | `Directory.Packages.props` | **Expected UNCHANGED** (D1(a) — no new package). Listed so its untouched state is checkable in the diff. |
| 7 | `src/AppHost/SchoolCollab.AppHost/appsettings.json` | **Expected UNCHANGED** (D2 — no default added). Listed so its untouched state is explicit; touching it = STOP and report. |
| 8 | Round artifacts | `documents/rounds/round-ar-21-keycloak-apphost-hardening.md` (this doc — orchestrator-only; the worker NEVER edits it) + `documents/rounds/.ar-21-worker-report.md` (worker-written) + `diffs-ar-21-keycloak-apphost-hardening.patch` (parent-written post-worker). |

**Expected-files count:** 5 worker-touched files (rows 1–5, one of them NEW-via-rename, one NEW file) + **1 conditional row (1b, applied only on AC6 evidence)** + 2 expected-unchanged guard rows + round artifacts.

**Execution order (binding — resolves the freeze-timing finding from the plan review):** worker implements rows 1–5 → **the parent runs the AC9 pre-flight cold start and the AC6 migrator-`Development` check BEFORE the patch is frozen** → if AC6 fails, worker applies row 1b (a bounded rework pass) and build/tests re-run → **then** the parent freezes `diffs-ar-21-keycloak-apphost-hardening.patch` → diff review → re-verify. The patch is never frozen before AC9/AC6, so the diff review always sees the final file set and the AC2 revert-probe evidence cannot go stale. **The row-1b pass is an authorized bounded re-engagement of the SAME worker — the 30-minute cap applies per pass, not per round — and the worker must NOT idle-wait for the parent's AC9/AC6 run: it stops and reports, and is re-engaged only if AC6 actually fails.**

### Worker task spec (compact)

1. **Read `.github/copilot/rules/dotnet-best-practices.md` BEFORE the first `.cs` edit** (mandatory repo rule; the ArchitectureTests suite enforces its checkable subset).
2. Implement **exactly** the expected-files table above — every row's change, and **nothing more**. No speculative scaffolding, no placeholder comments, no drive-by fixes.
3. Build + tests: `dotnet build SchoolCollab.sln` (must be **0 errors**) and `dotnet test` on the affected projects — minimum set: **`tests/SchoolCollab.ArchitectureTests.Unit`** AND **`tests/SchoolCollab.Core.Tests.Unit`** (its `Architecture/CrossModuleWiringTests.cs` parses `Program.cs` — AppHost source read at `:62`, declared-resource-name parsing at `:230-256` — so it is in the blast radius of any AppHost edit) **plus** a full-matrix `dotnet test` if time allows; report per-suite counts.
4. **The new guard test must be REVERT-PROVEN**: one revert per failure mode — (a) re-introduce a `//` comment into the realm file, (b) rename the realm file, (c) break one of the three filename-agreement sites (e.g. the csproj `<None Include>`), **(d) mutate ONLY the `realm` property value in place** (the one probe that distinguishes a *derived* name from a hardcoded literal) — and **each revert must produce exactly its expected failure** (name the failing test). Revert each probe afterwards. A guard that cannot be broken this way is vacuous and fails AC2.
5. **Test-output rule (exact):** exactly **one** `dotnet test tests/<X> 2>&1 | grep -E "^\s*failed |total:|failed:" | head -40` per project per read; **at most 2 attempts** per result read; if output truncates, redirect to a file and read the tail **once**; **no ad-hoc pipelines; no `zz*.log` files**.
6. **Repo-scoped searches ONLY** — **NEVER `find /`**, and never scan `obj/`, `bin/`, or `~/.nuget` XML docs (they flood context and have burned previous workers).
7. **30-minute cap:** at 30 minutes, checkpoint — write what is done into the worker report, mark the remainder, and stop. Do not silently overrun.
8. **Worker report is MANDATORY and the worker writes it itself** to `documents/rounds/.ar-21-worker-report.md` (files changed, build/test results with per-suite counts, the **four** revert-probe results, residuals). **The worker must NEVER edit the round doc** (`round-ar-21-keycloak-apphost-hardening.md`) — the parent owns it.
9. **STOP and report** if: any `.razor`/`.razor.css`/`.css`/`.js` edit seems needed; the fix would exceed the expected-files table (incl. touching rows 6–7); or the Aspire 13.4.5 hosting API lacks the health-check surface row 1 assumes.

### Reviewer task spec (compact)

Static — **never builds, never tests, never writes**. Reads the frozen patch `documents/rounds/diffs-ar-21-keycloak-apphost-hardening.patch` plus the changed files' surrounding code (re-read `Program.cs`, the csproj, the realm file, `configuration.md` §2/§4 around the touched lines). Verify **AC1–AC4 and AC7–AC8** and the **falsifiability of the guard** (is the filename derived from the file's own `realm` property, not a hardcoded literal; does the guard cover all three agreement sites individually). Explicitly judge **the readiness gate — "does it actually gate?"**: no `.WaitFor` on a resource that is not the Keycloak resource; the health-check path/endpoint name matches the declared endpoint; all four consuming hosts gated. Explicitly judge the **filename-agreement trio** (bind-mount target, `keycloakRealmPath`, csproj copy item — plus the `/opt/keycloak/data/import/` prefix). Include the **best-coding-practices check**: no overwrites outside plan scope; repo skills honored (dotnet-best-practices, section/AGENTS rules); readable (comments accurate, no dead code). State the honesty position on AC3/AC4-runtime/AC5/AC9/AC10 (runtime-only — see below). Return **only the REVIEW block** (verdict + per-criterion findings), formatted for the parent to paste into `## Review`.

### Acceptance criteria

Runtime-only set, stated plainly: **AC3's cold-start clause, AC4's `.well-known`/issuer clause, AC5, AC6, AC9, AC10 are runtime-only.** CI cannot run Keycloak (owner decision (b), ar-20), so they are evidenced **only** by the parent-run manual verification (AC9/AC10) recorded in `## Acceptance` — never inferred from unit tests or from the code shape. If the run is not performed, that is the headline residual for all of them.

1. **Realm artifact.** The realm import file is **strict JSON** (zero `//` or `/* */` anywhere in the file) and named **`school-collab-realm.json` — derived from its own `realm` property** (`school-collab`), so the name cannot drift with a renamed realm; the csproj `<None Include>` item, `keycloakRealmPath`, and the container bind-mount path all agree with that derived name, and the bind-mount target sits under `/opt/keycloak/data/import/` (the only dir `--import-realm` scans). **Semantic preservation is part of this AC** (AC 5 may be skipped, so this is the only semantic guard): realm name + `enabled` + `sslRequired` (+ `registrationAllowed`/`loginWithEmailAllowed`), the `school-collab-client` client with `directAccessGrantsEnabled` + `standardFlowEnabled`, the `dev-teacher` user with its password credential and array-valued attributes, and all five protocol mappers — the audience mapper plus the four usermodel-attribute claim mappers (`tenant_id`/`tenant_name`/`tenant_type`/`teacher_id`) — survive the rewrite with their behaviour intact, checkable from the frozen patch. No dev credential or dev-only warning is lost — they move to the AppHost comment + `configuration.md` §4. Git records the change as a **rename**, not delete+add.
2. **Guard test.** A guard test file exists in `SchoolCollab.ArchitectureTests.Unit` (expected: `AppHostRealmImportArchitectureTests.cs`) that **fails** if the realm file is not strict JSON, or if its filename does not equal `<realm>-realm.json` derived from the file's **own** `realm` property (not a hardcoded literal — a literal passes vacuously if the realm is renamed), or if **any one** of the three agreement sites disagrees with that derived name (bind-mount target, `keycloakRealmPath`, csproj copy item — a stale copy item otherwise surfaces only during the AC 5 run), or if the bind-mount target leaves `/opt/keycloak/data/import/`; and it must **throw** on zero or multiple realm candidates (a rename must not pass by silently finding nothing). Discrimination **proven by revert, one revert per failure mode** (re-introduce a comment; rename the file; change one of the three sites; **mutate only the `realm` property in place — the only probe that distinguishes a derived name from a hardcoded literal**) → each produces exactly its expected failure.
3. **Readiness gate (code-verifiable clauses).** The keycloak container sets `KC_HEALTH_ENABLED=true`, declares the management endpoint (targetPort 9000), and binds an HTTP readiness health check (`/health/ready`) to it; **every** Keycloak-consuming host — settings-api, students-api, assignments-api, **and admin** — `.WaitFor(keycloak)`s. Falsifiable from the frozen patch: a missing env/endpoint/check, or any one of the four hosts ungated, fails this AC. The cold-start clause (no host logs an OIDC metadata / issuer failure) is **runtime-only** → evidenced only by AC9.
4. **Explicit targetPort.** The Keycloak http endpoint declares an explicit `targetPort: 8080`; whether a host `port` is pinned is decided deliberately (this plan pins **targetPort only** — see expected-files row 1) and the reasoning is stated in the round notes. Falsifiable from the patch: no `targetPort` on the http endpoint, or an unstated port decision, fails. The runtime clause — the resolved `Auth:Keycloak:Authority` reaches `/realms/school-collab` and `GET {Authority}/.well-known/openid-configuration` returns a matching `issuer` — is **runtime-only** → evidenced only by AC10.
5. **AC#4 — the real end-to-end run.** With `Parameters__keycloak_admin_password` + `Parameters__keycloak_client_secret` supplied and `DisableOIDCAuth=false`: password-grant a token for `dev-teacher` → `GET /assignments` returns the `Dev School` tenant's data (no 401/403). Expected, accepted deviation: the *create* leg answers 409 `UnknownTeacher` unless forwarding is added. **Executed and recorded verbatim** (commands + responses) by the parent in this doc's `## Acceptance` (AC10) — runtime-only.
6. **Dev-seed prerequisite confirmed at runtime.** The migrator runs in `Development` so `DevIdentitySeeder` executes and rows `tenant …0002` / `teacher …0003` exist before step 5; the migrator log line confirming `Development` is recorded before attempting AC 5. **Contingency (now owned by expected-files row 1b, CONDITIONAL):** if the migrator's environment is not `Development` (it has no `launchSettings.json`/`appsettings.json`), the fix is in scope **only** as the AppHost-side migrator-block environment value of row 1b — applied only on that failing evidence, before the patch is frozen (see the execution-order note under Expected files); if the environment cannot be set there, STOP and report rather than adding an undeclared file. Runtime-only.
7. **Build + tests.** `dotnet build SchoolCollab.sln` → **0 errors** (plain); test matrix → **0 failures** — at minimum the affected projects — **`SchoolCollab.ArchitectureTests.Unit`** for the guard **and `SchoolCollab.Core.Tests.Unit`** (its `Architecture/CrossModuleWiringTests.cs` parses the AppHost source at `:62` and the declared-resource-name list at `:230-256`, so a `Program.cs` edit is in its blast radius) — with the full matrix at acceptance. The **four** revert probes of AC2 are reported (test name → expected failure observed) in the worker report.
8. **No production behaviour change / config hygiene.** While `DisableOIDCAuth=true` nothing changes (the CI path stays container-free — falsifiable: all five hosts' `appsettings.json` untouched in the diff); no committed secret beyond the already-labelled dev-only realm literals (falsifiable: diff scan); any flag/parameter touched is mapped in `documents/configuration.md` §2 in the same round (repo rule — expected: none touched, per D2).
9. **Parent-run cold-start verification (runtime-only).** The parent starts the AppHost against a **fresh** keycloak container (no prior container state), and records in `## Acceptance`: (a) evidence the keycloak health check turned healthy **before** the four gated hosts started; (b) that no host log contains an OIDC metadata / issuer failure on that cold start. If the health check never turns healthy, the declared contingency applies (see Risks) — apply it, re-run, record both states.
10. **Manual AC#4 run recorded (runtime-only).** The parent executes the `configuration.md` §4 run (both secrets via user-secrets/env, `FeatureFlags__FEATURE__DisableOIDCAuth=false`), records commands + responses **verbatim** in `## Acceptance`, and marks the AC 5 outcome (including the expected 409 create-leg deviation) explicitly. An unexecuted run is recorded as the headline residual — never left silently unmentioned.

### Reviewer acceptance criteria

- Statically verify AC 1–4 and 7–8 from the frozen patch; confirm **each new guard is discriminating** by revert (not merely present) — and specifically that its discovery helper **throws on zero/multiple realm candidates** (not a silent no-op) and that **probe (d)** (mutate only the `realm` property) evidence is present, since that is the only probe distinguishing a derived name from a hardcoded literal.
- Confirm the readiness gate actually gates: no `WaitFor` on a resource that is not the Keycloak resource; the health check path/endpoint name matches the declared endpoint; **all four** consuming hosts gated (settings, students, assignments, admin).
- Confirm the filename-agreement trio: bind-mount target, `keycloakRealmPath`, csproj copy item — each individually guarded, name derived from the realm property, import-dir prefix asserted.
- **Honesty statement on the runtime-only set required:** AC 3's cold-start clause, AC 4's `.well-known`/issuer clause, AC 5, AC 6, AC 9, AC 10 can only be evidenced by the parent-run manual verification — CI cannot run Keycloak (owner decision (b), ar-20). If the run is not performed, say so as the headline residual for that set; never infer any of them from unit tests or from the code shape.
- Best-coding-practices check: no overwrites outside plan scope; repo skills honored (dotnet-best-practices; AGENTS.md communication/build rules); readable (comments accurate — no surviving "data volume" fiction, no stale filenames).
- Confirm no out-of-scope file moved (`.razor`/contracts/migrations/`Directory.Packages.props`/`appsettings.json`).

### Risks / residuals

- **AC#4 depends on the operator's two secrets** and a Docker pull of the pinned Keycloak image; disk headroom is the ar-19 ENOSPC precedent (check free space before starting host work).
- **ROPC / password grant** (`directAccessGrantsEnabled` in the realm) is exactly what AC#4 exercises; it is deprecated direction-of-travel for OAuth. If the adopted image path warns or breaks, the fallback is a service-account / authorization-code+PKCE path — which changes the AC#4 shape (record, do not silently substitute).
- **Keycloak 26 dev-mode management-interface bind (health-check pitfall, declared contingency — every name in this bullet is EXTERNAL-UNVERIFIED):** in `start-dev` the management interface (9000) may bind localhost inside the container, so a host-side/Aspire HTTP health check against it can never turn healthy. If AC9 shows that, the **pre-authorized fix** is to serve health on the main HTTP interface instead (`KC_HTTP_MANAGEMENT_HEALTH_ENABLED=false` + the health check bound to the `http` endpoint's `/health/ready`) — apply it, note it in the worker report, and re-read the diff review against the re-frozen patch. A **targeted management-host bind value** is an equally acceptable variant if the reviewer confirms it stays inside plan scope. This bullet is the declared fallback, **not** an exhaustive enumeration: do not invent a third *silent* shape — record whatever is applied.
- **F1 cannot be proven by unit test** — the only discriminating evidence is a cold-start run; state that plainly rather than claiming coverage.
- **D1(b) risk (deferred with the decision):** adopting the integration moves `--import-realm` semantics behind `WithRealmImport` (dev-time only) — a production realm story then has to exist somewhere (image bake or init service). Out of scope; Phase 5 WS-G.

## Worker Report

_— not started —_ (worker writes `documents/rounds/.ar-21-worker-report.md`; the parent transcribes the summary here)

## Review

### SCOPE REVIEW of this doc (2026-09-18, pre-D1) — REWORK, all findings landed

`219ddbed`, `ollama/glm-5.3:cloud`, read-only — a **scope review of this SCOPED round doc**, requested by the owner before D1–D3 were pinned. It is **NOT** the step-2b plan review of an orchestrator plan; that still happens at round start (SKILL step 2b) against the orchestrator's plan, on the tier-appropriate model.

Fact-check result: F1, F2, F4, F6, F7, F8 **CONFIRMED** with file:line evidence; F3's in-repo half confirmed, its Keycloak naming convention UNVERIFIED-EXTERNAL; **F5 UNVERIFIED-IN-REPO** (circular provenance — only owner-authored docs mention it).

| Sev | Finding | Fix landed in this pass |
|---|---|---|
| P1-1 | Plan-review **model misstated**: the doc asserted the round is lean *and* that step 2b runs under `glm-5.3:cloud` — lean uses the round's **reviewer** model; `glm-5.3:cloud` is **full-Tier-3 only** | Tier bullet rewritten |
| P1-2 | **F5 stated as fact with zero in-repo substantiation**, yet D1(b)/(c) and D2's third option are decided from its API specifics | F5 relabelled EXTERNAL + entry condition added to D1 |
| P2-3 | ar-20's `TestAuthShape_…`-partly-vacuous residual was silently dropped (in neither list) | added to the out-of-scope list |
| P2-4 | AC1/AC2 guarded JSON validity + naming but **not realm semantics** — a comment-strip that mangled mapper config would pass everything when AC 5 is skipped | semantic-preservation clause added to AC1 |
| P2-5 | AC2 under-specified: a hardcoded filename literal passes vacuously, and the csproj copy site was unguarded | AC2 rewritten (derive `<realm>` from the file; guard all three sites) |
| P2-6 | AC3/AC4 runtime clauses inherit AC5's manual-run dependency, but the honesty statement covered only AC5 | honesty statement extended to AC3/AC4 |
| P2-7 | AC6 had no in-scope fix path if the migrator env is not `Development` | contingency + STOP-and-report added |
| nits | F1's cite list omitted the assignments/admin call sites; the provenance entry is a paragraph, not a numbered row; `Directory.Packages.props` was missing from the in-scope sentence; base SHA unpinned; AC4's mailpit precedent ambiguous (host `port` vs `targetPort` only) | all landed |

**Endorsed by the review (kept):** AC2's per-failure-mode revert proof and the reviewer-AC "the gate actually gates" clause are genuine anti-vacuity devices; the tier anchoring on D1, the no-UI ⇒ lean ⇒ no-tester prediction, and the round-doc policy handling (one file now, patch written post-worker, tier-appropriate sections, nothing durable here) are correct.

### PLAN REVIEW (step 2b) — `c5091c01`, `ollama-cloud/deepseek-v4.1-flash`, read-only

Dispatched 2026-09-18T10:48:57Z, settled 10:52:11Z (3m14s). Judge target: the `## Plan` above plus the seams it cites.

**Verdict: REWORK — 2 P1, 6 P2, all four gates clean** (`UI: none — contract: none — migrations: none — secrets: none`).

```
PLAN REVIEW
Verdict: REWORK
P1: AC6 contradicts its own worker contract — expected-files row 1 restricts Program.cs to "Keycloak block only" (a)–(g) and the table header says "implements EXACTLY this table — nothing more", while worker step 9 says STOP if the fix exceeds it; a conditional migrator-block WithEnvironment (Program.cs:175-184) is therefore simultaneously in scope (AC6) and forbidden.
P1: AC2's "derived from the file's own realm property, not a hardcoded literal" is not falsifiable by the plan's own device — the three reverts (comment; rename; one agreement site) are all passed by a hardcoded literal; none mutates the realm property.
P2: row 1(a)/(b) and the Risks contingency assert Keycloak/Aspire externals as fact with no EXTERNAL-UNVERIFIED tag (KC_HEALTH_ENABLED, port 9000, /health/ready, KC_HTTP_MANAGEMENT_HEALTH_ENABLED=false, WithHttpHealthCheck(path:, endpointName:)); namespace reachability IS in-repo-sound, but the API's existence in the pinned Aspire 13.4.5 and the Keycloak semantics are not; the contingency is also presented as exhaustive.
P2: row 1(b) calls the management endpoint "no host port; internal-only" — an Aspire container HTTP endpoint declared without `port` is still proxy-exposed on the host (mailpit precedent, configuration.md §2/:289) → reword to "no pinned host port (ephemeral host port)".
P2: Risks/AC9 contingency timing — applying the fallback during acceptance is after the freeze + diff review, so "the reviewer judges it within plan scope" is unachievable and AC7's revert-probe evidence goes stale → run the parent cold start BEFORE freezing, or add a re-freeze + re-review step.
P2: AC7 names ArchitectureTests.Unit as "the only project the diff touches" — but Program.cs is parsed by Core.Tests.Unit/Architecture/CrossModuleWiringTests.cs:62 and :230-256 → name Core.Tests.Unit in the minimum set or state why its AppHost parser is unaffected.
P2: AC2/row 4 never require the guard to fail loudly on zero/multiple realm candidates; the cited precedent throws (SeedCsvArchitectureTests.cs:153-167) → without it, revert probe (b) can pass by silently finding nothing.
P2: row 1(a)–(g) does not name the Keycloak header comment at Program.cs:45, which still reads realm-school-collab.json, although the reviewer AC demands "no stale filenames".
Gates: UI: none — contract: none — migrations: none — secrets: none (D2 intact: no default added, rows 6–7 expected-unchanged; realm dev-only literals unchanged and still labelled; CPM untouched per D1(a))
Acceptance honesty: AC2's "derived from the file's own realm property" clause cannot fail under the three listed revert probes (see P1) — a literal-based guard satisfies all three. All other AC2 clauses (strict JSON, three agreement sites, import-dir prefix) can fail against the pre-fix tree, so the guard itself is discriminating. AC7 ("0 failures") and AC8's diff-scan clauses are regression/hygiene checks that can never fail against pre-fix code — labelled as falsifiability statements, not discriminating tests. The runtime-only set is honestly labelled and correctly attributed to the parent-run manual verification; no runtime claim is inferred from code shape or unit tests. No pinned decision D1–D3 is contradicted or silently re-opened; no out-of-scope surface is touched.
```

*(verbatim reviewer block as returned; full text also in the run log `subagent-log-c5091c01-…md`)*

| Sev | Finding | Fix landed by the parent in this pass |
|---|---|---|
| P1-1 | AC6 vs the expected-files table (the fix was in scope *and* forbidden) | **new conditional row 1b** (migrator-block env, applied only on failing AC6 evidence) + AC6 reworded to point at it + a binding **execution order** (AC9/AC6 run *before* the freeze) |
| P1-2 | the derived-name clause had no discriminating probe | **probe (d)** added to worker step 4 and AC2: mutate **only** the `realm` property in place → the derived-name assertion must fail |
| P2-1 | externals asserted as fact | `EXTERNAL-UNVERIFIED` tags added to row 1(a)/(b) and the Risks contingency; "do not silently invent a third shape" softened to "do not invent a third *silent* shape — record it"; a targeted management-host bind value admitted as an acceptable variant |
| P2-2 | "no host port; internal-only" was wrong | reworded to "no pinned host port (Aspire assigns an ephemeral host port; still proxy-exposed)" |
| P2-3 | contingency/freeze timing | resolved by the binding **execution-order** paragraph (AC9 + AC6 before the freeze; build/tests re-run if row 1b lands; re-read diff review against the re-frozen patch) |
| P2-4 | `Core.Tests.Unit` missing from the minimum test set | AC7 + worker step 3 now name it, with the `CrossModuleWiringTests` `:62`/`:230-256` reason |
| P2-5 | the guard could pass by finding nothing | row 4 + AC2 now require discovery to **throw** on zero/multiple candidates (precedent `SeedCsvArchitectureTests:153-167`) |
| P2-6 | stale filename in the header comment at `Program.cs:45` | folded into row 1(e) |

#### Re-read after the parent's revision (step 2b, ≤1 iteration) — `8c71316a`, `ollama-cloud/deepseek-v4.1-flash`, read-only

**Verdict: ACCEPT — 0 P1.** P1-1 and P1-2 CLOSED (with the closing text quoted back per finding), all six enumerated P2s CLOSED, and all four gates re-confirmed clean. The re-read independently verified the in-repo premises behind the fixes: row 1b's migrator chain at `Program.cs:177-183`; `CrossModuleWiringTests.cs:62` reading AppHost source; the `SeedCsvArchitectureTests:164-165` throw precedent; `Program.cs:45` as the only stale-filename comment; and that a repo-wide grep for the old realm filename now returns only the three sites rows 1(e)/3/5 already own.

| Sev | New finding (introduced by the revision) | Fix landed |
|---|---|---|
| R2-1 | worker step 8 + AC7 still demanded **three** revert-probe results while step 4/AC2 mandate **four** — probe (d) would have gone un-evidenced at acceptance | both changed to **four**, so probe-(d) evidence is now demanded |
| R2-2 | the reviewer task spec / AC named only the "three agreement sites" and the derived-vs-literal check | reviewer AC now explicitly requires the **throw-on-zero/multiple-candidates** rule and **probe (d)** evidence |
| R2-3 | the binding execution order implies a post-observation row-1b pass, but nothing said the worker is re-engaged or how the 30-min cap applies | execution-order note now states row 1b is an **authorized bounded re-engagement of the same worker, cap per pass**, and that the worker must not idle-wait for AC9/AC6 |
| R2-4 | row 1b triggered on "the parental pre-flight cold start (AC9)" while the migrator-`Development` observation lives in **AC6** | row 1b now cites **AC6** and notes the cold-start half is AC9 |
| R2-5 | the ledger header said "7 P2" while the verbatim block enumerates **six** | header corrected to **6 P2** |

**Plan is cleared for worker dispatch:** no open P1, ≤1 plan-review iteration used, all findings from both passes landed in this single pre-dispatch revision.

### DIFF REVIEW — `6718692b`, `ollama-cloud/deepseek-v4.1-flash`, read-only, dispatched 2026-09-18

Reviewed against the plan and the then-frozen patch (5 files, +204/−28; re-frozen after the fixes below as +210/−28).

**Verdict: P2-only — 0 P1.** All three P2s are in the new guard test and **all three were landed by the parent** in one pass:

| Sev | Finding | Fix landed |
|---|---|---|
| P2-1 | `raw.Should().NotContain("//")` was an over-broad proxy for "strict JSON": it scans raw text, so any legitimate URL value (a future `redirectUris: http://localhost:3000/*`) would fail with the misleading "must be strict JSON" message while the file parses fine — and strict JSON is already enforced by `JsonDocument.Parse` | replaced with an explicit `JsonDocument.Parse(raw, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow })` assertion — the same rule Keycloak applies |
| P2-2 | `target.Should().StartWith("/opt/keycloak/data/import/")` was a **tautology** (the literal builds `target` two lines above) → zero coverage | deleted; the import-dir prefix is genuinely enforced by the preceding `Contain($"WithBindMount(keycloakRealmPath, \"{target}\")")` assertion, with a comment saying so |
| P2-3 | four redundant `using`s (`System`, `System.Collections.Generic` — unused, `System.IO`, `System.Linq`) duplicated the project's `ImplicitUsings` (`IDE0005` is warning-level) | removed |

**Re-proof after the fixes** (P2-1 changes *how* strict JSON is enforced, so its discrimination was re-established rather than assumed): re-introducing a `//` line into the realm file now fails **five** guard tests (the strict-JSON test plus the four name/agreement tests, because every helper parses strictly) — a stronger signal than the original single-test probe; the file was then restored **byte-identically** (`cmp` verified) and the suite returned to **26/0**.

**Verified by the review (statics):** AC1 (strict JSON; name derived from the file's own `realm` property; the three agreement sites `Program.cs:69`, the bind target `:76`, `csproj:23`; the rewrite is comment-only at `similarity index 84%`, so the realm/client/user/attributes/five-mappers semantics survive; git records a **rename**; the dev-only warning moved to `Program.cs:47-58` + `configuration.md:289-293`), **AC2** (derived name, one test per agreement site, `FindRealmFile` throws on zero *and* multiple candidates, and **probe (d) is genuinely falsifiable** — mutating only the `realm` property leaves the filename unchanged, so the derived-name assertion fails as required), **AC3's code clauses** (`KC_HEALTH_ENABLED` `:77`; management `targetPort: 9000` `:81`; health check path/name match `:82`; `.WaitFor(keycloak)` reaching settings `:220`, students `:258`, assignments `:285` + the inline admin gate `:346` = all four consumers, with no non-Keycloak `WaitFor`), **AC4** (`targetPort: 8080` `:86`, port decision stated), **AC7/AC8** diff-scan (5 files only; no UI/contracts/migration; `Directory.Packages.props` and `appsettings.json` absent from the patch; no new secret; `Program.cs:45` no longer names the old file and no live `realm-school-collab.json` reference survives).

**Best-practices: clean** — no overwrites (the diff is confined to the Keycloak block, `WireKeycloakAuth`, the admin gate, the csproj copy item, the realm comment strip and `configuration.md` §4); skills honored (XML doc on the new public type, file-scoped namespace, nullable-safe, read-only file access, no new project/package references); readable (the F6 data-volume fiction is gone, no stale filenames). The three P2s were its only deviations.

**Runtime-only by the review's own statement** (not verifiable from the patch): AC3's cold-start clause, AC4's `.well-known`/issuer clause, AC5, AC6, AC9, AC10.

## Runtime evidence (parent-run, decomposed) — 2026-09-18

**Context:** the AppHost itself could not be run in this session. Launched detached (no console) it stalls right after model validation: `Unable to allocate a network port for service '<x>'` for *every* resource including untouched ones (postgres, rabbitmq, cache, pgadmin), DCP never starts, the dashboard never listens on :15006, and no keycloak container is created. Two attempts reproduced it (`--launch-profile http`, `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`, Development env, both Keycloak parameters supplied as dev values: `Parameters__keycloak_admin_password=dev-only-ar21-admin`, `Parameters__keycloak_client_secret=dev-only-school-collab-client-secret`). This is an **execution-context blocker, not an ar-21 defect** — the warnings cover resources this round does not touch — so AC9's *orchestrated* cold start and AC10's API leg remain open. The substantive runtime claims were verified by driving the parts directly.

Command context: `quay.io/keycloak/keycloak:26.2` (pulled fresh), the **new** `school-collab-realm.json` bind-mounted at `/opt/keycloak/data/import/school-collab-realm.json`, container `ar21-kc` on host ports 18080→8080 and 19000→9000, `KC_HEALTH_ENABLED=true`, `start-dev --import-realm`.

| Check | Evidence | AC |
|---|---|---|
| Realm artifact **imports** | `INFO … Realm 'school-collab' imported` / `KC-SERVICES0032: Import finished successfully` (strategy `IGNORE_EXISTING`) | AC1 — the artifact is accepted by the server, not merely parseable |
| **Readiness signal exists** | `KC_HEALTH_ENABLED=true` + management port 9000 → `GET :19000/health/ready` = **HTTP 200 `{"status":"UP"}`** after ~36 s, reachable **from the host** | AC3's signal clause; the plan's EXTERNAL-UNVERIFIED flag on this claim is now closed, and the pre-authorised fallback (`KC_HTTP_MANAGEMENT_HEALTH_ENABLED=false` / main-interface health) is **not** required on this tag |
| Issuer shape | `/.well-known/openid-configuration` → `issuer = http://localhost:18080/realms/school-collab` — issuer equals the realm path the AppHost's Authority expression builds | AC4's runtime clause (partial: the AppHost-resolved Authority is still unexercised) |
| **Token mint + mappers** | ROPC `dev-teacher` (`grant_type=password`, `client_id=school-collab-client`, the committed dev-only client secret) → **HTTP 200**, `expires_in 300`, claims `aud=school-collab-client`, `azp=school-collab-client`, `tenant_id=…0002`, `tenant_name=Dev School`, `tenant_type=School`, `teacher_id=…0003` | AC5's token half; also empirically confirms ar-20's audience-mapper fix (the missing `aud` was that round's plan-review P1) |
| **Negative probe — the old file really was broken** | The pre-fix realm file (from `74a42da1`, comments intact) mounted under the **conventional** name fails: `ERROR: Unexpected character ('/' (code 47)): maybe a (non-standard) comment? (not recognized as one since Feature 'ALLOW_COMMENTS' not enabled for parser)` → `Failed to run import` → `Failed to start server in (development) mode` | **Revert-proof for F4/AC1**: the comment strip is load-bearing — ar-20's AC#4 could never have passed on the old artifact |

| **AC6 — migrator env gate, run TWICE** | **Production**: exit 0 + `Dev identity seed skipped: host environment 'Production' is not Development`. **Development**: exit 0 + `Unified migration service completed successfully`, after which `settings-db` holds `00000000-0000-0000-0000-000000000002 = Dev School` (and `…0003` in `students-db`). The gate is real and the Development path seeds. | AC6 — the owed observed run. Mechanism: the AppHost's `Properties/launchSettings.json` sets `DOTNET_ENVIRONMENT=Development` for both profiles and the migrator resource block sets no environment of its own, so the child inherits it |
| **AC5/AC10 — live bearer path against a real host** | Standalone `assignments-api` (dev DB + the live Keycloak + RabbitMQ; `Outbox__ExchangeName=assignments`, `FeatureFlags__FEATURE__DisableOIDCAuth=false`): **`GET /assignments` with the live `dev-teacher` token → HTTP 200 `[]`**; **no token → 401**; **bogus token → 401** — both 401s carrying `WWW-Authenticate: Bearer`. Discriminating triple: valid → 200, invalid → 401, absent → 401. | AC5 + AC10's API leg. **Honest limit:** the body is `[]` because `assignments-db` holds **zero** assignment rows for *every* tenant (`select tenant_id, count(*) from assignments` returns no rows), so this proves authentication and successful tenant resolution — **not** tenant-filtered data visibility. A SQL-seeded isolation probe is available if the owner wants it |

Still open (next parent steps): **AC9's orchestrated cold start** (blocked by the headless-AppHost condition described above; the Keycloak-side signals it depends on are proven in this section), the authoritative build/test pass, the frozen-patch diff review, and `## Acceptance`. **Conditional row 1b was NOT triggered** — AC6's observed run shows the seeder executes in Development, so the file set is final and the patch can be frozen without waiting for anything else.

## Acceptance

**Verdict: CLOSED — with one runtime residual (AC9's orchestrated cold start).** Parent-transcribed: this is a Tier 3 **lean** round, so the parent owns acceptance on the reviewer's verdict plus its own authoritative pass (the ar-17 precedent); no orchestrator-accept run and no UI tester (the deterministic UI trigger does not fire — no UI file is in the diff).

### Pipeline

orchestrator-plan (`0ec53da4`, `glm-5.3-flash`) → **PLAN REVIEW** (`c5091c01`, plus re-read `8c71316a`, `deepseek-v4.1-flash`) **REWORK → ACCEPT** → worker (`8a57bd12`, `deepseek-v4-flash-0731`) → patch frozen → **DIFF REVIEW** (`6718692b`, `deepseek-v4.1-flash`) **P2-only, 0 P1** → parent fixes pass (3 P2s + discrimination re-proof) → final patch.

### Acceptance criteria

| AC | Verdict | Evidence |
|---|---|---|
| 1 realm artifact | **PASS** | strict JSON; `school-collab-realm.json` derived from its own `realm` property; three agreement sites agree; **rename recorded**; semantics preserved (comment-only rewrite, `similarity index 84%`); **runtime: it imports** — `Realm 'school-collab' imported` / `Import finished successfully` |
| 2 guard test | **PASS** | derived name; per-site tests; throws on zero/multiple candidates; worker's four probes + the post-fix comment re-probe (5 failures) all discriminating; clean state 26/0 with the realm file byte-identical |
| 3 readiness gate | **PASS (code) · residual (runtime)** | `KC_HEALTH_ENABLED` + management 9000 + `/health/ready` health check + `.WaitFor(keycloak)` on all four consumers, verified from the patch; **the signal itself is proven live** (`/health/ready` → 200 `{"status":"UP"}` after ~36 s, host-reachable) — so the pre-authorised `KC_HTTP_MANAGEMENT_HEALTH_ENABLED=false` fallback was **not** needed. The *orchestrated* cold-start clause is the residual |
| 4 targetPort | **PASS (code) · partial (runtime)** | explicit `targetPort: 8080` with the decision stated; the issuer shape is verified live against a real container, but the AppHost-resolved Authority is unexercised |
| 5 AC#4 | **PASS (with a recorded limit)** | live `dev-teacher` token → **200 `[]`**; no token → **401**; bogus token → **401** (`WWW-Authenticate: Bearer`); the token carries `aud=school-collab-client`, `tenant_id=…0002`, `teacher_id=…0003`. The `[]` is because the dev DB has **zero** assignment rows, so tenant-filtered *data* visibility is not proven |
| 6 dev-seed prerequisite | **PASS** | the gate run twice on purpose: Production → `Dev identity seed skipped: host environment 'Production' is not Development`; Development → seeded (`…0002` Dev School, `…0003` in students-db) |
| 7 build + tests | **PASS** | parent (authoritative): build **0 errors** (112 warnings); ArchitectureTests **26/0**; Core.Tests.Unit **92/0**. Worker's full matrix: 2423 total / **3 pre-existing** failures in `CodedValueAIServiceLiveTests`, which call the real OpenRouter endpoint — network-dependent and outside this diff's blast radius |
| 8 no prod change / hygiene | **PASS** | diff scan: no UI, no contract shape, no migration; `Directory.Packages.props` and `appsettings.json` untouched; no new secret; `DisableOIDCAuth=true` stays the dev/CI default |
| 9 parent cold start | **RESIDUAL** | three AppHost attempts (detached, **new console**, and after clearing the stale `dcp`) stall identically right after model validation: 8 × `Unable to allocate a network port for service '<x>'` for **every** resource including untouched ones, DCP never progressing, the dashboard never listening; 0 listeners in 8000–8999 (so not port exhaustion) and a byte-identical log each time. Substitutes are recorded in `## Runtime evidence` |
| 10 AC#4 recorded verbatim | **PASS (decomposed)** | commands + responses recorded in `## Runtime evidence`; the AppHost-driven form is the AC9 residual |

### Final figures

| Check | Result |
|---|---|
| Patch | **5 files, +210 / −28** (325 lines), realm recorded as a rename, drift-free |
| Round-doc diff exclusions | dirty-at-start: `documents/solution/assignment-request-implementation-details.md`, `documents/solution/assignment-request-go-forward-breakdown.md` |
| Build | **0 errors** (plain `dotnet build SchoolCollab.sln`) |
| Matrix | ArchitectureTests **26/0** · Core.Tests.Unit **92/0** · worker full matrix 2423 with the 3 pre-existing OpenRouter-live failures |
| Gates | no UI · no contract shape · no migration · CPM untouched · no new secret · no committed default added (D2) · no data volume (D3) |

### Residuals (recorded)

1. **AC9's orchestrated cold start — headline.** The AppHost cannot be brought up in this environment (three reproducible attempts; see the AC9 row). Everything it would have evidenced is either **proven live** (the readiness signal, the realm import, the issuer, the token + mappers) or **code-verified** (the four `.WaitFor(keycloak)` gates and the endpoint/health-check wiring).
2. **AC5's data-visibility limit.** `200 []` proves authentication and tenant resolution, not tenant-filtered data — the dev DB has no assignment rows. A SQL-seeded isolation probe remains available if the owner wants that claim strengthened.
3. Carried from ar-20 and still out of scope: submission-review path not identity-threaded; `DisableOIDCAuth` split-brain; no cross-tenant `teacher_id` validation; ward `/students` + Admin UI bearer-only after a flip; dev `staff_number` NULL; `TestAuthShape_…` partly vacuous; no route-level 403/409 test; service-to-service token forwarding.
4. Deferred by D1: adopting `Aspire.Hosting.Keycloak` (F5) and a production realm-import story → Phase 5 WS-G.