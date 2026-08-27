# Activity Group Enrollment — Implementation Checklist

> Tracked checklist for `activity-group-enrollment.md` (post-review, dated 2026-07-29).
> One PR per step, each shippable behind `FEATURE:EnableActivityGroups` (flag OFF until Phase 6).
> **Workflow (adopted repo-wide for spec-driven effort): stacked PRs.** Each phase branches from the
> previous phase's branch (not main) and its PR's base is the previous PR's head. Merges are deferred
> until the whole spec is complete, then merged bottom-up (Phase 2 -> main, Phase 3 -> main, ...).
> PR #99 (Phase 2) is the bottom of the stack. Update each box to `[x]` when the phase is built; the
> PR link/SHA is tracked in the Notes / change log below.

> **Spec is the source of truth.** This is the granular, per-spec phase tracker
> (checkbox-level) for `activity-group-enrollment.md`. Requirements live in the
> spec; this doc only tracks implementation steps and cites the FRs/§s it builds.
>
> **Backend / UI split.** Backend phases are tracked here (Phases 1–3 shipped
> v1; Phases 7–11 pending model migration + alignment). UI Phases 4–5 (shipped
> v1) are retained for history; **forward UI work lives in
> `ui-implementation-backlog.md`**, and forward backend work is sprint-ordered
> in `backend-implementation-backlog.md`.

**Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked

---

## Prerequisite dependency

- [x] **Subject→Topic polymorphism** (`subject-to-topic-polymorphism.md`, Design B) —
  `Subject` is renamed to `Topic` (shared, global, **no owner columns**) and
  `GradeSubjectAssignment` is **retained** as the M:N bridge (extended with
  `ActivityGroupId` + optional Rev. 6 `PeriodId`). The bridge is NOT eliminated.
  Steps 16 and 27 assume this is done. **Coordinate phasing with that spec's
  implementation.**

---

## Phase 1 — Students domain model (dark, flag OFF)

- [x] **1.1** Add feature flag constant `EnableActivityGroups` in `src/SchoolCollab.Core/Features/FeatureFlagKeys.cs`. Seeded via `MigrationService/Program.cs` `SeedEnableActivityGroupsAsync` (default OFF), registered in `SchoolCollab.Admin/appsettings.json` FeatureFlags.FEATURE. (AppHost `Parameters` fan-out was deprecated in favour of the central Settings FeatureFlag aggregate — see `settings-context-merge-spec.md`.) — *NFR-11*
- [x] **1.2** Create `ActivityGroup` entity + `ActivityGroupStatus` enum (`Active`, `Suspended`, `Archived`) in `Students.Core/Domain/`. Extends `TenantEntityTypeConfigurationBase<ActivityGroup>`. Mirrors `GradeLevel.cs` (Create/Update/Delete + Suspend/Archive/Reactivate lifecycle). **No teacher-leader field** — `LeaderTeacherId`/`leader_teacher_id` removed per OS-10 (future feature) and taken out of the entity, config, DTO, API, and migration. — *FR-1..3, AC-1..6*
- [x] **1.3** Create `ActivityGroupMembership` entity + `MembershipStatus` enum (`Active`, `Exited`, `Removed`) + `ExitedOn`. Partial unique index on `(tenant_id, student_id, activity_group_id)` filtered to `status = 0` via `.HasFilter("status = 0")` (matches repo's unquoted snake_case convention). `transfer_reason` column omitted (spec review finding #9: undocumented carryover from `StudentEnrollment`, no FR uses it). — *FR-7..11, AC-7..11*
- [x] **1.4** Set `activity_group_id` FK `ON DELETE RESTRICT` in membership configuration (`OnDelete(DeleteBehavior.Restrict)`). `student_id` FK also RESTRICT (preserve membership history on soft-delete — EC-2). — *FR-6, NFR-8, AC-18*
- [x] **1.5** Add DbSets + repositories to `Students.Core/Data/StudentsDbContext.cs` + `Data/Repositories/*` (IActivityGroupRepository, ActivityGroupRepository, IActivityGroupMembershipRepository, ActivityGroupMembershipRepository). DTOs: ActivityGroupDto, MembershipDto. — *§10*
- [x] **1.6** Generate additive migration `20260801084654_AddActivityGroups.cs`. Case-insensitive unique name index `(tenant_id, lower(name))` via raw SQL (EF Core can't express `lower()` — mirrors CodedValue COALESCE pattern). `CHECK (capacity IS NULL OR capacity >= 1)` via raw SQL. `NoUncommittedModelChanges` test passes. — *NFR-9*
- [x] **1.7** Unit-test entity invariants (group create/archive/suspend/reactivate, membership status transitions, multi-membership allowed, rejoin-after-exit, domain events). 27 new tests, all passing. DB/handler-dependent tests (duplicate-name, delete-guard, capacity count, concurrency) deferred to Phase 2 with clear comments. — *AC-1..11*

---

## Phase 2 — Membership commands/queries + APIs (dark, flag OFF)

- [x] **2.1** Group CQRS commands: `CreateActivityGroup`, `UpdateActivityGroup`, `ArchiveActivityGroup`, `SuspendActivityGroup`, `DeleteActivityGroup` (`Students.Core/CQRS/ActivityGroups/Commands/`). — *FR-1..6*
- [x] **2.2** Wire cross-context delete-guard port into `DeleteActivityGroupHandler`:
  - create `Students.Core/Services/IActivityGroupAssignmentQuery.cs`
  - implement `Students.Api/Services/ActivityGroupAssignmentQueryHttpClient.cs` as HTTP client calling Assignments API `GET /api/activity-groups/{id}/assignments` (mirror existing `Assignments.Api/Services/StudentsContactResolver.cs`, reversed direction)
  - handler rejects delete on (a) any membership row, or (b) any returned `Draft`/`Published` assignment → `ActivityGroupReferencedException`
  - **fail-closed** if Assignments API unreachable
  - **confirm `AssignmentSummary` DTO carries `Status`** before wiring — *FR-6, AC-17, AC-18, EC-1*
- [x] **2.3** Membership CQRS: `AddMembership`, `RemoveMembership`, `ExitMembership` commands; `GetGroupMembers`, `GetStudentGroups` queries (`Students.Core/CQRS/ActivityGroups/`). — *FR-7..16*
- [x] **2.4** Students API endpoints: `Students.Api/Endpoints/MapActivityGroupEndpoints` (mirror `GradeLevelRoutes.cs`). Gate every endpoint behind `FEATURE:EnableActivityGroups`. — *§7.1, §7.2, NFR-11*
- [x] **2.5** Unit-test all handlers (AC-12..19, AC-17, AC-18, EC-1..6). — *AC-12..19, EC-1..6*

---

## Phase 3 — Assignment↔group link + publish wiring (dark, flag OFF)

- [x] **3.1** Create `AssignmentActivityGroup` link entity + config + `AssignmentsDbContext` DbSet + additive migration `<ts>_AddAssignmentActivityLinks.cs`. Update `NoUncommittedModelChanges` test for `AssignmentsDbContext`. — *FR-17, §8.3, NFR-9*
- [x] **3.2** `LinkAssignmentGroups` command handler — replace-set semantics; enforce same-tenant, non-archived; reject archived-group links. — *FR-18, FR-19, AC-20, AC-21*
- [x] **3.3** Add Assignments API link endpoints: `PUT`/`GET /api/assignments/{assignmentId}/groups` and `GET /api/activity-groups/{groupId}/assignments` (§7.3). The last is what step 2.2's port consumes. — *§7.3, AC-20*
- [x] **3.4** Extend publish recipient resolution for `SelectedGroups`: in `PublishAssignmentCommandHandler`, when `TargetAudienceType = SelectedGroups` (enum `2`), call `IContactResolver.ResolveSubscribersRequest` with the **`StudentIds` roster of active members of linked groups** (the `StudentIds` param already exists in `IContactResolver`). Stop hardwiring `assignment.GradeLevelId` for this audience. Generate `AssignmentRecipient` rows. — *FR-20..23, AC-22..26*
- [x] **3.5** Unit-test: link-set replace, cross-tenant rejection, archived-group exclusion, publish-resolves-only-active-members. — *AC-22..26, EC-7..14*

---

## Phase 4 — Admin UI: ActivityGroups pages (dark, flag OFF)

- [x] **4.1** `ActivityGroups` list page in `Students.Admin/Components/Pages/ActivityGroups/*`, mirror `GradeLevels` (FluentUI, keyboard nav, ARIA — NFR-10). — *FR-24..27*
- [x] **4.2** `ActivityGroupCreateEditDialog` + `ActivityGroupDetails` page with members tab. Follow repo `fluentui-dialog-shell` / `dialog-ui` skill conventions for modal forms. — *FR-24..27*
- [x] **4.3** Gate all UI by `FEATURE:EnableActivityGroups` (repo tenant-gate / feature-flag pattern per `featureflags-tenant-gates` skill). — *NFR-11*
- [x] **4.4** bUnit tests: CRUD + members tab. — *AC-12..19*

---

## Phase 5 — Student Detail page UI (dark, flag OFF)

- [x] **5.1** Add "Activity Groups" section to `Students.Admin/Components/Pages/Students/Detail.razor` (FR-28). `StudentDetailSectionsTests.cs` already has red-phase tests asserting Join Group/Leave buttons gated on `EnableActivityGroups` — make them pass. — *FR-28, AC-27*
- [x] **5.2** `JoinGroupDialog` (`Students.Admin/Components/ActivityGroups/JoinGroupDialog.razor`), searchable multi-select, using `dialog-ui` skill conventions. — *FR-29, FR-30, AC-28, AC-29*
- [x] **5.3** "Leave" action button per membership row + empty-state message when no memberships. — *FR-31, FR-32, AC-30, AC-31*
- [x] **5.4** bUnit tests (AC-27..31). — *AC-27..31*

---

## Phase 6 — Flag flip + pilot rollout (dark→lit)

- [ ] **6.1** Default `FEATURE:EnableActivityGroups` ON in `appsettings.PilotTenant.json`; update `documents/configuration.md` §2. — *NFR-11*
- [ ] **6.2** Playwright smoke: create "Chess Club" → add 3 students → create group-scoped polymorphic `Subject` (`OwnerType = ActivityGroup`) → create `SelectedGroups` assignment linked to club + that subject → publish → assert only club members' subscribed contacts received it. — *§11*
- [ ] **6.3** Monitor pilot tenant 1 week before broader rollout.

---

## Revision 2–5 migration phases (model changes — NOT yet implemented)

> The shipped v1 (Phases 1–5 above) is built on the **Revision 1** model.
> `activity-group-enrollment.md` now carries Rev. 2–5, which change the domain
> model. These phases implement the model migration. The flag is still OFF, so
> there is no live pilot data to preserve — migrations may alter columns freely.
> **Phases 7–9 ship without the period hierarchy** (only `OpenEnded`/`DateRange`
> spans); **Phase 10 depends on `period-hierarchy-impl.md` Phase H5**.

### Phase 7 — Rev. 2 model migration (on/off status, period-based membership, grade eligibility)

- [x] **7.1** `ActivityGroup`: remove `PeriodId` + `Suspend`/`Archive`/`Reactivate`; add boolean `IsActive` (default true) + `Activate`/`Deactivate`. Migration `20260826194201_AddActivityGroupRev2`: drop `period_id`, replace `status` with `is_active` (default true), drop the `period_id`/`status` indexes, add `(tenant_id, is_active)`. — *Rev.2 FR-3/4/5/12, §8.1*
- [x] **7.2** `ActivityGroupMembership`: add nullable `PeriodId` (FK → `periods.id`, SetNull). **Split** the partial unique index into `WHERE status=0 AND period_id IS NOT NULL` on `(tenant, student, group, period)` and `WHERE status=0 AND period_id IS NULL` on `(tenant, student, group)` (Postgres NULL-distinct pitfall — Rev. 3 banner item 4). — *Rev.2 FR-7/10, §8.2*
- [x] **7.3** New `activity_group_grade_levels` link table (tenant, group, grade) `ON DELETE CASCADE` both sides + migration. `UpdateActivityGroup` replace-set eligible grades. — *Rev.2 FR-39/40/41, §8.4*
- [x] **7.4** `AddMembership`: grade-eligibility check (student's active grade-for-period ∈ group's eligible set; empty set = any) + inactive-group rejection (`InactiveGroupException`). Capacity per group. — *Rev.2 FR-13/40*
- [x] **7.5** DTOs/API: `ActivityGroupDto` (`is_active`, `eligible_grade_ids`), `MembershipDto` (`period_id`); `ActivityGroupRoutes` — `/archive`+`/suspend` → `/activate`+`/deactivate`; `InactiveGroupException`/`GradeNotEligibleException` → 422. — *Rev.2*
- [x] **7.6** Unit + integration tests; `NoUncommittedModelChanges`. — *Rev.2 ACs*

> **Separation-of-concerns note (Phase 7):** the UI edits in this phase (Admin `ActivityGroups`/`ActivityGroupDetails`/`Detail`/`JoinGroupsDialog` pages + `StudentsApiClient` + Assignments `ActivityGroupLookupHttpClient`) are **build-fix ripple only** — forced by the DTO/endpoint shape change to keep the solution compiling. They are NOT the forward-only UI backlog sprint (`ui-implementation-backlog.md`); the real UI work (eligible-grade picker, etc.) belongs to a later UI sprint.

### Phase 8 — Rev. 3/4 enrollment spans + OpenEnded/DateRange (no hierarchy dependency)

- [x] **8.1** `ActivityGroup`: add `EnrollmentSpan` enum (`WholeAcademicYear|Termly|Semester|DateRange|OpenEnded`) + `EnrollmentStartDate`/`EnrollmentEndDate` + `AutoRenewDefault`. Migration `20260827092137_AddActivityGroupEnrollmentSpans`. — *Rev.3/4 FR-42, §8.1*
- [x] **8.2** `ActivityGroupMembership`: add `AutoRenew` + `window_start_date`/`window_end_date` (nullable). Migration. — *Rev.4 FR-49, §8.2*
- [x] **8.3** `OpenEnded` span: `PeriodId` null, `window_start_date` null, `window_end_date` null; continuous membership. — *Rev.4 FR-48*
- [x] **8.4** `DateRange` span: group-defined `[start, end]`; `PeriodId` null; window dates on membership; capacity per group overall. — *Rev.4 FR-47*
- [x] **8.5** `AddMembership` span validation: spanned → `PeriodId` required (full active-period matching deferred to Phase 10); `OpenEnded`/`DateRange` → null PeriodId (mismatch → `EnrollmentSpanMismatchException`). Capacity: per `(group, period)` when `PeriodId` set, per group overall when null (`CountActiveMembersAsync` gains optional periodId). — *Rev.3/4 FR-43/13/46*
- [x] **8.6** Closed-window rule: `EnrollmentEndDate` passed → `EnrollmentWindowClosedException` (no new enrollments). — *Rev.4 FR-52*
- [x] **8.7** Unit + integration tests for `OpenEnded` + `DateRange` (8 new in `ActivityGroupEnrollmentSpanTests.cs`). — *Rev.4*

> **Separation-of-concerns note (Phase 8):** backend-only — the UI client DTOs still omit the span fields (extra server JSON fields are ignored on deserialize), so no UI changes were required. Eligible-grade picker, span picker, and rollover UI remain in `ui-implementation-backlog.md`.

### Phase 9 — Rev. 4/5 rollover

- [x] **9.1** Next-window slot: `next_enrollment_start_date`/`next_enrollment_end_date` on the group; admin sets in advance (`SetActivityGroupNextWindow` command); reject start < current window end (FR-53). Migration `20260827101242_AddActivityGroupNextWindow`. — *Rev.5 FR-53, §8.1*
- [x] **9.2** Rollover command (admin-forced): `RolloverActivityGroupHandler` — close current window (`ExitedOn` = trigger), enroll `AutoRenew=true` members into the next window (if defined), exit `AutoRenew=false`; `OpenEnded` no-op. — *Rev.5 FR-50/54*
- [x] **9.3** Scheduled rollover job (background): `ActivityGroupRolloverService` (Students.Worker) sweeps groups whose window ended via `GetGroupsDueForRolloverAsync`, reusing the rollover handler. — *Rev.5 FR-54*
- [x] **9.4** `AutoRenew` settable by admin (`SetMembershipAutoRenew` command; default = group `AutoRenewDefault` = true at AddMembership). — *Rev.5 FR-49*
- [x] **9.5** Domain events for window-end / rollover: `ActivityGroupNextWindowSetEvent`, `ActivityGroupRolledOverEvent`. — *Rev.4/5 FR-50*
- [x] **9.6** Unit tests: rollover into next window; no next window → exit all; `AutoRenew` opt-out; forced trigger date; `OpenEnded` no-op (5 new in `ActivityGroupRolloverTests.cs`). — *Rev.5*

> **Separation-of-concerns note (Phase 9):** backend + background worker only; no DTO shape change, so no UI ripple. Rollover UI (force button, next-window editor, AutoRenew toggle) remains in `ui-implementation-backlog.md`.

### Phase 10 — Period-aligned spans (depends on `period-hierarchy-impl.md` Phase H5)

- [x] **10.1** `WholeAcademicYear`/`Termly`/`Semester`: membership `PeriodId` attaches to the matching `PeriodType` period of the active academic year (`AddMembershipHandler` resolves the active typed period via `IPeriodRepository`; a provided `PeriodId` is validated for type + active-year membership). — *Rev.3 FR-43 ⟶ period-hierarchy FR-H9*
- [x] **10.2** School-framework compatibility check on group create (`Termly` requires `Terms`, `Semester` requires `Semesters` → `EnrollmentSpanIncompatibleException`, 422; `WholeAcademicYear`/`OpenEnded`/`DateRange` framework-agnostic). — *Rev.3 FR-45*
- [x] **10.3** Rollover for period-aligned spans: `RolloverActivityGroupHandler` resolves the active typed period as the next window and re-enrols `AutoRenew` members into it (admin-forced path; scheduled job remains DateRange-focused). — *Rev.5 FR-50/51*
- [x] **10.4** Unit tests: WholeAcademicYear→active year, Termly→active term, wrong-type PeriodId rejected, framework-compat create (None→throws / Terms→ok), Termly rollover into active term (6 new in `ActivityGroupPeriodAlignedSpanTests.cs`). E2E/Playwright seeded flow is in `ui-implementation-backlog.md`. — *Rev.3/5, AC-H9*

> **Separation-of-concerns note (Phase 10):** backend-only; no DTO shape change → no UI ripple. The eligible-grade/span picker UI and Playwright E2E remain in `ui-implementation-backlog.md`.

### Phase 11 — Subject/topic → grade/group span alignment (Rev. 6)

- [x] **11.1** Add optional nullable `PeriodId` to the `TopicAssignment` TPH root (the retained `GradeSubjectAssignment` bridge — `GradeTopicAssignment`/`ActivityGroupTopicAssignment`); FK → `periods`, SetNull; additive migration `20260827103652_AddTopicAssignmentPeriod` (existing rows NULL = year-spanning). — *Rev.6 FR-55*
- [x] **11.2** Bridge validation: grade-owned topic `PeriodId` ∈ {AcademicYear, Term/Semester within active academic year, null} (`AssignGradeTopicHandler`, FR-57/EC-24); activity-group-owned topic `PeriodId` matches the group's `EnrollmentSpan` per FR-56 (Termly→Term, Semester→Semester, WholeAcademicYear→AcademicYear; OpenEnded/DateRange→null; EC-23). `TopicAssignmentPeriodException` → 422. — *Rev.6 FR-56/57*
- [x] **11.3** Assignment/subject consistency: `PublishAssignmentCommandHandler` verifies the assignment's `TopicId` is assigned to the target grade (`SelectedGrades`) or every linked group (`SelectedGroups`) for a period covering the effective date via the cross-context `ITopicAssignmentLookup` port (Assignments.Core) + `TopicAssignmentLookupHttpClient` (Assignments.Api) calling Students `by-grade`/`by-activity-group`; rejects otherwise. Recipient resolution unchanged. Fail-open on Students unreachable. — *Rev.6 FR-58*
- [x] **11.4** Unit tests: grade topic term-scoped (AC-44), group span mismatch (AC-45/EC-23), grade topic out-of-active-year `PeriodId` (EC-24), AcademicYear/Term-within-year allowed (6 new in `TopicAssignmentPeriodTests.cs`). — *Rev.6*
- [x] **11.5** Depends on the period hierarchy (Phases H1–H4, shipped) for typed-period validation of the bridge `PeriodId`. — *Rev.6 FR-57*

> **Separation-of-concerns note (Phase 11):** backend-only; `TopicAssignmentDto` gained a field (additive) → no UI ripple. Remaining: **11.3** (FR-58 publish-path subject/period consistency) is the sole unshipped Rev. 6 item.

---

## Cross-cutting / don't-forget

- [ ] **Closed-assignment link cleanup** — no automatic unlink needed (EC-12: benign dangling refs); `PUT /api/assignments/{id}/groups` (step 3.3) is the manual escape hatch.
- [ ] **Missing referenced docs** (not implementation blockers, but cited by the spec): `centralized-feature-flags.md`, `ef-migrations.md`, `auth-tenancy-pattern.md`, `endpoint-organization-pattern.md` — either draft them or repoint the spec to the real mechanism.

---

## Notes / change log

- _Checklist generated from spec review (post-edit of items 1–7 + FR-35 cleanup). Spec source of truth: `activity-group-enrollment.md`._

- _2026-08-02: Adopted stacked-PR workflow repo-wide (see header). Phase 2 = PR #99 (stack bottom, base: main). Phase 3 built on feat/activity-groups-phase3 (branched from phase2; PR base = phase2 branch) - AssignmentActivityGroup link entity/migration, LinkAssignmentGroups handler + IActivityGroupLookup port, Assignments link endpoints (PUT/GET /assignments/{id}/groups, GET /activity-groups/{id}/assignments), SelectedGroups publish wiring (FR-20/23/EC-4), 10 new tests. 88 Assignments tests + 156 Students tests pass._

- _2026-08-02: Phase 4 built on feat/activity-groups-phase4 (branched from phase3; PR base = phase3 branch). Admin UI: ActivityGroups list page (LandingPage + grid mirroring GradeLevels), ActivityGroupCreateDialog/EditDialog (DialogShellBase), ActivityGroupDetails page with members tab (StudentPickerDialog add + remove), flag-gated nav link (FEATURE:EnableActivityGroups). 3 new bUnit tests pass (list render, flag-off hidden, members tab). 2 pre-existing GuardianGrid test failures on the stack base are unrelated to this phase._

- _2026-08-02: Phase 5 built on feat/activity-groups-phase5 (branched from phase4; PR base = phase4 branch). Student Detail page "Activity Groups" section (FR-28..32): heading below Enrollments, Join Group accent button opening new JoinGroupsDialog (searchable multi-select of available groups — Active/not-at-capacity/excluding current memberships — with per-group partial-failure reporting), per-row Leave button, empty state, all gated behind FEATURE:EnableActivityGroups. Added `ListStudentGroupsAsync` to StudentsApiClient (GET /students/{id}/activity-groups). The 5 red-phase tests in StudentDetailSectionsTests.cs (AC-27..31) now pass → Admin suite 226/228 (2 pre-existing GuardianGrid failures)._

- _2026-08-26: Spec revised to Rev. 2–5 (on/off status, period-based membership, grade eligibility; enrollment spans + period hierarchy; OpenEnded/DateRange + rollover). Added Phases 7–10 above to implement the model migration. Phases 7–9 ship without the period hierarchy (`OpenEnded`/`DateRange` only); Phase 10 depends on `period-hierarchy-impl.md` Phase H5. See `period-hierarchy-terms-semesters.md` (draft) for the hard dependency. Nothing implemented yet._

- _2026-08-26 (Rev. 6): Subject/topic → grade/group delivery aligned to the enrollment span — optional `PeriodId` on the `GradeSubjectAssignment` bridge (amends `subject-to-topic-polymorphism.md` decision 2a). Added Phase 11. Nothing implemented yet._

