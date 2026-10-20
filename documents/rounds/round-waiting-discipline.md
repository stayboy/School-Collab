# Round — waiting discipline (harness layer 2)

**Status:** CLOSED — pending PR
**Mode:** attended · policy layer, no runtime surface
**Base:** `chore/agent-harness-optimisation` @ `262ed139` (layer 1 = PR #385)
**Branch:** `chore/waiting-discipline`
**Decisions:** grill Q108–Q113 (2026-10-20); Q111 as amended for stacked PRs

## Plan

Layer 1 shipped the *input* side of context discipline (briefs carry paths; no re-reads).
This layer is the **waiting** side, and the measurement says the ordering was wrong in a
useful way: the largest wait burn was never the parent's poll loop.

| Where the wait happens | Mechanism | Observed cost |
|---|---|---|
| **inside a seat** (worker sleeping / retrying) | the child re-sends its whole context every turn | **919k tokens** — the B2c worker that died in its own gate at the 30-minute cap |
| parent poll loop | `sleep 60` + a status line per iteration | ~25 tok/iteration, **plus** a 5-minute abort that returned nothing |
| unbounded command output | full build/test logs | context-mode already keeps 215 KB out of context |

The 30-minute subagent cap bounds **wall-clock, not tokens** — it permitted a 919k burn
without ever being violated. That is the finding this layer exists to fix.

## Implementation

| Path | Change |
|---|---|
| `scripts/round/wait-ci.sh` | new — one call, one line, hard `timeout`; `--pr` / `--run`; exit 0 green · 1 failed · 3 timed out |
| `scripts/round/README.md` | the third script, and why a `sleep` loop is the wrong primitive |
| `references/role-contracts.md` | § **Output discipline** (the brief rule, on the way back) and § **Waiting discipline** (no sleeps; ~90 s per-command budget with the `gates.sh` carve-out; block only when the turn needs it; auto-merge off, with the stack hazard and the `submit --auto` naming trap) |
| `SKILL.md` step 0 | the same three rules, in the dispatch checklist |
| `documents/rounds/README.md` | Q113's three numbers at round close |

**Not built, deliberately:** a mechanical sleep-detector. Prompts are not testable, and a
token watchdog belongs in the harness rather than this repo — Q108(c) was rejected on that
basis rather than on effort.

## Verification

| Check | Result |
|---|---|
| `bash -n scripts/round/wait-ci.sh` | clean |
| `wait-ci.sh --help` | usage only, exit 0 |
| `wait-ci.sh` against a **merged** PR / bogus id | exit 1 with the failing name, not a hang |
| `gates.sh` (layer 1) unaffected | scripts/docs only; no test project touched |
| Architecture suite | unchanged at 142 (no test edits this layer) |

**Measured on the round that produced this layer:** the 7-iteration poll loop it replaces
cost ~200 tokens and one aborted call; the two `--watch` calls that replaced it cost ~40
tokens and both returned a verdict. Same information, one third the context, and it
survives an interruption.

## Acceptance

Accepted by inspection against Q108–Q113. Every item is a file, a printed line, or a rule
with a number in it; no branch of the tree is left assumed. The auto-merge paragraph carries
the owner's amended answer for **stacked** PRs: atomic and per-PR auto-merge do not coexist,
an upper layer's base is the layer below (so auto-merging it folds layers together), and the
hands-off path is `gh stack merge --yes --squash` on the owner's word.

## Follow-ups

- **Q100** — the parallel-worker fan-out edits are *still* uncommitted in `.pi/skills/`;
  they now depend on two sections that shipped after they were written (brief discipline,
  waiting discipline) and need their own decision.
- The Q113 numbers need a full round to prove themselves; the residuals round will be the
  first one that can carry them.
