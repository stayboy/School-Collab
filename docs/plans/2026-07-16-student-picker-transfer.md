# Plan: Student Picker Grid + Grade Transfer with Audit

**Date:** 2026-07-16
**Status:** PLAN (not yet implemented)
**Branch target:** feature branch off `main`, squash-merge via PR (repo convention: push with `SCHOOLCOLLAB_ALLOW_PUSH=1`, wait for Build & Test CI, then squash-merge).

---

## 1. Goal (from request)

1. Fix the **Student picker dialog grid** to expand to the available dialog width.
2. **Add more columns** to the picker grid.
3. Show a **Name** column combining name + gender + age, plus a **DateOfBirth** column.
4. Name format: `FirstName + " " + LastName + " (Gender, Age)"`, and the **Name column should grow** to fill available space.
5. In the wizard, the picker lists only students **not assigned to the current grade level**; students **already in a different grade are excluded** from the picker.
6. The **Landing page** gets a **Transfer** feature to move a student from one grade to another, recording an **audit entry + reason**.
7. **Transfer is the unified promote/demote mechanism** — moving a student to a *higher* grade level is a **promotion**, to a *lower* grade level is a **demotion**. The target-grade dropdown already supports any grade, so no separate promotion/demotion UI is needed; the dialog shows a computed *Promote / Demote / Lateral* hint from the grade `Level`s.
8. **Drop the existing promotion alternative**: the Worker `PromotionService` (automated end-of-period promotion) and its `PromotionOutcome` / `IPromotionRule` / `StudentsPromotedEvent` machinery are removed. Transfer replaces them as the single way students move between grades.

---

## 2. Key codebase facts (research)

- **Two `StudentDto` types** exist:
  - `SchoolCollab.Students.Core.DTOs.StudentDto` (Core) — used by the picker & wizard (via a `ToCore(...)` mapping).
  - `SchoolCollab.Students.Admin.Services.StudentDto` (Admin client) — returned by `StudentsApiClient`.
  - Today `StudentDto` has `FirstName, LastName, DateOfBirth, GenderCodedValueId` — **no `Age`, no `GenderName`**.
- **Grade assignment** is via `StudentEnrollment` (`StudentEnrollment.cs`): `GradeLevelId`, `PeriodId`, `ExitDate`, `Status` (Active/Transferred/Withdrawn), `RowVersion`. Tenant-scoped.
- **Transfer already exists** but is minimal: `POST /enrollments/{id}/transfer` → `TransferStudentHandler` → `enrollment.Transfer(newGradeLevelId, transferDate)` → emits `StudentTransferredEvent`. **No `Reason`, no audit row.**
- **`EntityGrid`** (`SchoolCollab.Admin.Shared/Components/EntityGrid.razor`) is shared by the picker, guardian picker, and landing pages. It renders `<FluentDataGrid GridTemplateColumns="@EffectiveTemplate">`. `EffectiveTemplate` = `"44px " + GridSettings.GridTemplateColumns` (the `44px` is the select-checkbox column). Column layout is controlled by `LandingGridSettings.GridTemplateColumns`.
- **Picker** (`StudentPickerDialog.razor`) currently: `GridTemplateColumns = "120px minmax(180px,2fr) minmax(180px,2fr) 100px"`, columns = Number, FirstName, LastName, Age(template). Loads ALL tenant students via `StudentsApi.ListStudentsAsync(ct, search)`. Opened from the wizard with `new StudentPickerModel()` (no scoping).
- **Wizard** (`GradeLevelWizard.razor`) already computes `_alreadyEnrolledStudentIds` (students with an active enrollment in another grade this period) in `OnStep2ChangingAsync` via `ListEnrollmentsByPeriodAsync`. It only uses this to skip at save — the picker still shows everyone.
- **Audit pattern to mirror** (`FlagAuditEntry` + `FeatureFlagAuditor` + `IActorAccessor` in `Settings.Core`): append-only `IEntity, IAuditableEntity` row written in the same transaction as the mutation, carrying actor + reason. `IAuditableEntity` lives in `SchoolCollab.Core.Data`.
- **Coded values are a separate module.** `CodedValue` domain + `ICodedValueRepository` are in **Settings.Core** (`SettingsDbContext`). `Students.Core` has **no** reference to Settings.Core and **no** `CodedValue` DbSet. → Students.Core must NOT resolve coded-value names; only the Admin client service may (it already uses `CodedValuesApiClient`).

---

## 3. Design decisions

### 3.1 Age + GenderName belong in the DTO, built in the client service (NOT UI, NOT Students.Core)
- Add `Age` (`int?`) and `GenderName` (`string?`) to **both** `StudentDto` types.
- **`StudentsApiClient`** (Students.Admin) enriches every returned `StudentDto`:
  - `Age` computed from `DateOfBirth`.
  - `GenderName` resolved by collecting all distinct `GenderCodedValueId` and calling `CodedValuesApiClient.GetByIdsAsync(ids)` **once** (batched), then mapping.
  - Central private `EnrichStudentsAsync(StudentDto[]?)` helper; every list/get method passes results through it before returning.
- The 7 server projection sites in `Students.Core` (`new StudentDto(...)`) just carry the two new fields as `null` (the client fills them). **No `Students.Core` change touches coded values.**
- `ToCore(...)` in the picker copies `Age`/`GenderName` from the Admin DTO to the Core DTO.
- UI (picker + wizard enrolled list + landing page) binds directly to `student.Age` / `student.GenderName` — **no client-side `GetAgeLabel` / coded-value name resolution in the components.** (The wizard's existing `RefreshCodedValueNamesAsync`/`GenderName` helpers become obsolete for these fields and can be removed as cleanup.)
- **Why not server-side in Students.Core?** Students.Core cannot read `CodedValue` from its own DbContext (separate module). Doing it server-side would require a Students→Settings project reference (over-dependency the user rejected) or a Core→API HTTP call (anti-pattern). The client-service approach matches the existing wizard/picker pattern and keeps module boundaries intact.

### 3.2 Picker scoping (exclude already-assigned / other-grade students)
- Add `ExcludedStudentIds` (`IEnumerable<Guid>?`) + optional `PeriodId` (`Guid?`) to `StudentPickerModel`.
- Wizard passes `ExcludedStudentIds = _alreadyEnrolledStudentIds ∪ _assignedStudents.Select(s => s.Id)` when opening the dialog (Part 4, step A4).
- Picker filters `ListStudentsAsync` results client-side by `ExcludedStudentIds` (MVP, zero backend change).
- **Enhancement (recommended for correctness at scale):** add a backend `ListStudentsNotEnrolledAsync(periodId, search)` (EXCEPT students with an active enrollment for that period) and have the picker call it when `PeriodId` is supplied. Reuses the same `EnrichStudentsAsync` client path.

---

## 4. Implementation steps

### Part A — Picker grid: columns + grow + scope
**Files:** `StudentPickerDialog.razor`, `StudentPickerModel.cs`, `GradeLevelWizard.razor`, (optional) `StudentsApiClient.cs`.

1. **Columns** (replace current 4 with):
   - `StudentNumber` — "Number" (`120px`)
   - **Name** (TemplateColumn, grows): `@($"{x.FirstName} {x.LastName} ({(x.GenderName ?? "—")}, {x.Age ?? "—"})")` — `minmax(260px,1fr)`
   - `GenderName` — "Gender" (`150px`) (PropertyColumn `x => x.GenderName`, or TemplateColumn)
   - `Age` — "Age" (`90px`) (PropertyColumn `x => x.Age`)
   - `DateOfBirth` — "Date of Birth" (`160px`) (TemplateColumn → `x.DateOfBirth?.ToShortDateString()`)
2. **GridTemplateColumns** (no select prefix; EntityGrid adds `44px`):
   `"120px minmax(260px,1fr) 150px 90px 160px"` — Name is column 2 and grows.
3. **Scope:** add `ExcludedStudentIds`/`PeriodId` to `StudentPickerModel`; filter loaded items by exclusion set.
4. **Wizard wiring:** in `OnRequestAddStudents`, pass the composed `ExcludedStudentIds` into `new StudentPickerModel { ExcludedStudentIds = ..., PeriodId = _activePeriod?.Id }`.

### Part B — DTO enrichment in client service
**Files:** `Students.Core/DTOs/StudentDto.cs`, `Students.Admin/Services/StudentDto` (Admin record), `StudentsApiClient.cs`, `StudentPickerDialog.razor` (`ToCore`).

1. Add `int? Age` + `string? GenderName` to **both** `StudentDto` records (append to positional params).
2. In `StudentsApiClient`, add `private async Task<StudentDto[]?> EnrichStudentsAsync(StudentDto[]? items)`:
   - compute `Age` from `DateOfBirth`;
   - collect `GenderCodedValueId`s, call `CodedValuesApiClient.GetByIdsAsync(ids)` once, build `id→name` map, set `GenderName`.
   - return enriched DTOs (via `with` or `new`).
3. Wrap `ListStudentsAsync`, `ListDeletedStudentsAsync`, `ListStudentsByGradeAsync`, `GetStudentByIdAsync`, `GetStudentByNumberAsync`, `ListStudentsForGuardian` (client), etc. through `EnrichStudentsAsync`.
4. Update `ToCore(...)` in the picker to copy `Age`/`GenderName`.
5. Remove now-redundant client-side `GetAgeLabel`/`GenderName` usage in picker & wizard (bind to DTO fields).

### Part C — Transfer feature + audit/reason (Landing page) — unified promote/demote
The transfer dialog moves a student to **any** target grade. Compared to the
student's current grade `Level`, that is a **promotion** (higher), **demotion**
(lower), or **lateral** move; the dialog surfaces that as a hint. This single
feature replaces the old automated promotion flow (see Part D).

**UI:** `Students/Index.razor` (+ optional `Students/Detail.razor`), new `StudentTransferDialog.razor`.
**API/contract:** `StudentsApiClient.TransferStudentRequest`, `EnrollmentRoutes.cs`, `TransferStudent` command.
**Domain/audit:** `StudentEnrollment.cs`, `StudentTransferredEvent`, new `StudentTransferAuditEntry`, `StudentsDbContext`, config, migration, `StudentTransferAuditor`, `IActorAccessor` (Students.Core).

1. **UI — Students landing page:** add a "Transfer" row action (next to View/Delete) on `/students`. Opens `StudentTransferDialog` with the selected student id.
2. **`StudentTransferDialog`:**
   - Load the student's **active enrollment** for the current period via `ListEnrollmentsByStudentAsync(studentId)` (filter `Status == Active && ExitDate == null`; prefer the active period from `ListPeriodsAsync`).
   - Show current grade; populate a target-grade `<FluentSelect>` from `ListGradeLevelsAsync` (exclude current grade).
   - **Reason** `<FluentTextArea>` (required) + optional transfer date.
   - On confirm → `StudentsApi.TransferStudentAsync(enrollmentId, new TransferStudentRequest(targetGradeId, reason, transferDate))`; close with success; refresh the landing list.
   - Disable/guard when the student has no active enrollment (nothing to transfer).
3. **Contract:** add `string Reason` (required) to `TransferStudentRequest` (API) and to the `TransferStudent` command.
4. **Domain:** `StudentEnrollment.Transfer(newGradeLevelId, transferDate, reason)` — store `TransferReason` (and `TransferredAt`) on the entity; update `StudentTransferredEvent` to carry `FromGradeLevelId` + `Reason`.
5. **Audit (mirror `FlagAuditEntry`):**
   - New `StudentTransferAuditEntry` (Students.Core): `Id, TenantId, StudentId, FromGradeLevelId, ToGradeLevelId, PeriodId, Reason, ActorId, ActorDisplayName, OccurredAt, CreatedAt, UpdatedAt`; implements `IEntity, IAuditableEntity`; append-only.
   - Add `DbSet<StudentTransferAuditEntry>` + `EntityTypeConfigurationBase<...>` config + EF migration.
   - Add `IActorAccessor` to Students.Core (mirror Settings.Core; backed by the authenticated user's claims/tenant context).
   - New `StudentTransferAuditor` (mirror `FeatureFlagAuditor`) that adds the audit row to the `StudentsDbContext` in the same transaction.
   - `TransferStudentHandler` captures `fromGradeLevelId` (already does) + `reason`, records the audit row via the auditor before/with `repository.UpdateAsync`.
6. **(Optional) History read:** `GET /students/{id}/transfer-audit` + query handler returning `StudentTransferAuditEntryDto[]`, surfaced on the student Detail page.

---

### Part D — Drop the existing promotion alternative (Worker `PromotionService`)
The automated end-of-period promotion (`PromotionService`) is removed; transfer
(Part C) is now the only way students move between grades. **`NextPeriodId` on
`Period` is kept** — it is a general period-linking field used by the Period
feature, not only by promotion.

**Files to DELETE:**
- `src/Students/SchoolCollab.Students.Worker/Services/PromotionService.cs`
- `src/Students/SchoolCollab.Students.Worker/Services/PromotionOptions.cs`
- `src/Students/SchoolCollab.Students.Core/Domain/PromotionRule.cs` (`IPromotionRule` + `DefaultPromotionRule`)
- `src/Students/SchoolCollab.Students.Core/Domain/PromotionOutcome.cs` (enum)

**Files to EDIT:**
- `src/Students/SchoolCollab.Students.Core/Domain/Events/DomainEvents.cs` — remove `StudentsPromotedEvent`.
- `src/Students/SchoolCollab.Students.Core/Domain/StudentEnrollment.cs` — remove `PromotionOutcome?` property + the `promotionOutcome` param on `Create` (no remaining caller passes it).
- `src/Students/SchoolCollab.Students.Core/Data/Configurations/StudentEnrollmentConfiguration.cs` — remove the `promotion_outcome` column.
- `src/Students/SchoolCollab.Students.Worker/Program.cs` — remove `AddTenantDirectory`, `PromotionOptions` `Configure`, and `AddHostedService<PromotionService>` (these existed only for the promotion loop).
- **Migration:** keep `20260710230128_AddPromotionOutcome` for history; the schema migration generated for this feature (Part C audit table + `transfer_reason` column) also **drops `promotion_outcome`**, so the model/snapshot end consistent.

> Note: `ActivatePeriodHandler.cs` has a comment referencing `PromotionService` — update the comment only (no behavior change).

## 5. Files to change (summary)

| Area | File |
|------|------|
| Picker grid + scope | `src/Students/SchoolCollab.Students.Admin/Components/Students/StudentPickerDialog.razor` |
| Picker model | `src/Students/SchoolCollab.Students.Admin/Components/Students/StudentPickerModel.cs` |
| Wizard wiring | `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/GradeLevels/GradeLevelWizard.razor` |
| Core DTO | `src/Students/SchoolCollab.Students.Core/DTOs/StudentDto.cs` |
| Admin DTO | `src/Students/SchoolCollab.Students.Admin/Services/StudentsApiClient.cs` (record + 7 projection sites) |
| Transfer UI | `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/Index.razor`, new `StudentTransferDialog.razor` |
| Transfer API | `src/Students/SchoolCollab.Students.Api/Endpoints/EnrollmentRoutes.cs` |
| Transfer command/handler | `src/Students/SchoolCollab.Students.Core/CQRS/Enrollments/Commands/TransferStudent/*` |
| Domain | `src/Students/SchoolCollab.Students.Core/Domain/StudentEnrollment.cs`, `Domain/Events/DomainEvents.cs` |
| Audit | new `StudentTransferAuditEntry.cs`, config, `StudentsDbContext.cs`, migration, new `StudentTransferAuditor.cs`, `IActorAccessor.cs` (Students.Core) |
| **Drop promotion** | delete `PromotionService.cs`, `PromotionOptions.cs`, `PromotionRule.cs`, `PromotionOutcome.cs`; edit `DomainEvents.cs`, `StudentEnrollment.cs`, `StudentEnrollmentConfiguration.cs`, `Students.Worker/Program.cs` |

---

## 6. Testing / verification

- **Unit:** `EnrichStudentsAsync` (age + batched gender name), `TransferStudentHandler` writes audit row with correct from/to/reason/actor, picker exclusion logic.
- **Build & Test CI** must pass (repo gate: push requires `SCHOOLCOLLAB_ALLOW_PUSH=1`; PR must pass Build & Test before squash-merge).
- **Manual:**
  - Wizard "Add students" shows only unassigned students; students in another grade are absent.
  - Picker Name column grows to fill width; shows `First Last (Gender, Age)` + DOB column.
  - Landing page Transfer action opens dialog, requires a reason, moves the student, and the move is reflected; audit row persists with reason + actor.

---

## 7. Open questions / decisions

1. **DTO unification:** keep two `StudentDto` types (enrich both, copy via `ToCore`) vs. collapse to the Core DTO and change `StudentsApiClient` return types. Plan assumes keep-both (lower risk).
2. **Picker scope MVP:** client-side exclusion (zero backend change) vs. new `ListStudentsNotEnrolledAsync` backend query. Plan recommends client filter MVP, backend query as enhancement.
3. **Transfer location:** primary = Students landing page row action; optionally also on Student Detail. Plan assumes landing page.
4. **`IActorAccessor` in Students.Core:** mirror Settings.Core pattern; backed by the authenticated user's claims/tenant context (registered as `ClaimsPrincipalActorAccessor` in `Students.Api`). A `SystemActorAccessor` default is registered in `AddStudentsCore` so any host has a fallback.
5. **Promotion alternative — DECIDED:** the Worker `PromotionService` + `PromotionOutcome`/`IPromotionRule`/`StudentsPromotedEvent` are dropped; transfer is the unified promote/demote mechanism. `NextPeriodId` is retained.
