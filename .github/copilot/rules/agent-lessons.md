# Agent lessons (durable)

Lessons accepted by the owner after a build or defect pass. **Newest first.** Each entry carries:

- **the date** it was accepted,
- **the rule** — imperative, in this repo's voice,
- **the evidence** — what actually happened, with the command or error that showed it,
- **the trigger** — the signal a future agent should recognise as "this lesson applies".

`AGENTS.md` and the specialty rules **point here; they never restate a lesson inline.** A lesson
earns a place here only after the owner accepts it (`AGENTS.md` § "Propose, never pre-write"). A
lesson that fires twice should be promoted into a deterministic check or a specialty rule, and the
entry here shrinks to a pointer at that check.

---

## A half-converted gate is a capability loss (2026-10-26)

**Promoted to `documents/solution/feature-flag-workflow.md` § "Retiring a flag".** Removing a flag means
*converting every consumer* — grep the residue shape (`if (false)`, an always-firing `return;`, a `bool`
nothing assigns `true`), never the deleted key — and assert the **rendered consequence**, not the request.
That doc is read during a retirement, when the rule must fire. Evidence: PR #420 — `Subjects.razor` kept
`if (false)` and the Activity Group owner filter silently vanished, hidden by a guard test that asserted
only the HTTP call.

---
## An over-broad assertion reports itself as timing (2026-10-26)

**Promoted to `.github/copilot/rules/testing.md` § "A render-time exception looks like a FLAKY TIMEOUT"**
as its second cause: `Single`/`First`/`Count` over a shared request log is a **session** claim, not an
endpoint claim — filter to the endpoint and put the observed values in the failure message. Evidence: PR
#420/#418 (`NotificationTemplateCreateTests` reddened two of three full-suite runs with
`Sequence contains more than one matching element`). Five latent sibling sites remain unfixed:
`EnrollmentExceptionsDialogTests.cs:648, 796, 853, 944, 975`.

---
## Kill the repo's own processes yourself — do not ask (2026-10-24)

**Rule.** A `MSB3021` / `MSB3027` lock on a solution build is yours to clear. The locked outputs
belong to the repo's own services, and stopping one destroys none of the user's work:

```powershell
Get-Process | Where-Object { $_.ProcessName -like 'SchoolCollab*' } | Stop-Process -Force
```

A `dotnet watch` or the AppHost also needs its `dotnet` parent killed (`taskkill /PID <pid> /T /F`),
or it respawns the children within seconds. **One exception:** an IDE-launched debugger session is
the user's own session — ask before closing that. Kill, rebuild once, and report only a *surviving*
lock. Never retry the build in a loop and never wait on a question you can answer with a process list.

**Evidence.** 2026-10-24, the #404 round: three solution builds died on
`MSB3027 … The file is locked by: "SchoolCollab.Auth (31876)"` /
`"SchoolCollab.AI.Server (36632)"` / `"SchoolCollab.Assignments.Worker (33196)"` across roughly
twenty minutes. The first two rounds of that were spent surfacing the lock and waiting for an
instruction; the third was spent learning that killing the `dotnet watch` parent was necessary
because its children respawned. `AGENTS.md` at the time said *"surface the lock to the user and let
them choose how to stop the offending process"* — the wrong default for processes the agent owns.

**Trigger.** A build fails with `MSB3021`/`MSB3027`, or you are about to write "the build is blocked
by a running service — should I stop it?".

---

## A test run's summary is not its verdict (2026-10-24)

**Rule.** Read the **exit code** and the `Zero tests ran` lines, never the `failed:` counter alone.
`Directory.Build.props` injects `--ignore-exit-code 8` into every test project, so a filter that
matched nothing exits 0 and prints `failed: 0`; the same tolerance makes the summary lie in the
other direction when only *some* assemblies match nothing. `scripts/round/gates.sh` reads both on
purpose and treats "zero tests ran" as a failure — let it, or replicate that check.

**Evidence.** 2026-10-24, twice in one session. First: a targeted `--filter` run passed while CI
failed, because the filter could not see the consumer it had broken (a test asserting
`#authoring-basics .panel-section__title`, a header the change had just removed). Second, and the
sharper one: a local Release run printed `failed: 0 / succeeded: 4522` and **still exited 8** with
`Failed!`, because five assemblies ran zero tests — the exact reading that "closed nothing" on
2026-10-12 and again on 2026-10-15 (`scripts/round/README.md`).

**Trigger.** Any `dotnet test` you read yourself, especially one whose numbers you are about to
quote as proof; and any change to a shared component, where a filter cannot see the consumers.

---

## The MAUI head is not in the solution, so a shared-component break is invisible (2026-10-16)

**Rule.** A change to `SchoolCollab.Admin.Shared` or to any package version must also build the
mobile head —
`dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj -f net10.0-windows10.0.19041.0 -m:1 -p:BuildInParallel=false`
— because `src/Mobile/SchoolCollab.Mobile` is deliberately absent from `SchoolCollab.slnx`. The
`-m:1 -p:BuildInParallel=false` flags are part of the rule: the parallel build exhausts memory on
this machine and kills the agent process instead of reporting a compile error. `gates.sh` runs this
leg by default; `--no-maui` is only ever a baseline flag.

**Evidence.** 2026-10-16, the FluentUI Blazor 4.14.2 → 5.0.1 bump: the main solution stayed green
while the mobile head carried 22 v5-invalid call sites. Nothing in the primary build could see them.

**Trigger.** Touching `Admin.Shared`, any `Directory.Packages.props` pin, or writing a PR that
claims a shared component is covered.

---

## Hot reload cannot apply rude edits — a green build is not a changed page (2026-10-12)

**Rule.** When the tests pass but the running preview "doesn't work", **restart the preview
(stop/start) before debugging the code.** Hot reload refuses "rude edits": a new component
`[Parameter]`, a changed record's shape, new `@implements`/`@inject`, or a **new static asset**
(a collocated `*.razor.js`, served from `_content/...` only after a rebuild). bUnit's loose JS mode
hides the missing-asset 404, so nothing in the test suite contradicts the blank page.

**Evidence.** 2026-10-12, the recorder-pill round: tests green, preview missing the pill entirely.
The asset was a new collocated module that the running circuit had never loaded. Restarting
reproduced a working page with no code change. Symptom set:
`documents/runbooks/dev-loop-hot-reload.md`.

**Trigger.** "Tests pass but the page is blank / the new control is absent", especially right after
adding a `[Parameter]`, changing a DTO's shape, or adding a collocated JS module.

---

## A `(W)` deny refuses reads, and a folder deny breaks the skill tooling (2026-10-05)

**Rule.** Lock a vendored skill file with the write-only mask `(WD,AD)` on the **file**. Never a bare
`(W)` — `icacls` renders it *Write, Synchronize*, and almost every open requests Synchronize, so it
refuses reads as well as writes. Never a deny on the folder: it breaks the skill tooling outright.

**Evidence.** With `(W)` in place, `.NET OpenRead`, `OpenText`, `Get-Content -ErrorAction Stop` and
`cmd /c type` all failed with *Access denied*, while an undenyed skill (`gh-stack`) read fine —
the lock had made the skill unusable, not just unwritable. A folder-level `(W,D,DC)` deny was
removed the same day after `skill_manage create` began reporting `EPERM … scandir
'<…>/<skill-folder>'`; the failing `create` was never retried, so the folder deny is the
strongly-indicated cause rather than a proven one. `(WD,AD)` was then verified to allow
`OpenRead`/`OpenText` while still refusing `OpenWrite` and `Add-Content`.

**Trigger.** About to run `icacls … /deny` on anything, or a skill file that reads back as
"Access denied" and looks corrupt.

---

## Never put a multi-minute wait in a subagent brief (2026-09-25)

**Rule.** CI polling, sleeps and any other multi-minute wait belong in the **parent**, which
survives an interrupted call or a compaction. Hand a delegate only the quick legs (stage/commit/
push/PR, or merge + pull). When a brief is cut short anyway, recover from live state — `git log`,
`git ls-remote`, `gh pr view`, `gh pr checks` — and resume from the first unfinished step; never
re-run the whole brief.

**Evidence.** 2026-09-25: a delegate was aborted mid-poll by a session compaction. Only the steps it
had already completed — commit, push, PR — had persisted; the poll it was sitting in was lost, and
the round had to be reconstructed from the remote rather than from the agent.

**Trigger.** Writing a brief that contains "wait for CI", a `sleep`, or a poll loop.
