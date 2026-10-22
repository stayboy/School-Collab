# ADR: Execution tiers — Solo is Tier 0, and the ladder lives in the skill

- **Status:** Accepted (owner, 2026-10-21 — per landing commit `ecf27831`'s record, "Q77–Q102 of the 2026-10-21 grill round"; see the provenance note below)
- **Date:** 2026-10-21
- **Supersedes:** the split accepted the same day ("AGENTS.md owns mode definitions, skills own
  procedure") — see *Why the earlier split was reversed*
- **Related:** `GLOSSARY.md` (tier · seat · round · unattended);
  `.pi/skills/orchestrator-worker-reviewer/SKILL.md` § "Tiers";
  `documents/rounds/README.md`; `documents/specs/round-runner-model-policy.md`

> **Provenance note (added in the 2026-10-22 review-fix round):** the Status line above
> originally cited "Q89–Q92" — a range that is not a recorded settle set
> (`spec-and-ticket-skills-in-the-orchestrator.md` §6 pins that day's sets as Q85–Q89 and
> Q90–Q94). The verifiable record is the landing commit `ecf27831`'s body: "Q77–Q102 of the
> 2026-10-21 grill round". That range overlaps the Q85–Q104 numbering the later durable
> layers use — a known Q-number collision, recorded here and deliberately **not renumbered**
> (the pins are content-rich and already cited by number across durable docs; a renumbering
> audit is a deferred candidate, not a settled change).

## Context

The repo ran **two ladders** for the same idea. `AGENTS.md` offered a menu of "modes" — Solo, Light,
Full — while the skill offered "tiers" 1–3 plus a **routing row 0** that said *"0 — do not invoke this
skill … solo, per AGENTS.md"*. Solo was therefore simultaneously a mode of the menu, a row of the tier
table, a row of the "per-mode model sets" table, and a paragraph of procedure — with no single home.

It had already drifted. The skill's models table read *"the single agent does plan + implement +
**check itself**"* while a paragraph added hours earlier said Solo **must** run the two-axis
`code-review`. Two statements, same file, opposite rules. The question *"does the skill have a Solo
tier?"* was answerable both ways, which is the whole problem.

## Decision

1. **Solo is Tier 0.** The ladder is `0 · 1 · 2 · 3`, and "default to the lowest tier that qualifies"
   now covers every piece of work. Tier 0 dispatches **no child runs**; its agent is implementer,
   reviewer of its own diff, and acceptor.
2. **The skill owns the ladder and the workflow vocabulary.** It is the document a round runner reads,
   so the tier definitions — including Tier 0's — live there.
3. **`AGENTS.md` owns the menu and the repo-wide constraints on tiers** — which tier to offer, the
   unattended modifier, the hard exclusions, the approval gates — and **points** at the skill for the
   ladder. Precedence is unchanged: `AGENTS.md` wins any conflict and the losing file is amended in the
   same change.
4. **Vocabulary:** "mode" is retired in favour of **tier**; **round** means one execution of any tier,
   Tier 0 included; **unattended** is a modifier available on any tier.
5. **Unattended Tier 0 records, it does not accept.** The self-acceptance ban in the skill ("never
   auto-accept the plan author's own recommendations") bites hardest here, because Tier 0's plan author
   is its only seat: `grill-me` is still mandatory, the frontier is written into the round doc under
   `## Open decisions (unattended solo - recorded, NOT accepted)`, every pin is marked `unratified`, and
   the hard exclusions stop the round for a human.

## Why the earlier split was reversed

The first settlement put *definitions* in `AGENTS.md` and *procedure* in the skill. In practice a mode's
definition **is** procedure — when to use it, how many seats it runs, who accepts — so the definition
had to be restated next to the procedure that used it, which is exactly how the two files came to
disagree. The line that holds is subject-matter, not document kind: **workflow vocabulary and the ladder
belong to the workflow document; policy about workflows (who may run what unattended, what must reach a
human) belongs to `AGENTS.md`.**

## Alternatives rejected

- **A named tier outside the numbering** (`Solo · Light 1–2 · Full 3`) — two ladders, the same ambiguity.
- **Renumbering** (`1=Solo …`) — breaks every tier reference in `documents/rounds/**` and the skill's
  cross-references for no benefit.
- **Retiring the word "tier" in favour of "mode"** — the skill's tables, tests and round docs are
  tier-shaped already; "mode" was the newer, thinner word.
- **Keeping the definition in `AGENTS.md`** — reversed above.

## Consequences

- `GLOSSARY.md` exists for the first time, with four terms and no mechanics.
- The skill's description, tier-0 row, per-tier model table and UI-only rule are reworded; its Tier 0
  paragraph keeps the model pin and the self-review requirement.
- `AGENTS.md`'s menu item shrinks to a pointer plus the repo-wide constraints; its unattended paragraph
  now reads "a modifier on any tier" and carries the Tier 0 recording rule.
- Round-doc line 1 keeps three distinct markers: owner-attended · `unattended (auto-accept
  recommendations)` (Tiers 1–3, with a seat other than the plan's author) · `unattended solo (recorded
  frontier, unratified)`.
