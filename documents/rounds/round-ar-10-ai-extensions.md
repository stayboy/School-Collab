# Round ar-10-ai-extensions — WS-B2: difficulty distribution + org-level TenantAssignmentAiPrompt + versioned question-draft regeneration + URL resource ingestion

Provider: pi — **EXECUTION MODE (owner decision 2026-09-14): SOLO on the current session model (`ollama-cloud/deepseek-v4.1-flash`).** The parent agent implements this round directly, step by step, with a build after each step and a test run at each step end; the four-agent role contracts (worker / static reviewer / UI tester) are retained as the checklists the parent self-applies, and the parent writes `.ar-10-worker-report.md`. No subagent worker is dispatched for this round. **Subagent worker ladder (recorded for future worker dispatches only — NOT used here):** the pi subagent runner cannot resolve the session id `ollama-cloud/deepseek-v4.1-flash` (its namespace is the local `ollama` provider in `~/.pi/agent/models.json`, which holds exactly `deepseek-v4-pro:cloud`, `deepseek-v4-flash:0731-cloud`, `deepseek-v4-flash:cloud`); the resolvable ladder is ① `ollama/deepseek-v4-flash:cloud` → ② `ollama/deepseek-v4-flash:0731-cloud`. UI round — Create.razor, Edit.razor, ResourcesSection.razor, QuestionGenerationSection.razor + the new QuestionsDraftSection.razor + the new Settings AssignmentAiPrompt page ship → the tester pass (parent self-applied in solo mode) fires after acceptance.

- **Tier:** 3 workload (full four-agent checks) executed **SOLO** (owner decision — see the provider line). The round touches six `.razor` surfaces. **This is a LARGE round: ~68 files across THREE bounded contexts (Settings, Assignments, AI) + Admin.Shared/Admin UI with TWO additive migrations and ONE new CPM package. Solo discipline: land steps in order, each step leaves the tree build-coherent, build after every step, report progress to `.ar-10-worker-report.md`, and stop at a step boundary when the session budget runs short — the next session resumes the bounded remainder. Never skip silently, never reverse the order.** **Prepared pass budget (parent, 2026-09-14):** pass 1 = steps 1–2 (Settings backend + Admin surface); pass 2 = steps 3–4 (AI seam + Assignments domain/contracts); pass 3 = steps 5–6 (draft CQRS/routes/resolver + wizard surfaces); pass 4 = steps 7–8 (draft UI + full matrix + freeze). The cut-line may move each boundary session-by-session; the parent re-slices every dispatch against the reported remainder.
- **Round base — REFRESHED 2026-09-11 (post-ar-9 drift pass):** ar-9 (WS-C1/C2/C4 guardian sign-off) executed first and landed as PR **#231** (`stack/9-ar-9-signoff`); it replaces the “next round after this one” reference in the Out-list below. **Drift refresh performed and clean:** all 19 expected modified/precedent files exist at their planned paths (zero file-map drift); verified seams — `Assignment.Create`/`Update` both end `bool requiresSignature = false` (the difficulty params slot after it, decision (c)); `AI.Server/Program.cs` has `TryAddTransient<TenantForwardingDelegatingHandler>()` + `AddCodedValuesAiTools` (decision (e) insertion point); `Settings.Api/Program.cs` has `MapAssignmentPolicyEndpoints` (74) + `MapSignatureConsentTextEndpoints` (76) (decision (a) inserts after the policy call); `AssignmentQuestionGenerator` client still registers `propagateTenant: false` (decision (e) flip target); `QuestionOptionDtoValidator.ValidateQuestions` present at both create/update call sites (decision (d) validate-before-stage); `AssignmentQuestionGenerationSystemPromptProvider.BuildMessages(request)` still single-arg (decision (e) adds the org-prompt parameter); `HtmlAgilityPack` absent from `Directory.Packages.props` (decision (g) adds the one CPM entry). **ar-9-introduced facts that touch this round:** `Assignments.Api.Tests.Unit` gained a `Microsoft.AspNetCore.Mvc.Testing` reference and the minimal-TestServer route-test pattern (`SignOffRoutesTests`) — `AssignmentDraftRoutesTests` should reuse that harness rather than inventing one; `bUnit` test fixtures must give distinct `@key` values per row (an ar-9 fixture defect broke PR #231 CI). **Round base (recorded 2026-09-14): `main` @ `d6d544ce`** — the squash-merge commit of PR #231 (ar-9). Branch `stack/10-ar-10-ai-extensions` was cut from it and registered via `gh stack init` (single layer, trunk `main`, local-only — no push yet). **Tracked tree clean at base.**
- **Known provider risk (carried from ar-8):** `ollama/deepseek-v4-flash:0731-cloud` hit a weekly usage limit during ar-8's mechanical phase (429). If a subagent run dies on a 429, surface it to the owner immediately — do NOT retry-loop. **Also recorded (ar-9):** the pi subagent runner does NOT resolve the ids advertised by `{action:"models"}` (e.g. `clinepass/...`, `cline/...`, `ollama-cloud/...` all failed with `Model "..." not found`); the resolvable namespace is the local `ollama` provider — pass `ollama/<id>` from `~/.pi/agent/models.json`. **Binding for this round's worker dispatch:** the owner-named `ollama-cloud/deepseek-v4.1-flash` does not resolve — use the ladder in the provider line above, confirmed at the dispatch preflight; a 429 on any deepseek-flash id → surface to the owner immediately, do NOT retry-loop.
- **Rounds 1–9 landed what this round builds on** (do NOT re-plan any of it): questions/options/attachments + `AiPromptOverride` (ar-1); the anonymous generation endpoint `POST /api/ai/assignments/questions` + `QuestionGenerationRequest`/`GeneratedQuestionDto` in AI.Abstractions (ar-2); the wizard question UI + `QuestionGenerationSection`/`QuestionEditorSection`/`QuestionReviewList` (ar-3); `AssignmentResource`/`NewResourceDto` + staging (ar-4); lifecycle/approval (ar-5); scoring (ar-6); duplicate-as-template (ar-7); `TenantAssignmentPolicy` policy-pair precedent + `ISignatureDefaultResolver` fail-open resolver pattern + `propagateTenant` clients (ar-8); **guardian sign-off domain + e-sign UI + locking/consent-text (ar-9, #231)** — which also landed the `Settings` consent-text tenant-override row, the `SignatureConsentTextApiClient` mirror this round's `AssignmentAiPromptApiClient` should follow, and the `FluentBadge` render-safe-triad constraint (4.14.2).
- **Parent-adjudicated scope note (binding):** the implementation-details §2 WS-B2 list is the design brief, and two of its open sub-decisions are adjudicated here: (1) difficulty distribution = **three nullable int columns** on `Assignment` (not a ratio string); (2) versioned regeneration v1 = **single `QuestionsDraftJson` blob column on `Assignment`** (the doc's own recommendation — NOT a `QuestionGenerationDraft` row table); (3) URL extraction = **HtmlAgilityPack (new CPM entry)**, extraction performed in the **Assignments Application** (server-side, not in AI.Server — keeps the AI endpoint stateless apart from the org prompt read).
- **Sources:** `documents/solution/assignment-request-implementation-details.md` §2 WS-B2 (THE design brief) + §3 round-slicing row 9 + §4 risks; `documents/specs/assignment-request-feature-spec.md` §3.4 (lines 68–73, 91: org-level system prompt "locked or editable by admin", author-specified count/type-mix/difficulty-distribution, versioned regenerate-without-destroying, `PromptConfig` shape); `documents/rounds/round-ar-8-signature-defaults.md` (house format + the policy/resolver precedents to mirror); `.github/copilot/rules/dotnet-best-practices.md`; `.github/copilot/rules/blazor-components.md`; `.github/copilot/rules/ef-migrations.md`; `.github/skills/input-width-scale/SKILL.md` (W-ladder for the new number/text fields).

## Plan

### Goal

Execute workstream **WS-B2** (implementation-details §2 WS-B2; round log §3 row 9): the four AR extensions on top of the shipped B1 generation surface, end-to-end. Concretely:

1. **Difficulty distribution** — three nullable int columns (`DifficultyEasyCount`/`DifficultyMediumCount`/`DifficultyHardCount`) on `Assignment`, threaded through create/update/duplicate/summary round-trip, carried into `QuestionGenerationRequest`, framed in the generation prompt, and editable in the wizard (`QuestionGenerationSection`).
2. **Org-level system prompt** — `TenantAssignmentAiPrompt` (Settings.Core; one row per tenant; `SystemPrompt string?` capped 4000 + `IsLocked bool`) with CQRS + `GET/PUT /api/settings/assignment-ai-prompt` + additive migration + Admin UI page (`/assignment-ai-prompt`, editable + lockable). The AI server reads it per generation call (tenant-propagated settings-api client) and layers it: org prompt replaces the embedded default; `AiPromptOverride` stays user framing; when `IsLocked`, the override is ignored server-side AND disabled in the wizard. The `propagateTenant: false` on the generator client (the documented ar-2 flip note) flips to `true`.
3. **Versioned regeneration** — `QuestionsDraftJson` blob column on `Assignment` + stage/confirm/discard commands + `GET/PUT/POST/DELETE /assignments/{id}/questions-draft` routes + a new `QuestionsDraftSection.razor` on the Edit page (Draft-only): regenerate stages a draft server-side, the existing questions stay intact until Confirm materializes the draft into the owned collection; Discard clears it.
4. **URL ingestion** — `HtmlAgilityPack`-based `IUrlTextExtractor` in the Assignments Application; a URL row list in `ResourcesSection` feeding `NewResourceDto` on create; up to 3 included URLs are fetched (http/https only, 5s timeout, 20k-char text cap), stripped to text, and passed to the generation request as `ResourceTexts`, framed as reference material in the prompt. Per-URL fetch failure is fail-open with a visible warning.

### Scope

**In (fixed — this is the whole round):** everything in the four numbered points above, the two additive migrations (Settings `tenant_assignment_ai_prompts`; Assignments `assignments.difficulty_*` × 3 + `assignments.questions_draft_json`), the ONE CPM entry (`HtmlAgilityPack` → Assignments.Application only), the Admin.Shared `AssignmentAiPromptApiClient` (+ its Settings.Application ModuleServices registration), and the binding test coverage list below.

**Out (record — do not touch):** the C1/C2 sign-off domain + UI (**landed in ar-9 / #231** — `SignOffState`, `SignatureEvent`, tenant consent text, locking, the sign-off Detail surface; only the C3 certificate remains open, and it is not this round's); the approver "AI-generated" provenance marker (Phase-5 backlog — needs per-question provenance tracking, NOT in this round); file/PDF/docx resource extraction (Phase-5+ per the brief — this round extracts URL resources ONLY); regeneration for `Scheduled` assignments (Draft-only, recorded); a `QuestionsDraftJson` surface on Detail.razor (the Edit section owns it); `documents/configuration.md` (no new flags/params — no new feature flag is introduced this round); the AI chat/coded-values surfaces (untouched); existing migrations; the pre-round untracked scratch files listed in the header; `wwwroot/app.css`.

### Decisions (binding — implement as written; (a)–(h) are the parent-adjudicated design)

- **(a) Settings — `TenantAssignmentAiPrompt` (the `TenantAssignmentPolicy` precedent from ar-8, mirrored exactly).** New `src/Settings/SchoolCollab.Settings.Core/Domain/TenantAssignmentAiPrompt.cs`: `sealed class TenantAssignmentAiPrompt : BaseTenantEntityWithAudit, IHasRowVersion`, private ctor, `string? SystemPrompt { get; private set; }` (nullable = no org prompt set ⇒ AI uses the embedded default), `bool IsLocked { get; private set; }` (default false; locked ⇒ teacher guidance/override is suppressed), `uint RowVersion`, `static TenantAssignmentAiPrompt Create(Guid tenantId, string? systemPrompt = null, bool isLocked = false)` and `void SetPrompt(string? systemPrompt, bool isLocked)` — the factory/setter trims the prompt and **throws `ArgumentException` when the trimmed prompt exceeds 4000 characters** (matches `AssignmentQuestionGenerationService.MaxPromptOverrideLength` and the `AiPromptOverride` column cap; this is the round's ONE new domain validation throw — a typed `ArgumentException`, not an `InvalidOperationException`). New `Data/Configurations/TenantAssignmentAiPromptConfiguration.cs` mirroring `TenantAssignmentPolicyConfiguration`: `TenantEntityTypeConfigurationBase<TenantAssignmentAiPrompt>`, `ToTable("tenant_assignment_ai_prompts")`, audit + soft-delete + query-filter + `ConfigurePostgresRowVersion`. `SettingsDbContext` gains the `DbSet` + the `ApplyConfiguration(new TenantAssignmentAiPromptConfiguration(() => CurrentTenantId))` line (explicit instantiation, no scanning). New `DTOs/TenantAssignmentAiPromptDto.cs`: `sealed record TenantAssignmentAiPromptDto(string? SystemPrompt, bool IsLocked)`. New CQRS folder `CQRS/AssignmentAiPrompts/`: `Queries/GetTenantAssignmentAiPrompt/GetTenantAssignmentAiPrompt : IQuery<TenantAssignmentAiPromptDto?>` with `static readonly … Instance = new()` + handler returning the single tenant row or null; `Commands/UpsertTenantAssignmentAiPrompt/UpsertTenantAssignmentAiPrompt(string? SystemPrompt, bool IsLocked) : ICommand` + handler `ICommandHandler<UpsertTenantAssignmentAiPrompt, TenantAssignmentAiPromptDto>` mirroring `UpsertTenantAssignmentPolicyHandler` (load existing → `SetPrompt`, else `Create` + `Add`; return the DTO). API: new `Endpoints/AssignmentAiPromptRoutes.cs` mirroring `AssignmentPolicyRoutes` — `MapAssignmentAiPromptRoutes(this RouteGroupBuilder group)` with `GET /assignment-ai-prompt` (null → `Results.NoContent()`, else `Results.Ok(dto)`) and `PUT /assignment-ai-prompt` (`[FromBody] UpsertTenantAssignmentAiPromptRequest(string? SystemPrompt, bool IsLocked)` → `Results.Ok(result)`); catch `ArgumentException` → `Results.Json(new { error = ex.Message }, statusCode: 400)`. New `AssignmentAiPromptEndpoints.cs` mirroring `AssignmentPolicyEndpoints` (`MapAssignmentAiPromptEndpoints(this WebApplication app, IFeatureFlagService featureFlags)`: `app.MapGroup("/api/settings")`, `RequireAuthorization()` unless `FeatureFlagKeys.DisableOIDCAuth`, then `group.MapAssignmentAiPromptRoutes()`); `Settings.Api/Program.cs` gains `app.MapAssignmentAiPromptEndpoints(featureFlags);` directly after the `MapAssignmentPolicyEndpoints` call. **Additive migration** `AddTenantAssignmentAiPrompt` (EF-generated; migration + Designer + snapshot edit).
- **(b) Admin surface — client + page + dashboard entry.** New `src/SchoolCollab.Admin.Shared/Services/AssignmentAiPromptApiClient.cs` mirroring `AssignmentPolicyApiClient` line-for-line (primary ctor `HttpClient http`, re-declared wire DTOs at the bottom — NO Settings reference, the `AssignmentPolicyApiClient` comment explains why): `GetAsync(CancellationToken ct = default)` → `Task<TenantAssignmentAiPromptDto?>` (`GET /api/settings/assignment-ai-prompt`, null on 204) and `UpsertAsync(string? systemPrompt, bool isLocked, CancellationToken ct = default)` (PUT; 400 → surface the error body message as an exception the page can show). Registered in `src/Settings/SchoolCollab.Settings.Application/ModuleServices.cs` directly after the existing policy/notification client blocks — `AddHttpClient<AssignmentAiPromptApiClient>` with base address `https+http://settings-api` **and `TenantPropagationDelegatingHandler`** (STRICT tenant entity — the ModuleServices comment block documents the wrong-tenant symptom class; a missing handler is a P1). New page `src/Settings/SchoolCollab.Settings.Application/Components/Pages/AssignmentAiPrompt.razor` — `@page "/assignment-ai-prompt"`, a single `FluentCard`-based editor (NOT a dialog; the ConfigFlagDetail page is the layout precedent): `FluentTextArea` "Organization AI prompt for assignment questions" (`MaxLength="4000"`, `Rows="8"`, `class="full-width"`, placeholder noting "Leave empty to use the built-in default prompt"), a `FluentSwitch` "Locked — prevent teachers from adding their own AI guidance", a Save `FluentButton` (Accent, disabled while saving), Cancel-via-back link, loading `FluentProgressRing`, per-failure `FluentMessageBar` (the `_error` idiom), success confirmation via a transient `_saved` flag or message bar. Save → `Api.UpsertAsync(prompt, locked)` then reload. `src/SchoolCollab.Admin/Components/Pages/Settings.razor` gains a fourth `DashboardItem` (`Href: "/assignment-ai-prompt"`, Title "Assignment AI Prompt", Description "Organization-level AI prompt and teacher-override lock for question generation.", a Size24 icon — pick from the verified FluentIcons set). **Styling: existing utility classes only — NO `<style>` block, no inline `style=`, no new `.razor.css`.**
- **(c) Assignments domain + contracts — difficulty distribution columns.** `Assignment.cs`: `public int? DifficultyEasyCount { get; private set; }`, `DifficultyMediumCount`, `DifficultyHardCount` (XML docs citing WS-B2 / spec §3.4 line 70 — optional requested per-difficulty counts; null = let the model decide; no cross-field sum validation — the prompt reconciles). `Create(...)` and `Update(...)` each gain **three trailing named-arg params** `int? difficultyEasy = null, int? difficultyMedium = null, int? difficultyHard = null` positioned after `requiresSignature`; both validate `>= 0` when set (`ArgumentException`, the MaxAttempts precedent). `ContractTypes.cs`: `CreateAssignmentRequest`, `UpdateAssignmentRequest`, AND `AssignmentSummaryDto` each gain the three trailing fields (same names, XML doc citing WS-B2). `CreateAssignmentCommand`/`UpdateAssignmentCommand` + handlers thread them by named arg (`difficultyEasy: source.DifficultyEasyCount`, …). `AssignmentRoutes.cs` POST/PUT thread `req.*`. **BOTH** entity→DTO mapping sites set them by named arg — `GetAssignmentByIdQueryHandler` and `ListAssignmentsQueryHandler` (follow the existing `PassScore`/`MaxAttempts` flow including any intermediate projection — omitting them silently resets difficulty on every read-back). `DuplicateAssignmentCommandHandler` gains the three named args on its `Create` call (full scalar-clone precedent). `AssignmentEditFormModel.cs`: `public int? DifficultyEasyCount { get; set; }` (+ Medium/Hard) + `LoadFrom` sets them from the DTO; `ToCreateRequest` threads them (`DifficultyEasyCount: DifficultyEasyCount`, …). `Edit.razor`: three private `int?` fields loaded from `_item` beside `_requiresSignature` and threaded into the update request — **the round-trip is REQUIRED** (the ar-8 silent-reset lesson; no visible Edit UI for them this round — the wizard owns the authoring surface).
- **(d) Versioned regeneration — `QuestionsDraftJson` blob (v1 = single column, the brief's own recommendation).** `Assignment.cs`: `public string? QuestionsDraftJson { get; private set; }` (XML doc: WS-B2 / spec §3.4 line 73 — staged-but-unconfirmed AI question set; only confirmed questions enter the owned `_questions` collection; JSON of `NewQuestionDto[]`, validated before staging so the blob is always parseable) + three domain methods: `StageQuestionsDraft(string questionsJson)` (Draft-only guard → `AssignmentDomainException`-style typed throw — reuse the same exception type/shape the aggregate already uses for state guards, e.g. the `Update` guard's `InvalidOperationException` posture is NOT to be copied: use the existing typed state-guard exception the file already favours for lifecycle misuse; trims null/whitespace → `ArgumentException`; stamps `UpdatedAt`), `DiscardQuestionsDraft()` (Draft-only guard; sets null; stamps `UpdatedAt`), `ConfirmQuestionsDraft()` (Draft-only guard; sets null; stamps `UpdatedAt` — the handler materializes the questions BEFORE calling this, so confirm is atomic from the caller's perspective). New `Domain/Exceptions/InvalidQuestionsDraftException.cs` (typed, mirrors `AssignmentApprovalRequiredException` posture) — thrown by the confirm/discard **handlers** when the blob is missing or fails to parse (defensive — staging validates first). New CQRS folder `CQRS/Assignments/Commands/QuestionsDraft/`: `StageQuestionsDraftCommand(Guid AssignmentId, IReadOnlyList<NewQuestionDto> Questions)` + handler — assignment-not-found → the existing `AssignmentNotFoundException`; status guard via the domain method; **validate with `QuestionOptionDtoValidator.ValidateQuestions(command.Questions)` FIRST** (the update handler's precedent — invalid drafts never enter the blob), serialize with the same `JsonSerializerOptions(JsonSerializerDefaults.Web)` used across the Api, `assignment.StageQuestionsDraft(json)`, save; `ConfirmQuestionsDraftCommand(Guid AssignmentId)` + handler — load, blob null → `InvalidQuestionsDraftException`; parse → `List<NewQuestionDto>` (parse failure → same); **materialize: remove every existing question the same way the `UpdateAssignmentCommandHandler` question-sync block removes questions, then add each draft question via the same add path that handler uses for new questions** (mirror its loop shape exactly — `AssignmentContentValidator` applies if that is what create/update call); `assignment.ConfirmQuestionsDraft()`; save; `DiscardQuestionsDraftCommand(Guid AssignmentId)` + handler — `DiscardQuestionsDraft()`, save. New `CQRS/Assignments/Queries/GetQuestionsDraft/GetQuestionsDraftQuery(Guid AssignmentId) : IQuery<IReadOnlyList<NewQuestionDto>?>` + handler (blob null → null; else parse + return — parse failure → `InvalidQuestionsDraftException`, defensive). Routes in `AssignmentRoutes.cs` (after the existing `/{id:guid}` group members): `GET /{id:guid}/questions-draft` (200 `{ questions: [...] }` or 204 when none; `AssignmentNotFoundException` → 404), `PUT /{id:guid}/questions-draft` (body `{ questions: [...] }` → 204; validation `ArgumentException` → 400), `POST /{id:guid}/questions-draft/confirm` (200 + the updated `AssignmentSummaryDto`; `InvalidQuestionsDraftException` → 409), `DELETE /{id:guid}/questions-draft` (204) — all four catch `AssignmentNotFoundException` → 404. **Additive migration** (ONE, together with (c)): `AddAssignmentAiConfig` — `difficulty_easy_count int NULL`, `difficulty_medium_count int NULL`, `difficulty_hard_count int NULL`, `questions_draft_json text NULL` on `assignments` (migration + Designer + snapshot edit). **Recorded:** `DuplicateAssignmentCommandHandler` does NOT copy the draft blob (staged, unconfirmed work; the copy is a scalar clone). `AssignmentsApiClient.cs` gains `StageQuestionsDraftAsync(Guid id, IReadOnlyList<NewQuestionDto> questions, …)`, `GetQuestionsDraftAsync(Guid id, …) → Task<IReadOnlyList<NewQuestionDto>?>`, `ConfirmQuestionsDraftAsync(Guid id, …) → Task<AssignmentSummaryDto>`, `DiscardQuestionsDraftAsync(Guid id, …)` (+ the private wire records at the bottom of the class, the round-7 `IdResponse` precedent, camel-case via the existing `_jsonOptions`).
- **(e) AI seam — org-prompt layering + request extensions.** `AI.Abstractions/AssignmentQuestionGenerationTypes.cs`: `QuestionGenerationRequest` gains trailing `int? DifficultyEasyCount = null, int? DifficultyMediumCount = null, int? DifficultyHardCount = null, IReadOnlyList<string>? ResourceTexts = null` (XML docs citing WS-B2). `AssignmentQuestionGenerationService`: `ValidateRequest` adds — each difficulty value `< 0` → 400; `ResourceTexts` count `> 5` → 400; any entry `> 20000` chars → 400 (defensive mirrors of the wizard-side caps). New `src/AI/SchoolCollab.AI.Server/Services/TenantAssignmentAiPromptProvider.cs`: primary-ctor `(HttpClient http, ILogger<TenantAssignmentAiPromptProvider> logger)`, `Task<TenantAssignmentAiPromptInfo?> FetchAsync(CancellationToken ct)` where `record TenantAssignmentAiPromptInfo(string? SystemPrompt, bool IsLocked)` — `GET /api/settings/assignment-ai-prompt` on the settings-api base address; **204/404 ⇒ null; `HttpRequestException` ⇒ `LogWarning` + null (fail-open to the embedded default); non-success ⇒ warning + null**; never throws to the caller. DI in `AI.Server/Program.cs` directly after the `AddCodedValuesAiTools` block: `builder.Services.AddHttpClient<TenantAssignmentAiPromptProvider>(client => client.BaseAddress = new Uri("https+http://settings-api"))` **with `.AddHttpMessageHandler<TenantForwardingDelegatingHandler>()`** (the handler is already `TryAddTransient`-registered there — the tenant header MUST travel, this is the whole point of the round). `AssignmentQuestionGenerationService` gains the provider as a ctor dependency; `GenerateAsync` becomes: fetch org info (fail-open null) → build messages via the prompt provider **passing the org prompt** → `AssignmentQuestionGenerationSystemPromptProvider.BuildMessages(request, orgSystemPrompt)` where a non-empty `orgSystemPrompt` **REPLACES the embedded system message** (the org prompt IS the tenant's system prompt; the embedded file remains the no-org-prompt default) and, **when `isLocked`, `request.PromptOverride` is ignored entirely** (defence-in-depth under the wizard's disabled textarea). `BuildMessages` framing additions: a difficulty line ("Difficulty distribution: X easy / Y medium / Z hard questions." — only the non-null ones, appended to the user-role request text) and a reference-material block (each `ResourceTexts` entry as a fenced excerpt under "Reference material excerpts to ground the questions:" — only when present). Update the endpoint's XML doc comment: the surface now reads tenant data (the org prompt) via the propagated tenant header — the anonymous dev posture is unchanged but the old "reads no tenant data" line is updated. **Flip in `src/Assignments/SchoolCollab.Assignments.Application/ModuleServices.cs`:** the `AssignmentQuestionGenerator` client registration changes `propagateTenant: false` → `propagateTenant: true` (the ar-2 documented flip note) — comment updated to cite WS-B2.
- **(f) Wizard — lock awareness + difficulty fields.** New `src/Assignments/SchoolCollab.Assignments.Core/Services/IAiPromptPolicyResolver.cs`: `public interface IAiPromptPolicyResolver { Task<bool> ResolveAiPromptLockedAsync(CancellationToken cancellationToken = default); }` (XML doc citing WS-B2 + the `ISignatureDefaultResolver` mirror note). New `src/Assignments/SchoolCollab.Assignments.Api/Services/AiPromptPolicyResolver.cs` mirroring `SignatureDefaultResolver`'s fail-open posture: named client `settings-api`, `GET /api/settings/assignment-ai-prompt` → 204 ⇒ false; 200 ⇒ `dto.IsLocked`; `HttpRequestException` ⇒ `LogWarning` + false. DI in `Assignments.Api/Program.cs` beside the signature resolver registration (named client exists — no new `AddHttpClient`). Route in `AssignmentRoutes.cs`: `group.MapGet("/ai-prompt-policy", ...)` returning **always 200** `{ aiPromptLocked = await resolver.ResolveAiPromptLockedAsync(ct) }` (fail-open false mirrors the resolver posture; literal segment wins over the guid template). `AssignmentsApiClient` gains `GetAiPromptPolicyAsync(CancellationToken ct = default) → Task<bool>` (+ private `sealed record AiPromptPolicyResponse(bool AiPromptLocked);`). `Create.razor`: `private bool _aiPromptLocked;` loaded at the end of `OnInitializedAsync` (after the signature pre-fill, separate try, **fail-open — warning log, NO error bar**, the `ResolveSignatureDefaultAsync` precedent) via a `LoadAiPromptPolicyAsync()` helper; passed into the section as a new parameter. `QuestionGenerationSection.razor`: `[Parameter] public bool PromptLocked { get; set; }` → the `AiPromptOverride` `FluentTextArea` gains `Disabled="@PromptLocked"` + a `Tooltip` "Your organization has locked the AI prompt." when locked; three new `FluentNumberField<int?>` "Easy / Medium / Hard questions" (`Min="0"`, `Max="30"`, W2 width-ladder class each, inline in the `question-generation-row` group — the flex-row-alignment skill's `align-items: flex-start` posture if the row wraps) bound to `_difficultyEasy/_difficultyMedium/_difficultyHard` (int? fields), threaded into `QuestionGenerationRequest` (`DifficultyEasyCount: _difficultyEasy`, …) and persisted onto `Model.DifficultyEasyCount` (Medium/Hard likewise) so `ToCreateRequest` carries them (the model properties from decision (c)).
- **(g) URL ingestion — extraction + wizard resource rows.** **CPM:** `Directory.Packages.props` gains `<PackageVersion Include="HtmlAgilityPack" Version="1.11.72" />` (latest stable; the worker may bump to the actual latest patch — CPM-clean either way) and `SchoolCollab.Assignments.Application.csproj` gains `<PackageReference Include="HtmlAgilityPack" />` (no Version — NU1008 otherwise). New `src/Assignments/SchoolCollab.Assignments.Application/Services/IUrlTextExtractor.cs`: `public interface IUrlTextExtractor { Task<UrlTextExtractionResult> ExtractAsync(string url, CancellationToken ct = default); }` with `sealed record UrlTextExtractionResult(bool Success, string? Text, string? Error)`. New `UrlTextExtractor.cs` implementing it: scheme guard (`http`/`https` only — anything else fails fast "Only http and https URLs are supported."); `IHttpClientFactory` named client `"url-fetcher"` (registered in `ModuleServices.cs`: `AddHttpClient("url-fetcher", client => client.Timeout = TimeSpan.FromSeconds(5))` — **NO tenant propagation handler**, external hosts; the client registration is the only place the timeout lives); response body read with a 512KB byte cap (read up to cap, stop); `HtmlAgilityPack.HtmlDocument` load → remove `script`/`style`/`noscript` nodes → `DocumentNode.InnerText` → collapse whitespace runs → truncate to **20,000 chars**; non-success status / timeout / `HttpRequestException` / `OperationCanceledException` when not caller-cancelled → `Success=false` + friendly `Error`. `AssignmentEditFormModel.cs`: new `public List<ResourceUrlRow> ResourceUrls { get; } = []` + `AddResourceUrl(string url)` (trims; rejects empty — `ArgumentException`; dedupes by exact trimmed string, returning false when a dup) + `RemoveResourceUrlAt(int index)`; `ToCreateRequest` maps them to `NewResourceDto` entries (`ResourceKindDto.Url`, `Url: row.Url`, `DisplayName: row.DisplayName`, `IncludedInGeneration: true`) appended into the request's `Resources` list. New `ResourceUrlRow` class (`string Url`, `string? DisplayName`) — add it beside the other row classes in `AssignmentQuestionEditorRows.cs`. `ResourcesSection.razor`: under the existing upload zone, a compact URL block — `FluentTextField` (placeholder "https://example.com/article", W4 ladder width) + an "Add link" `FluentButton` (the flex-row-input-alignment skill) + a list of URL rows (`@key="row"`, link icon `Icons.Regular.Size20.Link`, name = display name or URL, remove `FluentButton`, `aria-label` per the existing row list precedent). `QuestionGenerationSection.razor`: on `OnGenerateAsync`, BEFORE building the request — take `Model.ResourceUrls` up to **3** (beyond 3 ⇒ a warning message bar naming the skipped count, proceed with the first 3); for each, `UrlTextExtractor.ExtractAsync` (`@inject` the interface); per-URL failure ⇒ a warning `FluentMessageBar` naming the failed URL ("Couldn't read content from {url} — generating without it.") and proceed (fail-open); successes ⇒ their texts go into `request.ResourceTexts`. Cancellation stays caller-owned (the existing `_generateCts`). **SSRF note (recorded residual, binding v1):** scheme guard only — no private-range/IP-allowlist this round; recorded in residuals.
- **(h) Draft-regeneration UI — `QuestionsDraftSection.razor`.** New `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionsDraftSection.razor` (+ a minimal `.razor.css` ONLY if genuinely needed — prefer existing utility classes; the section-card skill governs the card layout). `[Parameter] public Guid AssignmentId { get; set; }`, `[Parameter] public bool GateEnabled { get; set; }` (the same FR-220/EC-3 gate computed in `Edit.razor` from the grading format + type), `[Parameter] public bool PromptLocked { get; set; }`, `[Parameter] public EventCallback OnConfirmed { get; set; }` (Edit re-loads the summary after confirm). Self-contained local state (NO form-model dependency — unlike the wizard's generation section): count `FluentNumberField` (1–30, default 5), the three type `FluentCheckbox`es, the three difficulty `FluentNumberField<int?>`s, optional `FluentTextArea` prompt guidance (Disabled when locked, 4000 cap). Buttons: **Generate draft** → `IAssignmentQuestionGenerator.GenerateAsync` (the same injected service the wizard uses) → map `GeneratedQuestionDto` list → `NewQuestionDto` list via the new `Helpers/GeneratedQuestionMapper.cs` (static `ToNewQuestionDto(GeneratedQuestionDto)` — reuses the enum map + option mapping already in `AssignmentQuestionEditorRows.cs`; `DisplayOrder` = index; TrueFalse options canonicalized exactly like `AppendGenerated` does — mirror its logic, do NOT invent new canonicalization) → `Api.StageQuestionsDraftAsync` → reload preview; **Confirm & replace questions** → `Api.ConfirmQuestionsDraftAsync` → `OnConfirmed`; **Discard draft** → `Api.DiscardQuestionsDraftAsync` → clear preview. On init: `Api.GetQuestionsDraftAsync` — an existing staged draft renders immediately (survives page reloads; that is the point of server-side staging). Preview: read-only `ul` list with `@key="row"` — question text, a type `FluentBadge`, option texts with the correct one marked (mirror `QuestionReviewList`'s rendering conventions, but a local flat list — no paging needed for ≤30). States: `_busy`, `_error` per-failure message bar, generate failure keeps prior draft intact (client-side staging means a failed AI call stages nothing). `Edit.razor`: renders the section inside a "AI question draft (regenerate)" `FluentCard`-style block, **only when `_item.Status == AssignmentStatusDto.Draft`**, `GateEnabled` computed from the selected grading format (the wizard's gate expression mirrored), `PromptLocked` loaded fail-open at init via `Api.GetAiPromptPolicyAsync()`. **Recorded intended behaviors (tester handover):** Confirm REPLACES all existing questions (that is the feature, not data loss — the button label says "Confirm & replace"); Generate-draft failure leaves the prior draft untouched; the section is Draft-only by design (Scheduled/Published assignments never render it).
- **(i) Tests** (MSTest/FluentAssertions/bUnit/MockHttp — exact names in the binding coverage list below; every new test file mirrors its named precedent's scaffolding; existing test cases are NEVER edited or deleted — additive `new[] { … }` / new `[TestMethod]`s only).
- **(k) Constraints (repo rules — binding):** CPM (no `Version` on any `PackageReference`; this round adds exactly ONE package — `HtmlAgilityPack` — via the CPM entry in decision (g), everything else zero); net10.0; **no MediatR** — all new handlers auto-register via the Scrutor scans (NO manual registrations); **typed exceptions only** — the new throws are the (a) 4000-cap `ArgumentException`, the (c) difficulty `< 0` `ArgumentException`s, and the (d) `InvalidQuestionsDraftException`; the domain state guards reuse the aggregate's existing guard style; **no new `InvalidOperationException` anywhere**; **migrations ARE required**: exactly TWO additive EF migrations (Settings + Assignments), each with Designer + snapshot edit, `MigrationGuardTests.NoUncommittedModelChanges` green everywhere WITH them; **`dotnet build SchoolCollab.sln` after every change**; primary constructors, XML `<summary>` docs citing WS-B2 / spec §3.4 on every new public type/member, structured logging with named placeholders; Blazor rules: FluentUI components for all controls, `@key` on every `@foreach`, **no `<style>` blocks or inline `style=`** in any touched component (existing utility classes only; `QuestionsDraftSection.razor.css` is the only permitted new stylesheet and only if needed), CSS isolation otherwise untouched; **the worker NEVER edits `documents/rounds/round-ar-10-ai-extensions.md`** — self-reports go to `documents/rounds/.ar-10-worker-report.md` (scratch, untracked); **30-minute run cap + cut-line protocol** (this round WILL need more than one worker run — budget 2–3); **repo-scoped searches only — NEVER `find /`** (a prior run hung 28 minutes on an unbounded search).

### Expected files

**No other file may change.** The list below is the reviewer's conformance baseline AND the tester-scope handover basis. Migration timestamps are worker-chosen (`<ts>`); each migration = the `.cs` + `.Designer.cs` (created) + the context `ModelSnapshot.cs` edit (modified).

**Settings context** (created 9, modified 3):
- `src/Settings/SchoolCollab.Settings.Core/Domain/TenantAssignmentAiPrompt.cs` (c)
- `src/Settings/SchoolCollab.Settings.Core/Data/Configurations/TenantAssignmentAiPromptConfiguration.cs` (c)
- `src/Settings/SchoolCollab.Settings.Core/DTOs/TenantAssignmentAiPromptDto.cs` (c)
- `src/Settings/SchoolCollab.Settings.Core/CQRS/AssignmentAiPrompts/Queries/GetTenantAssignmentAiPrompt/GetTenantAssignmentAiPrompt.cs` (c)
- `src/Settings/SchoolCollab.Settings.Core/CQRS/AssignmentAiPrompts/Queries/GetTenantAssignmentAiPrompt/GetTenantAssignmentAiPromptHandler.cs` (c)
- `src/Settings/SchoolCollab.Settings.Core/CQRS/AssignmentAiPrompts/Commands/UpsertTenantAssignmentAiPrompt/UpsertTenantAssignmentAiPrompt.cs` (c)
- `src/Settings/SchoolCollab.Settings.Core/CQRS/AssignmentAiPrompts/Commands/UpsertTenantAssignmentAiPrompt/UpsertTenantAssignmentAiPromptHandler.cs` (c)
- `src/Settings/SchoolCollab.Settings.Api/Endpoints/AssignmentAiPromptRoutes.cs` (c)
- `src/Settings/SchoolCollab.Settings.Api/AssignmentAiPromptEndpoints.cs` (c)
- `src/Settings/SchoolCollab.Settings.Core/Migrations/<ts>_AddTenantAssignmentAiPrompt.cs` (c)
- `src/Settings/SchoolCollab.Settings.Core/Migrations/<ts>_AddTenantAssignmentAiPrompt.Designer.cs` (c)
- `src/Settings/SchoolCollab.Settings.Core/Data/SettingsDbContext.cs` (m)
- `src/Settings/SchoolCollab.Settings.Core/Migrations/SettingsDbContextModelSnapshot.cs` (m)
- `src/Settings/SchoolCollab.Settings.Api/Program.cs` (m)

**Admin surface** (created 3, modified 2):
- `src/SchoolCollab.Admin.Shared/Services/AssignmentAiPromptApiClient.cs` (c)
- `src/Settings/SchoolCollab.Settings.Application/Components/Pages/AssignmentAiPrompt.razor` (c)
- `tests/SchoolCollab.Admin.Tests.Unit/AssignmentAiPromptPageTests.cs` (c)
- `src/Settings/SchoolCollab.Settings.Application/ModuleServices.cs` (m)
- `src/SchoolCollab.Admin/Components/Pages/Settings.razor` (m)

**AI** (created 2, modified 5):
- `src/AI/SchoolCollab.AI.Server/Services/TenantAssignmentAiPromptProvider.cs` (c)
- `tests/SchoolCollab.Settings.Tests.Unit/TenantAssignmentAiPromptProviderTests.cs` (c)
- `src/AI/SchoolCollab.AI.Abstractions/AssignmentQuestionGenerationTypes.cs` (m)
- `src/AI/SchoolCollab.AI.Server/Services/AssignmentQuestionGenerationService.cs` (m)
- `src/AI/SchoolCollab.AI.Server/Services/AssignmentQuestionGenerationSystemPromptProvider.cs` (m)
- `src/AI/SchoolCollab.AI.Server/Endpoints/AssignmentQuestionGenerationEndpoints.cs` (m — XML doc only)
- `src/AI/SchoolCollab.AI.Server/Program.cs` (m)
- `tests/SchoolCollab.Settings.Tests.Unit/AssignmentQuestionGenerationServiceTests.cs` (m)
- `tests/SchoolCollab.Settings.Tests.Unit/AssignmentQuestionGenerationSystemPromptProviderTests.cs` (m)

**Assignments domain + contracts + Api** (created 13, modified 12):
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/Exceptions/InvalidQuestionsDraftException.cs` (c)
- `src/Assignments/SchoolCollab.Assignments.Core/Services/IAiPromptPolicyResolver.cs` (c)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/QuestionsDraft/StageQuestionsDraftCommand.cs` (c)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/QuestionsDraft/StageQuestionsDraftCommandHandler.cs` (c)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/QuestionsDraft/ConfirmQuestionsDraftCommand.cs` (c)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/QuestionsDraft/ConfirmQuestionsDraftCommandHandler.cs` (c)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/QuestionsDraft/DiscardQuestionsDraftCommand.cs` (c)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/QuestionsDraft/DiscardQuestionsDraftCommandHandler.cs` (c)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Queries/GetQuestionsDraft/GetQuestionsDraftQuery.cs` (c)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Queries/GetQuestionsDraft/GetQuestionsDraftQueryHandler.cs` (c)
- `src/Assignments/SchoolCollab.Assignments.Api/Services/AiPromptPolicyResolver.cs` (c)
- `src/Assignments/SchoolCollab.Assignments.Core/Migrations/<ts>_AddAssignmentAiConfig.cs` (c)
- `src/Assignments/SchoolCollab.Assignments.Core/Migrations/<ts>_AddAssignmentAiConfig.Designer.cs` (c)
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/Assignment.cs` (m)
- `src/Assignments/SchoolCollab.Assignments.Contracts/ContractTypes.cs` (m)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/CreateAssignmentCommand/CreateAssignmentCommand.cs` (m)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/CreateAssignmentCommand/CreateAssignmentCommandHandler.cs` (m)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/UpdateAssignmentCommand/UpdateAssignmentCommand.cs` (m)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/UpdateAssignmentCommand/UpdateAssignmentCommandHandler.cs` (m)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/DuplicateAssignmentCommand/DuplicateAssignmentCommandHandler.cs` (m)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Queries/GetAssignmentById/GetAssignmentByIdQueryHandler.cs` (m — file name per actual path)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Queries/ListAssignments/ListAssignmentsQueryHandler.cs` (m — file name per actual path)
- `src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs` (m)
- `src/Assignments/SchoolCollab.Assignments.Api/Program.cs` (m)
- `src/Assignments/SchoolCollab.Assignments.Core/Migrations/AssignmentsDbContextModelSnapshot.cs` (m)

**Assignments Application** (created 6, modified 9):
- `src/Assignments/SchoolCollab.Assignments.Application/Services/IUrlTextExtractor.cs` (c)
- `src/Assignments/SchoolCollab.Assignments.Application/Services/UrlTextExtractor.cs` (c)
- `src/Assignments/SchoolCollab.Assignments.Application/Helpers/GeneratedQuestionMapper.cs` (c)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionsDraftSection.razor` (c)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionsDraftSection.razor.css` (c — only if genuinely needed; otherwise drop and use utilities)
- `tests/SchoolCollab.Assignments.Tests.Unit/UrlTextExtractorTests.cs` (c)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentEditFormModel.cs` (m)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentQuestionEditorRows.cs` (m — `ResourceUrlRow` class only)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionGenerationSection.razor` (m)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/ResourcesSection.razor` (m)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Create.razor` (m)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Edit.razor` (m)
- `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentsApiClient.cs` (m)
- `src/Assignments/SchoolCollab.Assignments.Application/ModuleServices.cs` (m)
- `src/Assignments/SchoolCollab.Assignments.Application/SchoolCollab.Assignments.Application.csproj` (m — HtmlAgilityPack ref only)

**Solution-wide** (modified 1):
- `Directory.Packages.props` (m — the one `HtmlAgilityPack` `PackageVersion` line)

**Test files** (created 8, modified 8):
- `tests/SchoolCollab.Settings.Tests.Unit/TenantAssignmentAiPromptTests.cs` (c — entity cap/trim + upsert/get handlers)
- `tests/SchoolCollab.Settings.Tests.Unit/TenantAssignmentAiPromptProviderTests.cs` (c — counted above under AI)
- `tests/SchoolCollab.Settings.Tests.Unit/AssignmentQuestionGenerationServiceTests.cs` (m — difficulty/resource validation + org-prompt layering + locked-ignore cases)
- `tests/SchoolCollab.Settings.Tests.Unit/AssignmentQuestionGenerationSystemPromptProviderTests.cs` (m — org-prompt replacement + difficulty line + resource block framing)
- `tests/SchoolCollab.Admin.Tests.Unit/AssignmentAiPromptPageTests.cs` (c — counted above)
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentDifficultyMixTests.cs` (c — domain guards + round-trip)
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentQuestionsDraftTests.cs` (c — domain stage/discard/confirm guards)
- `tests/SchoolCollab.Assignments.Tests.Unit/StageQuestionsDraftCommandHandlerTests.cs` (c — validate-then-stage, 404 guard, Draft-only)
- `tests/SchoolCollab.Assignments.Tests.Unit/ConfirmQuestionsDraftCommandHandlerTests.cs` (c — replace-all + clear blob + invalid/missing blob → typed exception)
- `tests/SchoolCollab.Assignments.Tests.Unit/DiscardQuestionsDraftCommandHandlerTests.cs` (c — idempotent discard on empty blob)
- `tests/SchoolCollab.Assignments.Tests.Unit/UrlTextExtractorTests.cs` (c — counted above)
- `tests/SchoolCollab.Assignments.Tests.Unit/GeneratedQuestionMapperTests.cs` (c — TrueFalse canonicalization + option mapping)
- `tests/SchoolCollab.Assignments.Tests.Unit/QuestionsDraftSectionBunitTests.cs` (c — generate→stage→confirm flow with mocked Api + generator; discard; lock disables guidance)
- `tests/SchoolCollab.Assignments.Tests.Unit/QuestionGenerationSectionBunitTests.cs` (m — difficulty fields thread into the request; lock disables the textarea; URL extraction success/failure fail-open)
- `tests/SchoolCollab.Assignments.Tests.Unit/ResourcesSectionBunitTests.cs` (c or m — whichever exists post-ar-4: URL add/remove/dedupe + ToCreateRequest mapping; if the file does not exist, create it)
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentCreateBunitTests.cs` (m — additive cases only)
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentFormModelMappingsTests.cs` (m — difficulty + resource-URL threading; additive cases only)
- `tests/SchoolCollab.Assignments.Api.Tests.Unit/AiPromptPolicyResolverTests.cs` (c — 204⇒false, 200⇒dto, failure⇒false)
- `tests/SchoolCollab.Assignments.Api.Tests.Unit/AssignmentDraftRoutesTests.cs` (c — the four routes' status/404/409 mapping)
- `tests/SchoolCollab.Assignments.Tests.Unit/CreateAssignmentCommandHandlerQuestionsTests.cs` (m — difficulty threading; additive cases only)
- `tests/SchoolCollab.Assignments.Tests.Unit/DuplicateAssignmentCommandHandlerTests.cs` (m — difficulty clone + blob NOT copied; additive cases only)

Approximate total: **68 files** (44 created, 24 modified; exact split may drift ±2 where a created file folds into an existing modified test file — deviations must be reported, not silent).

### Implementation steps (ordered — follow literally; `dotnet build SchoolCollab.sln` after every step; run the named tests where the step says so. Each step lands build-coherent so a mid-round clock-out never breaks the build)

1. **Settings backend (decision (a)).** Entity + config + DbContext + DTO + CQRS + routes + endpoints + Program map call + migration. Build; run `dotnet test tests/SchoolCollab.Settings.Tests.Unit` (existing cases green; new `TenantAssignmentAiPromptTests` land with this step or step 2's clock budget).
2. **Admin surface (decision (b)).** Admin.Shared client + ModuleServices registration + `/assignment-ai-prompt` page + Settings.razor dashboard item + `AssignmentAiPromptPageTests`. Build; run `dotnet test tests/SchoolCollab.Admin.Tests.Unit`.
3. **AI seam (decision (e)).** Request DTO fields + service validation + prompt provider (org + difficulty + resource framing) + `TenantAssignmentAiPromptProvider` + Program DI + endpoint XML doc + `ModuleServices` propagateTenant flip. Build; run `dotnet test tests/SchoolCollab.Settings.Tests.Unit` (generation service + provider + prompt-provider cases).
4. **Assignments domain + contracts (decisions (c) + (d)).** Difficulty columns + draft blob + domain methods + exception + DTO threading (requests, SummaryDto, commands, handlers, both query mappings, duplicate clone) + migration. Build; run `dotnet test tests/SchoolCollab.Assignments.Tests.Unit`.
5. **Draft CQRS + routes + resolver + Api client (decisions (d) + (f) backend half).** The four draft commands/handlers + query + `AssignmentRoutes` draft routes + `IAiPromptPolicyResolver` + `AiPromptPolicyResolver` + Program DI + `/ai-prompt-policy` route + `AssignmentsApiClient` methods. Build; run `dotnet test tests/SchoolCollab.Assignments.Tests.Unit` + `dotnet test tests/SchoolCollab.Assignments.Api.Tests.Unit`.
6. **Wizard surfaces (decisions (f) + (g)).** `AssignmentEditFormModel` fields (difficulty + `ResourceUrls`) + `ResourceUrlRow` + `ResourcesSection` URL block + `UrlTextExtractor` (+ CPM entry + csproj ref + url-fetcher registration) + `QuestionGenerationSection` (difficulty fields, lock, URL extraction) + `Create.razor` lock load. Build; run `dotnet test tests/SchoolCollab.Assignments.Tests.Unit`.
7. **Draft UI (decision (h)).** `GeneratedQuestionMapper` + `QuestionsDraftSection.razor` + `Edit.razor` wiring (Draft-only render, gate, lock, difficulty round-trip fields). Build; run `dotnet test tests/SchoolCollab.Assignments.Tests.Unit`.
8. **Full matrix + freeze.** `dotnet build SchoolCollab.sln` (0 errors) + all eight test projects (below) + `git add -N -- src tests` + the round patch freeze (`diffs-ar-10-ai-extensions.patch` is produced by the PARENT after acceptance — the worker just leaves a clean additive tree) + the worker self-report.

### Binding test coverage list (names binding, file paths exact — MSTest + FluentAssertions)

- `TenantAssignmentAiPromptTests` (Settings.Tests.Unit): `Create_FactoryAppliesTrimAndLock`, `SetPrompt_TrimsToNull`, `SetPrompt_RejectsOver4000Chars`, `UpsertHandler_InsertsThenUpdates`, `GetHandler_ReturnsNullWhenMissing`.
- `TenantAssignmentAiPromptProviderTests` (Settings.Tests.Unit, MockHttp): `FetchAsync_ReturnsNullOn204`, `FetchAsync_ReturnsNullOn404`, `FetchAsync_MapsDtoOn200`, `FetchAsync_FailsOpenOnNetworkError`.
- `AssignmentQuestionGenerationServiceTests` (additive cases): `GenerateAsync_RejectsNegativeDifficulty`, `GenerateAsync_RejectsMoreThan5ResourceTexts`, `GenerateAsync_RejectsOversizeResourceText`, `GenerateAsync_LocksPromptOverrideWhenTenantLocked`, `GenerateAsync_OrgPromptWinsOverEmbedded` (via the prompt-provider seam), `GenerateAsync_Locked_IgnoresPromptOverride`.
- `AssignmentQuestionGenerationSystemPromptProviderTests` (additive): `BuildMessages_UsesOrgPromptWhenProvided`, `BuildMessages_FallsBackToEmbeddedWithoutOrgPrompt`, `BuildMessages_FramesDifficultyCounts`, `BuildMessages_FramesResourceExcerpts`.
- `AssignmentAiPromptPageTests` (Admin.Tests.Unit, bUnit + MockHttp): `Renders_EmptyState_WhenNoRow`, `Save_UpsertsPromptAndLock`, `Save_SurfacesValidationError`, `LockSwitch_Binds`.
- `AssignmentDifficultyMixTests` (Assignments.Tests.Unit): `Create_AcceptsNullableCounts`, `Update_RejectsNegativeCounts`, `RoundTrips_ThroughSummaryDto` (via the handler mapping), `Duplicate_CopiesCounts`.
- `AssignmentQuestionsDraftTests`: `Stage_OnlyInDraft`, `Stage_RejectsEmptyJson`, `Discard_ClearsBlob`, `Confirm_OnlyInDraft`, `Confirm_ClearsBlob`.
- `StageQuestionsDraftCommandHandlerTests`: `Stages_ValidatedQuestions`, `Throws_WhenAssignmentMissing`, `Throws_WhenNotDraft`, `Rejects_InvalidQuestionPayload` (validation-before-stage).
- `ConfirmQuestionsDraftCommandHandlerTests`: `ReplacesAllExistingQuestions`, `ClearsBlobOnConfirm`, `Throws_OnMissingBlob`, `Throws_OnCorruptBlob` (defensive parse).
- `DiscardQuestionsDraftCommandHandlerTests`: `Discards_StagedBlob`, `Succeeds_WhenNoBlob` (idempotent).
- `UrlTextExtractorTests`: `Rejects_NonHttpScheme`, `Extracts_StripsScriptAndStyle`, `Truncates_AtCap`, `FailsOpen_OnTimeoutOrError`.
- `GeneratedQuestionMapperTests`: `Maps_MultipleChoiceWithOptions`, `Maps_TrueFalse_Canonicalized`, `Maps_ShortAnswerWithModelAnswer`.
- `QuestionsDraftSectionBunitTests`: `Generate_Then_Stages_Draft`, `Confirm_CallsClient_And_RaisesChanged`, `Discard_ClearsPreview`, `PromptLocked_DisablesGuidance`, `Renders_Preload_ExistingDraft`.
- `QuestionGenerationSectionBunitTests` (additive): `Difficulty_Fields_ThreadIntoRequest`, `PromptLocked_DisablesTextArea`, `ResourceUrls_PassExtractedTexts`, `ResourceUrl_Failure_FailsOpenWithWarning`, `MoreThanThreeUrls_WarnsAndCaps`.
- `ResourcesSectionBunitTests`: `AddUrl_AppendsRow`, `AddUrl_Dedupes`, `RemoveUrl_RemovesRow`.
- `AiPromptPolicyResolverTests` (Api.Tests.Unit): `ReturnsFalse_On204`, `ReturnsDto_On200`, `FailsOpen_OnNetworkError`.
- `AssignmentDraftRoutesTests` (Api.Tests.Unit): `GetDraft_204WhenNone`, `PutDraft_Stages`, `ConfirmDraft_200And409Paths`, `DeleteDraft_204`.
- `AssignmentFormModelMappingsTests` / `AssignmentCreateBunitTests` / `CreateAssignmentCommandHandlerQuestionsTests` / `DuplicateAssignmentCommandHandlerTests` (additive only): difficulty threading, resource-URL rows → `NewResourceDto`, duplicate copies difficulty but NOT the draft blob.

### Constraints (repo AGENTS.md + rules — restated for the worker)

CPM — no `Version` on any `PackageReference`; this round adds exactly **one** package (`HtmlAgilityPack`) via the (g) CPM entry. net10.0. **No MediatR** — CQRS via `ICommandHandler<T[,R]>` / `IQueryHandler<T,R>` + Scrutor scanning (no manual registrations). MSTest + FluentAssertions + bUnit + Moq + RichardSzalay.MockHttp. Run `dotnet build SchoolCollab.sln` after every change. **No git commits — working tree only.** Primary constructors for ctor injection; XML `<summary>` docs on every new public type/member; structured logging via `ILogger<T>` with named placeholders; typed exceptions only (the (a)/(c) `ArgumentException`s + (d) `InvalidQuestionsDraftException`); **no `InvalidOperationException` from any new code**. **Migrations required:** exactly two additive migrations (Settings + Assignments), each with Designer + snapshot; `MigrationGuardTests.NoUncommittedModelChanges` green everywhere WITH them (never touch an existing migration). Blazor rules (six `.razor` surfaces ship): FluentUI components for all controls; `@key` on every `@foreach`; **no `<style>` blocks, no inline `style=`** — existing utility classes only (`QuestionsDraftSection.razor.css` is the sole permitted new stylesheet, and only if needed); `EventCallback` for child→parent. **The worker NEVER edits `documents/rounds/round-ar-10-ai-extensions.md`** — self-reports go to `documents/rounds/.ar-10-worker-report.md` (scratch, untracked). **30-minute run cap + cut-line protocol:** if the clock runs short, finish the CURRENT step to a build-coherent state, update `documents/rounds/.ar-10-worker-report.md` with step-by-step progress (done / in-flight / remaining), and STOP — the parent resumes the bounded remainder. **Repo-scoped searches only — NEVER `find /`** (a prior run hung 28 minutes on an unbounded search).

### Worker task spec (commands + self-report)

- Build after every step; final: `dotnet build SchoolCollab.sln -c Debug` — 0 errors.
- Tests (all 8, 0 failures): `dotnet test tests/SchoolCollab.Settings.Tests.Unit`; `dotnet test tests/SchoolCollab.Settings.Api.Tests.Unit`; `dotnet test tests/SchoolCollab.Students.Tests.Unit`; `dotnet test tests/SchoolCollab.Students.Api.Tests.Unit`; `dotnet test tests/SchoolCollab.Admin.Tests.Unit`; `dotnet test tests/SchoolCollab.Assignments.Tests.Unit`; `dotnet test tests/SchoolCollab.Assignments.Api.Tests.Unit`; `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` (repo-wide scanner — always).
- Changed files = the expected-files list exactly (~68; migration `<ts>` pairs exact-path-agnostic; ±2 where a created test file folds into an existing modified one); report deviations from the plan one line each.
- Self-report (WORKER REPORT block from the role contract) to `documents/rounds/.ar-10-worker-report.md`; never the round doc.

### Acceptance criteria

Worker-facing:

- `dotnet build SchoolCollab.sln -c Debug`: 0 errors.
- `dotnet test` on the eight projects above: 0 failures (all `MigrationGuardTests` pass WITH the two new migrations).
- Changed files = the expected-files list one-for-one (~68); no unrelated deletions/reformatting; no new project/sln change; exactly ONE CPM entry; no existing migration touched.
- Steps 1–8 followed in order; any clock-out honored the cut-line protocol; deviations reported, not silent.

Reviewer-facing (static, diff-only):

- **Plan conformance against the expected-files list, one-for-one** — any file outside the list (or missing from it) is a P1.
- **Decisions (a)–(i) + (k) implemented as written:** (a) the mirrored entity/config/CQRS/route shapes + the 4000-cap throw → 400; (b) `TenantPropagationDelegatingHandler` on the `AssignmentAiPromptApiClient` registration (missing = P1) + no new css; (c) trailing-param threading at EVERY site — aggregate, both commands + handlers, all three DTO records, POST/PUT routes, BOTH query-handler mappings by named arg, duplicate clone, the model + Edit round-trip (missing = P1 — the silent-reset defect class); (d) validate-before-stage, replace-then-clear confirm, Draft-only guards, `InvalidQuestionsDraftException` → 409, blob NOT duplicated; (e) fail-open provider (204/404/network ⇒ null), org prompt REPLACES embedded, locked ⇒ override ignored, `propagateTenant: true` flip; (f) always-200 policy route + fail-open wizard load + disabled textarea when locked; (g) scheme guard + caps (3 URLs / 5 texts / 20k chars / 512KB / 5s) + fail-open per-URL warnings + dedupe; (h) Draft-only render + preload of an existing draft + confirm-replaces semantics labelled clearly.
- **Migration check:** exactly two additive migrations; `questions_draft_json` nullable text; the three difficulty columns nullable int.
- **Exception routing:** 400 (cap + negative difficulty + invalid draft payload), 404 (missing assignment), 409 (missing/corrupt blob on confirm); no `InvalidOperationException` anywhere new.
- **Best-practices check:** (1) no overwrites — purely additive on all modified files (existing test cases untouched); (2) repo skills honored — `dotnet-best-practices` (CPM, no MediatR, primary constructors, XML docs, structured logging, typed exceptions), `blazor-components` + `input-width-scale` + `flex-row-input-alignment` (the new field rows), `ef-migrations` (additive only, snapshot consistency); (3) readability — naming matches the `*AssignmentAiPrompt` / `QuestionsDraft` conventions, minimal focused diff, no dead code.

Orchestrator-facing (accept verdict):

- `dotnet build SchoolCollab.sln -c Debug`: **0 errors** (authoritative parent re-run).
- `dotnet test` on the eight projects: **0 failures** (authoritative parent re-run).
- Reviewer verdict PASS (or P2-only with all P2s triaged) + worker self-report reconciled against the tree (multi-run resumes stitched correctly).
- **P1 list empty → CLOSED**; otherwise the accept block names each P1 with its fix owner and the round stays open.
- Residual list refreshed (the pre-identified residuals below unless disproven).

### Residual risks / notes for acceptance

- **Round size vs the 30-minute worker cap:** ~68 files across three contexts + two migrations. Budget 2–3 worker runs; the steps are ordered so every stop is build-coherent.
- **ollama 429 risk:** the worker provider hit a weekly usage limit during ar-8. If the run dies on 429, surface immediately; do not retry-loop.
- **Service-to-service auth posture:** the settings-api routes `RequireAuthorization()` when OIDC is on; the AI server's org-prompt fetch (and the Assignments.Api resolvers) call settings-api without a bearer token. In dev (`DisableOIDCAuth`) this works; with OIDC on the fetch fails-open to the embedded prompt. Same residual class as ar-8's resolvers — resolved by the D-6 identity round.
- **Anonymous AI endpoint now reads tenant data:** the generation endpoint stays anonymous in dev and fails open (embedded prompt) when no tenant header arrives — an external anonymous caller never gets another tenant's org prompt (the header is required to resolve it). Recorded posture for the identity round.
- **SSRF:** the URL extractor guards scheme only (http/https); no private-range/IP-allowlist in v1. Recorded residual; the wizard is teacher-facing and the fetch caps at 512KB/5s, but an allowlist belongs in a later hardening pass.
- **Duplicate does NOT copy the draft blob** (decision (d)) — intended; the copy is a scalar clone and the blob is unconfirmed staged work.
- **Scheduled assignments cannot regenerate** — Draft-only by decision (d); `Update` allows Scheduled but the draft surface does not. Recorded, not a defect.
- **Difficulty sum ≠ QuestionCount is NOT validated** — soft by design (decision (a)); the prompt reconciles ("produce roughly the requested distribution").
- **Detail.razor does not surface the draft state** — the signature flag is now surfaced there (ar-9's Guardian Sign-off tab); the questions draft stays Edit-owned by design.
- **D-6 identity (`CreatedByTeacherId = Guid.Empty`) + ar-1 EF owned-children verification residual** — both carried, untouched by this round.

### UI-round note (tester handover — derived by the parent AFTER acceptance)

This is a UI round (Create.razor, Edit.razor, ResourcesSection.razor, QuestionGenerationSection.razor, the new QuestionsDraftSection.razor, the new Settings AssignmentAiPrompt page + the Admin Settings landing). Per the role contract, after the worker + parent verification and the accept verdict, the parent derives the tester-scope handover from the changed-file list (the changed files, the pages/cards that render them, the ApiClient methods they call, navigation entry points — one line of rationale per entry) and the minimax-m3 tester fires against exactly that list, no more. The handover is written into this doc's UI Tester section by the parent; the tester's scope is NOT this plan. Tester-relevant known behaviors to hand over as "intended, not bugs": Confirm & replace REPLACES all existing questions (labelled); generate-draft failure leaves the prior draft intact; the draft section is Draft-only; per-URL extraction failure warns but proceeds; >3 included URLs warns and caps at 3; the AI-prompt textarea disabled when org-locked; lock pre-load failure silently fail-opens (no error bar).

---

### Prepared worker dispatch (parent, 2026-09-14 — awaits #231 merge + branch cut)

Preconditions the parent completes before pass 1, in order (each gated on owner instruction where the merge policy requires):

1. ✅ **DONE 2026-09-14 — merge mechanism deviation (recorded):** `gh stack merge --yes --squash` is structurally unavailable for this single-layer stack (`.git/gh-stack` carries the PR #231 linkage but no GitHub-side stack id/number; a re-run of `gh stack submit --auto --open` pushed no-op and did not change it — single-layer stacks never get a GitHub stack registration). The merge ran as plain **`gh pr merge 231 --squash`** (owner-authorized) — merge SHA `d6d544ce`, PR #231 **MERGED**, retention held (`stack/9-ar-9-signoff` intact local `22d81132` + remote).
2. ✅ **DONE** — `main` synced to `d6d544ce` (local == origin == mergeSha); recorded as the round base.
3. ✅ **DONE** — `stack/10-ar-10-ai-extensions` cut from `main @ d6d544ce`; `gh stack init` adopted it ("main ← stack/10-ar-10-ai-extensions" — no zero-delta rejection).
4. ⏳ **PENDING** — model preflight: confirm the resolvable worker id — ladder ① `ollama/deepseek-v4-flash:cloud` → ② `ollama/deepseek-v4-flash:0731-cloud`.

**Pass-1 worker task (prepared; compose with `agent: "worker"` + the resolved model):**

> Implement round ar-10-ai-extensions **steps 1–2 only** (decision (a) Settings backend, decision (b) Admin surface). Read the plan at `documents/rounds/round-ar-10-ai-extensions.md` — it is the single source of truth (decisions (a)/(b), implementation steps 1–2, the "Settings context" + "Admin surface" expected-files blocks, the `TenantAssignmentAiPromptTests` + `AssignmentAiPromptPageTests` coverage entries). Build after every step (`dotnet build SchoolCollab.sln`); run `dotnet test tests/SchoolCollab.Settings.Tests.Unit` and `dotnet test tests/SchoolCollab.Admin.Tests.Unit` at step ends. NEVER edit the round doc; self-report to `documents/rounds/.ar-10-worker-report.md`. No git commits — working tree only. Repo-scoped searches only — NEVER `find /`. CPM: no `Version` on any `PackageReference`. No MediatR; typed exceptions only; no new `InvalidOperationException`. 30-minute cap: finish the current step to a build-coherent state, append progress to the worker report plus a `git status --short` snapshot to `documents/rounds/.ar-10-scope.txt` (the ar-1…ar-6 resume-state precedent), and STOP.
>
> Return ONLY:
> WORKER REPORT / Changed files: … / Build: … / Tests: … / Deviations from plan: …

Subsequent passes re-slice against the reported remainder (prepared budget: pass 2 = steps 3–4; pass 3 = steps 5–6; pass 4 = steps 7–8). The parent owns build authority, the patch freeze (`diffs-ar-10-ai-extensions.patch`), the static reviewer dispatch, the accept verdict, and the tester handover.

---

## Worker Report

Passes 1–8 **DONE**; full self-report at `documents/rounds/.ar-10-worker-report.md` (scratch, untracked). Summary: all 8 implementation steps landed build-coherent; Pass-8 freeze delivered `diffs-ar-10-ai-extensions.patch` (7,260 lines / 83 files) with `git add -N -- src tests` (intent-only, nothing committed). Worker-declared deviations: (1) `AssignmentSummary` + `AssignmentRepository` threaded for the difficulty round-trip; (2) no new typed exception beyond `InvalidQuestionsDraftException`; (3) `QuestionsDraftSection` takes TopicId/TopicName/GradeLevelId params; (4) `QuestionsDraftSection` UI bUnit not authored; (5) settings resolver 409 via `EnsureSuccessStatusCode`.

## Review

Static reviewer: `ollama/kimi-k2.7-code:cloud` (2026-09-15), read-only, never built.

```
REVIEW
Verdict: P1
P1: AssignmentSummary.cs / AssignmentRepository.cs — modified but not in expected-files list
P1: ResourcesSection.razor.css — modified but not in expected-files list
P1: ResourceUrlRow.cs — created instead of modifying AssignmentQuestionEditorRows.cs (listed, untouched)
P1: GeneratedQuestionMapperTests.cs — listed as created, missing from patch
P1: QuestionsDraftSectionBunitTests.cs — listed as created, missing from patch
P1: AssignmentDraftRoutesTests.cs — listed as created, missing from patch
P1: ResourcesSectionBunitTests.cs — listed as modified, untouched (no URL add/remove/dedupe tests)
P1: AssignmentFormModelMappingsTests.cs — listed as modified, untouched (no difficulty / resource-URL threading tests)
P1: QuestionGenerationSectionBunitTests.cs — harness updated, binding cases missing
P1: GetQuestionsDraftQueryHandlerTests.cs — created but not in expected-files list
P2: AssignmentAiPrompt.razor — plan specified Cancel-via-back link; page has Save only
P2: QuestionGenerationSection.razor — difficulty counts persisted to Model only inside OnGenerateAsync
P2: TenantAssignmentAiPromptProvider.cs — timeout not caught → does not fail open
P2: AiPromptPolicyResolver.cs — same timeout-fail-open gap
Best-practices: no overwrites (additive) / skills largely honored / readable
```

**Parent verification of each finding** (tree + patch cross-check, 2026-09-15):

| Reviewer finding | Verified state | Adjudication |
|---|---|---|
| `AssignmentSummary.cs` + `AssignmentRepository.cs` unlisted | Present in patch; plan decision (c) itself requires "any intermediate projection" threading | **Plan amendment A1** — in-scope; plan list omitted them |
| `ResourcesSection.razor.css` unlisted | Present in patch; file pre-existed (modified, not new); no inline `style=`/`<style>` | **Plan amendment A2** — accepted |
| `ResourceUrlRow.cs` created; `AssignmentQuestionEditorRows.cs` untouched | Confirmed — both files exist; the class was split into its own file instead of added to the listed file | **Plan amendment A3** — functionally equivalent, accepted; worker failed to declare it (noted) |
| `GeneratedQuestionMapperTests.cs` missing | **Confirmed absent** | **P1 → rework R1** |
| `QuestionsDraftSectionBunitTests.cs` missing | **Confirmed absent** | **P1 → rework R2** |
| `AssignmentDraftRoutesTests.cs` missing | **Confirmed absent** (`Assignments.Api.Tests.Unit` has no such file) | **P1 → rework R3** |
| `ResourcesSectionBunitTests.cs` untouched | Exists, **not in patch** | **P1 → rework R4** |
| `AssignmentFormModelMappingsTests.cs` untouched | Exists, **not in patch** | **P1 → rework R5** |
| `QuestionGenerationSectionBunitTests.cs` binding cases missing | In patch (harness only); all five binding-case names grep to **0 hits** | **P1 → rework R6** |
| `GetQuestionsDraftQueryHandlerTests.cs` extra | Confirmed extra, beneficial coverage for a planned query handler | **Downgraded** — accepted extra |
| `AssignmentAiPrompt.razor` no Cancel link | Confirmed | **P2 → folded into rework R9** (cheap) |
| Difficulty counts persisted only in `OnGenerateAsync` | **Confirmed** — `QuestionGenerationSection.razor:195–197` set `Model.Difficulty*` inside `OnGenerateAsync` only; Save-without-Generate silently drops them — the exact silent-reset class decision (c) forbids | **Escalated to P1 → rework R7** |
| Timeout fail-open gap (both files) | **Confirmed** — `TenantAssignmentAiPromptProvider.cs:42` catches `OperationCanceledException` only `when (ct.IsCancellationRequested)` (HttpClient timeout with an uncancelled token escapes); `AiPromptPolicyResolver.cs` catches `HttpRequestException` only | **Escalated to P1 → rework R8** (violates acceptance criteria (e)/(f) "network ⇒ fail open") |

## Acceptance

**Verdict: CLOSED** (parent/orchestrator verdict, 2026-09-15). Reviewer loop: 1 rework iteration (R1–R9), then static re-verification **PASS**.

Re-verification (reviewer, static, re-frozen patch 8,255 lines / 88 files, after the R1 rework):

```
REVIEW
Verdict: PASS
P1: No issues found.
P2: No issues found.
Best-practices: no overwrites / skills honored / readable
```

All nine rework items R1–R9 confirmed addressed on disk; plan amendments A1–A3 respected; no new P1 introduced. **P1 list empty → CLOSED.** Post-rework authoritative parent numbers: build 0 errors; 2,119 tests / 0 failures (Assignments 566/0, Assignments.Api 45/0, Architecture 20/0; others unchanged).

Authoritative parent re-run (only source of truth):

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.sln -c Debug` | **0 errors**, 6 warnings |
| Settings.Tests.Unit | 519 / 0 |
| Settings.Api.Tests.Unit | 1 / 0 |
| Students.Tests.Unit | 422 / 0 |
| Students.Api.Tests.Unit | 1 / 0 |
| Admin.Tests.Unit | 545 / 0 |
| Assignments.Tests.Unit | 545 / 0 |
| Assignments.Api.Tests.Unit | 41 / 0 |
| ArchitectureTests.Unit (incl. MigrationGuardTests) | 20 / 0 |
| **Total** | **2,094 / 0** |

Everything green — but green is not sufficient: the plan's **binding test-coverage list** is a hard acceptance criterion (`AGENTS.md` pre-flight: no PR for behavioural code without tests). Four plan-required test files/cases are absent and two plan-required behaviours are defective.

**P1 list (rework scope R1–R9):**

| # | Item | Fix owner |
|---|---|---|
| R1 | Add `tests/SchoolCollab.Assignments.Tests.Unit/GeneratedQuestionMapperTests.cs` (3 cases) | worker |
| R2 | Add `tests/SchoolCollab.Assignments.Tests.Unit/QuestionsDraftSectionBunitTests.cs` (5 cases) | worker |
| R3 | Add `tests/SchoolCollab.Assignments.Api.Tests.Unit/AssignmentDraftRoutesTests.cs` (4 route cases; reuse `SignOffRoutesTests` harness) | worker |
| R4 | Additive URL cases to `ResourcesSectionBunitTests.cs` (add/remove/dedupe + ToCreateRequest mapping) | worker |
| R5 | Additive difficulty + resource-URL threading cases to `AssignmentFormModelMappingsTests.cs` | worker |
| R6 | Add the 5 missing binding cases to `QuestionGenerationSectionBunitTests.cs` | worker |
| R7 | Fix `QuestionGenerationSection.razor` difficulty persistence (survives Save without Generate) | worker |
| R8 | Fix fail-open on timeout in `TenantAssignmentAiPromptProvider.cs` + `AiPromptPolicyResolver.cs` | worker |
| R9 | Add Cancel-via-back link to `AssignmentAiPrompt.razor` (P2, folded) | worker |

**Plan amendments (orchestrator, recorded):** A1 `AssignmentSummary.cs` + `AssignmentRepository.cs` in-scope (decision (c) intermediate projection); A2 `ResourcesSection.razor.css` modification accepted (existing file, no inline styles); A3 `ResourceUrlRow.cs` accepted as the created file in place of modifying `AssignmentQuestionEditorRows.cs`.

**Residual P2s (triaged, non-blocking):** none outstanding beyond R9. Carried residuals unchanged from "Residual risks / notes for acceptance" above (SSRF v1 scheme-only guard; anonymous endpoint posture; service-to-service auth; duplicate does not copy the draft blob; scheduled assignments cannot regenerate; difficulty-sum not validated).

**UI trigger:** FIRES — the changed-file list contains `.razor` + `.razor.css` surfaces and ApiClient changes.

## UI Tester (handover — derived by the parent after acceptance)

Scope is this list **verbatim**; the tester hunts exactly these surfaces and does not derive or expand scope.

Changed UI/client surfaces (one line of rationale each):
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionsDraftSection.razor` (+ `.razor.css`, new) — the Draft-only AI question-draft surface: generate → stage → read-only preview → confirm/discard.
- `.../Assignments/Edit.razor` — renders `QuestionsDraftSection` when status is Draft + gate enabled; passes TopicId/TopicName/GradeLevelId; reloads the summary on confirm.
- `.../Assignments/Create.razor` — wizard org-lock pre-load (`LoadAiPromptPolicyAsync`) threading lock + difficulty into the wizard.
- `.../Assignments/QuestionGenerationSection.razor` — three difficulty `FluentNumberField<int?>` (now bound to `Model`), org-lock disables the AI-prompt textarea, URL extraction on Generate.
- `.../Assignments/ResourcesSection.razor` (+ `.razor.css`, `ResourceUrlRow.cs`) — the URL rows block: add/remove, dedupe, >3-included cap warning.
- `.../Assignments/AssignmentEditFormModel.cs` — difficulty + `ResourceUrls` form fields feeding `ToCreateRequest`.
- `src/Settings/SchoolCollab.Settings.Application/Components/Pages/AssignmentAiPrompt.razor` (new) — org AI-prompt page: Save + Cancel→`/settings`.
- `src/SchoolCollab.Admin/Components/Pages/Settings.razor` — navigation item to the new page.

ApiClient methods these call:
- `AssignmentsApiClient`: `GetAiPromptPolicyAsync`, `GetQuestionsDraftAsync`, `StageQuestionsDraftAsync`, `ConfirmQuestionsDraftAsync`, `DiscardQuestionsDraftAsync`.
- `AssignmentAiPromptApiClient`: get + upsert.

Navigation entry points:
- Admin Settings landing → `/assignment-ai-prompt` (the new org AI-prompt page).
- Assignments Edit (a Draft assignment) → the Draft section.

Known-intended behaviors — hand these over as **intended, not bugs**:
- Confirm & replace REPLACES all existing questions (labelled as such).
- Generate-draft failure leaves the prior draft intact.
- The draft section renders only for Draft status.
- Per-URL extraction failure warns but proceeds (fail-open).
- More than 3 included URLs warns and caps at 3.
- The AI-prompt textarea is disabled when the org prompt is locked.
- Lock pre-load failure silently fail-opens (no error bar).

Hunt user-facing defects: swallowed errors, perpetual spinners, missing error surfaces, invisible validation, wrong bindings, DTO/property mismatches surfacing as silent no-ops, accessibility regressions, broken refresh/navigation. Not a second reviewer — no plan conformance. Anything outside this list is an out-of-round observation for the parent, never rework.

### UI TEST result

Tester: `ollama/minimax-m3:cloud` (2026-09-15), adversarial, scope = the handover above.

```
UI TEST
Verdict: P2-only
P1: none.
P2:
- QuestionsDraftSection.razor:33-36,133-135 — difficulty fields bind to local _difficultyEasy/Medium/Hard, never propagated to the assignment; teacher sets a mix, generates, confirms, saves — persisted mix unchanged. No tooltip clarifying "affects this generation only".
- QuestionsDraftSection.razor:47-51 — disabled Generate button carries no tooltip (wizard uses Tooltip=DisabledTooltip for the same gate).
- QuestionsDraftSection.razor:47-78 — no Cancel button while _busy (wizard surfaces one during _generating); teacher stuck on a slow AI call.
- ResourcesSection.razor:50 — "0 link(s)" wording when the URL list is empty.
Out-of-round observations: Edit.razor cannot display/edit the persisted AiPromptOverride because AssignmentSummaryDto does not expose it; the new QuestionsDraftSection prompt textarea consequently cannot be pre-filled from a persisted value.
```

**Parent triage (non-blocking, recorded as residuals):**

| P2 | Triage |
|---|---|
| Draft-section difficulty is generation-only, unlabelled | **Intended behaviour, UX gap** — difficulty in the draft section is a regeneration parameter (decision (h)); the wizard owns the persisted assignment mix (decision (c)+(f)). Residual: add a "affects this generation only" tooltip in a polish pass. |
| Disabled Generate button has no tooltip | **Residual (consistency)** — wizard precedent `QuestionGenerationGate.DisabledTooltip`. Cheap polish. |
| No Cancel while `_busy` | **Residual (UX)** — wizard precedent; cheap polish. |
| "0 link(s)" wording | **Residual (cosmetic)**. |
| Out-of-round: `AssignmentSummaryDto` omits `AiPromptOverride` → draft prompt textarea cannot pre-fill | **Out of round scope** — carried to the D-6 identity/assignment-override follow-up; not a round ar-10 defect (the textarea is author-optional for regeneration). |

No tester rework required (P2-only). Round **CLOSED** with the four P2 residuals above recorded for an optional polish pass.

## Execution provenance (actual — recorded by the parent)

The header records an owner **SOLO** decision (2026-09-14): the parent self-applies all four role contracts and writes `.ar-10-worker-report.md`; no subagent worker is dispatched. Actual execution at the verification phases differs and is recorded here for traceability:

| Phase | Executed by | Model |
|---|---|---|
| Worker passes 1–8 (implementation + freeze) | parent, SOLO self-applied | session model (`ollama-cloud/deepseek-v4.1-flash`) |
| Worker pass R1 (rework items R1–R9) | dispatched subagent `worker` | `ollama/deepseek-v4-flash:0731-cloud` |
| Static reviewer — initial + re-verification | dispatched subagent `reviewer` ×2 | `ollama/kimi-k2.7-code:cloud` |
| UI tester | dispatched subagent ×1 | `ollama/minimax-m3:cloud` |
| Authoritative build + tests (both passes) | parent | pi session |

The reviewer and UI-tester contracts were executed as **independent agents** rather than parent self-applied — strictly stronger than the SOLO plan; the SOLO mode is superseded at the verification phases (owner authorized the reviewer dispatch on 2026-09-15).

**Durable outcome to fold into `documents/specs/`:** WS-B2 rows (difficulty distribution, org `TenantAssignmentAiPrompt`, versioned question-draft regeneration, URL ingestion) are implemented; carry the four P2 residuals + the `AssignmentSummaryDto`/`AiPromptOverride` out-of-round observation into the D-6 identity follow-up.
