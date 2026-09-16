# Models — orchestrator-worker-reviewer

Exact id tables, per-tier strategy, substitution rules, and the traceability
header format. `SKILL.md` summarizes; this file is the catalog.

## Selecting a provider — a per-round choice, never a fixed setting

The provider is chosen **per round** and recorded on round-doc line 1. It is not
a static setting, and no profile is mandatory. Two profiles are supported:

| Role | pi `ollama` profile (long-standing default) | `clinepass` profile (option) |
|---|---|---|
| Orchestrator | `ollama-cloud/glm-5.3-flash` | `cline-pass/glm-5.3` |
| Worker | `ollama/deepseek-v4-flash:0731-cloud` | `cline-pass/deepseek-v4-flash` |
| Reviewer | `ollama-cloud/deepseek-v4.1-flash` (owner override 2026-09-15) | `cline-pass/deepseek-v4.1-flash` |
| UI Tester | `ollama/minimax-m3:cloud` | `cline-pass/minimax-m3` |
| Escalator (blocked-pass rework) | the round's reviewer model | the round's reviewer model |
| Higher-model re-verify | `ollama/glm-5.3:cloud` | `cline-pass/glm-5.3` |

**Opting into clinepass from pi:** pass `clinepass/cline-pass/<id>` in every
role's `runs.run` `model` field for that round (e.g.
`clinepass/cline-pass/deepseek-v4-flash-0731`), and record it in the round-doc
header. **Pick one provider per round; never mix providers mid-round.**

Resolve ids at dispatch time with `subagent({ action: "models" })` and copy exact
`provider/id` strings; bare ids resolve only when unique. The `ollama*` ladder has
drifted historically (the `ollama-cloud` provider hosts the modern ids) — verify
the ids rather than assuming them.

### Defaults and overrides — precedence, highest first

1. **A per-role model the user names** for this round — that role only; other
   roles keep the profile defaults.
2. **A provider the user names** for this round — that profile's defaults for
   every role not individually named.
3. **The profile already recorded in the round doc** (resumed/continuing rounds).
4. **The skill default: the pi `ollama` profile**, per tier.
5. **Cline**: always `clinepass` (cannot resolve `ollama` ids) — an exception,
   not an override.

An override governs the **whole round**, not one dispatch: apply it to every
dispatch not yet started, never mix two providers in a round, and record the
effective choice on round-doc line 1 (plus the execution log, with the reason,
if it arrived mid-round). Already-dispatched roles keep the model they ran on.
A user-named escalation or higher-model re-verify model wins over the profile
default for that step; an unavailable id is substituted within the same tier and
the substitute is recorded. **Never omit the `model` field** — an omitted field
is NOT the skill default: the child then inherits the current session model (one
id, on whatever provider the session is using), collapsing every role onto it.
Treat an omitted `model` as a dispatch error to fix, not a fallback.

## clinepass profile — verified ids (2026-09-16, `pi-clinepass` catalog)

`glm-5.3`, `glm-5.3-flash`, `glm-5.2`, `deepseek-v4-flash-0731`,
`deepseek-v4-flash`, `deepseek-v4-pro`, `deepseek-v4.1-flash`,
`kimi-k2.7-code`, `kimi-k2.6`, `kimi-k3`, `minimax-m3`, `minimax-m2.6-pro`,
`mimo-v2.5`/`-pro`, `qwen3.8-max`, `qwen3.7-max`, `qwen3.7-plus`.

Role mapping under this profile: orchestrator `glm-5.3`, worker
`deepseek-v4-flash-0731`, reviewer `deepseek-v4.1-flash` (owner override
2026-09-15; otherwise `kimi-k2.7-code`), UI tester `minimax-m3`, higher-model
re-verify `glm-5.3`.

## Cline profile

Cline cannot resolve pi's `ollama/<id>:cloud` ids — its session provider is
`clinepass`, so use the clinepass column above.

## Escalation ladder (build-escalation pattern, SKILL.md step 3)

When a worker pass times out / stalls / hangs: the escalation EXECUTOR is the
round's reviewer model, dispatched through a write-capable shell
(`worker`/`delegate` — never the read-only `reviewer` shell), and the escalated
work is then statically re-verified by the HIGHER model **of the same profile**
(`ollama/glm-5.3:cloud`, or `clinepass/cline-pass/glm-5.3`).
One escalation per blocked pass; record provenance in the round doc.

## Substitution rule

Substitute only when a listed id is unavailable, and **within the same tier**
(fast generalist / implementer / deep verifier). Clinepass substitutes:

- `cline-pass/kimi-k3` — stronger reviewer
- `cline-pass/deepseek-v4-pro` — stronger worker
- `cline-pass/glm-5.2`, `cline-pass/kimi-k2.6`
- `cline-pass/qwen3.8-max`, `cline-pass/qwen3.7-max`, `cline-pass/qwen3.7-plus`
- `cline-pass/mimo-v2.5-pro`, `cline-pass/mimo-v2.5`

Other providers (and other profiles): pass the exact `provider/id` in each
`runs.run`'s `model` field (e.g. `model: "github-copilot/claude-sonnet-5"`);
agent definitions via `agent: "<name>"` (builtin
`delegate`/`oracle`/`reviewer`/`worker`/`scout`/`researcher`, or a custom agent).

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

- `Provider: pi (models: glm-5.3-flash, deepseek-v4-flash-0731, deepseek-v4.1-flash, minimax-m3)`
- `Provider: pi/clinepass (models: glm-5.3, deepseek-v4-flash-0731, deepseek-v4.1-flash, minimax-m3)`
- `Provider: Cline/clinepass (models: glm-5.3, deepseek-v4-flash, kimi-k2.7-code, minimax-m3)`

so build/test numbers and findings can be traced to the driving models.
