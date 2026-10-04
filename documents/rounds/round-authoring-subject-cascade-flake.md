# Round — `authoring-subject-cascade-flake`

**Status:** planned
**Tier:** Light (Tier 2) — worker + independent static diff reviewer
**Branch:** `stack/9-portal-teacher-surface` (layer 3, tip `b29e02ba`) — the fix rides the **lowest red layer** so every layer above inherits a green head. Layer 4 (`stack/10`) is restacked onto the new tip afterwards.
**Stack:** #297 — layer 3 carries the fix, layer 4 restacks onto it.

## Goal

Remove a **CI-only bUnit race** that reddens `Build & Test` non-deterministically — and therefore blocks the train, because `gh stack merge` merges a registered stack **atomically** (one red layer and nothing merges).

**The failure:** `SchoolCollab.Assignments.Tests.Unit.AssignmentCreateBunitTests.Create_AuthorOverrides_OverridesPrefillAndSubmitsValue` →
`AssertFailedException: Expected capturedBody not to be <null> because the primary action saves the draft`, with *"Total render count across all components: 0"*.

**It is not any layer's content.** It failed on **#298** (layer 3) and **#299** (layer 4), and **passed on a same-SHA re-run** — the skill's own diagnostic for non-determinism. `git diff --stat main stack/9-portal-teacher-surface -- tests/SchoolCollab.Assignments.Tests.Unit/AssignmentCreateBunitTests.cs` is **empty**: the test file is byte-identical to `main`.

## Settled rules this round follows (cited, not restated)

| Settled by | Rule applied |
|---|---|
| project skill `fix-flaky-bunit-fluentui-after-cascade` | *"A re-run is a **DIAGNOSTIC only** … the fix is always a code/config change to the test."* Harden the **test** — never the product handler (the FR-58 subject reload on grade change is intended behaviour). |
| same skill (step 9 + pitfalls) | *"make each UI interaction **OBSERVABLY applied** before the next step"* — the bUnit render queue drains lazily; `InvokeAsync` completes only work queued at entry. |
| same skill (step 4 + pitfall) | The class should carry the diagnostic `Assert.Fail` wrapper **permanently** — **absent in this tree** (grep for `Assert.Fail` finds nothing), so a recurrence reports a bare `NotBeNull` instead of the bailed guard. |
| **AGENTS.md:328 / :347** | *"fix the issue on the branch"* — the fix rides the branch, not a separate PR. |
| same skill's Verification + `reproduce-ci-test-failure-locally` | Validate with **10+ Linux Release container iterations** — a single local pass proves nothing for this class. |
| **Q3 (this round)** | **No CI retry.** A retry step would institutionalise exactly the masking the skill forbids — it would convert "this race exists" into "we don't notice it". |

## The mechanism (read from the code, not assumed)

`SelectAsync` (`AssignmentCreateBunitTests.cs:190-192`) was a bare `cut.InvokeAsync(...)` with **no settle**.

The clearing itself is **not** a late cascade. `OnGradeLevelChangedAsync` clears
`_selectedSubject = null; _subjectOptions = [];` **synchronously, before its first `await`**
(`AssignmentAuthoring.razor:1771-1772`), and the picker binds that handler **directly** (`:167-170`),
so the grade pick's state change is already applied when the test returns. What the missing settle
exposed is **render lag plus the FR-58 union-identity drop**: the subject picker's `Items` is rebuilt
as a **fresh array on every pass** (`:923-945`), and the control **drops a selection it cannot match by
instance** — the component documents exactly this failure mode at `:912-921` ("a fresh array on every
render makes the picker see its `Items` change on each pass, and the control then drops a selection it
cannot match by instance (the subject was silently cleared between the author's pick and the save)").
The test picks a **fresh** `new PickerOption(...)` (`:195`), never an instance from `Items`, so any
post-pick identity change drops it. Settling the pick therefore closes **render/union-identity lag**,
not a state-clobber window inside the handler.

> **Correction (review P2-2).** An earlier draft cited the `fix-flaky-bunit-fluentui-after-cascade`
> skill bullet as authority for "`Items` non-empty proves the FR-58 reload completed". That bullet was
> **authored by this round itself**, so it is not independent precedent, and the claim was broader than
> the evidence supports. The independent authorities are the component's own comment
> (`AssignmentAuthoring.razor:912-921`) and the sibling suite's load-complete settle
> (`AssignmentAuthoringBunitTests.cs:1392-1393`).

## Deliverables

| # | Deliverable |
|---|---|
| **D1** | Settle the interaction in the **shared helpers** (`SelectAsync` / `SelectGradeAsync`) so each pick is observably applied before the next step — one seam, so every caller inherits it, including the twin test (Q2). |
| **D2** | Sweep the class for other same-shape primings (the skill's own search rule) and cover them. |
| **D3** | Add the permanent diagnostic wrapper the skill mandates, so a future recurrence names the bailed guard. |
| **D4** | Verify per the skill: a local run **and** 10+ consecutive Linux Release container iterations with CI env vars. |

## Acceptance criteria

1. `dotnet test tests/SchoolCollab.Assignments.Tests.Unit` → **0 failures** locally.
2. **10+ consecutive iterations** in the Linux Release container (with CI env vars) → **all green**.
3. `Create_AuthorOverrides_OverridesPrefillAndSubmitsValue` is exercised in those iterations (the settle must not have been achieved by skipping it).
4. **No product code changed** — test-only, per the skill. **No CI retry** added (Q3).
5. Layer 4 rebases onto the new layer-3 tip **without conflict** and its content is unchanged (`git diff --stat <old-layer4-tip> <new-layer4-tip>` must be empty).

## Expected files

`tests/SchoolCollab.Assignments.Tests.Unit/AssignmentCreateBunitTests.cs` — **only**.

## Out of scope

- Any product/page change (the FR-58 subject clearing is intended).
- A CI retry step.
- Layers 1–2 (not red).
- Anything belonging to round `dev-teacher-identity-wiring`.

## Worker Report

**Worker `89410c64`** (implementation) + resumed run `665730bb` (P2-1 rework).

| # | Result |
|---|---|
| **D1** | `SelectAsync` / `SelectGradeAsync` now `await` the pick and then `WaitForAssertion` that `picker.SelectedOption.Value` equals the picked GUID **and** that the subject picker's `Items` is non-empty (`:198`, `:216-219`) — so the FR-58 subject reload has landed before anything depends on the pick. |
| **D2** | Swept the class: `SelectEveryoneAsync` and `SetTitleAsync` settled; the inline signature override extracted into `SetSignatureAsync` (`:257`). All **five** interaction seams settled; every `InvokeAsync` in the file is one of them. |
| **D3** | `AssertCapturedBodyNotNull` (`:280`) reflects `_error` / `_selectedSubject` plus `_createLogs` and calls `Assert.Fail` — permanently, on **both** POST paths (`:501`, `:538`). |
| **Rework** | P2-1: new shared `RenderCreatePage()` (`:194-201`) settles the **initial load** (`#authoring-basics-grade fluent-option` non-empty) before the first pick, matching the sibling suite's idiom (`AssignmentAuthoringBunitTests.cs:1392-1393`); used by all three affected tests (`:465`, `:491`, `:533`). |

**Evidence:** local `884 / 0` (parent re-ran it: `884 / 0`) · **Linux Release container** (`mcr.microsoft.com/dotnet/sdk:10.0`, `CI=true TZ=UTC LANG=en_US.UTF-8`) — **10 iterations, each 884 passed / 0 failed** · raw log contains **0** failure lines. Disclosed honestly rather than hidden: an earlier 12-iteration run was truncated at iteration 8 by a tool timeout, so a fresh 10-iteration pass is the reported evidence.

## Review

**Static diff reviewer `5117239e` (`deepseek-v4.1-flash`) — Verdict: `P2-only`, no P1, "round can be accepted: yes".**

Confirmed: the settle is sound; all five seams settled with non-vacuous waits that **fail on timeout** (5 s, no sleeps); the diagnostic wrapper fails properly and is applied to both POST assertions; sweep complete for the class; the twin test and `Create_GradeSelected_ReResolvesTheSignatureDefault` inherit the settle; scope clean — one tracked file, no product/page change, no workflow change.

**P2-1 — a real residual window (FIXED).** With no initial-load settle, `LoadPickersAsync`'s tail (`AssignmentAuthoring.razor:1614-1616` → `:1802-1805`) can still replace `_subjectOptions` after the picks — the same union-identity drop. Fixed by `RenderCreatePage()` above.

**P2-2 — this round's own citation was circular (CORRECTED).** The doc cited the `fix-flaky-bunit-fluentui-after-cascade` skill bullet as authority for "`Items` non-empty proves the FR-58 reload completed". That bullet was **authored by this round itself**, so it is not independent precedent, and the claim was broader than the evidence supports. The reviewer's precision is now recorded in the mechanism section above: the handler's clear site is **synchronous and pre-await** (`razor:1771-1772`), so the settle closes **render/union-identity lag**, not a state-clobber window inside the handler. Independent authorities: the component's own comment (`AssignmentAuthoring.razor:912-921`) and the sibling suite's load-complete settle (`AssignmentAuthoringBunitTests.cs:1392-1393`).

**Report-only, outside this round's declared scope:** the sibling suite's helpers remain bare (`AssignmentAuthoringBunitTests.cs:252-256`, `:266`); this class never drives the other subject-clearing control, the due-date picker (`razor:401` → `:1826/:1833`); `:450` waits with the default timeout while the rest of the file uses 5 s (pre-existing).

## Acceptance

**Verdict: CLOSED** (commit/push gated on an explicit instruction).

**Tier 2 (Light).** Parent planned; worker `89410c64` implemented; reviewer `5117239e` returned **P2-only, no P1**; rework `665730bb` closed P2-1; the parent verified the evidence and corrected P2-2.

| Gate | Result |
|---|---|
| `dotnet test tests/SchoolCollab.Assignments.Tests.Unit` | **884 / 0** (parent re-ran) |
| Linux Release container | **10 iterations, per-iteration 884 / 0** |
| Failure lines in the raw log | **0** |
| Scope | one tracked test file · no product code · **no CI retry** (Q3) |

**Acceptance criteria:** **AC1** met · **AC2** met (10 iterations with per-iteration evidence; the truncated earlier run disclosed rather than hidden) · **AC3** met · **AC4** met · **AC5** performed after the commit (layer 4 restacked onto this tip, `git diff --stat` empty).

**What this round bought:** the stack is unblocked. The same flake reddened **#298 and #299** and would have blocked `gh stack merge`, which merges a registered stack **atomically**. The fix targets the mechanism the component itself documents, at the shared seam, so every caller inherits it — and the mandated diagnostic is now permanent, so a recurrence names the bailed guard instead of a bare `NotBeNull`.

**Carried (report-only):** the sibling suite's bare helpers and the undriven due-date control.
