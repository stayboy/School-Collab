# Round: drop the assignment Primary grade (`GradeLevelId`) + chip-based targets Add

**Status:** CLOSED with residual — delivered in the working tree (uncommitted; owner has not authorized a commit). UI-tester seat unavailable (timed out twice); see `## Acceptance` residuals.
**Tier:** 3 (full — schema + contract + multi-context; UI round)
**Provider:** pi/ollama (`ollama-cloud`)
**Models:** orchestrator `ollama-cloud/glm-5.3-flash` · plan-review `ollama-cloud/glm-5.3` (substitute for the unavailable `ollama/glm-5.3:cloud`; no `ollama` provider is registered) · worker `ollama-cloud/deepseek-v4.1-flash` · reviewer `ollama-cloud/kimi-k2.7-code` · UI tester `ollama-cloud/minimax-m3`
**Round base:** `870b4b0f314f239de400663b8c65a4cc10f8faca` (branch `main`)
**Tree dirty at round start:** YES — the previous feature (targets builder) is uncommitted on `main`; this round continues it. The round patch is the FULL feature diff from the base.
**Patch:** `documents/rounds/diffs-drop-primary-grade.patch`

## Context for the orchestrator

Owner decisions (already settled — do not re-open in the plan):

- Remove the **`GradeLevelId` ("Primary grade") concept completely** from an assignment. Rationale (owner): the assignment's recipients are defined by the audience targets, so a separately-authored primary grade is not needed. The `StudentsContactResolver` grade fallback, `ResolveSignatureDefaultAsync`, and grade-scoped question generation must be updated or removed accordingly.
- The targets Add dialog must use a **single value dropdown that appends the pick to a dismissible `<Chip>` row** (the shared `Chip.razor`, the `GuardianPickerDialog` precedent), replacing the current multi-select listbox. Submit returns every chip as an entry.
- Prior UI decisions still hold: one `SectionCard` with the header owning "Targets & audience"; the card's `FluentCard` is the single frame; the dialog opens at `DialogSize.Large`.

## Plan

> **Standalone-worker contract:** everything needed to implement lives in this section.
> The worker is NOT expected to have read the spec; where a spec idea is load-bearing it is
> restated here. Read the actual files before editing — line/section references were taken
> at round start (base `870b4b0`) and the tree is dirty (the targets-builder feature is
> uncommitted on `main`; this round continues it, so the round patch is the FULL feature
> diff from the base).

### P1. Goal and owner decisions (settled — do NOT re-open)

Remove the assignment's **`GradeLevelId` ("Primary grade") concept completely**. The
assignment's recipients are defined by its **audience targets** (`AssignmentTarget` child
rows, persisted by the previous round); a separately-authored primary grade is redundant.
Every write point, read point, projection, contract field and UI control that carries the
assignment's grade is edited or removed this round.

Also settled:

- **UI:** the targets **Add dialog** replaces its multi-select listboxes with a **single-value
  dropdown that appends the pick to a dismissible `<Chip>` row** (shared
  `src/SchoolCollab.Admin.Shared/Components/Chip.razor`; use the
  `GuardianPickerDialog.razor` chip-row as the interaction precedent →
  `src/Students/SchoolCollab.Students.Application/Components/Students/GuardianPickerDialog.razor`
  + its `.razor.css`). **Submit returns every chip as an entry** in the existing
  `TargetsAndAudienceDialogResult`.
- **Prior UI decisions hold:** one `SectionCard` whose header owns the title "Targets &
  audience" (the section renders no `<h3>` of its own); its `FluentCard` is the single frame;
  the Add dialog opens at **`DialogSize.Large`**. One compartmentalized page
  (`AssignmentAuthoring.razor`) for Create/Edit/View; disabled-with-reason, never hidden.
- **`TargetAudienceType` STAYS.** Only the grade half of the legacy compat pair is dropped
  (TGT-15's follow-up round was scoped to `GradeLevelId`). `Assignment.SyncDerivedTargeting()`
  (src/Assignments/SchoolCollab.Assignments.Core/Domain/Assignment.cs) keeps deriving
  `TargetAudienceType` from the target rows; the list surface keeps reading it.

**ADR bearing (documents/solution/adr-cross-module-calls.md):** this round *removes* one
cross-module hop (`assignments-api → students-api` `students/by-grade/{id}` roster fallback)
and widens one existing fail-open enrichment (the `grade-levels/{id}/teachers` leg goes from
one call to one call per grade target). No new write-path hop is added, so no architect
sign-off trigger. The teacher leg keeps its graceful-degradation posture
(catch `HttpRequestException`, warn, continue).

### P2. Exact scope

**IN — `GradeLevelId` removal sites (the complete enumeration found in the round-start sweep):**

| # | Site | What happens |
|---|---|---|
| 1 | `src/Assignments/SchoolCollab.Assignments.Core/Domain/Assignment.cs` — property (~line 35), `Create`/`Update` signatures (~lines 191, 266), the D-2 primary-grade guard inside `SetTargets` (~line 345–358), **the `SelectedGrades` requires-a-grade guards in `Create` (~line 159) and `Update` (~line 244) (`if (targetAudienceType == SelectedGrades && !gradeLevelId.HasValue) throw`), header comments; PLUS the stale doc comment in `src/Assignments/SchoolCollab.Assignments.Core/Domain/AssignmentActivityGroup.cs:10` that names `<c>Assignment.GradeLevelId</c>`** | Remove property, parameters and guards. **Disposition of the two `SelectedGrades` guards: DELETE them** — `SelectedGrades` no longer implies a grade argument, and `SyncDerivedTargeting()` re-derives the audience compat from the target rows anyway, so the invariant the guard protected is now maintained by the target set, not by a parameter. Rewrite the `AssignmentActivityGroup.cs:10` doc comment (it matches AC-2's gate). |
| 2 | `src/Assignments/SchoolCollab.Assignments.Core/Data/Configurations/AssignmentConfiguration.cs` — `builder.Property(x => x.GradeLevelId)` (~line 46) and index `ix_assignments_grade_level_id` (~line 93–96) | Both removed |
| 3 | New EF migration (see P4.1) | Drop column + index |
| 4 | `src/Assignments/SchoolCollab.Assignments.Core/DTOs/AssignmentSummary.cs` — `Guid? GradeLevelId` | Removed; **replaced by** `IReadOnlyList<Guid> TargetGradeIds` (the row's grade-target ids, needed by the list/approval/scoping seams — see P4.6) |
| 5 | `src/Assignments/SchoolCollab.Assignments.Core/DTOs/NotificationSweepCandidates.cs` — `Guid? GradeLevelId` on `AssignmentReminderSweepCandidate`, `AssignmentCompletionSweepCandidate`, `AssignmentOverdueSweepCandidate` | Renamed to `Guid? PolicyGradeId` carrying the **derived** policy-scope grade (P3 D-C) |
| 6 | `src/Assignments/SchoolCollab.Assignments.Core/Data/Repositories/AssignmentRepository.cs` — `ListAsync` summary projection (line ~28) and the three sweep-candidate projections (lines ~117, ~190, + overdue equivalent) | Project `TargetGradeIds`/derived `PolicyGradeId` instead |
| 7 | `src/Assignments/SchoolCollab.Assignments.Core/Data/Repositories/WardAssignmentProjectionRepository.cs` — summary projection (line ~25) | Same as #6 |
| 8 | `src/Assignments/SchoolCollab.Assignments.Core/Services/IContactResolver.cs` — `ResolveSubscribersRequest(Guid? GradeLevelId, …)` | Becomes `IReadOnlyList<Guid>? GradeLevelIds = null` (teacher-cohort leg only); doc comment rewritten — the by-grade roster fallback is gone |
| 9 | `src/Assignments/SchoolCollab.Assignments.Api/Services/StudentsContactResolver.cs` — by-grade fallback (`students/by-grade/{id}`, lines 27–33) and single-grade teacher leg (lines 61–77) | Fallback **removed**; teacher leg loops `request.GradeLevelIds` (deduped by id across grades), still fail-open per grade |
| 10 | `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/CreateAssignmentCommand/CreateAssignmentCommand.cs` + `CreateAssignmentCommandHandler.cs` — `Guid? GradeLevelId` (~command line 21, handler line 86) | Removed |
| 11 | same for `UpdateAssignmentCommand`/`UpdateAssignmentCommandHandler` (command line 22, handler line 48) | Removed |
| 12 | `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/DuplicateAssignmentCommand/DuplicateAssignmentCommandHandler.cs` (line ~60) | Clones no longer copy a grade |
| 13 | `.../PublishAssignmentCommand/PublishAssignmentCommandHandler.cs` — three legs (lines ~44, ~56, ~163) | All three re-derive from targets (P4.4) |
| 14 | `.../ScheduleAssignmentCommand/ScheduleAssignmentCommandHandler.cs` (line ~36) | Approval-policy leg re-derived from targets (P4.4) |
| 15 | `.../Queries/GetAssignmentByIdQuery/GetAssignmentByIdQueryHandler.cs` — policy cache key + resolution (lines ~73–93) **and the by-id teacher-scope read (lines ~139–145, `scope.Allows(summary.CreatedByTeacherId, summary.GradeLevelId, summary.TopicId)`)** | Policy leg re-derived from `assignment.Targets` (P4.6). **The scope leg replaces the single `summary.GradeLevelId` with the any-overlap rule over 
ewline`summary.TargetGradeIds`** — identical to `ListAssignmentsQueryHandler.ApplyScope` (P3/P4.6): visible iff creator matches, topic matches, OR **any** row grade-target id is in the teacher's taught grades; **fail-closed** (invisible) when a cached summary yields `TargetGradeIds == null`. Never `Allows(creator, DeriveGrade(ids), topic)` (that would wrongly narrow multi-grade visibility) and never drop the check (that would widen detail visibility). |
| 16 | `.../Queries/ListAssignmentsQuery/ListAssignmentsQueryHandler.cs` — per-grade approval map + `ApplyScope` + DTO mapping (lines ~79–165) | Re-derived per row from the projected grade-target ids (P4.6) |
| 16b | `src/Assignments/SchoolCollab.Assignments.Core/Data/Repositories/SubmissionRepository.cs` — `ListSubmissionsForReviewAsync` (lines ~125–129): projects `a.GradeLevelId` and calls `scope.Allows(a.CreatedByTeacherId, a.GradeLevelId, a.TopicId)` | **THIRD teacher-scope seam (missed by the first sweep).** Drop the projected grade column and fetch the row's grade-target id key-set (the same correlated subquery as #6); apply the **same any-overlap / fail-closed rule** as #15/#16: visible iff creator matches, topic matches, or ANY row grade-target id is in the teacher's taught grades; **fail-closed** (invisible) when the key-set is empty/null. |
| 17 | `src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs` — create/update bodies passing `req.GradeLevelId` (lines ~539, ~612) and `recipient-preview` (line ~450: `primaryGradeId` query param + the two policy resolutions + the `ResolveSubscribersRequest`) | Param dropped; preview re-derived from the posted constraint set (P4.5). The `/signature-default` route's `gradeLevelId` **query param STAYS** (it resolves a Students-grade policy generally) — only the authoring page's *caller* changes (P3 E) |
| 18 | `src/Assignments/SchoolCollab.Assignments.Contracts/ContractTypes.cs` — `AssignmentSummaryDto` (`Guid? GradeLevelId` line 125, `string? GradeName` line 126), `CreateAssignmentRequest` (line 191), `UpdateAssignmentRequest` (line 235) | All three fields removed. `AssignmentSummaryDto` gains `IReadOnlyList<Guid>? TargetGradeIds = null` |
| 19 | `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentsApiClient.cs` — `RecipientPreviewRequest.PrimaryGradeId` (lines ~19–23), its query-string emission (lines ~174–176), signature-default log wording | `PrimaryGradeId` removed from request + URL |
| 20 | `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentAuthoring.razor` — the "Primary grade" `FormRow` (lines ~171–186), `GradePickerReason`/`GradePickerDisabled` (~line 992–999), `GradeLevelId` derived prop (~line 940), `PolicyLinkHref` (~line 1018), the D-2 validation block in `ValidateForSave` (~line 2288–2294), `OnGradeLevelChangedAsync` (~line 1693), `ResolveSignatureDefaultAsync` call sites (~lines 1393, 1713, 1973), `LoadPickersAsync`'s grade seeding (~line 1524–1528), `SubjectPickerDisabled`/placeholder (`_selectedGradeLevel` reads), `CaptureSaveSnapshot`'s `GradeLevelId` arg (~line 1165), grade options seeding | See P4.7 — the picker goes; the internals that survive are re-keyed to the grade-target rows |
| 21 | `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentEditFormModel.cs` — `ToCreateRequest`/`ToUpdateRequest` `Guid? gradeLevelId` parameter + request-arg (lines ~469, ~525) and the snapshot signature | Parameter removed from both projections and from `CaptureSaveSnapshot` |
| 22 | `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionGenerationSection.razor` + `QuestionsDraftSection.razor` — `[Parameter] Guid? GradeLevelId` (~lines 111 / 133) and its `QuestionGenerationRequest` arg (~lines 210 / 234) | Parameter removed; the page stop-passing it (P4.7) |
| 23 | `src/AI/SchoolCollab.AI.Abstractions/AssignmentQuestionGenerationTypes.cs` — `QuestionGenerationRequest.GradeLevelId` (line ~36) | Removed. **Verified round-start:** zero consumers read it — `grep -rn GradeLevelId src/AI/SchoolCollab.AI.Server` returns nothing, so the removal is contract-tidying, not a behaviour change |
| 24 | `src/Assignments/SchoolCollab.Assignments.Worker/Services/ReminderSweeper.cs` (line ~95) | Reads `candidate.PolicyGradeId` (the renamed field #5) |
| 25 | UI dialog chip flow: `TargetsAndAudienceDialog.razor`, `TargetsAndAudienceDialogModel.cs`, `TargetsAndAudienceEntry.cs` (+ NEW `TargetsAndAudienceDialog.razor.css`) | See P4.8 |

**OUT of scope (explicitly do NOT touch):**

- Anything under `src/Students/`, `src/SchoolCollab.MigrationService/Seeding/` — every other
  `GradeLevelId` (enrollments, teachers, grade-stream bridge, grade policies) is a *different*
  grade concept. The `students/by-grade` and `grade-levels/{id}/teachers` endpoints stay.
- The historical Assignments migration files (`2026*_*.Designer.cs` of migrations **older than
  the newest**) — they are immutable records and keep describing `grade_level_id` as it was.
  Only the **current** `AssignmentsDbContextModelSnapshot.cs` and the new migration reflect the drop.
  The existing **`AddAssignmentTargets` backfill migration is NOT edited** — see P4.1.
- `tests/SchoolCollab.Assignments.Tests.Integration/AssignmentTargetBackfillMigrationTests.cs`
  is verify-only. **Fact (verified):** its forward leg is a **no-arg `MigrateAsync()` at line 76,
  i.e. migrate-to-LATEST**, so once the drop migration lands above `AddAssignmentTargets` that
  leg would run past the point the raw `grade_level_id` seeding expects. **Pin it to
  `AddAssignmentTargets`** (the raw seeding below that migration keeps working). Do not edit the
  test to migrate forward past the drop migration.
- Portals (`src/SchoolCollab.Portals`): verified round-start — no Python consumer reads an
  assignment grade.

**Affected tests (~27 files):**

1. `tests/SchoolCollab.Assignments.Api.Tests.Unit/AssignmentDraftRoutesTests.cs` (request arg line ~173)
2. `tests/SchoolCollab.Assignments.Api.Tests.Unit/AssignmentReaderPolicyRouteTests.cs` (scope-test row, lines ~605–617 — switch the row/scoping to `TargetGradeIds`)
3. `tests/SchoolCollab.Assignments.Api.Tests.Unit/AssignmentRecipientPreviewRouteTests.cs` (asserts `LastRequest!.GradeLevelId` — now `GradeLevelIds`)
4. `tests/SchoolCollab.Assignments.Api.Tests.Unit/AssignmentSignatureDefaultRouteTests.cs`
5. `tests/SchoolCollab.Assignments.Tests.Integration/AssignmentTargetBackfillMigrationTests.cs` (verify-only, P2 OUT)
6. `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentTests.cs` (line ~117)
7. `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentTargetTests.cs` (line ~132 — the auto-derive-primary-grade assertion is **deleted**; the derived-grade tests replace it, see P5)
8. `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentActivityGroupTests.cs` (TGT-9 leg tests, lines ~192–222)
9. `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentApprovalPolicyReconciliationTests.cs`
10. `tests/SchoolCollab.Assignments.Tests.Unit/PublishAssignmentApprovalGateTests.cs`
11. `tests/SchoolCollab.Assignments.Tests.Unit/PublishAssignmentCommandHandlerDeepLinkTests.cs`
12. `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentRequiresApprovalProjectionTests.cs` (fake resolver's `RequestedGradeLevelIds`, lines ~137–327)
13. `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentInstructionsTests.cs` (lines ~145, ~314)
14. `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentFormModelMappingsTests.cs` (line ~41)
15. `tests/SchoolCollab.Assignments.Tests.Unit/SignatureDefaultsTests.cs` (lines ~76–120)
16. `tests/SchoolCollab.Assignments.Tests.Unit/DuplicateAssignmentCommandHandlerTests.cs` (line ~202)
17. `tests/SchoolCollab.Assignments.Tests.Unit/CreateAssignmentCommandHandlerQuestionsTests.cs` (line ~91)
18. `tests/SchoolCollab.Assignments.Tests.Unit/CreateAssignmentCommandHandlerModuleResourceTests.cs` (line ~84)
19. `tests/SchoolCollab.Assignments.Tests.Unit/Handlers/CreateAssignmentCommandHandlerEntityCodeTests.cs` (line ~77)
20. `tests/SchoolCollab.Assignments.Tests.Unit/UpdateAssignmentCommandHandlerQuestionsTests.cs` (line ~101)
21. `tests/SchoolCollab.Assignments.Tests.Unit/UpdateAssignmentCommandHandlerModuleResourceTests.cs` (line ~117)
22. `tests/SchoolCollab.Assignments.Tests.Unit/SubmissionEngineTests.cs` (lines ~31–72 — keep `WithGradeTarget`, drop the entity grade)
23. `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentAuthoringBunitTests.cs` (grade-picker assertions, lines ~417–434, ~1957–1974 → replaced by the chip/derived-grade assertions)
23b. `tests/SchoolCollab.Assignments.Tests.Unit/GetSubmissionsForReviewScopeTests.cs` (scope row, line ~185)
23c. `tests/SchoolCollab.Assignments.Tests.Integration/TeacherScopeReviewQueuePostgresTests.cs` (line ~61) — the review-queue scope tests for the #16b `SubmissionRepository` seam
24. `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentCreateBunitTests.cs` (dialog helper, line ~225)
25. `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentDetailBunitTests.cs` (line ~181)
26. `tests/SchoolCollab.Assignments.Tests.Unit/QuestionGenerationSectionBunitTests.cs` (line ~97) + `QuestionsDraftSectionBunitTests.cs` (line ~106) — the `GradeLevelId` parameter renders are removed
27. `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentQuestionGeneratorTests.cs` — verify the generator never carried the grade (round-start check showed the request assembly lives in the sections
    and `AssignmentQuestionGenerator.GenerateAsync` passes the record through; update only if compilation requires)

Plus NEW tests (see P5).

### P3. The three called-out behaviours — what each becomes

Owner rationale: **the target resolver already covers grades** — the publish path resolves the
recipients from the target rows (`IAssignmentTargetResolver.ResolveStudentIdsAsync`), so the
grade fallback leg is redundant, and a policy authored per grade still applies where the
author's constraints name that grade.

- **D-C (new derivation — the single seam).** New internal helper
  `src/Assignments/SchoolCollab.Assignments.Core/Services/AssignmentPolicyScope.cs`:
  `public static Guid? DeriveGrade(IReadOnlyList<Guid> gradeTargetIds)` → **exactly one
  distinct grade-target id ⇒ that id; otherwise (zero, or two or more) ⇒ null (tenant-default
  policy)**. Every policy leg uses this one rule — never re-spell it at a call site. Same
  helper takes `Assignment` and filters `Kind == TargetKind.GradeLevel`.
  *(This is the retired D-2's replacement: instead of one author-maintained grade, the
  policy-scope grade is a pure function of the target rows.)*
- **A. `StudentsContactResolver` grade fallback** → **removed**. The publish path fail-closes on
  an empty resolved set before the resolver call, so the empty-`StudentIds`-by-grade path is
  dead. What survives: the **teacher-cohort leg**, re-keyed from `GradeLevelId` to
  `GradeLevelIds` (the assignment's distinct grade-target ids, passed at publish and at the
  preview). Different input: **yes** — the request record changes shape (P2 #8) and its two
  callers (publish handler, preview route) pass the target-derived grade-id list.
- **B. `ResolveSignatureDefaultAsync` (authoring page)** → **derived from targets**. The page
  keeps the method but derives its argument: `AssignmentPolicyScope.DeriveGrade` over the
  current grade-target ids → call `Api.GetSignatureDefaultAsync(derivedGrade)` (null ⇒ the
  route's tenant-default behaviour, unchanged). Re-resolution happens in `OnTargetsChangedAsync`
  when the derived grade *moves* (single grade target added/removed), NOT on a picker change
  (there is no picker). The `/signature-default` route itself is untouched.
- **Q. Grade-scoped question generation** → **removed (contract field only)**. The AI host never
  read `QuestionGenerationRequest.GradeLevelId` (verified: zero reads in
  `src/AI/SchoolCollab.AI.Server`), so generation is *not* actually grade-scoped today. Remove
  the request field, the two sections' `GradeLevelId` parameters, and the page's passing of
  them. Consumers needing a different input: **AI host — none; sections — none**.
- **Approval/notification-policy legs and teacher scoping** (implied consumers, decided here):
  `PublishAssignmentCommandHandler`, `ScheduleAssignmentCommandHandler`,
  `GetAssignmentByIdQueryHandler`, `ListAssignmentsQueryHandler`, **`SubmissionRepository.ListSubmissionsForReviewAsync` (#16b)**, the sweep candidates and the
  `recipient-preview` route all resolve policies with `AssignmentPolicyScope.DeriveGrade(...)`.
  **Every teacher-scope `scope.Allows(...)` call — #15 (by-id), #16 (list `ApplyScope`) and #16b (review queue) — uses the one any-overlap-over-`TargetGradeIds` rule, fail-closed on an empty/null key-set** (they must never diverge).
  `ListAssignmentsQueryHandler.ApplyScope` keeps grade-aware teacher scoping by checking **any**
  overlap between the row's `TargetGradeIds` and the teacher's taught grades
  (`scope.Allows(createdByTeacherId, gradeId, topicId)` per id; a row with no grade target is
  visible only via creator/topic, as a null grade already is today).

### P4. Design — the seams

**P4.1 Migration (assignment-grade drop).**

Model side first (#1, #2), then generate:
`dotnet ef migrations add DropAssignmentGradeLevelColumn --project src/Assignments/SchoolCollab.Assignments.Core`
(design-time factory: `src/Assignments/SchoolCollab.Assignments.Core/Data/DesignTimeAssignmentsDbContextFactory.cs`;
use the same invocation the existing migrations were generated with — e.g. startup-project
`src/SchoolCollab.MigrationService` if the bare command cannot resolve).

- **No backfill, no down-data:** the *authoritative* copy of every persisted grade already became a `GradeLevel` target row in the shipped `AddAssignmentTargets` backfill (TGT-14). **Caveat stated honestly:** that backfill is not lossless for every row — its rule 1 (`AddAssignmentTargets`, audience `= 0`) inserts ONLY the `AllStudents` row even when `grade_level_id` was set, and rule 2 requires `target_audience_type <> 0`. So a legacy AllStudents row carrying a primary grade loses it with no target echo; the teacher-cohort leg for that row, and any grade-specific policy it fed, fall to tenant/default. That outcome is consistent with the owner's settled decision (recipients come from targets), and is recorded here rather than claimed as zero-information-loss. `Up` = `DropIndex("ix_assignments_grade_level_id")`
  + `DropColumn("grade_level_id")`. `Down` = re-add a **nullable** `grade_level_id` `Guid?` column
  and the index **without restoring values** (document in the migration file's header that the
  down is schema-only and lossy by design).
- **Must NOT edit older designers** (P2 OUT). Only the current
  `AssignmentsDbContextModelSnapshot.cs` and the new migration's `*.cs`/`*.Designer.cs` change.
- **Guard check (repo gate):** `dotnet ef migrations has-pending-model-changes` must report no
  changes once the migration is added — `MigrationGuardTests` fails otherwise per the
  `snapshot-config-guard` round.
- A new **Postgres round-trip test** (P5) asserts, against a fresh migrated database, that
  `information_schema.columns` holds no `assignments.grade_level_id` row.

**P4.2 Contracts** (`src/Assignments/SchoolCollab.Assignments.Contracts/ContractTypes.cs`):

- `AssignmentSummaryDto`: remove `GradeLevelId`, `GradeName`; add
  `IReadOnlyList<Guid>? TargetGradeIds = null` (after `RequiresApproval`-adjacent optional
  params, keeping every existing positional call site compiling — **all projection sites must
  set it**, or the list/detail reads silently lose approval/scoping inputs: covered by AC-7).
- `CreateAssignmentRequest` / `UpdateAssignmentRequest`: remove `Guid? GradeLevelId`. Unknown
  older payloads carrying `gradeLevelId` deserialize-cleanly (System.Text.Json ignores unknown
  fields) — no versioning bump needed.
- `TargetAudienceType` fields stay everywhere.

**P4.3 Entity** (`Assignment.cs` + `AssignmentConfiguration.cs`):

- Remove `Guid? GradeLevelId` property, the `Create`/`Update` parameters, the SetTargets
  primary-grade guard and its `targetedGradeIds` block, and stale doc mentions of D-2/TGT-11.
- `AssignmentConfiguration`: remove the property mapping and the `ix_assignments_grade_level_id`
  index declaration. Leave `SyncDerivedTargeting()` (target-audience compat) as is.

**P4.4 Publish/Schedule handlers + contact resolver:**

- `PublishAssignmentCommandHandler`: `assignmentPolicyResolver.ResolveAsync(AssignmentPolicyScope.DeriveGrade(assignment.Targets…))`;
  same derivation into `policyResolver.ResolveEffectiveAsync(tenantId, grade, ct)`; build the
  `ResolveSubscribersRequest` with `GradeLevelIds: <distinct grade-target ids>` and
  `StudentIds: resolvedStudentIds`. FR-58's per-grade topic gate (the existing grade-target loop)
  is unchanged and authoritative.
- `ScheduleAssignmentCommandHandler`: `DeriveGrade` → `assignmentPolicyResolver.ResolveAsync`.
- `IContactResolver.ResolveSubscribersRequest`: `Guid? GradeLevelId` → `IReadOnlyList<Guid?>`
  — precisely: `IReadOnlyList<Guid>? GradeLevelIds = null`.
- `StudentsContactResolver`: delete the `students/by-grade` fallback block; the teacher leg
  iterates `(request.GradeLevelIds ?? []).Distinct()` and unions/dedups the returned
  `TeacherWithRoleDto[]` by teacher id before adding owners; keep the per-grade
  `HttpRequestException` catch (fail-open, ADR posture).

**P4.5 Preview route** (`AssignmentRoutes.cs` `recipient-preview`):

- Drop the `primaryGradeId` query param. Derive the policy-scope grade + teacher-cohort grade
  ids from the **posted** `gradeLevelIds` array: pass `gradeLevelIds` straight into
  `ResolveSubscribersRequest.GradeLevelIds`; `DeriveGrade(gradeLevelIds)` into the two policy
  resolutions. This keeps the advisory preview in sync with publish's derivation (the old
  `primaryGradeId` preview used the *author-picked* grade — which no longer exists).
- `AssignmentsApiClient.RecipientPreviewRequest`: drop `PrimaryGradeId`; the URL no longer emits
  it.

**P4.6 Read handlers (approval + scoping):**

- `AssignmentRepository.ListAsync` + `WardAssignmentProjectionRepository.ListWardAssignmentsAsync`:
  add `a.Targets.Where(t => t.Kind == TargetKind.GradeLevel).Select(t => t.RefId!.Value)
  .ToList()` (as `TargetGradeIds`) to the summary projections; keep the collection projection
  inside the **final** `Select` so EF translates it as a correlated subquery — do not introduce
  client evaluation earlier; run the Postgres integration suite (P5) to prove translation.
- Sweep candidates: project the same subquery, then **in memory** per candidate set
  `PolicyGradeId = AssignmentPolicyScope.DeriveGrade(ids)` (derive in the repository's mapping
  step after `ToListAsync`, not inside the EF expression — `DeriveGrade` has no expression
  translation). `ReminderSweeper` reads `candidate.PolicyGradeId`.
- `GetAssignmentByIdQueryHandler`: cache key + resolver argument use
  `AssignmentPolicyScope.DeriveGrade(assignment.Targets…)` (the aggregate is loaded with
  `Targets` auto-included, so no extra read).
- `ListAssignmentsQueryHandler`: `ResolveApprovalByGradeAsync` keys on the per-row derived grade
  (distinct it across the page as today); `RequiresApproval: approvalByGrade[GradeKey(derived)]`;
  `ApplyScope` per P3; DTO mapping sets `TargetGradeIds`.

**P4.7 Authoring page + form model** (`AssignmentAuthoring.razor`, `AssignmentEditFormModel.cs`):

- **Remove the "Primary grade" `FormRow`** (markup lines ~171–192 incl. the
  `authoring-grade-reason` paragraph), `GradeLevelId`, `GradePickerReason`, `GradePickerDisabled`,
  `OnGradeLevelChangedAsync`, `PolicyLinkHref`'s `_item?.GradeLevelId` read (the policy link
  becomes the tenant/grade-levels list href or simply the same fallback used today when no
  grade — re-implement as: link to `/students/grade-levels` unless exactly one grade target,
  then that grade's card — mirror of DeriveGrade; covered by AC-6), the D-2
  `ValidateForSave` block, `_selectedGradeLevel`'s seeding in `LoadPickersAsync`, and the
  `GradeLevelId` argument in `CaptureSaveSnapshot` + the form-model projections
  (`ToCreateRequest`/`ToUpdateRequest` signatures lose `Guid? gradeLevelId`).
- **The page keeps** `_gradeLevels`/`_gradeLevelOptions` — the Add dialog's Grade Levels
  dropdown, stream-option grouping and chip labels still need them.
- **Subject picker re-keyed (consumer with a different input):** FR-58 said subjects = primary
  grade's effective list ∪ group list. It becomes **the union over the distinct grade-target
  ids ∪ the group-subject union**: replace `LoadSubjectsForGradeAsync(_selectedGradeLevel…)`
  with `LoadSubjectsForGradeTargetsAsync(IReadOnlyList<Guid> gradeTargetIds)` — loop the
  `StudentsApi.ListSubjectsByGradeEffectiveAsync(id, date)` calls per distinct grade target,
  per-call fail-isolated (the existing per-fetch isolation pattern), union + dedupe by id into
  `_subjectOptions`. `SubjectOptions` unchanged union logic over the two sources;
  `SubjectPickerDisabled`/`SubjectPickerPlaceholder` read `TargetIds(TargetKindDto.GradeLevel)`
  emptiness instead of `_selectedGradeLevel`; trigger points: `LoadPickersAsync` (create/edit
  load, using the target rows just loaded), `OnTargetsChangedAsync` (when the grade-target set
  changes — replacing today's `previousGrade` comparison), and `OnDueDateChangedAsync` (re-run
  over the grade-target ids). `SyncTargetPickerState` shrinks to dropping `_selectedGradeLevel`
  and only clearing the stale subject selection when the grade-target id set changed.
- **Signature default (P3 B):** in `OnTargetsChangedAsync`, when the derived grade id changed
  since the last resolution, call `ResolveSignatureDefaultAsync(derivedGrade)`; the load path
  (`LoadAsync`) calls it once with `DeriveGrade` of the loaded/entered targets (create → null).
  The log message loses `GradeLevelId` wording.
- **Section params:** `QuestionGenerationSection`/`QuestionsDraftSection` lose the
  `GradeLevelId` parameter (and the page stop-passes it).

**P4.8 Dialog chip flow** (`TargetsAndAudienceDialog.razor` (+ new `.razor.css`),
`TargetsAndAudienceDialogModel.cs`, `TargetsAndAudienceEntry.cs`):

- **Model:** replace `IReadOnlyList<TargetsAndAudienceOption> SelectedOptions` with
  `List<TargetsAndAudienceEntry> PickedEntries`. `Category` stays. Option sources +
  `StudentSearch` stay. Add `AppendPicked(category, option)` — dedupes by
  `(Category, RefId)` (a double pick must not mint a duplicate chip) and clears the Everyone
  entry when an entity value is appended (TGT-2).
- **Dialog markup:** every category renders ONE **single-value** control that **appends on pick**
  and clears itself back to its placeholder (no multi-select listbox, no `min-height:160px`):
  - GradeLevels / Streams / ActivityGroups: `FluentSelect` bound via
    `SelectedOption`/`SelectedOptionChanged` → `AppendPicked`; no inline `Style` (the
    blazor-components rule) — a `form-select--dialog` class.
  - Students: keep the server-search `FluentAutocomplete` but **single-pick**
    (`MaximumOptionsSearch`, `ImmediateDelay` and `OnOptionsSearch` unchanged; the pick lands in
    `PickedEntries` and the control's displayed selection resets).
- **Chip row:** above the value control, when `PickedEntries.Count > 0`, render a
  `<div class="entity-grid-chips">` of the shared `<Chip Label=… Subtitle=…
  OnDismiss="@(() => RemovePicked(entry))" />` — identity-dismiss (reference equality, the
  `RemoveTargetEntryAsync` precedent), since duplicate entries are impossible after dedupe but
  label collisions are not. Provide the row/container + placeholder styling in a NEW scoped
  `TargetsAndAudienceDialog.razor.css` (mirror the `GuardianPickerDialog.razor.css` chip block;
  `Chip.razor.css` already ships its own `.chip` styling).
- **Everyone:** the Everyone category shows the checkpoint and its pick sets
  `PickedEntries = [Everyone entry]` (mutual exclusion); switching category to an entity one
  **keeps** previously picked entity chips (they carry their own category) — unlike the current
  dialog, a submission may now return a **mixed-kind batch**. Update the
  `TargetsAndAudienceDialogResult` doc (remove the "always homogeneous" wording) and note that
  Everyone + entity entries still never mix because the dialog clears the other side on either
  pick.
- **Submit:** unchanged shape — `OnValidSubmit` → `SubmitAsync` returns
  `TargetsAndAudienceDialogResult(PickedEntries)`; empty list ⇒ `Error = "Add at least one
  target."` (wording: keep the current per-category missing-value error semantics — with chips
  the empty case is simply an empty `PickedEntries`). Cancel semantics unchanged.
- **Doc-comment sites to update with the "always homogeneous" wording:** `TargetsAndAudienceEntry.cs:48`
  (the `TargetsAndAudienceDialogResult` doc) and `AssignmentAuthoring.razor:767` (the page-merge
  comment). Both must now allow a mixed-kind batch (Everyone still never mixes, because the
  dialog clears the opposite side on either pick).
- **Page side (`OpenTargetsAndAudienceDialogAsync`):** the existing merge already appends the
  batch and enforces Everyone exclusivity on the page — it needs no change beyond surviving the
  mixed-kind batch (it does: it `RemoveAll`s the opposite side and `AddRange`s). Keep
  `DialogSize.Large` (the dialog now carries the chip row + dropdown; the Large sizing decision
  holds).

### P5. Acceptance criteria (falsifiable against pre-change code)

| # | Criterion | Fails-on-the-old-code because |
|---|---|---|
| AC-1 | `dotnet build SchoolCollab.slnx` — **0 errors** (attempt the canonical command FIRST; if the dev stack holds MSBuild locks, re-run with `-p:BaseOutputPath=<repo-root>/artifacts/build-drop-grade` and record that; never kill the running services) | the old surface is self-consistent; the new one only builds after every seam moved together |
| AC-2 | **Source-inspection gate (new architecture test in `SchoolCollab.ArchitectureTests.Unit`):** no live seam carries the ASSIGNMENT's grade. Scope the pattern to the assignment-grade seam only — fail if `Assignment.GradeLevelId`, `AssignmentConfiguration`'s grade mapping/index, `AssignmentSummary(.Dto).GradeLevelId`/`GradeName`, `Create/UpdateAssignmentRequest.GradeLevelId`, `QuestionGenerationRequest.GradeLevelId`, or `AssignmentReminder/Completion/OverdueSweepCandidate.GradeLevelId` still exist, or if any non-migration `src/Assignments` file reads the singular `a.GradeLevelId`/`assignment.GradeLevelId`. **Allowlist (must NOT be flagged):** historical `*Migrations/**` + `AssignmentsDbContextModelSnapshot.cs`; the teacher-scope mirror `TeacherScope.cs`, `TeacherScopeHttpClient.cs`, `TeacherGradeAssignmentResponse.cs` (a *different* grade concept — the teacher's taught grade); the generic grade-policy resolvers' log wording (`AssignmentPolicyResolver.cs`, `NotificationPolicyResolver.cs`) and the `/signature-default` route param, which resolve a Students-grade policy generally and stay. **Match the SINGULAR `GradeLevelId` only** (receiver-qualified or word-boundaried) — the plural `GradeLevelIds` (`IContactResolver`, `AssignmentsApiClient.cs:170`, the preview route) is the KEPT teacher-cohort input and must not trip the gate. | today it matches the entity, configuration, DTOs, contracts, handlers, resolvers, page |
| AC-3 | **Drop migration round-trip (new Postgres test in `SchoolCollab.Assignments.Tests.Integration`):** migrate fresh → no `assignments.grade_level_id` column, no `ix_assignments_grade_level_id` index (query `pg_catalog`); pre-existing assignment rows + their targets survive | the old schema carries the column |
| AC-4 | **Entity:** `Assignment.Create`/`Update`/`SetTargets` have no grade parameter and no primary-grade guard (unit tests: a two-grade-target set publishes-ready without any grade argument — old code threw `ArgumentException`) | old `SetTargets` refused a 2-grade set without a primary grade |
| AC-5 | **Publish legs (handler tests):** (a) single grade target ⇒ contact resolver receives `GradeLevelIds == [gradeId]` and both policy resolvers receive that grade; (b) two grade targets ⇒ resolvers receive `GradeLevelIds == [a, b]` but the **derived policy grade is null (tenant default)**; (c) zero grade targets ⇒ same null policy grade, teacher leg empty; (d) empty resolved student set still refuses publish | old code fed an authored `GradeLevelId` regardless of targets |
| AC-6 | **Signature default:** authoring page (or the helper it calls) resolves `GetSignatureDefaultAsync(derivedGrade)` — single grade target ⇒ that grade's override; none/multi ⇒ tenant default (`SignatureDefaultsTests` updated likewise); the `/signature-default` route itself still honours an explicit `gradeLevelId` | old code resolved from the author-picked picker value |
| AC-7 | **Summary projection:** list + details + ward summaries carry `TargetGradeIds` populated and no `GradeLevelId`/`GradeName`; `RequiresApproval` mirrors the derived grade (old code resolved per the authored column — a test seeding an assignment whose grade policy differs from the tenant default and which carries that grade as a target now still lands approval=true, while a 2-grade assignment with no single-grade override lands the tenant default) | old DTO carried `GradeLevelId`|
| AC-8 | **Chip dialog (bUnit + source inspection):** the Add dialog renders a single-value dropdown (no `Multiple="true"`, no 160px listbox), appending an option adds one chip **without closing/cycling**, dismissing removes exactly that chip, submit returns every chip as an entry (mixed kinds allowed), Everyone-pick clears the entity chips and vice versa, and the double-pick produces ONE chip | old dialog had `Multiple="true"` controls and reset the selection on category change |
| AC-9 | **Authoring page:** no "Primary grade" field/`authoring-basics-grade` element anywhere in Create/Edit/View; the subject picker stays usable (loads its options from the grade-target set) with the same disabled-with-reason contract; `IsDirty` snapshot excludes the grade | old page rendered the picker and validated D-2 |
| AC-10 | **Question generation:** `QuestionGenerationRequest` (AI.Abstractions) has no `GradeLevelId` field, and `QuestionGenerationSection`/`QuestionsDraftSection` compile with no `GradeLevelId` parameter (source-inspection assertions). **Do NOT assert an AI-host smoke test on this** — the field was optional and never read, so a grade-less request behaves identically pre-change; that half cannot fail. | the old record carried the field and the sections declared the parameter |
| AC-11 | **`dotnet test` 0 failures** across the solution (run the canonical no-arg command; same MSBuild-lock fallback as AC-1). Include the new tests in the relevant projects (ArchitectureTests, Assignments.Tests.Integration, Assignments.Tests.Unit, Admin.Tests.Unit bUnit) | the updated suites pin every seam above |
| AC-12 | `dotnet ef migrations has-pending-model-changes` (assignments model) reports none after P4.1 — `MigrationGuardTests` green | a model change without a migration fails the guard |

### P6. Verification constraints (from the repo/dev stack)

- The dev stack (`dotnet run` AppHost/API/worker) holds **MSBuild locks** — `dotnet build`/`dotnet test` can fail with MSB3021/MSB3027. **Always attempt `dotnet build SchoolCollab.slnx` (repo root) first**; on a lock, re-run with a temp output path
  (e.g. `-p:BaseOutputPath=C:/Users/skwar/source/repos/School-Collab/artifacts/build-drop-primary-grade`) and record in the Worker Report which shape was green. Do not kill the running services; record the conflict instead.
- Test targeting: run the affected projects in a sane order when iterating
  (`SchoolCollab.Assignments.Tests.Unit` → `…Api.Tests.Unit` → `…Tests.Integration` (needs
  Postgres per the existing factory) → `SchoolCollab.Admin.Tests.Unit`), then the full no-arg
  `dotnet test` for AC-11.
- The round produces **one** `documents/rounds/diffs-drop-primary-grade.patch`
  (the FULL feature diff from base `870b4b0` — the tree was dirty at round start).

### P7. Risks / open questions for the plan-review gate

1. **Multi-grade policy posture (decided, worth confirming):** with 2+ distinct grade targets the
   assignment resolves the **tenant-default** assignment/notification policy (the derived grade
   is null). The retired D-2 gave the author a choice; owners accepted target-derivation. If the
   reviewer reads this as a policy gap, the alternative (resolve per grade target and merge —
   stricter cap wins) is a bigger change; say so explicitly if you want it.
2. **Teacher-cohort widening:** grade teachers now derive from grade TARGETS (multi-grade ⇒
   more teacher rows) instead of the single primary grade. Group-only assignments with no grade
   target lose their teacher-recipient leg unless a grade target names one — same as a group-only
   assignment with no primary grade today, but now with no override possible. Confirm this is
   the intended recipient surface.
3. **EF correlated-collection projection:** `TargetGradeIds` inside the final `Select` of the
   sweep/summary queries must translate on Npgsql (the reminder query already nests
   `IgnoreQueryFilters` subqueries — adding a correlated collection is the riskiest EF edit in
   the round). Fallback if translation fails: fetch id+grade-target-ids pairs in a second
   query and map in memory.
4. **Summary contract churn:** `AssignmentSummary`/`AssignmentSummaryDto` change (field removal +
   addition) — a cached hybrid-cache payload from before the deploy would deserialize with
   `TargetGradeIds` null until the `assignments` tag cache is invalidated; publish/schedule
   already `RemoveByTagAsync("assignments")`, but the list cache's own tag-based invalidation
   should be verified during review.
5. **Subject-union semantics for 2+ grade targets:** I chose **union** over the grades'
   effective subject lists (FR-58's grade∪group union, extended). Intersection ("subject must be
   offered by every targeted grade") is the safer publish gate — but the publish-time FR-58
   grade gate already runs per grade target, so a union at authoring time only widens the
   pick-list while publish still refuses an unassigned subject. Confirm union is acceptable.
6. **Backfill-test pinning:** `AssignmentTargetBackfillMigrationTests` must not migrate past the
   drop migration; if its forward migrate targets "latest", pinning it to
   `AddAssignmentTargets` is required (P2 OUT).

## Worker Report

Worker `deepseek-v4.1-flash` (two runs: an initial pass that hit its 45-min cap after completing the removal, then a resumed pass that cleaned up and verified).

- Changed: 32 modified `src` files + the new `AssignmentPolicyScope.cs`, the `20261006225404_DropAssignmentGradeLevelColumn` migration pair, `TargetsAndAudienceDialog.razor.css`, and the edited untracked dialog trio; 69 modified + 4 new test files. 112 files / 10 566 patch lines, 0 phantom deletions.
- Build: canonical `dotnet build SchoolCollab.slnx` → 76 MSB3021/MSB3027 **lock-only** errors, **0 CS errors** (dev stack holds the DLLs); redirected `-p:BaseOutputPath` → **Build succeeded, 0 errors**.
- Tests: `SchoolCollab.Assignments.Tests.Unit` 889/0 · `SchoolCollab.ArchitectureTests.Unit` 102/0 · `SchoolCollab.Assignments.Api.Tests.Unit` 158/0 · `SchoolCollab.Assignments.Tests.Integration` 21/0 · `SchoolCollab.Admin.Tests.Unit` 623/0.
- Deviations (8, all recorded in the worker report): `TargetGradeIds` appended as the last optional DTO param; the page's private `DerivePolicyGrade` mirror (Application cannot reference Core); one shared `TeacherScope.AllowsAnyTargetGrade` for the three scope seams; `AssignmentCompletionSweepCandidate.PolicyGradeId` left null (a correlated projection would defeat that query's server-side `Distinct()`); the backfill test's forward leg pinned to `AddAssignmentTargets`; two `SelectedGrades` guard tests replaced; test-harness adjustments; the preview armed before the awaited reloads.

## Review

- **Plan-review gate** (`glm-5.3`): first pass REWORK (2 P1: unsatisfiable AC-2 gate; unenumerated by-id teacher-scope seam) → fixed; bounded re-read caught a **third** teacher-scope seam (`SubmissionRepository.ListSubmissionsForReviewAsync`) → fixed. All 6 P7 questions ruled **safe**.
- **Diff reviewer — Core slice** (`kimi-k2.7-code`): **PASS**, no P1/P2. (First dispatch timed out; the re-run on the same model returned the verdict.)
- **Diff reviewer — Contracts/API/Application/Worker/AI slice** (`kimi-k2.7-code`): **PASS**, no P1/P2. Explicitly ruled the page's `DerivePolicyGrade` mirror behaviourally exact for all inputs the page receives (`TargetIds` already filters `RefId.HasValue` and `Distinct()`s).

## UI Tester

**No verdict — unavailable.** The `minimax-m3` UI-tester seat timed out at its 30-min cap on two dispatches (provider under load); its session persisted but returned no UI TEST block. Per the skill, an empty tester verdict means the UI was **not** independently bug-hunted. Recorded as a residual, not a pass.

## Acceptance

Verdict: **CLOSED with residual** (no open P1 from the two source reviewers; the UI tester did not run).

| Criterion | Result |
|---|---|
| AC-1 build 0 errors | **pass** — canonical 0 CS errors (locks only); redirected 0 errors (parent-run) |
| AC-2 assignment-grade seam gone (allowlisted refs only) | **pass** — parent grep: only migrations/snapshot, the teacher-scope mirror, resolver log wording and the plural `GradeLevelIds` remain |
| AC-3 drop-migration round-trip | **pass** — `DropAssignmentGradeLevelColumnMigrationTests` in the green Integration suite |
| AC-4 entity guards gone | **pass** — `AssignmentTests` replacements + Core reviewer |
| AC-5 publish legs derive from targets | **pass** — Core reviewer; `AssignmentPolicyScope.DeriveGrade` is the single rule |
| AC-6 signature default derived from the single grade target | **pass** — app reviewer + `SignatureDefaultsTests` |
| AC-7 summary `TargetGradeIds` | **pass** — projections populate it; `RequiresApproval` mirrors the derived grade |
| AC-8 chip dialog | **pass** — app reviewer + `TargetsAndAudienceDialogTests`; **but not UI-tester-verified** |
| AC-9 authoring page | **pass** — no Primary-grade element; subject picker target-fed |
| AC-10 question generation | **pass** — request field + section params gone |
| AC-11 `dotnet test` 0 failures | **pass on the five affected suites** (889/102/158/21/623). The full-solution run shows 27 failures the worker attributes to pre-existing/unrelated conditions (22 Config/Settings harness, 3 AI OpenRouter, 2 assignment bUnit flakes); **not independently re-adjudicated by the parent** — see residual 2 |
| AC-12 `has-pending-model-changes` clean | **pass** — worker ran it: "No changes…" |

### Residuals

1. **UI tester seat never produced a verdict** (timed out twice). The chip dialog and the removed Primary-grade surface are covered by the app-slice reviewer and by bUnit tests, but not by an independent adversarial UI pass. Re-run `ollama-cloud/minimax-m3` (or another UI-capable model) against handover scope before relying on the UI.
2. **Full-solution `dotnet test` was not adjudicated by the parent.** The five affected projects are individually green; the 27 full-run failures are worker-attributed to pre-existing harness/parallel-load conditions and need a parent confirmation (or a `main`-baseline comparison).
3. **Two deliberate deviations to confirm with the owner:** (a) an Everyone-only assignment now has **no subject option source** (the subject union is grade-target ∪ group-target, so the author must target a grade or group to pick a subject) — the plan's chosen union semantics; (b) with **2+ grade targets** the policy resolves to the **tenant default** (the retired D-2 author choice is gone), so a grade that would have required approval can no longer force the gate by itself.
4. The page's `DerivePolicyGrade` is a private behavioural mirror of `AssignmentPolicyScope.DeriveGrade` (Application cannot reference Core) — both reviewers accepted it; a future change to `TargetIds` filtering could diverge.
5. The whole feature (prior targets-builder round + this round) is still **uncommitted on `main`**. Owner authorization is required to commit/push/PR; a canonical build needs the dev stack stopped.

## Residuals resolution (owner round 2)

Owner answered the grill: Q1 baseline, Q2 defer, Q3 accept+hint, Q4(a) tenant-default confirmed, Q5 leave+comment.

**Q1 — full-suite baseline (DONE).** Ran the full suite on base `870b4b0` in a clean detached `git worktree` (own output dir, no lock conflict):
- **Baseline: 3497 total, 3 failed** — all three are `ChatAsync_WithOpenRouter_*` (untouched AI tests).
- **Branch (redirected output): 3497 total, 25 failed** — the same 3 AI tests, **21 `0 ms` host-init failures in projects this round did NOT touch** (CodedValues, feature-flag, Config/Settings route tests), and **1 assignment bUnit** (`Create_Save_CarriesTheAuthoredTargetRows`, 5.6 s).
- **Reading:** the branch's own output path is MSB-locked by the running dev stack, so its only observable full run uses the redirected `-p:BaseOutputPath`, and the 21-test `0 ms` cluster is the redirected-output-path artifact the worker flagged (the fixed `..`-walk source-path tests + the host-init suites resolve the repo root differently at that depth). The 3 AI failures are genuinely pre-existing (identical on the baseline). The 1 assignment bUnit is a parallel-load flake (passes 889/0 in isolation, twice).
- **Not yet done:** a like-for-like branch run on the DEFAULT output path (needs the dev stack stopped). Until then the 21-test cluster is *attributed* to the output path, not *proven* by a matched-path run.

**Q1b — matched-path proof (DONE).** Dev stack stopped; canonical `dotnet build SchoolCollab.slnx` = **0 errors**, and the full suite on the DEFAULT output path = **3509 total, 3 failed** — the same 3 pre-existing AI OpenRouter tests as the baseline. The ~21 `0 ms` cluster was therefore the redirected-output-path artifact, and this round adds **12 tests with zero new failures**. Residual 2's "unattributed failures" are fully resolved.

**Q2 — flakes (FIXED).** The flake's root cause: the shared `SelectAsync` pick helper (`tests/SchoolCollab.Assignments.Tests.Unit/AssignmentAuthoringBunitTests.cs:333`) drove the subject picker with a **synthetic** `PickerOption` instance. `AddTargetEntryAsync` (group target) kicks off an async group-subjects reload that replaces `_subjectOptions` and rebuilds the picker's `Items` as a **fresh array**; when that rebuild lands after the pick, FluentUI — which matches the selection **by instance** — drops it, `_selectedSubject` goes null, the save guard bails ("Please select a subject.") and `createBody` stays null. This is the verified case-1/case-2 race class (skill `fix-flaky-bunit-fluentui-after-cascade`), not a regression.

Hardened the **shared seam** (every picker-driving test inherits it), per the skill's explicit "highest-leverage seam" guidance: `SelectAsync` is now `async` and (1) `WaitForAssertion`s that the value is present in the picker's **current** `Items` (the prerequisite reload settled), (2) picks **that instance** — never a synthetic one — and (3) `WaitForAssertion`s that the landed selection (`SelectedOption.Value`) equals the value before returning, so a Submit in the next statement cannot read the pre-pick state. The one call site lost its now-unused `label` argument.

Validation: test project builds 0 errors; the flake test passes **10/10** on the compiled binary; the full Assignments suite passes **889/0**. No product code changed — test-only ordering/identity fix.

**Q3 — Everyone-only subject source (already satisfied).** `AssignmentAuthoring.razor:989` already renders `SubjectPickerPlaceholder` = "Select a grade level or activity group first" when there is no grade and no group target. No change needed.

**Q4 — tenant-default for 2+ grade targets (confirmed).** `AssignmentPolicyScope.DeriveGrade`: exactly one distinct grade target ⇒ that grade's policy; zero or two-or-more ⇒ tenant default. No per-grade policy merge this round.

**Q5 — `DerivePolicyGrade` mirror (hardened).** The pointer comment already existed; the mirror in `AssignmentAuthoring.razor:945` was additionally made **byte-for-byte equivalent** to the Core rule (it now drops `Guid.Empty` and `Distinct()`s before the one-element pattern match), removing the divergence the app reviewer flagged.
