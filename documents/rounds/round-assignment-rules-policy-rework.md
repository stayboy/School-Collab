Provider: pi (models: glm-5.3-flash orchestrator, glm-5.3 plan gate, deepseek-v4.1-flash worker, kimi-k2.7-code reviewer, minimax-m3 UI tester)
Tier: 3 full (UI round)
Base: HEAD 870b4b0f314f239de400663b8c65a4cc10f8faca; tree DIRTY at round start (106 tracked-modified paths recorded in documents/rounds/round-assignment-rules-policy-rework.base-names.txt); this round diff is isolated from the uncommitted blob by a full-tree snapshot taken at round start, NOT by git diff against HEAD.
Status: CLOSED — pre-commit close: the round is committed nowhere yet (no commit, no PR, no push). Destination branch: `feature/assignment-authoring-targets-and-rules`.

# Round — assignment-rules-policy-rework

Source of truth: `documents/specs/assignment-rules-policy-rework.md` (Accepted 2026-10-07).
This round implements D1–D10. The spec is settled; nothing here re-opens it.

## Plan

### Goal

Retire the authoring page's **Delivery & Publishing** and **Submission & Sign-off**
compartments (6 → 5) and replace them with one **Rules** section that renders
policy-derived readouts only; move `MandatoryReview` and `ArchiveGraceDays` into the
shared assignment-policy field set (tenant default + per-grade override, two
migrations); snapshot the three policy columns (`ArchiveGraceDays`, `RequiresSignature`,
`MandatoryReview`) onto the assignment from the **resolved effective policy** at
create/update instead of the request; enforce the D4 implication
(`RequiresSignature ⇒ MandatoryReview`) in the effective-policy resolver **and** as an
`Assignment` domain backstop mirroring the existing `passScore ≤ maxScore` guard; move
the author-fed scoring fields into Basics under Grading format (D9); retire the
author-facing Guardian signature checkbox (D10); and expose both new fields in both
policy editors (D8).

### Scope IN

- D1/D2/D3/D4/D5/D6/D7/D8/D9/D10 exactly as written in the spec §2.
- UI round: the tier-3 UI trigger fires (`AssignmentAuthoring.razor` is in the changed set).

### Scope OUT (do not widen)

- **No retrofit of existing assignment rows** — no back-fill migration on Assignments;
  the spec §4 non-goals are binding.
- `ArchiveSweepService` and `CreateStudentSubmissionCommandHandler` are **unchanged
  code**; they keep reading stored rows (D6).
- No change to the archive sweep cadence / `Published`-`Closed` condition, and no change
  to the notification-policy contract.
- No subject-option source for Everyone-only audiences; no multi-grade policy merge
  when 2+ grade targets are selected.
- **Legacy wire-compat members stay**: `RequiresSignatureDefault` (Students
  `GradeAssignmentPolicyDto.cs:43-58`, `GradeLevelRoutes.cs:330-344`),
  `LegacyUpsertAssignmentPolicyRequest` (`AssignmentPolicyApiClient.cs:49`) and the legacy
  boolean on `/assignments/signature-default` predate this spec's arc; deleting them is a
  separate cleanup and widens this round.
- **Never touch `src/SchoolCollab.MigrationService/**`** (hard constraint).
- No commit / stage / push. No edits to the round doc by anyone but the orchestrator.

### Expected files (grouped by layer)

**Core (shared policy field set + merge)**
- `src/SchoolCollab.Core/AssignmentPolicies/AssignmentPolicyFields.cs` — add `bool? MandatoryReview`, `int? ArchiveGraceDays` (D3/D5).
- `src/SchoolCollab.Core/AssignmentPolicies/EffectiveAssignmentPolicy.cs` — add the two resolved values (+ per-field `…FromOverride` flags) and the built-in-default doc comment.
- `src/SchoolCollab.Core/AssignmentPolicies/EffectiveAssignmentPolicyResolver.cs` — per-field merge for both; D4 derivation `MandatoryReview = requiresSignature-effective || policyReview` (lines 18-29 today resolve exactly 4 fields + 4 override flags — extend the same record/merge shape).
- `src/SchoolCollab.Core/AssignmentPolicies/IEffectiveAssignmentPolicyResolver.cs` — no signature change expected (record grows); confirm callers compile.

**Settings (tenant default)**
- `src/Settings/SchoolCollab.Settings.Core/Domain/TenantAssignmentPolicy.cs` — two nullable columns; `Create`/`SetPolicy` signatures grow (CQRS seam: `UpsertTenantAssignmentPolicy.cs:13-16` command record; `UpsertTenantAssignmentPolicyHandler.cs` mapping; query handler for the read shape).
- `src/Settings/SchoolCollab.Settings.Core/DTOs/TenantAssignmentPolicyDto.cs` (`:12-16` today — 4 fields) — two optional fields.
- `src/Settings/SchoolCollab.Settings.Api/Endpoints/AssignmentPolicyRoutes.cs` — PUT body binding carries the two fields.
- `src/Settings/SchoolCollab.Settings.Core/Migrations/` — one additive migration (+ Designer + snapshot) following the `20260930150642_AddAssignmentPolicyFields` precedent (D7).
- `src/Settings/SchoolCollab.Settings.Core/Data/Configurations/TenantAssignmentPolicyConfiguration.cs` — only if the new columns need explicit config (nullable bool/int are conventional; do not add noise).

**Students (per-grade override)**
- `src/Students/SchoolCollab.Students.Core/Domain/GradeAssignmentPolicy.cs` — two nullable columns (`SignatureRequirement`/`RequiresApprovalBeforePublish`/`MaxPrimaryContacts`/`MaxCopyContacts` live at lines 30/36/39/42).
- `src/Students/SchoolCollab.Students.Core/DTOs/GradeAssignmentPolicyDto.cs` — two optional fields.
- `src/Students/SchoolCollab.Students.Api/Endpoints/GradeLevelRoutes.cs` — the `/grade-levels/{id}/assignment-policy` GET (:269) + PUT (:280) and `UpsertGradeAssignmentPolicyRequest` (:324) carry the two fields.
- `src/Students/SchoolCollab.Students.Core/Migrations/` — one additive migration (+ Designer + snapshot) following `20260930150717_AddAssignmentPolicyFields` (D7 — the two migrations land together).
- `src/Students/SchoolCollab.Students.Application/Components/Students/AssignmentPolicyEditor.razor` — the policy-field grid's `FieldDef[]` (:62-70) gains the two rows.
- `src/Students/SchoolCollab.Students.Application/Components/Students/AssignmentPolicyFieldEditDialog.razor` — the scope switch/apply (:145-156) handles the two new field kinds (`Bool` and `Int` kinds already exist).
- `src/SchoolCollab.Admin.Shared/Services/AssignmentPolicyApiClient.cs` — tenant-default upsert DTO (:37) gains the two fields (D8's tenant-editor seam — the tenant default is edited from the grade-level page's Global scope, there is no separate Settings tenant editor page).
- `src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs` — grade policy DTO binding if it re-declares the record.

**Deleted legacy route's test surface (OD4 — API route tests, not bUnit)**
- `tests/SchoolCollab.Assignments.Api.Tests.Unit/AssignmentSignatureDefaultRouteTests.cs` — **DELETE/replace** with effective-policy route tests (the dedicated `/signature-default` suite dies with the route).
- `tests/SchoolCollab.Assignments.Api.Tests.Unit/AssignmentReaderPolicyRouteTests.cs` — update the covered-route table (~line 96: the `assignments/signature-default` entry leaves NotCoveredRoutes; the new `/assignments/effective-policy` route enters the covered list).
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentDetailBunitTests.cs` — update the route mock (~line 129: the `GET /assignments/signature-default` stub) onto the OD4 effective-policy seam.

**Assignments**
- `src/Assignments/SchoolCollab.Assignments.Contracts/ContractTypes.cs` — `CreateAssignmentRequest` (:187) drops the author `ArchiveGraceDays` (D6); `MandatoryReview` per Pinned OD1; `RequiresSignature` leaves the request (D10). `UpdateAssignmentRequest` (:230) likewise.
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/Assignment.cs` — D4 backstop guard in `Create`/`Update` next to the existing guard (:171-172, :247-248); columns unchanged (`RequiresSignature` :62, `MandatoryReview` :83, `ArchiveGraceDays` :96).
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/CreateAssignmentCommand/CreateAssignmentCommand.cs` (+ Handler) — command fields per OD1/D10; handler injects `IAssignmentPolicyResolver` and sources the three values from `ResolveAsync` (:89/:92/:98 today feed the request values).
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/UpdateAssignmentCommand/UpdateAssignmentCommand.cs` (+ Handler) — same (:50/:52/:58).
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/DuplicateAssignmentCommand/DuplicateAssignmentCommandHandler.cs` — **P1 (third D4 call site):** the clone **COERCES** — `mandatoryReview: source.MandatoryReview || source.RequiresSignature` — and must **NOT** re-resolve via `IAssignmentPolicyResolver`. Rationale: a duplicate copies the source's terms rather than re-authoring them, and a legacy row may legally hold `RequiresSignature = true` with `MandatoryReview = false` (the retired author checkboxes allowed exactly that pair), so without the coercion the new D4 `ArgumentException` guard in `Assignment.Create` throws on duplicating such a row.
- `src/Assignments/SchoolCollab.Assignments.Api/Services/AssignmentPolicyResolver.cs` — both `ToFields` overloads (:88, :96) map the two new DTO fields.
- `src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs` — the request→command mappings (:545, :551, :557, :617-629) stop reading `ArchiveGraceDays`/`RequiresSignature` from the request; the page readout seam per Pinned OD4 (the `/signature-default` route lives at :308-321).
- `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentsApiClient.cs` — `GetSignatureDefaultAsync` (:118) replaced per OD4; page readout client.
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentAuthoring.razor` — the `Compartments` array (:811) 6 → 5, "Rules" section replaces `#authoring-delivery` (:246) and `#authoring-submission` (:296); readout rows per D2; scoring fields (:303-310 incl. `ScoringFieldsSection`) move into Basics under the Grading format field (:147); Guardian signature checkbox (:authoring-submission-requires-signature) removed; Guardian review toggle moves to Basics per Pinned OD3; `ResolveSignatureDefaultAsync` (:1985) / `ApplySignatureRequirement` (:2013) reworked onto the OD4 readout seam.
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentAuthoring.razor.css` — only what the section restructure forces (section-card classes reuse; no restyle).
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentEditFormModel.cs` — `ArchiveGraceDays` (:75) stops being a form field (kept only as the create-request pass-through the handler ignores? — no: remove from the projection at :530 and the update path per D6/AC6); `RequiresSignature` (:93) and the `MandatoryReview` projection (:470, :524) follow Pinned OD1/OD3 (the Guardian-review toggle moves to Basics).

**Tests (per layer, existing suites — add/update, don't scatter new projects)**
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentPolicies/EffectiveAssignmentPolicyResolverTests.cs` — **the existing resolver suite** (AC1/AC2 live here): merged-field resolution + D4 derivation + built-in defaults. Do **NOT** create a new `AssignmentPolicies` location under `SchoolCollab.Core.Tests.Unit` — that would scatter against this section's add/update rule.
- `tests/SchoolCollab.Settings.Tests.Unit/Domain/TenantAssignmentPolicyTests.cs`, `.../Handlers/TenantAssignmentPolicyHandlerTests.cs`, `tests/SchoolCollab.Settings.Api.Tests.Unit/AssignmentPolicyRoutesTests.cs` (+ DTO compat), Settings migration/upsert coverage.
- `tests/SchoolCollab.Students.Tests.Unit` / `tests/SchoolCollab.Students.Api.Tests.Unit` — grade override entity/DTO/endpoint for the two fields; migration coverage.
- `tests/SchoolCollab.Assignments.Api.Tests.Unit/AssignmentPolicyResolverTests.cs` — `ToFields` mappings + fail-open posture for the two new fields.
- `tests/SchoolCollab.Assignments.Tests.Unit/DuplicateAssignmentCommandHandlerTests.cs` — clone coercion test (AC14): a legacy source row `RequiresSignature = true, MandatoryReview = false` duplicates without throwing, the clone persists `RequiresSignature = true, MandatoryReview = true`, and the handler does not call `IAssignmentPolicyResolver`.
- `tests/SchoolCollab.Assignments.Tests.Unit/` — domain backstop (D4), handler snapshot-at-create/update tests, bUnit authoring (`AssignmentAuthoringBunitTests.cs`, `AssignmentCreateBunitTests.cs` — compartments 6→5, Rules readouts, scoring-in-Basics, checkbox retirement, Basics review toggle per OD3).
- `tests/SchoolCollab.Admin.Tests.Unit/AssignmentPolicyEditorTests.cs`, `AssignmentPolicyFieldEditDialogTests.cs`, `GradeLevelDetailPageTests.cs` — the two new editor rows/fields.

### Ordered implementation steps

1. **Core seam first.** Add `MandatoryReview` (`bool?`) and `ArchiveGraceDays`
   (`int?`) to `AssignmentPolicyFields`; extend `EffectiveAssignmentPolicy` (values +
   override flags + built-in-default comment); extend `EffectiveAssignmentPolicyResolver`
   with the per-field merge and the D4 derivation. Resolver unit tests in the **existing
   suite** `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentPolicies/EffectiveAssignmentPolicyResolverTests.cs`
   (resolver merge, D4 implication matrix, null semantics: null grade override ⇒ tenant
   default; both null ⇒ unset per Pinned OD2) — no new test project/location.
2. **Settings tenant default.** Entity columns + `Create`/`SetPolicy` + command + handler
   + DTO + route binding + additive migration. Unit/API tests.
3. **Students grade override.** Entity columns + DTO + `UpsertGradeAssignmentPolicyRequest`
   + endpoint + additive migration (the Settings and Students migrations land together —
   spec §5). Unit/API tests.
4. **Assignments.Api resolution.** Both `ToFields` overloads map the two fields;
   resolver tests assert the new fields flow and that a fetch failure degrades per OD2
   (explicit degradation posture recorded in the plan note below).
5. **Assignments write path.** Domain D4 backstop in `Create`/`Update` (mirror the
   `ArgumentException` guard pattern); `CreateAssignmentCommand`/`UpdateAssignmentCommand`
   stop carrying author `ArchiveGraceDays`/`RequiresSignature`; both handlers inject
   `IAssignmentPolicyResolver` and source the three stored values from the resolved
   policy (request `MandatoryReview` only per Pinned OD1); request/DTO shapes updated
   (Pinned OD1); handler + domain tests. **Third D4 call site — duplicate:**
   `DuplicateAssignmentCommandHandler` coerces
   `mandatoryReview: source.MandatoryReview || source.RequiresSignature` and does **not**
   inject/re-resolve via `IAssignmentPolicyResolver` (a duplicate copies the source's
   terms, not re-authored ones; a legacy row may legally hold
   `RequiresSignature = true, MandatoryReview = false` and would otherwise trip the new
   `Assignment.Create` guard) — covered by AC14's test in
   `DuplicateAssignmentCommandHandlerTests.cs`.
6. **Page restructure (D1/D2/D9/D10 + Pinned OD3/OD4).** `Compartments` 6 → 5 with "Rules"
   between Targets and Content; Rules renders the five policy readouts (approval,
   notification policy, archive window, signature requirement, guardian review) with the
   existing `authoring-policy-row` pattern and the existing `PolicyLinkHref` link;
   scoring fields (`ScoringFieldsSection` + Max score + feedback-mode hint) move into
   Basics directly under Grading format (:147), TeacherGraded disable logic moves with
   them; Guardian signature checkbox removed (the signature policy row states the
   outcome); Guardian review toggle **moves to Basics** (beside the grading format) and
   is editable only when the effective policy leaves review **unset** — Rules keeps the
   policy readout row for review (Pinned OD3); the readout/pre-fill path rewires from
   `GetSignatureDefaultAsync` onto the OD4 seam. Route-test fallout of the OD4 deletion
   in the same pass: DELETE/replace `AssignmentSignatureDefaultRouteTests.cs` with
   effective-policy route tests, update the covered-route table in
   `AssignmentReaderPolicyRouteTests.cs` (~line 96), and update the `/signature-default`
   route mock in `AssignmentDetailBunitTests.cs` (~line 129). Form model per the same
   decisions.
7. **Editors (D8).** `AssignmentPolicyEditor.razor` field defs + rows +
   `AssignmentPolicyFieldEditDialog.razor` kinds/apply + `AssignmentPolicyApiClient`
   upsert DTO + `StudentsApiClient` binding; editor tests on all three touched surfaces.
8. **Authoritative verification.** `dotnet build SchoolCollab.slnx` (0 errors), then
   `dotnet test` on the affected projects **plus `SchoolCollab.ArchitectureTests.Unit`**.

### Degradation posture (spec §5 risk, restated so the worker does not guess)

`AssignmentPolicyResolver` is fail-open per its doc comment. For the two new fields the
degraded shapes are explicitly: policy fetch fails / row absent ⇒ resolver sees
`AssignmentPolicyFields.Empty` ⇒ `MandatoryReview` is **null** (author-choice, Pinned
OD1's `?? true` write fallback preserves today's mandatory-review default) and
`ArchiveGraceDays` is **null** ⇒ the write seam uses the built-in **30** so the archive
sweep keeps its retention floor. Degradation never turns a stricter policy ON, and it
never silently zeroes the archive window — mirroring the existing approval-OR-flag
posture (`IAssignmentPolicyResolver.cs` doc comment).

### Open decisions: none (all five pinned — the list was resolved by the owner; see
`## Pinned decisions (owner, 2026-10-07)` below).

(Nothing else in the plan is a fork; everything is a fact pinned to a file:line above.)

### Acceptance criteria (each discriminating)

Each criterion states what the pre-fix code does that would fail the test —
"a pre-fix run of the same test would go red".

- **AC1 — Field set + merge (D5/D7).** A resolver test in the **existing suite**
  `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentPolicies/EffectiveAssignmentPolicyResolverTests.cs`:
  tenant default sets
  `ArchiveGraceDays = 42`, grade override sets `MandatoryReview = true`; resolved value
  of `ArchiveGraceDays` (no override) is 42, of `MandatoryReview` (override) is true,
  and both `…FromOverride` flags are right. *Pre-fix failure:* the test does not
  compile — neither field exists on `AssignmentPolicyFields`/`EffectiveAssignmentPolicy`
  (today: exactly 4 fields, `AssignmentPolicyFields.cs:24-28`).
- **AC2 — D4 resolver derivation.** Same existing resolver suite
  (`…/AssignmentPolicies/EffectiveAssignmentPolicyResolverTests.cs`) test matrix:
  mode `Mandatory` + policy
  review null ⇒ resolved `MandatoryReview` = true; mode `Disabled` + policy review true
  ⇒ true; mode `Optional` + policy review null ⇒ true per Pinned OD5; mode `Disabled` +
  policy review unset ⇒ null (Pinned OD2). *Pre-fix failure:* cannot express — no
  field, and the resolver has no implication rule.
- **AC3 — D4 domain backstop.** `Assignment.Create(…, requiresSignature: true,
  mandatoryReview: false)` throws `ArgumentException`; same guard for `Update`.
  *Pre-fix failure:* both calls succeed silently — `Assignment.cs` has no such guard
  (only `passScore > maxScore` throws, :171-172/:247-248).
- **AC4 — Snapshot at create (D6).** Handler test: policy resolved as
  `MandatoryReview = true`, payload sends `MandatoryReview = false`, no
  `ArchiveGraceDays` in play ⇒ persisted row has `MandatoryReview = true` and
  `ArchiveGraceDays` = the resolver's value (30 when unset, Pinned OD2). *Pre-fix failure:* the
  pre-fix handler sources all three from the request (`CreateAssignmentCommandHandler.cs:89/:92/:98`)
  and would persist `false`/the request's grace days.
- **AC5 — No author input for archive grace / signature (D6/D10).**
  `CreateAssignmentRequest`/`UpdateAssignmentRequest` carry no `ArchiveGraceDays` and no
  `RequiresSignature`; the API route mappings (:551/:557/:623/:629) and the form-model
  projection (:530) no longer thread them. *Pre-fix failure:* the DTOs carry both
  (`ContractTypes.cs:187/:230`) and the request wins.
- **AC6 — Migrations pair (D7).** Settings AND Students each have one additive
  migration adding `MandatoryReview` (nullable bool) and `ArchiveGraceDays` (nullable
  int) to their policy tables, plus snapshots; the two land in the same change.
  *Pre-fix failure:* no such migrations exist; an upsert of the new fields would fail
  against either database.
- **AC7 — Editors expose the fields (D8).** Editor tests: the field dialog can set
  `MandatoryReview` (Bool kind) and `ArchiveGraceDays` (Int kind) in both scopes; the
  tenant-default upsert and the grade PUT round-trip them (DTO compat tests).
  *Pre-fix failure:* `AssignmentPolicyEditor.razor`'s `FieldDef[]` (:65-70) and the
  dialog's apply switch (:145-156) have no such cases; the wire DTOs drop the fields.
- **AC8 — Compartments 6 → 5, Rules (D1/D2).** bUnit: the jump-nav/`Compartments`
  render exactly Basics, Targets & audience, **Rules**, Content & Resources, Questions &
  AI, in that order; Rules contains the five readout rows (approval, notification,
  archive window, signature requirement, guardian review) and no `FluentNumberField`
  /`FluentDatePicker`/`FluentCheckbox` bound to author-editable policy inputs. **OD3
  reconciliation:** the author Guardian-review toggle is **not** among them — it lives
  in Basics (editable only when the effective policy leaves review unset); Rules keeps
  the guardian-review row as a pure policy READOUT, exactly like every other readout
  row, so D2's "Rules is readouts only" holds literally. *Pre-fix failure:* six
  compartments render (`Compartments` :811) with
  `Delivery & Publishing` and `Submission & Sign-off`, and no Rules section exists.
- **AC9 — Scoring fields in Basics (D9).** bUnit: Max score / `ScoringFieldsSection`
  / feedback-mode hint render inside `#authoring-basics` directly after the Grading
  format field (:147); the TeacherGraded disable-with-reason behavior survives the move.
  *Pre-fix failure:* they render in `#authoring-submission` (:303-310).
- **AC10 — Guardian signature checkbox retired (D10).** bUnit/source: no author-facing
  `RequiresSignature` checkbox binds anywhere (`_requiresSignature` writes gone from the
  render tree); the signature policy row states the outcome. The Guardian-review toggle
  follows Pinned OD3: it lives in Basics beside the grading format, editable **only**
  when the effective policy leaves review unset, and a locked readout row in Rules
  otherwise. *Pre-fix failure:* the checkbox renders
  (`#authoring-submission-requires-signature`) with the `Optional` pre-tick path
  (`ApplySignatureRequirement`, razor :2013).
- **AC11 — Readout seam (OD4).** bUnit/mock: the page fetches the full effective policy
  from `/assignments/effective-policy` and renders the archive window + guardian review
  readouts from it without recomputing the D4 implication in Razor; the
  `/signature-default` route and `GetSignatureDefaultAsync` are gone.
  *Pre-fix failure:* no such endpoint/client exists and the pre-fix page has no
  archive/review readouts.
- **AC12 — No retro-apply seams (D6).** Diff shows **zero** changes to
  `ArchiveSweepService`, `ListDueForArchiveAsync` (`AssignmentRepository.cs:74`),
  `CreateStudentSubmissionCommandHandler` (:36-38), and no back-fill migration; a
  handler test proves update re-snapshots the three columns from the **currently
  resolved** policy only at explicit save time (spec blast radius pins
  `UpdateAssignmentCommandHandler` to the resolver source). *Pre-fix failure:* pre-fix
  reads the same rows but from request-fed columns — the discriminating change is that
  the pre-fix update path persists request values the plan forbids.
- **AC13 — Hard constraints.** The diff touches nothing under
  `src/SchoolCollab.MigrationService/**`, no cross-context project references are added,
  no CPM `Version=` appears, and the two policy migrations are additive (no drops).
- **AC14 — Duplicate coercion (D4, third call site).**
  `DuplicateAssignmentCommandHandlerTests.cs`: a legacy source row
  (`RequiresSignature = true`, `MandatoryReview = false` — a pair the retired author
  checkboxes legally allowed) duplicates **without throwing**, the clone persists
  `RequiresSignature = true` and `MandatoryReview = true` (coerced), and the duplicate
  path never calls `IAssignmentPolicyResolver`. *Pre-fix failure:* the clone stores the
  false/false pair verbatim (`mandatoryReview: source.MandatoryReview` passed
  straight through), which after AC3's `Assignment.Create` guard throws
  `ArgumentException` on duplicating exactly such a legacy row.

### Worker task spec

You are the worker. Repo: `C:\Users\skwar\source\repos\School-Collab`. Implement
exactly the `## Plan` section of
`documents/rounds/round-assignment-rules-policy-rework.md` (read it first, together
with its source of truth `documents/specs/assignment-rules-policy-rework.md` for
ambiguity only — the plan is authoritative). Never edit the round doc, the spec, or
anything under `src/SchoolCollab.MigrationService/**`. Do not commit, stage, or push.

The tree is DIRTY pre-round (see
`documents/rounds/round-assignment-rules-policy-rework.base-names.txt`); touch **only**
the plan's expected files — any path outside that list in your change set must appear
under "Deviations from plan".

The `## Pinned decisions (owner, 2026-10-07)` section is **binding**; implement each
OD1–OD5 exactly as pinned there. If any pin arrives unresolved or contradicts the
code being changed, STOP and ask the parent instead of picking.

Guards (hard):
- Repo-scoped search only: `grep`/`rg` under `src/`, `tests/`, or
  `~/.nuget/packages` — never `find /`.
- Build: `dotnet build SchoolCollab.slnx` after every change layer; fix errors before
  continuing (MSB3021/MSB3027 = find and stop the offending `dotnet` process; do not
  retry in a loop).
- Tests: exactly one output read per suite —
  `dotnet test tests/<Proj> 2>&1 | grep -E "^\s*failed|total:|failed:" | head -40`;
  no ad-hoc pipelines, no `zz*.log` debug files; at most 2 attempts per result read;
  if output is truncated redirect to a file once and read the tail.
- Include `tests/SchoolCollab.ArchitectureTests.Unit` in the final test run.
- bUnit on FluentUI: drive bound callbacks via
  `cut.InvokeAsync(() => cut.FindComponent<FluentCheckbox>().Instance.ValueChanged.InvokeAsync(...))`
  — never generic `TriggerEvent` (repo pitfall); every context-mock matcher must pass an
  explicit `HttpMethod.Get`/`HttpMethod.Post`.

Expected files: the plan's "Expected files" list (grouped by layer), verbatim. The
round patch will be frozen at `documents/rounds/diffs-assignment-rules-policy-rework.patch`.

Return ONLY:

    WORKER REPORT
    Changed files: <path list>
    Build: <"0 errors" | "n errors" + one-line detail each>
    Tests: <project: n passed, m failed | "not run" + why>
    Deviations from plan: <none | one line each>

### Reviewer task spec

You are the static diff reviewer. **Never build, never test, never write files.** Repo:
`C:\Users\skwar\source\repos\School-Collab`. Read:

1. `documents/rounds/round-assignment-rules-policy-rework.md` — `## Plan`
   (incl. `## Pinned decisions (owner, 2026-10-07)` and `### Acceptance criteria`) is
   the contract.
2. The frozen diff: `documents/rounds/diffs-assignment-rules-policy-rework.patch`
   (round base `870b4b0f314f239de400663b8c65a4cc10f8faca`; the tree was dirty at
   round start — the plan's expected-files list, not git status, is your scope
   reference).
3. The WORKER REPORT below the `## Worker Report` heading of the round doc (its changed
   files, build/test verdicts, deviations).
4. The changed files' surrounding code as needed, and the cited seams (spec
   `documents/specs/assignment-rules-policy-rework.md` for the settled decisions only).

Verify the diff against the **plan** — every expected file touched and no more; each
plan decision (D1–D10, OD1–OD5) implemented in the pinned form; scope OUT items not
bleeding in. Include the best-coding-practices check:
(1) **no overwrites** — the diff must not rewrite or delete pre-existing code outside
the plan's scope (unrelated deletions/reformatting/"improvements" are findings);
(2) **repo skills honored** — `dotnet-best-practices` (any `.cs`), `blazor-components`
/ CSS-isolation (`.razor`/`.razor.css`), `fluentui-*` (component props/icons),
`dialog-ui` (the policy field dialog edits), `dropdown-ui` where selects are touched;
(3) **readability** — naming matches repo conventions, minimal focused diff, no dead
or duplicated code; the two policy-migration pair and the snapshot-at-create handler
wiring get specific attention.

**Split rule:** if the frozen patch exceeds ~3k lines, the parent splits it by area
(Core+Settings / Students+editors / Assignments write path / page+bUnit) into
`diffs-…-<area>.patch` slices whose file lists mechanically partition the full patch
(verified by concatenating slices against the full patch); you receive your slice path
plus the plan sections covering it and the round's cross-slice seams — **both ends of
each seam must be traced**: the new `AssignmentPolicyFields` members ↔ both DTOs ↔ both
`ToFields` overloads ↔ `EffectiveAssignmentPolicyResolver` merge ↔ the handlers'
resolver source ↔ the page readouts; the two migrations ↔ both entity configurations;
the request-shape change ↔ route mappings ↔ form-model projection. "The far slice is
clean" is not evidence; trace the seam yourself. Return only the REVIEW block for your
slice plus a `Seams:` line naming the cross-slice seams you traced.

Return ONLY:

    REVIEW
    Verdict: PASS | P1 | P2-only
    P1: <file:line — issue>   (one per line; none if empty)
    P2: <file:line — issue>
    Best-practices: <no overwrites / skills honored / readable | violations as P1/P2 above>
    Seams: <traced seam list | n/a for whole-patch review>

### Acceptance criteria

The acceptance verdict is written against this checklist (criteria AC1–AC14 in
`## Plan` are the discriminators — each must pass on the post-fix tree, and the parent
may spot-check one by confirming the pre-fix behavior it names from the cited file:line):

- [ ] Plan gate: `## Review` carries the PLAN REVIEW entry with **no open P1** at
      dispatch time; the `## Pinned decisions (owner, 2026-10-07)` section pins OD1–OD5
      before the worker runs.
- [ ] WORKER REPORT present in `## Worker Report` with build "0 errors" and
      test verdicts, and the parent's authoritative pass confirms them (the parent's
      numbers win; child-reported numbers are never the source of truth).
- [ ] Authoritative build: `dotnet build SchoolCollab.slnx` — 0 errors (parent-run).
- [ ] Authoritative tests: `dotnet test` on the affected projects **plus
      `SchoolCollab.ArchitectureTests.Unit`** — 0 failures (parent-run), counts in
      `## Acceptance`.
- [ ] AC1–AC14 pass (reviewer + parent spot-checks; migrations pair AC6 verified
      against the actual migration files, editors AC7 against the dialogs' field
      handling).
- [ ] REVIEW verdict CLOSED (no open P1; residual P2s listed with accept/defer).
- [ ] Frozen patch exists at `documents/rounds/diffs-assignment-rules-policy-rework.patch`.
- [ ] Scope OUT unviolated: `ArchiveSweepService`, submit gate, and
      `src/SchoolCollab.MigrationService/**` untouched; no legacy-compat deletions
      beyond OD4's pinned `/signature-default` removal; no commit/push.
- [ ] UI trigger fired → UI tester dispatched on the orchestrator's tester-scope
      handover (changed `.razor` surfaces = AssignmentAuthoring page + policy editors;
      tester returns UI TEST; ≤2 tester rework iterations).
- [ ] Round-doc `**Status:**` flips to CLOSED only when every box above is green and
      the ledger row (`documents/solution/round-runner-model-ledger.md`) is written.
## Pinned decisions (owner, 2026-10-07)

All five former open decisions are resolved and pinned. These supersede any
recommended-answer wording elsewhere in this doc and are binding on worker and
reviewer alike.

- **OD1** — `MandatoryReview` stays in `CreateAssignmentRequest`/`UpdateAssignmentRequest`
  as `bool?`; the handler stores
  `effective.MandatoryReview ?? request.MandatoryReview ?? true`, so a resolved policy
  always beats an author payload; `RequiresSignature` leaves the request entirely (D10).
- **OD2** — `EffectiveAssignmentPolicy.MandatoryReview` stays `bool?`; the resolver emits
  the merged nullable value with the D4 implication applied (null only when neither
  level sets it); the write seam falls back to `true`; the archive window falls back
  to `30`.
- **OD3** — the author guardian-review toggle MOVES TO BASICS (beside the grading
  format, where the scoring fields moved per D9); it is editable ONLY when the
  effective policy leaves review unset. `Rules` keeps a policy READOUT row for review,
  exactly like every other readout row. This keeps spec decision D2 (Rules is readouts
  only) literally true while keeping D3 author-choice half alive.
- **OD4** — add `GET /assignments/effective-policy?gradeLevelId=` and DELETE
  `/assignments/signature-default` (the route, its dedicated test suite,
  its covered-route table entry, and the bUnit mock).
- **OD5** — `SignatureRequirementMode.Optional` stores `RequiresSignature = true` and
  feeds the D4 implication, matching the existing precedent in
  `SchoolCollab.Assignments.Application/Services/AssignmentsApiClient.cs`
  (`RequiresSignatureDefault`: Optional/Mandatory -> true, Disabled/unset -> false).
  No new semantics.


### PLAN REVIEW RE-READ (gate pass 1, revised sections) - **ACCEPT**

Verdict ACCEPT; P1 none; P2 none new. All six checks verified: (1) the third D4 call site is
covered in step 5 and the expected-files list with the pinned COERCE posture, and the live
handler today passes both fields verbatim - the premise is accurate; (2) the deleted legacy
route's test surface is listed; (3) AC1/AC2 point at the existing resolver suite; (4) AC8 is
reconciled with the OD3 pin (toggle in Basics, Rules keeps a readout row); (5) OD1-OD5 are
quoted verbatim and the plan body is consistent with all five; (6) `Open decisions:` reads
none and the worker spec, reviewer spec and AC1-AC14 agree. Plan cleared for worker dispatch
(iteration 1/1 used).


### WORKER RUN (pass 1) - **TIMED OUT** at the 30-minute cap; escalation dispatched

The first worker pass (`deepseek-v4.1-flash`) hit the harness timeout after 1800000 ms. On-disk
reconciliation at the timeout (parent): both additive migrations were already GENERATED -
`20261007100017_AddAssignmentPolicyReviewAndArchiveFields` (Settings) and
`20261007100026_AddAssignmentPolicyReviewAndArchiveFields` (Students), each with its Designer and an
updated model snapshot - and the working tree grew from 127 to 175 entries. No protected file was
touched by the pass (round doc, spec, ledger, AGENTS.md, .pi/skills unchanged). Per the skill build
escalation pattern the SAME pass scope is re-dispatched as an ESCALATION PASS on the round reviewer
model (`kimi-k2.7-code`) through a write-capable shell, reconciling the on-disk state first;
escalated work is re-verified by the higher model of the same profile (`glm-5.3`). Subsequent worker
passes revert to the worker model.


### DIFF REVIEW - split slices (both PASS, no P1)

Patch frozen at `documents/rounds/diffs-assignment-rules-policy-rework.patch` (8762 lines; 5 new / 74 modified / 1
deleted), diffed against the pre-round tree snapshot so only this round changes appear. Slices partition the patch
exactly: A (domain/contracts/policy/migrations/routes) 4647 lines on `glm-5.3` - also satisfying the policy
requirement that ESCALATED work be re-verified on the higher model - and B (UI/clients/tests) 4115 lines on
`kimi-k2.7-code`.

**Slice A - PASS**, P2 only. Verified the shared field set and the D4 derivation
(`EffectiveAssignmentPolicyResolver.cs:31-49`), both additive migrations with matching Designers and snapshots,
the request-field removal with `MandatoryReview` still `bool?`, the domain backstop
(`Assignment.cs:180-185`, `:263-266`), the handlers sourcing `policy.X ?? command.X ?? default`, the duplicate
coercion with no resolver injected (`DuplicateAssignmentCommandHandler.cs:68`), the new authorized
`/assignments/effective-policy` route and the deleted legacy route, and tenant isolation on both policy entities.
Its P2 (a stale `AssignmentEndpoints.cs` comment saying six GETs / signature-default) was APPLIED.

**Slice B - PASS**, P2 only. Verified five compartments
(`AssignmentAuthoring.razor:843-850`), Rules holding only readout rows, the scoring fields moved into Basics under
the grading format (`:147-166`), the review toggle in Basics with `PolicyReviewLocked` (`:1068`) and its reason,
the retired signature checkbox and the effective-policy fetch, the form-model projection, both admin editors
exposing the two new fields, and the deleted legacy route suite being REPLACED by
`AssignmentEffectivePolicyRouteTests.cs` (incl. a 404 assertion for the deleted route). Its three stale-comment
P2s were APPLIED. Two P2s are recorded, not fixed: `DuplicateAssignmentCommandHandlerTests.cs:160` is a regression
guard rather than a discriminator (its sibling coercion test is the discriminating one), and six test files took
fixture-only updates that keep them legal under the new D4 guard without testing new behaviour.

### PARENT AUTHORITATIVE VERIFICATION (post-fix)

`dotnet build SchoolCollab.slnx` - 0 errors, 17 pre-existing warnings. Suites: Assignments 909/0, Assignments.Api
160/0, Settings 539/0, Students 624/0, Admin 626/0, Architecture 102/0; re-run after the P2 fixes: Assignments
PASS, Architecture PASS.

## Review

### PLAN REVIEW (gate pass 1 — one iteration used)

Verdict: **REWORK** — 1 P1 + 3 P2. All applied in this revision; no open findings.

| # | Finding | Where applied |
|---|---|---|
| P1 | D4 domain guard has an uncovered third call site: the duplicate handler passes `source.MandatoryReview`/`source.RequiresSignature` through verbatim and would trip the new `Assignment.Create` guard on a legacy `true`/`false` row | step 5 + expected files (`DuplicateAssignmentCommandHandler.cs`, `DuplicateAssignmentCommandHandlerTests.cs`) + AC14, pinned COERCE posture, no resolver re-resolution |
| P2a | The deleted `/signature-default` route owns test files the expected list omitted | expected files ("Deleted legacy route's test surface" group) + step 6: `AssignmentSignatureDefaultRouteTests.cs` delete/replace, `AssignmentReaderPolicyRouteTests.cs` covered-route table (~:96), `AssignmentDetailBunitTests.cs` route mock (~:129) — API route tests, not bUnit |
| P2b | Resolver tests scattered to a new location | AC1/AC2 retargeted to the existing suite `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentPolicies/EffectiveAssignmentPolicyResolverTests.cs`; the `SchoolCollab.Core.Tests.Unit` tests bullet removed; step 1 updated; no new `AssignmentPolicies` location under `SchoolCollab.Core.Tests.Unit` |
| P2c | AC8 conflicted with the pinned OD3 toggle placement | AC8 and AC10 reconciled: the Guardian-review toggle lives in Basics (editable only when review is unset); Rules keeps the review policy READOUT row, so D2 stays literally true |

Consistency re-check after applying: worker spec, reviewer spec, expected-files list,
steps, and AC1–AC14 all reference the pinned OD1–OD5 with a single meaning (OD3 = Basics
toggle + Rules readout row; OD4 = effective-policy route add + signature-default delete
including its test surface). `Open decisions:` is **none**.

## Acceptance

**Verdict: ACCEPT.** Plan gate: REWORK then, after one revision, ACCEPT
(iteration 1/1 used). Diff review split by area: slice A PASS on `glm-5.3`, slice B
PASS on `kimi-k2.7-code`, **0 P1 in either slice**. UI test: 0 P1. The parent's
authoritative build/test pass is green. The round doc `Status` is flipped to CLOSED
as a **pre-commit** close — the work is committed nowhere yet (no commit, no PR, no
push; destination branch `feature/assignment-authoring-targets-and-rules`).

### Criteria checklist (AC1–AC14, all satisfied)

Evidence taken from the two DIFF REVIEW slices and the UI TEST.

| AC | Status | Evidence |
|---|---|---|
| AC1 — field set + merge (D5/D7) | satisfied | `EffectiveAssignmentPolicyResolver.cs:31-49` — shared field set + per-field merge verified in slice A (`glm-5.3`); resolver suite in `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentPolicies/` |
| AC2 — D4 resolver derivation | satisfied | `EffectiveAssignmentPolicyResolver.cs:31-49` — D4 derivation verified in slice A |
| AC3 — D4 domain backstop | satisfied | `Assignment.cs:180-185` and `:263-266` — guard alongside the existing `passScore ≤ maxScore` guard, verified in slice A |
| AC4 — snapshot at create (D6) | satisfied | Create/Update handlers source `policy.X ?? command.X ?? default` from the resolved policy, verified in slice A |
| AC5 — no author input for archive grace / signature (D6/D10) | satisfied | request-field removal with `MandatoryReview` still `bool?` (slice A) + the form-model projection (`AssignmentEditFormModel.cs`, slice B) |
| AC6 — migrations pair (D7) | satisfied | `20261007100017_AddAssignmentPolicyReviewAndArchiveFields` (Settings) + `20261007100026_AddAssignmentPolicyReviewAndArchiveFields` (Students), each with its Designer and updated model snapshot, verified in slice A |
| AC7 — editors expose the fields (D8) | satisfied | both admin editors expose the two new fields — `AssignmentPolicyEditor.razor` and `AssignmentPolicyFieldEditDialog.razor`, verified in slice B (Admin suite 626/0) |
| AC8 — compartments 6 → 5, Rules readouts only (D1/D2) | satisfied | `AssignmentAuthoring.razor:843-850` — five compartments, Rules holding only readout rows, verified in slice B |
| AC9 — scoring fields in Basics (D9) | satisfied | `AssignmentAuthoring.razor:147-166` — scoring fields moved into Basics under the grading format, verified in slice B |
| AC10 — Guardian signature checkbox retired (D10/OD3) | satisfied | retired signature checkbox, review toggle in Basics with `PolicyReviewLocked` and its reason (`AssignmentAuthoring.razor:1068`), verified in slice B |
| AC11 — effective-policy readout seam (OD4) | satisfied | authorized `GET /assignments/effective-policy` route + deleted legacy route (slice A); page fetches the effective policy and the legacy suite was REPLACED by `AssignmentEffectivePolicyRouteTests.cs` incl. a 404 assertion for the deleted route (slice B) |
| AC12 — no retro-apply seams (D6) | satisfied | slice A verified the handlers source the three stored values from the resolved policy at save time; the patch's file list shows zero changes to `ArchiveSweepService`, `CreateStudentSubmissionCommandHandler`, or any back-fill migration (`diffs-assignment-rules-policy-rework.patch`) |
| AC13 — hard constraints | satisfied | frozen patch touches nothing under `src/SchoolCollab.MigrationService/**`, adds no cross-context project references, no CPM `Version=`, and both policy migrations are additive (`diffs-assignment-rules-policy-rework.patch`) |
| AC14 — duplicate coercion (D4 third call site) | satisfied | `DuplicateAssignmentCommandHandler.cs:68` — legacy `true`/`false` pair coerced, no resolver injected, verified in slice A (test in `DuplicateAssignmentCommandHandlerTests.cs`) |

### Authoritative build/test (parent-run; parent numbers win — verbatim)

`dotnet build SchoolCollab.slnx` = 0 errors, 17 pre-existing warnings. Suites:
Assignments 909/0, Assignments.Api 160/0, Settings 539/0, Students 624/0, Admin 626/0,
Architecture 102/0. After the P2 comment fixes were applied, Assignments and
Architecture were re-run and passed.

### P1 list

None. (The plan gate's single P1 — the uncovered third D4 call site — was resolved in
the one plan revision before dispatch. Diff slices A and B: 0 P1. UI test: 0 P1.)

### Residual P2s and their dispositions

- **APPLIED (4, stale comments):** the `AssignmentEndpoints.cs` six-GETs /
  signature-default comment (slice A's P2), and the three stale-comment P2s in slice
  B's file set. Assignments and Architecture suites re-run green post-application.
- **RECORDED, NOT FIXED (6):**
  - `DuplicateAssignmentCommandHandlerTests.cs:160` — vacuous-vs-discriminating test
    note: that test is a regression guard rather than a discriminator; its sibling
    coercion test is the discriminating one.
  - Six test files took fixture-only updates that keep them legal under the new D4
    guard without testing new behaviour.
  - UI tester P2 (a): in View / read-only mode the guardian-review toggle is disabled
    by `IsReadOnly` alone, so no reason tooltip surfaces (informational, not a defect).
  - UI tester P2 (b): the Students field-edit dialog's help text is generic for the
    two new rows (informational, not a defect).
- **Backlog (out of round):** `.scoring-fields-reason` has no CSS rule anywhere in the
  tree (`ScoringFieldsSection.razor:35`), pre-dating this round.

## UI Tester

UI TEST (tier-3 UI round; tester dispatched on the orchestrator's tester-scope
handover over the changed `.razor` surfaces — AssignmentAuthoring page + both policy
editors):

```
UI TEST
Verdict: PASS — no P1.
P1: (none)
P2 (a): AssignmentAuthoring View / read-only mode — the guardian-review toggle is
        disabled by `IsReadOnly` alone, so no reason tooltip surfaces in that mode.
P2 (b): Students field-edit dialog — the help text is generic for the two new rows
        (`MandatoryReview`, `ArchiveGraceDays`).
```

Both P2s are informational, not defects — RECORDED, NOT FIXED (see `## Acceptance`).
