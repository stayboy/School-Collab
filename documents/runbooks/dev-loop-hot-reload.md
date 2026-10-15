# Dev loop: hot reload & watch (UI iteration without restarts)

Adopted 2026-10-12 via an owner grill round (all decisions: runbook + AGENTS.md pointer,
docs-only enablement — no config or profile flips). Every claim marked *observed* was
exercised live on this repo (SDK 10.0.401, Aspire 13.5.4, Windows) on 2026-10-12.

## Why

The legacy visual-verification loop is kill AppHost → full `dotnet build` → restart →
poll `:7300` → reopen the browser → screenshot: 1.5–2 min per cycle, state-destroying,
and it invents its own file-lock failures (MSB3021/3027). Hot reload attacks the *apply*
step only — **observation stays a gate** (Playwright screenshots/asserts, bUnit, CI);
hot reload just makes each observation reachable in seconds.

## Human loop (standard): IDE hot reload

Run the AppHost under the **Visual Studio or Rider debugger**. Aspire delegates hot
reload to the IDE for its project resources; `.razor` markup, `.css`, and method-body
edits apply in place with app state preserved. No configuration needed — the AppHost
has a `launchSettings.json`.

- *Observed caveat:* with Visual Studio open, its background processes hold build
  outputs — CLI builds/watches against the same tree can fail CS2012/MSB3027.
  Either use the IDE's own build, or close/stop what holds the outputs first.

## Agent loop (standard): detached `dotnet watch` on the AppHost

```powershell
$p = Start-Process dotnet -ArgumentList 'watch','--project','src/AppHost/SchoolCollab.AppHost' `
     -RedirectStandardOutput "$pwd\watch.log" -RedirectStandardError "$pwd\watch.err.log" `
     -NoNewWindow -PassThru
$p.Id | Out-File -Encoding ascii "$pwd\watch.pid"
# poll until https://localhost:7300 answers 200, same as with dotnet run
```

Watch messages go to **stderr** (`watch.err.log`); watch stdout interleaves with the
AppHost's dashboard output (`watch.log`).

*Observed behaviour on 2026-10-12:*

- `dotnet watch` loads the **whole AppHost project graph** (26 projects here) and enables
  hot reload; the stack then starts exactly as with `dotnet run`.
- Editing a `.razor` file: `🔥 C# and Razor changes applied in ~3s`. Editing a
  component's `.css`: `🔥 Static asset changes applied in ~0.5–3s`. Batched edits can
  coalesce into one longer apply cycle (observed once at ~51s) — watch logs the real
  figure; trust the log, then re-assert.
- The already-open Playwright page reflects both kinds of delta **without navigation**
  and without a server restart (caption text and computed font-size verified live, then
  verified again after reverting — same page throughout).
- AppHost edits (`Program.cs`, `appsettings.json`) restart the whole AppHost-managed
  application automatically (documented on aspire.dev; not exercised here).

### Failure modes & recovery (observed)

- Watch's **initial build obeys the same lock rules** as any build: with VS open (or a
  stray service holding `obj`/`bin` DLLs) it fails CS2012/MSB3027 and parks at
  `Fix the error to continue or press Ctrl+C to exit.` — a blocking prompt a detached
  run cannot answer. **Recovery:** kill the watcher tree (`taskkill /PID <watch.pid> /T /F`),
  clear the lock holder, start watch again. Worst case is *today's legacy loop* — the
  watcher never makes things slower than before.
- A rude edit (needs restart) prompts on the console; same recovery applies.
- **"Rude edits" and NEW static assets are not hot-reloadable** (observed
  2026-10-12, recorder pill round): a new component `[Parameter]`, a changed
  record's shape, a new `@implements`/`@inject`, or a freshly added collocated
  `*.razor.js` (a new `_content/...` static web asset) require a full restart —
  delta apply silently keeps the old circuit running. Symptom set: bUnit pins
  the behaviour green yet the preview misbehaves (that round: no play pill after
  commit, AND a row-removal crash from the 404'd new module). Restart before
  debugging the code — the code was correct both times.
- The legacy kill→build→run loop remains correct for: full-solution builds/tests
  (PR pre-flight), NuGet changes, migrations, and AppHost resource-graph changes.
- **A `dotnet watch` AppHost can come up with a dead proxy route (observed 2026-10-15).** The
  resource proxy kept its listener but never forwarded: `https://localhost:7300` timed out on both
  schemes (`curl (28)`) while the admin itself answered **200** on its own endpoint
  (`https://127.0.0.1:<admin-port>/`, discoverable from the `SchoolCollab.Admin` process's listening
  ports). Symptom to the user is "the app is broken / the page hangs" while every service is healthy.
  Workaround: drive the admin's own endpoint instead of the proxy port; if the proxy route is wanted
  back, restart the AppHost (the mapping is regenerated). Do not debug application code for this — check
  the direct endpoint first.

## Boundaries

- Hot reload accelerates iteration; it is **not** a verification gate. The build/test
  rules in `AGENTS.md` (build after every change, fix errors before continuing) are
  unchanged and still gate commits.
- CI is unaffected — this is a local-loop standard only.
