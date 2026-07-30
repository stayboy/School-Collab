# Progress — Enrollment validation guard clauses

**Plan:** [`2026-07-29-enrollment-validation-guard-clauses.md`](./2026-07-29-enrollment-validation-guard-clauses.md)
**Branch target:** `feature/enrollment-validation-guard-clauses`
**Last updated:** 2026-07-30 (spec-pattern interface-gateway refactor)

> **2026-07-30 refactor — specification wiring redesign.** The validation pipeline was changed from "handler injects 3 concrete specs" to an **interface-gateway** design (option B from the design review):
> - New marker `ILeafEnrollmentSpecification : IEnrollmentSpecification` for the 3 leaf rules.
> - New gateway `ICompositeEnrollmentSpecification : IEnrollmentSpecification` exposing `ILeafEnrollmentSpecification? FailingSpecification`.
> - `CompositeEnrollmentSpecification` implements `ICompositeEnrollmentSpecification` and depends on `IEnumerable<ILeafEnrollmentSpecification>` (so it can't pull itself into its own dependency set).
> - Handler injects the single `ICompositeEnrollmentSpecification` abstraction (no concrete specs, no cast) and maps the failing rule to its typed exception via a new `ResolveException`.
> - DI reduced from 7 registrations to 4 — no double-instantiation, no dead registrations, no singular-resolve footgun, no circular-resolution risk.
>
> Build green, **104/104 unit tests still pass** after the refactor.

## Status: ALL PHASES COMPLETE — build green, 104/104 unit tests passing.

### Verification at last save
- `dotnet build` Students.Core → **succeeded** (0 errors)
- `dotnet build` Students.Api → **succeeded** (0 errors)
- `dotnet build` Students.Admin → **succeeded** (0 errors)
- `dotnet build` MigrationService → **succeeded** (0 errors)
- `dotnet build` Students.Tests.Unit → **succeeded** (0 errors, 0 warnings)
- `dotnet test` full Students unit suite → **104/104 passed** (86 original + 10 new spec tests + 8 new flag-on handler tests).

> Note: a full `dotnet build SchoolCollab.sln` may show `MSB3027`/`MSB3021`/`CS2012` file-lock errors — those are **environment** (running VS + `Students.Api`/`Students.Worker`/`Admin`/`Settings.Api`/`Assignments.Api` locking their bin/obj DLLs), not code errors. Each project builds cleanly on its own.

---

## Done — Phase 1 (Domain)

### `src/Students/SchoolCollab.Students.Core/Domain/GradeLevel.cs`
- Added nullable `int? MinAge`, `int? MaxAge`, `Guid? AllowedGenderCodedValueId`.
- `Create(...)` accepts the 3 new optional params; throws `GradeLevelConstraintException` when `MinAge > MaxAge`.
- `Update(...)` now accepts the 3 new optional params + same `MinAge > MaxAge` guard. ⚠️ Optional-default-null means an `Update` call that omits them NULLS the fields — this is intentional per plan §2; Phase 3 wires the real values through the UpdateGradeLevel command so `null` means "clear restriction".

### `src/Students/SchoolCollab.Students.Core/Data/Configurations/GradeLevelConfiguration.cs`
- Maps `min_age` / `max_age` / `allowed_gender_coded_value_id` (all nullable), snake_case, no FK (matches existing `CodedValueId` pattern).

### `src/Students/SchoolCollab.Students.Core/Domain/Exceptions/DomainExceptions.cs`
- New abstract `EnrollmentValidationException` (base) carrying `StudentId` + `GradeLevelId`.
- `StudentAgeViolationException : EnrollmentValidationException` (CalculatedAge, MinAge?, MaxAge?, DateOfBirth, EnrollmentDate as `DateOnly`, GradeLevelId).
- `StudentGenderViolationException : EnrollmentValidationException` (AllowedGenderCodedValueId?, StudentGenderCodedValueId?, GradeLevelId).
- `MultipleActiveEnrollmentsException : EnrollmentValidationException` (ExistingActiveEnrollmentIds, GradeLevelId). (Renamed from the earlier `StudentAlreadyActiveEnrollmentException`.)
- Kept `GradeLevelConstraintException` (standalone, for the GradeLevel entity's own MinAge>MaxAge guard).

### `src/Students/SchoolCollab.Students.Core/Domain/Specifications/` (NEW — plan §3)
- `IEnrollmentSpecification.cs` — base contract: `bool IsSatisfiedBy(EnrollmentContext)` + `string FailureMessage`.
- `ILeafEnrollmentSpecification.cs` — marker `interface ILeafEnrollmentSpecification : IEnrollmentSpecification {}` for the 3 leaf rules, so the composite can take `IEnumerable<ILeafEnrollmentSpecification>` without including itself.
- `ICompositeEnrollmentSpecification.cs` — gateway contract `interface ICompositeEnrollmentSpecification : IEnrollmentSpecification` adding `ILeafEnrollmentSpecification? FailingSpecification { get; }`. The handler depends on this abstraction.
- `EnrollmentContext.cs` (record) — Student, GradeLevel, EnrollmentDate (DateOnly), ExistingActiveEnrollments.
- `AgeRangeSpecification.cs` (`: ILeafEnrollmentSpecification`) — null DOB fails; inclusive `[MinAge, MaxAge]`; null bound = no limit. `internal static int ComputeAge(DateOnly dob, DateOnly asOf)` (birthday-not-yet-reached aware, leap-safe via `DateOnly.AddYears`).
- `GenderRestrictionSpecification.cs` (`: ILeafEnrollmentSpecification`) — null `AllowedGenderCodedValueId` ⇒ co-ed (satisfied); else must equal `Student.GenderCodedValueId`.
- `SingleActiveEnrollmentSpecification.cs` (`: ILeafEnrollmentSpecification`) — fails when any existing active enrollment exists (cross-period).
- `CompositeEnrollmentSpecification.cs` (`: ICompositeEnrollmentSpecification`) — AND-combines the registered `ILeafEnrollmentSpecification`s; short-circuits; exposes `ILeafEnrollmentSpecification? FailingSpecification` + `FailureMessage`.

### EF migration (plan §8)
- **`Migrations/20260729185904_AddEnrollmentValidationToGradeLevels.cs`** + `.Designer.cs` (EF Core timestamp-prefix convention), generated via `dotnet ef migrations add` using `Students.Core` as both project and startup-project (the in-repo `Data/DesignTimeStudentsDbContextFactory` avoids the running-API file locks). Adds nullable `min_age`, `max_age`, `allowed_gender_coded_value_id` to `grade_levels`; `Down` drops them. `StudentsDbContextModelSnapshot.cs` auto-updated.
- Deleted the earlier stray `migrations/0000000000000001_AddEnrollmentValidationToGradeLevels.sql` (wrong location/format/name — user-flagged).

---

## Done — Phase 2 (Application)

### `src/SchoolCollab.Core/Features/FeatureFlagKeys.cs`
- Added `EnableEnrollmentValidation = "FEATURE:EnableEnrollmentValidation"` (default OFF, gradual rollout).

### `src/Students/SchoolCollab.Students.Core/CQRS/Enrollments/Commands/EnrollStudent/EnrollStudentHandler.cs`
- Constructor adds: `IFeatureFlagService`, `IStudentRepository`, `ICompositeEnrollmentSpecification enrollmentSpecification` (the single validation gateway — no concrete leaf specs injected).
- New `ValidateEnrollmentAsync` runs **after** the FR-A3 active-period guard and **before** `StudentEnrollment.Create`, only when `await featureFlagService.IsEnabledAsync(FeatureFlagKeys.EnableEnrollmentValidation, ct)`.
- Resolves student (`StudentNotFoundException` if missing) + grade level (`GradeLevelNotFoundException`), fetches cross-period active enrollments via `GetActiveEnrollmentsByStudentAsync`, builds `EnrollmentContext`, then calls `enrollmentSpecification.IsSatisfiedBy(context)` once.
- On failure, a new `ResolveException(ICompositeEnrollmentSpecification, EnrollmentContext)` maps `enrollmentSpecification.FailingSpecification` to the typed exception via a `switch` on the leaf type (`AgeRangeSpecification` → `StudentAgeViolationException`, `GenderRestrictionSpecification` → `StudentGenderViolationException`, `SingleActiveEnrollmentSpecification` → `MultipleActiveEnrollmentsException`), with a defensive `InvalidOperationException` fallback. **Exception construction stays in the handler; specs stay side-effect-free apart from `FailureMessage`.**
- **Alignment with plan §6:** this now matches the plan's intent (a single composite gateway injected as the abstraction) while preserving rule-to-exception mapping — the earlier "inject 3 concrete specs" deviation was replaced by the `ILeafEnrollmentSpecification`/`ICompositeEnrollmentSpecification` split during the 2026-07-30 refactor.

### `src/Students/SchoolCollab.Students.Core/Data/Repositories/IStudentEnrollmentRepository.cs` + `StudentEnrollmentRepository.cs`
- Added `GetActiveEnrollmentsByStudentAsync(Guid studentId, CancellationToken)` (cross-period, `Status == EnrollmentStatus.Active`). Tenant scoping comes from the existing global query filter (`TenantEntityTypeConfigurationBase` applies `HasQueryFilter("Tenant", …)`), so NO `tenantId` param (matches plan §7; the earlier extra param was removed).

### `src/Students/SchoolCollab.Students.Core/Extensions.cs` (`AddStudentsCore`)
- 4 spec registrations (down from 7): the 3 leaf rules as `ILeafEnrollmentSpecification` (so `IEnumerable<ILeafEnrollmentSpecification>` resolves exactly them) + `CompositeEnrollmentSpecification` as `ICompositeEnrollmentSpecification` (the gateway the handler injects). The composite is NOT registered as `ILeafEnrollmentSpecification`, so it can't appear in its own dependency set — no circular resolution. No concrete-leaf registrations and no multi-`IEnrollmentSpecification` registrations, so each spec is instantiated once per scope and there's no singular-resolve footgun. `IFeatureFlagService` is available via `AddTenancy()`.

### Tests wiring
- `tests/SchoolCollab.Students.Tests.Unit/EnrollStudentHandlerTests.cs`: `NewHandler` factory builds `new CompositeEnrollmentSpecification(new ILeafEnrollmentSpecification[]{ new AgeRangeSpecification(), new GenderRestrictionSpecification(), new SingleActiveEnrollmentSpecification() })` (age → gender → single-active order, matching DI registration order) and passes it as the single `ICompositeEnrollmentSpecification` dep; `StubFeatureFlagService(bool enabled)` defaults OFF so the existing flag-off contract tests stay green. Uses real `StudentRepository(s.Db)` so flag-on tests can seed a `Student`.
- `tests/SchoolCollab.Students.Tests.Unit/Specifications/EnrollmentSpecificationTests.cs`: `CompositeSpec_FirstFailingSpecWins` constructs the composite with an `ILeafEnrollmentSpecification[]` (matches the redesigned ctor).

---

## Done — Phase 3 (API), §5 (Feature-flag seed), Phase 4 (Admin UI), Phase 5 (Tests)

### §5 Feature-flag seed (Settings) ✅
- `src/SchoolCollab.MigrationService/Program.cs` — added `SeedEnableEnrollmentValidationAsync` (idempotent, default `false`, audit row). Called after `SeedEnableGradeLevelSetupOnEnrollDialogAsync`.
- `src/Students/SchoolCollab.Students.Api/appsettings.json` — added `"EnableEnrollmentValidation": "false"` under `FeatureFlags:FEATURE`.
- `src/SchoolCollab.Admin/appsettings.json` — added `"EnableEnrollmentValidation": "false"` (Admin host reads the flag for any future UI gating).

### Phase 3 — API (plan §9) ✅
- `src/Students/SchoolCollab.Students.Core/DTOs/GradeLevelDto.cs` — added `MinAge`, `MaxAge`, `AllowedGenderCodedValueId` (all nullable, default null for backward compat).
- `CreateGradeLevel.cs` / `UpdateGradeLevel.cs` / `GetOrCreateGradeLevel.cs` — added the 3 fields (optional, default null).
- `CreateGradeLevelHandler.cs` — passes the 3 fields through to `GradeLevel.Create`.
- `UpdateGradeLevelHandler.cs` — passes the 3 fields through to `GradeLevel.Update`.
- `GetOrCreateGradeLevelHandler.cs` — on **reuse**, preserves the existing validation fields (not nulled by the wizard sync); on **create**, passes the command's fields. DTO return includes the 3 fields.
- `GradeLevelRoutes.cs` — `UpdateGradeLevelRequest` + `GetOrCreateGradeLevelRequest` records carry the 3 fields; endpoint handlers pass them through.
- All 3 query handlers (`GetGradeLevelByIdHandler`, `GetGradeLevelByCodedValueHandler`, `ListGradeLevelsHandler`) — DTO construction includes the 3 fields.
- Admin client `StudentsApiClient.cs` — `GradeLevelDto` + all 3 request records carry the 3 fields.

### Phase 4 — Admin UI (plan §10) ✅
- `GradeLevelFormFields.razor` — added Min Age (`FluentNumberField<int?>`), Max Age (`FluentNumberField<int?>`), Allowed Gender (`CodedValueDropdown Parent="CodedValueParent.Genders"` with "No restriction (co-ed)" placeholder). `GradeLevelFormModel` gained the 3 fields.
- `Create.razor` — passes the 3 model fields to `GetOrCreateGradeLevelAsync`.
- `Edit.razor` — loads the 3 fields from the DTO into the model; passes them to `UpdateGradeLevelAsync`.
- `EnrollStudentDialog.razor` — **verified, no change needed**: the dialog already surfaces the full exception message via `Error = ex.Message` (rendered in the per-field `FluentMessageBar` + `DialogShellFooter`). The `EnrollStudentAsync` client reads the response body on failure, so the typed validation exception messages render verbatim — same path as `PeriodNotOpenException` today.

### Phase 5 — Tests (plan §11) ✅
- **NEW** `tests/SchoolCollab.Students.Tests.Unit/Specifications/EnrollmentSpecificationTests.cs` — all 10 spec cases:
  - `AgeRangeSpecification_WithinRange_Satisfied`, `_TooYoung_NotSatisfied`, `_TooOld_NotSatisfied`, `_NoRange_Satisfied`
  - `GenderRestrictionSpecification_Match_Satisfied`, `_Mismatch_NotSatisfied`, `_NullAllowed_Satisfied`
  - `SingleActiveEnrollmentSpecification_NoActive_Satisfied`, `_HasActive_NotSatisfied`
  - `CompositeSpec_FirstFailingSpecWins`
- `EnrollStudentHandlerTests.cs` — added 8 flag-on/flag-off handler tests:
  - `AgeValidation_TooYoung/TooOld_ThrowsStudentAgeViolationException`, `AgeValidation_WithinRange_Persists`
  - `GenderValidation_Mismatch_ThrowsStudentGenderViolationException`, `GenderValidation_NullAllowed_Persists`
  - `MultipleActiveEnrollments_ThrowsMultipleActiveEnrollmentsException`
  - `FeatureFlag_Disabled_SkipsValidation_AndPersists`, `FeatureFlag_Enabled_NoGradeLevelRules_Persists`
  - `NewHandler` factory + `SeedStudent` helper support the new minAge/maxAge/allowedGender seeding.
- Integration tests (§11 integration) — **deferred** (requires a running Postgres + test-host fixture; the unit suite covers the handler + spec logic comprehensively).

### Acceptance criteria (plan §12) — status
1. ✅ GradeLevel stores the 3 nullable fields.
2. ✅ (flag on) age outside range throws `StudentAgeViolationException` & persists nothing — **test-pinned** (`AgeValidation_TooYoung/TooOld`).
3. ✅ (flag on) gender mismatch throws `StudentGenderViolationException` & persists nothing — **test-pinned** (`GenderValidation_Mismatch`).
4. ✅ (flag on) existing active enrollment throws `MultipleActiveEnrollmentsException` & persists nothing — **test-pinned** (`MultipleActiveEnrollments_Throws…`).
5. ✅ Flag off (default) ⇒ behaviour unchanged; existing handler tests pass (`FeatureFlag_Disabled_SkipsValidation_AndPersists` + original 86).
6. ✅ Existing active enrollments grandfathered (validation runs only for new enrollments; migration backfills null).
7. ✅ Admin UI can set Min/Max age + Allowed Gender (`GradeLevelFormFields.razor` + Create/Edit pages).
8. ✅ Validation exceptions render actionable messages in `EnrollStudentDialog` (verified — existing `Error = ex.Message` path; client reads body on 4xx).
9. ✅ Unit tests green — 104/104 passed. `dotnet build` clean for Students.Core, Students.Api, Students.Admin, MigrationService, Tests.Unit. Integration tests deferred (unit suite covers handler + spec logic).