# Student Guardian — Implementation Plan (draft)

> Status: draft, not yet implemented. Companion to the guardian feature branch
> `feature/student-guardians`. Follows the existing `documents/solution/*`
> design-doc style and the CQRS organization pattern in
> `documents/solution/cqrs-organization-pattern.md`.
>
> Revised per scope decisions: **no preference ordering** (role is the only
> classification), **multiple Primary guardians per student**, **publish to
> students AND guardians directly**, **CC = read-only + broadcast notifications**,
> and a **Primary review-gate** (review → submit-on-behalf OR enable student
> submission).

## 1. Goal

Allow a student to have one or more **guardians**, each classified as
**Primary** or **CC** (role is the *only* classification — there is no
preference ordering, and a student may have **multiple Primary** guardians, e.g.
father and mother). Guardians can:

- **Receive assignment notifications.** **CC** guardians are **broadcast every
  assignment notification** and are **read-only** on the assignment (they can
  view but cannot act). **Primary** guardians receive notifications and can
  **act**.
- **Review then submit / enable submission (Primary only).** A Primary guardian
  **reviews** the student's work, then either **submits on behalf of** the
  student, or **enables submission** so the student can submit themselves.
- **Be published to directly.** An assignment can be published to **students**
  and/or to **guardians** as an audience in its own right — not only via the
  student.

A guardian carries: **name** (title, first, last, optional display name for
email), **relationship** to the student, **contact** (phone), **email**,
**address**. A guardian's contact info (email/address/phone) is **shared across
all the student links** for that guardian (one guardian profile, e.g. a parent
of siblings). Guardian **names are auditable entities that change** — every name
change is retained as history, not overwritten.

When an assignment is published, the targeted students and/or guardians are
linked to it as `AssignmentRecipient`s carrying their role, which drives
downstream access (CC read-only + broadcast; Primary review-gate + submit/enable).

## 2. Key decisions (do not relitigate)

- **Role (Primary/CC) is the only guardian classification — no preference order.**
  A student may have **multiple Primary** guardians (father, mother, etc.);
  Primary is **not** unique per student. `StudentGuardian` carries `Role` only.
- **Guardian is owned by the Students bounded context** (it is student-scoped
  data). The Assignments context references guardians **by `Guid` only** — never
  via an EF navigation across DbContexts (the two contexts have separate
  DbContexts / databases).
- **Publish targets both students and guardians directly.** Guardians are not
  only resolved from targeted students; an assignment can be published to a
  guardian audience in its own right. `AssignmentRecipient.RecipientType`
  distinguishes the two.
- **CC = read-only + broadcast.** A CC guardian's actions on an assignment are
  **read-only** (no submit, no enable, no edit), and **every** assignment
  notification is **broadcast** to all CC recipients.
- **Primary = review-gate before submission.** A Primary guardian reviews the
  student's work, then either submits on behalf of the student or sets
  `SubmissionEnabledForStudent = true` so the student can self-submit. The gate
  state lives on `GuardianSubmissionGate` (§4.6).
- **Name audit = append-only history table** on top of the existing
  `IAuditableEntity` (created/updated tracking) that every domain entity
  already implements. A name edit appends a `GuardianNameHistory` row rather than
  mutating history.
- **All entities are tenant-scoped** (`ITenantEntity`) and follow the repo's
  `ModuleDbContext` + `TenantEntityTypeConfigurationBase<T>` conventions
  (tenant query filter + soft-delete filter are applied by the base).
- **Access role (Primary/CC) is captured at two points**: on the
  `StudentGuardian` link (authoritative) and mirrored onto the
  `AssignmentRecipient` created at publish time (so submission/gate checks don't
  have to re-resolve the link).

## 3. Project layout

```
src/Students/SchoolCollab.Students.Core/
  Domain/
    Guardians/
      Guardian.cs                 # : ITenantEntity, IEntity, IAuditableEntity, ISoftDeletableEntity, IHasRowVersion
      GuardianNameHistory.cs      # : IEntity, IAuditableEntity (append-only, NO soft-delete)
      StudentGuardian.cs          # link : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion  (Role only, no order)
      GuardianRole.cs             # enum Primary=0, CC=1
  CQRS/Guardians/
    Commands/CreateGuardian/ UpdateGuardian/ DeleteGuardian/
              LinkGuardianToStudent/ UpdateGuardianLink/ UnlinkGuardian/
    Queries/GetGuardianById/ ListGuardians/ ListGuardiansByStudent/ GetGuardianNameHistory/
  Data/Configurations/ GuardianConfiguration.cs, GuardianNameHistoryConfiguration.cs, StudentGuardianConfiguration.cs
  DTOs/ GuardianDto.cs, StudentGuardianDto.cs, GuardianNameHistoryDto.cs

src/Assignments/SchoolCollab.Assignments.Core/
  Domain/AssignmentRecipient.cs        # : ITenantEntity, IEntity, IAuditableEntity
  Domain/GuardianSubmissionGate.cs     # : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
  Domain/RecipientType.cs              # enum Student=0, Guardian=1
  Domain/RecipientChannel.cs           # enum Email=0 (extensible)
  CQRS/Assignments/Commands/PublishAssignmentCommand/  # extended to resolve + link student AND guardian recipients
       (or a dedicated LinkRecipientsToAssignmentCommand invoked by publish)
  CQRS/Assignments/Commands/GuardianReview/  # ReviewStudentWork / EnableStudentSubmission / SubmitOnBehalfOfStudent
  CQRS/Assignments/Queries/ListAssignmentRecipients/
  Data/Configurations/AssignmentRecipientConfiguration.cs, GuardianSubmissionGateConfiguration.cs
  Services/IGuardianResolver.cs + GuardiansApiClient.cs        # HTTP client to Students.Api
  Services/IAssignmentNotificationBroadcaster.cs               # routes notifications to recipients (CC = all)

src/Students/SchoolCollab.Students.Api/Endpoints/  GuardianRoutes.cs, StudentGuardianRoutes.cs
src/Assignments/SchoolCollab.Assignments.Api/Endpoints/ AssignmentRecipientRoutes.cs, GuardianReviewRoutes.cs

src/Students/SchoolCollab.Students.Admin/Components/Pages/Guardians/   (list, create, edit)
src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/.../GuardiansTab  (link + role editors, add/remove — no reorder)
src/Assignments/SchoolCollab.Assignments.Admin/.../AssignmentRecipients  (recipient view, CC read-only, Primary review/submit/enable)
```

## 4. Domain model

### 4.1 `Guardian` : `ITenantEntity, IEntity, IAuditableEntity, ISoftDeletableEntity, IHasRowVersion`

```
Guid  Id
Guid  TenantId
string? Title                 # Mr/Mrs/Dr — free text or coded value (see §17)
string  FirstName
string  LastName
string? DisplayName           # optional; guardian's display name as it appears in outgoing notifications (email To/From)
string  Email                 # shared across all this guardian's student links; receive target
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
current columns. Email/phone/address are shared across the guardian's links.

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
GuardianRole Role                     # Primary=0, CC=1  (drives access; the only classification)
bool        IsEmergencyContact
uint        RowVersion
DateTimeOffset CreatedAt
DateTimeOffset UpdatedAt
```
**No preference order.** Multiple `Primary` rows are allowed per student
(father, mother, etc.) — there is no uniqueness constraint on `Role`.
Unique constraint is `(tenant_id, student_id, guardian_id)` (a guardian is
linked to a student at most once).

### 4.4 `AssignmentRecipient` (in Assignments.Core) : `ITenantEntity, IEntity, IAuditableEntity`

```
Guid           Id
Guid           TenantId
Guid           AssignmentId
Guid?          StudentId               # cross-BC ref; nullable for direct guardian-only targeting
Guid?          GuardianId              # cross-BC ref; null = direct student recipient
RecipientType  RecipientType           # Student=0, Guardian=1
GuardianRole?  Role                    # mirrored from StudentGuardian.Role (null for student recipients)
RecipientChannel Channel               # Email=0
bool           NotifyOnBroadcast       # true => enrolled in all assignment notifications (CC defaults true)
DateTimeOffset? DeliveredAt
DateTimeOffset? OpenedAt
```
Created at publish. Two targeting modes:
- **Student audience** → one `Student` recipient per student + one `Guardian`
  recipient per guardian of that student (Role mirrored from the link).
- **Guardian audience (direct)** → `Guardian` recipient with `GuardianId` set,
  `StudentId` nullable (the ward context, if any), Role from the guardian's link
  for that student or specified at publish.

**CC** recipients are linked with `Role == CC` and `NotifyOnBroadcast = true`
(read-only + broadcast). **Primary** recipients get `Role == Primary` and drive
the review-gate (§4.6).

### 4.5 Enums
- `GuardianRole { Primary = 0, CC = 1 }`
- `RecipientType { Student = 0, Guardian = 1 }`
- `RecipientChannel { Email = 0 }` (extensible)

### 4.6 `GuardianSubmissionGate` (in Assignments.Core) : `ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion`

Tracks the **Primary review-gate** per `(AssignmentId, StudentId)`:

```
Guid            Id
Guid            TenantId
Guid            AssignmentId
Guid            StudentId
Guid?           ReviewedByGuardianId      # the Primary guardian who reviewed
DateTimeOffset? ReviewedAt
string?         ReviewComment
bool            SubmissionEnabledForStudent   # Primary enabled the student to self-submit
Guid?           SubmittedByGuardianId         # Primary submitted on behalf of the student
DateTimeOffset? SubmittedByGuardianAt
uint            RowVersion
DateTimeOffset  CreatedAt
DateTimeOffset  UpdatedAt
```
Unique on `(tenant_id, assignment_id, student_id)`. A Primary guardian: sets
`ReviewedByGuardianId`/`ReviewedAt` (review), then either sets
`SubmissionEnabledForStudent = true` (student may self-submit) **or** sets
`SubmittedByGuardianId`/`SubmittedByGuardianAt` (submit on behalf). CC guardians
cannot write to this entity. **Note:** this assumes a student-submission model
exists (or is added) in Assignments.Core; if submissions are not yet modeled,
the gate is the first piece of that flow (see §17).

## 5. Storage (Postgres, snake_case)

| Table | Notes |
|---|---|
| `guardians` | tenant-scoped; `email` indexed per tenant |
| `guardian_name_history` | append-only; index `(guardian_id, created_at)` |
| `student_guardians` | unique `(tenant_id, student_id, guardian_id)`; index `(guardian_id)`; **no preference_order column** |
| `assignment_recipients` (assignments db) | index `(assignment_id, guardian_id)`, `(assignment_id, student_id)`; `notify_on_broadcast` for CC broadcast |
| `guardian_submission_gates` (assignments db) | unique `(tenant_id, assignment_id, student_id)` |

All tables carry `tenant_id` and `created_at`/`updated_at` per `IAuditableEntity`;
`guardians`/`student_guardians`/`guardian_submission_gates` carry `row_version`
for concurrency. Use the existing `TenantEntityTypeConfigurationBase<T>` base so
tenant + soft-delete query filters are applied automatically.

## 6. Cross-bounded-context integration (the load-bearing part)

The Assignments context cannot navigate to Students entities. Two viable
patterns:

- **(A) Synchronous resolve at publish (recommended for v1).** Extend
  `PublishAssignmentCommandHandler` (or add `LinkRecipientsToAssignmentCommand`
  invoked by it). For a **student audience**, after the audience students are
  known (by grade/group/selected), call
  `IGuardianResolver.ResolveGuardiansAsync(studentIds, ct)` (an `HttpClient` to
  `Students.Api` `GET /students/{id}/guardians` returning guardian id +
  `GuardianRole`) and insert one `Student` recipient per student plus one
  `Guardian` recipient per guardian (Role mirrored, CC `NotifyOnBroadcast=true`).
  For a **guardian audience (direct)**, the publish command already carries
  guardian ids; fetch each guardian's role/student context via the resolver and
  insert `Guardian` recipients directly.
- **(B) Event-driven read-model (future).** `AssignmentPublishedEvent` (already
  emitted via the shared outbox) is consumed by a Students-side projector that
  writes recipients into a local Assignments read-model. More decoupled, more
  moving parts — defer to v1.1.

**Notifications broadcast:** `IAssignmentNotificationBroadcaster` routes every
assignment notification (published, due, submitted, graded, etc.) to all
`AssignmentRecipient`s with `NotifyOnBroadcast = true` — i.e. **all CC guardians
plus Primary guardians**. The recipient set is computed in v1; actual email/SMS
delivery is v1.1 (§18).

**Unpublish / republish** removes or rebuilds the `AssignmentRecipient` rows for
that assignment (and resets the `GuardianSubmissionGate`).

**Access enforcement:** mutating homework actions in Assignments.Api are gated
by authorization requirements that consult `AssignmentRecipient.Role` and
`GuardianSubmissionGate`:
- **CC** → read-only (all mutating actions denied).
- **Primary** → may review; may submit-on-behalf or enable student submission
  via the gate. Student self-submit is allowed only when
  `SubmissionEnabledForStudent = true`.

The mechanism by which a guardian authenticates is a **must-decide** (§17) — the
review/submit/enable flow is in v1 scope, so an actor surface (guardian
portal/magic-link vs teacher-orchestrated attribution) must be chosen.

## 7. CQRS — Students.Core

Commands (records implementing `ICommand`; handlers `ICommandHandler<T>` with
`HandleAsync`, using repositories + `IIntegrationEventPublisher` (outbox) +
`HybridCache` + `ITenantProvider`, mirroring `CreateStudentHandler`):

- `CreateGuardian(Title?, FirstName, LastName, DisplayName?, Email, ContactPhone?, Address*, RelationshipCodedValueId?)`
- `UpdateGuardian(...)` — if name fields changed, append `GuardianNameHistory`.
- `DeleteGuardian(Guid)` — soft delete; decide link handling (§17).
- `LinkGuardianToStudent(StudentId, GuardianId, RelationshipCodedValueId?, Role, IsEmergencyContact?)`
- `UpdateGuardianLink(...)` — change `Role`/`Relationship`/`IsEmergencyContact`.
- `UnlinkGuardian(StudentId, GuardianId)`

Queries: `GetGuardianById`, `ListGuardians` (tenant), `ListGuardiansByStudent`
(grouped by `Role`), `GetGuardianNameHistory`.

## 8. CQRS — Assignments.Core

- Extend `PublishAssignmentCommand` (or add `LinkRecipientsToAssignmentCommand`)
  to resolve **student AND guardian** recipients via `IGuardianResolver` and
  create `AssignmentRecipient`s (CC `NotifyOnBroadcast=true`).
- `UnpublishAssignmentCommand` rebuilds/removes recipients + resets the gate.
- **Review-gate commands:**
  - `ReviewStudentWork(AssignmentId, StudentId, GuardianId, Comment?)` — Primary
    only; sets `ReviewedByGuardianId`/`ReviewedAt`.
  - `EnableStudentSubmission(AssignmentId, StudentId, GuardianId)` — Primary
    only; sets `SubmissionEnabledForStudent = true`.
  - `SubmitOnBehalfOfStudent(AssignmentId, StudentId, GuardianId, payload)` —
    Primary only; sets `SubmittedByGuardianId`/`At` and creates the submission.
- `IAssignmentNotificationBroadcaster.BroadcastAsync(assignmentId, notification, ct)`.
- Query: `ListAssignmentRecipients(assignmentId)`, `GetSubmissionGate(assignmentId, studentId)`.

## 9. API endpoints

**Students.Api**
- `POST   /students/guardians` — create
- `GET    /students/guardians`, `GET /students/guardians/{id}`
- `PUT    /students/guardians/{id}`
- `DELETE /students/guardians/{id}`
- `POST   /students/{studentId}/guardians` — link (sets relationship/role)
- `PUT    /students/{studentId}/guardians/{guardianId}` — update link (role/relationship)
- `DELETE /students/{studentId}/guardians/{guardianId}` — unlink
- `GET    /students/{studentId}/guardians` — grouped by role (**used by the Assignments resolver**)
- `GET    /students/guardians/{id}/name-history`

**Assignments.Api**
- `POST   /assignments/{id}/publish` — accepts student audience **and/or** guardian audience
- `GET    /assignments/{id}/recipients`
- `POST   /assignments/{id}/students/{studentId}/guardian-review` — `ReviewStudentWork` (Primary)
- `POST   /assignments/{id}/students/{studentId}/enable-submission` — (Primary)
- `POST   /assignments/{id}/students/{studentId}/submit-on-behalf` — (Primary)
- `GET    /assignments/{id}/students/{studentId}/submission-gate`
- Submission/action endpoints gated by role + gate (CC denied; Primary per gate).

## 10. Access control & review-gate

**Role semantics**
- `GuardianRole` on `StudentGuardian` is authoritative; mirrored onto
  `AssignmentRecipient.Role` at publish.
- **CC** → **read-only** on all assignment actions (view only; no submit, no
  enable, no edit, no review-write) + **broadcast** every assignment notification.
- **Primary** → may **review** the student's work, then **submit on behalf** OR
  **enable student submission**. Multiple Primary guardians are allowed; any
  Primary can perform these actions for the student.

**Review-gate flow (Primary)**
1. Primary guardian reviews the student's work → `ReviewStudentWork` sets
   `ReviewedByGuardianId`/`ReviewedAt` on `GuardianSubmissionGate`.
2. Primary chooses one of:
   - **Enable** → `EnableStudentSubmission` sets `SubmissionEnabledForStudent = true`;
     the student may then self-submit.
   - **Submit on behalf** → `SubmitOnBehalfOfStudent` sets
     `SubmittedByGuardianId`/`At` and creates the submission attributed to the
     guardian.
3. Student self-submit is allowed only when `SubmissionEnabledForStudent = true`.
   CC self-submit / submit-on-behalf is always denied.

**Authorization (Assignments.Api)**
- `GuardianReadOnlyRequirement` — CC recipients denied all mutating actions.
- `GuardianSubmissionRequirement` — submit/enable/submit-on-behalf require an
  `AssignmentRecipient` with `Role == Primary` for that assignment+guardian, and
  consult the `GuardianSubmissionGate` state.
- Student self-submit requires the gate's `SubmissionEnabledForStudent = true`.

**Notifications broadcast**
- `IAssignmentNotificationBroadcaster` enrolls every CC recipient
  (`NotifyOnBroadcast = true`) into **all** assignment notifications; Primary
  recipients also receive notifications. Routing/recipient-set is v1; actual
  email/SMS delivery is v1.1 (§18).

**Authentication (must-decide, §17)** — the review/submit/enable flow is in v1,
so an actor surface is required: guardian portal/magic-link vs
teacher-orchestrated attribution. This is no longer a deferrable non-goal.

## 11. Admin UI — Students.Admin

- **Guardians list / create / edit** — reuse the repo's `FormRow` layout primitive
  and `CodedValueDropdown` for `Relationship`. Name fields (Title/First/Last/
  DisplayName) grouped; email/phone/address sized to expected length (per the
  `StudentFormFields` pattern).
- **Student → Guardians tab** — list linked guardians grouped by role
  (Primary / CC), with relationship + role editors and add/remove. **No
  reorder** (role is the only classification). Multiple Primary allowed.
- **Guardian name history** — read-only timeline of `GuardianNameHistory`.

## 12. Admin UI — Assignments.Admin

- **Assignment detail → Recipients** — students + guardians with Primary/CC
  badges; CC rows marked "read-only + broadcast."
- **Publish dialog** — choose audience: students (by grade/group/selected)
  and/or guardians (direct).
- **Primary guardian review UI** — review the student's work, then "Submit on
  behalf" or "Enable student submission." CC sees read-only.
- **Student submission UI** — submission action is enabled only when the gate's
  `SubmissionEnabledForStudent = true`.

## 13. Migrations

- Students.Core: new migration `AddGuardians`
  (`guardians`, `guardian_name_history`, `student_guardians`).
- Assignments.Core: new migration `AddAssignmentRecipientsAndGate`
  (`assignment_recipients`, `guardian_submission_gates`).
- `SchoolCollab.MigrationService` already runs per-module migrations; no new
  wiring beyond registering the new DbContexts' migrators (consistent with the
  existing per-module pattern).

## 14. Audit

- `GuardianNameHistory` — append-only audit of every name change.
- `IAuditableEntity` (`CreatedAt`/`UpdatedAt`/by) on `Guardian`,
  `StudentGuardian`, `AssignmentRecipient`, `GuardianSubmissionGate`.
- `GuardianSubmissionGate` records who reviewed / who submitted-on-behalf / when
  (an audit trail for the review-gate).
- Optional (v1.1): emit `GuardianNameChanged` integration event to the shared
  outbox for downstream notification/audit consumers.

## 15. Testing

- **Unit:** create guardian; name update appends exactly one `GuardianNameHistory`;
  link sets `Role` (multiple Primary allowed per student); publish creates
  `AssignmentRecipient`s for student + guardian recipients with correct `Role`
  and `NotifyOnBroadcast` (CC true); CC guardian is denied mutating actions by
  `GuardianReadOnlyRequirement`; Primary `ReviewStudentWork` →
  `EnableStudentSubmission` opens self-submit; Primary `SubmitOnBehalfOfStudent`
  sets `SubmittedByGuardianId`; student self-submit rejected until
  `SubmissionEnabledForStudent = true`; broadcaster routes to all CC + Primary.
- **ArchitectureTests:** folder/pattern compliance (CQRS org pattern).
- **Playwright:** guardian CRUD; link + role on student (multiple Primary);
  publish to students and to guardians; CC read-only; Primary review → enable /
  submit-on-behalf; student self-submit gated.

## 16. Implementation order

1. Students.Core domain entities + EF configurations + `StudentsDbContext` DbSets.
2. Students.Core migration `AddGuardians`.
3. Students.Core CQRS (commands/queries) + handlers.
4. Students.Api guardian + student-guardian endpoints (+ `name-history`).
5. Assignments.Core `AssignmentRecipient` + `GuardianSubmissionGate` + configs + migration.
6. Assignments.Core `IGuardianResolver`/`GuardiansApiClient` + publish integration (student + guardian audiences).
7. Assignments.Core review-gate commands + `IAssignmentNotificationBroadcaster`.
8. Assignments.Api recipients + review-gate endpoints + role/gate authorization requirements.
9. Students.Admin guardian pages + student-guardian link/role UI + name history.
10. Assignments.Admin recipient view + publish dialog (students/guardians) + Primary review/submit/enable UI + gated student submission.
11. MigrationService wiring + full build/test.

## 17. Open questions / decisions needed

- **Guardian authentication (must-decide — was a follow-up, now blocks v1):** the
  review/submit/enable flow is in v1, so an actor surface is required. Options:
  (a) **guardian portal / magic-link** (guardian logs in, acts directly — bigger
  build), or (b) **teacher-orchestrated attribution** (a teacher/staff performs
  review/submit/enable on behalf of a named guardian — no guardian login). Pick
  one; it shapes §10 and the Admin UI.
- **Review-gate enforcement:** is the gate **mandatory** (a student cannot submit
  until a Primary guardian has reviewed + enabled) or **optional per assignment**
  (teacher/publish decides whether the gate applies)?
- **Direct-to-guardian publish context:** when publishing directly to a guardian,
  is a student/ward context required (so Role is resolved from the link), or can
  a guardian be a standalone audience (Role specified at publish, defaulting to
  Primary)? Affects `AssignmentRecipient.StudentId` nullability semantics.
- **Student-submission model:** `GuardianSubmissionGate` assumes a student
  submission exists in Assignments.Core. If submissions are not yet modeled,
  adding the gate implies adding (or depending on) a submission entity — confirm
  scope.
- **Relationship:** CodedValue (add `Relationships` to `CodedValueParent`, reuse
  the CodedValues system + `CodedValueDropdown`) **vs** free string. Recommend
  CodedValue for consistency with Gender/Subject/Grade.
- **Address:** structured columns (line1/line2/city/state/postal/country) **vs**
  single free-text. Recommend structured for validation/formatting.
- **Title:** free text **vs** coded value. Recommend free text (small, low
  churn) unless a coded list is desired.
- **Soft-delete cascade:** on `DeleteGuardian`, auto-unlink `StudentGuardian`
  rows (recommended) vs preserve links with a dangling-guardian state.
- **Publish-time resolution:** synchronous HTTP (§6-A, recommended v1) adds
  latency/coupling to publish; event-driven read-model (§6-B) is the v1.1 path.

## 18. Explicit non-goals (v1)

- **Email/SMS delivery** of assignments (v1 captures recipients + role flags +
  broadcast routing; actual notification **delivery** is a follow-up). The
  broadcast **routing/recipient-set** is in v1.
- **Guardian self-service portal UI** — *if* the auth decision (§17) is
  teacher-orchestrated, no guardian portal is needed in v1 (admin/teacher
  manages guardians and performs review/submit/enable on their behalf). If the
  decision is guardian portal/magic-link, the portal becomes part of v1 scope
  (re-baseline accordingly).
- Group-based guardian audiences beyond per-student links and direct guardian
  targeting.
- Replacing the existing `TargetAudienceType` (AllStudents/SelectedGrades/
  SelectedGroups) — guardians are an *additional* targeting dimension
  (`RecipientType = Guardian`) resolved from student audiences **or** directly
  targeted, layered on top of the existing audience model.