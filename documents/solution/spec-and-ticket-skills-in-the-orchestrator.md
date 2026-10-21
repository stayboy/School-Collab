# Spec and ticket skills in the orchestrator — front-door research and the review-shape update

Status: **RESEARCH (2026-10-21)** for §§1-2, **ADOPTED 2026-10-21** for §3 (Q47-Q52 of the
2026-10-21 grill round). The implementation of §3 is a separate PR, opened **after** the
notification-template authoring redesign merges (Q52).

Provenance: one full arc of this repo's own agents, start to finish - `/to-spec` -> issue #391,
`/to-tickets` -> #392-#395, `/implement-spec` -> the tiered round in
`documents/rounds/round-notification-template-authoring-redesign.md` - followed by the first
two-axis `/code-review` the repo has run on its own work. The findings of that review are the
evidence in §2 and the reason §3 exists.

---

## 1. The front door, as it actually ran

The three skills form a chain, and each one handed a *different* artifact to the next. Naming
those artifacts is the whole value of the chain: the round runner consumes them, so a weak one
starves a whole round.

| Step | Input | Output (the artifact the next step consumes) | Where it landed here |
|---|---|---|---|
| `/to-spec` | A conversation, no document | **A spec that is also a published issue**: problem statement, settled-decisions record (Q1-Q22), design, data model, API surface, security table, out-of-scope list, verification surface, execution plan, as-built placeholder | Spec `documents/specs/notification-template-authoring-redesign.md` -> issue **#391** |
| `/to-tickets` | The spec | **Vertically sliced tickets**, each an issue that names its slice, its exit criteria and its dependencies - never one ticket per layer | **#392** W1 landing/create, **#393** W2 detail/constraints/audit, **#394** W3 ownership, **#395** docs + acceptance |
| `/implement-spec` | The spec + its tickets | **A round run under `orchestrator-worker-reviewer`**, the ticket rows becoming workstreams (W1/W2/W3) rather than commits | Round doc + 3 commits (`2e92ca6c` docs, `a3140736` W1, `16a6d18f` W2) + the uncommitted W3 worktree |

What the chain bought, in this arc's evidence:

- **The spec carried its own grill record**, so the round never re-litigated a settled decision
  (`Q15` every-dimension intersect, `Q67` tie-break, `Q14` the `PanelSection` move). A round that
  has to re-open design questions pays for them twice.
- **Tickets became workstreams, not PRs.** W1/W2/W3 each ended at the build gates and were
  committed on their own; the ticket boundary was what made "stop after W2" safe.
- **The `as-built` placeholder in §10 forced the closing pass** - the spec could not be called done
  while §10 still said "not implemented".

What it did **not** buy, and this is the gap this document closes: the chain stops at
*implementation*. Nothing in `/to-spec`, `/to-tickets` or `/implement-spec` says how the finished
diff is reviewed, so the review step fell back to whatever the operator improvised - here, a
`/code-review` run that found two defects and six drift findings the round's own gates had passed.

---

## 2. What the two-axis review added

Six findings, each with the process lesson it implies. §3 turns the lessons into skill changes;
the findings stay here as the evidence.

| # | Finding (evidence) | Lesson |
|---|---|---|
| 1 | The reviewer seat returns ONE `Verdict: PASS \| P1 \| P2-only` (`references/role-contracts.md:144-150`), so a spec-conformance failure and a standards failure collapse into one line | **L1 - one verdict masks an axis.** A change can pass standards and fail spec, or the reverse; a single verdict lets the passing axis hide the failing one |
| 2 | "Cross-tenant read without `SuppressTenantGuard()`" was almost filed as a rule breach; `ListProvisionalCodedValuesHandler.cs:21` does the same and the guard is used only around writes | **L2 - precedent before citation.** A rule's literal text is not a finding until the nearest sibling handler/component/test is read; where they disagree, the finding is "rule and precedent disagree" |
| 3 | W3 widened the template list read (tenant U blueprint); `TemplateEditor`'s conflict warning kept comparing across scopes and began warning falsely - its own comment said *"Scope joins the comparison with W3"* | **L3 - a widened read invalidates its consumers.** Scope, filter, and `include...` changes must re-derive every consumer in the same plan |
| 4 | Spec §3.2 still said Reset is "destructive"; §6 said the cross-tenant read exists in "exactly one place"; §8 named `TemplateEditorPreviewTests`, which never existed | **L4 - declared deviations and named rows drift.** A deviation amends the spec text in the same PR, and a named verification row must exist on disk |
| 5 | `spawn_agent` aborts in this CLI and the AppHost cannot serve here, so a mandated sub-agent step and the live UI pass could not run | **L5 - unavailable is not silent.** A step that cannot execute is recorded as *not run*, with reason and owner |
| 6 | `Assignments.Tests.Unit` read 1384/1385 on the first parallel-order run, 1385/1385 on re-run, and the failing test passed in isolation | **L6 - a green number hides a flake.** Record the first total, the isolation result and the re-run total |
---

## 3. The review-shape update — adopted 2026-10-21 (Q47-Q52)

Six decisions that change how the reviewer seat reports and what the round doc records. They
belong to the **same skill** as the model policy (`documents/specs/round-runner-model-policy.md`),
which is the policy half of the same split: that file holds model seats and cadence, this section
holds review shape. The mechanics - the brief's wording, the return block's label spelling, the
round doc's field names - stay in `.pi/skills/orchestrator-worker-reviewer/references/role-contracts.md`
and `documents/rounds/README.md`; this section holds the decisions those mechanics must satisfy.

### 3.1 The six decisions, settled

| # | Decision | Settled as | Note |
|---|---|---|---|
| Q47 | Reviewer verdict shape | **Two verdicts in one seat**: `Standards:` and `Spec:`, each `PASS \| P1 \| P2-only`, findings still `file:line` | One reviewer, not two agents - the axes are reported side by side and never blended (L1). The return block must change shape; prose alone cannot deliver this |
| Q48 | Behavioural lessons | **Three named rows in the brief requirements** (the 2026-10-18 list in `role-contracts.md`): (a) precedent before citation, (b) widened-read blast radius, (c) declared deviation => spec amended in the same PR | They are reviewer *behaviour*, so they belong in the brief, not in a style rule file (L2/L3/L4). (b) is the one that would have caught a real defect |
| Q49 | Mechanised lessons | **Two gates**: (a) every test class named in a spec's verification table exists on disk; (b) no bare `_`-prefixed literal in a **string-typed** component parameter. **Not** gated: `async void` via `Action<T>` | No analyzer infrastructure exists, and a regex would false-positive on legitimate `Func<Task>` adapter sites (L4). Gate (a) may flag pre-existing aspirational rows: if more than five, mark those rows `planned` in their spec rather than weakening the gate |
| Q50 | Unavailable steps | **A mandatory round-doc section `## Not run`** - one row per mandated step that could not execute: step, reason, owner | An unavailable environment is recorded there; never silently skipped, never implied complete (L5) |
| Q51 | Flake protocol | **Any red is re-run in isolation and then as a second full run**; the round doc records all three facts (first total, isolation result, re-run total) | Never just the green number (L6). Creates the evidence a real flake issue needs - #396 is the anecdote this would have replaced |
| Q52 | Landing sequence | **Its own PR, after the redesign PR merges** | The redesign branch is already 40 modified + 9 new files; a skill change reviewed inside a feature diff gets neither the right reviewer nor a clean revert. Whether it shares a branch with this document is decided separately |

### 3.2 The requirements, in operational order

1. **Report two verdicts (Q47).** The reviewer's return block carries `Standards:` and `Spec:`
   separately. A finding belongs to exactly one axis; the report never picks a single winner across
   axes, because that reranking is what the separation exists to prevent. The round doc's `## Review`
   carries the two reports unmerged under their own headings.
2. **Check the precedent before citing a rule (Q48a).** A documented rule's literal text is not a
   finding until the nearest sibling handler, component or test is read. Where precedent and rule
   disagree, the finding is "rule and precedent disagree", not a violation of the rule.
3. **Re-derive a widened read's consumers (Q48b).** When a change widens what a read returns - a
   scope, a filter, an `include...` flag, a tenant union - every consumer is re-derived in the same
   plan, and the plan names them. A comparison or badge written against the narrower semantics is a
   P1, not a follow-up.
4. **Amend the spec in the same PR (Q48c).** Every declared deviation amends the spec text it departs
   from, in the change that introduces it. An `as-built` note is not a substitute: it becomes the only
   true account of a spec that reads otherwise.
5. **Assert named verification rows exist (Q49a).** A test class named in a spec's verification table
   must exist on disk; the gate fails the branch when one does not. A row that is intentionally not yet
   implemented says `planned` - that is the only escape.
6. **Guard the string-attribute trap (Q49b).** A string-typed component parameter must not carry a bare
   `_`-prefixed literal (`Value="_model.Body"` binds the text and silently disconnects the field). The
   guard sits with the other Razor hygiene invariants so the class cannot recur.
7. **Record what did not run (Q50).** A mandated step whose environment is unavailable is recorded in
   the round doc's `## Not run` table with its reason and owner, and the round is reported with that row
   visible. Absence is never silence.
8. **Re-run a red in isolation, then whole (Q51).** One failing test in a full run is re-run alone and
   then as a second full run; the round doc records the first total, the isolation result and the re-run
   total. A flaky pass is recorded as a flake, not as a pass.
9. **Land this separately (Q52).** This section's implementation is its own PR, reviewed on its own
   diff, opened after the redesign PR merges.
### 3.3 Verification

| Contract | Checked by |
|---|---|
| The return block carries both verdicts, and `## Review` carries two unmerged reports | the plan gate's structural check on the next Tier-3 round doc, plus review of `references/role-contracts.md` |
| Every `*Tests` class named in a spec's verification table exists | new `SpecVerificationRowsArchitectureTests` (report the flagged rows first; mark pre-existing aspirational rows `planned` if more than five) |
| No string-typed component parameter carries a bare `_`-prefixed literal | new case in `RazorMarkupHygieneArchitectureTests` |
| A round that skipped a mandated step carries `## Not run` with step, reason and owner | `documents/rounds/README.md` rule + the diff gate on that round's doc |
| A round with a red first run records first total, isolation result and re-run total | `documents/rounds/README.md` rule |
| `async void` via `Action<T>` is **not** gated | recorded exclusion - no analyzer infrastructure; a regex would false-positive |

### 3.4 Out of scope

- Re-opening the vendored `code-review` skill, its two-axis brief or its issue-tracker coupling. The
  reviewer seat restates the axes for this repo's surfaces; it does not adopt that skill as a
  dependency (it spawns sub-agents, which abort in this CLI, and it assumes a tracker).
- Any model seat, provider or cadence decision - those are the nine decisions in
  `documents/specs/round-runner-model-policy.md`, untouched by this section.
- Teaching `/to-spec`, `/to-tickets` or `/implement-spec` to run the review themselves. §1 records
  that the chain stops at implementation; the fix is the reviewer seat's shape (§3), not a fourth
  front-door step. Revisit only if a round still ships a review-passable defect after Q47-Q51 are
  in effect.

---

## 4. What happens next

1. The redesign PR lands first (with its own W3 fixes and the round-doc close-out).
2. This section's implementation is a second PR: `references/role-contracts.md` (two verdicts + three
   brief rows), `documents/rounds/README.md` (`## Not run`, flake protocol), the two gates, and the
   `SKILL.md` pointer. It is documentation plus two tests - no production behaviour.
3. The first round run under the new shape is the evidence of record: if its `Spec:` verdict does not
   move independently of `Standards:`, Q47 did not land and the block is amended, not defended.

## 5. Open questions carried forward

- Whether this document shares a branch with the Q52 implementation PR, or ships as its own docs PR
  (round 2 of the go-forward grill).
- Whether the structural smells from the same review - owner value object, duplicated adopt/promote
  handler bodies, the 991-line `TemplateEditor`, the test fixture's tenant default - become one
  follow-up issue or several.