# Plan: Entity Code Auto-Generation for Students and Staff

> **Status:** SPEC — do not implement; this is a specification document only.

**Date:** 2026-07-28
**Status:** SPEC
**Branch target:** `feature/entity-code-auto-generation` (squash-merge via PR)
**Related plans:**
- [2026-07-16-unified-audit-temporal-period.md](./2026-07-16-unified-audit-temporal-period.md) — temporal period infrastructure used by period-based code templates
- [2026-07-17-country-codes-for-contacts.md](./2026-07-17-country-codes-for-contacts.md) — CodedValues seed pattern used for migration data

---

## 1. Goal

Add an **entity code auto-generation** feature so the system can automatically produce unique, formatted identifiers for entities — starting with **students** (`STU-…`) and **staff/teachers** (`STF-…`). The feature must be **configurable** so that template rules, stamps, sequence formatting, and period-based resets can be changed without code changes.

### 1.1 Requirements (verbatim from the user)

1. **Auto-generate** the next sequential code for an entity (student, staff) on creation.
2. **Stamped prefix** — the entity type stamp is always present in the generated code (e.g., `STU` for students, `STF` for staff).
3. **Templated format** — the body of the code is defined by an ordered sequence of configurable segments. Each segment is either fixed text or an auto-incrementing sequence (numeric, alphabetic, or alphanumeric with rollover). Period tokens (year, month, quarter) are resolved internally per segment based on its ResetPeriod setting.
4. **Period-based grouping** — sequence resets per period when configured (e.g., reset every year, every month, or every quarter).
5. **Configurable** — templates and rules are stored in the Settings bounded context, not hard-coded; rules are fully managed through the admin UI.
6. **Minimum viable** — implement for students, staff, and assignments in v1.

### 1.2 Non-Goals (v1)

- No support for custom entity types beyond Student, Staff, and Assignment in v1 (extensible architecture, but only three implementations).
- No support for batch code generation (single entity creation only).
- No support for real-time code-format preview as the user edits the template in the admin panel (preview is computed on save).
- No support for code-format preview in the student/staff/assignment creation form (generated code is shown after creation).
- No support for multi-tenant sequence isolation (all tenants share the same rule's sequence counter; per-tenant counters are a follow-up).

---

## 2. Key codebase facts (research)

### 2.1 Existing CodedValues system (Settings bounded context)

- **`CodedValue`** entity (`src/Settings/SchoolCollab.Settings.Core/Domain/CodedValue.cs`) — hybrid tenant entity with `Code`, `Name`, `Description`, `ParentId`, `Attributes`, `AttributeDefinitions`, `TenantId`. Currently used for reference data: Genders, Subjects, Grades, Salutations, Countries, etc.
- **`CodedValueAttributeDefinition`** — defines attribute slots on parent values; children populate them with typed metadata.
- **`CodedValueAttribute`** — key-value pairs attached to individual coded values (e.g., `dial_code` for country codes).
- **`SettingsDbContext`** — unified DbContext; CodedValue is a hybrid tenant entity (shared blueprints + tenant-owned rows).
- **CQRS commands**: `CreateCodedValue`, `UpdateCodedValue`, `DeleteCodedValue`, `BulkCreateCodedValues`, `EnableCodedValue`, `DisableCodedValue`, `RecoverCodedValue`.
- **REST API**: `/api/coded-values` with full CRUD, `/api/coded-values/bulk`, tenant overrides.
- **Seed data**: `src/SchoolCollab.MigrationService/SeedData/seed.csv` — CSV-driven, idempotent, topological insertion. No student/staff generation rules exist yet.
- **Admin UI**: `src/Settings/SchoolCollab.Settings.Admin/Components/Pages/CodedValues/` — Index, Create, Edit, Children views.
- **Admin client**: `src/SchoolCollab.Admin.Shared/Services/CodedValuesApiClient.cs` — typed HTTP client.

### 2.2 Student, Staff, and Assignment entities

- **`Student`** (`src/Students/SchoolCollab.Students.Core/Domain/Student.cs`) — has `StudentNumber` (plain string, set manually on creation today), `FirstName`, `LastName`, `DateOfBirth`, `GenderCodedValueId`. Uses `ITenantEntity`.
- **`Teacher`** (`src/Students/SchoolCollab.Students.Core/Domain/Teacher.cs`) — has **no** staff number/code field yet. Has `TitleCodedValueId`, `FirstName`, `LastName`, `DisplayName`, `Email`, `ContactPhone`, `StaffUserId`.
- **`StudentCreatedEvent`** — domain event fired on student creation (used for integration events, audit).
- **`Assignment`** (`src/Assignments/SchoolCollab.Assignments.Core/Domain/Assignment.cs`) — lives in the **Assignments bounded context** (separate from Students). Has **no** code/number field yet. Has `Title`, `Description`, `AssignmentType`, `SubjectId`, `GradeLevelId`, `Status`, `CreatedByTeacherId`, `DueDate`. Uses `ITenantEntity` (tenant-scoped — `TenantId` is required, not the hybrid tenant model).
- **`AssignmentCreatedEvent`** / `AssignmentCreatedIntegrationEvent` — domain + integration events fired on assignment creation.
- **`CreateAssignmentCommandHandler`** (`src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/CreateAssignmentCommand/`) — calls `Assignment.Create(...)` then `repository.AddAsync`; emits the integration event. This is the integration point for assignment code generation.

### 2.3 CQRS and patterns

- All domain writes go through CQRS commands handled in `Settings.Core` and `Students.Core`.
- `ICommandHandler<TCommand, TResult>` interface with `HandleAsync`.
- Repository pattern: `ICodedValueRepository` in Settings.Core.
- `ModuleDbContext` base class with tenant filtering, soft-delete query filters, audit properties.
- DTOs in `Settings.Core/DTOs/` and `Students.Core/DTOs/`.

### 2.4 Existing period/temporal infrastructure

- The `2026-07-16-unified-audit-temporal-period` plan introduces a unified temporal period model (year, quarter, month) used across the system. This will be referenced for period-based code reset logic.

---

## 3. Design decisions

### 3.1 New entity: `EntityCodeRule`

A new domain entity in the Settings bounded context that stores **how** a code should be generated for a given entity type. It lives alongside the existing `CodedValue` aggregate (they are separate concerns: coded values are reference data; generation rules are operational configuration).

**Why a separate entity, not extending `CodedValue`?**
- Generation rules have different lifecycle semantics (they are operational infra, not reference data).
- They need fields that don't fit the `CodedValue` model (segment configuration, per-segment sequence state, period reset config).
- Keeping them separate avoids polluting the CodedValue query/filter contracts.

**Naming convention (`EntityCode*`, not `CodedValue*`):** the existing `CodedValue` entity holds reference data (genders, subjects, grades, countries, salutations). The auto-generation feature is a different concern — it produces sequential **entity codes** (student numbers, staff numbers). To avoid a naming clash and make the domain clear, the new types use the `EntityCode*` prefix: `EntityCodeRule` (the rule + template), `EntityCodeSegment` (a template segment), `IEntityCodeGenerator` (the generation service). The existing `CodedValue` reference-data types are unchanged.

**Industry-standard conventions reflected in this design:**
- **Stamped entity prefixes** (`STU`, `STF`) — common in student information systems (SIS) and HR systems to namespace identifiers by entity type.
- **Zero-padded sequential numbers** with configurable width — the dominant pattern for human-readable document/record numbers (invoice numbers, student IDs, employee IDs).
- **Alphanumeric series with rollover** (A01→A09→B01) — used in classroom/section naming and seat numbering where a leading letter groups items.
- **Period-based sequence reset** (yearly/monthly/quarterly) — standard for fiscal-year document numbering and academic-year cohort numbering.
- **Check digits** (e.g., Luhn, Verhoeff) are **not** included in v1; they are a follow-up if identifier integrity/validation becomes a requirement. The segment model is extensible enough to add a `CheckDigitSegment` type later.

### 3.2 Template format: ordered segments

The generation template is a **sequence of ordered segments**, each defined by its role, type, and optional auto-increment configuration. The final code is produced by concatenating all segments in index order.

Three named roles are available for segments (a rule does **not** need all three; it can have any number of segments, and any segment can carry any role or be unnamed):

| Role | Description | Example |
|------|-------------|--------|
| **Prefix** | Text that precedes the stamp in the generated code | `"STU"` in `STUA01` |
| **Stamp** | The entity-type identifier, always present | `"STU"` for students, `"STF"` for staff |
| **Suffix** | Text that follows the stamp/sequence in the generated code | `"-2026"` in `STU01-2026` |

Each segment can be one of four types:

| Type | Description | Example |
|------|-------------|--------|
| `Fixed` | Static text, never changes | `"STU"`, `"2026"` |
| `NumericSequence` | Auto-incrementing zero-padded number | `01`, `02`, …, `99` |
| `AlphabeticSequence` | Auto-incrementing alphabetic series | `A`, `B`, …, `AA`, `AB` |
| `AlphanumericSequence` | Auto-incrementing prefix+number with rollover | `A01`, `A02`, …, `A09`, `B01`, `B02`, … |

Each segment also has:

| Property | Description | Values |
|----------|-------------|--------|
| `Index` | Position in the template (0 = first) | `0`, `1`, `2`, … |
| `ResetPeriod` | If and when the sequence resets | `None`, `Yearly`, `Monthly`, `Quarterly` |
| `MinWidth` | Minimum digits/letters for the sequence | `2` → `01`, `02` |
| `UpperLimit` | When exceeded, triggers rollover to the next prefix | `"09"` (after A09 → B01) |

The user can assign any of the three roles (prefix, stamp, suffix) to any segment and can also add unnamed custom segments. Each role can independently be set to auto-increment (or not).

**Template examples:**

Example 1 — stamp + alphanumeric sequence (default student template):

| Index | Role | Type | FixedText | Prefix | Suffix |
|-------|------|------|-----------|--------|--------|
| 0 | stamp | Fixed | `STU` | — | — |
| 1 | (none) | AlphanumericSequence | — | `A` | — |

Produces: `STUA01`, `STUA02`, …, `STUA09`, `STUB01`, …

Example 2 — prefix + stamp sequence + fixed suffix:

| Index | Role | Type | FixedText | Prefix | Suffix |
|-------|------|------|-----------|--------|--------|
| 0 | prefix | Fixed | `STU-` | — | — |
| 1 | stamp | AlphanumericSequence | — | `A` | — |
| 2 | (none) | Fixed | `-2026` | — | — |

Produces: `STU-A01-2026`, `STU-A02-2026`, …, `STU-A09-2026`, `STU-B01-2026`, …

Example 3 — year prefix + stamp + numeric sequence (two independent sequences):

| Index | Role | Type | FixedText | Prefix | Suffix | ResetPeriod |
|-------|------|------|-----------|--------|--------|-------------|
| 0 | prefix | NumericSequence | — | — | `-` | Yearly |
| 1 | stamp | Fixed | `STU-` | — | — | — |
| 2 | (none) | NumericSequence | — | — | — | None |

Produces: `2026-STU-0001`, `2026-STU-0002`, … (the index-0 segment resets yearly; the index-2 segment never resets). Each NumericSequence maintains its own `LastSequence` and `LastPeriodBucket`.

**Upper-limit and rollover behavior (per sequence type):**

The user-set `UpperLimit` restrains increments for all sequence types. Behavior when the limit is hit differs by type:

| Sequence type | Behavior when `UpperLimit` is hit |
|---------------|-----------------------------------|
| `NumericSequence` | Throws `EntityCodeGenerationCollisionException` (the sequence cannot roll over; the admin must widen `MinWidth` or raise `UpperLimit`). |
| `AlphabeticSequence` | Throws `EntityCodeGenerationCollisionException` when the alphabetic value exceeds `UpperLimit` (e.g., past `Z`). |
| `AlphanumericSequence` | Rolls over: the alphabetic prefix increments by one (`A` → `B`, `AA` → `AB`) and the numeric portion resets to 1. When the prefix itself exceeds a configurable max (default `Z`), throws `EntityCodeGenerationCollisionException`. |

Example trace for `AlphanumericSequence` with `Prefix=A`, `MinWidth=2`, `UpperLimit=09`:
```
A01 → A02 → … → A09 → B01 → B02 → … → B09 → C01 → …
```

### 3.3 Period-based sequence reset (per segment)

Each `EntityCodeRule` contains an ordered list of `EntityCodeSegment` entities. Each segment independently carries its own `ResetPeriod`:

| ResetPeriod | Behavior |
|-------------|----------|
| `None` | Sequence never resets; monotonically increasing across all time |
| `Yearly` | Sequence resets to 1 on Jan 1 of each calendar year |
| `Monthly` | Sequence resets to 1 on the 1st of each month |
| `Quarterly` | Sequence resets to 1 at the start of each quarter (Jan 1, Apr 1, Jul 1, Oct 1) |

The "last sequence" and "last period bucket" are tracked **per segment**, so different segments on the same rule can have different reset schedules. For example, a prefix segment could reset yearly (`2026-`, `2027-`) while the stamp sequence resets monthly (`A01` in Jan, `A01` again in Feb).

This is a significant change from the previous design: the rule-level `ResetPeriod`, `LastSequence`, and `LastPeriodBucket` are replaced by per-segment state stored on the `EntityCodeSegment` entity.

### 3.4 Where generation happens

Code generation is triggered **on entity creation** (Student.Create, and later Teacher.Create) by a `IEntityCodeGenerator` service injected into the command handlers. The handler:
1. Looks up the active generation rule for the entity type.
2. Iterates the rule's ordered segments in index order.
3. For each Fixed segment: outputs `FixedText` unchanged.
4. For each sequence segment: checks if that segment's `LastPeriodBucket` changed versus the current period; if so, resets `LastSequence` to 0 and `LastPrefix` to `"A"`. Increments the sequence, rolls over the alphabetic prefix if `UpperLimit` is hit, and formats with `Prefix`, `MinWidth`, and `Suffix`.
5. Concatenates all segment outputs into the final code string.
6. Persists updated per-segment state (`LastSequence`, `LastPrefix`, `LastPeriodBucket`).
7. Returns the generated code.

Period bucket computation (per segment):

| ResetPeriod | Bucket key format | Example |
|-------------|-------------------|--------|
| `None` | (empty) | — |
| `Yearly` | Year string | `"2026"` |
| `Monthly` | `Year-Month` | `"2026-07"` |
| `Quarterly` | `Year-Quarter` | `"2026-Q3"` |

### 3.5 Rule persistence and state

Each `EntityCodeRule` stores:
- `Name` (string, e.g., "Student Code Template")
- `Description` (string)
- `IsActive` (bool) — only one active rule per entity type
- `TenantId` (Guid?) — hybrid tenant isolation

Each rule has a collection of **`EntityCodeSegment`** entities (ordered by `Index`):

`EntityCodeSegment` persisted state:
- `Index` (int) — position in the template
- `Role` (string?) — `"prefix"`, `"stamp"`, `"suffix"`, or null for unnamed segments
- `SegmentType` (enum) — `Fixed`, `NumericSequence`, `AlphabeticSequence`, `AlphanumericSequence`
- `FixedText` (string?) — for Fixed-type segments
- `Prefix` (string?) — leading static text for sequence segments (e.g., `"A"` in `A01`)
- `Suffix` (string?) — trailing static text (e.g., `"-2026"` in `01-2026`)
- `ResetPeriod` (enum) — per-segment reset schedule
- `MinWidth` (int) — minimum digits/letters (e.g., 2 for `01`)
- `UpperLimit` (string?) — when exceeded, triggers alphabetic rollover (e.g., `"09"` → A09 rolls to B01)
- `LastSequence` (int) — last used numeric sequence for this segment
- `LastPrefix` (string?) — current alphabetic prefix for alphanumeric segments (e.g., `"A"`)
- `LastPeriodBucket` (string?) — period bucket of the last generated code for this segment

The full generated code is produced at runtime by:
1. Iterating segments in index order.
2. For Fixed segments: output `FixedText`.
3. For sequence segments: check if `LastPeriodBucket` has changed; if yes, reset `LastSequence` to 0 and `LastPrefix` to `"A"` (or the initial prefix).
4. Increment the sequence and format with `Prefix`, `MinWidth`, and `Suffix`.
5. Concatenate all segment outputs.
6. Persist updated per-segment state.

This avoids a full sequence table while supporting rich, multi-segment templates. For multi-instance deployments, pessimistic locking on the rule+segment rows handles concurrency.

### 3.6 Student, Staff, and Assignment default segment configurations

| Entity | Generated field | Rule code | Default segments (index order) |
|--------|----------------|-----------|-------------------------------|
| Student | `StudentNumber` | `STUDENT_CODE` | 0: Fixed/`STU` (stamp), 1: AlphanumericSequence/Prefix=`A`, MinWidth=2, UpperLimit=`09` |
| Staff/Teacher | `StaffNumber` | `STAFF_CODE` | 0: Fixed/`STF` (stamp), 1: AlphanumericSequence/Prefix=`A`, MinWidth=2, UpperLimit=`09` |
| Assignment | `AssignmentNumber` | `ASSIGNMENT_CODE` | 0: Fixed/`ASG` (stamp), 1: AlphanumericSequence/Prefix=`A`, MinWidth=2, UpperLimit=`09` |

Default generated codes:
- Student: `STUA01`, `STUA02`, …, `STUA09`, `STUB01`, `STUB02`, …
- Staff: `STFA01`, `STFA02`, …, `STFA09`, `STFB01`, `STFB02`, …
- Assignment: `ASGA01`, `ASGA02`, …, `ASGA09`, `ASGB01`, `ASGB02`, …

The `StaffNumber` field is **new** on the `Teacher` entity (Teacher has no number today). The `AssignmentNumber` field is **new** on the `Assignment` entity (Assignment has no code today). The `StudentNumber` field is an existing plain string that will be populated automatically by the generator when the rule is active.

### 3.7 Seed data

Three root-level `EntityCodeRule` entries are seeded via `SeedData/seed.csv`, each with their `EntityCodeSegment` children:

**Rules (seed.csv):**

| Code | Name | Description | IsActive |
|------|------|-------------|----------|
| STUDENT_CODE | Student Code Template | Auto-generates student student numbers | true |
| STAFF_CODE | Staff Code Template | Auto-generates staff staff numbers | true |
| ASSIGNMENT_CODE | Assignment Code Template | Auto-generates assignment numbers | true |

**Student rule segments (seed.csv or seed-attribute CSVs):**

| RuleCode | Index | Role | SegmentType | FixedText | Prefix | Suffix | ResetPeriod | MinWidth | UpperLimit |
|----------|-------|------|-------------|-----------|--------|--------|-------------|----------|------------|
| STUDENT_CODE | 0 | stamp | Fixed | STU | — | — | None | — | — |
| STUDENT_CODE | 1 | (none) | AlphanumericSequence | — | A | — | None | 2 | 09 |

**Staff rule segments:**

| RuleCode | Index | Role | SegmentType | FixedText | Prefix | Suffix | ResetPeriod | MinWidth | UpperLimit |
|----------|-------|------|-------------|-----------|--------|--------|-------------|----------|------------|
| STAFF_CODE | 0 | stamp | Fixed | STF | — | — | None | — | — |
| STAFF_CODE | 1 | (none) | AlphanumericSequence | — | A | — | None | 2 | 09 |

**Assignment rule segments:**

| RuleCode | Index | Role | SegmentType | FixedText | Prefix | Suffix | ResetPeriod | MinWidth | UpperLimit |
|----------|-------|------|-------------|-----------|--------|--------|-------------|----------|------------|
| ASSIGNMENT_CODE | 0 | stamp | Fixed | ASG | — | — | None | — | — |
| ASSIGNMENT_CODE | 1 | (none) | AlphanumericSequence | — | A | — | None | 2 | 09 |

The above seed data produces:
- Students: `STUA01`, `STUA02`, …, `STUA09`, `STUB01`, `STUB02`, …
- Staff: `STFA01`, `STFA02`, …, `STFA09`, `STFB01`, `STFB02`, …
- Assignments: `ASGA01`, `ASGA02`, …, `ASGA09`, `ASGB01`, `ASGB02`, …

Tenant overrides on the rule allow per-tenant template customization (e.g., a tenant can change prefix to `STUD-` or switch to a period-based reset). These are implemented via the `TenantEntityCodeRuleOverride` table (see §4.12).

---

## 4. Detailed design

### 4.1 New domain entity: `EntityCodeRule`

**File:** `src/Settings/SchoolCollab.Settings.Core/Domain/EntityCodeRule.cs`

```csharp
public enum SegmentType { Fixed = 0, NumericSequence = 1, AlphabeticSequence = 2, AlphanumericSequence = 3 }

public sealed class EntityCodeRule : IEntity, ISoftDeletableEntity, IHasRowVersion, IHybridTenantEntity
{
    public Guid Id { get; private set; }
    public string Code { get; private set; } = default!;     // e.g. "STUDENT_CODE"
    public string Name { get; private set; } = default!;     // e.g. "Student Code Template"
    public string Description { get; private set; } = default!;
    public bool IsActive { get; private set; }               // only one active rule per entity type
    public int Version { get; private set; }                 // optimistic concurrency for rule updates
    public Guid? TenantId { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<EntityCodeSegment> Segments => _segments.AsReadOnly();
    private readonly List<EntityCodeSegment> _segments = [];

    // Domain methods: AddSegment, RemoveSegment, ReorderSegments, Activate, Deactivate
}
```

### 4.1.1 New sub-entity: `EntityCodeSegment`

**File:** `src/Settings/SchoolCollab.Settings.Core/Domain/EntityCodeSegment.cs`

```csharp
public sealed class EntityCodeSegment
{
    public Guid Id { get; private set; }
    public Guid GenerationRuleId { get; private set; }
    public int Index { get; private set; }             // position in the template
    public string? Role { get; private set; }         // "prefix", "stamp", "suffix", or null
    public SegmentType Type { get; private set; }
    public string FixedText { get; private set; } = "";  // for Fixed-type segments
    public string Prefix { get; private set; } = "";    // leading text for sequence segments (e.g., "A" in A01)
    public string Suffix { get; private set; } = "";    // trailing text (e.g., "-2026" in 01-2026)
    public int ResetPeriod { get; private set; }        // 0=None, 1=Yearly, 2=Monthly, 3=Quarterly
    public int MinWidth { get; private set; }           // e.g., 2 for "01"
    public string? UpperLimit { get; private set; }     // e.g., "09" — after A09, rolls to B01
    public int LastSequence { get; private set; }       // last used numeric sequence
    public string? LastPrefix { get; private set; }     // current alphabetic prefix (A, B, …)
    public string? LastPeriodBucket { get; private set; }

    // Domain methods: Increment, Reset, Format
}
```

**Entity configuration:** `src/Settings/SchoolCollab.Settings.Core/Data/Configurations/EntityCodeRuleConfiguration.cs`

- `EntityCodeRule` → table `entity_code_rules`, hybrid tenant
- `EntityCodeSegment` → table `entity_code_segments`, owned by `EntityCodeRule`
- Unique index on `Code` (rule level)
- Query filter for active rules only
- Segments queried ordered by `Index`

### 4.2 New fields on Teacher and Assignment entities

**Teacher** — `src/Students/SchoolCollab.Students.Core/Domain/Teacher.cs` — add:

```csharp
public string? StaffNumber { get; private set; }
```

Update `Teacher.Create` to accept an optional `staffNumber` parameter (nullable; populated by the generator).

**Assignment** — `src/Assignments/SchoolCollab.Assignments.Core/Domain/Assignment.cs` — add:

```csharp
public string? AssignmentNumber { get; private set; }
```

Update `Assignment.Create` to accept an optional `assignmentNumber` parameter (nullable; populated by the generator). This is a schema change in the **Assignments bounded context** and requires an EF Core migration there (the Assignments `DbContext` is separate from the Settings `DbContext`).

### 4.3 New command: `GenerateNextEntityCode`

**Command:** `src/Settings/SchoolCollab.Settings.Core/CQRS/EntityCodes/Commands/GenerateNextEntityCode/GenerateNextEntityCode.cs`

```csharp
public sealed record GenerateNextEntityCode(string RuleCode, CancellationToken CancellationToken);
```

**Handler:** `GenerateNextEntityCodeHandler.cs` — implements `ICommandHandler<GenerateNextEntityCode, string>`:
1. Load the active rule by `RuleCode` (including its `EntityCodeSegment` collection, ordered by `Index`).
2. Iterate segments in index order:
   - For **Fixed** segments: output `FixedText` unchanged.
   - For **sequence segments** (`NumericSequence`, `AlphabeticSequence`, `AlphanumericSequence`):
     a. Compute the current period bucket for the segment using its `ResetPeriod`.
     b. If the bucket differs from the segment's `LastPeriodBucket`, reset `LastSequence` to 0 and `LastPrefix` to `"A"` (or the initial prefix).
     c. Increment `LastSequence`.
     d. If `LastSequence` exceeds `UpperLimit` (e.g., `09`):
        - Roll over: increment `LastPrefix` alphabetically (`A` → `B`, `AA` → `AB`).
        - Reset `LastSequence` to 1.
        - If `LastPrefix` exceeds a configurable max (e.g., `Z`), throw `EntityCodeGenerationCollisionException`.
     e. Format the segment: concatenate `Prefix` + padded `LastSequence` + `Suffix`.
3. Concatenate all segment outputs into the final code string.
4. Persist updated per-segment state (`LastSequence`, `LastPrefix`, `LastPeriodBucket`).
5. Return the generated code.

**Concurrency:** Pessimistic locking on the rule row (EF Core `ExecuteUpdateAsync` with `WHERE RowVersion = expectedVersion`). On conflict, the handler retries up to 3 attempts.

### 4.4 Integration: Student creation

**Command handler change:** `CreateStudentHandler.cs` (in `SchoolCollab.Students.Core/CQRS/Students/Commands/CreateStudent/`):
1. Call `IEntityCodeGenerator.GenerateAsync("STUDENT_CODE")` before constructing the entity, and pass the generated code into `Student.Create(...)` as the `StudentNumber`.
2. If no active rule exists for `STUDENT_CODE`, the generator throws `EntityCodeRuleNotFoundException` (a `DomainException`) which propagates and aborts the create — no student is persisted without a code. (The handler does not catch/rethrow; `EntityCodeGenerationException` is reserved for the narrower case where an active rule exists but has no segments.)
3. `command.StudentNumber` is retained on the command for API compatibility but is **not used** — the generated code is canonical.

### 4.5 Integration: Teacher and Assignment creation

**Teacher creation** — command handler change in `CreateTeacherHandler.cs` (`src/Students/SchoolCollab.Students.Core/CQRS/Teachers/Commands/CreateTeacher/`):
1. Call `IEntityCodeGenerator.GenerateAsync("STAFF_CODE")`.
2. Assign result to `teacher.StaffNumber`.
3. Add `StaffNumber` to the `Teacher` entity's `Create` factory method.

**Assignment creation** — command handler change in `CreateAssignmentCommandHandler.cs` (`src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/CreateAssignmentCommand/`):
1. Call `IEntityCodeGenerator.GenerateAsync("ASSIGNMENT_CODE")`.
2. Assign result to `assignment.AssignmentNumber`.
3. Add `AssignmentNumber` to the `Assignment.Create(...)` factory method call in the handler (pass it through from the generated code).
4. If no active rule exists for `ASSIGNMENT_CODE`, the generator throws `EntityCodeRuleNotFoundException` which propagates and aborts the create (same model as student creation above).

> **Cross-bounded-context dependency:** `IEntityCodeGenerator` is implemented in the **Settings** bounded context (where `EntityCodeRule` is stored) but its contract lives in **`SchoolCollab.Core/EntityCodes`** (not `Settings.Contracts`) — Students.Core and Assignments.Core reference only `SchoolCollab.Core`, so the contract is placed where they can see it without coupling to Settings. The Assignments bounded context consumes it the same way the Students bounded context does; only the API hosts (`Students.Api`, `Assignments.Api`) reference Settings.Core and call `AddSettingsCore` to register the `EntityCodeGenerator` implementation. `Assignment` is tenant-scoped (`ITenantEntity`, required `TenantId`) — in v1 the generated sequence counter is shared across tenants on the same rule (per §1.2 non-goal); only the template format is tenant-overridable (§4.12).

### 4.6 Service interface: `IEntityCodeGenerator`

```csharp
public interface IEntityCodeGenerator
{
    Task<string> GenerateAsync(string ruleCode, CancellationToken ct = default);
}
```

Implementation: `EntityCodeGenerator` — injects `ISchoolCollabSettingsDbContext` (or a dedicated repository) and the `EntityCodeRule` repository.

### 4.7 API endpoints for generation rules

Add a new **`EntityCodeRuleRoutes.cs`** route group under `/api/entity-code-rules` (a standalone routes file, separate from the existing `CodedValueRoutes.cs` which serves reference data). Mutating endpoints are keyed by **`{id:guid}`** (robust Guid routing, no code-encoding issues); rule lookup by code is a separate `by-code` GET. Segments are **not** exposed as standalone sub-resources — they ride along on the rule GET (`Segments[]` in the DTO) and are updated atomically with the rule via the replace-all `PUT /{id}` (the admin segment editor posts the full ordered list in the rule update). Activate uses **POST** (an action, not a PUT of resource state).

| Method | Path | Handler | Description |
|--------|------|---------|-------------|
| GET | `/` | `ListGenerationRules` | List all rules (with segments) |
| GET | `/{id:guid}` | `GetGenerationRuleById` | Get rule by id (with segments), ignoring soft-delete |
| GET | `/by-code/{code}` | `GetGenerationRuleByCode` | Get the **active** rule by code (generator lookup path; 404 if inactive/deleted) |
| POST | `/` | `CreateGenerationRule` | Create a new rule with segments (409 on duplicate code) |
| PUT | `/{id:guid}` | `UpdateGenerationRule` | Update rule metadata + replace-all segments (404 / 409 on concurrency) |
| DELETE | `/{id:guid}` | `DeleteGenerationRule` | Soft-delete a rule (404 if not found) |
| POST | `/{id:guid}/activate` | `ActivateRule` | Activate a rule (deactivates other active rules with the same `Code`) |

> **Note (divergence from earlier draft):** an earlier version of this table keyed mutating endpoints by `/{code}` and listed separate `GET/PUT /{code}/segments` endpoints. The implementation uses `{id:guid}` keys and folds segment read/update into the rule endpoints (above), which is more robust and avoids a separate segment sub-resource. The §5.5 integration-test list was updated to match.

### 4.8 New CQRS commands for rules CRUD

| Command | Description |
|---------|-------------|
| `CreateEntityCodeRule` | Admin creates a new rule |
| `UpdateEntityCodeRule` | Admin updates rule metadata and segment configuration |
| `DeleteEntityCodeRule` | Soft-delete a rule |
| `ActivateEntityCodeRule` | Set a rule as active (deactivates others for same entity type) |

### 4.9 Seed data additions

Rules rows in `SeedData/seed.csv`:

```csv
STUDENT_CODE,Student Code Template,Auto-generates student student numbers,true
STAFF_CODE,Staff Code Template,Auto-generates staff staff numbers,true
ASSIGNMENT_CODE,Assignment Code Template,Auto-generates assignment numbers,true
```

`EntityCodeSegment` child rows (inserted after rule rows, topologically):

| RuleCode | Index | Role | SegmentType | FixedText | Prefix | Suffix | ResetPeriod | MinWidth | UpperLimit |
|----------|-------|------|-------------|-----------|--------|--------|-------------|----------|------------|
| STUDENT_CODE | 0 | stamp | Fixed | STU | — | — | None | — | — |
| STUDENT_CODE | 1 | (none) | AlphanumericSequence | — | A | — | None | 2 | 09 |
| STAFF_CODE | 0 | stamp | Fixed | STF | — | — | None | — | — |
| STAFF_CODE | 1 | (none) | AlphanumericSequence | — | A | — | None | 2 | 09 |
| ASSIGNMENT_CODE | 0 | stamp | Fixed | ASG | — | — | None | — | — |
| ASSIGNMENT_CODE | 1 | (none) | AlphanumericSequence | — | A | — | None | 2 | 09 |

Tenant overrides on the rule allow per-tenant template customization (e.g., a tenant can change a Fixed segment's text or switch a sequence segment's ResetPeriod). These are implemented via the `TenantEntityCodeRuleOverride` table (see §4.12).

> **Note on seed CSV format:** generation rules are a separate entity from `CodedValue`, so they are **not** seeded via the existing `seed.csv` (which follows the `Code,Name,Description,ParentId,Order` schema for reference data). A dedicated `EntityCodeRuleSeeder` (and optional `entity-code-rules.csv`) handles rule + segment insertion, following the same idempotent, topological pattern as `CodedValueSeeder`.

### 4.10 Extensibility

The `EntityCodeRule` + `EntityCodeSegment` model is extensible for future entity types by:
1. Adding a new rule code to seed data (e.g., `ENROLLMENT_CODE` for enrollments).
2. Defining the appropriate segments (fixed text + sequence types + reset periods).
3. Calling `IEntityCodeGenerator.GenerateAsync("ENROLLMENT_CODE")` in the relevant command handler.
No new infrastructure is needed per entity type.

### 4.11 Admin UI: Generation Rules management

The admin panel for generation rules lives in the Settings bounded context, consistent with the existing CodedValues admin UI pattern. This is a **v1 requirement** — rules must be fully manageable through the admin UI, not just the API.

**New page: Generation Rules** — accessible from the Settings admin navigation, alongside the existing Coded Values page.

**UI components** (new Razor components under `src/Settings/SchoolCollab.Settings.Admin/Components/Pages/EntityCodeRules/`):

| Component | Description |
|-----------|-------------|
| `Index.razor` | Lists all generation rules with their code, name, active status, and a preview of the rendered template. Supports search/filter and pagination. |
| `Create.razor` | Form to create a new rule. Fields: Code (unique identifier), Name, Description, IsActive. Includes the `SegmentEditor` for defining the initial segments. |
| `Edit.razor` | Form to edit rule metadata + segments. Same layout as Create. |
| `SegmentEditor.razor` | Interactive segment builder for the rule's template. Supports adding, removing, and reordering segments. Each segment row shows: Index, Role (dropdown: prefix/stamp/suffix/none), Type (dropdown: Fixed/NumericSequence/AlphabeticSequence/AlphanumericSequence), FixedText (input for Fixed), Prefix/Suffix (inputs for sequence types), ResetPeriod (dropdown), MinWidth (number input), UpperLimit (text input). |
| `SegmentsList.razor` | Read-only render of the ordered segments with a live preview of the next few codes the template would produce (computed on save, not real-time per keystroke — see §1.2 non-goal). |

**SegmentEditor row controls:**

| Field | Control | Applies to |
|-------|---------|------------|
| Index | Auto-assigned by drag-and-drop order | All |
| Role | Dropdown: prefix / stamp / suffix / (none) | All |
| Type | Dropdown: Fixed / NumericSequence / AlphabeticSequence / AlphanumericSequence | All |
| FixedText | Text input | Fixed |
| Prefix | Text input (leading static text, e.g., `A`) | Sequence types |
| Suffix | Text input (trailing static text, e.g., `-2026`) | Sequence types |
| ResetPeriod | Dropdown: None / Yearly / Monthly / Quarterly | Sequence types |
| MinWidth | Number input (e.g., 2 for `01`) | Sequence types |
| UpperLimit | Text input (e.g., `09`, `99`, `Z`) | Sequence types |

**UI interactions:**
- Creating/editing a rule saves the rule + all its segments atomically (single PUT/POST to the API).
- Activating a rule deactivates any other active rule for the same entity type (enforced server-side).
- Deleting a rule soft-deletes it (preserves existing generated codes on students/staff).
- The SegmentEditor provides validation: duplicate indices, missing `FixedText` on Fixed segments, missing `Prefix` on AlphanumericSequence segments, `MinWidth` ≤ 0, and `UpperLimit` format checks.
- A "Preview" button renders the next 5 codes the current segment configuration would produce, so the admin can verify the format before saving.

**Admin client** (`src/SchoolCollab.Admin.Shared/Services/EntityCodeRulesApiClient.cs`) — typed HTTP client mirroring the existing `CodedValuesApiClient` pattern, with methods for all §4.7 endpoints plus the §4.12 override endpoints (`GetOverridesAsync`, `ReplaceOverridesAsync`).

### 4.12 Tenant overrides

A `TenantEntityCodeRuleOverride` table stores per-tenant, per-segment, per-field
overrides for a shared-blueprint rule. Only the overridden fields are stored
(delta model), so a tenant can change one segment's `FixedText` or `Prefix`
without redefining the whole template. The active rule for a code is the one
the generator finds; if it is the **shared** blueprint (`TenantId = null`)
tenant overrides layer on top at generation time. If the active rule is
**tenant-owned** (a tenant has created their own rule with the same code)
no overrides apply — the tenant already has full control of the format.

| Column | Type | Description |
|--------|------|-------------|
| `TenantId` | `Guid` (NOT NULL) | The tenant scope. The override is scoped per tenant; the default sentinel `Guid.Empty` is rejected by the factory to keep the dev default out of override rows. |
| `GenerationRuleId` | `Guid` (NOT NULL) | The rule being overridden (FK by Guid, not a DB-level FK because the segments table is owned and has no DbSet). |
| `EntityCodeSegmentId` | `Guid` (NOT NULL) | The segment being overridden (v1: segment-level only). |
| `Field` | `int` (NOT NULL) | Which field is overridden. Stored as the integer value of `OverrideField` (FixedText=0, Prefix=1, Suffix=2, ResetPeriod=3, MinWidth=4, UpperLimit=5). |
| `Value` | `varchar(200)` (NOT NULL) | The tenant-specific value (stringly-typed; cast to int at apply time for `MinWidth` / `ResetPeriod`). |
| `CreatedAt` / `UpdatedAt` | `timestamptz` | Audit columns. |

The unique index `(TenantId, GenerationRuleId, EntityCodeSegmentId, Field)`
ensures one row per (tenant, rule, segment, field). A second lookup index
`(TenantId, GenerationRuleId)` is added for the per-rule query path.

**Override semantics at generation time:**

1. Load the active rule + its segments.
2. Skip the override lookup entirely if the active rule is tenant-owned.
3. Load the current tenant's overrides for the rule; group by
   `(segmentId, field) -> value`.
4. For each segment, transiently apply the `Prefix` and `ResetPeriod`
   overrides **before** calling `EntityCodeSegment.Advance`, then
   `Advance` (mutates the SHARED sequence state — `LastSequence`,
   `LastPrefix`, `LastPeriodBucket`), then `RenderWithOverrides` for
   the remaining fields (`FixedText`, `Suffix`, `MinWidth`).
5. Restore the persisted `Prefix` / `ResetPeriod` in a `finally` block
   so the shared rule is never permanently mutated by a tenant's
   override.

Per-tenant **sequence counters** are out of scope for v1 (see §1.2
non-goal) — all tenants on a rule share the same sequence state. Only
the **template format** is overridable per tenant. Concretely: the
alphabetic prefix in an `AlphanumericSequence` segment is part of
the shared sequence state; if tenant T1 overrides `Prefix` to `"X"`
and the segment's `LastPrefix` was `"A"`, the first call sets
`LastPrefix = "X"` and persists that. Tenant T2's next call sees
`LastPrefix = "X"` and advances to the next number with that letter.
Per-tenant sequence isolation would require per-tenant `LastSequence`
columns (or a separate counter table) and is deferred to a follow-up.

> **Spec divergence:** §4.12's original sketch allowed nullable
> `EntityCodeSegmentId` for "whole-rule overrides" (e.g. overriding
> `Code` or `Name`). The implementation chose segment-level only
> (`EntityCodeSegmentId` is NOT NULL). Whole-rule metadata overrides
> are unnecessary because a tenant can create their own rule with
> their own `Code` (the existing CreateEntityCodeRule flow already
> scopes `TenantId` from the current tenant). API surface stays
> tight: `GET /api/entity-code-rules/{id}/overrides` + `PUT
> /api/entity-code-rules/{id}/overrides` (full replace-all).

> **OverrideField as int, not string:** the spec sketch used a free-form
> string column for `OverrideField` ("e.g. `FixedText`, `ResetPeriod`").
> The implementation stores the integer value of the
> `SchoolCollab.Settings.Core.Domain.OverrideField` enum, so a typo
> in a free-form string cannot corrupt the override table and the
> field set is enforced at compile time.

---

## 5. Test plan

### 5.1 Unit tests — Settings.Core

- **`GenerateNextEntityCodeHandlerTests`:**
  - Non-reset rule using AlphanumericSequence: generates `STUA01`, then `STUA02`.
  - Yearly reset: sequence resets to `A01` when `now` advances to a new year.
  - Monthly reset: sequence resets to `A01` when `now` advances to a new month.
  - Quarterly reset: sequence resets to `A01` when `now` advances to a new quarter.
  - Alphanumeric rollover: sequence progresses `A01 → A02 → … → A09 → B01 → B02`.
  - NumericSequence hits UpperLimit (e.g., `99`): throws `EntityCodeGenerationCollisionException` (no rollover for pure numeric).
  - AlphabeticSequence hits UpperLimit (e.g., `Z`): throws `EntityCodeGenerationCollisionException`.
  - AlphanumericSequence prefix exceeds max (e.g., past `Z`): throws `EntityCodeGenerationCollisionException`.
  - Concurrent generation (two calls at same time): second call gets next sequence (no duplicates).
  - Unknown rule code: throws `EntityCodeGenerationException`.
  - Fixed-only segment (e.g., stamp segment): always returns the fixed text (e.g., `STU`).
  - Mix of Fixed and sequence segments: produces correct concatenation (e.g., `STUA01`).
  - Period bucket boundary test: sequence does NOT reset when the bucket has not changed.

### 5.2 Unit tests — Students.Core (Student creation)

- `CreateStudentHandler` calls `IEntityCodeGenerator.GenerateAsync("STUDENT_CODE")` and assigns result to `StudentNumber`.
- When generation fails, `CreateStudent` throws and student is not created.
- Student creation emits `StudentCreatedEvent` with the generated `StudentNumber`.

### 5.3 Unit tests — Students.Core (Teacher creation)

- `CreateTeacherHandler` calls `IEntityCodeGenerator.GenerateAsync("STAFF_CODE")` and assigns result to `StaffNumber`.
- `Teacher.StaffNumber` property is populated on creation.

### 5.4 Unit tests — Assignments.Core (Assignment creation)

- `CreateAssignmentCommandHandler` calls `IEntityCodeGenerator.GenerateAsync("ASSIGNMENT_CODE")` and assigns result to `AssignmentNumber`.
- `Assignment.AssignmentNumber` property is populated on creation.
- When generation fails (no active `ASSIGNMENT_CODE` rule), `CreateAssignment` throws and the assignment is not created.
- Assignment creation emits `AssignmentCreatedIntegrationEvent` with the generated `AssignmentNumber`.
- Cross-bounded-context: the handler resolves the generator via the shared Settings contract (not a direct Settings.Core reference).

### 5.5 Integration tests — Settings.Api

- `GET /api/entity-code-rules` returns seed data rules with their segments.
- `GET /api/entity-code-rules/{id}` returns the rule with segment details.
- `GET /api/entity-code-rules/by-code/{code}` returns the active rule by code (404 if inactive).
- `POST /api/entity-code-rules` creates a new rule with segments (409 on duplicate code).
- `PUT /api/entity-code-rules/{id}` updates rule metadata and replaces segments.
- `DELETE /api/entity-code-rules/{id}` soft-deletes a rule.
- `POST /api/entity-code-rules/{id}/activate` activates a rule (deactivates other active rules with the same `Code`).
- `GET /api/entity-code-rules/{id}/overrides` returns the current tenant's override rows for the rule (404 if rule missing).
- `PUT /api/entity-code-rules/{id}/overrides` replaces the current tenant's full override set atomically (404 if rule missing; 400 on invalid input).

> Segments are verified via the rule-level GET/PUT above (no standalone segment sub-resource).

### 5.6 Unit tests — Phase 5 (tenant overrides)

**Domain (Settings.Core):**
- `GenerateNextWithOverrides_OverridesFixedText_AppliesAtRenderTime` — override changes the rendered output without mutating the persisted segment.
- `GenerateNextWithOverrides_OverrideUnknownSegment_IsSilentlyIgnored` — stale override pointing at a removed segment is dropped silently.
- `GenerateNextWithOverrides_OverrideMinWidthForNumericSegment_Applies` — MinWidth override takes effect at render time.
- `GenerateNextWithOverrides_OverrideMinWidthBelow1_IsIgnored` — invalid MinWidth override is dropped, persisted value stays.
- `GenerateNextWithOverrides_OverrideResetPeriodToYearly_Applies` — ResetPeriod override triggers a period reset on the next-year boundary.

**Generator service (Settings.Core):**
- `GenerateAsync_DefaultTenantSkipsOverrideLookup` — the default-sentinel tenant short-circuits the override query.
- `GenerateAsync_TenantOverridesFixedText_RendersTenantSpecificStamp` — tenant sees its FixedText override; another tenant without the override sees the shared stamp.
- `GenerateAsync_TenantOverrideOnAlphanumericPrefix_ChangesFormatOnlySequenceShared` — sequence state is shared across tenants; only the format differs (per §1.2 / §4.12).
- `GenerateAsync_TenantOverrideOnTenantOwnedRule_IsIgnored` — when the active rule is tenant-owned, no overrides are layered on top.

**Handler (Settings.Core):**
- `ReplaceEntityCodeRuleOverrides_UnknownRule_ThrowsNotFound`.
- `ReplaceEntityCodeRuleOverrides_DefaultTenant_ThrowsInvalidOperation` — default-sentinel tenant cannot manage overrides.
- `ReplaceEntityCodeRuleOverrides_ValidInput_BuildsEntitiesWithCorrectTenantIdAndCallsRepository`.
- `ReplaceEntityCodeRuleOverrides_UnknownFieldValue_ThrowsArgumentException`.
- `ReplaceEntityCodeRuleOverrides_BlankValue_ThrowsArgumentException`.
- `ReplaceEntityCodeRuleOverrides_ExistingId_UsesRehydratePath` — the repository sees a row whose id matches an existing override so the replace is treated as an update.

### 5.6 Build and test

Run:
```bash
dotnet test src/Settings/SchoolCollab.Settings.Core/SchoolCollab.Settings.Core.csproj --filter "FullyQualifiedName~GenerateNextEntityCode" -p:BuildProjectReferences=false
dotnet test src/Students/SchoolCollab.Students.Core/SchoolCollab.Students.Core.csproj --filter "FullyQualifiedName~CreateStudent" -p:BuildProjectReferences=false
dotnet build SchoolCollab.sln
```

### 5.7 Admin UI tests — Settings.Admin

- `Index.razor` lists all generation rules with code, name, active status, a segment-count badge, and a rendered **template preview** column (`EntityCodePreview.RenderFirst` computes the first code from the segments' initial state). A wired **search box** filters by Code or Name client-side (via `LandingPage.SearchText`/`SearchTextChanged`).
- `Create.razor` + `SegmentEditor` creates a rule with multiple segments; save is atomic.
- `SegmentEditor` validation: rejects duplicate indices, missing `FixedText` on Fixed segments, missing `Prefix` on AlphanumericSequence segments, `MinWidth` ≤ 0, and **`UpperLimit` format checks** (numeric sequences → positive integer; alphabetic → single A-Z letter). Reordering is via ↑/↓ buttons (drag-and-drop deferred — FluentUI Blazor has no built-in DnD for this layout).
- `SegmentEditor` reordering: drag-and-drop updates `Index` values and re-renders preview.
- "Preview" button renders the next 5 codes the current configuration would produce.
- `Edit.razor` loads an existing rule with its segments and saves changes atomically. Embeds an **`OverrideEditor.razor`** section (shown only when the rule is the shared blueprint, `TenantId == null`) that loads the current tenant's per-segment overrides, lets the admin add/edit/remove rows (Segment dropdown + Field dropdown + Value), and saves atomically via `PUT /{id}/overrides` (spec §4.12).
- Activate action deactivates other active rules for the same entity type.
- Delete action soft-deletes a rule (existing student/staff codes are preserved).

---

## 6. Suggested implementation order

> **Progress:** Phase 1 (generation engine) ✅ DONE — `EntityCodeRule`/`EntityCodeSegment` + `IEntityCodeGenerator` (contract in `SchoolCollab.Core/EntityCodes`, impl in `Settings.Core/Services`) + `EntityCodeRuleSeeder` + migration `AddEntityCodeRules` + 16 unit tests (all green). **Phase 2 (wire into Student/Teacher/Assignment creation) ✅ DONE** — `StaffNumber` on `Teacher`, `AssignmentNumber` on `Assignment`; `CreateStudentHandler` / `CreateTeacherHandler` / `CreateAssignmentCommandHandler` all call `IEntityCodeGenerator`; migrations `AddStaffNumberToTeacher` + `AddAssignmentNumberToAssignment`; `Students.Api` + `Assignments.Api` register `AddSettingsCore` so the generator resolves at runtime; 6 new handler tests (all green, no regressions — 86 Students + 78 Assignments + 365 Settings + 12 architecture). **Phase 3 (admin API for rules CRUD) ✅ DONE** — `EntityCodeRuleDto`/`EntityCodeSegmentDto`; commands `Create/Update/Delete/ActivateEntityCodeRule` (handlers with unit tests); `EntityCodeRuleRoutes` under `/api/entity-code-rules` (GET list/by-id/by-code, POST, PUT update, DELETE soft-delete, POST activate); wired into `Settings.Api/Program.cs`; 8 new handler tests (all green — Settings 373, no regressions). **Review fixes applied (Phase 2/3 audit):** fixed the `AssignmentConfiguration` bug (`assignment_number` is now `varchar(50)` via migration `AlterAssignmentNumberMaxLength`, was `text`); extended `AssignmentCreatedIntegrationEvent` to carry `AssignmentNumber` (§5.4 now satisfied); updated spec §4.4/4.5 to name `EntityCodeRuleNotFoundException` (the actual no-active-rule exception), §4.5 cross-BC note to `SchoolCollab.Core/EntityCodes`, and §4.7/§5.5 to match the id-keyed/POST-activate endpoint design actually built. **Phase 4 (admin UI) ✅ DONE** — `EntityCodeRulesApiClient` (typed HttpClient against `settings-api` with re-declared wire DTOs in Admin.Shared per the CodedValues pattern, incl. override `GetOverridesAsync`/`ReplaceOverridesAsync`); pages `Index.razor` (`/entity-code-rules`, uses shared `<LandingPage TItem=EntityCodeRuleDto>` with a wired search box + a rendered **template preview** column), `Create.razor` + `Edit.razor` (`/{Id:guid}/edit`), `SegmentEditor.razor` (interactive add/remove/reorder via ↑/↓, with **UpperLimit format validation** added), `SegmentsList.razor` (read-only render + "Preview next 5 codes" using a tiny in-component simulator), `OverrideEditor.razor` (per-tenant override CRUD on the Edit page, shown only for shared-blueprint rules); nav link + Settings DashboardItem added; ModuleServices registers the client; all 549 tests still green. **Phase 5 (tenant overrides) ✅ DONE** — `OverrideField` enum + `TenantEntityCodeRuleOverride` (strict-tenant, per-tenant/per-rule/per-segment/per-field delta); `IEntityCodeRuleOverrideRepository` + `EntityCodeRuleRepository` + DI registration; EF configuration + migration `AddTenantEntityCodeRuleOverrides`; `EntityCodeRule.GenerateNextWithOverrides` + `EntityCodeSegment.RenderWithOverrides` layer tenant overrides at render time (with transient Prefix/ResetPeriod snapshot for Advance's period-reset logic) without mutating the shared rule; `EntityCodeGenerator` skips overrides for tenant-owned active rules (with an inline invariant comment documenting why `SingleOrDefaultAsync` is safe under the global Code unique index); CQRS command `ReplaceEntityCodeRuleOverrides` + handler + DTO; API endpoints `GET /api/entity-code-rules/{id}/overrides` + `PUT /api/entity-code-rules/{id}/overrides` (replace-all); 15 new tests (5 domain override-resolution, 4 generator end-to-end with seeded overrides, 6 handler tests for ReplaceEntityCodeRuleOverrides) — all 564 tests still green. **Phase 4/5 spec-review fixes applied:** added Index template-preview column + wired search (4-A/4-B), UpperLimit client validation in SegmentEditor + Create/Edit submit handlers (4-C), `OverrideEditor.razor` + API client override methods (5-A), generator invariant comment (5-B). Drag-and-drop reordering (4-D) left as ↑/↓ buttons — FluentUI Blazor has no built-in DnD for this layout; functionally equivalent and noted in §4.11. **Edit-page refactor ✅ DONE** (plan: `docs/plans/2026-07-28-entity-code-rules-edit-refactor.md`) — fixed the icon-only `FluentButton` rendering bug (the toolbar / grid scopes enforce `min-width: 2rem; min-height: 2rem` via the `fluent-button` element selector, since `<FluentIcon Icon="@FluentIcons.X">` is a CS0426 error — the shorthand `FluentIcons.X` is a field instance, not a type name the FluentIcon source generator can resolve; use `<FluentButton Icon="@FluentIcons.X">` instead, which accepts the `Icon` instance directly); replaced the dual `SegmentEditor` + `SegmentsList` on the Edit page with a single `SegmentsGrid.razor` (view/edit-mode-per-row with `SegmentRowEditor.razor` for the edit inputs, inline validation, ↑/↓/🗑 actions, shared `EntityCodePreview.RenderNext5` simulator); moved per-tenant override add/edit from the inline `OverrideEditor` to `OverrideDialog.razor` (a `DialogShellBase<OverrideFormModel, OverrideResult>` modal — the dialog is pure-form, the parent Edit page does the load-existing → merge → `PUT /{id}/overrides` → reload so each mutation is atomic); gated the override summary card on `VisibleTenantService.IsRealTenant` (a real `tenant_id` claim — the default-sentinel `Guid.Empty` claim sees neither the override card nor the Add button, and instead sees an informational banner); registered `VisibleTenantService` in `Settings.Admin.ModuleServices.AddSettingsModule` so the Edit page can inject it; all 564 tests still green, build clean. **Edit-page layout refactor ✅ DONE** — the vertically-stacked form/grid/overrides layout made the page tall and pushed the action buttons far down. The top section is now a 2-column `.edit-layout` grid: left column has `FormRow` (the shared horizontal label+input primitive from `SchoolCollab.Admin.Shared/Components/FormRow.razor`) for Code / Name / Description / Active; right column is a sticky `.edit-layout-right` sidebar holding "Add segment" (calls `_gridRef.AddRow()` via `@ref`), "Preview next 5 codes", and the 5-code preview list (parent owns `_preview` state and calls `EntityCodePreview.RenderNext5(_model.Segments)`). Below the layout, the `SegmentsGrid` and overrides card run full-width. The grid itself is now footer-less (Add + Preview moved to the sidebar) but `AddRow()` is `public` so the sidebar can invoke it. CSS: 2-column grid with `grid-template-columns: minmax(0, 1fr) 280px`, sticky sidebar (`position: sticky; top: 1rem`), responsive collapse to single column at ≤900px. Also fixed the override-table icon-only buttons (Edit / Delete) by adding `::deep` to the `fluent-button` selectors so the scope reaches the rendered child web-component DOM (without `::deep` the rules don't match because the FluentButton is rendered in a different scope). **Preview placement follow-up ✅ DONE** — the preview result was moved OUT of the right sidebar and INTO the bottom of the left column (`.edit-preview-row` beneath the `FormRow` fields), displayed horizontally as inline `<code>` chips in a flex row with a label and a small explanatory note. The "Preview next 5 codes" button stays in the right sidebar alongside "Add segment" (both are segment actions), but the rendered output now reads directly under the rule metadata so the admin sees the format samples next to the fields they describe. CSS: `.edit-preview-row` is `display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem 1rem`, `.edit-preview-codes` is a horizontal flex list (no bullets) with each `<code>` rendered as a chip. All 564 tests still green, build clean. **Project-wide icon-button fix ✅ DONE** — investigation against FluentUI Blazor v4.14.2 source (the project's package version) revealed the root cause of the "icon-only buttons invisible / button background overshadowing icons" report: **`FluentButton` has NO `Icon` parameter in v4.x — only `IconStart` and `IconEnd`**. Every `<FluentButton Icon="@FluentIcons.X" />` in the repo was a silent no-op (the Razor compiler matches the attribute to a non-existent inherited property; no error is raised; the icon never renders; the CSS `min-width`/`min-height`/`::deep` fixes produce an empty 32×32 square). Migrated all 19 occurrences across Settings.Admin (SegmentsGrid, SegmentEditor, SegmentRowEditor, OverrideEditor, Edit, ConfigFlagDetail) from `Icon="@…"` → `IconStart="@…"`. This is the only correct parameter for icon-only buttons (the FluentButton source places `IconStart` in the DEFAULT slot when there's no `ChildContent`, so it also resolves the "extra right padding" issue from upstream #2199). Icon-via-child-`<FluentIcon>` pattern in `GuardiansTab.razor` was already correct (uses fully-qualified type-instance form). All 564 tests still green, build clean. **Per-segment edit moved to a dialog ✅ DONE** — the previous inline row-expand (clicking Edit swapped the row to `SegmentRowEditor` with a colspan cell, ✓/✕ inline actions, error bar between rows) made the table columns misalign during edit, hid other segments' state, and pushed validation messages between rows. Replaced the inline pattern with `SegmentEditDialog.razor` (a `DialogShellBase<SegmentEditFormModel, SegmentEditResult>` modal, `DialogSize.Medium`, 2-column field grid): clicking the Edit icon on a row opens a dialog showing Role / Type / FixedText / Prefix / MinWidth / Suffix / Reset / UpperLimit, with the same validation as before (FixedText required on Fixed; Prefix required on AlphanumericSequence; MinWidth ≥ 1 on numeric/alphanumeric; UpperLimit format per spec §4.11) surfaced via the shared `DialogShellFooter` error bar. The dialog is pure-form (no API calls); on success the grid copies field-by-field from the returned `SegmentEditResult.Segment` back into the original row (preserving the row reference so callers holding `SegmentFormModel` references are unaffected), re-locates the row by the snapshot `OriginalIndex` (modal so reorders can't happen mid-edit, but guarded anyway), and `NotifyChanged()` fires `SegmentsChanged`. The grid is now pure view-mode: no more `_editingIndex`/`_validationError`/`BeginEdit`/`CommitEdit` state. `SegmentRowEditor.razor` deleted (no longer referenced). `AddRow()` (sidebar "Add segment") still appends silently — the user clicks Edit on the new row to fill it in, mirroring the established override-card "Add then edit" flow. All 564 tests still green, build clean. **Preview simulator parity fix ✅ DONE** — investigation revealed the "preview codes only show the numeric bits of the alphanumeric setup" report was caused by a buggy `SimSegment` private class that mirrored the production `EntityCodeSegment.Advance` logic but had drifted: AlphanumericSequence previews rendered `_alpha + _seq` where `_alpha` started as `""` and `_seq` was rendered BEFORE `Advance` ran, so the first code showed `ASG00` instead of `ASGA01`. The same buggy `SimSegment` lived in TWO places (`EntityCodePreview.RenderNext5` for the Edit/Create pages AND a private copy inside `SegmentsList.razor` for the Create page). Replaced both with code that builds real `EntityCodeSegment` instances via the production factories (`Fixed`/`Sequence`) and calls the production `Advance(now)` / `GenerateNext(now)` directly — the preview IS the production code by construction, no separate simulator to drift. Graceful handling: null/empty segments returns `[]`; factory exceptions (e.g. mid-edit `MinWidth=0`) return `[]` rather than crashing the page; `EntityCodeGenerationCollisionException` stops the run early (e.g. UpperLimit=`"01"` returns 5 codes from `A01..E01`); period resets are honored via the production path (ResetPeriod.None → no reset, others → reset to fresh state). Added `EntityCodePreviewTests.cs` with 14 tests covering: the user-reported `ASGA01..ASGA05` case, the default student template, preview-matches-production invariant, `AlphabeticSequence` (A..E), `NumericSequence` (`X-001..X-005` with MinWidth=3 padding), Suffix inclusion, Prefix-as-initial-letter, null/empty input, invalid input (MinWidth=0), upper-limit rollover, and the Index page's `RenderFirst` for both empty and `ASGA01` cases. Added role-aware labels to the `SegmentEditDialog`: when `Role=stamp` + `Type=Fixed` the FixedText field is labeled "Stamp text *" (placeholder "STU, STF, ASG, …"); when `Role=stamp` + `Type=AlphanumericSequence` the Prefix field is labeled "Stamp initial letter *" (placeholder "A"). The fields were already enabled for these combinations; the label change makes the enablement obvious to the admin. **All 578 tests green (564 + 14 new preview tests), build clean.**

### Phase 1 — Generation engine (Settings BC, self-contained) ✅

1. **Domain entities + configuration** — add `EntityCodeRule`, `EntityCodeSegment`, `SegmentType` enum, and EF Core configuration. Add `StaffNumber` to `Teacher` and `AssignmentNumber` to `Assignment` (separate EF migration in the Assignments bounded context).
2. **Command + handler** — implement `GenerateNextEntityCode` command and handler with per-segment iteration, period-bucket logic, sequence increment, and alphanumeric rollover.
3. **Service interface** — define `IEntityCodeGenerator`/`EntityCodeGenerator` and register it in DI; expose via shared Settings contract for cross-bounded-context use.
4. **Student creation integration** — wire generator into `CreateStudentHandler`, assign `StudentNumber`.
5. **Teacher creation integration** — wire generator into `CreateTeacherHandler`, assign `StaffNumber`, update `Teacher.Create`.
6. **Assignment creation integration** — wire generator into `CreateAssignmentCommandHandler`, assign `AssignmentNumber`, update `Assignment.Create`.
7. **API endpoints + CQRS commands** — add `/api/entity-code-rules` routes and CRUD/activate commands for rules and segments (§4.7, §4.8).
8. **Admin UI** — implement the Generation Rules pages and `SegmentEditor` (§4.11).
9. **Tenant overrides** — implement `TenantEntityCodeRuleOverride` (§4.12).
10. **Seed data** — add `EntityCodeRuleSeeder` with `STUDENT_CODE`, `STAFF_CODE`, and `ASSIGNMENT_CODE` rules + segments.
11. **Tests** — unit and integration tests per §5.
12. **Full build + test pass.**

Steps 1–4 are the minimum viable (students get auto-generated `StudentNumber`). Steps 5–6 complete staff + assignment generation. Steps 7–9 deliver the API, admin UI, and tenant overrides required for v1.

---

## 7. Open questions

1. **`StaffNumber` vs `TeacherNumber`**: the user said "students and teachers" — `Teacher` entity maps to "staff" in v1. Should the field be `StaffNumber` or `TeacherNumber` or `EmployeeNumber`? **Recommend `StaffNumber`** as the broader, more accurate name (staff includes teachers, administrators, etc.). Confirm at implementation.
2. **Segment-level vs rule-level `MaxPrefix`**: the alphabetic prefix rollover currently assumes `Z` as the max prefix. Should max prefix be configurable per segment (e.g., stop at `M` for some templates)? **Recommend `Z` as default with a future `MaxPrefix` property on `EntityCodeSegment` for customization.**
3. **Code collision handling**: if two students are created at the same instant (same rule, same segment, same sequence), the pessimistic lock retry handles this. If the retry exhausts, the command throws a `EntityCodeGenerationException`. **Is a manual override / collision resolution needed?** Not in v1.
4. **Teacher `StaffUserId` linkage**: should the generated staff code be linked back to the auth user? The existing `StaffUserId` is a separate auth concept. The `StaffNumber` is the operational identifier. These are independent.
5. **Period bucket granularity for `Quarterly`**: does quarter start on Jan 1 / Apr 1 / Jul 1 / Oct 1 (calendar quarters) or on the school's fiscal year? **Recommend calendar quarters** for v1; fiscal year customization is a follow-up.
6. **Admin UI scope in v1**: the admin UI for generation rules is included in v1 (§4.11), with full segment CRUD via the `SegmentEditor`. Confirm that the live preview (next-5-codes on save) is sufficient, or whether real-time per-keystroke preview is needed (currently a non-goal, §1.2).
7. **Per-tenant segment overrides**: tenant overrides should allow overriding any segment property per tenant. How should the override model work? **Recommend a `TenantEntityCodeSegmentOverride` table that stores only the delta (overridden fields) per tenant per segment.
8. **`AssignmentNumber` vs `AssignmentCode`**: should the generated assignment identifier field be `AssignmentNumber` (consistent with `StudentNumber`/`StaffNumber`) or `AssignmentCode`? **Recommend `AssignmentNumber`** for naming consistency across the three generated fields. Confirm at implementation.
9. **Assignment stamp**: the default stamp is `ASG`. Should it be `ASM` (assignment) or `ASG` (assignment)? **Recommend `ASG`** (three-letter convention matching `STU`/`STF`). Confirm at implementation.
10. **Assignment generation volume**: assignments are created by teachers and may have higher creation volume than students/staff. The shared per-rule sequence counter (§1.2 non-goal) is the concurrency bottleneck. Confirm the pessimistic-lock + retry design (§4.3) is adequate for assignment throughput in v1; a dedicated sequence table is the follow-up if needed.

---

## 8. Risks

- **`Student.StudentNumber` is currently a plain string** — auto-generation changes its semantics from "user-provided" to "system-generated." Existing students in the DB will have `null`/empty `StudentNumber` if they were created before this feature. The handler should handle this gracefully (generate a code for legacy students on demand, or require manual assignment on first login).
- **`Teacher` has no number field today** — adding `StaffNumber` is a schema change requiring a migration. Confirm the migration strategy (EF Core migration). The same applies to `Assignment.AssignmentNumber` — a separate migration in the Assignments bounded context (different `DbContext`).
- **Concurrency under load**: a single `Update` on the generation rule row is a bottleneck for high-throughput student/staff/assignment creation. The pessimistic lock + retry pattern mitigates this for v1; a sequence table would be needed if throughput exceeds ~50 req/s on a single rule. Assignments may drive higher creation volume than students/staff — monitor the `ASSIGNMENT_CODE` rule under load.
- **Seed data ordering**: `EntityCodeRule` rows must be seeded before any student/staff/assignment creation can work. If the seed runs after the DB is populated, the API endpoints must handle the case where no rule exists (graceful error).
- **Cross-bounded-context coupling**: `IEntityCodeGenerator` is consumed by both the Students and Assignments bounded contexts. A change to the generator contract must be coordinated across all three contexts (Settings, Students, Assignments). Version the shared contract (`SchoolCollab.Settings.Contracts`) to avoid breaking consumers.
- **Testing period-based resets**: unit tests that mock `DateTimeOffset.UtcNow` need careful setup to test boundary conditions (year rollover, month rollover, quarter boundary). Use a `IClock` abstraction or pass `DateTimeOffset` as a parameter to the handler for testability.
