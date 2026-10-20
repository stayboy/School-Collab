# Round B-approval — the assignment approval workflow (Spec B)

**Status:** PLANNING · Mode: Tier 3 **full, attended** (spec §9's recommendation; the round carries **three migrations**, which always reach a human) · Provider: **pi**
Orchestrator/plan: `ollama-cloud/glm-5.3-flash` · Plan gate: `ollama/glm-5.3:cloud` · Worker: `ollama-cloud/deepseek-v4.1-flash:1017` · Diff gate: `ollama-cloud/deepseek-v4-pro:0813` · Acceptance: `deepseek-v4-pro:0813` · UI tester: **deferred to the residuals round** (the AppHost cannot serve — Q91a; this round's UI evidence is bUnit-only, stated in acceptance)
Branch: `feat/assignment-approval-workflow` off **`feat/notification-templates-b3b`** (PR #382 stays open as the stack bottom, per the owner) · Spec: `documents/specs/assignment-approval-workflow.md` (§3–§9)

Owner rulings folded: **Q89** (single round per §9) · **Q91** (bUnit-only UI evidence; the browser pass and the AppHost diagnosis are residual) · stack policy (a related PR is open ⇒ this is a **layer**, not an independent PR).

## Substrate inventory (taken before planning)

| Question | Answer found | Consequence |
|---|---|---|
| What exists in the domain today? | `Assignment.ApprovalStatus` nullable (`Assignment.cs:107`); `SubmitForApproval()` sets `Pending` (`:691`, and today it checks **Status only** — a resubmit hole); `Approve` (`:706`); `Reject` (`:723`); publish guard `:601-602`, schedule guard `:663-664`; `AssignmentApprovalRequiredException` mapped at `AssignmentRoutes.cs:735`/`:773` | WS1 adds `Denied`, `SubmittedAt`, `ApprovalReason` and the transitions; **the guards stay untouched** |
| How is `ApprovalStatus` stored and travelled? | `AssignmentConfiguration.cs:87` is a bare `builder.Property(x => x.ApprovalStatus)` — **EF's default integer form**, no `HasConversion`. On the wire the Assignments API registers `JsonStringEnumConverter<ApprovalStatusDto>` (`Program.cs:49`) | adding `Denied = 3` is additive on both; the DTO travels by **name**, the column stays int — recorded so nobody "tidies" it into a string column mid-round |
| What do the approval requests carry? | `ApproveAssignmentRequest(Guid ApproverId)` (`ContractTypes.cs:386`), `RejectAssignmentRequest(Guid ApproverId)` (`:392`) | WS5 adds `Reason` to Reject and introduces Deny |
| What are the authoring write paths the hold must cover? | the commands folder holds **30** `ICommandHandler<>` implementations; the ones reachable while `Pending` today were enumerated and classified in WS2 | WS2 carries the **explicit** hold/exempt split and one test per row — "enumerate the folder" is not an instruction, it is a gap |
| Is the policy field shape ready? | `AssignmentPolicyFields` is a `sealed record` with **six** members (`SignatureRequirement`, `RequiresApprovalBeforePublish`, `MaxPrimaryContacts`, `MaxCopyContacts`, `MandatoryReview`, `ArchiveGraceDays`) — matching the spec's 2026-10-18 field-count note | `ApprovalWindowDays` is the **seventh**; WS3 adds it here plus the two mirrors |
| Which sweeps exist to mirror? | `ArchiveSweeper`, `ScheduledPublishSweeper`, `OverdueSweeper`, `ReminderSweeper`, `StagedFileSweeper` (+ their services) | WS4's `ApprovalSweeper` mirrors `ArchiveSweeper`'s cross-tenant/per-row-tenant shape |
| Where do the migrations land? | assignments-db latest is `20261019115238_…`; settings-db and students-db each hold their own context | three migrations, one per context (§4) |
| Which UI surfaces? | `Detail.razor:73-90` (approval panel + chip), `Index.razor:337/:416-422` (submit action, approval guard comment), the Edit page | WS6 touches all three |

## Workstreams

**WS1 — the domain state machine.**
1. `ApprovalStatus` gains **`Denied = 3`** (additive; `ApprovalStatusDto` mirrors it, and the name travels the wire per the converter above).
2. New `Assignment` columns: `SubmittedAt` (`DateTimeOffset?`), `ApprovalReason` (`string?`).
3. Transitions: `SubmitForApproval()` stamps `SubmittedAt`, **clears `ApprovalReason`**, **refuses when `Denied`** (terminal), and **refuses while `Pending`** — today's guard checks `Status == Draft` only (`Assignment.cs:691-694`), so a resubmit on a Pending row would re-stamp `SubmittedAt` and **reset the approval window**, making the §3.4 auto-return author-defeatable; resubmit is therefore legal only from `null`/`Rejected` (matching Q9/Q10 and the Detail UI, which already hides the button for Pending rows, `Detail.razor:83`). `Reject(approverId, reason?)` records the reason and **clears `SubmittedAt`** (a returned row must not carry a stale window); **`Deny(approverId, reason?)`** (Pending → Denied, terminal, releases the hold) also clears it; `Approve` unchanged.
4. Publish/schedule guards **unchanged** — `ApprovalStatus != Approved` already blocks Pending and Denied alike. A test pins that `Denied` is refused by both without touching the guard code.

**WS2 — the pending edit hold.**
`AssignmentPendingApprovalException` (new typed exception, the `AssignmentApprovalRequiredException` shape) thrown while `ApprovalStatus == Pending`; the API maps it to **409**.

**The classification is decided here, not discovered by the worker** (every handler below was checked against its guard and the domain state in `Pending`, which keeps `Status = Draft` — `Assignment.cs:691-699`):

| Handler | Choice | Why |
|---|---|---|
| `UpdateAssignment` | **HOLD** | the canonical authoring write |
| `ConfirmQuestionsDraft` | **HOLD** | its domain guard (`Assignment.cs:578-580`) passes for `Pending` — the reviewed questions would change |
| `LinkAssignmentGroups` | **HOLD** | changes the reviewed audience |
| `DeleteAssignment` | **HOLD** | today's guard is Draft-only, so an author could destroy the row under review; §3.2's "released only on Approve / Reject / Deny" means delete waits too |
| `StageQuestionsDraft` / `DiscardQuestionsDraft` | **HOLD** | their guards (`Assignment.cs:554`/`:565`) also pass for `Pending`, and the staged blob *is* authored content |
| `RegenerateAttachmentExtraction` | **HOLD** | mutates `ExtractedText`, which is the reviewed content (§3.2 names attachments) |
| `SubmitAssignmentForApproval` | **HOLD while `Pending`** | see WS1.3: a resubmit would re-stamp `SubmittedAt` and reset the window (the guard at `Assignment.cs:691-694` passes today) — this is the author-defeatable-window hole |
| `ApproveAssignment` / `RejectAssignment` | **exempt** | they *are* the release path (Q9) — holding them would deadlock a Pending row |
| `StageAttachment` | **exempt** (recorded) | blob store only — it never loads or writes the assignment row; the row-level attachment write happens inside the held `Update` |
| `RecordQuestionGeneration` | **exempt** | provenance header only |
| `ReviewAssignment` | **exempt** | peer review **of the assignment** (`ReviewAssignmentCommandHandler.cs:36` `AddReview(...)`) — an annotation, not authoring |
| `Create` | **exempt** | a fresh row cannot be Pending |
| `Duplicate` | **exempt** (Q9) | a copy is a new Draft with the approval stamps dropped (`DuplicateAssignmentCommandHandler.cs:19-21`) — the documented escape hatch |
| `Publish` / `Schedule` | **exempt** (already guarded) | `Assignment.cs:601-602`/`:663-664` refuse anything `!= Approved` — they throw `AssignmentApprovalRequiredException`, so the test asserts **that**, not success |
| `Unpublish` | **exempt** (status-guarded) | refuses Draft (`:625-626`) — a Pending row cannot reach it |
| `Archive` | **exempt** (status-guarded) | its guard (`:678-681`) refuses Draft; `:683` is only the status write |
| `Close` | **exempt, but its guard is NOT a protection** | `Close()` (`:634-643`) refuses only Archived and no-ops Closed — **a Pending row closes successfully** (Closed + Pending). It is a lifecycle op, not authoring, so it stays exempt; the sweep's candidate read is narrowed instead (WS4.1) |
| student / submission / sign-off handlers | **exempt** | outside assignment authoring |

One test **per row** — held handlers assert the typed exception (and the 409 mapping at the API for one representative); exempt rows assert **the hold exception is not thrown and the handler's pre-existing behaviour applies** (for `Publish`/`Schedule` that is `AssignmentApprovalRequiredException`; for `Unpublish`/`Archive`/`Close` it is their own guard or no-op) — asserting "still works" would be untestable for four of them.

**WS3 — the approval window policy field.**
1. `AssignmentPolicyFields.ApprovalWindowDays` (`int?`) — the seventh member of `src/SchoolCollab.Core/AssignmentPolicies/AssignmentPolicyFields.cs`.
2. Mirrored nullable columns: `TenantAssignmentPolicy` (settings-db) and `GradeAssignmentPolicy` (students-db) + their migrations; resolved by the existing `IEffectiveAssignmentPolicyResolver`, with both `ToFields` mappings gaining the field (`AssignmentPolicyResolver.cs:138`/`:148`). The built-in default stays **null** — that is what makes "fail-open never invents a window" true.
3. **Q7 range rule** at both write paths: non-positive ⇒ **400**; a stored non-positive value is treated as **no window** by consumers (the `MaxNotifications` defensive posture).
4. Derived `WindowExpiresAt = SubmittedAt + effective days` (null when either side is null) on the read DTOs, resolved through the **existing per-(tenant, grade) effective-policy cache** — the same one that serves `RequiresApproval`, so enforcement stays fresh on write paths. A fail-open resolve must **never invent** a window.

**WS4 — sweeps.**
1. New `ApprovalSweeper` (+ service) in the Assignments Worker, **mirroring the Worker-resident precedents** `OverdueSweeper`/`ReminderSweeper` (injected `TimeProvider`, `OverdueSweeper.cs:29`, `OverdueSweepService.cs:20`) rather than `ArchiveSweeper` — that one is an Api-resident **pure dispatch core** whose candidate reads live in the repository (`AssignmentRepository.cs:59-68`/`:74-84`). Candidate read: cross-tenant (`IgnoreQueryFilters(["Tenant"])`, `AsNoTracking`), **`ApprovalStatus == Pending` AND `Status == Draft`** — the status filter is load-bearing, because `Close()` lets a Pending row reach `Closed` (WS2 table), and auto-returning a row that can never be edited (`:297`) or resubmitted (`:694`) would be a trap. The read also **projects the grade id** (the reminder sweep's `PolicyGradeId` precedent, `AssignmentRepository.cs:95-99`), or a per-grade `ApprovalWindowDays` override would silently never apply in the sweep. Per-row explicit tenant context, per-candidate error isolation.
2. **The auto-return must be dispatchable — it is not, through the existing reject path.** `RejectAssignmentCommandHandler` carries the ar-20 principal guard (`:25-32`, `MissingTeacherPrincipalException` when the principal has no `teacher_id` — i.e. any Worker sweep under real auth), the domain `Reject` refuses `Guid.Empty` (`Assignment.cs:726`; `:706-709` is `Approve`'s identical guard), and the **Worker registers no `IAssignmentPolicyResolver`**. So this round: (a) adds a **sweep-safe `AutoReturnAssignmentCommand`** modelled on the dependency-free `ArchiveAssignmentCommandHandler` (`:15-18`) — no principal, no command-level policy resolve; (b) introduces a documented **system-approver sentinel** (`ApprovalSweeper.SystemApproverId`, a fixed non-empty constant, because the domain refuses `Guid.Empty` and identity wiring is the documented placeholder); (c) **relocates `AssignmentPolicyResolver` from `Assignments.Api/Services` to `Assignments.Core/Services`** — the only implementation is Api-resident and the Worker references ServiceDefaults + Core only, so "Worker-side wiring" is otherwise unexecutable; the `NotificationPolicyResolver`/`HttpNotificationTemplateResolver` split is the in-repo precedent, and it is registered in **both** hosts (Api `Program.cs:170-171`, Worker against its existing named clients at `:39-40`). A **Worker DI composition test** (WS7) proves the registration, because the sweeper tests inject fakes and would let a missing registration survive to runtime.
3. Rules (§3.4): window expired ⇒ auto-return via `AutoReturnAssignmentCommand` with reason *"approval window expired"*; **due date passed wins** when both apply, reason *"due date passed"*; null window ⇒ **skip**; `Draft` + due date passed ⇒ **warn-only** (no state change, no sweep action) — the badge half is WS6.
4. `ScheduledPublishSweeper` classifies `AssignmentApprovalRequiredException` as an **info-level "awaiting approval" skip** (counted, not error-spammed every 15 minutes); every other exception keeps today's error handling.

**WS5 — API.**
`POST /assignments/{id}/reject` gains optional `Reason`; **`POST /assignments/{id}/deny`** is new (optional `Reason`); read DTOs (`AssignmentSummaryDto` + detail) gain `ApprovalReason`, `SubmittedAt`, `WindowExpiresAt` — **every read handler must map them**, or they silently stay null (the `WindowExpiresAt` tests catch that); the policy endpoints (`/api/settings/assignment-policy`, `/grade-levels/{id}/assignment-policy`) bind the new field with the range rule. `TypedResults` throughout, in the existing `<Specialty>Routes` files. **`AssignmentsApiClient`** gains the deny call, a `Reason`-bearing reject, and the three read-DTO fields (`:363` is `SubmitForApprovalAsync`) — the UI cannot ship without that seam.

**WS6 — UI (four surfaces).**
1. `Detail.razor` (`:73-93`): **Deny** button; **Reject** opens a reason dialog (`DialogShellBase`, optional textarea, per `.github/skills/dialog-ui/`); the reason is surfaced to the author (chip + reason line); the window countdown badge renders **only while `ApprovalStatus == Pending`** (Reject/Deny/auto-return clear `SubmittedAt`, so a returned row can never show a stale, long-expired countdown).
2. `Index.razor` (`:337`, `:416-422`): approval-state badges incl. the expiring-window hint, the **"Awaiting approval" filter**, and the **§3.4 warn-only badge** — a *Draft whose due date has passed* shows the authoring warning without any state change.
3. Edit page (`@page "/assignments/{Id:guid}/edit"`): the **"Locked — pending approval"** banner rather than letting the author discover the 409.
4. **The grade-detail policy card**: `Students.Application/Components/Students/AssignmentPolicyEditor.razor` grows a row for `ApprovalWindowDays` (same editor-dialog pattern; spec §5 requires it — the first draft silently dropped it), tested in `tests/SchoolCollab.Admin.Tests.Unit/AssignmentPolicyEditorTests.cs`.

**WS7 — tests.** Domain state machine (all transitions, `Denied` terminal, **`SubmitForApproval` refused while `Pending`**, reason captured and `SubmittedAt` cleared on Reject/Deny) · **the hold, one test per row of the WS2 table** (held ⇒ typed exception; exempt ⇒ **the hold exception is not thrown** and the handler's own behaviour applies) + the 409 mapping + the duplicate exemption · policy field round-trip, 204 semantics, non-positive ⇒ 400 at **both** write paths, merge/`…FromOverride` · `WindowExpiresAt` derivation + cache + fail-open-never-invents · sweeper: window expiry, due-date-wins, **null-window skip**, **a grade-override fixture** (proving the projected grade id reaches the resolver), **`Status == Draft` exclusion** (a Closed+Pending row is untouched), per-candidate isolation, and the **auto-return path through `AutoReturnAssignmentCommand`** (proving it needs no principal) · **Worker DI composition** (the relocated resolver is registered and resolves) · `ScheduledPublishSweeper` classification (info-level vs error) · UI bUnit (badges/countdown **gated on Pending**, the **warn-only draft badge**, reason dialog optional, Deny, Index filter, lock banner, **the policy card's new row**) · tenancy: cross-tenant approve/reject/deny still **403**.

## Migrations (human-gated — the reason this round is attended)

| Database | Migration |
|---|---|
| assignments-db | `Assignment.SubmittedAt`, `Assignment.ApprovalReason` |
| settings-db | `TenantAssignmentPolicy.ApprovalWindowDays` |
| students-db | `GradeAssignmentPolicy.ApprovalWindowDays` |

All three follow `.github/copilot/rules/ef-migrations.md` (reversible, no data backfill beyond nulls).

## Explicitly NOT in this round

Author/approver **notification emails** (§7 — they land on the notification-templates mechanism once identity wiring resolves staff addresses) · a dedicated approver role (§3.7 defers it to the identity round) · auto-publish on approve (Q13) · revoking approval after publish / re-approval on post-approval edits (deliberately unsettled) · any new `AssignmentStatus` value (Q12) · the AppHost diagnosis and the browser pass (residuals).

**Plan-review history:** iteration 1 — **REWORK** (3 P1, 4 P2): the sweep's auto-return was not dispatchable (principal guard, `Guid.Empty` refusal, no Worker resolver); WS6 dropped spec §5's grade-detail policy card; "enumerate the folder" was a gap instead of a decision → all folded. Iteration 2 — **REWORK** (3 P1, 5 P2): `Close`'s exemption was wrongly justified (a Pending row *does* close, so the sweep's candidate read needs `Status == Draft`); the count was 30 not 31 and three reachable handlers were missing — including `SubmitAssignmentForApproval`, whose Draft-only guard let an author **re-stamp `SubmittedAt` and reset the window**; and "Worker-side resolver wiring" was unexecutable because the only resolver is Api-resident → folded (relocation to `Assignments.Core` + a Worker DI composition test). **The ≤2 bound is spent**; the round proceeds to the worker with no third gate run.

## Risks

- **The hold's coverage is the risk, not its mechanism.** A missed command is a silent hole an approver cannot see; hence one test per command rather than one test for "the hold works".
- **Three migrations across three contexts.** Each must be independently reversible, and `ApprovalWindowDays` must not be added to one policy mirror and forgotten in the other — the merge/`…FromOverride` tests are what catch that.
- **`Denied` is terminal.** A resubmit that slips through turns a terminal decision into a loop; the domain test must assert `SubmitForApproval` throws *and* that publish/schedule still refuse.
- **Sweep + window = clock-dependent logic.** The sweeper must use the existing `TimeProvider` seam (never `DateTimeOffset.UtcNow`) so the expiry tests are deterministic.

## Verification

`dotnet build SchoolCollab.slnx` 0 errors · `SchoolCollab.Assignments.Tests.Unit` · `SchoolCollab.Assignments.Api.Tests.Unit` · `SchoolCollab.Settings.Tests.Unit` · `SchoolCollab.Students.Tests.Unit` · `SchoolCollab.Admin.Tests.Unit` · `SchoolCollab.ArchitectureTests.Unit` · **MAUI head** (`-m:1 -p:BuildInParallel=false`, required when shared components change).

## Acceptance — ACCEPTED (2026-10-20, `deepseek-v4-pro:0813`)

Criterion-by-criterion over the working-tree diff + named tests; nothing accepted on narrative.

| Criterion | Evidence |
|---|---|
| WS1 domain | `Denied = 3` additive (`ApprovalStatus.cs:29`, DTO mirrors `ContractTypes.cs:30`), EF column stays bare int; `SubmittedAt`/`ApprovalReason` (+500 cap shared with the schema); `SubmitForApproval` stamps/clears and refuses Denied/Pending/Approved (`Assignment.cs:690-699`); `Reject`/`Deny` record the reason and clear `SubmittedAt` (`:720-771`); publish/schedule guards untouched, Denied refusal pinned |
| WS2 hold | the 8 HOLDs call `EnsureNotAwaitingApproval()` after load and map to 409 on every held route; the 22 exemptions assert **their own** behaviour (Publish/Schedule ⇒ `AssignmentApprovalRequiredException`, Close ⇒ Closed+Pending, Duplicate ⇒ fresh null draft); `TheHoldCall_SitsOnlyInTheEightHeldHandlers_AcrossTheWholeCommandsFolder` pins membership by folder scan |
| WS3 window | 7th policy member, both mirrors + configs + DTOs, both `ToFields` map it, merge + `…FromOverride`; Q7 non-positive ⇒ 400 at **both** seams, stored non-positive ⇒ no window via the single `ApprovalWindow.EffectiveDays`; built-in null; fail-open never invents; `WindowExpiresAt` through the existing per-(tenant, grade) cache |
| WS4 sweeps | candidate read `Pending && Draft` + `IgnoreQueryFilters(["Tenant"])` + `AssignmentPolicyScope.DeriveGrade`; due-date-wins / window-expired / null-window skip / per-candidate isolation; dependency-free `AutoReturnAssignmentCommand` + non-empty sentinel; resolver relocated to `Assignments.Core` and registered in **both** hosts with `WorkerApprovalPolicyCompositionTests`; `ScheduledPublishSweeper` classification pinned info-vs-error |
| WS5 API/client | `Reason` on reject with `JsonIgnore(WhenWritingNull)` ⇒ pre-round body byte-identical (mobile-pinned); new `/deny`; three read-DTO fields mapped in **both** handlers; both policy endpoints bind the field; `AssignmentsApiClient` deny + reason |
| WS6 UI (four surfaces) | Detail (Deny, reason dialog, reason line, countdown **gated on Pending**); Index (chips incl. Scheduled+Pending, expiring hint, "Awaiting approval" filter, **warn-only draft badge**, narrowed Draft actions); Edit lock banner; `AssignmentPolicyEditor` **seventh row**, both scopes |
| Migrations | `20261020104526_…`, `…532_…`, `…536_…` — additive-nullable, real `Down()`, snapshots updated, independently reversible, `approval_reason` `varchar(500)` matching the domain cap |
| Test honesty | every WS7 group discriminating; **one spec §8 contract has no test** — cross-tenant reject (see divergence); no vacuous test found |
| Hygiene | single arithmetic (`ApprovalWindow`) + single `NormalizeReason`; `TypedResults` where the file isn't documented legacy debt; no `documents/specs/**`, `AGENTS.md`, `Version=`; tenant isolation on new reads |

**Untested / undiscriminating:** cross-tenant **reject** (spec §8 row 3's reject leg) — no test; everything else discriminating.

**Deviation recorded:** `Deny`'s non-Pending guard throws `InvalidOperationException("Only pending assignments can be denied.")` (the Approve/Reject precedent, mapped to 400 by the deny route — no 500 path) rather than a new typed exception.

**Divergence recorded:** cross-tenant **reject** returns **404**, not the spec §8 "403": `RejectAssignmentCommandHandler` carries ar-20 but **no ar-24 tenant guard**, so a foreign-tenant id falls through the tenant query filter → `AssignmentNotFoundException` → 404, **before any mutation**. No isolation risk; disposition = **doc-layer spec-row correction + a route test** in the residuals round. (The worker's framing — "cross-tenant reject still succeeds" — was **false** and rejected at the diff gate.)

**UI evidence is bUnit-only (Q91a).** The browser pass over **B2b, B3b and this round's four surfaces**, plus the AppHost diagnosis, belong to the residuals round; nothing here implies browser verification.

**Residuals confirmed parked:** cross-tenant-reject spec correction + test · AppHost diagnosis · browser pass · `Admin.Shared` enum/lookup drift guards · `GetDefaultAsync` null-on-200 · per-candidate audit attribution · concurrent-create re-check · `Edit_RemovingAnActivityGroupEntry_…` flake.

**Gate trail:** plan gate REWORK ×2 (3 P1 + 4 P2, then 3 P1 + 5 P2 — the sweep's auto-return was undispatchable, the policy-card row was dropped, and the hold's coverage was declared rather than decided; then `Close`'s guard was misread, the handler count was wrong, and the Worker resolver wiring was unexecutable; bound spent) → worker → diff gate **PASS** (1 P2 = the recorded divergence; 8 deviations accepted) → acceptance **ACCEPTED**.

**Patterns to carry forward:** (1) an exemption is only real if its **guard** prevents the state — `Close` looked protected and wasn't, so the sweep's candidate read needed `Status == Draft`; (2) a spec's tenancy row can overclaim (reject is 404, not 403) — verify each verb, not the group; (3) a sweep cannot reuse a principal-bearing command — it needs a dependency-free path and a named sentinel.

## Verification (measured, canonical layout)

`dotnet build SchoolCollab.slnx` **0 errors** · Assignments **1380/0** · Assignments.Api **202/0** · Settings **635/0** · Students **635/0** · Admin **718/0** · Architecture **135/0** · MAUI head **0 errors**. Three migrations, human-gated.
