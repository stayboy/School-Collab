# Models — orchestrator-worker-reviewer

Exact id tables for both provider profiles, per-tier strategy, substitution
rules, and the traceability header format. `SKILL.md` summarizes; this file is
the catalog.

## pi profile defaults

Model ids come from `subagent({ action: "models" })` — copy exact `provider/id`
strings; bare ids resolve only when unique.

| Role | pi default |
|---|---|
| Orchestrator | `ollama/glm-5.3-flash:cloud` |
| Worker | `ollama/minimax-m3:cloud` |
| Reviewer | `ollama/kimi-k2.7-code:cloud` |
| UI Tester | `ollama/deepseek-v4-flash:0731-cloud` |

## Cline profile — switch to `clinepass` first

Cline cannot resolve pi's `ollama/<id>:cloud` ids. Before starting a round,
switch the Cline session's provider to `clinepass`, then use:

| Role | pi default | clinepass equivalent |
|---|---|---|
| Orchestrator | `ollama/glm-5.3-flash:cloud` | `cline-pass/glm-5.3` |
| Worker | `ollama/minimax-m3:cloud` | `cline-pass/minimax-m3` |
| Reviewer | `ollama/kimi-k2.7-code:cloud` | `cline-pass/kimi-k2.7-code` |
| UI Tester | `ollama/deepseek-v4-flash:0731-cloud` | `cline-pass/deepseek-v4-flash` |

Substitutes stored under `clinepass` — use only when a listed equivalent is
unavailable, and substitute **within the same tier** (fast generalist /
implementer / deep verifier):

- `cline-pass/kimi-k3` — stronger reviewer
- `cline-pass/deepseek-v4-pro` — stronger UI tester
- `cline-pass/glm-5.2`
- `cline-pass/kimi-k2.6`
- `cline-pass/qwen3.8-max`, `cline-pass/qwen3.7-max`, `cline-pass/qwen3.7-plus`
- `cline-pass/mimo-v2.5-pro`, `cline-pass/mimo-v2.5`

Other providers: pass the exact `provider/id` in each `runs.run`'s `model`
field (e.g. `model: "github-copilot/claude-sonnet-5"`); agent definitions via
`agent: "<name>"` (builtin `delegate`/`oracle`/`reviewer`/`worker`/`scout`/
`researcher`, or a custom agent).

## Per-tier model strategy

| Tier | Models used |
|---|---|
| 1 | Worker model only — no orchestrator/reviewer/tester models at all |
| 2 | Worker + reviewer code specialist |
| 3 | Full defaults above (stronger substitutes allowed per the substitution rule) |

Rationale: tiers exist to keep simple tasks cheap — never pay for model
round-trips (or roles) a task does not need.

## Traceability header

Round doc line 1 records which provider ran the round, e.g.:

- `Provider: pi (models: glm-5.3-flash, minimax-m3, kimi-k2.7-code, deepseek-v4-flash)`
- `Provider: Cline/clinepass (models: glm-5.3, minimax-m3, kimi-k2.7-code, deepseek-v4-flash)`

so build/test numbers and findings can be traced to the driving models.