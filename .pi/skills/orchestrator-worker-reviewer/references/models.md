# Models — orchestrator-worker-reviewer

Exact id tables, per-tier strategy, substitution rules, and the traceability
header format. `SKILL.md` summarizes; this file is the catalog.

**Policy lives elsewhere, on purpose:** the round-runner model policy — the metric
(iterations per round), the citation-discipline gate criterion, seat-diversity requirements
(the gate differs from the author in family or clearly-bigger tier), the author-escalation
triggers, and the re-evaluation cadence — is
`documents/specs/round-runner-model-policy.md` (adopted 2026-10-06). This file holds the
operational ids and dispatch rules that policy governs; if they ever disagree, the spec wins.

## Selecting a provider — a per-round choice, never a fixed setting

The provider is chosen **per round** and recorded on round-doc line 1. It is not
a static setting, and no profile is mandatory. Two profiles are supported:

| Role | pi `ollama` profile (long-standing default) | `clinepass` profile (option) |
|---|---|---|
| Orchestrator | `ollama-cloud/glm-5.3-flash` (owner 2026-09-22, **Option A**) | `cline-pass/glm-5.3` |
| Worker | `ollama-cloud/deepseek-v4.1-flash:1017` (owner 2026-10-18 — **suffix pinned**; the bare `deepseek-v4.1-flash` is ambiguous in the registry) | `cline-pass/deepseek-v4.1-flash` (owner 2026-10-05 **latest**, lean and full rounds alike — supersedes the same day's interim `glm-5.3-flash` ruling; the older `deepseek-v4-flash` it replaced is **deprecated on ClinePass** as of 2026-10-19, so it must not be dispatched) |
| Reviewer (diff review) | **`ollama-cloud/deepseek-v4-pro:0813`** (owner 2026-10-18 — the seat for every tier with a reviewer gate; different family from the orchestrator, so the gate is never the author) | **no dispatchable id** — `cline-pass/mimo-v2.6-flash` is **absent from the provider registry** (verified 2026-10-18: provider `cline` lists only `xiaomi/mimo-v2.6-flash`/`-pro`), so it must not be dispatched, and `cline/deepseek/deepseek-v4-pro-0813` is a different seat that returns `insufficient_credits`. The seat runs on the **pi profile only** |
| Plan reviewer (**every tier**) | **Tier 2: `ollama-cloud/deepseek-v4-pro:0813`** · **Tier 3: `ollama/glm-5.3:cloud`** (owner 2026-10-23 — a Tier 2 round runs exactly TWO models, so the plan gate returns to the diff-review seat; the 2026-10-18 ruling that put `glm-5.3:cloud` on every tier is superseded **for light rounds only** and still governs Tier 3). The gate must never be the plan author: on Tier 2 the author is the parent session, so record the parent's model on round-doc line 1 and escalate the gate one step if the parent already sits on the verifier seat | `cline-pass/deepseek-v4-pro` (Tier 2) · `cline-pass/glm-5.3` (Tier 3) |
| **Acceptance** (Tiers 1–3) | **`ollama-cloud/deepseek-v4-pro:0813`** (owner 2026-10-18 — a dispatched pass on its own seat, independent of worker and reviewer) | unchanged: `cline-pass/glm-5.3` full rounds · parent-transcribed on lean |
| UI Tester | **`ollama-cloud/beam` — dispatched through the `worker` agent** (owner 2026-10-19; validated by dispatch probe the same day, so the id carries **no `:cloud` suffix** — `ollama-cloud/beam:cloud` is *unknown*, `ollama-cloud/beam` answers). There is no `ui-tester` agent type in this install (dispatcher agent list verified 2026-10-19: `claude-code`, `claude-code-writer`, `codex-exec`, `codex-exec-writer`, `cursor-agent`, `cursor-agent-writer`, `delegate`, `evidence-auditor`, `oracle`, `researcher`, `reviewer`, `scout`, `worker`), so a plan naming `ui-tester` fails at dispatch — carry the UI-hunt brief on `worker`. Superseded: `ollama/minimax-m3:cloud` | `cline-pass/minimax-m3` |
| Escalator (blocked-pass rework) | Tier 3: `ollama-cloud/kimi-k2.7-code` (owner 2026-09-22 — pinned; distinct from the diff-review seat `deepseek-v4-pro:0813`, so an escalated rework and its verifier never share a model); otherwise the round's reviewer model | the round's reviewer model |
| Higher-model re-verify | `ollama/glm-5.3:cloud` | `cline-pass/glm-5.3` |

**Opting into clinepass from pi:** pass `clinepass/cline-pass/<id>` in every
role's `runs.run` `model` field for that round (e.g.
`clinepass/cline-pass/deepseek-v4.1-flash`), and record it in the round-doc
header. **Pick one provider per round; never mix providers mid-round.**

### Per-mode model sets (owner overrides 2026-09-16)

| Mode | Worker (implementer) | Orchestrator | Reviewer | Notes |
|---|---|---|---|---|
| **Solo** | the single agent plans + implements + checks its own work — default `ollama-cloud/deepseek-v4.1-flash:1017` | — | — | ask the user first (solo rule below) |
| **Light (Tiers 1–2)** | `ollama-cloud/deepseek-v4.1-flash:1017` | `glm-5.3-flash` (if dispatched) | **`deepseek-v4-pro:0813`** | **TWO models only** (owner 2026-10-23): worker `deepseek-v4.1-flash` + verifier `deepseek-v4-pro:0813`, which covers **plan gate, diff review AND acceptance**. The reviewer/orchestrator **must differ** from the worker's model (owner 2026-10-18: diff-review seat = `deepseek-v4-pro:0813`); the plan gate sits on that same verifier seat as of 2026-10-23, superseding `glm-5.3:cloud` for light rounds only. Record the parent's model on round-doc line 1 — the gate must never be the plan author |
| **Tier 3** | `ollama-cloud/deepseek-v4.1-flash:1017` | `glm-5.3-flash` | **`deepseek-v4-pro:0813`** | + UI tester `minimax-m3`; **plan-review on every tier = `glm-5.3:cloud`** (owner 2026-10-18); **escalator = `kimi-k2.7-code`** (pinned); acceptance seat `deepseek-v4-pro:0813` |

Clinepass equivalents: light worker `cline-pass/deepseek-v4.1-flash`, light
orchestrator `cline-pass/glm-5.3-flash`; the **diff reviewer has no dispatchable
clinepass id** — see the reviewer row (ollama profile:
`ollama-cloud/deepseek-v4.1-flash:1017` and `ollama-cloud/deepseek-v4-pro:0813`).
Tier 3 **lean** uses the standard Tier-3 ladder unchanged — only the
accept-run is skipped (SKILL.md § "Tier 3 lean") — **with the owner rulings of
2026-10-05 (latest wins): the clinepass lean worker is `cline-pass/deepseek-v4.1-flash`**
(superseding the same day's interim `glm-5.3-flash` ruling, which itself superseded
`deepseek-v4-flash`), **and the reviewer — plan-review and diff-review alike — is
`ollama-cloud/deepseek-v4-pro:0813`** (owner 2026-10-18), with the plan gate on
`ollama/glm-5.3:cloud` on **every** tier. The seat lives on the **ollama-cloud**
provider, whose catalog carries the bare `deepseek-v4-pro:0813`; the `:cloud` in an
earlier ruling is that provider's tag form. The previously named
`cline-pass/mimo-v2.6-flash` is **absent from the provider registry** (verified
2026-10-18), so it must not be dispatched; the verifier stays distinct from the
implementer.

**Solo rule:** a solo round is ONE agent doing everything — planner, implementer,
and its own acceptance check — the same single-agent shape as a light round's
worker. Before running solo, **always ask the user whether to use the current
session model**; use it only on their yes, otherwise fall back to the skill's
solo default **`ollama-cloud/deepseek-v4.1-flash:1017`** (owner 2026-10-18 — the
suffix-pinned worker id; the `glm-5.3-flash` fallback is retired). Record which
one was used on round-doc line 1.
Solo never dispatches an orchestrator or reviewer of its own.

Resolve ids at dispatch time with `subagent({ action: "models" })` and copy exact
`provider/id` strings; bare ids resolve only when unique. The `ollama*` ladder has
drifted historically (the `ollama-cloud` provider hosts the modern ids) — verify
the ids rather than assuming them. **Removed model (owner 2026-09-25):** `deepseek-v4-flash:0731` has been **removed by
the ollama provider** and must no longer be dispatched on any seat — it was already retired
from the worker seat by Option A (2026-09-22), and the git/gh-execution delegate model named
in `AGENTS.md` moved to `ollama-cloud/deepseek-v4.1-flash:1017` with it. Historical round docs may
still name it — they record what ran; never rewrite them. **Known trap (2026-09-22, retained
for its lesson):** its pi-profile id spelled a **COLON** before `0731`
(`ollama-cloud/deepseek-v4-flash:0731`); the hyphenated `deepseek-v4-flash-0731` was the
*clinepass* spelling and did NOT exist on `ollama-cloud` — dispatching a misspelled id fails
at launch with `Unknown subagent model '...' in the active Pi model registry` and writes
nothing. The lesson outlives the model: resolve every id against the registry
(`subagent({ action: "models" })`, or `~/.pi/agent/models-store.json` → the provider block)
before every dispatch — a stale local store can still list a model the provider has removed.

### Defaults and overrides — precedence, highest first

1. **A per-role model the user names** for this round — that role only; other
   roles keep the profile defaults.
2. **A provider the user names** for this round — that profile's defaults for
   every role not individually named.
3. **The profile already recorded in the round doc** (resumed/continuing rounds).
4. **The skill default: the pi `ollama` profile**, per tier — subject to the
   **per-mode model sets** above (solo / light / Tier 3), owner-overridden
   2026-09-16; the pi-profile reviewer default was replaced 2026-09-22 and
   **Option A was applied 2026-09-22**). Light and Tier 3 now run the SAME set:
   worker `ollama-cloud/deepseek-v4.1-flash:1017`, orchestrator `glm-5.3-flash`, diff reviewer
   `deepseek-v4-pro:0813` (owner 2026-10-18), acceptance `deepseek-v4-pro:0813` — the verifier never
   shares the implementer's model, and acceptance is independent of both. Tier 3 differs only by the UI tester and the
   plan-review, which runs `glm-5.3:cloud` on **every** tier (owner 2026-10-18); lean rounds skip the accept-run.
   **Plan-review (step 2b):** `glm-5.3:cloud` on every tier, lean and full alike —
   **the plan gate must never share the worker's model, and never drops to the fast generalist**
   (owner 2026-10-18, superseding the 2026-10-17 lean-on-reviewer-seat ruling). Subject to a user-named override (item 1). **Acceptance (step 5)** is its own seat: `ollama-cloud/deepseek-v4-pro:0813` on the pi profile (owner 2026-10-18), so acceptance is independent of worker and reviewer.
5. **Cline**: always `clinepass` (cannot resolve `ollama` ids) — an exception,
   not an override.

An override governs the **whole round**, not one dispatch: apply it to every
dispatch not yet started, never mix two providers in a round **except** where the
user explicitly names a per-role model on a different provider (precedence item
1) — that named role is the one sanctioned cross-provider override; every
unnamed role stays on the round's profile default, and both go on round-doc line
1. Record the effective choice on round-doc line 1 (plus the execution log, with
the reason, if it arrived mid-round). Already-dispatched roles keep the model
they ran on.
A user-named escalation or higher-model re-verify model wins over the profile
default for that step; an unavailable id is substituted within the same tier and
the substitute is recorded. **Never omit the `model` field** — an omitted field
is NOT the skill default: the child then inherits the current session model (one
id, on whatever provider the session is using), collapsing every role onto it.
Treat an omitted `model` as a dispatch error to fix, not a fallback.

## clinepass profile — verified ids (2026-10-19, ClinePass catalog)

`glm-5.3`, `glm-5.3-flash`, `deepseek-v4-pro`, `deepseek-v4.1-flash`,
`kimi-k3`, `minimax-m3`,
`mimo-v2.5`/`-pro`, `qwen3.8-max`, `qwen3.7-max`, `qwen3.7-plus`.

**Deprecated on ClinePass (2026-10-19, docs.cline.bot) — do not dispatch:**
`glm-5.2`, `deepseek-v4-flash` (bare id and `-0731`), `kimi-k2.6`,
`kimi-k2.7-code`.

Role mapping under this profile: orchestrator `glm-5.3`, worker
`deepseek-v4.1-flash`, reviewer `deepseek-v4.1-flash` (owner override
2026-09-15; otherwise `kimi-k3`), UI tester `minimax-m3`, higher-model
re-verify `glm-5.3`.

## Cline profile

Cline cannot resolve pi's `ollama/<id>:cloud` ids — its session provider is
`clinepass`, so use the clinepass column above.

## Escalation ladder (build-escalation pattern, SKILL.md step 3)

When a worker pass times out / stalls / hangs: the escalation EXECUTOR is the
ESCALATOR model (Tier 3 full: `kimi-k2.7-code`, pinned by owner
2026-09-22 — not derived from the reviewer; since 2026-10-18 the diff-review seat is
`deepseek-v4-pro:0813` and the plan gate is `glm-5.3:cloud`, so escalator and verifier are distinct), dispatched through a write-capable shell
(`worker`/`delegate` — never the read-only `reviewer` shell), and the escalated
work is then statically re-verified by the HIGHER model **of the same profile**
(`ollama/glm-5.3:cloud`, or `clinepass/cline-pass/glm-5.3`).
One escalation per blocked pass; record provenance in the round doc.

## Substitution rule

Substitute only when a listed id is unavailable, and **within the same tier**
(fast generalist / implementer / deep verifier). Clinepass substitutes:

- `cline-pass/kimi-k3` — stronger reviewer
- `cline-pass/deepseek-v4-pro` — stronger worker
- `cline-pass/glm-5.3-flash`
- `cline-pass/qwen3.8-max`, `cline-pass/qwen3.7-max`, `cline-pass/qwen3.7-plus`
- `cline-pass/mimo-v2.5-pro`, `cline-pass/mimo-v2.5`

Other providers (and other profiles): pass the exact `provider/id` in each
`runs.run`'s `model` field (e.g. `model: "github-copilot/claude-sonnet-5"`);
agent definitions via `agent: "<name>"` (builtin
`delegate`/`oracle`/`reviewer`/`worker`/`scout`/`researcher`, or a custom agent).

## Per-tier model strategy

| Tier | Models used |
|---|---|
| 1 | **Two models**: the worker, plus the verifier seat `ollama-cloud/deepseek-v4-pro:0813` — on light rounds that one seat carries the plan gate AND acceptance (owner 2026-10-23). No orchestrator/reviewer/tester models of their own |
| 2 | Worker + reviewer code specialist — the same two-model set: `ollama-cloud/deepseek-v4.1-flash:1017` (worker) and `ollama-cloud/deepseek-v4-pro:0813` (plan gate, diff review, acceptance) |
| 3 | Full defaults above (stronger substitutes allowed per the substitution rule); lean rounds use the same ladder — only the accept-run is skipped. The reviewer runs **twice**: plan-review (step 2b, before the worker) then diff-review. Plan-review model: **`glm-5.3:cloud` on every tier**, lean and full alike — never the worker's model (owner 2026-10-18). Pi-profile worker pinned to `ollama-cloud/deepseek-v4.1-flash:1017`; acceptance seat `ollama-cloud/deepseek-v4-pro:0813` |

Rationale: tiers exist to keep simple tasks cheap — never pay for model
round-trips (or roles) a task does not need.

## Traceability header

Round doc line 1 records which provider ran the round, e.g.:

- `Provider: pi (models: glm-5.3-flash reviewer/orchestrator, ollama-cloud/deepseek-v4.1-flash:1017 worker, ollama-cloud/deepseek-v4-pro:0813 acceptance, minimax-m3 tester)`
- `Provider: pi, full Tier 3, Option A (models: glm-5.3-flash orchestrator, glm-5.3 plan-review, ollama-cloud/deepseek-v4.1-flash:1017 worker, kimi-k2.7-code reviewer, kimi-k2.7-code escalator, minimax-m3 tester)`
- `Provider: pi/clinepass (models: glm-5.3, deepseek-v4.1-flash, deepseek-v4-pro, minimax-m3)`
- `Provider: Cline/clinepass (models: glm-5.3, deepseek-v4.1-flash, kimi-k3, minimax-m3)`

so build/test numbers and findings can be traced to the driving models.
