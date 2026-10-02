# Round — assignment-targeting-r2
Provider: pi/ollama-cloud · Tier 3 (full — schema + migration + cross-context port + endpoint + UI) · Option A
Models: orchestrator `ollama-cloud/glm-5.3-flash` · plan-review `ollama-cloud/glm-5.3` · worker `ollama-cloud/deepseek-v4.1-flash` · reviewer `ollama-cloud/kimi-k2.7-code` · UI tester `ollama-cloud/minimax-m3`
Round base: `565e48f8dada3a2d5c9dfd8113b1b9d412173feb` (== tip of stack #290)
Branch: `stack/4-assignment-targeting-r2` (based on the train tip because R2 builds on R1's code; the train is green but UNMERGED)
Patch freeze: `git add -N src tests && git diff 565e48f8 -- src tests documents/specs/assignment-authoring-compartments.md > documents/rounds/diffs-assignment-targeting-r2.patch`

> The spec path in that pathspec carries the **TGT-12 correction** landed during plan-review (resolution of plan-review **P2-n1**: the corrected spec text otherwise sits uncommitted and is missed by a `-- src tests` pathspec). Plan-review P2-n2 (preview `primaryGradeId`) is dispatched to the worker as an explicit instruction, not left as a residual.

## Plan

### Goal

Implement **R2** of `documents/specs/assignment-authoring-compartments.md` (§14 R2 row): the
multi-constraint targeting model — `AssignmentTarget` child rows + migration/backfill (TGT-1,
TGT-2, TGT-14, TGT-15), the primary-grade contract (TGT-11, TGT-12), the
`IAssignmentTargetResolver` port + Students `by-target` endpoint with fail-closed publish
(TGT-3 … TGT-10, TGT-13), and the Audience & Targets compartment editor + live recipient
preview (TGT-16). Targeting leaves the single-choice `TargetAudienceType` pickers and becomes
child rows on the aggregate.

**Spec authority:** `documents/specs/assignment-authoring-compartments.md` §7 (TGT-1…TGT-16),
§3 compartment 2, §5 UX-9/UX-21, §11, §14, §16; `documents/specs/assignment-policy.md` §2
(resolver fail-open/fail-closed posture, caps); `documents/solution/assignment-request-go-forward-breakdown.md`
(WS-A seams: `ResolveSubscribersRequest`, `IActivityGroupLookup`, FR-18/20/21/22/23, EC-4/EC-7).
This plan is the dispatch authority; open the spec only to resolve ambiguity.

### Settled design decisions (this pass)

These six questions are **settled here**; the worker implements them as written.

**D-1 — Single source of truth.** `AssignmentTarget` child rows are the sole authored target
source. `TargetAudienceType` and `GradeLevelId` remain **real, non-authored columns** kept for
one release (TGT-15 compat: the list DTO, `Admin` list surface and ward projection keep
compiling unchanged). They are **derived at every write point**, in one private aggregate
method `SyncDerivedTargeting()` on `Assignment`, called at the end of `Create`, `Update` and
`SetTargets`:

- an `AllStudents` target present ⇒ `TargetAudienceType = AllStudents`;
- else ≥1 `GradeLevel` target ⇒ `SelectedGrades`;
- else ≥1 `ActivityGroup` target ⇒ `SelectedGroups`;
- else (`Stream`/`Student`-only mixes, nothing else matches) ⇒ the **new enum member
  `TargetAudienceType.Mixed = 3`** (desc "Mixed") — this member is additive and is the only
  change to the legacy enum.

`GradeLevelId` is **not derived from targets** — it is re-purposed as the authored **primary
grade** field (D-2 below; TGT-11 says "reusing `GradeLevelId`"). Consumers of the compat pair
(`ListAssignmentsQueryHandler`, `GetAssignmentByIdQueryHandler`,
`GetWardAssignmentViewQueryHandler`, ward DTO) need **no code change**: the columns they
already read carry the derived values.

**D-2 — Primary grade (TGT-11/TGT-12).** `GradeLevelId` stays the scalar "Primary grade"
field authored in Basics (R1 already renders it). Validation rule: when the target set
contains ≥2 distinct `GradeLevel` targets, `GradeLevelId` must be **non-null and equal to one
of the targeted grade ids** (server-enforced in `SetTargets`; UI shows the existing grade
select and marks it required; disabled only when ≤1 grade target exists, where it auto-derives
from the single grade target). When the target set contains no grade target (`AllStudents`,
`Student`-only, `Stream`-only), `GradeLevelId` may be null — the authoring defaults
(`RequiresSignature` snapshot) and the approval gate (`RequiresApprovalBeforePublish`) then
resolve from **tenant-level policy only** (the resolver already handles a null grade by
falling through to the tenant leg / built-in defaults — fail-open, unchanged).

**Notification policy — real behaviour, one policy (TGT-12 spec deviation):**
`PublishAssignmentCommandHandler` resolves **ONE effective notification policy** from
`assignment.GradeLevelId` (`PublishAssignmentCommandHandler.cs:45,57` —
`assignmentPolicyResolver.ResolveAsync(GradeLevelId)` +
`policyResolver.ResolveEffectiveAsync(tenantId, GradeLevelId)`) and applies that single policy
to the **whole sendout**. There is **no per-recipient cap application** and no per-recipient
policy: an earlier draft of this plan claimed "per-recipient notification caps resolve per
recipient's grade" — that is FALSE against today's code and was removed. R2 keeps this
single-policy seam **unchanged** (Pass 5 asserts it as an unchanged-behaviour seam, not a
multi-policy split). This leaves the TGT-12 gap (spec says per-recipient caps/channels; code
is per-assignment single-policy from the primary grade) as a **recorded spec deviation**:
deviation text is to be written into
`documents/specs/assignment-authoring-compartments.md` in a follow-up pass — the spec file is
**not edited in this round**.

**D-3 — Backfill safety/idempotency.** The backfill runs **inside the additive migration's
`Up()`** (transactional — Npgsql DDL+DML in one transaction, same model as the
`DedupeNotificationLogPublishRows` ar-19 precedent), as SQL with `NOT EXISTS` guards on `(assignment_id, kind, ref_id)`. **The rules are an
exclusive precedence, not cumulative.** The legacy model enforces no exclusivity
(`LinkAssignmentGroupsHandler` never checks `TargetAudienceType`; `Assignment.Create`
permits `GradeLevelId` alongside `AllStudents`/`SelectedGroups`), so `AllStudents +
GradeLevel + ActivityGroup` coexist on legacy rows — a combination a `SetTargets` call
would reject. Precedence (first matching rule wins, only its rows are inserted):

1. `TargetAudienceType = AllStudents` ⇒ exactly one `AllStudents` row (`ref_id NULL`) — **no
   grade, stream, student or group rows for that assignment**. The guard for this rule must
   be **NULL-safe**: `NOT EXISTS (… WHERE assignment_id = a.id AND kind = 'AllStudents' AND
   ref_id IS NULL)` — a plain `(assignment_id, kind, ref_id)` equality `NOT EXISTS` never
   matches a NULL `ref_id` and would double-insert on retry.
2. `TargetAudienceType = SelectedGrades` OR (`grade_level_id IS NOT NULL` AND rule 1 did not
   match) ⇒ exactly one `GradeLevel` row (`ref_id = grade_level_id`). **Grade precedes
   groups**: legacy rows that combine a grade with group links backfill only the grade row
   (group links are ignored), keeping the backfilled set valid under `SetTargets`.
3. `TargetAudienceType = SelectedGroups` with null `grade_level_id` ⇒ one `ActivityGroup` row
   per `assignment_activity_groups` link (rows ordered by `created_at`, then `id`; inserted in
   `DisplayOrder` 0..n-1);
4. **no backfill for `TargetAudienceType.SelectedGrades` without a `GradeLevelId`** —
   impossible by the existing create/update guard.

**Zero-target backfill is real and accepted:** legacy rows never *guaranteed* produce zero
targets — a legacy **Draft `SelectedGroups` with no link rows and a null `GradeLevelId`
backfills to ZERO target rows** (the single-choice model's all-else fallback matches none of
the rules above). Such an assignment keeps publishing-refused semantics: D-6(a)/TGT-13 refuse
publish until it is re-authored. This legacy class (Draft, `SelectedGroups`, 0 links, null
grade) is seeded in the backfill integration test and asserted to backfill zero rows.

The single-choice model guarantees ≤1 grade and ≤N groups per assignment, so a plain pass
cannot produce duplicates within a kind; `NOT EXISTS` (NULL-safe for the AllStudents rule per
rule 1) makes any retry (MigrationService re-run after a mid-migration failure) a no-op.
`DisplayOrder`: `AllStudents`/`GradeLevel` = 0, activity-group
rows 0..n-1 in created order. The migration is **additive only** — no legacy column is dropped
or altered (TGT-15), `NoUncommittedModelChanges` green.

**D-4 — Preview endpoint shape, debounce, failure posture.** New Assignments-API route
`GET /assignments/recipient-preview?allStudents=&gradeLevelIds=&streamCodedValueIds=&studentIds=&activityGroupIds=`
→ `200 RecipientPreviewDto(int StudentsMatched, int PrimaryContacts, int OtherContacts)`.
`StudentsMatched` = the resolver's deduped union. `PrimaryContacts`/`OtherContacts` = the
**policy-filtered** contact counts: resolved ids fed through the publish handler's own
resolver chain — `IContactResolver.ResolveSubscribersAsync(new ResolveSubscribersRequest(tenantId,
Scope.AllAssignments, gradeLevelId, StudentIds))` then `NotificationRecipientFilter.Apply`
with the two fail-open policy resolvers — so "contacts reachable" means exactly what publish
will send, and policy resolution stays fail-open per `assignment-policy.md` §2. **Failure
posture:** the preview is **advisory** — any transport/resolve failure (or a missing current
period) returns `200` with all-zero counts plus `PreviewDegraded = true` (the UI renders an
inline "Preview unavailable" note in the compartment; save is never blocked and publish
re-resolves fresh server-side — TGT-16). Debounce: the UI calls the endpoint on a **500 ms
trailing debounce** after the last constraint change, with stale-response cancellation; the
call runs even in Draft mode without persisting.

**D-5 — Streams are grade-agnostic (TGT-5) and how that is validated.** `Stream` stores
`RefId = StreamCodedValueId`; resolution joins active `StudentEnrollment.StreamCodedValueId`
for the current period — the `GradeStreamAssignment` M:N bridge plays **no part** in target
resolution (the picker may group streams under their offering grade — read the bridge for
display grouping only; grade is never stored in the constraint). Validation posture: stream
ids are **bare cross-context refs, unchecked at save** — the exact precedent of
`StudentEnrollment.StreamCodedValueId` and `GradeStreamAssignment.StreamCodedValueId`
(no FK; coded values live in the Settings DB). A dangling stream id cannot publish:
resolution yields only real enrollments, and an empty resolved set is fail-closed (D-6 ⇒
publish blocked, TGT-10). The UI's stream picker sources its options from the existing
grade-stream listing so only real ids are offered; a stale id degrades to "no students match"
in the preview.

**D-6 — Fail-closed publish (TGT-10, TGT-13).** In `PublishAssignmentCommandHandler`, target
resolution replaces the `TargetAudienceType` if/else. `IAssignmentTargetResolver` returns the
deduped union of matching **active, not-soft-deleted student ids**.

**The `allStudents` leg is explicit on every surface:**

- `IAssignmentTargetResolver.ResolveStudentIdsAsync(targets, bool includeAllStudents, ct)` —
  the caller passes `true` iff the target set contains an `AllStudents` row;
- the Students query `ResolveStudentsByTarget` carries an `AllStudents` boolean — `true`
  means **all active, non-soft-deleted students of the tenant** (no grade/period predicate,
  unioned and deduped with the other legs);
- the Students route `GET /students/by-target` and the preview
  `GET /assignments/recipient-preview` both accept `allStudents=` (query-bound boolean).

Publish refuses when:

(a) the assignment has **no target rows** (TGT-13 — mirrors today's FR-23 empty-groups
`InvalidOperationException`, mapped to **400 only** by the route —
`AssignmentRoutes.cs:466-471` maps `InvalidOperationException` to `Results.BadRequest`; there
is no 409 mapping on this surface today and none is added);
(b) the resolver **throws** (transport/HTTP failure propagates — publish is blocked, never
degrades to publish-to-nobody/everybody); (c) the resolved set is **empty**. This is the
deliberate mirror of the fail-open policy resolver (`assignment-policy.md` §2).

**Owned semantic change — legacy `AllStudents` recipient scope:** backfilling legacy
`TargetAudienceType = AllStudents` rows **changes the recipient scope of existing Everyone
assignments on their next publish**. Pre-R2, a grade-less "Everyone" assignment resolved to
**zero subscribers** — the legacy if/else fell through to the grade cohort and
`GradeLevelId` was null. Post-R2, the `AllStudents` target resolves to all
active, non-soft-deleted tenant students, so a previously-silent assignment now publishes to
the whole tenant (and a previously-always-refused publish now passes, because the resolved set
is non-empty and fail-closed D-6 is satisfied). This is accepted, deliberate behaviour — not
preservation.

**Topic gate for `AllStudents` — decided explicitly, not "preserved":** the FR-58 gate set is
derived **exclusively from the kinded target rows**: `GradeLevel` targets run the grade gate
(as today via `IsTopicAssignedAsync(gradeId, [], topicId, effectiveDate)`), `ActivityGroup`
targets run the group gate (`IsTopicAssignedAsync(null, groupIds, topicId, effectiveDate)`).
The **`AllStudents` leg runs NO topic gate**, and an `AllStudents` target **with an authored
primary grade runs no grade gate either** — the primary grade is a policy/authoring field
(D-2), not a delivery constraint; it rides only `ResolveSubscribersRequest.GradeLevelId` for
the teacher-recipient leg. (Pre-R2 the grade-less Everyone case never reached a gate because
it resolved nothing; post-R2 the tenant-wide leg is ungated by design.) `Stream`/`Student`
legs impose no topic gate.

**Documented widening (accepted):** because the publish flow keeps
`ResolveSubscribersRequest.GradeLevelId = assignment.GradeLevelId`, a **group-only assignment
with an authored primary grade will newly notify that grade's teachers**
(`StudentsContactResolver.cs:54-68` includes the `grade-levels/{id}/teachers` cohort whenever
`GradeLevelId` is set). Pre-R2 a group-only resolve reached no grade cohort and therefore
notified no grade teachers. Accepted as part of keeping the primary-grade teacher leg intact.

**D-7 — Multi-tenant safety of the new endpoint.** The Students `by-target` route is mapped on
the **existing `studentsGroup`** in `StudentEndpoints.cs`, so it inherits
`RequireAuthorization` + `BearerScheme` exactly like `MapEnrollmentRoutes`/`MapStudentRoutes`
(ar-24 posture). The handler queries `StudentsDbContext` entities that all carry the global
tenant query filter (`Student`, `StudentEnrollment`, `ActivityGroup`,
`ActivityGroupMembership` are strict tenant entities); tenant scoping comes from the
handler's request context, not from a forwarded header. The Assignments-side HTTP client is
the **existing `students-api` named client registration reused unchanged — exactly like
`StudentsContactResolver`**: that chain carries only
`BearerForwardingDelegatingHandler` (`Assignments.Api/Program.cs:112-114`); there is **no
`TenantForwardingDelegatingHandler` in the assignment-side client chain**, and none is added.
The Assignments `recipient-preview` route sits on the existing assignment group and inherits
its authorization unchanged.

**D-8 — R1b residuals + legacy write-path bypasses: folded in.** All residuals are folded
(rationale: R2 rewires exactly the surfaces they live in, so deferring would create churn
inside this round's own files):

1. **Archived-group relink — save-time off-group rejection REINSTATED (no spec amendment).**
   The earlier draft of this decision dropped `SetTargets`' archived-group rejection and
   called that an "accepted semantics change" — **that claim is deleted**; it conflicts with
   `documents/specs/activity-group-enrollment.md` FR-22 (:619-620), AC-15 (:828-830) and
   EC-4 (:988-994), which are taken as-is. The minimal fix instead:
   - `SetTargets` validates group ids via `IActivityGroupLookup.GetByIdsAsync` — which already
     returns `IsActive`, so the check is free — and **rejects with the existing off-group
     error any NEWLY-ADDED activity-group id whose group is not active**;
   - an **unchanged, already-persisted target id pointing at a group archived after linking**
     is **allowed to re-save** (unchanged ids are not re-validated, so the historical row is
     not silently dropped and the pre-R2 re-save-without-touching flow keeps working);
   - archived state still filters recipients at resolution (EC-4) and the publish-time topic
     gate still requires the group's subject assignment.
2. **Two live surfaces that bypass target rows — disposition stated (both wired):**
   - **`PUT /assignments/{id}/groups` → `LinkAssignmentGroupsHandler` →
     `ReplaceForAssignmentAsync`** (still the R1 UI's persist path): it writes only the link
     table, so it can diverge from the `AssignmentTarget` rows and re-stale the derived
     compat columns on every call. Disposition: the route+handler becomes a **thin adapter
     over `SetTargets`** — it maps the posted group ids onto the full replacement target set
     (existing rows preserved for unchanged kinds, group rows swapped for the posted set),
     letting `SetTargets` + `SyncDerivedTargeting()` own validation and derivation. The
     link-table write (`ReplaceForAssignmentAsync`) is then only reachable through the
     aggregate. (If implementation shows the adapter duplicates the UI's target path exactly,
     the handler may be **deleted** and the route re-pointed at `SetTargets` — either
     disposition is acceptable; a divergent second write path is not.)
   - **FR-6 hard-delete guard** reads references only from the link table, so a hard-deleted
     group could strand dangling `AssignmentTarget` rows. Disposition: the guard's read
     (backed by `ListAssignmentsForGroup`) is **extended to count `AssignmentTarget` rows
     with `Kind = ActivityGroup` and `RefId = {groupId}`** alongside the link-table read, so
     a group referenced by targets still blocks the hard-delete with "Assignments".
3. **`ReloadAsync` not re-reading children** — the authoring form's save→reload path now
   re-issues the existing `GET /assignments/{id}/authoring` children read (R1b load-half
   route) which is **extended with the target rows**, so after every successful save the
   page re-reads targets + children from the server instead of navigating away on stale state.

### Implementation passes (keep the build green after each)

**Pass 1 — AssignmentTarget domain + EF + migration/backfill**

- New `src/Assignments/SchoolCollab.Assignments.Core/Domain/TargetKind.cs`:
  `enum TargetKind { AllStudents = 0, GradeLevel = 1, Stream = 2, Student = 3, ActivityGroup = 4 }`
  (wire form = string name per the repo's enum convention).
- New `.../Domain/AssignmentTarget.cs`: `ITenantEntity` + `IEntity` + `IAuditableEntity`
  (direct tenancy — operational data, the `AssignmentActivityGroup` precedent), private ctor,
  `Create(tenantId, assignmentId, kind, refId, displayOrder)` validating: `RefId != null` and
  non-empty **iff** `Kind != AllStudents` (TGT-2), `DisplayOrder >= 0`.
- `Assignment` aggregate: `_targets` list, `IReadOnlyList<AssignmentTarget> Targets`,
  `SetTargets(IReadOnlyList<(TargetKind Kind, Guid? RefId)> targets, Guid tenantId)` —
  **full-replacement when non-null / preserve-when-null** exactly matching the
  questions/attachments/module semantics (UX-21), validations: TGT-13 at-least-one, TGT-2
  AllStudents exclusivity + uniqueness (≤1 AllStudents row), per-kind duplicate (Kind, RefId)
  rejection, the D-2 primary-grade rule (≥2 grade targets ⇒ `GradeLevelId` ∈ those grades),
  and the **D-8.1 off-group rule: newly-added `ActivityGroup` ids whose group is not `IsActive`
  (per `IActivityGroupLookup.GetByIdsAsync`) are rejected; unchanged already-persisted ids are
  not re-validated**;
- `SyncDerivedTargeting()` per D-1; `CreateAssignmentCommandHandler` /
  `UpdateAssignmentCommandHandler` thread a new contract `Targets` array into `Create`/`Update`.
- `TargetAudienceType`: add `Mixed = 3` with `[Description("Mixed")]`.
- New `.../Data/Configurations/AssignmentTargetConfiguration.cs`: FK → assignments cascade,
  `Kind` conversion to string, `RefId` nullable, **unique index (assignment_id, kind, ref_id)**
  plus a **partial unique index `WHERE kind = 'AllStudents' AND ref_id IS NULL`** (one
  AllStudents row per assignment — Postgres partial index, the ar-19 precedent), tenant + audit
  columns mapped like `AssignmentActivityGroupConfiguration`.
- Additive migration `AddAssignmentTargets` (table + indexes) with the **`NOT EXISTS`-guarded
  SQL backfill per D-3**; model snapshot updated; `NoUncommittedModelChanges` green.

**Pass 2 — Resolver port + Students endpoint + publish integration**

- New `.../Services/IAssignmentTargetResolver.cs` in `Assignments.Core`:
  `sealed record TargetConstraint(TargetKind Kind, Guid? RefId)` and
  `Task<Guid[]> ResolveStudentIdsAsync(IReadOnlyList<TargetConstraint> targets,
  bool includeAllStudents, CancellationToken ct)` — `includeAllStudents` is `true` iff the
  target set contains an `AllStudents` row, and it opens the tenant-wide all-students leg per
  D-6 (empty constraint list and `false` ⇒ empty array; HTTP/transport exceptions
  **propagate** — never swallowed to empty). Interface lives in Core; the HTTP client lives in
  Api (the `StudentsContactResolver`/`ActivityGroupLookupHttpClient` precedent).
- New Students query `.../CQRS/Students/Queries/ResolveStudentsByTarget/`:
  `ResolveStudentsByTarget(bool AllStudents, IReadOnlyList<Guid> GradeLevelIds,
  IReadOnlyList<Guid> StreamCodedValueIds, IReadOnlyList<Guid> StudentIds,
  IReadOnlyList<Guid> ActivityGroupIds)` with a handler that derives the
  **current period** exactly as `ListStudentsByGradeHandler` does
  (`Periods.StartDate <= today <= EndDate`; no current period ⇒ no matches) and returns the
  union of deduped student ids as `Guid[]`:
  - **allStudents leg — `Students` where `NOT IsDeleted` and active (enrolled-active
    posture), tenant-wide — no grade, period or group predicate** (D-6 definition: all
    active, non-soft-deleted students of the tenant);
  - grade leg — `StudentEnrollments` joined on `GradeLevelId IN grades`, active status,
    current period (the real join shape of `ListStudentsByGradeHandler`);
  - grade leg — `StudentEnrollments` joined on `GradeLevelId IN grades`, active status,
    current period (the real join shape of `ListStudentsByGradeHandler`);
  - stream leg — `StudentEnrollments.StreamCodedValueId IN streams`, active, current period,
    **no grade predicate** (D-5);
  - student leg — `Students.Id IN ids AND NOT IsDeleted`;
  - group leg — `ActivityGroupMemberships` with **`Status = Active` only — no period check**,
    pinned to today's semantics (pre-R2 publish includes any `Status=Active` membership,
    `ActivityGroupLookupHttpClient.cs:78-86`; do NOT narrow it under R2) joined to
    `ActivityGroups` with `IsActive = true` (EC-4 — archived/suspended collapse to
    `IsActive=false`).
- New route in `src/Students/SchoolCollab.Students.Api/Endpoints/StudentRoutes.cs`:
  `GET /students/by-target?allStudents=&gradeLevelIds=&streamCodedValueIds=&studentIds=&activityGroupIds=`
  (query-bound `bool` + `Guid[]` per `EnrollmentRoutes`' `by-students` precedent; inherits the group's
  RequireAuthorization).
- `src/Assignments/SchoolCollab.Assignments.Api/Services/AssignmentTargetResolverHttpClient.cs`:
  named client `students-api`, `GetFromJsonAsync<Guid[]>`, exceptions propagate (fail-closed);
  DI registration in Assignments.Api `Program.cs`.
- `PublishAssignmentCommandHandler`: replace the `TargetAudienceType` branch — load
  `assignment.Targets` (empty ⇒ `InvalidOperationException` TGT-13); call
  `ResolveStudentIdsAsync(targets, includeAllStudents)`; empty/failed ⇒ publish refused (D-6); **feed the resolved ids as
  `ResolveSubscribersRequest.StudentIds`** in the single existing flow (keep `GradeLevelId`
  on the request so the primary-grade teacher-recipient leg is preserved when a primary grade
  exists — this is the D-6 documented widening for group-only+primary-grade assignments,
  which newly notifies that grade's teachers);
  run the per-kind topic gate per D-6 (AllStudents ⇒ no gate, incl. no gate for a coexisting
  primary grade). All FR-23/EC-4/EC-7 messages preserved.
- **Legacy group route disposition (D-8.2)**: `PUT /assignments/{id}/groups` →
  `LinkAssignmentGroupsHandler` becomes a **thin adapter over `SetTargets`** (mapping posted
  group ids onto the full replacement target set so validation + `SyncDerivedTargeting()`
  run through the aggregate; `ReplaceForAssignmentAsync` no longer called directly from the
  handler), or the handler is deleted and the route re-pointed at `SetTargets` — per D-8.2
  either disposition is acceptable, divergence is not.
- **FR-6 hard-delete guard read (D-8.2)**: the Assignments-side read behind
  `GET /activity-groups/{id}/assignments` (`ListAssignmentsForGroup`) also counts
  `AssignmentTarget` rows with `Kind = ActivityGroup` alongside the link table, so a
  hard-deleted group cannot strand dangling targets.

**Pass 3 — Contracts, handlers, preview endpoint**

- `.../Contracts/ContractTypes.cs`: `record AssignmentTargetDto(int Kind, Guid? RefId, int DisplayOrder)`
  (enum-as-int wire; DTO naming follows the existing `TargetAudienceTypeDto` style);
  `Targets` appended (default `null` = preserve) on `CreateAssignmentRequest` /
  `UpdateAssignmentRequest`; derived compat members **unchanged** (D-1); new
  `RecipientPreviewDto(int StudentsMatched, int PrimaryContacts, int OtherContacts, bool PreviewDegraded)`.
- Assignments.Api route `GET /assignments/recipient-preview` per D-4 (query-bound, same
  `MapAssignmentEndpoints`-style group; resolver + contact + policy resolution reused as
  described).
- `GetAssignmentAuthoringChildren` handler: include `AssignmentTargetDto[]` in the
  authoring children DTO (D-8 fold-in).
- `DuplicateAssignmentCommandHandler`: duplicate target rows (not recipients).

**Pass 4 — UI (Audience & Targets compartment + preview, TGT-16)**

- `AssignmentAuthoring.razor` compartment 2 rebuilt as the **multi-kind target editor**:
  - "Everyone" toggle — when on, all constraint pickers disabled-with-reason (TGT-2/UX-17) and
    the target set is a single `AllStudents` row;
  - grade multi-select (raw `FluentSelect Multiple` + `SelectedOptions`/`SelectedOptionsChanged`
    per `.github/skills/dropdown-ui/SKILL.md` and the `TeacherEditDialog` precedent — **never
    `@bind-SelectedValues`**), grouped options per grade name;
  - stream picker grouped under its offering grade for legibility (display grouping from the
    grade-stream listing only — D-5), storing `StreamCodedValueId` bare;
  - student picker — `FluentAutocomplete` server-search over `GET /students?search=`
    (the TGT-6 shape, mirroring the existing admin student search);
  - activity-group picker (existing `FluentSelect Multiple` repair lineage);
  - selected targets render as a chip list in `DisplayOrder` (chips carry the kind label +
    display name; removal re-orders 0..n-1);
  - the legacy single-choice "Target audience" select is **replaced** (it stops being a field;
    the derived value shows read-only in View mode only); "Primary grade" (Basics) gains the
    required-within-targeted-grades interaction from D-2;
  - form model: `AssignmentEditFormModel` gains the targets list, load via the authoring
    children read (fail-closed disabled-with-reason on read failure — the R1 P1 load-half
    posture, UX-21), full-replacement persist on save when loaded-set differs from selection
    (change-gated — the R1 group-picker pattern).
- Live recipient preview: a read-only line + small breakdown ("Students matched: 23 ·
  Contacts reachable: 31 (18 primary / 13 other)"), server-resolved, **500 ms trailing
  debounce**, stale-cancellation, degraded state renders the inline "Preview unavailable"
  note (D-4); never blocks save; publish re-resolves.
- Styling: section-card compartment, `FormRow`, the repo input-width ladder, isolated
  `.razor.css` (no inline styles) — `.github/copilot/rules/blazor-components.md` rules apply.

**Pass 5 — Tests**

- Unit (Assignments.Core): `AssignmentTargetTests` — create/validation (TGT-2 exclusivity,
  RefId per kind, TGT-13), `SetTargets` full-replacement + preserve-when-null, D-2
  primary-grade rule, D-8.1 off-group rule (**newly-added archived group id rejected;
  unchanged already-persisted archived id re-saves**), `SyncDerivedTargeting` matrix (D-1:
  AllStudents / grades / groups /
  Mixed derivation, `GradeLevelId` untouched by derivation).
- Unit (publish): `PublishAssignmentCommandHandlerTests` targets path — union of two kinds
  deduped (TGT-3); archived group excluded (TGT-7/EC-4); resolver throws ⇒ publish refused
  with the specific exception (TGT-10); empty resolved set ⇒ refused; ≥2 grade targets with a
  `GradeLevelId` outside them ⇒ refused; **the unresolved TGT-12 point is asserted as the
  UNCHANGED single-policy seam**: the handler resolves ONE effective notification policy from
  `assignment.GradeLevelId` and applies that single policy to the whole sendout (D-2) — the
  old plan text asserting "per-recipient cap application on a two-grade recipient set" was
  UNIMPLEMENTABLE against the real code and is replaced by this single-policy assertion;
  resolver contract honours the `allStudents` leg (D-6: `includeAllStudents` ⇒ tenant-wide
  leg, not an empty union).
- Unit (Students): `ResolveStudentsByTargetHandlerTests` — legs incl. the **allStudents leg**
  (tenant-wide, active, non-soft-deleted — `IsDeleted` excluded, no period predicate), other
  legs, union + dedupe, archived
  group excluded, current-period fallback, no-grade-predicate
  stream leg (D-5).
- Route tests: `by-target` route binds query arrays and maps handler exceptions; Students.Api
  route test pinned on `GET /students/by-target` (would 404 against pre-R2 code);
  `recipient-preview` route test incl. the degraded (all-zero + `PreviewDegraded`) shape;
  `by-target`/`recipient-preview` route tests pin the `allStudents=` boolean binding.
- Integration (backfill — Testcontainers `Assignments.Tests.Integration` ar-19 suite):
  seed legacy rows (AllStudents / grade / group links, plus the **Draft `SelectedGroups` with 0
links and null `GradeLevelId` zero-backfill class per D-3**), apply the migration, assert exactly
  the backfilled target rows incl. the **exclusive-precedence cases** (an AllStudents+grade
  legacy row backfills only the AllStudents row; a grade+groups legacy row backfills only the
  grade row) and the NULL-safe `NOT EXISTS` idempotency (re-running the backfill block
  inserts nothing, including the NULL-`ref_id` AllStudents row).
- Unit: `LinkAssignmentGroups` adapter test (thin adapter delegating to `SetTargets`,
  D-8.2) and the delete-guard read test (`ListAssignmentsForGroup` counting `AssignmentTarget`
  rows alongside link-table rows).
- bUnit (`AssignmentAuthoringAudienceBunitTests`): multi-kind pickers render; "Everyone"
  disables constraint pickers with a reason (bUnit asserting disabled-with-reason, not hidden);
  target chips render in `DisplayOrder`; preview debounce fires once after rapid changes and
  renders the counts (**uses fake timers — the 500 ms trailing debounce cannot be asserted
  with real time in bUnit**); preview failure renders the inline degraded note; Edit mode loads
  persisted targets before the editor enables (add-one-target preserves loaded targets);
  no stray attribute regressions (the R1 P1-b guard).

**Pass 6 — Final gates**

- `dotnet build SchoolCollab.slnx` → 0 errors.
- Affected suites + `SchoolCollab.ArchitectureTests.Unit` → 0 failures (the dotnet-best-
  practices "Never" list is CI-enforced by the architecture tests).
- Write the patch once (P1-6: `git add -N` so untracked new files appear in the diff, and
  pathspec-limited to `src tests` so the artifact never embeds itself):
  ```
  git add -N src tests && git diff 565e48f8 -- src tests > documents/rounds/diffs-assignment-targeting-r2.patch
  ```
  Caveat: the command as written captures only `src` and `tests`; any `documents/` change
  that is part of the deliverable must be **appended to the patch explicitly**
  (`git diff 565e48f8 -- <document-path> >> documents/rounds/diffs-assignment-targeting-r2.patch`).

### Scope gates

**Expected files (worker's target list — additions must be justified in the report):**

- `src/Assignments/SchoolCollab.Assignments.Core/Domain/` — `TargetKind.cs` (new),
  `AssignmentTarget.cs` (new), `TargetAudienceType.cs` (+`Mixed`), `Assignment.cs`
  (`Targets`, `SetTargets`, `SyncDerivedTargeting`, `Create`/`Update` signatures)
- `src/Assignments/SchoolCollab.Assignments.Core/Data/Configurations/AssignmentTargetConfiguration.cs` (new)
- `src/Assignments/SchoolCollab.Assignments.Core/Migrations/` — additive migration
  (`AddAssignmentTargets` + Designer + snapshot)
- `src/Assignments/SchoolCollab.Assignments.Core/Services/IAssignmentTargetResolver.cs` (new)
- `src/Assignments/SchoolCollab.Assignments.Api/Services/AssignmentTargetResolverHttpClient.cs` (new)
- `src/Assignments/SchoolCollab.Assignments.Api/Program.cs` (DI)
- `src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs` (+`recipient-preview`)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/` — Create/Update
  threading (in `CreateAssignmentCommand/`, `UpdateAssignmentCommand/`) and
  `PublishAssignmentCommand/PublishAssignmentCommandHandler` target-driven rework (single-policy
  notification seam unchanged, D-2)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Queries/GetAssignmentAuthoringChildren/*`
  (+targets), `DuplicateAssignmentCommand/*` (+targets), `LinkAssignmentGroups/*` (thin
  adapter over `SetTargets`, D-8.2), `ListAssignmentsForGroup/*` (+`AssignmentTarget` rows in
  the FR-6 guard read)
- `src/Assignments/SchoolCollab.Assignments.Api/Endpoints/ActivityGroupLinkRoutes.cs`
  (`PUT /assignments/{id}/groups` disposition, D-8.2)
- `src/Assignments/SchoolCollab.Assignments.Contracts/ContractTypes.cs` (target DTOs, request
  params, preview DTO)
- `src/Students/SchoolCollab.Students.Core/CQRS/Students/Queries/ResolveStudentsByTarget/` (new query + handler)
- `src/Students/SchoolCollab.Students.Api/Endpoints/StudentRoutes.cs` (+`by-target`)
- `src/Assignments/SchoolCollab.Assignments.Application/.../AssignmentAuthoring.razor` (+`.razor.css`),
  `AssignmentEditFormModel.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/.../AssignmentsApiClient` or equivalent client (+preview, targets)
- Tests: `tests/SchoolCollab.Assignments.Tests.Unit` (domain/publish/form-model/bUnit),
  `tests/SchoolCollab.Assignments.Api.Tests.Unit` (route + client), Students Api test project
  (query/route), `tests/SchoolCollab.Assignments.Tests.Integration` (backfill/backfill-retry)
- `documents/configuration.md` — only if a new flag/parameter were introduced (**none planned**; no flag).

**Out of scope (do NOT implement):**

- R3 attachment text extraction / attachment-grounded AI (AI-3): generators stay URL-grounded.
- Per-assignment policy overrides (D4) — policy fields stay read-only-resolved.
- Dropping `TargetAudienceType`/`GradeLevelId` (TGT-15: follow-up round; the D-1/Mixed enum is
  the *preparation* for that drop, not the drop).
- No new feature flag; no AppHost/parameter changes; no CPM version changes.
- Ward-portal Prefab surface: R2 changes the Students/Assignments APIs; the portal's
  targeting view is updated after the portal consumes the derived-compat columns (TGT-15).
- Rubric scoring, comment threads, autosave (unchanged §15 items).

### WORKER task spec (dispatch verbatim)

```
ROLE: worker (round assignment-targeting-r2). Agent "worker"; model ollama-cloud/deepseek-v4.1-flash.
READ FIRST: documents/rounds/round-assignment-targeting-r2.md (## Plan is the dispatch contract);
on ambiguity the cited spec sections of documents/specs/assignment-authoring-compartments.md
(§7 TGT-1..16, §5 UX-21, §14/§16) and documents/specs/assignment-policy.md §2 govern.
Implement EXACTLY the plan's passes 1–6 in order, keeping the build green after each pass.
Honour the six settled decisions D-1..D-8 verbatim; do not re-litigate them.
Guards: no commits/pushes; never edit the round doc; repo-scoped searches only (src/, tests/,
documents/ — never `find /`); CPM: no Version on PackageReference; net10.0 only.
UI conventions: .github/skills/dropdown-ui/SKILL.md (multi-select = raw FluentSelect Multiple,
SelectedOptions/SelectedOptionsChanged — NEVER @bind-SelectedValues), input-width ladder,
isolated .razor.css, FormRow/section-card layout, dotnet-best-practices rule file first.
After the final green state write the patch ONCE:
  git add -N src tests && git diff 565e48f8 -- src tests > documents/rounds/diffs-assignment-targeting-r2.patch
(`git add -N` makes untracked new files visible to the diff; the `-- src tests` pathspec keeps
the artifact from embedding itself). Caveat: this captures only `src` and `tests` — any
`documents/` change that is part of the deliverable must be appended to the patch explicitly
(`git diff 565e48f8 -- <document-path> >> documents/rounds/diffs-assignment-targeting-r2.patch`).
Return ONLY the role-contract WORKER REPORT block (changed files · build · tests per suite ·
deviations). A deviation from Settled decisions D-1..D-8 or the out-of-scope list is a
blocker — stop and report instead of improvising; escalate via the supervisor channel if a
decision is genuinely missing.
```

### REVIEWER task spec (dispatch verbatim)

```
ROLE: reviewer (round assignment-targeting-r2) — STATIC diff review; never builds, never tests,
never writes files. Read documents/rounds/diffs-assignment-targeting-r2.patch plus the changed
files' surrounding code, against ## Plan of documents/rounds/round-assignment-targeting-r2.md.
Verify: (1) the six passes + settled decisions D-1..D-8 are implemented as written — esp. the
backfill's NOT EXISTS idempotency, the partial unique index on AllStudents, fail-closed
publish (no silent empty-set or swallowed-resolver-exception path), the by-target route's
authorization inheritance from studentsGroup, tenant-filtered queries, and the preview's
degraded-not-failing posture; (2) scope gates — no R3 items, no flag, no CPM edits, no
dropped legacy columns; (3) best-coding-practices: no overwrites of unrelated code, no
@bind-SelectedValues, isolated css, naming per repo conventions, migration additive +
snapshot in sync. Acceptance-honesty spot-check: name each planned "discriminating" test and
confirm it would fail against pre-R2 (base 565e48f8) code.
Return ONLY the role-contract REVIEW block (Verdict PASS | P1 | P2-only · P1/P2 file:line ·
Best-practices line).
```

## Worker Report

### Pass R2-1 (implementation) — `723579e4` (`deepseek-v4.1-flash`), complete

Implements Passes 1–6 of the plan: 41 modified + 13 new under `src/`/`tests/`, plus the spec
correction in `documents/specs/assignment-authoring-compartments.md`.

New: `Domain/TargetKind.cs`, `Domain/AssignmentTarget.cs`,
`Data/Configurations/AssignmentTargetConfiguration.cs`,
`Migrations/20261002065949_AddAssignmentTargets(.Designer).cs`, `Services/IAssignmentTargetResolver.cs`,
`Api/Services/AssignmentTargetResolverHttpClient.cs`,
`Students.Core/…/ResolveStudentsByTarget/{ResolveStudentsByTarget,ResolveStudentsByTargetHandler}.cs`,
`FakeAssignmentTargetResolver.cs`, `AssignmentTargetTests.cs`, `ResolveStudentsByTargetHandlerTests.cs`,
`AssignmentTargetBackfillMigrationTests.cs`.

Parent-verified (parent-authoritative re-run, not the worker's self-report):

| Suite | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | 0 errors |
| Assignments.Tests.Unit | 785 / 0 |
| Assignments.Api.Tests.Unit | 94 / 0 |
| Students.Tests.Unit | 623 / 0 |
| Students.Api.Tests.Unit | 18 / 0 |
| ArchitectureTests.Unit | 73 / 0 |

Patch as originally frozen: 5,865 lines · 54 diffs · 0 `deleted file mode` · 13 `new file mode`.

**Declared deviations:** P2-n2 applied (`primaryGradeId=` threaded into `ResolveSubscribersRequest` and
both policy resolvers); `SetTargets` takes caller-resolved `inactiveActivityGroupIds` so the aggregate
stays synchronous/HTTP-free; Create/Update call `SetTargets` after the tenant stamps are applied;
`ArgumentException → 400` added to the create/update routes; the D-4 "missing current period ⇒
`PreviewDegraded`" clause documented as **not implementable** (the Students leg answers "no matches",
not an error).

**Declared residual — materialised as the P1 the Review below records:** the Pass 5 route tests and the
adapter/guard tests were not written, and the bUnit debounce test used the real clock.

**First R2-7 attempt `90b70568` hung** — `bash` open 44 minutes with no activity, and it produced
**zero** files. Interrupted (run paused, superseded, never revived); its 15 orphaned `dotnet` processes
were killed before re-dispatch. Escalated as `1bc4999a` on the round's reviewer model, with
reconcile-first plus bounded-timeout / skip-if-slow guards in the dispatch.

### Pass R2-7 (escalation rework) — `1bc4999a` (`kimi-k2.7-code`), complete

Reconciled the working tree (all R2 implementation
present, no unintended deltas), applied the required transaction fix, added the P1 and P2 tests
flagged by the reviewer, and re-froze the patch.

#### Transaction fix
`LinkAssignmentGroupsHandler` now injects `AssignmentsDbContext` and wraps the target-row update
and the legacy link-table replacement in a single EF Core transaction via
`await db.Database.BeginTransactionAsync()` / `CommitAsync()`.

#### Tests added / updated

**P1 (BLOCK) — now present:**
- `tests/SchoolCollab.Students.Api.Tests.Unit/StudentTargetRoutesTests.cs` — `GET /students/by-target`
  route tests: auth challenge, `allStudents`, repeated query-array binding, union/dedupe across the
  four id arrays, archived-group exclusion, grade-agnostic stream leg, soft-deleted student exclusion.
- `tests/SchoolCollab.Assignments.Api.Tests.Unit/AssignmentRecipientPreviewRouteTests.cs` —
  `GET /assignments/recipient-preview` route tests: full query binding incl. `primaryGradeId`, happy
  path counts, and degraded 200 shape.
- `tests/SchoolCollab.Assignments.Tests.Unit/LinkAssignmentGroupsHandlerTests.cs` — D-8.2 adapter
  behaviour: target-row preservation + replacement, archived-group rejection, persisted archived
  re-save, DisplayOrder re-indexing, single-transaction wiring.
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentActivityGroupTests.cs` — FR-6 hard-delete
  guard test for activity groups referenced only via `AssignmentTarget`.

**P2 — now present (with notes):**
- `AssignmentAuthoringBunitTests.cs`: recipient-preview counts and degraded-reason rendering use
  wall-clock `Task.Delay(700)` after toggling Everyone so the 500 ms trailing debounce can settle;
  Edit-mode disabled-with-reason for unknown persisted targets (UX-21) is covered. The original
  chip-DisplayOrder test was removed because bUnit cannot deterministically render the chips through
  the async picker callbacks in this component configuration; the chip markup (`authoring-target-chip`,
  `data-order`) remains exercised indirectly by the save/validation path.
- Existing handler tests updated to supply an in-memory `AssignmentsDbContext` with transaction-warning
  suppression.

#### Verification

| Suite | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | 0 errors (17 warnings, pre-existing NuGet advisories) |
| Assignments.Tests.Unit | 792 / 0 |
| Assignments.Api.Tests.Unit | 97 / 0 |
| Students.Tests.Unit | 623 / 0 |
| Students.Api.Tests.Unit | 25 / 0 |
| ArchitectureTests.Unit | 73 / 0 |

Patch re-frozen: `documents/rounds/diffs-assignment-targeting-r2.patch` · 7,369 lines · index clean ·
HEAD unchanged at `565e48f8`.

### Pass R2-8 (bounded test-only rework) — `57fc2aed` (`deepseek-v4.1-flash`), complete

**Test-only**; no `src/` or CPM change. Diff arithmetic confirms it independently: real diffs 58 → 59,
exactly the one added `FakeTimeProvider.cs`.

- **P2-a** — hand-rolled `FakeTimeProvider` (deliberately **not**
  `Microsoft.Extensions.TimeProvider.Testing`, which would force a `Directory.Packages.props` edit and
  is out of scope). Pending timers are observable; a disposed timer never calls back, which is what
  makes the debounce's stale cancellation observable with **no wall-clock wait**. Both real-clock
  waits replaced via the previously-dead `timeProvider:` helper parameter.
- **New coalescing test** `RecipientPreview_RapidConstraintChanges_CoalesceIntoASingleRead` — the
  property nothing previously pinned: three rapid changes leave `previewRequests == 1` before the
  advance, `PendingTimers == 1` (the burst left exactly one armed), `== 2` after one advance, and
  `PendingTimers == 0`. In the counts test the *load* read answers zeros and the *change* read answers
  7, so `Students matched: 7` can only pass on the change's own read.
- **P2-b** — `Edit_TargetChips_RenderInDisplayOrder`: Edit-mode priming with
  `targets: [ActivityGroup@0, GradeLevel@1]`, asserting 2 chips with `data-order` `0`/`1` and labels
  `Alpha`/`Grade 5`; no picker interaction.
- **Nit** — `EveryoneTarget_DisablesTheConstraintPickersWithReason` now awaits `SelectEveryoneAsync`;
  awaiting it exposed a **latent race** (the Create-mode load re-mirrors the still-empty target set and
  wiped an early toggle: **2/8 flaky** full-suite runs), fixed by waiting for the load's own preview
  read before toggling — **0/16** after.

Parent-verified: build 0 errors · Assignments.Tests.Unit **794/0** · Assignments.Api 97/0 ·
Students.Api 25/0 · Students.Tests.Unit 623/0 · Architecture 73/0. Parent scope-check of the two
touched test files passed (assertions are real and discriminating; no production change).

### Pass R2-9 (UI rework) — `9397e5f0` (`deepseek-v4.1-flash`), complete

Fixes the real findings from the UI-tester pass (the parent adjudicated which were real — see
`## UI Tester`). 3 files: `AssignmentAuthoring.razor`, `AssignmentEditFormModel.cs`,
`AssignmentAuthoringBunitTests.cs`.

- **F1 (P1, data-loss).** `GroupPickerDisabled = ConstraintPickersDisabled || GroupPickerReason is not null`,
  with the group picker gated on it. **The worker deviated from the literal instruction and was right to:**
  bare `ConstraintPickersDisabled` would *enable* the picker in the flag-off and links-unavailable cases
  (neither is expressible through it), which it proved by showing the existing P2-1 test fail under the
  literal gate.
- **F4 (P2).** `SetEveryoneTarget` snapshots the prior non-`AllStudents` targets on ON and restores them on
  OFF, so Everyone ON→OFF no longer silently discards the constraint set.
- **F6/F8** (`aria-describedby`/`aria-label`) · **F9/F14** (`AlignTop`) · **F10/F11** (the degraded preview
  no longer renders trustworthy counts beside the unavailable note; View mode no longer shows
  "Preview pending…" forever) · **F16** (`RefId` fallback rewrite).
- Two new discriminating tests, both **verified to fail pre-fix** (F1: `Disabled … but found False`;
  F4: empty chip collection); one existing test updated — it had asserted `Students matched: 0` in the
  degraded case, which contradicted F10.

Parent-verified: build 0 errors · Assignments.Tests.Unit **798/0** · Api 97/0 · Students 623/0 ·
Students.Api 25/0 · Architecture 73/0.

### Pass R2-10 (final micro-rework) — `793e5939` (`deepseek-v4.1-flash`), complete

- **P1 (NFR-1, found by the R2-9 re-verify).** In the UX-21 path the group picker was disabled while its
  hard-coded `aria-describedby="authoring-groups-reason"` pointed at a paragraph that does not render
  there — a dangling IDREF, so the disabled control exposed **no** reason. Now derived:
  `GroupPickerAriaDescribedBy` (`AssignmentAuthoring.razor:851-854`) picks the paragraph that actually
  renders (`authoring-groups-reason` when a group reason exists, else
  `authoring-audience-constraint-reason`, else `null`), bound at `:267`.
- **P2.** `LoadTargets` now clears `_everyonePriorTargets` (`AssignmentEditFormModel.cs:142`), so a reused
  component instance cannot restore assignment A's snapshot onto assignment B.
- Both fixes carry tests **verified to fail pre-fix** (`actual "authoring-groups-reason" / expected
  "authoring-audience-constraint-reason"`; and the stale grade constraint resurrecting out of a discarded
  snapshot).

Parent-verified (final): build 0 errors · Assignments.Tests.Unit **800/0** · Api 97/0 · Students 623/0 ·
Students.Api 25/0 · Architecture 73/0. Parent scope-check of this tiny diff passed.

## Review

**Diff review `074a0bad` (`kimi-k2.7-code`) — Verdict: P1 (BLOCK).** The implementation follows the
plan; the failure is **undelivered acceptance tests**, not design.

Confirmed correct (file:line): fail-closed publish — empty/no targets and resolver exceptions both
refuse, and the `AllStudents` leg returns tenant-wide non-deleted students
(`ResolveStudentsByTargetHandler.cs:33`); backfill exclusive precedence + NULL-safe `ref_id IS NULL`
+ partial unique index, `Down()` additive only (`20261002065949_AddAssignmentTargets.cs:61-117`);
`SetTargets` enforces FR-22 for newly-added ids only while an unchanged persisted archived id still
re-saves (`Assignment.cs:285`); the legacy route is a thin `SetTargets` adapter
(`LinkAssignmentGroupsHandler.cs:52-58`) and the FR-6 guard read unions `AssignmentTarget` rows
(`AssignmentActivityGroupRepository.cs:49-63`); `SyncDerivedTargeting` at every write point with
`GradeLevelId` never derived (`Assignment.cs:365`); preview threads `primaryGradeId` through the
request and both policy resolvers, returns degraded 200, uses stale-response cancellation
(`AssignmentRoutes.cs:226`); UI uses `SelectedOptions`/`SelectedOptionsChanged`,
disabled-with-reason, isolated `.razor.css`.

Scope gates clean (no R3 attachment work, no per-assignment policy overrides, no new feature flag, no
CPM edit, no legacy-column drop). The `ArgumentException → 400` addition
(`AssignmentRoutes.cs:378,449`) is correct and consistent with existing route handling. Acceptance
honesty: every existing planned discriminating test (`AssignmentTargetTests`,
`ResolveStudentsByTargetHandlerTests`, `AssignmentTargetBackfillMigrationTests`,
`AssignmentActivityGroupTests.Publish_GroupTargetWithNoResolvedStudents_IsRefused` /
`Publish_TargetResolverOutage_IsRefused` / `SelectedGrades_Path_ResolvesThroughTheTargetResolver`)
references new symbols — none passes vacuously against `565e48f8`.

**P1 (blocking) — missing acceptance tests**
- route test in `tests/SchoolCollab.Students.Api.Tests.Unit/` for `GET /students/by-target`
  (query-array binding, `allStudents` boolean, union/degraded semantics).
- route test in `tests/SchoolCollab.Assignments.Api.Tests.Unit/` for `GET /assignments/recipient-preview`
  (full binding incl. `primaryGradeId`; degraded 200 shape).
- D-8.2 unit tests: `LinkAssignmentGroupsHandler` as adapter over `SetTargets`, and the FR-6 guard read
  counting `AssignmentTarget` rows alongside link-table rows.

**P2**
- `Assignments.Tests.Unit/AssignmentAuthoringBunitTests.cs:575` — debounce test waits on the real
  500 ms clock; inject the `TimeProvider` parameter and advance it.
- Missing planned bUnit coverage: `PreviewUnavailableReason` rendering; Edit-mode
  disabled-with-reason until persisted targets load (UX-21); target chips in `DisplayOrder`.

**Best-practices (required)** — `LinkAssignmentGroupsHandler` performs two sequential `SaveChanges`
(target update, then link replace) with no explicit transaction; wrap them in a single EF Core
transaction so a failure cannot leave targets and links inconsistent.

### Re-verify (higher-model gate) — `d95a41da` (`glm-5.3`) — Verdict: **P2-only**, no P1 remains

Escalated work was independently re-verified. All three previously-missing acceptance tests are real
and **non-vacuous** — each fails against `565e48f8` (the routes 404; the adapter handler ctor had no
`AssignmentsDbContext`; the guard read had no target-row union): `StudentTargetRoutesTests.cs`
(auth :132, `allStudents` :145, repeated array binding :160, 4-array union+dedupe :176,
archived-group :195, grade-agnostic stream leg :212, soft-deleted :227);
`AssignmentRecipientPreviewRouteTests.cs` (`primaryGradeId` :145 asserting the captured request,
happy path :169, degraded 200 :184 asserting the **specific** all-zero + `PreviewDegraded:true`
payload rather than a bare 200); `LinkAssignmentGroupsHandlerTests.cs` (adapter behaviour :96-120,
newly-added archived rejection asserting **no partial write**); and
`AssignmentActivityGroupTests.DeleteGroup_ReferencedOnlyByTargetRow_Blocked` (patch :5311-5336,
link table explicitly emptied).

**Transaction fix verified correct by inspection** — `LinkAssignmentGroupsHandler.cs:66-84`: one
`await using var transaction = await db.Database.BeginTransactionAsync(ct)` wraps both writes over a
shared scoped `AssignmentsDbContext` (same DI instance per request); `DetectChanges()` before the
save, `CommitAsync` awaited, cache invalidated **after** commit. No nested transaction, no
silently-ignored `BeginTransaction`, no `SaveChanges` outside the scope; `SetTargets` is pure
in-memory mutation before the scope opens.

**P2-a** — accepted as non-compliance at review time (real-clock `Task.Delay(700)`); **fixed in R2-8**.
**P2-b** — the worker's stated infeasibility ("bUnit cannot deterministically render the chips through
the async picker callbacks") was **false**: chips are a synchronous projection of the form model
(`AssignmentAuthoring.razor:854`). Test added in R2-8. **P2-c** (atomicity path untested) **deferred
with a recorded rationale** — the InMemory fixture suppresses `TransactionIgnoredWarning`, so proving
rollback needs Testcontainers failure-injection in the Integration suite; the transaction code itself
is verified correct by inspection. **P2-d** — the frozen patch embedded
`.pi/skills/orchestrator-worker-reviewer/SKILL.md` and this round doc, neither of which the declared
pathspec produces; **fixed** by re-freezing with the pathspec (contamination gone, 59 diffs).

All other previously-verified behaviours were re-confirmed in the re-frozen patch: fail-closed
publish, the explicit `AllStudents` leg, backfill exclusive precedence + NULL-safe guards,
`SetTargets` FR-22 newly-added-only, `SyncDerivedTargeting` at all three write points with
`GradeLevelId` never derived, preview threading `primaryGradeId` into the request and both resolvers,
`OperationCanceledException` rethrown, and scope gates clean (no R3/AI work, no flag, no CPM edit —
the new version-less `Microsoft.EntityFrameworkCore.InMemory` reference resolves against the existing
`Directory.Packages.props:18` — no legacy-column drop).

### Final re-verify (R2-9 / R2-10) — `aedc1895` (`kimi-k2.7-code`) — Verdict: P1, then closed

Confirmed correct: the F1 composed gate disables the picker in **all four** cases with the right reason
paragraph each time (flag-off / links-unavailable → `authoring-groups-reason`; Everyone-on / UX-21 →
`authoring-audience-constraint-reason`); the F1 test genuinely stubs `/authoring` to 500 while `/groups`
returns a real link; F4's snapshot/restore and its test; F10/F11; and the updated degraded test (still
discriminating, not weakened to fit the fix). No regressions.

It also **corrected the parent's F16 framing**: the original `?.[..8]` chain was **not** a reachable crash
risk — `AllStudents` short-circuits to "Everyone" and `AssignmentTarget.Create`
(`AssignmentTarget.cs:54-60`) already forbids a null `RefId` for every other kind.

**P1 raised and closed in R2-10:** the dangling `aria-describedby` on the group picker in the UX-21 path
(NFR-1). That defect was introduced by R2-9's F6 and disclosed there as a residual which the parent
triaged as a harmless ARIA nit; the review escalated it correctly — a disclosed residual is not
automatically a non-blocking one.

## Acceptance

**Round CLOSED — accepted.** Final parent-authoritative verification on the frozen tree:

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| Assignments.Tests.Unit | **800 / 0** |
| Assignments.Api.Tests.Unit | 97 / 0 |
| Students.Tests.Unit | 623 / 0 |
| Students.Api.Tests.Unit | 25 / 0 |
| ArchitectureTests.Unit | 73 / 0 |
| Patch `diffs-assignment-targeting-r2.patch` | 7,349 lines · 59 diffs · **0** `deleted file mode` · 17 `new file mode` · no contamination |
| Index / HEAD | clean / unchanged `565e48f8` |

**Gate history:** plan-review → 6 P1 + 7 P2, revised → ACCEPT · diff review → **P1 BLOCK** (missing
acceptance tests) · escalated rework (after a hung first attempt) → P1 closed · re-verify → P2-only ·
R2-8 test-only rework → P2-a/b closed, latent race found (2/8 → 0/16) · UI tester → **1 real P1** (the
UX-21 group-picker data-loss path) + 2 claimed P1s rejected against source · R2-9 → that P1 + P2 fixed ·
final re-verify → 1 P1 (NFR-1 dangling IDREF, introduced by R2-9) · R2-10 → closed.

**Recorded, non-blocking residuals (carried forward, not silently dropped):**
- **F3 — UX-7 unsaved-changes confirmation** (`assignment-authoring-compartments.md:107`): real and
  spec'd, but **pre-existing** in the R1 component. → R1b residue, with the archived-group relink 422 and
  the `ReloadAsync` children re-read.
- **P2-c — transaction rollback proof**: the code is verified correct by inspection; proving rollback
  needs Testcontainers failure-injection (the InMemory fixture suppresses `TransactionIgnoredWarning`).
  → Integration-suite follow-up.
- **UI P2 backlog**: F7 (kebab disabled-without-reason), F12 (empty-state preview wording), F13
  (first-render debounce delay), F15 (per-row reason placement), F17 (no `OnParametersSet`).
- **Verified benign, not waved through**: the other three pickers' hard-coded `aria-describedby` dangles
  only in plain read-only, where there is no reason to convey — that paragraph renders exactly when a
  reason exists.
- **F4 student-label restore**: a restored student target shows the fallback `Student <id8>` chip until
  the picker is touched — identical to how a loaded student target already renders.

**Merge: NOT authorized.** The stack is green and unmerged; merging requires an explicit owner
instruction.

## UI Tester

**UI tester `2c679b1e` (`minimax-m3`) — Verdict: bugs found (5 claimed P1, 12 P2).** The report was
**adjudicated against source by the parent before any rework was dispatched**; two of the five claimed
P1s do not hold.

### Confirmed
- **F1 — P1, real data-loss path in R2's own new code (fixed in R2-9).** The Activity-groups picker
  (`AssignmentAuthoring.razor:248-257`) gates on `Disabled="@(IsReadOnly || GroupPickerReason is not null)"`
  while grades/streams/students gate on `Disabled="@ConstraintPickersDisabled"`. The gates diverge:
  `ConstraintPickersDisabled` (`:700-705`) includes `AudienceEditorDisabledReason` (the UX-21
  "persisted targets did not load" case); `GroupPickerReason` (`:803-810`) does not. The targets read
  (`GET /assignments/{id}/authoring` → `_model.LoadTargets`) and the group-links read
  (`LoadLinkedGroupIdsAsync` → `_groupLinksUnavailableReason`) are **separate calls**, so the first
  failing while the second succeeds leaves the group picker live over an empty in-memory target set —
  a group selection then sets `TargetsChanged` and Save full-replaces the persisted rows.
- **F4 — real, downgraded P1→P2 (fixed in R2-9).** `SetEveryoneTarget`
  (`AssignmentEditFormModel.cs:170-177`) clears `_targets` on both transitions and
  `OnEveryoneChangedAsync` clears the page selection state when Everyone goes ON, so ON→OFF leaves an
  empty set with the prior constraints silently discarded. **Not P1**: the empty set is caught by
  `ValidateForSave` (`:1820`, TGT-13) and by `SetTargets`, so nothing corrupts *silently* — but the
  prior set is lost without a word.
- **F6/F8/F9/F10/F11/F14/F16** — P2/P3 UI-hygiene items in the new surface; taken in R2-9 (a11y
  `aria-describedby`/`aria-label`, `AlignTop`, degraded-preview display, View-mode "Preview pending…"
  forever, and a defensive `RefId` rewrite).

### Rejected — no change (verified against source)
- **F2 (claimed P1).** The repro cannot occur. `PublishAsync` publishes via
  `Api.PublishAsync(_item.Id, …)` — the **persisted** assignment, not the in-memory form — so removing
  chips without saving does not change what publish acts on, and the claimed
  400-after-the-dialog path is unreachable. `ValidateForSave` does contain the TGT-13 check (`:1820`).
- **F5 (claimed P1).** `DerivedTargetAudience` falls back to `_item.TargetAudienceType` when targets did
  not load; since publish acts on the persisted assignment, the dialog's "Audience: …" accurately
  describes what will be published. It is not a lie.

### Deferred — recorded, not R2's scope
- **F3 — UX-7 unsaved-changes confirmation** (`assignment-authoring-compartments.md:107`). Real and
  spec'd, but **pre-existing** in the R1 component and not introduced or worsened by R2; carried on the
  R1b residual list alongside the archived-group relink 422 and the `ReloadAsync` children re-read.
- **F7, F12, F13, F15, F17** — P2 backlog (kebab disabled-without-reason, empty-state preview wording,
  first-render debounce delay, per-row reason placement, missing `OnParametersSet`).