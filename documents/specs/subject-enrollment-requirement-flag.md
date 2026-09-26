# Spec: Subject Enrollment Requirement Flag

> **Status:** Draft v1 — decisions locked by a design grilling session
> (2026-09-26). **Nothing implemented.**
> **Owner:** Students context (`SchoolCollab.Students.Core` / `.Api` / `.Application`)
> **Cross-context impact:** `SchoolCollab.Assignments.Api`
> (`StudentsContactResolver`) — recipient filtering, see §5.3. Compliant with
> `documents/solution/adr-cross-module-calls.md` (live-consistency hop;
> replication alternative rejected on record in §5.3.1) and
> `documents/solution/cross-module-http-client-pattern.md` (§5.3.2).
> **Depends on:** `subject-period-exception-model.md` (the block/exception model;
> the `PeriodId` on the bridge is superseded by that spec), `active-period-per-tenancy.md`,
> `period-hierarchy-terms-semesters.md` FR-H13, `global-tenant-filter.md` §3.2
> **Supersedes:** nothing. **Superseded in part by:** —
> **Relates to:** `topic-edit-dialog-redesign.md` §4 (which is withdrawn in favour of §8 here)

---

## 0. Decisions locked in this revision

1. **The feature is one boolean, not a subsystem.** A nullable
   `RequiresEnrollment` flag on the **shared `TopicAssignment` TPH base**, so
   both `GradeTopicAssignment` and `ActivityGroupTopicAssignment` carry it. There
   is no separate "ticket model": the per-student enrolment record
   (`StudentTopicAssignment`) **already exists** and only needs wiring (§9).
2. **`true` = students must be explicitly enrolled. `false` = automatically
   assumed enrolled** for every student of the grade.
3. **The default depends on the audience subtype**: `ActivityGroupTopicAssignment.Create`
   sets `true`; `GradeTopicAssignment.Create` sets `false`. Because `TopicAssignment`
   is table-per-hierarchy, **a discriminator-dependent column default is
   impossible** — the value must be set in the two factory methods, never by a
   schema `DEFAULT` (§2.2).
4. **`false` ⇒ zero rows.** Enrolment is derived from grade membership at read
   time. `StudentTopicAssignment` is populated only for `true` subjects. This
   keeps the table empty for the common case and is what makes the annual
   rollover cheap.
5. **Flipping `false → true` grandfathers** every current grade student by
   materialising a row for each, so the flip is non-destructive, and offers a
   bulk **"enrol the remaining"** action in the same place (§4).
6. **Publish filters, never blocks.** Students without an enrolment row are
   excluded from the assignment's recipients. There is no publish-time
   preview, no blocking validation, and no admin choice at publish. The
   reconciliation — warning plus bulk enrolment — happens **at the moment the
   flag is switched** (§4), not at publish.
7. **"My subjects" is a union**, not a filter: every `false` subject on the
   grade, plus every `true` subject the student holds a row for (§6).
8. **The primary UI is the student create/edit page**, which carries sections for
   subjects-per-grade and for activity groups. The grade-side students × subjects
   matrix is **not** built.
9. **Rollover is one dedicated grades × subjects screen**, reachable from **two**
   entry points — the period rollover, and the subject's flag switch — with
   **manual approval** of which students carry forward (§7).
10. **Activity groups are as-is.** Enforcement is wired for **grades only** in
    v1. The group default of `true` is set on creation but nothing reads it yet
    (§11 records this as a known interim state).
11. **Subscription-only activity groups are deferred** — out of scope.
12. The accepted cost of decision 6: the excluded students leave **no durable
    record** on the assignment, so a teacher can publish to a silently truncated
    cohort. Recorded, not mitigated, at this stage.

---

## 1. What this is

A subject becomes real for a school when it is *assigned* to a grade. Today that
is the only gate: everyone in the grade has it. That is right for Mathematics and
wrong for a selective subject, a remedial stream, or a subject only some
students may sit.

This spec adds exactly one switch for that case.

```
TopicAssignment.RequiresEnrollment
   false (default for grades)  →  every student of the grade is enrolled. No rows.
   true                        →  a student is enrolled iff a StudentTopicAssignment
                                  row exists for (student, topic, active year).
```

**This is not a period feature.** Periods are handled by
`subject-period-exception-model.md`, which removed `PeriodId` from the bridge
precisely so a subject's availability no longer depends on a period's lifecycle.
The two specs are independent and must not be conflated: that one decides *when*
a subject is offered, this one decides *who* may receive work for it.

---

## 2. Data model

### 2.1 The column

```csharp
public abstract class TopicAssignment : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    // …existing: Id, TenantId, TopicId, StartDate, EndDate, TopicStrandId, PeriodId…

    /// <summary>
    /// When true, a student is enrolled in this topic for this audience ONLY if a
    /// <see cref="StudentTopicAssignment"/> exists for them; there is no implied
    /// enrolment from audience membership. When false (the default for a grade),
    /// every member of the audience is assumed enrolled and no row is stored.
    /// <para>
    /// Lives on the TPH base so both audiences carry it. Default is NOT expressed
    /// as a schema DEFAULT — see §2.2. Enforcement is wired for grades only in
    /// v1; activity groups keep their existing behaviour (decision 10).
    /// </para>
    /// </summary>
    public bool RequiresEnrollment { get; private set; }
}
```

A mirror exists on the pattern of `GradeLevel.IsBlockedFromEnrollment` — a
`bool`, defaulting to the permissive value, with a `SetRequiresEnrollment(bool)`
that no-ops when the value is unchanged and bumps `UpdatedAt` otherwise. That
precedent is deliberate: the codebase already gates enrolment with exactly this
shape, and `EnrollStudentHandler.cs:85` is the precedent for a consumer refusing.

### 2.2 The TPH default problem

`TopicAssignment` is table-per-hierarchy with discriminator `topic_assignment_type`
(`"grade"` | `"activity_group"`). **Both subtypes share one table, so a
discriminator-dependent column default cannot be expressed in the schema.** A
`HasDefaultValue(false)` would apply to group rows too, silently contradicting
decision 3.

The default must therefore be assigned in the two factories:

| Factory | `RequiresEnrollment` |
|---|---|
| `GradeTopicAssignment.Create(...)` | `false` |
| `ActivityGroupTopicAssignment.Create(...)` | `true` |

Both are the only creation paths (`AssignGradeTopic`, `AssignActivityGroupTopic`),
so this is enforceable without a database constraint. **A reviewer should reject
any attempt to solve this with a column default.** A test must assert both
factory defaults — see FR-ER-2.

---

## 3. Enrolment semantics

```
Enrolled(student, topic, audience) =
      audience.RequiresEnrollment == false
   OR EXISTS StudentTopicAssignment(student, topic, PeriodId = activeAcademicYear.Id)
```

**FR-ER-1** — a `false` subject enrols every member of the audience. No row is
created, ever, for any student.

**FR-ER-2** — `GradeTopicAssignment.Create` yields `RequiresEnrollment = false`;
`ActivityGroupTopicAssignment.Create` yields `true`.

**FR-ER-3** — a `true` subject enrols only students holding a row whose
`PeriodId` is the tenant's **active academic year**. `StudentTopicAssignment.PeriodId`
is server-resolved to the active year by `AssignStudentTopicHandler` and cannot be
caller-chosen, so this is an exact id match, not a date range test.

**FR-ER-4** — the `StudentTopicAssignment` unique index is
`(tenant_id, student_id, topic_id, period_id)`. A duplicate enrolment is a
conflict, not a second row. No schema change needed.

**FR-ER-5** — `SubPeriodId` remains a **creation stamp** and is never used for
eligibility. Term-level variation is `subject-period-exception-model.md`'s job.

**FR-ER-6 (accepted trade-off)** — for a `false` subject there is **no record of
who was enrolled**. A mid-year roster change therefore retroactively changes the
answer to "who took this". Accepted at grilling; recorded so it is not mistaken
for an oversight.

---

## 4. Switching the flag on

**FR-ER-7** — `SetRequiresEnrollment(true)` on a subject that currently enrols by
implication **must grandfather**: materialise a `StudentTopicAssignment` for
every current member of the audience before the flag is persisted. The switch is
then non-destructive — the admin's mental model is "restrict from here on", not
"everything just vanished".

**FR-ER-8** — the switch surface shows, before confirming: the audience size, how
many are currently enrolled, and how many would be **un-enrolled** by the switch.
After confirming, it offers a bulk **"enrol the remaining N"** action
(`AssignStudentTopic` per student, `SourceType = IndividualAssignment`).

**FR-ER-9** — `SetRequiresEnrollment(false)` leaves existing rows in place as
inert history. They are **not** deleted: the unique index is per-year, so they do
not shadow a future re-enrolment, and deleting them would destroy the audit trail.
A `false` subject reads as "everyone", so the rows are simply never consulted.

**FR-ER-10** — grandfathering is server-side and transactional. A partial
grandfather (flag set, some students missing) is worse than no grandfather: it
reproduces exactly the silent-truncation failure decision 6 accepts.

---

## 5. Publish behaviour

### 5.1 Policy

**FR-ER-11** — publishing a subject-scoped assignment resolves recipients as
usual, then **excludes** any student who is not enrolled per §3. Publish
**succeeds**. There is no blocking validation and no publish-time choice.

**FR-ER-12** — `ITopicAssignmentLookup.IsTopicAssignedAsync(gradeLevelId,
activityGroupIds, topicId, effectiveDate, ct)` is **unchanged**. It remains the
FR-58 grade-level pre-check ("is this subject assigned to this audience at all").
It is not sufficient for per-student eligibility and is not being extended to be.

### 5.2 Why there is no publish-time guard

Decided at grilling: the reconciliation belongs where the condition is *created*.
A publish-time preview or block was explicitly rejected as over-engineering for
this stage. FR-ER-7/8 make the flag switch the place where the gap is surfaced
and one click from fixed.

### 5.3 Where the filter lives (cross-context)

`StudentsContactResolver` lives in **`SchoolCollab.Assignments.Api`**, not in the
Students context. It currently:

1. `GET students/by-grade/{gradeLevelId}` → `StudentDto[]`
2. per student: `GET students/{studentId}/guardians` → `StudentGuardianViewDto[]`
3. `GET grade-levels/{gradeLevelId}/teachers` → `TeacherWithRoleDto[]`
4. `GET …` subscribed contacts → `SubscribedContactDto[]`
5. flattens to `SubscriberInfo[]` (contacts, not students)

**FR-ER-13** — the eligibility filter is a step inserted **after** step 1, over
the `studentIds` list, in `StudentsContactResolver`.

**FR-ER-13a — no new port in `Assignments.Core`.** The filter is an
implementation detail of the existing `IContactResolver` implementation.
`PublishAssignmentCommandHandler` keeps injecting `IContactResolver` and its
test double is unchanged — the same shape as `TopicAssignmentLookupHttpClient`
(`Assignments.Api`) behind `ITopicAssignmentLookup` (`Assignments.Core`), both
registered in `Assignments.Api/Program.cs:115` and `:162`. A new
`ITopicEligibilityLookup` port would be a *second* abstraction over the same hop
with nothing to mock separately, so it is rejected.

**FR-ER-13b — `ResolveSubscribersRequest` gains a `TopicId` field.** It is a
record in `Assignments.Core`; adding the id the filter needs is a field
addition, not a new port. Confirm whether it already exposes `TopicId` (it was
not verifiable from the spec-writing pass) — the `Assignment` entity already
stores `TopicId`, so the value is available at the call site.

**FR-ER-14** — a **new** Students API endpoint supplies the eligible ids, e.g.
`GET /students/topic-enrollment?gradeLevelId={id}&topicId={id}&periodId={id}`
returning `studentId[]`. It returns **all** students when the subject is `false`,
so the caller does not need to know the flag. Registered in
`TopicAssignmentRoutes` per `endpoint-organization-pattern.md`.

**FR-ER-14a — the response carries `Guid[]` only.** No Students domain or DTO
type crosses into `Assignments`. `StudentDto`, `StudentGuardianViewDto` and
`SubscribedContactDto` are already consumed inside `StudentsContactResolver`
(an `Assignments.Api` type) — this endpoint follows suit and returns primitives,
so the hop does not widen the existing coupling surface.

**FR-ER-15** — the filter must be **fail-open**: if the eligibility endpoint
errors, publish proceeds unfiltered and the failure is logged. A transient error
must not silently withhold homework from a whole grade.

#### 5.3.1 ADR compliance (`adr-cross-module-calls.md`, accepted 2026-08-23)

**Hop class: live consistency, not reference data.** The ADR's rule bars *new
synchronous cross-module HTTP calls on a command's write path **for reference
data***, and requires that any new sync hop have a replication alternative
considered and rejected **on record**. Subject enrolment is not reference data:
it changes per-student, frequently, and a stale read would enrol the wrong
recipients at exactly the moment it matters. The ADR's own table already
classifies `StudentsContactResolver` as a live-consistency hop
(*"Assignments → students | `StudentsContactResolver` | write/read | live
consistency"*), so extending it stays inside the accepted class.

**Replication alternative — considered and rejected.** The Students context
already publishes through the outbox (`StudentEnrolled`), so the
ADR-sanctioned alternative would be a `StudentTopicAssigned` /
`StudentTopicRemoved` event plus a local projection in Assignments, read at
publish. Rejected because:

1. **Staleness is unacceptable here.** The projection is consulted inside a
   write-path publish; a lagging projection would send an assignment to an
   unenrolled student, or withhold it from an enrolled one. Reference-data
   hops tolerate seconds of lag by definition; this one cannot.
2. **Granularity.** It is per-student, per-subject, per-year, and changes on
   every enrol/disenrol — a high-churn projection for a low-volume read.
3. **The rule that actually bites is reference data.** A projection would be
   correct-but-overengineered here; the ADR asks for replication of *reference*
   data specifically, and this is not that.

Recorded here to satisfy the ADR's "rejected on record" requirement.

#### 5.3.2 HTTP client wiring (`cross-module-http-client-pattern.md`)

**FR-ER-16a** — the new call reuses the **existing** `students-api` named
client already resolved by `StudentsContactResolver` via
`IHttpClientFactory.CreateClient("students-api")`. It must be registered with
`CrossModuleHttpClientExtensions.AddCrossModuleHttpClient` (30-minute handler
lifetime, `CrossModuleRetryDelegatingHandler`, transient
`TenantPropagationDelegatingHandler`) like every other cross-boundary client —
the 2-minute default handler rotation disposes pipelines under in-flight
requests and surfaces as `ObjectDisposedException`/`NetworkStream`.

**FR-ER-16b** — **no new base address and no new `WithReference`.** The
`assignments-api → students-api` reference already exists in the AppHost, so
service discovery already resolves the target. This matters mechanically:
`CrossModuleWiringTests`
(`tests/SchoolCollab.Core.Tests.Unit/Architecture`) scans every cross-module
base address in `src/**/*.cs` against the AppHost `WithReference` wiring and
fails the build, and CI runs a dedicated **Cross-module wiring guard** job. A
reviewer must confirm the new endpoint is reached through the existing client
name, not a second `AddCrossModuleHttpClient` with a new address.

**FR-ER-16c** — tenant propagation. `Assignments.Api` is an API host, so the
tenant is resolved from the inbound request and the hop uses
`propagateTenant: false` — matching the existing Students client, not the admin
shell's `true`.

---

## 6. "My subjects" union

**FR-ER-16** — listing a student's subjects for a grade is a **union**, not a
filter:

```
subjectsForStudent(grade, student) =
      { t : GradeTopicAssignment(grade, t) where !t.RequiresEnrollment }
  ∪ { t : GradeTopicAssignment(grade, t) where t.RequiresEnrollment
                    and exists StudentTopicAssignment(student, t, activeYear) }
```

`StudentTopicAssignmentRepository.ListByStudentAsync(studentId, periodId)` already
provides the second half; the first half comes from
`ListGradeTopicsByGradeAsync`. The union is a new query — neither existing
repository method produces it.

**FR-ER-17** — the same union is the backing query for the student page's
subject section (§8) and for the rollover screen's eligibility count (§7).

---

## 7. Rollover

`StudentTopicAssignment.PeriodId` is the active academic year, so a row issued in
2026 does not carry into 2027. Every `true` subject needs its enrolments
re-issued at each year boundary.

**FR-ER-18** — a **single dedicated grades × subjects screen** is the rollover
surface. It lists each grade, its `true` subjects, the current enrolled count
against the audience size, and the set of students eligible to carry forward.

**FR-ER-19** — the screen has **two entry points** and no duplicate
implementation: (a) the **period rollover** action, and (b) the **subject's
`RequiresEnrollment` switch** (§4.8).

**FR-ER-20** — carry-forward is **manually approved**, not automatic. Students
may have discontinued the class or left the school, which is precisely why
auto-roll was rejected at grilling. The admin edits the selection, then commits.

**FR-ER-21** — a student no longer in the audience is never offered for
carry-forward. The screen must not re-enrol a student who has changed grade.

**FR-ER-22** — committing writes one `StudentTopicAssignment` per approved student
with the **new** active year. The unique index is keyed per period, so this is a
plain insert with no conflict against the prior year's rows.

**FR-ER-23** — the rollover is **idempotent per (subject, target year)**: a
second commit for the same year replaces the set rather than appending, so a
mistaken approval is correctable.

---

## 8. UI surfaces

**FR-ER-24** — the **student create/edit page** is the primary enrolment surface.
It carries a **Subjects** section (grouped by grade, showing each subject's
enrolment state) and an **Activity Groups** section, in the same dialog. This is
also the "merge" the owner asked for: one surface, two sections, rather than a
subjects dialog and a separate groups dialog.

**FR-ER-25** — the **grade Subjects card** carries the `RequiresEnrollment` toggle
per row, since the flag is a property of the grade↔subject link. Toggling it
there opens the FR-ER-7/8 warning and bulk-enrol flow.

**FR-ER-26** — the grade-side students × subjects **matrix is not built**. The
grade card shows a **read-only count** ("20 of 30 enrolled") linking to the
rollover screen, which is the minimum that makes the annual task possible.

**FR-ER-27** — the edit dialog's withdrawn "Delivery periods" section
(`topic-edit-dialog-redesign.md` §4) becomes an **"Enrollment"** section
reflecting this flag. Per `subject-period-exception-model.md` §5 it is not a
period-set editor.

**FR-ER-28** — gate the control, not just the call: with no audience selected the
toggle is not rendered; for a `false` subject the enrolment detail is not
rendered, because there is nothing per-student to show.

---

## 9. What is already built

`StudentTopicAssignment` is **existing infrastructure**, not new work:

| Piece | Location | Status |
|---|---|---|
| Entity | `Students.Core/Domain/StudentTopicAssignment.cs` | ✅ `StudentId`, `TopicId`, `PeriodId`, `SubPeriodId?`, `IsOverride`, `SourceType`, `TenantId`, `RowVersion` |
| EF config | `Data/Configurations/StudentTopicAssignmentConfiguration.cs` | ✅ unique `(tenant_id, student_id, topic_id, period_id)`; index `(tenant_id, period_id)` |
| Create command | `CQRS/StudentTopicAssignments/Commands/AssignStudentTopic` | ✅ validates active year, `PeriodId` must equal active year, resolves `SubPeriodId` |
| Remove command | `…/RemoveStudentTopic` | ✅ |
| Repository | `Data/Repositories/StudentTopicAssignmentRepository.cs` | ✅ `ListByStudentAsync`, `ListByPeriodAsync` |
| Routes | `/students/student-topics/by-student/{id}/period/{periodId}`, `/students/student-topics/by-period/{periodId}` | ✅ |
| `SubjectAssignmentSource` | `GradeAssignment = 0`, `IndividualAssignment = 1` | ✅ |
| **UI** | — | ❌ **none** |
| **Seeder** | — | ❌ **none** |
| **Reader** | — | ❌ nothing outside its own CQRS slice |

The work is therefore: add the flag, add the union query (FR-ER-16), add the
eligibility endpoint (FR-ER-14), filter in `StudentsContactResolver`
(FR-ER-13), and build the two UI surfaces (§8).

---

## 10. API surface

All in `SchoolCollab.Students.Api`, grouped per `endpoint-organization-pattern.md`.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/students/topic-enrollment?gradeLevelId&topicId&periodId` | eligible student ids (FR-ER-14); returns the whole audience when the subject is `false` |
| `GET` | `/students/students/{id}/subjects?gradeLevelId` | the union of §6 (FR-ER-16) |
| `PUT` | `/students/topic-assignments/{id}/requires-enrollment` | set the flag; server-side grandfather (FR-ER-7) |
| `POST` | `/students/topic-assignments/{id}/enroll-bulk` | "enrol the remaining N" (FR-ER-8) |
| `POST` | `/students/topic-assignments/rollover` | grades × subjects carry-forward commit (FR-ER-22) |

Existing `AssignStudentTopic` / `RemoveStudentTopic` are reused unchanged for
per-student enrolment.

**No feature flag.** This adds a gate rather than a parallel implementation, and
its default (`false`) preserves current behaviour exactly. A flag would mean two
divergent enrolment rules for the same data.

---

## 11. Known interim state — activity groups

Per decisions 3 and 10, `ActivityGroupTopicAssignment` rows are created with
`RequiresEnrollment = true`, but **nothing reads the flag for groups in v1**. So
the stored state is "flag set, unenforced".

This is deliberate and safe: activity groups are dark-launched behind
`FEATURE:EnableActivityGroups`, `ActivityGroupTopicAssignment` has no UI and no
seed data, and the flag is not consulted on any group code path. It is recorded
here so the state is not later mistaken for a bug — and so that if group subject
enrolment is ever built, the default is already the safe one.

Unrelated finding, worth its own note: the **group ↔ subject bridge is fully
built and entirely unexercised** — entity, TPH discriminator, `AssignActivityGroupTopic`
command, `ListActivityGroupTopicAssignments` and `ListTopicsByGroup` queries, a
route in `TopicAssignmentRoutes.cs:36-40`, and period/tag update handlers that
branch on the subtype. No `.razor` references it and nothing seeds it. It is dead
scaffolding, not merely unfinished.

## 12. Out of scope

- **Activity-group enrolment enforcement** — flag set, not read (§11).
- **Subscription-only activity groups** — deferred by decision 11. Note that when
  it lands it would *force* `RequiresEnrollment = true` for such a group, so it
  must be sequenced **with** this work, not after it.
- The **grade students × subjects matrix** (FR-ER-26).
- A **durable record of excluded students** on a published assignment
  (decision 12, accepted).
- Period/blocking behaviour — `subject-period-exception-model.md`.

---

## 13. Testing requirements

Per `.github/copilot/rules/testing.md` (MSTest + Moq + FluentAssertions, MTP) and
`.github/copilot/rules/dotnet-best-practices.md`.

**Domain / handler unit tests**

- `GradeTopicAssignment.Create` ⇒ `RequiresEnrollment == false`;
  `ActivityGroupTopicAssignment.Create` ⇒ `true` (FR-ER-2). Mutation-verified:
  inverting either assertion must fail.
- `SetRequiresEnrollment` no-ops and does not bump `UpdatedAt` when unchanged
  (the `GradeLevel.SetEnrollmentBlocked` precedent).
- Enrolment: `false` subject + no rows ⇒ enrolled. `true` + row for the active
  year ⇒ enrolled. `true` + no row ⇒ not enrolled. `true` + a row for the
  **prior** year ⇒ not enrolled (the rollover regression, FR-ER-3).
- Grandfather: flipping `true` materialises one row per current audience member,
  and a partial grandfather is rejected (FR-ER-7/10).
- Flipping back to `false` leaves rows intact and enrols everyone (FR-ER-9).
- Union query: a grade with one `false` and one `true` subject returns both for an
  enrolled student, and only the `false` one for an unenrolled student
  (FR-ER-16).

**Rollover**

- Carry-forward writes rows against the new year and does not conflict with the
  prior year's rows (FR-ER-22).
- A second commit for the same year **replaces** rather than appends (FR-ER-23).
- A student who left the grade is not offered (FR-ER-21).

**Publish / cross-context**

- An unenrolled student is excluded from the recipient list while publish
  succeeds (FR-ER-11).
- `ITopicAssignmentLookup` is **not** called for eligibility and its signature is
  unchanged (FR-ER-12) — a regression guard on the contract.
- `StudentsContactResolver` is tested **directly** (it is the whole seam —
  FR-ER-13a), with `IHttpClientFactory` returning a fake handler: enrolled
  students pass through, unenrolled are dropped, and the resolver still returns
  contacts.
- `ResolveSubscribersRequest.TopicId` flows through to the eligibility request
  (FR-ER-13b).
- The eligibility endpoint is called through the **existing** `students-api`
  client name — a new base address would fail `CrossModuleWiringTests` and the
  CI **Cross-module wiring guard** job (FR-ER-16b).
- The eligibility endpoint **fails open**: an error from it still publishes
  (FR-ER-15).
- The recipient list for a `false` subject is byte-identical to pre-change
  behaviour — the compatibility assertion that matters most.

**bUnit**

- Student page: both sections render; a `false` subject shows no per-student
  control; a `true` subject shows the enrolled/not state (FR-ER-24, FR-ER-28).
- Grade card: the toggle warns with the audience and unenrolled counts and offers
  the bulk action; the read-only "20 of 30 enrolled" count renders (FR-ER-25/26).
- The rollover screen edits the selection before committing, and the committed
  set is exactly what was approved — assert on the list state, not just the
  callback (`.github/copilot/rules/testing.md` §4).

**Integration**

- The full-year cycle: enrol 20 of 30 → roll over → approve 18 → next year reads
  18. This is the end-to-end guard for FR-ER-3/18-23.
