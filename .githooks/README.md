# Git hooks

This directory contains repository-tracked Git hook templates used by the SchoolCollab workflow.

## Enable locally

```bash
git config core.hooksPath .githooks
```

## pre-push

`.githooks/pre-push` blocks direct pushes to `main`. This is a local convenience guard only; GitHub branch protection or rulesets should enforce the same rule server-side where available.
