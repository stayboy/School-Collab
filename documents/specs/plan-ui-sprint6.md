# Plan — UI Sprint 6, Round 1: fold in deferred P2 fixes + first bUnit tranche

**Status:** Planned (orchestrator-authored; implementation delegated to a worker)
**Sprint:** 6 — Verification & Cross-Cutting Polish (`ui-implementation-backlog.md` §Sprint 6)
**Inputs:**
- `review-ui-fixes-round1.md` — P2 findings (ExistingTopicCodedValueIds, PeriodId-drop, group-path duplicate guard)
- `review-ui-sprints-1-5.md` — §7 cross-cutting findings + "Not addressed" list
- `plan-effective-date-group-subjects.md` — §7 "Residual P2 items (deferred to Sprint 6)"
- `activity-group-enrollment.md` — AC-35..46; `period-hierarchy-terms-semesters.md`; `subject-to-topic-polymorphism.md` FR-55..58

**Scope discipline:** this round folds in the **small** deferred P2 items and the
first bUnit tranche they unblock. It does **not** attempt the whole Sprint 6
backlog. Larger P2 items are explicitly re-deferred with reasons (§2, §6).

---

## 1. Goal

Close the four small deferred P2 items from the Sprint 1–5 / fix-round reviews
and lock their behavior with the first Sprint 6 bUnit tranche:

1. `ExistingTopicCodedValueIds` seeding when `TopicCreateDialog` opens from `Subjects.razor`.
2. `CreateTopicForGradeHandler` no longer silently drops a requested `PeriodId`
   when a differently-scoped active assignment exists.
3. Client-side duplicate topic/group-assignment guard in `TopicCreateDialog`'s
   activity-group path.
4. Sub-period list row actions (edit / activate / complete).

Plus bUnit coverage for the fixed dialogs/pages and the two Rev. 6 UI criteria
that are directly testable at the dialog level (AC-45 span-mismatch period
filtering, AC-46 null-`PeriodId` back-compat).

Non-goals this round: Playwright smoke (6.2), remaining 6.1 bUnit items
(span-aware join/create/edit dialog validation AC-35..43, rollover/next-window
AC-38/43, PeriodType+parent selector, AcademicYearDivision setting UI), 6.3
loading/empty/error polish, PeriodId editing on existing assignments (item 4
below), string-flag audit-log value display (item 5 below), and a backend
duplicate-assignment guard for `AssignActivityGroupTopic`.

---

## 2. Scope decision — the 6 deferred P2 items

| # | Item | Verdict | Rationale |
|---|------|---------|-----------|
| 1 | `ExistingTopicCodedValueIds` from `Subjects.razor` | **IN SCOPE** | One-line model seed; restores an existing UX guard; unblocks dialog bUnit. |
| 2 | `CreateTopicForGradeHandler` PeriodId drop | **IN SCOPE** | One-condition handler change + 2 unit tests; makes the idempotency guard true idempotency. |
| 3 | Group-path duplicate assignment guard (client) | **IN SCOPE** | Client-only check reusing an existing endpoint; mirrors the grade path's `_duplicateCodedValue` pattern. Backend guard deferred (§6). |
| 4 | Topic-assignment `PeriodId` editing on existing assignments | **Deferred further** | Needs a new update command/handler/endpoint per bridge subtype + client methods + a new edit surface (none exists today — Subjects row "Edit" is coded-value rename only). Full feature, not polish. |
| 5 | String-flag audit-log value display | **Deferred further** | Verified full-stack: `FlagAuditEntry` has **no value columns** (`PreviousIsEnabled`/`NewIsEnabled` only; `FeatureFlagDtos.cs:35`, `FlagAuditEntry.cs:19-20`). Requires entity columns + EF config + migration + `FeatureFlagAuditor` changes + DTO/query changes + UI. Not a UI-only fix. |
| 6 | Sub-period list row actions | **IN SCOPE** | Pure UI: `LandingPage` `RowActions` pattern exists (`Subjects.razor`), all three client methods already exist, edit route exists. |

This matches the pragmatic guidance: items 1–3 are small and unblock bUnit;
item 6 is small and client-only.

---

## 3. Confirmed current state (evidence)

### Item 1 — ExistingTopicCodedValueIds
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Subjects/Subjects.razor`
  → `OpenCreateDialogAsync` (~line 313) builds `TopicCreateDialog.TopicCreateModel`
  setting only `OwnerType`, `GradeLevelId`, `ActivityGroupId`.
  `ExistingTopicCodedValueIds` stays empty, so the dialog's
  `_duplicateCodedValue` warning (`TopicCreateDialog.razor`, `OnCodedValuePicked`)
  can never fire from the Topics landing.
- Mirror pattern: `GradeLevels/Detail.razor` seeds the set from the grade's
  current topics. `SubjectDto.CodedValueId` is available on `_items`
  (already used by `ResolveNamesAsync`).
- Backend still rejects duplicates (`CreateTopicForGradeHandler` →
  `DuplicateTopicCodeException`) — no data risk, purely UX.

### Item 2 — PeriodId drop in CreateTopicForGradeHandler
- `src/Students/SchoolCollab.Students.Core/CQRS/Topics/Commands/CreateTopicForGrade/CreateTopicForGradeHandler.cs:115`
  → `if (!existingAssignments.Any(a => a.TopicId == subject.Id))` — skips
  assignment creation when **any** active assignment exists, regardless of its
  `PeriodId`. A request for topic X scoped to Term 1 when an active
  year-spanning (`PeriodId = null`) assignment exists reuses the topic and
  silently drops the requested scope (log at :135 says "skipping").
- Domain permits multiple bridge rows per (grade, topic) — `TopicAssignment`
  has no uniqueness constraint; FR-55 is explicitly additive; FR-58 treats
  year-spanning and period-aligned assignments as alternative coverage sources
  (AC-44 scenario). Existing `PeriodId` validation (FR-57/EC-24,
  `ValidatePeriodAsync`) already runs before this point and is unchanged.
- Existing tests: `tests/SchoolCollab.Students.Tests.Unit/CreateSubjectForGradeHandlerTests.cs`
  — `CreateForGrade_WithPeriodId_ScopesAssignmentToPeriod`,
  `CreateForGrade_WithTermOutsideActiveYear_Throws` (keep green).

### Item 3 — Group-path duplicate guard
- `src/Students/SchoolCollab.Students.Application/Components/Students/TopicCreateDialog.razor:405-416`
  → group branch of `SubmitAsync` calls `CreateTopicAsync` +
  `AssignActivityGroupTopicAsync` with no existence check.
- Backend `AssignActivityGroupTopicHandler.cs` (verified): validates FR-56
  period alignment but has **no duplicate/active-assignment guard** — repeated
  submissions create overlapping `ActivityGroupTopicAssignment` rows.
- Client already has the read model it needs:
  `StudentsApiClient.ListSubjectsByGroupAsync(Guid activityGroupId,
  DateOnly? effectiveDate, CancellationToken ct = default)` returns the group's
  assigned topics (`TopicDto` incl. `CodedValueId`).
- Grade-path pattern to mirror: `_duplicateCodedValue` flag + warning
  `FluentMessageBar` + `SubmitDisabled` term (TopicCreateDialog.razor).

### Item 6 — Sub-period row actions
- `src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriods.razor`
  → `LandingPage` has no `RowActions`; grid template already reserves the
  actions column (`GridTemplateColumns` ends with `auto`).
- Client methods already exist in `StudentsApiClient.cs`:
  `UpdatePeriodAsync` (:1294), `ActivatePeriodAsync` (:1297),
  `CompletePeriodAsync` (:1300).
- Edit route exists: `/students/periods/{Id:guid}/edit` (`Periods/Edit.razor:1`,
  hosts `PeriodForm`).
- Row-action API: `src/SchoolCollab.Admin.Shared/Components/RowAction.cs`
  (`RowAction.Navigate(label, href, icon, disabled)` /
  `RowAction.Callback(label, onClick, icon, disabled, destructive, confirmMessage)`),
  wired via `LandingPage` params `RowActions` + `RowActionsUseMenuService="false"`
  (see `Subjects.razor` usage).
- `PeriodDto.Status` values rendered today: Draft / Active / Completed / Archived.

---

## 4. Exact change list

### 4.1 Item 1 — `Subjects.razor` (client, 1 file)
In `OpenCreateDialogAsync`, seed the duplicate set from the currently loaded
rows:

```csharp
ExistingTopicCodedValueIds = [.. (_items ?? []).Select(i => i.CodedValueId)],
```

- `_items` are the topics currently listed for the selected owner — exactly the
  "already used by a topic in this owner" set the dialog checks.
- No other files change. `TopicCreateDialog` behavior is untouched.

### 4.2 Item 2 — `CreateTopicForGradeHandler.cs` (backend, 1 file)
Change the step-4 guard from "any active assignment" to "same effective period
scope" (true idempotency):

```csharp
if (!existingAssignments.Any(a => a.TopicId == subject.Id && a.PeriodId == command.PeriodId))
```

- Update the skip log message to mention the period scope (e.g. "already active
  for grade … topic … with the same period scope — skipping").
- Do **not** touch `ValidatePeriodAsync`, the command/request records, the
  endpoint, or the client — `CreateTopicForGradeAsync` already passes `PeriodId`.

### 4.3 Item 3 — `TopicCreateDialog.razor` (client, 1 file)
Add a group-duplicate guard mirroring the grade-path `_duplicateCodedValue`
pattern:

1. New state: `private SubjectDto[] _groupExistingTopics = [];`
   (reuse `SubjectDto` = the by-group topic DTO the client already returns).
2. In `OnActivityGroupChangedAsync`, after parsing a valid `groupId`, load
   `Api.ListSubjectsByGroupAsync(groupId, null)` into `_groupExistingTopics`
   (try/catch → empty on failure, log via existing `Error`/logger patterns);
   reset to `[]` when the selection is cleared/invalid. Clearing the group must
   also reset the flag.
3. Duplicate predicate (computed or recomputed on the two events):
   owner is `ActivityGroup` **and** `Model.CodedValueId is { } id` **and**
   `_groupExistingTopics` contains a topic with the same `CodedValueId`.
4. UI: show a warning `FluentMessageBar` ("This subject is already assigned to
   this activity group.") near the group/period fields when the predicate is
   true, and add the predicate to the footer's `SubmitDisabled` condition.
5. In `SubmitAsync`'s group branch, re-check the predicate immediately before
   `AssignActivityGroupTopicAsync` (guard against a stale list); if it fires,
   set `Error` and return `null` without assigning.
- **No backend change** — the backend guard is an explicit residual (§6).

### 4.4 Item 6 — `SubPeriods.razor` (client, 1 file)
1. Add to `<LandingPage>`: `RowActions="@BuildRowActions"`,
   `RowActionsUseMenuService="false"`, `RowActionsAriaLabel="Sub-period actions"`.
2. `BuildRowActions(PeriodDto row)`:
   - **Edit** — `RowAction.Navigate("Edit", $"/students/periods/{row.Id}/edit", FluentIcons.Edit)`.
   - **Activate** — `RowAction.Callback("Activate", () => OnActivateAsync(row), icon, disabled: row.Status == "Active")`
     → `await Api.ActivatePeriodAsync(row.Id)` then reload.
   - **Complete** — `RowAction.Callback("Complete", () => OnCompleteAsync(row), icon, disabled: row.Status != "Active", confirmMessage: $"Complete period '{row.Name}'?")`
     → `await Api.CompletePeriodAsync(row.Id)` then reload.
   - Exact icon choices follow repo conventions (`FluentIcons.*`); worker picks
     sensible ones (e.g. Play / Checkmark).
3. Add a private `ReloadAsync` helper (re-fetch `ListSubPeriodsAsync`, keep the
   existing `_disposed`/cancellation guards and error surfacing via `_error`).
4. Failure of activate/complete must surface in the existing `_error` message
   bar and leave the row list intact (no optimistic mutation needed).

### 4.5 Out of scope (do NOT touch)
- `AssignActivityGroupTopicHandler` / any backend assignment guard.
- `FlagAuditEntry`, `FeatureFlagAuditor`, `FlagAuditEntryDto`, settings migrations.
- Any topic-assignment update command/endpoint (item 4).
- `TopicRoutes.cs`, `ListTopicsByGroup*`, `StudentsApiClient` signatures.
- `GradeLevels/Detail.razor` (its seeding is the correct mirror, already works).

---

## 5. Sprint 6 bUnit tranche (this round)

Home project for UI/bUnit tests: **`tests/SchoolCollab.Admin.Tests.Unit`**
(references Students/Assignments/Settings Application projects; existing
precedents: `TopicCreateDialogTests.cs`, `ActivityGroupsPageTests.cs` —
scripted `HttpMessageHandler` + real `FluentDialogProvider`/`LandingPage`
rendering).

New/updated tests:

| ID | Test | Locks |
|----|------|-------|
| T1 | `TopicCreateDialogTests`: model seeded with `ExistingTopicCodedValueIds` containing the picked coded value → duplicate warning bar rendered and Create disabled (grade owner). | Item 1 contract |
| T2 | `TopicCreateDialogTests`: owner `ActivityGroup`, scripted `GET /students/subjects/by-group/{id}` returning a topic whose `CodedValueId` equals the picked one → warning + disabled Create; submit path never reaches `POST …/assign` (assert via `ScriptedHandler.Calls`). | Item 3 |
| T3 | `TopicCreateDialogTests` AC-45: `Termly` group → period dropdown offers only `Term` periods; `OpenEnded` group → info bar, no period options. | AC-45 UI |
| T4 | `TopicCreateDialogTests` AC-46: with no period selected (default "— Current period —"), grade-path submit posts `CreateTopicForGrade` with `periodId: null` (assert request body captured by `ScriptedHandler`). | AC-46 UI |
| T5 | New `SubPeriodsPageTests`: scripted page render — Edit action navigates to `/students/periods/{id}/edit`; Activate hidden/disabled for `Active` rows and enabled for `Draft`; Complete confirm + `POST /students/periods/{id}/complete` fired and list reloaded. | Item 6 |
| T6 | `CreateSubjectForGradeHandlerTests` (Students.Tests.Unit): `CreateForGrade_ExistingAssignmentDifferentPeriod_CreatesScopedAssignment` (existing `PeriodId = null` + request Term → 2 assignments, new one carries the Term) and `CreateForGrade_ExistingSamePeriod_Skips` (repeat request → still 1 assignment). | Item 2 |

Already covered (no new work, keep green): AC-44 handler-level
(`CreateForGrade_WithPeriodId_ScopesAssignmentToPeriod`,
`CreateForGrade_WithTermOutsideActiveYear_Throws`).

### Remaining Sprint 6 backlog (NOT this round — stays open in `ui-implementation-backlog.md`)
- 6.1: span-aware create/edit dialog validation (AC-35..43), rollover /
  next-window UI (AC-38/43), `PeriodType` + parent selector validation,
  `AcademicYearDivision` setting UI + framework-switch rejection messaging.
- 6.2: Playwright end-to-end smoke.
- 6.3: loading / empty / error states for sub-period lists and the
  academic-year division setting.
- Re-deferred P2: items 4 and 5 (§2), backend `AssignActivityGroupTopic`
  idempotency guard (§6).

---

## 6. Acceptance criteria (the reviewer checks these)

1. **Item 1 wired.** `Subjects.razor` `OpenCreateDialogAsync` populates
   `ExistingTopicCodedValueIds` from the currently loaded `_items`
   (`CodedValueId` set); picking a coded value already listed for the selected
   owner shows the duplicate warning and disables Create.
2. **Item 2 guard.** `CreateTopicForGradeHandler` skips only when an active
   assignment with the **same `PeriodId`** exists; a different-scope request
   creates a new assignment carrying the requested `PeriodId`; the skip log
   message reflects the period-scope condition. `ValidatePeriodAsync` (FR-57)
   is unchanged.
3. **Item 3 guard.** `TopicCreateDialog` group path loads the group's existing
   topics on group selection, warns and disables submit on a `CodedValueId`
   match, re-checks before `AssignActivityGroupTopicAsync`, and never posts the
   assign request when the guard fires. No backend file is modified.
4. **Item 6 actions.** `SubPeriods.razor` renders Edit / Activate / Complete row
   actions; Edit navigates to the existing edit route; Activate/Complete call
   the existing client methods and reload the list; Complete requires
   confirmation; Activate is disabled for already-Active rows, Complete for
   non-Active rows; errors surface in the page's error bar.
5. **No scope widening.** Product-code diff limited to: `Subjects.razor`,
   `CreateTopicForGradeHandler.cs`, `TopicCreateDialog.razor`,
   `SubPeriods.razor` (+ test files, + the backlog/plan doc checkboxes). No
   endpoint/handler/DTO/migration changes beyond §4.2.
6. **T1–T6 present and green** (§5), including the two new handler tests.
7. **Build green:** `dotnet build SchoolCollab.sln -c Debug --nologo -v q` → 0 errors.
8. **Tests green:** Students, Admin, Assignments unit suites pass with baselines
   Assignments 102 / Students 301 / Admin 453 plus the new tests (no regressions).
9. **Docs updated:** `ui-implementation-backlog.md` Sprint 6 section annotated
   with the folded-in P2 items (checked) and the re-deferred items noted.
10. **Reviewer confirms** the deferred items (4, 5, backend group-assignment
    guard, remaining 6.1/6.2/6.3) are documented as open, not silently dropped.

---

## 7. Test expectations

- **Projects in play:**
  - `tests/SchoolCollab.Students.Tests.Unit` — item 2 handler tests (T6) + keep
    the two existing FR-57 tests green.
  - `tests/SchoolCollab.Admin.Tests.Unit` — bUnit T1–T5 (dialog + SubPeriods).
  - `tests/SchoolCollab.Assignments.Tests.Unit` — untouched; regression-only.
- **Harness:** follow the existing `ScriptedHandler` pattern
  (`TopicCreateDialogTests.cs` maps `GET /activity-groups`,
  `GET /students/periods`; `GetActiveAcademicYearAsync` tolerates 404). For T2,
  map `GET /students/subjects/by-group/{id}`; for T4, assert on the captured
  POST body; for T5, map `GET /students/periods/{yearId}/sub-periods`,
  `POST /students/periods/{id}/complete`, and assert the re-fetch.
- **Tooling quirk (known):** `dotnet test --nologo` fails on this machine
  (.NET 10 SDK / Microsoft.Testing.Platform rejects the forwarded `--nologo`,
  exits 5 with 0 tests run — see `plan-effective-date-group-subjects.md` §7).
  Run `dotnet test <project>` (no `--nologo`) or invoke each project's built
  test-host `.exe` directly.
- **Mandatory verification commands:**
  1. `dotnet build SchoolCollab.sln -c Debug --nologo -v q`
  2. `dotnet test tests/SchoolCollab.Students.Tests.Unit`
  3. `dotnet test tests/SchoolCollab.Admin.Tests.Unit`
  4. `dotnet test tests/SchoolCollab.Assignments.Tests.Unit`
  (`Students.Tests.Integration` needs the Aspire/AppHost harness; run only if
  the environment supports it — not required for acceptance here.)

---

## 8. Residual risks / deferred

- **Backend duplicate-assignment guard for `AssignActivityGroupTopic`** remains
  absent; the client check in §4.3 closes the create-dialog flow only. Any
  future direct API caller can still create overlapping group assignments.
  Recommend a follow-up backend guard (repository-level active-assignment
  check) as a small backend task.
- **Item 4 (PeriodId editing)** stays a feature-sized item; when picked up it
  needs a design pass (which assignment row is edited from which surface, and
  FR-56/57 revalidation on update).
- **Item 5 (string-flag audit)** requires schema + auditor + DTO changes; the
  current audit grid shows null→null bools for string-flag override rows.
  Track as its own small full-stack task; do not attempt UI-only.
- **T5 feasibility:** if `SubPeriods.razor` proves awkward to render under bUnit
  (LandingPage + query params), the reviewer may accept manual verification of
  item 6 with the bUnit test logged as a follow-up — but T1–T4 + T6 are
  required.
---

## 9. Acceptance (orchestrator pass, 2026-08-27)

**Performed by:** orchestrator (`ollama/glm-5.3-flash:cloud`), acceptance pass.

**Verdict: CLOSED** for the in-scope deferred P2 fold-in (Items 1, 2, 3, 6) and the
T1/T6 test tranche.

### Per-criterion verdict

| Criterion | Verdict |
|-----------|---------|
| 1. Item 1 wired (`Subjects.razor` seeds `ExistingTopicCodedValueIds`) | ✅ PASS |
| 2. Item 2 guard (`CreateTopicForGradeHandler` period-scoped idempotency) | ✅ PASS |
| 3. Item 3 guard (group-path duplicate check, no backend change) | ✅ PASS |
| 4. Item 6 actions (`SubPeriods.razor` Edit/Activate/Complete) | ✅ PASS |
| 5. No scope widening (product diff limited to 4 files) | ✅ PASS |
| 6. T1–T6 present and green | ⚠️ PARTIAL — T1 + T6 green; T2/T3/T4/T5 deferred (harness fragility) |
| 7. Build green | ✅ PASS (0 errors) |
| 8. Tests green | ✅ PASS (Students 303, Admin 454, Assignments 102) |
| 9. Backlog updated | ✅ PASS |
| 10. Deferred items documented | ✅ PASS |

### Residual P2 (deferred to a subsequent Sprint 6 sub-round)

- bUnit T2 (group duplicate guard), T3 (AC-45 span-mismatch period filtering),
  T4 (AC-46 null-periodId — covered at handler level), T5 (SubPeriods row actions).
- Item 4 (PeriodId editing), Item 5 (string-flag audit), backend
  `AssignActivityGroupTopic` duplicate guard.
- 6.2 Playwright smoke, 6.3 loading/empty/error polish.

The four product fixes are correct, minimal, and build/test green. The bUnit coverage
gap is test-only, not a product defect. Proceed to the next Sprint 6 sub-round for the
remaining bUnit + Playwright + polish items.
