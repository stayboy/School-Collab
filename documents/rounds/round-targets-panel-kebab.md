Provider: pi (models: deepseek-v4.1-flash worker, kimi-k2.7-code reviewer)

# Round — Targets & audience as a PanelSection with a category kebab

**Status:** CLOSED (2026-10-17) — verified green and reviewed with no P1. **Uncommitted:** no commit was authorised, so no
PR/commit carries it yet; the change set is live in the working tree and frozen in `diffs-targets-panel-kebab.patch`.
**Tier:** 2 — Light round (owner Q89, 2026-10-17): one worker + one static diff-only reviewer.
**Mode:** attended — every decision below was pinned by the owner in one grill round (Q86–Q93), answered "all as recommended"
except Q88, which the owner **modified** (see pinned decisions).
**Round base:** `HEAD` = `2683b7c9` (`main`, post the merged v5 train + specs #358). **No branch, no commit** — the owner has
not authorised either.
**Tree was DIRTY at round start** (prior round `fluentui-v5-postfix` left uncommitted by owner choice). Prior-round delta,
which the isolation base absorbs so it stays out of this round's patch (8 files):
`.github/copilot/rules/blazor-components.md`, `.github/skills/fluentui-component-props/SKILL.md`,
`documents/solution/fluentui-v5-residual-backlog.md`, `src/SchoolCollab.Admin.Shared/Components/FluentTabStrip.razor.css`,
`src/SchoolCollab.Admin.Shared/Components/RowActionsMenu.razor`,
`src/Students/SchoolCollab.Students.Application/Components/Toolbar/ActiveTermToolbar.razor.css`,
`tests/SchoolCollab.Admin.Tests.Unit/RowActionsMenuTests.cs`,
`tests/SchoolCollab.ArchitectureTests.Unit/RazorMarkupHygieneArchitectureTests.cs`. Plus its two artefacts
(`documents/rounds/round-fluentui-v5-postfix.md`, `documents/rounds/diffs-fluentui-v5-postfix.patch`).

**Isolation base (dangling commit, GC-able — rebuild with the three plumbing commands if "bad object"):**
`SNAP=d292e01b27b25caed50c016cf7434656694f4633`, built by `git add -A && TREE=$(git write-tree) && SNAP=$(git commit-tree
"$TREE" -p HEAD -m "round-targets-panel-kebab isolation base") && git reset -q`. Verified: 2927 files = 2925 tracked at HEAD
+ 2 untracked; `git diff --name-only HEAD $SNAP` = exactly the prior-round list above; index clean after reset.

**Freeze command for this round's patch:**
`git add -N -- src tests documents/specs documents/solution .github/copilot/rules && git diff $SNAP -- src tests documents/specs documents/solution .github/copilot/rules > documents/rounds/diffs-targets-panel-kebab.patch`
(explicit pathspecs, so the process artefacts — this doc and the patch itself — are deliberately excluded; the `git add -N`
is mandatory or the prior round's untracked artefacts diff as phantom deletions). The `documents/solution` and
`.github/copilot/rules` pathspecs were added at the rework step, because the accepted Q99/Q100 answers edit the `@key` rule and
the residual backlog — both of which the prior round also touched, and both of which the snapshot isolates by content.

---

## Plan

### Goal

On the create/edit assignment surface, render the **Targets & audience** block through the shared **`PanelSection`** — the
same component Content & Resources and Rules already wear — with its add affordances moved into the **header kebab menu**
(one item per target category) and **no Add button**. Also remove `PanelSection`'s hardcoded background so the panels carry
no surface of their own.

### Pinned decisions (owner, 2026-10-17 — grill Q86–Q93)

1. **Q86(a) — scope: create/edit surface only.** The read-only summary surface keeps its `FluentAccordion` + inner
   `SectionCard` untouched. PanelSection is used today in exactly two places (Content & Resources, Rules), both on the
   create/edit view — that view is the inconsistency.
2. **Q87(a) — keep the count chip**, supplied inside `HeaderActions` (reuse the existing `authoring-targets-count` class).
   `PanelSection` has no count slot; the Rules conversion dropped its count because Rules is a fixed five-row readout, but
   the target count carries information.
3. **Q88 (MODIFIED by the owner) — the category choice moves OUT of the dialog and into the kebab; no Add button.**
   One kebab item per category (the closed set of five), exactly like Content & Resources' kebab.
4. **Q90(a) — the dialog runs in *preset-category* mode**: it hides its Category row when the caller presets the category
   (the kebab path) and keeps the selector when nothing is preset (the summary's Add — otherwise that path would break).
5. **Q91(a) — the kebab is always rendered**; when the builder is disabled every item is disabled and carries
   `AudienceEditorDisabledReason` as its reason (the `RowActionsMenu` F7 treatment — reason becomes the item's `title`), and
   the reason paragraph stays **visible** (see change 2d). The separate disabled "Add target" button is deleted — it is an
   add button.
6. **Q92(a) — all five items open the dialog pre-scoped**, including Everyone (whose branch is a one-click confirm). The
   TGT-2 exclusive-clear rule therefore stays in the one place that already implements it
   (`OpenTargetsAndAudienceDialogAsync`'s merge), instead of gaining a second destructive path.
7. **Q93(a) — labels are verb-first**, reusing the dialog's own category labels: `Add grade levels`, `Add streams`,
   `Add students`, `Add activity groups`, `Add everyone`. The kebab trigger's `AriaLabel` is `Add a target` (mirroring
   Content & Resources' `Add a material`).
8. **Background removal (owner's first instruction, same message):** `PanelSection` must have **no background**.

### Expected files

1. `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/PanelSection.razor.css`
   — delete `background: #fafafa;` from `.panel-section` (border + radius stay). Extend the file's header comment to record
   that the hardcoded surface was removed and **why** (owner, 2026-10-17: a hardcoded off-white surface fights the theme).
2. `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentAuthoring.razor` — the
   **edit-view** Targets block only (currently lines ~532–561; the summary block at ~758–800 is NOT touched):
   a. Replace `<SectionCard Title="Targets & audience" …>` with
      `<PanelSection Title="Targets & Audience" TitleId="authoring-targets-title">`, whose `<HeaderActions>` holds the
      count chip (`<span class="authoring-targets-count">@_targetsAndAudience.Count</span>`) and
      `<RowActionsMenu UseMenuService="false" ForceKebab="true" Actions="@TargetActions" AriaLabel="Add a target" />`.
   b. `PanelSection` is a dumb container, so the body renders the list **inline** inside an explicit `<ChildContent>`
      (RZ9996): keep the existing `.targets-audience-item` / `__text` / `__label` / `__value` markup and the Remove
      `FluentButton` **verbatim** from the current `ItemTemplate`, wrapped in `@foreach (var entry in _targetsAndAudience)`;
      render the empty state when the list is empty.
   c. The empty-state copy must stop pointing at a button that no longer exists: `No targets yet. Click + to add.` becomes
      `No targets yet. Use the ⋮ menu to add one.` (the **summary** copy keeps its `+` wording — it still has an Add).
   d. Delete the disabled "Add target" `FluentButton` block (~lines 563–570) and, in its place, render the reason
      **visibly**: `<p class="authoring-reason" id="authoring-targets-reason">@AudienceEditorDisabledReason</p>` when that
      reason is non-null — mirroring the summary surface, so the reason is readable rather than tooltip-only.
   e. Add a computed `private IReadOnlyList<RowAction> TargetActions` next to `MaterialActions`, one entry per category, each
      with `Label` (pinned in decision 7), an `Icon`, `Disabled`/`DisabledReason` (from `!BuilderAddEnabled` /
      `AudienceEditorDisabledReason`) and `OnClick = () => OpenTargetsAndAudienceDialogAsync(<category>)`.
      **Icon rule: reuse icon types already referenced in this file** (grep `Icons.Regular.Size16.` in it) and only fall back
      to `FluentIcons.Add` if none fits — do not invent icon names (a wrong name is a compile error, and the repo's icon
      guidance is `.github/skills/fluentui-icons/SKILL.md`).
   f. `OpenTargetsAndAudienceDialogAsync()` gains a `TargetsAndAudienceCategory category` parameter; it passes the category
      into `BuildTargetsAndAudienceDialogModel()` (see change 3) and keeps its `!BuilderAddEnabled` early return and the
      whole TGT-2 Everyone merge block unchanged.
   g. **Do not move `id="authoring-targets"`**: it stays on the wrapping
      `<section id="authoring-targets" class="authoring-compartment authoring-compartment--right">` (line ~439), which the
      grid CSS (`.authoring-layout--summary > #authoring-targets`) and several bUnit assertions address.
3. `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/TargetsAndAudienceDialogModel.cs`
   — add a `CategoryLocked` (bool) property: the caller's statement that the category is already chosen.
4. `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/TargetsAndAudienceDialog.razor`
   — render the `Category` `FormRow` (`ta-category` `FluentSelect`) **only when `!Model.CategoryLocked`**. Nothing else in
   the dialog changes: the picked-chips zone, the per-category pickers, `OnCategoryChanged`, `OnValuePicked`, the student
   search and `SubmitAsync` all stay as they are.
5. Tests — `tests/SchoolCollab.Assignments.Tests.Unit/`:
   - `AssignmentAuthoringBunitTests.cs`: the shared helper that clicks `fluent-button[title='Add target']` must drive the
     **kebab** instead (open `fluent-button[title='Add a target']`, then activate the item by label through the repo's
     `MenuItemClick()` helper — v5 fires `FluentMenuItem.OnClick` on `onmenuitemchange`, not `click`; see
     `RowActionsMenuTests.KebabItem_InvokesItsAction`). The disabled-state test (~:3454) asserts **disabled kebab items** and
     the visible `#authoring-targets-reason` text, and **that no `fluent-button[title='Add target']` remains** on the edit
     surface. The single-title assertion (~:655) should now be tightened to `.panel-section__title` (it already tolerates
     `.panel-section__title`; the `SectionCard` variant is gone from this surface).
   - NEW tests (the round's real acceptance): (i) the kebab exposes all five categories with the pinned labels; (ii)
     choosing an item opens the dialog with `Model.Category` preset to it **and** `Model.CategoryLocked == true` (drive it
     through the existing dialog stub, which already records every `TargetsAndAudienceDialogModel`); (iii) the count chip
     renders the count inside `.panel-section__actions`.
   - `AssignmentCreateBunitTests.cs` (~:264, ~:295): same kebab drive.
   - `TargetsAndAudienceDialogTests.cs`: ADD a preset-mode test — the `ta-category` selector is **absent** when
     `CategoryLocked` and **present** when not (discriminating in both directions); the existing selector tests stay green
     because the summary path keeps the unlocked model.
   - `tests/SchoolCollab.ArchitectureTests.Unit/AssignmentGradeUiMarkupArchitectureTests.cs`: it reads
     `TargetsAndAudienceDialog.razor` as source text — if any of its assertions depend on the Category row being
     unconditional, update them to the new conditional shape.
6. `documents/specs/assignment-authoring-compartments.md` §7 (TGT-*) — amend the targeting-entry flow statements: the
   create/edit surface enters targets through the PanelSection header kebab (one item per category) with no Add button; the
   dialog's category selector appears only when the caller did not preset a category. Keep the spec's authority intact —
   stale statements about the in-dialog selector are worse than none.

### Acceptance criteria (parent-verified)

| # | Criterion |
|---|---|
| A1 | `dotnet build SchoolCollab.slnx` — 0 errors. |
| A2 | `dotnet test` on `SchoolCollab.Assignments.Tests.Unit`, `SchoolCollab.Admin.Tests.Unit`, `SchoolCollab.ArchitectureTests.Unit` — 0 failures. |
| A3 | The kebab renders **five** items with exactly the pinned labels, and activating one opens the dialog with `Model.Category` = that item's category and `Model.CategoryLocked` true. **Discriminating**: before this round there is no kebab on this surface at all and `Model.Category` is always the dialog's own default. |
| A4 | With `CategoryLocked` true the dialog renders **no** `ta-category`; with it false (default) the selector renders. **Discriminating in both directions** (the pre-change dialog always rendered it). |
| A5 | The disabled builder state renders **no** Add button, all kebab items disabled with the reason, and a visible `#authoring-targets-reason`. **Discriminating**: the pre-change path was a disabled `fluent-button[title='Add target']`. |
| A6 | `#authoring-targets` still occurs exactly once on the create/edit surface and still contains the panel title, the count chip and the `.targets-audience-item` / `fluent-button[title='Remove']` seams. |
| A7 | `PanelSection`'s CSS declares no `background` for `.panel-section`. |
| A8 | The **summary** surface is byte-unchanged (`FluentAccordion` + inner `SectionCard` + its `add` affordance). |
| A9 | No weakened assertions: every test the round touches either keeps its discriminating power or gains a stated reason for changing (the worker reports each test edit). |

### Reviewer's additional acceptance criteria (Tier 2)

- **R1** Plan conformance file-by-file, and **nothing outside the expected-files list** changed (especially: the summary
  surface, `SectionCard.razor` itself, `PanelSection.razor`'s parameter contract).
- **R2** The two ids that carry test/grid meaning are intact and correctly placed: `id="authoring-targets"` on the wrapping
  `<section>`, `TitleId="authoring-targets-title"` on the panel title.
- **R3** The v5 menu contract is honoured: items inside `<FluentMenuList>` (that component's own defect from the previous
  round — bare items under `<FluentMenu>` never hide), `Trigger` by element id, no `Anchor`/`UseMenuService` assumptions.
- **R4** Test edits are honest: no assertion weakened to pass, no `WaitForAssertion` timeout widened as a substitute for a
  settled cascade, and the new tests would fail against the pre-round markup.
- **R5** No dead code/CSS left behind (unused `.section-card*` overrides for this surface, orphaned `AddTitle`/`AddAriaLabel`
  parameters, the `:deep(.section-card__header)` hack if it was only for targets).

## Worker Report

Run `172f6192-2daf-4274-94c9-f3a7f3a5202f` (`ollama-cloud/deepseek-v4.1-flash`).

**Files changed:** the eight expected files (PanelSection.razor.css; AssignmentAuthoring.razor; TargetsAndAudienceDialogModel.cs;
TargetsAndAudienceDialog.razor; AssignmentAuthoringBunitTests.cs; AssignmentCreateBunitTests.cs; TargetsAndAudienceDialogTests.cs;
documents/specs/assignment-authoring-compartments.md). Nothing outside the plan's list.

**Build:** `dotnet build SchoolCollab.slnx` 0 errors. (First attempt failed with `CS0123: No overload for
'OpenTargetsAndAudienceDialogAsync' matches delegate 'Func<Task>'` — the summary binds the method group and C# does not
substitute an optional argument; fixed with a dedicated zero-arg entry point.)

**Tests (worker numbers; parent re-ran them):** Assignments 1171 total / 0 failed; Admin 699 / 0; Architecture 116 / 0.

**Test edits:** 12 items, each with a stated non-weakening rationale — the add path moves to the kebab drive; the
disabled-state test is *strengthened* to require zero Add buttons plus five disabled reason-titled items; the single-title
selector is *tightened* from `.section-card__title, .panel-section__title` to `.panel-section__title`; three new tests
(five pinned labels; item → preset `Category` + `CategoryLocked`; count chip in `.panel-section__actions`); a new dialog
test for preset-vs-unpreset. No `WaitForAssertion` timeout widened.

**Deviations:** (a) did **not** add the second `#authoring-targets-reason` paragraph the plan asked for — the id already
exists at `AssignmentAuthoring.razor:457` in the same section and a second one would duplicate the id and break an
existing `ContainSingle()` assertion; (b) `OpenTargetsAndAudienceDialogAsync(TargetsAndAudienceCategory?)` **plus** a
zero-arg entry point, keeping the summary markup byte-unchanged; (c) all five items share `FluentIcons.Add` (the plan's
documented fallback — no other proven name in this file fits a category, and an invented name is a compile error);
(d) `@key` omitted on the new loop (duplicate record keys are legitimate here and would throw); (e) the edit panel is
Title Case ("Targets & Audience") while the untouched summary accordion keeps "Targets & audience" — flagged.

## Review

Static diff review, run `aef2f3c4-540d-42b3-ae82-cae10f60123b` (`ollama-cloud/kimi-k2.7-code`), read-only, patch-scoped.

- **R1** plan conformance, file-by-file: **PASS** (exactly the eight files; `AssignmentGradeUiMarkupArchitectureTests`
  correctly untouched).
- **R2** the test/grid ids intact: **PASS** (`id="authoring-targets"` on the wrapping `<section>` at :439 edit / :776
  summary; `TitleId="authoring-targets-title"` at :543).
- **R3** the v5 menu contract: **PASS** — the kebab goes through the repo's `RowActionsMenu`, which wraps items in
  `<FluentMenuList>` and uses `Trigger="@_buttonId"`; the new test asserts the rendered shape.
- **R4** test edits honest: **PASS** — no assertion weakened, the title selector tightened, the disabled test strengthened;
  no timeout widened.
- **R5** no dead code/CSS left behind: **PASS**, one note (a dead Rules-era `:deep(.section-card__header)` rule, outside
  this round's targets scope).
- **Deviations:** all three accepted — (a) correct (a duplicate id would break a real assertion); (b) required for CS0123
  while keeping the summary byte-unchanged; (c) the plan's sanctioned fallback.
- **P1:** none. **P2:** (i) the dead `.authoring-rules-card :deep(.section-card__header)` rule in
  `AssignmentAuthoring.razor.css:240-243` (pre-existing, Rules-era); (ii) the new targets `@foreach`
  (`AssignmentAuthoring.razor:561-563`, and the neighbouring Rules loop at `:614-616`) omits `@key`, contrary to
  `.github/copilot/rules/blazor-components.md` "Any `@foreach` that renders child components or elements **must** include
  `@key`".

## Acceptance

**Verdict: CLOSED** (2026-10-17). Every criterion is met, the static review found **no P1**, and the four acceptance
residuals were put to the owner in one grill round (Q99–Q102) whose answers were then applied in a bounded rework.

| # | Criterion | Result |
|---|---|---|
| A1 | `dotnet build SchoolCollab.slnx` — 0 errors | **PASS** (0 errors). One earlier attempt failed with MSB3027/MSB3021 because **my own** live-probe dev loop held `SchoolCollab.Assignments.Application.dll`; the loop was stopped (my process, not the owner's) and the build re-run clean. |
| A2 | affected suites 0 failures | **PASS** — Assignments **1171/1171**, Admin **699/699**, Architecture **116/116**. |
| A3 | five pinned kebab labels; item presets the category | **PASS** — bUnit (labels asserted on `Actions` *and* on the rendered `fluent-menu-list fluent-menu-item` list; the recorded dialog model asserts `Category` + `CategoryLocked`) **and live**: the open kebab lists exactly Add grade levels / Add streams / Add students / Add activity groups / Add everyone. |
| A4 | preset mode hides the selector; unpreset keeps it | **PASS** — bUnit both directions, plus live: after choosing *Add students* the dialog renders **no** `#ta-category` and names the Students category. |
| A5 | no Add button; disabled items carry the reason; reason stays visible | **PASS** — bUnit (zero Add buttons, five disabled reason-titled items, single visible `#authoring-targets-reason`) and live (`addTargetButtonCount: 0`). |
| A6 | ids/seams/count chip | **PASS** — live: the Targets panel renders `authoring-targets-title`, the count chip **inside `.panel-section__actions`**, and the empty state; `id="authoring-targets"` remains on the wrapping `<section>`. |
| A7 | `PanelSection` has no background | **PASS** — live computed background `rgba(0, 0, 0, 0)`; the declaration is deleted in source. |
| A8 | the summary surface unchanged | **PASS** — reviewer R1/R5; only its accordion *heading text* was later aligned (Q101), which is copy, not structure. |
| A9 | test edits honest | **PASS** — reviewer R4: no assertion weakened, the single-title selector tightened, the disabled-state test strengthened, no timeout widened. |

**Plan defect caught by the worker and adjudicated by the parent** (recorded as a *plan* error, not a deviation): plan item 2d
asked for a second `#authoring-targets-reason` paragraph where the disabled Add button had been; `AssignmentAuthoring.razor:457`
already renders that paragraph inside the same `#authoring-targets` section, so the instruction would have duplicated the id and
broken an existing `ContainSingle()` assertion. The worker refused it, explained why, and the parent verified the claim at source.

**Owner decisions on the residuals (one grill round, Q99–Q102):**

| Q | Decision | Effect |
|---|---|---|
| Q99 | **(b) narrow the `@key` rule** | `.github/copilot/rules/blazor-components.md`: `@key` is required where a collection can reorder/insert/filter or when items own state; append-only purely presentational rows are **exempt, on the record**, with the reason stated in a comment on the loop. Grounds: a review measured **66 of 80 `@foreach` loops in `src/**/*.razor` unkeyed** — an unenforced "must" weakens every rule beside it, and a value key on a list that can legitimately repeat entries throws at render. Our loop carries the note (`AssignmentAuthoring.razor:561`). |
| Q100 | **(a) fix the prose, park the dead CSS** | The stale Rules comment and the `.razor.css` header now describe what actually renders; the dead `.authoring-rules-card :deep(.section-card__header)` rule is parked as backlog **§N.1**. |
| Q101 | **(a) align the casing** | The summary accordion's heading now reads **"Targets & Audience"**, matching the create/edit panel, and the assertion that pinned the lowercase form was updated (`AssignmentAuthoringBunitTests.cs:699`). |
| Q102 | **leave the icons as-is** | All five kebab items keep the shared `FluentIcons.Add` (the plan's documented fallback). Cosmetic; no backlog item. |

**Rework record.** Tier 2 allows ≤1 reviewer iteration; this round used it: 4 files edited by the parent (the `@key` rule, the
razor prose + exemption note, the CSS header comment, the test string) plus backlog §N, then the patch was re-frozen with the
extended pathspecs and every gate re-run green. No P1 was ever open, so no worker rework loop was needed.

**Residuals accepted:** both reviewer P2s are closed (one fixed at Q100, one parked as §N.1). Two environment observations —
the hung first launch (Aspire `dcp` holding port 7300 with an idle listener, two container generations present) and the
ArchitectureTests `total: 0 / Failed!` seen only when the three suites run chained in one shell — are recorded, with their
triggers, as backlog **§N.2**. Nothing in this round is committed; the owner has not authorised a commit.
