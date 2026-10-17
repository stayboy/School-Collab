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
