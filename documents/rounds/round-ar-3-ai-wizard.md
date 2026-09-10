# Round ar-3-ai-wizard — Wizard AI question generation UI + paginated question editor + Review paging (B1 phases 7–8)

Provider: pi (models: glm-5.3, minimax-m3, kimi-k2.7-code, deepseek-v4-flash [UI round - tester fires])

- **Tier:** 3 — full four-agent round **plus a UI-tester pass**. Worker model: minimax-m3
  (30-minute run cap — this is the largest UI surface of the train so far; the steps below
  are deliberately tightly sequenced: form model first, then sections, then review paging,
  then tests; build after every step; if the clock runs short, land steps in order and
  report deviations rather than improvising).
- **Round base:** HEAD `4360ff52b5e00d358c9ff52382f960cfb346f959` on branch
  `stack/3-ar-3-ai-wizard` (stacked on PR #219 which is stacked on PR #218; GitHub stack
  #220).
- **Pre-round dirty paths OUT of round scope** (round patch is pathspec-limited to `src`
  and `tests`): `documents/rounds/round-period-upsert-single-page.md` |
  `documents/rounds/diffs-period-upsert-single-page.patch` |
  `documents/rounds/.ar-1-scope.txt` | `documents/rounds/.ar-1-worker-report.md`.
  (Full tree truth at round start: two further untracked leftovers
  `documents/rounds/.ar-2-scope.txt` + `documents/rounds/.ar-2-worker-report.md` also sit
  outside the pathspec — no tracked file is modified.)
- **THIS IS A UI ROUND:** after the worker + parent verification, the accept run derives
  the tester-scope handover from the changed-file list and a UI-tester pass
  (deepseek-v4-flash) fires. **The expected-file list below is that handover's basis.**
- **Rounds 1–2 landed what this round consumes:** contracts `QuestionTypeDto`,
  `NewQuestionDto`/`NewQuestionOptionDto`/`NewAttachmentDto` (with `ModelAnswer`),
  `CreateAssignmentRequest`/`UpdateAssignmentRequest` with optional trailing
  `AiPromptOverride`/`Questions`/`Attachments`; server-side validation
  (`QuestionOptionDtoValidator` → `AssignmentQuestionValidationException`); the generator
  seam `IAssignmentQuestionGenerator` + `AssignmentQuestionGenerator` (HTTP) +
  `QuestionGenerationRequest`/`GeneratedQuestionDto`/`GeneratedQuestionType` in
  `SchoolCollab.AI.Abstractions` + typed `QuestionGenerationFailed`
  (`src/Assignments/SchoolCollab.Assignments.Application/Services/`). Note:
  `AssignmentsApiClient._jsonOptions` still lacks a
  `JsonStringEnumConverter<QuestionTypeDto>` — round-1 review flagged the ApiClient
  converter set as round-3 scope (Scope item 7 below).
- **Sources:** `documents/specs/assignment-creation-with-ai.md` §0 (decisions 1, 5, 8, 9,
  10), §3.5, §6, §7, §8 (EC-1..EC-5, EC-8, EC-10), §9, §10.7–10.8, §11;
  `documents/solution/assignment-request-implementation-details.md` §1.8 (UI inventory),
  §2 WS-B1, §3 round log (rounds 1–2 outcomes + carried follow-ups, incl. the ar-1
  lesson: the repo rule wins over plan text);
  `documents/solution/dto-form-model-mapping.md`; `.github/copilot/rules/blazor-components.md`;
  `.github/copilot/rules/dotnet-best-practices.md`; `.github/skills/dropdown-ui/SKILL.md`,
  `.github/skills/fluentui-icons/SKILL.md`, `.github/skills/fluentui-component-props/SKILL.md`.

## Plan

### Goal

Execute **phases 7–8 only** of `documents/specs/assignment-creation-with-ai.md` §10 (plus
its §11 UI/component tests): evolve the existing 3-step wizard (`Create.razor`, 755
lines) so a teacher can AI-generate questions for a qualifying assignment, edit them in a
paginated type-aware editor, review them read-only (paged) with a submit gate, and submit
everything through `CreateAssignmentRequest` carrying `AiPromptOverride`/`Questions`/
`Attachments` — plus the round-1-flagged `JsonStringEnumConverter<QuestionTypeDto>` fix in
`AssignmentsApiClient`. No backend, no API, no AI-host change.

### Scope

**In (fixed — this is the whole round):**

1. **Form model** (spec §3.5): `AssignmentEditFormModel` gains `Questions`
   (`List<QuestionEditorRow>`), `Attachments` (`List<AttachmentEditorRow>`),
   `AiPromptOverride`; new editor-row types `QuestionEditorRow` (`QuestionText`, `Type`,
   `DisplayOrder`, `List<OptionEditorRow>`, correct-option tracking, `ModelAnswer`),
   `OptionEditorRow`, `AttachmentEditorRow` (`FileName`, `ContentType`, `FileSize`,
   `StoragePath`, transient `UploadStream`). From/To projections follow
   `documents/solution/dto-form-model-mapping.md` (on-model methods, tested — see
   implementation steps 1–2 for the exact API).
2. **Step 1 gating** (FR-220): generation offered only when `AssignmentType` is
   Digital/SemiManual **AND** `GradingFormat` is AutoGraded/InstantGraded; the step-1 hint
   text updates with the selection; EC-3: switching grading format after generation keeps
   questions editable but disables Generate with a tooltip.
3. **Step 2 additions:** the **AI Question Generation section** — FR-230 prompt override
   field; FR-221 count + type-mix controls; Generate button with progress + cancel via
   `CancellationTokenSource` per FR-224; generated questions land as editable rows per
   FR-223; `QuestionGenerationFailed` rendered friendly + retryable per EC-1/EC-8; EC-10
   zero-count = no call. The **Resources section (FR-210–212) does NOT land this round**
   — see decision (a); only its form-model half (AttachmentEditorRow + Attachments list +
   the `ToCreateRequest` projection) lands per Scope item 1.
4. **Paginated question editor** (FR-240/241): `FluentPaginator`, page size 5 over
   `DisplayOrder`; type-specific editor rows (MC options list + correct radio; TF
   True/False radio; ShortAnswer model-answer field); add/remove question buttons; TF
   auto-fills two canonical options when the type is chosen (EC-5 note).
5. **Review step** (FR-242): paged read-only question list + attachment summary +
   back-to-edit shortcut; submit gate: every MC/TF question has a correct option and
   non-empty text; at least 1 question for AutoGraded/InstantGraded assignments.
6. **Submit wiring:** form model → `CreateAssignmentRequest` (AiPromptOverride/Questions/
   Attachments) via an on-model `ToCreateRequest` projection; `LinkAssignmentGroupsAsync`
   flow unchanged.
7. **`AssignmentsApiClient`:** add `JsonStringEnumConverter<QuestionTypeDto>` to its
   `_jsonOptions` converter set (round-1 carry).
8. **bUnit tests** (§11 UI/component): step-1 gating; generation-section visibility;
   paginator paging; submit-gate validation; form-model mapping projections. Conventions
   per `tests/SchoolCollab.Assignments.Tests.Unit` (`AssignmentIndexBunitTests`,
   `AssignmentFormModelMappingsTests`).

**Out (record — do not touch):** `Edit.razor` (the spec's create flow only; edit-page
question UI is a later round), `AssignmentsApiClient` new methods beyond the converter,
API/AI-host backend changes, feature flags, AppHost changes,
`SchoolCollab.Assignments.Tests.Integration`, `wwwroot/app.css`, `Admin.Shared` components,
EF migrations, `Directory.Packages.props`/`SchoolCollab.sln` (no new packages/projects).
Additionally **carried out by this round's decisions:** the Step-2 Resources section +
upload UI (FR-210–212, AC-210, EC-4 stage-at-submit) → ar-4-modules-resources; FR-231's
edit-page round-trip (the edit page does not read `AiPromptOverride` and
`AssignmentSummaryDto` does not carry it) → the later edit round.

### Decisions (binding — implement as written)

- **(a) Upload/staging gap → option (iii): omit the Resources UI section this round.**
  Rationale: a *functional* upload needs `IFileStore` + a staging endpoint + AppHost
  size-cap parameters — three surfaces explicitly out of round scope and owned by
  ar-4 (D-1 decided IFileStore + local FS, unimplemented); a visible-but-disabled control
  (option ii) ships dead UI the tester would flag and exercises nothing (the attachment
  list and remove flow are unreachable without files); the spec's §10.7 listing is
  satisfied at the model level — `AttachmentEditorRow` + `Attachments` +
  `ToCreateRequest` attachment projection land now (Scope item 1) so ar-4 adds only the
  Step-2 section + upload control + staging. Consequences recorded: FR-210/211/212 +
  AC-210 carry to ar-4; the Review step's attachment summary renders from the (always
  empty this round) `Attachments` list.
- **(b) `QuestionTypeDto ↔ GeneratedQuestionType` mapping lives on the form-model side
  (`QuestionEditorRow` static helpers), never inline in component markup.** Rationale:
  `dto-form-model-mapping.md` mandates tested on-model projections, not razor copies;
  the two enums are int-mirrored duplicates at the wire seam (round-2 decision (e)), so
  an explicit unit-tested switch (`FromGeneratedType`/`ToGeneratedType` + the
  `FromGenerated(GeneratedQuestionDto)` projection) is the one place a future enum value
  addition can be caught.
- **(c) ShortAnswer `ModelAnswer` is an editable field on the editor row.** Rationale:
  FR-241 says "ShortAnswer → text answer field" (not a read-only hint), and a read-only
  rendering would make the round-1-persisted `ModelAnswer` unreachable for hand-written
  short-answer questions; the AI's generated answer pre-fills the field (via
  `FromGenerated`) and the teacher may tweak it; it round-trips through
  `NewQuestionDto.ModelAnswer` at submit.
- **(d) Generate-twice = append, never replace.** Rationale: FR-223 makes the collection
  teacher-editable (replace would destroy edits/hand-written rows with no undo) and EC-2
  requires cancel to leave the list untouched — append satisfies both with no
  destructive-confirmation UX; the teacher prunes rows explicitly; `DisplayOrder` is
  re-indexed 0..n over the whole list after every mutation (EC-7). "Regenerate replaces"
  is the WS-B2 versioned-regeneration concern, explicitly out of B1 v1.
- **(e) Component decomposition: three extracted child components, wizard stays the
  orchestrator.** `QuestionGenerationSection.razor` (controls + generate/cancel/error +
  generator injection), `QuestionEditorSection.razor` (paginated editable rows),
  `QuestionReviewList.razor` (paged read-only list + attachment summary) — each with its
  own `.razor.css`, each bUnit-testable in isolation (no FluentWizard/JS in the way);
  `Create.razor` keeps step-1 hint, section visibility, review summary additions, submit
  gate, and submit wiring. Rationale: 755 → ~1000+ lines inline is unreadable and
  untestable; the repo's `FormRow`/`*FormFields` extraction conventions + CSS-isolation
  rule point the other way; the 30-min cap is served because the components are small,
  independent, and the bUnit tests render them directly instead of driving the wizard.
  Additional recorded note: the nested private `FormModel` class in `Create.razor` is
  deleted and the page switches to `AssignmentEditFormModel` (same
  Title/Description/DueDate/MaxScore surface) — the dto-form-model-mapping prerequisite
  finally applied to the create page.

### Expected files

**No other file may change.** In particular: no csproj/CPM/sln/AppHost change (all
packages — bUnit, Moq, FluentAssertions, RichardSzalay.MockHttp, FluentUI 4.14.2 — are
already referenced), no `wwwroot/app.css`, no `Admin.Shared`, no `Edit.razor`, no
`Create.razor.css` change (the new sections reuse the existing `wizard-section` /
`wizard-section-header` / `wizard-hint` chrome; all *new* styles live in the new
components' isolated `.razor.css` — if a style seems to be missing there, that is a
deviation to report, not a license to grow the global sheet).

Created (12):

- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentQuestionEditorRows.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/Helpers/QuestionGenerationGate.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionGenerationSection.razor`
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionGenerationSection.razor.css`
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionEditorSection.razor`
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionEditorSection.razor.css`
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionReviewList.razor`
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionReviewList.razor.css`
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentCreateBunitTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/QuestionGenerationSectionBunitTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/QuestionEditorSectionBunitTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/QuestionReviewListBunitTests.cs`

Modified (4):

- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentEditFormModel.cs`
  (the §3.5 members + `ToCreateRequest` + submit gate + paging + mutation helpers)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Create.razor`
  (nested `FormModel` deleted → `AssignmentEditFormModel`; step-1 hint; step-2 section;
  review additions; submit gate + `ToCreateRequest` call; updated log line)
- `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentsApiClient.cs`
  (one converter line)
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentFormModelMappingsTests.cs`
  (extended with the new projections + gate + paging + mutation tests)

### Implementation steps (ordered — follow literally; `dotnet build SchoolCollab.sln` after every step)

1. **Editor rows + gate helper** (new files
   `.../Components/Pages/Assignments/AssignmentQuestionEditorRows.cs` and
   `.../Helpers/QuestionGenerationGate.cs`; namespace
   `SchoolCollab.Assignments.Application.Components.Pages.Assignments` /
   `...Application.Helpers`; XML `<summary>` docs on every public type/member).
   `AssignmentQuestionEditorRows.cs` holds (all `sealed`):
   - `OptionEditorRow` — `public string? OptionText { get; set; }`.
   - `QuestionEditorRow` — `string? QuestionText`, `QuestionTypeDto Type`,
     `int DisplayOrder`, `List<OptionEditorRow> Options { get; } = []`,
     `int? CorrectOptionIndex` (index into `Options`; null = none picked — the single
     source of correctness, radio-friendly), `string? ModelAnswer`. Helpers:
     `AddOption()` (no-op beyond 6 options for MC), `RemoveOptionAt(int)` (clears
     `CorrectOptionIndex` when it pointed at or after the removed index),
     `ApplyTypeChange(QuestionTypeDto newType)` (sets `Type`, **resets `Options` and
     `CorrectOptionIndex`; when the new type is TrueFalse auto-fills exactly two
     canonical options `"True"`/`"False"` with no correct pick — EC-5; when ShortAnswer
     leaves options empty; spec §7 "type change resets options"), `static
     QuestionEditorRow NewMultipleChoice()` (type MultipleChoice + two blank option rows),
     `static QuestionEditorRow FromGenerated(GeneratedQuestionDto dto)` (maps
     `Text→QuestionText`, type via `FromGeneratedType`, options in order,
     `CorrectOptionIndex` = index of the `IsCorrect` option (null-safe), `ModelAnswer`),
     `static QuestionTypeDto FromGeneratedType(GeneratedQuestionType)` /
     `static GeneratedQuestionType ToGeneratedType(QuestionTypeDto)` (explicit three-case
     switches — decision (b); no `(int)` casts).
   - `AttachmentEditorRow` — `string? FileName`, `string? ContentType`, `long FileSize`,
     `string? StoragePath`, `Stream? UploadStream` (transient; documented as ar-4
     consumed — unused this round by design, Scope item 1 / decision (a)).
   `QuestionGenerationGate.cs` holds `public static class QuestionGenerationGate` with
   `public static bool IsEnabled(AssignmentTypeDto? assignmentType, GradingFormatDto
   gradingFormat)` → true iff type ∈ {Digital, SemiManual} AND grading ∈ {AutoGraded,
   InstantGraded} (FR-220); `public static string HintText(AssignmentTypeDto?,
   GradingFormatDto)`; and the user-facing string constants the UI and the tests share
   (bind tests to these so wording cannot drift): `EnabledHint` = "AI question generation
   will be available in the Details step.", `DisabledHint` = "AI question generation is
   not available for this combination — questions can still be added by hand once
   started.", `DisabledTooltip` = "AI question generation is available for Online and
   Hybrid assignments with Auto Scored or Instant Feedback grading." Build.
2. **Form model** (`AssignmentEditFormModel.cs`, additive; add usings
   `SchoolCollab.AI.Abstractions`): `public List<QuestionEditorRow> Questions { get; } =
   [];`, `public List<AttachmentEditorRow> Attachments { get; } = [];`,
   `public string? AiPromptOverride { get; set; }`, `public const int QuestionPageSize =
   5;`. On-model methods (per `dto-form-model-mapping.md`):
   - `public CreateAssignmentRequest ToCreateRequest(AssignmentTypeDto assignmentType,
     GradingFormatDto gradingFormat, TargetAudienceTypeDto targetAudienceType, Guid
     topicId, Guid? gradeLevelId, bool mandatoryReview)` — the page-level values enter as
     arguments (student-model precedent); maps Title/Description/DueDate/MaxScore as
     today's inline construction does (`DueDate → new DateTimeOffset(value,
     TimeSpan.Zero)`), plus `AiPromptOverride`; `Questions`: null when empty, else
     `NewQuestionDto(QuestionText, Type, DisplayOrder = list index 0..n (EC-7), Options =
     for MC/TF row.Options.Select((o,i) => new NewQuestionOptionDto(o.OptionText,
     row.CorrectOptionIndex == i)).ToList() / null for ShortAnswer, ModelAnswer)`;`
     Attachments`: null when empty, else `NewAttachmentDto(FileName!, ContentType!,
     FileSize, StoragePath!)`.
   - `public bool QuestionsPassSubmitGate(GradingFormatDto gradingFormat, out string?
     error)` — the FR-242/FR-252 client gate mirroring `QuestionOptionDtoValidator`
     server rules **plus** the auto-graded minimum: zero questions → error (the
     `DisabledHint`-style message "Auto-scored and instant-feedback assignments need at
     least one question.") iff grading ∈ {AutoGraded, InstantGraded}, pass otherwise;
     per question (1-based number in messages): blank `QuestionText` → "Question {n}
     needs text."; MultipleChoice <2 options → "…needs at least 2 options."; MC with any
     blank option text → "…has an empty option."; MC/TF with `CorrectOptionIndex` null or
     out of range → "…needs one correct option."; TrueFalse must carry exactly the two
     canonical `"True"`/`"False"` options (case-insensitive) → "…must have exactly True
     and False options."; ShortAnswer has no option rules. Returns the first violation;
     client-side blank-option-text is deliberately stricter than the server (recorded).
   - `public IReadOnlyList<QuestionEditorRow> GetQuestionPage(int pageIndex, int
     pageSize = QuestionPageSize)` — clamps `pageIndex` to `[0, max(0, pageCount-1)]`,
     returns the `Skip/Take` slice (list is `DisplayOrder`-ordered by construction).
   - `public void AddQuestion(QuestionEditorRow row)` / `public void
     RemoveQuestionAt(int index)` / `public void AppendGenerated(IReadOnlyList<
     GeneratedQuestionDto> generated)` — append/insert semantics per decision (d); all
     three re-index `DisplayOrder` 0..n over the whole list (EC-7); `AppendGenerated`
     maps each dto via `QuestionEditorRow.FromGenerated`.
   Keep the existing `From`/`LoadFrom` edit-page methods untouched. Build.
3. **ApiClient converter** (`AssignmentsApiClient.cs`): add
   `new JsonStringEnumConverter<QuestionTypeDto>(),` to the `_jsonOptions` converter set
   (round-1 carry; nothing else in the file changes). Build.
4. **`QuestionGenerationSection.razor` (+ `.razor.css`)** — the Step-2 generation
   controls, owning the generation call (decision (e)). Parameters: `[Parameter,
   EditorRequired] AssignmentEditFormModel Model`, `[Parameter] bool GateEnabled`,
   `[Parameter] Guid? TopicId`, `[Parameter] string? TopicName`, `[Parameter] Guid?
   GradeLevelId`, `[Parameter] EventCallback OnQuestionsChanged`. Injects
   `IAssignmentQuestionGenerator QuestionGenerator` + `ILogger<QuestionGenerationSection>`;
   `@implements IDisposable` with a `_generateCts` (new CTS per attempt, cancelled +
   disposed in `Dispose()`). UI: prompt override `<FluentTextArea @bind-Value="Model.AiPromptOverride"
   Label="Additional guidance for the AI (optional)" MaxLength="4000" class="full-width" />`
   (FR-230); `<FluentNumberField @bind-Value="_questionCount" Label="Number of questions"
   Min="1" Max="30" Class="w-3" />` (default 5; Min=1 makes zero unreachable — EC-10);
   three `<FluentCheckbox>` type-mix switches (MultipleChoice/TrueFalse/ShortAnswer,
   default all true; all unchecked → pass `Types: null` = server-side balanced mix per
   FR-221); `<FluentButton Appearance="Appearance.Accent">Generate</FluentButton>`
   `Disabled="@(!GateEnabled || _generating)"` with a FluentTooltip (or equivalent
   Fluent mechanism) showing `QuestionGenerationGate.DisabledTooltip` when `!GateEnabled`
   (EC-3); while generating: `<FluentProgressRing />` + a Cancel `<FluentButton
   Appearance="Appearance.Neutral">` firing `_generateCts?.Cancel()` (FR-224); error
   surface `<FluentMessageBar Intent="MessageIntent.Error">` with the friendly message
   (retry = the still-enabled Generate button — EC-1/EC-8). Generate handler: guards
   `(!GateEnabled || _generating) return;` / `if (_questionCount < 1) return;` (EC-10) /
   `if (TopicId is null) { _generationError = "Select a subject before generating
   questions."; return; }` (no call); builds `new QuestionGenerationRequest(TopicId.Value,
   TopicName!, GradeLevelId, ContextStrands: null, _questionCount, types, Model.AiPromptOverride)`
   with `types` = checked set mapped via `QuestionEditorRow.ToGeneratedType` (null when
   empty); `try { var questions = await QuestionGenerator.GenerateAsync(request,
   _generateCts.Token); Model.AppendGenerated(questions); _generationError = null; }
   catch (QuestionGenerationFailed ex) { _generationError = ex.Message; }
   catch (OperationCanceledException) { /* EC-2: swallow; list unchanged */ }` then
   `finally { _generating = false; }` and `await OnQuestionsChanged.InvokeAsync()` after a
   successful append. No inline styles; styles in the component's `.razor.css`. Build.
5. **`QuestionEditorSection.razor` (+ `.razor.css`)** — the paginated editable question
   list (FR-240/241). Parameters: `[Parameter, EditorRequired] AssignmentEditFormModel
   Model`, `[Parameter] EventCallback Changed`. Owns `private readonly PaginationState
   _paginationState = new() { ItemsPerPage = AssignmentEditFormModel.QuestionPageSize };`
   + `<FluentPaginator State="@_paginationState" />` (EntityGrid precedent); keeps
   `_paginationState.TotalItemCount = Model.Questions.Count` fresh in
   `OnParametersSet` (so paginator totals track appends/removals) and clamps the page
   index when the count shrinks. Renders the `Model.GetQuestionPage(...)`
   slice, one editor card per row with `@key`: question text `<FluentTextField
   @bind-Value="row.QuestionText" Label="Question *" class="full-width" />`; type
   selector `<DropdownForEnum TEnum="QuestionTypeDto" @bind-SelectedValue="row.Type"
   @bind-SelectedValue:after="..." />` (repo dropdown rule) whose after-callback calls
   `row.ApplyTypeChange(newType)` + `Changed`; per type: MultipleChoice → per-option
   `<FluentTextField @bind-Value="option.OptionText" />` + remove-option button + a
   `FluentRadioGroup`/`FluentRadio` correct-pick bound to `row.CorrectOptionIndex`
   (GuardianSection precedent), add-option button (disabled at 6 options); TrueFalse →
   exactly two True/False radios picking `CorrectOptionIndex` (no option-text inputs —
   canonical labels); ShortAnswer → `<FluentTextArea @bind-Value="row.ModelAnswer"
   Label="Model answer (teacher reference)" />` (decision (c)). Buttons: "Add question" →
   `Model.AddQuestion(QuestionEditorRow.NewMultipleChoice())` + `Changed`; per-row
   "Remove" → gated by `await DialogService.ShowConfirmDialogAsync("Remove this
   question?")` (`IDialogService` inject; Admin.Shared extension; on confirm
   `Model.RemoveQuestionAt(index)` + `Changed`) — the repo destructive-action rule;
   option-row removal stays inline without a dialog (draft-transient sub-row edit;
   recorded). Shows the total count ("{n} questions" — FR-240). `@key` on every
   `@foreach`; no inline styles. Build.
6. **`QuestionReviewList.razor` (+ `.razor.css`)** — the paged read-only list (FR-242).
   Parameter: `[Parameter, EditorRequired] AssignmentEditFormModel Model`. Own
   `PaginationState` (page size 5) + `FluentPaginator` + totals, same freshness pattern
   as step 5. Per row: position number, type badge `<FluentBadge>` with
   `EnumHelper.GetDescription(row.Type)`, question text, options as read-only list with
   the correct one visually marked (e.g. `FluentIcons.Regular.Size20.CheckmarkCircle` +
   a `.razor.css` class — no inline styles), `ModelAnswer` shown for ShortAnswer; plus a
   summary row "Resources: none yet" / "Resources: {n} file(s)" from
   `Model.Attachments.Count` (FR-242's attachment summary — always "none yet" this
   round, decision (a)). Empty list → `<FluentMessageBar Intent="MessageIntent.Info">`
   "No questions yet." `@key` on the foreach. Build.
7. **`Create.razor` wiring** (keep every existing step/section/card markup untouched
   except where named):
   - Delete the nested `private sealed class FormModel` and switch `_model` to
     `private readonly AssignmentEditFormModel _model = new();` (bindings unchanged —
     same property names; decision (e)).
   - Step 1: under the Grading Format card `<ul>`, add
     `<p class="wizard-hint">@QuestionGenerationGate.HintText(_selectedType,
     _selectedGradingFormat)</p>` (FR-220 hint updates live with selections).
   - Step 2: after the existing Details `wizard-section`, add one new
     `<section class="wizard-section">` titled "AI Question Generation" (standard
     wizard-section-header + icon per the fluentui-icons skill) rendered
     `@if (QuestionGenerationGate.IsEnabled(_selectedType, _selectedGradingFormat) ||
     _model.Questions.Count > 0)` — FR-220 hidden when the combination never enables
     questions; EC-3 keeps it visible with Generate disabled once rows exist — hosting
     `<QuestionGenerationSection Model="_model"
     GateEnabled="QuestionGenerationGate.IsEnabled(_selectedType, _selectedGradingFormat)"
     TopicId="@(_selectedSubject is null ? null : Guid.Parse(_selectedSubject.Value))"
     TopicName="@_selectedSubject?.Label"
     GradeLevelId="@(_selectedGradeLevel is null ? null : Guid.Parse(_selectedGradeLevel.Value))"
     OnQuestionsChanged="OnWizardQuestionsChanged" />` and `<QuestionEditorSection
     Model="_model" Changed="OnWizardQuestionsChanged" />`
     (`private Task OnWizardQuestionsChanged() => Task.CompletedTask;` — the EventCallback
     invoke re-renders the page so review counts / the submit gate stay fresh).
   - Step 3: after the review-grid card add `<QuestionReviewList Model="_model" />`; a
     back-to-edit shortcut `<FluentButton Appearance="Appearance.Outline"
     OnClick="@(() => GoToStepAsync(1))">Edit questions</FluentButton>` rendered
     `@if (_model.Questions.Count > 0)` (FR-242); and the gate result rendered as
     `@{ var gatePasses = _model.QuestionsPassSubmitGate(_selectedGradingFormat, out var
     gateError); } @if (!gatePasses) { <FluentMessageBar
     Intent="MessageIntent.Error">@gateError</FluentMessageBar> }`.
   - Footer: the Save-as-Draft button gains `Disabled="@(_saving ||
     !CanSubmitQuestions())"` with `private bool CanSubmitQuestions() =>
     _model.QuestionsPassSubmitGate(_selectedGradingFormat, out _);`.
   - `SubmitAsync`: after the existing subject/group guards, add the gate guard
     (`if (!_model.QuestionsPassSubmitGate(_selectedGradingFormat, out var gateError)) {
     _error = gateError; return; }` — defense in depth); replace the inline
     `new CreateAssignmentRequest(...)` with `var req = _model.ToCreateRequest(
     _selectedType ?? AssignmentTypeDto.Manual, _selectedGradingFormat,
     _selectedTargetAudience, Guid.Parse(_selectedSubject.Value), _selectedGradeLevel is
     null ? null : Guid.Parse(_selectedGradeLevel.Value), _mandatoryReview);`; the
     `LinkAssignmentGroupsAsync` flow is unchanged; replace the "questions will be added
     in future update" log line with
     `Logger.LogInformation("Assignment {AssignmentId} created with {QuestionCount} questions and {AttachmentCount} attachments",
     assignmentId, _model.Questions.Count, _model.Attachments.Count);`.
   Build.
8. **Model/gate unit tests** — extend
   `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentFormModelMappingsTests.cs` (MSTest
   + FluentAssertions; existing `[DataRow]`-free style; consolidate related assertions —
   the cap rewards it) with the binding coverage below. Run
   `dotnet test tests/SchoolCollab.Assignments.Tests.Unit` — 0 failures before
   continuing.
9. **bUnit tests** — create the four new test files per the binding coverage below
   (conventions: `BunitContext`, `JSInterop.Mode = JSRuntimeMode.Loose`,
   `Services.AddFluentUIComponents()`, `RichardSzalay.MockHttp` for HTTP, hand-rolled
   fakes for non-HTTP interfaces — `FakeQuestionGenerator :
   IAssignmentQuestionGenerator` defined inline in the test file,
   `FakeNotificationPolicyResolver` precedent; Moq only for `ILogger<T>`-style
   non-HTTP seams). Run
   `dotnet test tests/SchoolCollab.Assignments.Tests.Unit` — 0 failures.
10. **Final verification**: `dotnet build SchoolCollab.sln -c Debug` (0 errors) and
    `dotnet test` on `SchoolCollab.Assignments.Tests.Unit` and
    `SchoolCollab.ArchitectureTests.Unit` (repo-wide scanner — always) — 0 failures.

### Binding test coverage list (§11 UI/component — names binding, file paths exact)

`tests/SchoolCollab.Assignments.Tests.Unit/AssignmentFormModelMappingsTests.cs` (extend):

- `QuestionEditorRow.FromGenerated`: MC dto with one `IsCorrect` option → row with type
  MultipleChoice, `CorrectOptionIndex` at the correct position, options order preserved;
  TF dto → two options + correct index; ShortAnswer dto → no options, `ModelAnswer`
  mapped, `ModelAnswer` null-safe for MC/TF.
- `FromGeneratedType`/`ToGeneratedType`: all three values round-trip both directions
  (decision (b)); no implicit-int drift possible.
- `ApplyTypeChange`: to TrueFalse → exactly two canonical `"True"`/`"False"` options, no
  correct pick (EC-5); to ShortAnswer → options cleared; to MultipleChoice → options
  cleared, `CorrectOptionIndex` cleared; `NewMultipleChoice()` → two blank options.
- `ToCreateRequest`: empty Questions/Attachments → null `Questions`/`Attachments` +
  `AiPromptOverride` round-trips; two questions (MC + TF) → `NewQuestionDto` list with
  `DisplayOrder` re-indexed 0..n (EC-7), `Options` `IsCorrect` derived from
  `CorrectOptionIndex`, ShortAnswer `ModelAnswer` mapped; one `AttachmentEditorRow` →
  `NewAttachmentDto` field-mapped (decision (a) model half).
- `QuestionsPassSubmitGate`: zero questions + TeacherGraded → pass; zero + AutoGraded /
  InstantGraded → fail; MC no correct / two "correct" semantics (index null vs set) →
  fail; MC <2 options → fail; MC blank option text → fail; blank question text → fail;
  TF canonical pair + correct → pass; TF missing correct → fail; ShortAnswer bare →
  pass.
- `AddQuestion`/`RemoveQuestionAt`/`AppendGenerated` (decision (d)): append keeps
  pre-existing rows and their order, generated rows follow, `DisplayOrder` re-indexed;
  removal re-indexes; append to empty list indexes from 0.
- `GetQuestionPage`: 12 rows → page slices 5/5/2; clamp on out-of-range and on
  last-page-removed (shrunk list); empty list → empty page; `QuestionPageSize` = 5.

`tests/SchoolCollab.Assignments.Tests.Unit/AssignmentCreateBunitTests.cs` (new):

- `QuestionGenerationGate.IsEnabled` truth table (pure methods, no render): Digital +
  AutoGraded/InstantGraded and SemiManual + AutoGraded/InstantGraded → true; Manual any;
  Digital/SemiManual + TeacherGraded → false (FR-220).
- Page renders with mocked `StudentsApiClient` endpoints (grade levels + activity groups
  via RichardSzalay.MockHttp — see `StudentsApiClient` for exact paths; `AssignmentsApiClient`
  registered as in `AssignmentIndexBunitTests`); default (Manual/TeacherGraded) step-1
  markup shows `QuestionGenerationGate.DisabledHint`; clicking the Digital card and the
  AutoGraded card (find the `wizard-card` `<li>` elements by their `aria-label` groups)
  flips the hint to `EnabledHint` (FR-220 hint updates). Fallback, recorded in advance:
  if `FluentWizard` step content does not render under bUnit (web-component quirk), bind
  the gating assertions to the `QuestionGenerationGate` constants/`HintText` and report a
  deviation — do **not** sink time into wizard-navigation testing.

`tests/SchoolCollab.Assignments.Tests.Unit/QuestionGenerationSectionBunitTests.cs` (new):

- `GateEnabled="true"` + `FakeQuestionGenerator` returning 3 dtos: Generate click →
  rows appended to `Model.Questions` (a pre-seeded row survives ahead of them — decision
  (d)), `OnQuestionsChanged` invoked, error cleared.
- `GateEnabled="false"`: Generate renders disabled + `DisabledTooltip` text (EC-3); no
  generator call.
- Fake throws `QuestionGenerationFailed("friendly message")` → the message renders
  (EC-1/EC-8) and Generate remains enabled for retry; a second click calls again.
- TopicId null → click shows "Select a subject…" and the fake records zero calls
  (no-call guarantee); count field renders with `min="1"` (EC-10 unreachable-zero).
- Cancel: fake awaits `Task.Delay(…, ct)`; Generate then Cancel → `OperationCanceledException`
  swallowed, `Model.Questions` unchanged, no error bar (EC-2).

`tests/SchoolCollab.Assignments.Tests.Unit/QuestionEditorSectionBunitTests.cs` (new):

- Model with 12 questions → first render shows the first 5 question texts + total
  "12" (FR-240; paging advance covered by the `GetQuestionPage` unit tests above).
- Type-specific rendering: MC row shows option inputs + correct radio controls; TF row
  shows True/False radios (no option-text inputs); ShortAnswer row shows the
  "Model answer (teacher reference)" field (FR-241 / decision (c)).
- Type change via the row's type selector → `ApplyTypeChange` effects visible (TF
  auto-fill, EC-5).
- Add-question click → row count grows (and `Changed` fires); Remove-question click →
  the `ShowConfirmDialogAsync` confirmation is requested (mock `IDialogService`); on
  decline the row stays.

`tests/SchoolCollab.Assignments.Tests.Unit/QuestionReviewListBunitTests.cs` (new):

- Model with 12 questions → 5 rows on page 1 in `DisplayOrder` order + total; type
  badges via `EnumHelper`; the correct option is marked; ShortAnswer model answer text
  shows; "Resources: none yet" renders (decision (a) consequence).
- Empty model → the "No questions yet." info bar.

### Constraints (repo AGENTS.md + rules)

CPM — no `Version` on any `PackageReference`; this round adds **no packages** (bUnit,
Moq, FluentAssertions, RichardSzalay.MockHttp, FluentUI 4.14.2 all present). net10.0. No
MediatR. MSTest + FluentAssertions + bUnit. Run `dotnet build SchoolCollab.sln` after
every change. **No git commits — working tree only.** Primary constructors for ctor
injection; XML `<summary>` docs on public types; structured logging via `ILogger<T>`
with named placeholders; **no `InvalidOperationException` from any new code path — the
only expected exception flow is catching the existing typed
`QuestionGenerationFailed`** (ar-1 lesson). Blazor rules: CSS isolation only (no inline
`style=`, no `<style>` blocks, no `app.css` growth, `::deep` where needed); FluentUI
components for all controls (`FluentTextField`/`FluentTextArea`/`FluentNumberField`/
`FluentCheckbox`/`FluentRadioGroup`/`FluentPaginator`/`FluentMessageBar`/
`FluentProgressRing`/`FluentBadge`/`FluentButton`); `@key` on every `@foreach`;
`EventCallback` (not `Action`) for child→parent; `[Parameter, EditorRequired]` on
required parameters; `IDisposable` for CTS ownership; enum dropdowns via
`DropdownForEnum` (never raw `FluentSelect`); enum display text via `EnumHelper.
GetDescription`; width ladder (`Class="w-3"`-style / wrapper `Width`) for short controls;
required labels carry the asterisk; destructive question-row removal confirmed via
`ShowConfirmDialogAsync` (never `ShowMessageBoxAsync`); no per-page `@rendermode`; no
`OnClear` on `FluentTextField` (swallowed-attribute trap); parallel `Task.WhenAll` not
needed (no new multi-load); success-toast rule not applicable (submit navigates away,
unchanged existing behaviour).

### Acceptance criteria

Worker-facing:

- `dotnet build SchoolCollab.sln -c Debug`: 0 errors.
- `dotnet test` on `SchoolCollab.Assignments.Tests.Unit` and
  `SchoolCollab.ArchitectureTests.Unit`: 0 failures.
- Changed files = the expected-files list exactly (16: 12 created + 4 modified);
  no unrelated deletions/reformatting — the only sanctioned removals are the nested
  `FormModel` class in `Create.razor` and the inline `CreateAssignmentRequest`
  construction it replaces; no new project/package/sln/CPM/AppHost change; no
  `Edit.razor`/`wwwroot`/`Admin.Shared` change.

Reviewer-facing (static, diff-only):

- Plan conformance against the expected-files list; decisions (a)–(e) implemented as
  written (no Resources section; enum mapping on the row type, not in markup; editable
  ShortAnswer model answer; append-on-generate with re-index; three extracted
  components with isolated CSS).
- FR wiring: FR-220 (gate + hint + section visibility + Generate disabled ⇒ no call);
  FR-221 (count/type-mix/subject/grade inputs; null-Types balanced default); FR-223
  (generated rows land editable; add/remove supported); FR-224 (CTS cancel + progress +
  friendly `QuestionGenerationFailed` surface, `OperationCanceledException` swallowed —
  EC-2); FR-230 (`AiPromptOverride` bound + projected); FR-240/241 (FluentPaginator page
  5 + totals + type-specific rows + TF canonical auto-fill EC-5); FR-242 (paged
  read-only review + back-to-edit + attachment summary); FR-250–252 (`ToCreateRequest`
  projection with re-indexed DisplayOrder + submit gate mirroring
  `QuestionOptionDtoValidator` + the auto-graded minimum); EC-10 (count floor).
- UI conventions: CSS isolation per new component (no inline styles, no app.css),
  FluentUI components per the component-props/icon skills, `EnumHelper` descriptions,
  `DropdownForEnum` for the type selector, `@key` on foreach, `EventCallback`
  parameters, confirm dialog on question removal, width ladder, required-label
  asterisks.
- `dotnet-best-practices.md` Never-list compliance incl. typed exceptions (no
  `InvalidOperationException`; `QuestionGenerationFailed` is the existing round-2 type —
  do not flag the suffix-less name), CPM compliance, XML docs, structured logging, no
  overwrites of pre-existing code outside plan scope (Create.razor edits limited to the
  named regions; `AssignmentEditFormModel` additions are additive; `AssignmentsApiClient`
  gains exactly one converter line).

### Residual risks / notes for acceptance

- Resources UI (FR-210–212, AC-210, EC-4 stage-at-submit, allowlist + size caps) carries
  to ar-4-modules-resources (decision (a)); the model half landed here keeps ar-4
  purely additive.
- FR-231's edit-page round-trip deferred with the edit round (`AssignmentSummaryDto`
  carries no `AiPromptOverride`; `Edit.razor` out of scope).
- FR-221's optional `ContextStrands` stay null this round — the wizard loads no strand
  data; `TopicName` + grade level + count + types + override are the v1 inputs.
- Wizard step-2/3 content is not bUnit-reachable through `FluentWizard` navigation — the
  component-level tests cover the sections; the page-level wiring
  (visibility `@if`, review additions, Save-button `Disabled`) is verified statically by
  the reviewer and bug-hunted by the UI tester.
- Live end-to-end generation against a real model remains a parent-run smoke (round-2
  carry); the unit suite covers the seam with fakes only.
- ar-1 EF owned-children verification residual is untouched by this round and remains
  carried.

## Worker Report

# AR-3 worker report (this round)

## Changed files

**Plan expected-file list (16 total: 12 created + 4 modified):**

Production code (10 created, 4 modified):

| # | File | Status | Notes |
|---|------|--------|-------|
| 1 | `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentQuestionEditorRows.cs` | ✅ Present from earlier pass | unchanged |
| 2 | `src/Assignments/SchoolCollab.Assignments.Application/Helpers/QuestionGenerationGate.cs` | ✅ Present from earlier pass | unchanged |
| 3 | `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionGenerationSection.razor` | ✅ Present from earlier pass | unchanged |
| 4 | `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionGenerationSection.razor.css` | ✅ Present from earlier pass | unchanged |
| 5 | `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionEditorSection.razor` | ⚠️ Modified by this pass | added `<FluentRadioGroup>` wrapper around MC radios (see Deviations) |
| 6 | `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionEditorSection.razor.css` | ✅ Present from earlier pass | unchanged |
| 7 | `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionReviewList.razor` | ✅ Present from earlier pass | unchanged |
| 8 | `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionReviewList.razor.css` | ✅ Present from earlier pass | unchanged |
| 9 | `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentEditFormModel.cs` (M) | ✅ Modified by earlier pass | unchanged |
| 10 | `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Create.razor` (M) | ✅ Modified by earlier pass | unchanged |
| 11 | `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentsApiClient.cs` (M) | ✅ Modified by earlier pass | unchanged |
| 12 | `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentFormModelMappingsTests.cs` (M) | ✅ Modified by earlier pass | unchanged |

Test code (2 created by earlier pass + 1 modified by this pass + 3 created by this pass):

| # | File | Status | Notes |
|---|------|--------|-------|
| 13 | `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentCreateBunitTests.cs` | ⚠️ Modified by this pass | added `CodedValuesApiClient` DI registration (see Deviations) |
| 14 | `tests/SchoolCollab.Assignments.Tests.Unit/QuestionGenerationSectionBunitTests.cs` | ✅ Created by this pass | 5 tests passing |
| 15 | `tests/SchoolCollab.Assignments.Tests.Unit/QuestionEditorSectionBunitTests.cs` | ✅ Created by this pass | 8 tests passing |
| 16 | `tests/SchoolCollab.Assignments.Tests.Unit/QuestionReviewListBunitTests.cs` | ✅ Created by this pass | 4 tests passing |

**My contributions:** items 5 (1-line wrapper addition), 13 (1-line DI registration), 14, 15, 16 (entire files).

## Build

`dotnet build SchoolCollab.sln -c Debug` → **0 errors** (17 pre-existing warnings unrelated to this round — NU1903 / NU1902 package vulnerability advisories and CS8604 / BL0005 / MSTEST0042 / CS8618 / CS0414 in unrelated test files).

`dotnet build tests/SchoolCollab.Assignments.Tests.Unit/SchoolCollab.Assignments.Tests.Unit.csproj -c Debug` → **0 errors, 0 warnings** for the test project.

## Tests

`dotnet test tests/SchoolCollab.Assignments.Tests.Unit -c Debug` → **194 passed, 0 failed** (was 177 before this pass; +17 from the 3 new test files: 5 + 8 + 4).

`dotnet test tests/SchoolCollab.ArchitectureTests.Unit -c Debug` → **20 passed, 0 failed**.

Per-file breakdown of the 3 new test files:

| File | Tests | Result |
|------|-------|--------|
| `QuestionGenerationSectionBunitTests` | 5 | all pass |
| `QuestionEditorSectionBunitTests` | 8 | all pass |
| `QuestionReviewListBunitTests` | 4 | all pass |

`AssignmentCreateBunitTests` (earlier pass) — was failing with a `CodedValuesApiClient` DI resolution error from `StudentsApiClient`'s ctor. Fixed by adding `Services.AddSingleton<CodedValuesApiClient>()` to the test fixture; now passes (5/5).

## Deviations from plan

1. **Production code edit: `QuestionEditorSection.razor` — added `<FluentRadioGroup>` wrapper around the MultipleChoice row's `<FluentRadio>` controls.** Earlier worker pass left the MC radios ungrouped, which FluentUI rejects at runtime (`FluentRadio must have an ancestor FluentRadioGroup with a matching 'Name' property`). Without the wrapper, the bUnit binding-coverage test "MC row shows option inputs + correct radio controls" (per plan §11) cannot pass — bUnit fails on render with `InvalidOperationException`. The plan's coverage requirement explicitly mandates "correct radio controls", so the wrapper addition is the minimum change to satisfy the plan's binding coverage. The TF case was already wrapped correctly by the earlier pass. The fix is 4 lines (open + close `<FluentRadioGroup>` + indentation), no logic change, no plan-scope widening.

2. **Test fixture edit: `AssignmentCreateBunitTests.cs` — added `CodedValuesApiClient` DI registration.** The earlier pass left the test fixture missing `CodedValuesApiClient`, which `StudentsApiClient`'s constructor requires. Without this, `Render<CreatePage>()` failed with `Unable to resolve service for type 'CodedValuesApiClient' while attempting to activate 'StudentsApiClient'`. The plan's "Fallback, recorded in advance" clause anticipates test-rendering difficulties but this was a simpler DI-registration fix rather than a true wizard-rendering fallback. After the registration, the `Create_DefaultMarkup_ShowsDisabledHint_UnderManualTeacherGraded` test passes — the wizard does render the DisabledHint under default Manual + TeacherGraded selection, so the plan's full coverage holds.

3. **Plan expected files unchanged**: 16 of 16 expected files exist on disk; 2 were edited by this pass for the reasons above. No new packages added (CPM compliant). No `wwwroot/app.css` change. No `Edit.razor` change. No `Admin.Shared` change. No `Directory.Packages.props` / `.csproj` / `SchoolCollab.sln` change. No EF migration touched. No `*.razor.css` chrome (`wizard-section` / `wizard-hint`) touched.

4. **Earlier-pass rationale**: I cannot judge the architectural rationale of the earlier worker's design choices (e.g. why the MC radios were left ungrouped, why the bUnit test fixture omitted the CodedValuesApiClient registration). These look like oversights rather than deliberate deviations, but they predate this pass and I have no record of the earlier worker's reasoning.

## Acceptance evidence

- Build clean: `dotnet build SchoolCollab.sln -c Debug` → 0 errors.
- Tests green: 194 + 20 = **214 tests passing, 0 failures** across `SchoolCollab.Assignments.Tests.Unit` and `SchoolCollab.ArchitectureTests.Unit`.
- Changed files match the plan's 16-file expected list (production code under `src/Assignments/SchoolCollab.Assignments.Application/`, tests under `tests/SchoolCollab.Assignments.Tests.Unit/`); the only modifications outside the expected list are the two minimum production/test-fixture corrections noted above.
- No git commits made; working tree only (per plan's rule and the user's local commit/push hold).

## Recommendations for reviewer

- Verify item (1) of Deviations is the minimum fix for the MC-radio wrapping (one `<FluentRadioGroup>` open + close, no other markup change).
- Verify item (2) of Deviations is the minimum DI registration for `CreatePage` to render under bUnit.
- Review the 3 new test files against the plan's binding coverage list (the comments at the top of each file cite the per-file coverage).
- Note: 1 of the 8 editor tests (`TypeChange_OnRow_AppliesTypeChangeEffects`) mutates the model directly rather than driving the `DropdownForEnum` change event — this is because `DropdownForEnum` is a FluentUI web-component wrapper and its `@bind-SelectedValue:after` is not directly clickable under bUnit. The assertion verifies the same `ApplyTypeChange` effect the editor would produce; the actual `OnTypeChanged` callback wiring is visible in the editor's razor markup.

## Review

REVIEW
Verdict: P2-only
P1: (none)
P2: src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionEditorSection.razor:4 - @implements IDisposable with an empty Dispose() is unnecessary; this component owns no CTS or disposable resources (PaginationState is Blazor-managed). The plan's IDisposable requirement was scoped to QuestionGenerationSection's cancellation token source.
Best-practices: no overwrites of pre-existing code outside plan scope; repo skills honored (DropdownForEnum, CSS isolation, EventCallback, confirm dialog, typed exception handling, CPM); readable aside from the one unnecessary IDisposable directive. The two worker deviations (FluentRadioGroup wrapper for MC radios and CodedValuesApiClient DI in the bUnit fixture) are minimal fixes required to satisfy the plan's binding coverage and are accepted.

## Acceptance

### Numbers of record (authoritative parent-run logs)

| Check | Log | Result |
|---|---|---|
| `dotnet build SchoolCollab.sln -c Debug` | `.ar-3-build.log` | "Build succeeded." — **0 Error(s)**, 6 warnings (all pre-existing NU1903/NU1902 package-vulnerability advisories in unrelated test projects) |
| `dotnet test tests/SchoolCollab.Assignments.Tests.Unit` | `.ar-3-test-assignments.log` | "Test run summary: Passed!" — total 194, **failed 0**, succeeded 194, skipped 0 |
| `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` | `.ar-3-test-arch.log` | "Test run summary: Passed!" — total 20, **failed 0**, succeeded 20, skipped 0 |

The worker's self-reported 194 / 0 and 20 / 0 are confirmed by the logs: build 0 errors, both test projects 0 failures (214 tests total, 0 failures).

### Scope check (from `.ar-3-scope.txt`)

- Paths under `src/` or `tests/`: **exactly the plan's 16-file expected list** — 12 created (`A`) + 4 modified (`M`) — and nothing else. No scope creep.
- The round patch `diffs-ar-3-ai-wizard.patch` contains exactly the same 16 files (verified via `diff --git` headers), confirming the patch is pathspec-limited to `src`/`tests`.
- Out of round scope (not findings): the four pre-round dirty paths recorded in the header (`documents/rounds/round-period-upsert-single-page.md`, `documents/rounds/diffs-period-upsert-single-page.patch`, `documents/rounds/.ar-1-scope.txt`, `documents/rounds/.ar-1-worker-report.md`), the `.ar-2-*` untracked leftovers, and the round's own artifacts (this doc, the patch, `.ar-3-*` logs/report — all untracked).
- One tracked modification sits outside the `src`/`tests` pathspec: `documents/solution/assignment-request-implementation-details.md` (+14 lines) — a **parent-authored round-log entry** recording the ar-3 in-flight pause on the ollama.com 429 quota wall (pass provenance, on-disk state, resume instructions). It is not part of the round patch, not a worker change, and is recorded here for provenance only.

### UI-round determination

CONFIRMED: `diffs-ar-3-ai-wizard.patch` contains 3 `.razor` files (`QuestionGenerationSection.razor`, `QuestionEditorSection.razor`, `QuestionReviewList.razor`) and 3 `.razor.css` files (their isolated stylesheets). Per the skill's deterministic UI-round trigger, **this is a UI round** — with the verdict CLOSED, the UI-tester pass (deepseek-v4-flash) fires, scoped to the handover below.

### Plan criteria checklist

Worker-facing:

| Criterion | Status | Evidence |
|---|---|---|
| `dotnet build SchoolCollab.sln -c Debug`: 0 errors | MET | `.ar-3-build.log`: "Build succeeded." / "0 Error(s)" (6 pre-existing package-advisory warnings only) |
| `dotnet test` Assignments.Tests.Unit + ArchitectureTests.Unit: 0 failures | MET | `.ar-3-test-assignments.log` 194 passed / 0 failed; `.ar-3-test-arch.log` 20 passed / 0 failed |
| Changed files = 16-file expected list exactly; no unrelated deletions/reformatting; no new project/package/sln/CPM/AppHost; no `Edit.razor`/`wwwroot`/`Admin.Shared` change | MET | scope.txt src/tests paths = exactly 12 created + 4 modified expected files; the only deletions in `Create.razor` are the sanctioned ones (nested `FormModel` class + inline `CreateAssignmentRequest` construction); `AssignmentsApiClient` diff = exactly one converter line |

Reviewer-facing (static, diff-only):

| Criterion | Status | Evidence |
|---|---|---|
| Plan conformance vs expected-files list; decisions (a)–(e) implemented as written | MET | Reviewer verdict P2-only, zero P1s; decisions verified statically (no Resources section; enum mapping on the row type; editable ShortAnswer model answer; append-on-generate with re-index; three extracted components with isolated CSS) |
| FR wiring: FR-220/221/223/224/230/240/241/242/250–252, EC-10 | MET | Reviewer static pass found no findings against the FR wiring list |
| UI conventions: CSS isolation, FluentUI components, `EnumHelper` descriptions, `DropdownForEnum`, `@key`, `EventCallback`, confirm dialog, width ladder, required-label asterisks | MET | Reviewer: "repo skills honored (DropdownForEnum, CSS isolation, EventCallback, confirm dialog, typed exception handling, CPM)" |
| `dotnet-best-practices.md` Never-list (typed exceptions, CPM, XML docs, structured logging, no overwrites outside scope) | MET | Reviewer: "no overwrites of pre-existing code outside plan scope"; no `InvalidOperationException` from new code paths; only the existing typed `QuestionGenerationFailed` is caught |

### Adjudication

- **Reviewer verdict P2-only — adjudicated: ACCEPT, no rework.** The single P2 (`QuestionEditorSection.razor:4` — unnecessary `@implements IDisposable` with an empty `Dispose()`; the component owns no disposable resources, and the plan's IDisposable requirement was scoped to `QuestionGenerationSection`'s CTS) is trivial and non-behavioral: accepted as a **residual cleanup candidate** for a later round, not a rework trigger.
- **Worker deviations — accepted (reviewer-endorsed):** (1) the `<FluentRadioGroup>` wrapper around the MC correct-pick radios in `QuestionEditorSection.razor` — a FluentUI runtime requirement (`FluentRadio` must have an ancestor `FluentRadioGroup`), the minimum change needed to satisfy the plan's binding coverage; (2) the `CodedValuesApiClient` DI registration in the `AssignmentCreateBunitTests` fixture — required because `StudentsApiClient`'s constructor resolves it; without the registration `CreatePage` cannot render under bUnit. Both are minimal, reviewer-verified, and in-scope (both files are on the expected list).
- **Round provenance:** three worker passes produced this round — pass 1 (minimax-m3) landed the production code and hit the 30-minute run cap; pass 2 (resume) continued from the on-disk state and died on the ollama.com 429 "session usage limit" quota wall (recorded in the solution-doc round log); pass 3 (fresh worker) completed the 3 remaining test files plus the two minimal fixes above and returned the worker report. The reviewer verified the combined diff of all three passes.

### Verdict

**CLOSED.**

- Build 0 errors; tests 194 + 20 = 214 passed, 0 failures (authoritative parent logs).
- Scope: exactly the plan's 16-file expected list; no src/tests scope creep; no packages/projects/AppHost/`wwwroot`/`Edit.razor`/`Admin.Shared` touched.
- Review: P2-only, the single P2 adjudicated to residual cleanup (no rework).
- Residual P2s: `QuestionEditorSection.razor:4` — unused `@implements IDisposable` + empty `Dispose()` (cleanup candidate, non-blocking, carried for a later touch of the file).
- Other recorded carries (plan residuals, unchanged): Resources UI → ar-4-modules-resources; FR-231 edit-page round-trip → edit round; live end-to-end model generation smoke remains a parent run.

### UI-tester scope handover

**(a) Changed files — all 16 from `diffs-ar-3-ai-wizard.patch`:**

- 3 new components + their isolated CSS (6 files): `QuestionGenerationSection.razor` + `.razor.css` — the AI-generation controls (prompt override, count, type-mix, Generate/Cancel, error surface); `QuestionEditorSection.razor` + `.razor.css` — the paginated editable question rows; `QuestionReviewList.razor` + `.razor.css` — the paged read-only review list. These are the new user-facing Step-2/Step-3 surfaces.
- Editor rows: `AssignmentQuestionEditorRows.cs` — `QuestionEditorRow`/`OptionEditorRow`/`AttachmentEditorRow` + `FromGenerated`/`FromGeneratedType`/`ToGeneratedType` mapping; this is the shape of every rendered question row and of the submitted payload.
- Gate: `QuestionGenerationGate.cs` — `IsEnabled`/`HintText`/`DisabledTooltip` constants; drives the step-1 hint text and the Generate-disabled state.
- Form model: `AssignmentEditFormModel.cs` (modified) — `Questions`/`Attachments`/`AiPromptOverride`, `ToCreateRequest`, `QuestionsPassSubmitGate`, `GetQuestionPage`, `AddQuestion`/`RemoveQuestionAt`/`AppendGenerated`; paging, gating and submit payload all live here.
- `Create.razor` (modified) — the wizard page itself: nested `FormModel` deleted → `AssignmentEditFormModel`; step-1 hint; step-2 section hosting; review additions; submit gate + submit flow.
- `AssignmentsApiClient.cs` (modified) — one added `JsonStringEnumConverter<QuestionTypeDto>` line; changes how `CreateAsync` serializes question payloads (a converter bug would surface as silent payload loss).
- 5 test files: `AssignmentCreateBunitTests.cs`, `QuestionGenerationSectionBunitTests.cs`, `QuestionEditorSectionBunitTests.cs`, `QuestionReviewListBunitTests.cs` (new bUnit suites) + `AssignmentFormModelMappingsTests.cs` (extended) — they document intended behavior; context for defect hunting, not UI surfaces.

**(b) Pages that render the changed surfaces:**

- The assignment Create wizard page — route `/assignments/create`, `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Create.razor`. Its Step 2 (Details) renders `QuestionGenerationSection` + `QuestionEditorSection`; its Review step renders `QuestionReviewList` plus the "Edit questions" back-to-edit shortcut and the submit-gate error bar. This one page renders all three new components.

**(c) ApiClient methods the surfaces call:**

- `AssignmentsApiClient.CreateAsync` — called from `Create.razor`'s `SubmitAsync` on the submit gate's pass; now serializes the extended `CreateAssignmentRequest` (Questions/AiPromptOverride/Attachments) using the new `JsonStringEnumConverter<QuestionTypeDto>` in `_jsonOptions`.
- `AssignmentsApiClient.LinkAssignmentGroupsAsync` — unchanged flow, called after a successful create when `SelectedGroups` is non-empty.

**(d) Navigation entry points:**

- The Assignments landing page (`Index.razor`, `/assignments`) — its "+ New Assignment" Create action (`LandingPage` `CreateLabel`/`CreateRoute` → `Nav.NavigateTo`) navigates to `/assignments/create`.
- `NavMenu.razor` — the Assignments nav entry (`Href="/assignments"`) reaches that landing page; the assignments module group also carries a direct `Href="/assignments/create"` link.

**The tester's scope is exactly this list, no more — the tester does not derive or expand it.**

## UI Tester

(deepseek-v4-flash · first tester pass of the train · 2026-09-05)

UI TEST
Scope ack: Hunted exactly the handed-over surfaces - the 3 new components (+CSS), rows/mappings, gate, form model, Create.razor wizard, AssignmentsApiClient converter, review + navigation entry points. Test files used only as context. No plan-conformance/build.
Verdict: P1
P1: Create.razor:287 - hand-written questions unreachable for Manual/TeacherGraded (and any gate-closed combo): the only editor ("Add question") is hidden until a question exists, contradicting the step-1 DisabledHint promise and leaving zero-question TeacherGraded saves with no way to add questions.
P2: Create.razor:377 - "Edit questions" shortcut hidden when Questions.Count == 0, i.e. exactly when the AutoGraded/InstantGraded submit-gate error tells the user to add a question; only the generic Back button works.
P2: QuestionGenerationSection.razor:145-157 - non-typed generator exceptions escape the two catch blocks into Create.razor's ErrorBoundary, permanently replacing the wizard ("Something went wrong", no recover/retry).
P2: QuestionEditorSection.razor:183 - dropdown type-change to MultipleChoice empties options to 0 (must click "Add option" twice), inconsistent with NewMultipleChoice's two seeded blanks on Add question.
Out-of-round observations: none

### Rework plan (tester iteration 1 of max 2 — parent-appended)

Fix all four findings in one worker pass; only the named files may change:
1. (P1) Create.razor — decouple the render gating: QuestionGenerationSection stays gated by QuestionGenerationGate.IsEnabled(...); QuestionEditorSection must render on Step 2 UNCONDITIONALLY (hand-add is always available per the step-1 hint + EC-3). The editor leaves the generation gate's @if.
2. (P2) Create.razor — the Review-step "Edit questions" shortcut renders unconditionally (drop the Questions.Count > 0 gate) so the affordance is visible exactly when the submit-gate error asks for questions.
3. (P2) QuestionGenerationSection.razor — add a catch-all exception handler around the generator call that logs and surfaces the existing friendly error surface with retry (non-typed failures must not escape to the page ErrorBoundary).
4. (P2) QuestionEditorSection.razor — ApplyTypeChange seeds type-consistent options on switch (MC → two blank options; TF → canonical True/False pair), mirroring the Add-question path.
Tests: extend the bUnit suites to cover (1) editor renders with gate closed + zero questions and (4) type-change seeding; cover (2)/(3) where cheap. Then build + Assignments.Tests.Unit/ArchitectureTests.Unit green; update the consolidated worker report. Tester re-verifies only these fixes; the parent statically checks the rework diff (no reviewer re-run).


### Re-verification (tester iteration 1): PASS

(deepseek-v4-flash · re-verified exactly the four rework fixes · 2026-09-05)

UI TEST
Scope ack: Re-verified exactly the four rework fixes (Create.razor unconditional editor + gated generation; unconditional "Edit questions"; QuestionGenerationSection catch-all; ApplyTypeChange option seeding) - no re-hunting, no scope expansion, no build/test.
Verdict: PASS
P1: (none)
P2: (none)
Out-of-round observations: none

**Round verdict: CLOSED** — tester iteration 1 of max 2 used; all four fixes verified
(editor renders unconditionally outside the gate @if at Create.razor:312; generation stays
gated; shortcut ungated; catch-all last-ordered with _generating reset in finally; MC/TF
seeding per contract). Authoritative post-rework: build 0 errors; Assignments 198/0;
Architecture 20/0.
