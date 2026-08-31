# UI Work Review — Sprints 1–5 vs. Spec

**Scope:** review the Blazor UI shipped in Sprints 1–5 of `ui-implementation-backlog.md` against the authoritative specs (`activity-group-enrollment.md`, `period-hierarchy-terms-semesters.md`, `subject-to-topic-polymorphism.md`).

**Verdict:** the broad scaffolding is in place and the solution builds/tests green, but **there are material spec gaps and one duplicate-assignment bug** that must be fixed before Sprint 6 (verification) is meaningful. The most urgent issue is in Sprint 4 (topic create/assignment period handling); the most visible missing feature is the `DateRange` next-window UI (FR-53).

---

## 1. Review Method

- Read each completed UI file against the matching spec section.
- Cross-checked API contracts (`StudentsApiClient`, `AssignmentsApiClient`) for field coverage and query-parameter semantics.
- Verified backend support exists where the UI claims it (e.g., `SetActivityGroupNextWindow`, `AssignGradeTopic`/`AssignActivityGroupTopic` with `PeriodId`).
- Did **not** yet run Playwright/bUnit — this is a code/spec review; Sprint 6 is still open.

Build/test state at time of review:
- Full solution build: **0 errors**.
- Students 299/0, Admin 453/0, Assignments 102/0.

---

## 2. Sprint 1 — Period Hierarchy Foundation

| Item | Spec Ref | Status | Finding |
|------|----------|--------|---------|
| 1.1 String-valued feature-flag UI | `period-hierarchy-terms-semesters.md` §8.2, FR-H6/7 | ✅ | `ConfigFlagDetail.razor` correctly distinguishes `FlagKindDto.String`, shows the global default value read-only, and offers a `None`/`Terms`/`Semesters` override selector. |
| 1.1 Audit log value display | NFR-H1 | ⚠️ | The audit grid only renders boolean `Before -> After`; string flag value changes are not surfaced. Low-priority polish. |
| 1.2 Period type + parent | FR-H1/2 | ✅ | `PeriodForm.razor` has type selector and parent academic-year dropdown; create/update requests carry `PeriodType` + `ParentPeriodId`; date validation present. |
| 1.3 Sub-period list | FR-H12 | ✅ | `SubPeriods.razor` lists sub-periods with `Type` column and `?parent=` pre-select on create. |
| 1.3 Row actions | FR-H11/FR-H4 | ⚠️ | Sub-period list has no row actions (edit, activate, complete). Backend supports these; UI omits them. |
| 1.4 Active-year gate | `active-period-per-tenancy.md` FR-A6 | ✅ | `GradeLevels/Detail.razor` `OpenStudentCreateAsync` resolves `GetActiveAcademicYearAsync()` and passes it into `StudentCreateDialog`; warns when none is active. |

### Sprint 1 Conclusion
Mostly complete. Minor polish: audit-log string values and sub-period row actions.

---

## 3. Sprint 2 — Activity Group Model Extension

| Item | Spec Ref | Status | Finding |
|------|----------|--------|---------|
| 2.1 Landing columns | FR-3/5/39/42/49/53 | ✅ | `ActivityGroups.razor` shows Status, Span badge, Window, Auto-renew, Eligible grades, Capacity, Members. |
| 2.2 Create dialog span fields | FR-1/5/39/42/49 | ✅ | `ActivityGroupCreateDialog.razor` has span selector, DateRange window pickers, AutoRenewDefault, eligible-grade checkboxes, capacity. |
| 2.2 Next-window inputs | FR-53, AC-43 | ❌ | **Missing.** `DateRange` groups should allow defining the next window in advance (`NextEnrollmentStartDate`/`NextEnrollmentEndDate`). The backend has `SetActivityGroupNextWindow`; the UI never exposes it in create or edit. |
| 2.2 Edit dialog immutability | FR-5 | ✅ | `ActivityGroupEditDialog.razor` renders Span read-only; other fields editable. |
| 2.3 Members tab | FR-47/49 | ✅ | `ActivityGroupDetails.razor` grid shows Window, Auto-renew toggle, Exit/Remove actions. |

### Sprint 2 Conclusion
Core is solid. The **next-window UI is a real gap** that blocks the AC-43 acceptance criterion and the rollover preview flow.

---

## 4. Sprint 3 — Span-Aware Operations

| Item | Spec Ref | Status | Finding |
|------|----------|--------|---------|
| 3.1 Span-aware join dialog | FR-28..32, FR-40/43/52 | ✅ | `JoinGroupsDialog.razor` resolves active sub-period/year, filters by `SpanCompatible` (DateRange window open, period-aligned spans require matching active period type, OpenEnded always allowed). |
| 3.2 Forced rollover | FR-54, EC-21 | ✅ | `ActivityGroupDetails.razor` shows "Roll over" only for non-OpenEnded spans, confirms, calls `RolloverActivityGroupAsync`. |

### Sprint 3 Conclusion
Complete per spec.

---

## 5. Sprint 4 — Subject / Topic Alignment

| Item | Spec Ref | Status | Finding |
|------|----------|--------|---------|
| 4.1 Owner filter | `subject-to-topic-polymorphism.md` Design B | ✅ | `Subjects.razor` has `OwnerType` selector (GradeLevel/ActivityGroup), grade/group dropdowns, label rename to "Topics". |
| 4.1 `PeriodId` query param | FR-55/58 | ❌ | `Subjects.razor` calls `ListSubjectsByGradeAsync(gradeLevelId, PeriodId)`. The client passes `?periodId={PeriodId}`, but the backend `GET /students/subjects/by-grade/{id}` reads `effectiveDate`, not `periodId`. The period filter is **silently ignored**. |
| 4.2 Group-owned topic create | FR-56 | ✅ | `TopicCreateDialog.razor` group path: creates generic topic then calls `AssignActivityGroupTopicAsync(groupId, topicId, start, end, periodId)`. |
| 4.2 Grade-owned topic create — **duplicate assignment bug** | FR-55/57 | ❌ | **Bug.** The grade path calls `CreateTopicForGradeAsync(...)` (which already creates the topic + grade assignment), then if a `PeriodId` is selected calls `AssignGradeTopicAsync(...)` again with the same grade + topic. This creates **two grade topic assignments** for the same topic. |
| 4.2 Period dropdown filtering | FR-56/57 | ⚠️ | The period picker shows **all** periods. It does not filter to: (a) for grade-owned — AcademicYear or Term/Semester within the active academic year; (b) for group-owned — period type matching the selected group's `EnrollmentSpan`. The backend will reject mismatches, but the UX is poor and the duplicate bug above is the bigger issue. |
| 4.2 Dialog wiring | Design B | ❌ | `Subjects.razor` still opens the old `SubjectCreateDialog`, not the updated `TopicCreateDialog`. The new owner/period fields are therefore **only reachable from `GradeLevels/Detail.razor`**, not from the main Topics landing. |
| 4.3 Strand/lesson dialogs | FR-16..19 | ✅ | `TopicStrandsDialog` operates on `TopicId` and is owner-agnostic; no grade-specific assumptions found. |
| 4.4 Label rename | Design B | ✅ | `Subjects.razor` title, buttons, placeholders, dialog titles use "Topic" terminology. |

### Sprint 4 Conclusion
**This is the weakest sprint.** The duplicate-assignment bug must be fixed before any period-scoped grade topic can be created safely. The `Subjects.razor`/`SubjectCreateDialog` wiring is inconsistent with the new `TopicCreateDialog`. The `PeriodId` query-param mismatch makes the landing's period filter non-functional.

---

## 6. Sprint 5 — Assignments UI

| Item | Spec Ref | Status | Finding |
|------|----------|--------|---------|
| 5.1 `SelectedGroups` target | FR-17..23, FR-34 | ✅ | `Create.razor` shows group multi-select when `SelectedGroups`, validates ≥1 group, creates assignment, then calls `LinkAssignmentGroupsAsync`. |
| 5.1 Group filter | FR-22 | ⚠️ | Groups are filtered to `IsActive` only. The spec also wants "off groups rejected" — the picker does not include inactive groups, so rejection is implicit, but there's no explicit feedback if a previously selected group becomes inactive. |
| 5.1 Group-owned subject picker | FR-58 | ❌ | For `SelectedGroups` assignments, the subject picker still loads **grade-level** subjects (`ListSubjectsByGradeEffectiveAsync`). It does not load subjects assigned to the selected activity groups. The spec requires the assignment subject to be assigned to the target group for a period covering the effective date. |
| 5.2 Recipient preview | FR-20, FR-25 | ✅ | `PublishDialog.razor` now displays `@Model.Contacts.Count resolved recipient(s)`. |
| 5.3 Period consistency | FR-58 | ⚠️ | For grade-level assignments the picker passes `effectiveDate = DueDate ?? UtcNow` and re-filters when the due date changes. This only covers the `SelectedGrades` path; `SelectedGroups` is not covered (see 5.1 gap). |

### Sprint 5 Conclusion
`SelectedGroups` creation works mechanically, but the subject picker is not group-aware. The FR-58 consistency rule is only half-implemented.

---

## 7. Cross-Cutting Findings

| Area | Finding | Severity |
|------|---------|----------|
| **DateRange next window** | No UI surface for `SetActivityGroupNextWindow`. Blocks AC-43 and forced-rollover preview. | High |
| **Topic assignment PeriodId editing** | Existing topic assignments cannot have their `PeriodId` edited in the UI; only creation-time assignment is supported. | Medium |
| **String flag audit** | Audit log grid does not display value changes for string flags. | Low |
| **Sub-period actions** | No activate/complete/edit actions on the sub-period list. | Low |
| **Sprint 6 not started** | No bUnit tests or Playwright smoke exist for the new UI flows. Verification is entirely manual at this point. | N/A |

---

## 8. Recommended Fix Order

1. **Fix duplicate-assignment bug in `TopicCreateDialog.razor` grade path.** Either extend `CreateTopicForGradeRequest`/`Command` to accept `PeriodId`, or switch the grade path to generic topic create + single `AssignGradeTopicAsync` call.
2. **Unify topic-create entry points.** Replace `SubjectCreateDialog` usage in `Subjects.razor` with `TopicCreateDialog` (or extend `SubjectCreateDialog` with owner/period fields). Otherwise the new owner/period UI is orphaned.
3. **Fix `Subjects.razor` period filter.** Use `effectiveDate` (derived from the active period) or update the backend to honor `periodId` consistently.
4. **Add next-window inputs** to `ActivityGroupCreateDialog.razor` and `ActivityGroupEditDialog.razor` for `DateRange` spans.
5. **Make assignment subject picker group-aware** for `SelectedGroups` (load `ListSubjectsByGroupAsync` for the selected groups, or union with grade path as appropriate).
6. **Filter period dropdowns** in `TopicCreateDialog.razor` to valid period types for the chosen owner.
7. **Sprint 6** — add bUnit tests covering the above fixes, plus the join-dialog span filtering, rollover button, and config-flag string override.

---

## 9. Files Reviewed

- `src/Settings/SchoolCollab.Settings.Application/Components/Pages/ConfigFlagDetail.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriods.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Pages/ActivityGroups/ActivityGroups.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Pages/ActivityGroups/ActivityGroupDetails.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Students/ActivityGroupCreateDialog.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Students/ActivityGroupEditDialog.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Students/JoinGroupsDialog.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Subjects/Subjects.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Students/TopicCreateDialog.razor`
- `src/Students/SchoolCollab.Students.Application/Components/Students/SubjectCreateDialog.razor`
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Create.razor`
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/PublishDialog.razor`
- `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentsApiClient.cs`

---

## 10. Implementation Summary (2026-08-27)

All §8 recommended fixes were implemented and verified (full solution build 0
errors; Students 301/0, Admin 453/0, Assignments 102/0, plus Core/Architecture/API
suites green).

### Fix 1 — Duplicate-assignment bug (grade-owned topic create)
- Extended `CreateTopicForGrade` command, `CreateTopicForGradeRequest`, and the
  handler to accept an optional `PeriodId`. The handler now validates the period
  (FR-57/EC-24) and applies it to the single created `GradeTopicAssignment`.
- `TopicCreateDialog.razor` grade path now passes `PeriodId` into
  `CreateTopicForGradeAsync` and no longer calls `AssignGradeTopicAsync` again.
- Added 2 unit tests (`CreateForGrade_WithPeriodId_ScopesAssignmentToPeriod`,
  `CreateForGrade_WithTermOutsideActiveYear_Throws`).

### Fix 2 — Unify topic-create entry points
- `Subjects.razor` now opens `TopicCreateDialog` (not the legacy
  `SubjectCreateDialog`) with the current owner context; the create button is
  enabled for both grade and group owners.

### Fix 3 — Subjects.razor period filter
- Added `Guid? PeriodId` to the `ListTopicsByGrade` query/handler and the
  `by-grade` endpoint. When supplied it filters assignments by `PeriodId`;
  otherwise it falls back to the effective-date window. The client already
  passed `periodId`.

### Fix 4 — DateRange next-window UI
- Added `SetActivityGroupNextWindowAsync` to the client.
- `ActivityGroupCreateDialog.razor` and `ActivityGroupEditDialog.razor` now show
  optional next-window date pickers for `DateRange` spans, validate next start
  >= current window end, and call the next-window endpoint on submit.

### Fix 5 — Group-aware assignment subject picker
- Added `ListTopicsByGroup` query/handler and `GET /students/subjects/by-group/{id}`
  endpoint (registered under both `/topics` and `/subjects` prefixes).
- `Create.razor` loads the union of topics assigned to the selected activity
  groups for `SelectedGroups` assignments and validates the chosen subject is
  assigned to at least one selected group.

### Fix 6 — Period dropdown filtering in TopicCreateDialog
- Grade-owned: offers `AcademicYear` + Term/Semester within the active year.
- Group-owned: offers periods matching the group's `EnrollmentSpan`
  (Termly→Term, Semester→Semester, WholeAcademicYear→AcademicYear); none for
  `OpenEnded`/`DateRange`.

### Test updates
- `CreateSubjectForGradeHandlerTests.cs`: updated handler construction for the new
  `IPeriodRepository` dependency; added 2 period-scoping tests.
- `TopicCreateDialogTests.cs`: mapped the dialog's `OnInitializedAsync` API calls
  (`/activity-groups`, `/students/periods`) so the bUnit tests render.

### Not addressed (out of scope for this pass)
- Topic-assignment `PeriodId` editing on existing assignments (creation-time only).
- String-flag audit-log value display.
- Sub-period list row actions (edit/activate/complete).
- Sprint 6 bUnit/Playwright verification suite (still open).
