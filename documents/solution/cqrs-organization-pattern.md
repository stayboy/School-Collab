# CQRS Command and Query Organization Pattern

This document defines the standard pattern for organizing CQRS **commands** and
**queries** inside the `*.Core` projects of the SchoolCollab solution. The goal
is to keep each bounded context's command/query surface area maintainable as
the number of operations grows, while keeping the source layout aligned with
the HTTP endpoint specialties that consume them.

It is **mandatory** for every bounded-context `*.Core` project under `src/`
that defines commands and queries.

It pairs with [`endpoint-organization-pattern.md`](./endpoint-organization-pattern.md)
— every specialty in an API's `Endpoints/` folder must have a matching
specialty in the corresponding `*.Core` project.

CQRS **abstractions** (`ICommand`, `IQuery`, `ICommandHandler<,>`,
`IQueryHandler<,>`) live in the shared kernel, not in any `*.Core` project.
See [`shared-kernel-extraction-pattern.md`](./shared-kernel-extraction-pattern.md)
for the rule that decides when a type belongs in `SchoolCollab.Core` and
when it stays local.

## 1. Scope

This rule applies to every CQRS-bearing `*.Core` project in `src/`, including:

- `SchoolCollab.Students.Core`
- `SchoolCollab.Assignments.Core`
- `SchoolCollab.CodedValues.Core`
- Any future bounded-context core project (e.g. `SchoolCollab.Attendance.Core`,
  `SchoolCollab.Timetables.Core`).

It does **not** apply to:

- `SchoolCollab.Core` (the shared kernel) — it contains only cross-cutting
  primitives, not commands/queries.
- `SchoolCollab.Students.Contracts` and equivalent contract assemblies — they
  hold DTOs and event types, not command/query definitions.
- `*.Admin`, `*.Worker`, `*.Api`, `*.AppHost`, `*.MigrationService` projects.

## 2. Required Folder Structure

Every `*.Core` project that defines commands and queries must contain a
top-level `CQRS/` folder, with one **specialty** sub-folder per HTTP
endpoint specialty, with a `Commands/` and a `Queries/` subgroup inside
each:

```
<Domain>.Core/
├── CQRS/                                 ← specialty-grouped commands/queries
│   ├── <Specialty1>/                     ← mirrors an API Endpoints/<Specialty>Routes.cs
│   │   ├── Commands/
│   │   │   └── <VerbEntity>/
│   │   │       ├── <VerbEntity>.cs         ← record + ICommand marker
│   │   │       └── <VerbEntity>Handler.cs  ← ICommandHandler<,> implementation
│   │   └── Queries/
│   │       └── <VerbEntity>/
│   │           ├── <VerbEntity>.cs         ← record + IQuery<TResult> marker
│   │           └── <VerbEntity>Handler.cs  ← IQueryHandler<,TResult> implementation
│   └── <Specialty2>/
│       ├── Commands/
│       └── Queries/
├── Domain/
├── Data/
├── DTOs/
├── Messaging/
├── Migrations/
├── Extensions.cs                         ← Add<Domain>Core(...) DI registration
└── SchoolCollab.<Domain>.Core.csproj
```

The shared CQRS *abstractions* (`ICommand`, `IQuery`,
`ICommandHandler<>`, `IQueryHandler<,>`) live in
`SchoolCollab.Core/CQRS/` — see
[`shared-kernel-extraction-pattern.md`](./shared-kernel-extraction-pattern.md).
They are not in this `<Domain>.Core/CQRS/` folder.

### Folder rules

- **One `CQRS/` folder per project, at the project root.** All command and
  query files for a given domain live under `CQRS/`, grouped by specialty
  in sub-folders. The `CQRS/` folder name is reserved for the
  specialty-grouped command/query layout — it is **not** used for
  per-operation folders at the project root, and it does **not** hold the
  shared CQRS abstractions.
- **One specialty sub-folder per HTTP endpoint specialty.** Each specialty
  folder lives at `<Domain>.Core/CQRS/<Specialty>/`, mirroring the
  matching `<Specialty>Routes.cs` in the API's `Endpoints/` folder.
- **`Commands/` and `Queries/` are subgroups inside each specialty
  folder.** They never appear at the project root or directly inside
  `CQRS/` — always nested one level deeper.
- **One folder per command/query.** Each command/query gets its own folder
  named with the full type name (e.g. `CreateStudentCommand/` for the
  `CreateStudentCommand` record, `EnrollStudent/` for `EnrollStudent`,
  `ListEnrollmentsByPeriod/` for `ListEnrollmentsByPeriod`). The folder
  name is **always** the exact type name — including any `Command` /
  `Query` suffix that the project uses in its naming convention.
- **Each folder contains exactly two files:** `<TypeName>.cs` (the record
  and its marker) and `<TypeName>Handler.cs` (the handler). Do not add
  extra files (helpers, exceptions, builders) to the folder — put them
  in `Domain/` or a sibling folder instead.
- **The shared CQRS abstractions live in `SchoolCollab.Core/CQRS/`.** See
  [`shared-kernel-extraction-pattern.md`](./shared-kernel-extraction-pattern.md)
  for the rule on extracting types into the shared kernel.

## 3. Naming

| Element | Convention | Example |
|---|---|---|
| Specialty folder | `<Specialty>` (PascalCase, matches the API route specialty) | `Students`, `GradeLevels`, `Subjects` |
| Command/query folder | `<Verb><Entity>` (PascalCase) | `CreateStudent`, `EnrollStudent`, `ListEnrollmentsByPeriod` |
| Record file | `<Verb><Entity>.cs` | `CreateStudent.cs` |
| Handler file | `<Verb><Entity>Handler.cs` | `CreateStudentHandler.cs` |
| Record class | `<Verb><Entity>` | `public sealed record CreateStudent(...) : ICommand;` |
| Handler class | `<Verb><Entity>Handler` | `public sealed class CreateStudentHandler(...) : ICommandHandler<CreateStudent, Guid>` |
| Specialty namespace | `SchoolCollab.<Domain>.Core.<Specialty>.Commands` and `.Queries` | `SchoolCollab.Students.Core.Subjects.Commands` |
| Per-operation namespace | `SchoolCollab.<Domain>.Core.<Specialty>.Commands.<VerbEntity>` | `SchoolCollab.Students.Core.Subjects.Commands.CreateSubject` |

All files in the same `<Specialty>/Commands/<VerbEntity>/` folder share the
record's namespace — i.e. the handler and the record have the **same**
namespace, not different ones. This keeps `using` imports short: a handler
can reference its own record without any extra `using` directive.

## 4. Specialty ↔ Endpoint Alignment

Every specialty in `*.Core` must correspond to exactly one
`<Specialty>Routes.cs` file in the matching API project's
`Endpoints/` folder:

| API endpoint file | Matching Core specialty |
|---|---|
| `StudentRoutes.cs` | `Students/` |
| `GradeLevelRoutes.cs` | `GradeLevels/` |
| `SubjectRoutes.cs` | `Subjects/` |
| `PeriodRoutes.cs` | `Periods/` |
| `EnrollmentRoutes.cs` | `Enrollments/` |
| `GradeSubjectAssignmentRoutes.cs` | `GradeSubjectAssignments/` |
| `StudentSubjectAssignmentRoutes.cs` | `StudentSubjectAssignments/` |

If a new command/query does not fit any existing specialty, **add a new
specialty folder to both `*.Core` and the API at the same time** — never
create a one-off operation that lives outside the specialty groupings.

## 5. Required Folder Structure (Assignments, CodedValues specifics)

`SchoolCollab.Assignments.Core` and `SchoolCollab.CodedValues.Core` are also
covered by this rule. They follow the same specialty-grouped layout, with
specialties derived from their existing API endpoint files:

### Assignments
- Specialty: `Assignments/`
  - Commands: `ListAssignmentsQuery` lives in `Queries/` (see note), `CreateAssignmentCommand`, `UpdateAssignmentCommand`, `DeleteAssignmentCommand`, `PublishAssignmentCommand`, `UnpublishAssignmentCommand`, `CloseAssignmentCommand`, `ReviewAssignmentCommand`.
  - Queries: `ListAssignmentsQuery`, `GetAssignmentByIdQuery`.

### CodedValues
- Specialty: `CodedValues/`
  - Commands: `CreateCodedValue`, `DeleteCodedValue`, `RecoverCodedValue`,
    `UpdateCodedValue`, `DisableCodedValue`, `EnableCodedValue`,
    `BulkCreateCodedValues`, `SetCodedValueAttribute`,
    `RemoveCodedValueAttribute`, `SetCodedValueAttributeDefinition`,
    `RemoveCodedValueAttributeDefinition`.
  - Queries: `ListRootCodedValues`, `SearchCodedValues`, `GetCodedValueById`,
    `GetCodedValueByCode`, `GetCodedValuesByIds`, `GetCodedValuesByParent`,
    `ListDeletedCodedValues` (if added), etc.

If a CodedValues sub-domain grows (e.g. attribute definitions acquire their
own commands), split it into its own specialty folder in lockstep with
the API's `Endpoints/` split.

## 6. Moving From the Legacy Layout

The legacy layout had two top-level folders `Commands/` and `Queries/`,
each containing one folder per command/query. To migrate an existing
project:

1. **Inventory** every command/query in the legacy `Commands/` and
   `Queries/` folders. Decide which HTTP endpoint specialty each one
   belongs to.
2. **Create the specialty folders** under the project root:
   `mkdir <Specialty>/Commands <Specialty>/Queries`.
3. **Move** the legacy command/query folders into the matching specialty's
   `Commands/` or `Queries/` subfolder:
   `mv Commands/CreateStudent Students/Commands/CreateStudent`.
4. **Rewrite namespaces** in every record and handler file from
   `SchoolCollab.<Domain>.Core.Commands.<Name>` to
   `SchoolCollab.<Domain>.Core.<Specialty>.Commands.<Name>`, and similarly
   for queries. The handler namespace matches the record namespace (not
   the handler's filename).
5. **Update `using` directives** in the API project and any other consumer
   (Worker, Admin, tests) to use the new namespaces.
6. **Delete** the now-empty `Commands/` and `Queries/` top-level folders.
7. **Build the whole solution** and run all unit/integration tests for the
   affected projects. Endpoint URIs, request shapes, and DI behaviour must
   not change — only file paths and namespaces move.

## 7. Audit Checklist

When reviewing a new `*.Core` project or a PR that adds a command/query, verify:

- [ ] The new command/query lives under `<Specialty>/Commands/<VerbEntity>/`
      or `<Specialty>/Queries/<VerbEntity>/` (specialty group at the project
      root level).
- [ ] There is no top-level `Commands/` or `Queries/` folder at the project
      root.
- [ ] The matching API project has a corresponding `<Specialty>Routes.cs`
      file in its `Endpoints/` folder. If not, add one in the same PR.
- [ ] The specialty folder is mentioned by name in
      `endpoint-organization-pattern.md`'s alignment table (or a new entry
      is added).
- [ ] The record and handler share a namespace ending in
      `.<VerbEntity>`, not separate namespaces.
- [ ] The record file is named `<VerbEntity>.cs` and the handler file is
      named `<VerbEntity>Handler.cs`. No extra files in the folder.
- [ ] `dotnet build` of the full solution succeeds with 0 errors and 0 new
      warnings.
- [ ] All unit and integration tests for the affected projects still pass.

## 8. Worked Example: Students Core

The canonical reference implementation is `SchoolCollab.Students.Core`. After
applying this pattern the layout is:

```
src/Students/SchoolCollab.Students.Core/
├── CQRS/                       ← ICommand, IQuery, ICommandHandler, IQueryHandler
├── Domain/
├── Data/
├── DTOs/
├── Messaging/
├── Migrations/
├── Extensions.cs
├── SchoolCollab.Students.Core.csproj
│
├── Students/
│   ├── Commands/   (CreateStudent, UpdateStudent, DeleteStudent, RecoverStudent)
│   └── Queries/    (ListStudents, GetStudentById, GetStudentByStudentNumber, ListDeletedStudents)
├── GradeLevels/
│   ├── Commands/   (CreateGradeLevel, UpdateGradeLevel)
│   └── Queries/    (ListGradeLevels, GetGradeLevelById)
├── Subjects/
│   ├── Commands/   (CreateSubject, UpdateSubject, + 7 strand/lesson commands)
│   └── Queries/    (ListSubjects, GetSubjectById, GetSubjectByCode, + 2 list queries)
├── Periods/
│   ├── Commands/   (CreatePeriod, UpdatePeriod, ActivatePeriod, CompletePeriod)
│   └── Queries/    (ListPeriods, GetPeriodById)
├── Enrollments/
│   ├── Commands/   (EnrollStudent, TransferStudent, WithdrawStudent)
│   └── Queries/    (ListEnrollmentsByStudent, ListEnrollmentsByPeriod)
├── GradeSubjectAssignments/
│   ├── Commands/   (AssignGradeSubject, UpdateGradeSubjectTags, RemoveGradeSubject)
│   └── Queries/    (ListGradeSubjectAssignmentsByPeriod, ListGradeSubjectAssignmentsByGradeLevel)
└── StudentSubjectAssignments/
    ├── Commands/   (AssignStudentSubject, RemoveStudentSubject)
    └── Queries/    (ListStudentSubjectAssignmentsByStudent, ListStudentSubjectAssignmentsByPeriod)
```
