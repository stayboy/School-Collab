# Round ar-2-ai-endpoint — AI question-generation endpoint + Assignments seam (B1 phases 5–6)

Provider: pi (models: glm-5.3, minimax-m3, kimi-k2.7-code; deepseek-v4-flash unused - backend round)

- **Tier:** 3 — full four-agent round. Worker model: minimax-m3 (30-minute run cap — the
  plan below is deliberately tight and strictly sequenced; keep each step small and build
  after every change).
- **Round base:** HEAD `f594d0eb04ed474804c86378a18bfe9a924e947a` on branch
  `stack/2-ar-2-ai-endpoint` (stacked on PR #218 = round ar-1, which landed the contracts
  this round consumes: `QuestionTypeDto`, `NewQuestionDto`/`NewQuestionOptionDto`/
  `NewAttachmentDto`, `AssignmentQuestionValidationException`).
- **Pre-round dirty paths OUT of round scope** (round patch is pathspec-limited to `src`
  and `tests`): `documents/rounds/round-period-upsert-single-page.md` |
  `documents/rounds/diffs-period-upsert-single-page.patch` |
  `documents/rounds/.ar-1-scope.txt` | `documents/rounds/.ar-1-worker-report.md`.
- Sources: `documents/specs/assignment-creation-with-ai.md` §0 (decisions 3, 4, 8, 10),
  §3.4, §4.3, §8 (EC-1, EC-2, EC-8, EC-9, EC-10), §10.5–10.6, §11;
  `documents/solution/assignment-request-implementation-details.md` §1.5 + §3 round log
  (ar-1 lesson: typed exceptions beat plan text — the repo rule wins);
  `.github/copilot/rules/ai-services.md`; `.github/copilot/rules/dotnet-best-practices.md`.

## Plan

### Goal

Execute **phases 5–6 only** of `documents/specs/assignment-creation-with-ai.md` §10 — the
AI host question-generation endpoint (`POST /api/ai/assignments/questions`, single JSON
document per §4.3, **not SSE**) plus the Assignments-side
`IAssignmentQuestionGenerator` HTTP seam — including the spec §11 "Unit (AI)" tests.
No UI, no wizard, no ApiClient change (§10.7 = round 3).

### Scope

**In:**
1. AI host (phase 5): embedded `Prompts/assignment-question-system-prompt.md` (+ `.original.md`
   fallback copy) in `SchoolCollab.AI.Server`, loaded via
   `AssignmentQuestionGenerationSystemPromptProvider` mirroring
   `CodedValuesSystemPromptProvider` (embedded resource + `.original.md` fallback +
   Development file override with mtime caching); the provider composes base prompt +
   optional `PromptOverride` as **user-role framing** (spec decision 8 / EC-9); the endpoint
   returns one validated JSON document per §4.3 with schema validation (exactly one
   `isCorrect` option for multipleChoice/trueFalse; `modelAnswer` optional for shortAnswer)
   and friendly provider-error mapping (401/403/429/5xx) via the `FormatProviderError`
   conventions extracted out of `AIChatEngine`.
2. Assignments seam (phase 6): `IAssignmentQuestionGenerator` abstraction + HTTP
   implementation (`QuestionGenerationRequest` per spec §3.4; typed `QuestionGenerationFailed`
   exception on provider error or malformed JSON); DI registration in
   `AddAssignmentsModule` (Assignments.Application) with a typed HttpClient against
   `https+http://settings-ai` (the Admin host already consumes that Aspire resource via
   `AddSettingsModule` → `AddAiChat`; no AppHost change).
3. Tests per spec §11 (AI): prompt provider load + override framing; response JSON parser
   against valid + malformed payloads; provider-error mapping; seam-client failure mapping.
   AI-host tests land in `tests/SchoolCollab.Settings.Tests.Unit` (that is where AI-host
   unit tests live today — the project references `SchoolCollab.AI.Server.csproj` and has
   `InternalsVisibleTo`; follow its `MockChatClient` precedent per `ai-services.md`); seam
   tests land in `tests/SchoolCollab.Assignments.Tests.Unit` (its
   `RichardSzalay.MockHttp` scripted-handler precedent).

**Out (do not touch):** wizard/UI changes (§10.7 = round 3), `AssignmentsApiClient`,
`documents/configuration.md` (no new config keys — the endpoint reuses
`codedvalue-ai-provider`, `Ollama:*`, `OpenRouter:*`), AppHost changes (no new parameters),
any `.razor` file, feature flags, EF migrations, `SchoolCollab.AI.Tools.CodedValues`
(untouched), the `/api/ai/chat` endpoint behaviour (untouched), git commits.

### Decisions (binding — implement as written)

- **(a) Endpoint engine: direct `IChatClient` call, NOT a per-endpoint `AIChatEngine`.**
  The endpoint's `AssignmentQuestionGenerationService` injects `IChatClientFactory`, calls
  `GetClient().GetResponseAsync(...)` (non-streaming), collects the full text, then
  parses + schema-validates. Rationale: the response is a single JSON document (§1.5 —
  JSON-not-SSE); the DI singleton `ISystemPromptProvider` is occupied by
  `CodedValuesSystemPromptProvider` (a second registration collides — §1.5), and reusing
  the SSE tool-loop engine for a tool-less JSON flow would drag `ChatUpdate`
  reassembly into a path that needs none of it. The provider still **implements**
  `ISystemPromptProvider` (spec decision 3) but is **registered as its concrete type**,
  avoiding the collision. Consequence recorded: the seam returns
  `Task<IReadOnlyList<GeneratedQuestionDto>>` — the spec §3.4 `IAsyncEnumerable` wording
  predates the verified §1.5 single-JSON finding.
- **(b) Prompt file + provider live in `SchoolCollab.AI.Server` directly — NO new
  project.** `src/AI/SchoolCollab.AI.Server/Prompts/assignment-question-system-prompt.md`
  (+ `.original.md`), provider in `.../Services/`. Rationale: the CodedValues precedent
  got a separate tools project because it ships 9 tools + an API client; this provider
  ships **zero tools (v1)**, so a dedicated project (csproj + sln + CPM churn) is pure
  overhead — and the solution doc WS-B1 itself suggests the AI.Server path. The csproj
  gains one `<EmbeddedResource Include="Prompts\**\*.md" />` line.
- **(c) Error contract: real HTTP status + JSON error body, never 200-with-error.**
  Endpoint: 400 for invalid requests (validation before the AI call), 502 for **any**
  upstream failure — provider transport/auth/rate-limit/5xx **and** malformed model JSON
  (retryable, message differs); both bodies are `{"error": "<friendly message>"}`.
  The Assignments seam maps **any** non-success status, any network failure, and any
  unparseable success body to `QuestionGenerationFailed` carrying the body's message
  (fallback to a status-derived message). Rationale: a JSON endpoint is not SSE (chat
  legitimately returns 200 + Error *events*); a typed contract gives the round-3 wizard a
  single rule for friendly retryable errors (EC-1/EC-8). The exception keeps the
  spec/task-blessed name `QuestionGenerationFailed` (no `-Exception` suffix — spec §3.4
  and the round task name it; the repo typed-exception rule is satisfied by *typedness*,
  not suffix).
- **(d) Auth/tenancy: same posture as `/api/ai/chat` — no `RequireAuthorization`, no
  tenant header.** Generation is stateless and reads no tenant data (the request carries
  `TopicName`/context strings, not tenant-scoped lookups). Recorded as accepted posture;
  hardening (auth on AI host endpoints) is a later-round candidate once identity wiring
  lands (solution doc §4 item 1). Corollary: the seam client registers with
  `propagateTenant: false`.
- **(e) Request/response DTO placement: `SchoolCollab.AI.Abstractions` (the shared seam
  lib) — new file `AssignmentQuestionGenerationTypes.cs` with
  `QuestionGenerationRequest`, `QuestionGenerationResponse`, `GeneratedQuestionDto`,
  `GeneratedQuestionOptionDto`, and a dedicated `GeneratedQuestionType` enum
  (MultipleChoice=0/TrueFalse=1/ShortAnswer=2, `[JsonConverter(typeof(JsonStringEnumConverter<GeneratedQuestionType>))]`).**
  Rationale: the Assignments side needs the request type and the AI host needs both —
  `AI.Abstractions` is the one library both may reference without a bounded-context
  violation; `Assignments.Contracts` would force `AI.Server → Assignments.Contracts`
  (forbidden cross-context reference); AI.Server-local records would be invisible to the
  seam. AI.Abstractions cannot reference `Assignments.Contracts`, so the 3-value enum is
  duplicated at the wire seam (values mirror `QuestionTypeDto`); the round-3 wizard maps
  `QuestionTypeDto ↔ GeneratedQuestionType` at the boundary. Wire format = §4.3 camelCase
  + string enums (the attribute makes every serializer honour it).

### Expected files

Created (15 production/test files + 2 prompt files = 17; test-file names indicative, the
coverage list below is binding):

- `src/AI/SchoolCollab.AI.Abstractions/AssignmentQuestionGenerationTypes.cs`
- `src/AI/SchoolCollab.AI.Server/Prompts/assignment-question-system-prompt.md`
- `src/AI/SchoolCollab.AI.Server/Prompts/assignment-question-system-prompt.original.md`
- `src/AI/SchoolCollab.AI.Server/Services/AssignmentQuestionGenerationSystemPromptProvider.cs`
- `src/AI/SchoolCollab.AI.Server/Services/ProviderErrorFormatter.cs`
- `src/AI/SchoolCollab.AI.Server/Services/AssignmentQuestionResponseParser.cs`
- `src/AI/SchoolCollab.AI.Server/Services/AssignmentQuestionGenerationException.cs`
- `src/AI/SchoolCollab.AI.Server/Services/AssignmentQuestionGenerationService.cs`
- `src/AI/SchoolCollab.AI.Server/Services/AssignmentQuestionGenerationExtensions.cs`
- `src/AI/SchoolCollab.AI.Server/Endpoints/AssignmentQuestionGenerationEndpoints.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/Services/IAssignmentQuestionGenerator.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentQuestionGenerator.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/Services/QuestionGenerationFailed.cs`
- `tests/SchoolCollab.Settings.Tests.Unit/AssignmentQuestionGenerationSystemPromptProviderTests.cs`
- `tests/SchoolCollab.Settings.Tests.Unit/AssignmentQuestionResponseParserTests.cs`
- `tests/SchoolCollab.Settings.Tests.Unit/AssignmentQuestionGenerationServiceTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentQuestionGeneratorTests.cs`

Modified (5):

- `src/AI/SchoolCollab.AI.Server/SchoolCollab.AI.Server.csproj` (add
  `<EmbeddedResource Include="Prompts\**\*.md" />`)
- `src/AI/SchoolCollab.AI.Server/Services/AIChatEngine.cs` (delete private
  `FormatProviderError`, delegate its single call site to `ProviderErrorFormatter.Format` —
  behaviour byte-identical)
- `src/AI/SchoolCollab.AI.Server/Program.cs` (add
  `builder.Services.AddAssignmentQuestionGeneration();` next to `AddCodedValuesAiTools`,
  and `app.MapAssignmentQuestionGenerationEndpoints();` right after `MapDefaultEndpoints`)
- `src/Assignments/SchoolCollab.Assignments.Application/ModuleServices.cs` (register the
  typed client + interface)
- `src/Assignments/SchoolCollab.Assignments.Application/SchoolCollab.Assignments.Application.csproj`
  (add `<ProjectReference Include="..\..\AI\SchoolCollab.AI.Abstractions\SchoolCollab.AI.Abstractions.csproj" />`)

**No other file may change.** In particular: no `Directory.Packages.props` change (no new
packages — System.Text.Json is inbox, `Microsoft.Extensions.AI` 10.6.0 is already
referenced everywhere it is needed), no `SchoolCollab.sln` change (no new project), no
AppHost change.

### Implementation steps (ordered — follow literally; build after every step)

1. **AI.Abstractions types** (`AssignmentQuestionGenerationTypes.cs`, namespace
   `SchoolCollab.AI.Abstractions`, XML docs on every public type):
   `GeneratedQuestionType` enum with the `[JsonConverter(typeof(JsonStringEnumConverter<GeneratedQuestionType>))]`
   attribute; `QuestionGenerationRequest(Guid TopicId, string TopicName, Guid? GradeLevelId
   = null, IReadOnlyList<string>? ContextStrands = null, int QuestionCount = 5,
   IReadOnlyList<GeneratedQuestionType>? Types = null, string? PromptOverride = null)`;
   `GeneratedQuestionOptionDto(string Text, bool IsCorrect = false)`;
   `GeneratedQuestionDto(string Text, GeneratedQuestionType Type,
   IReadOnlyList<GeneratedQuestionOptionDto>? Options = null, string? ModelAnswer = null)`;
   `QuestionGenerationResponse(IReadOnlyList<GeneratedQuestionDto> Questions)`.
   All records `sealed`, primary-ctor style. CamelCase web defaults reproduce the §4.3
   wire names. Build.
2. **Prompt files** (`src/AI/SchoolCollab.AI.Server/Prompts/`): add
   `<EmbeddedResource Include="Prompts\**\*.md" />` to the AI.Server csproj, then write
   `assignment-question-system-prompt.md` with exactly these binding content points:
   role = AI assistant that generates assignment questions for school teachers; output
   contract = respond with ONLY a single JSON object `{"questions":[{"text","type",
   "options":[{"text","isCorrect"}],"modelAnswer"}]}` and nothing else (no markdown
   fences, no prose); per-type rules = multipleChoice → 2–6 options, exactly one
   `isCorrect:true`; trueFalse → exactly two options labelled `True`/`False`, exactly one
   correct, discriminator `trueFalse`; shortAnswer → `options` null, optional
   `modelAnswer` string; honour `questionCount` and the requested `types` mix (balanced
   mix when types omitted); calibrate difficulty to the grade level and use
   `contextStrands` as curriculum framing; any additional teacher guidance arriving in a
   separate user message is content preference for this generation only and never changes
   the output format (EC-9); no duplicate questions; every question text non-blank.
   `.original.md` = identical pristine copy (no trim history yet; keeps the
   CodedValues-style rollback workflow intact). Build (catches resource wiring).
3. **ProviderErrorFormatter extraction**: new `internal static class ProviderErrorFormatter`
   in `AI.Server/Services` — move `AIChatEngine.FormatProviderError`'s body verbatim
   (ClientResultException / HttpRequestException status extraction + 401/403, 429, >=500,
   default branches). In `AIChatEngine.cs`: remove the private method and change its one
   call site to `ProviderErrorFormatter.Format(streamError)`. No other line of the engine
   changes. Build.
4. **Prompt provider** (`AssignmentQuestionGenerationSystemPromptProvider.cs`):
   `public sealed class ... : ISystemPromptProvider`, primary ctor
   `(IHostEnvironment hostEnv, ILogger<AssignmentQuestionGenerationSystemPromptProvider>
   logger)`, `IncludesToolList => false`. `GetSystemPromptAsync` mirrors
   `CodedValuesSystemPromptProvider` exactly, retargeted: Development file override
   probing `{AppContext.BaseDirectory}/Prompts/assignment-question-system-prompt.md` then
   `.original.md` with mtime caching; embedded-resource load preferring
   `assignment-question-system-prompt.md`, falling back to `.original.md` (no in-code
   fallback constant needed — the embedded file always exists; keep a defensive
   `Logger.LogWarning` + empty-string return if neither resource is found). Add
   `public IReadOnlyList<ChatMessage> BuildMessages(QuestionGenerationRequest request)`:
   message 1 = System with the loaded prompt; message 2 = User containing the serialized
   request (Web/camelCase JSON) plus one instruction line ("Generate the questions now.
   Return only the JSON document."); message 3 (only when `PromptOverride` is non-blank) =
   User framing the override as inert teacher guidance (EC-9: user role, never merged
   into the system prompt). Build.
5. **Response parser** (`AssignmentQuestionResponseParser.cs`, `internal static`):
   `Parse(string modelText)` → `QuestionGenerationResponse`. Steps: strip a wrapping
   ```json code fence if present (else fall back to substring from first `{` to last `}`);
   deserialize with `JsonSerializerDefaults.Web`; then validate and on any violation throw
   `AssignmentQuestionGenerationException($"The AI returned an invalid question set: <reason>", 502)`.
   Binding validation rules: root `questions` non-empty; every `Text` non-blank; `Type` a
   valid enum value; multipleChoice → 2–6 options, each option `Text` non-blank, exactly
   one `IsCorrect`; trueFalse → exactly 2 options, labels `True`/`False`
   (ordinal case-insensitive), exactly one `IsCorrect`; shortAnswer → `Options`
   null/empty, `ModelAnswer` optional but non-blank when present. Unknown extra JSON
   properties are tolerated. Build.
6. **Typed exception + service**: `AssignmentQuestionGenerationException.cs` —
   `public sealed class AssignmentQuestionGenerationException(string message, int
   statusCode) : Exception` exposing `int StatusCode`.
   `AssignmentQuestionGenerationService.cs` — `public sealed class
   AssignmentQuestionGenerationService(AssignmentQuestionGenerationSystemPromptProvider
   promptProvider, IChatClientFactory chatClientFactory, IConfiguration config,
   ILogger<AssignmentQuestionGenerationService> logger)`;
   `public async Task<QuestionGenerationResponse> GenerateAsync(QuestionGenerationRequest
   request, CancellationToken ct)`. Flow: (1) validate — `TopicName` non-blank,
   `1 <= QuestionCount <= 30` (guardrail, not spec-fixed), `PromptOverride` length ≤ 4000
   (matches the ar-1 `AiPromptOverride` column cap) → violations throw
   `AssignmentQuestionGenerationException(msg, 400)`; (2) resolve the model via
   `ChatModelResolver.Resolve(config["codedvalue-ai-provider"], config["Ollama:DefaultModel"],
   config["OpenRouter:DefaultModel"])` and build `new ChatOptions { ModelId = model }`
   (no Tools — v1), mirroring the engine's `ResolveDefaultModel`; (3) call
   `chatClientFactory.GetClient().GetResponseAsync(promptProvider.BuildMessages(request),
   options, ct)`; (4) catch `OperationCanceledException` when `ct.IsCancellationRequested`
   → rethrow (EC-2 — cancellation must not surface as a 502); catch
   `ClientResultException`/`HttpRequestException`/any other `Exception` → throw
   `AssignmentQuestionGenerationException(ProviderErrorFormatter.Format(ex), 502)`
   (EC-8 friendly mapping); (5) `AssignmentQuestionResponseParser.Parse(response.Text)`.
   Structured logging with named placeholders throughout. Build.
7. **DI extension + endpoint + wiring**:
   `AssignmentQuestionGenerationExtensions.cs` — `AddAssignmentQuestionGeneration(this
   IServiceCollection services)` registering
   `AddSingleton<AssignmentQuestionGenerationSystemPromptProvider>()` (**concrete type —
   never register it as `ISystemPromptProvider`**, decision (a)/(b)) and
   `AddSingleton<AssignmentQuestionGenerationService>()`.
   `Endpoints/AssignmentQuestionGenerationEndpoints.cs` —
   `MapAssignmentQuestionGenerationEndpoints(this WebApplication app)` containing
   `app.MapPost("/api/ai/assignments/questions", ...)` (no `RequireAuthorization` —
   decision (d)): bind `QuestionGenerationRequest? request` from the body; `null` → 400
   `{"error": "A question generation request is required."}`; call the service inside
   `try/catch (AssignmentQuestionGenerationException ex) → Results.Json(new { error =
   ex.Message }, statusCode: ex.StatusCode)`; success → `Results.Ok(response)`.
   `Program.cs`: add the two wiring lines per the expected-files list. Build.
8. **AI-host tests** (`tests/SchoolCollab.Settings.Tests.Unit/`): MSTest + FluentAssertions
   + Moq, following the existing conventions in that project (see binding coverage
   below). Run `dotnet test tests/SchoolCollab.Settings.Tests.Unit` — 0 failures before
   continuing.
9. **Assignments seam** (in `src/Assignments/SchoolCollab.Assignments.Application/`):
   csproj project reference to `SchoolCollab.AI.Abstractions` (decision (e));
   `Services/IAssignmentQuestionGenerator.cs` —
   `Task<IReadOnlyList<GeneratedQuestionDto>> GenerateAsync(QuestionGenerationRequest
   request, CancellationToken ct = default);` (decision (a) consequence);
   `Services/QuestionGenerationFailed.cs` — `public sealed class
   QuestionGenerationFailed(string message) : Exception` (decision (c) naming);
   `Services/AssignmentQuestionGenerator.cs` — `public sealed class
   AssignmentQuestionGenerator(HttpClient http, ILogger<AssignmentQuestionGenerator>
   logger) : IAssignmentQuestionGenerator` with `JsonSerializerOptions(JsonSerializerDefaults.Web)`
   (the enum attribute on `GeneratedQuestionType` covers string enums — no converter list
   needed); POST `/api/ai/assignments/questions` via `PostAsJsonAsync`; success →
   `ReadFromJsonAsync<QuestionGenerationResponse>` → return `Questions`; non-success →
   read the `{"error": ...}` body and throw `QuestionGenerationFailed(body error ?? $"Question
   generation failed (HTTP {(int)status}).")`; `HttpRequestException` → `QuestionGenerationFailed("The
   AI question service is unreachable. Please try again in a moment.")`; malformed success
   body → `QuestionGenerationFailed("The AI returned an unreadable response. Please try
   again.")`; pass `ct` through and rethrow `OperationCanceledException` when
   `ct.IsCancellationRequested` (EC-2). `ModuleServices.cs` inside `AddAssignmentsModule`:
   `services.AddCrossModuleHttpClient<AssignmentQuestionGenerator>("https+http://settings-ai",
   propagateTenant: false);` (decision (d) corollary) +
   `services.AddTransient<IAssignmentQuestionGenerator>(sp =>
   sp.GetRequiredService<AssignmentQuestionGenerator>());`. Build.
10. **Seam tests** (`tests/SchoolCollab.Assignments.Tests.Unit/AssignmentQuestionGeneratorTests.cs`):
    `RichardSzalay.MockHttp` scripted handler (repo rule: no Moq/NSubstitute for HTTP;
    precedent `AssignmentIndexBunitTests`). Run
    `dotnet test tests/SchoolCollab.Assignments.Tests.Unit` — 0 failures.
11. **Final verification**: `dotnet build SchoolCollab.sln -c Debug` (0 errors) and
    `dotnet test` on `SchoolCollab.Settings.Tests.Unit`,
    `SchoolCollab.Assignments.Tests.Unit`, and
    `SchoolCollab.ArchitectureTests.Unit` (repo-wide scanner — always) — 0 failures.

### Binding test coverage list (§11 Unit (AI) + seam)

`tests/SchoolCollab.Settings.Tests.Unit/` (AI host; consolidate related assertions into
few `[TestMethod]` bodies — `[DataRow]` where natural; the 30-minute cap rewards
consolidation):

- `AssignmentQuestionGenerationSystemPromptProviderTests` (mark the class
  `[DoNotParallelize]` — the project pins `Parallelize(Scope.MethodLevel)` and the
  file-override tests write into `{AppContext.BaseDirectory}/Prompts`, which must not
  race): embedded load with a non-Development `IHostEnvironment` (Moq) returns a
  non-empty prompt and `IncludesToolList` is false; Development file override in
  `{AppContext.BaseDirectory}/Prompts/assignment-question-system-prompt.md` wins over the
  embedded resource, and the mtime cache reloads only when the file changes (drive
  distinct mtimes via `File.SetLastWriteTimeUtc`); `BuildMessages` without
  `PromptOverride` = [system, user] with the loaded prompt as the system message and the
  request JSON (topicName) in the user message; `BuildMessages` with `PromptOverride` =
  three messages, the third user-role containing the override text, the system message
  unchanged (EC-9).
- `AssignmentQuestionResponseParserTests`: the §4.3 example payload (one MC + one TF +
  one shortAnswer) parses and validates; fenced (```json-wrapped) payload parses;
  malformed JSON rejects; MC with zero correct, MC with two correct, MC with <2 and >6
  options, TF non-canonical labels, TF ≠2 options, TF ≠1 correct, shortAnswer with
  options, blank question text, empty questions array, unknown type string — each rejects
  with `AssignmentQuestionGenerationException` (502); shortAnswer with `modelAnswer`
  accepts.
- `AssignmentQuestionGenerationServiceTests` (local private mock
  `IChatClient` implementing `GetResponseAsync` — do **not** modify the legacy private
  `MockChatClient` in `CodedValueAIServiceChatTests`, which only implements the streaming
  method): happy path returns the validated response and the outgoing messages contain
  the system prompt + user request (+ override framing when set) and `ChatOptions.ModelId`
  resolves from configuration; provider 401/403 → unauthorized friendly message, 429 →
  rate-limit message, 5xx → server-error message (drive via `HttpRequestException` with
  `StatusCode`, or `ClientResultException` where constructible) each surfacing as
  `AssignmentQuestionGenerationException` with 502; mock returning garbage text → 502
  malformed; request validation (blank topicName, count out of range, oversized
  `PromptOverride`) → 400.

`tests/SchoolCollab.Assignments.Tests.Unit/`:

- `AssignmentQuestionGeneratorTests`: 200 + valid response → questions list returned
  (enum values intact); 502 + `{"error": msg}` → `QuestionGenerationFailed` with that
  message; 400 + `{"error": msg}` → `QuestionGenerationFailed` (any non-success is a
  failure — decision (c)); network `HttpRequestException` → `QuestionGenerationFailed`
  friendly; 200 + malformed body → `QuestionGenerationFailed`.

### Constraints (repo AGENTS.md + rules)

Central Package Management — never add `Version` to a `PackageReference`; this round adds
**no packages at all**. net10.0. No MediatR (not a CQRS round — no handlers). Run
`dotnet build SchoolCollab.sln` after every change. **No git commits — working tree
only.** Primary constructors for ctor injection; DTOs are records; XML `<summary>` docs on
public types; structured logging via injected `ILogger<T>` with named placeholders;
**never throw `InvalidOperationException` from any of this round's code paths — typed
exceptions only** (ar-1 rework lesson); minimal-API endpoints grouped in an extension
method (the new endpoint gets its own `Map...` class — do NOT inline it in `Program.cs`,
and do NOT move the existing inline `/api/ai/chat` + `/api/ai/config` endpoints); never
`new OpenAIClient(...)` outside `ChatClientFactory`; the endpoint must not accept a model
string from the client (server resolves via `ChatModelResolver`).

### Acceptance criteria

Worker-facing:
- `dotnet build SchoolCollab.sln -c Debug`: 0 errors.
- `dotnet test` on `SchoolCollab.Settings.Tests.Unit`,
  `SchoolCollab.Assignments.Tests.Unit`, `SchoolCollab.ArchitectureTests.Unit`:
  0 failures.
- Changed files = the expected-files list exactly (test-file names indicative; coverage
  list binding); no unrelated deletions/reformatting; no new project, no sln/CPM/AppHost
  change.

Reviewer-facing (static, diff-only):
- Plan conformance against the expected-files list; decisions (a)–(e) implemented as
  written (concrete-type provider registration; no new project; 400/502 error contract;
  no auth requirement; AI.Abstractions DTOs + enum).
- §4.3 schema-validation correctness: exactly one `isCorrect` for multipleChoice/
  trueFalse; `modelAnswer` optional for shortAnswer; TF canonical `True`/`False`; empty
  questions rejected; fenced/prose-wrapped model output handled.
- Provider-error mapping: `FormatProviderError` conventions preserved verbatim in
  `ProviderErrorFormatter` (401/403/429/5xx branches); the `AIChatEngine` edit is limited
  to that extraction; cancellation is rethrown, never mapped to 502 (EC-2).
- `dotnet-best-practices.md` Never-list compliance incl. **typed exceptions**
  (`AssignmentQuestionGenerationException` on the AI side, `QuestionGenerationFailed` on
  the Assignments side — the suffix-less name is spec-blessed, do not flag it), CPM
  compliance (no new packages), no overwrites of pre-existing code outside plan scope
  (the only pre-existing files touched are the five listed modifications).

### Residual risks / notes for acceptance

- No live-model round trip in this round — parser tolerance (fence-strip, prose-strip,
  canonical TF enforcement) is what scripted tests cover; a live probe folds into round 3
  (wizard UI testing).
- Endpoint is anonymous (decision (d), matching `/api/ai/chat` posture) — hardening
  candidate for a later identity round.
- `propagateTenant: false` on the seam client flips to `true` when org-level prompts
  (WS-B2 `TenantAssignmentAiPrompt`) make generation tenant-aware — recorded, not
  implemented.
- ar-1 carried residual (EF owned-children replacement verification on a real provider)
  is untouched by this round and remains carried.

## Worker Report

# Round ar-2-ai-endpoint — Worker Report

## WORKER REPORT

### Changed files

Created (17 — 12 production + 2 prompts + 4 test files; test-file names are
the plan's indicative names):

- `src/AI/SchoolCollab.AI.Abstractions/AssignmentQuestionGenerationTypes.cs`
- `src/AI/SchoolCollab.AI.Server/Prompts/assignment-question-system-prompt.md`
- `src/AI/SchoolCollab.AI.Server/Prompts/assignment-question-system-prompt.original.md`
- `src/AI/SchoolCollab.AI.Server/Services/AssignmentQuestionGenerationSystemPromptProvider.cs`
- `src/AI/SchoolCollab.AI.Server/Services/ProviderErrorFormatter.cs`
- `src/AI/SchoolCollab.AI.Server/Services/AssignmentQuestionResponseParser.cs`
- `src/AI/SchoolCollab.AI.Server/Services/AssignmentQuestionGenerationException.cs`
- `src/AI/SchoolCollab.AI.Server/Services/AssignmentQuestionGenerationService.cs`
- `src/AI/SchoolCollab.AI.Server/Services/AssignmentQuestionGenerationExtensions.cs`
- `src/AI/SchoolCollab.AI.Server/Endpoints/AssignmentQuestionGenerationEndpoints.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/Services/IAssignmentQuestionGenerator.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/Services/QuestionGenerationFailed.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentQuestionGenerator.cs`
- `tests/SchoolCollab.Settings.Tests.Unit/AssignmentQuestionGenerationSystemPromptProviderTests.cs`
- `tests/SchoolCollab.Settings.Tests.Unit/AssignmentQuestionResponseParserTests.cs`
- `tests/SchoolCollab.Settings.Tests.Unit/AssignmentQuestionGenerationServiceTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentQuestionGeneratorTests.cs`

Modified (5):

- `src/AI/SchoolCollab.AI.Server/SchoolCollab.AI.Server.csproj` — added
  `<EmbeddedResource Include="Prompts\**\*.md" />`
- `src/AI/SchoolCollab.AI.Server/Services/AIChatEngine.cs` — deleted the
  private `FormatProviderError` method and changed its single call site
  to `ProviderErrorFormatter.Format(streamError)` (no other lines changed)
- `src/AI/SchoolCollab.AI.Server/Program.cs` — added
  `using SchoolCollab.AI.Endpoints;`, `builder.Services.AddAssignmentQuestionGeneration();`,
  and `app.MapAssignmentQuestionGenerationEndpoints();` (next to the existing
  `AddCodedValuesAiTools` / `MapDefaultEndpoints` calls respectively)
- `src/Assignments/SchoolCollab.Assignments.Application/ModuleServices.cs` —
  added the typed HttpClient registration against
  `https+http://settings-ai` (propagateTenant: false) and the
  `IAssignmentQuestionGenerator` transient
- `src/Assignments/SchoolCollab.Assignments.Application/SchoolCollab.Assignments.Application.csproj`
  — added the `<ProjectReference>` to `SchoolCollab.AI.Abstractions.csproj`

No other file changed. No `Directory.Packages.props` change. No new project.
No AppHost change. No `.razor` change. No new config keys.

### Build

`dotnet build SchoolCollab.sln -c Debug` → **0 errors** (17 pre-existing
warnings, unchanged).

### Tests

- `tests/SchoolCollab.Settings.Tests.Unit`: 485 passed, 0 failed
  (39 new tests across the three new AI-host test classes:
  `AssignmentQuestionGenerationSystemPromptProviderTests`,
  `AssignmentQuestionResponseParserTests`,
  `AssignmentQuestionGenerationServiceTests`).
- `tests/SchoolCollab.Assignments.Tests.Unit`: 129 passed, 0 failed
  (8 new tests in `AssignmentQuestionGeneratorTests`).
- `tests/SchoolCollab.ArchitectureTests.Unit`: 20 passed, 0 failed.

### Deviations from plan

- **`ClientResultException` construction in the service test.** The
  `System.ClientModel` 1.10.0 public ctor signature is
  `ClientResultException(PipelineResponse, Exception?)` (not
  `(HttpStatusCode, string)`). The plan's coverage list says
  "`ClientResultException` where constructible" — the test still exercises
  it via `RuntimeHelpers.GetUninitializedObject` + reflection on the
  private `_status` backing field so the production
  `ProviderErrorFormatter.Format` mapping (ex.Status → 401 → "unauthorised"
  message) is verified end-to-end. Behaviour identical from the production
  code's perspective.
- **`JsonSerializer.Serialize(request, JsonSerializerDefaults.Web)` does not
  compile in .NET 10.** The plan's prompt-provider description says
  "serialized request (Web/camelCase JSON)". I created a static
  `JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)`
  field and used `JsonSerializer.Serialize(request, _jsonOptions)` —
  byte-equivalent output (Web defaults: camelCase, case-insensitive
  enum-as-string is irrelevant here because `GeneratedQuestionType` is
  serialised as a string via the `JsonStringEnumConverter` attribute that
  the plan also prescribes). No behaviour change.
- **Test class file name `AssignmentQuestionGenerationServiceTests` is the
  indicative name from the plan's expected-files list (bound by coverage
  list), kept as-is.** Same for
  `AssignmentQuestionGenerationSystemPromptProviderTests` and
  `AssignmentQuestionResponseParserTests`.

All other implementation steps were followed literally; the
expected-files list is matched exactly (the only difference is the
class-test name pattern matches the plan's "binding test coverage list").

## Review
- Correct: all 22 expected files changed and only those; 17 created + 5 modified matches the plan exactly; no unplanned files or deletions.
- Correct: decision (a) - AssignmentQuestionGenerationService calls IChatClientFactory.GetClient().GetResponseAsync directly; provider registered as concrete AssignmentQuestionGenerationSystemPromptProvider; IAssignmentQuestionGenerator returns Task<IReadOnlyList<GeneratedQuestionDto>>.
- Correct: decision (b) - prompt files and provider live in SchoolCollab.AI.Server; csproj adds EmbeddedResource for Prompts.
- Correct: decision (c) - endpoint returns 400 for validation and 502 for upstream/malformed; both bodies serialize {"error":...}; AssignmentQuestionGenerator maps non-success/network/parse failures to QuestionGenerationFailed.
- Correct: decision (d) - endpoint has no RequireAuthorization; seam client uses propagateTenant: false.
- Correct: decision (e) - request/response DTOs and GeneratedQuestionType enum with JsonStringEnumConverter live in SchoolCollab.AI.Abstractions.
- Correct: §4.3 schema validation enforces exactly one isCorrect for MC/TF, optional modelAnswer for shortAnswer, canonical TF labels, rejects malformed JSON with no partial state.
- Correct: AIChatEngine.FormatProviderError extracted verbatim to ProviderErrorFormatter.Format; call site updated; logic byte-identical.
- Correct: binding test coverage exists and asserts all listed behaviors across both test projects.
- Correct: no Version attributes added, no new packages, no MediatR, no .razor/wwwroot/ApiClient/AppHost/sln changes, typed exceptions used, structured logging present.

Verdict: PASS
P1:
P2:
Best-practices: no overwrites outside plan scope / skills honored / readable

## Acceptance

**Verdict: CLOSED** — all plan criteria MET; reviewer verdict **PASS with zero P1s
and zero P2s, so no rework iteration was needed** (single worker pass accepted);
authoritative build + tests green; scope exact; backend round (no UI-tester handover).
Residuals below are carried follow-ups — none blocking.

### Authoritative numbers (numbers of record — parent logs)

| Check | Log | Result |
|---|---|---|
| `dotnet build SchoolCollab.sln -c Debug` | `.ar-2-build.log` | "Build succeeded" — **0 Error(s)**, 6 Warning(s) (pre-existing NuGet NU1902/NU1903 advisories; unchanged by this round) |
| `dotnet test` SchoolCollab.Settings.Tests.Unit | `.ar-2-test-settings.log` | Test run summary: Passed! — **485 passed / 0 failed** (total 485, skipped 0) |
| `dotnet test` SchoolCollab.Assignments.Tests.Unit | `.ar-2-test-assignments.log` | Test run summary: Passed! — **129 passed / 0 failed** (total 129, skipped 0) |
| `dotnet test` SchoolCollab.ArchitectureTests.Unit | `.ar-2-test-arch.log` | Test run summary: Passed! — **20 passed / 0 failed** (total 20, skipped 0) |

The worker self-reported exactly these totals (485/0, 129/0, 20/0) plus 39 new Settings
tests and 8 new Assignments tests; the new-test counts are confirmed by a static count of
the four new test files: 38 `[TestMethod]` + one double-`[DataRow]` method (the blank-text
empty/whitespace pair in `AssignmentQuestionResponseParserTests`) = 39 in Settings, and
8 `[TestMethod]` = 8 in Assignments. Two counting-only slips in the Worker Report prose,
neither material: "17 pre-existing warnings" vs the log's 6 (restore-output double
counting — 0 errors either way), and "12 production" files vs the listed 11 (the
enumerated 17-file list itself is correct and matches git status exactly).

### Plan criteria checklist — worker-facing

- **`dotnet build SchoolCollab.sln -c Debug`: 0 errors — MET.** `.ar-2-build.log`:
  "Build succeeded. … 6 Warning(s) / 0 Error(s)".
- **All three required test projects 0 failures — MET.** 485/0 Settings, 129/0
  Assignments, 20/0 ArchitectureTests (table above).
- **Changed files = the expected-files list exactly; no unrelated deletions/reformatting;
  no new project, no sln/CPM/AppHost change — MET.** Scope check (`.ar-2-scope.txt`) and
  patch cross-check: the 22 paths under `src`/`tests` (17 created + 5 modified) are
  file-for-file identical to the plan's expected-files list; the patch is 22 files,
  2000 insertions / 25 deletions, with deletions confined to the planned `AIChatEngine`
  extraction and edited lines inside the four other planned modifications; no
  `Directory.Packages.props`, `SchoolCollab.sln`, or AppHost change anywhere in the patch.

### Plan criteria checklist — reviewer-facing

- **Plan conformance against the expected-files list — MET.** Reviewer: "all 22 expected
  files changed and only those; 17 created + 5 modified matches the plan exactly; no
  unplanned files or deletions."
- **Decisions (a)–(e) implemented as written — MET.** Reviewer verified each:
  (a) direct `IChatClientFactory.GetClient().GetResponseAsync` + concrete-type provider
  registration + `Task<IReadOnlyList<GeneratedQuestionDto>>` seam; (b) prompt files and
  provider in `SchoolCollab.AI.Server` with `EmbeddedResource`; (c) 400/502 +
  `{"error":...}` bodies + `QuestionGenerationFailed` mapping; (d) no
  `RequireAuthorization` + `propagateTenant: false`; (e) DTOs + `GeneratedQuestionType`
  enum in `SchoolCollab.AI.Abstractions`. Independently spot-checked at acceptance:
  zero `RequireAuthorization` occurrences in the patch; `propagateTenant: false` on the
  new client registration (ModuleServices hunk — the pre-existing tenant-propagating
  `AssignmentsApiClient` line is unchanged context); the `AIChatEngine.cs` hunk is
  exactly the call-site swap + private-method removal.
- **§4.3 schema-validation correctness — MET.** Reviewer: exactly one `isCorrect` for
  MC/TF, optional `modelAnswer` for shortAnswer, canonical TF labels, empty-questions
  rejected, fenced/wrapped output handled, malformed JSON rejected with no partial state.
- **Provider-error mapping — MET.** Reviewer: `FormatProviderError` extracted verbatim
  to `ProviderErrorFormatter.Format`, call site updated, logic byte-identical; the
  `AIChatEngine` edit is limited to that extraction; cancellation rethrown, never mapped
  to 502 (EC-2).
- **`dotnet-best-practices.md` Never-list compliance — MET.** Reviewer: typed exceptions
  on both sides (`AssignmentQuestionGenerationException`; spec-blessed suffix-less
  `QuestionGenerationFailed`), no `Version` attributes, no new packages, no MediatR,
  structured logging present, no overwrites outside the five listed modifications.

### Review adjudication (REVIEW block above vs plan criteria)

- **Reviewer verdict: PASS — zero P1s, zero P2s.** No rework iteration was needed; the
  worker's single pass is accepted as delivered.
- **Worker deviation 1 — `ClientResultException` in
  `AssignmentQuestionGenerationServiceTests` built via
  `RuntimeHelpers.GetUninitializedObject` + reflection on the private `_status` field**
  (System.ClientModel 1.10.0's public ctor takes `PipelineResponse`, so the plan's
  "`ClientResultException` where constructible" had no ordinary construction path):
  **ACCEPT as a documented residual note — no rework.** Test-construction-only (no
  production code touched), behaviour-preserving, and it still exercises the real
  `ProviderErrorFormatter.Format` 401→unauthorised mapping end-to-end. Maintenance note
  carried below: the reflection breaks if `_status` is renamed.
- **Worker deviation 2 — static `JsonSerializerOptions _jsonOptions =
  new(JsonSerializerDefaults.Web)` field instead of an inline
  `JsonSerializer.Serialize(request, JsonSerializerDefaults.Web)` call** (the inline
  overload does not compile on .NET 10): **ACCEPT as a documented residual note — no
  rework.** Byte-equivalent Web/camelCase serialization; production behaviour unchanged.
- **Worker deviation 3 — test-file names kept as the plan's indicative names:** not a
  deviation in substance — the plan itself marks test-file names indicative with the
  coverage list binding, and the reviewer confirmed the binding coverage exists. Noted,
  no action.

### Scope adjudication

`.ar-2-scope.txt` checked against the plan expected-files list: the 22 paths under
`src/`/`tests/` are **exactly** the expected files — 17 created + 5 modified, no extras,
no deletions. Everything else in the snapshot is a pre-round dirty path recorded in the
doc header (the four `documents/rounds` leftovers), a round artifact
(`round-ar-2-ai-endpoint.md`, `diffs-ar-2-ai-endpoint.patch`, `.ar-2-worker-report.md`),
or — noted here as an out-of-round observation — one additional non-patch dirty path the
header omitted: `documents/solution/assignment-request-implementation-details.md`. Its
working-tree diff is the §3 round-log **pause entry written before implementation**
("PAUSED … before implementation … no round-2 artifacts on disk, no code changes" +
resume checklist) — parent-side orchestration bookkeeping from the interrupted plan
phase, not a worker change: it is not in the pathspec-limited round patch and the
worker's change set is complete without it. **No scope creep — no P1.**

### UI-round trigger determination

Patch inspected (`diffs-ar-2-ai-endpoint.patch`, 22 files — all under `src/AI/**`,
`src/Assignments/**`, `tests/**`): zero `.razor` / `.razor.css` / `.css` / `.js` files,
nothing under `wwwroot`, and no ApiClient/Blazor client project file changed. The two
`Prompts/*.md` files are AI.Server embedded prompt resources, not UI files, and do not
trigger (as expected — backend phase 5–6 round; §10.7 wizard UI is round 3).
**Backend round — NO UI-tester handover.**

### Residuals / carried follow-ups

1. **Anonymous AI-host endpoint** (decision (d), matching `/api/ai/chat` posture) —
   accepted posture pending the identity round (solution doc §4 item 1); carried.
2. **`propagateTenant: false`** on the seam client flips to `true` when org-level prompts
   (WS-B2 `TenantAssignmentAiPrompt`) make generation tenant-aware — recorded, not
   implemented; carried.
3. **No live-model round trip** — parser tolerance (fence-strip, prose-strip, canonical TF
   enforcement) is covered by scripted tests only; the live probe folds into round 3
   (wizard UI testing); carried.
4. **ar-1 carried residual** (EF owned-children replacement verification on a real
   provider) — untouched by this round, remains carried.
5. **Test-construction notes** (adjudicated above): the reflection-built
   `ClientResultException` in `AssignmentQuestionGenerationServiceTests` breaks if
   System.ClientModel renames the private `_status` field — replace when a constructible
   public overload appears; the static `JsonSerializerOptions` field is the canonical
   .NET 10 shape, no action.

### Round state

- No git commits; **no staged files** (`git diff --cached` empty; the 17 ` A` index
  entries are intent-to-add markers from patch generation, listed under "Changes not
  staged for commit").
- Recommended next round: round 3 — wizard UI + ApiClient consumption of the seam
  (spec §10.7), which also picks up the live-model probe residual.
