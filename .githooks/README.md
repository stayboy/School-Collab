# Git Hooks

Shared hooks live in `.githooks/`. After cloning, activate them once:

```bash
git config core.hooksPath .githooks
```

## Hooks

| Hook | Enforces |
|---|---|
| `pre-push` | Blocks direct pushes to `main` — use a feature branch + PR instead |

## Feature branch workflow

```bash
# Start a feature
git checkout -b feature/<name>

# Work, commit, then push
git push -u origin feature/<name>

# Open a PR — the CI workflow runs automatically
gh pr create --base main --title "feat: ..." --body "..."

# Auto-merge when CI passes (after branch protection is enabled)
gh pr merge --auto --squash
```

## Enabling branch protection on GitHub

Branch protection requires **GitHub Pro** (private repo) or making the repo public.

Once eligible, run:

```bash
gh api repos/stayboy/School-Collab/branches/main/protection \
  --method PUT \
  --field 'required_status_checks={"strict":true,"contexts":["Build & Test"]}' \
  --field 'enforce_admins=true' \
  --field 'required_pull_request_reviews={"required_approving_review_count":0,"dismiss_stale_reviews":true}' \
  --field 'restrictions=null' \
  --field 'required_linear_history=true' \
  --field 'allow_force_pushes=false' \
  --field 'allow_deletions=false'
```

This enforces:
- All changes via PR (no direct pushes, even from admins)
- CI `Build & Test` job must pass before merge
- Linear history (squash/rebase only — makes `git revert` of a feature trivial)
- No force-pushes or branch deletion
