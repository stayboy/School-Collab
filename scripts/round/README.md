# `scripts/round/` — the round harness

Two scripts, one purpose: remove the mechanical repetition from a
`orchestrator-worker-reviewer` round **without** removing any owner gate.

| Script | What it does | What it deliberately does not do |
|---|---|---|
| `gates.sh` | Runs the CI-parity local gate: restore, Release build, then CI's exact `--filter` legs (unit, integration, cross-module wiring), the MAUI head with its non-optional serial flags, and both Python portal suites. Prints an explicit **NOT checked here** list. | It does not claim to *be* CI. Anything it skipped is named. |
| `deliver.sh` | Stages an explicitly-named path set, proves nothing else was staged, refuses policy-layer paths, shows the change set, commits, stops. | No push, no PR, no merge. Push belongs to `SCHOOLCOLLAB_ALLOW_PUSH=1`, the PR body is authored judgement, and a script cannot know whether a merge was authorized. |

## Why `gates.sh` mirrors filters instead of enumerating projects

`SchoolCollab.slnx` registers **13** `*.Tests.Unit` projects. A script listing "the six
affected projects" is wrong twice over: wrong count, and wrong *method* — CI gates by
**filter**, across the whole solution, so a per-project loop silently drops the repo-wide
`ArchitectureTests.Unit` scanner, every `*.Tests.Integration` project, the
`CrossModuleWiringTests` guard, and both portal suites.

The parity between this script and `.github/workflows/ci.yml` is pinned by
`tests/SchoolCollab.ArchitectureTests.Unit/LocalGateCiParityTests.cs`. **Edit both or
neither** — the test fails if a CI filter leg is missing here, if the MAUI serial flags
disappear, or if the "NOT checked here" list is removed.

`gates.sh` is also **stricter than CI in exactly one place**: CI passes
`--ignore-exit-code 8` ("no tests ran"), and here that case is a failure — the repo has
already paid for that trap (`dotnet test | grep failed` prints `failed: 0` when zero tests
ran, which closed nothing on 2026-10-12 and again on 2026-10-15).

## Why `deliver.sh` exists at all

The round's git leg was ~30 lines of brief repeated every round, and the brief is where the
owner's gates live (staged change-set review, push authorization, no merge without a word).
Only the mechanical part moved into the script. The two failure modes it closes are real:
a stray `git add -A` once pulled `.pi/skills/**` into a round commit, and a 187-line
*empty* patch once shipped because files were never `git add -N`'d.

```bash
# stage, verify, print — then stop (no commit)
scripts/round/deliver.sh --check --message /tmp/msg.txt -- src tests documents/rounds/round-x.md

# commit only, then stop with the push/PR/merge reminders
scripts/round/deliver.sh --message /tmp/msg.txt -- src tests documents/rounds/round-x.md
```

## Usage

```bash
scripts/round/gates.sh                # everything, including the MAUI head and both portals
scripts/round/gates.sh --no-maui      # fast .NET loop; "NOT checked" names the head instead
scripts/round/gates.sh --no-portals   # no uv on this machine
```

`gates.sh` needs `uv` for the portal legs; without it they are listed as **not checked**
rather than silently skipped.
