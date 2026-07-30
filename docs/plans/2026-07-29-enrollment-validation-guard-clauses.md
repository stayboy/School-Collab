# Plan — Enrollment validation guard clauses (age, gender, single-active)

> **Goal:** Add validation rules for enrolling students of a certain demography to guard against wrongful enrollment. Add age and gender guard clauses against enrollment, set up on `GradeLevel` entities to enforce the rules. Add a handler-level rule preventing a student from holding more than one active enrollment at a time.
>
> **Branch target:** `feature/enrollment-validation-guard-clauses`.
> **Convention references:** specification pattern (new), `featureflags-tenant-gates` skill (feature flag), existing `EnrollStudentHandler` guard pattern (`PeriodNotOpenException`), `GradeLevel` entity + EF configuration.

---

## 0. Prompt-to-plan traceability

| Prompt requirement | Plan section | Resolution |
|---|---|---|
| Add validation rules for enrolling students of a certain demography to guard against wrongful enrollment | §3 Specification pattern | `IEnrollmentSpecification` + composable specs |
| **Confirm** if there's a rule to allow student enrollment in more than 1 active enrollment | §1 Current-state finding + §3.3 | **Confirmed:** no handler-level guard exists today |
| Add age, gender guard clauses against enrollment | §3.1, §3.2 | `AgeRangeSpecification`, `GenderRestrictionSpecification` |
| Guard clauses must be set up on grade levels to enforce rule | §2 | `MinAge` / `MaxAge` / `AllowedGenderCodedValueId` on `GradeLevel` |

---

## 1. Current-state finding — multiple active enrollments

The prompt asks to **confirm** whether a rule currently allows a student to hold more than one active enrollment. Findings from read-only exploration:

- **Database:** `StudentEnrollmentConfiguration` defines a unique index
  `ix_student_enrollments_tenant_student_period` on `(TenantId, StudentId, PeriodId)`.
  This blocks a duplicate enrollment **within the same period** at the DB level, but
  does not block a student from holding active enrollments across different periods.
- **Handler:** `EnrollStudentHandler` does **not** check for an existing active
  enrollment before persisting. The unit test
  `TwoEnrollments_ForSameStudentAndPeriod_BothPersist` explicitly pins this contract:
  *"The handler does NOT enforce a single active enrollment per student+period …
  The single-active-enrollment invariant is enforced at the UX layer."*
  (Note: the in-memory test DbContext does not enforce the unique index, so both
  rows persist in the test; in Postgres the second insert would fail with a unique
  violation, surfacing as a raw `DbUpdateException` rather than a domain error.)
- **UX layer:** `EnrollStudentDialog` uses an `IsNewEnrollment` check
  (`EnrollStudentModel.SuggestedGradeLevelId` is null ⇒ new enrollment) to hide the
  inline-grade-setup path for a re-enrollment; the supported "move an already-enrolled
  student" path is the Transfer / Withdraw flow.

**Conclusion:** There is **no handler-level domain rule** preventing multiple active
enrollments. The invariant is partially enforced by the DB unique index (per period)
and by the UX dialog, but not as an explicit, testable domain guard. This plan adds
that guard as `SingleActiveEnrollmentSpecification` (§3.3), chosen "Single active only"
per the user decision.

---

## 2. GradeLevel entity enhancement

**File:** `src/Students/SchoolCollab.Students.Core/Domain/GradeLevel.cs`

Add nullable validation properties:

- `int? MinAge` — minimum age in years required to enroll (inclusive).
- `int? MaxAge` — maximum age in years allowed to enroll (inclusive).
- `Guid? AllowedGenderCodedValueId` — required gender coded value; `null` = co-ed / no restriction.

Update `Create(...)` and `Update(...)` signatures to accept the three new parameters
(keep them optional/nullable so existing callers and seeds continue to compile).
Add guard clauses in `Update` rejecting `MinAge > MaxAge` when both are set.

**File:** `src/Students/SchoolCollab.Students.Core/Data/Configurations/GradeLevelConfiguration.cs`

Map the new columns:
- `min_age` (int, nullable)
- `max_age` (int, nullable)
- `allowed_gender_coded_value_id` (uuid, nullable)

No FK to coded_values (the Settings API owns that table; mirror the existing
`CodedValueId` pattern — no DB FK, validated in domain logic).

---

## 3. Specification pattern for validation

**New directory:** `src/Students/SchoolCollab.Students.Core/Domain/Specifications/`

### 3.1 `IEnrollmentSpecification.cs`
```csharp
public interface IEnrollmentSpecification
{
    bool IsSatisfiedBy(EnrollmentContext context);
    string FailureMessage { get; }
}
```
A spec returns `false` and exposes a `FailureMessage` so the handler can throw the
matching typed exception (keeps exception construction in the handler, specs pure).

### 3.2 `EnrollmentContext.cs` (record)
```csharp
public sealed record EnrollmentContext(
    Student Student,
    GradeLevel GradeLevel,
    DateOnly EnrollmentDate,
    IReadOnlyList<StudentEnrollment> ExistingActiveEnrollments);
```

### 3.3 Specifications

- **`AgeRangeSpecification`** — satisfied when `MinAge == null || age >= MinAge` AND
  `MaxAge == null || age <= MaxAge`, where
  `age = (EnrollmentDate - Student.DateOfBirth) truncated to whole years`.
  Handles null DOB by failing (students require DOB per `Student.Create`).
- **`GenderRestrictionSpecification`** — satisfied when
  `AllowedGenderCodedValueId == null || == Student.GenderCodedValueId`.
- **`SingleActiveEnrollmentSpecification`** — satisfied when
  `ExistingActiveEnrollments` is empty. (Active = `EnrollmentStatus.Active`.)
- **`CompositeEnrollmentSpecification`** — AND-combines the three; exposes the first
  failing spec's message. Registered in DI as `IEnrollmentSpecification`.

---

## 4. New exception types

**File:** `src/Students/SchoolCollab.Students.Core/Domain/Exceptions/DomainExceptions.cs`

- `EnrollmentValidationException` (base) — carries `StudentId`, `GradeLevelId`.
- `StudentAgeViolationException` — message names student, grade, DOB, computed age, and the `[MinAge, MaxAge]` range.
- `StudentGenderViolationException` — message names student, grade, required gender coded value id, and the student's gender coded value id.
- `MultipleActiveEnrollmentsException` — message names student and the existing active enrollment id(s).

All include actionable, UI-renderable messages (mirrors `PeriodNotOpenException` style).

---

## 5. Feature flag

**File:** `src/SchoolCollab.Core/Features/FeatureFlagKeys.cs`
```csharp
/// <summary>
/// Enables demographic (age, gender) and single-active-enrollment validation
/// in EnrollStudentHandler. Disabled by default for gradual rollout; existing
/// active enrollments are grandfathered (validation applies to new enrollments
/// only).
/// </summary>
public const string EnableEnrollmentValidation = "FEATURE:EnableEnrollmentValidation";
```

Default: **disabled**. Seed the flag row (default `false`) in the Settings feature-flag
seed/migration so it appears on the ConfigFlags page.

---

## 6. EnrollStudentHandler updates

**File:** `src/Students/SchoolCollab.Students.Core/CQRS/Enrollments/Commands/EnrollStudent/EnrollStudentHandler.cs`

Add constructor dependencies:
- `IEnrollmentSpecification enrollmentSpecification`
- `IFeatureFlagService featureFlagService`
- `IStudentRepository studentRepository`

Insert validation block **after** the active-period guard and **before**
`StudentEnrollment.Create`, only when the flag is enabled:

```csharp
if (await featureFlagService.IsEnabledAsync(
        FeatureFlagKeys.EnableEnrollmentValidation, cancellationToken))
{
    var student = await studentRepository.GetAsync(command.StudentId, cancellationToken)
        ?? throw new StudentNotFoundException(command.StudentId);

    var gradeLevel = await gradeLevelRepository.GetAsync(command.GradeLevelId, cancellationToken)
        ?? throw new GradeLevelNotFoundException(command.GradeLevelId);

    var existing = await repository.GetActiveEnrollmentsByStudentAsync(
        command.StudentId, cancellationToken);

    var context = new EnrollmentContext(
        student, gradeLevel,
        command.EnrolledOn ?? DateOnly.FromDateTime(DateTime.UtcNow),
        existing);

    if (!enrollmentSpecification.IsSatisfiedBy(context))
    {
        throw ResolveException(enrollmentSpecification, context);
    }
}
```

`ResolveException` maps the failing spec to its typed exception
(age → `StudentAgeViolationException`, gender → `StudentGenderViolationException`,
single-active → `MultipleActiveEnrollmentsException`).

**Note:** The existing test
`TwoEnrollments_ForSameStudentAndPeriod_BothPersist` documents the *current* "no
handler guard" contract. With the flag **off** (default) the test still passes
unchanged. Add a new flag-on test that asserts the second enrollment now throws
`MultipleActiveEnrollmentsException`. Do not delete the existing test; gate it
behind the flag-off path or update its documentation.

---

## 7. Repository updates

**File:** `src/Students/SchoolCollab.Students.Core/Data/Repositories/IStudentEnrollmentRepository.cs`

Add:
```csharp
Task<StudentEnrollment[]> GetActiveEnrollmentsByStudentAsync(
    Guid studentId, CancellationToken cancellationToken = default);
```

**File:** `src/Students/SchoolCollab.Students.Core/Data/Repositories/StudentEnrollmentRepository.cs`

Implement: query `StudentEnrollments` where `StudentId == studentId &&
Status == EnrollmentStatus.Active`, tenant-scoped via the global filter.

(The existing `GetActiveEnrollmentsForPeriodAsync` is per-period; this new method
is per-student across all periods — the single-active rule is cross-period.)

---

## 8. Database migration

**New file:** `src/Students/SchoolCollab.Students.Core/Migrations/<timestamp>_AddEnrollmentValidationToGradeLevels.cs`

Add nullable columns to `grade_levels`:
- `min_age` int null
- `max_age` int null
- `allowed_gender_coded_value_id` uuid null

Backfill: leave null for all existing rows (no restriction ⇒ co-ed, any age), so
existing active enrollments remain valid and new enrollments only validate when a
grade level has the fields set **and** the flag is on.

---

## 9. GradeLevel API updates

**Files:**
- `src/Students/SchoolCollab.Students.Core/DTOs/GradeLevelDto.cs` — add `MinAge`, `MaxAge`, `AllowedGenderCodedValueId`.
- `src/Students/SchoolCollab.Students.Api/Endpoints/GradeLevelRoutes.cs` — accept the three fields on create/update request bodies and return them on reads.
- `src/Students/SchoolCollab.Students.Core/CQRS/GradeLevels/Commands/CreateGradeLevel/CreateGradeLevel.cs` and `.../UpdateGradeLevel/UpdateGradeLevel.cs` — add the fields to the command records.
- Corresponding handlers — pass through to `GradeLevel.Create` / `Update`.

---

## 10. Admin UI updates

**File:** `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/GradeLevels/GradeLevelFormFields.razor`

Add fields (use the `input-width-scale` skill widths):
- Min Age — `FluentNumberField<int?>`, width W3.
- Max Age — `FluentNumberField<int?>`, width W3.
- Allowed Gender — `CodedValueDropdown Parent="CodedValueParent.Genders"` with a
  "No restriction (co-ed)" clear option, width W6.

**File:** `src/Students/SchoolCollab.Students.Admin/Components/Students/EnrollStudentDialog.razor`

Surface the new validation exceptions in the existing per-field / footer error
`FluentMessageBar`. No new fields needed — validation is server-side; the dialog
already renders `Error` from `SubmitAsync`. Verify the typed exception messages
render verbatim (they do today for `PeriodNotOpenException`).

---

## 11. Test cases

### Unit — specifications
**New file:** `tests/SchoolCollab.Students.Tests.Unit/Specifications/EnrollmentSpecificationTests.cs`
- `AgeRangeSpecification_WithinRange_Satisfied`
- `AgeRangeSpecification_TooYoung_NotSatisfied`
- `AgeRangeSpecification_TooOld_NotSatisfied`
- `AgeRangeSpecification_NoRange_Satisfied` (both null)
- `GenderRestrictionSpecification_Match_Satisfied`
- `GenderRestrictionSpecification_Mismatch_NotSatisfied`
- `GenderRestrictionSpecification_NullAllowed_Satisfied`
- `SingleActiveEnrollmentSpecification_NoActive_Satisfied`
- `SingleActiveEnrollmentSpecification_HasActive_NotSatisfied`
- `CompositeSpec_FirstFailingSpecWins`

### Unit — handler
**File:** `tests/SchoolCollab.Students.Tests.Unit/EnrollStudentHandlerTests.cs`
- `AgeValidation_TooYoung_ThrowsStudentAgeViolationException`
- `AgeValidation_TooOld_ThrowsStudentAgeViolationException`
- `AgeValidation_WithinRange_Persists`
- `GenderValidation_Mismatch_ThrowsStudentGenderViolationException`
- `GenderValidation_NullAllowed_Persists`
- `MultipleActiveEnrollments_ThrowsMultipleActiveEnrollmentsException`
- `FeatureFlag_Disabled_SkipsValidation_AndPersists` (existing happy path stays green)
- `FeatureFlag_Enabled_NoGradeLevelRules_Persists` (rules null ⇒ no restriction)

### Integration
**File:** `tests/SchoolCollab.Students.Tests.Integration/`
End-to-end: flag on, grade level with MinAge=6 MaxAge=8, enroll 5-yr-old ⇒ 4xx with
age message; enroll 7-yr-old ⇒ 201; second active enrollment ⇒ 4xx with
single-active message.

---

## 12. Acceptance criteria

1. `GradeLevel` stores `MinAge`, `MaxAge`, `AllowedGenderCodedValueId` (nullable).
2. With flag on, enrolling a student whose age (computed from DOB vs enrollment date)
   is outside `[MinAge, MaxAge]` throws `StudentAgeViolationException` and persists nothing.
3. With flag on, enrolling a student whose gender ≠ `AllowedGenderCodedValueId`
   (when set) throws `StudentGenderViolationException` and persists nothing.
4. With flag on, enrolling a student that already has an active enrollment throws
   `MultipleActiveEnrollmentsException` and persists nothing.
5. With flag off (default), behavior is unchanged — existing handler tests pass.
6. Existing active enrollments are grandfathered (no backfill validation).
7. Admin UI lets a tenant set Min/Max age and Allowed Gender on a grade level.
8. All validation exceptions render actionable messages in `EnrollStudentDialog`.
9. Unit + integration tests green; `dotnet build` clean.

---

## 13. Assumptions & defaults

1. **Single active enrollment** — chosen "Single active only" (user decision).
2. **Age calculation** — whole years between DOB and enrollment date
   (`(EnrollmentDate - DOB).TotalDays / 365.2425`, truncated). Inclusive bounds.
3. **Gender** — null `AllowedGenderCodedValueId` ⇒ co-ed / no restriction.
4. **Feature flag** — `FEATURE:EnableEnrollmentValidation`, default **off**, gradual rollout.
5. **Specification pattern** — chosen for composability + unit testability.
6. **Migration scope** — new enrollments only; existing enrollments grandfathered.
7. **Cross-period single-active** — `GetActiveEnrollmentsByStudentAsync` is
   cross-period (not just the active period), so a student cannot be active in two
   periods simultaneously. Confirm this matches intent before implementation.

---

## 14. Implementation phases

1. **Domain** — `GradeLevel` properties, specifications, exceptions, migration.
2. **Application** — handler validation block, repository method, feature flag key + seed.
3. **API** — `GradeLevelDto` + route + command fields.
4. **Admin UI** — `GradeLevelFormFields` fields, dialog error surfacing verification.
5. **Tests** — spec unit tests, handler unit tests, integration tests.