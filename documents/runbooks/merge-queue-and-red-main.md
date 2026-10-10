# Runbook — the merge queue, and what to do when `main` is red

Owner-facing procedure. Written 2026-10-10 after a post-merge CI question exposed the gap it closes.

## The gap this closes

`main` receives **squash** merges. A squash is a *new* commit: the PR's checks ran against the branch
tip, and the commit that becomes the trunk is built for the first time **after** it lands. Add
concurrent sessions — two merges landed five minutes apart on 2026-10-09/10 — and the tree `main`
receives is rarely the tree any PR was validated against.

Today the only post-merge signal is the `push`-triggered CI run on `main`, and nothing enforces
anything server-side: as of 2026-10-10 `main` has **no ruleset and no branch protection**, so every
merge rule in this repo is a convention the sessions honour by hand.

A **merge queue** is the one mechanism that tests the merged result *before* the trunk moves.

## Availability — there is no queue here, and cannot be

**Verified 2026-10-10:** this repository is **public, owned by a user account** (`owner_type=User`).
GitHub offers merge queues in **public repositories owned by an organisation**, or in private
organisation repositories on Enterprise Cloud — so a queue is **not available to this repository in its
current shape**, whatever the settings say. Nothing below can be applied until the ownership or plan
changes; a queue also needs the `merge_group` trigger already added to `.github/workflows/ci.yml`
(inert until such a queue could exist).

What that means for the gap: the queue's one guarantee — *the merged result is tested before the trunk
moves* — cannot be bought here. It has to be approximated:

1. **A ruleset requiring the status check** (rulesets are available for public repositories on the
   free plan, so this one applies here — the owner applies it): blocks a red PR from merging
   server-side, which the repo currently does not do. It does **not** test the squashed combination;
   pairing it with "require branches to be up to date before merging" is the closest available
   substitute, because it forces the PR's checks to run against the `main` it will land on.
2. **The reading discipline in `.github/merge-policy.md`**: read `main`'s run once after the merge, and
   treat a red trunk as an incident with a fix-forward-through-a-PR response.
3. **Serialise merges between sessions** where practical: check `main`'s state immediately before asking
   for a merge, so the tree you validated against is the tree you land on.

If the repository ever moves to an organisation (or to Enterprise Cloud as a private org repo), revisit
the queue — the enablement steps are retained below for that day.

## Prerequisites (for a future organisation-owned shape)

- **Admin permission** on the repository — the queue and its ruleset are repository settings. Agents
  cannot and must not apply them.
- **Organisation ownership** plus a qualifying plan (see the availability note above).
- **The workflow must listen for `merge_group`** — done in `.github/workflows/ci.yml`. A queue dispatches
  that event for its temporary `main/pr-N` branches and waits for the required check to report *there*;
  without the trigger the queue would wait forever. The trigger is inert until a queue exists.

## Enablement (UI first — safest)

1. **Settings → Rules → Rulesets → New branch ruleset.**
2. Target the default branch (`main`).
3. Enable **Require status checks to pass**, naming the check(s) that must gate a merge — at minimum
   `Build & Test`; the other four (`Cross-module wiring guard`, `Portals (Python)`,
   `Auth portal (Python)`, `Mobile (MAUI)`) may be added deliberately (every one you add is a check the
   queue must wait on).
4. Enable **Require merge queue** and pick the merge method (**Squash**, matching the repo policy).
5. Apply, then verify (below).

The same settings can be applied through the repository-rulesets REST API with an admin token. Verify
the payload against the API reference for your API version before running it — do not paste a ruleset
body from memory, and do not script it against a repository you cannot roll back.

## How it behaves

- A queued PR is validated on a temporary branch containing `main` **plus** the PRs ahead of it, so the
  combination — not the isolated PR — is what gets tested.
- A failing or timed-out required check **removes that PR from the queue**, with the reason shown on the
  PR timeline; the queue rebuilds the remaining entries without it.
- Merges are therefore *queued*, not instant. That is the point, not a regression.
- Jumping the queue rebuilds every in-progress entry; use it sparingly.

## Verification after enablement

1. Queue a trivial PR and confirm a run with the `merge_group` event appears for a `main/pr-N` branch.
2. Confirm the trunk commit afterwards carries a green run for the required check(s).
3. Confirm a deliberately failing PR is dropped from the queue rather than merged.

## When `main` is red (the incident procedure)

The merge already happened; a red run cannot un-merge it. Read `main`'s run **once** at a decision
point — before starting new work off `main`, or on a failure notification — never as a polling gate.

| Situation | Response |
|---|---|
| Failure in the code just merged | **Fix forward on a new branch, through a PR.** Never push to `main` directly, not even to repair it. Tell the owner immediately. |
| The fix is not quick | Recommend a revert to the owner and wait. **A revert is a manual, owner-agreed action** — never unilateral. |
| Failure in another session's job (e.g. `Mobile (MAUI)`) | Report it; the owning train fixes it. Don't touch another worktree's area. |
| Infrastructure/flake (Docker, browser download, network) | Re-run the job once, say so, and only escalate if it repeats. |

## Related

- `.github/merge-policy.md` — the merge path and the health-signal rule.
- `.github/copilot/preflight-pr.prompt.md` §8 — the agent-side wording for the same procedure.
