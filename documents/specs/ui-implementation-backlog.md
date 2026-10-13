# UI Implementation Backlog — Forward Only

> **Implementation track: UI.** This is the UI half of the activity-group
> enrollment-span work; `backend-implementation-backlog.md` is the backend
> half. The two are coordinated by the table at the bottom of each doc. All
> items here are net-new UI work or changes to existing shipped UI.
>
> **The spec is the source of truth — this backlog is a derived, sprint-ordered
> task list, not a restatement of requirements.** Each item cites the spec
> section / FR it implements. Read the spec before starting any sprint:
> - `activity-group-enrollment.md` (Rev. 2–6) — activity groups, enrollment spans, rollover
> - `period-hierarchy-terms-semesters.md` — period type/parent, academic-year division
> - `subject-to-topic-polymorphism.md` — Topic entity, bridge, owner validation, UI rename
> - `active-period-per-tenancy.md` — active-period provider, grade-level wizard gate
>
> **Granular per-spec phase trackers:**
> `activity-group-enrollment-impl.md` Phases 4–5 (shipped v1 UI, retained for history),
> `subject-to-topic-polymorphism-impl.md` Phase 5 (Subjects→Topics admin rename).
> Forward UI work lives here.

## Legend

- **P0** — Blocks subsequent sprints or breaks existing shipped UI.
- **P1** — Required to deliver the activity-group enrollment-span feature end-to-end.
- **P2** — Polish, testing, or usability improvements.

---

## Sprint 1 — Period Hierarchy Foundation

*Goal: unblock activity-group span UI and topic period alignment by shipping
 the admin surfaces for the period hierarchy and string-valued feature flags.*

### 1.1 FeatureFlag admin surface — string values

- [x] **P0** Extend the existing feature-flag override admin UI to support
  `FlagKind.String` values. Must allow reading/writing `"None"` | `"Terms"` |
  `"Semesters"` for the `academic_year_division` flag, while keeping boolean
  flags unchanged.
  - *Source:* `period-hierarchy-terms-semesters.md` FR-H6/7, §8.2
  - *Likely files:* Settings admin feature-flag override page/resolver UI

### 1.2 Period admin — type + parent

- [x] **P0** Extend the Period create/edit form with:
  - `PeriodType` selector (`AcademicYear` | `Term` | `Semester`).
  - `ParentPeriodId` dropdown filtered to the tenant's `AcademicYear` periods
    (required for `Term`/`Semester`, hidden for `AcademicYear`).
  - Client-side or server-roundtrip validation messages for containment /
    no-overlap errors.
  - *Source:* `period-hierarchy-terms-semesters.md` FR-H2/H3/H12
  - *Likely files:* `PeriodRoutes` consumer, Period create/edit page/dialog

### 1.3 Period admin — sub-period list

- [x] **P1** Add a sub-period list view under an academic year
  (`GET /students/periods/{academicYearId}/sub-periods`).
  - *Source:* `period-hierarchy-terms-semesters.md` FR-H12

### 1.4 Grade-Level Wizard active-year gate

- [x] **P1** Update `GradeLevelWizard.razor` to use the active **AcademicYear**
  (not just any active period) and surface the existing "open a term / no
  active year" placeholder when needed.
  - *Source:* `active-period-per-tenancy.md` FR-A6
  - *Note:* `GradeLevelWizard.razor` does not exist; the equivalent gate lives in
    `GradeLevels/Detail.razor` (`OpenStudentCreateAsync`), which now uses
    `GetActiveAcademicYearAsync()` instead of filtering any active period.

---

## Sprint 2 — Activity Group Model Extension UI

*Goal: reflect the Rev. 2–6 domain model changes in the activity-group admin
 surfaces. Depends on Sprint 1 for period-aware spans.*

### 2.1 Activity group landing page — Rev. 2–6 columns

- [x] **P1** Extend `/students/activity-groups` grid with:
  - `IsActive` on/off toggle or status badge.
  - Eligible grades chip/list.
  - `EnrollmentSpan` badge (`WholeAcademicYear`, `Termly`, `Semester`,
    `DateRange`, `OpenEnded`).
  - Current window dates and next-window summary for `DateRange`.
  - `AutoRenewDefault` indicator.
  - *Source:* `activity-group-enrollment.md` FR-3/5/39/42/49/53

### 2.2 Activity group create/edit dialog — span fields

- [x] **P1** Extend `ActivityGroupCreateEditDialog.razor` with:
  - Eligible `GradeLevel`s multi-select.
  - `EnrollmentSpan` dropdown (immutable after creation).
  - `EnrollmentStartDate` + `EnrollmentEndDate` (required for `DateRange`;
    hidden/disabled for `OpenEnded`).
  - `AutoRenewDefault` toggle.
  - For `DateRange`: next-window inputs with validation that the next start
    is on/after the current window end.
  - *Source:* `activity-group-enrollment.md` FR-1/5/39/42/49/53

### 2.3 Activity group details — members tab updates

- [x] **P1** Extend the members tab to show:
  - Membership `PeriodId` / window dates.
  - `AutoRenew` toggle per member (admin-settable).
  - Exit/Remove row actions.
  - Rollover status / next-window preview for `DateRange`.
  - *Source:* `activity-group-enrollment.md` FR-24/25/49/54

---

## Sprint 3 — Activity Group Span-Aware Operations

*Goal: make join/leave and rollover behave correctly across all enrollment spans.*

### 3.1 Span-aware join dialog

- [x] **P1** Update `JoinGroupDialog.razor` to respect spans:
  - `Termly`/`Semester`/`WholeAcademicYear`: only list groups whose span matches
    the active period; surface the selected period to the user.
  - `DateRange`: warn if the current window is closed; new joins attach to the
    next open window.
  - `OpenEnded`: continuous join, no period selector.
  - *Source:* `activity-group-enrollment.md` FR-28..32, FR-40/43/52

### 3.2 Forced rollover admin action

- [x] **P1** Add a "Roll over" button on the activity-group details page.
  Confirm before closing the current window and re-enrolling `AutoRenew`
  members into the next window.
  - *Source:* `activity-group-enrollment.md` FR-54
  - *Likely files:* activity-group details page, `StudentsApiClient`

---

## Sprint 4 — Subject / Topic Alignment

*Goal: align subject/topic delivery with enrollment spans. Depends on Sprint 1
 for the period hierarchy and Sprint 2 for the group-span data.*

### 4.1 Subjects landing — ActivityGroup owner filter

- [x] **P1** Extend `Subjects.razor` with an `OwnerType` filter
  (`GradeLevel` | `ActivityGroup`) so the page lists topics owned by either.
  Keep the mandatory `GradeLevel` filter for grade-owned topics.
  - *Source:* `activity-group-enrollment.md` FR-35 (superseded); `subject-to-topic-polymorphism.md` §7.1

### 4.2 Topic create/edit — owner + period alignment

- [x] **P1** Extend `Subjects/Create.razor` and `Subjects/Edit.razor` with:
  - `OwnerType` selector (`GradeLevel` | `ActivityGroup`).
  - `OwnerId` picker (grade dropdown or activity-group dropdown).
  - Optional `PeriodId` aligned to the owner (Rev. 6):
    - grade-owned: `AcademicYear` or `Term`/`Semester` within the active year;
    - activity-group-owned: must match the group's `EnrollmentSpan`
      (`Term`/`Semester`/`AcademicYear`), or `null` for `OpenEnded`/`DateRange`.
  - Preserve the existing date-effective `[StartDate, EndDate]` window.
  - *Source:* `subject-to-topic-polymorphism.md` FR-1..4; `activity-group-enrollment.md` FR-55..58
  - *Review fixes (2026-08-27):* fixed the duplicate-assignment bug in the
    grade-owned path (extended `CreateTopicForGrade` command/request/handler to
    accept an optional `PeriodId` and apply it to the single created assignment,
    with FR-57 validation); the period dropdown is now filtered to valid periods
    for the chosen owner; `Subjects.razor` now opens `TopicCreateDialog` (not the
    legacy `SubjectCreateDialog`) so the owner/period fields are reachable from
    the Topics landing.

### 4.3 Strand / lesson dialogs for group-owned topics

- [x] **P1** Verify and fix strand/lesson dialogs (`StrandsDialog.razor`,
  `LessonsDialog.razor` or inline editors) for activity-group-owned topics.
  - *Source:* `subject-to-topic-polymorphism.md` FR-16..19
  - *Note:* strand/lesson dialogs operate on `TopicId` and are owner-agnostic;
    no grade-specific assumptions found — they work for group-owned topics.

### 4.4 Subject → Topic label rename (UI only)

- [x] **P2** Update user-visible strings from "Subject" to "Topic" in the
  admin app (page titles, breadcrumbs, dialog titles, grid headers, buttons,
  validation messages). DB/API identifiers stay as-is for this pass.
  - *Source:* `subject-to-topic-polymorphism.md` OS-4

---

## Sprint 5 — Assignments UI

*Goal: let teachers target assignments at activity groups and respect subject
 period alignment.*

### 5.1 SelectedGroups target

- [x] **P1** Extend the assignment Create/Edit page to support
  `TargetAudienceType = SelectedGroups`:
  - Multi-group picker (Active groups only; off groups rejected per FR-22).
  - Optional subject/topic scoped to a linked group
    (`OwnerType = ActivityGroup`).
  - Publish validation requiring at least one linked group.
  - *Source:* `activity-group-enrollment.md` FR-17..23, FR-34
  - *Review fixes (2026-08-27):* the subject picker is now group-aware for
    `SelectedGroups` — it loads the union of topics assigned to the selected
    activity groups (new `GET /students/subjects/by-group/{id}` endpoint) and
    validates the chosen subject is assigned to at least one selected group.

### 5.2 Publish recipient preview

- [x] **P1** Add a recipient preview / confirmation showing the resolved count
  of active group members before publishing a `SelectedGroups` assignment.
  - *Source:* `activity-group-enrollment.md` FR-20, FR-25

### 5.3 Subject/assignment period consistency

- [x] **P1** Update the assignment subject picker to reject a topic whose
  `PeriodId` does not cover the assignment's effective date / target period
  (`SelectedGrades` or `SelectedGroups`).
  - *Source:* `activity-group-enrollment.md` FR-58
  - *Note:* the picker now calls `ListSubjectsByGradeEffectiveAsync` with the
    assignment's effective date (`DueDate ?? UtcNow`), and re-filters when the
    due date changes, so only topics effective on that date are offered.
  - *Effective-date gap closed (2026-08-27):* the `SelectedGroups` path now
    mirrors the grade path — `ListSubjectsByGroupAsync` accepts an optional
    `DateOnly? effectiveDate` and appends `?effectiveDate=yyyy-MM-dd` to the
    by-group request; `Create.razor` `LoadGroupSubjectsAsync` computes the
    effective date once (`DueDate ?? UtcNow`) and passes it for every selected
    group, and `OnDueDateChangedAsync` reloads the group subject union when the
    audience is `SelectedGroups`. FR-58 is now complete for both paths.

---

## Sprint 6 — Verification & Cross-Cutting Polish

### 6.1 bUnit tests

- [x] **P2** bUnit: grade topic term-scoped assignment validation (AC-44) —
  covered by handler tests `CreateForGrade_ExistingAssignmentDifferentPeriod_CreatesScopedAssignment`
  and `CreateForGrade_ExistingSamePeriod_Skips` (Students.Tests.Unit, +2).
- [x] **P2** bUnit: null `PeriodId` back-compat UI (AC-46) —
  `CreateDialog_GradeOwner_NullPeriodId_PostsPeriodIdNull` (Admin.Tests.Unit).
- [x] **P2** bUnit: duplicate-coded-value warning + disabled Create (grade owner) —
  `CreateDialog_GradeOwner_DuplicateCodedValue_WarnsAndDisables` (Admin.Tests.Unit, +1).
- [x] **P2** bUnit: activity-group topic span mismatch UI (AC-45) —
  `CreateDialog_GroupOwner_TermlyGroup_OffersOnlyTermPeriods` / `_OpenEndedGroup_ShowsHintNoPeriods`
  (Admin.Tests.Unit).
- [x] **P2** bUnit: group-path duplicate guard —
  `CreateDialog_GroupOwner_DuplicateCodedValue_WarnsAndBlocksAssign` (Admin.Tests.Unit).
- [x] **P2** bUnit: `AcademicYearDivision` setting UI and framework-switch
  rejection messaging — `AcademicYearDivisionSettingTests.cs`
  (`DivisionSetting_CardShowsEffectiveValueAndSource`,
  `DivisionSetting_SwitchRejection_ShowsServerMessage`).
- [x] **P2** bUnit: sub-period list loading/empty/error states — `SubPeriodsPageTests.cs`
  (`SubPeriods_EmptyList_ShowsEmptyMessage`, `SubPeriods_LoadError_ShowsErrorBarAndBackButton`).
- [x] **P2** bUnit: division client methods — `ConfigFlagsApiClientTests`
  (`GetAcademicYearDivisionAsync` 404→null / 200→deserialize; `SetAcademicYearDivisionAsync`
  204 success / 422 message extraction).
- [x] **P2** bUnit: span-aware create/edit dialog validation (AC-35..43) —
  `ActivityGroupSpanDialogTests.cs` (`CreateDialog_DateRangeSpan_RevealsWindowDates`,
  `CreateDialog_OpenEndedSpan_HidesWindowDates`, `CreateDialog_NextWindow_OnlyStart_ShowsBothOrNeitherError`,
  `CreateDialog_NextWindow_StartBeforeEnd_Rejected`, `CreateDialog_ValidDateRange_PostsCreateAndNextWindow`,
  `EditDialog_ReadOnlySpan_AndValidPut`).
- [x] **P2** bUnit: rollover / next-window UI (AC-38/43) — rollover button
  visibility by span (`DetailsPage_RolloverButton_HiddenForOpenEnded` /
  `DetailsPage_RolloverButton_ShownForDateRange` in `ActivityGroupsPageTests.cs`);
  next-window validation covered by the create/edit dialog tests above.
  Confirmation-dialog driving dropped as optional/fragile (documented follow-up).
  Product fix surfaced by these tests: `ActivityGroupCreateDialog`/`EditDialog`
  `DialogShellFooter` now binds `Error="Error"` so validation errors render.
- [x] **P2** bUnit: `PeriodType` + parent selector validation — `PeriodFormTests.cs`
  (`PeriodForm_Term_ShowsParentSelector`, `PeriodForm_AcademicYear_HidesParentSelector`,
  `PeriodForm_Term_NoParent_ShowsError`).
- [x] **P2** bUnit: span-aware join filtering (AC-35/36) — `JoinGroupsDialogTests.cs`
  (`JoinDialog_OpenEnded_Listed_WhenNoActivePeriod`, `JoinDialog_Termly_Listed_SemesterFiltered_WhenActiveTerm`).

### 6.2 Playwright smoke

- [ ] **P2** End-to-end: create activity group → add students → create
  group-scoped topic (`OwnerType = ActivityGroup`) → create `SelectedGroups`
  assignment linked to the group + topic → publish → only group members'
  subscribed contacts receive it.
  - *Source:* `activity-group-enrollment.md` §11
  - **DEFERRED until the activity-group feature is complete** (per user, 2026-08-27).
    The smoke test needs a running AppHost + seeded data and is most valuable once
    the remaining Sprint 6 items (AC-35..43, rollover/next-window, PeriodType
    selector) and the re-deferred items (Item 4 PeriodId editing, Item 5 string-flag
    audit, backend group duplicate guard) are shipped.

### 6.3 Cross-cutting polish

- [x] **P2** Loading / empty / error states for sub-period lists and the
  academic-year division setting — `SubPeriods.razor` (`_loading` →
  `FluentProgressRing`, `_error` → `FluentMessageBar` + Back, `EmptyMessage`,
  `ErrorBoundary`); `ConfigFlagDetail.razor` division card (loading ring,
  error bar, value/source, select + reason + save, reload on success). Locked by
  `SubPeriodsPageTests.cs` + `AcademicYearDivisionSettingTests.cs`.

### Deferred P2 fold-in (Sprint 6 Round 1 — plan-ui-sprint6.md)

- [x] **Item 1** `ExistingTopicCodedValueIds` seeded from `Subjects.razor`
  `OpenCreateDialogAsync` (duplicate-coded-value warning works from Topics landing).
- [x] **Item 2** `CreateTopicForGradeHandler` idempotency guard is now
  period-scoped (same `(TopicId, PeriodId)`); a differently-scoped request
  creates a new assignment carrying the requested `PeriodId`.
- [x] **Item 3** `TopicCreateDialog` activity-group path loads the group's
  existing topics, warns + disables submit on a duplicate `CodedValueId`, and
  re-checks before `AssignActivityGroupTopicAsync` (client-side guard).
- [x] **Item 6** `SubPeriods.razor` row actions: Edit (navigate), Activate
  (disabled for Active), Complete (confirm + disabled unless Active).
- [x] **Item 4** Topic-assignment `PeriodId` editing on existing assignments —
  `UpdateTopicAssignmentPeriod` command/handler + `PUT /topic-assignments/{id}/period`
  (reuses shared `TopicAssignmentPeriodValidator`), `TopicAssignment.UpdatePeriod`,
  application-layer `TopicAssignmentDto.PeriodId`, grade Topics card "Edit period"
  action + `TopicAssignmentPeriodEditDialog`. Group-path UI deferred (no group-topics
  list page; endpoint supports it). Tests: `UpdateTopicAssignmentPeriodTests.cs`.
- [x] **Item 5** String-flag audit-log value display — `FlagAuditEntry.PreviousValue`/
  `NewValue` + migration `AddFlagAuditEntryValueColumns`, auditor + call sites capture
  string-flag values, `FlagAuditEntryDto` (Core + Admin.Shared) + query expose them,
  `ConfigFlagDetail.razor` renders a Value column for string flags. Tests:
  `FeatureFlagAuditorTests.Record_adds_audit_row_with_previous_and_new_value`,
  `ConfigApiTests.PUT_UpsertStringOverride_WritesValueAuditRow`.
- [x] **Backend guard** for `AssignActivityGroupTopic` duplicate active assignments —
  `DuplicateTopicAssignmentException` (→409) in `AssignActivityGroupTopicHandler`
  (period validation runs first, so 422 wins over 409). Grade-path skip-vs-reject
  semantics deferred. Tests: `TopicAssignmentPeriodTests` (4 new).

---

## Follow-ups — Period Activation Window (period-activation-window-auto-activation.md)

- [ ] **P2** Expose per-period `ActivationToleranceDays` in the period create/edit UI
  (year + sub-periods). Backend (entity, DTO, create/update commands, API) is shipped;
  only the form field is missing. *Source:* `period-activation-window-auto-activation.md` FR-W3.

---

## Mobile (Blazor Hybrid) train — `mobile-hybrid-app.md`

Tracked outside the sprint count below (it is not part of the period/activity-group arc).

**All four layers are merged** — L1 `#326` (`fab7dd5e`, CI 5/5), L2 `#338` (`d2b2e98d`), L3 `#343`
(`4fccd8a3`), L4 `#344` (`a8e38fb8`) — so the train is **complete as specified**: §8 defines L1–L4 and
there is no L5. The layers were sequential: **L2 blocked L3/L4** because no device TFM built until
severance landed. What remains is device-bound verification (below), not construction.

- [x] **P0** **L2 — severance — DONE** (round `mobile-severance`; re-verified on `ea7b2df1`,
  2026-10-12). Delivered: `Admin.Shared`'s `Microsoft.AspNetCore.App` `FrameworkReference` became
  `Microsoft.AspNetCore.Components[.Authorization|.Web]` packages (10.0.8) and it dropped its
  `ServiceDefaults` / `Students.Core` / `Core` references, so the head's closure carries no framework
  reference; the new zero-dependency `SchoolCollab.Core.Abstractions` project hosts `Core.Features`
  plus the two enums `SignatureRequirementMode` / `NotificationChannel` — the plan gate proved
  `Core` was *itself* a carrier, because its `JwtBearer`/`OpenIdConnect` packages declare the
  framework reference; the 12-file contacts/guardian family moved to `Students.Application`; and
  `Students.Core` / `Students.Contracts` dropped their unused `ServiceDefaults` references.
  Verified at L2 (2026-10-12; these are the L2-day numbers — later layers moved them):
  `-f net10.0-android` **0 errors**; root solution 0 errors; ArchitectureTests 105, Admin 651,
  Core 126, Students 624, Assignments 1109, Mobile 8 — 0 failures. The mobile CI job asserts
  `-f net10.0-android` but is still skipped behind `ENABLE_MAUI_CI`. *Source:* `mobile-hybrid-app.md`
  §8 L2; `documents/rounds/round-mobile-severance.md`.
- [x] **P0** **L3 — credential + registration path — DONE** (round `mobile-credential`; merged `#343`,
  squash `4fccd8a3`, 2026-10-13). Delivered the public PKCE client `school-collab-mobile-client` with
  mapper parity plus an audience mapper emitting `school-collab-client` (the realm guard's
  exact-client-set assertion widened, owner-approved, + six mobile-client tests), and in `Mobile.Logic`:
  auth options, token store + keys, JWT payload reader, `TokenSession` (refresh with rotation,
  memory-first sign-out), system-browser authenticator, `OidcLoginService` (Authorization Code + PKCE
  S256), the bearer `DelegatingHandler`, the `AuthenticationStateProvider` bridge, and the client-side
  registration of the eight clients (tokens in `SecureStorage`, never `sessionStorage`/`localStorage`).
  Verified: android **0 errors**; root solution 0 errors; Mobile 123, ArchitectureTests 111, Core 126,
  Admin 651, Students 624, Assignments 1118 — 0 failures; plus a **real Keycloak container** run proving
  realm import, PKCE (S256) sign-in, a token carrying `aud`/`tenant_id`/`teacher_id`/`roles`, and
  refresh rotation. **§8 criterion 3 (device 200-OK) carried OPEN — owner-run.** *Source:* §8 L3;
  `documents/rounds/round-mobile-credential.md`.
- [x] **P2** **L4 — first vertical slice — DONE** (round `mobile-assignments-slice`; merged `#344`,
  squash `a8e38fb8`, 2026-10-13). Delivered the assignments list → detail slice: a slice-scoped
  12-method `MobileAssignmentsApiClient`, `AssignmentsListViewModel` + `AssignmentDetailViewModel`,
  a copied 17-line `AssignmentEnumDisplay`, five pages with isolated scoped CSS and safe-area handling,
  and the mobile suite at **141/0** (123 → 141). The severance-or-copy decision landed as **copy, not
  reuse** — `src/Assignments/**` untouched — because reusing `AssignmentsApiClient` would drag
  `EffectiveAssignmentPolicy` out of the framework-carrying `Core` that L2 pinned. **§8 criterion 2
  (safe area on both form factors) carried OPEN** (device-bound), with two low-severity UI residuals
  and an iOS double-inset risk recorded. *Source:* §8 L4, §9 OD1;
  `documents/rounds/round-mobile-assignments-slice.md`.
- [ ] **P2** **The one open decision the spec still carries: OD2 (token refresh policy) — PARTIAL.**
  L3 shipped silent refresh with rotation; the **401 → refresh → retry resend** and the
  `session_ended`/410 mapping did **not** ship. OD3 (Logic split) closed as MET — L4 followed it, with
  both view models and the client in `Mobile.Logic`; OD4 (dev-loop base URL) closed as MET, with the
  emulator/LAN values deliberately developer-local. See `mobile-hybrid-app.md` §9. *Source:* §9.
- [x] **P2** **G5 — wiring guards — DECIDED (2026-10-13): the mobile head is excluded** from
  `CrossModuleWiringTests`' `AddProject`/`WithReference`-derived half. The literal-base-address half
  already scans `src/**/*.cs`, which includes the head, so the load-bearing protection is in place;
  the host-model half is structurally inapplicable to a head that is not an AppHost resource, and
  `Aspire.Hosting.Maui` is preview-only. Revisit only if the head becomes an AppHost resource.
  *Source:* `maui-agent-skills-readiness.md` §4 G5; `mobile-hybrid-app.md` §10.

---

## Dependency Graph

```
Sprint 1 (period hierarchy foundation)
  │
  ├── unblocks Sprint 2 (activity group model extension UI)
  │     │
  │     ├── unblocks Sprint 3 (span-aware operations)
  │     │
  │     └── unblocks Sprint 4 (subject/topic alignment)
  │           │
  │           └── unblocks Sprint 5 (assignments SelectedGroups)
  │
  └── unblocks Sprint 4 directly (topic PeriodId needs typed periods)

Sprint 6 (tests + polish) runs after Sprints 1–5.
```

---

## Summary Counts

| Sprint | Items | P0 | P1 | P2 |
|---|---:|---:|---:|---:|
| 1 — Period Hierarchy Foundation | 4 | 2 | 2 | 0 |
| 2 — Activity Group Model Extension | 3 | 0 | 3 | 0 |
| 3 — Span-Aware Operations | 2 | 0 | 2 | 0 |
| 4 — Subject/Topic Alignment | 4 | 0 | 3 | 1 |
| 5 — Assignments UI | 3 | 0 | 3 | 0 |
| 6 — Verification & Polish | 9 | 0 | 0 | 9 |
| **Total** | **25** | **2** | **13** | **10** |

---

## Source Specs

| Spec | Relevant UI areas |
|---|---|
| `activity-group-enrollment.md` | Activity group list, create/edit/details, student Detail section, assignment targeting |
| `activity-group-enrollment-impl.md` | Phases 7–11 UI updates |
| `period-hierarchy-terms-semesters.md` | Period type/parent editor, academic-year division setting, sub-period list |
| `subject-to-topic-polymorphism.md` | Subjects landing filter, topic create/edit, strand/lesson dialogs, Topic rename |
| `active-period-per-tenancy.md` | Grade-level wizard active-year gate |
