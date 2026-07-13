# Student Guardian — Implementation Plan (draft)

> Status: draft, not yet implemented. Companion to the guardian feature branch
> `feature/student-guardians`. Follows the existing `documents/solution/*`
> design-doc style and the CQRS organization pattern in
> `documents/solution/cqrs-organization-pattern.md`.
>
> **Scope has expanded** beyond guardians: this plan also covers the **Teacher**
> entity + setup wizard (teachers don't exist yet), the **assignment submission
> lifecycle** (submission + versioning + teacher post-submission review/grade),
> the **guardian admin portal**, and **multi-channel contacts + subscription-based
> publishing** for students and guardians.
>
> Decisions applied: **no preference ordering** (role only; multiple Primary),
> **publish to students AND guardians directly**, **CC = read-only + broadcast**,
> a **Primary review-gate** that is **mandatory by default per assignment**
> (`MandatoryReview`), **guardian portal** (OIDC auth, **disabled via feature
> flag** for now) listing assignments by status + CC admin, **CodedValue-backed
> relationship / title / community / city** (`RELATSHIPS` / `SALUTS` /
> `COMMUNITYS` / `CITIES`) with **community → city → country** attribute
> hierarchy, **free-text address + community**, **submission versioning**,
> **teacher post-submission review/grade**, **soft-delete guardian = block only,
> preserve history**, **multi-channel contacts (email/SMS/WhatsApp)** for
> students + guardians, and **subscription-based publishing to multiple contacts**.
> "Publish" in this plan always means **assignment publish**.

## 1. Goal

Allow a student to have one or more **guardians**, each classified as
**Primary** or **CC** (role is the *only* classification — no preference
ordering; a student may have **multiple Primary** guardians). Guardians can:

- **Receive assignment notifications** on their **subscribed contacts**. Both
  **students and guardians have multiple contacts** (email, SMS, WhatsApp, …);
  publishing to a contact is **subscription-based** — only **subscribed** contacts
  receive. **CC** guardian contacts are **broadcast every assignment notification**
  and are **read-only**; **Primary** guardian contacts receive notifications and
  the guardian can **act**. The ward (student) may have no subscribed contact, in
  which case notifications go to the guardians.
- **Review then submit / enable submission (Primary only).** A Primary guardian
  reviews the student's work, then either submits on behalf of the student or
  enables submission so the student can submit themselves. This review-gate is
  **mandatory by default** per assignment (`Assignment.MandatoryReview = true`),
  unless the assignment explicitly sets it to **no**.
- **Be published to directly.** An assignment can be published to students and/or
  to guardians, targeting **multiple contacts** per student/guardian.
- **Manage CC guardians** — in v1, **teacher/admin** adds/removes CC guardians
  (and manages contacts/subscriptions). Guardian self-service via the guardian
  portal (OIDC) is a **later feature** (§18).

A guardian carries: **name** (title [coded], first, last, optional display name
for email), **relationship** [coded] to the student, **contacts** (multiple:
email/SMS/WhatsApp…), **address** (free text) + **community** [coded → city →
country]. A guardian's profile (contacts, address, name) is **shared across all
the student links** for that guardian. Guardian **names are auditable entities
that change** — every name change is retained as history, not overwritten.
**Soft-deleting a guardian blocks them but preserves all history**.

The plan also adds **teachers** (none exist today): a `Teacher` entity with a
**setup wizard** (specifying **subjects taught** and **grade levels**), who
**create assignments** and **review or grade submissions** on the assignments
they created.

Guardian onboarding reuses the `GradeLevelWizard` pattern: the wizard gains a
new step to **add guardians for the enrolled students in the grade** (per-student
Primary/CC links), and a standalone **`GuardianSetupWizard`** + **guardians
landing page** mirror the wizard/landing scaffold. A shared **`ContactsEditor`**
component (like `StudentFormFields`) owns add/remove/verify/subscribe for a
student's or guardian's multi-channel contacts.

## 2. Key decisions (do not relitigate)

- **Role (Primary/CC) is the only guardian classification — no preference order.**
  Multiple Primary guardians per student; `StudentGuardian` carries `Role` only.
- **Multi-channel contacts for students + guardians.** A `Contact` entity
  (email/SMS/WhatsApp…, extensible) owned by a `Student` or `Guardian`
  (`OwnerType` + `OwnerId`). Replaces the single `Email`/`ContactPhone` on
  `Guardian` and `ContactEmail`/`ContactPhone` on `Student` (migrated). A
  guardian's **verified primary email contact** is the identity anchor for the
  later OIDC portal feature (out of v1).
- **Subscription-based publishing.** A `ContactSubscription` per contact (scope +
  status) gates delivery — publishing targets **subscribed** contacts only. v1
  scope = **`AllAssignments` only** (scoped subscriptions are out of v1 — §18).
  Contacts opt in/out via **admin** in v1 (the guardian portal is a later feature).
- **Publish targets multiple contacts.** The publish command selects which
  contacts (per student/guardian) to publish to; `AssignmentRecipient` is **one
  row per (assignment, contact)** (cross-BC `ContactId` ref), carrying channel +
  owner + (for guardians) mirrored role + `SubscriptionActive`.
- **Notifications deduplicated by contact.** A shared guardian with multiple
  wards in the audience gets **one recipient row per contact** (not per ward) and
  **one consolidated notification per contact per assignment** (listing the
  affected wards); the wards are resolved at broadcast time from the audience.
- **Ward may lack a subscribed contact → guardians receive.** The student is
  notified only if they have a subscribed contact; otherwise notifications go to
  the guardians' subscribed contacts.
- **Guardian portal + OIDC is a later feature (out of v1).** v1 manages
  guardians, CC links, contacts, and subscriptions via the teacher/admin app
  (`Students.Admin`). The guardian-facing portal (OIDC auth, assignments by
  status, self-service CC + contacts) is deferred to a later feature (§18).
- **Review-gate is mandatory by default per assignment.**
  `Assignment.MandatoryReview` (bool, default `true`).
- **Publish = assignment publish.** Every "publish" reference means publishing an
  assignment (`PublishAssignmentCommand` / `AssignmentPublishedEvent`).
- **Publish targets both students and guardians directly.**
  `AssignmentRecipient.OwnerType` distinguishes them.
- **CC = read-only + broadcast.** CC actions are read-only; every assignment
  notification is broadcast to all subscribed CC contacts.
- **Primary = review-gate before submission.** Primary reviews, then
  submit-on-behalf or enable student submission. Gate state on
  `GuardianSubmissionGate` (§4.10).
- **Submission versioning.** `AssignmentSubmission` (current) +
  `AssignmentSubmissionVersion` (one row per submission/resubmission) (§4.11).
- **Teacher post-submission review/grade is in scope.** A teacher reviews/grades
  `AssignmentSubmission`s for assignments they created
  (`Assignment.CreatedByTeacherId`); recorded on `SubmissionReview` (§4.13). The
  existing `AssignmentReview` (keyed by `AssignmentId`) is a separate concept,
  **not** reused.
- **Teachers don't exist yet → add `Teacher` + setup wizard** (subjects taught +
  grade levels; mirrors `GradeLevelWizard`). `Teacher` lives in Students.Core;
  Assignments references teachers by `Guid`; `Teacher.StaffUserId` links to the
  existing staff auth. **Teacher self-service portal** (create assignments /
  grade / message students+wards) is a **later feature** (§18); v1 uses the
  existing admin apps + staff auth.
- **Guardian onboarding reuses the `GradeLevelWizard` scaffold.** (a) `GradeLevelWizard`
  gains a new step to **add guardians for the enrolled students in the grade**
  (per-student `StudentGuardian` Primary/CC links, created on save). (b) A
  standalone **`GuardianSetupWizard`** + **guardians landing page** mirror the
  wizard/landing pattern (entry gates, `FluentWizardStep` validation, inline
  sub-form, sequential `SaveAsync`). (c) A shared **`ContactsEditor`** component
  (like `StudentFormFields`) owns add/remove/verify/subscribe for a student's or
  guardian's multi-channel contacts — reused by the guardian wizard, the
  GradeLevelWizard guardian step, and the student/guardian ContactsTab.
- **Relationship / Title / Community / City / Country are CodedValues**: `RELATSHIPS`,
  `SALUTS`, `COMMUNITYS`, `CITIES`, `COUNTRYS`. Community → City → Country attribute
  hierarchy (§4.8). Reuse `CodedValueDropdown` + the existing coded-value attribute
  mechanism — no Settings schema change.
- **Address = free text + community.** `Guardian.Address` (free text) +
  `Guardian.CommunityId` (`COMMUNITYS`); city/country resolve via attributes.
- **Soft-delete guardian = block only, preserve history.** `DeleteGuardian` sets
  `IsDeleted=true`; no cascade-unlink, no history deletion.
- **Guardian is owned by the Students bounded context.** Assignments references
  guardians/contacts by `Guid` only (separate DbContexts/databases).
- **Name audit = append-only `GuardianNameHistory`** on top of `IAuditableEntity`.
- **All entities are tenant-scoped** (`ITenantEntity`) via `ModuleDbContext` +
  `TenantEntityTypeConfigurationBase<T>`.
- **Access role captured at two points**: `StudentGuardian` (authoritative) +
  mirrored onto `AssignmentRecipient.Role`.
- **A student submission entity does not exist today** → add `AssignmentSubmission`
  + versioning.

## 3. Project layout

```
src/SchoolCollab.Admin.Shared/Constants/CodedValueConstants.cs   # add Relationships/Communities/Cities/Salutations to CodedValueParent + ToCode
src/Settings/SchoolCollab.Settings.Core/                         # CodedValue + attribute system already here — NO schema change
src/SchoolCollab.MigrationService/Seeding/SeedData/              # seed.csv (RELATSHIPS/SALUTS/COMMUNITYS/CITIES + communities), seed-attribute-definitions.csv (City on COMMUNITYS, Country on CITIES), seed-attributes.csv (Lapaz/Achimota/East Legon/Adenta/Haatso → City=Accra; Accra → Country=Ghana)

src/Students/SchoolCollab.Students.Core/
  Domain/Guardians/   Guardian.cs, GuardianNameHistory.cs, StudentGuardian.cs, GuardianRole.cs
  Domain/Contacts/    Contact.cs, ContactSubscription.cs, ContactChannel.cs, ContactOwnerType.cs, SubscriptionScope.cs, SubscriptionStatus.cs   # NEW
  Domain/Teachers/    Teacher.cs, TeacherSubject.cs, TeacherGradeLevel.cs    # NEW
  Domain/Student.cs   # add StudentContact collection; migrate ContactEmail/ContactPhone → Contact rows
  CQRS/Guardians/     Commands + Queries (incl. Primary-adds-CC)
  CQRS/Contacts/      Commands (AddContact/UpdateContact/DeleteContact/VerifyContact) + Subscription (Subscribe/Unsubscribe) + Queries   # NEW
  CQRS/Teachers/      Commands + Queries + SetupWizard steps   # NEW
  Data/Configurations/ ...Guardian*, Contact*, Subscription*, Teacher*, StudentGuardian*
  DTOs/ ...

src/Assignments/SchoolCollab.Assignments.Core/
  Domain/Assignment.cs                # add bool MandatoryReview (default true)
  Domain/AssignmentRecipient.cs       # reworked: per (assignment, contact)
  Domain/GuardianSubmissionGate.cs
  Domain/AssignmentSubmission.cs       # NEW
  Domain/AssignmentSubmissionVersion.cs # NEW
  Domain/SubmissionReview.cs           # NEW
  Domain/SubmissionSource.cs, ReviewState.cs, ContactOwnerType.cs, ContactChannel.cs
  CQRS/Assignments/Commands/PublishAssignmentCommand/   # resolves subscribed contacts (student + guardian audiences)
  CQRS/Assignments/Commands/GuardianReview/  Submission/  SubmissionReview/
  CQRS/Assignments/Queries/...
  Data/Configurations/ ...Recipient, Gate, Submission, SubmissionVersion, SubmissionReview
  Services/IContactResolver.cs + ContactsApiClient.cs (replaces GuardianResolver; HTTP to Students.Api), IAssignmentNotificationBroadcaster.cs

src/Students/SchoolCollab.Students.Api/Endpoints/   GuardianRoutes.cs, StudentGuardianRoutes.cs, ContactRoutes.cs, SubscriptionRoutes.cs, TeacherRoutes.cs
src/Assignments/SchoolCollab.Assignments.Api/Endpoints/ AssignmentRecipientRoutes.cs, GuardianReviewRoutes.cs, SubmissionRoutes.cs, SubmissionReviewRoutes.cs

src/Students/SchoolCollab.Students.Admin/Components/Pages/Guardians/   Guardians.razor (landing + TenantGate + search), GuardianSetupWizard.razor (NEW — mirrors GradeLevelWizard), Create/Edit, name-history
src/SchoolCollab.Admin.Shared/Components/ContactsEditor.razor (+ .razor.css)   # NEW shared multi-channel contacts editor (add/remove/verify/subscribe) — like StudentFormFields; reused by guardian wizard, GradeLevelWizard guardian step, student/guardian ContactsTab
# GradeLevelWizard.razor (existing) gains a new 'add guardians for grade students' step (per-student Primary/CC StudentGuardian links, created on save)
src/Students/SchoolCollab.Students.Admin/Components/Pages/Teachers/    (teacher admin + SetupWizard)  # NEW
src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/.../ContactsTab + GuardiansTab
src/Assignments/SchoolCollab.Assignments.Admin/.../AssignmentRecipients + MandatoryReview toggle + publish-to-contacts + SubmissionReview

# LATER FEATURE (not v1): src/Guardians/SchoolCollab.Guardians.Admin/ — guardian-facing Blazor host (portal): OIDC, assignments by status, review/submit/enable, self-service CC + contacts/subscriptions
```

## 4. Domain model

### 4.1 `Guardian` : `ITenantEntity, IEntity, IAuditableEntity, ISoftDeletableEntity, IHasRowVersion`

```
Guid  Id
Guid  TenantId
Guid? TitleCodedValueId        # CodedValue 'SALUTS'
string  FirstName
string  LastName
string? DisplayName            # optional; display name in outgoing notifications
string? Address                # free text (street / locality line)
Guid?  CommunityId             # CodedValue 'COMMUNITYS' → City (CITIES) → Country attribute
bool   IsDeleted               # soft-delete = block only (history preserved)
DateTimeOffset? DeletedAt
uint   RowVersion
DateTimeOffset CreatedAt
DateTimeOffset UpdatedAt
# navigation: List<Contact> Contacts (owned, multi-channel) — §4.4
```
**No `Email`/`ContactPhone` columns** — those live on `Contact` rows (§4.4). The
guardian's **OIDC identity** maps to a **verified primary Email contact**. Name
edits via `UpdateGuardian` append a `GuardianNameHistory` row when
`Title/FirstName/LastName/DisplayName` change. `DeleteGuardian` sets
`IsDeleted=true` only — no cascade-unlink, no history deletion; contacts are
retained (soft-deleted with the guardian) for audit.

### 4.2 `GuardianNameHistory` : `IEntity, IAuditableEntity` (append-only, NO soft-delete)

```
Guid   Id
Guid   TenantId
Guid   GuardianId
Guid?  TitleCodedValueId
string  FirstName
string  LastName
string? DisplayName
Guid?   ChangedBy
string? ChangeReason
DateTimeOffset ChangedAt
```
Append-only. **Never deleted**, even when the guardian is soft-deleted.

### 4.3 `StudentGuardian` (link) : `ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion`

```
Guid        Id
Guid        TenantId
Guid        StudentId
Guid        GuardianId
Guid?       RelationshipCodedValueId   # CodedValue 'RELATSHIPS'
GuardianRole Role                      # Primary=0, CC=1
bool        IsEmergencyContact
Guid?       CreatedByGuardianId        # if linked via portal (Primary adding CC)
uint        RowVersion
DateTimeOffset CreatedAt
DateTimeOffset UpdatedAt
```
No preference order. Multiple `Primary` per student. Unique on
`(tenant_id, student_id, guardian_id)`. **Retained when the guardian is
soft-deleted** (history); the soft-delete filter excludes the guardian from
active recipient resolution but keeps the link rows.

### 4.4 `Contact` (NEW, Students.Core) : `ITenantEntity, IEntity, IAuditableEntity, ISoftDeletableEntity, IHasRowVersion`

Multi-channel contact owned by a student or guardian:

```
Guid             Id
Guid             TenantId
ContactOwnerType OwnerType     # Student=0, Guardian=1
Guid             OwnerId       # StudentId or GuardianId
ContactChannel   Channel       # Email=0, SMS=1, WhatsApp=2 (extensible)
string           Value         # email address / phone number / whatsapp id
string?          Label         # "Home", "Work", "Primary"…
bool             IsPrimary
bool             IsVerified
bool             IsDeleted
DateTimeOffset?  DeletedAt
uint             RowVersion
DateTimeOffset   CreatedAt
DateTimeOffset   UpdatedAt
```
Unique on `(tenant_id, owner_type, owner_id, channel, value)` (one of each
value per channel). A student/guardian has **many contacts across channels**.
**Migration:** existing `Student.ContactEmail`/`ContactPhone` and any legacy
guardian email/phone are migrated into `Contact` rows (`OwnerType=Student`/
`Guardian`, `Channel=Email`/`SMS`); the legacy columns are then dropped
(§13). At most one `IsPrimary` email per owner. **`IsVerified` is admin-set in
v1** (no OTP) and is **only required for subscribed contacts** (a contact must be
verified before it can be subscribed/delivered to). The OIDC identity mapping is
a later feature (§18).

### 4.5 `ContactSubscription` (NEW, Students.Core) : `ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion`

Subscription gates whether a contact receives **published assignments**:

```
Guid               Id
Guid               TenantId
Guid               ContactId
ContactChannel     Channel          # denormalized for convenience
SubscriptionScope  Scope            # AllAssignments=0 (extensible: ByGrade=1, BySubject=2, ByPeriod=3)
SubscriptionStatus Status           # Subscribed=0, Unsubscribed=1
Guid?              ScopeRefId       # grade/subject/period id when Scope != AllAssignments
DateTimeOffset     SubscribedAt
DateTimeOffset?    UnsubscribedAt
uint               RowVersion
DateTimeOffset     CreatedAt
DateTimeOffset     UpdatedAt
```
Unique on `(tenant_id, contact_id, scope, scope_ref_id)`. v1 uses
`Scope = AllAssignments` **only** (a contact is subscribed/unsubscribed to all
assignment publications; scoped subscriptions `ByGrade`/`BySubject`/`ByPeriod`
are out of scope for v1 — §18). **New contacts default to `Unsubscribed`
(opted-out)** — an explicit `Subscribe` (after verification) opts them in. **A
contact must be `IsVerified` (admin-set) before it can be subscribed.** The
Assignments publish/broadcast filters recipients by `Status == Subscribed`.

### 4.6 `AssignmentRecipient` (Assignments.Core) — reworked to **per (assignment, contact)** : `ITenantEntity, IEntity, IAuditableEntity`

```
Guid              Id
Guid              TenantId
Guid              AssignmentId
ContactOwnerType  OwnerType        # Student=0, Guardian=1
Guid              OwnerId          # student/guardian Guid (cross-BC)
Guid?             WardStudentId    # ward student for guardian recipients (required for direct guardian publish; Role from StudentGuardian(student,guardian)); null for student recipients
Guid              ContactId        # cross-BC ref to Contact
ContactChannel    Channel          # Email/SMS/WhatsApp
GuardianRole?     Role             # mirrored from StudentGuardian.Role when OwnerType==Guardian
bool              NotifyOnBroadcast   # CC defaults true
bool              SubscriptionActive  # contact's subscription was active at publish
DateTimeOffset?   DeliveredAt
DateTimeOffset?   OpenedAt
```
Unique on `(tenant_id, assignment_id, contact_id)` — **one recipient row per
contact per assignment**. At publish:
- **Student audience** → for each student, for each **subscribed** contact, one
  `Student` recipient; plus for each guardian of the student, for each subscribed
  contact, one `Guardian` recipient (Role mirrored; CC `NotifyOnBroadcast=true`).
- **Guardian audience (direct)** → **ward required**: the publish specifies the
  ward (student); for each guardian linked to that ward, for each subscribed
  contact, one `Guardian` recipient (`WardStudentId` set, `Role` from the
  `StudentGuardian` link for that ward). No standalone (ward-less) guardian audience.
- **Publish to multiple contacts** — the publish command selects which contacts
  (per student/guardian) to target; recipient rows are created for selected +
  subscribed contacts.
- **Shared guardian** → one recipient row **per contact** (not per ward); the
  broadcast lists the affected wards (resolved from the audience at send time).
- The student is a target only if they have a subscribed contact (no
  `StudentHasContact` flag needed — no recipient rows ⇒ not notified).

### 4.7 `Assignment` — new field `MandatoryReview`

```
bool MandatoryReview          # default TRUE. true => student self-submit blocked until Primary reviews + enables (or submits on behalf). false => gate optional.
```
Set at `CreateAssignment`/`UpdateAssignment`; toggle in the assignment UI (default on).

### 4.8 CodedValue extensions (Settings context — no schema change)

Coded values live in `Settings.Core` (`CodedValue`: `Code`, `Name`, `Description`,
`ParentId`, `DisplayOrder`, `TenantId`, …) with a generic attribute mechanism
(`CodedValueAttributeDefinition` + `CodedValueAttribute`). Add parents to
`CodedValueParent` (`Admin.Shared/Constants/CodedValueConstants.cs`):

```
Relationships   => "RELATSHIPS"
Communities     => "COMMUNITYS"
Cities          => "CITIES"
Countries       => "COUNTRYS"
Salutations     => "SALUTS"
```

**Hierarchy via attributes:** Community → City → Country.
- On `COMMUNITYS` parent: attribute definition `City` (holds the city's `Code`/`Id`).
- On `CITIES` parent: attribute definition `Country` (references a `COUNTRYS` coded value).

**Seeded values (CSV in `MigrationService/Seeding/SeedData/`, idempotent via
`CodedValueSeeder`):**
- `SALUTS`: Mr, Mrs, Ms, Miss, Dr, Prof, Rev, Mx (global/blueprint).
- `RELATSHIPS`: Father, Mother, Guardian, Grandparent, Aunt, Uncle, Step-parent,
  Foster-parent, Other (global/blueprint).
- `COUNTRYS`: Ghana (global/blueprint).
- `CITIES`: Accra (global/blueprint); attribute `Country` = Ghana (COUNTRYS ref).
- `COMMUNITYS`: **Lapaz, Achimota, East Legon, Adenta, Haatso** — each with
  attribute `City` = Accra.
- Attribute definitions seeded: `City` on `COMMUNITYS`, `Country` on `CITIES`
  (via `seed-attribute-definitions.csv`); attribute values via
  `seed-attributes.csv`.

**Reuse:** `Title`/`Relationship`/`Community` dropdowns use
`<CodedValueDropdown Parent="CodedValueParent.Salutations|Relationships|Communities" />`;
city/country resolve via coded-value attribute reads.

### 4.9 Enums
- `GuardianRole { Primary = 0, CC = 1 }`
- `ContactOwnerType { Student = 0, Guardian = 1 }`
- `ContactChannel { Email = 0, SMS = 1, WhatsApp = 2 }` (extensible)
- `SubscriptionScope { AllAssignments = 0, ByGrade = 1, BySubject = 2, ByPeriod = 3 }`
- `SubscriptionStatus { Subscribed = 0, Unsubscribed = 1 }`
- `SubmissionSource { Student = 0, GuardianOnBehalf = 1 }`
- `ReviewState { Pending = 0, Reviewed = 1, Graded = 2 }`

### 4.10 `GuardianSubmissionGate` (Assignments.Core) : `ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion`

```
Guid            Id
Guid            TenantId
Guid            AssignmentId
Guid            StudentId
Guid?           ReviewedByGuardianId
DateTimeOffset? ReviewedAt
string?         ReviewComment
bool            SubmissionEnabledForStudent
Guid?           SubmittedByGuardianId
DateTimeOffset? SubmittedByGuardianAt
uint            RowVersion
DateTimeOffset  CreatedAt
DateTimeOffset  UpdatedAt
```
Unique on `(tenant_id, assignment_id, student_id)`. Primary: review → enable OR
submit-on-behalf. When `Assignment.MandatoryReview == true`, student self-submit
requires `SubmissionEnabledForStudent == true` (and a Primary has reviewed); when
`false`, the gate is informational.

### 4.11 `AssignmentSubmission` (NEW) + `AssignmentSubmissionVersion` (NEW, versioning)

`AssignmentSubmission` (one current row per assignment+student):

```
Guid            Id
Guid            TenantId
Guid            AssignmentId
Guid            StudentId
int             CurrentVersionNumber
SubmissionSource CurrentSource             # Student | GuardianOnBehalf
Guid?           SubmittedByGuardianId
DateTimeOffset  LastSubmittedAt
Guid?           SubmissionGateId
ReviewState     ReviewState                # Pending/Reviewed/Graded
uint            RowVersion
DateTimeOffset  CreatedAt
DateTimeOffset  UpdatedAt
```

`AssignmentSubmissionVersion` (one row per submission/resubmission):

```
Guid            Id
Guid            TenantId
Guid            SubmissionId
Guid            AssignmentId
Guid            StudentId
int             VersionNumber
SubmissionSource Source
Guid?           SubmittedByGuardianId
DateTimeOffset  SubmittedAt
string?         Content
uint            RowVersion
DateTimeOffset  CreatedAt
```
Unique `(tenant_id, submission_id, version_number)`. Each
`CreateStudentSubmission` / `SubmitOnBehalfOfStudent` / resubmission inserts a
new version and bumps `CurrentVersionNumber`. Authorization per the gate +
`MandatoryReview`.

### 4.12 `Teacher` (NEW, Students.Core) + links + setup wizard

`Teacher` : `ITenantEntity, IEntity, IAuditableEntity, ISoftDeletableEntity, IHasRowVersion`

```
Guid  Id
Guid  TenantId
Guid? TitleCodedValueId        # CodedValue 'SALUTS'
string  FirstName
string  LastName
string? DisplayName
string  Email                  # staff single email (staff auth); multi-contact is a follow-up
string? ContactPhone
Guid?  StaffUserId            # links to staff user identity (existing staff OIDC); used by Assignment.CreatedByTeacherId + teacher portal (later)
bool   IsDeleted
DateTimeOffset? DeletedAt
uint   RowVersion
DateTimeOffset CreatedAt
DateTimeOffset UpdatedAt
```

`TeacherSubject` (link: subjects taught) : `ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion`
`Guid Id; Guid TenantId; Guid TeacherId; Guid SubjectId; uint RowVersion; CreatedAt; UpdatedAt`

`TeacherGradeLevel` (link: grade levels) : `ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion`
`Guid Id; Guid TenantId; Guid TeacherId; Guid GradeLevelId; uint RowVersion; CreatedAt; UpdatedAt`

**Teacher setup wizard** (mirrors `GradeLevelWizard`): steps — (1) teacher
profile (title, name, email, phone), (2) pick **subjects taught**, (3) pick
**grade levels**. Reuse `FormRow` + `CodedValueDropdown` (title). A teacher
creates assignments (`Assignment.CreatedByTeacherId`) and reviews/grades
submissions on assignments they created (§4.13).

### 4.13 `SubmissionReview` (NEW, Assignments.Core) — teacher post-submission review/grade

: `ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion`

```
Guid            Id
Guid            TenantId
Guid            SubmissionId
Guid            AssignmentId
Guid            StudentId
Guid            TeacherId          # == Assignment.CreatedByTeacherId
decimal?        Score
string?         Grade
string?         Comments
DateTimeOffset  ReviewedAt
uint            RowVersion
DateTimeOffset  CreatedAt
DateTimeOffset  UpdatedAt
```
Unique on `(tenant_id, submission_id)` — **single row per submission +
`RowVersion`** for revisions (no review-versions table in v1). Created by the
teacher who owns the assignment; flips `AssignmentSubmission.ReviewState` to
`Reviewed`/`Graded`. **Distinct from the existing `AssignmentReview`** (keyed by
`AssignmentId`).

## 5. Storage (Postgres, snake_case)

| Table | Notes |
|---|---|
| `guardians` | tenant-scoped; `title_coded_value_id`, `community_id` Guid refs; `address` free text; **no email/phone columns**; soft-delete (block only) |
| `guardian_name_history` | append-only; index `(guardian_id, created_at)`; **retained on soft-delete** |
| `student_guardians` | unique `(tenant_id, student_id, guardian_id)`; `relationship_coded_value_id`; `created_by_guardian_id`; retained on soft-delete |
| `contacts` | unique `(tenant_id, owner_type, owner_id, channel, value)`; `is_primary`, `is_verified`; soft-delete |
| `contact_subscriptions` | unique `(tenant_id, contact_id, scope, scope_ref_id)`; `status` |
| `students` | **drop `contact_email`/`contact_phone`** (migrated to `contacts`) |
| `teachers` | tenant-scoped; `title_coded_value_id`; soft-delete |
| `teacher_subjects` | `(tenant_id, teacher_id, subject_id)` unique |
| `teacher_grade_levels` | `(tenant_id, teacher_id, grade_level_id)` unique |
| `assignment_recipients` | unique `(tenant_id, assignment_id, contact_id)`; index `(assignment_id, owner_id)`; `channel`, `role`, `notify_on_broadcast`, `subscription_active` |
| `guardian_submission_gates` | unique `(tenant_id, assignment_id, student_id)` |
| `assignment_submissions` | unique `(tenant_id, assignment_id, student_id)`; `current_version_number`, `review_state` |
| `assignment_submission_versions` | unique `(tenant_id, submission_id, version_number)` |
| `submission_reviews` | unique `(tenant_id, submission_id)` |
| `assignments` | add `mandatory_review` bool NOT NULL DEFAULT true |
| **Settings db** | no schema change — coded-value rows + attribute definitions/values via seed CSV |

All new tables carry `tenant_id` + `created_at`/`updated_at`; `row_version` on
guardians/links/contacts/subscriptions/teachers/gate/submission/review. Use
`TenantEntityTypeConfigurationBase<T>` for tenant + soft-delete filters.

## 6. Cross-bounded-context integration

Assignments cannot navigate Students/Settings entities. Patterns:

- **(A) Synchronous resolve at publish (recommended v1).** Extend
  `PublishAssignmentCommandHandler` (or `LinkRecipientsToAssignmentCommand`).
  `IContactResolver` (HttpClient to `Students.Api`) returns, per student/guardian,
  their **subscribed contacts** (id + channel + value + role). Student audience →
  for each student, one `AssignmentRecipient` per subscribed contact
  (`OwnerType=Student`) + for each guardian, one per subscribed contact
  (`OwnerType=Guardian`, Role mirrored; CC `NotifyOnBroadcast=true`). Guardian
  audience (direct) → one recipient per the guardian's subscribed contact. The
  publish command may **select a subset of contacts** (publish to multiple
  contacts of a student/guardian). A shared guardian yields **one recipient row
  per contact** (not per ward); the broadcaster lists wards at send time.
- **(B) Event-driven read-model (v1.1).** `AssignmentPublishedEvent` consumed by
  a Students-side projector.

**Notifications broadcast:** `IAssignmentNotificationBroadcaster` routes every
assignment notification (published/due/submitted/graded/closed) to
`AssignmentRecipient`s with `SubscriptionActive && NotifyOnBroadcast` (i.e. all
subscribed **CC + Primary** contacts), **deduplicated by contact** — a shared
guardian with multiple wards in the audience receives **one consolidated
notification per contact per assignment** (listing the affected wards, resolved
from the audience at send time), not one per ward. The student's contacts receive
only if the student has subscribed contacts. Routing/recipient-set in v1; actual
email/SMS/WhatsApp **delivery** is v1.1 (§18). **"Publish" here = assignment publish.**

**Unpublish/republish** rebuilds `AssignmentRecipient`s + resets the gate
(submissions + versions retained).

**Access enforcement** consults `AssignmentRecipient.Role`,
`Assignment.MandatoryReview`, `GuardianSubmissionGate`, the guardian's portal
identity, and (for review) `Assignment.CreatedByTeacherId`:
- CC → read-only. Primary → review + submit-on-behalf/enable.
- Student self-submit → allowed only when `MandatoryReview == false` OR
  `SubmissionEnabledForStudent == true`.
- Teacher review/grade → only `CreatedByTeacherId == teacher.Id`.

## 7. CQRS — Students.Core

**Guardians:**
- `CreateGuardian(TitleCodedValueId?, FirstName, LastName, DisplayName?, Address?, CommunityId?, RelationshipCodedValueId?)` (no email/phone — add via `AddContact`)
- `UpdateGuardian(...)` — name change appends `GuardianNameHistory`.
- `DeleteGuardian(Guid)` — **soft-delete = block only**.
- `LinkGuardianToStudent(StudentId, GuardianId, RelationshipCodedValueId?, Role, IsEmergencyContact?, ActingGuardianId?)` — Primary guardian (portal, **CC only**) or teacher/admin.
- `UpdateGuardianLink(...)`, `UnlinkGuardian(...)`.
- Queries: `GetGuardianById`, `ListGuardians`, `ListGuardiansByStudent`, `GetGuardianNameHistory`, `ListStudentsForGuardian`.

**Contacts + Subscriptions (NEW):**
- `AddContact(OwnerType, OwnerId, Channel, Value, Label?, IsPrimary?)`
- `UpdateContact`, `DeleteContact` (soft), `VerifyContact(ContactId)`, `SetPrimaryContact`.
- `Subscribe(ContactId, Scope, ScopeRefId?)`, `Unsubscribe(ContactId, Scope, ScopeRefId?)`.
- Queries: `ListContacts(OwnerType, OwnerId)`, `ListSubscribedContacts(OwnerType, OwnerId, Scope?)`, `GetSubscription`.

**Teachers (NEW):**
- `CreateTeacher(TitleCodedValueId?, FirstName, LastName, DisplayName?, Email, ContactPhone?)`, `UpdateTeacher`, `DeleteTeacher`.
- `LinkTeacherSubject`/`UnlinkTeacherSubject`, `LinkTeacherGradeLevel`/`UnlinkTeacherGradeLevel`.
- Queries: `GetTeacherById`, `ListTeachers`, `ListSubjectsForTeacher`, `ListGradeLevelsForTeacher`, `ListAssignmentsForTeacher`.

## 8. CQRS — Assignments.Core

- `PublishAssignmentCommand` (or `LinkRecipientsToAssignmentCommand`) resolves
  **subscribed contacts** via `IContactResolver` and creates `AssignmentRecipient`s
  (one per contact; `SubscriptionActive=true`; CC `NotifyOnBroadcast=true`). The
  command accepts an optional **contact selection** (publish to multiple specific
  contacts per student/guardian).
- `CreateAssignment`/`UpdateAssignment` accept `MandatoryReview` (default true).
- `UnpublishAssignmentCommand` rebuilds recipients + resets gate.
- **Review-gate (Primary):** `ReviewStudentWork`, `EnableStudentSubmission`,
  `SubmitOnBehalfOfStudent` (creates submission + version, `Source=GuardianOnBehalf`).
- **Submission:** `CreateStudentSubmission(AssignmentId, StudentId, Content)` —
  gated (`MandatoryReview==false` OR `SubmissionEnabledForStudent`); inserts a new
  `AssignmentSubmissionVersion` and bumps `CurrentVersionNumber`.
- **Teacher review/grade:** `ReviewSubmission(SubmissionId, TeacherId, Score?, Grade?, Comments?)` —
  authorized only for `CreatedByTeacherId == teacher.Id`; flips `ReviewState`;
  creates `SubmissionReview`.
- `IAssignmentNotificationBroadcaster.BroadcastAsync(...)` (dedup by contact).
- Queries: `ListAssignmentRecipients`, `GetSubmissionGate`, `GetSubmission` (with
  versions), `ListSubmissionsByAssignment`, `ListAssignmentsForGuardian` (by
  status open/submitted/closed).

## 9. API endpoints

**Students.Api** — guardians + links (+ `name-history`, `guardians/me/students`);
**contacts + subscriptions** (`POST /contacts`, `GET /contacts?ownerType=&ownerId=`,
`PUT /contacts/{id}`, `DELETE /contacts/{id}`, `POST /contacts/{id}/verify`,
`POST /contacts/{id}/subscribe`, `POST /contacts/{id}/unsubscribe`,
`GET /contacts/subscribed?ownerType=&ownerId=&scope=` — used by the Assignments
resolver); **teachers** + subject/grade links.

**Assignments.Api**
- `POST /assignments/{id}/publish` (student and/or guardian audience; optional contact selection)
- `GET  /assignments/{id}/recipients`
- `POST /assignments/{id}/students/{studentId}/guardian-review` (Primary)
- `POST /assignments/{id}/students/{studentId}/enable-submission` (Primary)
- `POST /assignments/{id}/students/{studentId}/submit-on-behalf` (Primary)
- `POST /assignments/{id}/students/{studentId}/submission` (student self-submit, gated)
- `GET  /assignments/{id}/students/{studentId}/submission` (with versions)
- `POST /assignments/{id}/students/{studentId}/submission/review` (teacher grade)
- `GET  /guardians/me/assignments` (portal: by status open/submitted/closed)

**Settings.Api** — existing CodedValue endpoints serve `RELATSHIPS`/`SALUTS`/
`COMMUNITYS`/`CITIES` once the parent enum + seed rows are added; community/city
attributes use existing `CodedValueAttribute*` endpoints. No new Settings endpoints.

## 10. Access control, review-gate, guardian portal & teacher review

**Roles** — CC = read-only + broadcast; Primary = review + submit-on-behalf/enable
+ manage CC for their student + manage own contacts/subscriptions.

**Guardian admin portal — LATER FEATURE (not v1).** The guardian-facing
portal (`SchoolCollab.Guardians.Admin`, OIDC auth, assignments by status,
self-service CC/contacts/subscriptions, Primary review/submit/enable) is deferred
to a later feature (§18). In v1, all guardian management (CC links, contacts,
subscriptions) and review/submit/enable actions are performed by **teacher/admin**
in `Students.Admin`/`Assignments.Admin`.

**Review-gate flow (Primary)** — review → enable (student self-submit) OR
submit-on-behind; mandatory per `Assignment.MandatoryReview`.

**Teacher review/grade** — the teacher who created an assignment
(`CreatedByTeacherId`) reviews/grades its `AssignmentSubmission`s via
`ReviewSubmission` → `SubmissionReview` + `ReviewState` flip.

**Authorization (Assignments.Api)** — `GuardianReadOnlyRequirement` (CC),
`GuardianSubmissionRequirement` (Primary + gate), `StudentSubmissionRequirement`
(self-submit per `MandatoryReview`/gate), `TeacherReviewRequirement`
(`CreatedByTeacherId == teacher.Id`).

**Notifications** — broadcast to all subscribed CC + Primary contacts (deduped by
contact); student contacts only if subscribed. Routing v1; delivery v1.1.

## 11. Admin UI — Students.Admin (teacher/admin)

- **Guardians landing page** (`Guardians.razor`) — `TenantGate` + search + “Create
  guardian” → **`GuardianSetupWizard`**; per-row Edit + name-history.
- **GuardianSetupWizard** (NEW — mirrors `GradeLevelWizard`): `<TenantGate>` +
  ward-selection gate → Step 1 guardian profile (Title `Salutations`, name,
  Relationship `Relationships`, Address + Community `Communities`) → Step 2
  contacts (shared `<ContactsEditor>`: add/remove/verify email/SMS/WhatsApp;
  each defaults opted-out; subscribe toggle) → Step 3 link to ward(s) with
  Primary/CC role → Step 4 review + save (`SaveAsync` creates `Guardian` +
  initial `GuardianNameHistory`, `Contact`s + `ContactSubscription`s, and
  `StudentGuardian` links).
- **GradeLevelWizard — new “add guardians” step** (existing wizard): after enrol
  students, a step to **add guardians for the grade's enrolled students** — per
  student, pick an existing guardian or create one inline (mini
  `GuardianSetupWizard`/`ContactsEditor`) and set Primary/CC; `SaveAsync` creates
  the `StudentGuardian` links alongside the grade/subject/enrollment saves.
- **Student → Guardians tab** — grouped by role, add/remove, no reorder, multiple Primary.
- **Student → Contacts tab** (NEW) — shared `<ContactsEditor>` for the student's contacts + subscriptions.
- **Guardian name history** — read-only timeline.
- **Teachers list/create/edit + SetupWizard** (NEW) — profile → subjects taught → grade levels.

## 12. Admin UI — Assignments.Admin (teacher/admin)

- Assignment create/edit — `MandatoryReview` toggle (default on).
- **Publish dialog** — choose audience (students and/or guardians) + **select
  contacts** to publish to (per student/guardian, filtered by subscription).
- Recipients view — per contact, with Primary/CC badges + channel + subscription
  state; CC "read-only + broadcast".
- **Teacher submission review/grade** — per submission: score/grade/comments;
  submission version history.

## 12b. Guardian admin portal UI — LATER FEATURE (not v1)

The guardian-facing portal (`SchoolCollab.Guardians.Admin`) — OIDC sign-in,
assignments by status (open/submitted/closed), Primary review/submit/enable,
self-service CC + contacts/subscriptions — is deferred to a later feature
(§18). v1 surfaces all guardian/CC/contact/subscription management and
review/submit/enable in the teacher/admin apps (§11, §12).

## 13. Migrations

- Students.Core: `AddGuardians` (guardians, guardian_name_history, student_guardians)
  + `AddContacts` (contacts, contact_subscriptions) + `AddTeachers` (teachers,
  teacher_subjects, teacher_grade_levels); **migrate `students.contact_email`/
  `contact_phone` → `contacts` rows, then drop the legacy columns**.
- Assignments.Core: `AddAssignmentRecipients` (reworked per-contact) +
  `AddGuardianSubmissionGates`; `AddAssignmentSubmissions` (submissions +
  versions); `AddSubmissionReviews`; add `mandatory_review` to `assignments`.
- Settings.Core: **no migration** — coded-value rows + attribute definitions/values via seed CSV.
- `SchoolCollab.MigrationService` runs per-module migrations + the seeder.

## 14. Audit

- `GuardianNameHistory` (append-only, **retained on soft-delete**).
- `Contact`/`ContactSubscription`/`StudentGuardian`/`AssignmentRecipient`/
  `GuardianSubmissionGate`/`AssignmentSubmission`/`SubmissionReview` carry
  `IAuditableEntity` + `RowVersion`.
- `StudentGuardian.CreatedByGuardianId` audits portal-created CC links.
- `AssignmentSubmissionVersion` is the full submission version history.
- `SubmissionReview` records teacher + score/grade/comments + when.
- `ContactSubscription` status changes (subscribe/unsubscribe) are auditable.
- Optional (v1.1): `GuardianNameChanged` integration event to the outbox.

## 15. Testing

- **Unit:** guardian CRUD (coded title/relationship/community; no email/phone);
  contact CRUD (multi-channel, per owner); subscription subscribe/unsubscribe;
  name change → one `GuardianNameHistory`; soft-delete blocks but keeps history +
  links + contacts; Primary adds CC (not Primary) via `ActingGuardianId`; publish
  creates **one recipient per subscribed contact** (CC `NotifyOnBroadcast`;
  unsubscribed contacts excluded); publish to a selected subset of contacts;
  shared guardian → one recipient per contact (not per ward) + broadcaster sends
  one consolidated notification per contact listing wards; `MandatoryReview`
  gating; submit-on-behind creates version; resubmission bumps version; teacher
  `ReviewSubmission` authorized only for `CreatedByTeacherId`.
- **ArchitectureTests:** CQRS org; `CodedValueParent` codes present; seed CSV
  contains `RELATSHIPS`/`SALUTS`/`CITIES`/`COMMUNITYS` + community attribute
  definitions + the five communities (City=Accra) + Accra (Country=Ghana).
- **Playwright (v1):** guardian CRUD + contacts/subscriptions editor; teacher setup
  wizard; student contacts tab; publish to selected contacts (students/guardians);
  `MandatoryReview` toggle; teacher review/grade; submission version history.
- **Playwright (later, not v1):** guardian portal (flag on) assignments by status +
  Primary review/submit/enable + manage CC + manage contacts/subscriptions; CC
  read-only.

## 16. Implementation order (phased)

The work is grouped into phases. Each phase is independently build/test-able and
is ordered by dependency. The **`Teacher` entity + migration lands in Phase 2**
(so assignments can reference real teachers from Phase 5 onward); the **Teacher
CQRS + API + SetupWizard UI (Phase 8) is the parallelizable part** and can run
alongside Phases 3–7. The guardian portal + OIDC, teacher portal, OTP, scoped
subscriptions, and email/SMS/WhatsApp delivery are **later features** (not v1 —
see §18). Each phase lands its own tests (unit with CQRS phases, Playwright with
UI phases) per §15 — not deferred to Phase 9.

### Phase 1 — Seed data & coded values (prerequisite, no domain logic)
**Delivers:** the CodedValue parents + seedable picklists the rest of the plan
depends on. **Tests:** ArchitectureTests on the seed CSV (parents + 5 communities
+ Accra/Ghana).
- `CodedValueParent` enum + codes; seed CSV (SALUTS/RELATSHIPS/CITIES/COMMUNITYS +
  communities Lapaz/Achimota/East Legon/Adenta/Haatso → City=Accra; Accra →
  Country=Ghana) + attribute definitions (City on COMMUNITYS, Country on CITIES).
- Blueprint/global scope (matches the existing seeder pattern).

### Phase 2 — Guardian, Contacts, link domain + Teacher entity (Students.Core)
**Depends on:** Phase 1. **Delivers:** persistence for guardians, multi-channel
contacts, subscriptions, the student→guardian link, and the teacher entity
(needed by assignments from Phase 5). **Tests:** unit tests for EF configs +
migration up/down.
- Guardian + `GuardianNameHistory` + `StudentGuardian` + EF configs + migration `AddGuardians`.
- **Contact + ContactSubscription** + EF configs + migration `AddContacts`.
- **Teacher + `TeacherSubject` + `TeacherGradeLevel`** + EF configs + migration
  `AddTeachers` (entity lands here; CQRS/UI in Phase 8).
- **Legacy contact migration (M1):** `AddContacts` → backfill data-migration
  (`students.contact_email`/`contact_phone` → `contacts` rows, opted-out by
  default) → `DropLegacyStudentContactColumns`. Backfill before dropping.
- **G1 — update the existing Student surface:** remove `Email`/`ContactPhone` from
  the `Student` entity, `CreateStudent`/`UpdateStudent` commands + DTOs, and the
  PR #74 `StudentFormFields`/`StudentFormModel` (contact editing moves to the
  Student ContactsTab in Phase 4).

### Phase 3 — Guardian + Contact CQRS & Students.Api
**Depends on:** Phase 2. **Delivers:** the command/query surface for guardians,
contacts, subscriptions, and links. **Tests:** unit tests for guardian/contact/
subscription CQRS (incl. Primary-adds-CC, soft-delete-preserves-history).
- Guardian CQRS + **Contact/Subscription CQRS** (incl. Primary-adds-CC,
  soft-delete-preserves-history).
- Students.Api: guardian + student-guardian + **contact + subscription** endpoints.
- **G2 — authorization:** guardian/contact/subscription management endpoints are
  admin/teacher-only (not student/anonymous).
- **G5 — cross-BC contract:** define the shared “subscribed contacts” DTO shape
  (id + channel + value + role) returned by `GET /contacts/subscribed`, consumed
  by Phase 6's `ContactsApiClient`.
- **M4 — cache:** invalidate HybridCache entries for a student's/guardian's
  contacts on add/remove/verify/subscribe.

### Phase 4 — Guardian admin UI (Students.Admin)
**Depends on:** Phase 1 (coded values) + Phase 3 (CQRS/API). **Delivers:** guardian
onboarding + contact-management screens. **Tests:** Playwright guardian CRUD +
contacts/subscriptions editor + student contacts tab.
- Shared `<ContactsEditor>` (Admin.Shared) — multi-channel add/remove/verify/subscribe.
- Guardians landing page (`Guardians.razor`, `TenantGate` + search) +
  `GuardianSetupWizard` (ward gate → profile → contacts → link wards → review +
  save; `SaveAsync` creates `Guardian` + initial `GuardianNameHistory`, `Contact`s
  + `ContactSubscription`s, and `StudentGuardian` links).
- Student → Guardians tab + Student → Contacts tab + guardian name-history timeline.
- **GradeLevelWizard “add guardians” step** (per-student Primary/CC links).

### Phase 5 — Assignment publishing & submission domain (Assignments.Core)
**Depends on:** Phase 1 + Phase 2 (Teacher entity for `CreatedByTeacherId`).
**Delivers:** persistence for publish targeting, review-gates, and the submission
lifecycle. **Tests:** unit tests for entity configs + migration up/down.
- `Assignment.MandatoryReview` + `AssignmentRecipient` (per-contact) +
  `GuardianSubmissionGate` + `AssignmentSubmission` + `AssignmentSubmissionVersion`
  + `SubmissionReview` + configs + migrations.

### Phase 6 — Publish integration, review-gate & teacher review (Assignments.Core logic)
**Depends on:** Phase 3 (ContactsApiClient + subscribed-contacts DTO) + Phase 5.
**Delivers:** publish-time contact resolution, broadcast + dedup, and the
submission/review engine. **Tests:** unit tests for publish recipient
resolution, dedup-by-contact, `MandatoryReview` gating, submit-on-behalf,
resubmission version bump, teacher `ReviewSubmission` authz.
- `IContactResolver`/`ContactsApiClient` (consumes the Phase 3 DTO) + publish
  integration (subscribed contacts; student + guardian audiences; contact selection).
- Review-gate + submission (versioning) + teacher review + `IAssignmentNotificationBroadcaster`
  (dedup by contact).
- **M4 — outbox:** broadcast via the existing `IIntegrationEventPublisher` outbox
  pattern (delivery itself is v1.1 — §18).

### Phase 7 — Assignments API + admin UI (Assignments.Admin)
**Depends on:** Phases 3 (Students.Api contact endpoints for the publish dialog's
contact selection) + 5 + 6. **Delivers:** publish/recipient/submission endpoints +
screens. **Tests:** Playwright publish-to-selected-contacts + `MandatoryReview`
toggle + submission/version + teacher review/grade.
- Assignments.Api: recipients + review-gate + submission + submission-review endpoints +
  authorization requirements.
- Assignments.Admin: recipients + `MandatoryReview` + publish dialog (audience + contact
  selection) + submission/version + teacher review/grade UI.

### Phase 8 — Teacher CQRS + API + SetupWizard UI (parallelizable)
**Depends on:** Phase 1 (coded values for subjects/grades) + Phase 2 (Teacher
entity). **Delivers:** teacher onboarding + the command/query surface. Can run in
parallel with Phases 3–7. **Tests:** unit tests for Teacher CQRS + Playwright
setup wizard.
- Teacher CQRS + setup wizard steps (`CreateTeacher`, subject/grade links, queries).
- Students.Api: teacher endpoints (admin/teacher-only — G2).
- Students.Admin: teacher admin + SetupWizard. (Teacher self-service portal is a
  later feature — §18.)

### Phase 9 — Migration wiring + seed run + full build/test (cross-cutting)
**Depends on:** all of the above. **Delivers:** a reproducible end-to-end state.
- MigrationService wiring + seed run + full build/test across all modules.

### Later (not v1) — see §18
`SchoolCollab.Guardians.Admin` portal + OIDC, teacher portal, OTP verification, scoped
subscriptions, and email/SMS/WhatsApp delivery channels.

## 17. Open questions / decisions needed

_All v1-shaping open questions are now resolved — see “Resolved in this
revision” below. Remaining work is deferred to later features (§18: guardian
portal + OIDC, teacher portal, OTP verification, scoped subscriptions,
email/SMS/WhatsApp delivery) or to v1.1 (event-driven publish-time
read-model)._

### Resolved in this revision
- Multi-channel contacts (email/SMS/WhatsApp) for students + guardians (`Contact`).
- Subscription-based publishing (`ContactSubscription`); publish targets subscribed contacts.
- Publish to multiple contacts of a student/guardian; `AssignmentRecipient` is per (assignment, contact).
- Notifications deduplicated by contact (one consolidated notification per contact per assignment, listing wards for shared guardians).
- Subscription scope → **`AllAssignments` only** in v1 (scoped subscriptions out).
- Contact verification → **admin-set `IsVerified`**, **only required for subscribed contacts** (no OTP in v1).
- Guardian portal + OIDC → **later feature** (out of v1); v1 manages guardians/CC/contacts/subscriptions via the teacher/admin app. The guardian's verified primary email contact is the identity anchor for the later OIDC portal.
- Ward may lack a subscribed contact → notifications go to guardians.
- "Publish" → assignment publish.
- Submission versioning → yes (`AssignmentSubmissionVersion`).
- Teacher post-submission review → in scope (`SubmissionReview`, by `CreatedByTeacherId`).
- Teachers → add `Teacher` + setup wizard (subjects taught + grade levels).
- Community/city → CodedValue `COMMUNITYS`/`CITIES`; community→city→country attributes; seed Lapaz/Achimota/East Legon/Adenta/Haatso (City=Accra), Accra (Country=Ghana).
- Soft-delete guardian → block only, preserve history (no cascade).
- Relationship → `RELATSHIPS`; Title → `SALUTS`; Address → free text + community.
- Review-gate → mandatory by default (`Assignment.MandatoryReview`).
- Preference order → dropped (role only; multiple Primary).
- New contacts default to **opted-out** (`Unsubscribed`); explicit `Subscribe` after verification.
- Direct-to-guardian publish → **ward required** (Role from the `StudentGuardian` link; no standalone guardian audience; `AssignmentRecipient.WardStudentId`).
- SubmissionReview revisions → **single row + `RowVersion`** (no review-versions table in v1).
- Country → **CodedValue `COUNTRYS`** (seed Ghana); the city's `Country` attribute references it.
- Community seeding → **blueprint/global** (matches the existing seeder pattern).
- Teacher multi-contact → **single staff email/phone** in v1 (teachers aren't notification recipients).
- Publish-time resolution → **synchronous HTTP** in v1 (event-driven read-model is v1.1).
- Teacher identity → existing **staff auth**; `Teacher.StaffUserId` links to the staff user. **Teacher portal** (create assignments / grade / message students+wards) is a **later feature** (§18).

## 18. Explicit non-goals (v1)

- **Guardian portal + OIDC** — a **later feature** (not v1). v1 has no
  guardian-facing portal; guardian/CC/contact/subscription management and
  review/submit/enable are teacher/admin-driven. The portal + OIDC + guardian
  identity mapping are designed later.
- **Email/SMS/WhatsApp delivery** of assignments (v1 = recipients + subscription
  + broadcast routing; actual **delivery** is a follow-up). Broadcast
  routing/recipient-set is in v1.
- **Scoped subscriptions** (`ByGrade`/`BySubject`/`ByPeriod`) — v1 ships
  `AllAssignments` scope only.
- **Contact OTP verification** — v1 uses admin-set `IsVerified` (only required for
  subscribed contacts); OTP flow is a follow-up.
- **Teacher multi-contact** — v1 keeps single staff `Email`/`ContactPhone` on `Teacher`.
- Group-based guardian audiences beyond per-student links + direct guardian targeting.
- Replacing the existing `TargetAudienceType` — guardians are an *additional*
  targeting dimension (`OwnerType = Guardian`) layered on top.
- **Teacher portal** (create assignments / grade / message students+wards) — a
  **later feature** (not v1). v1 has the `Teacher` entity + setup wizard; teacher
  actions happen in the existing admin apps via staff auth.
- Guardian portal mobile app (the guardian portal itself is a later feature).
---

## Implementation Status (2026-07-12)

| Phase | Scope | Status | Notes |
|-------|-------|--------|-------|
| 1 | Coded values + seed | **DONE** | +5 enum members, +21 seed rows, attribute rows, 4/4 arch tests |
| 2 | Domain model + EF | **DONE** | 8 entities, 6 enums, 8 configs, single migration `AddGuardiansContactsTeachers`, 7/7 domain tests |
| 3 | CQRS + API | **DONE** | 11 guardian commands, 5 guardian queries, 7 contact/subscription commands, 3 queries, 4 route files, 14/14 `GuardianContactsCqrsTests` |
| 4 | Admin UI | **DONE (core) + PLAYWRIGHT SMOKE** | `Guardians.razor`, `GuardianSetupWizard`, `GuardianDetail`, `ContactsEditor`, `GuardiansTab`, student `Detail` FluentTabs (Overview/Guardians/Contacts), NavMenu link — build clean; 69/69 domain + 12/12 arch. `SchoolCollab.Students.Tests.Playwright` smoke tests authored (Guardians index, create button, wizard nav, student Guardians+Contacts tabs). GradeLevelWizard guardian step deferred (Tier 2). Full create-wizard Playwright flow + execution require running AppHost + seeded tenant (env-dependent). |
| 5 | Assignment publishing & submission domain (Assignments.Core) | **DONE** | `Assignment.MandatoryReview`; new `AssignmentRecipient`, `GuardianSubmissionGate`, `AssignmentSubmission`, `AssignmentSubmissionVersion`, `SubmissionReview`; new enums `SubmissionSource`/`ReviewState`; 5 configs; migration `AddAssignmentSubmissionLifecycle`; 6 entity-config tests. 51/51 Assignments + 12/12 Arch tests pass. |
| 6 | Publish integration, review-gate & teacher review engine | **DONE (engine) + POLISH** | `IContactResolver` + `StudentsContactResolver`; publish upserts `AssignmentRecipient` (deduped by contact) + auto-creates `GuardianSubmissionGate` when `MandatoryReview`; **6 CQRS handlers** (+`CreateStudentSubmission` §4.10 gate) + §9-shaped submit endpoints; `ISubmissionRepository`; `ReviewSubmission` enforces `CreatedByTeacherId` authz; **`IAssignmentNotificationBroadcaster`** extracted (outbox); `PublishAssignmentCommand` contact selection; `Unpublish` rebuilds recipients + resets gate. 73/73 Assignments.Unit + 12/12 Arch. Remaining: full §9 route alignment (gate/submission review) + §8 recipient/submission queries — deferred to Phase 7. |

### Phase 4 deviations / notes
- **`ListGuardiansByStudent` returns `StudentGuardianViewDto[]`** (not `GuardianDto[]`)
  so the student Guardians tab can render role + name without N round-trips.
  Added `StudentGuardianViewDto` to Core.DTOs.
- **GradeLevelWizard "Add guardians" step (Tier 2)** — **deferred**. The plan
  flags this as a larger, deferrable enhancement; the core guardian management
  UI (list / create wizard / detail / contacts editor / student tabs / nav) is
  complete and the full API is functional, so guardian linking is available from
  both the Guardian detail page and the student Guardians tab. Adding a nested
  guardian-create step inside the GradeLevelWizard is left as a follow-up to
  avoid destabilizing the existing enrollment flow.
- **Playwright tests (Phase 4 Tests) — smoke suite authored** — added
  `SchoolCollab.Students.Tests.Playwright` with `GuardianAdminTests` (Guardians
  index load, create button visible, wizard navigation from Create, student
  Guardians + Contacts tab visibility). Builds clean. The full guardian
  create-wizard flow + test execution require a running AppHost + a seeded tenant
  (environment-dependent), same as the existing Settings Playwright suite; left
  as a follow-up once CI can run the app.
- **`FluentSelect` usage**: enum/object options bind via `@bind-SelectedOption`
  + `OptionValue` (not `@bind-Value`, which is `string` in this FluentUI build).
- **`WardLink`** is a `readonly record struct` so `Nullable<>.HasValue/.Value`
  work for the wizard's selected-wards dictionary.

### Phase 5 deviations / notes
- **Cross-BC enum reuse:** `AssignmentRecipient.OwnerType`/`Channel`/`Role` use
  the Students.Core enums `ContactOwnerType` / `ContactChannel` / `GuardianRole`.
  `SchoolCollab.Assignments.Core` now takes a **project reference to
  `SchoolCollab.Students.Core`** (acyclic — Students.Core does not reference
  Assignments). `SubmissionSource` and `ReviewState` are new enums defined in
  Assignments.Core (§4.9).
- **`AssignmentRecipient` was created fresh** (the rework described in §4.6 — it
  did not previously exist in Assignments.Core), as a per-(assignment, contact)
  tenant-scoped entity with the unique index `(tenant_id, assignment_id,
  contact_id)`.
- **`MandatoryReview`** added to `Assignment` (default `true`); `Assignment.Create`
  hard-codes the default. Wiring it through `CreateAssignment`/`UpdateAssignment`
  commands is **Phase 6/7** work (CQRS + UI toggle).
- **Entity behaviors** (publish recipient resolution, gate enable / submit-on-
  behalf, version bump, teacher review) are intentionally left to **Phase 6**
  (the submission/review engine); Phase 5 delivers persistence only. The entity
  methods `GuardianSubmissionGate.EnableForStudent` / `SubmitOnBehalf` and
  `AssignmentSubmission.RecordSubmission` / `ApplyReview` exist as internal
  seams for that engine.
- Migration `AddAssignmentSubmissionLifecycle` (snake_case, unique indexes,
  `xmin` row version) generated; model⇄snapshot sync verified by both the
  per-domain and central `MigrationGuardTests`.

### Phase 6 deviations / notes
- **Publish resolves subscribers via `IContactResolver`** (new Core service
  interface) instead of reaching into Students.Core directly — keeps the BC
  boundary clean. `StudentsContactResolver` (Assignments.Api) calls the existing
  Students.Api `GET /contacts/subscribed?ownerType=&ownerId=&scope=` and
  `GET /students/by-grade/{gradeLevelId}` and `GET /students/{id}/guardians`
  endpoints (no new Students endpoints were required). It uses an Aspire-
  discovered named `HttpClient` `"students-api"` and is registered as a typed
  client in `Program.cs`; the AppHost wires `assignments-api --reference
  students-api`.
- **Two new migrations in Assignments.Core:** `AddAssignmentPublishedAt`
  (adds `published_at` to `assignments`, set by `Assignment.Publish()`) and the
  previously-generated `AddAssignmentSubmissionLifecycle`. Both are in the same
  model; model⇄snapshot sync verified by `MigrationGuardTests` (12/12).
- **`AssignmentRecipient.MarkSubscribed(bool)`** added (the entity previously
  had private setters only) so the resolver can re-affirm subscription state.
- **`GuardianSubmissionGate.Review(reviewerGuardianId, approve, comment)`**
  renamed from the Phase 5 `EnableForStudent` seam to reflect that a Primary
  guardian *reviews* the gate (approve → enables student self-submit; deny →
  records review but keeps submission disabled, per §4.10). `SubmitOnBehalf`
  keeps its name and additionally marks the gate enabled + records the
  guardian.
- **New domain exceptions** `GuardianSubmissionGateNotFoundException` and
  `SubmissionNotFoundException` are thrown by the review handlers when the
  referenced gate/submission is missing (defensive; current callers pre-create
  both on publish).
- **`SubmissionSource.GuardianOnBehalf`** is the source emitted by
  `SubmitAssignmentOnBehalfCommandHandler` (a Primary guardian submitting a
  student's work).
- **Teacher review** (`ReviewSubmissionCommandHandler`) appends a
  `SubmissionReview` and sets `AssignmentSubmission.ReviewState` to `Graded`
  when a score/grade/outcome is present, otherwise `Reviewed`. The legacy
  assignment-level `ReviewAssignmentCommand` (§4.8, teacher reviewing the whole
  assignment) is unchanged and distinct from submission review.
- **Tests:** `SubmissionEngineTests` (13 tests) cover domain methods
  (`Publish` sets `PublishedAt`; gate `Review`/`SubmitOnBehalf`; submission
  `RecordSubmission`/`ApplyReview`; recipient create/mark-subscribed) and
  handler orchestration with faked `IContactResolver`, `ISubmissionRepository`,
  `IAssignmentRepository`, `ITenantProvider`, `IIntegrationEventPublisher` and a
  real `HybridCache` (the new entities use relational `xmin` row-version, so the
  handlers are exercised with fakes rather than InMemory DbContext).

#### Phase 6 gaps (review 2026-07-12)
✔ _Resolved (Tier 1, 2026-07-12):_
- **`ReviewSubmission` authz** — `ReviewSubmissionCommandHandler` now loads the
  `Assignment` via `submission.AssignmentId` and throws `UnauthorizedAccessException`
  when `command.TeacherId != assignment.CreatedByTeacherId` (§8/§10). The review
  endpoint returns 403 on that exception. _HIGH — fixed_
- **Student self-submit** — `CreateStudentSubmissionCommand` +
  `CreateStudentSubmissionCommandHandler` implement the
  `!MandatoryReview || gate.SubmissionEnabledForStudent` gate, insert an
  `AssignmentSubmissionVersion` (`Source=Student`), and bump `CurrentVersionNumber`
  (§4.11); new §9-shaped endpoint `POST /assignments/{id}/students/{studentId}/submission`. _MEDIUM — fixed_
- **Required tests** — added `ReviewSubmission_RejectsNonCreatorTeacher` (authz),
  `PublishAssignment_DeduplicatesByContact` (idempotent upsert), and
  `SubmitAssignmentOnBehalf_ResubmissionBumpsVersion` (v2), plus 3
  `CreateStudentSubmission` tests. 71/71 Assignments.Unit pass. _MEDIUM — fixed_

_Remaining gaps:_
- **Route shapes partly diverge from §9** — the legacy gate review
  (`/gates/{gateId}/review`) + submission review (`/submissions/{submissionId}/review`)
  are still id-based; student-on-behalf + student self-submit now use the §9
  student-scoped shape. Full alignment deferred to Phase 7 (UI finalizes routes). _LOW_
- **Several §8 queries absent** (`ListAssignmentRecipients`, `GetSubmission` w/
  versions, `ListSubmissionsByAssignment`, `ListAssignmentsForGuardian`) —
  mostly Phase 7 / portal. _LOW_

✔ _Resolved (Tier 3, 2026-07-12):_ broadcaster extracted (#5), publish contact
selection (#6), Unpublish rebuild recipients + reset gate (#7), student-on-behalf
+ student self-submit §9 routes (#8 partial). 73/73 Assignments.Unit pass.

## Consolidated gaps & recommendations (next steps)

_Review dated 2026-07-12 (Tiers 1–3 applied). Test state: Students.Unit 69/69,
ArchitectureTests 12/12, Assignments.Unit 73/73; `SchoolCollab.sln` builds 0
errors. Phases 1, 2, 3, 5 fully meet the plan; Phase 4 UI complete + Playwright
smoke tests authored; Phase 6 engine complete + plan-fidelity polish applied;
remaining work is Phase 7–9 + the deferred Tier 3 items below._

### Tier 1 — finish Phase 6 (its own stated scope) ✔ DONE (2026-07-12)
1. ~~**`ReviewSubmission` authorization**~~ — done: handler loads the
   `Assignment` and rejects non-creator teachers; review endpoint returns 403.
   _HIGH — fixed_
2. ~~**Student self-submit `CreateStudentSubmission`**~~ — done: gate rule
   `!assignment.MandatoryReview || gate.SubmissionEnabledForStudent`, inserts a
   version + bumps `CurrentVersionNumber`; §9-shaped endpoint added. _MEDIUM — fixed_
3. ~~**Phase 6 missing tests**~~ — done: dedup-by-contact, resubmission v2,
   `ReviewSubmission` authz + 3 `CreateStudentSubmission` tests; 73/73 pass.
   _MEDIUM — fixed_

### Tier 2 — close the Phase 4 test gap ✔ DONE (2026-07-12)
4. ~~**Playwright: guardian CRUD + `<ContactsEditor>` + student Contacts tab**~~ —
   done: added `SchoolCollab.Students.Tests.Playwright` with smoke tests
   (Guardians index, create button, wizard navigation, student Guardians +
   Contacts tabs). Builds clean. _Smoke-level; full create-wizard flow + execution
   require a running AppHost + seeded tenant (env-dependent), same as the
   Settings Playwright suite._ _MEDIUM — fixed_

### Tier 3 — plan fidelity / polish ✔ PARTLY DONE (2026-07-12)
5. ~~**`IAssignmentNotificationBroadcaster`**~~ — done: extracted the inline
   publish routing into `IAssignmentNotificationBroadcaster` + default
   `AssignmentNotificationBroadcaster` (outbox enqueue); v1.1 per-contact
   consolidated notification (listing wards) plugs in behind the interface.
   _LOW-MED — fixed_
6. ~~**`PublishAssignmentCommand` contact selection**~~ — done: optional
   `ContactIds` parameter + `PublishAssignmentRequest`; handler filters the
   resolved subscribers to the selected subset. _LOW — fixed_
7. ~~**`Unpublish` rebuild recipients + reset gate**~~ — done:
   `DeleteRecipientsForAssignmentAsync` + `ListGatesForAssignmentAsync` +
   `GuardianSubmissionGate.Reset()`; Unpublish handler rebuilds recipients + resets
   gates (submissions/versions retained). _LOW — fixed_
8. **Align Phase 6 route shapes to §9** — _partial_: `submit-on-behalf` moved to
   `/{id}/students/{studentId}/submit-on-behalf` (+ student self-submit already
   §9-shaped). Remaining: gate review + submission review still id-based — align
   with Phase 7 UI (needs student-scoped lookup queries). _LOW — deferred to Phase 7_
9. **GradeLevelWizard "add guardians" step** — deferred (larger UI work).
   _LOW — deferred_
10. **Move `ContactsEditor` → `Admin.Shared`** — only if another admin app needs
    reuse; not needed yet. _LOW — deferred_

### Tier 4 — upcoming phases (not started)
11. **Phase 7** — Assignments admin UI: recipients view, `MandatoryReview` toggle,
    publish dialog (audience + contact selection), submission/version list,
    teacher review/grade screen + endpoints; finalize student-scoped routes.
12. **Phase 8** — Teacher CQRS + API (admin/teacher-only) + SetupWizard UI.
13. **Phase 9** — MigrationService wiring + seed run + full cross-module build/test.
14. **Integration tests (Testcontainers)** — currently blocked environmentally
    (no Docker); revisit when available.
