# Round-runner model policy

Status: **ADOPTED 2026-10-06.** The nine decisions below are settled. They were posed — with
recommended answers — in the handoff stub of the same day (PR #310) and adopted in one round,
under the owner's standing instruction ("for any decision to make, grill-me if necessary" —
proceed on recommendations; grill only where a decision is genuinely the owner's). Two of the
recommendations were **amended by evidence found while grounding the round**, noted in their
rows. Nothing here delegates the git/gh gates: every policy change still lands through a
reviewed PR on the owner's word.

Date: 2026-10-06 (posed and adopted the same day) · Provenance: the 2026-10-05 corpus below ·
Companion: `.pi/skills/orchestrator-worker-reviewer/SKILL.md` — the tiered skill this policy
governs — and its `references/models.md`, the **operational id catalog** this policy
deliberately does not duplicate.

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

The full operational precedence ladder — the five-item resolution order, the dispatch-time
id-resolution rules, the substitution rule, and the never-omit-the-model-field dispatch rule —
lives in `references/models.md` and is binding there by reference.

## The nine decisions, settled

| # | Decision | Settled as | Note |
|---|---|---|---|
| 1 | Vehicle | **This file is the spec** | The stub flipped in the same change; Q8's ledger created with it |
| 2 | Metric | **Iterations per round** is the primary metric; cost is a constraint, not a target | Feeds Q9's re-evaluation trigger |
| 3 | Seat coverage and order | **All seats, author/gate first** — orchestrator-plan author, plan-review gate, then worker, diff-review, UI tester | Author/gate is where the corpus shows the leverage |
| 4 | First move | **Citation discipline:** a plan that asserts a fact must cite `file:line`; the gate reddens uncited claims | A step-2b review criterion, not a model change — effective from the next orchestrator round |
| 5 | Author escalation | **Risk-based from a named trigger list, never by default** | The trigger list is in §"The policy" item 4 |
| 6 | The gate tier | **Keep the gate tier**; trial lean (Tier 2) rounds only after citation discipline is in effect | The gate is the proven instrument (5/5) — trading it for tokens trades the brake for the accelerator |
| 7 | Gate vs author diversity | **The gate must differ from the author in family or clearly-bigger tier — never the same id at the same size** | **Amended while grounding this round:** the stub's letter said "family"; the standing Option-A pairing (author `glm-5.3-flash`, Tier-3-full gate `glm-5.3`) is *same family, bigger tier* — and produced the corpus's recorded author-side catch (the split-review P1). The rule encodes the intent — no effective self-review — not the letter |
| 8 | Ledger | **`documents/solution/round-runner-model-ledger.md`**, one row per tiered round: seats→models, gate P1s by severity, iterations, rework cause | Seeded with the 2026-10-03→05 portal arc; earlier rounds stay authoritative on their round-doc line 1 |
| 9 | Split and cadence | **This spec holds policy; `references/models.md` holds operational ids.** Re-evaluate on **two consecutive rounds with ≥2 rework iterations each**, or after **5 rounds** regardless | A re-evaluation is a grill round argued from the ledger — ids churn in `models.md`, policy changes here |

## The policy, in operational order

1. **Measure iterations per round.** A round's quality record is its iteration count, not its
   P1 count. The ledger row is where the number lives.
2. **Cite or fail (Q4).** From the next orchestrator round, a plan asserting a repo fact —
   the state of code, a file's contents, a guard's reach — must cite `file:line`. The step-2b
   plan gate treats an uncited factual claim as a finding. This is the cheapest lever the
   corpus names: plans asserting stale state are exactly what the gate caught, and no model
   swap fixes that.
3. **Keep seats independent (Q7).** The reviewer/orchestrator must differ from the worker's
   model (the standing owner rule, restated here as policy); the plan gate must differ from
   the plan author in **family or clearly-bigger tier**. The Option-A pairing satisfies this;
   a same-id-same-size gate never does.
4. **Escalate the author on a trigger, never by default (Q5).** Triggers: (i) cross-context
   contract changes; (ii) an auth/security surface; (iii) more than two prior rework
   iterations in the same area; (iv) a round whose previous plan failed its gate. On a
   trigger, escalate the author seat one step for that round and record the trigger in the
   round doc.
5. **Keep the gate (Q6).** The plan-review gate stays on full rounds. Trial lean (Tier 2)
   rounds only once citation discipline is in effect, and count them in the ledger like any
   other row.
6. **One provider per round** — recorded on round-doc line 1, never mixed mid-round except the
   per-role cross-provider override the user names (precedence item 1). Operational detail
   stays in `references/models.md`.
7. **Write the ledger row when the round closes (Q8).** The orchestrator adds it in the
   round-doc flow; a round is not closed until its row exists.
8. **Re-evaluate on the trigger, not on a whim (Q9).** Two consecutive rounds with ≥2 rework
   iterations each, or five rounds since the last evaluation — then a grill round argued from
   the ledger, never from memory.

## What this spec does not hold

Operational model ids, provider catalogs, the substitution rule, and the dispatch-time
resolution rules — all in `references/models.md`. This spec names model ids only as evidence,
never as policy.