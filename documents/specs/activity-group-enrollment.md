# Spec: Activity-Group Enrollment & Assignment Targeting

> **Status:** Approved
> **Author:** Cline (spec-driven-workflow)
> **Date:** 2026-07-29
> **Reviewers:** Students context owner, Assignments context owner, Architecture
> **Owner contexts:** `SchoolCollab.Students.Core`, `SchoolCollab.Assignments.Core`,
> `SchoolCollab.Students.Admin`, `SchoolCollab.Assignments.Admin`
> **Depends on:** `grade-level-setup.md`, `global-tenant-filter.md`,
> `auth-tenancy-pattern.md`, `ef-migrations.md`, `endpoint-organization-pattern.md`

---

> ## Revision 2 (2026-08-26) — period-based enrollment, grade eligibility, on/off status
>
> **Supersedes parts of the original (Revision 1) decisions.** Revision 1 has
> already been **implemented and shipped dark** behind `EnableActivityGroups`
> (Phases 1–5 in `activity-group-enrollment-impl.md`). Revision 2 changes the
> domain model and therefore requires a **new additive migration phase**, not
> edits to the shipped code:
>
> 1. **ActivityGroup is fully period-independent.** The optional `PeriodId` on
>    `activity_groups` is **removed**. A group carries no period association.
> 2. **Group status is a simple on/off flag.** The three-state
>    `ActivityGroupStatus { Active, Suspended, Archived }` is replaced by a
>    boolean `IsActive` (on = accepts enrollment + assignment targeting; off =
>    blocks new enrollment/links, history preserved, toggleable both ways).
>    Throughout this document, prior references to "Suspended"/"Archived" read
>    as "off (`IsActive = false`)".
> 3. **Enrollment (`ActivityGroupMembership`) is period-based.** Each
>    membership row gains a **required** `PeriodId` (FK → `periods.id`). A
>    student enrols in a group *for a specific period*. Multi-membership is
>    still allowed (a student may be active in several groups in the same
>    period). The unique-active constraint becomes per
>    `(tenant, student, group, period)`. `Capacity` is enforced per
>    `(group, period)`.
> 4. **Grade-level eligibility (new).** A group is grade-level independent but
>    declares an explicit set of **eligible `GradeLevels`** (many-to-many
>    `activity_group_grade_levels`). A student may enrol in a group for a
>    period only if their **active grade enrollment for that period** is in the
>    group's eligible grade set. This is the basis for the grade-level-card
>    enrollment landing page (the motivating feature for this revision).
>
> **Readers' guide:** the body below is edited in place to reflect Revision 2
> where it changes semantics (§0 decisions, §2.2, FR-1/3/4/5/7/9/10/12/13/16/22,
> §8.1/§8.2, §8.4, §8.6, plus new FR-39..41). Acceptance criteria and edge cases
> that mention "Suspended"/"Archived" should be read under the on/off model;
> criteria that assume period-independent membership should be read as
> period-scoped. A grade-level enrollment landing page UI is the target
> deliverable and will be specified in detail once this model is locked.
>
> ---

> ## Revision 3 (2026-08-26) — enrollment span on the group + period hierarchy
>
> Introduces the enrollment time-box on the `ActivityGroup` itself and exposes
> a gap in the flat `Period` model:
>
> 1. **Enrollment span on the group.** An `ActivityGroup` declares an
>    `EnrollmentSpan` — `WholeAcademicYear | Termly | Semester | OpenEnded` —
>    plus an optional `EndDate` (nullable; `OpenEnded` ⇒ `EndDate` null). The
>    span declares the granularity at which students enrol and how a membership
>    attaches to a period.
> 2. **Period hierarchy (new dependency — gap).** The shipped `Period` model is
>    flat (Name/Start/End/Status/`NextPeriodId`, one active period per tenant per
>    `active-period-per-tenancy.md`). To support termly/semester spans it MUST
>    be extended with a `PeriodType` (`AcademicYear | Term | Semester`) and a
>    nullable `ParentPeriodId` (term/semester → its academic year), and the
>    "one active period per tenant" invariant MUST be relaxed to "one active
>    *AcademicYear* per tenant" with its term/semester sub-periods active within
>    it. A tenant-level **academic-year division** (terms vs semesters) selects the
>    sub-division in use. These changes are defined in the **periods spec**
>    (`active-period-per-tenancy.md`, to be extended) and are a hard dependency
>    for Rev. 3 spans other than `OpenEnded`.
> 3. **OpenEnded reconciles with period-based enrollment.** For
>    `WholeAcademicYear`/`Termly`/`Semester`, the membership's `PeriodId` is
>    required and points at the matching year/term/semester period (Rev. 2). For
>    `OpenEnded`, `PeriodId` is **nullable** — the membership is continuous,
>    governed by the group's `EndDate` (or until the member exits/is removed).
>    `ActivityGroupMembership.PeriodId` is therefore **nullable** overall.
> 4. **Unique-constraint pitfall (Postgres NULLs).** The Rev. 2 partial unique
>    index `(tenant, student, group, period_id) WHERE status = 0` does **not**
>    enforce uniqueness when `period_id` is NULL (Postgres treats NULLs as
>    distinct). To keep "at most one active membership per (student, group)" for
>    `OpenEnded` groups, use a second partial index `WHERE status = 0 AND
>    period_id IS NULL` on `(tenant, student, activity_group_id)`. Flagged for the
>    implementation phase.
>
> **Readers' guide (cumulative):** the body reflects Rev. 2 *and* Rev. 3. Rev. 3
> edits §0 decisions 13–15, FR-7/10/13 (span + nullable period), new FR-42..46,
> §8.1 (enrollment_span/end_date), §8.2 (period_id nullable + the
> null-uniqueness pitfall), §8.6 (`EnrollmentSpan`), and adds a dependency on
> the extended periods spec.
>
> ---

> ## Revision 4 (2026-08-26) — OpenEnded as an open interval + DateRange span + rollover
>
> Reframes `OpenEnded` and adds a bounded custom-window span with member
> rollover:
>
> 1. **OpenEnded = [StartDate, EndDate null].** Supersedes Rev. 3's
>    "`EndDate` nullable caps `OpenEnded`". An `OpenEnded` group now has an
>    `EnrollmentStartDate` and `EnrollmentEndDate` **always null** — ongoing
>    from the start, no end, no rollover. The fixed-closure-date idea moves to
>    `DateRange`.
> 2. **New `DateRange` span.** A group may define its enrollment as a bounded
>    custom window `[EnrollmentStartDate, EnrollmentEndDate]` not tied to any
>    academic `Period` (membership `PeriodId` null). The window is recorded on
>    each membership (`window_start_date`/`window_end_date`).
> 3. **Rollover at window end.** At a bounded window's end (`DateRange` end, or
>    the period end for period-aligned spans), active **willing** members —
>    those with `AutoRenew = true` (new membership flag, default true) — are
>    re-enrolled into the **next open window**; unwilling members are exited.
>    `OpenEnded` is exempt. The current membership is exited before the next is
>    created, so FR-10 active-uniqueness holds.
> 4. **Open implementation questions (not yet locked):** (a) whether the next
>    `DateRange` window is admin-defined in advance (a sequence of windows) or
>    set at rollover time; (b) whether rollover runs automatically on a schedule
>    or is admin-triggered; (c) the `AutoRenew` consent model (admin-set vs.
>    member-set, and the default). These are flagged for the next refinement
>    pass.
>
> **Readers' guide (cumulative):** the body reflects Rev. 2–4. Rev. 4 edits §0
> decisions 16–18, §2.2, FR-13/42/44/46, adds FR-47..52, §8.1
> (`enrollment_start_date`/`enrollment_end_date`/`auto_renew_default`), §8.2
> (`auto_renew`/`window_start_date`/`window_end_date`), §8.6 (`DateRange`).
>
> ---

> ## Revision 5 (2026-08-26) — rollover logistics locked
>
> Locks the three open questions from the Rev. 4 banner:
>
> 1. **Next `DateRange` window — defined by admin in advance.** A `DateRange`
>    group carries a single next-window slot
>    (`next_enrollment_start_date`/`next_enrollment_end_date`) the admin sets
>    before the current window ends (FR-53). At rollover the next becomes
>    current and the slot is cleared. A full multi-window sequence table is a
>    tracked future enhancement.
> 2. **Rollover trigger — scheduled or admin-forced.** Rollover runs either on
>    a **scheduled** background job at window end, or via an **admin-forced**
>    command that closes the window at the trigger time (FR-54). Both use the
>    same logic and respect `AutoRenew`.
> 3. **`AutoRenew` — default true, admin-set.** Confirmed (FR-49). Member
>    self-service remains a future enhancement (OS-9).
>
> **Readers' guide (cumulative):** the body reflects Rev. 2–5. Rev. 5 adds §0
> decision 19, FR-53/54, updates FR-49/50/51, and §8.1
> (`next_enrollment_start_date`/`next_enrollment_end_date`).
>
> ---

> ## Revision 6 (2026-08-26) — subject/topic → grade/group delivery aligned to the enrollment span
>
> Propagates the enrollment-span refinement (Rev. 3–5) to subject/topic delivery:
>
> 1. **The `GradeSubjectAssignment` bridge gains an optional `PeriodId`.** The
>    bridge stays date-based by default (`PeriodId` null = year-spanning, the
>    current `subject-to-topic-polymorphism.md` decision 2a behavior unchanged);
>    a non-null `PeriodId` aligns a topic assignment to a specific
>    term/semester/academic-year period.
> 2. **Activity-group-owned topics align to the group's `EnrollmentSpan`** —
>    Termly→Term, Semester→Semester, WholeAcademicYear→AcademicYear;
>    OpenEnded/DateRange→`PeriodId` null (date-based window).
> 3. **Grade-owned topics** may be scoped to an AcademicYear or a
>    Term/Semester within the active academic year (term-delivered subjects);
>    null = year-spanning. **Grade enrollment stays AcademicYear-level**
>    (period-hierarchy decision 3) — a term-scoped topic gates *when* the
>    subject is active, not *who* is grade-enrolled.
> 4. **Assignment/subject consistency** — a `SelectedGrades`/`SelectedGroups`
>    assignment's subject must be assigned to the target for a period covering
>    the assignment's effective date (year-spanning active, or period-aligned).
>    Recipient resolution (FR-20) is unchanged.
>
> **Readers' guide (cumulative):** the body reflects Rev. 2–6. Rev. 6 adds §0
> decision 20, §3.8 + FR-55..58, AC-44..46, EC-23..24, and amends
> `subject-to-topic-polymorphism.md` decision 2a (optional `PeriodId` on the
> bridge).
>
> ---

## 0. Decisions locked in this revision

1. **Create a NEW entity pair — do NOT adapt `StudentEnrollment`.** Activity
   groups get their own `ActivityGroup` (group definition) and
   `ActivityGroupMembership` (student↔group link) entities, both in the
   **Students** bounded context. `StudentEnrollment` stays untouched.
   Rationale in §2.3. This keeps the change **additive / non-breaking**
   (NFR-6) and fulfills the already-existing
   `TargetAudienceType.SelectedGroups = 2` placeholder rather than fighting it.
2. **Multi-membership is allowed.** A student MAY be an active member of one or
   more activity groups at the same time. This is the opposite of grade
   enrollment's single-active rule, which is exactly why the two must be
   separate entities (FR-6, FR-9).
3. **Group is period-independent with a simple on/off status (Rev. 2).** An
   `ActivityGroup` carries **no** `PeriodId` and is not associated with a
   `Period`. Its viability is a single boolean `IsActive` (on/off), toggleable
   both ways — replacing the earlier Active/Suspended/Archived lifecycle. The
   group definition outlasts any period; only **enrollment** is period-scoped
   (see decision 11). This satisfies the requirement that activity groups
   outlast grade enrollment while keeping the group definition stable across
   terms.
4. **Audience type is mutually exclusive per assignment.** An assignment picks
   exactly one of `AllStudents | SelectedGrades | SelectedGroups` — matching
   the existing closed-set `TargetAudienceType` enum semantics. A *single*
   assignment simultaneously targeting a grade **and** a group requires a
   unified `AssignmentTarget` table, which is a **breaking change** to the
   existing scalar `Assignment.GradeLevelId` and is therefore **out of scope**
   for this spec (OS-1, OS-2). The system as a whole supports grades **and**
   groups (an assignment may target grades, or groups, or all students).
5. **Assignment↔group is many-to-many** via a new `AssignmentActivityGroup`
   link table in the **Assignments** context (FR-17). `SelectedGrades`
   continues to use the existing scalar `Assignment.GradeLevelId` — unchanged.

6. **Recipient resolution reuses the existing publish pipeline.** Group-targeted
   publish resolves group members → their subscribed contacts →
   `AssignmentRecipient` rows, exactly as grade-targeted publish does today
   (FR-20). No new notification channel.
7. **Hard delete is referentially guarded.** `ActivityGroup.Delete()` mirrors
   `GradeLevel.Delete()` — the repository rejects the delete when the group has
   any `ActivityGroupMembership` row (any status; the `activity_group_id` FK
   is `ON DELETE RESTRICT`, preserving history — NFR-8) OR when a
   `Draft`/`Published` assignment references the group (FR-6, EC-1), throwing
   an `ActivityGroupReferencedException`. The cross-context assignment check
   is obtained via the Assignments-API endpoint in §7.3 — the Students context
   cannot query the Assignments `DbContext` directly.
8. **Group membership validation does NOT apply grade age/gender specs.**
   `ActivityGroupMembership` only checks student existence, tenant match, and
   non-deleted status (FR-16). Age/gender restrictions are a grade-enrollment
   concern and do not apply to extracurricular groups.
9. **Activity Groups use the existing `Subject` entity (polymorphic).**
   Activity Groups do NOT have a separate `ActivityList` entity — instead,
   the existing `Subject` entity is made polymorphic via `OwnerType`
   (GradeLevel | ActivityGroup) and `OwnerId`. An assignment targeting a
   group MUST link to at least one group (FR-23) and MUST have a
   `SubjectId` whose `OwnerType = ActivityGroup` and `OwnerId` matches a
   linked group (per `subject-to-topic-polymorphism.md` FR-15). The
   `Subject` entity carries strands and lessons uniformly for both owner
   types. See `subject-to-topic-polymorphism.md` for the full design.

10. **Grade-level eligibility is an explicit many-to-many (Rev. 2).** A group
    is grade-level independent (no single owning `GradeLevel`) but declares the
    set of `GradeLevel`s it is offered to via a new
    `activity_group_grade_levels` link table (tenant-scoped, audited). A
    student may enrol only if their active grade enrollment for the
    membership's period is in this set. This enables the grade-level-card
    enrollment landing page: each card = one grade, showing the groups that
    grade is eligible for and offering enrollment into them for the active
    period.
11. **Enrollment is period-based (Rev. 2).** `ActivityGroupMembership` gains a
    required `PeriodId`. A student enrols in a group *for a period*. Multi-
    membership is preserved (several groups in the same period). The
    unique-active constraint is per `(tenant, student, group, period)`, and
    `Capacity` is enforced per `(group, period)`. The group definition remains
    period-independent; only the membership row is period-scoped.
12. **On/off status replaces the three-state lifecycle (Rev. 2).** `Archive`
    and `Suspend` are removed in favour of `IsActive`. "Off" blocks new
    enrollment and new assignment links but preserves history and existing
    links (the soft-retire semantics formerly provided by `Archive`). Hard
    delete remains referentially guarded (decision 7). The toggle is reversible.
13. **Enrollment span lives on the group (Rev. 3).** `ActivityGroup` carries an
    `EnrollmentSpan` enum (`WholeAcademicYear | Termly | Semester | OpenEnded`)
    and a nullable `EndDate`. The span declares the enrollment granularity and
    determines which period a membership attaches to; `EndDate` caps an
    `OpenEnded` group (null = truly open until members exit/the group is turned
    off). The span is immutable after creation (changing granularity would
    invalidate existing memberships); only `EndDate` may be extended for an
    `OpenEnded` group.
14. **Period hierarchy is a new hard dependency (Rev. 3).** `Period` MUST gain
    `PeriodType` (`AcademicYear | Term | Semester`) and a nullable
    `ParentPeriodId` (term/semester → academic year), and the per-tenant "one
    active period" invariant MUST become "one active *AcademicYear* with its
    sub-periods active within it". A tenant **academic-year division** (terms vs
    semesters) selects the sub-division. Defined in the periods spec
    (`active-period-per-tenancy.md`, to be extended); activity-group
    `Termly`/`Semester`/`WholeAcademicYear` spans depend on it. `OpenEnded`
    does not.
15. **OpenEnded ⇒ nullable membership `PeriodId` (Rev. 3).** For spanned groups
    the membership's `PeriodId` is required (the matching year/term/semester);
    for `OpenEnded` it is null. `ActivityGroupMembership.PeriodId` is therefore
    nullable. The active-uniqueness invariant (FR-10) must also hold for the
    null-`PeriodId` (OpenEnded) case — see the Postgres NULL pitfall in the Rev.
    3 banner.
16. **OpenEnded is an open interval [StartDate, EndDate null] (Rev. 4).**
    Supersedes Rev. 3's "EndDate nullable caps OpenEnded" framing: an `OpenEnded`
    group has an `EnrollmentStartDate` and `EnrollmentEndDate` **always null** —
    ongoing from the start, no end, no rollover. The earlier fixed-closure-date
    concept is relocated to the bounded `DateRange` span (decision 17).
17. **DateRange span — a group-defined bounded window (Rev. 4).** A group with
    `EnrollmentSpan = DateRange` defines its own enrollment window
    `[EnrollmentStartDate, EnrollmentEndDate]`, not tied to any academic
    `Period` (membership `PeriodId` null; the window is recorded on each
    membership as `window_start_date`/`window_end_date`). At `EnrollmentEndDate`
    the window closes and rollover (decision 18) applies.
18. **Rollover & AutoRenew (Rev. 4).** Memberships carry an `AutoRenew` flag
    ("willing"). At a bounded window's end (`DateRange` end, or the period end
    for period-aligned spans), active members with `AutoRenew = true` are
    re-enrolled into the next open window (a new active membership; the old one
    is exited at the window end); members with `AutoRenew = false` are exited
    without re-enrolling. `OpenEnded` is exempt. The current membership is
    always exited before the next is created, preserving the FR-10
    active-uniqueness invariant. How/when the next window is defined and whether
    rollover is admin-triggered or scheduled is an open implementation question
    (Rev. 4 banner, item 4).
19. **Rollover logistics locked (Rev. 5).** Resolves the three Rev. 4 open
    questions: (a) the next `DateRange` window is **defined by an admin in
    advance** via a single next-window slot on the group (FR-53); (b) rollover
    is triggered either **scheduled** (background job at window end) or
    **admin-forced** (explicit command, closes the window at trigger time)
    (FR-54); (c) `AutoRenew` defaults to **true** and is **admin-set** (FR-49).
    A full multi-window sequence table remains a tracked future enhancement.
20. **Subject/topic delivery aligns to the enrollment span (Rev. 6).** The
    `GradeSubjectAssignment` bridge (Topic → `GradeLevel` OR `ActivityGroup`,
    `subject-to-topic-polymorphism.md` decision 2a: date-based, not period-bound)
    gains an **optional** `PeriodId`. Null = the current year-spanning
    date-based behavior (unchanged); non-null = the topic is delivered during a
    specific term/semester/academic-year period, aligning subject delivery to
    the enrollment-span granularity introduced in Rev. 3–5. This is additive
    and amends the polymorphism spec's decision 2a (tracked there). Grade
    enrollment stays `AcademicYear`-level — a term/semester-scoped topic gates
    *when* the subject is active, not *who* is enrolled in the grade.

These decisions resolve the user's explicit question ("adapt grade enrollment
or create a new entity?") in favour of **a new entity**, with reasoning in §2.3.

## 1. Goal

Introduce **extracurricular activity groups** as a second grouping mechanism
alongside grade levels, let students belong to one or more such groups
(independent of their single grade enrollment), and let assignments be targeted
at the members of those groups — scoped to specific **subjects** (the
existing `Subject` entity, now polymorphic). Activity groups may outlast
grade enrollment periods. Unlike grade enrollment (single-active), a student
MAY belong to one or more activity groups simultaneously. Activity groups
use the polymorphic `Subject` entity for categorisation — see
`subject-to-topic-polymorphism.md`.

---

## 2. Context

### 2.1 Why this feature exists

School-Collab currently enrols students in **grade levels**, which act as the
grouping unit for assignments. An assignment's `TargetAudienceType` can be
`AllStudents` or `SelectedGrades` (backed by `Assignment.GradeLevelId`).
However, schools also run **extracurricular activity groups** (clubs, sports
teams, debate, music, etc.) that are *not* grade levels. Today there is no way
to assign work to those groups: the `TargetAudienceType.SelectedGroups = 2`
enum value exists in `TargetAudienceType.cs` but has **no backing entity, no
data model, and no UI**. It is an unfulfilled placeholder.

### 2.2 The differences from grade enrollment (Rev. 2)

1. **Group lifecycle vs. enrollment window.** The `ActivityGroup` definition is
   **period-independent** (no `PeriodId`, simple on/off `IsActive`) and declares
   an `EnrollmentSpan` (`WholeAcademicYear | Termly | Semester | DateRange |
   OpenEnded`). Period-aligned spans attach each membership to a year/term/
   semester period; `DateRange` is a group-defined bounded window
   `[EnrollmentStartDate, EnrollmentEndDate]`; `OpenEnded` is a continuous
   interval from `EnrollmentStartDate` with no end. At a bounded window's end,
   active and willing (`AutoRenew`) members roll into the next open window
   (FR-50). This separates the stable group definition from window-scoped (or
   continuous) roster participation.
2. **Cardinality.** A student holds **one** active grade enrollment at a time
   (enforced by the DB unique index `(tenant_id, student_id, period_id)` in
   `StudentEnrollmentConfiguration`). Activity groups require
   **multi-membership**: a student may be in the chess club, the choir, and the
   robotics team **in the same period** simultaneously. Reusing
   `StudentEnrollment` would require deleting its unique index and its
   single-active specification — destroying the grade invariant to accommodate
   the group invariant.
3. **Grade eligibility (new).** A group is grade-level independent but declares
   an explicit set of eligible `GradeLevel`s. Grade enrollment's
   `AgeRangeSpecification`/`GenderRestrictionSpecification` do **not** apply to
   activity enrollment (FR-16); eligibility is determined by the student's
   active grade-for-period membership in the group's eligible grade set.

### 2.3 Decision: adapt vs. new entity

**Adapting `StudentEnrollment`** would mean: making `GradeLevelId` nullable,
adding a polymorphic target column, *removing* the
`(tenant_id, student_id, period_id)` unique index, *removing* the
`SingleActiveEnrollmentSpecification`, and type-branching every enrollment
query/spec on "is this a grade or a group?" It would also drag grade-only
guard clauses (`AgeRangeSpecification`, `GenderRestrictionSpecification`) into
a code path where they must be conditionally skipped. This is a **breaking
change** to an existing entity, its DB schema, its unique constraint, its
specification chain, and its existing tests — an explicit escalation trigger
under the bounded-autonomy rules, with no compensating benefit.

**Creating a new entity pair** (`ActivityGroup` + `ActivityGroupMembership`)
preserves every grade-enrollment invariant unchanged, gives groups their own
multi-membership + period-independent rules, and is purely additive (new
tables, one new optional link table, one new optional assignment field). It
also turns the dormant `SelectedGroups` enum value into a real feature.

**Decision: create new entities (Option B).** See §0 decision 1.

### 2.4 Success looks like

- An admin can create an activity group, add/remove students (multiple at once,
  multi-membership), and the group persists across periods.
- A teacher can create an assignment with `TargetAudienceType = SelectedGroups`,
  link one or more groups, optionally scope to specific subjects (via the
  polymorphic `Subject` entity), publish it, and only the active members
  of those groups (or specifically-scoped subjects) receive it via their
  subscribed contacts.
- Grade enrollment and its single-active invariant are completely unaffected.
- The student Detail page shows the student's activity groups and lets the
  admin join/leave groups from there.

---

## 3. Functional Requirements

> RFC 2119 keywords (MUST / MUST NOT / SHOULD / MAY) are used precisely.
> Each requirement is atomic and testable. IDs: `FR-N`.

### 3.1 Activity group lifecycle

- **FR-1** — The system MUST allow a tenant admin to create an `ActivityGroup`
  with a unique-within-tenant `Name`, an optional `Description`, an optional
  `Category` (free text, max 100 chars), an optional integer `Capacity` (max
  members per period — Rev. 2), and an explicit set of eligible `GradeLevel`s
  (FR-39). The group carries **no** `PeriodId` (Rev. 2).
- **FR-2** — `ActivityGroup` MUST be a strict tenant entity
  (`ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion`), inheriting the
  tenant via `TenantEntityTypeConfigurationBase<T>` exactly as `GradeLevel`
  does.
- **FR-3** — `ActivityGroup` MUST have a simple on/off `IsActive` flag (Rev. 2,
  default on) independent of `PeriodStatus`. A group MUST be creatable as on
  (`IsActive = true`) without an active period existing.
- **FR-4 (Rev. 2)** — `ActivityGroup` is fully **period-independent**: it
  carries no `PeriodId` and is not associated with any `Period`. The group
  definition is stable across periods; only enrollment (membership) is
  period-scoped (FR-7, decision 11).
- **FR-5** — The system MUST support `Update` (name, description, category,
  capacity, eligible grade set, `IsActive`) and a referentially-guarded `Delete`
  for `ActivityGroup`, mirroring `GradeLevel.Update`/`GradeLevel.Delete`.
- **FR-6** — `Delete` MUST be rejected (throwing
  `ActivityGroupReferencedException`) when EITHER of the following holds:
  (a) the group has **any** `ActivityGroupMembership` row (Active, Exited, or
  Removed) — the `activity_group_id` FK is `ON DELETE RESTRICT` so membership
  history is preserved (NFR-8); OR
  (b) an assignment whose `Status` is `Draft` or `Published` references the
  group. Because assignments live in the **Assignments** bounded context
  (`assignment_activity_groups` is an operational ref with no cross-context DB
  FK — §8.3), `DeleteActivityGroupHandler` (Students context) MUST obtain
  this check via the existing Assignments-API endpoint
  `GET /api/activity-groups/{id}/assignments` (§7.3) through a cross-context
  port (e.g. `IActivityGroupAssignmentQuery`, implemented in `Students.Api`
  as an HTTP client, mirroring the existing `StudentsContactResolver` pattern
  in `Assignments.Api`) and reject if any returned assignment is
  `Draft`/`Published`. The check is **fail-closed**: if the Assignments API
  is unreachable the delete is rejected. To retire a referenced group, use
  `Archive` (FR-3, Q3).

### 3.2 Activity group membership

- **FR-7** — The system MUST allow adding a student to an `ActivityGroup`,
  creating an `ActivityGroupMembership` (`StudentId`, `ActivityGroupId`,
  `PeriodId` (required for spanned groups; NULL for `OpenEnded` — Rev. 3),
  `JoinedOn`, optional `ExitedOn`, `MembershipStatus`).
- **FR-8** — `MembershipStatus` MUST be `Active=0, Exited=1, Removed=2`.
- **FR-9** — A student MUST be permitted to be an active member of **one or
  more** activity groups **in the same period** simultaneously (multi-
  membership). This MUST NOT be constrained by the student's single-active grade
  enrollment or by any single-active rule.
- **FR-10** — The system MUST enforce at most one **active** membership per
  `(tenant_id, student_id, activity_group_id, period_id)` via a partial unique
  index on rows where `Status = Active` (Rev. 2 — period-scoped). For the
  `OpenEnded` case (`period_id` NULL) it MUST enforce at most one active
  membership per `(tenant_id, student_id, activity_group_id)` via a second
  partial index `WHERE status = 0 AND period_id IS NULL` (Rev. 3 — Postgres
  treats NULL as distinct in a plain unique index). Re-joining a previously
  exited group in the **same period** (or, for OpenEnded, while still active)
  MUST set the old row to `Exited`/`Removed` (or reuse via a new active row)
  without violating the unique constraint; enrolling in a **new period** always
  creates a fresh membership row.
- **FR-11** — The system MUST reject membership for a `Student` that is
  soft-deleted (`IsDeleted = true`), belongs to a different tenant, or does not
  exist.
- **FR-12** — The system MUST reject new membership for an `ActivityGroup`
  that is **off** (`IsActive = false`, Rev. 2). Existing memberships are
  preserved (history).

- **FR-13** — If `ActivityGroup.Capacity` is set, the system MUST reject a
  new active membership with `GroupAtCapacityException` when the relevant active
  count is >= `Capacity`: per `(group, period)` when the membership's `PeriodId`
  is set (period-aligned spans — Rev. 2) and per group overall when `PeriodId` is
  null (`OpenEnded` and `DateRange` — Rev. 4, FR-46). If `Capacity` is null, no
  limit is enforced.
- **FR-14** — The system MUST support removing a member (`Remove`) and a member
  exiting (`Exit`), both setting `ExitedOn` and moving `Status` to `Removed` or
  `Exited` respectively, and emitting a domain event.
- **FR-15** — `ActivityGroupMembership` MUST be a strict tenant entity with
  `IHasRowVersion` (PostgreSQL `xmin`) and audit properties, consistent with
  `StudentEnrollment`.
- **FR-16** — Membership validation MUST NOT apply `AgeRangeSpecification` or
  `GenderRestrictionSpecification` (those are grade-enrollment-only).
  Eligibility is by grade set, not age/gender (FR-40).

- **FR-39 (Rev. 2)** — An `ActivityGroup` MUST declare an explicit set of
  eligible `GradeLevel`s via the `activity_group_grade_levels` link table
  (tenant-scoped, audited). The set MAY be empty (no grade restriction — any
  actively-enrolled student in the period may join) or non-empty (only
  students whose active grade-for-period is in the set may join).
- **FR-40 (Rev. 2)** — When adding a membership, the system MUST validate
  that the student's active `StudentEnrollment` for the membership's `Period`
  has a `GradeLevelId` that is in the group's eligible grade set (when the set
  is non-empty); otherwise the add MUST be rejected with a clear validation
  error. When the set is empty, any actively-enrolled student in that period is
  eligible.
- **FR-41 (Rev. 2)** — The system MUST support updating a group's eligible
  grade set (replace-set semantics) via `UpdateActivityGroup` (FR-5), emitting a
  domain event on change.

- **FR-42 (Rev. 3/4)** — An `ActivityGroup` MUST declare an `EnrollmentSpan`
  (`WholeAcademicYear | Termly | Semester | DateRange | OpenEnded`; Rev. 4 adds
  `DateRange`) and, for `DateRange`/`OpenEnded`, `EnrollmentStartDate` plus
  `EnrollmentEndDate` (required for `DateRange`; always null for `OpenEnded`).
  Period-aligned spans derive their window from the linked `Period`. The span
  is immutable after creation (changing granularity would invalidate existing
  memberships); for `DateRange` the admin advances successive windows by
  updating `EnrollmentStartDate`/`EnrollmentEndDate` (FR-51).
- **FR-43 (Rev. 3)** — For a spanned group (`WholeAcademicYear`/`Termly`/
  `Semester`), a membership MUST attach to a `Period` whose `PeriodType`
  matches the span (`AcademicYear`/`Term`/`Semester`) and which belongs to the
  tenant's active academic year. Adding a membership MUST be rejected with a
  clear error when no such period exists or is not active.
- **FR-44 (Rev. 3/4)** — For an `OpenEnded` group, `EnrollmentEndDate` is
  always null (Rev. 4) and a membership's `PeriodId` is NULL (continuous
  membership). The membership stays active until the member exits/is removed or
  the group is turned off. OpenEnded has no window end and no rollover (FR-50).
- **FR-45 (Rev. 3)** — The group's `EnrollmentSpan` MUST be compatible with
  the tenant's academic-year division (terms vs semesters): a `Termly` span requires
  a terms-based framework; a `Semester` span requires a semesters-based
  framework. Creating a group with an incompatible span MUST be rejected.
  `WholeAcademicYear` and `OpenEnded` are framework-agnostic.
- **FR-46 (Rev. 3/4)** — `Capacity` is enforced per `(group, period)` when the
  membership's `PeriodId` is set (period-aligned spans) and **per group
  overall** when `PeriodId` is null (`OpenEnded` and `DateRange` — count all
  active members of the group regardless of window).
- **FR-47 (Rev. 4)** — An `ActivityGroup` with `EnrollmentSpan = DateRange` MUST
  define a bounded enrollment window via `EnrollmentStartDate` +
  `EnrollmentEndDate` (both required). Memberships carry a null `PeriodId` and
  belong to that window (recorded as `window_start_date`/`window_end_date` on
  the membership). The window is not tied to any academic `Period`.
- **FR-48 (Rev. 4)** — `EnrollmentSpan = OpenEnded` defines an interval from
  `EnrollmentStartDate` with `EnrollmentEndDate` **always null** (Rev. 4
  supersedes Rev. 3's nullable-EndDate framing). Memberships are continuous
  (null `PeriodId`, `window_end_date` null) until the member exits/is removed or
  the group is turned off. OpenEnded has no window end and no rollover.
- **FR-49 (Rev. 4/5)** — Each `ActivityGroupMembership` MUST carry an `AutoRenew`
  boolean. Default = the group's `AutoRenewDefault`, which defaults to **true**
  (locked Rev. 5). "Willing" = `AutoRenew = true`. The flag is **admin-set**
  (locked Rev. 5) and settable at any time while the membership is active;
  member self-service is a future enhancement (OS-9).
- **FR-50 (Rev. 4/5)** — **Rollover.** At a bounded enrollment window's end
  (`DateRange.EnrollmentEndDate`, or the period end for `WholeAcademicYear`/
  `Termly`/`Semester`), the system MUST, for each **active** member: if
  `AutoRenew = true` AND a next open window exists, create a new active
  membership in the next window and exit the current one (`ExitedOn` = window
  end); if `AutoRenew = false`, exit the current membership at window end
  without re-enrolling. `OpenEnded` is exempt (no window end). Rollover MUST
  exit the current membership before creating the next, so the FR-10
  active-uniqueness invariant is never violated. Rollover is triggered either
  **scheduled** (a background job at window end — FR-54) or **admin-forced**
  (an explicit rollover command — FR-54).
- **FR-51 (Rev. 4/5)** — **Next window.** For period-aligned spans the next
  window is the next `Period` of the matching `PeriodType` in the tenant's
  academic year. For `DateRange` the next window MUST be **defined by an admin
  in advance** via the group's `next_enrollment_start_date`/
  `next_enrollment_end_date` (FR-53). At rollover the next window becomes the
  current window and the next slot is cleared. If no next window is defined at
  rollover, all remaining active members are exited at window end (no
  re-enrollment); they may re-enrol manually when a new window opens.
- **FR-52 (Rev. 4)** — A window whose `EnrollmentEndDate` has passed MUST NOT
  accept new enrollments; new enrollments attach to the current or next open
  window only.
- **FR-53 (Rev. 5)** — **Next DateRange window defined in advance.** An admin
  MUST be able to set a `DateRange` group's next window
  (`next_enrollment_start_date`/`next_enrollment_end_date`) before the current
  window ends. At most one next window is held at a time (a single advance
  slot; a full multi-window sequence table is a tracked future enhancement).
  Setting the next window MUST be rejected if its start is not on/after the
  current window's end.
- **FR-54 (Rev. 5)** — **Rollover trigger.** Rollover MUST be runnable in two
  modes: (a) **scheduled** — a background job that runs rollover for every
  group whose current window has ended (at or after `EnrollmentEndDate` / period
  end); (b) **admin-forced** — an explicit command to roll a specific group
  immediately, which closes the current window at the trigger time (`ExitedOn`
  = trigger date) and enrolls `AutoRenew = true` members into the next window
  if one is defined. Forced rollover of an `OpenEnded` group is a no-op (no
  window end). Both modes use the same rollover logic (FR-50) and respect
  `AutoRenew` (FR-49).

### 3.3 Assignment targeting

- **FR-17** — The Assignments context MUST provide an `AssignmentActivityGroup`
  link entity (`AssignmentId`, `ActivityGroupId`, tenant, audit) implementing a
  many-to-many relationship between `Assignment` and `ActivityGroup`.
- **FR-18** — When `Assignment.TargetAudienceType = SelectedGroups`, the
  assignment MUST target the members of the group(s) linked via
  `AssignmentActivityGroup`. The audience type MUST remain mutually exclusive
  per assignment (one of `AllStudents | SelectedGrades | SelectedGroups`),
  matching the existing closed-set enum.
- **FR-19** — `SelectedGrades` MUST continue to use the existing scalar
  `Assignment.GradeLevelId` unchanged. This spec MUST NOT alter
  `Assignment.GradeLevelId`, its index, or the `SelectedGrades` code path.
- **FR-20** — At publish time, for a `SelectedGroups` assignment the system
  MUST resolve the active members of each linked group, then resolve each
  member's subscribed contacts, and create `AssignmentRecipient` rows — reusing
  the existing recipient-resolution pipeline used for grade-targeted publishes.
- **FR-21** — The system MUST reject linking a group from a different tenant
  than the assignment (tenant mismatch → `ArgumentException`/400).
- **FR-22** — The system MUST reject linking an **off** (`IsActive = false`,
  Rev. 2) group to an assignment.
- **FR-23** — An assignment with `TargetAudienceType = SelectedGroups` MUST
  have at least one linked group before it can be published; publishing with
  zero linked groups MUST be rejected with a clear validation error (EC-7).

### 3.4 Reads / queries

- **FR-24** — The system MUST expose queries: list activity groups
  (tenant-scoped, filterable by status/period), list active members of a group,
  list the groups a given student is an active member of, and list the activity
  lists of a given group.
- **FR-25** — The system MUST expose a query listing assignments targeting a
  given activity group.
- **FR-26** — Group and membership list endpoints MUST be tenant-filtered via
  the global tenant filter (`global-tenant-filter.md` §3.2 Strict).

### 3.5 Domain events

- **FR-27** — Creating a group, adding/removing/exiting a member, and
  archiving/suspending a group MUST each emit a corresponding domain event
  (e.g. `ActivityGroupCreatedEvent`, `ActivityGroupMemberAddedEvent`) following
  the existing `IDomainEvent` + `_domainEvents` + `ClearDomainEvents()` pattern.

### 3.6 Student Detail page UI

- **FR-28** — The student Detail page (`Detail.razor`) MUST display an
  "Activity Groups" section below the existing Enrollments section, listing the
  groups the student is an active member of. Each row MUST show the group name,
  joined-on date, and status (`Active` badge or `Exited`/`Removed`).
- **FR-29** — The "Activity Groups" section header MUST contain a "Join Group"
  accent button that opens a group-membership dialog.
- **FR-30** — The group-membership dialog MUST present a searchable, pickable
  list of available activity groups in the same tenant (Active status, not at
  capacity, excluding groups the student is already an active member of). The
  user MUST be able to select one or more groups and submit to join them in a
  single operation. The dialog MUST handle partial failures: accepted groups
  create memberships, failed ones are reported per-group.
- **FR-31** — Each active membership row MUST include a "Leave" lightweight
  button that exits the student from that group.
- **FR-32** — The section MUST show an appropriate empty state message when
  the student has no active memberships.

### 3.7 Activity-group subjects (polymorphic Subject)

> Activity Groups do NOT have a separate `ActivityList` entity. The existing
> `Subject` entity is made polymorphic (`OwnerType` + `OwnerId`) to support
> both grade-level and activity-group ownership. See
> `subject-to-topic-polymorphism.md` (§0 decisions 1-7, FR-1..12) for the
> full design.

- **FR-33 (superseded)** — Replaced by `subject-to-topic-polymorphism.md`
  FR-1..4. The `Subject` entity gains `OwnerType` (GradeLevel | ActivityGroup),
  `OwnerId`, optional `PeriodId`, `Description`, `DefaultStrandId`, and
  `DefaultLessonId`. `Code` and `CodedValueId` become nullable.
- **FR-34 (superseded)** — Replaced by `subject-to-topic-polymorphism.md`
  FR-13..15. An assignment with `SelectedGroups` MUST have a `SubjectId`
  whose `OwnerType = ActivityGroup` and `OwnerId` matches a linked group.
  The `AssignmentActivityGroup` link table is **retained** (it is NOT
  eliminated — `subject-to-topic-polymorphism.md` FR-15/FR-17 keep it; only
  the separate `AssignmentActivityList` link table from the old §7.5 design
  is eliminated by that spec).
- **FR-35 (superseded)** — Replaced by `subject-to-topic-polymorphism.md`
  §7.1. The Subjects admin page gains an ActivityGroup filter. The existing
  Subjects admin page (`Subjects.razor`, `SubjectCreateDialog.razor`) is
  extended rather than introducing a separate activity-list UI (the
  standalone `ActivityList` concept is eliminated by
  `subject-to-topic-polymorphism.md`).

### 3.8 Subject/topic → grade/group delivery aligned to the enrollment span (Rev. 6)

> The `GradeSubjectAssignment` bridge (Topic → `GradeLevel` OR `ActivityGroup`,
> per `subject-to-topic-polymorphism.md` decision 2a: date-based, not
> period-bound) is refined to align subject/topic delivery to the
> enrollment-span granularity introduced in Rev. 3–5. This amends the
> polymorphism spec's decision 2a (optional `PeriodId` on the bridge); the
> bridge stays date-based by default.

- **FR-55 (Rev. 6)** — The `GradeSubjectAssignment` bridge MUST gain an
  optional nullable `PeriodId` (FK → `periods`). `PeriodId = null` = the
  current date-based, year-spanning assignment (polymorphism decision 2a
  unchanged); a non-null `PeriodId` = the topic is delivered during that
  specific term/semester/academic-year period. The amendment is additive.
- **FR-56 (Rev. 6)** — For an **activity-group-owned** topic
  (`ActivityGroupId` set), the bridge `PeriodId`, when set, MUST match the
  group's `EnrollmentSpan`: `Termly` → `Term`, `Semester` → `Semester`,
  `WholeAcademicYear` → `AcademicYear`; `OpenEnded`/`DateRange` → `PeriodId`
  null (date-based window, since these spans carry no period).
- **FR-57 (Rev. 6)** — For a **grade-owned** topic (`GradeLevelId` set), the
  bridge `PeriodId`, when set, MUST be an `AcademicYear` or a `Term`/`Semester`
  within the tenant's active academic year. `PeriodId = null` = year-spanning
  delivery to all grade-enrolled students (current behavior). Grade enrollment
  stays `AcademicYear`-level (period-hierarchy decision 3) — a
  term/semester-scoped topic gates **when** the subject is active, not **who**
  is grade-enrolled.
- **FR-58 (Rev. 6)** — **Assignment/subject consistency.** A `SelectedGrades`
  assignment's subject MUST be assigned to the target grade for a period
  covering the assignment's effective date (a null-`PeriodId` year-spanning
  assignment active on that date, or a period-aligned assignment whose period
  contains the date); otherwise the assignment is rejected. A `SelectedGroups`
  assignment's subject MUST be assigned to a linked group for the relevant
  enrollment period (date-based or period-aligned per FR-56). Recipient
  resolution (FR-20) is unchanged — this refinement gates the
  subject/period validity, not recipient selection.

---

## 4. Non-Functional Requirements

> All thresholds are measurable. IDs: `NFR-N`.

- **NFR-1 (Performance — list members)** — Listing the active members of a
  group MUST return p95 < 300 ms for a group of 5,000 members, measured against
  a warmed PostgreSQL instance on the standard dev hardware.
- **NFR-2 (Performance — recipient resolution)** — Resolving recipients for a
  `SelectedGroups` publish MUST complete p95 < 2 s for 10,000 total active
  members across the linked groups.
- **NFR-3 (Indexing)** — All hot-path indexes MUST lead with `tenant_id`,
  matching the existing convention (`ix_student_enrollments_tenant_*`).
  Specifically: `(tenant_id, activity_group_id, status)` on memberships and
  `(tenant_id, activity_group_id)` on the assignment link.
- **NFR-4 (Concurrency)** — `ActivityGroup` and `ActivityGroupMembership` MUST
  use PostgreSQL `xmin` row versioning via `IHasRowVersion` +
  `ConfigurePostgresRowVersion()`. A conflicting write MUST throw the existing
  `ConcurrencyException`.
- **NFR-5 (Tenancy)** — All group/membership/link reads and writes MUST be
  strict-tenant-filtered (`global-tenant-filter.md` §3.2 Strict). No
  cross-tenant group or membership access is permitted.
- **NFR-6 (Non-breaking migration)** — The migration MUST be purely additive:
  new tables `activity_groups`, `activity_group_memberships`,
  `assignment_activity_groups`;
  no alteration, drop, or type change to `student_enrollments`, `assignments`,
  or any existing column/index. The existing `NoUncommittedModelChanges` guard
  MUST continue to pass for both contexts.
- **NFR-7 (Auditability)** — `CreatedAt` / `UpdatedAt` MUST be present and
  populated on all new entities.
- **NFR-8 (Deletion semantics)** — `ActivityGroup` uses a **referentially
  guarded hard delete** (like `GradeLevel`): delete is blocked when any
  membership row exists (the `activity_group_id` FK is `ON DELETE RESTRICT`,
  preserving history) or when a `Draft`/`Published` assignment references the
  group (FR-6). `ActivityGroupMembership` uses **status transitions**
  (`Active → Exited|Removed`) plus `ExitedOn`; membership rows are not
  hard-deleted in normal operation (preserving history). To retire a group
  with history or live assignment links, use `Archive` (FR-3, Q3).
- **NFR-9 (Migration guard)** — A `NoUncommittedModelChanges` unit test MUST be
  added/updated for `StudentsDbContext` and `AssignmentsDbContext` per
  `ef-migrations.md`.
- **NFR-10 (Accessibility)** — Group management UI MUST reuse existing FluentUI
  components, be fully keyboard navigable, and use ARIA labels consistent with
  the `GradeLevels` page. WCAG 2.1 AA target (inherited from the app baseline).
- **NFR-11 (Feature flag)** — The group-management surface MUST be gated behind
  a centralized feature flag `FEATURE:EnableActivityGroups` (fan-out via
  AppHost `Parameters`, per `centralized-feature-flags`), defaulting OFF, so
  the feature ships dark.

## 5. Acceptance Criteria

> Given / When / Then. Every criterion references at least one `FR-*` or
> `NFR-*`. IDs: `AC-N`.

- **AC-1 (create group — Rev. 2)** — **Given** an admin in tenant T with
  `FEATURE:EnableActivityGroups` on, **When** they create an `ActivityGroup`
  named "Chess Club" with `EnrollmentSpan = OpenEnded` and no eligible-grade
  restriction, **Then** a group row is persisted with `IsActive = true`,
  `TenantId = T`, **no** `PeriodId` (Rev. 2), and an
  `ActivityGroupCreatedEvent` is raised. *(FR-1, FR-2, FR-3, FR-42, FR-27)*
- **AC-2 (create without active period)** — **Given** tenant T has **no**
  active `Period`, **When** the admin creates a group, **Then** creation
  succeeds (no period required). *(FR-3, FR-4)*
- **AC-3 (duplicate group name rejected)** — **Given** tenant T already has a
  group "Chess Club", **When** the admin creates another group "Chess Club" in
  T, **Then** the request is rejected with a 409/validation error. *(FR-1)*
- **AC-4 (multi-membership allowed)** — **Given** student S is an active member
  of group G1, **When** S is added to group G2, **Then** both memberships are
  `Active` and no exception is raised. *(FR-7, FR-9)*
- **AC-5 (duplicate active rejected)** — **Given** student S is an active
  member of group G, **When** S is added to G again while still active,
  **Then** the request is rejected by the partial unique constraint. *(FR-10)*
- **AC-6 (rejoin after exit)** — **Given** student S exited group G
  (`Status = Exited`), **When** S is re-added to G, **Then** a new active
  membership is created and the unique constraint is not violated. *(FR-8,
  FR-10, FR-14)*
- **AC-7 (deleted student rejected)** — **Given** student S is soft-deleted,
  **When** S is added to any group, **Then** the request is rejected. *(FR-11)*
- **AC-8 (off group blocks membership — Rev. 2)** — **Given** group G is
  **off** (`IsActive = false`), **When** a student is added to G, **Then** the
  request is rejected; existing memberships are preserved. *(FR-12)*
- **AC-9 (capacity enforced)** — **Given** group G has `Capacity = 20` and 20
  active members, **When** a 21st student is added, **Then** the request is
  rejected with `GroupAtCapacityException`. *(FR-13)*
- **AC-10 (null capacity = unlimited)** — **Given** group G has
  `Capacity = null`, **When** more than any fixed number of students are added,
  **Then** all are accepted. *(FR-13)*
- **AC-11 (no age/gender check)** — **Given** a student whose age/gender would
  fail the grade `AgeRangeSpecification`/`GenderRestrictionSpecification`,
  **When** the student is added to an activity group, **Then** the membership is
  accepted (those specs are not applied). *(FR-16)*
- **AC-12 (assignment targets groups)** — **Given** an assignment A with
  `TargetAudienceType = SelectedGroups` linked to groups G1 and G2, **When** A
  is published, **Then** `AssignmentRecipient` rows are created only for the
  active members of G1 ∪ G2 who have subscribed contacts. *(FR-17, FR-18,
  FR-20)*
- **AC-13 (publish with zero groups rejected)** — **Given** assignment A has
  `TargetAudienceType = SelectedGroups` but no linked groups, **When** A is
  published, **Then** the publish is rejected with a validation error.
  *(FR-23, EC-7)*
- **AC-14 (cross-tenant link rejected)** — **Given** assignment A in tenant T1,
  **When** a group from tenant T2 is linked to A, **Then** the link is
  rejected. *(FR-21, NFR-5)*
- **AC-15 (off group link rejected — Rev. 2)** — **Given** group G is **off**
  (`IsActive = false`), **When** G is linked to an assignment, **Then** the
  link is rejected. *(FR-22)*
- **AC-16 (grade path unchanged)** — **Given** an assignment with
  `TargetAudienceType = SelectedGrades`, **When** it is created/published,
  **Then** behaviour is identical to today (`GradeLevelId` used, no group
  involvement). *(FR-19, NFR-6)*

- **AC-17 (referential delete guard — assignments)** — **Given** group G is
  referenced by a `Draft` or `Published` assignment, **When** the admin
  deletes G, **Then** the delete is rejected with
  `ActivityGroupReferencedException` (the Students delete handler obtains the
  check via the Assignments-API endpoint in §7.3 — the Students context
  cannot query the Assignments `DbContext` directly). *(FR-6, FR-17)*
- **AC-18 (delete guard on membership history)** — **Given** group G has
  any membership row (Active, Exited, or Removed) and is not referenced by
  any assignment, **When** the admin deletes G, **Then** the delete is
  rejected with `ActivityGroupReferencedException` (the `activity_group_id`
  FK is `ON DELETE RESTRICT`, preserving history). *(FR-6, NFR-8)*
- **AC-19 (group outlasts periods — Rev. 2)** — **Given** group G
  (period-independent, `IsActive = true`), **When** periods transition to
  `Completed`/`Archived`, **Then** G stays on and the group definition carries no
  `PeriodId`; enrollment is period/window-scoped per membership (Rev. 3/4), so
  a completed period simply stops accepting new enrollments — the group itself
  is unaffected. *(FR-3, FR-4, FR-10, EC-8)*
- **AC-20 (list queries tenant-scoped)** — **Given** tenants T1 and T2 each
  have groups, **When** an admin in T1 lists groups, **Then** only T1's groups
  are returned. *(FR-24, FR-26, NFR-5)*
- **AC-21 (performance — list members)** — **Given** a group with 5,000 active
  members, **When** the members list endpoint is called, **Then** p95 response
  time is < 300 ms. *(NFR-1, NFR-3)*
- **AC-22 (performance — recipient resolution)** — **Given** a
  `SelectedGroups` assignment linked to groups totalling 10,000 active members,
  **When** it is published, **Then** recipient resolution completes p95 < 2 s.
  *(NFR-2)*
- **AC-23 (concurrency conflict)** — **Given** two concurrent edits to the same
  membership, **When** both are saved, **Then** the second throws
  `ConcurrencyException`. *(FR-15, NFR-4)*
- **AC-24 (feature flag off = dark)** — **Given** `FEATURE:EnableActivityGroups`
  is OFF, **When** the admin navigates to the groups route, **Then** the surface
  is hidden/403 and no group endpoints are reachable. *(NFR-11)*
- **AC-25 (group update)** — **Given** an existing `Active` group "Chess Club"
  in tenant T, **When** the admin updates its name to "Chess Club Advanced" and
  sets `Capacity = 30`, **Then** the persisted row reflects the new name and
  capacity with `UpdatedAt` bumped and an `ActivityGroupUpdatedEvent` raised.
  *(FR-5, FR-27)*
- **AC-26 (list assignments targeting a group)** — **Given** group G is linked
  to assignments A1 and A2 (both `SelectedGroups`), **When** the admin queries
  `/api/activity-groups/{G}/assignments`, **Then** A1 and A2 are returned and
  no assignments from other tenants appear. *(FR-25, FR-26, NFR-5)*
- **AC-27 (detail page shows activity groups)** — **Given** student S is an
  active member of groups G1 and G2, **When** the admin views S's Detail page,
  **Then** the page shows an "Activity Groups" section listing G1 and G2 with
  their status badges and no Enrollments-section interference. *(FR-28,
  NFR-10)*
- **AC-28 (join group button opens dialog)** — **Given** the admin is on the
  student Detail page, **When** they click "Join Group", **Then** a dialog
  opens listing available groups (Active, not-at-capacity, not already joined).
  *(FR-29, NFR-10)*
- **AC-29 (multi-join in single operation)** — **Given** the dialog shows groups
  G1, G2, G3, **When** the admin selects G1 and G3 and submits, **Then** both
  memberships are created, the student becomes an active member of both and
  the section refreshes. *(FR-30)*
- **AC-30 (leave group from row action)** — **Given** the student is an active
  member of group G on the Detail page, **When** the admin clicks "Leave" on
  G's row, **Then** the membership status transitions to `Exited` with
  `ExitedOn` set and the section refreshes. *(FR-31, FR-14)*
- **AC-31 (section hidden when flag OFF)** — **Given**
  `FEATURE:EnableActivityGroups` is OFF, **When** the admin navigates to the
  student Detail page, **Then** the "Activity Groups" section does not render
  and no activity-group API calls are made. *(FR-32, NFR-11)*

### Revision 2–5 acceptance-criteria amendments

> The original AC-1..31 were written under Rev. 1. The in-place edits above fix
> the directly-contradictory ones (AC-1/8/15/19). The criteria below cover Rev. 2–5
> behavior not present in the original set; remaining Rev. 1 criteria that
> mention period-independent membership or Suspended/Archived read under the
> on/off + period-scoped + span model per the readers' guide.

- **AC-33 (on/off toggle — Rev. 2)** — **Given** an on group G, **When** the
  admin turns G off, **Then** `IsActive = false`, new enrollments/links are
  blocked, existing memberships are preserved, and toggling back on restores
  enrollability. *(FR-3, FR-12, FR-22)*
- **AC-34 (grade eligibility — Rev. 2)** — **Given** a group G with eligible
  grades {G5, G6}, **When** a student whose active grade-for-period is G4 is
  added, **Then** the add is rejected; when the student's active grade is G5,
  the add succeeds. An empty eligible set accepts any actively-enrolled
  student. *(FR-39, FR-40)*
- **AC-35 (period-based membership — Rev. 3)** — **Given** a `Termly` group in
  an active term P, **When** a student is added, **Then** the membership's
  `PeriodId = P` (matching `PeriodType`); adding with no active matching period
  is rejected. *(FR-7, FR-43)*
- **AC-36 (OpenEnded continuous — Rev. 4)** — **Given** an `OpenEnded` group
  from `EnrollmentStartDate` with null `EnrollmentEndDate`, **When** a student
  is added, **Then** the membership has a null `PeriodId` and stays active until
  exited/removed or the group is turned off; there is no rollover. *(FR-48)*
- **AC-37 (DateRange window — Rev. 4)** — **Given** a `DateRange` group with
  window [S, E], **When** a student is added within the window, **Then** the
  membership records `window_start_date`/`window_end_date`; after E, new
  enrollments are rejected. *(FR-47, FR-52)*
- **AC-38 (rollover into next window — Rev. 5)** — **Given** a `DateRange`
  group at window end with a next window pre-defined and an active member with
  `AutoRenew = true`, **When** rollover runs (scheduled or forced), **Then** the
  current membership is exited (`ExitedOn` = window end) and a new active
  membership is created in the next window. *(FR-50, FR-53, FR-54)*
- **AC-39 (rollover with no next window — Rev. 5)** — **Given** a bounded
  group at window end with no next window defined, **When** rollover runs,
  **Then** all active members are exited at window end (no re-enrollment).
  *(FR-51)*
- **AC-40 (AutoRenew opt-out — Rev. 5)** — **Given** an active member with
  `AutoRenew = false` in a bounded group at window end, **When** rollover runs,
  **Then** the member is exited at window end and not re-enrolled. *(FR-49,
  FR-50)*
- **AC-41 (capacity per-period vs per-group — Rev. 4)** — **Given** a `Termly`
  group with `Capacity = 20` and 20 active members in term P, **When** a 21st
  student is added to P, **Then** it is rejected; the same group can accept
  members in a different term up to its own capacity. For an `OpenEnded`/
  `DateRange` group, capacity counts all active members of the group. *(FR-13,
  FR-46)*
- **AC-42 (framework compatibility — Rev. 3)** — **Given** a tenant with
  `AcademicYearDivision = Terms`, **When** the admin creates a `Semester`-span
  group, **Then** creation is rejected; a `Termly` group is allowed. *(FR-45)*
- **AC-43 (next window defined in advance — Rev. 5)** — **Given** a `DateRange`
  group, **When** the admin sets a next window whose start is before the
  current window's end, **Then** it is rejected; a valid next window is held
  until rollover advances it to current. *(FR-53)*
- **AC-44 (grade topic term-scoped — Rev. 6)** — **Given** a topic assigned to
  Grade 5 with `PeriodId = Term 2`, **When** an assignment about that topic is
  dated in Term 1, **Then** it is rejected; **When** dated in Term 2, **Then**
  it targets Grade-5 students (year-enrolled). *(FR-55, FR-57, FR-58)*
- **AC-45 (activity-group topic period-aligned — Rev. 6)** — **Given** a
  `Termly` group, **When** a topic is assigned to it with `PeriodId = Semester`,
  **Then** the assignment is rejected; `PeriodId = Term` is accepted. An
  `OpenEnded` group's topic assignment MUST have `PeriodId = null`. *(FR-56)*
- **AC-46 (null PeriodId back-compat — Rev. 6)** — **Given** existing
  grade/group topic assignments with `PeriodId = null`, **When** the Rev. 6
  migration lands, **Then** they behave identically to today (year-spanning,
  date-effective). *(FR-55, NFR-6)*

---

## 6. Edge Cases

> Numbered `EC-N`. At least one failure mode per external dependency
> (DB, user input, cross-context reference, publish pipeline).

- **EC-1 (delete group referenced by assignment)** — Deleting a group that a
  `Draft`/`Published` assignment links to MUST be rejected
  (`ActivityGroupReferencedException`), resolved via the cross-context
  Assignments-API check described in FR-6. *(FR-6, AC-17)*
- **EC-2 (student soft-deleted while active member)** — When a `Student` is
  soft-deleted while holding active memberships, the memberships MUST NOT be
  silently removed; they remain `Active` historically, but the student is
  excluded from recipient resolution and from "add" operations. Re-adding the
  student is blocked (FR-11). (Hard cleanup is out of scope — OS-8.)
- **EC-3 (duplicate active membership race)** — Two concurrent "add same
  student to same group" requests: the partial unique index MUST let exactly
  one succeed and reject the other with a unique-constraint violation
  surfaced as a 409. *(FR-10, AC-5)*
- **EC-4 (turn off group with live assignments — Rev. 2)** — Turning a group
  **off** (`IsActive = false`) that live (`Draft`/`Published`) assignments
  target is ALLOWED at the group level; already-published assignments keep
  their recipient snapshot; an off group MUST NOT be linkable to NEW
  assignments (FR-22) and MUST be excluded from future re-publish recipient
  resolution. Toggling back on is allowed (off is reversible, unlike the old
  terminal `Archive`).
- **EC-5 (capacity exceeded concurrently)** — Two concurrent joins that both
  pass the capacity check but together exceed `Capacity`: the unique index
  does not help here, so the handler MUST re-check the active count inside the
  transaction and reject the loser with `GroupAtCapacityException`. *(FR-13)*
- **EC-6 (cross-tenant group reference)** — An assignment link referencing a
  `groupId` from another tenant MUST be rejected before persistence. *(FR-21,
  AC-14)*
- **EC-7 (SelectedGroups publish with zero groups)** — Publishing a
  `SelectedGroups` assignment with no linked groups MUST be rejected (not
  silently produce zero recipients). *(FR-23, AC-13)*
- **EC-8 (group outlasts its periods — Rev. 2)** — A group carries no period
  association; when periods complete/archive the group remains on. Enrollment
  (membership) is period/window-scoped (Rev. 3/4), so completed periods simply
  stop accepting new enrollments — the group itself is unaffected. *(FR-3,
  FR-4, AC-19)*

- **EC-9 (student in zero groups)** — A student in no groups MUST NOT receive
  any `SelectedGroups` assignment; this is not an error, just no recipient row.
- **EC-10 (concurrency on membership update)** — A stale `RowVersion` on a
  membership update MUST throw `ConcurrencyException`. *(NFR-4, AC-23)*
- **EC-11 (linking a non-existent group id)** — Linking an assignment to a
  `groupId` that does not exist MUST be rejected with 404/400.
- **EC-12 (orphaned link after group hard-delete)** — Deletes are
  referentially guarded against membership history (FK `ON DELETE RESTRICT`
  — a group with any membership row cannot be hard-deleted) and against
  `Draft`/`Published` assignment links (EC-1). A group linked **only** to
  `Closed` assignments MAY still be hard-deleted; those `Closed`-assignment
  link rows remain in the Assignments context as dangling operational refs
  (there is no cross-context DB FK — §8.3). This is **benign**: `Closed`
  assignments are never re-published and never re-resolve recipients (and
  `Assignment.Update` rejects edits to non-draft assignments, so the links
  cannot be silently re-pointed). For tidiness, unlink `Closed` assignments
  via `PUT /api/assignments/{id}/groups` (§7.3) before deleting the group.
  No membership orphans can occur because the FK blocks the delete whenever
  any membership row exists.
- **EC-13 (partial multi-join failure)** — When joining multiple groups in
  one operation and one group is at capacity, the capacity-checked groups
  MUST create memberships successfully; the full group MUST be reported as a
  per-group error without rolling back successful joins. *(FR-30)*
- **EC-14 (all groups already joined)** — When the student is already an
  active member of every available group, the dialog's group picker MUST
  show an empty state message (e.g. "You are already a member of all
  available groups") and the submit button MUST be disabled. *(FR-30)*

### Revision 2–5 edge-case amendments

- **EC-15 (off toggle preserves history — Rev. 2)** — Turning a group off does
  not exit/remove existing memberships (history preserved); only new
  enrollment/links are blocked. *(FR-12)*
- **EC-16 (enroll into closed window — Rev. 4)** — Adding a membership to a
  `DateRange` group whose `EnrollmentEndDate` has passed is rejected; the admin
  must open/advance to the next window first. *(FR-52)*
- **EC-17 (OpenEnded rejoin uniqueness — Rev. 4)** — For an `OpenEnded` group
  the null-`PeriodId` partial unique index enforces at most one active
  membership per (student, group); re-adding an active member is rejected,
  re-adding after exit creates a new active row. *(FR-10, FR-48)*
- **EC-18 (rollover invariant — Rev. 5)** — Rollover MUST exit the current
  membership before creating the next, so the FR-10 active-uniqueness invariant
  is never violated mid-rollover. *(FR-50)*
- **EC-19 (framework mismatch at span create — Rev. 3)** — Creating a group
  whose `EnrollmentSpan` is incompatible with the tenant's `AcademicYearDivision` is
  rejected at create time. *(FR-45)*
- **EC-20 (term/semester span without period hierarchy — Rev. 3)** — Until the
  period hierarchy (`period-hierarchy-terms-semesters.md`) ships,
  `Termly`/`Semester`/`WholeAcademicYear` spans cannot attach a membership
  `PeriodId` and MUST be rejected; only `OpenEnded`/`DateRange` are usable.
  *(FR-43, dependency on period-hierarchy Phase H5)*
- **EC-21 (forced rollover of OpenEnded — Rev. 5)** — Admin-forced rollover of
  an `OpenEnded` group is a no-op (no window end). *(FR-54)*
- **EC-22 (period-aligned rollover at period end — Rev. 5)** — For period-aligned
  spans, rollover at the period's end re-enrolls `AutoRenew = true` members into
  the next period of the matching `PeriodType`; if no such next period exists,
  members are exited. *(FR-50, FR-51)*
- **EC-23 (topic PeriodId / span mismatch — Rev. 6)** — Assigning a topic to a
  `Termly` group with a `Semester` `PeriodId` (or vice-versa) is rejected at
  the bridge. *(FR-56)*
- **EC-24 (grade topic PeriodId outside active year — Rev. 6)** — A grade
  topic assignment whose `PeriodId` is a `Term`/`Semester` not within the
  tenant's active academic year is rejected. *(FR-57)*

---

## 7. API Contracts

> TypeScript-style interfaces. Success and error responses are both shown.
> Routes follow the existing `endpoint-organization-pattern.md`
> (`Map...Endpoints(this WebApplication, IFeatureFlagService)`).
> Base path `/api` implied.

### 7.1 Activity group CRUD — Students API

```ts
// POST   /api/activity-groups                 -> 201 ActivityGroupDto | 409
// GET    /api/activity-groups                 -> 200 ActivityGroupDto[]
// GET    /api/activity-groups/{id}            -> 200 ActivityGroupDto | 404
// PUT    /api/activity-groups/{id}            -> 204 | 404 | 409
// DELETE /api/activity-groups/{id}            -> 204 | 404 | 409 (referenced)
// POST   /api/activity-groups/{id}/archive    -> 204 | 404
// POST   /api/activity-groups/{id}/suspend    -> 204 | 404

interface ActivityGroupDto {
  id: string;            // Guid
  tenantId: string;
  name: string;
  description: string | null;
  category: string | null;
  periodId: string | null;
  capacity: number | null;
  status: "Active" | "Suspended" | "Archived";
  activeMemberCount: number;
  createdAt: string;     // ISO 8601
  updatedAt: string;
}

interface CreateActivityGroupRequest {
  name: string;                 // required, <= 200
  description?: string | null;  // <= 2000
  category?: string | null;     // <= 100
  periodId?: string | null;     // Guid | null
  capacity?: number | null;     // >= 1
}

interface UpdateActivityGroupRequest extends Partial<CreateActivityGroupRequest> {}

// Errors
interface ProblemDetails {
  type: string; title: string; status: number;
  detail: string; errors?: Record<string, string[]>;
}
```

### 7.2 Membership — Students API

```ts
// POST   /api/activity-groups/{groupId}/members              -> 201 | 409 | 422
// DELETE /api/activity-groups/{groupId}/members/{studentId}  -> 204 | 404
// POST   /api/activity-groups/{groupId}/members/{studentId}/exit -> 204 | 404
// GET    /api/activity-groups/{groupId}/members              -> 200 MembershipDto[]
// GET    /api/students/{studentId}/activity-groups           -> 200 ActivityGroupDto[]

interface AddMemberRequest {
  studentId: string;     // Guid
  joinedOn?: string;     // DateOnly ISO, default today
}

interface MembershipDto {
  id: string;
  activityGroupId: string;
  studentId: string;
  studentName: string;   // denormalized for display
  joinedOn: string;      // DateOnly ISO
  exitedOn: string | null;
  status: "Active" | "Exited" | "Removed";
  createdAt: string;
  updatedAt: string;
}

// 409 GroupAtCapacityException | duplicate active membership
// 422 archived group | deleted student | tenant mismatch
```

### 7.3 Assignment↔group link — Assignments API

```ts
// PUT   /api/assignments/{assignmentId}/groups        -> 204 | 422 (replace set)
// GET   /api/assignments/{assignmentId}/groups        -> 200 ActivityGroupRefDto[]
// GET   /api/activity-groups/{groupId}/assignments    -> 200 AssignmentSummary[]

interface AssignmentGroupLinkRequest {
  activityGroupIds: string[];   // Guid[]; must be same tenant, non-archived
}

interface ActivityGroupRefDto {
  id: string; name: string; status: "Active" | "Suspended" | "Archived";
}
```

> Note: assignment create/update already accepts `targetAudienceType`; the
> `groups` set is managed via the dedicated link endpoints above so that
> `SelectedGrades` (scalar `gradeLevelId`) and `SelectedGroups` (link set)
> stay on independent code paths (FR-19).

## 8. Data Models

> Snake_case table names (matches `student_enrollments`, `assignments`).
> All entities are strict-tenant (`tenant_id`), audited, and use `xmin` row
> version where noted.

### 8.1 `activity_groups` (Students context)

| field | type | constraints |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | NOT NULL; strict-tenant filter |
| `name` | text | NOT NULL, <= 200 |
| `description` | text | NULL, <= 2000 |
| `category` | text | NULL, <= 100 |
| `capacity` | integer | NULL; CHECK (capacity >= 1) |
| `enrollment_span` | integer | NOT NULL; enum `EnrollmentSpan` (Rev. 3/4) |
| `enrollment_start_date` | date | NULL; required for `DateRange`/`OpenEnded`; null for period-aligned (Rev. 4) |
| `enrollment_end_date` | date | NULL; required for `DateRange`; always null for `OpenEnded`; null for period-aligned (Rev. 4) |
| `next_enrollment_start_date` | date | NULL; `DateRange` next window start, admin-defined in advance (Rev. 5) |
| `next_enrollment_end_date` | date | NULL; `DateRange` next window end, admin-defined in advance (Rev. 5) |
| `auto_renew_default` | boolean | NOT NULL; default true — default `AutoRenew` for new memberships (Rev. 4) |
| `is_active` | boolean | NOT NULL; default true (on/off — Rev. 2; replaces `status`) |
| `xmin` | xid | row version (PostgreSQL) |
| `created_at` | timestamptz | NOT NULL |
| `updated_at` | timestamptz | NOT NULL |

Indexes: unique `(tenant_id, lower(name))` → `ix_activity_groups_tenant_name`;
`(tenant_id, is_active)` (Rev. 2 — replaces the `status` and `period_id` indexes).

### 8.2 `activity_group_memberships` (Students context)

| field | type | constraints |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | NOT NULL |
| `activity_group_id` | uuid | NOT NULL; FK → `activity_groups.id` `ON DELETE RESTRICT` (preserves membership history — NFR-8; a group with any membership row cannot be hard-deleted, only turned off via `is_active = false`) |
| `student_id` | uuid | NOT NULL; FK → `students.id` |
| `period_id` | uuid | NULL; FK → `periods.id`. Required for `WholeAcademicYear`/`Termly`/`Semester` spans; NULL for `OpenEnded` (Rev. 3) |
| `joined_on` | date | NOT NULL |
| `exited_on` | date | NULL |
| `status` | integer | NOT NULL; default 0 (Active); enum `MembershipStatus` |
| `auto_renew` | boolean | NOT NULL; default true (Rev. 4 — willing/rollover opt-in) |
| `window_start_date` | date | NULL; set for `DateRange`/`OpenEnded` memberships; null for period-aligned (Rev. 4) |
| `window_end_date` | date | NULL; set for `DateRange` memberships; null for `OpenEnded`/period-aligned (Rev. 4) |
| `transfer_reason` | text | NULL |
| `xmin` | xid | row version |
| `created_at` | timestamptz | NOT NULL |
| `updated_at` | timestamptz | NOT NULL |

Indexes: **partial unique** `(tenant_id, student_id, activity_group_id, period_id)
WHERE status = 0 AND period_id IS NOT NULL` → `ix_agm_tenant_student_group_period_active`
(FR-10, Rev. 2/3 — period-scoped); **partial unique** `(tenant_id, student_id,
activity_group_id) WHERE status = 0 AND period_id IS NULL` →
`ix_agm_tenant_student_group_open_active` (FR-10, Rev. 3 — OpenEnded case,
sidesteps the Postgres NULL-distinct pitfall); `(tenant_id, activity_group_id,
period_id, status)` → hot path (NFR-3); `(tenant_id, student_id, period_id)`;
`(tenant_id, period_id)`.

### 8.3 `assignment_activity_groups` (Assignments context)

| field | type | constraints |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | NOT NULL |
| `assignment_id` | uuid | NOT NULL; FK → `assignments.id` |
| `activity_group_id` | uuid | NOT NULL; operational ref (no DB FK across contexts) |
| `created_at` | timestamptz | NOT NULL |
| `updated_at` | timestamptz | NOT NULL |

Indexes: unique `(tenant_id, assignment_id, activity_group_id)` → no duplicate
links; `(tenant_id, activity_group_id)` → reverse lookup (NFR-3).

> `activity_group_id` is an **operational reference** into the Students
> context, exactly like `Assignment.GradeLevelId` / `Assignment.SubjectId`
> today (no cross-context DB foreign key; integrity is enforced in code via
> FR-21/FR-22 and the referential delete guard FR-6).

### 8.4 `activity_group_grade_levels` (Students context — Rev. 2)

| field | type | constraints |
|---|---|---|
| `id` | uuid | PK |
| `tenant_id` | uuid | NOT NULL; strict-tenant filter |
| `activity_group_id` | uuid | NOT NULL; FK → `activity_groups.id` `ON DELETE CASCADE` (the eligibility set is owned by the group) |
| `grade_level_id` | uuid | NOT NULL; FK → `grade_levels.id` `ON DELETE CASCADE` |
| `created_at` | timestamptz | NOT NULL |
| `updated_at` | timestamptz | NOT NULL |

Indexes: unique `(tenant_id, activity_group_id, grade_level_id)` → no duplicate
eligibility rows; `(tenant_id, grade_level_id)` → reverse lookup (which groups a
grade is eligible for — drives the grade-level enrollment landing page).

> The eligible-grade set is **owned by** the `ActivityGroup`; deleting a group
> or grade cascades the link rows. It is **not** a membership — it is the
> declaration of which grades may enrol. Enrollment validation (FR-40) joins
> this set against the student's active grade-for-period.

### 8.6 Enums

```csharp
namespace SchoolCollab.Students.Core.Domain;
// Rev. 2: the three-state ActivityGroupStatus enum is removed. ActivityGroup
// now carries a boolean IsActive (on/off). MembershipStatus is unchanged.
// Rev. 3: ActivityGroup carries an EnrollmentSpan (see FR-42).
public enum MembershipStatus { Active = 0, Exited = 1, Removed = 2 }
public enum EnrollmentSpan { WholeAcademicYear = 0, Termly = 1, Semester = 2, DateRange = 3, OpenEnded = 4 }
```

`TargetAudienceType` is **unchanged** (`AllStudents=0, SelectedGrades=1,
SelectedGroups=2`) — this spec finally implements the existing `SelectedGroups`
value.

## 9. Out of Scope

> Explicit exclusions with reasons. IDs: `OS-N`.

- **OS-1 (Unified `AssignmentTarget` table)** — Replacing the scalar
  `Assignment.GradeLevelId` with a polymorphic `AssignmentTarget(TargetType,
  TargetId)` table that lets one assignment target grades **and** groups in a
  single config. **Reason:** breaking change to an existing column/index and
  the `SelectedGrades` code path; requires its own spec + migration + test
  rework. Tracked as a follow-up spec.
- **OS-2 (One assignment targeting both grades and groups simultaneously)** —
  Blocked on OS-1. The current closed-set `TargetAudienceType` keeps audience
  mutually exclusive per assignment. The *system* supports grades and groups;
  a *single* assignment does not mix them yet.
- **OS-3 (Group hierarchy / sub-groups)** — No parent/child groups. Reason: no
  current requirement; adds recursion complexity with no caller.
- **OS-4 (Group chat / messaging channel)** — Groups are a targeting audience,
  not a messaging surface. The notification pipeline is reused as-is (FR-20).
- **OS-5 (Attendance / roster scheduling for groups)** — Out of scope; groups
  here are membership sets for assignment targeting, not timetabled sessions.
- **OS-6 (Group-specific grading rubrics)** — Assignment grading
  (`GradingFormat`, `AssignmentReview`) is unchanged and not partitioned by
  group.
- **OS-7 (Multi-grade per assignment)** — Changing `SelectedGrades` from one
  `GradeLevelId` to many is a separate concern and a breaking change; not
  touched here (NFR-6).
- **OS-8 (Hard cleanup of memberships for soft-deleted students)** — EC-2 keeps
  history; an automated purge job is a separate data-retention concern.
- **OS-9 (Self-service student join/leave)** — Only admin/teacher manages
  membership in this spec. Student-initiated join requests are a future UX
  feature.
- **OS-10 (Teacher assignment / role on activity groups)** — Associating a
  teacher (leader, coach, advisor) with an `ActivityGroup`, granting that
  teacher group-management or publish rights, and displaying the leader in the
  UI are all deferred to a future spec. **Reason:** the teacher-role model
  (permissions, scoping, authorization for group-scoped publish) needs its own
  design and is not required for the core membership + assignment-targeting
  loop this spec delivers. The `leader_teacher_id` column was removed from the
  data model (§8.1), entity (FR-1), and API (§7.1) so it is not a dormant
  nullable column carrying an unimplemented feature.

## 10. Affected files (indicative)

| Context | Path | Change |
|---|---|---|
| Students.Core | `Domain/ActivityGroup.cs`, `Domain/ActivityGroupStatus.cs` | **new** entities |
| Students.Core | `Domain/ActivityGroupMembership.cs`, `Domain/MembershipStatus.cs` | **new** entities |
| Students.Core | `Data/Repositories/*` + `Data/StudentsDbContext.cs` | add DbSets + repos |
| Students.Core | `Migrations/<ts>_AddActivityGroups.cs` | **new** additive migration |
| Students.Core | `Services/IActivityGroupAssignmentQuery.cs` | **new** cross-context port for FR-6 delete guard |
| Students.Api | `Services/ActivityGroupAssignmentQueryHttpClient.cs` | **new** HTTP client impl (calls Assignments API `GET /api/activity-groups/{id}/assignments`) |
| Assignments.Core | `Data/AssignmentsDbContext.cs` | add DbSets |
| Assignments.Core | `Migrations/<ts>_AddAssignmentActivityLinks.cs` | **new** additive migration |
| Students.Admin | `Components/Pages/ActivityGroups/*` | **new** FluentUI pages (mirror `GradeLevels`) |
| Students.Admin | `Components/Pages/Students/Detail.razor` | **extend** — add Activity Groups section (FR-28..32) |
| Students.Admin | `Components/ActivityGroups/JoinGroupDialog.razor` | **new** dialog (FR-29, FR-30) |
| AppHost | `appsettings.json` + `Program.cs` | add `Parameters:FEATURE:EnableActivityGroups`, fan out |
| Docs | `documents/configuration.md` §2 | register the new feature flag |

## 11. Verification

- `dotnet build SchoolCollab.sln` — 0 errors, 0 new warnings.
- `dotnet ef migrations add DiagnosePendingChanges --project
  src/Students/SchoolCollab.Students.Core --context StudentsDbContext` → empty
  after the real migration lands (per `ef-migrations.md`). Same for
  `AssignmentsDbContext`.
- Unit tests (`SchoolCollab.Students.Tests.Unit`):
  - `ActivityGroup.Create/Update/Delete` invariants; delete rejected when
    referenced by assignment or when active members exist (AC-17, AC-18).
  - `ActivityGroupMembership`: multi-membership allowed (AC-4); duplicate
    active rejected (AC-5); rejoin after exit (AC-6); deleted-student/
    archived-group/capacity rejections (AC-7/8/9/10); age/gender specs NOT
    applied (AC-11).
  - `NoUncommittedModelChanges` passes for `StudentsDbContext`.
- Unit tests (`SchoolCollab.Assignments.Tests.Unit`):
  - `AssignmentActivityGroup` link set replace; cross-tenant + archived
    rejected (AC-14/15); `SelectedGrades` path unchanged (AC-16).
  - Publish with `SelectedGroups` resolves members → recipients (AC-12);
    publish rejected (AC-13).
  - `NoUncommittedModelChanges` passes for `AssignmentsDbContext`.
- bUnit: `ActivityGroups` landing + create/edit/delete + members tab; hidden
- bUnit: student Detail page "Activity Groups" section (AC-27); "Join Group"
  dialog opens (AC-28); multi-join operation (AC-29); "Leave" button exits
  member (AC-30); section hidden when flag OFF (AC-31).
- Performance: a benchmark harness seeds 5,000 members (AC-21) and 10,000
  across groups (AC-22) and asserts the NFR-1/NFR-2 thresholds.
- Playwright smoke (seeded): create "Chess Club" → add 3 students → create
  a group-scoped `Subject` (polymorphic, `OwnerType = ActivityGroup`) → create
  a `SelectedGroups` assignment linked to the club and that subject → publish
  → assert only club members' subscribed contacts received it.

## 12. Open questions (resolved)

1. **Group category taxonomy** — free-text `Category` (this spec) vs. a coded
   value under a new `CodedValueParent.ActivityCategory`? ✅ **Decision:
   free-text for v1** (FR-1). Promoted to coded values later if reporting
   needs it.
2. **Leader teacher / group ownership** — ~~should `LeaderTeacherId` grant
   the leader assignment-publish rights to that group?~~ **Decision: deferred.**
   Teacher assignment/role on an `ActivityGroup` is removed from this spec
   entirely and tracked as a future feature (see **OS-10**). The
   `leader_teacher_id` column, the `LeaderTeacherId` entity property, and the
   API field are all removed. Authorization stays as today (admin/teacher
   manages groups and membership); group-specific teacher roles will be
   planned in a separate spec.
3. **Archive vs. delete on active members** — when a group with active members
   is no longer needed, do we force `Archive` (keeps history) and forbid
   hard-delete (current spec, FR-6)? ✅ **Decision: yes** — archive is the
   soft-retire path; hard-delete only when truly unreferenced.
4. **Re-publish recipient semantics for archived groups** — EC-4 says archived
   groups are excluded from future re-publish. Confirm this is desired vs.
   keeping the last snapshot. ✅ **Decision: exclude from re-publish** (fresh
   resolution each publish).

> All questions are resolved. Implementation may proceed.

## 13. Implementation phases (one PR per step, each shippable behind the flag)

### Phase 1 — Students domain model (dark)
- `ActivityGroup` entity + `ActivityGroupStatus` enum
- `ActivityGroupMembership` entity + `MembershipStatus` enum
- Configurations with partial unique index on `(tenant_id, student_id, activity_group_id, status='Active')`
- `StudentsDbContext` migration
- Unit tests for entity invariants (AC-1..11)
- **Flag OFF**

### Phase 2 — Membership commands/queries + APIs (dark)
- CQRS: `CreateActivityGroup`, `UpdateActivityGroup`, `DeleteActivityGroup`, `ArchiveActivityGroup`, `SuspendActivityGroup` commands
- CQRS: `AddMembership`, `RemoveMembership`, `ExitMembership`, `GetGroupMembers`, `GetStudentGroups` queries
- Students API endpoints: `MapActivityGroupEndpoints`
- Cross-context delete-guard port `IActivityGroupAssignmentQuery` (HTTP client in `Students.Api` calling Assignments API `GET /api/activity-groups/{id}/assignments`) wired into `DeleteActivityGroupHandler` (FR-6)
- Feature flag gate on all endpoints
- Unit tests for all command/query handlers
- **Flag OFF**

### Phase 3 — Assignment↔group link + publish wiring (dark)
- `AssignmentActivityGroup` entity + configuration + migration
- `LinkAssignmentGroups` command handler
- Extend publish recipient resolution for `SelectedGroups` (resolve active
  members of linked groups → recipients)
- Unit tests for link set replace, cross-tenant rejection, archived group
  exclusion
- **Flag OFF**

### Phase 4 — Admin UI: ActivityGroups pages (dark)
- `ActivityGroups` list page (mirror `GradeLevels`)
- `ActivityGroupCreateEditDialog`
- `ActivityGroupDetails` page with members tab
- All UI gated by `FEATURE:EnableActivityGroups`
- bUnit tests for CRUD + members tab
- **Flag OFF**

### Phase 5 — Student Detail page UI (dark)
- "Activity Groups" section on student Detail page (FR-28)
- "Join Group" dialog with searchable multi-select (FR-29, FR-30)
- "Leave" action button per membership row (FR-31)
- Empty state message when no memberships (FR-32)
- Feature flag gate on entire section + all API calls
- bUnit tests (AC-27, AC-28, AC-29, AC-30, AC-31)
- **Flag OFF**

### Phase 6 — Flag flip + pilot rollout (dark→lit)
- `FEATURE:EnableActivityGroups` defaults ON in `appsettings.Pilot Tenant.json`
- Configuration documentation update
- Playwright smoke test: create group → add students → create `SelectedGroups` assignment → publish → verify recipients
- Monitor pilot tenant for 1 week before broader rollout

---

### Traceability summary

- **35 FRs (FR-1..35), 11 NFRs (NFR-1..11), 31 ACs (AC-1..31), 14 ECs (EC-1..14), 10 OS items (OS-1..10).
- Every entity in §8 maps to a requirement: `activity_groups` → FR-1..6;
  `activity_group_memberships` → FR-7..16; `assignment_activity_groups` →
  FR-17..23; FR-28..32 for student Detail page UI; FR-35 for admin UI.
- `TargetAudienceType.SelectedGroups` (pre-existing enum value) is now backed
  by data model §8.3 and behavior FR-17..23.
