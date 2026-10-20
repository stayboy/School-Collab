# Round — agent-harness optimisation (policy layer)

**Status:** CLOSED — pending PR
**Mode:** attended · solo authoring with a static diff gate (no runtime surface)
**Base:** `main` @ `ac779653`
**Branch:** `chore/agent-harness-optimisation`
**Decisions:** grill Q99 + Q101–Q107, all "as recommended" (2026-10-20)

## Plan

The measured problem, from this session's own event taxonomy:

| Category | Events | Addressed by |
|---|---|---|
| `Files tracked` (re-reads, repeated listings) | **4599** | R5 — nothing else touched it |
| `pi` (seat dispatch payloads) | 1445 | R5 (briefs carry paths) |
| `iteration-loop` | 630 | not examined |
| `Git operations` | 592 | R3 (mechanical delivery) |

Context-mode proved the *output* side works (**55.1K tokens / 30% saved**, 207 KB never
entering context over 38 calls). The input side had no baseline and no rule.

An independent review (`deepseek-v4-pro:0813`) endorsed R2 and R4 as written, **amended R1
and R3**, and rejected the framing that R1–R4 were the main win. Its amendments are folded in
below; each one closed a way this layer could have shipped a lie.

| # | Item | Amended shape actually built |
|---|---|---|
| **Q101** | Brief discipline (first) | `role-contracts.md` § "Brief discipline" + a standing rule in the skill's step 0: briefs carry **paths, not content**; **no seat re-opens a file it already read this session** |
| **Q102** | Runbook router | **No stored index.** `grep -l '^## Symptom' documents/runbooks/*.md` is the router; `## Symptom` written for the **3 runbooks that lacked it** (1 of 4 had it); `RunbookSymptomRouterTests` pins the anchor as a contract |
| **Q103** | CI-parity gate | `scripts/round/gates.sh` mirrors ci.yml's **filter legs verbatim**, always includes the repo-wide scanner, carries the non-optional MAUI serial flags, runs both Python portal suites, and prints an explicit **"NOT checked here"** list. `preflight-pr.sh` now **calls it** instead of defining a second, weaker leg |
| **Q104** | Mechanical delivery | `scripts/round/deliver.sh` stages an explicitly-named path set, proves nothing outside it was staged, refuses policy-layer paths, shows the change set, commits, **stops** — no push, no PR, no merge |
| **Q105** | Trash cadence | `documents/rounds/README.md`: keep open-PR rounds, the last 2 closed rounds, and any PARKED round; bulk-trash on arc close, in its own commit |
| **Q106** | PR shape | one policy-layer PR off `main` |
| **Q107** | Measurement | before/after recorded here; never a merge blocker |

## Implementation

| Path | Change |
|---|---|
| `scripts/round/gates.sh` | new — CI-parity gate (restore → Release build → unit / integration / cross-module-wiring legs → MAUI head → both portals), `--no-maui` / `--no-integration` / `--no-portals` |
| `scripts/round/deliver.sh` | new — stage/verify/commit, gated |
| `scripts/round/README.md` | new — why filters rather than a project list, why delivery stops before push |
| `scripts/preflight-pr.sh` | step 4 replaced by a call to `gates.sh` (one definition of "green") |
| `tests/SchoolCollab.ArchitectureTests.Unit/LocalGateCiParityTests.cs` | new — 5 tests: filter-set equality both ways, both portals, MAUI serial flags, "NOT checked here" present, preflight reuses the gate |
| `tests/SchoolCollab.ArchitectureTests.Unit/RunbookSymptomRouterTests.cs` | new — 2 tests: every runbook has `## Symptom`; the router anchor returns every runbook |
| `documents/runbooks/{dev-loop,fr58,merge-queue-*}.md` | `## Symptom` block each |
| `documents/rounds/README.md` | trash cadence |
| `.pi/skills/…/references/role-contracts.md`, `SKILL.md` | brief discipline + routing rule in step 0 |

**Deliberately not done:** `deliver.sh` does not push, open a PR, or merge (a script cannot
know whether a push or merge was authorized); `gates.sh` does not enumerate projects (13
`*.Tests.Unit` in the solution, and CI gates by filter across it).

## Verification

### The gate found three things on its first real run

| Finding | Verdict |
|---|---|
| `error CS5001: no static 'Main'` for `SchoolCollab.Students.Tests.Unit` in **Release** only | **stale `obj/Release`** (a half-written editor temp file was sitting next to the generated `MicrosoftTestingPlatformEntryPoint.cs`). Cleared → 0 errors. Debug had always passed, which is exactly why the Release leg earns its place |
| `CodedValueDropdown_WithSelectedId_MarksOptionSelected` failed 1× in 4348 | **flake, not a regression** — passed 3/3 in isolation, and `Settings.Tests.Unit` is untouched by this layer. Belongs to the known FluentUI `@bind-*:after` cascade family |
| `ChatProviderLiveTests` (2 tests) failed locally | **documented local-vs-CI divergence**: CI has no `Parameters:openrouter-api-key`, so they self-skip as Inconclusive (ci.yml records that the pinned model answers HTTP 400). This machine has a key, so they run. `gates.sh` classifies **exactly those two** as a divergence and still fails the leg on anything else |

That third one is the design point worth keeping: the gate names the divergence instead of
either failing red forever or silently filtering it out.

### Measured

| Command | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** (119 warnings pre-existing) |
| `gates.sh --no-maui --no-portals` | **exit 0** — restore ✔ · Release build ✔ · unit **4348 / 0 failed** ✔ · integration 148 total, 2 known-divergence only ✔ (reclassified) · wiring **2 / 0** ✔ |
| `RunbookSymptomRouterTests` + `LocalGateCiParityTests` | **7 / 7 passed** |
| `deliver.sh` negative | policy path + `main` both refused (exit 3) |
| `deliver.sh` positive | staged exactly the 10 named paths, 0 extras, no policy files |

### Q107 baseline (to be compared after the next round)

| Metric | Before | After |
|---|---|---|
| `Files tracked` | 4599 | *measured at the next round close* |
| `pi` | 1445 | *idem* |
| `git` hand-written brief lines per round | ~30 | `deliver.sh` + `gates.sh` |
| local gate legs | 1 (unit only) | 3 .NET + MAUI + 2 portals, with skips named |
| runbooks routable by symptom | **1 of 4** | **4 of 4** |

## Acceptance

Accepted by inspection against the settled Q99/Q101–Q107 answers — every item is a file, a
test, or a printed line, and each amendment from the review is traceable to a row above.
This layer has no runtime surface: no `.cs`/`.razor` behaviour change, no migration, no
contract change. Its risk is entirely "does it tell the truth about green", which is what
`LocalGateCiParityTests` and the "NOT checked here" list exist to answer.

## Follow-ups (not this layer)

- `Q100` — the parallel-worker fan-out edits still sit uncommitted in `.pi/skills/`; they
  depend on Q101's brief discipline and need their own decision.
- `preflight-pr.sh` steps 1/3/8 still auto-commit, auto-push and auto-merge, which contradicts
  the AGENTS.md merge policy (no merge without an explicit word). Pre-existing, flagged here,
  **not changed** — it needs its own decision.
- The residuals round: AppHost diagnosis (step 0 = runbook 2-minute elimination), one browser
  pass over B2b/B3b/Spec B, spec §5 + §8 doc corrections, drift guards, parked odds.
