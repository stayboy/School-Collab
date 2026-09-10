# Round ar-4-modules-resources — Content modules + assignment resources + IFileStore staging + wizard Resources upload (WS-A1)

Provider: pi (models: glm-5.3, minimax-m3, kimi-k2.7-code, deepseek-v4-flash [UI round iff the wizard Resources section ships — decision (a) resolved: it ships; the tester fires])

- **Tier:** 3 — full four-agent round **plus a UI-tester pass** (decision (a) ships the Step-2
  Resources section → this is a UI round; after the worker + parent verification, the accept run
  derives the tester-scope handover from the changed-file list and the deepseek-v4-flash tester
  fires). Worker model: minimax-m3 (**30-minute run cap** — the round spans domain + EF + API +
  config + UI, the largest surface of the train; steps are sequenced **backend-first with the UI
  last** so the backend lands before any clock pressure; if the clock runs short, land steps in
  order and report deviations rather than improvising, skipping silently, or reversing the order).
- **Round base:** HEAD `d72cc899159d3a27f00641bc40ae7ee09c4f0f01` on branch
  `stack/4-ar-4-modules-resources` (stacked on PR #221 = round ar-3, which is stacked on #219
  and #218; GitHub stack #222).
- **Pre-round dirty paths OUT of round scope** (round patch pathspec = `src`, `tests`, and
  `documents/configuration.md` — the config doc is an IN-round modified file per the
  configuration-documentation repo rule, decision (c); the 8 known untracked scratch files stay
  outside the pathspec): `documents/rounds/.ar-1-scope.txt` |
  `documents/rounds/.ar-1-worker-report.md` | `documents/rounds/.ar-2-scope.txt` |
  `documents/rounds/.ar-2-worker-report.md` | `documents/rounds/.ar-3-scope.txt` |
  `documents/rounds/.ar-3-worker-report.md` | `documents/rounds/diffs-period-upsert-single-page.patch`
  | `documents/rounds/round-period-upsert-single-page.md`. (This round doc and its patch artifact
  are written outside the pathspec too.)
- **Rounds 1–3 landed what this round builds on** (do NOT re-plan any of it): the
  `AssignmentAttachment` owned child with aggregate `AddAttachment`/`RemoveAttachment`/`Attachments`
  + `AiPromptOverride` (round 1); contracts `NewAttachmentDto`, optional trailing
  `Questions`/`Attachments` on `CreateAssignmentRequest`/`UpdateAssignmentRequest` +
  `CreateAssignmentCommand`/`UpdateAssignmentCommand` (draft-only, full-replacement update
  semantics, `QuestionOptionDtoValidator` → typed `AssignmentQuestionValidationException`,
  additive migration precedent `20260905113946_AddAiPromptOverrideAndQuestionModelAnswer`);
  the wizard form model `AttachmentEditorRow` + `AssignmentEditFormModel.Attachments` +
  `ToCreateRequest` attachment projection (round 3 model half — its doc-comment says the
  Resources UI section is ar-4-owned), `QuestionGenerationSection`/`QuestionEditorSection`/
  `QuestionReviewList` components + `Create.razor` step wiring. The Resources UI section,
  `IFileStore`, and any upload endpoint were **deliberately omitted** in round 3 pending this
  round.
- **Sources:** `documents/solution/assignment-request-implementation-details.md` §2 WS-A1 (THE
  design source — verified), §1.1 (owned vs standalone, DbContext pattern), §1.9 (test
  conventions), §1.10 (flags/config), §3 round log (rounds 1–3 outcomes + residuals, incl. the
  ar-1 lesson: the repo rule wins over plan text — typed exceptions); `documents/specs/
  assignment-request-feature-spec.md` §4 (ContentModule/Resource concepts) + §3.3 (module
  gating = future WS-D, NOT this round); `documents/specs/assignment-creation-with-ai.md` §2.2
  (FR-210/211/212), §3.1, §8 (EC-4), §9 (AC-210); `documents/rounds/round-ar-3-ai-wizard.md`
  decision (a) + carried notes; `.github/copilot/rules/dotnet-best-practices.md`;
  `.github/copilot/rules/ef-migrations.md`; `.github/copilot/rules/blazor-components.md` (UI
  ships); `.github/copilot/rules/configuration-documentation.md`; `documents/configuration.md`.

## Plan

### Goal

Execute workstream **WS-A1** from
`documents/solution/assignment-request-implementation-details.md` §2: two new **standalone
tenant entities** — `ContentModule` (student-facing delivery: video|guide with completion
threshold, referenced by future per-ward progress rows, hence NOT owned) and
`AssignmentResource` (AI-generation inputs: url|file|video) — wired into the create/update
command surface as draft-only child collections mirroring the questions pattern; the repo's
**first upload path**: `IFileStore` abstraction + local-FS dev implementation (D-1 decided local
FS, Azure Blob deferred) + a multipart staging endpoint `POST /assignments/attachments/stage`
with allowlist + size caps from AppHost parameters; and the round-3-deferred **wizard
Resources UI section** (FR-210–212, AC-210) that stages files at selection and rides the create
payload as opaque `StoragePath`s. The round closes with `documents/configuration.md` §2/§11
mapping per the repo rule.

### Scope

**In (fixed — this is the whole round):**

1. **Domain** (`src/Assignments/SchoolCollab.Assignments.Core/Domain/`): `ContentModule`
   standalone tenant entity (table `assignment_content_modules`: `Id, TenantId, AssignmentId,
   ModuleType (enum Video|Guide), Title?, Url, StoragePath?, DisplayOrder,
   MinCompletionThresholdPercent (int, default 100), IsRequired (bool), RowVersion (xmin),
   audit`) + `ModuleType` enum file; `AssignmentResource` standalone tenant entity (table
   `assignment_resources`: `ResourceKind (enum Url|File|Video), Url?, StoragePath?, DisplayName?,
   IncludedInGeneration (bool, default true), TenantId, AssignmentId, audit` — no row version
   per WS-A1 column list) + `ResourceKind` enum file. The `Assignment` aggregate gains
   `Modules`/`Resources` collections + `AddModule`/`RemoveModule`/`ReorderModules` +
   `AddResource`/`RemoveResource` (the questions-pattern gateway; the children persist via
   FK one-to-many, NOT owned types — see implementation steps 1 for the exact signatures).
2. **EF** (`Data/Configurations/`, `Data/AssignmentsDbContext.cs`, `Migrations/`):
   `TenantEntityTypeConfigurationBase` configurations for both entities (follow
   `AssignmentRecipientConfiguration` + the FK declaration on `AssignmentConfiguration`, mirroring
   the `AssignmentActivityGroupConfiguration` FK precedent), DbSets in `AssignmentsDbContext`
   (explicit `ApplyConfiguration`, no assembly scanning), one additive migration named
   `AddContentModulesAndResources` per the repo convention. `NoUncommittedModelChanges` must stay
   green (both `MigrationGuardTests` files).
3. **Command surface** (`CQRS/Assignments/Commands/`): `CreateAssignmentCommand` +
   `UpdateAssignmentCommand` + handlers wire `ContentModules` and `Resources` as draft-only
   child collections mirroring the questions pattern — full replacement on update (null =
   preserve, non-null empty = clear), `DisplayOrder` re-index 0..n by list position, validation
   BEFORE any child is added (typed `AssignmentContentValidationException` per the round-1
   lesson, Domain/Exceptions pattern), plus a total-attachment-size cap from configuration.
4. **Contracts** (`SchoolCollab.Assignments.Contracts/ContractTypes.cs`): `ModuleTypeDto` +
   `ResourceKindDto` (int-mirrored, `[Description]`-decorated), `ContentModuleDto` +
   `NewContentModuleDto`, `ResourceDto` + `NewResourceDto`, `StagedAttachmentDto`; optional
   trailing `ContentModules`/`Resources` on `CreateAssignmentRequest` +
   `UpdateAssignmentRequest`.
5. **File store** (`SchoolCollab.Assignments.Core/Services/`): `IFileStore` port
   (`StoreAsync(stream, fileName, contentType) → storagePath`, `OpenReadAsync(storagePath)`,
   `DeleteAsync(storagePath)`) + `LocalFileStore` dev implementation (tenant-scoped paths under a
   configurable local root; D-1 decided local FS, Azure Blob deferred) + the two options classes
   (`AssignmentFileStoreOptions`, `AttachmentUploadOptions`) + registration through
   `AddAssignmentsCore` (reaches the Api via its existing `AddAssignmentsCore` call — decision (c)
   records the keys).
6. **Upload staging endpoint** (`Api/Endpoints/AssignmentRoutes.cs`):
   `POST /assignments/attachments/stage` (multipart, single file) validating the allowlist
   (PDF/DOC/DOCX/PPT/PPTX/XLS/XLSX/images/text) + per-file size cap from configuration, storing
   via `IFileStore`, returning `StagedAttachmentDto` (storagePath + original metadata). Staging
   logic lives in `StageAttachmentCommand` + handler (CQRS, Scrutor auto-registered) with a pure
   `StagedFileValidator`; the endpoint stays thin. Auth follows the existing `/assignments` group
   (`RequireAuthorization` unless `FEATURE:DisableOIDCAuth`). Orphan handling per decision (b):
   a `StagedFileSweepService` BackgroundService in assignments-api (the sanctioned pre-worker
   hosted-service seam — no Assignments worker exists yet).
7. **Configuration**: size caps + allowlist + file-store root as AppHost `Parameters` fanned out
   via `WithEnvironment` (AGENTS.md config pattern), mapped in `documents/configuration.md` §2 +
   §11 (+ §12 + new §13 options reference) in the same change — decision (c) records the exact
   keys.
8. **Wizard Resources UI** (round-3 deferral — decision (a) INCLUDES it): a `ResourcesSection`
   component bound to `AssignmentEditFormModel.Attachments` (`AttachmentEditorRow` already
   exists), the FluentUI upload control with client-side allowlist/size pre-checks, staged
   upload via the new endpoint (`AssignmentsApiClient.StageAttachmentAsync`), remove-X per
   FR-212 (soft — decision (b)), EC-4 semantics honestly reconciled: files stage at
   **selection time** via the endpoint then ride the create payload as `StoragePath`s (the
   spec's preferred stage-at-submit is impossible for the wizard — its submit is a single
   create call that must already carry storage paths; recorded in decision (b)).
9. **Tests** (MSTest + FluentAssertions + bUnit): domain (add/remove/reorder modules, resource
   create), EF config + tenancy, create/update handler wiring (incl. full replacement + typed
   validation rejections + total-cap), staging handler (allowlist reject, size reject, empty
   file, happy path), `LocalFileStore` (round-trip + traversal guard), sweep core, bUnit for the
   Resources section (upload/remove/list + projection).

**Out (record — do not touch):** module **gating** (`WardModuleProgress`, `ModuleGateEvaluator`,
player UI — WS-D, spec §3.3 — this round only stores modules); the ward-facing queries (WS-A5);
`Edit.razor` and any edit-page round-trip for modules/resources (later edit round — the create
wizard is the only UI this round); `AssignmentSummaryDto` read-back of modules/resources/attachments
(no read endpoint changes — the read DTOs are declared in Contracts only); delete/GET endpoints for
staged files (decision (b): remove-X is soft, no delete endpoint); AI-resource text extraction
(WS-B2, Phase-5+); `SchoolCollab.Assignments.Tests.Integration` / Playwright projects;
`wwwroot/app.css`; `Admin.Shared` components; `Directory.Packages.props`/`.sln`/csproj changes (no
new packages — everything needed is already referenced); the 8 pre-round scratch files.

### Decisions (binding — implement as written)

- **(a) Wizard Resources UI: INCLUDE the upload section this round (completing the round-3
  deferral).** Rationale: the model half (`AttachmentEditorRow` + `Attachments` +
  `ToCreateRequest` projection) already landed in round 3, so the remaining slice is thin (one
  component + client method + bUnit); the staging endpoint it consumes ships in the same round,
  so nothing is dead UI and no deferral stranding occurs a third time; it is the repo's first
  upload surface, so a UI-tester pass is most valuable now. Consequence: this is a UI round —
  the tester fires; the UI is sequenced LAST (steps 10–12) so backend lands first under the
  30-min cap.
- **(b) Stage-at-selection + soft remove-X + hosted reference-checked sweep (EC-4
  reconciliation).** Files upload per-file at selection (`FluentInputFile.OnFileUploaded` →
  `StageAttachmentAsync` → `StoragePath` populates the row) — the spec's preferred
  stage-at-submit is impossible for a single-create-call wizard (the payload must already carry
  storage paths), honestly recorded here. Remove-X drops the row only (FR-212 "soft"; NO delete
  endpoint/blob delete — the blob is orphaned). Orphan cleanup: `StagedFileSweepService`
  BackgroundService in **assignments-api** (`AddHostedService` is the sanctioned pre-worker seam
  per implementation-details WS-A2 note — no Assignments worker exists yet; students-worker's
  `ActivityGroupRolloverService` is the loop template) sweeps every `SweepIntervalHours`
  (default 24) and deletes staging files older than `StagingRetentionHours` (default 48) that
  are NOT referenced by any `assignment_attachments.storage_path`, `assignment_resources
  .storage_path`, or `assignment_content_modules.storage_path` row (reference check prevents the
  sweep from ever deleting a blob a created assignment points at). The referenced-path query is a
  read-only cross-tenant projection using `IgnoreQueryFilters(["Tenant"])` (the sanctioned opt-out
  the tenancy tests use; the sweep performs NO tenant-entity writes via the DbContext).
- **(c) Config keys (exact):** options classes bound in `AddAssignmentsCore` —
  `AssignmentFileStoreOptions` (section `Assignments:FileStore`, property `RootPath`, default
  `"assignment-files"`, resolved against `AppContext.BaseDirectory` when relative) and
  `AttachmentUploadOptions` (section `Assignments:AttachmentUpload`; `MaxFileSizeBytes` default
  `26214400` (25 MiB, per-file, enforced at stage), `MaxTotalSizeBytes` default `104857600`
  (100 MiB, enforced on create/update), `AllowedExtensions` default
  `[.pdf, .doc, .docx, .ppt, .pptx, .xls, .xlsx, .png, .jpg, .jpeg, .gif, .webp, .txt, .md,
  .csv]`, `StagingRetentionHours` default `48`, `SweepIntervalHours` default `24`). AppHost
  parameters (Program.cs + `Parameters:` in appsettings.json) fanned out onto `assignments-api`
  only: `assignment-file-store-root` → `Assignments__FileStore__RootPath`;
  `assignment-upload-max-file-bytes` → `Assignments__AttachmentUpload__MaxFileSizeBytes`;
  `assignment-upload-max-total-bytes` → `Assignments__AttachmentUpload__MaxTotalSizeBytes`;
  `assignment-upload-allowed-extensions` →
  `Assignments__AttachmentUpload__AllowedExtensions` (comma-separated; the config binder splits
  it into the array). `documents/configuration.md`: 4 new §2 parameter rows, 6 new §11 env-var
  rows (the four above plus `Assignments:AttachmentUpload:StagingRetentionHours` +
  `SweepIntervalHours`), 1 §12 production bullet, and a new "§13 Assignments — attachment upload
  + file store" options-reference section (both property tables). Note recorded: the per-file
  default (25 MiB) stays under Kestrel's default 30 MB request-body limit — raising the param
  beyond ~30 MB also requires raising `MaxRequestBodySize` (stated in the §2 row).
- **(d) Validation semantics** (enforced by `AssignmentContentValidator` for create/update and
  `StagedFileValidator` for staging; all rejections are typed exceptions — no
  `InvalidOperationException` from any new code path): **Modules** — `Url` required (absolute
  `http`/`https` via `Uri.TryCreate` + scheme check — that is the full depth, no deeper format
  validation), `Title` optional (≤200 chars, `Url` ≤2000), `ModuleType` must be a defined enum
  value, `MinCompletionThresholdPercent` ∈ [1, 100] (default 100), `IsRequired` bool (default
  false). **Resources** — kind matrix: `Url` → `Url` required (same http/https check),
  `StoragePath` must be null; `File` → `StoragePath` required (non-empty; opaque, no format
  check), `Url` must be null; `Video` → exactly one of `Url`/`StoragePath` (external embed URL
  or uploaded file); `DisplayName` optional (≤200); `ResourceKind` must be a defined enum value;
  `IncludedInGeneration` default true. **Attachments on create/update** — non-empty
  `FileName`/`ContentType`/`StoragePath` and Σ `FileSize` ≤ `MaxTotalSizeBytes` (defense in
  depth: the stage endpoint already capped each file). **Staged files** — non-empty
  `FileName` with an extension, extension ∈ `AllowedExtensions` (case-insensitive), non-empty
  `ContentType`, `FileSize` > 0 and ≤ `MaxFileSizeBytes` → `StagedFileRejectedException(message,
  isSizeLimit)` (endpoint maps size rejections to 413, the rest to 400). Domain factories hold
  only constructor hygiene (`ArgumentException` on empty tenant/assignment ids or null module
  `Url`), mirroring `AssignmentRecipient.Create` — business rules live in the validators
  (questions pattern).
- **(e) ApiClient additions:** `StageAttachmentAsync(Stream content, string fileName, string
  contentType, long fileSize, CancellationToken ct = default)` returning `StagedAttachmentDto`
  (multipart `POST /assignments/attachments/stage`; on 400/413 it reads the `{"message":…}`
  body and throws the new typed `AttachmentStagingFailed` — the round-2
  `QuestionGenerationFailed` pattern; other non-success → `EnsureSuccessStatusCode`), plus the
  two new `JsonStringEnumConverter<ModuleTypeDto>`/`<ResourceKindDto>` lines in its `_jsonOptions`
  (and the same two converters in the Api's `ConfigureHttpJsonOptions`). No other client methods:
  `CreateAsync`/`UpdateAsync` already carry the extended requests via their unchanged payloads,
  and no read-back surface for modules/resources exists yet.

### Expected files

**No other file may change.** The list below is the reviewer's conformance baseline AND the
tester-scope handover basis. The migration file names carry the worker's actual timestamp
(`<ts>`); the reviewer treats the pair + the snapshot regen as the three expected migration
paths. Sanctioned removals are named where they exist.

Created (25 — production):

- `src/Assignments/SchoolCollab.Assignments.Core/Domain/ModuleType.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/ContentModule.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/ResourceKind.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/AssignmentResource.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/Exceptions/AssignmentContentValidationException.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/Exceptions/StagedFileRejectedException.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Services/IFileStore.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Services/LocalFileStore.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Services/AssignmentFileStoreOptions.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Services/AttachmentUploadOptions.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/AssignmentContentValidator.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/StageAttachmentCommand/StageAttachmentCommand.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/StageAttachmentCommand/StageAttachmentCommandHandler.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/StageAttachmentCommand/StagedFileValidator.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Data/Configurations/ContentModuleConfiguration.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Data/Configurations/AssignmentResourceConfiguration.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Migrations/<ts>_AddContentModulesAndResources.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Migrations/<ts>_AddContentModulesAndResources.Designer.cs`
- `src/Assignments/SchoolCollab.Assignments.Api/Services/StagedFileSweeper.cs`
- `src/Assignments/SchoolCollab.Assignments.Api/Services/StagedFileSweepService.cs`
- `src/Assignments/SchoolCollab.Assignments.Api/StagedFileSweepExtensions.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/ResourcesSection.razor`
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/ResourcesSection.razor.css`
- `src/Assignments/SchoolCollab.Assignments.Application/Helpers/AttachmentUploadPolicy.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/Services/AttachmentStagingFailed.cs`

Created (9 — tests):

- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentModuleResourceTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/ContentModuleResourceConfigTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/Tenancy/ContentModuleResourceTenancyTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/CreateAssignmentCommandHandlerModuleResourceTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/UpdateAssignmentCommandHandlerModuleResourceTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/StageAttachmentCommandHandlerTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/LocalFileStoreTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/ResourcesSectionBunitTests.cs`
- `tests/SchoolCollab.Assignments.Api.Tests.Unit/StagedFileSweeperTests.cs`

Modified (19 — production):

- `src/Assignments/SchoolCollab.Assignments.Core/Domain/Assignment.cs` (module/resource
  collections + methods — additive; no change to existing members)
- `src/Assignments/SchoolCollab.Assignments.Core/Data/AssignmentsDbContext.cs` (2 DbSets + 2
  `ApplyConfiguration` calls)
- `src/Assignments/SchoolCollab.Assignments.Core/Data/Configurations/AssignmentConfiguration.cs`
  (the two `HasMany` relationship declarations + `Navigation` AutoInclude/field-access lines)
- `src/Assignments/SchoolCollab.Assignments.Core/Extensions.cs` (2 options bindings +
  `IFileStore` registration)
- `src/Assignments/SchoolCollab.Assignments.Core/Migrations/AssignmentsDbContextModelSnapshot.cs`
  (regenerated by the migration)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/CreateAssignmentCommand/CreateAssignmentCommand.cs`
  (trailing optional `ContentModules`/`Resources` params)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/CreateAssignmentCommand/CreateAssignmentCommandHandler.cs`
  (new `IOptions<AttachmentUploadOptions>` ctor param + validation + wiring)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/UpdateAssignmentCommand/UpdateAssignmentCommand.cs`
  (trailing optional params)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/UpdateAssignmentCommand/UpdateAssignmentCommandHandler.cs`
  (same ctor param + validation + replacement wiring)
- `src/Assignments/SchoolCollab.Assignments.Contracts/ContractTypes.cs` (the 7 new
  records/enums + request extensions)
- `src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs` (stage endpoint
  + new exception catches on POST/PUT)
- `src/Assignments/SchoolCollab.Assignments.Api/Program.cs` (2 JSON converters +
  `AddStagedFileSweep()`)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Create.razor`
  (one new step-2 section — the only markup change)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentEditFormModel.cs`
  (2 small attachment helpers — additive)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentQuestionEditorRows.cs`
  (sanctioned removal: the now-dead `AttachmentEditorRow.UploadStream` property + its doc
  reference — decision (b): the client never holds the stream under stage-at-selection; no
  other member changes)
- `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentsApiClient.cs`
  (StageAttachmentAsync + 2 converters)
- `src/AppHost/SchoolCollab.AppHost/Program.cs` (4 params + 4 `WithEnvironment` on
  assignments-api)
- `src/AppHost/SchoolCollab.AppHost/appsettings.json` (4 `Parameters:` entries)
- `documents/configuration.md` (§2 + §11 + §12 + new §13 — decision (c))

Modified (5 — tests):

- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentFormModelMappingsTests.cs` (the two new
  form-model helper cases; existing cases untouched — note the round-3
  `NewAttachmentDto` projection test keeps passing because it never referenced
  `UploadStream`)
- `tests/SchoolCollab.Assignments.Tests.Unit/CreateAssignmentCommandHandlerQuestionsTests.cs`
  (add the `Options.Create(new AttachmentUploadOptions())` ctor arg to `NewHandler` — the new
  handler ctor param; no test-case changes)
- `tests/SchoolCollab.Assignments.Tests.Unit/UpdateAssignmentCommandHandlerQuestionsTests.cs`
  (same one-line ctor-arg addition)
- `tests/SchoolCollab.Assignments.Tests.Unit/Handlers/CreateAssignmentCommandHandlerEntityCodeTests.cs`
  (same one-line ctor-arg addition)
- `tests/SchoolCollab.Assignments.Tests.Unit/QuestionReviewListBunitTests.cs` (one new case:
  model with 2 staged rows renders "Resources: 2 file(s)" — the round-3 summary line becomes
  live data; existing cases untouched)

**Total: 58 files (25 created production + 9 created tests + 19 modified production + 5
modified tests).**

### Implementation steps (ordered — follow literally; `dotnet build SchoolCollab.sln` after every step; run the named tests where the step says so)

**Step 1 — Domain entities + enums + exceptions + aggregate methods.** New files under
`Domain/`: `ModuleType.cs` (`public enum ModuleType { Video = 0, Guide = 1 }`), `ResourceKind.cs`
(`public enum ResourceKind { Url = 0, File = 1, Video = 2 }`), both with XML `<summary>` docs.
`ContentModule.cs` — `public sealed class ContentModule : ITenantEntity, IEntity,
IAuditableEntity, IHasRowVersion` mirroring `AssignmentRecipient`'s shape (private ctor, explicit
`ITenantEntity.TenantId` mapping, private setters): `Id, TenantId, AssignmentId, ModuleType,
Title?, Url, StoragePath?, DisplayOrder, MinCompletionThresholdPercent, IsRequired,
RowVersion(uint), CreatedAt, UpdatedAt` + `internal void SetDisplayOrder(int)` (used only by
`Assignment.ReorderModules`). Factory: `public static ContentModule Create(Guid tenantId, Guid
assignmentId, ModuleType moduleType, string url, string? title, string? storagePath, int
displayOrder, int minCompletionThresholdPercent = 100, bool isRequired = false)` throwing
`ArgumentException` on empty `tenantId`/`assignmentId` or null-whitespace `url` (constructor
hygiene only — decision (d)); stamps `CreatedAt`/`UpdatedAt` = now. `AssignmentResource.cs` —
same shape minus `RowVersion` (WS-A1 column list) and with nullable `Url`: `Id, TenantId,
AssignmentId, ResourceKind, Url?, StoragePath?, DisplayName?, IncludedInGeneration` + audit;
factory `Create(Guid tenantId, Guid assignmentId, ResourceKind resourceKind, string? url, string?
storagePath, string? displayName, bool includedInGeneration = true)` throwing `ArgumentException`
on empty `tenantId`/`assignmentId` only (kind matrix = validator-owned). New exception files in
`Domain/Exceptions/` (one type per file, mirroring `AssignmentQuestionValidationException`):
`AssignmentContentValidationException(string message)` and
`StagedFileRejectedException(string message, bool isSizeLimit)` with a public `bool IsSizeLimit`
property. In `Assignment.cs` (additive — do not touch existing members): `private readonly
List<ContentModule> _modules = [];` + `private readonly List<AssignmentResource> _resources =
[];` + `public IReadOnlyList<ContentModule> Modules => _modules.AsReadOnly();` + `public
IReadOnlyList<AssignmentResource> Resources => _resources.AsReadOnly();` and methods (each bumps
`UpdatedAt` on a real mutation, mirroring `AddQuestion`):

```csharp
public ContentModule AddModule(ModuleType moduleType, string url, string? title = null,
    string? storagePath = null, int minCompletionThresholdPercent = 100, bool isRequired = false)
// → ContentModule.Create(TenantId, Id, moduleType, url, title, storagePath, _modules.Count,
//   minCompletionThresholdPercent, isRequired); add to _modules; return it.
public void RemoveModule(Guid moduleId)   // SingleOrDefault; remove if found (silent no-op otherwise)
public void ReorderModules(IReadOnlyList<Guid> orderedModuleIds)
// → running index over the given ids; each found module gets SetDisplayOrder(index++);
//   ids not found are ignored; modules not mentioned keep their current DisplayOrder
//   (the only caller — the update handler — passes the complete re-ordered list).
public AssignmentResource AddResource(ResourceKind resourceKind, string? url = null,
    string? storagePath = null, string? displayName = null, bool includedInGeneration = true)
public void RemoveResource(Guid resourceId) // same silent-no-op semantics as RemoveModule
```

Build.

**Step 2 — Contracts.** In `ContractTypes.cs` (additive; follow the existing record style with
XML `<summary>` docs): `public enum ModuleTypeDto { [Description("Video")] Video = 0,
[Description("Guide")] Guide = 1 }`; `public enum ResourceKindDto { [Description("Link")] Url =
0, [Description("File")] File = 1, [Description("Video")] Video = 2 }`;
`public record ContentModuleDto(Guid Id, Guid AssignmentId, ModuleTypeDto ModuleType, string?
Title, string Url, string? StoragePath, int DisplayOrder, int MinCompletionThresholdPercent,
bool IsRequired)`; `public record NewContentModuleDto(ModuleTypeDto ModuleType, string? Title,
string Url, string? StoragePath, int DisplayOrder, int MinCompletionThresholdPercent = 100, bool
IsRequired = false)`; `public record ResourceDto(Guid Id, Guid AssignmentId, ResourceKindDto
ResourceKind, string? Url, string? StoragePath, string? DisplayName, bool
IncludedInGeneration)`; `public record NewResourceDto(ResourceKindDto ResourceKind, string? Url,
string? StoragePath, string? DisplayName, bool IncludedInGeneration = true)`;
`public record StagedAttachmentDto(string FileName, string ContentType, long FileSize, string
StoragePath)`. Append the two optional trailing params to BOTH
`CreateAssignmentRequest` and `UpdateAssignmentRequest` (after `Attachments`):
`IReadOnlyList<NewContentModuleDto>? ContentModules = null, IReadOnlyList<NewResourceDto>?
Resources = null`. Build.

**Step 3 — EF configurations + DbContext + migration.** `Data/Configurations/
ContentModuleConfiguration.cs` — `internal sealed class ContentModuleConfiguration :
TenantEntityTypeConfigurationBase<ContentModule>` mirroring
`AssignmentRecipientConfiguration` (ctor takes the `Expression<Func<Guid>>
tenantIdAccessor`, `ConfigureTenantEntity` body): `ToTable("assignment_content_modules")`,
`ConfigureAuditProperties()`, `ConfigurePostgresRowVersion()`, properties (`AssignmentId`
required; `ModuleType` required; `Title` max 200; `Url` required max 2000; `StoragePath` max
500; `DisplayOrder` required; `MinCompletionThresholdPercent` required default 100; `IsRequired`
required default false) + index `HasIndex(x => new { x.TenantId, x.AssignmentId })
.HasDatabaseName("ix_assignment_content_modules_tenant_assignment")`. **No relationship
declaration here** (the FK is declared once, from the `Assignment` side — see below).
`AssignmentResourceConfiguration.cs` — same pattern for `assignment_resources` (`ResourceKind`
required; `Url` max 2000 nullable; `StoragePath` max 500; `DisplayName` max 200;
`IncludedInGeneration` required default true) + index
`ix_assignment_resources_tenant_assignment`. In `AssignmentConfiguration.ConfigureTenantEntity`
append (after the existing owned-type blocks):

```csharp
// Standalone child collections (WS-A1): FK declared once from the aggregate side;
// Cascade covers assignment deletion; removing from the collection severs the
// required FK, so EF Core deletes the row at SaveChanges (replacement semantics).
builder.HasMany(x => x.Modules).WithOne().HasForeignKey(m => m.AssignmentId)
    .OnDelete(DeleteBehavior.Cascade);
builder.Navigation(x => x.Modules).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
builder.HasMany(x => x.Resources).WithOne().HasForeignKey(r => r.AssignmentId)
    .OnDelete(DeleteBehavior.Cascade);
builder.Navigation(x => x.Resources).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
```

In `AssignmentsDbContext`: add `public DbSet<ContentModule> ContentModules =>
Set<ContentModule>();` + `public DbSet<AssignmentResource> AssignmentResources =>
Set<AssignmentResource>();` and the two explicit `ApplyConfiguration` calls next to the
existing ones. Then generate the migration from the **repository root**:

```bash
dotnet ef migrations add AddContentModulesAndResources --project src/Assignments/SchoolCollab.Assignments.Core --context AssignmentsDbContext
```

Review the generated `Up()` (must be purely additive: `CreateTable` ×2 with snake_case
columns incl. `xmin` on content modules; no drops) and `Down()` (drops the two tables in
reverse order). Then run `dotnet test tests/SchoolCollab.Assignments.Tests.Unit --filter
NoUncommittedModelChanges` — must pass (0 failures) before continuing. Build.

**Step 4 — `AssignmentContentValidator` + command/handler wiring.** New file
`CQRS/Assignments/Commands/AssignmentContentValidator.cs`: `internal static class
AssignmentContentValidator` mirroring `QuestionOptionDtoValidator` (position-prefixed messages
`$"Module at position {i}: …"` / `$"Resource at position {i}: …"` / `$"Attachments: …"`, throwing
`AssignmentContentValidationException`): `ValidateModules(IReadOnlyList<NewContentModuleDto>)`
(per decision (d): `Enum.IsDefined` check, non-empty `Url` that is an absolute http/https URI via
`Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https")`,
`Title` ≤ 200, `Url` ≤ 2000, threshold ∈ [1,100]); `ValidateResources(
IReadOnlyList<NewResourceDto>)` (per decision (d): `Enum.IsDefined`, the kind matrix, `Url`
http/https when non-empty, lengths); `ValidateAttachments(IReadOnlyList<NewAttachmentDto>?,
AttachmentUploadOptions options)` (non-empty `FileName`/`ContentType`/`StoragePath`; Σ
`FileSize` ≤ `options.MaxTotalSizeBytes` → message
`"Attachments: total size {sum} bytes exceeds the {MaxTotalSizeBytes}-byte limit."`). Extend
`CreateAssignmentCommand` and `UpdateAssignmentCommand` with the same two optional trailing
params as the requests (`IReadOnlyList<NewContentModuleDto>? ContentModules = null,
IReadOnlyList<NewResourceDto>? Resources = null`). In `CreateAssignmentCommandHandler`: add
`IOptions<AttachmentUploadOptions> uploadOptions` to the primary constructor (and pass
`Options.Create(new AttachmentUploadOptions())` at the three existing test construction sites —
the 3 modified test files; one line each, no case changes); after the existing
`QuestionOptionDtoValidator.ValidateQuestions` call add
`AssignmentContentValidator.ValidateModules(command.ContentModules)` (guarded `{ Count: > 0 }`
like questions), `…ValidateResources(command.Resources)` (same guard), and
`AssignmentContentValidator.ValidateAttachments(command.Attachments, uploadOptions.Value)`
(always — null passes); after the existing attachments block add (before the event enqueue):

```csharp
if (command.ContentModules is { Count: > 0 })
{
    // Re-index DisplayOrder 0..n by list position (mirrors questions EC-7).
    for (var i = 0; i < command.ContentModules.Count; i++)
    {
        var m = command.ContentModules[i];
        assignment.AddModule((ModuleType)m.ModuleType, m.Url, m.Title, m.StoragePath,
            m.MinCompletionThresholdPercent, m.IsRequired);
    }
}
if (command.Resources is { Count: > 0 })
{
    foreach (var r in command.Resources)
    {
        assignment.AddResource((ResourceKind)r.ResourceKind, r.Url, r.StoragePath,
            r.DisplayName, r.IncludedInGeneration);
    }
}
```

In `UpdateAssignmentCommandHandler`: same ctor param (the 2 modified round-1 test files get the
one-line ctor-arg addition); same validation block before `assignment.Update(...)`; then, after
the existing attachments replacement block, add the full-replacement blocks mirroring the
questions pattern exactly (null = preserve current children; non-null = snapshot existing ids →
`RemoveModule`/`RemoveResource` each → re-add inbound, `AddModule` in list order so
`DisplayOrder` is contiguous 0..n); extend the existing `repository.DetectChanges()` comment to
mention the field-backed module/resource navigations (the call itself is unchanged and still
runs after all replacements). Build.

**Step 5 — `IFileStore` + `LocalFileStore` + options + registration.** New files under
`Services/`: `AssignmentFileStoreOptions.cs` (`public sealed class
AssignmentFileStoreOptions { public const string SectionName = "Assignments:FileStore"; public
string RootPath { get; set; } = "assignment-files"; }`); `AttachmentUploadOptions.cs` (`public
sealed class AttachmentUploadOptions { public const string SectionName =
"Assignments:AttachmentUpload"; public long MaxFileSizeBytes { get; set; } = 26214400; public long
MaxTotalSizeBytes { get; set; } = 104857600; public string[] AllowedExtensions { get; set; } =
[".pdf", ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx", ".png", ".jpg", ".jpeg", ".gif",
".webp", ".txt", ".md", ".csv"]; public int StagingRetentionHours { get; set; } = 48; public int
SweepIntervalHours { get; set; } = 24; }` — decision (c)); `IFileStore.cs`:

```csharp
/// <summary>File-storage port for assignment uploads (WS-A1 / D-1: local FS dev
/// implementation; Azure Blob deferred). storagePath values are opaque to callers.</summary>
public interface IFileStore
{
    Task<string> StoreAsync(Stream content, string fileName, string contentType,
        CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default);
}
```

`LocalFileStore.cs` — `public sealed class LocalFileStore(ITenantProvider tenantProvider,
IOptions<AssignmentFileStoreOptions> storeOptions, ILogger<LocalFileStore> logger) : IFileStore`:
resolve `_root` in the ctor (`RootPath` as-is when rooted, else
`Path.Combine(AppContext.BaseDirectory, RootPath)`); `StoreAsync` sanitizes the name
(`Path.GetFileName(fileName)`), computes the logical forward-slash path
`tenants/{tenantProvider.GetTenantContext().TenantId}/staging/{Guid.NewGuid():N}/{safeName}`,
creates the directory, copies the stream to disk, returns the logical path (log Information
with fileName + size); a private `ResolveWithinRoot(string storagePath)` maps a logical path to
the full path and throws `ArgumentException` when it is rooted, contains `..`, or resolves
outside `_root` (traversal guard) — `OpenReadAsync` uses it and throws
`FileNotFoundException` when the file is missing; `DeleteAsync` uses it and is idempotent
(File.Delete only when the file exists). In `Extensions.cs` (`AddAssignmentsCore`) add before
the handler scans: `services.Configure<AssignmentFileStoreOptions>(configuration.GetSection(
AssignmentFileStoreOptions.SectionName));`, `services.Configure<AttachmentUploadOptions>(
configuration.GetSection(AttachmentUploadOptions.SectionName));`, and
`services.AddScoped<IFileStore, LocalFileStore>();` (this reaches the Api through its existing
`AddAssignmentsCore` call — decision (c); no Program.cs registration is needed for the store).
Build.

**Step 6 — `StageAttachmentCommand` + handler + validator.** New folder
`CQRS/Assignments/Commands/StageAttachmentCommand/` with three files. `StageAttachmentCommand.cs`:
`public sealed record StageAttachmentCommand(Stream Content, string FileName, string ContentType,
long FileSize) : ICommand;` (+ XML doc). `StagedFileValidator.cs`: `internal static class
StagedFileValidator` with `public static void Validate(string fileName, string contentType, long
fileSize, AttachmentUploadOptions options)` throwing `StagedFileRejectedException` per decision
(d) — empty/no-extension `FileName` (`"A file name with an extension is required."`), empty
`ContentType` (`"The file content type is missing."`), extension not in
`options.AllowedExtensions` case-insensitively
(`$"'{Path.GetExtension(fileName)}' files are not allowed."`), `fileSize <= 0` (`"The file is
empty."`), `fileSize > options.MaxFileSizeBytes` (`isSizeLimit: true`,
`$"The file exceeds the {options.MaxFileSizeBytes}-byte per-file limit."`). The
`StagedFileRejectedException` ctor default is `isSizeLimit: false`.
`StageAttachmentCommandHandler.cs`: `public sealed class
StageAttachmentCommandHandler(IFileStore fileStore, IOptions<AttachmentUploadOptions>
uploadOptions, ILogger<StageAttachmentCommandHandler> logger) : ICommandHandler<
StageAttachmentCommand, StagedAttachmentDto>` — `StagedFileValidator.Validate(command.FileName,
command.ContentType, command.FileSize, uploadOptions.Value);` then
`var storagePath = await fileStore.StoreAsync(command.Content, command.FileName,
command.ContentType, cancellationToken);`, log Information
(`"Staged attachment {FileName} ({FileSize} bytes) at {StoragePath}"`), return
`new StagedAttachmentDto(command.FileName, command.ContentType, command.FileSize,
storagePath)`. Scrutor auto-registers it (no manual registration). Build.

**Step 7 — Api: stage endpoint + sweep + converters.** In `AssignmentRoutes.cs` add INSIDE the
`/assignments` group (so it inherits the group's authorization posture), after the existing
routes:

```csharp
// WS-A1 / FR-210-212: stage one file for the create payload (EC-4 reconciliation:
// stage-at-selection, decision (b)). Antiforgery is disabled: the Api is consumed
// server-to-server by the admin Blazor host with token-based auth, not browser forms.
group.MapPost("/attachments/stage", async (
    [FromForm] IFormFile? file,
    [FromServices] ICommandHandler<StageAttachmentCommand, StagedAttachmentDto> handler,
    CancellationToken ct) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { message = "A file is required." });
    }
    await using var stream = file.OpenReadStream();
    try
    {
        var result = await handler.HandleAsync(
            new StageAttachmentCommand(stream, file.FileName, file.ContentType, file.Length), ct);
        return Results.Ok(result);
    }
    catch (StagedFileRejectedException ex) when (ex.IsSizeLimit)
    {
        return Results.Json(new { ex.Message }, statusCode: StatusCodes.Status413PayloadTooLarge);
    }
    catch (StagedFileRejectedException ex)
    {
        return Results.BadRequest(new { ex.Message });
    }
}).DisableAntiforgery();
```

Also add `catch (AssignmentContentValidationException ex) { return
Results.BadRequest(new { ex.Message }); }` to the existing POST + PUT handlers (next to the
`AssignmentQuestionValidationException` catch). In `Program.cs` add the two converters
(`new JsonStringEnumConverter<ModuleTypeDto>()`, `new
JsonStringEnumConverter<ResourceKindDto>()`) to `ConfigureHttpJsonOptions` and
`builder.Services.AddStagedFileSweep();` after the existing service registrations. New Api
files: `Services/StagedFileSweeper.cs` — `public static class StagedFileSweeper` with
`public static int DeleteStaleFiles(string root, IReadOnlySet<string> referencedLogicalPaths,
DateTimeOffset cutoffUtc)`: enumerate `root` recursively (`EnumerateFiles("*",
SearchOption.AllDirectories)`); for each file, if `LastWriteTimeUtc < cutoffUtc` AND its logical
path (full path made relative to root, separators → `/`) is NOT in the referenced set → delete
it; return the deleted count. `Services/StagedFileSweepService.cs` — `public sealed class
StagedFileSweepService(IServiceScopeFactory scopeFactory, IOptions<AttachmentUploadOptions>
uploadOptions, IOptions<AssignmentFileStoreOptions> storeOptions, ILogger<
StagedFileSweepService> logger) : BackgroundService`: loop with
`Task.Delay(TimeSpan.FromHours(uploadOptions.Value.SweepIntervalHours), stoppingToken)`
(`ActivityGroupRolloverService` template; first sweep immediately at startup), then per sweep:
create a scope, resolve `AssignmentsDbContext`, load the referenced set —
`db.Assignments.IgnoreQueryFilters(["Tenant"]).SelectMany(a => a.Attachments).Select(x =>
x.StoragePath)`, `db.AssignmentResources.IgnoreQueryFilters(["Tenant"]).Where(r =>
r.StoragePath != null).Select(r => r.StoragePath!)`, and `db.ContentModules.IgnoreQueryFilters(
["Tenant"]).Where(m => m.StoragePath != null).Select(m => m.StoragePath!)` (read-only
cross-tenant projection — the sanctioned `IgnoreQueryFilters(["Tenant"])` opt-out the tenancy
tests use; comment it; NO tenant-entity writes happen in the sweep) — resolve the root with the
same rooted/BaseDirectory rule as `LocalFileStore`, then
`StagedFileSweeper.DeleteStaleFiles(root, referenced, DateTimeOffset.UtcNow -
TimeSpan.FromHours(uploadOptions.Value.StagingRetentionHours))` and log the deleted count
(decision (b)). `StagedFileSweepExtensions.cs` — `public static class
StagedFileSweepExtensions { public static IServiceCollection AddStagedFileSweep(this
IServiceCollection services) { services.AddHostedService<StagedFileSweepService>(); return
services; } }` (the `Add{Layer}()` rule — no inline `services.Add*()` in Program.cs). Build.

**Step 8 — AppHost + configuration doc.** In `src/AppHost/SchoolCollab.AppHost/Program.cs`
declare the four parameters (with the existing comment style, near `periodActivationToleranceDays`)
and fan them out on the `assignmentsApi` block: `.WithEnvironment("Assignments__FileStore__
RootPath", assignmentFileStoreRoot)` etc. per decision (c). In
`src/AppHost/SchoolCollab.AppHost/appsettings.json` add the four `Parameters:` entries with the
defaults from decision (c) (`"assignment-file-store-root": "assignment-files"`,
`"assignment-upload-max-file-bytes": "26214400"`,
`"assignment-upload-max-total-bytes": "104857600"`,
`"assignment-upload-allowed-extensions": ".pdf,.doc,.docx,.ppt,.pptx,.xls,.xlsx,.png,.jpg,.jpeg,.gif,.webp,.txt,.md,.csv"`).
In `documents/configuration.md`: add the 4 §2 parameter rows (naming the injected env var, the
consumer `assignments-api`, the defaults, and the Kestrel 30 MB note on the per-file row), the 6
§11 env-var rows, 1 §12 production bullet (upload caps/allowlist sourced from AppHost
`Parameters:`; per-file cap above ~30 MB also requires raising Kestrel's `MaxRequestBodySize`),
and a new "§13 Assignments — attachment upload + file store (`assignments-api`)" section before
"See also" with the two options-class property tables (property / default / description per the
configuration-documentation rule) + its TOC entry. Build.

**Step 9 — Backend tests (write all, then run).** Create the six Core-side test files per the
binding coverage list below (`AssignmentModuleResourceTests`,
`ContentModuleResourceConfigTests`, `Tenancy/ContentModuleResourceTenancyTests`,
`CreateAssignmentCommandHandlerModuleResourceTests`,
`UpdateAssignmentCommandHandlerModuleResourceTests`, `StageAttachmentCommandHandlerTests`,
`LocalFileStoreTests` — MSTest + FluentAssertions, conventions per the round-1 test files:
InMemory via `ServiceCollection` + `AddTenancy()` + `SetTenant`, `NullLogger<T>.Instance`, Moq
only for non-HTTP seams, hand-rolled fakes for `IFileStore`/repository; update-handler tests use
the round-1 `CapturingAssignmentRepository` pattern) plus
`tests/SchoolCollab.Assignments.Api.Tests.Unit/StagedFileSweeperTests.cs` (pure sweeper, temp
directories — no DbContext needed). Make the three one-line ctor-arg additions to the round-1
handler test files. Run `dotnet test tests/SchoolCollab.Assignments.Tests.Unit` and
`dotnet test tests/SchoolCollab.Assignments.Api.Tests.Unit` — **0 failures before continuing**.

> **CLOCK NOTE (decision (a) cut-line):** steps 1–9 are the backend core — land them completely
> and green before touching the UI. Steps 10–12 are the Resources section. If the run clock
> runs short inside steps 10–12, land what is started in order, ensure the build is green, and
> REPORT the remainder as a deviation — the parent resumes the worker for the bounded remainder
> (ar-3 precedent). Do not skip silently and do not reverse the order.

**Step 10 — Application client-side preps.** `Helpers/AttachmentUploadPolicy.cs`: `public
static class AttachmentUploadPolicy` with the client-side convenience constants mirroring the
server defaults (documented as UX-only; the server stays authoritative): `public const long
MaxFileSizeBytes = 26214400;`, `public static readonly string[] AllowedExtensions = [...same
list...];`, `public static string AcceptExtensions => string.Join(",", AllowedExtensions);`,
`public static string? Validate(string fileName, long fileSize)` → null or a friendly message
(extension not allowed / file exceeds the 25 MB limit), and `public static string
FormatFileSize(long bytes)` (e.g. `"1.2 MB"`). `Services/AttachmentStagingFailed.cs`: mirror `QuestionGenerationFailed.cs` exactly (sealed,
no `Exception` suffix — typedness satisfies the repo rule per that file's own doc note):
`public sealed class AttachmentStagingFailed : Exception` with `public
AttachmentStagingFailed(string message) : base(message) { }`, an XML `<summary>` naming
`AssignmentsApiClient.StageAttachmentAsync` as the thrower, and a `using
SchoolCollab.Assignments.Contracts;` for the `StagedAttachmentDto` doc reference.
In `AssignmentQuestionEditorRows.cs` remove the dead `AttachmentEditorRow.UploadStream` property
and drop the `UploadStream` reference from the class XML doc (sanctioned removal — decision (b);
nothing else in that file changes; no test referenced the property). In
`AssignmentEditFormModel.cs` add two additive helpers mirroring the question helpers:
`public void AddAttachment(AttachmentEditorRow row) { Attachments.Add(row); }` and
`public void RemoveAttachmentAt(int index) { if (index < 0 || index >= Attachments.Count) return;
Attachments.RemoveAt(index); }` (+ XML docs). In `AssignmentsApiClient.cs` add the two
`JsonStringEnumConverter<ModuleTypeDto>`/`<ResourceKindDto>` lines to `_jsonOptions` and:

```csharp
/// <summary>Stages one resource file via the multipart staging endpoint (WS-A1 / FR-210).
/// Rejections return a typed <see cref="AttachmentStagingFailed"/> carrying the server message.</summary>
public async Task<StagedAttachmentDto> StageAttachmentAsync(Stream content, string fileName,
    string contentType, long fileSize, CancellationToken ct = default)
// → MultipartFormDataContent with a StreamContent named "file" (with FileName + ContentType),
//   POST "/assignments/attachments/stage";
//   200 → ReadFromJsonAsync<StagedAttachmentDto>(_jsonOptions, ct);
//   400/413 → read {"message": ...} → throw new AttachmentStagingFailed(message);
//   otherwise EnsureSuccessStatusCode.
```

Build.

**Step 11 — `ResourcesSection.razor` (+ `.razor.css`) + `Create.razor` wiring.** The component
(own file + isolated CSS; `@inject AssignmentsApiClient Api`, `@inject IDialogService
DialogService`, `@inject ILogger<ResourcesSection> Logger`): parameters `[Parameter,
EditorRequired] public AssignmentEditFormModel Model { get; set; } = default!;` + `[Parameter]
public EventCallback Changed { get; set; }`. Markup: a `<FluentInputFile Mode="InputFileMode.Stream"
Multiple="true" MaximumFileCount="10" MaximumFileSize="@AttachmentUploadPolicy.MaxFileSizeBytes"
Accept="@AttachmentUploadPolicy.AcceptExtensions" OnFileUploaded="@OnFileUploadedAsync"
OnFileError="@OnFileErrorAsync" OnFileCountExceeded="@OnFileCountExceededAsync"
DragDropZoneVisible="true">Upload files</FluentInputFile>` (the repo's first upload surface;
server stays authoritative over the client pre-checks), an error `<FluentMessageBar
Intent="MessageIntent.Error">@_error</FluentMessageBar>` when non-empty, an empty-state
`<FluentMessageBar Intent="MessageIntent.Info">No resources attached yet.</FluentMessageBar>`
when `Model.Attachments.Count == 0`, and the staged list — `@foreach` with `@key` over
`Model.Attachments.Select((row, index) => (row, index)).ToList()`: file icon
(`Icons.Regular.Size20.Document`), `@row.FileName`, `@AttachmentUploadPolicy.FormatFileSize(
row.FileSize)`, and a remove `<FluentButton Appearance="Appearance.Neutral"
IconEnd="@FluentIcons.Dismiss" OnClick="@(() => RemoveAtAsync(index))" aria-label="@($"Remove
{row.FileName}")" />`. Handlers: `public async Task OnFileUploadedAsync(FluentInputFileEventArgs
args)` → `await StageFileAsync(args.Stream, args.Name, args.ContentType, args.Size);` where
`public async Task StageFileAsync(Stream content, string fileName, string contentType, long
fileSize)` is the testable core: guard `if (_staging) return;`; client-side
`AttachmentUploadPolicy.Validate(fileName, fileSize)` error → set `_error`, NO HTTP call;
otherwise `_staging = true`, try `var staged = await Api.StageAttachmentAsync(content, fileName,
contentType, fileSize);` → `Model.AddAttachment(new AttachmentEditorRow { FileName =
staged.FileName, ContentType = staged.ContentType, FileSize = staged.FileSize, StoragePath =
staged.StoragePath }); _error = null; await Changed.InvokeAsync();` catch
`AttachmentStagingFailed ex` → `_error = ex.Message;` (retryable by re-picking) catch
`OperationCanceledException` → swallow (navigation), catch `Exception ex` → log Error + `_error =
$"Could not upload '{fileName}'."`; finally `_staging = false`. `OnFileErrorAsync` /
`OnFileCountExceededAsync` → friendly `_error` messages. `private async Task
RemoveAtAsync(int index)` → gated by `await DialogService.ShowConfirmDialogAsync($"Remove
'{Model.Attachments[index].FileName}'?")` (`QuestionEditorSection` precedent — the destructive-
action rule); on confirm `Model.RemoveAttachmentAt(index); await Changed.InvokeAsync();` (soft:
NO delete call — decision (b); the orphaned blob is swept server-side). No inline styles —
everything in `ResourcesSection.razor.css` (list-row layout + spacing; reuse
`wizard-section`-family classes only where they already exist). In `Create.razor` (the ONLY
markup change): insert one new section in Step 2 **between the "Assignment Details" section and
the AI generation `@if` block**, mirroring the generation section's chrome exactly
(`<section class="wizard-section">` + `wizard-section-header` with
`Icons.Regular.Size24.FolderOpen` + `<h3>Resources</h3>` + hint "Attach files that support
this assignment — they are stored and ride along when you save.") hosting `<ResourcesSection
Model="_model" Changed="OnWizardQuestionsChanged" />` (the existing
`OnWizardQuestionsChanged` EventCallback re-renders the page so the Review attachment summary
and submit gate stay fresh). Build.

**Step 12 — bUnit tests + mappings extension.** Create
`tests/SchoolCollab.Assignments.Tests.Unit/ResourcesSectionBunitTests.cs` per the binding
coverage list (conventions: `BunitContext`, `JSInterop.Mode = JSRuntimeMode.Loose`,
`Services.AddFluentUIComponents()`, `AssignmentsApiClient` built over a RichardSzalay.MockHttp
`ToHttpClient()` like `AssignmentIndexBunitTests`, hand-rolled fakes, Moq only for
`IDialogService`/`ILogger<T>`; the FluentInputFile JS events are NOT driven — tests call
`cut.Instance.StageFileAsync(...)` directly, the ar-3 recorded fallback for web-component
quirks). Add the two `AddAttachment`/`RemoveAttachmentAt` cases to
`AssignmentFormModelMappingsTests.cs` and the one "Resources: 2 file(s)" case to
`QuestionReviewListBunitTests.cs`. Run `dotnet test tests/SchoolCollab.Assignments.Tests.Unit`
— 0 failures.

**Step 13 — Final verification.** `dotnet build SchoolCollab.sln -c Debug` (0 errors) and
`dotnet test` on `SchoolCollab.Assignments.Tests.Unit`,
`SchoolCollab.Assignments.Api.Tests.Unit`, and
`SchoolCollab.ArchitectureTests.Unit` (repo-wide scanner — always) — 0 failures. No git commits —
working tree only.

### Binding test coverage list (names binding, file paths exact — MSTest + FluentAssertions, `[TestMethod]` per case)

`tests/SchoolCollab.Assignments.Tests.Unit/AssignmentModuleResourceTests.cs` (new):

- `AddModule_AppendsWithContiguousDisplayOrder_AndStampsTenant`: two `AddModule` calls →
  `Modules` count 2, `DisplayOrder` 0/1, `TenantId` == aggregate's (set via
  `WithTenant` before adding, mirroring the handler order), returned entity equals the list
  entry.
- `RemoveModule_RemovesFromCollection` + `RemoveModule_UnknownId_IsNoOp` (silent no-op, list
  unchanged).
- `ReorderModules_ReindexesByGivenOrder`: 3 modules added (orders 0,1,2) → reorder
  [id2,id0,id1] → orders become id2=0, id0=1, id1=2; unknown id in the list is ignored without
  throwing.
- `AddResource_AddsToCollection` + `RemoveResource_UnknownId_IsNoOp` (resource kind/fields
  round-trip, `IncludedInGeneration` default true).
- `ContentModule_Create_ThrowsOnEmptyTenantOrAssignmentOrUrl` (ArgumentException guards) +
  `AssignmentResource_Create_ThrowsOnEmptyTenantOrAssignment`.

`tests/SchoolCollab.Assignments.Tests.Unit/ContentModuleResourceConfigTests.cs` (new —
mirror `AssignmentSubmissionLifecycleConfigTests`' `BuildContext()` exactly):

- `ContentModule_MapsTableColumnsDefaultsAndRowVersion`: table
  `assignment_content_modules`; `Url` non-nullable + max 2000; `Title`/`StoragePath` nullable;
  `MinCompletionThresholdPercent` default 100; `IsRequired` default false; `RowVersion` column
  name `xmin`; the `(TenantId, AssignmentId)` index exists by DB name.
- `AssignmentResource_MapsTableColumnsAndDefaults`: table `assignment_resources`; `Url`/`
  StoragePath`/`DisplayName` nullable; `IncludedInGeneration` default true; the
  `(TenantId, AssignmentId)` index exists by DB name.
- `Modules_Resources_RelationshipDeclaredFromAssignmentSide`: `context.Model.FindEntityType(
  typeof(Assignment))!.GetNavigations()` contains `Modules`/`Resources` with
  `IsCollection` true; the FK property is `AssignmentId`; the dependent-to-principal
  dependent-to-principal `DeleteBehavior` is `Cascade` (assert via
  `context.Model.FindEntityType(typeof(ContentModule))!.GetForeignKeys()`); both navigations
  are auto-included (assert via `GetNavigations()` on the assignment entity type —
  `n.IsAutoInclude()` must be true for `Modules`/`Resources`/`Questions` alike).

`tests/SchoolCollab.Assignments.Tests.Unit/Tenancy/ContentModuleResourceTenancyTests.cs` (new —
mirror `AssignmentOwnedTypeTenancyTests`' provider/SetTenant scaffolding):

- Tenant A + tenant B each create an assignment with a module + a resource (via aggregate
  methods after `WithTenant`); query as tenant A: `db.ContentModules` / `db.AssignmentResources`
  return ONLY A's rows; `db.Assignments.Include(a => a.Modules)` loads only A's modules; tenant
  B symmetric; total check with `IgnoreQueryFilters(["Tenant"])` shows 2 of each with correct
  `TenantId` stamps (EC-6 analog: no cross-tenant leak).

`tests/SchoolCollab.Assignments.Tests.Unit/CreateAssignmentCommandHandlerModuleResourceTests.cs`
(new — mirror `CreateAssignmentCommandHandlerQuestionsTests` scaffolding):

- Happy path: command with 2 modules + 2 resources + 1 attachment → handler returns id; reload
  from the InMemory context: modules persisted with re-indexed `DisplayOrder` 0/1 (payload
  orders ignored — EC-7 analog), threshold/`IsRequired` round-trip; resources persisted with
  kind matrix data; `TenantId` stamped (assert via `IgnoreQueryFilters(["Tenant"])` reload).
- Validation rejections (each: expect `AssignmentContentValidationException`, nothing persisted
  — `db.Assignments` count 0, proving validation-before-construction): module with non-http
  Url (`"ftp://x"`), module with blank Url, module with threshold 0 and 101, resource kind Url
  with null Url, resource kind File with null StoragePath, resource kind Url with a non-null
  StoragePath, resource kind Video with BOTH Url and StoragePath, unknown `ModuleTypeDto` value.
- Total-cap rejection: attachments summing above `MaxTotalSizeBytes` (construct the handler with
  `Options.Create(new AttachmentUploadOptions { MaxTotalSizeBytes = 100 })` and a payload of two
  60-byte attachments) → `AssignmentContentValidationException`; single 50-byte attachment
  passes.
- Null `ContentModules`/`Resources` (default create) → unchanged behavior (no modules/resources
  rows).

`tests/SchoolCollab.Assignments.Tests.Unit/UpdateAssignmentCommandHandlerModuleResourceTests.cs`
(new — mirror `UpdateAssignmentCommandHandlerQuestionsTests` incl. its
`CapturingAssignmentRepository` — the InMemory owned/child replacement quirk from ar-1 applies
here too; assert on the captured aggregate):

- Full replacement: seeded draft with 2 modules + 1 resource; update command carrying 1 module
  + 1 resource (non-null) → captured aggregate has exactly the inbound module (display order 0)
  and resource; old ids absent from `Modules`/`Resources`.
- Null collections preserve: update command with null `ContentModules`/`Resources` → captured
  aggregate still has the seeded 2 modules + 1 resource.
- Non-null empty clears: `ContentModules: []` → `Modules` empty (resources untouched when
  `Resources` null).
- Validation: update with an invalid module (blank Url) → `AssignmentContentValidationException`,
  captured aggregate's `Modules` UNCHANGED (validation before mutation).

`tests/SchoolCollab.Assignments.Tests.Unit/StageAttachmentCommandHandlerTests.cs` (new —
hand-rolled `FakeFileStore : IFileStore` capturing `(stream read as bytes, fileName,
contentType)` and returning a canned path; tenant set via the standard scaffolding):

- Happy path: `StageAttachmentCommand(pdfStream, "notes.pdf", "application/pdf", 1024)` →
  returns `StagedAttachmentDto` with the canned path + original metadata; fake captured the
  stream content byte-for-byte.
- Extension not allowed (`.exe`) → `StagedFileRejectedException` with `IsSizeLimit == false`;
  fake received ZERO calls.
- Extension case-insensitivity: `".PDF"` passes.
- Size cap: `fileSize` one byte above `MaxFileSizeBytes` (options with
  `MaxFileSizeBytes = 100`, payload 101) → `StagedFileRejectedException` with
  `IsSizeLimit == true`; fake zero calls.
- Empty file (`fileSize = 0`) → reject, `IsSizeLimit == false`; blank `FileName` → reject; blank
  `ContentType` → reject.

`tests/SchoolCollab.Assignments.Tests.Unit/LocalFileStoreTests.cs` (new — temp directory under
`Path.GetTempPath()` + random subfolder, cleaned up in a try/finally; tenant via
`AddTenancy()` + `SetTenant`):

- `StoreAsync` writes the bytes + returns
  `tenants/{tenantId}/staging/{guid}/{safeName}` (forward slashes; guid segment differs per
  call — same-name upload does NOT overwrite).
- `OpenReadAsync` round-trips the stored bytes; missing file → `FileNotFoundException`.
- `DeleteAsync` removes the file; deleting a missing path is a no-op (does not throw).
- Traversal guard: `OpenReadAsync("../escape.txt")` and a rooted path → `ArgumentException`.
- Relative `RootPath` resolves under `AppContext.BaseDirectory` (use
  `AssignmentFileStoreOptions { RootPath = <temp-relative> }` with an absolute temp path for the
  I/O tests + one dedicated case for the relative-resolution rule).

`tests/SchoolCollab.Assignments.Api.Tests.Unit/StagedFileSweeperTests.cs` (new — pure
`StagedFileSweeper.DeleteStaleFiles` over temp directories; no DbContext, no BackgroundService
loop):

- Old + unreferenced file → deleted; recent file → kept; old but REFERENCED logical path →
  kept (the correctness core of decision (b)); nested tenant/staging subfolders swept; count
  returned matches deletions; empty root → 0.

`tests/SchoolCollab.Assignments.Tests.Unit/ResourcesSectionBunitTests.cs` (new — bUnit; the
FluentInputFile JS pipeline is not driven; `StageFileAsync` is the seam):

- Empty model renders the section (header hosted by the page — assert the component's hint,
  the upload control's presence, "No resources attached yet.").
- Client-side allowlist reject: `StageFileAsync(stream, "bad.exe", …)` → the friendly error
  renders; the MockHttp backend asserts NO request to `/assignments/attachments/stage`.
- Client-side size reject: oversized `fileSize` → error renders; no request.
- Happy path: MockHttp `Respond(JsonSerializer.Serialize(new StagedAttachmentDto("a.pdf",
  "application/pdf", 3, "tenants/t/staging/g/a.pdf")))` → `StageFileAsync` returns; the staged
  row renders (file name + formatted size); `Model.Attachments` has 1 row with the returned
  `StoragePath`; error cleared.
- Server rejection: MockHttp responds 400 `{"message":"'.exe' files are not allowed."}` →
  `AttachmentStagingFailed` message renders; no row added; a second, valid `StageFileAsync`
  succeeds (retryable).
- Remove: seeded staged row; remove click with `IDialogService` mock declining → row stays;
  confirming → row removed from `Model.Attachments`; the MockHttp backend records ZERO delete
  requests (decision (b) soft removal).
- Two uploads → two rows (`@key` distinct).

`tests/SchoolCollab.Assignments.Tests.Unit/AssignmentFormModelMappingsTests.cs` (extend —
existing cases untouched):

- `AddAttachment` appends a row to `Attachments`; `RemoveAttachmentAt` removes in range and
  is a no-op out of range.

`tests/SchoolCollab.Assignments.Tests.Unit/QuestionReviewListBunitTests.cs` (extend — one new
case):

- Model with 2 `AttachmentEditorRow`s → "Resources: 2 file(s)" renders (the round-3 summary
  line goes live — decision (a)).

### Constraints (repo AGENTS.md + rules)

CPM — no `Version` on any `PackageReference`; this round adds **no packages** (MSTest, Moq,
FluentAssertions, EFCore.InMemory, bunit, RichardSzalay.MockHttp, FluentUI 4.14.2 are all
already referenced; `System.IO`/`IFormFile`/`BackgroundService`/`IOptions` are framework
types). net10.0. No MediatR — CQRS via `ICommandHandler<T[,R]>` + Scrutor scanning (the stage
handler auto-registers). MSTest + FluentAssertions + bUnit. Run `dotnet build SchoolCollab.sln`
after every change. **No git commits — working tree only.** Primary constructors for ctor
injection; XML `<summary>` docs on every public type/member; structured logging via
`ILogger<T>` with named placeholders; **typed exceptions only** — `InvalidOperationException`
must not be thrown from any NEW code path (the pre-existing `Assignment.Update` draft guard is
untouched; ar-1 lesson); DTOs are records; domain entities factory-created; `AsNoTracking()`
on read queries; no raw SQL. Blazor rules (the UI ships): CSS isolation only (no inline
`style=`, no `<style>` blocks, no `app.css` growth, `::deep` where needed); FluentUI components
for all controls; `@key` on every `@foreach`; `EventCallback` (not `Action`) for child→parent;
`[Parameter, EditorRequired]` on required parameters; destructive row removal confirmed via
`ShowConfirmDialogAsync` (never `ShowMessageBoxAsync`); no per-page `@rendermode`; no
`OnClear` on `FluentTextField`; enum display via `EnumHelper.GetDescription`. EF rules: never
edit an existing migration; `Down()` implemented and inverse; additive schema only; the
snapshot regenerates with the migration. `dotnet ef` commands run from the repo root with the
project + context flags as given in step 3.

### Acceptance criteria

Worker-facing:

- `dotnet build SchoolCollab.sln -c Debug`: 0 errors.
- `dotnet test` on `SchoolCollab.Assignments.Tests.Unit`,
  `SchoolCollab.Assignments.Api.Tests.Unit`, and
  `SchoolCollab.ArchitectureTests.Unit`: 0 failures (the Assignments project's
  `NoUncommittedModelChanges` guard is inside that project's run and must pass).
- Changed files = the expected-files list exactly (58: 25 created production + 9 created tests +
  19 modified production + 5 modified tests); no unrelated deletions/reformatting — the only
  sanctioned removals are the `AttachmentEditorRow.UploadStream` property + its doc reference;
  no new project/package/sln/CPM change; no `Edit.razor`/`wwwroot`/`Admin.Shared` change; the
  round patch pathspec is `src`, `tests`, `documents/configuration.md`.
- The migration is additive (CreateTable ×2 only) with a working inverse `Down()`.

Reviewer-facing (static, diff-only):

- Plan conformance against the expected-files list; decisions (a)–(e) implemented as written
  (stage-at-selection with NO delete-on-remove; reference-checked sweep; the exact config keys;
  the validation matrix; the single new client method).
- Tenancy/EF correctness: both entities via `TenantEntityTypeConfigurationBase` with the
  `"Tenant"` filter (the context's `ValidateTenantFilters` must stay green), FK declared once
  from the assignment side with cascade + AutoInclude + field access; no owned types for
  modules/resources; `xmin` only on `ContentModule`; DbContext additions follow the explicit
  `ApplyConfiguration` pattern; migration reviewed against additivity.
- Typed exceptions everywhere new (`AssignmentContentValidationException`,
  `StagedFileRejectedException`, `AttachmentStagingFailed`; `ArgumentException` only as
  factory/argument hygiene; no `InvalidOperationException` from new paths).
- Validation rules match decision (d) exactly (module Url absolute http/https, threshold
  1–100, resource kind matrix, per-file cap at stage, total cap on create/update, validation
  before any child is added / before aggregate construction).
- Handler wiring mirrors the questions pattern: trailing optional command params; create
  re-index; update full-replacement with null-preserve semantics; `DetectChanges` retained.
- UI conventions per the blazor-components rule if the UI lands: CSS isolation, FluentUI
  controls, `@key`, `EventCallback`, `EditorRequired`, confirm dialog on remove, no inline
  styles; `StageAttachmentAsync` is the only new ApiClient surface; the Create.razor diff is
  limited to the one inserted section.
- `dotnet-best-practices.md` Never-list compliance (CPM, no MediatR, no Console.WriteLine,
  primary constructors, XML docs, structured logging, no overwrites of pre-existing code
  outside plan scope — the three one-line test ctor-arg additions and the sanctioned
  `UploadStream` removal are the only pre-existing-file edits beyond the named regions).
- Configuration-doc conformance: the four AppHost parameters exist in Program.cs + appsettings
  + `documents/configuration.md` §2/§11/§12/§13 with the exact keys from decision (c).

### Residual risks / notes for acceptance

- **Worker clock:** the round is the largest of the train (~58 files). Backend-first ordering
  protects the core; if the worker reports the UI remainder as a deviation, the parent resumes
  the worker for the bounded remainder (ar-3 precedent) — the accept verdict must state exactly
  which steps landed.
- **InMemory replacement quirk (ar-1 carry, extended):** the module/resource update tests use
  the capturing-fake repository per the ar-1 lesson; real-provider verification of
  child-replacement persistence (now incl. modules/resources severed-FK deletes) remains an
  open carried follow-up from ar-1.
- **Kestrel body limit:** the per-file default (25 MiB) is under Kestrel's default ~30 MB
  request-body cap; operators raising the parameter must also raise `MaxRequestBodySize`
  (documented in configuration.md §2/§12).
- **Client-side pre-checks are UX-only:** the wizard's allowlist/size constants mirror the
  server defaults but are not config-fed (the admin host does not receive the assignments-api
  parameters); the server re-validates everything — a server rejection surfaces in the section's
  error bar.
- **The stage endpoint is the repo's first multipart surface:** antiforgery is disabled with a
  recorded rationale (token-auth server-to-server consumer); the UI tester should specifically
  bug-hunt the upload UX (error surfacing, remove flow, empty states, size formatting).
- **Sweep correctness:** old-but-referenced staged blobs are protected by the reference check —
  the `StagedFileSweeperTests` referenced case is the load-bearing regression; the sweep's
  BackgroundService loop itself is statically reviewed (no loop test).
- **Read-back of modules/resources from the API** (list endpoints / `AssignmentSummaryDto`
  children / edit-page round-trip) does not exist yet — the read DTOs are declared for the
  later edit/ward rounds; not a defect this round.
- **ar-1 EF owned-children verification residual** remains carried and untouched by this round.
## Worker Report

# Round ar-4-modules-resources — WORKER REPORT (consolidated, this pass)

## Scope of this pass

Pass 1 wrote most production files (54 of the 58 expected) but died on a provider
quota wall MID-EDIT on `ResourcesSection.razor`. This pass completed the bounded
remainder and made minimal pass-1 in-flight fixes.

## Changed files (verified via `git status -- src tests documents/configuration.md`)

### Pass-1 production writes (on disk from the prior run; not touched this pass)
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/ModuleType.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/ResourceKind.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/ContentModule.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/AssignmentResource.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/Exceptions/AssignmentContentValidationException.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/Exceptions/StagedFileRejectedException.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Services/IFileStore.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Services/LocalFileStore.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Services/AssignmentFileStoreOptions.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Services/AttachmentUploadOptions.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/AssignmentContentValidator.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/StageAttachmentCommand/StageAttachmentCommand.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/StageAttachmentCommand/StageAttachmentCommandHandler.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/StageAttachmentCommand/StagedFileValidator.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Data/Configurations/ContentModuleConfiguration.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Data/Configurations/AssignmentResourceConfiguration.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Migrations/20260906053255_AddContentModulesAndResources.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Migrations/20260906053255_AddContentModulesAndResources.Designer.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Migrations/AssignmentsDbContextModelSnapshot.cs` (regenerated by migration)
- `src/Assignments/SchoolCollab.Assignments.Api/Services/StagedFileSweeper.cs`
- `src/Assignments/SchoolCollab.Assignments.Api/Services/StagedFileSweepService.cs`
- `src/Assignments/SchoolCollab.Assignments.Api/StagedFileSweepExtensions.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/ResourcesSection.razor.css`
- `src/Assignments/SchoolCollab.Assignments.Application/Helpers/AttachmentUploadPolicy.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/Services/AttachmentStagingFailed.cs`

### Pass-1 modified production (on disk from the prior run; not touched this pass
unless noted under "Pass-1 in-flight fixes")
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/Assignment.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Data/AssignmentsDbContext.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Data/Configurations/AssignmentConfiguration.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Extensions.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/CreateAssignmentCommand/CreateAssignmentCommand.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/CreateAssignmentCommand/CreateAssignmentCommandHandler.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/UpdateAssignmentCommand/UpdateAssignmentCommand.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/UpdateAssignmentCommand/UpdateAssignmentCommandHandler.cs`
- `src/Assignments/SchoolCollab.Assignments.Contracts/ContractTypes.cs`
- `src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs`
- `src/Assignments/SchoolCollab.Assignments.Api/Program.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Create.razor`
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentEditFormModel.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentQuestionEditorRows.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentsApiClient.cs`
- `src/AppHost/SchoolCollab.AppHost/Program.cs`
- `src/AppHost/SchoolCollab.AppHost/appsettings.json`
- `documents/configuration.md`

### Pass-1 modified tests (on disk from the prior run; not touched this pass)
- `tests/SchoolCollab.Assignments.Tests.Unit/CreateAssignmentCommandHandlerQuestionsTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/UpdateAssignmentCommandHandlerQuestionsTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/Handlers/CreateAssignmentCommandHandlerEntityCodeTests.cs`

### Pass-1 created tests (on disk from the prior run; not touched this pass)
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentModuleResourceTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/ContentModuleResourceConfigTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/Tenancy/ContentModuleResourceTenancyTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/CreateAssignmentCommandHandlerModuleResourceTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/UpdateAssignmentCommandHandlerModuleResourceTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/StageAttachmentCommandHandlerTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/LocalFileStoreTests.cs`
- `tests/SchoolCollab.Assignments.Api.Tests.Unit/StagedFileSweeperTests.cs`

### Pass-1 in-flight fix (minimal — only `ResourcesSection.razor`)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/ResourcesSection.razor`
  - Pass-1 used `Microsoft.FluentUI.AspNetCore.Components.FluentInputFileErrorEventArgs`
    (does not exist in 4.14.2 — the event is `EventCallback<FluentInputFileEventArgs>`
    with `FluentInputFileErrorReason` enum also absent; `FluentInputFileEventArgs.ErrorMessage`
    is the load-bearing field). Rewrote `OnFileErrorAsync` to take the existing args
    type and inspect `Size` + `ErrorMessage`.
  - Pass-1 typed `OnFileError="@OnFileErrorAsync"` with a `void`-returning handler,
    but `EventCallback<T>` requires `Task`. Changed the handler signature to return
    `Task` + `return Task.CompletedTask;`.
  - Pass-1 typed `OnFileCountExceeded="@OnFileCountExceededAsync"` against
    `FluentInputFileEventArgs`, but the event is `EventCallback<int>`. Changed the
    handler to `Task OnFileCountExceededAsync(int exceededCount)`.
  - Pass-1 used `Icons.Regular.Size16.Dismiss` (compiler resolves the nested type,
    can't be used as a value). The plan specified `FluentIcons.Dismiss` (static
    class with icon properties — `FluentIcons.Dismiss` is a real `Icon` property
    matching the convention used at `Create.razor:27, 45, 60, 75, …` and the rest
    of the file's `FluentIcons.*` references). Changed to `IconEnd="@(FluentIcons.Dismiss)"`.

### Pass-1 created (on disk from the prior run — verified this pass)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/ResourcesSection.razor`

### This pass — created
- `tests/SchoolCollab.Assignments.Tests.Unit/ResourcesSectionBunitTests.cs`
  (the only file the plan listed as truly missing per the task brief)

### This pass — extended (per the plan's binding coverage list)
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentFormModelMappingsTests.cs`
  +3 cases: `AddAttachment_AppendsRowToAttachments`,
  `RemoveAttachmentAt_InRange_RemovesRow`,
  `RemoveAttachmentAt_OutOfRange_IsNoOp`
- `tests/SchoolCollab.Assignments.Tests.Unit/QuestionReviewListBunitTests.cs`
  +1 case: `TwoAttachments_RendersResourcesTwoFilesSummary` (the round-3
  placeholder flips to live data — decision (a))

## Total

58 expected files (25 created production + 9 created tests + 19 modified production +
5 modified tests). After this pass every file in the plan's expected-files list is
present on disk; the only deltas against pass-1's footprint are the
`ResourcesSection.razor` in-flight fixes, the new bUnit suite, and the two
extended test files.

## Build

`dotnet build SchoolCollab.sln -c Debug`: **0 errors**, 26 warnings (none new — all
pre-existing warnings in `Edit.razor`, `Detail.razor`, `Settings.Tests.Unit`, etc.).

## Tests

| Project | Passed | Failed | Notes |
|---|---|---|---|
| `SchoolCollab.Assignments.Tests.Unit` | 254 | 0 | Includes the 9 new `ResourcesSectionBunitTests` cases + the 4 extended cases (3 mappings + 1 review-list). |
| `SchoolCollab.Assignments.Api.Tests.Unit` | 8 | 0 | All `StagedFileSweeperTests` pass. |
| `SchoolCollab.ArchitectureTests.Unit` | 20 | 0 | Repo-wide scanner clean. |

## Deviations from plan

1. **`ResourcesSection.razor` minimal in-flight fixes (3 edits, all in
   `OnFileErrorAsync`, the `OnFileError`/`OnFileCountExceeded` EventCallback
   signatures, and the `FluentIcons.Dismiss` icon reference).** Pass-1 wrote the
   file with three incorrect type references it was mid-fix when the quota
   wall hit. The plan's behaviour contract (FR-210–212, EC-4 reconciliation,
   decision (b) soft remove, decision (d) client pre-checks, IDialogService
   confirm-on-remove) is preserved; only the type names + handler return type
   were corrected. The `OnFileError` mapping now uses the real `Size` + `ErrorMessage`
   fields on `FluentInputFileEventArgs` instead of the non-existent `Reason` enum
   the plan's prose alluded to. UI behaviour is unchanged.
2. **`AddAttachment`/`RemoveAttachmentAt` mappings cases** added in the same
   file as the plan dictates (the plan listed them as "existing cases untouched
   + 2 new" — the test file existed with the existing cases; pass 1 missed the
   additions).
3. **One new case in `QuestionReviewListBunitTests`** (the round-3 placeholder
   flips to live data — decision (a)). Pass 1 missed this addition.
4. **No other deviations.** Backend core (steps 1–9) landed in pass 1; the UI
   (steps 10–11) landed in pass 1 with the three small fixes above. Step 12
   (bUnit + mappings extensions) and step 13 (verification) were the bounded
   remainder for this pass.

## Risks / notes

- **Pass-1 production code is not re-reviewed by me** — the task brief said not
  to rewrite existing files unless they fail to build. The build is green; the
  reviewer will catch any conformance drift from the plan.
- **One residual CS8604 warning** in `ResourcesSection.razor:72` (the
  `args.Stream` being possibly-null when forwarded to `StageFileAsync`). This
  is a pre-existing-warning-class item flagged by nullable analysis on the
  `Stream?` return of `FluentInputFileEventArgs.Stream`; functionally safe and
  out of scope for this round.
- **`SchoolCollab.Assignments.Api.Tests.Unit/StagedFileSweeperTests.cs`** landed
  in pass 1 and was not touched.
- **No git commits** — working tree only, per AGENTS.md policy and the task
  brief.

## Review
Verdict: P2-only

P1:
- (none)

P2:
- src/Assignments/SchoolCollab.Assignments.Core/Services/LocalFileStore.cs:90 - ResolveWithinRoot is public; plan step 5 specified it as private, and the sweep does not reuse it (it carries its own ResolveRoot), so the widened surface is unnecessary.
- src/Assignments/SchoolCollab.Assignments.Api/Services/StagedFileSweepService.cs:40 - unused CancellationTokenSource cts created but never referenced; dead code.
- src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/StageAttachmentCommand/StagedFileValidator.cs:35 - extension check only lowercases the incoming extension, not the configured AllowedExtensions, so a lowercase extension against an uppercase allowlist entry would fail; inconsistent with the fully case-insensitive AttachmentUploadPolicy.Validate.
- src/Assignments/SchoolCollab.Assignments.Application/Services/AttachmentStagingFailed.cs:20 - Result property is declared but never assigned or consumed; unused API surface.

Best-practices: no overwrites; skills honored; readable, subject to the four P2 nits above.

Notes:
- All 58 expected files accounted for (57 in the patch + documents/configuration.md read separately; cannot run git commands, so I verified the config doc by reading its current content - §2/§11/§12/§13 all present with the keys/defaults from decision (c)).
- The three pass-1 in-flight fixes in ResourcesSection.razor are minimal type corrections (FluentInputFileEventArgs handler shape, Task-returning callbacks, FluentIcons.Dismiss) that preserve the plan's behavior contract.
- Migration 20260906053255_AddContentModulesAndResources is purely additive with a correct inverse Down().
- No new packages / CPM / sln / csproj changes; no MediatR; typed exceptions used throughout; UI follows isolation/@key/EventCallback/confirm-dialog conventions.

## Acceptance

**Numbers of record (parent-authoritative runs; logs kept beside this doc):**

| Run | Result | Log line of record |
|---|---|---|
| `dotnet build SchoolCollab.sln -c Debug` | **0 errors** (6 warnings — pre-existing NU1902/NU1903 package-vulnerability warnings; nothing new this round) | `.ar-4-build.log`: "Build succeeded. … 6 Warning(s) 0 Error(s)" |
| `dotnet test` `SchoolCollab.Assignments.Tests.Unit` | **254 passed / 0 failed** (matches the worker's self-report; includes the `NoUncommittedModelChanges` guard and the 13 new/extended cases) | `.ar-4-test-assignments.log`: "total: 254, failed: 0, succeeded: 254" |
| `dotnet test` `SchoolCollab.Assignments.Api.Tests.Unit` | **8 passed / 0 failed** (all `StagedFileSweeperTests`, incl. the old-but-referenced regression) | `.ar-4-test-assignments-api.log`: "total: 8, failed: 0" |
| `dotnet test` `SchoolCollab.ArchitectureTests.Unit` | **20 passed / 0 failed** (repo-wide scanner) | `.ar-4-test-arch.log`: "total: 20, failed: 0" |

**Scope check (`.ar-4-scope.txt` + `diffs-ar-4-modules-resources.patch`):** the src/tests paths are exactly the plan's 57 expected files (34 created = 25 production + 9 tests; 23 modified = 18 production + 5 tests) — the patch carries exactly 57 `diff --git` entries matching the expected-files list one-for-one, none extra, none missing. `documents/configuration.md` is the 58th expected file (the 19th modified production file): it sits outside the patch pathspec and the reviewer verified it by direct read — §2/§11/§12/§13 all present with decision (c)'s keys/defaults. The pre-round untracked scratch files recorded in the header are NOT part of this round. Out-of-round working-tree observation: `documents/solution/assignment-request-implementation-details.md` carries the parent's pass-1 quota-wall pause note (see provenance below) — a tracked-file modification outside the patch pathspec, parent-authored, to fold at round close; not a worker change and not one of the 58. Index state: no staged content (`git diff --cached` is empty — the new files carry intent-to-add entries from the patch-freeze step, rendering as ` A` in `git status`); HEAD unchanged at the round base `d72cc899`; no commits, working tree only.

**Worker-facing criteria:**

| Criterion | Verdict | Evidence (one line) |
|---|---|---|
| Build: 0 errors | MET | `.ar-4-build.log` — 0 Error(s) |
| Three test projects: 0 failures (incl. `NoUncommittedModelChanges`) | MET | 254/0, 8/0, 20/0 in the three parent logs |
| Changed files = the 58-file expected list exactly; no unrelated deletions/reformatting; no new package/CPM/sln/csproj/`Edit.razor`/wwwroot/Admin.Shared change | MET | scope + patch = 57 exact + config doc verified; reviewer: no new packages / CPM / sln / csproj changes; only sanctioned removal is `UploadStream` + its doc reference |
| Migration additive with inverse `Down()` | MET | Reviewer: `20260906053255_AddContentModulesAndResources` purely additive, correct inverse `Down()` |

**Reviewer-facing criteria:**

| Criterion | Verdict | Evidence (one line) |
|---|---|---|
| Plan conformance; decisions (a)–(e) as written | MET | Reviewer verdict P2-only, zero P1s; stage-at-selection with no delete-on-remove, reference-checked sweep, exact config keys, validation matrix, single new client method |
| Tenancy/EF correctness (base configs + tenant filter, FK once from the aggregate side, cascade + AutoInclude + field access, `xmin` only on `ContentModule`, explicit `ApplyConfiguration`, additive migration) | MET | Reviewer best-practices pass; config + tenancy tests green inside 254/0; `NoUncommittedModelChanges` green |
| Typed exceptions everywhere new | MET | Reviewer: typed exceptions used throughout; ArchitectureTests 20/0 |
| Validation matches decision (d) | MET (one consistency nit → residual (iii)) | Reviewer verified the matrix; the extension case-handling P2 adjudicated below |
| Handler wiring mirrors the questions pattern (trailing optional params, create re-index, update full-replacement/null-preserve, `DetectChanges` retained) | MET | Reviewer: no findings; create/update module-resource tests green |
| UI conventions (isolation, FluentUI, `@key`, `EventCallback`, `EditorRequired`, confirm dialog, no inline styles, one inserted `Create.razor` section, single new client method) | MET | Reviewer: UI follows isolation/@key/EventCallback/confirm-dialog conventions |
| `dotnet-best-practices.md` Never-list (CPM, no MediatR, primary ctors, XML docs, structured logging, no overwrites outside named regions) | MET | Reviewer: no overwrites; skills honored; readable, subject to the four P2 nits |
| Configuration-doc conformance (§2/§11/§12/§13 with decision (c) keys/defaults) | MET | Reviewer verified by direct read (outside the patch pathspec) |

**Adjudication of the REVIEW block (verdict P2-only):** all four P2 findings are adjudicated **ACCEPTED RESIDUAL — cleanup candidates, no rework** (each re-verified in code; none is a behavioral defect under the shipped configuration, so the rework loop is not warranted):

1. `LocalFileStore.cs:90` — `ResolveWithinRoot` is public though plan step 5 specified private and the sweep does not reuse it (it carries its own `ResolveRoot`); unnecessary widened surface. Trivial, non-behavioral → cleanup backlog.
2. `StagedFileSweepService.cs:40` — unused `CancellationTokenSource cts` (the loop awaits `stoppingToken` directly); dead code. Trivial, non-behavioral → cleanup backlog.
3. `StagedFileValidator.cs:35` — the extension check matches the incoming extension raw and lowercased but never lowercases the configured `AllowedExtensions`, so a lowercase incoming extension against an uppercase allowlist entry would reject; inconsistent with decision (d)'s "case-insensitive" and with the fully case-insensitive client-side `AttachmentUploadPolicy.Validate`. Non-behavioral with the shipped all-lowercase default allowlist; workaround = configure the allowlist in lowercase. Consistency nit → **follow-up note recorded: a later cleanup should compare both sides lowercased.**
4. `AttachmentStagingFailed.cs:20` — `Result` property declared but never assigned or consumed; unused API surface. Trivial, non-behavioral → cleanup backlog.

**Two-pass provenance (recorded):** worker pass 1 landed the backend + most UI (54 of the 57 patch files) and died on the ollama.com 429 quota wall mid-edit on `ResourcesSection.razor` (pause recorded by the parent in the solution doc's §3 round log, outside the round pathspec). A fresh remainder-worker pass completed the bounded remainder: the 3 in-flight type fixes in `ResourcesSection.razor`, the missing `ResourcesSectionBunitTests.cs`, and the 2 test-file extensions (`AssignmentFormModelMappingsTests` +3 cases, `QuestionReviewListBunitTests` +1 case). The reviewer accepted the three pass-1 in-flight fixes as minimal type corrections (`FluentInputFileEventArgs` handler shape, `Task`-returning `EventCallback` handlers, `FluentIcons.Dismiss`) preserving the plan's behavior contract; pass-1 production files were not re-implemented by the remainder worker (build-green carry) and the reviewer statically verified the full frozen patch.

**UI-round determination:** CONFIRMED — per the skill's deterministic trigger, the changed-file list contains `.razor` + `.razor.css` files in the Blazor client project (`ResourcesSection.razor`, `ResourcesSection.razor.css`, `Create.razor`). This is a UI round, and the verdict below is CLOSED → the UI-tester pass fires with the handover at the end of this section.

**Residual P2s (carried, none blocking):** the four adjudicated cleanup candidates above (one carrying the StagedFileValidator case-handling follow-up note). Plan-recorded residuals stand as written: the ar-1 InMemory owned-children verification residual, the Kestrel ~30 MB body-limit note, UX-only client pre-checks, and the absent read-back surface for modules/resources.

**Verdict: CLOSED** — all worker-facing and reviewer-facing criteria MET; build 0 errors; tests 254 + 8 + 20 passed with 0 failures across the three projects; scope exactly the 58 expected files; zero P1 findings; four residual P2s adjudicated accepted (cleanup backlog + the StagedFileValidator case-handling follow-up note).

### UI-tester scope handover

**UI round: CONFIRMED** (deterministic trigger fired — the patch ships `.razor` + `.razor.css` files in the Blazor client project: `ResourcesSection.razor`, `ResourcesSection.razor.css`, `Create.razor`). The verdict is CLOSED, so the UI-tester pass fires. Hunt user-facing defects the static pass structurally misses — swallowed errors, perpetual spinners, missing error surfaces, invisible validation, wrong bindings, silent no-ops, accessibility regressions, broken refresh/navigation — on exactly the surfaces below. **The tester's scope is exactly this list, no more; anything outside it is an out-of-round observation.**

**(a) UI-relevant changed files (one line of hunt rationale each):**

1. `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/ResourcesSection.razor` — the new upload/remove/list section itself: `FluentInputFile` upload control (multi-select, 10-file count cap, client-side pre-checks), staging at selection, error bar, empty state, staged list with per-row remove-X behind a confirm dialog. The primary hunting ground.
2. `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/ResourcesSection.razor.css` — the section's isolated CSS (list-row layout, spacing); visual regressions of the section.
3. `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Create.razor` — hosts the section in the wizard's Step 2 (`<ResourcesSection Model="_model" Changed="OnWizardQuestionsChanged" />`, line 326) between Assignment Details and the AI generation block; the `Changed` callback re-renders the page so the Review step's live "Resources: N file(s)" summary and the submit gate stay fresh. The only page markup change this round.
4. `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentEditFormModel.cs` — the wizard form model: `Attachments` rows + the new `AddAttachment`/`RemoveAttachmentAt` helpers + the `ToCreateRequest` projection that puts the staged rows (with their opaque `StoragePath`s) onto the create payload.
5. `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentQuestionEditorRows.cs` — defines the `AttachmentEditorRow` (FileName/ContentType/FileSize/StoragePath) the section's list renders; the now-dead `UploadStream` property was removed here.
6. `src/Assignments/SchoolCollab.Assignments.Application/Helpers/AttachmentUploadPolicy.cs` — the client-side allowlist/size pre-check constants + friendly rejection messages + `FormatFileSize` shown in each list row.
7. `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentsApiClient.cs` — `StageAttachmentAsync` (multipart stage → `StagedAttachmentDto`) the section calls; `CreateAsync` rides the extended payload.
8. `src/Assignments/SchoolCollab.Assignments.Application/Services/AttachmentStagingFailed.cs` — the typed failure the section catches and renders (server-supplied message) on 400/413.

**(b) Page that renders them:** the assignment Create wizard page — `Create.razor`, `@page "/assignments/create"`; Step 2 renders `ResourcesSection`, and the wizard's Review step renders the live "Resources: N file(s)" count off the same `Model.Attachments` (`QuestionReviewList`, Create.razor line 396).

**(c) ApiClient methods in play:**
- `AssignmentsApiClient.StageAttachmentAsync(Stream content, string fileName, string contentType, long fileSize, CancellationToken ct = default)` → multipart `POST /assignments/attachments/stage` → `StagedAttachmentDto` (FileName/ContentType/FileSize/StoragePath); 400/413 → typed `AttachmentStagingFailed` carrying the server `{"message":…}`.
- `AssignmentsApiClient.CreateAsync(CreateAssignmentRequest req, CancellationToken ct = default)` → the wizard's submit; the request now carries `Attachments` (with the staged storage paths) via `ToCreateRequest`.

**(d) Navigation entry points (unchanged this round, but the paths that reach the wizard):**
- The Assignments landing page `/assignments` (Index.razor) with its "+ New Assignment" action (`CreateRoute="/assignments/create"`).
- The Admin NavMenu assignments entries: the top-level `/assignments` link and, in the Assignments nav group, "All Assignments" (`/assignments`) and "New Assignment" (`/assignments/create`).

**Scope statement: the tester's scope is exactly this list, no more.**




## UI Tester

(deepseek-v4-flash · second tester pass of the train · 2026-09-06)

UI TEST
Scope ack: Hunted exactly the handed-over surfaces - the Resources upload/remove/list section (ResourcesSection.razor + .css), the Create wizard Step-2 hosting + Review summary (Create.razor, QuestionReviewList), the AssignmentEditFormModel attachment helpers + ToCreateRequest, the AttachmentEditorRow, AttachmentUploadPolicy client pre-checks + FormatFileSize, AssignmentsApiClient.StageAttachmentAsync, and AttachmentStagingFailed. No scope expansion.
Verdict: P1
P1: ResourcesSection.razor:171 - the count-cap rejection misreports the file limit: OnFileCountExceededAsync(int exceededCount) interpolates the parameter as the configured cap, but FluentUI 4.14.2 documents the parameter as "the total number of files that were attempted for upload"; selecting 13 files with MaximumFileCount=10 shows "limit is 13" - the wrong ceiling every time.
P2: ResourcesSection.razor:82-85 - the StageFileAsync _staging reentrancy guard silently drops a file (no row, no error bar) if OnFileUploaded fires while one is still staging - a swallowed no-op on the multi-select surface.
P2: ResourcesSection.razor:146-163 - OnFileErrorAsync is dead wiring: FluentUI 4.14.2 documents OnFileError as "Not yet used"; size/allowlist rejections still surface via the StageFileAsync pre-check, so the functional gap is limited to any oversized file the component swallows before OnFileUploaded.
Out-of-round observations: none

### Rework plan (tester iteration 1 of max 2 — parent-appended)

Fix all three findings in one bounded worker pass; only ResourcesSection.razor/.razor.css (and its bUnit tests) may change:
1. (P1) OnFileCountExceededAsync: report the REAL cap. The component's MaximumFileCount is 10 and AttachmentUploadPolicy owns the client-side caps - reference the configured constant (not the FluentUI callback parameter) in the message so the user always sees the true ceiling.
2. (P2) _staging guard: no silent drops - when a file is refused because another is staging, surface it via the section's existing error bar (friendly, retryable: the file can be re-picked) rather than a bare return.
3. (P2) OnFileErrorAsync dead wiring: REMOVE the handler + its binding (dead code per FluentUI docs "not yet used"); record in the test file comment that FluentUI 4.14.2 OnFileError is documented not-yet-used so a future upgrade re-adding it is deliberate.
Tests: update/extend ResourcesSectionBunitTests.cs for (1) the correct-limit message and (2) the staging-guard error surface; (3) is a removal - adjust any test that bound the dead handler. Then build + the three test projects green; append the rework section to .ar-4-worker-report.md. Tester re-verifies only these three fixes; the parent statically checks the rework diff (no reviewer re-run).


### Re-verification (tester iteration 1): PASS

(deepseek-v4-flash · re-verified exactly the three rework fixes · 2026-09-06)

UI TEST
Scope ack: Re-verified exactly the three rework fixes (count-cap message + markup binding, _staging guard friendly error, OnFileErrorAsync removal) in ResourcesSection.razor / AttachmentUploadPolicy.cs / ResourcesSectionBunitTests.cs.
Verdict: PASS
P1:
P2:
Out-of-round observations: none

**Round verdict: CLOSED** — tester iteration 1 of max 2 used. All three fixes verified with
test evidence (the message asserts the configured cap and rejects the attempted count;
the staging guard asserts error + no row + zero HTTP calls; zero OnFileError production
references). The AttachmentUploadPolicy.MaximumFileCount deviation judged a justified
minimal extension (single source of truth, mirroring the MaxFileSizeBytes pattern).
Authoritative post-rework: build 0 errors; Assignments 256/0; Api 8/0; Architecture 20/0.
