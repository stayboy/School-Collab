# Backend Implementation Backlog — Forward Only

> **Implementation track: BACKEND.** This is the backend half of the
> activity-group enrollment-span work; `ui-implementation-backlog.md` is the UI
> half. The two are coordinated by the table at the bottom of each doc.
>
> **The spec is the source of truth — this backlog is a derived, sprint-ordered
> task list, not a restatement of requirements.** Each item cites the spec
> section / FR it implements. Read the spec before starting any sprint:
> - `activity-group-enrollment.md` (Rev. 2–6) — activity groups, enrollment spans, rollover
> - `period-hierarchy-terms-semesters.md` — period type/parent, academic-year division
> - `subject-to-topic-polymorphism.md` — Topic entity, bridge, owner validation
> - `active-period-per-tenancy.md` — active-period provider, enrollment guard
>
> **Granular per-spec phase trackers (checkbox-level):**
> `activity-group-enrollment-impl.md` (Phases 1–3 shipped; 7–11 pending backend),
> `period-hierarchy-impl.md` (Phases H1–H5),
> `subject-to-topic-polymorphism-impl.md` (Subject→Topic rename + bridge extension; prerequisite for activity-group).

## Legend

- **P0** — Blocks subsequent sprints or breaks existing shipped behavior.
- **P1** — Required to deliver the activity-group enrollment-span feature end-to-end.
- **P2** — Polish, testing, or non-blocking enhancements.

---

## Sprint 1 — Period Hierarchy Backend Foundation

*Goal: extend the period model and active-period machinery so that terms /
 semesters can exist. Unblocks activity-group span attachment and topic period
 alignment.*

### 1.1 Period type & parent hierarchy

- [ ] **P0** Add `PeriodType` enum (`AcademicYear`, `Term`, `Semester`) to
  `Students.Core/Domain/`. Add `PeriodType` (NOT NULL, default `AcademicYear`)
  and nullable `ParentPeriodId` to the `Period` entity.
- [ ] **P0** Create/update validation: sub-periods require an `AcademicYear`
  parent; `AcademicYear` requires null parent.
  - *Source:* `period-hierarchy-terms-semesters.md` FR-H1/H2
  - *Files:* `Domain/Period.cs`, `Domain/PeriodType.cs`

### 1.2 Additive migration

- [ ] **P0** Migration `<ts>_AddPeriodHierarchy`: add `period_type` (default 0)
  and `parent_period_id` (FK → `periods.id` `ON DELETE CASCADE`) to `periods`;
  back-fill `period_type = 0` for existing rows. `NoUncommittedModelChanges`
  must pass for `StudentsDbContext`.
  - *Source:* `period-hierarchy-terms-semesters.md` NFR-H1, AC-H1
  - *Files:* `Data/Configurations/PeriodConfiguration.cs`, `Migrations/`

### 1.3 Active-period provider extension

- [ ] **P0** Extend `IActivePeriodProvider` (Core) + `ActivePeriod` projection
  with `PeriodType`/`ParentPeriodId`. Add `GetActiveAcademicYearAsync` and
  `GetActiveSubPeriodAsync`. `GetActivePeriodAsync` returns the active
  `AcademicYear` so `EnrollStudent`'s guard is unchanged.
  - *Source:* `period-hierarchy-terms-semesters.md` FR-H8
  - *Files:* `Core/Tenancy/IActivePeriodProvider.cs`, `Students.Core/Tenancy/ActivePeriodProvider.cs`

### 1.4 Period API contract updates

- [ ] **P1** Surface `periodType`/`parentPeriodId` on `PeriodDto` and the Period
  CRUD endpoints.
  - *Source:* `period-hierarchy-terms-semesters.md` FR-H12
  - *Files:* `Students.Api/Endpoints/PeriodRoutes.cs`, `Students.Core/DTOs/PeriodDto.cs`

### 1.5 Relaxed active invariant

- [ ] **P0** Add partial-unique indexes: one active `AcademicYear` per tenant,
  and one active sub-period of each type per academic year.
- [ ] **P0** Update `ActivatePeriodHandler`:
  - Activating an `AcademicYear` auto-completes the prior active year (cascading
    its sub-periods).
  - Activating a `Term`/`Semester` requires its parent year to be active and
    auto-completes the prior active sibling sub-period of the same type.
- [ ] **P1** Update `CompletePeriod`: AcademicYear completion cascade-completes
  still-active sub-periods; sub-period completion does NOT trigger promotion.
  - *Source:* `period-hierarchy-terms-semesters.md` FR-H4/H5/H10/H11
  - *Files:* `CQRS/Periods/Commands/ActivatePeriod/`, `CQRS/Periods/Commands/CompletePeriod/`, `Data/Configurations/PeriodConfiguration.cs`

### 1.6 Academic-year division tenant setting (Settings context)

- [ ] **P0** Extend `FlagKind` with `String = 1`; add nullable `Value` columns
  to `FeatureFlag` and `TenantFeatureFlagOverride`. Add additive migration.
- [ ] **P0** Seed the `academic_year_division` `FeatureFlag` (`Kind = String`,
  `Value = 'None'`) via the MigrationService seed path.
- [ ] **P0** Add `AcademicYearDivision` enum in `Settings.Core/Domain`.
- [ ] **P1** Settings API: `GET`/`PUT /api/config/flags/academic_year_division`
  reusing the existing feature-flag override surface; reject switching to `None`
  or across `Terms`↔`Semesters` while sub-periods of the disallowed type exist.
- [ ] **P1** Gate `Term`/`Semester` period creation on the tenant's framework.
  - *Source:* `period-hierarchy-terms-semesters.md` FR-H6/H7, §8.2
  - *Files:* `Students.Core/Services/IAcademicYearDivisionProvider.cs`,
    `Students.Api/Services/AcademicYearDivisionProviderHttpClient.cs`,
    `Students.Core/CQRS/Periods/Commands/CreatePeriod/CreatePeriodHandler.cs`

### 1.7 Containment & no-overlap validation

- [ ] **P1** Enforce sub-period `[StartDate, EndDate]` contained within its
  parent AcademicYear; no sibling overlap within a year of the same type;
  cross-year-boundary sub-periods rejected.
  - *Source:* `period-hierarchy-terms-semesters.md` FR-H3
  - *Files:* Period create/update command handlers, domain validators

### 1.8 Grade enrollment stays year-level

- [ ] **P1** `EnrollStudentHandler` rejects a `Term`/`Semester` `PeriodId` and
  continues to enroll into the active `AcademicYear`.
  - *Source:* `period-hierarchy-terms-semesters.md` FR-H9
  - *Files:* `Students.Core/CQRS/Enrollments/Commands/EnrollStudent/`

### 1.9 Sub-period read endpoints

- [ ] **P1** Add endpoints: `GET /students/periods/active-academic-year`,
  `GET /students/periods/active-sub-period`,
  `GET /students/periods/{academicYearId}/sub-periods`.
  - *Source:* `period-hierarchy-terms-semesters.md` FR-H12
  - *Files:* `Students.Api/Endpoints/PeriodRoutes.cs`

### 1.10 Tests

- [ ] **P2** Unit + integration tests: back-fill, active invariant, cascade
  completion, containment/no-overlap, framework gating, grade-enrollment
  rejection, sub-period list.
  - *Source:* `period-hierarchy-terms-semesters.md` AC-H1..H10, EC-H1..H5

---

## Sprint 2 — Activity Group Model Migration (Rev. 2)

*Goal: move the shipped v1 model onto the Rev. 2 model (on/off status,
 period-scoped membership, grade eligibility).* Depends on Sprint 1 for
 `PeriodId` semantics.

### 2.1 Activity group entity simplification

- [ ] **P0** Remove `PeriodId`, `Suspend`/`Archive`/`Reactivate` from
  `ActivityGroup`; replace with boolean `IsActive` (default true) +
  `Activate`/`Deactivate` domain methods.
- [ ] **P0** Migration: drop `period_id`, replace `status` enum with
  `is_active` (Active→true, Suspended/Archived→false), drop old status
  indexes, add `(tenant_id, is_active)`.
  - *Source:* `activity-group-enrollment.md` FR-3/4/5/12; `activity-group-enrollment-impl.md` Phase 7.1
  - *Files:* `Domain/ActivityGroup.cs`, `Data/Configurations/ActivityGroupConfiguration.cs`, `Migrations/`

### 2.2 Period-scoped membership

- [ ] **P0** Add nullable `PeriodId` to `ActivityGroupMembership`.
- [ ] **P0** Split the partial unique index:
  - `WHERE status=0 AND period_id IS NOT NULL` on
    `(tenant_id, student_id, activity_group_id, period_id)`.
  - `WHERE status=0 AND period_id IS NULL` on
    `(tenant_id, student_id, activity_group_id)`.
- [ ] **P0** Add hot-path indexes including `period_id`.
  - *Source:* `activity-group-enrollment.md` FR-7/10; `activity-group-enrollment-impl.md` Phase 7.2
  - *Files:* `Domain/ActivityGroupMembership.cs`, `Data/Configurations/ActivityGroupMembershipConfiguration.cs`

### 2.3 Grade eligibility link table

- [ ] **P0** Create `activity_group_grade_levels` link table
  (`tenant_id`, `activity_group_id`, `grade_level_id`) with `ON DELETE CASCADE`
  on both FKs, audited.
- [ ] **P0** `UpdateActivityGroup` supports replace-set eligible grades and emits
  a domain event on change.
- [ ] **P1** `AddMembership` validates that the student's active grade-for-period
  is in the group's eligible set (or the set is empty = any actively-enrolled
  student).
  - *Source:* `activity-group-enrollment.md` FR-39/40/41; `activity-group-enrollment-impl.md` Phase 7.3/7.4
  - *Files:* `Domain/ActivityGroupGradeLevel.cs`, `CQRS/ActivityGroups/Commands/UpdateActivityGroup/`, `CQRS/ActivityGroups/Commands/AddMembership/`

### 2.4 Capacity per period

- [ ] **P1** Enforce `Capacity` per `(group, period)` when `PeriodId` is set;
  per group overall when `PeriodId` is null (anticipates `OpenEnded`/`DateRange`).
  - *Source:* `activity-group-enrollment.md` FR-13/46; `activity-group-enrollment-impl.md` Phase 7.4
  - *Files:* `CQRS/ActivityGroups/Commands/AddMembership/`

### 2.5 DTO/API updates

- [ ] **P1** Update `ActivityGroupDto` (`is_active`, eligible grades) and
  `MembershipDto` (`period_id`); update `ActivityGroupRoutes`.
  - *Source:* `activity-group-enrollment-impl.md` Phase 7.5
  - *Files:* `Students.Core/DTOs/ActivityGroupDto.cs`, `Students.Api/Endpoints/ActivityGroupRoutes.cs`

### 2.6 Tests

- [ ] **P2** Unit + integration tests for on/off toggle, grade eligibility,
  period-scoped unique constraints, capacity, `NoUncommittedModelChanges`.
  - *Source:* `activity-group-enrollment.md` AC-33/34, AC-8/15/19

---

## Sprint 3 — Enrollment Spans Backend (Rev. 3–4)

*Goal: add `EnrollmentSpan`, `AutoRenewDefault`, window dates, and
 `OpenEnded`/`DateRange` behavior.* Depends on Sprint 2.

### 3.1 Group span fields

- [ ] **P0** Add `EnrollmentSpan` enum (`WholeAcademicYear`, `Termly`,
  `Semester`, `DateRange`, `OpenEnded`) + `EnrollmentStartDate` /
  `EnrollmentEndDate` + `AutoRenewDefault` to `ActivityGroup`. Migration.
  - *Source:* `activity-group-enrollment.md` FR-42; `activity-group-enrollment-impl.md` Phase 8.1
  - *Files:* `Domain/ActivityGroup.cs`, `Domain/EnrollmentSpan.cs`, `Data/Configurations/ActivityGroupConfiguration.cs`

### 3.2 Membership window + auto-renew fields

- [ ] **P0** Add `AutoRenew` + `window_start_date` + `window_end_date` to
  `ActivityGroupMembership`. Migration.
  - *Source:* `activity-group-enrollment.md` FR-49; `activity-group-enrollment-impl.md` Phase 8.2
  - *Files:* `Domain/ActivityGroupMembership.cs`, `Data/Configurations/ActivityGroupMembershipConfiguration.cs`

### 3.3 OpenEnded span behavior

- [ ] **P1** For `OpenEnded`:
  - `EnrollmentEndDate` always null.
  - `PeriodId` null.
  - `window_start_date` set; `window_end_date` null.
  - No rollover.
  - *Source:* `activity-group-enrollment.md` FR-48; `activity-group-enrollment-impl.md` Phase 8.3
  - *Files:* `CQRS/ActivityGroups/Commands/AddMembership/`, domain logic

### 3.4 DateRange span behavior

- [ ] **P1** For `DateRange`:
  - Group defines bounded `[EnrollmentStartDate, EnrollmentEndDate]`.
  - `PeriodId` null; window dates recorded on the membership.
  - Capacity per group overall.
  - `EnrollmentEndDate` passed → no new enrollments.
  - *Source:* `activity-group-enrollment.md` FR-47/52; `activity-group-enrollment-impl.md` Phase 8.4/8.6
  - *Files:* `CQRS/ActivityGroups/Commands/AddMembership/`, domain logic

### 3.5 Span validation in AddMembership

- [ ] **P1** Validate that spanned groups require a matching `PeriodId`
  (full period-type wiring in Sprint 4); `OpenEnded`/`DateRange` require
  `PeriodId` null.
  - *Source:* `activity-group-enrollment.md` FR-43/46; `activity-group-enrollment-impl.md` Phase 8.5
  - *Files:* `CQRS/ActivityGroups/Commands/AddMembership/`

### 3.6 Tests

- [ ] **P2** Unit + integration tests for `OpenEnded` and `DateRange` spans,
  closed-window rejection, capacity modes.
  - *Source:* `activity-group-enrollment.md` AC-36/37, AC-41

---

## Sprint 4 — Period-Aligned Spans (Rev. 3) & Rollover (Rev. 5)

*Goal: wire period-aligned spans to the hierarchy and implement rollover.*
 Depends on Sprint 1 (period hierarchy) and Sprint 3 (spans).

### 4.1 Period-aligned span attachment

- [ ] **P0** For `WholeAcademicYear`/`Termly`/`Semester` groups, `AddMembership`
  attaches to the matching `PeriodType` period of the active academic year.
- [ ] **P0** Reject the add when no matching active period exists.
  - *Source:* `activity-group-enrollment.md` FR-43; `activity-group-enrollment-impl.md` Phase 10.1
  - *Files:* `CQRS/ActivityGroups/Commands/AddMembership/`

### 4.2 Framework compatibility check

- [ ] **P1** On group create, reject `Termly` span if tenant framework ≠ `Terms`,
  and `Semester` span if framework ≠ `Semesters`. `WholeAcademicYear` and
  `OpenEnded`/`DateRange` always allowed.
  - *Source:* `activity-group-enrollment.md` FR-45; `activity-group-enrollment-impl.md` Phase 10.2
  - *Files:* `CQRS/ActivityGroups/Commands/CreateActivityGroup/`

### 4.3 Rollover command

- [ ] **P0** Implement a shared rollover command/handler:
  - At bounded window end (`DateRange` end or period-aligned period end), for
    each active member:
    - `AutoRenew = true` + next window exists → exit current, create new active
      membership in the next window.
    - `AutoRenew = false` → exit current at window end without re-enrolling.
  - `OpenEnded` is a no-op.
  - Always exit the current membership before creating the next (preserves the
    active-uniqueness invariant).
  - *Source:* `activity-group-enrollment.md` FR-50; `activity-group-enrollment-impl.md` Phase 9.2
  - *Files:* `CQRS/ActivityGroups/Commands/RolloverActivityGroup/`, `Domain/ActivityGroupMembership.cs`

### 4.4 Next DateRange window

- [ ] **P0** Add `next_enrollment_start_date`/`next_enrollment_end_date` to
  `ActivityGroup`. Admin sets in advance; reject if next start is before
  current window end.
  - *Source:* `activity-group-enrollment.md` FR-53; `activity-group-enrollment-impl.md` Phase 9.1
  - *Files:* `Domain/ActivityGroup.cs`, `CQRS/ActivityGroups/Commands/SetNextWindow/`

### 4.5 Next period resolution

- [ ] **P1** For period-aligned spans, resolve the next window as the next
  `Period` of the matching `PeriodType` in the tenant's active academic year.
  If none exists, exit all active members at period end.
  - *Source:* `activity-group-enrollment.md` FR-51; `activity-group-enrollment-impl.md` Phase 10.3
  - *Files:* Rollover handler, `IActivePeriodProvider`

### 4.6 Admin-set AutoRenew

- [ ] **P1** Allow admins to set `AutoRenew` on a membership (default = group
  `AutoRenewDefault` = true).
  - *Source:* `activity-group-enrollment.md` FR-49; `activity-group-enrollment-impl.md` Phase 9.4
  - *Files:* `CQRS/ActivityGroups/Commands/SetMembershipAutoRenew/`, API endpoint

### 4.7 Domain events

- [ ] **P1** Emit domain events for window-end / rollover actions.
  - *Source:* `activity-group-enrollment.md` FR-50; `activity-group-enrollment-impl.md` Phase 9.5
  - *Files:* `Domain/Events/ActivityGroupRolledOverEvent.cs` or similar

### 4.8 Scheduled rollover job

- [ ] **P1** Background worker/job that runs rollover for every group whose
  current window has ended.
  - *Source:* `activity-group-enrollment.md` FR-54; `activity-group-enrollment-impl.md` Phase 9.3
  - *Files:* `Students.Worker/Services/ActivityGroupRolloverJob.cs` or equivalent

### 4.9 Tests

- [ ] **P2** Unit + integration tests: rollover into next window, no next
  window → exit all, forced vs scheduled, `AutoRenew` opt-out, period-aligned
  span attachment, framework compatibility.
  - *Source:* `activity-group-enrollment.md` AC-38/39/40/42/43, EC-15..22

---

## Sprint 5 — Assignment ↔ Activity Group Backend + Publish Wiring

*Goal: enable `SelectedGroups` assignments and the activity-group subject
 relationship.* Depends on Sprint 2 (link table + membership model) and Sprint 4
 (stable spans).

### 5.1 Assignment↔group link entity

- [ ] **P0** Create `AssignmentActivityGroup` link entity + configuration +
  migration in `Assignments.Core`.
- [ ] **P0** `LinkAssignmentGroups` command handler with replace-set semantics,
  same-tenant check, and rejection of off (`IsActive = false`) groups.
  - *Source:* `activity-group-enrollment.md` FR-17/21/22; `activity-group-enrollment-impl.md` Phase 3.1/3.2
  - *Files:* `Assignments.Core/Domain/AssignmentActivityGroup.cs`, `Assignments.Core/CQRS/Assignments/Commands/LinkAssignmentGroups/`

### 5.2 Assignment group endpoints

- [ ] **P0** Add Assignments API:
  - `PUT`/`GET /api/assignments/{assignmentId}/groups`
  - `GET /api/activity-groups/{groupId}/assignments` (consumed by the Students
    delete-guard port from Phase 2).
  - *Source:* `activity-group-enrollment.md` FR-17/25; `activity-group-enrollment-impl.md` Phase 3.3
  - *Files:* `Assignments.Api/Endpoints/AssignmentRoutes.cs`, `Students.Api/Services/ActivityGroupAssignmentQueryHttpClient.cs`

### 5.3 Publish recipient resolution for SelectedGroups

- [ ] **P0** Extend `PublishAssignmentCommandHandler`: when
  `TargetAudienceType = SelectedGroups`, resolve the active members of linked
  groups, then resolve their subscribed contacts via the existing
  `IContactResolver` pipeline.
- [ ] **P0** Reject publish if zero linked groups.
  - *Source:* `activity-group-enrollment.md` FR-18/20/23; `activity-group-enrollment-impl.md` Phase 3.4
  - *Files:* `Assignments.Core/CQRS/Assignments/Commands/PublishAssignment/`

### 5.4 Activity-group-owned topic validation

- [ ] **P1** Validate that a `SelectedGroups` assignment's `TopicId` points to a
  topic whose `OwnerType = ActivityGroup` and `OwnerId` matches a linked group.
  Conversely, reject `SelectedGrades` assignments that reference an
  activity-group-owned topic.
  - *Source:* `activity-group-enrollment.md` FR-34; `subject-to-topic-polymorphism.md` EC-1
  - *Files:* `Assignments.Core/CQRS/Assignments/Commands/CreateAssignment/`, `PublishAssignmentCommandHandler`

### 5.5 Delete guard

- [ ] **P1** Ensure `DeleteActivityGroupHandler` still obtains the
  cross-context assignment check via the existing port (already wired in
  shipped Phase 2). No new work unless the port contract needs extending for
  `Assignment.Status`.
  - *Source:* `activity-group-enrollment.md` FR-6, AC-17
  - *Files:* `Students.Core/Services/IActivityGroupAssignmentQuery.cs`, `Students.Api/Services/ActivityGroupAssignmentQueryHttpClient.cs`

### 5.6 Tests

- [ ] **P2** Unit + integration tests: link-set replace, cross-tenant rejection,
  off-group exclusion, publish resolves members → recipients, publish rejected
  with zero groups.
  - *Source:* `activity-group-enrollment.md` AC-12..16, EC-1, EC-7

---

## Sprint 6 — Subject/Topic Bridge Alignment (Rev. 6)

*Goal: add optional `PeriodId` to the `GradeSubjectAssignment` bridge and
 enforce assignment/subject period consistency.* Depends on Sprint 1 (typed
 periods) and Sprint 5 (assignment targeting).

### 6.1 Bridge schema change

- [ ] **P0** Add optional nullable `PeriodId` (FK → `periods`) to
  `GradeSubjectAssignment`. Additive migration; existing rows stay NULL
  (year-spanning).
  - *Source:* `activity-group-enrollment.md` FR-55; `activity-group-enrollment-impl.md` Phase 11.1
  - *Files:* `Domain/GradeSubjectAssignment.cs`, `Data/Configurations/GradeSubjectAssignmentConfiguration.cs`

### 6.2 Bridge validation

- [ ] **P0** For grade-owned topic (`GradeLevelId` set): `PeriodId` must be
  `AcademicYear` or `Term`/`Semester` within the tenant's active academic year
  (or null).
- [ ] **P0** For activity-group-owned topic (`ActivityGroupId` set): `PeriodId`
  must match the group's `EnrollmentSpan` (`Term`/`Semester`/`AcademicYear`),
  or null for `OpenEnded`/`DateRange`.
  - *Source:* `activity-group-enrollment.md` FR-56/57; `activity-group-enrollment-impl.md` Phase 11.2
  - Files: `Domain/GradeSubjectAssignment.cs`, create/update command validators

### 6.3 Assignment/subject consistency

- [ ] **P0** When creating/publishing an assignment, validate that its subject
  is assigned to the target grade/group for a period covering the assignment's
  effective date (null-`PeriodId` year-spanning active on that date, or
  period-aligned assignment whose period contains the date). Recipient
  resolution stays unchanged.
  - *Source:* `activity-group-enrollment.md` FR-58; `activity-group-enrollment-impl.md` Phase 11.3
  - *Files:* `Assignments.Core/CQRS/Assignments/Commands/CreateAssignment/`, `PublishAssignmentCommandHandler`

### 6.4 Tests

- [ ] **P2** Unit + integration tests: term-scoped grade topic, activity-group
  span mismatch, null-`PeriodId` back-compat, out-of-year grade topic
  `PeriodId`, assignment/subject consistency.
  - *Source:* `activity-group-enrollment.md` AC-44..46, EC-23/24

---

## Sprint 7 — Integration, Verification & Cross-Cutting

*Goal: end-to-end validation, docs, and rollout.*

### 7.1 Integration tests

- [ ] **P2** Open academic year → open term → create `Termly` activity group
  → enroll student → verify `period_id` = the term.
  - *Source:* `period-hierarchy-terms-semesters.md` AC-H9; `activity-group-enrollment-impl.md` Phase 10.4
  - *Files:* Integration test project

### 7.2 Playwright smoke

- [ ] **P2** End-to-end: create activity group → add students → create
  group-scoped topic (`OwnerType = ActivityGroup`) → create `SelectedGroups`
  assignment linked to group + topic → publish → only group members' subscribed
  contacts receive it.
  - *Source:* `activity-group-enrollment.md` §11; `activity-group-enrollment-impl.md` Phase 6.2

### 7.3 Back-compat regression tests

- [ ] **P2** Verify tenants with `AcademicYearDivision = None` behave
  identically to the shipped flow (one active year, year-level grade
  enrollment, year-to-year promotion).
  - *Source:* `period-hierarchy-terms-semesters.md` NFR-H4, EC-H5

### 7.4 Feature flag flip

- [ ] **P2** Default `FEATURE:EnableActivityGroups` ON in pilot config;
  update `documents/configuration.md` §2.
  - *Source:* `activity-group-enrollment.md` NFR-11; `activity-group-enrollment-impl.md` Phase 6.1

### 7.5 Cross-cutting checks

- [ ] **P2** `NoUncommittedModelChanges` passes for `StudentsDbContext` and
  `AssignmentsDbContext` after all migrations.
- [ ] **P2** Strict-tenant tests for sub-periods, framework setting, and
  activity-group link queries.
- [ ] **P2** Confirm `IActivePeriodProvider` hybrid-cache invalidation covers
  the new active-academic-year / active-sub-period lookups.

---

## Follow-ups — Period Activation Window (period-activation-window-auto-activation.md)

- [ ] **P2** Implement the FR-AA auto-activation sweep: `PeriodAutoActivationService`
  (`BackgroundService`) in `Students.Worker` + `GetDraftPeriodsDueForActivationAsync`
  repository query + sweep unit tests. Spec-only this round. *Source:*
  `period-activation-window-auto-activation.md` FR-AA1..AA8.

---

## Dependency Graph

```
Sprint 1 (period hierarchy backend)
  │
  ├── unblocks Sprint 2 (activity group model migration)
  │     │
  │     ├── unblocks Sprint 3 (enrollment spans)
  │     │     │
  │     │     └── unblocks Sprint 4 (period-aligned spans + rollover)
  │     │
  │     └── unblocks Sprint 5 (assignment↔group + publish wiring)
  │           │
  │           └── unblocks Sprint 6 (subject/topic bridge alignment)
  │
  └── unblocks Sprint 6 directly (typed periods for bridge PeriodId)

Sprint 7 (integration + verification) runs after Sprints 1–6.
```

---

## Summary Counts

| Sprint | Items | P0 | P1 | P2 |
|---|---:|---:|---:|---:|
| 1 — Period Hierarchy Foundation | 10 | 5 | 3 | 2 |
| 2 — Activity Group Model Migration | 6 | 3 | 2 | 1 |
| 3 — Enrollment Spans | 6 | 1 | 4 | 1 |
| 4 — Period-Aligned Spans + Rollover | 9 | 3 | 4 | 2 |
| 5 — Assignment↔Group + Publish | 6 | 3 | 1 | 2 |
| 6 — Subject/Topic Bridge Alignment | 4 | 3 | 0 | 1 |
| 7 — Integration + Verification | 5 | 0 | 0 | 5 |
| **Total** | **46** | **18** | **14** | **14** |

---

## Source Specs

| Spec | Relevant backend areas |
|---|---|
| `activity-group-enrollment.md` | Domain model, membership, spans, rollover, assignment targeting, recipient resolution |
| `activity-group-enrollment-impl.md` | Phases 1–3 (shipped v1), Phases 7–11 (model migration + alignment) |
| `period-hierarchy-terms-semesters.md` | Period type/parent, active invariant, academic-year division, grade-enrollment guard |
| `period-hierarchy-impl.md` | Phases H1–H5 |
| `subject-to-topic-polymorphism.md` | Topic entity, bridge, assignment/subject owner validation |
| `active-period-per-tenancy.md` | Active-period provider, grade-level wizard gate |

---

## Coordination with UI Backlog

| Backend Sprint | UI Sprint it feeds |
|---|---|
| Sprint 1 — Period Hierarchy Foundation | Sprint 1 — Period Hierarchy Foundation |
| Sprint 2 — Activity Group Model Migration | Sprint 2 — Activity Group Model Extension UI |
| Sprint 3 — Enrollment Spans | Sprint 2 + Sprint 3 — Span fields + span-aware operations |
| Sprint 4 — Period-Aligned Spans + Rollover | Sprint 2 + Sprint 3 — Rollover button, span-aware join |
| Sprint 5 — Assignment↔Group + Publish | Sprint 5 — Assignments UI |
| Sprint 6 — Subject/Topic Bridge Alignment | Sprint 4 — Subject/Topic Alignment |
| Sprint 7 — Integration + Verification | Sprint 6 — Verification & Polish |
