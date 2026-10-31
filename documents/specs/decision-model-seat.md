# The decision-model seat — an advisory adjudication pass for any tier

Status: **PROPOSED 2026-10-30 — mechanism proven on all three payloads, adoption parked on a
consumer.** Q1–Q34 were answered as recommended across four grill rounds; five of the early answers were
overturned by the vendor documentation and are recorded as superseded (§9). Three probe sittings exercised
the seat on real artefacts — 13 text claims on a mock's source, 6 image claims on a real page (and 5 on an
80% crop of it), and 13 claims on **bUnit-rendered component markup** — all correct, all permutation checks
stable, every row above threshold, for ~$0.01 in total
(`documents/rounds/round-decision-model-spike.md`). **Adoption has not started and now waits on a consumer,
not on evidence** (Q33/Q34): §8's files, the protected-file reviews and the first real use are open. The
harness is checked in as `scripts/round/decision-probe.mjs` so the method is rerunnable rather than described.

Date: 2026-10-30 · Provenance: a session question — *"how do I use `clef:27b` as a decision model
to compare mocks against what is actually implemented?"* — generalised, because the answer is not
about mocks: it is a **seat** the tiered harness does not currently have. The first draft of this
spec was built on the wrong premise (a chat model that reads files and cites lines); the vendor
page for `clef:27b-cloud` overturned it, and §5 records what it actually is.

Companions (neither is restated here): `.pi/skills/orchestrator-worker-reviewer/SKILL.md` — the
tier ladder this seat plugs into — and its `references/models.md`, the **operational id catalog**.
`documents/specs/round-runner-model-policy.md` decision 9 splits policy from ids; this document follows
that split and adds no id catalog of its own. That policy spec links back here under its "Related harness
documents" section, because this seat decides nothing and therefore neither consumes nor displaces any seat
the policy assigns.

---

## 1. The gap this closes

Every seat in the harness today is a **gate** or a **producer**:

| Seat | Produces | Can block a round |
|---|---|---|
| Orchestrator (plan author) | `## Plan`, acceptance criteria | — |
| Plan gate (step 2b) | PLAN REVIEW | yes — P1 blocks the worker |
| Worker | the diff, WORKER REPORT | — |
| Diff gate | REVIEW (Standards / Spec) | yes |
| Acceptance (step 5) | the verdict | yes — CLOSED / REWORK / RESIDUALS |
| UI tester | UI TEST block | yes, within tester scope |

What the harness has **no** shape for is the question that is neither a diff finding nor a
verdict: *"of these N claims about what the code and the screens say, which are true?"* —
mock-vs-built parity, spec-vs-code conformance, claim lists mined out of a spec, a bulk
classification over a bounded set of paths. Those today either (a) get done by the parent as
informal reading with no record, or (b) get pushed into a gate brief optimised for a different
question, quietly weakening that gate at its own job.

The seat is deliberately **weak by construction**: it answers, it never gates. That is what makes
it safe to add on every tier.

## 2. Definition

> **The decision-model seat (DM)** is an **advisory, bounded classification pass**: the parent
> submits a *state payload* (text, JSON, and optionally images) plus a schema of **typed,
> atom-level questions**, and a classifier model returns one answer per question with a
> probability or a choice distribution. It is **never** a gate, an acceptance, a reviewer, or an
> owner decision — and it decides nothing that the owner or a gate decides.

Five properties define it, and all five are load-bearing:

1. **Enumerated.** The parent authors the question schema before the call. The model cannot ask,
   invent or extend questions — the API takes 1–64 named questions and returns answers in that
   order.
2. **Typed.** Each question is a `choice` (2–26 options locally, up to 255 in the cloud), a `noul`
   (a yes/no probability), or a `score`. A question that will not fit one of those is not a DM
   question.
3. **Atom-level.** Every question must be answerable from **one visible atom** of the artifact — a
   label, a field, a column header, a route, a row's presence. Intent-level and holistic questions
   are banned by §6.2, because they fail at the model's weakest measured capability.
4. **Scored, not cited.** Answers arrive as probabilities, not provenance. The parent's
   verification is therefore a **threshold plus a spot-check** (§6.4), never a citation re-grep.
5. **Advisory.** Its output is input to a human or a gate, never a verdict from one (§3).

## 3. What it is not (the rulings this document must not disturb)

Adopting the DM changes **no** existing seat, model or gate. Specifically, unchanged:

| Ruling | Where it lives | Why the DM cannot touch it |
|---|---|---|
| Plan gate is the accepting seat, step 2b | SKILL.md § Procedure 2b | DM output **feeds** a gate brief as evidence; it never writes PLAN REVIEW |
| Diff gate on Tiers 2–3 | SKILL.md § Tiers, `references/models.md` | DM has no diff verdict; it answers claim rows |
| Acceptance is the plan author's, implementer ≠ verifier | SKILL.md § "Acceptance is a gate in every tier" | DM is a third thing; it may not appear as `ACCEPTED` |
| **Two-model light tiers** (owner 2026-10-23) | `references/models.md` § Per-mode model sets | The ruling budgets *models that do judgment work*. A classifier call judges nothing — it answers a fixed schema, costs ~$0.002 and runs in ~0.2 s — so it is recorded as `DM:` on line 1, not counted as a seat (§9 Q17) |
| Unattended rounds auto-accept nothing from the plan author | SKILL.md § "Unattended rounds" | DM never echoes a FRONTIER block and never ratifies a pin (§6.6) |
| A gate must never be the plan author | SKILL.md § Tiers | DM may share the author's model freely — it gates nothing, so diversity is not an invariant here |

**The one-line test:** if removing the DM pass would let wrong work through a gate, the DM was
being used as a gate. It was never supposed to be one.

**The second one-line test** (Q11, restated because probabilities invite the mistake): a
probability is not provenance. A gate needs a `path:line` it can be held to; `confidence: 0.63` is
a number, so nothing in the DM's output can carry a gate's verdict.

## 4. Seat map — the same seat, a different form on every tier

Available on **all tiers**, trigger-automatic (§9 Q17), never counted as a child run (§9 Q2).

| Tier | Existing child runs | DM form | Where its output goes |
|---|---|---|---|
| **0 Solo** | 0 (no dispatch allowed) | **No dispatch and no child.** The parent issues the call itself (a `curl`/script in one command) and records `DM: self` | `## Review` (marked self-run) |
| **1** | 2 (plan gate + worker) | 1–2 calls by the parent, trigger-driven | Between worker and acceptance — the author reads it before checking criteria; it is not a criterion |
| **2** | 3 (plan gate + worker + diff gate) | as Tier 1 | After the diff gate, before acceptance |
| **3 lean** | 4 | 1–2 calls by the parent or the orchestrator run, trigger-driven | (a) pre-plan fact sweep → folded into the **plan gate's brief** as evidence; (b) post-diff parity check → acceptance input |
| **3 full** | 4–6 | as lean; the orchestrator requests it, the parent issues the call | as lean, except the orchestrator folds (a) into the plan it publishes |

Two rules hold on every row:

- **Never concurrent with the seat whose question it duplicates.** Asking the DM "is the diff what
  the plan said" while the diff gate is mid-flight pays twice and invites two answers with no
  arbiter.
- **A pass is bounded to one re-run.** A second re-run needs a reason in `## Decision passes`. A
  pass whose answers mostly failed the threshold is a **schema defect** — rewrite the questions, do
  not repeat them. (Cost makes re-runs cheap; it does not make an unchanged bad schema good.)

## 5. The mechanism — what `clef:27b-cloud` actually is

Verified 2026-10-30 against `https://ollama.com/library/clef:27b-cloud` (the tag page; the library
index and this machine's model registry disagree with it, see §5.4).

| Property | Value | Consequence |
|---|---|---|
| Kind | 27B **decision/classifier** model, Cloudflare, fine-tuned from Qwen3.8-27B, Apache 2.0 | It **does not chat**: one non-autoregressive forward pass scores every option of every question jointly. No prose, no tools, no citations, no file reading |
| API | `POST /v1/systemone` — `{model, state, images?, questions}`, 1–64 questions | The parent builds the whole request; the model fills a schema |
| Cost | **$0.24 / 1M input tokens**, `usage.output_tokens: 0` | An 8K-token, 3-image pass ≈ **$0.002** |
| Latency | median **209 ms**, p95 **239 ms** | Faster than one `grep` round-trip |
| Context | **64K**; cloud input **≤65,536 tokens — "rejected, not truncated"** | Over-long briefs **fail**; they do not trim themselves |
| Images | PNG/JPEG/WebP, **≤4 per request**, ≤4 MiB each, ≤16 MP, ≤8 MiB total | A screenshot pass is per-page-section, not per-page-set |
| Question types | **`noul`** (yes/no → probability), `choice` (→ option + `probabilities` + `confidence`), `score` | See §5.3 — `bool` is rejected with a 400 on this route |
| Variants | local `clef:27b` (256K ctx) and `clef:27b-cloud` (64K); `clef-flash` is smaller/faster but **CLINC 66.8 vs 97.4** | Flash is the wrong variant for closed classification |
| Requires | Ollama ≥0.35.1 (cloud ≥0.40.6) | Checked before a round relies on it |

### 5.1 Measured accuracy — and the line it draws

| Benchmark | Clef | What our questions look like |
|---|---|---|
| BFCL (case exact) | **98.5** | — |
| CLINC150 + OOS (macro-F1) | **97.4** | **This one.** "Is there a Contacts column header?", "does this DTO carry `score`?" |
| ANLI (macro-F1) | 69.8 | "Does this page honour the mock's intent?" — **banned by §6.2** |
| End-to-end business workflows | 62.9–76.3 | Holistic judgments — **also banned** |

The gap between 97.4 and 69.8 is the whole design constraint: at atom level the seat is near-exact;
asked for intent it is roughly a coin-flip dressed as a percentage.

### 5.2 The route (settled — Q16)

**Ollama's own `/v1/systemone`**, called by the parent's script or `curl`, for text **and** images.
No `codemode` dependency (which would need `defaultTools: ["+codemode"]` — currently unset in
`~/.pi/agent/settings.json`, so classifier models are unreachable this session without it), no pi
extension, no key material in the repository's configuration (the key lives in the environment).

### 5.3 The `bool` / `noul` type — settled by probe (2026-10-30)

Ollama's API names the yes/no type **`noul`**; pi's classifier documentation example spells it `bool`.
The spike settled it against the wired route (local Ollama 0.40.8 → ollama.com):

| Spelling | Result |
|---|---|
| `noul` | `200`, `{"noul": 0.9858}` |
| `bool` | **`400` — `question "…": type must be choice, noul, or score`** |

So the pi doc example is **wrong for this route**: use `noul`, and treat a `bool` in any copied example as
a defect. The probe cost 147 input tokens; a wrong spelling fails the whole request, not one question.
Evidence: `documents/rounds/round-decision-model-spike.md` § Worker Report A1.

### 5.4 Two silent-drop traps — and why "supported" is a pair, not a property

| Route | What happens | Detection |
|---|---|---|
| **OpenRouter** `cloudflare/clef` | Answers in TypeSafe's request format **without an error, dropping the images** | none — the answer looks normal |
| **TypeSafe Python SDK** → Ollama | Uploads images in **a format Ollama does not accept** | none — text-only answers, still plausible |

Both are silent. So the seat's capability statement is never *"the model takes images"* — it is
*"this model **on this route** takes images"*, and §11 makes the route part of the record.

### 5.5 Registry rows are not authoritative

`pi --list-models` on this machine reports `ollama clef:27b-cloud  128K  16.4K  no  no` — wrong on
context (64K) and wrong on images (they are supported). The row is a chat-model listing; the model
is a classifier, and classifier models do not appear in `/model` at all. **Read the vendor tag page
for capability and kind; read the registry only for what the harness will resolve.**

## 6. The question contract

The schema is the whole harness. A classifier cannot be talked into a better question.

### 6.1 Shape

```jsonc
// POST /v1/systemone
{
  "model": "clef:27b-cloud",
  "state":  "<text or JSON object>",          // the artifact under judgment
  "images": ["<base64>", "…"],                 // optional, ≤4, atomic visual questions only
  "questions": {
    "q_contacts_column": {
      "type": "choice",
      "instructions": "Does the header row contain a column labelled 'Contacts'?",
      "criteria": { "present": "A column header reads Contacts", "absent": "No such header" }
    },
    "q_score_field": {
      "type": "noul",
      "instructions": "Does the payload's row object include a 'score' field?"
    }
  }
}
```

The verdict vocabulary is therefore **one `choice` per claim**, with the repo's four words as
options — `PRESENT` · `PARTIAL` · `ABSENT` · `UNVERIFIABLE` — which is close to a direct mapping of
the classifier's native shape rather than an imposition on it. `UNVERIFIABLE` is a real option, not
a failure: it is the answer for a question the artifact cannot settle.

### 6.2 Atomisation discipline (honest, because it is restrictive)

- Every question is answerable from **one visible atom**: a label, a field, a column header, a
  route, a row's presence, a value.
- **Banned:** "does this honour the mock's intent", "is the layout right", "does the
  implementation look complete", and every other holistic question. They sit at ANLI-level (~70%),
  not CLINC-level (~97%), and their wrongness is invisible in the answer.
- **Cap ~40 questions** per pass. Past that the schema gets vague long before the context fills.
- **Images only for atomic visual presence** — "does a 'Contacts' header appear", "what label sits
  left of the first row". A screenshot is evidence for a *presence* claim, never for a verdict.
- If a claim cannot be atomised, it is **not** a DM question: it belongs to the UI tester, a gate,
  or the owner.

### 6.3 Thresholds — on the probability, never on `confidence`

| Type | Counts as answered when | Rationale |
|---|---|---|
| `choice` | **max probability ≥ 0.80** | The distribution is directly comparable across options |
| `noul` | **probability ≥ 0.90** | The answer *is* a probability; no derived value needed |
| `score` | not used for claim rows | Ordinal scores have no calibrated threshold |

`confidence` is recorded and **never thresholded**. The vendor's own example returns top
probability `0.8425` against `confidence` `0.6298`, so a 0.80 threshold on `confidence` would
reject a row whose top option is 84% likely — the two quantities are not interchangeable.

These numbers are chosen by judgement, not justified by vendor documentation. The first pass that
disagrees with one records the disagreement as a deviation in `## Decision passes` rather than
silently re-tuning them.

**The one recalibration the evidence supports — and it says *do not tighten* (Q27, 2026-10-30).** Three
independent passes now straddle the question: the mock source (13 claims, 0.8952–0.9690), a permutation re-run of
the same (lowest `c08` 0.8943), and a bUnit-rendered component (13 claims, **0.8836–0.9736**). Every one of them
contains a **confirmed-correct row below 0.90** — `c08` at 0.8943/0.8952 and `d02` at 0.8836. A **0.90**
threshold was considered and **refused on that evidence**: it would have rejected rows the record proves correct.
0.80 stays. This is a floor from 45 observations across three payloads, not a calibration curve: a later pass
clustering near 0.85 is the signal to revisit.

**A missing probability is not a pass (fail closed).** If `probabilities` is absent or non-numeric for a `choice`
answer, the row is **not answered** — never treated as a pass, never defaulted to the top option. This was learned
the hard way during the probe: a scoring bug produced `NaN` for every row, and only the fail-closed comparison
kept it from being reported as a result (`documents/rounds/round-decision-model-spike.md`, Probe sitting 3,
finding 3).

### 6.4 Verification without citations (the replacement for re-grep)

Two things carry this section's weight, and both were **verified rather than assumed** (2026-10-30): answers
do **not** move when the question order moves, and they do **not** move when 80% of the image is cropped away
(13/13 text claims, 5/5 image claims stable in both cases). Since answers carry no provenance, the parent does
four things — all cheap, none optional:

0. **Claim provenance (Q25).** The claim list is **derived from an external artefact** — the mock, the spec,
   the ticket. A claim the author invented is inadmissible, because then the pass grades the author's
   imagination rather than the repository. Truth for the rows is established **after** the pass, by a
   **different actor**, on a **sample of 3–5 rows** carrying `path:line` the checker can reopen. Full ground
   truth is never required: someone who already knows all N answers has no reason to run the seat.

1. **Threshold gate** (§6.3): an answer below its threshold is not reported as an answer.
2. **Stability re-run** (verified 2026-10-30: 13/13 text and 5/5 image stable under reorder + crop): any
   sub-threshold row, and a random 10% of passing rows, are re-asked once with the **question order
   permuted** and, where available, a second image crop. A classifier's answer should not move when the
   order moves. A row that flips is reported as `unstable`, never averaged. At ~$0.002 and ~0.2 s a re-run
   costs less than the argument about it.
3. **Parent spot-check**: 3 rows checked by hand against the artifact, at least one of which the
   model marked high-confidence. A spot-check that contradicts the model demotes the whole pass.

### 6.5 Payload ranking — which artefact answers which atom (Q26)

The two sides of a parity pass are rarely the same kind of file, and the payload decides what a claim can even
mean.

| Payload | Answers | Do not use it for |
|---|---|---|
| **Rendered DOM text** — the default for implementation claims | rendered and data-dependent atoms: is this row present, does this label exist in the output | visual atoms (icons, colour, layout), code structure |
| **Screenshot** (≤4 per request) | atomic visual presence | structure, data paths, anything a human would call "looks right" (§6.2) |
| **Source text** (`.razor`, the mock's HTML) | structural atoms: a field exists on a DTO, a route is registered, the mock *specifies* a column | anything about the rendered page |

Captures are **per section**, not per page: a full DOM dump is the fastest route to the 65,536-token hard cap,
which **rejects** the request rather than trimming it. Capture reuses the repo's existing Playwright harness —
no new mechanism (repo rule: reuse before new infrastructure).

**Measured (2026-10-30).** A bUnit render of one real page (`PeriodUpsert`) produced 45.8 KB of markup =
**21,285 tokens = 32.5% of the cap**, so at most **~3** such captures fit a single request. Most of that is noise a
human would not call content (two date-picker calendar grids, a scoped-CSS attribute on every element), so prune
the capture to the section under judgment. Capturing rendered markup needs **no app boot**: bUnit renders a
component to markup in ~4 s with no database, no identity provider and no lock on build outputs, which is the cheap
way to answer structural rendered atoms. Data-dependent atoms still need a live instance.

### 6.6 Prohibited content

- **No owner decisions.** A question the specs do not determine is a grill question (repo
  `AGENTS.md` § "Posing questions and decisions"), never a DM question. A DM answer must never be
  quoted as an owner pin.
- **No acceptance questions.** "Is criterion 3 met?" belongs to the acceptance seat.
- **No FRONTIER blocks.** The unattended-round plan gate is the only seat that adjudicates the
  frontier; the DM does not echo, accept or refuse pins on any tier.
- **No secrets in `state`.** Redact: the state is sent to a hosted endpoint and billed per token.
- **No holistic questions** (§6.2), and no question whose answer would be a verdict on a seat.

## 7. When it fires (triggers)

Automatic (§9 Q17), on evidence that a **closed claim set with a cheap check** exists:

1. **Mock-vs-built parity** — a visual/UX mock exists and its claims must be measured (§10).
2. **Spec-vs-code conformance** — a durable spec asserts N behaviours and the round needs to know
   which already exist before pricing work. This is the pre-plan fact sweep: a plan built on it
   stops re-litigating facts.
3. **Bulk classification over a bounded artifact** — which of 60 changed paths are UI files for the
   tier gate; which call sites of a renamed member were missed. Mechanical, enumerable, atom-level.
4. **A contested seat claim that is really a fact question.** Adjudicate as evidence — the parent
   still records the seat split and resolves it per the existing disagreement rule; the DM's answer
   is not the resolution.

Not a trigger, ever: anything a test can prove (write the test); anything the owner must decide
(grill); anything a gate must decide (dispatch the gate); open-ended "review this" (diff gate or
tester); long-document comprehension (read it).

## 8. Implementation map (what adoption costs)

| File | Change | Gate |
|---|---|---|
| `documents/specs/decision-model-seat.md` | this document | normal PR |
| `scripts/round/decision-probe.mjs` + `scripts/round/README.md` | **already shipped 2026-10-31** (Q35): the harness as a script, plus its row and section in the round-harness README. Not wired into `gates.sh` — a model endpoint is not a repo precondition | normal PR |
| `documents/specs/round-runner-model-policy.md` | **already shipped 2026-10-31** for the pointer only (Q36): a "Related harness documents" section. No decision in that spec is amended, because this seat settles nothing | normal PR |
| `.pi/skills/orchestrator-worker-reviewer/references/models.md` | **one row**: seat, route, default id, clinepass/provider note, substitution, and the "a classifier call is not a seated model" clause of §3 | **protected** — reviewed before merge |
| `.pi/skills/orchestrator-worker-reviewer/SKILL.md` | one short § "Decision model (advisory classifier seat)": the seat, the trigger list pointer, the `DM:` line-1 record, and "never a gate" | **protected** — reviewed before merge |
| `documents/rounds/README.md` | the `## Decision passes` section (§6 recording shape) beside the existing `## Not run` rule | normal PR |
| `documents/solution/round-runner-model-ledger.md` | DM column: passes, route, rows over/under threshold, re-runs | normal PR |
| `documents/agents/protected-doc-reviews.json` | record for each protected file above — SHA-256 of the exact reviewed text, reviewer, reason | mechanical, enforced by `ProtectedAgentDocReviewTests` |

Prerequisites, stated because adoption fails silently without them: an ollama.com API key in the
environment; an Ollama ≥0.35.1 client if the call is made locally rather than to the hosted tag.
All three are satisfied on this machine as of 2026-10-30 (`OLLAMA_API_KEY` set, `ollama 0.40.8`), and
the probe evidence is `documents/rounds/round-decision-model-spike.md`.

**Doc layer, not code layer.** Per SKILL.md ("policy travels separately"), these ship as their own
documentation commit — never swept into a feature branch's code diff.

## 9. The decisions — settled 2026-10-30

Settled in one grill round ("all as recommended"). Supersessions are recorded, not deleted: the
first draft of this spec was written against a chat-model premise that the vendor page disproved.

| # | Decision | Settled as | Note |
|---|---|---|---|
| Q1 | Opt-in or automatic | **Superseded by Q17** — automatic | The opt-in argument rested on cost and on the two-model ruling; both dissolved at $0.24/M and zero output tokens |
| Q2 | Does a DM call count as a child run | **No** — recorded `DM:` on line 1 | Keeps the tier table's child-run count honest for pricing |
| Q3 | Which shell | **Dissolved** — there is no shell; it is an HTTP call, not a subagent | The first draft named `oracle`/`evidence-auditor`; classifiers are not dispatchable |
| Q4 | May it run unattended | **Yes, evidence only** — never echoes a FRONTIER block, never ratifies a pin | Preserves the 2026-10-21 / 2026-10-23 rulings |
| Q5 | Replaces the UI tester's parity check | **No** | Tester judges rendered behaviour; the DM reads an artifact. Neither substitutes |
| Q6 | Spec or skill section | **Spec**, with operational detail split per policy decision 9 | Three files touch one ruling; an amendment must reach all three |
| Q7 | Vehicle: codemode, extension, or subagent | **Superseded by Q16** — a direct HTTP call | `pi-subagents` has no `systemone` path; `/model` does not list classifiers |
| Q8 | Provider for image work | **Superseded by Q16** — the route is part of the answer, and OpenRouter silently drops images | — |
| Q9 | Images vs source text | **Both, by claim type** (Q15) | Structural claims need text; visual presence needs an image |
| Q10 | Verification without citations | **Superseded by Q14 + §6.4** — probability threshold, never `confidence` | The vendor example (0.8425 vs 0.6298) falsified the original wording |
| Q11 | Do probabilities make it more allowed to gate | **Less — the advisory rule gets stricter** | A probability is not provenance |
| Q12 | `codemode` repo-wide, per-round, or never | **Never** — the route needs no pi tool | No profile-wide settings change for a per-round seat |
| Q13 | Rewrite or park | **Rewrite** §2/§5/§6/§8/§10; §3 stands | This document |
| Q14 | Which number carries the threshold | **Max probability** — `choice` ≥0.80, `noul` ≥0.90; `confidence` recorded only | §6.3 |
| Q15 | Atomisation and images | **Atom-level only**; images for atomic visual presence only; holistic questions banned | §6.2 |
| Q16 | Route | **Ollama `/v1/systemone`**, text and images, no `codemode`; the `noul` spelling is settled (§5.3) | §5.2–5.3, spike A1 |
| Q17 | Re-tier it | **Trigger-automatic on every tier**; `DM:` on line 1; never a gate; Tier 0 = the parent's own call | §4 |
| Q18 | Rename the seat | **Keep "decision-model seat"**; one sentence in §2 states it decides nothing the owner or a gate decides | Renaming would touch five files to fix a one-sentence collision |
| Q19 | Lessons into the repo | **Two accepted**, the third dropped as a corollary | `.github/copilot/rules/agent-lessons.md` |
| Q25 | Who establishes ground truth | **Claims external; truth sampled post-hoc by a different actor** (3–5 rows with `path:line`) | §6.4 item 0 |
| Q26 | Payload for the implemented side | **Rendered DOM default**; screenshot for visual atoms; source for structural; per-section captures | §6.5 |
| Q27 | Recalibrate the thresholds | **0.80 kept; 0.90 refused on evidence** — a confirmed-correct row sat at 0.8943 / 0.8952 | §6.3 |
| Q28 | Run the permutation check before adoption | **Yes — run, and it passed** (13/13 text, 5/5 image) | §6.4 |
| Q29 | Adopt now, or close the DOM gap first | **Close it first** — done: the DOM leg passed on rendered component markup | Q30 |
| Q30 | Route to the DOM leg | **bUnit render, no app boot** — real markup in ~4 s; data-dependent atoms deferred to a live instance | §6.5, round doc Probe sitting 3 |
| Q31 | If the DOM leg had failed | **It would shrink, not kill** — screenshot + source already covered 19/19 claims | moot: the leg passed |
| Q32 | May the seat be used before adoption | **Yes, as method only** — no `DM:` convention, no ledger column, no gate reference until adopted | §8 |
| Q33 | Continue or park the arc | **Stop after the DOM leg**; resume when a consumer exists | round doc verdict |
| Q34 | First real consumer | **Whatever live round next presents a closed claim set** — not the parked round, which waits on an owner deliverable | §7 |

## 10. Worked case — mock vs implemented (the motivating question)

Generic form, so the spec is not about mocks:

1. **Fix the claim list** from the mock's own text — one atom per claim, falsifiable: *the row
   carries a contacts column*; *a state filter exists*; *the list is cohort ∪ submissions*.
2. **Split by claim type.** Structural claims (data path, DTO fields, route existence) go in
   `state` as text excerpts. Rendered visual claims (header present, label wording) go with 1–4
   `images` — and **only** those; "does it look like the mock" is not a question (§6.2).
3. **Call** `/v1/systemone` with a `choice` per claim over `PRESENT | PARTIAL | ABSENT |
   UNVERIFIABLE`.
4. **Threshold and verify** (§6.3–6.4): drop sub-threshold answers, re-run the unstable ones with
   permuted order, spot-check 3 by hand.
5. **Record** `## Decision passes`: route, model, pass count, rows over/under threshold, dropped,
   unstable.
6. **Then** the surviving table feeds the plan (Tier 3) or the author's acceptance check — never a
   gate verdict.

What that pass must not do, stated once because it is the trap: it must not conclude *"the mock and
the portal should converge"*. Whether they should is a **scope decision that already exists in the
repo** (`documents/solution/portal-scope-decision.md`, and the modernization spec's own
out-of-scope line naming the Python portal). The DM answers *what is there*; *what ought to be
there* is the owner's, and in that case it was already settled — the pass exists to stop an agent
re-litigating a settled scope call by accident, not to enable it.

## 11. Verification (how you know an adopted seat is working)

- A pass that reports `ACCEPTED`, a P1, a REVIEW block, or an owner pin ⇒ **schema defect**, logged
  as such.
- A holistic question in a schema (`looks right`, `honours intent`, `is complete`) ⇒ **schema
  defect**; the pass is not reported as evidence until the questions are atomised.
- A row counted as answered while below its §6.3 threshold ⇒ **verification defect**.
- A pass reported as evidence while its scoring harness proved nothing (no real probability present, every row
  `NaN`, zero variance across ratings) ⇒ **harness defect**; assert at least one real probability first.
- An unstable row reported as an answer rather than as `unstable` ⇒ **verification defect**.
- An image-based pass whose record does not name **the route** ⇒ **incomplete record**, because the
  two silent-drop routes (§5.4) make "clef with images" meaningless without it.
- A protected-doc change without its `protected-doc-reviews.json` record ⇒ build fails
  (`ProtectedAgentDocReviewTests`), which is the guard that keeps §8 honest.
