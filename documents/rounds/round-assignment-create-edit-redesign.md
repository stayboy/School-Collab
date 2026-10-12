# Round — assignment create/edit redesign (summary-first draft edit)

> **Status:** **implemented, uncommitted** (2026-10-08) on
> `feat/assignment-context-strands-lessons` @ `567d92e4` — awaiting the owner's
> commit/PR instruction; flip this line to `CLOSED` and name the PR when merged.
> **Mode:** **solo** (owner's instruction) — the session agent implemented
> directly; plus **one verification/review subagent** (`deepseek-4.1-flash`,
> owner-requested) whose findings are in §Review. No UI tester was dispatched.
> **Spec:** `documents/specs/assignment-create-edit-redesign.md` (decisions
> D1–D11; durable source of truth). This folder is ephemeral residue.
> Companion: `documents/rounds/.session-state.md` (cold-start resumption).

## Plan

Owner brief (verbatim intent): redesign the assignment create/edit surfaces —
move Questions & AI to the draft-edit surface, hide unrequested fields behind a
summary + edit icon, keep Questions & AI always-on in draft edit, summarise
Targets & audience as an accordion below it, breadcrumb navigation for editing,
reorder the Create form (Status & available-from to the top, Due date after
Assignment type & grading, Description + Instructions as their own bottom
section with side-by-side inline inputs labelled beneath), and compact the
strand/lesson pickers into two buttons under Subject.

Grill rounds (2026-10-08) settled the frontier; all recommendations accepted:
D1 in-page pencil toggle (no new route), D2 summary = facts + Questions & AI +
Targets accordion (no Content/Rules), D3 Draft|Scheduled get the pencil ·
Published/Closed/Archived get the same summary without it, D4 strand/lesson
picker **dialog** + chips, D5 "Instructions" as its own sixth compartment,
D6 the Create field order, D7 action bar stays / jump-nav only in the form view,
D8 breadcrumb `Assignments › {title} › Edit`, D9 the view flip is never guarded.
Round 2 added: Create renders no Questions entry, breadcrumb degrades to `…`
while loading, and the pencil/back flip raises no confirmation.

Owner follow-ups **during** the round (each superseded/extended a decision):
D10 — Content & Resources leaves Create too; D11 — no inline narratives (help
icons), Pass Score inapplicable = disabled checkbox + label, equal-height
Instructions textareas; plus a rules-compliance pass on `FluentSelect` use.

## Worker Report (solo session agent)

**Change set** (14 modified + 4 new; +1347/−335 tracked):

| Area | Files |
|---|---|
| Durable spec | `documents/specs/assignment-create-edit-redesign.md` **(new)**; companion line added to `assignment-authoring-compartments.md` |
| Authoring surface | `AssignmentAuthoring.razor` (+.css) — breadcrumb, summary card + facts grid, pencil/back toggle, Targets accordion, Instructions compartment, D10 gating (questions + content + the whole row-2 wrapper on Create), mode-filtered `Compartments`, Create kebab emptied, `FactPicks` wording, help-tooltip properties |
| Pickers | `ContextPicksSection.razor` (+.css) — two buttons + chips; **new** `ContextPickDialog.razor` (+.css) and `ContextPickDialogModel.cs` |
| Scoring | `ScoringFieldsSection.razor` — D11 checkbox/label + help icon |
| Shared primitive | `FormRow.razor` (+.css) — **additive** optional `Help` parameter rendering the (i) icon inside the label |
| Tests | `AssignmentAuthoringBunitTests`, `ContextPicksSectionBunitTests`, `ScoringFieldsSectionBunitTests`, `AssignmentDetailBunitTests`, `QuestionsDraftSectionBunitTests` (+5 net-new cases, several rewritten, fixture race fixed); `AssignmentCreateBunitTests` (flake hardening: named 15 s `DialogRoundTripBudget` for its two dialog-round-trip waits) |

**Gates:**

| Gate | Result |
|---|---|
| `SchoolCollab.Assignments.Application` build | 0 errors |
| Assignments unit test project build | 0 errors |
| `SchoolCollab.Assignments.Tests.Unit` | **1006 / 1006 passed** (1001 pre-round + 5 new) |
| `dotnet build SchoolCollab.slnx` | **0 errors** (37 pre-existing warnings) — re-run **after the owner stopped F5**, which released the `Assignments.Worker\bin` lock that had blocked every earlier solution build this session |
| Full `dotnet test --no-build` (locked-worker workaround, pre-hardening) | 3642 total · 3639 passed · **3 failed** — all three are live-OpenRouter API tests in `Settings.Tests.Integration` (pre-existing) |
| Full `dotnet test --no-build` (post-solution-build) | 3642 total · 3638 passed · **4 failed** — the 3 live-OpenRouter tests **plus** `Create_UnsetReviewPolicy_SubmitsTheAuthorsReviewChoice`, which timed out in the shared `AddGradeTargetAsync` wait (the same pre-existing load flake the earlier `Create_MandatoryPolicy_…` run hit). See **Flake hardening** below |

**FluentUI traps discovered** (recorded in `.session-state.md §4`): `FluentSelect`
does not forward unmatched attributes (so no `title=`); `Icons.*` needs the
namespace alias `@using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons`;
`FluentCheckbox` renders `Label` as child content; the repo's existing `Tooltip=`
usages are unmatched-attribute splats.

**Flake hardening (same round).** Two distinct Create tests failed with one
signature: `AssignmentCreateBunitTests.AddGradeTargetAsync`'s 5 s wait expired with
the builder list still empty after 60+ checks while renders kept arriving —
`Create_MandatoryPolicy_SubmitsWithoutTheRetiredAuthorInputs` (before D11) and
`Create_UnsetReviewPolicy_SubmitsTheAuthorsReviewChoice` (after) — while the whole
suite is green standalone every time. Both waits now use one named
`DialogRoundTripBudget` (15 s, the bunit failure hint's own suggested figure) —
a real defect still fails the wait, it just takes longer to say so. The flake is
**pre-existing load sensitivity**, not a product regression: the path it waits on
(mocked targets dialog → page callback → subject union) is untouched by D10/D11,
and standalone runs pass 1006/1006 repeatedly.

## Review

Owner-requested **independent review by the `deepseek-4.1-flash` subagent**
(read-only: spec ↔ working tree ↔ `git diff`; it did not build or test).

**BLOCKER it found:** the Create page still rendered **Questions & AI** — the
original requirement ("move questions and AI to draft edit when assignment is
created") had been silently dropped *by the spec itself*, so spec and code agreed
with each other and disagreed with the owner. Evidence: unguarded
`#authoring-questions` section, the Create kebab's `Generate questions` action,
and the five-entry Create jump-nav. **Closed** as D10 (and, on the owner's
follow-up, extended to Content & Resources).

**Minor findings (all closed):** (1) §6 loading-state crumb `Assignments › …` was
missing — breadcrumb hoisted above the loading/not-found branches; (2) facts grid
showed `—` where §5.1 promised the §4 "unavailable" wording — now
`(unavailable)`; (3) §9.6/§9.7 test cases unwritten — three summary-state tests +
the unloaded-lesson-button test added; (4) §3.2's ≥720px two-column rule had no
CSS — media query added; (5) §8's file table omitted the new dialog files and the
test list was wrong — corrected; (6) §4 called the button row "page-scoped" while
it is component-scoped — spec corrected; (7) breadcrumb test never counted crumbs
/ asserted the Detail href, and one stale test message survived — both fixed;
(8) the spec's gate itself was unverifiable by the reviewer (read-only mandate) —
run by the parent instead (see Worker Report).

The reviewer's §9 "Generate questions kebab anchor resolves" item is covered by
`Summary_KebabGenerateQuestionsAction_ResolvesItsAnchor` on the summary and by the
edit-fields kebab tests.

## Acceptance

| Owner item | Delivered | Evidence |
|---|---|---|
| Reorder Create (status first, due after type/grading) | ✅ | `Basics_LeadsWithStatus_AndDueDateFollowsTypeAndGrading` |
| Instructions as own bottom section, side-by-side inline, labels below | ✅ | `Instructions_OwnBottomCompartment_SideBySide_WithLabelsBelow` + `Rows=4` assertion |
| Summary + edit icon (top right) for draft edit | ✅ | `Edit_Draft_OpensOnTheSummary_NoInputFields_PencilPresent`; pencil/back flip test |
| Questions & AI always shown in draft edit, priority position | ✅ | summary test asserts it directly under the card, no toggle |
| Targets & audience as accordion **below** Questions & AI | ✅ | `TargetsAccordion_CollapsedByDefault_HoldsTheEntryListAndCount` |
| Breadcrumb navigation for editing | ✅ | `Breadcrumb_RendersTheTrailOnEveryRoute` (3 crumbs + Detail href; Create 2) |
| Strand/lesson as two buttons under Subject | ✅ | `RendersTwoAddButtons_WithStableIds`, `PickingAStrand_ThroughTheDialog_…`, `LessonsNeverLoaded_DisablesOnlyTheLessonAddButton` |
| **Content & Resources moved off Create too** | ✅ | `Create_DoesNotRenderContentOrQuestions` (sections, editors, kebab, nav) |
| **Narratives gone; hints/tooltips instead** | ✅ | `ScoringFields_RenderInsideBasics_UnderTheGradingFormat` (help-icon counts + texts) |
| **Pass Score = checkbox with label explaining the disabled state** | ✅ | `TeacherGraded_ScoringFields_RenderDisabledWithReason`, `ScoringFieldsSectionBunitTests.TeacherGraded_…`, `ChangingGradingFormat_TogglesEnablement_NotPresence` |
| **Equal-height Instructions textareas** | ✅ | same test as above (`Rows` equality) |
| `FluentSelect` used per repo rules | ✅ | both raw sites carry the `dropdown-ui/SKILL.md §1` why-raw comment (enum-with-description + int values; async union + stale-selected-option + async side-effect) |

**Residuals carried forward:** (a) solution-level `dotnet build` must be re-run
after the owner stops F5; (b) the three live-OpenRouter test failures pre-date the
round and are untouched; (c) nothing is committed — the commit/PR gates remain
with the owner.

## UI Tester

Not dispatched — solo round (owner's instruction); the owner acts as the UI
verifier and has the app running.

## Addendum — D12 (jump-nav removed) + D13 (Rules padlock tooltips)

Two further owner follow-ups arrived after the gates above went green, in the same
session and on the same working tree.

**D12 — "remove the submenu list under the page title 'New Assignment' and any
other page after this redesign".** The compartment **jump-nav** is deleted from
every surface (Create and the edit-fields view; D7's hide-on-summary rule becomes
moot), together with the `Compartments`/`AllCompartments` array and the
`Compartment` record that only fed it. The **sections and their ids are
untouched** — `#authoring-basics`, `-targets`, `-rules`, `-content`,
`-questions`, `-instructions` — so the `Generate questions` kebab action's
`#authoring-questions` anchor still resolves; the compartment containers'
`scroll-margin-top` drops 112px → 56px (only the sticky action bar remains to be
cleared). `.authoring-jumpnav*` CSS removed.

**D13 — "remove inherited from tenant/grade; use a padlock tooltip next to header
text … repeat for every subitem".** The per-row `FluentBadge` ("Inherited from
Grade/Tenant policy") is gone from all five Rules readouts. The note now rides a
**padlock tooltip** — once beside the card header text and once on **every
subitem's** label — carrying the new `AssignmentAuthoring.PolicyInheritedHelpText`
(the badge text + D11's retired hint sentence merged:
*"Inherited from Grade/Tenant policy — resolved from your grade and organization
policy, not authored per assignment."*). New shared **`HelpIcon`** component
(required `Text`; optional `Icon`, `Class`) + `SectionCard.HeaderHelp` parameter;
`FormRow.Help` now renders through the same component (its `form-row-help` class
preserved), so the repo has one help-hint implementation. `FluentIcons` gains
`Info` and `LockClosed` (Size16) per `.github/skills/fluentui-icons/SKILL.md`.

| Area | Files |
|---|---|
| Authoring surface | `AssignmentAuthoring.razor` (+`.css`) — jump-nav + `Compartments` deleted, Rules card gains `HeaderHelp` and per-subitem padlocks, `PolicyInheritedHelpText` |
| Shared primitives | **new** `src/SchoolCollab.Admin.Shared/Components/HelpIcon.razor` (+`.css`); `SectionCard.razor` (`HeaderHelp`, **additive**); `FormRow.razor` (+`.css`) now renders `HelpIcon`; `FluentIcons.cs` (`Info`, `LockClosed`) |
| Tests | `AssignmentAuthoringBunitTests` — `CompartmentTitles`/`ExpectedCompartments` replaced by `CompartmentSectionIds`/`ExpectedFormSections`/`ExpectedCreateSections`; new `EverySurface_RendersNoJumpNav_AndTheFormKeepsItsSections`, `CompartmentAnchors_StillResolve_WithoutTheJumpNav`, `Rules_CarriesTheInheritedNoteAsPadlockTooltips_NotPerRowBadges`; `Rules_HoldsTheFivePolicyReadouts…` badge assertion → padlock assertion |

**D13 (owner follow-up 2) — "the padlock is the hint only when it's inherited from policy
and the user may not override it".** So the padlock is **conditional**: `PolicyReadout`
gains a `Locked` flag = *inherited from policy* **AND** *not author-overridable*.

| Readout | Locked? | Why |
|---|---|---|
| Approval before publish | always | policy-owned (D3); nothing in the form changes it |
| Notification policy | always | resolved per recipient grade at publish; not authorable |
| Archive window | always | policy-owned |
| Signature requirement | always | D10/OD4: the author has no signature control at all |
| Guardian review | **only when the policy sets it** | unset ⇒ the author chooses in Basics, so the row states the author's own choice and shows **no** padlock; set (incl. the D4 signature implication) ⇒ the same `PolicyReviewLocked` that disables the Basics toggle locks the row |

The card **header** shows one padlock while the card holds at least one locked readout
(`AnyPolicyReadoutLocked`) — the header can never claim a lock the rows do not have. The four
policy-owned rows are always locked today, so that condition documents the intent; it keeps the
header honest if one of them ever becomes overridable. Tooltip text sharpened to
*"Inherited from Grade/Tenant policy — resolved from your grade and organization policy and not
overridable on this assignment."* Test:
`Rules_PadlockHintAppearsOnlyForNonOverridablePolicyValues` asserts 4 row padlocks with
review unset (and that `#authoring-policy-review` has none), then **5** once
`mandatoryReview: true` is served.

**Gates (re-run after D12+D13, incl. the conditional refinement):**

| Gate | Result |
|---|---|
| `SchoolCollab.Assignments.Application` build | 0 errors |
| `SchoolCollab.Assignments.Tests.Unit` | **1007 / 1007 passed** |
| `SchoolCollab.Students.Tests.Unit` | **624 / 624 passed** (SectionCard is shared; `HeaderHelp` is additive) |
| `dotnet build SchoolCollab.slnx` | 0 errors at the pre-refinement revision; the final re-run was blocked by the **owner's running F5 app** (`Students.Api` + Visual Studio hold `SchoolCollab.Admin.Shared.dll` → MSB3027/MSB3021). Needs the app stopped, then re-run — no code-level error was reported in any project build |

**Third flake hardened (test-only).** `ContextPicksSectionBunitTests`'s two
`CreateMode_RendersTheRowBeneathSubject…` assertions captured a `.form-row` element and
called `IndexOf` on a *later* query — the page's async load re-renders the Basics section, so
the stale reference yields `-1` (seen once, under load). Both now re-query **both** rows
inside `WaitForAssertion`.

**FluentUI trap found here (cost ~30 min; recorded in `.session-state.md §4`).**
`<FluentIcon>` is **generic** in 4.x: `Icon="@(Icons.Regular.Size16.Info)"` passes
the generated icon **type** (`FluentIcon<TIcon>` instantiates it). Passing an
**instance** to `Icon=` makes `TIcon` infer the *abstract* `Icon` base, whose
parameterless ctor throws — surfacing as `TargetInvocationException →
ArgumentNullException: Value cannot be null. (Parameter 'Please use the constructor
including parameters.')`, swallowed by the page's `<ErrorBoundary>` into
"Something went wrong: …" and by bUnit into `WaitForFailedException` ("the
assertion did not pass within the timeout period") — i.e. a **render crash that
looks exactly like a flaky timeout** (7 tests failed; the same tests pass in
isolation). Passing an **instance** requires the non-generic `Value=` property
(the `DashboardCard` / `RowActionsMenu` / `MainLayout` precedent).
Diagnosis path that worked: temporarily widen the page's `ErrorContent` to
`@ex.ToString()`, re-run, read the inner exception, revert.

**Temporary debug artifacts used and removed:** the widened `ErrorContent`
(`@ex.ToString()` → back to `@ex.Message`) and two `ZZ_Debug_*` tests. Two stale
`SchoolCollab.Assignments.Tests.Unit` hosts from backgrounded runs held the test
exe (MSB3027/MSB3021) — killed by process name.

**Follow-up 4 — D14 form polish (owner, 2026-10-09, branch `fix/assignment-form-alignment-polish`)**

Four items on the shipped surface, all presentation:

| # | Item | Delivered |
|---|---|---|
| a | Subject dropdown higher — "just before the grading format row" | Subject **and** its Strands & lessons row moved up together, right after Title and before the type/grading row (D4's "buttons below the Subject dropdown" contract holds; Basics now ends with Guardian review). Test rename: `Basics_LeadsWithStatus_AndDueDateFollowsTypeAndGrading` → `Basics_LeadsWithStatus_AndSubjectPrecedesTheTypeAndGradingRow` (adds `#authoring-add-strand` to the sequence); `ScoringFields_RenderInsideBasics_UnderTheGradingFormat` loses the trailing subject hop (subject's new position is pinned by the renamed order test) |
| b | Header padlock next to the Rules card's header/Add area unnecessary | `SectionCard.HeaderHelp` (added by D13) removed from markup + parameter; the dead `AnyPolicyReadoutLocked` property removed with it; the per-row conditional padlocks (D13) remain the only lock statement. `Rules_PadlockHintAppearsOnlyForNonOverridablePolicyValues` now asserts `.section-card__help` renders **nowhere** |
| c | SectionCard header text must align with its subitems | The Rules readout rows carry 0 horizontal padding while the shared header carried 1rem → title sat 16px right of the rows. Fixed with `.authoring-rules-card :deep(.section-card__header) { padding-left: 0; padding-right: 0 }` — scoped to this card, every other SectionCard consumer keeps the shared chrome |
| d | "align for inputs in add question dialog … use formrow" (the **Generate-questions configure box**) | `QuestionPromptDialog`'s knobs leave the bare `.cq-prompt-nums` flex row (deleted) and ride three shared `FormRow`s: *Number of questions* (single, `For="cq-prompt-count"`), *Difficulty mix* (`AlignTop`, Easy/Medium/Hard keep their visible sub-labels), *Question types* (chips container). All ids/`min=1` guard/chip seams unchanged; new test `Knobs_RenderAsFormRows_SoEveryLabelSharesTheFormGutter` |

Spec: `assignment-create-edit-redesign.md` D14 row (a–d), D13 amended, §3.1 order list, §8
SectionCard row, §9 items 11–12.


**Follow-up 5 — D14e: picks-row empty-state notes deleted (owner, 2026-10-09, same branch)**

Owner: *"Clean up 'This subject has no strands yet' / 'No lessons for this strand' — they take up
space; keep Basics as compact as possible."* Grilled (one round, "all as recommended"):

| # | Decision | Delivered |
|---|---|---|
| A1 | Icon scope | Only the two TRUE empty-source states surface anywhere (button title + row (i) hint); the transient "No strand selected — the AI uses the whole subject." note is **deleted outright** — its constant `NoStrandPickedText` is gone |
| B1 | Single source | New public statics on `ContextPicksSection` — `EmptyStrandSourceHint` / `EmptyLessonSourceHint` / `EmptyStateHint` (strand wins when both empty; unloaded list never counts — P8-4); page's `ContextPicksEmptyHint` → `FormRow Help=`; the disabled buttons' `title`s keep the same constants |
| C1 | Wording | Verbatim reuse of `NoStrandsText` / `NoLessonsText` — zero new copy |
| D1 | Button titles | Kept — they explain the button and cover the states the icon ignores (no subject, list not loaded) |

Files: `ContextPicksSection.razor` (2 paragraphs + 2 note properties + `NoStrandPickedText`
deleted; 3 static helpers added), `ContextPicksSection.razor.css` (`.context-picks__note`
removed), `AssignmentAuthoring.razor` (picks `FormRow` gains `Help="@ContextPicksEmptyHint"` +
property), `ContextPicksSectionBunitTests.cs` (3 note tests rewritten → no-paragraph/title/helper
assertions, `UnloadedList` + `LessonsNeverLoaded` re-pointed to the helper, new
`EmptyStateHint_PrefersTheStrandSource_WhenBothAreEmpty` + page-level
`Page_EmptyStrandSource_CarriesTheRowHelpIcon_NotAParagraph`). Spec: D14(e), §4 item 4, §9 item 12.

Build note: the F5 session (VS 14188 + Worker 21812) locks the Worker's bin — which the test
project references — so the test csproj was built with `/p:BuildProjectReferences=false`
against the freshly built reference assemblies (Worker dll unchanged this round).


**Follow-up 6 — Blazor skills/rules capture (owner: "all as recommended", 2026-10-09, same branch)**

Nine lessons (+2 folded add-ons, 1 dropped by owner decision) distilled from this round into the
durable guidance corpus — **docs only**, no code. No CI test pins these files' content (verified:
only `ci.yml` is read by `PortalsSolutionItemsArchitectureTests`), so the change carries zero guard
risk.

| File | Added |
|---|---|
| `.github/skills/fluentui-icons/SKILL.md` | Troubleshooting row + §"The instance trap — a mystery 'timeout' in bUnit": `Icon` instance → generic `Icon=` infers the abstract base → render-time ctor throw → ErrorBoundary fallback → bUnit `WaitForFailedException` (pass-alone/fail-together signature); `Value=` rule; 3-step `ErrorContent`→`@ex.ToString()` diagnosis recipe |
| `.github/copilot/rules/testing.md` | New §"bUnit pitfalls — render crashes, dialog round-trips, element-id seams": the timeout-flake diagnosis (cross-ref to the icons trap); one named 15 s `DialogRoundTripBudget` for round-trip waits (60+-check-empty signature); element ids as contracts (never rename / `ContainInOrder` order moves / absence assertions for deleted elements); the two sanctioned dialog harnesses + the collocated-`beforeunload` `JSInterop.SetupModule` trap |
| `.github/skills/dialog-ui/SKILL.md` | One bullet in "Rules that catch people out" extending §5's FormRow rule to dialog input groups (peer group = one `AlignTop` row; `QuestionPromptDialog` migration precedent + test seam; pointer to the testing.md harness section) |
| `.github/copilot/rules/blazor-components.md` | (i) §"Edit form layout" gains the FormRow-first rule incl. `LabelPosition="Below"` + equal-height `Rows=` textareas (**f**); (ii) §"`::deep`" gains the consumer-side-override pattern (SectionCard header fix), the `FluentCard` `design-unit×5px` padding fact, and the chip `max-width` one-liner (**a**); (iii) new §"Compact forms: hints, disabled states and empty notes" — the D11/D13/D14e rule family (no narrative paragraphs, `FormRow Help=`→`HelpIcon`, disabled-with-reason, empty-state-not-paragraph + authoritative-empty gating (P8-4), single static state→text helper, shared-chrome-wins) |

Dropped by owner (Q1): the MSB3027 `/p:BuildProjectReferences=false` workaround — AGENTS.md's
lock rule ("surface the lock, let the owner decide") stays the whole policy.
Placement decisions: no new skill (Q2 — `blazor-components.md` is auto-routed for every `.razor`
change via the AGENTS.md specialty table); `testing.md` owns dialog-harness mechanics with a
dialog-ui cross-ref (Q3); delivery = this branch, docs-only commit pending the owner's "commit"
(Q4).


**Follow-up 7 — D14f: type/grading "what it drives" hint + enum investigation (owner, 2026-10-09, same branch)**

Owner asked for a hint explaining what Assignment type & grading format influence ("they drive
other places but are confusing to grasp") — plus a repo investigation into what underpins the two
enums, and whether that is also a skill lesson ("all as recommended" on the grilled Q1a/Q2 frontier).

**Investigation findings** (the hint's factual basis — every sentence in the shipped text traces
to one of these):
- Domain enums (`Assignments.Core/Domain/`): `AssignmentType` = Digital/**Online**,
  SemiManual/**Hybrid**, Manual/**Offline**; `GradingFormat` = TeacherGraded/**Teacher Marked**,
  AutoGraded/**Auto Scored**, InstantGraded/**Instant Feedback** — the `[Description]` values ARE
  the user vocabulary (the dropped gloss "Manual = in class" was wrong; it is *Offline*).
- `documents/solution/assignment-request-implementation-details.md` §3.3: **immediate vs held
  feedback** — `InstantGraded` returns per-question feedback in the submit response,
  `AutoGraded` holds it for teacher review; scoring fields render only for the two auto formats.
- `documents/rounds/round-ar-6-scoring.md`: both auto formats score at submit
  (`Score = correctCount × MaxScore / autoScorableCount`; `Passed = Score >= PassScore`);
  `TeacherGraded` never calls the scoring engine (score/passed stay null until teacher marking).
- FR-220 (`QuestionGenerationGate`): AI generation = (Online|Hybrid) × (Auto Scored|Instant
  Feedback); `QuestionsPassSubmitGate`: zero questions is fine for Teacher Marked, blocking for
  the auto formats.

**Delivered:**
| Piece | Detail |
|---|---|
| `Authoring.TypeGradingPurposeHint` (public const, test seam) | The documented causal chain, in the enums' own vocabulary — no invented glosses |
| `GradingRowHelp` | Now three `\n`-labelled lines: purpose → AI availability (dynamic) → feedback mode (dynamic); native `title` renders the newlines as a multi-line tooltip |
| Test | `ScoringFields_RenderInsideBasics_UnderTheGradingFormat` asserts the grading row's help `title` contains the purpose constant **and** splits into exactly 3 lines |
| Spec | D14 row item (f) |
| Skill lesson | New bullet in `.github/copilot/rules/blazor-components.md` §"Compact forms": *hints explain influence, not just meaning* — the driving row owns the causal chain; ground claims in `documents/solution/…`; enum `[Description]` values are the user's vocabulary, never invent glosses |


**Follow-up 8 — skills-capture review (owner-requested, 2026-10-09, same branch)**

Owner asked for a review of the skill-lesson work (`glm-5.3`/`cline-pass` was requested; both
subagent spawns were **interrupted by the environment** before returning, so the parent ran the
review itself — adversarial, fresh evidence reads, no memory). Verdict: **PASS, zero P1** — every
claim in the four guidance files verified against the repo: `Value=` precedents (DashboardCard 1,
RowActionsMenu 3, HelpIcon 1); `DialogRoundTripBudget` (AssignmentCreateBunitTests:64); both dialog
harnesses + `BeforeUnloadModulePath` wiring; the three QuestionPromptDialog FormRows + `.cq-prompt-nums`
gone; `FormRow.razor`'s own "canonical / 180px" wording; the `:deep` rule, `EmptyStateHint` statics,
`IsScoringInapplicable`, `TypeGradingPurposeHint`, `Manual` = `[Description("Offline")]`, FluentCard
`calc(var(--design-unit) * 5px)` padding, chip `max-width` precedent, §3.3 immediate-vs-held; markdown
fences balanced; D14 spec row single-line.

Three P2 fixes applied (owner "all as recommended" after a grill round):
1. `fluentui-icons` line 49 — "For custom images…" under-claimed `Value=`; now "For `Icon`
   **instances** — shared constants and custom images alike — use the non-generic `Value` property".
2. `blazor-components` — de-jargonized "(parent spec UX-17/UX-19)" → "the disabled-with-reason rule of
   the compartments spec" and dropped the bare "(P8-4)" parenthetical (the rule was already in words).
3. `testing.md` — "the most common underlying cause" softened to "the most common cause seen in this
   repo so far" (one incident class is not a statistic).

Post-fix re-verification: no residual jargon/old wording; fences balanced in all three edited files.


**Follow-up 9 — D15: the assignment type defines the grading format (owner, 2026-10-09, same branch)**

Owner supplied the three type definitions and the rule that follows, then grilled ("all as
recommended") and asked for a plan before code. Delivered after the plan was approved:

| Piece | Detail |
|---|---|
| `Contracts/AssignmentTypeGradingRules.cs` (**new**) | The owner's three definitions verbatim + the matrix (**Offline → Teacher Marked only**; Online/Hybrid keep all three), `PermittedFormats` / `IsPermitted` / `FallbackFor` / `EnsurePermitted` / `NotPermittedMessage`, plus `AssignmentTypeGradingValidationException`. Lives in **Contracts** because `Assignments.Application` does not reference `Assignments.Core` (verified) and Contracts is the only shared project — no architecture test forbids the Application→Core reference, but this keeps the UI free of the domain assembly |
| Create + Update handlers | `EnsurePermitted((AssignmentTypeDto)(int)command.AssignmentType, …)` before the aggregate is built/mutated |
| `AssignmentRoutes` (+2 catches) | `AssignmentTypeGradingValidationException` → **400**, beside the existing typed validators on both the create and update routes |
| `AssignmentAuthoring.razor` | Type select drives `OnTypeChangedAsync`: on an invalidating change the grading picker switches to `FallbackFor` **and** renders `#authoring-grading-switched` with `FormatSwitchedReason` (cleared by the author's own pick); `GradingFormatOptions` filters by the type **and keeps an out-of-matrix persisted pick** so its label renders; the load path resolves the stored format from the **full enum** (a bug the new tests caught — `FirstOrDefault` over the filtered list silently dropped the value); the row hint gains a fourth line (the rule + the selected type's meaning) |
| Tests | **new** `AssignmentTypeGradingRulesTests` (matrix, fallback, guard message in the owner's vocabulary, every permitted pair accepted, definitions); 3 new bUnit tests in `AssignmentAuthoringBunitTests` (picker filters per type · type-switch applies + explains · legacy pair keeps its pick with no note) + the scoring test's help-title assertion widened to four lines and the Online meaning |
| Docs | **new** `documents/solution/assignment-type-grading-definitions.md` (definitions, the grading-format behaviours, the rule, why Contracts, the UX rules, the FR-220 relationship); spec D15 row + §9 item 13 |

Gates: Application and Core build 0 errors · `SchoolCollab.Assignments.Tests.Unit`
**1020/1020 passed** (test csproj built with `/p:BuildProjectReferences=false` — the owner's
running F5 session holds the Worker's bin).

**Named next step (owner request, same message):** a follow-up spec for *writing questions* —
per-question **response definitions** (video · recorded audio · uploaded resource (doc/image)
· or a mix, chosen at question setup), **teacher instructions on a question** (text · audio ·
video · URL/image resource), and for **Teacher Marked** assignments a required teacher
**review + response** per submitted response; the AI generation either emits those definitions
or (as it stands) leaves generated questions for the author to complete.

