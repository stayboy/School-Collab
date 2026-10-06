# Round — `assignment-index-menu-flake`

**Status:** **accepted** 2026-10-06 — reviewer approved; Linux Release 12× validation green
**Tier:** Light (Tier 2) — one worker run + an independent static diff reviewer
**Models:** parent `ollama-cloud/deepseek-v4.1-flash` (session) · worker `ollama-cloud/deepseek-v4.1-flash` · diff reviewer `ollama-cloud/kimi-k2.7-code`
**Patch:** `documents/rounds/diffs-assignment-index-menu-flake.patch` (18 hunks, +34/−23, one file, base `HEAD` = `35d43a44`)
**Branch:** none — the fix rides the working tree; no branch/commit authorized.

## Plan

Harden a **verified CI-only bUnit flake** (skill `fix-flaky-bunit-fluentui-after-cascade`, case 3).

**The failure:** `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentIndexBunitTests.cs:202` →
`Index_RequiresApproval_ApprovedDraftRow_ShowsPublish_NotSubmitForApproval`, a FluentAssertions `.Contain`,
**3359/3360 passed**, on a **docs-only** PR (#313, run `37443490998`) minutes after the same suite passed on
#312; `gh run rerun --failed` gave **pass 6m22s on the SAME SHA**. Per the skill, that green re-run is a
**diagnostic, not a fix** — the race remains and can redden unrelated PRs (and `gh stack merge` merges
atomically, so one red layer blocks a train).

**Mechanism (root-caused at the seam before the round):** the tests open the row-actions kebab and read the
items in the very next statement —

```csharp
cut.Find("fluent-button[title=\"Assignment actions\"]").Click();
var items = cut.FindAll("fluent-menu-item").Select(i => i.TextContent.Trim()).ToList();
items.Should().Contain("Publish", …);
```

A FluentUI `FluentMenu` renders its `fluent-menu-item` children only while open, on the render pass raised
*after* the click's state change cascades, so `FindAll` in the next statement has no ordering guarantee: on a
loaded runner it can observe an empty or partial list. A `.NotContain` cannot be waited on, so the ordering
must be fixed where the items are read.

**Intended scope (parent):** one shared private helper that opens the kebab **and waits until the items are
observably rendered**, then returns them; applied at the flake site and its sibling
(`Index_RequiresApproval_DraftRow_ShowsSubmitForApproval_NotPublish`), and extended to any other
`fluent-menu-item` site in the class that shares the race. **Test-only** — no product code, no assertion text
changed, no new packages, minimal diff.

**Verification split:** the worker runs the affected project 3× locally (a single green pass proves nothing
for this class); the **parent owns** the Linux-container 10-iteration Release validation, because only that
run carries the loaded-runner ordering.

## Worker Report

Delivered: 1 file, +34/−23, 18 hunks. Nothing staged; no product code touched; assertion text unchanged.

- **Helper** added at `AssignmentIndexBunitTests.cs:84-99` (placed after the `MakeRow` overloads):
  `private static List<string> OpenRowActionsAndReadItems(IRenderedComponent<IndexPage> cut)` — clicks the
  kebab, then `cut.WaitForAssertion(() => cut.FindAll("fluent-menu-item").Count.Should().BeGreaterThan(0, …),
  TimeSpan.FromSeconds(15))`, then returns the trimmed labels. The file's `15s` convention is kept.
- **6 read sites** now take the helper's list (the CI flake site moved 221 → 236; the sibling 184 → 200).
- **11 click-only sites** now call the helper for its open+wait and discard the returned labels (the
  `List<string>` signature cannot serve a caller that needs the element itself).
- **1 site deliberately left without a second helper call** (old 197 / new 213): the same test has already
  opened the menu, and `RowActionsMenu.ToggleMenu()` flips `_open`, so a second kebab click would **close**
  it. Clicking a `fluent-menu-item` directly is correct there.
- **Verification:** `dotnet build … -c Release` → 0 errors (42 pre-existing warnings);
  `dotnet run --project tests/SchoolCollab.Assignments.Tests.Unit -c Release --no-build` **3×, each
  884/884 passed, 0 failed**.
- **Not claimed:** the worker explicitly does not claim the flake closed — 3 green Windows runs cannot prove
  a loaded-runner ordering fix.
- **Residual points raised for review:** (a) the 11 click-only sites discard the helper's return value;
  (b) the wait's observable is *the menu is non-empty*, not *the item this test asserts is present*.

## Review

Static diff reviewer (`ollama-cloud/kimi-k2.7-code`), read-only, patch passed by path. **Verdict: approve.**

| Question | Verdict | Evidence |
|---|---|---|
| Q1 — is the `Count > 0` gate sufficient, or only narrowing? | **Sufficient — it closes the race** | `src/SchoolCollab.Admin.Shared/Components/RowActionsMenu.razor:115-131` renders the items with one synchronous `@foreach` over the already-materialized `Actions` list, built synchronously by `Index.razor:408-487` (`BuildAssignmentActions` → plain `List<RowAction>`, no async yield, no progressive rendering). The DOM goes from **zero** items (closed) to the **full** set in a single cascade, so waiting for the first item guarantees the asserted item is present. |
| Q2 — toggle hazard (a second kebab click closes the menu) | **Verified, no hazard** | `RowActionsMenu.razor:152-155` `ToggleMenu()` is `_open = !_open`. Enumeration confirms **no test calls the helper twice**; all 11 click-only sites call it once and immediately click their item while the menu is open. |
| Q3 — missed sites | **None** | All 18 `fluent-menu-item` read/click sites are preceded by the helper in the same test; the unguarded pattern no longer exists anywhere in the file. |
| Q4 — semantics/scope | **Test-only, no behaviour change** | One file; every expected value and polarity unchanged; no product code. |
| Q5 — `_mockHttp.Expect` ordering | **Preserved** | The helper issues **no HTTP** (a DOM click + `WaitForAssertion`), so inserting it between the Expect registration and the item click cannot satisfy or disturb the Expect; at `:839`/`:919` the Expect remains literally immediately before the click. |

The reviewer's only suggestion (an overload waiting on a specific label) was explicitly marked **unnecessary** given the single-pass render. My pre-review suspicion that `Count > 0` was too weak was **refuted by component evidence** — recorded here so it is not re-litigated.

## Acceptance

**Accepted 2026-10-06.** The race at the flake site is closed at the seam, not merely narrowed.

| Evidence | Result |
|---|---|
| Worker, Windows Release, `dotnet run --project tests/SchoolCollab.Assignments.Tests.Unit -c Release` | **3× 884/884, 0 failed** |
| Parent, **Linux Release container** (`mcr.microsoft.com/dotnet/sdk:10.0`, `CI=true TZ=UTC LANG=en_US.UTF-8`), full-suite loop | **12× 884/884, 0 failed** |
| Build | Linux `0 Error(s)`; Windows Release 0 errors (42 pre-existing warnings) |
| Independent static diff review | **approve**, all five questions answered with code evidence |
| Product code | untouched |
| Patch | `diffs-assignment-index-menu-flake.patch` — 18 hunks, +34/−23, 1 file, base `HEAD` |

Per the skill this is the *diagnostic-then-fix* standard: the green same-SHA re-run in CI proved non-determinism, and the fix is a **code change to the test** (the read is now gated on an observable render), not a re-run.

**Durable outcome:** the project skill `fix-flaky-bunit-fluentui-after-cascade` case 3 is updated from *"Not yet hardened"* to the hardened record, with this round's mechanism (**a lazy `FluentMenu` render**, a third trigger beside the `:after` cascade and the un-landed second interaction).

## UI Tester

N/A — no UI was delivered (a test's ordering was hardened).
