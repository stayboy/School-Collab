# Assignment approval workflow — feature spec

> **Status: adopted design, NOT implemented** — grill round 2 (2026-10-15), owner confirmed all
> recommendations. This file is the source of truth for the feature. Companion spec:
> `documents/specs/assignment-notification-templates.md` (separate round). **No round has been
> started** — the execution mode (Solo / Light / Full four-agent) is an open owner decision under the
> AGENTS.md menu.

## 1. Current state (findings, 2026-10-15)

An approval flow exists but is minimal (`documents/specs/assignment-policy.md` §3.2, D3):

- `ApprovalStatus` (`Pending`/`Approved`/`Rejected`, nullable on `Assignment`). `SubmitForApproval()` runs
  **only from `Draft`** and sets `ApprovalStatus = Pending` **while leaving `Status = Draft`** — there is
  **no edit hold**: `UpdateAssignmentCommandHandler` carries no approval guard, so an author can change
  content while an approver reviews it.
- `Approve`/`Reject` run only from `Pending`. `Reject` releases the row for editing + resubmit — i.e.
  today's `Rejected` already behaves as "sent back for redo"; there is **no terminal denial** and **no
  reason field** (`RejectAssignmentRequest(Guid ApproverId)`).
- **No approval window** — a Pending row can sit forever; nothing sweeps it.
- **No expiry handling** for unpublished rows: nothing happens when a due date passes on a Draft/Pending
  assignment, and a `Scheduled` assignment that reaches its publish window without approval makes the
  15-minute sweep throw `AssignmentApprovalRequiredException` **every tick, error-logged, forever**
  (reachable only if the approval policy turns on after scheduling — schedule-time enforcement blocks the
  normal path).
- Enforcement: effective `RequiresApprovalBeforePublish` **OR** `FEATURE:RequireAssignmentApproval`
  refuses publish and schedule unless `ApprovalStatus == Approved`.
- UI: approval panel on `Detail.razor` (chip + Submit/Approve/Reject buttons), gated on the server-derived
  `RequiresApproval`. The approver seam is a documented placeholder (identity wiring pending); ar-24 added
  a tenant guard (foreign-tenant approver ⇒ 403).

## 2. Settled decisions (grill record, round 2 — all "as recommended")

| # | Decision |
|---|---|
| Q9 | **Pending-approval edit hold**: while `ApprovalStatus == Pending`, authoring write paths refuse with a typed exception (→ 409); the hold releases only on Approve / Reject / Deny. **Duplicate stays allowed** (fresh Draft, `ApprovalStatus = null`). |
| Q10 | **Denial vs sent-back-for-redo**: `Rejected` = sent back for redo (editable, resubmit — today's semantics); new terminal **`Denied`** = denial (never publishable from this row; duplicate is the escape hatch). Both capture an optional free-form **reason**, stored and shown to the author. |
| Q11 | **Approval window**: new policy field `ApprovalWindowDays` (tenant + grade override; **built-in default null = no deadline** — a failed resolve can never invent one) + a `SubmittedAt` column. A Worker sweep auto-**returns the row for redo** at window expiry (reason "approval window expired"); expiring-window badge on Index/Detail; **no auto-deny**; **no author email in v1** (teacher emails are not resolvable — the identity placeholder seam). |
| Q12 | **Expired / overdue-before-approval**: Pending + due date passed ⇒ auto-return for redo ("due date passed"); Draft + due date passed ⇒ **warn-only** (authoring badge; the author's private workspace never auto-destructs); Scheduled + publish window reached while unapproved ⇒ the sweep classifies `AssignmentApprovalRequiredException` as an **info-level "awaiting approval" skip** (no error spam) + an **"Awaiting approval" Index filter**. No new `AssignmentStatus` value. |
| Q13 | **Approve ≠ auto-publish**: approval releases the gate; the author then explicitly Publishes or Schedules. When the approval policy is off, nothing applies — publish directly; the existing lifecycle (Published → Closed → auto-archive on `DueDate + ArchiveGraceDays`) is unchanged. |
| Q14 | **Approver posture**: any authenticated staff member of the tenant (today's posture + the ar-24 tenant guard); a dedicated approver role (dept head / principal) is deferred to the identity-wiring round. |

## 3. Design

### 3.1 Approval states + reason

- `ApprovalStatus` gains **`Denied = 3`** (additive; `ApprovalStatusDto` mirrors; storage form — int vs
  name — verified against the EF configuration in the round plan).
- New `Assignment` columns: **`SubmittedAt`** (`DateTimeOffset?`, stamped by `SubmitForApproval`) and
  **`ApprovalReason`** (`string?`).
- Domain transitions:
  - `SubmitForApproval()` — stamps `SubmittedAt`; **refuses when `Denied`** (terminal — no resubmit);
    clears `ApprovalReason` on a fresh submission.
  - `Reject(approverId, reason?)` — Pending → `Rejected` (redo): editable, resubmit allowed; records the reason.
  - **`Deny(approverId, reason?)`** — Pending → `Denied` (terminal): releases the edit hold, never
    publishable from this row; records the reason.
  - `Approve(approverId)` — unchanged.
- Publish/Schedule guards are **unchanged** — the existing `ApprovalStatus != Approved` check already
  blocks `Denied` and `Pending` alike.

### 3.2 Pending edit hold

Every authoring write path (assignment update, questions confirm/materialize, attachments) refuses while
`ApprovalStatus == Pending` with a new typed `AssignmentPendingApprovalException` (the API maps it to
**409**). Released by Approve / Reject / Deny. `DuplicateAssignment` is explicitly exempt (a copy is a new
Draft with `ApprovalStatus = null` — it never bypasses the gate). UI: the Edit page shows a
"Locked — pending approval" banner instead of relying on users discovering the 409.

### 3.3 Approval window policy field

`AssignmentPolicyFields` (shared shape, `SchoolCollab.Core/AssignmentPolicies/`) gains
**`ApprovalWindowDays` (`int?`)** — mirrored as a nullable column by `TenantAssignmentPolicy` (settings-db)
and `GradeAssignmentPolicy` (students-db), resolved by the existing
`IEffectiveAssignmentPolicyResolver` (grade ?? tenant ?? built-in, null default). The **Q7 range rule**
applies verbatim: non-positive ⇒ 400 at both write paths; a stored non-positive value is treated as
**no window** by the consumers (defensive, the `MaxNotifications` posture).

Derived projection: `WindowExpiresAt = SubmittedAt + effective days` (null when either side is null) —
carried on the read DTOs, resolved via the existing per-(tenant, grade) effective-policy cache (the same
cache that serves `RequiresApproval`, so enforcement stays fresh on write paths). UI: countdown badge on
Detail, hint on Index rows.

### 3.4 Expiry rules (no new lifecycle status)

| Trigger | Outcome |
|---|---|
| `Pending` **and** window expired (`SubmittedAt + window ≤ now`) | auto-return for redo via the existing `Reject` path, reason **"approval window expired"** |
| `Pending` **and** due date passed | auto-return for redo, reason **"due date passed"** (wins when both apply — an approved-but-overdue assignment is pointless) |
| `Draft` + due date passed | **warn-only**: authoring badge; no state change, no sweep action |
| `Scheduled` + publish window reached while unapproved | no state change — §3.5 posture |

### 3.5 Scheduled-unapproved posture

`ScheduledPublishSweeper` classifies `AssignmentApprovalRequiredException` specifically: **info-level**
"awaiting approval" skip (counted, not error-spamming the log every 15 minutes); every other exception
keeps today's error handling. The row stays `Scheduled`; the Assignments Index gains an
**"Awaiting approval" filter** so blocked rows are visible instead of silent.

### 3.6 UI surfaces

- `Detail.razor` approval panel: **Deny** button; **Reject** gains a reason dialog (`DialogShellBase`
  dialog, optional reason textarea — the dialog conventions in `.github/skills/dialog-ui/`); the reason is
  surfaced to the author (Rejected/Denied chip + reason line); window countdown when set.
- `Index.razor`: approval-state badges incl. the expiring-window hint; the "Awaiting approval" filter.
- Edit page: the §3.2 lock banner.

### 3.7 Approver posture

Unchanged for v1: any authenticated staff member of the tenant may approve/reject/deny (the ar-24 tenant
guard keeps foreign-tenant actors at 403). A dedicated approver role is deferred to the identity-wiring
round, per Q14.

## 4. Data model + migrations

| Database | Migration |
|---|---|
| assignments-db | `Assignment.SubmittedAt`, `Assignment.ApprovalReason` |
| settings-db | `TenantAssignmentPolicy.ApprovalWindowDays` |
| students-db | `GradeAssignmentPolicy.ApprovalWindowDays` |

All three follow `.github/copilot/rules/ef-migrations.md`.

## 5. API surface

- `POST /assignments/{id}/approve` — unchanged.
- `POST /assignments/{id}/reject` — request gains optional `Reason`.
- **`POST /assignments/{id}/deny`** — new; optional `Reason`.
- Read DTOs (`AssignmentSummaryDto` / detail) gain `ApprovalReason`, `SubmittedAt`, `WindowExpiresAt`
  (derived); the policy endpoints (`GET/PUT /api/settings/assignment-policy`,
  `GET/PUT /grade-levels/{id}/assignment-policy`) bind the **new** field with the Q7 range rule (non-positive ⇒ 400).
  Field-count note (2026-10-18): `AssignmentPolicyFields` has grown since this spec was written — it now
  carries six members (`SignatureRequirement`, `RequiresApprovalBeforePublish`, `MaxPrimaryContacts`,
  `MaxCopyContacts`, `MandatoryReview`, `ArchiveGraceDays`), so `ApprovalWindowDays` is the **seventh**,
  not the fifth.
- The grade-detail Assignment Policy card (`AssignmentPolicyEditor`) grows a row for the new field, same editor dialog
  pattern (§4 of `assignment-policy.md`).

## 6. Sweeps

A new **approval sweep** in the Assignments Worker (the E3 shape): cross-tenant candidate read
(`IgnoreQueryFilters(["Tenant"])`, `AsNoTracking`, `ApprovalStatus == Pending`), per-row explicit tenant
context, resolve the effective window, apply §3.4 (auto-return via the existing reject command path),
per-candidate error isolation — mirroring `ScheduledPublishSweeper`/`ArchiveSweeper`. The
`ScheduledPublishSweeper` classification change is §3.5.

## 7. Out of scope (v1)

- **Author/approver notification emails** (window expiry, submission, decision) — the approver/identity
  seam is still a placeholder; these land on the notification-templates mechanism (companion spec) in a
  later round, once identity wiring makes staff emails resolvable.
- A dedicated approver role model (identity round).
- Auto-publish on approve (Q13 — publish stays the author's explicit act).
- Revoking approval after publish; re-approval on edit-after-approval (a re-submission requirement on
  post-approval edits is a follow-up question, deliberately not settled here).
- Any new `AssignmentStatus` value (Q12 — none).

## 8. Verification surface

| Contract | Discriminating test |
|---|---|
| State machine: Pending→Rejected/Denied/Approved; Denied terminal (submit refused, publish blocked); reason captured + cleared on resubmit | domain unit tests |
| Edit hold: every authoring path refuses while Pending (409 mapping); duplicate exempt | handler + route tests |
| Window policy: field round-trips + 204 semantics + non-positive ⇒ 400 both write paths (Q7 rule); merge + `…FromOverride` flags | the `assignment-policy.md` §6 suites, extended |
| `WindowExpiresAt` derivation + per-(tenant, grade) cache; fail-open resolve never invents a window | projection tests |
| Sweep auto-return: window expiry + due-date reasons; due-date wins; null window ⇒ skip; per-candidate isolation | sweeper core tests |
| Scheduled-unapproved: info-level skip classification; other exceptions still error | sweeper tests |
| UI: badges/countdown, reason dialog (optional reason), Deny button, Index filter, lock banner | bUnit suites (`Detail`/`Index`/Edit), MTP |
| Tenancy: cross-tenant approve/reject/deny stays 403 (ar-24 posture intact) | route tests |

## 9. Execution plan (slicing)

Second of the two rounds, **after** `assignment-notification-templates` (companion spec) merges —
sequential on `main`, each its own branch + PR + round doc per the merge policy; not a stacked train.
Independence note: nothing in this spec depends on the templates round technically; the ordering is
preference (the templates round is additive config; this round touches publish-path guards and the domain
state machine and benefits from a quiet tree).

**Recommended mode: full four-agent round (Tier 3)** — behavioural domain + state-machine changes, three
migrations across three bounded contexts, and UI surfaces (badges, filters, reason dialogs) that fire the
skill's deterministic UI trigger. Attended. **Open item:** the owner picks the actual execution mode (and
any unattended modifier) when starting the round — AGENTS.md feature menu; nothing has been started.
Per the unattended rules, this round contains **migrations**, which always reach a human unless the owner
pre-authorises the round by name.