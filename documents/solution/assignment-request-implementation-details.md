# Assignment Request (AR) — Implementation Details Exploration

> **Companion to:** `documents/solution/assignment-request-go-forward-breakdown.md`
> (situation analysis + workstreams WS-A…WS-G + Phases 0–5). This doc is the
> file-level implementation exploration that feeds the orchestrator's round
> plans for the **full four-agent rounds** (Tier 3, `orchestrator-worker-reviewer`
> skill). Execution mode chosen by the user: **full four-agent; not yet executed.**
>
> Everything here was verified against the tree on 2026-09-03 (paths, DI
> registrations, table names, test conventions). Round plans should cite this
> doc instead of re-exploring.
>
> **Decisions:** all Phase-0 decisions (spec §7 Q1–Q6, D-1…D-6) were resolved
> 2026-09-03 — see `assignment-request-go-forward-breakdown.md` §2. Affected
> design spots are updated in-place below.

---

## 1. Confirmed architecture inventory (verified file-level)

### 1.1 Assignments domain — aggregation shape

`Assignment` (`src/Assignments/.../Domain/Assignment.cs`) is the aggregate root.
Children are **EF owned types** (see `Data/Configurations/AssignmentConfiguration.cs`):

| Child | Table | Owned via | Notes |
|---|---|---|---|
| `AssignmentQuestion` | `assignment_questions` | `OwnsMany` → own `OwnsMany` `QuestionOption` → `question_options` | `CorrectOptionId` nullable pointer; `DisplayOrder`; AutoInclude + field access mode |
| `AssignmentAttachment` | `assignment_attachments` | `OwnsMany` | `(fileName, contentType, fileSize, storagePath)` — private ctor only, **no aggregate `AddAttachment` method, no upload backing anywhere** |
| `AssignmentReview` | `assignment_reviews` | `OwnsMany` | teacher score/comments pre-submission |

Standalone tenant entities (own tenant columns, `TenantEntityTypeConfigurationBase`
configs, `xmin` row versions where noted): `AssignmentRecipient`, `GuardianSubmissionGate`,
`AssignmentSubmission`, `AssignmentSubmissionVersion`, `SubmissionReview`,
`AssignmentActivityGroup`. `AssignmentsDbContext` explicitly instantiates each
configuration (no assembly scanning) and runs `ValidateTenantFilters` — only
`OutboxMessage` is in `GlobalEntityAllowList`.

Lifecycle today: `Draft=0 → Published=1 → Closed=2` (+ `Unpublish` back to Draft,
`Close` from Published). `Update()` throws unless Draft. `Publish()` stamps
`PublishedAt`. `AssignmentNumber` via `IEntityCodeGenerator.GenerateAsync("ASSIGNMENT_CODE")`
(seed rule: stamp `ASG` + `A01..A99` — `MigrationService/Seeding/EntityCodeRuleSeeder.cs`).

**Identity gap (P0 discovery):** `CreateAssignmentCommandHandler` passes
`createdByTeacherId: Guid.Empty // TODO: wire up authenticated teacher ID`. No
staff identity wiring exists. Every AR persona (Author/Approver/Ward/Guardian
role-scoped access, spec §6) depends on resolving this — it must be a round-0
decision whether deep-link tokens (no login) carry the ward/guardian flows
initially and identity wiring is deferred, or Keycloak users are provisioned.

### 1.2 Command flow — publish (the pattern AR extends)

`PublishAssignmentCommandHandler` (verified) does, in order:
1. `repository.GetAsync` → `assignment.Publish()`
2. `ResolveRecipientsAndGatesAsync`: FR-58 topic-assignment validation via
   `ITopicAssignmentLookup` (fail-closed for grade/group mismatches),
   `SelectedGroups` needs ≥1 `AssignmentActivityGroup` link (FR-23) + active
   members via `IActivityGroupLookup.GetActiveMemberIdsAsync`
3. `IContactResolver.ResolveSubscribersAsync` (HTTP → students-api:
   `/students/by-grade/{id}`, `/students/{id}/guardians`, `/contacts/subscribed`,
   `/grade-levels/{id}/teachers`) → upsert `AssignmentRecipient` per contact
   (dedup by contact, `MarkSubscribed` on republish)
4. `GuardianSubmissionGate.Create` for every student with a Primary-guardian
   recipient when `MandatoryReview`
5. `INotificationPolicyResolver.ResolveEffectiveAsync` (HTTP settings-api +
   students-api) → `NotificationRecipientFilter.Apply` (blocked/preferred/cap)
6. `IAssignmentNotificationBroadcaster.BroadcastPublishedAsync` — **enqueue
   BEFORE save** (buffering outbox commits atomically with the mutation);
   v1 emits one `AssignmentPublishedIntegrationEvent`
7. `UpdateAsync` + `SaveChangesAsync` + cache tag removal + `ClearDomainEvents`

New AR publish-time behaviors (deep links, per-recipient NotificationLog rows,
sign-off state init, module-gate init) plug into steps 5–6 behind the same seams.

### 1.3 Submission flow

`CreateStudentSubmissionCommandHandler`: gate check (`MandatoryReview` +
`SubmissionEnabledForStudent`) → upsert `AssignmentSubmission` → new immutable
`AssignmentSubmissionVersion` (`VersionNumber = Current+1`, `Content` string) →
`RecordSubmission` (reopens `ReviewState.Graded → Pending` on resubmit).
`SubmitAssignmentOnBehalf` mirrors it with `SubmissionSource.GuardianOnBehalf`.
**`Content` is an opaque string — structured answers, scoring, pass/fail do not
exist.** `SubmissionReview` (teacher post-submission grade) flips
`ReviewState` via `ApplyReview`.

### 1.4 API + contracts

`AssignmentEndpoints.MapAssignmentEndpoints(this WebApplication app,
IFeatureFlagService featureFlags)` — group `/assignments`, `RequireAuthorization()`
unless `FeatureFlagKeys.DisableOIDCAuth`; `MapActivityGroupLinkRoutes` additionally
gated by `EnableActivityGroups` (dark-launch precedent to copy for new flags).
Routes (all verified in `Endpoints/AssignmentRoutes.cs`): CRUD + publish/unpublish/
close/review + `/{id}/recipients` + `/{id}/submissions` + review-queue +
`/students/{sid}/submission` (GET/POST) + `/guardian-review` +
`/enable-submission` + `/submit-on-behalf` + `/submission/review`.
Contract records in `Contracts/ContractTypes.cs` (DTO enums mirror Core enums by
int value; JSON string-enum converters registered both in Api `Program.cs`
`ConfigureHttpJsonOptions` and in `AssignmentsApiClient`).

### 1.5 AI host (the B1 seam)

- Aspire resource is **`settings-ai`** (`SchoolCollab.AI.Server`). Program.cs
  registers keyed `IChatClient` ("ollama" via OpenAI-compat client, "openrouter"),
  `ChatClientFactory`, `AddCodedValuesAiTools(...)` → registers
  `CodedValuesToolProvider` (singleton `IToolProvider`) **and
  `CodedValuesSystemPromptProvider` (singleton `ISystemPromptProvider`)**, plus
  singleton `AIChatEngine` (constructor injects the single registered provider set).
- **DI conflict to design around:** a second `ISystemPromptProvider` registration
  would collide with the CodedValues singleton. The AI spec (decision 3) resolves
  this: the new endpoint `POST /api/ai/assignments/questions` constructs its own
  engine — `AIChatEngine`'s ctor is public, so the endpoint can build
  `new AIChatEngine([], assignmentPromptProvider, chatClientFactory, config, logger)`
  (no tools in v1) or a keyed registration. Round plan must pick one; spec-preferred
  is per-endpoint construction.
- **Streaming vs JSON:** `/api/ai/chat` is SSE. The questions endpoint returns a
  single validated JSON document (spec §4.3) — so it should call the chat client
  directly (collect full text, parse, schema-validate) rather than reuse the
  SSE engine loop. `FormatProviderError` mapping (401/403/429/5xx) is in
  `AIChatEngine` — extract/reuse for the new endpoint.
- Prompt loading pattern (verified in `CodedValuesSystemPromptProvider`):
  embedded resource `Prompts/<name>.md` (+ `.original.md` fallback) +
  Development file override with mtime caching. New
  `Prompts/assignment-question-system-prompt.md` follows exactly this.
- Provider/model resolution: `ChatModelResolver.Resolve` + config keys
  `codedvalue-ai-provider`, `Ollama:DefaultModel`, `OpenRouter:DefaultModel`
  (AppHost Parameters fan these to `settings-ai`).

### 1.6 Cross-context + messaging conventions

- HTTP lookups: interface in consumer `Core/Services`, implementation in
  consumer `Api/Services` using `IHttpClientFactory.CreateClient("students-api"|"settings-api")`
  (Aspire service discovery). Tenant flows via `TenantForwardingDelegatingHandler`.
- Integration events: contract records in `Contracts/Events/`, published through
  `IIntegrationEventPublisher` (buffering outbox → `outbox_messages` table →
  `OutboxDispatcher` hosted service → RabbitMQ exchange per context:
  `assignments`, `students`, `settings`). Consumers register via
  `AddRabbitMqSubscriber` (worker precedent: `students-worker/Program.cs` also
  shows the appsettings.json re-anchoring pitfall + `AddHostedService` sweeps
  like `ActivityGroupRolloverService` — the model for the future
  `Assignments.Worker` reminder/archive sweep).
- **There is no assignments worker yet.** Reminders (E3), auto-archive (A2),
  scheduled-publish (A2) need one; `students-worker` is the template project.

### 1.7 Auth + tenancy

`AddAuthAndTenancy` (`SchoolCollab.Core/Auth/AuthTenancyExtensions.cs`): cookie +
OIDC (Keycloak) with `TenantClaimsTransformation` bridging `tenant_id` claim →
`ITenantProvider`; `TestAuthExtensions.TestAuthScheme` replaces it under
`FEATURE:DisableOIDCAuth`; `DevTenantSwitcher` + `TenantPropagationDelegatingHandler`
for dev/test. **No token-based public auth exists** — deep links (E1) are net-new:
recommend `IDataProtection`-signed tokens (purpose-scoped, TTL from
`LinkValidityDays`), no new auth scheme.

### 1.8 UI inventory

- Host: `SchoolCollab.Admin` (Interactive Server only) consumes RCLs:
  `AddSettingsModule` + `AddAssignmentsModule` + `AddStudentsModule`
  (`ModuleServices.cs` per module wires typed API clients via
  `AddCrossModuleHttpClient<T>("https+http://assignments-api", propagateTenant: true)`).
- `Create.razor` (755 lines): `FluentWizard` 3 steps — Type&Format card selectors
  (type × grading × audience), Details (group `FluentMultiSelect`-style selection
  `_selectedGroupIds`, grade + subject dropdowns with cascading loads
  `OnGradeLevelChangedAsync`/`LoadGroupSubjectsAsync`), Review; footer nav;
  `SubmitAsync` posts `CreateAssignmentRequest` then `LinkAssignmentGroupsAsync`.
- `Detail.razor` (482): status header, publish dialog launch, recipients grid,
  submissions grid → submission detail (versions + teacher review form),
  assignment review form. `Index.razor` (354): Admin.Shared `LandingPage` +
  `EntityGrid` + `RowActionsMenu` with status filter and publish/unpublish/close/delete.
- `PublishDialog.razor` + `PublishDialogTypes.cs` (`PublishFormModel`, contact
  checkboxes, `PublishResult`).
- Admin.Shared reusable set (verified): `LandingPage`, `EntityGrid`,
  `RowActionsMenu`, `DialogShellFooter`, `ConfirmDialog`, `SideDrawer`/`DialogDrawer`,
  `GateBase`, `Chip`, `DropdownForEnum`, `CodedValueDropdown`, `ContactsEditor`,
  `FormRow`, `FieldDisplay`.
- **No file-upload surface exists anywhere** (`IFormFile`/`IBrowserFile`: zero
  hits repo-wide) — A1 needs the first upload path (Fluent `InputFile` client-side
  + multipart API endpoint + `IFileStore` abstraction).

### 1.9 Feature flags

`IFeatureFlagService` (`SchoolCollab.Core/Features/FeatureFlagService.cs`):
config-based (`FeatureFlags:KEY` env) default; DB-backed tenant-override impl in
Settings (ConfigFlags admin page; `PilotActivityGroupFlagOverrideSeeder` in
MigrationService is the dark-launch seeding precedent). `FeatureFlagKeys` holds
canonical `FEATURE:...` strings. New AR flags (approval workflow, sign-off,
AI-review gate) = new constants + endpoint/UI gates + optional seeded tenant
override + `documents/configuration.md` §2 mapping.

### 1.10 Tests & enforcement

- MSTest (`[TestClass]/[TestMethod]`) + FluentAssertions; bUnit for components
  (`AssignmentIndexBunitTests`, `AssignmentFormModelMappingsTests`);
  `MigrationGuardTests` (NoUncommittedModelChanges), `AssignmentOwnedTypeTenancyTests`,
  `SubmissionEngineTests`, `NotificationRecipientFilterTests` (pure-logic style to
  copy for scoring/gating engines), fake-repository handler tests
  (`CreateAssignmentCommandHandlerEntityCodeTests`, `FakeNotificationPolicyResolver`).
- `tests/SchoolCollab.ArchitectureTests.Unit` enforces the
  `dotnet-best-practices.md` "Never" list repo-wide — **always in the
  authoritative test set** per the skill.
- Test projects per context: `SchoolCollab.Assignments.Tests.Unit` (Core) and
  `...Assignments.Api.Tests.Unit` (currently only `SmokeTests.cs`).

---

## 2. Workstream implementation details

### WS-A1 — Resources + content modules

**Design decision to fix in the plan:** two distinct concepts per AR spec §4 —
`Resource` (AI-generation inputs: url|file|video) and `ContentModule`
(student-facing delivery: video|guide with completion threshold). Keep them separate:

- `AssignmentAttachment` (owned) stays the file material store; add aggregate
  methods `AddAttachment`/`RemoveAttachment` per AI spec §3.1 (owned-type child).
- **`ContentModule`: standalone tenant entity** (not owned) — it needs to be
  referenced by per-ward progress rows (D1) and re-ordered independently.
  Table `assignment_content_modules`: `Id, TenantId, AssignmentId, ModuleType
  (enum Video|Guide), Title?, Url, StoragePath?, DisplayOrder,
  MinCompletionThresholdPercent (int, default 100), IsRequired (bool), RowVersion,
  audit`. Aggregate gains `AddModule/RemoveModule/ReorderModules`; create/update
  commands accept module list (draft-only, mirrors questions).
- **`AssignmentResource`: standalone tenant entity** for AI inputs —
  `ResourceKind (enum Url|File|Video)`, `Url`, `StoragePath?`, `DisplayName`,
  `IncludedInGeneration (bool)`. Table `assignment_resources`.
- Upload path (first in repo): `IFileStore` abstraction in Assignments.Core
  (`StoreAsync(stream, fileName) → storagePath; OpenReadAsync(storagePath)`);
  dev implementation = local folder under AppContext.BaseDirectory configured via
  AppHost parameter (D-1 decision); production impl later. API endpoint
  `POST /assignments/{id}/attachments` (multipart, allowlist + size caps from
  config params) staged at submit per AI spec EC-4 (preferred: stage-at-submit
  so removed files never upload — but note the wizard's submit is a single create
  call; the upload must therefore precede the create call for a *new* assignment:
  either a staging area endpoint keyed by client-generated assignment id, or
  multipart create. Plan must resolve: recommend `POST /assignments/attachments/stage`
  returning `{attachmentId, storagePath}` + cleanup sweep for orphaned staging).
- Files: `Domain/ContentModule.cs`, `Domain/AssignmentResource.cs`,
  `Domain/ModuleType.cs`, `Domain/ResourceKind.cs`, aggregate methods in
  `Assignment.cs`, `Data/Configurations/ContentModuleConfiguration.cs`,
  `.../AssignmentResourceConfiguration.cs`, DbContext DbSets, migration
  `<ts>_AddContentModulesAndResources`, commands/DTOs/routes/client methods.

### WS-A2 — Lifecycle extensions

- `AssignmentStatus` gains `Scheduled = 3`, `Archived = 4` (enum ints are
  persisted; DTO mirror + `EnumHelper` descriptions + Index status filter list
  must be extended; no DB migration for enum ints themselves).
- `AvailableFromUtc DateTimeOffset?` + `ArchiveGraceDays` — column migration.
  `Schedule()`/`PublishNow()`/`Archive()` domain methods; `Update()` allowed in
  Scheduled (structural edits still freeze at Published). Auto-publish: hosted
  sweep service (assignments-api `AddHostedService<T>` is acceptable pre-worker;
  worker preferred — see E3).
- Approval: `ApprovalStatus (Pending|Approved|Rejected)` + `ApprovedBy`,
  `ApprovedAt` columns; `SubmitForApproval`/`Approve`/`Reject` commands; publish
  guard `if (_approvalRequired && ApprovalStatus != Approved) throw` where
  `_approvalRequired` = new flag `FEATURE:RequireAssignmentApproval` (default off;
  tenant-override seedable). Approver authorization: role claim check — depends
  on the identity decision (§1.1); with identity unwired, v1 gates the endpoint
  to authenticated users only and records `ApprovedBy` from a request field
  (consistent with existing `ReviewAssignmentRequest.TeacherId`).
- Archive: after `DueDate + grace` → `Archived`; read-only guard in commands;
  export deferred to Phase 5.

### WS-A3 — Scoring & attempts

- Structured answers: new owned or standalone `SubmissionAnswer` — recommend
  **standalone tenant entity** (`assignment_submission_answers`:
  `SubmissionVersionId, QuestionId, SelectedOptionId?, TextAnswer?`) so scoring
  and per-question analytics are queryable. Extend `AssignmentSubmissionVersion`
  with `Score (decimal?), Passed (bool?)` columns (attempt = version).
- `Assignment` gains `PassScore (decimal?)`, `MaxAttempts (int?)` columns +
  factory/Update params (draft-only).
- `IScoringEngine` (pure, `Assignments.Core/Services/ScoringEngine.cs` +
  interface): input = assignment (questions+correct pointers, PassScore,
  GradingFormat) + submitted answers → output = per-question correctness +
  total score + passed. Rules: MC/TF via `CorrectOptionId`; ShortAnswer =
  normalized exact match to model answer only when a model-answer exists
  (persist model answer: `AssignmentQuestion.ModelAnswer` column — AI spec §5
  mentions modelAnswer kept for teacher reference; needed here); `TeacherGraded`
  assignments skip auto-score.
- Submission commands extended: `CreateStudentSubmissionCommand` accepts
  answers; handler validates module gating (D1) → score → attempt cap check
  (`MaxAttempts` exceeded → 409-style error) → persist; `InstantGraded` returns
  per-question feedback immediately (response body), `AutoGraded` holds it until
  teacher review or submission lock.
- Tests copy `NotificationRecipientFilterTests` pure-logic style + handler tests
  with fake repos.

### WS-A4 — Duplicate-as-template

`DuplicateAssignmentCommand(Guid sourceId)`: load source (with children — they
AutoInclude), `Assignment.Create` clone (new Id, new `ASSIGNMENT_CODE` via
`IEntityCodeGenerator`, status Draft, no recipients/gates/submissions/reviews —
those live outside the aggregate anyway), copy questions+options (re-init
`CorrectOptionId`), modules, resources, prompt override, pass-score fields.
Route `POST /assignments/{id}/duplicate`; client method; Index row action +
Detail button. Pure handler test with fake repo.

### WS-A5 — Ward-facing queries

`ListAssignmentsForWard(Guid studentId)` (published-or-scheduled-available,
with per-ward status projection: recipient DeliveredAt/OpenedAt + submission +
gate + signature state), `GetWardAssignmentDetail` (modules + progress +
questions without correct answers + attempts remaining), submit command reuses
existing endpoints. These live in Assignments Core/API with normal OIDC auth —
the *portal* surface decision (D-4) only affects hosting.

### WS-B1 — Execute `assignment-creation-with-ai.md` verbatim

Its own §10 phase list is the round plan skeleton (domain → contracts →
command/handler → migration → AI endpoint → seam → UI → tests). Implementation
notes from this exploration that the worker needs:
- **Endpoint construction** (§1.5): per-endpoint `AIChatEngine`/direct
  `IChatClient` use, JSON collection (not SSE), `FormatProviderError`-style
  mapping, request/response per AI spec §4.3; prompt file
  `src/AI/SchoolCollab.AI.Tools.CodedValues/Prompts/…` pattern → new file
  (suggest `SchoolCollab.AI.Server/Prompts/assignment-question-system-prompt.md`
  embedded + `.original.md` fallback).
- `IAssignmentQuestionGenerator` lives in Assignments.Application (or Core with
  HTTP impl in Application/Api) → named client `https+http://settings-ai` wired
  in `AddAssignmentsModule` (Admin AppHost ref to settings-ai already exists).
- Form model per AI spec §3.5; wizard changes confined to Step 2 (Resources +
  AI generation section, gated `FR-220`: Digital/SemiManual AND
  AutoGraded/InstantGraded) + Review paging (`FluentPaginator`, page 5).
- Cancellation via `CancellationTokenSource` (FR-224); malformed-JSON → typed
  `QuestionGenerationFailed` (FR-222/EC-1).

### WS-B2 — AR extensions on top of B1

- `PromptConfig` on assignment: `QuestionCount` + type mix already in B1 request
  shape; add `DifficultyMix` (e.g. Easy/Medium/Hard counts or ratio string) —
  persist on `Assignment` (columns) and thread into the generation request +
  system prompt framing.
- Org-level system prompt: Settings context entity `TenantAssignmentAiPrompt`
  (mirror `TenantNotificationPolicy`: one row per tenant, nullable fields =
  inherit; CQRS + `/api/settings/assignment-ai-prompt` route + Admin UI tab on
  the Settings page). Generation endpoint reads it via settings-api client and
  layers `AiPromptOverride` as user framing (matches AI spec decision 8).
- Versioned regeneration: server-side staging — `QuestionGenerationDraft`
  rows (or a `DraftSetId` on staged questions) keyed by assignment+session,
  `ConfirmQuestions` command materializes into the aggregate; regenerate
  creates a new draft set without touching confirmed ones. Keep v1 simple:
  draft set = single JSON blob column on assignment (`QuestionsDraftJson`)
  mutated by generate/confirm; only confirmed questions enter the owned
  collections. Plan must pick blob-vs-rows (recommend blob column for v1).
- URL ingestion: `ResourceKind.Url` fetch + text extraction (HtmlAgilityPack or
  plain-text strip — new CPM package decision) feeding the generation prompt;
  file extraction for PDF/docx is Phase-5+ (B1 v1 uses topic/strand context only
  — resource text extraction is an explicit AR-spec extension, do not block B1).

### WS-C — Signature (post-completion; distinct from the shipped gate)

- `RequiresSignature bool` on `Assignment` — snapshotted at create from the
  **grade-level signature default** (Q1 decision): policy pair
  `TenantAssignmentPolicy` (Settings.Core; one row per tenant;
  `RequiresSignatureDefault` default false) + `GradeAssignmentPolicy`
  (Students.Core; per (tenant, grade); `RequiresSignatureDefault bool?`,
  null = inherit tenant default). Resolution mirrors `INotificationPolicyResolver`
  (interface in Assignments.Core, HTTP impl in Assignments.Api); the create
  wizard pre-fills the checkbox from the resolved default; the author may
  override per AR. **C1 prerequisite round** (`ar-8-signature-defaults`).
- Per-(assignment, ward) sign-off: **extend `AssignmentSubmission`** with
  `SignOffState (None|AwaitingSignature|Signed)` + `SignedAt` + `FinalizedAt`
  (it already exists per pair and carries the lifecycle; no new gate-style table
  needed for v1). Computed ward status chain (spec §3.2) = projection from
  recipient (Delivered/Opened) + submission (InProgress/Completed) + sign-off
  (AwaitingSignature/Signed/Finalized) — do **not** store the full chain enum.
- `SignatureEvent` standalone tenant entity (`signature_events`): `AssignmentId,
  StudentId, SignerGuardianId, SignedAt, IpAddress, UserAgent, ConsentTextShown,
  CertificateStoragePath?` + immutable (no Update methods). Idempotency: command
  rejects when `SignOffState == Signed`; unique index `(AssignmentId, StudentId)`
  where state Signed (partial index or checked in handler with rowversion).
- Sign command: `SignOffSubmissionCommand(assignmentId, studentId, guardianId,
  signatureType(Typed|Click), typedSignature?)` — endpoint captures
  `HttpContext.Connection.RemoteIpAddress` + `User-Agent`; consent text from
  Settings `TenantSignatureConsentText` (same Settings-entity pattern as B2
  org prompt; fallback embedded default).
- Locking: `Signed` blocks further submissions (`RecordSubmission` guard);
  `Finalize` (auto on sign or teacher action) bridges grade to `SubmissionReview`.
- Certificate (C3; D-1/D-3 decided — local-FS `IFileStore` + QuestPDF): generate on Finalize → `IFileStore` path →
  reference in `SignatureEvent`; download endpoint
  `GET /assignments/{id}/students/{sid}/certificate`.
- Delegation: `ReassignSignOffCommand(guardianId)` — validates the new guardian
  is a `StudentGuardian` for the ward (via students-api lookup), resets
  `AwaitingSignature` and re-notifies.

### WS-D — Module gating

- `WardModuleProgress` standalone tenant entity (`ward_module_progress`):
  `(AssignmentId, StudentId, ContentModuleId)` unique, `ProgressPercent`,
  `CompletedAt?`. `RecordModuleProgressCommand` (upsert, clamp 0–100, mark
  complete at threshold).
- Gating: submit-command validation — every `IsRequired` module for the
  assignment must have `CompletedAt != null` else 403-style error; UI mirrors
  with locked question block. Pure `ModuleGateEvaluator` (unit-testable, filter-style).
- Player UI (ward surface): module sequence page, embedded video (`<video>` or
  Fluent embed), guide render (markdown or PDF link), progress beacon posts
  (debounced), question block (reuses B1 question rendering minus correct flags),
  submit. Video watch-% requires JS interop timeupdate posts (collocated
  `.razor.js` — `use-js-interop` skill); scroll-complete via JS IntersectionObserver.
  Captions: require `<track>` when module is video (NFR §6) — author-side
  validation can only warn in v1.

### WS-E — Delivery & deep links

- Deep links (E1): `IDataProtection` token: purpose `ar-deeplink`, payload
  `(assignmentId, wardStudentId, contactId/ownerType, role, exp)`; TTL from
  effective policy `LinkValidityDays` (already resolved at publish — persist the
  resolved value + issued token on `AssignmentRecipient` new column `DeepLinkToken`,
  `DeepLinkExpiresAt`). Public (unauthenticated) routes group in the portal host:
  `MapWardDeepLinkEndpoints` with NO `RequireAuthorization` but token validation
  middleware — new pattern; flag-gate (`FEATURE:EnableDeepLinks`) for dark launch.
- Delivery (E2): channel-provider abstraction in a new
  `Assignments.Core/Services/Delivery` (`IEmailSender`, `ISmsSender` — D-2
  decided: **MailKit SMTP** implementation; SMS + WhatsApp stubbed
  log-and-skip, already filtered by policy `BlockedChannels`). `NotificationLog` standalone tenant entity (`notification_logs`:
  `AssignmentId, RecipientId, ContactId, Channel, Kind (Publish|Reminder|
  Completion|Overdue), Attempt, SentAt?, DeliveryStatus (Queued|Sent|Failed),
  FailureReason?, NextRetryAt?`). Publisher v1.1 (already noted in
  `AssignmentNotificationBroadcaster`): one consolidated per-contact message with
  deep link; retry with backoff (store-driven worker loop).
- Worker (E3): new project `SchoolCollab.Assignments.Worker` (copy
  students-worker shape: appsettings re-anchor, RabbitMQ, Redis, hosted
  services). Hosted services: `NotificationDispatchService` (drain Queued logs,
  send via providers, backoff), `ReminderSweepService` (unsigned/incomplete →
  reminder logs honoring `MaxReminders`/`ReminderIntervalHours`/`SendoutTimeOfDay`),
  `ArchiveSweepService` (A2). Consumes `AssignmentPublishedIntegrationEvent` +
  new `SubmissionCompletedIntegrationEvent` (guardian completion trigger).
  AppHost wiring + CPM packages for providers.

### WS-F — Portal surface

- **D-4 resolved (2026-09-03): new lightweight host `SchoolCollab.Families`**
  (Blazor SSR + InteractiveServer, DataProtection token middleware, no OIDC
  initially). **No identity/auth system exists for students/guardians yet** —
  "ward" ≡ "student" (AR-spec term; repo terms are Student/Guardian; the
  Assignments context already names student references `WardStudentId` on
  guardian-facing rows). Identity is the existing Students-context
  `Student`/`Guardian` entities with their verified `Contact` records; deep-link
  tokens address contacts (E1 payload already carries contactId); no Keycloak
  users for students/guardians in v1. Student completion + guardian sign-off
  components live in an RCL so the host stays swappable.
- Ward completion page + guardian sign-off page are WS-D2/C2 components;
  they must be authored as RCL components (Admin.Shared or a new
  `Families` RCL) so the host choice is deferred.

### WS-G — Compliance

- WCAG 2.1 AA: FluentUI baseline helps; audit checklist round in Phase 5
  (axe-core Playwright pass — `Settings.Tests.Playwright` is the precedent).
- Audit immutability: `SignatureEvent` has no Update; outbox already append-only.
- Retention (open question 6): archive indefinitely + manual export in v1.
- New flags → `documents/configuration.md` §2 mapping in the same round that
  adds them (AGENTS.md rule).

---

## 3. Round slicing for the four-agent rounds

Each round = one `documents/rounds/round-<slug>.md` + `diffs-<slug>.patch`
(Tier 3). Recommended slicing (each is a UI round when it touches `.razor` →
tester pass fires; each keeps the Tier-1 checklist failing so Tier 3 is correct):

| # | Round | Contents | Sources |
|---|---|---|---|
| 1 | `ar-1-ai-spec-core` | B1 phases 1–4 (domain + contracts + command/handler + migration) — backend only | AI spec §10.1–10.4, §3, §4 |
| 2 | `ar-2-ai-endpoint` | B1 phases 5–6 (prompt file + provider + endpoint + generator seam + tests) | AI spec §3.4, §4.3, §11 |
| 3 | `ar-3-ai-wizard` | B1 phases 7–8 (wizard Resources + generation UI + Review paging + bUnit) — UI round | AI spec §3.5, §6, §7 |
| 4 | `ar-4-modules-resources` | A1 (ContentModule + AssignmentResource + IFileStore + upload stage + tests) | this doc §2 WS-A1 |
| 5 | `ar-5-lifecycle` | A2 (Scheduled/Approval/Archive + flags + Index/Detail surfaces) | this doc §2 WS-A2 |
| 6 | `ar-6-scoring` | A3 (SubmissionAnswer + scoring engine + attempts + submission extension) | this doc §2 WS-A3 |
| 7 | `ar-7-template` | A4 duplicate-as-template (+ Index action) | this doc §2 WS-A4 |
| 8 | `ar-8-signature-defaults` | C1 prerequisite: `TenantAssignmentPolicy` (Settings) + `GradeAssignmentPolicy` (Students) + effective resolver + grade-Detail UI + wizard pre-fill + tests | this doc §2 WS-C1 |
| 9+ | Phase 2–4 rounds | ward queries/player (D), sign-off (C, after ar-8), delivery/worker (E), portal host (F) | this doc §2 |

Phase 0 decisions were resolved 2026-09-03 (breakdown doc §2) — the orchestrator
cites them in the round doc's Plan header; no open blockers remain for rounds 4+.

**Round log:**
- `ar-1-ai-spec-core` — **CLOSED 2026-09-05** (`documents/rounds/round-ar-1-ai-spec-core.md`
  + `diffs-ar-1-ai-spec-core.patch`). Delivered: questions/options/attachments +
  `AiPromptOverride` wired through create/update commands with typed
  `AssignmentQuestionValidationException` validation (FR-252 / TF-canonical / EC-7),
  additive migration `20260905113946_AddAiPromptOverrideAndQuestionModelAnswer`, API
  route mapping + 400/404 handling, `QuestionTypeDto` JSON converter. 22 files,
  1830 insertions / 20 deletions. Authoritative: build 0 errors; tests 121/0 + 1/0 +
  20/0. One rework iteration (typed-exception rule — the plan's own literal text was
  the defect; the repo rule wins over plan text).
- **Carried follow-up (later round):** verify EF owned-children replacement
  persistence on a real provider — the update test uses a capturing fake due to an
  InMemory quirk; the `DetectChanges` repository seam is an accepted residual.
- **Operational notes (apply to all later rounds):** orchestrator doc-writing phases
  must use write-capable agents (`delegate`/`worker` — `oracle` is read-only); the
  parent runs all host steps itself (`runs.host` is unavailable in raw
  workflowScript); the worker child cap is 30 min — resume via the retained run id.

---

## 4. Discovered risks / open items for the orchestrator

1. **Identity TODO (§1.1)** — `CreatedByTeacherId = Guid.Empty` everywhere; AR
   personas need an identity decision before Approver auth, guardian attribution,
   or audit "attributable" NFRs can fully land. Deep-link tokens cover ward/
   guardian flows without it; teacher identity wiring is a candidate separate round.
2. **AI DI conflict (§1.5)** — second `ISystemPromptProvider` registration
   collides; per-endpoint engine construction is the spec-blessed fix.
3. **Upload ordering (§2 WS-A1)** — create-wizard needs staged uploads before
   the assignment id exists; stage-endpoint + orphan sweep is the recommended fix.
4. **Status-chain modeling (§2 WS-C)** — store minimal `SignOffState` on
   submission; full chain stays a projection. Don't duplicate the shipped
   `GuardianSubmissionGate` semantics.
5. **Enum extension side effects (§2 WS-A2)** — `AssignmentStatus` gains values;
   sweep every switch/description/filter list (Core, Contracts, EnumHelper,
   Index.razor, tests) — the architecture tests will catch unhandled patterns.
6. **Worker is new territory for Assignments** — no assignments worker exists;
   copy students-worker shape exactly (including the appsettings re-anchor comment).
7. **Playwright coverage** — no Assignments Playwright project; family-portal
   E2E rounds may add `SchoolCollab.Assignments.Tests.Playwright` (precedents:
   Settings/Students Playwright projects).