# FluentUI v5 residual backlog

**Why this file exists.** The FluentUI Blazor 4.14.2 → 5.0.1 upgrade (`documents/rounds/round-fluentui-v5-rounda.md`,
`-roundb.md`) deliberately left work behind. That work was recorded only in round docs, and `documents/rounds/` is
**ephemeral by design** — the folder is documented as safe to bulk-trash once rounds close (`documents/rounds/README.md`).
Anything worth keeping therefore has to live here. The round docs link here; this file is the durable list.

Status at the time of writing: Round A (main solution + MAUI head) and Round B (rename families + authored breadcrumb) are
delivered as the registered stack **#357** — `main ← fluentui-v5 (#355) ← s2-fluentui-v5-roundb (#356)`.

---

## A. Pre-existing inert attributes (~66 occurrences, NOT caused by the upgrade)

These attributes are passed to FluentUI components that do not declare them — and **did not declare them in v4.14.2
either** (verified by grepping the 4.14.2 XML for each). They are captured by `AdditionalAttributes` and render as
meaningless HTML attributes: no warning, no error, the feature simply does nothing. They predate the upgrade, which is why
they are backlog rather than regressions.

| Attribute | Sites | v5 mechanism to use instead |
|---|---|---|
| `FluentStack.Spacing` / `.Gap` | 15 + 5 | `HorizontalGap` / `VerticalGap` (the real v4 *and* v5 names — `Spacing`/`Gap` never existed) |
| `FluentIcon<T>.Icon` | 11 | `Value` |
| `FluentButton.AriaLabel` | 8 | the native `aria-label` attribute |
| `FluentCheckbox.HelpText` | 2 | `Message` / `LabelInfo` |
| `FluentDataGrid.AriaLabel` | 2 | the native `aria-label` attribute |
| `FluentDataGrid.Resizable` | 1 | `ResizableColumns` |
| `FluentRadio.Group` / `.Checked` / `.CheckedChanged` | 3 | wrap in `FluentRadioGroup`; `Value` / `ValueChanged` |
| `FluentAutocomplete.AutoComplete` | 2 | no such parameter in v4 or v5 — the native `autocomplete` attribute if the intent was browser autofill |

**Method for any future sweep** (and the reason this list can be trusted): enumerate each component's `[Parameter]`
properties from the **installed** package XML, resolve the **inheritance chain** — inherited members are legitimate and
must not be reported as unknown (`FluentTextInput : FluentInputImmediateBase<string?>` legitimately carries `Immediate` /
`ImmediateDelay`, and a false "inert" report is as damaging as a miss) — then check the *previous* version's XML: present
there means this upgrade broke it, absent there means it belongs in this table. The full method lives in
`.github/copilot/rules/blazor-components.md` § "Removed FluentUI *parameters* are silent".

## B. Pre-existing defects

- **`src/AI/SchoolCollab.AI.Chat/Components/AiChat.razor:115`** — `Disabled="@_isStreaming"` on a `<FluentLink>` is inert;
  v4.14.2's `FluentAnchor` had no `Disabled` either, so the streaming state has never disabled that control. Minimal fix:
  guard `ClearAsync` on `_isStreaming` (no visual change), or convert the control to a `FluentButton` (which does declare
  `Disabled`).

## C. Deferred: the dialog-shape leg (owner decision Q54 → folds in the Q43–Q45 work)

The approved "merge the dialogs onto one shell" could **not** be separated from form ownership: `DataAnnotationsValidator`
requires an ambient `EditContext` and `DialogShellFooter` submits with `Type="Submit"`, so a shell that owns the validator
and footer must own the `EditForm`. Doing the shell as a pure wrapper first would touch all 42 dialogs twice, so the whole
thing was deferred to one coherent leg, planned as its own round **with a plan review** (it is behavioural, not mechanical):

1. `DialogShell` owns `EditForm` **and** `dialog-container` **and** `DataAnnotationsValidator` **and**
   `DialogShellFooter`, taking the fields as content (`Model` / `OnValidSubmit` as parameters).
2. The five `*FormFields` components (`StudentFormFields`, `PeriodFormFields`, `GradeLevelFormFields`,
   `GuardianFormFields`, `TopicFormFields`) become form-free — they render fields against the ambient `EditContext`,
   which `FluentField.CurrentEditContext` already supports.
3. Their ~10 page consumers declare their own `EditForm` (`PeriodUpsert`, grade-level `Create`/`Edit`, the topic and
   guardian call sites), because those same components are shared between dialogs and pages.
4. The four component-scoped `dialog-container` overrides move to shared rules (`CodedValueDialog.razor.css:5`,
   `AttributeDefinitionDialog.razor.css:2`, `NotificationPolicyFieldEditDialog.razor.css:4`, `TopicEditDialog.razor.css:1`)
   and `QuestionPromptDialog`'s second class (`dialog-container cq-prompt-dialog`) becomes a shell parameter.
5. `ReadOnlyDialogShell` stays separate; `DialogChromeArchitectureTests` moves from `<DialogShellBody>` to `<DialogShell`;
   `.github/skills/dialog-ui/SKILL.md` is rewritten for the new shape.
6. **Per-surface validation tests are mandatory** (invalid submit blocked; valid submit reaches the API) — a mistake here
   fails *silently*, so a green suite proves nothing.

## D. The deferred UI-tester pass (owner decision Q51/Q57) — visible deltas to bug-hunt

1. Every confirm dialog now renders a **title** (`ConfirmDialog` was the one dialog not wrapped in `DialogShellBody`;
   v5 renders the header only through `FluentDialogBody`) — ~25 call sites.
2. Folder-tab chrome re-pointed to the real v5 DOM, with the **v5-native** active indicator (v5 exposes no
   `::part(activeIndicator)`) — eyeball the look.
3. **45 anchors converted** from inert elements to `FluentLink`: 30 `Hyperlink`→`LinkAppearance.Default`, 14
   `Lightweight`→`LinkAppearance.Subtle` (the `Subtle` set is the most likely to look different), and one accent-styled
   create control which is now a `FluentButton` (`ButtonAppearance.Primary`).
4. Inputs and dropdowns are unchanged in look but now render inside a **`<fluent-field>` wrapper** — v5 puts `class`/width
   on the wrapper (the W1–W9 ladder was made deterministic for this).
5. The **nav** (rebuilt on `FluentNav`/`FluentNavItem`/`FluentNavCategory`, with `IconRest`) and the **authored
   breadcrumb** (real `FluentLink` crumbs + `aria-current`).
6. The **MAUI head** is ported but has had no runtime/visual validation at all.

## E. v4-era durable docs to update (owner decision Q62: after the dialog-shape leg)

Update only the docs that describe **current** code: `documents/solution/dialog-parameter-binding.md` (12 v4 references)
and `documents/specs/dialog-parameter-binding-docs-followup.md` (12), `documents/solution/dialog-consolidation-plan.md`
(21), plus the specs citing `DialogSize`-era APIs (`teacher-edit-dialog-modernization.md` 9, `grade-detail-modern-ui-plan.md`
7, `landing-page-wrapper.md` 8, `fluentui-dead-selectedvalues-binding.md` 6). **Exempt:**
`documents/solution/fluentui-v5-upgrade-path.md` (30 references) — v4 names are that document's subject matter; it is the
migration research record, not stale guidance.

## F. Housekeeping

- `documents/rounds/` held **205 files / 18 MB** of ephemeral round material when this was written. Once the stack is
  merged it is safe to clear, per `documents/rounds/README.md` — but this backlog file must survive it.
- The two adopted-but-unimplemented feature specs (`assignment-approval-workflow.md`,
  `assignment-notification-templates.md`) were **untracked** — one `git clean` from losing the source of truth for two
  features. They are being committed in a standalone specs PR, deliberately outside the FluentUI train.
- The `Mobile (MAUI)` CI job skips unless the repository variable `ENABLE_MAUI_CI` is `true`; the manual build is
  documented in `AGENTS.md` § Build verification. Enabling the gate is an owner decision (Q55).

## G. Obsolete-enum usage still emitting `CS0618` (found 2026-10-16 while starting the dev loop)

The v5.0.1 package keeps the old appearance/colour values as **obsolete** members, so a v4-era spelling still compiles and
still renders — it just warns. A full non-incremental build shows the pattern in at least these shapes:

- `Appearance.Accent` / `Appearance.Neutral` on `FluentButton` (`ActivityGroupDetails.razor:154`, `ActivityGroups.razor:126`,
  `Students/Detail.razor:1029`) — obsolete in favour of `ButtonAppearance.Primary` / `.Default`.
- `Color.Accent` on icons/badges (`TeacherEditDialog.razor:40,109`, `GuardianSetupWizard.razor:39,93,118,174`,
  `GuardianGrid.razor:149`, `ResourcesSection.razor:36,82,136`) — obsolete in favour of `Primary`.

This is a different defect class from the tables above: the attribute is **valid and works**, it is merely deprecated.
So it is cosmetic/forward-compatibility work (a future v6 will likely remove the members), not a silent break — but it is
the same *class* of risk, and the fix is mechanical (`ButtonAppearance.*` / `BadgeColor.*` / `Color.Primary`).

**Why this matters for the rule:** `.github/copilot/rules/blazor-components.md` teaches the sweep for attributes that no
longer exist. Its mirror image is this: attributes that still exist but are **obsolete**. A build with warnings visible is
the detector — which is one more reason the warning count should be treated as signal here rather than noise.

## H. `FluentWizard` receives step children directly — pre-existing defect, VERIFY before fixing

Found 2026-10-16 by the Round C same-class sweep (Q68), and adjudicated by the parent as **pre-existing, not a v5
regression** — which is why it was not fixed in Round C.

`src/Students/SchoolCollab.Students.Application/Components/Pages/Guardians/GuardianSetupWizard.razor:26` places
`<FluentWizardStep>` elements **directly** inside `<FluentWizard>`. The v5 API declares
`[Parameter] public RenderFragment? Steps` whose own doc says *"Add WizardStep tags inside this tag"*, and the library's
canonical example nests them as `<FluentWizard><Steps><FluentWizardStep …>`. `FluentWizard` declares no `ChildContent`.

**This is not a v5 regression:** v4.14.2's `FluentWizard.razor` renders the same `@Steps` fragment
(`<ol …>@Steps</ol>`), so our usage did not satisfy the API before the upgrade either. It is the same *shape* as the
`FluentProviders` defect (children handed to a component that does not read them), which is exactly why the sweep surfaced
it — but a different *cause*: ours predates the port.

**Verify before fixing — one bUnit render of `GuardianSetupWizard` answers it.** Two outcomes are possible and they need
different fixes: (a) the steps do not render at all (the stepper is empty, navigation dead) → wrap them in `<Steps>`;
(b) Blazor captures the child content via `CaptureUnmatchedAttributes`/`AdditionalAttributes` and splats it, so the step
*bodies* render while `_steps` stays empty and the stepper/navigation logic is still dead → the same `<Steps>` fix, but
the symptom seen by the user is different. No bUnit test covers this component today; the only witness is
`tests/SchoolCollab.Students.Tests.Playwright/Tests/GuardianAdminTests.cs`, which Round C did not run.

## I. `ConfirmDialog`'s title is statically verified but untested (found 2026-10-16, Round C)

The Round C UI-tester leg could not exercise delta 1 (does a confirm prompt render a title?) because **the dev tenant's lists
are empty** — no rows, therefore no destructive buttons to press — even though `ShowConfirmDialogAsync` has **46 call sites**
under `src/`. The chain is nevertheless correct **by inspection**: `ConfirmDialog.razor` wraps its body in
`<DialogShellBody>`, which is literally `<FluentDialogBody>@ChildContent</FluentDialogBody>` — the only place v5 renders the
dialog header/title slot and the close (X) — and `ShowConfirmDialogAsync`/`BuildShellOptions` set `Header.Title` and
`CloseAction.Visible = true`.

**Gap:** no test asserts any of that. A `ConfirmDialog` render test asserting the title reaches the DOM (the same
"placement, not presence" shape as `RoutesShellRenderTests`) would convert a static claim into a tested one, and would
have caught the original omission at build time. Low cost, real value.

## J. Cosmetic `fluent-field` wrapper cluster (found 2026-10-16, Round C tester)

Not visibly broken, deliberately deferred rather than re-opening form layout after the round's gates went green:

- `fluent-field` wrappers still carry the legacy width classes (`w-4`, `w-9`, `coded-value-dropdown`, `fluent-datepicker`),
  so the v5 wrapper's sizing is *additive* alongside the old classes rather than owning width.
- 1–2 `fluent-field` wrappers per create form have `width: 0; height: 0` with only a `div` child — empty placeholders in the DOM.
- No co-located label text inside `fluent-field` — several fields rely on the input's `placeholder` as their label, which is
  also an accessibility weakness (a placeholder is not a label).

The last item is the one worth promoting first: it is an a11y issue, not just tidiness.

## K. The nav marks more than one item as the current page (found 2026-10-16, Round C tester)

On `/students/create` **three** nav items carry `aria-current="page"`: `New Student` (`/students/create`, correct), plus
`Students` and `All Students`, **both pointing at `/students`**. `aria-current="page"` should identify the current page only;
marking a parent (or two entries sharing one href) is announced to screen readers as several "current page" items.

Two observations bundled here: (1) active marking follows the href prefix, so a parent link stays "current" on child routes;
(2) `/students` appears **twice** in the nav, which may be intentional (a category landing link plus a list link) but reads as a
duplicate. Round C verified the nav renders, highlights the active item and carries `aria-current` — this is a refinement, not
a break, which is why it is backlogged rather than fixed inside the repair round that touched the router.

## L. Test-suite stability observations (from the Round D verification loop, 2026-10-16)

Surfaced by the 12-iteration whole-suite Release loop that Round D ran in a 2-CPU Linux container
(`--filter FullyQualifiedName~Tests.Unit`, 3951 tests). **None of these is caused by Round D** — that round's diff is a single
test file, and each item below lives in a different file or a different test project.

**Fixed by Round D (the reason the loop ran):** `Edit_RemovingAnActivityGroupEntry_SendsTheReducedTargetSetAndLinks` — the
subject pick was not settled, so a FluentUI `Items` rebuild could drop it, leaving `_selectedSubject` null and the save guard
bailing with no PUT (the CI `NotBeNull` on `updateBody`). Now routed through the hardened `SelectAsync`. Result: **0 failures
across 12 iterations** of the loaded whole-suite run that produced the original failure.

**Deterministic, pre-existing, container-only: `AutoGraded_BindToModelEchoesValues`**
(`tests/SchoolCollab.Assignments.Tests.Unit/ScoringFieldsSectionBunitTests.cs:108`) — fails **12/12** inside the whole-suite
container run (~1.0s each), but passes when run alone and passes when its own assembly runs alone (1167/1167). An A/B run of
the identical command with Round D's diff reverted reproduces the failure exactly (`failed: 1`, 3950 succeeded), so it is not
Round D's. CI has not exhibited it (all three stack layers are green). Signature suggests cross-assembly interference or a
container-environment difference rather than a product defect. **Next step if it bites CI:** reproduce with the flake skill's
loop and check for shared static/ambient state across the unit assemblies.

**Two one-off failures, 1/12 each, both outside the Round D surface:**

| Test | File | Duration | Note |
|---|---|---|---|
| `Create_UnsetReviewPolicy_SubmitsTheAuthorsReviewChoice` | `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentCreateBunitTests.cs` | 15.1 s | this is the very file the flake skill names as carrying the case-1 diagnostic wrapper (`_error` dump) — the longest-running failure in the loop, so a contention/cascade shape is suspected |
| `CodedValueDropdown_WithSelectedId_MarksOptionSelected` | `tests/SchoolCollab.Settings.Tests.Unit/Components/CodedValueDropdownTests.cs` | 59 ms | a shared-component test; `CodedValueDropdown`'s clear-on-stale-selection contract was touched in Round A (Q31), so a late-cascade interaction is plausible |

**Recommended follow-up:** run each through `fix-flaky-bunit-fluentui-after-cascade` before it red-lights a PR — in an atomic
`gh stack` train a single red test in any layer blocks the whole merge, which is exactly how the Round D work started.
