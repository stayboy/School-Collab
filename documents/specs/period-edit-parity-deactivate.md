# Spec Addendum: Edit/Create Form Parity + Period Deactivation

> **Status:** Draft (round r2 source of truth)
> **Date:** 2026-08-31
> **Owner contexts:** `SchoolCollab.Students.Core` (Period entity, lifecycle, CQRS), `SchoolCollab.Students.Api` (routes), `SchoolCollab.Students.Application` (Blazor UI).
> **Builds on:** `period-draft-delete.md` (r1, shipped), `period-hierarchy-terms-semesters.md` (FR-H1–H12, overlap), `active-period-per-tenancy.md` (FR-A1–A6), `period-activation-guard-atomic-create.md`.

---

## 1. Goal

Three changes to period management, plus a UI-architecture correction:

1. **Division immutable on edit.** `AcademicYearDivision` is set at create and cannot be changed on an existing period.
2. **Edit ↔ create form parity, Auto-split visible on edit.** The edit form renders the same field set + sub-periods section as the create form, including the Auto-split button.
3. **New `Deactivated` state (overlap relief).** A new `PeriodStatus.Deactivated` lets an Active period that cannot be deleted be deactivated so a corrected new period can be created in its freed date range. Only Active periods can be deactivated.
4. **`PeriodForm` wrapper elimination (Topic pattern).** Delete the `PeriodForm.razor` wrapper; `PeriodFormFields.razor` is the shared field-rows component; the owning `<EditForm>` + submit + save/load logic lives in the consuming pages (`Create.razor`, `Edit.razor`), matching the `TopicFormFields` pattern (owning form supplied by the dialog/page, no `*Form` wrapper).

## 2. Functional Requirements

### Division immutability
- **FR-E1** — `Period.Update` MUST NOT change `Division`. The `UpdatePeriod` command MUST drop the `Division` parameter; the handler MUST NOT accept or persist a division change.
- **FR-E2** — The edit form's Division field MUST be visible (same look as create) but disabled when editing an existing period.

### Edit ↔ create form parity
- **FR-E3** — The edit form and create form MUST render the same field set (`PeriodFormFields`: Division → Parent → Name → Dates) and the same sub-periods section.
- **FR-E4** — The Auto-split button MUST be visible on the edit form for a top-level Terms/Semesters year, identical to create.
- **FR-E5** — Auto-split on edit: enabled only when the year has a valid date range AND all existing sub-periods are Draft (or none exist). When non-Draft sub-periods exist, Auto-split MUST be disabled with an explanatory tooltip. Replacing existing Draft sub-periods MUST require a confirmation naming the count.
- **FR-E6** — Suggest/Backfill helper buttons are **create-only** (Edit.razor passes no `NameActions`).
- **FR-E7** — A single shared sub-periods component (sub-period list + add/edit/delete + Auto-split) is consumed by both create and edit. The separate `SubPeriodsSection.razor` is retired after grep-confirmation of no other callers.

### Deactivated state
- **FR-X1** — `PeriodStatus.Deactivated` is a new lifecycle value. `Period.Deactivate()` transitions `Active → Deactivated` only; any other status throws `PeriodNotDeactivatableException`.
- **FR-X2** — Deactivating a top-level academic year MUST cascade: its Active sub-periods are deactivated in the same transaction (no Active orphans under a Deactivated parent). Guard first, then mutate all, then one `SaveChanges`.
- **FR-X3** — `GetOverlappingPeriodsAsync` MUST exclude `Deactivated` periods from the no-overlap check. `Completed` and `Archived` periods still block overlap (only Deactivated is excluded).
- **FR-X4** — Deactivated periods are NOT deletable (Draft-only delete unchanged).
- **FR-X5** — `Deactivated → Archived` is allowed (cleanup path); the grid exposes an Archive action on Deactivated rows. `Deactivated → Active` (re-activation) is **out of scope**.
- **FR-X6** — A `PeriodDeactivatedEvent` domain event is emitted (observability parity with `PeriodCompletedEvent`). No integration/outbox event this round.
- **FR-X7** — `POST /students/periods/{id}/deactivate` returns 204 on success, 404 on unknown/other-tenant/already-gone (concurrency → 404, never 409), 422 on `PeriodNotDeactivatableException` (non-Active).
- **FR-X8** — `StudentsApiClient.DeactivatePeriodAsync(Guid id)` maps 422 to the existing error-surfacing pattern.
- **FR-X9** — The grid and edit page expose a Deactivate action on Active periods (confirmation wording shared via `PeriodDeactivatePrompts`). Deactivated rows show no Delete/Activate/Complete/Deactivate; they show Archive.
- **FR-X10** — Tenant scoping: only the owning tenant's periods are deactivatable (existing query filters).

## 3. Non-Functional Requirements

- **NFR-E1** — Deactivate (including cascade) completes in a single transaction; a failure leaves zero partial changes.
- **NFR-E2** — Optimistic concurrency (`xmin`): deactivating an already-deleted/gone period returns 404, not an exception (DbUpdateConcurrencyException → ConcurrencyException → 404, never 409).
- **NFR-E3** — All new actions are keyboard-reachable (grid kebab trigger is a tab stop; menu items announce).

## 4. Acceptance Criteria

- **AC-E1** (FR-E1/E2) — Given an existing year, when edited, then Division is disabled and unchanged on save.
- **AC-E2** (FR-E3/E4) — Given a top-level Terms year edit page, then the sub-periods section + Auto-split button render identically to create.
- **AC-E3** (FR-E5) — Given a year with an Active sub-period, then Auto-split is disabled with a tooltip; given all-Draft subs, Auto-split is enabled and confirms before replacing.
- **AC-E4** (FR-X1) — Given an Active period, when deactivated, then status is Deactivated; given a non-Active period, then 422 and unchanged.
- **AC-E5** (FR-X2) — Given an Active year with Active sub-periods, when deactivated, then the year + all Active sub-periods are Deactivated in one transaction.
- **AC-E6** (FR-X3) — Given a Deactivated period occupying a date range, when a new period is created in that range, then it succeeds (no overlap rejection).
- **AC-E7** (FR-X5) — Given a Deactivated row, an Archive action is present; Delete/Activate/Complete/Deactivate are absent.
- **AC-E8** (FR-X7) — Given a valid Active id, `POST /deactivate` returns 204; repeating returns 422 (already Deactivated, no idempotent early-return); unknown/other-tenant returns 404.
- **AC-E9** (FR-X10) — Given another tenant's Active period id, `POST /deactivate` returns 404 and no row changes.
- **AC-E10** (FR-E7) — The `PeriodForm` wrapper no longer exists; `PeriodFormFields` is the shared field-rows component; Create.razor and Edit.razor own their `<EditForm>` + submit + save/load.

## 5. Edge Cases

- **EC-E1** — Deactivating the active academic year pauses enrollment: `EnrollStudentHandler` requires an active period, so enrollment cannot proceed until a corrected period is activated. This is the **intended** side effect (the user is correcting entries). Documented in the spec, not a defect.
- **EC-E2** — Concurrent edit + deactivate: optimistic-concurrency failure resolves to 404 (NFR-E2).
- **EC-E3** — Deactivating a sub-period individually (without its parent year) is allowed (Active sub-period → Deactivated). The parent year remains Active.
- **EC-E4** — A Deactivated period's `NextPeriodId` link (no handler sets it today) is untouched by deactivation (no FK constraint; FR-D6 housekeeping from r1 applies only to hard delete).

## 6. API Contracts

```ts
// POST /students/periods/{id}/deactivate
// 204 No Content        — deactivated (incl. cascade)
// 404 Not Found         — unknown id, other tenant, or already gone (concurrency)
// 422 Unprocessable     — { "message": string }  (non-Active)

// PUT /students/periods/{id}  (UpdatePeriod)
//   request body no longer carries `division` (immutable)

// Client wrapper
deactivatePeriodAsync(id: Guid): Promise<void>   // throws HttpRequestException with server message on 422
```

## 7. Data Models

No schema changes. `PeriodStatus` gains `Deactivated = 4` (enum stored as int → no migration). `Period` gains `Deactivate()` and `Archive()` is extended to allow `Deactivated → Archived`. New `PeriodDeactivatedEvent` record + `PeriodNotDeactivatableException` (mirrors `PeriodNotDeletableException`). `UpdatePeriod` command drops `Division`.

## 8. Out of Scope

| Exclusion | Reason |
| --- | --- |
| Re-activation of Deactivated periods (Deactivated → Active) | Not requested; Deactivated → Archived covers cleanup |
| Soft delete / recycle bin for periods | r1 decision (Drafts carry no history) |
| Bulk deactivate | No demand signal |
| Excluding Completed/Archived from overlap | Only Deactivated frees overlap (user decision) |
| Feature flag | Period hygiene, not a tenant preference |
| Integration/outbox event for deactivate | Domain event only this round |
| Migrations | Enum stored as int; new value needs no schema change |
| `SubPeriodsListDialog.razor` | r1 out-of-scope carryover (superseded) |

## 9. Reviewer r1 re-check (round r2 scope)

The r2 reviewer re-verifies the r1 delete implementation against `period-draft-delete.md` FR-D1..D12 / NFR-D1..D3 / AC-D1..D10 still holds after r2 edits — especially the `PeriodForm` elimination + sub-period unification moving r1's per-row Draft delete into the new shared component. Any r1 regression is a P1.