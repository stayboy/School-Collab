# orchestrate-fix extension

A `/orchestrate-fix` slash command that wraps the `orchestrator-worker-reviewer`
skill into a one-line kickoff.

## What it does

`/orchestrate-fix <task>` injects a priming user message that tells the parent
agent to follow the local skill at
`.pi/skills/orchestrator-worker-reviewer/SKILL.md` and run a full fix round:

1. **Orchestrator** writes a plan + authors worker/reviewer tasks (owns the docs).
2. **Worker** implements the plan, runs build + tests.
3. **Reviewer** verifies against the plan + specs, writes a review doc.
4. **Orchestrator** acceptance pass (CLOSED / REMAINING P1).

The actual multi-agent execution is driven by the installed **`pi-subagents`**
extension's machinery (`workflowScript`, `runs.run`, `runs.all`, intercom).
This extension only seeds the turn — it does not spawn processes itself.

## Usage

```text
/orchestrate-fix make the SelectedGroups subject picker effective-date aware
/orchestrate-fix --orchestrator github-copilot/claude-sonnet-5 --worker ollama/deepseek-v4-pro:cloud fix the rollover double-save race
```

### Flags

| Flag | Default | Purpose |
|------|---------|---------|
| `--orchestrator <provider/id>` | `ollama/glm-5.2:cloud` | Orchestrator model override |
| `--worker <provider/id>` | `ollama/deepseek-v4-flash:0731-cloud` | Worker model override |
| `--reviewer <provider/id>` | `ollama/kimi-k2.7-code:cloud` | Reviewer model override |

Copy an exact `provider/id` from `subagent({ action: "models" })`. Bare ids
resolve only when unique in the registry.

## Placement

Project-local, auto-discovered: `.pi/extensions/orchestrate-fix/index.ts`.

- Reload after edits with `/reload`.
- Requires the project to be trusted (project-local extensions load only after
  the project is trusted).

## Dependencies

- Installed **`pi-subagents`** extension (provides `workflowScript` /
  `runs.run` / intercom).
- The local skill **`.pi/skills/orchestrator-worker-reviewer/SKILL.md`**
  (provides the procedure the parent follows).

## Notes

- The parent agent is the final source of truth for build/test numbers — the
  kickoff prompt instructs it to rerun `dotnet build` / `dotnet test` itself.
- If the reviewer agent lacks shell/file-write tools, the kickoff prompt tells
  it to return its report inline so the parent persists it.