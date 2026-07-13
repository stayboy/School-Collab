# Student Guardian — Implementation Plan (draft)

> Status: draft, not yet implemented. Companion to the guardian feature branch
> `feature/student-guardians`. Follows the existing `documents/solution/*`
> design-doc style and the CQRS organization pattern in
> `documents/solution/cqrs-organization-pattern.md`.

## 1. Goal

Allow a student to have one or more **guardians** who can:

- **Receive** assignments that are published to the student (delivery / CC), and
- **Submit** assignments **on behalf of** the student (when they are a *primary*
  contact), while a *CC* guardian may receive but **not** perform actions such
  as submission.

A guardian carries: **name** (title, first, last, optional display name for
email), **relationship** to the student, **contact** (phone), **email**,
**address**, and an **order of preference** (a student has multiple guardians,
ranked). Guardian **names are auditable entities that change** — every name
change must be retained as history, not overwritten.

When an assignment is published, guardians of the targeted students are linked
to that assignment as recipients (primary or CC), and their access role
controls which homework actions they may perform.

## 2. Key decisions (do not relitigate)

- **Guardian is owned by the Students bounded context** (it is student-scoped
  data). The Assignments context references guardians **by `Guid` only** — never
  via an EF navigation across DbContexts (the two contexts have separate
  DbContextes / databases).
- **Name audit = append-only history table** on top of the existing
  `IAuditableEntity` (created/updated tracking) that every domain entity
  already implements. A name edit appends a `GuardianNameHistory` row rather than
  mutating history.
- **All entities are tenant-scoped** (`ITenantEntity`) and follow the repo's
  `ModuleDbContext` + `TenantEntityTypeConfigurationBase<T>` conventions
  (tenant query filter + soft-delete filter are applied by the base).
- **Preference order lives on the link entity** (`StudentGuardian`), not on the
  guardian, because the same guardian can be linked to multiple students at
  different ranks.
- **Access role (Primary/CC) is captured at two points**: on the
  `StudentGuardian` link (authoritative) and mirrored onto the
  `AssignmentRecipient` created at publish time (so submission checks don't have
  to re-resolve the link).

## 3. Project layout

```
src/Students/SchoolCollab.Students.Core/
  Domain/
    Guardians/
      Guardian.cs                 # : ITenantEntity, IEntity, IAuditableEntity, ISoftDeletableEntity, IHasRowVersion
      GuardianNameHistory.cs      # : IEntity, IAuditableEntity (append-only, NO soft-delete)
      StudentGuardian.cs          # link : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
      GuardianRole.cs             # enum Primary=0, CC=1
    Enums/ (or Domain/)           # Relationship sourced from CodedValues
  CQRS/Guardians/
    Commands/CreateGuardian/ UpdateGuardian/ DeleteGuardian/
              LinkGuardianToStudent/ UpdateGuardianLink/ UnlinkGuardian/
    Queries/GetGuardianById/ ListGuardians/ ListGuardiansByStudent/ GetGuardianNameHistory/
  Data/Configurations/ GuardianConfiguration.cs, GuardianNameHistoryConfiguration.cs, StudentGuardianConfiguration.cs
  DTOs/ GuardianDto.cs, StudentGuardianDto.cs, GuardianNameHistoryDto.cs

src/Assignments/SchoolCollab.Assignments.Core/
  Domain/AssignmentRecipient.cs   # : ITenantEntity, IEntity, IAuditableEntity
  Domain/RecipientType.cs         # enum Student=0, Guardian=1
  Domain/RecipientChannel.cs      # enum Email=0 (extensible)
  CQRS/Assignments/Commands/PublishAssignmentCommand/  # extended to resolve + link guardians
       (or a dedicated LinkGuardiansToAssignmentCommand invoked by publish)
  CQRS/Assignments/Queries/ListAssignmentRecipients/
  Data/Configurations/AssignmentRecipientConfiguration.cs
  Services/IGuardianResolver.cs + GuardiansApiClient.cs   # HTTP client to Students.Api

src/Students/SchoolCollab.Students.Api/Endpoints/  GuardianRoutes.cs, StudentGuardianRoutes.cs
src/Assignments/SchoolCollab.Assignments.Api/Endpoints/ AssignmentRecipientRoutes.cs

src/Students/SchoolCollab.Students.Admin/Components/Pages/Guardians/   (list, create, edit)
src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/.../GuardiansTab  (link + reorder + role)
src/Assignments/SchoolCollab.Assignments.Admin/.../AssignmentRecipients  (read-only recipient view)
```

## 4. Domain model

### 4.1 `Guardian` : `ITenantEntity, IEntity, IAuditableEntity, ISoftDeletableEntity, IHasRowVersion`

```
Guid  Id
Guid  TenantId
string? Title                 # Mr/Mrs/Dr — free text or coded value (see §17)
string  FirstName
string  LastName
string? DisplayName           # optional, used as the "from/display" name in email
string  Email                 # unique-ish per tenant; receive target
string? ContactPhone
string? AddressLine1
string? AddressLine2
string? City
string? State
string? PostalCode
string? Country
bool   IsDeleted
DateTimeOffset? DeletedAt
uint   RowVersion
DateTimeOffset CreatedAt
DateTimeOffset UpdatedAt
```
Name edits go through `UpdateGuardian` which, when `Title/FirstName/LastName/
DisplayName` change, appends a `GuardianNameHistory` row (§4.2) and updates the
current columns.

### 4.2 `GuardianNameHistory` : `IEntity, IAuditableEntity` (append-only, NO soft-delete)

```
Guid   Id
Guid   TenantId
Guid   GuardianId
string? Title
string  FirstName
string  LastName
string? DisplayName
Guid?   ChangedBy            # actor (teacher/admin user id) when available
string? ChangeReason
DateTimeOffset ChangedAt     # from IAuditableEntity.CreatedAt
```
Append-only: never updated or soft-deleted. This is the audit trail for "names
are auditable entities that change."

### 4.3 `StudentGuardian` (link) : `ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion`

```
Guid        Id
Guid        TenantId
Guid        StudentId
Guid        GuardianId
Guid?       RelationshipCodedValueId   # from CodedValues (see §17)
int         PreferenceOrder            # 1 = highest preference for this student
GuardianRole Role                     # Primary=0, CC=1  (drives access)
bool        IsEmergencyContact
uint        RowVersion
DateTimeOffset CreatedAt
DateTimeOffset UpdatedAt
```
`PreferenceOrder` is unique per `(TenantId, StudentId)`. Reordering swaps orders.

### 4.4 `AssignmentRecipient` (in Assignments.Core) : `ITenantEntity, IEntity, IAuditableEntity`

```
Guid           Id
Guid           TenantId
Guid           AssignmentId
Guid           StudentId               # cross-BC reference (no navigation)
Guid?          GuardianId              # cross-BC reference; null = direct student recipient
RecipientType  RecipientType           # Student=0, Guardian=1
GuardianRole   Role                    # mirrored from StudentGuardian.Role at publish
RecipientChannel Channel               # Email=0
DateTimeOffset? DeliveredAt
DateTimeOffset? OpenedAt
```
Created when an assignment is published and the student's guardians are
resolved. CC guardians are still linked (so they receive) but `Role == CC`
blocks submission.

### 4.5 Enums
- `GuardianRole { Primary = 0, CC = 1 }`
- `RecipientType { Student = 0, Guardian = 1 }`
- `RecipientChannel { Email = 0 }` (extensible)

## 5. Storage (Postgres, snake_case)

| Table | Notes |
|---|---|
| `guardians` | tenant-scoped; `email` indexed per tenant |
| `guardian_name_history` | append-only; index `(guardian_id, created_at)` |
| `student_guardians` | unique `(tenant_id, student_id, preference_order)`; index `(guardian_id)` |
| `assignment_recipients` (assignments db) | index `(assignment_id, guardian_id)`, `(assignment_id, student_id)` |

All tables carry `tenant_id` and `created_at`/`updated_at` per `IAuditableEntity`;
`guardians`/`student_guardians` carry soft-delete + `row_version` for
concurrency. Use the existing `TenantEntityTypeConfigurationBase<T>` base so
tenant + soft-delete query filters are applied automatically.

## 6. Cross-bounded-context integration (the load-bearing part)

The Assignments context cannot navigate to Students entities. Two viable
patterns:

- **(A) Synchronous resolve at publish (recommended for v1).** Extend
  `PublishAssignmentCommandHandler` (or add `LinkGuardiansToAssignmentCommand`
  invoked by it). After the audience students are known (by grade/group/selected),
  call `IGuardianResolver.ResolveGuardiansAsync(studentIds, ct)` implemented by
  `GuardiansApiClient` — an `HttpClient` to `Students.Api`
  (`GET /students/{id}/guardians` returning guardian id + `GuardianRole`). For
  each guardian, insert an `AssignmentRecipient` with `Role` mirrored from
  `StudentGuardian.Role`. CC guardians are inserted too (they receive) but their
  `Role == CC` blocks submission downstream.
- **(B) Event-driven read-model (future).** `AssignmentPublishedEvent` (already
  emitted via the shared outbox) is consumed by a Students-side projector that
  writes guardian recipients into a local Assignments read-model. More
  decoupled, more moving parts — defer to v1.1.

**Unpublish / republish** removes or rebuilds the `AssignmentRecipient` rows for
that assignment.

**Access enforcement:** submission (and other mutating homework actions) in
Assignments.Api is gated by an authorization requirement
(`GuardianSubmissionRequirement`) that asserts an `AssignmentRecipient` exists
for `(AssignmentId, GuardianId)` with `Role == Primary`. CC recipients get a
read-only / "cannot submit" experience. For v1 the role is **data-driven**; the
mechanism by which a guardian authenticates is a follow-up (§17, §18).

## 7. CQRS — Students.Core

Commands (records implementing `ICommand`; handlers `ICommandHandler<T>` with
`HandleAsync`, using repositories + `IIntegrationEventPublisher` (outbox) +
`HybridCache` + `ITenantProvider`, mirroring `CreateStudentHandler`):

- `CreateGuardian(Title?, FirstName, LastName, DisplayName?, Email, ContactPhone?, Address*, RelationshipCodedValueId?)`
- `UpdateGuardian(...)` — if name fields changed, append `GuardianNameHistory`.
- `DeleteGuardian(Guid)` — soft delete; decide link handling (§17).
- `LinkGuardianToStudent(StudentId, GuardianId, RelationshipCodedValueId?, PreferenceOrder, Role, IsEmergencyContact?)`
- `UpdateGuardianLink(...)` — reorder preference, change `Role`/`Relationship`.
- `UnlinkGuardian(StudentId, GuardianId)`

Queries: `GetGuardianById`, `ListGuardians` (tenant), `ListGuardiansByStudent`
(ordered by `PreferenceOrder`), `GetGuardianNameHistory`.

## 8. CQRS — Assignments.Core

- Extend `PublishAssignmentCommand` (or add `LinkGuardiansToAssignmentCommand`)
  to resolve guardians via `IGuardianResolver` and create `AssignmentRecipient`s.
- `UnpublishAssignmentCommand` rebuilds/removes recipients.
- Query: `ListAssignmentRecipients(assignmentId)`.

## 9. API endpoints

**Students.Api**
- `POST   /students/guardians` — create
- `GET    /students/guardians`, `GET /students/guardians/{id}`
- `PUT    /students/guardians/{id}`
- `DELETE /students/guardians/{id}`
- `POST   /students/{studentId}/guardians` — link (sets relationship/order/role)
- `PUT    /students/{studentId}/guardians/{guardianId}` — update link
- `DELETE /students/{studentId}/guardians/{guardianId}` — unlink
- `GET    /students/{studentId}/guardians` — ordered by preference (**used by the Assignments resolver**)
- `GET    /students/guardians/{id}/name-history`

**Assignments.Api**
- `GET  /assignments/{id}/recipients`
- Submission/action endpoints gated by `GuardianSubmissionRequirement`
  (Primary-only).

## 10. Access control (CC vs Primary)

- `GuardianRole` on `StudentGuardian` is authoritative; it is mirrored onto
  `AssignmentRecipient.Role` at publish.
- Authorization policy in Assignments.Api: a guardian acting on an assignment
  must have a `AssignmentRecipient` row with `Role == Primary` for that
  assignment to submit. CC recipients receive/view but cannot submit.
- v1 is **data-model + linkage + role flags**; actual guardian authentication
  (how a guardian presents identity) is a follow-up (§17, §18).

## 11. Admin UI — Students.Admin

- **Guardians list / create / edit** — reuse the repo's `FormRow` layout primitive
  and `CodedValueDropdown` for `Relationship`. Name fields (Title/First/Last/
  DisplayName) grouped; email/phone/address sized to expected length (per the
  `StudentFormFields` pattern).
- **Student → Guardians tab** — list linked guardians ordered by preference with
  up/down (or drag) reorder, relationship + role (Primary/CC) editors, add/remove.
- **Guardian name history** — read-only timeline of `GuardianNameHistory`.

## 12. Admin UI — Assignments.Admin

- **Assignment detail → Recipients** — students + guardians with Primary/CC
  badge; CC rows marked "receives only."
- **Submission UI** — respects `Role`: CC sees read-only / "cannot submit."

## 13. Migrations

- Students.Core: new migration `AddGuardians`
  (`guardians`, `guardian_name_history`, `student_guardians`).
- Assignments.Core: new migration `AddAssignmentRecipients`.
- `SchoolCollab.MigrationService` already runs per-module migrations; no new
  wiring beyond registering the new DbContexts' migrators (consistent with the
  existing per-module pattern).

## 14. Audit

- `GuardianNameHistory` — append-only audit of every name change.
- `IAuditableEntity` (`CreatedAt`/`UpdatedAt`/by) on `Guardian`,
  `StudentGuardian`, `AssignmentRecipient`.
- Optional (v1.1): emit `GuardianNameChanged` integration event to the shared
  outbox for downstream notification/audit consumers.

## 15. Testing

- **Unit (SchoolCollab.Admin.Tests.Unit / core test projects):** create guardian;
  name update appends exactly one `GuardianNameHistory`; link sets
  `PreferenceOrder` + `Role`; reorder swaps orders; publish creates
  `AssignmentRecipient`s for each guardian with correct `Role` (CC included);
  CC guardian is rejected by `GuardianSubmissionRequirement`.
- **ArchitectureTests:** folder/pattern compliance (CQRS org pattern).
- **Playwright (SchoolCollab.*.Tests.Playwright):** guardian CRUD; link + reorder
  + role on student; assignment recipient view; CC cannot submit.

## 16. Implementation order

1. Students.Core domain entities + EF configurations + `StudentsDbContext` DbSets.
2. Students.Core migration `AddGuardians`.
3. Students.Core CQRS (commands/queries) + handlers.
4. Students.Api guardian + student-guardian endpoints (+ `name-history`).
5. Assignments.Core `AssignmentRecipient` + config + migration.
6. Assignments.Core `IGuardianResolver`/`GuardiansApiClient` + publish integration.
7. Assignments.Api recipients endpoint + `GuardianSubmissionRequirement`.
8. Students.Admin guardian pages + student-guardian link/reorder/role UI + name history.
9. Assignments.Admin recipient view + role-aware submission UI.
10. MigrationService wiring + full build/test.

## 17. Open questions / decisions needed

- **Relationship:** CodedValue (add `Relationships` to `CodedValueParent`, reuse
  the CodedValues system + `CodedValueDropdown`) **vs** free string. Recommend
  CodedValue for consistency with Gender/Subject/Grade.
- **Address:** structured columns (line1/line2/city/state/postal/country) **vs**
  single free-text. Recommend structured for validation/formatting.
- **Title:** free text **vs** coded value. Recommend free text (small, low
  churn) unless a coded list is desired.
- **Guardian authentication:** v1 models + links + role flags only. The mechanism
  by which a guardian authenticates (portal, magic-link, teacher-orchestrated
  "submit on behalf") is a follow-up. "Submit on behalf" in v1 may be
  teacher-initiated; confirm intended UX.
- **Soft-delete cascade:** on `DeleteGuardian`, auto-unlink `StudentGuardian`
  rows (recommended) vs preserve links with a dangling-guardian state.
- **Publish-time resolution:** synchronous HTTP (§6-A, recommended v1) adds
  latency/coupling to publish; event-driven read-model (§6-B) is the v1.1 path.
- **Preference-order collisions** when linking: auto-assign `max+1` if omitted.

## 18. Explicit non-goals (v1)

- Guardian self-service login / identity provider.
- Email/SMS **delivery** of assignments (v1 captures recipients + role flags;
  actual notification delivery is a follow-up).
- Guardian-facing portal UI (admin/teacher manages guardians; guardian action is
  future).
- Group-based guardian audiences beyond per-student links.
- Replacing the existing `TargetAudienceType` (AllStudents/SelectedGrades/
  SelectedGroups) — guardians are an *additional* recipient dimension resolved
  from the already-targeted students.
