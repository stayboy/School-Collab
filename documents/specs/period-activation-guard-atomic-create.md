# Spec: Period Activation Guard & Atomic Period Create

> **Status:** Draft
> **Date:** 2026-08-31
> **Owner contexts:** `SchoolCollab.Students.Core` (Period entity, CQRS handlers),
> `SchoolCollab.Students.Api` (routes), `SchoolCollab.Students.Application` (Blazor UI).
> **Depends on:** `period-hierarchy-terms-semesters.md` (FR-H1–H12, shipped hierarchy),
> `active-period-per-tenancy.md` (FR-A1–A6).
> **Decisions locked (from planning, 2026-08-31):**
> 1. The activation guard is a **hard, always-on invariant** — no feature flag.
> 2. Sub-periods are created **atomically with their academic year** via an extended
>    create flow, not a post-create second step.

---

## 1. Goal

Add two guard-rails/enhancements to the Periods domain:

1. **Activation guard:** an academic year divided into Terms or Semesters cannot
   be activated until it has at least one sub-period (term/semester) that can be
   activated. Plain (`None`-division) academic years are unaffected.
2. **Atomic create with sub-periods:** when creating a top-level academic year
   with a Terms/Semesters division, the user can define its sub-periods in the
   same create form; the year and its sub-periods are persisted in a single
   atomic operation.

## 2. Context (what exists today)

| Concern | Today | File |
| --- | --- | --- |
| Hierarchy | `Period.Division` (`None/Terms/Semesters`) + `ParentPeriodId` | `Students.Core/Domain/Period.cs` |
| Activation | FR-H4a **optionally** auto-activates the earliest draft sub-period after a year activates | `CQRS/Periods/Commands/ActivatePeriod/ActivatePeriodHandler.cs` |
| Create | Single period per command; sub-periods created separately afterwards | `CQRS/Periods/Commands/CreatePeriod/`, `Pages/Periods/PeriodForm.razor` |
| Errors | `PeriodNotOpenException`, `PeriodOverlapException`, `PeriodContainmentException`, `PeriodFrameworkMismatchException` mapped to 422/400 in routes | `Students.Api/Endpoints/PeriodRoutes.cs` |

The gap: FR-H4a treats "year has no activatable sub-period" as a valid gap state
(zero Active sub-periods stays valid — FR-H4), but a Terms/Semesters year with
**zero draft sub-periods at activation time** activates with no term/semester to
attach Termly/Semester activity-group memberships to (activity-group FR-43),
forcing admins into a second, non-atomic create step.

## 3. Functional Requirements

> RFC 2119 keywords. IDs: `FR-G-N` (guard), `FR-C-N` (atomic create).

### 3.1 Activation guard

- **FR-G1** — Activating a **top-level** academic year (`ParentPeriodId == null`)
  whose `Division != None` MUST be rejected unless the year contains **at least
  one Draft sub-period** (i.e., a sub-period that `Activate()` can transition).
  Rejection MUST use a new `PeriodGuardException` with a message naming the year
  and the required action ("create and activate at least one Term/Semester").
- **FR-G2** — The guard MUST be evaluated **before any state mutation** in
  `ActivatePeriodHandler`: no prior-year completion, no sibling close, no
  `period.Activate()` may run when the guard fails (all-or-nothing).
- **FR-G3** — `None`-division top-level years MUST activate with no sub-periods
  (unchanged; FR-H4's "gap state is valid" still applies to `None` years and to
  post-activation flows).
- **FR-G4** — Sub-period activation MUST be unchanged (parent-must-be-Active
  rule, FR-H5). The FR-H4a auto-activation of the earliest sub-period remains a
  convenience **after** the guard passes.
- **FR-G5** — The API activate endpoint (`POST /periods/{id}/activate`) MUST map
  `PeriodGuardException` to **422** with the exception message.
- **FR-G6** — The Periods landing page (`Periods.razor`) MUST surface the guard
  failure as a clear message bar; where the sub-period list is already loaded,
  the Activate button for a Terms/Semesters year with zero Draft sub-periods
  SHOULD be disabled with an explanatory tooltip.

### 3.2 Atomic create with sub-periods

- **FR-C1** — `CreatePeriod` MUST accept an optional list of sub-period
  definitions (`Name`, `StartDate`, `EndDate`). It MAY be non-empty only when
  the period is a **top-level** year with `Division = Terms` or `Semesters`;
  otherwise the request MUST be rejected (`ArgumentException` / 400).
- **FR-C2** — Each sub-period definition MUST satisfy the existing invariants:
  division matches the year's division (FR-H1/H2 shape), range contained in the
  year's range (FR-H3), and no overlap with sibling definitions (§5.6). Any
  violation MUST reject the **whole** request.
- **FR-C3** — The year and all its sub-periods MUST be persisted **atomically**:
  one unit of work / single `SaveChanges`; a failure at any point leaves zero
  rows. All created periods start in `Draft` (no auto-activation on create).
- **FR-C4** — The create endpoint response MUST include the created year id (and
  the ids of created sub-periods) so the UI can navigate/refresh.
- **FR-C5** — `PeriodForm.razor` MUST render a **Sub-periods section** when the
  form creates a top-level year with Division `Terms`/`Semesters` (not shown for
  `None`, for sub-period create, or in edit mode). The section supports adding
  rows (name + start/end) and a helper that **auto-splits** the year range into
  2 equal spans. Client-side validation mirrors FR-C2 before submit.
- **FR-C6** — The edit flow (`UpdatePeriod`) and the standalone SubPeriods
  section MUST be unchanged.

## 4. Non-functional requirements

- **NFR-G1** — No schema/migration changes: guard and atomic create are handler/
  domain-level only.
- **NFR-G2** — Concurrency: guard evaluation and persistence stay within the
  existing per-command context; a concurrent sub-period delete between check and
  activate falls back to the auto-activation "no candidate" path (log + proceed)
  rather than a hard failure — the guard is a user-facing rail, not a race
  boundary.
- **NFR-C1** — Unit tests cover: guard pass (≥1 draft sub-period), guard fail
  (zero sub-periods, and zero *draft* sub-periods), `None`-division unaffected,
  partial-mutation-free failure; atomic create success, each rejection path, and
  the `None`-division-with-subperiods rejection.
- **NFR-C2** — `dotnet build` (0 errors) and `dotnet test` (0 failures) before
  the branch is PR-ready (repo pre-flight standard).

## 5. Acceptance criteria

| ID | Scenario | Expected |
| --- | --- | --- |
| AC-G1 | Activate Terms year with 1 draft term | 204; term auto-activated (FR-H4a) |
| AC-G2 | Activate Terms year with 0 sub-periods | 422 `PeriodGuardException`; year still Draft; no prior year closed |
| AC-G3 | Activate Terms year with only Completed sub-periods | 422 (no *draft* candidate) |
| AC-G4 | Activate None year, no sub-periods | 204 (unchanged) |
| AC-C1 | Create Terms year + 2 terms atomically | 201; 3 rows Draft in one save |
| AC-C2 | Create with one term overlapping a sibling | 422; zero rows persisted |
| AC-C3 | Create None year with sub-period list | 400 |
| AC-C4 | UI: Terms year create shows sub-period section; None does not | Section toggles with Division |

## 6. Implementation notes

- Guard check placed at the top of `ActivatePeriodHandler.HandleAsync`, after
  loading the period, using `repository.GetSubPeriodsAsync(period.Id, ...)`.
- Atomic create implemented in `CreatePeriodHandler`: create the year entity,
  then `Period.Create` each sub-period with the same division and the year's id
  as parent, and let the repository's single `AddAsync`/`SaveChanges` persist the
  object graph (existing overlap check extended to consider sibling definitions
  against each other).
- New exception `PeriodGuardException` in `Students.Core/Domain/Exceptions/`;
  mapped in `PeriodRoutes.cs` activate endpoint (422).
- `PeriodForm.razor` gains a sub-period rows collection + auto-split helper;
  `StudentsApiClient.CreatePeriodAsync` request DTO extended with the optional
  sub-period list.
