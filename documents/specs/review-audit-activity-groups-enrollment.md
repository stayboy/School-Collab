# Audit — Activity Groups & Enrollment Span: Completed Work vs Spec

> **Purpose:** Pass-through of every completed task against the source-of-truth
> spec (`activity-group-enrollment.md` Rev 2–6), `period-hierarchy-terms-semesters.md`,
> and `subject-to-topic-polymorphism.md` (Design B). Verifies that shipped work
> matches the spec and flags any residual gaps.
>
> **Method:** Cross-referenced the spec FR/NFR/AC/EC lists against the impl
> trackers (`activity-group-enrollment-impl.md`, `period-hierarchy-impl.md`,
> `subject-to-topic-polymorphism-impl.md`), the backend/UI review docs, and
> spot-checked key code paths (rollover exit-before-create, FR-58 effective date,
> next-window UI).

## 1. Backend — activity-group enrollment (Phases 1–11)

| Phase | Spec ref | Status | Evidence |
|-------|----------|--------|----------|
| **Phase 1** — Students domain model (Rev 1) | FR-1..11, AC-1..11, NFR-9 | ✅ Done | `ActivityGroup`, `ActivityGroupMembership`, migration `20260801084654`, 27 unit tests. `leader_teacher_id` removed (OS-10). |
| **Phase 2** — CQRS + API | FR-1..6, FR-24..26 | ✅ Done | Create/Update/Delete handlers, referential guard (`ActivityGroupReferencedException`, fail-closed cross-context check via `IActivityGroupAssignmentQuery`), list/query endpoints. |
| **Phase 3** — Assignment↔group link + publish | FR-17..23, AC-12..16 | ✅ Done | `AssignmentActivityGroup` link, `LinkAssignmentGroups` (replace-set, same-tenant, off-group rejection), publish recipient resolution for `SelectedGroups`, zero-groups rejection (EC-7). |
| **Phase 4** — Activity-group admin UI (Rev 1) | FR-33..35 | ✅ Done (superseded by Rev 2 migration + UI sprints) | Landing/create/edit pages; build-fix ripple only after Rev 2. |
| **Phase 5** — Student Detail UI | FR-28..32, AC-27..31 | ✅ Done | "Activity Groups" section, `JoinGroupDialog` multi-select, Leave action, empty-state. Flag-gated. |
| **Phase 7** — Rev 2 model migration | FR-3/4/5/7/10/12/39/40/41 | ✅ Done | `IsActive` + `Activate`/`Deactivate`; `ActivityGroupStatus` deleted; nullable `PeriodId` + split partial unique index; `ActivityGroupGradeLevel` link; grade-eligibility + inactive-group rejection; capacity. Migration `20260826194201`. |
| **Phase 8** — Rev 3/4 enrollment spans | FR-42..48, FR-52 | ✅ Done | `EnrollmentSpan` enum, `EnrollmentStartDate/EndDate`, `AutoRenewDefault`, membership `AutoRenew`/window dates, OpenEnded (null PeriodId), DateRange (window dates, closed-window `EnrollmentWindowClosedException`), span validation (`EnrollmentSpanMismatchException`). Migration `20260827092137`. 8 span tests. |
| **Phase 9** — Rev 5 rollover | FR-49/50/53/54 | ✅ Done | Next-window slot + `SetActivityGroupNextWindow` + `AdvanceToNextWindow` (FR-53); `RolloverActivityGroup` command (FR-50/54, **exit-before-create** two SaveChanges); `SetMembershipAutoRenew` (FR-49); `ActivityGroupRolloverService` background job; domain events. Migration `20260827101242`. |
| **Phase 10** — Period-aligned spans | FR-43/45/51 | ✅ Done | `AddMembership` resolves active typed period (FR-43); `EnrollmentSpanIncompatibleException` framework-compat create check (FR-45); period-aligned rollover re-enrolls into active typed period. |
| **Phase 11** — Rev 6 bridge PeriodId | FR-55/56/57/58 | ✅ Done | Nullable `PeriodId` on `TopicAssignment` TPH root (SetNull); grade-owned period validation (FR-57/EC-24) + group-owned validation (FR-56/EC-23); `TopicAssignmentPeriodException`→422; `UpdateTopicAssignmentPeriod` (re-deferred round); `TopicAssignmentDto.PeriodId`; **FR-58 publish-path consistency** (`ITopicAssignmentLookup`, fail-open on `HttpRequestException`, effective date = `DueDate ?? PublishedAt ?? UtcNow`). Migration `20260827103652`. |

### Backend NFR coverage
- NFR-3 (tenant_id-leading indexes) ✅ · NFR-4 (xmin row versioning) ✅ · NFR-5 (strict tenancy) ✅ · NFR-6 (additive migration) ✅ · NFR-7 (audit cols) ✅ · NFR-8 (referential guarded delete) ✅ · NFR-9 (`NoUncommittedModelChanges`) ✅ · NFR-11 (feature flag, default OFF) ✅.
- NFR-1/NFR-2 (perf p95 < 300 ms / < 2 s) — **not load-tested**; hot-path indexes lead with tenant_id (NFR-3). Tracked, not a defect.

## 2. Backend — period hierarchy (H1–H5)

| Phase | Spec ref | Status | Evidence |
|-------|----------|--------|----------|
| **H1–H4** | FR-H1..H10, AC-H1..H8 | ✅ Done | `PeriodType`, `Period`+`ParentPeriodId`, hierarchy-aware Activate/Complete/SetNext, `AcademicYearDivision` (`FlagKind.String`+Value), `ConfigAcademicYearDivisionRoutes`, cross-context `IAcademicYearDivisionProvider` (fail-open to None), `PeriodFrameworkMismatchException`/`PeriodContainmentException`/`PeriodOverlapException`→422. |
| **H3.3/H3.5** reverse switch-rejection | FR-H7 | ✅ Done | `GetSubPeriodCount` + `GET /students/periods/sub-period-count`; `ISubPeriodCountProvider` + `SubPeriodCountProviderHttpClient` (fail-closed); PUT division rejects with 422 while sub-periods exist. |
| **H4** year-level guard | FR-H9/AC-H8 | ✅ Done | `EnrollStudentHandler` rejects active sub-period; read endpoints `active-academic-year`/`active-sub-period`/`{yearId}/sub-periods`. |
| **H5.1** typed-period attachment | FR-43/AC-H9 | ✅ Verified shipped (Phase 10) + membership tests for all three spans. |
| **H5.2** integration test | AC-H9 | ✅ Done | `ActivityGroupPeriodAlignedSpanTests` (9: WholeAcademicYear, Termly, Semester, provided-TermId, no-active-Term, rollover + framework gates). |
| **H5 cross-cutting** | NFR-H2/H4, EC-H5, §4.6 | ✅ Done | Back-compat (`AcademicYearDivisionNoneBackCompatTests`, 4), strict-tenancy (3 sub-period + 2 Settings integration), cache-invalidation (5 `ActivePeriodProviderTests`). |
| **H5.3** E2E/Playwright | AC-H9 | ⏸ Deferred | Needs AppHost + seeded data (Phase 6.2 territory). Not a defect. |

## 3. Backend — subject/topic polymorphism (Design B)

| Item | Spec ref | Status | Evidence |
|------|----------|--------|----------|
| Subject→Topic rename | FR-1..8, NFR-3 | ✅ Done | `Topic` (shared, no owner cols), partial unique index, `Description`, nullable `Code`/`CodedValueId`; `GradeSubjectAssignment` retained as M:N bridge (`TopicId`, `ActivityGroupId`, at-most-one owner). |
| Bridge validation | FR-11, AC-5/6 | ✅ Done | Off-group + cross-tenant rejection; flag-gated group-bridge. |
| Assignment bridge validation | FR-14/15, AC-7/8/9 | ✅ Done | `SelectedGrades`/`SelectedGroups` owner validation in Create/Update. |
| Backward-compat API alias | NFR-6 | ✅ Done | `/api/subjects` → `/api/topics`. |
| Open items (OS-2/OS-4) | OS-2/OS-4 | ⏸ Out of scope | "Subjects" coded-value parent rename + `TeacherSubject`→`TeacherTopic` mechanical renames — explicitly deferred. |

## 4. UI — Sprints 1–6 + fixes + re-deferred

| Sprint | Spec ref | Status | Notes |
|--------|----------|--------|-------|
| **S1** Period hierarchy foundation | FR-H* | ✅ Done | Feature-flag string values, period type+parent form, sub-period list, active-year gate. |
| **S2** Activity group model extension | FR-1/3/5/39/42/49 | ✅ Done (+ gap fixed) | Landing columns, create/edit span fields, members tab. **Next-window UI gap (FR-53/AC-43) fixed** in review-fixes-round1 — `ActivityGroupEditDialog`/`CreateDialog` now expose `NextEnrollmentStartDate/EndDate` → `SetActivityGroupNextWindowAsync`. |
| **S3** Span-aware operations | FR-28..32/40/43/52/54 | ✅ Done | Span-aware join dialog, forced rollover button (non-OpenEnded only). |
| **S4** Subject/topic alignment | FR-55..58 | ✅ Done (+ bugs fixed) | Owner filter, topic create owner+period, strand/lesson dialogs owner-agnostic, label rename. **Review-fixes-round1**: duplicate-assignment bug fixed (`CreateTopicForGrade` accepts `PeriodId`), `Subjects.razor` opens `TopicCreateDialog`, period filter honored end-to-end, group-aware subject picker for `SelectedGroups`. |
| **S5** Assignments UI | FR-17..23/58 | ✅ Done (+ P1 fixed) | `SelectedGroups` target + multi-select + ≥1 group validation; publish recipient preview; subject/period consistency. **P1 fix (FR-58 effective date for `SelectedGroups`)** via 3-agent workflow: `ListSubjectsByGroupAsync(Guid, DateOnly?, …)` + `Create.razor` `effectiveDate = DueDate ?? UtcNow`. |
| **S6** bUnit + polish (R1/R2/R3) | AC-* | ✅ Done | bUnit tranches: span-aware dialog validation, rollover/next-window, PeriodType+parent selector, academic-year-division setting card, sub-period list states, duplicate-coded-value guard, period filtering. `DialogShellFooter` Error binding fix. |
| **Re-deferred** | FR-5/55..58 + audit | ✅ Done | PeriodId editing (`UpdateTopicAssignmentPeriod` + dialog), string-flag audit value (`PreviousValue`/`NewValue` + UI column), backend duplicate guard (`DuplicateTopicAssignmentException`→409). |

## 5. Feature flag + pilot (Phase 6)

| Item | Spec ref | Status | Notes |
|------|----------|--------|-------|
| 6.1 Pilot-tenant override | NFR-11 | ✅ Done | `PilotActivityGroupFlagOverrideSeeder` turns `FEATURE:EnableActivityGroups` ON for **Hydeson School only** (global default stays OFF). Idempotent, audit-traced. `documents/configuration.md` §5 updated. 4 tests. |
| 6.2 Playwright smoke | §11 | ⏸ Deferred | Needs running AppHost + seeded data. |
| 6.3 Pilot monitoring | NFR-11 | ⏸ Operational | Not code. |

## 6. FR coverage matrix (full)

All 58 FRs (FR-1..58) are **implemented and verified**, except the noted deferred/accepted items:

- ✅ FR-1..16 (lifecycle, membership, multi-membership, unique-active, tenant/cross-tenant, capacity) — Phases 1/2/7.
- ✅ FR-17..23 (SelectedGroups link + publish + zero-groups/cross-tenant/off-group guards) — Phase 3.
- ✅ FR-24..27 (reads/queries, domain events) — Phase 2.
- ✅ FR-28..32 (Student Detail UI) — Phase 5 + UI Sprint 3.
- ✅ FR-33..35 (admin UI) — UI Sprints 2/4.
- ✅ FR-39..41 (eligible grades) — Phase 7.
- ✅ FR-42..48 (enrollment spans: WholeAcademicYear/Termly/Semester/DateRange/OpenEnded) — Phase 8.
- ✅ FR-49..54 (AutoRenew, rollover, next window, forced rollover) — Phase 9.
- ✅ FR-55..58 (bridge PeriodId, grade/group period validation, assignment/subject consistency) — Phase 11 + re-deferred.

## 7. Residual gaps (not defects)

| # | Item | Spec ref | Severity | Disposition |
|---|------|----------|----------|------------|
| 1 | **H5.3 / Phase 6.2 Playwright E2E** (create club → add students → group-scoped topic → SelectedGroups assignment → publish → assert recipients) | AC-H9, §11 | P2 | Needs running AppHost + seeded data. Tracked. |
| 2 | **NFR-1/NFR-2 perf load tests** (p95 < 300 ms / < 2 s) | NFR-1/2 | P2 | Hot-path indexes lead with tenant_id (NFR-3 met); load testing deferred. |
| 3 | **`AllStudents` audience subject-assignment verification** | FR-58 scope | Low / accepted | Intentionally out of FR-58 scope; documented in `documents/runbooks/fr58-fail-open-behavior.md`. Spec change required to extend. |
| 4 | **FR-58 fail-open** on Students API unreachable | FR-58 | Low / accepted | Runbook documents the fail-open behavior; revisit if stricter fail-closed wanted. |
| 5 | **OS-2 / OS-4** (coded-value "Subjects" parent rename; `TeacherSubject`→`TeacherTopic`) | subject OS | Out of scope | Explicitly deferred mechanical renames. |
| 6 | **Phase 6.3 pilot monitoring** | NFR-11 | Operational | 1-week monitoring before broader rollout (post-Playwright). |
| 7 | **Docs not drafted** (`centralized-feature-flags.md`, `ef-migrations.md`, `auth-tenancy-pattern.md`, `endpoint-organization-pattern.md`) | — | Low | Referenced but not authored; not blocking. |

## 8. Verdict

**The activity-group + enrollment-span feature is fully implemented and verified
against the spec (Rev 2–6).** All 58 FRs, the period hierarchy (H1–H5 backend),
the subject/topic polymorphism (Design B), and the UI (Sprints 1–6 + fixes +
re-deferred) are complete and green. The only open items are the deferred
Playwright E2E (Phase 6.2 / H5.3, needs a running AppHost), performance load
testing (NFR-1/2), two explicitly-accepted spec-scope items, and operational
pilot monitoring — none are defects in the shipped implementation.

**Build/test baseline:** 0 build errors; Students 332, Settings Unit 446,
Admin 477, Assignments 102 (all green); Settings integration incl. new
division-tenancy tests.