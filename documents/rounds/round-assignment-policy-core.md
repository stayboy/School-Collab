tier 3 lean; provider ollama; orchestrator ollama-cloud/glm-5.3-flash; plan-review + worker ollama-cloud/deepseek-v4.1-flash; diff-review ollama-cloud/kimi-k2.7-code; round base 93d340cc9297db07398a9f47f68445df4ff1c782; tree clean EXCEPT one untracked file — documents/solution/assignment-policy-fields.md (the authoritative findings doc for this round, intentionally uncommitted input).

**Plan-gate deviation (recorded):** this round's plan review ran on `ollama-cloud/deepseek-v4.1-flash` — the worker's own model — under the 2026-09-24 lean default. The owner withdrew that default on 2026-09-30 ("plan reviewer cannot be the same as worker in tier 3 lean — use the diff reviewer as the plan reviewer"), effective **from the next round**; the rule is recorded in `.pi/skills/orchestrator-worker-reviewer/SKILL.md`. Its findings were therefore treated as parent-side analysis and folded into this Plan (P1 1-3, P2 1-5); the round was not re-gated.

# Round assignment-policy-core — Round A: assignment policy core (NO UI)

Implements Round A only of `documents/solution/assignment-policy-fields.md` (owner decisions
D1–D8, 2026-09-30 — do NOT re-litigate D1–D8). This round extends the half-built assignment
policy (`ar-8-signature-defaults`) from a single signature flag into the full per-grade
assignment policy **data/protocol layer**. No UI, no enforcement UI, no recipient caps.

**Round split (D8):** Round A = policy core (this doc). Round B = grade-detail UI
(`GradeAssignmentPolicyEditor` card, retire `GradeSignaturePolicyEditor`), create-wizard pre-fill
+ `Mandatory` lock, publish-time approval enforcement UI, recipient role caps.

---

## Plan

### (a) Goal

Give assignments a four-field, grade-overridable policy (`SignatureRequirement`
Disabled/Optional/Mandatory, `RequiresApprovalBeforePublish`, `MaxPrimaryContacts`,
`MaxCopyContacts`) mirroring the notification-policy consolidation pattern exactly:
shared nullable shape + effective shape with per-field `…FromOverride` flags + pure resolver in
**SchoolCollab.Core**, real columns on `TenantAssignmentPolicy` (Settings) and
`GradeAssignmentPolicy` (Students), widened CQRS/DTO/routes/clients, the Assignments HTTP
resolver interface replaced (`ISignatureDefaultResolver` → `IAssignmentPolicyResolver`), and the
approval flag reconciled (effective policy OR `FEATURE:RequireAssignmentApproval` for one
release — D3; retirement itself is planned, not executed).

### (b) Scope gates

**IN (Round A):**
1. `AssignmentPolicyFields` + `SignatureRequirementMode` + `EffectiveAssignmentPolicy` +
   `IEffectiveAssignmentPolicyResolver`/impl — all in `src/SchoolCollab.Core/AssignmentPolicies/`.
2. Extend both entities with the four new nullable columns (real columns, no JSON/owned type —
   mirrors `GradeNotificationPolicy.cs:22-30`) + **two additive EF migrations** (Settings,
   Students) with back-compat backfill `false→Disabled, true→Optional, null→null`.
3. Widen CQRS commands/queries, DTOs, route request records, `AssignmentPolicyApiClient`
   (Admin.Shared) and `StudentsApiClient` (Students.Application).
4. Replace `ISignatureDefaultResolver` with `IAssignmentPolicyResolver` (Assignments.Core
   interface, Assignments.Api HTTP impl) and update **all** callers.
5. Approval-flag reconciliation in `PublishAssignmentCommandHandler` +
   `ScheduleAssignmentCommandHandler` (effective policy OR existing flag, one release).
6. Docs: configuration §2 + feature-flag-workflow.md retirement-plan rows; findings doc gains an
   implementation record.
7. Unit tests (resolver, entities, handlers, reconciliation, HTTP resolver, migrations guards).
8. **Legacy-input compatibility on the wire (revised after the plan review — R2):** the two
   **server** request records accept the legacy input-only `requiresSignatureDefault` bool
   *alongside* the new field set (`true→Optional`, `false→Disabled`, `null→null` when the new field
   is absent), and both DTOs keep a `[JsonIgnore]` computed `RequiresSignatureDefault` property
   (`SignatureRequirement != Disabled`) so the existing UI compiles and the existing Admin wire
   assertions keep passing with no UI or UI-test edit in this round.

**OUT (Round B — a UI file in Round A means the wrong round):** any `.razor`/`.razor.css`/
`.css`/`.js` change; create-wizard pre-fill + `Mandatory` lock; publish-time approval
**enforcement surfaces**; recipient role caps (`NotificationRecipientFilter`); retiring
`GradeSignaturePolicyEditor` (Round B deletes the component **and** its `GradeSignaturePolicyEditorTests.cs`; in Round A that test file is deliberately **not touched**, because IN item 8 keeps its asserted request bodies valid); retiring the actual
`FEATURE:RequireAssignmentApproval` flag rows/artefacts.

### (c) Verified seams (read by the orchestrator, cite-worthy)

| Seam | Evidence |
|---|---|
| Consolidation pattern | `src/SchoolCollab.Core/Notifications/NotificationPolicyFields.cs` (null-semantics doc); `EffectiveNotificationPolicy.cs` (per-field flags); `EffectiveNotificationPolicyResolver.cs` (pure merge); entity-mirrors-fields-as-real-columns: `GradeNotificationPolicy.cs:22-30` |
| Tenant entity (half-built) | `src/Settings/SchoolCollab.Settings.Core/Domain/TenantAssignmentPolicy.cs` — single `bool RequiresSignatureDefault`, `BaseTenantEntityWithAudit` + `IHasRowVersion` |
| Grade entity (half-built) | `src/Students/SchoolCollab.Students.Core/Domain/GradeAssignmentPolicy.cs` — `bool? RequiresSignatureDefault` (null = inherit) |
| EF configurations | `src/Settings/SchoolCollab.Settings.Core/Data/Configurations/TenantAssignmentPolicyConfiguration.cs`; `src/Students/SchoolCollab.Students.Core/Data/Configurations/GradeAssignmentPolicyConfiguration.cs`; enum→string precedent `FeatureFlagConfiguration.cs:39` (`HasConversion<string>()`) |
| CQRS (Settings) | `UpsertTenantAssignmentPolicy.cs` (command record), `UpsertTenantAssignmentPolicyHandler.cs`, `GetTenantAssignmentPolicyHandler.cs`; DTO `Settings.Core/DTOs/TenantAssignmentPolicyDto.cs:9` (single bool) |
| CQRS (Students) | `UpsertGradeAssignmentPolicy.cs`, `UpsertGradeAssignmentPolicyHandler.cs` (grade-exists guard), `GetGradeAssignmentPolicyHandler.cs`; DTO `GradeAssignmentPolicyDto.cs:10-13` |
| Routes | `Settings…/Endpoints/AssignmentPolicyRoutes.cs:19-41` (GET/PUT + `UpsertAssignmentPolicyRequest(bool)`:41); `Students…/Endpoints/GradeLevelRoutes.cs:269-307` (grade GET/PUT + `UpsertGradeAssignmentPolicyRequest(bool?)`:307) |
| Clients | `src/SchoolCollab.Admin.Shared/Services/AssignmentPolicyApiClient.cs:16-24` (local DTO/rq mirrors — NO Settings ref, by design); `src/SchoolCollab.Students.Application/Services/StudentsApiClient.cs:1019-1033` (+ local request record) |
| Resolver to replace | `src/Assignments/SchoolCollab.Assignments.Core/Services/ISignatureDefaultResolver.cs:17-21`; HTTP impl `src/Assignments/SchoolCollab.Assignments.Api/Services/SignatureDefaultResolver.cs` (fail-open posture, 204 handling); DI `src/Assignments/SchoolCollab.Assignments.Api/Program.cs:130-131`; endpoint caller `src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs:80-83`; other doc references `IAiPromptPolicyResolver.cs:8`, `ISignatureConsentTextResolver.cs:10`, `IStudentDirectory.cs:23`, `SignatureConsentTextResolver.cs:15`, `AssignmentsApiClient.cs:92-100` |
| Approval flag seam | `Assignment.cs:94-95` (`ApprovalStatus` doc names only the flag), `:309` Publish gate; `PublishAssignmentCommandHandler.cs:40`, `ScheduleAssignmentCommandHandler.cs:29` (`IFeatureFlagService.IsEnabledAsync(FeatureFlagKeys.RequireAssignmentApproval)`); flag constant `SchoolCollab.Core/Features/FeatureFlagKeys.cs:71` |
| Existing tests to extend/replace | `tests/SchoolCollab.Settings.Tests.Unit/Domain/TenantAssignmentPolicyTests.cs` + `Handlers/TenantAssignmentPolicyHandlerTests.cs`; `tests/SchoolCollab.Students.Tests.Unit/Domain/GradeAssignmentPolicyTests.cs` + `GradeAssignmentPolicyHandlerTests.cs`; `tests/SchoolCollab.Assignments.Api.Tests.Unit/SignatureDefaultResolverTests.cs`; `tests/SchoolCollab.Assignments.Tests.Unit/FakeFeatureFlagService.cs` |
| Migrations precedent | Settings `20260910124751_AddTenantAssignmentPolicy.*`; Students `20260910125042_AddGradeAssignmentPolicy.*` (in each Core project's `Migrations/`) |

### (d) Expected files

| # | File | Role |
|---|---|---|
| 1 | `src/SchoolCollab.Core/AssignmentPolicies/SignatureRequirementMode.cs` | NEW — enum `Disabled`/`Optional`/`Mandatory` (D2) |
| 2 | `src/SchoolCollab.Core/AssignmentPolicies/AssignmentPolicyFields.cs` | NEW — shared nullable field shape; XML doc states per-role null semantics (grade=inherit / tenant=unset) |
| 3 | `src/SchoolCollab.Core/AssignmentPolicies/EffectiveAssignmentPolicy.cs` | NEW — resolved values + `SignatureRequirement`/`RequiresApprovalBeforePublish`/`MaxPrimaryContacts`/`MaxCopyContacts` + four `…FromOverride` flags |
| 4 | `src/SchoolCollab.Core/AssignmentPolicies/IEffectiveAssignmentPolicyResolver.cs` | NEW — pure iface `Resolve(tenantDefault, gradeOverride)` |
| 5 | `src/SchoolCollab.Core/AssignmentPolicies/EffectiveAssignmentPolicyResolver.cs` | NEW — merge `grade ?? tenant ?? built-in`; built-ins: `SignatureRequirement=Disabled` (old default-false), `RequiresApprovalBeforePublish=false`, caps `null` (uncapped) |
| 6 | `src/Settings/SchoolCollab.Settings.Core/Domain/TenantAssignmentPolicy.cs` | MODIFY — replace `bool RequiresSignatureDefault` with the four mapped fields (nullable; unset field = null); widen `Create`/`SetPolicy` |
| 7 | `src/Settings/SchoolCollab.Settings.Core/Data/Configurations/TenantAssignmentPolicyConfiguration.cs` | MODIFY — add 4 columns; `SignatureRequirementMode` via `HasConversion<string>()` |
| 8 | Settings migration trio | NEW — `AddAssignmentPolicyFields` + `.Designer.cs` + `SettingsDbContextModelSnapshot.cs` update; additive columns + backfill SQL (old bool column left orphaned — see risk R1). **The generated `DropColumn` for `requires_signature_default` must be deleted** and replaced with `ALTER COLUMN … SET DEFAULT false` (or `DROP NOT NULL`) mirrored in `Down()`: that column is `NOT NULL` today (`20260910124751_AddTenantAssignmentPolicy`), so once the property is un-mapped the create path would INSERT without it and fail `23502` for every tenant with no policy row (P1-2) |
| 9 | `src/Settings/SchoolCollab.Settings.Core/DTOs/TenantAssignmentPolicyDto.cs` | MODIFY — four nullable fields (row-absence still = 204) |
| 10 | `src/Settings/SchoolCollab.Settings.Core/CQRS/AssignmentPolicies/Commands/UpsertTenantAssignmentPolicy/UpsertTenantAssignmentPolicy.cs` + `UpsertTenantAssignmentPolicyHandler.cs` | MODIFY — command takes the field set; handler maps to entity |
| 11 | `src/Settings/SchoolCollab.Settings.Core/CQRS/AssignmentPolicies/Queries/GetTenantAssignmentPolicy/GetTenantAssignmentPolicyHandler.cs` | MODIFY — widened DTO projection |
| 12 | `src/Settings/SchoolCollab.Settings.Api/Endpoints/AssignmentPolicyRoutes.cs` | MODIFY — widen `UpsertAssignmentPolicyRequest` to four fields |
| 13 | `src/Students/SchoolCollab.Students.Core/Domain/GradeAssignmentPolicy.cs` | MODIFY — `bool?` → `SignatureRequirementMode?` + three new nullable fields; `SetOverride` widened |
| 14 | `src/Students/SchoolCollab.Students.Core/Data/Configurations/GradeAssignmentPolicyConfiguration.cs` | MODIFY — same as #7 |
| 15 | Students migration trio | NEW — `AddAssignmentPolicyFields` + Designer + `StudentsDbContextModelSnapshot.cs`; backfill `false→Disabled, true→Optional, null→null` |
| 16 | `src/Students/SchoolCollab.Students.Core/DTOs/GradeAssignmentPolicyDto.cs` | MODIFY — widened fields + `[JsonIgnore]` compat property (R2) |
| 17 | `src/Students/SchoolCollab.Students.Core/CQRS/GradeAssignmentPolicies/**` (command record + 2 handlers) | MODIFY — widened field set |
| 18 | `src/Students/SchoolCollab.Students.Api/Endpoints/GradeLevelRoutes.cs` | MODIFY — widen `UpsertGradeAssignmentPolicyRequest` (:307) + handler call sites (:286-288) |
| 19 | `src/SchoolCollab.Students.Application/Services/StudentsApiClient.cs` | MODIFY — widen local request record + add fields-based upsert; keep bool? signature as compat (R2) |
| 20 | `src/SchoolCollab.Admin.Shared/Services/AssignmentPolicyApiClient.cs` | MODIFY — widen local DTO/rq mirrors + fields-based `UpsertAsync`; bool overload kept compat (R2) |
| 21 | `src/Assignments/SchoolCollab.Assignments.Core/Services/IAssignmentPolicyResolver.cs` | NEW — `Task<EffectiveAssignmentPolicy> ResolveAsync(Guid? gradeLevelId, CancellationToken)` |
| 22 | `src/Assignments/SchoolCollab.Assignments.Core/Services/ISignatureDefaultResolver.cs` | DELETE |
| 23 | `src/Assignments/SchoolCollab.Assignments.Api/Services/AssignmentPolicyResolver.cs` | NEW — HTTP impl mirroring `SignatureDefaultResolver` fail-open posture (tenant fetch fail ⇒ unset; grade fail ⇒ inherit) |
| 24 | `src/Assignments/SchoolCollab.Assignments.Api/Services/SignatureDefaultResolver.cs` | DELETE |
| 25 | `src/Assignments/SchoolCollab.Assignments.Api/Program.cs` | MODIFY — DI swap (:130-131) |
| 26 | `src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs` | MODIFY — signature-default endpoint internals use resolver; **wire contract unchanged** (`requiresSignature = mode != Disabled`) so `AssignmentsApiClient` + `Create.razor` stay untouched until Round B |
| 27 | `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/PublishAssignmentCommand/PublishAssignmentCommandHandler.cs` | MODIFY — `approvalRequired = policy.RequiresApprovalBeforePublish == true \|\| flag` (D3 OR) |
| 28 | `…/ScheduleAssignmentCommand/ScheduleAssignmentCommandHandler.cs` | MODIFY — same OR |
| 29 | `src/Assignments/SchoolCollab.Assignments.Core/Domain/Assignment.cs` | MODIFY — XML-doc only (:94-95, :347): approval now sourced from effective policy OR flag (one release) |
| 30 | `src/Assignments/SchoolCollab.Assignments.Core/Services/IAiPromptPolicyResolver.cs`, `ISignatureConsentTextResolver.cs`, `IStudentDirectory.cs`; `src/Assignments/SchoolCollab.Assignments.Api/Services/SignatureConsentTextResolver.cs`; **`src/AI/SchoolCollab.AI.Server/Services/TenantAssignmentAiPromptProvider.cs:21`** | MODIFY — doc-comment references to the replaced type rename to `IAssignmentPolicyResolver` (textual only). The AI.Server file is a real hit AC6's grep would fail on (P1-3); `AssignmentsApiClient.cs:92-100` (`GetSignatureDefaultAsync`) references no deleted type and is **not** a row |
| 31 | `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentPolicies/EffectiveAssignmentPolicyResolverTests.cs` | NEW — resolver merge + flags + built-ins |
| 32 | `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentApprovalPolicyReconciliationTests.cs` | NEW — publish/schedule OR logic w/ fake flag service + fake policy resolver |
| 33 | `tests/SchoolCollab.Assignments.Api.Tests.Unit/AssignmentPolicyResolverTests.cs` | NEW (replaces `SignatureDefaultResolverTests.cs`) — scripted-HTTP-handler tests |
| 34 | `tests/SchoolCollab.Settings.Tests.Unit/Domain/TenantAssignmentPolicyTests.cs` + `Handlers/TenantAssignmentPolicyHandlerTests.cs` | MODIFY — widened field set |
| 35 | `tests/SchoolCollab.Students.Tests.Unit/Domain/GradeAssignmentPolicyTests.cs` + `GradeAssignmentPolicyHandlerTests.cs` | MODIFY — widened field set, null=inherit |
| 36 | Migration guard tests | VERIFY existing `NoUncommittedModelChanges` for `SettingsDbContext`/`StudentsDbContext` still green (add if absent) |
| 37 | `documents/solution/assignment-policy-fields.md` | MODIFY — append §8 "Round A implementation record" |
| 38 | `documents/configuration.md` (§2 rows for `FEATURE:RequireAssignmentApproval`) + `documents/solution/feature-flag-workflow.md` | MODIFY — mark flag **replaced-with-a-policy-field, retirement planned in Round B** (D3; one write-point per flag kind) |
| 39 | `tests/SchoolCollab.Admin.Tests.Unit/GradeSignaturePolicyEditorTests.cs` | VERIFY unchanged (no edit) — IN item 8 keeps its asserted request bodies valid, so the file stays as-is; Round B deletes it with the editor (P1-1) |
| 40 | `tests/SchoolCollab.Assignments.Tests.Unit/PublishAssignmentApprovalGateTests.cs`, `PublishAssignmentCommandHandlerDeepLinkTests.cs`, `SubmissionEngineTests.cs`, `AssignmentActivityGroupTests.cs`, `ScheduleAssignmentCommandHandlerTests.cs` | MODIFY — these construct the two approval handlers positionally; adding the resolver parameter breaks `dotnet build` unless every call site is updated (P2-1) |
| 41 | `tests/SchoolCollab.Assignments.Api.Tests.Unit/AssignmentSignatureDefaultRouteTests.cs` | NEW — TestHost route test (the `WardRoutesTests` precedent) pinning `requiresSignature = mode != Disabled` at the signature-default endpoint; a resolver unit test cannot reach an endpoint (P2-4) |

### (e) Ordered implementation steps (worker can follow standalone)

1. Read `documents/solution/assignment-policy-fields.md` (D1–D8, §4, §6), the notification
   pattern files in (c), and `.github/copilot/rules/dotnet-best-practices.md` +
   `.github/skills/dotnet-best-practices/SKILL.md` + `.github/skills/bounded-context/SKILL.md`.
2. Create Core shared shape (#1–#5) in `SchoolCollab.Core/AssignmentPolicies`. Null semantics are
   explicit per field in XML docs: grade null = inherit, tenant null = unset, built-in defaults as
   in #3/#5. Follow `NotificationPolicyFields`/`EffectiveNotificationPolicy` naming verbatim
   (`…FromOverride` suffixes, `Empty` static).
3. Settings: widen entity + configuration + DTO + CQRS + route request record (#6–#12). Tenant
   rows: field null = unset. Keep `UpsertTenantAssignmentPolicy` command record carrying the four
   nullable fields.
4. Students: widen entity + configuration + DTO + CQRS + route request record (#13–#18); keep the
   grade-exists guard.
5. Migrations: `dotnet ef migrations add AddAssignmentPolicyFields --project
   src/Settings/SchoolCollab.Settings.Core --context SettingsDbContext` and the Students
   equivalent (design-time factories exist — no connection string). Additive columns only +
   hand-reviewed backfill `migrationBuilder.Sql` for the legacy bool columns
   (`SignatureRequirementMode` text conversions). Implement `Down()` (drop the new columns; the
   orphaned legacy columns are NOT dropped until Round B — see R1). Commit Designer + snapshot.
6. Assignments: create `IAssignmentPolicyResolver` + HTTP `AssignmentPolicyResolver` (clone the
   fail-open posture of `SignatureDefaultResolver`), delete the two old types, swap DI, update the
   AssignmentRoutes endpoint internals, fix textual references (#30).
7. Approval reconciliation (#27–#29): both handlers resolve the effective policy for
   `assignment.GradeLevelId` and OR it with the flag. Resolver call is allowed to return the
   all-built-in policy on fetch failure — this must never turn approval ON silently (fail-open to
   `RequiresApprovalBeforePublish=false`, D3 posture preserved via the OR).
8. Clients (#19–#20), including the compat members (R2) so that **no UI file changes**.
9. Round-trip docs (#37–#38).
10. Full test matrix (#31–#36), including scripted-HTTP-handler style per
    `dotnet-best-practices` (no Moq).
11. `dotnet build SchoolCollab.slnx` (0 errors), then `dotnet test` (0 failures). Confirm
    `git diff --name-only` shows **no** `.razor`/`.razor.css`/`.css`/`.js` files.

### (f) Acceptance criteria (numbered, with discriminating evidence)

1. **Shared shape lives only in Core; no cross-context refs.** Evidence: files #1–#5 under
   `src/SchoolCollab.Core/AssignmentPolicies/`; `SchoolCollab.ArchitectureTests.Unit` green
   (no new inter-context project references).
2. **Resolver is pure and mirrors the notification merge.** Discriminating test:
   `EffectiveAssignmentPolicyResolverTests` — (a) grade override wins per field, (b) null field
   inherits tenant, (c) tenant null falls to exact built-ins (`Disabled`, `false`, null, null),
   (d) each `…FromOverride` flag true iff the grade field was non-null.
3. **Entities extended, tenancy baseline preserved.** Evidence: both entities still
   `BaseTenantEntityWithAudit` + `IHasRowVersion`; widened `TenantAssignmentPolicyTests` /
   `GradeAssignmentPolicyTests` cover factory + `SetPolicy`/`SetOverride` for all four fields and
   null=inherit semantics.
4. **Two additive migrations with back-compat mapping.** Evidence: Settings + Students migration
   trios (migration + Designer + updated snapshot) exist; backfill SQL maps `false→Disabled`,
   `true→Optional`, `null→null`; `NoUncommittedModelChanges` guard green in both contexts — specifically
   `tests/SchoolCollab.ArchitectureTests.Unit/MigrationGuardTests.cs` (`NoDomainDbContext_HasPendingModelChanges`)
   plus `tests/SchoolCollab.Students.Tests.Unit/MigrationGuardTests.cs`; the backfill SQL
   (`false→Disabled`, `true→Optional`, `null→null`) is asserted by a shipped-SQL regex test per
   migration (the `PeriodSequenceBackfillMigrationTests` precedent), not hand-review alone;
   legacy `requires_signature_default` stays present **and INSERT-safe** in `Up()` (DB default /
   nullable — (d)#8, P1-2), dropped in Round B (P2-2).
5. **End-to-end widen of CQRS/DTO/routes/clients.** Evidence: handler tests round-trip the four
   fields through `Upsert*/Get*` handlers (Settings + Students); PUT bodies carry the new
   field set; 204-when-unset behaviours unchanged.
6. **Resolver interface replaced.** Evidence: `ISignatureDefaultResolver`/
   `SignatureDefaultResolver` deleted, grep across `src/` finds zero references (including the
   AI.Server doc comment — (d)#30);
   `AssignmentPolicyResolverTests` (scripted `HttpMessageHandler`) prove fail-open resolution of
   `EffectiveAssignmentPolicy`; the create-wizard wire contract (`requiresSignature = mode != Disabled`)
   is pinned by the new TestHost route test `AssignmentSignatureDefaultRouteTests` at the
   `AssignmentRoutes` endpoint — a resolver unit test cannot reach an endpoint (P2-4).
7. **Approval reconciliation.** Discriminating test:
   `AssignmentApprovalPolicyReconciliationTests` — (a) policy `RequiresApprovalBeforePublish=true`
   + flag OFF ⇒ publish/schedule gated; (b) policy false + flag ON ⇒ gated; (c) both off ⇒ not
   gated; (d) resolver failure + flag OFF ⇒ **not** gated (fail-open).
8. **No UI files touched.** Evidence: `git diff --name-only` contains no `.razor`, `.razor.css`,
   `.css`, or `.js` path; `GradeSignaturePolicyEditor`, `Create.razor` untouched.
9. **Build + tests green.** Evidence: `dotnet build SchoolCollab.slnx` 0 errors;
   `dotnet test` 0 failures (CI command parity: no-arg build/test).
10. **Docs updated.** Evidence: findings doc §8 appended; `documents/configuration.md` §2 and
    `feature-flag-workflow.md` mark the flag as reconciled-with-policy-field / planned-retired.

### (g) Risks

- **R1 — legacy column lifecycle.** Round A leaves `requires_signature_default` in both tables
  as an orphaned column (additive-only migration → rule 8 backward-compat honoured, old app
  binaries keep working through the deploy window). Round B's migrations drop them. Do not
  re-map the property "just for safety" — that would create a second source of truth.
- **R2 — legacy-input compat (revised after the plan review, P1-1/P2-3).** Widened wire
  records would break `GradeSignaturePolicyEditor.razor` / `GradeLevelDetailPage` compiles, and
  Round A must not touch UI files. Therefore: local mirrors (`Admin.Shared` DTO, Students Core
  DTO) keep a `[JsonIgnore]`-marked computed `RequiresSignatureDefault` compat property, and the
  bool-based `UpsertAsync` / `UpsertGradeAssignmentPolicyAsync` overloads keep sending their
  **legacy** bodies; and the two **server** request records accept that input-only bool alongside
  the new field set (`true→Optional`, `false→Disabled`, `null→null` when the new field is absent).
  That keeps the existing Admin wire assertions green with no UI-test edit, and closes the deploy
  window in which an old-shaped PUT would otherwise bind all-null and silently clear a configured
  policy (P1-1, P2-3). Round B deletes every compat member with the retired editor.
- **R3 — fail-open approval posture.** The resolver's failure path yields
  `RequiresApprovalBeforePublish=false`; the OR keeps the flag as the second trigger. If a tenant
  relied on the flag only, nothing changes; tenants configuring the policy field must set the flag
  off themselves in Round B (documented in the config §2 row). While the flag is off the approval
  *surfaces* stay flag-gated (`Assignments.Application/Components/Pages/Assignments/Detail.razor:142`,
  `Index.razor:163`), so in Round A the new field is settable only through the API and is not
  UI-reachable — intended (D8); R3 and the config §2 row must say so (P2-5).
- **R4 — `MaxNotifications` interaction** (D5): role caps and the sendout-wide cap both bind in
  Round B's filter; Round A only stores the values — no behaviour risk yet.
- **R5 — migration name collision** with parallel branches touching the same snapshot files
  (`ModelSnapshot.cs` conflicts): if it happens, drop + re-add after merging per
  `.github/copilot/rules/ef-migrations.md` rule 7.

### Constraints honoured (cited)

- **No cross-context project references** — the shared shape lives in `SchoolCollab.Core`
  (AGENTS.md "Architecture reminders"; Admin.Shared deliberately re-declares mirrors, precedent
  `AssignmentPolicyApiClient.cs:14-16`).
- **CPM untouched** — no `Version` attributes on any `PackageReference`; no new packages expected
  (HTTP via already-referenced `Microsoft.Extensions.Http`; if one is genuinely required, it is
  added to `Directory.Packages.props` first — NU1008 guard).
- **Tenancy** — entities stay on `BaseTenantEntityWithAudit` + `IHasRowVersion` (`xmin` row
  version); tenant query filters unchanged; no CodedValue/override pattern (operational data).
- **Migrations**: EF Core, per-context `dotnet ef migrations add` from repo root
  (design-time factories exist), Designer + snapshot committed, `Down()` implemented,
  PascalCase `{Verb}{Entity}…` naming — `.github/copilot/rules/ef-migrations.md`.
- **null = inherit is explicit per field** on `AssignmentPolicyFields` (grade) and per-field
  "unset" on the tenant row, mirroring the `NotificationPolicyFields` doc comment.
- **Round hygiene** — per `documents/rounds/README.md`: this is the round's single
  `round-*.md`; no specs/round mixing.

---

## Worker Task Spec (compact)

ROLE: implement Round A exactly per **Plan (d)+(e)** above. Read, in order:
`documents/solution/assignment-policy-fields.md` (D1–D8 authoritative, do not re-litigate),
this doc's (c) seam table, `.github/skills/dotnet-best-practices/SKILL.md`,
`.github/copilot/rules/ef-migrations.md`.
Hard gates: no UI file; no cross-context `ProjectReference`; no `Version=` on any
`PackageReference`; entities keep `BaseTenantEntityWithAudit` + `IHasRowVersion`; delete
`ISignatureDefaultResolver`/`SignatureDefaultResolver` completely; keep old bool columns
in-migration (R1) and keep the client compat members (R2).
Build rule: `dotnet build SchoolCollab.slnx` after every code change batch
(AGENTS.md "Build verification"), fix errors before continuing.
Exit: all of Plan (f) acceptance criteria 1–9 demonstrable; report per AC.

## Reviewer Task Spec (compact, static diff-only)

ROLE: verify the delivered diff against this round doc only. Check: (1) no UI files in the diff;
(2) shared shape lives in `SchoolCollab.Core` only, no new cross-context refs; (3) both
migrations additive + Designer + snapshot committed + `Down()` implemented + backfill SQL matches
D8 mapping exactly; (4) null=inherit semantics present per field in wire + entity + resolver;
(5) resolver replacement complete (grep the deleted type names — zero hits in `src/`);
(6) reconciliation OR logic matches criterion 7 cases; (7) compat members are the only legacy
bool surfaces and are annotated for Round-B deletion; (8) build + full test run outputs green.
Do NOT run builds/tests yourself (static verification only) — cite worker-report outputs.
Flag any `.razor`/UI change as a blocker.

## Worker Report

**Worker** `ollama-cloud/deepseek-v4.1-flash` (async run `b48f33b4`). Build **0 errors** (17 pre-existing warnings). Affected-project matrix + Admin + Architecture all green — **the parent re-ran the authoritative pass and reproduced every count exactly**: Settings 535/0, Settings.Api 1/0, Students 616/0, Students.Api 13/0, Assignments 690/0, Assignments.Api 93/0, Admin 602/0, ArchitectureTests 72/0. Six deviations, all judged acceptable by the diff review: (1) type-level `JsonStringEnumConverter<SignatureRequirementMode>` on the shared enum (the `GeneratedQuestionType` precedent; DB still stores names via `HasConversion<string>()`); (2) the Students migration hand-fixed from an EF-scaffolded `RenameColumn` to `AddColumn` + three-valued backfill (a rename would have reinterpreted every legacy signature boolean as an approval flag); (3) two extra test files beyond (d) (route binding, R2 compat — test-only); (4) no cap-range validation (D5 → Round B); (5) two extra test projects run (still no repo-wide run); (6) one stale doc comment — **fixed in-round** as P2-1.

## Review

### Plan review (step 2b)

Ran 2026-09-30 on `ollama-cloud/deepseek-v4.1-flash` — the worker's own model under the then-current 2026-09-24 lean default. **Verdict: P1-BLOCK (3 × P1, 5 × P2).** The owner withdrew that default the same day (*"plan reviewer cannot be the same as worker in tier 3 lean — use the diff reviewer as the plan reviewer"*), effective from the next round, so this pass is recorded as **parent-side analysis rather than an independent gate** (see the round-doc deviation note). All eight findings were folded into `## Plan` **before the worker was dispatched**:

- **P1-1** — R2 kept the UI *compiling* but not its *tests*: the compat overloads re-shaped the request body, so `GradeSignaturePolicyEditorTests`' asserted bodies would fail and that file was outside the round's licence. Fixed by IN item 8 (legacy **input-only** bool on the server request records + a `[JsonIgnore]` computed property on both DTOs) → the test file is now a `VERIFY unchanged` row.
- **P1-2** — the Settings legacy column is `NOT NULL` with no default, so once the property is un-mapped the create path would INSERT without it and fail `23502` for every tenant with no policy row (invisible to InMemory tests). Fixed in (d)#8: delete the generated `DropColumn`, add `ALTER COLUMN … SET DEFAULT false` (mirrored in `Down()`).
- **P1-3** — AC6's "zero references" grep would have hit `src/AI/SchoolCollab.AI.Server/Services/TenantAssignmentAiPromptProvider.cs:21`, absent from the file list; (d)#30 re-pointed, and the `AssignmentsApiClient` entry (which references nothing) removed.
- **P2s** — positional construction of the two approval handlers in five test files (build-break), a named backfill-SQL assertion test plus the correct migration guards, the R2 deploy-window note, a TestHost route test for the `requiresSignature = mode != Disabled` wire contract (AC6 had overstated resolver-test reach), and the flag-gated-UI sentence in R3.

### Diff review

Ran 2026-09-30 on `ollama-cloud/kimi-k2.7-code` against the frozen patch `documents/rounds/diffs-assignment-policy-core.patch` — **verdict: PASS, no P1.** Independently verified: no UI path in the diff (and the editor, its tests, `GradeLevelDetailPage`, `Create.razor` untouched); the shared shape exists once, in `SchoolCollab.Core/AssignmentPolicies/`; no `.csproj`/CPM change and no `Version=`; `ISignatureDefaultResolver`/`SignatureDefaultResolver` have **zero** remaining references (AI.Server included) and DI is swapped to `IAssignmentPolicyResolver`; both migrations are additive with the exact D8 backfill, Designer + snapshot present, and the P1-2 `SET DEFAULT false` confirmed against the original NOT NULL migration; null=inherit per field across fields/entities/DTOs/resolver with built-ins exactly `Disabled`/`false`/null/null; the OR-reconciliation is non-nullable so a fail-open resolution can never turn approval ON; all nine R2 legacy surfaces are derived-only and annotated for Round-B deletion; repo best-practice rules hold (scripted MockHttp handlers rather than Moq for HTTP, cancellation tokens threaded, XML docs on public types, no `ConfigureAwait(false)`, no `Version=`).

**Two P2s:** (1) stale `<summary>` in `Settings.Api/AssignmentPolicyEndpoints.cs` (still "guardian-signature policy") — **fixed in-round**; (2) `.pi/skills/orchestrator-worker-reviewer/SKILL.md` modified outside the plan — **intentional and parent-side** (the owner's lean plan-gate model rule), deliberately excluded from the frozen patch and to be committed separately.

## Acceptance

**Verdict: CLOSED** — parent-transcribed (Tier 3 lean; no UI in this round, so no tester pass).

| AC | Evidence | Status |
|---|---|---|
| 1 shared shape in Core only; no cross-context ref | five new files under `src/SchoolCollab.Core/AssignmentPolicies/`; no `.csproj`/CPM in the diff; ArchitectureTests 72/0 | ✅ |
| 2 pure resolver mirrors the notification merge | `EffectiveAssignmentPolicyResolverTests` — override wins per field, null inherits, exact built-ins, per-field `…FromOverride` flags | ✅ |
| 3 entities extended, tenancy preserved | both still `BaseTenantEntityWithAudit, IHasRowVersion`; widened entity tests incl. explicit `false`/`0` ≠ unset and all-null restore | ✅ |
| 4 two additive migrations + D8 backfill | both trios + Designer + snapshots; `AssignmentPolicyFieldsMigrationTests` in **both** contexts regex-read the shipped SQL; `SET DEFAULT false` present; guards green | ✅ |
| 5 end-to-end widen | Settings/Students handler round-trips for all four fields; `GradeAssignmentPolicyRoutesTests`; `UpsertAssignmentPolicyRequestTests` | ✅ |
| 6 resolver replaced + wire contract | zero references; `AssignmentPolicyResolverTests` (fail-open, 204 handling); `AssignmentSignatureDefaultRouteTests` pins the wire form at the real endpoint | ✅ |
| 7 approval reconciliation | `AssignmentApprovalPolicyReconciliationTests` (8 tests: policy-only, flag-only, neither, fail-open) for **both** publish and schedule | ✅ |
| 8 no UI files | parent-verified: no `.razor`/`.razor.css`/`.css`/`.js` in the diff; editor + its tests untouched | ✅ |
| 9 build + affected tests green | parent pass: build 0 errors; 535 / 1 / 616 / 13 / 690 / 93 / 602 / 72 — all 0 failed | ✅ |
| 10 docs updated | findings §8; `configuration.md` §2 + §5 rows; `feature-flag-workflow.md` gains the retirement pattern | ✅ |

**Residuals (accepted, carried to Round B):** no repo-wide `dotnet test` was run, so **CI parity is not proven by this round** (the affected-project matrix + ArchitectureTests is); the two contact caps are stored but **unenforced and unvalidated** (D5); neither backfill is executed against a populated Postgres (no Testcontainers) — the shipped-SQL regex tests assert the SQL text and `MigrationGuardTests` the snapshot sync; the nine R2 compat members and the retired legacy columns must be deleted in Round B; `FEATURE:RequireAssignmentApproval` retirement is planned, not executed.

**Round hygiene:** patch frozen at `documents/rounds/diffs-assignment-policy-core.patch`; the session-level skill rule change is excluded from it; nothing committed or staged.

## UI Tester

_N/A — Tier 3 lean round (no UI scope): no tester is dispatched, per the skill's § "Tier 3 lean". Round B owns every UI surface (the grade-detail `GradeAssignmentPolicyEditor` grid + field dialog, the create-wizard pre-fill and `Mandatory` lock) and will run **full** Tier 3, so a UI-tester pass is due there._