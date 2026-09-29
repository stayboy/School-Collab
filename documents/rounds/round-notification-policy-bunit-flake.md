# Round: notification-policy bUnit CI flake — close the toggle→Submit window

Provider: pi `ollama` · **Tier 2** (light round: worker + static diff-only reviewer).
Models: worker `ollama-cloud/deepseek-v4.1-flash` · reviewer `openrouter/stealth/space-bunny-alpha` (thinking high)
— per-role override, the owner's standing choice for the code/diff reviewer (skill precedence item 1).
Round base: `3bfab443` (origin/main; tree clean at round start). Branch: `fix/notification-policy-bunit-flake`.

## Plan

### Goal
Make the interactive tests in `NotificationPolicyFieldEditDialogTests` deterministic so the CI-only flake
`Dialog_ChangesBothScopes_WritesBothEndpoints` stops blocking merges — without weakening a single assertion.

### Established facts (do not re-derive)
- PR #269 run `36551970011`: `Build & Test` failed on `Dialog_ChangesBothScopes_WritesBothEndpoints` (37 ms),
  `failed: 1` of 2858; `SchoolCollab.Admin.Tests.Unit` reported `failed with 1 error(s)`.
- The SAME commit **passed on a re-run** ⇒ non-deterministic. The test file was last changed in #149; the dialog
  and its child editor are untouched by #269. The missing item is the **SECOND** of two interactions.
- `NotificationPolicyFieldValueEditor` renders `checked="@Channels.Contains(channel)"` with
  `@onchange="e => OnToggleChannel(channel, e.Value is true)"`; `OnToggleChannel` is `async Task` ending in
  `await ChannelsChanged.InvokeAsync(updated)`; `Channels` is ONE-WAY from the parent's `@bind-Channels`.
- Razor codegen emits `EventCallback.Factory.Create<ChangeEventArgs>(this, e => OnToggleChannel(...))`
  (`obj/Debug/net10.0/generated/.../NotificationPolicyFieldValueEditor_razor.g.cs:212-215`). The `.g.cs` does
  **not** reveal which `Create` overload resolves — `Action<T>` (fire-and-forget) or `Func<T,Task>` (awaited).
  **Treat the await-ness as UNKNOWN until Step 1 measures it.**
- `SubmitAsync` decides each scope by comparing `_globalChannels`/`_gradeChannels` against load-time snapshots
  taken in `OnModelInitialized`; a lost toggle ⇒ `gradeChanged == false` ⇒ no grade PUT ⇒ the observed failure.

### Step 1 — establish the mechanism FIRST (bounded; report the evidence)
Determine whether the binding's continuation can be observed late in-process. Run a bounded probe: toggle a
channel with `.Change(true)` and assert **immediately** (no await, no wait) that the one-way `checked` state
reflects the toggle, repeated **≥50 times in one process**. Report whether it ever failed and, if so, how often.
State, from the generated code plus that measurement, whether the handler's `Task` is awaited by the dispatch.

### Step 2 — fix, causally
Add **ONE** private helper used by every interactive test that performs a checkbox toggle and returns only when
the toggle's effect is observable through the one-way bound parameter (the same `input[type=checkbox]` now reports
checked — which can only be true *after* the parent's field was assigned and re-rendered). Apply it to all four
interactive tests.

### Step 3 — report the outcome honestly
If Step 1 shows the same continuation loss can be triggered by a user clicking Save straight after a toggle, say
so explicitly: the product then has a real race and a test-side wait **masks** it. Report it as a finding with
your evidence; do **not** change product code in this round (out of scope — it will be scheduled deliberately).

### Scope
- `tests/SchoolCollab.Admin.Tests.Unit/NotificationPolicyFieldEditDialogTests.cs` ONLY.
- No product file, no schema, no new public API.

### Explicitly rejected
- `Task.Delay` / `Thread.Sleep` / retry loops / a longer `WaitAsync` timeout — the wait must be causal.
- Weakening or deleting an assertion; keep BOTH endpoints asserted in the two-scope test.
- Re-running CI as the remedy.

### Acceptance criteria
- **A1** all four interactive tests prove the interaction landed before `Submit()`.
- **A2** `SchoolCollab.Admin.Tests.Unit` passes locally with 0 failures.
- **A3** the wait is causal (observable bound state), not time-based.
- **A4** no product file changed.
- **A5** the two-scope test still asserts BOTH the global and grade PUTs and both bodies.
- **A6** the mechanism, with the Step-1 evidence, is stated in the WORKER REPORT.

## Worker Report

Run `45219bfd` (worker `ollama-cloud/deepseek-v4.1-flash`), settled.

- Changed files: `tests/SchoolCollab.Admin.Tests.Unit/NotificationPolicyFieldEditDialogTests.cs` (+59/−10; the only changed path).
- Build: 0 errors (`dotnet build SchoolCollab.slnx`).
- Tests: `SchoolCollab.Admin.Tests.Unit` 595 passed / 0 failed (5 consecutive project runs); the five dialog tests 0 failed over 30 class-only runs.
- Mechanism (Step 1, the plan's mandated measurement): A/B in one process, 100 iterations per shape — the pre-fix immediate `checked` observation was stale in **12/100**, then **22/100** on a repeat, and in exactly those runs the captured calls were *global PUT present, grade PUT missing* (the CI signature). The shipped helper shape measured **0/100** with both PUT bodies asserted. Codegen binds the `onchange` lambda to `Func<ChangeEventArgs,Task>` (awaited form), but bUnit's synchronous `Change()` dispatches through the renderer and returns after only `ThrowIfFailedSynchronously`; the second dispatch is therefore queued holding an event-handler id the first interaction's render has already re-assigned/disposed (measured as a `GetRequiredEventBindingEntry` ArgumentException). The interaction is **dropped, not merely late**. No product-side race: the parent binding is the Razor-generated synchronous `Action<List<NotificationChannel>>` setter, so a real click→Save cannot lose a toggle.
- Deviations: (1) a symmetric `SetTimeAsync` helper was added so the time-field test gets the same proof (the plan permitted either choice); (2) the helper quiesces the renderer both before find+dispatch and after it, extra to the plan's wording, justified by the measured disposed-handler-id drop; (3) all Step-1/Step-2 probes deleted — the diff contains only the fix.

## Review

Static diff review, run `4addbfe6` (`openrouter/stealth/space-bunny-alpha`, read-only; `watchdog_diff` confirmed the working tree matched the frozen patch at 1 file, +59/−10).

```
REVIEW
Verdict: PASS
P1: (none)
P2: tests/.../NotificationPolicyFieldEditDialogTests.cs:2 - `using AngleSharp.Dom;` inserted after `using Bunit;`, breaking the
    sort order the sibling files in this directory all use (.editorconfig:28 dotnet_sort_system_directives_first = true).
    Cosmetic only: EnforceCodeStyleInBuild is off and CI runs no `dotnet format --verify-no-changes`.
P2: tests/.../NotificationPolicyFieldEditDialogTests.cs:211,242,307 - the new GlobalPanel/GradePanel constants are not used by
    three pre-existing WaitForAssertion selectors that still spell the panels as literals. Optional; leaving it is defensible
    (rewriting pre-existing lines is out of scope).
Best-practices: no overwrites / skills honored / readable - no P1 violations.
```

Independent findings worth recording:
- **A3 (causality) upheld.** `cut.InvokeAsync(() => { })` enqueues a no-op on the renderer's own FIFO dispatcher, so it completes strictly after the handler and the render it caused — no elapsed time is involved. It is sound only *because* the helper **re-queries the DOM after both barriers**; had it held the pre-toggle `IElement`, the quiesce would have been cosmetic.
- **Cannot mask a product defect.** Both helpers can only produce a *false failure*, never a false pass, since the asserted state is only producible by the parent's assignment.
- **No product race, confirmed from source**: `OnToggleChannel` (`NotificationPolicyFieldValueEditor.razor:76-88`) awaits only `ChannelsChanged.InvokeAsync(updated)`, whose receiver's generated binding is a synchronous field setter, so `SubmitAsync` can never read a pre-toggle value on a real click→Save.
- The doc comment matches the code; the one sentence "the render re-assigns the element's event-handler id" is a *diagnostic* claim inherited from the probe rather than something the shipped code demonstrates — scoping, not misrepresentation.

## Acceptance

**Verdict: CLOSED — no P1. Rework iterations: 0.** Tier 2 (parent-authored plan, parent-transcribed verdict).

| Criterion | Verdict | Evidence |
|---|---|---|
| A1 all four interactive tests prove the interaction landed before `Submit()` | PASS | all four route through `ToggleChannelAsync`/`SetTimeAsync` (patch lines 213, 242, 275-276, 308) |
| A2 `Admin.Tests.Unit` passes locally, 0 failures | PASS | parent-run **595 / 0 failed / 0 skipped**, twice (pre- and post-P2-1) |
| A3 the wait is causal, not time-based | PASS | `QuiesceAsync` = no-op on the FIFO renderer dispatcher + assertion on one-way bound state; reviewer independently upheld it; no `Task.Delay`/`Thread.Sleep`/retry/lengthened `WaitAsync` in the diff |
| A4 no product file changed | PASS | `git diff --name-only` = exactly the one test file; reviewer confirmed |
| A5 both endpoint assertions survive | PASS | both PUT bodies (`"preferredChannelOrder":[0,1]` and `[2]`) and both `NotContain` guards unchanged |
| A6 mechanism stated in the report | PASS | Step-1 A/B above |

Parent authoritative pass: `dotnet build SchoolCollab.slnx` **0 errors**; `SchoolCollab.Admin.Tests.Unit` **595/0**; `SchoolCollab.ArchitectureTests.Unit` **72/0**.

P2 dispositions: **P2-1 applied** (a 1-line using-order reorder made *after* the review; the diff's only post-review delta, re-verified build 0 errors + 595/0, and the substantive diff the reviewer PASSed is otherwise byte-identical). **P2-2 accepted as a residual** — applying it would mean rewriting three pre-existing lines, which is scope creep the round is required to reject.

Residuals and open items:
1. **Closed by parent measurement** — the worker's Step-1 A/B was independently reproduced by the parent. A temporary probe (100 iterations per shape, a fresh bUnit context per iteration because `Register()` adds services and so cannot run after a render) reported:
   `oldStale=29/100  oldCiSignature=29/100  newStale=0/100`
   — the pre-fix shape loses the grade write in **29%** of runs and in **all 29** the global write still happened (the exact CI signature), while the shipped helper shape is **0/100**. The probe was then deleted and the file restored byte-for-byte to the reviewed state, verified by diffing `git diff` against the frozen patch (identical).
2. Reviewer's out-of-round observation: `OnToggleChannel` copies the possibly-stale `Channels` parameter, so a rapid same-panel double-toggle could tear. Pre-existing, not made reachable by this diff, and not masked by any test — a candidate backlog item, not this round's scope.
3. If a future bUnit/Blazor splits a render batch across dispatcher turns, the one-shot assertions would surface as a *new false-failure* flake rather than a silent pass; `WaitForAssertion` would be the fallback.
