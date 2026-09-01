# Spec: Draft Period Delete

> **Status:** Draft
> **Date:** 2026-08-31
> **Owner contexts:** `SchoolCollab.Students.Core` (Period entity, CQRS handlers),
> `SchoolCollab.Students.Api` (routes), `SchoolCollab.Students.Application` (Blazor UI).
> **Depends on:** `period-activation-guard-atomic-create.md` (shipped guard +
> atomic create), `period-hierarchy-terms-semesters.md` (FR-H1–H12), `active-period-per-tenancy.md` (FR-A1–A6).

---

## 1. Goal

Periods currently have **no delete action anywhere** — once created, a period
lives forever, even when it was created by mistake and never activated. This
spec adds a delete affordance for **Draft** periods only:

1. A Draft period (top-level academic year or sub-period) MUST be deletable
   by its tenant.
2. Deleting a Draft academic year MUST also delete its draft sub-periods
   (cascade), since a year without sub-periods cannot be activated and orphaned
   drafts have no purpose.
3. Non-draft periods (Active / Completed / Archived) MUST NOT be deletable.
   Their lifecycle continues through Complete → Archive, and they are
   referenced by operational data (memberships, assignments, audit entries).

## 2. Context (what exists today)

| Concern | Today | File |
| --- | --- | --- |
| Period lifecycle | `Draft → Active → Completed → Archived`; only Drafts can be updated/activated; no delete path | `Students.Core/Domain/Period.cs` |
| Create | Single or atomic-with-sub-periods create (FR-C5) | `CQRS/Periods/Commands/CreatePeriod/` |
| Delete precedent | Soft delete (`IsDeleted`/`DeletedAt`) + recover + `ListDeleted` for Students | `CQRS/Students/Commands/DeleteStudent/` |
| UI | Periods landing grid kebab/RowActions; guarded Activate for Draft years | `Pages/Periods/Periods.razor` |
| Client | `StudentsApiClient` per-command wrappers | `Services/StudentsApiClient.cs` |

**Decisions locked (from planning, 2026-08-31):**

1. **Hard delete, Draft-only.** Drafts have no referencing operational data by
   definition (nothing can attach to a period that was never active), so a
   physical delete is safe and keeps the grid clean without a
   "deleted periods" view. This differs from Students (soft delete) because
   Students accumulate audit history from day one; Draft periods do not.
2. **Cascade to draft descendants.** Deleting a Draft year deletes its Draft
   sub-periods in the same command (single transaction, same atomic semantics
   as FR-C5's create).
3. **No feature flag** — the same reasoning as the activation guard: a period
   that cannot be deleted is a data-hygiene defect, not a tenant preference.

## 3. Functional Requirements

- **FR-D1** — The system MUST support deleting a Period that is in `Draft`
  status via a new `DeletePeriod` command (`ICommand`) and handler.
- **FR-D2** — Deleting a period that is NOT in `Draft` status MUST be rejected
  with a clear domain error via a dedicated exception
  (`PeriodNotDeletableException`), following the `PeriodGuardException`
  precedent of a named domain exception mapped to 422 in the routes — not the
  domain's internal `InvalidOperationException` style.
- **FR-D3** — When the deleted period is a top-level academic year, its
  sub-periods MUST be deleted in the same transaction. A sub-period that is
  NOT in `Draft` status MUST abort the whole deletion before any removal (no
  partial cascade) — an Active/Completed/Archived sub-period proves the year
  is in use. The handler MUST rely on the **already-declared** EF cascade
  (`PeriodConfiguration` maps the self-referencing `ParentPeriodId` FK as
  `OnDelete(DeleteBehavior.Cascade)`): guard first, then a single
  `Remove(year)` — do NOT re-implement per-row sub-period removal in the
  handler or repository.
- **FR-D4** — A Draft sub-period MAY be deleted individually (without touching
  its parent year).
- **FR-D5** — The delete MUST be tenant-scoped: only the owning tenant's
  periods are deletable, enforced by the existing tenancy query filters.
- **FR-D6** — Defensive housekeeping (SHOULD, not MUST): after deletion, any
  surviving **Draft** period whose `NextPeriodId` points at a deleted period
  SHOULD be nulled. Rationale: `SetNextPeriod` currently has **no production
  call sites** (no handler ever sets `NextPeriodId`), and `PeriodConfiguration`
  maps `NextPeriodId` as a plain property with **no FK constraint**, so a
  dangling link can neither occur today nor violate the database — this is
  purely future-proofing if chain-linking is ever wired up.
- **FR-D7** — A domain event (`PeriodDeletedEvent`) SHOULD be emitted on
  successful deletion for observability parity with `PeriodCompletedEvent`.
- **FR-D8** — The API MUST expose `DELETE /students/periods/{id}` (grouped via
  the existing `PeriodRoutes` extension-method pattern), returning 204 on
  success, 404 when the period does not exist (or belongs to another tenant),
  and 422 on FR-D2/FR-D3 violations.
- **FR-D9** — The Periods landing grid MUST offer a **Delete** action on rows
  whose status is `Draft`, with a confirmation step naming the period and —
  for a year — the number of draft sub-periods that will be removed with it.
- **FR-D10** — The edit page (`Edit.razor`) SHOULD surface the same delete
  action for Draft periods (danger-zone placement), sharing the confirmation
  wording with the grid.
- **FR-D11** — `StudentsApiClient` MUST gain a `DeletePeriodAsync(Guid id)`
  wrapper that maps the 422 message body to the existing error-surfacing
  pattern used by `ActivatePeriodAsync`.
- **FR-D12** — Sub-period delete affordance lives on the sub-period rows of the
  **edit page's `SubPeriodsSection`** (the current sub-period management
  surface, alongside create): each Draft sub-period row MUST offer Delete with
  the same confirmation wording as the grid. The legacy
  `SubPeriodsListDialog.razor` is NOT extended — sub-period management moved
  onto the edit/create period pages, so the dialog is superseded (its removal
  is a separate cleanup, see Out of Scope).

## 4. Non-Functional Requirements

- **NFR-D1** — The delete (including cascade) MUST complete in a single
  transaction; a failure MUST leave zero partial deletions.
- **NFR-D2** — Optimistic concurrency (`xmin` row version) semantics MUST be
  preserved: deleting an already-deleted period returns 404, not an exception.
- **NFR-D3** — The grid Delete action MUST be keyboard-reachable (not a
  disabled-button tooltip; this addresses the keyboard-inaccessibility finding
  from the r1 UI-tester round).

## 5. Acceptance Criteria

- **AC-D1** (FR-D1, FR-D2) — *Given* an Active period, *when* `DeletePeriod`
  is handled, *then* it throws and the period row is unchanged.
- **AC-D2** (FR-D1, FR-D3) — *Given* a Draft year with 2 Draft sub-periods,
  *when* the year is deleted, *then* all three rows are gone in one transaction.
- **AC-D3** (FR-D3) — *Given* a Draft year with 1 Draft + 1 Active sub-period,
  *when* the year is deleted, *then* nothing is deleted and the error names the
  blocking sub-period.
- **AC-D4** (FR-D4) — *Given* a Draft sub-period, *when* deleted, *then* only
  that row is gone and the parent year remains.
- **AC-D5** (FR-D5) — *Given* another tenant's Draft period id, *when* deleted,
  *then* the API responds 404 and no row changes.
- **AC-D6** (FR-D6) — *Given* a Draft B whose `NextPeriodId` points at deleted
  Draft A (only constructible in tests today — no handler sets links), *when* A
  is deleted, *then* B.`NextPeriodId` is null afterwards.
- **AC-D7** (FR-D8) — *Given* a valid Draft id, *when* `DELETE` is called,
  *then* the response is 204; repeating the same call returns 404.
- **AC-D8** (FR-D9) — *Given* the landing grid, *when* a row's status is
  `Draft`, *then* a Delete action is present and asks for confirmation; *when*
  the status is not Draft, *then* no Delete action is offered.
- **AC-D9** (FR-D9, NFR-D3) — *Given* keyboard-only navigation, *when* focusing
  a Draft row's actions, *then* the Delete affordance is reachable and
  announceable.
- **AC-D10** (FR-D12) — *Given* an academic year's edit page, *when* its
  `SubPeriodsSection` renders a Draft sub-period row, *then* the row offers a
  Delete action with the same confirmation wording as the grid; *when* the
  sub-period is not Draft, *then* no Delete action is offered on that row.

## 6. Edge Cases

- **EC-1** — Delete the only Draft year in a tenant with no other periods:
  allowed; tenant simply has no periods again.
- **EC-2** — Draft year pre-linked as `NextPeriodId` of a non-Draft period
  (hypothetical today — no handler sets `NextPeriodId`): deleting the Draft year
  MUST NOT alter the other period's record, and it is safe precisely because
  `NextPeriodId` carries **no FK constraint** (a dangling link cannot violate
  the database). FR-D6's cleanup applies only to surviving **Draft** links;
  links from non-Draft periods are historical records and stay untouched.
- **EC-3** — Concurrent edit + delete: optimistic-concurrency failure resolves
  to 404 per NFR-D2 (re-delete or delete-after-delete is idempotent 404; these
  routes do not use 409); the UI shows the standard error bar.
- **EC-4** — The `?parent=` create flow pointing at a deleted year: parent
  select is rebuilt from the API on page load, so a stale link naturally 404s;
  no special handling beyond FR-D8's 404 contract.
- **EC-5** — Sub-period deletion while the parent year's atomic-create section
  is open in another tab: last-writer wins via row version; no server-side
  locking beyond the existing pattern.

## 7. API Contracts

```ts
// DELETE /students/periods/{id}
// 204 No Content                       — deleted (incl. cascade)
// 404 Not Found                        — unknown id, other tenant, or already deleted
// 422 Unprocessable Entity             — { "message": string }  (FR-D2/FR-D3 violations)

// Client wrapper
deletePeriodAsync(id: Guid): Promise<void>   // throws HttpRequestException with server message on 422
```

## 8. Data Models

No schema changes. `Period` gains a `Delete()` domain method (Draft-only guard
+ `PeriodDeletedEvent`). No new repository method: the handler runs the
all-Draft sub-period guard (FR-D3), then issues a single `Remove(year)` —
sub-period rows go with it via the **existing**
`OnDelete(DeleteBehavior.Cascade)` declared in `PeriodConfiguration` for the
self-referencing `ParentPeriodId` FK. No new tables, no migrations.

## 9. Out of Scope

| Exclusion | Reason |
| --- | --- |
| Soft delete / recycle bin for periods | Drafts carry no history; a recycle bin adds surface with no user story (decision 1) |
| Bulk delete | No demand signal; single-row delete first |
| Deleting Active/Completed/Archived periods | They are referenced by memberships, assignments, and audit entries; lifecycle is Complete → Archive |
| Recovery of `NextPeriodId` links into non-Draft periods | EC-2 decision: historical records stay untouched |
| Cross-tenant delete or admin override | Tenancy model has no cross-tenant operations |
| Extending or maintaining `SubPeriodsListDialog.razor` | Superseded — sub-period management lives on the edit/create period pages (FR-D12); deleting the unused dialog is a separate cleanup PR, not part of this feature |

