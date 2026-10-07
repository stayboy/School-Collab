# Assignment authoring — Content & Resources + Questions & AI modern UI (implementation record)

**Status:** Implemented 2026-10-07 — R1 + R2 shipped, **except View-mode read-only chrome (D8/VM-1/VM-2), deferred** (see §3).
**Branch:** `feat/authoring-content-questions-modern-ui` (uncommitted on `main`'s tree at time of writing)
**Spec:** `documents/specs/assignment-authoring-content-questions-modern-ui.md`
**Mode:** Solo (owner instruction).

## 1. What shipped

| Spec area | Implementation |
|---|---|
| §3 design language (DL) | `--cq-*` custom properties declared once on `.authoring-layout--cq` in `AssignmentAuthoring.razor.css`; all children reference them with fallbacks. Tokens only — no hex literals. Motion guards on every transition. |
| §4 layout (LA) | New `authoring-layout--cq` row: grid **areas** put Questions & AI in the main column and Content & Resources in the rail while DOM order stays `content → questions`. Rail track shared with row 1 (`minmax(320px, 460px)`); collapses to one column at 1100px. |
| §5 Content & Resources (CR) | `ResourcesSection` rewritten as the Context card: `cq-header` (h3 + count + `+`), the drop tile (icon composition + helper text + the `FluentInputFile`'s own "Upload files" trigger, drag-drop zone), always-rendered **Files** / **Links** group heads (`N file(s) attached`, `N link(s)`), two-line file rows (name / `size · badge` + Re-read/remove), link rows, and a link input revealed by `+`. |
| §5 CH (shared Chip) | `Chip.razor` gains an **opt-in** selectable mode (`Selected` + `OnToggle` → `<button aria-pressed>`), `.chip--toggle` two-state look. Existing badge/dismiss modes untouched. |
| §6.1 composer (QA-1…QA-7) | `QuestionGenerationSection` rewritten as the composer: label-less hero prompt with a visually-hidden label, footer row of three type **chips** (shared `Chip`), inline count/difficulty fields, trailing Generate, "all off" helper line, warning bars. |
| §6.2 list + dialog (QA-8…QA-17) | `QuestionEditorSection` rewritten as the single-line row list (`#cq-qrow-{i}`, text as a `FluentAnchor` open target, type badge, `RowActionsMenu` kebab with **Edit** / **Remove** (destructive → shared confirm), revealed on hover/`focus-within`). New `QuestionEditDialog` (`DialogShellBase`, `DialogSize.Medium`) holds the per-type fields verbatim and edits a detached working copy. `QuestionEditModel` / `QuestionEditResult` in `QuestionEditDialogTypes.cs`. |
| §6.3 draft (QA-18…QA-21) | `QuestionsDraftSection` gains a "Draft preview" disclosure (`aria-expanded`/`aria-controls`), auto-expanded when a draft is staged, never auto-collapsed. |
| §7/§9 cleanup | `QuestionReviewList.razor` + `.razor.css` + `QuestionReviewListBunitTests.cs` deleted (dead code — nothing in `src` rendered it). |

## 2. Verification

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | 0 errors |
| `SchoolCollab.Assignments.Tests.Unit` | 914 / 914 |
| `SchoolCollab.Admin.Tests.Unit` (shared `Chip`) | 626 / 626 |
| `SchoolCollab.Students.Tests.Unit` | 624 / 624 |
| `SchoolCollab.Settings.Tests.Unit` | 539 / 539 |
| `SchoolCollab.Core.Tests.Unit` | 126 / 126 |
| `SchoolCollab.ArchitectureTests.Unit` | 102 / 102 |

New/changed tests: `QuestionEditDialogBunitTests` (new — dialog shell harness, per-type fields, Add option, Cancel-discards-copy), `QuestionEditorSectionBunitTests` (row-shape + kebab Remove tests replace the three per-type tests), `QuestionsDraftSectionBunitTests` (disclosure fixture + collapsed-default test), `ResourcesSectionBunitTests` (two URL tests now reveal the link input through `+`).

## 3. Deviations and deferrals

1. **View-mode read-only chrome deferred (D8/VM-1/VM-2).** View mode keeps today's one-line hints. Implementing the read-only rows would have had to change three deliberate invariants — `Edit_LaterStatus_DegradesToReadOnlyView` and `View_LaterStatus_RendersAllCompartmentsReadOnly` assert `FindComponents<QuestionEditorSection>()` / `<ResourcesSection>()` are **empty** in View mode — which is a behaviour change outside the approved "presentation + small affordances only" fence (D7). Needs its own decision. The dead `QuestionReviewList` was deleted regardless.
2. **QA-13:** Add appends a blank row **without** auto-opening the dialog — exactly today's behaviour, and it keeps the existing add test free of a dialog host.
3. **CR-3:** `FluentInputFile.AnchorId` was **not** used. The component's own "Upload files" button remains the trigger (no JS, no hidden-element click) inside the tile that is the drag-drop zone — same user-visible behaviour as the spec's intent, without relying on an unverified `AnchorId` path in 4.14.2.
4. **`#cq-qrow-{i}-actions`** is a wrapper `<span>` around `RowActionsMenu` (the component exposes no `Id`). Kebab items render inside `FluentMenu` and are found in tests only after the trigger is clicked.
5. **Toggle-chip pointer fallback:** the row kebab is kept visible under `@media (hover: none)` so touch users are not locked out.

## 4. Follow-ups

- Implement D8/VM-1/VM-2 (read-only chrome in View mode) with the three invariant tests rewritten to assert *read-only* rather than *absent*.
- Deduplicate the composer/draft-panel generation controls (spec §9 non-goal, QA-20).
- Manual light/dark + keyboard pass on `/assignments/{id}/edit` and `/assignments/create` (spec §12) — not run in this session; no browser was driven.

---

# Round R3 — prompt-builder dialog (implemented 2026-10-07, Solo round)

Spec: `documents/specs/assignment-authoring-content-questions-modern-ui.md` §6.1/§6.1a (QA-1…QA-23, PB-1…PB-11).
Executed **Solo** by explicit owner election (D28), after a three-pass independent spec review on
`ollama-cloud/glm-5.3` (which caught two blockers, six majors and a factually wrong `EnumHelper`
claim before a line of code was written).

## 5. What shipped (R3)

| File | Change |
|---|---|
| `Helpers/QuestionPromptComposer.cs` | **New.** The deterministic PB-4 skeleton: `Compose`, `ComposeSummary`, `IsTemplateMatch`/`TryParse` (strict, complete-structural-match), `FormatGrades`, `TypeName`, `NormalizeTypes`, plus `QuestionPromptNarrativeInputs` / `QuestionPromptKnobs`. Single predicate source for PB-5, PB-6 and QA-23; no AI call, no transient marker. |
| `Components/Pages/Assignments/QuestionPromptTypes.cs` | **New.** `QuestionPromptModel` (working copy + read-only context) and `QuestionPromptResult` (knobs only — no prompt text, see deviation 1). |
| `Components/Pages/Assignments/QuestionPromptDialog.razor` (+`.css`) | **New.** PB-1…PB-11: read-only Subject / Grade level / Grounding preview, the count + three difficulty fields (EC-10 `min="1"` now lives here), the three selectable type chips, the all-off note, the `PromptLocked` explanation, `DialogSize.Medium`, `SubmitText = "Apply"`. |
| `Components/Pages/Assignments/QuestionGenerationSection.razor` (+`.css`) | Knobs removed from the composer (QA-22); `#cq-config-summary` + `#cq-configure` + `#cq-configure-reason` added; `OpenPromptDialogAsync` (PB-6 defaults), `ApplyPromptResult`, `WriteComposedPromptAsync` (PB-5), `RefreshComposedPromptIfStale` (QA-23). New `TargetGradeLevels` parameter. |
| `Components/Pages/Assignments/QuestionsDraftSection.razor` (+`.css`) | PB-11: keeps its inline controls (QA-20) and gains `#cq-draft-config-summary` + `#cq-draft-configure`; composes into its own transient `_prompt` and never dirties the form. |
| `Components/Pages/Assignments/AssignmentAuthoring.razor` | New `TargetGradeLevels` computed property (from `KnownGradeTargetIds` + the already-loaded `_gradeLevels` — no new API call), passed to both composer call sites and the draft section. |
| `Assignments.Contracts/ContractTypes.cs` | `[Description]` on `QuestionTypeDto` — **attributes only**; the wire shape is unchanged (enum serialization is name/number, unaffected by metadata) and the existing wire-name assertions still pass because they read `ToString()`. |
| `SchoolCollab.Admin.Shared/Components/Chip.razor` | **Defect fix found by the new tests.** `aria-pressed="@Selected"` passed a `bool`, and Blazor renders a bool attribute as a **bare/empty attribute** when true and omits it when false — so the shipped toggle emitted `aria-pressed=""` (invalid ⇒ screen readers read *not pressed*) and could never report `true`. Now `aria-pressed="@(Selected ? "true" : "false")"`, matching the draft disclosure's pattern (X-7). |

## 6. R3 verification

| Suite | Result |
|---|---|
| `QuestionPromptComposerTests` (new) | **23 / 23** |
| `QuestionPromptDialogBunitTests` (new, real `IDialogService` + `FluentDialogProvider`) | **9 / 9** |
| `QuestionGenerationSectionBunitTests` (+3: summary, QA-23 refresh, hand-written untouched) | **22 / 22** |
| `SchoolCollab.Assignments.Tests.Unit` (whole project) | **937 / 937** |
| `SchoolCollab.Admin.Tests.Unit` (the `Chip` fix) | passed |
| `SchoolCollab.ArchitectureTests.Unit` | passed |
| Full `dotnet test` | 3574 passed, 4 failed — **all 4 environmental/flake**: 3 × `CodedValueAIServiceLiveTests` (`ChatAsync_WithOpenRouter_*`, live network, fail in isolation, pre-existing) and 1 × `Create_UnsetReviewPolicy_SubmitsTheAuthorsReviewChoice` (passes in isolation — bUnit `WaitForAssertion` timeout under full-solution load, the same flake class as `Renders_Preload_ExistingDraft`) |

`dotnet build SchoolCollab.slnx` — 0 errors throughout.

## 7. R3 deviations

1. **PB-5's confirm runs caller-side**, immediately after the dialog closes, instead of inside it. `dialog-ui` forbids nested dialogs and a `ConfirmDialog` inside a shell dialog is exactly that; no such nesting exists anywhere in the repo (`ShowConfirmDialogAsync` is only ever called from page/section components). A declined confirm still applies the author's **knob** edits — only the text write is skipped. Spec PB-5 updated to record this.
2. **The composer no longer renders the "No type selected — the AI will choose a mix." note** — it moved into the dialog (PB-2). QA-22 says the composer shows only the summary and Configure, and the summary already surfaces `Balanced mix`. That string is not in the X-6 preserved list.
3. **`[Description]` landed on `QuestionTypeDto` (Contracts)**, not the domain `QuestionType`: every UI label already renders through `EnumHelper.GetDescription` on the `*Dto` enums, and the domain enum has no Application-layer call sites at all. `ContractTypes.cs` already carried 31 such attributes and every sibling enum has them, so this is the consistent repair.
4. **The EC-10 `min="1"` assertion moved** from `QuestionGenerationSectionBunitTests` to `QuestionPromptDialogBunitTests` — chrome-necessary, per §10's allowed-rework list.
5. Difficulty is read on the draft surface from its **own transient** knobs, not the persisted assignment counts (PB-6(i) is composer-only) — recorded as PB-11's closing clause.

## 8. R3 follow-ups

- Manual light/dark + keyboard pass (spec §12) — not run; no browser was driven this session.
- The composer and the draft panel still duplicate their composition plumbing (`OpenPromptDialogAsync` / `WriteComposedPromptAsync`) — deduplication remains the §9 non-goal (QA-20).
- R4 (§6A) is specced and independently reviewed as **ready**, with its migration/contract plan carried to its own Tier-3 round.

---

# Grill round 4 outcomes (2026-10-07)

Six open decisions closed with the owner (recorded as **G44–G49** in the spec):

| Decision | Outcome |
|---|---|
| G44 commit granularity | **one commit** for R1–R3 (the default squash-merge makes a multi-commit PR history moot) |
| G45 keyless live tests | **out of this PR** — and, after diagnosis, **no fix was needed**: the self-skip guard already works. See §10 (corrected) |
| G46 `Chip` coverage | **`ChipBunitTests` added** + CH-1 and the §10 claim corrected |
| G47 the load-flaky draft-preload test | `WaitForAssertion` budgets raised from bUnit's 1s default to an explicit 5s |
| G48 D8/VM read-only chrome | **withdrawn** — a recorded non-goal of this spec |
| G49 the manual light/dark + keyboard pass | runs **after** the PR opens |

## 9. Additions in this round

- **`tests/SchoolCollab.Admin.Tests.Unit/ChipBunitTests.cs`** (new, 6 tests) — pins `aria-pressed` to
  the literal `"true"` / `"false"` in *both* states, the toggle callback, the two-state class, and
  that the badge / dismissible renderings are unchanged. There was **no** `Chip` suite before this,
  which is precisely why the R2 defect survived: the spec's §10 claimed CH-1…CH-3 coverage that was
  never written, and CH-1 itself prescribed the buggy binding.
- **`QuestionsDraftSectionBunitTests`** — the three initial-load waits (the shared fixture's two, plus
  `Renders_Preload_ExistingDraft`'s) now carry an explicit 5s budget instead of bUnit's 1s default,
  removing the load-sensitive timeout that failed once under a full-solution run.

## 10. The live OpenRouter tests — corrected diagnosis (G45)

**The original entry here was wrong; corrected 2026-10-07.** It claimed the missing-secret self-skip
was broken and needed fixing. The verified facts:

- **The self-skip already works.** `CodedValueAIServiceLiveTests.LoadOpenRouterSettings` calls
  `Assert.Inconclusive` when the key is empty (`CodedValueAIServiceLiveTests.cs:85-91`) — exactly what
  the workflow comment describes.
- **CI therefore never exercises the live provider**, despite `ci.yml`'s comment claiming it does.
  `ci.yml:55` injects `OpenRouter__ApiKey`, the name the **AI host** reads
  (`AI.Server/Program.cs:32`, fed by `AppHost/Program.cs:308`), while the **tests** resolve
  `Parameters:openrouter-api-key` (user secrets / `Parameters__openrouter_api_key`). Different key
  path ⇒ empty key in CI ⇒ every live test self-skips as Inconclusive. That is why CI is green.
- **Locally the failures are a live provider rejection, not a missing secret.** With a real key
  configured the test calls OpenRouter and receives **HTTP 400 Bad Request**, which the skip
  classification (timeout / transient / rate-limit / connection, lines 492-497) does not cover. The
  pinned model is hard-asserted at line 104 (`google/gemma-4-31b-it`, from the AppHost's
  `openrouter-default-model`), so a stale model entitlement is the likely cause.

All three tests fail in isolation on untouched code, so none of this is related to R1–R3. Left as-is by
owner decision: the area is environmental, and wiring the CI secret to the test-facing name would turn
CI red on the same 400. `ci.yml`'s misleading comment is corrected in place so the misconception cannot
spread.
