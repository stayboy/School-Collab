---
name: orchestrator-worker-reviewer
description: >-
  Tiered orchestrator-led workflow for implementing features and fixes in the
  School-Collab repo, optimized for speed and token usage. Tier 1 collapses to a
  single worker run with the parent authoring the plan and transcribing
  acceptance; Tier 2 adds a static diff-only reviewer; Tier 3 runs the full
  four-agent pipeline - orchestrator (document owner) plans and owns the round
  doc, worker implements, reviewer statically verifies against the plan, and
  acceptance is written by the plan's author and gates every tier. A UI tester
  bug-hunts delivered UI work when the UI trigger fires. Use for feature
  implementation, multi-fix rounds, or any work where plans and reviews must be
  checked by a document owner before closing. Do NOT use for trivial
  single-file non-behavioural changes - those run at Tier 0 (Solo): self-review
  and self-acceptance still apply, no child runs are dispatched.
---

# Orchestrator-Worker-Reviewer (with UI Tester) — tiered

Four roles collaborate on spec/plan-owned implementation with independent
verification, executed at the cheapest tier the task safely supports:

1. **Orchestrator** — document owner. Reads specs, plans, authors acceptance
   criteria, owns the round doc, and writes the acceptance verdict. In Tier 3
   it also derives the UI-tester scope handover. In Tiers 1–2 the **parent
   acts as orchestrator** (parent-authored plan, parent-transcribed verdict).
2. **Worker** — implements exactly the plan; runs build + affected tests; never
   edits the round doc; returns a structured WORKER REPORT.
3. **Reviewer** — **static; never builds or tests**. Two passes on the same
   model: the **plan-review pass** (Tier 3, *before* the worker is dispatched —
   feasibility, scope gates, acceptance honesty, security posture, judged
   against the plan doc + the seams it cites) and the **diff-review pass**
   (the worker's diff against the plan plus the best-coding-practices check).
   Returns a structured PLAN REVIEW or REVIEW block inline.
4. **UI Tester** — adversarial bug hunter over delivered UI, scoped
   **verbatim** to the orchestrator's handover. Not a second reviewer.
   Returns a structured UI TEST block inline.

Cost rules that apply to every round (speed + token budget):

- **Cheapest safe tier** — never pay for agents a task does not need.
- **One authoritative build/test pass** — the worker builds + tests the
  affected projects; the parent reruns the authoritative pass once
  (incremental); the reviewer never builds. Parent numbers are the only
  source of truth.
- **One round doc + one diff artifact** — hand-offs pass paths, not payloads;
  the plan is the single source of truth for worker and reviewer.
- **Policy travels separately** — a round's commit never carries `.pi/skills/**`,
  `AGENTS.md` or `documents/specs/**` edits; they ship as their own doc layer
  (2026-10-18: a feature branch that swept a `models.md` seat edit into its code
  commit then conflicted with the doc layer carrying the same ruling, and the
  layer had to supersede the feature PR's copy).

- **Acceptance is a gate on every tier** — no round, pass or layer is reported as
  completed, handed over, or marked done before its `## Acceptance` section
  exists: every criterion the plan wrote, checked one by one against the diff and
  the parent's authoritative build/test numbers. Work without an acceptance is
  **unaccepted work** — the same posture as "an empty reviewer verdict means no
  review". The accepting seat is always the **plan's author** (§ "Acceptance is a
  gate in every tier"), never the worker, reviewer or tester.

## Tiers

| Tier | Use when | Child runs | Acceptance |
|---|---|---|---|
| 0 | **Solo** — trivial or contained work: a typo, a comment, a config tweak, a single-file non-behavioural change (or a small fix the owner explicitly puts here) | **0 — no child runs** (the agent is implementer, self-reviewer and acceptor) | **self-acceptance after the self-run two-axis `code-review`** (see "Solo still reviews" below) |
| 1 | Small behavioural fix passing the eligibility checklist | 2 (plan gate + worker) | **Plan-author acceptance after the work**: the parent checks every plan criterion against the diff + authoritative build/test and transcribes the verdict |
| 2 | Behavioural, no UI, single-context plan | 3 (plan gate + worker + static reviewer); ≤1 rework iteration (worker + reviewer) | **Plan-author acceptance after the work**: the parent adjudicates REVIEW, then checks every plan criterion against the diff + build/test and transcribes the verdict |
| 3 | Feature rounds; anything carrying schema / endpoint / contract / multi-context risk; anything failing the Tier-1 checklist for a NON-UI reason | 4–6: orchestrator-plan, **reviewer plan-review**, worker, reviewer diff-review, then orchestrator-accept OR plan-author acceptance by the parent (**lean**, no-UI rounds) + UI tester when the UI trigger fires | the orchestrator (full) or the parent as plan author (lean) writes the verdict after checking every criterion — always before the round is reported complete (+ tester-scope handover when the tester fires) |

Default to the **lowest tier that qualifies**; when ambiguous, go one tier up.
When starting a feature or fix, offer the user the menu (Tier 0 Solo / Tier 1–2
light round / Tier 3 full four-agent) per repo `AGENTS.md` — do not default silently.

### Acceptance is a gate in every tier (owner rule, 2026-10-17)

Every tier and mode — **solo**, 1, 2, 3-lean, 3-full — ends with a written
acceptance **before** the work is passed on as completed. "Completed" without an
acceptance section is unaccepted work, exactly as an empty reviewer verdict is
"no review".

- **Who accepts.** The **plan's author owns the verdict** — the parent on Tiers 1–2
  and Tier 3 lean, the orchestrator on Tier 3 full. Since the owner ruling of
  2026-10-18 the acceptance is also a **dispatched pass on its own seat**
  (`ollama-cloud/deepseek-v4-pro:0813` on the pi profile) for Tiers 1–3: the seat
  verifies the criterion-by-criterion checklist against the diff and the parent's
  authoritative numbers, and the plan's author signs the verdict. The gate is
  therefore model-independent of both the worker and the reviewer — the lean cut
  removes the UI tester, not the acceptance run. Clinepass is unchanged
  (`cline-pass/glm-5.3` on full rounds; parent-transcribed on lean). **Tier 0 (Solo)**:
  the single agent accepts its own work — **after** the self-run two-axis review above — and
  may opt into the acceptance seat when the owner asks. Never the worker, the reviewer, or the
  tester. Acceptance is the author's own
  gate, so it is never self-congratulatory: it is a per-criterion check against
  evidence, not a restatement of the WORKER REPORT.
- **What it checks.** Each criterion the plan wrote, one by one, with evidence —
  the diff (file:line) or the named test — plus the parent's **authoritative**
  build/test numbers (child-reported numbers are discarded). A criterion the diff
  does not evidence is **not met**, however plausible the worker's prose sounds.
- **Outcomes.** ACCEPTED (verdict CLOSED) · REWORK (bounded loop, procedure step
  7) · RESIDUALS (grill the owner before the verdict, procedure step 5). The
  acceptance section records which, plus any accepted residuals verbatim.
- **Disagreement between seats (owner 2026-10-18).** When two reviewer seats
  disagree, the parent adjudicates against the source and the acceptance records
  the disagreement **plus the evidence that resolved it** (Round B1: one seat
  called the tests vacuous, the other called them honest-but-wrong-vocabulary; the
  pre-fix base settled it for the second). A verdict that silently picks a side
  without recording the split is not an acceptance.
- **Pass-on rule.** A round reports to the owner, or a layer hands to the next
  layer (a stack layer, a follow-up round), **only with its acceptance written**.
  Anything else is reported as "unaccepted" or "parked" — never as done.

### Tier-1 eligibility checklist (ALL must hold)

- Single bounded context; expected diff ≤ ~4 files.
- No UI surfaces: no `.razor`, `.razor.css`, `.css`, or `.js` files, nothing
  under `wwwroot/`, no ApiClient / Blazor client project files.
- No EF migration, schema, or MassTransit contract changes; no new public API.
- Existing tests cover it, or the plan states why a test change is unnecessary.
- No interplay with other in-flight work.

### UI-only work is NEVER Tier 3

A **UI-only** change — presentation in the broadest sense: markup, `*.razor.css`,
dialog size/height, icon or button placement, grid columns, column order, labels,
tooltips, spacing — carries no schema, endpoint, contract or multi-context logic.
Even though the deterministic UI trigger fires (the changed-file list contains
`.razor`/`.css`), such work runs **Tier 0 (Solo, preferred, with the pinned model
`ollama-cloud/deepseek-v4.1-flash:1017`) or Tier 1–2 (light)** and **never Tier 3**.
The UI tester exists to bug-hunt delivered UI *behaviour*; it is not a reason to
promote a presentation tweak to a four-agent round.

Owner workflow for this class of work:

- **Always ask the owner which tier to use before starting** (repo `AGENTS.md`).
  For a single-surface tweak, recommend Tier 0 (Solo) with the pinned model; for something
  touching several components or adding a small behaviour, recommend Tier 1–2 (light).
- A UI-only revision requested **mid-round or after** a feature round — e.g. "ditch
  the icon buttons, use the kebab", "make the dialog bigger", "move the buttons to
  the first column" — is a **bounded rework**, not a fresh round: apply it at Tier 0 (Solo), or
  one worker run when it spans several files, and re-verify only what it touched.
- Two adjacent traps this rule exists to avoid: (i) burning a plan-review + diff-review
  cycle on CSS/markup; (ii) letting the Tier-3 shell hide a genuinely cheap change.
- Test fallout from a surface change (a test asserting the old markup, or a widened
  callback signature) is part of the same Tier 0 rework — fix it in the same pass.

### Tier 3 lean (no-UI feature rounds)

When a Tier 3 round's UI trigger does **not** fire, the round runs **lean** — the
orchestrator-plan, worker, and reviewer runs and the Tier-3 model ladder are
unchanged, but the parent **transcribes the acceptance** (on the reviewer's
verdict + its own authoritative pass) instead of dispatching an
orchestrator-accept run, and no UI tester is dispatched (there is nothing to
bug-hunt). Rework bound stays ≤2.

**The plan-review pass is NOT part of the lean/full split:** every Tier 3 round
— lean or full — has the static reviewer read the **plan before the worker
runs** (procedure step 2b). The plan is the round's most load-bearing artifact
and a defect in it is the cheapest defect to fix; a diff-review alone cannot
catch an unworkable premise.

**Plan-review model: identical on every tier (owner 2026-10-18).** The plan gate
runs on the round's **higher model** — `ollama/glm-5.3:cloud` on the pi profile,
`cline-pass/glm-5.3` on clinepass — for **lean and full alike**. The plan is the
round's most load-bearing artifact and its gate the highest-leverage review in the
round, so it never shares the worker's model and never drops to the fast
generalist. The **diff review** runs on the reviewer seat,
`ollama-cloud/deepseek-v4-pro:0813` (no dispatchable clinepass id — see the seat
table) facing an
`ollama-cloud/deepseek-v4.1-flash:1017` worker (`cline-pass/deepseek-v4.1-flash` —
owner 2026-10-05, latest). Both use the read-only `reviewer` shell; a user-named
model still wins (precedence item 1).

**Hand rule: lean drops the accept-run, never the plan-run.** The
orchestrator-plan pass is what separates Tier 3 from a stretched Tier 2 — it is
where the design questions get *resolved* (worker tenancy, dedupe/backfill
strategy for a mutating migration, contract inventories, idempotency
semantics). It is mandatory on every Tier 3 round, lean or full. Full Tier 3
(with the orchestrator-accept run) is for UI rounds — the tester fires anyway
and the document owner hands over tester scope — or whenever the owner wants
the plan's author to adjudicate the REVIEW personally.

There is no defined tier between 2 and 3. A round that fails the Tier-2
checklist only on file-level gates (migration, contracts, new project, diff
size) with its design fully settled may be run at Tier 2 as an explicit
**owner override**, recorded as a deviation on round-doc line 1; if the
design is open, the round is Tier 3.

**Light (Tier 1–2) and Tier 3 lean share the acceptance seat, not the rigour.**
Since the 2026-10-17 owner rule both end with the **plan's author** writing the
verdict after the work (the parent — the plan author on those tiers — instead of
a separate orchestrator-accept run). They stay distinct in what precedes
acceptance: Tier 3 lean runs the **mandatory plan-review gate before the
worker** and an orchestrator-authored plan, and allows ≤2 reviewer iterations;
Tier 2 has **no plan gate**, a ≤1 iteration bound and a diff-only reviewer. Do
not collapse Tier 2 into "lean" — that would silently drop the plan gate, the
round's most load-bearing artifact.
**Size alone no longer earns Tier 2 (owner 2026-10-18).** The diff gate runs
`deepseek-v4-pro:0813` on Tiers 2–3, so a change that qualifies *only* on size is
**Tier 0 (Solo)** — Tier 2 is for contained behavioural work where an independent diff gate
earns its cost.

### Mid-round escalation

If any agent (or the parent) discovers scope creep — more files than planned,
UI touched, schema/contract changes, wider behavioural surface — stop the fast
path and **bump the tier**; continue the round at the higher tier. Never force
a light tier through. Record escalations in the round doc.

## Round docs — one doc, one diff artifact

- `documents/rounds/diffs-<round-slug>.patch` is written **once** by the parent
  and passed **by path** to the reviewer and tester instead of inline hunks;
  the round doc's **sole writer** is the orchestrator run (Tier 3) or the
  parent (Tiers 1–2) — reviewers and testers never write files; they return
  structured blocks inline and the parent persists them into the doc.
- Round-doc format, line-1 provider/model traceability, and the ephemerality
  rules live in `documents/rounds/README.md` and `references/models.md`.
- **Review shape (Q47–Q52).** The reviewer returns **two verdicts** (`Standards:` / `Spec:`), the
  round doc carries `## Not run` for unavailable steps and the flake protocol (first total,
  isolation, re-run), and two gates mechanise the rest. Decisions and evidence:
  `documents/solution/spec-and-ticket-skills-in-the-orchestrator.md` §3.
- **Fan-out provenance (opt-in).** A round that dispatches parallel workers
  records it on round-doc line 1 as `Workers: parallel (slices: A, B)` — the
  slice ids, comma-separated. Mechanics: step 3's fan-out variant and
  `references/parallel-workers.md`.

## Unattended rounds (owner-opted)

A round runs **unattended** only when the owner says so for that round - never by
default, never inferred from silence, never on by absence. The option removes the human
*wait*; it does not remove the discipline. Record it on round-doc line 1 as —
`Mode: unattended (auto-accept recommendations)`.

When the plan-stage grill (step 2b) or the acceptance residual grill (step 5) fires in an
unattended round, the two sites behave differently (owner, 2026-10-21):

- **Step 2b — the plan gate is the accepting seat.** The parent enumerates the frontier in
  the round doc and names it in the gate's brief; the answers ride the plan-gate dispatch
  that already runs on every tier: the gate returns its PLAN REVIEW **and a FRONTIER
  block in the same iteration** — no second dispatch. The block echoes **each question
  number it accepts**, one per line, and ends `accepted: N of N`. Any
  `not accepted: Q<k> — reason` line, any missing echo, any count mismatch — or silence —
  fails the block, and the round parks for the owner rather than proceeding on a
  partially accepted frontier.
- **Still hold the round.** Enumerate the whole frontier in grill format — every
  open, prerequisite-settled decision, one round, each numbered with a recommendation -
  exactly as an attended round would; the gate adjudicates it, never the author. Only the
  human wait disappears; the enumeration, the recommendations
  and the round itself do not.
- **Never auto-accept the plan author's own recommendations.** Recommendations may be
  auto-accepted only from a seat OTHER than the plan author - in practice the plan gate
  (a different, larger-tier model). A plan author accepting its own
  recommendations is self-review wearing a grill costume, and that case stays
  human-gated even in an unattended round.
- **Step 5 — a non-clean verdict parks.** The acceptance residual grill has no
  auto-accepting seat on any tier: by step 5 the gate has run, and nothing between it and
  the verdict may accept a recommendation for the owner. An unattended round whose verdict
  would be anything other than a clean CLOSED records the residual frontier - each
  question echoed, marked unratified - and parks under the existing PARKED convention
  (`documents/rounds/README.md`). Unattended runs park clean; they never accept at the
  gate line.
- **Hard exclusions.** Migrations, contract/wire shape, security and auth, tenant
  isolation, and deleting a public route always reach a human unless the owner has
  pre-authorized that specific round by name. The blast radius decides, not the phrasing
  of the question.
- **A too-big unattended plan parks (owner, 2026-10-21).** Unattended rounds can neither
  invoke `/to-tickets` nor quiz the owner, so a plan that exceeds the reviewability sizing
  rule parks via the existing gate-P1 path, its recommendation recorded unratified; the only
  bypass is fan-out pre-named in the owner's round instruction. An unattended round never
  sub-scopes silently (that is a descope) and never publishes issues.
- **Record provenance; never blur it.** Unattended decisions use a DISTINCT heading -
  `## Pinned decisions (unattended - recommendations accepted)` - never the owner-pinned
  heading, and the round's ledger row's `Mode` cell reads `unattended` (`unattended solo` at
  Tier 0). A reader must be able to tell
  at a glance that no human confirmed those pins.
- **Tier 0 records; it never accepts.** Because Tier 0's plan author is its only seat, the
  self-acceptance ban above makes auto-acceptance impossible there: `grill-me` is still
  mandatory, the whole frontier is written into the round doc under
  `## Open decisions (unattended solo - recorded, NOT accepted)`, every pin is marked
  `unratified`, and a hard exclusion stops the round. Round-doc line 1 reads
  `Mode: unattended solo (recorded frontier, unratified)` - never the Tiers 1–3 marker.
- **Report them; list them in the acceptance (owner, 2026-10-21).** When the round closes,
  `## Acceptance` carries one **pinned-decisions** list — the same entries as the round doc's
  unattended heading — and the report to the owner repeats that list as a compact table:
  decision · the recommendation that was accepted · the heading it came from. At **Tier 0** the
  same list is headed **awaiting ratification** and every line is marked `unratified`: it is a
  request, not an acceptance. A pin must never survive only in the round doc — this folder is
  ephemeral.
- **Budget unchanged.** The grill still costs one round: it still counts against the
  existing plan-review iteration at step 2b, and it is still the bound's terminal step at
  step 5. Unattended mode adds no iterations.

## Models and per-tier strategy

Exact id tables, substitution rules, and the traceability format live in
`references/models.md`. Summary:

**Provider is a per-round choice, not a fixed setting.** Pick ONE profile for
the whole round, record it on round-doc line 1, and never mix providers
mid-round. Both profiles below are supported options; resolve the exact ids at
dispatch time with `subagent({ action: "models" })` and copy `provider/id`
verbatim (the `ollama*` ids have drifted historically — verify, don't assume).

**Pin the worker id WITH its build suffix (owner, 2026-10-18).** The pi-profile
worker is **`ollama-cloud/deepseek-v4.1-flash:1017`** — never the suffix-less
`deepseek-v4.1-flash`. The registry carries several v4.1-flash builds
(`…-1017`, `…:batch`, plus the clinepass variant), so a bare id is not unique and
a dispatch can silently land on a different build than the round recorded. Treat
a looser or missing suffix as a dispatch error to fix, exactly like an omitted
`model` field.

| Role | pi `ollama` profile (long-standing default) | `clinepass` profile (option) | Tiers |
|---|---|---|---|
| Orchestrator | `ollama-cloud/glm-5.3-flash` | `cline-pass/glm-5.3` | 3 only |
| Worker | `ollama-cloud/deepseek-v4.1-flash:1017` | `cline-pass/deepseek-v4.1-flash` (lean, owner 2026-10-05 **latest** — supersedes the same day's `glm-5.3-flash` interim; full: `cline-pass/deepseek-v4-flash`) | 1–3 |
| Reviewer (diff review) | **`ollama-cloud/deepseek-v4-pro:0813`** (owner 2026-10-18 — the seat for every tier with a reviewer gate) | no dispatchable id — `cline-pass/mimo-v2.6-flash` is absent from the provider registry (verified 2026-10-18); the seat runs on the pi profile only | 2–3 |
| **Plan reviewer** (every tier with a plan gate) | **Tier 2: `ollama-cloud/deepseek-v4-pro:0813` · Tier 3: `ollama/glm-5.3:cloud`** — a light round runs exactly TWO models (owner 2026-10-23), so its gate sits on the verifier seat; ids and the full ruling: `references/models.md` | `cline-pass/deepseek-v4-pro` · `cline-pass/glm-5.3` | 1–3 |
| **Acceptance** (Tiers 1–3) | **`ollama-cloud/deepseek-v4-pro:0813`** (owner 2026-10-18 — a dispatched pass on its own seat, model-independent from worker and reviewer) | unchanged (see the acceptance section): `cline-pass/glm-5.3` full rounds · parent-transcribed on lean | 1–3 |
| UI Tester | `ollama/minimax-m3:cloud` | `cline-pass/minimax-m3` | 3 + UI |
| Escalator (blocked-pass rework) | Tier 3: `ollama-cloud/kimi-k2.7-code` (pinned; distinct from the diff-review seat `deepseek-v4-pro:0813`, so an escalated rework and its verifier never share a model); otherwise the round's reviewer model | the round's reviewer model | on block |
| Higher-model re-verify | `ollama/glm-5.3:cloud` | `cline-pass/glm-5.3` | on escalation |

From pi, opt into the clinepass profile for a round by passing
`clinepass/cline-pass/<id>` (e.g. `clinepass/cline-pass/deepseek-v4.1-flash`) in
every role's `runs.run` — and record that choice in the round doc header.

**Per-tier model sets:**

| Mode | Worker (implementer) | Orchestrator | Reviewer | Notes |
|---|---|---|---|---|
| **Tier 0 (Solo)** | plan + implement + self-review + self-acceptance (this table's tier 0) — default `ollama-cloud/deepseek-v4.1-flash:1017` | — | — | ask the user first: session model only on their yes (see the procedure below) |
| **Light (Tiers 1–2)** | `ollama-cloud/deepseek-v4.1-flash:1017` | `glm-5.3-flash` (if dispatched) | **`deepseek-v4-pro:0813`** | **the reviewer/orchestrator must NOT share the worker's model** — the verifier must not be the implementer's own model (owner 2026-10-18: the diff-review seat is `deepseek-v4-pro:0813`). Acceptance seat: `deepseek-v4-pro:0813` |
| **Tier 3** | `ollama-cloud/deepseek-v4.1-flash:1017` | `glm-5.3-flash` | **`deepseek-v4-pro:0813`** | Tier 3 adds the UI tester `minimax-m3` and keeps the plan-review on `glm-5.3:cloud` (owner 2026-10-18 — the 2026-10-23 two-model ruling applies to light rounds only). **escalator = `kimi-k2.7-code`** (pinned, distinct from the diff-review seat). Acceptance seat: `deepseek-v4-pro:0813` |

**Tier 0 — procedure.** The definition is the ladder's row 0 above; what belongs here is the
mechanical detail. Before running a Tier 0 round, **always ask the user whether to use the current
session model**; use it only on their yes, otherwise fall back to
**`ollama-cloud/deepseek-v4.1-flash:1017`** (owner 2026-10-18 — the suffix-pinned worker id, shared
with a dispatched worker round; the earlier `glm-5.3-flash` fallback is retired). Record the choice
(session model or default) on round-doc line 1.

**Solo still reviews (owner, 2026-10-21).** Before the acceptance is written, the solo
agent runs the two-axis **`code-review`** on its own diff (`Standards` + `Spec`), records
both reports in the round doc's `## Review` marked **self-run**, fixes any P1, and re-reads
only the fixed hunks. The mode removes the independent reviewer *seat*; it never removes
the review. An unreviewed solo diff is unaccepted work, in the same sense as an empty
reviewer verdict. This closes the gap observed the same day: a behavioural solo round
shipped with no review because the mode's wording said "no reviewer".

**Defaults and overrides — precedence, highest first:**

1. **A per-role model the user names** for this round (e.g. “reviewer = X”) —
   that role only; every other role keeps the profile defaults.
2. **A provider the user names** for this round (“use clinepass”) — that
   profile's defaults for every role the user did not name individually.
3. **The profile already recorded in the round doc** (resumed or continuing
   rounds — the earlier decision carries forward).
4. **The skill default: the pi `ollama` profile** in the table above, per tier —
   subject to the **per-tier model sets** below it (Tier 0 Solo / Tier 1–2 light / Tier 3). Light
   and Tier 3 run the SAME role set: worker `ollama-cloud/deepseek-v4.1-flash:1017`, orchestrator
   `glm-5.3-flash`, diff reviewer `deepseek-v4-pro:0813` (owner 2026-10-18), acceptance
   `deepseek-v4-pro:0813` — the verifier never shares the implementer's model, and
   acceptance is independent of both. Tier 3 differs only by
   adding the UI tester; the plan-gate seat differs per tier (Tier 2 = the verifier
   seat, Tier 3 = `glm-5.3:cloud`) — see `references/models.md`.
5. **Cline is the exception, not an override** — Cline cannot resolve `ollama`
   ids, so it always uses the `clinepass` profile.

Rules: an override governs the **whole round**, not a single dispatch — apply it
to every dispatch that has not yet started and never run two providers in one
round. **Exception:** a per-role model override that the user names explicitly
(precedence item 1) MAY sit on a different provider than the round's profile —
that is the one sanctioned way a round spans providers. Record it as an explicit
per-role override on round-doc line 1, and keep every role the user did not name
on the profile default. Record the effective choice on round-doc line 1; if the
override arrives
mid-round, also log it in the execution log with the reason. Roles already
dispatched keep the model they actually ran on (traceability). A user-named
**escalation** or **higher-model re-verify** model wins over the profile default
for that step. If an id is unavailable, substitute within the same tier and
record the substitute. **Never omit the `model` field** — an omitted field is
NOT the skill default: the child then inherits the current session model (one
id, on whatever provider the session is using), collapsing every role onto it.
Treat an omitted `model` as a dispatch error to fix, not a fallback.

## Provider profiles

- **pi (default):** run all phases as ONE `workflowScript` call with
  `async: true` using `await runs.run(...)`. Never combine structured
  single-child execution (`agent`+`task`) with `workflowScript`. Resolve the
  round's provider ids with `subagent({ action: "models" })` and copy exact
  `provider/id` strings — the `ollama*` ladder is the long-standing default, and
  the **`clinepass` profile is an available option** (`clinepass/cline-pass/<id>`
  for every role in the round).
- **Cline:** spawn teammates named `orchestrator`, `worker`, `reviewer`,
  `ui-tester` with the compact contracts from `references/role-contracts.md`.
  **Spawn once per session and reuse them across rounds** — do not re-spawn
  per round. The session provider is `clinepass`; `ollama/<id>:cloud` ids do
  not resolve there. If teammate dispatch
  returns `Unauthorized: ... re-authenticate your Cline account`, stop and
  re-authenticate before rerunning — a round must not proceed on a dead
  session.
- **Pick one provider per round; never mix mid-round.**

## Role contracts and structured output

Each child's task = its compact contract (see `references/role-contracts.md`)
+ the plan + the round-doc and patch paths. Children read the plan and patch
artifact from disk; they do NOT receive full reports verbatim and do NOT
re-read source specs — the plan is the single source of truth, and specs are
opened only to resolve ambiguity.

Structured blocks (formats in `references/role-contracts.md`): **WORKER
REPORT** (changed files, build/test verdicts, deviations), **PLAN REVIEW**
(Tier 3 only: feasibility/scope gates/acceptance honesty/security + P1/P2 with
file:line), **REVIEW** (P1/P2 with file:line evidence + best-practices check),
**UI TEST** (scope ack, P1/P2 with file:line, out-of-round observations).

## Deterministic UI-round trigger

After the worker run, the parent derives the UI verdict from
`git diff --name-only`: the round is a **UI round** iff the changed-file list
contains any `.razor`, `.razor.css`, `.css`, or `.js` file, anything under
`wwwroot/`, or a file in the ApiClient / Blazor client projects. The **same file set,
read off the plan's expected files**, decides the **step-2c UI mock gate** before the
worker runs — one definition, two moments. Only UI
rounds get a tester pass; the tester-scope handover enumerates exactly these
surfaces — the changed files, the pages/dialogs/landing pages that render
them, the ApiClient methods they call, and the navigation entry points — and
the tester never derives or expands its own scope.

## Procedure

0. **Select the tier.** Apply the eligibility checklist; default to the lowest
   safe tier; a no-UI Tier 3 round runs **lean** (see "Tier 3 lean" above);
   offer the AGENTS.md menu when the choice is not obvious. Record
   the tier + provider + models in the round doc header, plus the round base
   (`git rev-parse HEAD`; if the tree is dirty at round start, also record
   `git diff --name-only` so the round diff can be isolated).
   **Two standing rules before you dispatch anything:** (a) **briefs carry
   paths, not content**, and no seat re-opens a file it already read this session
   (`references/role-contracts.md` § "Brief discipline"); (b) if the round starts
   from a **symptom** rather than a task — "it hangs", "the page is blank",
   "`main` is red" — route to the matching runbook first:
   `grep -l '^## Symptom' documents/runbooks/*.md` (every runbook carries that
   block; guarded by `RunbookSymptomRouterTests`). Run its 2-minute discriminator
   **before** diagnosing; if none matches, say so explicitly, diagnose, and record
   the new symptom family as a runbook — never re-burn a documented hunt (two
   30-minute UI hunts ran without consulting `aspire-apphost-silent-hang.md`,
   2026-10-20). (c) **Never sleep in a tool call.** Wait for CI with
   `scripts/round/wait-ci.sh --pr <n>` (one call, one line, hard timeout); dispatch
   seats `async` and let the wake bring you back; block only when the next action needs
   the result in this turn. A seat's per-command budget is ~90 s — `gates.sh` is the
   parent's tool, never a worker's — and a seat that is waiting should be reporting
   instead. Full rules: `references/role-contracts.md` § "Waiting discipline".
1. **Setup (once per session).** Resolve model ids (pi:
   `subagent({ action: "models" })` — choose the round's provider profile there).
   Cline:
   spawn the four teammates once and reuse them across rounds.
2. **Plan.** Tiers 1–2: the parent writes the `## Plan` section (goal, scope,
   expected files, acceptance criteria; Tier 2 adds the reviewer's acceptance
   criteria). Tier 3: the orchestrator run reads the source specs/review
   docs, writes `## Plan`, and authors the worker/reviewer task specs and
   acceptance criteria. The plan must be implementable standalone.
   **Reviewability sizing (owner, 2026-10-21).** The plan decomposes the work
   into **workstreams** — vertical units: a published ticket or a
   plan-decomposed bullet; the fan-out variant's file-disjoint partitions keep
   the name *slices*. Sizing is a plan criterion, not a freeze-time discovery:
   every workstream must fit a single fresh context window (a tracer bullet
   through the stack), and the whole expected diff must be honestly reviewable
   in one reviewer pass — reuse the freeze-time split calibration (~3k lines,
   provisional) as the honesty check, never a second number. If the cited spec
   already has published tickets (`/to-tickets`), name them as workstreams —
   consume, do not re-decompose. If the plan exceeds the bound and the author
   cannot re-slice it alone, surface the re-slice/re-scope frontier to the
   owner in grill format first (one round, recommendations attached, counted as
   the plan-review iteration); only if the revised plan still exceeds it does
   the round **park** recommending the owner run `/to-tickets` — the orchestrator
   never invokes that skill (`disable-model-invocation: true`).
   **Dispatch declaration (>3 workstreams).** A plan with more than three
   workstreams must state, in its sizing block, `Dispatch: single pass` or
   `Dispatch: one worker per workstream` — the decision goes on the record before
   any worker runs. Three or fewer needs no declaration. A plan with more than four
   workstreams **and** no published tickets must also carry a one-line **offer** of
   `/to-tickets` to the owner in the plan; the offer is the parent's, the invocation
   stays the owner's (owner, 2026-10-24 — a 5-workstream single pass timed out with
   three landed; nobody had asked the question).
   **Open decisions (author side).** A plan that leaves a decision fork the specs
   do not determine must DECLARE it in `## Plan` as an `Open decisions:` list in
   grill format — numbered, each with a recommended answer — rather than silently
   deciding it. Genuine forks only: a fact anyone could look up is never an open
   decision. Declared forks are the plan-review gate's cleanest input and the
   step-2b grill escalation's earliest trigger; a plan with an unresolved fork is
   not implementable standalone.
2b. **Plan review (Tier 3 — BEFORE the worker runs).** Dispatch the static
   reviewer against the **plan**, not a diff: the plan doc plus the code/spec
   seams it cites. It judges (i) **feasibility** — do the named files, routes,
   entities, clients and patterns actually exist and behave as claimed? (ii)
   **scope gates** — UI files, contract shape, migrations, committed secrets,
   CPM rules: is the in/out boundary honest? (iii) **acceptance honesty** —
   would each test the plan calls "discriminating" actually fail against the
   pre-fix code? (iv) **security posture** — auth schemes, claim handling,
   token validation, credential handling, tenant isolation; (v) **pinned
   decisions** — does the plan contradict or silently re-open one? It returns a
   **PLAN REVIEW** block. A plan **P1** → the orchestrator (Tier 3 full) or the
   parent (lean) revises `## Plan` **before dispatch**; the reviewer then
   re-reads only the revised sections. **≤1 plan-review iteration.** The worker
   is never dispatched on a plan with an open P1. The pass is static — any
   build/test numbers it volunteers are discarded like any other child's.
   **Grill escalation (plan stage only).** If settling a plan P1 within the one revision
   requires a choice the specs do not determine — a genuine trade-off the owner owns, not
   a fact anyone could look up — do not loop the reviewer. The parent surfaces the whole
   residual frontier to the user in grill format: every open, prerequisite-settled
   decision in one round, each numbered, each with a recommended answer. One grill round
   maximum, counted as the plan-review iteration. Fold the answers into `## Plan` as
   pinned decisions before the reviewer re-reads the revised sections; the worker is
   still never dispatched on a plan with an open P1.
   **Model:** **Tier 2 → `ollama-cloud/deepseek-v4-pro:0813`**, **Tier 3 → `ollama/glm-5.3:cloud`**
   (owner 2026-10-23: a light round runs exactly TWO models — worker + verifier — so the gate shares
   the verifier seat; the 2026-10-18 every-tier `glm-5.3:cloud` ruling survives for Tier 3). The gate
   must never be the plan author, so **record the parent's model on round-doc line 1**; if the parent
   already sits on the verifier seat, escalate the gate one step. Read-only `reviewer` shell;
   a user-named model wins. Ids: `references/models.md`.
2c. **UI mock (owner gate — before the worker runs; owner, 2026-10-23).** When the
   plan touches a **user-visible surface** — the same file set the *Deterministic
   UI-round trigger* names, evaluated against the plan's expected files instead of a
   post-hoc diff — the parent renders a **console mock of the final look** and posts it
   for the owner's approval **before** dispatching the worker. This is a **gate, not a
   courtesy**: the worker is never dispatched on an unapproved mock, and an approved
   mock is what the diff-reviewer and the acceptance seat check the delivered UI
   against.
   - **Format (fixed):** a fenced `text` block, **dotted** box-drawing borders —
     `┄` horizontal, `┆` vertical, `╭ ╮ ╰ ╯` corners — with one labelled section per
     affected surface. Dotted = a **boxed block**; drawing **nothing** = a **layout
     region**. Annotate with the pinned decision ids (`Q5`, `Q6`, …) so the reader can
     trace a row to the rule that produced it. Keep each panel's inner width exact — a
     mock whose columns misalign is unreadable and gets re-worked, not approved.
   - **One round.** Post the whole mock once, covering every affected surface, then
     wait. Owner edits come back as plain notes and are re-rendered in one revised
     mock; a mock is approved when the owner says the look is right.
   - **What a mock IS and IS NOT.** It is a **design artifact**: it settles layout,
     grouping, chrome and where actions sit. It is **never evidence** — it proves no
     behaviour, renders no real CSS, and must never be cited as verification in the
     round doc, the reviewer task or the acceptance. Real-UI verification stays the
     UI tester's (Tier 3) or a live browser pass; a mock is not a substitute for either.
   - **The mock RENDERS the pinned pattern; it never invents one.** Every box, region,
     header row, width and rail entry in a mock traces to the pattern doc named by the
     `AGENTS.md` specialty table (`documents/solution/panel-layout-pattern.md`) — cite the
     decision ids it carries (`Q5`, `Q6`, …) next to the rows they produced. If a mock
     cannot be drawn without contradicting that doc, the round **parks**: the doc is the
     contract and is amended first, never overridden by a prettier mock.
   - **The mock step is where durable lessons surface (owner, 2026-10-23).** A mock
     review is the cheapest place a design lesson ever appears: the owner sees the look,
     not a diff, so a correction here is a general rule rather than a line change. When
     the owner corrects the mock, treat the correction as a **candidate lesson** — record
     it with its evidence (the mock row, the incident date, one clause), and **propose**
     it in the reply for the pattern doc or the repo-owned rule file. **Never pre-write
     it**: an unaccepted lesson riding this round's commit is a rule the next reader
     assumes was ratified. Accepted lessons land in the **one** pattern doc — never
     restated across specs, rule files and `AGENTS.md`, which only ever carry a pointer.
     This is also the step that stops a round re-learning the session's own findings
     (two box languages double-framing a rail; `width: inherit` failing to size a slotted
     Fluent control; a literal colour fighting the theme).
   - **Every mock carries a CONSENT CHECKLIST** (owner, 2026-10-23): a short table naming,
     panel by panel, (a) which region it sits in, (b) **whether it is collapsible**, and
     (c) where its action button lives. These are per-instance decisions the specs cannot
     derive — whether an affordance exists is exactly the kind of thing the owner must
     consent to before it is built, and a collapsible panel that should have been static
     (or the reverse) is invisible in a diff. The owner approves the table together with
     the layout; the approved table is the acceptance criterion for that part of the diff.
     The pattern doc's own checklist is the starting point, not the answer — a new surface
     gets its own rows.
   - **Record it.** The approved mock (or its path), the owner's verdict and any
     candidate lessons it produced go into the round doc, so a later reader can tell an
     approved look from an assumed one, and an accepted lesson from an unratified one.
   - **All tiers, including Tier 0.** The point is owner engagement with the look, so
     the gate does not depend on a dispatched orchestrator: a solo round that changes a
     visible surface shows its mock and waits too. No UI trigger fires ⇒ no mock step.
3. **Worker run.** Task = worker contract + the plan inline + expected files +
   round-doc path (the worker does not edit it). The worker implements, runs
   build + affected tests, returns WORKER REPORT. The parent persists the
   report into the doc.
    **Parallel-worker fan-out variant (opt-in, criterion-gated).** When the
    plan's expected-files list partitions into ≥2 file-disjoint, seam-free
    slices (each ≥~10 files, provisional) — a mechanical dispatch-time check,
    **not a tier gate**; any tier may qualify, self-selection does the tiering —
    the parent may fan the worker run out to N parallel workers, one per slice,
    each isolated in its own git worktree, **only with the owner's opt-in**
    recorded on round-doc line 1 (`Workers: parallel (slices: A, B)`). Workers
    run build + unit tests only (never Testcontainers integration tests) and
    leave work uncommitted; the parent integrates the slice patches at the
    recorded base, and step 4 below freezes one combined patch unchanged.
    Full mechanics, guards and the independence layers:
    `references/parallel-workers.md`. Every other step — plan, plan review,
    freeze, review, acceptance, tester, rework, escalation — runs byte-for-byte
    as written.

   **Build-escalation pattern (worker block rule):** when a worker pass times
   out, stalls, or hangs mid-round (30-min cap, runaway shell command, repeated
   build failures it cannot recover from), the parent interrupts it and
   re-dispatches the SAME pass scope as an ESCALATION PASS on
   the ESCALATOR model (Tier 3: `kimi-k2.7-code`, pinned; since 2026-10-18 distinct
   from the diff-review seat `deepseek-v4-pro:0813`) — **unless the scope-split
   rung below fires first: a pass that carried more than three workstreams is
   SPLIT, not escalated** (owner, 2026-10-24; see that rung, which also covers the
   judgement branch below the size test). Everything else here applies to the
   escalation route — via a WRITE-CAPABLE agent shell (the `worker` or `delegate`
   agent; the `reviewer` agent shell is read-only by design and cannot
   execute passes) — which reconciles the on-disk state first, then
   completes the blocked pass. Subsequent worker passes revert to the worker
   model. **Escalated work is reviewed by the HIGHER model: the static
   re-verification of any pass completed via escalation runs on
   the round's provider's higher model (`ollama/glm-5.3:cloud` on the ollama
   profile, `clinepass/cline-pass/glm-5.3` on clinepass)** — the escalator never
   re-verifies its own pass. One escalation per blocked pass; record
   the provenance in the round doc (e.g. "pass 3 completed via escalation").
   Do not steer or revive a run whose bash has been open past a plausible
   build/test window — interrupt it; a hung process never settles.

   **Scope-split rung — check it before escalating.** Escalation is the rung for a
   *capability*-shaped block; a block that is *scope*-shaped is split instead. Two
   branches, and only the first is mechanical:

   1. **Size branch (mechanical, applies first).** If the timed-out pass carried
      **more than three workstreams** — read off the plan's own dispatch
      declaration, not off an impression of the worker — the pass is **split, not
      escalated**. No judgement is involved and none may be substituted. The owner
      asked for exactly this on 2026-10-24: a large blob is the remedy-shaped
      failure, so it is split *before* another model is paid to do the same
      quantity faster.
   2. **Judgement branch (≤3 workstreams).** Classify by what the round doc
      records, and **write the reason down**: no workstream landed, repeated build
      failures it could not recover from, wrong approach being pursued ⇒
      capability-shaped ⇒ escalate. Otherwise ⇒ split. Do **not** treat "the build
      was green" as proof of health — build ≠ tests, and the 2026-10-23 round that
      produced this rule timed out with a green build **and one failing test**. The
      real protection is branch 1 plus the residual rule below, not a health signal.

   A split means: freeze the landed state (frozen patch + one written line per
   completed workstream), then dispatch **only** the named residual — and the
   residual must be a plan **slice with its own acceptance criteria**, never a
   restatement of the timed-out workstream. The split unit is **state-first**
   ("landed vs residual"), never a re-dispatch of the original W-list: a second
   worker redoing applied edits is worse than the timeout. Run the slices
   **sequentially in one working tree**; worktrees are for genuinely independent
   slices, and a residual is dependent by construction.
   Accounting: a split is a re-dispatch of reduced scope, not an escalation, so it
   leaves the one-escalation-per-blocked-pass allowance untouched and does **not**
   consume the tier's rework bound (≤1 on Tier 2, ≤2 on Tier 3). Caps, so the rung
   cannot become a chain: **one split per blocked pass and one per workstream**; a
   split whose residual *also* times out is **escalated** (or, if the escalation is
   already spent, **parked** to the owner) — never split again, and never a second
   split of the same workstream. Once the residual lands, the tier's normal bound
   resumes: "does not consume an iteration" must never read as "unbounded extra
   passes". Evidence: the 2026-10-23 panel-layout round put 5 workstreams in one
   30-minute pass, timed out with three landed and a named residual, and escalated
   only because no rule authorised the split (round doc holds the story).
   **Time-box rule — probe with the `grill-me` skill, then park.** The escalation pattern
   above assumes the block has a known cause and a short path. When a pass (or the
   parent's own verification) is over its time box, **measurably** — there is no
   "expected duration" to estimate against, so the trigger is a recorded bound, not a
   judgement: a **worker pass** that has hit its 30-minute cap once **and** whose single
   escalation pass then also timed out, stalled, or was interrupted without completing the
   pass (never re-dispatch a third time), **or** the **parent's own verification**
   (build/test) exceeding the same 30-minute cap or producing no output for 10 minutes.
   Once over the time box, stop iterating on it:
   - **Probe the block with the `grill-me` skill.** An unexplained hold-up is an open decision,
     not something to grind on: put the whole frontier to the owner in the skill's own
     `❓ **Qn** — **title**` / `➡️ <recommended answer>` format — one round, numbered,
     each item with a recommended answer and its cost (the "cost" wording is this repo's
     `AGENTS.md` rule, layered onto grill-me, which does not itself ask for it) — e.g.
     "stuck command vs. harness defect vs. product defect vs. unknown?",
     "interrupt now vs. let it finish?", "fix in this round vs. park?". Resolve every
     fact first (run status, live processes, disk state, transcript tail); never ask
     the owner for what `status`/`git`/`tasklist` can answer.
   - **If the grill cannot settle it cheaply, park the work.** Record it as an explicit
     **backlog / follow-up item for a dedicated look at that specific issue** — a line
     in the round doc **plus** a durable follow-up note in `documents/solution/` (or a line in
     the relevant existing spec's follow-up section) plus a backlog entry in
     `documents/specs/*backlog*.md` — the backlog doc is the durable tracker for parked
     follow-ups, so never open a GitHub issue for one: issues here are the `/to-tickets`
     front door's artifact, closed by the round's acceptance ("Ticket closure", step 5),
     never a parked item's —
     then preserve the on-disk state (branch, round doc, whatever the pass produced) so it
     resumes via `resume-interrupted-orchestrator-round`. That skill resumes from a round doc
     **and its frozen patch**, so freeze `diffs-<slug>.patch` from the live tree *before*
     parking (the resume skill's own step 7 shows how) — a round parked at step 3 has no
     patch yet. Leave the partial work **uncommitted** unless the owner explicitly says
     commit, and record in the round doc what is and is not committed.
   A parked block ends the round honestly; an endlessly retried one burns the round, the
   token budget, and the owner's patience.
   **A parked round takes no verdict and skips steps 4–8**: set its `**Status:**` to `PARKED`,
   and its acceptance *is* the follow-up reference, not a pass. It is picked up by a later round
   that plans it explicitly, or resumed via `resume-interrupted-orchestrator-round` when the
   owner asks.
4. **Freeze the diff, then verify in parallel.** The parent writes
   `diffs-<slug>.patch` (`git diff`, or `git diff <base-sha>` when the tree
   was dirty at start), then concurrently:
   - (a) The parent runs the authoritative `dotnet build SchoolCollab.slnx`
     (incremental after the worker), then **`scripts/round/gates.sh --no-maui --no-integration`** — the
     CI-parity gate, never a hand-picked project list: it carries the unit + integration
     filter legs, the repo-wide `ArchitectureTests.Unit` scanner, the wiring guard and both
     portal suites, and it fails on "zero tests ran" (`AGENTS.md` § Pre-flight 3). Run the
     full gate, no flags, when the round touches shared chrome, a migration, a contract or
     auth. A parent verification run that exceeds the time box
     is parked the same way (interrupt, probe with the `grill-me` skill, park) — see step 3.
   - (b) Tiers 2–3: dispatch the static reviewer with the plan + patch path +
     WORKER REPORT; the reviewer returns the REVIEW block. **Brief requirements
     (owner 2026-10-18):** name the binding spec sections by path (a plan can drift from
     its spec — plan-conformance alone misses it); on high-risk rounds (send path,
     migration, wire contract) dispatch a **second, cross-family CROSS-CHECK** seat with the
     owner-named seat authoritative and both verdicts transcribed; state that findings
     cite the patch, never the WORKER REPORT's prose. Tier 1 has no
     reviewer — the parent does the scope check itself (diff-stat vs plan
     scope; unrelated deletions/reformatting are findings).

   Never start a build while a child may still write to the working tree
   (MSB3027 file locks). The static reviewer is safe to run in parallel
   precisely because it never builds.
5. **Accept — the plan author's gate, on every tier.** Tiers 1–2 and Tier 3
   **lean**: the parent (the plan's author) adjudicates findings, then checks
   **every plan criterion** against the diff + its own authoritative build/test
   numbers and transcribes the verdict into `## Acceptance` (criteria checklist
   with file:line or test-name evidence, build/test numbers, P1 list or CLOSED,
   residual P2s). Tier 3 full: the orchestrator-accept run receives the REVIEW
   block + the parent's build/test numbers and writes `## Acceptance` the same
   way. No tier reports the work as completed before that section exists (see
   "Acceptance is a gate in every tier").
   When the verdict is CLOSED and the UI trigger fires,
   the acceptance also appends the tester-scope handover.
   **Residual grill (acceptance).** When the verdict would be anything other than a
   clean CLOSED — an unaddressed P1 at the rework bound, an unmet or descoped criterion,
   explicitly deferred scope, or residual P2s needing an accept/defer/backlog call —
   surface the whole residual frontier to the user in grill format **before the verdict
   is written**: every open, prerequisite-settled residual decision in one round, each
   numbered, each with a recommended answer. One round maximum; a still-open frontier is
   recorded as accepted residuals with the open items listed — never looped. Tiers 1–2
   and Tier 3 lean: the parent transcribes the user's answers into `## Acceptance` as
   the recorded owner decisions. Tier 3 full: the parent runs the grill and passes the
   answers in the orchestrator-accept brief as pinned decisions, before that run writes
   the verdict. A clean CLOSED needs no grill.
   **Ticket closure (conditional — owner, 2026-10-21).** When the plan or round doc
   names ticket issues — the `/to-tickets` front door publishes one GitHub issue per
   ticket, and the round consumes them as workstreams — `## Acceptance` carries **one
   row per named ticket**: the ticket's own acceptance criteria (its
   `## Acceptance criteria` checkboxes or the spec sections it cites) checked against
   the diff and the authoritative build/test numbers. A ticket closes only on a
   fully-met row; a deferred, descoped or parked slice writes the row as a comment and
   the issue **stays open** — residual work is never hidden. The acceptor seat writes
   the rows (self at Tier 0, the parent at Tiers 1–2 and Tier 3 lean, the
   orchestrator-accept run at Tier 3 full); the **parent** executes
   `gh issue close N --comment` with the row plus the round-doc and carrying-PR links -
   never a child run, and never a parent issue (the `/to-tickets` skill's own guard:
   Do NOT close or modify any parent issue). An unattended round that parks closes
   nothing. No named tickets, no step - most rounds have none.
6. **UI tester pass (Tier 3, UI rounds).** Task = tester contract + the
   handover verbatim + patch path. The tester bug-hunts only the handed-over
   surfaces and returns UI TEST; the parent persists it into `## UI Tester`.
7. **Bounded rework loops.**
   - Reviewer P1s → worker rework task (failing items + patch path only, no
     full history) → re-verify statically (reviewer, or parent scope-check
     for tiny rework diffs). **Tier 2: ≤1 iteration. Tier 3: ≤2.**
   - Tester P1s → orchestrator (Tier 3) or parent appends a rework plan → one
     worker run → **tester re-verifies only**; the parent statically checks
     the rework diff for plan conformance (no reviewer re-run). **≤2
     iterations.**
   - At the bound, surface residuals to the user instead of looping.
8. **Report.** Per-agent status, build/test counts, findings, rework
   iterations, round-doc path. Fold durable outcomes into the spec and update
   the backlog; when the plan named ticket issues, list each ticket closed or left
   open with its verdict row.

## Pitfalls

- **One writer per working tree; isolate concurrent writers.** Two writing
  subagents dispatched into the same checkout are NOT isolated by their paths
  happening to be disjoint: their edits land in one shared diff that cannot be
  attributed to either run, and a reviewer cannot diff it at all. Run writers
  sequentially, or give each its own git worktree (`worktree: true`) — for the
  sanctioned opt-in arrangement, per-slice worktrees under the fan-out variant
  (`references/parallel-workers.md`). The design
  record - plan, spec, round doc - is the **orchestrator's** own artifact; delegating
  it both breaks ownership and manufactures the concurrent-writer case. Verified
  2026-10-07 (round `targets-dialog-picked-zone`): an implementation `worker` and a
  spec `worker` ran in parallel against one checkout, and the reviewer could only
  reconstruct the round's diff from a *sibling* round's patch - attribution was
  never provable.
- **Keep a diff reviewable; a large patch buys no review.** An unbounded
  patch times the static reviewer out into a missed-P1 verdict. Bound the diff for
  a light round, or **split the review by area** (one reviewer per patch slice)
  instead of handing one reviewer an unbounded patch. Split at freeze time when
  the patch exceeds ~3k lines (provisional — one calibration point) via
  `git diff <base> -- <area paths>` per area, dispatched in parallel; a timed-out
  whole-patch review is split before re-dispatch, never re-sent as-is. Gate the
  split mechanically: the slices' file lists must partition the full patch's file
  list (none dropped, none in two slices) — verified by diffing the concatenated
  slices against the full patch, not by narrated line totals, which drift. Brief
  each slice with its own plan sections, patch path, and findings, and keep its
  reading brief-scoped — but the parent lists the round's cross-slice seams (wire
  contracts, registration pairings, call chains that cross the cut) and each
  brief requires tracing them to the far end: "both slices passed" is not a
  whole-system review; the parent's authoritative pass owns the composition.
  Calibrated on round `portal-session-adoption` (5.2k-line whole-patch review:
  timeout, no verdict; ~2.9k/2.3k-line slices: two verdicts, the C# slice finding
  the round's only P1 — a dead wire contract that passed every named test).
- **An empty reviewer verdict means "no review", never "clean".** A reviewer that
  times out, errors, or returns without a REVIEW block has verified nothing. Treat
  the round as unreviewed (resume or re-dispatch it) and never transcribe `CLOSED`
  acceptance off a missing verdict.
- **Verify a FluentUI parameter name against the installed version before binding
  it.** `@bind-SelectedValues` does not exist on 4.14.2's list components (the pair
  is `SelectedOptions` / `SelectedOptionsChanged`); an unknown name silently lands
  in the component's `AdditionalAttributes`, rendering a stray attribute
  (`selectedvalues="System.Collections.Generic.HashSet\`1[System.Guid]"`) while the
  binding stays dead. It is invisible until something depends on the value. Repo
  guidance: `.github/skills/dropdown-ui/SKILL.md`,
  `.github/skills/fluentui-component-props/SKILL.md`; findings and the fix pattern:
  `documents/solution/fluentui-dead-selectedvalues-binding.md`.
- **Never combine structured single-child execution with `workflowScript`.**
- **Test-output starvation kills worker passes.** A worker fighting truncated
  MSTest output can burn its entire 30-minute cap on tooling instead of the
  code — self-reporting it cannot clearly see pass/fail. Treat the output rule as a hard gate in every worker
  task spec: exactly one `dotnet test tests/<X> 2>&1 | grep -E "^\s*failed
  |total:|failed:" | head -40`; no ad-hoc pipelines, no `zz*.log` debug
  files; at most 2 attempts per result read; if output is truncated, redirect
  to a file once and read the tail. A worker that starts narrating tooling
  problems instead of failures is on the timeout path — interrupt early.
- **A MockHttp matcher without an explicit HTTP method shadows later
  method-specific matchers** (first match wins). A context mock's
  method-agnostic `When(url)` can swallow the sign **POST**, so the bUnit test
  named for that POST never exercises it and fails on a 2 s
  `WaitForAssertion`. Always pass `HttpMethod.Get`/`HttpMethod.Post`; when the
  same URL answers before *and* after a mutation, use a counter-based
  `.Respond(_ => …)` sequence, not two same-URL matchers.
- **FluentUI web components cannot be driven with generic bUnit events.**
  `TriggerEvent("oncheckedchange", new ChangeEventArgs{…})` throws inside the
  page's `ErrorBoundary` (`ChangeEventArgs` cannot convert to
  `CheckboxChangeEventArgs`), which reads as a page defect when it is a test
  defect. Drive the bound callback instead:
  `cut.InvokeAsync(() => cut.FindComponent<FluentCheckbox>().Instance.ValueChanged.InvokeAsync(true))`
  (the `InvokeAsync` wrapper is required — invoking off the renderer thread
  raises *"not associated with the Dispatcher"*).
- **`workflowScript` continuation may not persist across detached children** —
  if the workflow errors `unsupported-continuation`, recover each child via
  `subagent({ action: "status", id, view: "transcript" })`. All durable round
  state is already on disk (round doc + patch), so resuming is cheap.
- **The reviewer is static by design.** If it reports build/test numbers,
  discard them — the parent is the only build/test authority; never trust
  child-reported counts.
- **Workers sometimes overwrite code, ignore repo skills, or skip repo
  conventions** — the reviewer's best-coding-practices check exists for this.
  A P1 overwrite finding goes through the rework loop like any other P1.
- **Do not build while a child can still write to the tree** (MSB3027 locks).
  Safe pattern: worker settled → patch frozen → parent build in parallel with
  the static reviewer.
- **Bare model ids resolve only when unique** — copy exact `provider/id`
  strings (`references/models.md`).
- **Only the orchestrator (Tier 3) or the parent (Tiers 1–2) writes the round
  doc** — never the worker, reviewer, or tester.
- **The UI tester is not a second reviewer** — no plan conformance; scope
  comes verbatim from the handover; out-of-scope findings are parent
  observations (optional backlog item), never rework.
- **Escalate instead of forcing a light tier through.**
- Children may pause for supervisor decisions via intercom (pi) — reply, then
  wait for the child to settle.
- **Runaway shell commands hang worker runs** — a filesystem-wide scan from the
  root (`find /`) can block a run for its full cap. Worker task specs must carry the guard: repo-scoped
  searches only (`grep`/`rg` under `src/`, `tests/`, or the NuGet cache under
  `~/.nuget/packages`), never `find /`. If a worker's bash stays open past a
  plausible build/test window, interrupt and escalate the pass per the
  build-escalation pattern in step 3 — steering a hung process is wasted
  quota. **Escalating is not enough on its own:** the re-dispatched brief must carry the
  same guard *plus* **bounded per-command timeouts and a skip-if-slow rule** — the escalation
  inherits the identical hang risk, and a second hang on a test/Testcontainers suite burns
  another full cycle. **Reconcile the tree first** — a hung pass may have produced nothing, so
  never assume partial work exists — and **kill that run's orphaned `dotnet`/testhost
  processes before re-dispatching**, or the next build fails on MSB3021/MSB3027 locks.
- **Adjudicate a tester's/reviewer's findings against source BEFORE dispatching a rework.** A
  severity is a claim, not a fact — the parent owns the triage; verify each claimed P1 in the
  code before spending a rework cycle. Relaying a plausible-sounding repro into a rework brief
  spends a whole cycle on phantom defects **and** hides the real one.
- **A round that builds on an UNMERGED layer must branch from THAT layer's tip, not `main` — and when the
  plan's named symbols don't exist at HEAD, suspect the base before the plan.** Pre-flight is two
  commands: `git merge-base --is-ancestor <prereq-tip> HEAD`, plus one `grep` for a symbol the plan
  names. `git checkout -B <branch> <prereq-tip>` re-mounts the plan losslessly when the branch has no
  commits of its own (untracked round docs survive either that or `reset --hard`).
- **Orphaned `dotnet` processes hang the PARENT too, not just the child.** After any
  hung/killed child — and before a parent verification run — clear stale
  `dotnet`/`testhost` processes first; and when a parent test run looks hung, suspect process contention *before* concluding that the
  suite (or the code) hangs. A false "the tests hang" diagnosis sends you hunting a defect that does not
  exist, in a change you were about to accept.
- **A block that outlives its time box is parked, not retried forever.** See the time-box rule
  in step 3; re-dispatching the same blocked pass is the failure mode it exists to stop, and it
  is what turns one bad pass into a lost session.

## Verification

1. All child runs completed (subagent status completed, exit 0) — a parked round is exempt
   (see the time-box rule in step 3).
2. Parent-run `dotnet build SchoolCollab.slnx -c Debug`: 0 errors.
3. Parent-run `scripts/round/gates.sh --no-maui --no-integration` (full gate, no flags, on shared-chrome /
   migration / contract / auth rounds): 0 failures, and every leg it did not check is named
   in the round doc; pass counts recorded there.
4. The round doc exists with tier-appropriate sections filled and an explicit
   verdict (CLOSED, PARKED with the follow-up reference, or remaining P1s listed), plus
   the provider/models header — and a written `## Acceptance` section produced by the
   plan's author before anything was reported as completed (no acceptance ⇒ unaccepted
   work, never "done").
5. No P1 findings remain unaddressed, or the user explicitly accepted the
   residuals. Loop bounds respected (Tier 2 reviewer loop ≤1; Tier 3 reviewer
   ≤2, tester ≤2).
6. The relevant backlog/spec doc is updated with completion notes; durable
   outcomes folded into `documents/specs/`.
7. **Tier 3 only:** the plan carries a PLAN REVIEW with no open P1 from *before*
   the worker was dispatched (step 2b), recorded in the round doc's `## Review`
   above the diff-review verdict.