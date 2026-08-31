# Implementation Review — Activity-Group Enrollment + Period Hierarchy

> Review date: 2026-08-27
> Scope: backend implementation completed since 2026-08-26
> Status: **backend Phases H1–H4 and activity-group Phases 7–11 are shipped and green; UI work and a few integration/E2E items remain.**

---

## 1. What was implemented

### 1.1 Period hierarchy (`period-hierarchy-impl.md` Phases H1–H4)

| Phase | Status | Key deliverables |
|-------|--------|------------------|
| H1 | ✅ Done | `PeriodType` enum; `Period` gains `PeriodType` + `ParentPeriodId`; additive migration; `Create`/`Update` pass-through; active-period provider extended; 6 unit tests |
| H2 | ✅ Done | Partial-unique indexes (one active year + one active sub-period per type/year); hierarchy-aware `ActivatePeriod`/`CompletePeriod`; cascade completion; 6 unit tests |
| H3 | ✅ Done | `FlagKind.String` + `Value` columns; `academic_year_division` feature flag seeded; `GET`/`PUT /api/config/flags/academic_year_division`; cross-context framework gate on `CreatePeriod`/`UpdatePeriod`; reverse switch-rejection via `ISubPeriodCountProvider`; ~10 unit tests |
| H4 | ✅ Done | Sub-period containment validation; sibling no-overlap; `EnrollStudent` rejects Term/Semester periods; read endpoints for active academic year / active sub-period / sub-periods list; 9 unit tests |

**Test coverage:** `SchoolCollab.Students.Tests.Unit` **299/299 passing** (includes the new period-hierarchy tests). Full solution builds with 0 errors.

**Notable spec corrections made during implementation:**
- `PeriodStatus.Active = 1`, so the unique-index filters use `status = 1`, not the spec's originally written `status = 0` (which is `Draft`).
- The Settings API base path is `/api/config/flags/...`, not `/api/settings/feature-flags/...` as the draft spec had it.
- `AcademicYear` creation/update is always allowed, regardless of division; only `Term`/`Semester` are gated.
- The reverse switch-rejection is **fail-closed**: if the cross-context Students count is unavailable or returns non-zero, the PUT rejects the switch.

### 1.2 Activity-group backend Phases 7–11 (`activity-group-enrollment-impl.md`)

| Phase | Status | Key deliverables |
|-------|--------|------------------|
| 7 (Rev. 2) | ✅ Done | `ActivityGroup` on/off `IsActive`; `PeriodId` removed; `ActivityGroupStatus` enum deleted; `ActivityGroupMembership` gains nullable `PeriodId`; split partial-unique indexes; `activity_group_grade_levels` link table; grade-eligibility check; `InactiveGroupException`; UI build-fix ripple |
| 8 (Rev. 3/4) | ✅ Done | `EnrollmentSpan` enum + `EnrollmentStartDate`/`EnrollmentEndDate`/`AutoRenewDefault`; membership `AutoRenew` + window dates; `OpenEnded`/`DateRange` semantics; closed-window rule; 8 unit tests |
| 9 (Rev. 4/5) | ✅ Done | Next-window slot; `RolloverActivityGroup` command; scheduled `ActivityGroupRolloverService` in Students.Worker; `SetMembershipAutoRenew`; 5 unit tests |
| 10 (Rev. 3 period-aligned) | ✅ Done | `AddMembership` resolves/validates typed periods for `WholeAcademicYear`/`Termly`/`Semester`; framework-compatibility check on create; rollover into active typed period; 6 unit tests |
| 11 (Rev. 6 bridge) | ✅ Done | Optional `PeriodId` on `TopicAssignment` TPH root; validation in `AssignGradeTopic`/`AssignActivityGroupTopic`; cross-context `ITopicAssignmentLookup` in publish path; 6 + 1 unit tests |

**Test coverage:**
- `SchoolCollab.Students.Tests.Unit`: **299/299 passing**
- `SchoolCollab.Assignments.Tests.Unit`: **100/100 passing**
- `SchoolCollab.Admin.Tests.Unit`: **453/453 passing**
- `SchoolCollab.ArchitectureTests.Unit`: **20/20 passing**
- `SchoolCollab.Core.Tests.Unit` (cross-module wiring): **79/79 passing**
- `SchoolCollab.Students.Api.Tests.Unit` + `SchoolCollab.Assignments.Api.Tests.Unit`: **1/1 each passing**

### 1.3 Cross-context / Aspire wiring added

- `IAcademicYearDivisionProvider` / `AcademicYearDivisionProviderHttpClient` (Students.Api → Settings)
- `ISubPeriodCountProvider` / `SubPeriodCountProviderHttpClient` (Settings.Api → Students)
- `ITopicAssignmentLookup` / `TopicAssignmentLookupHttpClient` (Assignments.Api → Students)
- AppHost `.WithReference(studentsApi)` added to `settingsApi` to make the reverse call resolvable.
- `CrossModuleWiringTests` still pass.

---

## 2. Gaps and remaining work

### 2.1 Tracker vs. reality mismatch (documentation only)

`subject-to-topic-polymorphism-impl.md` lists Phases 1–2 as **not started**, but the codebase **already shipped Design B** as a TPH design:
- `Topic` (shared global, no owner columns)
- `TopicAssignment` abstract TPH root with `GradeTopicAssignment`/`ActivityGroupTopicAssignment` subtypes
- `StudentTopicAssignment`
- Migrations: `TopicSharedGlobalBridge`, `MakeGradeSubjectAssignmentDateBased`, etc.

**Gap:** the tracker and spec need to be reconciled to reflect that the rename + bridge is already implemented (as TPH, not the literal `GradeSubjectAssignment` table the spec still describes). The Rev. 6 `PeriodId` work was applied on top of this existing TPH bridge, so the tracker is effectively obsolete for Phases 1–2.

### 2.2 Not-yet-implemented backend items

| Item | Tracker | Why it's still open |
|------|---------|---------------------|
| `Phase H5.2` | Integration test: Termly group → membership attaches to active Term | Requires a full in-memory or integration harness wiring active year + term + activity group. Not done. |
| `Phase H5.3` | E2E/Playwright seeded flow | Requires UI + Aspire runtime; intentionally UI backlog. |
| `Phase 6.1` | Default `EnableActivityGroups` ON in pilot tenant settings + config doc update | Pilot/config decision; intentionally deferred until model migration is complete. |
| `Phase 6.2` | Playwright smoke: create club → add students → group-scoped topic → SelectedGroups assignment → publish → assert recipients | Requires UI (topic create with owner, assignment create with groups, publish) — entirely UI backlog. |
| `Phase 6.3` | Pilot monitoring | Operational; not code. |

### 2.3 UI work entirely unstarted

All forward UI work lives in `ui-implementation-backlog.md` and is **not implemented**:
- Period-hierarchy admin UI (academic-year/term/semester creation, parent picker, framework setting editor).
- Activity-group span picker in create/edit dialog (`WholeAcademicYear`/`Termly`/`Semester`/`DateRange`/`OpenEnded`, date pickers, next-window editor).
- Eligible-grade picker in activity-group create/edit.
- Membership AutoRenew toggle, forced rollover button.
- Student Detail activity-group section updates for spans/periods.
- Subjects→Topics admin rename (labels/pages) if not already done — verify.
- Assignment create/edit topic picker filtered by audience + period alignment.

### 2.4 Code-level gaps found during review

| # | Gap | Severity | Where |
|---|-----|----------|-------|
| 1 | `PeriodContainmentException` and `PeriodOverlapException` are mapped to HTTP 422 in `PeriodRoutes`, but `PeriodNotFoundException` is mapped to 404. Verify the API smoke tests exercise the new 422 paths. | Low | `src/Students/SchoolCollab.Students.Api/Endpoints/PeriodRoutes.cs` |
| 2 | `ActivityGroupRolloverService` runs daily at 02:00 but has no observable telemetry/metric other than logs. Consider adding a `RolloverCompleted` metric or health-check hook. | Low | `src/Students/SchoolCollab.Students.Worker/ActivityGroupRolloverService.cs` |
| 3 | `ITopicAssignmentLookup` in `PublishAssignmentCommandHandler` is **fail-open** when Students API is unreachable (returns `true`). This matches the existing `IActivityGroupLookup` behavior but contradicts the fail-closed pattern used elsewhere. Document the rationale or align with fail-closed. | Medium | `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/PublishAssignmentCommand/PublishAssignmentCommandHandler.cs` |
| 4 | The `Assignments.Api` `TopicAssignmentLookupHttpClient` calls `effectiveDate ?? DateOnly.FromDateTime(DateTime.UtcNow)`; ensure the assignment's own effective-date semantics (publish date vs due date) are the right anchor. | Medium | `src/Assignments/SchoolCollab.Assignments.Api/Services/TopicAssignmentLookupHttpClient.cs` |
| 5 | `AutoRenewDefault` on `ActivityGroup` defaults to `true`, but existing v1 groups created before the migration will have `false` (CLR default for `bool`). If v1 data exists in pilot, this may be a data issue. | Low | Migration `20260827092137_AddActivityGroupEnrollmentSpans` |
| 6 | `ActivityGroup` `IsActive` defaults to `true` in the migration, but the entity factory also defaults to `true`. Consistent; no issue. | None | — |

### 2.5 Spec/doc drift

| Spec | Drift | Suggested action |
|------|-------|------------------|
| `period-hierarchy-terms-semesters.md` §7 API contract | Still references `/api/settings/feature-flags/academic_year_division`; actual route is `/api/config/flags/academic_year_division`. | Update spec §7 to match implemented Settings API base. |
| `period-hierarchy-terms-semesters.md` §8.1 | Filter was `status = 0`; corrected to `status = 1` in implementation. | Update spec. |
| `subject-to-topic-polymorphism.md` / `-impl.md` | Describes a single-table `GradeSubjectAssignment` bridge and unshipped rename; codebase uses TPH `TopicAssignment` + already-shipped rename. | Reconcile tracker/spec to Design B TPH reality. |
| `activity-group-enrollment-impl.md` Phase 11 notes | Says 11.3 was the "sole unshipped Rev. 6 item" but 11.3 is now done. | Update tracker. |

---

## 3. Test summary

```text
SchoolCollab.Admin.Tests.Unit        453/453 ✅
SchoolCollab.ArchitectureTests.Unit   20/20  ✅
SchoolCollab.Assignments.Tests.Unit  100/100 ✅
SchoolCollab.Assignments.Api.Tests.Unit 1/1  ✅
SchoolCollab.Core.Tests.Unit          79/79  ✅
SchoolCollab.Students.Tests.Unit      299/299 ✅
SchoolCollab.Students.Api.Tests.Unit    1/1  ✅
```

Integration tests with external OpenRouter (`Settings.Tests.Integration CodedValueAIServiceLiveTests`) are pre-existing environmental failures and unrelated.

---

## 4. Recommendations / next steps

1. **Reconcile documentation first** before any more implementation:
   - Update `subject-to-topic-polymorphism-impl.md` to reflect that Phases 1–2 are already shipped as TPH.
   - Update `period-hierarchy-terms-semesters.md` §7 and §8.1 to match implemented paths/filters.
   - Update `activity-group-enrollment-impl.md` Phase 11 status and remove the stale "11.3 unshipped" note.

2. **Decide on the fail-open vs fail-closed inconsistency** for `ITopicAssignmentLookup` and document the choice.

3. **Implement the remaining backend integration test** (`H5.2`) if you want full backend coverage before opening the UI backlog.

4. **Open the UI backlog sprint** (`ui-implementation-backlog.md`) — that is the bulk of remaining work.

5. **Do not flip `FEATURE:EnableActivityGroups` ON** until the UI backlog is complete and the Playwright smoke (`Phase 6.2`) passes.

---

## 5. Files changed (high-level)

```
src/
  SchoolCollab.Core/
    Tenancy/IActivePeriodProvider.cs
    Features/FeatureFlagKeys.cs
  SchoolCollab.MigrationService/Program.cs
  Settings/
    Settings.Core/Domain/{FeatureFlag,TenantFeatureFlagOverride,FlagKind,AcademicYearDivision}.cs
    Settings.Core/DTOs/FeatureFlagDtos.cs
    Settings.Core/CQRS/FeatureFlags/{Commands,Queries}/*.cs
    Settings.Core/Data/Configurations/*Configuration.cs
    Settings.Core/Services/{ISubPeriodCountProvider,DefaultSubPeriodCountProvider}.cs
    Settings.Core/Extensions.cs
    Settings.Api/{ConfigEndpoints.cs,Program.cs}
    Settings.Api/Endpoints/ConfigAcademicYearDivisionRoutes.cs
    Settings.Api/Endpoints/ConfigTenantFlagOverrideRoutes.cs
    Settings.Api/Services/SubPeriodCountProviderHttpClient.cs
  Students/
    Students.Core/Domain/{PeriodType,Period,ActivityGroup,ActivityGroupMembership,
                           ActivityGroupGradeLevel,TopicAssignment,EnrollmentSpan}.cs
    Students.Core/Domain/Exceptions/*.cs
    Students.Core/Domain/Events/ActivityGroupEvents.cs
    Students.Core/Data/Configurations/{Period,ActivityGroup,ActivityGroupMembership,
                                       ActivityGroupGradeLevel,TopicAssignment}Configuration.cs
    Students.Core/Data/StudentsDbContext.cs
    Students.Core/Data/Repositories/*.cs
    Students.Core/CQRS/Periods/{Commands,Queries}/*.cs
    Students.Core/CQRS/ActivityGroups/{Commands,Queries}/*.cs
    Students.Core/CQRS/TopicAssignments/{Commands}/*.cs
    Students.Core/Services/{IAcademicYearDivisionProvider,DefaultAcademicYearDivisionProvider}.cs
    Students.Core/DTOs/{ActivityGroupDto,MembershipDto,PeriodDto,TopicAssignmentDto}.cs
    Students.Core/Extensions.cs
    Students.Api/{Program.cs,Endpoints/PeriodRoutes.cs,Endpoints/ActivityGroupRoutes.cs,
                   Endpoints/TopicAssignmentRoutes.cs}
    Students.Api/Services/AcademicYearDivisionProviderHttpClient.cs
    Students.Application/Services/StudentsApiClient.cs
    Students.Application/Components/Pages/ActivityGroups/*.razor
    Students.Application/Components/Pages/Students/Detail.razor
    Students.Application/Components/Students/{ActivityGroupCreateDialog,ActivityGroupEditDialog,JoinGroupsDialog}.razor
    Students.Worker/{Program.cs,ActivityGroupRolloverService.cs}
  Assignments/
    Assignments.Core/DTOs/ActivityGroupRefDto.cs
    Assignments.Core/Services/ITopicAssignmentLookup.cs
    Assignments.Core/CQRS/Assignments/Commands/PublishAssignmentCommand/PublishAssignmentCommandHandler.cs
    Assignments.Api/Program.cs
    Assignments.Api/Services/TopicAssignmentLookupHttpClient.cs
  AppHost/SchoolCollab.AppHost/Program.cs

tests/
  SchoolCollab.Students.Tests.Unit/
    PeriodHierarchy*.cs, ActivityGroup*.cs, TopicAssignmentPeriodTests.cs,
    EnrollStudentHandlerTests.cs, StubAcademicYearDivisionProvider.cs
  SchoolCollab.Admin.Tests.Unit/ActivityGroupsPageTests.cs
  SchoolCollab.Assignments.Tests.Unit/
    AssignmentActivityGroupTests.cs, SubmissionEngineTests.cs

documents/specs/
  activity-group-enrollment-impl.md
  period-hierarchy-impl.md
  period-hierarchy-terms-semesters.md
  subject-to-topic-polymorphism-impl.md
  backend-implementation-backlog.md
  ui-implementation-backlog.md

migrations/
  Students.Core/Migrations/20260826164501_AddPeriodHierarchy.cs
  Students.Core/Migrations/2026082617*_AddActivePeriodUniqueIndexes.cs
  Students.Core/Migrations/2026082617*_AddFeatureFlagValue.cs
  Students.Core/Migrations/20260826194201_AddActivityGroupRev2.cs
  Students.Core/Migrations/20260827092137_AddActivityGroupEnrollmentSpans.cs
  Students.Core/Migrations/20260827101242_AddActivityGroupNextWindow.cs
  Students.Core/Migrations/20260827103652_AddTopicAssignmentPeriod.cs
```

---

*End of review.*
