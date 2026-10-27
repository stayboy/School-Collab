# Round — debt sweep (Q52–Q57)

Mode: attended · Tier 2 light (plan gate + worker + static reviewer; no UI-tester seat — owner Q48 posture carried forward)

**Settled by the owner (grill round, 2026-10-27):**

| # | Decision |
|---|---|
| Q52 | #416 is fixed as a **pattern**, not two spots: one friendly-error seam, every raw-`ex.Message` site routed through it, plus an **architecture guard** forbidding raw exception text in `.razor`. |
| Q53 | **#413 is split out** of this round — it changes what the API does with user input, and mixing it with four cleanups makes the diff unreviewable. |
| Q54 | #418: a **bounded cause hunt first** (one sitting), then sweep all five sibling sites with the same scoped fix and post the finding. If the hunt finds nothing, fall through to the defensive sweep and say so in the issue. |
| Q55 | Layer 2 stays **one round, both surfaces** — deferred entirely; not this round. |
| Q56 | #413's shape **when a numbering policy exists**: the Student Number field is **read-only**, with a **refresh button** beside it that fetches the **in-flight next number**, presented as **provisional/cautionary** ("reserved on save"). Where no policy exists, the field stays editable. |
| Q57 | The `orchestrator-worker-reviewer` skill correction rides **this round's doc layer**, never the code diff. |

**CORRECTION (owner challenge, 2026-10-27).** An earlier revision of this doc asserted *"there is no student-numbering policy or generator in `src/Students` and every `Sequence` belongs to `Period`"* and concluded Q56's condition had no antecedent. **That was wrong.** The numbering feature exists and is not named after students:

- **`SchoolCollab.Core/EntityCodes`** — the shared kernel's entity-code contract (`IEntityCodeGenerator`, `GenerateAsync` / `GenerateWithNameAsync`).
- **`Settings.Core/Services/EntityCodeGenerator.cs`** — the generator. Loads the active `EntityCodeRule`, advances every segment for the current period, and **persists** the per-segment sequence state under `xmin` optimistic concurrency with up to 3 retries.
- **`EntityCodeRule.GenerateNext`** (`Domain/EntityCodeRule.cs:150`) — mutates segment state in place; tenant format overrides are applied at render time only, so a shared blueprint's counter is never permanently rewritten by a tenant.
- **`EntityCodeSegment.Advance`** — the shared counter all tenants race on (per-tenant counters are an explicit v1 non-goal).
- Tests: `CreateStudentHandlerEntityCodeTests`; design doc: `docs/plans/2026-07-28-entity-code-auto-generation.md`.
- **The call sites** (found by the plan gate, not by me): `CreateStudentHandler.cs:31` and `CreateStudentWithLinkedDataHandler.cs:69` call `entityCodeGenerator.GenerateAsync("STUDENT_CODE")`; the rule is seeded at `EntityCodeRuleSeeder.cs:30`.

What is genuinely **absent** is a **peek/preview** operation and a next-number HTTP endpoint — the generator consumes, it does not look ahead. So #413 is *closer to renderable* than the original wording implied: the policy and the rule exist; only the preview surface is missing.

This is the repo's **override pattern for reference data** (AGENTS.md § Tenancy), not a student-side helper — which is why a search scoped to `src/Students` and seeded with student-shaped identifiers (`GenerateStudentNumber`, `NextStudentNumber`, `numbering`, `Sequence`) found nothing. The claim survived its own review precisely because it was *narrowly true* (`src/Students` does not contain the policy) while being *operationally false*.

**Consequence for Q56, now that the mechanism is known — this is a decision for #413's round, not this one.** `GenerateAsync` **consumes** a code: it advances and persists the counter, so a "refresh next number" button is a **write, not a read**. Every click burns a number, and abandoning the form wastes it. So #413 must pick one of:

1. **Preview without consuming** — compute the next code from current rule state without persisting. Cheap and side-effect-free, but it can be stale: two concurrent previews show the same number, and a save that generates would get a different one. Safe only if save re-generates and the preview is labelled approximate.
2. **Reserve (consume) and honour** — the refresh button calls `GenerateAsync` for real, the UI shows *"Reserved: STU-2026-0147 — used when you save"*, and create accepts that reserved code instead of generating a second. Truthful and race-free, at the cost of burning a number on every abandoned form.
3. **Reserve + release** — (2) plus a release path on cancel, which needs a new operation on the rule (and a decision about whether gaps in a numbering sequence are acceptable to schools at all — usually they are not).

The owner's "cautionary tail" wording points at (2) or (3); (1) is the only shape that needs no new machinery and the only one that can lie. **That is the question to put to the owner when #413 opens** — it cannot be answered from the code.

## Scope

| Item | Issue | What "done" means |
|---|---|---|
| P1 | #416 | One friendly-error seam; every `ex.Message`-in-`.razor` site uses it; a guard forbids regressions |
| P2 | #419 | bUnit coverage for `ActivityGroupPickerDialog` — blocked rows, selection gating |
| P3 | #418 | The five sibling `Single(c => c.Method == "POST")` sites hardened; the cause either found or the hunt recorded as bounded-and-unresolved |
| P4 | #414 | `FormRow`'s W9 "fill" step actually fills the input cell |
| P5 | Q57 | The skill correction, in the doc layer |

### Explicitly NOT in this round

| Deferred | Why |
|---|---|
| #413 Student Number discarded | Its own round (Q53). The numbering policy **exists** (`Settings` `EntityCodeRule` + `EntityCodeGenerator`) — Q56 is implementable today; what #413 must first settle is whether a preview button **consumes** a number, which is a design decision, not a missing prerequisite |
| Layer 2 — bands, kebab switch, enrol action | Owner Q55: one round, later |
| §7 rollover screen | Its own round, before the year boundary |
| #423 `/recipient-preview` over-count | Filed, belongs with the wizard |
| Any activity-group feature work | #416 is presentation only — the feature is always-on since #420 |

## Plan

### P1 — the friendly-error seam (#416) — **SCOPE OPEN, see Q58**

**Today:** raw `ex.Message` reaches a `FluentMessageBar` across the whole UI, not three pages. The plan gate counted **~180+ sites**: ~100+ in `Students.Application` (`ContactsEditor` ×7, `Students/Detail` ×15, `ActivityGroupDetails` ×6, `PeriodUpsert` ×7, `Periods` ×6, `SubPeriods` ×5, `GuardianDetail` ×5, plus dozens of dialogs), ~50 in `Assignments` (`AssignmentAuthoring` ×9, `Detail` ×10, `Index` ×10, `SignOff` ×8…), plus `Families`, `Settings` (`Tenants/Index` ×5), `Admin.Shared` (`LandingPage.razor:80`, `CodedValueDialog.razor:172`), `Mobile`, and `AiChat.razor:165,648`. The original "7 places across 3 pages" was measured on one page family and was wrong by an order of magnitude — the same class of error as the numbering-policy claim, made in the same hour.

There are **no `*.razor.cs` code-behind files** in the tree (gate N5), so a `.razor`-only scan covers the whole UI idiom. The only `.cs` uses are API route responses (`new { ex.Message }`) — a different concern, out of scope.

**The seam (unchanged):** a shared helper in `SchoolCollab.Admin.Shared` taking `(Exception, string plainSentence, ILogger)`, logging the detail and returning the sentence; a companion message-bar rendering it. Each site keeps its own wording, so the fix never flattens meaning into one generic message.

**The guard — detection rule, corrected by the gate.** The dominant shape is a C# assignment in `@code` (`_error = ex.Message;`, `$"Could not load: {ex.Message}"`), **not** `@ex.Message` in markup; and `@_error` is the **sink the fix keeps**, so flagging it either reddens the corrected code or is defeated by renaming `_error`→`_msg`. The guard must therefore detect the **source** — a `Message` read off a caught exception inside a `.razor` — with an allow-list for legitimate uses (validation summaries, provider text the UI is meant to show).

**Scope of the sweep and of the guard's file set — settled by the owner as Q58(a): THE RATCHET.**

- **Guard: repo-wide** over `src/**/*.razor` (precedent: `DropdownWidthFloorArchitectureTests` scans all `.razor` and is why the W2→W3 floor never regressed).
- **Allow-list: explicit and enumerated**, one entry per remaining site with its file and the reason it is not yet converted. Every unconverted site from the ~180 is listed; nothing is grandfathered by silence. New violations fail immediately.
- **First tranche converted now (4 surfaces):** `Teachers.razor`, `TeacherDetail.razor`, `GuardianSetupWizard.razor`, and `ActivityGroupPickerDialog.razor` — the #416 pair, the wizard's four sites, and the picker the gate added (N4).
- **The allow-list must shrink, never grow.** A test asserts the entry count is <= the number recorded in this round, so a new violation cannot be admitted by adding a line.

The three alternatives the agent rejected for the record: a full ~180-site sweep now (unreviewable, untested pages at risk), and a `Students.Application`-only guard (leaves `Assignments`/`Settings`/`Families`/`Mobile` with the bug).

1. The seam, above.
2. **Sweep** — the file set Q58 settles. Each converted site keeps its own wording.
3. **The guard**, with a negative control (a fixture containing the forbidden shape, asserted to fail) — the shape proven by `DropdownWidthFloorArchitectureTests`, which scans all `src/**/*.razor` and is therefore the precedent for a repo-wide ratchet.
4. **The empty-vs-failed distinction** on **four** surfaces, not three (gate N4): the three named pages **plus `ActivityGroupPickerDialog`**, where a failed load leaves `_options` empty and `_loading` cleared, so the `_options.Count == 0` branch renders "No activity groups exist yet" beside the raw error. P2 touches that file anyway.

### P2 — `ActivityGroupPickerDialog` bUnit test (#419)

The picker's behaviour was verified **live only** (blocked rows struck through, add→chip→remove). Bring it into the suite: a mocked activity-group client returning a mix of available and blocked groups; assert (a) blocked rows are not selectable, (b) their reason is in the visible text, (c) selecting an available row enables Add, (d) the dialog returns the selected ids.

### P3 — #418, the flake and its siblings

1. **Bounded hunt.** Reproduce `NotificationTemplateCreateTests.Save_PostsTheWholeDraft` failing in the **full** unit leg (it passes alone), and find why a second POST appears. Time-box: one sitting. Record whatever is learned either way on #418.
2. **Sweep the five siblings** — `EnrollmentExceptionsDialogTests.cs:648,796,853,944,975`. Gate N3: these are **already** preceded by `Contain(c => c.Method == "POST" && c.Url == $"{ExceptionsResource}/bulk")`, so the scoped `Single()` must add that **same URL predicate** — not `TemplateApiHandler.BasePath`, which is the wrong analogue (different handler, different test file).
3. **No blanket `SingleOrDefault()`.** The assertions must keep failing when the expected call is missing — a silent pass is worse than a flake. The failure message enumerates every observed POST so the next occurrence diagnoses itself.

### P4 — `FormRow` W9 fill (#414) — cause already known, gate N2

**The plan's own guess was wrong and is withdrawn.** `.form-row-input` **is** a flex container (`FormRow.razor.css:121` → `display: flex`). The real cause is documented in the tree: `flex: 1 1 0` (zero flex-basis) makes the width resolve from the flex algorithm instead of the percentage, so the field sizes to content. `src/SchoolCollab.Admin/wwwroot/css/app.css:73-78` already records exactly this and fixes it for `.w-9` with **`flex: 1 1 auto`** — but `FieldWidth.W9.ToCssStyle` (`FieldWidth.cs:122`) still emits `flex: 1 1 0`.

So P4 is **a one-line alignment, not a diagnosis**: make `FieldWidth.cs:122` agree with `.w-9`, then pin it with a test asserting the emitted style string and a bUnit/visual check that the cell fills. The doc comment on `W9` should carry the reason (zero basis resolves from the flex algorithm) so the next editor does not "tidy" it back.

### P5 — doc layer

- `.pi/skills/orchestrator-worker-reviewer/SKILL.md`: the acceptance seat is model-independent of the **worker**, shared with the reviewer **by design** (Q51). Already written, uncommitted; travels here, in its own commit.
- If P1's guard lands, `.github/copilot/rules/blazor-components.md` gains a pointer to it.

## Acceptance criteria

1. No `.razor` file in the swept set renders `Exception.Message`; the architecture guard passes and its negative control fails without the fix.
2. Each swept surface logs the exception with detail and shows a plain sentence — asserted by a test that fails if the exception text is rendered.
3. A failed activity-group load renders a failed state, never the empty state; test asserts the negative.
4. `ActivityGroupPickerDialog`: blocked rows unselectable, reason visible, selection enables Add, result returned — four assertions in the suite.
5. #418: all five sibling sites scope their `Single()` to the relevant handler path; the message enumerates observed calls.
6. #418: the cause is either identified with a discriminating test, or the bounded hunt is recorded on the issue with what was ruled out.
7. `FormRow` W9 fills its input cell across a full-width row; a test pins it; the failure mode is diagnosed, not guessed.
8. The skill correction is committed in the doc layer, not the code diff.
9. `dotnet build SchoolCollab.slnx` — 0 errors.
10. `scripts/round/gates.sh --no-maui --no-integration` (baseline) — 0 failures, every skipped leg named. Escalate to no-flags only if the diff touches shared chrome or auth.

## Not run

| Leg | Why |
|---|---|
| OpenRouter live tests (`OpenRouterClient_UsesApiKey_AuthenticatesSuccessfully`, `OpenRouterProvider_WithGoogleGemma4_RespondsToSimplePrompt`) | The only two failures in the integration leg, demoted by `gates.sh`'s own allow-list: CI self-skips them for lack of a key, and this machine's provider answers HTTP 400. Named because a skipped leg is recorded, never implied. |

Nothing else was skipped: `gates.sh` ran with **no flags** — no `--no-maui`, no `--no-integration`, no `--no-portals`.

## Acceptance

**Verdict: ACCEPTED.** Signed by the plan's author (parent) on the acceptance seat's criterion-by-criterion verification,
the diff reviewer's re-review, and the parent's own gate run. The only criterion outstanding at the review's first
pass was 10, and it is now satisfied.

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | No swept `.razor` renders `Exception.Message`; guard passes with a failing negative control | **met** | zero `.Message` reads in the four converted surfaces; `ActivityGroups` keeps only the line-290 *wording* read; `Detector_FlagsEveryShippedShapeOfTheDefect` (4 shapes) + `Detector_IgnoresTheSeamAndTheErrorFieldItRenders` |
| 2 | Each swept surface logs the detail and shows a plain sentence | **met** | `FriendlyErrorSeamTests.Describe_ReturnsOnlyThePlainSentence` + page-level tests on `ActivityGroups`, picker, `TeacherDetail`; `Teachers`/`GuardianSetupWizard` rest on the guard (#425) |
| 3 | A failed activity-group load never renders the empty state | **met** | `NotContain("No activity groups yet.")` (page) and `NotContain("No activity groups exist yet")` (picker) |
| 4 | Picker: blocked unselectable, reason visible, selection enables Add, ids returned | **met** | `BlockedRows_AreNotSelectable_AndCarryTheirReason`, `SelectingAnAvailableRow_EnablesAdd`, `Submitting_ReturnsTheSelectedIds` |
| 5 | The five #418 siblings scope `Single()` to the handler URL, message enumerates POSTs | **met** | `TheBulkPost` at :673/:821/:878/:969/:1000, no `SingleOrDefault` |
| 6 | #418 cause identified with a discriminating test, or the bounded hunt recorded | **met** | cause found and pinned: `DraftEdits_PostTheLivePreview_SoTheCreateCannotBeFoundByMethodAlone` |
| 7 | `W9` fills its cell, test pins it, failure diagnosed not guessed | **met** (mechanism, not measurement) | `flex:1 1 auto` aligned with `.w-9`; `FieldWidthW9FillTests` pins the style string. bUnit cannot measure flex layout — see note 3 |
| 8 | Skill correction in the doc layer, not the code diff | **met** | `protected-doc-reviews.json`, ACCEPTED by `deepseek-v4-pro:0813` |
| 9 | Build 0 errors | **met** | parent: Release `-m:1`, 0 errors |
| 10 | Escalated gate, every skipped leg named | **met** | `gates.sh` **no flags, exit 0**; unit 4608/4608 ×2, MAUI 0 errors, Portals 88, AuthPortal 123, CrossModuleWiring green; the OpenRouter pair named above |

**Accepted notes (P2, none blocking)**

1. **The ledger's line numbers are advisory.** `LandingPage.razor:80` is recorded but the site now sits at `:89` after the F1 branch shifted it; the guard matches per *(file, ordinal)* so this passes. Recorded on #424 with the decision to settle: bump entries when sites move, or stop implying the line is enforced.
2. **`LandingPage.razor:89` still renders `@ex.Message`** in the shared wrapper's own `<ErrorContent>` — allow-listed, in the very file the F1 fix touched. Widest blast radius in the ledger (every consumer inherits it); filed as **tranche 0** on #424 rather than welded onto the rework.
3. **Criterion 7 is met by mechanism, not by a rendered-width observation.** `FieldWidthW9FillTests` pins the emitted style and its agreement with `.w-9`; bUnit cannot measure flex layout, so "the cell fills" is asserted at the mechanism the browser obeys. Stated rather than glossed.
4. **Two guard blind spots stand**: implicit `@context.Message` and an exception held in a field. Both are known shapes of the source-detector; all 34 existing `ErrorContent` sites use `Context="ex"`, so following convention stays caught.
5. **Per-(file,ordinal) matching means a same-file net-zero swap is invisible.** The never-grow cap still binds. Accepted as the cost of a ratchet that does not redden on unrelated edits to ~120 files.

## Rework (F1) — the diff review's one blocking finding

**Verdict on the first diff review: `CHANGES REQUIRED`, one blocking finding.** Everything else (seam contract,
guard, ledger teeth, #418 diagnosis, W9) was certified correct against source, not the patch.

**F1 — the failed-vs-empty claim was true for three surfaces, false for the fourth.** `Teachers.razor` sets
`_items = []` **and** `_error` on a failed load, and `LandingPage` rendered the `Items.Length == 0` empty branch
independently of the error bar — so the user saw *"No teachers yet. Create one to get started."* beside the
error. That is #416 itself, surviving in the one page with no bUnit harness (#425). The fix went into the
**shared component**, not the page, because that is where the coexistence bug lives and because
`LandingPageTests` exists while `Teachers` has no harness.

**The collision the fix exposed, and the owner-of-the-round's ruling (A+).** Suppressing the empty branch
broke a test from PR #194 on `ActivityGroups.razor` — the one page of 15 that reuses `EmptyMessage` as its
*failure* sentence. Reading that test decided the ruling: it asserted `Contain("404")` as
*"AC-1a: the rendered error text includes the failure (HttpRequestException message)"* — the test **pinned the
raw HTTP text as user-facing copy**, which is why the ledger carried `ActivityGroups.razor:164` as
`_error = ex.Message` and nobody looked again. Options weighed and rejected: an opt-out `bool` on the shared
component (preserves a redundant double message), a page-local fix in `Teachers` (untestable, leaves the
footgun for 13 pages). **Chosen (A+):** keep the shared suppression **and** convert `ActivityGroups` to the
seam, moving *"Could not load activity groups."* from the empty bar into the error bar as a plain sentence —
the page keeps its wording, the user stops seeing HTTP status text, and the assertion inverts **deliberately**
with a comment saying PR #194 pinned the leak.

**A flake the rework introduced, and fixed rather than residualised.** The new picker test asserted
`NotContain("500")` against the **whole** bUnit markup — every `blazor:elementReference` is a random 32-hex
GUID, and a GUID contains `"500"` often enough to redden roughly one run in five (observed 2 of 13). The
contract was right; the assertion's **blast radius** was wrong. Both negatives now read the error bar's
`TextContent`, anchored by a **positive** assertion first so a selector hitting the wrong bar cannot satisfy
the negatives while asserting nothing.

**Ledger arithmetic, stated so it need not be reverse-engineered:** 301 − **4** deleted (`ActivityGroups.razor`
164, 254, 283, 285) + **1** surviving (`:290`, which reads `ex.Message.Contains("referenced")` only to pick
**wording** — reading is legitimate, displaying is not) = **298**. The ledger is now shrinkable, not merely
capped; #424 is the burn-down.

### Rework evidence (parent's own runs, serial builds — the worker died of the documented `0xC0000409` OOM crash mid-verification, so the numbers below are the parent's)

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx -c Release -m:1` | **0 errors** |
| Full unit leg ×2 | **4608 / 4608**, 0 failed, 0 skipped — both runs |
| `ActivityGroupPickerDialogTests` loop | **10 / 10** consecutive green |
| F1 test teeth | `EmptyItems_WithError_RendersErrorSentence_And_Not_EmptyMessage` fails without the suppression branch |
| **Scoped-assertion teeth** | injected `_loadError = ex.Message`, rebuilt, `FailedLoad_SaysItCouldNotBeRead` **failed** (`total: 1, failed: 1`); restored → green |

The first teeth attempt was invalid and is recorded as such: it used `--no-build`, so the injected defect never
compiled, and a PowerShell multi-line literal silently no-matched. A teeth check that cannot fail its own test
is the defect it was meant to detect.
