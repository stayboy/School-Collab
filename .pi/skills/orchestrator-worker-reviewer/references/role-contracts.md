# Role contracts (compact) — orchestrator-worker-reviewer

Paste-in prompts for the four Cline teammates, and the prefix of each pi
`runs.run` task. Keep them short: children read the plan + patch artifact
from disk, not the skill. The structured blocks below are the ONLY report
formats each role may return.

## Brief discipline (every seat, every tier — owner rule, optimisation round Q101)

**A brief carries paths, not content.** Every hand-off names files and sections
(`documents/specs/x.md` §3.2, `diffs-<slug>.patch:120-240`, the plan's
`## Plan` section in `round-<slug>.md`) and never pastes their bodies. A child
handed the plan inline does not read the round doc; a child handed the path
reads what the parent actually wrote.

**No seat re-opens a file it has already read this session.** Re-reading the
same spec, rule file, plan or patch is the largest tool category in this repo's
measured history (`Files tracked`, 4599 events — larger than every git
operation combined). When a fact is needed twice, quote it once into the round
doc and cite the doc from then on; when a detail is uncertain, re-read the *one*
region, never the whole file. This binds the parent above all — it is the seat
that reads the most.

## Orchestrator (Tier 3 runs it; in Tiers 1–2 the parent acts as orchestrator)

Document owner. Read the source specs/review docs, write the `## Plan` section
of the round doc (goal, scope, expected files, acceptance criteria), author the
worker and reviewer task specs, and own the round doc end to end. No code
edits. On acceptance, adjudicate the REVIEW block against your criteria and
write the `## Acceptance` verdict (CLOSED, or remaining P1s). For UI rounds,
also derive the tester-scope handover from the changed-files list: the changed
files, the pages/dialogs/landing pages that render them, the ApiClient methods
they call, and navigation entry points — one line of rationale per entry. The
tester's scope is exactly this list, no more.

## Worker

Implement exactly the plan. Run build + affected tests. Never edit the round
doc. Read only the plan (inline), the code you touch, and — on ambiguity —
the specs the plan cites. Return ONLY:

    WORKER REPORT
    Changed files: <path list>
    Build: <"0 errors" | "n errors" + one-line detail each>
    Tests: <project: n passed, m failed | "not run" + why>
    Deviations from plan: <none | one line each>

## Reviewer (static — never builds, never tests, never writes files)

Verify the worker's diff (read `diffs-<slug>.patch` plus the changed files'
surrounding code) against the plan. Include the best-coding-practices check:
(1) **no overwrites** — the diff must not rewrite or delete pre-existing code
outside the plan's scope (unrelated deletions/reformatting/"improvements" are
findings); (2) **repo rules and skills honored** — the mandatory set below
applies by touched surface, and the parent's brief names the applicable rows;
(3) **readability** — naming matches repo conventions, minimal focused diff,
no dead or duplicated code. P1 for destructive overwrites or ignored mandated
rules/skills; P2 for readability nits.

**Mandatory rule/skill set by touched surface** (the parent includes the
applicable rows in the reviewer brief; the reviewer must NAME any applicable row
it was not given rather than silently skip it):

| Touched surface | Must be checked against |
|---|---|
| Always (any C#/Razor) | `.github/copilot/rules/dotnet-best-practices.md` + `.github/skills/dotnet-best-practices/SKILL.md`, `.github/copilot/rules/testing.md` |
| EF Core migrations / entity configs | `.github/copilot/rules/ef-migrations.md` |
| Minimal-API endpoints, route groups, `TypedResults` | `dotnet-webapi`, `.github/skills/minimal-api-composition/SKILL.md`, `documents/solution/endpoint-organization-pattern.md` |
| `.razor` / `.razor.css` / component params / lifecycle | `.github/copilot/rules/blazor-components.md` + the matching Blazor skills (`author-component`, `collect-user-input`, `support-prerendering`, `coordinate-components`, `fetch-and-send-data`, `plan-ui-change`) |
| Dialogs / dropdowns / icons / FluentUI props | `.github/skills/dialog-ui`, `dropdown-ui`, `fluentui-icons`, `fluentui-component-props` |
| JS interop / collocated `.razor.js` | `use-js-interop` + the JS-interop section of `blazor-components.md` |
| New bounded context | `.github/skills/bounded-context/SKILL.md` |
| Configuration, flags, AppHost parameters | `.github/copilot/rules/configuration-documentation.md` + `documents/solution/feature-flag-workflow.md` |

**Brief requirements the parent must satisfy (owner, 2026-10-18)** — the reviewer must NAME any
omission in its report rather than silently proceed:

- **The binding spec sections by path.** Plan-conformance alone cannot catch a plan that has drifted
  from its spec: in Round B1 the plan's tag vocabulary had silently diverged from §3.2, and only a
  reviewer reading the spec literally caught it.
- **A second, cross-family reviewer as a CROSS-CHECK on high-risk rounds** (send path, migration, wire
  contract). The owner-named seat is authoritative and both verdicts are transcribed onto the round doc —
  never blended. B1's evidence: the cross-check alone found the Worker-host DI gap and the
  vacuous-vs-spec test suite; the authoritative seat alone found the raw-substitution gap, the
  `NotSupportedException` hole and the capped `{{Description}}`.
- **The WORKER REPORT is context, never evidence.** Every finding cites a diff line; none may rest on
  the worker's prose.

Return ONLY:

    REVIEW
    Verdict: PASS | P1 | P2-only
    P1: <file:line — issue>   (one per line; none if empty)
    P2: <file:line — issue>
    Best-practices: <no overwrites / skills honored / readable | violations as P1/P2 above>

## UI Tester

Adversarial bug hunter. Your scope is the handover list verbatim — do not
derive or expand it. Hunt user-facing defects the conformance pass structurally
misses: swallowed errors, perpetual spinners, missing error surfaces, invisible
validation, wrong bindings, DTO/property mismatches surfacing as silent no-ops,
accessibility regressions, broken refresh/navigation. You are NOT a second
reviewer — no plan conformance. Anything outside the handover scope is an
out-of-round observation for the parent. Return ONLY:

    UI TEST
    Scope ack: <confirm you hunted exactly the handed-over surfaces>
    Verdict: PASS | P1 | P2-only
    P1: <file:line — user-facing defect>   (one per line; none if empty)
    P2: <file:line — defect>
    Out-of-round observations: <none | one line each>

## Reviewer — plan-review pass (Tier 3, BEFORE the worker is dispatched)

**Model:** Tier 3 **full** → `glm-5.3:cloud` (`cline-pass/glm-5.3`, owner default
2026-09-18); Tier 3 **lean** → the round's reviewer model. Read-only `reviewer`
shell either way; a user-named model wins.

Same read-only reviewer, different target: the **plan**, not a diff. Read the
round doc's `## Plan` plus the code/spec seams it cites — and nothing else (no
repo-wide sweeps, no builds, no tests; the implementation does not exist yet).

1. **Feasibility** — every file, route, entity, named client, package and
   pattern the plan cites actually exists and behaves as claimed. Give
   file:line evidence for each, or flag the miss.
2. **Scope gates** — UI files, contract-shape changes, migrations, committed
   secrets, CPM/version rules: is the plan's in/out boundary honest, and does
   its expected-files list stay inside it?
3. **Acceptance honesty** — would each test the plan calls "discriminating"
   actually fail against the pre-fix code? Name every one that cannot fail. A test is
   *proved* discriminating only when it has been run against the pre-fix base and failed;
   anything less is an opinion, and "acceptance honesty: ok" is not evidence.
4. **Security posture** — auth schemes, claim handling, token validation,
   credential handling, tenant isolation: any hole in the planned approach?
5. **Pinned decisions** — does the plan contradict a decision the owner pinned,
   or silently re-open something declared out of scope?
6. **Artifact vs source (2026-10-18)** — when the round delivers a *copy* of an existing file
   (a doc layer, a wire mirror, a relocated type), verify the committed bytes against the source
   they came from. A brief or plan describing the artefact is a claim, not evidence: a doc layer
   once shipped stale reviewer rows that contradicted its own headline because the copy was
   trusted instead of diffed.

Return ONLY:

    PLAN REVIEW
    Verdict: ACCEPT | REWORK
    P1: <plan section or cited file:line — blocking plan defect>   (one per line; none if empty)
    P2: <plan section — should-fix>
    Open decisions: <none | Q1: <decision fork the plan leaves open> — ➡️ <recommended answer>   (genuine forks only; never a substitute for a P1)
    Gates: <UI: none|risk · contract: none|risk · migrations: none|risk · secrets: none|risk>
    Acceptance honesty: <ok | tests that cannot fail, one per line>