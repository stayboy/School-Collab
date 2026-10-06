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

¹ *The round doc's header does not restate the models (the light default set of the period:
worker `deepseek-v4.1-flash`, reviewer `kimi-k2.7-code`); see the round doc.*

**Corpus headline (recorded 2026-10-05, not re-derived from these rows):** the plan gate caught
real P1s on every full round it had then run (**5/5**) — including a P1 in the parent's own
split-review proposal, caught by a **same-family-bigger-tier** gate (`glm-5.3:cloud` over the
`glm-5.3-flash` author) — and `portal-submission-grade` was the first plan to pass with **0
P1**. That same-family catch is the evidence behind Q7's amended "family **or** clearly-bigger
tier" rule.