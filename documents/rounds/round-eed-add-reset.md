# Round — enrollment-exceptions dialog: a Reset that cancels a pending add

**Provider: pi/clinepass — Tier 2 (light), owner override on a UI round** (worker `clinepass/cline-pass/deepseek-v4.1-flash`; reviewer `clinepass/cline-free/mimo-v2.6-flash`, owner-named). Third round on branch `feat/enrollment-exception-dialog`, continuing the same uncommitted change set (`enrollment-exception-dialog`, then `eed-form-gating`). Tier/provider carry forward from the prior round, per the skill's precedence rule for continuing rounds.

> **Owner override recorded (Tier 2 on a UI round), third round running.** Same deviation. **Consequence: no UI-tester pass.**

---

## 1. The owner's request

> "'Add Exception' must have a reset button to cancel 'Add' in the dialog"

Today the trigger (`+ Add exception`) is rendered only while the form is gated and then **disappears** once armed, so the only way out of an armed form is the footer's Close — which dismisses the whole dialog. The armed state needs its own escape that abandons the pending add but keeps the dialog.

## 2. Settled decisions

| # | Decision |
|---|---|
| R1 | **Reset is the exact inverse of the trigger.** `ShowAddReset => _addTriggered;` — shown iff the reader explicitly armed the form. By construction it can never be true alongside the trigger (`ShowAddTrigger => _exceptions is { Length: > 0 } && !_addTriggered`), so the two controls are mutually exclusive in the DOM. |
| R2 | **Placement and shape:** an outlined `Reset` `FluentButton` (`OnClick="OnAddReset"`) in the **add region's heading row**, exactly where the trigger sits, so the control does not move between the two states. An icon is optional — if one is used it **must** be an existing constant from the repo's `FluentIcons` (an invented name is a build break, not a fallback). |
| R3 | **Do not duplicate the clear logic.** The `SubmitAsync` success path already clears positions, both dates, Reason and the division (`.razor:633-637`). Extract that into **one** private method (e.g. `ClearAddForm()`) and call it from both the success path and `OnAddReset`, so a post-add clear and a Reset clear can never drift apart. **This refactor is the point of R3, not a nicety.** |
| R4 | **Reset writes nothing and closes nothing.** No API call, no `SubmitAsync`, no dialog close — it discards in-progress input only. |
| R5 | **No Reset when the list is empty.** The form is live there by construction (F1's first clause), so there is nothing to cancel; and `_addTriggered` can only become true while a list exists, because the trigger is only rendered for `Length > 0`. |

## 3. Expected files

- `src/Students/SchoolCollab.Students.Application/Components/Students/EnrollmentExceptionsDialog.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Students/EnrollmentExceptionsDialog.razor.css` (only if the heading row needs a rule)
- `tests/SchoolCollab.Admin.Tests.Unit/EnrollmentExceptionsDialogTests.cs`
- `documents/specs/subject-period-exception-model.md` — a short clause added to the §5.1 sentence this feature already amended

**Nothing else.** NOT `DialogShellFooter.razor`, NOT the three host pages, NOT the round doc.

## 4. Implementation steps

1. Extract the success path's clear (`.razor:633-637`) into one private method; call it from that path unchanged (no behaviour change from the extraction alone).
2. Add `ShowAddReset => _addTriggered;` and `OnAddReset()` — clear via the extracted method, then `_addTriggered = false`.
3. Add the Reset button to the heading row, gated on R1/R2, beside the existing trigger block.
4. Add the scoped CSS only if the heading row needs it — **merge, never blanket-overwrite**.
5. Tests: add the H-cases; the existing 26 tests must stay green untouched unless one genuinely needs the new control.
6. Amend the spec clause.
7. `dotnet build SchoolCollab.slnx` (0 errors), then `dotnet test tests/SchoolCollab.Admin.Tests.Unit`.

## 5. Acceptance criteria

| # | Criterion | Discriminating check |
|---|---|---|
| H1 | Armed ⇒ the trigger is **gone** and a `Reset` control **is rendered** | new bUnit test asserting both, against the pre-change DOM where no Reset exists at all |
| H2 | `Reset` **clears** positions, both dates and Reason, and returns the division to its default | new bUnit test that arms, ticks a position, picks a real division and types a reason, then Resets and asserts each is back to default — **it must assert the pre-Reset state first**, or it cannot fail |
| H3 | After `Reset` the controls are **disabled again** and the trigger is **back** | new bUnit test |
| H4 | `Reset` writes **nothing** (no `POST`, in particular no `/bulk`) and the dialog stays open | new bUnit test asserting the handler saw no POST and the dialog is still rendered |
| H5 | With an **empty** list no `Reset` is rendered | new bUnit test |
| H6 | The trigger and `Reset` are **never both** in the DOM | asserted in both H1 and H3 |
| H7 | **One** clear implementation — the success path and `OnAddReset` both call it | source-level assertion that `OnAddReset` calls `ClearAddForm()` (or the chosen name) and that the success path does too; plus H2/H3 prove it behaviourally |
| H8 | `dotnet build SchoolCollab.slnx` 0 errors; `Admin.Tests.Unit` 0 failures | worker + parent run |

## 6. Constraints

- Repo-scoped searches only (`src/`, `tests/`, `documents/`) — **never** `find /`.
- No new NuGet package; CPM — never a `Version` on a `PackageReference`.
- No API / endpoint / migration / contract change. No new feature flag.
- Honour `dialog-ui` §2/§3, the CSS-isolation rule and `dotnet-best-practices`.
- **Do not weaken or delete an existing assertion.** If a kept test now needs to account for the new control, adjust its setup, not its verdict.
- **Never edit this round doc.** Self-report to `documents/rounds/.eed-reset-worker-report.md` (scratch, untracked).
- Test-output rule: one `dotnet test tests/SchoolCollab.Admin.Tests.Unit 2>&1 | grep -E "^\s*failed|total:|failed:" | head -40` per read, at most 2 attempts.

## 7. Worker task spec

Implement §4 in order. Return ONLY:

```
WORKER REPORT
Changed files: <path list>
Build: <"0 errors" | "n errors" + one-line detail each>
Tests: <project: n passed, m failed | "not run" + why>
Extracted clear method: <name> called from <success path | OnAddReset>
Deviations from plan: <none | one line each>
```

---

## Worker Report

> **Run 1 was aborted by a harness interruption** after it had already written the production code and all six tests (no self-report exists; the plan's `§4 step 6` spec clause was not reached — the parent folded it at report time per the skill's step 8). The table below is reconstructed from the frozen patch and the parent's authoritative pass; the **Rework** section carries the rework run's verbatim report.

| Field | Value |
|---|---|
| Changed files | `EnrollmentExceptionsDialog.razor` (59+/11−), `EnrollmentExceptionsDialogTests.cs` (212+/0−) |
| Build (parent) | **0 errors**, 17 warnings (pre-existing) — nothing from the changed files |
| Tests (parent) | `Admin.Tests.Unit` **595 / 0** (dialog tests 32, +6); `ArchitectureTests.Unit` **72 / 0** |
| Wiring added | `ShowAddReset` `:602`, `OnAddReset` `:709-717`, `ClearAddForm` `:727-735` (all six statements lifted verbatim from the removed success-path block) called from `:653` (success) and `:715` (Reset) |
| Tests added | `Reset_ReplacesTheTrigger_WhenTheFormIsArmed`, `Reset_ClearsTheTypedRangeAndReason_WithoutWritingAnything`, `Reset_ClearsTheChosenPart_AndReGatesTheForm`, `Reset_UnTicksTheChosenSequence_WhereTheLadderSurvivesIt`, `EmptyList_OffersNoReset`, `Reset_AndTheSuccessPath_ShareTheOneClearImplementation` |
| Deviations | No self-report (abort); spec clause deferred to the parent |

---

## Rework — Tier-2 iteration 1 (the one permitted)

The reviewer found a **reachable R5 violation** plus two real test-quality gaps.

| Item | Fix |
|---|---|
| **P1** | `ShowAddReset` gained the list-length clause: `=> _addTriggered && _exceptions is { Length: > 0 }` (`:604`), so arm → remove-the-last-row no longer leaves a Reset on an empty list where the form is live by construction. `RemoveAsync`'s success path also disarms when the reload empties the list (`:692`), so the flag cannot outlive the row set it was armed against. New test `ArmedForm_LosesItsReset_WhenTheLastRowIsRemoved` covers exactly the corner the reviewer traced. |
| **P2 #1** | The H7 test was narrower than its own comment: a partial copy omitting `_reason = null;` passed. `NotContain("_positions.Clear()")` added to **both** caller segments, and the overstated class comment rewritten to claim only what is checked. |
| **P2 #2** | `Reset_ClearsTheTypedRangeAndReason_WithoutWritingAnything` carried an assertion that could not fail (the division is never moved off default there). The **sanctioned fallback** was taken — assertion removed — because the preferred fix is **structurally impossible**: `SpanIsDerived` (`:517`) removes both range pickers once a real part is chosen, so no render can hold the typed free-window range *and* be off `Any date`; and moving the division after typing clears both dates via `OnDivisionChangedAsync`, which would weaken the test's pre-existing range assertions. The division half stays covered discriminatingly by `Reset_ClearsTheChosenPart_AndReGatesTheForm`. |
| **P2 ×2 accepted** | `:1253-1256` preservation pins; `:1266-1283` `EmptyList_OffersNoReset` (an inherent H5 negative guard that cannot fail pre-change). Report-only. |

**Mutation proofs (required, and both delivered):**

1. **The P1 corner test is falsifiable** — reverting the predicate to `=> _addTriggered` and holding back the disarm made `ArmedForm_LosesItsReset_WhenTheLastRowIsRemoved` **FAIL** (`total: 1, failed: 1`); restored → full suite **596 / 0**.
2. **The tightened H7 test is falsifiable** — re-inlining a partial copy in `OnAddReset` that keeps `ClearAddForm()` but omits `_reason = null;` made H7 **FAIL** on `NotContain("_positions.Clear()")`. The legitimate partial copy in `OnDivisionChangedAsync` (`:745-748`) lives in a different method and stays outside both asserted segments.

Restore was byte-exact (`md5sum -c` against a pre-mutation snapshot: both `OK`); no mutation residue in `git diff`.

**Rework worker report** (`clinepass/cline-pass/deepseek-v4.1-flash`): changed files = the dialog `.razor` and the test file; build 0 errors; `Admin.Tests.Unit` **596 passed / 0 failed**; both mutation checks as above; leftover mutation diff: none.

**Re-verification:** **parent scope-check** (sanctioned for a small rework) — the delta is the guarded predicate + one disarm + test tightening with no production change beyond P1's two lines; both mutation proofs are stronger evidence than a static re-read. No reviewer re-run.

---

## Review

Static reviewer `clinepass/cline-free/mimo-v2.6-flash` — read-only; never builds, never tests, never writes files.

### Diff review #1 — verdict **P1**

```
REVIEW
Verdict: P1

P1:
- src/Students/SchoolCollab.Students.Application/Components/Students/EnrollmentExceptionsDialog.razor:602 — R5/§5.1 contradiction in a reachable corner: `ShowAddReset => _addTriggered` carries no list-length guard, and `_addTriggered` is only ever cleared at `:654` (success) and `:716` (Reset). Chain: arm via `OnAddTrigger` `:702` -> click the row's remove button (`:83-88`, `Disabled="@_busy"` only, not gated by armed state) -> `RemoveAsync` `:672-691` reloads an empty list without touching the flag -> `:602` renders **Reset on an empty list**. This contradicts binding R5 ("No Reset when the list is empty") and the spec sentence that already sits in working-tree §5.1 (`documents/specs/subject-period-exception-model.md:489-491`). The H5 test (`tests/.../EnrollmentExceptionsDialogTests.cs:1265`) only covers the fresh-open-empty case, so it cannot see this. Smallest fix: `=> _addTriggered && _exceptions is { Length: > 0 }`, or — if the armed escape is deliberately preferred in that corner — re-scope R5 and the §5.1 sentence to "a list that opens empty" during the parent's spec fold-in.

P2:
- tests/SchoolCollab.Admin.Tests.Unit/EnrollmentExceptionsDialogTests.cs:1508-1513 — H7 gap: a re-introduced clear that **omits `_reason = null;`** passes all four checks; the `OnAddReset` segment asserts only `Contain("ClearAddForm()")`, and the global tripwire at `:1512` counts only `_reason = null;`. Smallest fix: add `NotContain("_positions.Clear()")` to both caller segments.
- tests/.../EnrollmentExceptionsDialogTests.cs:1184-1185 — assertion that cannot fail: `DivisionLabel(...).Be("Any date")` after Reset in `Reset_ClearsTheTypedRangeAndReason_WithoutWritingAnything` — the division is never moved off default there, so it holds whether or not the Reset clears it.
- tests/.../EnrollmentExceptionsDialogTests.cs:1253-1256 — preservation pins that cannot fail from the Reset action.
- tests/.../EnrollmentExceptionsDialogTests.cs:1266-1283 — `EmptyList_OffersNoReset` would not fail against pre-change code (inherent to an H5 negative guard), report-only.

Correct: R1 — mutual exclusivity unconditional, asserted both directions (`:1131-1136`, `:1260-1262`). R2 — Reset in `add-head` (`:113`), button `:124-134`, outlined, no icon, so no invented `FluentIcons` constant. R3 — exactly one `ClearAddForm` (`:727-735`) with both call sites (`:653`, `:715`); the second `_positions.Clear` is in `OnDivisionChangedAsync` (`:745-748`, out of scope as stated). R4 — `OnAddReset` (`:709-717`) makes no API call, no submit, no close. Zero deleted assertions — the three test hunks are pure context+addition, 212+/0− genuine; 32 `[TestMethod]` = 26 pre-existing untouched + 6 new. Scope — the patch holds exactly the two expected files; the combined working-tree delta (15 files) equals the union of the three round patches with no strays.

Best-practices: no overwrites / skills honored (dialog-ui §2 separator untouched, no `.razor.css` change this round so §3's blanket-overwrite risk is moot, CSS isolation not engaged, `dotnet-best-practices` guards/`_disposed`/XML-docs/naming consistent with `OnAddTrigger`) / readable — violations as P1/P2 above.
```

---

## Acceptance

**Verdict: CLOSED.** Tier 2 — the parent adjudicates the REVIEW and transcribes the verdict; the parent's numbers are the only source of truth.

| # | Criterion | Result |
|---|---|---|
| H1 | Armed ⇒ trigger gone, `Reset` rendered | ✅ `Reset_ReplacesTheTrigger_WhenTheFormIsArmed` — asserts both directions |
| H2 | `Reset` clears positions, both dates, Reason, division | ✅ `Reset_ClearsTheChosenPart_AndReGatesTheForm` (asserts `Term` **before** → `Any date` after), `Reset_UnTicksTheChosenSequence_…` (pre-asserted tick then untick), `Reset_ClearsTheTypedRangeAndReason_WithoutWritingAnything` (range + Reason; its non-discriminating division clause was removed in the rework) |
| H3 | After `Reset` the controls are disabled and the trigger is back | ✅ `Reset_ClearsTheChosenPart_AndReGatesTheForm`, `Reset_ReplacesTheTrigger_…` |
| H4 | `Reset` writes nothing and the dialog stays open | ✅ `Reset_ClearsTheTypedRangeAndReason_WithoutWritingAnything` asserts no request reached the handler |
| H5 | Empty list ⇒ no `Reset` | ✅ `EmptyList_OffersNoReset` **plus** the rework's `ArmedForm_LosesItsReset_WhenTheLastRowIsRemoved`, which closes the reachable corner the reviewer found |
| H6 | Trigger and `Reset` never both in the DOM | ✅ asserted in H1 and H3 |
| H7 | One clear implementation for both paths | ✅ `Reset_AndTheSuccessPath_ShareTheOneClearImplementation`, tightened in the rework (`NotContain("_positions.Clear()")` on both caller segments) and **mutation-proved** to fail on a partial copy |
| H8 | Build 0 errors; Admin suite 0 failures | ✅ build **0 errors** (17 warnings, pre-existing); `Admin.Tests.Unit` **596 / 0**; `ArchitectureTests.Unit` **72 / 0** |

**Authoritative parent pass (final state):** `dotnet build SchoolCollab.slnx` **0 errors**; `Admin.Tests.Unit` **596 passed / 0 failed**; `ArchitectureTests.Unit` **72 passed / 0 failed**.

**Review path:** diff review #1 → **P1** (a reachable R5 corner) + 2 actionable P2s → the one permitted rework iteration → closed by **two mutation proofs + parent scope-check**.

---

## Residual

1. **H7 is still not a full-copy tripwire.** A caller that keeps `ClearAddForm()` and inlines only `_startDate = null` / `_endDate = null` / `_chosenDivision = AcademicYearDivision.None` would pass; those three are pinned behaviourally (H2/H3) rather than at source level. Narrowed further by the rework's `_positions.Clear()`/`_reason` checks.
2. **Two accepted P2s** — the `:1253-1256` preservation pins, and `:1266-1283` (an H5 negative guard that cannot fail pre-change, which is inherent to asserting an absence).
3. **`SubmitAsync` still guards on `CanAdd`, not `FormActive`** (carried from `eed-form-gating`): a *programmatic* submit on a gated form with a complete batch would still POST. Unreachable by click — the button renders `disabled` when gated.
4. **No UI-tester pass** — third round running at Tier 2 on a UI change, per the owner's explicit override. Whether the `Reset`/`+ Add exception` swap reads clearly is **not** machine-verified.
5. **The spec fold was the parent's**, not the worker's: the abort skipped plan step 6, so §5.1's clause was written by the parent at report time (skill step 8), then corrected after the rework — its inline predicate reference had briefly gone stale against the guarded code.
6. **P2 #2's deviation is accepted**: the preferred fix was structurally impossible, so the assertion was dropped and the reason recorded in the test's own doc comment rather than weakening the test's real discriminators.
