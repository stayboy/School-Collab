# Round: review-shape landing — P1 provenance fix + consistency syncs

Mode: attended solo (Tier 0, doc-only). Branch: `chore/review-shape` (PR #399). Base head: `85aa0126`.

## Scope

The go-forward settle (Q100–Q104, "all as recommended") sent this arc through an independent
review pass over PR #399's full diff, then parked on its verdict (1 P1, 7 P2s) and a red CI run.
This round (Q105–Q107, "all as recommended") clears the P1 and the mechanical P2s so #399 can land:

- **P1 — ADR provenance (blocks merge).** `adr-execution-tiers.md`'s Status line cited "Q89–Q92",
  a question set that exists nowhere. The verifiable record is landing commit `ecf27831`'s body:
  "Q77–Q102 of the 2026-10-21 grill round". Fixed: the Status line now cites that record, and a
  provenance note records the Q-number collision (Q77–Q102 overlaps the later Q85–Q104 series),
  deliberately **not renumbered** — the pins are content-rich and cited by number across durable
  docs; a renumbering audit is a deferred candidate.
- **P2 ①** — `AGENTS.md` light-round menu item synced to the skill's tier table (Tier 1 = plan
  gate + worker; Tier 2 = plan gate + worker + static diff-only reviewer).
- **P2 ②** — the mandated unattended-solo heading unified on the skill's spelling
  (`(unattended solo - recorded, NOT accepted)`); the review found AGENTS.md vs SKILL.md, the
  spell-check found a third copy in the ADR — both non-skill instances fixed.
- **P2 ③** — ledger EOF trailing blank lines removed.
- **P2 ⑦** — the sizing round-doc's acceptance evidence restated accurately: the SKILL cites the
  ~3k-line freeze calibration; role-contracts cites the plan-step sizing rule it carries.
- **P2 ④⑤⑥ — deferred on record.** `SpecVerificationRowsArchitectureTests` false-negative
  holes, `RazorMarkupHygieneArchitectureTests` stale line numbers, and the topic-edit spec's
  stale counts are C#/spec work, out of scope for a doc-only round; each needs its own round.
- **Q106 — flake.** CI run 39752479573 red: 1 of 4414 tests
  (`ConsentThatReachedNoParameterChange_DoesNotSilenceALaterUnguardedDiscard`,
  `SchoolCollab.Assignments.Tests.Unit`, `ContainSingle`) — a docs-only delta cannot reach that
  test's inputs; flake protocol governs. The run was rerun `--failed`; landing gate: this push's
  CI green **and** the rerun green. A same-test re-failure = main-owned hardening item, park again.

## Grill record (this arc, all "as recommended")

- Q100–Q104 (go-forward): independent review pass over the branch diff; park on P1/CI red;
  Q105–Q107 fix round; arc-close trash pass; flake → rerun + main-owned hardening on re-fail;
  Q104(a) single confirmation executes to the stop rule.
- Q105 (subject of the arc's concerns): **(a) the just-landed review-shape layers** — the
  review pass's own verdict.
- Q106: **rerun the failed job now** (`gh run rerun 39752479573 --failed`) — executed 2026-10-22.
- Q107: **the bundled Tier 0 Solo attended doc-only fix round** (this diff), then commit →
  push → CI green → land #399 → trash pass as its own docs-only branch + PR.

## Diff summary

4 `.md` files, doc-only: `adr-execution-tiers.md`, `AGENTS.md`,
`round-runner-model-ledger.md`, `round-review-sizing-and-smell-baseline.md`.
Frozen in `diffs-review-shape-landing.patch` (content files only, per convention).

## Acceptance

| Criterion | Verdict | Evidence |
|---|---|---|
| P1: Status cites the verifiable record + collision noted, no renumbering | MET | ADR Status line + provenance note; `ecf27831` body is the cited record |
| Menu item 2 matches the skill's tier table | MET | AGENTS.md menu vs SKILL.md § Tiers rows 1–2 |
| Heading spelling unified (skill's hyphen form, every instance) | MET | `git grep -n "recorded, NOT accepted"` — 3 instances, one spelling |
| Ledger EOF normalized | MET | trim rewrite; diff = EOF blanks only |
| Sizing round-doc evidence row accurate | MET | row 1 re-read vs role-contracts.md's actual citation |
| Doc-only diff (P2④⑤⑥ deferred, not silently dropped) | MET | changed paths = 4 `.md` files |

Verdict: **CLOSED** (attended solo; plan-author acceptance after self-review).

## Not run

`dotnet build` / `dotnet test` — no `.cs`/`.razor`/`.csproj`/props/appsettings touched (the
build-verification rule's list). The branch's `Build & Test` CI run is the authoritative
signal for this push; run 39752479573's rerun covers the parked flake.

## Wait cost

- Reviewer seat (independent pass over #399's full diff, 2026-10-21): the P1 + 7×P2 verdict
  quoted throughout this doc — parked the merge per the stop rule.
- Parent: go-forward settle + park (Q100–Q104), this fix round (Q105–Q107), edits, CI rerun
  and polls; the commit/push/merge legs are mechanical delegate runs.
- CI: run 39752479573 rerun (`--failed`) + one full `Build & Test` on the fix push.