# Spec: Assignment Creation with AI Question Generation

> **Status:** Draft — for review
> **Author:** Cline (spec-driven-workflow)
> **Date:** 2026-09-01
> **Reviewers:** Assignments context owner, Students context owner, AI services owner, Architecture
> **Owner contexts:** `SchoolCollab.Assignments.Core`,
> `SchoolCollab.Assignments.Application`, `SchoolCollab.Assignments.Contracts`,
> `SchoolCollab.AI.Server`, `SchoolCollab.AI.Abstractions`, `SchoolCollab.Students.Core`
> **Depends on:** `subject-to-topic-polymorphism.md`,
> `activity-group-enrollment.md`, `ai-services.md`, `ef-migrations.md`,
> `endpoint-organization-pattern.md`, `section-card.md`, `blazor-components.md`
>
> **Go-forward scope note (2026-09-03):** This spec is subsumed by
> `assignment-request-feature-spec.md` §3.4 as the core of WS-B1. It remains
> **authoritative for B1 execution** (execute unchanged); the AR extensions
> (difficulty mix, org-level system prompts, versioned regeneration, URL
> ingestion) layer on top — see
> `documents/solution/assignment-request-implementation-details.md` §2 WS-B.

---

## 0. Decisions (decision log)

1. **Questions & attachments are additive to the existing 3-step wizard, not a new wizard.** The current `Create.razor` has three steps ("Type & Format", "Details", "Review"). We evolve that flow rather than replacing it. The wizard remains a *single* create operation: questions/attachments are persisted together at submit, not streamed incrementally.

2. **Question generation is a server-side AI flow, gated per assignment type/grading format.** Generation is offered only when meaningful: `AssignmentType=Digital/SemiManual` AND `GradingFormat=AutoGraded/InstantGraded`. `Manual`/`TeacherGraded` assignments never auto-generate (a teacher writes questions by hand).

3. **A dedicated AI seam, not an overload of `/api/ai/chat`.** `AIChatEngine` consumes a **single** injected `ISystemPromptProvider`; the Coded Values engine occupies that registration. Question generation therefore gets its **own host endpoint** (`POST /api/ai/assignments/questions`) that wires its own `AIChatEngine` bound to a new `AssignmentQuestionGenerationSystemPromptProvider`. The Coded Values route/provider are **untouched**. The AI host remains the only place that constructs chat clients (rule: *never* `new OpenAIClient(...)` outside `ChatClientFactory`).

4. **New `IAssignmentQuestionGenerator` abstraction in the Assignments layer.** Blazor pages and the create command must not call the AI host directly. We introduce `IAssignmentQuestionGenerator` with `GenerateAsync(QuestionGenerationRequest, ct)` — the single seam between assignment UI/command and the AI question endpoint.

5. **`AssignmentQuestion`, `AssignmentAttachment`, `QuestionType` are wired into the create command, handler, form model, and UI now.** These domain entities already exist but are **not** referenced by `CreateAssignmentCommand`, its handler, or `Create.razor`. This spec closes that gap.

6. **Attachments are file-based and require new domain methods.** `AssignmentAttachment` is built from `(fileName, contentType, fileSize, storagePath)` — no URL constructor, and the aggregate currently owns a private `_attachments` list with no add/remove method. We add `Assignment.AddAttachment`/`RemoveAttachment` and route uploads through the aggregate.

7. **Tenancy follows the Direct Tenancy (operational data) pattern** (`BaseTenantEntity`, tenant-filtered). Questions and attachments are child aggregates of `Assignment` and inherit its `TenantId`. No override-pattern involvement.

8. **Custom AI prompt is an optional per-assignment override.** When blank, the system prompt loads from the embedded `assignment-question-system-prompt.md`; when set, the override is appended as a user-role framing message and flagged in the prompt provider. The override is a first-class field on the create command (persisted at the Assignment level), not per-question.

9. **Question display/paging is a pure UI concern over an owned collection.** Generated questions are materialised into wizard state as editor rows. Paging (`FluentPaginator`) splits those rows per page. Domain `DisplayOrder` is the sort key; page size is fixed (e.g. 5).

10. **True/False is represented as a MultipleChoice question with two canonical options** (`True`/`False`) so there is one `QuestionOption` table and one `CorrectOptionId` pointer in the DB. `QuestionType.TrueFalse` is retained as the discriminator; the handler emits exactly two options.

---

## 1. Current state (as-is)

Assignment creation is a 3-step wizard (`Create.razor`):

1. **Step 1 "Type & Format"** — `AssignmentType` (Digital/SemiManual/Manual), `GradingFormat` (TeacherGraded/AutoGraded/InstantGraded), `TargetAudienceType` (AllStudents/SelectedGrades/SelectedGroups), and a `MandatoryReview` checkbox.
2. **Step 2 "Details"** — Title, Activity Groups (multi-select, when audience is groups) or Grade Level, Subject (Topic), Description, Due Date, Max Score.
3. **Step 3 "Review"** — summary + submit.

On submit `SubmitAsync()` builds a `CreateAssignmentRequest` and calls `Api.CreateAsync(req)`; for `SelectedGroups` it additionally calls `Api.LinkAssignmentGroupsAsync(...)`. A log line is explicit about the gap:

```
Assignment {AssignmentId} created, questions will be added in future update
```

Domain entities already exist but are unwired:

| Entity | File | Notes |
|---|---|---|
| `AssignmentQuestion` | `Core/Domain/AssignmentQuestion.cs` | `Options`, `AddOption`, `RemoveOption`, `CorrectOptionId`, `DisplayOrder` — **not referenced** by create flow |
| `AssignmentAttachment` | `Core/Domain/AssignmentAttachment.cs` | file-based ctor `(assignmentId, fileName, contentType, fileSize, storagePath)` — **no `AddAttachment` on aggregate** |
| `QuestionType` | `Core/Domain/QuestionType.cs` | `MultipleChoice=0, TrueFalse=1, ShortAnswer=2` — **unused** by create flow |
| `Assignment.AddQuestion` / `RemoveQuestion` | `Core/Domain/Assignment.cs` | exists but **never called** by create flow |

`CreateAssignmentCommand` is a positional record
`(Title, Description, AssignmentType, GradingFormat, TargetAudienceType, TopicId,
GradeLevelId, DueDate, MaxScore, MandatoryReview)`; `CreateAssignmentRequest` mirrors
it; `AssignmentEditFormModel` holds only `Title/Description/DueDate/MaxScore`. The AI
host exposes one endpoint (`/api/ai/chat`) backed by `AIChatEngine` +
`CodedValuesSystemPromptProvider` + `CodedValuesToolProvider`; a single
`ISystemPromptProvider` is injected and prepended per turn.

---
## 2. Requirements (FR-*)

### 2.1 Target selection (audience)

- **FR-201** — The existing Step 1 audience controls remain the single source of
  targeting: AllStudents, SelectedGrades, SelectedGroups. No new targeting type is
  introduced. The subject/topic chosen in Step 2 must be deliverable to the chosen
  audience (existing FR-58 guard for groups): when `SelectedGroups`, the chosen
  Subject/Topic must be assigned to at least one selected activity group.
- **FR-202** — Activity-group targeting MUST still persist via
  `AssignmentActivityGroup` link records (retained per `subject-to-topic-polymorphism.md`
  FR-15/17). Grade targeting flows through `GradeLevelId`/`TopicId`.

### 2.2 Resources (attachments)

- **FR-210** — A teacher MUST be able to attach one or more resource files to an
  assignment before submit. Valid uploads: PDF, DOC/DOCX, PPT/PPTX, XLS/XLSX, images,
  text. Maximum total size and per-file size limits MUST be enforced at the API boundary
  (configurable; defaults in AppHost `Parameters:*`).
- **FR-211** — Each attachment MUST be stored to an object store / file store and
  modelled as an `AssignmentAttachment(assignmentId, fileName, contentType, fileSize,
  storagePath)` record via the aggregate — the UI never passes URLs. `storagePath` is
  opaque to the UI.
- **FR-212** — The wizard MUST let the teacher remove an attachment added in the current
  session before submit (soft: the row is dropped from the create payload, not deleted
  from store until the whole create is rejected).

### 2.3 AI question generation (server-side)

- **FR-220** — The wizard MUST offer AI question generation only when
  `AssignmentType=Digital/SemiManual` AND `GradingFormat=AutoGraded/InstantGraded`.
  Otherwise the section is hidden and no generation endpoint is called.
- **FR-221** — Generation request inputs: subject/topic (from Step 2), grade level (if
  any), optional lesson/strand context (topics from TopicStrand), number of questions,
  and desired `QuestionType` set (MultipleChoice / TrueFalse / ShortAnswer). The teacher
  MAY request a mix; counts default to a balanced mix when unspecified.
- **FR-222** — The AI MUST return structured questions: for `MultipleChoice`/
  `TrueFalse` exactly one option flagged correct (`CorrectOptionId`); for `ShortAnswer`
  a model answer line. Response parsing MUST be schema-validated and reject malformed
  JSON without crashing the wizard (show a retryable error).
- **FR-223** — The teacher MUST be able to edit, delete, or add questions in the wizard
  before submit. Generated questions land as an editable collection in wizard state; the
  teacher approves them in Review.
- **FR-224** — A single generation call MUST be cancellable and MUST surface a friendly
  provider error (401/403 auth, 429 rate limit, 5xx) rather than a raw trace, matching
  `FormatProviderError` conventions in `AIChatEngine`.
- **FR-225** — Generation MUST NOT occur for `Manual`/`TeacherGraded` assignments, and
## 3. Design

### 3.1 Domain — new aggregate methods

`Assignment` (in `SchoolCollab.Assignments.Core/Domain/Assignment.cs`) already exposes
`AddQuestion(questionText, questionType, displayOrder)` and `RemoveQuestion`. We add and
use the missing surface.

```csharp
// New on Assignment:
public AssignmentAttachment AddAttachment(string fileName, string contentType,
    long fileSize, string storagePath);
public void RemoveAttachment(Guid attachmentId);          // sets no tombstone; UI-side drop only
public IReadOnlyList<Attachment> Attachments => _attachments.AsReadOnly();
```

`AddQuestion`/`AddOption` (existing) are the only way questions enter the aggregate:

```csharp
var q = assignment.AddQuestion(questionText, QuestionType.MultipleChoice, displayOrder);
q.AddOption("Alpha", isCorrect: false);
q.AddOption("Beta",  isCorrect: true);   // sets q.CorrectOptionId
```

### 3.2 Commands & DTOs (additive, defaults preserved)

`CreateAssignmentCommand` gains two optional collections (defaults keep positional
signature source-compatible):

```csharp
public sealed record CreateAssignmentCommand(
    string Title,
    string? Description,
    AssignmentType AssignmentType,
    GradingFormat GradingFormat,
    TargetAudienceType TargetAudienceType,
    Guid TopicId,
    Guid? GradeLevelId,
    DateTimeOffset? DueDate,
    decimal? MaxScore,
    bool MandatoryReview = true,
    string? AiPromptOverride = null,
    IReadOnlyList<NewQuestionDto>? Questions = null,
    IReadOnlyList<NewAttachmentDto>? Attachments = null) : ICommand;
```

```csharp
public sealed record NewQuestionDto(
    string QuestionText,
    QuestionTypeDto QuestionType,     // mirrors Core QuestionType
    int DisplayOrder,
    IReadOnlyList<NewQuestionOptionDto>? Options);   // required for MC/TF

public sealed record NewQuestionOptionDto(string OptionText, bool IsCorrect);

public sealed record NewAttachmentDto(
    string FileName, string ContentType, long FileSize, string StoragePath);
```

`CreateAssignmentRequest` (Contracts) mirrors the three new members with `QuestionTypeDto`
and `NewQuestionDto`/`NewQuestionOptionDto`/`NewAttachmentDto` contract records, added as
optional trailing parameters so existing callers/Json deserialisation stay valid.

### 3.3 Handler sequence

`CreateAssignmentCommandHandler.HandleAsync` keeps its current prelude (code generation,
`Assignment.Create(...)`, domain-event publish, `AddAsync`, cache invalidation) and is
**extended after `Assignment.Create` and before `AddAsync`**:

1. For each `NewQuestionDto` (in `DisplayOrder` order): validate `QuestionText` non-empty;
   call `assignment.AddQuestion(text, type, displayOrder)`.
   - `MultipleChoice`/`TrueFalse`: require ≥2 options and exactly one `IsCorrect`;
     call `AddOption` each, set correct. `TrueFalse` → exactly `True`/`False`.
   - `ShortAnswer`: no options required.
2. For each `NewAttachmentDto`: `assignment.AddAttachment(fileName, contentType,
   fileSize, storagePath)`.
3. `repository.AddAsync(assignment, ct)` persists assignment **and** children within the
   same unit of work (existing EF graph save).
4. Existing `AssignmentCreatedIntegrationEvent` publish, cache-tag removal unchanged.

### 3.4 AI architecture

**Seam (Assignments side).** New interface in the Assignments AI-consuming project:

```csharp
public interface IAssignmentQuestionGenerator
{
    IAsyncEnumerable<GeneratedQuestionDto> GenerateAsync(
        QuestionGenerationRequest request, CancellationToken ct);
}
```

- `QuestionGenerationRequest` carries: `TopicId`, `TopicName`, `GradeLevelId?`,
  `Lesson/Strand` context strings, `int QuestionCount`, `IReadOnlyList<QuestionTypeDto>
  Types`, `string? PromptOverride`.
- Implementation (`AssignmentQuestionGenerator`) is an HTTP client to the AI host's
  `POST /api/ai/assignments/questions` (registered in the Assignments process only — it
  does **not** construct a chat client; the AI host owns `ChatClientFactory`). It returns
  validated, parsed questions and throws a typed `QuestionGenerationFailed` on provider
  error so the wizard can render a friendly message + retry.

**AI host (SchoolCollab.AI).**
- New endpoint `POST /api/ai/assignments/questions` in `SchoolCollab.AI`, registered in
  its `Program.cs`, behind the same auth as `/api/ai/chat`.
- It constructs/scopes an `AIChatEngine` bound to
  `AssignmentQuestionGenerationSystemPromptProvider` (embedded
  `assignment-question-system-prompt.md` + optional `PromptOverride` framing) and any
  needed tool providers. The existing `/api/ai/chat` + `CodedValuesSystemPromptProvider`
## 4. Data model & persistence

### 4.1 Relational model

Child tables already exist in the Assignments schema (from the domain entities above);
this spec does **not** add new tables, only wiring + a migration for the new
`Assignment.AiPromptOverride` column.

| Table | Key columns | Notes |
|---|---|---|
| `assignments` | + `ai_prompt_override` (text, null) | additive column via EF migration |
| `assignment_questions` | `id, assignment_id, question_text, question_type, display_order, correct_option_id` | `correct_option_id` nullable FK→`assignment_question_options` |
| `assignment_question_options` | `id, question_id, option_text, is_correct` | one row per option; exactly one `is_correct` per MC/TF question |
| `assignment_attachments` | `id, assignment_id, file_name, content_type, file_size, storage_path` | `storage_path` opaque |

All rows inherit `TenantId` from `Assignment` (Direct Tenancy pattern). EF relationships
already exist for the question→option child graph; cascading delete on assignment removal
is retained. `DisplayOrder` is store-ordered; `assignment_id` per-tenant unique ordering is
enforced in the handler (assign 0..n on inbound list).

### 4.2 Migration

One **additive** EF migration in `SchoolCollab.Assignments.Infrastructure`:

```
AddColumn: assignments.ai_prompt_override (text, nullable)
```

No changes to questions/options/attachments tables (already present). Follow
`ef-migrations.md` for naming and rollback.

### 4.3 AI question JSON contract

`POST /api/ai/assignments/questions` request:

```json
{
  "topicId": "…", "topicName": "Photosynthesis",
  "gradeLevelId": "…", "contextStrands": ["Cell biology"],
  "questionCount": 5,
  "types": ["multipleChoice", "trueFalse", "shortAnswer"],
  "promptOverride": null
}
```

Response (validated by the generator client):

```json
{
  "questions": [
    {
      "text": "Which organelle performs photosynthesis?",
      "type": "multipleChoice",
      "options": [{"text": "Mitochondria"}, {"text": "Chloroplast", "isCorrect": true}],
      "modelAnswer": null
    },
    {
      "text": "Photosynthesis requires sunlight.",
      "type": "trueFalse",
      "options": [{"text": "True", "isCorrect": true}, {"text": "False"}],
      "modelAnswer": null
    },
    { "text": "Name the main product…", "type": "shortAnswer",
      "options": null, "modelAnswer": "Glucose" }
  ]
}
```

Rule: for `multipleChoice`/`trueFalse`, the model MUST return exactly one option with
`isCorrect: true`; `shortAnswer` MAY return `modelAnswer`. Any deviation → validation
error surfaced via `QuestionGenerationFailed`.

---

## 5. Question type representation

- **MultipleChoice** — N options (2–6), one correct. Rendered as option rows + correct-radio.
- **TrueFalse** — stored as `QuestionType.TrueFalse`; the handler emits exactly two options
  `True` and `False` with exactly one correct, so grading reuses a single
  `correct_option_id` pointer (decision 10). No separate "answer" storage.
- **ShortAnswer** — no options; a `modelAnswer` (optional) is kept on the question for
  teacher reference; auto-grading of free text is out of scope (teacher grades).

Paging is orthogonal to type: a single `FluentPaginator` over the ordered `Questions`
collection, page size 5 (configurable), applied identically in the editor and Review.

---

## 6. Wizard flow (to-be)

## 7. UI specifics

- **Step 1 gating.** The "questions enabled" hint text updates based on the selected
  `AssignmentType` + `GradingFormat` combination. If the combination becomes
  non-generating after a step-2 visit, generated questions are retained but the "Generate"
  action is disabled with a tooltip explaining why.
- **Resources section** (`Step 2`). Fluent file-upload control bound to the
  `Attachments` editor list. Show name, extension icon, size and a remove (X) button.
  Reject non-allowlisted content types and oversized files at the UI before upload.
- **Question editor.** A `FluentDataGrid` or list of editor cards, one per question,
  paginated. Each card: text input, a type selector (locked after generation unless the
  type is changed, which resets options), and type-specific option controls. Add/Remove
  question buttons at the bottom.
- **Generate controls.** Button `[Generate N questions]`, a numeric count, and type-mix
  checkboxes. While generating, the button is disabled and a progress indicator shows;
  an in-flight `CancellationTokenSource` supports cancel (FR-224).
- **Review step.** Paged read-only question list with icons per type, plus an attachment
  summary and a "Back to edit" link. The submit button is disabled until validation passes
  (every MC/TF question has a correct option; ≥1 question for auto-graded assignments).
- Reuse `documents/rules/section-card.md` for section chrome and
  `documents/skills/fluentui-*` conventions for controls/icons.

---

## 8. Edge cases & error handling

- **EC-1** — AI provider returns malformed/partial JSON: schema validation fails; wizard
  shows a retryable error; no partial questions are persisted. The user can retry or
  proceed with hand-written questions.
- **EC-2** — Generate cancelled mid-call: token-cancelled exception is swallowed; the
  in-memory question list is left as it was; no server-side side effects (generation is
  read-only — nothing is persisted until submit).
- **EC-3** — User changes grading format from Auto to Teacher after generating questions:
  questions remain editable but "Generate" is disabled; already-entered questions still
  submit (a teacher may hand-write anyway). No blocking delete.
- **EC-4** — Attach then remove the same file before submit: the staged blob for a removed
  row is orphaned — a cleanup pass deletes staged blobs not referenced in the final
  payload, or staging happens only at submit (preferred: stage at submit, so a removed file
  never uploads).
- **EC-5** — MC question with no correct option: blocked at validation (FR-252 / submit
  gate). TrueFalse auto-fills two options when the type is chosen.
- **EC-6** — Tenant isolation: questions/attachments inherit `Assignment.TenantId`; the
  repository querying/inserting them uses the same tenant filter — no cross-tenant reads.
- **EC-7** — Duplicate display orders: handler re-orders inbound questions 0..n by the
  position given; persisted values are always contiguous per assignment.
- **EC-8** — Provider auth/rate-limit (401/403/429/5xx): mapped to friendly text via the
  existing `FormatProviderError`-style mapping; the wizard offers retry.
- **EC-9** — Prompt override contains injection-style text: treated as inert user data
## 9. Acceptance criteria

- **AC-201** — Creating a Digital/AutoGraded assignment with 3 generated questions
  (1 MC, 1 TF, 1 ShortAnswer) persists the questions + options + correct option under the
  assignment's tenant, and the create returns the assignment id.
- **AC-210** — Uploading a PDF in Step 2 and submitting stores an
  `assignment_attachments` row with an opaque `storage_path`; downloading the file later
  uses that path.
- **AC-223** — A teacher edits an option's correct flag in the editor and it is reflected
  in the persisted `correct_option_id`.
- **AC-230** — Setting `AiPromptOverride` persists it; reading the assignment back round-
  trips the value.
- **AC-240** — With 12 questions, Review shows paged rows (5/page) in `DisplayOrder`
  order with correct totals.
- **AC-25x** — A `Manual`/`TeacherGraded` assignment never triggers the generation
  endpoint (no request is issued).
- **AC-26x** — Unit tests cover the handler wiring, the domain add/remove methods, the
  MC-not-correct rejection, and the AI response parser (see §11).

---

## 10. Implementation plan (phases)

1. **Domain**: add `Assignment.AddAttachment`/`RemoveAttachment`/`Attachments`; add
   `AiPromptOverride` to the assignment + aggregate create factory (optional arg).
2. **Contracts**: add `QuestionTypeDto`, `NewQuestionDto`, `NewQuestionOptionDto`,
   `NewAttachmentDto`; extend `CreateAssignmentRequest`/`UpdateAssignmentRequest` with
   `AiPromptOverride`, `Questions`, `Attachments` (optional trailing defaults).
3. **Command + handler**: extend `CreateAssignmentCommand`; wire questions/attachments in
   `CreateAssignmentCommandHandler` (FR-250/251) with validation (FR-252).
4. **EF migration**: add `ai_prompt_override` column.
5. **AI**: add embedded `assignment-question-system-prompt.md`; add
   `AssignmentQuestionGenerationSystemPromptProvider`; add `POST /api/ai/assignments/questions`
   endpoint + JSON schema validation.
6. **Assignments AI seam**: add `IAssignmentQuestionGenerator` + HTTP implementation;
   register in DI (Assignments process only).
7. **UI**: extend `AssignmentEditFormModel`; add Resources + AI Question Generation
   sections to Step 2 and the paged question list to Review in `Create.razor`; wire submit.
8. **Tests** + build verification per §11.
9. Update `documents/configuration.md` only if new non-secret config keys land in the
   AppHost (e.g. upload size limits); secrets stay out of source.

---

## 11. Testing

- **Unit** (`SchoolCollab.Assignments.Tests.Unit`): `Assignment` add/remove attachment +
  question wiring; handler question/attachment persistence using an in-memory/fake
  repository; MC-without-correct rejection; truefalse two-option emission; `DisplayOrder`
  re-indexing.
- **Unit (AI)**: `AssignmentQuestionGenerationSystemPromptProvider` load + override
  framing; AI response JSON parser against valid/malformed payloads; typed
  `QuestionGenerationFailed` mapping for 401/429/5xx (Moq + `MockChatClient` pattern per
  `ai-services.md`).
- **UI/component**: `Create.razor` step-1 gating; generation section visibility; paginator
  paging; submit gate validation. Follow `testing.md` MTP Standard.
- **End-to-end**: create a Digital/AutoGraded assignment with AI questions + an attachment
  via the HTTP API; assert rows in `assignment_questions`, `assignment_question_options`,
  `assignment_attachments`, and the `ai_prompt_override` column, all under the correct
  tenant.
- **Build**: `dotnet build SchoolCollab.sln` after every production/test edit; run
  `dotnet test` with 0 failures before merge.

  (user-role framing), never concatenated into the tool list or config.
- **EC-10** — Zero questions requested (count 0 / "skip AI"): no call is made; the step
  submits either with hand-written questions or with none if grading doesn't require them.

---

```
Step 1  Type & Format      ── assignment type, grading format, audience + mandatory review
   │        (AutoGraded/InstantGraded only ⇒ "questions" enabled downstream)
   ▼
Step 2  Details            ── title, subject/topic, grade/groups, description, due date, score
   ├─ section: Resources (upload/remove attachments)
   └─ section: AI Question Generation (gated FR-220)
          • optional prompt override field (FR-230)
          • count + type mix controls (FR-221)
          • [Generate] → IAssignmentQuestionGenerator → edits questions in place (FR-223)
          • paginated question editor (FR-240/241)
   ▼
Step 3  Review             ── summary; paged read-only question list + attachment list (FR-242)
   ▼
Submit  CreateAssignmentCommand(…questions, attachments, aiPromptOverride)
        → handler wires questions/attachments → AddAsync → publish AssignmentCreated
        → LinkAssignmentGroups when SelectedGroups
```

On submit, transient `UploadStream` attachments are staged to storage first; the returned
`StoragePath`s populate the DTO, so the create payload only ever carries opaque paths.

---

  registration is left untouched (decision 3).
- Output is streamed/tooled; the endpoint returns JSON conforming to the schema in §4.3.

**Prompt provider.** `AssignmentQuestionGenerationSystemPromptProvider : ISystemPromptProvider`
mirrors `CodedValuesSystemPromptProvider`: embedded-resource load with `.original.md`
fallback and Development file override, `IncludesToolList => true` (if a tool advertises
available subjects). It composes the base prompt + an optional user-role override framing.

### 3.5 Form model

`AssignmentEditFormModel` gains:

```csharp
public List<QuestionEditorRow> Questions { get; } = [];
public List<AttachmentEditorRow> Attachments { get; } = [];
public string? AiPromptOverride { get; set; }
```

- `QuestionEditorRow` — editable `QuestionText`, `QuestionTypeDto Type`, `DisplayOrder`,
  `List<OptionEditorRow> Options`, `int? CorrectOptionIndex`. Add/remove/toggle helpers.
- `AttachmentEditorRow` — `FileName`, `ContentType`, `FileSize`, `StoragePath`,
  `Stream? UploadStream` (transient). Uploads are staged to storage at submit, then the
  returned `StoragePath` populates the create payload.
- Projection `From/To` extension points documented in `documents/solution/dto-form-model-mapping.md`.

---

  MUST NOT run at submit time in a blocking way; generation is interactive on demand via
  an explicit "Generate" action.

### 2.4 Custom AI prompt override

- **FR-230** — The assignment MAY carry an optional free-text `AiPromptOverride`. When
  blank, the system prompt loads from the embedded `assignment-question-system-prompt.md`
  resource. When set, the override is passed to the AI host and used as the user-role
  framing message appended to the system prompt (per decision 8).
- **FR-231** — The override MUST be persisted on the Assignment and round-trip through
  the create command/DTO so a teacher editing an existing assignment sees it again.

### 2.5 Question presentation & paging

- **FR-240** — In Step 2 / Review, questions are listed in `DisplayOrder` order, paginated
  with `FluentPaginator` at a fixed page size (default 5). Total count is shown.
- **FR-241** — Each question editor row renders by `QuestionType`: `MultipleChoice` →
  options list + correct-option radio; `TrueFalse` → True/False radio; `ShortAnswer` →
  text answer field. Option add/remove and correct-answer toggling MUST be supported.
- **FR-242** — In Review, paged read-only list + "edit" shortcut back to the question
  editor; no question is submitted until the teacher confirms.

### 2.6 Persistence (create command + handler)

- **FR-250** — `CreateAssignmentCommand` and `CreateAssignmentRequest` MUST accept a
  collection of questions (`QuestionType`, text, options with correct flag, `DisplayOrder`)
  and attachments (file metadata). These are optional (manual assignments may omit them).
- **FR-251** — `CreateAssignmentCommandHandler` MUST, after `Assignment.Create(...)`,
  call `AddQuestion(...)`/`AddOption(...)` for each inbound question and persist
  attachments via `AddAttachment(...)`. The aggregate's domain events still fire once for
  the assignment; questions/attachments are children saved in the same unit of work.
- **FR-252** — No question may be persisted without a non-empty `QuestionText`; a
  `MultipleChoice`/`TrueFalse` question without a correct option MUST be rejected
  (validation error before submit).

---


