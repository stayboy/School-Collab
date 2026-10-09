# Assignment Create/Edit Redesign — Reordered Create + Summary-First Draft Edit

> **Status:** adopted (grill session 2026-10-08; Round 1 Q1–Q6 + Round 2 Q1–Q3 all
> ratified "as recommended" by the owner). **Presentation-only** companion to
> `documents/specs/assignment-authoring-compartments.md` (which stays CLOSED and
> authoritative for behaviour, gating, save semantics, the targeting model and the
> lifecycle matrix). Where this spec reorders, renames or hides **markup**, it wins;
> everything it does not mention is unchanged.
>
> **Execution mode (owner, 2026-10-08): SOLO** — the session agent implements this
> spec directly; no multi-agent round.
>
> **Companions:**
> - `assignment-authoring-compartments.md` — compartment model, modes, §11 action matrix (behaviour).
> - `assignment-authoring-content-questions-modern-ui.md` — Content/Questions presentation (unchanged).
> - `assignment-creation-with-ai.md` — AI generation as an action (unchanged).

---

## 1. Summary

Two changes to the single `AssignmentAuthoring` surface:

1. **Create page reordering** — the form's field order is re-laid out per §3
   (Status & available from first; Due date right after Assignment type & grading;
   Description + Instructions extracted into a new bottom **"Instructions"**
   compartment with side-by-side inputs, labels below); Strands & lessons collapse
   from two stacked multi-selects into **two buttons under the Subject select** (§4).
2. **Summary-first draft edit** — the Edit route no longer opens on the full
   compartment form. It opens on a **summary surface**: read-only facts + a pencil
   edit icon (top-right), then **Questions & AI (always rendered, always
   editable)**, then **Targets & Audience as a collapsed accordion**. No input
   fields render on the summary unless the author explicitly requests them via the
   pencil. Breadcrumb navigation is added on top (§6).
3. **Questions & AI and Content & Resources leave the Create page** (D10,
   verification round) — the original requirement "move questions and AI to draft
   edit when assignment is created", extended by the owner to Content &amp;
   Resources, means Create collects the assignment's details only; the question
   editor, AI generation, staged drafts, attachments and URL resources all begin
   at draft-edit time.

**Interpretation note (owner's "do not show all input fields except requested"):**
this applies to the **edit/draft surface**. The Create page shows every field it
needs — reordered, not hidden (§3).

---

## 2. Decision log (grill rounds 2026-10-08)

| # | Decision | Choice |
|---|---|---|
| D1 | Draft-edit shape | **In-page view toggle** on `/assignments/{id}/edit` — the pencil flips a page-level summary/edit-fields state; no new route, no new guard surface (Round 1 Q1-A). |
| D2 | Summary contents | Facts grid + Questions & AI + Targets accordion. **No** Content & Resources and **no** Rules on the summary — reachable only via the pencil (Round 1 Q2-A). |
| D3 | Status scope | **Draft + Scheduled** → summary with pencil; **Published/Closed/Archived** → same summary, **no pencil** (replaces today's read-only full-form View) (Round 1 Q3-A). |
| D4 | Strand/lesson buttons | Each button opens a **picker dialog** (shared dialog shell, multi-checkbox, subject-filtered); picks shown as dismissible `Chip`s below the buttons (Round 1 Q4-A). |
| D5 | "Instructions" section | A **real compartment** (its own stack position); Basics ends after the strand/lesson row (Round 1 Q5-A). Its jump-nav entry became moot when **D12** removed the jump-nav. |
| D6 | Create field order | Adopted as written in Round 1 Q6 (§3). |
| D7 | Action bar / jump-nav on summary | **Action bar stays** (status-driven, §11 matrix untouched). **Jump-nav rendered only in the edit-fields view** — hidden on the summary (Round 2 Q1-A). *Superseded in part by **D12** (2026-10-08): the jump-nav no longer exists on any surface; the action-bar half stands unchanged.* |
| D8 | Breadcrumb | `Assignments › {Title} › Edit` (Edit route) / `Assignments › New assignment` (Create); title crumb → Detail. First `FluentBreadcrumb` usage in the repo (Round 2 Q2-A). |
| D9 | Pencil / back-to-summary while dirty | **Never blocked.** The view toggle is not navigation — the form model and unsaved edits survive it, so showing a "discard" confirmation would be false. UX-7 guards only genuine navigation (Round 2 Q3-B). |
| D10 | Content & Questions on Create (**added by the verification round 2026-10-08 — restores the original request the first draft of this spec had dropped**) | **Create renders NEITHER Questions & AI NOR Content & Resources.** "Move questions and AI to draft edit when assignment is created" — and Content & Resources follows the same rule (owner confirmation 2026-10-08) — means authoring children (questions, attachments, links) starts only once a draft exists. Create therefore renders no `#authoring-questions` / `#authoring-content` sections, no question/resource editors, no `Generate questions` kebab action. (Its jump-nav listed **four** entries — Basics · Targets · Rules · Instructions; that list went away with **D12**.) Edit and summary surfaces are unchanged. |
| D11 | Inline narratives on the form (owner, 2026-10-08) | Prose paragraphs that eat the form's space become **(i) help icons on their row labels** — FormRow's new optional `Help` parameter renders a small info icon inside the label whose native `title` carries the text (null = previous markup byte-for-byte). "Feedback mode…" + the AI-availability text merge into one icon on the paired type/grading row; "Set the pass threshold…" rides the Pass Score row's icon; the guardian-review lock reason rides its row's icon (conditional — rendered only while locked). The Rules card's explanatory hint is removed outright — the "Inherited from Grade/Tenant policy" badge on every readout already said it. *(The badge itself was then replaced by padlock tooltips — **D13**.)* The Pass Score's **inapplicable** state renders a **disabled checkbox whose label carries `ScoringInapplicableReason`** instead of a disabled number field + separate reason paragraph. The Instructions compartment's two textareas render at **equal height** (both `Rows="4"`). |
| D12 | Compartment jump-nav (**owner, 2026-10-08**) | **The jump-nav is removed from every surface** — the submenu list that used to sit under the page title ("New assignment" / the assignment title) is gone, on Create and on the edit-fields view alike (D7's hide-on-summary rule becomes moot). The **compartment sections and their long-standing ids are untouched** (`#authoring-basics`, `-targets`, `-rules`, `-content`, `-questions`, `-instructions`), so the `Generate questions` kebab action's `#authoring-questions` anchor and every bUnit test seam still resolve; the `scroll-margin-top` on those containers drops from 112px to 56px now that only the sticky action bar has to be cleared. The now-unused `Compartments`/`AllCompartments` list and the `Compartment` record are deleted, and the assertions that used to read the compartment titles *through* the nav links read the `section.authoring-compartment` order directly. |
| D13 | Rules card: inherited note (**owner, 2026-10-08**) | **The per-row "Inherited from Grade/Tenant policy" badge is removed** — five readouts no longer repeat one sentence. The note now rides a **padlock tooltip**, and the padlock is **conditional**: it appears only when the value is *inherited from policy* **and** the author *may not override it*. Four readouts — approval before publish, the notification policy, the archive window and the signature requirement — are policy-owned end to end (the author has no control over any of them), so they always carry it; **guardian review is the one conditional entry**: while the policy leaves it unset the author chooses in Basics, so that row states the author's choice and carries **no** padlock, and once the policy (or the D4 signature implication) sets it — the same condition that disables the Basics toggle (`PolicyReviewLocked`) — the row is locked and gains it. *(D14 later dropped the card-**header** padlock and its `SectionCard.HeaderHelp` parameter — only the rows state the lock.)* Each padlock's native `title` is `AssignmentAuthoring.PolicyInheritedHelpText` ("Inherited from Grade/Tenant policy — resolved from your grade and organization policy and not overridable on this assignment."), the badge text + D11's retired hint sentence folded together. Implemented as a new shared `HelpIcon` component (`src/SchoolCollab.Admin.Shared/Components/HelpIcon.razor`) — required `Text`, optional `Icon` (defaults to the info glyph) and `Class` *(D13's `SectionCard.HeaderHelp` parameter was then removed by **D14** as unused)*. `FormRow`'s existing `Help` hint now renders through the same component (identical markup contract: the `form-row-help` class is preserved), so the repo has exactly one help-hint implementation. |
| D14 | Form polish (**owner, 2026-10-09**) | Four follow-ups on the shipped surface: **(a) Subject moves up** — directly after Title and *before* the Assignment type & grading row (it is vital), and its **Strands & lessons row moves with it** so D4's "buttons directly beneath the Subject dropdown" contract still holds (§3.1); **(b) the Rules card's header padlock is removed** — the owner found the lock beside the header/Add area unnecessary noise; the `SectionCard.HeaderHelp` parameter (added by D13, now unused) is deleted with it, and the per-row padlocks (D13's conditional rule) remain the only place the inherited-policy lock is stated; the now-dead `AnyPolicyReadoutLocked` property goes too; **(c) the Rules card header aligns with its subitems** — the readout rows carry no horizontal padding, so the shared header's 1rem indent made "Rules" sit 16px right of the rows; a `:deep()` rule zeroes the header's horizontal padding *for this card only* (`.authoring-rules-card :deep(.section-card__header)`), leaving every other `SectionCard` consumer on the shared chrome; **(d) the Generate-questions configure box's inputs ride `FormRow`** — `QuestionPromptDialog`'s bare flex row (`.cq-prompt-nums`, per-field labels floating above their inputs) becomes three shared rows — "Number of questions" (single), "Difficulty mix" (`AlignTop` multi-input, Easy/Medium/Hard keep their visible sub-labels) and "Question types" (the chips container) — so every knob label aligns to the same 180px gutter as the rest of the authoring form; `.cq-prompt-nums` is deleted with the container; **(e) the picks row's empty-state notes are deleted** — "This subject has no strands yet." and "No lessons for this strand yet." no longer render as paragraphs in Basics (they ate two rows); they ride the disabled buttons' `title`s (unchanged) plus the "Strands & lessons" row label's (i) hint via `FormRow Help=` → the component's new static `EmptyStateHint(...)` (single source, B1; strand sentence wins, A1's icon scope), the transient "No strand selected" note is gone outright, and `.context-picks__note` CSS + `NoStrandPickedText` are deleted with the paragraphs; **(f) the type/grading row's (i) icon explains what the fields DRIVE** — new `public const string Authoring.TypeGradingPurposeHint` (the `PolicyInheritedHelpText` test-seam precedent) states the DOCUMENTED causal chain (grading format powers the scoring fields/pass threshold; Instant vs Auto = immediate vs held per-question feedback per `assignment-request-implementation-details.md` §3.3; Teacher Marked disables them; the type joins the grading format in the FR-220 AI gate — enum `[Description]` values are the user's vocabulary, no invented glosses) and `GradingRowHelp` becomes four `\n`-labelled lines — purpose, the selected type's meaning (D15), AI availability, feedback mode (native `title` renders newlines as line breaks). |
| D15 | **The assignment type defines the available grading formats** (**owner, 2026-10-09**) | The owner's definitions of Online / Hybrid / Offline and the rule that follows — **Offline (handwritten) is Teacher Marked only**; Online and Hybrid keep all three formats — live in `src/Assignments/SchoolCollab.Assignments.Contracts/AssignmentTypeGradingRules.cs` (Contracts, the only project both the UI and the domain handlers see: `Assignments.Application` does **not** reference `Assignments.Core`), with the durable prose record in `documents/solution/assignment-type-grading-definitions.md`. The page filters the grading picker through `PermittedFormats` and applies `FallbackFor` when a type change invalidates the current pick — explained by the new `#authoring-grading-switched` note (`FormatSwitchedReason`), never a silent rewrite; a **persisted** out-of-matrix pair loads untouched so its label still renders (the load path resolves the stored format from the full enum, not the filtered list). The create/update handlers call `EnsurePermitted` → `AssignmentTypeGradingValidationException` → **400**, caught by both assignment routes beside the existing typed validators. The type/grading row's (i) hint gains a fourth labelled line: the rule + the selected type's meaning. The AI gate (FR-220) is unchanged. |


---

## 3. Create page — field order and the Instructions compartment

### 3.1 Canonical order (top → bottom of the page)

Inside `#authoring-basics` (all ids unchanged — this is markup order only):

1. **Status & available from** — `authoring-basics-status`, `authoring-basics-available-from` (readout row, **first form field**)
2. **Title** — `authoring-basics-title`
3. **Subject** — `authoring-basics-subject` *(D14: moved up — the subject is vital, so it leads the
   authoring decisions right after Title)*
4. **Strands & lessons buttons + chips row** — §4 *(D14: moves WITH the subject so the two stay
   adjacent — D4's contract that the buttons sit directly beneath the Subject dropdown holds)*
5. **Assignment type & grading format** — `authoring-basics-type`, `authoring-basics-grading` (multi-input row)
6. **Due date** — `authoring-basics-due` *(moved up; directly after type & grading)*
7. **Max score & max attempts** — `authoring-basics-max-score`, `scoringFieldsMaxAttempts`
8. Feedback-mode text · `ScoringFieldsSection` · Guardian review toggle · AI-availability text *(D11: the narratives become (i) help icons on the row labels — one merged icon on the paired type/grading row, `Help` on the Pass Score row, conditional `Help` on the Guardian review row; no inline paragraphs)* — Basics then **ends** here

### 3.2 New compartment: Instructions

- New `<section id="authoring-instructions" class="authoring-compartment">` with
  `<h3 class="authoring-compartment-title">Instructions</h3>`, rendered **last on
  the page** (after the Content/Questions row).
- Contains Description (`authoring-basics-description` — id kept for test seams)
  and Instructions (`authoring-basics-instructions`) **side by side (inline)**:
  a page-scoped two-column wrapper (`.authoring-instructions-grid`: two `flex: 1`
  cells at ≥720px, stacked below), each cell wrapping one
  `<FormRow LabelPosition="RowLabelPosition.Below" AlignTop>` — the row's **own
  label renders beneath its own textarea** (the existing FormRow `Below`
  placement; no shared-component change, no bespoke label markup).
- Section label per the owner: the compartment title is **"Instructions"**; the
  individual field labels keep their own text ("Description", "Instructions").
- `Compartments` gained `new("instructions", "Instructions")` → the jump-nav gained the
  entry (last position); D7 kept the jump-nav hidden on the summary only.
  **D12 later deleted that list and the nav entirely** — the compartment sections are
  now simply stacked in this order, with no anchor list to keep in sync.

---

## 4. Strands & lessons — compact pattern (D4)

Replace `ContextPicksSection`'s two stacked `Multiple` selects with:

1. **A two-button row directly under the Subject select**, component-scoped layout
   (`.context-picks__buttons` in `ContextPicksSection.razor.css` — the row is the
   component's own chrome, not the page's):
   - `Add strand` — `id="authoring-add-strand"`, `IconStart=FluentIcons.Add`
   - `Add lesson` — `id="authoring-add-lesson"`, `IconStart=FluentIcons.Add`
   - Both **disabled** (with the existing inline disabled reason, never hidden —
     §12 of the parent spec) while: no subject selected · `IsReadOnly` ·
     `ContextPickersDisabled` (children-unavailable / load failure reasons carry
     over unchanged).
   - `Add lesson` is disabled while the lesson list has **not loaded**
     (`!LessonsLoaded`), while its option list resolved **empty**, or while the
     page's `ContextPickersDisabled` holds: with no strand picked the source is
     every lesson of the subject (G31 — unchanged).
2. **Picker dialog** (shared dialog shell — `.github/skills/dialog-ui/SKILL.md`):
   multi-checkbox list of the subject's root strands (strand button) / the picked
   strands' lessons (lesson button); OK commits the id set to
   `Model.ContextStrandIds` / `Model.ContextLessonIds` and raises the existing
   `StrandPicksChanged` (strand adds re-read lessons) + `Changed` events. Cancel
   changes nothing.
3. **Chips** below the buttons — the existing `Chip` strips
   (`authoring-basics-strand-chips` / `authoring-basics-lesson-chips`) with all
   current semantics intact: CP-11 dangling-pick markers, id subtitles for
   unresolved picks, "(unavailable)" while the name list never loaded, per-chip
   remove.

4. **Empty states (D14e, owner 2026-10-09)** — no paragraphs. The two
   empty-source sentences ("This subject has no strands yet." / "No lessons for
   this strand yet.") ride (i) the disabled button's native `title` (unchanged)
   and (ii) the row label's (i) hint — `FormRow Help=` wired to the component's
   static `EmptyStateHint(...)` (the `ScoringFieldsSection.IsScoringInapplicable`
   single-source precedent), rendered only while a source is authoritatively
   empty; the strand sentence wins when both are (root cause). The transient
   "No strand selected" note is **deleted outright** (A1: the normal state, not a
   condition to flag), and an unloaded list never produces a hint (P8-4).

The option/name sources, fetches and save projection on the page are **unchanged**;
only the control vehicle changes. `ContextPicksSection.razor` becomes the
dialog + chips component (or is split: chips stay inline, dialog is new) — the
page keeps owning data, per its header comment.

---

## 5. Summary-first draft edit (D1/D2/D3)

### 5.1 The two views of the Edit route

`/assignments/{id}/edit` renders one of two views from a page-level
summary/edit-fields state (default **summary** whenever an item is loaded):

**A) Summary view (default)** — top to bottom:

1. Breadcrumb (§6) → sticky action bar (unchanged, D7) → error/notice bars (unchanged).
2. **Summary card** — `id="authoring-summary"`:
   - Header: assignment title + status badge; **pencil edit button top-right**
     (`id="authoring-summary-edit"`, `FluentIcons.Edit`, `AriaLabel="Edit assignment details"`),
     rendered **only when `EffectiveMode == Edit`** (Draft|Scheduled — D3). Never
     rendered for Published/Closed/Archived.
   - **Read-only facts grid** (label/value pairs, no inputs):
     Status · Available from · Due date · Assignment type · Grading format ·
     Max score · Max attempts · Subject · Strands · Lessons · Description ·
     Instructions. Empty values render "—"; Strands/Lessons show their resolved
     names (or the chip-unavailable wording from §4).
3. **Questions & AI** — `id="authoring-questions"`, the **existing
   QuestionGenerationSection + QuestionEditorSection (+ QuestionsDraftSection for
   Draft)** markup, rendered **verbatim and always** (same gate, fail-closed
   `ChildrenEditorDisabledReason`, disabled-with-reason rules as today). This is
   the priority block: it sits immediately under the summary, no accordion, no
   toggle. The kebab's `Generate questions` anchor (`#authoring-questions`)
   therefore still resolves from the summary.
4. **Targets & Audience accordion** — `id="authoring-targets"`, a
   `FluentAccordion` (**first usage in repo** — verify props against the
   `fluentui-component-props` skill at implementation), **collapsed by default**:
   header shows "Targets & audience" + the count badge; expanded body reuses the
   existing `SectionCard` readout list (entry rows with remove) and the live
   recipient-preview hint. The `+ Add target` action lives in the expanded body.

Content & Resources and Rules are **not on the summary** (D2).

**B) Edit-fields view** — entered via the pencil, exited via a
"Back to summary" button (`id="authoring-summary-back"`). This is **today's full
compartment form verbatim** (Basics in §3 order, Targets card, Rules card,
Content, Questions) and the sticky action bar unchanged — **no jump-nav** (D12
removed it). Saving stays in the action bar kebab, as today.

### 5.2 Rules

- The toggle is **pure presentation state**: the `AssignmentEditFormModel`,
  loaded children and all unsaved edits survive flips in both directions (D9).
  No location change, no guard prompt, no reload.
- `HeadingText` on the summary: `Edit assignment` (Edit) / `Assignment overview`
  (degraded View) — unchanged strings.
- View mode (D3) renders the **same summary** with no pencil; today's read-only
  full-form View markup is no longer reachable on this route — the read-only
  questions hint ("Questions are managed on the assignment's edit surface")
  moves to the summary's Questions block unchanged.
- Create mode never shows the summary (there is nothing to summarize before the
  first save); post-create navigation stays as today.

---

## 6. Breadcrumb (D8)

- Rendered at the very top of `.authoring-page`, above the action bar,
  `id="authoring-breadcrumb"`, via `FluentBreadcrumb` + `FluentBreadcrumbItem`
  (Microsoft.FluentUI.AspNetCore.Components 4.14.2 — first repo usage):
  - **Edit route:** `Assignments` → `/assignments` · `{Assignment title}` →
    `/assignments/{Id}` (Detail) · `Edit` (current, not a link — or `Overview`
    when degraded to View). While `_item` is null/loading: `Assignments › …`.
  - **Create route:** `Assignments` → `/assignments` · `New assignment` (current).
- The breadcrumb does not change on the view toggle (same route, same trail).

---

## 7. Element ids and anchors

Every existing id is preserved (moves are markup-order only) — the bUnit seams
`authoring-basics-*`, `scoringFields*`, `#authoring-basics`, `#authoring-targets`,
`#authoring-rules`, `#authoring-content`, `#authoring-questions` keep their
values. **New ids:** `authoring-breadcrumb`, `authoring-summary`,
`authoring-summary-edit`, `authoring-summary-back`, `authoring-instructions`,
`authoring-add-strand`, `authoring-add-lesson`, `authoring-targets-accordion`.
There is **no jump-nav** (D12). The compartment SECTIONS are stacked in the
canonical order and their ids are the anchors: `#authoring-basics`,
`#authoring-targets`, `#authoring-rules` (the Rules card's container, in the
right-hand column), `#authoring-content`, `#authoring-questions`,
`#authoring-instructions`. On **Create** the Content & Resources and Questions &
AI sections are absent, so only basics/targets/rules/instructions exist (D10);
the ids resolve for the `Generate questions` kebab action wherever the section
renders.

---

## 8. Affected code surface (solo round)

| File | Change |
|---|---|
| `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentAuthoring.razor` | Field reorder, Instructions compartment, summary/edit-fields toggle, breadcrumb, button row wiring, `Compartments` entry |
| `…/AssignmentAuthoring.razor.css` | `.authoring-instructions-grid`, summary card + facts grid, button row, accordion chrome |
| `…/ContextPicksSection.razor` (+ `.css`) | Becomes chips + picker dialog (D4) |
| `…/ContextPickDialog.razor` (+ `.css`), `…/ContextPickDialogModel.cs` | **New** — the D4 multi-checkbox picker dialog and its form model (dialog state only; no behaviour/DTO/route change) |
| `src/SchoolCollab.Admin.Shared/Components/HelpIcon.razor` (+ `.css`) | **New (D13)** — the single help-hint implementation: required `Text` (native `title`), optional `Icon` (default the info glyph) and `Class` |
| `src/SchoolCollab.Admin.Shared/Components/FormRow.razor` (+ `.css`) | **Additive**: the optional `Help` parameter (D11) renders an (i) icon inside the label whose native `title` carries the row's hint — null (the default) renders the previous markup, so every existing caller is unchanged. **D13**: the icon is now the shared `HelpIcon` (the `form-row-help` class is preserved, so the scoped CSS/tests are unaffected) |
| `src/Students/…/Components/Students/SectionCard.razor` | ~~**Additive (D13)**: optional `HeaderHelp` parameter~~ *(D13 added the `HeaderHelp` padlock hint beside the title; **D14 removed it** as unused noise — the component is back to its pre-D13 shape)* |
| `…/TargetsAndAudienceDialog.razor` | Unchanged (invoked from accordion body) |
| Tests: `AssignmentAuthoringBunitTests`, `ContextPicksSectionBunitTests`, `ScoringFieldsSectionBunitTests`, `AssignmentDetailBunitTests`, `QuestionsDraftSectionBunitTests`; `AssignmentCreateBunitTests` (its two dialog-round-trip waits hardened to a named 15 s budget — pre-existing load flake) | §9 (`AssignmentContextPicksTests`/`AssignmentInstructionsTests` needed no change) |
| `documents/specs/assignment-authoring-compartments.md` | Add this spec to the header's companion list |

No behaviour `.cs`, DTO, route, or API change (the only new `.cs` file is the
dialog's form model). No change to `Create.razor` / `Edit.razor` hosts,
`Detail.razor`, or the save path.

---

## 9. Verification surface

**Update (assertions that encode the old order/shape):**

- `ExpectedFormSections` / `CompartmentSectionIds` — the compartment order read straight off
  the DOM (`section.authoring-compartment`): five sections for **Edit**, three on **Create**
  (no content/questions sections — D10). **D12** replaced the old
  `ExpectedCompartments`/`CompartmentTitles` pair, which read the titles *through* the
  jump-nav links (so it also asserted every anchor resolved) — the anchors are now pinned
  directly by `CompartmentAnchors_StillResolve_WithoutTheJumpNav`.
- `EverySurface_RendersNoJumpNav_AndTheFormKeepsItsSections` (was
  `EveryMode_RendersTheJumpNavForAllFiveCompartments` / `JumpNav_StillResolvesAllFiveAnchors…`)
  — asserts `nav.authoring-jumpnav` is **absent on every surface** (D12) and that each mode
  keeps its section set; `Edit_Draft_RendersTheFormSections` and
  `Create_RendersItsCompartmentSections_NoContentNoQuestions` cover the per-mode order.
- `View_Published_RendersAllFiveCompartments` /
  `View_LaterStatus_RendersAllCompartmentsReadOnly` → View now asserts the
  **summary** (facts text present, no inputs, no pencil).
- Field-order assertions in `#authoring-basics` (status row first, due date
  directly after the type/grading row, description/instructions absent from
  Basics) and their presence inside `#authoring-instructions`.
- `AssignmentContextPicksTests` — buttons/dialog/chips instead of multi-selects.

**New:**

1. Summary-first: Edit route + Draft loads with `#authoring-summary`, facts grid
   populated, **zero input fields** in the summary card, pencil present.
2. Pencil toggles to the full form (compartment sections visible; **no jump-nav** —
   D12); "Back to summary" returns; **no confirmation prompt in either direction with
   dirty edits** (D9) — model values survive the flip.
3. Targets accordion: collapsed by default, expands to reveal entries + preview;
   count badge matches.
4. Published/Closed/Archived → summary with **no** `#authoring-summary-edit`.
5. Breadcrumb: three crumbs on Edit (title links to Detail), two on Create;
   degraded View's current crumb reads `Overview`.
6. Questions & AI renders on the summary in every mode incl. gate-disabled and
   children-unavailable states; `Generate questions` kebab anchor resolves.
7. Strand/lesson buttons: both disabled until a subject is picked (existing
   `NoSubjectReason`); `Add lesson` additionally disabled while the lesson list
   never loaded; dialog OK commits picks, chips render/remove, strand
   change still reloads lessons.
8. **D10:** Create renders **neither Questions & AI nor Content & Resources** —
   no `#authoring-questions` / `#authoring-content` sections, no
   `QuestionGenerationSection`/`QuestionEditorSection`/`ResourcesSection`
   components, no `Generate questions` kebab action.
9. **D11:** no inline narrative paragraphs remain (`#authoring-feedback-mode`,
   `#authoring-generation-hint`, `#authoring-rules-hint`,
   `#authoring-review-policy-reason`, `.scoring-fields-reason`,
   `.muted` pass-threshold line all gone); the texts ride **(i) help icons on
   the row labels** (`.form-row-help` with a native `title` — merged on the
   type/grading row, `Help` on Pass Score, conditional on Guardian review when
   locked); the inapplicable Pass Score renders a `fluent-checkbox` whose
   `label` content carries `ScoringInapplicableReason`; both Instructions
   textareas are `Rows=4`.
10. **D12:** no `nav.authoring-jumpnav` on **any** surface (Create, edit-fields,
    summary); the compartment **section** set per mode is unchanged
    (`ExpectedCreateSections` / `ExpectedFormSections`), and the anchors the nav
    used to link still resolve — `#authoring-rules` (the Rules card container)
    and `#authoring-questions` (the kebab's target) — asserted by
    `CompartmentAnchors_StillResolve_WithoutTheJumpNav`.
11. **D13** (+ **D14**): no `fluent-badge` inside `#authoring-rules` (the per-row
    "Inherited from Grade/Tenant policy" text is gone); **no** card-header padlock
    either (`.section-card__help` renders nowhere — D14), and
    `.authoring-policy-help` padlocks appear **only on the
    non-overridable rows** — four of them by default (guardian review is the
    author's choice while the policy leaves it unset), five once the policy sets
    it — each carrying `AssignmentAuthoring.PolicyInheritedHelpText` as its native
    `title` (`Rules_PadlockHintAppearsOnlyForNonOverridablePolicyValues`).
12. **D14:** the Basics field order reads `status → title → subject → add-strand →
    type → due` (subject + its picks row precede the type/grading row), asserted by
    `Basics_LeadsWithStatus_AndSubjectPrecedesTheTypeAndGradingRow`; the
    configure box renders three `.form-row` rows whose `.form-row-label` texts are
    "Number of questions" / "Difficulty mix" / "Question types" and the count row's
    `<label for>` targets `cq-prompt-count`
    (`Knobs_RenderAsFormRows_SoEveryLabelSharesTheFormGutter`); the picks row
    renders **no** `#authoring-strands-none` / `#authoring-lessons-none`
    paragraphs in any state, its disabled buttons keep the empty-source
    `title`s, and `FormRow.Help` renders the (i) hint (`.form-row-help` with
    `NoStrandsText`) while a source is authoritatively empty
    (`SubjectWithNoStrands_RendersNoParagraph_AndTheButtonCarriesTheText`,
    `StrandsWithNothingPicked_RendersNoNote_AndNoRowHint`,
    `NoLessonsForPickedStrands_RendersNoParagraph_AndTheButtonCarriesTheText`,
    `EmptyStateHint_PrefersTheStrandSource_WhenBothAreEmpty`,
    `UnloadedList_RendersNoEmptyNote`,
    `Page_EmptyStrandSource_CarriesTheRowHelpIcon_NotAParagraph`).
13. **D15:** the grading picker offers exactly the selected type's permitted formats
    and an Offline assignment is Teacher Marked only
    (`GradingPicker_OffersOnlyTheSelectedTypesPermittedFormats`); a type change that
    invalidates the pick applies the fallback **and** renders
    `#authoring-grading-switched` carrying `FormatSwitchedReason`
    (`SwitchingToOffline_MovesTheGradingFormat_AndExplainsWhy`); a persisted
    out-of-matrix pair keeps its value and label with no switch note
    (`LegacyOfflineWithAnAutoFormat_KeepsTheStoredPick`); the matrix, the fallback,
    the guard's rejection message and the owner's definitions are pinned by
    `AssignmentTypeGradingRulesTests`.

**Gate:** `dotnet build SchoolCollab.slnx` clean + `dotnet test` 0 failures
(solo round; no UI-tester scope).

---

## 10. Out of scope

- Any behaviour/gating/save/contract change (parent spec stays authoritative).
- Autosave, new routes, Detail.razor redesign, Prefab portal surfaces.
- Renaming existing element ids (explicitly forbidden — §7).

