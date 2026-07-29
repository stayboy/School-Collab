# Spec: Simplified Grade-Level Management + Grade Strands

> Status: **Draft / Plan**
> Owner: Students + Settings + Admin contexts
> Supersedes: the wizard portions of `grade-level-setup.md` §6 and the whole of
> `gradelevel-wizard-subject-override-per-row.md`. The landing-page aggregation
> (§7 of `grade-level-setup.md`) is **kept and verified**, not rebuilt.
> Depends on: `coded-values-architecture.md`, `multi-tenant-coded-values.md`,
> `coded-values-tenancy-impl.md`, `shared-coded-value-dropdown.md`,
> `landing-page-wrapper.md`, `ui-visible-tenancy-guard.md`

## 0. Decisions locked in this revision

1. **Keep the hybrid architecture.** `CodedValue` (+ `TenantCodedValueOverride`) is
   the shared, renameable *catalog*; `GradeLevel` / `Subject` /
   `GradeSubjectAssignment` are the tenant-owned, period-scoped, referentially-
   anchored *transactional* rows. `GradeLevel.CodedValueId` is the **reporting
   integrity** anchor (referenced by enrolments, assignments, teacher links).
   This spec does **not** fold grades into `CodedValue`.
2. **`CodedValue.DisplayOrder` IS the grade level.** No `level` attribute, no
   `Level` column on `CodedValue`. The grade's numeric level is its coded
   value's `DisplayOrder`. **Duplicity is tracked by `DisplayOrder`**: no two
   children of `GRADE` may share the same `DisplayOrder` (enforced in
   `Settings.Core`). The seed is renumbered so `DisplayOrder == level`
   (`GRADE_R=0`, `GRADE_1=1` … `GRADE_12=12`).
3. **Grade strands are coded values — no new tables.** A strand is a child of a
   new `GRSTRNDS` parent coded value. Each strand carries two attributes:
   - `gradeLevel` — a `CodedValue`-typed attribute (`SourceCode = GRADE`) that
     references the grade coded value the strand belongs to. This is the
     "attribute reference to grade levels."
   - `strandVersion` — a `Text` attribute holding a **strand label** like
     `1A`, `1B`, `1C` (regex `^\d+[A-Z]$`). This is the strand's sub-grade
     identity and the **duplicity key**: unique per grade.
   No new persistence table — strands reuse `coded_values` +
   `coded_value_attributes` (both already exist).
4. **One field on the student's grade reference for the strand.**
   `StudentEnrollment` gains a single nullable `GradeStrandCodedValueId`
   (Guid?) column pointing at the strand coded value. Set at enrolment and on
   transfer (step-up). No new table; one column on an existing table.
5. **Discard the 4-step `GradeLevelWizard`.** Replace with a single-page
   `GradeLevelForm` shared by create + edit (grade coded value + assigned
   subjects for the current period). Carried over from the prior revision.
6. **Strands are picked at enrolment, not on the grade form.** Strand coded
   values are managed in the coded-value admin (seeded or created there). The
   enrolment UI and the step-up transfer dialog get a strand picker filtered by
   the selected grade.
7. **The landing page aggregation is already implemented** and is preserved
   unchanged; this spec adds a verification gate only (§8).

## 1. Goal

A tenant admin can:

- On one screen (`GradeLevelForm`), pick/create/override a grade coded value and
  manage the subjects assigned to that grade for the current period — with the
  grade's `Level` correctly sourced from `CodedValue.DisplayOrder`.
- Define **grade strands** (e.g. Grade 1 → strands 1A, 1B, 1C) as coded
  values, each referencing its grade, with strand labels that are unique per
  grade.
- Enrol a student into a grade **and** a strand for the current period, and
  transfer (step up/down) the student to another grade+strand.
- See, on the landing page, the aggregated subject count and student count per
  grade for the current period (already works).

And the catalog guarantees: **no two grade coded values share a level
(DisplayOrder); no two strands of the same grade share a version number.**

## 2. Non-goals (this spec)

- A `Level` attribute or `Level` column on `CodedValue` (rejected — use
  `DisplayOrder`).
- New tables for strands or student-strand links (rejected — reuse coded values
  + one enrollment column).
- Student enrolment *creation* UI on the grade-level form (enrolment happens on
  the Students page; the grade form is grade + subjects only).
- Period creation / activation on the grade form (periods are managed on the
  Periods page + global `ActiveTermToolbar`).
- Strands / lessons of *subjects* (`SubjectStrand` / `SubjectLesson`) — those are
  a different concept and are untouched.
- Folding `GradeLevel` / `Subject` into `CodedValue` (rejected).
- Strand-aware landing aggregation (grouping counts by strand) — flagged as a
  follow-up (§11), not built here.

## 3. Current state (as found) + what changes

### 3.1 Grades as coded values (DisplayOrder = level — CHANGES)

`GRADE` parent seeds `GRADE_R`, `GRADE_1`…`GRADE_12` with `DisplayOrder`
1…13 (`GRADE_1.DisplayOrder = 2` today — **off by one**). `GradeLevel.Level`
(the Students.Core mirror) is today set by the wizard to
`CodedValue.DisplayOrder`, so `GradeLevel.Level` for Grade 1 is currently `2`,
not `1`. The step-up comparator (`StudentTransferDialog`) compares
`GradeLevel.Level`, so it is comparing display orders that are shifted by one
(and Grade R is level 1 instead of 0).

**After this spec:** `DisplayOrder` is renumbered to equal the level
(`GRADE_R=0`, `GRADE_1=1` … `GRADE_12=12`), and uniqueness is enforced on
`DisplayOrder` among `GRADE` children. `GradeLevel.Level` continues to mirror
`DisplayOrder` — now correctly.

### 3.2 The wizard (to be discarded)

`GradeLevelWizard.razor` is a 4-step wizard bundling grade creation, subject
assignment, and student enrolment. It is too heavy, couples three concerns, and
forces a period to be open before a grade can be created. **Deleted**; replaced
by `GradeLevelForm` (§6). `Edit.razor` is a bare stub today and is also replaced.

### 3.3 The landing page (kept, verified)

`GradeLevels.razor` on `<LandingPage<GradeLevelLandingDto>>` with Level, Name
(tenant-resolved), Subjects (count → link), Students (count → link), Period,
Actions. `ListGradeLevelsForLandingHandler` derives the current period
server-side and computes per-current-period counts. **No change** — §8 verifies.

### 3.4 Grade strands (NEW — modeled as coded values)

There is no "grade strand" concept today. `SubjectStrand` exists but is a strand
of a *subject* (used by `GradeSubjectAssignment.SubjectStrandId`) — a different
thing. This spec introduces **grade strands** (class/section divisions of a
grade, e.g. "Grade 1 — 1A / 1B / 1C") as coded values under a new
`GRSTRNDS` parent, with attributes linking each strand to its grade and
giving it a strand label. **No new table.**

### 3.5 Student enrolment (CHANGES — one field)

`StudentEnrollment` today: `StudentId`, `PeriodId`, `GradeLevelId`, `EnrolledOn`,
`ExitDate`, `Status`, `TransferReason`. `Create(studentId, periodId,
gradeLevelId, enrolledOn?)`; `Transfer(newGradeLevelId, transferDate?, reason?)`.
Commands `EnrollStudent` and `TransferStudent` mirror these. **This spec adds one
nullable column** `GradeStrandCodedValueId` (Guid?) to `StudentEnrollment` and
threads it through `Create` / `Transfer` and the two commands.

## 4. Data model

### 4.1 Grade coded values — DisplayOrder = level + uniqueness

| CodedValue | Parent | DisplayOrder (= level) |
|------------|--------|------------------------|
| `GRADE_R`  | `GRADE` | 0 |
| `GRADE_1`  | `GRADE` | 1 |
| `GRADE_2`  | `GRADE` | 2 |
| … | … | … |
| `GRADE_12` | `GRADE` | 12 |

- **Seed change:** renumber `DisplayOrder` in `seed.csv` so it equals the level
  (currently 1…13). A one-time data migration updates existing rows.
- **Uniqueness (duplicity tracking):** among children of `GRADE`, `DisplayOrder`
  must be unique. Enforced in `Settings.Core` (`CreateCodedValueHandler` /
  `UpdateCodedValueHandler`) when the parent is `GRADE`: throw a new
  `DuplicateGradeLevelException(level, existingCodedValueId)`.
- **DB guard (best-effort):** a partial unique index is impractical with a
  dynamic parent Guid, so the application check is the real guard. (Optional:
  a generated `is_grade_child` column + partial index — noted in §11, not
  required.)

### 4.2 Grade strand coded values — `GRSTRNDS` parent + two attributes

New parent coded value `GRSTRNDS` (seeded). Strand children use the code
pattern `GRSTRNDS_<level><letter>`, e.g. `GRSTRNDS_1A`, `GRSTRNDS_1B`,
`GRSTRNDS_2A`. Up to three strands per grade (letters A, B, C).

**Attribute definitions on `GRSTRNDS`** (added to
`seed-attribute-definitions.csv`):

```
ParentCode,Key,DisplayName,DataType,SourceCode,IsRequired,AllowMultiple,MinLength,MaxLength,RegexPattern
GRSTRNDS,gradeLevel,Grade Level,7,GRADE,True,False,,,
GRSTRNDS,strandVersion,Strand Version,0,,True,False,2,3,^\d+[A-Z]$
```

- `gradeLevel`: `DataType = 7` (CodedValue), `SourceCode = GRADE` → the valid
  values are the `Code`s of grade coded values (`GRADE_1`, …). This is the
  strand→grade reference (the "attribute reference to grade levels").
- `strandVersion`: `DataType = 0` (Text), regex `^\d+[A-Z]$` → values like
  `1A`, `1B`, `1C` (a numeric level prefix + a single letter). `MinLength=2`
  (`1A`), `MaxLength=3` (`12A`).

**Seeded strand coded values** — added to `seed.csv` (39 strands: 13 grades × 3):

```
GRSTRNDS,Grade Strands,Class/section divisions of a grade,,0
GRSTRNDS_0A,Grade R — A,,GRSTRNDS,1
GRSTRNDS_0B,Grade R — B,,GRSTRNDS,2
GRSTRNDS_0C,Grade R — C,,GRSTRNDS,3
GRSTRNDS_1A,Grade 1 — A,,GRSTRNDS,4
GRSTRNDS_1B,Grade 1 — B,,GRSTRNDS,5
GRSTRNDS_1C,Grade 1 — C,,GRSTRNDS,6
GRSTRNDS_2A,Grade 2 — A,,GRSTRNDS,7
GRSTRNDS_2B,Grade 2 — B,,GRSTRNDS,8
GRSTRNDS_2C,Grade 2 — C,,GRSTRNDS,9
GRSTRNDS_3A,Grade 3 — A,,GRSTRNDS,10
GRSTRNDS_3B,Grade 3 — B,,GRSTRNDS,11
GRSTRNDS_3C,Grade 3 — C,,GRSTRNDS,12
GRSTRNDS_4A,Grade 4 — A,,GRSTRNDS,13
GRSTRNDS_4B,Grade 4 — B,,GRSTRNDS,14
GRSTRNDS_4C,Grade 4 — C,,GRSTRNDS,15
GRSTRNDS_5A,Grade 5 — A,,GRSTRNDS,16
GRSTRNDS_5B,Grade 5 — B,,GRSTRNDS,17
GRSTRNDS_5C,Grade 5 — C,,GRSTRNDS,18
GRSTRNDS_6A,Grade 6 — A,,GRSTRNDS,19
GRSTRNDS_6B,Grade 6 — B,,GRSTRNDS,20
GRSTRNDS_6C,Grade 6 — C,,GRSTRNDS,21
GRSTRNDS_7A,Grade 7 — A,,GRSTRNDS,22
GRSTRNDS_7B,Grade 7 — B,,GRSTRNDS,23
GRSTRNDS_7C,Grade 7 — C,,GRSTRNDS,24
GRSTRNDS_8A,Grade 8 — A,,GRSTRNDS,25
GRSTRNDS_8B,Grade 8 — B,,GRSTRNDS,26
GRSTRNDS_8C,Grade 8 — C,,GRSTRNDS,27
GRSTRNDS_9A,Grade 9 — A,,GRSTRNDS,28
GRSTRNDS_9B,Grade 9 — B,,GRSTRNDS,29
GRSTRNDS_9C,Grade 9 — C,,GRSTRNDS,30
GRSTRNDS_10A,Grade 10 — A,,GRSTRNDS,31
GRSTRNDS_10B,Grade 10 — B,,GRSTRNDS,32
GRSTRNDS_10C,Grade 10 — C,,GRSTRNDS,33
GRSTRNDS_11A,Grade 11 — A,,GRSTRNDS,34
GRSTRNDS_11B,Grade 11 — B,,GRSTRNDS,35
GRSTRNDS_11C,Grade 11 — C,,GRSTRNDS,36
GRSTRNDS_12A,Grade 12 — A,,GRSTRNDS,37
GRSTRNDS_12B,Grade 12 — B,,GRSTRNDS,38
GRSTRNDS_12C,Grade 12 — C,,GRSTRNDS,39
```

**Seeded attribute values** — added to `seed-attributes.csv`. Each strand gets
two rows (`gradeLevel` + `strandVersion`). Shown for Grade R / 1 / 2; the
pattern repeats for all 13 grades (78 rows total):

```
Code,Key,Value
GRSTRNDS_0A,gradeLevel,GRADE_R
GRSTRNDS_0A,strandVersion,0A
GRSTRNDS_0B,gradeLevel,GRADE_R
GRSTRNDS_0B,strandVersion,0B
GRSTRNDS_0C,gradeLevel,GRADE_R
GRSTRNDS_0C,strandVersion,0C
GRSTRNDS_1A,gradeLevel,GRADE_1
GRSTRNDS_1A,strandVersion,1A
GRSTRNDS_1B,gradeLevel,GRADE_1
GRSTRNDS_1B,strandVersion,1B
GRSTRNDS_1C,gradeLevel,GRADE_1
GRSTRNDS_1C,strandVersion,1C
GRSTRNDS_2A,gradeLevel,GRADE_2
GRSTRNDS_2A,strandVersion,2A
GRSTRNDS_2B,gradeLevel,GRADE_2
GRSTRNDS_2B,strandVersion,2B
GRSTRNDS_2C,gradeLevel,GRADE_2
GRSTRNDS_2C,strandVersion,2C
…(repeats for GRADE_3 through GRADE_12)
```

**Duplicity / uniqueness (the strand-label rule):**

- Among strands whose `gradeLevel` attribute references the same grade, the
  `strandVersion` value must be unique. "Grade 1 / 1A" and "Grade 1 / 1A" is
  illegal; "Grade 1 / 1A" and "Grade 2 / 2A" is fine (different grades may
  reuse the letter suffix).
- **Prefix rule — relaxed (NOT enforced).** The seeded strands follow the
  convention that a label's leading digits match its grade's `DisplayOrder`
  (`1A`→Grade 1, `2A`→Grade 2), but this is a seeding convention only. The
  application does **not** validate that a strand label's leading digits equal
  the referenced grade's level. A `2A` strand attached to `GRADE_1` is
  allowed at the data level (only per-grade uniqueness is checked). This is
  deliberately relaxed for now — see §11 item 1.
- Enforced at `SetCodedValueAttributeHandler` (and at strand create): throw a
  new `DuplicateGradeStrandException(gradeCode, strandVersion,
  existingStrandCodedValueId)` on a same-grade duplicate. No prefix exception is
  thrown.

> The strand label `1A`, `1B`, `1C` is the strand's **level in the
> grade-subdivision sense** — the "level hack" the user named, used for tracking
> duplicity (uniqueness per grade). It is stored as an attribute, not as a
> column on `CodedValue`.

### 4.3 `StudentEnrollment.GradeStrandCodedValueId` — the one student field

`StudentEnrollment` gains:

```csharp
/// <summary>Optional grade strand (class/section) for this enrolment.
/// References a GRSTRNDS coded value whose gradeLevel attribute matches
/// this enrollment's GradeLevel. Null = enrolled in the grade with no strand.
/// No DB FK (cross-module to Settings.CodedValue); validated at write time.</summary>
public Guid? GradeStrandCodedValueId { get; private set; }
```

- Nullable. No DB foreign key (it points into the Settings module's
  `coded_values` table). Integrity is validated in the command handler.
- `Create(..., Guid? gradeStrandCodedValueId = null)` and
  `Transfer(Guid newGradeLevelId, Guid? newGradeStrandCodedValueId = null, ...)`
  gain the parameter.
- **Validation (in `EnrollStudentHandler` / `TransferStudentHandler`):** if a
  strand is provided, (a) the coded value exists and is a child of
  `GRSTRNDS`; (b) its `gradeLevel` attribute references the same grade
  coded value as `enrollment.GradeLevelId`'s `GradeLevel.CodedValueId`.
  Mismatch → `GradeStrandGradeMismatchException(strandCode, gradeCode)`.
- A transfer that changes the grade **must** also re-pick a strand (the old
  strand's `gradeLevel` won't match the new grade). The transfer command treats
  a strand carrying over to a different grade as a validation failure and
  requires the caller to pass a new strand (or null).
- New index for strand-filtered enrolment queries:
  `ix_student_enrollments_tenant_grade_strand` on
  `(TenantId, GradeStrandCodedValueId)` — supports a future "students in strand
  1A" view.

### 4.4 `GradeLevel` mirror — unchanged shape, correct source

`GradeLevel.Level` / `Name` / `DisplayOrder` stay as the denormalized mirror
(load-bearing for step-up and indexing). `Level` is sourced from
`CodedValue.DisplayOrder` (now == the real level). No `Level` attribute is
introduced. `GradeLevel` gains **no strand awareness** — strands live on the
enrollment, not on the grade.

## 5. Backend changes

### 5.1 Settings.Core — grade DisplayOrder uniqueness + strands

| Change | File |
|--------|------|
| Renumber `GRADE_*` `DisplayOrder` to equal level | `MigrationService/SeedData/seed.csv` |
| Data migration to renumber existing rows | `Settings.Core/Migrations/<ts>_RenumberGradeDisplayOrder.cs` |
| New `DuplicateGradeLevelException` | `Settings.Core/Domain/Exceptions/DuplicateGradeLevelException.cs` |
| DisplayOrder-uniqueness check for `GRADE` children | `CreateCodedValueHandler` + `UpdateCodedValueHandler` |
| Seed `GRSTRNDS` parent + 39 strand children | `seed.csv` |
| Seed `gradeLevel` + `strandVersion` definitions on `GRSTRNDS` | `seed-attribute-definitions.csv` |
| Seed strand attribute values (78 rows: 39 strands × 2 attrs) | `seed-attributes.csv` |
| New `DuplicateGradeStrandException` | `Settings.Core/Domain/Exceptions/` |
| Strand uniqueness validation | `SetCodedValueAttributeHandler` (+ `CreateCodedValueHandler` for strand children) |
| (Optional) reserved-key guard so `gradeLevel`/`strandVersion` only apply to `GRSTRNDS` | `SetCodedValueAttributeDefinitionHandler` |

**DisplayOrder uniqueness check (pseudo, in `CreateCodedValueHandler`):**
```
if parent.Code == "GRADE":
    if any sibling under parent has DisplayOrder == value:
        throw DuplicateGradeLevelException(value, sibling.Id)
```

**Strand uniqueness check (pseudo, in `SetCodedValueAttributeHandler`):**
```
if key == "strandVersion" and codedValue.Parent.Code == "GRSTRNDS":
    gradeCode = codedValue.attribute["gradeLevel"]
    if any sibling strand has gradeLevel==gradeCode and strandVersion==value:
        throw DuplicateGradeStrandException(gradeCode, value, sibling.Id)
```

### 5.2 Students.Core — enrollment strand field + validation

| Change | File |
|--------|------|
| Add `GradeStrandCodedValueId` property | `Domain/StudentEnrollment.cs` |
| Thread param through `Create` + `Transfer` | `Domain/StudentEnrollment.cs` |
| Migration: add nullable `grade_strand_coded_value_id` column + index | `Students.Core/Migrations/<ts>_AddGradeStrandToEnrollment.cs` |
| Configure column + index | `Data/Configurations/StudentEnrollmentConfiguration.cs` |
| Add param to `EnrollStudent` + `TransferStudent` records | `CQRS/Enrollments/Commands/...` |
| Validate strand ↔ grade match | `EnrollStudentHandler` + `TransferStudentHandler` |
| New `GradeStrandGradeMismatchException` | `Domain/Exceptions/` |
| `GetOrCreateGradeLevel` / `CreateGradeLevel` unchanged | — |
| `ListGradeLevelsForLanding` unchanged | — |
| DTO: add strand to enrollment DTOs / transfer model | `DTOs/`, `Contracts/` |

**Validation requires reading the strand's `gradeLevel` attribute.** The
handler resolves it via the existing `ICodedValueResolver` / a
`GetCodedValueById` query (the strand coded value's `Attributes` collection
already carries `gradeLevel`). No cross-module DB join — the Settings API /
in-process query is used.

### 5.3 Admin UI — strand picker at enrolment + transfer

- **`StudentTransferDialog`** (the step-up dialog): add a strand
  `CodedValueDropdown` (parent `GRSTRNDS`) that is **filtered** to strands
  whose `gradeLevel` attribute == the newly selected grade's coded value. Bound
  to `TransferStudent.NewGradeStrandCodedValueId`. When the grade changes, the
  strand picker resets and re-filters. The promote/demote label still derives
  from `GradeLevel.Level` (DisplayOrder) — unchanged logic, now correct.
- **Enrolment UI** (the Students page enrol flow / inline create): same strand
  picker, filtered by the chosen grade, bound to
  `EnrollStudent.GradeStrandCodedValueId`.
- The `CodedValueDropdown` may need a new filter mode "strands for grade X"
  (filter children of `GRSTRNDS` whose `gradeLevel` attribute == X). If the
  dropdown can't attribute-filter today, a small
  `GetCodedValuesByParentFilteredByAttribute` query is added in Settings.Core
  (in scope).

### 5.4 Nothing else changes

`CreateSubjectForGrade`, `RemoveGradeSubject`, `ListGradeSubjectAssignments…`,
`DeleteGradeLevel` (+ `GradeLevelReferencedException`), the landing query — all
reused. `SubjectStrand` / `SubjectLesson` are untouched.

## 6. The simplified form (`GradeLevelForm`)

Carried over from the prior revision, with the level sourcing corrected.

### 6.1 Component + routes

- **NEW** `GradeLevelForm.razor` (collocated in
  `Students.Admin/.../GradeLevels/`).
- `Create.razor` → `@page "/students/grade-levels/create"` →
  `<GradeLevelForm Mode="Create" />` (replaces `GradeLevelWizard.razor`).
- `Edit.razor` → `@page "/students/grade-levels/{Id:guid}/edit"` →
  `<GradeLevelForm Mode="Edit" GradeLevelId="Id" />` (replaces the bare stub).
- **DELETE** `GradeLevelWizard.razor` (+ `.css`), `GuardianAssignment.cs`,
  `GuardianAssignmentList.razor`.

### 6.2 Layout (single screen)

Grade section: `CodedValueDropdown` (parent `GRADE`) + Override / New-grade
dialogs (reused from `Admin.Shared`). On select, the form reads
`Level = cv.DisplayOrder`, `Name = cv.Name`, `DisplayOrder = cv.DisplayOrder`.
Subjects section: `CodedValueDropdown` (parent `SUBJECT`) + Add; list of
assigned subjects with Override / Remove, bound to `CreateSubjectForGrade` /
`RemoveGradeSubject` for the current period. If no current period, subject Add
is disabled with a warning; the grade can still be created.

### 6.3 Save

Create: `GetOrCreateGradeLevelAsync(codedValueId, level=DisplayOrder, name,
displayOrder)`; subjects already committed live. Edit: grade coded value locked
(reporting integrity); subjects committed live.

> Strands are **not** managed on this form. Strands are coded values managed in
> the coded-value admin (or seeded) and picked at enrolment (§5.3).

## 7. Step-up with strands

`StudentTransferDialog` compares `GradeLevel.Level` (now == the grade's
`DisplayOrder`) to label promote vs demote — logic unchanged, now correct. The
dialog gains a strand picker for the destination grade (§5.3). On a transfer
that changes the grade, the strand is required to be re-picked (or null); a
carry-over strand fails validation (§4.3). On a same-grade strand-only change,
`Transfer` is still used with the same `NewGradeLevelId` and the new strand.

## 8. Landing page aggregation — preserve + verify

`GradeLevels.razor` + `ListGradeLevelsForLandingHandler` unchanged. Verify
(§10): per-current-period `SubjectCount` / `StudentCount`, links, muted-0 +
"No current period" when no period, tenant- + period-aware cache. Strand-aware
grouping is a follow-up (§11).

## 9. File / change map

| Action | Path |
|--------|------|
| **EDIT** | `MigrationService/SeedData/seed.csv` — renumber `GRADE_*` `DisplayOrder`; add `GRSTRNDS` parent + 39 strand children |
| **EDIT** | `MigrationService/SeedData/seed-attribute-definitions.csv` — `gradeLevel` + `strandVersion` on `GRSTRNDS` |
| **EDIT** | `MigrationService/SeedData/seed-attributes.csv` — strand attribute values |
| **NEW** | `Settings.Core/Domain/Exceptions/DuplicateGradeLevelException.cs` |
| **NEW** | `Settings.Core/Domain/Exceptions/DuplicateGradeStrandException.cs` |
| **NEW** | `Settings.Core/Domain/Exceptions/DuplicateGradeStrandException.cs` |
| **EDIT** | `Settings.Core/.../CreateCodedValueHandler.cs` + `UpdateCodedValueHandler.cs` — grade DisplayOrder uniqueness |
| **EDIT** | `Settings.Core/.../SetCodedValueAttributeHandler.cs` — strand uniqueness |
| **NEW** | `Settings.Core/Migrations/<ts>_RenumberGradeDisplayOrder.cs` |
| **NEW** (opt) | `Settings.Core/CQRS/.../GetCodedValuesByParentFilteredByAttribute` — strand-by-grade picker query |
| **EDIT** | `Students.Core/Domain/StudentEnrollment.cs` — `GradeStrandCodedValueId` + `Create`/`Transfer` params |
| **EDIT** | `Students.Core/Data/Configurations/StudentEnrollmentConfiguration.cs` — column + strand index |
| **NEW** | `Students.Core/Migrations/<ts>_AddGradeStrandToEnrollment.cs` |
| **EDIT** | `Students.Core/CQRS/Enrollments/Commands/EnrollStudent/EnrollStudent.cs` (+ handler) |
| **EDIT** | `Students.Core/CQRS/Enrollments/Commands/TransferStudent/TransferStudent.cs` (+ handler) |
| **NEW** | `Students.Core/Domain/Exceptions/GradeStrandGradeMismatchException.cs` |
| **EDIT** | `Students.Core/DTOs/` + `Contracts/` — strand on enrollment/transfer models |
| **DELETE** | `Students.Admin/.../GradeLevels/GradeLevelWizard.razor` (+ `.css`) |
| **DELETE** | `Students.Admin/.../GradeLevels/GuardianAssignment.cs` + `GuardianAssignmentList.razor` |
| **REPLACE** | `Students.Admin/.../GradeLevels/Edit.razor` → `GradeLevelForm` (Edit) wrapper |
| **NEW** | `Students.Admin/.../GradeLevels/Create.razor` → `GradeLevelForm` (Create) wrapper |
| **NEW** | `Students.Admin/.../GradeLevels/GradeLevelForm.razor` (+ `.css`) |
| **EDIT** | `Students.Admin/.../Students/StudentTransferDialog.razor` (+ `StudentTransferModel.cs`) — strand picker |
| **EDIT** | enrolment UI — strand picker |
| **NEW tests** | `Settings.Tests.Unit` (grade + strand uniqueness); `Students.Tests.Unit` / `Admin.Tests.Unit` (form, enrollment strand validation, step-up ordering) |

## 10. Verification

1. **Seed:** `GRADE_*` `DisplayOrder` == level (0…12); `GRSTRNDS` parent +
   39 strand children exist (13 grades × 3); each strand has `gradeLevel` +
   `strandVersion` attributes.
2. **Grade uniqueness:** creating/updating a `GRADE` child with a
   `DisplayOrder` already used by a sibling throws
   `DuplicateGradeLevelException`.
3. **Strand uniqueness:** setting `strandVersion = 1A` on a second strand
   whose `gradeLevel = GRADE_1` throws `DuplicateGradeStrandException`. (The
   prefix-equals-grade rule is relaxed — a `2A` strand attached to `GRADE_1` is
   allowed.)
4. **Enrollment strand:** enroll with a strand whose `gradeLevel` matches the
   grade → persists; mismatch → `GradeStrandGradeMismatchException`. Null strand
   → persists.
5. **Transfer (step-up):** promote Grade 1 → Grade 2 requires a re-picked
   strand (or null); a Grade-1 strand carried to Grade 2 fails validation.
   Promote/demote label correct from `Level` (DisplayOrder).
6. **Strand picker:** filtered to strands of the selected grade; resets on
   grade change.
7. **Form:** create Grade 8 → `GradeLevel.Level == 8` (from DisplayOrder), name
   resolved, subjects assignable for the current period; edit locks the coded
   value.
8. **Landing:** counts match DB for the current period; links correct; no
   period → muted 0 + "No current period".
9. **No new tables:** `grep` for new `ToTable(` in the diff shows none — strands
   reuse `coded_values`/`coded_value_attributes`; strand link is one column on
   `student_enrollments`.
10. **Wizard removal:** `grep -r GradeLevelWizard` returns nothing in `src/`.

## 11. Open questions / follow-ups

1. **Strand label prefix strictness — RELAXED for now.** The seeded strands
   follow the convention that a label's leading digits match its grade's level
   (`1A`→Grade 1, `2B`→Grade 2), but this is a seeding convention only and is
   **not enforced** by the application. Only per-grade `strandVersion`
   uniqueness is enforced (`DuplicateGradeStrandException`). If strict
   prefix-locking is wanted later, it's one check to add back.
2. **Strand-aware landing aggregation.** Show per-strand student counts on the
   landing page (drill into a grade → strands). Not built here; flagged for a
   follow-up.
3. **Strand coded-value management UI.** Strands are managed in the generic
   coded-value admin today. If a dedicated "Strands" admin page per grade is
   wanted, that's a follow-up (it would still be CRUD over `GRSTRNDS`
   coded values, no new table).
4. **Denormalization drift sync.** `GradeLevel.Name`/`Level`/`DisplayOrder` are
   mirrors of the coded value (+ override). A `CodedValueUpdatedEvent` handler
   in Students.Core re-syncs the mirror — follow-up, not blocking (the form
   reads the live coded value, so create/edit is always correct).
5. **Grade R level = 0.** Confirmed: Grade R is level 0 (below Grade 1 in the
   step-up ladder). Step-up already treats 0 < 1 correctly.
6. **DB guard for grade DisplayOrder uniqueness.** The app-level check is the
   real guard. A partial unique index needs a fixed parent Guid (not portable
   across environments). A generated `is_grade_child` column + partial index is
   possible but not required — confirm if you want it.

## 12. Rollout order (one PR per step, each shippable)

1. **Settings: grade DisplayOrder = level + uniqueness + strands.** Renumber
   seed + data migration; `DuplicateGradeLevelException`; grade DisplayOrder
   uniqueness. Seed `GRSTRNDS` + `gradeLevel`/`strandVersion` definitions +
   39 strand children; `DuplicateGradeStrandException`; strand uniqueness
   validation. Unit tests.
2. **Students: enrollment strand field + validation.** `StudentEnrollment`
   column + migration + index; `EnrollStudent` / `TransferStudent` params +
   mismatch validation; DTOs/contracts. Unit tests.
3. **Admin: `GradeLevelForm` + strand pickers + wizard deletion.** New form +
   route wrappers; delete wizard + guardian files; strand picker on transfer
   dialog + enrolment UI (with the strand-by-grade filter query if needed).
   Form + step-up tests.
4. **Verify landing aggregation.** Run §10.8 checks; fix regressions in-scope.

## Appendix: What this spec supersedes

- `grade-level-setup.md` **§6 (Wizard)** — discarded; the dialog components and
  find-or-create save are absorbed into `GradeLevelForm`.
- `gradelevel-wizard-subject-override-per-row.md` — the per-row subject override
  UX moves into `GradeLevelForm`'s subjects section; the wizard framing is
  obsolete.
- `grade-level-setup.md` **§7 (Landing)** is **retained**, not superseded.
- The earlier `level`-attribute proposal (previous draft of this spec) is
  **replaced** by "`DisplayOrder` is the level" + grade strands (§0.2–§0.4).
