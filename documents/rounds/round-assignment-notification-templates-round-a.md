Provider: Cline/clinepass (models: parent-as-worker = Cline agent; reviewer = `deepseek-v4.1-flash` — reviewer dispatch blocked by provider auth, see `## Review`) · **Tier 2, owner override** — this round carries an EF migration, a new contract surface and >4 files, none of which pass the Tier-2 file-level gates; the owner explicitly chose Light Tier 2 for Round A on 2026-10-17, recorded here per the skill's "there is no defined tier between 2 and 3" override clause.
**Status:** **CLOSED — PR #362 (merged as `d3328320`, squash; branch deleted).** The gh-stack "layer 2" expectation was later superseded — `gh stack` could not create the server-side stack object on this repo (merge reported "not submitted" after a successful submit), so B1 is an ordinary branch off `main`, merged with `gh pr merge --squash --delete-branch`.

# Round A — assignment-notification-templates: template store (settings-db)

Spec: `documents/specs/assignment-notification-templates.md` (§3.1, §3.2, §3.9, §4, §5, §8 workstream 1).
Preceded by Solo prep S1–S3 (same working tree, same PR).

## Plan

Round A delivers workstream 1 only: **template store + constraints + audit entity + Settings
endpoints + the settings-db migration**. It is the foundation every later workstream reads from.

In scope:

1. `TenantNotificationTemplate` (tenant-scoped, soft-delete, `xmin`), `TemplateConstraint`
   (owned child), `TemplateAuditEntry` (append-only, global — mirrors `FlagAuditEntry`).
2. Settings endpoints under `/api/settings/notification-templates`: list, get-one (204 = unset),
   upsert, reset (delete), audit trail.
3. One `settings-db` migration creating the three tables.
4. Unit tests for the spec §8 rows that belong to this slice.

Explicitly deferred (with the workstream that owns each):

| Deferred | Owned by |
|---|---|
| `GET …/applicable/{gradeLevelId}` (§5) | workstream 5 (grade-detail card, §3.11) |
| `NotificationTemplateApiClient` (Admin.Shared) | workstream 5 (F5) |
| Write-time `RichInstructionSanitizer` on save (§3.12) | workstream 5 (F5) — the sanitizer lives in `SchoolCollab.Admin.Shared`, which Settings.Api must not reference; the editor applies it client-side |
| Renderer / resolver / `NotificationLog.TemplateId` | workstream 2 (Round B) |
| `ShortLink` + Families `/l/{code}` + base-URL consumption | workstream 3 (Round B) — S1 only declares the parameter |
| Publish stamps + medium restriction | workstream 4 (Round B) |
| Sweep integration | workstream 6 (Round B) |

Design decisions taken in this round:

- **Constraints carry no audit columns/row version.** They are an owned child collection replaced
  wholesale on every template save; the parent template and `TemplateAuditEntry` hold the history.
  (An earlier cut inherited `BaseTenantEntityWithAudit`, which produced `is_deleted`/`deleted_at`
  columns with no matching query filter — a half-applied soft delete.)
- **The audit trail is keyed on `TemplateKey`, not `TemplateId`.** `TenantNotificationTemplates` is
  soft-deleted, so an id-based join erased a template's entire history the moment an admin reset it.
  §3.9 requires the trail to stay visible, so the query matches `"{Kind}:{Channel}"` and applies the
  tenant scope explicitly (the audit table is on the global allow-list).
- **WhatsApp is not a template channel.** §2 routes WhatsApp recipients at the SMS body, so a
  WhatsApp row would be a second, silently-ignored copy. `Create` rejects it.
- **Subject is dropped for SMS** (§5 "subject ignored for SMS") — enforced in the domain, on both
  create and update, so no caller can store one.
- **The unique index is filtered on `is_deleted = false`**, so a reset does not block re-creating the
  same kind × channel.
- **`Reason` is an optional extra field on the upsert body.** §5 documents `{ subject?, body,
  constraints }`; the audit entity carries a `Reason` column, so the field is additive and optional.

## Worker Report

Files changed (**new** = added by this round; S1–S3 files are listed for completeness):

| File | Change |
|---|---|
| `src/Settings/SchoolCollab.Settings.Core/Domain/NotificationTemplateKind.cs` **new** | 4-value kind enum, declared locally (no Assignments.Core reference) |
| `…/Domain/TemplateConstraintDimension.cs` **new** | 5 audience/content dimensions (Q15) |
| `…/Domain/TemplateChangeKind.cs` **new** | Created / Updated / Deleted |
| `…/Domain/TenantNotificationTemplate.cs` **new** | aggregate root; `Create` builds its own constraint rows so each carries the generated template id; WhatsApp + empty-body + SMS-subject guards |
| `…/Domain/TemplateConstraint.cs` **new** | owned child (Id, TenantId, TemplateId, Dimension, Value) |
| `…/Domain/TemplateAuditEntry.cs` **new** | append-only audit row; before/after subject/body/constraints + actor + reason |
| `…/DTOs/TenantNotificationTemplateDto.cs` **new** | wire shapes + `UpsertTenantNotificationTemplateRequest` (optional `Reason`) |
| `…/Services/TemplateAuditRecorder.cs` **new** | writes the audit row into the handler's `SettingsDbContext` |
| `…/Services/TemplateConstraintSummary.cs` **new** | deterministic (dimension-ordered) JSON summary shared by both handlers |
| `…/Data/Configurations/*.cs` ×3 **new** | table maps; principal-side relationship with field access; unique filtered index |
| `…/Data/SettingsDbContext.cs` | 3 `DbSet`s, `TemplateAuditEntry` on the global allow-list, 3 configurations |
| `…/Extensions.cs` | register `TemplateAuditRecorder` |
| `…/CQRS/NotificationTemplates/**` **new (9 files)** | 3 queries + handlers, 2 commands + handlers |
| `…/Migrations/20261017175725_AddTenantNotificationTemplates.cs` + `.Designer.cs` **new** | creates `tenant_notification_templates`, `template_constraints`, `template_audit_entries` |
| `…/Migrations/SettingsDbContextModelSnapshot.cs` | snapshot |
| `src/Settings/SchoolCollab.Settings.Api/Endpoints/NotificationTemplateRoutes.cs` **new** | 5 routes |
| `src/Settings/SchoolCollab.Settings.Api/NotificationTemplateEndpoints.cs` **new** | group + conditional OIDC authorization |
| `src/Settings/SchoolCollab.Settings.Api/Program.cs` | `MapNotificationTemplateEndpoints(featureFlags)` |
| `src/AppHost/SchoolCollab.AppHost/Program.cs` | S1 completion: `AddParameter("families-public-base-url")` + fan-out to `assignments-api` as `Families__PublicBaseUrl` |
| `tests/SchoolCollab.Settings.Tests.Unit/NotificationTemplateHandlerTests.cs` **new** | 13 tests |

Defects found and fixed during the round (all caught before the build went green):

1. **Create path wrote `Guid.Empty` as every constraint's `TemplateId`** — the factory now builds the
   constraint rows itself, after generating the template id.
2. **Constraint replacement relied on EF navigation severance** for a required FK — replaced with an
   explicit `RemoveRange(old)` + `AddRange(new)`.
3. **`template_constraints` had `is_deleted`/`deleted_at` with no query filter** (see Plan) — the
   entity no longer inherits the soft-deletable base; the snapshot/designer/migration were corrected
   and verified with `dotnet ef migrations has-pending-model-changes` → *"No changes have been made
   to the model since the last migration."*
4. **The audit trail vanished after a reset** (see Plan) — now keyed on `TemplateKey`.
5. **`TemplateAuditEntryDto` omitted the constraint summaries** §3.9 requires — added.
6. **S1 was incomplete**: `families-public-base-url` had JSON defaults but no `AddParameter`
   declaration. `AppHostDevParameterDefaultsArchitectureTests.EveryDevParameterDefault_IsDeclaredAsAnAppHostParameter`
   caught it — exactly the failure mode that guard exists for.
7. Wrong namespace imports (`SchoolCollab.Core.Abstractions` does not exist) and `ICommand<T>` used
   where `SchoolCollab.Core.CQRS.ICommand` is non-generic; plus removed `Id`/`TenantId` shadows that
   tripped CS0108 against `BaseTenantEntity`.
8. **The route layer had no tests at all**, while spec §8 specifies "Settings **endpoint** tests" for
   the audit row. The handler tests could not reach enum route binding, the 204-vs-200 get-one
   contract, the WhatsApp 400 arm, or the required-reason 400 — all of which live in the route.
   `NotificationTemplateRoutesTests` (9 wire tests, `AssignmentPolicyRoutesTests` harness, no
   database) closes it.

Verification:

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** (310 pre-existing warnings) |
| `dotnet test tests/SchoolCollab.Settings.Tests.Unit` | **592 passed, 0 failed** (13 new) |
| `dotnet test tests/SchoolCollab.Settings.Api.Tests.Unit` | **24 passed, 0 failed** (9 new) |
| `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` | **116 passed, 0 failed** |
| `dotnet ef migrations has-pending-model-changes` | no pending changes |
| MAUI head | not required — `SchoolCollab.Admin.Shared` untouched by this round |



## Review

**Reviewer dispatch failed.** The designated reviewer (`deepseek-v4.1-flash` via the
clinepass provider) was dispatched once through `team_spawn_teammate` /
`team_run_task` and returned `Unauthorized: Please make sure you're using the latest
version of Cline and re-authenticate your Cline account.` — the same provider
authentication block that aborted the S1–S3 reviewer run. The teammate was shut down
and no reviewer output exists.

**Fallback applied:** the parent performed the static diff review itself over
`documents/rounds/diffs-assignment-notification-templates-round-a.patch`, which is the
Tier-2 adjudication the skill assigns to the parent when no independent reviewer runs.
Recorded explicitly so this is not mistaken for independent review.

Findings, all fixed in this round:

| # | Severity | Finding | Fix |
|---|---|---|---|
| R1 | MAJOR | Create path stamped `Guid.Empty` as every constraint's `TemplateId`, orphaning the whole constraint set of a newly created template | `TenantNotificationTemplate.Create` now builds its own constraint rows after generating the template id; regression test `Upsert_CreatesRowAndConstraints_...` asserts `TemplateId == created.Id` |
| R2 | MAJOR | Audit trail disappeared after a reset: `GetTemplateAuditEntriesHandler` joined through the soft-deleted template table, so `DELETE` erased the visible history (§3.9 violation) | Query now matches `TemplateKey` + explicit `TenantId`; regression test `AuditTrail_SurvivesReset` |
| R3 | MAJOR | `template_constraints` inherited `BaseTenantEntityWithAudit`, emitting `is_deleted`/`deleted_at` with no query filter — a half-applied soft delete that would let "deleted" child rows stay visible | Entity reduced to `BaseTenantEntity`; migration + snapshot + designer corrected; `has-pending-model-changes` clean |
| R4 | MAJOR | S1 shipped `families-public-base-url` JSON defaults with no `AddParameter` declaration (S1 was reported complete) | `AddParameter` + `assignments-api` fan-out added; the architecture guard that catches exactly this now passes |
| R5 | MINOR | `TemplateAuditEntryDto` omitted the before/after constraint summaries §3.9 requires | Both fields added and mapped |
| R6 | MINOR | §5 says "subject ignored for SMS" but the handler stored whatever subject was sent | Domain drops the subject for non-email channels on create *and* update; test `Upsert_DropsTheSubjectForSmsTemplates` |
| R7 | MINOR | Two audit tests asserted by list index while `OccurredAt` can tie within one timer tick — latent flakiness | Assertions select by `ChangeKind` |
| R8 | MINOR | Wrong namespace import (`SchoolCollab.Core.Abstractions` does not exist) and `ICommand<T>` used for a non-generic interface | Corrected to `SchoolCollab.Core.Notifications` / `SchoolCollab.Core.CQRS.ICommand` |

Accepted residuals (documented, not fixed):

- **Audit tie-ordering.** Entries written inside one `DateTimeOffset` tick have no
  defined relative order. Harmless for the UI; a sequence column would make it total.
- **`reason` is a query parameter on `DELETE`**, not part of the §5 wire table. The
  audit entity has a `Reason` column and §5 does not document a delete body, so this
  is additive — but it is an undocumented surface.
- **Constraint rows are hard-deleted on reset** while the template is soft-deleted.
  Intentional: child rows have no soft-delete column, and the audit row records the
  full prior constraint summary.

**Verdict: PASS** — subject to the reviewer-unavailable caveat above.

## Acceptance

Scope check against the plan: every in-scope item delivered; every deferred item is
still deferred and correctly attributed to its owning workstream (no partial
implementations left behind). No behaviour outside the Settings context changed except
the AppHost parameter declaration S1 owed.

| Gate | Command | Result |
|---|---|---|
| Build | `dotnet build SchoolCollab.slnx` | 0 errors |
| Tests (handlers) | `dotnet test tests/SchoolCollab.Settings.Tests.Unit` | 592 passed / 0 failed |
| Tests (wire) | `dotnet test tests/SchoolCollab.Settings.Api.Tests.Unit` | 24 passed / 0 failed |
| Architecture | `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` | 116 passed / 0 failed |
| Model/snapshot parity | `dotnet ef migrations has-pending-model-changes` | no pending changes |

**Migrations gate.** This round adds
`20261017175725_AddTenantNotificationTemplates`. The "migrations always reach a human"
clause in AGENTS.md is scoped to **unattended** rounds; this round is attended, so
attendance is the human gate. Machine coverage for the risk that clause exists to catch:
`MigrationGuardTests.NoDomainDbContext_HasPendingModelChanges` (in CI via `dotnet test`)
plus a direct `has-pending-model-changes` run — both green. See "Decisions settled by the
grill round" below for the correction and its provenance.

### Decisions settled by the grill round (2026-10-17)

**Correction — the "migrations reach a human" clause does not gate this round.** I
initially recorded the migration as needing a separate name-level authorisation. That was
wrong, twice over:

- The clause lives inside AGENTS.md's **Unattended** paragraph (lines 247–256, added
  2026-10-07 in `27db5386`, PR #318, under "process: one-writer-per-tree and grill-me
  adoption"): *"**In an unattended round** the parent … and migrations, contract/wire
  shape, security and auth, tenant isolation, and deleting a public route **always reach
  a human** unless the owner pre-authorises that specific round by name."* Its subject is
  the **unattended** round. This round was **attended** throughout — the owner chose the
  tier, the scope, and answered every grill round. Attendance **is** the human gate; a
  second name-level sign-off on top of it is not required by any rule in the repo.
- The technical risk it exists to catch is already machine-enforced.
  `tests/SchoolCollab.ArchitectureTests.Unit/MigrationGuardTests.cs:190`
  (`NoDomainDbContext_HasPendingModelChanges`) builds every domain DbContext from its
  design-time factory and asserts `Database.HasPendingModelChanges() == false` — which
  fails exactly when a model change has no migration — and CI runs it via `dotnet test`
  (`ci.yml:34–43`).

Settled:

| # | Decision | Outcome |
|---|---|---|
| G1 | Commit gate | **Accepted.** Branch `feat/notification-templates-round-a` from `main`; two commits (S1–S3 prep, then Round A). |
| G2 | Migration authorisation | **Withdrawn** — not applicable to an attended round; the guard already covers parity. |
| G3 | Audit ordering | **No sequence column.** Repo precedent: `FeatureFlagQueryHandlers.cs:73` orders audit by `OccurredAt` descending, and `FlagAuditEntry` has none. A column here would make template auditing diverge from flag auditing for no user-visible gain. |
| G4 | `reason` on delete | **Required.** Aligned to the audited-delete precedent `ConfigFlagRoutes.cs:98` — `[FromQuery] string reason`, feeding the audit row. Blank now returns 400. |
| G5 | `applicable/{gradeLevelId}` | **Stays deferred** to workstream 5. |
| G6 | Reviewer | Account-level auth failure; see below. |

Round B gate and branch state (settled 2026-10-17, second grill round):

| # | Decision | Outcome |
|---|---|---|
| D1 | Endpoint tests | **Accepted.** 9 wire tests committed as the round's fourth commit. |
| D2 | Round B tier | **Full Tier 3**, overriding Round A's Tier 2. Round B `ALTER`s existing tables, rewrites the render path every send uses, adds a cross-context resolver, and carries two UI surfaces — the skill's "schema / endpoint / contract / multi-context risk" gate, with no machine check covering the render rewrite. |
| D3 | Reviewer before Round B | **Hard prerequisite.** Round B does not start until a real reviewer run (`cline-pass/mimo-v2.6-flash`) is possible. Round A closed on parent-transcribed review, which is its weakest link and must not repeat on the round that alters production tables and the send path. |
| D4 | Push / PR | **Superseded.** Originally "hold"; the owner then instructed "push to stack pr". Branch pushed as layer 1 of a `gh stack` train on `main`, PR #362 opened ready for review. |
| D5 | Round B slicing (B1/B2/B3 boundaries) | Next round's frontier — downstream of D2, since slicing granularity follows the tier. |


**Reviewer status — account-level auth failure, not a model problem.** The registry
(`.pi/skills/orchestrator-worker-reviewer/references/models.md`) does not list
`deepseek-4.1-flash` as any id (the id is `deepseek-v4.1-flash`, with "v"), and assigns
`deepseek-v4.1-flash` to the **worker** seat, not the reviewer. The clinepass reviewer per
that file is `cline-pass/mimo-v2.6-flash` (owner 2026-10-05); the light-tier reviewer is
`kimi-k2.7-code`; `cline-pass/kimi-k3` and `cline-pass/mimo-v2.5(-pro)` are the listed
substitutes. The earlier seat also violated the seat-diversity rule (reviewer must differ
from the worker's model).

Substituting the id cannot help: a bare liveness probe with no model selection
(`round-a-reviewer-2`, task `Reply with exactly: REVIEWER-ONLINE`) returned the identical
`Unauthorized: … re-authenticate your Cline account`, so the failure happens before any
model is chosen. **Next reviewer attempt must use `cline-pass/mimo-v2.6-flash` (or
`kimi-k2.7-code`) once Cline account auth is restored.**

**Scope note carried to workstream 5:** because the reset reason is now required, the
`/notification-templates` editor's Reset button must prompt for a reason before calling
DELETE (the `/config-flags` detail page already has that dialog to copy).

