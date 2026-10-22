# Round: reviewability sizing, fix-grill front door, and the reviewer smell baseline

**Status:** CLOSED — doc layer riding `chore/review-shape` (PR #399); base commit `84e53ac4`
**Tier:** 0 (Solo), attended — session agent implements, self-reviews two-axis (marked self-run), accepts
**Mode:** attended
**Patch:** `diffs-review-sizing-and-smell-baseline.patch`

## Pinned decisions (owner, 2026-10-21)

| Q | Settled decision |
|---|---|
| Q95 | Plan-time reviewability sizing: workstreams sized to one fresh context window; whole expected diff honestly reviewable in one reviewer pass (freeze-time ~3k-line calibration reused — no new number); vertical by default, fan-out stays the owner-opted horizontal exception; vocabulary split — workstream = vertical unit (published ticket or plan-decomposed), slice = fan-out's file-disjoint partition; consume published tickets before re-decomposing; gate's *Scope gates* criterion gains reviewability — exceeds-and-cannot-reslice = P1 → park |
| Q96 (owner-amended) | When sizing fires attended: fix-grill FIRST — grill-me over the re-slice/re-scope frontier, one round max, counted as the plan-review iteration, recommendations attached; only if the revised plan still exceeds the bound does the round park asking the owner to run `/to-tickets` (never self-invoked) |
| Q97 | Unattended: a too-big plan parks via the existing gate-P1 mechanics, recommendation recorded unratified; fan-out pre-named in the owner's round instruction is the only bypass; never auto-sub-scope, never publish issues |
| Q98 | Reviewer contract: provenance sentence in the "Two verdicts, one seat (Q47)" block (REVIEW block = in-seat implementation of `code-review`'s two-axis method; Tier 0 invokes the skill directly) + Standards axis gains the 12-smell Fowler baseline (`~/.agents/skills/code-review/SKILL.md`, step 3 — read once per review) as judgement-call P2s; documented repo rules override, tooling-enforced smells skipped; no new REVIEW-block line; Tier 1 scope-check stays smell-free |
| Q99 | Execution: after the pending Q85–Q94 layer's commit; this round's edits (SKILL.md + role-contracts.md only) as their own doc-layer commit on `chore/review-shape`, riding PR #399; Tier 0 Solo, attended |

## Plan

**Goal:** fold grill-round decisions Q95–Q98 into the two repo-owned contract files so a plan is sized reviewable at plan time (with the fix-grill-then-front-door escape hatch) and the reviewer's two-axis REVIEW block carries explicit provenance and the smell baseline.

**Scope (in):**
- `SKILL.md` step 2 (Plan): one sizing block — workstream/slice vocabulary, fresh-context-window + one-reviewer-pass bound, calibration reuse, consume-published-tickets, fix-grill-then-`/to-tickets` front door.
- `SKILL.md` § Unattended rounds: one bullet — a too-big unattended plan parks.
- `role-contracts.md` § Reviewer — plan-review pass: criterion 2 gains reviewability.
- `role-contracts.md` REVIEW-block region: provenance + smell-baseline paragraph.

**Scope (out):** fan-out mechanics, freeze-time split mechanics, Tier-1 eligibility checklist, AGENTS.md, the vendored skill files (`~/.agents/skills/*` — cited, never patched).

**Expected files:** `.pi/skills/orchestrator-worker-reviewer/SKILL.md`, `.pi/skills/orchestrator-worker-reviewer/references/role-contracts.md`.

**Acceptance criteria:**
1. Q95 present in both the plan step and the gate criterion; workstream/slice vocabulary honored; no new numeric calibration (the ~3k-line freeze calibration cited, not redefined).
2. Q96 present in the plan step: fix-grill first (one round, counted as the plan-review iteration), park-recommending-`/to-tickets` only if still oversized; `disable-model-invocation: true` cited.
3. Q97 present in the unattended section: park via gate-P1, recommendation unratified, fan-out pre-named = only bypass, no silent sub-scope, no issue publishing.
4. Q98 present in the REVIEW-block region: provenance sentence + 12-smell baseline as P2s, documented repo rules override, tooling-enforced skipped, no new REVIEW-block line, Tier 1 exempt.
5. No prior text contradicted; existing terms reused (workstream, slice, park, plan-review iteration); file voice, indentation and wrapping preserved.

## Worker Report

Four insertions, two files, **+36/−1** (frozen in `diffs-review-sizing-and-smell-baseline.patch`):

1. `SKILL.md` step 2 — inserted **Reviewability sizing (owner, 2026-10-21)** between "The plan must be implementable standalone." and **Open decisions (author side)**: vocabulary, sizing bound, calibration reuse, consume-first, fix-grill-then-front-door.
2. `SKILL.md` § Unattended rounds — new bullet **A too-big unattended plan parks (owner, 2026-10-21)** between Hard exclusions and Record provenance.
3. `role-contracts.md` plan-review pass — criterion 2 **Scope gates** extended with **Reviewability (owner, 2026-10-21)**: exceeds-and-cannot-re-slice = P1 → park (re-slice grill first, then the `/to-tickets` front door). The −1 is this criterion's reflowed tail line.
4. `role-contracts.md` REVIEW-block region — new paragraph **Provenance and the smell baseline (owner, 2026-10-21)** after "Two verdicts, one seat (Q47)".

No other regions touched. Doc-only round: no `.cs`/`.razor`/`.csproj`/`appsettings` change, so no build or test run (see `## Not run`).

## Review

*Self-run (Tier 0 Solo — the independent seat is skipped, the review is not; both axes marked self-run per the `code-review` skill's two-axis method).*

**Standards: PASS**
- Voice/conformance: owner-dated bold bullets match the file's convention; 3-space list-indent and ~76-col wrapping preserved (verified by region re-read); em-dashes intact, no encoding damage.
- No overwrites: fan-out opt-in, freeze-time split, Tier-1 eligibility checklist, unattended 2b/step-5 mechanics all untouched — the diff touches exactly the four insertion points.
- Term consistency: "workstream" defined once and consistent with step 5's ticket-workstream usage; "slice" left to the fan-out; the fix-grill shares the existing ≤1 plan-review-iteration budget rather than inventing a second one.

**Spec: PASS**
- Q95 → sizing block + gate criterion + vocabulary + consume-first; calibration reused, no second number.
- Q96 → fix-grill first, park-recommending-`/to-tickets` second, never-invoke with the disable flag cited.
- Q97 → unattended parks; fan-out pre-named bypass; silent sub-scope and publishing explicitly banned.
- Q98 → provenance sentence; 12-smell Fowler baseline as judgement-call P2s; documented-repo-rule override and tooling skip stated; no new REVIEW-block line; Tier 1 exempt.

P1: none. P2: none.

## Acceptance

| Criterion | Verdict | Evidence |
|---|---|---|
| 1 | MET | `SKILL.md` step 2 "Reviewability sizing" block; `role-contracts.md` criterion 2 "Reviewability" — both cite the ~3k-line freeze calibration, no new number |
| 2 | MET | sizing block, final four sentences — fix-grill, park, `/to-tickets` recommendation, `disable-model-invocation: true` |
| 3 | MET | unattended-section bullet "A too-big unattended plan parks" |
| 4 | MET | role-contracts "Provenance and the smell baseline" paragraph |
| 5 | MET | diff = 4 insertions, 0 deletions (the −1 line is criterion 2's reflowed tail); region re-reads show neighbours unchanged |

Build: not run — doc-only round (the in-session build rule applies to code files, none touched). Tests: not run — no behavioural code, no spec-verification rows touched.

Verdict: **CLOSED**. Residual P2s: none.

## Not run

| Step | Reason | Owner |
|---|---|---|
| Step 4(a) `dotnet build` + tests | doc-only round; the in-session build rule lists code files only | none needed |
| `SpecVerificationRowsArchitectureTests` | this layer touches no spec doc carrying verification rows | none needed |

## Wait cost (Q113)

- wait calls: 0
- commands over 90 s: 0
- per-seat tokens: no dispatched seats this round (Tier 0 Solo). Pre-round commit delegate (pending layer → commit `84e53ac4`): 66,830 in / 1,457 out.