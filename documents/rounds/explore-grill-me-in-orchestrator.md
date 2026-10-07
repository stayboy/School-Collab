# Exploration — grill-me in the orchestrator at plan-review and work-review

## Status

Exploration only — not adopted; no skill file was changed. **Extended after owner
input** ("grill-me will be more useful during acceptance test"): acceptance-stage
(step 5) analysis added below; the original plan-stage verdicts are retained except
where the extension explicitly revises them (`## Recommendation`, `## Stage ranking`).

## What the two review stages are today

Verified against `.pi/skills/orchestrator-worker-reviewer/SKILL.md` (cited `SKILL.md`),
`references/role-contracts.md` (cited `role-contracts.md`), and `references/models.md`
(cited `models.md`).

**Step 2b — plan review (Tier 3, before the worker runs).**

- Dispatch of a read-only `reviewer` child against the **plan doc + the code/spec seams it
  cites** — "the plan, not a diff" (`SKILL.md:273`; `role-contracts.md:68–73`, which also
  bounds the read: "and nothing else (no repo-wide sweeps, no builds, no tests; the
  implementation does not exist yet)").
- It judges exactly five things: (i) feasibility, (ii) scope gates, (iii) acceptance
  honesty, (iv) security posture, (v) **pinned decisions** (`SKILL.md:273–283`;
  `role-contracts.md:74–88`, pinned decisions at `role-contracts.md:88`).
- Output is one structured **PLAN REVIEW** block with `Verdict: ACCEPT | REWORK`, P1/P2
  with plan-section or file:line evidence, gates, and acceptance honesty
  (`role-contracts.md:93–98`).
- A plan P1 → the orchestrator (full) or parent (lean) revises `## Plan` **before
  dispatch**; the reviewer re-reads only the revised sections; **≤1 plan-review
  iteration**; the worker "is never dispatched on a plan with an open P1"
  (`SKILL.md:284–286`).
- Models: full → `glm-5.3:cloud` (highest-leverage review in the round); lean → the diff
  reviewer's model `kimi-k2.7-code`, because "the plan gate must never share the worker's
  model" (`SKILL.md:79–91`, `:169`; `role-contracts.md:69–71`). Side observation from
  verification, not assumed in the analysis below: `models.md:90` and `models.md:159`
  still record a superseded lean plan-review ruling (`deepseek-v4.1-flash`, owner
  2026-09-24) that contradicts `SKILL.md:169` — a stale remnant, flagged here for the
  owner, since this exploration changes nothing.

**Step 4b — work review (diff review, Tiers 2–3, after the worker, after diff freeze).**

- The parent freezes the diff to `diffs-<slug>.patch` first, then runs the authoritative
  build/test itself **in parallel** with the static reviewer (`SKILL.md:312–325`).
- The reviewer gets plan + patch **path** + WORKER REPORT and returns one structured
  **REVIEW** block — verdict `PASS | P1 | P2-only`, P1/P2 with file:line, plus the
  best-coding-practices check (`SKILL.md:318–321`; `role-contracts.md:32–50`).

**Invariants that constrain any integration** (all verified):

- The reviewer is **static by design** — never builds, never tests, never writes files
  (`SKILL.md:17`, `:423`; `role-contracts.md:32`); its volunteered build/test numbers are
  discarded (`SKILL.md:288`, `:423`).
- Reviewers and testers **never write files**; the round doc's sole writer is the
  orchestrator run (Tier 3) or the parent (Tiers 1–2) (`SKILL.md:128–136`, esp. `:133`).
- The **plan is the single source of truth** for worker and reviewer; children read it
  and the patch artifact from disk, "do NOT receive full reports verbatim" and do not
  re-read source specs except on ambiguity (`SKILL.md:35`, `:231–237`).
- Structured blocks are "the ONLY report formats each role may return"
  (`role-contracts.md:6`).
- Hard iteration bounds: plan-review ≤1 (`SKILL.md:285`); rework Tier 2 ≤1, Tier 3 ≤2;
  "At the bound, surface residuals to the user instead of looping" (`SKILL.md:338–346`);
  verification re-checks the bounds and that residuals are either fixed or "the user
  explicitly accepted the residuals" (`SKILL.md:478`).
- Pitfalls that any integration collides with: "One writer per working tree" — the design
  record is the orchestrator's own artifact (`SKILL.md:353–357`); "Keep a diff reviewable"
  (`SKILL.md:364`); **"An empty reviewer verdict means 'no review', never 'clean'"**
  (`SKILL.md:382`); "Adjudicate a tester's/reviewer's findings against source BEFORE
  dispatching a rework" — a severity is a claim, the parent owns triage (`SKILL.md:454`);
  mid-round escalation bumps the tier instead of forcing a light tier through
  (`SKILL.md:121–123`).
- The only sanctioned child→parent channel is the intercom: "Children may pause for
  supervisor decisions via intercom (pi) — reply, then wait for the child to settle"
  (`SKILL.md:440`). There is **no child→user channel**: children reach the human only via
  the parent surfacing residuals.

## What grill-me actually requires

Verified at `C:\Users\skwar\.pi\agent\pi-hermes-memory\skills\grill-me\SKILL.md` (vendored
from `mattpocock/skills` — per repo `AGENTS.md` this copy is read-only upstream content;
it is a personal-session skill, not repo content).

- It is a **multi-round interview with the human**: "Interview the user relentlessly
  until you reach a shared understanding" (`grill-me:10`).
- It works a **design tree**: every decision branches into the decisions hanging off it;
  the **frontier** is "every decision whose prerequisites are already settled"; ask the
  whole frontier in one round, "number each question and give your recommended answer.
  Then wait for the user's answers before the next round" (`grill-me:10–12`).
- Format per question: numbered, title, body, `➡️ <your recommended answer>`
  (`grill-me:14–27`).
- Each round of answers reshapes the tree; recompute the frontier; a question depending
  on a still-open question belongs to a later round (`grill-me:28`).
- **Facts vs decisions**: "Finding facts is your job, never the user's … dispatch a
  sub-agent to find it; don't ask the user for anything you could look up yourself. The
  decisions are the user's: put each to them and wait." (`grill-me:30`).
- Termination: done when the frontier is empty; **"Do not act on it until the user
  confirms you have reached a shared understanding"** (`grill-me:32`).

Constraints these rules impose on integration:

1. grill-me needs an **interactive channel to a party that owns the decisions** and can
   answer in rounds. Child reviewers have neither a user channel nor multi-round
   capability — their contract is one shot, one block (`role-contracts.md:6`, `:93`).
2. grill-me has **no iteration cap by design** ("relentlessly", until the frontier is
   empty) — the opposite of the skill's hard bounds (`SKILL.md:285`, `:338–346`).
3. grill-me's output is **questions with recommendations**, not verdicts — the opposite
   of a block whose `Verdict:` line mechanically drives the rework loop and the
   acceptance transcription (`SKILL.md:284–286`, `:478`, `:484–485`).
4. grill-me's facts/decisions split says most of what a **diff** reviewer would "ask" is
   the agents' job to look up, not the user's to answer (`grill-me:30`) — this asymmetry
   is the skill's own rule, not a rationalisation invented here.

## The core tension

grill-me is human-in-the-loop dialogue over a design tree; both review stages are static,
one-shot, child-run artifact inspections returning a findings block that a parent/orchestrator
adjudicates and persists. Any integration must answer **who gets grilled, by whom, over
which channel, and when** — and must do it without breaking: the reviewer's static
read-only shell, the block contract (empty verdict = no review, `SKILL.md:382`), the
≤1/≤2 iteration bounds, "the plan is the single source of truth", "reviewers never write
files", and the cost rule "never pay for agents a task does not need" (`SKILL.md:29–35`).

The one structural opening the skill itself provides: at the loop bound it already
escalates to the human — "surface residuals to the user instead of looping"
(`SKILL.md:346`) — and verification already treats "the user explicitly accepted the
residuals" as a legitimate terminal state (`SKILL.md:478`). grill-me is, at minimum, a
disciplined **format** for that existing escalation path.

## Candidate shapes

### (a) Parent-orchestrated grill — reviewer stays static, the PARENT grills the USER with the reviewer's open questions, frontier-disciplined

- **Mechanics:** the reviewer returns its usual block; the parent (or orchestrator on
  full Tier 3 — but see Open question 5) converts the residual, un-settleable items into
  a frontier of numbered questions with recommended answers and puts them to the user —
  before dispatch at the plan stage, before acceptance at the work stage.
- **Cost:** a human round-trip per Tier 3 round that has residuals the parent cannot
  settle itself; parent must transform *findings* (claims) into *questions* (decisions)
  without laundering facts into the user's lap — grill-me forbids asking the user
  anything look-up-able (`grill-me:30`).
- **Invariants strained, not broken:** reviewer staticity, block contract, writer rules
  and single-source-of-truth all survive *if* answers are folded back into `## Plan` and
  re-reviewed rather than living in a side dialogue. Strained: "cheapest safe tier"
  (`SKILL.md:29`) — human round-trips are the most expensive resource in the round; and
  grill-me's "do not act until the user confirms" (`grill-me:32`) would, applied
  unconditionally, add a mandatory user gate before every worker dispatch, which the
  ≤1-iteration revision loop currently avoids (`SKILL.md:284–285`).
- **Verdict: viable, but only as an escalation (merge with (d)).** As a default on every
  round it converts an automated pipeline into a human-paced one.

### (b) Agent-to-agent grill — the reviewer grills the plan author / worker via the intercom instead of returning a block

- **Mechanics:** reviewer asks questions through the supervisor/contact channel until the
  frontier closes; the plan author (orchestrator run) or worker answers.
- **Cost:** multiple round-trips on the most expensive models in the round
  (`glm-5.3:cloud` plan reviewer × `glm-5.3-flash` orchestrator); the child's context
  must survive across intercom turns.
- **Invariants broken — this shape is structurally incompatible:**
  - "Return ONLY" the block (`role-contracts.md:6`, `:93`) — the block contract ceases
    to exist, so the "empty reviewer verdict means no review" pitfall (`SKILL.md:382`)
    has nothing to attach to: a Q&A transcript is not a verdict and cannot drive the
    rework loop or verification step 7's "no open P1" check (`SKILL.md:484–485`).
  - Reviewer staticity/read-scope: grill-me makes the *interviewer* responsible for
    facts (`grill-me:30`), but the plan-review contract explicitly forbids repo-wide
    sweeps, builds, tests (`role-contracts.md:72–73`). A grilling reviewer must either
    break its read scope or take the author's answers on trust — and an author answering
    questions about its own plan is the least independent source available.
  - "Children do NOT receive full reports verbatim" / plan-is-single-source
    (`SKILL.md:35`, `:235–237`): an agent-to-agent Q&A is a second decision stream; if it
    is not folded into `## Plan` it competes with the plan, and if it is folded in it is
    just the existing ≤1 revision loop wearing a question mark.
- **Verdict: rejected.** It preserves grill-me's loop but destroys both review contracts,
  and it substitutes the artifact's author for the decision-owner — the one substitution
  grill-me's rationale forbids.

### (c) Grill-shaped block — keep the single static block, but the reviewer's residual output must be numbered questions each with a recommended answer; the parent relays only what it cannot settle itself

- **Mechanics:** the PLAN REVIEW block gains an optional line, e.g.
  `Open decisions: <Q1 … ➡️ recommendation>`, for genuine forks (two defensible
  approaches) the reviewer can see but should not pick. Verdict/P1/P2 semantics stay.
- **Cost:** near zero at runtime; the cost is a contract amendment — "the ONLY report
  formats" (`role-contracts.md:6`) must be edited, and the optional line must not become
  a channel for laziness (a reviewer that converts every P1 into a question has produced
  an empty verdict, i.e. "no review", `SKILL.md:382`).
- **Invariants:** staticity, writer rules, bounds all intact. This is format adoption —
  grill-me's shape without its loop — and it composes with (a)/(d): the parent relays
  `Open decisions` items it cannot settle from the specs.
- **Real gap it fills:** today a reviewer facing a genuine decision fork must either
  overstate it as a P1 (fabricating a defect) or silently pick a side (overstepping its
  static role). Neither is honest; an explicit optional line is.
- **Verdict: adopt as an optional line in the PLAN REVIEW block only** (see
  Ready-to-paste wording). It is the minimal, loop-free residue of grill-me that is
  compatible with the existing contract.

### (d) Grill as an escalation only — normal rounds keep today's block; a grill fires only when the reviewer returns a P1 the author cannot settle, or on a tier escalation

- **Mechanics:** identical to (a) but gated. Trigger: a plan-review P1 survives the ≤1
  revision because settling it requires a choice the specs do not determine (a real
  trade-off the owner owns). The parent then puts the whole residual frontier to the
  user in grill format, folds the answers into `## Plan` as pinned decisions, and the
  reviewer re-reads the revised sections (which is exactly what it already does,
  `SKILL.md:284`). At the work stage, the analogue is the existing "at the bound,
  surface residuals to the user" (`SKILL.md:346`) and the mid-round escalation path
  (`SKILL.md:121`) — the format changes, the machinery does not.
- **Cost:** rare by construction; bounded by being counted against the existing bounds
  (a grill round *is* the plan-review iteration, not an extra one).
- **Invariants broken:** none. This shape *strengthens* two existing rules — the
  bound-escalation rule (`SKILL.md:346`) gains a disciplined format, and the
  "adjudicate findings against source before rework" pitfall (`SKILL.md:454`) gains its
  natural continuation for the residue that cannot be adjudicated from source.
- **Verdict: recommended primary shape** (with (c) as its optional input format).

### (e) No integration at work review — grill only at the plan stage

- **The claim to test:** by work-review the decisions are locked, and the diff is frozen
  evidence, so grilling there re-opens pinned decisions.
- **Is the asymmetry real or a rationalisation?** Real, and provable from grill-me's own
  rule rather than from convenience: "don't ask the user for anything you could look up
  yourself. The decisions are the user's" (`grill-me:30`). A plan's residual content is
  choices underdetermined by the code — decisions, the user's. A diff's residual content
  is claims about code that is now on disk — facts, the agents' job, verified by the
  static reviewer and the parent's authoritative build/test pass (`SKILL.md:312–325`).
  The skill already routes every work-stage outcome mechanically: P1s → bounded rework
  (`SKILL.md:338–341`), phantom findings → parent adjudication against source
  (`SKILL.md:454`), plan-contradicting improvisations → rework, genuine scope growth →
  tier escalation (`SKILL.md:121`). A worker's *legitimate* deviation on an unanticipated
  seam is already surfaced as a WORKER REPORT deviation line the parent adjudicates —
  one bounded decision, not a frontier.
- **The one exception that proves the rule:** if a diff-review finding would require
  changing a decision rather than fixing code, that is not a work-review grill — it is a
  mid-round escalation (`SKILL.md:121`) or a re-opening of the plan, handled at the plan
  stage with the reviewer re-reading revised sections. Grilling at work-review would
  invert plan-review criterion (v), whose whole job is to catch decisions being silently
  re-opened (`SKILL.md:281`; `role-contracts.md:88`).
- **Also load-bearing:** the freeze. The patch is frozen so the reviewer and the parent's
  build verify the same artifact (`SKILL.md:312–317`); a post-freeze decision dialogue
  invalidates the review target and re-creates the "keep a diff reviewable" hazard
  (`SKILL.md:364`).
- **Verdict: adopt — no grill at work-review.** The residual value there is zero once
  (d)'s plan-stage escalation exists.

## Acceptance — what it is today

Verified per `SKILL.md` step 5 (`SKILL.md:327–334`) and the orchestrator contract
(`role-contracts.md:11–21`).

- **Tiers 1–2:** the parent adjudicates findings and transcribes the verdict into
  `## Acceptance` — "criteria checklist, build/test numbers, P1 list or CLOSED, residual
  P2s" (`SKILL.md:327–329`).
- **Tier 3 full:** the orchestrator-accept run receives the REVIEW block + the parent's
  build/test numbers and writes `## Acceptance` (`SKILL.md:329–331`); its contract:
  "adjudicate the REVIEW block against your criteria and write the `## Acceptance`
  verdict (CLOSED, or remaining P1s)" (`role-contracts.md:13–14`).
- **Tier 3 lean:** the parent transcribes the acceptance itself, on the reviewer's
  verdict plus its own authoritative pass (`SKILL.md:331–333`).
- **UI rounds:** when the verdict is CLOSED and the UI trigger fires, acceptance also
  appends the tester-scope handover (`SKILL.md:333–334`) — derived mechanically by the
  orchestrator from the changed-files list (`role-contracts.md:14–21`), i.e. facts,
  not decisions.
- **Terminal conditions:** the round doc must carry "an explicit verdict (CLOSED, or
  remaining P1s listed)" (`SKILL.md:476–477`), and verification passes only when "No
  P1 findings remain unaddressed, **or the user explicitly accepted the residuals**"
  (`SKILL.md:478`). Residuals reach acceptance only after the bounded rework loops —
  "At the bound, surface residuals to the user instead of looping" (`SKILL.md:346`) —
  after which step 8 folds durable outcomes into the spec (`SKILL.md:347–348`).

Scope note on the owner's wording ("acceptance test"): the closest literal match in the
skill is the step-6 UI tester pass — an adversarial bug hunt over delivered UI
(`SKILL.md:335–337`) — but the tester is scoped verbatim and is "not a second reviewer"
(`SKILL.md:23–25`); its findings route through the rework loop and terminate at step 5.
The decision-bearing stage the owner means is **step 5**; this extension analyses
step 5.

## Is acceptance the better home? (testing the owner's claim)

Single axis, per grill-me's own rule: is the residual a **decision the owner owns, or a
fact an agent can look up** (`grill-me:30`)?

What is actually open at acceptance, item by item:

| Residual | Decision or fact? |
|---|---|
| Residual P2s (`SKILL.md:328`) | the finding is a fact; accept / defer / backlog is the owner's call — **decision** |
| Open P1 at the rework bound (`SKILL.md:346`) | **decision** — the skill already assigns it to the user ("the user explicitly accepted the residuals", `SKILL.md:478`) |
| Unmet / descoped acceptance criterion | did-the-test-pass is a fact (parent numbers are the only build/test authority, `SKILL.md:29–33`); descope-vs-rework-vs-accept is a **decision** |
| Explicitly deferred scope | **decision** |
| CLOSED-with-caveats | the caveats are owner sign-offs — **decision** |
| Tester-scope handover (`SKILL.md:333–334`) | mechanically derived from the changed-files list — **fact**, no grill content |

Acceptance residuals are predominantly **owner-owned decisions** — the same side of the
axis as plan residuals (which is why the plan-stage escalation was recommended), and
the opposite of diff residuals (why work review was excluded).

**The channel argument — the owner's strongest point, and it verifies.** The skill
ALREADY requires the user at acceptance whenever residuals exist: verification item 5
makes "the user explicitly accepted the residuals" a passing condition (`SKILL.md:478`),
and the rework-bound rule already commands surfacing residuals to the user
(`SKILL.md:346`). At the plan stage the user is normally not in the loop mid-round at
all — the original recommendation had to *create* a pause there. At acceptance the
pause already exists in the skill's own passing conditions; a grill there adds
**format, not channel**. It is also the one stage where grill-me's "do not act on it
until the user confirms" (`grill-me:32`) matches a confirmation the round already
requires.

**Honest counterweights — "more useful" is not "the only useful":**

1. **Clean rounds have nothing to grill.** An unconditional acceptance grill adds a
   human round-trip to every round for zero decisions. It must be gated on a non-clean
   verdict (shape (d) below) — then it costs nothing on clean rounds and formalizes
   what `SKILL.md:478` already demands on dirty ones.
2. **Latency-to-correction.** A decision silently assumed at plan time and first
   surfaced at acceptance is the most expensive way to find it — the worker run, review
   cycles and the rework bound have all been burned on it. The step-2b escalation
   exists precisely to catch those pre-dispatch, where the skill itself calls a plan
   defect "the cheapest defect to fix" (`SKILL.md:93–96`). The two stages therefore
   cover **complementary decision classes** — plan: decisions the round is about to act
   on; acceptance: what to do with the residue the round leaves. Neither replaces the
   other.
3. **Who writes the verdict (full Tier 3).** The orchestrator-accept run owns
   `## Acceptance` (`SKILL.md:329–331`; `role-contracts.md:13–14`) under the
   sole-writer rule (`SKILL.md:128–136`). A parent-run grill is compatible only if the
   user's answers reach the accept run **as pinned decisions before it writes**.
   On Tiers 1–2 and lean the parent is both griller and transcriber — no sequencing
   problem at all.

**Conclusion of the test: the owner's claim holds.** Acceptance is the better
*default* home — the one stage whose residuals are decisions AND whose user channel
the skill already requires. It demotes the plan-stage grill from "primary" to
"complement"; it does not delete it.

## Candidate shapes at acceptance (mirroring the plan-stage a–e)

### (a) The parent grills the owner on the residual frontier before `## Acceptance` is written

- **Mechanics:** REVIEW block (and tester block, if any) in hand, the parent identifies
  the residual decisions — everything that would make the verdict non-clean — and puts
  the whole frontier to the user in grill format before the verdict is written.
- **Cost:** one human round-trip, but only on non-clean verdicts — and `SKILL.md:478`
  already demands user sign-off there, so the marginal latency is the format itself.
- **Invariants broken:** none. The parent asking the user is not writing the round doc
  (the sole-writer rule governs who *writes*, `SKILL.md:128–136`); on full Tier 3 the
  answers must travel into the accept-run brief (counterweight 3 above).
- **Verdict: adopt — the core of the revised recommendation.**

### (b) The orchestrator-accept run grills the parent, agent-to-agent

- **Mechanics:** the accept run poses its open questions to the parent over intercom
  (`SKILL.md:440`) instead of writing a verdict.
- **Cost:** extra intercom round-trips on the round's most expensive model; and the
  parent's answers are not authoritative for accept/defer calls the *user* owns —
  verification assigns residual acceptance to the user, not to the parent
  (`SKILL.md:478`).
- **Invariants broken:** the same decision-owner substitution as plan-stage (b), plus a
  second decision stream outside `## Acceptance` competing with the verdict it must
  write.
- **Verdict: rejected** — same reasons as plan-stage (b).

### (c) Grill-shaped `## Acceptance`: residuals rendered as numbered questions with recommended answers, plus the owner's recorded answers

- **Mechanics:** the residual P2 list and caveats the acceptance section already
  records (`SKILL.md:328`) are formatted as `Qn + ➡️ recommendation + owner's answer`.
- **Cost:** near zero — formatting inside a section the verdict-writer already owns. No
  block amendment at all (unlike plan-stage (c), which had to amend the reviewer's
  contract).
- **Invariants:** verification requires "an explicit verdict (CLOSED, or remaining P1s
  listed)" (`SKILL.md:476–477`) — the question list must decorate, never replace, the
  verdict line.
- **Verdict: adopt as formatting guidance** — the natural persistence form of (a).

### (d) Grill only when the verdict is NOT a clean CLOSED

- **Mechanics:** trigger = an unaddressed P1 at the rework bound (`SKILL.md:346`,
  `:478`), any unmet or descoped criterion, explicitly deferred scope, or residual P2s
  needing an accept/defer/backlog call. Clean-CLOSED rounds transcribe exactly as today.
- **Cost:** none on clean rounds; on dirty rounds it is a prescribed form for a user
  confirmation `SKILL.md:478` already requires.
- **Invariants broken:** none — it *strengthens* the bound rule (`SKILL.md:346`) and
  verification item 5 (`:478`) with a format.
- **On the ">N residual P2s" variant:** a mechanical count is the wrong gate —
  readability-nit P2s are common and need no owner call. Gate on the adjudicator's
  "this verdict is not clean", not on a number.
- **Verdict: adopt as the trigger** for (a)+(c).

### (e) No integration at acceptance

- **Argument for:** the channel already exists (`SKILL.md:478`), so grill-me adds only
  formatting — arguably not worth a skill edit.
- **Counter:** "the user explicitly accepted the residuals" currently has **no
  prescribed form**, so it is easily satisfied by a loose "user said fine". The
  frontier + recommendation + numbering discipline makes the acceptance auditable and
  one-round-efficient — and the owner explicitly asked for it.
- **Verdict: rejected.** The owner's claim holds at this stage.

## Stage ranking

1. **Acceptance (step 5) — primary home.** Residuals are owner-owned decisions
   (`SKILL.md:328`, `:346`, `:478`) and the user channel already exists there — grill-me
   adds format, not a new pause. Works in every mode: Tiers 1–2 and lean (the parent is
   both griller and transcriber, `SKILL.md:327–329`, `:331–333`) and full Tier 3 (the
   parent grills; answers pinned into the accept-run brief, `SKILL.md:329–331`).
2. **Plan review (step 2b) — retained escalation.** Plan residuals are decisions too,
   but the channel is new there, so it stays gated on a P1 surviving its one revision;
   its unique value is pre-dispatch correction of decisions the round is about to spend
   worker and review cycles on (`SKILL.md:93–96`).
3. **Work review (step 4b) — still excluded.** Facts, freeze, mechanical routing
   (`SKILL.md:312–317`, `:364`; `grill-me:30`); nothing in this extension overturns the
   original shape (e).

The ranking does **not** flip between Tiers 1–2, Tier 3 full, and Tier 3 lean —
acceptance is the user-facing stage in every mode; only the relaying mechanics differ
(transcribe directly vs pin into the accept brief).

## Recommendation

**Revised after owner input: grill-me's primary home is step 5 — acceptance — gated on
a non-clean verdict. The plan-stage escalation is retained as a complement; work
review stays excluded. The acceptance grill ADDS TO the step-2b escalation; it does
not replace it.**

- **Acceptance (primary):** when the adjudicated verdict would be anything other than a
  clean CLOSED, the parent surfaces the whole residual frontier to the user in grill
  format (one round, numbered questions, each with a recommended answer) **before the
  verdict is written** — on Tiers 1–2 and lean it transcribes the answers into
  `## Acceptance` as recorded owner decisions; on full Tier 3 it pins them into the
  orchestrator-accept brief. This formalizes a user confirmation the skill already
  requires (`SKILL.md:478`) instead of inventing a pause, and the residuals there are
  owner-owned decisions by the skill's own assignment (`SKILL.md:328`, `:346`).
- **Plan stage (complement, demoted from primary):** the step-2b escalation stands
  exactly as originally recommended — grill only when a plan P1 survives its one
  revision because it hides an owner-owned decision. The two stages cover disjoint
  decision classes: step 2b settles choices **before** the round acts on them (a
  decision first surfaced at acceptance is the expensive failure mode,
  `SKILL.md:93–96`); step 5 settles the **residue's disposition**. The step-2b wording
  drafted below is unchanged.
- (c)'s optional `Open decisions:` line in the PLAN REVIEW block is still recommended
  as the plan-stage input; at acceptance **no block amendment is needed** — the
  residual questions live inside `## Acceptance`, a section the verdict-writer already
  owns.
- **What it would NOT replace (unchanged):** the static reviewer shells and block
  contracts; the diff-review pass (step 4b) — still no integration in any respect; the
  mid-round escalation machinery; the rework loop and its bounds; the round-doc writer
  rules (the grill asks, the verdict-writer writes); the model ladder. grill-me is
  imported as a **format and discipline for user touchpoints the skill already
  requires** (`SKILL.md:346`, `:478`), not as a new role, channel, or loop.

## Failure modes

1. **Grill-loops masquerading as review.** A reviewer that emits questions instead of
   verdicts has verified nothing — the "empty reviewer verdict means 'no review'" pitfall
   (`SKILL.md:382`) applies verbatim to a question-list block. Guard: the verdict, P1/P2
   and gates lines stay mandatory; `Open decisions:` is optional and never substitutes
   for a P1.
2. **Unbounded rounds vs hard bounds.** grill-me is "relentless" until the frontier is
   empty (`grill-me:10`, `:32`); the skill is ≤1 plan-review iteration (`SKILL.md:285`)
   and ≤2 rework (`SKILL.md:341`, verification at `:478`). Guard: a grill round **is**
   an iteration — it substitutes for, never supplements, the bound; if the frontier is
   still open after one grill round, stop and report residuals, exactly as the bound rule
   already commands (`SKILL.md:346`).
3. **Re-opening pinned decisions at work-review.** Plan-review criterion (v) exists to
   catch silent re-opening (`SKILL.md:281`; `role-contracts.md:88`); a work-stage grill
   would make the reviewer the party doing the re-opening, against a frozen patch whose
   whole reviewability rests on the freeze (`SKILL.md:312`, `:364`). This is the
   strongest argument for (e), and it is why the recommendation puts the grill before
   dispatch, where decisions are legitimately open.
4. **Fact-laundering into the user's lap.** The parent converting reviewer *findings*
   into user *questions* must not ask the user anything look-up-able (`grill-me:30`) —
   e.g. "does route X exist?" is the reviewer's feasibility job (`SKILL.md:275–277`),
   never a grill question. Only spec-underdetermined trade-offs reach the user.
5. **Human-latency stall.** A mid-round mandatory user gate would park the round's
   parallelism (parent build ∥ static reviewer, `SKILL.md:312–317`) on a human response.
   Guard: escalation-only trigger; normal rounds never pause.
6. **Grill answers arriving after the verdict is written (full Tier 3).** The
   orchestrator-accept run owns `## Acceptance` (`SKILL.md:329–331`;
   `role-contracts.md:13–14`); if the parent grills after that run has written, the
   user's answers have no legitimate writer — appending them post hoc would break the
   sole-writer rule (`SKILL.md:128–136`). Guard: the grill runs **before** the accept
   run and its answers travel in the brief as pinned decisions. On Tiers 1–2 and lean
   the parent is both griller and transcriber, so the hazard does not exist.

## Ready-to-paste wording

Minimal edits — three insertions: step 2b and step 5 in `SKILL.md`, plus one
optional block line in `role-contracts.md`. Step 4 still gains nothing.

**1. Into `SKILL.md` step 2b, after the "≤1 plan-review iteration" sentence
(`SKILL.md:285`):**

> **Grill escalation (plan stage only).** If settling a plan P1 within the one revision
> requires a choice the specs do not determine — a genuine trade-off the owner owns, not
> a fact anyone could look up — do not loop the reviewer. The parent surfaces the whole
> residual frontier to the user in grill format: every open, prerequisite-settled
> decision in one round, each numbered, each with a recommended answer. One grill round
> maximum, counted as the plan-review iteration. Fold the answers into `## Plan` as
> pinned decisions before the reviewer re-reads the revised sections; the worker is
> still never dispatched on a plan with an open P1.

**2. Into `role-contracts.md`'s plan-review block definition
(`role-contracts.md:93–98`), as a new optional line before `Gates:`:**

>     Open decisions: <none | Q1: <decision fork the plan leaves open> — ➡️ <recommended answer>   (genuine forks only; never a substitute for a P1)

**3. Into `SKILL.md` step 5, after the tester-scope sentence (`SKILL.md:333–334`):**

> **Residual grill (acceptance).** When the verdict would be anything other than a
> clean CLOSED — an unaddressed P1 at the rework bound, an unmet or descoped criterion,
> explicitly deferred scope, or residual P2s needing an accept/defer/backlog call —
> surface the whole residual frontier to the user in grill format **before the verdict
> is written**: every open, prerequisite-settled residual decision in one round, each
> numbered, each with a recommended answer. One round maximum; a still-open frontier is
> recorded as accepted residuals with the open items listed — never looped. Tiers 1–2
> and Tier 3 lean: the parent transcribes the user's answers into `## Acceptance` as
> the recorded owner decisions. Tier 3 full: the parent runs the grill and passes the
> answers in the orchestrator-accept brief as pinned decisions, before that run writes
> the verdict. A clean CLOSED needs no grill.

**What would NOT change:** step 4b (work review) — no wording, no grill, no block
change; the reviewer shell, staticity, and read scope; the REVIEW block format; the
model ladder and lean/full split; the rework loop and bounds; the round-doc writer
rules; every Pitfall. The `REVIEW` (diff) contract deliberately gains no `Open
decisions` line — a diff-stage fork is a mid-round escalation or a re-opened plan,
not a work-review question. The acceptance **verdict line stays mandatory**
(`SKILL.md:476–477`) — the residual grill decorates the residual/caveat list inside
`## Acceptance`; it never replaces the verdict.

## Open questions for the owner

Revised after the acceptance extension. The original lean-vs-full and
who-runs-the-grill questions are settled by it: acceptance reaches the user in every
mode (`SKILL.md:327–333`), and the parent is the only role with a user channel in every
mode (`SKILL.md:440`). Q1 supersedes the original adoption question; Q4–Q5 are new.

❓ **Q1** — **Confirm the two-stage shape?** Acceptance-stage residual grill (primary,
gated on a non-clean verdict) + plan-stage escalation (complement) + no integration at
work review.

➡️ **Recommended: yes.** The owner's claim held: acceptance is the one stage whose
residuals are decisions *and* whose user channel the skill already requires
(`SKILL.md:478`); the plan-stage escalation survives because pre-dispatch correction is
strictly cheaper than discovering the same decision at acceptance (`SKILL.md:93–96`).

---

❓ **Q2** — **Add the optional `Open decisions:` line to the PLAN REVIEW block?**
(unchanged from the original exploration)

➡️ **Recommended: yes.** Today a reviewer facing a real fork must fabricate a P1 or
silently decide — both corrupt the contract. The line is optional, cheap, and gives the
parent a clean input for the plan-stage escalation.

---

❓ **Q3** — **Iteration budgets with two grills?** Does the acceptance grill get a
budget of its own, or count against the rework bound?

➡️ **Recommended: it is not a rework iteration — it is the bound's terminal step.**
`SKILL.md:346` already ends the loop by surfacing residuals to the user; the grill is
that surfacing, formatted. Cap it at one round; a still-open frontier is recorded as
accepted residuals. The plan-stage grill keeps counting as the ≤1 plan-review
iteration (original Q3 answer, unchanged).

---

❓ **Q4** — **Gate threshold for residual P2s:** the adjudicator's judgment ("this
verdict is not clean") or a mechanical count (">N P2s")?

➡️ **Recommended: adjudicator judgment.** Readability-nit P2s are common and need no
owner call; a mechanical count would fire the grill on rounds whose only residuals are
nits, burning the user's attention — the exact cost grill-me's frontier discipline
exists to prevent.

---

❓ **Q5** — **Full-Tier-3 sequencing:** parent grills the user *before* dispatching the
orchestrator-accept run (answers pinned in its brief), or the accept run flags
residuals and a second accept run re-writes after the grill?

➡️ **Recommended: grill before the accept run.** The alternative burns a second accept
run and creates a post-hoc writer problem (the user's answers arriving after
`## Acceptance` was written, with no legitimate writer — `SKILL.md:128–136`). The
residual set is already known from the REVIEW block and the parent's build/test
numbers (`SKILL.md:329–331`); nothing about the accept run's adjudication is needed to
pose the questions.