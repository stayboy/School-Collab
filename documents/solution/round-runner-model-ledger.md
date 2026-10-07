# Round-runner model ledger

One row per tiered round, per `documents/specs/round-runner-model-policy.md` (Q8). The
orchestrator writes the row when the round doc closes; a round is not closed until its row
exists. The rows exist to feed one number — **iterations per round** — never raw P1 counts.

**Seed note (2026-10-06).** The rows below cover the 2026-10-03 → 2026-10-05 portal arc — the
corpus the policy was mined from. Earlier rounds are deliberately **not** re-mined: their
seat→model assignments are recorded on line 1 of each round doc in `documents/rounds/` and
remain authoritative there. Solo rounds have no seats and no gate and are not rowed. An em dash
(—) in a data cell means "not recorded in this seed" — the round doc remains the record.

**Post-seed rows (from 2026-10-06) are written live**, one per closed round, and are not part of
that mined corpus — they are the corpus going forward.

**Unattended rounds.** When the owner runs a round unattended (skill section
"Unattended rounds"), flag it in that row - an unattended pin (recommendations accepted
without a human) and an owner pin must never look alike.

| Round (doc) | Date | Tier | Provider | Author | Plan gate | Worker | Diff review | Gate P1s | Rework iterations | Rework cause |
|---|---|---|---|---|---|---|---|---|---|---|
| `teacher-scope-auth` | 2026-10-03 | 3 full | ollama-cloud | glm-5.3-flash | glm-5.3 | deepseek-v4.1-flash | kimi-k2.7-code | — | — | — |
| `portal-teacher-surface` | 2026-10-03 | 2 light | ollama-cloud | parent | — (light: no plan gate) | deepseek-v4.1-flash | kimi-k2.7-code | — | — | — |
| `snapshot-config-guard` | 2026-10-03 | 2 light | ollama-cloud | parent | — | deepseek-v4.1-flash | kimi-k2.7-code | — | — | — |
| `authoring-subject-cascade-flake` | 2026-10-04 | 2 light | ollama-cloud | parent | — | light set of record¹ | light set of record¹ | — | — | — |
| `dev-teacher-identity-wiring` | 2026-10-04 | 2 light | ollama-cloud | parent | — | light set of record¹ | light set of record¹ | — | — | — |
| `portal-spec-fanout-guard` | 2026-10-04 | 2 light | ollama-cloud | parent | — | light set of record¹ | light set of record¹ | — | 1 fix-up (quota-killed worker re-run, parent-verified) | provider HTTP 429 |
| `portal-session-adoption` | 2026-10-05 | 3 lean | clinepass → ollama-cloud (mid-round, owner) | clinepass glm-5.3 | clinepass deepseek-v4.1-flash (2 passes) | glm-5.3-flash (timed out) → escalation deepseek-v4.1-flash | mimo-v2.6-flash → kimi-k2.7-code | plan **5 P1** + 8 P2; diff slice A **1 P1** | 2 plan revisions; 1 worker escalation; 1 diff rework | plan: stale wire-spelling claims; worker: 30-min timeout |
| `portal-submission-grade` | 2026-10-05 | 3 lean | ollama-cloud | glm-5.3-flash | kimi-k2.7-code — **0 P1, the first** | deepseek-v4.1-flash | kimi-k2.7-code (split slices) | diff slice A **1 P1** + 2 P2 | 1 rework | wire-spelling (snake_case vs camelCase `PortalClaims` names — every real call would have 401ed) |
| `portal-shell-and-ward-retirement` | 2026-10-05 | 2 light | ollama-cloud | parent | — | deepseek-v4.1-flash | kimi-k2.7-code | diff **0 P1** + 2 P2 | 0 (both fixed pre-commit) | import placement + E302 formatting |
| `assignment-index-menu-flake` | 2026-10-06 | 2 light | ollama-cloud | parent | — | deepseek-v4.1-flash | kimi-k2.7-code | diff **0 P1** + 2 guard-internal notes (1 P2 parse, 1 P3 stripper) | 0 (P2 applied verbatim pre-PR; no re-dispatch) | — |
| `portal-claims-port-binding` | 2026-10-06 | 2 light | ollama-cloud | parent | — | deepseek-v4.1-flash | kimi-k2.7-code | diff **0 P1** + 2 P2 (one owner-accepted as shape-vs-semantics) | 0 | — |
| `targets-audience-typed` | 2026-10-06 | 2 light | ollama-cloud | parent | — (light: no plan gate) | deepseek-v4.1-flash | kimi-k2.7-code | — | 2 (rework + focused fix) | reviewer P1s: `SectionCard` `ItemTemplate` ignores `ItemActions`; jump-nav anchor drift; UX-21 fail-closed; stale `AllStudents`; TGT-16 preview lost |
| `drop-primary-grade` | 2026-10-06/07 | 3 full (UI) | ollama-cloud | glm-5.3-flash | glm-5.3 (substitute; `ollama/glm-5.3:cloud` unavailable) | deepseek-v4.1-flash | kimi-k2.7-code (split core+app slices, PASS); UI tester minimax-m3 NO VERDICT (timed out twice) | plan REWORK: 2 P1 + 1 seam on re-read (3rd teacher-scope seam, `SubmissionRepository`) | 1 plan revision; 0 diff rework | plan feasibility seams in the teacher-scope rule's application |
| `targets-dialog-picked-zone` | 2026-10-07 | 2 light | ollama-cloud | parent | — | inherited session model (— `model` field omitted at dispatch: deviation, not the skill default) | inherited session model | diff 0 P1 + 2 P2 (fixed) + 3 P3 | 0 (parent fixed the P2s post-acceptance) | stale "survive a category switch" docs; untested same-category guard |
| `assignment-rules-policy-rework` | 2026-10-07 | 3 full (UI) | ollama-cloud | glm-5.3-flash | glm-5.3 — REWORK then ACCEPT (1 P1 + 3 P2) | deepseek-v4.1-flash (timed out at 30 min) → escalation kimi-k2.7-code | kimi-k2.7-code slice B + glm-5.3 slice A (escalation re-verify), both PASS 0 P1 | plan 1 P1 + 3 P2; 0 diff P1 | 1 plan revision | plan feasibility/coverage seams; worker timeout at the cap |
| `authoring-compact-fields` | 2026-10-07 | 2 light (REDUCED gate set) | ollama-cloud | parent | — (light: no plan gate) | deepseek-v4.1-flash | **SKIPPED** — reduced close for a cosmetic UI change; parent scope-check instead | n/a (no reviewer gate); 1 supervisor ruling resolved mid-run (RZ2012 / `EditorRequired`) | 1 (post-close) | post-close user-reported defect: `LabelPosition="Below"` dropped the 180px label gutter (pairs out of line) and put the pair label under the first control; fixed with `AlignTop` + a per-control aria-label |

¹ *The round doc's header does not restate the models (the light default set of the period:
worker `deepseek-v4.1-flash`, reviewer `kimi-k2.7-code`); see the round doc.*

**Corpus headline (recorded 2026-10-05, not re-derived from these rows):** the plan gate caught
real P1s on every full round it had then run (**5/5**) — including a P1 in the parent's own
split-review proposal, caught by a **same-family-bigger-tier** gate (`glm-5.3:cloud` over the
`glm-5.3-flash` author) — and `portal-submission-grade` was the first plan to pass with **0
P1**. That same-family catch is the evidence behind Q7's amended "family **or** clearly-bigger
tier" rule.

**Grill instrumentation (adopted 2026-10-07, after these rows).** The plan-stage grill
escalation (SKILL.md step 2b), the acceptance residual grill (step 5), the reviewer-side
`Open decisions:` block line, and the author-side `## Plan` `Open decisions:` duty are
now in the skill — from the next round on, a grill firing is a ledger-worthy event:
record it like a rework iteration, with the questions asked and the answers pinned.
The `targets-dialog-picked-zone` round also produced the one-writer incident behind the
"One writer per working tree" pitfall (SKILL.md Pitfalls + AGENTS.md).
