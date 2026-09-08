# Round ar-5-lifecycle — Lifecycle extensions: Scheduled / Approval / Archive states + feature flag + Index/Detail surfaces (WS-A2)

Provider: pi (models: glm-5.3, minimax-m3, kimi-k2.7-code, deepseek-v4-flash [UI round — Index.razor + Detail.razor + a new dialog ship → the tester fires after acceptance])

- **Tier:** 3 — full four-agent round **plus a UI-tester pass** (the round touches `.razor` — Index status filter/actions, Detail approval
  section + Schedule dialog — so after the worker + parent verification and the accept verdict, the parent derives the tester-scope handover
  from the changed-file list and the deepseek-v4-flash tester fires). Worker model: minimax-m3 (**30-minute run cap** — the round spans
  domain + EF + API + config + UI; steps are sequenced **backend-first with the UI last**; if the clock runs short, land steps in order and
  report deviations — never skip silently, never reverse the order).
- **Round base:** HEAD `044257606ec190b55c5903675427094e86599609` on branch `stack/5-ar-5-lifecycle` (created from the stack/4 tip; the tree is
  clean of tracked changes).
- **Pre-round dirty paths OUT of round scope** (round patch pathspec = `src`, `tests`, and `documents/configuration.md` — the config doc is an
  IN-round modified file per the configuration-documentation repo rule, decision (d)): the 9 known untracked scratch files —
  `documents/rounds/.ar-1-scope.txt` | `.ar-1-worker-report.md` | `.ar-2-scope.txt` | `.ar-2-worker-report.md` | `.ar-3-scope.txt` |
  `.ar-3-worker-report.md` | `.ar-4-scope.txt` | `.ar-4-worker-report.md` | `documents/rounds/diffs-period-upsert-single-page.patch` |
  `documents/rounds/round-period-upsert-single-page.md` (this round doc and its patch artifact are written outside the pathspec too).
- **Rounds 1–4 landed what this round builds on** (do NOT re-plan any of it): questions/options/attachments + `AiPromptOverride` through
  create/update commands with typed validation (ar-1); the AI generation endpoint + seam (ar-2); the wizard question UI + form model
  (ar-3); `ContentModule` + `AssignmentResource` standalone tenant entities + `IFileStore`/`LocalFileStore` + the staging endpoint +
  `StagedFileSweeper`/`StagedFileSweepService` (the sweep pattern this round mirrors, ar-4) + wizard `ResourcesSection`. The lifecycle today
  is `Draft=0 → Published=1 → Closed=2` (+ `Unpublish` → Draft, `Close` from Published); `Update()` throws unless Draft; `Publish()` stamps
  `PublishedAt`; delete is Draft-only.
- **Sources:** `documents/solution/assignment-request-implementation-details.md` §2 WS-A2 (THE design source), §3 round-slicing row 5,
  §1.2 (publish command flow — where the approval guard plugs in), §1.9 (feature-flag conventions), §1.10 (test conventions);
  `documents/specs/assignment-request-feature-spec.md` §3.5 (lifecycle: Draft → Review/Approval → Publish → … → Archive), §7 Q2 (approval
  flag default OFF, tenant-level, every AR requires approval when on) + §7 Q6 (archives retained indefinitely, manual export only);
  `documents/rounds/round-ar-4-modules-resources.md` (house format + the sweep/extension precedents);
  `.github/copilot/rules/dotnet-best-practices.md`; `.github/copilot/rules/ef-migrations.md`; `.github/copilot/rules/blazor-components.md`;
  `.github/skills/dialog-ui/SKILL.md` (the Schedule dialog ships); `documents/configuration.md` §2 + §5.

## Plan

### Goal

Execute workstream **WS-A2** from `documents/solution/assignment-request-implementation-details.md` §2 (round log §3 row 5): extend the
assignment lifecycle with **Scheduled** and **Archived** states (`AssignmentStatus` + DTO mirror), an **approval workflow**
(`ApprovalStatus` + `SubmitForApproval`/`Approve`/`Reject` commands) gated by the new tenant-overridable flag
`FEATURE:RequireAssignmentApproval` (global default OFF, spec §7 Q2), the **approval guard on publish** (typed
`AssignmentApprovalRequiredException`), two **auto-hosted sweeps** in assignments-api (scheduled auto-publish + due-date+grace archive, spec
§3.5 step 8 / §7 Q6), and the **Index/Detail UI surfaces** for all of it (status filter + badges + row actions, Detail approval section +
Schedule dialog, flag-gated Draft behavior). The round closes with the `documents/configuration.md` §2/§5 flag mapping in the same change
(AGENTS.md rule).

### Scope

**In (fixed — this is the whole round):**

1. **Domain** (`src/Assignments/SchoolCollab.Assignments.Core/Domain/`): `AssignmentStatus` gains `Scheduled = 3`, `Archived = 4` (enum ints
   are persisted — no enum-value migration); new `ApprovalStatus` enum (`Pending = 0, Approved = 1, Rejected = 2`; null on the entity = not
   submitted); new typed exception `AssignmentApprovalRequiredException` (Domain/Exceptions pattern, one type per file); five new domain
   events (`AssignmentScheduledEvent`, `AssignmentArchivedEvent`, `AssignmentApprovalSubmittedEvent`, `AssignmentApprovedEvent`,
   `AssignmentRejectedEvent` — all `(Guid AssignmentId, string Title)` records mirroring `AssignmentClosedEvent`; the approval-stamp events
   may carry the approver id — see decision (c)); `Assignment` gains `AvailableFromUtc`, `ArchiveGraceDays`, `ApprovalStatus`, `ApprovedBy`,
   `ApprovedAt` properties + the new lifecycle methods and guards per decision (a)/(c).
2. **EF** (`Data/Configurations/AssignmentConfiguration.cs`, `Migrations/`): property mappings for the five new columns + one additive
   migration `AddAssignmentLifecycleAndApproval`. `MigrationGuardTests.NoUncommittedModelChanges` must stay green.
3. **Command surface** (`CQRS/Assignments/Commands/`): `ScheduleAssignmentCommand`, `SubmitAssignmentForApprovalCommand`,
   `ApproveAssignmentCommand`, `RejectAssignmentCommand`, `ArchiveAssignmentCommand` (sweep-only dispatch — NO public route) + handlers;
   `PublishAssignmentCommandHandler` + `ScheduleAssignmentCommandHandler` inject `IFeatureFlagService` and pass `approvalRequired` into the
   domain methods; `CreateAssignmentCommand`/`UpdateAssignmentCommand` gain a trailing `ArchiveGraceDays` param threaded to
   `Create()`/`Update()`; `DeleteAssignmentCommandHandler` verified (decision (a) — no production change).
4. **Queries/contracts**: `AssignmentSummary` (Core DTO record) + `AssignmentSummaryDto` (Contracts) gain the five lifecycle fields as
   trailing optional params; the repo `ListAsync` projection + both query handlers (List + GetById) carry them; `AssignmentStatusDto` gains
   `Scheduled = 3`/`Archived = 4`; new `ApprovalStatusDto` mirror; `CreateAssignmentRequest`/`UpdateAssignmentRequest` gain trailing
   `ArchiveGraceDays` (int, default 30); new request records `ScheduleAssignmentRequest`, `ApproveAssignmentRequest`,
   `RejectAssignmentRequest`.
5. **Sweeps** (`SchoolCollab.Assignments.Api/Services/`): `ScheduledPublishSweeper` (pure dispatch core) + `ScheduledPublishSweepService`
   (BackgroundService) and `ArchiveSweeper` + `ArchiveSweepService`, registered via a new `AssignmentLifecycleSweepExtensions` — mirroring
   the ar-4 `StagedFileSweeper`/`StagedFileSweepService`/`StagedFileSweepExtensions` structure exactly; two read-only cross-tenant candidate
   queries on `IAssignmentRepository`.
6. **API** (`Api/Endpoints/AssignmentRoutes.cs`, `Api/Program.cs`): four new routes inside the existing `/assignments` group (schedule,
   submit-for-approval, approve, reject) + `AssignmentApprovalRequiredException` → 400 mapping; `JsonStringEnumConverter<ApprovalStatusDto>`
   in `ConfigureHttpJsonOptions`.
7. **Feature flag**: `FeatureFlagKeys.RequireAssignmentApproval` const; MigrationService seeds the global flag row (default OFF, idempotent —
   NO pilot-tenant override); AppHost parameter `feature-flag-require-assignment-approval` + `WithEnvironment` fan-out to **assignments-api
   AND admin**; `SchoolCollab.Admin/appsettings.json` cold-start fallback; `documents/configuration.md` §2 + §5 mapping (decision (d)).
8. **UI** (`SchoolCollab.Assignments.Application`): Index status filter + badge + row actions (incl. flag-on Draft → "Submit for Approval"),
   Detail badge + Overview rows + flag-gated approval section + Schedule dialog, `AssignmentsApiClient` additions
   (Schedule/SubmitForApproval/Approve/Reject), `AssignmentEditFormModel.ArchiveGraceDays` pass-through (NO visible field),
   `Edit.razor` Scheduled-status gate + pass-through. `Create.razor` UNTOUCHED.
9. **Tests** (MSTest + FluentAssertions + bUnit): domain lifecycle matrix, handler tests (schedule/submit/approve/reject + the publish
   approval-gate matrix), sweep tests (pure-core dispatch + candidate queries), bUnit extensions, create/update ArchiveGraceDays threading.

**Out (record — do not touch):** ward-facing queries and the ward experience (WS-A5 / Phase 2 — D1 module-gating); scoring/attempts
(WS-A3, round 6); duplicate-as-template (WS-A4, round 7); manual archive **export** (spec §7 Q6 — PDF/CSV export is Phase 5; this round
only stores archive state); any archive **retention UI** or ConfigFlags UI changes (the flag row becomes tenant-overridable through the
existing `/config-flags` surface — no admin UI work needed); integration events for the new transitions (decisions (c)/(g) name domain
events only — `IIntegrationEventPublisher` enqueue stays publish/unpublish/close-only); `AssignmentSubmission`/gate behavior on archive
(read-only enforcement is at the aggregate + command level); `SchoolCollab.Assignments.Tests.Integration`/Playwright projects;
`wwwroot/app.css`; `Admin.Shared` components (FeatureFlagGate is consumed, not modified); `Directory.Packages.props`/`.sln`/csproj changes
(no new packages); `Create.razor` (create defaults ArchiveGraceDays=30 via the request default); the 9 pre-round scratch files.

### Decisions (binding — implement as written; (a)–(k) are the parent-adjudicated design, folded in verbatim with two recorded adjustments)

- **(a) Status extension + consumer sweep.** `AssignmentStatus` gains `Scheduled = 3`, `Archived = 4` (no enum-value migration — ints are
  persisted); `AssignmentStatusDto` mirror gains the same values. Sweep **every** status consumer:
  - `Assignment.Update()` guard: Draft **OR Scheduled** (structural edits still freeze at Published).
  - `Assignment.Unpublish()`: Published **OR Scheduled** → Draft; clears `AvailableFromUtc` in both cases.
  - `Assignment.Close()`: throws when **Archived** (archived is read-only); existing Published→Closed + idempotent Closed no-op kept.
  - `Assignment.Publish()`: throws when **Archived**; gains the approval guard (decision (c)).
  - `Index.razor`: filter options + badge mapping + `BuildAssignmentActions` switch (decision (j)).
  - `Detail.razor`: badge + action visibility (decision (j)).
  - `AssignmentRepository.ListAsync`: unchanged — the status filter passes through.
  - **DELETE guard:** an Archived assignment must not be deletable (retention — spec §7 Q6: archives retained indefinitely).
    **Verified against the current `DeleteAssignmentCommandHandler`:** the existing `Status != Draft → throw` check already blocks Archived
    (and everything else non-Draft). **Adjusted (recorded):** no production change to the delete handler; the round PINS the rule with the
    `Archived_delete_is_blocked` handler test (binding coverage list) so a future relaxation cannot silently lose retention. The
    pre-existing `InvalidOperationException` in that handler is untouched (ar-1 lesson applies to NEW code paths only).
- **(b) Additive migration `AddAssignmentLifecycleAndApproval`.** Columns on `assignment`:
  `AvailableFromUtc` (`DateTimeOffset?`, null), `ArchiveGraceDays` (`int` NOT NULL, default 30), `ApprovalStatus` (`int?` null —
  0=Pending, 1=Approved, 2=Rejected), `ApprovedBy` (`Guid?`), `ApprovedAt` (`DateTimeOffset?`). New Core enum `ApprovalStatus`
  (Pending=0, Approved=1, Rejected=2; null = not submitted) + `ApprovalStatusDto` mirror in Contracts. EF config: the five property
  declarations in `AssignmentConfiguration.ConfigureTenantEntity` next to the existing ones (`ArchiveGraceDays` with
  `HasDefaultValue(30)`).
- **(c) Domain methods on `Assignment`** (each bumps `UpdatedAt` on a real mutation and enqueues the named domain event; new state guards
  follow the existing aggregate pattern — `InvalidOperationException` with a clear message — except the two argument-hygiene cases and the
  NEW typed approval guard; the ar-1 "typed exceptions" lesson applies to NEW command-surface exceptions, the approval guard being exactly
  that):
  - `Schedule(DateTimeOffset availableFromUtc, bool approvalRequired = false)` — from Draft OR Scheduled (reschedule); past dates
    rejected with `ArgumentException` ("availableFrom" must be in the future); approval guard: `approvalRequired && ApprovalStatus !=
    ApprovalStatus.Approved` → `AssignmentApprovalRequiredException`; sets `AvailableFromUtc` + `Status = Scheduled` +
    `AssignmentScheduledEvent`.
  - `Publish(bool approvalRequired = false)` — **the approval guard**: `approvalRequired && ApprovalStatus != ApprovalStatus.Approved` →
    throw **`AssignmentApprovalRequiredException`** (new typed exception, `Domain/Exceptions/AssignmentApprovalRequiredException.cs`,
    ctor `(string message)` mirroring `AssignmentQuestionValidationException`). Serves both immediate publish (Draft) and publish-now
    (Scheduled): WS-A2's PublishNow ≡ an early publish of a Scheduled assignment — same handler flow, **no separate method**. Archived →
    `InvalidOperationException`; the existing Published no-op return is kept; stamps `PublishedAt` +
    `AssignmentPublishedEvent` (existing). Publishing a Scheduled assignment does NOT need to clear `AvailableFromUtc` (the window has
    fired; `AvailableFromUtc` stays as the historical record).
  - `Unpublish()` extended per (a) — Published OR Scheduled → Draft, `AvailableFromUtc = null`, + `AssignmentUnpublishedEvent` (existing);
    Archived → `InvalidOperationException`.
  - `Archive()` — from Published OR Closed, idempotent (already-Archived → no-op return), + `AssignmentArchivedEvent`; any other state →
    `InvalidOperationException`. Does NOT clear `PublishedAt`/`DueDate` (retention — the archived row keeps its history).
  - `SubmitForApproval()` — Draft only → `ApprovalStatus = Pending` + `AssignmentApprovalSubmittedEvent`; else
    `InvalidOperationException`.
  - `Approve(Guid approverId)` — Pending only → `ApprovalStatus = Approved`, `ApprovedBy = approverId`, `ApprovedAt = now` +
    `AssignmentApprovedEvent`; empty `approverId` → `ArgumentException`; else `InvalidOperationException`.
  - `Reject(Guid approverId)` — Pending only → `ApprovalStatus = Rejected`, **clears** `ApprovedBy`/`ApprovedAt` +
    `AssignmentRejectedEvent`; empty `approverId` → `ArgumentException`; else `InvalidOperationException`.
  - `Update(...)` gains an `int archiveGraceDays = 30` trailing param (stamps `ArchiveGraceDays`); the guard widens to Draft OR Scheduled
    per (a).
  - `Create(...)` gains an `int archiveGraceDays = 30` trailing param (stamps `ArchiveGraceDays`).
- **(d) Feature flag `FEATURE:RequireAssignmentApproval`.** New const in `FeatureFlagKeys` (`RequireAssignmentApproval =
  "FEATURE:RequireAssignmentApproval"`, XML doc naming consumers). Global default **OFF**, seeded idempotently by the MigrationService
  (mirror the `SeedEnableActivityGroupsAsync` static-method precedent in `src/SchoolCollab.MigrationService/Program.cs` — a
  `SeedRequireAssignmentApprovalAsync(SettingsDbContext db, ILogger logger)` called after `SeedEnableActivityGroupsAsync`;
  `FeatureFlag.Create(NormalizeKey(...), description, null, isEnabled: false)`; **NO pilot-tenant override** — tenants opt in via the
  existing ConfigFlags tenant-override surface, spec §7 Q2). AppHost: `var requireAssignmentApproval =
  builder.AddParameter("feature-flag-require-assignment-approval");` + `.WithEnvironment("FeatureFlags__FEATURE__RequireAssignmentApproval",
  requireAssignmentApproval)` fanned out onto **assignments-api AND admin** (the flag gates both API publish behavior and UI) +
  `"feature-flag-require-assignment-approval": "false"` in `Parameters:`. `SchoolCollab.Admin/appsettings.json` cold-start fallback:
  `"RequireAssignmentApproval": "false"` in the existing `FeatureFlags.FEATURE` block (mirrors `EnableActivityGroups`). The parameter is the
  IConfiguration **cold-start value** for both hosts; the Settings Config-service row stays the runtime authority (tenant-overridable).
  `documents/configuration.md`: §2 parameter row (naming the injected env var + both consumer sites) + §5 runtime-flags table row (default
  OFF, consumer sites: `PublishAssignmentCommandHandler`/`ScheduleAssignmentCommandHandler` publish gates + the Admin Assignments Index/Detail
  UI, seeded by the migration service, tenant-overridable, cold-start fallback in Admin appsettings). AGENTS.md rule: the flag mapping lands
  in the SAME round.
- **(e) Flag resolution.** `PublishAssignmentCommandHandler` + `ScheduleAssignmentCommandHandler` inject
  `IFeatureFlagService` (interface in `SchoolCollab.Core/Features` — the Api host provides the implementation via `AddAuthAndTenancy`'s
  `TryAddSingleton<IFeatureFlagService, ConfigurationFeatureFlagService>` fallback / the host's DB-backed registration) and compute
  `var approvalRequired = await featureFlags.IsEnabledAsync(FeatureFlagKeys.RequireAssignmentApproval, cancellationToken);` **before**
  calling `assignment.Publish(approvalRequired)` / `assignment.Schedule(command.AvailableFromUtc, approvalRequired)`. The auto-publish sweep
  resolves the flag the same way because it dispatches `PublishAssignmentCommand` through the shared handler (no duplicate flag logic).
- **(f) Commands** (all `: ICommand`, Scrutor auto-registered, one folder per command with command + handler files as the repo does):
  `ScheduleAssignmentCommand(Guid AssignmentId, DateTimeOffset AvailableFromUtc)`; `SubmitAssignmentForApprovalCommand(Guid AssignmentId)`;
  `ApproveAssignmentCommand(Guid AssignmentId, Guid ApproverId)`; `RejectAssignmentCommand(Guid AssignmentId, Guid ApproverId)`;
  `ArchiveAssignmentCommand(Guid AssignmentId)` — dispatched **ONLY by the sweep** (no public route). **Adjusted (recorded):** the parent's
  sketch carried `contactIds:null?` on the schedule command — omitted as a dead field: scheduling never snapshots recipients (the sweep
  dispatches `PublishAssignmentCommand(id, null)` per decision (g), and publish-now passes its own selection); a persisted-but-ignored
  field would be a readability defect. `ApproverId` is a request field (`Guid.Empty` placeholder in the UI — same attribution posture as
  `ReviewAssignmentRequest.TeacherId`, identity gap D-6); the new endpoints inherit the group's authenticated-only authorization.
  Handler shape (all four + archive): `repository.GetAsync` → `AssignmentNotFoundException` → domain method → `repository.UpdateAsync` +
  `cache.RemoveByTagAsync("assignments")` + `assignment.ClearDomainEvents()` + structured log — mirroring
  `CloseAssignmentCommandHandler` (no recipients/gates/broadcast; no `DetectChanges` — scalar mutations only). The schedule handler resolves
  the flag per (e).
- **(g) Sweeps in assignments-api** (mirror the ar-4 `StagedFileSweeper`/`StagedFileSweepService` structure exactly — pure helper class +
  hosted `BackgroundService` + `Add{Layer}()` extension + first-sweep-immediately loop): `ScheduledPublishSweepService` finds
  `Status == Scheduled && AvailableFromUtc <= now` → dispatches `PublishAssignmentCommand(id, null)` per candidate (the full publish
  flow: recipients, gates, broadcast — through the existing handler); `ArchiveSweepService` finds `(Published OR Closed) && DueDate !=
  null && DueDate + ArchiveGraceDays days <= now` → dispatches `ArchiveAssignmentCommand`. **Per-candidate try/catch** — one failing
  assignment never kills the sweep (mirror `StagedFileSweeper`'s error isolation; log + continue). Repository: two candidate-query methods
  following the existing repo pattern:
  `Task<List<AssignmentSweepCandidate>> ListScheduledForAutoPublishAsync(DateTimeOffset nowUtc, CancellationToken ct = default)` and
  `Task<List<AssignmentSweepCandidate>> ListDueForArchiveAsync(DateTimeOffset nowUtc, CancellationToken ct = default)` over
  `Db.Assignments.IgnoreQueryFilters(["Tenant"]).AsNoTracking()` — the **sanctioned cross-tenant read** the `StagedFileSweepService`
  reference query uses (comment it; the sweep performs NO cross-tenant writes). New tiny record
  `AssignmentSweepCandidate(Guid Id, Guid TenantId)` (Core/DTOs). **Tenancy of the dispatch** (required — the ambient tenant in a
  BackgroundService is the System/empty context, under which tenant-filtered `GetAsync` finds nothing): each candidate's handler dispatch
  is wrapped in `ITenantContextAccessor.RunWithExplicitTenantAsync(candidate.TenantId, …)` (the sanctioned explicit-tenant path the
  `PilotActivityGroupFlagOverrideSeeder` uses) so the publish/archive handler resolves recipients, gates, and the broadcaster under the
  candidate's tenant. The pure cores — `ScheduledPublishSweeper.PublishDueAsync(candidates, publishHandler, tenantAccessor, logger, ct)`
  and `ArchiveSweeper.ArchiveDueAsync(candidates, archiveHandler, tenantAccessor, logger, ct)` — are static, DI-free (the
  `StagedFileSweeper` testability pattern), returning the processed count. Intervals: `ScheduledPublishSweepService` every
  `TimeSpan.FromMinutes(15)` (publish is time-sensitive), `ArchiveSweepService` every `TimeSpan.FromHours(24)` (the
  `ActivityGroupRolloverService.DefaultInterval` precedent) — constants, no new config surface this round (recorded as a residual).
- **(h) Endpoints** (`AssignmentRoutes.cs`, inside the existing `/assignments` group so they inherit its authorization posture; response
  shape mirrors the existing publish/close routes — `Results.NoContent()` on success): `POST /{id:guid}/schedule` (body
  `ScheduleAssignmentRequest(DateTimeOffset AvailableFromUtc)`), `POST /{id:guid}/submit-for-approval` (no body),
  `POST /{id:guid}/approve` (body `ApproveAssignmentRequest(Guid ApproverId)`), `POST /{id:guid}/reject` (body
  `RejectAssignmentRequest(Guid ApproverId)`). Exception mapping: `AssignmentApprovalRequiredException` →
  `Results.BadRequest(new { ex.Message })` (consistent with the group's catch pattern); `AssignmentNotFoundException` →
  `Results.NotFound()`; the domain `InvalidOperationException` guards → 400 `{ ex.Message }` (the unpublish route's existing pattern).
  DTOs: `AssignmentSummaryDto` gains trailing optional `AvailableFromUtc` (`DateTimeOffset? = null`), `ArchiveGraceDays` (`int = 30`),
  `ApprovalStatus` (`ApprovalStatusDto? = null`), `ApprovedBy` (`Guid? = null`), `ApprovedAt` (`DateTimeOffset? = null`); the repo's
  `AssignmentSummary` record + `ListAsync` projection + both query handlers (List + GetById) carry them; `CreateAssignmentRequest` +
  `UpdateAssignmentRequest` gain trailing `int ArchiveGraceDays = 30`. Also: `JsonStringEnumConverter<ApprovalStatusDto>` added to Api
  `ConfigureHttpJsonOptions` (next to the existing `AssignmentStatusDto` line — the enum is string-serialized on the wire) and the same
  converter in `AssignmentsApiClient._jsonOptions`.
- **(i) ApiClient:** `ScheduleAsync(Guid id, DateTimeOffset availableFromUtc, CancellationToken ct = default)`,
  `SubmitForApprovalAsync(Guid id, CancellationToken ct = default)`, `ApproveAsync(Guid id, Guid approverId, CancellationToken ct =
  default)`, `RejectAsync(Guid id, Guid approverId, CancellationToken ct = default)` — POST to the new routes, mirroring
  `CloseAsync`/`PublishAsync` exactly.
- **(j) UI (this makes it a UI round — the tester will fire).** Badge mapping (binding, both Index and Detail, keeping the existing
  ternary style): Draft → `Appearance.Lightweight`, Published → `Accent`, **Scheduled → `Appearance.Outline`** (distinct from Draft and
  Published; the decision's "pick sensible appearances" resolved to Outline because it is the only unused in-repo member — Accent is taken
  by Published), **Closed → `Neutral`, Archived → `Neutral`** (like Closed). `Index.razor`: status filter adds `new(3, "Scheduled")` +
  `new(4, "Archived")` options + `LoadAssignmentsAsync` maps `3 → AssignmentStatusDto.Scheduled`, `4 → AssignmentStatusDto.Archived`; row
  actions: **Scheduled → Edit + Publish now (existing `PublishAsync`) + Unpublish**; **Archived → Review only** (read-only);
  **Draft rows when the approval flag is ON → "Submit for Approval" action instead of Publish** (publishing unapproved 400s) — flag read
  via injected `IFeatureFlagService` (`@inject IFeatureFlagService FeatureFlags`, resolved once in `OnInitializedAsync` and cached in a
  field — the NavMenu.razor C#-side flag-read precedent). `Detail.razor`: badge extension; Overview grid adds "Available from"
  (`AvailableFromUtc` or —) + "Archive after" (`DueDate + ArchiveGraceDays` computed date or —, with the grace-day count shown inline,
  e.g. "grace: 30 days"); Draft actions: Publish + **"Schedule…"** (new `ScheduleDialog` — mirrors `PublishDialog` + the dialog-ui
  skill/DialogShell pattern: `DialogShellBase<ScheduleFormModel, ScheduleResult>` + a date-only `FluentDatePicker` following the repo's
  existing date-input precedent (DueDate in the wizard); **schedule lands at 00:00 UTC of the chosen date** —
  `new DateTimeOffset(model.AvailableFrom.Value.Date, TimeSpan.Zero)`); Scheduled actions: **Publish now + Cancel schedule**
  (Unpublish — Publish now reuses the existing `OpenPublishDialogAsync` flow); flag-gated approval section (`FeatureFlagGate` component
  from Admin.Shared, `Key="@FeatureFlagKeys.RequireAssignmentApproval"`): status chip (Not submitted / Pending / Approved / Rejected,
  mapped from the nullable `ApprovalStatus`) + **Submit for Approval** button (Draft) / **Approve + Reject** buttons each confirmed via
  `DialogService.ShowConfirmDialogAsync` (the destructive-action rule; Pending) — ApproverId passes `Guid.Empty // placeholder, wired to
  auth in a later phase` (the ReviewAssignmentRequest.TeacherId posture); Archived: read-only (Edit/Publish hidden — the Draft/Scheduled
  action blocks don't render for Archived). `AssignmentEditFormModel`: `ArchiveGraceDays` pass-through property (`LoadFrom` sets it; NO
  visible wizard/edit field this round) — **Adjusted (recorded):** the parent's sketch named `ToUpdateRequest`, which does not exist; the
  edit page builds `UpdateAssignmentRequest` inline, so the round-trip is: `LoadFrom` populates the model property AND `Edit.razor` passes
  the loaded value into its inline `UpdateAssignmentRequest(..., ArchiveGraceDays: _model.ArchiveGraceDays)` so an edit never resets it to
  the default. `Edit.razor` status gate widens to Draft **OR Scheduled** (message: "Only Draft and Scheduled assignments can be edited.") —
  the UI consequence of decision (a)'s Update-guard widening. `Create.razor`: **UNTOUCHED** (create defaults ArchiveGraceDays=30 via the
  request default).
- **(k) Tests** (MSTest + FluentAssertions + bUnit): domain lifecycle matrix tests (every transition + every guard incl.
  Scheduled-update-allowed, Published/Archived-update-throws, Archived-delete-blocks, approval transitions); handler tests
  (schedule/approve/reject/submit-for-approval; publish blocked when flag on + not approved; flag off publishes; fake-repo style
  mirroring existing handler tests); sweep tests mirroring `StagedFileSweeperTests` (due/not-due/no-due-date candidates, error isolation);
  bUnit extensions (Index status options + row actions incl. flag-on Draft behavior; Detail approval section flag-gated); create/update
  handler test extensions for ArchiveGraceDays threading.

### Expected files

**No other file may change.** The list below is the reviewer's conformance baseline AND the tester-scope handover basis. Migration file
names carry the worker's actual timestamp (`<ts>`); the reviewer treats the pair + the snapshot regen as the three expected migration paths.
There are no sanctioned removals this round.

Created (27 — production):

- `src/Assignments/SchoolCollab.Assignments.Core/Domain/ApprovalStatus.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/Exceptions/AssignmentApprovalRequiredException.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/Events/AssignmentScheduledEvent.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/Events/AssignmentArchivedEvent.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/Events/AssignmentApprovalSubmittedEvent.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/Events/AssignmentApprovedEvent.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/Events/AssignmentRejectedEvent.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/ScheduleAssignmentCommand/ScheduleAssignmentCommand.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/ScheduleAssignmentCommand/ScheduleAssignmentCommandHandler.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/SubmitAssignmentForApprovalCommand/SubmitAssignmentForApprovalCommand.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/SubmitAssignmentForApprovalCommand/SubmitAssignmentForApprovalCommandHandler.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/ApproveAssignmentCommand/ApproveAssignmentCommand.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/ApproveAssignmentCommand/ApproveAssignmentCommandHandler.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/RejectAssignmentCommand/RejectAssignmentCommand.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/RejectAssignmentCommand/RejectAssignmentCommandHandler.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/ArchiveAssignmentCommand/ArchiveAssignmentCommand.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/ArchiveAssignmentCommand/ArchiveAssignmentCommandHandler.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/DTOs/AssignmentSweepCandidate.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Migrations/<ts>_AddAssignmentLifecycleAndApproval.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Migrations/<ts>_AddAssignmentLifecycleAndApproval.Designer.cs`
- `src/Assignments/SchoolCollab.Assignments.Api/Services/ScheduledPublishSweeper.cs`
- `src/Assignments/SchoolCollab.Assignments.Api/Services/ScheduledPublishSweepService.cs`
- `src/Assignments/SchoolCollab.Assignments.Api/Services/ArchiveSweeper.cs`
- `src/Assignments/SchoolCollab.Assignments.Api/Services/ArchiveSweepService.cs`
- `src/Assignments/SchoolCollab.Assignments.Api/AssignmentLifecycleSweepExtensions.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/ScheduleDialog.razor`
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/ScheduleDialogTypes.cs`

Created (9 — tests):

- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentLifecycleTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/ScheduleAssignmentCommandHandlerTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentApprovalCommandHandlerTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/PublishAssignmentApprovalGateTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentLifecycleSweepQueryTests.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/FakeFeatureFlagService.cs`
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentDetailBunitTests.cs`
- `tests/SchoolCollab.Assignments.Api.Tests.Unit/ScheduledPublishSweeperTests.cs`
- `tests/SchoolCollab.Assignments.Api.Tests.Unit/ArchiveSweeperTests.cs`

Modified (28 — production):

- `src/Assignments/SchoolCollab.Assignments.Core/Domain/AssignmentStatus.cs` (Scheduled = 3, Archived = 4 — additive)
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/Assignment.cs` (5 properties + lifecycle methods + guard widenings + param
  additions — additive except the three named guard changes from decision (a))
- `src/Assignments/SchoolCollab.Assignments.Core/Data/Configurations/AssignmentConfiguration.cs` (5 property declarations)
- `src/Assignments/SchoolCollab.Assignments.Core/Data/Repositories/IAssignmentRepository.cs` (2 candidate-query methods)
- `src/Assignments/SchoolCollab.Assignments.Core/Data/Repositories/AssignmentRepository.cs` (implementations + projection fields)
- `src/Assignments/SchoolCollab.Assignments.Core/DTOs/AssignmentSummary.cs` (5 trailing optional fields)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Queries/ListAssignmentsQuery/ListAssignmentsQueryHandler.cs` (DTO mapping)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Queries/GetAssignmentByIdQuery/GetAssignmentByIdQueryHandler.cs` (DTO
  mapping)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/CreateAssignmentCommand/CreateAssignmentCommand.cs` (trailing
  `ArchiveGraceDays = 30` param)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/CreateAssignmentCommand/CreateAssignmentCommandHandler.cs`
  (thread to `Create`)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/UpdateAssignmentCommand/UpdateAssignmentCommand.cs` (trailing
  `ArchiveGraceDays = 30` param)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/UpdateAssignmentCommand/UpdateAssignmentCommandHandler.cs`
  (thread to `Update`)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/PublishAssignmentCommand/PublishAssignmentCommandHandler.cs`
  (inject `IFeatureFlagService` + `Publish(approvalRequired)`)
- `src/Assignments/SchoolCollab.Assignments.Core/Migrations/AssignmentsDbContextModelSnapshot.cs` (regenerated by the migration)
- `src/Assignments/SchoolCollab.Assignments.Contracts/ContractTypes.cs` (`AssignmentStatusDto` +3/+4, `ApprovalStatusDto`,
  `AssignmentSummaryDto` trailing fields, requests gain `ArchiveGraceDays`, `ScheduleAssignmentRequest`, `ApproveAssignmentRequest`,
  `RejectAssignmentRequest`)
- `src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs` (4 new routes + `AssignmentApprovalRequiredException` catch)
- `src/Assignments/SchoolCollab.Assignments.Api/Program.cs` (`AddAssignmentLifecycleSweeps()` + `ApprovalStatusDto` JSON converter)
- `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentsApiClient.cs` (4 methods + `ApprovalStatusDto` converter)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Index.razor` (filter options + mapping + badge +
  row actions + flag-on Draft behavior + `OnSubmitForApprovalAsync`)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Detail.razor` (badge + Overview rows + Draft
  Schedule…/Publish-now/Scheduled Cancel + flag-gated approval section)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentEditFormModel.cs`
  (`ArchiveGraceDays` property + `LoadFrom` — additive)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Edit.razor` (status gate Draft OR Scheduled +
  inline request pass-through)
- `src/SchoolCollab.Core/Features/FeatureFlagKeys.cs` (new const)
- `src/SchoolCollab.MigrationService/Program.cs` (`SeedRequireAssignmentApprovalAsync` + its call)
- `src/AppHost/SchoolCollab.AppHost/Program.cs` (param + 2 `WithEnvironment` — assignments-api AND admin)
- `src/AppHost/SchoolCollab.AppHost/appsettings.json` (`Parameters:` entry)
- `src/SchoolCollab.Admin/appsettings.json` (cold-start fallback)
- `documents/configuration.md` (§2 + §5)

Modified (5 — tests):

- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentActivityGroupTests.cs` (the publish-handler construction sites gain the
  `FakeFeatureFlagService` ctor arg — one line each, no case changes)
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentIndexBunitTests.cs` (status options + row actions incl. flag-on Draft behavior)
- `tests/SchoolCollab.Assignments.Tests.Unit/CreateAssignmentCommandHandlerQuestionsTests.cs` (ArchiveGraceDays default + explicit
  threading cases)
- `tests/SchoolCollab.Assignments.Tests.Unit/UpdateAssignmentCommandHandlerQuestionsTests.cs` (ArchiveGraceDays threading cases)
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentFormModelMappingsTests.cs` (`LoadFrom` carries `ArchiveGraceDays` case)

**Total: 69 files (27 created production + 9 created tests + 28 modified production + 5 modified tests).**

### Implementation steps (ordered — follow literally; `dotnet build SchoolCollab.sln` after every step; run the named tests where the step says so)

**Step 1 — Domain: enum + exception + events + aggregate changes.** `AssignmentStatus.cs`: add `Scheduled = 3`, `Archived = 4` (additive,
XML docs on the new members). New `Domain/ApprovalStatus.cs`: `public enum ApprovalStatus { Pending = 0, Approved = 1, Rejected = 2 }`
(+ XML doc: null on the entity = not submitted). New `Domain/Exceptions/AssignmentApprovalRequiredException.cs`:
`public sealed class AssignmentApprovalRequiredException(string message) : Exception` — mirror
`AssignmentQuestionValidationException`'s shape exactly (base ctor, XML `<summary>` naming the approval gate). Five new event files in
`Domain/Events/`, each `public sealed record <Name>(Guid AssignmentId, string Title) : IDomainEvent` mirroring
`AssignmentClosedEvent` (the approval-stamp events `AssignmentApprovedEvent`/`AssignmentRejectedEvent` take
`(Guid AssignmentId, Guid ApproverId)` instead of Title — they are approval records, not broadcast notices; keep both consistent with
their aggregate call sites). In `Assignment.cs`: add the properties — `public DateTimeOffset? AvailableFromUtc { get; private set; }`,
`public int ArchiveGraceDays { get; private set; }`, `public ApprovalStatus? ApprovalStatus { get; private set; }`,
`public Guid? ApprovedBy { get; private set; }`, `public DateTimeOffset? ApprovedAt { get; private set; }` (XML docs per the existing
style, spec §3.5 citations); extend `Create(...)` with trailing `int archiveGraceDays = 30` (stamp `ArchiveGraceDays =
archiveGraceDays`); widen the `Update()` guard to `Status is not (AssignmentStatus.Draft or AssignmentStatus.Scheduled)` (message: "Only
draft or scheduled assignments can be updated."), add the trailing `int archiveGraceDays = 30` param (stamp it); rewrite `Publish()` per
decision (c) — signature `Publish(bool approvalRequired = false)`: Archived → `InvalidOperationException("Archived assignments are
read-only.")`, Published → existing no-op return, then the approval guard → `AssignmentApprovalRequiredException("This assignment
requires approval before it can be published.")`, then the existing Published transition; extend `Unpublish()` per (c) (Published OR
Scheduled → Draft; `AvailableFromUtc = null`; Archived throws); extend `Close()` (Archived throws; rest unchanged); add `Archive()`,
`Schedule(...)`, `SubmitForApproval()`, `Approve(...)`, `Reject(...)` per decision (c). Build.

**Step 2 — Contracts.** In `ContractTypes.cs` (additive + the one enum extension): `AssignmentStatusDto` gains `Scheduled = 3`,
`Archived = 4`; new `public enum ApprovalStatusDto { Pending = 0, Approved = 1, Rejected = 2 }` (int-mirrored; no
`JsonStringEnumConverter` here — it is registered in Api Program.cs + ApiClient per decision (h)); `AssignmentSummaryDto` gains the five
trailing optional params from decision (h) (after `UpdatedAt`; keep all existing params in order so existing positional construction
sites compile); `CreateAssignmentRequest` + `UpdateAssignmentRequest` gain trailing `int ArchiveGraceDays = 30`; new records
`ScheduleAssignmentRequest(DateTimeOffset AvailableFromUtc)`, `ApproveAssignmentRequest(Guid ApproverId)`,
`RejectAssignmentRequest(Guid ApproverId)` (+ XML docs citing spec §3.5 step 2 / §7 Q2). Build.

**Step 3 — EF configuration + migration.** In `AssignmentConfiguration.ConfigureTenantEntity`, next to the existing property
declarations: `builder.Property(x => x.AvailableFromUtc);`, `builder.Property(x => x.ArchiveGraceDays).HasDefaultValue(30);`,
`builder.Property(x => x.ApprovalStatus);`, `builder.Property(x => x.ApprovedBy);`, `builder.Property(x => x.ApprovedAt);`
(nullable enum → nullable int column automatically; comment the new block with the WS-A2 / spec §7 Q6 citation). Generate the migration
from the repository root:

```bash
dotnet ef migrations add AddAssignmentLifecycleAndApproval --project src/Assignments/SchoolCollab.Assignments.Core --context AssignmentsDbContext
```

Review `Up()`: purely additive — five `AddColumn` calls on the `assignment` table (no drops, no renames); `Down()` drops the five columns
in reverse. Then `dotnet test tests/SchoolCollab.Assignments.Tests.Unit --filter NoUncommittedModelChanges` — must pass before
continuing. Build.

**Step 4 — DTOs + queries + repository.** `DTOs/AssignmentSummary.cs`: append the five trailing optional fields
(`DateTimeOffset? AvailableFromUtc = null, int ArchiveGraceDays = 30, ApprovalStatus? ApprovalStatus = null, Guid? ApprovedBy = null,
DateTimeOffset? ApprovedAt = null`). New `DTOs/AssignmentSweepCandidate.cs`: `public sealed record AssignmentSweepCandidate(Guid Id, Guid
TenantId);` (+ XML doc naming the two sweeps). `AssignmentRepository`: extend the `ListAsync` projection with the five new fields (after
`a.UpdatedAt`); add the two candidate queries per decision (g) — `Db.Assignments.IgnoreQueryFilters(["Tenant"]).AsNoTracking().Where(a =>
a.Status == AssignmentStatus.Scheduled && a.AvailableFromUtc != null && a.AvailableFromUtc <= nowUtc).Select(a => new
AssignmentSweepCandidate(a.Id, a.TenantId)).ToListAsync(ct)` and the archive variant (`(a.Status == AssignmentStatus.Published ||
a.Status == AssignmentStatus.Closed) && a.DueDate != null && a.DueDate.Value.AddDays(a.ArchiveGraceDays) <= nowUtc`), each with the
sanctioned cross-tenant-read comment mirroring `StagedFileSweepService`; declare both on `IAssignmentRepository`. In
`ListAssignmentsQueryHandler` + `GetAssignmentByIdQueryHandler`: extend the `AssignmentSummaryDto` constructions with the five trailing
fields (`(ApprovalStatusDto?)s.ApprovalStatus` cast, rest pass-through). Build.

**Step 5 — Create/Update ArchiveGraceDays threading.** `CreateAssignmentCommand` + `UpdateAssignmentCommand` gain trailing
`int ArchiveGraceDays = 30` (mirroring the requests); the handlers pass it into `Create(..., ArchiveGraceDays: command.ArchiveGraceDays)`
/ `Update(..., ArchiveGraceDays: command.ArchiveGraceDays)` (the existing positional/named call style at each site). Build.

**Step 6 — Lifecycle commands + handlers + the publish approval gate.** Five new command folders per decision (f): each `public sealed
record <Name>(...) : ICommand;` (+ XML doc) + `public sealed class <Name>Handler(...) : ICommandHandler<Name>` using the
`CloseAssignmentCommandHandler` shape (primary constructor: `IAssignmentRepository repository, HybridCache cache,
ILogger<...> logger` — + `IFeatureFlagService featureFlags` ONLY on the schedule handler; approve/reject take the approver id from the
command; all end with `UpdateAsync` + `RemoveByTagAsync("assignments")` + `ClearDomainEvents` + structured log). The schedule handler
resolves the flag per decision (e) and calls `assignment.Schedule(command.AvailableFromUtc, approvalRequired)`. In
`PublishAssignmentCommandHandler`: add `IFeatureFlagService featureFlags` to the primary constructor, resolve `approvalRequired` before
`assignment.Publish(...)`, call `assignment.Publish(approvalRequired)`. Make the one-line ctor-arg addition to the publish-handler
construction sites in `AssignmentActivityGroupTests.cs` (the new `FakeFeatureFlagService`, default OFF — no case changes). Build; run
`dotnet test tests/SchoolCollab.Assignments.Tests.Unit --filter AssignmentActivityGroupTests` (existing publish coverage must stay green).

**Step 7 — Sweeps.** Four new Api files per decision (g). `Services/ScheduledPublishSweeper.cs` — `public static class
ScheduledPublishSweeper` with `public static async Task<int> PublishDueAsync(IReadOnlyList<AssignmentSweepCandidate> candidates,
ICommandHandler<PublishAssignmentCommand> publishHandler, ITenantContextAccessor tenantAccessor, ILogger logger, CancellationToken
cancellationToken = default)`: per candidate, inside a try/catch, `await tenantAccessor.RunWithExplicitTenantAsync(candidate.TenantId,
…)` around `publishHandler.HandleAsync(new PublishAssignmentCommand(candidate.Id, null), cancellationToken)`; catch `Exception ex` →
`logger.LogError(ex, "Auto-publish failed for assignment {Id}", candidate.Id)` and continue; return the success count. Mirror in
`Services/ArchiveSweeper.cs` (`ArchiveDueAsync`, dispatching `ArchiveAssignmentCommand(candidate.Id)`). The services —
`Services/ScheduledPublishSweepService.cs` + `Services/ArchiveSweepService.cs` — `: BackgroundService` with the
`StagedFileSweepService` loop shape (first sweep immediately; `try { SweepAsync } catch { log }` then `Task.Delay(interval)`; intervals
15 min / 24 h per decision (g)); each `SweepAsync` creates a scope, resolves `IAssignmentRepository` + the command handler +
`ITenantContextAccessor`, loads the candidates, early-returns on none, logs the candidate count, and delegates to the pure core.
`AssignmentLifecycleSweepExtensions.cs` — `public static class AssignmentLifecycleSweepExtensions { public static IServiceCollection
AddAssignmentLifecycleSweeps(this IServiceCollection services) { services.AddHostedService<ScheduledPublishSweepService>();
services.AddHostedService<ArchiveSweepService>(); return services; } }` (the `Add{Layer}()` rule; the StagedFileSweepExtensions precedent).
In `Api/Program.cs`: `builder.Services.AddAssignmentLifecycleSweeps();` next to `AddStagedFileSweep()`, and the
`JsonStringEnumConverter<ApprovalStatusDto>()` line in `ConfigureHttpJsonOptions`. Build.

**Step 8 — Feature flag: keys + seeder + AppHost + fallback + config doc.** `FeatureFlagKeys.cs`: the `RequireAssignmentApproval` const +
XML doc (gates the assignment approval workflow: publish + schedule handlers and the Assignments Index/Detail UI). MigrationService
`Program.cs`: static `SeedRequireAssignmentApprovalAsync(SettingsDbContext db, ILogger logger)` mirroring
`SeedEnableActivityGroupsAsync` (normalize `FeatureFlagKeys.RequireAssignmentApproval`, existence check, `FeatureFlag.Create(key,
"Require approval before assignment publish", null, isEnabled: false)`, log) + the call after `SeedEnableActivityGroupsAsync(settingsDb,
logger)`; NO pilot-tenant override. AppHost `Program.cs`: `var requireAssignmentApproval =
builder.AddParameter("feature-flag-require-assignment-approval");` near the other non-secret params, `.WithEnvironment(
"FeatureFlags__FEATURE__RequireAssignmentApproval", requireAssignmentApproval)` on the `assignmentsApi` block AND on the `admin`
block; `"feature-flag-require-assignment-approval": "false"` in `Parameters:`. `SchoolCollab.Admin/appsettings.json`:
`"RequireAssignmentApproval": "false"` in the `FeatureFlags.FEATURE` block. `documents/configuration.md`: §2 parameter row (Aspire
parameter, default `false`, injected as `FeatureFlags__FEATURE__RequireAssignmentApproval` into `assignments-api` and `admin`; note the
runtime authority is the Settings Config-service flag — the parameter is the cold-start value, per §5) + §5 "Runtime flags (current)"
table row (`FEATURE:RequireAssignmentApproval | false | Gates the assignment approval workflow (spec §7 Q2): when on, every assignment
requires approval before publish — the publish + schedule command handlers throw, and the Admin Assignments Index/Detail UI shows
Submit-for-approval/Approve/Reject. Seeded by the migration service; tenant-overridable via /config-flags. Cold-start fallback in
SchoolCollab.Admin/appsettings.json + AppHost parameter fan-out.`). Build.

**Step 9 — Endpoints + ApiClient.** In `AssignmentRoutes.cs`, inside the existing group after the `/close` route: the four routes per
decision (h) — each `[FromBody]` request (null-tolerant like publish), `Results.NoContent()` on success,
`catch (AssignmentApprovalRequiredException ex) { return Results.BadRequest(new { ex.Message }); }`,
`catch (AssignmentNotFoundException) { return Results.NotFound(); }`, and on schedule/unpublish-shaped guards
`catch (InvalidOperationException ex) { return Results.BadRequest(new { ex.Message }); }` (match the unpublish route's pattern). NO
archive route. In `AssignmentsApiClient.cs`: the four methods per decision (i) (schedule posts
`new ScheduleAssignmentRequest(availableFromUtc)`; approve/reject post their request records; all mirror `CloseAsync`'s
PostAsync/EnsureSuccess pattern) + the `JsonStringEnumConverter<ApprovalStatusDto>()` line in `_jsonOptions`. Build.

**Step 10 — UI: Index + Edit pass-through + ScheduleDialog.** `Index.razor` per decision (j): `@using SchoolCollab.Core.Features` +
`@inject IFeatureFlagService FeatureFlags`; in `OnInitializedAsync` resolve `_approvalRequired = await
FeatureFlags.IsEnabledAsync(FeatureFlagKeys.RequireAssignmentApproval, token)` (cache at load — NavMenu precedent; a resolution failure
logs and leaves the field false, never blocks the list); `_statusOptions` += `new(3, "Scheduled")`, `new(4, "Archived")`;
`LoadAssignmentsAsync` maps `3 → AssignmentStatusDto.Scheduled`, `4 → AssignmentStatusDto.Archived`; badge ternary per decision (j);
`BuildAssignmentActions`: `case AssignmentStatusDto.Scheduled:` → Edit + "Publish now" (`OnPublishAsync`) + "Unpublish"
(`OnUnpublishAsync`); `case AssignmentStatusDto.Archived:` → "Review" navigate (read-only); in the Draft case, when `_approvalRequired`
→ add `RowAction.Callback("Submit for Approval", () => OnSubmitForApprovalAsync(a.Id), FluentIcons.CheckmarkMark)` INSTEAD of Publish;
new `OnSubmitForApprovalAsync` mirroring `OnPublishAsync`'s optimistic-update pattern but updating
`ApprovalStatus` (`previous with { ApprovalStatus = ApprovalStatusDto.Pending }`) via `Api.SubmitForApprovalAsync(id)`.
`AssignmentEditFormModel.cs`: `public int ArchiveGraceDays { get; set; }` (+ XML doc: pass-through, no visible field, WS-A2) + `LoadFrom`
sets it from `assignment.ArchiveGraceDays`. `Edit.razor`: the non-editable status gate widens to `_item.Status is not
(AssignmentStatusDto.Draft or AssignmentStatusDto.Scheduled)` (message "Only Draft and Scheduled assignments can be edited."); the inline
`UpdateAssignmentRequest` gains `ArchiveGraceDays: _model.ArchiveGraceDays` (loaded from the DTO via `LoadFrom` — an edit never resets
it). New `ScheduleDialogTypes.cs` (`public sealed class ScheduleFormModel { [Required] public DateTime? AvailableFrom { get; set; } }` +
`public sealed record ScheduleResult(DateTimeOffset AvailableFromUtc);` with XML docs) + `ScheduleDialog.razor`:
`@inherits DialogShellBase<ScheduleFormModel, ScheduleResult>` mirroring `PublishDialog.razor`'s structure — a hint ("The assignment
publishes automatically at 00:00 UTC on the chosen date."), a `FluentDatePicker` bound to `Model.AvailableFrom` (the wizard DueDate
precedent), `DialogShellFooter`, `SubmitAsync` validates the date is set (else `Error = "Pick an available-from date."`) and returns
`new ScheduleResult(new DateTimeOffset(model.AvailableFrom.Value.Date, TimeSpan.Zero))`. Build.

**Step 11 — UI: Detail.** `Detail.razor` per decision (j): `@using SchoolCollab.Admin.Shared.Components` + `@using SchoolCollab.Core.Features`;
badge ternary extension (same mapping as Index); Overview grid gains "Available from" (`_item.AvailableFromUtc?.ToLocalTime().ToString("g")
?? "—"`) and "Archive after" (`_item.DueDate.HasValue ?
_item.DueDate.Value.AddDays(_item.ArchiveGraceDays).ToLocalTime().ToString("g") : "—"` with an inline grace hint, e.g.
`<span class="dialog-hint">(grace: @_item.ArchiveGraceDays days)</span>`); the Draft action block keeps Edit + Publish and adds
`FluentButton` "Schedule…" (`OpenScheduleDialogAsync`: `DialogService.ShowShellDialogAsync<ScheduleDialog, ScheduleFormModel,
ScheduleResult>(new ScheduleFormModel(), title: "Schedule assignment", size: DialogSize.Medium)` → `Api.ScheduleAsync(Id,
result.AvailableFromUtc)` → reload `_item` → `Nav.NavigateTo($"/assignments/{Id}", forceLoad: false)`); a new Scheduled block: "Publish
now" (`OpenPublishDialogAsync` — the existing flow works unchanged for Scheduled because publish-now is the same command) + "Cancel
schedule" (`Api.UnpublishAsync(Id)` → reload); a flag-gated approval section after the action buttons:

```razor
<FeatureFlagGate Key="@FeatureFlagKeys.RequireAssignmentApproval">
    @* approval panel: status chip + Submit-for-approval (Draft) / Approve+Reject confirmed (Pending) *@
</FeatureFlagGate>
```

panel contents per decision (j) — chip from a small local `ApprovalStatusText()` mapping (null → "Not submitted", Pending → "Pending",
Approved → "Approved", Rejected → "Rejected") on a `FluentBadge` (Pending → Outline, Approved → Accent, Rejected → Neutral, Not submitted
→ Lightweight); Draft → "Submit for Approval" button (`Api.SubmitForApprovalAsync(Id)` → reload `_item`); Pending → "Approve" +
"Reject" buttons, each wrapped in `await DialogService.ShowConfirmDialogAsync($"Approve assignment '{_item.Title}'?")` /
`"...Reject..."` (the destructive-action rule) then `Api.ApproveAsync(Id, Guid.Empty /* approver placeholder — wired to auth in a later
phase */)` / `Api.RejectAsync(Id, Guid.Empty ...)` → reload; Approved/Rejected → chip only (read-only); error handling + `OperationCanceledException`
swallows mirror the page's existing catch style. Archived rows render no Edit/Publish/Schedule blocks (the Draft/Scheduled conditions) —
verify no stale action shows. Build.

**Step 12 — Tests (write all, then run).** The 9 new + 5 extended test files per the binding coverage list below
(`FakeFeatureFlagService.cs` first — the other files use it). Conventions: MSTest `[TestClass]/[TestMethod]` + FluentAssertions; InMemory
via `ServiceCollection` + `AddTenancy()` + `SetTenant` where a DbContext is needed; `NullLogger<T>.Instance`; Moq only for non-HTTP seams;
hand-rolled fakes otherwise (the repo's fake style — `FakeNotificationPolicyResolver` precedent); bUnit per
`AssignmentIndexBunitTests` (RichardSzalay.MockHttp `ToHttpClient()`, `JSRuntimeMode.Loose`). Run `dotnet test
tests/SchoolCollab.Assignments.Tests.Unit` and `dotnet test tests/SchoolCollab.Assignments.Api.Tests.Unit` — **0 failures before
continuing**.

> **CLOCK NOTE:** steps 1–9 are the backend core — land them completely and green before touching the UI (steps 10–11). If the run clock
> runs short inside steps 10–12, land what is started in order, ensure the build is green, and REPORT the remainder as a deviation — the
> parent resumes the worker for the bounded remainder (ar-3/ar-4 precedents). Do not skip silently and do not reverse the order.

**Step 13 — Final verification.** `dotnet build SchoolCollab.sln -c Debug` (0 errors) and `dotnet test` on
`SchoolCollab.Assignments.Tests.Unit`, `SchoolCollab.Assignments.Api.Tests.Unit`, and `SchoolCollab.ArchitectureTests.Unit`
(repo-wide scanner — always) — 0 failures. No git commits — working tree only. Self-report to
`documents/rounds/.ar-5-worker-report.md` (scratch, untracked; the WORKER REPORT block format from the role contract).

### Binding test coverage list (names binding, file paths exact — MSTest + FluentAssertions)

`tests/SchoolCollab.Assignments.Tests.Unit/FakeFeatureFlagService.cs` (new — shared fake, the `FakeNotificationPolicyResolver`
standalone-file precedent): `public sealed class FakeFeatureFlagService : IFeatureFlagService` with a settable
`public bool IsEnabled { get; set; }` (sync `IsEnabled` returns it; `IsEnabledAsync` returns it; `GetAllFlags`/`GetAllFlagsAsync` return a
single-entry dictionary).

`tests/SchoolCollab.Assignments.Tests.Unit/AssignmentLifecycleTests.cs` (new — the domain matrix, `AssignmentTests` conventions):

- `Schedule_FromDraft_SetsStatusAndAvailableFrom`: Draft → `Schedule(future, approvalRequired: false)` → Status Scheduled,
  `AvailableFromUtc` stamped, `AssignmentScheduledEvent` enqueued, `UpdatedAt` bumped.
- `Schedule_RescheduleFromScheduled_Allowed` (second `Schedule` with a new future date updates the value) +
  `Schedule_FromPublished_Throws` + `Schedule_FromArchived_Throws` (InvalidOperationException).
- `Schedule_PastDate_Throws` (ArgumentException).
- `Schedule_ApprovalRequiredNotApproved_Throws` (flag-approval guard on schedule → `AssignmentApprovalRequiredException`); same call
  after `Approve` succeeds.
- `Publish_FlagOnNotApproved_Throws` + `Publish_FlagOnApproved_Publishes` + `Publish_FlagOffNotSubmitted_Publishes` (the approval-gate
  matrix on the domain method).
- `Publish_FromArchived_Throws` + `Publish_FromScheduled_Publishes` (publish-now ≡ early publish) + `Publish_AlreadyPublished_NoOp`
  (existing behavior pinned).
- `Unpublish_FromScheduled_ClearsAvailableFromUtc` (→ Draft, `AvailableFromUtc` null) + `Unpublish_FromPublished_ToDraft` (existing) +
  `Unpublish_FromArchived_Throws`.
- `Close_FromArchived_Throws` + `Close_FromPublished_Closes` (existing pinned).
- `Archive_FromPublished` + `Archive_FromClosed` + `Archive_Idempotent` + `Archive_FromDraft_Throws` + `Archive_FromScheduled_Throws`;
  `Archive` keeps `PublishedAt`/`DueDate`.
- `SubmitForApproval_FromDraft_SetsPending` + `SubmitForApproval_FromScheduled_Throws`; `Approve_FromPending_StampsApprover` (ApprovedBy +
  ApprovedAt set, `AssignmentApprovedEvent`) + `Approve_FromDraft_Throws` + `Approve_EmptyApprover_Throws` (ArgumentException);
  `Reject_FromPending_ClearsStamps` (Rejected, ApprovedBy/ApprovedAt null, `AssignmentRejectedEvent`) + `Reject_FromDraft_Throws`.
- `Update_FromScheduled_Allowed` (values applied, `ArchiveGraceDays` stamped) + `Update_FromPublished_Throws` (existing pinned) +
  `Update_FromArchived_Throws` + `Update_FromDraft_SetsArchiveGraceDays` (explicit non-default value round-trips; default 30 when the
  param is omitted).
- `Create_DefaultsArchiveGraceDaysTo30` + `Create_ExplicitArchiveGraceDays`.

`tests/SchoolCollab.Assignments.Tests.Unit/ScheduleAssignmentCommandHandlerTests.cs` (new — fake-repo handler style,
`AssignmentActivityGroupTests` scaffolding):

- Happy path: Draft assignment → `ScheduleAssignmentCommand(id, future)` with the fake flag OFF → repository captured the aggregate
  with Status Scheduled + `AvailableFromUtc`; cache-tag removal invoked.
- Flag ON + not approved → `AssignmentApprovalRequiredException` propagates; flag ON + approved assignment → scheduled.
- Unknown id → `AssignmentNotFoundException`.
- Past date → `ArgumentException` propagates (no mutation).

`tests/SchoolCollab.Assignments.Tests.Unit/AssignmentApprovalCommandHandlerTests.cs` (new — same scaffolding):

- `SubmitForApproval`: Draft → captured aggregate `ApprovalStatus == Pending` + `AssignmentApprovalSubmittedEvent`; Scheduled → throws.
- `Approve`: Pending → Approved + `ApprovedBy == approverId` + `ApprovedAt` non-null; Pending→Rejected path N/A here; unknown id → 404
  exception; Draft → throws.
- `Reject`: Pending → Rejected + stamps cleared; Draft → throws.

`tests/SchoolCollab.Assignments.Tests.Unit/PublishAssignmentApprovalGateTests.cs` (new — the publish handler with the fake repo set:

- Flag ON (`IsEnabled = true`) + Draft not-approved → `AssignmentApprovalRequiredException`, NOTHING published (no recipient/gate writes
  on the fake).
- Flag ON + approved (seed the aggregate via `SubmitForApproval` + `Approve` first) → publishes (existing publish-path assertions).
- Flag OFF (`IsEnabled = false`) + never-submitted Draft → publishes (today's behavior pinned — the flag default OFF).

`tests/SchoolCollab.Assignments.Tests.Unit/AssignmentLifecycleSweepQueryTests.cs` (new — InMemory `BuildContext()` +
`AddTenancy()`/`SetTenant` scaffolding, `IgnoreQueryFilters(["Tenant"])` reads):

- `ListScheduledForAutoPublishAsync`: Scheduled+due returns the candidate (id + correct `TenantId`); Scheduled+not-yet-due excluded;
  Scheduled+`AvailableFromUtc == null` excluded; Draft/Published/Archived excluded; two tenants' candidates both returned with their
  own `TenantId` (cross-tenant read correctness).
- `ListDueForArchiveAsync`: Published past `DueDate + ArchiveGraceDays` returned; Closed past grace returned; Published inside grace
  excluded; `DueDate == null` excluded (never archives); grace honored per-row (two rows, `ArchiveGraceDays` 30 vs 0 → only the 0-grace
  one due); Draft/Scheduled/Archived excluded.

`tests/SchoolCollab.Assignments.Api.Tests.Unit/ScheduledPublishSweeperTests.cs` (new — pure core, no DbContext, Moq/fake handler +
fake `ITenantContextAccessor`):

- Two candidates → handler receives `PublishAssignmentCommand(id, null)` per candidate, count 2.
- First candidate's handler throws → second still dispatched, count 1, no exception escapes (the error-isolation core).
- Empty candidate list → 0, handler never called.

`tests/SchoolCollab.Assignments.Api.Tests.Unit/ArchiveSweeperTests.cs` (new — same shape dispatching `ArchiveAssignmentCommand`):
due candidates archived, failing candidate isolated, empty list → 0.

`tests/SchoolCollab.Assignments.Tests.Unit/AssignmentDetailBunitTests.cs` (new — bUnit; MockHttp backend for `AssignmentsApiClient` +
`StudentsApiClient`, Moq `IDialogService`/`IFeatureFlagService` per the Index test conventions):

- Detail for a Scheduled assignment renders the Scheduled badge text + "Publish now" + "Cancel schedule" actions; "Available from"
  row shows the value.
- Detail for an Archived assignment renders the Archived badge and NO Edit/Publish/Schedule/Submit-for-approval controls (read-only).
- Approval section gated: flag OFF (mock returns false) → the approval panel absent; flag ON + Draft → "Submit for Approval" renders;
  flag ON + Pending → Approve + Reject render; approve click with the dialog mock declining → no API call; confirming → MockHttp asserts
  `POST /assignments/{id}/approve` fired and the page reloads (`ApproveAssignmentRequest` with `Guid.Empty` approver body).
- Approval chip texts: null → "Not submitted", Pending → "Pending", Approved → "Approved", Rejected → "Rejected".
- Draft + flag ON → "Schedule…" renders; the ScheduleDialog flow (drive `OpenScheduleDialogAsync` via the dialog-mock result) posts
  `POST /assignments/{id}/schedule` with `AvailableFromUtc` at 00:00 UTC of the chosen date.

`tests/SchoolCollab.Assignments.Tests.Unit/AssignmentIndexBunitTests.cs` (extend — existing cases untouched):

- Status filter options include "Scheduled" (3) and "Archived" (4); selecting Scheduled issues
  `GET /assignments?status=Scheduled`.
- Draft row, flag ON (mock `IFeatureFlagService` true) → "Submit for Approval" action present, "Publish" absent; clicking it POSTs
  `/assignments/{id}/submit-for-approval` and the row's chip updates; flag OFF → "Publish" present, submit absent (today's behavior).
- Scheduled row → Edit + Publish now + Unpublish; clicking Publish now POSTs `/assignments/{id}/publish`; Unpublish POSTs
  `/assignments/{id}/unpublish`. Archived row → Review action only.
- Badge appearance mapping: Scheduled + Archived rows render the mapped appearances (Outline / Neutral).

`tests/SchoolCollab.Assignments.Tests.Unit/CreateAssignmentCommandHandlerQuestionsTests.cs` (extend — existing cases untouched):
create without `ArchiveGraceDays` → persisted 30; create with `ArchiveGraceDays: 7` → persisted 7.

`tests/SchoolCollab.Assignments.Tests.Unit/UpdateAssignmentCommandHandlerQuestionsTests.cs` (extend — existing cases untouched):
update with `ArchiveGraceDays: 7` → captured aggregate `ArchiveGraceDays == 7`; update from a Scheduled assignment succeeds (guard
widening); update from Published still throws (pinned).

`tests/SchoolCollab.Assignments.Tests.Unit/AssignmentFormModelMappingsTests.cs` (extend — existing cases untouched):
`LoadFrom` carries `ArchiveGraceDays` onto the model (non-default DTO value round-trips; 30 when the DTO default applies).

`tests/SchoolCollab.Assignments.Tests.Unit/AssignmentActivityGroupTests.cs` (extend — construction sites only):
the `PublishAssignmentCommandHandler` sites gain the `FakeFeatureFlagService` (default OFF) ctor arg — one line each, no case changes.

### Constraints (repo AGENTS.md + rules — restated for the worker)

CPM — no `Version` on any `PackageReference`; this round adds **no packages** (MSTest, Moq, FluentAssertions, EFCore.InMemory, bunit,
RichardSzalay.MockHttp, FluentUI, HybridCache are all already referenced). net10.0. **No MediatR** — CQRS via `ICommandHandler<T[,R]>` +
Scrutor assembly scanning (the five new handlers auto-register; NO manual registration). MSTest + FluentAssertions + bUnit. Run
`dotnet build SchoolCollab.sln` after every change. **No git commits — working tree only.** Primary constructors for ctor injection; XML
`<summary>` docs on every public type/member; structured logging via `ILogger<T>` with named placeholders; **typed exceptions only** — no
`InvalidOperationException` from NEW command-surface code paths (domain state guards follow the existing aggregate pattern per decision
(c); `ArgumentException` only for argument hygiene; the NEW approval guard is the typed `AssignmentApprovalRequiredException` — the
ar-1 lesson). DTOs are records; domain entities factory/method-created. `AsNoTracking()` on read queries; no raw SQL.
`MigrationGuardTests.NoUncommittedModelChanges` must stay green (additive migration only; never edit an existing migration; `Down()`
implemented and inverse). EF migrations run from the repo root with the project + context flags as given in step 3. Blazor rules (the UI
ships): CSS isolation only (no inline `style=`, no `<style>` blocks, no `app.css` growth, `::deep` where needed); FluentUI components for
all controls; `@key` on every `@foreach`; `EventCallback` (not `Action`) for child→parent; `[Parameter, EditorRequired]` on required
parameters; destructive actions confirmed via `ShowConfirmDialogAsync` (never `ShowMessageBoxAsync`); no per-page `@rendermode`; dialogs
follow the dialog-ui skill (`DialogShellBase` + `ShowShellDialogAsync`, no nested dialogs). **The worker NEVER edits
`documents/rounds/round-ar-5-lifecycle.md`** — self-reports go to `documents/rounds/.ar-5-worker-report.md` (scratch, untracked).

### Worker task spec (commands + self-report)

- Build after every step; final: `dotnet build SchoolCollab.sln -c Debug` — 0 errors.
- Tests: `dotnet test tests/SchoolCollab.Assignments.Tests.Unit`; `dotnet test tests/SchoolCollab.Assignments.Api.Tests.Unit`;
  `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` (repo-wide scanner — always) — 0 failures.
- Changed files = the expected-files list exactly (69 files); report deviations from the plan one line each.
- Self-report (WORKER REPORT block from the role contract) to `documents/rounds/.ar-5-worker-report.md`; never the round doc.

### Acceptance criteria

Worker-facing:

- `dotnet build SchoolCollab.sln -c Debug`: 0 errors.
- `dotnet test` on `SchoolCollab.Assignments.Tests.Unit`, `SchoolCollab.Assignments.Api.Tests.Unit`, and
  `SchoolCollab.ArchitectureTests.Unit`: 0 failures (`NoUncommittedModelChanges` passes inside the Assignments run).
- Changed files = the expected-files list one-for-one (69: 27 created production + 9 created tests + 28 modified production + 5 modified
  tests); no unrelated deletions/reformatting; no new project/package/sln/CPM change; `Create.razor`, `wwwroot`, `Admin.Shared`
  unmodified; the round patch pathspec is `src`, `tests`, `documents/configuration.md`.
- The migration is additive (five AddColumns only) with a working inverse `Down()`.
- Every step-1-to-13 ordering followed; deviations reported, not silent.

Reviewer-facing (static, diff-only):

- **Plan conformance against the expected-files list, one-for-one** — any file outside the list (or missing from it) is a P1; the
  migration pair + snapshot regen count as the three expected migration paths.
- **Decisions (a)–(k) implemented as written**, including the two recorded adjustments (DELETE guard = test-pin only; schedule command
  has no dead `ContactIds` field; the `ToUpdateRequest` pass-through resolved onto `LoadFrom` + the inline Edit.razor request). The
  status-consumer sweep is complete: `Assignment.Update/Unpublish/Close/Publish`, `DeleteAssignmentCommandHandler` (verified
  Draft-only), `AssignmentRepository.ListAsync` (pass-through), both query handlers, `AssignmentStatusDto`, Index.razor (filter +
  mapping + badge + actions), Detail.razor (badge + actions), Edit.razor (status gate), `AssignmentsApiClient`/Api `Program.cs`
  (string-enum converters — new enum members serialize automatically). **No status-switch consumer missed.**
- **Best-practices check:** (1) no overwrites — the diff must not rewrite or delete pre-existing code outside the plan's scope (the
  named guard changes in `Assignment.cs` are the only pre-existing-logic edits beyond additive members; the one-line
  `AssignmentActivityGroupTests` ctor-arg additions are the only pre-existing test edits beyond named extensions); (2) repo skills
  honored — `dotnet-best-practices` (CPM, no MediatR, primary constructors, XML docs, structured logging, typed exceptions on NEW
  paths, no Console.WriteLine), `dialog-ui` (ScheduleDialog mirrors PublishDialog/DialogShell), `blazor-components` (CSS isolation,
  FluentUI, `@key`, EventCallback, confirm dialogs); (3) readability — naming matches repo conventions, minimal focused diff, no dead
  code.
- **Tenancy correctness:** the candidate queries use the sanctioned `IgnoreQueryFilters(["Tenant"])` read-only projection with the
  comment; dispatch wraps each candidate in `RunWithExplicitTenantAsync`; NO cross-tenant writes outside the explicit-tenant wrapper;
  the tenant filter itself (`ConfigureTenantQueryFilter`) is untouched.
- **Approval gate correctness:** the guard lives in the domain (`Publish`/`Schedule`), reached by handler-resolved flag state
  (`IFeatureFlagService`); `AssignmentApprovalRequiredException` → 400 on both publish and schedule routes; flag OFF behaves exactly as
  today (pinned by the OFF-path tests).
- **Flag plumbing:** const → MigrationService seed (OFF, idempotent, no pilot override) → AppHost param + fan-out to BOTH
  assignments-api and admin → Admin appsettings fallback → configuration.md §2/§5 rows with the exact key
  `FeatureFlags__FEATURE__RequireAssignmentApproval` (AGENTS.md: mapping lands in the same round).
- **Sweep structure mirrors the ar-4 precedent:** pure static core + BackgroundService + `Add{Layer}()` extension + per-candidate
  try/catch + interval constants; no `AddHostedService` inline in Program.cs.

Orchestrator-facing (accept verdict):

- `dotnet build SchoolCollab.sln -c Debug`: **0 errors** (authoritative parent re-run).
- `dotnet test` on the three projects: **0 failures** (authoritative parent re-run).
- Reviewer verdict PASS (or P2-only with all P2s triaged) + worker self-report reconciled against the tree.
- **P1 list empty → CLOSED**; otherwise the accept block names each P1 with its fix owner and the round stays open.
- Residual list refreshed (the pre-identified residuals below unless disproven).

### Residual risks / notes for acceptance

- **Worker clock:** the round spans domain + EF + 5 commands + 2 sweeps + config + 3 UI surfaces (~69 files). Backend-first ordering
  protects the core; a reported UI remainder resumes as a bounded second worker pass (ar-3/ar-4 precedents).
- **Approver identity is a placeholder:** `ApproverId` rides the request as `Guid.Empty` (the `ReviewAssignmentRequest.TeacherId`
  posture, identity gap D-6) — real approver-role authorization is a later identity round; the round records who approved only when a
  non-empty id arrives.
- **Flag OFF is today's behavior:** the approval flag ships dark (spec §7 Q2); the flag-on path is exercised by unit/bUnit tests, not by
  any seeded tenant — the UI tester should specifically hunt the flag-on Draft flow (Submit-for-approval visibility, publish 400
  surfacing).
- **Sweep intervals are constants** (15 min / 24 h — decision (g)); a configurable interval via AppHost parameters is a recorded
  follow-up, not a defect.
- **The BackgroundService loops are statically reviewed only** (the ar-4 precedent: the pure cores carry the unit tests — candidate
  selection + error isolation); the scheduled-publish sweep's real-provider timing behavior is a manual runbook item.
- **Archive export does not exist yet** (spec §7 Q6 — manual PDF/CSV export is Phase 5); archives are read-only rows retained
  indefinitely; the DELETE-block test pins the retention rule.
- **No integration events for the new transitions** (decisions (c)/(g) name domain events only) — a ward-facing "assignment archived"
  broadcast, if ever needed, is a Phase-2+ follow-up.
- **ar-1 EF owned-children verification residual** remains carried and untouched by this round.

### UI-round note (tester handover — derived by the parent AFTER acceptance)

This is a UI round (Index.razor, Detail.razor, ScheduleDialog.razor, Edit.razor gate). Per the role contract, after the worker + parent
verification and the accept verdict, the parent derives the tester-scope handover from the changed-file list (the changed files, the
pages/dialogs that render them, the ApiClient methods they call, navigation entry points — one line of rationale per entry) and the
deepseek-v4-flash tester fires against exactly that list, no more. The handover is written into this doc's UI Tester section by the
parent; the tester's scope is NOT this plan.

## Worker Report

(passes 1–3 — pass 3 completed via the build-escalation pattern, glm-5.3 on delegate per the user-mandated worker block rule; provenance recorded in SKILL.md Procedure step 3)

# AR-5 WORKER REPORT (final — consolidated passes 1–3)

Round: ar-5-lifecycle — Lifecycle extensions: Scheduled / Approval / Archive states + feature flag + Index/Detail surfaces (WS-A2)
Plan: documents/rounds/round-ar-5-lifecycle.md (never edited by the worker)
Passes: 1–2 (minimax-m3, production surface — parent-verified) + pass 3 (minimax-m3, interrupted mid-flight on the bUnit test batch; **completed by the ESCALATION worker model** per the build-escalation pattern, same brief).

## Step status (all DONE)

- Step 1 (Domain enum/exception/events/aggregate): DONE — 0 errors
- Step 2 (Contracts): DONE — 0 errors
- Step 3 (EF config + migration `20260907090343_AddAssignmentLifecycleAndApproval`): DONE — MigrationGuardTests green
- Step 4 (DTOs + queries + repository + sweep candidate queries): DONE — 0 errors
- Step 5 (Create/Update ArchiveGraceDays threading): DONE — 0 errors
- Step 6 (Lifecycle commands + handlers + publish approval gate): DONE — 0 errors
- Step 7 (Sweeps: ScheduledPublishSweeper/SweepService, ArchiveSweeper/SweepService, extensions, registered in Api Program.cs): DONE — 0 errors
- Step 8 (Feature flag: keys + MigrationService seeder + AppHost param/fan-out to assignments-api AND admin + Admin appsettings fallback + configuration.md §2/§5): DONE — 0 errors
- Step 9 (Endpoints + ApiClient): DONE — 0 errors
- Step 10 (UI: Index + Edit pass-through + ScheduleDialog): DONE — 0 errors
- Step 11 (UI: Detail): DONE — 0 errors
- Step 12 (Tests): DONE — 0 failures after the escalation pass (see Deviations 1–3)
- Step 13 (Final verification): DONE — build 0 errors; all three test projects 0 failures

Pass-3 interruption note: the normal worker hung mid-flight while investigating the 5 new failing bUnit tests (rendered the failure log, then launched a runaway filesystem-wide `find /`). The escalation pass reconciled build (0 errors) + re-ran the failing project, diagnosed the two real root causes from the failure-log markup (not render-count-0/DI), and fixed them.

## Root cause of the 5 failing bUnit tests (escalation-pass diagnosis)

All 5 failures were one page-level error messagebar (ErrorBoundary "Something went wrong") with two distinct causes visible in the assertion markup:

1. `FluentBadge Appearance needs to be one of Accent, Lightweight or Neutral.` — decision (j)'s `Appearance.Outline` is not a valid FluentBadge appearance in Fluent UI 4.14.2 (verified against the package XML docs). Both new pages rendered Scheduled/Pending badges with Outline → the badge component threw at render → the whole page collapsed into the error state. Genuine production defect (crashes in the running app too).
   Affected: `Detail_Scheduled_RendersScheduledBadgeAndActions`, `Detail_FlagOn_Pending_RendersApproveAndReject`, `Index_ScheduledRow_Renders`.
2. `<FluentMenuProvider /> needs to be added to the main layout (Parameter 'UseMenuService')` — the Index page renders Draft rows with 2+ actions → the kebab (RowActionsMenu → FluentMenu) uses the menu service, which requires a layout-level FluentMenuProvider. bUnit renders no layout, and menu items never render for markup assertions. The repo convention (RowActionsMenu doc + Subjects/Periods/ActivityGroups/GradeLevels/SubPeriods precedents) is `RowActionsUseMenuService="false"` on the page.
   Affected: `Index_FlagOn_DraftRow_ShowsSubmitForApproval_NotPublish`, `Index_FlagOff_DraftRow_ShowsPublish`.

## Changed files (one-for-one vs the 69-file expected list)

All 36 created files verified present on disk (27 production + 9 tests), the migration pair `20260907090343_AddAssignmentLifecycleAndApproval(.Designer).cs` + snapshot regen matching the three expected migration paths. Flag plumbing verified in place (FeatureFlagKeys const, MigrationService seeder, AppHost param + WithEnvironment to assignments-api AND admin, Admin appsettings fallback, documents/configuration.md rows). `Create.razor`, `wwwroot`, `Admin.Shared` unmodified; no sln/csproj/CPM change.

Escalation-pass changes (this pass, on top of the parent-verified passes 1–2 surface):

- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Index.razor` — (a) Scheduled badge `Appearance.Outline` → `Appearance.Accent`; (b) `RowActionsUseMenuService="false"` added to the LandingPage. [Both in-round modified files; deviations 1–2]
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Detail.razor` — Scheduled status badge `Outline` → `Accent`; approval-chip `Pending` `Outline` → `Neutral`. [In-round modified file; deviation 1]
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentIndexBunitTests.cs` — the 4 new lifecycle row tests now open the kebab before asserting menu items (FluentMenu renders items only when open — GradeLevelDetailPageTests precedent); flag-ON Draft test adds the binding-list click-through (Expect POST `/assignments/{id}/submit-for-approval` + `VerifyNoOutstandingExpectation`, Expect registered immediately before the click — MockHttp request expectations match BEFORE backend definitions, so an outstanding Expect 404s the initial list GET); Scheduled test asserts Edit + Publish now + Unpublish items; Archived test asserts the labeled Review button and no kebab. [In-round modified file]

No files outside the round patch pathspec (`src`, `tests`, `documents/configuration.md`) were touched.

## Build

`dotnet build SchoolCollab.sln -c Debug` — **0 errors**, 6 warnings (all pre-existing NuGet vulnerability advisories NU1902/NU1903 — unrelated to this round; no compiler warnings introduced).

## Tests (final runs, this pass)

| Project | Passed | Failed |
|---|---|---|
| SchoolCollab.Assignments.Tests.Unit | 330 | 0 |
| SchoolCollab.Assignments.Api.Tests.Unit | 14 | 0 |
| SchoolCollab.ArchitectureTests.Unit | 20 | 0 |

(Before the escalation pass: 325/5 — the 5 new bUnit failures above.)

## Deviations from plan (rationale one line each)

1. **Decision (j) badge appearance deviation (production, genuine defect):** `Appearance.Outline` is not a valid FluentBadge appearance (Fluent UI 4.14.2 validates Accent/Lightweight/Neutral and throws otherwise — surfaced by the failing tests as a page-crash). Scheduled now shares **Accent** with Published (both committed publishing states; badge text distinguishes), the Pending approval chip shares **Neutral** with Rejected (Approved keeps Accent, Not-submitted keeps Lightweight). Documented in inline comments at both sites.
2. **`RowActionsUseMenuService="false"` on Index.razor's LandingPage (production, convention):** not named in decision (j), but it is the established repo pattern for bUnit-tested landing pages (5 in-repo precedents; RowActionsMenu's own doc) and the only way the row-action items are assertable in bUnit markup.
3. **Test-side kebab interaction:** the binding-list row-action assertions require opening the kebab first (FluentMenu renders items only when open). The MockHttp Expect-before-backend ordering gotcha is documented in the test comment.
4. **(Inherited, passes 1–2, recorded) Two test files outside the expected list were necessarily touched:** `SubmissionEngineTests.cs` + `UpdateAssignmentCommandHandlerModuleResourceTests.cs` — their embedded fake repositories implement `IAssignmentRepository` (the 2 new sweep-query methods were stubbed) and their `PublishAssignmentCommandHandler` construction sites gained the `FakeFeatureFlagService` ctor arg; both changes are compile-required by the interface/ctor changes the plan did mandate. All other touched test files are on the expected list.
5. **(Inherited, passes 1–2, recorded) One existing test message updated:** the widened Update-guard expectation ("Only draft or scheduled") — direct consequence of decision (a).
6. **(Recorded residual — binding-list coverage gap):** the pass-3/escalation bUnit tests cover the new surfaces at render + action-list level; the binding list's dialog-mock interaction flows (Detail Approve/Reject decline-vs-confirm POST flows, Schedule-dialog POST flow, Index Publish-now/Unpublish click POSTs, status-filter GET-on-select) were not implemented as automated tests (FluentSelect option rendering is timing-sensitive in bUnit — asserted on the source options array instead, documented in the test). Command/handler/sweep-level behavior IS covered by the unit suites. The UI-tester pass should hunt these flows live.


## Review

(kimi-k2.7-code · static diff-only · first reviewer pass died on the ollama.com 429 quota wall after 49 turns with no findings produced; this block is the fresh re-dispatch's verdict · 2026-09-08)

REVIEW
Plan conformance: FAIL
P1: src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs:65 — Create route constructs CreateAssignmentCommand without forwarding req.ArchiveGraceDays, so non-default values are dropped and edits always reset the grace window to 30
P1: src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs:105 — Update route constructs UpdateAssignmentCommand without forwarding req.ArchiveGraceDays, breaking decision (j)'s "edit never resets it" guarantee
P1: src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs:160 — Publish route catches AssignmentApprovalRequiredException but not InvalidOperationException for the new Archived guard, so publishing an Archived assignment returns 500 instead of 400
P1: src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs:312 — Close route catches AssignmentNotFoundException only; the new InvalidOperationException for Archived assignments escapes as 500
P1: tests/SchoolCollab.Assignments.Tests.Unit/CreateAssignmentCommandHandlerQuestionsTests.cs:66 — expected ArchiveGraceDays default + explicit handler threading tests are absent and not covered elsewhere
P1: tests/SchoolCollab.Assignments.Tests.Unit/UpdateAssignmentCommandHandlerQuestionsTests.cs:81 — expected ArchiveGraceDays threading + Scheduled-update handler tests are absent and not covered elsewhere
P1: tests/SchoolCollab.Assignments.Tests.Unit/AssignmentFormModelMappingsTests.cs:53 — expected LoadFrom carries ArchiveGraceDays test is absent and not covered elsewhere
P1: tests/SchoolCollab.Assignments.Tests.Unit/AssignmentDetailBunitTests.cs:1 — plan-listed Schedule dialog POST flow, Approve/Reject confirm/decline flows, and approval chip-text matrix are not implemented
P1: tests/SchoolCollab.Assignments.Tests.Unit/AssignmentIndexBunitTests.cs:119 — plan-listed status-filter GET assertion and Publish-now/Unpublish POST click flows are not implemented
P2: src/Assignments/SchoolCollab.Assignments.Api/Services/ScheduledPublishSweepService.cs:29 — linked CancellationTokenSource is created but never referenced
P2: src/Assignments/SchoolCollab.Assignments.Api/Services/ArchiveSweepService.cs:29 — linked CancellationTokenSource is created but never referenced
P2: documents/configuration.md:682 — §11 env-var reference table omits the new Parameters:feature-flag-require-assignment-approval mapping
Best-practices: PASS
Test-conformance: FAIL
Verdict: FAIL

**Parent adjudication (same day):** all four route P1s independently CONFIRMED against actual code before rework dispatch — AssignmentRoutes.cs contains zero ArchiveGraceDays references (requests + commands + handlers all carry the field; the routes drop it at the boundary), and Publish()/Close() throw InvalidOperationException("Archived assignments are read-only.") while the publish/close routes catch neither → 500s. The five test P1s match the plan's binding test-coverage list and the worker's own deviation notes (render-level-only coverage). All 9 P1s stand; the 3 P2s accepted as rework cleanups. Rework iteration 1 of max 2 dispatched to the worker model per the build-escalation pattern (escalate only if the pass blocks).

### Re-verification (reviewer iteration 1): PASS

(kimi-k2.7-code · static re-verification of the rework pass · 2026-09-08 · transition rule applied: the escalation pass was executed by glm-5.3 before the user-set refinement (kimi=escalator, glm=reviews-escalated-work) took effect, so the mirror assignment preserved the escalator-never-reviews invariant)

REVIEW
Rework re-verification: PASS
P1: none
P2: none
Verdict: PASS

**Post-rework authoritative (parent):** build 0 errors; Assignments.Tests.Unit 347/0 (+17 net); Api.Tests.Unit 14/0; ArchitectureTests.Unit 20/0. Patch re-frozen: diffs-ar-5-lifecycle.patch, 70 files (the two files the first review flagged unchanged in the patch — CreateAssignmentCommandHandlerQuestionsTests.cs, AssignmentFormModelMappingsTests.cs — now carry their grace-days tests, explaining 68→70).

## Acceptance

(orchestrator · accept-phase adjudication · 2026-09-08 · verdict below; UI-tester scope handover at the end — the round ships `.razor`, so the tester fires)

### Criteria checklist

**Worker-facing:**

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| W1 | `dotnet build SchoolCollab.sln -c Debug`: 0 errors | **MET** | Parent-authoritative re-run: 0 errors (6 warnings — all pre-existing NU1902/NU1903 NuGet advisories; no compiler warnings introduced) — Re-verification block. |
| W2 | Tests 0 failures on Assignments.Tests.Unit / Api.Tests.Unit / ArchitectureTests.Unit | **MET** | Parent-authoritative: 347/0 (+17 net), 14/0, 20/0; `NoUncommittedModelChanges` green inside the 347. |
| W3 | Changed files one-for-one vs the 69-file expected list; no unrelated deletions; no project/package/sln/CPM change; `Create.razor`/`wwwroot`/`Admin.Shared` unmodified; pathspec `src`+`tests`+`documents/configuration.md` | **MET** | On-disk reconciliation (this pass): all 69 expected files present — incl. `documents/configuration.md` (M; §2 row L133, §5 rows L317+L352, §11 row L683) — plus only the 2 worker-documented compile-required extras (`SubmissionEngineTests.cs`, `UpdateAssignmentCommandHandlerModuleResourceTests.cs`); `git status` confirms zero csproj/sln/CPM/wwwroot/Admin.Shared/Create.razor entries; reviewer Best-practices PASS both passes. Frozen-patch artifact nit recorded in the scope reconciliation + residual 9. |
| W4 | Migration additive (five AddColumns) with working inverse `Down()` | **MET** | `20260907090343_AddAssignmentLifecycleAndApproval` — five AddColumns only, inverse `Down()`; MigrationGuard green; first review raised no migration finding; re-verification PASS. |
| W5 | Steps 1–13 in order; deviations reported, never silent | **MET** | Worker Report step table: 1–13 all DONE, backend-first (UI last); 6 deviations reported with rationale; both clock-cap interruptions escalated per the build-escalation pattern with provenance. |

**Reviewer-facing (adjudicated against the Review block):**

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| R1 | Plan conformance one-for-one vs the expected-files list | **MET** | First review FAIL (9 P1) → parent adjudication independently confirmed all 9 → rework fixed 12/12 findings → re-verification PASS (P1: none, P2: none). Migration pair + snapshot regen = the three expected migration paths, present. |
| R2 | Decisions (a)–(k) as written incl. the two recorded adjustments; status-consumer sweep complete | **MET** | Re-verification PASS; spot-check corroborates the reworked boundary (ArchiveGraceDays forwarded at `AssignmentRoutes.cs:76/116`; `InvalidOperationException` catches on the publish/close/schedule routes). Decision (j)'s `Appearance.Outline` was a plan defect — amended per deviation (a), adjudicated ACCEPTED below. |
| R3 | Best-practices: no overwrites; skills honored; readable | **MET** | Best-practices PASS in both review passes; ArchitectureTests.Unit 20/0 enforces the dotnet-best-practices "Never" list repo-wide. |
| R4 | Tenancy correctness (sanctioned cross-tenant read; explicit-tenant dispatch; tenant filter untouched) | **MET** | No tenancy finding in either review pass; re-verification PASS. |
| R5 | Approval-gate correctness (domain guard, handler-resolved flag, typed 400, OFF pinned) | **MET** | Re-verification PASS; OFF-path pinned by `PublishAssignmentApprovalGateTests`; typed `AssignmentApprovalRequiredException` → 400 on the publish + schedule routes. |
| R6 | Flag plumbing end-to-end + configuration.md §2/§5 in the same round | **MET** | Const → MigrationService seed (OFF, idempotent, no pilot override) → AppHost param + fan-out to assignments-api AND admin → Admin appsettings fallback → configuration.md §2/§5/§5-consumer/§11 rows (L133/L317/L352/L683 — the §11 row is the rework's P2 fix). |
| R7 | Sweep structure mirrors the ar-4 precedent | **MET** | Pure static cores + BackgroundServices + `AddAssignmentLifecycleSweeps()` extension (no inline AddHostedService); per-candidate try/catch; 15-min/24-h constants; dead-CTS P2s removed in rework. |

**Orchestrator-facing:**

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| O1 | Build 0 errors (authoritative parent re-run) | **MET** | 0 errors — Re-verification block. |
| O2 | Tests 0 failures (authoritative parent re-run) | **MET** | 347/0, 14/0, 20/0 — Re-verification block. |
| O3 | Reviewer PASS + worker self-report reconciled against the tree | **MET** | Re-verification PASS; reconciliation done in this pass (W3 + scope reconciliation below) — the on-disk set matches the worker report, the expected list, and the 2 documented extras. |
| O4 | P1 list empty → CLOSED | **MET** | Re-verification: `P1: none`. |
| O5 | Residual list refreshed | **MET** | Residual list below — pre-identified residuals confirmed, one new artifact nit added. |

### Authoritative build & test numbers (parent re-run — the round's source of truth)

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.sln -c Debug` | **0 errors** |
| `SchoolCollab.Assignments.Tests.Unit` | **347 passed, 0 failed** (+17 net over the round) |
| `SchoolCollab.Assignments.Api.Tests.Unit` | **14 passed, 0 failed** |
| `SchoolCollab.ArchitectureTests.Unit` | **20 passed, 0 failed** |

### Scope reconciliation (on-disk vs the expected list vs the frozen patch)

- **On disk (authoritative): 71 in-pathspec files = the plan's 69 expected one-for-one + 2 extras.** The extras — `tests/SchoolCollab.Assignments.Tests.Unit/SubmissionEngineTests.cs` + `tests/SchoolCollab.Assignments.Tests.Unit/UpdateAssignmentCommandHandlerModuleResourceTests.cs` — are the worker's documented compile-required deviations (their embedded fake repositories implement `IAssignmentRepository`; their `PublishAssignmentCommandHandler` construction sites gained the mandated `FakeFeatureFlagService` ctor arg): content is stub + ctor-arg only, no case changes. Adjudicated benign.
- **Frozen patch: 70 files = the on-disk set minus `documents/configuration.md`.** The config doc is modified on disk with all four required rows but was not captured in the re-frozen patch artifact (the 68→70 growth is the two grace-days test files the first review found unchanged, now carrying their tests — plan-expected content, previously missing). Artifact-refresh nit, not a worker non-conformance: the reviewer verified the config doc from disk in both passes (its §11 P2 + the rework fix prove it). Recommend re-freezing the patch with the config doc included before any squash/apply that consumes the artifact.
- **Unmodified guards confirmed:** `Create.razor`, `wwwroot/`, `Admin.Shared`, `.sln`, any `.csproj`, `Directory.Packages.props` — zero working-tree entries.
- **Out-of-round dirty paths (untouched, outside the pathspec):** the 9 pre-round untracked scratch files + the period-upsert round doc/patch; this round's own artifacts (round doc, patch, `.ar-5-scope.txt`, `.ar-5-worker-report.md`); the plan-phase pause note in `documents/solution/assignment-request-implementation-details.md` (parent artifact, predates the worker dispatch); the `.pi/skills/orchestrator-worker-reviewer/SKILL.md` provenance edit (parent-side). None are worker output.

### Rework provenance

Worker pass 3 and the rework pass both hit the 30-minute run cap and were completed via escalation passes (the build-escalation pattern; provenance recorded in the Worker Report header + SKILL.md Procedure step 3; the Re-verification block records the transition-rule mirror that preserved the escalator-never-reviews invariant). Reviewer loop: first review FAIL (9 P1 + 3 P2; parent adjudication independently confirmed all twelve) → rework iteration 1 fixed 12/12 findings → reviewer re-verification PASS. Reviewer iteration 1 of ≤2 — loop bounds respected; no further iterations needed. No git commits; the working tree carries everything unstaged (the 36 created files hold intent-to-add markers from the patch freeze — empty-blob index entries, no staged content).

### Deviations adjudicated

| Deviation | Verdict | Rationale |
|---|---|---|
| (a) `FluentBadge` `Appearance.Outline` → Accent/Neutral | **ACCEPTED** | Plan defect, not a worker defect: decision (j) named `Outline`, which Fluent UI 4.14.2 `FluentBadge` rejects (Accent/Lightweight/Neutral only) — a genuine page-crash production defect the new bUnit tests exposed. The worker's mapping (Scheduled shares Accent with Published, badge text distinguishes; the Pending approval chip shares Neutral with Rejected; inline comments at both sites) is the correct resolution. Decision (j) is amended accordingly in this accept record. |
| (b) `RowActionsUseMenuService="false"` on Index | **ACCEPTED** | Established repo convention (RowActionsMenu doc + 5 in-repo landing-page precedents) and the only way row-action items are assertable in bUnit markup; no behavior change beyond menu rendering mode. |
| (c) Escalation bUnit test-pattern corrections | **ACCEPTED** | In-repo-precedent corrections already adjudicated acceptable by the re-verification: kebab-open-before-assert, text-content selectors, STJ-serialized expected JSON, MockHttp Expect registered immediately before the click, SectionOutlet test host for the toolbar SectionContent. |
| (d) Inherited compile-required test-file updates (+ the widened-guard message expectation) | **ACCEPTED** | Direct compile consequences of plan-mandated interface/ctor changes; minimal stub + one-line ctor args; no case changes. |

### Residual list (accepted)

1. **ar-1 EF owned-children verification residual** — carried, unrelated to this round.
2. **Approver identity placeholder** — the UI passes `Guid.Empty` (D-6 identity gap; the `ReviewAssignmentRequest.TeacherId` posture); real approver-role authorization is a later identity round.
3. **Flag ON exercised by tests only** — no seeded tenant; the flag ships dark (spec §7 Q2). The UI tester's primary live hunt (handover directive below).
4. **Sweep intervals are constants** (15 min / 24 h) — a configurable interval via AppHost parameters is a recorded follow-up, not a defect.
5. **BackgroundService loops statically reviewed only** (ar-4 precedent — the pure cores carry the unit tests); real-provider sweep timing is a runbook item.
6. **Archive export does not exist** (Phase 5, spec §7 Q6); archives are read-only rows retained indefinitely — the DELETE-block test pins the retention rule.
7. **No integration events for the new transitions** (plan-out, decisions (c)/(g) name domain events only); a ward-facing broadcast, if ever needed, is Phase-2+.
8. **Real `ScheduleDialog` picker interaction** — the date→00:00-UTC conversion is covered via the mocked-dialog POST flow; the live `FluentDatePicker` interaction remains a tester hunt item.
9. **Patch-artifact refresh nit** — `documents/configuration.md` is modified on disk (all four rows verified) but absent from the re-frozen 70-file patch; refresh the artifact before any squash/apply that consumes it.

### Verdict

**CLOSED** — all worker-facing, reviewer-facing, and orchestrator-facing criteria MET; reviewer re-verification PASS; zero unaddressed P1s; both clock-cap interruptions resolved through the sanctioned escalation pattern with provenance; all four worker-reported deviations adjudicated ACCEPTED.

### UI-tester scope handover (UI round CONFIRMED — the patch ships `.razor`; the deepseek-v4-flash tester fires next; the tester's scope is exactly this list, no more)

**(a) Changed files to hunt (one line of rationale each):**

1. `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Index.razor` — hunt the new status filter (Scheduled/Archived options must issue the filtered `GET /assignments?status=…` and swap the grid), the badge mapping (Scheduled = Accent sharing Published's look — badge text must distinguish; Archived = Neutral), the row actions (Scheduled: Edit / Publish now / Unpublish; Archived: Review only), and the flag-gated Draft behavior (flag ON: "Submit for Approval" replaces "Publish"; optimistic ApprovalStatus chip flip + rollback on a failing POST).
2. `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Detail.razor` — hunt the flag-gated approval section (`FeatureFlagGate`: chip matrix Not submitted / Pending / Approved / Rejected; Draft "Submit for Approval"; Pending Approve/Reject each behind a confirm dialog — declining must fire no POST; every action must visibly reload the page state), the Scheduled actions ("Publish now" via the existing publish dialog; "Cancel schedule" → Unpublish → Draft), the Overview rows ("Available from"; "Archive after" + the inline grace-day hint), and Archived read-only (no Edit/Publish/Schedule/approval controls render).
3. `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/ScheduleDialog.razor` + `ScheduleDialogTypes.cs` — hunt the real dialog: the `FluentDatePicker` interaction and the live date→00:00-UTC conversion (the automated coverage uses a mocked dialog — residual 8), the unset-date validation ("Pick an available-from date."), the `POST /{id}/schedule` body at 00:00 UTC of the chosen date, and the reload/navigation after confirm.
4. `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentEditFormModel.cs` + `Edit.razor` (the grace pass-through pair + the widened gate) — hunt the status gate (a Scheduled assignment must open the editor; Published/Archived must show "Only Draft and Scheduled assignments can be edited.") and the `ArchiveGraceDays` pass-through (an edit must NOT reset the grace window to 30; no visible field exists — verify via the Detail "Archive after" row after an edit).

**(b) Pages that render them (routes):** `/assignments` (Index landing — filter, badges, row actions, flag-gated Draft actions); `/assignments/{Id:guid}` (Detail — approval section, Schedule/Publish-now/Cancel-schedule actions, Overview rows, badges); `/assignments/{Id:guid}/edit` (the Edit status gate + grace pass-through surface, reached from the Index/Detail Edit actions).

**(c) ApiClient methods in play:** `ScheduleAsync` (schedule POST from the dialog), `SubmitForApprovalAsync` (flag-on Draft flow, Index + Detail), `ApproveAsync` / `RejectAsync` (Pending approval buttons, `Guid.Empty` approver body), `PublishAsync` (Scheduled "Publish now" row action + the Detail publish flow), `UnpublishAsync` ("Cancel schedule" + the Scheduled row action), `ListAsync` with status filter (the new filter options).

**(d) Navigation entry points:** Admin NavMenu "Assignments" / "All Assignments" → `/assignments`; NavMenu "New Assignment" → `/assignments/create` (a fresh Draft row feeds every Draft flow above); the Index row-action kebab and the Detail action buttons are the in-page entry points to each flow.

**Explicit scope statement:** the tester's scope is exactly this list, no more; anything outside it is an out-of-round observation for the parent. Directive from the plan's residual note: the flag ships dark — hunt the flag-ON Draft flow live (toggle `FEATURE:RequireAssignmentApproval` via the `/config-flags` tenant override or the AppHost parameter): Submit-for-approval visibility, publish-400 surfacing when unapproved, and the Approve/Reject lifecycle end to end.


## UI Tester

(deepseek-v4-flash · first tester pass · 2026-09-08)

UI TEST
Scope ack: hunted exactly the handed-over surfaces (Index filter/badges/row-actions/flag-on Draft; Detail approval section/Scheduled actions/Overview rows/Archived read-only; the live ScheduleDialog date→00:00-UTC flow; the Edit gate + ArchiveGraceDays pass-through). No plan conformance, no build/test runs, no writes.
Verdict: P1
P1: src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Index.razor:312-324 — flag-on Draft row action substitutes "Submit for Approval" for "Publish" unconditionally on ApprovalStatus; an already-APPROVED (unpublished) Draft shows "Submit for Approval" instead of "Publish", so it cannot be published from the Index, and clicking re-POSTs /submit-for-approval which (domain SubmitForApproval is Draft-only guard) resets ApprovalStatus Approved→Pending, silently revoking the approval. Fix: substitute only when flag-on && ApprovalStatus is not Approved.
P2: Index.razor:282-301 — the optimistic "ApprovalStatus chip flip + rollback" is a silent no-op in the rendered grid: no Index column/chip renders ApprovalStatus, so neither the optimistic Pending flip nor the failure rollback is visible to the user (only the POST executes). Consider an approval-status column/chip for the flag-on path.
P2: Detail.razor:137-142 — "Submit for Approval" is gated on ApprovalStatus is null, so a REJECTED (still-Draft) assignment shows only the "Rejected" chip in Detail with no resubmit path, while Index offers "Submit for Approval" for the same row — inconsistent resubmission surface for a rejected draft.
Out-of-round observations:
- Index search (OnSearchValueChanged) calls Api.ListAsync(null,...) and drops the active status filter, so searching with "Scheduled"/"Archived" selected swaps the grid to all-status results while the filter dropdown still shows the selected option (newly reachable by the round's server-side filter).
- Detail schedule/cancel-schedule errors render into the flag-gated "Approval" panel's _approvalError bar (functional, but an odd surface for schedule errors).

### Rework plan (tester iteration 1 of max 2 — parent-appended)

Fix the P1 + both P2s in one bounded pass; only Index.razor, Detail.razor, and their bUnit tests may change:
1. (P1) Index.razor BuildAssignmentActions flag-on branch: substitute "Submit for Approval" for "Publish" ONLY when the assignment's ApprovalStatus is NOT Approved (null/Pending/Rejected → Submit for Approval; Approved → Publish as on the flag-off path). An approved draft must be publishable from the Index, and no action may silently revoke an approval.
2. (P2) Give the flag-on grid a visible approval surface: render an approval-state chip (Not submitted/Pending/Approved/Rejected) for Draft rows when the flag is on — as a conditional column or appended to the Status column — so the optimistic Pending flip and its failure rollback are both visible.
3. (P2) Detail.razor: widen the "Submit for Approval" button gate from "ApprovalStatus is null" to "null or Rejected" so a rejected Draft has a resubmit path in Detail, consistent with the Index.
Tests: extend AssignmentIndexBunitTests (flag-on: Approved Draft row shows Publish — no approval revocation; approval chip renders + flips optimistically + rolls back) and AssignmentDetailBunitTests (Rejected Draft shows the Submit for Approval button). Then build + the three test projects green; append the rework section to .ar-5-worker-report.md. Tester re-verifies only these three fixes; the parent statically checks the rework diff. Out-of-round observations are parent backlog items, never rework.


### Re-verification (tester iteration 1): PASS

(deepseek-v4-flash · re-verified exactly the three rework fixes in source + against the 6 new tests · 2026-09-08)

UI TEST
Scope ack: Re-verified exactly the three rework fixes (Index P1 substitution gate, Index P2 conditional Approval column + chip, Detail P2 resubmit gate) in source and against the 6 new bUnit tests; no re-hunt, no plan conformance, no build/test runs.
Verdict: PASS
P1: (none)
P2: (none)
Out-of-round observations: Index shows "Submit for Approval" on a Pending Draft row while Detail shows Approve/Reject for the same state — per the explicit spec gate (null/Pending/Rejected → Submit), not a defect, but a cross-page action inconsistency the parent may want to note.

**Round verdict: CLOSED** — tester iteration 1 of max 2 used. All three fixes verified: the P1 gate routes
Approved Drafts to Publish (no silent approval revocation), the conditional Approval column makes the
optimistic flip + rollback visible (flag-off renders no column; grid template widened with a trailing auto),
and rejected Drafts have a Detail resubmit path. Both worker deviations judged justified.
Authoritative post-UI-rework: build 0 errors; Assignments 353/0; Api 14/0; Architecture 20/0.
Out-of-round backlog notes (never rework, recorded in the round log): Index search drops the active
status filter; schedule errors render in the approval panel's error bar; the cross-page Pending-Draft
action inconsistency noted above.
