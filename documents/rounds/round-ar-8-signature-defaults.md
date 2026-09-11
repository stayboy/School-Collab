# Round ar-8-signature-defaults — WS-C1 prerequisite: TenantAssignmentPolicy (Settings) + GradeAssignmentPolicy (Students) + ISignatureDefaultResolver + Assignment.RequiresSignature end-to-end + grade-Detail editor + wizard pre-fill

Provider: pi (models: clinepass glm-5.3 orchestrator, clinepass deepseek-v4-flash worker, clinepass kimi-k2.7-code reviewer, clinepass minimax-m3 UI tester [UI round — Create.razor, Edit.razor, GradeLevels/Detail.razor + the new GradeSignaturePolicyEditor.razor ship → the tester fires after acceptance])

- **Tier:** 3 — full four-agent round **plus a UI-tester pass** (the round touches four `.razor` surfaces). Worker model: deepseek-v4-flash (**30-minute run cap — this is a LARGE round: ~61 files across FOUR bounded contexts (Settings, Students, Assignments, Admin.Shared/Admin UI) with THREE additive migrations. The cut-line protocol WILL likely engage: land steps in order, each step leaves the tree build-coherent, self-report progress, STOP when the clock runs short — the parent resumes the bounded remainder. Never skip silently, never reverse the order).
- **Round base:** HEAD `1e4f59c8` on branch `stack/8-ar-8-signature-defaults` (created from the stack/7 tip after its round-7 commit). **Tracked tree clean at base.**
- **Pre-round untracked scratch OUT of round scope:** the 14 `.ar-*-scope`/`.ar-*-worker-report` files + `diffs-period-upsert-single-page.patch` + `round-period-upsert-single-page.md` in `documents/rounds/`. They sit outside the round patch pathspec (`src`, `tests`).
- **Rounds 1–7 landed what this round builds on** (do NOT re-plan any of it): questions/options/attachments + AiPromptOverride (ar-1); the AI generation endpoint (ar-2); the wizard question UI (ar-3); ContentModule/AssignmentResource + IFileStore + staging (ar-4); Scheduled/Approval/Archive lifecycle + flags (ar-5); PassScore/MaxAttempts + scoring (ar-6); duplicate-as-template (ar-7). Today `Assignment.Create(...)` carries the trailing `archiveGraceDays/passScore/maxAttempts` params (named-arg call style), `CreateAssignmentRequest`/`UpdateAssignmentRequest`/`AssignmentSummaryDto` carry the same trailing fields, and the entity→DTO mapping lives in `GetAssignmentByIdQueryHandler` + `ListAssignmentsQueryHandler` (the WS-A3 PassScore/MaxAttempts threading is the precedent to mirror).
- **Parent-adjudicated scope decision (Option A, binding):** the implementation-details doc §3 row 8 lists "wizard pre-fill" in ar-8, and a pre-filled checkbox the author can override is only meaningful if the value persists — so this round DOES include the minimal author-side slice: `RequiresSignature` on `Assignment` (default false, set via the create command), additive Assignments migration, `CreateAssignmentRequest` DTO field, wizard checkbox pre-filled from the resolved default and overridable, value submitted with create (and round-tripped through the update path). The later C1 sign-off round must **NOT** re-add the flag — its scope is SignOffState/SignatureEvent/sign command/locking/certificate.
- **Sources:** `documents/solution/assignment-request-implementation-details.md` §2 WS-C (THE design brief — first bullet, the C1 prerequisite paragraph) + §3 round-slicing row 8 + §4 risks; `documents/solution/assignment-request-go-forward-breakdown.md` §2 spec-§7-Q1 decision + WS-C; `documents/rounds/round-ar-7-template.md` (house format); `.github/copilot/rules/dotnet-best-practices.md`; `.github/copilot/rules/blazor-components.md`; `.github/copilot/rules/ef-migrations.md`; `.github/skills/dropdown-ui/SKILL.md` (the grade tri-state select); `.github/copilot/rules/section-card.md`.

## Plan

### Goal

Execute workstream **WS-C1 prerequisite** (implementation-details §2 WS-C first bullet; round log §3 row 8; breakdown §2 Q1): the signature-default **policy pair** + **effective resolution** + **grade-Detail editing UI** + **wizard pre-fill**, plus the minimal author-side slice that makes the pre-fill real. Concretely:

1. `TenantAssignmentPolicy` (Settings.Core) — one row per tenant, `RequiresSignatureDefault bool` (default false), full CRUD-via-upsert + `/api/settings/assignment-policy` GET/PUT + additive migration.
2. `GradeAssignmentPolicy` (Students.Core) — one row per (tenant, grade), `RequiresSignatureDefault bool?` (null = inherit tenant default), grade-scoped GET/PUT + additive migration.
3. `ISignatureDefaultResolver` (interface in Assignments.Core, HTTP impl in Assignments.Api — mirrors `INotificationPolicyResolver` exactly) + a `GET /assignments/signature-default` route + `AssignmentsApiClient.GetSignatureDefaultAsync` so the wizard can resolve the effective default.
4. `Assignment.RequiresSignature` persisted end-to-end via the create command (update path round-trips it); the create wizard pre-fills the checkbox from the resolved default (tenant default at init, grade-resolved on grade selection), the author may override, the final value is submitted with create.
5. A compact "Guardian Signature" editor card on the Students grade-Detail page (tenant default switch + grade tri-state override), plus tests across all touched contexts.

### Scope

**In (fixed — this is the whole round):** everything in the five numbered points above, the three additive migrations (Settings `tenant_assignment_policies`, Students `grade_assignment_policies`, Assignments `assignments.requires_signature`), the Admin.Shared `AssignmentPolicyApiClient` (+ its Settings.Application ModuleServices registration), and the binding test coverage list below.

**Out (record — do not touch):** `SignOffState`/`SignedAt`/`FinalizedAt` on `AssignmentSubmission`, `SignatureEvent`, the sign command, consent text, locking, certificates, delegation (ALL of that is the later C1/C2/C4 rounds — this round stores only the author-side flag); the assignments Detail.razor display of the flag (the summary DTO carries it; the C2 sign-off UI surfaces it where sign-off matters — recorded backlog); server-side resolution inside `CreateAssignmentCommandHandler` (the wizard owns pre-fill; the request value IS the snapshot — a raw API caller omitting the field gets `false`); `documents/configuration.md` (no new flags/params); `wwwroot/app.css`, `Directory.Packages.props`/`.sln`/csproj changes (no new packages); AppHost wiring (the `settings-api`/`students-api` named clients already exist in Assignments.Api Program.cs and the Admin host already reaches settings-api via `TenantPropagationDelegatingHandler`); any PublishDialog/publish-handler change (sign-off enforcement at publish is a later round); the pre-round untracked scratch files listed in the header.

### Decisions (binding — implement as written; (a)–(g) then (k) are the parent-adjudicated design)

- **(a) Settings — `TenantAssignmentPolicy` (the `TenantNotificationPolicy` precedent, mirrored exactly).** New `src/Settings/SchoolCollab.Settings.Core/Domain/TenantAssignmentPolicy.cs`: `sealed class TenantAssignmentPolicy : BaseTenantEntityWithAudit, IHasRowVersion`, private ctor, `bool RequiresSignatureDefault { get; private set; }` (non-nullable, default false), `uint RowVersion`, `static TenantAssignmentPolicy Create(Guid tenantId, bool requiresSignatureDefault = false)` and `void SetPolicy(bool requiresSignatureDefault)` (stamps `UpdatedAt`). No validation throws (a bool needs none — do NOT invent any). New `Data/Configurations/TenantAssignmentPolicyConfiguration.cs` mirroring `TenantNotificationPolicyConfiguration`: `TenantEntityTypeConfigurationBase<TenantAssignmentPolicy>`, `ToTable("tenant_assignment_policies")`, `ConfigureAuditProperties` + `ConfigureSoftDeleteProperties` + `ConfigureSoftDeleteQueryFilter` + `ConfigurePostgresRowVersion`. `SettingsDbContext` gains the `DbSet` + the `ApplyConfiguration(new TenantAssignmentPolicyConfiguration(() => CurrentTenantId))` line (explicit instantiation, no scanning). New `DTOs/TenantAssignmentPolicyDto.cs`: `sealed record TenantAssignmentPolicyDto(bool RequiresSignatureDefault)`. New CQRS folder `CQRS/AssignmentPolicies/`: `Queries/GetTenantAssignmentPolicy/GetTenantNotificationPolicy` shape — `GetTenantAssignmentPolicy : IQuery<TenantAssignmentPolicyDto?>` with `static readonly GetTenantAssignmentPolicy Instance = new();` + handler `IQueryHandler<GetTenantAssignmentPolicy, TenantNotificationPolicyDto?>`-style returning the single tenant row or null; `Commands/UpsertTenantAssignmentPolicy/UpsertTenantAssignmentPolicy(bool RequiresSignatureDefault) : ICommand` + handler `ICommandHandler<UpsertTenantAssignmentPolicy, TenantAssignmentPolicyDto>` mirroring `UpsertTenantNotificationPolicyHandler` (load existing row → `SetPolicy`, else `Create(tenantId, …)` + `Add`; return the DTO). API: new `Endpoints/AssignmentPolicyRoutes.cs` mirroring `NotificationPolicyRoutes` — `MapAssignmentPolicyRoutes(this RouteGroupBuilder group)` with `GET /assignment-policy` (null → `Results.NoContent()`, else `Results.Ok(dto)`) and `PUT /assignment-policy` (`[FromBody] UpsertAssignmentPolicyRequest(bool RequiresSignatureDefault)` → `Results.Ok(result)`); new `AssignmentPolicyEndpoints.cs` mirroring `NotificationPolicyEndpoints` (`MapAssignmentPolicyEndpoints(this WebApplication app, IFeatureFlagService featureFlags)`: `app.MapGroup("/api/settings")`, `RequireAuthorization()` unless `FeatureFlagKeys.DisableOIDCAuth`, then `group.MapAssignmentPolicyRoutes()`); `Settings.Api/Program.cs` gains `app.MapAssignmentPolicyEndpoints(featureFlags);` directly after the `MapNotificationPolicyEndpoints` call (line ~72). **Additive migration** `AddTenantAssignmentPolicy` (EF-generated; migration + Designer + snapshot edit).
- **(b) Students — `GradeAssignmentPolicy` (the `GradeNotificationPolicy` precedent, mirrored exactly).** New `src/Students/SchoolCollab.Students.Core/Domain/GradeAssignmentPolicy.cs`: `sealed class GradeAssignmentPolicy : BaseTenantEntityWithAudit, IHasRowVersion`, private ctor, `Guid GradeLevelId { get; private set; }`, `bool? RequiresSignatureDefault { get; private set; }` (**null = inherit the tenant default**), `uint RowVersion`, `static GradeAssignmentPolicy Create(Guid tenantId, Guid gradeLevelId, bool? requiresSignatureDefault = null)` and `void SetOverride(bool? requiresSignatureDefault)` (null restores inherit; stamps `UpdatedAt`). New `Data/Configurations/GradeAssignmentPolicyConfiguration.cs` mirroring `GradeNotificationPolicyConfiguration`: `ToTable("grade_assignment_policies")`, audit/soft-delete/rowversion, `GradeLevelId` required, `HasOne<GradeLevel>().WithMany().HasForeignKey(x => x.GradeLevelId).OnDelete(DeleteBehavior.Cascade)`, unique index `(TenantId, GradeLevelId)` named `ix_grade_assignment_policies_tenant_grade` with `HasFilter("is_deleted = false")`. `StudentsDbContext` gains the `DbSet` + `ApplyConfiguration` line. New `DTOs/GradeAssignmentPolicyDto.cs`: `sealed record GradeAssignmentPolicyDto(Guid GradeLevelId, bool? RequiresSignatureDefault, DateTimeOffset UpdatedAt)`. New CQRS folder `CQRS/GradeAssignmentPolicies/`: `Queries/GetGradeAssignmentPolicy/GetGradeAssignmentPolicy(Guid GradeLevelId) : IQuery<GradeAssignmentPolicyDto?>` + handler (single-or-default by grade, null when no row); `Commands/UpsertGradeAssignmentPolicy/UpsertGradeAssignmentPolicy(Guid GradeLevelId, bool? RequiresSignatureDefault) : ICommand` + handler mirroring `UpsertGradeNotificationPolicyHandler` — **grade-existence guard first** (`db.GradeLevels.AnyAsync(x => x.Id == command.GradeLevelId)` → `throw new GradeLevelNotFoundException(command.GradeLevelId)`), then upsert (`SetOverride` on existing, else `Create` + `Add`), return the DTO. API: `GradeLevelRoutes.cs` gains (directly after the notification-policy pair) `GET /grade-levels/{id:guid}/assignment-policy` (null → `Results.NoContent()`, else `Results.Ok(result)`) and `PUT /grade-levels/{id:guid}/assignment-policy` with `internal record UpsertGradeAssignmentPolicyRequest(bool? RequiresSignatureDefault)` — catches `GradeLevelNotFoundException` → `Results.NotFound()` (the notification PUT's catch set; no `ArgumentOutOfRangeException` case needed for a bool?). `StudentsApiClient` gains `GetGradeAssignmentPolicyAsync(Guid gradeLevelId, CancellationToken ct = default)` → `Task<GradeAssignmentPolicyDto?>` and `UpsertGradeAssignmentPolicyAsync(Guid gradeLevelId, bool? requiresSignatureDefault, CancellationToken ct = default)` (PUT JSON body `{ "requiresSignatureDefault": … }`; null serializes as null = inherit — the `UpsertGradeNotificationPolicyRequest` wire precedent) + the mirrored request record at the bottom of the file. **Additive migration** `AddGradeAssignmentPolicy` (migration + Designer + snapshot edit).
- **(c) Assignments — effective resolution (the `INotificationPolicyResolver` seam, mirrored exactly).** New `src/Assignments/SchoolCollab.Assignments.Core/Services/ISignatureDefaultResolver.cs`: `public interface ISignatureDefaultResolver { Task<bool> ResolveRequiresSignatureDefaultAsync(Guid? gradeLevelId, CancellationToken cancellationToken = default); }` — XML doc citing WS-C1 / spec §7 Q1 and noting the mirror of `INotificationPolicyResolver`. New `src/Assignments/SchoolCollab.Assignments.Api/Services/SignatureDefaultResolver.cs` mirroring `NotificationPolicyResolver`: primary-ctor `(IHttpClientFactory httpClientFactory, ILogger<SignatureDefaultResolver> logger)`, named clients `settings-api` + `students-api`; tenant fetch `GET /api/settings/assignment-policy` → 204/null ⇒ tenant default `false`, 200 ⇒ `dto.RequiresSignatureDefault`; grade fetch (only when `gradeLevelId.HasValue`) `GET /students/grade-levels/{id}/assignment-policy` → 204/null ⇒ inherit (`null`), 200 ⇒ `dto.RequiresSignatureDefault`; effective = `gradeOverride ?? tenantDefault`; **any `HttpRequestException` → `LogWarning` + degrade** (tenant fetch fails ⇒ `false`; grade fetch fails ⇒ inherit) — the same fail-open best-effort posture as the notification resolver. DI in `Assignments.Api/Program.cs` directly after the `INotificationPolicyResolver` registration: `builder.Services.AddScoped<SchoolCollab.Assignments.Core.Services.ISignatureDefaultResolver, SchoolCollab.Assignments.Api.Services.SignatureDefaultResolver>();` (the named clients already exist — no new `AddHttpClient`). Route in `AssignmentRoutes.cs` `MapAssignmentRoutes`: `group.MapGet("/signature-default", ...)` with `Guid? gradeLevelId` query parameter, injecting `ISignatureDefaultResolver`, returning `Results.Ok(new { requiresSignature = await resolver.ResolveRequiresSignatureDefaultAsync(gradeLevelId, ct) })` — **always 200** (fail-open false mirrors the resolver posture); place it after the `/{id:guid}` GET route (the literal segment wins over the guid template — no conflict, but keep it visually near the top with the other GETs). `AssignmentsApiClient` gains `public async Task<bool> GetSignatureDefaultAsync(Guid? gradeLevelId, CancellationToken ct = default)` → `GET /assignments/signature-default?gradeLevelId={id}` (omit the query param when null), parse `{ requiresSignature }` via a private `sealed record SignatureDefaultResponse(bool RequiresSignature);` at the bottom of the class (the round-7 `IdResponse` precedent — camel-case JSON via the existing `_jsonOptions`), plus entry/resolved structured logs.
- **(d) `Assignment.RequiresSignature` end-to-end (Option A — parent-adjudicated; the WS-A3 `PassScore`/`MaxAttempts` threading is the precedent, mirrored position-for-position).** `Assignment.cs`: `public bool RequiresSignature { get; private set; }` (XML doc: WS-C1 / spec §7 Q1 — snapshotted at create from the resolved grade/tenant default; the author may override); `Create(...)` gains **trailing** `bool requiresSignature = false` (named-arg call sites — duplicate handler and tests pass `requiresSignature: source.RequiresSignature` so the ar-7 copy carries it too); `Update(...)` gains **trailing** `bool requiresSignature = false`. `CreateAssignmentCommand` + `UpdateAssignmentCommand` gain trailing `bool RequiresSignature = false` (XML docs "Threaded to `Assignment.Create/Update`"); their handlers pass it into the domain call. `ContractTypes.cs`: `CreateAssignmentRequest`, `UpdateAssignmentRequest`, AND `AssignmentSummaryDto` each gain trailing `bool RequiresSignature = false` (XML doc citing WS-C1 / spec §7 Q1). `AssignmentRoutes.cs` POST `/` and PUT `/{id:guid}` thread `req.RequiresSignature` into the commands. **Both** entity→DTO mapping sites set it by named arg — `GetAssignmentByIdQueryHandler` (`RequiresSignature = …entity value…`) and `ListAssignmentsQueryHandler` (follow the existing `PassScore`/`MaxAttempts` flow in that file, including whatever intermediate projection carries them — omitting the field would make every summary report `false` and the edit page would silently reset the flag). **Additive migration** `AddAssignmentRequiresSignature` (nullable-free `requires_signature boolean NOT NULL DEFAULT false` on `assignments`; migration + Designer + snapshot edit). **Recorded:** the duplicate handler (ar-7) gains the one-line `requiresSignature: source.RequiresSignature` named arg — the copy is a full scalar clone. **Recorded for the parent:** the later C1 sign-off round must NOT re-add this flag; its scope starts at `SignOffState`.
- **(e) grade-Detail UI (UI round — the tester will fire).** New `src/SchoolCollab.Admin.Shared/Services/AssignmentPolicyApiClient.cs` mirroring `NotificationPolicyApiClient` line-for-line: `public sealed class AssignmentPolicyApiClient(HttpClient http)`, `GetAsync(CancellationToken ct = default)` → `Task<TenantAssignmentPolicyDto?>` (`GET /api/settings/assignment-policy`, null on 204) and `UpsertAsync(bool requiresSignatureDefault, CancellationToken ct = default)` (PUT). Its wire DTO record mirrors the Settings DTO. Registered in `src/Settings/SchoolCollab.Settings.Application/ModuleServices.cs` directly after the `NotificationPolicyApiClient` block — same `https+http://settings-api` base address **and the same `TenantPropagationDelegatingHandler`** (`TenantAssignmentPolicy` is a STRICT tenant entity; without the handler reads/writes hit the wrong tenant — the ModuleServices comment block explains exactly this for the notification client). New `src/Students/SchoolCollab.Students.Application/Components/Students/GradeSignaturePolicyEditor.razor` — a compact single-setting editor (NOT the 8-field grid; NOT a dialog): `@inject StudentsApiClient Api` + `@inject AssignmentPolicyApiClient SettingsApi`; `[Parameter] public Guid GradeLevelId { get; set; }`; loads both in parallel (`Task.WhenAll`, the `GradeNotificationPolicyEditor.LoadAsync` pattern); renders a "Guardian signature" row with (1) the **tenant-global default** as a `FluentSwitch` labeled "Required by default (tenant)" — immediate save on toggle → `SettingsApi.UpsertAsync(value)` then reload; (2) the **grade override** as a `FluentSelect` (dropdown-ui skill: wrapper binding, fixed width) with three options — *Inherit global*, *Require signature*, *No signature required* — immediate save → `Api.UpsertGradeAssignmentPolicyAsync(GradeLevelId, value)` (Inherit ⇒ `null`) then reload; an "Inherit global" `FluentBadge` display when the override is null; loading `FluentProgressRing`; per-failure `FluentMessageBar` (the `_error` idiom). **Recorded decision: inline immediate-save controls, NO dialog component** — a single boolean does not need the `NotificationPolicyFieldEditDialog` apparatus (8 fields × 2 scopes did); immediate-save avoids a new dialog + the no-nesting rule entirely; the grade-Detail `SetEnrollmentBlocked` toggle is the immediate-save precedent. Styling: existing utility classes only (NO `<style>` block, no inline `style=`, no new `.razor.css`). `GradeLevels/Detail.razor` embeds it: a new `<FluentCard class="detail-card">` "Guardian Signature" block placed directly after the "Notification & Delivery" card, `<GradeSignaturePolicyEditor GradeLevelId="@Id" />` (same `TenantGate` child-content region).
- **(f) Wizard pre-fill + Edit round-trip.** `Create.razor`: new `private bool _requiresSignature;` field; new checkbox rendered directly under the existing "Require guardian review" checkbox (step 1, ~line 201): `<FluentCheckbox @bind-Value="_requiresSignature" Label="Require guardian signature after completion" class="mt-3" />`. Pre-fill rules (binding): (1) at the end of `OnInitializedAsync` (after the grade-level/group loads, outside their try) resolve `await ResolveSignatureDefaultAsync(null)` — the tenant default; (2) in `OnGradeLevelChangedAsync`, after the subject load block, resolve with the newly selected grade id (option null ⇒ resolve `null` ⇒ back to tenant default). `ResolveSignatureDefaultAsync(Guid? gradeLevelId)` helper: `try { _requiresSignature = await Api.GetSignatureDefaultAsync(gradeLevelId, ct); } catch (OperationCanceledException) { } catch (Exception ex) { Logger.LogWarning(ex, "Signature-default pre-fill failed for grade {GradeLevelId}"; ) }` — **fail-open, NO error bar** (the pre-fill is best-effort; the checkbox stays at its current value and the author sets it manually). Audience switch does NOT re-resolve (the tenant default from init stands). **Recorded:** switching grade re-fills the checkbox and overwrites any prior author override — intended (mirrors the subject-list reset on grade change); the author re-checks before submit. `SubmitAsync` threads it: `_model.ToCreateRequest(..., _mandatoryReview, _requiresSignature)`. `AssignmentEditFormModel.cs`: `ToCreateRequest` gains a trailing `bool requiresSignature = false` parameter → `RequiresSignature: requiresSignature` in the request; new `public bool RequiresSignature { get; set; }` property + `LoadFrom` sets `RequiresSignature = assignment.RequiresSignature` (round-trip safety; the mapping test covers it). `Edit.razor`: new `private bool _requiresSignature;` field, loaded from `_item.RequiresSignature` beside the `_mandatoryReview` load (~line 175); new `FormRow` + `FluentCheckbox` (id `assignmentEditRequiresSignature`, label "Require guardian signature after completion") directly under the "Guardian review" row (~line 83); threaded into the update request beside `_mandatoryReview` (~line 242). **Recorded:** the Edit round-trip is REQUIRED — without it, editing any Draft would silently reset the flag to false via `UpdateAssignmentRequest`'s default.
- **(g) Tests** (MSTest/FluentAssertions/bUnit/MockHttp — exact names in the binding coverage list below; every new test file mirrors its named precedent's scaffolding).
- **(k) Constraints (repo rules — binding):** CPM (no `Version` on any `PackageReference`; this round adds **no packages**); net10.0; **no MediatR** — all new handlers auto-register via the Scrutor `ICommandHandler<,>`/`IQueryHandler<,>` scans (NO manual registrations); **typed exceptions only** — the ONE new throw is the mirrored `GradeLevelNotFoundException` grade guard; **no `InvalidOperationException` anywhere new**; **migrations ARE required this round** (unlike ar-7): exactly THREE additive EF migrations (Settings/Students/Assignments), each committed with its Designer + snapshot edit, and every `MigrationGuardTests.NoUncommittedModelChanges` (Assignments.Tests.Unit, Students.Tests.Unit, ArchitectureTests.Unit repo-wide) must be green **with** them; **`dotnet build SchoolCollab.sln` after every change**; primary constructors, XML `<summary>` docs citing WS-C1 / spec §7 Q1 on every new public type/member, structured logging with named placeholders; Blazor rules: FluentUI components for all controls, `@key` on every `@foreach`, **NO `<style>` blocks or inline `style=` in the new component** (existing utility classes only), CSS isolation untouched; **the worker NEVER edits `documents/rounds/round-ar-8-signature-defaults.md`** — self-reports go to `documents/rounds/.ar-8-worker-report.md` (scratch, untracked); **30-minute run cap + cut-line protocol** (see header — this round is expected to need more than one worker run; land steps in order, each build-coherent, self-report, STOP); **repo-scoped searches only — NEVER `find /`** (a prior run hung 28 minutes on an unbounded search).

### Expected files

**No other file may change.** The list below is the reviewer's conformance baseline AND the tester-scope handover basis. Migration timestamps are worker-chosen (`<ts>`); each migration = the `.cs` + `.Designer.cs` (created) + the context `ModelSnapshot.cs` edit (modified).

**Settings context** (created 11, modified 4):

- `src/Settings/SchoolCollab.Settings.Core/Domain/TenantAssignmentPolicy.cs` (C)
- `src/Settings/SchoolCollab.Settings.Core/Data/Configurations/TenantAssignmentPolicyConfiguration.cs` (C)
- `src/Settings/SchoolCollab.Settings.Core/DTOs/TenantAssignmentPolicyDto.cs` (C)
- `src/Settings/SchoolCollab.Settings.Core/CQRS/AssignmentPolicies/Queries/GetTenantAssignmentPolicy/GetTenantAssignmentPolicy.cs` (C)
- `src/Settings/SchoolCollab.Settings.Core/CQRS/AssignmentPolicies/Queries/GetTenantAssignmentPolicy/GetTenantAssignmentPolicyHandler.cs` (C)
- `src/Settings/SchoolCollab.Settings.Core/CQRS/AssignmentPolicies/Commands/UpsertTenantAssignmentPolicy/UpsertTenantAssignmentPolicy.cs` (C)
- `src/Settings/SchoolCollab.Settings.Core/CQRS/AssignmentPolicies/Commands/UpsertTenantAssignmentPolicy/UpsertTenantAssignmentPolicyHandler.cs` (C)
- `src/Settings/SchoolCollab.Settings.Core/Migrations/<ts>_AddTenantAssignmentPolicy.cs` (C)
- `src/Settings/SchoolCollab.Settings.Core/Migrations/<ts>_AddTenantAssignmentPolicy.Designer.cs` (C)
- `src/Settings/SchoolCollab.Settings.Api/Endpoints/AssignmentPolicyRoutes.cs` (C)
- `src/Settings/SchoolCollab.Settings.Api/AssignmentPolicyEndpoints.cs` (C)
- `src/Settings/SchoolCollab.Settings.Core/Data/SettingsDbContext.cs` (M — DbSet + ApplyConfiguration)
- `src/Settings/SchoolCollab.Settings.Core/Migrations/SettingsDbContextModelSnapshot.cs` (M)
- `src/Settings/SchoolCollab.Settings.Api/Program.cs` (M — one mapping call)
- `src/Settings/SchoolCollab.Settings.Application/ModuleServices.cs` (M — AssignmentPolicyApiClient registration)

**Students context + grade-Detail UI** (created 10, modified 5):

- `src/Students/SchoolCollab.Students.Core/Domain/GradeAssignmentPolicy.cs` (C)
- `src/Students/SchoolCollab.Students.Core/Data/Configurations/GradeAssignmentPolicyConfiguration.cs` (C)
- `src/Students/SchoolCollab.Students.Core/DTOs/GradeAssignmentPolicyDto.cs` (C)
- `src/Students/SchoolCollab.Students.Core/CQRS/GradeAssignmentPolicies/Queries/GetGradeAssignmentPolicy/GetGradeAssignmentPolicy.cs` (C)
- `src/Students/SchoolCollab.Students.Core/CQRS/GradeAssignmentPolicies/Queries/GetGradeAssignmentPolicy/GetGradeAssignmentPolicyHandler.cs` (C)
- `src/Students/SchoolCollab.Students.Core/CQRS/GradeAssignmentPolicies/Commands/UpsertGradeAssignmentPolicy/UpsertGradeAssignmentPolicy.cs` (C)
- `src/Students/SchoolCollab.Students.Core/CQRS/GradeAssignmentPolicies/Commands/UpsertGradeAssignmentPolicy/UpsertGradeAssignmentPolicyHandler.cs` (C)
- `src/Students/SchoolCollab.Students.Core/Migrations/<ts>_AddGradeAssignmentPolicy.cs` (C)
- `src/Students/SchoolCollab.Students.Core/Migrations/<ts>_AddGradeAssignmentPolicy.Designer.cs` (C)
- `src/Students/SchoolCollab.Students.Application/Components/Students/GradeSignaturePolicyEditor.razor` (C)
- `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs` (M — Get/Upsert methods + request record)
- `src/Students/SchoolCollab.Students.Core/Data/StudentsDbContext.cs` (M — DbSet + ApplyConfiguration)
- `src/Students/SchoolCollab.Students.Core/Migrations/StudentsDbContextModelSnapshot.cs` (M)
- `src/Students/SchoolCollab.Students.Api/Endpoints/GradeLevelRoutes.cs` (M — GET/PUT assignment-policy routes + request record + usings)
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor` (M — the Guardian Signature card)

**Admin.Shared** (created 1):

- `src/SchoolCollab.Admin.Shared/Services/AssignmentPolicyApiClient.cs` (C)

**Assignments context** (created 4, modified 16):

- `src/Assignments/SchoolCollab.Assignments.Core/Services/ISignatureDefaultResolver.cs` (C)
- `src/Assignments/SchoolCollab.Assignments.Api/Services/SignatureDefaultResolver.cs` (C)
- `src/Assignments/SchoolCollab.Assignments.Core/Migrations/<ts>_AddAssignmentRequiresSignature.cs` (C)
- `src/Assignments/SchoolCollab.Assignments.Core/Migrations/<ts>_AddAssignmentRequiresSignature.Designer.cs` (C)
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/Assignment.cs` (M — property + Create/Update trailing params)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/CreateAssignmentCommand/CreateAssignmentCommand.cs` (M)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/CreateAssignmentCommand/CreateAssignmentCommandHandler.cs` (M)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/UpdateAssignmentCommand/UpdateAssignmentCommand.cs` (M)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/UpdateAssignmentCommand/UpdateAssignmentCommandHandler.cs` (M)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/DuplicateAssignmentCommand/DuplicateAssignmentCommandHandler.cs` (M — the one named arg)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Queries/GetAssignmentByIdQuery/GetAssignmentByIdQueryHandler.cs` (M — named-arg mapping)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Queries/ListAssignmentsQuery/ListAssignmentsQueryHandler.cs` (M — named-arg mapping, PassScore/MaxAttempts flow)
- `src/Assignments/SchoolCollab.Assignments.Contracts/ContractTypes.cs` (M — three records gain the trailing field)
- `src/Assignments/SchoolCollab.Assignments.Core/Migrations/AssignmentsDbContextModelSnapshot.cs` (M)
- `src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs` (M — GET /signature-default + RequiresSignature threading on POST/PUT)
- `src/Assignments/SchoolCollab.Assignments.Api/Program.cs` (M — resolver DI)
- `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentsApiClient.cs` (M — GetSignatureDefaultAsync + SignatureDefaultResponse record)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Create.razor` (M — checkbox + pre-fill helper + submit threading)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Edit.razor` (M — checkbox + round-trip)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentEditFormModel.cs` (M — property + LoadFrom + ToCreateRequest arg)

**Tests** (created 7, modified 3):

- `tests/SchoolCollab.Settings.Tests.Unit/Domain/TenantAssignmentPolicyTests.cs` (C)
- `tests/SchoolCollab.Settings.Tests.Unit/Handlers/TenantAssignmentPolicyHandlerTests.cs` (C)
- `tests/SchoolCollab.Students.Tests.Unit/Domain/GradeAssignmentPolicyTests.cs` (C)
- `tests/SchoolCollab.Students.Tests.Unit/GradeAssignmentPolicyHandlerTests.cs` (C)
- `tests/SchoolCollab.Assignments.Tests.Unit/SignatureDefaultsTests.cs` (C — domain + create/update handler threading)
- `tests/SchoolCollab.Assignments.Api.Tests.Unit/SignatureDefaultResolverTests.cs` (C — MockHttp)
- `tests/SchoolCollab.Admin.Tests.Unit/GradeSignaturePolicyEditorTests.cs` (C — bUnit)
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentCreateBunitTests.cs` (M — 4 new cases)
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentFormModelMappingsTests.cs` (M — 2 new cases)
- `tests/SchoolCollab.Admin.Tests.Unit/GradeLevelDetailPageTests.cs` (M — 1 new case)

**Total: 61 files (33 created — 26 production incl. the 6 migration files + 7 test; 28 modified — 25 production + 3 test).** The reviewer treats the three migration timestamp pairs as exact-path-agnostic (any `<ts>`).

### Implementation steps (ordered — follow literally; `dotnet build SchoolCollab.sln` after every step; run the named tests where the step says so. Each step lands build-coherent so a mid-round clock-out never breaks the build)

**Step 1 — Settings backend + tests.** Per decision (a): entity → configuration → `SettingsDbContext` (DbSet + ApplyConfiguration) → DTO → Get/Upsert CQRS → `dotnet ef migrations add AddTenantAssignmentPolicy` in the Settings context → write `TenantAssignmentPolicyTests.cs` + `TenantAssignmentPolicyHandlerTests.cs` (InMemory, the `TenantNotificationPolicyHandlerTests` scaffolding). Build; `dotnet test tests/SchoolCollab.Settings.Tests.Unit` — 0 failures.

**Step 2 — Settings API.** Per decision (a): `AssignmentPolicyRoutes.cs` + `AssignmentPolicyEndpoints.cs` + the `Program.cs` mapping call. Build (Settings.Api must compile; no new packages).

**Step 3 — Students backend + routes + client + tests.** Per decision (b): entity → configuration → `StudentsDbContext` → DTO → Get/Upsert CQRS (grade guard) → migration `AddGradeAssignmentPolicy` → `GradeLevelRoutes.cs` GET/PUT + request record → `StudentsApiClient` Get/Upsert + request record → write `GradeAssignmentPolicyTests.cs` + `GradeAssignmentPolicyHandlerTests.cs` (the `StudentsTestScope` scaffolding from `GradeNotificationPolicyHandlerTests`). Build; `dotnet test tests/SchoolCollab.Students.Tests.Unit` — 0 failures.

**Step 4 — Assignment flag + migration + tests.** Per decision (d): `Assignment.cs` (property + Create/Update trailing params) → `CreateAssignmentCommand`/`UpdateAssignmentCommand` + handlers → `DuplicateAssignmentCommandHandler` named arg → `ContractTypes.cs` (three records) → `AssignmentRoutes.cs` POST/PUT threading → both query-handler mappings → migration `AddAssignmentRequiresSignature` → write `SignatureDefaultsTests.cs` (domain + handler cases, the existing InMemory `BuildScope` scaffolding). Build; `dotnet test tests/SchoolCollab.Assignments.Tests.Unit` — 0 failures (MigrationGuard green WITH the migration).

**Step 5 — Resolver + route + ApiClient + tests.** Per decision (c): `ISignatureDefaultResolver.cs` → `SignatureDefaultResolver.cs` → `Program.cs` DI → `AssignmentRoutes.cs` GET route → `AssignmentsApiClient.GetSignatureDefaultAsync` + response record → write `SignatureDefaultResolverTests.cs` (RichardSzalay MockHttp, the named-client stub pattern). Build; `dotnet test tests/SchoolCollab.Assignments.Api.Tests.Unit` — 0 failures.

**Step 6 — grade-Detail UI.** Per decision (e): `AssignmentPolicyApiClient.cs` → `ModuleServices.cs` registration → `GradeSignaturePolicyEditor.razor` → `GradeLevels/Detail.razor` card. Build.

**Step 7 — Wizard + Edit.** Per decision (f): `AssignmentEditFormModel.cs` (property + LoadFrom + ToCreateRequest arg) → `Create.razor` (checkbox + pre-fill helper + init/grade-change hooks + submit threading) → `Edit.razor` (field + load + FormRow checkbox + update threading). Build.

**Step 8 — UI tests + full matrix.** Per decision (g): `GradeSignaturePolicyEditorTests.cs` (new), `GradeLevelDetailPageTests.cs` (+1 case), `AssignmentCreateBunitTests.cs` (+4 cases), `AssignmentFormModelMappingsTests.cs` (+2 cases). Then final verification: `dotnet build SchoolCollab.sln -c Debug` (0 errors) + `dotnet test` on `SchoolCollab.Settings.Tests.Unit`, `SchoolCollab.Settings.Api.Tests.Unit`, `SchoolCollab.Students.Tests.Unit`, `SchoolCollab.Students.Api.Tests.Unit`, `SchoolCollab.Admin.Tests.Unit`, `SchoolCollab.Assignments.Tests.Unit`, `SchoolCollab.Assignments.Api.Tests.Unit`, `SchoolCollab.ArchitectureTests.Unit` — **0 failures**. No git commits — working tree only. Self-report to `documents/rounds/.ar-8-worker-report.md` (the WORKER REPORT block from the role contract).

### Binding test coverage list (names binding, file paths exact — MSTest + FluentAssertions)

`tests/SchoolCollab.Settings.Tests.Unit/Domain/TenantAssignmentPolicyTests.cs` (new):

- `Create_DefaultsToRequiresSignatureFalse`
- `Create_WithTrue_StoresFlag`
- `SetPolicy_UpdatesFlagAndStampsUpdatedAt`

`tests/SchoolCollab.Settings.Tests.Unit/Handlers/TenantAssignmentPolicyHandlerTests.cs` (new):

- `Get_WhenUnset_ReturnsNull`
- `Upsert_CreatesRow_GetReturnsDto`
- `Upsert_Twice_ReplacesValue`

`tests/SchoolCollab.Students.Tests.Unit/Domain/GradeAssignmentPolicyTests.cs` (new):

- `Create_DefaultsToInheritNull`
- `Create_WithExplicitOverride_StoresFlag`
- `SetOverride_ToNull_RestoresInherit`
- `SetOverride_StampsUpdatedAt`

`tests/SchoolCollab.Students.Tests.Unit/GradeAssignmentPolicyHandlerTests.cs` (new):

- `Get_WhenNoRow_ReturnsNull`
- `Upsert_CreatesOverride_GetReturnsDto`
- `Upsert_ReplacesWithInherit`
- `Upsert_UnknownGrade_ThrowsGradeLevelNotFound`

`tests/SchoolCollab.Assignments.Tests.Unit/SignatureDefaultsTests.cs` (new — domain + create/update handler threading on the existing InMemory scaffolding):

- `Assignment_Create_DefaultsRequiresSignatureFalse`
- `Assignment_Create_WithRequiresSignature_StoresFlag`
- `Assignment_Update_RequiresSignature_RoundTrips`
- `CreateAssignmentHandler_ThreadsRequiresSignature`
- `UpdateAssignmentHandler_ThreadsRequiresSignature`

`tests/SchoolCollab.Assignments.Api.Tests.Unit/SignatureDefaultResolverTests.cs` (new — MockHttp against the named clients):

- `Resolve_NoGrade_ReturnsTenantDefault`
- `Resolve_TenantUnset_NoGrade_ReturnsFalse`
- `Resolve_GradeOverrideWins_OverTenantDefault`
- `Resolve_GradeInheritNull_FallsBackToTenant`
- `Resolve_SettingsUnreachable_DegradesToFalse`
- `Resolve_StudentsUnreachable_DegradesToTenantDefault`

`tests/SchoolCollab.Assignments.Tests.Unit/AssignmentCreateBunitTests.cs` (extend — existing cases untouched):

- `Create_RendersRequiresSignatureCheckbox_DefaultsUnchecked`
- `Create_GradeSelected_PreFillsCheckboxFromResolvedDefault` — MockHttp `Expect` GET `/assignments/signature-default?gradeLevelId={id}` → `{ "requiresSignature": true }`; select the grade; checkbox is checked.
- `Create_AuthorOverrides_OverridesPrefillAndSubmitsValue` — pre-fill true, uncheck, complete submit; the POST body asserts `"requiresSignature": false`.
- `Create_PreFillFetchFails_CheckboxStaysDefault_NoError` — GET 500; checkbox stays unchecked; no error message bar renders; submit still proceeds.

`tests/SchoolCollab.Assignments.Tests.Unit/AssignmentFormModelMappingsTests.cs` (extend — existing cases untouched):

- `LoadFrom_CarriesRequiresSignature`
- `ToCreateRequest_ThreadsRequiresSignature`

`tests/SchoolCollab.Admin.Tests.Unit/GradeSignaturePolicyEditorTests.cs` (new — bUnit, the `GradeNotificationPolicyEditorTests` scaffolding: mocked `AssignmentPolicyApiClient` HttpClient + `StudentsApiClient` MockHttp):

- `Editor_Loads_ShowsGlobalDefaultAndInheritBadge`
- `Editor_ToggleGlobalSwitch_PutsTenantPolicy`
- `Editor_SelectGradeOverride_PutsGradePolicy`
- `Editor_ResetToInherit_PutsNullOverride`
- `Editor_LoadFails_ShowsError`

`tests/SchoolCollab.Admin.Tests.Unit/GradeLevelDetailPageTests.cs` (extend — existing cases untouched):

- `Detail_RendersSignaturePolicyCard` — the rendered source contains `GradeSignaturePolicyEditor` (the existing `GradeNotificationPolicyEditor` assertion precedent at ~line 960).

### Constraints (repo AGENTS.md + rules — restated for the worker)

CPM — no `Version` on any `PackageReference`; this round adds **no packages** (EF migrations use the existing `dotnet-ef` tooling + already-referenced design packages). net10.0. **No MediatR** — CQRS via `ICommandHandler<T[,R]>` / `IQueryHandler<T,R>` + Scrutor assembly scanning (all new handlers auto-register; NO manual registration). MSTest + FluentAssertions + bUnit + Moq + RichardSzalay.MockHttp. Run `dotnet build SchoolCollab.sln` after every change. **No git commits — working tree only.** Primary constructors for ctor injection; XML `<summary>` docs on every new public type/member; structured logging via `ILogger<T>` with named placeholders; **typed exceptions only** — the single new throw is the mirrored `GradeLevelNotFoundException` grade guard; **no `InvalidOperationException` from any new code**. **Migrations required:** exactly three additive migrations (Settings/Students/Assignments), each with Designer + snapshot; `MigrationGuardTests.NoUncommittedModelChanges` green everywhere WITH them (never touch an existing migration). Blazor rules (four `.razor` surfaces ship): FluentUI components for all controls (the grade tri-state is a `FluentSelect` — dropdown-ui skill binding/width rules); `@key` on every `@foreach`; **no `<style>` blocks, no inline `style=`, no new `.razor.css`** — existing utility classes only; `EventCallback` for child→parent. **The worker NEVER edits `documents/rounds/round-ar-8-signature-defaults.md`** — self-reports go to `documents/rounds/.ar-8-worker-report.md` (scratch, untracked). **30-minute run cap + cut-line protocol:** if the clock runs short, finish the CURRENT step to a build-coherent state, update `documents/rounds/.ar-8-worker-report.md` with step-by-step progress (done / in-flight / remaining), and STOP — the parent resumes the bounded remainder. **Repo-scoped searches only — NEVER `find /`** (a prior run hung 28 minutes on an unbounded search).

### Worker task spec (commands + self-report)

- Build after every step; final: `dotnet build SchoolCollab.sln -c Debug` — 0 errors.
- Tests (all 8, 0 failures): `dotnet test tests/SchoolCollab.Settings.Tests.Unit`; `dotnet test tests/SchoolCollab.Settings.Api.Tests.Unit`; `dotnet test tests/SchoolCollab.Students.Tests.Unit`; `dotnet test tests/SchoolCollab.Students.Api.Tests.Unit`; `dotnet test tests/SchoolCollab.Admin.Tests.Unit`; `dotnet test tests/SchoolCollab.Assignments.Tests.Unit`; `dotnet test tests/SchoolCollab.Assignments.Api.Tests.Unit`; `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` (repo-wide scanner — always).
- Changed files = the expected-files list exactly (61; migration `<ts>` pairs exact-path-agnostic); report deviations from the plan one line each.
- Self-report (WORKER REPORT block from the role contract) to `documents/rounds/.ar-8-worker-report.md`; never the round doc.

### Acceptance criteria

Worker-facing:

- `dotnet build SchoolCollab.sln -c Debug`: 0 errors.
- `dotnet test` on the eight projects above: 0 failures (all three `MigrationGuardTests` pass WITH the three new migrations).
- Changed files = the expected-files list one-for-one (61 files); no unrelated deletions/reformatting; no new project/package/sln/CPM change; no existing migration touched.
- Steps 1–8 followed in order; any clock-out honored the cut-line protocol (build-coherent stop + step-by-step self-report); deviations reported, not silent.

Reviewer-facing (static, diff-only):

- **Plan conformance against the expected-files list, one-for-one** — any file outside the list (or missing from it) is a P1.
- **Decisions (a)–(g) + (k) implemented as written, including the recorded adjustments:** (a)/(b) the mirrored entity/config/CQRS/route shapes (STRICT-tenant config, unique filtered index, grade-existence guard → `GradeLevelNotFoundException` → 404); (c) the fail-open resolver (tenant fetch failure ⇒ false; grade fetch failure ⇒ inherit) + always-200 route + `SignatureDefaultResponse` parse; (d) the trailing-param threading at EVERY site — aggregate, both commands + handlers, all three DTO records, POST/PUT route threading, BOTH query-handler mappings by named arg, the duplicate handler's `requiresSignature: source.RequiresSignature`, and the additive migration; (e) `TenantPropagationDelegatingHandler` on the `AssignmentPolicyApiClient` registration + inline immediate-save editor (no dialog, no `<style>` block); (f) pre-fill fail-open (warning log, NO error bar) + the Edit round-trip (missing it = P1 — it is the silent-reset defect).
- **Migration check:** exactly three additive migrations; no existing migration/snapshot regressed beyond the three snapshot edits; `requires_signature` is `NOT NULL DEFAULT false`.
- **Exception routing:** `GradeLevelNotFoundException` → 404 on the students PUT; no `InvalidOperationException` anywhere new; no invented validation throws on the bool fields.
- **Best-practices check:** (1) no overwrites — purely additive on all 28 modified files (existing test cases untouched); (2) repo skills honored — `dotnet-best-practices` (CPM, no MediatR, primary constructors, XML docs, structured logging, typed exceptions), `blazor-components` + `dropdown-ui` (FluentSelect wrapper binding/width), `ef-migrations` (additive only, snapshot consistency); (3) readability — naming matches the `*AssignmentPolicy` / `SignatureDefault` conventions, minimal focused diff, no dead code.

Orchestrator-facing (accept verdict):

- `dotnet build SchoolCollab.sln -c Debug`: **0 errors** (authoritative parent re-run).
- `dotnet test` on the eight projects: **0 failures** (authoritative parent re-run).
- Reviewer verdict PASS (or P2-only with all P2s triaged) + worker self-report reconciled against the tree (multi-run resumes stitched correctly).
- **P1 list empty → CLOSED**; otherwise the accept block names each P1 with its fix owner and the round stays open.
- Residual list refreshed (the pre-identified residuals below unless disproven).

### Residual risks / notes for acceptance

- **Round size vs the 30-minute worker cap:** ~61 files + 3 migrations is 6× the ar-7 round. Expect the cut-line protocol to engage; the steps are ordered so every stop is build-coherent and the parent resumes cleanly. Budget 2–3 worker runs.
- **The later C1 sign-off round must NOT re-add `RequiresSignature`** (decision (d)) — its scope starts at `SignOffState`/`SignatureEvent`. The parent carries this note into that round's plan.
- **No server-side resolution at create:** the create handler persists the request value verbatim; a raw API caller omitting `requiresSignature` gets `false`. The wizard owns pre-fill. If non-wizard clients later need server-side snapshot resolution, that is a follow-up decision (recorded, not a defect).
- **Detail.razor does not display the flag** (the summary DTO carries it) — surfaced by the C2 sign-off UI round; recorded backlog, not a defect.
- **Pre-fill fail-open:** a resolver/endpoint failure leaves the checkbox at its current value with only a warning log (no user-visible error) — the accepted best-effort posture mirroring `NotificationPolicyResolver`; the author always sees and can set the checkbox.
- **Grade-switch clobbers an author's checkbox override** (re-fill on grade change, decision (f)) — intended, mirrors the subject-list reset; called out for the UI tester so it is not reported as a bug.
- **`AssignmentPolicyApiClient` tenancy:** STRICT tenant entity — the `TenantPropagationDelegatingHandler` is mandatory in the registration (decision (e)); a missing handler reproduces the historical "wrong tenant / TenantContextRequiredException" symptom class documented in `ModuleServices.cs`.
- **InMemory + rowversion quirks:** the new handler tests mirror the existing policy-test scaffolding; if the `IHasRowVersion` column trips InMemory, follow the same accommodation the notification-policy tests use (do not weaken the PostgreSQL config).
- **ar-1 EF owned-children verification residual** remains carried and untouched by this round.

### UI-round note (tester handover — derived by the parent AFTER acceptance)

This is a UI round (Create.razor, Edit.razor, GradeLevels/Detail.razor, and the new GradeSignaturePolicyEditor.razor). Per the role contract, after the worker + parent verification and the accept verdict, the parent derives the tester-scope handover from the changed-file list (the changed files, the pages/cards that render them, the ApiClient methods they call, navigation entry points — one line of rationale per entry) and the minimax-m3 tester fires against exactly that list, no more. The handover is written into this doc's UI Tester section by the parent; the tester's scope is NOT this plan. Tester-relevant known behaviors to hand over as "intended, not bugs": the grade-switch pre-fill clobber and the fail-open pre-fill.

---

## Worker Report

Implementation COMPLETE via two worker passes (both hit the 30-min cap) + parent authoritative reconciliation:
- **Pass 1** (deepseek-v4-flash): Steps 1–6 + most of Step 7 production code + 6 test files.
- **Pass 2** (kimi-k2.7-code escalation): Step 7 (`Edit.razor`) + Step 8 UI tests.
- **Parent authoritative pass**: fixed residual test-code defects (build errors in `AssignmentCreateBunitTests.cs`, missing `AssignmentPolicyApiClient` DI registration in the Admin harness, MockHttp URL mismatch, over-strict log assertion, two test-shape deviations from the plan).

Changed files: 64 (53 production + 11 test) — all 61 plan-expected files present plus the Assignments.Api.Tests csproj MockHttp reference and 2 additive test-file lines. Full report: `documents/rounds/.ar-8-worker-report.md`.

## Review

REVIEW (kimi-k2.7-code, static diff-only)
Verdict: P1 → rework → **PASS**
- Iteration 1: P1 — `AssignmentCreateBunitTests.cs` existing `Create_HintText_ReflectsSelectionViaGate` was replaced (plan requires existing cases untouched); P2 — csproj MockHttp ref (accepted note, CPM-clean), ContractTypes XML doc "Null in pre-WS-C1 data" on non-nullable `bool`, Create.razor `Guid.Parse` outside try.
- Rework (parent): restored the deleted test, fixed the XML doc, moved `Guid.Parse` inside the try with the WS-C1 re-resolve guarded on successful parse.
- Iteration 2: **PASS** — no P1s, no P2s, best-practices clean.

## Acceptance

- Build: `dotnet build SchoolCollab.sln -c Debug` — **0 errors** (parent authoritative).
- Tests: Settings.Tests.Unit 491/0; Settings.Api.Tests.Unit 1/0; Students.Tests.Unit 422/0; Students.Api.Tests.Unit 1/0; Admin.Tests.Unit 537/0; Assignments.Tests.Unit 449/0; Assignments.Api.Tests.Unit 20/0; ArchitectureTests.Unit 20/0 — **0 failures**.
- Decisions (a)–(g)+(k) implemented as written incl. the recorded adjustments (Option A RequiresSignature end-to-end; resolver fail-open; inline immediate-save editor; wizard pre-fill fail-open).
- Exactly three additive migrations (Settings `20260910124751`, Students `20260910125042`, Assignments `20260910125859`) — `MigrationGuardTests.NoUncommittedModelChanges` green.
- Reviewer PASS (iteration 2). Worker self-report reconciled against the tree.
- **P1 list empty → CLOSED.**

## UI Tester

Scope is limited to the surfaces below. The feature: a per-tenant guardian-signature default + per-grade override that pre-fills the assignment create wizard's "Require guardian signature after completion" checkbox (author-overridable, persisted on the assignment).

- `src/Students/SchoolCollab.Students.Application/Components/Students/GradeSignaturePolicyEditor.razor` — NEW compact editor on the grade Detail page: tenant default `FluentSwitch` (immediate save), grade tri-state `FluentSelect` (Inherit/Require/No — immediate save), Inherit-global badge, loading ring, per-failure error bar.
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor` — "Guardian Signature" card hosting the editor (after the Notification & Delivery card).
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Create.razor` — signature checkbox pre-filled from the resolved default on grade selection, author-overridable, submitted with create.
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Edit.razor` — signature checkbox round-trips on edit.
- `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentsApiClient.cs` — `GetSignatureDefaultAsync` (the pre-fill source).

**Intended, NOT bugs (do not report):** grade-switch pre-fill clobbers an author's override (decision (f)); pre-fill fail-open leaves the checkbox at its current value with no error bar.

---

## Progress Checkpoint (parent, mid-round)

State saved 2026-09-10 while the round is OPEN (not yet committed/pushed):
- Implementation COMPLETE (61 plan-expected files + 3 additive) — see Worker Report above.
- Reviewer (kimi-k2.7-code): iteration 1 = P1 (restored `Create_HintText_ReflectsSelectionViaGate` in `AssignmentCreateBunitTests.cs`; moved `Guid.Parse` inside the try in `Create.razor:OnGradeLevelChangedAsync`; fixed `ContractTypes.cs` XML doc) → iteration 2 = **PASS**.
- UI tester (minimax-m3): found P1 + P2 in `GradeSignaturePolicyEditor.razor` —
  - P1: `_busy` only reset in the catch path of `ToggleTenantDefaultAsync` (line ~116) and `OnOverrideChangedAsync` (line ~134); the success path calls `LoadAsync` which never clears `_busy`, so `FluentSwitch`/`FluentSelect` stay `Disabled` until page reload (silent-broken-controls).
  - P2: `LoadAsync` does not clear `_error` before refetching; a successful re-save after a prior failure leaves the stale error bar visible.
- Parent rework applied: both save methods now reset `_busy = false` in a `finally` (+`StateHasChanged()`); `LoadAsync` clears `_error` at start. Verified present: `_busy = false` at lines 130/158, `_error = null` at line 92.
- Build: `dotnet build SchoolCollab.sln -c Debug` **0 errors**; Admin.Tests.Unit **537 pass / 0 fail** (test project covering the editor).
- NEXT (open): re-freeze `diffs-ar-8-signature-defaults.patch`, re-dispatch UI tester for re-verification of the two fixes, record tester verdict, then commit + push + PR via the dedicated subagent.

## UI Tester — final verdict

UI TEST (minimax-m3) iteration 1: **P1** — `_busy` never reset on the success path in `ToggleTenantDefaultAsync`/`OnOverrideChangedAsync` (controls permanently disabled after one successful save) + **P2** — `LoadAsync` does not clear `_error` before refetch (stale error bar after successful re-save).
Parent rework: `_busy = false` in a `finally` (+`StateHasChanged()`) in both save methods; `_error = null` at the top of `LoadAsync`.
UI TEST iteration 2: **PASS** — both fixes confirmed, no new defects, no out-of-round observations.

## Round status: CLOSED

- Implementation: 64 files (61 plan-expected + 3 additive) — see Worker Report.
- Reviewer (kimi-k2.7-code): PASS (iteration 2, after P1 rework).
- UI tester (minimax-m3): PASS (iteration 2, after P1/P2 rework).
- Build: 0 errors. Tests: Settings 491 / Settings.Api 1 / Students 422 / Students.Api 1 / Admin 537 / Assignments 449 / Assignments.Api 20 / Architecture 20 — 0 failures.
- Migrations: exactly three additive (Settings `20260910124751`, Students `20260910125042`, Assignments `20260910125859`).
