# Assignment Rules — policy-derived authoring section

> **Status:** Accepted (2026-10-07). The authoring page's Delivery & Publishing and
> Submission & Sign-off sections are retired and merged into one policy-driven
> **Rules** section; `MandatoryReview` and `ArchiveGraceDays` become assignment-policy
> fields resolved tenant-default + per-grade and snapshotted onto the assignment at
> create. This is a settled design — the decisions in §3 are final.

---

## 1. Findings — verified current state

**Authoring page.** `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentAuthoring.razor`
renders **six compartments** (the `Compartments` array near line 811) as stacked,
always-visible section cards; the sticky compartment jump-nav renders from that same
array, so a section and its nav entry are one artifact.

- **Delivery & Publishing** (near line 246): `Status` (readonly), `Available from`
  (readonly), `Due date` (author), `Archive grace days` (author), `Approval before
  publish` (policy readout), `Notification policy` (policy readout).
- **Submission & Sign-off** (near line 296): `Max score` (author), `Feedback mode`
  (derived), pass-score and attempt-cap fields (author; disabled for TeacherGraded),
  the **Guardian review** checkbox (`MandatoryReview`), the **Guardian signature**
  checkbox (`RequiresSignature`), and the signature-requirement policy row.

**Assignment entity.** `src/Assignments/SchoolCollab.Assignments.Core/Domain/Assignment.cs`:

- `ArchiveGraceDays` — `int` NOT NULL, default 30. Sole consumer is the archive
  sweep: `ArchiveSweepService` (24h loop) → `ListDueForArchiveAsync`, condition
  `Published`/`Closed` && `DueDate + ArchiveGraceDays <= now`.
- `RequiresSignature` — `bool`, snapshotted from the resolved policy at create.
- `MandatoryReview` — `bool`, default true; gates student self-submit in
  `CreateStudentSubmissionCommandHandler`.
- The existing `passScore <= maxScore` guard (throws `ArgumentException` in both
  `Create` and `Update`) is the domain-backstop precedent this spec mirrors.

**Policy domain.** `src/SchoolCollab.Core/AssignmentPolicies/AssignmentPolicyFields.cs`
is the shared field set and carries exactly **4 fields** today:
`SignatureRequirement`, `RequiresApprovalBeforePublish`, `MaxPrimaryContacts`,
`MaxCopyContacts`. `TenantAssignmentPolicy`
(`src/Settings/SchoolCollab.Settings.Core/Domain/TenantAssignmentPolicy.cs`) is the
tenant default; `GradeAssignmentPolicy` (Students) is the per-grade override;
`EffectiveAssignmentPolicyResolver` merges per field. `IAssignmentPolicyResolver` +
`AssignmentPolicyResolver` (Assignments.Api) fetch tenant
(`settings-api /api/settings/assignment-policy`) and grade
(`students-api /students/grade-levels/{id}/assignment-policy`) values; wire DTOs are
`TenantAssignmentPolicyDto` / `GradeAssignmentPolicyDto`. Admin editors exist
(`AssignmentPolicyApiClient`, `GradeSignaturePolicyEditor`).

**Policy-domain gap.** There is **no `MandatoryReview` and no `ArchiveGraceDays`** in
the policy domain today. Both must be added to `AssignmentPolicyFields` and carried
through the tenant default, the per-grade override, the resolver, both entities, both
DTOs, and both admin editors.

**Contradiction with the original design intent.** `ArchiveGraceDays` was originally
specced as a **pass-through with NO visible field**
(`documents/rounds/round-ar-5-lifecycle.md`: the round adds the domain column, the
sweep, and an `AssignmentEditFormModel.ArchiveGraceDays` pass-through — "NO visible
field" — with `Create.razor` untouched and the value defaulting to 30). The
author-facing input now on the page contradicts that intent: the value is a
tenant/grade policy decision, not an author decision, so it belongs in the policy set
and must be removed from the authoring form.

## 2. Decisions

The numbering below is the settled record. Each decision states its rationale and its
cost.

**D1 — Merge the two sections into one "Rules" compartment (6 → 5).** Delivery &
Publishing and Submission & Sign-off are removed and replaced by a single **Rules**
section, in the retired sections' place in the sequence (between Targets and Content):
Basics, Targets, Rules, Content, Questions. Because the stacked section and the
jump-nav both render from the one `Compartments` array, the page and the nav change
together — no separate nav edit. *Rationale:* the two sections held policy readouts,
author fields, and retired controls mixed together; one policy section makes the
policy/author split visible. *Cost:* `AssignmentAuthoring.razor` restructure plus
nav-order/`data-` anchor updates and the bUnit compartment assertions.

**D2 — "Rules" shows policy-derived readouts only.** The section renders approval
before publish, notification policy, the archive window (from `ArchiveGraceDays`),
signature requirement, and guardian review. **No author-editable field remains in
it.** *Rationale:* every value in the section is a policy outcome, so the section
becomes a pure readout and cannot drift from the resolved policy. *Cost:* the author
inputs that lived here are relocated (D9) or retired (D10).

**D3 — `MandatoryReview` becomes a `bool?` policy field.** Added to
`AssignmentPolicyFields` as `bool?`. **null = unset** (the author may choose);
**non-null = the policy sets it and it is NOT author-editable** (locked on the page).
*Rationale:* the tenant/grade decides whether guardian review is mandatory; a locked
non-null value must not be overridable per assignment. *Cost:* new nullable column and
DTO field in Settings and Students, resolver mapping, and the page lock.

**D4 — Implication rule: `RequiresSignature` implies `MandatoryReview`.** When
`RequiresSignature` is true, `MandatoryReview` is **implied true** and must not be
author-tampered. Enforced in **both** places: (a) the effective-policy resolver derives
`mandatoryReview = requiresSignature || policyReview`, and (b) the `Assignment` entity
(`Create`/`Update`) asserts it as a **domain backstop**, mirroring the existing
`passScore <= maxScore` guard. *Rationale:* a signed assignment cannot be one the
guardian review gate lets through unsigned — the invariant must hold even if a caller
bypasses the page or the resolver. *Cost:* one derived expression in the resolver, one
guard + exception message in the domain, and tests for both seams.

**D5 — `ArchiveGraceDays` moves to policy, into the same field set.** It joins
`AssignmentPolicyFields` (not a separate setting) so it flows through the existing
tenant-default + per-grade-override merge and the existing policy editors. Both new
fields (D3, D5) are **nullable; null = unset**. *Rationale:* reusing the existing
shape and merge avoids a second resolution mechanism and keeps the editors single-
purpose. *Cost:* the archive-grace column is added to both policy entities and both
DTOs; the migration in each of Settings and Students must add it alongside the D3
column.

**D6 — Resolution is snapshot at create.** `Assignment` keeps all three columns
(`ArchiveGraceDays`, `RequiresSignature`, `MandatoryReview`); the create/update
handlers source those values from the **resolved effective policy** instead of the
request. The archive sweep and the submit gate read the stored rows, so there is **no
cross-module hop on a background or write path**. *Consequence, stated explicitly:* a
later policy change does **not** retro-apply to existing assignments — an assignment's
terms are fixed when it is authored. *Rationale:* the sweep is a 24h background loop
and the submit gate is on the write path; both must read local data, never call
Settings/Students. *Cost:* create/update handlers depend on
`IAssignmentPolicyResolver`; request/DTO `ArchiveGraceDays` stops being an author
input.

**D7 — Level: tenant default AND per-grade override.** Both new fields are resolved by
the existing `EffectiveAssignmentPolicyResolver` per-field merge (tenant default, then
grade override). *Rationale:* signature/review/archive policy is set per grade in this
system; the tenant row is the fallback. *Cost:* **migrations in Settings and
Students** (one per database).

**D8 — Both policy editors must expose the new fields.** The tenant policy editor and
the Students `GradeSignaturePolicyEditor` must surface `MandatoryReview` and
`ArchiveGraceDays`, or policy could never set them. **In scope; this is the largest
hidden cost.** *Rationale:* a field the resolver can read but no editor can write is
dead configuration. *Cost:* `AssignmentPolicyApiClient` + tenant editor form, and
`GradeSignaturePolicyEditor` + its DTO/binding, plus editor tests on both surfaces.

**D9 — Author-fed scoring fields move to Basics.** `Max score`, pass score, and
attempt cap move out of the retired Submission & Sign-off section into **Basics**,
directly under the existing **Grading format** field that already drives them.
*Rationale:* grading is an author decision, and the control that drives it
(`Grading format`) is already in Basics; co-locating them removes a split-brain form.
*Cost:* Basics layout change, the TeacherGraded disable logic moves with the fields,
and bUnit assertions update.

**D10 — Retire the author-facing Guardian signature checkbox.** `RequiresSignature` is
no longer author-editable; **policy decides**, and the readonly policy row in Rules
states the outcome. *Rationale:* signature requirement is already policy-resolved and
snapshotted (D6); an author checkbox duplicates and can contradict it. *Cost:* remove
the checkbox from the page and form model; the entity property and its snapshot stay.

## 3. Impact / blast radius

| Layer | Files | Change |
|---|---|---|
| Shared policy field set | `src/SchoolCollab.Core/AssignmentPolicies/AssignmentPolicyFields.cs` | add `bool? MandatoryReview`, `int? ArchiveGraceDays` |
| Shared effective policy | `src/SchoolCollab.Core/AssignmentPolicies/EffectiveAssignmentPolicy.cs` | add `MandatoryReview`, `ArchiveGraceDays` resolved values + one `…FromOverride` flag each |
| Effective resolver | `src/SchoolCollab.Core/AssignmentPolicies/EffectiveAssignmentPolicyResolver.cs` | merge the two fields per-field; derive `MandatoryReview = RequiresSignature == Mandatory \|\| policyReview` (D4) |
| Built-in defaults | same resolver + `EffectiveAssignmentPolicy` doc comment | `MandatoryReview` default and `ArchiveGraceDays` default (30) when nothing is configured |
| Resolver interface | `src/SchoolCollab.Core/AssignmentPolicies/IEffectiveAssignmentPolicyResolver.cs` | no signature change if the record grows; confirm callers compile |
| Settings entity | `src/Settings/SchoolCollab.Settings.Core/Domain/TenantAssignmentPolicy.cs` | two nullable columns |
| Settings DTO + wire | `TenantAssignmentPolicyDto` (+ endpoint body binding) | two optional fields; PUT accepts/serialises them |
| Settings upsert command | the assignment-policy upsert command/handler | map the two fields |
| Settings migration | `src/Settings/SchoolCollab.Settings.Core/Migrations/` | additive migration (D7) |
| Settings editor | `AssignmentPolicyApiClient` + tenant policy editor page | two new inputs (D8) |
| Students entity | `GradeAssignmentPolicy` | two nullable columns |
| Students DTO + wire | `GradeAssignmentPolicyDto` | two optional fields |
| Students migration | Students `Migrations/` | additive migration (D7) |
| Students editor | `GradeSignaturePolicyEditor` | two new inputs (D8) |
| Assignments resolver | `src/Assignments/SchoolCollab.Assignments.Api/Services/` `AssignmentPolicyResolver` | map the two new fields from both endpoints into the effective policy |
| Assignments request/DTO optionality | `CreateAssignmentRequest` / `UpdateAssignmentRequest` and assignment DTOs | drop `ArchiveGraceDays` as an author input (D6); keep `MandatoryReview`/`RequiresSignature` out of the request |
| Assignments domain | `src/Assignments/SchoolCollab.Assignments.Core/Domain/Assignment.cs` | D4 backstop assert in `Create`/`Update`; columns unchanged |
| Assignments handlers | `CreateAssignmentCommandHandler`, `UpdateAssignmentCommandHandler` | source the three values from the resolved policy, not the request |
| Assignments page | `AssignmentAuthoring.razor` (+ `.razor.css`), `AssignmentEditFormModel.cs` | six → five compartments; Rules section (D2); scoring fields into Basics (D9); signature checkbox removed (D10) |
| Archives sweep | `ArchiveSweepService` + `ListDueForArchiveAsync` | unchanged code; now reads policy-snapshotted rows (D6) |
| Submit gate | `CreateStudentSubmissionCommandHandler` | unchanged code; reads the snapshotted `MandatoryReview` (D6) |
| Tests | `SchoolCollab.Core` resolver/fields tests; Settings + Students migration/upsert tests; Assignments handler + domain-guard tests; bUnit page tests; editor tests | new/updated per layer above |

## 4. Non-goals / explicitly deferred

- **No retrofit of existing assignments.** A policy change after this ships does not
  back-fill `ArchiveGraceDays` / `MandatoryReview` on already-authored rows (D6).
- **No subject-option source for Everyone-only audiences.** Out of scope here.
- **No per-grade policy merge when 2 or more grade targets are selected.** The tenant
  default applies instead; multi-grade merging is deferred.
- No change to the archive sweep's cadence or its `Published`/`Closed` condition, and
  no change to the notification-policy contract itself.

## 5. Open risks

- **Page-side derivation mirror.** The Rules readout re-derives values the page did not
  compute locally before; if the page and the resolver disagree, the readout lies. The
  page must read the same resolved effective policy, not recompute the implication rule
  in Razor.
- **Fail-open resolution posture.** `AssignmentPolicyResolver` fetches tenant and grade
  policy over HTTP; when a fetch degrades the resolver falls back to defaults. A
  transient failure could therefore snapshot `MandatoryReview = false` for an
  assignment whose policy requires review — the degradation policy must be explicit for
  these two fields.
- **Two-migration cost.** Settings and Students each need a migration (D7); they must
  land together or the resolver reads a column that does not exist in one database.
- **Admin-editor scope.** D8 is the largest hidden cost: both editors must ship the two
  new inputs in the same change as the fields, or policy can never set them.
