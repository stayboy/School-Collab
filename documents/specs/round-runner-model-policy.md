# Round-runner model policy — the open questions (handoff stub)

Status: **DRAFT — handoff stub.** The nine decisions below are the frontier of the
orchestrator/model-policy grill. They were posed — with recommended answers — during the
2026-10-05/06 session (the portal-scope arc), and per the recommendation the *answers* are to
be settled in a **fresh owner grill round**, not at the tail of the session that posed them.
This file is what survives, so that round can start with full context. When the answers land,
this stub becomes the spec: the Status line flips, each row carries its verdict, and the
"what this stub is not" section is deleted.

Date: 2026-10-06 · Provenance: owner grill (deferred 2026-10-05), evidence corpus below ·
Companion: `.pi/skills/orchestrator-worker-reviewer/SKILL.md` — the tiered skill this policy
governs — and its `references/models.md`, which holds the **operational** model ids today.

## Why this policy exists (the corpus, mined 2026-10-05)

- **The metric is iterations per round, never raw P1 count.** A P1 found by plan review is the
  system working; the thing the owner pays for is the rework loop it triggers. Any model-ladder
  change answers one question: did iterations per round fall?
- **The gate earns its keep.** The plan-review gate has caught real P1s on every full round it
  has run (5/5) — including a P1 in the parent's own split-review proposal (a slice bounds
  ownership, not reading; the parent must enumerate cross-slice seams). `portal-submission-grade`
  was the first plan to pass its gate with **0 P1** — the ceiling to aim at, not the floor.
- **Model-ladder corrections were applied ad hoc all session.** The worker, reviewer and
  delegate seats each moved at least once, and the provider moved to `ollama-cloud` — every
  correction recorded in some round doc, none in one policy place. This spec is what turns
  those corrections from memory into policy.

## Standing precedence (settled, not open)

1. A user-named per-role model always wins, for any seat.
2. A role already dispatched keeps the model it ran on.
3. Mid-round overrides are logged, with the reason, in the round doc.

## The nine questions

| # | Decision | Recommended answer | Trade-offs / cost |
|---|---|---|---|
| 1 | Vehicle | **This stub becomes the spec**; the answers settle in a fresh owner grill round (one round, the whole frontier). | One grill round in a clean session. The alternative — deciding at the tail of a compacted session — risks losing the corpus before it is written down. |
| 2 | Metric | **Iterations per round is the primary metric; cost is a constraint, not a target.** | Measures the rework loop the owner actually pays for; refuses to let "more P1s caught" masquerade as quality. |
| 3 | Seat coverage and order | **All seats, author/gate first** — the orchestrator-plan author, then the plan-review gate, then worker, diff-review, UI tester. | Author/gate is where the corpus shows the leverage; the remaining seats join the same policy table once the first two are settled. |
| 4 | First move | **Citation discipline before any model change:** a plan that asserts a fact must cite `file:line`, and the gate reddens uncited claims. | Free — and it attacks the root cause the corpus actually shows (plans asserting stale state), which no model swap fixes. |
| 5 | Author escalation | **Risk-based, from a named trigger list** (e.g. cross-context contracts, an auth/security surface, >2 prior rework iterations on the same area). Escalate the author seat on a trigger, never by default. | Keeps the cheap ladder as the default and reserves the strong model for rounds that can pay for it. Cost: writing the trigger list once. |
| 6 | The gate tier | **Keep the gate tier; trial lean (Tier 2) rounds only after Q4 lands.** | The gate is the proven instrument (5/5); weakening it to save tokens trades the brake for the accelerator. |
| 7 | Gate vs author diversity | **Require the gate model to differ from the author's in family** (never an effective self-review). | A second strong seat costs a little; a gate that shares the author's blind spots catches nothing — independence is what the corpus shows paying. |
| 8 | Ledger | **A durable ledger** — `documents/solution/round-runner-model-ledger.md`, one row per round: seat→model, iterations, P1s by severity, rework cause. | The corpus becomes data instead of anecdote; re-evaluation stops being memory-work. Cost: one row per round, written in the round-doc flow. |
| 9 | Spec vs operational ids, and review cadence | **This spec holds policy; `references/models.md` keeps operational ids.** Re-evaluate on a named trigger — two consecutive rounds with ≥2 rework iterations each — and in any case after **5 rounds**. | The spec stays stable while ids churn; the trigger prevents both "never re-look" and "re-litigate every round". |

## How to run the round

Fresh session, `grill-me`, one round: read this file and the skill, then answer the nine —
"all as recommended" is a valid answer to the whole frontier. Then: flip the Status line,
carry the verdicts into the rows, apply Q9's split, and start Q8's ledger with the rounds
already on `main`.

## What this stub is not

Not decided policy: nothing above is binding until the fresh grill round answers the nine.
The skill's current `references/models.md` remains the operational source of truth until then.