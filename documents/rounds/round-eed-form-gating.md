# Round — enrollment-exceptions dialog: gate the add form behind an action

**Provider: pi/clinepass — Tier 2 (light), owner override on a UI round** (worker `clinepass/cline-pass/deepseek-v4.1-flash`; reviewer `clinepass/cline-free/mimo-v2.6-flash`, owner-named). Continues branch `feat/enrollment-exception-dialog` and its uncommitted change set from round `enrollment-exception-dialog` (base `main` @ `e9df9240`).

> **Owner override recorded (Tier 2 on a UI round), second round running.** Same deviation as the parent round. **Consequence: no UI-tester pass** — the four requirements below are backed by bUnit assertions plus the parent's UI checklist, not by an adversarial hunt.

---

## 1. The owner's request

> "Form looks confusing with list of exceptions. clear and keep new entry fields disabled, unless triggered by action. where list is empty, entry fields to add exception should remain active. After an exception is added, entry fields must be cleared and returned back to disabled"

## 2. Settled decisions

| # | Decision |
|---|---|
| F1 | **One derived property, no state machine.** `FormActive => _exceptions is { Length: 0 } \|\| _addTriggered;` — `_addTriggered` is set by the trigger and reset to `false` on a successful add. "Empty list ⇒ active" is the *first* clause, so deleting the last row re-activates the form by construction rather than as a special case. **`_exceptions is null` (still loading) ⇒ NOT active**, so a loading dialog never offers an armed form. |
| F2 | **The trigger** is an outlined `+ Add exception` `FluentButton` in the add region's heading row, rendered **iff `_exceptions is { Length: > 0 } && !_addTriggered`** — i.e. only when there is a list to read and the form is still gated. It is therefore never simultaneously visible with an armed form, so it cannot compete with the footer's `SubmitText="Add exception"`. |
| F3 | **Controls grey in place, they do not collapse.** `Disabled="@(!FormActive)"` on Fill-from, both date pickers and Reason; the Position checkboxes get `Disabled="@(!FormActive \|\| !PositionIsDerivable(position))"` so v7's *unbacked-position* rule survives an **armed** form (a disabled-by-v7 box must stay disabled). |
| F4 | The footer submit becomes `SubmitDisabled="@(!FormActive \|\| !CanAdd)"` (D3's `SubmitText="Add exception"` and the "stay open after add" behaviour are unchanged). |
| F5 | **A successful add clears every entry field** — position ticks, both dates, Reason, **and** resets the division to its initial default — then sets `_addTriggered = false` so F1 re-derives the disabled state. *Rationale for clearing the division too: the instruction says the entry fields are cleared; keeping it would be a nicer repeat-entry flow, and is a one-line change if the owner wants it back.* |
| F6 | **The two regions are visually separated.** The add region gets a `border-top` separator (the repo's `dialog-ui` §2 convention — never a `FluentDivider`/`<hr>`) and a heading row that **stops repeating the subject name** (the subject is already the dialog scope title in `.section-title`). The list keeps the scope heading + the count badge. |

## 3. Expected files

- `src/Students/SchoolCollab.Students.Application/Components/Students/EnrollmentExceptionsDialog.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Students/EnrollmentExceptionsDialog.razor.css`
- `tests/SchoolCollab.Admin.Tests.Unit/EnrollmentExceptionsDialogTests.cs`
- `documents/specs/subject-period-exception-model.md` — a **small §5.1 amendment** recording the gating rule (v8 stays v8; this is a clause inside it)

**Nothing else.** In particular NOT `DialogShellFooter.razor`, NOT the three host pages, NOT the round doc.

## 4. Implementation steps

1. Add `_addTriggered` and the `FormActive` property (F1).
2. Add the trigger button to the add region's heading row and gate its render on F2.
3. Apply `Disabled` to every add control (F3) and to the footer submit (F4).
4. On a successful add, clear + reset every entry field and set `_addTriggered = false` (F5).
5. Restructure the add region's markup for F6 (separator + de-duplicated heading) and update `EnrollmentExceptionsDialog.razor.css` — **merge, never blanket-overwrite**.
6. **Update the existing tests, then add the new ones.** Every existing test that drives the add form against a **non-empty** fixture list must now arm the form first: add one shared helper (e.g. `ArmAddForm(cut)` — click the trigger if it is rendered) and call it in each affected test. Then add the new assertions for G1–G6.
7. Amend spec §5.1 with the gating rule.
8. `dotnet build SchoolCollab.slnx` (0 errors) then `dotnet test tests/SchoolCollab.Admin.Tests.Unit`.

## 5. Acceptance criteria

| # | Criterion | Discriminating check |
|---|---|---|
| G1 | With ≥1 exception listed, the add controls are **disabled** | new bUnit test asserting `Disabled` on Fill-from, Position, both date pickers, Reason |
| G2 | With an **empty** list, the add controls are **active** and no trigger is rendered | new bUnit test |
| G3 | The trigger **enables** the form and then **disappears** | new bUnit test (trigger present → click → controls enabled → trigger gone) |
| G4 | After a **successful add**: positions, dates and Reason are cleared, the division is back to its default, and the controls are **disabled** again | new bUnit test |
| G5 | With the form **armed**, an unbacked position is still disabled (v7 preserved) | existing `PositionsWithoutAPeriod_*` still passes **without** modification of its assertion |
| G6 | The add region carries a `border-top` separator and its heading no longer repeats the subject name | bUnit markup assertion + `grep` for `FluentDivider`/`<hr>` in the dialog → none |
| G7 | `dotnet build SchoolCollab.slnx` 0 errors; `Admin.Tests.Unit` 0 failures | worker + parent run |

## 6. Constraints

- Repo-scoped searches only (`src/`, `tests/`, `documents/`) — **never** `find /`.
- No new NuGet package; CPM — never a `Version` on a `PackageReference`.
- No API / endpoint / migration / contract change. No new feature flag.
- Honour `dialog-ui` (§2 the separator is a CSS `border-top`; §3 merge-not-overwrite on `.razor.css`), the CSS-isolation rule, and `dotnet-best-practices`.
- **Do not weaken an existing assertion to make it pass.** If a kept test now needs arming, add the helper call — do not delete the assertion. If it fails for a real reason, report the deviation.
- **Never edit this round doc.** Self-report to `documents/rounds/.eed-gating-worker-report.md` (scratch, untracked).
- Test-output rule: one `dotnet test tests/SchoolCollab.Admin.Tests.Unit 2>&1 | grep -E "^\s*failed|total:|failed:" | head -40` per read, at most 2 attempts.

## 7. Worker task spec

Implement §4 in order. Return ONLY:

```
WORKER REPORT
Changed files: <path list>
Build: <"0 errors" | "n errors" + one-line detail each>
Tests: <project: n passed, m failed | "not run" + why>
Tests armed via helper: <count + names of tests that needed ArmAddForm>
Deviations from plan: <none | one line each>
```

---

## Worker Report

Verbatim from worker `clinepass/cline-pass/deepseek-v4.1-flash` (the run completed; this is self-reported and is superseded by the parent's authoritative pass below).

```
WORKER REPORT
Changed files:
  src/Students/SchoolCollab.Students.Application/Components/Students/EnrollmentExceptionsDialog.razor
  src/Students/SchoolCollab.Students.Application/Components/Students/EnrollmentExceptionsDialog.razor.css
  tests/SchoolCollab.Admin.Tests.Unit/EnrollmentExceptionsDialogTests.cs
  documents/specs/subject-period-exception-model.md   (§5.1 amendment only — no version bump)

Build: 0 errors (`dotnet build SchoolCollab.slnx`, 84 warnings — all pre-existing, none from these files).

Tests: SchoolCollab.Admin.Tests.Unit — 588 passed, 0 failed.
       EnrollmentExceptionsDialogTests (filtered): 25 passed, 0 failed (18 existing + 7 new).

Tests armed via helper: 1 — FailedAdd_LeavesTheScopeUnchanged.
  (Every other test that drives the add form uses `exceptionsJson: "[]"`, i.e. an empty list, which
  is armed by construction — Add_Writes…, OpenEnds_ReadAs…, DuplicateRejected…, ChosenPart_*,
  Add_CarriesTheChosen…, Positions_*, PickersCheck…, ScopedEmptyState_…. The three tests seeding a
  NON-empty list — TermShapedAndPlainWindowRows, OpenBounds, RemovingTheLastException — never touch
  the add form. No assertion was weakened or deleted; G5's PositionsWithoutAPeriod_* is byte-identical.)

Deviations from plan:
  1. F6 leaves the heading WORDS open; chose "Add exception" (the old heading minus its subject
     clause) to reuse the action spelling the footer already uses — "New exception" is a one-liner
     if the owner prefers.
  2. One clause beyond F1–F6: the "Choose a position to add an exception." note is now also gated on
     FormActive, otherwise a gated group-owned form (whose single FR-56 division renders at once)
     tells the reader to choose a position under disabled checkboxes. Recorded in the spec clause.
  3. G1 is two tests, not one: v7 makes the range and the position ladder mutually exclusive, and one
     bUnit context cannot host two dialog providers — NonEmptyList_GatesTheFreeWindowFields_AndOffersTheTrigger
     (Fill from / both ends / Reason / footer) + NonEmptyList_GatesThePositionLadder (whole ladder
     disabled, including the v7-backed 1st).
  4. G4 likewise split — SuccessfulAdd_ClearsTheRangeAndReason_AndReGatesTheForm (grade) and
     SuccessfulAdd_ClearsTheTickedPositions_AndReGatesTheForm (group): F5's division reset takes the
     ladder off screen for a grade, so ticked positions are only observable on a group-owned render.
  5. G6 reads the separator at its SOURCE (`CssRuleFor(".add-panel")` must contain `border-top`) beside
     the bUnit markup assertions — a scoped-CSS border has no markup footprint and dialog-ui §3 warns
     the build cannot verify it (precedent: FormRowOrientationTests / EnrollStudentDialogFeatureFlagTests).
     The dialog renders no FluentDivider/<hr>; `grep -E "FluentDivider|<hr"` hits only the two
     prohibition comments.

Open risks (not silently patched):
- The trigger relies on FluentButton's default `ButtonType.Button` (confirmed in the 4.14.2 package XML docs) so it cannot submit the enclosing EditForm — behaviourally true today, not pinned by a test.
- F4 is expressed only at the footer: `SubmitAsync` still guards on `CanAdd`, not `FormActive`, so a programmatically fired submit (not a click — the button renders `disabled`) on a gated form with a complete batch would still POST. Outside F1–F6, so flagged, not changed.
- Keep-vs-clear of the division after a successful add (F5) is the one reversible product call; the UI-tester/owner may want it kept.
```

**Parent's authoritative pass** (the only source of truth for numbers): `dotnet build SchoolCollab.slnx` — **0 errors**; `Admin.Tests.Unit` — **588 passed / 0 failed**; `ArchitectureTests.Unit` — **72 passed / 0 failed**.

---

## Rework — Tier-2 iteration 1 (the one permitted)

The reviewer's P1 was an **acceptance-honesty defect in the test suite, not in the product**: G4's
"the division is back to its default" clause was asserted by no test that could fail.

| Item | Fix |
|---|---|
| **P1** | New test `SuccessfulAdd_ReturnsTheDivisionToItsDefault_AndLeavesNoTickedPosition` — grade owner: arm → `SelectDivisionAsync(Terms)` → `ChoosePositionAsync(1)` → submit → assert `DivisionLabel == "Any date"` **and** no ticked position. It asserts its own falsifying **precondition** inline — *"the part is OFF its default before the write — without that move the reset below is unobservable"*. |
| **Verification** | A **mutation proof**, run by the worker: removing `_chosenDivision = AcademicYearDivision.None;` (dialog `.razor:637`) made the NEW test **FAIL** — `Expected DivisionLabel(harness.Dialog) to be "Any date" … but "Term" has a length of 4` — while the two pre-existing G4 tests stayed **green** under the same mutation, reproducing the reviewer's vacuity finding exactly. The line was then restored **byte-exactly** (md5 `fec9a57f…` before == after) and no mutation residue survives. |
| **P2 ×2 (accepted, report-only)** | `:1071` G2 is a **preservation pin** — pre-change code already rendered "empty list ⇒ active, no trigger", so it cannot fail pre-change; retained deliberately, because F1's first clause is the rule it preserves. `:1215` G6's `CssRuleFor(".add-panel")` sub-assertions pre-date this round (the `border-top` shipped with the parent round); G6 stays backed by its **heading** assertions, which do fail pre-change. |

**Rework worker report** (`clinepass/cline-pass/deepseek-v4.1-flash`): changed files = `EnrollmentExceptionsDialogTests.cs` **only** (341 additions, **0 deletions**); build 0 errors; `Admin.Tests.Unit` **589 passed / 0 failed**; mutation check as above; leftover mutation diff after restore: **none**; deviations: none.

**Re-verification:** **parent scope-check** (sanctioned for tiny rework diffs) — the rework delta is test-only (0 deletions, no production file touched relative to the freeze), the reset line is intact at `:637`, and the mutation proof is a *stronger* discriminator than a static read would have been. No reviewer re-run.

---

## Review

Static reviewer `clinepass/cline-free/mimo-v2.6-flash` — read-only; never builds, never tests, never writes files.

### Diff review #1 — verdict **P1**

```
REVIEW
Verdict: P1
P1: tests/SchoolCollab.Admin.Tests.Unit/EnrollmentExceptionsDialogTests.cs:1148 — F5's division reset is asserted but unverifiable: neither G4 test ever moves the division off its default (no `SelectDivisionAsync` in either), so `DivisionLabel == "Any date"` (:1148) holds with or without the reset, and G4b's :1188 is vacuous too (a group's `_chosenDivision` also stays initial `None`; the getter falls back to Terms). Deleting `_chosenDivision = AcademicYearDivision.None;` (src/Students/SchoolCollab.Students.Application/Components/Students/EnrollmentExceptionsDialog.razor:637) passes both tests, so plan G4's clause "the division is back to its default" is backed by no assertion that can fail — the one F5 clause with zero discriminating coverage. Smallest fix: one grade-owner test — arm, `SelectDivisionAsync(Terms)`, `ChoosePositionAsync(1)`, successful submit, assert `DivisionLabel == "Any date"`.
P2: tests/SchoolCollab.Admin.Tests.Unit/EnrollmentExceptionsDialogTests.cs:1071 — G2 (`EmptyList_LeavesTheAddFormLive_AndOffersNoTrigger`) passes against the pre-change code (pre-change already rendered "empty ⇒ active, no trigger"), so it cannot fail pre-change; acceptable as a preservation pin for F1's first clause, but it is the one G-criterion test with no pre-change discriminating power.
P2: tests/SchoolCollab.Admin.Tests.Unit/EnrollmentExceptionsDialogTests.cs:1215 — G6's `CssRuleFor(".add-panel")` sub-assertions also pass pre-change (the `.add-panel` `border-top` pre-existed this round — parent patch `diffs-enrollment-exception-dialog.patch:2907`); G6 remains backed overall because its heading assertions (:1221–1222) fail pre-change ("Add exception for Mathematics"). Report-only.
Best-practices: no overwrites (round patch touches exactly the 4 expected files; the combined working delta adds no paths beyond the parent round's 15; untracked = round docs only) / skills honored (dialog-ui §2: `border-top` at EnrollmentExceptionsDialog.razor.css:102, zero `FluentDivider`/`<hr>` in the dialog; §3: CSS edit is a merge — zero rule deletions, every markup class still resolves; §4: exactly one `<EditForm>` at EnrollmentExceptionsDialog.razor:106; CSS-isolation and dotnet-best-practices observed) / readable
```

---

## Acceptance

**Verdict: CLOSED.** Tier 2 — the parent adjudicates the REVIEW and transcribes the verdict; the parent's numbers are the only source of truth.

| # | Criterion | Result |
|---|---|---|
| G1 | ≥1 exception ⇒ the add controls are **disabled** | ✅ `NonEmptyList_GatesTheFreeWindowFields_AndOffersTheTrigger` + `NonEmptyList_GatesThePositionLadder` (an honest split — v7 makes the free-window range and the position ladder mutually exclusive, so one bUnit render cannot show both) |
| G2 | Empty list ⇒ controls **active**, no trigger | ✅ `EmptyList_LeavesTheAddFormLive_AndOffersNoTrigger` — a preservation pin (P2 above) |
| G3 | The trigger **enables** the form then **disappears** | ✅ trigger test |
| G4 | After a **successful add** the fields are **cleared** and **disabled again** | ✅ `SuccessfulAdd_ClearsTheRangeAndReason_AndReGatesTheForm`, `SuccessfulAdd_ClearsTheTickedPositions_AndReGatesTheForm`, and — after the rework — `SuccessfulAdd_ReturnsTheDivisionToItsDefault_AndLeavesNoTickedPosition`, **the only one that fails without the reset** |
| G5 | v7's unbacked-position rule survives an **armed** form | ✅ `PositionsWithoutAPeriod_*` byte-identical and passing |
| G6 | A `border-top` separator + a heading that no longer repeats the subject | ✅ heading markup assertions (these do fail pre-change) + `CssRuleFor(".add-panel")` `border-top`; zero `FluentDivider`/`<hr>` in the dialog |
| G7 | Build 0 errors; Admin suite 0 failures | ✅ build **0 errors** (17 warnings, pre-existing); `Admin.Tests.Unit` **589 / 0**; `ArchitectureTests.Unit` **72 / 0** |

**Authoritative parent pass (final state):** `dotnet build SchoolCollab.slnx` 0 errors; `Admin.Tests.Unit` **589 passed / 0 failed**; `ArchitectureTests.Unit` **72 passed / 0 failed**.

**Review path:** diff review #1 → **P1** → the one permitted rework iteration → closed by **mutation proof + parent scope-check**.

---

## Residual

1. **F4 is enforced at the footer only.** `SubmitAsync` still guards on `CanAdd`, not `FormActive`, so a *programmatically* fired submit on a gated form with a complete batch would still POST. Practically unreachable — the submit button renders `disabled` when gated, so no user can click it — but the invariant is not enforced on the write path. Flagged by the worker, judged by the reviewer, left as **recorded debt** rather than expanding a light round.
2. **The trigger's non-submitting button type is not pinned by a test.** It rests on `FluentButton`'s default `ButtonType.Button` (confirmed against the 4.14.2 package XML docs). Behaviourally true today; a future `Type="Submit"` on that control would be caught by nothing.
3. **The division reset after a successful add is the one reversible product call.** F5 clears it (the literal reading of "entry fields must be cleared"); keeping it would make repeat entry one click shorter and is a one-line change. **Owner's call.**
4. **No UI-tester pass** — second round running at Tier 2 on a UI change, per the owner's explicit override. The gating is proven at the DOM level (which controls carry `disabled`, when the trigger renders, that the fields clear), but whether *disabling in place actually reads as clearer* — the very thing this round was asked to fix — is **not** machine-verified.
5. **The two accepted P2s** — G2's preservation pin, and G6's pre-existing CSS sub-assertion.
