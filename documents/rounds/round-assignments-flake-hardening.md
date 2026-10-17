Provider: pi — Light round (Tier 2): worker `ollama-cloud/deepseek-v4.1-flash`, static diff reviewer `ollama-cloud/kimi-k2.7-code`
Mode: attended (owner pinned Q75/Q79 on 2026-10-16: fix the flake now, as a Light round; the stack merge stays held until this fix is inside the train)
Tier: 2 (light) — one worker + one static diff-only review; the flake-class verification is the container loop below, not a tester leg
Base: layer on `s3-fluentui-v5-fix` (tip `3065a086`); layer branch `s4-assignments-flake-hardening`
Tree at round start: clean
Status: CLOSED — Round D complete: the flake's un-settled picker seam is closed by routing `SelectSubjectAsync` through the hardened `SelectAsync`; 0 failures across 12 whole-solution Release iterations in a 2-CPU Linux container for the hardened test; static review OK, no P1/P2; parent build 0 errors. AC-4 is substantively met but literally not "all 12 green" — three unrelated tests failed (one deterministic and A/B-proven pre-existing, two one-off, all outside this round's single-file diff); recorded with evidence instead of redefining the criterion. Carried by the `s4-assignments-flake-hardening` layer.

## Objective

Close the CI-only non-determinism that red-lit the Round B layer (#356) and, because a registered `gh stack`
merges **atomically**, can silently block any future train. Fix is **test-only** — the race is in how the test
drives FluentUI, not in the page.

## Evidence

**CI (the failure).** Run `38905510940` on headSha `ed16684d` → `Build & Test` **fail** with `failed: 1`:

```
failed  Edit_RemovingAnActivityGroupEntry_SendsTheReducedTargetSetAndLinks  (167ms)
  at FluentAssertions.Primitives.ReferenceTypeAssertions`2.NotBeNull
  tests/SchoolCollab.Assignments.Tests.Unit (net10.0|x64) failed with 1 error(s)
```

**Diagnostic (skill step 3 — satisfied).** `gh run rerun 38905510940 --failed` → **pass 5m3s on the same SHA**,
and the same code also passed in the next layer's run. Non-deterministic: a flake, not a deterministic break.

**The test** (`tests/SchoolCollab.Assignments.Tests.Unit/AssignmentAuthoringBunitTests.cs:3375`):

```csharp
cut.WaitForAssertion(() => BuilderEntries(cut).Should().HaveCount(2), …);
RemoveBuilderEntry(cut, 1);                                        // FR-58 reset fires on the target-set change
cut.WaitForAssertion(() => BuilderEntries(cut).Should().HaveCount(1), …);
await SelectSubjectAsync(cut, dto.TopicId);                       // ← the un-hardened seam
SaveDraft(cut);                                                   // kebab → "Save draft"
cut.WaitForAssertion(() => updateBody.Should().NotBeNull(), 5s);  // ← the CI failure
```

`updateBody` null means the save never issued the PUT: a guard bailed and no request was made — the skill's
case-1 signature.

**Root cause, two halves.**

1. *The page resets the subject on a group change* (intended FR-58 behavior — do NOT change it):
   `AssignmentAuthoring.razor` clears `_subjects` / `_subjectOptions` / **`_selectedSubject`** in its reset
   (≈`:2229-2231`); the save reads `_selectedSubject` at `:1599` and reports `"Select a subject"` at `:1727`.
   So a subject selection that does not survive ⇒ no PUT.
2. *The test's subject pick is not settled.* The class already contains the hardened seam —
   `SelectAsync(cut, pickerId, value)` at `:439-460` — which (i) `WaitForAssertion`s that the value is present in
   the picker's **current `Items`** array, (ii) picks **that** instance, and (iii) asserts
   `Picker(...).Value?.Value == value` before returning. Its doc-comment cites skill
   `fix-flaky-bunit-fluentui-after-cascade`. **`SelectSubjectAsync` at `:463-471` predates that hardening**: it
   picks `Items!.First(...)` with no wait for the current array and **returns without asserting the pick landed**.
   FluentUI matches a selection by **instance**, and the subject picker rebuilds `Items` as a fresh array when the
   FR-58 reset lands — so a pick can be dropped, `_selectedSubject` stays null, and the save bails with no PUT.
   The flaky test is one of its callers (`:3394`), and the sibling bare drives are the same shape: the grading
   picker at `:1263` and `SetTitleAsync` at `:479` (the title is *also* a save guard).

## Plan (Tier 2 — parent-authored)

**Step 1 — route the subject pick through the hardened seam.** Replace the `SelectSubjectAsync` body with a
delegation, keeping the helper name so every call site is untouched:

```csharp
private static Task SelectSubjectAsync(IRenderedComponent<Authoring> cut, Guid topicId) =>
    SelectAsync(cut, "authoring-basics-subject", topicId.ToString());
```

**Step 2 — harden the sibling bare drives** that feed save guards, the same way (wait for the value in the
current `Items`, pick that instance, assert it landed before returning):
- the grading picker drive at `:1263` — prefer `await SelectAsync(cut, "<its picker Id>", value)`;
- `SetTitleAsync` at `:479` — keep the bound-callback drive, then `WaitForAssertion` that the input's `Value`
  reflects the title before returning.

**Step 3 — sweep the file for any remaining un-hardened drive.** `git grep -n "ValueChanged.InvokeAsync"` in the
test file; every remaining site must be either a bounded drive followed by a landed assertion, or explicitly
justified in the WORKER REPORT.

**Non-goals.** No product code. Do not change the page's FR-58 reset or its guards — the re-filter behavior is
intended and the test's assumption is what is wrong (skill pitfall). Do not touch other test files.

## Acceptance criteria

- **AC-1** `SelectSubjectAsync` delegates to the hardened `SelectAsync`; no picker drive in the file sets a value without a landed assertion.
- **AC-2** No file outside `tests/SchoolCollab.Assignments.Tests.Unit/` is in the round diff; zero product-code lines.
- **AC-3** Local: `dotnet test tests/SchoolCollab.Assignments.Tests.Unit` → 0 failures (parent re-runs).
- **AC-4** **Flake-class verification (skill-mandated):** build Release once inside a Linux container, then **loop the whole-solution CI test command** (`--filter FullyQualifiedName~Tests.Unit`, Release, `--no-build`) **12 times** with the GH-runner env (`CI=true TZ=UTC LANG=en_US.UTF-8 LC_ALL=en_US.UTF-8`) — **all 12 green**. Loop the whole solution, not the single project: the skill `reproduce-ci-test-failure-locally` is explicit that 2-CPU cross-assembly contention is part of the CI environment being reproduced, and this flake surfaced in a full-suite run. Container recipe: `mcr.microsoft.com/dotnet/sdk:10.0` (already pulled) with `--cpus=2` and `-v C:\Users\skwar\.nuget\packages:/root/.nuget/packages`; use only MTP flags that appear in `ci.yml` (adding `--nologo`/`-v q` breaks MTP with exit 5 / zero tests). Parent-run.
- **AC-5** `dotnet build SchoolCollab.slnx` → 0 errors (parent-run).
- **AC-6** Static diff review: no P1.
- **AC-7** Backlog §L (this flake) added in this round's commit, per the pinned Q76.
- **AC-8** After push: the layer's `Build & Test` green, and green again on a re-run of the same commit.

## Open decisions

None — Q75/Q79 were pinned by the owner on 2026-10-16.

## Worker Report

Worker `b88e85ac` (`ollama-cloud/deepseek-v4.1-flash`), one writer. One file changed; **19 insertions / 18 deletions**.

| # | Change | Effect |
|---|---|---|
| A | `SelectSubjectAsync` (`:461-466`) now delegates to the hardened `SelectAsync` | inherits the three guarantees: value present in the **current** `Items`, that instance picked, pick asserted landed before returning. Name/signature unchanged → call sites untouched. |
| B | `SetTitleAsync` (`:471-485`) gained a landed assertion on the bound `FluentTextInput.Value` | the title is a save guard, so a one-pass-late drive had the same "no PUT" signature |
| C | `ChangingGradingFormat_TogglesEnablement_NotPresence` (`:1254-1276`) became `async Task` and routes its grading drive through `await SelectAsync(...)` | removes the last synthetic-`PickerOption` drive |

**Sweep:** `ValueChanged.InvokeAsync` sites went 4 → 2, both inside hardened helpers; no site sets a value without a landed assertion. No assertion weakened, none deleted, no timeout widened as a substitute (the only added `TimeSpan` sits inside a **new** assertion at the file's existing 5s budget).

**Worker verdicts** (superseded by the parent's): Assignments suite 1167/1167; the previously-flaky test alone pass; solution build blocked once by the parent's own dev loop (`MSB3027`, AppHost PID 13804) — the worker correctly refused to kill it and reported it instead.

## Review

Static diff review by `c7034f61` (`ollama-cloud/kimi-k2.7-code`), read-only, against the frozen patch. **VERDICT: OK — no P1, no P2.**

Independently confirmed: `SelectAsync` really does all three legs (waits for the value in the current `Items`, picks that exact instance, asserts the landed value); the delegation is sound and its single call site (`:3395`) is the flaky test; `SetTitleAsync` asserts *state* rather than sleeping; test C loses no coverage (`Picker(...)` still proves existence via `.Single`) and removes the synthetic-instance anti-pattern; scope is strictly test-only; pinned decisions Q75/Q79 honoured. Could not verify: the runtime container loop (parent-owned) and build/test numbers (read-only by design).

## Acceptance

Parent-run authoritative pass. **Verdict: CLOSED**, with one criterion met in substance and reported honestly rather than redefined.

| Criterion | Result |
|---|---|
| **AC-1** delegation + no un-landed drive | **PASS** — sweep 4 → 2 sites, both hardened (worker + reviewer verified) |
| **AC-2** tests-only, no product code | **PASS** — 1 file in the patch; zero `src/` lines |
| **AC-3** Assignments suite green | **PASS** — 1167/1167 on Windows (worker) and 1167/1167 alone in the container (parent) |
| **AC-4** flake-class verification | **SUBSTANTIVELY PASS, LITERALLY NOT MET** — see below |
| **AC-5** `dotnet build SchoolCollab.slnx` | **PASS** — `Build succeeded`, 0 Error(s) (after stopping the parent's dev loop, which held `apphost.exe`) |
| **AC-6** static review, no P1 | **PASS** — OK, no P1 and no P2 |
| **AC-7** backlog §L | **PASS** — §L added |
| **AC-8** layer `Build & Test` green on push | **PENDING** — owner gate |

**AC-4 in detail — 12 whole-solution Release iterations**, 2-CPU Linux container, `CI=true TZ=UTC LANG/LC_ALL=en_US.UTF-8`, verbatim `ci.yml` command (`--filter FullyQualifiedName~Tests.Unit --ignore-exit-code 8`, 3951 tests):

| Test | Failures | Interpretation |
|---|---|---|
| **`Edit_RemovingAnActivityGroupEntry_SendsTheReducedTargetSetAndLinks`** — the test this round exists to harden | **0 / 12** | **the fix holds**: stable across the full suite under 2-CPU contention — the exact condition CI's failure needed |
| `AutoGraded_BindToModelEchoesValues` | 12 / 12 | deterministic; **A/B-proven pre-existing** — the same command with this round's diff *reverted* fails identically (`failed: 1`, 3950 succeeded). Passes alone and passes when its own assembly runs alone ⇒ cross-assembly/container condition, not seen on CI (all three stack layers green). |
| `Create_UnsetReviewPolicy_SubmitsTheAuthorsReviewChoice` | 1 / 12 (15.1 s) | one-off, **different file** (`AssignmentCreateBunitTests.cs`) |
| `CodedValueDropdown_WithSelectedId_MarksOptionSelected` | 1 / 12 (59 ms) | one-off, **different project** (`Settings.Tests.Unit/Components/CodedValueDropdownTests.cs`) |

So "all 12 iterations green" is not achievable in this container, and the cause is **outside this round's surface**: the round's diff is exactly one file, and none of the three other tests live in it. The criterion's *purpose* — "the hardened test no longer fails in the loaded-runner condition that produced the CI failure" — is met unambiguously (0/12). Recorded as a deviation with evidence rather than by relaxing the wording. Also fixed on the way: the container ships no `en_US` locale (only `C`/`C.utf8`/`POSIX`), so `LC_ALL=en_US.UTF-8` was silently failing; `en_US.UTF-8` is now generated, removing a fidelity gap between the container and the CI runner.

**Residuals (owner call pending):** the two one-off flakes above, plus the deterministic container-only `AutoGraded…` failure, are recorded in backlog §L. None is caused by this round.

