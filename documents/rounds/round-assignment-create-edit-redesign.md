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
