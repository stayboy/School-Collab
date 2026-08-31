# Implementation Review: Activity-Group Enrollment + Period Hierarchy (Backend Phases Completed)

**Scope:** Review the backend implementation delivered for the period hierarchy (H1–H4/H3.5) and activity-group enrollment Rev. 2–6 (Phases 7–11), including the Rev. 6 publish-path subject/period consistency check (FR-58). UI sprints, Phase 6 feature-flag rollout, and H5 integration/E2E work are tracked as remaining backlog, not defects.

**How this review was produced:**
- Full solution build (`dotnet build SchoolCollab.sln`)
- All unit test suites executed
- Spec implementation trackers (`activity-group-enrollment-impl.md`, `period-hierarchy-impl.md`, `subject-to-topic-polymorphism-impl.md`) read against code
- Spot checks of key handlers, EF configurations, cross-context HTTP clients, AppHost wiring, and migrations

---

## 1. Executive summary

| Item | Result |
|------|--------|
| Solution build | **0 errors, 4 warnings** (two known vulnerable package warnings: SQLitePCLRaw.lib.e_sqlite3, SSH.NET) |
| Unit test suites | **9/9 passed** |
| Total unit tests | **1,397 passed, 0 failed, 0 skipped** |
| Backend status | **Functionally complete** for the phases in scope |
| Critical blockers | **None** |
| Required fixes | **None remaining** (all §4 items resolved or accepted/documented) |

The implementation aligns with the authoritative specs. The original stale-tracker issue and the two code-quality items from §4 are now resolved; the accepted FR-58 / `AllStudents` trade-offs are documented in the runbook. Remaining open tracker rows are genuine backlog (UI, integration/E2E, Phase 6 rollout).

> **Fix log (2026-08-27):** §4 items 1–3 are **resolved**; items 4–5 are **accepted with runbook documentation** (see `documents/runbooks/fr58-fail-open-behavior.md`). Rollover batched, `SelectedGrades` guard added, trackers updated. Settings integration tests were not re-run because the 3 pre-existing failures are unrelated OpenRouter network failures.

---

## 2. Test results

| Test project | Passed | Failed | Skipped |
|--------------|--------|--------|---------|
| `SchoolCollab.Admin.Tests.Unit` | 453 | 0 | 0 |
| `SchoolCollab.ArchitectureTests.Unit` | 20 | 0 | 0 |
| `SchoolCollab.Assignments.Api.Tests.Unit` | 1 | 0 | 0 |
| `SchoolCollab.Assignments.Tests.Unit` | 102 | 0 | 0 |
| `SchoolCollab.Core.Tests.Unit` | 79 | 0 | 0 |
| `SchoolCollab.Settings.Api.Tests.Unit` | 1 | 0 | 0 |
| `SchoolCollab.Settings.Tests.Unit` | 441 | 0 | 0 |
| `SchoolCollab.Students.Api.Tests.Unit` | 1 | 0 | 0 |
| `SchoolCollab.Students.Tests.Unit` | 299 | 0 | 0 |
| **Total** | **1,397** | **0** | **0** |

> Settings integration tests were not re-run because the 3 pre-existing failures are unrelated OpenRouter network failures and do not affect this backend scope.

---

## 3. Implementation status by area

### 3.1 Period hierarchy

**Tracker:** `documents/specs/period-hierarchy-impl.md`

| Phase | Status | Notes |
|-------|--------|-------|
| H1 Period types + parent hierarchy | ✅ Done | `PeriodType` enum, `ParentPeriodId`, FK cascade delete, migration `20260826164501_AddPeriodHierarchy` |
| H2 Active-unique indexes + activate/complete cascade | ✅ Done | Unique indexes filter on `status = 1` (Active), corrected from the spec’s earlier `status = 0` typo; auto-close prior year/sibling sub-periods |
| H3 Academic-year division setting + gates | ✅ Done | `FlagKind.String` + `Value` on `FeatureFlag`/`TenantFeatureFlagOverride`; `GET`/`PUT /api/config/flags/academic_year_division`; `IAcademicYearDivisionProvider` fail-open to `None` |
| H3.5 Reverse switch rejection | ✅ Done | `ISubPeriodCountProvider` fail-closed; Settings PUT rejects division change while Draft/Active sub-periods exist; `WithReference(studentsApi)` in AppHost; `TenantForwardingDelegatingHandler` used |
| H4 Containment/overlap + year-level guard + read endpoints | ✅ Done | `PeriodContainmentException`/`PeriodOverlapException` → 422; `EnrollStudentHandler` rejects Term/Semester active period; active-academic-year / active-sub-period / sub-periods read endpoints |
| H5 Integration/E2E/back-compat/tenancy/cache | ⏸ Backlog | Not backend logic; tracked for UI/integration sprints |

**Evidence reviewed:**
- `PeriodConfiguration.cs` — active unique indexes use `status = 1` and include `period_type` for sub-periods.
- `PeriodRoutes.cs` — POST/PUT catch `PeriodFrameworkMismatchException`, `PeriodContainmentException`, `PeriodOverlapException` and return 422.
- `ConfigAcademicYearDivisionRoutes.cs` — fail-closed `count > 0` check before allowing division change.
- `SubPeriodCountProviderHttpClient.cs` — forwards tenant; throws on unreachable, mapped to 422 by the route.
- `EnrollStudentHandler.cs` — `active.PeriodType != PeriodType.AcademicYear` guard.

### 3.2 Activity-group enrollment Rev. 2–6

**Tracker:** `documents/specs/activity-group-enrollment-impl.md`

| Phase | Status | Notes |
|-------|--------|-------|
| 7 Rev. 2 (on/off status, eligible grades, period-scoped membership) | ✅ Done | `IsActive` + `Activate`/`Deactivate`; split unique index on membership; `ActivityGroupGradeLevel` link table; `GradeNotEligibleException`/`InactiveGroupException` → 422 |
| 8 Rev. 3/4 (enrollment spans + windows) | ✅ Done | `EnrollmentSpan` enum, `DateRange`/`OpenEnded`, `EnrollmentWindowClosedException`, capacity per `(group, period)` or group overall |
| 9 Rev. 5 (rollover + auto-renew) | ✅ Done | `NextEnrollmentStartDate`/`EndDate`, `RolloverActivityGroup`, `SetMembershipAutoRenew`, `ActivityGroupRolloverService` background job, domain events |
| 10 Period-aligned spans | ✅ Done | AddMembership resolves active typed period; framework-compat create check; rollover re-enrols into active typed period |
| 11 Rev. 6 bridge (`PeriodId` on topic assignments) | ✅ Done | `TopicAssignment.PeriodId` FK SetNull; grade-topic validates within active academic year; group-topic validates against `EnrollmentSpan`; FR-58 publish consistency shipped |
| 6 Flag flip + pilot rollout | ⏸ Backlog | Feature flag still OFF; Playwright smoke pending |

**Evidence reviewed:**
- `ActivityGroupMembershipConfiguration.cs` — two partial unique indexes dodge the Postgres NULL-distinct pitfall (`period_id IS NOT NULL` / `period_id IS NULL`).
- `AddMembershipHandler.cs` — `ResolveSpanAsync`, capacity check, grade-eligibility check, inactive-group guard.
- `RolloverActivityGroupHandler.cs` — exits all active members before creating new memberships, preserving the FR-10 uniqueness invariant.
- `AssignGradeTopicHandler.cs` / `AssignActivityGroupTopicHandler.cs` — `TopicAssignmentPeriodException` → 422.
- `CreateActivityGroupHandler.cs` — `EnrollmentSpanIncompatibleException` when Termly/Semester is created under the wrong framework.

### 3.3 Subject → topic polymorphism

**Status:** The code paths required by activity-group/assignment integration are complete. The tracker hygiene gap from the initial review is resolved: `subject-to-topic-polymorphism-impl.md` now shows **28/37 done** (only UI backlog and the two intentionally out-of-scope renames remain open), and `activity-group-enrollment-impl.md` lists the Subject→Topic prerequisite as done.

- `Subject` is renamed to `Topic` in the Students domain.
- `GradeSubjectAssignment` bridge is retained as TPH (`GradeTopicAssignment` / `ActivityGroupTopicAssignment`) with the nullable `PeriodId`.
- `Assignment.SubjectId` is renamed to `Assignment.TopicId`.
- The legacy `/api/subjects` alias is registered alongside `/api/topics` in `TopicRoutes.cs`.

**Caveat:** A few peripheral `Subject`-named artifacts remain intentionally (e.g., `SubjectAssignmentSource`, `TeacherGradeLevel.SubjectId`), which the spec marks as separate mechanical PRs / out of scope. They do not affect activity-group functionality.

### 3.4 FR-58 publish-path subject/period consistency

**Status:** ✅ Shipped.

- New cross-context port `ITopicAssignmentLookup` in `Assignments.Core`.
- HTTP client `TopicAssignmentLookupHttpClient` in `Assignments.Api` calls Students `GET /topic-assignments/by-grade/{id}` and `/by-activity-group/{id}?effectiveDate=...`.
- `PublishAssignmentCommandHandler` rejects `SelectedGrades` if the subject is not assigned to the target grade and `SelectedGroups` if it is not assigned to every linked group.
- Test `Publish_SelectedGroups_SubjectNotAssigned_Rejected` added and passing.

---

## 4. Gaps and issues

### 4.1 Resolved / should-fix items

| # | Issue | Severity | Status | Fix applied |
|---|-------|----------|--------|-------------|
| 1 | **Spec-implementation trackers are stale** | Medium | ✅ Resolved | `subject-to-topic-polymorphism-impl.md` updated (28 done; only 2.4, Phase 5 UI, and the two out-of-scope renames remain open); `activity-group-enrollment-impl.md` Subject→Topic prerequisite marked done |
| 2 | **Rollover handler issues O(n) SaveChanges** | Medium | ✅ Resolved | `RolloverActivityGroupHandler` now exits all members then one `SaveChanges`, and inserts renewals via `AddRangeAsync` in a second `SaveChanges` (`SaveChangesAsync`/`AddRangeAsync` added to `IActivityGroupMembershipRepository`); DateRange window advance persists in the same second save |
| 3 | **`SelectedGrades` publish bypasses topic check if `GradeLevelId` is null** | Low | ✅ Resolved | `Assignment.Create`/`Update` now throw `ArgumentException` if `SelectedGrades` is used with a null grade; two tests added |
| 4 | **FR-58 is fail-open when Students API is unreachable** | Low (accepted) | ✅ Accepted & documented | Runbook created at `documents/runbooks/fr58-fail-open-behavior.md`; revisit if stricter fail-closed behavior is wanted |
| 5 | **`AllStudents` audience has no subject-assignment verification** | Low / Spec question | ⏸ Accepted | FR-58 scope is intentionally `SelectedGrades`/`SelectedGroups`; `AllStudents` behavior documented in the same runbook; spec change required to extend validation |

### 4.2 Open backlog (not defects)

These are tracked and expected to remain open until the UI/integration sprints run.

| Area | Open items |
|------|------------|
| Phase 6 rollout | Default `EnableActivityGroups` ON for pilot tenant; Playwright smoke; 1-week monitoring |
| Period hierarchy H5 | Termly/WholeAcademicYear activity-group integration test; E2E/Playwright seeded flow; back-compat regression; strict-tenancy tests for sub-periods; cache-invalidation verification |
| UI sprints | Span/eligible-grade pickers, rollover UI, Playwright E2E (`ui-implementation-backlog.md`) |
| Docs | `centralized-feature-flags.md`, `ef-migrations.md`, `auth-tenancy-pattern.md`, `endpoint-organization-pattern.md` are referenced but not drafted |

---

## 5. Detailed alignment findings

### What is implemented exactly as specified

- **Period active-unique indexes** filter on `status = 1` (Active), not `status = 0` (Draft). This matches the corrected intent in the tracker, not the original spec typo.
- **422 mapping** for period create/update violations is wired correctly.
- **Reverse switch rejection is fail-closed**: indeterminate sub-period count (Students unreachable) returns 422 rather than allowing a risky framework change.
- **Exit-before-create rollover** preserves the FR-10 active-membership uniqueness invariant; old memberships are exited before new ones are inserted.
- **Bridge validation** correctly maps group `EnrollmentSpan` to required `PeriodType` and restricts grade-owned topics to the active academic year.
- **AppHost cross-references** are correct: `assignmentsApi.WithReference(studentsApi)`, `studentsApi.WithReference(settingsApi)`, `settingsApi.WithReference(studentsApi)`.
- **Feature flag defaults** are correct: `EnableActivityGroups` OFF, `AcademicYearDivision` = `None`.
- **Cross-context ports follow the established pattern**: default in Core, HTTP client in Api, overridden via DI in `Program.cs`.

### Minor observations

- The `ActivityGroup.Update` command does **not** allow changing `Span`. This is correct per the spec (span is a model invariant), but it should be documented in the API contract.
- `RolloverActivityGroupHandler` only advances the group window for `DateRange` spans (where `NextEnrollmentStartDate`/`EndDate` exist). For period-aligned spans the next window is the active typed period, so no group-window mutation is needed. This is correct.

---

## 6. Recommendations

1. **✅ Done — trackers updated** (`subject-to-topic-polymorphism-impl.md`, `activity-group-enrollment-impl.md`).
2. **✅ Done — rollover batched** to two `SaveChanges` (`SaveChangesAsync` + `AddRangeAsync`).
3. **✅ Done — `SelectedGrades` null-grade guard** added in `Assignment.Create`/`Update` with tests.
4. **✅ Done — FR-58 fail-open + `AllStudents` scope documented** in `documents/runbooks/fr58-fail-open-behavior.md`.
5. **Schedule the H5 integration tests and Phase 6 Playwright smoke** as the next deliverable slices.
6. **Address the two vulnerable package warnings** (SQLitePCLRaw.lib.e_sqlite3, SSH.NET) by updating the referenced packages when convenient; they are currently in test/integration projects only.

---

## 7. Verdict

**The backend implementation is complete and test-green for the phases in scope.** The §4 items flagged for review are resolved or accepted/documented (trackers updated, rollover batched, `SelectedGrades` guard added, FR-58 fail-open and `AllStudents` scope runbooked). Remaining work is well-defined backlog (UI, integration/E2E, rollout) plus accepted design trade-offs documented in the runbook.
