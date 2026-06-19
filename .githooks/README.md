# Git hooks

This directory contains repository-tracked Git hook templates used by the SchoolCollab workflow.

## Enable locally

```bash
git config core.hooksPath .githooks
```

## pre-push

`.githooks/pre-push` holds local pushes until the user explicitly allows them. This is a local convenience guard only; GitHub branch protection or rulesets should enforce the same rule server-side where available.

To allow a push after an explicit user instruction, run:

```bash
SCHOOLCOLLAB_ALLOW_PUSH=1 git push <remote> <branch>
```

PowerShell example:

```powershell
$env:SCHOOLCOLLAB_ALLOW_PUSH='1'
git push <remote> <branch>
Remove-Item Env:SCHOOLCOLLAB_ALLOW_PUSH
```

The hook still blocks direct pushes to `main`.
